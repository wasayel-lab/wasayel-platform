using System.Security.Cryptography;
using System.Text;
using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَنجو قَبلَ أَن تُحصَرَ بِطاقَةُ رَفضِ مُضيفِ الإدارَةِ في مُضيفِ الإدارَة (‏2026-11-11، الإغلاقُ الأَخيرُ لِـW13-4 · W13-4·C2f·4 — C2z-PATHS-R3-03) ═══
//
// `187e832` يَرسُمُ البِطاقَةَ لِكُلِّ `?err=not_store_admin` — في وَضعِ المَسارِ وعَلى مُضيفِ المَتجَرِ ومُضيفِ دَورٍ آخَر (مَقيسٌ حَيّاً عِندَ ad0a770: البِطاقَةُ
// ولا استِمارَة عَلى /ashare/login و/ejar/login وashare.localhost/login وashare-customer.localhost/login). وما لا يَتَغَيَّرُ بِحَصرِها — ويَبقى هذا المِلَفُّ بِبَصمَتِه:
// (١) مَن يَصدُرُ لَه الرَفضُ (`AdminHostSignIn.Refusal`) ومَخرَجُه (`StoreHome`) في كُلِّ شَكلِ مُضيف؛ (٢) جِسمُ البِطاقَةِ في `Login.razor` بِبايتاتِه
// (البَصمَةُ أَدناه) — يَتَغَيَّرُ شَرطُها وَحدَه.

public class AdminHostRefusalCardCharacterizationTests
{
    private const string Apex = "example.test";

    private static HostView View(string? hostSlug, string? hostRole, string port = "")
        => new(hostSlug is null ? null : Apex, hostSlug, "https", port) { HostRole = hostRole };

    [Theory]
    [InlineData("ejar", "tenant_admin", "", "/login?err=not_store_admin")]
    [InlineData("ejar", "tenant_admin", "customer", "/login?err=not_store_admin")]
    [InlineData("ejar", "tenant_admin", "tenant_admin", null)]
    [InlineData("ejar", null, "", null)]
    [InlineData("ejar", "vendor", "", null)]
    [InlineData("ejar", "customer", "", null)]
    [InlineData("ashare", "tenant_admin", "", null)]
    [InlineData(null, null, "", null)]
    public void The_refusal_and_whom_it_names_stay(string? hostSlug, string? hostRole, string held, string? expected)
        => Assert.Equal(expected, AdminHostSignIn.Refusal(View(hostSlug, hostRole), "ejar", held));

    [Theory]
    [InlineData("ejar", "tenant_admin", "", "https://ejar.example.test/")]
    [InlineData("ejar", "tenant_admin", ":5050", "https://ejar.example.test:5050/")]
    [InlineData("ejar", "vendor", "", "https://ejar.example.test/")]
    [InlineData("ejar", null, "", "/")]
    public void The_way_out_stays(string hostSlug, string? hostRole, string port, string expected)
        => Assert.Equal(expected, AdminHostSignIn.StoreHome(View(hostSlug, hostRole, port), "ejar"));

    /// <summary>جِسمُ البِطاقَةِ بَينَ قَوسَي فَرعِها — ما بَعدَ سَطرِ الشَرطِ إلى ما قَبلَ <c>else</c> الاستِمارَة.</summary>
    [Fact]
    public void The_card_body_in_the_login_page_stays_byte_for_byte()
    {
        var s = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/Login.razor"));
        var cond = s.IndexOf("Err == Services.Ux.AdminHostSignIn.NotStoreAdmin", StringComparison.Ordinal);
        Assert.True(cond > 0);
        var body = s.IndexOf("\n{\n", cond, StringComparison.Ordinal) + 3;
        var end = s.IndexOf("\n}\nelse\n{\n", body, StringComparison.Ordinal);
        var card = s[body..(end + 1)];
        Assert.Equal(18, card.Split('\n').Length);
        Assert.Equal("3b5a545ad992f91c48eabaf036fa4fc201eeca5e7473dc48b401ba24feef0ab7",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(card))));
    }
}
