using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

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
public sealed record AgentBackendResponse(string? Text, AgentToolCallOut? ToolCall, string? Error);

public interface IAgentBackend
{
    string ProviderName { get; }
    string DefaultModel { get; }
    bool IsConfigured { get; }
    Task<AgentBackendResponse> CallAsync(AgentRequest req, CancellationToken ct);
}

// ─── Factory + DI helper ─────────────────────────────────────────────
public static class AgentBackendFactory
{
    public static IAgentBackend Create(IConfiguration cfg)
    {
        var provider = (cfg["Agent:Provider"] ?? "anthropic").Trim().ToLowerInvariant();
        return provider switch
        {
            "gemini" => new GeminiBackend(cfg),
            "openai" => new OpenAIBackend(cfg),
            _        => new AnthropicBackend(cfg)
        };
    }
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

    public AnthropicBackend(IConfiguration cfg)
    {
        _apiKey = cfg["Agent:ApiKey"]
                  ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                  ?? "";
    }

    public string ProviderName => "anthropic";
    public string DefaultModel => "claude-sonnet-4-6";
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

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
                    $"Anthropic {(int)resp.StatusCode}: {Truncate(json, 500)}");

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
            return new AgentBackendResponse(text, tool, null);
        }
        catch (Exception ex)
        {
            return new AgentBackendResponse(null, null, "Anthropic exception: " + ex.Message);
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

    public GeminiBackend(IConfiguration cfg)
    {
        _apiKey = cfg["Agent:ApiKey"]
                  ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                  ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY")
                  ?? "";
    }

    public string ProviderName => "gemini";
    public string DefaultModel => "gemini-2.0-flash";
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

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
                    $"Gemini {(int)resp.StatusCode}: {Truncate(json, 500)}");

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
            return new AgentBackendResponse(text, tool, null);
        }
        catch (Exception ex)
        {
            return new AgentBackendResponse(null, null, "Gemini exception: " + ex.Message);
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

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}

// ─── OpenAI ──────────────────────────────────────────────────────────
public sealed class OpenAIBackend : IAgentBackend
{
    private readonly string _apiKey;
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://api.openai.com/"),
        Timeout = TimeSpan.FromSeconds(60)
    };
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public OpenAIBackend(IConfiguration cfg)
    {
        _apiKey = cfg["Agent:ApiKey"]
                  ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                  ?? "";
    }

    public string ProviderName => "openai";
    public string DefaultModel => "gpt-4o";
    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

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
            using var resp = await Http.SendAsync(http, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return new AgentBackendResponse(null, null,
                    $"OpenAI {(int)resp.StatusCode}: {Truncate(json, 500)}");

            using var doc = JsonDocument.Parse(json);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
                return new AgentBackendResponse(null, null, "OpenAI: no choices");
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
            return new AgentBackendResponse(text, tool, null);
        }
        catch (Exception ex)
        {
            return new AgentBackendResponse(null, null, "OpenAI exception: " + ex.Message);
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

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
