namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>سؤال اكتشاف واحد. Options فارغة = حقل نصّي حر.</summary>
public sealed record DiscoveryQuestion(
    string Id,
    string QuestionAr,
    string Type,                       // single | multi | text
    IReadOnlyList<DiscoveryOption> Options);

public sealed record DiscoveryOption(string Value, string LabelAr);

/// <summary>
/// بنك أسئلة الاكتشاف — ثابت ومرتّب. أسئلة قليلة مركّزة (لا 15 سؤالاً
/// مرهقاً) تكفي PatternMatcher لاقتراح نمط. السؤال الأخير وصف حر يُغذّي
/// التحليل بكلمات صاحب المشروع.
/// </summary>
public static class DiscoveryQuestionBank
{
    public static readonly IReadOnlyList<DiscoveryQuestion> Questions = new[]
    {
        new DiscoveryQuestion("sector", "في أي قطاع فكرتك؟", "single", new[]
        {
            new DiscoveryOption("ecommerce", "تجارة إلكترونية / بيع منتجات"),
            new DiscoveryOption("fnb",        "أغذية ومشروبات / مطاعم"),
            new DiscoveryOption("realestate", "عقار / إيجار"),
            new DiscoveryOption("services",   "خدمات (حرفية، منزلية، مهنية)"),
            new DiscoveryOption("transport",  "نقل / توصيل"),
            new DiscoveryOption("other",      "غير ذلك"),
        }),
        new DiscoveryQuestion("customer", "من عميلك المستهدف؟", "single", new[]
        {
            new DiscoveryOption("b2c",  "أفراد"),
            new DiscoveryOption("b2b",  "شركات صغيرة"),
            new DiscoveryOption("b2b_large", "شركات كبيرة / حكومة"),
        }),
        new DiscoveryQuestion("offer", "ماذا تقدّم أساساً؟", "single", new[]
        {
            new DiscoveryOption("physical", "منتج مادي"),
            new DiscoveryOption("service",  "خدمة"),
            new DiscoveryOption("digital",  "محتوى رقمي"),
            new DiscoveryOption("twosided", "منصة تربط طرفين"),
            new DiscoveryOption("rental",   "تأجير أصول"),
        }),
        new DiscoveryQuestion("payment", "كيف يدفع العميل؟", "single", new[]
        {
            new DiscoveryOption("oneoff",     "مرة واحدة"),
            new DiscoveryOption("subscription","اشتراك دوري"),
            new DiscoveryOption("commission", "عمولة على معاملة"),
        }),
        new DiscoveryQuestion("geo", "ما النطاق الجغرافي؟", "single", new[]
        {
            new DiscoveryOption("city",      "مدينة محددة"),
            new DiscoveryOption("national",  "كامل المملكة"),
            new DiscoveryOption("global",    "عالمي"),
        }),
        new DiscoveryQuestion("realtime", "هل الخدمة فورية ومرتبطة بالموقع؟", "single", new[]
        {
            new DiscoveryOption("yes", "نعم (طلب فوري لمزوّد قريب)"),
            new DiscoveryOption("no",  "لا"),
        }),
        new DiscoveryQuestion("description",
            "صف فكرتك بكلماتك (ما المشكلة التي تحلّها؟ ومن يدفع؟)",
            "text", Array.Empty<DiscoveryOption>()),
    };

    public static DiscoveryQuestion? At(int index)
        => index >= 0 && index < Questions.Count ? Questions[index] : null;

    public static int Count => Questions.Count;
}
