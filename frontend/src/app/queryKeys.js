// ============================================================================
// مفاتيح الاستعلامات في مكان واحد (ADR-0037).
//
// المفتاح هو هوية البيانات في الذاكرة المؤقّتة: مفتاحان متطابقان يعنيان "نفس الشيء"، ومفتاح
// مبنيّ في مكانين يفترق يوماً فيُبطَل أحدهما ولا يُبطَل الآخر. فكلّها هنا، ولا تُكتب كنصّ في شاشة.
//
// لماذا لا يحمل المفتاح معرّف المتجر؟ لأن المتجر يُحلّ من المضيف على الخادم، والذاكرة المؤقّتة
// تعيش في تحميل صفحة واحد على أصل واحد — فلا يوجد مسار يجعل متجرين يتشاركان عميلاً واحداً.
// أما تبدّل *الهوية* (خروج ثم دخول بحساب آخر بلا إعادة تحميل) فممكن، ولذلك تُمسح الذاكرة عند
// كل تبدّل مستخدم (QueryProvider) — وهو ما يحرسه اختبار.
// ============================================================================
export const queryKeys = {
  categories: () => ['categories'],

  products: (params) => ['products', params],
  product: (handle) => ['product', String(handle)],
  relatedProducts: (productId) => ['product', String(productId), 'related'],
  productReviews: (productId, page, pageSize) => ['product', String(productId), 'reviews', page, pageSize],

  myOrders: (page, pageSize) => ['my-orders', page, pageSize],
  order: (id) => ['order', String(id)],
  orderTracking: (token) => ['order-tracking', String(token)],

  storeDashboard: (range) => ['store-dashboard', range],
  platformStats: () => ['platform-stats'],
  provisioningOptions: () => ['provisioning-options'],
  platformStores: (params) => ['platform-stores', params],
  platformStore: (id) => ['platform-store', String(id)],
  platformStoreSettings: (id) => ['platform-store', String(id), 'settings'],
  platformStoreAccounts: (id) => ['platform-store', String(id), 'accounts'],

  storeSettings: () => ['store-settings'],
  storeSettingsOptions: () => ['store-settings-options'],
  staff: (page, pageSize) => ['staff', page, pageSize],

  myProfile: () => ['my-profile'],
  myAddresses: () => ['my-addresses'],
};

// كل ما يخصّ المستخدم الحالي — يُزال عند تبدّل الهوية.
export const CUSTOMER_SCOPED_KEYS = ['my-orders', 'order', 'my-profile', 'my-addresses'];
