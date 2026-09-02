using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

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
//
// ═══ شَرطُ الاستِخراجِ — القاعِدَة ١ داخِلَ المِلَفِّ نَفسِه ═══════════
//
// **دَينٌ مُعلَنٌ لا ادِّعاءُ إنجاز**: الكِتابَةُ لَها ثَلاثَةُ
// مُستَهلِكينَ أَحياءَ ومُثَبَّتَةٌ بِماسِح (`AgentService` ·
// `RefineSectionAsync` · `RunAnalysisAsync`)، أَمّا **القِراءَةُ
// التَجميعِيَّة** (`StudioTierService.ReadModelUsageAsync` و
// `ModelCallTotals` و`ModelPricingCatalog.All`) فَمُستَهلِكُها اليَومَ
// **الاختِبارُ وَحدَه** — وذلك خَرقٌ مُعلَنٌ لِلقاعِدَةِ ١ («التَجريدُ
// لا يَسبِقُ مُستَهلِكَه»)، مَكتوبٌ في ‏ADR-031 §٣ ومَكتوبٌ هُنا
// لِأَنّ القاعِدَةَ تَشتَرِطُ الاثنَين.
//
// **وشَرطُ سُقوطِ الدَين**: أَوَّلُ شاشَةٍ تَعرِضُ الإنفاقَ لِمالِكِ
// المَنَصَّةِ (لَوحُ الاستوديو) تَستَهلِكُ `ReadModelUsageAsync` —
// فَإن مَرَّت مَوجَةٌ ولَم تُبنَ تِلكَ الشاشَة، **تُحذَفُ القِراءَةُ
// التَجميعِيَّةُ** ويَبقى سَطرُ الكِتابَةِ وَحدَه؛ فَالخامُ
// المُخَزَّنُ يُقرَأُ بِاستِعلامٍ عِندَ الحاجَة، والتَجميعُ بِلا
// شاشَةٍ كودٌ مَيِّتٌ بِكامِلِ كِلفَتِه.

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

    /// <summary>
    /// <para><b>هَل قُرِئَ الاستِهلاكُ أَصلاً؟</b> <c>false</c> =
    /// «لَم يُقَس» (‏رَدُّ ‏401 بِلا جِسمٍ مَقروء، أَو استِثناءُ
    /// شَبَكَة)، و<c>true</c> = «قيسَ فَكانَ كَذا» ولَو كانَ الأَربَعَةُ
    /// أَصفاراً.</para>
    ///
    /// <para><b>ولِماذا حَقلٌ لا اشتِقاق</b>: بِلا هذا الحَقلِ
    /// يَنهارُ «لَم يُقَس» إلى «صِفر» — فَيُخَزَّنُ رَدٌّ أَعمى
    /// مُطابِقاً لِنِداءٍ صِفرِيٍّ حَقيقِيّ، ويُقَلِّلُ كُلُّ تَقريرٍ
    /// الفاتورَةَ دونَ أَن يَقول. وهو نَفسُ ثابِتِ
    /// <see cref="AgentUsage"/>: «<c>null</c> لا أَصفار».</para>
    /// </summary>
    public bool UsageMeasured { get; set; } = true;

    public DateTime AtUtc { get; set; } = Instant(DateTime.UtcNow);

    /// <summary>
    /// <para><b>وَحدَةُ الزَمَنِ الواحِدَةُ لِهذِه الوَثيقَة —
    /// <c>DateTimeKind.Unspecified</c> بِتَوقيتٍ عالَميّ.</b></para>
    ///
    /// <para><b>الكِلفَةُ الَّتي كَتَبَت هذا</b>: عَمودُ Marten
    /// لِحَقلِ <see cref="AtUtc"/> هو
    /// <c>timestamp without time zone</c>، وNpgsql <b>يَرفُضُ</b>
    /// وَسيطاً بِـ<c>Kind=Utc</c> عَلَيه
    /// (<c>ArgumentException: Cannot write DateTime with Kind=UTC…</c>).
    /// فَـ<c>ReadModelUsageAsync(userId, DateTime.UtcNow.AddDays(-30))</c>
    /// — وهُوَ ما يُمَرِّرُه <b>كُلُّ مُستَدعٍ طَبيعيّ</b> — كانَ
    /// يَرمي عَلى Postgres حَقيقيٍّ بَينَما الكِتابَةُ تَعمَل: أَي
    /// سِجِلٌّ يُملَأُ ولا يُقرَأ.</para>
    ///
    /// <para><b>والعِلاجُ وَحدَةٌ واحِدَةٌ عَلى الطَرَفَين</b>: تُطَبَّعُ
    /// اللَحظَةُ هُنا عِندَ الكِتابَةِ وتُطَبَّعُ حُدودُ المُدَّةِ عِندَ
    /// القِراءَة — فَلا يَبقى في المَسارِ <c>Kind</c> يَختَلِفُ عَنِ
    /// العَمود. و<c>Local</c> يُحَوَّلُ إلى عالَميٍّ أَوَّلاً، فَلا
    /// تُخَزَّنُ ساعَةُ جِهازٍ باسمِ ساعَةٍ عالَمِيَّة.</para>
    /// </summary>
    public static DateTime Instant(DateTime t) => t.Kind switch
    {
        DateTimeKind.Utc   => DateTime.SpecifyKind(t, DateTimeKind.Unspecified),
        DateTimeKind.Local => DateTime.SpecifyKind(t.ToUniversalTime(), DateTimeKind.Unspecified),
        _                  => t,
    };

    /// <summary>يَبني السَطرَ ويُسَعِّرُه. <paramref name="usage"/>
    /// <c>null</c> (‏رَدٌّ بِلا جِسمٍ مَقروء) يُعطي أَصفاراً
    /// <b>مَوسومَةً</b> بِـ<see cref="UsageMeasured"/> = <c>false</c> —
    /// والسَطرُ يُكتَبُ على كُلِّ حال، فَنِداءٌ وَقَعَ يُعَدّ.</summary>
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
            UsageMeasured    = usage is not null,
            CostUsd   = ModelPricingCatalog.CostUsd(model, usage),
            Success   = success,
            AtUtc     = Instant(atUtc ?? DateTime.UtcNow),
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

/// <summary>نَفسُ شَكلِ <c>ProviderDefinitionViolation</c> و
/// <c>RoleDefinitionViolation</c> حَرفاً (القاعِدَة ٤).</summary>
public sealed record ModelPriceViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>مُصادِقُ جَدوَلِ الأَسعار — سِتَّةُ رُموزِ خَرق، ولِكُلٍّ
/// اختِبارٌ موجِبٌ وسالِب</b> (القاعِدَة ٤).</para>
///
/// <para><b>ولِماذا مُصادِقٌ لا قارِئٌ مُتَساهِل</b>: القارِئُ
/// المُتَساهِلُ يَقرَأُ مِفتاحاً مَكتوباً خَطَأً («<c>cache_write</c>»
/// مَكانَ «<c>cacheWrite</c>») «لَم يُملَأ بَعد» — فَتَبقى الكِلفَةُ
/// <c>null</c> ويُقرَأُ العَطَبُ <b>تَأَخُّراً في التَسعير</b> لا
/// خَطَأً في المِلَفّ. وذلك بِعَينِه الشَكلُ الَّذي تَلتَزِمُه ثَمانِيَةُ
/// مُحَمِّلينَ في المُستَودَع: <c>UnmappedMemberHandling.Disallow</c>
/// + رُموزُ خَرقٍ مُعلَنَة.</para>
/// </summary>
public static class ModelPricingValidator
{
    /// <summary>الرُموزُ السِتَّة — مُعلَنَةً لِيُقاسَ أَنّ لِكُلٍّ
    /// اختِبارَين، لا لِتُقرَأَ في تَعليق.</summary>
    public static readonly IReadOnlyList<string> Codes = new[]
    {
        "currency_out_of_vocabulary",
        "unit_out_of_vocabulary",
        "models_empty",
        "model_key_blank",
        "price_not_positive",
        "priced_at_missing",
    };

    public const string Currency = "USD";
    public const string Unit     = "perMillionTokens";

    public static IReadOnlyList<ModelPriceViolation> Validate(
        string? currency, string? unit, IReadOnlyDictionary<string, ModelPrice> models)
    {
        var v = new List<ModelPriceViolation>();

        // العُملَةُ والوَحدَةُ لَيسَتا زينَة: الصيغَةُ في
        // `CostUsd` تَقسِمُ عَلى مِليونٍ وتُرجِعُ دولاراً — فَمِلَفٌّ
        // يَقولُ «رِيال» أَو «لِكُلِّ أَلف» يُنتِجُ رَقَماً خاطِئاً
        // بِصَمت، وهو أَسوَأُ مِن لا رَقَم.
        if (!string.Equals(currency, Currency, StringComparison.Ordinal))
            v.Add(new("currency_out_of_vocabulary",
                $"العُملَة «{currency}» خارِج المَعجَم — المُتَوَقَّع «{Currency}» "
                + "لِأَنّ `CostUsd` تُرجِعُ دولاراً."));

        if (!string.Equals(unit, Unit, StringComparison.Ordinal))
            v.Add(new("unit_out_of_vocabulary",
                $"الوَحدَة «{unit}» خارِج المَعجَم — المُتَوَقَّع «{Unit}» "
                + "لِأَنّ الصيغَة تَقسِم عَلى مِليون."));

        if (models.Count == 0)
            v.Add(new("models_empty", "جَدوَل الأَسعار بِلا نَموذَجٍ واحِد."));

        foreach (var (model, p) in models)
        {
            if (string.IsNullOrWhiteSpace(model))
                v.Add(new("model_key_blank", "مِفتاح نَموذَجٍ فارِغ في الجَدوَل."));

            var filled = 0;
            foreach (var (field, value) in new (string, decimal?)[]
            {
                ("input",      p.InputPerMillionUsd),
                ("output",     p.OutputPerMillionUsd),
                ("cacheWrite", p.CacheWritePerMillionUsd),
                ("cacheRead",  p.CacheReadPerMillionUsd),
            })
            {
                if (value is null) continue;
                filled++;

                // صِفرٌ مَمنوع: يُقرَأُ «مَجّانيّ» فَيُطمئِنُ كَذِباً
                // (القاعِدَة ١٦).
                if (value <= 0m)
                    v.Add(new("price_not_positive",
                        $"سِعرٌ غَيرُ مُوجَبٍ في «{model}.{field}» = {value} — الفارِغُ `null` لا صِفر."));
            }

            // وسِعرٌ بِلا تاريخِ قِراءَةٍ لا يُدقَّق: الأَسعارُ
            // تَتَغَيَّرُ بِقَرارِ المُزَوِّد، ورَقَمٌ بِلا تاريخٍ لا
            // يُعرَفُ أَقَديمٌ هُوَ أَم جَديد.
            if (filled > 0 && string.IsNullOrWhiteSpace(p.PricedAtUtc))
                v.Add(new("priced_at_missing",
                    $"«{model}» فيه سِعرٌ مَملوءٌ ولا `pricedAtUtc` — رَقَمٌ لا يُدقَّق."));
        }

        return v;
    }
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

    // ─── القِراءَةُ الصارِمَة — شَكلُ عائِلَةِ التَعريفاتِ حَرفاً ────

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        // مِفتاحٌ مَجهولٌ في مِلَفِّ بَياناتٍ = خَطَأٌ صَريحٌ لا تَجاهُلٌ
        // صامِت. وهذا هُوَ العَطَبُ بِعَينِه: «cache_write» مَكانَ
        // «cacheWrite» كانَ يُقرَأُ «لَم يُملَأ».
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    private sealed record PriceEntry
    {
        public decimal? Input { get; init; }
        public decimal? Output { get; init; }
        public decimal? CacheWrite { get; init; }
        public decimal? CacheRead { get; init; }
        public string? PricedAtUtc { get; init; }
    }

    private sealed record PricingFile
    {
        public IReadOnlyList<string> Note { get; init; } = [];
        public string? Currency { get; init; }
        public string? Unit { get; init; }
        public IReadOnlyDictionary<string, PriceEntry> Models { get; init; }
            = new Dictionary<string, PriceEntry>(StringComparer.Ordinal);
    }

    /// <summary>
    /// <para><b>يُحَلِّلُ ويُصادِقُ ويَرمي بِرَمزِ الخَرق</b> — بِلا
    /// ابتِلاع. وهذا هُوَ المَدخَلُ الَّذي يَستَعمِلُه الفَحصُ، وهو
    /// نَفسُ عَقدِ <c>ProviderDefinitionLoader.ParseDefinition</c>.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, ModelPrice> Parse(string json)
    {
        var file = JsonSerializer.Deserialize<PricingFile>(json, Options)
            ?? throw new InvalidOperationException("جَدوَل الأَسعار أَعطى null.");

        var map = new Dictionary<string, ModelPrice>(StringComparer.Ordinal);
        foreach (var (name, e) in file.Models)
            map[name] = new ModelPrice(
                e.Input, e.Output, e.CacheWrite, e.CacheRead, e.PricedAtUtc);

        var violations = ModelPricingValidator.Validate(file.Currency, file.Unit, map);
        if (violations.Count > 0)
            throw new InvalidOperationException(
                "جَدوَل أَسعار النَماذِج لا يَجتاز المُصادَقَة: "
                + string.Join(" | ", violations.Select(v => $"{v.Code}: {v.MessageAr}")));

        return map;
    }

    /// <summary>
    /// <para><b>والتَحميلُ لا يَكسِرُ المَسار</b> (القاعِدَة ٧ في
    /// التَكليف، وحُجَّةُ «القياسُ مُراقِبٌ لا حارِس» نَفسُها): مِلَفٌّ
    /// فاسِدٌ يُعطي جَدوَلاً <b>فارِغاً</b> فَتَصيرُ كُلُّ كِلفَةٍ
    /// <c>null</c> «غَيرَ مَعروفَة» — ولا يُنتِجُ رَقَماً خاطِئاً
    /// أَبَداً — ويُطبَعُ التَحذيرُ مَسموعاً. أَمّا الخَطَأُ نَفسُه
    /// فَيُمسَكُ في الفَحصِ عَبرَ <see cref="Parse"/> الَّذي يَرمي.</para>
    /// </summary>
    private static IReadOnlyDictionary<string, ModelPrice> Load()
    {
        try
        {
            using var stream = typeof(ModelPricingCatalog).Assembly
                .GetManifestResourceStream(Resource)
                ?? throw new InvalidOperationException(
                    $"المَورِد «{Resource}» غَير مُضَمَّن في المُجَمَّعَة.");

            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[metering] تَعَذَّرَت قِراءَةُ جَدوَلِ أَسعارِ النَماذِج — "
              + $"كُلُّ كِلفَةٍ تَبقى «غَيرَ مَعروفَة»: {ex.Message}");
            return new Dictionary<string, ModelPrice>(StringComparer.Ordinal);
        }
    }
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
    decimal CostUsd, int UncostedCalls, int UnmeasuredCalls)
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
            UncostedCalls:    list.Count(l => l.CostUsd is null),
            // «لَم يُقَس» عَدَدٌ مُستَقِلٌّ عَن «لَم يُسَعَّر»:
            // الأَوَّلُ نَقصٌ في التوكناتِ نَفسِها، والثاني نَقصٌ في
            // السِعر. وتَقريرٌ يَخلِطُهُما يُقَلِّلُ الفاتورَةَ
            // مَرَّتَين.
            UnmeasuredCalls:  list.Count(l => !l.UsageMeasured));
    }
}
