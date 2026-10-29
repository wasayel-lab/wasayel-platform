using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Cart;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Favorites;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using JasperFx.Events.Projections;
using Marten;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ د57 (٣) «أ»: الحِساباتُ المُكَرَّرَةُ بِصيغَتَي الجَوّال — الخُطَّة، والقِياس، والدَمج، والسكربتان (‏2026-10-29؛ ‏ADR-044 §٤) ═══
//
// قَرارُ المالِك: «الخُطوَةُ الأولى عَدٌّ لِلقِراءَةِ فَقَط… ثُمَّ ترحيلٌ يَكتُبُ كُلَّ رَقَمٍ بِالصيغَةِ الدَولِيَّةِ المُوَحَّدَة، ويَجمَعُ إعلاناتِ الشَخصِ وصَفَقاتِه
// في حِسابٍ واحِد». فَهُنا: الخُطَّةُ النَقِيَّةُ مُوجَبَةً وسالِبَة، والسكربتانِ بِحَدَّيهِما (العَدُّ لا يَكتُب، والدَمجُ لا يَكتُبُ بِلا --apply والعَدَدِ المُطابِق)،
// والدَمجُ حَيّاً عَلى Postgres — بِما يُعادُ تَوجيهُه وبِمانِعٍ يَحمي المَجموعَةَ كُلَّها.
public class DuplicatePhonesTests(ITestOutputHelper output)
{
    private static readonly DateTime T0 = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static DuplicatePhones.Account A(string phone, int day, bool deleted = false, Guid? id = null)
        => new(id ?? Guid.NewGuid(), phone, T0.AddDays(day), deleted);

    /// <summary>المُفرَدُ: يُعادُ كَتبُه إن لَم يَكُن مُوَحَّداً (مُوجَب)، ولا يُمَسُّ مُوَحَّدٌ ولا خارِجَ السُعودِيَّةِ ولا <c>NID-…</c> ولا فارِغٌ ولا مَحذوف (سالِب).</summary>
    [Fact]
    public void A_single_account_is_renamed_only_when_its_phone_is_not_canonical()
    {
        var local = A("0500000081", 1);
        var plan = DuplicatePhones.Build(new[]
        {
            local, A("+966500000082", 2), A("+12025550123", 3), A("NID-1052001001", 4), A("", 5), A("0500000083", 6, deleted: true),
        });
        output.WriteLine($"مَنظور: {plan.Considered} · يُعادُ كَتبُه: {plan.Renames.Count} · مَجموعات: {plan.Groups.Count}");
        Assert.Equal(3, plan.Considered);
        var rename = Assert.Single(plan.Renames);
        Assert.Equal(new DuplicatePhones.Rename(local.Id, "+966500000081"), rename);
        Assert.Empty(plan.Groups);
    }

    /// <summary>المَجموعَة: كِتاباتُ الرَقَمِ الواحِد — يَبقى أَقدَمُها، ويَندَمِجُ الباقونَ بِتَرتيبِ قِدَمِهِم؛ والتَعادُلُ في الوَقتِ يَحسِمُه المُعَرِّف؛ ونَصّانِ مُتَطابِقانِ
    /// خارِجَ السُعودِيَّةِ مَجموعَةٌ أَيضاً؛ ورَقَمانِ مُختَلِفانِ لا يَجتَمِعان (سالِب).</summary>
    [Fact]
    public void One_number_under_many_spellings_is_one_group_whose_oldest_account_stays()
    {
        var oldest = A("+966 50 000 0084", 1);
        var mid = A("0500000084", 2);
        var newest = A("966500000084", 3);
        var tieA = A("0500000085", 1, id: Guid.Parse("00000000-0000-0000-0000-00000000000a"));
        var tieB = A("+966500000085", 1, id: Guid.Parse("00000000-0000-0000-0000-00000000000b"));
        var foreignA = A("+12025550123", 2);
        var foreignB = A("+12025550123", 1);
        var other = A("0500000086", 1);
        var plan = DuplicatePhones.Build(new[] { newest, other, mid, tieB, oldest, foreignA, tieA, foreignB });
        output.WriteLine($"مَجموعات: {plan.Groups.Count} · {string.Join(" · ", plan.Groups.Select(g => $"{g.Losers.Count + 1} حِسابات"))}");

        Assert.Equal(3, plan.Groups.Count);
        var g84 = plan.Groups.Single(g => g.Canonical == "+966500000084");
        Assert.Equal(oldest.Id, g84.Survivor);
        Assert.Equal(new[] { mid.Id, newest.Id }, g84.Losers);
        var g85 = plan.Groups.Single(g => g.Canonical == "+966500000085");
        Assert.Equal((tieA.Id, tieB.Id), (g85.Survivor, Assert.Single(g85.Losers)));
        var foreign = plan.Groups.Single(g => g.Canonical == "+12025550123");
        Assert.Equal((foreignB.Id, foreignA.Id), (foreign.Survivor, Assert.Single(foreign.Losers)));
        Assert.Equal(new DuplicatePhones.Rename(other.Id, "+966500000086"), Assert.Single(plan.Renames));
    }

    // ─── السكربتان ─────────────────────────────────────────────────────────────────────────

    private static string Code(string script) => string.Join('\n', StudioConsentCharacterizationTests.Source("scripts", script).Split('\n')
        .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && !l.StartsWith("#", StringComparison.Ordinal)));

    /// <summary><b>العَدُّ لا يَكتُبُ حَرفاً</b>: لا بابَ كِتابَةٍ يَعرِفُه Marten وNpgsql، والاتِّصالُ بِـ<c>default_transaction_read_only=on</c> يُقرَأُ مِن
    /// الخادِمِ ويَخرُجُ بِـ1 إن لَم يَكُن <c>on</c>، ويَخرُجُ بِـ1 عَلى صِفرِ مَتجَر، ويَقيسُ بِدالَّتَي الدَمجِ نَفسَيهِما.</summary>
    [Fact]
    public void The_count_script_writes_nothing_reads_through_a_read_only_connection_and_fails_on_zero()
    {
        var code = Code("count-duplicate-phones.cs");
        var writes = new[]
        {
            "LightweightSession", "IdentitySession", "DirtyTrackedSession", "OpenSession", "SaveChanges", ".Store(", ".Insert(", ".Update(", ".Delete(",
            "HardDelete", ".Patch<", "Events.Append", "StartStream", "ExecuteNonQuery", "ApplyAsync", "RenameAsync", "AutoCreate.All", "AuditWriter",
        };
        var found = writes.Where(w => code.Contains(w, StringComparison.Ordinal)).ToList();
        output.WriteLine($"أَبوابُ كِتابَةٍ مَفحوصَة: {writes.Length} · مَوجودَة: {found.Count}");
        Assert.Empty(found);
        Assert.Contains("const string ReadOnlyOption = \"-c default_transaction_read_only=on\";", code, StringComparison.Ordinal);
        Assert.Contains("if (readOnly != \"on\")", code, StringComparison.Ordinal);
        Assert.Contains("o.Connection(readOnlyCs);", code, StringComparison.Ordinal);
        Assert.Contains("o.AutoCreateSchemaObjects = AutoCreate.None;", code, StringComparison.Ordinal);
        Assert.Contains("DuplicatePhones.Build(members)", code, StringComparison.Ordinal);
        Assert.Contains("DuplicatePhones.ScanAsync(conn, slug, g, tables)", code, StringComparison.Ordinal);
        var blind = Regex.Match(code, @"if \(tenants\.Count == 0\)\s*\{(?<body>[^}]*)\}");
        Assert.True(blind.Success, "حارِسُ العَمى غائِب.");
        Assert.Contains("return 1;", blind.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("فُحِصَ: {tenants.Count} مَتجَراً والاستوديو", code, StringComparison.Ordinal);
    }

    /// <summary><b>الدَمجُ عَرضٌ افتِراضاً</b>: لا كِتابَةَ قَبلَ <c>--apply</c> وعَدَدٍ يُطابِقُ ما طَبَعَه العَرض؛ والعَرضُ يَخرُجُ بِـ2 حينَ يَجِدُ تَغييراً،
    /// والمُطابَقَةُ قَبلَ أَوَّلِ كِتابَة، وكُلُّ مَجموعَةٍ تُقاسُ مِن جَديدٍ قَبلَ دَمجِها، ولا نُسخَةَ بِبَياناتٍ شَخصِيَّةٍ تُكتَبُ عَلى القُرص.</summary>
    [Fact]
    public void The_merge_script_writes_only_after_apply_and_a_matching_confirm_and_rescans_each_group()
    {
        var code = Code("merge-duplicate-phones.cs");
        int At(string s) { var i = code.IndexOf(s, StringComparison.Ordinal); Assert.True(i >= 0, $"لا «{s}» — الأَداةُ عَمياء."); return i; }
        var dryRun = At("if (!apply)");
        var dryRunExit = At("return 2;");
        var confirmGate = At("if (confirm != changes)");
        var firstWrite = new[] { At("DuplicatePhones.RenameAsync<User>("), At("DuplicatePhones.ApplyAsync(store, slug, scan, now)"), At("audit.WriteAsync(") }.Min();
        var rescan = code.IndexOf("DuplicatePhones.ScanAsync(conn, slug, before.Group, tables)", StringComparison.Ordinal);
        output.WriteLine($"العَرض@{dryRun} خُروجُه@{dryRunExit} المُطابَقَة@{confirmGate} أَوَّلُ كِتابَة@{firstWrite} إعادَةُ القِياس@{rescan}");

        Assert.True(dryRun < dryRunExit && dryRunExit < confirmGate && confirmGate < firstWrite);
        Assert.True(rescan > confirmGate && rescan < At("DuplicatePhones.ApplyAsync(store, slug, scan, now)"));
        Assert.DoesNotContain("File.Write", code, StringComparison.Ordinal);
        // ‏مَخزَنُ الدَمجِ في مِلَفِّه (لا في libs/ — ‏MartenSchemaVerbTests)، ويُعيدُ مِن تَركيبِ المُضيفِ ما يَمَسُّه: أَحداثٌ مُقتَرِنَة، ونُسخَةُ الإعلانِ في المُعامَلَة.
        foreach (var line in StoreLines)
            Assert.Contains(line, code, StringComparison.Ordinal);
        Assert.Contains("o.AutoCreateSchemaObjects = AutoCreate.None;", code, StringComparison.Ordinal);
        var host = StudioConsentCharacterizationTests.Source("libs", "core", "ACommerce.Platform.Hosting", "HostingExtensions.cs");
        Assert.Contains("opts.Projections.Snapshot<ACommerce.Kit.Listings.Listing>(SnapshotLifecycle.Inline);", host, StringComparison.Ordinal);
        Assert.Contains("TenancyStyle.Conjoined", host, StringComparison.Ordinal);
        Assert.DoesNotContain("DocumentStore.For(", StudioConsentCharacterizationTests.Source(
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Identity", "DuplicatePhones.cs"), StringComparison.Ordinal);

        // ‏والصَفُّ الحَيُّ يُرَكِّبُ مَخزَنَه بِالسُطورِ نَفسِها — مِن جِسمِ دالَّتِه لا مِن هذه المَصفوفَة.
        var live = Regex.Match(StudioConsentCharacterizationTests.Source("tests", "ACommerce.Platform.Tests", "DuplicatePhonesTests.cs"),
            @"private static DocumentStore Store\(\) => DocumentStore\.For\(o =>(?<body>.*?)\}\);", RegexOptions.Singleline);
        Assert.True(live.Success, "مَخزَنُ الصَفِّ الحَيِّ غائِب — الأَداةُ عَمياء.");
        output.WriteLine($"سُطورُ المَخزَنِ المُثَبَّتَة: {StoreLines.Length} · في السكربت وفي الصَفِّ الحَيّ");
        foreach (var line in StoreLines)
            Assert.Contains(line, live.Groups["body"].Value, StringComparison.Ordinal);
    }

    /// <summary>سُطورُ مَخزَنِ الدَمجِ الَّتي يُعيدُها الصَفُّ الحَيُّ بِحَرفِها — فَما يُقاسُ هُوَ ما يُشَغَّل.</summary>
    internal static readonly string[] StoreLines =
    {
        "o.DatabaseSchemaName = \"platform\";",
        "o.Policies.AllDocumentsAreMultiTenanted();",
        "o.Events.TenancyStyle = JasperFx.MultiTenancy.TenancyStyle.Conjoined;",
        "o.Schema.For<Tenant>().SingleTenanted().Identity(x => x.Id);",
        "o.Projections.Snapshot<Listing>(SnapshotLifecycle.Inline);",
    };
}

[Collection(LiveProofCollection.Name)]
public class LiveDuplicatePhonesProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>مَخزَنُ <c>merge-duplicate-phones.cs</c> بِسُطورِه (<see cref="DuplicatePhonesTests.StoreLines"/> مُثَبَّتَةٌ في السكربت)، ويُنشِئُ
    /// المُخَطَّطَ عَلى القاعِدَةِ المَحَلِّيَّةِ وَحدَها (‏<c>AutoCreate.All</c> — والسكربتُ <c>None</c>).</summary>
    private static DocumentStore Store() => DocumentStore.For(o =>
    {
        o.Connection(LiveDealConcurrencyProofTests.ConnectionString!);
        o.DatabaseSchemaName = "platform";
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Events.TenancyStyle = JasperFx.MultiTenancy.TenancyStyle.Conjoined;
        o.Schema.For<Tenant>().SingleTenanted().Identity(x => x.Id);
        o.Projections.Snapshot<Listing>(SnapshotLifecycle.Inline);
        o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
    });

    private static async Task CleanAsync(string slug)
    {
        await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
        await conn.OpenAsync();
        foreach (var t in await DuplicatePhones.TenantTablesAsync(conn))
        {
            await using var d = new NpgsqlCommand($"delete from platform.{t.Name} where tenant_id = @t", conn);
            d.Parameters.AddWithValue("t", slug);
            await d.ExecuteNonQueryAsync();
        }
        foreach (var t in new[] { "mt_events", "mt_streams" })
        {
            await using var d = new NpgsqlCommand($"delete from platform.{t} where tenant_id = @t", conn);
            d.Parameters.AddWithValue("t", slug);
            await d.ExecuteNonQueryAsync();
        }
    }

    private static async Task<Guid> ListingAsync(IDocumentStore store, string slug, Guid owner)
    {
        var id = Guid.NewGuid();
        await using var s = store.LightweightSession(slug);
        s.Events.StartStream<Listing>(id, new ListingCreated(id, slug, "إعلان", null, 10m, "general", null, null,
            new Dictionary<string, string> { [ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingEditService.OwnerAttribute] = owner.ToString() },
            DateTime.UtcNow));
        await s.SaveChangesAsync();
        return id;
    }

    private sealed record PhoneMergeProbeEvent(Guid UserId);

    /// <summary><b>الدَمجُ حَيّاً عَلى Postgres</b>: أَ (أَقدَمُ، <c>0500000091</c>) وب (<c>+966500000091</c>) شَخصٌ واحِد — لِبٍ إعلانٌ وصَفقَةٌ مَعَ جـ
    /// ومُحادَثَةٌ ورِسالَةٌ وإشعارٌ ومُفَضَّلَةٌ وسَلَّة. بَعدَ القِياسِ والدَمج: كُلُّها لِأَ، وبٌ مَمحُوٌّ بِإصدارٍ مَرفوع، وجَوّالُ أَ مُوَحَّد، والدُخولُ بِالكِتابَتَينِ
    /// يَفتَحُ أَ. <b>ومانِعٌ يَحمي المَجموعَةَ كُلَّها</b>: د وهـ بَينَهُما صَفقَة، وهـ في مَجرى أَحداثٍ غَيرِ الإعلان ⇒ يُمتَنَعُ ولا يُكتَبُ حَرف.</summary>
    [Fact]
    public async Task A_measured_group_merges_listings_deals_chats_favorites_and_cart_into_the_oldest_account_and_a_blocked_group_is_untouched_on_Postgres()
    {
        if (!Enabled) return;
        using var store = Store();
        await using var dealStore = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "dup-ph-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var (a, b, c, d, e) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(new User { Id = a, TenantSlug = slug, Phone = "0500000091", CreatedAt = DateTime.UtcNow.AddDays(-10), PhoneVerified = false });
                s.Store(new User { Id = b, TenantSlug = slug, Phone = "+966500000091", CreatedAt = DateTime.UtcNow.AddDays(-2), PhoneVerified = true, Email = "b@example.com", EmailVerified = true });
                s.Store(new User { Id = c, TenantSlug = slug, Phone = "0500000092", CreatedAt = DateTime.UtcNow.AddDays(-5) });
                s.Store(new User { Id = d, TenantSlug = slug, Phone = "0500000093", CreatedAt = DateTime.UtcNow.AddDays(-9) });
                s.Store(new User { Id = e, TenantSlug = slug, Phone = "+966500000093", CreatedAt = DateTime.UtcNow.AddDays(-1) });
                var conv = Guid.NewGuid();
                s.Store(new Conversation { Id = conv, OwnerId = c, PartnerId = b, OwnerName = "ج", PartnerName = "ب" });
                s.Store(new Message { Id = Guid.NewGuid(), ConversationId = conv, SenderId = b, Body = "مَرحَباً" });
                s.Store(new Notification { Id = Guid.NewGuid(), UserId = b, Title = "إشعار", Body = "—" });
                s.Store(new Cart { Id = b, Items = [new CartItem { ListingId = Guid.NewGuid(), Title = "بَند", UnitPriceSar = 5m, Quantity = 2 }] });
                s.Events.StartStream(Guid.NewGuid(), new PhoneMergeProbeEvent(e));
                await s.SaveChangesAsync();
            }
            var listingA = await ListingAsync(store, slug, a);
            var listingB = await ListingAsync(store, slug, b);
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(new Favorite { Id = Favorite.MakeId(b, listingA), UserId = b, ListingId = listingA });
                await s.SaveChangesAsync();
            }
            var dealBC = Guid.NewGuid();
            var dealDE = Guid.NewGuid();
            await using (var s = dealStore.LightweightSession(slug))
            {
                s.Store(new Deal { Id = dealBC, TenantSlug = slug, InitiatorId = b, CounterpartyId = c, ListingId = listingB });
                s.Store(new Deal { Id = dealDE, TenantSlug = slug, InitiatorId = d, CounterpartyId = e });
                await s.SaveChangesAsync();
            }

            List<DuplicatePhones.Account> members;
            await using (var q = store.QuerySession(slug))
                members = (await q.Query<User>().ToListAsync()).Select(u => new DuplicatePhones.Account(u.Id, u.Phone, u.CreatedAt, u.DeletedAt is not null)).ToList();
            var plan = DuplicatePhones.Build(members);
            await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
            await conn.OpenAsync();
            var tables = await DuplicatePhones.TenantTablesAsync(conn);
            var gab = plan.Groups.Single(g => g.Survivor == a);
            var gde = plan.Groups.Single(g => g.Survivor == d);
            var scanAB = await DuplicatePhones.ScanAsync(conn, slug, gab, tables);
            var scanDE = await DuplicatePhones.ScanAsync(conn, slug, gde, tables);
            output.WriteLine($"جَداوِل: {tables.Count} · مَجموعات: {plan.Groups.Count} · يُعادُ كَتبُه: {plan.Renames.Count} · أ←ب: يُعادُ {scanAB.Repoints.Sum(r => r.Rows)} صَفّاً في "
                + $"{string.Join(",", scanAB.Repoints.Select(r => r.Table))} · إعلانات {scanAB.Listings.Count} · سِلال {scanAB.LoserCarts} · مُفَضَّلات {scanAB.Favorites} · "
                + $"مَوانِع {scanAB.Blocks.Count} · د←هـ: مَوانِع {string.Join(" | ", scanDE.Blocks)}");

            Assert.Equal(2, plan.Groups.Count);
            Assert.Single(plan.Renames);   // جـ (0500000092)
            Assert.True(scanAB.Mergeable, string.Join(" · ", scanAB.Blocks));
            Assert.False(scanDE.Mergeable);
            Assert.Contains(scanDE.Blocks, x => x.StartsWith("mt_doc_deal:", StringComparison.Ordinal) && x.Contains("يَجمَعُ الحِسابَين", StringComparison.Ordinal));
            Assert.Contains(scanDE.Blocks, x => x.StartsWith("mt_events:", StringComparison.Ordinal));
            await Assert.ThrowsAsync<InvalidOperationException>(() => DuplicatePhones.ApplyAsync(store, slug, scanDE, DateTime.UtcNow));

            var moved = await DuplicatePhones.ApplyAsync(store, slug, scanAB, DateTime.UtcNow);

            await using var r = store.QuerySession(slug);
            var ua = await r.LoadAsync<User>(a);
            var ub = await r.LoadAsync<User>(b);
            var ue = await r.LoadAsync<User>(e);
            var deal = await r.Query<Deal>().SingleAsync(x => x.Id == dealBC);
            var listing = await r.Events.AggregateStreamAsync<Listing>(listingB);
            var snapshot = await r.LoadAsync<Listing>(listingB);
            var conv2 = await r.Query<Conversation>().SingleAsync();
            var msg = await r.Query<Message>().SingleAsync();
            var note = await r.Query<Notification>().SingleAsync();
            var fav = await r.Query<Favorite>().ToListAsync();
            var cartA = await r.LoadAsync<Cart>(a);
            var cartB = await r.LoadAsync<Cart>(b);
            var signTyped = (await LivePhoneIdentityProofTests.SignInAsync(store, slug, "0500000091")).Id;
            var signIntl = (await LivePhoneIdentityProofTests.SignInAsync(store, slug, "+966500000091")).Id;
            var survivorCanonical = ua!.Phone == "+966500000091";
            var survivorInherited = ua.PhoneVerified && ua.Email == "b@example.com" && ua.EmailVerified;
            var blockedUntouched = ue!.Phone == "+966500000093" && ue.DeletedAt is null;
            output.WriteLine($"نُقِلَ: {moved} · أَ مُوَحَّد: {survivorCanonical} · وَرِثَ التَحَقُّقَ والبَريد: {survivorInherited} · بٌ مَمحُوّ: {ub!.DeletedAt is not null} "
                + $"(إصدار {ub.SessionVersion}) · المُمتَنِعَةُ كَما هِي: {blockedUntouched} · الدُخولُ بِالكِتابَتَين ⇒ أَ: {signTyped == a && signIntl == a}");

            Assert.True(survivorCanonical);
            Assert.True(survivorInherited);
            Assert.NotNull(ub.DeletedAt);
            Assert.Equal("", ub.Phone);
            Assert.True(ub.SessionVersion > 0);
            Assert.Equal((a, (Guid?)c), (deal.InitiatorId, deal.CounterpartyId));
            Assert.Equal(a.ToString(), listing!.Attributes[ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingEditService.OwnerAttribute]);
            Assert.Equal(a.ToString(), snapshot!.Attributes[ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingEditService.OwnerAttribute]);
            Assert.Equal((c, a), (conv2.OwnerId, conv2.PartnerId));
            Assert.Equal(a, msg.SenderId);
            Assert.Equal(a, note.UserId);
            var f = Assert.Single(fav);
            Assert.Equal((Favorite.MakeId(a, listingA), a), (f.Id, f.UserId));
            Assert.NotNull(cartA);
            Assert.Null(cartB);
            Assert.Equal(2, cartA!.TotalQuantity);
            Assert.Equal(a, signTyped);
            Assert.Equal(a, signIntl);
            Assert.True(blockedUntouched);
        }
        finally { await CleanAsync(slug); }
    }

    /// <summary><b>تَوصيفُ «اليَوم» قَبلَ نَقلِ مَفاتيحِ الرَبطِ في الدَمج</b> (القاعِدَة ٣؛ مُراجَعَةُ <c>9f00eac</c>): مِفتاحُ الرَبطِ وَثيقَةٌ عامَّة
    /// (‏<c>SingleTenanted</c>، لا عَمودَ <c>tenant_id</c>) فَلا يَراه مَسحُ <see cref="DuplicatePhones.TenantTablesAsync"/>، ولا يَلمِسُه
    /// <see cref="DuplicatePhones.ApplyAsync"/>. فَمِفتاحٌ صَدَرَ بِاسمِ بٍ (المُندَمِج) يُصادِقُ قَبلَ الدَمجِ بِاسمِ ب، وبَعدَه يُرَدُّ <c>ActorGone</c>
    /// (‏بٌ مَمحُوّ) ووَثيقَتُه «فَعّال» بِلا تاريخِ إبطال — مِفتاحٌ مَيِّتٌ يَبدو حَيّاً في شاشَةِ المَفاتيح.</summary>
    [Fact]
    public async Task Today_a_key_issued_to_the_merged_away_account_still_reads_active_and_is_refused_actor_gone_after_the_merge_on_Postgres()
    {
        if (!Enabled) return;
        using var store = Store();
        await using var keyStore = LiveApiKeyActorDeletionProofTests.BuildStore();
        var slug = "dup-ak-" + Guid.NewGuid().ToString("N")[..8];
        var issued = new List<string>();
        try
        {
            var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(new User { Id = a, TenantSlug = slug, Phone = "0500000094", CreatedAt = DateTime.UtcNow.AddDays(-10) });
                s.Store(new User { Id = b, TenantSlug = slug, Phone = "+966500000094", CreatedAt = DateTime.UtcNow.AddDays(-2) });
                await s.SaveChangesAsync();
            }
            var keys = new ApiKeyService(keyStore);
            var key = await keys.IssueAsync(slug, new ApiKeyValidator.IssueRequest("رَبطُ الناقِل", b, "سائِق", new[] { ApiScopeCatalog.DealsRead }, null), Guid.NewGuid());
            issued.Add(key.Key.Id);
            var before = await keys.AuthenticateAsync(key.Presented);

            List<DuplicatePhones.Account> members;
            await using (var q = store.QuerySession(slug))
                members = (await q.Query<User>().ToListAsync()).Select(u => new DuplicatePhones.Account(u.Id, u.Phone, u.CreatedAt, u.DeletedAt is not null)).ToList();
            var group = Assert.Single(DuplicatePhones.Build(members).Groups);
            await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
            await conn.OpenAsync();
            var tables = await DuplicatePhones.TenantTablesAsync(conn);
            var scan = await DuplicatePhones.ScanAsync(conn, slug, group, tables);
            var moved = await DuplicatePhones.ApplyAsync(store, slug, scan, DateTime.UtcNow);

            var after = await keys.AuthenticateAsync(key.Presented);
            ApiKeyDocument? doc;
            await using (var q = keyStore.QuerySession()) doc = await q.LoadAsync<ApiKeyDocument>(key.Key.Id);
            output.WriteLine($"جَدوَلُ المَفاتيحِ في المَسح: {tables.Any(t => t.Name == "mt_doc_apikeydocument")} · قَبلَ الدَمج: {before.Rejection} (الفاعِلُ ب: {before.Principal?.ActorUserId == b}) · "
                + $"نُقِلَ: {moved} · بَعدَه: {after.Rejection} · الوَثيقَة: {doc?.Status}/{(doc?.RevokedAt is null ? "بِلا تاريخِ إبطال" : "مُؤَرَّخ")} · فاعِلُها ب: {doc?.ActorUserId == b}");

            Assert.DoesNotContain(tables, t => t.Name == "mt_doc_apikeydocument");
            Assert.Equal(ApiKeyRejection.None, before.Rejection);
            Assert.Equal(b, before.Principal!.ActorUserId);
            Assert.True(scan.Mergeable);
            Assert.Equal(ApiKeyRejection.ActorGone, after.Rejection);
            Assert.Equal((ApiKeyDocument.StatusActive, (DateTime?)null, b), (doc!.Status, doc.RevokedAt, doc.ActorUserId));
        }
        finally
        {
            await using (var g = keyStore.LightweightSession())
            {
                foreach (var id in issued) g.Delete<ApiKeyDocument>(id);
                await g.SaveChangesAsync();
            }
            await CleanAsync(slug);
        }
    }
}
