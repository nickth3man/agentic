import { expect, test } from '@playwright/test';

test.describe('Model picker viewport clamp', () => {
  test('popover and page stay within the viewport on a narrow width', async ({ page }) => {
    await page.setViewportSize({ width: 420, height: 720 });
    await page.goto('/chat');
    await page.getByRole('button', { name: 'New chat', exact: true }).last().click();

    const pageBounds = await page.locator('.chat-page').evaluate((el) => {
      const rect = el.getBoundingClientRect();
      return {
        right: rect.right,
        scrollWidth: el.scrollWidth,
        viewportWidth: window.innerWidth,
        docScrollWidth: document.documentElement.scrollWidth,
      };
    });
    expect(pageBounds.right).toBeLessThanOrEqual(pageBounds.viewportWidth + 0.5);
    expect(pageBounds.docScrollWidth).toBeLessThanOrEqual(pageBounds.viewportWidth + 1);

    await page.getByRole('button', { name: 'Select model' }).click();
    const popover = page.locator('.model-picker-popover');
    await expect(popover).toBeVisible();

    const bounds = await popover.evaluate((el) => {
      const rect = el.getBoundingClientRect();
      return {
        left: rect.left,
        right: rect.right,
        top: rect.top,
        bottom: rect.bottom,
        width: rect.width,
        viewportWidth: window.innerWidth,
        viewportHeight: window.innerHeight,
      };
    });

    expect(bounds.left).toBeGreaterThanOrEqual(0);
    expect(bounds.right).toBeLessThanOrEqual(bounds.viewportWidth + 0.5);
    expect(bounds.top).toBeGreaterThanOrEqual(0);
    expect(bounds.bottom).toBeLessThanOrEqual(bounds.viewportHeight + 0.5);
    expect(bounds.width).toBeLessThanOrEqual(640 + 0.5);
  });

  test('popover stays within the viewport on a mid desktop width', async ({ page }) => {
    await page.setViewportSize({ width: 900, height: 720 });
    await page.goto('/chat');
    await page.getByRole('button', { name: 'New chat', exact: true }).last().click();

    await page.getByRole('button', { name: 'Select model' }).click();
    const popover = page.locator('.model-picker-popover');
    await expect(popover).toBeVisible();

    const bounds = await popover.evaluate((el) => {
      const rect = el.getBoundingClientRect();
      return {
        left: rect.left,
        right: rect.right,
        width: rect.width,
        viewportWidth: window.innerWidth,
      };
    });

    expect(bounds.left).toBeGreaterThanOrEqual(0);
    expect(bounds.right).toBeLessThanOrEqual(bounds.viewportWidth + 0.5);
    expect(bounds.width).toBeLessThanOrEqual(640 + 0.5);

    await expect(popover.getByRole('button', { name: /Context/i })).toBeVisible();
    const contextVisible = await popover.getByRole('button', { name: /Context/i }).evaluate((el) => {
      const rect = el.getBoundingClientRect();
      return rect.right <= window.innerWidth && rect.width > 0;
    });
    expect(contextVisible).toBe(true);
  });
});
