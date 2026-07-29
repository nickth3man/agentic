# AGENTS.md

Guidance for AI agents (and humans) working in this repo.

## What this is

**Agentic.Chat** — a Blazor Server (.NET 10) chat app backed by the OpenRouter API.
It runs via `start-dev.sh`, which starts the app with `dotnet watch` (hot reload). Users pick models via a ModelPicker UI; selection persists across sessions via `ProtectedLocalStorage`.

- `Agentic.Chat/` — the web app (entry point: `Program.cs`)
- `Agentic.Chat.Tests/` — xUnit test project
- `start-dev.sh` — one-command Git Bash script: `dotnet watch` (hot reload) on `localhost:5123` + clean shutdown. **The only supported way to run the server.**
- `agentic.slnx` — solution file (XML format, .NET 10 default)

### Where things live

Start here instead of re-deriving the layout each session.

| File | Responsibility | Usually breaks on |
| --- | --- | --- |
| `Services/ChatAgentService.cs` | **Core.** Scoped service owning the in-memory transcript, streaming send (`SendStreamingAsync`), SSE delta application (`TryApplyDelta`), and `Reset()` | Async/streaming edits; scoped-lifetime assumptions (state resets on circuit restart) |
| `Services/ModelCatalogService.cs` | Fetches + 15-min caches the OpenRouter model list | Cache expiry, `IHttpClientFactory` usage, network error handling |
| `Services/SelectedModelService.cs` | Persists the chosen model via `ProtectedLocalStorage`; raises `OnChange` | Blazor prerender (storage unavailable until interactive); event wiring |
| `Services/SystemPromptService.cs` | Persists the UI system-prompt override via `ProtectedLocalStorage`; presets; raises `OnChange` | Same prerender/storage caveats as `SelectedModelService`; never log prompt text |
| `Services/OpenRouterOptions.cs` | Bound config (`BaseUrl`, `Model`, `HttpReferer`, `AppTitle`, `SystemPrompt`) | Options binding; a test forbids an API key here |
| `Components/SystemPromptSettings.razor` | Gear-icon settings popover for system prompt presets + textarea | Popover stacking; apply vs idle transcript refresh via `RefreshSystemMessageIfIdle` |
| `Models/` | `ChatDisplayMessage`, `OpenRouterModel` DTOs | JSON shape drift vs. the OpenRouter API |
| `Components/Pages/Chat.razor` | Chat page: renders messages and streaming output | `@key`, render mode, markup rendering, mobile overflow |
| `Components/ModelPicker.razor` (+`.js`) | Model dropdown UI + JS interop | Dropdown z-index/stacking (see #10), interop disposal |
| `Components/Layout/ReconnectModal.razor` (+`.js`) | SignalR circuit-reconnect UI; auto-refresh on rude-edit restart | `resume-failed` handler; circuit lifecycle |
| `Program.cs` / `Program.Partial.cs` | Startup, DI registration, options binding | Rude-edit restarts (see hot reload); service lifetimes |

## Hard rules

1. **Never commit secrets.** `OPENROUTER_API_KEY` lives only as a Windows User
   environment variable. It must never appear in files, logs, diffs, or commit
   messages. `appsettings*.json` must never contain an API key (a test enforces this).
2. **Don't kill a running server you didn't start.** A live `Agentic.Chat.exe`
   locks `bin/Debug` build outputs; if a session may be in use, build/test with `-c Release` to use a separate output dir,
   or ask before stopping it.
3. **Keep `*.sh` at LF** (`.gitattributes` enforces this — don't override it).

## Setup (Windows + Git Bash)

- .NET SDK 10.x (`dotnet --version` → 10.0.x)
- `OPENROUTER_API_KEY` set as a Windows User env var. Git Bash sessions started
  before the var was set won't inherit it — `start-dev.sh` handles this by
  loading it from the registry without printing it.

## Run the server

Interactive (Git Bash):

```bash
bash start-dev.sh     # hot-reload dev server on http://localhost:5123
```

This is the **only** supported way to run the server. The script starts `dotnet watch`
(hot reload) and cleans up on Ctrl+C.

Hot reload behavior:
- **In-place edits** (Razor markup, C# method bodies, CSS): applied live to every
  connected browser without a page reload. No state loss.
- **Rude edits** (`Program.cs`, new `.razor` file): server restarts. Local browser
  auto-refreshes via `dotnet watch`'s signal. In-memory chat
  state resets — `ChatAgentService` is scoped.
- **Silent stall** (.NET 10 GA bug, [dotnet/sdk#51185](https://github.com/dotnet/sdk/issues/51185)):
  if the verbose log prints `No hot reload changes to apply` after an edit that didn't
  propagate, press `Ctrl+R` in the terminal to force a rebuild.

### Run from an agent or automated environment

When running from an AI agent or automated environment, choose the approach matching your tooling:

1. **Managed Observable Terminal (Recommended):**
   Use a process manager tool (e.g. `hub` or an IDE task terminal) to run `start-dev.sh`:
   ```json
   hub(op: "start", name: "server", application: "bash", args: ["start-dev.sh"], ready: {"port": 5123, "log": "App is responding on"})
   ```
   This keeps the server process observable, streams logs live, and allows sending keys (such as `Ctrl+R` to force a hot reload rebuild via `hub send`).

2. **Detached Shell Execution (Fallback for raw shell tools):**
   Standard shell tools must not run `bash start-dev.sh` in the foreground because blocking calls never return. Use a fully detached subshell:
   ```bash
   nohup bash start-dev.sh </dev/null >/dev/null 2>&1 &
   SCRIPT_PID=$!

   # Wait for readiness in logs/dev/LATEST/script.log
   RUN_ID=$(cat logs/dev/LATEST)
   for i in $(seq 1 60); do
     grep -q "App is responding on" "logs/dev/$RUN_ID/script.log" 2>/dev/null && break
     sleep 1
   done
   ```

The script writes all output to `logs/dev/<run_id>/` automatically:
- `script.log` — script's own stdout/stderr (banner, status, errors)
- `app.log` — dotnet watch verbose output (hot-reload flakiness signals live here)
- `meta.json` — structured run summary (PIDs, timing, `exit_reason`). Written on exit.

The 10 most recent runs are kept; older ones are auto-pruned. `logs/` is already gitignored.

Stopping it later: **`kill -TERM $SCRIPT_PID` — do NOT use `kill -INT`**.
Backgrounded bash jobs inherit SIGINT as *ignored* (POSIX), so INT does nothing;
the script's INT trap only works in an interactive foreground terminal (real Ctrl+C).
TERM runs the exact same cleanup: process tree killed, port 5123 freed,
`meta.json` finalized with the actual exit reason.

If `kill -TERM` reports "no such process" — shut down at the Windows level instead:

```bash
# Find whatever is listening on 5123, then tree-kill it.
LPID=$(netstat -ano | grep :5123 | grep LISTENING | awk '{print $NF}' | head -1)
[ -n "$LPID" ] && taskkill //PID "$LPID" //T //F
```

Verify shutdown:

```bash
netstat -ano | grep :5123 | grep LISTENING                       # expect: nothing
tasklist //FI "IMAGENAME eq Agentic.Chat.exe" //NH               # expect: gone
cat "logs/dev/$(cat logs/dev/LATEST)/meta.json"  # exit_reason, PIDs, timing
```

Gotchas learned the hard way:

- `/` responds `302 → /chat`; use `curl -L` when health-checking.
- If port 5123 is occupied, the script refuses (with the exact `taskkill` command)
  and exits. `dotnet watch` must own the port for the whole session — free stale
  `Agentic.Chat`/`dotnet` listeners yourself before re-running.
- If an agent tool call that launches the server is **interrupted** (you stop the
  call, or the call errors out), the script is SIGKILLed and its EXIT cleanup
  trap does **not** run. `dotnet watch` / `Agentic.Chat` are then
  orphaned — still alive, still holding port 5123, with no `meta.json` written.
  The next launch either refuses with `port_occupied`, or worse, `dotnet watch`
  starts but its app crashes with `Failed to bind to address ... address already
  in use`. After any interrupted run, verify the port is free and taskkill stragglers explicitly before re-launching.

## Testing

```bash
dotnet test              # or: dotnet test -c Release (see hard rule 2)
```

The .NET tests are xUnit in `Agentic.Chat.Tests/`. Add one `[Fact]` class per concern,
named `<Thing>Tests.cs`. Keep unit tests fast and hermetic (< 1s execution).

### Developer Feedback Loops

- **Inner Loop (Fast TDD Iteration ~1s):** Run `dotnet test -c Release` frequently while editing code. Executes 350+ unit tests in under 1 second.
- **Outer Loop (CI Parity before push ~75s):** Run the complete 4-job suite before opening/updating PRs.

Additional test suites (run as separate CI jobs, not part of `dotnet test`):

- `tests/dev/` — bash suite for `start-dev.sh` lifecycle, logging, and
  error paths. Run locally with `bash tests/dev/run-tests.sh`. Uses no
  external test framework (no bats); plain bash with assertions in `lib/assertions.sh`.
- `tests/playwright/` — Playwright browser suite for the Blazor reconnect UI.
  Run locally with `cd tests/playwright && npm install && npm test`. Auto-starts
  the app via `dotnet run` on port 5123 with a fake `OPENROUTER_API_KEY`.

### Run what CI runs (before pushing)

CI has four required jobs (see [Git workflow](#git-workflow-prs-on-main) for the
mapping). Reproduce them locally, in the same order, before you push:

```bash
# Job `format` (ubuntu): dotnet format + analyzers (TreatWarningsAsErrors)
dotnet format --verify-no-changes && dotnet build -warnaserror -c Release

# Job `test` (ubuntu): .NET build + xUnit
dotnet restore && dotnet build --no-restore -c Release && dotnet test --no-build -c Release

# Job `dev-tests` (windows): bash lifecycle suite
bash -n start-dev.sh && bash tests/dev/run-tests.sh

# Job `playwright-tests` (ubuntu): Blazor reconnect UI
cd tests/playwright && npm install && npx playwright test
```

Run `dotnet format` (no flags) to auto-fix any formatting the first job flags —
it must be a no-op on a clean checkout, so run it before pushing. The `format`
and `test` jobs are fast and hermetic — always run them. The `dev-tests`
job runs the bash test suite on windows-latest. The Playwright
job is slower (`npm install` + `npx playwright install chromium`); it's fine to
**skip it locally and rely on CI** *unless your change touches the reconnect UI,
`ReconnectModal.*`, or `Chat.razor` rendering*. If you skip it, say so in "How
tested" — a documented skip, not a silent one.

### Verifying UI changes

Automated suites don't cover visual correctness. For UI work (rendering,
layout, mobile), "verified" means you actually looked:

1. Run the server (`bash start-dev.sh`) and open `http://localhost:5123/chat` (follow the `302`, or use `curl -L`).
2. Check the acceptance criteria on both surfaces where relevant. Hot reload makes iterating cheap — in-place edits apply live.
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

## Git workflow (PRs on `main`)

`main` is gated by a repository **ruleset** (the newer GitHub rules engine —
which is why `GET /branches/main/protection` returns 404; that endpoint only
covers legacy branch protection). The ruleset enforces: direct pushes blocked,
PRs required, only `squash` merges allowed, `dismiss_stale_reviews_on_push` on,
`required_approving_review_count` 0, `required_review_thread_resolution` false,
and all four CI jobs required as status checks — `format` (dotnet format +
`-warnaserror` Release build on ubuntu-latest), `test` (xUnit on ubuntu-latest),
`dev-tests` (bash suite on windows-latest), and `playwright-tests` (browser suite on
ubuntu-latest). A PR cannot merge until each of the four reports success, so run
the CI-parity commands above before pushing. AI reviewer checks (CodeRabbit /
Sourcery / cubic) are configured advisory — CodeRabbit posts reviews as
`COMMENT`, never `REQUEST_CHANGES` (see `.coderabbit.yaml`) — so they cannot
block a merge; a human decides.

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
