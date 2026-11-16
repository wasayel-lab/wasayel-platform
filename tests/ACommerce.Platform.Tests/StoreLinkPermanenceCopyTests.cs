using ACommerce.Platform.I18n;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ الرابِطُ لا يُباعُ قابِلاً لِلتَغيير ولا بابَ يُغَيِّرُه (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·C1D-2 — القاعِدَة ١٢) ═══
//
// **المَقيسُ قَبل**: الصَفحَةُ الأُولى تَقول «الاسمُ والرابِطُ واللَونُ… كُلُّها مِن لَوحَةِ مَتجَرِك»، واستِمارَةُ الهُوِيَّةِ تُرسِلُ الاسمَ والسَطرَ والمَدينَةَ واللَونَ
// والقَناةَ وصيغَةَ السَلَّةِ وَحدَها، ولا بابَ يُغَيِّرُ مُعَرِّفَ مَتجَر — فَرائِدُ الأَعمالِ يَختارُ رابِطَه واثِقاً أَنّه يُغَيَّرُ لاحِقاً ولا يُغَيَّر («ميزَةٌ تُباعُ
// ولا تُفرَض»). **والآن** النَصُّ لا يَعِدُ بِه، وحَقلُ الاستِمارَةِ يَقولُ إنّه ثابِت؛ ويَومَ يولَدُ بابُ تَغييرِ المُعَرِّفِ يَحمَرُّ هذا الصَفُّ فَيُعادُ الوَعدُ مَعَه.

public class StoreLinkPermanenceCopyTests(ITestOutputHelper output)
{
    private static string Ar(string key) => LocaleCatalog.Find(LocaleCatalog.Arabic, key)!;

    [Fact]
    public void The_landing_does_not_promise_a_changeable_link_while_no_door_changes_a_slug()
    {
        var renameDoors = ComplianceInspectorTests.RepoRoutes
            .Where(r => r.StartsWith("/studio/apps/{slug}", StringComparison.Ordinal) || r.StartsWith("/admin/tenants/{slug}", StringComparison.Ordinal))
            .Where(r => System.Text.RegularExpressions.Regex.IsMatch(r, "rename|slug/|new_slug|change-slug", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
        var setup = Ar("landing.feature.setup_body");
        output.WriteLine($"بابُ تَغييرِ المُعَرِّف: {renameDoors.Count} · نَصُّ الصَفحَةِ الأُولى: {setup}");
        Assert.Empty(renameDoors);
        Assert.DoesNotContain("الرابِط", setup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_create_form_says_the_link_is_fixed_after_creation()
        => Assert.Contains("لا يَتَغَيَّرُ بَعدَ الإنشاء", Ar("studio.create.field_slug"), StringComparison.Ordinal);
}
