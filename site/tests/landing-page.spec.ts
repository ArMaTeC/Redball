import { test, expect } from '@playwright/test';

test.describe('Landing Page UI Controls', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:3000/');
  });

  test('Hero section call to action downloads exist', async ({ page }) => {
    const downloadBtn = page.getByRole('link', { name: /Download Redball/i });
    await expect(downloadBtn).toBeVisible();
    await expect(downloadBtn).toHaveAttribute('href', /#download/);
  });

  test('Feature toggles and state changes work', async ({ page }) => {
    // Scroll to features
    await page.locator('#features').scrollIntoViewIfNeeded();

    // Check tabs if we have any interactable UI like tabbed feature displays
    const typeThingTab = page.locator('button:has-text("TypeThing")');
    if (await typeThingTab.isVisible()) {
      await typeThingTab.click();
      await expect(typeThingTab).toHaveAttribute('aria-selected', 'true');
    }
  });

  test('Dark mode toggle functionality', async ({ page }) => {
    // If the site has a dark mode toggle control
    const themeToggle = page.locator('button[aria-label="Toggle dark mode"]');
    if (await themeToggle.isVisible()) {
      await themeToggle.click();
      // Check for adding a dark class on html or body
      await expect(page.locator('html')).toHaveClass(/dark/);
    }
  });
});
