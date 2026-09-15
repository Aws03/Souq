// ============================================================================
// مظهر حقل بطاقة Stripe من هوية المتجر (المرحلة 16) — منطق خالص مُختبَر.
//
// حقل البطاقة يعيش في إطار Stripe: لا يرث أنماط صفحتنا ولا خطّها، ولا يقبل var(--x) — يريد
// قيماً صريحة. فكان مكتوباً بلون وخطّ القالب الافتراضي، فيظهر لمتجر غيّر خطّه وألوانه حقلٌ
// واحد بهوية متجر آخر، في أكثر خطوة تحتاج ثقة. هنا تُقرأ القيم المحسوبة من نفس متغيّرات
// التصميم التي يكتبها applyStoreTheme، وتُمرَّر إلى Stripe كنصّ.
//
// القراءة تُمرَّر دالةً (read) لا تُنفَّذ هنا: الدالة تبقى قابلة للاختبار بلا متصفّح.
// ============================================================================
const FALLBACK = { text: '#111827', muted: '#5B616B', danger: '#C4674E', font: 'sans-serif' };

// var() غير محسوبة تصل الإطار كنصّ لا يفهمه ⇒ حقل بلا لون. القيمة الاحتياطية أوضح من لا شيء.
const value = (read, name, fallback) => {
  const raw = typeof read(name) === 'string' ? read(name).trim() : '';
  return raw && !raw.includes('var(') ? raw : fallback;
};

export function cardAppearance(read) {
  return {
    style: {
      base: {
        fontSize: '15px',
        fontFamily: value(read, '--font-body', FALLBACK.font),
        color: value(read, '--color-text', FALLBACK.text),
        '::placeholder': { color: value(read, '--color-text-muted', FALLBACK.muted) },
      },
      invalid: { color: value(read, '--color-danger', FALLBACK.danger) },
    },
  };
}

// الخطّ نفسه يجب أن يُحمَّل داخل إطار Stripe، وإلا سقط إلى خطّ النظام مهما سمّيناه.
export const cardFonts = (stylesheetUrl) => (stylesheetUrl ? [{ cssSrc: stylesheetUrl }] : undefined);

export const readCssVariable = (name) =>
  getComputedStyle(document.documentElement).getPropertyValue(name);
