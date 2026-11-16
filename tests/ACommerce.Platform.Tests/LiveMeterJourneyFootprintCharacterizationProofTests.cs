using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.Shared;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ بَصمَةُ الرِحلَةِ في جَداوِلِ `platform` — تُثَبَّتُ قَبلَ مُستَمِعِ العَدّ (‏2026-11-12، الموجَة ١٣ W13-6·٢ — ADR-051) ═══════════════
//
// **لِماذا قَبلَ أَيِّ مُستَمِع** (القاعِدَتانِ ٣ و١٣): البَندُ ٣ يُسَجِّلُ مُستَمِعَ جَلسَةٍ عامّاً (`UsageListener`) في تَركيبِ القالِب، فَيَمُرُّ بِه
// كُلُّ حِفظٍ في كُلِّ جَلسَة. ووَعدُه في ADR-051 أَنّه **يَكتُبُ في `meter.*` وَحدَها ولا يُغَيِّرُ صَفّاً في `platform`**. والوَعدُ لا يُحفَظُ بِاسمِه
// (القاعِدَة ٢): هذا المِلَفُّ يَعُدُّ صُفوفَ كُلِّ جَدوَلٍ في `platform` بَعدَ رِحلَةٍ ثابِتَةٍ عَلى قاعِدَةٍ فارِغَةٍ جَديدَة، ويُثَبِّتُها بِالرَقَم؛
// ويُودَعُ وَحدَه، ثُمَّ تُعيدُ البُنودُ ٣ و٧ و٨ و٩ تَشغيلَه **بِلا تَعديلِ حَرف** (بَصمَتُه sha256 في رِسالَةِ كُلٍّ مِنها). فَإن كَتَبَ المُستَمِعُ
// أَو تَبديلُ `Store`←`Insert` صَفّاً واحِداً في `platform` أَو أَنشَأَ جَدوَلاً فيها، احمَرَّ هُنا.
//
// **والرِحلَةُ بِأَبوابِها المُسَجَّلَةِ لا بِنُسَخٍ مِنها** (`LiveTemplateDoorHost` — تَركيبُ المُضيفِ والقالِبِ بِعَينِه): دُخولُ رائِدِ الأَعمالِ
// إلى الاستوديو بِرَمز، ومُوافَقَتُه، وإنشاءُ مَتجَرِه (`store.create`)؛ وانضِمامُ عُضوَينِ بِرَمزِ الجَوّال (`member.join`)؛ والبائِعُ يَختارُ دَورَه
// ويَنشُرُ إعلاناً (`listing.create`)؛ والمُشتَري يُقَدِّمُ عَرضاً (`offer.submit`)، ويَطلُبُ (`deal.create`) فَتَمضي الصَفقَةُ إلى الاكتِمال،
// ويُقَيِّمُ (`review.create`)، ويُراسِلُ (`chat.message`)، ويَفتَحُ تَذكِرَة (`ticket.open`). وكُلُّ خُطوَةٍ تُثبِتُ جَوابَ بابِها — فَبَصمَةٌ عَلى رِحلَةٍ
// انقَطَعَت لا تُثَبَّتُ صامِتَة.
//
// **وقاعِدَةٌ جَديدَةٌ لِكُلِّ تَشغيل** لا القاعِدَةُ المُشتَرَكَة: البَراهينُ الحَيَّةُ الأُخرى تَكتُبُ في القاعِدَةِ نَفسِها بِمُعَرِّفاتٍ عَشوائِيَّة، فَعَدُّ
// الصُفوفِ فيها لا يُثَبَّت. يُنشِئُ الاختِبارُ `{القاعِدَة}_fp_{8}` بِجانِبِ قاعِدَةِ البيئَة، ويَحذِفُها هُوَ في `finally` — ولا يَمَسُّ غَيرَها.
//
// **والاستِثناءُ الوَحيدُ مَكتوبٌ بِسَبَبِه** (RULES-18، التَصميم §٨ و§١٢، ADR-051 سَطرُ الأَثَر): `wolverine_%` — طابورُ Wolverine المَتين يَكتُبُ
// `platform.wolverine_incoming_envelopes` (‏`LiveScheduledExpiryProofTests`)، ولَقطَةُ الحُضورِ اليَومِيَّةُ (البَند ١٩) رِسالَةٌ مُجَدوَلَةٌ فيه
// تُعَدُّ في بُرهانِها هِيَ. فَجَداوِلُه تُطبَعُ ولا تُقارَن. **وما سِواها لا يُستَثنى**: `meter.*` خارِجَ `platform` أَصلاً فَلا يَحتاجُ قاعِدَة،
// ويُطبَعُ عَدُّه لِلبُرهانِ وَحدَه («لا يَنمو إلّا `meter.*`»).
//
// **والأَداةُ تُقاسُ قَبلَ أَن يُوثَقَ بِها** (القاعِدَة ١٠): تَطبَعُ عَدَدَ الجَداوِلِ المُقارَنَةِ والصُفوفَ لِكُلِّ جَدوَل، وتَفشَلُ عَلى صِفرِ جَدوَل
// (مَخزَنٌ يَكتُبُ في غَيرِ القاعِدَةِ الجَديدَةِ يُعطي صِفراً)، وعَلى جَدوَلٍ زائِدٍ أَو ناقِصٍ أَو صَفٍّ واحِدٍ مُختَلِف.

[Collection(LiveProofCollection.Name)]
public class LiveMeterJourneyFootprintCharacterizationProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>المُخَطَّطُ الَّذي يُعَدّ — مُخَطَّطُ Marten لِلمُضيف (<c>HostingExtensions.AddPlatformHost</c>).</summary>
    internal const string Schema = "platform";

    /// <summary><b>القاعِدَةُ المَكتوبَة</b> (RULES-18): جَداوِلُ طابورِ Wolverine المَتينِ لا تُقارَن — تُطبَعُ وَحدَها. لِأَنّ رَسائِلَ لَقطَةِ الحُضورِ
    /// المُجَدوَلَةَ تُكتَبُ فيها ابتِداءً مِن البَند ١٩ وتُعَدُّ في بُرهانِها (التَصميم §٨)، والطابورُ بُنيَةٌ تَحتِيَّةٌ لا أَثَرُ رِحلَة. نَمَطُ <c>LIKE</c>،
    /// ويُطَبَّقُ بادِئَةً حَرفِيَّةً (<c>wolverine_</c>).</summary>
    internal const string ExcludedTables = "wolverine_%";

    internal static bool Excluded(string table) => table.StartsWith(ExcludedTables.TrimEnd('%'), StringComparison.Ordinal);

    /// <summary><b>البَصمَةُ المُثَبَّتَة</b> — صُفوفُ كُلِّ جَدوَلٍ في <c>platform</c> (عَدا <see cref="ExcludedTables"/>) بَعدَ الرِحلَة، مَقيسَةً عِندَ
    /// <c>5eea4f2</c> عَلى Postgres 16 مَحَلِّيٍّ فارِغ. جَدوَلٌ زائِدٌ أَو ناقِصٌ أَو رَقَمٌ مُختَلِفٌ يَحمَرّ.</summary>
    internal static readonly IReadOnlyDictionary<string, long> Footprint = new SortedDictionary<string, long>(StringComparer.Ordinal)
    {
        ["mt_doc_consentrecord"] = 1,          // مُوافَقَةُ رائِدِ الأَعمال
        ["mt_doc_conversation"] = 1,
        ["mt_doc_deadletterevent"] = 0,        // جَداوِلُ الأَصفارِ أَنشَأَتها قِراءاتُ الأَبوابِ نَفسِها — وجَدوَلٌ جَديدٌ في platform تَغييرٌ يُرى
        ["mt_doc_deal"] = 1,
        ["mt_doc_listing"] = 1,                // لَقطَةٌ مُضَمَّنَةٌ لِتَيّارِ الإعلان
        ["mt_doc_listingmatch"] = 0,
        ["mt_doc_message"] = 1,
        ["mt_doc_notification"] = 3,           // مَقيسَةٌ بِأَنواعِها: deal ← البائِع · deal_advanced ← المُشتَري · chat_message ← البائِع
        ["mt_doc_offer"] = 1,
        ["mt_doc_review"] = 1,
        ["mt_doc_savedsearch"] = 0,
        ["mt_doc_studiouser"] = 1,             // رائِدُ الأَعمال
        ["mt_doc_subscription"] = 0,
        ["mt_doc_tenant"] = 1,
        ["mt_doc_tenantplan"] = 0,
        ["mt_doc_tenantroledefinition"] = 0,
        ["mt_doc_ticket"] = 1,
        ["mt_doc_user"] = 2,                   // البائِعُ والمُشتَري
        ["mt_event_progression"] = 0,
        ["mt_events"] = 3,                     // ListingCreated · OfferSubmitted · TicketCreated
        ["mt_high_water_skips"] = 0,
        ["mt_streams"] = 3,
    };

    /// <summary>قَناةُ رُموزٍ لا تُرسِل — تَحفَظُ آخِرَ رَمزٍ لِكُلِّ جَوّالٍ لِيُعادَ إلى بابِ التَحَقُّق كَما يَكتُبُه صاحِبُه.</summary>
    private sealed class CodeKeepingSms : IOtpChannel
    {
        private readonly Dictionary<string, string> _codes = new(StringComparer.Ordinal);
        public string ChannelName => "footprint-sms";
        public string? DevHintCode => null;
        public Task SendOtpAsync(string phone, string code, CancellationToken ct)
        {
            lock (_codes) _codes[PhoneNumber.Canonical(phone)] = code;
            return Task.CompletedTask;
        }
        public Task SendNoticeAsync(string phone, string text, CancellationToken ct) => Task.CompletedTask;
        public string CodeFor(string phone)
        {
            lock (_codes)
                return _codes.TryGetValue(PhoneNumber.Canonical(phone), out var code)
                    ? code : throw new InvalidOperationException($"لا رَمزَ أُرسِلَ إلى {phone} — البابُ لَم يُرسِل.");
        }
    }

    private static string NewPhone() => "+9665" + Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static Dictionary<string, string> Route(params (string Key, string Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);

    /// <summary>القاعِدَةُ الجَديدَة: بِجانِبِ قاعِدَةِ البيئَةِ بِاسمِها ولاحِقَةِ <c>_fp_</c>.</summary>
    private static (string Name, string ConnectionString) FreshDatabase()
    {
        var b = new NpgsqlConnectionStringBuilder(LiveDealConcurrencyProofTests.ConnectionString);
        var stem = (b.Database ?? "wasayel").ToLowerInvariant();
        if (stem.Length > 48) stem = stem[..48];
        var name = $"{stem}_fp_{Guid.NewGuid().ToString("N")[..8]}";
        b.Database = name;
        return (name, b.ConnectionString.TrimEnd(';') + ";Timeout=60;Command Timeout=120");
    }

    private static async Task AdminAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>صُفوفُ كُلِّ جَدوَلٍ في المُخَطَّطاتِ غَيرِ النِظامِيَّة — بِـNpgsql لا بِـMarten، فَالعَدُّ لا يُنشِئُ جَدوَلاً ولا يَكتُبُ صَفّاً.</summary>
    private static async Task<List<(string Schema, string Table, long Rows)>> CountAsync(string connectionString)
    {
        var tables = new List<(string Schema, string Table)>();
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand(
            "select n.nspname, c.relname from pg_class c join pg_namespace n on n.oid = c.relnamespace " +
            "where c.relkind in ('r','p') and not c.relispartition " +
            "and n.nspname not in ('pg_catalog','information_schema') and n.nspname not like 'pg\\_%' " +
            "order by n.nspname, c.relname", conn))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) tables.Add((r.GetString(0), r.GetString(1)));

        var counted = new List<(string, string, long)>();
        foreach (var (schema, table) in tables)
        {
            await using var cmd = new NpgsqlCommand($"select count(*) from \"{schema}\".\"{table}\"", conn);
            counted.Add((schema, table, Convert.ToInt64(await cmd.ExecuteScalarAsync())));
        }
        return counted;
    }

    [Fact]
    public async Task The_fixed_journey_leaves_the_pinned_rows_in_every_platform_table_on_a_fresh_database()
    {
        if (!Enabled) return;

        var (database, connectionString) = FreshDatabase();
        await AdminAsync($"create database \"{database}\"");
        output.WriteLine($"[footprint] قاعِدَةٌ جَديدَة: {database}");
        try
        {
            await WalkAsync(connectionString);
            NpgsqlConnection.ClearAllPools();

            var all = await CountAsync(connectionString);
            var compared = all.Where(t => t.Schema == Schema && !Excluded(t.Table)).ToDictionary(t => t.Table, t => t.Rows, StringComparer.Ordinal);
            var excluded = all.Where(t => t.Schema == Schema && Excluded(t.Table)).ToList();
            var others = all.Where(t => t.Schema != Schema).GroupBy(t => t.Schema).ToList();

            foreach (var (table, rows) in compared.OrderBy(t => t.Key, StringComparer.Ordinal))
                output.WriteLine($"[footprint] {Schema}.{table} = {rows}");
            output.WriteLine($"[footprint] جَداوِلُ مُقارَنَة {compared.Count} · صُفوفُها {compared.Values.Sum()} · مُستَثناةٌ بِـ«{ExcludedTables}» {excluded.Count}"
                + (excluded.Count == 0 ? "" : ": " + string.Join(", ", excluded.Select(t => $"{t.Table}={t.Rows}"))));
            foreach (var g in others)
                output.WriteLine($"[footprint] خارِجَ {Schema} (يُطبَعُ ولا يُقارَن): {g.Key} — جَداوِل {g.Count()} · صُفوف {g.Sum(t => t.Rows)}");
            output.WriteLine("[footprint] المُقاسُ بِشَكلِ المِلَفّ:\n" + string.Join("\n", compared.OrderBy(t => t.Key, StringComparer.Ordinal)
                .Select(t => $"        [\"{t.Key}\"] = {t.Value},")));

            Assert.True(compared.Count > 0, $"أَداةٌ عَمياء: صِفرُ جَدوَلٍ في {Schema} — المَخزَنُ لَم يَكتُب في القاعِدَةِ الجَديدَة.");
            var diff = compared.Keys.Union(Footprint.Keys).OrderBy(k => k, StringComparer.Ordinal)
                .Select(k => (Table: k, Pinned: Footprint.TryGetValue(k, out var p) ? p : (long?)null, Measured: compared.TryGetValue(k, out var m) ? m : (long?)null))
                .Where(d => d.Pinned != d.Measured)
                .Select(d => $"{Schema}.{d.Table}: المُثَبَّت {d.Pinned?.ToString() ?? "غائِب"} · المَقيس {d.Measured?.ToString() ?? "غائِب"}")
                .ToList();
            output.WriteLine($"[footprint] قورِنَ {Footprint.Count} جَدوَلاً مُثَبَّتاً بِـ{compared.Count} مَقيساً — فُروق {diff.Count}");
            Assert.True(diff.Count == 0, "بَصمَةُ الرِحلَةِ تَغَيَّرَت في " + Schema + ":\n" + string.Join("\n", diff));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await AdminAsync($"drop database if exists \"{database}\" with (force)");
            output.WriteLine($"[footprint] حُذِفَت {database}");
        }
    }

    /// <summary>الرِحلَةُ الثابِتَة — كُلُّ خُطوَةٍ بِبابِها المُسَجَّل، وكُلُّ جَوابٍ مُثبَت.</summary>
    private async Task WalkAsync(string connectionString)
    {
        var sms = new CodeKeepingSms();
        await using var host = LiveTemplateDoorHost.Build(
            marten: o => o.Connection(connectionString),
            services: s => s.AddSingleton<IOtpChannel>(sms));

        // ١ · رائِدُ الأَعمال: دُخولُ الاستوديو بِرَمز ← المُوافَقَة ← إنشاءُ المَتجَر.
        var ownerPhone = NewPhone();
        var step = await host.PostStudioFormAsync("/studio/auth/login", Route(), null, ("phone", ownerPhone));
        Assert.Equal(302, step.Status);
        step = await host.PostStudioFormAsync("/studio/auth/verify", Route(), null,
            ("method", "phone"), ("phone", ownerPhone), ("code", sms.CodeFor(ownerPhone)));
        Assert.Equal((302, StudioStoreCreate.Page), step);
        StudioUser owner;
        await using (var q = host.Store.QuerySession(StudioAuth.Tenant))
            owner = Assert.Single(await q.Query<StudioUser>().Where(u => u.Phone == PhoneNumber.Canonical(ownerPhone)).ToListAsync());
        var studioToken = AuthHandlers.MakeToken(owner.Id, StudioAuth.Tenant, owner.SessionVersion);
        step = await host.PostStudioFormAsync("/studio/consent/accept", Route(), studioToken, ("returnUrl", StudioStoreCreate.Page));
        Assert.Equal((302, StudioStoreCreate.Page), step);
        var slug = "w13fp-" + Guid.NewGuid().ToString("N")[..8];
        step = await host.PostStudioFormAsync(StudioStoreCreate.Endpoint, Route(), studioToken,
            ("slug", slug), ("name", "مَتجَرُ البَصمَة"), ("color", "#0F766E"), ("tagline", "رِحلَةٌ ثابِتَة"));
        Assert.Equal((302, $"/studio/apps/{slug}?built=1"), step);
        Tenant tenant;
        await using (var q = host.Store.QuerySession())
            tenant = (await q.LoadAsync<Tenant>(slug))!;
        Assert.NotNull(tenant);
        output.WriteLine($"[journey] مَتجَر {slug} · قَناة {tenant.AuthChannel} · أَدوار {tenant.Roles.Count}");

        // ٢ · عُضوانِ يَنضَمّانِ بِرَمزِ الجَوّال — والمُحَلِّلُ يَضَعُ المَتجَرَ في الطَلَبِ كَما يَضَعُه `TenantResolverMiddleware`.
        void AsStore(Microsoft.AspNetCore.Http.HttpContext ctx)
            => ctx.SetTenant(tenant.Id, tenant.Name, tenant.BrandColor, tenant.AuthChannel, tenant.TagLine, tenant.City, hasRoles: tenant.Roles.Count > 0);
        async Task<(User Member, string Token)> JoinAsync(string phone)
        {
            var sent = await host.PostFormOnHostAsync("/{slug}/auth/phone/login", Route(("slug", slug)), null, AsStore, ("phone", phone));
            Assert.Equal(302, sent.Status);
            Assert.DoesNotContain("err=", sent.Location, StringComparison.Ordinal);
            var verified = await host.PostFormOnHostAsync("/{slug}/auth/phone/verify", Route(("slug", slug)), null, AsStore,
                ("phone", phone), ("code", sms.CodeFor(phone)));
            Assert.Equal(302, verified.Status);
            Assert.DoesNotContain("err=", verified.Location, StringComparison.Ordinal);
            await using var q = host.Store.QuerySession(slug);
            var member = Assert.Single(await q.Query<User>().Where(u => u.Phone == PhoneNumber.Canonical(phone)).ToListAsync());
            output.WriteLine($"[journey] انضَمّ {phone} ⇒ {verified.Location}");
            return (member, AuthHandlers.MakeToken(member.Id, slug, member.SessionVersion));
        }
        var (seller, sellerToken) = await JoinAsync(NewPhone());
        var (buyer, buyerToken) = await JoinAsync(NewPhone());

        async Task<string> PostAsync(string pattern, IReadOnlyDictionary<string, string> route, string token, params (string, string)[] form)
        {
            var (status, location) = await host.PostFormAsync(pattern, route, slug, token, form);
            output.WriteLine($"[journey] POST {pattern} ⇒ {status} {location}");
            Assert.Equal(302, status);
            Assert.DoesNotContain("err=", location, StringComparison.Ordinal);
            return location;
        }
        var store = Route(("slug", slug));

        // ٣ · البائِعُ يَختارُ «تاجِر» ويَملَأُ حَقلَه، والمُشتَري «عَميل» — كَما تَمشيهِما رِحلَةُ الإطلاق — ويَقبَلانِ الشُروط.
        await PostAsync("/{slug}/me/role/save", store, sellerToken, ("role", "vendor"));
        await PostAsync("/{slug}/me/role/onboarding/save", store, sellerToken, ("role_vendor_attr_business_name", "نَشاطُ البَصمَة"));
        await PostAsync("/{slug}/terms/accept", store, sellerToken);
        await PostAsync("/{slug}/me/role/save", store, buyerToken, ("role", "customer"));
        await PostAsync("/{slug}/terms/accept", store, buyerToken);

        // ٤ · إعلانٌ يُنشَر.
        var at = await PostAsync("/{slug}/listings/create", store, sellerToken,
            ("title", "سِلعَةُ البَصمَة"), ("category", "general"), ("price", "120"), ("description", "إعلانُ رِحلَةِ العَدّ"));
        var listingId = Guid.Parse(at[(at.LastIndexOf('/') + 1)..]);
        var listing = Route(("slug", slug), ("id", listingId.ToString()));

        // ٥ · عَرضٌ عَلى الإعلان.
        await PostAsync("/{slug}/listings/{id:guid}/offers", listing, buyerToken,
            ("price", "110"), ("message", "عَرضُ البَصمَة"), ("lat", "24.7136"), ("lng", "46.6753"), ("ttl_minutes", "15"));

        // ٦ · طَلَبٌ مِن صَفحَةِ الإعلان ← قَبولُ البائِع ← المَراحِلُ إلى الاكتِمال ← تَقييمُ المُشتَري.
        at = await PostAsync("/{slug}/listings/{id:guid}/deal", listing, buyerToken, ("note", "طَلَبُ البَصمَة"));
        var dealId = Guid.Parse(at[(at.LastIndexOf('/') + 1)..]);
        var deal = Route(("slug", slug), ("id", dealId.ToString()));
        await PostAsync("/{slug}/deals/{id:guid}/accept", deal, sellerToken);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var deals = scope.ServiceProvider.GetRequiredService<DealsService>();
            for (var i = 0; i < 8 && (await deals.LoadAsync(slug, dealId))!.Status == DealStatus.Active; i++)
            {
                var d = (await deals.LoadAsync(slug, dealId))!;
                var actor = DealsPolicy.RequiredActorToLeave(d) == "initiator" ? buyerToken : sellerToken;
                await PostAsync("/{slug}/deals/{id:guid}/advance", deal, actor, ("from", d.Stage.ToString()));
            }
            Assert.Equal(DealStatus.Completed, (await deals.LoadAsync(slug, dealId))!.Status);
        }
        await PostAsync("/{slug}/deals/{id:guid}/review", deal, buyerToken, ("rating", "5"), ("body", "تَقييمُ البَصمَة"));

        // ٧ · مُحادَثَةٌ مَعَ صاحِبِ الإعلانِ ورِسالَة.
        at = await PostAsync("/{slug}/listings/{id:guid}/chat", listing, buyerToken);
        var conversationId = Guid.Parse(at[(at.LastIndexOf('/') + 1)..]);
        await PostAsync("/{slug}/chats/{conversationId:guid}/send", Route(("slug", slug), ("conversationId", conversationId.ToString())), buyerToken,
            ("body", "رِسالَةُ البَصمَة"));

        // ٨ · تَذكِرَةُ دَعم.
        await PostAsync("/{slug}/support/open", store, buyerToken, ("subject", "تَذكِرَةُ البَصمَة"), ("body", "سُؤالُ رِحلَةِ العَدّ"));

        output.WriteLine($"[journey] اكتَمَلَت: مالِك {owner.Id} · بائِع {seller.Id} · مُشتَرٍ {buyer.Id} · إعلان {listingId} · صَفقَة {dealId} · مُحادَثَة {conversationId}");
    }
}
