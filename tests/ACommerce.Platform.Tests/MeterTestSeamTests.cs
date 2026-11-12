using System.Reflection;
using System.Reflection.Emit;
using ACommerce.Kit.Auth;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G6c — مَقاعِدُ الاختِبارِ في كودِ الإنتاجِ لا يُناديها كودُ الإنتاج (‏2026-11-12، W13-6·١١ — ADR-051 §١٨؛ التَصميم §6.3) ═══════════════════════
//
// **المَقعَدُ اليَوم**: أَخَوا `IOtpChannel` بِلا `ProviderCall` (`SendOtpAsync(string, string, CancellationToken)` و`SendNoticeAsync(string, string, CancellationToken)`).
// بَقِيا في الواجِهَةِ لِأَنّ قَنَواتِ الاختِبارِ المُزَيَّفَةَ تُنَفِّذُهُما — ومِنها مِلَفُّ تَوصيفٍ لا يُعَدَّلُ حَرفُه (`LiveMeterJourneyFootprintCharacterizationProofTests`) —
// فَصارَ «نِسيانُ `ProviderCall` خَطَأُ بِناءٍ» (التَصميم §6.1) **أَحمَرَ هُنا** لا عِندَ المُتَرجِم: مُنادٍ واحِدٌ لَهُما في `libs/` أَو `apps/` يُرسِلُ بِلا مَتجَرٍ ولا
// مُعَرِّفِ طَلَب. والمُنادي يُقرَأُ مِن **الـIL المَبنِيّ** لا مِن النَصّ: `SendOtpAsync` اسمٌ يَحمِلُه `IEmailOtpChannel` أَيضاً بِثَلاثَةِ وُسَطاء، والنَصُّ لا يُمَيِّزُ
// الواجِهَة؛ والـIL يَحمِلُ الطَريقَةَ بِعَينِها (نِداءً أَو تَحويلاً إلى مُفَوَّض). والمُنادي المَأذونُ واحِد: جِسمُ الأَخِ ذي `ProviderCall` الافتِراضِيُّ في الواجِهَةِ نَفسِها.
// والمَقاعِدُ الآتِيَةُ (التَحويلُ مِن `HttpClient` و`ProviderCalls.Unrecorded` وبُناةُ SDK القَديمَة) تَدخُلُ هذه القائِمَةَ مَعَ أَوَّلِ مُستَهلِكٍ لَها (البُنودُ ١٢–١٥).

public class MeterTestSeamTests(ITestOutputHelper output)
{
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);

    private static int OperandSize(OpCode op, byte[] il, int at) => op.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, at),
        _ => 4,
    };

    /// <summary>كُلُّ طَريقَةٍ يُشيرُ إلَيها جِسمُ طَريقَةٍ في التَجميع — نِداءً (<c>call</c>/<c>callvirt</c>) أَو تَحويلاً إلى مُفَوَّض (<c>ldftn</c>/<c>ldvirtftn</c>).</summary>
    internal static IEnumerable<(MethodBase Caller, MethodBase Callee)> References(Assembly asm, Action<int>? bodies = null)
    {
        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var type in types)
        {
            MethodBase[] methods;
            try { methods = [.. type.GetMethods(all), .. type.GetConstructors(all)]; } catch { continue; }
            foreach (var method in methods)
            {
                byte[]? il;
                try { il = method.GetMethodBody()?.GetILAsByteArray(); } catch { continue; }
                if (il is null) continue;
                bodies?.Invoke(1);
                for (var i = 0; i < il.Length;)
                {
                    OpCode op;
                    if (il[i] == 0xFE) { op = Codes[unchecked((short)(0xFE00 | il[i + 1]))]; i += 2; }
                    else { op = Codes[il[i]]; i += 1; }
                    if (op.OperandType == OperandType.InlineMethod)
                    {
                        MethodBase? callee = null;
                        try
                        {
                            callee = method.Module.ResolveMethod(BitConverter.ToInt32(il, i),
                                type.IsGenericType ? type.GetGenericArguments() : null, method.IsGenericMethod ? method.GetGenericArguments() : null);
                        }
                        catch { /* مَرجِعٌ إلى تَجميعٍ غائِبٍ عَن مُشَغِّلِ الاختِبار — لا يَكونُ مَقعَداً */ }
                        if (callee is not null) yield return (method, callee);
                    }
                    i += OperandSize(op, il, i);
                }
            }
        }
    }

    /// <summary>تَجميعاتُ <c>libs/</c> و<c>apps/V1.App</c> كَما يَشحَنُها التَطبيق: مِجَلَّدُ بِنائِه (كَما يَقرَأُ G2 المُجَمِّع) بِكُلِّ <c>ACommerce.*</c> فيه — ومِنها ما لا
    /// يُرَجِّعُه الاختِبار (المُحاكِيات) — ومَعَها ما بِجِوارِ الاختِبار. والتَجميعُ المُحَمَّلُ بِاسمِه يُؤخَذُ نَفسُه، فَلا نَوعَ يُحَمَّلُ مَرَّتَين. وغِيابُ البِناءِ أَداةٌ عَمياء.</summary>
    internal static IReadOnlyList<Assembly> ProductionAssemblies()
    {
        var tfm = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var bin = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "apps", "V1.App", "bin", tfm.Parent!.Name, tfm.Name);
        var app = Path.Combine(bin, "V1.App.dll");
        Assert.True(File.Exists(app), $"أَداةٌ عَمياء: {app} غائِب — ابنِ الحَلَّ أَوَّلاً (dotnet build PlatformV1.slnx -c {tfm.Parent!.Name}).");
        var byName = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        foreach (var dll in Directory.EnumerateFiles(AppContext.BaseDirectory, "ACommerce.*.dll").Concat(Directory.EnumerateFiles(bin, "ACommerce.*.dll")))
        {
            var name = AssemblyName.GetAssemblyName(dll);
            if (name.Name!.StartsWith("ACommerce.Platform.Tests", StringComparison.Ordinal) || byName.ContainsKey(name.Name)) continue;
            Assembly asm;
            try { asm = Assembly.Load(name); }
            catch (FileNotFoundException) { asm = Assembly.LoadFrom(dll); }
            byName[name.Name] = asm;
        }
        return [.. byName.Values.OrderBy(a => a.GetName().Name, StringComparer.Ordinal), Assembly.LoadFrom(app)];
    }

    /// <summary>المَقاعِدُ وتَنفيذاتُها في كُلِّ نَوعٍ مَبنِيٍّ يُنَفِّذُ الواجِهَة — نِداءُ التَنفيذِ بِعَينِه مَقعَدٌ أَيضاً.</summary>
    internal static IReadOnlyList<MethodBase> Seams(IEnumerable<Assembly> assemblies)
    {
        var declared = new[]
        {
            typeof(IOtpChannel).GetMethod(nameof(IOtpChannel.SendOtpAsync), [typeof(string), typeof(string), typeof(CancellationToken)])!,
            typeof(IOtpChannel).GetMethod(nameof(IOtpChannel.SendNoticeAsync), [typeof(string), typeof(string), typeof(CancellationToken)])!,
        };
        var seams = new List<MethodBase>(declared);
        foreach (var asm in assemblies)
        {
            Type[] types;
            try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }
            foreach (var t in types.Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IOtpChannel).IsAssignableFrom(t)))
            {
                var map = t.GetInterfaceMap(typeof(IOtpChannel));
                for (var i = 0; i < map.InterfaceMethods.Length; i++)
                    if (declared.Contains(map.InterfaceMethods[i]) && map.TargetMethods[i].DeclaringType == t) seams.Add(map.TargetMethods[i]);
            }
        }
        return seams;
    }

    [Fact]
    public void No_production_code_calls_the_ProviderCall_less_channel_seam()
    {
        var assemblies = ProductionAssemblies();
        var seams = Seams(assemblies);
        var seamSet = seams.ToHashSet();
        var bodies = 0;
        var refs = assemblies.SelectMany(a => References(a, n => bodies += n)).Where(r => seamSet.Contains(r.Callee)).ToList();
        static string Name(MethodBase m) => $"{m.DeclaringType?.FullName}.{m.Name}";
        // المَأذون: الجِسمُ الافتِراضِيُّ لِلأَخِ ذي ProviderCall في الواجِهَةِ نَفسِها يُمَرِّرُ إلى المَقعَد (القَنَواتُ المُزَيَّفَةُ ومُحاكي التَطوير).
        var sanctioned = refs.Where(r => r.Caller.DeclaringType == typeof(IOtpChannel)).ToList();
        var callers = refs.Except(sanctioned).ToList();
        output.WriteLine($"[G6c] تَجميعات {assemblies.Count} · أَجسام {bodies} · مَقاعِد {seams.Count} ({string.Join(", ", seams.Select(Name))})");
        output.WriteLine($"[G6c] مَأذون {sanctioned.Count} ({string.Join(", ", sanctioned.Select(r => $"{Name(r.Caller)}→{r.Callee.Name}"))}) · مُنادونَ في libs/apps {callers.Count}");
        foreach (var c in callers) output.WriteLine($"  ✗ {Name(c.Caller)} → {Name(c.Callee)}");
        Assert.True(assemblies.Count >= 40 && bodies > 10_000, $"أَداةٌ عَمياء: {assemblies.Count} تَجميعاً و{bodies} جِسماً.");
        Assert.True(seams.Count >= 4, $"أَداةٌ عَمياء: {seams.Count} مَقعَداً (الأَخَوانِ وتَنفيذاتُهُما في Twilio والمُحاكي).");
        Assert.True(sanctioned.Count == 2, $"أَداةٌ عَمياء: المُنادي المَأذونُ {sanctioned.Count} لا 2 — قارِئُ الـIL لا يَرى النِداء.");
        Assert.True(callers.Count == 0, "مُنادٍ في كودِ الإنتاجِ لِمَقعَدِ الاختِبار — يُرسِلُ بِلا ProviderCall: "
                                        + string.Join(", ", callers.Select(c => $"{Name(c.Caller)}→{c.Callee.Name}")));
    }
}
