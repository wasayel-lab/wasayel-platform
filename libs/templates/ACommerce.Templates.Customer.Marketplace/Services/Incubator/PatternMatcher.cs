namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

public sealed record PatternSuggestion(
    string Pattern,        // marketplace | classifieds | rental | ondemand | custom
    string LabelAr,
    string Confidence,     // high | medium | low
    string ReasoningAr);

/// <summary>
/// مُطابِق الأنماط — قواعد منطقية (لا AI) تأخذ إجابات الاكتشاف وتقترح
/// نمط ACommerce المناسب. الترتيب مهم: القواعد الأكثر تحديداً أولاً.
/// </summary>
public static class PatternMatcher
{
    public static PatternSuggestion Match(IReadOnlyDictionary<string, string> a)
    {
        string Get(string k) => a.TryGetValue(k, out var v) ? v : "";

        var offer    = Get("offer");
        var payment  = Get("payment");
        var realtime = Get("realtime");

        // 1) طلب فوري مرتبط بالموقع → on-demand
        if (realtime == "yes")
            return new("ondemand", "الطلبات حسب الموقع",
                "high",
                "اخترت خدمة فورية مرتبطة بموقع المزوّد — هذا نمط المطابقة الفورية (راكب⇄سائق، طلب⇄مندوب).");

        // 2) تأجير أصول → rental
        if (offer == "rental")
            return new("rental", "التأجير",
                "high",
                "فكرتك تأجير أصول لفترة محددة — نمط التأجير يدير حالة الأصل (متاح/محجوز/مُرجَع) والتحقق.");

        // 3) منصة طرفين بعمولة → marketplace
        if (offer == "twosided" && payment == "commission")
            return new("marketplace", "المتجر متعدد البائعين",
                "high",
                "تربط بائعين ومشترين وتأخذ عمولة على المعاملة — نمط المتجر متعدد البائعين يحصّل الدفع ويوزّعه.");

        // 4) منصة طرفين باشتراك/بلا تحصيل → classifieds
        if (offer == "twosided")
            return new("classifieds", "الإعلانات المبوّبة",
                "medium",
                "تربط طرفين دون تحصيل دفع مركزي — نمط الإعلانات المبوّبة، الإيراد من اشتراك المعلنين أو ترقية الإعلان.");

        // 5) منتج مادي/رقمي يُباع → marketplace أو متجر
        if (offer is "physical" or "digital")
            return new("marketplace", "المتجر متعدد البائعين",
                payment == "commission" ? "high" : "medium",
                "تبيع منتجات — نمط المتجر يناسب البيع المباشر أو من بائعين متعددين.");

        // 6) خدمة غير فورية → classifieds (تجميع مزوّدين)
        if (offer == "service")
            return new("classifieds", "الإعلانات المبوّبة",
                "medium",
                "تقدّم خدمة غير فورية — يمكن البدء بإعلانات مبوّبة لمزوّدي الخدمة ثم التوسّع للحجز.");

        // افتراضي
        return new("custom", "تكوين مخصّص",
            "low",
            "فكرتك لا تطابق نمطاً جاهزاً بوضوح — سنبني تكويناً مخصّصاً فوق نفس المحرّك بعد التحليل.");
    }
}
