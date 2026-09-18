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
  searchSuggestions: (keyword) => ['search-suggestions', keyword],
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
  platformUsers: (page, pageSize) => ['platform-users', page, pageSize],
  platformAudit: (params) => ['platform-audit', params],

  adminProduct: (id) => ['admin-product', String(id)],

  // ============================================================================
  // شاشات لوحة المتجر (TD-25، هُجِّرت في M10). كلّ مفتاح يحمل **كل** ما يحدّد ما يُعرَض — الصفحة
  // والبحث والتصفية — لأن ذلك هو ما يُغلق العيب الذي سمّاه الدين: ردٌّ لبحثٍ سابق يصل بعد ردّ بحثٍ
  // أحدث فيُكتب في مفتاحه لا على الشاشة. مفتاحٌ ينقص منه معيارٌ يعيد العيب صامتاً.
  //
  // والجذر لكلٍّ منها نصّ واحد، ودالّةُ الجذر مُعلَنة أيضاً: الإبطال بعد تعديلٍ مفتاحٌ كأيّ مفتاح،
  // ولا يُكتب نصّاً في شاشة (القاعدة أعلى هذا الملفّ).
  // ============================================================================
  adminCategoriesAll: () => ['admin-categories'],
  adminCategories: (params) => ['admin-categories', params],
  adminCouponsAll: () => ['admin-coupons'],
  adminCoupons: (params) => ['admin-coupons', params],
  adminCouponRedemptions: (id, params) => ['admin-coupons', String(id), 'redemptions', params],
  adminCustomersAll: () => ['admin-customers'],
  adminCustomers: (params) => ['admin-customers', params],
  adminOrdersAll: () => ['admin-orders'],
  adminOrders: (params) => ['admin-orders', params],
  adminOrder: (id) => ['admin-orders', String(id)],
  adminProductsAll: () => ['admin-products'],
  adminProducts: (params) => ['admin-products', params],
  adminPaymentsAll: () => ['admin-payments'],
  adminPayments: (params) => ['admin-payments', params],
  adminReviewsAll: () => ['admin-reviews'],
  adminReviews: (params) => ['admin-reviews', params],
  adminSynonymsAll: () => ['admin-synonyms'],
  adminSynonyms: (params) => ['admin-synonyms', params],
  adminShippingAll: () => ['admin-shipping'],
  adminShipping: (params) => ['admin-shipping', params],

  // جذر واحد لكل ما تعرضه شاشة الجرد: تصحيحُ مخزون يغيّر صفحة الجرد **وعدد المنخفض** معاً،
  // فإبطال الجذر يُصيبهما بنداء واحد بدل تتبّع مفتاحين منفصلين عند كل تعديل.
  // والجذر نفسه دالّة هنا لا نصّاً في الشاشة: القاعدة أعلى هذا الملفّ تمنع بناء مفتاح في مكانين،
  // والإبطال مفتاحٌ كأيّ مفتاح — `['inventory']` مكتوبةً في شاشةٍ تفترق عن هذه يوماً بلا أن يشتكي شيء.
  inventoryAll: () => ['inventory'],
  inventory: (page, pageSize) => ['inventory', page, pageSize],
  inventoryLowCount: () => ['inventory', 'low-count'],

  storeSettings: () => ['store-settings'],
  storeSettingsOptions: () => ['store-settings-options'],
  staff: (page, pageSize) => ['staff', page, pageSize],

  myProfile: () => ['my-profile'],
  myAddresses: () => ['my-addresses'],
};

// كل ما يخصّ المستخدم الحالي — يُزال عند تبدّل الهوية.
export const CUSTOMER_SCOPED_KEYS = ['my-orders', 'order', 'my-profile', 'my-addresses'];
