#!/usr/bin/env bash
#
# start-phone.sh — one-command dev startup: dotnet watch (hot reload) + Cloudflare quick tunnel.
# This is the ONLY supported way to run Agentic.Chat locally.
#
# Run from Git Bash on Windows:
#   bash start-phone.sh
#
# What it does:
#   1. Fails fast if OPENROUTER_API_KEY is not available (env or Windows User env var).
#      The key is never printed, logged, or persisted by this script.
#   2. Starts `dotnet watch run` on http://localhost:5123 with hot reload enabled
#      (browser auto-launch suppressed, --non-interactive so rude edits auto-restart).
#      Edits to Razor markup / C# method bodies propagate live to every connected browser
#      (local + phone) without a page reload. Rude edits (Program.cs, new .razor file)
#      restart the server; both browsers auto-reload — local via dotnet watch's refresh
#      signal, phone via the ReconnectModal `resume-failed` handler in Components/Layout/.
#   3. Starts `cloudflared tunnel --url http://localhost:5123`, detects the printed
#      https://....trycloudflare.com URL and displays it as the phone link.
#      The tunnel URL stays the SAME across dotnet watch restarts (rude edits), because
#      cloudflared is a separate process that keeps pointing at port 5123.
#   4. On Ctrl+C / termination, stops both processes (full process trees) so port 5123
#      is freed and the public URL stops serving.
#
# LOGGING (project-local, self-managed — no need to redirect to /tmp):
#   Every run creates logs/start-phone/<YYYY-MM-DD_HH-MM-SS>/ containing:
#     - script.log  — this script's full stdout/stderr (banner, status, errors)
#     - app.log     — dotnet watch verbose output (hot-reload flakiness signals live here)
#     - tunnel.log  — cloudflared output (URL provisioning, connection events)
#     - meta.json   — structured run summary (URLs, PIDs, timing, exit_reason)
#   logs/start-phone/LATEST is a plain-text marker with the most recent run's dir name.
#   The 10 most recent runs are kept; older ones are pruned. logs/ is already gitignored.
#
# NOTE: the tunnel URL changes every RUN, but stays stable WITHIN a run across rude edits.
# NOTE: keep this file at LF (.gitattributes enforces it).
#
# Caveats the user will actually hit:
#   - Hot reload can silently stall in .NET 10 GA (dotnet/sdk#51185). The --verbose log
#     surfaces "No hot reload changes to apply"; press Ctrl+R here to force a rebuild.
#   - Rude edits (Program.cs, new .razor file) reset in-memory chat state (ChatAgentService
#     is scoped). In-place edits preserve state. There is no persistence layer.
#   - The phone does not auto-refresh on in-place hot reloads (refresh signal is local
#     only), but it shares the same running server as the local browser, so it sees
#     component re-renders live. Only full restarts (rude edits) trigger a phone reload.

set -euo pipefail

APP_URL="http://localhost:5123"
APP_PORT=5123

# ---------------------------------------------------------------------------
# 0. Project-local log directory (one subdir per run, auto-rotated).
# ---------------------------------------------------------------------------
LOGS_ROOT="logs/start-phone"
RUN_ID="$(date +%Y-%m-%d_%H-%M-%S)"
LOG_DIR="$LOGS_ROOT/$RUN_ID"
mkdir -p "$LOG_DIR"
APP_LOG="$LOG_DIR/app.log"
TUNNEL_LOG="$LOG_DIR/tunnel.log"
SCRIPT_LOG="$LOG_DIR/script.log"
META_JSON="$LOG_DIR/meta.json"

# Plain-text marker (not symlink — Windows symlinks need admin/dev mode).
echo "$RUN_ID" > "$LOGS_ROOT/LATEST"

# Rotate: keep the 10 most recent run dirs.
# The regex '^[0-9]{4}-[0-9]{2}-[0-9]{2}_' matches ONLY timestamp-prefixed run dirs
# (format: YYYY-MM-DD_HH-MM-SS). It deliberately does NOT match:
#   - LATEST (the marker file — convention name, no extension; see above. Like LICENSE /
#     Makefile / .git/HEAD, the filename itself is the type indicator, not an extension.
#     An extension would misrepresent its purpose: .log implies log lines, .json implies
#     structured data. It's a bare string pointer to the current run dir name.)
#   - any future non-run files we may add under $LOGS_ROOT (README, format docs, etc.)
# So rotation is safe to run on every startup without risking the marker or aux files.
ls -1 "$LOGS_ROOT" 2>/dev/null | grep -E '^[0-9]{4}-[0-9]{2}-[0-9]{2}_' | sort -r | tail -n +11 2>/dev/null | while read -r old; do
  rm -rf "${LOGS_ROOT:?}/$old"
done

START_TIME="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
EXIT_REASON="unknown"
START_EPOCH=$SECONDS

APP_PID=""
TUNNEL_PID=""
PUBLIC_URL=""
SHUTTING_DOWN=""

info()  { printf '[start-phone] %s\n' "$*"; }
fail()  {
  EXIT_REASON="${1:-startup_error}"
  shift
  printf '[start-phone] ERROR: %s\n' "$*" >&2
  exit 1
}

# ---------------------------------------------------------------------------
# Helper functions — defined EARLY (before any check that can call fail()) so
# the EXIT trap can fire safely on every exit path: api_key_missing,
# prereq_missing, port_occupied, app_startup_failed, SIGTERM, normal exit.
# Under `set -euo pipefail`, calling an undefined function from inside a trap
# crashes the trap itself, so all helpers must exist before the trap is set.
# ---------------------------------------------------------------------------
listener_pid() {
  netstat -ano 2>/dev/null | grep 'LISTENING' | grep -E "[:.]$APP_PORT[[:space:]]" | awk '{print $NF}' | head -1
}

winpid_of() { cat "/proc/$1/winpid" 2>/dev/null || true; }

kill_tree() {
  local pid="$1"
  [[ -n "$pid" ]] || return 0
  local wpid; wpid="$(winpid_of "$pid")"
  if [[ -n "$wpid" ]]; then
    taskkill //PID "$wpid" //T //F >/dev/null 2>&1 || true
  fi
  kill "$pid" 2>/dev/null || true
}

write_meta() {
  local end_time; end_time="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  local duration=$(( SECONDS - START_EPOCH ))
  cat > "$META_JSON" <<EOF
{
  "run_id": "$RUN_ID",
  "started_at": "$START_TIME",
  "ended_at": "$end_time",
  "duration_seconds": $duration,
  "exit_reason": "${EXIT_REASON:-unknown}",
  "app_pid": ${APP_PID:-null},
  "tunnel_pid": ${TUNNEL_PID:-null},
  "local_url": "$APP_URL",
  "public_url": "${PUBLIC_URL:-}",
  "log_dir": "$LOG_DIR",
  "files": {
    "script": "script.log",
    "app": "app.log",
    "tunnel": "tunnel.log"
  }
}
EOF
}

cleanup() {
  trap - INT TERM EXIT
  SHUTTING_DOWN=1
  [[ -z "${EXIT_REASON:-}" || "$EXIT_REASON" == "unknown" ]] && EXIT_REASON="terminated"
  info "Shutting down app and tunnel (reason: $EXIT_REASON)..."
  kill_tree "$TUNNEL_PID"
  kill_tree "$APP_PID"
  sleep 2
  # Safety net: nothing may remain bound to the port. ONLY run if we actually
  # started the app (APP_PID is set). Without this guard, an early fail() path
  # (api_key_missing / prereq_missing / port_occupied) would taskkill a process
  # the script never started — e.g., the test's dummy listener, or in production
  # another developer's concurrent start-phone.sh instance.
  if [[ -n "$APP_PID" ]]; then
    local leftover; leftover="$(listener_pid || true)"
    if [[ -n "$leftover" ]]; then
      taskkill //PID "$leftover" //T //F >/dev/null 2>&1 || true
    fi
  fi
  write_meta
  info "Stopped. Port $APP_PORT freed; tunnel URL no longer serves."
  info "Run artifacts: $LOG_DIR  (script.log, app.log, tunnel.log, meta.json)"
  info "Latest run marker: $LOGS_ROOT/LATEST  (contains: $RUN_ID)"
}

# Send all subsequent stdout/stderr to BOTH the inherited destination (terminal,
# or wherever the caller redirected) AND $SCRIPT_LOG.
#
# TTY-conditional: when stdout is a real terminal (a human running this
# interactively), mirror to the terminal AND $SCRIPT_LOG via tee. When stdout is
# NOT a TTY — i.e. an agent backgrounded the script and stdout is the agent
# tool's capture pipe — write to $SCRIPT_LOG ONLY.
#
# Why the split: this script blocks in `wait` (see §8) for its entire lifetime.
# A `tee` here inherits and holds the caller's stdout pipe open for that whole
# lifetime, which hangs agent bash tools that block on pipe EOF (the tool call
# never returns). File-only output in the non-TTY case detaches that pipe so the
# tool returns promptly, and agents recover the full run narrative from
# $SCRIPT_LOG regardless. (Agents should still launch with nohup + </dev/null
# >/dev/null 2>&1 — see AGENTS.md "Run from an agent" — so the server also
# survives the agent shell session tearing down after the tool returns.)
if [ -t 1 ]; then
    exec > >(tee "$SCRIPT_LOG") 2>&1
else
    exec >"$SCRIPT_LOG" 2>&1
fi

# Register the EXIT trap NOW — after the exec redirect (so cleanup output flows
# to script.log) but BEFORE any check that can call fail() (api_key / prereq /
# port checks below). This guarantees meta.json is written on every exit path,
# including early fail() exits that previously missed cleanup entirely because
# the trap wasn't registered yet.
trap cleanup INT TERM EXIT

info "Run ID: $RUN_ID"
info "Log dir: $LOG_DIR"
info "Started at: $START_TIME"

# ---------------------------------------------------------------------------
# 1. OPENROUTER_API_KEY — must be present; never printed or persisted.
# ---------------------------------------------------------------------------
if [[ -z "${OPENROUTER_API_KEY:-}" ]]; then
  # Git Bash sessions started before the var was set won't inherit it; pull it
  # from the Windows User (then Machine) environment without displaying it.
  for scope in User Machine; do
    val="$(powershell -NoProfile -Command "[Environment]::GetEnvironmentVariable('OPENROUTER_API_KEY','$scope')" 2>/dev/null | tr -d '\r' || true)"
    if [[ -n "$val" ]]; then
      export OPENROUTER_API_KEY="$val"
      info "OPENROUTER_API_KEY loaded from Windows $scope environment (value not shown)."
      break
    fi
  done
fi
[[ -n "${OPENROUTER_API_KEY:-}" ]] || fail "api_key_missing" "OPENROUTER_API_KEY is not set. Set it as a Windows User environment variable (or export it) and re-run."
unset val

# ---------------------------------------------------------------------------
# 2. Prerequisites
# ---------------------------------------------------------------------------
command -v dotnet      >/dev/null 2>&1 || fail "prereq_missing" "dotnet not found on PATH. Install the .NET SDK and re-run."
command -v cloudflared >/dev/null 2>&1 || fail "prereq_missing" "cloudflared not found on PATH. Install cloudflared (https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/) and re-run."

# ---------------------------------------------------------------------------
# 3. Port check — REFUSE if occupied. dotnet watch owns 5123 for the whole session.
# ---------------------------------------------------------------------------
existing_pid="$(listener_pid || true)"
if [[ -n "$existing_pid" ]]; then
  pname="$(powershell -NoProfile -Command "(Get-Process -Id $existing_pid -ErrorAction SilentlyContinue).ProcessName" 2>/dev/null | tr -d '\r' || true)"
  fail "port_occupied" "Port $APP_PORT is already occupied by '$pname' (PID $existing_pid). start-phone.sh uses dotnet watch and must own the port for the whole session. Free it yourself (taskkill //PID $existing_pid //T //F) and re-run."
fi

# ---------------------------------------------------------------------------
# 4. (Helpers and trap were hoisted above — see "Helper functions" section
#     right after fail(). This keeps the EXIT trap registered before any
#     fail()-reachable check, so meta.json is written on every exit path.)
# ---------------------------------------------------------------------------

# ---------------------------------------------------------------------------
# 5. Start dotnet watch (hot reload) in the background and wait for the app.
# ---------------------------------------------------------------------------
# DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER — do not auto-open a browser tab per restart.
# --non-interactive — auto-restart on rude edits instead of prompting the terminal.
# --verbose         — surfaces "No hot reload changes to apply" so silent flakiness
#                    (.NET 10 GA regression, dotnet/sdk#51185) is detectable; press
#                    Ctrl+R in this terminal to force a rebuild if it stalls.
export DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER=1

info "Starting dotnet watch (hot reload) on $APP_URL ..."
dotnet watch run --project Agentic.Chat --launch-profile http --non-interactive --verbose >"$APP_LOG" 2>&1 &
APP_PID=$!

app_ready=0
deadline=$((SECONDS + 120))
while (( SECONDS < deadline )); do
  if ! kill -0 "$APP_PID" 2>/dev/null; then
    printf '%s\n' "--- app.log (tail) ---" >&2
    tail -n 25 "$APP_LOG" >&2 || true
    fail "app_startup_failed" "dotnet watch exited during startup. See output above (full log: $APP_LOG)."
  fi
  if curl -s -o /dev/null --max-time 2 "$APP_URL/"; then app_ready=1; break; fi
  sleep 1
done
if (( ! app_ready )); then
  printf '%s\n' "--- app.log (tail) ---" >&2
  tail -n 25 "$APP_LOG" >&2 || true
  fail "app_timeout" "App did not respond on $APP_URL within 120s. See output above (full log: $APP_LOG)."
fi
info "App is responding on $APP_URL (hot reload active)"

# ---------------------------------------------------------------------------
# 6. Start the tunnel and detect the public URL.
# ---------------------------------------------------------------------------
info "Starting Cloudflare quick tunnel..."
cloudflared tunnel --url "$APP_URL" >"$TUNNEL_LOG" 2>&1 &
TUNNEL_PID=$!

deadline=$((SECONDS + 60))
while (( SECONDS < deadline )); do
  if ! kill -0 "$TUNNEL_PID" 2>/dev/null; then
    printf '%s\n' "--- tunnel.log (tail) ---" >&2
    tail -n 25 "$TUNNEL_LOG" >&2 || true
    fail "tunnel_startup_failed" "cloudflared exited before printing a URL. See output above (full log: $TUNNEL_LOG)."
  fi
  PUBLIC_URL="$(grep -oE 'https://[a-zA-Z0-9.-]+\.trycloudflare\.com' "$TUNNEL_LOG" | head -1 || true)"
  [[ -n "$PUBLIC_URL" ]] && break
  sleep 1
done
if [[ -z "$PUBLIC_URL" ]]; then
  printf '%s\n' "--- tunnel.log (tail) ---" >&2
  tail -n 25 "$TUNNEL_LOG" >&2 || true
  fail "tunnel_timeout" "No trycloudflare.com URL appeared within 60s (tunnel egress failing?). Check network/firewall and re-run. Full log: $TUNNEL_LOG"
fi

# ---------------------------------------------------------------------------
# 7. Display the phone link.
# ---------------------------------------------------------------------------
printf '\n'
printf '==================================================================\n'
printf '  PHONE LINK (stable across rude-edit restarts this session):\n'
printf '    %s\n' "$PUBLIC_URL"
printf '  Open on your phone. Edit any file; both browsers update live.\n'
printf '  - In-place edits (Razor/CSS/method bodies): no reload, no state loss.\n'
printf '  - Rude edits (Program.cs, new .razor): server restarts, both browsers\n'
printf '    auto-reload, URL stays the same. Chat state resets (scoped service).\n'
printf '  - If hot reload silently stalls (.NET 10 GA bug), press Ctrl+R here.\n'
printf '  Tip: if the link does not load, your router DNS may be filtering\n'
printf '  fresh trycloudflare.com names - use cellular data or set the\n'
printf '  phone DNS to 1.1.1.1.\n'
printf '==================================================================\n\n'

# Best-effort: confirm the public URL serves the app (do not abort on slow propagation).
# -L because / responds 302 -> /chat. NOTE: may fail with HTTP 000 on routers that
# NXDOMAIN fresh trycloudflare.com names (e.g. Verizon CR1000A); not a real failure.
verification_note=""
for _ in $(seq 1 30); do
  code="$(curl -sL -o /dev/null -w '%{http_code}' --max-time 5 "$PUBLIC_URL/" || true)"
  [[ "$code" == "200" ]] && { verification_note="Verified: $PUBLIC_URL returns HTTP 200."; break; }
  sleep 2
done
if [[ -n "$verification_note" ]]; then
  info "$verification_note"
else
  info "Verification: $PUBLIC_URL did not return HTTP 200 within 60s. This is expected on"
  info "routers with DNS-rebinding protection (Verizon CR1000A etc.); the phone on cellular"
  info "will work. See AGENTS.md 'Gotchas learned the hard way'."
fi

# ---------------------------------------------------------------------------
# 8. Foreground wait until Ctrl+C (or a child dies).
# ---------------------------------------------------------------------------
info "Logs: script=$SCRIPT_LOG  app=$APP_LOG  tunnel=$TUNNEL_LOG"
info "Tip: tail -f \"$APP_LOG\" to watch hot-reload deltas apply in real time."
wait "$APP_PID" "$TUNNEL_PID" 2>/dev/null || true
if [[ -z "$SHUTTING_DOWN" ]]; then
  if   ! kill -0 "$APP_PID"    2>/dev/null; then EXIT_REASON="app_died";      info "dotnet watch ended unexpectedly; see $APP_LOG"
  elif ! kill -0 "$TUNNEL_PID" 2>/dev/null; then EXIT_REASON="tunnel_died";   info "The tunnel process ended unexpectedly; see $TUNNEL_LOG"
  fi
fi
