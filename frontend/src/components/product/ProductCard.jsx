import { useState } from 'react';
import { useCart } from '../../context/CartContext';
import Button from '../common/Button';
import ProductImage from './ProductImage';
import { CategoryBadge, PriceTag, StockBadge } from './ProductBadges';
import styles from './ProductCard.module.css';

const ADD_FEEDBACK_MS = 350;

// بطاقة منتج واحدة قابلة لإعادة الاستخدام. مبدأ "المسؤولية الواحدة":
// مهمتها عرض منتج وزر الإضافة فقط.
export default function ProductCard({ product, onAdded }) {
  const { add } = useCart();
  const [adding, setAdding] = useState(false);
  const outOfStock = product.stockQuantity <= 0;

  const handleAdd = () => {
    setAdding(true);
    add(product);
    setTimeout(() => { setAdding(false); onAdded?.(product.name); }, ADD_FEEDBACK_MS);
  };

  return (
    <article className={styles.card}>
      <div className={styles.media}><ProductImage product={product} /></div>
      <div className={styles.body}>
        <CategoryBadge name={product.categoryName} />
        <h3 className={styles.name}>{product.name}</h3>
        <p className={styles.desc}>{product.description}</p>
        <StockBadge quantity={product.stockQuantity} />
        <div className={styles.foot}>
          <PriceTag amount={product.price} currency={product.currency} />
          <Button variant="primary" size="sm" loading={adding} disabled={outOfStock} onClick={handleAdd}>
            {outOfStock ? 'نفد' : 'أضف للسلة'}
          </Button>
        </div>
      </div>
    </article>
  );
}
