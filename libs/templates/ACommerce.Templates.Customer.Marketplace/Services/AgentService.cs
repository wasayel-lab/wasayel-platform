using System.Text;
using System.Text.Json;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.Shared;
using Marten;

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

    // مِلَفّ «Studio» — مُزَوِّدُه ومِفتاحُه ونَموذَجُه مُستَقِلَّة عَن وَكيل
    // التَحليل. بِلا تَهيئَة مُسَمّاة يَسقُط إلى Agent:* القَديم فَيَبقى
    // السُلوك كَما كان.
    public AgentService(IDocumentStore store, IAgentBackendProvider agents)
    {
        _backend = agents.For(AgentNames.Studio);
        _model   = agents.ModelFor(AgentNames.Studio);
        _store   = store;
    }

    public string ProviderName => _backend.ProviderName;
    public string ModelName    => _model;
    public bool IsConfigured   => _backend.IsConfigured;

    /// <summary>مُعَرِّف جَلسَة المُحادَثَة لِنِطاق مُعَيَّن. لِلادمن: جَلسَة
    /// مُشتَرَكَة (تَوافُق رَجعيّ). لِرائِد الأَعمال: جَلسَة لِكُلّ مُستَخدِم
    /// مُنفَصِلَة — لا تَسريب مُحادَثات بَين رُوَّاد الأَعمال.</summary>
    private static string SessionIdFor(string? scopeId)
        => string.IsNullOrEmpty(scopeId) ? AgentSession.SessionId : "scope:" + scopeId;

    public async Task<AgentSession> LoadSessionAsync(string? scopeId = null, CancellationToken ct = default)
    {
        await using var s = _store.QuerySession(AdminTenant);
        return await s.LoadAsync<AgentSession>(SessionIdFor(scopeId), ct)
               ?? new AgentSession { Id = SessionIdFor(scopeId) };
    }

    public async Task ResetAsync(string? scopeId = null, CancellationToken ct = default)
    {
        await using var s = _store.LightweightSession(AdminTenant);
        s.Delete<AgentSession>(SessionIdFor(scopeId));
        await s.SaveChangesAsync(ct);
    }

    public async Task<AgentSession> AskAsync(string userMessage, string? scopeId = null, CancellationToken ct = default)
    {
        await using var sess = _store.LightweightSession(AdminTenant);
        var id = SessionIdFor(scopeId);
        var session = await sess.LoadAsync<AgentSession>(id, ct)
                      ?? new AgentSession { Id = id };
        session.Turns.Add(new AgentTurn { Role = "user", Text = userMessage });
        await CallBackendAsync(session, ct);
        session.UpdatedAt = DateTime.UtcNow;
        sess.Store(session);
        await sess.SaveChangesAsync(ct);
        return session;
    }

    public async Task<AgentSession> ContinueAfterToolAsync(string? scopeId = null, CancellationToken ct = default)
    {
        await using var sess = _store.LightweightSession(AdminTenant);
        var session = await sess.LoadAsync<AgentSession>(SessionIdFor(scopeId), ct);
        if (session is null) return new AgentSession { Id = SessionIdFor(scopeId) };
        await CallBackendAsync(session, ct);
        session.UpdatedAt = DateTime.UtcNow;
        sess.Store(session);
        await sess.SaveChangesAsync(ct);
        return session;
    }

    public async Task UpdateToolStatusAsync(
        string toolId, string status, string? result, string? scopeId = null, CancellationToken ct = default)
    {
        await using var sess = _store.LightweightSession(AdminTenant);
        var session = await sess.LoadAsync<AgentSession>(SessionIdFor(scopeId), ct);
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
            sb.Append("• ").Append(t.Slug).Append(" «").Append(t.Name).Append("»")
              .Append(" — color=").Append(t.BrandColor)
              .Append(", channel=").Append(t.AuthChannel)
              .Append(", city=").Append(t.City)
              .AppendLine();
            if (t.Roles.Count > 0)
            {
                sb.Append("    roles: ").Append(
                    string.Join(", ", t.Roles.OrderBy(r => r.SortOrder)
                        .Select(r => $"{r.Slug}«{r.Label}»{(r.IsDefault ? "*" : "")}")))
                  .AppendLine();
            }
            foreach (var c in t.Categories.OrderBy(c => c.SortOrder))
            {
                var sid = DeriveListingScope(t.Slug, c.Slug);
                sb.Append("    ↳ ").Append(c.Slug)
                  .Append(" «").Append(c.Label).Append("»")
                  .Append(c.Kind.Length > 0 ? $" kind={c.Kind}" : "")
                  .Append("  scope_id=").Append(sid.ToString())
                  .AppendLine();
            }
            sb.Append("    ↳ (بروفايل) scope_id=00000000-0000-0000-0000-000000000F01")
              .AppendLine();
            foreach (var r in t.Roles.OrderBy(r => r.SortOrder))
            {
                var rsid = ACommerce.Kit.Roles.RoleScopes.DeriveProfileScope(t.Slug, r.Slug);
                sb.Append("    ↳ (بروفايل/").Append(r.Slug).Append(") scope_id=")
                  .Append(rsid.ToString()).AppendLine();
            }
        }
        return sb.ToString();
    }

    // نَفس مَنطِق التَوليد المُستَخدَم في صَفحَة /admin/tenants/{slug}/attributes
    // (DeriveListingScope) — يَجعَل scope_id الَّذي يَحسُبه الوَكيل مُطابِقاً
    // لِما يَراه المُشرِف في الـ UI.
    private static Guid DeriveListingScope(string tenantSlug, string categorySlug)
    {
        var raw = tenantSlug == "ashare"
            ? "ashare-listing:" + categorySlug.ToLowerInvariant()
            : (tenantSlug == "ejar" ? "ejar-listing:" : tenantSlug + ":listing:")
              + categorySlug.ToLowerInvariant();
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return new Guid(hash);
    }

    private static string BuildSystemPrompt(string snapshot) => $$"""
أَنتَ وَكيل إداري لِمنصّة ACommerce SaaS مُتَعَدِّدَة المُستَأجِرين.
مَهَمَّتُكَ مُساعَدَة المُشرِف عَلى:
- إنشاء مَتاجِر (مُستَأجِرين) جَديدَة
- تَعديل أَشجار الفِئات
- تَعديل المُدُن والأَحياء
- تَعديل الخَصائِص الديناميكِيَّة (لِإعلانات فِئَة، أَو لِلبروفايل)
- تَعديل الهُويَّة البَصَريَّة (اسم/لَون/شِعار/مَدينَة/قَناة الدُخول)
- تَخصيص PWA لِكُلّ دَور (set_pwa): اسم وَأَيقونَة data: URL

قَواعِد:
1. اِستَخدِم الأَدَوات لِكُلّ كِتابَة. لا تَكتُب SQL. لا تَختَرِع APIs.
2. في كُلّ دَور assistant، نِداء أَداة واحِدَة فَقَط كَحَدّ أَقصى.
3. كُلّ نِداء أَداة يَنتَظِر مُوافَقَة المُشرِف. لا تَفتَرِض النَّجاح
   قَبل أَن تَرى tool_result.
4. عِند الغُموض اِسأَل قَبل أَن تَكتُب. خاصَّةً:
   - channel الدُخول: "phone" (هاتِف+OTP) أَو "nafath" (نَفاذ سُعودي)
     أَو "email" (بَريد إلِكترونيّ+OTP).
   - اللَون hex (مَثَلاً #1d4ed8).
   - الـ slug (حُروف صَغيرَة وأَرقام و - و _).
5. الـ scope_id في set_attributes مَحسوب لَكَ مُسبَقاً في قائِمَة المُستَأجِرين
   أَدناه (تَحت كُلّ فِئَة + sentinel البروفايل لِكُلّ مَتجَر). استَخدِمه
   مُباشَرَة بِدون سُؤال المُشرِف. الـ sentinel الثابِت لِلبروفايل دائِماً:
   00000000-0000-0000-0000-000000000F01.
6. الفِئات تُجَمَّع بِـ "kind": residential, commercial, events, vehicles,
   roommate، أَو فارِغ.
7. الأَدوار تُختار مِن كاتالوج الأَدوار — set_roles يَقبَل هذه الـ slugs
   حَصراً (مَقروءَة مِن مِلَفّات تَعريف الأَدوار، فَما يُؤَلَّف مِنها
   يَظهَر هُنا بِلا تَعديل نَصّ):
{{CatalogRoleLines}}
   لِتَطبيق نَموذَج إنجيز (راكِب⇄سائِق) اِختَر rider + driver.
8. لُغَتُكَ الافتراضيَّة العَرَبيَّة الفُصحى مَع تَشكيل خَفيف.

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
    // internal (لا private): نَفس التَّعريفات مَصدَر مُخَطَّطات
    // AgentToolValidator — مَصدَر واحِد لِلمُخَطَّط تَوليداً وفَرضاً.
    /// <summary>
    /// <para>الأَدَوات في سِياق المَنصَّة — نَفس ما كانَ حَرفاً. تُبقي
    /// <c>AgentToolValidator</c> عَلى خَريطَتِه السّاكِنَة كَما هي.</para>
    /// </summary>
    internal static List<AgentToolDef> BuildAbstractTools() =>
        BuildAbstractTools(TenantRoleSet.Platform);

    /// <summary>
    /// <para><b>الأَدَوات في سِياق لَقطَة أَدوار</b>. الفَرق الوَحيد
    /// عَن السِياق العامّ هو تَعداد <c>set_roles</c>: في سِياق مُستَأجِر
    /// يَضُمّ أَدوارَه المُؤَلَّفَة المُعتَمَدَة. بَقِيَّة الأَدَوات نَصّها
    /// ساكِن ولا يَتَغَيَّر بِالمُستَأجِر.</para>
    /// </summary>
    internal static List<AgentToolDef> BuildAbstractTools(TenantRoleSet roleSet) => new()
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
        new("set_roles",
            "إعادَة كِتابَة قائِمَة أَدوار المَتجَر بِالكامِل. اِترُكها فارِغَة "
          + "لِنَمَط user-فَرد. كُلّ دَور لَه scope_id بروفايل خاصّ (يَظهَر "
          + "في snapshot أَدناه).",
            ReferenceEquals(roleSet, TenantRoleSet.Platform)
                ? SetRolesSchema                       // النَّصّ السّاكِن كَما كان
                : SetRolesSchemaFor(roleSet)),
        new("define_role",
            "تَأليف دَور جَديد لِمَتجَر — دَور لا يُوجَد في كاتالوج المَنصَّة. "
          + "يُخزَّن التَعريف <b>مُعَلَّقاً</b> ولا يَراه أَيّ مُستَخدِم حَتَّى "
          + "يَعتَمِدَه مُشرِف المَنصَّة. الـ slug لا يُصادِم أَسماء الكاتالوج، "
          + "والصَلاحِيّات وأَنواع الحُقول ومُكَوِّنات التَركيب مِن المَعاجِم "
          + "المُغلَقَة حَصراً — الخُروج عَنها يُرجِع رَمز خَرق تُصَحِّح عَلَيه.",
            DefineRoleSchema),
        new("set_attributes",
            "إعادَة كِتابَة الخَصائِص الديناميكِيَّة لِنِطاق (scope) مَحَدَّد. "
          + "النِّطاق إمّا Guid فِئَة أَو scope_id بروفايل دَور أَو "
          + "00000000-0000-0000-0000-000000000F01 لِلبروفايل العامّ.",
            SetAttributesSchema),
        new("set_pwa",
            "تَخصيص PWA لِدَور: اسم مُخَصَّص (يَتَجاوَز التَّوليد) أَو "
          + "أَيقونَة مُخَصَّصَة (data: URL، يَتَجاوَز التَّوليد بِلَون "
          + "العَلامَة). الحَذف بِتَمرير سِلسِلَة فارِغَة.",
            SetPwaSchema)
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
        "slug":    {"type": "string", "pattern": "^[A-Za-z0-9_-]+$", "description": "[a-z0-9_-]+"},
        "name":    {"type": "string"},
        "tagline": {"type": "string"},
        "city":    {"type": "string"},
        "color":   {"type": "string", "pattern": "^#[0-9A-Fa-f]{6}$", "description": "لَون hex مَثَلاً #1d4ed8"},
        "channel": {"type": "string", "enum": ["phone", "nafath", "email"]},
        "categories": {"type": "array", "items": {{CategoryItemSchema}}}
      }
    }
    """;

    private static readonly string SetCategoriesSchema = $$"""
    {
      "type": "object",
      "required": ["slug", "categories"],
      "properties": {
        "slug": {"type": "string", "pattern": "^[A-Za-z0-9_-]+$"},
        "categories": {"type": "array", "items": {{CategoryItemSchema}}}
      }
    }
    """;

    private static readonly string SetBrandingSchema = """
    {
      "type": "object",
      "required": ["slug"],
      "properties": {
        "slug":    {"type": "string", "pattern": "^[A-Za-z0-9_-]+$"},
        "name":    {"type": "string"},
        "tagline": {"type": "string"},
        "city":    {"type": "string"},
        "color":   {"type": "string", "pattern": "^#[0-9A-Fa-f]{6}$"},
        "channel": {"type": "string", "enum": ["phone", "nafath", "email"]}
      }
    }
    """;

    private static readonly string SetPwaSchema = """
    {
      "type": "object",
      "required": ["slug", "role"],
      "properties": {
        "slug":          {"type": "string", "pattern": "^[A-Za-z0-9_-]+$", "description": "slug المَتجَر"},
        "role":          {"type": "string", "description": "slug الدَور"},
        "pwa_name":      {"type": "string", "description": "اسم التَطبيق، فارِغ = حَذف"},
        "pwa_icon_url":  {"type": "string", "maxLength": 350000, "description": "data:image/png;base64,… (حَتَّى ٢٥٦ ك.ب. — الحَدّ مُرَمَّز maxLength عَلى طول الـ data URL)، فارِغ = حَذف"}
      }
    }
    """;

    // ─── تَعداد أَدوار set_roles — مُشتَقّ لا مَكتوب ───────────────────
    /// <summary>
    /// <para>slugs الكاتالوج مَفصولَة بِفاصِلَة عَرَبيَّة — لِنُصوص
    /// الوَصف الَّتي يَقرَؤُها النَّموذَج.</para>
    /// </summary>
    private static readonly string CatalogSlugsCsv =
        string.Join("، ", ACommerce.Kit.Roles.RoleCatalog.All.Select(t => t.Slug));

    // (كانَ هُنا CatalogSlugsEnumJson — تَعداد JSON لِنَفس الـ slugs.
    //  نُقِلَ إلى SetRolesSchemaFor لِأَنّ التَعداد صارَ يُشتَقّ مِن
    //  لَقطَة أَدوار قَد تَكون مُستَأجِراً لا مِن الكاتالوج وَحدَه —
    //  والقيمَة السّاكِنَة لَم تَتَغَيَّر: لَقطَة المَنصَّة هي الكاتالوج.)

    /// <summary>سُطور «slug — تَسمِيَة: وَصف» لِقاعِدَة الأَدوار في
    /// رِسالَة النِّظام.</summary>
    private static readonly string CatalogRoleLines =
        string.Join("\n", ACommerce.Kit.Roles.RoleCatalog.All
            .Select(t => $"   - {t.Slug} ({t.Label}): {t.Description}"));

    /// <summary>
    /// <para><b>التَّعداد مُشتَقّ مِن الكاتالوج لا مَكتوب يَدَوِيّاً</b>.
    /// كانَ هُنا مَصفوفَة <c>enum</c> بِسَبعَة أَسماء مَنسوخَة، وتَحتَها
    /// وَصف عَرَبيّ يَذكُر السَبعَة مَرَّةً أُخرى، وفَوقَها تَعليق يَذكُرُها
    /// مَرَّةً ثالِثَة — <b>ونَقَصَ مِن التَّعليق <c>rider</c> أَصلاً</b>،
    /// وهو بِعَينِه ما تَفعَلُه ثَلاثَة مَصادِر لِحَقيقَة واحِدَة.</para>
    ///
    /// <para>صارَت الثَّلاثَة تُشتَقّ مِن
    /// <see cref="ACommerce.Kit.Roles.RoleCatalog.All"/>، فَدَور جَديد
    /// يُؤَلَّف مِلَفّاً يَدخُل تَعداد الوَكيل <b>ووَصفَه ورِسالَةَ
    /// نِظامِه</b> بِلا سَطر كود. و<c>AgentToolValidator</c> يَبني
    /// مُخَطَّطاتِه مِن <see cref="BuildAbstractTools"/> نَفسِها، فَالبَوّابَة
    /// تَتَّسِع مَعَه بِلا لَمس.</para>
    ///
    /// <para><b>وما لَم يَتَغَيَّر</b>: الوَكيل ما يَزال عاجِزاً عَن
    /// اختِراع دَور خارِج الكاتالوج — الحارِس هو الكاتالوج نَفسُه بَدَل
    /// نُسخَة مِنه.</para>
    /// </summary>
    private static readonly string SetRolesSchema = SetRolesSchemaFor(TenantRoleSet.Platform);

    /// <summary>
    /// <para><b>نَفس الاشتِقاق، بِمَصدَر يُمكِن أَن يَكون مُستَأجِراً</b>.
    /// كانَ التَعداد يُقرَأ مِن <c>RoleCatalog.All</c> مُباشَرَةً؛ صارَ
    /// يُقرَأ مِن لَقطَة أَدوار — و<see cref="TenantRoleSet.Platform"/>
    /// هي الكاتالوج بِعَينِه، فَالحَقل السّاكِن أَعلاه <b>نَفس النَّصّ
    /// حَرفاً</b> كَما كانَ.</para>
    ///
    /// <para>وحينَ يُبنى في سِياق مُستَأجِر يَضُمّ التَعداد أَدوارَه
    /// المُؤَلَّفَة المُعتَمَدَة — فَيَستَطيع الوَكيل أَن يُسَكِّن دَوراً
    /// أَلَّفَه هو نَفسُه في نَفس المَتجَر، ولا يَستَطيع تَسكينَه في
    /// مَتجَر آخَر.</para>
    /// </summary>
    private static string SetRolesSchemaFor(TenantRoleSet roleSet)
    {
        var csv  = string.Join("، ", roleSet.All.Select(t => t.Slug));
        var json = "[" + string.Join(", ", roleSet.All.Select(t => "\"" + t.Slug + "\"")) + "]";

        return $$"""
        {
          "type": "object",
          "required": ["slug", "roles"],
          "properties": {
            "slug": {"type": "string", "pattern": "^[A-Za-z0-9_-]+$"},
            "roles": {
              "type": "array",
              "description": "قائِمَة catalog slugs مَختارَة مِن: {{csv}}",
              "items": {
                "type": "string",
                "enum": {{json}}
              }
            },
            "default_role": {
              "type": "string",
              "description": "أَيّ دَور يُسَكَّن لِلمُستَخدِم الجَديد افتراضيّاً."
            }
          }
        }
        """;
    }

    // ─── define_role — مُخَطَّطُه شَكل RoleDefinition نَفسُه ────────────
    /// <summary>حاوِيَة نَصّ مُوَطَّن — العَرَبيَّة إلزامِيَّة كَما في
    /// <c>LocalizedText</c>، والإنجليزيَّة مَوضِع مَحجوز لا يَقرَؤُه
    /// شَيء بَعد.</summary>
    private const string LocalizedTextSchema = """
    {
      "type": "object",
      "required": ["ar"],
      "additionalProperties": false,
      "properties": {
        "ar": {"type": "string", "description": "العَرَبيَّة بِتَشكيل خَفيف — إلزامِيَّة"},
        "en": {"type": ["string", "null"], "description": "مَحجوزَة — اُترُكها null"}
      }
    }
    """;

    /// <summary>مُفرَدات المَعاجِم المُغلَقَة كَنُصوص وَصف — تُشتَقّ مِن
    /// نَفس القَوائِم الَّتي يَفرِضُها <c>RoleDefinitionValidator</c>،
    /// فَلا تَنحَرِف عَنها.</summary>
    private static readonly string PermissionVocabCsv =
        string.Join("، ", ACommerce.Kit.Roles.PermissionCatalog.All);

    private static readonly string FieldTypeVocabCsv =
        string.Join("، ", ACommerce.Kit.Roles.RoleFieldTypes.All);

    private static readonly string ComponentVocabCsv =
        string.Join("، ", ACommerce.Kit.Roles.RoleComponents.All);

    private static readonly string AffinityVocabCsv =
        string.Join("، ", ACommerce.Kit.Roles.RoleDealPatternAffinity.All);

    /// <summary>
    /// <para><b>شَكل <c>RoleDefinition</c> حَرفِيّاً</b> — نَفس المَفاتيح
    /// بِنَفس أَسمائِها camelCase، و<c>additionalProperties: false</c>
    /// لِيُطابِق <c>UnmappedMemberHandling.Disallow</c> في
    /// <c>ParseDefinition</c>: ما يَرفُضُه المُخَطَّط يَرفُضُه القارِئ،
    /// فَلا يَمُرّ مِفتاح مَجهول مِن أَحَدِهِما ويَسقُط في الآخَر.</para>
    ///
    /// <para><b>ومُفترَق مُعلَن: المَعاجِم المُغلَقَة وَصفٌ هُنا وفَرضٌ
    /// في المُصادِق</b>. كانَ يُمكِن تَرميزُها <c>enum</c> في المُخَطَّط
    /// (كَما في <c>set_roles</c>)، والاختِيار وَقَعَ عَلى الوَصف لِسَبَبَين:
    /// أَنّ <c>RoleDefinitionValidator</c> هو المَقعَد المَوضوع لِهذا
    /// بِالضَبط ويُعطي <b>رَمز خَرق ثابِتاً</b> يَقرَؤُه الوَكيل ويُصَحِّح
    /// عَلَيه (<c>permission_out_of_vocabulary</c> أَوضَح مِن «لا يُطابِق
    /// enum»)، وأَنّ مَصدَرَين لِلمَعجَم يَنحَرِفان بِصَمت. المُخَطَّط
    /// يَحرُس <b>الشَكل</b>، والمُصادِق يَحرُس <b>المُفرَدات</b>.</para>
    /// </summary>
    private static readonly string RoleDefinitionSchema = $$"""
    {
      "type": "object",
      "required": ["slug", "icon", "label", "description", "permissions"],
      "additionalProperties": false,
      "properties": {
        "slug": {
          "type": "string",
          "pattern": "^[a-z][a-z0-9_]*$",
          "description": "مُعَرِّف ASCII فَريد. لا يُصادِم أَسماء الكاتالوج ({{CatalogSlugsCsv}}) — التَصادُم يُرفَض بِرَمز slug_shadows_platform_catalog."
        },
        "icon": {"type": "string", "description": "إيموجي واحِد"},
        "homeRoute": {"type": "string", "description": "المَسار بَعد الدُخول، يَبدَأ بِـ / — أَو فارِغ لِلصَفحَة الافتِراضِيَّة"},
        "label":       {{LocalizedTextSchema}},
        "description": {{LocalizedTextSchema}},
        "permissions": {
          "type": "array",
          "items": {"type": "string"},
          "description": "مِن هذا المَعجَم حَصراً: {{PermissionVocabCsv}}"
        },
        "fields": {
          "type": "array",
          "description": "حُقول تُجمَع في الـ onboarding",
          "items": {
            "type": "object",
            "required": ["code", "label", "type"],
            "additionalProperties": false,
            "properties": {
              "code":  {"type": "string"},
              "label": {{LocalizedTextSchema}},
              "type": {
                "type": "string",
                "description": "مِن: {{FieldTypeVocabCsv}}"
              },
              "isRequired": {"type": "boolean"},
              "options": {
                "type": "array",
                "description": "إلزامِيَّة لِـ SingleSelect و MultiSelect",
                "items": {
                  "type": "object",
                  "required": ["value", "label"],
                  "additionalProperties": false,
                  "properties": {
                    "value": {"type": "string"},
                    "label": {{LocalizedTextSchema}}
                  }
                }
              }
            }
          }
        },
        "composition": {
          "type": "object",
          "additionalProperties": false,
          "description": "تَركيب الواجِهَة — كُلّ قيمَة مِن مَعجَم المُكَوِّنات القائِم حَصراً، ولا مُكَوِّن جَديد: {{ComponentVocabCsv}}",
          "properties": {
            "home":          {"type": "string"},
            "createListing": {"type": "string"},
            "nav":           {"type": "string"},
            "explore":       {"type": "string"},
            "publicProfile": {"type": ["string", "null"]},
            "extras":        {"type": "array", "items": {"type": "string"} }
          }
        },
        "dealPatternAffinity": {
          "type": ["string", "null"],
          "description": "نَمَط الصَفقَة الَّذي يَجُرّ إلَيه — مِن: {{AffinityVocabCsv}} — أَو null"
        }
      }
    }
    """;

    private static readonly string DefineRoleSchema = $$"""
    {
      "type": "object",
      "required": ["slug", "definition"],
      "properties": {
        "slug": {"type": "string", "pattern": "^[A-Za-z0-9_-]+$", "description": "slug المَتجَر"},
        "definition": {{RoleDefinitionSchema}}
      }
    }
    """;

    private static readonly string SetRegionsSchema = """
    {
      "type": "object",
      "required": ["slug", "cities"],
      "properties": {
        "slug": {"type": "string", "pattern": "^[A-Za-z0-9_-]+$"},
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
        "slug":     {"type": "string", "pattern": "^[A-Za-z0-9_-]+$"},
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

    /// <summary>لَقطَة أَدوار المُستَأجِر — يَقرَؤُها المُنَفِّذ مَرَّتَين:
    /// لِبِناء مُخَطَّط <c>set_roles</c> في سِياقِه، ولِيَجِد
    /// <c>set_roles</c> دَوراً أَلَّفَه المُستَأجِر لا يَعرِفُه
    /// الكاتالوج. وهي أَيضاً مَن يَكتُب تَعريف <c>define_role</c>.</summary>
    private readonly TenantRoleService _roles;

    public AgentToolExecutor(IDocumentStore store, TenantRoleService roles)
    { _store = store; _roles = roles; }

    public Task<(bool Ok, string Message)> ExecuteAsync(
        string toolName, string inputJson, CancellationToken ct = default)
        => ExecuteAsync(toolName, inputJson, ownerUserId: null, ct);

    /// <summary>تَنفيذ أَداة مَع فَرض ملكيَّة المُستَخدِم. عِندَ
    /// <paramref name="ownerUserId"/> = null: سُلوك ادمن المَنصَّة (بِلا قُيود).
    /// عِندَ تَمرير قِيمَة: كُلّ أَداة تُعَدِّل tenant تَتَطَلَّب أَن يَكون
    /// <c>tenant.OwnerUserId == ownerUserId</c>؛ وَ <c>create_tenant</c>
    /// يَكتُب OwnerUserId مُباشَرَةً عَلى الـ tenant الجَديد. هذا يَمنَع
    /// رائِد أَعمال مِن تَعديل تَطبيق رائِد أَعمال آخَر عَبر الوَكيل.</summary>
    public async Task<(bool Ok, string Message)> ExecuteAsync(
        string toolName, string inputJson, Guid? ownerUserId, CancellationToken ct = default)
    {
        // لَقطَة أَدوار المَتجَر المَقصود قَبل المُصادَقَة — لِأَنّ
        // مُخَطَّط set_roles نَفسُه يَتَّسِع بِأَدوار المُستَأجِر
        // المُعتَمَدَة. قِراءَة السلاج هُنا مُتَسامِحَة عَمداً: حُمولَة
        // تالِفَة أَو بِلا slug تَسقُط إلى لَقطَة المَنصَّة، ثُمَّ
        // يَرفُضُها المُصادِق بِرِسالَتِه المُعتادَة.
        var tenantSlugForTools = TryReadTenantSlug(inputJson);
        var roleSet = await _roles.ForAsync(tenantSlugForTools, ct);

        // بَوّابَة إلزاميَّة أُولى: مُصادَقَة الحُمولَة ضِدّ المُخَطَّط
        // المُعلَن قَبل فَحص الملكيَّة وقَبل أَيّ تَنفيذ (TESTING-PROTOCOL
        // §T3). الفُحوص اليَدَويَّة أَدناه تَبقى — دِفاع مُتَعَدِّد الطَّبَقات.
        var validation = AgentToolValidator.Validate(toolName, inputJson, roleSet);
        if (!validation.IsValid)
            return (false, $"رُفِضَت الحُمولَة — مُخالَفَة مُخَطَّط أَداة «{toolName}»: "
                         + string.Join(" | ", validation.Errors));

        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            var root = doc.RootElement;

            // فَحص الملكيَّة لِلأَدَوات الَّتي تُعَدِّل tenant قائم.
            if (ownerUserId is Guid owner && toolName != "create_tenant")
            {
                var slug = Str(root, "slug").ToLowerInvariant();
                if (string.IsNullOrEmpty(slug))
                    return (false, "slug مَطلوب لِأَداة تَعديل المَتجَر.");
                await using var qs = _store.QuerySession();
                var existing = await qs.LoadAsync<Tenant>(slug, ct);
                if (existing is null)
                    return (false, $"المَتجَر «{slug}» غَير مَوجود.");
                if (existing.OwnerUserId != owner)
                    return (false, $"غَير مَسموح: لا تَملِك المَتجَر «{slug}». يُمكِن لِكُلّ رائِد أَعمال تَعديل تَطبيقاته فَقَط.");
            }

            return toolName switch
            {
                "create_tenant"  => await CreateTenantAsync(root, ownerUserId, ct),
                "set_categories" => await SetCategoriesAsync(root, ct),
                "set_branding"   => await SetBrandingAsync(root, ct),
                "set_regions"    => await SetRegionsAsync(root, ct),
                "set_roles"      => await SetRolesAsync(root, roleSet, ct),
                "define_role"    => await DefineRoleAsync(root, ct),
                "set_attributes" => await SetAttributesAsync(root, ct),
                "set_pwa"        => await SetPwaAsync(root, ct),
                _ => (false, $"أَداة غَير مَعروفَة: {toolName}")
            };
        }
        catch (Exception ex)
        {
            return (false, "خَطَأ في تَنفيذ الأَداة: " + ex.Message);
        }
    }

    private async Task<(bool, string)> CreateTenantAsync(JsonElement root, Guid? ownerUserId, CancellationToken ct)
    {
        var slug    = Str(root, "slug").ToLowerInvariant();
        var name    = Str(root, "name");
        var color   = Str(root, "color");
        var tagline = Str(root, "tagline");
        var city    = Str(root, "city");
        var channel = ACommerce.Kit.Auth.AuthChannels.NormalizeOrDefault(Str(root, "channel"));
        if (!System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9_-]+$"))
            return (false, "slug غَير صالِح.");
        if (string.IsNullOrEmpty(name)) return (false, "الاسم مَطلوب.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$"))
            return (false, "لَون غَير صالِح.");

        var cats = ParseCategories(root);
        if (cats.Count == 0) return (false, "يَجِب فِئَة واحِدَة عَلى الأَقَلّ.");

        // ─── بَوّابَةُ حِصَّةِ المَتاجِر — والتَخويلُ يَسبِقُ الكِتابَة ──
        //
        // **العِلَّةُ المَقيسَة (‏2026-08-30)**: هذا المَسارُ كانَ يُنشِئُ
        // مُستَأجِراً **بِلا `CheckBuildAsync` ولا `RecordStoreBuiltAsync`**،
        // بَينَما نَظيرُه `POST /studio/s/{id}/build` يَحرُسُ ويَعُدّ.
        // فَحَصُ المِلكِيَّةِ كانَ قائِماً وفَحصُ الحِصَّةِ مَعدوماً —
        // أَي أَنّ `StoresMax` (‏40 بَعدَ الرَفع) **لَم يَكُن سَقفاً**:
        // يُخترَقُ كُلِّيّاً بِنَقرَةٍ في `/studio/agent`. وهذا يَنقُضُ
        // حُجَّةَ الرَفعِ نَفسَها — «فَيَبقى السَقفُ مُنتَهِياً فَتُغلَقُ
        // البَوّابَةُ في وَجهِ حَلقَةٍ آلِيَّة».
        //
        // **ولا رَقمَ يُخترَعُ هُنا** (القاعِدَة ١٦): نَفسُ الحَدِّ ونَفسُ
        // العَدّادِ ونَفسُ رَمزِ الخَرقِ الَّذي تَقرَؤُهُ الشاشَة — ولا
        // أُنبوبَ رابِع (القاعِدَة ٨).
        //
        // **و`ownerUserId == null` سُلطَةُ مُشرِفِ المَنصَّة** لا ثَغرَة:
        // هُوَ نَفسُ الاستِثناءِ الَّذي يَقرَؤُهُ
        // `LanguageModelQuotaGateTests.HasOwnerAuthority`.
        var tierService = new Services.Incubator.StudioTierService(_store);
        if (ownerUserId is Guid quotaOwner)
        {
            var gate = await tierService.CheckBuildAsync(quotaOwner, ct);
            if (!gate.Allowed)
                return (false,
                    $"بَلَغتَ حَدَّ التَطبيقاتِ في باقَتِك ({gate.Used} مِن {gate.Limit}). "
                  + "اِحذِف تَطبيقاً أَو اِرفَع باقَتَك مِن /studio/billing.");
        }

        await using var s = _store.LightweightSession();
        var existing = await s.LoadAsync<Tenant>(slug, ct);
        if (existing is not null) return (false, $"الـ slug «{slug}» مَوجود بِالفِعل.");

        s.Store(new Tenant
        {
            Id = slug, Name = name, BrandColor = color, TagLine = tagline,
            City = city, AuthChannel = channel, Categories = cats,
            // عِندَ إنشاء عَبر وَكيل ستوديو، اِكتُب المالِك مُباشَرَةً —
            // وإلّا أَيّ رائِد أَعمال يَستَطيع لاحِقاً «تَبَنّي» مَتجَر بِلا مالِك.
            // Guid.Empty = بِلا مالِك (سُلوك ادمن المَنصَّة الافتراضيّ).
            OwnerUserId = ownerUserId ?? Guid.Empty,
            CreatedAt = DateTime.UtcNow
        });
        await s.SaveChangesAsync(ct);

        // والعَدُّ **بَعدَ** الكِتابَةِ الناجِحَة — نَفسُ تَرتيبِ
        // `POST /studio/s/{id}/build` حَرفاً: لا يُحاسَبُ على مَتجَرٍ
        // لَم يوجَد.
        if (ownerUserId is Guid counted)
            await tierService.RecordStoreBuiltAsync(counted, ct);

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
            if (!ACommerce.Kit.Auth.AuthChannels.IsSupported(channel))
                return (false, "channel غَير صالِح.");
            t.AuthChannel = channel;
        }
        s.Store(t);
        await s.SaveChangesAsync(ct);
        return (true, $"تَمّ تَحديث هُويَّة «{slug}».");
    }

    /// <summary>
    /// <para><b>مَوضِع التِقاط</b>: البَحث صارَ في لَقطَة أَدوار
    /// المُستَأجِر لا في الكاتالوج السّاكِن — فَدَور أَلَّفَه هذا المَتجَر
    /// واعتُمِدَ يُمكِن تَسكينُه بِـ <c>set_roles</c>. وبِلا وَثيقَة
    /// واحِدَة اللَقطَة <b>هي</b> الكاتالوج، فَالسُلوك لَم يَتَغَيَّر
    /// بِحَرف.</para>
    /// </summary>
    private async Task<(bool, string)> SetRolesAsync(
        JsonElement root, TenantRoleSet roleSet, CancellationToken ct)
    {
        var slug = Str(root, "slug").ToLowerInvariant();
        if (!root.TryGetProperty("roles", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return (false, "roles مَطلوب (قائِمَة catalog slugs).");

        var defaultRole = Str(root, "default_role").ToLowerInvariant();
        var roles = new List<ACommerce.Kit.Roles.Role>();
        var i = 0;
        foreach (var r in arr.EnumerateArray())
        {
            var rslug = r.ValueKind == JsonValueKind.String
                ? r.GetString()?.Trim().ToLowerInvariant() ?? ""
                : "";
            if (string.IsNullOrEmpty(rslug)) continue;
            var tmpl = roleSet.Find(rslug);
            if (tmpl is null) continue;  // تَجاهُل slugs خارِج اللَقطَة
            var role = ACommerce.Kit.Roles.RoleCatalog.InstantiateRole(tmpl, i++);
            role.IsDefault = defaultRole == rslug;
            roles.Add(role);
        }
        if (roles.Count == 0)
            return (false, "أَيّ مِن الـ slugs لَم تُطابِق الكاتالوج.");

        await using var s = _store.LightweightSession();
        var t = await s.LoadAsync<Tenant>(slug, ct);
        if (t is null) return (false, "المَتجَر غَير مَوجود.");
        t.Roles = roles;
        s.Store(t);
        await s.SaveChangesAsync(ct);
        return (true, $"تَمّ تَحديث «{slug}» إلى {roles.Count} أَدوار مِن الكاتالوج: "
                    + string.Join(", ", roles.Select(r => r.Slug)));
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

    private async Task<(bool, string)> SetPwaAsync(JsonElement root, CancellationToken ct)
    {
        var slug = Str(root, "slug").ToLowerInvariant();
        var roleSlug = Str(root, "role").ToLowerInvariant();
        if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(roleSlug))
            return (false, "slug وَ role مَطلوبان.");

        await using var s = _store.LightweightSession();
        var t = await s.LoadAsync<Tenant>(slug, ct);
        if (t is null) return (false, "المَتجَر غَير مَوجود.");
        var role = t.Roles.FirstOrDefault(r => r.Slug == roleSlug);
        if (role is null) return (false, $"الدَور «{roleSlug}» غَير مَوجود.");

        var changed = new List<string>();
        if (TryStr(root, "pwa_name", out var pwaName))
        {
            role.PwaName = string.IsNullOrEmpty(pwaName) ? null : pwaName;
            changed.Add("الاسم");
        }
        if (TryStr(root, "pwa_icon_url", out var iconUrl))
        {
            if (string.IsNullOrEmpty(iconUrl))
            {
                role.PwaIconDataUrl = null;
                changed.Add("الأَيقونَة (حُذِفَت)");
            }
            else
            {
                if (!iconUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                    return (false, "pwa_icon_url يَجِب أَن يَكون data:image/…");
                // سَقف ٢٥٦ كيلوبايت بَعد فَكّ base64 (نَحسِبُه تَقريباً بِـ length * 3/4).
                if (iconUrl.Length > 350_000)
                    return (false, "الأَيقونَة أَكبَر مِن ٢٥٦ كيلوبايت.");
                role.PwaIconDataUrl = iconUrl;
                changed.Add("الأَيقونَة");
            }
        }
        if (changed.Count == 0) return (true, "لا تَغيير.");

        s.Store(t);
        await s.SaveChangesAsync(ct);
        return (true, $"تَمّ تَحديث PWA «{roleSlug}»: {string.Join("، ", changed)}.");
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

    /// <summary>
    /// <para><b>تَأليف دَور — المَسار الكامِل في ثَلاث خُطوات</b>:
    /// <c>ParseDefinition</c> (نَفس القارِئ الَّذي يَقرَأ مِلَفّات
    /// الكاتالوج المَضمونَة، بِنَفس خِياراتِه) ثُمَّ
    /// <c>ValidateTenantDefinition</c> (نَفس المُصادِق، زائِدَ قاعِدَة
    /// عَدَم الظِلّ) ثُمَّ <b>تَخزين مُعَلَّق</b>.</para>
    ///
    /// <para><b>ولا يَصير حَيّاً هُنا بِحال</b>: الوَثيقَة تُكتَب
    /// <c>pending</c>، ولا سَطح لاعِب يَقرَأ إلّا <c>approved</c>. القَرار
    /// البَشَريّ في <c>/admin/tenants/{slug}/roles</c> هو ما يُحييها،
    /// وهو يُعيد المُصادَقَة عَلى النَّصّ المُخَزَّن قَبل أَن يَفعَل.</para>
    ///
    /// <para><b>والرَّفض يَعود بِرَمزِه</b> لا بِرِسالَة حُرَّة — لِأَنّ
    /// الوَكيل يُصَحِّح عَلى الرَّمز: <c>permission_out_of_vocabulary</c>
    /// يَقول «بَدِّل الصَلاحِيَّة»، و<c>slug_shadows_platform_catalog</c>
    /// يَقول «بَدِّل الاسم». هذا هو نَفس عَقد بَقِيَّة الأَدَوات.</para>
    /// </summary>
    private async Task<(bool, string)> DefineRoleAsync(JsonElement root, CancellationToken ct)
    {
        var slug = Str(root, "slug").ToLowerInvariant();
        if (!root.TryGetProperty("definition", out var defEl) ||
            defEl.ValueKind != JsonValueKind.Object)
            return (false, "definition مَطلوب (كائِن تَعريف دَور).");

        // النَّصّ كَما كَتَبَه الوَكيل حَرفِيّاً — هو ما يُخزَّن، فَلا
        // شَكل ثانٍ لِلتَعريف يَنحَرِف عَن الأَوَّل.
        var definitionJson = defEl.GetRawText();

        ACommerce.Kit.Roles.RoleDefinition parsed;
        try
        {
            parsed = ACommerce.Kit.Roles.RoleDefinitionLoader.ParseDefinition(definitionJson);
        }
        catch (Exception ex)
        {
            return (false, "تَعَذَّرَت قِراءَة التَعريف: " + ex.Message);
        }

        var violations = ACommerce.Kit.Roles.RoleDefinitionValidator
            .ValidateTenantDefinition(parsed);
        if (violations.Count > 0)
            return (false, $"رُفِضَ تَعريف الدَور «{parsed.Slug}»: " +
                           string.Join(" | ", violations.Select(v => $"{v.Code}: {v.MessageAr}")));

        await using (var qs = _store.QuerySession())
        {
            var t = await qs.LoadAsync<Tenant>(slug, ct);
            if (t is null) return (false, $"المَتجَر «{slug}» غَير مَوجود.");
        }

        var (ok, msg) = await _roles.ProposeAsync(
            slug, parsed.Slug, definitionJson, by: "agent:define_role", ct);
        if (!ok) return (false, msg);

        return (true,
            $"سُجِّلَ تَعريف الدَور «{parsed.Slug}» ({parsed.Label.Current}) لِلمَتجَر «{slug}» " +
            "مُعَلَّقاً. لا يَظهَر لِأَيّ مُستَخدِم حَتَّى يَعتَمِدَه مُشرِف المَنصَّة " +
            $"مِن /admin/tenants/{slug}/roles.");
    }

    /// <summary>سلاج المَتجَر مِن حُمولَة أَداة، أَو <c>null</c> إن
    /// تَعَذَّرَ. <b>مُتَسامِحَة عَمداً</b>: مُهِمَّتُها اختِيار لَقطَة
    /// الأَدوار الَّتي يُصادَق بِها، لا الحُكم عَلى الحُمولَة — الحُكم
    /// لِلمُصادِق بَعدَها.</summary>
    private static string? TryReadTenantSlug(string inputJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var s = Str(doc.RootElement, "slug").Trim().ToLowerInvariant();
            return string.IsNullOrEmpty(s) ? null : s;
        }
        catch { return null; }
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
