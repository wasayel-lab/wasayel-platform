using ACommerce.Templates.Customer.Marketplace.Services.Patterns;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «اليَوم» — نُصوصُ شَخصِيّاتِ الأَنماطِ الخَمسَة بايتاً بايتاً قَبلَ تَرحيلِها إلى القامُوس (القاعِدَة ٣ و١١) ═══
//
// **المَقيس** (‏2026-10-13): `Services/Patterns/PatternProfile.cs` يَحمِلُ اثنَي عَشَرَ نَصّاً يَراه المُستَخدِمُ حَرفِيّاً في `.cs`
// خارِجَ `ar.json` — زِرُّ الفِعلِ الرَئيسيِّ (`PrimaryCtaLabel`) لِكُلِّ نَمَطٍ خَمسَة، وعُنوانُ البَطَل (`HomeHeroTitlePattern`) خَمسَة،
// وتَحذيرُ الاحتِيال (`FraudWarning`) اثنان. ومُستَهلِكوها في وَقتِ التَشغيل: `TenantHome.razor` (البَطَل) و`TenantListingDetail.razor`
// (التَحذيرُ والزِرُّ في ثَلاثَةِ مَواضِع) — كُلُّها تَعبيرُ `@…` يُرَمَّز، فَالنَصُّ نَفسُه بايتاً يَعني الصَفحَةَ نَفسَها بايتاً.
//
// **وأَداةُ المَظهَرِ لا تُغَطّي إلّا البَطَل** (‏137 صَفحَةً بِسِتَّةِ مِلَفّاتٍ عَلى قاعِدَةٍ مَبذورَة): «اطلُب مِن مَقاهيكَ المُفَضَّلَة»
// في `theme-demo-city` و`theme-demo-portal` وَحدَهُما، وصِفرُ صَفحَةٍ تَرسُمُ زِرّاً أَو تَحذيراً مِنها (صَفَحاتُ التَفاصيلِ المُلتَقَطَةُ
// عُروضٌ أَو لِمالِكِها، فَقِسمُ التَواصُلِ مَخفِيّ). **فَبُرهانُ ما لا تُغَطّيه الأَداةُ هذا الجَدوَل**: النَصُّ المَحلولُ بِعَينِه.
//
// **ويَبقى أَخضَرَ بِلا تَعديلِ حَرفٍ عِندَ التَرحيل** — ثُمَّ يَنقَلِبُ صَفُّ بَطَلِ `Marketplace` وَحدَه بِاسمِه في كوميتِ الصِياغَة
// (مَفصولٌ عَن الجَدوَلِ لِذلك، فَالجَدوَلُ لا يُمَسُّ حَتّى حينَئِذ).

public class PatternProfileTextsCharacterizationTests
{
    internal static string? Field(PatternProfile p, string field) => field switch
    {
        "cta"   => p.PrimaryCtaLabel,
        "hero"  => p.HomeHeroTitlePattern,
        "fraud" => p.FraudWarning,
        _       => throw new ArgumentOutOfRangeException(nameof(field), field, "حَقلٌ لا يَعرِفُه الجَدوَل")
    };

    /// <summary>أَحَدَ عَشَرَ نَصّاً بِحَرفِها — كُلُّ ما يَراه المُستَخدِمُ مِن الشَخصِيّاتِ إلّا بَطَلَ <c>Marketplace</c> (صَفُّه أَدناه).</summary>
    [Theory]
    [InlineData(AppPattern.Roommate,    "cta",   "تَواصَل مَع المُعلِن")]
    [InlineData(AppPattern.Roommate,    "hero",  "اِبحَث عَن شَريك السَكَن المُناسِب")]
    [InlineData(AppPattern.Roommate,    "fraud", "لا تَدفَع أَيّ مَبلَغ قَبل المُعايَنَة الشَخصِيَّة وَالتَأَكُّد مِن السَكَن.")]
    [InlineData(AppPattern.Rental,      "cta",   "اطلُب مُعايَنَة")]
    [InlineData(AppPattern.Rental,      "hero",  "كُلّ ما يُؤَجَّر في مَدينَتِكَ")]
    [InlineData(AppPattern.Rental,      "fraud", "لا تَدفَع التَأمين قَبل تَوقيع العَقد ومُعايَنَة العَقار.")]
    [InlineData(AppPattern.Marketplace, "cta",   "أَضِف لِلسَلَّة")]
    [InlineData(AppPattern.Trip,        "cta",   "قَدِّم عَرضاً")]
    [InlineData(AppPattern.Trip,        "hero",  "تَوصيلَتُك مِن أَيّ مَكان")]
    [InlineData(AppPattern.Service,     "cta",   "احجِز الخِدمَة")]
    [InlineData(AppPattern.Service,     "hero",  "اِحجِز خِدمَتَكَ بِنَقرَة")]
    public void Every_user_visible_profile_text_is_pinned_byte_for_byte(AppPattern pattern, string field, string expected)
    {
        var actual = Field(PatternProfileResolver.Profile(pattern), field);
        Assert.Equal(expected, actual);
    }

    /// <summary><b>بَطَلُ المَتجَرِ يَصدُقُ عَلى أَيِّ مَتجَرٍ يَبيعُ مُنتَجات</b> (‏2026-10-13 — انقَلَبَ بِقَرارٍ مَرئِيٍّ مِن
    /// <c>Today_the_marketplace_hero_names_cafes</c>: كانَ «اطلُب مِن مَقاهيكَ المُفَضَّلَة»، وهو بَطَلُ كُلِّ مَتجَرٍ يولَدُ مِن الاستوديو
    /// مُنذُ ‏ADR-034 — لا مَقهىً فيه بِالضَرورَة، وقَد يَكونُ بائِعُه واحِداً (‏ADR-032 §٦)). والصُفوفُ الأَحَدَ عَشَرَ أَعلاه لَم تُمَسّ.</summary>
    [Fact]
    public void The_marketplace_hero_is_true_for_any_store_that_sells_products()
    {
        var hero = PatternProfileResolver.Profile(AppPattern.Marketplace).HomeHeroTitlePattern;
        Assert.Equal("تَصَفَّح المُنتَجات واطلُب ما تُحِبّ", hero);
        Assert.DoesNotContain("مَقاهي", hero, StringComparison.Ordinal);
    }

    /// <summary>ثَلاثَةُ أَنماطٍ بِلا تَحذيرِ احتِيال — <c>null</c> لا نَصٌّ فارِغ، فَالتَفاصيلُ لا تَرسُمُ تَنبيهاً (‏<c>IsNullOrEmpty</c>).</summary>
    [Fact]
    public void The_three_patterns_without_a_fraud_warning_carry_none()
    {
        foreach (var p in new[] { AppPattern.Marketplace, AppPattern.Trip, AppPattern.Service })
            Assert.Null(PatternProfileResolver.Profile(p).FraudWarning);

        // ‏والأَنماطُ خَمسَةٌ لا غَير — نَمَطٌ سادِسٌ بِنُصوصٍ حَرفِيَّةٍ لا يَمُرُّ بِلا صَفٍّ هُنا.
        Assert.Equal(5, Enum.GetValues<AppPattern>().Length);
    }
}
