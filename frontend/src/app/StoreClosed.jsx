import { Suspense } from 'react';
import { Link, Outlet } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { storeName, storeStatusKey, storefrontIsOpen } from './tenantModel';
import { useStoreConfig } from './TenantProvider';
import styles from './BootScreens.module.css';

// ============================================================================
// واجهة متجر مغلق، **داخل** التطبيق لا بدلاً منه (C3، قرار المالك C-17 = B).
//
// قبل هذا كان المتجر غير الفعّال يمنع تركيب التطبيق كلّه، فلم يكن لصاحبه طريق إلى لوحته ولا
// لمشترٍ طريقٌ إلى طلبٍ دفع ثمنه. الآن التطبيق يُركَّب، وهذه الشاشة تحلّ محلّ **صفحات التسوّق
// وحدها**: الرئيسية والمنتجات والسلّة والدفع وحساب العميل.
//
// وما يبقى عاملاً بجانبها مقصود كلّه: `/admin` (التاجر يُصلح سبب الإيقاف)، و`/login` (كي يدخل
// أصلاً)، و`/track/:token` (المشتري دفع ثمن طلبه قبل الإيقاف). ثلاثتها مفتوحة في الخادم بالحالة
// نفسها، فلا يعد المتصفّح بما يرفضه الخادم ولا يمنع ما يسمح به.
//
// ثلاث حالات ثلاث رسائل: كانت الثلاث رسالةً واحدة ("مغلق مؤقتاً") — فمتجرٌ قيد التجهيز يقول
// لصاحبه إنه مغلق، والمؤرشف يقول إنه يعود قريباً.
// ============================================================================
export function StoreClosedNotice() {
  const { t, i18n } = useTranslation();
  const config = useStoreConfig();
  const key = storeStatusKey(config);
  const name = storeName(config, i18n.language);

  return (
    <main className={styles.screen}>
      <div className={styles.card}>
        {name && <p className={styles.message}><strong>{name}</strong></p>}
        <h1 className={styles.title}>{t(`storeClosed.${key}.title`)}</h1>
        <p className={styles.message}>{t(`storeClosed.${key}.message`)}</p>
        {/* رابط الدخول قائم عمداً: صاحب المتجر يصل إلى لوحته من هنا بلا أن يعرف مساراً بقلبه. */}
        <Link className={styles.retry} to="/login">{t('storeClosed.signIn')}</Link>
      </div>
    </main>
  );
}

// بوّابة صفحات التسوّق: مفتوحة ⇒ الصفحة كما هي، مغلقة ⇒ الإشعار. مسارٌ بلا مسار (pathless) كي
// يكون الشرط في مكان واحد بدل أن يُنسخ على كل صفحة — ونسيانُه على صفحةٍ واحدة هو العطب كلّه.
export function StorefrontGate() {
  return storefrontIsOpen(useStoreConfig()) ? <Outlet /> : <StoreClosedNotice />;
}

// قشرة صغيرة لمتجر مغلق: بلا شريط تنقّل ولا سلّة ولا بحث — كلّها تنادي نقاطاً يردّها الخادم 503،
// فإظهارها يدعو الزائر إلى أخطاء. ما يظهر هنا يعمل.
export function ClosedStoreShell() {
  return (
    <Suspense fallback={<main className={styles.screen} aria-busy="true" />}>
      <Outlet />
    </Suspense>
  );
}
