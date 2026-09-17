using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Souq.ArchitectureTests;

// ============================================================================
// التوثيق جزء من البناء (docs/10-TESTING/TestingStrategy.md): المستودع هو ذاكرة سوق الطويلة، وتوثيق يكذب أسوأ من غيابه.
// نفحص هنا ما يُفحص آلياً بلا حكم بشري:
//   • كل رابط نسبي ومرساة يصلان (لا خريطة تقود إلى فراغ بعد نقل ملف أو تغيير عنوان)؛
//   • كل مسار مكتوب ككود `src/…` موجود؛
//   • كل اسم كود مركّب مكتوب ككود (`OrderStatusChanged`) موجود فعلاً في الكود — ما لم يُبنَ بعد يُكتب مائلاً لا ككود؛
//   • كل ADR مكتمل البنية ومفهرس في docs/11-ADR/README.md.
// docs/archive تاريخ مكتوب لا يُفحص. الـ ADRs وخارطة الطريق سجلّات تاريخية: تُفحص روابطها، لا أسماء الكود فيها (قد تذكر
// أنواعاً حُذفت لاحقاً عن حقّ).
// ============================================================================
public class DocumentationTests
{
    private static readonly string[] RootDocuments = ["README.md", "AGENTS.md", "CLAUDE.md"];
    private static readonly string[] HistoricalRecords = ["docs/11-ADR/", "docs/12-ROADMAP/"];
    private static readonly string[] PathRoots = ["src/", "tests/", "frontend/", "docs/"];
    private static readonly HashSet<string> RootFiles = new(StringComparer.Ordinal)
    {
        "README.md", "AGENTS.md", "CLAUDE.md", "LICENSE", "Souq.sln", "docker-compose.yml", ".env.example",
        ".gitignore", ".dockerignore",
    };

    private static readonly Regex Link = new(@"(?<!\\)\[(?:[^\[\]]|\[[^\]]*\])*\]\((?<target>[^)\s]+)(?:\s+""[^""]*"")?\)", RegexOptions.Compiled);
    private static readonly Regex InlineCode = new(@"(?<!`)`(?<code>[^`\n]+)`(?!`)", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^(?<level>#{1,6})\s+(?<text>.+?)\s*#*\s*$", RegexOptions.Compiled);
    private static readonly Regex HtmlAnchor = new(@"\s(?:id|name)=""(?<id>[^""]+)""", RegexOptions.Compiled);
    private static readonly Regex Identifier = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);
    private static readonly Regex FileNameLike = new(@"^[\w.\-]+\.(md|cs|csproj|sln|js|jsx|mjs|json|ya?ml|css|html|conf)$", RegexOptions.Compiled);
    private static readonly Regex AdrFile = new(@"^\d{4}-.+\.md$", RegexOptions.Compiled);

    // أسماء كائنات قاعدة البيانات (فهارس، مفاتيح) اصطلاح لا أنواع في الكود: IX_Orders_TenantId_CustomerId.
    private static readonly Regex DatabaseObject = new(@"^(IX|AK|PK|FK|CK|UQ)_", RegexOptions.Compiled);

    private static readonly Lazy<Corpus> Code = new(BuildCorpus);
    private static readonly Dictionary<string, HashSet<string>> AnchorCache = new(StringComparer.Ordinal);

    [Fact]
    public void كل_رابط_نسبي_ومرساة_في_التوثيق_يصل()
    {
        var broken = new List<string>();
        foreach (var doc in LiveDocuments())
        {
            var absolute = RepositoryPaths.Combine(doc);
            foreach (var (line, prose) in ProseLines(File.ReadAllText(absolute)))
                foreach (Match link in Link.Matches(StripInlineCode(prose)))
                {
                    var target = link.Groups["target"].Value;
                    if (IsExternal(target)) continue;

                    var hashAt = target.IndexOf('#');
                    var pathPart = hashAt >= 0 ? target[..hashAt] : target;
                    var anchor = hashAt >= 0 ? target[(hashAt + 1)..] : "";
                    var resolved = pathPart.Length == 0
                        ? absolute
                        : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(absolute)!, Uri.UnescapeDataString(pathPart)));

                    if (!File.Exists(resolved) && !Directory.Exists(resolved))
                        broken.Add($"{doc}:{line} → {target}");
                    else if (anchor.Length > 0 && resolved.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && File.Exists(resolved)
                             && !Anchors(resolved).Contains(Uri.UnescapeDataString(anchor).ToLowerInvariant()))
                        broken.Add($"{doc}:{line} → {target} (لا عنوان بهذه المرساة)");
                }
        }

        broken.Should().BeEmpty("روابط التوثيق تقود إلى ملفّات وعناوين موجودة. المكسورة: {0}", Join(broken));
    }

    [Fact]
    public void كل_مسار_مكتوب_ككود_في_التوثيق_الحالي_موجود()
    {
        var missing = new List<string>();
        foreach (var doc in CurrentStateDocuments())
            foreach (var (line, prose) in ProseLines(File.ReadAllText(RepositoryPaths.Combine(doc))))
                foreach (Match span in InlineCode.Matches(prose))
                {
                    var code = span.Groups["code"].Value.Trim();
                    if (!LooksLikeRepositoryPath(code)) continue;
                    var path = code.Split('#')[0].TrimEnd('/');
                    if (!File.Exists(RepositoryPaths.Combine(path)) && !Directory.Exists(RepositoryPaths.Combine(path)))
                        missing.Add($"{doc}:{line} → {code}");
                }

        missing.Should().BeEmpty(
            "مسار في التوثيق يجب أن يوجد في المستودع (أو يُكتب نصّاً إن كان مستقبلياً). المفقودة: {0}", Join(missing));
    }

    [Fact]
    public void كل_اسم_كود_مركّب_في_التوثيق_الحالي_موجود_في_الكود()
    {
        var corpus = Code.Value;
        var unknown = new List<string>();
        foreach (var doc in CurrentStateDocuments())
            foreach (var (line, prose) in ProseLines(File.ReadAllText(RepositoryPaths.Combine(doc))))
                foreach (Match span in InlineCode.Matches(prose))
                {
                    var code = span.Groups["code"].Value.Trim();
                    if (code.Contains('/')) continue;                       // مسار أو مسار HTTP: يفحصه الاختبار السابق
                    if (code.Contains('<') || code.Contains('>')) continue;  // عيّنة فيها موضع يملؤه القارئ: <PreviousMigration>
                    if (FileNameLike.IsMatch(code))
                    {
                        if (!corpus.FileNames.Contains(code)) unknown.Add($"{doc}:{line} → {code} (لا ملف بهذا الاسم)");
                        continue;
                    }

                    foreach (Match token in Identifier.Matches(code))
                        if (IsCompoundName(token.Value) && !DatabaseObject.IsMatch(token.Value) && !corpus.Knows(token.Value))
                            unknown.Add($"{doc}:{line} → {token.Value} في `{code}`");
                }

        unknown.Should().BeEmpty(
            "كل اسم نوع أو عضو مكتوب ككود في التوثيق الحالي موجود في الكود — المخطَّط والمؤجَّل يُكتب *مائلاً* لا ككود."
            + " غير الموجودة: {0}", Join(unknown));
    }

    [Fact]
    public void كل_ADR_مكتمل_البنية_ومفهرس()
    {
        var index = File.ReadAllText(RepositoryPaths.Combine("docs/11-ADR/README.md"));
        var problems = new List<string>();

        foreach (var adr in Directory.EnumerateFiles(RepositoryPaths.Combine("docs/11-ADR"))
                     .Where(f => AdrFile.IsMatch(Path.GetFileName(f)))
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(adr);
            var text = File.ReadAllText(adr);

            foreach (var label in new[] { "Status", "Related modules", "Related ADRs" })
                if (!Regex.IsMatch(text, $@"^- \*\*{label}:\*\*\s*\S", RegexOptions.Multiline))
                    problems.Add($"{name}: '- **{label}:**' مفقود");
            if (!Regex.IsMatch(text, @"^- \*\*Date:\*\*\s*\d{4}-\d{2}-\d{2}", RegexOptions.Multiline))
                problems.Add($"{name}: '- **Date:** YYYY-MM-DD' مفقود");

            var headings = Regex.Matches(text, @"^## (.+?)\s*$", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value.Trim()).ToList();
            void Require(string section, Func<string, bool> accepts)
            {
                if (!headings.Any(accepts)) problems.Add($"{name}: قسم '{section}' مفقود");
            }

            Require("Context", h => h == "Context");
            Require("Problem", h => h == "Problem");
            Require("Options considered", h => h.StartsWith("Options considered", StringComparison.Ordinal)
                                               || h.StartsWith("Alternatives considered", StringComparison.Ordinal));
            Require("Decision", h => h.StartsWith("Decision", StringComparison.Ordinal) || h == "Options considered and decisions");
            Require("Consequences", h => h == "Consequences");

            if (!index.Contains($"]({name})", StringComparison.Ordinal))
                problems.Add($"{name}: غير مفهرس في docs/11-ADR/README.md");
        }

        problems.Should().BeEmpty(
            "كل قرار معماري كامل البنية (السياق، المشكلة، البدائل، القرار، العواقب) ومفهرس. النواقص: {0}", Join(problems));
    }

    // ── نظام المعرفة (مسار التعلّم) ─────────────────────────────────────────
    // وثيقة لا يصل إليها رابطٌ من مداخل القراءة كأنها غير موجودة: لا يجدها مهندس جديد ولا يُصلحها أحد حين يتغيّر الكود.
    // المداخل: README.md وAGENTS.md وفهرس docs. السجلّات التاريخية (docs/archive) خارج الفحص.
    [Fact]
    public void كل_وثيقة_حالية_يصل_إليها_رابط_من_مداخل_القراءة()
    {
        var entries = new[] { "README.md", "AGENTS.md", "docs/README.md" };
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(entries.Where(e => File.Exists(RepositoryPaths.Combine(e))));
        foreach (var entry in queue) reached.Add(entry);

        while (queue.Count > 0)
        {
            var doc = queue.Dequeue();
            var absolute = RepositoryPaths.Combine(doc);
            foreach (var (_, prose) in ProseLines(File.ReadAllText(absolute)))
                foreach (Match link in Link.Matches(StripInlineCode(prose)))
                {
                    var target = link.Groups["target"].Value;
                    if (IsExternal(target)) continue;
                    var pathPart = target.Split('#')[0];
                    if (pathPart.Length == 0) continue;
                    var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(absolute)!, Uri.UnescapeDataString(pathPart)));
                    // رابط إلى مجلّد يقود إلى README.md فيه، كما يعرضه GitHub.
                    if (Directory.Exists(resolved)) resolved = Path.Combine(resolved, "README.md");
                    if (!resolved.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || !File.Exists(resolved)) continue;
                    var relative = RepositoryPaths.Relative(resolved);
                    if (reached.Add(relative)) queue.Enqueue(relative);
                }
        }

        var orphans = LiveDocuments().Where(d => !reached.Contains(d)).ToList();
        orphans.Should().BeEmpty(
            "كل وثيقة حالية يُوصَل إليها برابط من README.md أو AGENTS.md أو docs/README.md. غير المربوطة: {0}", Join(orphans));
    }

    // مسار التعلّم يجيب "من أين أبدأ وماذا أقرأ بعدها؟" بأرقام متتالية — خطوة محذوفة أو مكرّرة تكسر الجواب نفسه.
    [Fact]
    public void مسار_التعلّم_خطوات_متتالية_من_00_إلى_18()
    {
        var text = File.ReadAllText(RepositoryPaths.Combine("docs/00-START-HERE/LearningPath.md"));
        var steps = Regex.Matches(text, @"^## (\d{2}) — ", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();

        steps.Should().Equal(Enumerable.Range(0, 19).Select(i => i.ToString("00", CultureInfo.InvariantCulture)),
            "خطوات LearningPath.md مرقّمة 00…18 بالترتيب، مرّة واحدة لكل رقم");
    }

    // صفحة ملاحة تحمل تاريخ آخر تحقّق من الكود — لا تاريخ آخر تعديل: القارئ يعرف كم عمر ما يثق به.
    // الفحص يضمن وجود السطر بتاريخ صالح لا صدقه؛ صدقه مسؤولية من يكتبه (docs/README.md، الأعراف).
    [Fact]
    public void صفحات_الملاحة_تذكر_تاريخ_آخر_تحقّق_من_الكود()
    {
        var navigation = RepositoryPaths.Walk("docs/00-START-HERE")
            .Where(f => f.EndsWith(".md", StringComparison.Ordinal))
            .Select(RepositoryPaths.Relative)
            .Append("docs/README.md")
            .OrderBy(f => f, StringComparer.Ordinal);
        var stamp = new Regex(@"\*\*Last verified against the (code|repository):\*\* (?<date>\d{4}-\d{2}-\d{2})");

        var missing = navigation
            .Where(doc =>
            {
                var match = stamp.Match(File.ReadAllText(RepositoryPaths.Combine(doc)));
                return !match.Success || !DateOnly.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
            })
            .ToList();

        missing.Should().BeEmpty(
            "كل صفحة ملاحة تحمل '**Last verified against the code:** YYYY-MM-DD'. الناقصة: {0}", Join(missing));
    }

    // رسالة الفشل تحمل القائمة كاملة (كوسيط، فالأقواس في المسارات لا تُفسَّر كتنسيق): إصلاح واحد لكل تشغيل مضيعة للوقت.
    private static string Join(IEnumerable<string> items) => string.Join("\n  • ", items.Distinct().OrderBy(i => i, StringComparer.Ordinal));

    // ── مجموعات الوثائق ─────────────────────────────────────────────────────

    private static IReadOnlyList<string> LiveDocuments() => RootDocuments
        .Where(f => File.Exists(RepositoryPaths.Combine(f)))
        .Concat(RepositoryPaths.Walk("docs")
            .Where(f => f.EndsWith(".md", StringComparison.Ordinal))
            .Select(RepositoryPaths.Relative)
            .Where(f => !f.StartsWith("docs/archive/", StringComparison.Ordinal)))
        .OrderBy(f => f, StringComparer.Ordinal)
        .ToList();

    // وثائق تصف الحاضر: أسماء الكود ومساراته فيها حقيقية اليوم.
    private static IEnumerable<string> CurrentStateDocuments() => LiveDocuments()
        .Where(f => !HistoricalRecords.Any(h => f.StartsWith(h, StringComparison.Ordinal)));

    // ── Markdown ────────────────────────────────────────────────────────────

    // أسطر النثر خارج كتل الكود المسوَّرة (```)، مع أرقامها للرسائل.
    private static IEnumerable<(int Line, string Text)> ProseLines(string markdown)
    {
        var inFence = false;
        var number = 0;
        foreach (var raw in markdown.Split('\n'))
        {
            number++;
            var line = raw.TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (!inFence) yield return (number, line);
        }
    }

    private static string StripInlineCode(string line) => InlineCode.Replace(line, m => new string(' ', m.Length));

    private static bool IsExternal(string target) =>
        target.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https:", StringComparison.OrdinalIgnoreCase)
        || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || target.StartsWith("//", StringComparison.Ordinal);

    // مراسي العناوين كما يولّدها GitHub: أحرف صغيرة، حذف الترقيم، المسافات شرطات، ولاحقة -1، -2 للعناوين المكرّرة.
    private static HashSet<string> Anchors(string absolutePath)
    {
        if (AnchorCache.TryGetValue(absolutePath, out var cached)) return cached;

        var anchors = new HashSet<string>(StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, line) in ProseLines(File.ReadAllText(absolutePath)))
        {
            foreach (Match html in HtmlAnchor.Matches(line)) anchors.Add(html.Groups["id"].Value.ToLowerInvariant());

            var heading = Heading.Match(line);
            if (!heading.Success) continue;
            var slug = Slug(heading.Groups["text"].Value);
            var count = seen.GetValueOrDefault(slug);
            anchors.Add(count == 0 ? slug : $"{slug}-{count}");
            seen[slug] = count + 1;
        }

        AnchorCache[absolutePath] = anchors;
        return anchors;
    }

    private static string Slug(string heading)
    {
        var text = Regex.Replace(heading, @"\[([^\]]*)\]\([^)]*\)", "$1").Replace("`", "").ToLowerInvariant();
        var slug = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            var category = char.GetUnicodeCategory(ch);
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_'
                || category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
                slug.Append(ch);
            else if (ch == ' ')
                slug.Append('-');
        }
        return slug.ToString();
    }

    private static bool LooksLikeRepositoryPath(string code) =>
        (PathRoots.Any(r => code.StartsWith(r, StringComparison.Ordinal)) || RootFiles.Contains(code))
        && !code.Any(c => c is ' ' or '*' or '<' or '>' or '{' or '}' or '|' or '?' or '…')
        && !code.Contains("...", StringComparison.Ordinal);

    // اسم كود مركّب: يبدأ بحرف كبير وفيه حرفان كبيران على الأقل وحرف صغير (OrderStatusChanged، IEmailSender) —
    // الكلمات العادية والثوابت بالأحرف الكبيرة لا تُفحص.
    private static bool IsCompoundName(string token) =>
        token.Length >= 4 && char.IsUpper(token[0]) && token.Any(char.IsLower) && token.Count(char.IsUpper) >= 2;

    // ── نصّ الكود ────────────────────────────────────────────────────────────

    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".props", ".targets", ".json", ".js", ".jsx", ".mjs", ".css", ".html", ".yml", ".yaml", ".conf",
    };

    private sealed record Corpus(HashSet<string> Identifiers, HashSet<string> FileNames, string Text)
    {
        // اسم موجود في الكود، أو اسم ملفّ بلا امتداده (توثيق يحيل إلى ملفّ: `InventoryConfiguration`).
        public bool Knows(string token) =>
            Identifiers.Contains(token) || FileNames.Contains(token) || Text.Contains(token, StringComparison.Ordinal);
    }

    private static Corpus BuildCorpus()
    {
        var rootFiles = new[]
        {
            "docker-compose.yml", ".env.example", "Souq.sln", "frontend/index.html", "frontend/vite.config.js",
            "frontend/package.json", "frontend/nginx.conf", "frontend/Dockerfile", "src/Souq.API/Dockerfile",
        }.Select(RepositoryPaths.Combine).Where(File.Exists);

        var files = RepositoryPaths.Walk("src").Concat(RepositoryPaths.Walk("tests")).Concat(RepositoryPaths.Walk("frontend/src"))
            .Where(f => CodeExtensions.Contains(Path.GetExtension(f)))
            .Concat(rootFiles)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var text = new StringBuilder();
        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            text.Append(content).Append('\n');
            foreach (Match match in Identifier.Matches(content)) identifiers.Add(match.Value);
        }

        var fileNames = RepositoryPaths.Walk("src").Concat(RepositoryPaths.Walk("tests")).Concat(RepositoryPaths.Walk("frontend"))
            .Concat(RepositoryPaths.Walk("docs"))
            .Concat(Directory.EnumerateFiles(RepositoryPaths.Root))
            .SelectMany(f => new[] { Path.GetFileName(f), Path.GetFileNameWithoutExtension(f) })
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        return new Corpus(identifiers, fileNames, text.ToString());
    }
}
