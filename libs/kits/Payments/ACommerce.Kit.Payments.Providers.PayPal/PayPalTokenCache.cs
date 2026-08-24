namespace ACommerce.Kit.Payments.Providers.PayPal;

/// <summary>
/// <para><b>رَمزُ OAuth2 مُخَزَّناً حَتّى قُبَيلَ انتِهائِه.</b> صِنفٌ
/// مُستَقِلٌّ عَن المُزَوِّد عَمداً، ولِسَبَبَينِ كِلاهُما مَقيس:</para>
///
/// <list type="number">
///   <item><b>عُمرُ التَخزينِ لَيسَ عُمرَ المُزَوِّد</b>:
///   <c>AddHttpClient&lt;T&gt;</c> يُسَجِّل <c>T</c> <b>عابِراً</b> —
///   نُسخَةٌ لِكُلّ نِداء. فَتَخزينٌ داخِلَ المُزَوِّدِ نَفسِه
///   <b>لا يُخَزِّن شَيئاً</b>: كُلُّ طَلَبٍ يَبدَأُ بِـ
///   <c>_token = null</c> ويَطلُب رَمزاً جَديداً. وهذا عَطَبٌ
///   <b>لا يُرى</b> — كُلُّ شَيءٍ يَعمَل، ويُنادى PayPal مَرَّتَينِ
///   بَدَلَ مَرَّة.</item>
///
///   <item><b>وما يُخَزَّنُ مُنفَصِلاً يُقاس مُنفَصِلاً</b>: هذا
///   الصِنفُ بِلا HTTP إطلاقاً — يَأخُذ <b>دالَّةَ الجَلب</b> ويَعُدُّ
///   كَم مَرَّةً نادَتها. فَـ«الرَمزُ يُخَزَّن» تَصير جُملَةً
///   <b>مَعدودَة</b> لا مَظنونَة (القاعِدَة ١٠).</item>
/// </list>
///
/// <para><b>والقُفلُ لَيسَ زينَة</b>: نِداءانِ مُتَزامِنانِ بِلا رَمزٍ
/// مُخَزَّنٍ يَطلُبانِ رَمزَينِ فَيُبطِل أَحَدُهُما الآخَرَ عِندَ
/// بَعضِ المُزَوِّدين. والفَحصُ يَقَع <b>مَرَّتَين</b> — قَبلَ القُفلِ
/// وبَعدَه — فَالفائِزُ يَجلِب والخاسِرُ يَقرَأُ ما جَلَب.</para>
/// </summary>
public sealed class PayPalTokenCache
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    /// <summary>كَم مَرَّةً نودِيَ المُزَوِّدُ فِعلاً — عَدّادٌ
    /// لِلاختِبار، وهُوَ الفَرقُ بَينَ «مُخَزَّن» و«يَبدو
    /// مُخَزَّناً».</summary>
    public int FetchCount { get; private set; }

    /// <summary>الرَمزُ المُخَزَّنُ إن كانَ صالِحاً الآن، وإلّا
    /// <c>null</c>. دالَّةٌ نَقِيَّةٌ بِالنِسبَةِ لِلوَقتِ المُمَرَّر
    /// — الوَقتُ لا يُقرَأُ مِن الساعَة هُنا.</summary>
    public string? Cached(DateTimeOffset now)
        => _token is not null && now < _expiresAt ? _token : null;

    /// <summary>
    /// يُعيد رَمزاً صالِحاً: المُخَزَّنَ إن بَقِيَ لَه أَكثَرُ مِن
    /// <see cref="PayPalEnvironment.TokenSafetySeconds"/>، وإلّا
    /// يُنادي <paramref name="fetch"/> مَرَّةً واحِدَةً ويُخَزِّن.
    /// </summary>
    /// <param name="fetch">جَلبُ الرَمز — يُعيد الرَمزَ وعُمرَه
    /// بِالثَواني كَما تَقولُهُما PayPal.</param>
    public async Task<string> GetAsync(
        Func<CancellationToken, Task<(string Token, int ExpiresInSeconds)>> fetch,
        DateTimeOffset now, CancellationToken ct = default)
    {
        if (Cached(now) is { } hit) return hit;

        await _gate.WaitAsync(ct);
        try
        {
            // الفَحصُ ثانِيَةً داخِلَ القُفل: الخاسِرُ في السِباقِ
            // يَقرَأُ ما جَلَبَه الفائِزُ ولا يَجلِبُ ثانِيَةً.
            if (Cached(now) is { } second) return second;

            var (token, expiresIn) = await fetch(ct);
            FetchCount++;

            // الهامِشُ يُطرَح، ولا يُسمَح لِعُمرٍ قَصيرٍ أَن يُعطِيَ
            // انتِهاءً في الماضي — رَمزٌ عُمرُه ثانِيَةٌ يُستَعمَل
            // مَرَّةً ثُمَّ يُجدَّد، ولا يُرمى فَوراً في حَلقَة.
            var lifetime = Math.Max(expiresIn - PayPalEnvironment.TokenSafetySeconds, 1);
            _token = token;
            _expiresAt = now.AddSeconds(lifetime);
            return token;
        }
        finally { _gate.Release(); }
    }

    /// <summary>يُسقِط المُخَزَّن — يُنادى عِندَ ‏401 فَيُجَدَّدُ
    /// الرَمزُ في النِداءِ التالي بَدَلَ أَن يَبقى فاسِداً حَتّى
    /// انتِهاءِ عُمرِه المُعلَن.</summary>
    public void Invalidate()
    {
        _token = null;
        _expiresAt = DateTimeOffset.MinValue;
    }
}
