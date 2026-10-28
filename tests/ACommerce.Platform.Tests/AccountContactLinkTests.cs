using System.Text.RegularExpressions;
using ACommerce.Kit.Auth.Server;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>شَخصٌ واحِدٌ بِحِسابَين — دَخَلَ مَرَّةً بِالجَوّالِ ومَرَّةً بِالبَريد. قَرارُ المالِك د27 (‏2026-10-28، «ج»).</b> «لا دَمجَ تِلقائيّ. يُضافُ في
/// «حِسابي» قِسمُ «أَضِف بَريدَك»: يَدخُلُ الشَخصُ بِجَوّالِه، ثُمَّ يُثبِتُ بَريدَه بِرَمزٍ يَصِلُه، فَيُربَطُ البَريدُ بِالحِسابِ نَفسِه… وبِالمِثلِ يُضيفُ مَن
/// بَدَأَ بِالبَريدِ جَوّالَه. وإن كانَ البَريدُ مُستَعمَلاً في حِسابٍ ثانٍ يُرفَضُ الرَبطُ بِرِسالَةٍ تَشرَحُ الحَلّ… ولا يُدمَجُ شَيءٌ بِصَمت.» ومَرجِعُه
/// NIST 800-63B: لا تُضافُ وَسيلَةُ دُخولٍ إلّا مِن جَلسَةٍ مُثبَتَةٍ وبَعدَ إثباتِ مِلكِيَّتِها.</para>
///
/// <para><b>«اليَوم» قَبلَ البِناء</b> (القاعِدَة ٣): لا بابَ يَربِطُ وَسيلَةً ثانِيَةً بِحِسابٍ قائِم، وصَفحَةُ «حِسابي» لا تَعرِضُ شَيئاً مِنه، وأَنواعُ
/// المُحاوَلَةِ ثَلاثَةٌ (جَوّالٌ ونَفاذٌ وبَريدٌ لِلدُخول)، والدُخولُ بِالبَريدِ يَبحَثُ بِالبَريدِ وَحدَه ويُنشِئُ حِساباً جَديداً إن لَم يَجِد. وكُلُّ صَفٍّ
/// «Today_» يَنقَلِبُ بِاسمِه في كوميتِ البِناء.</para>
/// </summary>
public class AccountContactLinkTests(ITestOutputHelper output)
{
    internal static string Tpl(params string[] parts) => File.ReadAllText(Path.Combine(
        new[] { ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace" }.Concat(parts).ToArray()));

    internal static string AuthHandlersSource() => WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(
        ThemeZeroEquivalenceTests.RepoRoot, "libs", "kits", "Auth", "ACommerce.Kit.Auth.Server", "AuthHandlers.cs")));

    [Fact]
    public void Today_no_endpoint_links_a_second_contact_to_an_existing_account()
    {
        var mte = WriteEndpointGuardTests.StripComments(Tpl("MarketplaceTemplateExtensions.cs"));
        var me = WriteEndpointGuardTests.StripMarkupComments(Tpl("Components", "Pages", "Me.razor"));
        output.WriteLine($"me/contact في النِقاط: {Regex.Matches(mte, "/me/contact/").Count} · في «حِسابي»: {Regex.Matches(me, "me/contact").Count} · حَقلُ بَريدٍ في «حِسابي»: {me.Contains("type=\"email\"", StringComparison.Ordinal)}");
        Assert.True(mte.Length > 100_000, "أَداةٌ عَمياء.");
        Assert.DoesNotContain("/me/contact/", mte, StringComparison.Ordinal);
        Assert.DoesNotContain("me/contact", me, StringComparison.Ordinal);
        Assert.DoesNotContain("type=\"email\"", me, StringComparison.Ordinal);
    }

    [Fact]
    public void Today_the_attempt_kinds_are_phone_nafath_and_email_login_alone()
    {
        var names = Enum.GetNames<AuthHandlers.AuthKind>();
        output.WriteLine($"أَنواعُ المُحاوَلَة: {string.Join(", ", names)}");
        Assert.Equal(new[] { "PhoneOtp", "Nafath", "EmailOtp" }, names);
    }

    [Fact]
    public void Today_email_sign_in_looks_up_the_email_alone_and_creates_a_new_account_when_none_holds_it()
    {
        var src = AuthHandlersSource();
        var at = src.IndexOf("private static async Task<AuthResult> GetOrCreateUserAsync(", StringComparison.Ordinal);
        Assert.True(at > 0, "لا GetOrCreateUserAsync — الأَداةُ عَمياء.");
        var body = src[at..src.IndexOf("public static bool RaiseVerified(", at, StringComparison.Ordinal)];
        Assert.Contains("? await session.Query<User>().FirstOrDefaultAsync(u => u.Email == email)", body, StringComparison.Ordinal);
        Assert.Contains("if (existing is null)", body, StringComparison.Ordinal);
        Assert.Contains("existing = new User", body, StringComparison.Ordinal);
    }
}
