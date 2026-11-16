namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>
/// <para><b>قَرار «إلى أَينَ يَجوز أَن نُحَوِّل»</b> — التَّعريف الوَحيد في
/// المُستَودَع. كُلّ نُقطَة تَقرَأ وِجهَةً <b>مِن الطَّلَب</b> (حَقل فورم
/// أَو استِعلام) ثُمَّ تُمَرِّرُها إلى <c>Results.Redirect</c> تَسأَل هذا
/// المَوضِع، ولا تَكتُب شَرطَها بِيَدِها.</para>
///
/// <para><b>الكلفة الَّتي كَتَبَت هذا المِلَفّ</b>: ‏<c>POST /lang/{lang}</c>
/// كانَ يَقرَأ <c>return</c> ويُحَوِّل إلَيه <b>بِلا فَحص أَصلاً</b> — ثُغرَة
/// تَحويل مَفتوح: صَفحَة خارِجِيَّة تُرسِل فورم إلى مَوقِعِنا فَيَقذِف
/// المُستَخدِم إلى مُضيف المُهاجِم، والعُنوان في شَريط المُتَصَفِّح عُنوانُنا
/// حَتّى لَحظَة القَذف. وإخفاء الزِرّ لا يُغلِق المَسار: الـ<c>POST</c>
/// يُصاغ بِـ<c>curl</c>. وفي مَوضِعَين آخَرَين كانَ الشَرط
/// <c>StartsWith("/")</c> وَحدَه — وهو <b>يُمَرِّر</b> <c>//evil.com</c>
/// و<c>/\evil.com</c>: كِلاهُما عِندَ المُتَصَفِّح عُنوان بِمُضيف آخَر.</para>
///
/// <para><b>والقاعِدَة مَكتوبَة بِالقَبول لا بِالمَنع</b>: لا نَعُدّ أَشكال
/// الهُروب المَعروفَة (فَالقائِمَة لا تَنتَهي)، بَل نَقبَل شَكلاً واحِداً
/// مَوصوفاً — مَسار مَحَلِّيّ نِسبيّ — وما عَداه يَسقُط إلى الوِجهَة
/// الآمِنَة الَّتي يُسَمّيها المُنادي.</para>
/// </summary>
public static class LocalRedirect
{
    /// <summary>
    /// <para>هَل هذه وِجهَة مَحَلِّيَّة يَجوز التَحويل إلَيها؟ الشُروط
    /// <b>مُجتَمِعَة</b>:</para>
    /// <list type="number">
    ///   <item>لَيسَت فارِغَة.</item>
    ///   <item>تَبدَأ بِـ<c>/</c> — فَلا مُخَطَّط ولا مُضيف.</item>
    ///   <item>لا تَبدَأ بِـ<c>//</c> ولا بِـ<c>/\</c> — كِلاهُما
    ///   «عُنوان بِلا مُخَطَّط» عِندَ المُتَصَفِّح، أَي مُضيف آخَر.</item>
    ///   <item>لا تَحمِل <c>\</c> إطلاقاً — المُتَصَفِّحات تُطَبِّعُها
    ///   إلى <c>/</c>، فَما بَعدَها قَد يَصير سُلطَة.</item>
    ///   <item>لا تَحمِل <c>:</c> — يَقطَع كُلّ مُحاوَلَة لِتَهريب
    ///   مُخَطَّط بَعدَ حَرف أَوَّل مَقبول.</item>
    ///   <item>لا تَحمِل مَحرَف تَحَكُّم ولا فَراغاً (‏‎&lt;= 0x20‎‏ أَو
    ///   ‎0x7F‎) — المُتَصَفِّح يَحذِفُها قَبل التَحليل فَيَقرَأ نَصّاً
    ///   غَير الَّذي فَحَصناه.</item>
    /// </list>
    /// </summary>
    public static bool IsLocal(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate)) return false;
        if (candidate[0] != '/') return false;
        if (candidate.Length > 1 && (candidate[1] == '/' || candidate[1] == '\\')) return false;

        foreach (var c in candidate)
        {
            if (c is '\\' or ':') return false;
            if (c <= ' ' || c == (char)0x7F) return false;
        }
        return true;
    }

    /// <summary>الوِجهَة إن كانَت مَحَلِّيَّة، وإلّا <paramref name="fallback"/>.
    /// <b>لا تَرمي ولا تُبَلِّغ خَطَأً</b>: طَلَبٌ بِوِجهَة خارِجِيَّة لا
    /// يَستَحِقّ صَفحَة خَطأ تَشرَح لَه ما فَحَصناه — يَستَحِقّ أَن يَنتَهي
    /// في الصَفحَة الَّتي كانَ سَيَنتَهي فيها لَو لَم يُرسِل شَيئاً.</summary>
    /// <remarks>‏(‏2026-11-11، الإغلاقُ الأَخيرُ لِـW13-4 · SEC-C2z-R3-3 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>والمَقبولُ يَخرُجُ بِحُروفٍ يَحمِلُها رَأس</b>
    /// (<see cref="Ascii"/>): <see cref="IsLocal"/> يَقبَلُ كُلَّ مَحرَفٍ لَيسَ تَحَكُّماً، و<c>Results.Redirect</c> يَكتُبُه في <c>Location</c> بِحَرفِه، وKestrel يَرفُضُ
    /// ما خارِجَ ASCII — فَـ<c>POST /lang/ar</c> بِـ<c>return=/ashare/مثال</c> كانَ <b>500</b> (<c>Invalid non-ASCII or control character in header: 0x0645</c>
    /// — مَقيسٌ عِندَ <c>ad0a770</c> عَلى الجَذرِ في الوَضعَين وعَلى مُضيفِ المَتجَر). الآن تَحويلٌ بِوِجهَةٍ مُرَمَّزَة؛ ووِجهَةُ ASCII بِحَرفِها
    /// (<c>SchemeRobustnessCharacterizationTests</c>).</remarks>
    public static string Resolve(string? candidate, string fallback) =>
        IsLocal(candidate) ? Ascii(candidate!) : fallback;

    /// <summary>
    /// <para><b>الوِجهَةُ بِحُروفٍ يَحمِلُها رَأسُ <c>Location</c></b> (‏2026-11-11، W13-4·C2f·2 — SEC-C2z-R3-3): كُلُّ مَحرَفٍ خارِجَ ASCII المَطبوعِ (ومَعَه المَسافَةُ
    /// ومَحارِفُ التَحَكُّم) يُرَمَّزُ بِبايتاتِه UTF-8 بِنِسبَةٍ مِئَوِيَّة، وما سِواه لا يُمَسّ — فَالمُرَمَّزُ سَلَفاً يَبقى بِحَرفِه، وASCII يَعودُ هُوَ هُوَ. قاعِدَةُ
    /// <c>TenantResolverMiddleware.AsciiOnly</c> لِلاستِعلامِ في تَحويلِ مَنطِقَةِ المَنَصَّة (‏<c>SEC-C2a-2</c>) — نُسخَتانِ في مَشروعَينِ لا يَرى أَحَدُهُما الآخَر.</para>
    /// <para>ويَسأَلُه <see cref="Resolve"/> وبُناةُ تَحويلِ ما بَعدَ الدُخولِ وبَيتِ الدَور (<c>MarketplaceTemplateExtensions.Home</c> و<c>RoleHome</c>
    /// و<c>PostLoginRouteAsync</c>): مَسارُ الدَورِ (<c>HomeRoute</c>) يُؤَلِّفُه المَتجَرُ، والمُصادِقُ يَقبَلُ <c>/مثال</c>.</para>
    /// </summary>
    public static string Ascii(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (url.All(c => c is > ' ' and < (char)0x7F)) return url;
        var sb = new System.Text.StringBuilder(url.Length * 3);
        Span<byte> bytes = stackalloc byte[4];
        foreach (var rune in url.EnumerateRunes())
        {
            if (rune.Value is > ' ' and < 0x7F) { sb.Append((char)rune.Value); continue; }
            var n = rune.EncodeToUtf8(bytes);
            for (var i = 0; i < n; i++) sb.Append('%').Append(bytes[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}
