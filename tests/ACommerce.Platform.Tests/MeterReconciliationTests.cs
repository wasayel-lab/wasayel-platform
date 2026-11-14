using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Offers;
using ACommerce.Kit.Reviews;
using ACommerce.Kit.Subscriptions;
using ACommerce.Kit.Support;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G13 (نَقِيّ) — المُطابَقَةُ بِالاتِّجاهَين، والنِيّات، والمَجاميع، والحُضور، وقُرّاءُ المَصادِرِ يُحاكونَ المُصَنِّف (‏2026-11-13، الموجَة ١٣ W13-6·٢٠ — ADR-051 §٢٤؛ التَصميم §9) ═══
//
// **لِماذا نَقِيّ قَبلَ الحَيّ**: الحُكمُ (`MeterReconciliation.Compare*`) دَوالُّ عَلى ما قُرِئ، فَكُلُّ حالَةٍ تُقاسُ بِمُدخَلٍ مَكتوبٍ هُنا — ناقِصٌ ويَتيمٌ ومَتجَرُ صِفرٍ
// وما قَبلَ النافِذَةِ والمُستَثنى والبَذر؛ والبُرهانُ الحَيُّ (`LiveMeterReconciliationProofTests`) يَقيسُ القِراءَةَ والحَقنَ عَلى قاعِدَةٍ حَقيقِيَّة.
// **والقارِئُ يُحاكي المُصَنِّفَ ولا يُخَمِّنُه**: لِكُلِّ وَحدَةِ حِفظٍ مُعَرِّفُ السَطرِ المُنتَظَرُ مِن `MeterSourceReaders.SourceOf` = ما يَكتُبُه `MeterRules.Classify` لِلمَصدَرِ نَفسِه.
public class MeterReconciliationTests(ITestOutputHelper output)
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>(StringComparer.Ordinal);

    private static MeterSource Src(string app, string id, bool inWindow = true) => new(app, id, inWindow);
    private static MeterLedgerRow Row(string unit, string app, string id, string role = "_none") => new(id, unit, app, role);

    // ─── الاتِّجاهان ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Compare_both_ways_a_source_without_its_entry_is_missing_and_an_entry_without_its_source_is_an_orphan()
    {
        var sources = new[] { Src("a", "deal.create:a:1"), Src("a", "deal.create:a:2"), Src("b", "deal.create:b:3") };
        var entries = new[] { Row("deal.create", "a", "deal.create:a:1"), Row("deal.create", "b", "deal.create:b:3"), Row("deal.create", "a", "deal.create:a:9") };
        var c = MeterReconciliation.Compare("deal.create", "deal_ids", true, sources, entries, None);
        output.WriteLine($"[G13·pure] {c}");
        Assert.Equal((3, 1, 1, 0), (c.Compared, c.Missing, c.Orphans, c.ZeroEntryStores.Count));
        Assert.Equal(["deal.create:a:2"], c.MissingIds);
        Assert.Equal(["deal.create:a:9"], c.OrphanIds);
        Assert.Equal(2, c.Defects);

        var clean = MeterReconciliation.Compare("deal.create", "deal_ids", true, sources[..1], entries[..1], None);
        Assert.Equal((1, 0, 0, 0, 0), (clean.Compared, clean.Missing, clean.Orphans, clean.ZeroEntryStores.Count, clean.Defects));
    }

    [Fact]
    public void A_store_with_sources_and_no_entry_of_the_unit_is_a_zero_entry_store_and_each_of_its_sources_is_missing()
    {
        var sources = new[] { Src("a", "u.x:a:1"), Src("b", "u.x:b:1"), Src("b", "u.x:b:2") };
        var entries = new[] { Row("u.x", "a", "u.x:a:1"), Row("u.other", "b", "u.other:b:1") };
        var c = MeterReconciliation.Compare("u.x", "deal_ids", true, sources, entries, None);
        Assert.Equal((3, 2, 0), (c.Compared, c.Missing, c.Orphans));
        Assert.Equal(["b"], c.ZeroEntryStores);
        Assert.Equal(3, c.Defects);
    }

    [Fact]
    public void A_source_before_the_window_is_not_missing_and_its_entry_is_not_an_orphan()
    {
        var sources = new[] { Src("a", "u.x:a:old", inWindow: false), Src("a", "u.x:a:older", inWindow: false) };
        var entries = new[] { Row("u.x", "a", "u.x:a:old") };
        var c = MeterReconciliation.Compare("u.x", "deal_ids", true, sources, entries, None);
        Assert.Equal((0, 0, 0, 0), (c.Compared, c.Missing, c.Orphans, c.ZeroEntryStores.Count));
    }

    [Fact]
    public void An_excluded_store_is_neither_compared_nor_orphaned_and_its_sources_are_counted_apart_and_seed_entries_are_printed()
    {
        var sources = new[] { Src("imp", "u.x:imp:1"), Src("a", "u.x:a:1") };
        var entries = new[] { Row("u.x", "imp", "u.x:imp:ghost"), Row("u.x", "a", "u.x:a:1", MeterSentinels.Seed) };
        var c = MeterReconciliation.Compare("u.x", "event_ids:ListingCreated", true, sources, entries, new HashSet<string>(["imp"], StringComparer.Ordinal));
        Assert.Equal((1, 0, 0, 1, 1), (c.Compared, c.Missing, c.Orphans, c.Excluded, c.Seed));
        // ‏وبِلا استِثناءٍ يَحمَرّ: المَصدَرُ بِلا سَطرٍ والسَطرُ بِلا مَصدَر.
        var open = MeterReconciliation.Compare("u.x", "event_ids:ListingCreated", true, sources, entries, None);
        Assert.Equal((1, 1), (open.Missing, open.Orphans));
    }

    // ─── النِيّات ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Intents_an_accepted_intent_without_an_entry_is_missing_an_entry_without_an_accepted_intent_is_an_orphan_and_the_rest_are_printed()
    {
        const string U = "otp.sms.send";
        var intents = new[]
        {
            new MeterIntentRow($"{U}:twilio:1", U, "a", "accepted", false),
            new MeterIntentRow($"{U}:twilio:2", U, "a", "accepted", false),
            new MeterIntentRow($"{U}:twilio:3", U, "a", "pending", true),
            new MeterIntentRow($"{U}:twilio:4", U, "a", "pending", false),
            new MeterIntentRow($"{U}:twilio:5", U, "a", "unknown", false),
            new MeterIntentRow($"{U}:twilio:6", U, "a", "refused", false),
            new MeterIntentRow("push.send:webpush:7", "push.send", "a", "accepted", false),
        };
        var entries = new[] { Row(U, "a", $"{U}:twilio:1"), Row(U, "a", $"{U}:twilio:9") };
        var c = MeterReconciliation.CompareIntents(U, intents, entries);
        output.WriteLine($"[G13·pure] {c}");
        Assert.Equal((2, 1, 1, 1, 1, 1, 1), (c.Accepted, c.Missing, c.Orphans, c.Unrecorded, c.Pending, c.Unknown, c.Refused));
        Assert.Equal([$"{U}:twilio:2"], c.MissingIds);
        Assert.Equal((2, 6), (c.Defects, c.Intents));
    }

    // ─── المَجاميع ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Aggregates_equal_to_the_ledger_up_to_the_cursor_match_and_any_difference_on_either_side_is_one_mismatch()
    {
        var ledger = new[] { new MeterDayCount("2026-11-13", "a", "deal.create", 3, 3), new MeterDayCount("2026-11-13", "a", "file.upload", 1, 900) };
        Assert.Empty(MeterReconciliation.CompareAggregates(ledger, ledger));
        var lost = MeterReconciliation.CompareAggregates(ledger, [ledger[0]]);
        Assert.Equal(("file.upload", 1L, 0L), (Assert.Single(lost).Unit, lost[0].LedgerEntries, lost[0].DailyEntries));
        var extra = MeterReconciliation.CompareAggregates([ledger[0]], [.. ledger, new MeterDayCount("2026-11-12", "b", "deal.create", 1, 1)]);
        Assert.Equal(2, extra.Count);
        var bytes = MeterReconciliation.CompareAggregates(ledger, [ledger[0], ledger[1] with { Quantity = 899 }]);
        Assert.Equal((900L, 899L), (Assert.Single(bytes).LedgerQuantity, bytes[0].DailyQuantity));
        // ‏بِلا مُؤَشِّرٍ فَالدَفتَرُ «حَتّى المُؤَشِّر» فارِغ — ومَجموعٌ قائِمٌ عَيب.
        Assert.Single(MeterReconciliation.CompareAggregates([], [ledger[0]]));
    }

    /// <summary>
    /// <para>‏(‏2026-11-14، مُراجَعَةُ إغلاقِ المَرحَلَةِ هـ الثانِيَة · W13-6·E-R2·١ — M6E-CNT-R2-1) <b>مَجموعٌ مَنسوبٌ إلى غَيرِ صاحِبِه عَدَمانِ عَلى مَفاتيحِ <c>agg_daily</c> السَبعَة</b>
    /// (<see cref="MeterSql.AggDailyKey"/>): كانَت المُقارَنَةُ بِـ(يَوم، مَتجَر، وَحدَة) فَصَفٌّ بِعَدَدِه وكَمِّيَّتِه نَفسَيهِما تَحتَ رائِدِ أَعمالٍ آخَرَ يُطابِقُ نَظيفاً — وصَفحَةُ رائِدِ
    /// الأَعمالِ تَقرَأُ ما تَحتَ المُؤَشِّرِ مِن ذلكَ الصَفّ. الآن لِكُلِّ بُعدٍ مِن الأَربَعَةِ الَّتي لا تَراها الثَلاثَة: ما في الدَفتَرِ بِلا مَجموعٍ وما في المَجموعِ بِلا دَفتَر، كُلٌّ بِأَبعادِه
    /// وسَطرُه يُسَمّيها. والضابِطُ: الطَرَفانِ بِالثَلاثَةِ وَحدَها مُتَساويان (فَما كانَ يُطابِقُ نَظيفاً هُوَ هذا بِعَينِه).</para>
    /// </summary>
    [Fact]
    public void An_aggregate_written_under_another_entrepreneur_role_feature_or_provider_is_two_mismatches_on_the_seven_keys()
    {
        var right = new MeterDayCount("2026-11-13", "a", "deal.create", 3, 3) { Entrepreneur = "e1", Role = "_none", Feature = "deal_status", Provider = "_none" };
        var cases = new (string Dimension, MeterDayCount Wrong)[]
        {
            ("entrepreneur", right with { Entrepreneur = "e2" }),
            ("role", right with { Role = "seller" }),
            ("feature", right with { Feature = "chat" }),
            ("provider", right with { Provider = "twilio" }),
        };
        static MeterAggregateMismatch Side(MeterDayCount c, bool ledger)
            => new(c.Day, c.App, c.Unit, ledger ? c.Entries : 0, ledger ? 0 : c.Entries, ledger ? c.Quantity : 0, ledger ? 0 : c.Quantity)
            { Entrepreneur = c.Entrepreneur, Role = c.Role, Feature = c.Feature, Provider = c.Provider };
        static MeterDayCount ThreeKeys(MeterDayCount c) => c with { Entrepreneur = "", Role = "", Feature = "", Provider = "" };

        Assert.Empty(MeterReconciliation.CompareAggregates([right], [right]));
        foreach (var (dimension, wrong) in cases)
        {
            var ms = MeterReconciliation.CompareAggregates([right], [wrong]);
            var lines = new MeterReconciliationReport(MeterReconcileState.Data, null, [], [], "1|5|x", 1, ms, new MeterPresenceCheck(1, 1, [], 0), [], [], 41).Lines()
                .Where(l => l.StartsWith($"{MeterReconciliation.LogTag}   mismatch ", StringComparison.Ordinal)).ToList();
            output.WriteLine($"[G13·pure·agg] {dimension}: {ms.Count} · {string.Join(" | ", lines)} · بِالثَلاثَة {MeterReconciliation.CompareAggregates([ThreeKeys(right)], [ThreeKeys(wrong)]).Count}");
            Assert.Equal(2, ms.Count);
            Assert.Contains(Side(right, ledger: true), ms);
            Assert.Contains(Side(wrong, ledger: false), ms);
            Assert.Contains($"{MeterReconciliation.LogTag}   mismatch 2026-11-13 a deal.create · entrepreneur {right.Entrepreneur} · role {right.Role} · feature {right.Feature} · "
                            + $"provider {right.Provider} · ledger 3/3 · agg_daily 0/0", lines);
            Assert.Contains($"{MeterReconciliation.LogTag}   mismatch 2026-11-13 a deal.create · entrepreneur {wrong.Entrepreneur} · role {wrong.Role} · feature {wrong.Feature} · "
                            + $"provider {wrong.Provider} · ledger 0/0 · agg_daily 3/3", lines);
            Assert.Empty(MeterReconciliation.CompareAggregates([ThreeKeys(right)], [ThreeKeys(wrong)]));
        }
        // ‏والأَبعادُ المَفحوصَةُ هِيَ ما في مِفتاحِ الجَدوَلِ سِوى الثَلاثَة — عَمودٌ يُضافُ إلى المِفتاحِ ولا يُقارَنُ يُحَمِّرُ هذا السَطر.
        Assert.Equal(MeterSql.AggDailyKey.Except(["day", "app", "unit"]), cases.Select(c => c.Dimension));
    }

    // ─── الحُضور ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Presence_a_day_without_a_completed_claim_is_absent_and_a_row_on_such_a_day_is_an_orphan()
    {
        var completed = new SortedSet<string>(["2026-11-11", "2026-11-13"], StringComparer.Ordinal);
        var rows = new[] { new MeterDayCount("2026-11-11", "a", "presence.store", 1, 1), new MeterDayCount("2026-11-12", "a", "presence.store", 1, 1) };
        var p = MeterReconciliation.ComparePresence(new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 13), completed, rows);
        output.WriteLine($"[G13·pure] {p} · {string.Join(",", p.DayKeys)}");
        Assert.Equal((3, 2, 1), (p.Days, p.Completed, p.Orphans));
        Assert.Equal(["2026-11-12"], p.AbsentDays);
        Assert.Equal(["2026-11-11", "2026-11-12", "2026-11-13"], p.DayKeys);
        var never = MeterReconciliation.ComparePresence(null, new DateOnly(2026, 11, 13), completed, []);
        Assert.Equal((0, 0, 0), (never.Days, never.AbsentDays.Count, never.Orphans));
    }

    // ─── الحُكم ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private static MeterReconciliationReport Report(IReadOnlyList<MeterUnitCheck> units, IReadOnlyList<MeterIntentCheck>? intents = null,
        IReadOnlyList<MeterAggregateMismatch>? mismatches = null, int presenceOrphans = 0)
        => new(MeterReconcileState.Data, null, units, intents ?? [], "1|5|x", 1, mismatches ?? [], new MeterPresenceCheck(1, 1, [], presenceOrphans), [], [], 41);

    private static MeterUnitCheck Unit(int compared, int missing = 0, int orphans = 0, string[]? zero = null)
        => new("deal.create", "deal_ids", true, compared, missing, orphans, zero ?? [], 0, 0, 0, [], []);

    [Fact]
    public void The_exit_code_is_0_only_when_something_was_compared_and_nothing_is_a_defect()
    {
        var rows = new (string Name, MeterReconciliationReport R, int Exit)[]
        {
            ("clean", Report([Unit(3)]), 0),
            ("zero sources", Report([Unit(0)]), 1),
            ("missing", Report([Unit(3, missing: 1)]), 1),
            ("orphan", Report([Unit(3, orphans: 1)]), 1),
            ("zero-entry store", Report([Unit(3, zero: ["b"])]), 1),
            ("intent missing", Report([Unit(3)], [new MeterIntentCheck("otp.sms.send", 1, 1, 0, 0, 0, 0, 0, [])]), 1),
            ("only intents compared", Report([Unit(0)], [new MeterIntentCheck("otp.sms.send", 1, 0, 0, 3, 0, 2, 1, [])]), 0),
            ("aggregate mismatch", Report([Unit(3)], mismatches: [new MeterAggregateMismatch("2026-11-13", "a", "deal.create", 3, 2, 3, 2)]), 1),
            ("presence orphan", Report([Unit(3)], presenceOrphans: 1), 1),
            ("no ledger", MeterReconciliationReport.Failed(MeterReconcileState.NoLedger, null), 1),
            ("read failed", MeterReconciliationReport.Failed(MeterReconcileState.Error, null), 1),
        };
        foreach (var (name, r, exit) in rows)
        {
            output.WriteLine($"[G13·pure] {name} ⇐ exit {r.ExitCode} · {r.Lines()[^1]}");
            Assert.Equal(exit, r.ExitCode);
            Assert.Contains($"→ exit {exit}", r.Lines()[^1], StringComparison.Ordinal);
        }
        Assert.Contains(Report([Unit(0)]).Lines(), l => l.Contains("0 sources compared — a blind run", StringComparison.Ordinal));
    }

    // ─── القُرّاء ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary><b>لِكُلِّ قيمَةِ <c>reconcile</c> في المُعجَمِ قارِئٌ في القائِمَةِ المُغلَقَة</b> — ووَحدَةٌ بِقيمَةٍ لا قارِئَ لَها تَحمَرّ (حَقنٌ: نَوعُ حَدَثٍ لا يَعُدُّه المُصَنِّف).</summary>
    [Fact]
    public void Every_reconcile_value_of_the_lexicon_has_a_reader_and_an_unknown_one_has_none()
    {
        var units = MeterUnitCatalog.Default.Units;
        var values = units.Select(u => u.Reconcile!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var unread = values.Where(v => !MeterSourceReaders.Reads(v)).ToList();
        var persistence = units.Where(u => u.Boundary == MeterBoundaries.Persistence).ToList();
        output.WriteLine($"[G13·pure] وَحَدات {units.Count} · قِيَمُ reconcile {values.Count}: {string.Join(", ", values)} · بِلا قارِئ {unread.Count}");
        Assert.True(units.Count >= 41 && values.Count >= 9, $"أَداةٌ عَمياء: {units.Count} وَحدَة و{values.Count} قيمَة.");
        Assert.Empty(unread);
        Assert.All(persistence, u => Assert.True(MeterSourceReaders.IsPersistence(u.Reconcile), u.Code));
        Assert.All(units.Where(u => u.Boundary == MeterBoundaries.Outbound), u => Assert.Equal(MeterReconcilers.Intents, u.Reconcile));
        Assert.All(units.Where(u => u.Boundary == MeterBoundaries.Presence), u => Assert.Equal(MeterReconcilers.PresenceClaims, u.Reconcile));
        foreach (var injected in new[] { "event_ids:ListingViewed", "event_ids:Nope", "ledger", "" })
            Assert.False(MeterSourceReaders.Reads(injected), injected);
    }

    /// <summary>
    /// <para><b>المُعَرِّفُ المُنتَظَرُ = ما يَكتُبُه المُصَنِّف</b>، لِكُلِّ وَحدَةِ حِفظٍ عَشر: المَصدَرُ يُبنى كَما يَحفَظُه البابُ (وَثيقَةٌ أَو حَدَثُ بِدءٍ في مُستَأجِر)،
    /// ويُصَنَّفُ بِـ<see cref="MeterRules.Classify"/>، ويُقرَأُ بِـ<see cref="MeterSourceReaders.SourceOf"/> مِن القيمَةِ الخامِ الَّتي في الجَدوَل — والاثنانِ واحِد.
    /// ومَعَه المُستَأجِرُ الافتِراضِيُّ والفارِغ: <c>_unresolved</c> في الطَرَفَين.</para>
    /// </summary>
    [Fact]
    public void The_expected_entry_id_of_each_persistence_reader_is_the_id_the_classifier_writes()
    {
        var catalog = MeterUnitCatalog.Default;
        var at = DateTime.UtcNow;
        var actor = Guid.NewGuid();
        var deal = new Deal { Id = Guid.NewGuid(), TenantSlug = "store-a", InitiatorId = actor, CounterpartyId = Guid.NewGuid() };
        var tenant = new Tenant { Id = "store-n", OwnerUserId = actor };
        var account = new StudioUser { Id = Guid.NewGuid() };
        var member = new User { Id = Guid.NewGuid() };
        var review = new Review { Id = Guid.NewGuid(), TenantSlug = "store-a", AuthorId = actor, TargetUserId = Guid.NewGuid() };
        var message = new Message { Id = Guid.NewGuid() };
        var docs = new (string Session, object Doc, string? Raw, string Id)[]
        {
            ("store-a", deal, deal.TenantSlug, deal.Id.ToString("D")),
            ("*DEFAULT*", tenant, tenant.Id, tenant.Id),
            ("*DEFAULT*", account, null, account.Id.ToString("D")),
            ("store-a", member, "store-a", member.Id.ToString("D")),
            ("*DEFAULT*", member, "*DEFAULT*", member.Id.ToString("D")),
            ("store-a", review, "store-a", review.Id.ToString("D")),
            ("store-a", message, "store-a", message.Id.ToString("D")),
        };
        var checkedUnits = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (session, doc, raw, id) in docs)
        {
            var written = Assert.Single(MeterRules.Classify(new PendingSnapshot(session, [new PendingDocument(doc, Created: true)]), null, catalog));
            var unit = catalog.Find(written.Unit)!;
            var expected = MeterSourceReaders.SourceOf(unit.Code, unit.Reconcile!, raw, id, at, at.AddMinutes(-1));
            output.WriteLine($"[G13·pure] {unit.Code} ({unit.Reconcile}) · المُصَنِّف {written.Id} · القارِئ {expected.EntryId}");
            Assert.Equal(written.Id, expected.EntryId);
            Assert.Equal(written.App, expected.App);
            Assert.True(expected.InWindow);
            checkedUnits.Add(unit.Code);
        }
        var events = new (object Data, string Tenant)[]
        {
            (new ListingCreated(Guid.NewGuid(), "store-a", "إعلانٌ", null, 1m, "general", null, null, [], at), "store-a"),
            (new OfferSubmitted(Guid.NewGuid(), Guid.NewGuid(), actor, "عارِض", 1m, null, 24.7, 46.6, at.AddMinutes(15), at), "store-a"),
            (new TicketCreated(Guid.NewGuid(), actor, "كاتِب", "سُؤال", "نَصّ", at), "store-a"),
            (new SubscriptionCreated(Guid.NewGuid(), actor, "free", 3, 30, at), "*DEFAULT*"),
        };
        foreach (var (data, tenantId) in events)
        {
            var eventId = Guid.NewGuid();
            var pending = new PendingSnapshot(tenantId, [], [new PendingStream(tenantId, Started: true, [new PendingEvent(eventId, data)])]);
            var written = Assert.Single(MeterRules.Classify(pending, null, catalog));
            var unit = catalog.Find(written.Unit)!;
            Assert.Equal(data.GetType(), MeterSourceReaders.EventTypeOf(unit.Reconcile));
            var expected = MeterSourceReaders.SourceOf(unit.Code, unit.Reconcile!, tenantId, eventId.ToString("D"), at, at);
            output.WriteLine($"[G13·pure] {unit.Code} ({unit.Reconcile}) · المُصَنِّف {written.Id} · القارِئ {expected.EntryId}");
            Assert.Equal(written.Id, expected.EntryId);
            checkedUnits.Add(unit.Code);
        }
        var persistence = catalog.Units.Where(u => u.Boundary == MeterBoundaries.Persistence).Select(u => u.Code).Order(StringComparer.Ordinal).ToList();
        output.WriteLine($"[G13·pure] وَحَداتُ حِفظٍ مُطابَقَةُ المُعَرِّف {checkedUnits.Count}/{persistence.Count}");
        Assert.Equal(persistence, checkedUnits);
        // ‏والعُضوُ اسمٌ مُستَعار — لا مُعَرِّفُه في المُعَرِّفِ المُنتَظَر.
        Assert.DoesNotContain(member.Id.ToString("D"), MeterSourceReaders.SourceOf("member.join", "user_ids", "store-a", member.Id.ToString("D"), at, at).EntryId, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_time_is_read_as_utc_and_a_missing_or_unreadable_time_is_outside_the_window()
    {
        var since = new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal(since, MeterSourceReaders.TimeOf("2026-11-13T10:00:00Z"));
        Assert.Equal(since, MeterSourceReaders.TimeOf("2026-11-13T10:00:00"));
        Assert.Equal(since, MeterSourceReaders.TimeOf("2026-11-13T13:00:00+03:00"));
        Assert.Null(MeterSourceReaders.TimeOf(null));
        Assert.Null(MeterSourceReaders.TimeOf("لا تاريخ"));
        Assert.True(MeterSourceReaders.SourceOf("deal.create", "deal_ids", "a", "1", since, since).InWindow);
        Assert.False(MeterSourceReaders.SourceOf("deal.create", "deal_ids", "a", "1", since.AddTicks(-1), since).InWindow);
        Assert.False(MeterSourceReaders.SourceOf("deal.create", "deal_ids", "a", "1", null, since).InWindow);
        Assert.False(MeterSourceReaders.SourceOf("deal.create", "deal_ids", "a", "1", since, null).InWindow);
    }

    /// <summary><b>المُستَورِدُ استِثناءٌ مُسَمّى بِوَحَداتِه</b>: ما يَكتُبُه <c>TargetWriter</c> بِلا مُستَمِعٍ مِن نَوعٍ مَعدود (مُثَبَّتٌ <c>ImporterExcluded</c> في G18) وَحَداتُه هِيَ
    /// قائِمَةُ الاستِثناء — لا أَقَلَّ ولا أَكثَر؛ وجَداوِلُ <c>ImportedRecord</c> الَّتي تَكتُبُها المَنَصَّةُ لا تَجعَلُ مَتجَراً مُستَورَداً.</summary>
    [Fact]
    public void The_importer_exclusion_covers_exactly_the_units_the_importer_writes_without_the_listener()
    {
        var excludedTypes = MeterSeedWritersTests.Pinned.Where(p => p.Fate == MeterSeedWritersTests.Fate.ImporterExcluded)
            .Select(p => p.Type).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var units = MeterUnitCatalog.Default.Units.Where(u => u.Boundary == MeterBoundaries.Persistence).ToList();
        var byType = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(Tenant)] = MeterReconcilers.TenantIds, [nameof(User)] = MeterReconcilers.UserIds, [nameof(Message)] = MeterReconcilers.MessageIds,
        };
        var expected = excludedTypes.Select(t => byType.TryGetValue(t, out var r) ? r : MeterReconcilers.EventIdsPrefix + t)
            .Order(StringComparer.Ordinal).ToList();
        output.WriteLine($"[G13·pure] أَنواعُ المُستَورِدِ المُستَثناةُ في G18: {string.Join(", ", excludedTypes)} · قُرّاؤُها {string.Join(", ", expected)}");
        Assert.True(excludedTypes.Count >= 4, $"أَداةٌ عَمياء: {excludedTypes.Count} نَوعاً.");
        // ‏والرِسالَةُ يَكتُبُها المَوضِعُ العامُّ نَفسُه (`UpsertAsync<T>`، مُثَبَّتٌ في G18 بِنَوعِه الأَوَّلِ User) — مَقيسَةٌ هُنا بِمَوضِعِها في المُستَورِدَين.
        foreach (var importer in new[] { "AshareImporter.cs", "EjarImporter.cs" })
        {
            var text = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "tools", "ACommerce.Importer", importer));
            Assert.Contains("_target.UpsertAsync(TenantSlug, msgs)", text, StringComparison.Ordinal);
            Assert.Contains("new Message", text, StringComparison.Ordinal);
        }
        expected = [.. expected.Append(MeterReconcilers.MessageIds).Order(StringComparer.Ordinal)];
        Assert.Equal(expected, MeterSourceReaders.ImporterReconcilers.Order(StringComparer.Ordinal));
        Assert.All(MeterSourceReaders.ImporterReconcilers, r => Assert.Contains(units, u => u.Reconcile == r));
        Assert.Equal(["AttributeDefinitions", "AttributeValues", "CategoryAttributeMappings", "DiscoveryRegions"],
            MeterSourceReaders.PlatformImportedTables.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_report_prints_per_unit_compared_missing_orphans_zero_entry_stores_and_the_intent_counts()
    {
        var r = Report([new MeterUnitCheck("deal.create", "deal_ids", true, 4, 1, 0, ["b"], 2, 0, 0, ["deal.create:b:1"], [])],
            [new MeterIntentCheck("otp.sms.send", 1, 0, 0, 1, 0, 1, 1, [])]);
        var lines = r.Lines();
        foreach (var l in lines) output.WriteLine(l);
        Assert.Contains("[meter-reconcile] deal.create · compared 4 · missing 1 · orphans 0 · zero-entry stores 1 (b) · seed 2", lines);
        Assert.Contains("[meter-reconcile]   missing deal.create:b:1", lines);
        Assert.Contains("[meter-reconcile] otp.sms.send · intents 4 · accepted 1 · missing 0 · orphans 0 · unrecorded 1 · pending 0 · unknown 1 · refused 1", lines);
        Assert.Contains(lines, l => l.StartsWith("[meter-reconcile] exclusions · seed 2 (deal.create 2)", StringComparison.Ordinal));
        Assert.Equal("[meter-reconcile] result · sources compared 5 · defects 2 → exit 1", lines[^1]);
    }
}
