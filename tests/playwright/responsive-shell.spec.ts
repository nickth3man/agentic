import { expect, test, type Locator } from '@playwright/test';

async function openOverlay(trigger: Locator, openState: Locator) {
  await expect.poll(async () => {
    if (await openState.count() === 0) {
      await trigger.click();
    }

    return openState.count();
  }, {
    timeout: 15_000,
    intervals: [100, 250, 500, 1_000],
  }).toBe(1);
}

test.describe('Responsive chat shell', () => {
  test('keeps settings on-screen and restores focus on mobile', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/chat');

    const trigger = page.locator('button.settings-gear');
    const dialog = page.getByRole('dialog', { name: 'Chat settings' });
    await openOverlay(trigger, page.locator('dialog.settings-popover'));
    await expect(dialog).toBeVisible();
    const bounds = await dialog.evaluate((element) => {
      const rect = element.getBoundingClientRect();
      return {
        left: rect.left,
        right: rect.right,
        top: rect.top,
        bottom: rect.bottom,
        viewportWidth: window.innerWidth,
        viewportHeight: window.innerHeight,
        containsFocus: element.contains(document.activeElement),
      };
    });
    expect(bounds.left).toBeGreaterThanOrEqual(0);
    expect(bounds.right).toBeLessThanOrEqual(bounds.viewportWidth);
    expect(bounds.top).toBeGreaterThanOrEqual(0);
    expect(bounds.bottom).toBeLessThanOrEqual(bounds.viewportHeight);
    expect(bounds.containsFocus).toBe(true);
    await expect(dialog.getByRole('button', { name: 'Apply' })).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Close', exact: true })).toBeVisible();

    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(trigger).toBeFocused();
  });

  test('uses a compact mobile header with touch-sized primary controls', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/chat');

    const headerHeight = await page.locator('.chat-brand').evaluate(
      (element) => element.getBoundingClientRect().height,
    );
    expect(headerHeight).toBeLessThan(150);

    const controls = [
      { name: 'Open conversation list', control: page.getByRole('button', { name: 'Open conversation list' }) },
      { name: 'Select model', control: page.getByRole('button', { name: 'Select model' }) },
      { name: 'Theme', control: page.locator('.theme-toggle') },
      { name: 'Chat settings', control: page.getByRole('button', { name: 'Chat settings' }) },
      { name: 'New chat', control: page.locator('header').getByRole('button', { name: 'New chat' }) },
      { name: 'Send message', control: page.getByRole('button', { name: 'Send message' }) },
    ];
    for (const { name, control } of controls) {
      const size = await control.evaluate((element) => {
        const rect = element.getBoundingClientRect();
        return { width: rect.width, height: rect.height };
      });
      expect(size.width, `${name} width`).toBeGreaterThanOrEqual(44);
      expect(size.height, `${name} height`).toBeGreaterThanOrEqual(44);
    }
  });

  test('contains mobile drawer focus and closes it with Escape', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/chat');
    await page.getByRole('button', { name: 'New chat', exact: true }).last().click();

    const trigger = page.locator('button.sidebar-toggle');
    const drawer = page.getByRole('dialog', { name: 'Conversations' });
    await openOverlay(trigger, page.locator('.chat-shell.sidebar-open'));
    await expect(drawer).toBeVisible();
    await expect(page.locator('main')).toHaveAttribute('inert', '');
    const closeButton = drawer.getByRole('button', { name: 'Close conversation list' });
    await expect(closeButton).toBeFocused();
    for (const control of [
      closeButton,
      drawer.getByRole('button', { name: 'New chat', exact: true }),
      drawer.getByRole('button', { name: 'Rename conversation' }).first(),
      drawer.getByRole('button', { name: 'Delete conversation' }).first(),
    ]) {
      const size = await control.evaluate((element) => {
        const rect = element.getBoundingClientRect();
        return { width: rect.width, height: rect.height };
      });
      expect(size.width).toBeGreaterThanOrEqual(43.9);
      expect(size.height).toBeGreaterThanOrEqual(43.9);
    }

    await page.keyboard.press('Escape');
    await expect(drawer).toBeHidden();
    await expect(page.locator('main')).not.toHaveAttribute('inert', '');
    await expect(trigger).toBeFocused();
  });

  test('keeps the conversation rail persistent on desktop', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 900 });
    await page.goto('/chat');

    const sidebar = page.locator('aside.chat-sidebar[aria-label="Conversations"]');
    await expect(sidebar).toBeVisible();
    await expect(sidebar).toHaveCSS('width', '320px');
    const geometry = await sidebar.boundingBox();
    expect(geometry).not.toBeNull();
    expect(geometry!.x).toBe(0);
    expect(geometry!.width).toBeGreaterThanOrEqual(280);
    const workspace = await page.locator('main').evaluate((element) => {
      const main = element.getBoundingClientRect();
      const header = element.querySelector('header')!.getBoundingClientRect();
      return { width: main.width, headerHeight: header.height };
    });
    expect(workspace.width).toBeGreaterThanOrEqual(1000);
    expect(workspace.headerHeight).toBeLessThan(100);
    await expect(page.getByRole('button', { name: 'Open conversation list' })).toBeHidden();
    await expect(page.locator('.sidebar-backdrop')).toBeHidden();
  });
});
