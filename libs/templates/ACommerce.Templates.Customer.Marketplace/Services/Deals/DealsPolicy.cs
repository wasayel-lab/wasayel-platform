namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>
/// <para>سياسَة الـ Deal — تُحَدِّد لِكُلّ نَمَط: المَراحِل المُستَخدَمَة،
/// مَن يَملِك الإجراء في كُلّ مَرحَلَة، والمَرحَلَة التالِيَة.
/// تَحكُم state machine في <see cref="DealsService"/>.</para>
///
/// <para><b>واجِهَة، لا بَيانات.</b> كانَ هذا الصَّنف يَحمِل الأَنماط
/// الخَمسَة في <c>switch</c> مُجَمَّع؛ صارَت بَيانات في
/// <see cref="DealPatternCatalog"/> (META-MODEL §8، TESTING-PROTOCOL §5
/// الخُطوَة 4). التَّواقيع الأَربَعَة لَم تَتَغَيَّر حَرفيّاً، فَلا
/// مُستَهلِك واحِد احتاجَ تَعديلاً، و<see cref="DealsService"/> لَم
/// يُمَسّ. التَّطابُق مُبرهَن بِـ
/// <c>DealsPolicyCharacterizationTests</c>: كُتِبَ عَلى الـ switch
/// واخضَرَّ، ولَم يُمَسّ سَطر مِنه عِندَ الرَّفع.</para>
///
/// <para>حينَ يَنتَقِل الكاتالوج إلى تَخزين لِكُلّ مُستَأجِر، يَتَغَيَّر
/// مَصدَر القِراءَة وَحدَه — هذا السَّطح يَبقى كَما هو.</para>
/// </summary>
public static class DealsPolicy
{
    /// <summary>المَراحِل المُتَتالِيَة لِكُلّ نَمَط.</summary>
    public static IReadOnlyList<DealStage> StagesFor(string pattern)
        => DealPatternCatalog.For(pattern).StageOrder;

    /// <summary>المَرحَلَة التالِيَة في النَّمَط (null لَو وَصَلنا النِهايَة).</summary>
    public static DealStage? Next(string pattern, DealStage current)
        => DealPatternCatalog.For(pattern).Next(current);

    /// <summary>أَدوار اللاعِبين في كُلّ مَرحَلَة — مَن يُحَرِّك الإجراء؟
    /// "initiator" = صاحِب الطَّلَب (مُشتَري/راكِب/مُستَأجِر).
    /// "counterparty" = الطَّرَف الآخَر (بائِع/سائِق/مالِك).
    /// "platform" = المَنصَّة (إجراء آليّ مَثَلاً webhook دَفع).
    /// "either" = أَيّ مِن الطَّرَفَين.</summary>
    public static string Actor(DealStage stage)
        => DealPatternCatalog.ActorFor(stage);

    /// <summary>تَسمِيَة المَرحَلَة — تُعرَض في الواجِهات.</summary>
    public static string LabelAr(DealStage s)
        => DealPatternCatalog.LabelFor(s);
}
