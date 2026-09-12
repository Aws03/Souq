namespace Souq.Application.Common.Models;

// حدود الترقيم الموحّدة لكل قوائم الـ API. بلا حدّ أعلى يطلب أي زائر pageSize=1000000
// (استعلام ثقيل = حرمان خدمة رخيص)، وpage=0 كان يُنتج OFFSET سالباً ⇒ خطأ SQL ⇒ 500.
public static class PagingRules
{
    public const int MaxPageSize = 100;

    // والإزاحة كذلك (F-18): حجم الصفحة كان محدوداً ورقمها لا، فـ page=100000&pageSize=100 يقرأ ملايين الصفوف
    // ويرميها — العلّة نفسها المكتوبة أعلاه، في الطرف الذي فاتها. الحدّ على حاصل الضرب (Page-1)*PageSize لا على
    // Page وحدها، لأن الكلفة هي الإزاحة: page=10000&pageSize=1 و page=100&pageSize=100 سواء.
    // عشرة آلاف صفّ أعمق من أي تصفّح حقيقي (أضخم كتالوج واقعي بضعة آلاف)؛ ما بعدها يحتاج تصفية لا ترقيماً.
    public const int MaxOffset = 10_000;
}
