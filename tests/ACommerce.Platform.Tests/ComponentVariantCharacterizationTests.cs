using Xunit;

namespace ACommerce.Platform.Tests;

// ─── تَوصيف المُتَغايِرات قَبل تَبديلِها ────────────────────────────────
//
// المَوجَة القادِمَة تَجعَل **شَكل** ثَلاثَة مُكَوِّنات مَرئيَّة قابِلاً
// لِلتَبديل مِن مِلَفّ الثيم: بِطاقَة الدَور في البَوّابَة، وبِطاقَة
// الإعلان، وشَريط الترويسَة. والعَقد المُعلَن أَنّ **القيمَة
// الافتِراضيَّة لِكُلّ مُتَغايِر هي الشَكل الحاليّ حَرفاً** — لا
// «مُشابِهاً» ولا «مُكافِئاً بَصَرِيّاً»، بَل نَفس البايتات.
//
// ودَعوى كَهذه لا تُفحَص بَعدَ التَغيير: بَعدَه لا يَبقى طَرَف مُقارَن
// إلّا ذاكِرَتي عَمّا كانَ. لِذلك يُكتَب هذا المِلَفّ **قَبلَها**،
// ويَقرَأ لَقطَة الأَساس المُودَعَة (‏tests/characterization/appearance/‎)
// فَيُثَبِّت الحَرفِيّات الثَلاث وعَدَدَ مَرّاتِها في كُلّ صَفحَة.
//
// **ولِماذا الحَرفِيَّة بِمَسافَتِها**: صَنف بِطاقَة الإعلان اليَوم
// يُصَيَّر <c>class="ac-space "</c> — بِمَسافَة ذَيلِيَّة، لِأَنّ
// المُكَوِّن يَكتُب <c>"ac-space @(Class ?? "")"</c> والـ<c>Class</c>
// فارِغ. المَسافَة تِلكَ لَيسَت تَفصيلاً تَجميلِيّاً: أَيّ تَنفيذ
// لِلمُتَغايِرات يُعيد تَركيب السَطر ويَنسى المَسافَة **يَكسِر
// المُقارَنَة بايتاً بِبايت** في سِتّ صَفَحات دُفعَةً واحِدَة. أَن
// تُكتَب هُنا صَراحَةً أَرخَص مِن أَن تُكتَشَف هُناك.
//
// وهذا نَظير <c>RoleCatalogCharacterizationTests</c> حَرفاً: تُوصَف
// الحالَة القائِمَة أَوَّلاً، ثُمَّ تُبَدَّل الآلِيَّة تَحتَها والتَوصيف
// لا يَتَحَرَّك.

public class ComponentVariantCharacterizationTests
{
    // ─── الحَرفِيّات الثَلاث ─────────────────────────────────────────
    //
    // تُقرَأ مِن اللَقطَة لا مِن الشيفرَة، وتُصَدَّر <c>internal</c>
    // لِيَربِطَها اختِبار المَعجَم بِأَصناف الكاتالوج حينَ يُوجَد —
    // فَيَبقى طَرَفا الرِباط نَصّاً واحِداً لا نَصَّين يَنحَرِفان.

    /// <summary>حاوِيَة بِطاقات الأَدوار في بَوّابَة المَتجَر.</summary>
    internal const string PortalRoleCardsBaseClass = "acm-role-landing-cards";
    internal const string PortalRoleCardsMarkup    = "<div class=\"acm-role-landing-cards\">";

    /// <summary>بِطاقَة الإعلان — <b>بِمَسافَتِها الذَيلِيَّة</b>.</summary>
    internal const string ListingCardBaseClass = "ac-space";
    internal const string ListingCardMarkup    = "<article class=\"ac-space \">";

    /// <summary>غِلاف شَريط الترويسَة العُلويّ.</summary>
    internal const string HeaderBarBaseClass = "acm-v2-topnav-wrap";
    internal const string HeaderBarMarkup    = "<div class=\"acm-v2-topnav-wrap\">";

    /// <summary>عَدَد مَرّات كُلّ حَرفِيَّة في كُلّ صَفحَة مَرجِعِيَّة —
    /// <b>مَعدود مِن اللَقطَة لا مُقَدَّر</b>. الصِفر هُنا دَعوى كَغَيرِه:
    /// بَوّابَة المَتجَر لا شَريط ترويسَة فيها ولا بِطاقَة إعلان،
    /// وصَفحَة الاستِكشاف لا بِطاقات أَدوار فيها.</summary>
    public static readonly TheoryData<string, int, int, int> ExpectedCounts = new()
    {
        //  الصَفحَة                        بِطاقات الأَدوار، بِطاقَة الإعلان، الترويسَة
        { "adwar-demo-portal.html",     1,  0, 0 },
        { "ashare-portal.html",         1,  0, 0 },
        { "ashare-role-customer.html",  1,  0, 1 },
        { "ashare-role-host.html",      1,  0, 1 },
        { "ashare-role-vendor.html",    1,  0, 1 },
        { "ashare-explore.html",        0, 17, 1 },
    };

    [Theory]
    [MemberData(nameof(ExpectedCounts))]
    public void TheThreeComponents_RenderExactlyTheseBytesToday(
        string page, int roleCards, int listingCards, int headerBars)
    {
        var html = ReadBaselinePage(page);

        Assert.Equal(roleCards,    Occurrences(html, PortalRoleCardsMarkup));
        Assert.Equal(listingCards, Occurrences(html, ListingCardMarkup));
        Assert.Equal(headerBars,   Occurrences(html, HeaderBarMarkup));
    }

    [Fact]
    public void EveryOccurrenceOfEachBaseClass_IsAccountedForByTheLiteral()
    {
        // الفَحص المُكَمِّل: لَيسَ فَقَط «الحَرفِيَّة تَظهَر ن مَرَّة»، بَل
        // **لا ظُهور آخَر لِلصَنف الأَساس بِشَكل مُغايِر**. بِدونِه كانَ
        // يُمكِن أَن يَمُرّ سَطر يَحمِل الصَنف نَفسَه بِصِفَة زائِدَة
        // فَيُظَنّ الشَكل واحِداً وهو اثنان.
        foreach (var (page, _, _, _) in Rows())
        {
            var html = ReadBaselinePage(page);

            Assert.Equal(Occurrences(html, PortalRoleCardsMarkup),
                         Occurrences(html, $"class=\"{PortalRoleCardsBaseClass}\""));
            Assert.Equal(Occurrences(html, ListingCardMarkup),
                         Occurrences(html, $"class=\"{ListingCardBaseClass} \""));
            Assert.Equal(Occurrences(html, HeaderBarMarkup),
                         Occurrences(html, $"class=\"{HeaderBarBaseClass}\""));
        }
    }

    [Fact]
    public void NoModifierClassExistsYet_NotInTheMarkupAndNotInTheStyleSheets()
    {
        // النَّفي جُزء مِن التَوصيف: «قَبل» تَعني أَنَّه لا يُوجَد اليَوم
        // أَيّ صَنف مُعَدِّل عَلى هذه الأُسُس الثَلاثَة — لا في HTML ولا
        // في أَيّ وَرَقَة أَنماط. فَكُلّ صَنف مِن هذا الشَكل يَظهَر
        // لاحِقاً هو **إضافَة هذه المَوجَة**، مَنسوبَة إلَيها بِبُرهان لا
        // بِذاكِرَة.
        var bases = new[] { PortalRoleCardsBaseClass, ListingCardBaseClass, HeaderBarBaseClass };

        foreach (var file in Directory.GetFiles(BaselineDir, "*.html")
                     .Concat(Directory.GetFiles(Path.Combine(BaselineDir, "css"), "*.css")))
        {
            var text = File.ReadAllText(file);
            foreach (var b in bases)
                Assert.DoesNotContain($"{b}--", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheBaselineItselfIsPresent_OrTheCharacterizationProvesNothing()
    {
        // أَداة تَقرَأ مِلَفّاً غائِباً تُجيب «صِفر» وتَمُرّ خَضراء. تُفحَص
        // اللَقطَة أَوَّلاً كَي لا يَتَحَوَّل غِيابُها إلى نَجاح صامِت.
        foreach (var (page, _, _, _) in Rows())
            Assert.True(File.Exists(Path.Combine(BaselineDir, page)),
                $"لَقطَة الأَساس مَفقودَة: {page} — التَوصيف بِلا طَرَف مُقارَن.");
    }

    // ─── الأَداة ─────────────────────────────────────────────────────

    private static string BaselineDir => Path.Combine(
        ThemeZeroEquivalenceTests.RepoRoot,
        "tests", "characterization", "appearance", "baseline");

    private static string ReadBaselinePage(string page) =>
        File.ReadAllText(Path.Combine(BaselineDir, page));

    private static IEnumerable<(string Page, int A, int B, int C)> Rows() =>
        ExpectedCounts.Select(r => (
            (string)r[0]!, (int)r[1]!, (int)r[2]!, (int)r[3]!));

    private static int Occurrences(string haystack, string needle)
    {
        var n = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }
}
