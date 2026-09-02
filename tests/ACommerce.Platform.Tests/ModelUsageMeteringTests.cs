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

    /// <summary>
    /// <para><b>والعَقدُ واحِدٌ في الثَلاث: مِفتاحٌ داخِليٌّ غائِبٌ =
    /// <c>null</c>، لا أَصفار.</b> كانَ جيميناي يَنفَرِدُ بِقَبولِ
    /// <c>usageMetadata</c> فارِغاً فَيُرجِعُ أَربَعَةَ أَصفارٍ
    /// تُقرَأُ «نِداءٌ بِلا كِلفَة» — بَينَما أَخَواه يَشتَرِطانِ
    /// مِفتاحاً. تَبايُنٌ في العَقدِ نَفسِه، وقَد أُزيل.</para>
    /// </summary>
    [Fact]
    public void An_empty_usage_object_is_unmeasured_in_all_three_backends()
    {
        Assert.Null(GeminiBackend.ReadUsage(Root("""{ "usageMetadata": { } }""")));
        Assert.Null(AnthropicBackend.ReadUsage(Root("""{ "usage": { } }""")));
        Assert.Null(OpenAIBackend.ReadUsage(Root("""{ "usage": { } }""")));

        // وأَصفارٌ **مُصَرَّحٌ بِها** تُقرَأُ أَصفاراً مَقيسَة — الفَرقُ
        // بَينَ «لَم يُقَس» و«قيسَ فَكانَ صِفراً» مَحفوظ.
        var z = GeminiBackend.ReadUsage(Root("""{ "usageMetadata": { "promptTokenCount": 0 } }"""));
        Assert.NotNull(z);
        Assert.Equal(0, z!.InputTokens);
    }

    /// <summary>
    /// <para><b>وتوكناتُ التَفكيرِ عِندَ جيميناي تُفَوتَرُ مُخرَجاً
    /// ولا تَدخُلُ في <c>candidatesTokenCount</c>.</b> النَموذَجُ
    /// الافتِراضيُّ اليَومَ (<c>gemini-2.0-flash</c>) لا يُوَلِّدُها،
    /// لكِنّ النَموذَجَ يُضبَطُ بِـ<c>Agents:{Name}:Model</c> وأَيُّ
    /// نَموذَجٍ مِن سِلسِلَةِ ‏2.5 يُفَوتِرُها — فَإسقاطُها كانَ
    /// يُنقِصُ الفاتورَةَ صامِتاً. و<c>toolUsePromptTokenCount</c>
    /// مُدخَلٌ <b>خارِجَ</b> <c>promptTokenCount</c> فَيُضافُ ولا
    /// يُطرَح.</para>
    /// </summary>
    [Fact]
    public void Gemini_thinking_and_tool_use_tokens_are_not_dropped()
    {
        var u = GeminiBackend.ReadUsage(Root("""
            {
              "candidates": [ { "content": { "parts": [ { "text": "…" } ] } } ],
              "usageMetadata": {
                "promptTokenCount": 1000,
                "candidatesTokenCount": 50,
                "cachedContentTokenCount": 800,
                "thoughtsTokenCount": 700,
                "toolUsePromptTokenCount": 120,
                "totalTokenCount": 1870
              }
            }
            """));

        Assert.NotNull(u);
        Assert.Equal(320, u!.InputTokens);    // ‏(1000 − 800) + 120
        Assert.Equal(750, u.OutputTokens);    // ‏50 + 700
        Assert.Equal(0,   u.CacheWriteTokens);
        Assert.Equal(800, u.CacheReadTokens);

        // ومَجموعُ الأَربَعَةِ = مَجموعُ جوجل المُعلَن.
        Assert.Equal(1870,
            u.InputTokens + u.OutputTokens + u.CacheWriteTokens + u.CacheReadTokens);
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

    // ─── المُصادِق: سِتَّةُ رُموزٍ، لِكُلٍّ موجِبٌ وسالِب ──────────────

    private static string PricingRaw =>
        File.ReadAllText(Path.Combine(TemplateRoot, "Data", "model-pricing.json"));

    /// <summary>
    /// <para><b>المِلَفُّ الفِعليُّ يَجتازُ المُصادَقَةَ الصارِمَة.</b>
    /// وهذا هُوَ الطَرَفُ الموجِبُ لِكُلِّ رَمزِ خَرقٍ مَعاً: لا
    /// عُملَةَ غَريبَة، ولا وَحدَةَ غَريبَة، ولا جَدوَلَ فارِغ، ولا
    /// مِفتاحَ فارِغ، ولا سِعرَ غَيرَ مُوجَب، ولا سِعرَ بِلا
    /// تاريخ.</para>
    /// </summary>
    [Fact]
    public void The_shipped_pricing_file_passes_the_strict_reader()
    {
        var parsed = ModelPricingCatalog.Parse(PricingRaw);
        output.WriteLine($"نَماذِجُ الجَدوَل: {string.Join("، ", parsed.Keys)}");
        Assert.True(parsed.Count >= 3, $"أَداةٌ عَمياء: قُرِئَ {parsed.Count} نَموذَجاً.");
        Assert.Empty(ModelPricingValidator.Validate(
            ModelPricingValidator.Currency, ModelPricingValidator.Unit, parsed));

        // والجَدوَلُ الحَيُّ هو نَفسُه المِلَفّ — لا نُسخَةٌ ثانِيَة.
        Assert.Equal(parsed.Count, ModelPricingCatalog.All.Count);
    }

    /// <summary>
    /// <para><b>ومِفتاحٌ مَجهولٌ خَطَأٌ صَريحٌ لا «لَم يُملَأ بَعد».</b>
    /// هذا هُوَ العَطَبُ بِعَينِه الَّذي كانَ القارِئُ المُتَساهِلُ
    /// يَبتَلِعُه: «<c>cache_write</c>» مَكانَ «<c>cacheWrite</c>»
    /// يُقرَأُ سِعراً فارِغاً، فَتَبقى الكِلفَةُ <c>null</c> ويُقرَأُ
    /// الأَمرُ <b>تَأَخُّراً في التَسعير</b> لا خَطَأً في المِلَفّ —
    /// أَي فاتورَةٌ مَنقوصَةٌ بِلا صَوت.</para>
    /// </summary>
    [Fact]
    public void A_misspelled_price_key_is_an_error_not_an_unfilled_price()
    {
        var typo = PricingRaw.Replace("\"cacheWrite\"", "\"cache_write\"", StringComparison.Ordinal);
        Assert.NotEqual(PricingRaw, typo);   // حارِسُ عَمى: الاستِبدالُ وَقَعَ فِعلاً

        var ex = Assert.ThrowsAny<Exception>(() => ModelPricingCatalog.Parse(typo));
        output.WriteLine($"رَدُّ القارِئِ الصارِم: {ex.GetType().Name}: {ex.Message}");

        // ولَو كانَ القارِئُ مُتَساهِلاً لَمَرَّ الحَرفُ ولَبَقِيَ
        // السِعرُ «فارِغاً» — فَيُقاسُ الفَرقُ لا يُدَّعى.
        Assert.Contains("cache_write", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>ولِكُلِّ رَمزِ خَرقٍ طَرَفٌ سالِبٌ يُحمِرُّ بِه — وإلّا
    /// كانَ المُصادِقُ حِبراً.</summary>
    [Theory]
    [InlineData("currency_out_of_vocabulary", "\"currency\": \"USD\"", "\"currency\": \"SAR\"")]
    [InlineData("unit_out_of_vocabulary",     "\"unit\": \"perMillionTokens\"", "\"unit\": \"perThousandTokens\"")]
    public void Each_header_violation_code_has_a_failing_case(string code, string from, string to)
    {
        Assert.Contains(code, ModelPricingValidator.Codes);

        var broken = PricingRaw.Replace(from, to, StringComparison.Ordinal);
        Assert.NotEqual(PricingRaw, broken);   // حارِسُ عَمى

        var ex = Assert.Throws<InvalidOperationException>(() => ModelPricingCatalog.Parse(broken));
        Assert.Contains(code, ex.Message, StringComparison.Ordinal);
    }

    /// <summary>والرُموزُ الأَربَعَةُ الباقِيَةُ تُقاسُ عَلى
    /// <c>Validate</c> مُباشَرَةً — أَجسامٌ لا تُكتَبُ في مِلَفِّ
    /// المُستَودَعِ أَصلاً.</summary>
    [Fact]
    public void Each_model_violation_code_has_a_failing_case_and_a_passing_one()
    {
        var ok = new ModelPrice(1m, 2m, 3m, 4m, "2026-09-02");
        var good = new Dictionary<string, ModelPrice>(StringComparer.Ordinal) { ["m"] = ok };
        var C = ModelPricingValidator.Currency;
        var U = ModelPricingValidator.Unit;

        // الطَرَفُ الموجِب — لا خَرقَ في جَدوَلٍ سَليم.
        Assert.Empty(ModelPricingValidator.Validate(C, U, good));

        static string[] CodesOf(IReadOnlyList<ModelPriceViolation> v)
            => v.Select(x => x.Code).ToArray();

        // ‏models_empty
        Assert.Contains("models_empty", CodesOf(ModelPricingValidator.Validate(
            C, U, new Dictionary<string, ModelPrice>(StringComparer.Ordinal))));

        // ‏model_key_blank
        Assert.Contains("model_key_blank", CodesOf(ModelPricingValidator.Validate(
            C, U, new Dictionary<string, ModelPrice>(StringComparer.Ordinal) { ["  "] = ok })));

        // ‏price_not_positive — والصِفرُ هُوَ الحالَةُ المَقصودَة.
        Assert.Contains("price_not_positive", CodesOf(ModelPricingValidator.Validate(
            C, U, new Dictionary<string, ModelPrice>(StringComparer.Ordinal)
            { ["m"] = ok with { CacheReadPerMillionUsd = 0m } })));

        // ‏priced_at_missing — سِعرٌ مَملوءٌ بِلا تاريخِ قِراءَة.
        Assert.Contains("priced_at_missing", CodesOf(ModelPricingValidator.Validate(
            C, U, new Dictionary<string, ModelPrice>(StringComparer.Ordinal)
            { ["m"] = ok with { PricedAtUtc = null } })));

        // ولا يُطلَبُ التاريخُ مِن نَموذَجٍ كُلُّ أَسعارِه `null` — وهي
        // حالُ المِلَفِّ اليَوم.
        Assert.Empty(ModelPricingValidator.Validate(
            C, U, new Dictionary<string, ModelPrice>(StringComparer.Ordinal)
            { ["m"] = new ModelPrice(null, null, null, null, null) }));

        // وكُلُّ رَمزٍ مُعلَنٍ مَذكورٌ في هذا الفَحصِ أَو في أَخيه.
        Assert.Equal(6, ModelPricingValidator.Codes.Count);
        Assert.Equal(6, ModelPricingValidator.Codes.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// <para><b>وجَدوَلٌ فاسِدٌ لا يَكسِرُ مَسارَ النِداء</b> (القاعِدَة
    /// ٧): <see cref="ModelPricingCatalog.Parse"/> يَرمي بِرَمزِه —
    /// وذاكَ مَدخَلُ الفَحص — بَينَما <c>All</c> يَبتَلِعُ مَسموعاً
    /// ويُعطي جَدوَلاً فارِغاً، فَتَصيرُ كُلُّ كِلفَةٍ «غَيرَ مَعروفَة»
    /// ولا يُنتَجُ رَقَمٌ خاطِئٌ أَبَداً.</para>
    /// </summary>
    [Fact]
    public void A_broken_pricing_table_never_produces_a_wrong_number()
    {
        // لا سِعرَ يُخترَع عِندَ الجَهل: `null` لا صِفر.
        Assert.Null(ModelPricingCatalog.CostUsd(
            (ModelPrice?)null, new AgentUsage(1_000_000, 1_000_000, 1_000_000, 1_000_000)));

        var line = ModelCallRecord.For(
            "_incubator", null, "anthropic", "نَموذَجٌ-لا-وُجودَ-لَه",
            ModelCallOperation.Analyze, new AgentUsage(5, 5, 5, 5), success: true);
        Assert.Null(line.CostUsd);
        Assert.True(line.UsageMeasured);
        Assert.Equal(5, line.InputTokens);
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
        // سِعرٌ مُصطَنَعٌ لِلبُرهانِ وَحدَه — أَرقامٌ **لا تُطابِقُ
        // جَدوَلَ أَيِّ مُزَوِّد** عَمداً: النُسخَةُ الأولى استَعمَلَت
        // ‏(‏3 · 15 · 3.75 · 0.30) ووَصَفَتها بِأَنَّها «لَيسَت سِعرَ
        // أَحَد» — وهي حَرفاً أَسعارُ Claude Sonnet المَنشورَة. والخَطَرُ
        // أَن تُنقَلَ يَوماً إلى `model-pricing.json` بِاعتِبارِها حَشواً
        // (القاعِدَة ١٦: لا سِعرَ يُخترَع).
        var price = new ModelPrice(8m, 26m, 9m, 0.4m, "probe");
        var cost = ModelPricingCatalog.CostUsd(price, new AgentUsage(1_000_000, 1_000_000, 1_000_000, 1_000_000));

        Assert.Equal(8m + 26m + 9m + 0.4m, cost);

        // ونِصفُ مِليونٍ يُعطي نِصفَ السِعر — الوَحدَةُ «لِكُلِّ مِليون».
        Assert.Equal(4m, ModelPricingCatalog.CostUsd(price, new AgentUsage(500_000, 0, 0, 0)));

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
    ///
    /// <para><b>ويُفحَصُ النَوعُ لا الاسمُ ولا <c>string</c> وَحدَه</b>:
    /// النُسخَةُ السابِقَةُ كانَت تَمُرُّ عَلى
    /// <c>List&lt;string&gt;</c> و<c>Dictionary&lt;string,string&gt;</c>
    /// و<c>object</c> بِاسمٍ مُحايِد — وهي كُلُّها مَواضِعُ يُدَسُّ فيها
    /// المُحتَوى. فَالمَسموحُ الآنَ <b>مَعجَمُ أَنواعٍ مُغلَق</b>: أَربَعَةُ
    /// حُقولٍ نَصِّيَّةٍ مُسَمّاة، وسِواها عَدَدٌ أَو مُعَرِّفٌ أَو
    /// مَنطِقِيٌّ أَو زَمَن — لا حاوِيَةَ ولا نَوعَ مَفتوح.</para>
    /// </summary>
    [Fact]
    public void The_line_has_nowhere_to_hold_the_prompt_or_the_answer()
    {
        var allowedStrings = new HashSet<string>(StringComparer.Ordinal)
        { "TenantId", "Provider", "Model", "Operation" };

        // مَعجَمُ الأَنواعِ المُغلَق: لا نَصَّ إلّا بِاسمٍ مَسموح، ولا
        // حاوِيَةَ إطلاقاً.
        var scalars = new HashSet<Type>
        {
            typeof(Guid), typeof(Guid?), typeof(int), typeof(int?),
            typeof(long), typeof(long?), typeof(decimal), typeof(decimal?),
            typeof(bool), typeof(bool?), typeof(DateTime), typeof(DateTime?),
        };

        var props = typeof(ModelCallRecord)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToArray();

        var stringProps = props.Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name).ToArray();

        output.WriteLine($"حُقولُ السَطر: {props.Length} — نَصِّيَّةٌ مِنها: {string.Join("، ", stringProps)}");
        Assert.True(stringProps.Length >= 4, "أَداةٌ عَمياء: لا حُقولَ نَصِّيَّةً أَصلاً.");
        Assert.True(props.Length >= 12, $"أَداةٌ عَمياء: فُحِصَ {props.Length} حَقلاً فَقَط.");

        var leaky = props
            .Where(p => !(p.PropertyType == typeof(string)
                            ? allowedStrings.Contains(p.Name)
                            : scalars.Contains(p.PropertyType)))
            .Select(p => $"{p.PropertyType.Name} {p.Name}")
            .ToArray();

        Assert.True(leaky.Length == 0,
            "حَقلٌ في سَطرِ القياسِ خارِجَ مَعجَمِ الأَنواعِ المَسموح — وهُوَ "
            + "مَوضِعٌ يَتَسَرَّبُ إلَيه نَصُّ الطَلَبِ أَو الرَدّ:\n  "
            + string.Join("\n  ", leaky));

        foreach (var p in props)
            Assert.DoesNotMatch(
                new Regex("text|prompt|response|content|body|message|answer", RegexOptions.IgnoreCase),
                p.Name);
    }

    /// <summary>
    /// <para><b>وحارِسُ الخُصوصِيَّةِ نَفسُه يُقاسُ بِعَيبٍ مَحقون</b>
    /// (القاعِدَة ١٠): الشَرطُ يُطَبَّقُ عَلى نَوعٍ بَديلٍ يَحمِلُ
    /// حاوِيَةً بِاسمٍ مُحايِد — و<b>يَجِبُ</b> أَن يُمسَك. وإلّا كانَ
    /// «صِفرُ تَسَرُّب» جَوابَ حارِسٍ أَعمى.</para>
    /// </summary>
    [Fact]
    public void The_privacy_guard_catches_a_container_field_with_an_innocent_name()
    {
        var scalars = new HashSet<Type>
        {
            typeof(Guid), typeof(Guid?), typeof(int), typeof(decimal?),
            typeof(bool), typeof(DateTime),
        };
        var allowedStrings = new HashSet<string>(StringComparer.Ordinal) { "TenantId" };

        static string[] Leaks(Type t, HashSet<Type> scalars, HashSet<string> allowedStrings) => t
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !(p.PropertyType == typeof(string)
                            ? allowedStrings.Contains(p.Name)
                            : scalars.Contains(p.PropertyType)))
            .Select(p => p.Name).ToArray();

        Assert.Equal(new[] { "Extra" }, Leaks(typeof(LeakyProbe), scalars, allowedStrings));
        Assert.Empty(Leaks(typeof(CleanProbe), scalars, allowedStrings));
    }

    private sealed class LeakyProbe
    {
        public string TenantId { get; set; } = "";
        public int InputTokens { get; set; }
        /// <summary>حاوِيَةٌ بِاسمٍ مُحايِدٍ — تَمُرُّ مِن أَيِّ فَحصٍ
        /// يَنظُرُ في <c>string</c> وَحدَه.</summary>
        public List<string> Extra { get; set; } = new();
    }

    private sealed class CleanProbe
    {
        public string TenantId { get; set; } = "";
        public int InputTokens { get; set; }
        public bool Success { get; set; }
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
    /// <para><b>«لَم يُقَس» لا يَنهارُ إلى «صِفر».</b> رَدُّ ‏401 بِلا
    /// جِسمٍ مَقروءٍ يُخَزَّنُ بِأَربَعَةِ أَصفار، ونِداءٌ صِفرِيٌّ
    /// حَقيقيٌّ يُخَزَّنُ بِأَربَعَةِ أَصفارٍ أَيضاً — فَبِلا حَقلٍ
    /// يُفَرِّقُ بَينَهُما يُقَلِّلُ كُلُّ تَقريرٍ الفاتورَةَ دونَ أَن
    /// يَقول. وهُوَ نَفسُ ثابِتِ <see cref="AgentUsage"/> المُعلَنِ
    /// نَصّاً: «<c>null</c> لا أَصفار: لَم يُقَس ≠ لَم يُنفَق».</para>
    ///
    /// <para><b>و«لَم يُقَس» غَيرُ «لَم يُسَعَّر»</b>: الأَوَّلُ نَقصٌ
    /// في التوكناتِ نَفسِها، والثاني نَقصٌ في السِعر — وعَدّادانِ
    /// مُنفَصِلانِ لِأَنّ خَلطَهُما يُنقِصُ الفاتورَةَ مَرَّتَين.</para>
    /// </summary>
    [Fact]
    public void An_unmeasured_call_is_not_the_same_as_a_zero_call()
    {
        var blind = ModelCallRecord.For(
            "_incubator", null, "anthropic", "claude-sonnet-4-6",
            ModelCallOperation.Analyze, usage: null, success: false);

        var real = ModelCallRecord.For(
            "_incubator", null, "anthropic", "claude-sonnet-4-6",
            ModelCallOperation.Analyze, new AgentUsage(0, 0, 0, 0), success: true);

        Assert.False(blind.UsageMeasured);
        Assert.True(real.UsageMeasured);

        // والأَربَعَةُ مُتَطابِقَةٌ بَينَهُما — فَالحَقلُ وَحدَه
        // يُفَرِّق.
        Assert.Equal(blind.InputTokens, real.InputTokens);
        Assert.Equal(blind.OutputTokens, real.OutputTokens);

        var t = ModelCallTotals.Of(new[] { blind, real });
        Assert.Equal(2, t.Calls);
        Assert.Equal(1, t.UnmeasuredCalls);
        Assert.Equal(2, t.UncostedCalls);   // لا سِعرَ في الجَدوَلِ بَعد
    }

    /// <summary>
    /// <para><b>ولَحظَةُ السَطرِ وَحدَةٌ واحِدَةٌ عَلى طَرَفَي
    /// المَسار.</b> عَمودُ <c>AtUtc</c> عِندَ Postgres هو
    /// <c>timestamp without time zone</c>، وNpgsql يَرفُضُ وَسيطاً
    /// بِـ<c>Kind=Utc</c> عَلَيه — فَكانَت
    /// <c>ReadModelUsageAsync(uid, DateTime.UtcNow.AddDays(-30))</c>
    /// تَرمي عَلى قاعِدَةٍ حَقيقِيَّةٍ بَينَما الكِتابَةُ تَعمَل. هذا
    /// الفَحصُ يُثَبِّتُ الاختِيار: <c>Unspecified</c> بِتَوقيتٍ
    /// عالَميّ، عِندَ الكِتابَةِ وعِندَ القِراءَةِ مَعاً.</para>
    /// </summary>
    [Fact]
    public void The_timestamp_is_one_unit_on_both_ends_of_the_path()
    {
        // ١) الكِتابَةُ تُطَبِّع.
        var utc = new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);
        var line = ModelCallRecord.For(
            "_incubator", null, "anthropic", "claude-sonnet-4-6",
            ModelCallOperation.Analyze, new AgentUsage(1, 1, 1, 1), true, atUtc: utc);

        Assert.Equal(DateTimeKind.Unspecified, line.AtUtc.Kind);
        Assert.Equal(utc.Ticks, line.AtUtc.Ticks);

        // والافتِراضيُّ (‏`DateTime.UtcNow`) كَذلك.
        var now = ModelCallRecord.For(
            "_incubator", null, "anthropic", "m", ModelCallOperation.Build, null, true);
        Assert.Equal(DateTimeKind.Unspecified, now.AtUtc.Kind);
        Assert.Equal(DateTimeKind.Unspecified, new ModelCallRecord().AtUtc.Kind);

        // ٢) و`Local` يُحَوَّلُ إلى عالَمِيٍّ أَوَّلاً — فَلا تُخَزَّنُ
        //    ساعَةُ جِهازٍ بِاسمِ ساعَةٍ عالَمِيَّة.
        var local = utc.ToLocalTime();
        Assert.Equal(utc.Ticks, ModelCallRecord.Instant(local).Ticks);
        Assert.Equal(DateTimeKind.Unspecified, ModelCallRecord.Instant(local).Kind);

        // ٣) والتَطبيعُ ثابِتٌ (‏idempotent) فَلا يَنزاحُ بِتَكرارِه.
        var once = ModelCallRecord.Instant(utc);
        Assert.Equal(once, ModelCallRecord.Instant(once));

        // ٤) وجانِبُ القِراءَةِ يُطَبِّعُ بِنَفسِ الدالَّة — مَقيسٌ
        //    بِالمَصدَرِ لِأَنّ إثباتَه بِقاعِدَةٍ حَيَّةٍ في
        //    `LiveModelUsageMeteringProofTests`.
        var src = File.ReadAllText(Path.Combine(
            TemplateRoot, "Services", "Incubator", "StudioTier.cs"));
        var read = BlockAfter(src, "public async Task<Metering.ModelCallTotals> ReadModelUsageAsync");
        Assert.NotNull(read);
        Assert.Contains("ModelCallRecord.Instant(sinceUtc)", read!, StringComparison.Ordinal);
        Assert.DoesNotContain("r.AtUtc >= sinceUtc", read, StringComparison.Ordinal);
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
            public async Task RefineAsync(CancellationToken ct)
            {
                var req = new AgentRequest(systemPrompt, messages,
                    Array.Empty<AgentToolDef>(), _model, MaxTokens: 3000);
                var resp = await _backend.CallAsync(req, ct);
                if (resp.Error is not null) return;
            }
            """;

        const string metered = """
            public async Task RefineAsync(CancellationToken ct)
            {
                var req = new AgentRequest(systemPrompt, messages,
                    Array.Empty<AgentToolDef>(), _model, MaxTokens: 3000);
                var resp = await _backend.CallAsync(req, ct);
                await _tier.RecordModelCallAsync(ModelCallRecord.For(
                    IncubatorTenant, s.OwnerUserId, _backend.ProviderName, _model,
                    ModelCallOperation.Refine, resp.Usage, resp.Error is null), ct);
                if (resp.Error is not null) return;
            }
            """;

        // ═══ والفَحصُ يَستَدعي الماسِحَ نَفسَه ═══════════════════════
        //
        // النُسخَةُ السابِقَةُ مِن هذا الفَحصِ لَم تُنادِ
        // `BackendCallSites` إطلاقاً — كانَت تُؤَكِّدُ
        // `Assert.Contains` عَلى سِلسِلَتَينِ مَكتوبَتَينِ فيه، أَي
        // أَنَّها تَختَبِرُ `string.Contains` لا الأَداة. وهو بِعَينِه
        // ما تُحَذِّرُ مِنه القاعِدَة ١٠: «الأَداةُ تُقاسُ قَبلَ أَن
        // يُوثَقَ بِها». فَالعَيبُ يُحقَنُ الآنَ في **شَجَرَةٍ
        // مُؤَقَّتَةٍ** يَمسَحُها الماسِحُ فِعلاً — بِلا لَمسِ
        // المُستَودَع.
        var tmp = Path.Combine(Path.GetTempPath(), "wasayel-scanner-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tmp, "Deep", "Nested"));
        try
        {
            // في مُجَلَّدٍ **مُتَداخِلٍ**: القائِمَةُ اليَدَوِيَّةُ
            // السابِقَةُ كانَت عَمياءَ عَنه تَماماً.
            File.WriteAllText(Path.Combine(tmp, "Deep", "Nested", "ZzSilentProbe.cs"), silent);
            var caught = BackendCallSites(tmp).ToList();

            Assert.True(caught.Count == 1,
                $"الماسِحُ لَم يَرَ المَوضِعَ المَحقونَ في مُجَلَّدٍ مُتَداخِل (وَجَدَ {caught.Count}).");
            Assert.DoesNotContain("RecordModelCallAsync", caught[0].Window, StringComparison.Ordinal);

            // ونَظيرُه المُسَجِّلُ يَمُرّ — وإلّا كانَ الماسِحُ
            // يُحمِرُّ عَلى كُلِّ شَيءٍ فَلا يُمَيِّزُ.
            File.WriteAllText(Path.Combine(tmp, "Deep", "Nested", "ZzSilentProbe.cs"), metered);
            var clean = BackendCallSites(tmp).ToList();
            Assert.Single(clean);
            Assert.Contains("RecordModelCallAsync", clean[0].Window, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }

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

    /// <summary>
    /// <para>كُلُّ نِداءٍ لِلخَلفِيَّةِ <b>تَحتَ الجِذرِ المُعطى
    /// بِكامِلِ عُمقِه</b>، ومَعَه نافِذَةُ الجُملَةِ الَّتي تَلي —
    /// حَتّى بِدايَةِ الدالَّةِ التالِيَة.</para>
    ///
    /// <para><b>ولِماذا مَسحٌ لا قائِمَةٌ بِاليَد</b>: القائِمَةُ
    /// السابِقَةُ كانَت مِلَفَّينِ مُثبَّتَينِ نَصّاً، فَـ«كُلُّ مَوضِعِ
    /// نِداءٍ يَكتُبُ سَطراً» كانَ في الحَقيقَةِ «كُلُّ مَوضِعٍ في
    /// هذَينِ المِلَفَّين» — ومَوضِعٌ رابِعٌ في مِلَفٍّ جَديدٍ لا يَراه
    /// الفاحِصُ إطلاقاً، وحارِسُ العَمى <c>Count &gt;= 3</c> يَبقى
    /// أَخضَرَ لِأَنَّه يَعُدُّ ما وَجَدَ لا ما يَنبَغي أَن يَجِد.
    /// وهذا بِعَينِه صِنفُ العَطَبِ الَّذي كَتَبَ القاعِدَةَ ٢.</para>
    ///
    /// <para><b>و<paramref name="root"/> وَسيطٌ لا ثابِت</b> لِيُقاسَ
    /// الماسِحُ نَفسُه بِعَيبٍ مَحقونٍ في شَجَرَةٍ مُؤَقَّتَة، بِلا
    /// لَمسِ المُستَودَع (القاعِدَة ١٠).</para>
    /// </summary>
    private static IEnumerable<CallSite> BackendCallSites(string? root = null)
    {
        root ??= Path.Combine(TemplateRoot, "Services");
        var files = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal);

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
