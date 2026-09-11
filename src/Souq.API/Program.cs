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
using Souq.Application.Common.Interfaces;
using Souq.Application.Common.Security;
using Souq.Infrastructure;
using Souq.Infrastructure.Persistence;
using Souq.Infrastructure.Services;

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
builder.Services.Configure<RefreshCookieOptions>(builder.Configuration.GetSection(RefreshCookieOptions.SectionName));
builder.Services.AddSouqRateLimiting(builder.Configuration);

// ── عنوان العميل ومخطّط الطلب الحقيقيان خلف Nginx (لحدّ المعدّل والسجلات وروابط البريد) — من
// الشبكات الموثوقة في ForwardedHeaders:KnownNetworks فقط؛ ترويسة X-Forwarded-For من عميل مباشر
// لا تُصدَّق (وإلا تجاوز أي مهاجم حدّ المعدّل بتزويرها). ──
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var network in ReadList(builder.Configuration, "ForwardedHeaders:KnownNetworks"))
        o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
});

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

// ── CORS للواجهة حين تعمل كخادم منفصل (التطوير المحلي). النشر خلف Nginx أصل واحد. ──
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173" };
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

// ── الهجرات + البذر عند الإقلاع. المدير الافتراضي في Development فقط؛ خارجها يُنشأ أول مدير
// من Seed:AdminEmail/Seed:AdminPassword إن ضُبطا (Phase 0 B1). Seed:DefaultTenantHosts يربط
// مضيفين بالمتجر الافتراضي صراحةً (حزمة Docker التجريبية: localhost). ──
await DbSeeder.SeedAsync(app.Services,
    new SeedOptions(
        app.Configuration["Seed:AdminEmail"], app.Configuration["Seed:AdminPassword"], app.Environment.IsDevelopment(),
        ReadList(app.Configuration, "Seed:DefaultTenantHosts"),
        app.Configuration["Seed:PlatformOwnerEmail"], app.Configuration["Seed:PlatformOwnerPassword"]),
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Souq.Seeding"));

// ── خط أنابيب الطلب (Request Pipeline) — الترتيب مهم ──────────────────────
app.UseForwardedHeaders();   // أولاً: عنوان العميل ومخطّطه من الوكيل الموثوق قبل أي قرار
app.UseMiddleware<CorrelationHeaderMiddleware>(); // X-Correlation-Id على كل استجابة، حتى الأخطاء (ADR-0018)
app.UseExceptionHandler();   // الاستثناءات ⇒ ProblemDetails (GlobalExceptionHandler)
app.UseStatusCodePages();    // 401/403/404/405 بجسم فارغ من الإطار ⇒ ProblemDetails بنفس العقد
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// المستأجر أولاً (قبل الملفات والمصادقة): المضيف ⇒ المتجر؛ مضيف مجهول ⇒ 404 قبل أي منطق.
app.UseMiddleware<TenantResolutionMiddleware>();

// مجلد الرفع يُخدَم بأنواع وسائط مسموحة فقط + nosniff + CSP معزول (ADR-0016): حتى
// لو وصل ملف غير متوقّع إلى المجلّد بطريقة ما، لا يُخدَم كصفحة تُنفَّذ على أصل الموقع.
var uploadsPath = app.Services.GetRequiredService<IOptions<FileStorageOptions>>().Value.RootPath;
Directory.CreateDirectory(uploadsPath);
var mediaContentTypes = new FileExtensionContentTypeProvider(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".gif"] = "image/gif",
    [".webp"] = "image/webp", [".mp4"] = "video/mp4", [".webm"] = "video/webm",
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

// قائمة من الإعداد بصيغتيها: مصفوفة (Seed:DefaultTenantHosts:0) أو نص مفصول بفواصل (متغيّر بيئة واحد).
static string[] ReadList(IConfiguration configuration, string key) =>
    configuration.GetSection(key).Get<string[]>() is { Length: > 0 } list
        ? list
        : (configuration[key] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

// يُتاح لمشروع اختبارات التكامل (WebApplicationFactory<Program>).
public partial class Program { }
