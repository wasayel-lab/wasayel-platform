using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ لِكُلِّ سِجِلِّ خَرقٍ شَكلٌ مُسَمّى — القاعِدَة ٤ وعائِلَتاها (‏2026-10-25، تَدقيقُ الإطلاق lens-identity-rules-3؛ ‏ADR-038) ═══════════
//
// **المَقيسُ قَبلَ** (عِندَ dace7cd): القاعِدَة ٤ تَكتُبُ شَكلاً واحِداً — `record XViolation(string Code, string MessageAr)` +
// `Validate(T) : IReadOnlyList<XViolation>` + `IsValid` — والكودُ بِثَلاثَة: قائِمَةٌ + `IsValid` (مُصادِقاتُ التَعريفاتِ والبَيانات)، وخَرقٌ أَوَّلُ
// (`XViolation?`) + `IsAllowed` (بَوّاباتُ التَخويلِ عَلى حالَةٍ مَخزونَة: القَبولُ والإلغاءُ والتَقييمُ والدَفعُ والشَحنُ وحَذفُ الحِساب)، وقَبولٌ
// (`Admit` ← سِجِلٌّ يَحمِلُ السُلطَةَ أَو الخَرق) + `IsAllowed` (هُوِيَّةُ البائِعِ وجِهَةُ تَواصُلِه). والانقِسامُ قَرارٌ مُسَجَّلٌ في سِجِلِّ المالِكِ
// «أ» وَحدَه — خارِجَ المُستَودَع — و`grep` لِاختِبارٍ يُثَبِّتُ الشَكلَ = صِفر. القاعِدَة ٢: الحَدُّ الَّذي لا يُقاسُ يَنهار.
//
// **وصارَ**: الشَكلُ يُقرَأُ بِالانعِكاسِ عَلى تَجميعاتِ `ACommerce` (نَسَقُ `AppliedEventEmitterTests`)، ويُقابَلُ بِمَسحِ المَصدَرِ فَلا يَختَبِئُ سِجِلٌّ
// في تَجميعٍ لَم يُحَمَّل (القاعِدَة ١٠). لِكُلِّ سِجِلٍّ: الشَكلُ `(string Code, string MessageAr)`، وعائِلَةٌ واحِدَةٌ عَلى الأَقَلِّ مِن المُعجَمِ المُغلَق
// `list`/`first`/`admission` في صِنفٍ واحِدٍ مَعَ دالَّتِها المَنطِقِيَّة — أَو تَثبيتٌ بِاسمِه وسَبَبِه في <see cref="PinnedUnnamed"/>، ثُنائِيَّ الاتِّجاه.

public class ViolationShapeTests(ITestOutputHelper output)
{
    /// <summary>العائِلاتُ — مُعجَمٌ مُغلَق (‏ADR-038 §٢).</summary>
    internal const string List = "list", First = "first", Admission = "admission";

    /// <summary>سِجِلُّ خَرقٍ بِلا عائِلَةٍ مُسَمّاةٍ اليَوم — ومَن يُنتِجُه، ولِماذا يَبقى. <b>تَتَقَلَّصُ ولا تَنمو.</b></summary>
    private sealed record Unnamed(string Violation, string Producer, string WhyAr);

    private static readonly Unnamed[] PinnedUnnamed =
    {
        new("ModelPriceViolation", "ModelPricingValidator",
            "قائِمَةٌ بِلا IsValid: مُستَهلِكُها الوَحيدُ مُحَمِّلُ مِلَفِّ الأَسعارِ (`ModelUsageMetering`) يَقرَأُ القائِمَةَ لِيَطبَعَ خُروقَها ويَرفُضَ المِلَفّ، " +
            "ولا سائِلَ «أَصالِحٌ؟» في وَقتِ التَشغيل — ودالَّةٌ بِلا مُستَهلِكٍ تَجريدٌ يَسبِقُه (القاعِدَة ١)."),
        new("OtoNoticeViolation", "OtoOrderStatusGate",
            "بَوّابَةٌ مَرحَلِيَّةٌ لِوارِدِ OTO: Admit عَلى الجِسمِ الخامّ، ثُمَّ Match عَلى الصَفقَةِ المَخزونَة، ثُمَّ Verify بِسِرِّ البائِع، ثُمَّ Decide عَلى " +
            "جَوابِ OTO — كُلٌّ خَرقٌ أَوَّلُ يَقرَأُ حالَةً غَيرَ الَّتي قَبلَه، ويُناديها مُعالِجٌ واحِد (`DealShipping`). وIsAllowed واحِدٌ يَدمُجُ مَراحِلَ " +
            "لا تَتَوَفَّرُ مُعطَياتُها مَعاً — فَلا مُستَهلِكَ لَه."),
    };

    // ─── الفَحص ───────────────────────────────────────────────────────

    [Fact]
    public void Every_violation_record_has_the_code_message_shape_and_a_named_family()
    {
        var types = AcommerceTypes();
        var violations = types.Where(IsViolationRecord).OrderBy(t => t.Name, StringComparer.Ordinal).ToList();

        // ‏المَصدَرُ يُقابِلُ الانعِكاس: سِجِلٌّ في تَجميعٍ لَم يُحَمَّل لا يَختَبِئ.
        var declared = SourceViolationNames();
        Assert.True(declared.Count >= 25, $"أَداةٌ عَمياء: {declared.Count} سِجِلَّ خَرقٍ في المَصدَر — والمَقيسُ اليَومَ 29.");
        var missing = declared.Except(violations.Select(t => t.Name), StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "سِجِلُّ خَرقٍ في المَصدَرِ لا يَراه الانعِكاس (تَجميعٌ غَيرُ مُحَمَّل؟): " + string.Join(", ", missing));

        var badShape = violations.Where(t => !HasCodeMessageShape(t)).Select(t => t.Name).ToList();
        Assert.True(badShape.Count == 0, "سِجِلُّ خَرقٍ بِغَيرِ `(string Code, string MessageAr)`: " + string.Join(", ", badShape));

        var unnamed = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal) { [List] = 0, [First] = 0, [Admission] = 0 };
        foreach (var v in violations)
        {
            var families = Families(v, types);
            foreach (var f in families) counts[f]++;
            output.WriteLine($"{v.Name}: {(families.Count == 0 ? "—" : string.Join("+", families))}");
            if (families.Count == 0) unnamed.Add(v.Name);
        }
        output.WriteLine($"سِجِلّاتُ خَرق {violations.Count} · list {counts[List]} · first {counts[First]} · admission {counts[Admission]} · بِلا عائِلَة {unnamed.Count}");

        var pinned = PinnedUnnamed.Select(p => p.Violation).ToList();
        var newUnnamed = unnamed.Except(pinned, StringComparer.Ordinal).ToList();
        Assert.True(newUnnamed.Count == 0,
            "سِجِلُّ خَرقٍ بِلا عائِلَةٍ مُسَمّاة — أَعطِه شَكلَ list (Validate ← IReadOnlyList + IsValid) أَو first (Validate ← X? + IsAllowed) " +
            "أَو admission (Admit ← سِجِلٌّ يَحمِلُه + IsAllowed) في صِنفٍ واحِد، أَو ثَبِّته في PinnedUnnamed بِسَبَبِه:\n  " + string.Join("\n  ", newUnnamed));

        var stale = pinned.Except(unnamed, StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0, "مُثَبَّتٌ صارَت لَه عائِلَةٌ ولَم يُرفَع مِن PinnedUnnamed:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>كُلُّ مُثَبَّتٍ يُسَمّي مُنتِجَه الحَقيقيَّ وسَبَبَه، ولا تَكرار.</summary>
    [Fact]
    public void Every_pinned_unnamed_violation_names_its_real_producer_and_a_reason()
    {
        var types = AcommerceTypes();
        foreach (var p in PinnedUnnamed)
        {
            var v = Assert.Single(types, t => t.Name == p.Violation && IsViolationRecord(t));
            var producer = Assert.Single(types, t => t.Name == p.Producer);
            Assert.True(StaticMethods(producer).Any(m => Carries(m.ReturnType, v)),
                $"«{p.Producer}» لا يُنتِجُ «{p.Violation}» — التَثبيتُ يَصِفُ ماضِياً.");
            Assert.True(p.WhyAr.Length > 60, $"سَبَبُ «{p.Violation}» أَقصَرُ مِن أَن يَكونَ سَبَباً.");
        }
        Assert.Equal(PinnedUnnamed.Length, PinnedUnnamed.Select(p => p.Violation).Distinct(StringComparer.Ordinal).Count());
    }

    // ─── حَقنُ العَيب — عَيِّناتٌ بِأَجوِبَةٍ مَعلومَة ──────────────────────────────

    public sealed record SampleListViolation(string Code, string MessageAr);
    public static class SampleListValidator
    {
        public static IReadOnlyList<SampleListViolation> Validate(string? s) => s is null ? [new("x", "س")] : [];
        public static bool IsValid(string? s) => Validate(s).Count == 0;
    }

    public sealed record SampleFirstViolation(string Code, string MessageAr);
    public static class SampleFirstGate
    {
        public static SampleFirstViolation? Validate(string? s) => s is null ? new("x", "س") : null;
        public static bool IsAllowed(string? s) => Validate(s) is null;
    }

    public sealed record SampleAdmitViolation(string Code, string MessageAr);
    public sealed record SampleAdmission(string? Subject, SampleAdmitViolation? Violation)
    {
        public static SampleAdmission Refused(SampleAdmitViolation v) => new(null, v);   // ‏مَصنَعٌ عَلى الحامِلِ نَفسِه — لا يُعَدُّ بَوّابَة
    }
    public static class SampleAdmitGate
    {
        public static SampleAdmission Admit(string? s) => s is null ? new(null, new("x", "س")) : new(s, null);
        public static bool IsAllowed(string? s) => Admit(s).Subject is not null;
    }

    public sealed record SampleNakedListViolation(string Code, string MessageAr);
    public static class SampleNakedListValidator
    {
        public static IReadOnlyList<SampleNakedListViolation> Validate(string? s) => [];   // ‏بِلا IsValid
    }

    public sealed record SampleSplitViolation(string Code, string MessageAr);
    public static class SampleSplitGate { public static SampleSplitViolation? Validate(string? s) => null; }
    public static class SampleSplitElsewhere { public static bool IsAllowed(string? s) => true; }   // ‏الدالَّةُ في صِنفٍ آخَر لا تَكفي

    public sealed record SampleFactoryOnlyViolation(string Code, string MessageAr);
    public sealed record SampleFactoryOnlyResult(bool Ok, SampleFactoryOnlyViolation? Violation)
    {
        public static SampleFactoryOnlyResult Refused(SampleFactoryOnlyViolation v) => new(false, v);
        public static bool IsAllowed(bool ok) => ok;
    }

    public sealed record SampleCodeOnlyViolation(string Code);

    [Fact]
    public void Every_family_and_every_breach_answers_its_injected_sample()
    {
        var mine = typeof(ViolationShapeTests).GetNestedTypes(BindingFlags.Public).ToList();
        Assert.True(mine.Count >= 12, $"أَداةٌ عَمياء: {mine.Count} عَيِّنَة.");

        var cases = new (Type Violation, string[] Families)[]
        {
            (typeof(SampleListViolation), [List]),
            (typeof(SampleFirstViolation), [First]),
            (typeof(SampleAdmitViolation), [Admission]),
            (typeof(SampleNakedListViolation), []),
            (typeof(SampleSplitViolation), []),
            (typeof(SampleFactoryOnlyViolation), []),
        };
        var missed = new List<string>();
        foreach (var (v, want) in cases)
        {
            var got = Families(v, mine);
            output.WriteLine($"{v.Name}: {(got.Count == 0 ? "—" : string.Join("+", got))}");
            if (!got.SequenceEqual(want)) missed.Add($"{v.Name}: المُنتَظَرُ [{string.Join(",", want)}] والمَقروء [{string.Join(",", got)}]");
        }

        Assert.True(IsViolationRecord(typeof(SampleCodeOnlyViolation)));
        if (HasCodeMessageShape(typeof(SampleCodeOnlyViolation))) missed.Add("SampleCodeOnlyViolation: شَكلُ (Code) وَحدَه مَرَّ");
        if (!HasCodeMessageShape(typeof(SampleListViolation))) missed.Add("SampleListViolation: الشَكلُ الصَحيحُ رُدّ");

        output.WriteLine($"حُقِنَ {cases.Length + 2} عَيِّنَة؛ فاتَ الفاحِصَ {missed.Count}");
        Assert.True(missed.Count == 0, string.Join("\n", missed));
    }

    // ─── الأَدَوات ────────────────────────────────────────────────────

    private static List<Type> AcommerceTypes()
    {
        var all = new List<Type>();
        foreach (var dll in Directory.EnumerateFiles(AppContext.BaseDirectory, "ACommerce*.dll"))
        {
            Type[] types;
            try { types = Assembly.LoadFrom(dll).GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.OfType<Type>().ToArray(); }
            catch { continue; }
            all.AddRange(types.Where(t => t.Namespace?.StartsWith("ACommerce", StringComparison.Ordinal) == true
                                       && !t.Namespace.StartsWith("ACommerce.Platform.Tests", StringComparison.Ordinal)));
        }
        return all;
    }

    /// <summary>سِجِلٌّ (<c>record</c>) اسمُه يَنتَهي بِـ<c>Violation</c>.</summary>
    internal static bool IsViolationRecord(Type t) =>
        t.IsClass && t.Name.EndsWith("Violation", StringComparison.Ordinal) && t.GetMethod("<Clone>$") is not null;

    /// <summary>المُنشِئُ العامُّ الوَحيدُ <c>(string Code, string MessageAr)</c>.</summary>
    internal static bool HasCodeMessageShape(Type t)
    {
        var ctors = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (ctors.Length != 1) return false;
        var ps = ctors[0].GetParameters();
        return ps.Length == 2
               && ps[0].ParameterType == typeof(string) && ps[0].Name == "Code"
               && ps[1].ParameterType == typeof(string) && ps[1].Name == "MessageAr";
    }

    private static IEnumerable<MethodInfo> StaticMethods(Type c) =>
        c.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName);

    private static bool HasBool(Type c, Func<string, bool> name) =>
        StaticMethods(c).Any(m => m.ReturnType == typeof(bool) && name(m.Name));

    /// <summary>نَوعٌ يَحمِلُ الخَرقَ: هُوَ نَفسُه، أَو قائِمَتُه، أَو سِجِلٌّ لَه خاصِّيَّةٌ مِنه.</summary>
    private static bool Carries(Type returned, Type v) =>
        returned == v || returned == typeof(IReadOnlyList<>).MakeGenericType(v)
        || returned.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(p => p.PropertyType == v);

    /// <summary>
    /// العائِلاتُ الَّتي يُحَقِّقُها سِجِلُّ الخَرقِ <paramref name="v"/> بَينَ <paramref name="types"/> — كُلٌّ في صِنفٍ واحِدٍ مَعَ دالَّتِها:
    /// <c>list</c> ‏<c>Validate*</c> ← <c>IReadOnlyList&lt;X&gt;</c> + <c>IsValid*</c>؛ <c>first</c> طَريقَةٌ ← <c>X</c> + <c>IsAllowed</c>؛
    /// <c>admission</c> ‏<c>Admit</c> ← سِجِلٌّ لَه خاصِّيَّةٌ <c>X</c> (لا مَصنَعٌ عَلى الحامِلِ نَفسِه) + <c>IsAllowed</c>.
    /// </summary>
    internal static List<string> Families(Type v, IEnumerable<Type> types)
    {
        var list = typeof(IReadOnlyList<>).MakeGenericType(v);
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var c in types)
        {
            if (c == v) continue;
            var methods = StaticMethods(c).ToList();
            if (methods.Count == 0) continue;

            if (methods.Any(m => m.ReturnType == list && m.Name.StartsWith("Validate", StringComparison.Ordinal))
                && HasBool(c, n => n.StartsWith("IsValid", StringComparison.Ordinal)))
                found.Add(List);

            var allowed = HasBool(c, n => n == "IsAllowed");
            if (allowed && methods.Any(m => m.ReturnType == v)) found.Add(First);

            if (allowed && methods.Any(m => m.Name == "Admit" && m.ReturnType != c && m.ReturnType != v && Carries(m.ReturnType, v)))
                found.Add(Admission);
        }
        return found.ToList();
    }

    /// <summary>أَسماءُ <c>record …Violation</c> في مَصادِرِ libs وapps — بِلا التَعليقات.</summary>
    private static List<string> SourceViolationNames()
    {
        var rx = new Regex(@"\brecord\s+(?<n>[A-Za-z0-9_]+Violation)\b");
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (_, text) in EntitlementContractTests.SourceFiles())
        {
            var code = string.Join('\n', text.Split('\n').Where(l =>
            {
                var s = l.TrimStart();
                return !s.StartsWith("//", StringComparison.Ordinal) && !s.StartsWith('*');
            }));
            foreach (Match m in rx.Matches(code)) names.Add(m.Groups["n"].Value);
        }
        return names.ToList();
    }
}
