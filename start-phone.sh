#!/usr/bin/env bash
#
# start-phone.sh — one-command startup for Agentic.Chat + Cloudflare quick tunnel.
#
# Run from Git Bash on Windows:
#   bash start-phone.sh
#
# What it does:
#   1. Fails fast if OPENROUTER_API_KEY is not available (env or Windows User env var).
#      The key is never printed, logged, or persisted by this script.
#   2. Starts the app on http://localhost:5123 (dotnet run --project Agentic.Chat --launch-profile http)
#      and waits until it responds.
#   3. Starts `cloudflared tunnel --url http://localhost:5123`, detects the printed
#      https://....trycloudflare.com URL and displays it as the phone link.
#   4. On Ctrl+C / termination, stops both processes (full process trees) so port 5123
#      is freed and the public URL stops serving.
#
# NOTE: the tunnel URL changes every run — always use the URL printed by THIS run.

set -euo pipefail

APP_URL="http://localhost:5123"
APP_PORT=5123
LOG_DIR="$(mktemp -d)"
APP_LOG="$LOG_DIR/app.log"
TUNNEL_LOG="$LOG_DIR/tunnel.log"
APP_PID=""
TUNNEL_PID=""
PUBLIC_URL=""
SHUTTING_DOWN=""

info()  { printf '[start-phone] %s\n' "$*"; }
fail()  { printf '[start-phone] ERROR: %s\n' "$*" >&2; exit 1; }

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
[[ -n "${OPENROUTER_API_KEY:-}" ]] || fail "OPENROUTER_API_KEY is not set. Set it as a Windows User environment variable (or export it) and re-run."
unset val

# ---------------------------------------------------------------------------
# 2. Prerequisites
# ---------------------------------------------------------------------------
command -v dotnet      >/dev/null 2>&1 || fail "dotnet not found on PATH. Install the .NET SDK and re-run."
command -v cloudflared >/dev/null 2>&1 || fail "cloudflared not found on PATH. Install cloudflared (https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/downloads/) and re-run."

# ---------------------------------------------------------------------------
# 3. Port check — resolve stale listeners we own, refuse anything else.
# ---------------------------------------------------------------------------
listener_pid() {
  netstat -ano 2>/dev/null | grep 'LISTENING' | grep -E "[:.]$APP_PORT[[:space:]]" | awk '{print $NF}' | head -1
}

existing_pid="$(listener_pid || true)"
if [[ -n "$existing_pid" ]]; then
  pname="$(powershell -NoProfile -Command "(Get-Process -Id $existing_pid -ErrorAction SilentlyContinue).ProcessName" 2>/dev/null | tr -d '\r' || true)"
  case "$pname" in
    Agentic.Chat|dotnet)
      info "Port $APP_PORT is held by a stale $pname process (PID $existing_pid); terminating it."
      taskkill //PID "$existing_pid" //T //F >/dev/null 2>&1 || true
      sleep 2
      [[ -z "$(listener_pid || true)" ]] || fail "Could not free port $APP_PORT (PID $existing_pid still listening). Run: taskkill //PID $existing_pid //T //F"
      ;;
    *)
      fail "Port $APP_PORT is occupied by '$pname' (PID $existing_pid), which this script cannot safely stop. Free it yourself (taskkill //PID $existing_pid //T //F) and re-run."
      ;;
  esac
fi

# ---------------------------------------------------------------------------
# 4. Cleanup — kill both process trees, guarantee the port is freed.
# ---------------------------------------------------------------------------
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

cleanup() {
  trap - INT TERM EXIT
  SHUTTING_DOWN=1
  info "Shutting down app and tunnel..."
  kill_tree "$TUNNEL_PID"
  kill_tree "$APP_PID"
  sleep 2
  # Safety net: nothing may remain bound to the port.
  local leftover; leftover="$(listener_pid || true)"
  if [[ -n "$leftover" ]]; then
    taskkill //PID "$leftover" //T //F >/dev/null 2>&1 || true
  fi
  info "Stopped. Port $APP_PORT freed; tunnel URL no longer serves."
  info "Logs kept at: $LOG_DIR"
}
trap cleanup INT TERM EXIT

# ---------------------------------------------------------------------------
# 5. Start the app and wait for it to respond.
# ---------------------------------------------------------------------------
info "Starting Agentic.Chat on $APP_URL ..."
dotnet run --project Agentic.Chat --launch-profile http >"$APP_LOG" 2>&1 &
APP_PID=$!

app_ready=0
deadline=$((SECONDS + 120))
while (( SECONDS < deadline )); do
  if ! kill -0 "$APP_PID" 2>/dev/null; then
    printf '%s\n' "--- app.log (tail) ---" >&2
    tail -n 25 "$APP_LOG" >&2 || true
    fail "The app exited during startup. See output above (full log: $APP_LOG)."
  fi
  if curl -s -o /dev/null --max-time 2 "$APP_URL/"; then app_ready=1; break; fi
  sleep 1
done
if (( ! app_ready )); then
  printf '%s\n' "--- app.log (tail) ---" >&2
  tail -n 25 "$APP_LOG" >&2 || true
  fail "App did not respond on $APP_URL within 120s. See output above (full log: $APP_LOG)."
fi
info "App is responding on $APP_URL"

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
    fail "cloudflared exited before printing a URL. See output above (full log: $TUNNEL_LOG)."
  fi
  PUBLIC_URL="$(grep -oE 'https://[a-zA-Z0-9.-]+\.trycloudflare\.com' "$TUNNEL_LOG" | head -1 || true)"
  [[ -n "$PUBLIC_URL" ]] && break
  sleep 1
done
if [[ -z "$PUBLIC_URL" ]]; then
  printf '%s\n' "--- tunnel.log (tail) ---" >&2
  tail -n 25 "$TUNNEL_LOG" >&2 || true
  fail "No trycloudflare.com URL appeared within 60s (tunnel egress failing?). Check network/firewall and re-run. Full log: $TUNNEL_LOG"
fi

# ---------------------------------------------------------------------------
# 7. Display the phone link.
# ---------------------------------------------------------------------------
printf '\n'
printf '==================================================================\n'
printf '  PHONE LINK (this run only): %s\n' "$PUBLIC_URL"
printf '  Open this URL on your phone (any network). Ctrl+C here stops it.\n'
printf '  Tip: if the link does not load, your router DNS may be filtering\n'
printf '  fresh trycloudflare.com names - use cellular data or set the\n'
printf '  phone DNS to 1.1.1.1.\n'
printf '==================================================================\n\n'

# Best-effort: confirm the public URL serves the app (do not abort on slow propagation).
# -L because / responds 302 -> /chat.
for _ in $(seq 1 30); do
  code="$(curl -sL -o /dev/null -w '%{http_code}' --max-time 5 "$PUBLIC_URL/" || true)"
  [[ "$code" == "200" ]] && { info "Verified: $PUBLIC_URL returns HTTP 200."; break; }
  sleep 2
done

# ---------------------------------------------------------------------------
# 8. Foreground wait until Ctrl+C (or a child dies).
# ---------------------------------------------------------------------------
info "Logs: app=$APP_LOG tunnel=$TUNNEL_LOG"
wait "$APP_PID" "$TUNNEL_PID" 2>/dev/null || true
if [[ -z "$SHUTTING_DOWN" ]]; then
  kill -0 "$APP_PID"    2>/dev/null || info "The app process ended unexpectedly; see $APP_LOG"
  kill -0 "$TUNNEL_PID" 2>/dev/null || info "The tunnel process ended unexpectedly; see $TUNNEL_LOG"
fi
