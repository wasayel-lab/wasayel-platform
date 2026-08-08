namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>
/// سياسَة الـ Deal — تُحَدِّد لِكُلّ نَمَط: المَراحِل المُستَخدَمَة،
/// مَن يَملِك الإجراء في كُلّ مَرحَلَة، والمَرحَلَة التالِيَة.
/// تَحكُم state machine في <see cref="DealsService"/>.
/// </summary>
public static class DealsPolicy
{
    /// <summary>المَراحِل المُتَتالِيَة لِكُلّ نَمَط.</summary>
    public static IReadOnlyList<DealStage> StagesFor(string pattern) => pattern switch
    {
        "trip" => new[]
        {
            // راكِب يَنشُر طَلَب → سائِق يَقبَل → بَدء الرَّحلَة (Confirmed) →
            // إكمال (Delivered) → تَقييم.
            DealStage.Offered, DealStage.Booked, DealStage.Confirmed,
            DealStage.Delivered, DealStage.Reviewed
        },
        "rental" => new[]
        {
            // مُستَأجِر يَطلُب → مالِك يَقبَل → دَفع تَأمين → تَسليم →
            // إعادَة (Received) → تَقييم.
            DealStage.Offered, DealStage.Booked, DealStage.Confirmed,
            DealStage.Paid, DealStage.Delivered, DealStage.Received,
            DealStage.Reviewed
        },
        "marketplace" => new[]
        {
            // مُشتَري يَطلُب → بائِع يُؤَكِّد → دَفع → شَحن →
            // استِلام → تَقييم.
            DealStage.Offered, DealStage.Booked, DealStage.Confirmed,
            DealStage.Paid, DealStage.Shipping, DealStage.Delivered,
            DealStage.Reviewed
        },
        "classifieds" => new[]
        {
            // إعلانات بِلا دَفع داخِليّ — اِنتَهَت دَورَة عِندَ الاتِّصال.
            DealStage.Offered, DealStage.Booked, DealStage.Confirmed
        },
        "service" => new[]
        {
            // خَدَمَة: طَلَب → عَرض سِعر → تَأكيد → دَفع → تَأديَة (Delivered)
            // → تَقييم.
            DealStage.Offered, DealStage.Booked, DealStage.Confirmed,
            DealStage.Paid, DealStage.Delivered, DealStage.Reviewed
        },
        _ => new[]
        {
            DealStage.Offered, DealStage.Booked, DealStage.Confirmed,
            DealStage.Reviewed
        }
    };

    /// <summary>المَرحَلَة التالِيَة في النَّمَط (null لَو وَصَلنا النِهايَة).</summary>
    public static DealStage? Next(string pattern, DealStage current)
    {
        var stages = StagesFor(pattern);
        var idx = stages.ToList().IndexOf(current);
        if (idx < 0 || idx >= stages.Count - 1) return null;
        return stages[idx + 1];
    }

    /// <summary>أَدوار اللاعِبين في كُلّ مَرحَلَة — مَن يُحَرِّك الإجراء؟
    /// "initiator" = صاحِب الطَّلَب (مُشتَري/راكِب/مُستَأجِر).
    /// "counterparty" = الطَّرَف الآخَر (بائِع/سائِق/مالِك).
    /// "platform" = المَنصَّة (إجراء آليّ مَثَلاً webhook دَفع).
    /// "either" = أَيّ مِن الطَّرَفَين.</summary>
    public static string Actor(DealStage stage) => stage switch
    {
        DealStage.Offered    => "initiator",
        DealStage.Booked     => "counterparty",
        DealStage.Confirmed  => "either",
        DealStage.Paid       => "initiator",
        DealStage.Shipping   => "counterparty",
        DealStage.Delivered  => "counterparty",
        DealStage.Received   => "initiator",
        DealStage.Reviewed   => "either",
        _ => "platform"
    };

    /// <summary>تَسمِيَة المَرحَلَة — تُعرَض في الواجِهات.</summary>
    public static string LabelAr(DealStage s) => s switch
    {
        DealStage.Offered    => "عَرض/طَلَب",
        DealStage.Booked     => "حَجز",
        DealStage.Confirmed  => "تَأكيد",
        DealStage.Paid       => "دَفع",
        DealStage.Shipping   => "شَحن",
        DealStage.Delivered  => "تَسليم",
        DealStage.Received   => "استِلام",
        DealStage.Reviewed   => "تَقييم",
        _ => s.ToString()
    };
}
