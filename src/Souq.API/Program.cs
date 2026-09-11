using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Souq.API.Http;
using Souq.API.Middleware;
using Souq.API.Security;
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

// ── تخزين الوسائط محلياً: Storage:Local:RootPath (قرص مُثبَّت في الإنتاج، مجلّد مؤقت
// في الاختبارات) وإلا wwwroot/uploads. الصور تحت images/ والفيديو تحت videos/. ──
var uploadsPath = builder.Configuration["Storage:Local:RootPath"] is { Length: > 0 } configuredRoot
    ? configuredRoot
    : Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "uploads");
Directory.CreateDirectory(uploadsPath);
builder.Services.Configure<FileStorageOptions>(o =>
{
    o.RootPath = uploadsPath;
    o.PublicBasePath = "/uploads";
});

// ── المصادقة: التحقّق من توكن JWT الوارد ─────────────────────────────────
// المفتاح سرّ يأتي من user-secrets/البيئة. MapInboundClaims=false كي تصل المطالبات
// بالأسماء نفسها التي كتبها المُصدِّر تماماً.
var jwt = builder.Configuration.GetSection("Jwt");
var jwtKey = jwt["Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
    throw new InvalidOperationException(
        "مفتاح JWT (Jwt:Key) غير مضبوط. للتطوير: " +
        "dotnet user-secrets set \"Jwt:Key\" \"<مفتاح طويل عشوائي>\" --project src/Souq.API");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.NameIdentifier,
            ClockSkew = TimeSpan.FromSeconds(30),   // هامش ضيّق بدل 5 دقائق افتراضية
        };
    });
// ── التفويض بالصلاحيات (ADR-0019): [HasPermission] ⇒ سياسة تُبنى من اسمها، والقرار من
// RolePermissions. ICurrentUser: منفذ Application يُقرأ من مطالبات التوكن هنا فقط. ──
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

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
    p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// ── الهجرات + البذر عند الإقلاع. المدير الافتراضي في Development فقط؛ خارجها يُنشأ
// أول مدير من Seed:AdminEmail/Seed:AdminPassword إن ضُبطا (Phase 0 B1). ──
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var adminSeed = new AdminSeedOptions(
        app.Configuration["Seed:AdminEmail"], app.Configuration["Seed:AdminPassword"], app.Environment.IsDevelopment());
    await DbSeeder.SeedAsync(
        services.GetRequiredService<AppDbContext>(),
        services.GetRequiredService<IPasswordHasher>(),
        adminSeed,
        services.GetRequiredService<ILoggerFactory>().CreateLogger("Souq.Seeding"));
}

// ── خط أنابيب الطلب (Request Pipeline) — الترتيب مهم ──────────────────────
app.UseExceptionHandler();   // الاستثناءات ⇒ ProblemDetails (GlobalExceptionHandler) — أولاً
app.UseStatusCodePages();    // 401/403/404/405 بجسم فارغ من الإطار ⇒ ProblemDetails بنفس العقد
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// مجلد الرفع يُخدَم بأنواع وسائط مسموحة فقط + nosniff + CSP معزول (ADR-0016): حتى
// لو وصل ملف غير متوقّع إلى المجلّد بطريقة ما، لا يُخدَم كصفحة تُنفَّذ على أصل الموقع.
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
app.UseCors("frontend");
app.UseAuthentication();     // من أنت؟ (يفكّ التوكن)
app.UseAuthorization();      // هل يُسمح لك؟ (يفرض [Authorize])
app.MapControllers();

app.Run();

// يُتاح لمشروع اختبارات التكامل (WebApplicationFactory<Program>).
public partial class Program { }
