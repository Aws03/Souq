using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Souq.API.Middleware;
using Souq.Application;
using Souq.Application.Common.Interfaces;
using Souq.Infrastructure;
using Souq.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// ── تجميع الطبقات (كل طبقة تسجّل نفسها) ──────────────────────────────────
builder.Services.AddApplication();                         // طبقة حالات الاستخدام
builder.Services.AddInfrastructure(builder.Configuration); // التقنيات (DB, Payment, Auth...)
builder.Services.AddControllers();

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
builder.Services.AddCors(o => o.AddPolicy("frontend", p =>
    p.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));

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
app.UseCors("frontend");
app.UseAuthentication();     // من أنت؟ (يفكّ التوكن)
app.UseAuthorization();      // هل يُسمح لك؟ (يفرض [Authorize])
app.MapControllers();

app.Run();

// يُتاح لمشروع الاختبار (WebApplicationFactory) لاحقاً في المرحلة 5.
public partial class Program { }
