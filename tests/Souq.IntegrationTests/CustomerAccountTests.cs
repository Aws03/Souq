using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Souq.Domain.Common;
using Souq.Domain.Identity;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// العملاء والعناوين (المرحلة 7) عبر HTTP وSQL Server الحقيقيين: الملف ودفتر العناوين للعميل نفسه، الطلب بعنوان من الدفتر،
// الملكية داخل المتجر (عميل لا يمسّ عنوان غيره)، قائمة الإدارة وتفاصيلها وحظرها، والتصدير والمحو.
// العزل بين المتاجر في TenantIsolationTests.
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class CustomerAccountTests
{
    private const string Password = "Customer-Pass-1";

    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public CustomerAccountTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    [Fact]
    public async Task الملف_ودفتر_العناوين_للعميل_نفسه()
    {
        var (customer, email) = await _api.NewCustomerAsync();

        var profile = await customer.GetFromJsonAsync<ProfileBody>("/api/account/profile", TestApi.Json);
        (profile!.Email, profile.Status, profile.Addresses.Count).Should().Be((email, "Active", 0));

        var updated = await customer.PutAsJsonAsync("/api/account/profile", new { fullName = "سارة محمد", phone = "+962790000000" });
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        (await _api.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.FullName).SingleAsync()))
            .Should().Be("سارة محمد", "اسم الحساب يتبع اسم الملف");

        var home = await AddAddressAsync(customer, Address(city: "عمّان"));
        (home.IsDefaultShipping, home.IsDefaultBilling).Should().Be((true, true));
        var work = await AddAddressAsync(customer, Address(city: "إربد"), defaultShipping: true);
        (await AddressesAsync(customer)).Select(a => (a.Id, a.IsDefaultShipping, a.IsDefaultBilling))
            .Should().Equal((work.Id, true, false), (home.Id, false, true));

        (await customer.PutAsync($"/api/account/addresses/{home.Id}/default-shipping", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var edited = await customer.PutAsJsonAsync($"/api/account/addresses/{work.Id}", Address(city: "الزرقاء"));
        (await edited.Content.ReadFromJsonAsync<AddressBody>(TestApi.Json))!.City.Should().Be("الزرقاء");

        (await customer.DeleteAsync($"/api/account/addresses/{home.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var remaining = (await AddressesAsync(customer)).Should().ContainSingle().Which;
        (remaining.Id, remaining.IsDefaultShipping, remaining.IsDefaultBilling).Should().Be((work.Id, true, true));

        // الشكل يحرسه المدقّق (400) والصيغة يحرسها الكيان (422).
        (await ProblemAsync(await customer.PostAsJsonAsync("/api/account/addresses", new { address = Address(country: "Jordan") })))
            .Should().Be((HttpStatusCode.BadRequest, "ValidationFailed"));
        (await ProblemAsync(await customer.PostAsJsonAsync("/api/account/addresses", new { address = Address(phone: "abc-phone") })))
            .Should().Be((HttpStatusCode.UnprocessableEntity, "InvalidCustomerData"));
    }

    [Fact]
    public async Task عميل_لا_يصل_لعنوان_عميل_آخر_في_المتجر_نفسه()
    {
        var (owner, _) = await _api.NewCustomerAsync();
        var (other, _) = await _api.NewCustomerAsync();
        var address = await AddAddressAsync(owner, Address());

        foreach (var response in new[]
        {
            await other.PutAsJsonAsync($"/api/account/addresses/{address.Id}", Address(city: "مسروق")),
            await other.PutAsync($"/api/account/addresses/{address.Id}/default-billing", null),
            await other.DeleteAsync($"/api/account/addresses/{address.Id}"),
        })
            (await ProblemAsync(response)).Should().Be((HttpStatusCode.NotFound, "NotFound"));

        (await AddressesAsync(owner)).Should().ContainSingle().Which.City.Should().Be("عمّان");

        // ولا يُستخدم عنوان غيره في طلب.
        var productId = await _api.CreateProductAsync(await _api.AdminAsync(), price: 10m, stock: 5);
        var stolen = await other.PostAsJsonAsync("/api/orders", new { items = new[] { new { productId, quantity = 1 } }, shippingAddressId = address.Id });
        (await ProblemAsync(stolen)).Should().Be((HttpStatusCode.BadRequest, "AddressNotFound"));
    }

    [Fact]
    public async Task الطلب_بعنوان_من_الدفتر_يحفظ_لقطته_ولا_يتغيّر_بتعديل_العنوان()
    {
        var productId = await _api.CreateProductAsync(await _api.AdminAsync(), price: 10m, stock: 5);
        var (customer, _) = await _api.NewCustomerAsync();
        var address = await AddAddressAsync(customer, Address(city: "العقبة", line1: "شارع الكورنيش 7"));

        var placed = await customer.PostAsJsonAsync("/api/orders", new { items = new[] { new { productId, quantity = 1 } }, shippingAddressId = address.Id });
        placed.StatusCode.Should().Be(HttpStatusCode.Created, await placed.Content.ReadAsStringAsync());
        var orderId = (await placed.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;

        (await customer.PutAsJsonAsync($"/api/account/addresses/{address.Id}", Address(city: "مادبا"))).StatusCode.Should().Be(HttpStatusCode.OK);

        var snapshot = await _api.WithDbAsync(db => db.Orders.Where(o => o.Id == orderId).Select(o => o.ShippingAddress).SingleAsync());
        snapshot.Should().Contain("العقبة").And.Contain("شارع الكورنيش 7").And.NotContain("مادبا");
    }

    [Fact]
    public async Task الإدارة_تسرد_وتفصّل_وتحظر_والمحظور_لا_يطلب_ولا_يُمنع_من_حسابه()
    {
        var admin = await _api.AdminAsync();
        var productId = await _api.CreateProductAsync(admin, price: 10m, stock: 10);
        var (customer, email) = await _api.NewCustomerAsync();
        var customerId = await CustomerIdAsync(email);
        await AddAddressAsync(customer, Address());
        var paid = await PlaceAsync(customer, productId, 2);
        (await customer.PostAsync($"/api/orders/{paid}/confirm-payment", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        await PlaceAsync(customer, productId, 1);                                         // معلّق: لا يُحسب إنفاقاً

        var row = (await admin.GetFromJsonAsync<TestApi.PageBody<CustomerRow>>(
            $"/api/admin/customers?keyword={Uri.EscapeDataString(email)}", TestApi.Json))!.Items.Should().ContainSingle().Which;
        (row.Id, row.OrderCount, row.TotalSpent, row.Currency, row.Status).Should().Be((customerId, 2, 20m, "JOD", "Active"));

        var detail = await admin.GetFromJsonAsync<CustomerDetailBody>($"/api/admin/customers/{customerId}", TestApi.Json);
        (detail!.OrderCount, detail.TotalSpent, detail.Addresses.Count, detail.IsErased).Should().Be((2, 20m, 1, false));
        (await admin.GetFromJsonAsync<TestApi.PageBody<TestApi.IdBody>>($"/api/orders?customerId={customerId}", TestApi.Json))!
            .TotalCount.Should().Be(2);

        (await admin.PutAsJsonAsync($"/api/admin/customers/{customerId}/status", new { status = "Blocked" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ProblemAsync(await _api.PlaceOrderAsync(customer, productId, 1))).Should().Be((HttpStatusCode.Forbidden, "CustomerBlocked"));
        (await customer.GetFromJsonAsync<ProfileBody>("/api/account/profile", TestApi.Json))!.Status.Should().Be("Blocked");

        (await admin.PutAsJsonAsync($"/api/admin/customers/{customerId}/status", new { status = "Active" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _api.PlaceOrderAsync(customer, productId, 1)).StatusCode.Should().Be(HttpStatusCode.Created);

        // الموظّف يرى العملاء (customers.view) ولا يحظرهم ولا يمحوهم (customers.manage للمدير).
        var staff = await _api.LoginAsync(await _factory.CreateStoreUserAsync(await _factory.DefaultTenantAsync(), Roles.TenantStaff),
            SouqApiFactory.StoreAdminPassword);
        (await staff.GetAsync($"/api/admin/customers/{customerId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await staff.PutAsJsonAsync($"/api/admin/customers/{customerId}/status", new { status = "Blocked" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await staff.PostAsync($"/api/admin/customers/{customerId}/erase", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task التصدير_يحوي_بياناته_والمحو_بكلمة_المرور_يزيل_هويته_ويُبقي_طلبه()
    {
        var productId = await _api.CreateProductAsync(await _api.AdminAsync(), price: 10m, stock: 5);
        var (customer, email) = await _api.NewCustomerAsync();
        var customerId = await CustomerIdAsync(email);
        var address = await AddAddressAsync(customer, Address());
        var orderId = await PlaceWithAddressAsync(customer, productId, address.Id);

        var exported = await customer.GetAsync("/api/account/export");
        exported.StatusCode.Should().Be(HttpStatusCode.OK);
        exported.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        var export = await exported.Content.ReadFromJsonAsync<ExportBody>(TestApi.Json);
        (export!.Profile.Email, export.Addresses.Count, export.Orders.Single().Id, export.Orders.Single().Lines.Count)
            .Should().Be((email, 1, orderId, 1));

        (await ProblemAsync(await customer.PostAsJsonAsync("/api/account/erase", new { password = "wrong-password" })))
            .Should().Be((HttpStatusCode.BadRequest, "CurrentPasswordIncorrect"));
        (await customer.PostAsJsonAsync("/api/account/erase", new { password = Password })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // الهوية زالت: البريد القديم لا يدخل، والتوكن القائم سقط فوراً (ختم الأمان تغيّر ونُسي من الذاكرة).
        (await _api.Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password = Password })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await customer.GetAsync("/api/account/profile")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var state = await _api.WithDbAsync(async db => new
        {
            Customer = await db.Customers.Where(c => c.Id == customerId)
                .Select(c => new { c.FullName, c.Email, c.Phone, Addresses = c.Addresses.Count(), c.ErasedAt }).SingleAsync(),
            OrderKept = await db.Orders.AnyAsync(o => o.Id == orderId && o.CustomerId == customerId),
        });
        state.Customer.FullName.Should().Be("عميل محذوف");
        state.Customer.Email.Should().NotContain(email.Split('@')[0]);
        (state.Customer.Phone, state.Customer.Addresses, state.Customer.ErasedAt is null, state.OrderKept).Should().Be((null, 0, false, true));
    }

    [Fact]
    public async Task الإدارة_تصدّر_وتمحو_بطلب_العميل()
    {
        var admin = await _api.AdminAsync();
        var (customer, email) = await _api.NewCustomerAsync();
        var customerId = await CustomerIdAsync(email);
        await AddAddressAsync(customer, Address());

        var export = await admin.GetFromJsonAsync<ExportBody>($"/api/admin/customers/{customerId}/export", TestApi.Json);
        export!.Profile.Email.Should().Be(email);

        (await admin.PostAsync($"/api/admin/customers/{customerId}/erase", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var detail = await admin.GetFromJsonAsync<CustomerDetailBody>($"/api/admin/customers/{customerId}", TestApi.Json);
        (detail!.IsErased, detail.Status, detail.FullName, detail.Addresses.Count).Should().Be((true, "Blocked", "عميل محذوف", 0));
        (await customer.GetAsync("/api/account/profile")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── أدوات ──────────────────────────────────────────────────────────────────

    private static object Address(string city = "عمّان", string line1 = "شارع الجامعة 12", string country = "JO", string phone = "0790000000") =>
        new { recipientName = "سارة", phone, country, city, line1, label = "المنزل" };

    private static async Task<AddressBody> AddAddressAsync(HttpClient customer, object address, bool defaultShipping = false)
    {
        var response = await customer.PostAsJsonAsync("/api/account/addresses", new { address, defaultShipping });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AddressBody>(TestApi.Json))!;
    }

    private static async Task<List<AddressBody>> AddressesAsync(HttpClient customer) =>
        (await customer.GetFromJsonAsync<List<AddressBody>>("/api/account/addresses", TestApi.Json))!;

    private Task<int> CustomerIdAsync(string email) =>
        _api.WithDbAsync(db => db.Customers.Where(c => c.Email == email).Select(c => c.Id).SingleAsync());

    private async Task<int> PlaceAsync(HttpClient customer, int productId, int quantity)
    {
        var response = await _api.PlaceOrderAsync(customer, productId, quantity);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;
    }

    private static async Task<int> PlaceWithAddressAsync(HttpClient customer, int productId, int addressId)
    {
        var response = await customer.PostAsJsonAsync("/api/orders", new { items = new[] { new { productId, quantity = 1 } }, shippingAddressId = addressId });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<TestApi.OrderCreatedBody>(TestApi.Json))!.OrderId;
    }

    private static async Task<(HttpStatusCode, string?)> ProblemAsync(HttpResponseMessage response) =>
        (response.StatusCode, (await response.Content.ReadFromJsonAsync<TestApi.ProblemBody>(TestApi.Json))?.Code);

    private sealed record AddressBody(
        int Id, string? Label, string RecipientName, string Phone, string Country, string City, string? Region,
        string Line1, string? Line2, string? PostalCode, bool IsDefaultShipping, bool IsDefaultBilling);
    private sealed record ProfileBody(int Id, string FullName, string Email, string? Phone, string Status, List<AddressBody> Addresses);
    private sealed record CustomerRow(int Id, string FullName, string Email, string Status, int OrderCount, decimal TotalSpent, string Currency);
    private sealed record CustomerDetailBody(int Id, string FullName, string Status, bool IsErased, int OrderCount, decimal TotalSpent, List<AddressBody> Addresses);
    private sealed record ExportProfileBody(int Id, string FullName, string Email);
    private sealed record ExportLineBody(string ProductName, decimal UnitPrice, int Quantity);
    private sealed record ExportOrderBody(int Id, string Status, decimal Total, List<ExportLineBody> Lines);
    private sealed record ExportBody(ExportProfileBody Profile, List<AddressBody> Addresses, List<ExportOrderBody> Orders);
}
