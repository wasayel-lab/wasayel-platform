using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مَداخِلُ رائِدِ الأَعمالِ تَقولُ ما يوجَد — بِلا ذَكاءٍ اصطِناعيٍّ ولا دَعوى غَيرِ مَقيسَة (‏2026-11-07، الموجَة ١٣ W13-1·٤ — ADR-049 §٨) ═══
//
// **المَقيسُ قَبل**: عُنوانُ الصَفحَةِ الأُولى «مَدعوم بِالذَّكاء الاصطِناعيّ + بيانات السوق السعودي»، ووَصفُها «دِراسَة جَدوى احترافيَّة»، وبِطاقَةٌ تَعِدُ
// بِـ«أَربَعَة أَنماط جاهِزَة تُطلِق متجَرَكَ في ١-٣ أَيّام» (نَمَطٌ واحِدٌ يُبلَغ)، و«في أَيّام، لا شُهور» — ولا دَعوَةَ إلّا استِمارَةَ تَحليلٍ لا تُرسَمُ بِلا مِفتاح.
// **وما يُثَبَّت**: (١) لا نَصَّ قامُوسٍ تَرسُمُه الصَفحَةُ الأُولى أَو قِشرَةُ الاستوديو أَو استِمارَةُ الإنشاءِ فيه عِبارَةٌ مَحظورَةٌ في البَحث، مُقارَنَةً بِلا
// تَشكيل؛ (٢) الدَعوَةُ رابِطٌ إلى `/studio/new` خارِجَ كُلِّ باب؛ (٣) وِجهَةُ ما بَعدَ الدُخولِ لِمَن لا مَتجَرَ لَه هي الاستِمارَة.
// و`StudioHome` يَدخُلُ المَسحَ في W13-1·٥ حينَ يَخرُجُ قِسمُ الأَفكارِ ونُصوصُه.

public class StudioEntryCopyTests(ITestOutputHelper output)
{
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Read(string rel) => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, rel));

    /// <summary>الحَرَكاتُ والتَطويلُ يَسقُطان، وصُوَرُ الأَلِفِ تَتَوَحَّد — فَـ«مَدعوم بِالذَّكاء» و«مدعوم بالذكاء» واحِد.</summary>
    internal static string Bare(string s)
    {
        var t = Regex.Replace(s, "[ً-ٰٟـ]", "");
        return Regex.Replace(t, "[آأإ]", "ا");
    }

    /// <summary>العِباراتُ المَحظورَةُ في البَحثِ والقَرار (decisions §8) — ومَعَها ما لا يوجَدُ عِندَ الإطلاق.</summary>
    internal static readonly string[] Banned =
    [
        "الذكاء الاصطناعي", "ذكاء اصطناعي", "دراسة جدوى", "اربعة انماط", "في ايام لا شهور", "في ايام، لا شهور",
        "المطبخ الذكي", "الروبوت الطباخ", "بضغطة زر", "الافضل",
    ];

    private static readonly Regex KeyUse = new(@"L(?:\.Markup|\.Format)?\s*[\[(]\s*""(?<k>[a-z_]+(?:\.[a-z0-9_]+)+)""", RegexOptions.Compiled);

    /// <summary>كُلُّ مِفتاحِ قامُوسٍ تُسَمّيه صَفحَةٌ حَرفِيّاً (<c>L.Markup("…")</c>، <c>L["…"]</c>، <c>L.Format("…"</c>) خارِجَ التَعليقات.</summary>
    internal static IReadOnlyList<string> KeysDrawnBy(string page)
        => KeyUse.Matches(WriteEndpointGuardTests.StripMarkupComments(page)).Select(m => m.Groups["k"].Value).Distinct().ToList();

    [Theory]
    [InlineData("Components/Pages/Landing.razor")]
    [InlineData("Components/StudioShell.razor")]
    [InlineData("Components/Pages/StudioNewStore.razor")]
    public void No_text_an_entry_page_draws_carries_a_banned_or_unmeasured_claim(string page)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(Read("libs/core/ACommerce.Platform.I18n/Locales/ar.json"));
        var keys = KeysDrawnBy(Read($"{Template}/{page}"));
        var hits = new List<string>();
        foreach (var k in keys)
        {
            if (!doc.RootElement.TryGetProperty(k, out var v)) continue;
            var bare = Bare(v.GetString() ?? "");
            hits.AddRange(Banned.Where(b => bare.Contains(Bare(b), StringComparison.Ordinal)).Select(b => $"{k} ⇐ «{b}»"));
        }
        output.WriteLine($"{page}: مَفاتيحُ مَفحوصَة {keys.Count} · عِباراتٌ مَحظورَة {Banned.Length} · إصابات {hits.Count}");
        Assert.True(keys.Count >= 5, $"أَداةٌ عَمياء: {keys.Count} مِفتاحاً في {page}.");
        Assert.True(hits.Count == 0, string.Join("، ", hits));
    }

    /// <summary>والماسِحُ يُقاسُ: نَصٌّ بِتَشكيلٍ كامِلٍ وأَلِفٍ مَهموزَةٍ يُصابُ كَما يُصابُ عارِياً، ونَصٌّ قَريبٌ لا يُصاب.</summary>
    [Theory]
    [InlineData("مَدعوم بِالذَّكاء الاصطِناعيّ + بيانات السوق", true)]
    [InlineData("واحصُل على دِراسَة جَدوى احترافيَّة", true)]
    [InlineData("أَربَعَة أَنماط جاهِزَة", true)]
    [InlineData("حَوِّل فِكرَتَكَ إلى تَطبيق في أَيّام، لا شُهور", true)]
    [InlineData("هذا هو الأَفضَل لِمَتجَرِك", true)]
    [InlineData("اجمَع بائِعيكَ في مَتجَرٍ واحِد", false)]
    [InlineData("الدَفعُ والفَوتَرَةُ داخِلَه", false)]
    public void The_banned_phrase_matcher_ignores_diacritics_and_alef_forms(string text, bool hit)
        => Assert.Equal(hit, Banned.Any(b => Bare(text).Contains(Bare(b), StringComparison.Ordinal)));

    /// <summary>الدَعوَةُ في الصَفحَةِ الأُولى رابِطٌ إلى الاستِمارَةِ بِمِفتاحِها، خارِجَ فَرعِ «مُسَجَّل/زائِر» وخارِجَ كُلِّ باب — فَلِلاثنَينِ مَعاً.</summary>
    [Fact]
    public void The_landing_call_to_action_leads_to_the_create_page_for_visitor_and_owner_alike()
    {
        var page = WriteEndpointGuardTests.StripMarkupComments(Read($"{Template}/Components/Pages/Landing.razor"));
        const string cta = "<a class=\"st-btn st-btn-primary st-btn-lg\" href=\"/studio/new\">@L.Markup(\"landing.cta.create\")</a>";
        var at = page.IndexOf(cta, StringComparison.Ordinal);
        Assert.True(at > 0, "لا دَعوَةَ إلى الاستِمارَة في الصَفحَةِ الأُولى.");
        Assert.Equal(at, page.LastIndexOf(cta, StringComparison.Ordinal));
        var hero = page.IndexOf("<section class=\"landing-hero\">", StringComparison.Ordinal);
        Assert.True(hero > 0 && hero < at, "الدَعوَةُ خارِجَ رَأسِ الصَفحَة.");
        Assert.Empty(StudioStoreCreateTests.CreatePageLinksIn([("Landing.razor", page)]).Where(l => l.InsideDoor));
        var auth = page.IndexOf("@if (StudioAuthSvc.IsAuthenticated)", StringComparison.Ordinal);
        var authEnd = page.IndexOf("</nav>", auth, StringComparison.Ordinal);
        Assert.False(at > auth && at < authEnd, "الدَعوَةُ داخِلَ فَرعِ المُسَجَّلِ أَو الزائِر.");
    }

    /// <summary>وِجهَةُ ما بَعدَ الدُخول — موجَبٌ وسالِب: لا مَتجَرَ ⇒ الاستِمارَة؛ مَتجَرٌ فَأَكثَر ⇒ اللَوحَة.</summary>
    [Theory]
    [InlineData(0, "/studio/new")]
    [InlineData(1, "/studio")]
    [InlineData(4, "/studio")]
    public void After_sign_in_an_account_without_a_store_lands_on_the_create_form(int owned, string expected)
        => Assert.Equal(expected, StudioStoreCreate.AfterSignIn(owned));

    /// <summary>وبابُ الدُخولِ يَسأَلُها بِعَدَدِ ما يَملِكُه صاحِبُ الجَلسَةِ فِعلاً — لا بِثابِت.</summary>
    [Fact]
    public void The_sign_in_door_asks_the_destination_with_the_owned_store_count()
    {
        var verify = WriteEndpointGuardTests.AllMinimalApiEndpoints().Single(e => e.Route == "/studio/auth/verify").Body;
        Assert.Contains("StudioStoreCreate.AfterSignIn((await tenants.ListOwnedByAsync(user.Id)).Count)", verify, StringComparison.Ordinal);
        Assert.DoesNotContain("Results.Redirect(\"/studio\")", verify, StringComparison.Ordinal);
    }
}
