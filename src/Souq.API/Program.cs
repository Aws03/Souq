using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Souq.API.Http;
using Souq.API.Middleware;
using Souq.API.Observability;
using Souq.API.Security;
using Souq.API.Tenancy;
using Souq.Application;
using Souq.Application.Common.Auditing;
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Security;
using Souq.Infrastructure;
using Souq.Infrastructure.Persistence;
using Souq.Infrastructure.Services;

// ── مسبار الحاوية (Health.cs): `dotnet Souq.API.dll --health-check` يسأل هذه النسخة عن جاهزيتها
// ويخرج بـ 0/1. قبل بناء أي خدمة: لا إعداد ولا قاعدة ولا أسرار — لأن صورة الإنتاج بلا curl. ──
if (args.Contains(HealthProbe.Argument)) return await HealthProbe.RunAsync();

var builder = WebApplication.CreateBuilder(args);

// ── تجميع الطبقات (كل طبقة تسجّل نفسها) ──────────────────────────────────
builder.Services.AddApplication();                                                // حالات الاستخدام
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);    // التقنيات

// enums تُقرأ/تُكتب كنصوص ("Shipped" بدل 2) — أوضح لمستهلكي الـ API. رسائل أخطاء قارئ
// JSON الخام تكشف أسماء أنواعنا الداخلية ("could not be converted to Souq.…") ⇒ نعطّلها.
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());   // لحظات الردود UTC بلاحقة Z
        o.AllowInputFormatterExceptionMessages = false;
    });

// ── عقد الأخطاء الموحّد (ADR-0017): كل خطأ RFC 7807 ProblemDetails بـ code + traceId ──
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ProblemDetailsConventions.Customize);
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// ── تحديد المستأجر (ADR-0006): من المضيف فقط. وسائل التطوير (localhost، {slug}.localhost،
// ترويسة X-Tenant، ومضيف منصّة admin.localhost) تُحسب من البيئة وتطغى على أي إعداد — لا تعمل
// خارج Development/Testing مهما كُتب في appsettings. ──
var allowDevelopmentTenancy = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
builder.Services.AddOptions<TenancyOptions>()
    .Bind(builder.Configuration.GetSection(TenancyOptions.SectionName))
    .PostConfigure(o =>
    {
        o.AllowDevelopmentResolution = allowDevelopmentTenancy;
        if (allowDevelopmentTenancy && !o.PlatformHosts.Contains(TenancyOptions.DevelopmentPlatformHost))
            o.PlatformHosts = [.. o.PlatformHosts, TenancyOptions.DevelopmentPlatformHost];
        // localhost ⇒ متجر البذر ما لم يُضبط غيره: معرّفه من DbSeeder (بياناته) لا من إعداد مرفوع (المرحلة 15، A4).
        if (allowDevelopmentTenancy && string.IsNullOrWhiteSpace(o.LocalDefaultTenant))
            o.LocalDefaultTenant = DbSeeder.DefaultTenantSlug;
    });

// ── المصادقة: التحقّق من توكن JWT الوارد، من إعدادات JwtSettings نفسها التي يُصدِر بها
// JwtTokenGenerator (مُتحقَّق منها عند الإقلاع: مفتاح ≥ 256 بت، مُصدِر وجمهور). المفتاح سرّ من
// user-secrets/البيئة. MapInboundClaims=false كي تصل المطالبات بالأسماء التي كتبها المُصدِر.
// توكن متجر آخر (مطالبة tid لا تطابق المضيف) ⇒ فشل المصادقة (TenantTokenBinding). ──
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtSettings>>((options, jwtSettings) =>
    {
        var jwt = jwtSettings.Value;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.NameIdentifier,
            ClockSkew = TimeSpan.FromSeconds(30),   // هامش ضيّق بدل 5 دقائق افتراضية
        };
        options.Events = new JwtBearerEvents { OnTokenValidated = AccessTokenValidation.ValidateAsync };
    });
// ── التفويض بالصلاحيات (ADR-0019): [HasPermission] ⇒ سياسة تُبنى من اسمها، والقرار من
// RolePermissions. ICurrentUser: منفذ Application يُقرأ من مطالبات التوكن هنا فقط. ──
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// ── الجلسات (ADR-0010): روابط البريد على مضيف الطلب، ملف تعريف ارتباط رمز التجديد، وحدّ المعدّل
// على كل ما يقبل كلمة مرور أو يرسل بريداً أو يعاين كوبوناً. ──
builder.Services.AddScoped<IStorefrontLinks, RequestStorefrontLinks>();
builder.Services.AddSingleton<Souq.Application.Common.Tenancy.IPlatformHosts, ConfiguredPlatformHosts>();   // نطاق متجر لا يكون مضيف المنصّة
builder.Services.AddScoped<IClientInfo, RequestClientInfo>();   // عنوان العميل لسطر التدقيق (D-17)
builder.Services.Configure<RefreshCookieOptions>(builder.Configuration.GetSection(RefreshCookieOptions.SectionName));
builder.Services.AddSouqRateLimiting(builder.Configuration);

// ── الفحوص الصحّية (Health.cs): الجاهزية وحدها تلمس القاعدة؛ الحيوية بلا أي تبعية. ──
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: [HealthEndpoints.ReadyTag]);

// ── عنوان العميل ومخطّط الطلب الحقيقيان خلف Nginx (لحدّ المعدّل والسجلات وروابط البريد) — من
// الشبكات الموثوقة في ForwardedHeaders:KnownNetworks فقط؛ ترويسة X-Forwarded-For من عميل مباشر
// لا تُصدَّق (وإلا تجاوز أي مهاجم حدّ المعدّل بتزويرها). ──
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var network in ReadList(builder.Configuration, "ForwardedHeaders:KnownNetworks"))
        o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));

    // ── عدد الوكلاء بين الزائر والـ API (M15) ──
    // افتراضي الإطار **واحد**، وهو صحيح لطوبولوجيا هذا المستودع: المتصفّح ⇒ nginx ⇒ API.
    // لكن Deployment.md نفسه يوصي بإنهاء TLS عند الحافّة، أي أمام nginx — وتلك قفزتان. وnginx
    // **يُلحق** ولا يستبدل (`$proxy_add_x_forwarded_for`)، فتصل الترويسة "الزائر، المُنهي"؛
    // والحدّ واحد فيُستهلك الأيمن وحده، فيصير عنوان كل زائر هو عنوان المُنهي: حدّ معدّل واحد
    // للجميع، وسطر تدقيق ينسب كل شيء إلى الحافّة. صامتٌ تماماً — ولهذا يكشفه ProxyTrustDiagnostics.
    // يبقى الافتراضي كما كان: تغييره هنا يُغيّر وضع أمان كل نشر قائم بلا أن يطلبه أحد.
    o.ForwardLimit = builder.Configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 1;
});

// ── HSTS (R-16): المدّة قابلة للضبط، وبلا includeSubDomains ولا preload افتراضاً.
// السبب ليس الكسل: المتاجر تأتي بنطاقاتها الخاصة، وHSTS التزام يبقى في متصفّحات الزوّار
// بعد انتهاء العلاقة بالنطاق — و preload شبه دائم. توسيعه قرار نشر يُتَّخذ بعلم، لا افتراض.
builder.Services.AddHsts(o =>
    o.MaxAge = TimeSpan.FromDays(builder.Configuration.GetValue<int?>("Security:HstsMaxAgeDays") ?? 30));

builder.Services.AddEndpointsApiExplorer();

// ── Swagger مع دعم زرّ "Authorize" لاختبار النقاط المحمية بالتوكن ─────────
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "أدخل التوكن فقط (بدون كلمة Bearer)."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ── CORS. الواجهة تطلب مسارات نسبية (/api) ويمرّرها وكيل Vite في التطوير وnginx في النشر،
// فكل بيئة أحادية الأصل ولا تحتاج CORS أصلاً. الاحتياطي على http://localhost:5173 كان يُطبَّق
// في *كل* بيئة: أصل تطوير مسموح به بصمت في الإنتاج، وهو نوع التسرّب الذي يمنعه بقية هذا الملف.
// خارج التطوير/الاختبار: لا أصل افتراضي. من يحتاج أصلاً خارجياً حقاً يعلنه في Cors:AllowedOrigins.
var localEnvironment = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
var allowedOrigins = CorsOrigins.For(
    builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>(), localEnvironment);
builder.Services.AddCors(o => o.AddPolicy("frontend", p =>
    p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()
     .WithExposedHeaders(RequestCorrelation.HeaderName)));   // تقرؤه الواجهة لعرضه عند الدعم

var app = builder.Build();

// ── فشل آمن مبكّر (ADR-0020): كل إعداد مُسجَّل بـ ValidateOnStart (JWT، Stripe، التخزين) يُفحص
// الآن — قبل لمس قاعدة البيانات أو قبول أي طلب. سرّ ناقص أو ضعيف = إقلاع مرفوض برسالة تسمّي
// المفتاح (بلا قيمته). ثم تحذيرات الإعداد غير المناسب للإنتاج مرة واحدة في السجل. ──
app.Services.GetRequiredService<IStartupValidator>().Validate();
var startupLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Souq.Startup");
var startupReport = app.Services.GetRequiredService<InfrastructureStartupReport>();
startupLog.LogInformation("Adapters selected: payments {PaymentProvider}, email {EmailProvider}",
    startupReport.PaymentProvider, startupReport.EmailProvider);
foreach (var warning in startupReport.Warnings)
    startupLog.LogWarning("Configuration warning: {ConfigurationWarning}", warning);

// تحذيرات طبقة الـ API نفسها (نظيرة تحذيرات AddInfrastructure، لكنها تخصّ خط الطلب لا المحوّلات).
startupLog.LogInformation("CORS origins allowed: {CorsOriginCount} (same-origin deployments need none)",
    allowedOrigins.Length);
if (!localEnvironment && !app.Services.GetRequiredService<IOptions<RefreshCookieOptions>>().Value.Secure)
    startupLog.LogWarning("Configuration warning: {ConfigurationWarning}",
        "Auth:RefreshCookie:Secure=false خارج التطوير — رمز التجديد سيُرسَل على http. " +
        "لا تستخدمه إلا لنشر محلي على شبكة موثوقة.");

// ── الهجرات + البذر عند الإقلاع. المدير الافتراضي في Development فقط؛ خارجها يُنشأ أول مدير
// من Seed:AdminEmail/Seed:AdminPassword إن ضُبطا (Phase 0 B1). Seed:DefaultTenantHosts يربط
// مضيفين بالمتجر الافتراضي صراحةً (حزمة Docker التجريبية: localhost). ──
// ── الهجرات عند الإقلاع: قرارٌ صريح في الإنتاج (M17، R-18) ───────────────────
// تشغيلها تلقائياً يعني أنّ النشر هو الترحيل: هجرةٌ سيّئة تُطبَّق بمجرّد الإطلاق، ونسختان تقلعان معاً
// تتسابقان على المخطّط. مقبولٌ لنسخةٍ واحدة تُوقَف ثمّ تُشغَّل — وغيرُ مقبولٍ في اللحظة التي تُضاف فيها
// نسخةٌ ثانية، وهي لحظةٌ لا شيء فيها يذكّر أحداً بهذا السطر. فالاختيار يُطلب مقدّماً بدل أن يُورَث.
// نفس قاعدة Email:Provider، ولنفس السبب: ما يُغيّر سلوك نشرٍ حقيقي يُختار بعلم.
var migrateOnStartup = app.Configuration.GetValue<bool?>("Database:MigrateOnStartup");
if (migrateOnStartup is null)
{
    if (!PaymentProviderSelector.IsLocal(app.Environment.EnvironmentName))
        throw new InvalidOperationException(
            "Database:MigrateOnStartup غير مضبوط. اختر صراحةً: true (النشر يُرحّل — نسخة واحدة تُوقَف ثمّ تُشغَّل) "
            + "أو false (خطوة ترحيل متعمّدة قبل الإطلاق — انظر Deployment.md §الترحيل). "
            + "التلقائي يُعيد خطر R-18 بصمت عند أول نسخة ثانية.");
    migrateOnStartup = true;   // التطوير والاختبار: التلقائي هو الصواب، وقاعدةٌ تُرمى وتُبنى
}

await DbSeeder.SeedAsync(app.Services,
    new SeedOptions(
        app.Configuration["Seed:AdminEmail"], app.Configuration["Seed:AdminPassword"], app.Environment.IsDevelopment(),
        ReadList(app.Configuration, "Seed:DefaultTenantHosts"),
        app.Configuration["Seed:PlatformOwnerEmail"], app.Configuration["Seed:PlatformOwnerPassword"],
        DbSeeder.ShouldSeedDemoData(app.Environment.EnvironmentName, app.Configuration.GetValue<bool?>("Seed:DemoData")),
        migrateOnStartup.Value),
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Souq.Seeding"));

// ── تعبئة الصورة المطبَّعة للبحث لصفوف سابقة للحقل (M3، ADR-0042) ──────────
// بعد الهجرات والبذر: صفوف الكتالوج التي كُتبت قبل وجود عمود الصورة المطبَّعة تُطبَّع بالتنفيذ نفسه الذي يطبّع
// نصّ الاستعلام. بلا هذا يبقى كتالوج قائم غير قابل للبحث بعد الترقية — عطل صامت. الفحص بحث فهرس يعود بصفر
// صفّاً بعد أول تعبئة، فتكلفته على الإقلاع المعتاد لا تُذكر. SearchIndexBackfill يشرح التفصيل.
await SearchIndexBackfill.RunAsync(app.Services,
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Souq.Search"));

// ── R-12: هوية التشغيل كما *تراها القاعدة*، لا كما يصفها الإعداد ──────────
// الفرق بين "قِسنا أن الأقلّ يكفي" و"نعمل بالأقلّ فعلاً" غير مرئي بغير هذا السطر: نشرٌ يصل
// بـ sa يبدو سليماً تماماً في السجل. فحص واحد عند الإقلاع يجعل الفجوة مسموعة كل مرّة.
await using (var privilegeScope = app.Services.CreateAsyncScope())
{
    var privileges = await DatabasePrivileges.InspectAsync(
        privilegeScope.ServiceProvider.GetRequiredService<AppDbContext>());
    var migrationConnection = privilegeScope.ServiceProvider.GetRequiredService<MigrationConnection>();

    if (privileges is null)
        startupLog.LogInformation("Database privileges could not be read; the least-privilege check was skipped");
    else if (privileges.CanChangeSchema && !localEnvironment)
        startupLog.LogWarning("Configuration warning: {ConfigurationWarning}",
            $"التطبيق متصل بالهوية '{privileges.Login}' وهي تملك {privileges.Roles} — أي أنها تستطيع تغيير المخطّط " +
            "وحذف الجداول. التشغيل العادي لا يحتاج أكثر من db_datareader و db_datawriter. " +
            "طبّق scripts/sql/least-privilege-logins.sql — docs/07-SECURITY/DatabasePrivileges.md");
    else
        startupLog.LogInformation("Runtime database identity {Login} cannot change the schema", privileges.Login);

    var runtimeConnection = builder.Configuration.GetConnectionString("Default") ?? "";
    if (migrationConnection.IsSeparateIdentity
        && DatabasePrivileges.SameLogin(runtimeConnection, migrationConnection.Value))
        startupLog.LogWarning("Configuration warning: {ConfigurationWarning}",
            "ConnectionStrings:Migrations مضبوطة لكنها تحمل هوية التشغيل نفسها — الفصل اسمي لا فعلي.");

    // ── TD-68: مستوى عزل القاعدة كما *تقوله هي*، لا كما يُفترض ──────────────
    // حارس "آخر مدير" يفشل **مفتوحاً وبصمت** تحت READ_COMMITTED_SNAPSHOT، وهي مُفعَّلة افتراضياً
    // على قواعد مُدارة. لا اختبار يكشف ذلك — لا يُقاس إلّا على القاعدة العاملة. DatabaseIsolation.
    var snapshotOn = await DatabaseIsolation.IsReadCommittedSnapshotOnAsync(
        privilegeScope.ServiceProvider.GetRequiredService<AppDbContext>());
    if (snapshotOn is null)
        startupLog.LogInformation("The database isolation level could not be read; the RCSI check was skipped");
    else if (snapshotOn.Value)
        startupLog.LogWarning("Configuration warning: {ConfigurationWarning}", DatabaseIsolation.Warning);
    else
        startupLog.LogInformation("READ_COMMITTED_SNAPSHOT is off — the last-administrator guard holds");
}

// ── خط أنابيب الطلب (Request Pipeline) — الترتيب مهم ──────────────────────
app.UseForwardedHeaders();   // أولاً: عنوان العميل ومخطّطه من الوكيل الموثوق قبل أي قرار
app.UseMiddleware<ProxyTrustDiagnostics>();  // ترويسة وكيل وصلت ولم تُصدَّق ⇒ تحذير واحد يسمّي السبب
app.UseMiddleware<CorrelationHeaderMiddleware>(); // X-Correlation-Id على كل استجابة، حتى الأخطاء (ADR-0018)
app.UseMiddleware<SecurityHeadersMiddleware>();   // ترويسات الأمان على كل استجابة، بما فيها الأخطاء
// HSTS خارج التطوير فقط: الترويسة لا تُرسَل إلا على https (المخطّط من الوكيل الموثوق عبر
// UseForwardedHeaders أعلاه)، وإرسالها في التطوير يثبّت localhost على https في متصفّح المطوّر.
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseExceptionHandler();   // الاستثناءات ⇒ ProblemDetails (GlobalExceptionHandler)
app.UseStatusCodePages();    // 401/403/404/405 بجسم فارغ من الإطار ⇒ ProblemDetails بنفس العقد
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// الفحوص الصحّية قبل تحديد المستأجر وحدّ المعدّل والمصادقة عمداً (Health.cs): مسبار المنظّم
// يصل بمضيف الحاوية لا بمضيف متجر، والحيوية يجب أن تجيب حتى وقد تعطّل دليل المتاجر أو القاعدة.
app.UseHealthChecks(HealthEndpoints.Live, HealthEndpoints.Liveness());
app.UseHealthChecks(HealthEndpoints.Ready, HealthEndpoints.Readiness());

// المستأجر أولاً (قبل الملفات والمصادقة): المضيف ⇒ المتجر؛ مضيف مجهول ⇒ 404 قبل أي منطق.
app.UseMiddleware<TenantResolutionMiddleware>();

// مجلد الرفع يُخدَم بأنواع وسائط مسموحة فقط + nosniff + CSP معزول (ADR-0016): حتى
// لو وصل ملف غير متوقّع إلى المجلّد بطريقة ما، لا يُخدَم كصفحة تُنفَّذ على أصل الموقع.
var uploadsPath = app.Services.GetRequiredService<IOptions<FileStorageOptions>>().Value.RootPath;
Directory.CreateDirectory(uploadsPath);
var mediaContentTypes = new FileExtensionContentTypeProvider(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".gif"] = "image/gif",
    [".webp"] = "image/webp", [".mp4"] = "video/mp4", [".webm"] = "video/webm", [".ico"] = "image/x-icon",
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads",
    ContentTypeProvider = mediaContentTypes,
    ServeUnknownFileTypes = false,
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
    },
});
app.UseRouting();
app.UseMiddleware<TenantAvailabilityMiddleware>(); // نقطة منصّة/متجر على المضيف الصحيح؟ المتجر مفتوح؟
app.UseRateLimiter();        // سياسات النقاط ([EnableRateLimiting]) — بعد التوجيه، لكل (مضيف، عنوان)
app.UseCors("frontend");
app.UseAuthentication();     // من أنت؟ (يفكّ التوكن ويطابق tid مع المضيف)
app.UseMiddleware<RequestLoggingMiddleware>(); // سطر لكل طلب + نطاق (CorrelationId, TenantId, UserId)
app.UseAuthorization();      // هل يُسمح لك؟ (يفرض [Authorize])
app.MapControllers();

app.Run();
return 0;   // مسار المسبار أعلاه يعيد رمز خروج، فالنقطة كلها تعيد int.

// قائمة من الإعداد بصيغتيها: مصفوفة (Seed:DefaultTenantHosts:0) أو نص مفصول بفواصل (متغيّر بيئة واحد).
static string[] ReadList(IConfiguration configuration, string key) =>
    configuration.GetSection(key).Get<string[]>() is { Length: > 0 } list
        ? list
        : (configuration[key] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

// يُتاح لمشروع اختبارات التكامل (WebApplicationFactory<Program>).
public partial class Program { }
