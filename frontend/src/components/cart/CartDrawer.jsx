import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useCart } from '../../context/CartContext';
import { hasProblems } from '../../features/basket/basketModel';
import Drawer from '../common/Drawer';
import { EmptyState } from '../common/StateViews';
import { PackageIcon } from '../icons/Icons';
import CartLine from './CartLine';
import CartSummary from './CartSummary';
import styles from './CartDrawer.module.css';

// درج السلة: ينزلق من الجانب المقابل لجهة القراءة (يساراً في RTL، يميناً في LTR) ويعرض السلة كما يسعّرها الخادم مع
// تعديل الكميات والانتقال للدفع — الدفع موقوف ما دام سطر غير متاح أو يتجاوز المتاح.
// الدرج للنظرة السريعة؛ ومنه منفذ إلى صفحة السلة (/cart) لمراجعة أطول على رابط ثابت (المرحلة 16).
export default function CartDrawer({ open, onClose, onCheckout }) {
  const { t, i18n } = useTranslation();
  const { basket, items, inc, dec, remove } = useCart();
  const side = i18n.dir() === 'rtl' ? 'left' : 'right';

  return (
    <Drawer open={open} onClose={onClose} side={side} title={t('cart.title')} width={440}
      footer={items.length > 0 && (
        <>
          <CartSummary basket={basket} blocked={hasProblems(items)} onCheckout={onCheckout} />
          <Link to="/cart" className={styles.viewCart} onClick={onClose}>{t('cart.viewCart')}</Link>
        </>
      )}>
      {items.length === 0 ? (
        <EmptyState icon={PackageIcon} title={t('cart.emptyTitle')} message={t('cart.emptyMessage')} />
      ) : (
        items.map((i) => <CartLine key={i.id} item={i} onInc={inc} onDec={dec} onRemove={remove} />)
      )}
    </Drawer>
  );
}
