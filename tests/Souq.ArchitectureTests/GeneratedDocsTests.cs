using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Souq.API.Security;
using Souq.API.Tenancy;
using Souq.Application.Common.Auditing;

namespace Souq.ArchitectureTests;

// ============================================================================
// جرود مولَّدة من الكود نفسه، لا تُكتب يدوياً فتتقادم:
//   • docs/05-API/Endpoints.md — كل نقطة API بصلاحيتها ومضيفها ووحدتها الاختيارية وحدّ معدّلها، وحالة الاستخدام التي ترسلها
//     (من تعليمات IL لإجراء الـ Controller)؛ أي رابط "نقطة → حالة استخدام → وحدة".
//   • docs/04-MODULES/UseCases.md — أوامر كل وحدة واستعلاماتها بمعالجها ومدقّقها وتدقيقها ونقاطها، وعقودها العامة ومنفّذوها.
//   • docs/10-TESTING/TestInventory.md — ملفّات الاختبار وعدد اختباراتها لكل مشروع.
// الاختبار يقارن الملف المُلتزَم بما يولّده الكود الآن: الاختلاف تغيير في الكود لم يُحدَّث توثيقه. التحديث أمر واحد:
//   SOUQ_UPDATE_DOCS=1 dotnet test tests/Souq.ArchitectureTests --filter "FullyQualifiedName~GeneratedDocs"
// ============================================================================
public class GeneratedDocsTests
{
    private static readonly Assembly Api = typeof(Program).Assembly;
    private static readonly Assembly Application = typeof(Souq.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(Souq.Infrastructure.DependencyInjection).Assembly;

    internal const string NoAccessDeclared = "**none declared**";
    private const string Regenerate =
        "SOUQ_UPDATE_DOCS=1 dotnet test tests/Souq.ArchitectureTests --filter \"FullyQualifiedName~GeneratedDocs\"";

    [Fact]
    public void جرد_نقاط_الـ_API_مطابق_للكود() => AssertCurrent("docs/05-API/Endpoints.md", EndpointsDocument());

    [Fact]
    public void جرد_حالات_الاستخدام_والعقود_مطابق_للكود() => AssertCurrent("docs/04-MODULES/UseCases.md", UseCasesDocument());

    [Fact]
    public void جرد_الاختبارات_مطابق_للمصدر() => AssertCurrent("docs/10-TESTING/TestInventory.md", TestInventoryDocument());

    // سقّاطة الحدود: كل تسريب حدّ قائم اليوم مكتوب في الملف المُلتزَم. تسريب جديد يغيّر الملف فيُفشل الاختبار، فيراه المراجِع
    // قراراً لا صدفة — والعدد لا يرتفع إلا بموافقة مكتوبة. حذف تسريب يغيّر الملف أيضاً: يُعاد التوليد فينقص العدد.
    [Fact]
    public void جرد_تبعيات_المجال_بين_الوحدات_مطابق_للكود() =>
        AssertCurrent("docs/02-ARCHITECTURE/ModuleDomainDependencies.md", ModuleDomainDependenciesDocument());

    private static void AssertCurrent(string relativePath, string generated)
    {
        var path = RepositoryPaths.Combine(relativePath);
        if (Environment.GetEnvironmentVariable("SOUQ_UPDATE_DOCS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, generated);
            return;
        }

        var committed = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : "";
        committed.Should().Be(generated, $"{relativePath} تقادم عن الكود — أعد توليده: {Regenerate}");
    }

    // ── نقاط الـ API ─────────────────────────────────────────────────────────

    internal sealed record EndpointInfo(
        string Verb, string Route, string Access, string Hosts, string ModuleFlag, string RateLimit,
        IReadOnlyList<Type> Requests, string Controller, string Action);

    internal static IReadOnlyList<EndpointInfo> Endpoints()
    {
        using var module = ModuleDefinition.ReadModule(Api.Location);
        var endpoints = new List<EndpointInfo>();

        foreach (var controller in Api.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t)))
        {
            var classRoute = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? "";
            var token = controller.Name.EndsWith("Controller", StringComparison.Ordinal)
                ? controller.Name[..^"Controller".Length]
                : controller.Name;
            var definition = module.GetType(controller.FullName);

            foreach (var action in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                var verbs = action.GetCustomAttributes<HttpMethodAttribute>(inherit: true).ToList();
                if (verbs.Count == 0) continue;

                var requests = RequestsSentBy(definition, action);
                foreach (var verb in verbs)
                foreach (var method in verb.HttpMethods)
                    endpoints.Add(new EndpointInfo(
                        method,
                        Route(classRoute, verb.Template, token, action.Name),
                        Access(controller, action),
                        Hosts(controller, action),
                        Attribute<RequiresModuleAttribute>(controller, action)?.Module ?? "",
                        string.Join(", ", Attributes<EnableRateLimitingAttribute>(controller, action)
                            .Select(a => a.PolicyName).OfType<string>().Distinct()),
                        requests,
                        controller.Name,
                        action.Name));
            }
        }

        return endpoints
            .OrderBy(e => e.Route, StringComparer.Ordinal)
            .ThenBy(e => Array.IndexOf(VerbOrder, e.Verb))
            .ToList();
    }

    private static readonly string[] VerbOrder = ["GET", "POST", "PUT", "PATCH", "DELETE"];

    private static string Route(string classTemplate, string? actionTemplate, string controllerToken, string actionName)
    {
        var route = actionTemplate is not null && (actionTemplate.StartsWith('/') || actionTemplate.StartsWith("~/", StringComparison.Ordinal))
            ? actionTemplate.TrimStart('~')
            : "/" + string.Join('/', new[] { classTemplate, actionTemplate }.Where(s => !string.IsNullOrEmpty(s)));
        return route.Replace("[controller]", controllerToken.ToLowerInvariant(), StringComparison.Ordinal)
                    .Replace("[action]", actionName.ToLowerInvariant(), StringComparison.Ordinal);
    }

    // [AllowAnonymous] في أي مستوى يتجاوز كل [Authorize] (سلوك ASP.NET Core)؛ الصلاحيات على الصنف والإجراء كلها مطلوبة معاً.
    private static string Access(Type controller, MethodInfo action)
    {
        if (Has<AllowAnonymousAttribute>(controller, action)) return "anonymous";

        var authorize = Attributes<AuthorizeAttribute>(controller, action).ToList();
        var permissions = authorize.OfType<HasPermissionAttribute>().Select(a => $"`{a.Permission}`").Distinct().ToList();
        if (permissions.Count > 0) return string.Join(" + ", permissions);
        return authorize.Count > 0 ? "signed in" : NoAccessDeclared;
    }

    private static string Hosts(Type controller, MethodInfo action)
    {
        var hosts = Has<AvailableOnAllHostsAttribute>(controller, action) ? "store + platform"
            : Has<PlatformEndpointAttribute>(controller, action) ? "platform"
            : "store";
        if (Has<AvailableWhenStoreClosedAttribute>(controller, action)) hosts += ", even when closed";
        else if (Has<AvailableDuringProvisioningAttribute>(controller, action)) hosts += ", during provisioning";
        return hosts;
    }

    private static IEnumerable<T> Attributes<T>(Type controller, MethodInfo action) where T : System.Attribute =>
        controller.GetCustomAttributes<T>(inherit: true).Concat(action.GetCustomAttributes<T>(inherit: true));

    private static T? Attribute<T>(Type controller, MethodInfo action) where T : System.Attribute =>
        action.GetCustomAttribute<T>(inherit: true) ?? controller.GetCustomAttribute<T>(inherit: true);

    private static bool Has<T>(Type controller, MethodInfo action) where T : System.Attribute =>
        Attributes<T>(controller, action).Any();

    // الأوامر والاستعلامات التي يرسلها الإجراء: من تعليمات IL (newobj لنوع طلب، أو استدعاء يعيد نوع طلب مثل `with` على
    // سجلّ)، في جسم الإجراء وآلة حالته غير المتزامنة ودوالّه المحلية ولامداته؛ ومن معاملاته إن رُبط الطلب من الجسم مباشرة.
    private static IReadOnlyList<Type> RequestsSentBy(TypeDefinition controller, MethodInfo action)
    {
        var bodies = controller.Methods.Where(m => m.Name == action.Name).ToList();
        foreach (var method in bodies.ToList())
        {
            var stateMachine = method.CustomAttributes.FirstOrDefault(a =>
                a.AttributeType.Name is "AsyncStateMachineAttribute" or "IteratorStateMachineAttribute");
            if (stateMachine?.ConstructorArguments[0].Value is TypeReference reference && reference.Resolve() is { } resolved)
                bodies.AddRange(resolved.Methods);
        }

        var marker = $"<{action.Name}>";
        bodies.AddRange(controller.Methods.Where(m => m.Name.Contains(marker, StringComparison.Ordinal)));
        bodies.AddRange(controller.NestedTypes
            .Where(n => n.Name.Contains(marker, StringComparison.Ordinal) || n.Name.StartsWith("<>c", StringComparison.Ordinal))
            .SelectMany(n => n.Methods)
            .Where(m => m.DeclaringType.Name.Contains(marker, StringComparison.Ordinal) || m.Name.Contains(marker, StringComparison.Ordinal)));

        var found = new List<Type>();
        foreach (var body in bodies.Where(b => b.HasBody).Distinct())
        foreach (var instruction in body.Body.Instructions)
        {
            var candidate = instruction.OpCode.Code switch
            {
                Code.Newobj when instruction.Operand is MethodReference ctor => ctor.DeclaringType,
                Code.Call or Code.Callvirt when instruction.Operand is MethodReference call => call.ReturnType,
                _ => null,
            };
            if (candidate is not null && RequestType(candidate) is { } type && !found.Contains(type)) found.Add(type);
        }

        foreach (var parameter in action.GetParameters())
            if (IsRequest(parameter.ParameterType) && !found.Contains(parameter.ParameterType)) found.Add(parameter.ParameterType);

        return found;
    }

    private static Type? RequestType(TypeReference reference)
    {
        if (reference.IsGenericInstance || reference.IsGenericParameter || reference.IsArray) return null;
        var type = Application.GetType(reference.FullName.Replace('/', '+'));
        return type is not null && IsRequest(type) ? type : null;
    }

    internal static bool IsRequest(Type type) =>
        type is { IsAbstract: false, IsInterface: false }
        && type.GetInterfaces().Any(i => i == typeof(IRequest) || (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)));

    private static string EndpointsDocument()
    {
        var endpoints = Endpoints();
        var sb = new StringBuilder();
        sb.Append("# API endpoint inventory\n\n");
        sb.Append("> **Generated from the compiled API** by `GeneratedDocsTests` (`tests/Souq.ArchitectureTests/GeneratedDocsTests.cs`). ");
        sb.Append("Do not edit by hand. After changing a controller, regenerate it with:\n");
        sb.Append($"> `{Regenerate}`\n>\n");
        sb.Append("> Conventions (errors, paging, status codes): [ApiDocumentation.md](ApiDocumentation.md). ");
        sb.Append("Use cases per module: [UseCases.md](../04-MODULES/UseCases.md).\n\n");

        sb.Append($"**{endpoints.Count} endpoints** in {endpoints.Select(e => e.Controller).Distinct().Count()} controllers: ");
        sb.Append($"{endpoints.Count(e => e.Access == "anonymous")} anonymous, ");
        sb.Append($"{endpoints.Count(e => e.Access == "signed in")} for any signed-in account, ");
        sb.Append($"{endpoints.Count(e => e.Access.StartsWith('`'))} behind a permission");
        var undeclared = endpoints.Count(e => e.Access == NoAccessDeclared);
        sb.Append(undeclared == 0 ? ".\n\n" : $", **{undeclared} with no declared access**.\n\n");

        sb.Append("## How to read this table\n\n");
        sb.Append("- **Access:** `anonymous` = `[AllowAnonymous]`; *signed in* = `[Authorize]`, any account; a permission such as `orders.manage` = `[HasPermission]`, ");
        sb.Append("granted through roles in `RolePermissions` (401 without a session, 403 without the permission). Several permissions are all required.\n");
        sb.Append("- **Hosts:** *store* = served on a store's host only; *platform* = `[PlatformEndpoint]`, platform host only; ");
        sb.Append("*store + platform* = `[AvailableOnAllHosts]`. Endpoints on the wrong host answer 404. A store that is not active answers 503 `StoreUnavailable`, ");
        sb.Append("except endpoints marked `[AvailableWhenStoreClosed]`, and (during provisioning) `[AvailableDuringProvisioning]` or permission-protected ones. ");
        sb.Append("Enforced by `TenantAvailabilityMiddleware`.\n");
        sb.Append("- **Module:** `[RequiresModule]`; when the store has the module off, the endpoint answers 404 `ModuleDisabled`.\n");
        sb.Append("- **Rate limit:** the `[EnableRateLimiting]` policy (`RateLimitPolicies`).\n");
        sb.Append("- **Use case:** the MediatR command or query the action sends, found in the action's IL; its module follows from its namespace.\n\n");

        sb.Append("| Method | Route | Access | Hosts | Module | Rate limit | Use case | Owning module |\n");
        sb.Append("|---|---|---|---|---|---|---|---|\n");
        foreach (var e in endpoints)
        {
            var useCases = e.Requests.Count == 0 ? "—" : string.Join("<br>", e.Requests.Select(r => $"`{r.Name}`"));
            var modules = string.Join(", ", e.Requests.Select(ModuleMap.ModuleOf).OfType<string>().Distinct());
            sb.Append($"| {e.Verb} | `{e.Route}` | {e.Access} | {e.Hosts} | {Backticked(e.ModuleFlag)} | {Backticked(e.RateLimit)} | {useCases} | {(modules.Length == 0 ? "—" : modules)} |\n");
        }
        return sb.ToString();
    }

    private static string Backticked(string value) => value.Length == 0 ? "—" : $"`{value}`";

    // ── حالات الاستخدام والعقود ──────────────────────────────────────────────

    private static string UseCasesDocument()
    {
        var endpoints = Endpoints();
        var concrete = Application.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }).ToList();

        var handlers = concrete
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)
                                                || i.GetGenericTypeDefinition() == typeof(IRequestHandler<>)))
                .Select(i => (Request: i.GetGenericArguments()[0], Handler: t)))
            .ToLookup(x => x.Request, x => x.Handler);
        var validators = concrete
            .SelectMany(t => BaseTypes(t)
                .Where(b => b.IsGenericType && b.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
                .Select(b => (Request: b.GetGenericArguments()[0], Validator: t)))
            .ToLookup(x => x.Request, x => x.Validator);
        var implementations = Application.GetTypes().Concat(Infrastructure.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .ToList();

        var requests = Application.GetTypes().Where(IsRequest).Where(t => ModuleMap.ModuleOf(t) is not null).ToList();
        var contracts = Application.GetTypes()
            .Where(t => t.IsInterface && t.IsPublic && t.Namespace is { } ns && ModuleMap.ModuleOfNamespace(ns) is not null
                        && ns.Split('.').Contains("Contracts"))
            .ToList();

        var sb = new StringBuilder();
        sb.Append("# Use-case catalog\n\n");
        sb.Append("> **Generated from the compiled code** by `GeneratedDocsTests` (`tests/Souq.ArchitectureTests/GeneratedDocsTests.cs`). ");
        sb.Append("Do not edit by hand; regenerate with:\n");
        sb.Append($"> `{Regenerate}`\n>\n");
        sb.Append("> Every MediatR command and query per module, with its handler, its validator, whether it is audited, and the endpoints that send it; ");
        sb.Append("then each module's public contracts (the interfaces other modules may call) and what implements them. ");
        sb.Append("Module docs explain the *why*: [Modules.md](Modules.md). Endpoints: [Endpoints.md](../05-API/Endpoints.md).\n\n");

        sb.Append("| Module | Feature folders | Commands | Queries | Contracts |\n|---|---|---|---|---|\n");
        foreach (var module in ModuleMap.Modules)
        {
            var own = requests.Where(r => ModuleMap.ModuleOf(r) == module).ToList();
            sb.Append($"| [{module}](#{module.ToLowerInvariant()}) | {string.Join(", ", ModuleMap.FeatureFolders[module].Select(f => $"`src/Souq.Application/Features/{f}`"))} ");
            sb.Append($"| {own.Count(r => Kind(r) == "command")} | {own.Count(r => Kind(r) == "query")} | {contracts.Count(c => ModuleMap.ModuleOf(c) == module)} |\n");
        }
        sb.Append('\n');

        foreach (var module in ModuleMap.Modules)
        {
            sb.Append($"## {module}\n\n");
            sb.Append($"Module document: [{module}/README.md]({module}/README.md).\n\n");

            var own = requests.Where(r => ModuleMap.ModuleOf(r) == module)
                .OrderBy(r => Kind(r) == "command" ? 0 : 1).ThenBy(r => r.Name, StringComparer.Ordinal).ToList();
            if (own.Count == 0)
                sb.Append("No commands or queries.\n\n");
            else
            {
                sb.Append("| Use case | Kind | Handler | Validator | Audited | Sent by |\n|---|---|---|---|---|---|\n");
                foreach (var request in own)
                {
                    var sentBy = endpoints.Where(e => e.Requests.Contains(request)).Select(e => $"`{e.Verb} {e.Route}`").ToList();
                    sb.Append($"| `{request.Name}` | {Kind(request)} | {Names(handlers[request])} | {Names(validators[request])} ");
                    sb.Append($"| {(typeof(IAuditable).IsAssignableFrom(request) ? "yes" : "—")} ");
                    sb.Append($"| {(sentBy.Count == 0 ? "no endpoint (sent internally)" : string.Join("<br>", sentBy))} |\n");
                }
                sb.Append('\n');
            }

            var ownContracts = contracts.Where(c => ModuleMap.ModuleOf(c) == module).OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
            if (ownContracts.Count > 0)
            {
                sb.Append("| Public contract | Implemented by |\n|---|---|\n");
                foreach (var contract in ownContracts)
                    sb.Append($"| `{contract.Name}` | {Names(implementations.Where(t => contract.IsAssignableFrom(t)))} |\n");
                sb.Append('\n');
            }
        }
        return sb.ToString().TrimEnd('\n') + "\n";
    }

    private static string Kind(Type request) =>
        request.Name.EndsWith("Command", StringComparison.Ordinal) ? "command"
        : request.Name.EndsWith("Query", StringComparison.Ordinal) ? "query"
        : "request";

    private static string Names(IEnumerable<Type> types)
    {
        var names = types.Select(t => $"`{t.Name}`").Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        return names.Count == 0 ? "—" : string.Join(", ", names);
    }

    private static IEnumerable<Type> BaseTypes(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType) yield return current;
    }

    // ── تبعيات المجال بين الوحدات ────────────────────────────────────────────

    private sealed record DomainDependency(string From, string To, string DomainType, string UsedBy);

    private static IReadOnlyList<DomainDependency> ModuleDomainDependencies()
    {
        using var module = ModuleDefinition.ReadModule(Application.Location);
        var found = new HashSet<DomainDependency>();

        foreach (var type in module.GetTypes())
        {
            var top = TopLevel(type);
            var from = ModuleMap.ModuleOfNamespace(top.Namespace);
            if (from is null) continue;

            foreach (var referenced in DomainTypesUsedBy(type))
            {
                var owner = ModuleMap.DomainOwnerOf(referenced);
                if (owner is null || owner == from) continue;          // نواة مشتركة، أو ملك الوحدة نفسها
                found.Add(new DomainDependency(from, owner, referenced, top.Name));
            }
        }

        return found
            .OrderBy(d => Array.IndexOf(ModuleMap.Modules.ToArray(), d.From))
            .ThenBy(d => d.To, StringComparer.Ordinal)
            .ThenBy(d => d.DomainType, StringComparer.Ordinal)
            .ThenBy(d => d.UsedBy, StringComparer.Ordinal)
            .ToList();
    }

    // كل نوع من Souq.Domain يظهر في توقيعات النوع أو في تعليمات دوالّه (استدعاءات، حقول، إنشاء، معالجة استثناء).
    private static IEnumerable<string> DomainTypesUsedBy(TypeDefinition type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        void Consider(TypeReference? reference)
        {
            while (reference is not null)
            {
                if (reference is GenericInstanceType generic)
                {
                    foreach (var argument in generic.GenericArguments) Consider(argument);
                    reference = generic.ElementType;
                    continue;
                }
                if ((reference.Namespace ?? "").StartsWith("Souq.Domain", StringComparison.Ordinal))
                    names.Add(reference.Name.Split('`')[0]);
                reference = (reference as TypeSpecification)?.ElementType;
            }
        }

        Consider(type.BaseType);
        foreach (var @interface in type.Interfaces) Consider(@interface.InterfaceType);
        foreach (var field in type.Fields) Consider(field.FieldType);
        foreach (var property in type.Properties) Consider(property.PropertyType);

        foreach (var method in type.Methods)
        {
            Consider(method.ReturnType);
            foreach (var parameter in method.Parameters) Consider(parameter.ParameterType);
            if (!method.HasBody) continue;

            foreach (var handler in method.Body.ExceptionHandlers) Consider(handler.CatchType);
            foreach (var variable in method.Body.Variables) Consider(variable.VariableType);
            foreach (var instruction in method.Body.Instructions)
                switch (instruction.Operand)
                {
                    case MethodReference call: Consider(call.DeclaringType); Consider(call.ReturnType); break;
                    case FieldReference field: Consider(field.DeclaringType); Consider(field.FieldType); break;
                    case TypeReference reference: Consider(reference); break;
                }
        }

        return names;
    }

    private static TypeDefinition TopLevel(TypeDefinition type)
    {
        while (type.DeclaringType is not null) type = type.DeclaringType;
        return type;
    }

    private static string ModuleDomainDependenciesDocument()
    {
        var dependencies = ModuleDomainDependencies();
        var pairs = dependencies.GroupBy(d => (d.From, d.To)).ToList();
        var sb = new StringBuilder();

        sb.Append("# Cross-module domain dependencies\n\n");
        sb.Append("> **Generated from the compiled Application assembly** by `GeneratedDocsTests` (`tests/Souq.ArchitectureTests/GeneratedDocsTests.cs`). ");
        sb.Append("Do not edit by hand; regenerate with:\n");
        sb.Append($"> `{Regenerate}`\n\n");
        sb.Append("## What this file is, and why it fails your build\n\n");
        sb.Append("Modules are supposed to reach each other only through contracts ([ModuleBoundaries.md](ModuleBoundaries.md)). ");
        sb.Append("`ModuleAndContractRuleTests` enforces that **only inside `Souq.Application.Features`**: repository ports live in `Souq.Domain.Interfaces` ");
        sb.Append("and most entities in `Souq.Domain.Entities`, so a handler that loads another module's aggregate directly breaks no test.\n\n");
        sb.Append("This file makes those crossings countable. It lists every place one module's use cases touch a Domain type another module owns ");
        sb.Append("(ownership: the `DomainOwners` map in `tests/Souq.ArchitectureTests/ModuleMap.cs`; shared-kernel types such as `Money` and `Entity` are not crossings). ");
        sb.Append("The committed file is a ratchet:\n\n");
        sb.Append("- **A new crossing changes this file and fails the test.** That is the point: it should be a decision, made in review, not a quiet import. ");
        sb.Append("Prefer adding a contract to the owning module; if the crossing is deliberate, regenerate the file so the diff shows what you added.\n");
        sb.Append("- **Removing a crossing also changes this file.** Regenerate, and the count goes down.\n\n");
        sb.Append($"**Today: {dependencies.Count} crossings across {pairs.Count} module pairs.**\n\n");

        sb.Append("| From | To | Crossings |\n|---|---|---|\n");
        foreach (var pair in pairs.OrderByDescending(p => p.Count()).ThenBy(p => p.Key.From, StringComparer.Ordinal))
            sb.Append($"| {pair.Key.From} | {pair.Key.To} | {pair.Count()} |\n");
        sb.Append('\n');

        sb.Append("## Every crossing\n\n");
        sb.Append("| From | To | Domain type | Used by |\n|---|---|---|---|\n");
        foreach (var dependency in dependencies)
            sb.Append($"| {dependency.From} | {dependency.To} | `{dependency.DomainType}` | `{dependency.UsedBy}` |\n");

        return sb.ToString();
    }

    // ── جرد الاختبارات ───────────────────────────────────────────────────────

    private static readonly Regex XunitTest = new(@"\[(?<kind>Fact|Theory)\b", RegexOptions.Compiled);
    private static readonly Regex TestClass = new(@"\bclass\s+(?<name>\w+)", RegexOptions.Compiled);
    // النمط السابق كان `^\s*(it|test)(\.each\(.*\))?\s*\(` — و**أعطى نتيجتين مختلفتين على نظامين**: صفر تطابق
    // على macOS وواحداً على Linux لنفس البايتات ونفس .NET 10. السبب أنّ `.*` الشَرِه داخل مجموعة اختيارية يجعل
    // المطابقة معتمدة على تحسين "الذرّية التلقائية" في محرّك .NET: إن مُنع التراجع داخل `.*` تفشل المجموعة،
    // ولأنّها اختيارية تُتخطّى، فيفشل السطر كلّه. وهذا ما كان يُفشل CI وحده: الجرد المُلتزَم مولَّد على macOS.
    //
    // فالنمط الآن **لا يبحث عن قوس إغلاق مطابق أصلاً**: `it`/`test` في أوّل السطر، ولاحقة `.each` وحدها —
    // وهي الوحيدة التي تُعلن حالة اختبار. أمّا `test.describe`/`beforeAll`/`skip` فهي تجهيزات لا حالات،
    // وتبقى غير معدودة كما كانت. ولا تراجع في النمط، فلا فرق بين محرّك ومحرّك.
    private static readonly Regex VitestCase = new(@"^[ \t]*(it|test)(\.each)?\s*\(", RegexOptions.Compiled | RegexOptions.Multiline);

    private static string TestInventoryDocument()
    {
        var sb = new StringBuilder();
        sb.Append("# Test inventory\n\n");
        sb.Append("> **Generated from the test sources** by `GeneratedDocsTests` (`tests/Souq.ArchitectureTests/GeneratedDocsTests.cs`). ");
        sb.Append("Do not edit by hand; regenerate with:\n");
        sb.Append($"> `{Regenerate}`\n>\n");
        sb.Append("> Counts are test *methods* (`[Fact]`, `[Theory]`, Vitest `it`/`test`), not executed cases: a theory runs once per data row, ");
        sb.Append("so `dotnet test` reports more. What each suite is for: [TestingStrategy.md](TestingStrategy.md).\n\n");

        string[] projects = ["Souq.Domain.Tests", "Souq.Application.Tests", "Souq.ArchitectureTests", "Souq.IntegrationTests"];
        var sections = new StringBuilder();
        sb.Append("| Suite | Files | Facts | Theories |\n|---|---|---|---|\n");

        foreach (var project in projects)
        {
            var rows = RepositoryPaths.Walk($"tests/{project}")
                .Where(f => f.EndsWith(".cs", StringComparison.Ordinal))
                .Select(f => (Path: RepositoryPaths.Relative(f), Text: File.ReadAllText(f)))
                .Select(f => (f.Path,
                    Classes: TestClass.Matches(f.Text).Select(m => m.Groups["name"].Value).Distinct().ToList(),
                    Facts: XunitTest.Matches(f.Text).Count(m => m.Groups["kind"].Value == "Fact"),
                    Theories: XunitTest.Matches(f.Text).Count(m => m.Groups["kind"].Value == "Theory")))
                .Where(r => r.Facts + r.Theories > 0)
                .OrderBy(r => r.Path, StringComparer.Ordinal)
                .ToList();

            sb.Append($"| [`{project}`](#{project.ToLowerInvariant().Replace(".", "")}) | {rows.Count} | {rows.Sum(r => r.Facts)} | {rows.Sum(r => r.Theories)} |\n");

            sections.Append($"## {project}\n\n| File | Classes | Facts | Theories |\n|---|---|---|---|\n");
            foreach (var row in rows)
                sections.Append($"| `{row.Path}` | {string.Join(", ", row.Classes.Select(c => $"`{c}`"))} | {row.Facts} | {row.Theories} |\n");
            sections.Append('\n');
        }

        var frontend = RepositoryPaths.Walk("frontend/src")
            .Where(f => f.EndsWith(".test.js", StringComparison.Ordinal) || f.EndsWith(".test.jsx", StringComparison.Ordinal))
            .Select(f => (Path: RepositoryPaths.Relative(f), Cases: VitestCase.Matches(File.ReadAllText(f)).Count))
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ToList();
        sb.Append($"| [frontend (Vitest)](#frontend-vitest) | {frontend.Count} | {frontend.Sum(f => f.Cases)} | — |\n");

        // رحلات المتصفّح تُعَدّ هنا أيضاً (M7): الرقم كان مكتوباً بيد في FrontendGuide.md، وأضافت خمس مراحل
        // متتالية ملفّات دون تحديثه (قال "72 رحلة في ثمانية ملفّات" وكان الواقع 104 في أربعة عشر). رقمٌ يُقال
        // في نصّ ولا يُشتقّ من المصدر يكذب بهدوء — فصار مشتقّاً، والدليل يشير إلى هنا بدل أن ينسخه.
        var journeys = RepositoryPaths.Walk("frontend/e2e")
            .Where(f => f.EndsWith(".spec.js", StringComparison.Ordinal))
            .Select(f => (Path: RepositoryPaths.Relative(f), Cases: VitestCase.Matches(File.ReadAllText(f)).Count))
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ToList();
        sb.Append($"| [frontend (Playwright)](#frontend-playwright) | {journeys.Count} | {journeys.Sum(f => f.Cases)} | — |\n\n");

        sections.Append("## Frontend (Vitest)\n\n| File | Tests |\n|---|---|\n");
        foreach (var file in frontend) sections.Append($"| `{file.Path}` | {file.Cases} |\n");

        sections.Append("\n## Frontend (Playwright)\n\n");
        sections.Append("> Browser journeys, run by hand against a live stack — not in CI. ");
        sections.Append("How to run them: [FrontendGuide.md](../08-FRONTEND/FrontendGuide.md).\n\n| File | Journeys |\n|---|---|\n");
        foreach (var file in journeys) sections.Append($"| `{file.Path}` | {file.Cases} |\n");

        return sb.Append(sections).ToString();
    }
}
