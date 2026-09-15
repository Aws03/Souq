import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { PRIVATE_ROUTES } from './pageMetadata';

// ============================================================================
// وسم <meta name="robots"> لا يراه إلّا زاحف ينفّذ JavaScript، وهذا التطبيق يُعرَض في
// المتصفّح — فـrobots.txt هو الضابط الوحيد الذي يعمل بلا تنفيذ. وجود ضابطين يعني احتمال
// افتراقهما: مسار خاصّ جديد يُضاف إلى PRIVATE_ROUTES ويُنسى في الملفّ، فيُفهرَس بهدوء.
// ============================================================================
const robots = readFileSync(
  resolve(dirname(fileURLToPath(import.meta.url)), '../../public/robots.txt'), 'utf8');

const disallowed = robots.split('\n')
  .map((line) => line.trim())
  .filter((line) => line.startsWith('Disallow:'))
  .map((line) => line.slice('Disallow:'.length).trim());

describe('robots.txt', () => {
  it('يمنع كل مسار خاصّ تعرفه الواجهة', () => {
    expect(PRIVATE_ROUTES.filter((route) => !disallowed.includes(route))).toEqual([]);
  });

  it('يمنع منطقتي الإدارة والمنصّة', () => {
    expect(disallowed).toContain('/admin');
    expect(disallowed).toContain('/platform');
  });

  it('لا يمنع صفحات المتجر العامة', () => {
    for (const path of ['/', '/offers', '/products/blue-shirt']) {
      expect(disallowed.some((rule) => rule === path)).toBe(false);
    }
  });

  it('لا يشير إلى خريطة موقع غير موجودة', () => {
    // سطر Sitemap إلى ملفّ يردّ 404 يُبلَّغ عنه في أدوات المشرفين ويُقرأ إهمالاً.
    expect(robots).not.toMatch(/^Sitemap:/m);
  });

  it('لا يحمل اسم متجر بعينه — ملفّ واحد لكل المضيفين', () => {
    expect(robots).not.toMatch(/https?:\/\//);
  });
});
