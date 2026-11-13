using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.Shared;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G9 — سَقفُ `_unresolved` لِكُلِّ بُعدٍ ووَحدَةٍ عَلى رِحلَةِ الإطلاق، يَتَقَلَّصُ ولا يَنمو (‏2026-11-12، الموجَة ١٣ W13-6·١٠ — ADR-051 §١٧؛ التَصميم §12 G9) ═══
//
// **لِماذا** (القاعِدَتانِ ٢ و١٠): المُصَنِّفُ لا يَرمي لِفَجوَةِ تَصنيف بَل يَكتُبُ `_unresolved` — فَيُعَدُّ ويُعرَض. لكِنّ «يُعرَض» لا يَمنَعُ النُمُوّ: مُساعِدٌ
// يَكُفُّ عَن مَلءِ الحامِل، أَو وَسيطٌ يَكُفُّ عَن فَتحِه، يَقلِبُ سُطوراً صَحيحَةً `_unresolved` ولا يَحمَرُّ شَيء. فَهُنا **الرِحلَةُ بِأَبوابِها عَبرَ وَسيطِ
// المُستَأجِرِ الحَقيقِيّ** (`LiveTemplateDoorHost` بِـ`throughResolver` — أَوَّلُ سَطرٍ فيه يَفتَحُ الحامِلَ كَما في `UsePlatformMultiTenancy`) عَلى قاعِدَةٍ
// جَديدَةٍ فارِغَة، ثُمَّ يُعَدُّ `_unresolved` في كُلِّ عَمودٍ مِن الخَمسَةِ لِكُلِّ وَحدَة، ويُقارَنُ بِسَقفٍ مُثَبَّتٍ في `tests/meter-unresolved-ceiling.txt`:
// **زِيادَةٌ تَحمَرّ، ونَقصٌ يَحمَرُّ أَيضاً حَتّى يُخفَضَ السَقف** — فَلا يَعودُ ما تَقَلَّصَ إلّا بِتَعديلٍ مَرئِيٍّ في مُراجَعَة (القاعِدَة ١: قائِمَةٌ مُثَبَّتَةٌ تَتَقَلَّص).
//
// **والبَذرُ لا يُعَدُّ هُنا ويُطبَع**: سَطرٌ دَورُه أَو رائِدُ أَعمالِه `_seed` لَيسَ فَجوَةً بَل كِتابَةُ إقلاع (التَصميم §5.4)؛ وقاعِدَةٌ جَديدَةٌ بِلا بَذرٍ
// تُعطيه صِفراً يُطبَعُ ولا يُفتَرَض. **والسَقفُ أُخِذَ بَعدَ أَن خَضَرَّ G16** (`MeterScopeTests` بِصَفِّ شَكلِ `HasPermissionAsync`)، فَهُوَ ما يَبقى بَعدَ
// البَند ١٠: الدَورُ الفَعّالُ لِلنَشرِ والعَرضِ في الحامِل، وما سِواهُما بِلا حاسِبِ دَورٍ بَعد (الانضِمام، الطَلَب، التَقييم، الرِسالَة، التَذكِرَة، وبابا الاستوديو).
//
// **والأَداةُ تُقاسُ قَبلَ أَن يُوثَقَ بِها** (القاعِدَة ١٠): تَطبَعُ السُطورَ لِكُلِّ وَحدَةٍ وتَفشَلُ عَلى صِفرِ سَطرٍ أَو وَحدَةٍ مِن المُعجَمِ لَم تَبلُغها الرِحلَة؛
// والحَقنُ المَقيسُ أَحمَرَ في رِسالَةِ الكوميت: نَزعُ فاتِحِ الوَسيط، ونَزعُ مَلءِ `PermissionFilter`.

[Collection(LiveProofCollection.Name)]
public class LiveMeterUnresolvedCeilingProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>مِلَفُّ السَقف — سَطرٌ لِكُلِّ (بُعد، وَحدَة) عَدُّه فَوقَ الصِفر: <c>{بُعد} {وَحدَة} {عَدَد}</c>؛ وما لا سَطرَ لَه سَقفُه صِفر.</summary>
    internal static string CeilingFile => Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "tests", "meter-unresolved-ceiling.txt");

    /// <summary>الأَبعادُ الخَمسَةُ الَّتي قَد تَحمِلُ <c>_unresolved</c> — أَعمِدَةُ <c>meter.usage_entry</c> بِأَسمائِها (التَصميم §2).</summary>
    internal static readonly IReadOnlyList<string> Dimensions = ["app", "entrepreneur", "role", "feature", "provider"];

    internal static Dictionary<(string Dimension, string Unit), long> Parse(IEnumerable<string> lines)
    {
        var ceiling = new Dictionary<(string, string), long>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || !Dimensions.Contains(parts[0]) || !long.TryParse(parts[2], out var n) || n <= 0)
                throw new FormatException($"سَطرُ سَقفٍ لا يُقرَأ: «{line}» — الشَكلُ «بُعد وَحدَة عَدَدٌ مُوجَب».");
            if (!ceiling.TryAdd((parts[0], parts[1]), n)) throw new FormatException($"سَطرُ سَقفٍ مُكَرَّر: «{line}».");
        }
        return ceiling;
    }

    /// <summary>الحُكم — بِالاتِّجاهَين: مَقيسٌ فَوقَ سَقفِه «نَما»، ودونَه «تَقَلَّصَ فَاخفِض السَقف»، وسَطرٌ في المِلَفِّ لِوَحدَةٍ لَيسَت في المُعجَم «سَطرٌ بائِت».</summary>
    internal static List<string> Breaches(IReadOnlyDictionary<(string Dimension, string Unit), long> ceiling,
                                          IReadOnlyDictionary<(string Dimension, string Unit), long> measured, IReadOnlyCollection<string> units)
    {
        var breaches = new List<string>();
        foreach (var key in ceiling.Keys.Union(measured.Keys).OrderBy(k => k.Unit, StringComparer.Ordinal).ThenBy(k => k.Dimension, StringComparer.Ordinal))
        {
            var pinned = ceiling.GetValueOrDefault(key);
            var now = measured.GetValueOrDefault(key);
            if (!units.Contains(key.Unit)) breaches.Add($"stale_line {key.Dimension} {key.Unit}: لا وَحدَةَ بِهذا الاسمِ في المُعجَم");
            else if (now > pinned) breaches.Add($"grew {key.Dimension} {key.Unit}: السَقف {pinned} · المَقيس {now}");
            else if (now < pinned) breaches.Add($"shrank {key.Dimension} {key.Unit}: السَقف {pinned} · المَقيس {now} — اخفِض السَطرَ إلى {now}");
        }
        return breaches;
    }

    [Theory]
    [InlineData("role member.join 2", "role member.join 2", "")]
    [InlineData("role member.join 2", "role member.join 3", "grew")]
    [InlineData("role member.join 2", "role member.join 1", "shrank")]
    [InlineData("role member.join 2", "", "shrank")]
    [InlineData("", "role listing.create 1", "grew")]
    [InlineData("role no.such.unit 1", "", "stale_line")]
    public void The_ceiling_verdict_reads_both_directions_and_names_a_stale_line(string ceilingLine, string measuredLine, string code)
    {
        var units = MeterUnitCatalog.Default.Units.Select(u => u.Code).ToList();
        var breaches = Breaches(Parse([ceilingLine]), Parse([measuredLine]), units);
        output.WriteLine($"[G9] «{ceilingLine}» × «{measuredLine}» ⇒ {(breaches.Count == 0 ? "أَخضَر" : string.Join(" | ", breaches))}");
        if (code.Length == 0) Assert.Empty(breaches);
        else Assert.StartsWith(code + " ", Assert.Single(breaches), StringComparison.Ordinal);
    }

    [Fact]
    public void The_ceiling_file_parses_and_names_only_units_of_the_lexicon()
    {
        var ceiling = Parse(File.ReadAllLines(CeilingFile));
        var units = MeterUnitCatalog.Default.Units.Select(u => u.Code).ToHashSet(StringComparer.Ordinal);
        output.WriteLine($"[G9] السَقف: سُطور {ceiling.Count} · مَجموعُها {ceiling.Values.Sum()} · وَحَداتٌ لَيسَت في المُعجَم {ceiling.Keys.Count(k => !units.Contains(k.Unit))}");
        Assert.All(ceiling.Keys, k => Assert.Contains(k.Unit, units));
    }

    // ─── الرِحلَةُ الحَيَّة ─────────────────────────────────────────────────────────────────────────────────────────

    private static (string Name, string ConnectionString) FreshDatabase()
    {
        var b = new NpgsqlConnectionStringBuilder(LiveDealConcurrencyProofTests.ConnectionString);
        var stem = (b.Database ?? "wasayel").ToLowerInvariant();
        if (stem.Length > 48) stem = stem[..48];
        var name = $"{stem}_g9_{Guid.NewGuid().ToString("N")[..8]}";
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

    private static string NewPhone() => "+9665" + Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static Dictionary<string, string> Route(params (string Key, string Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public async Task On_the_launch_journey_through_the_real_resolver_unresolved_never_exceeds_its_pinned_ceiling_per_dimension_and_unit()
    {
        if (!Enabled) return;
        var (database, connectionString) = FreshDatabase();
        await AdminAsync($"create database \"{database}\"");
        output.WriteLine($"[G9] قاعِدَةٌ جَديدَة: {database}");
        try
        {
            var slug = await WalkAsync(connectionString);
            NpgsqlConnection.ClearAllPools();

            var perUnit = new Dictionary<string, (long Rows, long Seed)>(StringComparer.Ordinal);
            var measured = new Dictionary<(string Dimension, string Unit), long>();
            await using (var conn = new NpgsqlConnection(connectionString))
            {
                await conn.OpenAsync();
                var columns = string.Join(", ", Dimensions.Select(d => $"count(*) filter (where {d} = '{MeterSentinels.Unresolved}' and not seed)"));
                await using var cmd = new NpgsqlCommand(
                    $"select unit, count(*) filter (where not seed), count(*) filter (where seed), {columns} from "
                    + $"(select *, (role = '{MeterSentinels.Seed}' or entrepreneur = '{MeterSentinels.Seed}') as seed from meter.usage_entry) e group by unit order by unit", conn);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var unit = r.GetString(0);
                    perUnit[unit] = (r.GetInt64(1), r.GetInt64(2));
                    for (var i = 0; i < Dimensions.Count; i++)
                        if (r.GetInt64(3 + i) > 0) measured[(Dimensions[i], unit)] = r.GetInt64(3 + i);
                }
            }

            var units = MeterUnitCatalog.Default.Units.Select(u => u.Code).ToList();
            foreach (var unit in units)
            {
                var (rows, seed) = perUnit.GetValueOrDefault(unit);
                var gaps = Dimensions.Select(d => $"{d} {measured.GetValueOrDefault((d, unit))}");
                output.WriteLine($"[G9] {unit}: سُطور {rows} · _seed (لا تُعَدّ) {seed} · _unresolved: {string.Join(" · ", gaps)}");
            }
            var ceiling = Parse(File.ReadAllLines(CeilingFile));
            var breaches = Breaches(ceiling, measured, units);
            output.WriteLine($"[G9] المَتجَر {slug} · وَحَدات {units.Count} · بَلَغَتها الرِحلَة {units.Count(u => perUnit.GetValueOrDefault(u).Rows > 0)} · سُطور {perUnit.Values.Sum(v => v.Rows)} "
                + $"· _seed {perUnit.Values.Sum(v => v.Seed)} · _unresolved {measured.Values.Sum()} (السَقف {ceiling.Values.Sum()}) · خُروق {breaches.Count}");
            output.WriteLine("[G9] المَقيسُ بِشَكلِ المِلَفّ:\n" + string.Join("\n", measured.OrderBy(m => m.Key.Unit, StringComparer.Ordinal)
                .ThenBy(m => m.Key.Dimension, StringComparer.Ordinal).Select(m => $"{m.Key.Dimension} {m.Key.Unit} {m.Value}")));

            Assert.True(perUnit.Values.Sum(v => v.Rows) > 0, "أَداةٌ عَمياء: صِفرُ سَطرٍ في الدَفتَر — الرِحلَةُ لَم تَكتُب أَو كَتَبَت في غَيرِ القاعِدَةِ الجَديدَة.");
            // ‏(‏2026-11-12، W13-6·١١ — ADR-051 §١٨) **ووَحَداتُ الصادِرِ خارِجَ هذه الرِحلَة**: قَناةُ رُموزِها هُنا مُلتَقِطٌ في العَمَلِيَّةِ (`CaptureSms`) لا يُغادِرُها، فَلا
            // نِداءَ صادِراً يُعَدّ؛ وبابُ الرَمزِ عَبرَ Twilio المُسَجَّلِ يَمشيه `LiveProviderCallsProofTests` (P11) بِسَطرِه ونِيَّتِه. وكُلُّ وَحدَةِ حِفظٍ تَبقى مُلزَمَةً هُنا كَما كانَت.
            var outbound = MeterUnitCatalog.Default.Units.Where(u => u.Boundary == MeterBoundaries.Outbound).Select(u => u.Code).ToHashSet(StringComparer.Ordinal);
            // ‏(‏2026-11-13، W13-6·١٩ — ADR-051 §٢٣؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **ووَحَداتُ الحُضورِ خارِجَ هذه الرِحلَةِ أَيضاً**: لا يَكتُبُها طَلَبٌ بَل لَقطَةُ اليَوم
            // (`TakePresenceSnapshot`)، والمُضيفُ هُنا بِلا إقلاعِ تَطبيقٍ ولا مُجَدوِل؛ ويَمشيها `LivePresenceSnapshotProofTests` (G12) بِوَحَداتِها التِسعِ وأَرقامِها، ولا
            // `_unresolved` في سُطورِها (دَورُها `_system` أَو الدَورُ المَعدود). وكُلُّ وَحدَةِ حِفظٍ تَبقى مُلزَمَة.
            var presence = MeterUnitCatalog.Default.Units.Where(u => u.Boundary == MeterBoundaries.Presence).Select(u => u.Code).ToHashSet(StringComparer.Ordinal);
            Assert.All(units, u => Assert.True(perUnit.GetValueOrDefault(u).Rows > 0 || u == "member_plan.subscribe" || outbound.Contains(u) || presence.Contains(u),
                $"أَداةٌ عَمياء: الرِحلَةُ لَم تَبلُغ {u}."));
            Assert.True(breaches.Count == 0, "سَقفُ _unresolved:\n" + string.Join("\n", breaches));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await AdminAsync($"drop database if exists \"{database}\" with (force)");
            output.WriteLine($"[G9] حُذِفَت {database}");
        }
    }

    /// <summary>رِحلَةُ الإطلاقِ بِأَبوابِها المُسَجَّلَةِ عَبرَ الوَسيطِ الحَقيقِيّ — خُطُواتُ <c>LiveMeterJourneyFootprintCharacterizationProofTests</c> نَفسُها
    /// (ذلكَ المِلَفُّ تَوصيفٌ لا يُمَسّ، وخُطُواتُه خاصَّة؛ فَتُكتَبُ هُنا ولا تُستَخرَجُ مِنه): المالِكُ ومَتجَرُه مِن الاستوديو، وعُضوانِ بِرَمزِ الجَوّال، والبائِعُ تاجِرٌ
    /// يَنشُر، والمُشتَري عَميلٌ يَعرِضُ ويَطلُبُ ويُقَيِّمُ ويُراسِلُ ويَفتَحُ تَذكِرَة. وكُلُّ جَوابٍ مُثبَت.</summary>
    private async Task<string> WalkAsync(string connectionString)
    {
        var sms = new LiveContactNoticeProofTests.CaptureSms();
        await using var host = LiveTemplateDoorHost.Build(
            marten: o => o.Connection(connectionString),
            services: s => s.AddSingleton<IOtpChannel>(sms),
            throughResolver: true);

        var ownerPhone = NewPhone();
        var step = await host.PostStudioFormAsync("/studio/auth/login", Route(), null, ("phone", ownerPhone));
        Assert.Equal(302, step.Status);
        step = await host.PostStudioFormAsync("/studio/auth/verify", Route(), null, ("method", "phone"), ("phone", ownerPhone), ("code", sms.Code!));
        Assert.Equal((302, StudioStoreCreate.Page), step);
        StudioUser owner;
        await using (var q = host.Store.QuerySession(StudioAuth.Tenant))
            owner = Assert.Single(await q.Query<StudioUser>().Where(u => u.Phone == PhoneNumber.Canonical(ownerPhone)).ToListAsync());
        var studioToken = AuthHandlers.MakeToken(owner.Id, StudioAuth.Tenant, owner.SessionVersion);
        Assert.Equal((302, StudioStoreCreate.Page),
            await host.PostStudioFormAsync("/studio/consent/accept", Route(), studioToken, ("returnUrl", StudioStoreCreate.Page)));
        var slug = "w13g9-" + Guid.NewGuid().ToString("N")[..8];
        Assert.Equal((302, $"/studio/apps/{slug}?built=1"), await host.PostStudioFormAsync(StudioStoreCreate.Endpoint, Route(), studioToken,
            ("slug", slug), ("name", "مَتجَرُ السَقف"), ("color", "#0F766E"), ("tagline", "رِحلَةُ السَقف")));
        await using (var q = host.Store.QuerySession())
            output.WriteLine($"[G9·رِحلَة] مَتجَر {slug} · أَدوار {(await q.LoadAsync<Tenant>(slug))!.Roles.Count}");

        var store = Route(("slug", slug));
        async Task<string> PostAsync(string pattern, IReadOnlyDictionary<string, string> route, string? token, params (string, string)[] form)
        {
            var (status, location) = await host.PostFormAsync(pattern, route, slug, token, form);
            output.WriteLine($"[G9·رِحلَة] POST {pattern} ⇒ {status} {location}");
            Assert.Equal(302, status);
            Assert.DoesNotContain("err=", location, StringComparison.Ordinal);
            return location;
        }
        async Task<string> JoinAsync(string phone)
        {
            await PostAsync("/{slug}/auth/phone/login", store, null, ("phone", phone));
            await PostAsync("/{slug}/auth/phone/verify", store, null, ("phone", phone), ("code", sms.Code!));
            await using var q = host.Store.QuerySession(slug);
            var member = Assert.Single(await q.Query<User>().Where(u => u.Phone == PhoneNumber.Canonical(phone)).ToListAsync());
            return AuthHandlers.MakeToken(member.Id, slug, member.SessionVersion);
        }
        var seller = await JoinAsync(NewPhone());
        var buyer = await JoinAsync(NewPhone());

        await PostAsync("/{slug}/me/role/save", store, seller, ("role", "vendor"));
        await PostAsync("/{slug}/me/role/onboarding/save", store, seller, ("role_vendor_attr_business_name", "نَشاطُ السَقف"));
        await PostAsync("/{slug}/terms/accept", store, seller);
        await PostAsync("/{slug}/me/role/save", store, buyer, ("role", "customer"));
        await PostAsync("/{slug}/terms/accept", store, buyer);

        var at = await PostAsync("/{slug}/listings/create", store, seller,
            ("title", "سِلعَةُ السَقف"), ("category", "general"), ("price", "120"), ("description", "إعلانُ رِحلَةِ السَقف"));
        var listingId = Guid.Parse(at[(at.LastIndexOf('/') + 1)..]);
        var listing = Route(("slug", slug), ("id", listingId.ToString()));
        await PostAsync("/{slug}/listings/{id:guid}/offers", listing, buyer,
            ("price", "110"), ("message", "عَرضُ السَقف"), ("lat", "24.7136"), ("lng", "46.6753"), ("ttl_minutes", "15"));

        at = await PostAsync("/{slug}/listings/{id:guid}/deal", listing, buyer, ("note", "طَلَبُ السَقف"));
        var dealId = Guid.Parse(at[(at.LastIndexOf('/') + 1)..]);
        var deal = Route(("slug", slug), ("id", dealId.ToString()));
        await PostAsync("/{slug}/deals/{id:guid}/accept", deal, seller);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var deals = scope.ServiceProvider.GetRequiredService<DealsService>();
            for (var i = 0; i < 8 && (await deals.LoadAsync(slug, dealId))!.Status == DealStatus.Active; i++)
            {
                var d = (await deals.LoadAsync(slug, dealId))!;
                await PostAsync("/{slug}/deals/{id:guid}/advance", deal, DealsPolicy.RequiredActorToLeave(d) == "initiator" ? buyer : seller,
                    ("from", d.Stage.ToString()));
            }
            Assert.Equal(DealStatus.Completed, (await deals.LoadAsync(slug, dealId))!.Status);
        }
        await PostAsync("/{slug}/deals/{id:guid}/review", deal, buyer, ("rating", "5"), ("body", "تَقييمُ السَقف"));

        at = await PostAsync("/{slug}/listings/{id:guid}/chat", listing, buyer);
        var conversationId = Guid.Parse(at[(at.LastIndexOf('/') + 1)..]);
        await PostAsync("/{slug}/chats/{conversationId:guid}/send", Route(("slug", slug), ("conversationId", conversationId.ToString())), buyer,
            ("body", "رِسالَةُ السَقف"));
        await PostAsync("/{slug}/support/open", store, buyer, ("subject", "تَذكِرَةُ السَقف"), ("body", "سُؤالُ رِحلَةِ السَقف"));
        return slug;
    }
}
