using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── تَوصيف قَرار «إلى أَينَ يَجوز أَن نُحَوِّل» ────────────────────────
//
// الدَعوى المَفحوصَة: وِجهَةٌ تُقرَأ مِن الطَّلَب لا تَخرُج بِالمُستَخدِم
// مِن أَصلِنا — أَيّاً كانَ شَكل الالتِفاف. والاختِبارات السالِبَة هي
// المَقصودَة هُنا: الموجَب كانَ يَمُرّ قَبل الإصلاح وبَعدَه، والسالِب
// وَحدَه هُوَ ما تَغَيَّر.
public class LocalRedirectTests
{
    // ─── ١. السالِب — كُلّ صيغَة تَخرُج مِن الأَصل ─────────────────────

    [Theory]
    // مُضيف خارِجيّ بِمُخَطَّط صَريح
    [InlineData("https://example.com")]
    [InlineData("http://evil.com/path")]
    [InlineData("HTTPS://EVIL.COM")]
    // بِلا مُخَطَّط — وهو ما كانَ StartsWith("/") يُمَرِّرُه
    [InlineData("//evil.com")]
    [InlineData("//evil.com/path?x=1")]
    [InlineData(@"/\evil.com")]
    [InlineData(@"/\/evil.com")]
    // مُخَطَّطات أُخرى
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>1</script>")]
    [InlineData("mailto:a@b.c")]
    // مَحرَف تَحَكُّم يَحذِفُه المُتَصَفِّح قَبل التَحليل
    [InlineData("/\tevil")]
    [InlineData("/\nhttps://evil.com")]
    [InlineData("/ /evil.com")]
    // مَسار نِسبيّ بِلا جِذر — يُحَلّ مُقابِل الصَفحَة الحالِيَّة لا مُقابِل الأَصل
    [InlineData("evil.com")]
    [InlineData("../admin")]
    // فارِغ
    [InlineData("")]
    [InlineData(null)]
    public void ForeignDestinations_AreRejected(string? candidate)
    {
        Assert.False(LocalRedirect.IsLocal(candidate));
        Assert.Equal("/", LocalRedirect.Resolve(candidate, "/"));
    }

    [Fact]
    public void ARejectedDestination_FallsBackToWhatTheCallerNamed_NotToARoot()
    {
        // السُقوط لَيسَ ثابِتاً: كُلّ نُقطَة تُسَمّي وِجهَتَها الآمِنَة.
        Assert.Equal("/ashare/listings/7",
            LocalRedirect.Resolve("https://evil.com", "/ashare/listings/7"));
        Assert.Equal("/ashare/me",
            LocalRedirect.Resolve("//evil.com", "/ashare/me"));
    }

    // ─── ٢. المُوجَب — مَسار مَحَلِّيّ يَمُرّ كَما هُوَ ────────────────────

    [Theory]
    [InlineData("/")]
    [InlineData("/ashare")]
    [InlineData("/ashare/explore")]
    [InlineData("/ashare/r/broker/me")]
    [InlineData("/ashare/listings/3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    [InlineData("/ashare/explore?cat=cars&page=2")]
    [InlineData("/ashare/me#tab")]
    [InlineData("/%D8%A5%D8%B9%D9%84%D8%A7%D9%86")]
    public void LocalPaths_PassUnchanged(string candidate)
    {
        Assert.True(LocalRedirect.IsLocal(candidate));
        Assert.Equal(candidate, LocalRedirect.Resolve(candidate, "/"));
    }

    [Fact]
    public void AnEncodedSlashIsNotASlash()
    {
        // ‏%2f لا يَصير فاصِلَ سُلطَة عِندَ المُتَصَفِّح، فَلا يُرفَض.
        // مَكتوب صَراحَةً لِأَنَّه المَوضِع الَّذي يُغري بِمَنعٍ زائِد.
        Assert.True(LocalRedirect.IsLocal("/%2f%2fevil.com"));
    }

    // ─── ٣. الحَدّ نَفسُه — لا نُسخَة ثانِيَة مِن القَرار ────────────────

    [Fact]
    public void NoEndpointDecidesRedirectLocalityOnItsOwn()
    {
        // القاعِدَة ٢ في CLAUDE.md: الحَدّ الَّذي لا يُقاس آليّاً يَنهار.
        // فَهُنا يُقاس: أَيّ عَودَة إلى «‏StartsWith("/")» كَشَرط تَحويل
        // تُسقِط هذا الاختِبار — والبَديل مَوضِع واحِد اسمُه LocalRedirect.
        var raw = File.ReadAllText(Path.Combine(
            ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "MarketplaceTemplateExtensions.cs"));

        // التَعليقات تُقَصّ قَبل الفَحص: سَطرٌ يَشرَح الشَرط القَديم لَيسَ
        // الشَرط القَديم — وأَداةٌ لا تُفَرِّق بَينَهُما تَكذِب في
        // الاتِّجاهَين.
        var source = Regex.Replace(raw, @"//[^\r\n]*", string.Empty);

        var handRolled = Regex.Matches(source, @"StartsWith\(""/""\)").Count;
        Assert.Equal(0, handRolled);

        // وكُلّ قِراءَة لِوِجهَة مِن الطَّلَب مَوصولَة بِالقَرار الواحِد.
        var reads = Regex.Matches(source,
            @"(?:req\.Form|req\.Query)\[""(?:return|returnUrl)""\]").Count;
        var guarded = Regex.Matches(source,
            @"LocalRedirect\.Resolve\(\s*(?:req\.Form|req\.Query)\[""(?:return|returnUrl)""\]").Count;
        Assert.True(reads > 0, "لا قِراءَة وِجهَة أَصلاً — الاختِبار أَعمى.");
        Assert.Equal(reads, guarded);
    }

    // ─── ٤. والصَفَحاتُ أَيضاً — وِجهَةٌ تُربَطُ بِالاستِعلامِ لا تُطبَعُ قَبلَ القَرار (‏2026-10-24) ────
    //
    // **الكَلفَة** (تَدقيقُ الإطلاق lens-security-1، حاجِبَة): الفاحِصُ أَعلاه يَقرَأُ `MarketplaceTemplateExtensions.cs` وَحدَه، فَلَم
    // يَرَ `StudioConsent.razor` تَطبَعُ `[SupplyParameterFromQuery(Name = "returnUrl")]` كَما جاءَ في
    // `<script>location.replace('…')</script>` — رابِطٌ بِـ`javascript:` يُنَفِّذُ في أَصلِنا بِكوكي مُشرِفِ المَنَصَّة. فَصارَ يَقرَأُ كُلَّ
    // `.razor` في `libs/` و`apps/`: كُلُّ خاصِّيَّةٍ مَربوطَةٍ بِمِفتاحِ `return` أَو `returnUrl` لا تُذكَرُ إلّا أَوَّلَ وَسيطٍ لِـ
    // `LocalRedirect.Resolve(` — أَو لِـ`Uri.EscapeDataString(`، لِأَنّ المُرَمَّزَ لا يَحمِلُ `:` ولا `/` فَلا يَصيرُ مُخَطَّطاً ولا مُضيفاً
    // في أَيِّ مَوضِع (نَمَطُ `Terms.razor`، ونُقطَتُه تَسأَلُ القَرارَ عِندَ القَبول).

    private static readonly Regex QueryBound = new(
        @"\[SupplyParameterFromQuery(?:\(\s*Name\s*=\s*""(?<q>[^""]+)""\s*\))?\]\s*public\s+string\??\s+(?<p>\w+)");

    /// <summary>ما يَربِطُه مِلَفٌّ بِمِفتاحِ <c>return</c>/<c>returnUrl</c> في الاستِعلام، وكُلُّ ذِكرٍ لَه لا يَقَعُ أَوَّلَ وَسيطٍ لِلقَرار.
    /// المِفتاحُ اسمُ <c>Name</c> إن كُتِب، وإلّا اسمُ الخاصِّيَّةِ نَفسُه (المُزَوِّدُ يُطابِقُه بِلا حالَةِ أَحرُف).</summary>
    internal static (int Bound, List<string> Unguarded) ReturnUrlUses(string razor)
    {
        var code = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(razor));
        var bound = 0;
        var unguarded = new List<string>();
        foreach (Match m in QueryBound.Matches(code))
        {
            var key = m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["p"].Value;
            if (!Regex.IsMatch(key, "^(?:return|returnUrl)$", RegexOptions.IgnoreCase)) continue;
            bound++;
            var prop = m.Groups["p"].Value;
            foreach (Match use in Regex.Matches(code, $@"(?<![\w.]){Regex.Escape(prop)}\b"))
            {
                if (use.Index >= m.Index && use.Index < m.Index + m.Length) continue;
                if (Regex.IsMatch(code[..use.Index], @"(?:LocalRedirect\.Resolve|Uri\.EscapeDataString)\(\s*$")) continue;
                var start = code.LastIndexOf('\n', use.Index) + 1;
                var end = code.IndexOf('\n', use.Index);
                unguarded.Add(code[start..(end < 0 ? code.Length : end)].Trim());
            }
        }
        return (bound, unguarded);
    }

    [Fact]
    public void Every_query_bound_return_url_in_a_razor_page_passes_through_LocalRedirect()
    {
        var files = new[] { "libs", "apps" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, d), "*.razor",
                SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        var bound = 0;
        var pages = new List<string>();
        var bad = new List<string>();
        foreach (var f in files)
        {
            var (n, unguarded) = ReturnUrlUses(File.ReadAllText(f));
            if (n == 0) continue;
            bound += n;
            var rel = Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, f).Replace('\\', '/');
            pages.Add(rel);
            bad.AddRange(unguarded.Select(u => $"{rel}: {u}"));
        }

        // القاعِدَة ١٠: يُطبَعُ ما فُحِص، والصِفرُ عَمى. واليَومَ صَفحَتان: `StudioConsent.razor` و`Terms.razor`.
        var counted = $"فُحِصَ {files.Count} مِلَفَّ razor، فيها {bound} وِجهَةً مَربوطَةً بِالاستِعلامِ في {pages.Count} صَفحَة: {string.Join("، ", pages)}";
        Assert.True(files.Count > 100 && bound >= 2, "أَداةٌ عَمياء — " + counted);
        Assert.True(bad.Count == 0, counted + $"\nوتُطبَعُ قَبلَ القَرارِ {bad.Count}:\n  " + string.Join("\n  ", bad));
    }

    /// <summary><b>والفاحِصُ يَحمَرُّ عَلى ما كَتَبَه</b> (القاعِدَة ١٠): سَطرُ <c>StudioConsent.razor</c> كَما كانَ قَبلَ العِلاجِ حَرفاً، والحَقلُ
    /// المَخفِيُّ مَعَه، ومِفتاحٌ بِلا <c>Name</c> — كُلٌّ مِنها خَرقٌ مُسَمّى؛ والشَكلانِ المَقبولانِ لا يُعَدّان.</summary>
    [Fact]
    public void The_razor_scanner_goes_red_on_the_line_it_was_written_for()
    {
        const string before = """
            <script>location.replace('@(ReturnUrl ?? "/studio")');</script>
            <input type="hidden" name="returnUrl" value="@(ReturnUrl ?? "/studio")" />
            @code {
                [SupplyParameterFromQuery(Name = "returnUrl")] public string? ReturnUrl { get; set; }
            }
            """;
        var (bound, unguarded) = ReturnUrlUses(before);
        Assert.Equal(1, bound);
        Assert.Equal(2, unguarded.Count);
        Assert.Contains(unguarded, u => u.Contains("location.replace", StringComparison.Ordinal));

        const string unnamed = """
            <a href="@Return">back</a>
            @code { [SupplyParameterFromQuery] public string? Return { get; set; } }
            """;
        Assert.Equal((1, 1), (ReturnUrlUses(unnamed).Bound, ReturnUrlUses(unnamed).Unguarded.Count));

        const string after = """
            <script>location.replace('@Destination');</script>
            <form action="@($"/x/terms/accept?returnUrl={Uri.EscapeDataString(ReturnUrl ?? "/x")}")"></form>
            @code {
                [SupplyParameterFromQuery(Name = "returnUrl")] public string? ReturnUrl { get; set; }
                string Destination => Services.LocalRedirect.Resolve(ReturnUrl, "/studio");
            }
            """;
        Assert.Equal((1, 0), (ReturnUrlUses(after).Bound, ReturnUrlUses(after).Unguarded.Count));

        // ومِفتاحٌ آخَرُ لا يُعَدّ — `from` في صَفَحاتِ الإدارَةِ مُعجَمٌ مُغلَقٌ يُقارَنُ ولا يُطبَع.
        Assert.Equal(0, ReturnUrlUses("""[SupplyParameterFromQuery(Name = "from")] public string? From { get; set; }""").Bound);
    }
}
