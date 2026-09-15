import { useEffect } from 'react';

const ELEMENT_ID = 'souq-structured-data';

// ============================================================================
// يكتب البيانات المنظّمة في <head> ويرفعها عند مغادرة الصفحة — كـusePageMetadata تماماً:
// صفحة تالية يجب ألّا ترث بيان صفحة سابقتها. وسم واحد يحمل مصفوفة، فلا يتنازع مُستدعيان
// على العنصر نفسه.
// ============================================================================
export function useStructuredData(...blocks) {
  const payload = JSON.stringify(blocks.filter(Boolean));

  useEffect(() => {
    const parsed = JSON.parse(payload);
    if (parsed.length === 0) return undefined;

    const element = document.createElement('script');
    element.type = 'application/ld+json';
    element.id = ELEMENT_ID;
    element.textContent = JSON.stringify(parsed.length === 1 ? parsed[0] : parsed);
    document.head.append(element);

    return () => element.remove();
  }, [payload]);
}
