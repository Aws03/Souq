using System.Reflection;
using AwesomeAssertions;
using MediatR;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;
using Souq.Domain.Entities;

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

    private const string Features = ModuleMap.Features;

    // مجلّدات Features ⇒ وحدات docs/04-MODULES (ADR-0002: الوحدات نطاقات أسماء، لا مشاريع). الخريطة في ModuleMap:
    // مصدر واحد تشاركه قواعد الحدود هنا والجرود المولَّدة في GeneratedDocsTests.
    private static readonly IReadOnlyDictionary<string, string[]> ModuleFolders = ModuleMap.FeatureFolders;

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

    [Fact]
    public void مسار_الطلب_لا_يعرف_مزوّد_البريد()
    {
        // المرحلة 14 (D-14): حالات الاستخدام تضع رسائل في صندوق الصادر؛ مزوّد البريد لمعالجي الصندوق وحدهم (وحدة Notifications)
        // — فلا طلب HTTP ينتظر مزوّداً بطيئاً، ولا يُفقد بريد بفشله.
        var result = Types.InAssembly(Application)
            .That().ResideInNamespaceStartingWith("Souq.Application")
            .And().DoNotResideInNamespaceStartingWith($"{Features}.Notifications")
            .And().DoNotResideInNamespace("Souq.Application.Common.Notifications")
            .ShouldNot().HaveDependencyOn("Souq.Application.Common.Notifications.IEmailSender")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    // العقود المسموحة بين الوحدات وفق رسم الاعتماديات (Modules.md §2): الوحدة ⇒ الوحدات التي تستدعي عقودها.
    private static readonly IReadOnlyDictionary<string, string[]> AllowedContracts = new Dictionary<string, string[]>
    {
        // الحجز والمتاح (6)؛ IPricing والسلة (8/9)؛ استخدامات الكوبون (10)؛ دفعة الطلب واستردادها (11)
        ["Ordering"] = ["Inventory", "Shopping", "Promotions", "Payments", "Shipping"],   // … لقطة طريقة الشحن (12)
        ["Inventory"] = ["Catalog"],    // تنفّذ منفذ Catalog IVariantStockInitializer (عكس الاعتماد، المرحلة 6)
        // IStockAvailability لعرض المتاح في السلة — لا حجز (المرحلة 8)؛ IShippingRateProvider لمرحلة الشحن في التسعير (12).
        // وITaxCalculator (ADR-0055): مرحلةُ الضريبة في الخطّ نفسه، بعد الخصم والشحن — فمَن يحسب
        // الإجمالي هو مَن يسأل. والاتجاه واحد: الضريبة لا تسأل السلّةَ شيئاً.
        ["Shopping"] = ["Inventory", "Shipping", "Tax"],
        // IStorePaymentAccountEditor: منطقة المنصّة تدخل نطاق متجر مستهدف (ITenantScopeRunner) وتربط حسابه — استُخرج
        // عقداً في التدقيق المعماري M1 (TD-04/R-04) بدل إشارة Platform المباشرة لصنف Payments (الصنف D سابقاً).
        // IStoreEntitlements (C1، ADR-0047/0053): تجهيز متجر من المنصّة يُسنِد له الخطة التأسيسية، وإلّا
        // وُلد بلا وحدة اختيارية واحدة. عقدٌ لا إشارةٌ مباشرة: الإشارة كانت ستجعل Platform ⇄ Billing دورةً
        // في اتجاهَي المجال معاً (Billing تقرأ Tenant). ولا عكس: Billing لا تشير إلى عقود Platform.
        ["Platform"] = ["Payments", "Billing"],
        // ============================================================================
        // IAccountProfiles وIAccountLifecycle (TD-03/R-15، بُنيا في M9): الدورة الوحيدة التي يمنعها
        // الرسم الهدف، مكسورةً. الاتجاه المُقرَّر هو Customers → Identity، فالعقدان **كلاهما** مُعلَنان
        // في Identity: الأول تُنفّذه Customers (انعكاس تبعية كـ IVariantStockInitializer أعلاه) والثاني
        // تُنفّذه Identity. فكلّ إشارة تنطلق من Customers ولا شيء يعود — وهذا هو سبب انكسار الدورة.
        //
        // ولاحظ أنّ هذا المدخل وحده لا يكفي حرساً: العبورات القديمة كانت تمرّ عبر Souq.Domain.Interfaces
        // (مستودعات وتجمّعات)، وهي لا تخالف هذه القاعدة أصلاً — لذلك يحرسها الجردُ المولَّد
        // ModuleDomainDependencies.md، الذي يُظهر اليوم صفر عبور بين الوحدتين في الاتجاهين.
        // ============================================================================
        ["Customers"] = ["Identity"],
        // ============================================================================
        // ITenantQuotaGuard (C2، ADR-0049): الحدّ يأتي من الخطة، والخطة تملكها Billing — فالوحدة
        // التي تُنشئ الشيء المعدود هي من يسأل. حافّتان جديدتان، وكلتاهما في اتجاه واحد:
        //   • Catalog — إنشاء منتج، وأرشفته، واستعادته.
        //   • Identity — دعوة موظّف، وتفعيله وإيقافه (مقعد).
        //
        // ولا عكس أبداً: Billing لا تشير إلى عقود أيٍّ منهما، فلا دورة (`العقود_المسموحة_بلا_دورات`).
        // والبديل — فحصٌ داخل كل وحدة يقرأ الخطة بنفسه — كان يعني قاعدة عدّ لكل وحدة، وهو بالضبط
        // ما يمنعه ADR-0049: منفذ واحد، تنفيذ واحد.
        // ============================================================================
        // ============================================================================
        // IEventSink (C9، ADR-0050): مَن يقع عنده الحدث هو مَن يسجّله — فحافّةٌ من كل وحدة يقع
        // فيها سلوكٌ يُقاس إلى عقود Reporting (حيث يعيش مجلّد Analytics).
        //
        // الاتجاه واحد دائماً، **ولا عكسَ ممكن**: المصرف `void` لا يعيد شيئاً، فلا شيء تطلبه
        // Reporting من مُسجِّل. وهذا ما يجعل الحافّة آمنةً بالبناء لا بالانتباه.
        //
        // Catalog أوّلاً: البحث يصكّ معرّف تنفيذه ويسجّله. وما بعدها (السلّة، الطلبات) يأتي مع
        // أسطح الالتقاط في الشريحة التالية، وتُضاف حافّتُه هنا حين تُضاف.
        // ============================================================================
        ["Catalog"] = ["Billing", "Reporting"],
        ["Identity"] = ["Billing"],
        // ============================================================================
        // ITaxCalculator (C5، ADR-0056): فاتورةُ الاشتراك تُضرَّب كما تُضرَّب سلّةُ المتسوّق —
        // بالمنفذ نفسه وببوّابة التحقّق المهنيّ نفسها. الحافّةُ الثانية إلى Tax بعد Shopping،
        // وفي الاتجاه نفسه: **مَن يحسب الإجمالي هو مَن يسأل**، والضريبةُ لا تسأل أحداً.
        //
        // ولا دورة: Tax لا تشير إلى عقود Billing، واختيارُ المنصّة لملفّها يصل **وسيطاً** إلى
        // `QuoteForProfileAsync` بدل أن تقرأه الضريبةُ من إعداد الفوترة — ولو قرأته لانعكس السهم.
        // ============================================================================
        ["Billing"] = ["Tax"],
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

    private static bool InPlatformArea(Type t) => InFolders(t, ModuleMap.PlatformAreaFolders);

    private static bool InFolders(Type t, IReadOnlyList<string> folders) =>
        folders.Any(f => InNamespace(t.Namespace ?? "", $"{Features}.{f}"));

    private static bool InNamespace(string ns, string root) =>
        ns == root || ns.StartsWith(root + ".", StringComparison.Ordinal);

    [Fact]
    public void لا_طلب_من_العميل_يحمل_TenantId_خارج_منطقة_المنصّة()
    {
        // MultiTenancy.md §2: المستأجر يقرّره الخادم (المضيف + مطالبة tid) — TenantId في أمر أو
        // استعلام يربطه الـ API من الجسم/المسار ثغرة عبور مستأجرين. وحدها طلبات **منطقة المنصّة**
        // تستهدف مستأجراً بعينه، وهي خلف صلاحيات المنصّة ومضيفها.
        //
        // C1: القائمة كانت `Features.Platform` حرفاً، وصارت ModuleMap.PlatformAreaFolders — لأن
        // مستوى التحكّم التجاري يحمل TenantId بطبيعته (الاشتراك بمفتاح متجر). والامتياز **مكتسَب لا
        // مُعلَن**: `كل_مجلّد_في_منطقة_المنصّة_يُخدَم_على_مضيفها_وحده` أدناه يثبت أن كل طلب في هذه
        // المجلّدات لا يصل إليه أحد إلا عبر نقطة [PlatformEndpoint].
        var offenders = RequestTypes()
            .Where(t => !InPlatformArea(t))
            .SelectMany(t => Reachable(t)
                .SelectMany(r => r.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                .Where(p => p.Name.Equals("TenantId", StringComparison.OrdinalIgnoreCase))
                .Select(p => $"{t.Name} → {p.DeclaringType!.Name}.{p.Name}"))
            .ToList();

        offenders.Should().BeEmpty();
    }

    // ============================================================================
    // الامتياز مكتسَب لا مُعلَن (C1). طلبات منطقة المنصّة وحدها يجوز أن تحمل TenantId، والسبب
    // المكتوب هو "أنها خلف صلاحيات المنصّة ومضيفها" — لكن **لا شيء كان يتحقّق من ذلك**: إدراج
    // مجلّد في القائمة كان يمنحه الإعفاء بمجرّد كتابته.
    //
    // هنا نثبته: كل نقطة API تُرسل طلباً من مجلّد في منطقة المنصّة يجب أن تكون [PlatformEndpoint].
    // و[AvailableOnAllHosts] **مرفوضة صراحةً** هنا وإن كانت مقبولة في غيرها: هي تُخدَم على مضيف
    // متجر أيضاً، وطلبٌ يحمل TenantId من جسم الطلب على مضيف متجر هو ثغرة عبور المستأجرين بعينها.
    // ============================================================================
    [Fact]
    public void كل_مجلّد_في_منطقة_المنصّة_يُخدَم_على_مضيفها_وحده()
    {
        var offenders = GeneratedDocsTests.Endpoints()
            .Where(e => e.Hosts != "platform")
            .SelectMany(e => e.Requests.Where(InPlatformArea)
                .Select(r => $"{e.Verb} {e.Route} ({e.Hosts}) → {r.Name}"))
            .Distinct()
            .ToList();

        offenders.Should().BeEmpty(
            "طلبات منطقة المنصّة تحمل TenantId من الطلب، فإعفاؤها من قاعدة العزل مشروط بأنها لا تُخدَم "
            + "إلا على مضيف المنصّة خلف [PlatformEndpoint]");
    }

    [Fact]
    public void مجلّدات_منطقة_المنصّة_معرّفة_في_الخريطة()
    {
        // مجلّد في PlatformAreaFolders لا وجود له في FeatureFolders يمنح إعفاءً لا يقع على شيء،
        // ولا يُفشل أيّ اختبار آخر — فالخطأ الإملائي هنا صامت تماماً.
        ModuleMap.AuditedAreaFolders.Should().BeSubsetOf(ModuleFolders.Values.SelectMany(f => f));
    }

    [Fact]
    public void كل_طلب_في_منطقة_المنصّة_مُدقَّق()
    {
        // "وصول المنصّة صريح ومُدقَّق": كل أمر أو استعلام من منطقة المنصّة، وكل قراءة مجمَّعة عبر المتاجر
        // (Reporting)، يكتب سطر تدقيق (IAuditable ⇒ AuditBehavior). طلب منصّة جديد بلا تدقيق يُفشل البناء.
        // C1: القائمة كانت مجلّدين حرفيّين، وصارت ModuleMap.AuditedAreaFolders — فمجلّد جديد في
        // منطقة المنصّة يُغطّى بإضافته إلى الخريطة، لا بتذكُّر اختبارٍ لا شيء يذكّر به (ADR-0047 §3).
        var offenders = RequestTypes()
            .Where(t => InFolders(t, ModuleMap.AuditedAreaFolders))
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

    // ============================================================================
    // دفتر المخزون يُكتَب من داخل الكيان وحده (M7).
    //
    // الثابتة التي تقوم عليها كل مطابقة جرد: Σ حركات السجلّ = الموجود. تُحرَس اليوم بأن `OnHand` له
    // `private set` وبأن مُنشئ `StockMovement` `internal`، فلا يصنع سطراً إلا `InventoryItem.Record` —
    // ومعه تغييرُ الموجود في اللحظة نفسها وفي `SaveChanges` نفسه.
    //
    // لكن Souq.Domain يفتح داخله لـ Souq.Infrastructure (InternalsVisibleTo، لأجل EF والتهيئة)، فـ
    // Infrastructure **تستطيع** بناء سطر مباشرةً: سطرٌ بلا تغيير موجود، أو تغييرٌ بلا سطر. كلاهما يكسر
    // الثابتة بصمت — لا استثناء ولا خطأ، بل جردٌ لا يطابق بعد أسابيع بلا أثر يقول أين انفرط.
    // وAppplication ليست على تلك القائمة، فمعالجاتها عاجزة عن ذلك أصلاً؛ Infrastructure وحدها تحتاج حرساً.
    //
    // لا موضع يفعل ذلك اليوم (فُحص موضعاً بموضع في M7): كل بناء يمرّ بـ Record، وكل مستدعٍ يحفظ ما يعيده.
    // هذا الاختبار يجعل الموضع **التالي** يُكتشف عند البناء لا في جرد. ويفحص Newobj لا Call — بناء كائن
    // ليس نداء طريقة — وEF تُنشئ الكيانات بالانعكاس لا بـ Newobj في IL هذه الحزمة، فلا يمسّها.
    // ============================================================================
    [Fact]
    public void سطر_دفتر_المخزون_لا_يُبنى_إلا_داخل_الكيان()
    {
        using var module = ModuleDefinition.ReadModule(Infrastructure.Location);
        var offenders = module.GetTypes()
            .SelectMany(type => type.Methods.Where(m => m.HasBody).Select(method => (type, method)))
            .Where(x => x.method.Body.Instructions.Any(i =>
                i.OpCode == OpCodes.Newobj
                && i.Operand is MethodReference ctor
                && ctor.DeclaringType.FullName == typeof(StockMovement).FullName))
            .Select(x => x.type.FullName)
            .Distinct()
            .ToList();

        offenders.Should().BeEmpty(
            "StockMovement ينشئه InventoryItem وحده، مع تغيير الموجود في اللحظة نفسها. سطرٌ مبنيّ في "
            + "Infrastructure يفصل الاثنين فتكسر مطابقة Σ السجلّ = الموجود بلا أثر");
    }

    private static bool MentionsQueryable(Type type) =>
        typeof(IQueryable).IsAssignableFrom(type)
        || (type.IsGenericType && type.GetGenericArguments().Any(MentionsQueryable));
}
