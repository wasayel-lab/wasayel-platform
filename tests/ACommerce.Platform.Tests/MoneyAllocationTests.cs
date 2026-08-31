using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using ACommerce.Kit.Culture;
using ACommerce.Kit.Payments.Providers.Paddle;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ التَوزيع — الثابِتُ يُفحَصُ بِاستِقصاءٍ لا بِأَمثِلَة ═══════════
//
// **لِماذا استِقصاءٌ لا أَمثِلَة**: «مَجموعُ الأَنصِبَةِ يُساوي
// المَبلَغ» جُملَةٌ **كُلِّيَّة** — والمِثالُ المُفرَدُ يُثبِتُ حالَةً
// ولا يَنفي حالَة. والحالاتُ الَّتي تَكسِرُ التَوزيعَ هي بِالضَبطِ
// الَّتي لا يُفَكَّرُ فيها: أَوزانٌ فيها صِفر، ومَبلَغٌ أَصغَرُ مِن
// عَدَدِ الأَطراف، ومَبلَغٌ سالِبٌ (استِرداد)، ومِقدارٌ يَتَجاوَزُ
// دِقَّةَ العائِم. فَالمَكتوبُ هُنا يَكنُسُ الفَضاءَ كَنساً ويَطبَعُ
// **عَدَدَ ما فَحَص** — والأَداةُ الَّتي لا تَعُدُّ ما فَحَصَته لا
// تُميَّزُ مِن أَداةٍ عَمياء (‏القاعِدَة ١٠).
//
// **وكُلُّه دَوالُّ نَقِيَّة**: بِلا قاعِدَةِ بَيانات، وبِلا شَبَكَة،
// وبِلا وَقت، وبِلا عَشوائِيَّة — فَالمِلَفُّ كُلُّه يَعمَلُ في ثَوانٍ
// ويُعادُ إنتاجُه حَرفاً في أَيِّ آلَة.

public class MoneyAllocationTests(ITestOutputHelper output)
{
    private static readonly MoneyRemainder[] Policies =
        [MoneyRemainder.ToFirst, MoneyRemainder.ToLast, MoneyRemainder.LargestRemainder];

    // كُلُّ مُتَّجِهاتِ الأَوزانِ بِطولٍ ‏1..5 مِن المُعجَمِ {0,1,2,3}
    // = ‏4+16+64+256+1024 = ‏1364 مُتَّجِهاً. وفيها **مُتَّجِهاتُ
    // الصِفرِ الخالِص** (خَمسَة) وهي الحالَةُ الحَدِّيَّةُ الَّتي
    // تُرفَض، و**مُتَّجِهاتٌ فيها أَصفارٌ جُزئِيَّة** وهي الحالَةُ
    // الَّتي تُنسى.
    private static readonly long[][] WeightVectors = BuildWeightVectors(5, [0, 1, 2, 3]);

    private static long[][] BuildWeightVectors(int maxLength, long[] alphabet)
    {
        var all = new List<long[]>();
        for (var length = 1; length <= maxLength; length++)
        {
            var indices = new int[length];
            while (true)
            {
                var vector = new long[length];
                for (var i = 0; i < length; i++) vector[i] = alphabet[indices[i]];
                all.Add(vector);

                var position = length - 1;
                while (position >= 0 && ++indices[position] == alphabet.Length)
                    indices[position--] = 0;
                if (position < 0) break;
            }
        }
        return [.. all];
    }

    private static Int128 SumOf(long[] shares)
    {
        Int128 sum = 0;
        foreach (var share in shares) sum += share;
        return sum;
    }

    // ═══ ١. الثابِتُ الأَعظَم — بِاستِقصاءٍ شامِل ═════════════════════

    /// <summary><b>مَجموعُ الأَنصِبَةِ يُساوي المَبلَغَ بِالضَبط</b> —
    /// لِكُلّ مَبلَغٍ في <c>[-1000, 1000]</c>، ولِكُلّ عَدَدِ أَطرافٍ في
    /// <c>[1, 10]</c>، ولِكُلّ سِياسَةِ باقٍ. <b>بِلا استِثناءٍ
    /// واحِد.</b></summary>
    [Fact]
    public void TheSumIsExactlyTheAmount_AcrossEveryEqualSplit()
    {
        var checkedCases = 0;
        var breaches = new List<string>();

        for (var total = -1000L; total <= 1000L; total++)
            for (var parts = 1; parts <= 10; parts++)
                foreach (var policy in Policies)
                {
                    var shares = Money.Allocate(total, parts, policy);
                    checkedCases++;

                    if (shares.Length != parts)
                        breaches.Add($"طول {total}/{parts}/{policy} = {shares.Length}");
                    else if (SumOf(shares) != total)
                        breaches.Add($"مجموع {total}/{parts}/{policy} = {SumOf(shares)}");
                }

        output.WriteLine($"الأنصبة المتساوية: {checkedCases} حالة مفحوصة، {breaches.Count} خرق.");
        Assert.Equal(60_030, checkedCases);
        Assert.Empty(breaches);
    }

    /// <summary>ونَفسُ الثابِتِ على <b>الأَوزان</b>: ‏1364 مُتَّجِهَ
    /// أَوزانٍ × ‏401 مَبلَغ × ‏3 سِياسات. ومُتَّجِهُ الأَصفارِ
    /// الخالِصِ حالَةٌ مُعلَنَةٌ لا مَسكوتٌ عَنها: صِفرٌ يُوَزَّعُ
    /// أَصفاراً، وغَيرُ الصِفرِ **يُرفَضُ صَراحَةً**.</summary>
    [Fact]
    public void TheSumIsExactlyTheAmount_AcrossEveryWeightVector()
    {
        var checkedCases = 0;
        var refusedCases = 0;
        var breaches = new List<string>();

        foreach (var weights in WeightVectors)
        {
            var weightSum = weights.Sum();

            for (var total = -200L; total <= 200L; total++)
                foreach (var policy in Policies)
                {
                    checkedCases++;

                    if (weightSum == 0 && total != 0)
                    {
                        Assert.Throws<ArgumentException>(
                            () => Money.Allocate(total, weights, policy));
                        refusedCases++;
                        continue;
                    }

                    var shares = Money.Allocate(total, weights, policy);

                    if (shares.Length != weights.Length)
                        breaches.Add($"طول [{string.Join(',', weights)}]/{total}/{policy}");
                    else if (SumOf(shares) != total)
                        breaches.Add($"مجموع [{string.Join(',', weights)}]/{total}/{policy} = {SumOf(shares)}");

                    // ووَزنُ صِفرٍ يَعني نَصيبَ صِفر — دائِماً.
                    for (var i = 0; i < weights.Length; i++)
                        if (weights[i] == 0 && shares[i] != 0)
                            breaches.Add($"وزن صفر أخذ نصيباً [{string.Join(',', weights)}]/{total}/{policy}");
                }
        }

        output.WriteLine($"الأوزان: {checkedCases} حالة مفحوصة، منها {refusedCases} رفضاً معلناً، {breaches.Count} خرق.");
        Assert.Equal(1_640_892, checkedCases);
        Assert.Equal(6_000, refusedCases);
        Assert.Empty(breaches);
    }

    // ═══ ٢. الصِفرُ والسالِبُ — والاستِردادُ يَعكِسُ التَوزيعَ حَرفاً ══

    /// <summary><b>مَبلَغُ صِفرٍ يُعطي أَصفاراً</b> بِأَيِّ عَدَدِ
    /// أَطرافٍ وأَيِّ سِياسَة — لا وَحدَةَ تُخلَقُ مِن لا شَيء.</summary>
    [Fact]
    public void ZeroSplitsIntoZeros()
    {
        var cases = 0;
        for (var parts = 1; parts <= 10; parts++)
            foreach (var policy in Policies)
            {
                Assert.All(Money.Allocate(0L, parts, policy), share => Assert.Equal(0L, share));
                cases++;
            }

        output.WriteLine($"الصفر: {cases} حالة.");
        Assert.Equal(30, cases);
    }

    /// <summary><b>طَرَفٌ واحِدٌ يَأخُذُ كُلَّ شَيء</b> — والباقي
    /// مَعدومٌ فَلا تَظهَرُ السِياسَةُ أَصلاً.</summary>
    [Fact]
    public void ASinglePartTakesTheWholeAmount()
    {
        var cases = 0;
        for (var total = -500L; total <= 500L; total++)
            foreach (var policy in Policies)
            {
                Assert.Equal(new[] { total }, Money.Allocate(total, 1, policy));
                cases++;
            }

        output.WriteLine($"الطرف الواحد: {cases} حالة.");
        Assert.Equal(3_003, cases);
    }

    /// <summary><b>الاستِردادُ يَعكِسُ التَوزيعَ بِالضَبط</b>:
    /// <c>Allocate(-t) == -Allocate(t)</c> عُنصُراً بِعُنصُر. وإلّا
    /// **لَم يَتَوازَنِ الدَفتَرُ عِندَ الإلغاء**: تُستَرَدُّ الهَلَلَةُ
    /// الزائِدَةُ مِن غَيرِ مَن أَخَذَها، فَيَبقى فَرقٌ لا يَعرِفُ
    /// أَحَدٌ مِن أَينَ جاء.</summary>
    [Fact]
    public void ARefundReversesTheAllocationExactly_EqualSplit()
    {
        var cases = 0;
        var breaches = new List<string>();

        for (var total = 0L; total <= 1000L; total++)
            for (var parts = 1; parts <= 10; parts++)
                foreach (var policy in Policies)
                {
                    var charged = Money.Allocate(total, parts, policy);
                    var refunded = Money.Allocate(-total, parts, policy);
                    cases++;

                    for (var i = 0; i < parts; i++)
                        if (refunded[i] != -charged[i])
                            breaches.Add($"{total}/{parts}/{policy}[{i}]");
                }

        output.WriteLine($"الاسترداد (متساوٍ): {cases} زوجاً، {breaches.Count} خرق.");
        Assert.Equal(30_030, cases);
        Assert.Empty(breaches);
    }

    /// <summary>ونَفسُه على الأَوزان — فَالاستِردادُ الجُزئيُّ يَمُرُّ
    /// بِأَنصِبَةٍ غَيرِ مُتَساوِيَة.</summary>
    [Fact]
    public void ARefundReversesTheAllocationExactly_Weighted()
    {
        var cases = 0;
        var breaches = new List<string>();

        foreach (var weights in WeightVectors)
        {
            if (weights.Sum() == 0) continue;

            for (var total = 0L; total <= 100L; total++)
                foreach (var policy in Policies)
                {
                    var charged = Money.Allocate(total, weights, policy);
                    var refunded = Money.Allocate(-total, weights, policy);
                    cases++;

                    for (var i = 0; i < weights.Length; i++)
                        if (refunded[i] != -charged[i])
                            breaches.Add($"[{string.Join(',', weights)}]/{total}/{policy}[{i}]");
                }
        }

        output.WriteLine($"الاسترداد (موزون): {cases} زوجاً، {breaches.Count} خرق.");
        Assert.Equal(411_777, cases);
        Assert.Empty(breaches);
    }

    // ═══ ٣. الحَتمِيَّة — مَن يُعيدُ الحِسابَ يَبلُغُ العَدَدَ نَفسَه ═

    /// <summary><b>نَفسُ المُدخَلاتِ تُعطي نَفسَ التَوزيعِ دائِماً.</b>
    /// وهذا شَرطُ المُطابَقَة: لَو تَبَدَّلَ التَوزيعُ بَينَ نِداءَين
    /// لَاستَحالَ على أَيِّ طَرَفٍ أَن يُراجِعَ ما قُبِض.</summary>
    [Fact]
    public void TheSameInputAlwaysGivesTheSameShares()
    {
        const int repeats = 3;
        var cases = 0;
        var breaches = new List<string>();

        for (var total = -300L; total <= 300L; total++)
            for (var parts = 1; parts <= 10; parts++)
                foreach (var policy in Policies)
                {
                    var first = Money.Allocate(total, parts, policy);
                    for (var again = 1; again < repeats; again++)
                        if (!first.SequenceEqual(Money.Allocate(total, parts, policy)))
                            breaches.Add($"{total}/{parts}/{policy}");
                    cases++;
                }

        foreach (var weights in WeightVectors)
        {
            if (weights.Sum() == 0) continue;

            foreach (var total in new[] { 0L, 1L, 7L, 101L, 10_007L })
                foreach (var policy in Policies)
                {
                    var first = Money.Allocate(total, weights, policy);
                    for (var again = 1; again < repeats; again++)
                        if (!first.SequenceEqual(Money.Allocate(total, weights, policy)))
                            breaches.Add($"[{string.Join(',', weights)}]/{total}/{policy}");
                    cases++;
                }
        }

        output.WriteLine($"الحتمية: {cases} حالة × {repeats} نداءات، {breaches.Count} خرق.");
        Assert.Equal(38_415, cases);
        Assert.Empty(breaches);
    }

    // ═══ ٤. لا عائِمَ في المَسار — ويُفحَصُ بِطَريقَتَين ══════════════

    /// <summary>
    /// <para><b>قِراءَةُ IL الصَنفِ نَفسِه</b>: لا رَمزَ عائِمٍ
    /// (<c>ldc.r4/r8</c>، <c>conv.r4/r8</c>، <c>conv.r.un</c>)، ولا
    /// مُتَغَيِّرَ مَحَلِّيٍّ عائِم، ولا وَسيطَ ولا مُرتَجَعَ ولا حَقلَ
    /// عائِم، ولا نِداءَ دالَّةٍ تَوقيعُها عائِم (‏<c>Math.Pow</c>
    /// مَثَلاً).</para>
    ///
    /// <para><b>ولِماذا لا يَكفي فَحصُ التَواقيع</b>: مُترجِمُ الإصدارِ
    /// قَد لا يُنشِئُ مُتَغَيِّراً مَحَلِّيّاً لِوَسيطٍ عائِمٍ يَبقى
    /// على المَكدَس، فَيَمُرُّ بِلا اسمٍ يُفحَص. والرُموزُ لا
    /// تَمُرّ.</para>
    /// </summary>
    [Fact]
    public void NoFloatingPointEverReachesTheMoneyPath()
    {
        var opCodeByValue = new Dictionary<short, OpCode>();
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (field.GetValue(null) is OpCode op)
                opCodeByValue[op.Value] = op;

        var types = new List<Type> { typeof(Money) };
        types.AddRange(typeof(Money).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));

        var breaches = new List<string>();
        var unresolved = new List<string>();
        var methodsScanned = 0;
        var instructionsScanned = 0;
        var callsResolved = 0;
        var callsUnresolved = 0;

        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        foreach (var type in types)
        {
            foreach (var field in type.GetFields(all))
                if (IsFloating(field.FieldType))
                    breaches.Add($"حقل عائم: {type.Name}.{field.Name}");

            foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                methodsScanned++;

                if (method is MethodInfo info && IsFloating(info.ReturnType))
                    breaches.Add($"مرتجع عائم: {type.Name}.{method.Name}");
                foreach (var parameter in method.GetParameters())
                    if (IsFloating(parameter.ParameterType))
                        breaches.Add($"وسيط عائم: {type.Name}.{method.Name}({parameter.Name})");

                var body = method.GetMethodBody();
                if (body is null) continue;

                foreach (var local in body.LocalVariables)
                    if (IsFloating(local.LocalType))
                        breaches.Add($"محلي عائم: {type.Name}.{method.Name}#{local.LocalIndex}");

                var il = body.GetILAsByteArray();
                if (il is null) continue;

                var offset = 0;
                while (offset < il.Length)
                {
                    short code = il[offset];
                    if (code == 0xFE) { code = unchecked((short)(0xFE00 | il[offset + 1])); offset += 2; }
                    else offset += 1;

                    var op = opCodeByValue[code];
                    instructionsScanned++;

                    if (op.Name is { } name &&
                        (name.EndsWith(".r4", StringComparison.Ordinal)
                         || name.EndsWith(".r8", StringComparison.Ordinal)
                         || name == "conv.r.un"))
                        breaches.Add($"رمز عائم: {type.Name}.{method.Name} → {name}");

                    var operandSize = op.OperandType switch
                    {
                        OperandType.InlineNone => 0,
                        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI
                            or OperandType.ShortInlineVar => 1,
                        OperandType.InlineVar => 2,
                        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI
                            or OperandType.InlineMethod or OperandType.InlineSig
                            or OperandType.InlineString or OperandType.InlineTok
                            or OperandType.InlineType or OperandType.ShortInlineR => 4,
                        OperandType.InlineI8 or OperandType.InlineR => 8,
                        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                        _ => throw new InvalidOperationException($"معامل غير معروف: {op.OperandType}")
                    };

                    if (op.OperandType is OperandType.InlineMethod or OperandType.InlineField)
                    {
                        var token = BitConverter.ToInt32(il, offset);
                        try
                        {
                            if (op.OperandType == OperandType.InlineMethod)
                            {
                                var called = method.Module.ResolveMethod(
                                    token, type.GetGenericArguments(), method.GetGenericArguments());
                                callsResolved++;
                                if (called is MethodInfo calledInfo && IsFloating(calledInfo.ReturnType))
                                    breaches.Add($"نداء عائم: {type.Name}.{method.Name} → {called.Name}");
                                foreach (var parameter in called!.GetParameters())
                                    if (IsFloating(parameter.ParameterType))
                                        breaches.Add($"نداء بوسيط عائم: {type.Name}.{method.Name} → {called.Name}");
                            }
                            else
                            {
                                var touched = method.Module.ResolveField(
                                    token, type.GetGenericArguments(), method.GetGenericArguments());
                                callsResolved++;
                                if (touched is not null && IsFloating(touched.FieldType))
                                    breaches.Add($"حقل عائم مقروء: {type.Name}.{method.Name} → {touched.Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            callsUnresolved++;
                            unresolved.Add($"{type.Name}.{method.Name} {op.Name} 0x{token:X8} {ex.GetType().Name}");
                        }
                    }

                    offset += operandSize;
                }
            }
        }

        output.WriteLine(
            $"IL: {types.Count} نوعاً، {methodsScanned} دالة، {instructionsScanned} تعليمة، " +
            $"{callsResolved} إحالة محلولة، {callsUnresolved} غير محلولة، {breaches.Count} خرق.");
        foreach (var hole in unresolved) output.WriteLine($"  ثقب: {hole}");

        Assert.True(methodsScanned >= 6, $"عدد الدوال المفحوصة {methodsScanned} — الأداة عمياء.");
        Assert.True(instructionsScanned >= 200, $"عدد التعليمات {instructionsScanned} — الأداة عمياء.");

        // **الثُقبُ يُثَبَّتُ عَدَداً ولا يُخفى** (‏القاعِدَة ١٠): إحالَةٌ
        // واحِدَةٌ لا تُحَلُّ اليَوم — نِداءٌ عامٌّ داخِلَ نَوعٍ مُوَلَّد.
        // ولَو نَمَت الثُقوبُ لَتَراجَعَت تَغطِيَةُ الفَحصِ صامِتَةً،
        // فَالعَدَدُ مُثَبَّت. **وما يَسُدُّه**: الرُموزُ العائِمَةُ
        // لا تَمُرُّ بِأَيِّ حال، والمِرجَعُ بِـ‏BigInteger عِندَ ‏2^62
        // يَكشِفُ فَقدَ الدِقَّةِ سُلوكِيّاً حَتّى لَو أُفلِتَ رَمز.
        Assert.True(callsUnresolved <= 1,
            $"ثقوب الإحالة نمت إلى {callsUnresolved}: {string.Join(" · ", unresolved)}");
        Assert.True(callsResolved >= 50, $"الإحالات المحلولة {callsResolved} — التغطية انهارت.");
        Assert.Empty(breaches);

        static bool IsFloating(Type? type) =>
            type == typeof(double) || type == typeof(float) || type == typeof(Half)
            || type == typeof(double?) || type == typeof(float?)
            || (type is { IsByRef: true } && IsFloating(type.GetElementType()));
    }

    /// <summary>
    /// <para><b>والفَحصُ الثاني سُلوكيّ</b>: مِقدارٌ فَوقَ <c>2^53</c>
    /// حَيثُ يَفقِدُ <c>double</c> الدِقَّةَ حَتماً، ومِرجَعٌ مُستَقِلٌّ
    /// مَحسوبٌ بِـ<c>BigInteger</c> — <b>نَوعٌ آخَرُ لِلعَدَد</b>.
    /// فَلَو تَسَرَّبَ عائِمٌ إلى المَسارِ لَاختَلَفَتِ الأَنصِبَةُ
    /// **بِمِئاتِ الوَحَدات** لا بِواحِدَة.</para>
    /// </summary>
    [Fact]
    public void AtMagnitudesWhereDoubleWouldLoseBits_TheSharesAreStillExact()
    {
        long[] hugeTotals =
        [
            9_007_199_254_740_993L,          // ‏2^53 + 1
            4_611_686_018_427_387_903L,      // ‏2^62 − 1
            long.MaxValue,
            long.MaxValue - 1,
            1_000_000_000_000_000_007L
        ];

        long[][] shapes =
        [
            [1, 1], [1, 1, 1], [1, 2, 3], [3, 2], [2, 3, 4],
            [25, 975], [7, 11, 13, 17], [0, 1, 1], [1_000_000_007, 3]
        ];

        var cases = 0;
        var breaches = new List<string>();

        foreach (var total in hugeTotals)
            foreach (var weights in shapes)
                foreach (var policy in Policies)
                    foreach (var signed in new[] { total, -total })
                    {
                        var actual = Money.Allocate(signed, weights, policy);
                        var expected = OracleAllocate(signed, weights, policy);
                        cases++;

                        if (!actual.SequenceEqual(expected))
                            breaches.Add(
                                $"{signed}/[{string.Join(',', weights)}]/{policy}: " +
                                $"[{string.Join(',', actual)}] ≠ [{string.Join(',', expected)}]");

                        if (SumOf(actual) != signed)
                            breaches.Add($"مجموع {signed}/[{string.Join(',', weights)}]/{policy}");
                    }

        output.WriteLine($"المقادير الكبيرة: {cases} حالة، {breaches.Count} خرق.");
        Assert.Equal(270, cases);
        Assert.Empty(breaches);
    }

    /// <summary>مِرجَعٌ مُستَقِلٌّ بِـ<c>BigInteger</c> — لا لِيُعيدَ
    /// إثباتَ المَنطِقِ بَل لِيُثبِتَ أَنَّه <b>لا يَفقِدُ بِتّاً</b>
    /// عِندَ مِقدارٍ يَكسِرُ العائِم.</summary>
    private static long[] OracleAllocate(long total, long[] weights, MoneyRemainder policy)
    {
        var negative = total < 0;
        var magnitude = BigInteger.Abs(total);
        BigInteger weightSum = weights.Aggregate(BigInteger.Zero, (a, w) => a + w);

        var shares = new BigInteger[weights.Length];
        var leftovers = new BigInteger[weights.Length];
        BigInteger handed = 0;

        for (var i = 0; i < weights.Length; i++)
        {
            var product = magnitude * weights[i];
            shares[i] = BigInteger.Divide(product, weightSum);
            leftovers[i] = product - shares[i] * weightSum;
            handed += shares[i];
        }

        var extra = (int)(magnitude - handed);

        switch (policy)
        {
            case MoneyRemainder.ToFirst:
                for (var i = 0; i < weights.Length && extra > 0; i++)
                    if (weights[i] > 0) { shares[i] += 1; extra--; }
                break;
            case MoneyRemainder.ToLast:
                for (var i = weights.Length - 1; i >= 0 && extra > 0; i--)
                    if (weights[i] > 0) { shares[i] += 1; extra--; }
                break;
            case MoneyRemainder.LargestRemainder:
                var order = Enumerable.Range(0, weights.Length)
                    .OrderByDescending(i => leftovers[i]).ThenBy(i => i).ToArray();
                for (var k = 0; k < extra; k++) shares[order[k]] += 1;
                break;
        }

        return [.. shares.Select(s => (long)(negative ? -s : s))];
    }

    // ═══ ٥. لِكُلِّ رَمزٍ في المَعجَمِ اختِبارٌ موجِبٌ وسالِب ══════════

    /// <summary><b>الحالَةُ الفاصِلَةُ الَّتي تُفَرِّقُ السِياساتِ
    /// الثَلاثَ</b>: أَوزان <c>[2, 3, 4]</c> ومَبلَغ ‏5. والأَنصِبَةُ
    /// الأَوَّلِيَّةُ <c>[1, 1, 2]</c> بِبَواقٍ <c>[1, 6, 2]</c>،
    /// فَالوَحدَةُ الزائِدَةُ الواحِدَةُ تَذهَبُ إلى ثَلاثَةِ أَطرافٍ
    /// مُختَلِفَةٍ بِحَسَبِ السِياسَة — <b>وهذا هو مَعنى أَنّ السِياسَةَ
    /// قَرارٌ لا تَفصيل</b>.</summary>
    [Theory]
    [InlineData(MoneyRemainder.ToFirst, 2, 1, 2)]
    [InlineData(MoneyRemainder.ToLast, 1, 1, 3)]
    [InlineData(MoneyRemainder.LargestRemainder, 1, 2, 2)]
    public void EachPolicyHandsTheExtraUnitToADifferentParty(
        MoneyRemainder policy, long first, long second, long third)
        => Assert.Equal(new[] { first, second, third },
            Money.Allocate(5L, new long[] { 2, 3, 4 }, policy));

    /// <summary>والسالِبُ لِكُلِّ رَمز: كُلُّ سِياسَةٍ <b>لا</b> تُعطي
    /// ما تُعطيه أُختاها في الحالَةِ الفاصِلَةِ نَفسِها.</summary>
    [Theory]
    [InlineData(MoneyRemainder.ToFirst, MoneyRemainder.ToLast)]
    [InlineData(MoneyRemainder.ToFirst, MoneyRemainder.LargestRemainder)]
    [InlineData(MoneyRemainder.ToLast, MoneyRemainder.ToFirst)]
    [InlineData(MoneyRemainder.ToLast, MoneyRemainder.LargestRemainder)]
    [InlineData(MoneyRemainder.LargestRemainder, MoneyRemainder.ToFirst)]
    [InlineData(MoneyRemainder.LargestRemainder, MoneyRemainder.ToLast)]
    public void NoTwoPoliciesAgreeOnTheDecidingCase(MoneyRemainder one, MoneyRemainder other)
        => Assert.NotEqual(
            Money.Allocate(5L, new long[] { 2, 3, 4 }, one),
            Money.Allocate(5L, new long[] { 2, 3, 4 }, other));

    /// <summary>‏100.00 ر.س على ثَلاثَة = ‏3333 + 3333 + 3334 هَلَلَة —
    /// المِثالُ المَنصوصُ في التَكليفِ وفي ‏ADR-029.</summary>
    [Theory]
    [InlineData(MoneyRemainder.ToFirst, 3334, 3333, 3333)]
    [InlineData(MoneyRemainder.ToLast, 3333, 3333, 3334)]
    [InlineData(MoneyRemainder.LargestRemainder, 3334, 3333, 3333)]
    public void OneHundredRiyalsOverThreeParties(
        MoneyRemainder policy, long first, long second, long third)
        => Assert.Equal(new[] { first, second, third },
            Money.Allocate(Money.ToMinor(100.00m, 2), 3, policy));

    /// <summary>وقيمَةٌ خارِجَ المَعجَمِ تُرفَض — لا تَرتَدُّ إلى
    /// افتِراضٍ صامِت.</summary>
    [Fact]
    public void APolicyOutsideTheClosedVocabularyIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Money.Allocate(10L, 3, (MoneyRemainder)99));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Money.Allocate(10L, new long[] { 1, 1 }, (MoneyRemainder)(-1)));
    }

    // ═══ ٦. الحُدود — ما يُرفَضُ يُرفَضُ بِاسمِه ══════════════════════

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void FewerThanOnePartIsRefused(int parts)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => Money.Allocate(100L, parts, MoneyRemainder.ToFirst));

    [Fact]
    public void ANegativeWeightIsNotAShare()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => Money.Allocate(100L, new long[] { 1, -1 }, MoneyRemainder.ToFirst));

    [Fact]
    public void WeightsThatSumToZeroRefuseANonZeroAmount()
        => Assert.Throws<ArgumentException>(
            () => Money.Allocate(1L, new long[] { 0, 0, 0 }, MoneyRemainder.ToFirst));

    [Fact]
    public void WeightsThatSumToZeroStillSplitZeroIntoZeros()
        => Assert.Equal(new long[] { 0, 0, 0 },
            Money.Allocate(0L, new long[] { 0, 0, 0 }, MoneyRemainder.LargestRemainder));

    [Fact]
    public void AnEmptyWeightListSplitsZeroIntoNothing()
        => Assert.Empty(Money.Allocate(0L, Array.Empty<long>(), MoneyRemainder.ToFirst));

    [Fact]
    public void ANullWeightListIsRefused()
        => Assert.Throws<ArgumentNullException>(
            () => Money.Allocate(10L, null!, MoneyRemainder.ToFirst));

    /// <summary><c>long.MinValue</c> بِلا نَظيرٍ موجِب، فَالمِرآةُ
    /// تَنكَسِرُ عِندَه — <b>ويُرفَضُ صَراحَةً بَدَلَ أَن يَفيضَ
    /// صامِتاً</b>.</summary>
    [Fact]
    public void TheOneAmountWithNoMirrorIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Money.Allocate(long.MinValue, 3, MoneyRemainder.ToFirst));
        Assert.Equal(3, Money.Allocate(long.MinValue + 1, 3, MoneyRemainder.ToFirst).Length);
    }

    /// <summary>والأَوزانُ الكَبيرَةُ لا تُفيضُ الحِسابَ الوَسيط:
    /// <c>total × weight</c> يَتَجاوَزُ <c>long</c> بِسُهولَة،
    /// و<b>الفَيَضانُ الصامِتُ في المالِ عَيبٌ لا يُرى</b>.</summary>
    [Fact]
    public void HugeWeightsDoNotOverflowTheIntermediateProduct()
    {
        var shares = Money.Allocate(
            long.MaxValue,
            new[] { long.MaxValue, long.MaxValue, 1L },
            MoneyRemainder.LargestRemainder);

        Assert.Equal(long.MaxValue, (long)SumOf(shares));
        Assert.All(shares, share => Assert.True(share >= 0));
    }

    // ═══ ٧. الحَدُّ إلى الوَحدَةِ الصُغرى ومِنها ══════════════════════

    /// <summary><b>نِصفُ الوَحدَةِ يُقَرَّبُ بَعيداً عَنِ الصِفرِ
    /// دائِماً</b> — لا مَصرِفِيّاً بِحَسَبِ زَوجِيَّةِ الرَقَم.</summary>
    [Theory]
    [InlineData(0.005, 2, 1)]
    [InlineData(0.015, 2, 2)]
    [InlineData(0.025, 2, 3)]
    [InlineData(-0.005, 2, -1)]
    [InlineData(-0.015, 2, -2)]
    [InlineData(2.5, 0, 3)]
    [InlineData(3.5, 0, 4)]
    [InlineData(-2.5, 0, -3)]
    public void HalfUnitsRoundAwayFromZero(decimal amount, int exponent, long expected)
        => Assert.Equal(expected, Money.ToMinor(amount, exponent));

    /// <summary><b>وقاعِدَةُ التَحويلِ هي قاعِدَةُ Paddle حَرفاً</b> —
    /// مَقيسٌ بِكَنسِ المَدى لا بِأَمثِلَة. والحَدّانِ لَم يُوَحَّدا
    /// بَعد (قَرارُ مالِك، انظُر ‏ADR-029 §٦)، <b>فَالحارِسُ يَمنَعُ
    /// انجِرافَهُما في الأَثناء</b> (‏القاعِدَة ٢).</summary>
    [Fact]
    public void TheMinorUnitRuleMatchesThePaddleBoundaryExactly()
    {
        string[] currencies = ["USD", "SAR", "JPY", "KRW", "EUR"];
        var cases = 0;
        var breaches = new List<string>();

        foreach (var currency in currencies)
        {
            var exponent = PaddleCurrencies.Exponent(currency);

            for (var amount = -50.000m; amount <= 50.000m; amount += 0.005m)
            {
                var here = Money.ToMinor(amount, exponent);
                var there = long.Parse(PaddleCurrencies.Minor(amount, currency));
                cases++;

                if (here != there) breaches.Add($"{amount} {currency}: {here} ≠ {there}");
            }
        }

        output.WriteLine($"تكافؤ الحدّ: {cases} مقارنة، {breaches.Count} خرق.");
        Assert.Equal(100_005, cases);
        Assert.Empty(breaches);
    }

    /// <summary>والذَهابُ والإيابُ يُعيدُ المَبلَغَ كَما هو حينَ يَكونُ
    /// المَبلَغُ مَحسوماً بِالأُسِّ نَفسِه.</summary>
    [Fact]
    public void MinorUnitsRoundTripBackToTheAmount()
    {
        var cases = 0;
        for (var minor = -10_000L; minor <= 10_000L; minor++)
            for (var exponent = 0; exponent <= 4; exponent++)
            {
                Assert.Equal(minor, Money.ToMinor(Money.FromMinor(minor, exponent), exponent));
                cases++;
            }

        output.WriteLine($"الذهاب والإياب: {cases} حالة.");
        Assert.Equal(100_005, cases);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    [InlineData(int.MaxValue)]
    public void AnExponentOutsideTheRangeIsRefused(int exponent)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.ToMinor(1m, exponent));
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.FromMinor(1L, exponent));
    }

    // ═══ ٨. البِنيَةُ خالِصَة — لا عُمولَةَ ولا نِسبَةَ ولا مُنتَج ════

    /// <summary><b>الدالَّةُ لا تَعرِفُ نِسبَةً ولا عُمولَةً ولا
    /// عُملَةً بِعَينِها</b>: النِسبَةُ تَدخُلُ **وَزناً صَحيحاً** مِن
    /// فَوق. و‏‎2.5٪‎ = <c>[25, 975]</c>، و‏‎5٪‎ = <c>[5, 95]</c> —
    /// والقِسمَةُ واحِدَةٌ مَضمونَةُ المَجموع، لا ضَربٌ ثُمَّ طَرحٌ
    /// يَترُكُ فَرقاً.</summary>
    [Theory]
    [InlineData(25, 975, 41_000, 1_025, 39_975)]
    [InlineData(5, 95, 41_000, 2_050, 38_950)]
    [InlineData(1, 0, 41_000, 41_000, 0)]
    public void ARateEntersAsAnIntegerWeightFromAbove(
        long platformWeight, long sellerWeight, long totalMinor,
        long expectedPlatform, long expectedSeller)
    {
        var shares = Money.Allocate(
            totalMinor, new[] { platformWeight, sellerWeight }, MoneyRemainder.ToFirst);

        Assert.Equal(new[] { expectedPlatform, expectedSeller }, shares);
        Assert.Equal(totalMinor, shares[0] + shares[1]);
    }

    /// <summary>ومَصدَرُ الأَرقامِ في المُستَودَعِ لا يَتَسَرَّبُ إلى
    /// هُنا: <b>صِفرُ ذِكرٍ لِنِسبَةٍ أَو عُمولَةٍ أَو صَفقَةٍ في
    /// <c>Money</c></b> — فَالبِنيَةُ تُبنى مَرَّةً وتَخدُمُ كُلَّ
    /// قِسمَة.</summary>
    [Fact]
    public void TheAllocatorNamesNoProductConcept()
    {
        var names = typeof(Money).GetMembers(
                BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(member => member.Name)
            .ToArray();

        Assert.NotEmpty(names);
        foreach (var forbidden in new[] { "Commission", "Deal", "Vat", "Tax", "Platform", "Seller", "Rate" })
            Assert.DoesNotContain(names, name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));

        output.WriteLine($"أعضاء Money المفحوصة: {names.Length}.");
    }
}
