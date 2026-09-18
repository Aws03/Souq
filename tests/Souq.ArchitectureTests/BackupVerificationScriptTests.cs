using System.Diagnostics;
using System.Security.Cryptography;
using AwesomeAssertions;

namespace Souq.ArchitectureTests;

// ============================================================================
// scripts/backup-verify.sh هو الفحص الذي يفترض أن يوقظ إنساناً حين تصمت النسخ الاحتياطية.
// فحص لا أحد يختبره هو بالضبط الفحص الذي يمرّ دائماً — ولهذا يُختبر هنا بمجلّدات مصطنعة،
// بلا قاعدة بيانات وبلا Docker: كل حالاته ملفّات على القرص ورمز خروج.
//
// مكانه مع قواعد المصدر لا مع اختبارات التكامل: النصوص التشغيلية جزء من عقد المستودع كما
// التوثيق، وهذه الحالات تُنفَّذ في أجزاء من الثانية.
// ============================================================================
public class BackupVerificationScriptTests
{
    private static readonly string Script = RepositoryPaths.Combine("scripts/backup-verify.sh");

    [Fact]
    public void مجلد_بلا_أي_نسخة_يفشل()
    {
        using var backups = new TempDirectory();

        Run(backups.Path).Should().Be(1, "مهمّة لم تُشغَّل قط تبدو كمجلّد فارغ");
    }

    [Fact]
    public void مجموعة_كاملة_حديثة_تنجح()
    {
        using var backups = new TempDirectory();
        WriteSet(backups.Path, DateTime.UtcNow);

        Run(backups.Path).Should().Be(0);
    }

    [Fact]
    public void نسخة_قديمة_تفشل_وإن_كانت_سليمة()
    {
        // العطل الأخطر: المهمّة تفشل بصمت منذ أيام والمجلّد يبدو عامراً.
        using var backups = new TempDirectory();
        WriteSet(backups.Path, DateTime.UtcNow.AddDays(-9));

        Run(backups.Path).Should().Be(1);
    }

    // ── العمر يُفشَل مغلقاً (M18) ──────────────────────────────────────────
    // العطل الذي كُشف في مشوار Linux: العمر كان يُحسب بـ python3، وحين يغيب المفسّر كان السكربت
    // يطبع "لا فحص عمر" ويخرج **بنجاح**. أي أنّ نسخةً عمرها تسعة أيام تمرّ على خادم مُقتصَد،
    // وهو المضيف الأرجح لمهمّة نسخ احتياطي. الحالتان التاليتان تُثبّتان الاتّجاه الصحيح للفشل.

    [Fact]
    public void طابع_زمني_لا_يُقرأ_يفشل()
    {
        using var backups = new TempDirectory();
        var set = Path.Combine(backups.Path, "souq-backup-not-a-timestamp");
        Directory.CreateDirectory(set);
        File.WriteAllText(Path.Combine(set, "database.bak"), "backup bytes");

        Run(backups.Path).Should().Be(1, "اسم لا يحمل طابعاً يعني أنّ حداثة النسخة غير قابلة للإثبات");
    }

    [Fact]
    public void طابع_في_المستقبل_يفشل()
    {
        // ساعة خاطئة على مضيف النسخ تجعل كل نسخة تبدو حديثة إلى الأبد — فالحدّ نفسه يصير بلا معنى.
        using var backups = new TempDirectory();
        WriteSet(backups.Path, DateTime.UtcNow.AddDays(3));

        Run(backups.Path).Should().Be(1);
    }

    [Fact]
    public void فحص_العمر_لا_يعتمد_على_مفسّر_قد_يغيب()
    {
        // فحص شكلي مقصود: الحالتان أعلاه تُشغَّلان على آلة فيها python3، فلا تريان عودة الاعتماد.
        // وهذا يراها — وأي بديل (perl، node) له العطل نفسه: أداة غائبة تُسكِت إنذاراً.
        var script = File.ReadAllText(Script);

        script.Should().NotContain("python3", "حساب العمر يجب أن يبقى بـ date وحده (utc_epoch في lib.sh)");
        script.Should().NotContain("perl", "للسبب نفسه");
    }

    [Fact]
    public void تجزئة_لا_تطابق_تفشل()
    {
        using var backups = new TempDirectory();
        var set = WriteSet(backups.Path, DateTime.UtcNow);
        File.AppendAllText(Path.Combine(set, "database.bak"), "tampered");

        Run(backups.Path).Should().Be(1, "تلف صامت في التخزين أو النقل");
    }

    [Fact]
    public void مجموعة_ناقصة_تفشل()
    {
        using var backups = new TempDirectory();
        var set = WriteSet(backups.Path, DateTime.UtcNow);
        File.Delete(Path.Combine(set, "manifest.txt"));

        Run(backups.Path).Should().Be(1, "بلا بيان لا يمكن التحقّق من استعادة لاحقة");
    }

    [Fact]
    public void بيان_تالف_يفشل_ولو_تطابقت_تجزئته()
    {
        // العطل الحقيقي الذي وقع: USE يطبع رسالة إعلامية فتُلتقط كأنها قيمة الحقل. المجموعة
        // تبدو سليمة تماماً — التجزئات تطابق — وهي غير قابلة للتحقّق عند الاستعادة.
        using var backups = new TempDirectory();
        var set = WriteSet(backups.Path, DateTime.UtcNow,
            manifest: "migration_head=Changed database context to 'SouqDb'.\nrow_counts=Changed database context\nuploads=captured\n");

        Run(backups.Path).Should().Be(1);
    }

    [Theory]
    [InlineData(-2, 0)]      // تجربة قبل يومين، والحدّ 30 ⇒ مقبولة
    [InlineData(-400, 1)]    // تجربة قديمة جداً ⇒ نسخة لم تُستعَد منذ زمن
    public void دليل_تجربة_الاستعادة_يُفحص_بعمره(int drillDaysAgo, int expected)
    {
        using var backups = new TempDirectory();
        WriteSet(backups.Path, DateTime.UtcNow);
        File.WriteAllText(Path.Combine(backups.Path, "LAST-RESTORE-DRILL"),
            DateTime.UtcNow.AddDays(drillDaysAgo).ToString("yyyy-MM-ddTHH:mm:ssZ") + "\nset=x\n");

        Run(backups.Path, "--require-drill-within-days", "30").Should().Be(expected);
    }

    [Fact]
    public void غياب_دليل_التجربة_يفشل_حين_يُطلب()
    {
        using var backups = new TempDirectory();
        WriteSet(backups.Path, DateTime.UtcNow);

        Run(backups.Path, "--require-drill-within-days", "30").Should().Be(1,
            "نسخة لم تُستعَد ليست نسخة بعد");
    }

    // مجموعة صالحة: ملف قاعدة، بيان، وتجزئات تطابقهما فعلاً.
    private static string WriteSet(string root, DateTime takenUtc, string? manifest = null)
    {
        var set = Path.Combine(root, $"souq-backup-{takenUtc:yyyyMMdd'T'HHmmss}Z");
        Directory.CreateDirectory(set);
        File.WriteAllText(Path.Combine(set, "database.bak"), "backup bytes");
        File.WriteAllText(Path.Combine(set, "manifest.txt"), manifest ??
            "migration_head=20260911200431_Phase14Notifications\nrow_counts=Tenants:1,Users:2\nuploads=captured\n");

        var sums = new[] { "database.bak", "manifest.txt" }
            .Select(name => $"{Sha256(Path.Combine(set, name))}  {name}");
        File.WriteAllText(Path.Combine(set, "SHA256SUMS"), string.Join('\n', sums) + "\n");
        return set;
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static int Run(string directory, params string[] extra)
    {
        var arguments = new List<string> { Script, "--dir", directory };
        arguments.AddRange(extra);
        var start = new ProcessStartInfo("bash") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"souq-backups-{Guid.NewGuid():N}");

        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch (IOException) { } }
    }
}
