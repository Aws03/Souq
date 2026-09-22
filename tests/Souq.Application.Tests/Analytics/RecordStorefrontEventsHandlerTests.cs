using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Souq.Application.Features.Analytics;
using Souq.Application.Features.Analytics.Contracts;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;
using Souq.Application.Tests.TestDoubles;

namespace Souq.Application.Tests.Analytics;

// ============================================================================
// مدخلُ التقاط ما لا يقع إلّا في المتصفّح (C9b، ADR-0050 §3).
//
// **وأهمُّ ما يُحرس هنا ليس أنّ الحدث يُسجَّل، بل أنّ العميل لا يكتب رقماً مالياً.** حمولةُ
// الظهور تحمل سعراً وحالةَ توفّر؛ ولو قُبلا من المتصفّح لاستطاع أيُّ زائرٍ أن يقول إنّ منتجاً
// ظهر بسعرٍ لم يُعرض قطّ — وأرقامُ التاجر تُبنى على هذا الجدول. فما يرسله العميل معرّفٌ وموضع،
// وما سواهما يقرؤه الخادم من كتالوجه.
//
// ويليه ثلاثةٌ لكلٍّ منها ضرر: أنّ **المعطَّل لا يستعلم ولا يكتب**، وأنّ **معرّفاً مجهولاً
// يُسقَط بصمت** ولا يُفشل تصفّحاً، وأنّ **الظهور حدثٌ واحد بقائمته** لا حدثٌ لكلّ عنصر —
// وتفكيكُه يُفقد «أيّها ظهرت معاً»، وهو ما تُحسب منه نسبةُ النقر.
// ============================================================================
public class RecordStorefrontEventsHandlerTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private RecordingEventSink _events = new() { Enabled = true };

    private RecordStorefrontEventsHandler Handler() =>
        new(_events, _products, NullLogger<RecordStorefrontEventsHandler>.Instance);

    private void Catalogue(params Product[] products) =>
        _products.GetManyAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(products);

    private static Product Listed(int id, decimal price) => TestCatalog.Product(price: price, id: id);

    [Fact]
    public async Task السعر_يُقرأ_من_الكتالوج_لا_من_العميل()
    {
        Catalogue(Listed(5, 10m));

        await Handler().Handle(new RecordStorefrontEventsCommand(
            "search", [new ImpressionInput(5, 1)], null, null), CancellationToken.None);

        var payload = _events.Recorded.Should().ContainSingle().Subject
            .Payload.Should().BeOfType<ListViewedPayload>().Subject;
        payload.ListId.Should().Be("search");
        var item = payload.Items.Should().ContainSingle().Subject;
        (item.ProductId, item.Position).Should().Be((5, 1));
        item.UnitPrice.Should().Be(10m, "السعر من الكتالوج — العميل لا يرسله ولا يستطيع");
    }

    [Fact]
    public async Task الظهور_حدث_واحد_بقائمته_لا_حدث_لكل_عنصر()
    {
        Catalogue(Listed(5, 10m), Listed(6, 20m), Listed(7, 30m));

        await Handler().Handle(new RecordStorefrontEventsCommand(
            "category:3",
            [new ImpressionInput(5, 1), new ImpressionInput(6, 2), new ImpressionInput(7, 3)],
            null, null), CancellationToken.None);

        _events.Recorded.Should().ContainSingle();
        _events.Recorded[0].Payload.Should().BeOfType<ListViewedPayload>()
            .Which.Items.Should().HaveCount(3);
    }

    // معرّفٌ من متجرٍ آخر، أو منتجٌ حُذف بين العرض والإرسال: يُسقَط ولا يُفشل الطلب.
    [Fact]
    public async Task معرّف_لا_يجده_الخادم_يُسقَط_بصمت()
    {
        Catalogue(Listed(5, 10m));

        await Handler().Handle(new RecordStorefrontEventsCommand(
            "search", [new ImpressionInput(5, 1), new ImpressionInput(999, 2)], null, null), CancellationToken.None);

        _events.Recorded[0].Payload.Should().BeOfType<ListViewedPayload>()
            .Which.Items.Should().ContainSingle().Which.ProductId.Should().Be(5);
    }

    [Fact]
    public async Task ظهور_لا_يُطابق_منه_شيء_لا_يُسجّل_حدثاً_فارغاً()
    {
        Catalogue();

        await Handler().Handle(new RecordStorefrontEventsCommand(
            "search", [new ImpressionInput(999, 1)], null, null), CancellationToken.None);

        _events.Recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task النقر_يُسجَّل_بقائمته_وموضعه()
    {
        Catalogue(Listed(5, 10m));

        await Handler().Handle(new RecordStorefrontEventsCommand(
            "offers", null, new ImpressionInput(5, 4), null), CancellationToken.None);

        var payload = _events.Recorded.Should().ContainSingle().Subject
            .Payload.Should().BeOfType<ListClickedPayload>().Subject;
        (payload.ListId, payload.ProductId, payload.Position).Should().Be(("offers", 5, 4));
    }

    [Fact]
    public async Task معاينة_الصنف_تحمل_قائمتها_وموضعها_حين_جاءت_من_قائمة()
    {
        Catalogue(Listed(5, 10m));

        await Handler().Handle(new RecordStorefrontEventsCommand(
            null, null, null, new ItemViewInput(5, null, "search", 2)), CancellationToken.None);

        var payload = _events.Recorded.Should().ContainSingle().Subject
            .Payload.Should().BeOfType<ItemViewedPayload>().Subject;
        (payload.ProductId, payload.ListId, payload.Position).Should().Be((5, "search", 2));
        payload.UnitPrice.Should().Be(10m);
    }

    // **معطَّلاً: لا استعلام ولا حمولة ولا صفّ** — متجرٌ لم يُفعّل الالتقاط لا يدفع ثمن قراءة.
    [Fact]
    public async Task الالتقاط_معطّلاً_لا_يستعلم_ولا_يسجّل()
    {
        _events = new RecordingEventSink { Enabled = false };

        var result = await Handler().Handle(new RecordStorefrontEventsCommand(
            "search", [new ImpressionInput(5, 1)], null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue("الجواب هو نفسه سواءٌ التُقط أم لا");
        _events.Recorded.Should().BeEmpty();
        await _products.DidNotReceiveWithAnyArgs().GetManyAsync(default!, default);
    }

    // القياسُ لا يُفشل تصفّحاً: عطبٌ في القراءة يُبتلع، والجواب نجاحٌ كما لو تمّ.
    [Fact]
    public async Task عطب_في_القراءة_لا_يُفشل_الطلب()
    {
        _products.GetManyAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<Product>>(_ => throw new InvalidOperationException("قاعدة غير متاحة"));

        var result = await Handler().Handle(new RecordStorefrontEventsCommand(
            "search", [new ImpressionInput(5, 1)], null, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _events.Recorded.Should().BeEmpty();
    }
}
