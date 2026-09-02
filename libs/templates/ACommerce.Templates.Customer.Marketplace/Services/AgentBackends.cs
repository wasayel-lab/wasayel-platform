using System.Net.Http.Json;
using System.Text.Json;

namespace ACommerce.Templates.Customer.Marketplace.Services;

// ─── أَنواع مُحَايِدَة عَن المُزَوِّد ────────────────────────────────────
// AgentService يُنتِج هذه الأَنواع، الـ Backend يُحَوِّلها إلى شَكل API
// الخاصّ بِالمُزَوِّد. هَدَفُها: تَبديل المُزَوِّد بِتَغيير سَطر إعدادات
// واحِد بِدون لَمس مَنطِق الأَدَوات أَو المُحادَثَة.

public sealed record AgentRequest(
    string SystemPrompt,
    IReadOnlyList<AgentMessage> Messages,
    IReadOnlyList<AgentToolDef> Tools,
    string Model,
    int MaxTokens);

public sealed record AgentMessage(
    string Role,                    // "user" | "assistant"
    string? Text,
    AgentToolCallOut? ToolCall,     // assistant مَع نِداء أَداة
    AgentToolResult? ToolResult);   // user مَع نَتيجَة أَداة

public sealed record AgentToolCallOut(string Id, string Name, string InputJson);
public sealed record AgentToolResult(string ToolCallId, string ToolName, string Content);
public sealed record AgentToolDef(string Name, string Description, string InputSchemaJson);
/// <summary>
/// <para><b>استِهلاكُ نِداءٍ واحِد — أَربَعَةُ عَدّاداتٍ
/// <u>مُتَبايِنَة</u>.</b> «مُتَبايِنَة» شَرطٌ في الوَحدَةِ لا وَصفٌ
/// لَها: <c>InputTokens</c> هُنا <b>لا يَشمَل</b> ما قُرِئَ مِنَ
/// الكاشِ ولا ما كُتِبَ فيه، فَمَجموعُ ما دَخَلَ هو
/// <c>Input + CacheWrite + CacheRead</c>.</para>
///
/// <para><b>ولِماذا أَربَعَةٌ لا رَقمانِ</b>: الكاشُ يُفَوتَرُ
/// بِسِعرَينِ مُختَلِفَينِ عَنِ المُدخَلِ العادِيّ — كِتابَتُه أَغلى
/// وقِراءَتُه أَرخَصُ بِكَثير. وجَمعُ الثَلاثَةِ في رَقَمٍ واحِدٍ
/// يَمحو أَثَرَ <c>cache_control</c> الَّذي فُعِّلَ لِأَجلِه، فَلا
/// يُعرَفُ أَنَفَعَ أَم ضَرّ.</para>
///
/// <para><b>والتَطبيعُ يَقَعُ في كُلِّ خَلفِيَّةٍ على حِدَة</b>: مِن
/// المُزَوِّدينَ الثَلاثَةِ <b>واحِدٌ فَقَط</b> (أَنثروبيك) يَرُدُّ
/// الأَربَعَةَ مُتَبايِنَةً أَصلاً؛ والآخَرانِ يَضُمّانِ المُخَزَّنَ
/// داخِلَ عَدَدِ المُدخَل — فَيُطرَح، وإلّا حُسِبَ مَرَّتَين.</para>
/// </summary>
public sealed record AgentUsage(
    int InputTokens, int OutputTokens, int CacheWriteTokens, int CacheReadTokens);

/// <param name="Usage">‏<c>null</c> إن لَم يَحمِل الرَدُّ استِهلاكاً
/// مَقروءاً (‏خَطَأُ HTTP، أَو استِثناءُ شَبَكَة). و<c>null</c> لا
/// أَصفار: «لَم يُقَس» غَيرُ «لَم يُنفَق».</param>
public sealed record AgentBackendResponse(
    string? Text, AgentToolCallOut? ToolCall, string? Error, AgentUsage? Usage = null);

/// <summary>قِراءَةُ عَدَدٍ صَحيحٍ غَيرِ سالِبٍ مِن كائِنِ الاستِهلاك —
/// الحَقلُ الغائِبُ صِفر، لِأَنّ المُزَوِّدينَ يُسقِطونَ حُقولَ الكاشِ
/// حينَ لا كاش.</summary>
internal static class UsageJson
{
    public static int NonNegative(JsonElement obj, string key)
        => obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number
           && v.TryGetInt32(out var n) && n > 0 ? n : 0;
}

public interface IAgentBackend
{
    string ProviderName { get; }
    string DefaultModel { get; }
    bool IsConfigured { get; }

    /// <summary>
    /// العُنوان الَّذي **سَيُنادى فِعلاً** — مَقروءاً مِن
    /// <c>HttpClient.BaseAddress</c> نَفسِه لا مُعاداً بِناؤُه مِن الإعدادات.
    /// وُجِدَ لِسَطرِ الإقلاع: مُزَوِّدٌ مَحلولٌ خَطَأً يُرسِلُ المِفتاحَ إلى
    /// خادِمٍ لا يَعرِفُه فَيَرُدّ ‏401، وتُقرَأ المُصادَقَةُ حِصَّةً نافِدَة.
    /// </summary>
    string Endpoint { get; }

    Task<AgentBackendResponse> CallAsync(AgentRequest req, CancellationToken ct);
}


// ─── صَوتُ الخَطَأ — يَقولُ الخادِمَ لا الصَنف ────────────────────────
//
// **العِلَّةُ المَقيسَة (‏2026-08-31)**: الشاشَةُ قالَت
// `OpenAI 401: {"message":"Invalid API Key","code":"invalid_api_key"}`
// وذلكَ الجِسمُ **جِسمُ Groq** لا جِسمُ OpenAI. البادِئَةُ كانَت
// حَرفِيَّةً مَكتوبَةً في ثَلاثِ خَلفِيّاتٍ سَبعَ مَرّات — أَي اسمَ
// **الصَنفِ الَّذي سَأَل** لا اسمَ **الخادِمِ الَّذي رَدّ**؛ و
// `OpenAIBackend` وَحدَه يَخدِمُ Groq وCerebras وOpenRouter وOllama.
//
// ‏`82200f1f` عالَجَ الطَرَفَ الأَوَّلَ (سَطرُ الإقلاع) — وذاكَ
// يُقرَأُ في سِجِلِّ الحاوِيَة، ولا يَبلُغُه مَن يَرى الشاشَة. وهذِه
// تُعالِجُ الطَرَفَ الثاني: **الرِسالَةُ نَفسُها تَحمِلُ المُزَوِّدَ
// والعُنوانَ**، فَيُقرَأُ التَشخيصُ حَيثُ يَقَعُ العَطَب.
//
// **ولا مِفتاحَ فيها ولا جُزءٌ مِنه** — تَسميَةٌ وعُنوانٌ ورَمزٌ
// وجِسمُ الخادِمِ مَقصوصاً، لا غَير.
public static class AgentErrorText
{
    /// <summary>«‏groq (https://api.groq.com/openai/)» — التَسميَةُ
    /// والعُنوانُ الفِعلِيّ.</summary>
    public static string Where(string providerName, string endpoint)
        => $"{providerName} ({endpoint})";

    /// <summary>رَدٌّ غَيرُ ناجِح: تَسميَةٌ وعُنوانٌ ورَمزٌ وجِسم.</summary>
    public static string Http(string providerName, string endpoint, int status, string body)
        => $"{Where(providerName, endpoint)} {status}: {body}";

    /// <summary>استِثناءٌ في النِداء — والعُنوانُ هُنا أَهَمُّ ما يُقال
    /// (‏مَنفَذٌ مَقفولٌ أَو مُضيفٌ لا يُحَلّ).</summary>
    public static string Exception(string providerName, string endpoint, string message)
        => $"{Where(providerName, endpoint)} exception: {message}";

    /// <summary>رَدٌّ ناجِحٌ بِشَكلٍ لا يُقرَأ.</summary>
    public static string Malformed(string providerName, string endpoint, string what)
        => $"{Where(providerName, endpoint)}: {what}";
}
// ─── Factory ─────────────────────────────────────────────────────────
// الخَلفيّات خيارات-صِرفَة: تَأخُذ <see cref="AgentProfile"/> مَحلولاً ولا
// تَقرَأ إعدادات ولا بيئَة بِنَفسِها. كُلّ الحَلّ في AgentProfileResolver،
// والاختِيار المُسَمّى في AgentBackendProvider.
public static class AgentBackendFactory
{
    public static IAgentBackend Create(AgentProfile profile) => profile.Provider switch
    {
        "gemini" => new GeminiBackend(profile),
        "openai" => new OpenAIBackend(profile),
        _        => new AnthropicBackend(profile)
    };
}

// ─── Anthropic (مَع prompt caching) ──────────────────────────────────
public sealed class AnthropicBackend : IAgentBackend
{
    private readonly string _apiKey;
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://api.anthropic.com/"),
        Timeout = TimeSpan.FromSeconds(60)
    };
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public AnthropicBackend(AgentProfile profile) => _apiKey = profile.ApiKey;

    public string ProviderName => "anthropic";
    public string DefaultModel => "claude-sonnet-4-6";
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);
    public string Endpoint => Http.BaseAddress!.ToString();

    public async Task<AgentBackendResponse> CallAsync(AgentRequest req, CancellationToken ct)
    {
        // ── Prompt caching ──
        // نَضَع cache_control عَلى آخِر أَداة → يُخَزَّن قِسم tools كامِلاً.
        // وَنَضَع cache_control عَلى system block → يُخَزَّن system+tools.
        // النَتيجَة: ≈80% خَصم عَلى التوكنز المَقروءَة لِكُلّ نِداء بَعد الأَوَّل.
        var toolsArr = req.Tools.Select((t, i) =>
        {
            var obj = new Dictionary<string, object?>
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["input_schema"] = JsonSerializer.Deserialize<JsonElement>(t.InputSchemaJson)
            };
            if (i == req.Tools.Count - 1)
                obj["cache_control"] = new { type = "ephemeral" };
            return (object)obj;
        }).ToArray();

        var systemBlocks = new object[]
        {
            new
            {
                type = "text",
                text = req.SystemPrompt,
                cache_control = new { type = "ephemeral" }
            }
        };

        var messages = req.Messages.Select(ToAnthropicMessage).ToArray();

        var body = new
        {
            model = req.Model,
            max_tokens = req.MaxTokens,
            system = systemBlocks,
            tools = toolsArr,
            messages
        };

        using var http = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = JsonContent.Create(body, options: JsonOpts)
        };
        http.Headers.Add("x-api-key", _apiKey);
        http.Headers.Add("anthropic-version", "2023-06-01");

        try
        {
            using var resp = await Http.SendAsync(http, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return new AgentBackendResponse(null, null,
                    AgentErrorText.Http(ProviderName, Endpoint, (int)resp.StatusCode, Truncate(json, 500)));

            using var doc = JsonDocument.Parse(json);
            string? text = null;
            AgentToolCallOut? tool = null;
            foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
            {
                var type = block.GetProperty("type").GetString();
                if (type == "text")
                    text = (text ?? "") + block.GetProperty("text").GetString();
                else if (type == "tool_use")
                    tool = new AgentToolCallOut(
                        block.GetProperty("id").GetString() ?? "",
                        block.GetProperty("name").GetString() ?? "",
                        block.GetProperty("input").GetRawText());
            }
            return new AgentBackendResponse(text, tool, null, ReadUsage(doc.RootElement));
        }
        catch (Exception ex)
        {
            return new AgentBackendResponse(null, null,
                AgentErrorText.Exception(ProviderName, Endpoint, ex.Message));
        }
    }

    private static object ToAnthropicMessage(AgentMessage m)
    {
        if (m.Role == "user")
        {
            if (m.ToolResult is not null)
            {
                var blocks = new List<object>
                {
                    new
                    {
                        type = "tool_result",
                        tool_use_id = m.ToolResult.ToolCallId,
                        content = m.ToolResult.Content
                    }
                };
                if (!string.IsNullOrEmpty(m.Text))
                    blocks.Add(new { type = "text", text = m.Text });
                return new { role = "user", content = blocks.ToArray() };
            }
            return new { role = "user", content = m.Text ?? "" };
        }
        else
        {
            var blocks = new List<object>();
            if (!string.IsNullOrEmpty(m.Text))
                blocks.Add(new { type = "text", text = m.Text });
            if (m.ToolCall is not null)
                blocks.Add(new
                {
                    type = "tool_use",
                    id = m.ToolCall.Id,
                    name = m.ToolCall.Name,
                    input = JsonSerializer.Deserialize<JsonElement>(m.ToolCall.InputJson)
                });
            return new { role = "assistant", content = blocks.ToArray() };
        }
    }

    /// <summary>
    /// <para><b>شَكلُ أَنثروبيك</b>:
    /// <c>usage.{input_tokens, output_tokens, cache_creation_input_tokens,
    /// cache_read_input_tokens}</c>.</para>
    ///
    /// <para><b>وهُوَ المُزَوِّدُ الوَحيدُ الَّذي يَرُدُّ الأَربَعَةَ
    /// مُتَبايِنَةً أَصلاً</b>: <c>input_tokens</c> عِندَه <b>لا
    /// يَشمَلُ</b> المُخَزَّن، فَلا طَرحَ هُنا. ولا يُفتَرَضُ ذلك عَن
    /// غَيرِه (‏<c>No_backend_reads_another_backends_shape</c>).</para>
    ///
    /// <para>و<c>cache_creation_input_tokens</c> هو ثَمَنُ
    /// <c>cache_control</c> الَّذي يَضَعُه <see cref="CallAsync"/> على
    /// آخِرِ أَداةٍ وعلى كُتلَةِ النِظام — أَي أَنّ التَعليقَ «‏≈80%
    /// خَصم» صارَ لَه عَدّاد.</para>
    /// </summary>
    public static AgentUsage? ReadUsage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("usage", out var u)
            || u.ValueKind != JsonValueKind.Object
            || !u.TryGetProperty("input_tokens", out _)) return null;

        return new AgentUsage(
            UsageJson.NonNegative(u, "input_tokens"),
            UsageJson.NonNegative(u, "output_tokens"),
            UsageJson.NonNegative(u, "cache_creation_input_tokens"),
            UsageJson.NonNegative(u, "cache_read_input_tokens"));
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}

// ─── Gemini ──────────────────────────────────────────────────────────
public sealed class GeminiBackend : IAgentBackend
{
    private readonly string _apiKey;
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://generativelanguage.googleapis.com/"),
        Timeout = TimeSpan.FromSeconds(60)
    };
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public GeminiBackend(AgentProfile profile) => _apiKey = profile.ApiKey;

    public string ProviderName => "gemini";
    public string DefaultModel => "gemini-2.0-flash";
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);
    public string Endpoint => Http.BaseAddress!.ToString();

    public async Task<AgentBackendResponse> CallAsync(AgentRequest req, CancellationToken ct)
    {
        var contents = req.Messages.Select(ToGeminiContent).ToArray();
        var body = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = req.SystemPrompt } }
            },
            contents,
            tools = new[]
            {
                new
                {
                    functionDeclarations = req.Tools.Select(t => new
                    {
                        name = t.Name,
                        description = t.Description,
                        parameters = JsonSerializer.Deserialize<JsonElement>(t.InputSchemaJson)
                    }).ToArray()
                }
            },
            generationConfig = new { maxOutputTokens = req.MaxTokens }
        };

        var url = $"v1beta/models/{req.Model}:generateContent?key={Uri.EscapeDataString(_apiKey)}";

        try
        {
            using var resp = await Http.PostAsJsonAsync(url, body, JsonOpts, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return new AgentBackendResponse(null, null,
                    AgentErrorText.Http(ProviderName, Endpoint, (int)resp.StatusCode, Truncate(json, 500)));

            using var doc = JsonDocument.Parse(json);
            string? text = null;
            AgentToolCallOut? tool = null;
            if (doc.RootElement.TryGetProperty("candidates", out var cands) &&
                cands.GetArrayLength() > 0)
            {
                var content = cands[0].GetProperty("content");
                if (content.TryGetProperty("parts", out var parts))
                {
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var tEl) &&
                            tEl.ValueKind == JsonValueKind.String)
                            text = (text ?? "") + tEl.GetString();
                        else if (part.TryGetProperty("functionCall", out var fc))
                            tool = new AgentToolCallOut(
                                // Gemini لا يُعيد id — نُوَلِّد واحِداً لِنَستَخدِمَه
                                // داخِليّاً في الـ tool_result لاحِقاً.
                                "call_" + Guid.NewGuid().ToString("N")[..12],
                                fc.GetProperty("name").GetString() ?? "",
                                fc.GetProperty("args").GetRawText());
                    }
                }
            }
            return new AgentBackendResponse(text, tool, null, ReadUsage(doc.RootElement));
        }
        catch (Exception ex)
        {
            return new AgentBackendResponse(null, null,
                AgentErrorText.Exception(ProviderName, Endpoint, ex.Message));
        }
    }

    private static object ToGeminiContent(AgentMessage m)
    {
        var role = m.Role == "assistant" ? "model" : "user";
        var parts = new List<object>();
        if (!string.IsNullOrEmpty(m.Text)) parts.Add(new { text = m.Text });
        if (m.ToolCall is not null)
            parts.Add(new
            {
                functionCall = new
                {
                    name = m.ToolCall.Name,
                    args = JsonSerializer.Deserialize<JsonElement>(m.ToolCall.InputJson)
                }
            });
        if (m.ToolResult is not null)
            parts.Add(new
            {
                functionResponse = new
                {
                    name = m.ToolResult.ToolName,
                    response = new { content = m.ToolResult.Content }
                }
            });
        return new { role, parts = parts.ToArray() };
    }

    /// <summary>
    /// <para><b>شَكلُ جيميناي</b>:
    /// <c>usageMetadata.{promptTokenCount, candidatesTokenCount,
    /// cachedContentTokenCount}</c> — اسمٌ آخَرُ ومَعجَمٌ آخَر.</para>
    ///
    /// <para><b>و<c>promptTokenCount</c> يَشمَلُ المُخَزَّن</b> (خِلافُ
    /// أَنثروبيك حَرفاً) — فَلَو خُزِّنَ كَما وَرَدَ لَحُسِبَتِ
    /// التوكناتُ المُخَزَّنَةُ <b>مَرَّتَين</b>: بِسِعرِ المُدخَلِ
    /// وبِسِعرِ القِراءَة. والوَحدَةُ المُعلَنَةُ
    /// (<see cref="AgentUsage"/>) أَربَعَةٌ مُتَبايِنَة، فَيُطرَح.</para>
    ///
    /// <para><b>ولا كِتابَةَ كاشٍ تُعَدُّ هُنا</b>: كاشُ جيميناي
    /// (<c>cachedContents</c>) يُنشَأُ بِنِداءٍ مُستَقِلٍّ ويُفَوتَرُ
    /// بِالتَخزينِ لا بِالكِتابَة، و<see cref="CallAsync"/> لا يُنشِئُه
    /// أَصلاً. فَصِفرٌ هُنا <b>مَقيسٌ لا مُهمَل</b>.</para>
    /// </summary>
    public static AgentUsage? ReadUsage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("usageMetadata", out var u)
            || u.ValueKind != JsonValueKind.Object) return null;

        var prompt = UsageJson.NonNegative(u, "promptTokenCount");
        var cached = UsageJson.NonNegative(u, "cachedContentTokenCount");
        return new AgentUsage(
            Math.Max(0, prompt - cached),
            UsageJson.NonNegative(u, "candidatesTokenCount"),
            0,
            cached);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}

// ─── OpenAI (وَمُتَوافِقاتُه: GitHub Models, Groq, Cerebras, OpenRouter, Ollama…) ───
// أَيّ مُزَوِّد يَتَكَلَّم Chat Completions API يَعمَل بِتَبديل
// BaseUrl في مِلَفّ الوَكيل فَقَط (‏Agents:{Name}:BaseUrl أَو Agent:BaseUrl
// القَديم). أَمثِلَة في docs/LLM-ALTERNATIVES.md و docs/AGENT-TOOLS.md §1.
public sealed class OpenAIBackend : IAgentBackend
{
    private readonly string _apiKey;
    private readonly string _providerName;
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public OpenAIBackend(AgentProfile profile)
    {
        _apiKey = profile.ApiKey;
        var baseUrl = (profile.BaseUrl ?? "https://api.openai.com/").TrimEnd('/') + "/";
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(60)
        };
        _providerName = (profile.ProviderLabel ?? InferProvider(baseUrl)).ToLowerInvariant();
    }

    /// <summary>العُنوان الفِعليّ — لِلتَحَقُّق ولِتَمييز الخَلفيّات في السِجِلّ.</summary>
    public string BaseUrl => _http.BaseAddress!.ToString();

    /// <inheritdoc/>
    public string Endpoint => BaseUrl;

    private static string InferProvider(string baseUrl)
    {
        if (baseUrl.Contains("groq"))       return "groq";
        if (baseUrl.Contains("cerebras"))   return "cerebras";
        if (baseUrl.Contains("openrouter")) return "openrouter";
        if (baseUrl.Contains("11434") || baseUrl.Contains("localhost")) return "ollama";
        return "openai";
    }

    public string ProviderName => _providerName;
    public string DefaultModel => "gpt-4o";
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey) || _providerName == "ollama";

    public async Task<AgentBackendResponse> CallAsync(AgentRequest req, CancellationToken ct)
    {
        // OpenAI يُخَزِّن prefixes تلقائيّاً (>1024 token) — لا config مَطلوب.
        var messages = new List<object>
        {
            new { role = "system", content = req.SystemPrompt }
        };
        foreach (var m in req.Messages) messages.Add(ToOpenAIMessage(m));

        var body = new
        {
            model = req.Model,
            messages = messages.ToArray(),
            tools = req.Tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = JsonSerializer.Deserialize<JsonElement>(t.InputSchemaJson)
                }
            }).ToArray(),
            max_completion_tokens = req.MaxTokens
        };

        using var http = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = JsonContent.Create(body, options: JsonOpts)
        };
        http.Headers.Add("Authorization", "Bearer " + _apiKey);

        try
        {
            using var resp = await _http.SendAsync(http, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return new AgentBackendResponse(null, null,
                    AgentErrorText.Http(ProviderName, Endpoint, (int)resp.StatusCode, Truncate(json, 500)));

            using var doc = JsonDocument.Parse(json);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
                return new AgentBackendResponse(null, null,
                    AgentErrorText.Malformed(ProviderName, Endpoint, "no choices"));
            var msg = choices[0].GetProperty("message");

            string? text = msg.TryGetProperty("content", out var c) &&
                           c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            AgentToolCallOut? tool = null;
            if (msg.TryGetProperty("tool_calls", out var tc) &&
                tc.ValueKind == JsonValueKind.Array && tc.GetArrayLength() > 0)
            {
                var first = tc[0];
                var fn = first.GetProperty("function");
                tool = new AgentToolCallOut(
                    first.GetProperty("id").GetString() ?? "",
                    fn.GetProperty("name").GetString() ?? "",
                    fn.GetProperty("arguments").GetString() ?? "{}");
            }
            return new AgentBackendResponse(text, tool, null, ReadUsage(doc.RootElement));
        }
        catch (Exception ex)
        {
            return new AgentBackendResponse(null, null,
                AgentErrorText.Exception(ProviderName, Endpoint, ex.Message));
        }
    }

    private static object ToOpenAIMessage(AgentMessage m)
    {
        if (m.Role == "assistant")
        {
            var obj = new Dictionary<string, object?> { ["role"] = "assistant" };
            if (!string.IsNullOrEmpty(m.Text)) obj["content"] = m.Text;
            if (m.ToolCall is not null)
                obj["tool_calls"] = new[]
                {
                    new
                    {
                        id = m.ToolCall.Id,
                        type = "function",
                        function = new
                        {
                            name = m.ToolCall.Name,
                            arguments = m.ToolCall.InputJson
                        }
                    }
                };
            return obj;
        }
        else
        {
            if (m.ToolResult is not null)
                return new
                {
                    role = "tool",
                    tool_call_id = m.ToolResult.ToolCallId,
                    content = m.ToolResult.Content
                };
            return new { role = "user", content = m.Text ?? "" };
        }
    }

    /// <summary>
    /// <para><b>شَكلُ OpenAI ومُتَوافِقاتِه</b>:
    /// <c>usage.{prompt_tokens, completion_tokens,
    /// prompt_tokens_details.cached_tokens}</c>.</para>
    ///
    /// <para><b>ويَتَقاسَمُ الاسمَ <c>usage</c> مَعَ أَنثروبيك
    /// ويُخالِفُه في المَعجَمِ كُلِّه</b> — فَالقارِئُ يَنظُرُ في
    /// المِفتاحِ الداخِليِّ لا في الاسمِ الخارِجيّ، ويُقاسُ ذلك
    /// بِإعطاءِ كُلِّ قارِئٍ جِسمَ الآخَر.</para>
    ///
    /// <para><b>و<c>prompt_tokens</c> يَشمَلُ المُخَزَّن</b> كَما عِندَ
    /// جيميناي — فَيُطرَح. والتَخزينُ هُنا <b>تِلقائيٌّ بِلا
    /// إعداد</b> (‏prefix &gt; 1024 توكن) كَما يَقولُ تَعليقُ
    /// <see cref="CallAsync"/>، فَلا عَدّادَ كِتابَةٍ يَرُدُّه
    /// المُزَوِّدُ ولا كِتابَةَ تُفَوتَر.</para>
    /// </summary>
    public static AgentUsage? ReadUsage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("usage", out var u)
            || u.ValueKind != JsonValueKind.Object
            || !u.TryGetProperty("prompt_tokens", out _)) return null;

        var cached = u.TryGetProperty("prompt_tokens_details", out var d)
                     && d.ValueKind == JsonValueKind.Object
            ? UsageJson.NonNegative(d, "cached_tokens") : 0;

        return new AgentUsage(
            Math.Max(0, UsageJson.NonNegative(u, "prompt_tokens") - cached),
            UsageJson.NonNegative(u, "completion_tokens"),
            0,
            cached);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
