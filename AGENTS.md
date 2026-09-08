# مشروع سوق (Souq) — متجر إلكتروني

## نبذة
متجر إلكتروني بمعمارية Clean Architecture. الهدف: تحويله من مشروع مرجعي
إلى تطبيق إنتاجي حقيقي قابل للنشر والاستخدام.

## التقنيات
- Backend: C# .NET 10، REST API، MediatR (CQRS)، FluentValidation، EF Core 10.
- Database: SQL Server.
- Frontend: React 18 + Vite (RTL، عربي).

## بنية الطبقات (لا تُكسر هذه القاعدة أبداً)
API → Infrastructure → Application → Domain
- Domain: لا يعتمد على أي طبقة. الكيانات تحرس قواعدها بنفسها.
- Application: يعتمد على Domain فقط. حالات الاستخدام (Commands/Queries).
- Infrastructure: يعتمد على Application. تنفيذ المستودعات، DB، الدفع.
- API: نقطة التجميع. Controllers رفيعة تُترجم HTTP فقط.

## قواعد صارمة
1. أي قاعدة عمل جديدة تعيش في Domain، لا في Controller أو Handler.
2. لا تُضِف تبعية من طبقة داخلية إلى خارجية (تحقّق من csproj قبل أي using).
3. كل واجهة خارجية (دفع، بريد، تخزين) خلف Interface في Application ومنفّذة في Infrastructure.
4. لا تخزّن أسراراً (سلسلة الاتصال، مفاتيح JWT) في appsettings المرفوع — استخدم user-secrets/متغيرات بيئة.
5. حافظ على تعليقات معمارية موجزة ومفيدة عند النقاط المهمة، دون إفراط.

## أوامر مفيدة
- بناء: `dotnet build`
- تشغيل API: `dotnet run --project src/Souq.API`
- هجرات: `dotnet ef migrations add <Name> --project src/Souq.Infrastructure --startup-project src/Souq.API`
- اختبارات: `dotnet test`
- الواجهة: `cd frontend && npm install && npm run dev`

## أسلوب العمل معي
- نفّذ مرحلة واحدة في كل مرة. في نهايتها: لخّص التغييرات، شغّل البناء/الاختبارات،
  ثم توقّف وانتظر موافقتي قبل المرحلة التالية.
- عند أي قرار معماري كبير أو تبعية جديدة مهمة: اقترح واسأل قبل التنفيذ.
- بعد كل مرحلة ناجحة: اعمل commit برسالة بصيغة conventional commits.