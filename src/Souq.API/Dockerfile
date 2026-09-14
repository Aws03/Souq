# ============================================================================
# صورة Souq.API — بناء متعدّد المراحل (Multi-stage). المرحلة الأولى تستخدم صورة
# الـ SDK الثقيلة للبناء فقط، والثانية تنسخ الناتج المنشور إلى صورة تشغيل خفيفة
# (aspnet runtime) — لا SDK ولا كود مصدر في الصورة النهائية، حجم أصغر وسطح هجوم أقل.
#
# سياق البناء المتوقَّع: جذر المستودع (docker-compose.yml يضبط هذا) — نحتاج
# مشاريع Domain/Application/Infrastructure الشقيقة، لا src/Souq.API وحده.
# ============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# طبقة الاعتماديات مُخزَّنة مؤقتاً بمعزل عن الكود — نسخ ملفات csproj فقط أولاً
# يعني أن تعديل كود لاحقاً لا يُعيد تنزيل الحزم ما لم تتغيّر الاعتماديات نفسها.
COPY src/Souq.Domain/Souq.Domain.csproj src/Souq.Domain/
COPY src/Souq.Application/Souq.Application.csproj src/Souq.Application/
COPY src/Souq.Infrastructure/Souq.Infrastructure.csproj src/Souq.Infrastructure/
COPY src/Souq.API/Souq.API.csproj src/Souq.API/
RUN dotnet restore src/Souq.API/Souq.API.csproj

COPY src/ src/
RUN dotnet publish src/Souq.API/Souq.API.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# صورة aspnet الحديثة تستمع افتراضياً على 8080 داخل الحاوية (لا امتيازات جذر
# مطلوبة، بخلاف المنفذ 80 التقليدي) — نُصرّح بهذا بوضوح بدل الاعتماد على الافتراضي.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app .

# فحص صحّة الحاوية بالتطبيق نفسه (HealthProbe في Observability/Health.cs): هذه الصورة بلا curl
# ولا wget عمداً — إضافتهما تمنح المهاجم عميل HTTP جاهزاً بعد أي اختراق، وزمن تشغيل .NET
# الموجود أصلاً يكفي. المهلة الأولى سخيّة: الهجرات والبذر تجري عند الإقلاع قبل قبول أي طلب.
HEALTHCHECK --interval=30s --timeout=10s --start-period=90s --retries=3 \
    CMD ["dotnet", "Souq.API.dll", "--health-check"]

ENTRYPOINT ["dotnet", "Souq.API.dll"]
