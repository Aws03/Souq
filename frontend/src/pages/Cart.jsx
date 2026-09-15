import { Link, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useCart } from '../context/CartContext';
import { usePageMetadata } from '../app/usePageMetadata';
import { hasProblems } from '../features/basket/basketModel';
import CartLine from '../components/cart/CartLine';
import CartSummary from '../components/cart/CartSummary';
import Skeleton from '../components/common/Skeleton';
import { EmptyState } from '../components/common/StateViews';
import { ChevronIcon, PackageIcon } from '../components/icons/Icons';
import styles from './Cart.module.css';

// ============================================================================
// صفحة السلة (المرحلة 16) — إلى جانب الدرج لا بدلاً منه: الدرج للنظرة السريعة بعد الإضافة،
// والصفحة لمراجعة سلة كبيرة على رابط قابل للحفظ والمشاركة والرجوع إليه، وبمساحة تكفي أسماء
// المنتجات وصورها على الجوال.
//
// السلة نفسها لا تُحسب هنا: الخادم مصدر الحقيقة للأسعار والمجاميع (ADR-0028)، والصفحة والدرج
// يعرضان الحالة ذاتها من CartContext — لا نسخة ثانية من المنطق ولا سطر حسابٍ محلي.
//
// الكوبون يبقى في الدفع عمداً: خصمه يُحسب من /basket/quote مع العنوان وطريقة الشحن، وتكراره
// هنا يعني تسعيراً ثانياً قد يخالف ما يُنشئ به الطلب (TD-06 هو ما يحدث حين يتكرّر التسعير).
// ============================================================================
export default function Cart() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { basket, items, loaded, inc, dec, remove } = useCart();
  usePageMetadata({ title: t('cart.title') });

  const backDir = i18n.dir() === 'rtl' ? 'end' : 'start';

  return (
    <div className={`souq-layout ${styles.wrap}`}>
      <h1 className={styles.title}>{t('cart.title')}</h1>
      <p className={styles.subtitle}>{t('cart.pageSubtitle')}</p>

      {!loaded && <Skeleton height={280} radius={14} />}

      {loaded && items.length === 0 && (
        <EmptyState
          icon={PackageIcon}
          title={t('cart.emptyTitle')}
          message={t('cart.emptyMessage')}
          actionLabel={t('cart.continueShopping')}
          onAction={() => navigate('/')}
        />
      )}

      {loaded && items.length > 0 && (
        <div className={styles.grid}>
          <section className={styles.lines}>
            {items.map((item) => (
              <CartLine key={item.id} item={item} onInc={inc} onDec={dec} onRemove={remove} />
            ))}
          </section>

          <aside className={styles.summary}>
            <h2 className={styles.summaryTitle}>{t('cart.summaryTitle')}</h2>
            <CartSummary basket={basket} blocked={hasProblems(items)} onCheckout={() => navigate('/checkout')} />
            <Link to="/" className={styles.continue}>
              <ChevronIcon dir={backDir} size={16} /> {t('cart.continueShopping')}
            </Link>
          </aside>
        </div>
      )}
    </div>
  );
}
