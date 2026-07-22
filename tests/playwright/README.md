# Playwright tests — Agentic.Chat reconnect UI

Browser tests for `Components/Layout/ReconnectModal.razor.js`. Priority 4 of the start-phone.sh test plan.

## Why

The `.NET 10.0.0 GA` reconnect-flow regression introduced a new `resume-failed` state that fires after a `dotnet watch` server restart instead of `rejected`. Without the `|| event.detail.state === "resume-failed"` branch in `ReconnectModal.razor.js`, the phone browser sticks on a "Failed to resume" modal after every rude edit. These tests guard against silent regressions of that branch.

## Layout

```
tests/playwright/
├── package.json              # @playwright/test dependency
├── playwright.config.ts      # webServer (auto-starts dotnet run on 5123) + chromium project
├── reconnect-modal.spec.ts   # 4 tests: resume-failed, rejected, show (negative), failed (negative)
├── .gitignore                # node_modules, test-results, playwright-report
└── README.md
```

## Run locally

Prerequisite: nothing running on port 5123 (or reuse an existing server — see below).

```bash
cd tests/playwright
npm install
npx playwright install --with-deps chromium   # one-time browser download
npm test
```

If you already have `bash start-phone.sh` or `dotnet run --project Agentic.Chat` running on 5123, Playwright will reuse it (faster — skips `dotnet run` startup). To force a fresh server, set `CI=true`:

```bash
CI=true npm test
```

## What's covered

| State           | Expected behavior     | Why                                                  |
| --------------- | --------------------- | ---------------------------------------------------- |
| `resume-failed` | `location.reload()`   | .NET 10.0.0 GA terminal state — must reload or phone gets stuck on "Failed to resume" modal. |
| `rejected`      | `location.reload()`   | Original terminal state — must continue working alongside `resume-failed`. |
| `show`          | no reload             | Transient state (modal appears, retries continue). Reload would interrupt reconnection. |
| `failed`        | no reload             | Registers visibilitychange listener for retry-on-tab-focus. |

## CI

`.github/workflows/ci.yml` runs this suite on every PR as the `playwright-tests` job (parallel with `dotnet-test` and `start-phone-tests`).
