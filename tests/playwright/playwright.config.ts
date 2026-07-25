import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright config for Agentic.Chat browser tests.
 *
 * The webServer blocks start a deterministic local OpenRouter-shaped SSE fake and the
 * Blazor app (no cloudflared — Playwright talks to localhost). A fake
 * OPENROUTER_API_KEY is supplied so Program.cs's api-key guard passes; all model and
 * completion requests stay on localhost.
 *
 * Each run starts its own processes so local runs are as hermetic as CI.
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

  webServer: [
    {
      command: 'node fake-openrouter-server.mjs',
      url: 'http://127.0.0.1:5124/health',
      timeout: 10_000,
      reuseExistingServer: false,
    },
    {
      command: 'dotnet run --project ../../Agentic.Chat --launch-profile http',
      url: `${APP_URL}/chat`,
      timeout: 90_000,
      reuseExistingServer: false,
      env: {
        // Same fake-key pattern as Agentic.Chat.Tests/ProgramTests.cs. Not a real key.
        OPENROUTER_API_KEY: 'test-only-fake-key-not-real-no-network',
        OpenRouter__BaseUrl: 'http://127.0.0.1:5124',
        ASPNETCORE_ENVIRONMENT: 'Development',
      },
    },
  ],

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
