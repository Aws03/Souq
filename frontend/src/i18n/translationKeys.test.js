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
//
// ── والجمع استثناء حقيقي لا ثغرة ──────────────────────────────────────────
// عدد صيغ الجمع خاصّية لغة لا خيار مترجم: الإنجليزية صيغتان (one/other) والعربية ستّ
// (zero/one/two/few/many/other) بقاعدة CLDR. فاشتراط تطابق المفاتيح حرفياً يعني إمّا
// "منتج(ات)" في العربية أو "1 products" في الإنجليزية — كلاهما نصّ لا يكتبه ناطق.
//
// لذلك التطابق يُقاس على المفتاح الأساس (بلا لاحقة الصيغة)، ويُضاف إليه شرطان أضيق لا
// أوسع: مفتاح جمعٌ في إحداهما جمعٌ في الأخرى (لا نصّ ثابت مقابل جمع)، وكل لغة تحمل كل
// الصيغ التي تفرضها قاعدتها هي — فالعربية بـone/other وحدهما تُخرج "3 منتج".
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

// لواحق الجمع في i18next. القائمة مغلقة كي لا يُقرأ مفتاحٌ عادي ينتهي بـ"_one" كصيغة جمع.
const PLURAL_FORMS = ['zero', 'one', 'two', 'few', 'many', 'other'];
// ما تفرضه قاعدة كل لغة فعلاً. الإنجليزية صيغتان، والعربية خمس (الصفر اختياري: رسائلنا
// التي تعدّ لا تُعرض أصلاً عند الصفر).
const EN_FORMS = ['one', 'other'];
const AR_FORMS = ['one', 'two', 'few', 'many', 'other'];

const formSuffix = (key) => PLURAL_FORMS.find((form) => key.endsWith(`_${form}`)) ?? null;
const baseKey = (key) => {
  const suffix = formSuffix(key);
  return suffix ? key.slice(0, -(suffix.length + 1)) : key;
};
const pluralBases = (bundle) =>
  new Set(flatten(bundle).filter(formSuffix).map(baseKey));

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
    // الشيفرة تكتب المفتاح الأساس (t('x.y', { count })) وi18next يختار الصيغة.
    const available = new Set(flatten(en).map(baseKey));
    expect(usedKeys.filter((key) => !available.has(key))).toEqual([]);
  });

  it('كل مفتاح مستعمل موجود في العربية', () => {
    const available = new Set(flatten(ar).map(baseKey));
    expect(usedKeys.filter((key) => !available.has(key))).toEqual([]);
  });

  it('اللغتان متطابقتا البنية', () => {
    const english = new Set(flatten(en).map(baseKey));
    const arabic = new Set(flatten(ar).map(baseKey));
    expect([...english].filter((key) => !arabic.has(key))).toEqual([]);
    expect([...arabic].filter((key) => !english.has(key))).toEqual([]);
  });

  it('مفتاح الجمع جمعٌ في اللغتين معاً', () => {
    const enPlural = pluralBases(en);
    const arPlural = pluralBases(ar);
    expect([...enPlural].filter((key) => !arPlural.has(key))).toEqual([]);
    expect([...arPlural].filter((key) => !enPlural.has(key))).toEqual([]);
  });

  it('كل لغة تحمل الصيغ التي تفرضها قاعدتها', () => {
    const missing = [];
    for (const { language, bundle, required } of [
      { language: 'en', bundle: en, required: EN_FORMS },
      { language: 'ar', bundle: ar, required: AR_FORMS },
    ]) {
      const forms = new Map();
      for (const key of flatten(bundle)) {
        const suffix = formSuffix(key);
        if (suffix) {
          if (!forms.has(baseKey(key))) forms.set(baseKey(key), new Set());
          forms.get(baseKey(key)).add(suffix);
        }
      }
      for (const [base, present] of forms) {
        for (const form of required) {
          if (!present.has(form)) missing.push(`${language}: ${base}_${form}`);
        }
      }
    }
    expect(missing).toEqual([]);
  });
});
