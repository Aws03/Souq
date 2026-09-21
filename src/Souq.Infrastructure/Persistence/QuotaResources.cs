using Microsoft.EntityFrameworkCore;
using Souq.Domain.Common;
using Souq.Domain.Enums;
using Souq.Domain.Identity;
using Souq.Domain.Platform;

namespace Souq.Infrastructure.Persistence;

// ============================================================================
// قاعدة العدّ لكل حدّ (C2، ADR-0049) — **الحقيقة** التي يُفترض أن يعكسها العدّاد.
//
// تُستعمَل في موضعين فقط، وكلاهما نادر: إنشاء صفّ العدّاد أوّل مرّة لمتجر قائم (وإلّا بدأ من صفر
// فأهدى المتجر كلّ ما عنده)، والمسح المصالِح الذي يُصلح الانحراف. المسار الساخن لا يَعُدّ إطلاقاً
// — يزيد صفّاً واحداً، وهذا هو بيت القصيد كلّه.
//
// كلّها استعلامات LINQ عادية على AppDbContext داخل نطاق متجر، فمرشّح المستأجر يحصرها في متجرها
// بلا شرط مكتوب بيد. العدّ خارج نطاق متجر يرمي (CurrentTenantId) لا يَعُدّ الكلّ.
//
// **ما يُعَدّ هنا يجب أن يطابق ما يُحجَز ويُطلَق على المسارات** — الأرشفة تُطلق منتجاً، والتعطيل
// يُطلق مقعداً. اختلافُ الثلاثة يعني عدّاداً ينحرف كل يوم ومسحاً يُصلحه كل ليلة: يعمل، ويخفي أن
// أحد المسارات نسي نداءه. LimitNames يشرح لماذا المؤرشَف والمعطَّل لا يُعَدّان.
//
// وقاموسٌ لا `switch`: `Counted` هو ما يقرؤه الاختبار المعماري ليتحقّق أن كل اسم في LimitNames
// له قاعدة عدّ. مع `switch` كانت القائمة ستبقى في رأس القارئ وحده.
// ============================================================================
internal static class QuotaResources
{
    private static readonly IReadOnlyDictionary<string, Func<AppDbContext, CancellationToken, Task<int>>> Counters =
        new Dictionary<string, Func<AppDbContext, CancellationToken, Task<int>>>(StringComparer.Ordinal)
        {
            [LimitNames.CatalogProducts] = (db, ct) =>
                db.Products.AsNoTracking().CountAsync(p => p.Status != ProductStatus.Archived, ct),

            // المقعد = حساب موظّف غير معطّل. الدعوة المعلّقة حالتها Active فتُعَدّ، وهو المقصود.
            [LimitNames.StaffSeats] = (db, ct) =>
                db.Users.AsNoTracking().CountAsync(
                    u => u.Status == UserStatus.Active
                         && (u.Role == Roles.TenantAdmin || u.Role == Roles.TenantStaff), ct),
        };

    // يقرؤه `كل_اسم_حدّ_له_قاعدة_عدّ` انعكاسياً — اسمٌ في الكتالوج بلا قاعدة عدّ هو حدٌّ يُباع
    // ولا يُفرَض، ولا شيء آخر كان سيذكّر بذلك.
    public static IReadOnlyCollection<string> Counted => Counters.Keys.ToList();

    public static Task<int> CountAsync(AppDbContext db, string limitName, CancellationToken ct) =>
        Counters.TryGetValue(limitName, out var count)
            ? count(db, ct)
            : throw new InvalidOperationException($"لا قاعدة عدّ للحدّ '{limitName}' — أضفها في QuotaResources");
}
