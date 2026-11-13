using System.Reflection;
using System.Text.RegularExpressions;
using ACommerce.Kit.Offers;
using ACommerce.Kit.Roles;
using ACommerce.Platform.Flows;
using ACommerce.Platform.Providers;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G15 — لِكُلِّ وَحدَةٍ مُصدِر، ووَحَداتُ التَدَفُّقِ مَربوطَةٌ بِوِلادَتِه، ولِكُلِّ قيمَةٍ حارِسَةٍ واضِع، ولِكُلِّ حَقلِ مِترٍ قارِئٌ في وَقتِ التَشغيل ═══
// (‏2026-11-13، الموجَة ١٣ W13-6·١٨ — ‏ADR-051 §٢٢؛ التَصميم §7 و§12؛ القاعِدَة ١). الوَحدَةُ بِلا مَوضِعٍ يُصدِرُها تَنمو بِالخَيال، والحَقلُ بِلا قارِئٍ يُصادَقُ
// ولا يُقرَأ. فَهُنا الأَربَعُ بِعَدَدِها، وكُلٌّ يَفشَلُ عَلى الصِفرِ ويُقاسُ أَحمَرَ بِعَيبٍ مَحقون:
//   ١. «وَحَداتٌ لَها مُصدِر N مِن N» — الحِفظُ: قَواعِدُ المُصَنِّفِ نَفسُها (`MeterRules.DocumentRules`/`StreamRules`) مَحلولَةً بِمِتراتِ الوِلادَة؛ والصادِرُ: مَوضِعٌ
//      في `libs`/`apps` يَضَعُ الرَمزَ (`OutboundUnits.X`) لا يَقرَؤُه؛ والحُضورُ: لا وَحدَةَ حُضورٍ اليَوم — مُصدِرُها لَقطُ البَند ١٩، ووَحدَتُه تَدخُلُ مَعَه.
//   ٢. «bound 4 · pinned 6» — وَحدَةُ حِفظٍ مِيزَتُها اسمُ تَدَفُّقٍ (مِرساةُ `flow`، التَصميم §3.2) مَربوطَةٌ: مِترُ وِلادَةِ ذلكَ التَدَفُّقِ هُوَ هِيَ، والكودُ يَعرِفُ أَنّ نَوعَها
//      يولَدُ فيه؛ والسِتُّ الباقِيَةُ لا تَدَفُّقَ لَها (عَشَرَةُ مِلَفّات) — مُثَبَّتَةٌ بِأَسمائِها وسَبَبِ كُلٍّ، قائِمَةٌ تَنكَمِشُ ولا تَنمو.
//   ٣. «قِيَمٌ حارِسَة 9 · لَها واضِع 9» — بِأَداةِ `MeterSentinelSetterTests` نَفسِها (الواضِعُ بِشَكلِه لا بِاسمِه).
//   ٤. «حُقولُ المِتر 3 · لَها قارِئٌ في وَقتِ التَشغيل 3» — مِن الـIL المَبنِيّ (`MeterTestSeamTests.References`)، خارِجَ نَوعِ الحَقلِ نَفسِه (مُساواتُه وطِباعَتُه لَيسَتا
//      قِراءَة)؛ وسِلسِلَةُ «المُستَمِعُ يَقرَأُ مِتراتِ الوِلادَةِ عِندَ تَركيبِه» حَلقَةً حَلقَة.
// ‏(‏2026-11-13، W13-6·١٩ — ‏ADR-051 §٢٣؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **ودَخَلَت وَحَداتُ الحُضورِ التِسعُ مَعَ مُصدِرِها**: مُصدِرُها يُشَغَّلُ لا يُبحَثُ عَنه —
//   مُخَطِّطُ اللَقطَةِ (`PresenceReader.Entries`) عَلى مَتاجِرَ مَقروءَةٍ تَحمِلُ كُلَّ ما يُعَدّ (`PresenceReaderTests.Fixture`)، وما يُنتِجُه هُوَ المُصدَر؛ وقارِئا مِترَي
//   المُزَوِّدِ والدَورِ في وَقتِ التَشغيلِ صارا اللَقطَةَ نَفسَها (دَينُ §٢٠–§٢١ سُدِّد): `PresenceReader.Entries` يَقرَأُ `ProviderDefinition.Meter`، و`PresenceDefinitions`
//   يُنادي `TenantRoleSet.MeterOf`.

public class MeterEmitterTests(ITestOutputHelper output)
{
    // ─── ١. لِكُلِّ وَحدَةٍ مُصدِر ─────────────────────────────────────────

    /// <summary>وَحَداتُ الحِفظِ الَّتي يُصدِرُها المُصَنِّف: قَواعِدُه نَفسُها (ما يَقرَؤُه <c>Classify</c>) مَحلولَةً بِمِتراتِ وِلادَةٍ مُعطاة.</summary>
    internal static IReadOnlySet<string> PersistenceEmitters(MeterGenesis genesis)
        => MeterRules.DocumentRules.Values.Concat(MeterRules.StreamRules.Values)
            .Select(b => b.UnitWith(genesis)).OfType<string>().ToHashSet(StringComparer.Ordinal);

    /// <summary>ثَوابِتُ <see cref="OutboundUnits"/> — الاسمُ والرَمز.</summary>
    internal static IReadOnlyDictionary<string, string> OutboundConstants()
        => typeof(OutboundUnits).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!, StringComparer.Ordinal);

    /// <summary>
    /// <para>لِكُلِّ ثابِتٍ صادِر: كَم مَوضِعاً <b>يَضَعُه</b> في الكود — <c>OutboundUnits.{اسم}</c> بِلا تَعليقات، لا يُقارَنُ ولا يُطابَقُ ولا يُوصَلُ مِنه إلى عُضو، وخارِجَ
    /// مُجَلَّدَي القُرّاءِ ومِلَفِّ تَعريفِه — بِشَكلِ واضِعِ <c>MeterSentinelSetterTests</c> نَفسِه.</para>
    /// </summary>
    internal static IReadOnlyDictionary<string, int> OutboundSites(IEnumerable<string> names, IEnumerable<(string Path, string Text)> files)
    {
        var counts = names.ToDictionary(n => n, _ => 0, StringComparer.Ordinal);
        foreach (var (path, text) in files)
        {
            if (MeterSentinelSetterTests.IsReaderPath(path)) continue;
            var code = MeterListenerShapeTests.CodeOf(text);
            foreach (var n in counts.Keys.ToList())
                foreach (Match m in Regex.Matches(code, $@"\bOutboundUnits\.{n}\b"))
                    if (!MeterSentinelSetterTests.IsReaderUse(code, m.Index, m.Index + m.Length)) counts[n]++;
        }
        return counts;
    }

    /// <summary>‏(W13-6·١٩) وَحَداتُ الحُضورِ الَّتي يُصدِرُها مُخَطِّطُ اللَقطَة — يُشَغَّلُ عَلى مَتاجِرَ تَحمِلُ كُلَّ ما يُعَدُّ لا يُبحَثُ عَن اسمِه.</summary>
    internal static IReadOnlySet<string> PresenceEmitters()
        => PresenceReader.Entries(new DateOnly(2026, 11, 13), PresenceReaderTests.Fixture(), PresenceDefinitions.Platform, MeterUnitCatalog.Default)
            .Select(e => e.Unit).ToHashSet(StringComparer.Ordinal);

    /// <summary>وَحَداتُ المُعجَمِ الَّتي لا مُصدِرَ لَها.</summary>
    internal static IReadOnlyList<string> Orphans(IEnumerable<MeterUnit> units, IReadOnlySet<string> emitted)
        => [.. units.Where(u => !emitted.Contains(u.Code)).Select(u => u.Code)];

    /// <summary>مِلَفّاتُ الكودِ في <c>libs</c> و<c>apps</c> (‎.cs و‎.razor) بِلا <c>bin</c>/<c>obj</c> — بِلا مِلَفٍّ مُستَثنى.</summary>
    internal static IReadOnlyList<(string Path, string Text)> CodeFiles(string? except = null)
        => [.. new[] { "libs", "apps" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, d), "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && Path.GetFileName(f) != except)
            .Select(f => (f, File.ReadAllText(f)))];

    [Fact]
    public void Every_unit_of_the_lexicon_has_an_emitter()
    {
        var units = MeterUnitCatalog.Default.Units;
        var persistence = PersistenceEmitters(MeterGenesis.Default);
        var constants = OutboundConstants();
        var files = CodeFiles(except: "ProviderNames.cs");
        var sites = OutboundSites(constants.Keys, files);
        var outbound = sites.Where(s => s.Value > 0).Select(s => constants[s.Key]).ToHashSet(StringComparer.Ordinal);
        var emitted = persistence.Concat(outbound).Concat(PresenceEmitters()).ToHashSet(StringComparer.Ordinal);

        int Of(string boundary) => units.Count(u => u.Boundary == boundary && emitted.Contains(u.Code));
        int Total(string boundary) => units.Count(u => u.Boundary == boundary);
        output.WriteLine($"[G15] وَحَداتٌ لَها مُصدِر {units.Count(u => emitted.Contains(u.Code))} مِن {units.Count} — حِفظ {Of(MeterBoundaries.Persistence)}/{Total(MeterBoundaries.Persistence)}"
                         + $" · صادِر {Of(MeterBoundaries.Outbound)}/{Total(MeterBoundaries.Outbound)} · حُضور {Of(MeterBoundaries.Presence)}/{Total(MeterBoundaries.Presence)}");
        output.WriteLine($"[G15] مِلَفّاتٌ مَقروءَة {files.Count} · ثَوابِتُ صادِرَة {constants.Count} · مَواضِعُ تَضَعُها: "
                         + string.Join(" · ", sites.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => $"{constants[s.Key]} {s.Value}")));
        Assert.True(units.Count > 0 && files.Count > 400 && constants.Count > 0, $"أَداةٌ عَمياء: {units.Count} وَحدَة و{files.Count} مِلَفّاً و{constants.Count} ثابِتاً.");

        var orphans = Orphans(units, emitted);
        Assert.True(orphans.Count == 0, "وَحدَةٌ في المُعجَمِ بِلا مُصدِر — تَدخُلُه مَعَ مَوضِعِ عَدِّها لا قَبلَه (القاعِدَة ١): " + string.Join("، ", orphans));
        // ‏وثابِتٌ صادِرٌ بِلا صَفٍّ في المُعجَمِ أَو بِلا واضِعٍ لا يَبقى.
        Assert.DoesNotContain(constants.Values, v => MeterUnitCatalog.Default.Find(v) is not { Boundary: MeterBoundaries.Outbound });
        Assert.Empty(sites.Where(s => s.Value == 0).Select(s => s.Key));
    }

    /// <summary><b>حَقنُ العَيب</b> (القاعِدَة ١٠): صَفٌّ في المُعجَمِ بِلا مُصدِرٍ تَراه الأَداةُ يَتيماً، وثابِتٌ صادِرٌ لا يَذكُرُه إلّا تَعليقٌ ومُقارَنَةٌ يُعَدُّ صِفراً.</summary>
    [Fact]
    public void An_injected_lexicon_row_without_an_emitter_and_a_constant_without_a_setter_are_seen()
    {
        var injected = new MeterUnit
        {
            Code = "demo.orphan", Side = MeterSides.Operation, Boundary = MeterBoundaries.Persistence, Measure = MeterMeasures.Count,
            Feature = "deals", Reconcile = MeterReconcilers.DealIds, Source = "مِجَسٌّ في الاختِبار",
        };
        var emitted = PersistenceEmitters(MeterGenesis.Default);
        Assert.Equal(new[] { "demo.orphan" }, Orphans([.. MeterUnitCatalog.Default.Units.Where(u => u.Boundary == MeterBoundaries.Persistence), injected], emitted));

        var sites = OutboundSites(["X", "Y"], [("libs/k/Any.cs", "var c = new ProviderCall(OutboundUnits.X, slug, id); // OutboundUnits.Y\nif (c.Unit == OutboundUnits.Y) { }")]);
        Assert.Equal((1, 0), (sites["X"], sites["Y"]));
        Assert.Equal(0, OutboundSites(["X"], [("libs/t/Services/Queries/Q.cs", "var c = new ProviderCall(OutboundUnits.X, slug, id);")])["X"]);

        // ‏(W13-6·١٩) ووَحدَةُ حُضورٍ في المُعجَمِ لا يُنتِجُها المُخَطِّطُ يَتيمَة.
        var ghost = new MeterUnit
        {
            Code = "presence.ghost", Side = MeterSides.Technology, Boundary = MeterBoundaries.Presence, Measure = MeterMeasures.Count,
            Feature = "tenants", Reconcile = MeterReconcilers.PresenceClaims, Source = "مِجَسٌّ في الاختِبار",
        };
        Assert.Equal(new[] { "presence.ghost" }, Orphans([.. MeterUnitCatalog.Default.Units.Where(u => u.Boundary == MeterBoundaries.Presence), ghost], PresenceEmitters()));
    }

    // ─── ٢. bound 4 · pinned 6 ─────────────────────────────────────────

    /// <summary>
    /// <para><b>وَحَداتُ الحِفظِ الَّتي لا تَدَفُّقَ يَصِفُ وِلادَتَها</b> — مُثَبَّتَةٌ بِاسمِها وسَبَبِها، <b>قائِمَةٌ تَنكَمِشُ ولا تَنمو</b> (التَصميم §7): مِلَفُّ تَدَفُّقٍ
    /// يُكتَبُ لِإحداها يَنقُلُها إلى المَربوطَة، ووَحدَةُ حِفظٍ جَديدَةٌ بِلا تَدَفُّقٍ تُضافُ هُنا بِسَطرٍ يُرى في المُراجَعَة.</para>
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> Pinned = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["listing.create"] = "الإعلانُ يولَدُ بِحَدَثٍ في مَجراه ولا تَدَفُّقَ يَصِفُ حالاتِه (العَشَرَةُ لا تَحوي listings) — الوِلادَةُ ListingCreated ثابِتَةٌ في المُصَنِّف.",
        ["chat.message"] = "الرِسالَةُ وَثيقَةٌ تُدرَجُ ولا حالاتِ لَها تَتَنَقَّلُ بَينَها — لا تَدَفُّق.",
        ["review.create"] = "التَقييمُ وَثيقَةٌ تُدرَجُ ويُرَدُّ عَلَيها ويُخفى بِحِفظٍ لا بِانتِقالٍ مَوصوف — لا تَدَفُّق.",
        ["member.join"] = "انضِمامُ العُضوِ حِسابٌ يُنشَأُ عِندَ أَوَّلِ دُخولٍ لا تَدَفُّقٌ مَوصوف.",
        ["store.create"] = "المَتجَرُ يُنشَأُ بِإدراجِ وَثيقَتِه مِن أَبوابٍ وبَذرٍ — لا تَدَفُّقَ يَصِفُ حالاتِه (التَعليقُ والاشتِراكُ حُقولٌ لا انتِقالاتٌ مَوصوفَة).",
        ["studio.account.create"] = "حِسابُ الاستوديو يُنشَأُ عِندَ التَحَقُّقِ مِن الرَمز — لا تَدَفُّق.",
    };

    [Fact]
    public void The_flow_anchored_units_are_bound_to_genesis_and_the_rest_are_pinned_by_name()
    {
        var flows = FlowDefinitionLoader.LoadAll();
        var flowNames = flows.Select(f => f.Flow).ToHashSet(StringComparer.Ordinal);
        var genesis = MeterGenesis.Default;
        var persistence = MeterUnitCatalog.Default.Units.Where(u => u.Boundary == MeterBoundaries.Persistence).ToList();
        var rules = MeterRules.DocumentRules.Concat(MeterRules.StreamRules).ToList();
        var codeFlows = rules.Where(r => r.Value.Flow is not null).Select(r => r.Value.Flow!).ToHashSet(StringComparer.Ordinal);
        var codeFixed = rules.Where(r => r.Value.Unit is not null).Select(r => r.Value.Unit!).ToHashSet(StringComparer.Ordinal);

        var anchored = persistence.Where(u => flowNames.Contains(u.Feature!)).ToList();
        var bound = anchored.Where(u => genesis.UnitOf(u.Feature!) == u.Code && codeFlows.Contains(u.Feature!)).Select(u => u.Code).ToList();
        var unbound = persistence.Where(u => !flowNames.Contains(u.Feature!)).Select(u => u.Code).ToHashSet(StringComparer.Ordinal);

        output.WriteLine($"[G15] bound {bound.Count} · pinned {Pinned.Count} — تَدَفُّقات {flows.Count} · وَحَداتُ حِفظ {persistence.Count}");
        foreach (var u in anchored) output.WriteLine($"[G15]   مَربوطَة {u.Code} ⇐ وِلادَةُ «{u.Feature}» = «{genesis.UnitOf(u.Feature!) ?? FlowMeter.None}»");
        foreach (var p in Pinned) output.WriteLine($"[G15]   مُثَبَّتَة {p.Key} — {p.Value}");
        Assert.True(flows.Count > 0 && persistence.Count > 0, "أَداةٌ عَمياء: صِفرُ تَدَفُّقٍ أَو وَحدَةِ حِفظ.");

        // كُلُّ وَحدَةٍ مِرساتُها تَدَفُّقٌ مَربوطَةٌ بِوِلادَتِه — والكودُ يَعرِفُ أَنّ نَوعَها يولَدُ فيه.
        Assert.Equal(anchored.Select(u => u.Code), bound);
        Assert.Equal(4, bound.Count);
        // ومِتراتُ الوِلادَةِ الَّتي تُسَمّي وَحدَةً هِيَ المَربوطَةُ بِلا زِيادَة: لا مِلَفَّ يَربِطُ وِلادَتَه بِوَحدَةٍ مُثَبَّتَة.
        Assert.Equal(bound.Order(StringComparer.Ordinal), flowNames.Select(genesis.UnitOf).OfType<string>().Order(StringComparer.Ordinal));
        Assert.True(codeFlows.SetEquals(anchored.Select(u => u.Feature!)), "الكودُ يَربِطُ نَوعاً بِتَدَفُّقٍ لا تُرسيه وَحدَة: " + string.Join("، ", codeFlows));

        // والمُثَبَّتَةُ هِيَ بِالضَبطِ وَحَداتُ الحِفظِ بِلا تَدَفُّق، وثَوابِتُ المُصَنِّف — لا صَفَّ قَديمٌ ولا وَحدَةٌ بِلا صَفّ.
        var stale = Pinned.Keys.Where(k => !unbound.Contains(k)).ToList();
        var unpinned = unbound.Where(u => !Pinned.ContainsKey(u)).ToList();
        Assert.True(stale.Count == 0, "صَفٌّ مُثَبَّتٌ صارَ لَه تَدَفُّقٌ أَو خَرَجَ مِن المُعجَم — يُحذَفُ (القائِمَةُ تَنكَمِش): " + string.Join("، ", stale));
        Assert.True(unpinned.Count == 0, "وَحدَةُ حِفظٍ بِلا تَدَفُّقٍ وبِلا صَفٍّ هُنا: " + string.Join("، ", unpinned));
        Assert.True(codeFixed.SetEquals(Pinned.Keys), "ثَوابِتُ المُصَنِّفِ غَيرُ المُثَبَّتَة: " + string.Join("، ", codeFixed.Order(StringComparer.Ordinal)));
        Assert.All(Pinned.Values, why => Assert.False(string.IsNullOrWhiteSpace(why)));
        Assert.Equal(6, Pinned.Count);

        // وأَسماءُ الوَحَداتِ في المُصَنِّفِ هِيَ ما تُسَمّيه المِلَفّات.
        Assert.Equal((MeterRules.DealCreate, MeterRules.OfferSubmit, MeterRules.TicketOpen, MeterRules.MemberPlanSubscribe),
                     (genesis.UnitOf(MeterRules.DealFlow), genesis.UnitOf(MeterRules.OfferFlow), genesis.UnitOf(MeterRules.TicketFlow), genesis.UnitOf(MeterRules.SubscriptionFlow)));
    }

    /// <summary>
    /// <para><b>المِترُ في المِلَفِّ يُغَيِّرُ ما يُعَدّ</b> — الحَقلُ لَه قارِئٌ يُقَرِّرُ لا يُصادَقُ وَحدَه: نُسخَةٌ مِن مِلَفّاتِ التَدَفُّقِ بِمِترِ وِلادَةِ <c>deal_status</c>
    /// و<c>offer</c> «none» تُسكِتُ <c>deal.create</c> و<c>offer.submit</c> في المُصَنِّف، والمِلَفّاتُ كَما هِيَ تَعُدُّهُما — والوَحَداتُ الثابِتَةُ لا تَتَأَثَّر.</para>
    /// </summary>
    [Fact]
    public void A_genesis_meter_changed_in_a_copied_flow_changes_what_the_classifier_counts()
    {
        var files = FlowDefinitionLoader.LoadAll();
        var silenced = MeterGenesis.From(files.Select(f => f.Flow is MeterRules.DealFlow or MeterRules.OfferFlow
            ? f with { Transitions = [.. f.Transitions.Select(t => t.From == FlowVocabulary.Genesis ? t with { Meter = FlowMeter.None } : t)] }
            : f), MeterUnitCatalog.Default);

        var deal = new Deal { Id = Guid.NewGuid(), TenantSlug = "store-a", InitiatorId = Guid.NewGuid(), CounterpartyId = Guid.NewGuid() };
        var at = new DateTime(2026, 11, 13, 9, 0, 0, DateTimeKind.Utc);
        var offer = new PendingEvent(Guid.NewGuid(), new OfferSubmitted(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "عارِض", 1m, null, 24.7, 46.6, at.AddMinutes(15), at));
        var listing = new PendingEvent(Guid.NewGuid(), new ACommerce.Kit.Listings.ListingCreated(Guid.NewGuid(), "store-a", "إعلانٌ", null, 1m, "general", null, null, [], at));
        var pending = new PendingSnapshot("store-a", [new PendingDocument(deal, Created: true)],
            [new PendingStream("store-a", Started: true, [offer, listing])]);

        var counted = MeterRules.Classify(pending, null, MeterUnitCatalog.Default).Select(e => e.Unit).Order(StringComparer.Ordinal).ToList();
        var asFiles = MeterRules.Classify(pending, null, MeterUnitCatalog.Default, MeterGenesis.Default).Select(e => e.Unit).Order(StringComparer.Ordinal).ToList();
        var quiet = MeterRules.Classify(pending, null, MeterUnitCatalog.Default, silenced).Select(e => e.Unit).ToList();
        output.WriteLine($"[G15] المِلَفّات: {string.Join(", ", counted)} · نُسخَةٌ بِوِلادَتَينِ none: {string.Join(", ", quiet)}");
        Assert.Equal(new[] { "deal.create", "listing.create", "offer.submit" }, counted);
        Assert.Equal(counted, asFiles);
        Assert.Equal(new[] { "listing.create" }, quiet);
        Assert.Equal(((string?)null, (string?)null, (string?)"ticket.open"), (silenced.UnitOf(MeterRules.DealFlow), silenced.UnitOf(MeterRules.OfferFlow), silenced.UnitOf(MeterRules.TicketFlow)));
        Assert.DoesNotContain(typeof(OfferSubmitted), MeterRules.StreamBirthsOf(silenced).Keys);
        Assert.Equal(MeterRules.StreamBirths.OrderBy(b => b.Value, StringComparer.Ordinal), MeterRules.StreamBirthsOf(MeterGenesis.Default).OrderBy(b => b.Value, StringComparer.Ordinal));
    }

    // ─── ٣. لِكُلِّ قيمَةٍ حارِسَةٍ واضِع ────────────────────────────────

    [Fact]
    public void Every_sentinel_has_a_setter_site()
    {
        var declared = typeof(MeterSentinels).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => f.Name).ToList();
        var files = CodeFiles(except: "MeterSentinels.cs");
        var setters = MeterSentinelSetterTests.SetterSites(declared, files);
        output.WriteLine($"[G15] قِيَمٌ حارِسَة {declared.Count} · لَها واضِع {setters.Count(s => s.Value > 0)} — "
                         + string.Join(" · ", setters.Select(s => $"{s.Key} {s.Value}")));
        Assert.True(declared.Count > 0 && files.Count > 400, $"أَداةٌ عَمياء: {declared.Count} قيمَة و{files.Count} مِلَفّاً.");
        Assert.Equal(MeterSentinels.All.Count, declared.Count);
        Assert.Empty(setters.Where(s => s.Value == 0).Select(s => s.Key));
    }

    // ─── ٤. لِكُلِّ حَقلِ مِترٍ قارِئٌ في وَقتِ التَشغيل ─────────────────────

    /// <summary>حُقولُ المِترِ الثَلاثَةُ في التَعريفات (التَصميم §7): المُزَوِّدُ (البَند ١٦) والدَورُ (البَند ١٧) وانتِقالُ التَدَفُّق (البَند ١٨).</summary>
    internal static readonly IReadOnlyList<PropertyInfo> MeterFields =
    [
        typeof(ProviderDefinition).GetProperty(nameof(ProviderDefinition.Meter))!,
        typeof(RoleDefinition).GetProperty(nameof(RoleDefinition.Meter))!,
        typeof(FlowTransition).GetProperty(nameof(FlowTransition.Meter))!,
    ];

    /// <summary>النَوعُ الَّذي كُتِبَ فيه الجِسم — ما يُوَلِّدُه المُتَرجِمُ (مُغلَقاتُ اللامدا وآلاتُ async) يُرَدُّ إلى النَوعِ الحاوي.</summary>
    private static Type Owner(MethodBase m)
    {
        var t = m.DeclaringType!;
        while (t.DeclaringType is not null && t.Name.Contains('<', StringComparison.Ordinal)) t = t.DeclaringType;
        return t;
    }

    private static string NameOf(MethodBase m)
    {
        var name = m.Name;
        if (m.DeclaringType!.Name.Contains('<', StringComparison.Ordinal) || name.Contains('<', StringComparison.Ordinal))
        {
            var inner = Regex.Match(m.DeclaringType.Name + name, "<(?<n>[A-Za-z_][A-Za-z0-9_]*)>");
            if (inner.Success) name = inner.Groups["n"].Value;
        }
        return $"{Owner(m).Name}.{name}";
    }

    private static bool Same(MethodBase a, MethodBase b) => a.Module == b.Module && a.MetadataToken == b.MetadataToken;

    /// <summary>قُرّاءُ جالِبِ خاصِّيَّةٍ في تَجميعاتٍ مُعطاة، خارِجَ نَوعِها — مُساواةُ السِجِلِّ وطِباعَتُه وتَفكيكُه لَيسَت قِراءَة.</summary>
    internal static IReadOnlyList<string> Readers(PropertyInfo field, IEnumerable<(MethodBase Caller, MethodBase Callee)> references)
        => [.. references.Where(r => Same(r.Callee, field.GetMethod!) && Owner(r.Caller) != field.DeclaringType)
               .Select(r => NameOf(r.Caller)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>خاصِّيَّةٌ في نَوعٍ يُعرِّفُه الاختِبار — لا يَقرَؤُها كودُ الإنتاج: الضابِطُ السالِبُ لِلأَداة.</summary>
    private sealed record Orphan(string? Meter);

    [Fact]
    public void Every_meter_field_has_a_runtime_reader_outside_tests_and_the_listener_reads_the_genesis_at_composition()
    {
        var bodies = 0;
        var assemblies = MeterTestSeamTests.ProductionAssemblies();
        var references = assemblies.SelectMany(a => MeterTestSeamTests.References(a, n => bodies += n)).ToList();
        var readers = MeterFields.ToDictionary(f => $"{f.DeclaringType!.Name}.{f.Name}", f => Readers(f, references), StringComparer.Ordinal);
        output.WriteLine($"[G15] تَجميعاتٌ مَقروءَة {assemblies.Count} · أَجسام {bodies} · مَراجِع {references.Count}");
        output.WriteLine($"[G15] حُقولُ المِتر {readers.Count} · لَها قارِئٌ في وَقتِ التَشغيل {readers.Count(r => r.Value.Count > 0)}: "
                         + string.Join(" · ", readers.Select(r => $"{r.Key} ← {string.Join(", ", r.Value)}")));
        Assert.True(assemblies.Count > 10 && references.Count > 10_000, $"أَداةٌ عَمياء: {assemblies.Count} تَجميعاً و{references.Count} مَرجِعاً.");
        Assert.Equal(3, readers.Count);
        Assert.Empty(readers.Where(r => r.Value.Count == 0).Select(r => r.Key));
        Assert.Empty(Readers(typeof(Orphan).GetProperty(nameof(Orphan.Meter))!, references));

        // ‏(W13-6·١٩) ودَينُ §٢٠–§٢١ سُدِّد: لَقطُ الحُضورِ يَقرَأُ مِترَ المُزَوِّدِ، ويُنادي المِترَ المُشتَقَّ لِلدَورِ — قارِئانِ في وَقتِ التَشغيلِ غَيرُ بَوّابَةِ الإقلاع.
        var meterOf = typeof(ACommerce.Kit.Roles.TenantRoleSet).GetMethod(nameof(ACommerce.Kit.Roles.TenantRoleSet.MeterOf))!;
        var meterOfCallers = references.Where(r => Same(r.Callee, meterOf) && Owner(r.Caller) != typeof(ACommerce.Kit.Roles.TenantRoleSet))
            .Select(r => Owner(r.Caller).Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        output.WriteLine($"[G15] قُرّاءُ الحُضور: ProviderDefinition.Meter ← {string.Join(", ", readers["ProviderDefinition.Meter"])} · TenantRoleSet.MeterOf ← {string.Join(", ", meterOfCallers)}");
        Assert.Contains($"{nameof(PresenceReader)}.{nameof(PresenceReader.Entries)}", readers["ProviderDefinition.Meter"]);
        Assert.Contains(nameof(PresenceDefinitions), meterOfCallers);

        // سِلسِلَةُ «المُستَمِعُ يَقرَأُ مِتراتِ الوِلادَةِ عِندَ تَركيبِه» حَلقَةً حَلقَة: البانِي بِلا وَسيطٍ ← المِلَفّات ← القِراءَة ← مِترُ الانتِقال.
        bool Calls(MethodBase from, MethodBase to) => references.Any(r => Same(r.Callee, to)
            && (Same(r.Caller, from) || NameOf(r.Caller) == $"{Owner(from).Name}.{from.Name}"));
        var hops = new (MethodBase From, MethodBase To)[]
        {
            (typeof(UsageListener).GetConstructor(Type.EmptyTypes)!, typeof(MeterGenesis).GetProperty(nameof(MeterGenesis.Default))!.GetMethod!),
            (typeof(MeterGenesis).GetMethod(nameof(MeterGenesis.From))!, typeof(FlowMeter).GetMethod(nameof(FlowMeter.GenesisMeters))!),
            (typeof(MeterGenesis).GetMethod(nameof(MeterGenesis.From))!, typeof(FlowMeter).GetMethod(nameof(FlowMeter.Validate))!),
            (typeof(FlowMeter).GetMethod(nameof(FlowMeter.GenesisMeters))!, typeof(FlowTransition).GetProperty(nameof(FlowTransition.Meter))!.GetMethod!),
        };
        foreach (var (from, to) in hops)
            output.WriteLine($"[G15]   {from.DeclaringType!.Name}.{from.Name} → {to.DeclaringType!.Name}.{to.Name}: {Calls(from, to)}");
        Assert.All(hops, h => Assert.True(Calls(h.From, h.To), $"{h.From.DeclaringType!.Name}.{h.From.Name} لا يَبلُغُ {h.To.DeclaringType!.Name}.{h.To.Name}"));
        // والمَلَفّاتُ نَفسُها تُقرَأُ داخِلَ القارِئِ الكَسول (Lazy) لِـDefault.
        Assert.True(references.Any(r => Owner(r.Caller) == typeof(MeterGenesis)
            && Same(r.Callee, typeof(FlowDefinitionLoader).GetMethod(nameof(FlowDefinitionLoader.LoadAll))!)), "MeterGenesis لا يَقرَأُ مِلَفّاتِ التَدَفُّق.");
    }
}
