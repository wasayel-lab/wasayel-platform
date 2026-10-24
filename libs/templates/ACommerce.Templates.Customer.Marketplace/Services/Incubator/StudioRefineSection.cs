namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// <para><b>أَقسامُ الدِراسَةِ الَّتي يُعادُ تَوليدُها — مَعجَمٌ مُغلَق</b> (‏2026-10-24، مُراجَعَةُ تَيّارِ S15 الجَولَة 2؛ تَوصيفُه
/// <c>65d0b6a</c>؛ عَلى شَكلِ <c>Metering.ModelCallOperation</c>: <c>All</c> و<c>IsKnown</c>).</para>
///
/// <para><b>العِلَّةُ المَقيسَة</b>: <c>POST /studio/s/{id}/refine</c> كانَ يَأخُذُ مِفتاحَ القِسمِ مِن الاستِمارَةِ ويَفحَصُه بِالفَراغِ
/// وَحدَه، ثُمَّ يَكتُبُه في المُطالَبَةِ ويُمَرِّرُه إلى <see cref="FeasibilityAnalysisService"/>.<c>MergeSection</c> الَّتي تُضيفُ ما لَم
/// يَكُن (إضافَةٌ مَقصودَةٌ لِقِسمٍ مَعروفٍ غابَ عَن دِراسَةٍ أَقدَم). فَطَلَبٌ مَصنوعٌ بِـ<c>section=injected_by_b</c> زَرَعَ مِفتاحاً رابِعَ
/// عَشَرَ في دِراسَةٍ مَفاتيحُ مُخَطَّطِها ثَلاثَةَ عَشَر، وأَنفَقَ وَحدَةَ تَحسينٍ ونِداءً لِلمُزَوِّد.</para>
///
/// <para><b>والمَعجَمُ ما تَعرِضُه الصَفحَةُ بِحَرفِه</b>: قائِمَةُ «القِسم» في <c>StudioStudy.razor</c> عَشَرَةُ خِيارات، كُلٌّ مِنها
/// مِفتاحٌ أَعلى في <see cref="FeasibilityPromptBuilder.OutputSchema"/> — والثَلاثَةُ الباقِيَةُ (<c>summary</c>، <c>kpis</c>،
/// <c>recommendations</c>) لا تُعرَضُ فَلا تُقبَل. ولا يُعادُ رَسمُ الصَفحَةِ مِنه (تَبقى بايتاً كَما هي)؛ والتَطابُقُ في الاتِّجاهَينِ
/// صَفٌّ في <c>StudioRefineOwnershipTests</c> يَحمَرُّ إن زيدَ خِيارٌ في أَحَدِهِما دونَ الآخَر (القاعِدَة ٢).</para>
///
/// <para><b>ومَن يَسأَلُه</b>: النُقطَةُ قَبلَ الحِصَّة، و<c>RefineSectionAsync</c> قَبلَ المُطالَبَةِ والنِداءِ والدَمج — فَلا يَبلُغُ
/// الكاتِبَ مِفتاحٌ لا يَعرِفُه ولو جاءَه مِن غَيرِ النُقطَة. ولا رَمزَ خَرقٍ ولا رِسالَة: الرَدُّ تَحويلٌ صامِتٌ إلى الدِراسَة كَما
/// كانَ لِلحَقلِ الفارِغ، والاستِمارَةُ لا تُرسِلُ غَيرَ العَشَرَة؛ فَسِجِلُّ خَرقٍ بِلا قارِئٍ تَجريدٌ قَبلَ مُستَهلِكِه (القاعِدَة ١).
/// <b>وشَرطُ ذلك</b>: يَومَ يُعرَضُ لِلمُستَخدِمِ سَبَبُ الرَفض، يَصيرُ <c>Validate</c> بِرَمزٍ وسِجِلِّ خَرقٍ عَلى شَكلِ القاعِدَة ٤.</para>
/// </summary>
public static class StudioRefineSection
{
    /// <summary>المَعجَمُ المُغلَق — عَشَرَةٌ لا حادِيَ عَشَرَ لَها، بِتَرتيبِ الاستِمارَة.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        "marketSizing", "customerSegments", "competitors", "revenueModel", "costStructure",
        "financialProjection", "risks", "lessonsFromFailures", "roadmap", "recommendedPattern",
    };

    /// <summary>أَمِن المَعجَم؟ — مُطابَقَةٌ حَرفِيَّةٌ (<c>Ordinal</c>)، و<c>null</c> والفارِغُ لَيسا مِنه.</summary>
    public static bool IsKnown(string? section)
        => section is not null && All.Contains(section, StringComparer.Ordinal);
}
