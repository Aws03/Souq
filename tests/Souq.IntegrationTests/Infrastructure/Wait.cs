namespace Souq.IntegrationTests.Infrastructure;

// ============================================================================
// انتظارٌ بشرطٍ ومهلة — لا بـ `Task.Delay` برقمٍ مُخمَّن (M14).
//
// تحتاجه الاختبارات التي فيها طرفان يجريان معاً: سباق مُرسِلَي الصادر ينتظر أن يُمسك الأول العقد قبل أن
// يبدأ الثاني. نومٌ ثابت يجعل مثل هذا الاختبار بطيئاً دائماً ومتقلّباً أحياناً — أطول ممّا يلزم على آلةٍ
// سريعة، وأقصر ممّا يلزم على آلةٍ محمَّلة، وهو ما يجعله يُلغى لاحقاً بوصفه "متقطّعاً" بدل أن يُصلَح.
//
// والفشل يرمي برسالةٍ تقول ما لم يحدث، لا `false` تُقرأ في تأكيدٍ غامض بعد أسطر.
// ============================================================================
internal static class Wait
{
    public static async Task UntilAsync(
        Func<Task<bool>> condition, string whatDidNotHappen, int timeoutSeconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        var delay = TimeSpan.FromMilliseconds(10);

        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(delay);
            // تباعدٌ متصاعد بسقف: أول محاولةٍ فوراً تقريباً (الشرط يتحقّق عادةً في مللي ثوانٍ)، ثمّ
            // تباعدٌ كي لا يستقصي الاختبار قاعدةَ بياناتٍ مئات المرّات في الثانية إن طال الانتظار.
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 250));
        }

        throw new TimeoutException($"{whatDidNotHappen} خلال {timeoutSeconds} ثانية.");
    }
}
