using System.Text.RegularExpressions;
using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace;
using LoginStageQuery = ACommerce.Templates.Customer.Marketplace.Services.LoginStageQuery;
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
//
// **العِلاج** (‏2026-10-24): `AuthHandlers.SubjectOf`/`LiveAttemptOf` — المُحاوَلَةُ تُسَمّى بِمُعَرِّفِها؛ و`Services.LoginStageQuery` يَبني
// مَرحَلَةَ الرَمزِ بِه (`Verify`) ويُعيدُها بَعدَ الخَطَإِ ما بَقِيَت (`Retry`)، وصَفحَتا الرَمزِ تَقرَآنِ المَوضوعَ مِنه (`SubjectFor`). والعُنوانُ لا
// يُعادُ في رابِطِ خَطَإ، ونَفاذٌ بِلا هُوِيَّةٍ في الرابِط. الصَفّانِ انقَلَبا بِأَسمائِهِما، والدائِمُ كَما أُودِع.
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

    // ─── ١. لا تَحويلَ دُخولٍ بِالمَوضوع — والمُحاوَلَةُ تُسَمّيه ─────────────────────────────────────────
    //
    // انقَلَبَ بِاسمِه (‏2026-10-24) مِن `Today_eleven_login_redirects_carry_a_phone_email_or_national_id_in_the_query`.

    [Fact]
    public void No_login_redirect_carries_a_phone_email_or_national_id_and_the_attempt_names_the_stage()
    {
        var sites = SitesByFile();
        output.WriteLine("مَواضِعُ المَوضوعِ في الاستِعلام: " + string.Join(" · ", sites.Select(kv => $"{kv.Key}={kv.Value}")));
        Assert.Equal(0, sites.GetValueOrDefault("MarketplaceTemplateExtensions.cs"));
        foreach (var (file, n) in PinnedOutsideLogin) Assert.Equal(n, sites.GetValueOrDefault(file));
        Assert.Equal(PinnedOutsideLogin.Values.Sum(), sites.Values.Sum());

        // والمَراحِلُ بِالمُحاوَلَة: أَربَعٌ في المَتجَرِ بِالمُعَرِّفِ الَّذي أَعادَه الإصدار (الجَوّالُ والبَريدُ ونَفاذٌ ورَدُّه)، واثنَتانِ في الاستوديو
        // بِالمُحاوَلَةِ الحَيَّة (إصدارُه لا يُعيدُ المُعَرِّف)، وثَلاثٌ بَعدَ الرَمزِ الخاطِئ.
        var mte = WriteEndpointGuardTests.StripComments(StudioConsentCharacterizationTests.Endpoints());
        Assert.Equal(4, Regex.Matches(mte, @"login\?stage=verify&attempt=\{Uri\.EscapeDataString\((?:attemptId|pending\.AttemptId|attempt)\)\}").Count);
        Assert.Equal(2, Regex.Matches(mte, @"Services\.LoginStageQuery\.Verify\(").Count);
        Assert.Equal(3, Regex.Matches(mte, @"Services\.LoginStageQuery\.Retry\(").Count);

        // وصَفحَتا الرَمزِ تَقرَآنِ المَوضوعَ مِن المُحاوَلَةِ لِكُلِّ قَناة.
        var login = WriteEndpointGuardTests.StripMarkupComments(StudioConsentCharacterizationTests.Source(
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "Login.razor"));
        foreach (var call in new[] { "SubjectFor(AuthKind.PhoneOtp, PhoneValue)", "SubjectFor(AuthKind.EmailOtp, EmailValue)", "SubjectFor(AuthKind.Nafath, NationalIdValue)" })
            Assert.Contains(call, login, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"=""@(?:PhoneValue|EmailValue|NationalIdValue)""", login);
        var studio = WriteEndpointGuardTests.StripMarkupComments(StudioConsentCharacterizationTests.Source(
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "StudioAuth.razor"));
        Assert.Contains("Services.LoginStageQuery.SubjectFor(Services.Incubator.StudioAuth.Tenant, Attempt,", studio, StringComparison.Ordinal);
    }

    // ─── ٢. المُحاوَلَةُ الحَيَّةُ تُسَمّي مَوضوعَها في مَتجَرِها ونَوعِها وَحدَهُما ─────────────────────────────
    //
    // انقَلَبَ بِاسمِه (‏2026-10-24) مِن `Today_no_attempt_id_names_its_subject`.

    [Fact]
    public void A_live_attempt_names_its_subject_only_in_its_own_store_and_kind()
    {
        const string tenant = "login-subject";
        var phone = $"05{Random.Shared.Next(10000000, 99999999)}";
        Assert.Null(AuthHandlers.LiveAttemptOf(tenant, phone, AuthHandlers.AuthKind.PhoneOtp));
        Assert.Equal("err=code_invalid", LoginStageQuery.Retry(tenant, phone, AuthHandlers.AuthKind.PhoneOtp, "code_invalid"));

        var id = AuthHandlers.IssueAttempt(tenant, phone, "424242", AuthHandlers.AuthKind.PhoneOtp);
        Assert.Equal(id, AuthHandlers.LiveAttemptOf(tenant, phone, AuthHandlers.AuthKind.PhoneOtp));
        Assert.Equal(phone, AuthHandlers.SubjectOf(tenant, id, AuthHandlers.AuthKind.PhoneOtp));
        Assert.Null(AuthHandlers.SubjectOf("another-store", id, AuthHandlers.AuthKind.PhoneOtp));
        Assert.Null(AuthHandlers.SubjectOf(tenant, id, AuthHandlers.AuthKind.EmailOtp));
        Assert.Null(AuthHandlers.SubjectOf(tenant, Guid.NewGuid().ToString("N"), AuthHandlers.AuthKind.PhoneOtp));
        Assert.Null(AuthHandlers.SubjectOf(tenant, null, AuthHandlers.AuthKind.PhoneOtp));

        // الرابِطُ يَحمِلُ المُعَرِّفَ لا الرَقَم، والصَفحَةُ تَقرَأُ الرَقَمَ مِنه قَبلَ ما كَتَبَه الزائِر.
        var stage = LoginStageQuery.Verify(tenant, phone, AuthHandlers.AuthKind.PhoneOtp);
        Assert.Equal($"stage=verify&attempt={id}", stage);
        Assert.DoesNotContain(phone, stage!, StringComparison.Ordinal);
        Assert.Equal(phone, LoginStageQuery.SubjectFor(tenant, id, AuthHandlers.AuthKind.PhoneOtp, "0599999999"));
        Assert.Equal("0599999999", LoginStageQuery.SubjectFor(tenant, null, AuthHandlers.AuthKind.PhoneOtp, "0599999999"));

        // خَطَأٌ يُبقي المُحاوَلَةَ فَتُعادُ مَرحَلَتُها بِخَطَئِها؛ وحينَ تُحرِقُها الأَخطاءُ تَعودُ الخُطوَةُ الأُولى.
        Assert.False(AuthHandlers.ConsumeAttempt(tenant, phone, "000000", AuthHandlers.AuthKind.PhoneOtp));
        Assert.Equal($"stage=verify&attempt={id}&err=code_invalid", LoginStageQuery.Retry(tenant, phone, AuthHandlers.AuthKind.PhoneOtp, "code_invalid"));
        for (var i = 1; i < AuthHandlers.MaxWrongGuessesPerCode; i++)
            Assert.False(AuthHandlers.ConsumeAttempt(tenant, phone, "000000", AuthHandlers.AuthKind.PhoneOtp));
        Assert.Null(AuthHandlers.SubjectOf(tenant, id, AuthHandlers.AuthKind.PhoneOtp));
        Assert.Equal("err=code_invalid", LoginStageQuery.Retry(tenant, phone, AuthHandlers.AuthKind.PhoneOtp, "code_invalid"));

        // والمُنتَهِيَةُ لا تُسَمّي شَيئاً.
        var expired = AuthHandlers.IssueAttempt(tenant, phone + "9", "424242", AuthHandlers.AuthKind.EmailOtp, TimeSpan.FromSeconds(-1));
        Assert.Null(AuthHandlers.SubjectOf(tenant, expired, AuthHandlers.AuthKind.EmailOtp));
        Assert.Null(AuthHandlers.LiveAttemptOf(tenant, phone + "9", AuthHandlers.AuthKind.EmailOtp));
    }
    // ─── ٣. الرَمزُ الخاطِئُ لِمَوضوعٍ كَتَبَه غَيرُه (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-2 الجَولَة ١، صُغرى) ─────────────────────────────
    //
    // **تَوصيفُ «اليَوم»** (القاعِدَة ٣ — يُقلَبُ بِاسمِه في كوميتِ الإصلاح): نِقاطُ الرَمزِ الثَلاثُ تُعيدُ مَرحَلَتَه بَعدَ الخَطَإِ بِـ`Retry` عَلى
    // **المَوضوعِ المَكتوبِ في الاستِمارَة** — والكاتِبُ يَختارُه. فَمَن يُرسِلُ رَمزاً خاطِئاً لِجَوّالِ غَيرِه يَعرِفُ أَيَدخُلُ صاحِبُه الآن (تَحويلٌ
    // بِـ`stage=verify&attempt=…` مُقابِلَ `err=…` وَحدَه) ويَأخُذُ مُعَرِّفَ مُحاوَلَتِه في الرابِط؛ وكانَ التَحويلُ قَبلَ `f99f79a` واحِداً في الحالَين.

    /// <summary>ما تُمَرِّرُه النِقاطُ الثَلاثُ إلى <c>Retry</c> — المَوضوعُ مِن الاستِمارَةِ لا المُحاوَلَةُ الَّتي فَتَحَت الصَفحَة.</summary>
    private static readonly string[] RetryOnTypedSubject =
    {
        "Services.LoginStageQuery.Retry(tenant.Slug, phone, AuthHandlers.AuthKind.PhoneOtp, \"code_invalid\")",
        "Services.LoginStageQuery.Retry(tenant.Slug, email, AuthHandlers.AuthKind.EmailOtp, \"code_invalid\")",
        "Services.LoginStageQuery.Retry(\n                    Services.Incubator.StudioAuth.Tenant, subject, Services.Incubator.StudioAuthDoor.Kind(method), \"code\")",
    };

    [Fact]
    public void Today_a_wrong_code_for_someone_elses_live_subject_tells_it_is_live_and_hands_out_its_attempt()
    {
        var mte = StudioConsentCharacterizationTests.Endpoints().Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var call in RetryOnTypedSubject) Assert.Contains(call, mte, StringComparison.Ordinal);

        const string tenant = "login-retry-oracle";
        var victim = $"05{Random.Shared.Next(10000000, 99999999)}";
        var idle   = $"05{Random.Shared.Next(10000000, 99999999)}";
        var id = AuthHandlers.IssueAttempt(tenant, victim, "424242", AuthHandlers.AuthKind.PhoneOtp);

        // الغَريبُ يُرسِلُ رَمزاً خاطِئاً بِجَوّالِ غَيرِه — لا مُحاوَلَةَ مَعَه — فَتُعيدُه النُقطَةُ بِما تُعيدُه `Retry` عَلى ما كَتَبَه.
        Assert.False(AuthHandlers.ConsumeAttempt(tenant, victim, "000000", AuthHandlers.AuthKind.PhoneOtp));
        var live = LoginStageQuery.Retry(tenant, victim, AuthHandlers.AuthKind.PhoneOtp, "code_invalid");
        var none = LoginStageQuery.Retry(tenant, idle, AuthHandlers.AuthKind.PhoneOtp, "code_invalid");

        Assert.Equal($"stage=verify&attempt={id}&err=code_invalid", live);
        Assert.Equal("err=code_invalid", none);
        output.WriteLine($"جَوّالٌ يَدخُلُ صاحِبُه الآن: «{live.Replace(id, "<مُعَرِّفُه>")}» · جَوّالٌ لا: «{none}» — الجَوابانِ يَختَلِفان والمُعَرِّفُ في الأَوَّل");
    }
}
