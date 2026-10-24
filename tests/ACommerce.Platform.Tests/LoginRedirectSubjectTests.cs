using System.Text.RegularExpressions;
using ACommerce.Kit.Auth.Server;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «اليَوم» — تَحويلاتُ الدُخولِ تَحمِلُ الجَوّالَ والبَريدَ والهُوِيَّةَ الوَطَنِيَّةَ في الاستِعلام (تَدقيقُ الإطلاق lens-security-13) ═══
//
// **المَقيس** (‏2026-10-24، عِندَ `ee0bf19`): أَحَدَ عَشَرَ مَوضِعاً في `MarketplaceTemplateExtensions.cs` تَبني تَحويلَ دُخولٍ بِالمَوضوعِ نَفسِه —
// ‏`login?stage=verify&phone=…` بَعدَ الإرسالِ وبَعدَ الرَمزِ الخاطِئ، و`&email=…` في أَربَعَة (والعُنوانُ غَيرُ الصالِحِ وعُطلُ الإرسالِ مَعَهُما)،
// و`&nid=…&attempt=…&code=…` لِنَفاذ في اثنَين، و`/studio/auth?stage=verify&method=…&phone|email=…` في ثَلاثَةٍ لِبابِ مُشرِفِ المَنَصَّة. فَالرَقَمُ
// والعُنوانُ والهُوِيَّةُ في سِجِلِّ كُلِّ وَكيلٍ بَينَ المُتَصَفِّحِ والخادِم، وفي تاريخِ المُتَصَفِّح، وفي كُلِّ أَداةٍ تَقرَأُ الرابِط. ولا سَبيلَ اليَومَ إلى
// تَسمِيَةِ مُحاوَلَةٍ حَيَّةٍ بِغَيرِ مَوضوعِها: `AuthHandlers` لا يُعطي مُعَرِّفَ المُحاوَلَةِ ولا يَقرَأُ المَوضوعَ مِنه.
//
// **وخارِجَ الدُخولِ نَمَطٌ مِثلُه يُقال ولا يُبتلَع**: خُطُواتُ الدَفعِ تَحمِلُ الجَوّالَ (`CheckoutPayment.cs`، `CheckoutPage.razor`) — لَيسَت
// هذِه المُلاحَظَة، فَهِيَ مُثَبَّتَةٌ بِاسمِها أَدناه ولا تَنمو.
//
// **يُثَبَّتُ قَبلَ العِلاجِ** (القاعِدَة ٣): الصَفّانِ `Today_…` يَنقَلِبانِ بِأَسمائِهِما، وصَفُّ العَيِّنَةِ المَحقونَةِ يَبقى حَرفاً.
public class LoginRedirectSubjectTests(ITestOutputHelper output)
{
    /// <summary>تَحويلٌ يَحمِلُ مَوضوعَ الدُخولِ بِاسمِه — والحَقلُ المُتَغَيِّرُ (<c>{field}</c>) في بابِ الاستوديو مِنه.</summary>
    internal const string SubjectInQuery = @"[?&](?:phone|email|nid|\{field\})=\{Uri\.EscapeDataString\(";

    /// <summary>خارِجَ الدُخول — مُثَبَّتٌ بِاسمِه وعَدَدِه، لا يَنمو بِصَمت.</summary>
    internal static readonly IReadOnlyDictionary<string, int> PinnedOutsideLogin = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["CheckoutPayment.cs"] = 1,
        ["CheckoutPage.razor"] = 1,
    };

    internal static Dictionary<string, int> SitesByFile()
    {
        var root = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs");
        var sites = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                              && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(f);
            var code = f.EndsWith(".razor", StringComparison.Ordinal)
                ? WriteEndpointGuardTests.StripMarkupComments(text)
                : WriteEndpointGuardTests.StripComments(text);
            var n = Regex.Matches(code, SubjectInQuery).Count;
            if (n > 0) sites[Path.GetFileName(f)] = n;
        }
        return sites;
    }

    // ─── الصَفُّ الدائِم: النَمَطُ يَحمَرُّ عَلى سُطورِ اليَومِ حَرفاً، ولا يَعُدُّ ما لَيسَ مَوضوعاً ─────────────────────────

    [Fact]
    public void The_subject_pattern_goes_red_on_todays_lines_and_not_on_the_attempt_or_the_role()
    {
        Assert.Matches(SubjectInQuery, "$\"login?stage=verify&phone={Uri.EscapeDataString(phone)}{asParam}\"");
        Assert.Matches(SubjectInQuery, "$\"login?err=email_invalid&email={Uri.EscapeDataString(email)}{asParam}\"");
        Assert.Matches(SubjectInQuery, "$\"login?stage=verify&nid={Uri.EscapeDataString(nid)}\" +");
        Assert.Matches(SubjectInQuery, "$\"&{field}={Uri.EscapeDataString(subject)}\";");
        Assert.DoesNotMatch(SubjectInQuery, "$\"login?stage=verify&attempt={Uri.EscapeDataString(id)}\"");
        Assert.DoesNotMatch(SubjectInQuery, "$\"&as={Uri.EscapeDataString(asRole)}\"");
        Assert.DoesNotMatch(SubjectInQuery, WriteEndpointGuardTests.StripComments("// login?stage=verify&phone={Uri.EscapeDataString(phone)}"));
    }

    // ─── ١. أَحَدَ عَشَرَ تَحويلَ دُخولٍ بِالمَوضوع ─────────────────────────────────────────────────────

    [Fact]
    public void Today_eleven_login_redirects_carry_a_phone_email_or_national_id_in_the_query()
    {
        var sites = SitesByFile();
        output.WriteLine("مَواضِعُ المَوضوعِ في الاستِعلام: " + string.Join(" · ", sites.Select(kv => $"{kv.Key}={kv.Value}")));
        Assert.Equal(11, sites.GetValueOrDefault("MarketplaceTemplateExtensions.cs"));
        foreach (var (file, n) in PinnedOutsideLogin) Assert.Equal(n, sites.GetValueOrDefault(file));
        Assert.Equal(11 + PinnedOutsideLogin.Values.Sum(), sites.Values.Sum());
    }

    // ─── ٢. لا سَبيلَ إلى المَوضوعِ مِن مُعَرِّفِ المُحاوَلَة ─────────────────────────────────────────────

    [Fact]
    public void Today_no_attempt_id_names_its_subject()
    {
        Assert.Null(typeof(AuthHandlers).GetMethod("SubjectOf"));
        Assert.Null(typeof(AuthHandlers).GetMethod("LiveAttemptOf"));
    }
}
