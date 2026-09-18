import { useRef } from 'react';
import styles from './Tabs.module.css';

// ============================================================================
// ألسنة تبويب (M13) — بنمط ARIA الكامل، لأنّ نصفه لا يعمل.
//
// **لماذا مكوّن لا زرّان؟** لأنّ التبويب الصحيح ليس مظهراً: هو `tablist`/`tab`/`tabpanel` مترابطة
// بـ `aria-controls`/`aria-labelledby`، و`aria-selected`، و**tabindex متجوّل** — لسانٌ واحد في تسلسل
// الجدولة والأسهم تنقل بين الألسنة. زرّان مُنسَّقان يبدوان تبويباً ولا يُقرأان تبويباً، ومن يتنقّل بلوحة
// المفاتيح يجدُ Tab يمرّ على كل لسانٍ ثمّ لا يعرف أنّ ثمّة لوحاً تحته.
//
// ولمّا كان هذا المنطق هو المكوّن كلّه، فموضعه واحد: هنا. نسختان منه في شاشتين تفترقان في الرابع.
//
// الاتجاه: الأسهم تتبع اتجاه القراءة — في RTL يمضي السهم الأيسر إلى الأمام. تُقرأ من `dir` المحسوب على
// العنصر نفسه لا من لغة الواجهة، فالصواب يبقى صواباً في قسمٍ مقلوبٍ داخل صفحة.
// ============================================================================

/**
 * `base` يملكه المستدعي (`useId()` في الصفحة) لا هذا المكوّن: اللوح يحتاج المعرّف نفسه ليربط
 * `aria-labelledby` به، ومعرّفٌ مخفيّ داخل الألسنة لا يصل إليه.
 *
 * @param {{
 *   tabs: Array<{id: string, label: string, badge?: string|number}>,
 *   active: string,
 *   onChange: (id: string) => void,
 *   label: string,
 *   base: string,
 *   className?: string,
 * }} props
 */
export default function Tabs({ tabs, active, onChange, label, base, className }) {
  const listRef = useRef(/** @type {HTMLDivElement|null} */(null));

  const tabId = (id) => `${base}-tab-${id}`;

  // المُعالج على اللسان نفسه لا على الحاوية: `tablist` ليس عنصراً يُركَّز عليه، فمعالجُ لوحة مفاتيحٍ
  // عليه يعتمد على تصاعد الحدث من ابنٍ مركَّزٍ عليه — يعمل، ويقرؤه المُدقِّق دوراً تفاعلياً بلا تركيز،
  // وهو محقّ: مكان المعالج هو العنصر الذي يملك التركيز فعلاً.
  const onKeyDown = (index) => (e) => {
    const keys = ['ArrowRight', 'ArrowLeft', 'Home', 'End'];
    if (!keys.includes(e.key)) return;
    e.preventDefault();

    const rtl = listRef.current
      ? getComputedStyle(listRef.current).direction === 'rtl'
      : false;
    const forward = e.key === (rtl ? 'ArrowLeft' : 'ArrowRight');

    let next;
    if (e.key === 'Home') next = 0;
    else if (e.key === 'End') next = tabs.length - 1;
    // دورةٌ مغلقة: آخر لسانٍ يعود إلى الأول. هو ما يتوقّعه من يتنقّل بالأسهم، وهو ما يمنع طريقاً مسدوداً.
    else next = (index + (forward ? 1 : -1) + tabs.length) % tabs.length;

    const target = tabs[next];
    onChange(target.id);
    // النقل يُتبع بالتركيز: `aria-activedescendant` بديلٌ أضعف هنا، وقارئ الشاشة يُعلن اللسان الجديد
    // بانتقال التركيز إليه فعلاً.
    listRef.current?.querySelector(`#${CSS.escape(tabId(target.id))}`)?.focus();
  };

  return (
    <div
      ref={listRef}
      role="tablist"
      aria-label={label}
      className={className ? `${styles.list} ${className}` : styles.list}
    >
      {tabs.map((tab, index) => {
        const selected = tab.id === active;
        return (
          <button
            key={tab.id}
            id={tabId(tab.id)}
            type="button"
            role="tab"
            aria-selected={selected}
            aria-controls={`${base}-panel-${tab.id}`}
            className={styles.tab}
            // التجوّل: غير المختار خارج تسلسل الجدولة، فـ Tab يدخل مجموعة الألسنة مرّةً ويخرج منها إلى اللوح.
            tabIndex={selected ? 0 : -1}
            onClick={() => onChange(tab.id)}
            onKeyDown={onKeyDown(index)}
          >
            {tab.label}
            {tab.badge !== undefined && tab.badge !== null && <span data-badge>{tab.badge}</span>}
          </button>
        );
      })}
    </div>
  );
}

/**
 * اللوح المقابل للسان — بنفس `base` الذي أُعطي للألسنة.
 *
 * **بلا `tabIndex`**: نمط ARIA يجعل اللوح قابلاً للتركيز فقط إن لم يكن فيه عنصرٌ تفاعلي. ولوحُنا فيه
 * جدولٌ وأزرار، فإضافتُه تُدخل محطّةً زائدة في تسلسل الجدولة لا تفعل شيئاً — ومحطّةً بلا إطار تركيزٍ
 * مرئيّ إن أُخفي، أو بإطارٍ حول قسمٍ كامل إن أُظهر. كلاهما أسوأ من غيابها.
 * @param {{id: string, base: string, children: import('react').ReactNode}} props
 */
export function TabPanel({ id, base, children }) {
  return (
    <div
      id={`${base}-panel-${id}`}
      role="tabpanel"
      aria-labelledby={`${base}-tab-${id}`}
    >
      {children}
    </div>
  );
}
