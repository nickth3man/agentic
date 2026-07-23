# AGENTS.md

Guidance for AI agents (and humans) working in this repo.

## What this is

**Agentic.Chat** — a Blazor Server (.NET 10) chat app backed by the OpenRouter API.
It runs via `start-phone.sh`, which starts the app with `dotnet watch` (hot reload) and exposes it to a phone via a Cloudflare quick tunnel. Users pick models via a ModelPicker UI; selection persists across sessions via `ProtectedLocalStorage`.

- `Agentic.Chat/` — the web app (entry point: `Program.cs`)
- `Agentic.Chat.Tests/` — xUnit test project
- `start-phone.sh` — one-command Git Bash script: `dotnet watch` (hot reload) on `localhost:5123` + `cloudflared` quick tunnel + clean shutdown. **The only supported way to run the server.**
- `agentic.slnx` — solution file (XML format, .NET 10 default)

### Where things live

Start here instead of re-deriving the layout each session.

| File | Responsibility | Usually breaks on |
| --- | --- | --- |
| `Services/ChatAgentService.cs` | **Core.** Scoped service owning the in-memory transcript, streaming send (`SendStreamingAsync`), SSE delta application (`TryApplyDelta`), and `Reset()` | Async/streaming edits; scoped-lifetime assumptions (state resets on circuit restart) |
| `Services/ModelCatalogService.cs` | Fetches + 15-min caches the OpenRouter model list | Cache expiry, `IHttpClientFactory` usage, network error handling |
| `Services/SelectedModelService.cs` | Persists the chosen model via `ProtectedLocalStorage`; raises `OnChange` | Blazor prerender (storage unavailable until interactive); event wiring |
| `Services/OpenRouterOptions.cs` | Bound config (`BaseUrl`, `Model`, `HttpReferer`, `AppTitle`) | Options binding; a test forbids an API key here |
| `Models/` | `ChatDisplayMessage`, `OpenRouterModel` DTOs | JSON shape drift vs. the OpenRouter API |
| `Components/Pages/Chat.razor` | Chat page: renders messages and streaming output | `@key`, render mode, markup rendering, mobile overflow |
| `Components/ModelPicker.razor` (+`.js`) | Model dropdown UI + JS interop | Dropdown z-index/stacking (see #10), interop disposal |
| `Components/Layout/ReconnectModal.razor` (+`.js`) | SignalR circuit-reconnect UI; phone auto-refresh on rude-edit restart | `resume-failed` handler; circuit lifecycle |
| `Program.cs` / `Program.Partial.cs` | Startup, DI registration, options binding | Rude-edit restarts (see hot reload); service lifetimes |

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
# Detach fully so THIS tool call returns AND the server outlives the agent shell:
#   nohup              — ignore SIGHUP, so the server survives the tool shell
#                        tearing down after the call returns (without it, a
#                        backgrounded job can die when the agent session ends).
#   </dev/null         — don't let the script/children read the tool's stdin.
#   >/dev/null 2>&1    — don't hold the tool's stdout/stderr pipe open. The
#                        script detects the non-TTY stdout and writes to
#                        script.log only (see start-phone.sh "TTY-conditional"),
#                        so nothing anchors the pipe and this call returns at
#                        the `&` instead of blocking until Ctrl+C.
nohup bash start-phone.sh </dev/null >/dev/null 2>&1 &
SCRIPT_PID=$!

# dotnet watch build + tunnel provisioning takes ~20-40s. Poll the log for the
# PHONE LINK rather than sleeping blind — returns as soon as the URL is printed.
RUN_ID=$(cat logs/start-phone/LATEST)
for i in $(seq 1 60); do
  grep -q "PHONE LINK" "logs/start-phone/$RUN_ID/script.log" 2>/dev/null && break
  sleep 1
done
grep -A1 "PHONE LINK" "logs/start-phone/$RUN_ID/script.log"   # the https://....trycloudflare.com URL
```

> **Do not** launch with a bare `bash start-phone.sh &` and "no redirection".
> That form (documented in older revisions of this file) anchors the script's
> `tee` to the tool's stdout pipe for the script's whole lifetime, so the tool
> call never returns. The `nohup ... </dev/null >/dev/null 2>&1 &` form above is
> required from agents.

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

`$SCRIPT_PID` is an MSYS PID and only resolves inside the same bash session that
launched the script. From a **later** agent tool call (different session) — or if
`kill -TERM` reports "no such process" — shut down at the Windows level instead:

```bash
# Find whatever is listening on 5123, then tree-kill it + cloudflared.
LPID=$(netstat -ano | grep :5123 | grep LISTENING | awk '{print $NF}' | head -1)
[ -n "$LPID" ] && taskkill //PID "$LPID" //T //F
taskkill //IM cloudflared.exe //T //F 2>/dev/null || true
```

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
- If an agent tool call that launches the server is **interrupted** (you stop the
  call, or the call errors out), the script is SIGKILLed and its EXIT cleanup
  trap does **not** run. `dotnet watch` / `Agentic.Chat` / `cloudflared` are then
  orphaned — still alive, still holding port 5123, with no `meta.json` written.
  The next launch either refuses with `port_occupied`, or worse, `dotnet watch`
  starts but its app crashes with `Failed to bind to address ... address already
  in use` while the tunnel points at the dead port. After any interrupted run,
  verify the port is free and taskkill stragglers explicitly before re-launching.

## Testing

```bash
dotnet test              # or: dotnet test -c Release (see hard rule 2)
```

The .NET tests are xUnit in `Agentic.Chat.Tests/`. Add one `[Fact]` class per concern,
named `<Thing>Tests.cs`. There is no test DB or network mocking yet — keep tests
fast and hermetic (no OpenRouter calls).

Additional test suites (run as separate CI jobs, not part of `dotnet test`):

- `tests/start-phone/` — bash suite for `start-phone.sh` lifecycle, logging, and
  error paths. Run locally with `bash tests/start-phone/run-tests.sh`. Uses no
  external test framework (no bats); plain bash with assertions in `lib/assertions.sh`.
- `tests/playwright/` — Playwright browser suite for the Blazor reconnect UI.
  Run locally with `cd tests/playwright && npm install && npm test`. Auto-starts
  the app via `dotnet run` on port 5123 with a fake `OPENROUTER_API_KEY`.

### Run what CI runs (before pushing)

CI has three required jobs (see [Git workflow](#git-workflow-prs-on-main) for the
mapping). Reproduce them locally, in the same order, before you push:

```bash
# Job `test` (ubuntu): .NET build + xUnit
dotnet restore && dotnet build --no-restore -c Release && dotnet test --no-build -c Release

# Job `start-phone-tests` (windows): bash lifecycle suite
bash -n start-phone.sh && bash tests/start-phone/run-tests.sh

# Job `playwright-tests` (ubuntu): Blazor reconnect UI
cd tests/playwright && npm install && npx playwright test
```

The first two are fast and hermetic — always run them. The Playwright job is
slower (`npm install` + `npx playwright install chromium`); it's fine to **skip
it locally and rely on CI** *unless your change touches the reconnect UI,
`ReconnectModal.*`, or `Chat.razor` rendering*. If you skip it, say so in "How
tested" — a documented skip, not a silent one.

### Verifying UI changes

Automated suites don't cover visual correctness. For UI work (rendering,
layout, mobile), "verified" means you actually looked:

1. Run the server headless (see [Run from an agent](#run-from-an-agent--headless-shell-important))
   and open `http://localhost:5123/chat` (follow the `302`, or use `curl -L`).
2. Check the acceptance criteria on both surfaces where relevant: local browser
   **and** the phone via the tunnel URL (mobile overflow/scroll behaves
   differently). Hot reload makes iterating cheap — in-place edits apply live.
3. Capture the before/after screenshot the PR template asks for. The Playwright
   suite can drive a headless browser for a scripted screenshot if you don't
   have a device handy.

## AI reviewers

Three AI code reviewers run on every PR: **CodeRabbit**, **Sourcery**, and **cubic**.
All three are advisory — none block merge.

Configuration:

- `.coderabbit.yaml` (in-repo) — `request_changes_workflow: true` forces inline
  annotations instead of summary-only output; `path_instructions` add C#/.razor/.sh-
  specific risk areas (null safety, async, SignalR circuit lifecycle, dispose
  patterns, bash hygiene).
- `cubic.yaml` (in-repo) — `sensitivity: high` enables thorough inline feedback;
  `custom_instructions` inject Blazor/SignalR/.NET project context.
- Sourcery — AI review config is dashboard-only at
  [app.sourcery.ai/dashboard/review-settings](https://app.sourcery.ai/dashboard/review-settings).
  No in-repo YAML exists.

The PR template's `## Review focus` section is parsed by CodeRabbit and cubic as
per-PR guidance that adds to the config-file instructions. Use it on PRs with
specific concerns; delete the section on PRs where it doesn't apply.

Don't casually modify `.coderabbit.yaml` or `cubic.yaml` without understanding
the tradeoffs — see the commit history of those files for context on why each
flag is set the way it is.

## Dependency updates (Renovate)

**Renovate** manages all dependencies — NuGet, npm, and GitHub Actions are
auto-detected. Config: `.github/renovate.json5`. (Replaced Dependabot on
2026-07-22, PRs #48–#50; there is no `dependabot.yml` anymore.)

It is configured for **maximum automation**: it proposes every semver level
(including majors) the moment a version publishes — no cooldown — and
**auto-merges each PR via GitHub's native auto-merge once the required CI checks
are green**. Branch protection is the safety gate: a bump that breaks the build
or tests never merges, it just sits as an open PR. It also runs lockfile
maintenance and keeps pinned action digests current, and maintains a single
Dependency Dashboard issue listing everything it manages.

Because green CI ⇒ auto-merge, the required suites are the only thing standing
between a dependency bump and `main` — another reason not to weaken them. Don't
casually edit `renovate.json5`; if bot PRs get noisy, tune throttling there
rather than disabling checks.

## Issue → PR workflow (agent skills)

Two machine-level Agent Skills (open standard, agentskills.io) drive the
issue-resolution loop in OpenCode, Claude Code, and Cursor — nothing is stored
in this repo:

- `/fix-issue <number|url>` — read the issue, discover conventions, reproduce,
  implement, verify, open a draft PR, post a state comment.
- `/babysit-pr <number>` — resume any PR: classify CI failures and unresolved
  review threads, repair (max 3 rounds) or escalate.

Canonical skills: `~/.config/opencode/skills/{fix-issue,babysit-pr}/` (proxied
into `~/.claude/skills/` and `~/.agents/skills/`; commands installed per
agent). Cross-session state lives in a `<!-- agent-state -->` comment on the
PR, so any agent or human can resume. Portable copies:
`~/Documents/GitHub/skills/`.

Humans: treat the `<!-- agent-state -->` comment as agent-owned — put requests
and instructions in normal PR comments instead. Two safe interventions on a
stuck PR: set its `Status:` line to `needs-human` (agents stop and wait), or
delete the comment entirely (resets the loop and its attempt budget).

## Git workflow (PRs on `main`)

`main` is protected: PRs required, no force-push, no deletion. CI runs three
parallel jobs — `test` (xUnit on ubuntu-latest), `start-phone-tests` (bash suite
on windows-latest; installs `cloudflared` explicitly since it's not preinstalled),
`playwright-tests` (browser suite on ubuntu-latest). All three must pass to merge.
AI reviewer checks (CodeRabbit / Sourcery / cubic) are advisory and do not block.

```bash
git checkout -b feat/short-name          # or fix/, chore/, docs/
# ...work, commit early...
git push -u origin feat/short-name
gh pr create --draft                     # fill in What / Why / How tested
                                         # (.github/workflows/auto-ready.yml
                                         #  auto-promotes to ready when CI passes)
# review the diff: gh pr diff   (or ask an agent to review the PR)
gh pr merge --squash --delete-branch
git checkout main && git pull --prune
```

- One concern per PR; keep them small.
- **Conventional Commits.** Commit messages and PR titles use `feat:` / `fix:` /
  `chore:` / `docs:` (optional scope, imperative mood). Because PRs merge with
  `--squash`, **the PR title becomes the commit message on `main`** — make it a
  well-formed, self-contained Conventional Commit line, not "address review" or
  "fixes".
- "How tested" needs evidence (test output, HTTP checks), not intentions.
- Trivial docs/typo fixes may go straight to `main` only if protection allows —
  prefer a PR anyway; it costs a minute.
- **Auto-ready**: drafts auto-promote to "ready for review" when CI completes
  successfully. So `gh pr create --draft` is "fire and forget" — you don't have
  to come back and `gh pr ready` manually. Caveat: `workflow_run` events use the
  workflow file from `main`, so the auto-ready behavior only applies to PRs
  opened AFTER the workflow file itself landed on `main`.
