using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Souq.Application.Common.Models;
using Souq.Application.Features.Analytics.Contracts;
using Souq.Domain.Entities;
using Souq.Domain.Interfaces;

namespace Souq.Application.Features.Analytics;

// ============================================================================
// أسطحُ الالتقاط التي لا أثرَ لها في الخادم (C9b، ADR-0050 §3): الظهورُ في قائمة، والنقرُ عليها،
// ومعاينةُ صنف. ثلاثتُها تقع في المتصفّح ولا تمرّ بأيّ نقطةٍ أخرى، فلها مدخلٌ واحد.
//
// ============================================================================
// **والعميل يرسل معرّفاتٍ ومواضع، لا مالاً ولا مخزوناً.**
//
// هذا هو القرارُ الذي يحكم شكلَ المدخل كلّه. حمولةُ الظهور تحمل سعراً وعملةً وحالةَ توفّر
// (ADR-0050 §3: تُجمَّد وقت الكتابة)، ولو قبِلناها من المتصفّح لصار **أيُّ زائرٍ قادراً على
// تسميم أرقام التاجر** — يكفي طلبٌ واحد ليقول إنّ منتجاً ظهر بسعرٍ لم يُعرض قطّ. فما يصل من
// العميل هو ما يعرفه وحده (أيّ قائمة، وأيّ موضع)، وما يعرفه الخادم يقرؤه الخادم.
//
// وقراءةُ الكتالوج تقع **بعد** التحقّق من التفعيل: متجرٌ لم يُفعّل الالتقاط لا يدفع ثمن استعلام.
// ومنتجٌ لا يجده الخادم يُسقَط من الحمولة بصمت — معرّفٌ من متجرٍ آخر أو منتجٌ حُذف لا يُفشل
// الطلب، ولا يدخل الجدول.
//
// **ولا يعيد هذا المدخل شيئاً أبداً**، ولا يفشل بسبب القياس: عودتُه 202 دائماً ما دام الشكل
// صحيحاً. المتصفّحُ لا ينتظر قياساً، وخطأٌ هنا لا يجوز أن يظهر لمتسوّق.
// ============================================================================

public sealed record ImpressionInput(int ProductId, int Position);

public sealed record RecordStorefrontEventsCommand(
    string? ListId,
    IReadOnlyList<ImpressionInput>? Impressions,
    ImpressionInput? Click,
    ItemViewInput? View) : IRequest<Result>;

public sealed record ItemViewInput(int ProductId, int? VariantId, string? ListId, int? Position);

// الشكلُ فقط: الحدودُ هنا ليست ذوقاً بل حمايةٌ لجدولٍ يكتب فيه مجهول.
public sealed class RecordStorefrontEventsValidator : AbstractValidator<RecordStorefrontEventsCommand>
{
    public RecordStorefrontEventsValidator()
    {
        RuleFor(c => c.ListId).MaximumLength(BehaviouralEvent.IdentifierMaxLength);

        // الحدُّ نفسه الذي تعلنه الحمولة: أطولُ من ذلك يُرفض هنا بدل أن يُبنى ثمّ يُسقَط بصمت
        // عند تجاوز طول الحمولة المخزَّنة.
        RuleFor(c => c.Impressions).Must(i => i is null || i.Count <= ListViewedPayload.MaxItems)
            .WithMessage($"حتى {ListViewedPayload.MaxItems} عنصراً في الظهور الواحد");

        RuleForEach(c => c.Impressions).ChildRules(i =>
        {
            i.RuleFor(x => x.ProductId).GreaterThan(0);
            i.RuleFor(x => x.Position).GreaterThan(0);
        });

        When(c => c.Click is not null, () =>
        {
            RuleFor(c => c.Click!.ProductId).GreaterThan(0);
            RuleFor(c => c.Click!.Position).GreaterThan(0);
            RuleFor(c => c.ListId).NotEmpty().WithMessage("النقر يحتاج هويّة القائمة التي وقع فيها");
        });

        When(c => c.View is not null, () =>
        {
            RuleFor(c => c.View!.ProductId).GreaterThan(0);
            RuleFor(c => c.View!.Position).GreaterThan(0).When(c => c.View!.Position is not null);
            RuleFor(c => c.View!.ListId).MaximumLength(BehaviouralEvent.IdentifierMaxLength);
        });

        // طلبٌ لا يحمل شيئاً ليس خطأً في الشكل، لكنّه لا معنى له — ورفضُه يمنع عادةَ إرسالٍ فارغ.
        RuleFor(c => c).Must(c => c.Impressions is { Count: > 0 } || c.Click is not null || c.View is not null)
            .WithMessage("لا حدث في الطلب");
    }
}

public sealed class RecordStorefrontEventsHandler : IRequestHandler<RecordStorefrontEventsCommand, Result>
{
    private readonly IEventSink _events;
    private readonly IProductRepository _products;
    private readonly ILogger<RecordStorefrontEventsHandler> _logger;

    public RecordStorefrontEventsHandler(
        IEventSink events, IProductRepository products, ILogger<RecordStorefrontEventsHandler> logger)
    {
        _events = events; _products = products; _logger = logger;
    }

    public async Task<Result> Handle(RecordStorefrontEventsCommand cmd, CancellationToken ct)
    {
        // معطَّلاً: لا استعلام، ولا حمولة، ولا صفّ — والجواب هو الجواب نفسه، فلا يعرف المتصفّح
        // (ولا مَن يستكشف الواجهة) إن كان المتجر يلتقط أم لا.
        if (!_events.Enabled) return Result.Success();

        try
        {
            var ids = Ids(cmd);
            var catalogue = (await _products.GetManyAsync(ids, ct)).ToDictionary(p => p.Id);

            if (cmd.Impressions is { Count: > 0 } impressions && cmd.ListId is { Length: > 0 } listId)
                RecordImpressions(listId, impressions, catalogue);

            if (cmd.Click is { } click && cmd.ListId is { Length: > 0 } clickList && catalogue.ContainsKey(click.ProductId))
                _events.Record(BehaviouralEventNames.ListClicked,
                    new ListClickedPayload(clickList, click.ProductId, click.Position));

            if (cmd.View is { } view && catalogue.TryGetValue(view.ProductId, out var viewed))
                RecordView(view, viewed);
        }
        catch (Exception ex)
        {
            // القياسُ لا يُفشل تصفّحاً: يُسجَّل ويُبتلع، والجواب 202 كما لو نجح.
            _logger.LogWarning(ex, "Storefront behavioural events rejected; browsing is unaffected");
        }

        return Result.Success();
    }

    private static int[] Ids(RecordStorefrontEventsCommand cmd) =>
    [
        .. (cmd.Impressions ?? []).Select(i => i.ProductId)
            .Concat(cmd.Click is { } c ? [c.ProductId] : Array.Empty<int>())
            .Concat(cmd.View is { } v ? [v.ProductId] : Array.Empty<int>())
            .Distinct(),
    ];

    // الظهورُ حدثٌ واحد بقائمته لا حدثٌ لكل عنصر: هو ما رآه الزائر في لحظةٍ واحدة، وتفكيكُه
    // يُفقد «أيّ العناصر ظهرت معاً» — وهو بالضبط ما تُحسب منه نسبةُ النقر.
    private void RecordImpressions(string listId, IReadOnlyList<ImpressionInput> impressions, Dictionary<int, Product> catalogue)
    {
        var items = impressions
            .Where(i => catalogue.ContainsKey(i.ProductId))
            .Select(i => Item(catalogue[i.ProductId], i.Position))
            .ToList();

        if (items.Count > 0) _events.Record(BehaviouralEventNames.ListViewed, new ListViewedPayload(listId, items));
    }

    private void RecordView(ItemViewInput view, Product product)
    {
        var price = (view.VariantId is int id ? product.FindVariant(id)?.Price : null) ?? product.Price;
        _events.Record(BehaviouralEventNames.ItemViewed, new ItemViewedPayload(
            product.Id, view.VariantId, price.Amount, price.Currency, product.IsSellable,
            product.CategoryId, view.ListId, view.Position));
    }

    private static ImpressionItem Item(Product product, int position) => new(
        product.Id, position, product.Price.Amount, product.Price.Currency, product.IsSellable, product.CategoryId);
}
