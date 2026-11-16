using System.Text;
using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ كُلُّ مَوضِعِ «LinkFor» بِلا دَورٍ أَو بِدَورِ الصَفحَةِ يَبقى عَلى مُضيفِ الدَور (‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الأُولى · SEC-C2b-2) ═══
//
// **المَقيسُ قَبل** (عِندَ 220982d): 23 مَوضِعَ `LinkFor(x, null, …)` في 20 مِلَفّاً (16 مِنها مِن هذا المَدى) تُجيبُ عَلى مُضيفِ الدَورِ بِمُضيفِ المَتجَرِ مُطلَقاً —
// الجَرَسُ في كُلِّ صَفحَة، وتَنزيلُ بَياناتي، ومُرَشِّحاتُ الاستِكشاف، ورَقائِقُ التَصنيف، و«إلغاء»/«رُجوع»؛ والكَعكَةُ مَقصورَةٌ عَلى مُضيفِ الدَور. **والحارِس**: ماسِحٌ
// يَجِدُ كُلَّ مَوضِعِ استِدعاءٍ في القالِب (Razor وC#، بِلا التَعليقات)، ويَقرَأُ وَسيطَي الدَورِ والمَسار، ويَبني الرابِطَ بِـ`StoreUrl.Link` عَلى مُضيفِ الدَور
// (`ashare-vendor`) — الدَورُ `null` كَما هُوَ، ودَورُ الصَفحَةِ (`Role`/`urlRole`/`role`) دَورَ المُضيفِ كَما يَحقِنُه الحاقِن — ويَحمَرُّ إن خَرَجَ واحِدٌ عَن المُضيف
// سِوى العابِرِ المُعلَنِ بِاسمِه (بِطاقاتُ الأَدوار). يَطبَعُ ما عَدَّه، ويَحمَرُّ إن كانَ صِفراً.
// ‏(‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الأُولى · C2b-P-02 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **ومِن صَفحَةِ دَورٍ عَلى مُضيفِ المَتجَرِ أَيضاً** (`/r/vendor/…` — حَيثُ يَنزِلُ دُخولُ
// الدَور): كُلُّ مَوضِعٍ يُبنى ثانِيَةً عَلى `ashare.example.test` بِدَورِ الصَفحَةِ `vendor`. **والعابِرُ المُعلَنُ لَم يَعُد مَوضِعَ `LinkFor`**: بِطاقاتُ الأَدوارِ وبَيتُ الدَورِ
// بَعدَ المُنتَقي يُنادِيانِ `StoreUrl.App` بِاسمِه — فَمَواضِعُه تُعَدُّ وتُطبَعُ، ويَحمَرُّ إن زادَت عَلى المُسَمّاة (عابِرٌ جَديدٌ قَرارٌ مَرئِيٌّ في مُراجَعَة).

public class RoleHostLinkSitesTests(ITestOutputHelper output)
{
    private static readonly HostView VendorHost = new("example.test", "ashare", "https", "") { HostRole = "vendor", PageRole = "vendor" };
    private static readonly HostView StoreHostOnVendorPage = new("example.test", "ashare", "https", "") { PageRole = "vendor" };

    /// <summary>العابِرُ المُعلَن: مِلَفٌّ ونَصُّ مَسارِه — رابِطٌ إلى تَطبيقِ دَورٍ آخَرَ عَمداً. ‏(‏2026-11-07، C2b-P-02) خَرَجَت بِطاقاتُ الأَدوارِ إلى <c>StoreUrl.App</c>،
    /// فَلا مَوضِعَ <c>LinkFor</c> يُعلَنُ عابِراً.
    /// ‏(‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الثانِيَة · SEC-C2b-R2-1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **وعادَت البِطاقَةُ <c>LinkFor(Slug, null, $"r/{r.Slug}/login")</c>**
    /// بِحَرفِ تَوصيفِ ADR-035 — فَهِيَ العابِرُ المُعلَنُ هُنا ثانِيَةً، وتُفحَصُ بِسُلوكِها في <see cref="The_role_card_names_another_role_app_and_stays_for_its_own"/>.</summary>
    private static readonly (string File, string Path)[] DeclaredCrossApp = [("RoleLanding.razor", "$\"r/{r.Slug}/login\"")];

    /// <summary>مَواضِعُ <c>StoreUrl.App</c> المُسَمّاة — العابِرُ المُعلَنُ بِاسمِه (‏2026-11-07، C2b-P-02). ‏(‏2026-11-07، SEC-C2b-R2-1) مَوضِعٌ واحِدٌ لا اثنان:
    /// بَيتُ الدَورِ بَعدَ المُنتَقي (<c>RoleHome</c>)؛ والبِطاقَةُ عادَت <c>LinkFor</c>.</summary>
    private static readonly string[] DeclaredAppSites = ["MarketplaceTemplateExtensions.cs"];

    internal sealed record Site(string File, int Line, string Role, string Path);

    private static string TemplateRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "PlatformV1.slnx"))) d = d.Parent;
        return Path.Combine(d?.FullName ?? throw new InvalidOperationException("جَذرُ المُستَودَعِ غَيرُ مَوجود — الأَداةُ عَمياء."),
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace");
    }

    /// <summary>النَصُّ بِلا تَعليقاتِ Razor (<c>@* *@</c>) ولا أَسطُرِ <c>//</c> — وأَسطُرُه في مَواضِعِها (يُفرَغُ المَحذوفُ ولا يُزال)، فَالنِداءُ المُمتَدُّ عَلى سَطرَينِ يُقرَأ.</summary>
    private static string CodeText(string file)
    {
        var text = File.ReadAllText(file);
        if (file.EndsWith(".razor", StringComparison.Ordinal))
            text = Regex.Replace(text, @"@\*.*?\*@", m => new string('\n', m.Value.Count(c => c == '\n')), RegexOptions.Singleline);
        return string.Join('\n', text.Split('\n').Select(l => l.TrimStart().StartsWith("//", StringComparison.Ordinal) ? "" : l));
    }

    /// <summary>وَسائِطُ النِداءِ بَعدَ القَوسِ المَفتوح — فَواصِلُ المُستَوى الأَعلى وَحدَها، والنُصوصُ لا تُقَسَّم.</summary>
    internal static List<string>? Arguments(string text, int open)
    {
        var args = new List<string>();
        var cur = new StringBuilder();
        var depth = 0;
        for (var i = open + 1; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                var start = i;
                for (i++; i < text.Length && text[i] != '"'; i++) if (text[i] == '\\') i++;
                cur.Append(text, start, Math.Min(i, text.Length - 1) - start + 1);
                continue;
            }
            if (c is '(' or '[' or '{') depth++;
            if (c is ')' or ']' or '}')
            {
                if (depth == 0) { args.Add(cur.ToString().Trim()); return args; }
                depth--;
            }
            if (c == ',' && depth == 0) { args.Add(cur.ToString().Trim()); cur.Clear(); continue; }
            cur.Append(c);
        }
        return null;
    }

    internal static List<Site> Sites()
    {
        var root = TemplateRoot();
        var sites = new List<Site>();
        foreach (var file in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
                                 && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = CodeText(file);
            for (var at = text.IndexOf("LinkFor(", StringComparison.Ordinal); at >= 0; at = text.IndexOf("LinkFor(", at + 1, StringComparison.Ordinal))
            {
                var lineStart = text.LastIndexOf('\n', at) + 1;
                var lineEnd = text.IndexOf('\n', at);
                if (text[lineStart..(lineEnd < 0 ? text.Length : lineEnd)].Contains("public static string LinkFor(", StringComparison.Ordinal)) continue;
                var args = Arguments(text, at + "LinkFor".Length);
                if (args is not { Count: 3 }) continue;
                sites.Add(new Site(Path.GetRelativePath(root, file), text.Take(at).Count(c => c == '\n') + 1, args[1], Regex.Replace(args[2], @"\s+", " ")));
            }
        }
        return sites;
    }

    /// <summary>مَواضِعُ <c>StoreUrl.App(</c> في القالِب (بِلا تَعريفِها) — <c>مِلَفّ:سَطر</c>.</summary>
    internal static List<string> AppSites()
    {
        var root = TemplateRoot();
        return Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => CodeText(f).Split('\n').Select((l, i) => (f, i, l)))
            .Where(x => x.l.Contains("StoreUrl.App(", StringComparison.Ordinal))
            .Select(x => $"{Path.GetRelativePath(root, x.f).Replace(Path.DirectorySeparatorChar, '/')}:{x.i + 1}")
            .ToList();
    }

    /// <summary>المَسارُ الَّذي يَبنيه المَوضِع: نَصٌّ حَرفِيٌّ كَما هُوَ، ومُقحَمٌ بِقِيَمٍ عَيِّنَة (<c>{…}</c> ← <c>x</c>)، وتَعبيرٌ ← أَوَّلُ نَصٍّ فيه أَو <c>explore</c>.</summary>
    internal static string SamplePath(string arg)
    {
        var literal = Regex.Match(arg, @"(\$?)""((?:[^""\\]|\\.)*)""");
        if (!literal.Success) return "explore";
        var body = literal.Groups[2].Value;
        return literal.Groups[1].Value == "$" ? Regex.Replace(body, @"\{[^}]*\}", "x") : body;
    }

    [Fact]
    public void Every_roleless_or_page_role_link_site_stays_on_the_role_host()
    {
        var sites = Sites();
        var (roleless, pageRole, named, kept, declared) = (0, 0, 0, 0, new List<string>());
        var leaving = new List<string>();
        foreach (var s in sites)
        {
            string? role = s.Role switch
            {
                "null" => null,
                "Role" or "urlRole" or "role" => VendorHost.HostRole,
                _ => "—",
            };
            if (role == "—") { named++; continue; }
            if (role is null) roleless++; else pageRole++;
            foreach (var (where, view) in new[] { ("ashare-vendor", VendorHost), ("ashare /r/vendor", StoreHostOnVendorPage) })
            {
                var link = StoreUrl.Link(view, "ashare", role, SamplePath(s.Path));
                var stays = link.StartsWith('/') && !link.StartsWith("//", StringComparison.Ordinal);
                if (DeclaredCrossApp.Any(d => s.File.EndsWith(d.File, StringComparison.Ordinal) && s.Path == d.Path))
                {
                    declared.Add($"{where} · {s.File}:{s.Line} ⇒ {link}");
                    continue;
                }
                if (stays) kept++;
                else leaving.Add($"{where} · {s.File}:{s.Line} LinkFor(…, {s.Role}, {s.Path}) ⇒ {link}");
            }
        }
        var apps = AppSites();
        output.WriteLine($"مَواضِعُ StoreUrl.App (العابِرُ المُعلَن): {apps.Count} — {string.Join(" · ", apps)}");
        Assert.Equal(DeclaredAppSites.Order(StringComparer.Ordinal), apps.Select(a => a[..a.LastIndexOf(':')]).Distinct().Order(StringComparer.Ordinal));
        output.WriteLine($"مَواضِعُ LinkFor: {sites.Count} · بِلا دَور: {roleless} · بِدَورِ الصَفحَة: {pageRole} · بِدَورٍ مُسَمّىً آخَر (لا تُفحَص هُنا): {named}");
        output.WriteLine($"تَبقى عَلى المُضيف (مُضيفُ الدَور + صَفحَةُ دَورٍ عَلى مُضيفِ المَتجَر): {kept} · عابِرٌ مُعلَن: {declared.Count} · تُغادِر: {leaving.Count}");
        foreach (var d in declared) output.WriteLine("  ↗ " + d);
        foreach (var l in leaving) output.WriteLine("  ✗ " + l);
        Assert.True(sites.Count >= 150, $"فَحصٌ أَعمى: {sites.Count} مَوضِعاً");
        Assert.True(roleless >= 20, $"فَحصٌ أَعمى: {roleless} مَوضِعاً بِلا دَور");
        // ‏(‏2026-11-07، SEC-C2b-R2-1) والمُعلَنُ يُعَدُّ في كُلِّ مُضيفٍ مِن الاثنَين.
        Assert.Equal(DeclaredCrossApp.Length * 2, declared.Count);
        Assert.Empty(leaving);
    }

    /// <summary>
    /// <para><b>بِطاقَةُ الدَورِ بِسُلوكِها</b> (‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الثانِيَة · SEC-C2b-R2-1): <c>LinkFor(Slug, null, $"r/{role}/login")</c> بِالطَلَبِ
    /// كَما يَترُكُه الحاقِن — مِن جَذرِ المَتجَرِ ومِن صَفحَةِ دَورٍ آخَرَ ومِن مُضيفِ دَورٍ آخَرَ تُسَمّي مُضيفَ ذلكَ الدَور؛ ومِن صَفحَةِ دَورِها أَو مُضيفِه تَبقى
    /// (التَطبيقُ نَفسُه)؛ ودَورٌ مُؤَلَّفٌ بِلا اسمِ مُضيفٍ <c>/r/{role}/login</c> عَلى مُضيفِ المَتجَر؛ ووَضعُ المَسارِ بِحَرفِه.</para>
    /// </summary>
    [Theory]
    [InlineData("none", "/ashare", "vendor", "/ashare/r/vendor/login")]
    [InlineData("none", "/ashare/r/vendor", "vendor", "/ashare/r/vendor/login")]
    [InlineData("store", "/ashare", "vendor", "https://ashare-vendor.example.test/login")]
    [InlineData("store", "/ashare/r/customer", "vendor", "https://ashare-vendor.example.test/login")]
    [InlineData("store", "/ashare/r/vendor", "vendor", "/r/vendor/login")]
    [InlineData("store", "/ashare", "mandoob", "/r/mandoob/login")]
    [InlineData("vendor", "/ashare/r/vendor", "customer", "https://ashare-customer.example.test/login")]
    [InlineData("vendor", "/ashare/r/vendor", "vendor", "/login")]
    [InlineData("vendor", "/ashare/r/vendor", "mandoob", "https://ashare.example.test/r/mandoob/login")]
    public void The_role_card_names_another_role_app_and_stays_for_its_own(string where, string path, string cardRole, string expected)
    {
        var got = PageRoleLinkCharacterizationTests.LinkForOn(PageRoleLinkCharacterizationTests.Page(where, path), "ashare", null, $"r/{cardRole}/login");
        output.WriteLine($"{where} {path}: بِطاقَةُ {cardRole} ⇒ {got}");
        Assert.Equal(expected, got);
    }

    [Theory]
    [InlineData("\"explore\"", "explore")]
    [InlineData("$\"r/{r.Slug}/login\"", "r/x/login")]
    [InlineData("Via is null ? $\"login\" : $\"login?via={Via}\"", "login")]
    [InlineData("activeRole.HomeRoute", "explore")]
    public void The_sample_path_is_read_from_the_argument(string arg, string expected) => Assert.Equal(expected, SamplePath(arg));

    [Fact]
    public void The_argument_reader_keeps_strings_and_nested_calls_whole()
    {
        var text = "AuthSession.LinkFor(Slug, Role, $\"create-listing?category={Uri.EscapeDataString(c.Slug)}\") + x";
        var args = Arguments(text, text.IndexOf('('));
        Assert.Equal(["Slug", "Role", "$\"create-listing?category={Uri.EscapeDataString(c.Slug)}\""], args);
    }
}
