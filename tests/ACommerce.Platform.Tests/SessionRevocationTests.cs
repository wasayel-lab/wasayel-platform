using System.Text;
using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>«اخرج من كل الأجهزة» — الجَلسَةُ قَبلَ رَقمِ الإصدار</b> (‏2026-10-28، قَرارُ المالِك د28 «ب»: رَقمٌ في كُلِّ حِسابٍ اسمُه «إصدارُ الجَلسَة»،
/// يَرفَعُه الزِرُّ وحَذفُ الحِساب، فَتَسقُطُ كُلُّ رُموزِ الدُخولِ القَديمَة؛ ويَشمَلُ حِساباتِ الاستوديو ومِنها مُشرِفُ المَنَصَّة). <b>تَوصيفُ «اليَوم»
/// (القاعِدَة ٣)</b> قَبلَ أَيِّ سَطرِ كود: الرَمزُ HMAC بِأَربَعَةِ حُقولٍ صالِحٌ ثَلاثينَ يَوماً و<c>ParseToken</c> يَفحَصُ التَوقيعَ وَحدَه، فَلا
/// شَيءَ في الخادِمِ يُسقِطُ رَمزاً صَدَرَ — ولا حَذفُ الحِسابِ نَفسُه؛ والخُروجُ يَمسَحُ كوكيَّ الجِهازِ وَحدَه. وصُفوفُ <c>Today_</c> تُقلَبُ بِأَسمائِها
/// في كوميتِ التَبديل.</para>
/// </summary>
public class SessionRevocationTests(ITestOutputHelper output)
{
    private const string TemplateRoot = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string[] Fields(string token) => Encoding.UTF8.GetString(Convert.FromBase64String(token)).Split('|');

    [Fact]
    public void Today_a_session_token_carries_no_version_and_lives_thirty_days()
    {
        var token = AuthHandlers.MakeToken(Guid.NewGuid(), "zz-session");
        var parsed = AuthHandlers.ParseToken(token);
        output.WriteLine($"حُقولُ الرَمز: {Fields(token).Length} · يَنتَهي بَعدَ {(parsed!.Value.ExpiresAt - DateTime.UtcNow).TotalDays:0.0} يَوماً");
        Assert.Equal(4, Fields(token).Length);
        Assert.InRange((parsed.Value.ExpiresAt - DateTime.UtcNow).TotalDays, 29.9, 30.1);
    }

    [Fact]
    public void Today_account_deletion_leaves_a_token_issued_before_it_valid()
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = "zz-session", Phone = "966500000028", FullName = "عُضو" };
        var token = AuthHandlers.MakeToken(user.Id, user.TenantSlug);
        AccountDeletion.Erase(user, DateTime.UtcNow);
        output.WriteLine($"بَعدَ الحَذف: الرَمزُ {(AuthHandlers.ParseToken(token) is null ? "ساقِط" : "صالِح")} · حَقلُ إصدارٍ في User: {typeof(User).GetProperty("SessionVersion") is not null}");
        Assert.NotNull(user.DeletedAt);
        Assert.Equal(user.Id, AuthHandlers.ParseToken(token)!.Value.UserId);
        Assert.Null(typeof(User).GetProperty("SessionVersion"));
    }

    [Fact]
    public void Today_no_door_signs_an_account_out_of_every_device()
    {
        var code = WriteEndpointGuardTests.StripComments(Read($"{TemplateRoot}/MarketplaceTemplateExtensions.cs"));
        var doors = Regex.Matches(code, @"MapPost\(""(?<r>[^""]*sessions/revoke[^""]*)""").Count;
        var logouts = Regex.Matches(code, @"MapPost\(""(?<r>/(?:\{slug\}/auth|studio)/logout)""").Count;
        output.WriteLine($"أَبوابُ «اخرج من كل الأجهزة»: {doors} · أَبوابُ الخُروجِ مِن الجِهاز: {logouts}");
        Assert.Equal(2, logouts);
        Assert.Equal(0, doors);
    }
}
