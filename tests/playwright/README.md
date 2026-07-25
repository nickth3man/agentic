# Playwright tests — Agentic.Chat browser UI

Hermetic browser tests for chat streaming and `Components/Layout/ReconnectModal.razor.js`.

## Why

The `.NET 10.0.0 GA` reconnect-flow regression introduced a new `resume-failed` state that fires after a `dotnet watch` server restart instead of `rejected`. Without the `|| event.detail.state === "resume-failed"` branch in `ReconnectModal.razor.js`, the phone browser sticks on a "Failed to resume" modal after every rude edit. These tests guard against silent regressions of that branch.

## Layout

```
tests/playwright/
├── package.json              # @playwright/test dependency
├── playwright.config.ts      # local fake + app webServers and chromium project
├── fake-openrouter-server.mjs # local /models and /chat/completions fake
├── fixtures/
│   └── openrouter-reasoning-stream.sse # OpenRouter-shaped streaming fixture
├── chat.spec.ts              # streaming, multi-turn, and 429-recovery coverage
├── reconnect-modal.spec.ts   # 4 tests: resume-failed, rejected, show (negative), failed (negative)
├── .gitignore                # node_modules, test-results, playwright-report
└── README.md
```

## Run locally

Prerequisite: nothing running on ports 5123 or 5124. The suite starts fresh local
processes on both ports and sends no traffic to OpenRouter.

```bash
cd tests/playwright
npm install
npx playwright install --with-deps chromium   # one-time browser download
npm test
```

## What's covered

| State           | Expected behavior     | Why                                                  |
| --------------- | --------------------- | ---------------------------------------------------- |
| `resume-failed` | `location.reload()`   | .NET 10.0.0 GA terminal state — must reload or phone gets stuck on "Failed to resume" modal. |
| `rejected`      | `location.reload()`   | Original terminal state — must continue working alongside `resume-failed`. |
| `show`          | no reload             | Transient state (modal appears, retries continue). Reload would interrupt reconnection. |
| `failed`        | no reload             | Registers visibilitychange listener for retry-on-tab-focus. |
| Chat SSE        | reasoning then content | The local fixture streams OpenRouter-shaped `data:` chunks. |
| Multi-turn chat | prior turn preserved   | The fake rejects a second turn without prior user history. |
| 429 recovery    | error then next turn   | A rate-limit response renders without blocking a later send. |

## CI

`.github/workflows/ci.yml` runs this suite on every PR as the `playwright-tests` job (parallel with `dotnet-test` and `start-phone-tests`).
