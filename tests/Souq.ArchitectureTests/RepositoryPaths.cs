namespace Souq.ArchitectureTests;

// جذر المستودع وملفّاته — للاختبارات التي تفحص المصدر والتوثيق نصّاً لا أنواعاً (الواجهة البيضاء، التوثيق، الجرود المولَّدة).
// المشي يتخطّى مخرجات البناء والحزم (bin، obj، node_modules، dist) كي لا يفحص ما لم يكتبه أحد.
internal static class RepositoryPaths
{
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", "dist", ".git", ".vs", ".idea",
    };

    public static string Root { get; } = FindRoot();

    public static string Combine(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    public static string Relative(string absolute) =>
        Path.GetRelativePath(Root, absolute).Replace(Path.DirectorySeparatorChar, '/');

    public static IEnumerable<string> Walk(string relativeDirectory)
    {
        var start = Combine(relativeDirectory);
        if (!Directory.Exists(start)) yield break;

        var pending = new Stack<string>([start]);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(directory)) yield return file;
            foreach (var child in Directory.EnumerateDirectories(directory))
                if (!Skipped.Contains(Path.GetFileName(child))) pending.Push(child);
        }
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "Souq.Domain")))
                return dir.FullName;
        throw new InvalidOperationException("جذر المستودع غير موجود فوق مجلّد الاختبار");
    }
}
