// ============================================================================
// ميزانية حجم الحزمة الأولى (M16) — الالتزام الذي سجّله DesignSystem.md بوصفه مؤجّلاً إلى هذه المرحلة
// ("No budget is enforced in CI; bundle-size budgets are PLANNED for Phase 21").
//
// **يُقاس ما يُنزّله المتصفّح فعلاً، لا مجموع ملفّات `dist`.** جمع الملفّات يعطي رقماً لا يراه أحد: الحزمة
// مقسّمة، ولوحة الإدارة وشاشات المنصّة تُحمَّل كسولاً ولا تدخل أول رسم. الرقم الذي يهمّ هو ما يعبر الشبكة
// في تحميلٍ بارد لواجهة المتجر — وهو ما يقيسه هذا السكربت بفتح الصفحة فعلاً وجمع `encodedBodySize`
// (أي بعد الضغط) لكل مستند وسكربت ونمط.
//
// وهو نفس المنهج الذي وثّقه DesignSystem.md لقياساته السابقة (136.5 kB بالعربية، 134.2 بالإنجليزية)،
// فالأرقام تبقى قابلة للمقارنة به بدل أن تبدأ سلسلة جديدة لا تُقاس على ما قبلها.
//
// **واللغتان تُقاسان كلتاهما**: حزمة الترجمة تُستورد ديناميكياً بلغة الزائر وحدها، فلغةٌ واحدة تُخفي نموّ
// الأخرى. والعربية أكبر، فهي التي تُقاس عليها الميزانية.
//
//   node scripts/bundle-budget.mjs [--url http://localhost:4173] [--budget-kb 170]
// ============================================================================
import { chromium } from '@playwright/test';

const args = Object.fromEntries(
  process.argv.slice(2).flatMap((a, i, all) => (a.startsWith('--') ? [[a.slice(2), all[i + 1]]] : [])),
);

const url = args.url ?? 'http://localhost:4173';
// ── الميزانية ──
// مقيسة لا مختارة: أول رسمٍ يزن **156.8 kB** مضغوطة بالعربية و152.7 بالإنجليزية (M16). وسقف 170 يترك
// نحو 8% متّسعاً — ضيّقٌ عمداً، لأنّ ما يُمسَك هنا قفزةٌ في الرتبة لا زيادةُ ميزة: مكتبة رسوم بيانية
// (نحو 90 kB وحدها) أو حزمة ترجمة عادت إلى الاستيراد الساكن. وهما بالضبط الحادثتان اللتان سجّلهما
// DesignSystem.md بوصفهما ما يجب ألّا يتكرّر.
//
// وكان آخر قياسٍ مسجَّل 136.5 kB، أي أنّ أول الرسم نما نحو عشرين كيلوبايت عبر المراحل التي تلته (بحث
// M3 بشريطه واقتراحاته، وشارات M10، وألسنة M13). لا مُذنب واحد فيها، وهذا تحديداً ما تمنعه الميزانية:
// نموّاً لا يلاحظه أحد لأنّ كل خطوة منه صغيرة.
const budgetKb = Number(args['budget-kb'] ?? 170);

async function measure(language) {
  const browser = await chromium.launch();
  const context = await browser.newContext();
  const page = await context.newPage();

  // اللغة تُثبَّت قبل أي سكربت: حزمة الترجمة تُختار عند الإقلاع، فضبطها بعده يقيس تحميلاً ثانياً.
  await context.addInitScript((lang) => {
    try { localStorage.setItem('souq_lang', lang); localStorage.setItem('i18nextLng', lang); } catch { /* ignore */ }
  }, language);

  const seen = new Map();
  page.on('response', (response) => {
    const type = response.request().resourceType();
    if (!['document', 'script', 'stylesheet'].includes(type)) return;
    seen.set(response.url(), { type, status: response.status() });
  });

  await page.goto(url, { waitUntil: 'load' });
  await page.waitForTimeout(2500);   // للاستيراد الديناميكي بعد الإقلاع (الترجمة، السمة)

  // الحجم المنقول من الصفحة نفسها: `encodedBodySize` هو ما عبر الشبكة بعد الضغط.
  const bytes = await page.evaluate(() =>
    performance.getEntriesByType('resource')
      .filter((e) => ['script', 'css', 'link', 'fetch', 'other'].includes(e.initiatorType) || e.name.match(/\.(js|css)(\?|$)/))
      .reduce((total, e) => total + (e.encodedBodySize || 0), 0));

  const documentBytes = await page.evaluate(() => {
    const nav = performance.getEntriesByType('navigation')[0];
    return nav ? nav.encodedBodySize || 0 : 0;
  });

  await browser.close();
  return { language, kb: (bytes + documentBytes) / 1024, files: seen.size };
}

const results = [];
for (const language of ['ar', 'en']) results.push(await measure(language));

console.log(`First load, measured from the browser at ${url}`);
console.log(`${'language'.padEnd(10)}${'gzipped KB'.padStart(13)}${'requests'.padStart(11)}`);
for (const r of results) {
  console.log(`${r.language.padEnd(10)}${r.kb.toFixed(1).padStart(13)}${String(r.files).padStart(11)}`);
}

const worst = results.reduce((a, b) => (a.kb > b.kb ? a : b));
console.log(`\nbudget: ${budgetKb} KB gzipped — worst measured: ${worst.kb.toFixed(1)} KB (${worst.language})`);

if (worst.kb > budgetKb) {
  console.error(`\nOVER BUDGET by ${(worst.kb - budgetKb).toFixed(1)} KB.`);
  console.error('A first load is what every visitor pays for. If the growth is deliberate, raise the budget');
  console.error('in this file with the reason, rather than leaving the number silently untrue.');
  process.exit(1);
}
console.log('within budget.');
