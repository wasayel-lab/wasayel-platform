using System.Text;
using System.Text.Json;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.Shared;
using Marten;
using Microsoft.Extensions.Configuration;

namespace ACommerce.Templates.Customer.Marketplace.Services;

// ─── حالَة المُحادَثَة ─────────────────────────────────────────────────
// نَحفَظها كَ Marten doc واحِد في tenant "_admin". لا multi-user
// بَعد — الـ /admin بِلا مُصادَقَة (يُفتَرَض VPN/proxy).

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
    private readonly IAgentBackend _backend;
    private readonly string _model;
    private readonly IDocumentStore _store;
    private const string AdminTenant = "_admin";

    public AgentService(IConfiguration cfg, IDocumentStore store, IAgentBackend backend)
    {
        _backend = backend;
        _model   = cfg["Agent:Model"] ?? backend.DefaultModel;
        _store   = store;
    }

    public string ProviderName => _backend.ProviderName;
    public string ModelName    => _model;
    public bool IsConfigured   => _backend.IsConfigured;

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
        await CallBackendAsync(session, ct);
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
        await CallBackendAsync(session, ct);
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

    // ───────────────────────── نِداء الـ Backend ─────────────────────
    private async Task CallBackendAsync(AgentSession session, CancellationToken ct)
    {
        if (!_backend.IsConfigured)
        {
            session.Turns.Add(new AgentTurn
            {
                Role = "assistant",
                Text = $"لا يوجَد مِفتاح لِـ {_backend.ProviderName} مَضبوط. "
                     + "أَضِف `Agent:ApiKey` في appsettings.Local.json أَو مُتَغَيِّر بيئَة "
                     + "(ANTHROPIC_API_KEY / GEMINI_API_KEY / OPENAI_API_KEY)."
            });
            return;
        }

        var snapshot = await BuildTenantSnapshotAsync(ct);
        var system   = BuildSystemPrompt(snapshot);
        var messages = BuildAbstractMessages(session);
        var tools    = BuildAbstractTools();

        var resp = await _backend.CallAsync(
            new AgentRequest(system, messages, tools, _model, MaxTokens: 2048), ct);

        if (resp.Error is not null)
        {
            session.Turns.Add(new AgentTurn
            {
                Role = "assistant",
                Text = "⚠️ " + resp.Error
            });
            return;
        }

        var tool = resp.ToolCall is null ? null : new AgentToolCall
        {
            Id = resp.ToolCall.Id,
            Name = resp.ToolCall.Name,
            InputJson = resp.ToolCall.InputJson,
            Status = "pending"
        };
        session.Turns.Add(new AgentTurn
        {
            Role = "assistant",
            Text = resp.Text,
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
   عِند الشَكّ اِطلُب مِنَ المُشرِف فَتح صَفحَة /admin/tenants/{slug}/attributes
   وإرسال الـ scope_id.
6. الفِئات تُجَمَّع بِـ "kind": residential, commercial, events, vehicles,
   roommate، أَو فارِغ.
7. لُغَتُكَ الافتراضيَّة العَرَبيَّة الفُصحى مَع تَشكيل خَفيف.

حالَة المُستَأجِرين الحالِيَّة:
{{snapshot}}
""";

    // ─── تَحويل turns إلى AgentMessage مُحَايِدَة ─────────────────────
    private static List<AgentMessage> BuildAbstractMessages(AgentSession session)
    {
        var list = new List<AgentMessage>();
        AgentTurn? pending = null;

        foreach (var t in session.Turns)
        {
            if (t.Role == "user")
            {
                if (pending?.Tool is not null)
                {
                    list.Add(new AgentMessage(
                        "user", t.Text, null,
                        new AgentToolResult(pending.Tool.Id, pending.Tool.Name,
                                            ToolResultText(pending.Tool))));
                    pending = null;
                }
                else
                {
                    list.Add(new AgentMessage("user", t.Text, null, null));
                }
            }
            else
            {
                list.Add(new AgentMessage(
                    "assistant", t.Text,
                    t.Tool is null ? null
                        : new AgentToolCallOut(t.Tool.Id, t.Tool.Name, t.Tool.InputJson),
                    null));
                pending = t.Tool is not null ? t : null;
            }
        }

        // إن انتَهَت المُحادَثَة بِأَداة resolved بِلا user بَعدَها، أَضِف user
        // بِـ tool_result فَقَط لِيَستَجيب الـ backend.
        if (pending?.Tool is { Status: not "pending" })
        {
            list.Add(new AgentMessage(
                "user", null, null,
                new AgentToolResult(pending.Tool.Id, pending.Tool.Name,
                                    ToolResultText(pending.Tool))));
        }

        return list;
    }

    private static string ToolResultText(AgentToolCall tool) => tool.Status switch
    {
        "applied"  => tool.Result ?? "تَمّ التَّنفيذ بِنَجاح.",
        "rejected" => "رَفَضَ المُشرِف هذا الإجراء — لا تُعِد المُحاوَلَة بِدون تَوجيهٍ مِنه.",
        "error"    => "حَدَثَ خَطَأ أَثناء التَّنفيذ: " + (tool.Result ?? "غَير مَعروف"),
        _          => "لَم يُوافِق المُشرِف بَعد عَلى هذا الإجراء."
    };

    // ─── تَعريفات الأَدَوات (JSON Schema) ─────────────────────────────
    // مَكتوبَة كَ JSON خام لِتُمَرَّر مُباشَرَةً إلى الـ backends.
    private static List<AgentToolDef> BuildAbstractTools() => new()
    {
        new("create_tenant",
            "إنشاء مَتجَر (مُستَأجِر) جَديد. الـ slug يَجِب أَن يَكون فَريداً.",
            CreateTenantSchema),
        new("set_categories",
            "إعادَة كِتابَة قائِمَة فِئات مُستَأجِر مَوجود بِالكامِل.",
            SetCategoriesSchema),
        new("set_branding",
            "تَحديث الهُويَّة البَصَريَّة. كُلّ الحُقول اختِياريَّة عَدا slug.",
            SetBrandingSchema),
        new("set_regions",
            "إعادَة كِتابَة المُدُن والأَحياء لِمُستَأجِر بِالكامِل.",
            SetRegionsSchema),
        new("set_attributes",
            "إعادَة كِتابَة الخَصائِص الديناميكِيَّة لِنِطاق (scope) مَحَدَّد. "
          + "النِّطاق إمّا Guid فِئَة أَو 00000000-0000-0000-0000-000000000F01 لِلبروفايل.",
            SetAttributesSchema)
    };

    private static readonly string CategoryItemSchema = """
    {
      "type": "object",
      "required": ["slug", "label"],
      "properties": {
        "slug":  {"type": "string"},
        "label": {"type": "string"},
        "icon":  {"type": "string", "description": "إيموجي"},
        "kind":  {"type": "string"}
      }
    }
    """;

    private static readonly string CreateTenantSchema = $$"""
    {
      "type": "object",
      "required": ["slug", "name", "color", "channel", "categories"],
      "properties": {
        "slug":    {"type": "string", "description": "[a-z0-9_-]+"},
        "name":    {"type": "string"},
        "tagline": {"type": "string"},
        "city":    {"type": "string"},
        "color":   {"type": "string", "description": "لَون hex مَثَلاً #1d4ed8"},
        "channel": {"type": "string", "enum": ["phone", "nafath"]},
        "categories": {"type": "array", "items": {{CategoryItemSchema}}}
      }
    }
    """;

    private static readonly string SetCategoriesSchema = $$"""
    {
      "type": "object",
      "required": ["slug", "categories"],
      "properties": {
        "slug": {"type": "string"},
        "categories": {"type": "array", "items": {{CategoryItemSchema}}}
      }
    }
    """;

    private static readonly string SetBrandingSchema = """
    {
      "type": "object",
      "required": ["slug"],
      "properties": {
        "slug":    {"type": "string"},
        "name":    {"type": "string"},
        "tagline": {"type": "string"},
        "city":    {"type": "string"},
        "color":   {"type": "string"},
        "channel": {"type": "string", "enum": ["phone", "nafath"]}
      }
    }
    """;

    private static readonly string SetRegionsSchema = """
    {
      "type": "object",
      "required": ["slug", "cities"],
      "properties": {
        "slug": {"type": "string"},
        "cities": {
          "type": "array",
          "items": {
            "type": "object",
            "required": ["name"],
            "properties": {
              "name":      {"type": "string"},
              "districts": {"type": "array", "items": {"type": "string"}}
            }
          }
        }
      }
    }
    """;

    private static readonly string SetAttributesSchema = """
    {
      "type": "object",
      "required": ["slug", "scope_id", "definitions"],
      "properties": {
        "slug":     {"type": "string"},
        "scope_id": {"type": "string"},
        "definitions": {
          "type": "array",
          "items": {
            "type": "object",
            "required": ["code", "name", "type"],
            "properties": {
              "code":     {"type": "string"},
              "name":     {"type": "string"},
              "type":     {"type": "string", "enum": ["Text","LongText","Number","Boolean","SingleSelect","MultiSelect","Date"]},
              "required": {"type": "boolean"},
              "options": {
                "type": "array",
                "items": {
                  "type": "object",
                  "required": ["value", "label"],
                  "properties": {
                    "value": {"type": "string"},
                    "label": {"type": "string"}
                  }
                }
              }
            }
          }
        }
      }
    }
    """;
}

// ─── مُنَفِّذ الأَدَوات ───────────────────────────────────────────────────
// مَفصول عَن AgentService لِيَسهُل اختِبارُه. كُلّ tool يُحَوَّل إلى نِداء
// واحِد عَلى نَفس code-path الَّذي تَستَخدِمُه نَماذِج /admin/tenants/*/save.
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
