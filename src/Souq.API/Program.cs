using Souq.API.Middleware;
using Souq.Application;
using Souq.Infrastructure;
using Souq.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// ── تجميع الطبقات (كل طبقة تسجّل نفسها) ──────────────────────────────────
builder.Services.AddApplication();                         // طبقة حالات الاستخدام
builder.Services.AddInfrastructure(builder.Configuration); // التقنيات (DB, Payment...)
builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();                          // توثيق API تلقائي

// ── سياسة CORS للسماح للواجهة (React) بالاتصال بالـ API ───────────────────
builder.Services.AddCors(o => o.AddPolicy("frontend", p =>
    p.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// ── بذر قاعدة البيانات تلقائياً عند الإقلاع (للتجربة) ─────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbSeeder.SeedAsync(db);
}

// ── خط أنابيب الطلب (Request Pipeline) — الترتيب مهم ──────────────────────
app.UseMiddleware<ExceptionHandlingMiddleware>();   // معالجة الأخطاء أولاً
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors("frontend");
app.MapControllers();

app.Run();
