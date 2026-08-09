using System.Text;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── اختِبار تَوصيف DealsPolicy (Characterization) ───────────────────
// TESTING-PROTOCOL §5 الخُطوَة 4: «سُلوك الأَنماط الخَمسَة الحاليَّة لا
// يَتَغَيَّر بَتاً — اختِبار تَوصيف قَبل/بَعد».
//
// هذا المِلَفّ يَلتَقِط السُّلوك **كامِلاً** كَبَيانات ذَهَبيَّة حَرفيَّة:
// لِكُلّ نَمَط مِن السِّتَّة (الخَمسَة + الافتِراضيّ) التَّسَلسُل الكامِل،
// و Next لِكُلّ مَرحَلَة مِن الثَّمانِ (لا لِمَراحِل النَّمَط فَقَط — حَتَّى
// الـ null لِمَرحَلَة خارِج التَّسَلسُل مُوَثَّق)، ثُمَّ Actor و LabelAr
// لِلقِيَم الثَّمانِ.
//
// كُتِبَ واخضَرَّ **قَبل** رَفع السِّياسَة إلى بَيانات، ولَم يُمَسّ سَطر
// واحِد مِنه بَعدَه — فَمُروره عَلى الكودَين هو بُرهان التَّطابُق، ويَبقى
// حارِساً دائِماً ضِدّ أَيّ انحِراف صامِت لاحِق.

public class DealsPolicyCharacterizationTests
{
    /// <summary>الأَنماط بِتَرتيب ثابِت — الخَمسَة المُعَرَّفَة، ثُمَّ اسم
    /// غَير مَعروف يَسقُط عَلى التَّسَلسُل الافتِراضيّ.</summary>
    private static readonly string[] PatternsUnderTest =
        { "trip", "rental", "marketplace", "classifieds", "service", "pattern_from_the_future" };

    /// <summary>اللَّقطَة الذَّهَبيَّة — نَصّ حَرفيّ لا يُشتَقّ مِن الكود
    /// المَفحوص بِأَيّ حال (وإلّا صارَ الاختِبار دَورَة فارِغَة).</summary>
    private const string Golden =
"""
# DealsPolicy characterization snapshot

[stages]
trip: Offered > Booked > Confirmed > Delivered > Reviewed
rental: Offered > Booked > Confirmed > Paid > Delivered > Received > Reviewed
marketplace: Offered > Booked > Confirmed > Paid > Shipping > Delivered > Reviewed
classifieds: Offered > Booked > Confirmed
service: Offered > Booked > Confirmed > Paid > Delivered > Reviewed
pattern_from_the_future: Offered > Booked > Confirmed > Reviewed

[next]
trip.Offered -> Booked
trip.Booked -> Confirmed
trip.Confirmed -> Delivered
trip.Paid -> (null)
trip.Shipping -> (null)
trip.Delivered -> Reviewed
trip.Received -> (null)
trip.Reviewed -> (null)
rental.Offered -> Booked
rental.Booked -> Confirmed
rental.Confirmed -> Paid
rental.Paid -> Delivered
rental.Shipping -> (null)
rental.Delivered -> Received
rental.Received -> Reviewed
rental.Reviewed -> (null)
marketplace.Offered -> Booked
marketplace.Booked -> Confirmed
marketplace.Confirmed -> Paid
marketplace.Paid -> Shipping
marketplace.Shipping -> Delivered
marketplace.Delivered -> Reviewed
marketplace.Received -> (null)
marketplace.Reviewed -> (null)
classifieds.Offered -> Booked
classifieds.Booked -> Confirmed
classifieds.Confirmed -> (null)
classifieds.Paid -> (null)
classifieds.Shipping -> (null)
classifieds.Delivered -> (null)
classifieds.Received -> (null)
classifieds.Reviewed -> (null)
service.Offered -> Booked
service.Booked -> Confirmed
service.Confirmed -> Paid
service.Paid -> Delivered
service.Shipping -> (null)
service.Delivered -> Reviewed
service.Received -> (null)
service.Reviewed -> (null)
pattern_from_the_future.Offered -> Booked
pattern_from_the_future.Booked -> Confirmed
pattern_from_the_future.Confirmed -> Reviewed
pattern_from_the_future.Paid -> (null)
pattern_from_the_future.Shipping -> (null)
pattern_from_the_future.Delivered -> (null)
pattern_from_the_future.Received -> (null)
pattern_from_the_future.Reviewed -> (null)

[actor]
Offered = initiator
Booked = counterparty
Confirmed = either
Paid = initiator
Shipping = counterparty
Delivered = counterparty
Received = initiator
Reviewed = either

[label]
Offered = عَرض/طَلَب
Booked = حَجز
Confirmed = تَأكيد
Paid = دَفع
Shipping = شَحن
Delivered = تَسليم
Received = استِلام
Reviewed = تَقييم
""";

    [Fact]
    public void Policy_Snapshot_Matches_Golden()
    {
        Assert.Equal(
            Golden.ReplaceLineEndings("\n").Trim(),
            Render().ReplaceLineEndings("\n").Trim());
    }

    // ─── تَأكيدات مَقروءَة مُنفَصِلَة عَن اللَّقطَة ────────────────────
    // اللَّقطَة تَحرُس كُلّ شَيء دَفعَةً واحِدَة؛ هذه تَشرَح ماذا تَحرُس
    // بِلُغَة المَجال، وتَنكَسِر بِرِسالَة أَوضَح عِندَ تَغيير تَسَلسُل واحِد.

    [Fact]
    public void Stages_Trip() => AssertStages("trip",
        DealStage.Offered, DealStage.Booked, DealStage.Confirmed,
        DealStage.Delivered, DealStage.Reviewed);

    [Fact]
    public void Stages_Rental() => AssertStages("rental",
        DealStage.Offered, DealStage.Booked, DealStage.Confirmed,
        DealStage.Paid, DealStage.Delivered, DealStage.Received,
        DealStage.Reviewed);

    [Fact]
    public void Stages_Marketplace() => AssertStages("marketplace",
        DealStage.Offered, DealStage.Booked, DealStage.Confirmed,
        DealStage.Paid, DealStage.Shipping, DealStage.Delivered,
        DealStage.Reviewed);

    [Fact]
    public void Stages_Classifieds() => AssertStages("classifieds",
        DealStage.Offered, DealStage.Booked, DealStage.Confirmed);

    [Fact]
    public void Stages_Service() => AssertStages("service",
        DealStage.Offered, DealStage.Booked, DealStage.Confirmed,
        DealStage.Paid, DealStage.Delivered, DealStage.Reviewed);

    [Fact]
    public void Stages_UnknownPattern_FallsBackToDefault() => AssertStages("pattern_from_the_future",
        DealStage.Offered, DealStage.Booked, DealStage.Confirmed,
        DealStage.Reviewed);

    [Fact]
    public void Actor_PerStage_IsStable()
    {
        Assert.Equal("initiator",    DealsPolicy.Actor(DealStage.Offered));
        Assert.Equal("counterparty", DealsPolicy.Actor(DealStage.Booked));
        Assert.Equal("either",       DealsPolicy.Actor(DealStage.Confirmed));
        Assert.Equal("initiator",    DealsPolicy.Actor(DealStage.Paid));
        Assert.Equal("counterparty", DealsPolicy.Actor(DealStage.Shipping));
        Assert.Equal("counterparty", DealsPolicy.Actor(DealStage.Delivered));
        Assert.Equal("initiator",    DealsPolicy.Actor(DealStage.Received));
        Assert.Equal("either",       DealsPolicy.Actor(DealStage.Reviewed));
    }

    /// <summary>قيمَة خارِج التَّعداد — الفَرع الافتِراضيّ القَديم
    /// (<c>_ =&gt; "platform"</c>) جُزء مِن السُّلوك المَوصوف.</summary>
    [Fact]
    public void Actor_OutOfVocabularyStage_FallsBackToPlatform()
        => Assert.Equal("platform", DealsPolicy.Actor((DealStage)99));

    /// <summary>ونَظيرُه في التَّسمِيَة (<c>_ =&gt; s.ToString()</c>).</summary>
    [Fact]
    public void LabelAr_OutOfVocabularyStage_FallsBackToEnumName()
        => Assert.Equal("99", DealsPolicy.LabelAr((DealStage)99));

    [Fact]
    public void Next_OutsidePatternStages_IsNull()
    {
        // Paid ليسَت مِن مَراحِل trip — لا تالٍ لَها في ذلِك النَّمَط.
        Assert.Null(DealsPolicy.Next("trip", DealStage.Paid));
        // وآخِر مَرحَلَة في النَّمَط بِلا تالٍ.
        Assert.Null(DealsPolicy.Next("classifieds", DealStage.Confirmed));
    }

    private static void AssertStages(string pattern, params DealStage[] expected)
        => Assert.Equal(expected, DealsPolicy.StagesFor(pattern).ToArray());

    // ─── المُصَيِّر: نَصّ حَتميّ يُغَطّي كُلّ سَطح السِّياسَة ─────────────
    internal static string Render()
    {
        var all = Enum.GetValues<DealStage>();
        var sb = new StringBuilder();

        sb.Append("# DealsPolicy characterization snapshot\n");

        sb.Append("\n[stages]\n");
        foreach (var p in PatternsUnderTest)
            sb.Append($"{p}: {string.Join(" > ", DealsPolicy.StagesFor(p))}\n");

        sb.Append("\n[next]\n");
        foreach (var p in PatternsUnderTest)
            foreach (var s in all)
                sb.Append($"{p}.{s} -> {DealsPolicy.Next(p, s)?.ToString() ?? "(null)"}\n");

        sb.Append("\n[actor]\n");
        foreach (var s in all)
            sb.Append($"{s} = {DealsPolicy.Actor(s)}\n");

        sb.Append("\n[label]\n");
        foreach (var s in all)
            sb.Append($"{s} = {DealsPolicy.LabelAr(s)}\n");

        return sb.ToString();
    }
}
