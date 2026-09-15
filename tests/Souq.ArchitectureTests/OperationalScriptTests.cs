using System.Diagnostics;
using AwesomeAssertions;

namespace Souq.ArchitectureTests;

// ============================================================================
// النصوص التشغيلية جزء من عقد المستودع: فحص الإعداد قبل النشر، وقابلية تشغيل كل نص أصلاً.
//
// الفحص الثاني وُلد من عطل حقيقي: ${VAR,,} من bash 4، وmacOS يشحن 3.2 — و`bash -n` يمرّرها
// لأنها خطأ وقت تنفيذ لا وقت تحليل. نصّ لا يُنفَّذ قطّ على جهاز مطوّر هو نصّ يُكتشف عطله في
// أسوأ لحظة، فكل نص يُستدعى هنا فعلاً بصدفة النظام.
// ============================================================================
public class OperationalScriptTests
{
    private static string Script(string name) => RepositoryPaths.Combine($"scripts/{name}");

    public static TheoryData<string> AllScripts()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(RepositoryPaths.Combine("scripts"), "*.sh")
                     .Select(Path.GetFileName).Order(StringComparer.Ordinal))
            data.Add(file!);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllScripts))]
    public void كل_نص_يعمل_على_صدفة_النظام_لا_على_bash_حديث_فقط(string name)
    {
        if (name == "lib.sh") return;   // يُضمَّن ولا يُشغَّل

        var (exitCode, output) = Run(Script(name), "--help");

        output.Should().NotContain("bad substitution", "بناء من bash 4 على نظام يشحن 3.2");
        output.Should().NotContain("syntax error");
        exitCode.Should().Be(0, $"{name} --help يجب أن يعمل: {output}");
    }

    [Fact]
    public void مثال_الإعداد_المرفوع_يُرفض_للإنتاج()
    {
        // .env.example يجب أن يبقى غير صالح للاستعمال — وهذا الفحص هو ما يمنع انزلاقه.
        var (exitCode, output) = Run(Script("audit-config.sh"),
            "--env-file", RepositoryPaths.Combine(".env.example"), "--environment", "Production");

        exitCode.Should().Be(1);
        output.Should().Contain("JWT_KEY");
    }

    [Theory]
    [InlineData("Production", 1)]     // أدوات العرض التوضيحي خطر على زبائن
    [InlineData("Development", 0)]    // وهي نفسها مشروعة محلياً
    public void إعداد_العرض_التوضيحي_يُقاس_بالبيئة(string environment, int expected)
    {
        using var file = new TempEnv("""
            JWT_KEY=Zq4vN8pR2mK7wX3tY6uB9nC5hJ1dF0gS4aL7eI2oP5rT8
            PAYMENTS_PROVIDER=Fake
            EMAIL_PROVIDER=Log
            SEED_DEMO_DATA=true
            TRUSTED_PROXY_NETWORKS=172.16.0.0/12
            """);

        Run(Script("audit-config.sh"), "--env-file", file.Path, "--environment", environment)
            .ExitCode.Should().Be(expected);
    }

    [Fact]
    public void ثقة_مطلقة_بالوكيل_مرفوضة_في_كل_بيئة()
    {
        // 0.0.0.0/0 يعني أن أي عميل يزوّر عنوانه ومخطّطه — لا بيئة تجعل ذلك مقبولاً.
        using var file = new TempEnv("""
            JWT_KEY=Zq4vN8pR2mK7wX3tY6uB9nC5hJ1dF0gS4aL7eI2oP5rT8
            TRUSTED_PROXY_NETWORKS=0.0.0.0/0
            """);

        Run(Script("audit-config.sh"), "--env-file", file.Path, "--environment", "Development")
            .ExitCode.Should().Be(1);
    }

    [Fact]
    public void إعداد_إنتاج_سليم_يمرّ_بلا_تحذير()
    {
        using var file = new TempEnv("""
            DB_SA_PASSWORD=Xk7#mQ2vLp9$wRt4
            JWT_KEY=Zq4vN8pR2mK7wX3tY6uB9nC5hJ1dF0gS4aL7eI2oP5rT8
            PAYMENTS_PROVIDER=Stripe
            EMAIL_PROVIDER=Resend
            SEED_DEMO_DATA=false
            TRUSTED_PROXY_NETWORKS=172.16.0.0/12
            DB_MIGRATIONS_CONNECTION=Server=db;Database=SouqDb;User Id=souq_migrator;Password=Yh3
            SECRETS_KEY=c291cV9zZWNyZXRzX2tleV8zMl9ieXRlc19sb25nISE=
            FRONTEND_URL=https://store.example
            """);

        var (exitCode, output) = Run(Script("audit-config.sh"), "--env-file", file.Path, "--environment", "Production");

        exitCode.Should().Be(0);
        output.Should().Contain("errors=0").And.Contain("warnings=0",
            "فحص يُطلق تحذيرات على إعداد سليم يُتجاهَل بعد أسبوع");
    }

    private static (int ExitCode, string Output) Run(string script, params string[] arguments)
    {
        var start = new ProcessStartInfo("bash") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(script);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private sealed class TempEnv : IDisposable
    {
        public string Path { get; }

        public TempEnv(string content)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"souq-env-{Guid.NewGuid():N}");
            File.WriteAllText(Path, content);
        }

        public void Dispose() { try { File.Delete(Path); } catch (IOException) { } }
    }
}
