using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Services.Subscriptions;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَأليفُ باقَةِ مَتجَرٍ — قِراءَةُ النَموذَجِ دالَّةٌ نَقِيَّة ══════
//
// **العِلَّةُ المَقيسَة (‏2026-08-30)**: ‏`TenantPlanDefinition` وَثيقَةٌ
// **بِصِفرِ كاتِبٍ في المُستَودَعِ كُلِّه** — مُعَرَّفَةٌ، والخِدمَةُ
// تَرِثُ `Propose/Decide`، و`Plans.razor` تَعرِضُها،
// و`TenantExportLedger` يُصَدِّرُها، **ولا نُقطَةَ ولا شاشَةَ تَكتُبُ
// واحِدَة**. فَالتاجِرُ لا يُؤَلِّفُ باقَةً ولا يُسَعِّرُها.
//
// **ولِماذا دالَّةٌ نَقِيَّةٌ لِقِراءَةِ النَموذَج**: الباقَةُ **مالٌ
// وحِصَّة** لا تَسمِيَةٌ ولَون — حِصَّةٌ سالِبَةٌ تُعطي رَصيداً سالِباً
// مِن أَوَّلِ يَوم، ومُدَّةٌ صِفرِيَّةٌ اشتِراكاً يَنتَهي قَبلَ أَن
// يَبدَأ. وتَحويلُ سِلسِلَةِ نَموذَجٍ إلى عَدَدٍ هُوَ بِالضَبطِ حَيثُ
// تَقَعُ هذِه الأَخطاء، فَيُقاسُ وَحدَه (نَفسُ حُجَّةِ
// `TenantPlanPolicy.ReadSetting`).
public class TenantPlanAuthoringTests
{
    private static (PlanDefinition Def, IReadOnlyList<PlanDefinitionViolation> V) Read(
        string? slug = "silver", string? label = "الفِضِّيَّة", string? desc = "باقَةٌ لِلبائِعين",
        string? price = "0", string? quota = "10", string? days = "30", bool active = true)
        => TenantPlanAuthoring.ReadDefinition(slug, label, desc, price, quota, days, active);

    // ─── ١. الطَريقُ السَعيد ─────────────────────────────────────────

    [Fact]
    public void A_well_formed_form_becomes_a_valid_definition()
    {
        var (def, v) = Read();

        Assert.Empty(v);
        Assert.Equal("silver", def.Slug);
        Assert.Equal("الفِضِّيَّة", def.Label.Ar);
        Assert.Equal(0m, def.Price);
        Assert.Equal(10, def.ListingsQuota);
        Assert.Equal(30, def.DaysPeriod);
        Assert.True(def.IsActive);
    }

    /// <summary><b>والسلاجُ يُطَبَّع</b> — مِسافاتٌ وحالَةُ أَحرُفٍ لا
    /// تُنتِجُ باقَتَينِ بِنَفسِ الاسم.</summary>
    [Fact]
    public void The_slug_is_normalised_before_it_becomes_a_document_key()
    {
        var (def, v) = Read(slug: "  Silver  ");
        Assert.Empty(v);
        Assert.Equal("silver", def.Slug);
    }

    // ─── ٢. المال والحِصَّة — حَيثُ تَقَعُ الأَخطاءُ فِعلاً ──────────

    [Theory]
    [InlineData("-1",   "price_negative")]
    [InlineData("abc",  null)]            // غَيرُ مَقروءٍ = صِفر، وصِفرٌ صالِح
    public void A_price_that_cannot_be_read_never_becomes_a_silent_number(string price, string? code)
    {
        var (def, v) = Read(price: price);

        if (code is null)
        {
            Assert.Empty(v);
            Assert.Equal(0m, def.Price);
        }
        else Assert.Contains(v, x => x.Code == code);
    }

    [Fact]
    public void A_negative_quota_is_refused_with_its_code()
        => Assert.Contains(Read(quota: "-5").V, x => x.Code == "quota_negative");

    [Fact]
    public void A_zero_or_missing_period_is_refused_with_its_code()
    {
        Assert.Contains(Read(days: "0").V,  x => x.Code == "period_not_positive");
        Assert.Contains(Read(days: "").V,   x => x.Code == "period_not_positive");
        Assert.Contains(Read(days: "999").V, x => x.Code == "period_too_long");
    }

    // ─── ٣. الهُوِيَّةُ والتَوطين ────────────────────────────────────

    [Fact]
    public void An_empty_slug_or_a_slug_outside_the_pattern_is_refused()
    {
        Assert.Contains(Read(slug: "").V,        x => x.Code == "slug_empty");
        Assert.Contains(Read(slug: "9gold").V,   x => x.Code == "slug_pattern");
        Assert.Contains(Read(slug: "ذَهَبِيَّة").V, x => x.Code == "slug_pattern");
    }

    /// <summary><b>ولا يُظَلِّلُ سلاجَ باقَةٍ مَبذورَة</b> — كَي لا
    /// يُغَيِّرَ مُستَأجِرٌ مَعنى «مَجّانيّ» مِن تَحتِ مَن
    /// يَقرَؤُه.</summary>
    [Theory]
    [InlineData("basic")]
    [InlineData("free")]
    [InlineData("pro")]
    public void A_slug_reserved_for_a_seeded_plan_is_refused(string slug)
        => Assert.Contains(Read(slug: slug).V, x => x.Code == "slug_shadows_seeded_plan");

    [Fact]
    public void An_arabic_label_is_mandatory()
    {
        Assert.Contains(Read(label: "").V, x => x.Code == "localized_arabic_missing");
        Assert.Contains(Read(desc: " ").V, x => x.Code == "localized_arabic_missing");
    }

    // ─── ٤. ما يُخَزَّن يُقرَأُ كَما كُتِب ───────────────────────────

    /// <summary><b>ودَورَةٌ كامِلَة</b>: تَعريفٌ → نَصّ → تَعريف. وهذا
    /// هُوَ العَقدُ الَّذي تَعتَمِدُ عَلَيه
    /// <c>TenantPlanSet.FromDocuments</c> عِندَ كُلِّ قِراءَة.</summary>
    [Fact]
    public void The_definition_survives_the_round_trip_through_its_stored_text()
    {
        var (def, v) = Read(slug: "silver", price: "49.5", quota: "25", days: "90");
        Assert.Empty(v);

        var back = PlanDefinitionLoader.ParseDefinition(PlanDefinitionLoader.ToJson(def));

        Assert.Equal(def.Slug, back.Slug);
        Assert.Equal(def.Label.Ar, back.Label.Ar);
        Assert.Equal(def.Description.Ar, back.Description.Ar);
        Assert.Equal(def.Price, back.Price);
        Assert.Equal(def.ListingsQuota, back.ListingsQuota);
        Assert.Equal(def.DaysPeriod, back.DaysPeriod);
        Assert.Equal(def.IsActive, back.IsActive);
    }

    /// <summary><b>وباقَةٌ بِسِعرٍ لا تُمنَحُ ذاتِيّاً — ولَو أَلَّفَها
    /// صاحِبُ المَتجَرِ بِنَفسِه.</b> ‏<c>PlanPurchasePolicy</c> هُوَ
    /// الحارِس، وقَرارُ المالِكِ في ‏ADR-002 هُوَ سَبَبُه: «لا تَسمَح
    /// لِلتاجِر بِاستِلام حَوالات». فَتَأليفُ الباقاتِ **لا يُحَرِّكُ
    /// مالاً**، وهذا هُوَ ما يَجعَلُه آمِناً.</summary>
    [Fact]
    public void Authoring_a_priced_plan_moves_no_money()
    {
        var (def, _) = Read(price: "199");
        var plan = def.ToPlan();

        Assert.Equal(PlanPurchasePolicy.PaidUnavailable,
            PlanPurchasePolicy.Refuse(plan, paymentProviderConfigured: false));

        // ولا تُعرَضُ أَصلاً لِزائِرِ مَتجَرٍ لا يَقبِض.
        var visible = PlanPurchasePolicy.Visible(new[] { plan }, paymentProviderConfigured: false);
        Assert.Empty(visible);
    }
}
