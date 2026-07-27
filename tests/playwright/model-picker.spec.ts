import { expect, test, type Page } from '@playwright/test';

async function openPicker(page: Page) {
  await page.getByRole('button', { name: 'New chat', exact: true }).last().click();
  await page.getByRole('button', { name: 'Select model' }).click();
  const popover = page.locator('.model-picker-popover');
  await expect(popover).toBeVisible();
  return popover;
}

async function assertPopoverInViewport(popover: ReturnType<Page['locator']>) {
  const bounds = await popover.evaluate((el) => {
    const rect = el.getBoundingClientRect();
    const viewportWidth = window.visualViewport?.width ?? window.innerWidth;
    const viewportHeight = window.visualViewport?.height ?? window.innerHeight;
    return {
      left: rect.left,
      right: rect.right,
      top: rect.top,
      bottom: rect.bottom,
      width: rect.width,
      viewportWidth,
      viewportHeight,
    };
  });

  expect(bounds.left).toBeGreaterThanOrEqual(-0.5);
  expect(bounds.right).toBeLessThanOrEqual(bounds.viewportWidth + 0.5);
  expect(bounds.top).toBeGreaterThanOrEqual(-0.5);
  expect(bounds.bottom).toBeLessThanOrEqual(bounds.viewportHeight + 0.5);
  expect(bounds.width).toBeLessThanOrEqual(640 + 0.5);
}

test.describe('Model picker viewport clamp', () => {
  test('uses an accessible full-screen selector and restores focus on mobile', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/chat');

    const trigger = page.getByRole('button', { name: 'Select model' });
    await trigger.click();
    const popover = page.getByRole('dialog', { name: 'Select a model' });
    await expect(popover).toBeVisible();

    const metrics = await popover.evaluate((element) => {
      const rect = element.getBoundingClientRect();
      return {
        left: rect.left,
        top: rect.top,
        width: rect.width,
        height: rect.height,
        containsFocus: element.contains(document.activeElement),
      };
    });
    expect(metrics.left).toBe(0);
    expect(metrics.top).toBe(0);
    expect(metrics.width).toBe(390);
    expect(metrics.height).toBe(844);
    expect(metrics.containsFocus).toBe(true);
    await expect(popover.getByRole('button', { name: 'Close model picker' })).toBeVisible();
    await expect(popover.locator('.model-picker-row')).toHaveCount(80);
    await expect(popover.locator('.model-picker-limit')).toContainText('Showing 80 of 102 models');

    await page.keyboard.press('Escape');
    await expect(popover).toBeHidden();
    await expect(trigger).toBeFocused();
  });

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

    const popover = await openPicker(page);
    await assertPopoverInViewport(popover);
  });

  test('popover stays within the viewport on a mid desktop width', async ({ page }) => {
    await page.setViewportSize({ width: 900, height: 720 });
    await page.goto('/chat');

    const popover = await openPicker(page);
    await assertPopoverInViewport(popover);

    await expect(popover.getByRole('button', { name: /Context/i })).toBeVisible();
    const contextVisible = await popover.getByRole('button', { name: /Context/i }).evaluate((el) => {
      const rect = el.getBoundingClientRect();
      return rect.right <= window.innerWidth && rect.width > 0;
    });
    expect(contextVisible).toBe(true);
  });

  test('popover reclamps after viewport resize and stays in a short height', async ({ page }) => {
    await page.setViewportSize({ width: 900, height: 720 });
    await page.goto('/chat');

    const popover = await openPicker(page);
    await assertPopoverInViewport(popover);

    await page.setViewportSize({ width: 420, height: 380 });
    // Give the rAF-throttled reposition a frame to run.
    await page.waitForTimeout(50);
    await assertPopoverInViewport(popover);

    const tableMaxHeight = await popover.locator('.model-picker-table-wrap').evaluate((el) => {
      return (el as HTMLElement).style.maxHeight;
    });
    expect(tableMaxHeight).not.toBe('');
    const parsed = Number.parseFloat(tableMaxHeight);
    expect(Number.isFinite(parsed)).toBe(true);
    expect(parsed).toBeGreaterThanOrEqual(0);
    expect(parsed).toBeLessThan(380);
  });

  test('scrolling the model list does not eject the popover from the viewport', async ({ page }) => {
    await page.setViewportSize({ width: 720, height: 640 });
    await page.goto('/chat');

    const popover = await openPicker(page);
    const tableWrap = popover.locator('.model-picker-table-wrap');
    await expect(tableWrap).toBeVisible();

    // CI uses a small fake catalog that may not overflow at natural height.
    // Force a short wrap so nested scroll is exercised regardless of model count.
    await tableWrap.evaluate((el) => {
      (el as HTMLElement).style.maxHeight = '120px';
    });

    const metrics = await tableWrap.evaluate((el) => ({
      before: el.scrollTop,
      scrollHeight: el.scrollHeight,
      clientHeight: el.clientHeight,
    }));
    expect(metrics.scrollHeight).toBeGreaterThan(metrics.clientHeight);

    await tableWrap.evaluate((el) => {
      el.scrollTop = Math.min(el.scrollHeight, 80);
    });
    const after = await tableWrap.evaluate((el) => el.scrollTop);
    expect(after).toBeGreaterThan(metrics.before);

    await page.waitForTimeout(50);
    await assertPopoverInViewport(popover);
    // Nested scroll must not reset scrollTop via a max-height clear/reapply.
    const still = await tableWrap.evaluate((el) => el.scrollTop);
    expect(still).toBeGreaterThan(0);
  });
});
