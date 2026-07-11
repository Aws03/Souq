import { useCart } from '../../context/CartContext';
import Drawer from '../common/Drawer';
import { EmptyState } from '../common/StateViews';
import { PackageIcon } from '../icons/Icons';
import CartLine from './CartLine';
import CartSummary from './CartSummary';

// درج السلة: ينزلق من اليسار (صحيح لتخطيط RTL — الجانب المقابل لجهة القراءة)
// ويعرض محتواها مع تعديل الكميات والانتقال للدفع.
export default function CartDrawer({ open, onClose, onCheckout }) {
  const { items, total, inc, dec, remove } = useCart();
  const currency = items[0]?.currency || 'JOD';

  return (
    <Drawer open={open} onClose={onClose} side="left" title="سلّتك" width={440}
      footer={items.length > 0 && <CartSummary subtotal={total} currency={currency} onCheckout={onCheckout} />}>
      {items.length === 0 ? (
        <EmptyState icon={PackageIcon} title="سلّتك فارغة" message="أضِف منتجات تعجبك وابدأ التسوّق." />
      ) : (
        items.map((i) => <CartLine key={i.id} item={i} onInc={inc} onDec={dec} onRemove={remove} />)
      )}
    </Drawer>
  );
}
