namespace ACommerce.Kit.Culture;

/// <summary>
/// <para><b>القامُوس يَحمِل الجُملَة، والمُنَسِّق يَحمِل المال.</b></para>
///
/// <para>قَبلَ هذا المِلَفّ كانَت العُملَة <b>مُندَمِجَة بِاللُغَة</b>:
/// مِفتاح مِثل <c>listings.price.rental = "{0:N0} ر.س / {1}"</c> يَحبِس
/// ثَلاثَة مَحاوِر في سِلسِلَة واحِدَة — نَصّ الجُملَة، ورَمز العُملَة،
/// وشَكل الرَقَم. فَإضافَةُ الإنجليزِيَّة يَوماً كانَت تُعطي
/// «‏1,000 ر.س» في واجِهَة إنجليزِيَّة، لِأَنّ الرَمز راكِبٌ في قيمَة
/// المِفتاح لا في المُنَسِّق. والمَحاوِر الثَلاثَة تُفَكّ هُنا: المِفتاح
/// يَصير <c>"{0} / {1}"</c>، و<c>{0}</c> يَصِل <b>مُنَسَّقاً</b>.</para>
///
/// <para><b>ولِماذا هُنا لا في مِلَفّ جَديد</b> (‏القاعِدَة ٨ — لا أُنبوب
/// رابِع): العُملَة تَعيش في هذه العُدَّة أَصلاً
/// (<see cref="ICultureContext.Currency"/>)، وفيها
/// <see cref="ICultureContext.FormatMoney"/> مُسَجَّلَة في DI ولَها
/// middleware في الأُنبوب — <b>وصِفر مُستَهلِك</b> (مَقيس:
/// <c>grep FormatMoney</c> خارِج تَعريفِها = صِفر). وأَسوَأ مِن ذلك أَنَّها
/// كانَت <b>تُخالِف الشاشات</b>: <c>N2</c> ورَمزٌ لاحِق، والشاشات كُلُّها
/// <c>N0</c>. فَلَو استُعمِلَت يَوماً لَأَعطَت «‏1,000.00 ر.س» حَيثُ
/// المَعروض «‏1,000 ر.س». فَالمَكتوب هُنا يُصلِح القائِم ولا يُنافِسُه:
/// <c>FormatMoney</c> صارَت تُفَوِّض إلى <see cref="Format"/>.</para>
///
/// <para><b>والتَكافُؤ صِفريّ بِالبَناء</b>: <see cref="Format"/> تُنتِج
/// حَرفِيّاً ما كانَت تُنتِجُه الحَرفِيَّة — نَفس <c>N0</c>، ونَفس
/// المَسافَة الواحِدَة، ونَفس مَوضِع الرَمز بَعدَ الرَقَم. ولا تُمَرَّر
/// ثَقافَة صَريحَة: <c>{amount:N0}</c> هُنا هي <c>{l.Price:N0}</c> هُناكَ
/// حَرفاً بِحَرف، فَشَكل الرَقَم لا يَتَبَدَّل. وذاكَ مَقصود —
/// <b>فَتحُ البابِ لَيسَ عُبورَه</b>: المِحوَر الثالِث (هِنديّ/لاتينيّ)
/// يَبقى كَما هو حَتّى يُقَرَّر صَراحَةً.</para>
///
/// <para><b>ودَينٌ مُعلَن لا مَستور</b>: لا حَقلَ عُملَة في
/// <c>ACommerce.Kit.Tenants.Tenant</c> (مَقيس: صِفر مُطابَقَة لِـ
/// <c>Currency</c> في العُدَّة كُلِّها). فَالمَصدَر اليَوم ثابِتٌ واحِد
/// هو <see cref="DefaultCurrency"/>، وإضافَةُ الحَقل تَغييرُ بَيانات
/// لَه مَوجَتُه. والتَوقيع يَقبَل العُملَة وَسيطاً مِن اليَوم، فَيَوم
/// يوجَد الحَقل يُمَرَّر ولا يُعاد المُرور على مَواضِع الاستِدعاء.</para>
///
/// <para><b>وأُضيفَ التَوزيعُ إلى الصَنفِ نَفسِه — ‏2026-09-01</b>
/// (‏<c>docs/ADR-029</c>): التَنسيقُ يُجيبُ «كَيفَ يُكتَبُ المَبلَغ»،
/// والتَوزيعُ يُجيبُ «كَيفَ يُقسَمُ المَبلَغ»، وكِلاهُما حِسابُ مالٍ
/// <b>نَقِيّ</b> لا يَعتَمِدُ على مُزَوِّدٍ ولا قاعِدَةِ بَيانات.
/// و<c>Culture.Core</c> عُدَّةٌ <b>بِصِفرِ اعتِماد</b>، فَتَصلُحُ مَقَرّاً
/// لِما يَستَهلِكُه القالِبُ والمُزَوِّدانِ مَعاً — بِخِلافِ
/// <c>Payments.Core</c> الَّتي تَجعَلُ القالِبَ يَرِثُ المَدفوعاتِ
/// لِأَجلِ حِسابٍ لا عَلاقَةَ لَه بِمُزَوِّد (‏القاعِدَة ٨).</para>
/// </summary>
public static class Money
{
    /// <summary>العُملَة حينَ لا يُمَرَّر شَيء. ثابِتٌ واحِد لا حَقل
    /// مُستَأجِر — انظُر «دَينٌ مُعلَن» أَعلاه.</summary>
    public const string DefaultCurrency = "SAR";

    /// <summary>اللُغَة حينَ لا تُمَرَّر. العَرَبِيَّة هي المَعجَم
    /// المُغلَق في <c>docs/I18N.md</c>، فَهي السُقوط هُنا أَيضاً.</summary>
    public const string DefaultLocale = "ar";

    /// <summary><c>«‏1,000 ر.س»</c> — الرَقَم ثُمَّ مَسافَة ثُمَّ
    /// الوَحدَة. هذا هو الشَكل المَعروض اليَوم في كُلّ شاشَة، ولا
    /// يَتَبَدَّل بِهذا المِلَفّ.</summary>
    public static string Format(
        decimal amount,
        string? currency = null,
        string? locale = null,
        MoneyUnitStyle style = MoneyUnitStyle.Symbol)
        => $"{amount:N0} {Unit(currency, locale, style)}";

    /// <summary><para>الوَحدَة وَحدَها — لِلمَواضِع الَّتي تَذكُر
    /// العُملَة بِلا مَبلَغ («سِعر مُقتَرَح (ريال)»).</para>
    ///
    /// <para><b>ولِماذا شَكلان لِلوَحدَة نَفسِها</b>: المَقيس أَنّ
    /// المُستَودَع يَكتُب الريال بِصيغَتَين — <c>ر.س</c> في ‏55 مَوضِعاً
    /// و<c>ريال</c> في ‏28. وتَوحيدُهُما <b>تَغييرٌ بَصَريّ مَحسوس</b>
    /// على شاشات قائِمَة، أَي قَرار مالِك لا كَنسُ مُنَسِّق. فَالشَكلان
    /// يُحفَظان هُنا صَراحَةً لِيَبقى التَكافُؤ صِفرِيّاً، ويُقال
    /// التَنافُر رَقماً بَدَل أَن يُخفى.</para></summary>
    public static string Unit(
        string? currency = null,
        string? locale = null,
        MoneyUnitStyle style = MoneyUnitStyle.Symbol)
    {
        var code = string.IsNullOrWhiteSpace(currency) ? DefaultCurrency : currency;
        var lang = string.IsNullOrWhiteSpace(locale) ? DefaultLocale : locale;

        if (code == "SAR" && lang == "ar")
            return style == MoneyUnitStyle.Name ? "ريال" : "ر.س";

        // كُلّ ما عَدا ذلك: الرَمز الدَوليّ. لُغَةٌ ثانِيَة أَو عُملَة
        // ثانِيَة تَبدَأ مِن هُنا، ولا تَمُرّ على مَوضِع استِدعاء واحِد.
        return code;
    }

    // ═════════════════════════════════════════════════════════════════
    //  التَوزيع — الثابِتُ الوَحيدُ الَّذي لا يُخرَقُ أَبَداً
    //  ‏docs/ADR-029-WHO-EATS-THE-REMAINING-FILS.md
    // ═════════════════════════════════════════════════════════════════
    //
    // **الثابِتُ المُطلَق**: `Allocate(t, …).Sum() == t` — لِكُلّ مَبلَغ،
    // ولِكُلّ عَدَدِ أَطراف، ولِكُلّ تَوزيعِ أَوزان، وسالِباً كانَ أَو
    // موجِباً. **بِلا استِثناءٍ واحِد.**
    //
    // **ولِماذا `long` لا `decimal`**: `decimal` يَقبَل كُسوراً أَدَقَّ
    // مِن الهَلَلَة، فَتَصير «البَقِيَّة» قيمَةً كَسرِيَّةً تُقَرَّبُ
    // لاحِقاً في مَوضِعٍ آخَر — فَيَنكَسِرُ المَجموعُ **صامِتاً**.
    // والحِفظُ الوَحيدُ المُبرهَنُ في عَدَدٍ صَحيحٍ مِن الوَحَداتِ
    // الصُغرى: قِسمَةٌ صَحيحَة، وباقٍ مِن `%` يُوَزَّعُ بِسِياسَةٍ
    // مُعلَنَة. والمُستَودَع يَخزُن `decimal` بِالريال، فَالتَحويلُ
    // يَبقى عِندَ الحَدّ — تَماماً كَما يَفعَل حَدُّ Paddle اليَوم.
    //
    // **ولا عائِمَ في المَسار**: لا `double` ولا `float` ولا
    // `Math.Round(double)`. الحِسابُ الوَسيطُ في `Int128` لِأَنّ
    // `abs * weight` يَفيضُ عَن `long` بِأَوزانٍ كَبيرَة —
    // **والفَيَضانُ الصامِتُ في المالِ عَيبٌ لا يُرى**. وهُناكَ
    // اختِبارٌ يَقرَأُ IL هذا الصَنفِ ويَفشَلُ عِندَ أَوَّلِ رَمزٍ
    // عائِمٍ يَتَسَرَّبُ إلَيه.

    /// <summary>أَقصى أُسٍّ مَقبول. ‏9 لِأَنّ <c>10^9</c> يَبقى
    /// في مَدى الحِساب بِأَمان، ولا عُملَةَ في ISO 4217 تَتَجاوَزُ
    /// الأُسَّ ‏4 أَصلاً — فَالحَدُّ فُسحَةٌ لا دَعوى.</summary>
    public const int MaxExponent = 9;

    /// <summary>
    /// <para><b>مَبلَغٌ بِالوَحدَةِ الصُغرى ⟵ ‏N أَنصِبَةٍ مُتَساوِيَة.</b>
    /// وهي <see cref="Allocate(long, IReadOnlyList{long}, MoneyRemainder)"/>
    /// بِأَوزانٍ كُلُّها واحِد — لا خوارِزمِيَّةٌ ثانِيَة، فَلا
    /// يَنجَرِفُ الطَريقانِ.</para>
    ///
    /// <para><b>و<paramref name="remainder"/> بِلا قيمَةٍ افتِراضِيَّة</b>
    /// عَمداً: الافتِراضُ يَجعَلُ السِياسَةَ **تُختارُ بِالسَهو**،
    /// والسِياسَةُ هُنا تَقولُ <b>مَن يَأكُلُ الهَلَلَةَ الباقِيَة</b> —
    /// وذاكَ قَرارٌ تِجارِيٌّ يُعلَنُ في مَوضِعِ الاستِدعاء.</para>
    /// </summary>
    /// <param name="totalMinor">المَبلَغُ بِالوَحدَةِ الصُغرى. السالِبُ
    /// مَقبولٌ (استِرداد)، و<c>long.MinValue</c> مَرفوضٌ لِأَنّ
    /// نَظيرَه الموجِبَ لا يوجَد — فَالمِرآةُ تَنكَسِرُ عِندَه.</param>
    /// <param name="parts">عَدَدُ الأَطراف، واحِدٌ فَأَكثَر.</param>
    /// <param name="remainder">مَن يَأخُذُ الوَحَداتِ الزائِدَة.</param>
    public static long[] Allocate(long totalMinor, int parts, MoneyRemainder remainder)
    {
        if (parts < 1)
            throw new ArgumentOutOfRangeException(
                nameof(parts), parts, "عَدَدُ الأَطرافِ لا يَقِلُّ عَن واحِد.");

        var weights = new long[parts];
        Array.Fill(weights, 1L);
        return Allocate(totalMinor, weights, remainder);
    }

    /// <summary>
    /// <para><b>مَبلَغٌ بِالوَحدَةِ الصُغرى ⟵ أَنصِبَةٌ بِأَوزان.</b>
    /// الوَزنُ عَدَدٌ صَحيحٌ غَيرُ سالِب — <b>ولَيسَ نِسبَةً مِئَوِيَّة
    /// ولا كَسراً عائِماً</b>، فَالنِسَبُ تَدخُلُ أَوزاناً صَحيحَةً
    /// (‏‎2.5٪‎ ⟵ <c>[25, 975]</c>) ويَبقى الحِسابُ كُلُّه صَحيحاً.</para>
    ///
    /// <para><b>ووَزنُ صِفرٍ يَعني نَصيبَ صِفر — دائِماً.</b> لا
    /// يَستَقبِلُ صاحِبُه وَحدَةً زائِدَةً بِأَيِّ سِياسَة؛ فَمَن لا
    /// نَصيبَ لَه لا يَأخُذُ هَلَلَة. وهذا مُبرهَنٌ لا مَظنون: عَدَدُ
    /// الوَحَداتِ الزائِدَةِ أَقَلُّ حَتماً مِن عَدَدِ الأَطرافِ ذاتِ
    /// الباقي غَيرِ الصِفريّ، وتِلكَ كُلُّها ذاتُ وَزنٍ موجِب.</para>
    ///
    /// <para><b>ومَجموعُ أَوزانٍ صِفرٌ</b>: إن كانَ المَبلَغُ صِفراً
    /// فَالأَنصِبَةُ أَصفار (والثابِتُ قائِم)، وإلّا فَهو <b>رَفضٌ
    /// صَريح</b> — لا سَبيلَ لِقِسمَةِ مالٍ على لا أَحَد، والسُكوتُ
    /// عَنها يُضَيِّعُ المَبلَغَ صامِتاً.</para>
    /// </summary>
    public static long[] Allocate(
        long totalMinor, IReadOnlyList<long> weights, MoneyRemainder remainder)
    {
        ArgumentNullException.ThrowIfNull(weights);

        if (!Enum.IsDefined(remainder))
            throw new ArgumentOutOfRangeException(
                nameof(remainder), remainder,
                "سِياسَةُ الباقي مَعجَمٌ مُغلَق — لا قيمَةَ خارِجَه.");

        if (totalMinor == long.MinValue)
            throw new ArgumentOutOfRangeException(
                nameof(totalMinor), totalMinor,
                "‏long.MinValue بِلا نَظيرٍ موجِب، فَالاستِردادُ لا يَعكِسُ التَوزيع.");

        var count = weights.Count;
        Int128 totalWeight = 0;
        for (var i = 0; i < count; i++)
        {
            var w = weights[i];
            if (w < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(weights), w, "وَزنٌ سالِبٌ لَيسَ نَصيباً.");
            totalWeight += w;
        }

        var negative = totalMinor < 0;
        var magnitude = negative ? -(Int128)totalMinor : totalMinor;

        if (totalWeight == 0)
        {
            if (totalMinor != 0)
                throw new ArgumentException(
                    "لا يُقسَمُ مَبلَغٌ غَيرُ صِفرٍ على أَوزانٍ مَجموعُها صِفر.",
                    nameof(weights));
            return new long[count];
        }

        var shares = new long[count];
        var leftovers = new Int128[count];
        Int128 handed = 0;

        for (var i = 0; i < count; i++)
        {
            var product = magnitude * weights[i];
            var quotient = product / totalWeight;
            leftovers[i] = product - quotient * totalWeight;
            shares[i] = (long)quotient;
            handed += quotient;
        }

        // ‏extra < عَدَد الأَطراف ذات الباقي غَير الصِفريّ ≤ عَدَد
        // الأَطراف ذات الوَزن الموجِب. فَالتَوزيعُ يَجِدُ دائِماً
        // أَصحاباً يَستَحِقّون، ولا تَضيعُ وَحدَةٌ ولا تُضاعَف.
        var extra = (int)(magnitude - handed);

        switch (remainder)
        {
            case MoneyRemainder.ToFirst:
                for (var i = 0; i < count && extra > 0; i++)
                    if (weights[i] > 0) { shares[i]++; extra--; }
                break;

            case MoneyRemainder.ToLast:
                for (var i = count - 1; i >= 0 && extra > 0; i--)
                    if (weights[i] > 0) { shares[i]++; extra--; }
                break;

            case MoneyRemainder.LargestRemainder:
                var order = new int[count];
                for (var i = 0; i < count; i++) order[i] = i;
                // تَرتيبٌ كُلِّيٌّ بِلا تَعادُل: الباقي تَنازُلِيّاً، ثُمَّ
                // الفَهرَسُ تَصاعُدِيّاً. فَالنَتيجَةُ حَتمِيَّةٌ ولا
                // تَعتَمِدُ على استِقرارِ الفَرز.
                Array.Sort(order, (a, b) =>
                {
                    var byLeftover = leftovers[b].CompareTo(leftovers[a]);
                    return byLeftover != 0 ? byLeftover : a.CompareTo(b);
                });
                for (var k = 0; k < extra; k++) shares[order[k]]++;
                break;
        }

        if (negative)
            for (var i = 0; i < count; i++) shares[i] = -shares[i];

        return shares;
    }

    /// <summary>
    /// <para><b>‏decimal ⟵ وَحدَةٌ صُغرى صَحيحَة</b>، بِـ
    /// <c>MidpointRounding.AwayFromZero</c> لا المَصرِفِيَّةِ
    /// الافتِراضِيَّة — <b>نَفسُ قاعِدَةِ
    /// <c>PaddleCurrencies.Minor</c> حَرفاً</b>، ومَقيسٌ تَطابُقُهُما
    /// بِاختِبارٍ يَكنُسُ المَدى (‏<c>MoneyAllocationTests</c>).</para>
    ///
    /// <para><b>ولِماذا لا يُفَوِّضُ أَحَدُهُما إلى الآخَر اليَوم</b>:
    /// <c>Culture</c> لا يَجوزُ أَن تَعتَمِدَ على مُزَوِّدِ دَفع،
    /// والعَكسُ تَغييرٌ على مَسارِ مالٍ حَيٍّ لَه مَوجَتُه وقَرارُ
    /// مالِكِه. فَالحَدُّ **يُقاسُ حتّى يُوَحَّد** (‏القاعِدَة ٢)،
    /// ولا يُتركُ يَنجَرِفُ بِلا حارِس.</para>
    ///
    /// <para><b>والأُسُّ يُمَرَّرُ ولا يُخمَّن</b>: <c>Exponent()</c>
    /// تَسكُنُ عُدَّةَ المُزَوِّد، فَلا تَنشَأُ هُنا قائِمَةُ عُملاتٍ
    /// ثانِيَةٌ تَنجَرِفُ عَن الأولى (‏القاعِدَة ١٦).</para>
    /// </summary>
    public static long ToMinor(decimal amount, int exponent)
    {
        if (exponent is < 0 or > MaxExponent)
            throw new ArgumentOutOfRangeException(
                nameof(exponent), exponent, $"الأُسُّ بَينَ ‏0 و‏{MaxExponent}.");

        return (long)decimal.Round(
            amount * Pow10(exponent), 0, MidpointRounding.AwayFromZero);
    }

    /// <summary><b>والعَكس</b> — وهي غَيرُ مَوجودَةٍ في المُستَودَعِ قَبلَ
    /// اليَوم (<c>Minor</c> أُحادِيَّةُ الاتِّجاه)، ويَحتاجُها كُلُّ
    /// عَرضٍ لِنَصيبٍ مَحسوب. والقِسمَةُ على قُوَّةِ عَشَرَةٍ في
    /// <c>decimal</c> <b>تامَّةٌ بِلا فَقد</b>.</summary>
    public static decimal FromMinor(long minor, int exponent)
    {
        if (exponent is < 0 or > MaxExponent)
            throw new ArgumentOutOfRangeException(
                nameof(exponent), exponent, $"الأُسُّ بَينَ ‏0 و‏{MaxExponent}.");

        return minor / Pow10(exponent);
    }

    /// <summary>قُوَّةُ عَشَرَةٍ في <c>decimal</c> — بِضَربٍ لا بِـ
    /// <c>Math.Pow</c>، فَتِلكَ تَأخُذُ <c>double</c> وتُعيدُه.</summary>
    private static decimal Pow10(int exponent)
    {
        var value = 1m;
        for (var i = 0; i < exponent; i++) value *= 10m;
        return value;
    }
}

/// <summary>صيغَة الوَحدَة: الرَمز المُختَصَر (<c>ر.س</c>) أَم الاسم
/// (<c>ريال</c>). مَعجَمٌ مُغلَق بِقيمَتَين — لا سِلسِلَة حُرَّة.</summary>
public enum MoneyUnitStyle
{
    Symbol,
    Name
}

/// <summary>
/// <para><b>مَن يَأكُلُ الهَلَلَةَ الباقِيَة — مَعجَمٌ مُغلَقٌ لا
/// سِلسِلَةٌ حُرَّة.</b> ‏100.00 ر.س على ثَلاثَةٍ = ‏3333 + 3333 + 3334
/// هَلَلَة، والرابِعَةُ لا وُجودَ لَها: <b>الهَلَلَةُ الزائِدَةُ
/// تَذهَبُ إلى طَرَفٍ بِعَينِه</b>، والسُؤالُ أَيُّهُم — لا «هَل».</para>
///
/// <para><b>ولا قيمَةَ رابِعَة</b>: كُلُّ ما يُقتَرَحُ عادَةً
/// («عَشوائِيّ»، «بِالدَور حَسَبَ آخِرِ مَرَّة») يَكسِرُ نَقاءَ
/// الدالَّة — الأَوَّلُ بِالعَشوائِيَّة، والثاني بِحالَةٍ مُخَزَّنَة.
/// وكِلاهُما يَجعَلُ <b>مَن يُعيدُ الحِسابَ لا يَبلُغُ العَدَدَ
/// نَفسَه</b>، فَتَستَحيلُ المُطابَقَة.</para>
///
/// <para>التَفصيلُ والحُجَّةُ في
/// <c>docs/ADR-029-WHO-EATS-THE-REMAINING-FILS.md</c>.</para>
/// </summary>
public enum MoneyRemainder
{
    /// <summary>الوَحَداتُ الزائِدَةُ إلى <b>الأَوائِلِ بِتَرتيبِ
    /// القائِمَة</b>. فَالتَرتيبُ يُعلِنُ المُستَفيد، ومَوضِعُ
    /// الاستِدعاءِ هُوَ مَن يُرَتِّب.</summary>
    ToFirst,

    /// <summary>إلى <b>الأَواخِر</b>. مِرآةُ <see cref="ToFirst"/>
    /// حَرفاً، ولِمَن يُرَتِّبُ قائِمَتَه بِالعَكس.</summary>
    ToLast,

    /// <summary><b>إلى أَصحابِ أَكبَرِ كَسرٍ مُهدَر</b> (طَريقَةُ
    /// هير/الباقي الأَكبَر)، والتَعادُلُ يُحسَمُ بِالفَهرَسِ الأَصغَر
    /// فَلا يَبقى تَعادُلٌ حَقيقيّ. وهي الوَحيدَةُ الَّتي <b>لا
    /// تَنحازُ لِلتَرتيب</b>: يَأخُذُ الزائِدَ مَن خَسِرَ أَكثَرَ
    /// بِالتَقريب، لا مَن كُتِبَ أَوَّلاً.</summary>
    LargestRemainder
}
