import { test, expect } from '@playwright/test';

test.describe('Admin Dashboard UI Controls', () => {
  test.beforeEach(async ({ page }) => {
    // Navigate to admin interface (simulated local route)
    await page.goto('http://localhost:3000/admin');
  });

  test('Navigation controls switch sections properly', async ({ page }) => {
    // Test navigation links
    const navLinks = ['Dashboard', 'Build Central', 'Log Viewer', 'Users', 'Settings'];
    for (const linkText of navLinks) {
      const link = page.getByRole('link', { name: linkText });
      await expect(link).toBeVisible();
      await link.click();
      
      // Ensure the section changed
      const heading = page.getByRole('heading', { level: 2 });
      await expect(heading).toContainText(linkText);
    }
  });

  test('Build trigger buttons execute builds and show loaders', async ({ page }) => {
    await page.click('text=Build Central');
    
    // Positive path: Click build button
    const buildWpfBtn = page.getByRole('button', { name: 'Build WPF Application' });
    await expect(buildWpfBtn).toBeEnabled();
    await buildWpfBtn.click();
    
    // State change: Should show "Building..." and be disabled
    await expect(buildWpfBtn).toBeDisabled();
    await expect(buildWpfBtn).toHaveText(/Building.../);
  });

  test('Server release interactions', async ({ page }) => {
    await page.click('text=Settings');
    
    // Form input validation (Negative path: Empty Version)
    const versionInput = page.getByPlaceholder('e.g., v3.5.0');
    await versionInput.fill('');
    const publishBtn = page.getByRole('button', { name: 'Publish Update' });
    await publishBtn.click();
    
    // Error handling message
    await expect(page.locator('.error-message')).toBeVisible();
    await expect(page.locator('.error-message')).toHaveText('Version string is required.');
    
    // Positive path
    await versionInput.fill('v4.0.0');
    await publishBtn.click();
    await expect(page.locator('.success-message')).toBeVisible();
  });
  
  test('Accessibility and responsive checks', async ({ page }) => {
    // Check keyboard navigation
    await page.keyboard.press('Tab');
    
    // Set viewport to mobile size
    await page.setViewportSize({ width: 375, height: 812 });
    
    // Check hamburger menu button appears
    const menuBtn = page.getByRole('button', { name: 'Toggle Menu' });
    await expect(menuBtn).toBeVisible();
  });
});
