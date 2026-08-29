using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>الوَثيقَةُ الَّتي لا تُبلَغُ بِالنَقرِ غَيرُ مَوجودَة</b>
/// (القاعِدَة ١٢). وشَرطُ اعتِمادِ النِطاقِ عِندَ بَوّابَةِ الدَفعِ
/// يَنُصُّ عَلى ذلِكَ حَرفاً: الشُروطُ وسِياسَتا الاستِردادِ
/// والخُصوصِيَّةِ <b>يَبلُغُها الزائِرُ مِن قائِمَةِ المَوقِع</b> لا
/// بِرابِطٍ مُباشِرٍ يُرسَلُ إلَيه.</para>
///
/// <para><b>ولِذلِكَ يُفحَصُ الطَرَفانِ لا طَرَفٌ واحِد</b>: أَنّ
/// الصَفحَةَ مَوجودَةٌ بِمَسارِها، وأَنّ في الشِلِّ رابِطاً يَفتَحُها.
/// وصَفحَةٌ خَضراءُ الطَرَفِ الأَوَّلِ وَحدَه هي بِالضَبطِ ما
/// وُصِفَ في القاعِدَة: كودٌ مَيِّتٌ بِكامِلِ كِلفَتِه وبِلا
/// أَثَر.</para>
///
/// <para><b>وعَدّادُ ما فُحِصَ يُطبَع</b> (القاعِدَة ١٠): فَحصٌ
/// يَقرَأُ صِفرَ مِلَفٍّ يُعطي «صِفرَ مُخالَفَة» كَما يُعطيها فَحصٌ
/// قَرَأَ كُلَّ شَيء.</para>
/// </summary>
public class PlatformDocumentReachabilityTests(ITestOutputHelper output)
{
    private static string Root => ThemeZeroEquivalenceTests.RepoRoot;

    private const string TemplateRoot = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static readonly string[] PlatformPaths =
        { "/terms", "/privacy", "/refunds", "/pricing", "/contact" };

    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));

    // ─── الطَرَفُ الأَوَّل: الصَفحَةُ مَوجودَةٌ بِمَسارِها ─────────────

    /// <summary>كُلُّ مَسارٍ مِن الخَمسَةِ لَه تَوجيهُ
    /// <c>@page</c> في مِلَفِّ Razor واحِدٍ عَلى الأَقَلّ.</summary>
    [Fact]
    public void Every_platform_document_has_a_page_route()
    {
        var pagesDir = Path.Combine(Root, TemplateRoot.Replace('/', Path.DirectorySeparatorChar),
                                    "Components", "Pages");
        var razor = Directory.GetFiles(pagesDir, "*.razor", SearchOption.AllDirectories);
        Assert.True(razor.Length > 0, "صِفرُ مِلَفِّ Razor — الفاحِصُ أَعمى (القاعِدَة ١٠).");

        var routes = razor
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"^@page\s+""([^""]+)""",
                                           RegexOptions.Multiline)
                                  .Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        output.WriteLine($"فُحِصَ {razor.Length} مِلَفَّ Razor، وفيها {routes.Count} مَساراً.");

        var missing = PlatformPaths.Where(p => !routes.Contains(p)).ToArray();
        Assert.True(missing.Length == 0,
            $"مَساراتٌ بِلا صَفحَة: {string.Join("، ", missing)}");
    }

    /// <summary>والوَثائِقُ القانونِيَّةُ الثَلاثُ لَها فَرعٌ
    /// إنجليزيٌّ بِمَسارِه — <b>وهو شَرطُ مُراجَعَة</b>: مُراجِعُ
    /// البَوّابَةِ لا يَقرَأُ العَرَبِيَّة.</summary>
    [Theory]
    [InlineData("Platform/PlatformTerms.razor", "/terms")]
    [InlineData("Platform/PlatformPrivacy.razor", "/privacy")]
    [InlineData("Platform/PlatformRefunds.razor", "/refunds")]
    public void Every_legal_document_carries_an_english_route(string file, string path)
    {
        var text = Read($"{TemplateRoot}/Components/Pages/{file}");
        Assert.Contains($"@page \"{path}\"", text, StringComparison.Ordinal);
        Assert.Contains($"@page \"{path}/{{Lang}}\"", text, StringComparison.Ordinal);
    }

    // ─── الطَرَفُ الثاني: الشِلُّ يَفتَحُها ─────────────────────────

    /// <summary>التَذييلُ يَحمِلُ الخَمسَةَ كُلَّها.</summary>
    [Fact]
    public void The_footer_links_to_all_five_platform_documents()
    {
        var footer = Read($"{TemplateRoot}/Components/PlatformFooter.razor");
        var hrefs = Regex.Matches(footer, @"href=""(/[a-z]+)""")
                         .Select(m => m.Groups[1].Value)
                         .ToHashSet(StringComparer.Ordinal);

        output.WriteLine($"رَوابِطُ التَذييل: {string.Join("، ", hrefs.OrderBy(h => h, StringComparer.Ordinal))}");
        Assert.True(hrefs.Count > 0, "صِفرُ رابِطٍ في التَذييل — الفاحِصُ أَعمى.");

        var missing = PlatformPaths.Where(p => !hrefs.Contains(p)).ToArray();
        Assert.True(missing.Length == 0,
            $"وَثائِقُ لا يَفتَحُها التَذييل: {string.Join("، ", missing)}");
    }

    /// <summary>
    /// <para><b>والتَذييلُ نَفسُه يُصَيَّرُ في كُلِّ فَرعٍ مِن
    /// فُروعِ الشِلِّ الثَلاثَة</b> — المَتجَرُ المُعَلَّق،
    /// والمَتجَرُ العامِل، وصَفَحاتُ المَنَصَّةِ بِلا مُستَأجِر.
    /// وفَرعٌ واحِدٌ يُنسى يَعني ثُلثَ المَوقِعِ بِلا قائِمَة، وهو
    /// عَينُ ما لا يَراهُ اختِبارُ «الرابِطُ مَوجودٌ في
    /// المُكَوِّن».</para>
    /// </summary>
    [Fact]
    public void The_shell_renders_the_footer_in_each_of_its_three_branches()
    {
        var layout = Read($"{TemplateRoot}/Components/Layout/MainLayout.razor");
        var count = Regex.Matches(layout, @"<PlatformFooter\s*/>").Count;

        output.WriteLine($"مَواضِعُ التَذييلِ في الشِلّ: {count}");
        Assert.Equal(3, count);
    }

    // ─── اللُغَةُ مِن المَسارِ لا مِن الكوكي ────────────────────────

    [Theory]
    [InlineData(null, "ar", true, "rtl")]
    [InlineData("en", "en", true, "ltr")]
    [InlineData("EN", "en", true, "ltr")]
    public void A_recognised_route_language_resolves(string? route, string lang, bool ok, string dir)
    {
        var doc = PlatformDocLanguage.FromRoute(route);
        Assert.Equal(lang, doc.Lang);
        Assert.Equal(ok, doc.IsRecognised);
        Assert.Equal(dir, doc.Dir);
    }

    /// <summary>ولُغَةٌ لا نَعرِفُها لا تُصَيِّرُ نُسخَةً ثانِيَةً
    /// بِعُنوانٍ آخَر — فَلا يَفتَحُ المَسارُ فَضاءَ عَناوينَ لا
    /// نِهائِيّاً بِنَفسِ المُحتَوى.</summary>
    [Theory]
    [InlineData("zz")]
    [InlineData("fr")]
    [InlineData("ar")]
    public void An_unknown_route_language_is_refused(string route)
        => Assert.False(PlatformDocLanguage.FromRoute(route).IsRecognised);

    // ─── النَصُّ مِن القامُوسِ لا مِن الكود ─────────────────────────

    /// <summary>
    /// <para><b>كُلُّ مِفتاحٍ تَقرَؤُه هذِه الصَفَحاتُ مَوجودٌ في
    /// المَعجَمِ العَرَبيّ.</b> ومِفتاحٌ ناقِصٌ لا يَرمي ولا يُلَوَّن
    /// — يُطبَعُ خاماً عَلى الشاشَة، وهو عَطَبٌ مَرئيٌّ يَتَنَكَّرُ
    /// في هَيئَةِ تَرجُمَة.</para>
    /// </summary>
    [Fact]
    public void Every_key_these_pages_read_exists_in_the_arabic_lexicon()
    {
        var files = new[]
        {
            $"{TemplateRoot}/Components/PlatformFooter.razor",
            $"{TemplateRoot}/Components/Pages/Platform/PlatformTerms.razor",
            $"{TemplateRoot}/Components/Pages/Platform/PlatformPrivacy.razor",
            $"{TemplateRoot}/Components/Pages/Platform/PlatformRefunds.razor",
            $"{TemplateRoot}/Components/Pages/Platform/PlatformPricing.razor",
            $"{TemplateRoot}/Components/Pages/Platform/PlatformContact.razor",
        };

        var lexicon = LocaleCatalog.Lexicon.ToHashSet(StringComparer.Ordinal);
        var keys = files
            .SelectMany(f => Regex.Matches(Read(f), @"(?:Markup|doc|L)\(?\[?""(platform\.[a-z0-9_.]+)""")
                                  .Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        output.WriteLine($"فُحِصَ {keys.Count} مِفتاحاً مِن {files.Length} مِلَفّ، مُقابِلَ مَعجَمٍ فيه {lexicon.Count}.");
        Assert.True(keys.Count > 0, "صِفرُ مِفتاحٍ استُخرِج — الفاحِصُ أَعمى (القاعِدَة ١٠).");

        var missing = keys.Where(k => !lexicon.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.True(missing.Length == 0,
            $"مَفاتيحُ خارِجَ المَعجَم: {string.Join("، ", missing)}");
    }

    /// <summary>
    /// <para><b>والوَثائِقُ الثَلاثُ لَها إنجليزِيَّةٌ فِعلاً، لا
    /// سُقوطٌ صامِتٌ إلى العَرَبِيَّة.</b> ‏<c>LocaleCatalog.Text</c>
    /// يَسقُطُ إلى العَرَبِيَّةِ عِندَ نَقصِ المِفتاح — وهو سُلوكٌ
    /// صَحيحٌ عامَّةً، <b>وقاتِلٌ هُنا</b>: صَفحَةٌ عُنوانُها
    /// إنجليزيٌّ وجِسمُها عَرَبيٌّ تُطيلُ المُراجَعَةَ أَو
    /// تُفشِلُها. فَيُفحَصُ الوُجودُ بِـ<c>Find</c> لا
    /// بِـ<c>Text</c>.</para>
    /// </summary>
    [Fact]
    public void The_three_legal_documents_are_actually_translated()
    {
        var prefixes = new[] { "platform.terms.", "platform.privacy.", "platform.refunds.", "platform.doc." };

        var keys = LocaleCatalog.Lexicon
            .Where(k => prefixes.Any(p => k.StartsWith(p, StringComparison.Ordinal)))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        output.WriteLine($"فُحِصَ {keys.Length} مِفتاحاً قانونِيّاً.");
        Assert.True(keys.Length > 0, "صِفرُ مِفتاحٍ قانونيّ — الفاحِصُ أَعمى (القاعِدَة ١٠).");

        // مَفاتيحُ لا تُقرَأُ في الصَفحَةِ الإنجليزِيَّةِ أَصلاً: اسمُ
        // النُسخَتَين. قيمَتُهُما مَقصودَةٌ واحِدَةً في اللُغَتَين.
        var bilingualByDesign = new[] { "platform.doc.lang_ar", "platform.doc.lang_en" };

        var untranslated = keys
            .Where(k => !bilingualByDesign.Contains(k, StringComparer.Ordinal))
            .Where(k => LocaleCatalog.Find(PlatformDocLanguage.English, k) is null)
            .ToArray();

        Assert.True(untranslated.Length == 0,
            $"مَفاتيحُ قانونِيَّةٌ بِلا إنجليزِيَّة (‏{untranslated.Length}): " +
            string.Join("، ", untranslated));
    }

    /// <summary>
    /// <para><b>ضَمانُ الثَلاثينَ يَوماً مَنصوصٌ عَلَيه صَراحَةً —
    /// بِرَقمِه — في اللُغَتَين.</b> وهُوَ شَرطُ اعتِمادٍ مَكتوبٌ في
    /// مَركَزِ مُساعَدَةِ البَوّابَة، <b>والْتِزامٌ مالِيٌّ حَقيقيّ</b>.
    /// وحَذفُه أَو تَمييعُه يُفشِلُ المُراجَعَةَ بِصَمت، فَيُثَبَّتُ
    /// هُنا لا يُترَكُ لِتَحريرٍ عابِر.</para>
    /// </summary>
    [Theory]
    [InlineData("ar", "30")]
    [InlineData("en", "30")]
    public void The_refund_guarantee_states_thirty_days(string lang, string number)
    {
        var body = LocaleCatalog.Find(lang, "platform.refunds.guarantee_body");
        Assert.NotNull(body);
        Assert.Contains(number, body!, StringComparison.Ordinal);
    }
}
