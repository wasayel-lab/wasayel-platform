using System.Text.Json.Serialization;

namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>
/// صَفّ واحِد في تَعريف النَّمَط: مَرحَلَة، ومَن يَملِك تَحريكَها، وكَيف
/// تُسَمَّى في الواجِهَة. <b>مُكتَفٍ بِذاتِه</b> — لا يُحيل إلى جَدوَل
/// خارِجيّ، لِيَبقى تَعريف النَّمَط قابِلاً لِلتَّخزين وَثيقَةً واحِدَة.
/// </summary>
/// <param name="Stage">المَرحَلَة مِن مُفرَدات <see cref="DealStage"/>
/// الثَّمانِ المُغلَقَة.</param>
/// <param name="Actor">الفاعِل المُخَوَّل: initiator | counterparty |
/// either | platform (انظُر <see cref="DealPatternCatalog.Actors"/>).</param>
/// <param name="LabelAr">التَّسمِيَة العَرَبيَّة المَعروضَة.</param>
public sealed record DealStageRule(
    DealStage Stage,
    string Actor,
    string LabelAr);

/// <summary>
/// <para><b>تَعريف نَمَط صَفقَة كَبَيانات</b> — الأَثَر التَّصريحيّ الَّذي
/// حَلَّ مَحَلّ الـ switch المُجَمَّع في <see cref="DealsPolicy"/>
/// (META-MODEL §8، TESTING-PROTOCOL §5 الخُطوَة 4).</para>
///
/// <para>سِجِلّ بَيانات خالِص: لا اعتِماد عَلى Marten وَلا عَلى HTTP وَلا
/// عَلى أَيّ خِدمَة — يُبنى في الذاكِرَة اليَوم
/// (<see cref="DealPatternCatalog"/>)، ويُقرَأ مِن وَثيقَة لِكُلّ مُستَأجِر
/// غَداً بِلا تَغيير في شَكلِه. <b>حَدّ هذه المَوجَة صَراحَةً</b>: لا
/// تَخزين — الشَّكل جاهِز لَه فَقَط.</para>
///
/// <para>الصِّحَّة البُنيَويَّة والخَصائِص المُبرهَنَة تُفحَص بِدَوالّ
/// نَقِيَّة في <see cref="DealPatternValidator"/> (T5/T6).</para>
/// </summary>
/// <param name="Pattern">اسم النَّمَط (slug) — trip | rental |
/// marketplace | classifieds | service | …</param>
/// <param name="Stages">صُفوف المَراحِل <b>بِتَرتيب التَّدَفُّق</b>.</param>
public sealed record DealPatternDefinition(
    string Pattern,
    IReadOnlyList<DealStageRule> Stages)
{
    /// <summary>المَراحِل وَحدَها بِتَرتيبِها — تُحسَب مَرَّةً عِندَ
    /// الإنشاء. مُستَثناة مِن التَّسَلسُل لِأَنَّها مُشتَقَّة مِن
    /// <see cref="Stages"/> لا بَيان مُستَقِلّ.</summary>
    [JsonIgnore]
    public IReadOnlyList<DealStage> StageOrder { get; } =
        Stages.Select(r => r.Stage).ToArray();

    /// <summary>المَرحَلَة التالِيَة، أَو <c>null</c> عِندَ آخِر مَرحَلَة
    /// أَو لِمَرحَلَة ليسَت مِن هذا النَّمَط.</summary>
    public DealStage? Next(DealStage current)
    {
        var order = StageOrder;
        var idx = -1;
        for (var i = 0; i < order.Count; i++)
            if (order[i] == current) { idx = i; break; }
        if (idx < 0 || idx >= order.Count - 1) return null;
        return order[idx + 1];
    }

    /// <summary>الفاعِل المُخَوَّل بِتَحريك مَرحَلَة مِن هذا النَّمَط،
    /// أَو <c>null</c> لَو لَم تَكُن مِنه.</summary>
    public string? ActorFor(DealStage stage)
    {
        foreach (var r in Stages) if (r.Stage == stage) return r.Actor;
        return null;
    }

    /// <summary>تَسمِيَة مَرحَلَة مِن هذا النَّمَط، أَو <c>null</c>.</summary>
    public string? LabelFor(DealStage stage)
    {
        foreach (var r in Stages) if (r.Stage == stage) return r.LabelAr;
        return null;
    }

    /// <summary>هَل المَرحَلَة جُزء مِن هذا النَّمَط؟</summary>
    public bool Includes(DealStage stage) => ActorFor(stage) is not null;
}
