# AGENTS.md

Guidance for AI agents (and humans) working in this repo.

## What this is

**Agentic.Chat** — a Blazor Server (.NET 10) chat app backed by the OpenRouter API.
It runs via `start-phone.sh`, which starts the app with `dotnet watch` (hot reload) and exposes it to a phone via a Cloudflare quick tunnel.

- `Agentic.Chat/` — the web app (entry point: `Program.cs`)
- `Agentic.Chat.Tests/` — xUnit test project
- `start-phone.sh` — one-command Git Bash script: `dotnet watch` (hot reload) on `localhost:5123` + `cloudflared` quick tunnel + clean shutdown. **The only supported way to run the server.**
- `agentic.slnx` — solution file (XML format, .NET 10 default)

## Hard rules

1. **Never commit secrets.** `OPENROUTER_API_KEY` lives only as a Windows User
   environment variable. It must never appear in files, logs, diffs, or commit
   messages. `appsettings*.json` must never contain an API key (a test enforces this).
2. **Don't kill a running server you didn't start.** A live `Agentic.Chat.exe`
   locks `bin/Debug` build outputs; if a session may be in use (e.g. someone on a
   phone via tunnel), build/test with `-c Release` to use a separate output dir,
   or ask before stopping it.
3. **Keep `*.sh` at LF** (`.gitattributes` enforces this — don't override it).

## Setup (Windows + Git Bash)

- .NET SDK 10.x (`dotnet --version` → 10.0.x)
- `cloudflared` on PATH (for the phone tunnel)
- `OPENROUTER_API_KEY` set as a Windows User env var. Git Bash sessions started
  before the var was set won't inherit it — `start-phone.sh` handles this by
  loading it from the registry without printing it.

## Run the server

Interactive (Git Bash):

```bash
bash start-phone.sh     # hot-reload dev server on http://localhost:5123 + cloudflared tunnel
```

This is the **only** supported way to run the server. The script starts `dotnet watch`
(hot reload) and `cloudflared` together, prints the phone link, and cleans up both
processes on Ctrl+C.

Hot reload behavior:
- **In-place edits** (Razor markup, C# method bodies, CSS): applied live to every
  connected browser (local + phone) without a page reload. No state loss.
- **Rude edits** (`Program.cs`, new `.razor` file): server restarts. Local browser
  auto-refreshes via `dotnet watch`'s signal; phone auto-refreshes via the
  `resume-failed` handler in `Components/Layout/ReconnectModal.razor.js`. The
  cloudflared URL stays stable (cloudflared is a separate process). In-memory chat
  state resets — `ChatAgentService` is scoped.
- **Silent stall** (.NET 10 GA bug, [dotnet/sdk#51185](https://github.com/dotnet/sdk/issues/51185)):
  if the verbose log prints `No hot reload changes to apply` after an edit that didn't
  propagate, press `Ctrl+R` in the terminal to force a rebuild.

### Run from an agent / headless shell (IMPORTANT)

Agents must not run `bash start-phone.sh` in the foreground — the tool call
blocks forever. The pattern that works (server stays up, tool returns):

```bash
bash start-phone.sh &                             # no redirection — the script self-logs
SCRIPT_PID=$!
sleep 30                                          # dotnet watch build + tunnel provisioning
RUN_ID=$(cat logs/start-phone/LATEST)            # most recent run's ID
grep "PHONE LINK" "logs/start-phone/$RUN_ID/script.log"  # the https://....trycloudflare.com URL
```

The script writes all output to `logs/start-phone/<run_id>/` automatically:
- `script.log` — script's own stdout/stderr (banner, status, errors)
- `app.log` — dotnet watch verbose output (hot-reload flakiness signals live here)
- `tunnel.log` — cloudflared output (URL provisioning, connection events)
- `meta.json` — structured run summary (URLs, PIDs, timing, `exit_reason`). Written on exit.

The 10 most recent runs are kept; older ones are auto-pruned. `logs/` is already gitignored.

Stopping it later: **`kill -TERM $SCRIPT_PID` — do NOT use `kill -INT`**.
Backgrounded bash jobs inherit SIGINT as *ignored* (POSIX), so INT does nothing;
the script's INT trap only works in an interactive foreground terminal (real Ctrl+C).
TERM runs the exact same cleanup: both process trees killed, port 5123 freed,
`meta.json` finalized with the actual exit reason.

Verify shutdown:

```bash
netstat -ano | grep :5123 | grep LISTENING                       # expect: nothing
tasklist //FI "IMAGENAME eq cloudflared.exe" //NH                # expect: gone
tasklist //FI "IMAGENAME eq Agentic.Chat.exe" //NH               # expect: gone
cat "logs/start-phone/$(cat logs/start-phone/LATEST)/meta.json"  # exit_reason, PIDs, timing
```

Gotchas learned the hard way:

- The tunnel URL changes every run — always read it from the current run's log.
  Within a run, the URL stays stable across rude-edit restarts.
- `/` responds `302 → /chat`; use `curl -L` when health-checking.
- Some routers (e.g. Verizon CR1000A) NXDOMAIN fresh `trycloudflare.com` names
  (DNS-rebinding protection). PC-side, verify with `--doh-url https://1.1.1.1/dns-query`;
  on the phone use cellular data or DNS 1.1.1.1.
- If port 5123 is occupied, the script refuses (with the exact `taskkill` command)
  and exits. `dotnet watch` must own the port for the whole session — free stale
  `Agentic.Chat`/`dotnet` listeners yourself before re-running.

## Testing

```bash
dotnet test              # or: dotnet test -c Release (see hard rule 2)
```

Tests are xUnit in `Agentic.Chat.Tests/`. Add one `[Fact]` class per concern,
named `<Thing>Tests.cs`. There is no test DB or network mocking yet — keep tests
fast and hermetic (no OpenRouter calls).

## Git workflow (PRs on `main`)

`main` is protected: PRs required, no force-push, no deletion. CI (build + test)
must be green to merge.

```bash
git checkout -b feat/short-name          # or fix/, chore/, docs/
# ...work, commit early...
git push -u origin feat/short-name
gh pr create --draft                     # fill in What / Why / How tested
# review the diff: gh pr diff   (or ask an agent to review the PR)
gh pr merge --squash --delete-branch
git checkout main && git pull --prune
```

- One concern per PR; keep them small.
- "How tested" needs evidence (test output, HTTP checks), not intentions.
- Trivial docs/typo fixes may go straight to `main` only if protection allows —
  prefer a PR anyway; it costs a minute.
