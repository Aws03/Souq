using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Souq.Infrastructure.Persistence;
using Souq.IntegrationTests.Infrastructure;

namespace Souq.IntegrationTests;

// ============================================================================
// فوترةُ اشتراكات التجّار والتحصيلُ اليدويّ عبر الخادم الحقيقي (C5، ADR-0056).
//
// **ولماذا هذه الرحلة تحديداً؟** لأنّ قرار المالك `D-13` = A جعل اشتراكَ التاجر مصدرَ إيراد سوق
// **الوحيد**: لا عمولةَ تُقتطَع من مال متسوّق، ولا مزوّدَ في الحلقة. فما يُثبَت هنا ليس «تعمل
// الشاشة» بل: هل تستطيع المنصّة أن تُصدر مستنداً صحيحاً وتُحصّله وتُصحّحه وتمنع تاجراً من رؤية
// مستند غيره — وهي كلُّ الطريق بين شيفرةٍ وشركةٍ تُقبَض.
//
// والقيمُ هنا **صناعيّة للاختبار**: لا سعرَ شريحةٍ حقيقيّ (ذلك `C-12`، وهو المالك) ولا نسبةَ
// ضريبةٍ لاختصاصٍ حقيقيّ (ذلك P-06، ويحتاج محاسباً).
// ============================================================================
[Collection(IntegrationCollection.Name)]
public class PlatformInvoicingTests
{
    private readonly SouqApiFactory _factory;
    private readonly TestApi _api;

    public PlatformInvoicingTests(SouqApiFactory factory)
    {
        _factory = factory;
        _api = new TestApi(factory);
    }

    private static readonly JsonSerializerOptions Json = TestApi.Json;

    private static readonly DateTime PeriodStart = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    // إعدادُ الفوترة كما يضبطه المشغّل. العملةُ عملةُ المتجر الافتراضي — لا رمزٌ مكتوب هنا يفترق
    // عمّا تعرفه القاعدة.
    private async Task<HttpClient> ConfiguredOwnerAsync(string currency = "JOD")
    {
        var owner = await _api.PlatformOwnerAsync();
        (await owner.PutAsJsonAsync("/api/platform/billing/settings", new
        {
            currency,
            issuerName = "سوق (اختبار)",
            issuerAddress = "عمّان",
            issuerTaxNumber = "TX-TEST",
            invoiceNumberPrefix = "INV",
            creditNoteNumberPrefix = "CN",
            paymentTermsDays = 30,
            gracePeriodDays = 7,
            paymentInstructions = "حوالةٌ بنكية إلى الحساب الاختباري",
            taxProfileId = (int?)null,
            taxCollectionEnabled = false,
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return owner;
    }

    private static async Task<int> DraftAsync(
        HttpClient owner, int tenantId, decimal unit, decimal quantity = 1, string description = "اشتراكٌ اختباري")
    {
        var created = await owner.PostAsJsonAsync("/api/platform/invoices", new
        {
            tenantId,
            periodStartUtc = PeriodStart,
            periodEndUtc = PeriodEnd,
            includeSubscription = false,
            billingPeriodId = (int?)null,
            lines = new[] { new { description, quantity, unitAmount = unit, taxCategory = (string?)null } },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await created.Content.ReadFromJsonAsync<IdResponse>(Json))!.Id;
    }

    private static async Task<string> IssueAsync(HttpClient owner, int invoiceId)
    {
        var issued = await owner.PostAsJsonAsync($"/api/platform/invoices/{invoiceId}/issue",
            new { billedToTaxNumber = "MERCHANT-TX" });
        issued.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await issued.Content.ReadFromJsonAsync<NumberResponse>(Json))!.Number;
    }

    // ========================================================================
    // **الفشلُ المغلق أوّلاً.** بلا عملةِ فوترةٍ مضبوطة لا تُصدَر فاتورةٌ واحدة — والسببُ يُقال
    // برمزٍ ثابت لا برسالةٍ تُقرأ.
    //
    // وهذا هو الشكلُ الذي دخل به جوابُ المالك `C-15` إلى المنتج: قيمةٌ يُدخلها المشغّل، لا سطرٌ
    // في الشيفرة — قاعدةُ الواجهة البيضاء تمنع كتابةَ رمز عملةٍ في `src/`، ويحرسها اختبارٌ فعلاً.
    // فلو لم يكن الغيابُ حالةً يعرفها النظام ويقولها، لكانت مفاجأةً عند أوّل إصدار.
    // ========================================================================
    [Fact]
    public async Task بلا_عملة_فوترة_مضبوطة_لا_تُصدَر_فاتورة_ويُقال_السبب()
    {
        var owner = await _api.PlatformOwnerAsync();
        var store = await _factory.CreateStoreAsync();

        // ====================================================================
        // حالةُ «لم يُضبَط بعد» تُصنَع بحذف الصفّ من القاعدة مباشرةً، لا عبر الـ API — ولا
        // مفرّ من ذلك: إعدادُ الفوترة **صفٌّ عالميّ واحد** تتشاركه الرحلات في هذه المجموعة،
        // وتفريغُ عملته عبر الـ API يصطدم بقفل `BillingCurrencyLocked` متى سبقته رحلةٌ أصدرت
        // فاتورة. والقفلُ سليم — هو المقصود — فالاختبارُ هو من يبني حالتَه لا المنتجُ من يرخي
        // قاعدتَه.
        // ====================================================================
        await ResetBillingSettingsAsync();

        var settings = await (await owner.GetAsync("/api/platform/billing/settings"))
            .Content.ReadFromJsonAsync<SettingsView>(Json);
        settings!.CanIssue.Should().BeFalse();
        settings.BlockingReason.Should().Be("BillingSettingsMissing");

        var attempt = await owner.PostAsJsonAsync("/api/platform/invoices", new
        {
            tenantId = store.Tenant.Id,
            periodStartUtc = PeriodStart,
            periodEndUtc = PeriodEnd,
            includeSubscription = false,
            billingPeriodId = (int?)null,
            lines = new[] { new { description = "اشتراك", quantity = 1m, unitAmount = 10m, taxCategory = (string?)null } },
        });
        attempt.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await attempt.Content.ReadAsStringAsync()).Should().Contain("BillingSettingsMissing");

        // وتسعيرُ خطةٍ ممنوعٌ كذلك: السعرُ بلا عملةٍ ليس سعراً.
        var priced = await owner.PostAsJsonAsync("/api/platform/plans", new
        {
            code = $"p{Random.Shared.Next(1000, 9999)}",
            name = "خطة اختبارية",
            entitlements = Array.Empty<string>(),
            limits = Array.Empty<object>(),
            priceAmount = 30m,
            billingIntervalMonths = 1,
        });
        priced.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await priced.Content.ReadAsStringAsync()).Should().Contain("BillingCurrencyNotSet");

        // وعملةٌ بلا مُصدِر لا تكفي: المستندُ يحتاج مَن أصدره باسمه.
        (await owner.PutAsJsonAsync("/api/platform/billing/settings", new
        {
            currency = "JOD", issuerName = (string?)null, paymentTermsDays = 30, gracePeriodDays = 7,
            taxCollectionEnabled = false,
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var halfSet = await (await owner.GetAsync("/api/platform/billing/settings"))
            .Content.ReadFromJsonAsync<SettingsView>(Json);
        halfSet!.CanIssue.Should().BeFalse();
        halfSet.BlockingReason.Should().Be("BillingIssuerNotSet");
    }

    // يُعيد الصفّ العالميّ إلى «غير موجود». الفواتيرُ الصادرة لا تُمَسّ: هي مستندات، وحذفُها
    // لإرضاء اختبارٍ هو بالضبط ما يحرّمه هذا الملفّ كلّه.
    private async Task ResetBillingSettingsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = await db.PlatformBillingSettings.ToListAsync();
        if (existing.Count == 0) return;
        db.PlatformBillingSettings.RemoveRange(existing);
        await db.SaveChangesAsync();
    }

    // ========================================================================
    // **الرحلة الكاملة**: إعدادٌ ← مسوّدة ← إصدارٌ برقمٍ من السلسلة ← التاجر يراها ← سدادٌ جزئيّ
    // ← سدادٌ مُتمّم ← «مسدَّدة».
    //
    // وهذه هي C5 كلُّها في اختبارٍ واحد: لا مزوّدَ دفع في أيّ خطوة منها.
    // ========================================================================
    [Fact]
    public async Task الرحلة_الكاملة_من_مسوّدة_إلى_فاتورة_مسدَّدة_بلا_مزوّد()
    {
        var owner = await ConfiguredOwnerAsync();
        var store = await _factory.CreateStoreAsync();
        var merchant = await _api.ForStore(store).AdminAsync();

        var invoiceId = await DraftAsync(owner, store.Tenant.Id, unit: 100m);

        // المسوّدةُ لا يراها التاجر: ليست مطالبةً بعد.
        (await merchant.GetAsync($"/api/admin/store/subscription/invoices/{invoiceId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var number = await IssueAsync(owner, invoiceId);
        number.Should().StartWith("INV");

        // وما إن تصدر حتى تُقرأ من لوحة التاجر، بمُصدِرها وتعليمات دفعها المجمَّدة عليها.
        var mine = await (await merchant.GetAsync($"/api/admin/store/subscription/invoices/{invoiceId}"))
            .Content.ReadFromJsonAsync<InvoiceView>(Json);
        mine!.Number.Should().Be(number);
        mine.Status.Should().Be("Issued");
        mine.Total.Should().Be(100m);
        mine.Outstanding.Should().Be(100m);
        mine.IssuerName.Should().Be("سوق (اختبار)");
        mine.BilledToName.Should().Be(store.Tenant.Name);
        mine.BilledToTaxNumber.Should().Be("MERCHANT-TX");
        mine.PaymentInstructions.Should().Contain("حوالة");

        // **سدادٌ جزئيّ** — واقعةٌ عادية في هذا السوق، ورفضُها كان سيدفع المشغّل إلى تقريبٍ يدويّ.
        (await owner.PostAsJsonAsync($"/api/platform/invoices/{invoiceId}/payments", new
        {
            amount = 40m, method = "BankTransfer", receivedAtUtc = DateTime.UtcNow,
            reference = "TRX-1", note = (string?)null,
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var partly = await (await merchant.GetAsync($"/api/admin/store/subscription/invoices/{invoiceId}"))
            .Content.ReadFromJsonAsync<InvoiceView>(Json);
        partly!.AmountPaid.Should().Be(40m);
        partly.Outstanding.Should().Be(60m);
        partly.Status.Should().Be("Issued");

        // وما يتجاوز المتبقّي مرفوض: رصيدٌ دائنٌ على فاتورة مفهومٌ لم يقرّره أحد.
        (await owner.PostAsJsonAsync($"/api/platform/invoices/{invoiceId}/payments", new
        {
            amount = 100m, method = "Cash", receivedAtUtc = DateTime.UtcNow,
            reference = (string?)null, note = (string?)null,
        })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await owner.PostAsJsonAsync($"/api/platform/invoices/{invoiceId}/payments", new
        {
            amount = 60m, method = "Cash", receivedAtUtc = DateTime.UtcNow,
            reference = "TRX-2", note = "تسليمٌ نقديّ",
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var settled = await (await merchant.GetAsync($"/api/admin/store/subscription/invoices/{invoiceId}"))
            .Content.ReadFromJsonAsync<InvoiceView>(Json);
        settled!.Status.Should().Be("Settled");
        settled.Outstanding.Should().Be(0m);
        settled.Payments.Should().HaveCount(2);

        // وملخّصُ الاشتراك يتبع الفواتير لا عموداً مخزَّناً: لا شيء مفتوحٌ بعد الآن.
        var summary = await (await merchant.GetAsync("/api/admin/store/subscription"))
            .Content.ReadFromJsonAsync<SubscriptionView>(Json);
        summary!.OutstandingTotal.Should().Be(0m);
        summary.OpenInvoiceCount.Should().Be(0);
    }

    // ========================================================================
    // **المستندُ الصادر لا يُحرَّر ولا يُلغى** — وهو الفرقُ بين دفترٍ وجدول. التصحيحُ إشعارُ دائن،
    // وله سلسلتُه الخاصّة كي لا يحمل مستندان مختلفان الرقمَ نفسه.
    // ========================================================================
    [Fact]
    public async Task الفاتورة_الصادرة_تُصحَّح_بإشعار_دائن_لا_بتحرير()
    {
        var owner = await ConfiguredOwnerAsync();
        var store = await _factory.CreateStoreAsync();
        var invoiceId = await DraftAsync(owner, store.Tenant.Id, unit: 100m);
        await IssueAsync(owner, invoiceId);

        // كلُّ بابٍ للتحرير مغلق (422 من المجال).
        (await owner.PostAsJsonAsync($"/api/platform/invoices/{invoiceId}/lines",
                new { description = "سطرٌ بعد الإصدار", quantity = 1m, unitAmount = 5m, taxCategory = (string?)null }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.PutAsJsonAsync($"/api/platform/invoices/{invoiceId}/notes", new { notes = "لاحقة" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.PostAsync($"/api/platform/invoices/{invoiceId}/cancel", null))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // والتصحيحُ مستندٌ ثانٍ بسلسلته.
        var credited = await owner.PostAsJsonAsync($"/api/platform/invoices/{invoiceId}/credit-notes", new
        {
            reason = "خطأٌ في احتساب المدّة",
            lines = new[] { new { description = "تصحيحٌ جزئيّ", quantity = 1m, unitAmount = 30m, taxCategory = (string?)null } },
        });
        credited.StatusCode.Should().Be(HttpStatusCode.OK);
        var creditNumber = (await credited.Content.ReadFromJsonAsync<NumberResponse>(Json))!.Number;
        creditNumber.Should().StartWith("CN");

        var after = await (await owner.GetAsync($"/api/platform/invoices/{invoiceId}"))
            .Content.ReadFromJsonAsync<InvoiceView>(Json);
        after!.Credited.Should().Be(30m);
        after.Outstanding.Should().Be(70m);
        after.Total.Should().Be(100m, "الفاتورةُ نفسها لم تُمَسّ — المستندُ الصادر لا يتغيّر");
        after.CreditNotes.Should().ContainSingle().Which.Number.Should().Be(creditNumber);

        // وما يتجاوز المتبقّي مرفوض.
        (await owner.PostAsJsonAsync($"/api/platform/invoices/{invoiceId}/credit-notes", new
        {
            reason = "تصحيحٌ زائد",
            lines = new[] { new { description = "زائد", quantity = 1m, unitAmount = 200m, taxCategory = (string?)null } },
        })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ========================================================================
    // **سلسلةُ الترقيم متّصلة وفريدة عبر المنصّة كلّها** — لا عبر متجرٍ واحد. المُصدِر واحدٌ هو
    // سوق، والتسلسلُ غيرُ المنقطع لكل مُصدِر مطلبٌ محاسبيّ في اختصاصاتٍ كثيرة.
    // ========================================================================
    [Fact]
    public async Task أرقام_الفواتير_متتالية_وفريدة_عبر_المتاجر()
    {
        var owner = await ConfiguredOwnerAsync();
        var first = await _factory.CreateStoreAsync();
        var second = await _factory.CreateStoreAsync();

        var a = await IssueAsync(owner, await DraftAsync(owner, first.Tenant.Id, 10m));
        var b = await IssueAsync(owner, await DraftAsync(owner, second.Tenant.Id, 20m));
        var c = await IssueAsync(owner, await DraftAsync(owner, first.Tenant.Id, 30m));

        new[] { a, b, c }.Should().OnlyHaveUniqueItems();

        // والتتابعُ لا يتقسّم على المتاجر: رقمُ متجرٍ ثانٍ يقع **بين** رقمَي الأول.
        int Numeric(string number) => int.Parse(number[3..]);
        Numeric(b).Should().Be(Numeric(a) + 1);
        Numeric(c).Should().Be(Numeric(b) + 1);
    }

    // ========================================================================
    // **العزل**: تاجرٌ لا يقرأ فاتورةَ تاجرٍ آخر بتخمين رقم — و**404 لا 403**، فلا يُكشَف وجودُ
    // مستندات غيره. وهذا أهمُّ اختبارٍ في الملفّ: هذه جداولُ الشكل B، بلا مرشّحٍ مستأجرٍ يحميها،
    // فعزلُها كلُّه شرطٌ يكتبه المستدعي بيده — وهنا يُثبَت أنّه كُتب فعلاً.
    // ========================================================================
    [Fact]
    public async Task تاجر_لا_يقرأ_فاتورة_تاجر_آخر()
    {
        var owner = await ConfiguredOwnerAsync();
        var mine = await _factory.CreateStoreAsync();
        var theirs = await _factory.CreateStoreAsync();

        var theirInvoice = await DraftAsync(owner, theirs.Tenant.Id, 50m);
        await IssueAsync(owner, theirInvoice);

        var myInvoice = await DraftAsync(owner, mine.Tenant.Id, 10m);
        await IssueAsync(owner, myInvoice);

        var merchant = await _api.ForStore(mine).AdminAsync();

        (await merchant.GetAsync($"/api/admin/store/subscription/invoices/{theirInvoice}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await merchant.GetAsync($"/api/admin/store/subscription/invoices/{myInvoice}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // وقائمتُه لا تحمل إلا فواتيره.
        var list = await (await merchant.GetAsync("/api/admin/store/subscription/invoices"))
            .Content.ReadFromJsonAsync<InvoicePage>(Json);
        list!.Items.Should().OnlyContain(i => i.Id == myInvoice);

        // ونقاطُ المنصّة لا وجودَ لها على مضيف متجر أصلاً.
        (await merchant.GetAsync("/api/platform/invoices")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ========================================================================
    // **لقطةُ الضريبة تُجمَّد عند الإصدار، ولا يُحرّكها إصدارُ قواعدَ لاحق.**
    //
    // وتُثبَت البوّابةُ نفسها التي تحكم سلّة المتسوّق: إصدارٌ منشورٌ لم يتحقّق منه مهنيّ **لا
    // يُضرِّب فاتورةَ منصّة** — فلا بابَ خلفيّ إلى رقمٍ لم يؤكّده أحد.
    // ========================================================================
    [Fact]
    public async Task الضريبة_لا_تُحتسب_بلا_تحقّق_مهنيّ_وتُجمَّد_لقطتها_عند_الإصدار()
    {
        var owner = await ConfiguredOwnerAsync();
        var store = await _factory.CreateStoreAsync();
        var jurisdiction = $"B{Random.Shared.Next(100, 999)}";

        // ملفٌّ بقيمٍ **صناعيّة**، منشورٌ وغيرُ متحقَّقٍ منه.
        var profileId = (await (await owner.PostAsJsonAsync("/api/platform/tax/profiles",
                new { jurisdiction, name = "Platform jurisdiction (test)" }))
            .Content.ReadFromJsonAsync<IdResponse>(Json))!.Id;

        var versionId = (await (await owner.PostAsJsonAsync($"/api/platform/tax/profiles/{profileId}/versions", new
            {
                effectiveFrom = DateTime.UtcNow.AddYears(-1),
                priceMode = "Exclusive",
                shippingTaxable = false,
                rates = new[] { new { code = "standard", name = "Standard (test)", basisPoints = 1000, category = (string?)null } },
                notes = "قيمٌ اختبارية لا مرجعَ لها",
            })).Content.ReadFromJsonAsync<IdResponse>(Json))!.Id;

        (await owner.PostAsync($"/api/platform/tax/profiles/{profileId}/versions/{versionId}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // المنصّة تختاره وتفعّل الجمع — **ولا يُجمَع**، لأنّ أحداً لم يؤكّده.
        (await owner.PutAsJsonAsync("/api/platform/billing/settings", new
        {
            currency = "JOD", issuerName = "سوق (اختبار)", paymentTermsDays = 30, gracePeriodDays = 7,
            taxProfileId = profileId, taxCollectionEnabled = true,
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var unverifiedInvoice = await DraftAsync(owner, store.Tenant.Id, 100m);
        await IssueAsync(owner, unverifiedInvoice);

        var untaxed = await (await owner.GetAsync($"/api/platform/invoices/{unverifiedInvoice}"))
            .Content.ReadFromJsonAsync<InvoiceView>(Json);
        untaxed!.TaxAmount.Should().Be(0m,
            "إصدارٌ منشورٌ لم يتحقّق منه أحد لا يُضرِّب فاتورةَ منصّة — وهو الحدّ الذي يوجد لأجله ADR-0055");
        untaxed.Total.Should().Be(100m);
        untaxed.TaxSnapshot.Should().BeNull("لا لقطةَ بلا جمع — ولا مبلغَ بلا لقطة");

        // (2) تحقّقٌ مهنيّ باسمه ⇒ تُضرَّب الفاتورةُ التالية، وتُجمَّد لقطتُها بقيمها.
        (await owner.PostAsJsonAsync($"/api/platform/tax/profiles/{profileId}/versions/{versionId}/verify",
                new { verifiedBy = "مكتب محاسبة (اختبار)", note = "تحقّقٌ اختباري" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var taxedId = await DraftAsync(owner, store.Tenant.Id, 100m);
        await IssueAsync(owner, taxedId);

        var taxed = await (await owner.GetAsync($"/api/platform/invoices/{taxedId}"))
            .Content.ReadFromJsonAsync<InvoiceView>(Json);
        taxed!.TaxAmount.Should().Be(10m, "10% مضافةٌ على مئة");
        taxed.Total.Should().Be(110m, "العُرف «مضاف» فالضريبة تُزاد على الإجمالي");
        taxed.TaxSnapshot.Should().NotBeNull();
        taxed.TaxSnapshot!.Verification.Should().Be("Verified");
        taxed.TaxSnapshot.Lines.Should().ContainSingle().Which.BasisPoints.Should().Be(1000);

        // (3) **إصدارٌ لاحق بقواعد مختلفة لا يُحرّك فاتورةً صدرت.**
        var nextVersionId = (await (await owner.PostAsJsonAsync($"/api/platform/tax/profiles/{profileId}/versions", new
            {
                effectiveFrom = DateTime.UtcNow.AddYears(1),
                priceMode = "Exclusive",
                shippingTaxable = false,
                rates = new[] { new { code = "standard", name = "Standard (test)", basisPoints = 2500, category = (string?)null } },
                notes = "إصدارٌ لاحق",
            })).Content.ReadFromJsonAsync<IdResponse>(Json))!.Id;
        (await owner.PostAsync($"/api/platform/tax/profiles/{profileId}/versions/{nextVersionId}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var reread = await (await owner.GetAsync($"/api/platform/invoices/{taxedId}"))
            .Content.ReadFromJsonAsync<InvoiceView>(Json);
        reread!.TaxAmount.Should().Be(10m, "لقطةُ الفاتورة قيمٌ مجمَّدة لا مرجعٌ يتحرّك");
        reread.TaxSnapshot!.Lines.Single().BasisPoints.Should().Be(1000);

        // ونظافةً بعدُ: نُعيد إعدادَ المنصّة إلى بلا ضريبة كي لا تتسرّب إلى رحلةٍ أخرى.
        (await owner.PutAsJsonAsync("/api/platform/billing/settings", new
        {
            currency = "JOD", issuerName = "سوق (اختبار)", paymentTermsDays = 30, gracePeriodDays = 7,
            taxProfileId = (int?)null, taxCollectionEnabled = false,
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ========================================================================
    // **القياس**: حدثٌ لا يُسجَّل مرّتين بمفتاحه، وفترةٌ أُغلقت لا يلتحق بها شيء، وما قيس يُحمَّل
    // على الفاتورة مرّةً واحدة بسعرٍ يُمرّره المشغّل.
    // ========================================================================
    [Fact]
    public async Task القياس_لا_يُكرَّر_ولا_يلتحق_بفترة_أُغلقت_ولا_يُفوتَر_مرّتين()
    {
        var owner = await ConfiguredOwnerAsync();
        var store = await _factory.CreateStoreAsync();
        var tenantId = store.Tenant.Id;
        var occurred = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);
        var key = $"k{Guid.NewGuid():N}";

        var first = await owner.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/billing/events", new
        {
            meter = "storage.gb", quantity = 5m, occurredAtUtc = occurred,
            idempotencyKey = key, description = "تخزينٌ اختباري",
        });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var eventId = (await first.Content.ReadFromJsonAsync<IdResponse>(Json))!.Id;

        // **إعادةُ الإرسال تعيد الحدثَ نفسه** — نجاحٌ لا تعارض: مَن يُعيد المحاولة يريد أن تكون
        // الوحدةُ مسجَّلةً مرّةً، وقد صارت.
        var again = await owner.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/billing/events", new
        {
            meter = "storage.gb", quantity = 5m, occurredAtUtc = occurred,
            idempotencyKey = key, description = "تخزينٌ اختباري",
        });
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<IdResponse>(Json))!.Id.Should().Be(eventId);

        (await owner.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/billing/events", new
        {
            meter = "storage.gb", quantity = 3m, occurredAtUtc = occurred,
            idempotencyKey = $"k{Guid.NewGuid():N}", description = (string?)null,
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var periods = await (await owner.GetAsync($"/api/platform/tenants/{tenantId}/billing/periods"))
            .Content.ReadFromJsonAsync<List<PeriodView>>(Json);
        var period = periods!.Single();
        period.Status.Should().Be("Open");
        period.EventCount.Should().Be(2);
        period.UnbilledEventCount.Should().Be(2);

        // تحميلُ ما قيس **قبل** الإغلاق مرفوض: فترةٌ ما زالت تقبل أحداثاً تُنتج فاتورةً ناقصة.
        var invoiceId = await DraftAsync(owner, tenantId, 100m);
        (await owner.PostAsJsonAsync($"/api/platform/invoices/{invoiceId}/metered-lines", new
        {
            billingPeriodId = period.Id, meter = "storage.gb", unitAmount = 2m, description = "تخزين",
        })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await owner.PostAsync($"/api/platform/tenants/{tenantId}/billing/periods/{period.Id}/close", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // وبعد الإغلاق لا يلتحق حدثٌ متأخّر بها.
        (await owner.PostAsJsonAsync($"/api/platform/tenants/{tenantId}/billing/events", new
        {
            meter = "storage.gb", quantity = 1m, occurredAtUtc = occurred,
            idempotencyKey = $"k{Guid.NewGuid():N}", description = (string?)null,
        })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // والتحميلُ الآن يجمع الوحدتين في سطرٍ واحد بسعرِ الوحدة الذي مرّره المشغّل.
        (await owner.PostAsJsonAsync($"/api/platform/invoices/{invoiceId}/metered-lines", new
        {
            billingPeriodId = period.Id, meter = "storage.gb", unitAmount = 2m, description = "تخزين",
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var withMeter = await (await owner.GetAsync($"/api/platform/invoices/{invoiceId}"))
            .Content.ReadFromJsonAsync<InvoiceView>(Json);
        withMeter!.Subtotal.Should().Be(116m, "مئةٌ للاشتراك، و(5+3) وحدةً بسعر 2");

        // **ولا يُفوتَر ما فُوتِر**: لا وحداتٍ غيرَ مفوترة بقيت.
        (await owner.PostAsJsonAsync($"/api/platform/invoices/{invoiceId}/metered-lines", new
        {
            billingPeriodId = period.Id, meter = "storage.gb", unitAmount = 2m, description = "تخزين",
        })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ========================================================================
    // **سطرُ الاشتراك يأتي من سعر الخطة المجمَّد، ولا يُخمَّن.** وخطةٌ بلا سعرٍ تُرفض باسمها —
    // فلا تصدر فاتورةٌ بمبلغٍ اخترعه أحد.
    // ========================================================================
    [Fact]
    public async Task سطر_الاشتراك_من_سعر_الخطة_وخطة_بلا_سعر_تُرفض()
    {
        var owner = await ConfiguredOwnerAsync();
        var store = await _factory.CreateStoreAsync();

        // المتجر على الخطة التأسيسية، وهي **بلا سعر** عمداً.
        var unpriced = await owner.PostAsJsonAsync("/api/platform/invoices", new
        {
            tenantId = store.Tenant.Id,
            periodStartUtc = PeriodStart, periodEndUtc = PeriodEnd,
            includeSubscription = true, billingPeriodId = (int?)null,
            lines = Array.Empty<object>(),
        });
        unpriced.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await unpriced.Content.ReadAsStringAsync()).Should().Contain("PlanNotPriced");

        // خطةٌ مُسعَّرة ومنشورة، يُسنَد المتجر إليها.
        var code = $"p{Random.Shared.Next(1000, 9999)}";
        var planId = (await (await owner.PostAsJsonAsync("/api/platform/plans", new
        {
            code, name = "خطة اختبارية مُسعَّرة",
            entitlements = Array.Empty<string>(), limits = Array.Empty<object>(),
            priceAmount = 25m, billingIntervalMonths = 1,
        })).Content.ReadFromJsonAsync<IdResponse>(Json))!.Id;

        (await owner.PostAsync($"/api/platform/plans/{planId}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.PutAsJsonAsync($"/api/platform/tenants/{store.Tenant.Id}/plan", new { planId }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var created = await owner.PostAsJsonAsync("/api/platform/invoices", new
        {
            tenantId = store.Tenant.Id,
            periodStartUtc = PeriodStart, periodEndUtc = PeriodEnd,
            includeSubscription = true, billingPeriodId = (int?)null,
            lines = Array.Empty<object>(),
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var invoiceId = (await created.Content.ReadFromJsonAsync<IdResponse>(Json))!.Id;

        var draft = await (await owner.GetAsync($"/api/platform/invoices/{invoiceId}"))
            .Content.ReadFromJsonAsync<InvoiceView>(Json);
        draft!.Total.Should().Be(25m);
        // الوصفُ نصٌّ يبقى مقروءاً بعد تقاعد الخطة: اسمُها وإصدارُها، لا مرجعٌ إليها.
        draft.Lines.Should().ContainSingle().Which.Description.Should().Contain(code);

        // والتاجر يقرأ سعرَ خطته من ملخّصه.
        var merchant = await _api.ForStore(store).AdminAsync();
        var summary = await (await merchant.GetAsync("/api/admin/store/subscription"))
            .Content.ReadFromJsonAsync<SubscriptionView>(Json);
        summary!.PriceAmount.Should().Be(25m);
        summary.PlanCode.Should().Be(code);
    }

    // ========================================================================
    // العملةُ تُقفَل بعد أن تصدر فاتورةٌ واحدة: دفترٌ يقول عملةً وفواتيرُه تقول أخرى وضعٌ لا
    // يُصلحه شيء.
    // ========================================================================
    [Fact]
    public async Task عملة_الفوترة_تُقفَل_بعد_أوّل_فاتورة_صادرة()
    {
        var owner = await ConfiguredOwnerAsync();
        var store = await _factory.CreateStoreAsync();
        await IssueAsync(owner, await DraftAsync(owner, store.Tenant.Id, 10m));

        var changed = await owner.PutAsJsonAsync("/api/platform/billing/settings", new
        {
            currency = "USD", issuerName = "سوق (اختبار)", paymentTermsDays = 30, gracePeriodDays = 7,
            taxCollectionEnabled = false,
        });
        changed.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await changed.Content.ReadAsStringAsync()).Should().Contain("BillingCurrencyLocked");

        // وما عداها يبقى قابلاً للتعديل: القفلُ على العملة وحدها.
        (await owner.PutAsJsonAsync("/api/platform/billing/settings", new
        {
            currency = "JOD", issuerName = "سوق (اسمٌ جديد)", paymentTermsDays = 45, gracePeriodDays = 10,
            taxCollectionEnabled = false,
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ملاحظة: «نقاطُ الفوترة للمالك وحده» **لا تُختبر هنا** عمداً — يغطّيها المسحُ العامّ في
    // `AuthorizationBoundaryTests`، وهو يشتقّ توقّعه من `RolePermissions` لا من قائمةٍ مكتوبة،
    // فيغطّي كلَّ نقطةٍ جديدة لحظةَ توجيهها. ونسخُ التوكيد هنا كان سيُنتج حارساً أضعف يوهم بالقوّة.

    private sealed record IdResponse(int Id);
    private sealed record NumberResponse(string Number);

    private sealed record SettingsView(
        string? Currency, string? IssuerName, bool CanIssue, string? BlockingReason, string TaxReason);

    private sealed record LineView(int Id, string Description, decimal Quantity, decimal UnitAmount, decimal LineTotal);

    private sealed record PaymentView(int Id, decimal Amount, string Method, string? Reference);

    private sealed record SnapshotLineView(string Code, int BasisPoints, decimal Amount);

    private sealed record SnapshotView(
        string Jurisdiction, string PriceMode, string Verification, decimal TotalAmount,
        IReadOnlyList<SnapshotLineView> Lines);

    private sealed record CreditNoteView(int Id, string? Number, string Status, decimal Total);

    private sealed record InvoiceView(
        int Id, string? Number, string Status, string Currency,
        string? IssuerName, string? BilledToName, string? BilledToTaxNumber, string? PaymentInstructions,
        decimal Subtotal, decimal TaxAmount, decimal Total, decimal AmountPaid, decimal Credited,
        decimal Outstanding, bool IsOverdue,
        IReadOnlyList<LineView> Lines, IReadOnlyList<PaymentView> Payments,
        IReadOnlyList<CreditNoteView> CreditNotes, SnapshotView? TaxSnapshot);

    private sealed record InvoicePage(IReadOnlyList<InvoiceView> Items, int TotalCount);

    private sealed record PeriodView(int Id, string Status, int EventCount, int UnbilledEventCount);

    private sealed record SubscriptionView(
        string? PlanCode, string? PlanName, decimal? PriceAmount, string? PriceCurrency,
        decimal OutstandingTotal, int OpenInvoiceCount, int OverdueInvoiceCount);
}
