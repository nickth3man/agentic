import { test, expect, type Page } from '@playwright/test';

/**
 * Tests for Components/Layout/ReconnectModal.razor.js.
 *
 * Priority 4 of the start-phone.sh test plan. These guard the JS reconnect-state
 * handler against silent regressions — specifically the .NET 10.0.0 GA bug where
 * the framework fires 'resume-failed' instead of 'rejected' after a server restart.
 * Without the `|| event.detail.state === "resume-failed"` branch in ReconnectModal.razor.js,
 * the phone browser sticks on a "Failed to resume" modal after every rude edit.
 *
 * Approach: navigate to /chat, wait for Blazor to mount #components-reconnect-modal
 * (the element on which the state-change listener is registered), then dispatch
 * synthetic events with each state value and verify the expected behavior:
 *   - 'resume-failed' → location.reload() fires (terminal — server is gone)
 *   - 'rejected'      → location.reload() fires (terminal — same)
 *   - 'show'          → no reload (transient — modal appears, retries continue)
 *   - 'failed'        → no reload (registers visibility listener for retry)
 */

async function dispatchReconnectState(page: Page, state: string): Promise<void> {
  await page.evaluate((s) => {
    const modal = document.getElementById('components-reconnect-modal');
    if (!modal) {
      throw new Error(
        'components-reconnect-modal element not found — Blazor may not have loaded the layout yet',
      );
    }
    modal.dispatchEvent(
      new CustomEvent('components-reconnect-state-changed', { detail: { state: s } }),
    );
  }, state);
}

test.describe('ReconnectModal state handler', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/chat');
    // Wait for Blazor to mount the reconnect modal. The handler is registered by
    // ReconnectModal.razor.js (loaded as a module after the element exists).
    await page.waitForSelector('#components-reconnect-modal', { timeout: 15_000 });
  });

  test("'resume-failed' state triggers location.reload()", async ({ page }) => {
    // This is the state added by .NET 10.0.0 GA. The regression: without our fix,
    // the handler ignores it and the page never reloads.
    await Promise.all([
      page.waitForNavigation({ timeout: 5_000 }),
      dispatchReconnectState(page, 'resume-failed'),
    ]);
    // If we got here, the page reloaded — location.reload() fired.
    expect(page.url()).toContain('/chat');
  });

  test("'rejected' state triggers location.reload()", async ({ page }) => {
    // Original terminal state — must continue working alongside 'resume-failed'.
    await Promise.all([
      page.waitForNavigation({ timeout: 5_000 }),
      dispatchReconnectState(page, 'rejected'),
    ]);
    expect(page.url()).toContain('/chat');
  });

  test("'show' state does NOT trigger location.reload()", async ({ page }) => {
    // 'show' is transient (modal appears, retries continue underneath). A reload here
    // would interrupt live reconnection attempts. Assert no-reload via a window marker
    // that would be wiped if the page context were replaced.
    await page.evaluate(() => {
      (window as unknown as { __noReloadMarker?: string }).__noReloadMarker = 'alive';
    });
    await dispatchReconnectState(page, 'show');
    // Give the handler a generous window to fire reload if it were going to.
    await page.waitForTimeout(1_000);
    const marker = await page.evaluate(
      () => (window as unknown as { __noReloadMarker?: string }).__noReloadMarker,
    );
    expect(marker).toBe('alive');
  });

  test("'failed' state does NOT trigger location.reload()", async ({ page }) => {
    // 'failed' registers a visibilitychange listener for retry-on-tab-focus; not a reload.
    await page.evaluate(() => {
      (window as unknown as { __noReloadMarker?: string }).__noReloadMarker = 'alive';
    });
    await dispatchReconnectState(page, 'failed');
    await page.waitForTimeout(1_000);
    const marker = await page.evaluate(
      () => (window as unknown as { __noReloadMarker?: string }).__noReloadMarker,
    );
    expect(marker).toBe('alive');
  });
});
