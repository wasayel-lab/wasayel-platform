using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ حَبسُ الدَرَجَةِ المَجّانِيَّة — رَفعٌ مُؤَقَّتٌ مُعلَّمٌ بِسَبَبِه ══
//
// **الحالَةُ المَقيسَةُ قَبلَ العِلاج (‏2026-08-30)**: كُلُّ رائِدِ
// أَعمالٍ يَدخُلُ الاستوديو يولَد على `spark` — لِأَنّ
// `StudioUser.Tier` افتِراضُها `"spark"` و**الكاتِبُ الوَحيدُ لِلحَقلِ
// في المُستَودَعِ كُلِّه** (`u.Tier = tier` في
// `/studio/billing/select`) واقِعٌ خَلفَ رَفضِ `StudioTierPurchase.Refuse`،
// وذاكَ يَرُدُّ الدَرَجاتِ الأَربَعَ لِأَنّ كُلَّها بِسِعرٍ > 0
// و`SelfServiceCheckoutExists = false`. فَالدَرَجَةُ **مَقفولَةٌ على
// `spark` بِلا كاتِبٍ يَبلُغُه أَحَد**: تَحليلٌ واحِدٌ شَهرِيّاً · ثَلاثُ
// تَحسينات · مَتجَرٌ واحِد · ولا تَصديرَ دِراسَة.
//
// **وقَرارُ المالِكِ يَومَ ‏2026-08-30**: تُرفَعُ حُدودُ الدَرَجَةِ
// المَجّانِيَّةِ **مُؤَقَّتاً** حَتّى يَستَقِرَّ التَسعير — ولا يُفتَرَضُ
// سِعر، ولا يُبنى بَيعٌ ذاتيّ، ولا يُمَسُّ حارِسُ `RefusePrice`.
//
// **ولا رَقمَ يُخترَع** (القاعِدَة ١٦): القِيَمُ المَرفوعُ إلَيها هي
// **بِعَينِها** أَرقامُ `scale` الَّتي كَتَبَها تَكليفُ المالِكِ في
// اليَومِ نَفسِه (‏40 تَحليلاً · 200 تَحسيناً · 40 مَتجَراً) — أَي
// السَقفُ الَّذي أَعلَنَ صاحِبُه أَنَّه «بَعيدٌ عَن مُستَخدِمٍ حَقيقيّ،
// وقَريبٌ بِما يَكفي لِيُغلِقَ البابَ في وَجهِ حَلقَةٍ آلِيَّة».
// فَالرَفعُ يَنقُلُ رَقماً قائِماً ولا يَنحِتُ رَقماً جَديداً.
public class FreeTierCeilingTests
{
    private static TierLimits Spark => TierCatalog.All["spark"];
    private static TierLimits Scale => TierCatalog.All["scale"];

    // ─── ١. الرَفعُ نَفسُه ───────────────────────────────────────────

    /// <summary>
    /// <para><b>لا حَدَّ في الدَرَجَةِ المَجّانِيَّةِ أَضيَقُ مِن
    /// السَقفِ المُصَرَّحِ بِه.</b> والمُقارَنَةُ بِـ<c>scale</c> لا
    /// بِأَرقامٍ مَكتوبَةٍ هُنا: رَقمٌ يُكتَبُ في الاختِبارِ ورَقمٌ
    /// يُكتَبُ في الكاتالوجِ يَنجَرِفان، وهذا الشَكلُ يَجعَلُ الانجِرافَ
    /// مُستَحيلاً بِالبِناء.</para>
    /// </summary>
    [Fact]
    public void The_free_tier_is_raised_to_the_ceiling_the_owner_already_assigned()
    {
        Assert.True(TierCatalog.All.Count >= 4,
            $"أَداةٌ عَمياء: فُحِصَت {TierCatalog.All.Count} دَرَجَة — والمَقيسُ أَربَع.");

        // ‏**والفَحصُ يَصِفُ الحالَتَينِ لا واحِدَة** — وإلّا كَذَبَت
        // دَعوى «العَودَةُ سَطران». انظُر التَعليقَ أَعلى المِلَفّ.
        if (!TierCatalog.FreeTierTemporarilyRaised)
        {
            var before = TierCatalog.FreeTierBeforeRaise;
            Assert.Equal(before.AnalysesPerMonth, Spark.AnalysesPerMonth);
            Assert.Equal(before.RefinesPerMonth,  Spark.RefinesPerMonth);
            Assert.Equal(before.StoresMax,        Spark.StoresMax);
            Assert.Equal(before.AllowExport,      Spark.AllowExport);
            return;
        }

        Assert.Equal(Scale.AnalysesPerMonth, Spark.AnalysesPerMonth);
        Assert.Equal(Scale.RefinesPerMonth,  Spark.RefinesPerMonth);
        Assert.Equal(Scale.StoresMax,        Spark.StoresMax);
    }

    /// <summary>
    /// <para><b>وتَصديرُ الدِراسَةِ يُفتَح.</b> ‏
    /// <c>GET /studio/s/{id}/export.xlsx</c> خَلفَ
    /// <c>limits.AllowExport</c>، و<c>spark</c> كانَت <c>false</c> ولا
    /// دَرَجَةَ أُخرى تُبلَغ — أَي أَنّ النُقطَةَ كانَت **مَقفولَةً
    /// لِكُلِّ مُستَأجِرٍ في المُستَودَعِ كُلِّه**. وهذا أَثَرٌ لا يُرى
    /// مِن مِلَفِّ الدَرَجاتِ وَحدَه، فَيُقاسُ صَراحَةً.</para>
    /// </summary>
    [Fact]
    public void Study_export_is_reachable_by_every_tenant_once_the_free_tier_is_raised()
    {
        // مَشروطٌ بِالرَفع: يَومَ يَعودُ التَسعيرُ تَعودُ `spark` بِلا
        // تَصدير، وذلكَ سِياسَةُ باقَةٍ لا عَطَب. والحارِسُ الَّذي
        // يَبقى في الحالَتَينِ هُوَ `The_export_gate_is_not_a_dead_guard`.
        if (!TierCatalog.FreeTierTemporarilyRaised) return;

        var locked = TierCatalog.All.Values
            .Where(t => !t.AllowExport)
            .Select(t => t.Tier)
            .ToArray();

        Assert.True(locked.Length == 0,
            "دَرَجاتٌ ما زالَ تَصديرُ الدِراسَةِ مَقفولاً فيها: "
            + string.Join("، ", locked)
            + " — والدَرَجَةُ المَجّانِيَّةُ هي الَّتي يَقَعُ عَلَيها كُلُّ "
            + "مُستَأجِرٍ اليَوم.");
    }

    // ─── ٢. الرَفعُ **مُؤَقَّتٌ** ومَكتوبٌ أَنَّه كَذلك ───────────────

    /// <summary>
    /// <para><b>الحالَةُ السابِقَةُ مَحفوظَةٌ بَياناتٍ لا تَعليقاً</b> —
    /// فَالعَودَةُ يَومَ يَستَقِرُّ التَسعيرُ سَطرٌ واحِدٌ لا أَثَرٌ
    /// يُعادُ اكتِشافُه. وتَعليقٌ يَقولُ «مُؤَقَّت» بِلا قيمَةٍ
    /// مَحفوظَةٍ هُوَ نِيَّةٌ لا خُطَّةُ رُجوع.</para>
    /// </summary>
    [Fact]
    public void The_raise_is_declared_temporary_and_carries_the_values_it_replaced()
    {
        var before = TierCatalog.FreeTierBeforeRaise;
        Assert.Equal("spark", before.Tier);

        // القِيَمُ المَحفوظَةُ تَبقى مَحفوظَةً على **طَرَفَي** العَلَم —
        // فَهي التاريخُ لا الحالَة. أَمّا المُقارَنَةُ بِالمَعمولِ بِه
        // فَمَشروطَةٌ بِالرَفع.
        Assert.Equal(1,     before.AnalysesPerMonth);
        Assert.Equal(3,     before.RefinesPerMonth);
        Assert.Equal(1,     before.StoresMax);
        Assert.False(before.AllowExport);

        if (!TierCatalog.FreeTierTemporarilyRaised) return;

        // وهي **دونَ** المَعمولِ بِه الآن — وإلّا فَالرَفعُ لَم يَقَع.
        Assert.True(Spark.AnalysesPerMonth > before.AnalysesPerMonth);
        Assert.True(Spark.RefinesPerMonth  > before.RefinesPerMonth);
        Assert.True(Spark.StoresMax        > before.StoresMax);
    }

    // ─── ٣. ما لا يُمَسّ — نِصفُ التَكليفِ الحامِل ───────────────────

    /// <summary><b>لا سِعرَ يَتَبَدَّل.</b> نَصُّ المالِكِ صَريح: لا
    /// يُفتَرَضُ سِعر. والرَفعُ حُدودٌ لا تَسعير.</summary>
    [Fact]
    public void Not_one_price_moved()
    {
        Assert.Equal(99,  TierCatalog.All["spark"].MonthlyPriceSar);
        Assert.Equal(199, TierCatalog.All["lite"].MonthlyPriceSar);
        Assert.Equal(399, TierCatalog.All["growth"].MonthlyPriceSar);
        Assert.Equal(999, TierCatalog.All["scale"].MonthlyPriceSar);
    }

    /// <summary><b>ولا حارِسَ يَضعُف.</b> ‏<c>SelfServiceCheckoutExists</c>
    /// يَبقى <c>false</c>، وكُلُّ دَرَجَةٍ بِسِعرٍ تَبقى مَردودَةً —
    /// نَفسُ ما تَقيسُه <c>PaymentLeakTests</c>، ويُعادُ هُنا لِأَنّ
    /// هذِه المَوجَةَ هي الَّتي كانَ يُمكِنُ أَن تَكسِرَه.</summary>
    [Fact]
    public void The_self_service_purchase_guard_is_untouched()
    {
        Assert.False(StudioTierPurchase.SelfServiceCheckoutExists);

        var granted = TierCatalog.All.Values
            .Where(t => t.MonthlyPriceSar > 0)
            .Where(t => StudioTierPurchase.IsSelfGrantable(t.Tier))
            .Select(t => t.Tier)
            .ToArray();

        Assert.True(granted.Length == 0,
            "دَرَجاتٌ بِسِعرٍ صارَت تُمنَحُ ذاتِيّاً: " + string.Join("، ", granted));
    }

    /// <summary><b>ولا سَقفَ يَصيرُ لا نِهائِيّاً.</b> ‏<c>int.MaxValue</c>
    /// في حَدٍّ شَهرِيٍّ فاتورَةٌ مَفتوحَةٌ على مِفتاحِ نَموذَجِ لُغَةِ
    /// المالِك — وهي بِعَينِها الكَلفَةُ الَّتي أَغلَقَتها المَوجَةُ
    /// السابِقَة. الرَفعُ يَرفَعُ السَقفَ ولا يَرفَعُه.</summary>
    [Fact]
    public void The_raise_does_not_reopen_the_unbounded_ceiling()
    {
        var unbounded = TierCatalog.All.Values
            .SelectMany(t => new[]
            {
                (t.Tier, Value: t.AnalysesPerMonth),
                (t.Tier, Value: t.RefinesPerMonth),
                (t.Tier, Value: t.StoresMax),
            })
            .Where(x => x.Value == int.MaxValue)
            .Select(x => x.Tier)
            .ToArray();

        Assert.True(unbounded.Length == 0,
            "حُدودٌ بِلا سَقف: " + string.Join("، ", unbounded));
    }

    /// <summary>والبَوّابَةُ ما زالَت تُغلَقُ عِندَ السَقفِ الجَديد —
    /// حَدٌّ مَرفوعٌ لا حَدَّ مَحذوف.</summary>
    [Fact]
    public void The_gate_still_closes_at_the_raised_cap()
    {
        var user = new StudioUser { Tier = "spark", AnalysesUsed = Spark.AnalysesPerMonth };
        Assert.True(user.AnalysesUsed >= TierCatalog.For(user.Tier).AnalysesPerMonth);

        var below = new StudioUser { Tier = "spark", AnalysesUsed = Spark.AnalysesPerMonth - 1 };
        Assert.False(below.AnalysesUsed >= TierCatalog.For(below.Tier).AnalysesPerMonth);
    }
}
