import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, extname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

// ============================================================================
// ثوابت على مستوى الشيفرة المصدرية، كُتبت بعد عطل حقيقي: كان ملفّا صفحتين — "طلباتي"
// (/orders) وتتبّع الطلب العامّ (/track/:token) — فارغين تماماً في المستودع منذ المرحلة 9،
// ومسارَاهما موصولين في App.jsx. وحدة فارغة تُبنى بلا خطأ، وVite لا يشكو، والاختبارات لا
// تلمسها — فيسقط المسار وقت التشغيل وحده ("Element type is invalid") أمام العميل.
//
// هذان الفحصان رخيصان ويمسكان الصنف كلّه: ملفّ مصدر فارغ، ووحدة يحمّلها lazy() بلا تصدير
// افتراضي (وlazy تشترطه صراحةً).
// ============================================================================
const SRC = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const SOURCE_EXTENSIONS = new Set(['.js', '.jsx', '.css', '.json']);

function sourceFiles(directory = SRC) {
  return readdirSync(directory).flatMap((entry) => {
    const path = join(directory, entry);
    if (statSync(path).isDirectory()) return sourceFiles(path);
    return SOURCE_EXTENSIONS.has(extname(entry)) ? [path] : [];
  });
}

const files = sourceFiles();
const relative = (path) => path.slice(SRC.length + 1);

describe('ثوابت وحدات الواجهة', () => {
  it('لا ملفّ مصدر فارغ', () => {
    const empty = files.filter((path) => readFileSync(path, 'utf8').trim() === '').map(relative);
    expect(empty).toEqual([]);
  });

  it('كل وحدة يحمّلها lazy() لها تصدير افتراضي', () => {
    const missing = [];

    for (const path of files.filter((f) => extname(f) === '.jsx' || extname(f) === '.js')) {
      const source = readFileSync(path, 'utf8');
      for (const [, specifier] of source.matchAll(/lazy\(\s*\(\)\s*=>\s*import\(\s*'([^']+)'/g)) {
        const target = resolveModule(dirname(path), specifier);
        if (target === null) { missing.push(`${relative(path)} → ${specifier} (الملف غير موجود)`); continue; }
        if (!/export\s+default\b/.test(readFileSync(target, 'utf8'))) {
          missing.push(`${relative(path)} → ${specifier} (بلا تصدير افتراضي)`);
        }
      }
    }

    expect(missing).toEqual([]);
  });
});

// نفس ما يفعله Vite: المسار كما كُتب، وإلا بامتداد .jsx ثم .js.
function resolveModule(fromDirectory, specifier) {
  for (const candidate of ['', '.jsx', '.js']) {
    const path = resolve(fromDirectory, specifier + candidate);
    try { if (statSync(path).isFile()) return path; } catch { /* المرشّح التالي */ }
  }
  return null;
}
