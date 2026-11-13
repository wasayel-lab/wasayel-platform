using System.Reflection;
using System.Reflection.Emit;
using ACommerce.Kit.Auth;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services;
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
//
// ‏(‏2026-11-12، W13-6·١٢ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **دَخَلَها مَقعَدا المُستَدعي**: `ProviderCalls.Unrecorded` (مُستَدعٍ لا يُسَجِّل) والتَحويلُ الضِمنِيُّ
// `HttpClient → ProviderHttp<TKit>` (`op_Implicit`، أَوَّلُ مُستَهلِكٍ لَه بِناءُ Brevo في اختِباراتِها). والتَحويلُ عامٌّ عَلى نَوعٍ عامّ، فَالمُنادي يُشيرُ إلى
// `ProviderHttp<BrevoEmailChannel>.op_Implicit` لا إلى التَعريف — فَالمُطابَقَةُ بِالوَحدَةِ ومُعَرِّفِ البَيانِ الوَصفِيّ (`MetadataToken`) لا بِالكائِن. والمُنادي المَأذونُ
// الثالِث: جِسمُ التَحويلِ نَفسُه يَقرَأُ `Unrecorded`. أَمّا `IEmailOtpChannel` فَلا مَقعَدَ لَه: تَوقيعاه يَحمِلانِ `ProviderCall` ولا أَخَ بِدونِه — النِسيانُ خَطَأُ بِناء.
//
// ‏(‏2026-11-12، W13-6·١٣ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **ودَخَلَها بانِيا عُدَّتَيِ الـSDK القَديمان**: `S3FileStorage(IOptions, ILogger)` و`WebPushService(IConfiguration,
// ILogger)` — يَبنِيانِ بِـ`ProviderCalls.Unrecorded` فَتَبقى بُناتُهُما في الاختِباراتِ وسكربتاتِ المُشَغِّلِ كَما كانَت، والتَسجيلانِ في الإنتاجِ يَحُلّانِ الباني
// الأَطوَلَ بِمُستَدعي المُضيف. والمُنادي المَأذونُ الرابِعُ والخامِس: جِسما البانِيَينِ القَديمَينِ نَفسُهُما يَقرَآنِ `Unrecorded`. و`INafathChannel` و`IFileStorage`
// بِلا مَقعَد: `StartAsync` و`UploadAsync` يَحمِلانِ `ProviderCall` في التَوقيعِ نَفسِه.
//
// ‏(‏2026-11-12، W13-6·١٤ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **ودَخَلَها عُضوا واجِهَةِ PayPal بِلا مُنادٍ**: `CancelSubscriptionAsync` و`GetInvoiceAsync` — عُضوا
// `IPaymentProvider` وPayPal لا يُسَجَّلُ عَلَيها، فَلا يَجرِيانِ في الإنتاج (دَينُ القاعِدَة ١)؛ يَمُرّانِ بِالمُستَدعي مُساعِدَينِ ولا يُعَدّان، وأَوَّلُ مُنادٍ لَهُما
// هُنا أَحمَر. ولا مَأذونَ لَهُما. أَمّا ميسّر وPaddle وعَمَلِيّاتُ PayPal الخَمسُ فَبِلا مَقعَد: `ProviderCall` في التَوقيع.
//
// ‏(‏2026-11-12، W13-6·١٥ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) **ودَخَلَها أَخُ `IAgentBackend` بِلا `ProviderCall`** — `CallAsync(AgentRequest, CancellationToken)` وتَنفيذاتُه
// في الخَلفِيّاتِ الثَلاث: بَقِيَ لِأَنّ خَلفِيّاتِ الاختِبارِ المُزَيَّفَةَ تُنَفِّذُه، ومِنها مِلَفُّ تَوصيفٍ مِن مَوجَةٍ سابِقَة (`SessionCircuitTests.ProbeAgentBackends`) — نَسَقُ
// أَخَوَي `IOtpChannel` بِحَرفِه، والمَأذونُ السادِسُ جِسمُ الأَخِ ذي `ProviderCall` الافتِراضِيُّ في الواجِهَةِ نَفسِها. **وقارِئُ خِياراتِ OTO بِلا مُنادٍ**
// (`OtoClient.CheckOtoDeliveryFeeAsync`) مَقعَدٌ بِلا مَأذونٍ كَعُضوَي PayPal. أَمّا عَمَلِيّاتُ OTO الأَربَعُ فَبِلا مَقعَد: `ProviderCall` في التَوقيع.

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
        var seams = new List<MethodBase>(declared)
        {
            // ‏(‏2026-11-12، W13-6·١٢) مَقعَدا المُستَدعي: المُستَدعي الَّذي لا يُسَجِّل، والتَحويلُ مِن عَميلٍ خامٍّ إلَيه.
            typeof(ProviderCalls).GetProperty(nameof(ProviderCalls.Unrecorded))!.GetMethod!,
            typeof(ProviderHttp<>).GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m => m.Name == "op_Implicit"),
            // ‏(‏2026-11-12، W13-6·١٣) بانِيا عُدَّتَيِ الـSDK القَديمانِ — بِلا مُستَدعٍ يُسَجِّل.
            OldSdkConstructors[0], OldSdkConstructors[1],
        };
        // ‏(‏2026-11-12، W13-6·١٤) وعُضوا واجِهَةِ PayPal بِلا مُنادٍ — مُساعِدانِ بِسَبَبِ دَينِهِما (`uncalled_member`) لا وَحدَتان.
        seams.AddRange(UncalledMembers);
        // ‏(‏2026-11-12، W13-6·١٥) وأَخُ الخَلفِيَّةِ بِلا ProviderCall — المُعلَنُ وتَنفيذاتُه في كُلِّ نَوعٍ مَبنِيّ.
        seams.Add(AgentSeam);
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
            foreach (var t in types.Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IAgentBackend).IsAssignableFrom(t)))
            {
                var map = t.GetInterfaceMap(typeof(IAgentBackend));
                for (var i = 0; i < map.InterfaceMethods.Length; i++)
                    if (map.InterfaceMethods[i] == AgentSeam && map.TargetMethods[i].DeclaringType == t) seams.Add(map.TargetMethods[i]);
            }
        }
        return seams;
    }

    /// <summary>‏(‏2026-11-12، W13-6·١٤) <b>عُضوا <c>IPaymentProvider</c> في PayPal بِلا مُنادٍ في وَقتِ التَشغيل</b> (التَصميم §3.4: «دَينُ القاعِدَةِ ١ لا وَحدَة») — يَمُرّانِ
    /// بِالمُستَدعي نِداءً مُساعِداً (<see cref="ProviderHelperReasons.UncalledMember"/>) فَلا يُعَدّان. مُنادٍ لَهُما في <c>libs/</c> أَو <c>apps/</c> يُحَمِّرُ هذا الصَفّ، فَيولَدُ
    /// مَعَه صَفُّ وَحدَةٍ ونِداءٌ مَعدود.</summary>
    internal static readonly MethodInfo[] UncalledMembers =
    [
        typeof(ACommerce.Kit.Payments.Providers.PayPal.PayPalPaymentProvider).GetMethod(nameof(ACommerce.Kit.Payments.Providers.PayPal.PayPalPaymentProvider.CancelSubscriptionAsync))!,
        typeof(ACommerce.Kit.Payments.Providers.PayPal.PayPalPaymentProvider).GetMethod(nameof(ACommerce.Kit.Payments.Providers.PayPal.PayPalPaymentProvider.GetInvoiceAsync))!,
        // ‏(‏2026-11-12، W13-6·١٥) وقارِئُ خِياراتِ OTO بِصِفرِ مُنادٍ في وَقتِ التَشغيل (التَصميم §3.4).
        typeof(ACommerce.Kit.Delivery.OtoClient).GetMethod(nameof(ACommerce.Kit.Delivery.OtoClient.CheckOtoDeliveryFeeAsync))!,
    ];

    /// <summary>‏(‏2026-11-12، W13-6·١٥) <b>أَخُ <c>IAgentBackend</c> بِلا <c>ProviderCall</c></b> — مَقعَدُ خَلفِيّاتِ الاختِبارِ المُزَيَّفَة؛ ومُنادٍ لَه في <c>libs/</c> أَو <c>apps/</c>
    /// يُنادي نَموذَجاً بِلا مَتجَرٍ ولا مُعَرِّف.</summary>
    internal static readonly MethodInfo AgentSeam =
        typeof(IAgentBackend).GetMethod(nameof(IAgentBackend.CallAsync), [typeof(AgentRequest), typeof(CancellationToken)])!;

    /// <summary>‏(‏2026-11-12، W13-6·١٣) البانِيانِ القَديمانِ لِعُدَّتَيِ الـSDK — يَبنِيانِ بِـ<see cref="ProviderCalls.Unrecorded"/>.</summary>
    internal static readonly ConstructorInfo[] OldSdkConstructors =
    [
        typeof(ACommerce.Kit.Files.Providers.S3.S3FileStorage).GetConstructor(
            [typeof(Microsoft.Extensions.Options.IOptions<ACommerce.Kit.Files.Providers.S3.S3FileStorageOptions>),
             typeof(Microsoft.Extensions.Logging.ILogger<ACommerce.Kit.Files.Providers.S3.S3FileStorage>)])!,
        typeof(ACommerce.Templates.Customer.Marketplace.Services.WebPushService).GetConstructor(
            [typeof(Microsoft.Extensions.Configuration.IConfiguration),
             typeof(Microsoft.Extensions.Logging.ILogger<ACommerce.Templates.Customer.Marketplace.Services.WebPushService>)])!,
    ];

    /// <summary>‏(‏2026-11-12، W13-6·١٢) مِفتاحُ الطَريقَة: وَحدَتُها ومُعَرِّفُ بَيانِها الوَصفِيّ — يُطابِقُ طَريقَةَ النَوعِ العامِّ ونَظيرَتَها عَلى نَوعٍ مُغلَق.</summary>
    internal static (Module Module, int Token) Key(MethodBase m) => (m.Module, m.MetadataToken);

    private static bool IsConversion(MethodBase m)
        => m.Name == "op_Implicit" && m.DeclaringType is { IsGenericType: true } t && t.GetGenericTypeDefinition() == typeof(ProviderHttp<>);

    [Fact]
    public void No_production_code_calls_the_ProviderCall_less_channel_seam()
    {
        var assemblies = ProductionAssemblies();
        var seams = Seams(assemblies);
        var seamSet = seams.Select(Key).ToHashSet();
        var bodies = 0;
        var refs = assemblies.SelectMany(a => References(a, n => bodies += n)).Where(r => seamSet.Contains(Key(r.Callee))).ToList();
        static string Name(MethodBase m) => $"{m.DeclaringType?.FullName}.{m.Name}";
        // المَأذون: الجِسمُ الافتِراضِيُّ لِلأَخِ ذي ProviderCall في الواجِهَةِ نَفسِها يُمَرِّرُ إلى المَقعَد (القَنَواتُ المُزَيَّفَةُ ومُحاكي التَطوير)؛
        // و(W13-6·١٢) جِسمُ التَحويلِ مِن HttpClient يَقرَأُ ProviderCalls.Unrecorded؛ و(W13-6·١٣) جِسما البانِيَينِ القَديمَينِ يَقرَآنِه.
        var oldConstructors = OldSdkConstructors.Select(Key).ToHashSet();
        // و(W13-6·١٥) الجِسمُ الافتِراضِيُّ لِأَخِ IAgentBackend ذي ProviderCall يُمَرِّرُ إلى مَقعَدِه كَأَخَوَي IOtpChannel.
        var sanctioned = refs.Where(r => r.Caller.DeclaringType == typeof(IOtpChannel) || r.Caller.DeclaringType == typeof(IAgentBackend)
                                         || IsConversion(r.Caller) || oldConstructors.Contains(Key(r.Caller))).ToList();
        var callers = refs.Except(sanctioned).ToList();
        output.WriteLine($"[G6c] تَجميعات {assemblies.Count} · أَجسام {bodies} · مَقاعِد {seams.Count} ({string.Join(", ", seams.Select(Name))})");
        output.WriteLine($"[G6c] مَأذون {sanctioned.Count} ({string.Join(", ", sanctioned.Select(r => $"{Name(r.Caller)}→{r.Callee.Name}"))}) · مُنادونَ في libs/apps {callers.Count}");
        foreach (var c in callers) output.WriteLine($"  ✗ {Name(c.Caller)} → {Name(c.Callee)}");
        Assert.True(assemblies.Count >= 40 && bodies > 10_000, $"أَداةٌ عَمياء: {assemblies.Count} تَجميعاً و{bodies} جِسماً.");
        Assert.True(seams.Count >= 8, $"أَداةٌ عَمياء: {seams.Count} مَقعَداً (الأَخَوانِ وتَنفيذاتُهُما في Twilio والمُحاكي، ومَقعَدا المُستَدعي، والبانِيانِ القَديمان).");
        // ‏(W13-6·١٥) سِتّة: والجِسمُ الافتِراضِيُّ لِأَخِ IAgentBackend.
        Assert.True(sanctioned.Count == 6, $"أَداةٌ عَمياء: المُنادي المَأذونُ {sanctioned.Count} لا 6 — قارِئُ الـIL لا يَرى النِداء.");
        Assert.Contains(sanctioned, r => r.Caller.DeclaringType == typeof(IAgentBackend) && Key(r.Callee) == Key(AgentSeam));
        Assert.Equal(3, seams.Count(m => m.DeclaringType is { } d && d != typeof(IAgentBackend) && typeof(IAgentBackend).IsAssignableFrom(d)));
        Assert.Contains(sanctioned, r => IsConversion(r.Caller) && r.Callee.Name == "get_Unrecorded");
        Assert.All(OldSdkConstructors, c => Assert.Contains(sanctioned, r => Key(r.Caller) == Key(c) && r.Callee.Name == "get_Unrecorded"));
        Assert.True(callers.Count == 0, "مُنادٍ في كودِ الإنتاجِ لِمَقعَدِ الاختِبار — يُرسِلُ بِلا ProviderCall: "
                                        + string.Join(", ", callers.Select(c => $"{Name(c.Caller)}→{c.Callee.Name}")));
    }
}
