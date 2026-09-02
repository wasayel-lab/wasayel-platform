using System.Reflection;
using System.Text.Json;

namespace ACommerce.Templates.Customer.Marketplace.Services.Metering;

// ═══ قياسُ استِهلاكِ نَماذِجِ اللُغَة ═══════════════════════════════════
//
// **العِلَّةُ المَقيسَة (‏2026-09-02)**: الحُدودُ في `TierCatalog` تَعُدُّ
// **عَمَلِيّات** («تَحليل» · «تَحسين» · «مَتجَر»)، والفاتورَةُ عِندَ
// المُزَوِّدِ تُحسَبُ **بِالتوكن**. فَتَحليلٌ يَرُدُّ ‏400 توكن
// وتَحليلٌ يَرُدُّ ‏8000 يُعَدّانِ سَواءً عِندَنا ويُفَوتَرانِ بِعِشرينَ
// ضِعفاً عِندَه. وحَقلُ `usage` كانَ يَعودُ في رَدِّ **كُلٍّ** مِنَ
// الخَلفِيّاتِ الثَلاثِ و**لا يُقرَأُ في واحِدَةٍ مِنها** — أَي أَنّ
// الرَقَمَ يَصِلُ ويُرمى.
//
// **والوَحدَةُ المُخَزَّنَةُ توكناتٌ وكِلفَةٌ مَعاً**: التوكناتُ
// الأَربَعَةُ **خامٌ** لِأَنَّها الحَقيقَةُ الَّتي لا تَتَغَيَّر،
// والكِلفَةُ مَحسوبَةٌ مِن **جَدوَلٍ في مِلَفِّ بَيانات** لِأَنّ السِعرَ
// يُغَيِّرُه المُزَوِّدُ لا نَحن. سِعرٌ مَحبوسٌ في كودٍ يَحتاجُ بِناءً
// ونَشراً لِيُصَحَّح، وسَطرٌ خامٌ بِلا سِعرٍ يُسَعَّرُ لاحِقاً بِأَثَرٍ
// رَجعيّ — فَالخامُ يَبقى والسِعرُ يَتَحَرَّك.

/// <summary>
/// <para><b>نَوعُ العَمَلِيَّةِ الَّتي أَنفَقَتِ النِداء — مَعجَمٌ
/// مُغلَقٌ بِثَلاثٍ لا رابِعَ لَها</b>، وهي بِعَينِها الأَبوابُ
/// الثَلاثَةُ الَّتي يُثَبِّتُها
/// <c>LanguageModelQuotaGateTests.LlmReachingMethods</c>: تَحليلُ
/// الجَدوى، وتَحسينُ قِسمٍ مِنها، ومُحادَثَةُ وَكيلِ البِناء.</para>
///
/// <para><b>ولا يُعادُ استِعمالُ <c>StudioUpgradeReason</c></b> ولَو
/// تَشابَهَتِ السَلاسِل: ذاكَ مَعجَمُ <b>سَبَبِ دَعوَةِ تَرقِيَة</b>
/// وقيَمُه قيَمُ <c>?upgrade=</c> في العُنوان، وهذا مَعجَمُ <b>نَوعِ
/// إنفاق</b>. مَعجَمانِ يَتَشابَهانِ اليَومَ ويَنفَصِلانِ غَداً، ودَمجُهُما
/// يَجعَلُ تَعديلَ لافِتَةٍ في الشاشَةِ يُعيدُ تَسمِيَةَ صُفوفٍ في
/// سِجِلِّ الفاتورَة.</para>
/// </summary>
public static class ModelCallOperation
{
    /// <summary>دِراسَةُ الجَدوى — <c>FeasibilityAnalysisService.RunAnalysisAsync</c>.</summary>
    public const string Analyze = "analyze";

    /// <summary>إعادَةُ تَوليدِ قِسم — <c>RefineSectionAsync</c>.</summary>
    public const string Refine  = "refine";

    /// <summary>مُحادَثَةُ وَكيلِ البِناء — <c>AgentService</c>.</summary>
    public const string Build   = "build";

    public static readonly IReadOnlyList<string> All = new[] { Analyze, Refine, Build };

    public static bool IsKnown(string? op)
        => op is not null && All.Contains(op, StringComparer.Ordinal);
}

/// <summary>
/// <para><b>سَطرُ نِداءٍ واحِدٍ إلى نَموذَجِ لُغَة — ناجِحاً كانَ أَو
/// فاشِلاً.</b> وَثيقَةُ Marten تُكتَبُ في إيجارِ الاستوديو
/// (<c>StudioAuth.Tenant</c>) حَيثُ تَعيشُ عَدّاداتُ
/// <c>StudioUser</c>، فَلا جَدوَلَ ثانٍ لِنَفسِ السُؤال.</para>
///
/// <para><b>والفَشَلُ لا يُسقِطُ السَطر</b>: المُحاوَلَةُ الفاشِلَةُ
/// تَستَهلِكُ توكناتٍ فِعلاً (المُزَوِّدُ قَرَأَ الطَلَبَ وقَد يَكونُ
/// وَلَّدَ رَدّاً غَيرَ صالِح)، و<c>RunAnalysisAsync</c> يُحاوِلُ
/// <b>مَرَّتَين</b> — فَسِجِلٌّ يُسقِطُ الفاشِلَةَ يُخفي حَتّى نِصفَ
/// الإنفاق.</para>
///
/// <para><b>وما لا يُخزَّنُ عَمداً: نَصُّ الطَلَبِ ونَصُّ الرَدّ.</b>
/// تَخزينُ المُحتَوى قَرارُ خُصوصِيَّةٍ لَم يُتَّخَذ، و«نَحفَظُه الآنَ
/// ونُقَرِّرُ لاحِقاً» يَتَّخِذُ القَرارَ بِالأَمرِ الواقِع. والقياسُ
/// يَحتاجُ <b>عَدَداً</b> لا نَصّاً — ولِذلك لا حَقلَ نَصِّيّاً هُنا
/// خارِجَ الأَربَعَةِ المُعَرَّفَة، ويَحرُسُ ذلك
/// <c>ModelUsageMeteringTests.The_line_has_nowhere_to_hold_the_prompt_or_the_answer</c>.</para>
/// </summary>
public sealed class ModelCallRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>الإيجارُ الَّذي وَقَعَ فيه الإنفاق: <c>_incubator</c>
    /// لِلتَحليلِ والتَحسين، <c>_admin</c> لِمُحادَثَةِ الوَكيل.</summary>
    public string TenantId { get; set; } = "";

    /// <summary>صاحِبُ الجَلسَة، أَو <c>null</c> لِجَلسَةِ مُشرِفِ
    /// المَنَصَّةِ المُشتَرَكَة. و<c>null</c> لا <c>Guid.Empty</c>: «لا
    /// مُستَخدِمَ» لَيسَ «المُستَخدِمُ الصِفر».</summary>
    public Guid? UserId { get; set; }

    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";

    /// <summary>مِن <see cref="ModelCallOperation"/>.</summary>
    public string Operation { get; set; } = "";

    // ─── التوكناتُ الأَربَعَةُ الخام، **مُتَبايِنَة** ────────────────
    // لا تَتَقاطَع: مَجموعُ ما دَخَلَ = `InputTokens + CacheWriteTokens
    // + CacheReadTokens`. وتَطبيعُ أَشكالِ المُزَوِّدينَ إلى هذِه
    // الوَحدَةِ يَقَعُ في `AgentBackends.ReadUsage` لِكُلِّ خَلفِيَّة.
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }

    /// <summary>كِتابَةُ الكاش — <b>أَغلى</b> مِنَ المُدخَلِ العادِيّ
    /// عِندَ أَنثروبيك.</summary>
    public int CacheWriteTokens { get; set; }

    /// <summary>قِراءَةُ الكاش — <b>أَرخَصُ</b> مِنَ المُدخَلِ بِكَثير،
    /// وهي الَّتي يُشتَرى بِها الخَصمُ الَّذي فُعِّلَ
    /// <c>cache_control</c> لِأَجلِه.</summary>
    public int CacheReadTokens { get; set; }

    /// <summary><c>null</c> = «غَيرُ مَعروفَة» لا «مَجّانيّ» — نَموذَجٌ
    /// خارِجَ الجَدوَلِ أَو سِعرٌ لَم يُملَأ بَعد.</summary>
    public decimal? CostUsd { get; set; }

    public bool Success { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>يَبني السَطرَ ويُسَعِّرُه. <paramref name="usage"/>
    /// <c>null</c> (‏رَدٌّ بِلا جِسمٍ مَقروء) يُعطي أَصفاراً — والسَطرُ
    /// يُكتَبُ على كُلِّ حال، فَنِداءٌ وَقَعَ يُعَدّ.</summary>
    public static ModelCallRecord For(
        string tenantId, Guid? userId, string provider, string model,
        string operation, AgentUsage? usage, bool success, DateTime? atUtc = null)
        => new()
        {
            TenantId  = tenantId,
            UserId    = userId,
            Provider  = provider,
            Model     = model,
            Operation = operation,
            InputTokens      = usage?.InputTokens      ?? 0,
            OutputTokens     = usage?.OutputTokens     ?? 0,
            CacheWriteTokens = usage?.CacheWriteTokens ?? 0,
            CacheReadTokens  = usage?.CacheReadTokens  ?? 0,
            CostUsd   = ModelPricingCatalog.CostUsd(model, usage),
            Success   = success,
            AtUtc     = atUtc ?? DateTime.UtcNow,
        };
}

/// <summary>سِعرُ نَموذَجٍ واحِدٍ — أَربَعَةُ أَسعارٍ بِالدولارِ لِكُلِّ
/// مِليونِ توكن. و<c>null</c> = لَم يُملَأ.</summary>
public sealed record ModelPrice(
    decimal? InputPerMillionUsd,
    decimal? OutputPerMillionUsd,
    decimal? CacheWritePerMillionUsd,
    decimal? CacheReadPerMillionUsd,
    string? PricedAtUtc)
{
    /// <summary>أَتَكفي لِحِسابِ كِلفَةٍ صادِقَة؟ سِعرٌ ناقِصٌ واحِدٌ
    /// يَكفي لِتَبقى الكِلفَةُ «غَيرَ مَعروفَة» — وكِلفَةٌ مَبنِيَّةٌ
    /// على ثَلاثَةِ أَسعارٍ مِن أَربَعَةٍ تُقرَأُ رَقَماً كامِلاً وهي
    /// ناقِصَة.</summary>
    public bool IsComplete =>
        InputPerMillionUsd is not null && OutputPerMillionUsd is not null &&
        CacheWritePerMillionUsd is not null && CacheReadPerMillionUsd is not null;
}

/// <summary>
/// <para><b>جَدوَلُ الأَسعارِ — يُقرَأُ مِن <c>Data/model-pricing.json</c>
/// مَرَّةً واحِدَة</b> (القاعِدَة ٤: التَنَوُّعُ المُنتَهي يَصيرُ
/// بَيانات). المَفاتيحُ أَسماءُ النَماذِجِ الَّتي يَختارُها المُستَودَعُ
/// فِعلاً — <c>IAgentBackend.DefaultModel</c> في الخَلفِيّاتِ الثَلاث —
/// ويَحرُسُ ذلك
/// <c>ModelUsageMeteringTests.Every_default_model_the_repo_actually_selects_has_a_key_in_the_pricing_file</c>.</para>
/// </summary>
public static class ModelPricingCatalog
{
    private const string Resource =
        "ACommerce.Templates.Customer.Marketplace.Data.model-pricing.json";

    private static readonly Lazy<IReadOnlyDictionary<string, ModelPrice>> Loaded = new(Load);

    public static IReadOnlyDictionary<string, ModelPrice> All => Loaded.Value;

    public static ModelPrice? For(string? model)
        => model is not null && All.TryGetValue(model, out var p) ? p : null;

    /// <summary>كِلفَةُ نِداءٍ بِنَموذَجٍ مُسَمّى، أَو <c>null</c> إن لَم
    /// يُعرَف سِعرُه.</summary>
    public static decimal? CostUsd(string? model, AgentUsage? usage)
        => CostUsd(For(model), usage);

    /// <summary>
    /// <para><b>الصيغَة: أَربَعَةُ عَدّاداتٍ بِأَربَعَةِ أَسعار، لِكُلِّ
    /// مِليونِ توكن.</b> وهذا هُوَ سَبَبُ فَصلِ الكاشِ أَصلاً — سِعرٌ
    /// واحِدٌ لِلثَلاثَةِ يُلغي أَثَرَ <c>cache_control</c> حِسابِيّاً
    /// فَلا يُعرَفُ أَنَفَعَ أَم ضَرّ.</para>
    /// </summary>
    public static decimal? CostUsd(ModelPrice? price, AgentUsage? usage)
    {
        if (price is null || usage is null || !price.IsComplete) return null;
        const decimal million = 1_000_000m;
        return usage.InputTokens      / million * price.InputPerMillionUsd!.Value
             + usage.OutputTokens     / million * price.OutputPerMillionUsd!.Value
             + usage.CacheWriteTokens / million * price.CacheWritePerMillionUsd!.Value
             + usage.CacheReadTokens  / million * price.CacheReadPerMillionUsd!.Value;
    }

    private static IReadOnlyDictionary<string, ModelPrice> Load()
    {
        var map = new Dictionary<string, ModelPrice>(StringComparer.Ordinal);
        using var stream = typeof(ModelPricingCatalog).Assembly
            .GetManifestResourceStream(Resource);
        if (stream is null) return map;

        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("models", out var models)
            || models.ValueKind != JsonValueKind.Object) return map;

        foreach (var m in models.EnumerateObject())
            map[m.Name] = new ModelPrice(
                Money(m.Value, "input"),
                Money(m.Value, "output"),
                Money(m.Value, "cacheWrite"),
                Money(m.Value, "cacheRead"),
                m.Value.TryGetProperty("pricedAtUtc", out var at) && at.ValueKind == JsonValueKind.String
                    ? at.GetString() : null);
        return map;
    }

    /// <summary><c>null</c> يَبقى <c>null</c> — ولا يُقرَأُ صِفراً.</summary>
    private static decimal? Money(JsonElement obj, string key)
        => obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDecimal() : null;
}

/// <summary>
/// <para><b>مَجموعُ سُطورٍ — والأَربَعَةُ تَبقى مُنفَصِلَةً في
/// المَجموعِ أَيضاً.</b> و<see cref="UncostedCalls"/> لَيسَ حَشواً:
/// تَقريرٌ يَقولُ «‏12 دولاراً» عَن عَشرَةِ سُطورٍ نِصفُها بِلا سِعرٍ
/// <b>يَكذِب</b> — والعَدَدُ إلى جِوارِ المَبلَغِ يَقولُ كَم مِنه
/// مَحسوبٌ فِعلاً.</para>
/// </summary>
public sealed record ModelCallTotals(
    int Calls, int Failures,
    long InputTokens, long OutputTokens, long CacheWriteTokens, long CacheReadTokens,
    decimal CostUsd, int UncostedCalls)
{
    public static ModelCallTotals Of(IEnumerable<ModelCallRecord> lines)
    {
        var list = lines as IReadOnlyCollection<ModelCallRecord> ?? lines.ToArray();
        return new ModelCallTotals(
            Calls:            list.Count,
            Failures:         list.Count(l => !l.Success),
            InputTokens:      list.Sum(l => (long)l.InputTokens),
            OutputTokens:     list.Sum(l => (long)l.OutputTokens),
            CacheWriteTokens: list.Sum(l => (long)l.CacheWriteTokens),
            CacheReadTokens:  list.Sum(l => (long)l.CacheReadTokens),
            CostUsd:          list.Sum(l => l.CostUsd ?? 0m),
            UncostedCalls:    list.Count(l => l.CostUsd is null));
    }
}
