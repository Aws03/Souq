import { createContext, useContext, useReducer } from 'react';

// ============================================================================
// CartContext — إدارة حالة السلة المشتركة (مبدأ: حالة مركزية للبيانات المشتركة).
// لماذا Context + Reducer؟ السلة يصل إليها شريط التنقّل، صفحة المنتج، والدرج...
// تمريرها يدوياً عبر كل مكوّن (prop drilling) فوضى. الـ Context يجعلها متاحة
// لأي مكوّن دون تمرير، والـ Reducer يجمع كل منطق تعديلها في مكان واحد منظّم.
// ============================================================================
const CartContext = createContext();

function reducer(state, action) {
  switch (action.type) {
    case 'ADD': {
      const existing = state.find((i) => i.id === action.product.id);
      if (existing)
        return state.map((i) =>
          i.id === action.product.id ? { ...i, qty: i.qty + 1 } : i);
      return [...state, { ...action.product, qty: 1 }];
    }
    case 'INC':
      return state.map((i) => (i.id === action.id ? { ...i, qty: i.qty + 1 } : i));
    case 'DEC':
      return state
        .map((i) => (i.id === action.id ? { ...i, qty: i.qty - 1 } : i))
        .filter((i) => i.qty > 0);
    case 'REMOVE':
      return state.filter((i) => i.id !== action.id);
    case 'CLEAR':
      return [];
    default:
      return state;
  }
}

export function CartProvider({ children }) {
  const [items, dispatch] = useReducer(reducer, []);
  const total = items.reduce((s, i) => s + i.price * i.qty, 0);
  const count = items.reduce((s, i) => s + i.qty, 0);

  const value = {
    items, total, count,
    add: (product) => dispatch({ type: 'ADD', product }),
    inc: (id) => dispatch({ type: 'INC', id }),
    dec: (id) => dispatch({ type: 'DEC', id }),
    remove: (id) => dispatch({ type: 'REMOVE', id }),
    clear: () => dispatch({ type: 'CLEAR' }),
  };
  return <CartContext.Provider value={value}>{children}</CartContext.Provider>;
}

// Hook مخصّص: أي مكوّن ينادي useCart() ويحصل على السلة فوراً.
export const useCart = () => useContext(CartContext);
