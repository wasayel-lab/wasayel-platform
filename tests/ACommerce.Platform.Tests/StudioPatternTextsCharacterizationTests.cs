using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «اليَوم» — نَصّا النَمَطِ اللَّذانِ يَعِدانِ بِما لا يوجَد (‏ADR-032 §٦‑ج الصَفّ 3(أ)(ب)) ═══
//
// **ما يُثَبَّتُ هُنا قَبلَ أَيِّ تَبديل** (القاعِدَة ٣):
//   (أ) مَرجِعُ القُدُراتِ الَّذي يُحقَنُ في مُوَجِّهِ التَحليلِ (‏`SaudiDataProvider.CapabilitiesMarkdown` — المَورِدُ المُضَمَّنُ
//       بِعَينِه، لا المِلَفُّ عَلى القُرص) يَقولُ لِلمُحَلِّلِ إنّ «مُنتَجاً واحِداً بِمورِّدٍ واحِد» مُضادٌّ لِنَمَطِ المَتجَر و«يَكفيه
//       نَمَطٌ أَبسَط» — ولا نَمَطَ أَبسَطَ في `KnownPatterns`؛ والمَتجَرُ المُفرَدُ حالَةٌ مِن المُتَعَدِّد (‏ADR-032 §٦).
//   (ب) مُخرَجُ `PatternMatcher` بِفُروعِه السَبعَةِ بايتاً بايتاً — والتَعليلُ في فَرعِ «تَبيعُ مُنتَجات» حَرفِيٌّ في `.cs`
//       ويُرسَمُ لِلمُستَخدِم: يُخَزَّنُ في `IncubatorSession.PatternReasoning` ويَرسُمُه `Admin/IncubatorWizard.razor`
//       ويُحقَنُ في رِسالَةِ التَحليل (‏`FeasibilityPromptBuilder.BuildUserMessage`).
//
// **والجَدوَلُ يَبقى أَخضَرَ بِلا تَعديلِ حَرفٍ عِندَ تَرحيلِ التَعليلِ إلى `ar.json`** — وهو بُرهانُ أَنّ التَرحيلَ بايتِيٌّ
// (القاعِدَة ١١). ثُمَّ يَنقَلِبُ صَفّا النَصَّينِ المَقصودَينِ بِأَسمائِهِما في كوميتِ الصِياغَة، ولا يُمَسُّ غَيرُهُما.

public class StudioPatternTextsCharacterizationTests
{
    /// <summary>السَطرُ القَديمُ بِحَرفِه كَما كانَ يَبلُغُ المُوَجِّه — قَبلَ صِياغَةِ ‏2026-10-13.</summary>
    internal const string CapabilitiesContraLineOld =
        "- **متى لا يُستخدم (مضاد)**: منتج واحد بمورّد واحد (متجر بسيط يكفيه نمط أبسط)؛ خدمات تحتاج جدولة معقّدة.";

    /// <summary>(‏2026-10-13، كوميتُ الصِياغَة — انقَلَبَ بِقَرارٍ مَرئِيّ: كانَ <c>Today_the_capabilities_reference_tells_the_analyst_…</c>
    /// يُثبِتُ السَطرَ القَديمَ حاضِراً، وصارَ يُثبِتُه غائِباً؛ والنَصُّ الجَديدُ مُثَبَّتٌ في <c>StudioPatternTextsTests</c>.)</summary>
    [Fact]
    public void The_capabilities_reference_no_longer_tells_the_analyst_a_one_vendor_store_needs_a_simpler_pattern()
    {
        var caps = new SaudiDataProvider().CapabilitiesMarkdown;
        Assert.True(caps.Length > 500, $"أَداةٌ عَمياء: المَورِدُ المُضَمَّنُ طولُه {caps.Length} — لَم يُقرَأ.");
        Assert.DoesNotContain(CapabilitiesContraLineOld, caps.Split('\n'));

        // ‏«نَمَطٌ أَبسَط» لا وُجودَ لَه: المَعجَمُ أَربَعَة، ومَقصِدُ «مَتجَرٍ يَبيعُ مُنتَجات» فيه واحِدٌ هُوَ `marketplace`.
        Assert.Equal(new[] { "marketplace", "classifieds", "rental", "ondemand" }, TenantFromAnalysisFactory.KnownPatterns);
    }

    /// <summary>فُروعُ المُطابِقِ السَبعَةُ (وفَرعُ «تَبيعُ مُنتَجات» بِثِقَتَيه) — النَمَطُ والتَسمِيَةُ والثِقَةُ والتَعليلُ بِحَرفِها.
    /// (‏2026-10-13، كوميتُ الصِياغَة: صَفّا «تَبيعُ مُنتَجات» وَحدَهُما انقَلَبا إلى النَصِّ الجَديد — كانَ «… يناسب البيع المباشر أو
    /// من بائعين متعددين.»؛ والصُفوفُ السِتَّةُ الأُخرى لَم يُمَسَّ مِنها حَرف.)
    /// (‏2026-10-24، صِياغَةُ «ب» S11·٥ البَند 5: صَفُّ «twosided/commission» وَحدَه انقَلَبَ إلى النَصِّ الجَديد — كانَ «… نمط المتجر
    /// متعدد البائعين يحصّل الدفع ويوزّعه.»، والنَصُّ القَديمُ بِحَرفِه في <c>CapabilitiesPromptMoneyCharacterizationTests.CommissionReasoningOld</c>؛
    /// والصُفوفُ السَبعَةُ الأُخرى لَم يُمَسَّ مِنها حَرف.)
    /// (‏2026-10-29، قَرارُ المالِك د21 (٢) و(٣): صَفّا العُمولَةِ وَحدَهُما انقَلَبا — «twosided/commission» ثِقَتُه «medium» لا «high» وتَعليلُه
    /// «ووسايل لا تقتطع» لا «والمنصة لا تقتطع»، و«digital/commission» ثِقَتُه «medium» لا «high» («متوسّطة حين يكون نموذج الإيراد عمولة»)؛
    /// والصُفوفُ السِتَّةُ الأُخرى لَم يُمَسَّ مِنها حَرف.)</summary>
    [Theory]
    [InlineData("physical", "oneoff", "yes", "ondemand", "الطلبات حسب الموقع", "high", "اخترت خدمة فورية مرتبطة بموقع المزوّد — هذا نمط المطابقة الفورية (راكب⇄سائق، طلب⇄مندوب).")]
    [InlineData("rental", "oneoff", "no", "rental", "التأجير", "high", "فكرتك تأجير أصول لفترة محددة — نمط التأجير يدير حالة الأصل (متاح/محجوز/مُرجَع) والتحقق.")]
    [InlineData("twosided", "commission", "no", "marketplace", "المتجر متعدد البائعين", "medium", "تربط بائعين ومشترين وتأخذ عمولة على المعاملة — نمط المتجر متعدد البائعين يجمعهم في متجر واحد، والدفع يصل حساب صاحب المتجر بمفتاح ميسر الخاص به أو نقداً عند الاستلام؛ ووسايل لا تقتطع عمولة ولا توزّع المال.")]
    [InlineData("twosided", "subscription", "no", "classifieds", "الإعلانات المبوّبة", "medium", "تربط طرفين دون تحصيل دفع مركزي — نمط الإعلانات المبوّبة، الإيراد من اشتراك المعلنين أو ترقية الإعلان.")]
    [InlineData("physical", "oneoff", "no", "marketplace", "المتجر متعدد البائعين", "medium", "تبيع منتجات — نمط المتجر نفسه يعمل ببائع واحد (أنت) أو ببائعين متعددين يسجّلون فيه، بلا تبديل نمط.")]
    [InlineData("digital", "commission", "no", "marketplace", "المتجر متعدد البائعين", "medium", "تبيع منتجات — نمط المتجر نفسه يعمل ببائع واحد (أنت) أو ببائعين متعددين يسجّلون فيه، بلا تبديل نمط.")]
    [InlineData("service", "oneoff", "no", "classifieds", "الإعلانات المبوّبة", "medium", "تقدّم خدمة غير فورية — يمكن البدء بإعلانات مبوّبة لمزوّدي الخدمة ثم التوسّع للحجز.")]
    [InlineData("", "", "", "custom", "تكوين مخصّص", "low", "فكرتك لا تطابق نمطاً جاهزاً بوضوح — سنبني تكويناً مخصّصاً فوق نفس المحرّك بعد التحليل.")]
    public void PatternMatcher_outcomes_are_pinned_byte_for_byte(
        string offer, string payment, string realtime,
        string pattern, string label, string confidence, string reasoning)
    {
        var answers = new Dictionary<string, string>();
        if (offer.Length > 0) answers["offer"] = offer;
        if (payment.Length > 0) answers["payment"] = payment;
        if (realtime.Length > 0) answers["realtime"] = realtime;

        var s = PatternMatcher.Match(answers);

        Assert.Equal(pattern, s.Pattern);
        Assert.Equal(label, s.LabelAr);
        Assert.Equal(confidence, s.Confidence);
        Assert.Equal(reasoning, s.ReasoningAr);
    }
}
