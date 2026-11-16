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

// ═══ المُصَنِّفُ النَقِيّ — `deal.create` مِن صَفقَةٍ وُلِدَت، لا مِن تَحديث، وكُلُّ عَمودٍ قيمَةٌ أَو قيمَةٌ حارِسَة ═══
// (‏2026-11-12، الموجَة ١٣ W13-6·٣ — ADR-051 §١١؛ التَصميم §5.1–§5.5). بِلا قاعِدَةٍ ولا جَلسَة: المُصَنِّفُ دالَّةٌ عَلى قِيَم.

public class MeterRulesTests(ITestOutputHelper output)
{
    private static Deal NewDeal(string slug = "store-a") => new()
    {
        Id = Guid.NewGuid(), TenantSlug = slug, InitiatorId = Guid.NewGuid(), CounterpartyId = Guid.NewGuid(),
    };

    private static IReadOnlyList<UsageEntry> Classify(MeterScopeView? scope, params PendingDocument[] docs)
        => MeterRules.Classify(new PendingSnapshot("store-a", docs), scope, MeterUnitCatalog.Default);

    [Fact]
    public void A_deal_born_in_this_save_gives_one_deal_create_entry_with_a_deterministic_id()
    {
        var deal = NewDeal();
        var e = Assert.Single(Classify(null, new PendingDocument(deal, Created: true)));
        output.WriteLine($"[rules] {e}");
        Assert.Equal($"deal.create:store-a:{deal.Id:D}", e.Id);
        Assert.Equal(UsageEntryId.For("deal.create", "store-a", deal.Id.ToString("D")), e.Id);
        Assert.Equal(("deal.create", 1L, "store-a", "deal_status", MeterSentinels.None, "deal", deal.Id.ToString("D")),
                     (e.Unit, e.Quantity, e.App, e.Feature, e.Provider, e.SourceType, e.SourceId));
        Assert.Equal(e, Assert.Single(Classify(null, new PendingDocument(deal, Created: true))));
    }

    /// <summary><b>التَحديثُ لا يُعَدّ</b> (§5.6): صَفقَةٌ لَها نُسخَةٌ في الجَلسَة (<c>VersionFor != null</c>) تَحديثٌ لا وِلادَة.</summary>
    [Fact]
    public void An_update_of_a_deal_is_not_counted()
        => Assert.Empty(Classify(null, new PendingDocument(NewDeal(), Created: false)));

    [Fact]
    public void A_document_of_another_type_is_not_counted_and_one_deal_twice_is_one_entry()
    {
        var deal = NewDeal();
        Assert.Empty(Classify(null, new PendingDocument(new object(), Created: true), new PendingDocument("text", Created: true)));
        Assert.Single(Classify(null, new PendingDocument(deal, Created: true), new PendingDocument(deal, Created: true)));
        Assert.Equal(2, Classify(null, new PendingDocument(NewDeal(), true), new PendingDocument(NewDeal(), true)).Count);
    }

    /// <summary><b>كُلُّ عَمودٍ مَملوءٌ مِن قيمَةٍ أَو قيمَةٍ حارِسَة</b> (§5.5: <c>NOT NULL</c> لا يُفشِلُ كِتابَةَ عَمَلٍ أَبَداً): بِلا حامِلٍ وبِلا مَتجَرٍ
    /// في الصَفقَة، كُلُّ بُعدٍ مَجهولٍ <c>_unresolved</c> والمُزَوِّدُ <c>_none</c> — ولا يَرمي.</summary>
    [Fact]
    public void Every_column_is_a_value_or_a_sentinel_even_with_no_holder_and_no_store()
    {
        var entries = Classify(null, new PendingDocument(NewDeal(slug: ""), true), new PendingDocument(NewDeal(slug: "  "), true));
        Assert.Equal(2, entries.Count);
        foreach (var e in entries)
        {
            var columns = new[] { e.Id, e.Unit, e.App, e.Entrepreneur, e.Role, e.Feature, e.Provider, e.SourceType, e.SourceId };
            output.WriteLine($"[rules] {string.Join(" | ", columns)}");
            Assert.All(columns, c => Assert.False(string.IsNullOrWhiteSpace(c)));
            Assert.Equal((MeterSentinels.Unresolved, MeterSentinels.Unresolved, MeterSentinels.Unresolved, MeterSentinels.None),
                         (e.App, e.Entrepreneur, e.Role, e.Provider));
            Assert.True(e.Quantity > 0);
        }
        Assert.All(new[] { MeterSentinels.Unresolved, MeterSentinels.None }, s => Assert.True(MeterSentinels.IsSentinel(s)));
    }

    [Fact]
    public void The_holder_of_the_same_store_fills_entrepreneur_and_role_and_any_other_store_leaves_them_unresolved()
    {
        var owner = Guid.NewGuid().ToString("D");
        var same = Assert.Single(Classify(new MeterScopeView("store-a", owner, MeterSentinels.None, false), new PendingDocument(NewDeal(), true)));
        Assert.Equal((owner, MeterSentinels.None), (same.Entrepreneur, same.Role));

        var caseOnly = Assert.Single(Classify(new MeterScopeView("STORE-A", owner, "seller", false), new PendingDocument(NewDeal(), true)));
        Assert.Equal((owner, "seller"), (caseOnly.Entrepreneur, caseOnly.Role));

        var other = Assert.Single(Classify(new MeterScopeView("store-b", owner, MeterSentinels.None, false), new PendingDocument(NewDeal(), true)));
        Assert.Equal((MeterSentinels.Unresolved, MeterSentinels.Unresolved), (other.Entrepreneur, other.Role));

        var partial = Assert.Single(Classify(new MeterScopeView("store-a", owner, null, false), new PendingDocument(NewDeal(), true)));
        Assert.Equal((owner, MeterSentinels.Unresolved), (partial.Entrepreneur, partial.Role));
    }

    [Fact]
    public void The_seed_holder_attributes_every_store_to_seed()
    {
        var seed = new MeterScopeView(null, MeterSentinels.Seed, MeterSentinels.Seed, IsSeed: true);
        var entries = Classify(seed, new PendingDocument(NewDeal("ejar"), true), new PendingDocument(NewDeal("theme-demo"), true));
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.Equal((MeterSentinels.Seed, MeterSentinels.Seed), (e.Entrepreneur, e.Role)));
    }

    /// <summary>المُصَنِّفُ نَقِيٌّ بِالتَوقيع (القاعِدَة ٧): لا مُعامِلَ جَلسَةٍ ولا مَخزَنٍ في أَيِّ دالَّةٍ عامَّةٍ مِنه.</summary>
    [Fact]
    public void The_classifier_takes_no_session_and_no_store()
    {
        var methods = typeof(MeterRules).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var parameters = methods.SelectMany(m => m.GetParameters()).Select(p => p.ParameterType).ToList();
        output.WriteLine($"[rules] دَوالُّ عامَّة {methods.Length} · مُعامِلات {parameters.Count}: {string.Join(", ", parameters.Select(t => t.Name).Distinct())}");
        Assert.True(parameters.Count > 0, "أَداةٌ عَمياء: صِفرُ مُعامِل.");
        Assert.DoesNotContain(parameters, t => typeof(Marten.IQuerySession).IsAssignableFrom(t) || typeof(Marten.IDocumentStore).IsAssignableFrom(t));
    }

    // ═══ ‏(‏2026-11-12، W13-6·٧) وَحَداتُ الأَحداث — الحَدَثُ الَّذي يَبدَأُ بِه مَجراه سَطرٌ واحِد، والمَتجَرُ مُستَأجِرُ المَجرى، والمَصدَرُ مُعَرِّفُ الحَدَث ═══
    // (التَصميم §3.4 و§5.2 و§5.3). المُدخَلُ قِيَمٌ كَما يَبنيها `MeterRules.StreamsOf` مِن `PendingChanges.Streams()`؛ وبُرهانُ الجَلسَةِ الحَقيقِيَّةِ في G3 وG5.

    /// <summary>حَدَثُ وِلادَةٍ لِكُلِّ وَحدَةٍ بِأَطرافٍ مَعروفَة — والفاعِلُ في الحَدَثِ (المُعلِن، العارِض، الكاتِب، العُضو) مُعَرِّفٌ مُختَلِفٌ عَن مُعَرِّفِ الحَدَث.</summary>
    internal static IReadOnlyList<(string Unit, object Birth, Guid Actor)> Births()
    {
        var actor = Guid.NewGuid();
        var at = new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);
        return
        [
            (MeterRules.ListingCreate, new ListingCreated(Guid.NewGuid(), "store-a", "إعلانٌ", null, 1m, "general", null, null, new() { ["owner_id"] = actor.ToString() }, at), actor),
            (MeterRules.OfferSubmit, new OfferSubmitted(Guid.NewGuid(), Guid.NewGuid(), actor, "عارِض", 1m, null, 24.7, 46.6, at.AddMinutes(15), at), actor),
            (MeterRules.TicketOpen, new TicketCreated(Guid.NewGuid(), actor, "كاتِب", "سُؤال", "نَصّ", at), actor),
            (MeterRules.MemberPlanSubscribe, new SubscriptionCreated(Guid.NewGuid(), actor, "free", 3, 30, at), actor),
        ];
    }

    private static PendingStream Started(string? tenant, params PendingEvent[] events) => new(tenant, Started: true, events);

    private static IReadOnlyList<UsageEntry> ClassifyStreams(string sessionTenant, MeterScopeView? scope, params PendingStream[] streams)
        => MeterRules.Classify(new PendingSnapshot(sessionTenant, [], streams), scope, MeterUnitCatalog.Default);

    /// <summary><b>لِكُلِّ وَحدَةِ حَدَثٍ سَطرٌ واحِدٌ مِن مَجرىً يَبدَأ</b>: المُعَرِّفُ <c>{unit}:{مُستَأجِرُ المَجرى}:{مُعَرِّفُ الحَدَث}</c>، والمَتجَرُ مُستَأجِرُ المَجرى
    /// لا مُستَأجِرُ الجَلسَة، والمِيزَةُ مِيزَةُ صَفِّ المُعجَم، والمَصدَرُ الحَدَثُ لا فاعِلُه. والمَجرى نَفسُه مَرَّتَينِ سَطرٌ واحِد.</summary>
    [Fact]
    public void Each_event_unit_is_one_entry_from_a_started_stream_keyed_by_the_event_id_in_the_stream_tenant()
    {
        var births = Births();
        Assert.Equal(MeterRules.StreamBirths.Count, births.Count);
        foreach (var (unit, birth, actor) in births)
        {
            var eventId = Guid.NewGuid();
            var started = Started("store-b", new PendingEvent(eventId, birth));
            var e = Assert.Single(ClassifyStreams("store-a", null, started));
            output.WriteLine($"[rules·حَدَث] {e}");
            var row = MeterUnitCatalog.Default.Find(unit)!;
            Assert.Equal((unit, 1L, "store-b", $"{unit}:store-b:{eventId:D}", row.Feature, MeterSentinels.None, MeterRules.EventSource, eventId.ToString("D")),
                         (e.Unit, e.Quantity, e.App, e.Id, e.Feature, e.Provider, e.SourceType, e.SourceId));
            Assert.Equal(UsageEntryId.For(unit, "store-b", eventId.ToString("D")), e.Id);
            Assert.DoesNotContain(actor.ToString("D"), e.Id + " " + e.SourceId, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(e, Assert.Single(ClassifyStreams("store-a", null, started, started)));
        }
    }

    /// <summary><b>ما لا يُعَدُّ مِن المَجاري</b>: حَدَثُ الوِلادَةِ يُلحَقُ بِمَجرىً قائِم (لا يَبدَأ)، وأَحداثُ مَجرىً يَبدَأُ بِغَيرِ حَدَثِ وِلادَة (تَحريرٌ ووَسائِط
    /// وشارات)، والمَجرى الفارِغ — صِفر. والوِلادَةُ مَعَ أَحداثٍ أُخرى في البِدءِ نَفسِه (الإعلانُ بِصُوَرِه) سَطرٌ واحِد.</summary>
    [Fact]
    public void An_appended_birth_event_and_a_stream_started_by_another_event_are_not_counted()
    {
        var at = DateTime.UtcNow;
        var listing = Guid.NewGuid();
        var created = new ListingCreated(listing, "store-a", "إعلانٌ", null, 1m, "general", null, null, [], at);
        foreach (var (_, birth, _) in Births())
            Assert.Empty(ClassifyStreams("store-a", null, new PendingStream("store-a", Started: false, [new PendingEvent(Guid.NewGuid(), birth)])));
        Assert.Empty(ClassifyStreams("store-a", null,
            Started("store-a", new PendingEvent(Guid.NewGuid(), new ListingEdited(listing, "عُنوان", null, null, null, null, null, null, at))),
            Started("store-a", new PendingEvent(Guid.NewGuid(), new ListingMediaSet(listing, ["a.jpg"], at))),
            Started("store-a", new PendingEvent(Guid.NewGuid(), new ListingFlagsSet(listing, true, true, at))),
            Started("store-a")));
        var withMedia = Started("store-a", new PendingEvent(Guid.NewGuid(), created), new PendingEvent(Guid.NewGuid(), new ListingMediaSet(listing, ["a.jpg"], at)));
        Assert.Equal(MeterRules.ListingCreate, Assert.Single(ClassifyStreams("store-a", null, withMedia)).Unit);
    }

    /// <summary><b>اللَقَطاتُ المُباشِرَةُ مُشتَقَّةٌ ⇐ صِفر</b> (التَصميم §5.3): <c>Listing</c> و<c>Offer</c> و<c>Ticket</c> و<c>Subscription</c> تَكتُبُها الإسقاطاتُ
    /// في وَحدَةِ العَمَلِ نَفسِها، وعَدُّها مَعَ حَدَثِها عَدٌّ مَرَّتَين — وِلادَةً وتَحديثاً. ومَعَ مَجرى الإعلانِ في الحِفظِ نَفسِه: سَطرُ الحَدَثِ وَحدَه.</summary>
    [Fact]
    public void The_four_inline_snapshots_are_derived_and_yield_no_entry()
    {
        object[] snapshots = [new Listing { Id = Guid.NewGuid() }, new Offer { Id = Guid.NewGuid() }, new Ticket { Id = Guid.NewGuid() }, new Subscription { Id = Guid.NewGuid() }];
        foreach (var snap in snapshots)
            Assert.Empty(Classify(null, new PendingDocument(snap, Created: true), new PendingDocument(snap, Created: false)));
        var (_, birth, _) = Births()[0];
        var both = MeterRules.Classify(new PendingSnapshot("store-a", [new PendingDocument(snapshots[0], true)], [Started("store-a", new PendingEvent(Guid.NewGuid(), birth))]),
                                       null, MeterUnitCatalog.Default);
        Assert.Equal(MeterRules.ListingCreate, Assert.Single(both).Unit);
        output.WriteLine($"[rules·حَدَث] لَقَطاتٌ مَفحوصَة {snapshots.Length} · سُطورُها 0 · وَمَعَ حَدَثِها {both.Count}");
    }

    /// <summary><b>مَتجَرُ سَطرِ الحَدَث</b>: مُستَأجِرُ المَجرى؛ وإن غابَ فَمُستَأجِرُ الجَلسَة؛ والمُستَأجِرُ الافتِراضِيُّ (<c>*DEFAULT*</c>) والفارِغُ
    /// <c>_unresolved</c> — يُعَدُّ ولا يَرمي، وكُلُّ عَمودٍ قيمَةٌ أَو قيمَةٌ حارِسَة.</summary>
    [Fact]
    public void The_entry_app_is_the_stream_tenant_then_the_session_tenant_and_never_the_default_tenant()
    {
        var (_, birth, _) = Births()[2];
        Assert.Equal("store-b", Assert.Single(ClassifyStreams("store-a", null, Started("store-b", new PendingEvent(Guid.NewGuid(), birth)))).App);
        Assert.Equal("store-a", Assert.Single(ClassifyStreams("store-a", null, Started(null, new PendingEvent(Guid.NewGuid(), birth)))).App);
        foreach (var (stream, session) in new (string?, string)[] { ("*DEFAULT*", "store-a"), (null, "*DEFAULT*"), ("  ", ""), (null, " ") })
        {
            var e = Assert.Single(ClassifyStreams(session, null, Started(stream, new PendingEvent(Guid.NewGuid(), birth))));
            Assert.Equal(MeterSentinels.Unresolved, e.App);
            Assert.All(new[] { e.Id, e.Unit, e.App, e.Entrepreneur, e.Role, e.Feature, e.Provider, e.SourceType, e.SourceId },
                       c => Assert.False(string.IsNullOrWhiteSpace(c)));
        }
    }

    /// <summary><b>الحامِلُ لِسَطرِ الحَدَثِ كَما لِلصَفقَة</b> (§5.4): حامِلُ المَتجَرِ نَفسِه يَملَأُ رائِدَ الأَعمالِ والدَور، وحامِلُ مَتجَرٍ آخَرَ لا يَنسِبُ،
    /// وحامِلُ البَذرِ يَنسِبُ كُلَّ مَتجَرٍ إلى <c>_seed</c> (بَذرُ الإعلاناتِ في <c>PlatformSeed</c>).</summary>
    [Fact]
    public void The_holder_attributes_event_entries_only_for_its_own_store_and_the_seed_holder_for_every_store()
    {
        var owner = Guid.NewGuid().ToString("D");
        var (_, birth, _) = Births()[0];
        PendingStream In(string slug) => Started(slug, new PendingEvent(Guid.NewGuid(), birth));
        var same = Assert.Single(ClassifyStreams("store-a", new MeterScopeView("store-a", owner, MeterSentinels.None, false), In("store-a")));
        Assert.Equal((owner, MeterSentinels.None), (same.Entrepreneur, same.Role));
        var other = Assert.Single(ClassifyStreams("store-b", new MeterScopeView("store-a", owner, MeterSentinels.None, false), In("store-b")));
        Assert.Equal((MeterSentinels.Unresolved, MeterSentinels.Unresolved), (other.Entrepreneur, other.Role));
        var seeded = ClassifyStreams("ejar", new MeterScopeView(null, MeterSentinels.Seed, MeterSentinels.Seed, IsSeed: true), In("ejar"), In("theme-demo"));
        Assert.Equal(2, seeded.Count);
        Assert.All(seeded, e => Assert.Equal((MeterSentinels.Seed, MeterSentinels.Seed), (e.Entrepreneur, e.Role)));
    }

    /// <summary><b>القاعِدَةُ والمُعجَمُ والدَفتَرُ يَقولونَ الشَيءَ نَفسَه</b>: كُلُّ حَدَثِ وِلادَةٍ في <see cref="MeterRules.StreamBirths"/> وَحدَتُه في المُعجَمِ تَشغيلٌ
    /// عِندَ حَدِّ الحِفظ وقارِئُ مُطابَقَتِها <c>event_ids:{نَوعُه}</c> بِعَينِه، وصَفُّه في دَفتَرِ التَصنيفِ «مَعدودٌ» بِوَحدَتِه وبِشَكلِ <c>StartStream</c> —
    /// والصُفوفُ المَعدودَةُ بِهذا الشَكلِ هِيَ هذه بِلا زِيادَة. يَطبَعُ ما طابَقَ ويَفشَلُ عَلى الصِفر.</summary>
    [Fact]
    public void Every_stream_birth_rule_agrees_with_its_lexicon_row_and_its_classification_row()
    {
        var births = MeterRules.StreamBirths;
        foreach (var (type, code) in births)
        {
            var unit = MeterUnitCatalog.Default.Find(code);
            Assert.NotNull(unit);
            Assert.Equal((MeterSides.Operation, MeterBoundaries.Persistence, MeterMeasures.Count, MeterReconcilers.EventIdsPrefix + type.Name),
                         (unit.Side, unit.Boundary, unit.Measure, unit.Reconcile));
            var row = Assert.Single(MeterClassification.All, r => r.ClrType == type);
            Assert.Equal((MeterDisposition.Counted, code, (MeterCreation?)MeterCreation.StartStream), (row.Disposition, row.Unit, row.Creation));
        }
        var countedStreams = MeterClassification.All.Where(r => r is { Disposition: MeterDisposition.Counted, Creation: MeterCreation.StartStream }).Select(r => r.ClrType).ToHashSet();
        output.WriteLine($"[rules·حَدَث] قَواعِدُ الوِلادَة {births.Count}: {string.Join(", ", births.Select(b => $"{b.Key.Name}→{b.Value}"))} · صُفوفٌ مَعدودَةٌ بِبِدءِ مَجرى {countedStreams.Count}");
        Assert.True(births.Count >= 4, $"أَداةٌ عَمياء: {births.Count} قاعِدَةَ وِلادَة.");
        Assert.True(countedStreams.SetEquals(births.Keys), "صُفوفُ «مَعدودٍ بِبِدءِ مَجرى» والقَواعِدُ تَختَلِفان: " + string.Join(", ", countedStreams.Except(births.Keys).Concat(births.Keys.Except(countedStreams)).Select(t => t.Name)));
    }

    // ═══ ‏(‏2026-11-12، W13-6·٨) وَحَداتُ الوَثائِقِ الأَربَع — مِن الوِلادَةِ لا مِن التَحديث، والمَتجَرُ ورائِدُ الأَعمالِ حَيثُ تَحمِلُهُما الوَثيقَة ═══
    // (التَصميم §3.4 و§5.2 و§5.4). «وِلادَةٌ» يُقَرِّرُها المُستَمِع: <c>Inserts</c> لِـ<c>Tenant</c> و<c>Review</c>، و<c>VersionFor == null</c> لِلمُتَفائِلَتَين.

    private static IReadOnlyList<UsageEntry> ClassifyIn(string sessionTenant, MeterScopeView? scope, params PendingDocument[] docs)
        => MeterRules.Classify(new PendingSnapshot(sessionTenant, docs), scope, MeterUnitCatalog.Default);

    private static readonly MeterScopeView SeedScope = new(null, MeterSentinels.Seed, MeterSentinels.Seed, IsSeed: true);

    /// <summary><b><c>store.create</c></b>: مَتجَرٌ في <c>Inserts</c> سَطرٌ واحِد — مَتجَرُه ومَصدَرُه <c>Tenant.Id</c>، ورائِدُ أَعمالِه <c>OwnerUserId</c> <b>مِن الوَثيقَة</b>
    /// (و<c>_unowned</c> لِلفارِغ) حَتّى تَحتَ حامِلِ البَذر، ودَورُه مِن الحامِل. وتَحديثُه صِفر، ومُستَأجِرُ الجَلسَةِ لا شَأنَ لَه (الوَثيقَةُ عامَّة).</summary>
    [Fact]
    public void A_tenant_in_inserts_is_one_store_create_entry_whose_app_and_entrepreneur_come_from_the_document()
    {
        var owner = Guid.NewGuid();
        var tenant = new Tenant { Id = "store-n", OwnerUserId = owner };
        var e = Assert.Single(ClassifyIn("*DEFAULT*", null, new PendingDocument(tenant, Created: true)));
        output.WriteLine($"[rules·وَثيقَة] {e}");
        Assert.Equal((MeterRules.StoreCreate, 1L, "store-n", "store.create:store-n:store-n", owner.ToString("D"), MeterSentinels.Unresolved, "tenants",
                      MeterSentinels.None, MeterRules.TenantSource, "store-n"),
                     (e.Unit, e.Quantity, e.App, e.Id, e.Entrepreneur, e.Role, e.Feature, e.Provider, e.SourceType, e.SourceId));
        Assert.Empty(ClassifyIn("*DEFAULT*", null, new PendingDocument(tenant, Created: false)));

        var unowned = Assert.Single(ClassifyIn("*DEFAULT*", null, new PendingDocument(new Tenant { Id = "store-u" }, true)));
        Assert.Equal(MeterSentinels.Unowned, unowned.Entrepreneur);
        var seeded = Assert.Single(ClassifyIn("*DEFAULT*", SeedScope, new PendingDocument(new Tenant { Id = "ejar" }, true)));
        Assert.Equal((MeterSentinels.Unowned, MeterSentinels.Seed), (seeded.Entrepreneur, seeded.Role));
        var sameStore = Assert.Single(ClassifyIn("*DEFAULT*", new MeterScopeView("store-n", "holder-owner", "vendor", false), new PendingDocument(tenant, true)));
        Assert.Equal((owner.ToString("D"), "vendor"), (sameStore.Entrepreneur, sameStore.Role));
        var blank = Assert.Single(ClassifyIn("*DEFAULT*", null, new PendingDocument(new Tenant { Id = " " }, true)));
        Assert.Equal((MeterSentinels.Unresolved, MeterSentinels.Unresolved), (blank.App, blank.SourceId));
    }

    /// <summary><b><c>studio.account.create</c></b>: حِسابُ استوديو بِلا نُسخَةٍ سَطرٌ واحِدٌ عَلى <c>_platform</c>، رائِدُ أَعمالِه والمَصدَرُ الحِسابُ نَفسُه
    /// (مِفتاحُ عَميلِ المَنَصَّةِ صَريحاً، التَصميم §2). وتَحديثُه صِفر.</summary>
    [Fact]
    public void A_studio_account_born_is_one_studio_account_create_entry_on_the_platform_whose_entrepreneur_is_the_account()
    {
        var account = new StudioUser { Id = Guid.NewGuid() };
        var id = account.Id.ToString("D");
        var e = Assert.Single(ClassifyIn(StudioAuth.Tenant, null, new PendingDocument(account, Created: true)));
        output.WriteLine($"[rules·وَثيقَة] {e}");
        Assert.Equal((MeterRules.StudioAccountCreate, MeterSentinels.Platform, $"studio.account.create:_platform:{id}", id, MeterSentinels.Unresolved, "studio.build",
                      MeterRules.StudioUserSource, id),
                     (e.Unit, e.App, e.Id, e.Entrepreneur, e.Role, e.Feature, e.SourceType, e.SourceId));
        Assert.Empty(ClassifyIn(StudioAuth.Tenant, null, new PendingDocument(account, Created: false)));
        var seeded = Assert.Single(ClassifyIn(StudioAuth.Tenant, SeedScope, new PendingDocument(account, true)));
        Assert.Equal((id, MeterSentinels.Seed), (seeded.Entrepreneur, seeded.Role));
    }

    /// <summary><b><c>member.join</c></b>: عُضوٌ بِلا نُسخَةٍ سَطرٌ واحِدٌ في مَتجَرِ الجَلسَة — <b>ومَصدَرُه اسمٌ مُستَعار</b>، فَمُعَرِّفُ العُضوِ لا يَظهَرُ في المُعَرِّفِ
    /// ولا في المَصدَر (G17)؛ ورائِدُ الأَعمالِ والدَورُ مِن حامِلِ المَتجَرِ نَفسِه؛ والمُستَأجِرُ الافتِراضِيُّ <c>_unresolved</c>. وتَحديثُه صِفر.</summary>
    [Fact]
    public void A_member_born_is_one_member_join_entry_in_the_session_store_whose_source_is_a_pseudonym_never_the_user_id()
    {
        var member = new User { Id = Guid.NewGuid() };
        var owner = Guid.NewGuid().ToString("D");
        var e = Assert.Single(ClassifyIn("store-a", new MeterScopeView("store-a", owner, MeterSentinels.None, false), new PendingDocument(member, Created: true)));
        var pseudonym = UsageEntryId.Pseudonym(MeterRules.MemberJoin, "store-a", member.Id.ToString("D"));
        output.WriteLine($"[rules·وَثيقَة] {e}");
        Assert.Equal((MeterRules.MemberJoin, "store-a", $"member.join:store-a:{pseudonym}", owner, MeterSentinels.None, "auth", MeterRules.UserSource, pseudonym),
                     (e.Unit, e.App, e.Id, e.Entrepreneur, e.Role, e.Feature, e.SourceType, e.SourceId));
        foreach (var form in new[] { member.Id.ToString("D"), member.Id.ToString("N") })
            Assert.DoesNotContain(form, e.Id + " " + e.SourceId, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(ClassifyIn("store-a", null, new PendingDocument(member, Created: false)));
        Assert.Equal(MeterSentinels.Unresolved, Assert.Single(ClassifyIn("*DEFAULT*", null, new PendingDocument(member, true))).App);
    }

    /// <summary><b><c>review.create</c></b>: تَقييمٌ في <c>Inserts</c> سَطرٌ واحِدٌ في مَتجَرِ الجَلسَة بِمُعَرِّفِ التَقييم — لا الكاتِبُ ولا المُقَيَّم. وتَحديثُه
    /// (الرَدُّ والإخفاءُ <c>Store</c>) صِفر.</summary>
    [Fact]
    public void A_review_in_inserts_is_one_review_create_entry_in_the_session_store_keyed_by_the_review_id()
    {
        var review = new Review { Id = Guid.NewGuid(), TenantSlug = "store-a", AuthorId = Guid.NewGuid(), TargetUserId = Guid.NewGuid() };
        var e = Assert.Single(ClassifyIn("store-a", null, new PendingDocument(review, Created: true)));
        output.WriteLine($"[rules·وَثيقَة] {e}");
        Assert.Equal((MeterRules.ReviewCreate, "store-a", $"review.create:store-a:{review.Id:D}", "reviews", MeterRules.ReviewSource, review.Id.ToString("D")),
                     (e.Unit, e.App, e.Id, e.Feature, e.SourceType, e.SourceId));
        foreach (var person in new[] { review.AuthorId, review.TargetUserId })
            Assert.DoesNotContain(person.ToString("D"), e.Id + " " + e.SourceId, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(ClassifyIn("store-a", null, new PendingDocument(review, Created: false)));
    }

    /// <summary>‏(‏2026-11-12، W13-6·٩) <b><c>chat.message</c></b>: رِسالَةٌ في <c>Inserts</c> سَطرٌ واحِدٌ في مَتجَرِ الجَلسَةِ بِمُعَرِّفِ الرِسالَة — لا المُرسِلُ ولا
    /// المُحادَثَة؛ ورائِدُ الأَعمالِ والدَورُ مِن حامِلِ المَتجَرِ نَفسِه، وحامِلُ البَذرِ لِكُلِّ مَتجَر؛ ورِسالَتانِ بِالجِسمِ نَفسِه سَطران؛ والمُستَأجِرُ الافتِراضِيُّ
    /// <c>_unresolved</c>. وتَحديثُها (<c>Store</c> رِسالَةٍ قائِمَة) صِفر.</summary>
    [Fact]
    public void A_message_in_inserts_is_one_chat_message_entry_in_the_session_store_keyed_by_the_message_id()
    {
        var conversation = Guid.NewGuid();
        var message = new Message { Id = Guid.NewGuid(), ConversationId = conversation, SenderId = Guid.NewGuid(), Body = "مَرحَباً" };
        var owner = Guid.NewGuid().ToString("D");
        var e = Assert.Single(ClassifyIn("store-a", new MeterScopeView("store-a", owner, "customer", false), new PendingDocument(message, Created: true)));
        output.WriteLine($"[rules·وَثيقَة] {e}");
        Assert.Equal((MeterRules.ChatMessage, 1L, "store-a", $"chat.message:store-a:{message.Id:D}", owner, "customer", "chat", MeterSentinels.None,
                      MeterRules.MessageSource, message.Id.ToString("D")),
                     (e.Unit, e.Quantity, e.App, e.Id, e.Entrepreneur, e.Role, e.Feature, e.Provider, e.SourceType, e.SourceId));
        foreach (var other in new[] { message.SenderId, conversation })
            Assert.DoesNotContain(other.ToString("D"), e.Id + " " + e.SourceId, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(ClassifyIn("store-a", null, new PendingDocument(message, Created: false)));

        var again = new Message { Id = Guid.NewGuid(), ConversationId = conversation, SenderId = message.SenderId, Body = message.Body };
        Assert.Equal(2, ClassifyIn("store-a", null, new PendingDocument(message, true), new PendingDocument(again, true)).Select(x => x.Id).Distinct().Count());
        var seeded = Assert.Single(ClassifyIn("ejar", SeedScope, new PendingDocument(message, true)));
        Assert.Equal(("ejar", MeterSentinels.Seed, MeterSentinels.Seed), (seeded.App, seeded.Entrepreneur, seeded.Role));
        var otherStore = Assert.Single(ClassifyIn("store-b", new MeterScopeView("store-a", owner, "customer", false), new PendingDocument(message, true)));
        Assert.Equal(("store-b", MeterSentinels.Unresolved, MeterSentinels.Unresolved), (otherStore.App, otherStore.Entrepreneur, otherStore.Role));
        Assert.Equal(MeterSentinels.Unresolved, Assert.Single(ClassifyIn("*DEFAULT*", null, new PendingDocument(message, true))).App);
    }

    /// <summary><b>حامِلٌ بِلا مَتجَرٍ يَحمِلُ دَوراً</b> (فاعِلُ المَنَصَّة، <c>FillPlatformRole</c>): دَورُه لِكُلِّ مَتجَرٍ يَكتُبُ فيه طَلَبُه، ورائِدُ أَعمالِه لا يُؤخَذُ
    /// أَبَداً — فَوِلادَةُ المَتجَرِ عَلى <c>/admin/tenants/create</c> دَورُها <c>_platform_admin</c> ومالِكُها مِن الوَثيقَة. وحامِلٌ بِمَتجَرٍ يَبقى لِمَتجَرِه، والحامِلُ
    /// الفارِغُ لا يُعطي شَيئاً.</summary>
    [Fact]
    public void A_holder_with_no_store_and_a_platform_role_gives_its_role_to_any_store_and_never_an_entrepreneur()
    {
        var actor = new MeterScopeView(null, null, MeterSentinels.PlatformAdmin, false);
        var owner = Guid.NewGuid();
        var deal = Assert.Single(Classify(actor, new PendingDocument(NewDeal("store-b"), true)));
        var store = Assert.Single(ClassifyIn("*DEFAULT*", actor, new PendingDocument(new Tenant { Id = "store-n", OwnerUserId = owner }, true)));
        output.WriteLine($"[rules·فاعِلُ المَنَصَّة] صَفقَة {deal.App}={deal.Entrepreneur}/{deal.Role} · مَتجَر {store.App}={store.Entrepreneur}/{store.Role}");
        Assert.Equal((MeterSentinels.Unresolved, MeterSentinels.PlatformAdmin), (deal.Entrepreneur, deal.Role));
        Assert.Equal((owner.ToString("D"), MeterSentinels.PlatformAdmin), (store.Entrepreneur, store.Role));
        var scoped = Assert.Single(Classify(new MeterScopeView("store-a", "owner-a", MeterSentinels.PlatformAdmin, false), new PendingDocument(NewDeal("store-b"), true)));
        Assert.Equal((MeterSentinels.Unresolved, MeterSentinels.Unresolved), (scoped.Entrepreneur, scoped.Role));
        var empty = Assert.Single(Classify(new MeterScopeView(null, null, null, false), new PendingDocument(NewDeal("store-b"), true)));
        Assert.Equal((MeterSentinels.Unresolved, MeterSentinels.Unresolved), (empty.Entrepreneur, empty.Role));
    }

    /// <summary><b>القاعِدَةُ والمُعجَمُ والدَفتَرُ يَقولونَ الشَيءَ نَفسَه لِوَحَداتِ الوَثائِق</b>: كُلُّ وَحدَةٍ تَشغيلٌ عِندَ حَدِّ الحِفظِ بِمِيزَتِها وقارِئِ مُطابَقَتِها،
    /// وصَفُّ نَوعِها «مَعدودٌ» بِوَحدَتِها وشَكلِ وِلادَتِها. يَطبَعُ ما طابَقَ ويَفشَلُ عَلى الصِفر.</summary>
    [Fact]
    public void Every_document_unit_agrees_with_its_lexicon_row_and_its_classification_row()
    {
        (Type Type, string Unit, MeterCreation Creation, string Feature, string Reconcile)[] units =
        [
            (typeof(Tenant), MeterRules.StoreCreate, MeterCreation.Insert, "tenants", MeterReconcilers.TenantIds),
            (typeof(StudioUser), MeterRules.StudioAccountCreate, MeterCreation.OptimisticStore, "studio.build", MeterReconcilers.StudioUserIds),
            (typeof(User), MeterRules.MemberJoin, MeterCreation.OptimisticStore, "auth", MeterReconcilers.UserIds),
            (typeof(Review), MeterRules.ReviewCreate, MeterCreation.Insert, "reviews", MeterReconcilers.ReviewIds),
            // ‏(‏2026-11-12، W13-6·٩) وَحدَةُ الرِسالَة.
            (typeof(Message), MeterRules.ChatMessage, MeterCreation.Insert, "chat", MeterReconcilers.MessageIds),
        ];
        foreach (var (type, code, creation, feature, reconcile) in units)
        {
            var unit = MeterUnitCatalog.Default.Find(code);
            Assert.NotNull(unit);
            Assert.Equal((MeterSides.Operation, MeterBoundaries.Persistence, MeterMeasures.Count, feature, reconcile),
                         (unit.Side, unit.Boundary, unit.Measure, unit.Feature, unit.Reconcile));
            var row = Assert.Single(MeterClassification.All, r => r.ClrType == type);
            Assert.Equal((MeterDisposition.Counted, code, (MeterCreation?)creation), (row.Disposition, row.Unit, row.Creation));
        }
        var countedDocs = MeterClassification.All.Where(r => r is { Disposition: MeterDisposition.Counted, Kind: MeterTypeKind.Document }).Select(r => r.ClrType).ToList();
        output.WriteLine($"[rules·وَثيقَة] وَحَداتُ الوَثائِق {units.Length} · وَثائِقُ مَعدودَةٌ في الدَفتَر {countedDocs.Count}: {string.Join(", ", countedDocs.Select(t => t.Name))}");
        Assert.True(countedDocs.ToHashSet().SetEquals(units.Select(u => u.Type).Append(typeof(Deal))),
                    "الوَثائِقُ المَعدودَةُ في الدَفتَرِ والوَحَداتُ هُنا تَختَلِفان: " + string.Join(", ", countedDocs.Select(t => t.Name)));
    }
}
