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
        ["Reporting"] = ["Reporting"],
    };

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
