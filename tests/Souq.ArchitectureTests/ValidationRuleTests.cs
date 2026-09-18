using System.Reflection;
using AwesomeAssertions;
using FluentValidation;
using MediatR;

namespace Souq.ArchitectureTests;

// ============================================================================
// كل طلبٍ يحمل مدخلاً حرّاً له مُحقِّق (M15).
//
// **المشكلة أنّ خطّ التحقّق يفشل مفتوحاً.** `ValidationBehavior` يبدأ بـ `if (_validators.Any())`، فطلبٌ
// بلا مُحقِّق لا يُرفض ولا يُحذَّر منه — يمضي إلى معالجه مباشرةً. وهذا المستودع يفرض في البناء كل شيء
// آخر تقريباً: تغطية `[AllowAnonymous]`، وأسماء الصلاحيات، ومرشّحات المستأجر، ومواضع تجاوز المرشّح،
// وغياب SQL الخام، ومواضع الكتابة المجمَّعة، و`IAuditable` على طلبات المنصّة. الشيءُ الوحيد غير المفروض
// هو الذي يكون غيابه صامتاً.
//
// **ولا يُفرض مُحقِّقٌ على كل طلب**، لأنّ ذلك ضجيجٌ لا حراسة: من 141 طلباً، 59 بلا مُحقِّق — و45 منها
// لا تحمل إلا معرّفات وأعلاماً وتعدادات. هذه لا يحرسها مُحقِّقٌ بل نظامُ الأنواع (لا شيء غير العدد يصل
// `int`)، ثمّ مرشّح المستأجر وفحص الملكية في المعالج. مُحقِّقٌ يكتب `GreaterThan(0)` عليها يُضيف ملفّاً
// ولا يُضيف أماناً.
//
// **فالقاعدة على الشكل**: ما يحمل نصّاً أو مجرىً أو مجموعةً أو نوعاً مركّباً — أي ما يمكن أن يكون طويلاً
// أو فارغاً أو مشوّهاً — يحتاج مُحقِّقاً أو استثناءً مكتوباً بسببه. والاستثناءات أدناه ليست تسامحاً: كلٌّ
// منها يُحرَس فعلاً في مكان آخر، والمكان مذكور.
// ============================================================================
public class ValidationRuleTests
{
    private static readonly Assembly Application = typeof(Souq.Application.DependencyInjection).Assembly;

    // ما يُحرَس في مكانٍ آخر — والمكان مكتوب، فالاستثناء يُراجَع لا يُورَث.
    private static readonly Dictionary<string, string> ReviewedWithoutValidator = new(StringComparer.Ordinal)
    {
        ["ProcessPaymentWebhookCommand"] =
            "الجسم والتوقيع من الشبكة لا من مستخدم: التحقّق توقيعٌ مُعمّى (StripeGateway/FakeGateway) قبل قراءة أي معنى، "
            + "وحجمه محدود بـ [RequestSizeLimit] على النقطة (M15). مُحقِّقُ شكلٍ قبل ذلك لا يزيد شيئاً",
        ["ApplyPaymentEventCommand"] =
            "أمرٌ داخلي لا يصل من HTTP — يُرسله معالج الويبهوك بعد التحقّق من التوقيع",
        ["GetOrderTrackingQuery"] =
            "الرمز يُفحص في المعالج طولاً وترميزاً قبل أي استعلام، ويُنقَّح من السجلّات بقيمته (SEC-LOG-07)",
        ["LogoutCommand"] = "رمز التجديد من ملفّ تعريف ارتباط HttpOnly لا من جسم الطلب؛ المجهول يُعامَل كخروجٍ ناجح",
        ["RefreshSessionCommand"] = "كسابقه: من ملفّ تعريف الارتباط، والمجهول يُبطل الجلسة",
        ["ClearBasketCommand"] = "رمز سلّة الزائر من ملفّ تعريف ارتباط، ويُفحص طولاً ومحارفَ في BasketModels",
        ["RemoveBasketItemCommand"] = "كسابقه",
        ["RemoveBasketLineCommand"] = "كسابقه",
        ["UploadProductImageCommand"] =
            "المجرى يُفحص بمحتواه لا بشكله: MediaFileInspector يقرأ البايتات الأولى ويرفض ما ليس صورة، والحجم "
            + "محدود في المعالج وبـ [RequestSizeLimit]. لا شيء في السطر يمكن لمُحقِّقٍ أن يقوله",
        ["UploadProductVideoCommand"] = "كسابقه",
        ["UploadStoreBrandingCommand"] = "كسابقه",
        ["UploadTenantBrandingCommand"] = "كسابقه",
    };

    // نوع الطلب الذي يحقّقه هذا الصنف، مهما بَعُد `AbstractValidator<>` في سلسلته.
    private static IEnumerable<Type> ValidatedRequestOf(Type validator)
    {
        for (var t = validator.BaseType; t is not null; t = t.BaseType)
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
                return t.GetGenericArguments();
        return [];
    }

    // الأنواع التي لا يُضيف مُحقِّقٌ إليها شيئاً: نظام الأنواع يحرسها، والملكية تُفحص في المعالج.
    private static bool NeedsValidator(Type type) =>
        !(type.IsPrimitive || type.IsEnum
          || type == typeof(decimal) || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateOnly)
          || (Nullable.GetUnderlyingType(type) is { } inner && !NeedsValidator(inner)));

    [Fact]
    public void كل_طلب_يحمل_مدخلاً_حرّاً_له_مُحقِّق_أو_استثناء_مكتوب()
    {
        // سلسلة الوراثة كاملةً لا الأب المباشر: `PagedQueryValidator<T>` يرث `AbstractValidator<T>`، فاكتفاءٌ
        // بالأب المباشر كان يعتبر كل استعلام مُرقَّم بلا مُحقِّق. (أمسك ذلك نفسُه عند أول تشغيل.)
        var validated = Application.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(ValidatedRequestOf)
            .ToHashSet();

        var offenders = Application.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && t.GetInterfaces().Any(i => i.IsGenericType
                                                      && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
            .Where(t => (t.Namespace ?? "").StartsWith("Souq.Application.Features", StringComparison.Ordinal))
            .Where(t => !validated.Contains(t))
            .Where(t => !ReviewedWithoutValidator.ContainsKey(t.Name))
            .Where(t => t.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Any(p => NeedsValidator(p.ParameterType)))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();

        offenders.Should().BeEmpty(
            "خطّ التحقّق يفشل مفتوحاً: طلبٌ بلا مُحقِّق يمضي إلى معالجه بلا رفضٍ ولا تحذير. فما يحمل نصّاً "
            + "أو مجرىً أو نوعاً مركّباً يحتاج مُحقِّقاً — أو صفّاً في ReviewedWithoutValidator يقول أين يُحرَس بدلاً منه");
    }

    // الاستثناء يبقى استثناءً: اسمٌ حُذف أو صار له مُحقِّق يجب أن يخرج من القائمة، وإلّا تحوّلت إلى تراث.
    [Fact]
    public void قائمة_الاستثناءات_لا_تحمل_اسماً_لم_يعد_موجوداً()
    {
        var requests = Application.GetTypes().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        ReviewedWithoutValidator.Keys.Where(name => !requests.Contains(name))
            .Should().BeEmpty("استثناءٌ لطلبٍ غير موجود يُخفي أنّ القائمة لم تُراجَع");
    }
}
