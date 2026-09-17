import { useId, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { SearchIcon, PackageIcon, TagIcon } from '../icons/Icons';
import { useSearchSuggestions } from '../../features/catalog/useSearchSuggestions';
import { productPath } from '../../features/catalog/productRouting';
import styles from './SearchBar.module.css';

// ============================================================================
// صندوق البحث، وقائمة اقتراحاته (M3، ADR-0042).
//
// نمط combobox من WAI-ARIA لا صندوقاً بقائمة `div`: التركيز **يبقى في الحقل** والعنصر النشط يُعلَن بـ
// `aria-activedescendant`. هذا هو الفرق بين قائمة يقرؤها قارئ الشاشة وقائمة لا يعرف بوجودها أصلاً — ولو
// نُقل التركيز إلى العنصر لانكسرت الكتابة نفسها.
//
// لماذا لا نغلق بمستمع على المستند؟ لأنّ الخروج من الحقل (blur) يغلق القائمة أصلاً، وأزرار العناصر تمنع
// mousedown الافتراضي فلا يخرج التركيز عند النقر. سلوك واحد بدل مستمعَين يتسابقان.
//
// الصندوق يُركَّب مرّتين (سطح المكتب وورقة الجوال) وكلاهما في الشجرة معاً، فالمعرّفات من useId لكل مثيل:
// معرّفان متكرّران في المستند يجعلان aria-controls يشير إلى الخطأ.
// ============================================================================
export default function SearchBar({ value, onChange, onSubmit, onNavigate, className = '' }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  // useId يعطي صيغة مثل ":r0:" — معرّف HTML صالح تماماً ومرجع ARIA سليم، لكنه **ليس** مُعرِّف CSS صالحاً
  // (النقطتان محرف خاصّ في المحدِّدات). يُنظَّف كي يبقى قابلاً للتحديد في CSS وفي رحلات المتصفّح.
  const listId = `souq-search-${useId().replace(/[^a-zA-Z0-9_-]/g, '')}`;
  const optionId = (index) => `${listId}-option-${index}`;

  const [focused, setFocused] = useState(false);
  // إغلاق صريح بـ Escape أو Tab: الكتابة من جديد تُعيد فتح القائمة، فالإغلاق لا يلتصق بالجلسة.
  const [dismissed, setDismissed] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);

  const { items, loading } = useSearchSuggestions(value, focused && !dismissed);
  const open = focused && !dismissed && items.length > 0;
  const active = open && activeIndex >= 0 && activeIndex < items.length ? items[activeIndex] : null;

  const close = () => { setDismissed(true); setActiveIndex(-1); };

  const select = (item) => {
    close();
    // الفئة تفتح الكتالوج مصفّى بها، والمنتج يفتح صفحته — الاقتراح وجهة لا نصّ يُكتب في الحقل.
    navigate(item.kind === 'category' ? `/?cats=${item.id}` : productPath(item));
    // ورقة الجوال تُغلق بهذا: بلاه يبقى الحوار فوق الصفحة التي انتقل إليها المتسوّق، ويبقى تمرير الصفحة
    // محجوزاً (useDialog يضبط overflow: hidden) — فيبدو المتجر معلّقاً.
    onNavigate?.();
  };

  const move = (step) => {
    if (!open) return;
    setActiveIndex((current) => {
      const next = current + step;
      if (next < 0) return items.length - 1;
      if (next >= items.length) return 0;
      return next;
    });
  };

  const handleKeyDown = (event) => {
    switch (event.key) {
      case 'ArrowDown': event.preventDefault(); move(1); break;
      case 'ArrowUp': event.preventDefault(); move(-1); break;
      case 'Home': if (open) { event.preventDefault(); setActiveIndex(0); } break;
      case 'End': if (open) { event.preventDefault(); setActiveIndex(items.length - 1); } break;
      case 'Escape': if (open) { event.preventDefault(); close(); } break;
      // Tab يُغلق ولا يُختار: الخروج بالمفتاح ليس تأكيداً، وإلا انتقل المتسوّق إلى صفحة لم يطلبها.
      case 'Tab': close(); break;
      default: break;
    }
  };

  const handleSubmit = (event) => {
    event.preventDefault();
    // Enter على عنصر نشط يفتحه؛ وبلا عنصر نشط يُنفَّذ البحث كما كان قبل الاقتراحات تماماً.
    if (active) select(active);
    else { close(); onSubmit?.(); }
  };

  return (
    <form className={`${styles.wrap} ${className}`} role="search" onSubmit={handleSubmit}>
      <SearchIcon size={16} />
      <input
        type="search"
        role="combobox"
        value={value}
        placeholder={t('nav.searchPlaceholder')}
        aria-label={t('nav.searchAria')}
        aria-expanded={open}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={active ? optionId(activeIndex) : undefined}
        autoComplete="off"
        onChange={(e) => { onChange(e.target.value); setDismissed(false); setActiveIndex(-1); }}
        onFocus={() => setFocused(true)}
        onBlur={() => { setFocused(false); setActiveIndex(-1); }}
        onKeyDown={handleKeyDown}
      />

      {/* قائمة موجودة دائماً في الشجرة كي يبقى aria-controls صالحاً، وتُفرَّغ حين تُغلق.
          div لا ul: أبناء role="listbox" يجب أن يكونوا option، أمّا li فيحمل دور listitem ضمنياً وهو دور
          غير مسموح داخل listbox — فالقائمة الدلالية هنا تناقض دور ARIA بدل أن تخدمه. */}
      <div className={styles.list} id={listId} role="listbox" aria-label={t('nav.searchSuggestionsAria')}
        hidden={!open}>
        {/* ============================================================================
            الخيار **هو** العنصر التفاعلي: لا زرّ داخله. زرّ داخل role="option" عنصر تفاعلي داخل عنصر تفاعلي
            (قاعدة axe: nested-interactive) وقارئ الشاشة لا يعرف أيّهما يُعلِن — عطلٌ وجدته رحلة المتصفّح على
            حزمة الحاويات ولم تجده بيئة jsdom.
            
            tabIndex={-1}: الخيار قابل للتركيز برمجياً وغير قابل للوصول بـ Tab. لا نُركّزه فعلاً (التركيز يبقى
            في الحقل، وaria-activedescendant هو ما يسمّي النشط)، لكنّ الدور التفاعلي يقتضي قابلية التركيز.
            
            وأمّا مستمع المفاتيح فهو على combobox أعلاه (الأسهم/Home/End/Enter/Escape) كما يقتضي النمط نفسه:
            الخيار لا يُركَّز، فمستمع مفاتيح عليه لن يُستدعى أبداً وكان سيكون كوداً ميتاً. القاعدة أدناه لا
            تُميّز هذا النمط؛ وما تحرسه فعلاً — أنّ القائمة تُشغَّل بالكامل بلوحة المفاتيح — مُختبَر في
            SearchBar.test.jsx وفي e2e/search.spec.js، لا متروكاً للثقة.
            ============================================================================ */}
        {open && items.map((item, index) => (
          // eslint-disable-next-line jsx-a11y/click-events-have-key-events
          <div key={`${item.kind}-${item.id}`} id={optionId(index)} role="option" tabIndex={-1}
            aria-selected={index === activeIndex}
            className={`${styles.option} ${index === activeIndex ? styles.optionActive : ''}`}
            // يمنع خروج التركيز من الحقل، فلا يُغلق blur القائمة قبل أن تصل النقرة.
            onMouseDown={(e) => e.preventDefault()}
            onMouseEnter={() => setActiveIndex(index)}
            onClick={() => select(item)}>
            <span className={styles.optionIcon} aria-hidden="true">
              {item.kind === 'category' ? <TagIcon size={14} /> : <PackageIcon size={14} />}
            </span>
            <span className={styles.optionName}>{item.name}</span>
            {item.kind === 'category' && (
              <span className={styles.optionKind}>{t('nav.searchSuggestionCategory')}</span>
            )}
          </div>
        ))}
      </div>

      {/* منطقة حيّة مهذّبة: عدد الاقتراحات يُعلَن بلا مقاطعة الكتابة. polite لا assertive لهذا السبب. */}
      <span className="souq-visually-hidden" role="status" aria-live="polite">
        {open ? t('nav.searchSuggestionsCount', { count: items.length }) : ''}
        {loading ? t('common.loading') : ''}
      </span>
    </form>
  );
}
