import js from '@eslint/js';
import globals from 'globals';
import react from 'eslint-plugin-react';
import reactHooks from 'eslint-plugin-react-hooks';
import jsxA11y from 'eslint-plugin-jsx-a11y';

// ============================================================================
// لم يكن للواجهة مدقّق إطلاقاً حتى المرحلة 16 — فكان استيرادٌ لا يُستعمل أو تبعيةُ Hook قديمة
// تعيش في الشجرة بلا أن يشتكي أحد؛ البناء ينجح، والاختبارات لا تلمسها.
//
// القواعد المفعّلة هنا ثلاث فئات فقط، وكلها أخطاء سلوك لا ذوق:
//   • react-hooks — إغلاقات قديمة وتبعيات ناقصة (سبب صنف كامل من "لماذا تعرض بيانات سابقة؟").
//   • jsx-a11y — ما يمكن للأداة إثباته من إتاحة: بديل الصورة، زرّ بلا اسم، تسمية حقل.
//   • الأساسيات — متغيّر غير معرّف، استيراد ميّت، شرط لا يتغيّر.
// التنسيق (فواصل، مسافات، اقتباسات) مقصود تركه: مدقّق يشتكي من الفواصل يُتجاهَل خلال أسبوع.
// ============================================================================
export default [
  { ignores: ['dist/**', 'node_modules/**', 'coverage/**'] },

  // ── سكربتات Node داخل الواجهة ────────────────────────────────────────────
  // `scripts/bundle-budget.mjs` يعمل في Node لا في متصفّح، وكانت بيئته تُقرأ متصفّحاً — فيسقط
  // `npm run lint` بأربعة عشر خطأ `no-undef` على `process` و`console`. أي أنّ **مهمّة الواجهة في
  // الخطّ كانت حمراء منذ M16**، ولم يرها أحد لأنّ الخطّ نفسه لم يكن يعمل (TD-31). وُجد في M18
  // حين شُغّلت البوّابة محلّياً.
  //
  // والعالميان معاً مقصودان: الملفّ نصّه Node، وما بداخل `page.evaluate` يعمل في المتصفّح
  // فيذكر `localStorage` و`performance` لفظاً وإن لم ينفّذهما Node.
  {
    files: ['scripts/**/*.mjs'],
    languageOptions: { globals: { ...globals.node, ...globals.browser } },
  },

  js.configs.recommended,

  {
    files: ['**/*.{js,jsx}'],
    languageOptions: {
      ecmaVersion: 2023,
      sourceType: 'module',
      globals: { ...globals.browser, ...globals.es2021 },
      parserOptions: { ecmaFeatures: { jsx: true } },
    },
    settings: { react: { version: 'detect' } },
    plugins: { react, 'react-hooks': reactHooks, 'jsx-a11y': jsxA11y },
    rules: {
      ...react.configs.flat.recommended.rules,
      ...react.configs.flat['jsx-runtime'].rules,   // لا حاجة لاستيراد React مع vite
      ...reactHooks.configs.recommended.rules,
      ...jsxA11y.flatConfigs.recommended.rules,

      // الأنواع تُفرض بـ checkJs وJSDoc على الحدود (ADR-0037)، لا بـ prop-types.
      'react/prop-types': 'off',

      // SEC-UP-07 كان "لا شيء يمنعه تلقائياً": أوصاف المنتجات نصّ صِرف يعرضه React مهرَّباً،
      // والضمانة الوحيدة كانت ألّا يكتب أحد dangerouslySetInnerHTML. الآن يمنعه المدقّق.
      // وصفٌ غنيّ يوماً ما يحتاج منقّياً على الخادم وقراراً، لا استثناءً هنا.
      'react/no-danger': 'error',

      'no-unused-vars': ['error', { argsIgnorePattern: '^_', varsIgnorePattern: '^_' }],

      // ليست في recommended، وكان في الشجرة موضع حقيقي تُستعمل فيه t قبل تعريفها بسطر
      // (صفحة المفضّلة): const في منطقة الموت المؤقّت ⇒ ReferenceError وقت التشغيل وحده.
      // البناء يمرّ، والاختبارات لا تلمس الصفحة، والزائر يرى حدّ الأخطاء.
      'no-use-before-define': ['error', { functions: false, classes: true, variables: true }],
      'react-hooks/exhaustive-deps': 'warn',

      // تحذير لا خطأ، ومؤقّت: النمط الذي يشتكي منه (setState داخل effect قبل جلب البيانات) هو
      // بالضبط ما تستبدله طبقة الاستعلام (ADR-0037 / TD-23). رفعه خطأً قبل الهجرة يعني 27 تعطيلاً
      // موضعياً تُنسى بعدها؛ التحذير يبقى مرئياً ويتناقص مع كل شاشة تُهاجَر.
      'react-hooks/set-state-in-effect': 'warn',

      // التسمية الملتفّة حول حقلها (label > input + span) تسمية صحيحة ومستعملة هنا،
      // والقاعدة افتراضياً لا تنظر إلا مباشرةً تحت <label>.
      'jsx-a11y/label-has-associated-control': ['error', { depth: 3 }],

      // منطقة تُمرَّر أفقياً (جدول لوحة الإدارة على الهاتف) **يجب** أن تكون قابلة للتبئير، وإلا
      // تعذّر تمريرها بلوحة المفاتيح في كروم وسفاري — WCAG 2.1.1، وهو ما تسمّيه axe
      // scrollable-region-focusable وقِيس فعلاً على /admin/reviews بعرض Pixel 7. القاعدة
      // افتراضياً تمنع tabIndex على غير التفاعلي، ولها لهذا خيار أدوار: `region` وحده يُستثنى،
      // باسمٍ إلزامي (aria-label) — لا تعطيلٌ موضعي يُنسخ إلى كل غلافٍ لاحق.
      'jsx-a11y/no-noninteractive-tabindex': ['error', {
        tags: [], roles: ['region'], allowExpressionValues: true,
      }],
      eqeqeq: ['error', 'always', { null: 'ignore' }],  // == null تعني "غائب" عمداً في هذه الشيفرة
      'no-console': ['error', { allow: ['warn', 'error'] }],
    },
  },

  // ملفات الاختبار: بيئة Node وأدوات الاختبار، وnode: مسموح في فحوص المصدر.
  {
    files: ['**/*.test.{js,jsx}', 'src/test/**', 'e2e/**', 'vitest.config.js', 'vite.config.js',
      'eslint.config.js', 'playwright.config.js'],
    languageOptions: { globals: { ...globals.node } },
    // تشخيص تحقّق المتصفّح يُطبع عمداً: الأثر عند السقوط هو المعلومة كلّها.
    rules: { 'no-console': 'off', 'no-empty-pattern': 'off' },
  },
];
