using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Export;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Marten;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G3 — دَفتَرُ التَصنيفِ القابِلُ لِلتَشغيل: كُلُّ نَوعٍ يَكتُبُه الحِفظُ مَعدودٌ بِقاعِدَتِه، أَو مُشتَقّ، أَو غَيرُ مَعدودٍ بِسَبَبِه ═══
// (‏2026-11-12، الموجَة ١٣ W13-6·٦ — ADR-051 §١٤؛ التَصميم §5.6 و§12 G3؛ سِجِلُّ المُراجَعَة RULES-05: «كانَ الدَفتَرُ إعلاناً»).
//
// **ثَلاثُ أَدَواتٍ تَرى الأَنواع، ولا يُقرَأُ الدَفتَرُ مِن نَفسِه**:
//   الوَثائِق = سِجِلُّ التَخارُج (`TenantExportLedger.All`) ∪ أَداتا `MartenSchemaVerbTests` (النَصُّ وIL — المُستَعمَلُ في libs/apps) ∪ جَداوِلُ
//     الوَثائِقِ المُسَجَّلَةِ في التَركيبِ نَفسِه (`mt_doc_*` — ومِنها بُنيَةُ Wolverine وعَفريتِ الأَحداث)؛
//   الأَحداث = مُعامِلاتُ `Apply` (أَداةُ `AppliedEventEmitterTests`) ∪ الأَنواعُ المَبنِيَّةُ في مَواضِعِ `Events.Append`/`Events.StartStream`
//     (وَسائِطُ النِداءِ، ومُهَيِّئُ المُتَغَيِّرِ الَّذي يُمَرَّر، وجِسمُ الدالَّةِ الَّتي تُنادى فيه — في المِلَفِّ نَفسِه).
// والدَفتَرُ يُطابِقُها بِالاتِّجاهَين: نَوعٌ تَراهُ أَداةٌ بِلا صَفٍّ يَحمَرّ، وصَفٌّ لا تَراهُ أَداةٌ يَحمَرّ (نَسَقُ قائِمَةِ اليَتامى).
//
// **ثُمَّ يُشَغَّل**: كُلُّ نَوعٍ يُنشَأُ إنشاءً مُصطَنَعاً في جَلسَةٍ حَقيقِيَّةٍ مِن المَخزَنِ المُرَكَّب (بِلا قاعِدَة — لا حِفظ)، وتَغييراتُها المُعَلَّقَةُ
// تُبنى مُدخَلاً لِـ`MeterRules.Classify` بِما يَقرَؤُه المُستَمِع: الوَثيقَةُ و`VersionFor` (D6). المَعدودُ بِشَكلِ وِلادَتِه ← سَطرٌ واحِدٌ مِن وَحدَتِه؛
// والمُشتَقُّ وغَيرُ المَعدودِ بِكُلِّ شَكلٍ (Insert، وStore، وتَحديثٌ بِنُسخَة، وبِدءُ مَجرى) ← صِفر.
//
// **وحُدودُه مُعلَنَة**:
//   - المُدخَلُ يُبنى مِن كُلِّ الوَثائِقِ المُعَلَّقَة، والمُستَمِعُ اليَومَ يُمَرِّرُ `Deal` وَحدَها (`UsageListener.cs`). فَهذا الحارِسُ يَقيسُ المُصَنِّف؛
//     وتَضييقُ المُستَمِعِ تَقيسُه الصُفوفُ الحَيَّةُ لِكُلِّ وَحدَة (G5).
//   - **مُدخَلُ المُصَنِّفِ بِلا قَناةِ مَجرى عِندَ الرَأس** (`PendingSnapshot(string SessionTenant, IReadOnlyList<PendingDocument> Documents)`)، فَبِدءُ
//     المَجرى يُنشَأُ حَقّاً (`PendingChanges.Streams()` يَعُدُّه) ولا يَبلُغُ المُصَنِّفَ مِنه شَيء — فَصَفُّ حَدَثٍ «مَعدود» يَحمَرُّ اليَومَ بِلا
//     قاعِدَة (`counted_rule_missing`). والبَندُ ٧ يُضيفُ القَناةَ ويَقلِبُ صُفوفَ أَحداثِه.
//     ‏(‏2026-11-12، W13-6·٧ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **وأُضيفَت**: `PendingSnapshot(…, Streams)` و`SnapshotOf` يَبني المَجاري بِـ`MeterRules.StreamsOf`
//     — ما يُمَرِّرُه المُستَمِعُ نَفسُه — فَبِدءُ المَجرى يَبلُغُ المُصَنِّف. وقُلِبَت صُفوفُ الأَحداثِ الأَربَعَة، والمُشتَقُّ الَّذي يَعُدُّ صَفُّ حَدَثِه وَحدَتَه
//     لَيسَ «وَحدَةً آتِيَة» (`later_unit_in_lexicon` لِغَيرِ المَعدودِ وَحدَه)؛ وصَفّا الحَقنِ اللَذانِ كانا يَقيسانِ الحالَ قَبلَ البَند ٧ يَقيسانِ الرَمزَينِ نَفسَيهِما
//     بِحَدَثٍ لا قاعِدَةَ لَه (`ListingEdited`) وبِإعادَةِ صَفِّ `ListingCreated` إلى ما كانَ عَلَيه.
//   - اللَقطَةُ المُباشِرَةُ تُكتَبُ عِندَ الحِفظِ لا قَبلَه، فَلا تَظهَرُ هُنا مَعَ بِدءِ المَجرى؛ و«المُشتَقُّ صِفرٌ» يُقاسُ بِإنشائِه وَثيقَةً.
//   - ماسِحُ المَواضِعِ نَصِّيّ: الدالَّةُ المُناداةُ تُقرَأُ بِاسمِها، فَأَحمالُها الزائِدَةُ كُلُّها تُرى مَعاً (`AccountErasure.cs:610` يَرى حَدَثَي
//     `EraseEvents(Offer)` و`EraseEvents(Ticket)`) — زِيادَةٌ في الرُؤيَةِ لا نَقص. والوَسيطُ الأَوَّلُ حينَ يَليه غَيرُه مُعَرِّفُ المَجرى: يُعَدُّ ما يُبنى
//     فيه ولا يُتَتَبَّعُ اسمُه. وما يَفوتُه يُمسِكُه صَفّان: مَوضِعٌ بِلا أَنواع، وحَدَثٌ يُبنى في المُنتَجِ ولا يَراهُ الماسِح.

//
// **وهذا المِلَفُّ حارِسٌ لا تَوصيف** (القاعِدَة ٣ لا تَحكُمُه): لا يُثَبِّتُ سُلوكاً يَبقى بَعدَ تَبديل، بَل يَقيسُ دَفتَراً تَقلِبُ البُنودُ ٧ و٨ و٩ صُفوفَه
// في `MeterClassification.cs`. وما يَتَغَيَّرُ هُنا مَعَها — قَناةُ المَجرى في `SnapshotOf` (البَند ٧) — تَغييرُ اختِبارٍ عادِيٍّ يُطبَعُ بَصمَتاه.

public class MeterClassificationTests(ITestOutputHelper output)
{
    private const string SyntheticTenant = "store-a";

    // ─── الأَداة (١): أَنواعُ الوَثائِق ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>ما تَراهُ أَدَواتُ الوَثائِقِ الثَلاث — بِالاسمِ الصَغير، لِأَنّ جَدوَلَ Marten اسمُ النَوعِ صَغيراً (<c>mt_doc_{اسم}</c>).</summary>
    internal sealed record DocumentSight(IReadOnlyList<string> Export, IReadOnlyList<string> Used, IReadOnlyList<string> Registered)
    {
        public IReadOnlyList<string> All { get; } =
            [.. Export.Concat(Used).Select(n => n.ToLowerInvariant()).Concat(Registered).Distinct().Order(StringComparer.Ordinal)];
    }

    private static readonly Regex DocTable = new(@"^platform\.mt_doc_(?<n>[a-z0-9_]+)$", RegexOptions.Compiled);

    internal static async Task<DocumentSight> SeeDocuments()
    {
        var export = TenantExportLedger.All.Select(e => e.TypeName).Distinct().Order(StringComparer.Ordinal).ToList();
        var (used, _, _, _, _) = MartenSchemaVerbTests.UsedByBothInstruments();
        var registered = await MartenSchemaVerbTests.WithComposedStore(Environments.Production, s =>
            s.Storage.AllObjects().Select(o => DocTable.Match(o.Identifier.QualifiedName))
                .Where(m => m.Success && !m.Groups["n"].Value.EndsWith("_marten_tenant_isolation", StringComparison.Ordinal))
                .Select(m => m.Groups["n"].Value).Distinct().Order(StringComparer.Ordinal).ToList());
        return new DocumentSight(export, used, registered);
    }

    // ─── الأَداة (٢): أَنواعُ الأَحداث ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>مَوضِعُ إلحاقٍ واحِد وما يُبنى فيه.</summary>
    internal sealed record EventSite(string File, int Line, string Call, IReadOnlyList<string> Types);

    private static readonly Regex SiteCall = new(
        @"\bEvents\s*\.\s*(?<m>Append|StartStream)\b\s*(?:<[^<>()]*>)?\s*\(", RegexOptions.Compiled);

    private static readonly Regex NewType = new(
        @"\bnew\s+(?:[A-Za-z_]\w*\.)*(?<t>[A-Z]\w*)\s*(?:<[^<>();]*>)?\s*[\(\{]", RegexOptions.Compiled);

    private static readonly Regex Identifier = new(
        @"^(?<id>[A-Za-z_]\w*)\s*!?\s*(?:\.\s*(?:ToArray|ToList)\s*\(\s*\))?$", RegexOptions.Compiled);

    private static readonly Regex Call = new(
        @"^(?:[A-Za-z_]\w*\s*\.\s*)*(?<f>[A-Z]\w*)\s*\(", RegexOptions.Compiled);

    /// <summary>
    /// <para>كُلُّ مَوضِعِ <c>Events.Append</c>/<c>Events.StartStream</c> في مَصادِرَ مُعطاة، والأَنواعُ المَبنِيَّةُ فيه — نَصّاً بِلا تَعليقات، ثَلاثُ
    /// دَرَجات: (أ) <c>new X(</c> في وَسائِطِ النِداء؛ (ب) وَسيطٌ مُتَغَيِّرٌ (<c>ev</c>، <c>ev!</c>، <c>events.ToArray()</c>) ← آخِرُ إسنادٍ إلَيه قَبلَ المَوضِع
    /// (ومِنه التَفكيكُ <c>var (ev, code) = …</c> ومُتَغَيِّرُ <c>foreach</c> ومُتَغَيِّرُ النَمَط <c>F(…) is { … } events</c>) وكُلُّ <c>.Add(…)</c> عَلَيه؛ (ج) نِداءٌ (<c>EraseEvents(…)</c>، <c>Decide(…)</c>) ← جِسمُ كُلِّ دالَّةٍ
    /// بِاسمِه في المِلَفِّ نَفسِه. والعُمقُ ثَلاث.</para>
    /// <para><paramref name="isType"/> يُبقي ما هُوَ نَوعٌ في المُنتَج (فَلا يُعَدُّ <c>List</c> ولا <c>Guid</c>). ومَوضِعٌ لا يُرى فيه نَوعٌ يَبقى بِلا
    /// أَنواع — وصَفُّ الحارِسِ يُحمِرُّه، فَالأَداةُ لا تَعمى صامِتَة.</para>
    /// </summary>
    internal static IReadOnlyList<EventSite> EventSites(IEnumerable<(string File, string Text)> sources, Func<string, bool> isType)
    {
        var sites = new List<EventSite>();
        foreach (var (file, text) in sources)
        {
            if (!text.Contains("Events", StringComparison.Ordinal)) continue;
            var code = WriteEndpointGuardTests.StripComments(text);
            foreach (Match m in SiteCall.Matches(code))
            {
                var open = m.Index + m.Length - 1;
                var close = Closing(code, open);
                var types = new SortedSet<string>(StringComparer.Ordinal);
                var args = TopLevel(code, open + 1, close).ToList();
                for (var i = 0; i < args.Count; i++)
                {
                    // الوَسيطُ الأَوَّلُ مُعَرِّفُ المَجرى حينَ يَليه غَيرُه: يُعَدُّ ما يُبنى فيه، ولا يُتَتَبَّعُ اسمُه — المُعَرِّفُ لا يَحمِلُ حَدَثاً، وتَتَبُّعُه
                    // يَجلِبُ ما يُبنى حَولَ إسنادِه (مَقيس: `DuplicatePhones.cs:379` جَلَبَ `Cart` و`Favorite` و`Rename`).
                    if (i == 0 && args.Count > 1) { foreach (Match n in NewType.Matches(args[0])) types.Add(n.Groups["t"].Value); continue; }
                    Resolve(args[i], code, m.Index, types, 0);
                }
                var line = 1 + code.AsSpan(0, m.Index).Count('\n');
                sites.Add(new EventSite(Rel(file), line, m.Groups["m"].Value, [.. types.Where(isType)]));
            }
        }
        return sites;
    }

    private static void Resolve(string expr, string code, int before, ISet<string> types, int depth)
    {
        expr = Regex.Replace(expr.Trim(), @"^[A-Za-z_]\w*\s*:(?!:)\s*", "");
        foreach (Match n in NewType.Matches(expr)) types.Add(n.Groups["t"].Value);
        if (depth >= 3) return;

        if (Identifier.Match(expr) is { Success: true } id)
        {
            // أَقرَبُ ما يُعطي الاسمَ قيمَتَه قَبلَ المَوضِع: إسناد، أَو تَفكيك، أَو مُتَغَيِّرُ foreach، أَو مُتَغَيِّرُ نَمَطٍ (`F(…) is { Count: > 0 } events`).
            var name = Regex.Escape(id.Groups["id"].Value);
            var declared = new Regex($@"(?<assign>\b{name}\s*=(?![=>]))|(?<decon>\b{name}\b[^;=(){{}}]*\)\s*=(?![=>]))"
                + $@"|(?<each>\bforeach\s*\(\s*[^;()]*?\b{name}\s+in\b)"
                + $@"|(?<is>\bis\s+(?:not\s+)?(?:\{{[^{{}};]*\}}|[A-Za-z_][\w.]*(?:\s*\{{[^{{}};]*\}})?)\s+{name}\b)");
            var last = declared.Matches(code[..before]).LastOrDefault();
            if (last is not null)
            {
                var from = last.Index + last.Length;
                var init = last.Groups["is"].Success ? ExpressionBefore(code, last.Index) : code[from..StatementEnd(code, from)];
                Resolve(init, code, last.Index, types, depth + 1);
                foreach (Match add in new Regex($@"\b{name}\s*\.\s*Add\s*\(").Matches(code[from..before]))
                {
                    var open = from + add.Index + add.Length - 1;
                    Resolve(code[(open + 1)..Closing(code, open)], code, open, types, depth + 1);
                }
            }
            return;
        }

        if (Call.Match(expr) is { Success: true } call)
            foreach (var body in BodiesOf(code, call.Groups["f"].Value))
                Resolve(body, code, before, types, depth + 1);
    }

    /// <summary>جِسمُ كُلِّ دالَّةٍ بِهذا الاسمِ في المِلَفّ: الاسمُ ثُمَّ قَوسانِ ثُمَّ <c>{</c> أَو <c>=&gt;</c> (لا <c>;</c> — ذاكَ نِداءٌ لا إعلان).</summary>
    private static IEnumerable<string> BodiesOf(string code, string name)
    {
        foreach (Match d in new Regex($@"\b{Regex.Escape(name)}\s*\(").Matches(code))
        {
            var open = d.Index + d.Length - 1;
            var close = Closing(code, open);
            var rest = code.AsSpan(close + 1).TrimStart();
            if (rest.StartsWith("{"))
            {
                var brace = close + 1 + (code.Length - close - 1 - rest.Length);
                yield return code[brace..(Closing(code, brace) + 1)];
            }
            else if (rest.StartsWith("=>"))
            {
                var arrow = close + 1 + (code.Length - close - 1 - rest.Length) + 2;
                yield return code[arrow..StatementEnd(code, arrow)];
            }
        }
    }

    /// <summary>القَوسُ المُقابِلُ لِما عِندَ <paramref name="open"/> (<c>(</c> أَو <c>{</c> أَو <c>[</c>) — بِتَخَطّي السَلاسِلِ والحُروف.</summary>
    private static int Closing(string code, int open)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            var c = code[i];
            if (c is '"' or '\'') { i = WriteEndpointGuardTests.SkipLiteral(code, i); continue; }
            if (c is '(' or '{' or '[') depth++;
            else if (c is ')' or '}' or ']' && --depth == 0) return i;
        }
        return code.Length - 1;
    }

    /// <summary>العِبارَةُ الَّتي تَنتَهي قَبلَ <paramref name="end"/> — رُجوعاً حَتّى قَوسٍ لَم يُغلَق أَو <c>;</c> أَو <c>,</c> أَو <c>=</c> عِندَ العُمقِ صِفر.</summary>
    private static string ExpressionBefore(string code, int end)
    {
        var depth = 0;
        var i = end - 1;
        for (; i >= 0; i--)
        {
            var c = code[i];
            if (c is ')' or '}' or ']') depth++;
            else if (c is '(' or '{' or '[') { if (depth == 0) break; depth--; }
            else if (depth == 0 && c is ';' or ',' or '=') break;
        }
        return code[(i + 1)..end];
    }

    /// <summary>نِهايَةُ الجُملَة: أَوَّلُ <c>;</c> أَو فاصِلَةٍ أَو قَوسٍ يُغلِقُ ما لَم يُفتَح، عِندَ العُمقِ صِفر.</summary>
    private static int StatementEnd(string code, int from)
    {
        var depth = 0;
        for (var i = from; i < code.Length; i++)
        {
            var c = code[i];
            if (c is '"' or '\'') { i = WriteEndpointGuardTests.SkipLiteral(code, i); continue; }
            if (c is '(' or '{' or '[') depth++;
            else if (c is ')' or '}' or ']') { if (--depth < 0) return i; }
            else if (c == ';' && depth == 0) return i;
        }
        return code.Length;
    }

    /// <summary>وَسائِطُ النِداءِ بَينَ <paramref name="from"/> و<paramref name="to"/> مَقسومَةً عَلى الفَواصِلِ عِندَ العُمقِ صِفر.</summary>
    private static IEnumerable<string> TopLevel(string code, int from, int to)
    {
        var depth = 0;
        var start = from;
        for (var i = from; i < to; i++)
        {
            var c = code[i];
            if (c is '"' or '\'') { i = WriteEndpointGuardTests.SkipLiteral(code, i); continue; }
            if (c is '(' or '{' or '[') depth++;
            else if (c is ')' or '}' or ']') depth--;
            else if (c == ',' && depth == 0) { yield return code[start..i]; start = i + 1; }
        }
        if (to > start) yield return code[start..to];
    }

    private static string Rel(string path) =>
        Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, path).Replace('\\', '/');

    /// <summary>ما تَراهُ أَداتا الأَحداث.</summary>
    internal sealed record EventSight(IReadOnlyList<string> Applied, IReadOnlyList<EventSite> Sites)
    {
        public IReadOnlyList<string> Constructed { get; } =
            [.. Sites.SelectMany(s => s.Types).Distinct().Order(StringComparer.Ordinal)];
        public IReadOnlyList<string> All { get; } =
            [.. Applied.Concat(Sites.SelectMany(s => s.Types)).Distinct().Order(StringComparer.Ordinal)];
    }

    internal static EventSight SeeEvents()
    {
        var productTypes = ProductTypeNames();
        return new EventSight(
            [.. AppliedEventEmitterTests.AppliedEventTypes().Order(StringComparer.Ordinal)],
            EventSites(EntitlementContractTests.SourceFiles(), productTypes.Contains));
    }

    /// <summary>أَسماءُ أَنواعِ تَجميعاتِ <c>ACommerce*</c> بِجِوارِ الاختِبار — عَدا تَجميعِه.</summary>
    internal static IReadOnlySet<string> ProductTypeNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dll in Directory.EnumerateFiles(AppContext.BaseDirectory, "ACommerce*.dll"))
        {
            if (Path.GetFileNameWithoutExtension(dll) == typeof(MeterClassificationTests).Assembly.GetName().Name) continue;
            Type?[] types;
            try { types = Assembly.LoadFrom(dll).GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types; }
            foreach (var t in types) if (t is not null && !t.Name.StartsWith('<')) names.Add(t.Name);
        }
        return names;
    }

    /// <summary>اللَقَطاتُ المُباشِرَة: <c>Projections.Snapshot&lt;X&gt;(SnapshotLifecycle.Inline)</c> في libs/apps — مَوضِعُ تَسجيلِها.</summary>
    internal static IReadOnlyList<string> InlineSnapshots() =>
        [.. EntitlementContractTests.SourceFiles()
            .SelectMany(f => Regex.Matches(WriteEndpointGuardTests.StripComments(f.Text),
                @"Projections\s*\.\s*Snapshot\s*<\s*(?:[A-Za-z_]\w*\.)*(?<t>[A-Za-z_]\w*)\s*>\s*\(\s*SnapshotLifecycle\s*\.\s*Inline"))
            .Select(m => m.Groups["t"].Value).Distinct().Order(StringComparer.Ordinal)];

    /// <summary><b>أَداةٌ ثالِثَةٌ تَقيسُ الثانِيَة</b> (القاعِدَة ١٠): كُلُّ حَدَثٍ لَه <c>Apply</c> يُبنى (<c>new X(</c>) في مِلَفٍّ مِن libs/apps غَيرِ مِلَفِّ تَعريفِه —
    /// نَسَقُ <c>AppliedEventEmitterTests.EmitterSites</c> بِالبَذّارِ مَعَه. فَحَدَثٌ يُبنى ولا يَراهُ ماسِحُ المَواضِعِ يَعني ماسِحاً أَعمى عَن شَكلٍ جَديد.</summary>
    internal static IReadOnlyList<string> ConstructedAnywhere(IEnumerable<string> names, IEnumerable<(string File, string Text)> sources)
    {
        var code = sources.Select(f => WriteEndpointGuardTests.StripComments(f.Text)).ToList();
        return [.. names.Where(name => code.Any(c =>
                !Regex.IsMatch(c, $@"\b(?:record|class|struct)\s+{Regex.Escape(name)}\b")
                && Regex.IsMatch(c, $@"\bnew\s+(?:[A-Za-z_]\w*\.)*{Regex.Escape(name)}\s*[\(\{{]")))
            .Order(StringComparer.Ordinal)];
    }

    // ─── التَشغيل: الإنشاءُ المُصطَنَعُ يَمُرُّ بِالمُصَنِّف ──────────────────────────────────────────────────────────────

    /// <summary>شَكلُ إنشاءٍ مُصطَنَع: الثَلاثَةُ الَّتي يَعرِفُها الدَفتَر، والتَحديثُ (وَثيقَةٌ لَها نُسخَة) لِلوَثائِقِ كُلِّها.</summary>
    internal enum Shape { OptimisticStore, Insert, StartStream, UpdateWithVersion }

    /// <summary>تَشغيلَةٌ واحِدَة: النَوعُ والشَكلُ وما رَأَتهُ الجَلسَةُ وما أَعطاهُ المُصَنِّف.</summary>
    internal sealed record SyntheticRun(MeterClassificationRow Row, Shape Shape, int PendingDocuments, int PendingStreams, IReadOnlyList<UsageEntry> Entries);

    private static readonly MethodInfo VersionForOpen = typeof(IQuerySession).GetMethod(nameof(IQuerySession.VersionFor))!;

    /// <summary>الأَشكالُ الَّتي يُشَغِّلُها الحارِسُ لِصَفّ: المَعدودُ بِشَكلِ وِلادَتِه وَحدَه، والحَدَثُ بِبِدءِ مَجرى، والوَثيقَةُ بِكُلِّ شَكل.</summary>
    internal static IReadOnlyList<Shape> ShapesFor(MeterClassificationRow row) => row switch
    {
        { Disposition: MeterDisposition.Counted, Creation: MeterCreation.OptimisticStore } => [Shape.OptimisticStore, Shape.UpdateWithVersion],
        { Disposition: MeterDisposition.Counted, Creation: MeterCreation.Insert } => [Shape.Insert, Shape.UpdateWithVersion],
        { Disposition: MeterDisposition.Counted, Creation: MeterCreation.StartStream } => [Shape.StartStream],
        { Kind: MeterTypeKind.Event } => [Shape.StartStream],
        _ => [Shape.Insert, Shape.OptimisticStore, Shape.UpdateWithVersion],
    };

    /// <summary>
    /// <para>يُنشِئُ كُلَّ صَفٍّ بِأَشكالِه في جَلسَةٍ مِن المَخزَنِ المُرَكَّب — بِلا حِفظٍ فَلا قاعِدَة — ويَبني مُدخَلَ المُصَنِّفِ مِن تَغييراتِها المُعَلَّقَة
    /// بِما يَقرَؤُه المُستَمِع: الوَثيقَةُ، و«وِلادَةٌ» = <c>VersionFor == null</c>. والتَحديثُ وَحدَه بِلا جَلسَة: الوَثيقَةُ بِنُسخَةٍ (<c>Created: false</c>)،
    /// لِأَنّ النُسخَةَ لا تَكونُ إلّا لِوَثيقَةٍ حُمِّلَت مِن القاعِدَة.</para>
    /// </summary>
    internal static IReadOnlyList<SyntheticRun> Run(DocumentStore store, IEnumerable<MeterClassificationRow> rows)
    {
        var runs = new List<SyntheticRun>();
        var n = 0;
        foreach (var row in rows)
            foreach (var shape in ShapesFor(row))
            {
                var instance = Synthesize(row.ClrType, ++n);
                if (shape == Shape.UpdateWithVersion)
                {
                    var update = new PendingSnapshot(SyntheticTenant, [new PendingDocument(instance, Created: false)]);
                    runs.Add(new SyntheticRun(row, shape, 1, 0, MeterRules.Classify(update, null, MeterUnitCatalog.Default)));
                    continue;
                }

                using var session = store.LightweightSession(SyntheticTenant);
                switch (shape)
                {
                    case Shape.Insert: session.InsertObjects([instance]); break;
                    case Shape.OptimisticStore: session.StoreObjects([instance]); break;
                    case Shape.StartStream: session.Events.StartStream(Guid.NewGuid(), instance); break;
                }
                var snapshot = SnapshotOf(session);
                runs.Add(new SyntheticRun(row, shape, snapshot.Documents.Count, session.PendingChanges.Streams().Count,
                    MeterRules.Classify(snapshot, null, MeterUnitCatalog.Default)));
            }
        return runs;
    }

    /// <summary>مُدخَلُ المُصَنِّفِ مِن جَلسَةٍ بِما يَقرَؤُه المُستَمِع: كُلُّ وَثيقَةٍ مُعَلَّقَة (<c>Inserts</c> ∪ <c>Updates</c>)، و«وِلادَةٌ» = <c>VersionFor == null</c>.
    /// <b>ولا قَناةَ مَجرى فيه</b>، لِأَنّ <see cref="PendingSnapshot"/> بِلا قَناةٍ عِندَ الرَأس — والبَندُ ٧ يُضيفُها هُنا حينَ يُضيفُها إلى المُدخَل.
    /// ‏(‏2026-11-12، W13-6·٧) <b>وأُضيفَت</b>: المَجاري المُعَلَّقَةُ بِـ<see cref="MeterRules.StreamsOf"/> — ما يُمَرِّرُه المُستَمِع.</summary>
    internal static PendingSnapshot SnapshotOf(IDocumentSession session)
    {
        var pending = session.PendingChanges;
        return new PendingSnapshot(session.TenantId, [.. pending.Inserts().Concat(pending.Updates())
            .Select(d => new PendingDocument(d, VersionForOpen.MakeGenericMethod(d.GetType()).Invoke(session, [d]) is null))],
            MeterRules.StreamsOf(pending.Streams()));
    }

    /// <summary>نُسخَةٌ مِن النَوعِ بِمُعَرِّفٍ جَديد: بِمُنشِئِه الفارِغِ إن كانَ لَه، وإلّا بِلا مُنشِئ. والمُعَرِّفُ <c>Id</c> بِنَوعِه.</summary>
    internal static object Synthesize(Type type, int n)
    {
        var ctor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes);
        var instance = ctor is not null ? ctor.Invoke(null) : RuntimeHelpers.GetUninitializedObject(type);
        var id = type.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public);
        if (id is { CanWrite: true })
        {
            object? value = id.PropertyType switch
            {
                var t when t == typeof(Guid) => Guid.NewGuid(),
                var t when t == typeof(string) => $"synthetic-{n}",
                var t when t == typeof(int) => n,
                var t when t == typeof(long) => (long)n,
                _ => null,
            };
            if (value is not null) id.SetValue(instance, value);
        }
        return instance;
    }

    // ─── الحُكم ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <para>كُلُّ خَرقٍ بِرَمزِه — والقائِمَةُ الفارِغَةُ وَحدَها خَضراء. يُنادى بِالدَفتَرِ الحَقيقِيِّ وبِنُسَخٍ مَحقونَة (صُفوفُ الحَقنِ أَدناه):</para>
    /// <list type="bullet">
    ///   <item><c>unclassified_document</c>/<c>unclassified_event</c>: نَوعٌ تَراهُ أَداةٌ بِلا صَفّ. <c>stale_row</c>: صَفٌّ لا تَراهُ أَداة.
    ///   <c>duplicate_row</c>: صَفّانِ لِنَوعٍ واحِد. <c>reason_missing</c>: سَبَبٌ أَو قاعِدَةٌ أَقصَرُ مِن أَن تَكون.</item>
    ///   <item><c>counted_unit_unknown</c>: وَحدَةُ المَعدودِ لَيسَت في المُعجَمِ وَحدَةَ حِفظ. <c>counted_shape_mismatch</c>: شَكلُ الوِلادَةِ لا يُوافِقُ نَوعَه.</item>
    ///   <item><c>counted_rule_missing</c>: وِلادَةُ المَعدودِ لا تُعطي سَطراً واحِداً مِن وَحدَتِه. <c>uncounted_yields_entry</c>: مُشتَقٌّ أَو غَيرُ مَعدودٍ
    ///   (أَو تَحديثُ مَعدود) يُعطي سَطراً.</item>
    ///   <item><c>lexicon_unit_without_row</c>: وَحدَةُ حِفظٍ في المُعجَمِ لا يَعُدُّها صَفّ. <c>later_unit_in_lexicon</c>: وَحدَةٌ آتِيَةٌ دَخَلَت المُعجَمَ وصَفُّها
    ///   لَم يُقلَب.</item>
    ///   <item><c>derived_not_inline</c>/<c>inline_not_derived</c>: المُشتَقُّ هُوَ اللَقطَةُ المُباشِرَةُ بِعَينِها.</item>
    /// </list>
    /// <para>‏(‏2026-11-12، W13-6·B-R2·1 — مُراجَعَةُ المَرحَلَةِ ب الثانِيَة · M6B-CNT-R2-1؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>وشَكلُ الوِلادَةِ لِكُلِّ مَوضِعٍ لا لِكُلِّ نَوع</b>:
    /// <c>counted_birth_off_shape:{النَوع}@{المِلَفّ}:{السَطر}</c> — مَوضِعٌ يَبني حَدَثاً مَعدوداً بِشَكلِ <see cref="MeterCreation.StartStream"/> ولَيسَ <c>Events.StartStream</c>.
    /// كانَ التَشغيلُ المُصطَنَعُ يَقيسُ النَوعَ (بِدءُ مَجرىً مُصطَنَعٍ يُعطي سَطراً) والماسِحُ يُسَجِّلُ فِعلَ كُلِّ مَوضِعٍ ولا يُقارِنُه بِالدَفتَر: بابُ البَلاغِ (MTE:1167)
    /// وفَرعُ الصُوَرِ (MTE:1301) إن صارا <c>Append</c> كُتِبَت التَذكِرَةُ والإعلانُ بِلا سَطرٍ وكُلُّ حارِسٍ أَخضَر (مَقيسٌ في المُراجَعَة). و<paramref name="sites"/> اختِيارِيٌّ:
    /// الصُفوفُ الَّتي تَقيسُ الدَفتَرَ وَحدَه لا تُمَرِّرُه.</para>
    /// </summary>
    internal static IReadOnlyList<string> Breaches(
        IReadOnlyList<MeterClassificationRow> rows, IReadOnlyList<string> documents, IReadOnlyList<string> events,
        IReadOnlyList<string> inlineSnapshots, IReadOnlyList<SyntheticRun> runs, MeterUnitCatalog catalog,
        IReadOnlyList<EventSite>? sites = null)
    {
        var breaches = new List<string>();
        var docRows = rows.Where(r => r.Kind == MeterTypeKind.Document).ToList();
        var eventRows = rows.Where(r => r.Kind == MeterTypeKind.Event).ToList();

        foreach (var dup in rows.GroupBy(r => r.TypeName.ToLowerInvariant()).Where(g => g.Count() > 1))
            breaches.Add($"duplicate_row:{dup.Key}");

        var docNames = docRows.Select(r => r.TypeName.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var eventNames = eventRows.Select(r => r.TypeName).ToHashSet(StringComparer.Ordinal);
        breaches.AddRange(documents.Where(d => !docNames.Contains(d)).Select(d => $"unclassified_document:{d}"));
        breaches.AddRange(events.Where(e => !eventNames.Contains(e)).Select(e => $"unclassified_event:{e}"));
        breaches.AddRange(docRows.Where(r => !documents.Contains(r.TypeName.ToLowerInvariant())).Select(r => $"stale_row:{r.TypeName}"));
        breaches.AddRange(eventRows.Where(r => !events.Contains(r.TypeName)).Select(r => $"stale_row:{r.TypeName}"));

        foreach (var r in rows.Where(r => string.IsNullOrWhiteSpace(r.WhyAr) || r.WhyAr.Trim().Length < 20))
            breaches.Add($"reason_missing:{r.TypeName}");

        var persistence = catalog.Units.Where(u => u.Boundary == MeterBoundaries.Persistence).Select(u => u.Code).ToHashSet(StringComparer.Ordinal);
        foreach (var r in rows.Where(r => r.Disposition == MeterDisposition.Counted))
        {
            if (r.Unit is null || !persistence.Contains(r.Unit)) breaches.Add($"counted_unit_unknown:{r.TypeName}->{r.Unit}");
            var expectedKind = r.Creation == MeterCreation.StartStream ? MeterTypeKind.Event : MeterTypeKind.Document;
            if (r.Creation is null || r.Kind != expectedKind) breaches.Add($"counted_shape_mismatch:{r.TypeName}");
        }
        foreach (var unit in persistence.Where(u => !rows.Any(r => r.Disposition == MeterDisposition.Counted && r.Unit == u)))
            breaches.Add($"lexicon_unit_without_row:{unit}");
        // ‏(‏2026-11-12، W13-6·٧) والمُشتَقُّ الَّذي يَعُدُّ صَفُّ حَدَثِه وَحدَتَه لَيسَ «آتِياً»: وَحدَتُه وَحدَةُ الحَدَثِ الَّذي يُعَدُّ مَكانَه، فَإن عَدَّها صَفٌّ فَقَد قُلِبَ.
        foreach (var r in rows.Where(r => r.Disposition != MeterDisposition.Counted && r.Unit is not null && catalog.Find(r.Unit) is not null
                                          && !(r.Disposition == MeterDisposition.Derived
                                               && rows.Any(c => c.Disposition == MeterDisposition.Counted && c.Unit == r.Unit))))
            breaches.Add($"later_unit_in_lexicon:{r.TypeName}->{r.Unit}");

        var derived = rows.Where(r => r.Disposition == MeterDisposition.Derived).Select(r => r.TypeName).ToHashSet(StringComparer.Ordinal);
        breaches.AddRange(derived.Where(d => !inlineSnapshots.Contains(d)).Select(d => $"derived_not_inline:{d}"));
        breaches.AddRange(inlineSnapshots.Where(i => !derived.Contains(i)).Select(i => $"inline_not_derived:{i}"));

        foreach (var run in runs)
        {
            var counts = run.Row is { Disposition: MeterDisposition.Counted } && run.Shape != Shape.UpdateWithVersion;
            if (counts && !(run.Entries.Count == 1 && run.Entries[0].Unit == run.Row.Unit))
                breaches.Add($"counted_rule_missing:{run.Row.TypeName}@{run.Shape} ({run.Entries.Count} سَطراً، {run.PendingDocuments} وَثيقَة · {run.PendingStreams} مَجرى)");
            if (!counts && run.Entries.Count > 0)
                breaches.Add($"uncounted_yields_entry:{run.Row.TypeName}@{run.Shape} ({string.Join(", ", run.Entries.Select(e => e.Unit))})");
        }

        // ‏(‏2026-11-12، W13-6·B-R2·1) المَعدودُ بِبِدءِ مَجرىً يُبنى في مَوضِعِ `Events.StartStream` — كُلُّ مَوضِعٍ بِسَطرِه، لا النَوعُ مَرَّةً واحِدَة.
        var bornAtStart = rows.Where(r => r is { Disposition: MeterDisposition.Counted, Creation: MeterCreation.StartStream })
                              .Select(r => r.TypeName).ToHashSet(StringComparer.Ordinal);
        foreach (var site in (sites ?? []).Where(s => s.Call != "StartStream"))
            breaches.AddRange(site.Types.Where(bornAtStart.Contains).Select(t => $"counted_birth_off_shape:{t}@{site.File}:{site.Line}"));
        return breaches;
    }

    // ─── الصُفوف ────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Every_document_and_event_type_is_classified_and_each_counted_birth_is_one_entry_of_its_unit()
    {
        var docs = await SeeDocuments();
        var events = SeeEvents();
        var inline = InlineSnapshots();
        var rows = MeterClassification.All;
        var runs = await MartenSchemaVerbTests.WithComposedStore(Environments.Production, s => Run(s, rows));

        output.WriteLine($"[G3] الوَثائِق: التَخارُج {docs.Export.Count} · المُستَعمَل (نَصّ ∪ IL) {docs.Used.Count} · المُسَجَّل في التَركيب {docs.Registered.Count} · الاتِّحاد {docs.All.Count}");
        output.WriteLine($"[G3] الأَحداث: Apply {events.Applied.Count} · مَواضِعُ الإلحاق {events.Sites.Count} (Append {events.Sites.Count(s => s.Call == "Append")} · StartStream {events.Sites.Count(s => s.Call == "StartStream")}) "
                         + $"تَبني {events.Constructed.Count} نَوعاً · الاتِّحاد {events.All.Count} · مَبنِيٌّ بِلا Apply {events.Constructed.Except(events.Applied).Count()}");
        foreach (var site in events.Sites) output.WriteLine($"[G3]   {site.File}:{site.Line} {site.Call} → {string.Join(", ", site.Types)}");
        output.WriteLine($"[G3] اللَقَطاتُ المُباشِرَة {inline.Count}: {string.Join(", ", inline)}");
        output.WriteLine($"[G3] الدَفتَر {rows.Count}: مَعدود {rows.Count(r => r.Disposition == MeterDisposition.Counted)} · مُشتَقّ {rows.Count(r => r.Disposition == MeterDisposition.Derived)} · "
                         + $"غَيرُ مَعدود {rows.Count(r => r.Disposition == MeterDisposition.NotCounted)} (مِنها بِوَحدَةٍ آتِيَة {rows.Count(r => r.Disposition == MeterDisposition.NotCounted && r.Unit is not null)})");
        output.WriteLine($"[G3] الإنشاءاتُ المُصطَنَعَة {runs.Count}: " + string.Join(" · ", runs.GroupBy(r => r.Shape).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"))
                         + $" · سُطورٌ أَعطاها المُصَنِّف {runs.Sum(r => r.Entries.Count)} ({string.Join(", ", runs.SelectMany(r => r.Entries).GroupBy(e => e.Unit).Select(g => $"{g.Key} {g.Count()}"))})"
                         + $" · مَجارٍ بَدَأَت {runs.Sum(r => r.PendingStreams)} · وَثائِقُ بَلَغَت المُصَنِّفَ مِنها {runs.Where(r => r.Shape == Shape.StartStream).Sum(r => r.PendingDocuments)}");

        Assert.True(docs.Export.Count >= 30 && docs.Used.Count >= 30 && docs.Registered.Count >= 20, $"أَداةٌ عَمياء: {docs.Export.Count} · {docs.Used.Count} · {docs.Registered.Count}.");
        Assert.True(events.Applied.Count >= 20 && events.Sites.Count >= 25, $"أَداةٌ عَمياء: Apply {events.Applied.Count} · مَواضِع {events.Sites.Count}.");
        Assert.True(inline.Count > 0, "أَداةٌ عَمياء: صِفرُ لَقطَةٍ مُباشِرَة.");
        var blind = events.Sites.Where(s => s.Types.Count == 0).Select(s => $"{s.File}:{s.Line}").ToList();
        Assert.True(blind.Count == 0, "مَوضِعُ إلحاقٍ لا تَرى الأَداةُ ما يُبنى فيه: " + string.Join(", ", blind));
        var anywhere = ConstructedAnywhere(events.Applied, EntitlementContractTests.SourceFiles());
        var unseen = anywhere.Except(events.Constructed).ToList();
        output.WriteLine($"[G3] أَحداثُ Apply المَبنِيَّةُ في المُنتَج {anywhere.Count} · يَراها ماسِحُ المَواضِع {anywhere.Count - unseen.Count} · لا تُبنى (يَتامى) "
                         + $"{events.Applied.Count - anywhere.Count}: {string.Join(", ", events.Applied.Except(anywhere))}");
        Assert.True(anywhere.Count >= 10, $"أَداةٌ عَمياء: {anywhere.Count} حَدَثاً مَبنِيّاً.");
        Assert.True(unseen.Count == 0, "حَدَثٌ يُبنى في المُنتَجِ ولا يَراهُ ماسِحُ المَواضِع: " + string.Join(", ", unseen));
        Assert.True(runs.Count >= rows.Count && runs.Sum(r => r.PendingStreams) > 0, $"أَداةٌ عَمياء: {runs.Count} إنشاءً لِـ{rows.Count} صَفّاً.");

        // ‏(‏2026-11-12، W13-6·B-R2·1) والمَواضِعُ مَعَه: كُلُّ مَوضِعٍ يَبني مَعدوداً بِبِدءِ مَجرىً يُطبَعُ بِفِعلِه — والعَدُّ صِفرٌ عَمى.
        var bornAtStart = rows.Where(r => r is { Disposition: MeterDisposition.Counted, Creation: MeterCreation.StartStream }).Select(r => r.TypeName).ToHashSet(StringComparer.Ordinal);
        var birthSites = events.Sites.Where(s => s.Types.Any(bornAtStart.Contains)).ToList();
        output.WriteLine($"[G3] مَواضِعُ تَبني مَعدوداً بِبِدءِ مَجرى {birthSites.Count} (StartStream {birthSites.Count(s => s.Call == "StartStream")} · غَيرُه {birthSites.Count(s => s.Call != "StartStream")}): "
                         + string.Join(" · ", birthSites.Select(s => $"{s.File[(s.File.LastIndexOf('/') + 1)..]}:{s.Line} {s.Call} {string.Join("+", s.Types.Where(bornAtStart.Contains))}")));
        Assert.True(bornAtStart.Count >= 4 && bornAtStart.All(t => birthSites.Any(s => s.Types.Contains(t))),
            $"أَداةٌ عَمياء: مَعدودٌ بِبِدءِ مَجرىً لا يُرى مَوضِعُه — {string.Join(", ", bornAtStart.Where(t => !birthSites.Any(s => s.Types.Contains(t))))}.");

        var breaches = Breaches(rows, docs.All, events.All, inline, runs, MeterUnitCatalog.Default, events.Sites);
        output.WriteLine($"[G3] خُروق {breaches.Count}");
        Assert.True(breaches.Count == 0, "دَفتَرُ التَصنيف:\n  " + string.Join("\n  ", breaches));
    }

    // ─── حَقنُ العُيوب — الأَداةُ تُقاسُ قَبلَ أَن يُوثَقَ بِها (القاعِدَة ١٠) ─────────────────────────────────────────────

    /// <summary>
    /// <para><b>ماسِحُ المَواضِعِ يَرى كُلَّ شَكلٍ يَدَّعيه</b>: مَصدَرٌ مَصنوعٌ بِأَشكالِ المُستَودَعِ نَفسِها — بِناءٌ في الوَسيط (MTE:706)، ومُتَغَيِّرٌ (MTE:3527)،
    /// وتَفكيكٌ مِن دالَّةٍ تُعيدُ صَفّاً (ListingEditService:163)، وقائِمَةٌ بِمُهَيِّئٍ و<c>.Add</c> (AppearanceBaselineSeeder:663)، ومُتَغَيِّرُ نَمَطٍ مِن دالَّةٍ
    /// مُساعِدَة (AccountErasure:610)، ومُتَغَيِّرُ foreach، ونُسخَةٌ مُتَوَقَّعَةٌ قَبلَ الحَدَث (SubscriptionEntitlements:96). والمَذكورُ في تَعليقٍ لا يُعَدّ، ووَسيطٌ
    /// لا يُعرَفُ ما فيه يَبقى بِلا أَنواعٍ فَيُحمِرُّه صَفُّ الحارِس — لا يَسقُطُ صامِتاً.</para>
    /// </summary>
    [Fact]
    public void The_site_scanner_sees_each_shape_the_repo_uses_and_leaves_an_unknown_argument_visible()
    {
        const string Source = """
            class Injected
            {
                void A(IDocumentSession s, Guid id) { s.Events.Append(id, new InjDirect(id)); }
                void B(IDocumentSession s, Guid id) { var ev = new InjVar(id); s.Events.StartStream<Agg>(id, ev); }
                void C(IDocumentSession s, Guid id) { var (ev, code) = Decide(id); s.Events.Append(id, ev!); }
                static (object?, string?) Decide(Guid id) => (new InjDecon(id, "a, b (c)"), null);
                void D(IDocumentSession s, Guid id) { var events = new List<object> { new InjListInit(id) }; events.Add(new InjListAdd(id)); s.Events.StartStream<Agg>(id, events.ToArray()); }
                void E(IDocumentSession s, Agg a) { if (Make(a) is { Count: > 0 } events) { s.Events.Append(a.Id, events.ToArray()); } }
                static IReadOnlyList<object> Make(Agg a) { var l = new List<object>(); l.Add(new InjHelper(a.Id)); return l; }
                void F(IDocumentSession s, Guid id) { foreach (var x in Pending(id)) s.Events.Append(id, x); }
                static IEnumerable<object> Pending(Guid id) { yield return new InjForeach(id); }
                void G(IDocumentSession s, Guid id) { s.Events.Append(id, 3, new InjExpected(id)); }
                // s.Events.Append(id, new InjComment(id));
                void H(IDocumentSession s, Guid id, object mystery) { s.Events.Append(id, mystery); }
            }
            """;
        var sites = EventSites([("libs/injected/Injected.cs", Source)], t => t.StartsWith("Inj", StringComparison.Ordinal));
        foreach (var site in sites) output.WriteLine($"[G3·أَداة] :{site.Line} {site.Call} → {string.Join(", ", site.Types)}");
        Assert.Equal(
            [
                ["InjDirect"], ["InjVar"], ["InjDecon"], ["InjListAdd", "InjListInit"], ["InjHelper"], ["InjForeach"], ["InjExpected"], [],
            ],
            sites.Select(s => s.Types.ToArray()).ToArray());
        Assert.Equal(["Append", "StartStream", "Append", "StartStream", "Append", "Append", "Append", "Append"], sites.Select(s => s.Call));
        Assert.DoesNotContain(sites, s => s.Types.Contains("InjComment"));
    }

    private sealed class InjectedDocument { public Guid Id { get; set; } }

    /// <summary>
    /// <para>‏(‏2026-11-12، W13-6·B-R2·1 — مُراجَعَةُ المَرحَلَةِ ب الثانِيَة · M6B-CNT-R2-1) <b>الحَقنُ الَّذي قاسَتهُ المُراجَعَة، في نُسخَةٍ مِن المِلَفِّ الحَقيقِيّ</b>:
    /// بابُ البَلاغِ عَن إعلان (<c>POST /{slug}/listings/{id}/report</c>) يَعودُ فيه <c>StartStream&lt;Ticket&gt;(ev.Id, ev)</c> إلى <c>Append(ev.Id, ev)</c>، وفَرعُ
    /// الإعلانِ بِصُوَرِه يَعودُ فيه <c>StartStream&lt;Listing&gt;(id, ev, …)</c> إلى <c>Append(id, ev, …)</c> — وكِلاهُما كانَ أَخضَرَ في كُلِّ حارِس (التَذكِرَةُ تُكتَبُ
    /// و<c>ticket.open</c> صِفر). فَكُلٌّ يَحمَرُّ بِـ<c>counted_birth_off_shape</c> عَلى سَطرِه وَحدَه، والمَواضِعُ الأُخرى في المِلَفِّ — ومِنها بابُ الدَعمِ التَوأَمُ
    /// <c>/support/open</c> بِالجُملَةِ نَفسِها حَرفاً — تَبقى خُضراً: الحارِسُ يَحكُمُ المَوضِعَ لا النَوع.</para>
    /// </summary>
    [Fact]
    public void A_counted_birth_turned_into_an_append_at_the_report_door_or_the_photo_branch_is_red_on_its_own_line()
    {
        const string mte = "libs/templates/ACommerce.Templates.Customer.Marketplace/MarketplaceTemplateExtensions.cs";
        var productTypes = ProductTypeNames();
        var sources = EntitlementContractTests.SourceFiles().ToList();
        var text = Assert.Single(sources, f => Rel(f.File) == mte).Text;
        var rows = MeterClassification.All;
        IReadOnlyList<string> Off(string replaced)
        {
            var sites = EventSites(sources.Select(f => Rel(f.File) == mte ? (f.File, replaced) : f), productTypes.Contains);
            return [.. Breaches(rows, [], [], [], [], MeterUnitCatalog.Default, sites).Where(b => b.StartsWith("counted_birth_off_shape:", StringComparison.Ordinal))];
        }
        (string Injected, int Line) Inject(int from, string born, string append)
        {
            var at = text.IndexOf(born, from, StringComparison.Ordinal);
            Assert.True(at > from, $"«{born}» غائِبٌ بَعدَ مَوضِعِه — الأَداةُ عَمياء.");
            return (text[..at] + append + text[(at + born.Length)..], text.AsSpan(0, at).Count('\n') + 1);
        }

        const string ticketBirth = "s.Events.StartStream<ACommerce.Kit.Support.Ticket>(ev.Id, ev);";
        var reportDoor = text.IndexOf("\"/{slug}/listings/{id:guid}/report\"", StringComparison.Ordinal);
        var supportDoor = text.IndexOf("\"/{slug}/support/open\"", StringComparison.Ordinal);
        Assert.True(supportDoor > 0 && reportDoor > supportDoor, "البابانِ غائِبان — الأَداةُ عَمياء.");
        var (report, reportLine) = Inject(reportDoor, ticketBirth, "s.Events.Append(ev.Id, ev);");
        var (photo, photoLine) = Inject(0, "s.Events.StartStream<Listing>(id, ev,", "s.Events.Append(id, ev,");
        var (support, supportLine) = Inject(supportDoor, ticketBirth, "s.Events.Append(ev.Id, ev);");

        var real = Off(text);
        var cases = new (string Name, IReadOnlyList<string> Breaches, string[] Expected)[]
        {
            ("report door", Off(report), [$"counted_birth_off_shape:TicketCreated@{mte}:{reportLine}"]),
            ("photo branch", Off(photo), [$"counted_birth_off_shape:ListingCreated@{mte}:{photoLine}"]),
            ("support door (its twin)", Off(support), [$"counted_birth_off_shape:TicketCreated@{mte}:{supportLine}"]),
        };
        output.WriteLine($"[G3·مَوضِع] الشَجَرَةُ الحَقيقِيَّة {real.Count} خَرقاً");
        foreach (var (name, breaches, _) in cases) output.WriteLine($"[G3·مَوضِع] {name}: {string.Join(" | ", breaches)}");
        Assert.Empty(real);
        foreach (var (_, breaches, expected) in cases) Assert.Equal(expected, breaches);
    }

    /// <summary>
    /// <para><b>صَفٌّ مَعدودٌ بِلا قاعِدَةٍ يَحمَرّ</b> (حَقنُ G3 المُسَمّى في البَند): الدَفتَرُ الحَقيقِيُّ ونُسَخٌ مِنه بِصَفٍّ واحِدٍ مُبَدَّل، تَمُرُّ بِالتَشغيلِ نَفسِه:</para>
    /// <list type="number">
    ///   <item><c>Tenant</c> «مَعدودٌ» بِوَحدَةٍ في المُعجَم (<c>deal.create</c>) ولا قاعِدَةَ لَه ← إنشاؤُه صِفر: <c>counted_rule_missing</c>.</item>
    ///   <item><c>ListingCreated</c> «مَعدودٌ» قَبلَ البَند ٧ ← وَحدَتُه لَيسَت في المُعجَم، ولا قَناةَ مَجرىً إلى المُصَنِّف: الرَمزان.
    ///   ‏(‏2026-11-12، W13-6·٧) صارَ مَعدوداً بِقاعِدَتِه، فَالرَمزانِ نَفسُهُما بِحَدَثٍ «مَعدودٍ» بِوَحدَةٍ خارِجَ المُعجَمِ ولا قاعِدَةَ لَه: <c>ListingEdited</c>.</item>
    ///   <item><c>Deal</c> «غَيرُ مَعدود» والمُصَنِّفُ يَعُدُّه ← <c>uncounted_yields_entry</c> بِكُلِّ شَكل، ووَحدَتُه في المُعجَمِ بِلا صَفّ.</item>
    /// </list>
    /// <para>‏(‏2026-11-12، W13-6·٨ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <c>Tenant</c> صارَ مَعدوداً بِقاعِدَتِه (<c>store.create</c>)، فَحَقنُ الصَفِّ الأَوَّلِ بِنَوعٍ
    /// لا يُعَدُّ ولا قاعِدَةَ لَه أَبَداً: <c>Cart</c> — والرَمزُ نَفسُه.</para>
    /// </summary>
    [Fact]
    public async Task A_counted_row_whose_rule_is_missing_and_a_rule_on_an_uncounted_row_are_red()
    {
        var docs = await SeeDocuments();
        var events = SeeEvents();
        var inline = InlineSnapshots();
        static IReadOnlyList<MeterClassificationRow> Swap(Type type, MeterClassificationRow row)
            => [.. MeterClassification.All.Select(r => r.ClrType == type ? row : r)];

        var cases = new (string Name, IReadOnlyList<MeterClassificationRow> Rows, string[] Expected)[]
        {
            ("Cart counted with no rule",
             Swap(typeof(ACommerce.Kit.Cart.Cart), MeterClassification.Counted(typeof(ACommerce.Kit.Cart.Cart), "deal.create", MeterCreation.Insert, "حَقنٌ: مَعدودٌ بِلا قاعِدَةٍ في MeterRules.")),
             ["counted_rule_missing:Cart@Insert"]),
            ("ListingEdited counted with a unit outside the lexicon and no rule",
             Swap(typeof(ACommerce.Kit.Listings.ListingEdited), MeterClassification.Counted(typeof(ACommerce.Kit.Listings.ListingEdited), "listing.edit", MeterCreation.StartStream, "حَقنٌ: حَدَثٌ مَعدودٌ بِلا وَحدَةٍ ولا قاعِدَة.")),
             ["counted_unit_unknown:ListingEdited->listing.edit", "counted_rule_missing:ListingEdited@StartStream"]),
            ("Deal not counted while the rule counts it",
             Swap(typeof(Deal), MeterClassification.NotCounted(typeof(Deal), MeterTypeKind.Document, "حَقنٌ: الصَفقَةُ غَيرُ مَعدودَةٍ والمُصَنِّفُ يَعُدُّها.")),
             ["uncounted_yields_entry:Deal@Insert", "uncounted_yields_entry:Deal@OptimisticStore", "lexicon_unit_without_row:deal.create"]),
        };

        foreach (var (name, rows, expected) in cases)
        {
            var runs = await MartenSchemaVerbTests.WithComposedStore(Environments.Production, s => Run(s, rows));
            var breaches = Breaches(rows, docs.All, events.All, inline, runs, MeterUnitCatalog.Default);
            output.WriteLine($"[G3·حَقن] {name}: {breaches.Count} — {string.Join(" | ", breaches)}");
            Assert.Equal(expected.Order(StringComparer.Ordinal), breaches.Select(b => b.Split(' ')[0]).Order(StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// <para><b>والمُطابَقَةُ بِالاتِّجاهَين</b>: نَوعٌ تَراهُ أَداةٌ بِلا صَفٍّ (وَثيقَةٌ وحَدَث)، وصَفٌّ مَحذوف، وصَفٌّ لِنَوعٍ لا تَراهُ أَداة، ومُشتَقٌّ لَيسَ لَقطَة،
    /// ولَقطَةٌ لَيسَت مُشتَقّاً، وصَفّانِ لِنَوعٍ واحِد، ومَعدودٌ بِلا شَكلِ وِلادَة، وسَبَبٌ فارِغ، ووَحدَةٌ آتِيَةٌ تَدخُلُ المُعجَمَ قَبلَ قَلبِ صُفوفِها — كُلٌّ
    /// بِرَمزِه وَحدَه. والدَفتَرُ الحَقيقِيُّ صِفرُ خَرقٍ قَبلَها: فَلِكُلِّ رَمزٍ مِن الثَلاثَةَ عَشَرَ سالِبٌ يُنتِجُه ومُوجِبٌ لا يُنتِجُه.</para>
    /// <para>‏(‏2026-11-12، W13-6·B-R2·1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>والرَمزُ الرابِعَ عَشَر</b> <c>counted_birth_off_shape</c>: مُوجِبُه الشَجَرَةُ الحَقيقِيَّةُ بِمَواضِعِها،
    /// وسالِبُه مَوضِعُ <c>Append</c> يَبني <c>TicketCreated</c>؛ وسالِباه بِنُسخَتَينِ مِن المِلَفِّ الحَقيقِيِّ في الصَفِّ التالي.</para>
    /// </summary>
    [Fact]
    public async Task An_unclassified_a_stale_a_misplaced_derived_and_a_premature_lexicon_unit_are_each_red_by_their_code()
    {
        var docs = await SeeDocuments();
        var events = SeeEvents();
        var inline = InlineSnapshots();
        var all = MeterClassification.All;
        var runs = await MartenSchemaVerbTests.WithComposedStore(Environments.Production, s => Run(s, all));
        Assert.Empty(Breaches(all, docs.All, events.All, inline, runs, MeterUnitCatalog.Default));
        Assert.Empty(Breaches(all, docs.All, events.All, inline, runs, MeterUnitCatalog.Default, events.Sites));   // ‏(W13-6·B-R2·1) المُوجِبُ بِالمَواضِع

        // ‏(‏2026-11-12، W13-6·٧) `listing.create` في المُعجَمِ الحَقيقِيِّ الآن، فَالحالُ «وَحدَةٌ دَخَلَت المُعجَمَ قَبلَ قَلبِ صَفِّها» يُصنَعُ بِإعادَةِ صَفِّ
        // `ListingCreated` إلى ما كانَ عَلَيه قَبلَ البَند ٧ — والرُموزُ الثَلاثَةُ نَفسُها.
        IReadOnlyList<MeterClassificationRow> unflipped = [.. all.Select(r => r.ClrType == typeof(ACommerce.Kit.Listings.ListingCreated)
            ? MeterClassification.NotCounted(r.ClrType, MeterTypeKind.Event, "حَقنٌ: صَفُّ الحَدَثِ قَبلَ قَلبِه، ووَحدَتُه في المُعجَم.", "listing.create") : r)];
        var cases = new (string Name, string[] Expected, IReadOnlyList<string> Breaches)[]
        {
            ("document seen, no row", ["unclassified_document:injecteddocument"],
             Breaches(all, [.. docs.All, "injecteddocument"], events.All, inline, runs, MeterUnitCatalog.Default)),
            ("event seen, no row", ["unclassified_event:InjectedEvent"],
             Breaches(all, docs.All, [.. events.All, "InjectedEvent"], inline, runs, MeterUnitCatalog.Default)),
            // ‏(‏2026-11-12، W13-6·٩) صَفُّ `Message` صارَ مَعدوداً (`chat.message`)، فَحَذفُه يُعطي مَعَ رَمزِه `lexicon_unit_without_row` — فَالحَذفُ بِصَفٍّ لا يَعُدُّ
            // ولا يَعُدُّه شَيء: `Conversation`، والرَمزُ نَفسُه وَحدَه.
            ("row removed", ["unclassified_document:conversation"],
             Breaches([.. all.Where(r => r.ClrType != typeof(ACommerce.Kit.Chat.Conversation))], docs.All, events.All, inline, runs, MeterUnitCatalog.Default)),
            ("row for an unseen type", ["stale_row:InjectedDocument"],
             Breaches([.. all, MeterClassification.NotCounted(typeof(InjectedDocument), MeterTypeKind.Document, "حَقنٌ: صَفٌّ لِنَوعٍ لا تَراهُ أَداة.")],
                      docs.All, events.All, inline, runs, MeterUnitCatalog.Default)),
            ("derived that is no snapshot, snapshot that is not derived", ["derived_not_inline:Cart", "inline_not_derived:Listing"],
             Breaches([.. all.Select(r => r.ClrType == typeof(ACommerce.Kit.Cart.Cart) ? MeterClassification.Derived(r.ClrType, "listing.create", "حَقنٌ: سَلَّةٌ «مُشتَقَّة».")
                         : r.ClrType == typeof(ACommerce.Kit.Listings.Listing) ? MeterClassification.NotCounted(r.ClrType, MeterTypeKind.Document, "حَقنٌ: لَقطَةٌ «غَيرُ مَعدودَة».") : r)],
                      docs.All, events.All, inline, runs, MeterUnitCatalog.Default)),
            ("two rows for one type", ["duplicate_row:deal"],
             Breaches([.. all, MeterClassification.NotCounted(typeof(Deal), MeterTypeKind.Document, "حَقنٌ: صَفٌّ ثانٍ لِلصَفقَةِ نَفسِها.")],
                      docs.All, events.All, inline, runs, MeterUnitCatalog.Default)),
            ("counted with no birth shape", ["counted_shape_mismatch:Deal"],
             Breaches([.. all.Select(r => r.ClrType == typeof(Deal) ? r with { Creation = null } : r)],
                      docs.All, events.All, inline, runs, MeterUnitCatalog.Default)),
            ("empty reason", ["reason_missing:Favorite"],
             Breaches([.. all.Select(r => r.ClrType == typeof(ACommerce.Kit.Favorites.Favorite) ? r with { WhyAr = "  " } : r)],
                      docs.All, events.All, inline, runs, MeterUnitCatalog.Default)),
            ("a later unit enters the lexicon before its rows flip",
             ["lexicon_unit_without_row:listing.create", "later_unit_in_lexicon:Listing->listing.create", "later_unit_in_lexicon:ListingCreated->listing.create"],
             Breaches(unflipped, docs.All, events.All, inline, runs, MeterUnitCatalog.Default)),
            // ‏(‏2026-11-12، W13-6·B-R2·1) الرَمزُ الرابِعَ عَشَر: مَوضِعُ `Append` يَبني `TicketCreated` (مَعدودٌ بِبِدءِ مَجرى) — والمَواضِعُ الحَقيقِيَّةُ مَعَه خُضر.
            ("a counted event built at an Append site", ["counted_birth_off_shape:TicketCreated@libs/injected/Report.cs:7"],
             Breaches(all, docs.All, events.All, inline, runs, MeterUnitCatalog.Default,
                      [.. events.Sites, new EventSite("libs/injected/Report.cs", 7, "Append", ["TicketCreated"])])),
        };
        foreach (var (name, expected, breaches) in cases)
        {
            output.WriteLine($"[G3·حَقن] {name}: {string.Join(" | ", breaches)}");
            Assert.Equal(expected.Order(StringComparer.Ordinal), breaches.Order(StringComparer.Ordinal));
        }
    }
}
