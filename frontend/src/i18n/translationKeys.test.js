import { describe, it, expect } from 'vitest';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
import ar from './locales/ar.json';
import en from './locales/en.json';

// ============================================================================
// مفتاح ترجمة مفقود لا يرمي خطأً — يعرض اسم المفتاح نفسه للزبون ("orders.title" وسط
// الصفحة). عطل صامت بامتياز: يمرّ البناء، وتمرّ الاختبارات، ويراه المستخدم وحده.
// حدث فعلاً أثناء المرحلة 16 (مفتاحان مخترعان في بيانات الصفحة الوصفية).
//
// الفحص مزدوج: كل مفتاح مستعمل موجود في اللغتين، واللغتان متطابقتا البنية — ترجمة
// ناقصة في إحداهما تعني زبوناً يرى الإنجليزية داخل متجر عربي.
// ============================================================================
const SOURCE_ROOT = new URL('..', import.meta.url).pathname;

function sourceFiles(dir) {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) return sourceFiles(full);
    return /\.(jsx?|tsx?)$/.test(entry) && !entry.includes('.test.') ? [full] : [];
  });
}

function flatten(object, prefix = '') {
  return Object.entries(object).flatMap(([key, value]) => {
    const path = prefix ? `${prefix}.${key}` : key;
    return value && typeof value === 'object' && !Array.isArray(value) ? flatten(value, path) : [path];
  });
}

const usedKeys = [...new Set(
  sourceFiles(SOURCE_ROOT)
    .flatMap((file) => [...readFileSync(file, 'utf8').matchAll(/\bt\(\s*'([a-zA-Z0-9_.]+)'/g)]
      .map((match) => match[1]))
)];

describe('translation keys', () => {
  it('يُعثر على مفاتيح مستعملة أصلاً', () => {
    expect(usedKeys.length).toBeGreaterThan(50);
  });

  it('كل مفتاح مستعمل موجود في الإنجليزية', () => {
    const available = new Set(flatten(en));
    expect(usedKeys.filter((key) => !available.has(key))).toEqual([]);
  });

  it('كل مفتاح مستعمل موجود في العربية', () => {
    const available = new Set(flatten(ar));
    expect(usedKeys.filter((key) => !available.has(key))).toEqual([]);
  });

  it('اللغتان متطابقتا البنية', () => {
    const english = new Set(flatten(en));
    const arabic = new Set(flatten(ar));
    expect([...english].filter((key) => !arabic.has(key))).toEqual([]);
    expect([...arabic].filter((key) => !english.has(key))).toEqual([]);
  });
});
