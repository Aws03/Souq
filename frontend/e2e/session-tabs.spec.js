import { expect, test } from '@playwright/test';

// ============================================================================
// تشخيص: هل يستطيع زبون حقيقي أن يُخرج نفسه من جلسته؟
//
// الخادم يعتبر تجديدين برمز التجديد نفسه إعادةَ استخدام (سرقة) فيُبطل العائلة كاملة — وهذا
// سلوك صحيح ومقصود (ADR-0023). السؤال هو ما إذا كانت الواجهة تنتج ذلك الشكل بلا نيّة.
// client.js يمنع تجديدين متوازيين داخل صفحة واحدة (وعد مشترك)، لكن الحارس يعيش في نسخة
// الوحدة — وتبويبان من المتجر نفسه نسختان.
// ============================================================================
test('two tabs of the same store, opened together, must not sign the customer out', async ({ browser }) => {
  const context = await browser.newContext();
  const email = `tabs+${Date.now()}@souq.test`;

  const first = await context.newPage();
  await first.goto('/register');
  await first.getByRole('textbox').nth(0).fill('Two Tabs');
  await first.getByRole('textbox').nth(1).fill(email);
  const passwords = first.locator('input[type="password"]');
  await passwords.nth(0).fill('Customer@12345');
  await passwords.nth(1).fill('Customer@12345');
  await first.getByRole('button', { name: /create account/i }).click();
  await expect(first).toHaveURL(/\/$|\/account/);

  // تبويبان يُفتحان معاً — كلاهما يُقلع ويطلب تجديداً بالرمز نفسه.
  const [a, b] = [await context.newPage(), await context.newPage()];
  await Promise.all([a.goto('/account'), b.goto('/account')]);

  await expect(a.getByRole('navigation').filter({ has: a.locator('a[href="/account/addresses"]') })).toBeVisible();
  await expect(b.getByRole('navigation').filter({ has: b.locator('a[href="/account/addresses"]') })).toBeVisible();

  await context.close();
});
