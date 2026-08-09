using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;

namespace ACommerce.Templates.Customer.Marketplace.Services;

// ─── ملفّات تَعريف الوُكَلاء ──────────────────────────────────────────
// المِنَصَّة تُشَغِّل أَكثَر مِن وَكيل مَنطِقيّ واحِد، ولِكُلٍّ حاجَة
// مُختَلِفَة: وَكيل التَحليل يَحتاج نَموذجاً ذَكيّاً (دِراسَة جَدوى
// كامِلَة بِـ JSON)، ووَكيل الاستوديو يَحتاج نَموذجاً أَخَفّ وأَسرَع
// (نِداء أَدَوات قَصير). قَبل هذِه الطَبَقَة كانا يَتَقاسَمان خَلفيَّةً
// واحِدَة singleton ومِفتاحاً واحِداً، وكانَت قِراءَة الإعدادات
// مُبَعثَرَة داخِل كُلّ خَلفيَّة عَلى حِدَة.
//
// الآن: **مَوضِع واحِد** يَحُلّ الإعدادات (AgentProfileResolver)،
// و**مُزَوِّد مُسَمّى** يَبني خَلفيَّة لِكُلّ مِلَفّ مُتَمايِز
// (AgentBackendProvider)، والخَلفيّات نَفسُها صارَت خيارات-صِرفَة
// لا تَعرِف IConfiguration إطلاقاً.

/// <summary>أَسماء الوُكَلاء المَنطِقيّين. الاسم هو مِفتاح قِسم الإعدادات
/// <c>Agents:{Name}:*</c>، والبِنيَة مَفتوحَة لِأَسماء قادِمَة.</summary>
public static class AgentNames
{
    /// <summary>وَكيل الاستوديو/التَصميم — نِداء أَدَوات على المُستَأجِرين.</summary>
    public const string Studio = "Studio";

    /// <summary>وَكيل التَحليل الاستثماريّ (الحاضِنَة) — دِراسَة جَدوى JSON.</summary>
    public const string Analysis = "Analysis";
}

/// <summary>
/// خيارات وَكيل واحِد بَعد الحَلّ الكامِل. كُلّ ما تَحتاجُه الخَلفيَّة
/// لِتَعمَل — بِلا IConfiguration وبِلا قِراءَة بيئَة مُتَأَخِّرَة.
/// </summary>
/// <param name="Name">اسم الوَكيل المَنطِقيّ (<see cref="AgentNames"/>).</param>
/// <param name="Provider">‏<c>anthropic | gemini | openai</c> — مُطَبَّع صَغيراً ومَقصوص المَسافات.</param>
/// <param name="BaseUrl">عُنوان مُتَوافِق OpenAI (‏GitHub Models، Groq، Ollama…) أَو null لِلافتِراضيّ.</param>
/// <param name="ApiKey">المِفتاح بَعد سُقوط الإعدادات ثُمّ البيئَة. سِلسِلَة فارِغَة = غَير مُهَيَّأ.</param>
/// <param name="Model">النَموذج الصَريح، أَو null فَيُؤخَذ افتِراضيّ الخَلفيَّة.</param>
/// <param name="ProviderLabel">تَسميَة المُزَوِّد المَعروضَة (خَلفيَّة OpenAI فَقَط)، أَو null فَتُستَنتَج.</param>
public sealed record AgentProfile(
    string Name,
    string Provider,
    string? BaseUrl,
    string ApiKey,
    string? Model,
    string? ProviderLabel);

/// <summary>
/// قاعِدَة السُقوط الوَحيدَة في المِنَصَّة لِإعدادات الوُكَلاء:
/// <code>Agents:{Name}:{Key}  ←  Agent:{Key} (القَديم)  ←  مُتَغَيِّر البيئَة</code>
/// أَوَّل قيمَة **غَير فارِغَة** في السِلسِلَة تَفوز؛ القيمَة الفارِغَة أَو
/// المَسافات البيضاء تُعامَل كَغائِبَة (‏<c>appsettings.json</c> يَشحَن
/// <c>"ApiKey": ""</c> و<c>"Model": ""</c> كَحَشو، والمَقصود بِهِما
/// «غَير مَضبوط» لا «سِلسِلَة فارِغَة»).
/// </summary>
public static class AgentProfileResolver
{
    /// <summary>يَحُلّ مِلَفّ وَكيل مِن الإعدادات. <paramref name="env"/>
    /// لِلاختِبار فَقَط — الافتِراضيّ بيئَة العَمَليَّة الحَقيقيَّة.</summary>
    public static AgentProfile Resolve(
        IConfiguration cfg, string agentName, Func<string, string?>? env = null)
    {
        ArgumentNullException.ThrowIfNull(cfg);
        if (string.IsNullOrWhiteSpace(agentName))
            throw new ArgumentException("اسم الوَكيل مَطلوب.", nameof(agentName));

        env ??= Environment.GetEnvironmentVariable;

        // مُسَمّى أَوَّلاً، ثُمّ المِفتاح القَديم المُشتَرَك.
        string? Read(string key) => FirstSet(cfg[$"Agents:{agentName}:{key}"], cfg[$"Agent:{key}"]);

        var provider = (Read("Provider") ?? "anthropic").Trim().ToLowerInvariant();

        return new AgentProfile(
            Name:          agentName,
            Provider:      provider,
            BaseUrl:       Read("BaseUrl"),
            ApiKey:        Read("ApiKey") ?? EnvApiKey(provider, env) ?? "",
            Model:         Read("Model"),
            ProviderLabel: Read("ProviderLabel"));
    }

    /// <summary>سُقوط مُتَغَيِّرات البيئَة — هو نَفسُه الَّذي كانَ داخِل كُلّ
    /// خَلفيَّة، مَنقولاً إلى هُنا. يُختار بِحَسَب المُزَوِّد المَحلول، تَماماً
    /// كَما كانَ يُختار بِحَسَب صَنف الخَلفيَّة المُنشَأ.</summary>
    private static string? EnvApiKey(string provider, Func<string, string?> env) => provider switch
    {
        "gemini" => FirstSet(env("GEMINI_API_KEY"), env("GOOGLE_API_KEY")),
        "openai" => FirstSet(env("OPENAI_API_KEY"), env("GROQ_API_KEY"),
                             env("CEREBRAS_API_KEY"), env("OPENROUTER_API_KEY")),
        _        => FirstSet(env("ANTHROPIC_API_KEY")),
    };

    private static string? FirstSet(params string?[] candidates)
    {
        foreach (var c in candidates)
            if (!string.IsNullOrWhiteSpace(c)) return c;
        return null;
    }
}

/// <summary>
/// مُزَوِّد الخَلفيّات المُسَمّى. يَحُلّ المِلَفّ مَرَّةً واحِدَة لِكُلّ اسم،
/// ويُخَزِّن خَلفيَّةً واحِدَة لِكُلّ مِلَفّ **مُتَمايِز**.
/// </summary>
public interface IAgentBackendProvider
{
    /// <summary>المِلَفّ المَحلول لِهذا الوَكيل (مُخَزَّن).</summary>
    AgentProfile ProfileFor(string agentName);

    /// <summary>الخَلفيَّة الَّتي يَستَخدِمُها هذا الوَكيل (مُخَزَّنَة، ومُشتَرَكَة
    /// بَين الوُكَلاء ذَوي الإعدادات المُتَطابِقَة).</summary>
    IAgentBackend For(string agentName);

    /// <summary>النَموذج الفِعليّ: نَموذج المِلَفّ، وإن غابَ فافتِراضيّ الخَلفيَّة.</summary>
    string ModelFor(string agentName);
}

/// <inheritdoc cref="IAgentBackendProvider"/>
public sealed class AgentBackendProvider : IAgentBackendProvider
{
    private readonly IConfiguration _cfg;
    private readonly ConcurrentDictionary<string, AgentProfile> _profiles =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<BackendKey, IAgentBackend> _backends = new();

    public AgentBackendProvider(IConfiguration cfg) => _cfg = cfg;

    public AgentProfile ProfileFor(string agentName)
        => _profiles.GetOrAdd(agentName, n => AgentProfileResolver.Resolve(_cfg, n));

    public IAgentBackend For(string agentName)
    {
        var profile = ProfileFor(agentName);
        return _backends.GetOrAdd(BackendKey.From(profile), _ => AgentBackendFactory.Create(profile));
    }

    public string ModelFor(string agentName)
        => ProfileFor(agentName).Model ?? For(agentName).DefaultModel;

    /// <summary>
    /// هُوِيَّة الخَلفيَّة — ما يُغَيِّر **الاتِّصال** فَقَط. النَموذج ليس
    /// جُزءاً مِنها عَمداً: الخَلفيَّة لا تَحمِل نَموذجاً، بَل يُمَرَّر في
    /// كُلّ <see cref="AgentRequest"/>. فَوَكيلان بِنَفس المِفتاح والعُنوان
    /// ونَموذَجَين مُختَلِفَين يَتَقاسَمان <c>HttpClient</c> واحِداً — وهو
    /// حال GitHub Models المَقصود بِالضَبط.
    /// </summary>
    private sealed record BackendKey(
        string Provider, string? BaseUrl, string ApiKey, string? ProviderLabel)
    {
        public static BackendKey From(AgentProfile p)
            => new(p.Provider, p.BaseUrl, p.ApiKey, p.ProviderLabel);
    }
}
