using System.Reflection;
using System.Runtime.CompilerServices;
using ACommerce.Platform.Shared.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G16 (كُتّابُ الدَور) — مَن يَملَأُ دَورَ الحامِلِ بِأَيِّ مالِئ، مُثَبَّتاً بِنَوعِه وعَدَدِ مَواضِعِه (‏2026-11-13، مُراجَعَةُ المَرحَلَةِ ج الأولى · M6C-RULES-R1-4 — ADR-051 §١٩) ═══
//
// **العِلَّةُ المَقيسَة** (القاعِدَة ٢): المَرحَلَةُ ب كَتَبَت الحَدّ — «الدَورُ الَّذي قَبِلَ بِه حارِسُ صَلاحِيَّة (`FillAcceptedRole` — كاتِباه `PermissionFilter`
// و`HasPermissionAsync` وَحدَهُما)» في ADR-051 وفي `MeterScope.cs` — ثُمَّ انتَقَلَ الحَدُّ مَرَّتَينِ في مَرحَلَةٍ واحِدَة: بابُ إشعارِ البَوّابَةِ (db6786f) وبابُ وارِدِ OTO
// (cc79500) صارا يَكتُبانِ `_provider` بِه، والثاني لا تَذكُرُه وَثيقَةٌ ولا تَعليق — ولا شَيءَ احمَرّ: لا صَفَّ يُثَبِّتُ مَن يُنادي المالِئ. فَمالِئُ المُزَوِّدِ صارَ
// `FillProviderRole` بِاسمِه، وهذا الحارِسُ يُثَبِّتُ كُتّابَ المالِئاتِ الأَربَعَة.
//
// **والقِراءَةُ مِن الـIL المَبنِيّ** بِقارِئِ G6c نَفسِه (`MeterTestSeamTests.References` عَلى `ProductionAssemblies`): كُلُّ نِداءٍ أَو تَحويلٍ إلى مُفَوَّضٍ لِمالِئٍ
// مِن `MeterScopeHolder`، والكاتِبُ النَوعُ الخارِجِيُّ لِلجِسم (اللامبدا وآلَةُ الحالَةِ المُتَزامِنَةُ يُنسَبانِ إلى صِنفِهِما) — فَالاسمُ المُستَعارُ والمُفَوَّضُ لا يَخفَيان.
// ويَطبَعُ ما عَدّ، ويَحمَرُّ عَلى صِفرِ أَجسامٍ أَو صِفرِ كُتّاب، وعَلى كاتِبٍ جَديدٍ أَو غائِبٍ أَو عَدَدٍ تَغَيَّر.
//
// ‏(‏2026-11-13، مُراجَعَةُ المَرحَلَةِ ج الثانِيَة · M6C-RULES-R2-3 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **و«المالِئاتُ الأَربَعَة» كانَت خَمسَة**: `Fill(app, entrepreneur, role)`
// يَكتُبُ الدَورَ أَيضاً (الكاتِبُ الأَوَّلُ يَفوز) ولَم يَكُن في الجَدوَل — فَكانَ «كُتّابُ كُلِّ مالِئِ دَورٍ مُثَبَّتون» غَيرَ صادِق. صارَ فيه بِكُتّابِه الأَربَعَة.

public class MeterRoleWritersTests(ITestOutputHelper output)
{
    private const string Tpl = "ACommerce.Templates.Customer.Marketplace";

    /// <summary>الكُتّابُ المُثَبَّتون: لِكُلِّ مالِئٍ أَنواعُه وعَدَدُ مَواضِعِ كُلٍّ في الـIL. يَتَغَيَّرُ سَطرٌ هُنا بِقَرارٍ مَرئِيٍّ في مُراجَعَة.</summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Pinned = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal)
    {
        // الدَورُ الَّذي قَبِلَ بِه حارِسُ صَلاحِيَّة — الحارِسانِ وَحدَهُما (المَرحَلَةُ ب).
        [nameof(MeterScopeHolder.FillAcceptedRole)] = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [$"{Tpl}.Gates.PermissionFilter"] = 2,
            [$"{Tpl}.MarketplaceTemplateExtensions"] = 2,
        },
        // دَورُ المُزَوِّدِ عَلى بابِ إشعارِ المَتجَر — بَوّابَتُه وناقِلُه.
        [nameof(MeterScopeHolder.FillProviderRole)] = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [$"{Tpl}.Billing.TenantGatewayEndpoints"] = 1,
            [$"{Tpl}.Shipping.OtoShippingEndpoints"] = 1,
        },
        // دَورُ فاعِلٍ بِلا مَتجَر — مُشرِفُ المَنَصَّة، وبابا إشعارِ المَنَصَّة.
        [nameof(MeterScopeHolder.FillPlatformRole)] = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [$"{Tpl}.Services.PlatformAdminGuard"] = 1,
            [$"{Tpl}.Billing.PayPalEndpoints"] = 1,
            [$"{Tpl}.Billing.PaddleEndpoints"] = 1,
        },
        // الدَورُ المُؤَقَّتُ مِن الوَثيقَةِ المُخَزَّنَة — الوَسيطُ وَحدَه.
        [nameof(MeterScopeHolder.FillProvisionalRole)] = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["ACommerce.Platform.MultiTenancy.TenantResolverMiddleware"] = 1,
        },
        // ‏(‏2026-11-13، مُراجَعَةُ المَرحَلَةِ ج الثانِيَة · M6C-RULES-R2-3 — ADR-051 §١٩) والمالِئُ العامُّ بِوَسيطِ دَورِه (الكاتِبُ الأَوَّلُ يَفوز): كانَ خارِجَ الجَدوَل، فَبابٌ
        // جَديدٌ يَكتُبُ `_provider` أَو أَيَّ دَورٍ بِـ`Fill(slug, role: …)` لا يُحَمِّرُ شَيئاً (مَقيسٌ عِندَ المُراجِع: «مَواضِع 10 · خُروق 0» بِكاتِبَينِ مَحقونَين).
        // كُتّابُه الأَربَعَة: الوَسيطُ (المَتجَرُ ورائِدُ أَعمالِه بِلا دَور)، وحارِسُ إدارَةِ المَتجَر (دَورُ صاحِبِه)، وحارِسُ مِلكِيَّةِ الاستوديو (`_entrepreneur`)،
        // ومُعالِجُ المُهلَة (`_system`).
        [nameof(MeterScopeHolder.Fill)] = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["ACommerce.Platform.MultiTenancy.TenantResolverMiddleware"] = 1,
            [$"{Tpl}.Services.TenantAdminGuard"] = 1,
            [$"{Tpl}.MarketplaceTemplateExtensions"] = 1,
            [$"{Tpl}.Services.Deals.GatewayInvoiceExpiryHandler"] = 1,
        },
    };

    /// <summary>النَوعُ الَّذي كُتِبَ فيه الجِسم: اللامبدا وآلَةُ الحالَةِ ومُغلَقُ الالتِقاطِ أَنواعٌ مُتَداخِلَةٌ يولِدُها المُتَرجِم.</summary>
    internal static Type Outer(Type t)
    {
        while (t.DeclaringType is { } outer && (t.Name.StartsWith('<') || t.IsDefined(typeof(CompilerGeneratedAttribute), false))) t = outer;
        return t;
    }

    /// <summary>كُتّابُ كُلِّ مالِئٍ في المَراجِع: (المالِئ، الكاتِب) لِكُلِّ مَوضِع.</summary>
    internal static IReadOnlyList<(string Filler, string Writer)> Writers(IEnumerable<(MethodBase Caller, MethodBase Callee)> refs)
        => refs.Where(r => r.Callee.DeclaringType == typeof(MeterScopeHolder) && Pinned.ContainsKey(r.Callee.Name))
               .Select(r => (r.Callee.Name, Outer(r.Caller.DeclaringType!).FullName!)).ToList();

    /// <summary>الخُروق: كاتِبٌ لا يُثَبِّتُه الجَدوَل، وكاتِبٌ مُثَبَّتٌ غائِب، وعَدَدُ مَواضِعَ تَغَيَّر.</summary>
    internal static IReadOnlyList<string> Violations(IReadOnlyList<(string Filler, string Writer)> writers)
    {
        var measured = writers.GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
        var v = new List<string>();
        foreach (var ((filler, writer), count) in measured.OrderBy(m => m.Key.Filler, StringComparer.Ordinal).ThenBy(m => m.Key.Writer, StringComparer.Ordinal))
        {
            if (!Pinned[filler].TryGetValue(writer, out var pinned)) v.Add($"new_writer:{filler}←{writer}×{count}");
            else if (pinned != count) v.Add($"count_changed:{filler}←{writer} {pinned}→{count}");
        }
        foreach (var (filler, pinned) in Pinned)
            foreach (var writer in pinned.Keys.Where(w => !measured.ContainsKey((filler, w))))
                v.Add($"missing_writer:{filler}←{writer}");
        return v;
    }

    private static IReadOnlyList<(string Filler, string Writer)> Measure(out int assemblies, out int bodies)
    {
        var asms = MeterTestSeamTests.ProductionAssemblies();
        var n = 0;
        var refs = asms.SelectMany(a => MeterTestSeamTests.References(a, k => n += k)).ToList();
        (assemblies, bodies) = (asms.Count, n);
        return Writers(refs);
    }

    [Fact]
    public void Each_role_filler_is_called_only_by_its_pinned_writers()
    {
        var writers = Measure(out var assemblies, out var bodies);
        foreach (var filler in Pinned.Keys)
            output.WriteLine($"[G16·كُتّاب] {filler} {writers.Count(w => w.Filler == filler)}: "
                             + string.Join(", ", writers.Where(w => w.Filler == filler).GroupBy(w => w.Writer).Select(g => $"{g.Key.Split('.')[^1]}×{g.Count()}")));
        var violations = Violations(writers);
        output.WriteLine($"[G16·كُتّاب] تَجميعات {assemblies} · أَجسام {bodies} · مَواضِع {writers.Count} · خُروق {violations.Count}");
        Assert.True(assemblies >= 40 && bodies > 10_000 && writers.Count > 0, $"أَداةٌ عَمياء: {assemblies} تَجميعاً و{bodies} جِسماً و{writers.Count} مَوضِعاً.");
        Assert.True(violations.Count == 0, "كُتّابُ الدَورِ تَغَيَّروا بِلا قَرارٍ مَرئِيّ: " + string.Join(" | ", violations));
    }

    /// <summary><b>الحَقنُ المَقيس</b> (القاعِدَة ١٠): بابٌ ثالِثٌ يَكتُبُ بِـ<c>FillAcceptedRole</c> (ما فَعَلَه بابُ وارِدِ OTO) ⇐ كاتِبٌ جَديد؛ وحارِسٌ يُنزَعُ سَطرُ مَلئِه ⇐ كاتِبٌ غائِب؛
    /// ومَوضِعٌ ثانٍ في كاتِبٍ مُثَبَّت ⇐ عَدَدٌ تَغَيَّر.</summary>
    [Fact]
    public void A_new_a_missing_or_a_doubled_writer_turns_the_guard_red()
    {
        var writers = Measure(out _, out _).ToList();
        Assert.Empty(Violations(writers));
        var injected = writers.Append((nameof(MeterScopeHolder.FillAcceptedRole), $"{Tpl}.Shipping.OtoShippingEndpoints")).ToList();
        Assert.Equal([$"new_writer:FillAcceptedRole←{Tpl}.Shipping.OtoShippingEndpoints×1"], Violations(injected));
        var missing = writers.Where(w => w.Writer != $"{Tpl}.Services.PlatformAdminGuard").ToList();
        Assert.Equal([$"missing_writer:FillPlatformRole←{Tpl}.Services.PlatformAdminGuard"], Violations(missing));
        var doubled = writers.Append((nameof(MeterScopeHolder.FillProviderRole), $"{Tpl}.Billing.TenantGatewayEndpoints")).ToList();
        Assert.Equal([$"count_changed:FillProviderRole←{Tpl}.Billing.TenantGatewayEndpoints 1→2"], Violations(doubled));
        output.WriteLine($"[G16·كُتّاب] الحَقن: {Violations(injected)[0]} · {Violations(missing)[0]} · {Violations(doubled)[0]}");
        // ‏(‏2026-11-13، M6C-RULES-R2-3) وحَقنُ المُراجِعِ بِحَرفِه: بابُ رَبطِ جِهاتِ الاتِّصالِ يَكتُبُ الدَورَ بِالمالِئِ العامّ (`Fill(slug, role: MeterSentinels.Provider)`
        // و`Fill(slug, role: role)`) ⇐ كاتِبٌ جَديدٌ بِمَوضِعَيه.
        var viaFill = writers.Append((nameof(MeterScopeHolder.Fill), $"{Tpl}.Services.AccountContacts"))
                             .Append((nameof(MeterScopeHolder.Fill), $"{Tpl}.Services.AccountContacts")).ToList();
        Assert.Equal([$"new_writer:Fill←{Tpl}.Services.AccountContacts×2"], Violations(viaFill));
        output.WriteLine($"[G16·كُتّاب] حَقنُ المالِئِ العامّ: {Violations(viaFill)[0]}");
    }

    /// <summary>والقارِئُ يَنسِبُ الجِسمَ إلى صِنفِه: لامبدا البابِ وآلَةُ حالَةِ دالَّةٍ مُتَزامِنَةٍ تَعودانِ إلى النَوعِ الَّذي كُتِبَتا فيه.</summary>
    [Fact]
    public void A_lambda_or_an_async_body_is_attributed_to_its_declaring_type()
    {
        Func<Task> lambda = async () => await Task.Yield();
        Assert.Equal(typeof(MeterRoleWritersTests), Outer(lambda.Method.DeclaringType!));
        var machine = typeof(MeterRoleWritersTests).GetMethod(nameof(Each_role_filler_is_called_only_by_its_pinned_writers))!;
        Assert.Equal(typeof(MeterRoleWritersTests), Outer(machine.DeclaringType!));
        var stateMachine = typeof(MeterTestSeamTests).Assembly.GetTypes().First(t => t.DeclaringType == typeof(MeterRoleWritersTests) && t.Name.StartsWith('<'));
        Assert.Equal(typeof(MeterRoleWritersTests), Outer(stateMachine));
    }
}
