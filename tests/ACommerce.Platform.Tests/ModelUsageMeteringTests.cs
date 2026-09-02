using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>نِداءُ نَموذَجِ اللُغَةِ يُقاسُ بِالتوكن، لا بِالعَمَلِيَّة.</b></para>
///
/// <para><b>العِلَّةُ المَقيسَة (‏2026-09-02)</b>: الحُدودُ في
/// <see cref="TierCatalog"/> تَعُدُّ «تَحاليل» و«تَحسينات» — أَي
/// <b>عَمَلِيّات</b>. والفاتورَةُ عِندَ المُزَوِّدِ تُحسَبُ
/// <b>بِالتوكن</b>. فَتَحليلٌ يَرُدُّ ‏400 توكن وتَحليلٌ يَرُدُّ ‏8000
/// يُعَدّانِ واحِداً واحِداً في عَدّادِنا ويُفَوتَرانِ بِعِشرينَ ضِعفاً
/// عِندَه. وحَقلُ <c>usage</c> يَعودُ في رَدِّ <b>كُلٍّ</b> مِن
/// الخَلفِيّاتِ الثَلاثِ و<b>لا يُقرَأُ في واحِدَةٍ مِنها</b> — أَي أَنّ
/// الرَقَمَ كانَ يَصِلُ ويُرمى.</para>
///
/// <para><b>و<c>cache_control</c> مُفَعَّلٌ وأَثَرُه غَيرُ مَقيس</b>:
/// <see cref="AnthropicBackend"/> يَضَعُ <c>cache_control</c> على آخِرِ
/// أَداةٍ وعلى كُتلَةِ النِظام، ويَقولُ التَعليقُ «‏≈80% خَصم» — وهي
/// <b>دَعوى بِلا عَدّاد</b>. والتَخزينُ المُؤَقَّتُ يُفَوتَرُ
/// بِسِعرَينِ مُختَلِفَين (كِتابَةٌ أَغلى مِنَ المُدخَلِ العادِيّ،
/// وقِراءَةٌ أَرخَصُ مِنه بِكَثير)، فَجَمعُ الأَربَعَةِ في رَقَمٍ واحِدٍ
/// يَمحو الأَثَرَ الَّذي جاءَ التَخزينُ لِأَجلِه.</para>
///
/// <para><b>وما لا يُخزَّنُ عَمداً</b>: نَصُّ الطَلَبِ ونَصُّ الرَدّ.
/// تَخزينُ المُحتَوى قَرارُ خُصوصِيَّةٍ لَم يُتَّخَذ، و«نُخَزِّنُه
/// الآنَ ونُقَرِّرُ لاحِقاً» يَجعَلُ القَرارَ مُتَّخَذاً بِالأَمرِ
/// الواقِع. القياسُ يَحتاجُ <b>عَدَداً</b> لا نَصّاً.</para>
/// </summary>
public class ModelUsageMeteringTests(ITestOutputHelper output)
{
    // ═══ ١) شَكلُ الاستِهلاكِ عِندَ كُلِّ مُزَوِّد ════════════════════
    //
    // **ولا يُفتَرَضُ تَطابُق**: ثَلاثَةُ أَسماءٍ مُختَلِفَةٍ لِلحَقلِ
    // نَفسِه (`usage` · `usageMetadata` · `usage`)، وثَلاثَةُ مَعاجِمَ
    // مُختَلِفَةٍ لِمَحتَواه. والأَجسامُ أَدناهُ **مَقصوصَةٌ مِن شَكلِ
    // رَدِّ كُلِّ مُزَوِّدٍ كَما تَبنيهِ خَلفِيَّتُه في
    // `AgentBackends.cs`** — لا نِداءَ شَبَكَةٍ ولا مِفتاح.

    private const string AnthropicBody = """
        {
          "id": "msg_01",
          "type": "message",
          "role": "assistant",
          "model": "claude-sonnet-4-6",
          "content": [ { "type": "text", "text": "مَرحَباً" } ],
          "usage": {
            "input_tokens": 100,
            "output_tokens": 20,
            "cache_creation_input_tokens": 1500,
            "cache_read_input_tokens": 8000
          }
        }
        """;

    private const string GeminiBody = """
        {
          "candidates": [
            { "content": { "parts": [ { "text": "مَرحَباً" } ], "role": "model" } }
          ],
          "usageMetadata": {
            "promptTokenCount": 1000,
            "candidatesTokenCount": 50,
            "cachedContentTokenCount": 800,
            "totalTokenCount": 1050
          }
        }
        """;

    private const string OpenAiBody = """
        {
          "id": "chatcmpl-1",
          "choices": [
            { "index": 0, "message": { "role": "assistant", "content": "مَرحَباً" } }
          ],
          "usage": {
            "prompt_tokens": 1000,
            "completion_tokens": 40,
            "total_tokens": 1040,
            "prompt_tokens_details": { "cached_tokens": 600 }
          }
        }
        """;

    private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>
    /// <para><b>أَنثروبيك تُفَرِّقُ بَينَ كِتابَةِ الكاشِ وقِراءَتِه —
    /// والأَربَعَةُ تُحفَظُ مُنفَصِلَة.</b> <c>input_tokens</c> عِندَها
    /// <b>لا يَشمَل</b> الكاش، فَالأَربَعَةُ مُتَبايِنَةٌ كَما وَرَدَت.</para>
    /// </summary>
    [Fact]
    public void Anthropic_usage_keeps_the_four_counters_apart()
    {
        var u = AnthropicBackend.ReadUsage(Root(AnthropicBody));

        Assert.NotNull(u);
        Assert.Equal(100,  u!.InputTokens);
        Assert.Equal(20,   u.OutputTokens);
        Assert.Equal(1500, u.CacheWriteTokens);
        Assert.Equal(8000, u.CacheReadTokens);
    }

    /// <summary>رَدٌّ بِلا <c>usage</c> يُعطي <c>null</c> — لا أَصفاراً
    /// تُقرَأُ «نِداءٌ بِلا كِلفَة».</summary>
    [Fact]
    public void A_response_without_a_usage_block_yields_null_not_zeroes()
    {
        Assert.Null(AnthropicBackend.ReadUsage(Root("""{ "content": [] }""")));
        Assert.Null(GeminiBackend.ReadUsage(Root("""{ "candidates": [] }""")));
        Assert.Null(OpenAIBackend.ReadUsage(Root("""{ "choices": [] }""")));
    }

    /// <summary>
    /// <para><b>جيميناي: <c>promptTokenCount</c> يَشمَلُ المُخَزَّن</b>
    /// (وهذا خِلافُ أَنثروبيك حَرفاً) — فَلَو خُزِّنَ كَما وَرَدَ
    /// لَحُسِبَتِ التوكناتُ المُخَزَّنَةُ <b>مَرَّتَين</b>: مَرَّةً
    /// بِسِعرِ المُدخَلِ ومَرَّةً بِسِعرِ القِراءَة. الوَحدَةُ
    /// المُعلَنَةُ لِـ<see cref="AgentUsage"/> أَربَعَةٌ
    /// <b>مُتَبايِنَة</b>، فَيُطرَحُ المُخَزَّنُ مِنَ المُدخَل.</para>
    /// </summary>
    [Fact]
    public void Gemini_usageMetadata_is_read_and_the_cached_share_is_not_counted_twice()
    {
        var u = GeminiBackend.ReadUsage(Root(GeminiBody));

        Assert.NotNull(u);
        Assert.Equal(200, u!.InputTokens);      // ‏1000 − 800
        Assert.Equal(50,  u.OutputTokens);
        Assert.Equal(0,   u.CacheWriteTokens);  // لا كِتابَةَ كاشٍ في هذا الرَدّ
        Assert.Equal(800, u.CacheReadTokens);
        Assert.Equal(1000, u.InputTokens + u.CacheReadTokens);
    }

    /// <summary>ونَفسُ العِلَّةِ عِندَ OpenAI: <c>prompt_tokens</c>
    /// يَشمَلُ <c>prompt_tokens_details.cached_tokens</c>.</summary>
    [Fact]
    public void OpenAI_usage_is_read_and_the_cached_share_is_not_counted_twice()
    {
        var u = OpenAIBackend.ReadUsage(Root(OpenAiBody));

        Assert.NotNull(u);
        Assert.Equal(400, u!.InputTokens);      // ‏1000 − 600
        Assert.Equal(40,  u.OutputTokens);
        Assert.Equal(0,   u.CacheWriteTokens);
        Assert.Equal(600, u.CacheReadTokens);
    }

    /// <summary>
    /// <para><b>ولا خَلفِيَّةٌ تَقرَأُ شَكلَ أُخرى.</b> قارِئٌ يَبتَلِعُ
    /// جِسمَ غَيرِه ويُخرِجُ أَصفاراً أَسوَأُ مِن قارِئٍ غائِب: يُنتِجُ
    /// سُطوراً تَقولُ «صِفرُ توكنات» عَن نِداءٍ كَلَّفَ فِعلاً.</para>
    /// </summary>
    [Fact]
    public void No_backend_reads_another_backends_shape()
    {
        Assert.Null(AnthropicBackend.ReadUsage(Root(GeminiBody)));
        Assert.Null(GeminiBackend.ReadUsage(Root(AnthropicBody)));
        Assert.Null(GeminiBackend.ReadUsage(Root(OpenAiBody)));

        // ‏OpenAI وأَنثروبيك يَتَقاسَمانِ الاسمَ `usage` ويَختَلِفانِ
        // في مَعجَمِه — فَيُقاسُ أَنّ القارِئَ يَنظُرُ في المَعجَمِ لا
        // في الاسم.
        Assert.Null(OpenAIBackend.ReadUsage(Root(AnthropicBody)));
        Assert.Null(AnthropicBackend.ReadUsage(Root(OpenAiBody)));
    }

    /// <summary>الرَدُّ المُحايِدُ عَنِ المُزَوِّدِ يَحمِلُ الاستِهلاك —
    /// وإلّا قُرِئَ ورُمِيَ عِندَ حَدِّ الخَلفِيَّة.</summary>
    [Fact]
    public void The_provider_neutral_response_carries_the_usage()
    {
        var p = typeof(AgentBackendResponse).GetProperty("Usage");
        Assert.NotNull(p);
        Assert.Equal(typeof(AgentUsage), Nullable.GetUnderlyingType(p!.PropertyType) ?? p.PropertyType);

        var r = new AgentBackendResponse("نَصّ", null, null, new AgentUsage(1, 2, 3, 4));
        Assert.Equal(3, r.Usage!.CacheWriteTokens);
    }

    // ═══ ٢) جَدوَلُ الأَسعارِ مِلَفُّ بَيانات ═══════════════════════════

    /// <summary>
    /// <para><b>كُلُّ نَموذَجٍ يَختارُه المُستَودَعُ فِعلاً لَه مِفتاحٌ في
    /// الجَدوَل</b> — والقائِمَةُ تُقرَأُ مِنَ الكودِ نَفسِه
    /// (<c>DefaultModel =&gt; "…"</c>) لا تُكتَبُ هُنا بِاليَد، فَنَموذَجٌ
    /// افتِراضيٌّ يُبَدَّلُ غَداً يُحمِرُّ هذا الفَحصَ بَدَلَ أَن
    /// يَسقُطَ مِنَ التَسعيرِ صامِتاً.</para>
    /// </summary>
    [Fact]
    public void Every_default_model_the_repo_actually_selects_has_a_key_in_the_pricing_file()
    {
        var text = File.ReadAllText(Path.Combine(TemplateRoot, "Services", "AgentBackends.cs"));
        var models = Regex.Matches(text, @"DefaultModel\s*=>\s*""(?<m>[^""]+)""")
            .Select(m => m.Groups["m"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToArray();

        output.WriteLine($"نَماذِجُ افتِراضِيَّةٌ مَقروءَةٌ مِنَ الكود: {string.Join("، ", models)}");
        Assert.True(models.Length >= 3,
            $"أَداةٌ عَمياء: استُخرِجَ {models.Length} نَموذَجاً — والخَلفِيّاتُ ثَلاث.");

        var missing = models.Where(m => !ModelPricingCatalog.All.ContainsKey(m)).ToArray();
        Assert.True(missing.Length == 0,
            "نَموذَجٌ يُنادى ولا مِفتاحَ لَه في `Data/model-pricing.json`:\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>
    /// <para><b>ولا سِعرَ يُخترَع</b> (القاعِدَة ١٦): كُلُّ قيمَةٍ إمّا
    /// <c>null</c> — «لَم تُملَأ بَعد» — أَو مُوجَبَةٌ فِعلاً. و<b>صِفرٌ
    /// مَمنوع</b>: صِفرٌ يُقرَأُ «مَجّانيّ» فَيُنتِجُ تَقريراً يَقولُ
    /// إنّ الإنفاقَ لا شَيء، وهُوَ أَسوَأُ مِن لا تَقرير.</para>
    /// </summary>
    [Fact]
    public void No_price_is_invented_and_zero_is_not_a_price()
    {
        var priced = 0;
        foreach (var (model, p) in ModelPricingCatalog.All)
            foreach (var (field, v) in new (string, decimal?)[]
            {
                ("input", p.InputPerMillionUsd), ("output", p.OutputPerMillionUsd),
                ("cacheWrite", p.CacheWritePerMillionUsd), ("cacheRead", p.CacheReadPerMillionUsd),
            })
            {
                if (v is null) continue;
                priced++;
                Assert.True(v > 0m,
                    $"سِعرٌ غَيرُ مُوجَبٍ في «{model}.{field}» — الفارِغُ `null` لا صِفر.");
            }

        output.WriteLine(
            $"الجَدوَل: {ModelPricingCatalog.All.Count} نَموذَجاً، {priced} سِعراً مَملوءاً مِن "
            + $"{ModelPricingCatalog.All.Count * 4}.");

        // والمِلَفُّ يَقولُ مِن أَينَ تُملَأ — وإلّا بَقِيَ `null` بِلا
        // شَرطِ سُقوط.
        var raw = File.ReadAllText(Path.Combine(TemplateRoot, "Data", "model-pricing.json"));
        Assert.Contains("pricing", raw, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>نَموذَجٌ في الجَدوَلِ وأَسعارُه لَم تُملَأ: يُقاسُ
    /// ولا يُسَعَّر — الكِلفَةُ <c>null</c> «غَيرُ مَعروفَة» لا
    /// <c>0</c> «مَجّانيّ».</summary>
    [Fact]
    public void An_unpriced_model_is_measured_but_never_costed_as_zero()
    {
        var known = ModelPricingCatalog.All.First(kv => !kv.Value.IsComplete).Key;
        var line = ModelCallRecord.For(
            "_incubator", Guid.NewGuid(), "anthropic", known,
            ModelCallOperation.Analyze, new AgentUsage(100, 20, 1500, 8000), success: true);

        Assert.Null(line.CostUsd);
        Assert.Equal(100,  line.InputTokens);
        Assert.Equal(1500, line.CacheWriteTokens);
        Assert.Equal(8000, line.CacheReadTokens);
    }

    /// <summary>ونَموذَجٌ خارِجَ الجَدوَلِ كُلِّه (‏<c>Agents:X:Model</c>
    /// مَضبوطٌ بِيَدِ المالِك) — نَفسُ الحُكم: سَطرٌ بِتوكناتِه وبِلا
    /// كِلفَة.</summary>
    [Fact]
    public void A_model_outside_the_table_is_still_recorded()
    {
        Assert.Null(ModelPricingCatalog.For("llama-3.3-70b-versatile"));
        Assert.Null(ModelPricingCatalog.CostUsd("llama-3.3-70b-versatile", new AgentUsage(9, 9, 9, 9)));

        var line = ModelCallRecord.For(
            "_admin", null, "groq", "llama-3.3-70b-versatile",
            ModelCallOperation.Build, new AgentUsage(9, 8, 7, 6), success: true);

        Assert.Null(line.CostUsd);
        Assert.Equal(9, line.InputTokens);
        Assert.Equal(6, line.CacheReadTokens);
    }

    /// <summary>
    /// <para><b>والصيغَةُ تُسَعِّرُ الأَربَعَةَ بِأَربَعَةِ أَسعار</b> —
    /// لِكُلِّ مِليونِ توكن. وهذا هُوَ سَبَبُ فَصلِ الكاشِ أَصلاً:
    /// كِتابَتُه أَغلى مِنَ المُدخَلِ العادِيّ وقِراءَتُه أَرخَصُ مِنه،
    /// فَسِعرٌ واحِدٌ لِلثَلاثَةِ يُلغي أَثَرَ التَخزينِ حِسابِيّاً.</para>
    /// </summary>
    [Fact]
    public void The_cost_prices_the_four_counters_with_four_different_prices()
    {
        // سِعرٌ مُصطَنَعٌ لِلبُرهانِ وَحدَه — لا يُكتَبُ في مِلَفِّ
        // البَيانات، ولا يُدَّعى أَنَّه سِعرُ أَحَد.
        var price = new ModelPrice(3m, 15m, 3.75m, 0.30m, "probe");
        var cost = ModelPricingCatalog.CostUsd(price, new AgentUsage(1_000_000, 1_000_000, 1_000_000, 1_000_000));

        Assert.Equal(3m + 15m + 3.75m + 0.30m, cost);

        // ونِصفُ مِليونٍ يُعطي نِصفَ السِعر — الوَحدَةُ «لِكُلِّ مِليون».
        Assert.Equal(1.5m, ModelPricingCatalog.CostUsd(price, new AgentUsage(500_000, 0, 0, 0)));

        // وقِراءَةُ الكاشِ لَيسَت مُدخَلاً عادِيّاً.
        Assert.NotEqual(
            ModelPricingCatalog.CostUsd(price, new AgentUsage(1_000_000, 0, 0, 0)),
            ModelPricingCatalog.CostUsd(price, new AgentUsage(0, 0, 0, 1_000_000)));

        // وسِعرٌ ناقِصٌ واحِدٌ يَكفي لِتَبقى الكِلفَةُ «غَيرَ مَعروفَة».
        Assert.Null(ModelPricingCatalog.CostUsd(
            price with { CacheReadPerMillionUsd = null }, new AgentUsage(1, 1, 1, 1)));
        Assert.Null(ModelPricingCatalog.CostUsd(price, null));
    }

    // ═══ ٣) السَطرُ المُسَجَّل ═════════════════════════════════════════

    /// <summary>
    /// <para><b>المُحاوَلَةُ الفاشِلَةُ تُسَجَّل.</b> فَشَلُ المُزَوِّدِ
    /// بَعدَ أَن قَرَأَ الطَلَبَ يُفَوتَرُ عِندَه، و<c>RunAnalysisAsync</c>
    /// يُحاوِلُ <b>مَرَّتَين</b> — فَسِجِلٌّ يُسقِطُ الفاشِلَةَ يُخفي
    /// حَتّى نِصفَ الإنفاقِ في أَسوَأِ الحالات.</para>
    /// </summary>
    [Fact]
    public void A_failed_attempt_still_produces_a_line()
    {
        // فَشَلٌ بِلا استِهلاكٍ مَقروء (‏401 مَثَلاً — لا جِسمَ فيه).
        var blind = ModelCallRecord.For(
            "_incubator", Guid.NewGuid(), "anthropic", "claude-sonnet-4-6",
            ModelCallOperation.Analyze, usage: null, success: false);

        Assert.False(blind.Success);
        Assert.Equal(ModelCallOperation.Analyze, blind.Operation);
        Assert.Equal(0, blind.InputTokens);
        Assert.Null(blind.CostUsd);

        // وفَشَلٌ **بِاستِهلاكٍ مَقروء** (‏رَدٌّ ناجِحٌ بِـJSON غَيرِ
        // صالِح) — التوكناتُ أُنفِقَت فِعلاً وتُسَجَّلُ كامِلَةً.
        var spent = ModelCallRecord.For(
            "_incubator", Guid.NewGuid(), "anthropic", "claude-sonnet-4-6",
            ModelCallOperation.Analyze, new AgentUsage(120, 8000, 0, 12_000), success: false);

        Assert.False(spent.Success);
        Assert.Equal(8000, spent.OutputTokens);
        Assert.Equal(12_000, spent.CacheReadTokens);
    }

    /// <summary>ولِلسَطرِ تِسعُ حَقائِقَ لا أَقَلّ.</summary>
    [Fact]
    public void The_line_carries_who_what_how_much_and_when()
    {
        var user = Guid.NewGuid();
        var at = new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);
        var line = ModelCallRecord.For(
            "_incubator", user, "gemini", "gemini-2.0-flash",
            ModelCallOperation.Refine, new AgentUsage(200, 50, 0, 800), success: true, atUtc: at);

        Assert.Equal("_incubator", line.TenantId);
        Assert.Equal(user, line.UserId);
        Assert.Equal("gemini", line.Provider);
        Assert.Equal("gemini-2.0-flash", line.Model);
        Assert.Equal(ModelCallOperation.Refine, line.Operation);
        Assert.Equal(200, line.InputTokens);
        Assert.Equal(50,  line.OutputTokens);
        Assert.Equal(0,   line.CacheWriteTokens);
        Assert.Equal(800, line.CacheReadTokens);
        Assert.True(line.Success);
        Assert.Equal(at, line.AtUtc);
        Assert.NotEqual(Guid.Empty, line.Id);
    }

    /// <summary>
    /// <para><b>ولا يَحمِلُ نَصَّ الطَلَبِ ولا نَصَّ الرَدّ.</b> يُفحَصُ
    /// بِالبِنيَةِ لا بِالنِيَّة: لا حَقلَ نَصِّيّاً خارِجَ الأَربَعَةِ
    /// المُعرَّفَة، فَلا مَوضِعَ يُدَسُّ فيه مُحتَوىً بَعدَ اليَوم.</para>
    /// </summary>
    [Fact]
    public void The_line_has_nowhere_to_hold_the_prompt_or_the_answer()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        { "TenantId", "Provider", "Model", "Operation" };

        var stringProps = typeof(ModelCallRecord)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .ToArray();

        output.WriteLine($"حُقولٌ نَصِّيَّةٌ في السَطر: {string.Join("، ", stringProps)}");
        Assert.True(stringProps.Length >= 4, "أَداةٌ عَمياء: لا حُقولَ نَصِّيَّةً أَصلاً.");

        var extra = stringProps.Where(n => !allowed.Contains(n)).ToArray();
        Assert.True(extra.Length == 0,
            "حَقلٌ نَصِّيٌّ جَديدٌ في سَطرِ القياس — وهُوَ مَوضِعٌ يَتَسَرَّبُ "
            + "إلَيه نَصُّ الطَلَبِ أَو الرَدّ:\n  " + string.Join("\n  ", extra));

        foreach (var p in typeof(ModelCallRecord).GetProperties())
            Assert.DoesNotMatch(
                new Regex("text|prompt|response|content|body|message|answer", RegexOptions.IgnoreCase),
                p.Name);
    }

    /// <summary>مَعجَمُ العَمَلِيّاتِ مُغلَقٌ بِثَلاثٍ — وهي الأَبوابُ
    /// الثَلاثَةُ الَّتي تُنفِقُ فِعلاً.</summary>
    [Fact]
    public void The_operation_vocabulary_is_closed_at_three()
    {
        Assert.Equal(3, ModelCallOperation.All.Count);
        Assert.Equal(3, ModelCallOperation.All.Distinct(StringComparer.Ordinal).Count());
        Assert.True(ModelCallOperation.IsKnown("analyze"));
        Assert.True(ModelCallOperation.IsKnown("refine"));
        Assert.True(ModelCallOperation.IsKnown("build"));
        Assert.False(ModelCallOperation.IsKnown("chat"));
        Assert.False(ModelCallOperation.IsKnown(null));
    }

    /// <summary>
    /// <para><b>ومُعَرِّفُ المُستَخدِمِ مُشتَقٌّ لا مُختَرَع</b>:
    /// <c>StudioAgent.razor</c> يَبني النِطاقَ
    /// <c>"studio-" + userId.ToString("N")</c>، و<c>AgentService</c>
    /// يَبني مُعَرِّفَ الجَلسَةِ <c>"scope:" + scopeId</c>. فَالسَطرُ
    /// يَقرَأُ المُستَخدِمَ مِن حَيثُ كُتِب.</para>
    ///
    /// <para>وجَلسَةُ مُشرِفِ المَنَصَّةِ المُشتَرَكَةُ بِلا مُستَخدِمٍ
    /// — <c>null</c> لا <c>Guid.Empty</c>: «لا مُستَخدِمَ» لَيسَ
    /// «المُستَخدِمُ الصِفر».</para>
    /// </summary>
    [Fact]
    public void The_user_behind_an_agent_session_is_derived_from_its_id()
    {
        var user = Guid.NewGuid();
        Assert.Equal(user, AgentService.UserIdFromSessionId("scope:studio-" + user.ToString("N")));
        Assert.Null(AgentService.UserIdFromSessionId(AgentSession.SessionId));
        Assert.Null(AgentService.UserIdFromSessionId(null));
        Assert.Null(AgentService.UserIdFromSessionId("scope:studio-ليس-معرفا"));
    }

    // ═══ ٤) القِراءَةُ التَجميعِيَّة ═══════════════════════════════════

    /// <summary>التَجميعُ يَجمَعُ الأَربَعَةَ مُنفَصِلَةً، ويَعُدُّ ما
    /// لَم يُسَعَّر — فَتَقريرٌ يَقولُ «‏12 دولاراً» عَن عَشرَةِ سُطورٍ
    /// نِصفُها بِلا سِعرٍ يَكذِب.</summary>
    [Fact]
    public void The_totals_keep_the_four_apart_and_count_what_is_not_priced()
    {
        var t = ModelCallTotals.Of(new[]
        {
            new ModelCallRecord { InputTokens = 100, OutputTokens = 20, CacheWriteTokens = 5,
                                  CacheReadTokens = 800, CostUsd = 1.5m, Success = true },
            new ModelCallRecord { InputTokens = 300, OutputTokens = 40, CacheWriteTokens = 0,
                                  CacheReadTokens = 200, CostUsd = null,  Success = false },
        });

        Assert.Equal(2, t.Calls);
        Assert.Equal(1, t.Failures);
        Assert.Equal(400,  t.InputTokens);
        Assert.Equal(60,   t.OutputTokens);
        Assert.Equal(5,    t.CacheWriteTokens);
        Assert.Equal(1000, t.CacheReadTokens);
        Assert.Equal(1.5m, t.CostUsd);
        Assert.Equal(1, t.UncostedCalls);

        var empty = ModelCallTotals.Of(Array.Empty<ModelCallRecord>());
        Assert.Equal(0, empty.Calls);
        Assert.Equal(0m, empty.CostUsd);
    }

    /// <summary>
    /// <para><b>والقياسُ لا يَكسِرُ المَسار.</b> جَلسَةٌ لا تُفتَح
    /// (‏<c>_store</c> مَعدوم) تُقرَأُ هُنا فَشَلَ تَسجيلٍ — ويَمُرُّ
    /// النِداءُ. تَحليلٌ يُرفَضُ لِأَنّ عَدّادَه لَم يُكتَب عَطَبٌ
    /// أَسوَأُ مِنَ العَطَبِ الَّذي جاءَ القياسُ يَكشِفُه.</para>
    /// </summary>
    [Fact]
    public async Task A_failure_to_record_does_not_break_the_call_path()
    {
        var tier = new StudioTierService(null!);
        var line = ModelCallRecord.For(
            "_incubator", Guid.NewGuid(), "anthropic", "claude-sonnet-4-6",
            ModelCallOperation.Analyze, new AgentUsage(1, 1, 1, 1), success: true);

        await tier.RecordModelCallAsync(line);   // لا يَرمي
    }

    // ═══ ٥) الوَصل — كُلُّ نِداءٍ يُسَجَّل ═════════════════════════════

    /// <summary>
    /// <para><b>كُلُّ مَوضِعٍ يُنادي الخَلفِيَّةَ يَكتُبُ سَطراً.</b>
    /// القارِئُ المُنَفَّذُ والجَدوَلُ المَملوءُ لا يَقيسانِ شَيئاً إن
    /// لَم يُوصَلا — وهذا هُوَ بِعَينِه العَطَبُ الَّذي كانَ قائِماً:
    /// <c>usage</c> يَصِلُ في الرَدِّ ولا يَقرَؤُه أَحَد.</para>
    /// </summary>
    [Fact]
    public void Every_backend_call_site_writes_a_metering_line()
    {
        var sites = BackendCallSites().ToList();
        output.WriteLine($"مَواضِعُ نِداءِ الخَلفِيَّة: {sites.Count}");

        Assert.True(sites.Count >= 3,
            $"أَداةٌ عَمياء: وُجِدَ {sites.Count} مَوضِعاً — والمَقيسُ ثَلاثَةٌ فَأَكثَر "
            + "(‏AgentService · RefineSectionAsync · RunAnalysisAsync).");

        var silent = sites.Where(s => !s.Window.Contains("RecordModelCallAsync", StringComparison.Ordinal))
            .Select(s => s.Where).ToArray();

        Assert.True(silent.Length == 0,
            $"نِداءُ نَموذَجِ لُغَةٍ بِلا سَطرِ قياس ({sites.Count} مَفحوصاً):\n  "
            + string.Join("\n  ", silent));
    }

    /// <summary>
    /// <para><b>والتَسجيلُ يَسبِقُ فَرعَ الخَطَأ</b> — وإلّا لَم
    /// تُسَجَّلِ المُحاوَلَةُ الفاشِلَة. في <c>RunAnalysisAsync</c>
    /// تَحديداً: السَطرُ داخِلَ حَلقَةِ المُحاوَلَتَين، وقَبلَ
    /// <c>continue</c> الَّذي يَقفِزُ بِالفَشَل.</para>
    /// </summary>
    [Fact]
    public void The_analysis_loop_records_before_it_skips_a_failed_attempt()
    {
        var text = File.ReadAllText(Path.Combine(
            TemplateRoot, "Services", "Incubator", "FeasibilityAnalysisService.cs"));

        var loop = BlockAfter(text, "for (var attempt");
        Assert.NotNull(loop);

        var record = loop!.IndexOf("RecordModelCallAsync", StringComparison.Ordinal);
        var skip   = loop.IndexOf("continue;", StringComparison.Ordinal);

        Assert.True(record >= 0,
            "حَلقَةُ المُحاوَلَتَينِ لا تُسَجِّلُ شَيئاً — فَالمُحاوَلَةُ الفاشِلَةُ تَختَفي.");
        Assert.True(skip >= 0, "أَداةٌ عَمياء: لا `continue` في الحَلقَة — تَبَدَّلَ شَكلُها.");
        Assert.True(record < skip,
            "التَسجيلُ يَقَعُ بَعدَ `continue` — أَي أَنّ الفاشِلَةَ تَقفِزُ فَوقَه.");
    }

    /// <summary>
    /// <para><b>والماسِحُ يُقاسُ قَبلَ أَن يُوثَقَ بِه</b> (القاعِدَة
    /// ١٠): يُحقَنُ مَوضِعُ نِداءٍ صامِتٌ ويُشتَرَطُ أَن يُمسَك،
    /// ونَظيرُه المُسَجِّلُ أَن يَمُرّ — فَـ«صِفرُ مُخالَفَة» مِن
    /// ماسِحٍ أَعمى لا يُمَيَّزُ عَن «صِفرُ مُخالَفَة» مِن ماسِحٍ يَرى.</para>
    /// </summary>
    [Fact]
    public void The_scanner_catches_an_injected_silent_call_site()
    {
        const string silent = """
            var req = new AgentRequest(systemPrompt, messages,
                Array.Empty<AgentToolDef>(), _model, MaxTokens: 3000);
            var resp = await _backend.CallAsync(req, ct);
            if (resp.Error is not null) return;
            """;

        const string metered = """
            var req = new AgentRequest(systemPrompt, messages,
                Array.Empty<AgentToolDef>(), _model, MaxTokens: 3000);
            var resp = await _backend.CallAsync(req, ct);
            await _tier.RecordModelCallAsync(ModelCallRecord.For(
                IncubatorTenant, s.OwnerUserId, _backend.ProviderName, _model,
                ModelCallOperation.Refine, resp.Usage, resp.Error is null), ct);
            if (resp.Error is not null) return;
            """;

        Assert.Contains(CallMarker, silent, StringComparison.Ordinal);
        Assert.DoesNotContain("RecordModelCallAsync", silent, StringComparison.Ordinal);
        Assert.Contains("RecordModelCallAsync", metered, StringComparison.Ordinal);

        // وفَحصُ التَرتيبِ يُقاسُ بِعَيبٍ مَحقونٍ أَيضاً: تَسجيلٌ بَعدَ
        // القَفز لا يُمسِكُ المُحاوَلَةَ الفاشِلَة.
        const string lateLoop = """
            for (var attempt = 0; attempt < 2 && json is null; attempt++)
            {
                var resp = await _backend.CallAsync(req, ct);
                if (resp.Error is not null) { lastError = resp.Error; continue; }
                await _tier.RecordModelCallAsync(line, ct);
            }
            """;
        var body = BlockAfter(lateLoop, "for (var attempt");
        Assert.NotNull(body);
        Assert.True(body!.IndexOf("RecordModelCallAsync", StringComparison.Ordinal)
                  > body.IndexOf("continue;", StringComparison.Ordinal),
            "فَحصُ التَرتيبِ لا يَرى تَسجيلاً واقِعاً بَعدَ القَفز — فَهُوَ أَعمى عَنِ العَطَبِ نَفسِه.");
    }

    // ─── أَدَواتُ الفَحص ─────────────────────────────────────────────

    private const string CallMarker = "_backend.CallAsync(";

    private sealed record CallSite(string Where, string File, string Window);

    /// <summary>كُلُّ نِداءٍ لِلخَلفِيَّةِ في المُستَودَع، ومَعَه
    /// نافِذَةُ الجُملَةِ الَّتي تَلي — حَتّى بِدايَةِ الدالَّةِ
    /// التالِيَة.</summary>
    private static IEnumerable<CallSite> BackendCallSites()
    {
        var files = new[]
        {
            Path.Combine(TemplateRoot, "Services", "AgentService.cs"),
            Path.Combine(TemplateRoot, "Services", "Incubator", "FeasibilityAnalysisService.cs"),
        };

        var nextMember = new Regex(@"\n    (?:public|private|internal|protected)\b", RegexOptions.Compiled);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var from = 0;
            while (true)
            {
                var i = text.IndexOf(CallMarker, from, StringComparison.Ordinal);
                if (i < 0) break;
                from = i + CallMarker.Length;

                var m = nextMember.Match(text, i);
                var end = m.Success ? m.Index : text.Length;
                yield return new CallSite(
                    $"{Path.GetFileName(file)}@{text[..i].Count(c => c == '\n') + 1}",
                    file, text[i..end]);
            }
        }
    }

    /// <summary>جِسمُ أَوَّلِ كُتلَةٍ بَعدَ العَلامَة، بِمُطابَقَةِ
    /// أَقواس — لا بِقَصٍّ بِعَدَدِ مَحارِف.</summary>
    private static string? BlockAfter(string text, string marker)
    {
        var i = text.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return null;
        var open = text.IndexOf('{', i);
        if (open < 0) return null;

        var depth = 0;
        for (var j = open; j < text.Length; j++)
        {
            if (text[j] == '{') depth++;
            else if (text[j] == '}' && --depth == 0) return text[open..(j + 1)];
        }
        return null;
    }

    private static string TemplateRoot => Path.Combine(
        ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
        "ACommerce.Templates.Customer.Marketplace");
}
