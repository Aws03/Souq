import { useCallback, useEffect, useState } from 'react';
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { useDebouncedValue } from '../../hooks/useDebouncedValue';
import { isInPlace, searchDestination } from './searchRouting';

// ============================================================================
// يربط حقل البحث بالرابط: المسوّدة محلّية كي لا يُكتب الرابط مع كل حرف، وتُدفع بعد سكون
// قصير. الرابط هو المصدر: تغيّره من الخارج (رجوع، رابط مُشارَك) يعيد ملء الحقل.
// الكتابة استبدال لا إضافة في التاريخ — وإلا صار زرّ الرجوع يمرّ على كل حرف كُتب.
// ============================================================================
export function useStoreSearch() {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const location = useLocation();
  const urlQuery = searchParams.get('q') ?? '';

  const [draft, setDraft] = useState(urlQuery);
  useEffect(() => { setDraft(urlQuery); }, [urlQuery]);

  const debounced = useDebouncedValue(draft, 300);

  useEffect(() => {
    if (debounced.trim() === urlQuery) return;
    const destination = searchDestination(location.pathname, location.search, debounced);
    navigate(destination, { replace: isInPlace(location.pathname, destination) });
    // eslint-disable-next-line react-hooks/exhaustive-deps -- الدفع يتبع النصّ الساكن وحده
  }, [debounced]);

  // Enter: انتقال فوري بلا انتظار السكون.
  const submit = useCallback(() => {
    const destination = searchDestination(location.pathname, location.search, draft);
    navigate(destination, { replace: isInPlace(location.pathname, destination) });
  }, [draft, location.pathname, location.search, navigate]);

  return { value: draft, setValue: setDraft, submit, active: urlQuery };
}
