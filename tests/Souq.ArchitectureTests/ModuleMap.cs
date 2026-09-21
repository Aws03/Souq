namespace Souq.ArchitectureTests;

// ============================================================================
// خريطة مجلّدات Features إلى وحدات docs/04-MODULES (ADR-0002: الوحدة نطاق أسماء لا مشروع) — مصدر واحد لاختبارات الحدود
// والعقود (ModuleAndContractRuleTests) وللجرود المولَّدة (GeneratedDocsTests). مجلّد ميزات جديد يُضاف هنا ولوثيقة وحدته معاً.
// ============================================================================
internal static class ModuleMap
{
    public const string Features = "Souq.Application.Features";

    // الترتيب هو ترتيب العرض في الجرود: المنصّة والهوية أولاً، ثم رحلة الشراء، ثم ما حولها.
    public static readonly IReadOnlyDictionary<string, string[]> FeatureFolders = new Dictionary<string, string[]>
    {
        ["Platform"] = ["Platform", "Stores"],
        ["Identity"] = ["Auth", "Staff"],
        ["Catalog"] = ["Products", "Categories"],
        ["Inventory"] = ["Inventory"],
        ["Customers"] = ["Customers"],
        ["Shopping"] = ["Baskets", "Wishlist"],
        ["Ordering"] = ["Orders"],
        ["Payments"] = ["Payments"],
        ["Promotions"] = ["Coupons"],
        ["Shipping"] = ["Shipping"],
        ["Reviews"] = ["Reviews"],
        ["Notifications"] = ["Notifications"],
        // C9 (ADR-0050): الالتقاط السلوكي مجلّدٌ في وحدة Reporting لا وحدةٌ جديدة — Reporting هي
        // من يجيب عن أسئلة المتجر عن تجارته، وهذه موادُّ جوابها. والتوصياتُ (C10) تقرؤها عبر
        // عقودها من Catalog، فلا سهمَ تبعيةٍ جديد بين وحدتين.
        ["Reporting"] = ["Reporting", "Analytics"],
        // C1 (ADR-0047): مستوى التحكّم التجاري — الخطط والاشتراكات والاستحقاقات. مُلحقة في آخر
        // الترتيب عمداً: الترتيب هو ترتيب العرض في الجرود، وإدراجها في الوسط يعيد ترتيب ملفّين كاملين.
        ["Billing"] = ["Billing"],
        // ============================================================================
        // ADR-0055 (قرار المالك P-06): الضريبة وحدةٌ خامسة عشرة، والبدائلُ أسوأ وقد نُوقشت في الـ
        // ADR: داخل Shopping تصير Billing معتمدةً على Shopping لتُضرِّب فاتورة اشتراك؛ وداخل
        // Platform تحمل Platform خدمةَ حسابٍ وإعدادَ متجرٍ لا علاقة لهما بإدارة المتاجر؛ وداخل
        // Billing تُضرَّب سلّةُ متسوّقٍ بالوحدة التي تُفوتر التجّار.
        //
        // فهي قدرةٌ مشتركة لها بياناتُها ودورةُ حياتها، تستهلكها Shopping وOrdering وBilling عبر
        // عقودها — وذلك بالضبط ما تكون الوحدةُ لأجله هنا.
        // ============================================================================
        ["Tax"] = ["Tax"],
    };

    // ============================================================================
    // منطقة المنصّة: المجلّدات التي تُخدَم على مضيف المنصّة وحده، خلف صلاحية منصّة. لها امتيازان
    // لا يملكهما غيرها، وكلاهما كان مكتوباً حرفياً في اختبارين منفصلين قبل C1:
    //   • طلباتها **تحمل TenantId** (تستهدف متجراً بعينه ولا تُشتقّه من المضيف)،
    //   • وكلّها **مُدقَّقة** (IAuditable).
    // جمعُهما هنا يعني أن مجلّداً جديداً يُنسى في مكان واحد لا في مكانين — وهو بالضبط ما تشكوه
    // ADR-0047 §3: "ولا شيء سيذكّرك".
    // ============================================================================
    public static readonly IReadOnlyList<string> PlatformAreaFolders = ["Platform", "Billing"];

    // ============================================================================
    // تُدقَّق أيضاً وإن لم تكن منطقة منصّة:
    //   • Reporting — قراءاتها المجمَّعة تعبر المتاجر.
    //   • Tax (ADR-0055 §الثابت السادس) — **كلُّ تغييرٍ في إعدادٍ ضريبيّ مُدقَّق**: إنشاءُ ملفّ،
    //     ونشرُ إصدار، وتسجيلُ تحقّق، واختيارُ متجرٍ له. أثرُه مالٌ ومسؤوليةٌ قانونية، فمَن فعله
    //     ومتى ليس تفصيلاً. وليست منطقةَ منصّة: طلباتُ المتجر منها تأخذ متجرَها من المضيف كغيرها،
    //     وطلباتُ المنصّة لا تستهدف متجراً بعينه أصلاً — الملفّ عامٌّ لا يخصّ أحداً.
    // ============================================================================
    public static readonly IReadOnlyList<string> AuditedAreaFolders = [.. PlatformAreaFolders, "Reporting", "Tax"];

    public static IEnumerable<string> Modules => FeatureFolders.Keys;

    // ملكية أنواع المجال: من يملك كل كيان ومنفذ وحدث وقيمة في Souq.Domain. ما لا يُذكر هنا نواة مشتركة يستخدمها الجميع بلا
    // تسريب حدود (Entity، Money، CurrencyInfo، IRepository، IUnitOfWork، DomainException، Roles، AuditEntry، IDomainEvent).
    // الخريطة قرار توثيقي لا يُستنتج من الكود، وتغذّي جرد تبعيات المجال بين الوحدات
    // (docs/02-ARCHITECTURE/ModuleDomainDependencies.md): طبقة Application وحدها محروسة بالعقود، فهذا الجرد يجعل ما تبقّى مرئياً.
    public static readonly IReadOnlyDictionary<string, string> DomainOwners = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Platform
        ["Tenant"] = "Platform", ["TenantDomain"] = "Platform", ["TenantStatus"] = "Platform",
        ["StoreSettings"] = "Platform", ["StoreModules"] = "Platform", ["StoreBranding"] = "Platform",
        ["StoreContact"] = "Platform", ["SeoSettings"] = "Platform", ["SocialLink"] = "Platform",
        ["BrandColors"] = "Platform", ["BrandPresets"] = "Platform", ["BrandingAsset"] = "Platform",
        ["LocalizedText"] = "Platform", ["ITenantRepository"] = "Platform", ["InvalidTenantOperationException"] = "Platform",

        // Identity
        ["User"] = "Identity", ["RefreshToken"] = "Identity", ["UserStatus"] = "Identity",
        ["IUserRepository"] = "Identity", ["IRefreshTokenRepository"] = "Identity",
        ["InvalidIdentityOperationException"] = "Identity", ["InvalidPasswordResetException"] = "Identity",
        ["InvalidEmailVerificationException"] = "Identity",

        // Catalog
        ["Product"] = "Catalog", ["ProductVariant"] = "Catalog", ["ProductImage"] = "Catalog",
        ["ProductTranslation"] = "Catalog", ["ProductStatus"] = "Catalog", ["Category"] = "Catalog",
        ["CategoryTranslation"] = "Catalog", ["CategoryLink"] = "Catalog", ["CatalogTranslation"] = "Catalog",
        ["CatalogText"] = "Catalog", ["CatalogSlug"] = "Catalog", ["IProductRepository"] = "Catalog",
        ["SearchText"] = "Catalog", ["SearchDistance"] = "Catalog",
        ["SearchSynonym"] = "Catalog", ["InvalidSearchSynonymException"] = "Catalog",
        ["ICategoryRepository"] = "Catalog", ["InvalidProductDataException"] = "Catalog",
        ["InvalidCategoryException"] = "Catalog", ["InvalidCategoryParentException"] = "Catalog",

        // Inventory
        ["InventoryItem"] = "Inventory", ["StockMovement"] = "Inventory", ["StockMovementType"] = "Inventory",
        ["StockReservation"] = "Inventory", ["ReservationStatus"] = "Inventory", ["IInventoryRepository"] = "Inventory",
        ["IStockMovementRepository"] = "Inventory", ["InsufficientStockException"] = "Inventory",
        ["InvalidInventoryOperationException"] = "Inventory", ["StockBecameLow"] = "Inventory",

        // Customers
        ["Customer"] = "Customers", ["CustomerAddress"] = "Customers", ["CustomerStatus"] = "Customers",
        ["PostalAddress"] = "Customers", ["ICustomerRepository"] = "Customers",
        ["InvalidCustomerDataException"] = "Customers",

        // Shopping
        ["Basket"] = "Shopping", ["BasketLine"] = "Shopping", ["WishlistItem"] = "Shopping",
        ["IBasketRepository"] = "Shopping", ["IWishlistRepository"] = "Shopping",
        ["InvalidBasketOperationException"] = "Shopping",

        // Ordering
        ["Order"] = "Ordering", ["OrderItem"] = "Ordering", ["OrderStatus"] = "Ordering",
        ["OrderStatusHistory"] = "Ordering", ["OrderTransitions"] = "Ordering", ["OrderNumberSequence"] = "Ordering",
        ["OrderActor"] = "Ordering", ["OrderActorKind"] = "Ordering", ["OrderStatusChanged"] = "Ordering",
        ["IOrderRepository"] = "Ordering", ["InvalidOrderOperationException"] = "Ordering",

        // Payments
        ["Payment"] = "Payments", ["Refund"] = "Payments", ["PaymentStatus"] = "Payments",
        ["RefundStatus"] = "Payments", ["StorePaymentAccount"] = "Payments", ["PaymentKeyRules"] = "Payments",
        ["IPaymentRepository"] = "Payments", ["IStorePaymentAccountRepository"] = "Payments",
        ["InvalidPaymentOperationException"] = "Payments",

        // Promotions
        ["Coupon"] = "Promotions", ["CouponRedemption"] = "Promotions", ["CouponRedemptionStatus"] = "Promotions",
        ["DiscountType"] = "Promotions", ["ICouponRepository"] = "Promotions",
        ["ICouponRedemptionRepository"] = "Promotions", ["InvalidCouponException"] = "Promotions",

        // Shipping
        ["ShippingMethod"] = "Shipping", ["IShippingMethodRepository"] = "Shipping",
        ["InvalidShippingMethodException"] = "Shipping",

        // Reviews
        ["Review"] = "Reviews", ["ReviewStatus"] = "Reviews", ["IReviewRepository"] = "Reviews",
        ["InvalidReviewException"] = "Reviews",

        // Notifications
        ["Notification"] = "Notifications", ["NotificationKinds"] = "Notifications",
        ["INotificationRepository"] = "Notifications", ["InvalidNotificationException"] = "Notifications",

        // Billing (C1، ADR-0047): تعيش في نطاق Souq.Domain.Platform — النطاق يُقارَن بالمساواة في
        // TenancyRuleTests فنطاقٌ فرعي يكسر البناء — لكن **ملكيّتها** لوحدة Billing، وهذه الخريطة
        // هي الموضع الوحيد الذي يقول ذلك.
        ["Plan"] = "Billing", ["PlanStatus"] = "Billing", ["PlanEntitlement"] = "Billing",
        ["PlanLimit"] = "Billing", ["Limit"] = "Billing", ["Entitlements"] = "Billing",
        ["Subscription"] = "Billing", ["SubscriptionStatus"] = "Billing",
        ["EntitlementOverride"] = "Billing",
        ["IPlanRepository"] = "Billing", ["ISubscriptionRepository"] = "Billing",
        ["IEntitlementOverrideRepository"] = "Billing",
        ["InvalidPlanException"] = "Billing", ["InvalidSubscriptionException"] = "Billing",
        ["InvalidEntitlementOverrideException"] = "Billing",

        // ============================================================================
        // Analytics (C9، ADR-0050) — أوّل أنواع مجالٍ تملكها وحدة Reporting: كانت كلُّها خدمات
        // قراءة بلا كيان واحد.
        //
        // وإدراجُها هنا يُظهر حافّةً حقيقية في الجرد المولَّد: معالجُ البحث في Catalog يسمّي
        // `BehaviouralEventNames`، فالعبور يُرى بدل أن يُحسَب "نواةً مشتركة". و`SearchQueryLog`
        // غيرُ مدرَجٍ منذ M13 — نقصٌ قائم لا يصلحه هذا السطر، وتصحيحُه يغيّر أرقام جردٍ لمرحلةٍ
        // أخرى، فيُترك لمن يملكه.
        // ============================================================================
        ["BehaviouralEvent"] = "Reporting", ["BehaviouralEventNames"] = "Reporting",
        ["BehaviouralSurfaces"] = "Reporting", ["VisitorIdentityLink"] = "Reporting",
        ["ProductEngagementDaily"] = "Reporting", ["ProductPairDaily"] = "Reporting",
        ["AnalyticsRollupState"] = "Reporting",
    };

    public static string? DomainOwnerOf(string typeName) => DomainOwners.GetValueOrDefault(typeName);

    public static string? ModuleOf(Type type) => ModuleOfNamespace(type.Namespace);

    public static string? ModuleOfNamespace(string? ns)
    {
        if (ns is null || !ns.StartsWith(Features + ".", StringComparison.Ordinal)) return null;
        var folder = ns[(Features.Length + 1)..].Split('.')[0];
        return FeatureFolders.FirstOrDefault(m => m.Value.Contains(folder)).Key;
    }
}
