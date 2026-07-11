# 🛒 متجر "سوق" — مشروع تعليمي متكامل (Clean Architecture)

مرجع عملي يطبّق كل مبدأ ذكرناه في ملف **"كيف تفكّر كمهندس برمجيات"**، عبر متجر إلكتروني
حقيقي قابل للتشغيل: **C# .NET 10 REST API** + **SQL Server** + **React**.

> الفكرة ليست تغطية كل ميزة في كل متجر، بل بناء **النواة الكاملة باحتراف**
> (كتالوج → سلة → دفع → طلب) بحيث تتعلّم منها *النمط* الذي تُضيف به أي ميزة بنفسك.

---

## 🗺️ خريطة المشروع

```
Souq/
├── src/
│   ├── Souq.Domain/          ← القلب: الكيانات والقواعد. لا يعتمد على أحد.
│   ├── Souq.Application/      ← حالات الاستخدام (CQRS). يعتمد على Domain فقط.
│   ├── Souq.Infrastructure/   ← التقنيات (EF Core, SQL, الدفع). يعتمد على Application.
│   └── Souq.API/              ← الواجهة (Controllers). نقطة التجميع.
├── frontend/                 ← واجهة React (Vite) بهوية "سوق".
├── database/                 ← سكربتات SQL Server لـ Azure Data Studio.
└── docs/                     ← هذا الدليل وشرح أعمق.
```

### قاعدة Clean Architecture الذهبية
**السهم يشير للداخل دائماً.** الطبقة الخارجية تعرف الداخلية، والعكس ممنوع:

```
API ──► Infrastructure ──► Application ──► Domain
                                            (لا يعرف أحداً)
```

لماذا؟ لأن قواعد العمل (Domain) هي ما يتغيّر ببطء وقيمته الأعلى. التقنيات
(قاعدة البيانات، بوّابة الدفع، حتى REST نفسه) تتغيّر بسرعة. **نعزل ما يتغيّر بسرعة
عمّا يتغيّر ببطء، ونمنع البطيء من الاعتماد على السريع.**

---

## 🧩 المبادئ المطبّقة وأين تجدها

| المبدأ | الملف | الفكرة |
|--------|-------|--------|
| **التغليف** (Encapsulation) | `Domain/Entities/Product.cs` | خصائص `private set`؛ تغيير المخزون عبر دوال محروسة فقط. |
| **كائن القيمة** (Value Object) | `Domain/ValueObjects/Money.cs` | المال = مبلغ + عملة، غير قابل للتغيير، يحرس قواعده. |
| **جذر التجمّع** (Aggregate Root) | `Domain/Entities/Order.cs` | الطلب يملك أسطره؛ يستحيل أن يصبح إجماليه غير متّسق. |
| **عكس التبعية** (DIP) | `Domain/Interfaces/*` | Domain يُعرّف الواجهات، Infrastructure يُنفّذها. |
| **CQRS** | `Application/Features/**` | فصل الأوامر (تُعدّل) عن الاستعلامات (تقرأ). |
| **نمط Result** | `Application/Common/Models/Result.cs` | أخطاء متوقّعة كقيم صريحة بدل استثناءات. |
| **التحقّق المركزي** | `Application/Common/Behaviors/ValidationBehavior.cs` | التحقّق يُطبّق تلقائياً قبل كل أمر. |
| **نمط Repository + Unit of Work** | `Infrastructure/Persistence/**` | عزل الوصول للبيانات + حفظ ذرّي. |
| **تجميد الفاتورة** | `Domain/Entities/OrderItem.cs` | نسخ الاسم والسعر لحظة الشراء (لا يتأثّر بتغيّر السعر). |
| **الحذف المنطقي** | `Domain/Entities/Product.cs` (`Deactivate`) | لا نحذف فعلياً؛ نحافظ على التاريخ. |
| **عزل بوّابة الدفع** | `Application/Common/Interfaces/IPaymentService.cs` | تبديل Stripe بـ PayPal = تنفيذ جديد، صفر تغيير في المنطق. |
| **Thin Controllers** | `API/Controllers/*` | الـ Controller يترجم HTTP فقط؛ لا منطق أعمال. |
| **معالجة أخطاء مركزية** | `API/Middleware/ExceptionHandlingMiddleware.cs` | شكل خطأ موحّد لكل النظام. |

---

## ▶️ كيف تشغّله

### 1) قاعدة البيانات (Azure Data Studio)
الطريقة الأسهل: شغّل المشروع و EF Core سيُنشئ الجداول ويبذرها تلقائياً.
أو يدوياً لفهم البنية: شغّل `database/01_schema.sql` ثم `database/02_seed.sql`.

### 2) الـ Backend (.NET 10)
أولاً (مرة واحدة): اضبط سلسلة الاتصال كسرّ محلي — لا تُكتب في `appsettings.json` أبداً:
```bash
dotnet user-secrets set "ConnectionStrings:Default" \
  "Server=localhost,1433;Database=SouqDb;User ID=sa;Password=<كلمة-مرورك>;Encrypt=True;TrustServerCertificate=True;" \
  --project src/Souq.API
```
ثم شغّل:
```bash
dotnet run --project src/Souq.API
```
يفتح Swagger على `http://localhost:5200/swagger` لتجربة كل نقاط الـ API.
عند الإقلاع تُطبَّق هجرات EF تلقائياً وتُبذر البيانات الأولية.

### 3) الـ Frontend (React)
```bash
cd frontend
npm install
npm run dev
```
يفتح على `http://localhost:5173`. الـ proxy يوجّه `/api` تلقائياً للـ backend.

---

## 🔌 نقاط الـ API

| الطريقة | المسار | الوظيفة |
|--------|--------|---------|
| GET | `/api/products?keyword=&categoryId=&page=1&pageSize=12` | قائمة المنتجات (بحث + تصفية + ترقيم) |
| GET | `/api/products/{id}` | منتج واحد |
| POST | `/api/products` | إنشاء منتج (مدير) |
| GET | `/api/categories` | الفئات |
| POST | `/api/orders` | إنشاء طلب + دفع |
| GET | `/api/orders/{id}` | متابعة طلب |

**جرّب محاكاة فشل الدفع:** أرسِل `paymentToken` يبدأ بـ `"fail"` في `POST /api/orders`.

---

## ➕ كيف تُضيف ميزة جديدة (النمط الذي ستكرّره)

لنفترض ميزة **"كوبون خصم"**. تمشي من الداخل للخارج:

1. **Domain:** أضِف كيان `Coupon` بقواعده (نسبة صالحة، تاريخ انتهاء)، وعدّل `Order`
   ليطبّق الخصم على الإجمالي بقاعدة محمية.
2. **Application:** أضِف `ApplyCouponCommand` + معالجه + مدقّقه.
3. **Infrastructure:** أضِف `CouponConfiguration` و `CouponRepository`.
4. **API:** أضِف نقطة في `OrdersController` تنادي الأمر عبر MediatR.
5. **Frontend:** أضِف حقل الكوبون في صفحة الدفع.

لاحظ: **قواعد العمل تعيش في Domain، والتنسيق في Application، والتقنية في Infrastructure،
والترجمة في API.** كل تغيير في مكانه الطبيعي — هذا هو العائد الحقيقي للمعمارية النظيفة.

---

## 🎨 ملاحظة عن الهوية البصرية

اخترت طابع **"سوق عربي حديث"**:
- **بترولي عميق** `#0F3B3A` للثقة والعمق + **زعفراني** `#E8A33D` للحركة.
- خطّان عربيّان: **Reem Kufi** للعناوين، **Tajawal** للنصوص.
- عنصر التوقيع: **قوس مشربية** يؤطّر أعلى كل بطاقة منتج — يربط التصميم بفكرة كشك السوق.
- التطبيق **RTL** بالكامل. كل ألوان وقياسات التصميم في متغيّرات واحدة (`frontend/src/styles.css`).
