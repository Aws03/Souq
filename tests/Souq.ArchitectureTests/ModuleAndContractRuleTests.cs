using System.Reflection;
using AwesomeAssertions;
using MediatR;
using NetArchTest.Rules;

namespace Souq.ArchitectureTests;

// ============================================================================
// قواعد الوحدات والعقود (Architecture.md §5–§6، Modules.md، MultiTenancy.md §2): كل قاعدة هنا
// صحيحة اليوم، وكل واحدة تحرس مرحلة قادمة من خطأ يسهل ارتكابه ويصعب اكتشافه لاحقاً.
// ============================================================================
public class ModuleAndContractRuleTests
{
    private static readonly Assembly Domain = typeof(Souq.Domain.Common.Entity).Assembly;
    private static readonly Assembly Application = typeof(Souq.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(Souq.Infrastructure.DependencyInjection).Assembly;

    private const string Features = "Souq.Application.Features";

    // مجلّدات Features الحالية ⇒ وحدات Modules.md (ADR-0002: الوحدات نطاقات أسماء، لا مشاريع).
    private static readonly IReadOnlyDictionary<string, string[]> ModuleFolders = new Dictionary<string, string[]>
    {
        ["Catalog"] = ["Products", "Categories"],
        ["Inventory"] = ["Inventory"],
        ["Ordering"] = ["Orders"],
        ["Payments"] = ["Payments"],
        ["Promotions"] = ["Coupons"],
        ["Reviews"] = ["Reviews"],
        ["Identity"] = ["Auth", "Staff"],
        ["Customers"] = ["Customers"],
        ["Shopping"] = ["Baskets"],
        ["Platform"] = ["Platform", "Stores"],
        ["Reporting"] = ["Reporting"],
    };

    [Fact]
    public void كل_مجلّد_ميزات_ينتمي_لوحدة_معرّفة()
    {
        // مجلّد ميزات جديد بلا وحدة في الخريطة يُفشل الاختبار: تُضاف الوحدة لـ Modules.md أولاً.
        var folders = Application.GetTypes()
            .Select(t => t.Namespace)
            .OfType<string>()
            .Where(ns => ns.StartsWith(Features + ".", StringComparison.Ordinal))
            .Select(ns => ns[(Features.Length + 1)..].Split('.')[0])
            .Distinct();

        folders.Should().BeSubsetOf(ModuleFolders.Values.SelectMany(f => f));
    }

    // العقود المسموحة بين الوحدات وفق رسم الاعتماديات (Modules.md §2): الوحدة ⇒ الوحدات التي تستدعي عقودها.
    private static readonly IReadOnlyDictionary<string, string[]> AllowedContracts = new Dictionary<string, string[]>
    {
        // الحجز والمتاح (6)؛ IPricing والسلة (8/9)؛ استخدامات الكوبون (10)؛ دفعة الطلب واستردادها (11)
        ["Ordering"] = ["Inventory", "Shopping", "Promotions", "Payments"],
        ["Inventory"] = ["Catalog"],    // تنفّذ منفذ Catalog IVariantStockInitializer (عكس الاعتماد، المرحلة 6)
        ["Shopping"] = ["Inventory"],   // IStockAvailability لعرض المتاح في السلة — لا حجز (المرحلة 8)
    };

    [Theory]
    [MemberData(nameof(Modules))]
    public void وحدات_Application_لا_تشير_لبعضها_إلا_عبر_العقود_المسموحة(string module)
    {
        // لا إشارة لنطاق وحدة أخرى إلا <Module>.Contracts لوحدة يسمح بها رسم الاعتماديات. العقد ليس ثقباً: أوامر الوحدة
        // الأخرى ومعالجاتها واستعلاماتها وأنواعها الداخلية تبقى ممنوعة.
        var own = ModuleFolders[module].Select(f => $"{Features}.{f}").ToArray();
        var allowed = (AllowedContracts.GetValueOrDefault(module) ?? [])
            .SelectMany(m => ModuleFolders[m]).Select(f => $"{Features}.{f}.Contracts").ToArray();
        var others = ModuleFolders.Where(m => m.Key != module)
            .SelectMany(m => m.Value).Select(f => $"{Features}.{f}").ToArray();

        // أسماء الأنواع كاملةً لا بادئات النطاقات: حظر نطاق وحدة كان سيحظر عقدها المسموح معه.
        var forbidden = Application.GetTypes()
            .Where(t => t.Namespace is { } ns && others.Any(o => InNamespace(ns, o)) && !allowed.Any(a => InNamespace(ns, a)))
            .Select(t => t.FullName!)
            .ToArray();

        var result = Types.InAssembly(Application).That().ResideInNamespaceMatching(
                $"^({string.Join('|', own.Select(System.Text.RegularExpressions.Regex.Escape))})(\\..*)?$")
            .ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"{module} يشير إلى وحدة أخرى خارج عقودها المسموحة: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void العقود_المسموحة_بلا_دورات()
    {
        // Modules.md §2: "A cycle is never allowed" — وحدتان تستدعي كلٌّ منهما عقود الأخرى تُفشل البناء.
        foreach (var (module, targets) in AllowedContracts)
            foreach (var target in targets)
                (AllowedContracts.GetValueOrDefault(target) ?? []).Should().NotContain(module, $"{module} ⇄ {target}");
    }

    public static TheoryData<string> Modules => new(ModuleFolders.Keys);

    private static bool InNamespace(string ns, string root) =>
        ns == root || ns.StartsWith(root + ".", StringComparison.Ordinal);

    [Fact]
    public void لا_طلب_من_العميل_يحمل_TenantId_خارج_منطقة_المنصّة()
    {
        // MultiTenancy.md §2: المستأجر يقرّره الخادم (المضيف + مطالبة tid) — TenantId في أمر أو
        // استعلام يربطه الـ API من الجسم/المسار ثغرة عبور مستأجرين. وحدها أوامر المنصّة (المرحلة 4،
        // Features.Platform) تستهدف مستأجراً بعينه، وهي خلف صلاحيات المنصّة ومضيفها.
        var offenders = RequestTypes()
            .Where(t => !(t.Namespace ?? "").StartsWith($"{Features}.Platform", StringComparison.Ordinal))
            .SelectMany(t => Reachable(t)
                .SelectMany(r => r.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                .Where(p => p.Name.Equals("TenantId", StringComparison.OrdinalIgnoreCase))
                .Select(p => $"{t.Name} → {p.DeclaringType!.Name}.{p.Name}"))
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void كل_طلب_في_منطقة_المنصّة_مُدقَّق()
    {
        // "وصول المنصّة صريح ومُدقَّق": كل أمر أو استعلام من منطقة المنصّة، وكل قراءة مجمَّعة عبر المتاجر
        // (Reporting)، يكتب سطر تدقيق (IAuditable ⇒ AuditBehavior). طلب منصّة جديد بلا تدقيق يُفشل البناء.
        var offenders = RequestTypes()
            .Where(t => (t.Namespace ?? "").StartsWith($"{Features}.Platform", StringComparison.Ordinal)
                        || (t.Namespace ?? "").StartsWith($"{Features}.Reporting", StringComparison.Ordinal))
            .Where(t => !typeof(Souq.Application.Common.Auditing.IAuditable).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void لا_كيان_مجال_في_عقد_طلب_أو_استجابة()
    {
        // الكيانات ليست DTOs (Architecture.md §5): كشفها يسرّب الداخل (تجزئة كلمة المرور، RowVersion)
        // ويجمّد النموذج. نمشي كل نوع يمكن الوصول إليه من كل أمر/استعلام ومن نوع استجابته.
        var offenders = RequestTypes()
            .SelectMany(t => Reachable(t).Concat(ResponseTypes(t).SelectMany(Reachable))
                .Where(r => r.Namespace == "Souq.Domain.Entities")
                .Select(r => $"{t.Name} → {r.Name}"))
            .Distinct()
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void لا_IQueryable_يعبر_حدود_Application_أو_Domain()
    {
        // ADR-0008: الترشيح والترتيب والإسقاط داخل خدمات القراءة؛ IQueryable مكشوفاً يعني أن
        // المستدعي يبني SQL من خارج Infrastructure ويتجاوز حرّاس الترقيم ومرشّح المستأجر لاحقاً.
        var offenders = new[] { Application, Domain }
            .SelectMany(a => a.GetExportedTypes())
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType))
                .Concat(t.GetProperties().Select(p => p.PropertyType))
                .Where(MentionsQueryable)
                .Select(_ => t.FullName!))
            .Distinct()
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void خدمات_القراءة_في_Infrastructure_غير_مكشوفة_إلا_عبر_منافذها()
    {
        var result = Types.InAssembly(Infrastructure)
            .That().ResideInNamespace("Souq.Infrastructure.Persistence.Queries")
            .Should().NotBePublic()
            .GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    private static IEnumerable<Type> RequestTypes() => Application.GetTypes()
        .Where(t => !t.IsAbstract && !t.IsInterface && t.GetInterfaces().Any(IsRequestInterface));

    private static bool IsRequestInterface(Type i) =>
        i == typeof(IRequest) || (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>));

    private static IEnumerable<Type> ResponseTypes(Type request) => request.GetInterfaces()
        .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))
        .Select(i => i.GetGenericArguments()[0]);

    // كل الأنواع المنتسبة لـ Souq التي يمكن بلوغها عبر الخصائص العامة والوسائط العامة للأنواع.
    private static IEnumerable<Type> Reachable(Type root)
    {
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>([root]);
        while (pending.Count > 0)
        {
            var type = pending.Pop();
            if (!seen.Add(type)) continue;
            yield return type;

            if (type.IsArray) pending.Push(type.GetElementType()!);
            if (type.IsGenericType) foreach (var argument in type.GetGenericArguments()) pending.Push(argument);
            if ((type.Namespace ?? "").StartsWith("Souq", StringComparison.Ordinal) && !type.IsEnum)
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    pending.Push(property.PropertyType);
        }
    }

    private static bool MentionsQueryable(Type type) =>
        typeof(IQueryable).IsAssignableFrom(type)
        || (type.IsGenericType && type.GetGenericArguments().Any(MentionsQueryable));
}
