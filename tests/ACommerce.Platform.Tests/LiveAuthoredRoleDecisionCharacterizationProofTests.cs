using System.Reflection;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Gates;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ قَرارُ حارِسَيِ الصَلاحِيَّةِ في مَتجَرٍ أَدوارُه مُؤَلَّفَةٌ وَحدَها — يُثَبَّتُ قَبلَ أَن يَحِلَّ دَورُهُما المَقبولُ مَحَلَّ `_none` الوَسيط
//     (‏2026-11-12، الموجَة ١٣ W13-6·B-R1·1 — مُراجَعَةُ المَرحَلَةِ ب · M6B-CNT-1؛ ADR-051 §١٧) ═══
//
// **لِماذا الآن** (القاعِدَتانِ ٣ و١٣): الوَسيطُ يَضَعُ `_none` في حامِلِ العَدِّ مِن `Tenant.Roles` المُخَزَّنَةِ المُخَبَّأَة، والحارِسانِ (`PermissionFilter`
// و`HasPermissionAsync`) يُقَرِّرانِ عَلى المَجموعَةِ المُجَسَّدَة (`LoadTenantWithRolesAsync`: المُخَزَّنَةُ وفَوقَها المُؤَلَّفَةُ المُعتَمَدَة). فَمَتجَرٌ بِلا أَدوارٍ
// مُخَزَّنَةٍ وبِدَورٍ مُؤَلَّفٍ مُعتَمَدٍ يَقبَلُ الحارِسُ فيه النَشرَ بِذلكَ الدَور، و«الكاتِبُ الأَوَّلُ يَفوز» يُبقي `_none` في السَطر. والكوميتُ التالي يَجعَلُ
// دَورَ الحارِسِ المَقبولَ يَحِلُّ مَحَلَّ `_none` الوَسيط، ويَجعَلُ فَرعَ «بِلا أَدوار» عِندَهُما يَكتُبُ `_none` الَّذي قَرَّرَه — فَيَمَسُّ الفَرعَينِ كِلَيهِما.
// وتَوصيفُ القَرارِ القائِمُ (`LiveRoleDecisionCharacterizationProofTests`، c834264) مَتاجِرُه مُخَزَّنَةُ الأَدوارِ أَو بِلا وَثيقَةِ دَورٍ أَصلاً — لا مَتجَرَ
// أَدوارُه مُؤَلَّفَةٌ وَحدَها، ولا مَتجَرَ دَورُه المُؤَلَّفُ مُقتَرَحٌ لَم يُعتَمَد. فَهذا المِلَفُّ يُثَبِّتُ جَوابَ الحارِسَينِ فيهِما لِكُلِّ دَورٍ وصَلاحِيَّة، ويُودَعُ
// وَحدَه، ثُمَّ يَبقى أَخضَرَ بَعدَ التَبديلِ **بِلا مَسِّ حَرف** (بَصمَتُه في رِسالَةِ كوميتِ التَبديل). ولا يُقرَأُ `meter.*` هُنا أَصلاً: ما يَكتُبُه الحارِسانِ في
// الحامِلِ يُثبِتُه صَفُّه في `LiveMeterPersistenceProofTests`، وهذا يُثبِتُ أَنّ القَرارَ لَم يَتَغَيَّر.
//
// **والحارِسانِ بِأَعيانِهِما**: المُرَشِّحُ يُنادى كَما يُناديه أُنبوبُ النُقطَة، و`HasPermissionAsync` — خاصَّةٌ ساكِنَة — بِالانعِكاسِ بِتَوقيعِها (غِيابُها يُحمِرُّ).
// والدَورُ المُؤَلَّفُ «خَيّاط» (`TenantRoleDefinitionToolTests.KhayyatJson`: ‏listing.create وlisting.edit وlisting.delete وchat.respond — لا offer.submit
// ولا tenant.manage) وَثيقَةُ `TenantRoleDefinition` بِإيجارٍ مُقتَرِنٍ كَما يَكتُبُها الوَكيل.

[Collection(LiveProofCollection.Name)]
public class LiveAuthoredRoleDecisionCharacterizationProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private const string Authored = "khayyat";

    /// <summary>حارِسُ العَرضِ بِعَينِه — خاصٌّ ساكِن؛ غِيابُه أَو تَغَيُّرُ تَوقيعِه يُحمِرُّ الصَفّ (أَداةٌ لا تَجِدُه عَمياء).</summary>
    private static Task<bool> HasPermissionAsync(HttpContext http, string slug, Guid userId, string permission, IDocumentStore store)
    {
        var method = typeof(MarketplaceTemplateExtensions).GetMethod("HasPermissionAsync", BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(HttpContext), typeof(string), typeof(Guid), typeof(string), typeof(IDocumentStore)]);
        Assert.True(method is not null, "HasPermissionAsync بِتَوقيعِها غائِبَة — الأَداةُ عَمياء.");
        return (Task<bool>)method!.Invoke(null, [http, slug, userId, permission, store])!;
    }

    /// <summary>صَفٌّ: المَتجَرُ (<c>authored</c> أَدوارُه المُؤَلَّفَةُ المُعتَمَدَةُ وَحدَها · <c>proposed</c> دَورُه المُؤَلَّفُ مُقتَرَحٌ لَم يُعتَمَد، ولا مُخَزَّنَ فيهِما)
    /// والمُستَخدِم (مِفتاحُه أَدناه، أَو <c>—</c> بِلا هُوِيَّة) والصَلاحِيَّة، و<c>as</c> في الاستِمارَةِ وهَل يَحمِلُ كوكيَّ دَورِه، ودَورُ الرابِط.</summary>
    private sealed record Case(string Store, string User, string Permission, string? As = null, bool AsCookie = false, string? UrlRole = null)
    {
        public override string ToString()
            => $"{Store} · {User} · {Permission} · as={As ?? "—"}{(AsCookie ? "+كوكي" : "")} · رابِط={UrlRole ?? "—"}";
    }

    /// <summary><b>المُثَبَّت</b> — جَوابُ المُرَشِّح (<c>next</c> = مَرَّ إلى النُقطَة، أَو رَمزُ رَدِّه) وجَوابُ <c>HasPermissionAsync</c> لِكُلِّ صَفّ، مَقيسَينِ
    /// عِندَ <c>c11e7b0</c> عَلى Postgres 16 مَحَلِّيّ. وصَفٌّ بِلا هُوِيَّةٍ لا يَبلُغُ <c>HasPermissionAsync</c> فَجَوابُه <c>—</c>.</summary>
    private static readonly (Case Case, string Filter, string Has)[] Pinned =
    [
        (new("authored", "khayyat", "listing.create"), "next", "True"),
        (new("authored", "khayyat", "offer.submit"), "403", "False"),
        (new("authored", "khayyat", "tenant.manage"), "403", "False"),
        (new("authored", "khayyat", "chat.respond"), "next", "True"),
        (new("authored", "none", "listing.create"), "403", "False"),
        (new("authored", "none", "offer.submit"), "403", "False"),
        (new("authored", "ghost", "listing.create"), "403", "False"),
        (new("authored", "vendor", "listing.create"), "403", "False"),
        (new("authored", "absent", "listing.create"), "403", "False"),
        (new("authored", "—", "listing.create"), "401", "—"),
        (new("authored", "none", "listing.create", As: Authored, AsCookie: true), "next", "True"),
        (new("authored", "none", "listing.create", As: Authored), "403", "False"),
        (new("authored", "none", "listing.create", UrlRole: Authored), "next", "True"),
        (new("authored", "khayyat", "offer.submit", UrlRole: "customer"), "403", "False"),
        (new("proposed", "khayyat", "listing.create"), "next", "True"),
        (new("proposed", "khayyat", "offer.submit"), "next", "True"),
        (new("proposed", "khayyat", "tenant.manage"), "403", "False"),
        (new("proposed", "absent", "listing.create"), "next", "True"),
        (new("proposed", "—", "listing.create"), "401", "—"),
    ];

    private sealed record World(LiveTemplateDoorHost Host, string Authored, string Proposed, IReadOnlyDictionary<string, User> Users)
    {
        public string Slug(string store) => store == "proposed" ? Proposed : Authored;
    }

    /// <summary>المَتجَران — كِلاهُما بِلا <c>Tenant.Roles</c> مُخَزَّنَة — وتَعريفُ «خَيّاط» في كُلٍّ: مُعتَمَدٌ في الأَوَّل، ومُقتَرَحٌ في الثاني؛ وأَعضاءٌ بِدَورِ «خَيّاط»،
    /// وبِلا دَور، وبِدَورٍ لا وُجودَ لَه، وبِدَورِ كَتالوجٍ لَيسَ في المَتجَر؛ و<c>absent</c> مُعَرِّفٌ لا وَثيقَةَ لَه.</summary>
    private static async Task<World> BuildAsync(LiveTemplateDoorHost host)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (authored, proposed) = ($"w13ar-authored-{tag}", $"w13ar-proposed-{tag}");
        await using (var s = host.Store.LightweightSession())
        {
            s.Insert(new Tenant { Id = authored, Name = "مَتجَرٌ أَدوارُه مُؤَلَّفَة", OwnerUserId = Guid.NewGuid() });
            s.Insert(new Tenant { Id = proposed, Name = "مَتجَرٌ دَورُه مُقتَرَح", OwnerUserId = Guid.NewGuid() });
            await s.SaveChangesAsync();
        }
        static User Member(string? role) => new() { Id = Guid.NewGuid(), Phone = "+9665" + Random.Shared.Next(10_000_000, 99_999_999), FullName = "عُضوُ الدَورِ المُؤَلَّف",
                                                   ActiveRole = role ?? "", Role = role ?? "user" };
        var users = new Dictionary<string, User>(StringComparer.Ordinal)
        {
            ["khayyat"] = Member(Authored), ["none"] = Member(null), ["ghost"] = Member("ghost"), ["vendor"] = Member("vendor"),
        };
        foreach (var (slug, status) in new[] { (authored, TenantRoleStatuses.Approved), (proposed, TenantRoleStatuses.Pending) })
        {
            await using var s = host.Store.LightweightSession(slug);
            s.Store(new TenantRoleDefinition
            {
                Id = Authored, Slug = Authored, DefinitionJson = TenantRoleDefinitionToolTests.KhayyatJson, Status = status,
                CreatedBy = "live-proof", CreatedAt = DateTime.UtcNow,
                DecidedBy = status == TenantRoleStatuses.Approved ? "live-proof" : null,
                DecidedAt = status == TenantRoleStatuses.Approved ? DateTime.UtcNow : null,
            });
            foreach (var u in users.Values) s.Store(new User { Id = u.Id, Phone = u.Phone, FullName = u.FullName, ActiveRole = u.ActiveRole, Role = u.Role });
            await s.SaveChangesAsync();
        }
        users["absent"] = new User { Id = Guid.NewGuid() };
        return new World(host, authored, proposed, users);
    }

    /// <summary>طَلَبُ الكِتابَةِ كَما يَراه الحارِسان: المَسار (ودَورُ الرابِطِ فيه)، والاستِمارَةُ بِـ<c>as</c>، وكوكيُّ جَلسَةِ ذلكَ الدَورِ لِلمُستَخدِمِ نَفسِه إن طُلِب،
    /// والمَتجَرُ والهُوِيَّةُ في <c>Items</c> كَما يَضَعُهُما <c>AuthFilter</c>.</summary>
    private static DefaultHttpContext Request(World w, Case c)
    {
        var slug = w.Slug(c.Store);
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(w.Host.Store).BuildServiceProvider() };
        http.Request.Method = "POST";
        http.Request.Scheme = "http";
        http.Request.Host = new HostString("localhost");
        http.Request.Path = c.UrlRole is null ? $"/{slug}/listings/create" : $"/{slug}/r/{c.UrlRole}/listings/create";
        http.Request.ContentType = "application/x-www-form-urlencoded";
        http.Request.Form = new FormCollection(c.As is null
            ? new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>()
            : new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["as"] = c.As });
        http.Items[GateKeys.SlugItem] = slug;
        if (c.User != "—")
        {
            var user = w.Users[c.User];
            http.Items[GateKeys.UserId] = user.Id;
            if (c.AsCookie)
                http.Request.Headers.Cookie = $"{AuthSession.CookieNameOn(http.Request, slug, c.As)}={AuthHandlers.MakeToken(user.Id, slug, user.SessionVersion)}";
        }
        return http;
    }

    private static async Task<(string Filter, string Has)> DecideAsync(World w, Case c)
    {
        var passed = false;
        var result = await new PermissionFilter(c.Permission).InvokeAsync(new DefaultEndpointFilterInvocationContext(Request(w, c)),
            _ => { passed = true; return ValueTask.FromResult<object?>(Results.Ok()); });
        var filter = passed ? "next" : (result as IStatusCodeHttpResult)?.StatusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?";
        var has = c.User == "—" ? "—"
            : (await HasPermissionAsync(Request(w, c), w.Slug(c.Store), w.Users[c.User].Id, c.Permission, w.Host.Store)).ToString();
        return (filter, has);
    }

    private static async Task CleanAsync(World w)
    {
        await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
        await conn.OpenAsync();
        foreach (var sql in new[]
                 {
                     "delete from platform.mt_doc_user where tenant_id = any(@t)",
                     "delete from platform.mt_doc_tenantroledefinition where tenant_id = any(@t)",
                     "delete from platform.mt_doc_tenant where id = any(@t)",
                 })
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("t", new[] { w.Authored, w.Proposed });
            try { await cmd.ExecuteNonQueryAsync(); } catch (PostgresException e) when (e.SqlState == "42P01") { }
        }
    }

    /// <summary><b>المَصفوفَة</b>: كُلُّ صَفٍّ يُشَغَّلُ عَلى الحارِسَينِ مَعاً، ويُطبَعُ بِجَوابِهِما، ويُقارَنُ بِالمُثَبَّت — صَفٌّ واحِدٌ مُختَلِفٌ يَحمَرّ، وصِفرُ صَفٍّ أَداةٌ عَمياء.
    /// ويُطبَعُ مَعَه ما يَراه الحارِسانِ في كُلِّ مَتجَر: صِفرُ دَورٍ مُخَزَّنٍ، والمَجموعَةُ المُجَسَّدَة.</summary>
    [Fact]
    public async Task In_stores_without_stored_roles_the_publish_filter_and_the_offer_guard_answer_the_pinned_decision_with_an_approved_or_a_proposed_authored_role()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var w = await BuildAsync(host);
        try
        {
            foreach (var store in new[] { "authored", "proposed" })
            {
                Tenant? stored;
                await using (var q = host.Store.QuerySession()) stored = await q.LoadAsync<Tenant>(w.Slug(store));
                // ‏ما يُجَسِّدُه `LoadTenantWithRolesAsync` (داخِلِيَّة) بِحَرفِه: المُخَزَّنَةُ وفَوقَها المُؤَلَّفَةُ المُعتَمَدَة.
                var set = await ACommerce.Templates.Customer.Marketplace.Services.TenantRoleService.ReadUncachedAsync(host.Store, w.Slug(store));
                var materialized = set.Materialize(stored!.Roles);
                output.WriteLine($"[قَرار] {store}: أَدوارٌ مُخَزَّنَة {stored.Roles.Count} · مُجَسَّدَة {materialized.Count}"
                                 + (materialized.Count == 0 ? "" : ": " + string.Join(",", materialized.Select(r => r.Slug))));
                Assert.Empty(stored.Roles);
                Assert.Equal(store == "authored" ? [Authored] : Array.Empty<string>(), materialized.Select(r => r.Slug).ToArray());
            }

            var diff = new List<string>();
            foreach (var (c, filter, has) in Pinned)
            {
                var measured = await DecideAsync(w, c);
                output.WriteLine($"[قَرار] {c} ⇒ المُرَشِّح {measured.Filter} · HasPermissionAsync {measured.Has}");
                if (measured != (filter, has)) diff.Add($"{c}: المُثَبَّت ({filter}, {has}) · المَقيس ({measured.Filter}, {measured.Has})");
            }
            output.WriteLine($"[قَرار] صُفوفٌ مُقارَنَة {Pinned.Length} · مَرَّ مِنها إلى النُقطَة {Pinned.Count(p => p.Filter == "next")} · فُروق {diff.Count}");
            Assert.True(Pinned.Length > 0, "أَداةٌ عَمياء: صِفرُ صَفّ.");
            Assert.True(diff.Count == 0, "قَرارُ الدَورِ تَغَيَّر:\n" + string.Join("\n", diff));
        }
        finally { await CleanAsync(w); }
    }
}
