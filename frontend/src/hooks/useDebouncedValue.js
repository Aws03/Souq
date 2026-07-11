import { useEffect, useState } from 'react';

// يؤخّر انعكاس قيمة متغيّرة بسرعة (كتابة البحث) كي لا نُطلق طلباً للخادم مع كل حرف.
export function useDebouncedValue(value, delayMs = 300) {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(t);
  }, [value, delayMs]);
  return debounced;
}
