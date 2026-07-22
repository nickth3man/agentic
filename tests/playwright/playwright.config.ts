import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright config for Agentic.Chat reconnect-modal tests.
 *
 * The webServer block starts the Blazor app directly via `dotnet run` (no cloudflared —
 * Playwright talks to localhost). A fake OPENROUTER_API_KEY is supplied so Program.cs's
 * api-key guard passes; no real OpenRouter calls are made (these tests only load /chat
 * and dispatch synthetic events at the Blazor-managed reconnect modal).
 *
 * In CI: webServer starts fresh on every run (reuseExistingServer: false).
 * Locally: if you already have `bash start-phone.sh` or `dotnet run` running on 5123,
 * Playwright will reuse it (reuseExistingServer: true).
 */
const APP_URL = 'http://localhost:5123';

export default defineConfig({
  testDir: '.',
  // Tests share port 5123; running them in parallel would interleave navigations.
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : 'list',
  timeout: 30_000,
  expect: { timeout: 5_000 },

  use: {
    baseURL: APP_URL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },

  webServer: {
    command: 'dotnet run --project ../../Agentic.Chat --launch-profile http',
    url: `${APP_URL}/chat`,
    timeout: 90_000,
    reuseExistingServer: !process.env.CI,
    env: {
      // Same fake-key pattern as Agentic.Chat.Tests/ProgramTests.cs. Not a real key;
      // the reconnect tests only load /chat (no OpenRouter call).
      OPENROUTER_API_KEY: 'test-only-fake-key-not-real-no-network',
      ASPNETCORE_ENVIRONMENT: 'Development',
    },
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
