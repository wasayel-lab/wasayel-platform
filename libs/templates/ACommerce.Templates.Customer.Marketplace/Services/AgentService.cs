using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.Shared;
using Marten;
using Microsoft.Extensions.Configuration;

namespace ACommerce.Templates.Customer.Marketplace.Services;

// ─── حالَة المُحادَثَة ─────────────────────────────────────────────────
// نَحفَظها كَ Marten doc واحِد في المُستَأجِر الافتراضي. لا multi-user
// بَعد — الـ /admin بِلا مُصادَقَة (يُفتَرَض أَنَّه خَلف VPN/proxy).

public sealed class AgentSession
{
    public string Id { get; set; } = SessionId;
    public List<AgentTurn> Turns { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public const string SessionId = "agent-default";
}

public sealed class AgentTurn
{
    public string Role { get; set; } = "";   // "user" | "assistant"
    public string? Text { get; set; }
    public AgentToolCall? Tool { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}

public sealed class AgentToolCall
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string InputJson { get; set; } = "";
    public string Status { get; set; } = "pending"; // pending | applied | rejected | error
    public string? Result { get; set; }
}

// ─── الخِدمَة ────────────────────────────────────────────────────────────
public sealed class AgentService
{
    private readonly string _apiKey;
    private readonly string _model;
    private readonly IDocumentStore _store;
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri("https://api.anthropic.com/"),
        Timeout = TimeSpan.FromSeconds(60)
    };

    public AgentService(IConfiguration cfg, IDocumentStore store)
    {
        _apiKey = cfg["Anthropic:ApiKey"]
                  ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                  ?? "";
        _model  = cfg["Anthropic:Model"] ?? "claude-sonnet-4-6";
        _store  = store;
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    // الـ /admin مَفتوح بِلا tenant context — نَستَخدِم tenant ثابِت "_admin"
    // لِجَميع وَثائِق الوَكيل، لِيَتَوافَق مَع سياسَة AllDocumentsAreMultiTenanted.
    private const string AdminTenant = "_admin";

    public async Task<AgentSession> LoadSessionAsync(CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(AdminTenant);
        return await s.LoadAsync<AgentSession>(AgentSession.SessionId, ct)
               ?? new AgentSession();
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        await using var s = _store.LightweightSession(AdminTenant);
        s.Delete<AgentSession>(AgentSession.SessionId);
        await s.SaveChangesAsync(ct);
    }

    public async Task<AgentSession> AskAsync(string userMessage, CancellationToken ct = default)
    {
        await using var sess = _store.LightweightSession(AdminTenant);
        var session = await sess.LoadAsync<AgentSession>(AgentSession.SessionId, ct)
                      ?? new AgentSession();
        session.Turns.Add(new AgentTurn { Role = "user", Text = userMessage });
        await CallClaudeAsync(session, ct);
        session.UpdatedAt = DateTime.UtcNow;
        sess.Store(session);
        await sess.SaveChangesAsync(ct);
        return session;
    }

    public async Task<AgentSession> ContinueAfterToolAsync(CancellationToken ct = default)
    {
        await using var sess = _store.LightweightSession(AdminTenant);
        var session = await sess.LoadAsync<AgentSession>(AgentSession.SessionId, ct);
        if (session is null) return new AgentSession();
        await CallClaudeAsync(session, ct);
        session.UpdatedAt = DateTime.UtcNow;
        sess.Store(session);
        await sess.SaveChangesAsync(ct);
        return session;
    }

    public async Task UpdateToolStatusAsync(
        string toolId, string status, string? result, CancellationToken ct = default)
    {
        await using var sess = _store.LightweightSession(AdminTenant);
        var session = await sess.LoadAsync<AgentSession>(AgentSession.SessionId, ct);
        if (session is null) return;
        var turn = session.Turns.LastOrDefault(t => t.Tool?.Id == toolId);
        if (turn?.Tool is null) return;
        turn.Tool.Status = status;
        turn.Tool.Result = result;
        session.UpdatedAt = DateTime.UtcNow;
        sess.Store(session);
        await sess.SaveChangesAsync(ct);
    }

    // ───────────────────────── نِداء Claude ─────────────────────────
    private async Task CallClaudeAsync(AgentSession session, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            session.Turns.Add(new AgentTurn
            {
                Role = "assistant",
                Text = "لا يوجَد مِفتاح Anthropic مَضبوط. أَضِف `Anthropic:ApiKey` في "
                     + "appsettings.Local.json أَو مُتَغَيِّر بيئَة `ANTHROPIC_API_KEY`."
            });
            return;
        }

        var snapshot = await BuildTenantSnapshotAsync(ct);
        var system = BuildSystemPrompt(snapshot);
        var messages = BuildMessages(session);
        var tools = BuildTools();

        var body = new
        {
            model = _model,
            max_tokens = 2048,
            system,
            tools,
            messages
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = JsonContent.Create(body, options: new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            })
        };
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        string json;
        try
        {
            using var resp = await Http.SendAsync(req, ct);
            json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                session.Turns.Add(new AgentTurn
                {
                    Role = "assistant",
                    Text = $"⚠️ خَطَأ مِن Anthropic ({(int)resp.StatusCode}): {Truncate(json, 600)}"
                });
                return;
            }
        }
        catch (Exception ex)
        {
            session.Turns.Add(new AgentTurn
            {
                Role = "assistant",
                Text = "⚠️ تَعَذَّر الاتِّصال بِـ Anthropic API: " + ex.Message
            });
            return;
        }

        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("content");
        string? text = null;
        AgentToolCall? tool = null;
        foreach (var block in content.EnumerateArray())
        {
            var type = block.GetProperty("type").GetString();
            if (type == "text")
                text = (text ?? "") + block.GetProperty("text").GetString();
            else if (type == "tool_use")
                tool = new AgentToolCall
                {
                    Id = block.GetProperty("id").GetString() ?? "",
                    Name = block.GetProperty("name").GetString() ?? "",
                    InputJson = block.GetProperty("input").GetRawText(),
                    Status = "pending"
                };
        }

        session.Turns.Add(new AgentTurn
        {
            Role = "assistant",
            Text = text,
            Tool = tool
        });
    }

    private async Task<string> BuildTenantSnapshotAsync(CancellationToken ct)
    {
        await using var q = _store.QuerySession();
        var tenants = await q.Query<Tenant>().ToListAsync(ct);
        if (tenants.Count == 0) return "(لا مُستَأجِرين بَعد.)";
        var sb = new StringBuilder();
        foreach (var t in tenants)
        {
            sb.Append("- ").Append(t.Slug).Append(" «").Append(t.Name).Append("» ")
              .Append("channel=").Append(t.AuthChannel)
              .Append(", color=").Append(t.BrandColor)
              .Append(", city=").Append(t.City)
              .Append(", categories=[");
            sb.Append(string.Join(", ",
                t.Categories.OrderBy(c => c.SortOrder)
                    .Select(c => $"{c.Slug}({c.Kind})")));
            sb.AppendLine("]");
        }
        return sb.ToString();
    }

    private static string BuildSystemPrompt(string snapshot) => $$"""
أَنتَ وَكيل إداري لِمنصّة ACommerce SaaS مُتَعَدِّدَة المُستَأجِرين.
مَهَمَّتُكَ مُساعَدَة المُشرِف عَلى:
- إنشاء مَتاجِر (مُستَأجِرين) جَديدَة
- تَعديل أَشجار الفِئات
- تَعديل المُدُن والأَحياء
- تَعديل الخَصائِص الديناميكِيَّة (لِإعلانات فِئَة، أَو لِلبروفايل)
- تَعديل الهُويَّة البَصَريَّة (اسم/لَون/شِعار/مَدينَة/قَناة الدُخول)

قَواعِد:
1. اِستَخدِم الأَدَوات لِكُلّ كِتابَة. لا تَكتُب SQL. لا تَختَرِع APIs.
2. في كُلّ دَور assistant، نِداء أَداة واحِدَة فَقَط كَحَدّ أَقصى.
3. كُلّ نِداء أَداة يَنتَظِر مُوافَقَة المُشرِف. لا تَفتَرِض النَّجاح
   قَبل أَن تَرى tool_result.
4. عِند الغُموض اِسأَل قَبل أَن تَكتُب. خاصَّةً:
   - channel الدُخول: "phone" (هاتِف+OTP) أَو "nafath" (نَفاذ سُعودي).
   - اللَون hex (مَثَلاً #1d4ed8).
   - الـ slug (حُروف صَغيرَة وأَرقام و - و _).
5. الـ scope_id في set_attributes:
   - Guid فِئَة مَوجودَة لِخَصائِص إعلاناتها، أَو
   - "00000000-0000-0000-0000-000000000F01" sentinel لِخَصائِص البروفايل.
   لاحِظ: لِفِئَة جَديدَة، scope_id يُحسَب مِن MD5("{slug}-listing:{cat-slug}")
   إلّا لِـ ashare الَّذي يَستَخدِم Guidات ثابِتَة. عِند الشَكّ اِسأَل المُستَخدِم
   أَن يُرسِل الـ scope_id مِن صَفحَة /admin/tenants/{slug}/attributes.
6. الفِئات تُجَمَّع بِـ "kind": residential, commercial, events, vehicles,
   roommate، أَو فارِغ.
7. لُغَتُكَ الافتراضيَّة العَرَبيَّة الفُصحى مَع تَشكيل خَفيف.

حالَة المُستَأجِرين الحالِيَّة:
{{snapshot}}
""";

    // ─── تَحويل turns إلى Anthropic messages ─────────────────────────
    private static object[] BuildMessages(AgentSession session)
    {
        var list = new List<object>();
        AgentTurn? pendingToolTurn = null;

        foreach (var t in session.Turns)
        {
            if (t.Role == "user")
            {
                if (pendingToolTurn?.Tool is not null)
                {
                    list.Add(new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new
                            {
                                type = "tool_result",
                                tool_use_id = pendingToolTurn.Tool.Id,
                                content = ToolResultText(pendingToolTurn.Tool)
                            },
                            new { type = "text", text = t.Text ?? "" }
                        }
                    });
                    pendingToolTurn = null;
                }
                else
                {
                    list.Add(new { role = "user", content = t.Text ?? "" });
                }
            }
            else // assistant
            {
                var blocks = new List<object>();
                if (!string.IsNullOrEmpty(t.Text))
                    blocks.Add(new { type = "text", text = t.Text });
                if (t.Tool is not null)
                {
                    blocks.Add(new
                    {
                        type = "tool_use",
                        id = t.Tool.Id,
                        name = t.Tool.Name,
                        input = JsonSerializer.Deserialize<JsonElement>(t.Tool.InputJson)
                    });
                    pendingToolTurn = t;
                }
                else
                {
                    pendingToolTurn = null;
                }
                list.Add(new { role = "assistant", content = blocks.ToArray() });
            }
        }

        // إن كانَ آخِر دَور assistant فيه tool resolved وَلا يوجَد user يَتبَعه،
        // أَضِف user بِـ tool_result فَقَط لِيَستَجيب Claude.
        if (pendingToolTurn?.Tool is { Status: not "pending" })
        {
            list.Add(new
            {
                role = "user",
                content = new object[]
                {
                    new
                    {
                        type = "tool_result",
                        tool_use_id = pendingToolTurn.Tool!.Id,
                        content = ToolResultText(pendingToolTurn.Tool!)
                    }
                }
            });
        }

        return list.ToArray();
    }

    private static string ToolResultText(AgentToolCall tool) => tool.Status switch
    {
        "applied"  => tool.Result ?? "تَمّ التَّنفيذ بِنَجاح.",
        "rejected" => "رَفَضَ المُشرِف هذا الإجراء — لا تُعِد المُحاوَلَة بِدون تَوجيهٍ مِنه.",
        "error"    => "حَدَثَ خَطَأ أَثناء التَّنفيذ: " + (tool.Result ?? "غَير مَعروف"),
        _          => "لَم يُوافِق المُشرِف بَعد عَلى هذا الإجراء."
    };

    private static object[] BuildTools() => new object[]
    {
        new
        {
            name = "create_tenant",
            description = "إنشاء مَتجَر (مُستَأجِر) جَديد. الـ slug يَجِب أَن يَكون فَريداً.",
            input_schema = new
            {
                type = "object",
                required = new[] { "slug", "name", "color", "channel", "categories" },
                properties = new Dictionary<string, object>
                {
                    ["slug"]    = new { type = "string", description = "[a-z0-9_-]+" },
                    ["name"]    = new { type = "string" },
                    ["tagline"] = new { type = "string" },
                    ["city"]    = new { type = "string" },
                    ["color"]   = new { type = "string", description = "لَون hex مَثَلاً #1d4ed8" },
                    ["channel"] = new
                    {
                        type = "string",
                        description = "phone أَو nafath",
                        @enum = new[] { "phone", "nafath" }
                    },
                    ["categories"] = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            required = new[] { "slug", "label" },
                            properties = new Dictionary<string, object>
                            {
                                ["slug"]  = new { type = "string" },
                                ["label"] = new { type = "string" },
                                ["icon"]  = new { type = "string", description = "إيموجي" },
                                ["kind"]  = new { type = "string" }
                            }
                        }
                    }
                }
            }
        },
        new
        {
            name = "set_categories",
            description = "إعادَة كِتابَة قائِمَة فِئات مُستَأجِر مَوجود بِالكامِل.",
            input_schema = new
            {
                type = "object",
                required = new[] { "slug", "categories" },
                properties = new Dictionary<string, object>
                {
                    ["slug"] = new { type = "string" },
                    ["categories"] = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            required = new[] { "slug", "label" },
                            properties = new Dictionary<string, object>
                            {
                                ["slug"]  = new { type = "string" },
                                ["label"] = new { type = "string" },
                                ["icon"]  = new { type = "string" },
                                ["kind"]  = new { type = "string" }
                            }
                        }
                    }
                }
            }
        },
        new
        {
            name = "set_branding",
            description = "تَحديث الهُويَّة البَصَريَّة. كُلّ الحُقول اختِياريَّة عَدا slug.",
            input_schema = new
            {
                type = "object",
                required = new[] { "slug" },
                properties = new Dictionary<string, object>
                {
                    ["slug"]    = new { type = "string" },
                    ["name"]    = new { type = "string" },
                    ["tagline"] = new { type = "string" },
                    ["city"]    = new { type = "string" },
                    ["color"]   = new { type = "string" },
                    ["channel"] = new { type = "string", @enum = new[] { "phone", "nafath" } }
                }
            }
        },
        new
        {
            name = "set_regions",
            description = "إعادَة كِتابَة المُدُن والأَحياء لِمُستَأجِر بِالكامِل.",
            input_schema = new
            {
                type = "object",
                required = new[] { "slug", "cities" },
                properties = new Dictionary<string, object>
                {
                    ["slug"] = new { type = "string" },
                    ["cities"] = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            required = new[] { "name" },
                            properties = new Dictionary<string, object>
                            {
                                ["name"] = new { type = "string" },
                                ["districts"] = new { type = "array", items = new { type = "string" } }
                            }
                        }
                    }
                }
            }
        },
        new
        {
            name = "set_attributes",
            description =
                "إعادَة كِتابَة الخَصائِص الديناميكِيَّة لِنِطاق (scope) مَحَدَّد. "
              + "النِّطاق إمّا Guid فِئَة أَو الـ sentinel 00000000-0000-0000-0000-000000000F01 لِلبروفايل.",
            input_schema = new
            {
                type = "object",
                required = new[] { "slug", "scope_id", "definitions" },
                properties = new Dictionary<string, object>
                {
                    ["slug"]     = new { type = "string" },
                    ["scope_id"] = new { type = "string" },
                    ["definitions"] = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            required = new[] { "code", "name", "type" },
                            properties = new Dictionary<string, object>
                            {
                                ["code"] = new { type = "string" },
                                ["name"] = new { type = "string" },
                                ["type"] = new
                                {
                                    type = "string",
                                    @enum = new[] { "Text","LongText","Number","Boolean",
                                                    "SingleSelect","MultiSelect","Date" }
                                },
                                ["required"] = new { type = "boolean" },
                                ["options"] = new
                                {
                                    type = "array",
                                    items = new
                                    {
                                        type = "object",
                                        required = new[] { "value", "label" },
                                        properties = new Dictionary<string, object>
                                        {
                                            ["value"] = new { type = "string" },
                                            ["label"] = new { type = "string" }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    };

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}

// ─── مُنَفِّذ الأَدَوات ───────────────────────────────────────────────────
// مَفصول عَن AgentService لِيَسهُل اختِبارُه ولِتَجَنُّب الاعتِماد الدائِري
// مَع TenantOps. كُلّ tool يُحَوَّل إلى نِداء واحِد عَلى TenantOps.
public sealed class AgentToolExecutor
{
    private readonly IDocumentStore _store;
    public AgentToolExecutor(IDocumentStore store) { _store = store; }

    public async Task<(bool Ok, string Message)> ExecuteAsync(
        string toolName, string inputJson, CancellationToken ct = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            var root = doc.RootElement;
            return toolName switch
            {
                "create_tenant"  => await CreateTenantAsync(root, ct),
                "set_categories" => await SetCategoriesAsync(root, ct),
                "set_branding"   => await SetBrandingAsync(root, ct),
                "set_regions"    => await SetRegionsAsync(root, ct),
                "set_attributes" => await SetAttributesAsync(root, ct),
                _ => (false, $"أَداة غَير مَعروفَة: {toolName}")
            };
        }
        catch (Exception ex)
        {
            return (false, "خَطَأ في تَنفيذ الأَداة: " + ex.Message);
        }
    }

    private async Task<(bool, string)> CreateTenantAsync(JsonElement root, CancellationToken ct)
    {
        var slug    = Str(root, "slug").ToLowerInvariant();
        var name    = Str(root, "name");
        var color   = Str(root, "color");
        var tagline = Str(root, "tagline");
        var city    = Str(root, "city");
        var channel = Str(root, "channel");
        if (channel != "phone" && channel != "nafath") channel = "phone";
        if (!System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9_-]+$"))
            return (false, "slug غَير صالِح.");
        if (string.IsNullOrEmpty(name)) return (false, "الاسم مَطلوب.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$"))
            return (false, "لَون غَير صالِح.");

        var cats = ParseCategories(root);
        if (cats.Count == 0) return (false, "يَجِب فِئَة واحِدَة عَلى الأَقَلّ.");

        await using var s = _store.LightweightSession();
        var existing = await s.LoadAsync<Tenant>(slug, ct);
        if (existing is not null) return (false, $"الـ slug «{slug}» مَوجود بِالفِعل.");

        s.Store(new Tenant
        {
            Id = slug, Name = name, BrandColor = color, TagLine = tagline,
            City = city, AuthChannel = channel, Categories = cats,
            CreatedAt = DateTime.UtcNow
        });
        await s.SaveChangesAsync(ct);
        return (true, $"تَمّ إنشاء «{slug}» بِـ {cats.Count} فِئَات.");
    }

    private async Task<(bool, string)> SetCategoriesAsync(JsonElement root, CancellationToken ct)
    {
        var slug = Str(root, "slug").ToLowerInvariant();
        var cats = ParseCategories(root);
        if (cats.Count == 0) return (false, "يَجِب فِئَة واحِدَة عَلى الأَقَلّ.");

        await using var s = _store.LightweightSession();
        var t = await s.LoadAsync<Tenant>(slug, ct);
        if (t is null) return (false, "المَتجَر غَير مَوجود.");
        t.Categories = cats;
        s.Store(t);
        await s.SaveChangesAsync(ct);
        return (true, $"تَمّ تَحديث «{slug}» إلى {cats.Count} فِئَات.");
    }

    private async Task<(bool, string)> SetBrandingAsync(JsonElement root, CancellationToken ct)
    {
        var slug = Str(root, "slug").ToLowerInvariant();
        await using var s = _store.LightweightSession();
        var t = await s.LoadAsync<Tenant>(slug, ct);
        if (t is null) return (false, "المَتجَر غَير مَوجود.");

        if (TryStr(root, "name", out var name))       t.Name = name;
        if (TryStr(root, "tagline", out var tagline)) t.TagLine = tagline;
        if (TryStr(root, "city", out var city))       t.City = city;
        if (TryStr(root, "color", out var color))
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$"))
                return (false, "لَون غَير صالِح.");
            t.BrandColor = color;
        }
        if (TryStr(root, "channel", out var channel))
        {
            if (channel != "phone" && channel != "nafath") return (false, "channel غَير صالِح.");
            t.AuthChannel = channel;
        }
        s.Store(t);
        await s.SaveChangesAsync(ct);
        return (true, $"تَمّ تَحديث هُويَّة «{slug}».");
    }

    private async Task<(bool, string)> SetRegionsAsync(JsonElement root, CancellationToken ct)
    {
        var slug = Str(root, "slug").ToLowerInvariant();
        if (!root.TryGetProperty("cities", out var citiesArr) ||
            citiesArr.ValueKind != JsonValueKind.Array)
            return (false, "cities مَطلوب.");

        await using var sgl = _store.QuerySession();
        var t = await sgl.LoadAsync<Tenant>(slug, ct);
        if (t is null) return (false, "المَتجَر غَير مَوجود.");

        await using var s = _store.LightweightSession(slug);
        var existing = await s.Query<ImportedRecord>()
            .Where(r => r.Table == "DiscoveryRegions").ToListAsync(ct);
        foreach (var r in existing) s.Delete(r);

        var now = DateTime.UtcNow;
        var cityCount = 0; var distCount = 0;
        foreach (var c in citiesArr.EnumerateArray())
        {
            var cname = Str(c, "name").Trim();
            if (string.IsNullOrEmpty(cname)) continue;
            var cityId = Guid.NewGuid().ToString();
            s.Store(new ImportedRecord
            {
                Id = $"DiscoveryRegions/{cityId}",
                Table = "DiscoveryRegions",
                SourceId = cityId,
                ImportedAt = now,
                Data = new Dictionary<string, object?>
                {
                    ["Name"] = cname, ["ParentId"] = null, ["Level"] = "1"
                }
            });
            cityCount++;
            if (c.TryGetProperty("districts", out var distArr) &&
                distArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in distArr.EnumerateArray())
                {
                    var dname = d.GetString()?.Trim();
                    if (string.IsNullOrEmpty(dname)) continue;
                    var did = Guid.NewGuid().ToString();
                    s.Store(new ImportedRecord
                    {
                        Id = $"DiscoveryRegions/{did}",
                        Table = "DiscoveryRegions",
                        SourceId = did,
                        ImportedAt = now,
                        Data = new Dictionary<string, object?>
                        {
                            ["Name"] = dname, ["ParentId"] = cityId, ["Level"] = "2"
                        }
                    });
                    distCount++;
                }
            }
        }
        await s.SaveChangesAsync(ct);
        return (true, $"تَمّ تَحديث «{slug}» إلى {cityCount} مُدُن، {distCount} أَحياء.");
    }

    private async Task<(bool, string)> SetAttributesAsync(JsonElement root, CancellationToken ct)
    {
        var slug = Str(root, "slug").ToLowerInvariant();
        var scopeStr = Str(root, "scope_id");
        if (!Guid.TryParse(scopeStr, out var scopeId))
            return (false, "scope_id غَير صالِح.");
        if (!root.TryGetProperty("definitions", out var defsArr) ||
            defsArr.ValueKind != JsonValueKind.Array)
            return (false, "definitions مَطلوب.");

        await using var sgl = _store.QuerySession();
        var t = await sgl.LoadAsync<Tenant>(slug, ct);
        if (t is null) return (false, "المَتجَر غَير مَوجود.");

        await using var s = _store.LightweightSession(slug);
        var allMappings = await s.Query<ImportedRecord>()
            .Where(r => r.Table == "CategoryAttributeMappings").ToListAsync(ct);
        var allDefs = await s.Query<ImportedRecord>()
            .Where(r => r.Table == "AttributeDefinitions").ToListAsync(ct);
        var allValues = await s.Query<ImportedRecord>()
            .Where(r => r.Table == "AttributeValues").ToListAsync(ct);

        var scopeMappings = allMappings
            .Where(m => GuidFromData(m, "CategoryId") == scopeId).ToList();
        var defIdsInScope = scopeMappings
            .Select(m => GuidFromData(m, "AttributeDefinitionId"))
            .Where(g => g != Guid.Empty).Distinct().ToList();
        foreach (var m in scopeMappings) s.Delete(m);

        var stillUsed = allMappings
            .Where(m => GuidFromData(m, "CategoryId") != scopeId)
            .Select(m => GuidFromData(m, "AttributeDefinitionId")).ToHashSet();
        var orphans = defIdsInScope.Where(id => !stillUsed.Contains(id)).ToHashSet();
        if (orphans.Count > 0)
        {
            foreach (var d in allDefs)
                if (orphans.Contains(GuidFromData(d, "Id"))) s.Delete(d);
            foreach (var v in allValues)
                if (orphans.Contains(GuidFromData(v, "AttributeDefinitionId"))) s.Delete(v);
        }

        var now = DateTime.UtcNow;
        var order = 0; var defCount = 0; var valCount = 0;
        foreach (var def in defsArr.EnumerateArray())
        {
            var code = Str(def, "code");
            var name = Str(def, "name");
            var type = Str(def, "type");
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name) ||
                string.IsNullOrEmpty(type)) continue;
            var req = def.TryGetProperty("required", out var rEl) &&
                      rEl.ValueKind == JsonValueKind.True;

            var defId = Guid.NewGuid();
            s.Store(new ImportedRecord
            {
                Id = $"AttributeDefinitions/{defId}",
                Table = "AttributeDefinitions",
                SourceId = defId.ToString(),
                ImportedAt = now,
                Data = new Dictionary<string, object?>
                {
                    ["Id"] = defId.ToString(), ["Code"] = code, ["Name"] = name,
                    ["Type"] = type, ["IsRequired"] = req ? "true" : "false"
                }
            });
            s.Store(new ImportedRecord
            {
                Id = $"CategoryAttributeMappings/{defId}-{scopeId}",
                Table = "CategoryAttributeMappings",
                SourceId = $"{defId}-{scopeId}",
                ImportedAt = now,
                Data = new Dictionary<string, object?>
                {
                    ["CategoryId"] = scopeId.ToString(),
                    ["AttributeDefinitionId"] = defId.ToString(),
                    ["SortOrder"] = order.ToString()
                }
            });
            if (def.TryGetProperty("options", out var optsArr) &&
                optsArr.ValueKind == JsonValueKind.Array)
            {
                var voi = 0;
                foreach (var op in optsArr.EnumerateArray())
                {
                    var val = Str(op, "value");
                    var lbl = Str(op, "label");
                    if (string.IsNullOrEmpty(val)) continue;
                    var vid = Guid.NewGuid();
                    s.Store(new ImportedRecord
                    {
                        Id = $"AttributeValues/{vid}",
                        Table = "AttributeValues",
                        SourceId = vid.ToString(),
                        ImportedAt = now,
                        Data = new Dictionary<string, object?>
                        {
                            ["Id"] = vid.ToString(),
                            ["AttributeDefinitionId"] = defId.ToString(),
                            ["Value"] = val, ["DisplayName"] = lbl,
                            ["SortOrder"] = voi.ToString()
                        }
                    });
                    voi++; valCount++;
                }
            }
            order++; defCount++;
        }
        await s.SaveChangesAsync(ct);
        return (true, $"تَمّ تَعريف {defCount} حُقول و {valCount} خِيارات لِنِطاق «{scopeId}».");
    }

    private static List<Category> ParseCategories(JsonElement root)
    {
        var cats = new List<Category>();
        if (!root.TryGetProperty("categories", out var arr) ||
            arr.ValueKind != JsonValueKind.Array) return cats;
        var i = 0;
        foreach (var c in arr.EnumerateArray())
        {
            var cslug = Str(c, "slug").ToLowerInvariant();
            var clabel = Str(c, "label");
            if (string.IsNullOrEmpty(cslug) || string.IsNullOrEmpty(clabel)) continue;
            cats.Add(new Category
            {
                Slug = cslug,
                Label = clabel,
                Icon  = TryStr(c, "icon", out var ic) && !string.IsNullOrEmpty(ic) ? ic : "🏠",
                Kind  = TryStr(c, "kind", out var k) ? k.ToLowerInvariant() : "",
                SortOrder = i++
            });
        }
        return cats;
    }

    private static string Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private static bool TryStr(JsonElement e, string key, out string value)
    {
        if (e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
        { value = v.GetString() ?? ""; return true; }
        value = ""; return false;
    }

    private static Guid GuidFromData(ImportedRecord r, string key)
    {
        if (!r.Data.TryGetValue(key, out var v) || v is null) return Guid.Empty;
        string? str = v is JsonElement el
            ? (el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString())
            : v.ToString();
        return Guid.TryParse(str, out var g) ? g : Guid.Empty;
    }
}
