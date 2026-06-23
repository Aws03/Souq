import { useCart } from '../context/CartContext';

// مكوّن قابل لإعادة الاستخدام: بطاقة منتج واحدة. يتلقّى المنتج كـ prop ويعرضه.
// مبدأ "المسؤولية الواحدة": مهمته عرض منتج وزر الإضافة فقط.
export default function ProductCard({ product, onAdded }) {
  const { add } = useCart();
  const outOfStock = product.stockQuantity <= 0;

  const handleAdd = () => { add(product); onAdded?.(product.name); };

  return (
    <div className="card">
      <div className="card-arch"><span className="card-emoji">{product.emoji}</span></div>
      <div className="card-body">
        <div className="card-cat">{product.categoryName}</div>
        <h3>{product.name}</h3>
        <p className="card-desc">{product.description}</p>
        {product.stockQuantity > 0 && product.stockQuantity <= 5 && (
          <div className="stock-low">باقٍ {product.stockQuantity} فقط</div>
        )}
        <div className="card-foot">
          <div className="price">{product.price.toFixed(2)} <small>{product.currency}</small></div>
          <button className="add-btn" onClick={handleAdd} disabled={outOfStock}>
            {outOfStock ? 'نفد' : 'أضف'}
          </button>
        </div>
      </div>
    </div>
  );
}
