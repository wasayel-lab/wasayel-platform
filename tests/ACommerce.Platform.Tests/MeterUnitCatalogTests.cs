using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G2 — مُعجَمُ الوَحَداتِ المَضمون: لا سِعرَ فيه، ولا مِفتاحَ مَجهولٌ يَمُرّ، ولِكُلِّ وَحدَةٍ مِفتاحُ قامُوسٍ ومِيزَةٌ مِن مُعجَمٍ قائِم ═══
// (‏2026-11-12، الموجَة ١٣ W13-6·٣ — ADR-051 §١١؛ التَصميم §2 و§3). كُلُّ صَفٍّ يَطبَعُ ما عَدَّه ويَفشَلُ عَلى الصِفر (القاعِدَة ١٠).

// ‏(‏2026-11-12، مُراجَعَةُ المَرحَلَةِ أ الجَولَةُ الثالِثَة · M6A-RULES-R3-1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **والحارِسُ يُغَطّي ما يَدَّعيه التَصميم §2
// لا مُجَلَّدَ Shared وَحدَه**: كانَ صَفُّ الانعِكاسِ مَحصوراً في فَضاءِ `Shared/Metering`، فَعَمودُ `amount_sar` في `meter.agg_daily` و`const FeeSar` في
// `apps/V1.Meter` يَمُرّانِ والمَجموعَةُ كُلُّها خَضراء (مَقيسٌ عِندَ `925305c`). وصارَ: أَعمِدَةُ ميزَةِ الدَفتَرِ وجَداوِلُها مِن `UsageLedgerFeature` نَفسِها،
// وكُلُّ نَصٍّ ثابِتٍ في `MeterSql`، وأَعضاءُ المُجَمِّعِ المَبنِيِّ وأَعضاءُ أَنواعِ العَدِّ في القالِب — كُلٌّ بِصَفِّه ويَطبَعُ ما عَدَّ، وحَقنُ كُلِّ شَكلٍ يُحَمِّرُه
// (`The_price_guard_catches_each_injected_shape`). وقِيَمُ القامُوسِ بِالمُعجَمِ العَرَبِيِّ في G11.

public class MeterUnitCatalogTests(ITestOutputHelper output)
{
    /// <summary>كَلِماتُ السِعرِ المَمنوعَةُ في الاسمِ وفي المِفتاح — بِحَرفِ التَصميم §2 (القاعِدَة ١٦: هذه المَوجَةُ عَدٌّ وَحدَه).</summary>
    internal static readonly Regex PriceIsh = new("(?i)price|amount|cost|fee|currency|sar", RegexOptions.Compiled);

    /// <summary>
    /// <para><b>كَلِماتُ السِعرِ بِالعَرَبِيَّة</b> — تُطابَقُ عَلى النَصِّ بَعدَ نَزعِ تَشكيلِه (<see cref="Bare"/>)، فَـ«سِعر» و«سعر» واحِد: السِعرُ وجَمعُه وتَسعيرُه،
    /// والمَبلَغ، والكُلفَةُ والتَكلِفَة، والعُملَة، والريالُ وهَلَلَتُه ورَمزُه. و«رَسم» و«رُسوم» كَلِمَةً تامَّةً (بِسَوابِقِ العَطفِ والجَرِّ والتَعريف) لا مَقطَعاً،
    /// لِأَنّ «يُرسَم» و«رَسمِيّ» لَيسا رَسماً يُدفَع.</para>
    /// </summary>
    internal static readonly Regex PriceIshAr = new(
        @"سعر|أسعار|اسعار|تسعير|مبلغ|مبالغ|كلفة|تكاليف|عملة|عملات|ريال|هللة|هللات|ر\.س|﷼|(?<!\p{L})[وفبلك]?(?:ال|لل)?(?:رسوم|رسم)(?!\p{L})",
        RegexOptions.Compiled);

    private static readonly Regex Tashkeel = new("[ؐ-ًؚ-ٰٟۖ-ۭـ‌-‏]", RegexOptions.Compiled);

    /// <summary>النَصُّ بِلا تَشكيلٍ ولا تَطويلٍ ولا عَلاماتِ اتِّجاه.</summary>
    internal static string Bare(string text) => Tashkeel.Replace(text, "");

    /// <summary>يُسَمّي سِعراً بِأَيٍّ مِن المُعجَمَين؟</summary>
    internal static bool NamesAPrice(string text) => PriceIsh.IsMatch(text) || PriceIshAr.IsMatch(Bare(text));

    private static string UnitsFile() => Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
        "libs", "core", "ACommerce.Platform.Shared", "Metering", "Definitions", "meter-units.json");

    [Fact]
    public void The_embedded_lexicon_loads_and_carries_the_skeleton_unit()
    {
        var catalog = MeterUnitCatalog.Default;
        output.WriteLine($"[G2] وَحَداتٌ مُحَمَّلَة: {catalog.Units.Count}");
        Assert.True(catalog.Units.Count > 0, "أَداةٌ عَمياء: صِفرُ وَحدَة.");
        var deal = catalog.Find("deal.create");
        Assert.NotNull(deal);
        Assert.Equal((MeterSides.Operation, MeterBoundaries.Persistence, MeterMeasures.Count, "deal_status", MeterReconcilers.DealIds),
                     (deal.Side, deal.Boundary, deal.Measure, deal.Feature, deal.Reconcile));
        Assert.Contains("DealsService.StartAsync", deal.Source, StringComparison.Ordinal);
    }

    /// <summary><b>لا عُضوَ يُسَمّي سِعراً</b> في أَيِّ نَوعٍ مِن أَنواعِ المُجَلَّد — ولا مِفتاحَ في المِلَفّ. والأَداةُ تُقاسُ أَوَّلاً: الكَلِمَةُ تُمسَكُ
    /// في <c>AmountSar</c> و<c>UnitPrice</c> و<c>FeeCode</c>، ولا تُمسَكُ في أَسماءِ المُجَلَّدِ الحَقيقِيَّة.</summary>
    [Fact]
    public void No_member_or_key_of_the_lexicon_names_a_price()
    {
        foreach (var hit in new[] { "AmountSar", "UnitPrice", "FeeCode", "currency", "Cost" }) Assert.Matches(PriceIsh, hit);

        var types = typeof(MeterUnit).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(MeterUnit).Namespace && !t.Name.StartsWith('<')).ToList();
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var members = types.SelectMany(t => t.GetMembers(all).Select(m => $"{t.Name}.{m.Name}")).ToList();
        var priced = members.Concat(types.Select(t => t.Name)).Where(n => PriceIsh.IsMatch(n)).ToList();
        output.WriteLine($"[G2] أَنواعٌ مَفحوصَة {types.Count} · أَعضاء {members.Count} · تُسَمّي سِعراً {priced.Count}");
        Assert.True(types.Count >= 8 && members.Count > 50, $"أَداةٌ عَمياء: {types.Count} نَوعاً و{members.Count} عُضواً.");
        Assert.True(priced.Count == 0, "عُضوٌ يُسَمّي سِعراً في مُعجَمِ العَدّ: " + string.Join(", ", priced));

        using var doc = JsonDocument.Parse(File.ReadAllText(UnitsFile()));
        var keys = new List<string>();
        void Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
                foreach (var p in e.EnumerateObject()) { keys.Add(p.Name); Walk(p.Value); }
            else if (e.ValueKind == JsonValueKind.Array)
                foreach (var x in e.EnumerateArray()) Walk(x);
        }
        Walk(doc.RootElement);
        output.WriteLine($"[G2] مَفاتيحُ المِلَفّ {keys.Count}: {string.Join(", ", keys.Distinct())}");
        Assert.True(keys.Count > 0, "أَداةٌ عَمياء: صِفرُ مِفتاح.");
        Assert.DoesNotContain(keys, k => PriceIsh.IsMatch(k));
    }

    // ─── (‏2026-11-12، M6A-RULES-R3-1) الأَعمِدَةُ والجُمَلُ وأَعضاءُ المُجَمِّعِ وأَعضاءُ القالِب ─────────────────────────────────────────────────

    /// <summary>كُلُّ جَدوَلٍ وعَمودٍ تُعلِنُه ميزَةُ الدَفتَرِ — <c>جَدوَل.عَمود</c> — مِن الميزَةِ نَفسِها كَما يُنشِئُها الإقلاع، لا مِن نُسخَةٍ مِن أَسمائِها.</summary>
    internal static IReadOnlyList<string> LedgerNames(IEnumerable<ISchemaObject> objects)
    {
        var tables = objects.OfType<Table>().ToList();
        return [.. tables.Select(t => t.Identifier.Name), .. tables.SelectMany(t => t.Columns.Select(c => $"{t.Identifier.Name}.{c.Name}"))];
    }

    /// <summary>كُلُّ نَصٍّ ثابِتٍ في <see cref="MeterSql"/> — الثَوابِتُ (<c>const</c>) وقَوائِمُ الأَسماءِ الساكِنَة — بِاسمِ حَقلِه.</summary>
    internal static IReadOnlyList<(string Field, string Text)> SqlLiterals()
    {
        var found = new List<(string, string)>();
        foreach (var f in typeof(MeterSql).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            var value = f.IsLiteral ? f.GetRawConstantValue() : f.GetValue(null);
            if (value is string s) found.Add((f.Name, s));
            else if (value is IEnumerable<string> list) found.AddRange(list.Select(x => (f.Name, x)));
        }
        return found;
    }

    /// <summary>أَسماءُ الأَنواعِ وأَعضائِها — كُلُّ عُضوٍ مُعلَنٍ في النَوعِ نَفسِه، عامٌّ أَو خاصّ، ساكِنٌ أَو لِلنُسخَة، ومِنها الثَوابِت.</summary>
    internal static IReadOnlyList<string> MemberNames(IEnumerable<Type> types)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var list = types.ToList();
        return [.. list.Select(t => t.Name), .. list.SelectMany(t => t.GetMembers(all).Select(m => $"{t.Name}.{m.Name}"))];
    }

    /// <summary><b>المُجَمِّعُ المَبنِيُّ نَفسُه</b> — مَشروعُ الاختِبارِ لا يُشيرُ إلى <c>V1.Meter</c> (G10، ورُؤوسُ صُفوفِه الحَيَّة)، فَيُقرَأُ الثُنائِيُّ الَّذي يُشَغِّلُه
    /// <c>LiveMeterAggregatorProofTests</c> بِالتَهيِئَةِ ذاتِها؛ يَبنيه الحَلُّ قَبلَ خُطوَةِ الاختِبار (ci.yml). وغِيابُه أَداةٌ عَمياءُ تَفشَل، لا صَفٌّ يَتَخَطّى.</summary>
    internal static Assembly AggregatorAssembly()
    {
        var tfm = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var dll = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "apps", "V1.Meter", "bin", tfm.Parent!.Name, tfm.Name, "V1.Meter.dll");
        Assert.True(File.Exists(dll), $"أَداةٌ عَمياء: {dll} غائِب — ابنِ الحَلَّ أَوَّلاً (dotnet build PlatformV1.slnx -c {tfm.Parent!.Name}).");
        return Assembly.LoadFrom(dll);
    }

    /// <summary>أَنواعُ العَدِّ في القالِب: فَضاءُ <c>Services.Metering</c> — إلّا أَنواعَ <c>ModelUsageMetering.cs</c> (قِياسُ توكناتِ نَماذِجِ اللُغَةِ وكُلفَتِها
    /// ‏2026-09-02، مُسَعَّرٌ بِتَصميمِه، والتَصميم §2 يُسَمّي <c>ModelPricingCatalog</c> شَكلَ دَفتَرِ الأَسعارِ القادِم) — وكُلُّ نَوعٍ اسمُه يَبدَأُ بِـ<c>Meter</c>
    /// في أَيِّ فَضاء (القارِئُ <c>MeterQueries</c> وأَنواعُه، والصَفحَتان). والمُستَثنى يُقرَأُ مِن مِلَفِّه لا مِن قائِمَةٍ تُنسى.</summary>
    internal static (IReadOnlyList<Type> Scanned, IReadOnlyList<string> Excluded) TemplateMeterTypes()
    {
        var model = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Services", "Metering", "ModelUsageMetering.cs"));
        var excluded = Regex.Matches(WriteEndpointGuardTests.StripComments(model), @"\b(?:class|record(?:\s+(?:struct|class))?|struct|enum|interface)\s+(?<n>[A-Za-z_]\w*)")
            .Select(m => m.Groups["n"].Value).ToHashSet(StringComparer.Ordinal);
        var ns = typeof(UsageListener).Namespace;
        static Type Outer(Type t) { while (t.DeclaringType is not null) t = t.DeclaringType; return t; }
        var scanned = typeof(UsageListener).Assembly.GetTypes()
            .Where(t => !t.Name.StartsWith('<') && !Outer(t).Name.StartsWith('<'))
            .Where(t => (t.Namespace == ns && !excluded.Contains(Outer(t).Name)) || Outer(t).Name.StartsWith("Meter", StringComparison.Ordinal))
            .ToList();
        return (scanned, [.. excluded.Order(StringComparer.Ordinal)]);
    }

    /// <summary><b>لا جَدوَلَ ولا عَمودَ ولا جُملَةَ تُسَمّي سِعراً</b> (التَصميم §2: «No column … is named or means price»): جَداوِلُ <see cref="UsageLedgerFeature"/>
    /// وأَعمِدَتُها، وكُلُّ نَصٍّ ثابِتٍ في <see cref="MeterSql"/>.</summary>
    [Fact]
    public void No_table_column_or_statement_of_the_ledger_names_a_price()
    {
        var names = LedgerNames(new UsageLedgerFeature(new PostgresqlMigrator()).Objects);
        var tables = names.Count(n => !n.Contains('.'));
        var literals = SqlLiterals();
        var priced = names.Where(n => PriceIsh.IsMatch(n))
            .Concat(literals.Where(l => PriceIsh.IsMatch(l.Text)).Select(l => $"MeterSql.{l.Field}")).ToList();
        output.WriteLine($"[G2] جَداوِلُ الدَفتَر {tables} · أَعمِدَة {names.Count - tables} · نُصوصُ MeterSql الثابِتَة {literals.Count} مِن "
                         + $"{literals.Select(l => l.Field).Distinct().Count()} حَقلاً · تُسَمّي سِعراً {priced.Count}");
        Assert.True(tables >= 5 && names.Count - tables >= 30, $"أَداةٌ عَمياء: {tables} جَدوَلاً و{names.Count - tables} عَموداً.");
        Assert.True(literals.Count >= 30, $"أَداةٌ عَمياء: {literals.Count} نَصّاً ثابِتاً في MeterSql.");
        Assert.True(priced.Count == 0, "جَدوَلٌ أَو عَمودٌ أَو جُملَةٌ تُسَمّي سِعراً في الدَفتَر: " + string.Join(", ", priced));
    }

    /// <summary><b>لا عُضوَ في المُجَمِّعِ ولا في أَنواعِ العَدِّ في القالِبِ يُسَمّي سِعراً</b> — صَفُّ <c>Shared/Metering</c> أَعلاه بِمُعجَمِه، عَلى الثُنائِيِّ المَبنِيِّ
    /// <c>V1.Meter</c> وعَلى المُستَمِعِ والمُصَنِّفِ والإقلاعِ والميزَةِ والقارِئِ والصَفحَتَين.</summary>
    [Fact]
    public void No_member_of_the_aggregator_or_of_the_template_meter_names_a_price()
    {
        var aggregator = AggregatorAssembly().GetTypes().Where(t => !t.Name.StartsWith('<')).ToList();
        var (template, excluded) = TemplateMeterTypes();
        var aggNames = MemberNames(aggregator);
        var tplNames = MemberNames(template);
        var priced = aggNames.Concat(tplNames).Where(n => PriceIsh.IsMatch(n)).ToList();
        output.WriteLine($"[G2] المُجَمِّع (V1.Meter المَبنِيّ): أَنواع {aggregator.Count} · أَسماء {aggNames.Count}");
        output.WriteLine($"[G2] القالِب: أَنواع {template.Count} ({string.Join(", ", template.Select(t => t.Name).Distinct().Order(StringComparer.Ordinal))}) · أَسماء {tplNames.Count}");
        output.WriteLine($"[G2] مُستَثنى ModelUsageMetering.cs {excluded.Count}: {string.Join(", ", excluded)} · تُسَمّي سِعراً {priced.Count}");
        Assert.True(aggregator.Count >= 5 && aggNames.Count > 50, $"أَداةٌ عَمياء: {aggregator.Count} نَوعاً في المُجَمِّع و{aggNames.Count} اسماً.");
        Assert.Contains(aggregator, t => t.Name == "Aggregator");
        foreach (var name in new[] { "UsageListener", "MeterRules", "MeterBoot", "UsageLedgerFeature", "MeterQueries", "MeterIndex", "MeterStore" })
            Assert.Contains(template, t => t.Name == name);
        Assert.True(excluded.Count >= 5 && template.All(t => !excluded.Contains(t.Name)), $"أَداةٌ عَمياء: المُستَثنى {excluded.Count}.");
        Assert.True(priced.Count == 0, "عُضوٌ يُسَمّي سِعراً في المُجَمِّعِ أَو في عَدِّ القالِب: " + string.Join(", ", priced));
    }

    /// <summary>عُضوٌ مَحقونٌ يَقرَؤُه <see cref="MemberNames"/> كَما يَقرَأُ <c>Aggregator</c> — شَكلُ الحَقنِ المُسَمّى في المُلاحَظَة.</summary>
    private static class InjectedAggregator { public const decimal FeeSar = 0m; }

    /// <summary><b>والحارِسُ يُقاسُ قَبلَ أَن يُوثَقَ بِه</b> (القاعِدَة ١٠): كُلُّ شَكلٍ مَحقونٍ يَمُرُّ بِالدالَّةِ الَّتي يَمُرُّ بِها الحَقيقِيُّ فَيُمسَك — عَمودٌ في جَدوَلٍ
    /// مَحقونٍ بِجِوارِ الميزَة، وثابِتٌ في نَوعٍ بِجِوارِ أَنواعِ المُجَمِّع، وعُنوانٌ عَرَبِيٌّ بِالسِعرِ والريال — والكَلِماتُ الَّتي تُشبِهُ السِعرَ ولَيسَت إيّاه لا تُمسَك.</summary>
    [Fact]
    public void The_price_guard_catches_each_injected_shape()
    {
        var real = new UsageLedgerFeature(new PostgresqlMigrator()).Objects;
        var extra = new Table(new PostgresqlObjectName(MeterSql.Schema, MeterSql.AggDaily, SchemaUtils.IdentifierUsage.General));
        extra.AddColumn("amount_sar", "numeric");
        var column = LedgerNames([.. real, extra]).Where(n => PriceIsh.IsMatch(n)).ToList();
        var member = MemberNames([.. AggregatorAssembly().GetTypes().Where(t => t.Name == "Aggregator"), typeof(InjectedAggregator)])
            .Where(n => PriceIsh.IsMatch(n)).ToList();
        const string title = "سِعرُ عَمَلِيّاتِ المَتاجِرِ اليَوم بِالريال";
        output.WriteLine($"[G2] حَقنُ العَمود ⇐ {string.Join(", ", column)} · حَقنُ الثابِت ⇐ {string.Join(", ", member)} · حَقنُ العُنوان ⇐ "
                         + string.Join(", ", PriceIshAr.Matches(Bare(title)).Select(m => m.Value)));
        Assert.Contains("agg_daily.amount_sar", column);
        Assert.Contains("InjectedAggregator.FeeSar", member);
        Assert.True(NamesAPrice(title));

        var priced = new[] { "السِعر", "أَسعارُ الباقات", "تَسعيرُ التِقنِيَة", "مَبلَغُ الطَلَب", "التَكلِفَة", "كُلفَةُ التَشغيل", "رُسومُ الخِدمَة", "رَسمٌ ثابِت",
                             "بِالرُسوم", "العُملَة", "مِئَةُ ريال", "خَمسونَ هَلَلَة", "١٠ ر.س", "Fee" };
        var plain = new[] { "يُرسَمُ الجَدوَل", "رَسمِيّ", "عَمَلِيّاتُ المَتاجِر", "المُعامَلَة", "تُبلَغُ بِنَقرَة", "الرَسمِيَّة", "مَرسوم", "عَمَلِيَّةٌ لِكُلِّ سَطر" };
        var missed = priced.Where(p => !NamesAPrice(p)).ToList();
        var over = plain.Where(NamesAPrice).ToList();
        output.WriteLine($"[G2] المُعجَمُ العَرَبِيّ: يُمسِكُ {priced.Length - missed.Count}/{priced.Length} · يُخطِئُ {over.Count}/{plain.Length}");
        Assert.True(missed.Count == 0, "كَلِمَةُ سِعرٍ لا يُمسِكُها المُعجَم: " + string.Join(", ", missed));
        Assert.True(over.Count == 0, "كَلِمَةٌ لَيسَت سِعراً يُمسِكُها المُعجَم: " + string.Join(", ", over));
    }

    /// <summary><b>مِفتاحٌ مَجهولٌ يَرمي</b> (<c>Disallow</c>): «price» في صَفٍّ لا يُتَجاهَلُ صامِتاً — فَيُفشِلُ الإقلاع (الحَقنُ المُسَمّى في G2).</summary>
    [Fact]
    public void An_unknown_key_such_as_price_is_refused_not_ignored()
    {
        var text = File.ReadAllText(UnitsFile());
        Assert.NotEmpty(MeterUnitCatalog.Parse(text));
        var injected = text.Replace("\"measure\": \"count\",", "\"measure\": \"count\",\n      \"price\": 1,", StringComparison.Ordinal);
        Assert.NotEqual(text, injected);
        var ex = Assert.ThrowsAny<JsonException>(() => MeterUnitCatalog.Parse(injected));
        output.WriteLine($"[G2] الحَقن: {ex.Message.Split('\n')[0]}");
        Assert.Contains("price", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>صَفٌّ فاسِدٌ يُفشِلُ التَحميلَ بِرَمزِه — لا يَصِلُ الدَفتَرَ صامِتاً.</summary>
    [Fact]
    public void A_breaching_lexicon_fails_the_load_with_its_codes()
    {
        var units = MeterUnitCatalog.Parse(File.ReadAllText(UnitsFile()));
        var ex = Assert.Throws<InvalidOperationException>(() => MeterUnitCatalog.From([.. units, units[0] with { Side = "tech" }]));
        Assert.Contains("unit_side_out_of_vocabulary", ex.Message, StringComparison.Ordinal);
        Assert.Contains("units_code_duplicate", ex.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => MeterUnitCatalog.From([]));
    }

    /// <summary><b>لِكُلِّ وَحدَةٍ تَسمِيَةٌ عَرَبِيَّةٌ في القامُوس</b> بِمِفتاحِ <see cref="MeterUnitCatalog.LabelKey"/> — ومِفتاحٌ بِمَقاطِعِ القامُوسِ الثَلاثَة.</summary>
    [Fact]
    public void Every_unit_label_key_is_in_the_arabic_dictionary_and_has_three_segments()
    {
        var keys = MeterUnitCatalog.Default.Units.Select(u => MeterUnitCatalog.LabelKey(u.Code)).ToList();
        var missing = keys.Where(k => !LocaleCatalog.Lexicon.Contains(k)).ToList();
        output.WriteLine($"[G2] مَفاتيحُ التَسمِيَة {keys.Count} · في القامُوس {keys.Count - missing.Count}: {string.Join(", ", keys.Select(k => $"{k}={LocaleCatalog.Find("ar", k)}"))}");
        Assert.True(keys.Count > 0, "أَداةٌ عَمياء: صِفرُ مِفتاح.");
        Assert.True(missing.Count == 0, "وَحدَةٌ بِلا تَسمِيَةٍ في ar.json: " + string.Join(", ", missing));
        Assert.All(keys, k => Assert.Matches("^[a-z][a-z0-9_]*(?:\\.[a-z][a-z0-9_]*){2}$", k));
    }

    /// <summary><b>التَصادُمُ أَحمَر</b>: رَمزانِ يَقَعانِ عَلى مِفتاحٍ واحِدٍ يُرفَضانِ عِندَ التَحميل.</summary>
    [Fact]
    public void Two_codes_on_one_dictionary_key_fail_the_load()
    {
        var unit = MeterUnitCatalog.Default.Units[0];
        var ex = Assert.Throws<InvalidOperationException>(() => MeterUnitCatalog.From([unit with { Code = "a_b.c" }, unit with { Code = "a.b_c" }]));
        Assert.Contains("units_label_key_collision", ex.Message, StringComparison.Ordinal);
    }

    /// <summary><b>المِيزَةُ اسمٌ قائِمٌ لا كَلِمَةٌ تُختَرَع</b> (التَصميم §3.2، القاعِدَة ١٦): كُلُّ مِيزَةٍ ثابِتَةٍ اسمٌ في أَحَدِ المَعاجِمِ الأَربَعَة —
    /// مِلَفّاتُ التَدَفُّق، والعُدَد، وقُدُراتُ المُزَوِّد، وقُدُراتُ الاشتِراك. والمِرساةُ (<c>@…</c>) يُصادِقُها المُصادِق.</summary>
    [Fact]
    public void Every_fixed_feature_is_a_name_in_one_of_the_four_existing_closed_sets()
    {
        var root = ThemeZeroEquivalenceTests.RepoRoot;
        var flows = Directory.EnumerateFiles(Path.Combine(root, "libs", "core", "ACommerce.Platform.Flows", "Definitions"), "*.flow.json")
            .Select(f => Path.GetFileName(f)[..^".flow.json".Length]).ToHashSet(StringComparer.Ordinal);
        var kits = Directory.EnumerateDirectories(Path.Combine(root, "libs", "kits")).Select(d => Path.GetFileName(d).ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var anchors = new Dictionary<string, IReadOnlySet<string>>
        {
            ["flow"] = flows,
            ["kit"] = kits,
            ["provider_capability"] = ACommerce.Platform.Providers.ProviderCapabilities.All.ToHashSet(StringComparer.Ordinal),
            ["capability"] = ACommerce.Kit.Subscriptions.CapabilityCatalog.Codes.ToHashSet(StringComparer.Ordinal),
        };
        output.WriteLine("[G2] المَعاجِمُ الأَربَعَة: " + string.Join(" · ", anchors.Select(a => $"{a.Key} {a.Value.Count}")));
        Assert.All(anchors, a => Assert.True(a.Value.Count > 0, $"أَداةٌ عَمياء: صِفرُ اسمٍ في {a.Key}."));

        var fixedFeatures = MeterUnitCatalog.Default.Units.Where(u => !MeterFeatureAnchors.IsAnchor(u.Feature!)).ToList();
        var resolved = fixedFeatures.Select(u => (u.Code, u.Feature, Anchor: anchors.FirstOrDefault(a => a.Value.Contains(u.Feature!)).Key)).ToList();
        output.WriteLine("[G2] مِيزاتٌ مَحلولَة: " + string.Join(", ", resolved.Select(r => $"{r.Code}→{r.Feature}@{r.Anchor ?? "؟"}")));
        Assert.True(resolved.Count > 0, "أَداةٌ عَمياء: صِفرُ مِيزَةٍ ثابِتَة.");
        Assert.All(resolved, r => Assert.True(r.Anchor is not null, $"مِيزَةُ «{r.Code}» = «{r.Feature}» لَيسَت اسماً في أَيِّ مُعجَمٍ قائِم."));
    }

    // ─── (‏2026-11-13، مُراجَعَةُ المَرحَلَةِ ج الثالِثَة · M6C-RULES-R3-2) مَوضِعُ العَدِّ في المُعجَمِ يُسَمّي بانِيَ النِداءِ الَّذي يَستَعمِلُه مَوضِعُه ────────────────

    /// <summary>
    /// <para>‏(M6C-RULES-R3-2 — ADR-051 §١٩) <b>الوَحَداتُ الصادِرَةُ الَّتي صَحَّحَت المُراجَعاتُ عَدَّها، وبانِي كُلٍّ ومَوضِعُ استِعمالِه</b>: <c>source</c> هُوَ
    /// «مَوضِعُ العَدّ» (التَصميم §3.1)، ومَن يُطابِقُ أَو يُراجِعُ الخُصوصِيَّةَ مِن المُعجَمِ يَقرَأُ ما فيه. وكانَت خَمسٌ مِنها تَصِفُ عَدّاً غَيَّرَته الإصلاحات
    /// (<c>ce71c1f</c> و<c>7495de4</c> و<c>af3e566</c>) والمِلَفُّ لَم يُمَسّ — والمَعدودُ يُعادُ تَسمِيَتُه بِالمُتَرجِمِ هُنا (<c>nameof</c>) أَو بِالانعِكاسِ لِلخاصّ.</para>
    /// </summary>
    internal static readonly (string Unit, Type Builder, string Member, string CallSite, string CallText)[] CorrectedOutboundSources =
    [
        (OutboundUnits.BillingPayPalWebhookVerify, typeof(ACommerce.Templates.Customer.Marketplace.Billing.PayPalSurface),
            nameof(ACommerce.Templates.Customer.Marketplace.Billing.PayPalSurface.VerifyCall),
            "libs/templates/ACommerce.Templates.Customer.Marketplace/Billing/PayPalEndpoints.cs", "PayPalSurface.VerifyCall(headers)"),
        (OutboundUnits.PaymentGatewayInvoiceFetch, typeof(ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayCalls),
            nameof(ACommerce.Templates.Customer.Marketplace.Services.Deals.GatewayCalls.ExpiryRun),
            "libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Deals/GatewayPayments.cs", "GatewayCalls.ExpiryRun(deal)"),
        (OutboundUnits.DeliveryOrderCreate, typeof(ACommerce.Kit.Delivery.OtoClient), "Acceptance",
            "libs/kits/Delivery/ACommerce.Kit.Delivery.Core/OtoClient.cs", "a => Acceptance(a.Response.StatusCode, a.Root)"),
        (OutboundUnits.DeliveryOrderStatus, typeof(ACommerce.Kit.Delivery.OtoClient), "Acceptance",
            "libs/kits/Delivery/ACommerce.Kit.Delivery.Core/OtoClient.cs", "a => Acceptance(a.Response.StatusCode, a.Root)"),
        (OutboundUnits.DeliveryWebhookRegister, typeof(ACommerce.Kit.Delivery.OtoClient), "Acceptance",
            "libs/kits/Delivery/ACommerce.Kit.Delivery.Core/OtoClient.cs", "a => Acceptance(a.Response.StatusCode, a.Root)"),
        (OutboundUnits.DeliveryVendorRegister, typeof(ACommerce.Kit.Delivery.OtoClient), "Acceptance",
            "libs/kits/Delivery/ACommerce.Kit.Delivery.Core/OtoClient.cs", "a => Acceptance(a.Response.StatusCode, a.Root)"),
        (OutboundUnits.BillingPaddleTransactionCreate, typeof(ACommerce.Templates.Customer.Marketplace.Billing.PaddleSurface),
            nameof(ACommerce.Templates.Customer.Marketplace.Billing.PaddleSurface.CreateCall),
            "libs/templates/ACommerce.Templates.Customer.Marketplace/Billing/PaddleEndpoints.cs", "PaddleSurface.CreateCall(slug, reference)"),
    ];

    /// <summary>
    /// <para>‏(‏2026-11-13، مُراجَعَةُ المَرحَلَةِ ج الثالِثَة · M6C-RULES-R3-2 — ADR-051 §١٩) <b>كُلُّ وَحدَةٍ صادِرَةٍ صُحِّحَ عَدُّها يُسَمّي <c>source</c>ها بانِيَ نِدائِها</b>
    /// (<c>{نَوع}.{عُضو}</c>)، والعُضوُ قائِمٌ في الكود، ومَوضِعُ النِداءِ يَستَعمِلُه بِحَرفِه. فَإصلاحٌ يُغَيِّرُ البانِيَ أَو يَنقُلُ المَوضِعُ عَنه يُحَمِّرُ الصَفَّ حَتّى يُكتَبَ
    /// المُعجَمُ مَعَه؛ وحَقنُ النُصوصِ القَديمَةِ (المُعَرِّفُ «the transmission id PayPal sent»، و«{invoiceId}:expiry» وَحدَها، و«answers 2xx» لِـOTO) يُحَمِّرُه.</para>
    /// </summary>
    [Fact]
    public void Each_corrected_outbound_unit_source_names_the_builder_its_call_site_uses()
    {
        var catalog = MeterUnitCatalog.Default;
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var matched = 0;
        foreach (var (unit, builder, member, callSite, callText) in CorrectedOutboundSources)
        {
            var row = catalog.Find(unit);
            Assert.NotNull(row);
            Assert.Equal(MeterBoundaries.Outbound, row.Boundary);
            Assert.NotEmpty(builder.GetMember(member, all));
            var named = $"{builder.Name}.{member}";
            var site = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, callSite));
            var ok = row.Source.Contains(named, StringComparison.Ordinal) && site.Contains(callText, StringComparison.Ordinal);
            output.WriteLine($"[G2·source] {unit} ⇐ {named} · في المُعجَم {row.Source.Contains(named, StringComparison.Ordinal)} · في {Path.GetFileName(callSite)} {site.Contains(callText, StringComparison.Ordinal)}");
            if (ok) matched++;
        }
        output.WriteLine($"[G2·source] وَحَداتٌ مَفحوصَة {CorrectedOutboundSources.Length} · تُسَمّي بانِيَها {matched}");
        Assert.True(CorrectedOutboundSources.Length >= 7, $"أَداةٌ عَمياء: {CorrectedOutboundSources.Length} وَحدَة.");
        Assert.Equal(CorrectedOutboundSources.Length, matched);
    }
}
