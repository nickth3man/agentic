import { expect, test, type Page } from '@playwright/test';

async function selectReasoningModel(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Select model' }).click();
  await page.getByRole('searchbox', { name: 'Search models' }).fill('Test Reasoner');
  await page.locator('.model-picker-row').filter({ hasText: 'Test Reasoner' }).click();
  await expect(page.getByRole('button', { name: 'Select model' })).toContainText('Test Reasoner');
}

async function sendMessage(page: Page, message: string): Promise<void> {
  await page.getByRole('textbox', { name: 'Message' }).fill(message);
  await page.getByRole('button', { name: 'Send message' }).click();
}

test.describe('Chat with fake OpenRouter SSE', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/chat');
    await page.getByRole('button', { name: 'New chat', exact: true }).last().click();
    await expect(page.locator('.chat-bubble')).toHaveCount(0);
    await selectReasoningModel(page);
  });

  test('streams reasoning and content into the transcript', async ({ page }) => {
    await sendMessage(page, 'first turn');

    const assistant = page.locator('.chat-bubble.assistant').last();
    await expect(assistant.locator('.thinking-panel')).toBeVisible();
    await expect(assistant.locator('.thinking-text')).toContainText('I will reason through this.');
    await expect(assistant.locator('.markdown-body')).toContainText('Streamed answer.');
    await expect(assistant).not.toHaveClass(/is-streaming/);
    await expect(assistant.locator('.thinking-panel')).not.toHaveClass(/is-active/);
    await expect(assistant.locator('.think-pulse')).toHaveCount(0);
    await expect(page.locator('.chat-bubble.user')).toHaveCount(1);
    await expect(page.locator('.chat-bubble.assistant')).toHaveCount(1);
  });

  test('preserves prior turns for the next request', async ({ page }) => {
    await sendMessage(page, 'first turn');
    await expect(page.locator('.chat-bubble.assistant').last()).not.toHaveClass(/is-streaming/);

    await sendMessage(page, 'second turn');

    await expect(page.locator('.chat-bubble.user')).toHaveCount(2);
    await expect(page.locator('.chat-bubble.assistant')).toHaveCount(2);
    await expect(page.locator('.chat-bubble.assistant').last()).toContainText('Streamed answer.');
  });

  test('shows a rate-limit error and streams a later turn', async ({ page }) => {
    await sendMessage(page, 'trigger rate limit');

    await expect(page.locator('.chat-bubble.assistant').last()).toContainText(
      'Error 429: {"error":{"message":"Fake rate limit for E2E test"}}',
    );
    await expect(page.getByRole('button', { name: 'Retry last message' })).toBeVisible();

    await sendMessage(page, 'recovery turn');

    const recoveredAssistant = page.locator('.chat-bubble.assistant').last();
    await expect(recoveredAssistant).toContainText('Streamed answer.');
    await expect(recoveredAssistant).not.toHaveClass(/is-error/);
    await expect(page.locator('.chat-bubble.user')).toHaveCount(2);
  });
});
