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

// ═══ قَرارُ الدَورِ عِندَ حارِسَيِ الصَلاحِيَّة — يُثَبَّتُ قَبلَ أَن يَكتُبا الدَورَ الفَعّالَ في حامِلِ العَدّ (‏2026-11-12، الموجَة ١٣ W13-6·١٠ — ADR-051 §٧ صَفُّ «١٠») ═══
//
// **لِماذا الآن** (القاعِدَتانِ ٣ و١٣؛ التَصميم §5.4): الكوميتُ التالي يَجعَلُ `PermissionFilter` (قَبلَ `return await next(ctx)`) و`HasPermissionAsync`
// يَكتُبانِ الدَورَ الفَعّالَ الَّذي يَحسِبانِه — ويُرمى اليَوم — في حامِلِ العَدِّ حينَ يَقبَلان. والحارِسانِ بابا كُلِّ نَشرٍ وكُلِّ عَرض، فَالدَعوى «القَرارُ لا
// يَتَغَيَّر» تُقاسُ ولا تُقال: هذا المِلَفُّ يُثَبِّتُ جَوابَ كُلٍّ مِنهُما لِكُلِّ مَتجَرٍ ودَورٍ وصَلاحِيَّة — بِالدَورِ المُخَزَّنِ و`as` المَملوكِ وغَيرِ المَملوكِ
// ودَورِ الرابِط، والإدارِيِّ الَّذي لا يُؤخَذُ إلّا مُخَزَّناً — ويُودَعُ وَحدَه، ثُمَّ يَبقى أَخضَرَ بَعدَ التَبديلِ **بِلا مَسِّ حَرف** (بَصمَتُه في رِسالَةِ كوميتِ التَبديل).
//
// **والحارِسانِ بِأَعيانِهِما لا نُسخَةٌ مِنهُما**: المُرَشِّحُ يُنادى كَما يُناديه أُنبوبُ النُقطَة (شَكلُ `LiveComposedRolePublishProofTests`)، و`HasPermissionAsync`
// — خاصَّةٌ ساكِنَةٌ في `MarketplaceTemplateExtensions` — بِالانعِكاسِ بِتَوقيعِها، فَإن غابَت أَو تَغَيَّرَ تَوقيعُها احمَرَّ الصَفّ. وبابُ العَرضِ — مُستَهلِكُها
// الوَحيد — بِجِسمِه المُسَجَّل (`LiveTemplateDoorHost`): المَرفوضُ يُرَدُّ إلى `?err=forbidden`، والمَقبولُ يَمُرُّ إلى أَوَّلِ حَقلٍ (`?err=offer_price`)
// ولا يُكتَبُ شَيء. ولا يُقرَأُ `meter.*` هُنا أَصلاً: ما يَكتُبُه الحارِسانِ في الحامِلِ يُثبِتُه بُرهانُه، وهذا يُثبِتُ أَنّ القَرارَ لَم يَتَغَيَّر.

[Collection(LiveProofCollection.Name)]
public class LiveRoleDecisionCharacterizationProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private const string OfferDoor = "/{slug}/listings/{id:guid}/offers";

    /// <summary>حارِسُ العَرضِ بِعَينِه — خاصٌّ ساكِن؛ غِيابُه أَو تَغَيُّرُ تَوقيعِه يُحمِرُّ الصَفّ (أَداةٌ لا تَجِدُه عَمياء).</summary>
    private static Task<bool> HasPermissionAsync(HttpContext http, string slug, Guid userId, string permission, IDocumentStore store)
    {
        var method = typeof(MarketplaceTemplateExtensions).GetMethod("HasPermissionAsync", BindingFlags.NonPublic | BindingFlags.Static,
            [typeof(HttpContext), typeof(string), typeof(Guid), typeof(string), typeof(IDocumentStore)]);
        Assert.True(method is not null, "HasPermissionAsync بِتَوقيعِها غائِبَة — الأَداةُ عَمياء.");
        return (Task<bool>)method!.Invoke(null, [http, slug, userId, permission, store])!;
    }

    /// <summary>صَفٌّ مِن المَصفوفَة: المَتجَرُ (<c>missing</c> · <c>zero</c> بِلا أَدوار · <c>roled</c>) والمُستَخدِم (مِفتاحُه أَدناه، أَو <c>—</c> بِلا هُوِيَّة)
    /// والصَلاحِيَّة، و<c>as</c> في الاستِمارَةِ وهَل يَحمِلُ كوكيَّ دَورِه، ودَورُ الرابِط (<c>/{slug}/r/{role}/…</c>).</summary>
    private sealed record Case(string Store, string User, string Permission, string? As = null, bool AsCookie = false, string? UrlRole = null)
    {
        public override string ToString()
            => $"{Store} · {User} · {Permission} · as={As ?? "—"}{(AsCookie ? "+كوكي" : "")} · رابِط={UrlRole ?? "—"}";
    }

    /// <summary><b>المُثَبَّت</b> — جَوابُ المُرَشِّح (<c>next</c> = مَرَّ إلى النُقطَة، أَو رَمزُ رَدِّه) وجَوابُ <c>HasPermissionAsync</c> لِكُلِّ صَفّ، مَقيسَينِ
    /// عِندَ <c>79f0299</c> عَلى Postgres 16 مَحَلِّيّ. وصَفٌّ بِلا هُوِيَّةٍ لا يَبلُغُ <c>HasPermissionAsync</c> (تَوقيعُها يَطلُبُ مُعَرِّفاً) فَجَوابُه <c>—</c>.</summary>
    private static readonly (Case Case, string Filter, string Has)[] Pinned =
    [
        (new("missing", "vendor", "listing.create"), "404", "False"),
        (new("zero", "absent", "listing.create"), "next", "True"),
        (new("zero", "absent", "offer.submit"), "next", "True"),
        (new("zero", "absent", "tenant.manage"), "403", "False"),
        (new("zero", "—", "listing.create"), "401", "—"),
        (new("roled", "—", "listing.create"), "401", "—"),
        (new("roled", "absent", "listing.create"), "403", "False"),
        (new("roled", "vendor", "listing.create"), "next", "True"),
        (new("roled", "vendor", "offer.submit"), "403", "False"),
        (new("roled", "vendor", "tenant.manage"), "403", "False"),
        (new("roled", "customer", "listing.create"), "403", "False"),
        (new("roled", "customer", "offer.submit"), "next", "True"),
        (new("roled", "admin", "tenant.manage"), "next", "True"),
        (new("roled", "admin", "listing.create"), "next", "True"),
        (new("roled", "none", "listing.create"), "403", "False"),
        (new("roled", "none", "offer.submit"), "403", "False"),
        (new("roled", "ghost", "listing.create"), "403", "False"),
        (new("roled", "customer", "listing.create", As: "vendor", AsCookie: true), "next", "True"),
        (new("roled", "customer", "listing.create", As: "vendor"), "403", "False"),
        (new("roled", "none", "listing.create", As: "vendor", AsCookie: true), "next", "True"),
        (new("roled", "vendor", "offer.submit", As: "customer", AsCookie: true), "next", "True"),
        (new("roled", "customer", "listing.create", UrlRole: "vendor"), "next", "True"),
        (new("roled", "vendor", "listing.create", UrlRole: "customer"), "403", "False"),
        (new("roled", "customer", "tenant.manage", UrlRole: "tenant_admin"), "403", "False"),
        (new("roled", "customer", "tenant.manage", As: "tenant_admin", AsCookie: true), "403", "False"),
        (new("roled", "admin", "tenant.manage", UrlRole: "tenant_admin"), "next", "True"),
        (new("roled", "admin", "offer.submit", UrlRole: "vendor"), "403", "False"),
    ];

    private sealed record World(LiveTemplateDoorHost Host, string Missing, string Zero, string Roled, IReadOnlyDictionary<string, User> Users)
    {
        public string Slug(string store) => store switch { "missing" => Missing, "zero" => Zero, _ => Roled };
    }

    /// <summary>المَتاجِرُ الثَلاثَةُ وأَعضاءُ المَتجَرِ ذي الأَدوار: أَدوارُه مِن الكَتالوجِ بِدالَّتِه (<c>RoleCatalog.InstantiateRole</c>) — تاجِرٌ وعَميلٌ ومُديرُ مَتجَر —
    /// وأَعضاؤُه بِدَورٍ مُخَزَّنٍ لِكُلٍّ، وبِلا دَور، وبِدَورٍ لَيسَ في المَتجَر؛ و<c>absent</c> مُعَرِّفٌ لا وَثيقَةَ لَه.</summary>
    private static async Task<World> BuildAsync(LiveTemplateDoorHost host)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (missing, zero, roled) = ($"w13rd-missing-{tag}", $"w13rd-zero-{tag}", $"w13rd-roled-{tag}");
        Role Instantiate(string slug, int order) => RoleCatalog.InstantiateRole(RoleCatalog.Find(slug)!, sortOrder: order);
        await using (var s = host.Store.LightweightSession())
        {
            s.Insert(new Tenant { Id = zero, Name = "مَتجَرٌ بِلا أَدوار", OwnerUserId = Guid.NewGuid() });
            s.Insert(new Tenant { Id = roled, Name = "مَتجَرٌ بِأَدوار", OwnerUserId = Guid.NewGuid(),
                                  Roles = [Instantiate("vendor", 0), Instantiate("customer", 1), Instantiate(SelfGrantPolicy.AdminSlug, 2)] });
            await s.SaveChangesAsync();
        }
        static User Member(string? role) => new() { Id = Guid.NewGuid(), Phone = "+9665" + Random.Shared.Next(10_000_000, 99_999_999), FullName = "عُضوُ القَرار",
                                                   ActiveRole = role ?? "", Role = role ?? "user" };
        var users = new Dictionary<string, User>(StringComparer.Ordinal)
        {
            ["vendor"] = Member("vendor"), ["customer"] = Member("customer"), ["admin"] = Member(SelfGrantPolicy.AdminSlug),
            ["none"] = Member(null), ["ghost"] = Member("ghost"),
        };
        await using (var s = host.Store.LightweightSession(roled))
        {
            foreach (var u in users.Values) s.Store(u);
            await s.SaveChangesAsync();
        }
        users["absent"] = new User { Id = Guid.NewGuid() };
        return new World(host, missing, zero, roled, users);
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
                     "delete from platform.mt_doc_tenant where id = any(@t)",
                 })
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("t", new[] { w.Zero, w.Roled });
            try { await cmd.ExecuteNonQueryAsync(); } catch (PostgresException e) when (e.SqlState == "42P01") { }
        }
    }

    /// <summary><b>المَصفوفَة</b>: كُلُّ صَفٍّ يُشَغَّلُ عَلى الحارِسَينِ مَعاً، ويُطبَعُ بِجَوابِهِما، ويُقارَنُ بِالمُثَبَّت — صَفٌّ واحِدٌ مُختَلِفٌ يَحمَرّ، وصِفرُ صَفٍّ أَداةٌ عَمياء.</summary>
    [Fact]
    public async Task The_publish_filter_and_the_offer_guard_answer_the_pinned_decision_for_every_store_role_and_permission()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var w = await BuildAsync(host);
        try
        {
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

    /// <summary><b>بابُ العَرضِ — مُستَهلِكُ <c>HasPermissionAsync</c> الوَحيد</b>: المَقبولُ يَمُرُّ إلى أَوَّلِ حَقلٍ (سِعرٌ فارِغ ⇐ <c>?err=offer_price</c>) والمَرفوضُ
    /// <c>?err=forbidden</c>، ولا يَكتُبُ أَيٌّ مِنهُما حَدَثاً ولا عَرضاً في مَتجَرِه.</summary>
    [Fact]
    public async Task The_offer_door_turns_a_refusal_into_forbidden_and_an_acceptance_into_its_first_field_and_writes_nothing()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var w = await BuildAsync(host);
        try
        {
            async Task<long> WrittenAsync()
            {
                await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
                await conn.OpenAsync();
                long n = 0;
                foreach (var table in new[] { "mt_events", "mt_doc_offer" })
                {
                    await using var cmd = new NpgsqlCommand($"select count(*) from platform.{table} where tenant_id = any(@t)", conn);
                    cmd.Parameters.AddWithValue("t", new[] { w.Zero, w.Roled });
                    try { n += Convert.ToInt64(await cmd.ExecuteScalarAsync()); } catch (PostgresException e) when (e.SqlState == "42P01") { }
                }
                return n;
            }

            var before = await WrittenAsync();
            var listing = Guid.NewGuid();
            var rows = new (string Store, string User, string Location)[]
            {
                ("roled", "customer", "offer_price"), ("roled", "admin", "offer_price"), ("roled", "vendor", "forbidden"),
                ("roled", "none", "forbidden"), ("roled", "ghost", "forbidden"), ("zero", "absent", "offer_price"),
            };
            var diff = new List<string>();
            foreach (var (storeKey, userKey, expected) in rows)
            {
                var slug = w.Slug(storeKey);
                var user = w.Users[userKey];
                var route = new Dictionary<string, string> { ["slug"] = slug, ["id"] = listing.ToString() };
                var (status, location) = await host.PostFormAsync(OfferDoor, route, slug, AuthHandlers.MakeToken(user.Id, slug, user.SessionVersion),
                    ("price", ""), ("message", "عَرضُ القَرار"));
                var want = $"/{slug}/listings/{listing}?err={expected}";
                output.WriteLine($"[بابُ العَرض] {storeKey} · {userKey} ⇒ {status} {location}");
                if ((status, location) != (302, want)) diff.Add($"{storeKey} · {userKey}: المُثَبَّت 302 {want} · المَقيس {status} {location}");
            }
            var after = await WrittenAsync();
            output.WriteLine($"[بابُ العَرض] صُفوف {rows.Length} · مَقبولَة {rows.Count(r => r.Location == "offer_price")} · فُروق {diff.Count} · أَحداثٌ وعُروضٌ في المَتجَرَين {before} ← {after}");
            Assert.True(diff.Count == 0, "جَوابُ بابِ العَرضِ تَغَيَّر:\n" + string.Join("\n", diff));
            Assert.Equal(before, after);
        }
        finally { await CleanAsync(w); }
    }
}
