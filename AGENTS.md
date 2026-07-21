# AGENTS.md

Guidance for AI agents (and humans) working in this repo.

## What this is

**Agentic.Chat** — a Blazor Server (.NET 10) chat app backed by the OpenRouter API.
It can be exposed to a phone via a Cloudflare quick tunnel using `start-phone.sh`.

- `Agentic.Chat/` — the web app (entry point: `Program.cs`)
- `Agentic.Chat.Tests/` — xUnit test project
- `start-phone.sh` — one-command Git Bash script: app on `localhost:5123` + `cloudflared` quick tunnel + clean shutdown
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

Local only:

```bash
dotnet run --project Agentic.Chat --launch-profile http   # http://localhost:5123
```

With phone access (interactive, in a Git Bash terminal):

```bash
bash start-phone.sh     # prints the PHONE LINK; Ctrl+C stops app + tunnel
```

### Run from an agent / headless shell (IMPORTANT)

Agents must not run `bash start-phone.sh` in the foreground — the tool call
blocks forever. The pattern that works (server stays up, tool returns):

```bash
bash start-phone.sh > /tmp/phone-run.log 2>&1 &
SCRIPT_PID=$!
sleep 50                                          # app build + tunnel provisioning
grep "PHONE LINK" /tmp/phone-run.log              # the https://....trycloudflare.com URL
```

Stopping it later: **`kill -TERM $SCRIPT_PID` — do NOT use `kill -INT`**.
Backgrounded bash jobs inherit SIGINT as *ignored* (POSIX), so INT does nothing;
the script's INT trap only works in an interactive foreground terminal (real Ctrl+C).
TERM runs the exact same cleanup: both process trees killed, port 5123 freed.

Verify shutdown:

```bash
netstat -ano | grep :5123 | grep LISTENING        # expect: nothing
tasklist //FI "IMAGENAME eq cloudflared.exe" //NH # expect: gone
tasklist //FI "IMAGENAME eq Agentic.Chat.exe" //NH # expect: gone
```

Gotchas learned the hard way:

- The tunnel URL changes every run — always read it from the current run's log.
- `/` responds `302 → /chat`; use `curl -L` when health-checking.
- Some routers (e.g. Verizon CR1000A) NXDOMAIN fresh `trycloudflare.com` names
  (DNS-rebinding protection). PC-side, verify with `--doh-url https://1.1.1.1/dns-query`;
  on the phone use cellular data or DNS 1.1.1.1.
- If port 5123 is occupied, the script kills stale `Agentic.Chat`/`dotnet`
  listeners itself and refuses (with the exact `taskkill` command) for anything else.

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
