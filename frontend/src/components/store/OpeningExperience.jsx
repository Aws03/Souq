import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useStoreConfig } from '../../app/TenantProvider';
import { useStoreName } from '../../app/StoreBrand';
import {
  OPENING_DURATION_MS, hasSeenOpening, markOpeningSeen, prefersReducedMotion, resolveOpeningStyle, shouldPlayOpening,
} from '../../features/storefront/openingExperience';
import styles from './OpeningExperience.module.css';

// ============================================================================
// كشف الافتتاح — ستارتان تنفرجان عن المتجر، كأبواب صالة عرض.
//
// ثلاثة قيود تجعله لمسةً لا عائقاً:
//   • **لا يؤخّر شيئاً.** المتجر مرسوم خلفه من اللحظة الأولى (الستارتان فوقه لا بدله)، فلا
//     شبكة تنتظر ولا صورة تتأخّر. إغلاقه يكشف صفحةً جاهزة.
//   • **يُتخطّى دائماً.** زرّ ظاهر، وEscape، ونقرة في أي مكان. ومن يتخطّاه لا يراه ثانيةً.
//   • **يُزال من الشجرة بعده**، فلا طبقة شفّافة تبتلع النقرات بصمت.
//
// الحركة كلّها CSS: لا مكتبة، ولا JavaScript في كل إطار — وأي حركة هنا تُلغى تماماً عند
// تفضيل تقليل الحركة (القرار نفسه في openingExperience.js يمنع التركيب أصلاً).
// ============================================================================
export default function OpeningExperience() {
  const { t } = useTranslation();
  const config = useStoreConfig();
  const storeName = useStoreName();
  const opening = config?.settings?.branding?.opening;

  const [playing, setPlaying] = useState(() => shouldPlayOpening({
    enabled: Boolean(opening?.enabled),
    pathname: window.location.pathname,
    seenThisSession: hasSeenOpening(),
    prefersReducedMotion: prefersReducedMotion(),
    userAgent: navigator.userAgent,
  }));

  const skipRef = useRef(null);
  const dismiss = () => { markOpeningSeen(); setPlaying(false); };

  useEffect(() => {
    if (!playing) return undefined;
    markOpeningSeen();                       // تُحسب مرئية فور بدئها لا بعد انتهائها
    // التركيز يُنقل بالشيفرة لا بخاصّية autoFocus: نفس ما يفعله الدرج، ويتيح التحكّم بالتوقيت.
    skipRef.current?.focus();
    const timer = setTimeout(() => setPlaying(false), OPENING_DURATION_MS);
    const onKey = (event) => { if (event.key === 'Escape') dismiss(); };
    document.addEventListener('keydown', onKey);
    return () => { clearTimeout(timer); document.removeEventListener('keydown', onKey); };
  }, [playing]);

  if (!playing) return null;

  const style = resolveOpeningStyle(opening?.style);

  return (
    <div className={`${styles.stage} ${styles[style]}`} data-testid="opening-experience">
      {/* الستارتان والاسم زخرفة: المتجر نفسه مرسوم خلفهما ومقروء، فلا تُعلَن للقارئ الصوتي.
          أمّا زرّ التخطّي فيبقى معلَناً — طبقةٌ تُخفي زرّ الخروج معها تصير سجناً لمن لا يرى. */}
      <div className={`${styles.panel} ${styles.start}`} aria-hidden="true" />
      <div className={`${styles.panel} ${styles.end}`} aria-hidden="true" />
      <div className={styles.mark} aria-hidden="true">
        <span className={styles.markName}>{storeName}</span>
      </div>

      {/* النقر في أي مكان يتخطّى — عنصر <button> لا <div> بمستمع نقر، لكنه مخفيّ عن شجرة
          الإتاحة وخارج ترتيب التنقّل: راحةٌ للمؤشّر فقط. زرّ التخطّي الظاهر هو الضابط
          الوحيد المُعلَن، فلا يسمع قارئ الشاشة "تخطٍّ" مرّتين. */}
      <button type="button" className={styles.backdrop} onClick={dismiss} aria-hidden="true" tabIndex={-1} />
      <button type="button" ref={skipRef} className={styles.skip} onClick={dismiss}>
        {t('store.skipIntro')}
      </button>
    </div>
  );
}
