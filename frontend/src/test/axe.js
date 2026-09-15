import axe from 'axe-core';

// ============================================================================
// فحص إتاحة آليّ داخل مجموعة الاختبارات (المرحلة 16).
//
// axe-core مُرخَّص MPL-2.0 لا MIT. هذا مقبول هنا لأنه اعتماد تطوير لا يُشحن ولا يُعدَّل:
// MPL نسخٌ ضعيف على مستوى الملفّ، ولا ملفّ منه يدخل حزمة الزبون. سُجّل صراحةً لأن هذا
// المستودع لُدغ مرّتين من تغيّر الرخص (P-02، وMediatR المثبَّت على 12).
//
// ما لا يفعله هذا الفحص — والصمت عنه أسوأ من غيابه:
//   • تباين الألوان: jsdom بلا تخطيط ولا ألوان محسوبة، فالقاعدة تُعطَّل هنا وتُفحص في متصفّح.
//   • ترتيب التركيز الفعلي وقارئ الشاشة: لا يُحاكيان. هذا فحص بنية لا تجربة.
// أي انتهاك يجده حقيقيّ؛ وخلوّه لا يعني أن الصفحة مُتاحة.
// ============================================================================
const RULES_NEEDING_A_REAL_BROWSER = ['color-contrast'];

export async function expectNoViolations(container) {
  const results = await axe.run(container, {
    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] },
    rules: Object.fromEntries(RULES_NEEDING_A_REAL_BROWSER.map((id) => [id, { enabled: false }])),
  });

  // رسالة تقول ما العيب وأين، لا "expected 3 to be 0".
  const described = results.violations.map((v) =>
    `${v.id} (${v.impact}): ${v.help}\n  ${v.nodes.map((n) => n.html).join('\n  ')}`);
  return described;
}
