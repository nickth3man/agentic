import { expect, test } from '@playwright/test';

test.describe('Chat streaming scroll lock', () => {
  test('follows streamed content, unlocks on scroll, and resumes from the jump control', async ({ page }) => {
    await page.goto('/chat');

    await page.evaluate(async () => {
      const fixture = document.createElement('div');
      const container = document.createElement('div');
      const sentinel = document.createElement('div');
      const jumpButton = document.createElement('button');

      container.style.cssText = 'height: 100px; overflow-y: auto;';
      container.innerHTML = '<div style="height: 1000px"></div>';
      jumpButton.textContent = 'Jump to latest';
      container.append(sentinel);
      fixture.append(container, jumpButton);
      document.body.append(fixture);

      const module = await import('/Components/Pages/Chat.razor.js');
      const controller = module.initialize(container, sentinel, jumpButton);
      if (!controller) throw new Error('Scroll controller did not initialize');

      (window as Window & { __scrollFixture?: { container: HTMLDivElement; controller: typeof controller; jumpButton: HTMLButtonElement } }).__scrollFixture = {
        container,
        controller,
        jumpButton,
      };
      controller.update(true);
    });

    await page.waitForFunction(() => {
      const fixture = (window as Window & { __scrollFixture?: { container: HTMLDivElement } }).__scrollFixture;
      return fixture && fixture.container.scrollTop > 0;
    });

    await page.evaluate(() => {
      const fixture = (window as Window & {
        __scrollFixture: { container: HTMLDivElement; jumpButton: HTMLButtonElement };
      }).__scrollFixture;
      fixture.container.scrollTop = 0;
      fixture.container.dispatchEvent(new Event('scroll'));
    });

    await page.waitForFunction(() => {
      const fixture = (window as Window & {
        __scrollFixture?: { jumpButton: HTMLButtonElement };
      }).__scrollFixture;
      return fixture && !fixture.jumpButton.hidden;
    });

    await page.evaluate(() => {
      const fixture = (window as Window & {
        __scrollFixture: { jumpButton: HTMLButtonElement };
      }).__scrollFixture;
      fixture.jumpButton.click();
    });

    await page.waitForFunction(() => {
      const fixture = (window as Window & { __scrollFixture?: { container: HTMLDivElement } }).__scrollFixture;
      return fixture &&
        fixture.container.scrollTop + fixture.container.clientHeight >= fixture.container.scrollHeight - 1;
    });
  });

  test('does not scroll when no response is streaming', async ({ page }) => {
    await page.goto('/chat');

    const scrollTop = await page.evaluate(async () => {
      const fixture = document.createElement('div');
      const container = document.createElement('div');
      const sentinel = document.createElement('div');
      const jumpButton = document.createElement('button');

      container.style.cssText = 'height: 100px; overflow-y: auto;';
      container.innerHTML = '<div style="height: 1000px"></div>';
      container.append(sentinel);
      fixture.append(container, jumpButton);
      document.body.append(fixture);

      const module = await import('/Components/Pages/Chat.razor.js');
      const controller = module.initialize(container, sentinel, jumpButton);
      if (!controller) throw new Error('Scroll controller did not initialize');

      controller.update(false);
      container.scrollTop = 0;
      container.firstElementChild?.setAttribute('style', 'height: 1200px');
      await new Promise(requestAnimationFrame);

      return container.scrollTop;
    });

    expect(scrollTop).toBe(0);
  });
});
