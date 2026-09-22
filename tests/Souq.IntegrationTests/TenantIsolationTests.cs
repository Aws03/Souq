using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Exceptions;
using Souq.API.Tenancy;
using Souq.Application.Common.Tenancy;
using Souq.Domain.Common;
using Souq.Domain.Entities;
using Souq.Domain.Enums;
using Souq.Domain.Identity;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// عزل المستأجرين عبر HTTP الحقيقي وSQL Server الحقيقي (MultiTenancy.md §5). متجر B — بمديره
// وعميله وزوّاره على مضيفه — يطلب موارد متجر A بمعرّفاتها الحقيقية:
//   • القراءة والتعديل والحذف ⇒ 404 (المورد "غير موجود" من منظوره — لا 403 يكشف وجوده).
//   • القوائم لا تُظهر صفوف A أبداً، والقوائم تحت مورد لـ A فارغة.
//   • لا مرجع لمورد A عند الكتابة (فئة، أب، منتج في طلب، كوبون، تقييم).
//   • توكن A على مضيف B ⇒ 401. حارس الكتابة والمرشّح بلا متجر يفشلان بصوت عالٍ.
// الجدول صريح (قرار مراجَع لكل نقطة)، واختبار الاكتمال يفشل إن ظهرت نقطة بمعرّف مورد لم تُضف إليه.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class TenantIsolationTests
{
    private enum Actor { Anonymous, Admin, Customer }
    private enum Resource
    {
        Product, ProductVariant, ProductImage, Category, Coupon, Order, StaffAccount, Customer, CustomerAddress, ShippingMethod, Review, Notification,
        SearchSynonym,
        // C5 (ADR-0056): فاتورةُ اشتراكِ متجرٍ من المنصّة. **جدولُ الشكل B بلا مرشّح مستأجر
        // أصلاً** — عزلُها كلُّه شرطُ `TenantId` الذي يكتبه المستدعي بيده، فهي أحوجُ ما في
        // هذا الجدول إلى صفٍّ فيه لا أقلُّه.
        PlatformInvoice,
    }

    private sealed record ForeignCase(string Method, string Route, Resource Resource, Actor Actor, Func<HttpContent>? Body = null);

    // توقيعات PNG/MP4 صالحة كي يصل الرفع إلى حالة الاستخدام (لا يُرفض شكلاً قبل فحص المتجر).
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0];
    private static readonly byte[] Mp4Bytes = [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', 0, 0, 0, 0, 0, 0, 0, 0];

    private static readonly ForeignCase[] ForeignResourceCases =
    [
        new("GET", "api/Products/{id:int}", Resource.Product, Actor.Anonymous),
        new("GET", "api/Products/by-slug/{slug}", Resource.Product, Actor.Anonymous),
        new("GET", "api/Products/{id:int}/related", Resource.Product, Actor.Anonymous),
        new("PUT", "api/Products/{id:int}", Resource.Product, Actor.Admin,
            () => JsonBody(TestApi.ProductUpdateBody(categoryId: 1, slug: "foreign-edit", price: 5m))),
        new("DELETE", "api/Products/{id:int}", Resource.Product, Actor.Admin),
        new("POST", "api/Products/{id:int}/image", Resource.Product, Actor.Admin, () => FileBody(PngBytes, "a.png")),
        new("POST", "api/Products/{id:int}/video", Resource.Product, Actor.Admin, () => FileBody(Mp4Bytes, "a.mp4")),
        new("GET", "api/admin/products/{id:int}", Resource.Product, Actor.Admin),
        // الخيارات والمتغيّرات (ADR-0040): منتج A لا تُعرَّف خياراته ولا تُنشأ متغيّراته من B، ومتغيّره لا يُعدَّل ولا يُعطَّل ولا يصير افتراضياً.
        new("PUT", "api/admin/products/{id:int}/options", Resource.Product, Actor.Admin,
            () => JsonBody(new { options = new[] { new { names = new { ar = "من B" }, values = new[] { new { names = new { ar = "س" } } }, existingVariantsValue = 0 } } })),
        new("POST", "api/admin/products/{id:int}/variants", Resource.Product, Actor.Admin,
            () => JsonBody(new { variants = new[] { new { optionValueIds = new[] { 1 }, price = 1m } } })),
        new("PUT", "api/admin/products/{id:int}/variants/{variantId:int}", Resource.ProductVariant, Actor.Admin, () => JsonBody(new { price = 1m })),
        new("PUT", "api/admin/products/{id:int}/variants/{variantId:int}/status", Resource.ProductVariant, Actor.Admin,
            () => JsonBody(new { isActive = false })),
        new("PUT", "api/admin/products/{id:int}/variants/{variantId:int}/default", Resource.ProductVariant, Actor.Admin),
        new("PUT", "api/admin/products/{id:int}/status", Resource.Product, Actor.Admin, () => JsonBody(new { status = "Archived" })),
        new("DELETE", "api/admin/products/{id:int}/images/{imageId:int}", Resource.Product, Actor.Admin),
        new("PUT", "api/admin/products/{id:int}/images/order", Resource.Product, Actor.Admin,
            () => JsonBody(new { imageIds = Array.Empty<int>() })),
        new("PUT", "api/Categories/{id:int}", Resource.Category, Actor.Admin,
            () => JsonBody(TestApi.CategoryBody("foreign-edit", "فئة"))),
        new("DELETE", "api/Categories/{id:int}", Resource.Category, Actor.Admin),
        new("PUT", "api/Coupons/{id:int}", Resource.Coupon, Actor.Admin,
            () => JsonBody(new { type = "Percentage", value = 50m, isActive = true })),
        new("DELETE", "api/Coupons/{id:int}", Resource.Coupon, Actor.Admin),
        new("GET", "api/Coupons/{id:int}/redemptions", Resource.Coupon, Actor.Admin),
        new("GET", "api/Orders/{id:int}", Resource.Order, Actor.Admin),
        new("POST", "api/Orders/{id:int}/confirm-payment", Resource.Order, Actor.Admin),
        new("PUT", "api/Orders/{id:int}/status", Resource.Order, Actor.Admin, () => JsonBody(new { action = "Cancel" })),
        // التتبّع العام بالرمز (المرحلة 9): رمز طلب A الحقيقي على مضيف B غير موجود. إلغاء العميل: عميل B لا يلغي طلب A.
        new("GET", "api/Orders/track/{token}", Resource.Order, Actor.Anonymous),
        new("POST", "api/Orders/{id:int}/cancel", Resource.Order, Actor.Customer),
        // الاسترداد (المرحلة 11): طلب A لا دفعة له في B — ولا يُعاد استرداد من دفعات A.
        new("POST", "api/orders/{id:int}/refunds", Resource.Order, Actor.Admin, () => JsonBody(new { amount = 1m })),
        new("POST", "api/orders/{id:int}/refunds/{refundId:int}/retry", Resource.Order, Actor.Admin),
        // طرق الشحن (المرحلة 12): طريقة A لا تُعدَّل ولا تُحذف من B.
        new("PUT", "api/admin/shipping-methods/{id:int}", Resource.ShippingMethod, Actor.Admin,
            () => JsonBody(new { name = "من B", price = 1m })),
        new("DELETE", "api/admin/shipping-methods/{id:int}", Resource.ShippingMethod, Actor.Admin),
        // مفردات البحث (M3، ADR-0042): مفردة A لا تُعدَّل ولا تُحذف من B — وإلّا غيّر تاجرٌ ما يجده زبائن متجر آخر.
        new("PUT", "api/admin/search-synonyms/{id:int}", Resource.SearchSynonym, Actor.Admin,
            () => JsonBody(new { culture = "ar", term = "منB", expansion = "هاتف" })),
        new("DELETE", "api/admin/search-synonyms/{id:int}", Resource.SearchSynonym, Actor.Admin),
        // الإشراف والمفضّلة (المرحلة 13): تقييم A لا يُعتمد ولا يُرفض من B؛ منتج A لا يدخل مفضّلة عميل B ولا يُحذف منها.
        new("POST", "api/admin/reviews/{id:int}/approve", Resource.Review, Actor.Admin),
        new("POST", "api/admin/reviews/{id:int}/reject", Resource.Review, Actor.Admin, () => JsonBody(new { note = "من B" })),
        new("PUT", "api/wishlist/{productId:int}", Resource.Product, Actor.Customer),
        new("DELETE", "api/wishlist/{productId:int}", Resource.Product, Actor.Customer),
        // الإشعارات (المرحلة 14): إشعار عميل A لا يُعلَّم مقروءاً من حساب في B.
        new("POST", "api/notifications/{id:int}/read", Resource.Notification, Actor.Customer),
        new("POST", "api/admin/inventory/{productId:int}/adjustments", Resource.Product, Actor.Admin,
            () => JsonBody(new { delta = 50, reason = "محاولة من متجر آخر" })),
        new("PUT", "api/admin/inventory/{productId:int}/threshold", Resource.Product, Actor.Admin,
            () => JsonBody(new { lowStockThreshold = 99 })),
        // مخزون بالمتغيّر (ProductVariants.md، V1): متغيّر منتج A لا يُصحَّح مخزونه ولا حدّه من B.
        new("POST", "api/admin/inventory/variants/{variantId:int}/adjustments", Resource.ProductVariant, Actor.Admin,
            () => JsonBody(new { delta = 50, reason = "محاولة من متجر آخر" })),
        new("PUT", "api/admin/inventory/variants/{variantId:int}/threshold", Resource.ProductVariant, Actor.Admin,
            () => JsonBody(new { lowStockThreshold = 99 })),
        new("GET", "api/admin/customers/{id:int}", Resource.Customer, Actor.Admin),
        new("PUT", "api/admin/customers/{id:int}/status", Resource.Customer, Actor.Admin, () => JsonBody(new { status = "Blocked" })),
        new("GET", "api/admin/customers/{id:int}/export", Resource.Customer, Actor.Admin),
        new("POST", "api/admin/customers/{id:int}/erase", Resource.Customer, Actor.Admin),
        new("PUT", "api/account/addresses/{id:int}", Resource.CustomerAddress, Actor.Customer,
            () => JsonBody(new { recipientName = "مسروق", phone = "0790000000", country = "JO", city = "عمّان", line1 = "شارع" })),
        new("DELETE", "api/account/addresses/{id:int}", Resource.CustomerAddress, Actor.Customer),
        new("PUT", "api/account/addresses/{id:int}/default-shipping", Resource.CustomerAddress, Actor.Customer),
        new("PUT", "api/account/addresses/{id:int}/default-billing", Resource.CustomerAddress, Actor.Customer),
        new("POST", "api/admin/staff/{id:int}/status", Resource.StaffAccount, Actor.Admin, () => JsonBody(new { active = false })),
        // السلة (المرحلة 8): سطر لمنتج A غير موجود في أي سلة على مضيف B (الرمز والسلة مُرشَّحان بالمتجر — الاختبار
        // المخصّص أدناه يرسل رمز سلة A الحقيقي إلى B).
        new("PUT", "api/basket/items/{productId:int}", Resource.Product, Actor.Anonymous, () => JsonBody(new { quantity = 1 })),
        new("DELETE", "api/basket/items/{productId:int}", Resource.Product, Actor.Anonymous),
        new("PUT", "api/basket/items/variants/{variantId:int}", Resource.ProductVariant, Actor.Anonymous, () => JsonBody(new { quantity = 1 })),
        new("DELETE", "api/basket/items/variants/{variantId:int}", Resource.ProductVariant, Actor.Anonymous),
        // C5 (ADR-0056): فاتورةُ اشتراكِ متجرٍ آخر لا تُقرأ بتخمين رقمها — و**404 لا 403**، فلا
        // يُكشَف أنّ لمتجرٍ آخر فاتورةً بهذا الرقم أصلاً.
        new("GET", "api/admin/store/subscription/invoices/{id:int}", Resource.PlatformInvoice, Actor.Admin),
    ];

    // قوائم تحت منتج (أو متغيّر) لـ A: 200 بلا أي صف (القائمة موجودة؛ المورد "لا صفوف له" من منظور B).
    private static readonly (string Route, Actor Actor)[] ScopedListings =
    [
        ("api/admin/inventory/{productId:int}/movements", Actor.Admin),
        ("api/admin/inventory/variants/{variantId:int}/movements", Actor.Admin),
        ("api/products/{productId:int}/reviews", Actor.Anonymous),
    ];

    // كتابة تحت منتج لـ A: تُرفض ولا تُنشئ شيئاً مرتبطاً بمتجر آخر.
    private static readonly (string Method, string Route, Actor Actor)[] WritesUnderForeignParent =
    [
        ("POST", "api/products/{productId:int}/reviews", Actor.Customer),
    ];

    private readonly SouqApiFactory _factory;

    public TenantIsolationTests(SouqApiFactory factory) => _factory = factory;

    [Fact]
    public void كل_نقطة_بمعرّف_مورد_مغطّاة_في_جدول_العزل()
    {
        _factory.CreateClient();
        var covered = ForeignResourceCases.Select(c => $"{c.Method} {c.Route}")
            .Concat(ScopedListings.Select(l => $"GET {l.Route}"))
            .Concat(WritesUnderForeignParent.Select(w => $"{w.Method} {w.Route}"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // نقاط المنصّة (معرّف متجر في المسار عمداً) خارج هذا الجدول: لا وجود لها على مضيف متجر أصلاً
        // (AuthorizationBoundaryTests) وصلاحياتها منصّة فقط (PlatformAdministrationTests).
        var missing = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.Parameters.Count > 0)
            .Where(e => e.Metadata.GetMetadata<PlatformEndpointAttribute>() is null)
            .SelectMany(e => (e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => $"{method} {e.RoutePattern.RawText}"))
            .Where(endpoint => !covered.Contains(endpoint))
            .ToList();

        missing.Should().BeEmpty("كل نقطة تأخذ معرّف مورد متجر تُضاف لجدول العزل بقرار صريح");
    }

    [Fact]
    public async Task موارد_متجر_آخر_غير_موجودة_قراءةً_وتعديلاً_وحذفاً_والرد_404()
    {
        var s = await ArrangeAsync();

        foreach (var c in ForeignResourceCases)
        {
            var url = UrlFor(s, c);
            var response = await ClientFor(s, c.Actor)
                .SendAsync(new HttpRequestMessage(new HttpMethod(c.Method), url) { Content = c.Body?.Invoke() });

            response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{c.Method} {url} من متجر B يجب ألّا يرى مورد A");
        }

        // لم يتغيّر شيء في A: المنتج نشط بصورته، الفئة والكوبون موجودان، والطلب ما زال بانتظار الدفع.
        int productId = s.AIds[Resource.Product], categoryId = s.AIds[Resource.Category];
        int couponId = s.AIds[Resource.Coupon], orderId = s.AIds[Resource.Order];
        var state = await s.StoreA.WithDbAsync(async db => (
            ProductStatus: await db.Products.Where(p => p.Id == productId).Select(p => p.Status).SingleAsync(),
            Images: await db.Products.Where(p => p.Id == productId).Select(p => p.Images.Count()).SingleAsync(),
            Stock: await db.InventoryItems.Where(i => i.ProductId == productId)
                .Select(i => i.OnHand * 1000 + i.LowStockThreshold).SingleAsync(),
            CategoryExists: await db.Categories.AnyAsync(c => c.Id == categoryId),
            CouponValue: await db.Coupons.Where(c => c.Id == couponId).Select(c => c.Value).SingleAsync(),
            OrderStatus: await db.Orders.Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync()));
        // المخزون: الموجود 5 وحدّ التنبيه 5 كما أُنشئ — لم يصحّحه ولم يغيّر حدّه مدير متجر آخر.
        state.Should().Be((ProductStatus.Active, 1, 5005, true, 10m, OrderStatus.Pending));

        var staffId = s.AIds[Resource.StaffAccount];
        (await s.StoreA.WithDbAsync(db => db.Users.Where(u => u.Id == staffId).Select(u => u.Status).SingleAsync()))
            .Should().Be(UserStatus.Active, "مدير متجر B لا يوقف موظّف متجر A");

        // عميل A لم يُحظر ولم يُمحَ، وعنوانه باقٍ كما هو.
        var customerId = s.AIds[Resource.Customer];
        (await s.StoreA.WithDbAsync(db => db.Customers.Where(c => c.Id == customerId)
                .Select(c => new { c.Status, Erased = c.ErasedAt != null, Addresses = c.Addresses.Count() }).SingleAsync()))
            .Should().BeEquivalentTo(new { Status = CustomerStatus.Active, Erased = false, Addresses = 1 });

        // تقييم A ما زال معتمداً بلا قرار مشرف من B.
        var reviewId = s.AIds[Resource.Review];
        (await s.StoreA.WithDbAsync(db => db.Reviews.Where(r => r.Id == reviewId)
                .Select(r => new { r.Status, r.ModeratedByUserId }).SingleAsync()))
            .Should().BeEquivalentTo(new { Status = ReviewStatus.Approved, ModeratedByUserId = (int?)null });

        // وإشعار عميل A ما زال غير مقروء.
        var notificationId = s.AIds[Resource.Notification];
        (await s.StoreA.WithDbAsync(db => db.Notifications.Where(n => n.Id == notificationId).Select(n => n.ReadAt).SingleAsync()))
            .Should().BeNull();
    }

    [Fact]
    public async Task القوائم_تحت_مورد_متجر_آخر_فارغة()
    {
        var s = await ArrangeAsync();
        var productId = s.AIds[Resource.Product];

        // في A توجد حركة المخزون الابتدائي فعلاً — B لا يراها.
        (await CountAsync(s.AdminA, $"/api/admin/inventory/{productId}/movements")).Should().BeGreaterThan(0);
        (await CountAsync(s.AdminA, $"/api/admin/inventory/variants/{s.AIds[Resource.ProductVariant]}/movements")).Should().BeGreaterThan(0);

        foreach (var (route, actor) in ScopedListings)
        {
            var url = "/" + Regex.Replace(route, @"\{(\w+)[^}]*\}", m =>
                (m.Groups[1].Value == "variantId" ? s.AIds[Resource.ProductVariant] : productId).ToString());
            (await CountAsync(ClientFor(s, actor), url)).Should().Be(0, $"GET {url} من متجر B");
        }
    }

    [Fact]
    public async Task قوائم_المتجر_لا_تُظهر_صفوف_متجر_آخر()
    {
        var s = await ArrangeAsync();
        var bProduct = await s.StoreB.CreateProductAsync(s.AdminB, price: 7m, stock: 2);

        (await IdsAsync(s.StoreB.Anonymous(), "/api/products?pageSize=100")).Should().Equal(bProduct);
        (await IdsAsync(s.AdminB, "/api/admin/inventory?pageSize=100")).Should().Equal(bProduct);
        (await IdsAsync(s.AdminB, "/api/admin/inventory/low-stock?pageSize=100")).Should().Equal(bProduct);
        (await IdsAsync(s.AdminB, "/api/admin/products?pageSize=100")).Should().Equal(bProduct);
        (await IdsAsync(s.AdminB, "/api/admin/customers?pageSize=100")).Should().NotContain(s.AIds[Resource.Customer]);
        (await IdsAsync(s.AdminB, $"/api/orders?customerId={s.AIds[Resource.Customer]}&pageSize=100")).Should().BeEmpty();
        (await IdsAsync(s.AdminB, "/api/coupons?pageSize=100")).Should().BeEmpty();
        (await IdsAsync(s.AdminB, "/api/orders?pageSize=100")).Should().BeEmpty();
        (await IdsAsync(s.AdminB, "/api/admin/reviews?pageSize=100")).Should().BeEmpty();
        (await IdsAsync(s.AdminB, $"/api/admin/reviews?productId={s.AIds[Resource.Product]}&pageSize=100")).Should().BeEmpty();
        var bCategories = await s.StoreB.Anonymous().GetFromJsonAsync<List<TestApi.IdBody>>("/api/categories", TestApi.Json);
        bCategories!.Select(c => c.Id).Should().ContainSingle().And.NotContain(s.AIds[Resource.Category]);
        var bAdminCategories = await s.AdminB.GetFromJsonAsync<List<TestApi.IdBody>>("/api/admin/categories", TestApi.Json);
        bAdminCategories!.Select(c => c.Id).Should().Equal(bCategories!.Select(c => c.Id));

        // A يرى صفوفه لا صفوف B.
        (await IdsAsync(s.StoreA.Anonymous(), "/api/products?pageSize=100"))
            .Should().Contain(s.AIds[Resource.Product]).And.NotContain(bProduct);
    }

    [Fact]
    public async Task لا_مرجع_لمورد_متجر_آخر_عند_الكتابة()
    {
        var s = await ArrangeAsync();
        int aCategory = s.AIds[Resource.Category], aProduct = s.AIds[Resource.Product];

        (await ProblemAsync(await s.AdminB.PostAsJsonAsync("/api/products", TestApi.ProductBody(aCategory, 5m, 1, "منتج"))))
            .Should().Be((HttpStatusCode.BadRequest, "CategoryNotFound"));

        (await ProblemAsync(await s.AdminB.PostAsJsonAsync("/api/categories",
                TestApi.CategoryBody($"child-{Guid.NewGuid():N}"[..20], "فرعية", parentId: aCategory))))
            .Should().Be((HttpStatusCode.BadRequest, "ParentNotFound"));

        var bProduct = await s.StoreB.CreateProductAsync(s.AdminB);
        (await ProblemAsync(await s.AdminB.PutAsJsonAsync($"/api/products/{bProduct}",
                TestApi.ProductUpdateBody(aCategory, $"b-{Guid.NewGuid():N}"[..20], 5m))))
            .Should().Be((HttpStatusCode.BadRequest, "CategoryNotFound"));

        // ============================================================================
        // أبٌ من متجرٍ آخر عند **التعديل** لا عند الإنشاء وحده (M15).
        //
        // كان `ParentNotFound` مُختبَراً على POST فقط. والحارس في المعالجين معاً يقرأ القائمة المُرشَّحة
        // بالمستأجر، فالسلوك صحيح اليوم — لكنّ اختبارَ نصفِ السطح يعني أنّ النصف الآخر لو فقد حارسه لما
        // سقط شيء. وهو بالضبط الفرق الذي تدّعي وثيقة الصلاحيات تغطيتَه.
        // ============================================================================
        var bCategory = await s.StoreB.CreateCategoryAsync(s.AdminB);
        (await ProblemAsync(await s.AdminB.PutAsJsonAsync($"/api/categories/{bCategory}",
                TestApi.CategoryBody($"upd-{Guid.NewGuid():N}"[..20], "معدّلة", parentId: aCategory))))
            .Should().Be((HttpStatusCode.BadRequest, "ParentNotFound"));

        // صورة منتج A تحت منتج B: غير موجودة حذفاً، ولا تدخل ترتيب صوره — وتبقى في A كما هي.
        var aImage = s.AIds[Resource.ProductImage];
        (await ProblemAsync(await s.AdminB.DeleteAsync($"/api/admin/products/{bProduct}/images/{aImage}")))
            .Should().Be((HttpStatusCode.NotFound, "NotFound"));
        (await ProblemAsync(await s.AdminB.PutAsJsonAsync($"/api/admin/products/{bProduct}/images/order", new { imageIds = new[] { aImage } })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "InvalidProductData"));
        (await s.StoreA.WithDbAsync(db => db.Products.Where(p => p.Id == aProduct).Select(p => p.Images.Count()).SingleAsync()))
            .Should().Be(1);

        (await ProblemAsync(await s.StoreB.PlaceOrderAsync(s.CustomerB, aProduct, 1)))
            .Should().Be((HttpStatusCode.BadRequest, "ProductNotFound"));

        // متغيّر A مع منتج B الحقيقي (المتغيّرات، V1): لا يُسعَّر منتج B به ولا يدخل سلة — المتغيّر يُقبل من منتج السطر نفسه.
        var aVariant = s.AIds[Resource.ProductVariant];
        (await ProblemAsync(await s.CustomerB.PostAsJsonAsync("/api/orders", new
            {
                shippingAddress = "عمّان — عنوان اختبار",
                items = new[] { new { productId = bProduct, quantity = 1, variantId = aVariant } },
            })))
            .Should().Be((HttpStatusCode.BadRequest, "ProductNotFound"));
        (await ProblemAsync(await s.CustomerB.PostAsJsonAsync("/api/basket/items", new { productId = bProduct, variantId = aVariant, quantity = 1 })))
            .Should().Be((HttpStatusCode.NotFound, "NotFound"));
        (await s.StoreB.WithDbAsync(db => db.Baskets.SelectMany(b => b.Lines).CountAsync(l => l.VariantId == aVariant))).Should().Be(0);

        // منتج A لا يدخل سلة على مضيف B — لزائر ولا لعميل (غير موجود من منظور B).
        (await ProblemAsync(await s.StoreB.Anonymous().PostAsJsonAsync("/api/basket/items", new { productId = aProduct, quantity = 1 })))
            .Should().Be((HttpStatusCode.NotFound, "NotFound"));
        (await ProblemAsync(await s.CustomerB.PostAsJsonAsync("/api/basket/items", new { productId = aProduct, quantity = 1 })))
            .Should().Be((HttpStatusCode.NotFound, "NotFound"));

        // كوبون متجر A لا يُسعَّر على مضيف B. المُقيِّم صار واحداً بعد M8 (TD-06 حذف نقطة المعاينة الثانية)،
        // والرفض فيه **نتيجة داخل السلة** لا مشكلة HTTP: 200 بسلّة معروضة وكوبون غير مطبَّق برمزه.
        var foreign = await s.StoreB.Anonymous().GetAsync($"/api/basket/quote?couponCode={s.ACouponCode}");
        foreign.StatusCode.Should().Be(HttpStatusCode.OK);
        var foreignQuote = await foreign.Content.ReadFromJsonAsync<JsonDocument>(TestApi.Json);
        var foreignCoupon = foreignQuote!.RootElement.GetProperty("coupon");
        (foreignCoupon.GetProperty("applied").GetBoolean(), foreignCoupon.GetProperty("errorCode").GetString())
            .Should().Be((false, "CouponNotFound"));

        (await ProblemAsync(await s.CustomerB.PostAsJsonAsync($"/api/products/{aProduct}/reviews",
                new { rating = 5, comment = "رائع" })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "NotEligible"));

        // منتج A لا يدخل مفضّلة عميل B بالدمج أيضاً — يُتجاهل كمنتج مجهول.
        (await s.CustomerB.PostAsJsonAsync("/api/wishlist/merge", new { productIds = new[] { aProduct } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await s.StoreB.WithDbAsync(db => db.WishlistItems.CountAsync())).Should().Be(0);
    }

    [Fact]
    public async Task خيارات_متجر_آخر_وقيمه_ومتغيّراته_لا_تُستخدم_مع_منتج_المتصل()
    {
        var s = await ArrangeAsync();
        var adminA = s.AdminA;
        var aProduct = await s.StoreA.CreateProductAsync(adminA, price: 10m, stock: 3);
        (await VariantAdminApi.SetOptionsAsync(adminA, aProduct, [VariantAdminApi.Option("المقاس", ["S", "M"])])).EnsureSuccessStatusCode();
        var aDetails = await VariantAdminApi.ProductAsync(adminA, aProduct);
        var aValue = VariantAdminApi.ValueId(aDetails, "المقاس", "M");
        var aVariant = (await VariantAdminApi.CreateVariantsOkAsync(adminA, aProduct, new { optionValueIds = new[] { aValue }, price = 12m })).Single();

        var bProduct = await s.StoreB.CreateProductAsync(s.AdminB, price: 7m, stock: 2);
        (await VariantAdminApi.SetOptionsAsync(s.AdminB, bProduct, [VariantAdminApi.Option("المقاس", ["S", "M"])])).EnsureSuccessStatusCode();
        var bDetails = await VariantAdminApi.ProductAsync(s.AdminB, bProduct);

        // معرّف خيار A أو قيمته داخل تعريف منتج B: غير موجود في منتج B — ولا يُنسخ ولا يُنقل.
        (await ProblemAsync(await VariantAdminApi.SetOptionsAsync(s.AdminB, bProduct, [new
            {
                id = aDetails.Options.Single().Id, names = VariantAdminApi.Names("المقاس"),
                values = new[] { new { id = (int?)aValue, names = VariantAdminApi.Names("M") } },
            }])))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "OptionNotFound"));
        (await ProblemAsync(await VariantAdminApi.CreateVariantsAsync(s.AdminB, bProduct, [new { optionValueIds = new[] { aValue }, price = 5m }])))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "OptionValueNotFound"));
        foreach (var route in new[] { $"variants/{aVariant}", $"variants/{aVariant}/status", $"variants/{aVariant}/default" })
            (await s.AdminB.PutAsJsonAsync($"/api/admin/products/{bProduct}/{route}", new { price = 1m, isActive = false })).StatusCode
                .Should().Be(HttpStatusCode.NotFound, $"متغيّر A عبر منتج B ({route})");

        // القاعدة نفسها ترفض ربط متغيّر B بقيمة A — المفتاح الأجنبي يحمل المتجر.
        var bVariant = bDetails.Variants.Single().Id;
        var crossStoreLink = () => s.StoreB.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [ProductVariantOptionValues] ([TenantId], [OptionValueId], [ProductVariantId], [CreatedAt])
            SELECT [TenantId], {aValue}, [Id], SYSUTCDATETIME() FROM [ProductVariants] WHERE [Id] = {bVariant}
            """));
        await crossStoreLink.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();

        var aState = await VariantAdminApi.ProductAsync(adminA, aProduct);
        aState.Variants.Should().HaveCount(2).And.OnlyContain(v => v.IsActive && v.Price >= 10m);
        aState.Options.Single().Values.Select(v => v.Names["ar"]).Should().Equal("S", "M");
        (await VariantAdminApi.ProductAsync(s.AdminB, bProduct)).Variants.Should().ContainSingle();
    }

    [Fact]
    public async Task توكن_متجر_على_مضيف_متجر_آخر_يُرفض_بـ_401()
    {
        var s = await ArrangeAsync();
        var tokenA = await s.StoreA.AdminTokenAsync();
        var tokenB = await s.StoreB.AdminTokenAsync();

        (await s.StoreA.Authorized(tokenA).GetAsync("/api/coupons")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await s.StoreA.Authorized(tokenA, s.StoreB.Host).GetAsync("/api/coupons")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await s.StoreA.Authorized(tokenB, s.StoreA.Host).GetAsync("/api/coupons")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await s.StoreA.Authorized(tokenB, s.StoreA.Host).GetAsync("/api/orders/mine")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task التفرّد_داخل_المتجر_لا_على_المنصّة()
    {
        var s = await ArrangeAsync();

        var slug = $"shared-{Guid.NewGuid():N}"[..20];
        (await s.AdminA.PostAsJsonAsync("/api/categories", TestApi.CategoryBody(slug, "مشتركة"))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await s.AdminB.PostAsJsonAsync("/api/categories", TestApi.CategoryBody(slug, "مشتركة"))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await ProblemAsync(await s.AdminB.PostAsJsonAsync("/api/categories", TestApi.CategoryBody(slug, "مكرّرة"))))
            .Should().Be((HttpStatusCode.Conflict, "SlugTaken"));

        // معرّف المنتج النصّي وSKU فريدان داخل المتجر فقط.
        var bCategory = await s.StoreB.CreateCategoryAsync(s.AdminB);
        var sku = $"SKU-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        (await s.AdminA.PostAsJsonAsync("/api/products", TestApi.ProductBody(s.AIds[Resource.Category], slug: slug, sku: sku)))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await s.AdminB.PostAsJsonAsync("/api/products", TestApi.ProductBody(bCategory, slug: slug, sku: sku)))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await ProblemAsync(await s.AdminB.PostAsJsonAsync("/api/products", TestApi.ProductBody(bCategory, slug: slug))))
            .Should().Be((HttpStatusCode.Conflict, "ProductSlugTaken"));
        (await ProblemAsync(await s.AdminB.PostAsJsonAsync("/api/products", TestApi.ProductBody(bCategory, sku: sku.ToLowerInvariant()))))
            .Should().Be((HttpStatusCode.Conflict, "SkuTaken"));

        var coupon = new { code = s.ACouponCode, type = "Percentage", value = 5m };
        (await s.AdminB.PostAsJsonAsync("/api/coupons", coupon)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await ProblemAsync(await s.AdminB.PostAsJsonAsync("/api/coupons", coupon)))
            .Should().Be((HttpStatusCode.Conflict, "DuplicateCode"));

        // الحسابات لكل متجر (D-06): البريد نفسه حسابان مستقلّان في متجرين، ومكرّر داخل المتجر نفسه يُرفض.
        var email = $"both-{Guid.NewGuid():N}@souq.test";
        await s.StoreA.NewCustomerAsync(email);
        await s.StoreB.NewCustomerAsync(email);
        var owners = await Task.WhenAll(
            s.StoreA.WithDbAsync(db => db.Customers.Where(c => c.Email == email).Select(c => c.TenantId).SingleAsync()),
            s.StoreB.WithDbAsync(db => db.Customers.Where(c => c.Email == email).Select(c => c.TenantId).SingleAsync()));
        owners.Should().OnlyHaveUniqueItems();
        (await ProblemAsync(await s.StoreB.Anonymous().PostAsJsonAsync("/api/auth/register",
                new { fullName = "مكرّر", email, password = "Customer-Pass-1" })))
            .Should().Be((HttpStatusCode.Conflict, "EmailTaken"));
    }

    [Fact]
    public async Task حارس_الكتابة_يرفض_تعديل_صف_متجر_آخر_ولا_يُحفظ_شيء()
    {
        var s = await ArrangeAsync();
        var productId = s.AIds[Resource.Product];

        await using (var scope = await _factory.TenantScopeAsync(s.B.Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // تجاوز متعمَّد للمرشّح يحاكي ثغرة قراءة — الحارس هو خط الدفاع الثاني.
            var foreign = await db.Products.IgnoreQueryFilters().SingleAsync(p => p.Id == productId);
            foreign.Archive();

            var act = () => db.SaveChangesAsync();
            await act.Should().ThrowAsync<CrossTenantWriteException>();
        }

        (await s.StoreA.WithDbAsync(db => db.Products.Where(p => p.Id == productId).Select(p => p.IsActive).SingleAsync()))
            .Should().BeTrue();
        _factory.Logs.Entries.Should().Contain(e =>
            e.Level == LogLevel.Critical && e.Message.Contains("cross-tenant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task إضافة_صف_بمتجر_صريح_غير_متجر_السياق_تُرفض()
    {
        var store = await _factory.CreateStoreAsync();
        var defaultTenant = await _factory.DefaultTenantAsync();

        await using var scope = await _factory.TenantScopeAsync(store.Tenant);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = new Category($"planted-{Guid.NewGuid():N}"[..20], TestApi.ArabicText("مزروعة"));
        db.Categories.Add(category);
        db.Entry(category).Property(nameof(Category.TenantId)).CurrentValue = defaultTenant.Id;

        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<CrossTenantWriteException>();
    }

    [Fact]
    public async Task بلا_سياق_متجر_لا_تُقرأ_ولا_تُكتب_بيانات_المتاجر()
    {
        _factory.CreateClient();

        await using (var none = _factory.Services.CreateAsyncScope())
        {
            var db = none.ServiceProvider.GetRequiredService<AppDbContext>();
            var read = () => db.Products.CountAsync();
            Chain((await read.Should().ThrowAsync<Exception>()).Which)
                .Should().Contain(e => e is TenantContextMissingException, "استعلام بلا متجر لا يعيد كل الصفوف أبداً");
        }

        await using (var platform = _factory.Services.CreateAsyncScope())
        {
            platform.ServiceProvider.GetRequiredService<TenantContext>().UsePlatform();
            var db = platform.ServiceProvider.GetRequiredService<AppDbContext>();

            var read = () => db.Orders.AnyAsync();
            Chain((await read.Should().ThrowAsync<Exception>()).Which).Should().Contain(e => e is TenantContextMissingException);

            db.Categories.Add(new Category($"platform-{Guid.NewGuid():N}"[..20], TestApi.ArabicText("منصّة")));
            var write = () => db.SaveChangesAsync();
            await write.Should().ThrowAsync<TenantContextMissingException>();
        }
    }

    [Fact]
    public async Task ملفات_متجر_لا_تُخدَم_على_مضيف_متجر_آخر()
    {
        var s = await ArrangeAsync();

        var upload = await s.AdminA.PostAsync($"/api/products/{s.AIds[Resource.Product]}/image", FileBody(PngBytes, "a.png"));
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var url = (await upload.Content.ReadFromJsonAsync<ImageBody>(TestApi.Json))!.ImageUrl;

        url.Should().StartWith($"/uploads/tenants/{(await s.StoreA.TenantAsync()).Id}/images/");
        (await s.StoreA.Anonymous().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await s.StoreB.Anonymous().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task رمز_سلة_زائر_متجر_لا_يفتح_سلته_على_مضيف_متجر_آخر()
    {
        var s = await ArrangeAsync();
        var aProduct = s.AIds[Resource.Product];
        var added = await s.StoreA.SecureClient().PostAsJsonAsync("/api/basket/items", new { productId = aProduct, quantity = 1 });
        added.StatusCode.Should().Be(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
        var token = TestApi.GuestBasketToken(added);

        // الرمز نفسه على مضيف B: لا سلة (البحث مُرشَّح بالمتجر) ويُمسح الرمز، ولا تعديل ولا حذف لسطر A.
        var onB = s.StoreB.SecureClient(handleCookies: false);
        onB.DefaultRequestHeaders.Add("Cookie", $"{TestApi.GuestBasketCookie}={token}");
        var read = await onB.GetAsync("/api/basket");
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        (await read.Content.ReadFromJsonAsync<TestApi.BasketBody>(TestApi.Json))!.Lines.Should().BeEmpty();
        read.Headers.GetValues("Set-Cookie").Should().Contain(h => h.StartsWith($"{TestApi.GuestBasketCookie}=;", StringComparison.Ordinal));
        (await onB.PutAsJsonAsync($"/api/basket/items/{aProduct}", new { quantity = 5 })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await onB.DeleteAsync($"/api/basket/items/{aProduct}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var hash = Souq.Application.Features.Baskets.GuestBasketTokens.Hash(token);
        (await s.StoreA.WithDbAsync(db => db.Baskets.Where(b => b.GuestTokenHash == hash)
                .SelectMany(b => b.Lines).Select(l => l.Quantity).SingleAsync()))
            .Should().Be(1, "سلة A كما هي");
    }

    // ── الإعداد: موارد حقيقية في A عبر الـ API، ومتجر B فعلي بمديره وعميله على مضيفه ──
    private sealed record Arranged(
        TestApi StoreA, TestApi StoreB, TestStore B, HttpClient AdminA, HttpClient AdminB, HttpClient CustomerB,
        IReadOnlyDictionary<Resource, int> AIds, string ACouponCode, string AProductSlug, string AOrderToken);

    private async Task<Arranged> ArrangeAsync()
    {
        var storeA = new TestApi(_factory);
        var adminA = await storeA.AdminAsync();
        var categoryId = await storeA.CreateCategoryAsync(adminA);
        var productId = await storeA.CreateProductAsync(adminA, price: 10m, stock: 5, categoryId: categoryId);
        var upload = await adminA.PostAsync($"/api/products/{productId}/image", FileBody(PngBytes, "a.png"));
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var imageId = (await upload.Content.ReadFromJsonAsync<TestApi.IdBody>(TestApi.Json))!.Id;
        var productSlug = await storeA.WithDbAsync(db => db.Products.Where(p => p.Id == productId).Select(p => p.Slug).SingleAsync());
        var couponCode = $"ISO{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var couponResponse = await adminA.PostAsJsonAsync("/api/coupons", new { code = couponCode, type = "Percentage", value = 10m });
        couponResponse.StatusCode.Should().Be(HttpStatusCode.Created, await couponResponse.Content.ReadAsStringAsync());
        var couponId = (await couponResponse.Content.ReadFromJsonAsync<TestApi.IdBody>(TestApi.Json))!.Id;
        // معطّلة عمداً: A هو المتجر الافتراضي المشترك — طريقة مفعّلة فيه تلزم كل دفع في الاختبارات الأخرى باختيار شحن.
        var methodResponse = await adminA.PostAsJsonAsync("/api/admin/shipping-methods", new { name = "عزل", price = 1m, isActive = false });
        methodResponse.StatusCode.Should().Be(HttpStatusCode.Created, await methodResponse.Content.ReadAsStringAsync());
        var shippingMethodId = (await methodResponse.Content.ReadFromJsonAsync<TestApi.IdBody>(TestApi.Json))!.Id;
        // مفردة بحث لمتجر A (M3): لا أثر لها على بقيّة الاختبارات — كلمة لا ترد في أي كتالوج.
        var synonymResponse = await adminA.PostAsJsonAsync("/api/admin/search-synonyms",
            new { culture = "ar", term = $"زقفون{Guid.NewGuid():N}"[..14], expansion = "مكنسة" });
        synonymResponse.StatusCode.Should().Be(HttpStatusCode.Created, await synonymResponse.Content.ReadAsStringAsync());
        var searchSynonymId = (await synonymResponse.Content.ReadFromJsonAsync<TestApi.IdBody>(TestApi.Json))!.Id;
        var (customerA, customerEmail) = await storeA.NewCustomerAsync();
        var addressResponse = await customerA.PostAsJsonAsync("/api/account/addresses", new
        {
            address = new { recipientName = "عميل A", phone = "0790000000", country = "JO", city = "عمّان", line1 = "شارع A" },
        });
        addressResponse.StatusCode.Should().Be(HttpStatusCode.Created, await addressResponse.Content.ReadAsStringAsync());
        var addressId = (await addressResponse.Content.ReadFromJsonAsync<TestApi.IdBody>(TestApi.Json))!.Id;
        var customerId = await storeA.WithDbAsync(db => db.Customers.Where(c => c.Email == customerEmail).Select(c => c.Id).SingleAsync());
        var placed = await storeA.PlaceOrderAsync(customerA, productId, 1);
        placed.StatusCode.Should().Be(HttpStatusCode.Created, await placed.Content.ReadAsStringAsync());
        var orderId = (await placed.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;
        var orderToken = await storeA.WithDbAsync(db => db.Orders.Where(o => o.Id == orderId).Select(o => o.TrackingToken).SingleAsync());
        // تقييم معتمد في A (المتجر الافتراضي ينشر فوراً) على منتج مستقل: طلبه المُسلَّم يغيّر مخزون منتجه، لا المنتج المفحوص أدناه.
        var reviewedProductId = await storeA.CreateProductAsync(adminA, price: 10m, stock: 5, categoryId: categoryId);
        var delivered = await storeA.PlaceOrderAsync(customerA, reviewedProductId, 1);
        delivered.StatusCode.Should().Be(HttpStatusCode.Created, await delivered.Content.ReadAsStringAsync());
        var deliveredId = (await delivered.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;
        (await customerA.PostAsync($"/api/orders/{deliveredId}/confirm-payment", null)).EnsureSuccessStatusCode();
        (await adminA.PutAsJsonAsync($"/api/orders/{deliveredId}/status", new { action = "Ship" })).EnsureSuccessStatusCode();
        (await adminA.PutAsJsonAsync($"/api/orders/{deliveredId}/status", new { action = "Deliver" })).EnsureSuccessStatusCode();
        var reviewResponse = await customerA.PostAsJsonAsync($"/api/products/{reviewedProductId}/reviews", new { rating = 4, comment = "من A" });
        reviewResponse.StatusCode.Should().Be(HttpStatusCode.Created, await reviewResponse.Content.ReadAsStringAsync());
        var reviewId = (await reviewResponse.Content.ReadFromJsonAsync<TestApi.IdBody>(TestApi.Json))!.Id;
        var notificationId = await storeA.WithDbAsync(async db =>
        {
            var userId = await db.Customers.Where(c => c.Id == customerId).Select(c => c.UserId).SingleAsync();
            var notification = new Notification(userId, NotificationKinds.OrderStatus, "{}");
            db.Notifications.Add(notification);
            await db.SaveChangesAsync();
            return notification.Id;
        });
        var staffEmail = await _factory.CreateStoreUserAsync(await _factory.DefaultTenantAsync(), Roles.TenantStaff);
        var staffId = await storeA.WithDbAsync(db => db.Users.Where(u => u.Email == staffEmail).Select(u => u.Id).SingleAsync());

        var variantId = await storeA.WithDbAsync(db =>
            db.Products.Where(p => p.Id == productId).SelectMany(p => p.Variants).Select(v => v.Id).SingleAsync());

        // ====================================================================
        // فاتورةُ اشتراكٍ **صادرة** لمتجر A، تُكتب في القاعدة مباشرةً لا عبر الـ API.
        //
        // والسببُ أنّ الإصدار عبر الـ API يحتاج إعدادَ فوترةٍ عالميّاً يتشاركه كلُّ اختبار في
        // هذه المجموعة، فكان ضبطُه من هنا يُسرّب حالةً إلى رحلاتٍ أخرى. وما يحتاجه هذا الجدول
        // صفٌّ صادرٌ يخصّ A فحسب.
        // ====================================================================
        var tenantAId = (await _factory.DefaultTenantAsync()).Id;
        var invoiceId = await storeA.WithDbAsync(async db =>
        {
            var now = DateTime.UtcNow;
            var invoice = new Souq.Domain.Platform.PlatformInvoice(
                tenantAId, "JOD", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
            invoice.AddLine("اشتراك (عزل)", 1, new Souq.Domain.ValueObjects.Money(1m, "JOD"));
            invoice.Issue($"ISO{Guid.NewGuid():N}"[..16], now, now.AddDays(30),
                Souq.Domain.ValueObjects.Money.Zero("JOD"), null,
                "سوق (عزل)", null, null, "متجر A", null, null);
            db.PlatformInvoices.Add(invoice);
            await db.SaveChangesAsync();
            return invoice.Id;
        });

        var b = await _factory.CreateStoreAsync();
        var storeB = storeA.ForStore(b);
        return new Arranged(storeA, storeB, b, adminA, await storeB.AdminAsync(), (await storeB.NewCustomerAsync()).Client,
            new Dictionary<Resource, int>
            {
                [Resource.Product] = productId, [Resource.ProductVariant] = variantId, [Resource.ProductImage] = imageId, [Resource.Category] = categoryId,
                [Resource.Coupon] = couponId, [Resource.Order] = orderId, [Resource.StaffAccount] = staffId,
                [Resource.Customer] = customerId, [Resource.CustomerAddress] = addressId, [Resource.ShippingMethod] = shippingMethodId,
                [Resource.SearchSynonym] = searchSynonymId,
                [Resource.Review] = reviewId, [Resource.Notification] = notificationId,
                [Resource.PlatformInvoice] = invoiceId,
            },
            couponCode, productSlug, orderToken);
    }

    // كل معامل مسار بمعرّف A الحقيقي: الصورة بصورة منتج A، والمعرّف النصّي بمعرّف منتج A، والباقي بمورد الحالة.
    private static string UrlFor(Arranged s, ForeignCase c) =>
        "/" + Regex.Replace(c.Route, @"\{(\w+)[^}]*\}", m => m.Groups[1].Value switch
        {
            "imageId" => s.AIds[Resource.ProductImage].ToString(),
            "variantId" => s.AIds[Resource.ProductVariant].ToString(),
            "id" when c.Resource == Resource.ProductVariant => s.AIds[Resource.Product].ToString(),
            "slug" => s.AProductSlug,
            "token" => s.AOrderToken,
            _ => s.AIds[c.Resource].ToString(),
        });

    private static HttpClient ClientFor(Arranged s, Actor actor) => actor switch
    {
        Actor.Admin => s.AdminB,
        Actor.Customer => s.CustomerB,
        _ => s.StoreB.Anonymous(),
    };

    private static async Task<List<int>> IdsAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<TestApi.PageBody<TestApi.IdBody>>(url, TestApi.Json))!.Items.Select(i => i.Id).ToList();

    private static async Task<int> CountAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return (await response.Content.ReadFromJsonAsync<CountBody>(TestApi.Json))!.TotalCount;
    }

    private static async Task<(HttpStatusCode, string?)> ProblemAsync(HttpResponseMessage response) =>
        (response.StatusCode, (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))?.Code);

    private static HttpContent JsonBody(object body) => JsonContent.Create(body, options: TestApi.Json);

    private static HttpContent FileBody(byte[] bytes, string fileName)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", fileName);
        return form;
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            yield return current;
    }

    private sealed record CountBody(int TotalCount);
    private sealed record ImageBody(string ImageUrl);
}
