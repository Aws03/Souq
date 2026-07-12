using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Souq.API.Middleware;
using Souq.Application;
using Souq.Application.Common.Interfaces;
using Souq.Infrastructure;
using Souq.Infrastructure.Persistence;
using Souq.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// ── تجميع الطبقات (كل طبقة تسجّل نفسها) ──────────────────────────────────
builder.Services.AddApplication();                         // طبقة حالات الاستخدام
builder.Services.AddInfrastructure(builder.Configuration); // التقنيات (DB, Payment, Auth...)

// enums تُقرأ/تُكتب كنصوص ("Shipped" بدل 2) — أوضح لمستهلكي الـ API.
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ── تخزين ملفات الصور محلياً: نضبط المسار الفيزيائي هنا حيث يُعرف wwwroot ──
var uploadsPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "uploads");
Directory.CreateDirectory(uploadsPath);
builder.Services.Configure<FileStorageOptions>(o =>
{
    o.RootPath = uploadsPath;
    o.PublicBasePath = "/uploads";
});

// ── تخزين فيديوهات المنتجات: مجلّد فرعي تحت uploads نفسه (يخدمه نفس مزوّد
// الملفات الثابتة أدناه على /uploads تلقائياً، بلا تسجيل إضافي). ──
var videosPath = Path.Combine(uploadsPath, "videos");
Directory.CreateDirectory(videosPath);
builder.Services.Configure<VideoStorageOptions>(o =>
{
    o.RootPath = videosPath;
    o.PublicBasePath = "/uploads/videos";
});

// ── المصادقة: التحقّق من توكن JWT الوارد ─────────────────────────────────
// المفتاح سرّ يأتي من user-secrets/البيئة. نُعطّل إعادة تخطيط المطالبات
// (MapInboundClaims=false) كي تصل بالأسماء نفسها التي كتبها المُصدِّر تماماً.
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
builder.Services.AddAuthorization();

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

// ── سياسة CORS للسماح للواجهة (React) بالاتصال بالـ API ───────────────────
// الأصول المسموحة تأتي من Cors:AllowedOrigins (appsettings/متغيرات بيئة)، وتقع
// افتراضياً على خادم Vite للتطوير المحلي إن لم يُضبط شيء. النشر خلف Nginx
// (docker-compose.yml) لا يحتاج هذه السياسة إطلاقاً — المتصفح يرى أصلاً واحداً
// فقط هناك (Nginx يوكّل /api داخلياً)، فهذه السياسة تخدم فقط تشغيل الـ API
// والواجهة كخادمين منفصلين (تطوير محلي، أو نشر بلا توكيل عكسي).
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173" };
builder.Services.AddCors(o => o.AddPolicy("frontend", p =>
    p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// ── بذر قاعدة البيانات تلقائياً عند الإقلاع (للتجربة) ─────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    await DbSeeder.SeedAsync(db, hasher);
}

// ── خط أنابيب الطلب (Request Pipeline) — الترتيب مهم ──────────────────────
app.UseMiddleware<ExceptionHandlingMiddleware>();   // معالجة الأخطاء أولاً
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
// نخدم مجلد الرفع بمزوّد ملفات صريح على uploadsPath: لا نعتمد على WebRootPath
// لأن wwwroot قد لا يكون موجوداً لحظة بناء المضيف (فيصبح المزوّد الافتراضي فارغاً).
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads",
});
app.UseCors("frontend");
app.UseAuthentication();     // من أنت؟ (يفكّ التوكن)
app.UseAuthorization();      // هل يُسمح لك؟ (يفرض [Authorize])
app.MapControllers();

app.Run();

// يُتاح لمشروع الاختبار (WebApplicationFactory) لاحقاً في المرحلة 5.
public partial class Program { }
