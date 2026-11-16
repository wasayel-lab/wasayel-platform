using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ بِطاقَةُ رَفضِ مُضيفِ الإدارَةِ عَلى مُضيفِ الإدارَةِ وَحدَه، وما سِواه الاستِمارَة (‏2026-11-11، الإغلاقُ الأَخيرُ لِـW13-4 · W13-4·C2f·4 — C2z-PATHS-R3-03) ═══
//
// **المَقيسُ قَبل** (عِندَ ad0a770): `?err=not_store_admin` يَرسُمُ «هذا العُنوانُ لِإدارَةِ المَتجَرِ وَحدَها…» بِلا استِمارَةٍ عَلى `/ashare/login` بِلا نِطاقٍ
// وعَلى مُضيفِ المَتجَرِ ومُضيفِ دَورٍ آخَر — والرَمزُ لا يَصدُرُ إلّا عَلى `{slug}-admin.R`. **والآن** الشَرطُ شَرطُ `Refusal` بِحَرفِه (`Applies`)، وما سِواه
// الاستِمارَةُ كَما بِلا رَمز (`ErrorMessage` لا يَعرِفُ الرَمزَ فَلا تَنبيه). وما لا يَتَغَيَّرُ يُثَبِّتُه `AdminHostRefusalCardCharacterizationTests`.

public class AdminHostRefusalCardTests
{
    private static HostView View(string? hostSlug, string? hostRole)
        => new(hostSlug is null ? null : "example.test", hostSlug, "https", "") { HostRole = hostRole };

    [Theory]
    [InlineData("ejar", "tenant_admin", true)]
    [InlineData("ejar", null, false)]          // مُضيفُ المَتجَر
    [InlineData("ejar", "customer", false)]    // مُضيفُ دَورٍ آخَر
    [InlineData("ejar", "vendor", false)]
    [InlineData("ashare", "tenant_admin", false)] // مُضيفُ إدارَةِ مَتجَرٍ آخَر
    [InlineData(null, null, false)]            // وَضعُ المَسار
    public void The_card_applies_on_this_stores_admin_host_alone(string? hostSlug, string? hostRole, bool expected)
    {
        Assert.Equal(expected, AdminHostSignIn.Applies(View(hostSlug, hostRole), "ejar"));
        // وهُوَ شَرطُ الرَفضِ نَفسُه: حَيثُ لا يَنطَبِقُ لا يَصدُرُ الرَمز
        Assert.Equal(expected, AdminHostSignIn.Refusal(View(hostSlug, hostRole), "ejar", "") is not null);
    }

    [Fact]
    public void The_login_page_asks_it_before_drawing_the_card()
    {
        var s = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/Login.razor"));
        Assert.Contains("else if (Err == Services.Ux.AdminHostSignIn.NotStoreAdmin && Services.Ux.AdminHostSignIn.Applies(Slug))", s, StringComparison.Ordinal);
        Assert.Equal(1, s.Split("AdminHostSignIn.NotStoreAdmin").Length - 1);
    }
}
