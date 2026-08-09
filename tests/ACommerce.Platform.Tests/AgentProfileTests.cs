using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── مِلَفّات تَعريف الوُكَلاء — طَبَقَة الحَلّ والتَكوين ────────────────
// لا نِداء LLM حَيّ هُنا إطلاقاً: كُلّ ما يُختَبَر هو **مَن يَقرَأ ماذا مِن
// أَين**، وهي بِالضَبط الطَبَقَة الَّتي إن انزاحَت صامِتَةً ذَهَبَ وَكيلٌ
// إلى مُزَوِّد آخَر، أَو ضاعَ مِفتاح، أَو عادَ وَكيل التَحليل إلى نَموذج
// وَكيل الاستوديو بِلا أَن يُلاحِظ أَحَد.

internal static class AgentCfg
{
    /// <summary>إعدادات في الذاكِرَة — لا مَلَفّات ولا بيئَة حَقيقيَّة.</summary>
    public static IConfiguration Of(params (string Key, string? Value)[] pairs)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    /// <summary>بيئَة مُزَيَّفَة — لا نَلمِس مُتَغَيِّرات العَمَليَّة الحَقيقيَّة
    /// (حالَة عامَّة تُفسِد التَشغيل المُتَوازي).</summary>
    public static Func<string, string?> Env(params (string Key, string Value)[] pairs)
    {
        var map = pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        return name => map.TryGetValue(name, out var v) ? v : null;
    }

    public static readonly Func<string, string?> NoEnv = _ => null;

    /// <summary>ما يَشحَنُه <c>apps/V1.App/appsettings.json</c> اليَوم حَرفيّاً.</summary>
    public static IConfiguration ShippedAppsettings() => Of(
        ("Agent:Provider", "anthropic"),
        ("Agent:Model",    ""),
        ("Agent:ApiKey",   ""));
}

// ═══ 1) قاعِدَة السُقوط: مُسَمّى ← قَديم ← بيئَة ═══════════════════════
public class AgentProfileResolutionTests
{
    [Fact]
    public void NamedSection_WinsOverLegacy()
    {
        var cfg = AgentCfg.Of(
            ("Agent:Provider",           "anthropic"),
            ("Agent:ApiKey",             "legacy-key"),
            ("Agent:Model",              "legacy-model"),
            ("Agents:Analysis:Provider", "openai"),
            ("Agents:Analysis:ApiKey",   "named-key"),
            ("Agents:Analysis:Model",    "named-model"));

        var p = AgentProfileResolver.Resolve(cfg, AgentNames.Analysis, AgentCfg.NoEnv);

        Assert.Equal("openai",       p.Provider);
        Assert.Equal("named-key",    p.ApiKey);
        Assert.Equal("named-model",  p.Model);
        Assert.Equal("Analysis",     p.Name);
    }

    [Fact]
    public void MissingNamedKey_FallsBackToLegacyKey()
    {
        // مُزَوِّد مُسَمّى فَقَط؛ المِفتاح والنَموذج مِن القَديم.
        var cfg = AgentCfg.Of(
            ("Agent:ApiKey",           "legacy-key"),
            ("Agent:Model",            "legacy-model"),
            ("Agents:Studio:Provider", "gemini"));

        var p = AgentProfileResolver.Resolve(cfg, AgentNames.Studio, AgentCfg.NoEnv);

        Assert.Equal("gemini",       p.Provider);
        Assert.Equal("legacy-key",   p.ApiKey);
        Assert.Equal("legacy-model", p.Model);
    }

    [Fact]
    public void TwoAgents_ResolveFullyIndependentProviderKeyAndModel()
    {
        var cfg = AgentCfg.Of(
            ("Agents:Analysis:Provider", "anthropic"),
            ("Agents:Analysis:ApiKey",   "sk-analysis"),
            ("Agents:Analysis:Model",    "claude-opus-4"),
            ("Agents:Studio:Provider",   "openai"),
            ("Agents:Studio:BaseUrl",    "https://models.github.ai/inference"),
            ("Agents:Studio:ApiKey",     "ghp-studio"),
            ("Agents:Studio:Model",      "openai/gpt-4o-mini"));

        var analysis = AgentProfileResolver.Resolve(cfg, AgentNames.Analysis, AgentCfg.NoEnv);
        var studio   = AgentProfileResolver.Resolve(cfg, AgentNames.Studio,   AgentCfg.NoEnv);

        Assert.Equal("anthropic",     analysis.Provider);
        Assert.Equal("sk-analysis",   analysis.ApiKey);
        Assert.Equal("claude-opus-4", analysis.Model);
        Assert.Null(analysis.BaseUrl);

        Assert.Equal("openai",                              studio.Provider);
        Assert.Equal("ghp-studio",                          studio.ApiKey);
        Assert.Equal("openai/gpt-4o-mini",                  studio.Model);
        Assert.Equal("https://models.github.ai/inference",  studio.BaseUrl);
    }

    [Fact]
    public void ThirdAgentName_ResolvesWithoutCodeChange()
    {
        // البِنيَة مَفتوحَة: وَكيل تَوليد الأَنماط لاحِقاً لا يَحتاج سَطراً جَديداً.
        var cfg = AgentCfg.Of(
            ("Agent:ApiKey",             "legacy-key"),
            ("Agents:Patterns:Provider", "gemini"),
            ("Agents:Patterns:Model",    "gemini-2.5-pro"));

        var p = AgentProfileResolver.Resolve(cfg, "Patterns", AgentCfg.NoEnv);

        Assert.Equal("gemini",         p.Provider);
        Assert.Equal("legacy-key",     p.ApiKey);
        Assert.Equal("gemini-2.5-pro", p.Model);
    }

    [Theory]
    [InlineData("anthropic", "ANTHROPIC_API_KEY")]
    [InlineData("gemini",    "GEMINI_API_KEY")]
    [InlineData("openai",    "OPENAI_API_KEY")]
    public void NoConfigKey_FallsBackToProviderEnvironmentVariable(string provider, string envName)
    {
        var cfg = AgentCfg.Of(("Agent:Provider", provider));
        var p = AgentProfileResolver.Resolve(
            cfg, AgentNames.Studio, AgentCfg.Env((envName, "env-key")));

        Assert.Equal("env-key", p.ApiKey);
    }

    [Theory]
    [InlineData("gemini", "GOOGLE_API_KEY")]        // بَعد GEMINI_API_KEY
    [InlineData("openai", "GROQ_API_KEY")]          // بَعد OPENAI_API_KEY
    [InlineData("openai", "CEREBRAS_API_KEY")]
    [InlineData("openai", "OPENROUTER_API_KEY")]
    public void EnvironmentChain_LaterVariableUsed_WhenEarlierUnset(string provider, string envName)
    {
        var cfg = AgentCfg.Of(("Agent:Provider", provider));
        var p = AgentProfileResolver.Resolve(
            cfg, AgentNames.Analysis, AgentCfg.Env((envName, "chained-key")));

        Assert.Equal("chained-key", p.ApiKey);
    }

    [Fact]
    public void EnvironmentChain_RespectsOrder()
    {
        var cfg = AgentCfg.Of(("Agent:Provider", "openai"));
        var p = AgentProfileResolver.Resolve(cfg, AgentNames.Studio,
            AgentCfg.Env(("OPENAI_API_KEY", "first"), ("GROQ_API_KEY", "second")));

        Assert.Equal("first", p.ApiKey);
    }

    [Fact]
    public void ConfigKey_WinsOverEnvironment()
    {
        var cfg = AgentCfg.Of(("Agent:Provider", "anthropic"), ("Agent:ApiKey", "from-config"));
        var p = AgentProfileResolver.Resolve(
            cfg, AgentNames.Studio, AgentCfg.Env(("ANTHROPIC_API_KEY", "from-env")));

        Assert.Equal("from-config", p.ApiKey);
    }

    [Fact]
    public void ProviderEnvironmentChain_IsChosenByResolvedProvider_NotLegacyKey()
    {
        // المُزَوِّد المُسَمّى يُغَيِّر أَيضاً سِلسِلَة البيئَة المُستَشارَة.
        var cfg = AgentCfg.Of(
            ("Agent:Provider",           "anthropic"),
            ("Agents:Analysis:Provider", "gemini"));

        var p = AgentProfileResolver.Resolve(cfg, AgentNames.Analysis,
            AgentCfg.Env(("ANTHROPIC_API_KEY", "wrong"), ("GEMINI_API_KEY", "right")));

        Assert.Equal("right", p.ApiKey);
    }

    [Fact]
    public void UnknownProvider_UsesAnthropicEnvironmentChain()
    {
        // AgentBackendFactory يَسقُط إلى Anthropic لِأَيّ اسم غَير مَعروف —
        // وسِلسِلَة البيئَة تَتبَعُه.
        var cfg = AgentCfg.Of(("Agent:Provider", "llamafile"));
        var p = AgentProfileResolver.Resolve(
            cfg, AgentNames.Studio, AgentCfg.Env(("ANTHROPIC_API_KEY", "anthropic-env")));

        Assert.Equal("llamafile",      p.Provider);
        Assert.Equal("anthropic-env",  p.ApiKey);
    }

    [Theory]
    [InlineData("  OpenAI  ", "openai")]
    [InlineData("GEMINI",     "gemini")]
    [InlineData("Anthropic",  "anthropic")]
    public void Provider_IsTrimmedAndLowercased(string raw, string expected)
    {
        var cfg = AgentCfg.Of(("Agents:Studio:Provider", raw));
        Assert.Equal(expected,
            AgentProfileResolver.Resolve(cfg, AgentNames.Studio, AgentCfg.NoEnv).Provider);
    }

    [Fact]
    public void NothingConfigured_YieldsAnthropicWithEmptyKeyAndNoModel()
    {
        var p = AgentProfileResolver.Resolve(AgentCfg.Of(), AgentNames.Studio, AgentCfg.NoEnv);

        Assert.Equal("anthropic", p.Provider);
        Assert.Equal("",          p.ApiKey);
        Assert.Null(p.Model);
        Assert.Null(p.BaseUrl);
        Assert.Null(p.ProviderLabel);
    }

    [Fact]
    public void EmptyPlaceholders_AreTreatedAsAbsent()
    {
        // appsettings.json يَشحَن "ApiKey": "" و "Model": "" كَحَشو. المَقصود
        // «غَير مَضبوط» — فَلا يَحجُبان سُقوط البيئَة ولا يُرسِلان نَموذجاً فارِغاً.
        var p = AgentProfileResolver.Resolve(AgentCfg.ShippedAppsettings(),
            AgentNames.Studio, AgentCfg.Env(("ANTHROPIC_API_KEY", "env-key")));

        Assert.Equal("env-key", p.ApiKey);
        Assert.Null(p.Model);
    }

    [Fact]
    public void WhitespaceNamedValue_FallsThroughToLegacy()
    {
        var cfg = AgentCfg.Of(
            ("Agent:Model",         "legacy-model"),
            ("Agents:Studio:Model", "   "));

        Assert.Equal("legacy-model",
            AgentProfileResolver.Resolve(cfg, AgentNames.Studio, AgentCfg.NoEnv).Model);
    }

    [Fact]
    public void BaseUrlAndProviderLabel_ResolveNamedThenLegacy()
    {
        var cfg = AgentCfg.Of(
            ("Agent:BaseUrl",              "https://legacy.example/"),
            ("Agent:ProviderLabel",        "legacy-label"),
            ("Agents:Analysis:BaseUrl",    "https://named.example/"));

        var analysis = AgentProfileResolver.Resolve(cfg, AgentNames.Analysis, AgentCfg.NoEnv);
        var studio   = AgentProfileResolver.Resolve(cfg, AgentNames.Studio,   AgentCfg.NoEnv);

        Assert.Equal("https://named.example/",  analysis.BaseUrl);
        Assert.Equal("legacy-label",            analysis.ProviderLabel);
        Assert.Equal("https://legacy.example/", studio.BaseUrl);
        Assert.Equal("legacy-label",            studio.ProviderLabel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void BlankAgentName_IsRejected(string? name)
        => Assert.Throws<ArgumentException>(
            () => AgentProfileResolver.Resolve(AgentCfg.Of(), name!, AgentCfg.NoEnv));
}

// ═══ 2) المُزَوِّد المُسَمّى: بِناء وتَخزين الخَلفيّات ═══════════════════
public class AgentBackendProviderTests
{
    [Fact]
    public void IdenticalProfiles_ShareOneBackendInstance()
    {
        // قَرار مُوَثَّق: مِلَفّان مُتَطابِقان لا يُنشِئان خَلفيَّتَين. المِعيار
        // هو ما يُغَيِّر الاتِّصال (مُزَوِّد + عُنوان + مِفتاح + تَسميَة).
        var p = new AgentBackendProvider(AgentCfg.Of(
            ("Agent:Provider", "anthropic"), ("Agent:ApiKey", "k")));

        Assert.Same(p.For(AgentNames.Studio), p.For(AgentNames.Analysis));
    }

    [Fact]
    public void DifferentModelsOnly_StillShareOneBackend()
    {
        // النَموذج ليس جُزءاً مِن هُوِيَّة الخَلفيَّة — يُمَرَّر في كُلّ طَلَب.
        // هذا هو حال GitHub Models: مِفتاح وعُنوان واحِد، نَموذَجان.
        var p = new AgentBackendProvider(AgentCfg.Of(
            ("Agent:Provider",        "openai"),
            ("Agent:BaseUrl",         "https://models.github.ai/inference"),
            ("Agent:ApiKey",          "ghp-one"),
            ("Agents:Analysis:Model", "openai/gpt-4o"),
            ("Agents:Studio:Model",   "openai/gpt-4o-mini")));

        Assert.Same(p.For(AgentNames.Studio), p.For(AgentNames.Analysis));
        Assert.Equal("openai/gpt-4o",      p.ModelFor(AgentNames.Analysis));
        Assert.Equal("openai/gpt-4o-mini", p.ModelFor(AgentNames.Studio));
    }

    [Fact]
    public void DifferentApiKeys_ProduceDistinctBackends()
    {
        var p = new AgentBackendProvider(AgentCfg.Of(
            ("Agent:Provider",         "anthropic"),
            ("Agents:Analysis:ApiKey", "key-a"),
            ("Agents:Studio:ApiKey",   "key-b")));

        Assert.NotSame(p.For(AgentNames.Studio), p.For(AgentNames.Analysis));
    }

    [Fact]
    public void DifferentProviders_ProduceDistinctBackendTypes()
    {
        var p = new AgentBackendProvider(AgentCfg.Of(
            ("Agent:ApiKey",             "k"),
            ("Agents:Analysis:Provider", "anthropic"),
            ("Agents:Studio:Provider",   "openai")));

        Assert.IsType<AnthropicBackend>(p.For(AgentNames.Analysis));
        Assert.IsType<OpenAIBackend>(p.For(AgentNames.Studio));
    }

    [Fact]
    public void ProfileAndBackend_AreCached()
    {
        var p = new AgentBackendProvider(AgentCfg.Of(("Agent:ApiKey", "k")));

        Assert.Same(p.ProfileFor(AgentNames.Studio), p.ProfileFor(AgentNames.Studio));
        Assert.Same(p.For(AgentNames.Studio),        p.For(AgentNames.Studio));
    }

    [Theory]
    [InlineData("anthropic", "claude-sonnet-4-6")]
    [InlineData("gemini",    "gemini-2.0-flash")]
    [InlineData("openai",    "gpt-4o")]
    public void ModelFor_FallsBackToBackendDefault_WhenProfileHasNoModel(
        string provider, string expected)
    {
        var p = new AgentBackendProvider(AgentCfg.Of(
            ("Agent:Provider", provider), ("Agent:ApiKey", "k")));

        Assert.Equal(expected, p.ModelFor(AgentNames.Analysis));
    }

    [Fact]
    public void ModelFor_PrefersProfileModel()
    {
        var p = new AgentBackendProvider(AgentCfg.Of(
            ("Agent:Provider", "anthropic"), ("Agent:ApiKey", "k"),
            ("Agents:Analysis:Model", "claude-opus-4")));

        Assert.Equal("claude-opus-4", p.ModelFor(AgentNames.Analysis));
    }

    [Fact]
    public void OpenAiBackend_TakesBaseUrlAndLabelFromProfile()
    {
        var p = new AgentBackendProvider(AgentCfg.Of(
            ("Agents:Studio:Provider",      "openai"),
            ("Agents:Studio:ApiKey",        "k"),
            ("Agents:Studio:BaseUrl",       "https://models.github.ai/inference"),
            ("Agents:Studio:ProviderLabel", "GitHub-Models")));

        var backend = Assert.IsType<OpenAIBackend>(p.For(AgentNames.Studio));

        Assert.Equal("github-models", backend.ProviderName);
        Assert.Equal("https://models.github.ai/inference/", backend.BaseUrl);
        Assert.True(backend.IsConfigured);
    }

    [Fact]
    public void OpenAiBackend_InfersLabelFromBaseUrl_WhenLabelAbsent()
    {
        var p = new AgentBackendProvider(AgentCfg.Of(
            ("Agent:Provider", "openai"),
            ("Agent:BaseUrl",  "https://api.groq.com/openai"),
            ("Agent:ApiKey",   "k")));

        Assert.Equal("groq", p.For(AgentNames.Studio).ProviderName);
    }

    [Fact]
    public void IsConfigured_StaysFalse_WhenNoKeyAnywhere()
    {
        // دَلالَة IsConfigured هي ما تَعتَمِدُه رَسائِل الواجِهَة.
        var p = new AgentBackendProvider(AgentCfg.Of(
            ("Agent:Provider", "gemini"), ("Agent:ApiKey", "")));

        Assert.False(p.For(AgentNames.Studio).IsConfigured);
    }
}

// ═══ 3) توصيف التَوافُق الرَجعيّ ═══════════════════════════════════════
// تَهيئَة Agent:* وَحدَها — كَما في نَشر HF الحاليّ — يَجِب أَن تُعطي
// نَفس سُلوك ما قَبل إعادَة الهَيكَلَة لِلوَكيلَين مَعاً.
public class AgentBackwardCompatibilityTests
{
    [Theory]
    [InlineData("anthropic", "claude-sonnet-4-6")]
    [InlineData("gemini",    "gemini-2.0-flash")]
    [InlineData("openai",    "gpt-4o")]
    public void LegacyConfigWithoutModel_BothAgentsGetProviderDefault(
        string provider, string expectedModel)
    {
        var p = new AgentBackendProvider(AgentCfg.Of(
            ("Agent:Provider", provider), ("Agent:ApiKey", "legacy-key")));

        // خَلفيَّة واحِدَة مُشتَرَكَة — كَما كانَ التَسجيل singleton تَماماً.
        Assert.Same(p.For(AgentNames.Studio), p.For(AgentNames.Analysis));
        Assert.Equal(provider == "openai" ? "openai" : provider,
                     p.For(AgentNames.Studio).ProviderName);
        Assert.True(p.For(AgentNames.Studio).IsConfigured);
        Assert.Equal(expectedModel, p.ModelFor(AgentNames.Studio));
        Assert.Equal(expectedModel, p.ModelFor(AgentNames.Analysis));
    }

    [Fact]
    public void LegacyOpenAiWithModel_AnalysisModelUnchanged()
    {
        // قَبل: OpenAIBackend.DefaultModel = Agent:Model ?? "gpt-4o"، ووَكيل
        // التَحليل كانَ يَستَخدِم DefaultModel. بَعد: نَموذج المِلَفّ. نَفس القيمَة.
        var p = new AgentBackendProvider(AgentCfg.Of(
            ("Agent:Provider", "openai"),
            ("Agent:ApiKey",   "k"),
            ("Agent:Model",    "llama-3.3-70b")));

        Assert.Equal("llama-3.3-70b", p.ModelFor(AgentNames.Analysis));
        Assert.Equal("llama-3.3-70b", p.ModelFor(AgentNames.Studio));
    }

    [Fact]
    public void LegacyConfig_StudioModelRuleUnchanged()
    {
        // قَبل: cfg["Agent:Model"] ?? backend.DefaultModel — حَرفيّاً.
        var withModel = new AgentBackendProvider(AgentCfg.Of(
            ("Agent:Provider", "anthropic"), ("Agent:ApiKey", "k"),
            ("Agent:Model", "claude-haiku-4")));
        var withoutModel = new AgentBackendProvider(AgentCfg.Of(
            ("Agent:Provider", "anthropic"), ("Agent:ApiKey", "k")));

        Assert.Equal("claude-haiku-4",    withModel.ModelFor(AgentNames.Studio));
        Assert.Equal("claude-sonnet-4-6", withoutModel.ModelFor(AgentNames.Studio));
    }

    [Fact]
    public void ShippedAppsettings_WithoutEnvironment_StaysUnconfigured()
    {
        // الحَشو الفارِغ + بِلا بيئَة = وَكيلان غَير مُهَيَّأَين، ونَموذج
        // افتِراضيّ سَليم بَدَل السِلسِلَة الفارِغَة الَّتي كانَت تُرسَل قَبلاً.
        var p = new AgentBackendProvider(AgentCfg.ShippedAppsettings());

        Assert.False(p.For(AgentNames.Studio).IsConfigured);
        Assert.False(p.For(AgentNames.Analysis).IsConfigured);
        Assert.Equal("anthropic",         p.For(AgentNames.Studio).ProviderName);
        Assert.Equal("claude-sonnet-4-6", p.ModelFor(AgentNames.Studio));
        Assert.Equal("claude-sonnet-4-6", p.ModelFor(AgentNames.Analysis));
    }

    [Fact]
    public void NamedSectionAbsent_IsIndistinguishableFromLegacyOnly()
    {
        var legacyOnly = AgentProfileResolver.Resolve(
            AgentCfg.Of(("Agent:Provider", "gemini"), ("Agent:ApiKey", "k"), ("Agent:Model", "m")),
            AgentNames.Analysis, AgentCfg.NoEnv);

        var named = AgentProfileResolver.Resolve(
            AgentCfg.Of(("Agents:Analysis:Provider", "gemini"),
                        ("Agents:Analysis:ApiKey",   "k"),
                        ("Agents:Analysis:Model",    "m")),
            AgentNames.Analysis, AgentCfg.NoEnv);

        Assert.Equal(legacyOnly, named);
    }
}

// ═══ 4) رَبط الخِدمَتَين بِمِلَفَّيهِما ════════════════════════════════
// الخِدمَتان تُبنَيان بِـ store = null عَمداً: مُنشِئاهُما لا يَلمِسانه،
// والمَقصود هُنا اختيار النَموذج والخَلفيَّة فَقَط بِلا Marten.
public class AgentServiceProfileWiringTests
{
    [Fact]
    public void AnalysisService_UsesItsOwnProfileModel_NotSharedBackendDefault()
    {
        // هذا هو الانحِراف الَّذي كانَ: FeasibilityAnalysisService كانَ
        // يَستَخدِم _backend.DefaultModel عارِيَةً فَيَتَجاهَل تَهيئَتَه.
        var provider = new AgentBackendProvider(AgentCfg.Of(
            ("Agent:Provider",        "anthropic"),
            ("Agent:ApiKey",          "k"),
            ("Agents:Analysis:Model", "claude-opus-4")));

        var analysis = new FeasibilityAnalysisService(null!, provider, null!);

        Assert.Equal("claude-opus-4", analysis.ModelName);
        Assert.NotEqual(provider.For(AgentNames.Analysis).DefaultModel, analysis.ModelName);
    }

    [Fact]
    public void StudioAndAnalysis_CarryDifferentModelsFromOneConfiguration()
    {
        var provider = new AgentBackendProvider(AgentCfg.Of(
            ("Agent:Provider",        "openai"),
            ("Agent:BaseUrl",         "https://models.github.ai/inference"),
            ("Agent:ApiKey",          "ghp-token"),
            ("Agents:Analysis:Model", "openai/gpt-4o"),
            ("Agents:Studio:Model",   "openai/gpt-4o-mini")));

        var studio   = new AgentService(null!, provider);
        var analysis = new FeasibilityAnalysisService(null!, provider, null!);

        Assert.Equal("openai/gpt-4o-mini", studio.ModelName);
        Assert.Equal("openai/gpt-4o",      analysis.ModelName);
        Assert.True(studio.IsConfigured);
        Assert.True(analysis.IsConfigured);
    }

    [Fact]
    public void StudioAndAnalysis_CanUseDifferentProvidersAndKeys()
    {
        var provider = new AgentBackendProvider(AgentCfg.Of(
            ("Agents:Analysis:Provider", "anthropic"),
            ("Agents:Analysis:ApiKey",   "sk-ant"),
            ("Agents:Studio:Provider",   "openai"),
            ("Agents:Studio:ApiKey",     "sk-oai"),
            ("Agents:Studio:BaseUrl",    "https://models.github.ai/inference")));

        var studio = new AgentService(null!, provider);

        Assert.Equal("openai",    studio.ProviderName);
        Assert.Equal("anthropic", provider.For(AgentNames.Analysis).ProviderName);
        Assert.NotSame(provider.For(AgentNames.Studio), provider.For(AgentNames.Analysis));
    }

    [Fact]
    public void LegacyOnlyConfiguration_BothServicesMatchTodaysBehaviour()
    {
        var provider = new AgentBackendProvider(AgentCfg.Of(
            ("Agent:Provider", "anthropic"), ("Agent:ApiKey", "legacy-key")));

        var studio   = new AgentService(null!, provider);
        var analysis = new FeasibilityAnalysisService(null!, provider, null!);

        Assert.Equal("anthropic",         studio.ProviderName);
        Assert.Equal("claude-sonnet-4-6", studio.ModelName);
        Assert.Equal("claude-sonnet-4-6", analysis.ModelName);
        Assert.True(studio.IsConfigured);
        Assert.True(analysis.IsConfigured);
    }
}
