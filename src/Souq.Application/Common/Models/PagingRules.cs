namespace Souq.Application.Common.Models;

// حدود الترقيم الموحّدة لكل قوائم الـ API. بلا حدّ أعلى يطلب أي زائر pageSize=1000000
// (استعلام ثقيل = حرمان خدمة رخيص)، وpage=0 كان يُنتج OFFSET سالباً ⇒ خطأ SQL ⇒ 500.
public static class PagingRules
{
    public const int MaxPageSize = 100;
}
