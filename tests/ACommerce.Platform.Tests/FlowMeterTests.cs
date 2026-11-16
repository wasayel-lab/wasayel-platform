using System.Text.Json;
using ACommerce.Platform.Flows;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>G7 — مِترُ انتِقالِ التَدَفُّق</b> (‏2026-11-13، الموجَة ١٣ W13-6·١٨ — ‏ADR-051 §٢٢؛ التَصميم §7 و§12): لِكُلِّ رَمزٍ مِن الثَلاثَةِ اختِبارٌ موجِبٌ
/// وسالِب (القاعِدَة ٤)، وحارِسُ عَمىً (كُلُّ رَمزٍ مُعلَنٍ يُنتِجُه خَرق)، والانتِقالاتُ السِتَّةُ والثَلاثونَ في المِلَفّاتِ العَشَرَةِ تُفحَصُ واحِداً واحِداً ويُطبَعُ
/// عَدَدُها ويَفشَلُ عَلى الصِفر، وقِيَمُها مُثَبَّتَة؛ والمُساواةُ تَرى المِترَ (انتِقالانِ يَختَلِفانِ فيه وَحدَه غَيرُ مُتَساوِيَين) فَبُرهانُ «<c>ApprovalFlow</c> يُطابِقُ
/// مِلَفَّه» لَم يَعُد أَعمى عَنه؛ و<c>ApprovalFlow</c> يَحمِلُ «none»؛ وقِراءَةُ المِلَفّاتِ عِندَ التَركيبِ تَرمي بِكُلِّ خَرق.</para>
/// </summary>
public class FlowMeterTests(ITestOutputHelper output)
{
    private static readonly FlowMeterUnits Units = MeterGenesis.UnitsOf(MeterUnitCatalog.Default);

    private static FlowTransition T(string? meter, string from = "a", string to = "b")
        => new(from, to, "system", new FlowLabel("مِجَسّ"), null, meter);

    /// <summary>تَدَفُّقٌ صَغيرٌ بِانتِقالاتٍ مُعطاة — لِلمُصادِقِ وَحدَه، لا لِلمُصادِقِ البُنيَوِيّ.</summary>
    private static FlowDefinition Flow(params FlowTransition[] transitions) => new(
        "meter_probe", new FlowLabel("مِجَسّ"),
        [new FlowState("a", new FlowLabel("أ")), new FlowState("b", new FlowLabel("ب"), IsTerminal: true)],
        transitions, "a");

    private static string[] CodesOf(string? meter) => FlowMeter.Validate(Flow(T(meter)), Units).Select(v => v.Code).ToArray();

    // ─── المَعجَمُ والرُموز ─────────────────────────────────────────────

    [Fact]
    public void Three_codes_and_the_none_value_are_declared_and_none_repeats()
    {
        Assert.Equal(new[] { "meter_missing", "meter_unit_unknown", "meter_unit_not_persistence" }, FlowMeter.Codes);
        Assert.Equal(FlowMeter.Codes.Count, FlowMeter.Codes.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("none", FlowMeter.None);
        // ‏«none» لا تُصادِفُ رَمزَ وَحدَة: نَمَطُ الرُموزِ فيه نُقطَةٌ واحِدَةٌ فَأَكثَر (MeterUnitValidator)، فَلا يَلتَبِسُ «لا يُعَدّ» بِوَحدَة.
        Assert.Null(MeterUnitCatalog.Default.Find(FlowMeter.None));
        Assert.NotEmpty(MeterUnitValidator.Validate(new MeterUnit
        {
            Code = FlowMeter.None, Side = MeterSides.Operation, Boundary = MeterBoundaries.Persistence, Measure = MeterMeasures.Count,
            Feature = "deal_status", Reconcile = MeterReconcilers.DealIds, Source = "مِجَسّ",
        }));
        // ‏واسمُ حَدِّ الحِفظِ الَّذي يُعطيه المُنادي هُوَ اسمُه في المُعجَم.
        Assert.Equal(MeterBoundaries.Persistence, Units.Persistence);
        Assert.Equal(MeterUnitCatalog.Default.Units.Count, Units.BoundaryOf.Count);
    }

    // ─── meter_missing ─────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void meter_missing_fires_on_an_absent_or_blank_meter(string? meter)
        => Assert.Equal(new[] { "meter_missing" }, CodesOf(meter));

    [Theory]
    [InlineData("none")]
    [InlineData("deal.create")]
    [InlineData("chat.message")]
    public void meter_missing_does_not_fire_on_a_written_meter(string meter)
        => Assert.DoesNotContain("meter_missing", CodesOf(meter));

    // ─── meter_unit_unknown ────────────────────────────────────────────

    [Theory]
    [InlineData("deal.created")]
    [InlineData("Deal.create")]
    [InlineData("None")]
    [InlineData("none ")]
    // ‏(‏2026-11-13، W13-6·١٩) `presence.role` دَخَلَت المُعجَمَ حُضوراً فَصارَ رَمزُها `meter_unit_not_persistence` (الصَفُّ أَدناه)؛ ومَكانَها اسمُ حُضورٍ لَيسَ في المُعجَم.
    [InlineData("presence.roles")]
    public void meter_unit_unknown_fires_on_a_value_that_is_neither_a_unit_nor_none(string meter)
        => Assert.Equal(new[] { "meter_unit_unknown" }, CodesOf(meter));

    [Fact]
    public void meter_unit_unknown_never_fires_on_a_lexicon_unit_or_on_none()
    {
        var units = MeterUnitCatalog.Default.Units;
        Assert.All(units, u => Assert.DoesNotContain("meter_unit_unknown", CodesOf(u.Code)));
        Assert.Empty(CodesOf(FlowMeter.None));
        output.WriteLine($"[G7·flow] وَحَداتُ المُعجَمِ لا تُعَدُّ مَجهولَة: {units.Count} مِن {units.Count}");
    }

    // ─── meter_unit_not_persistence ────────────────────────────────────

    /// <summary>كُلُّ وَحدَةٍ صادِرَةٍ في المُعجَمِ تُرَدُّ عَلى انتِقال — والوَحدَةُ تُعَدُّ عِندَ نِداءِ المُزَوِّدِ لا عِندَ الانتِقال.</summary>
    [Fact]
    public void meter_unit_not_persistence_fires_on_every_outbound_unit_of_the_lexicon()
    {
        var outbound = MeterUnitCatalog.Default.Units.Where(u => u.Boundary == MeterBoundaries.Outbound).ToList();
        output.WriteLine($"[G7·flow] وَحَداتٌ صادِرَةٌ مَردودَةٌ عَلى انتِقال: {outbound.Count}");
        Assert.True(outbound.Count > 0, "أَداةٌ عَمياء: صِفرُ وَحدَةٍ صادِرَة.");
        Assert.All(outbound, u => Assert.Equal(new[] { "meter_unit_not_persistence" }, CodesOf(u.Code)));
    }

    /// <summary>ووَحدَةُ حُضورٍ في مُعجَمٍ يَحمِلُها (كَما سَيَصيرُ في البَند ١٩) تُرَدُّ أَيضاً، وكُلُّ وَحدَةٍ حَدُّها الحِفظُ تُقبَل.</summary>
    [Fact]
    public void meter_unit_not_persistence_fires_on_a_presence_unit_and_never_on_a_persistence_unit()
    {
        var withPresence = new FlowMeterUnits(
            new Dictionary<string, string>(Units.BoundaryOf, StringComparer.Ordinal) { ["presence.role"] = MeterBoundaries.Presence }, Units.Persistence);
        Assert.Equal(new[] { "meter_unit_not_persistence" },
            FlowMeter.Validate(Flow(T("presence.role")), withPresence).Select(v => v.Code));

        var persistence = MeterUnitCatalog.Default.Units.Where(u => u.Boundary == MeterBoundaries.Persistence).ToList();
        output.WriteLine($"[G7·flow] وَحَداتُ الحِفظِ مَقبولَةً عَلى انتِقال: {persistence.Count}");
        Assert.True(persistence.Count >= 10, $"أَداةٌ عَمياء: {persistence.Count} وَحدَةَ حِفظ.");
        Assert.All(persistence, u => Assert.Empty(CodesOf(u.Code)));
    }

    // ─── حارِسُ العَمى، وADR-038 ──────────────────────────────────────

    /// <summary>كُلُّ رَمزٍ مُعلَنٍ يُنتِجُه مُدخَلٌ خارِق — والقائِمَةُ مِن نَتائِجِ المُدخَلاتِ لا مِن نَصٍّ بِاليَد.</summary>
    [Fact]
    public void Every_declared_code_is_actually_produced_by_some_breach()
    {
        var produced = new[] { null, "deal.created", "otp.sms.send" }.SelectMany(CodesOf).ToHashSet(StringComparer.Ordinal);
        output.WriteLine($"[G7·flow] رُموزٌ أُنتِجَت: {produced.Count} مِن {FlowMeter.Codes.Count}");
        Assert.True(produced.Count > 0, "أَداةٌ عَمياء: صِفرُ خَرقٍ أُنتِج.");
        var never = FlowMeter.Codes.Where(c => !produced.Contains(c)).ToArray();
        Assert.True(never.Length == 0, "رَمزُ خَرقٍ مُعلَنٌ لا يُنتِجُه أَيُّ مُدخَل: " + string.Join("، ", never));
    }

    /// <summary>‏ADR-038: مُصادِقُ بَياناتٍ يُجيبُ بِكُلِّ خُروقِه — خَرقٌ لِكُلِّ انتِقالٍ خارِقٍ بِتَرتيبِه، والسَليمُ لا يُخفي ما بَعدَه.</summary>
    [Fact]
    public void A_flow_answers_every_breaching_transition_in_order()
    {
        var v = FlowMeter.Validate(Flow(T(null, "a", "b"), T("none", "a", "a"), T("otp.sms.send", "b", "b"), T("deal.created", "b", "a")), Units);
        Assert.Equal(new[] { "meter_missing", "meter_unit_not_persistence", "meter_unit_unknown" }, v.Select(x => x.Code));
        Assert.False(FlowMeter.IsValid(Flow(T(null)), Units));
        Assert.True(FlowMeter.IsValid(Flow(T("none"), T("deal.create")), Units));
        Assert.All(v, x => Assert.Contains("meter_probe", x.MessageAr, StringComparison.Ordinal));
    }

    // ─── المِلَفّاتُ العَشَرَة ───────────────────────────────────────────

    /// <summary>مِترُ وِلادَةِ كُلِّ تَدَفُّق — مُثَبَّتٌ هُنا، فَتَبديلُه صَفٌّ أَحمَرُ يُرى في المُراجَعَة (التَصميم §7: أَربَعَةٌ تُسَمّي وَحدَة، والسِتَّةُ «none»).</summary>
    internal static readonly IReadOnlyDictionary<string, string> ExpectedGenesis = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["agent_tool_call"] = "none",
        ["deal_status"] = "deal.create",
        ["incubator"] = "none",
        ["offer"] = "offer.submit",
        ["report"] = "none",
        ["subscription"] = "member_plan.subscribe",
        ["tenant_role"] = "none",
        ["tenant_theme"] = "none",
        ["ticket"] = "ticket.open",
        ["trip"] = "none",
    };

    /// <summary>
    /// <para><b>كُلُّ انتِقالٍ في المِلَفّاتِ العَشَرَةِ يَحمِلُ مِتراً صالِحاً</b>، ومِترُ وِلادَةِ كُلِّ تَدَفُّقٍ مُثَبَّت، <b>وكُلُّ انتِقالٍ غَيرِ مُنشِئٍ «none»</b> —
    /// عَدُّ الانتِقالاتِ وَحدَةٌ لاحِقَة (التَصميم §5.6)، فَوَحدَةٌ عَلى انتِقالٍ لا يَقرَؤُه شَيءٌ تَكذِبُ (القاعِدَة ١). يَطبَعُ ما فَحَصَ ويَفشَلُ عَلى الصِفر.</para>
    /// </summary>
    [Fact]
    public void Every_transition_of_the_ten_files_carries_a_valid_meter_and_the_genesis_meters_are_pinned()
    {
        var flows = FlowDefinitionLoader.LoadAll();
        var transitions = 0;
        var violations = new List<string>();
        var genesisWithUnit = 0;
        var none = 0;
        foreach (var f in flows)
        {
            transitions += f.Transitions.Count;
            violations.AddRange(FlowMeter.Validate(f, Units).Select(v => $"{f.Flow}: {v.Code}"));
            none += f.Transitions.Count(t => t.Meter == FlowMeter.None);
            genesisWithUnit += FlowMeter.GenesisMeters(f).Count(m => m != FlowMeter.None);
            output.WriteLine($"[G7·flow]   {f.Flow,-16} انتِقالات {f.Transitions.Count} · وِلادَة {string.Join(",", FlowMeter.GenesisMeters(f))}");
        }
        output.WriteLine($"[G7·flow] تَدَفُّقات {flows.Count} · انتِقالاتٌ مَفحوصَة {transitions} · خُروق {violations.Count} · وِلادَةٌ بِوَحدَة {genesisWithUnit} · none {none}");
        Assert.True(flows.Count > 0 && transitions > 0, "أَداةٌ عَمياء: صِفرُ تَدَفُّقٍ أَو انتِقال.");
        Assert.Equal(36, transitions);
        Assert.Empty(violations);
        Assert.Equal((4, 32), (genesisWithUnit, none));

        Assert.Equal(ExpectedGenesis.OrderBy(p => p.Key, StringComparer.Ordinal),
                     flows.ToDictionary(f => f.Flow, f => Assert.Single(FlowMeter.GenesisMeters(f))).OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.All(flows.SelectMany(f => f.Transitions.Where(t => t.From != FlowVocabulary.Genesis).Select(t => (f.Flow, t))),
                   x => Assert.True(x.t.Meter == FlowMeter.None, $"{x.Flow}: «{x.t.From} → {x.t.To}» = «{x.t.Meter}» — الانتِقالُ غَيرُ المُنشِئِ لا يَعُدّ."));
    }

    /// <summary><b>المِفتاحُ يُقرَأُ إلى الانتِقال</b>، ومِفتاحٌ بِحالَةٍ أُخرى مَجهولٌ تَرُدُّه القِراءَةُ قَبلَ أَيِّ مُصادِق (<c>UnmappedMemberHandling.Disallow</c>)،
    /// وانتِقالٌ بِلا مِفتاحٍ يُقرَأُ <c>null</c> يَرُدُّه هذا المُصادِقُ وَحدَه — والمُصادِقُ البُنيَوِيُّ بِرُموزِه نَفسِها بِالمِفتاحِ وبِدونِه.</summary>
    [Fact]
    public void The_key_reads_into_the_transition_an_unknown_casing_is_refused_and_the_structural_validator_does_not_see_it()
    {
        const string withMeter = """
            { "flow": "meter_probe", "label": { "ar": "مِجَسّ" }, "initial": "a",
              "states": [ { "key": "a", "label": { "ar": "أ" }, "isTerminal": true } ],
              "transitions": [ { "from": "(genesis)", "to": "a", "actor": "system", "label": { "ar": "اِبدَأ" }, "effects": [], "meter": "deal.create" } ] }
            """;
        var carried = FlowDefinitionLoader.Parse(withMeter);
        var bare = FlowDefinitionLoader.Parse(withMeter.Replace(", \"meter\": \"deal.create\"", "", StringComparison.Ordinal));
        Assert.Equal("deal.create", Assert.Single(carried.Transitions).Meter);
        Assert.Null(Assert.Single(bare.Transitions).Meter);
        Assert.Empty(FlowMeter.Validate(carried, Units));
        Assert.Equal(new[] { "meter_missing" }, FlowMeter.Validate(bare, Units).Select(v => v.Code));
        Assert.Equal(FlowDefinitionValidator.Validate(bare).Select(v => v.Code), FlowDefinitionValidator.Validate(carried).Select(v => v.Code));
        Assert.Throws<JsonException>(() => FlowDefinitionLoader.Parse(withMeter.Replace("\"meter\"", "\"Meter\"", StringComparison.Ordinal)));
    }

    // ─── المُساواة ─────────────────────────────────────────────────────

    /// <summary>
    /// <para><b>انتِقالانِ يَختَلِفانِ في المِترِ وَحدَه غَيرُ مُتَساوِيَين</b> — والمُساواةُ المَكتوبَةُ بِاليَدِ كانَت عَمياءَ عَنه (مَقيسٌ عَلى نُسخَةٍ خارِجَ المُستَودَع
    /// قَبلَ الحَقل: <c>Equals = True</c>)، وبِها يَتَساوى مِلَفُّ الاعتِمادِ و<c>ApprovalFlow</c>. وحَسّاسَةٌ لِلحالَة، و<c>null</c> غَيرُ «none»، والمُتَساوِيانِ بَصمَتُهُما واحِدَة.</para>
    /// </summary>
    [Fact]
    public void Two_transitions_differing_only_in_meter_are_unequal()
    {
        var none = T("none");
        var unit = T("deal.create");
        Assert.NotEqual(none, unit);
        Assert.False(none.Equals(unit));
        Assert.True(none != unit);
        Assert.NotEqual(T(null), none);
        Assert.NotEqual(T("None"), none);
        Assert.Equal(2, new HashSet<FlowTransition> { none, unit }.Count);

        var same = T("deal.create");
        Assert.NotSame(unit, same);
        Assert.Equal(unit, same);
        Assert.Equal(unit.GetHashCode(), same.GetHashCode());
        Assert.Equal(T(null).GetHashCode(), T(null).GetHashCode());
        Assert.Single(new HashSet<FlowTransition> { unit, same });
    }

    /// <summary>
    /// <para><b><c>ApprovalFlow</c> يَحمِلُ «none» عَلى انتِقالاتِه الثَلاثَة</b>، ومِلَفّا الاعتِمادِ يُطابِقانِه بِالمِترِ أَيضاً — و<b>حَقنُ العَيبِ الَّذي كانَ أَعمى</b>:
    /// مِلَفُّ <c>tenant_role</c> مَنسوخاً بِمِترِ وِلادَةٍ آخَرَ لا يُطابِقُه (وكانَ سَيُطابِقُه قَبلَ مَدِّ المُساواة).</para>
    /// </summary>
    [Fact]
    public void ApprovalFlow_carries_none_and_a_file_copy_with_another_meter_no_longer_matches_it()
    {
        var shape = ApprovalFlow.Definition("tenant_role", new FlowLabel("تَعريف دَور مُستَأجِر", "Tenant role definition"));
        Assert.Equal(3, shape.Transitions.Count);
        Assert.All(shape.Transitions, t => Assert.Equal(FlowMeter.None, t.Meter));
        Assert.Empty(FlowMeter.Validate(shape, Units));

        var file = FlowDefinitionLoader.Load("tenant_role");
        Assert.Equal(shape.Transitions, file.Transitions);

        var copy = file with { Transitions = [file.Transitions[0] with { Meter = "store.create" }, .. file.Transitions.Skip(1)] };
        Assert.NotEqual(shape.Transitions, copy.Transitions);
        Assert.Empty(FlowMeter.Validate(copy, Units));
    }

    // ─── القِراءَةُ عِندَ التَركيب ───────────────────────────────────────

    /// <summary>
    /// <para><b>قارِئُ المِلَفّاتِ عِندَ التَركيبِ يَرمي بِكُلِّ الخُروقِ لا بِأَوَّلِها</b> (<see cref="MeterGenesis.From"/>، ADR-038) — كَمُحَمِّلَي الأَدوارِ
    /// والمُزَوِّدين: مِترٌ لَيسَ وَحدَةً عَلى تَدَفُّقٍ ووَحدَةٌ صادِرَةٌ عَلى آخَرَ في رِسالَةٍ واحِدَة؛ <b>ووِلادَةٌ مُلتَبِسَة</b> (انتِقالانِ مُنشِئانِ بِوَحدَتَين) تَرمي
    /// أَيضاً، ومُنشِئانِ بِالوَحدَةِ نَفسِها لا يَرميان. والمِلَفّاتُ العَشَرَةُ تُقرَأُ بِلا رَمي.</para>
    /// </summary>
    [Fact]
    public void Reading_the_files_at_composition_throws_with_every_breach_and_on_an_ambiguous_genesis()
    {
        var files = FlowDefinitionLoader.LoadAll();
        var genesis = MeterGenesis.From(files, MeterUnitCatalog.Default);
        Assert.Equal(ExpectedGenesis.OrderBy(p => p.Key, StringComparer.Ordinal), genesis.Meters.OrderBy(p => p.Key, StringComparer.Ordinal));

        static FlowDefinition WithGenesis(FlowDefinition f, string meter)
            => f with { Transitions = [.. f.Transitions.Select(t => t.From == FlowVocabulary.Genesis ? t with { Meter = meter } : t)] };

        var breached = files.Select(f => f.Flow switch
        {
            "offer" => WithGenesis(f, "offer.submitted"),
            "ticket" => WithGenesis(f, "otp.sms.send"),
            _ => f,
        }).ToList();
        var ex = Assert.Throws<InvalidOperationException>(() => MeterGenesis.From(breached, MeterUnitCatalog.Default));
        output.WriteLine($"[G7·flow] الحَقن ⇐ {ex.Message}");
        Assert.Contains("offer: meter_unit_unknown", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ticket: meter_unit_not_persistence", ex.Message, StringComparison.Ordinal);

        var twoBirths = Flow(T("deal.create", FlowVocabulary.Genesis, "a"), T("store.create", FlowVocabulary.Genesis, "b"));
        var amb = Assert.Throws<InvalidOperationException>(() => MeterGenesis.From([twoBirths], MeterUnitCatalog.Default));
        Assert.Contains("meter_probe", amb.Message, StringComparison.Ordinal);
        var sameBirth = Flow(T("deal.create", FlowVocabulary.Genesis, "a"), T("deal.create", FlowVocabulary.Genesis, "b"));
        Assert.Equal("deal.create", MeterGenesis.From([sameBirth], MeterUnitCatalog.Default).UnitOf("meter_probe"));
    }

    // ─── القاعِدَة ١٦: عَدٌّ لا سِعر ─────────────────────────────────────

    /// <summary><b>لا عُضوَ ولا مِفتاحَ ولا قيمَةَ ولا رِسالَةَ في مِترِ التَدَفُّقِ تُسَمّي سِعراً</b> — بِالمُعجَمَينِ الإنجليزيِّ والعَرَبِيِّ نَفسِهِما
    /// (<c>MeterUnitCatalogTests.PriceIsh</c>/<c>NamesAPrice</c>)، والأَداةُ تُقاسُ أَوَّلاً عَلى اسمٍ مَحقون.</summary>
    [Fact]
    public void No_member_key_value_or_message_of_the_flow_meter_names_a_price()
    {
        Assert.Matches(MeterUnitCatalogTests.PriceIsh, "TransitionFee");
        Assert.True(MeterUnitCatalogTests.NamesAPrice("انتِقالٌ بِسِعرِه"));

        var types = new[] { typeof(FlowMeter), typeof(FlowMeterViolation), typeof(FlowMeterUnits), typeof(MeterGenesis), typeof(MeterBirth) };
        var names = MeterUnitCatalogTests.MemberNames(types).Append($"{nameof(FlowTransition)}.{nameof(FlowTransition.Meter)}").ToList();

        var dir = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "core", "ACommerce.Platform.Flows", "Definitions");
        var data = new List<string>();
        foreach (var file in Directory.GetFiles(dir, "*.flow.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var t in doc.RootElement.GetProperty("transitions").EnumerateArray())
            {
                data.Add("meter");
                data.Add(t.GetProperty("meter").GetString() ?? "");
            }
        }

        var texts = new[] { null, "deal.created", "otp.sms.send" }
            .SelectMany(m => FlowMeter.Validate(Flow(T(m)), Units).Select(v => v.MessageAr)).ToList();

        var priced = names.Where(n => MeterUnitCatalogTests.PriceIsh.IsMatch(n))
            .Concat(data.Where(MeterUnitCatalogTests.NamesAPrice))
            .Concat(texts.Where(MeterUnitCatalogTests.NamesAPrice)).ToList();
        output.WriteLine($"[G7·flow] قاعِدَة ١٦: أَنواع {types.Length} · أَسماء {names.Count} · مَفاتيحُ وقِيَمُ المِترِ في المِلَفّات {data.Count} · نُصوص {texts.Count} · تُسَمّي سِعراً {priced.Count}");
        Assert.True(names.Count > 10 && data.Count == 72 && texts.Count == 3, $"أَداةٌ عَمياء: {names.Count} اسماً و{data.Count} مِفتاحاً وقيمَة و{texts.Count} نَصّاً.");
        Assert.True(priced.Count == 0, "مِترُ التَدَفُّقِ يُسَمّي سِعراً: " + string.Join("، ", priced));
    }
}
