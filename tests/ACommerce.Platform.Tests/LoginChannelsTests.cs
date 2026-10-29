using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>«أَضِف بَريدَك/جَوّالَك» يَعِدُ بِالدُخولِ بِأَيِّهِما — وصَفحَةُ الدُخولِ تَرسُمُ قَناةً واحِدَة</b> (‏2026-10-29، تَدقيقُ الموجَة ١١ A2).
/// <c>Login.razor</c> يَختارُ استِمارَةً واحِدَةً مِن <c>Tenant.AuthChannel</c> (نَفاذ، أَو بَريد، أَو الجَوّالُ لِما سِواهُما)، و«حِسابي» يَعرِضُ
/// «أَضِف بَريدَك» في كُلِّ مَتجَرٍ قَناةُ بَريدِه مُسَجَّلَةٌ — ومِنها مَتاجِرُ الجَوّالِ ونَفاذ — والتَلميحُ يَقولُ «فَتَدخُلُ بَعدَها بِجَوّالِكَ أَو
/// بِبَريدِك». فَالبَريدُ المَربوطُ لا يُستَعمَلُ بِنَقرَة (القاعِدَة ١٢)، وأَبوابُ الدُخولِ بِالبَريدِ والجَوّالِ لا تَسأَلُ قَناةَ المَتجَرِ أَصلاً — بابٌ
/// خَفِيٌّ يَدخُلُ مِنه بَريدٌ مَزروعٌ مَتجَرَ جَوّال (A3). <b>تَوصيفُ «اليَوم» (القاعِدَة ٣)</b>: صُفوفُ <c>Today_</c> تُقلَبُ بِأَسمائِها في كوميتِ التَبديل.</para>
/// </summary>
public class LoginChannelsTests(ITestOutputHelper output)
{
    private const string Pages = "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages";

    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string Door(string route)
    {
        var doors = WriteEndpointGuardTests.AllMinimalApiEndpoints().Where(e => e.Route == route).ToList();
        Assert.True(doors.Count == 1, $"«{route}»: {doors.Count} باباً — الأَداةُ عَمياء.");
        return doors[0].Body;
    }

    internal static readonly string[] SignInDoors =
        ["/{slug}/auth/phone/login", "/{slug}/auth/phone/verify", "/{slug}/auth/email/login", "/{slug}/auth/email/verify"];

    [Fact]
    public void Today_the_login_page_draws_one_form_chosen_by_the_store_channel_alone()
    {
        var page = Read($"{Pages}/Login.razor");
        var forms = new[] { "<PhoneLoginForm", "<EmailLoginForm", "<NafathLoginForm" }.Select(f => Regex.Matches(page, Regex.Escape(f)).Count).ToList();
        output.WriteLine($"اليَوم: استِماراتُ الدُخولِ في الصَفحَة {string.Join(" · ", forms)} · مَفتاحُ قَناةٍ ثانِيَة {page.Contains("\"via\"", StringComparison.Ordinal)}");
        Assert.Equal(new[] { 1, 1, 1 }, forms);
        Assert.Contains("else if (Tenant.AuthChannel == \"email\")", page, StringComparison.Ordinal);
        Assert.DoesNotContain("\"via\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Today_the_account_page_offers_an_email_in_every_store_whose_email_channel_is_registered()
    {
        var page = Read($"{Pages}/Me.razor");
        Assert.Contains("offerEmail = string.IsNullOrEmpty(me.Email) && Sp.GetService(typeof(ACommerce.Kit.Auth.IEmailOtpChannel)) is not null;", page, StringComparison.Ordinal);
        Assert.Contains("offerPhone = string.IsNullOrEmpty(me.Phone) && Sp.GetService(typeof(ACommerce.Kit.Auth.IOtpChannel)) is not null;", page, StringComparison.Ordinal);
        Assert.DoesNotContain("AuthChannel", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Today_the_email_and_phone_sign_in_doors_never_read_the_store_channel()
    {
        var reading = SignInDoors.Where(r => Door(r).Contains("AuthChannel", StringComparison.Ordinal)).ToList();
        output.WriteLine($"اليَوم: أَبوابُ الدُخولِ بِالجَوّالِ والبَريد {SignInDoors.Length} · تَسأَلُ قَناةَ المَتجَر {reading.Count}");
        Assert.Empty(reading);
    }
}
