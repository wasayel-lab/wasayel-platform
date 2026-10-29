using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ACommerce.Platform.Shared;
using Marten;
using Wolverine.Http;

namespace ACommerce.Kit.Auth.Server;

/// <summary>
/// Handlers الـ Auth يَستَهلِكون مُزَوِّدين عَبر <see cref="IOtpChannel"/> و
/// <see cref="IEmailOtpChannel"/> و <see cref="INafathChannel"/>. التَطبيق
/// يَختار التَنفيذ (Mock، Twilio، Unifonic، SMTP، Nafath الحَقيقيّ، …)
/// ويُسَجِّله في DI.
/// </summary>
public static class AuthHandlers
{
    public static readonly ConcurrentDictionary<string, AuthAttempt> Attempts = new();

    /// <summary>مُحاوَلَةُ دُخول. و<paramref name="WrongGuesses"/> (‏2026-10-24، تَدقيقُ الإطلاق lens-security-8) عَدَدُ الرُموزِ الخاطِئَةِ عَلَيها —
    /// تُمحى حينَ يَبلُغُ <see cref="MaxWrongGuessesPerCode"/>.</summary>
    public sealed record AuthAttempt(
        string Id, string TenantSlug, string Subject, string CodeHash,
        DateTime ExpiresAt, AuthKind Kind, int WrongGuesses = 0);

    /// <summary><b>الرَمزُ يَحتَمِلُ خَمسَةَ أَخطاءٍ ثُمَّ يُمحى</b> (‏2026-10-24، تَدقيقُ الإطلاق lens-security-8): كانَ الخاطِئُ يُرَدُّ والمُحاوَلَةُ
    /// باقِيَة، فَلا حَدَّ عَلى الرَمزِ نَفسِه إلّا حِصَّةُ التَحَقُّقِ (خَمسٌ في الدَقيقَة) طَوالَ عُمرِه — نَحوُ خَمسينَ تَخميناً. **والعَدَدُ هُوَ حَدُّ
    /// الدَقيقَةِ نَفسُه** (<see cref="MaxVerifyPerMinute"/>) لا رَقَمٌ جَديد: الرَمزُ الواحِدُ يَحتَمِلُ ما تَحتَمِلُه الدَقيقَة. ويُبَدِّلُه المالِكُ هُنا
    /// («أ» W8-2).</summary>
    public const int MaxWrongGuessesPerCode = MaxVerifyPerMinute;

    /// <remarks>‏(2026-10-28، قَرارُ المالِك د27) <c>AddEmail</c> و<c>AddPhone</c>: رَمزُ رَبطِ وَسيلَةٍ بِحِسابٍ قائِمٍ مِن «حِسابي» — نَوعٌ مُستَقِلٌّ عَن
    /// رَمزِ الدُخول، فَرَمزُ الدُخولِ لا يَربِطُ ورَمزُ الرَبطِ لا يُدخِل؛ ومَوضوعُه الحِسابُ والوَسيلَةُ (<c>ContactLink.Subject</c>).</remarks>
    /// <remarks>‏(‏2026-10-29، تَدقيقُ الموجَة ١١ A3) <c>RemoveContact</c>: رَمزُ حَذفِ الوَسيلَةِ المُضافَة — يُرسَلُ إلى الوَسيلَةِ <b>الباقِيَة</b>، ومَوضوعُه
    /// الحِسابُ والنَوعُ المَحذوف؛ يُلحَقُ آخِراً فَلا تَتَغَيَّرُ قيَمُ الخَمسَة.</remarks>
    public enum AuthKind { PhoneOtp, Nafath, EmailOtp, AddEmail, AddPhone, RemoveContact }

    // ─── Rate-limit OTP ───────────────────────────────────────────────────
    // كانَ بِلا حُدود — مُهاجِم يَستَطيع تَجريب ١٠ٰ٠٠٠ OTP في الثانِيَة على
    // نَفس الرَقم. الآن: ٥ مُحاوَلات/دَقيقَة لِكُلّ phone+tenant، و١٠
    // طَلَبات/ساعَة لِلإرسال. الـ counters تُمسَح تِلقائيّاً بَعد النافِذَة.
    //
    // ‏**شَرطُ النَقلِ — مَكتوبٌ هُنا بِقَرارِ المالِك د28 (‏2026-10-28)**: العَدّاداتُ في ذاكِرَةِ العَمَلِيَّةِ ما دامَ الموقِعُ يَعمَلُ **بِنُسخَةٍ
    // واحِدَة** — والمُهاجِمُ لا يَفرِضُ نَشراً يُصَفِّرُها. **ويَومَ يَعمَلُ بِأَكثَرَ مِن نُسخَةٍ تُنقَلُ إلى قاعِدَةِ البَيانات** (عَدّادٌ مُشتَرَكٌ بِمِفتاحِه
    // ونافِذَتِه)، وإلّا صارَ لِكُلِّ نُسخَةٍ حَدُّها فَيَتَضاعَفُ الحَدُّ بِعَدَدِها. ولا مَخزَنَ مُشتَرَكٌ قَبلَ ذلك (القاعِدَة ١: لا تَجريدَ قَبلَ مُستَهلِكِه).
    private sealed record RateBucket(int Count, DateTime ResetAt);
    private static readonly ConcurrentDictionary<string, RateBucket> _verifyRl = new();
    private static readonly ConcurrentDictionary<string, RateBucket> _requestRl = new();
    private const int MaxVerifyPerMinute = 5;
    private const int MaxRequestPerHour = 10;

    /// <summary>حِصَّةُ الإرسال — عَشرُ رَسائِلَ في الساعَة لِكُلّ
    /// <c>tenant|subject</c>. تُنادى مِن أَبوابٍ خارِجَ هذا المِلَفّ
    /// (بابُ الاستوديو) كَي تَبقى النافِذَةُ والعَدّادُ **واحِداً**.</summary>
    public static bool TryConsumeSendQuota(string key)
        => TryConsume(_requestRl, key, MaxRequestPerHour, TimeSpan.FromHours(1));

    private static readonly ConcurrentDictionary<string, RateBucket> _actionRl = new();

    /// <summary>
    /// <para><b>حِصَّةُ فِعلٍ يُكَلِّفُ نِداءً خارِجِيّاً ولا يُرسِلُ رَمزاً</b> (‏2026-10-13، مُراجَعَةُ المَوجَة 3b، G5 M1): بِالنافِذَةِ والحَدِّ
    /// نَفسَيهِما (عَشرٌ في السّاعَة — لا رَقَمَ جَديد) وبِالآلِيَّةِ نَفسِها، في عَدّادٍ <b>لا يَبلُغُه بابُ دُخول</b>. كانَ حَدُّ «Register» عِندَ OTO
    /// يَستَهلِكُ <see cref="TryConsumeSendQuota"/> بِمِفتاحِ «‏tenant|oto-register:{vendor}»، وبابُ الدُخولِ بِالجَوّالِ — بِلا جَلسَة — يَستَهلِكُها
    /// بِـ«‏tenant|{phone}» والجَوّالُ نَصٌّ حُرّ: فَعَشرُ طَلَباتٍ بِذلِكَ النَصِّ تُقفِلُ تَسجيلَ البائِعِ ساعَة. مُستَهلِكُه:
    /// <c>VendorShippingService.RegisterAsync</c>.</para>
    /// </summary>
    public static bool TryConsumeActionQuota(string key)
        => TryConsume(_actionRl, key, MaxRequestPerHour, TimeSpan.FromHours(1));

    /// <summary>حِصَّةُ التَحَقُّق — خَمسُ مُحاوَلاتٍ في الدَقيقَة.</summary>
    public static bool TryConsumeVerifyQuota(string key)
        => TryConsume(_verifyRl, key, MaxVerifyPerMinute, TimeSpan.FromMinutes(1));

    /// <summary>
    /// <para><b>مُشاهَدَةٌ واحِدَةٌ تُعَدُّ لِكُلِّ مِفتاحٍ في السّاعَة</b> (‏2026-10-24، تَدقيقُ الإطلاق lens-security-5): نُقطَةُ
    /// <c>/{slug}/listings/{id}/view</c> عامَّةٌ بِالتَصميمِ (مُشاهَدَةُ الزائِرِ أَغلَبُ المُشاهَدات) وكانَت تُلحِقُ حَدَثاً بِكُلِّ طَلَبٍ بِلا حَدّ —
    /// وكُلُّ حَدَثٍ يُبطِّئُ كُلَّ عَرضٍ لاحِقٍ يُعيدُ بِناءَ الإعلانِ مِن أَحداثِه. **الحَدُّ «واحِدَة» والنافِذَةُ ساعَةُ حِصَّةِ الإرسالِ نَفسُها** — لا
    /// رَقَمَ جَديد؛ وطولُها رَقَمُ المالِك يُبَدِّلُه هُنا («ب» W8-2). والمِفتاحُ يَبنيه القالِب (<c>ListingViews.QuotaKey</c>): الزائِرُ المُوَثَّقُ
    /// بِهُوِيَّتِه، والمَجهولُ بِعُنوانِه.</para>
    /// <para><b>‏(‏2026-10-28، قَرارُ المالِك د58؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) النافِذَةُ يَومٌ والمَفاتيحُ بِسَقف</b>: «ساعَة» أَعلاه صارَت
    /// <see cref="ViewWindow"/> (‏٢٤ ساعَة — «زائِرٌ فَريدٌ في اليَوم» كَما يَفهَمُه صاحِبُ الإعلانِ مِن كَلِمَةِ «مُشاهَدَة»)؛ ولِأَنَّ اليَومَ يُبقي المَفاتيحَ
    /// أَطوَلَ ‏٢٤ مَرَّة صارَ لِعَدَدِها سَقفٌ تِقنِيّ <see cref="MaxViewKeys"/> (‏<see cref="OncePerWindow"/>): يُكنَسُ المُنتَهي فَوراً عِندَ بُلوغِه، فَإن بَقِيَ
    /// مُمتَلِئاً لا تُعَدُّ المُشاهَدَةُ الجَديدَة — ولا خَطَأَ يَراه الزائِر — ويَكتُبُ القالِبُ تَحذيراً في السِجِلّ (<c>ListingViews.Counts</c>). والحِصَّةُ
    /// قَبلَ قِراءَةِ الإعلانِ كَما كانَت. وتَوصيفُ ما قَبلَه <c>cf37171</c>.</para>
    /// </summary>
    public static ViewCount ConsumeViewQuota(string key) => _views.Consume(key, DateTime.UtcNow);

    /// <summary>يُعيدُ مِفتاحَ مُشاهَدَةٍ استُهلِكَ لِغَيرِ شَيء — إعلانٌ لا وُجودَ لَه (‏2026-10-29، تَدقيقُ الموجَة ١١ I-SEC-3؛ تَوصيفُه <c>c74b2ac</c>):
    /// الحِصَّةُ قَبلَ القِراءَةِ كَما كانَت، فَمُعَرِّفٌ عَشوائِيٌّ كانَ يَحجِزُ مِفتاحاً يَوماً ويَملَأُ <see cref="MaxViewKeys"/>. <c>true</c> إن نُزِع.</summary>
    public static bool ForgetView(string key) => _views.Forget(key);

    /// <summary>نافِذَةُ المُشاهَدَة — يَومٌ (قَرارُ المالِك د58 «ب»).</summary>
    public static readonly TimeSpan ViewWindow = TimeSpan.FromHours(24);

    /// <summary>سَقفُ مَفاتيحِ عَدّادِ المُشاهَدَةِ في الذاكِرَة — مِئَةُ أَلف (قَرارُ المالِك د58 «أ، مَعَ ج»): عَشَراتُ الميغابايتاتِ عَلى الأَكثَر.</summary>
    public const int MaxViewKeys = 100_000;

    private static readonly OncePerWindow _views = new(ViewWindow, MaxViewKeys);

    /// <summary>جَوابُ حِصَّةِ المُشاهَدَة: <c>Counted</c> تُعَدّ؛ <c>Repeat</c> عُدَّت لِلمِفتاحِ في النافِذَة؛ <c>Full</c> العَدّادُ مُمتَلِئٌ ولا مُنتَهِيَ يُكنَس
    /// قَبلَ أَقرَبِ انقِضاء؛ <c>FullAfterSweep</c> كُنِسَ الآنَ وبَقِيَ مُمتَلِئاً — وهُوَ وَحدَه ما يُكتَبُ لَه تَحذير، فَلا يَفيضُ السِجِلُّ بِسَطرٍ لِكُلِّ طَلَب.</summary>
    public enum ViewCount { Counted, Repeat, Full, FullAfterSweep }

    /// <summary>
    /// <para><b>«مَرَّةٌ لِكُلِّ مِفتاحٍ في النافِذَة» بِسَقفٍ لِعَدَدِ المَفاتيح</b> (‏2026-10-28، قَرارُ المالِك د58) — عَدّادُ المُشاهَدَةِ وَحدَه
    /// (‏<see cref="ConsumeViewQuota"/>)، بِآلِيَّةِ <see cref="TryConsume"/> نَفسِها. والساعَةُ مُعطاةٌ لا مَقروءَة فَيُقاسُ الكَنسُ بِلا انتِظار.</para>
    /// <para><b>والكَنسُ عِندَ الامتِلاءِ لا يَتَكَرَّرُ قَبلَ أَقرَبِ انقِضاء</b>: كَنسٌ لا يَجِدُ مُنتَهِياً يَحفَظُ أَقرَبَ <c>ResetAt</c> باقٍ، ولا يُعادُ المَسحُ
    /// (‏<c>O(MaxViewKeys)</c>) قَبلَه — فَلا يَصيرُ كُلُّ طَلَبٍ جَديدٍ تَحتَ الامتِلاءِ مَسحاً كامِلاً. لا رَقَمَ جَديد: الحَدُّ يُشتَقُّ مِن المَفاتيحِ نَفسِها.</para>
    /// <para><b>شَرطُ الاستِخراجِ (القاعِدَة ١)</b>: حِصَّةٌ ثانِيَةٌ في وَقتِ التَشغيلِ تَحتاجُ سَقفَ مَفاتيح — اليَومَ المُشاهَدَةُ وَحدَها؛ والحِصَصُ الأُخرى
    /// مَفاتيحُها أَرقامٌ يُحِدُّها سَقفُ المَنَصَّةِ ونافِذَتُها ساعَةٌ أَو دَقيقَة.</para>
    /// </summary>
    public sealed class OncePerWindow(TimeSpan window, int maxKeys)
    {
        private readonly ConcurrentDictionary<string, RateBucket> _rl = new();
        private readonly object _sweep = new();
        private DateTime _nextExpiry = DateTime.MinValue;

        /// <summary>عَدَدُ المَفاتيحِ الآن.</summary>
        public int Keys => _rl.Count;

        /// <summary>يَنزِعُ المِفتاحَ فَيُعَدُّ مِن جَديد — لِما استُهلِكَ لِغَيرِ شَيء (‏2026-10-29، I-SEC-3). <c>false</c> لِمِفتاحٍ غائِب.</summary>
        public bool Forget(string key) => _rl.TryRemove(key, out _);

        /// <summary>يَعُدُّ المِفتاحَ مَرَّةً في النافِذَة؛ ومِفتاحٌ جَديدٌ والعَدّادُ مُمتَلِئ يُكنَسُ لَه المُنتَهي أَوَّلاً.</summary>
        public ViewCount Consume(string key, DateTime now)
        {
            if (!_rl.ContainsKey(key) && _rl.Count >= maxKeys)
            {
                var swept = false;
                lock (_sweep)
                {
                    if (_rl.Count >= maxKeys && now >= _nextExpiry)
                    {
                        var next = DateTime.MaxValue;
                        foreach (var (k, bucket) in _rl)
                        {
                            if (bucket.ResetAt < now) _rl.TryRemove(new KeyValuePair<string, RateBucket>(k, bucket));
                            else if (bucket.ResetAt < next) next = bucket.ResetAt;
                        }
                        _nextExpiry = next;
                        swept = true;
                    }
                }
                if (_rl.Count >= maxKeys) return swept ? ViewCount.FullAfterSweep : ViewCount.Full;
            }
            return TryConsume(_rl, key, 1, window, now) ? ViewCount.Counted : ViewCount.Repeat;
        }
    }

    // ‏(‏2026-10-24، تَدقيقُ الإطلاق lens-security-5) عَدّادٌ انقَضَت نافِذَتُه يُطرَد — مَرَّةً في النافِذَةِ لِكُلِّ قاموس. كانَ كُلُّ مِفتاحٍ يَبقى
    // إلى إعادَةِ التَشغيل، ومِفتاحُ المُشاهَدَةِ لِكُلِّ (زائِرٍ، إعلان). والدَلالَةُ لا تَتَغَيَّر: المُنقَضي يُعامَلُ عَدّاداً جَديداً في الحالَين.
    private static readonly ConcurrentDictionary<object, DateTime> _sweptAt = new();

    private static void SweepExpired(ConcurrentDictionary<string, RateBucket> rl, DateTime now, TimeSpan window)
    {
        var last = _sweptAt.GetOrAdd(rl, now);
        if (now - last < window || !_sweptAt.TryUpdate(rl, now, last)) return;
        foreach (var (key, bucket) in rl)
            if (bucket.ResetAt < now) rl.TryRemove(new KeyValuePair<string, RateBucket>(key, bucket));
    }

    // ─── سَقفُ المَنَصَّة — رَقَمُ المالِك، ومُطفَأٌ حَتّى يُضبَط (‏2026-10-24، تَدقيقُ الإطلاق lens-security-3) ──
    //
    // الحِصَّةُ أَعلاه تَحُدُّ **الرَقَمَ الواحِد**؛ ومَن يُدَوِّرُ الأَرقامَ لا يَحُدُّه شَيء — والرَسائِلُ عَلى حِسابِ المَنَصَّة. فَهذا سَقفٌ
    // واحِدٌ لِكُلِّ إرسالٍ (جَوّالٌ وبَريدٌ وبَدءُ نَفاذٍ، في كُلِّ مَتجَرٍ وفي الاستوديو) في السّاعَة، بِالآلِيَّةِ نَفسِها. **ورَقَمُه لا
    // يُخترَع** (القاعِدَة ١٦): بِلا `ACOMMERCE_OTP_SENDS_PER_HOUR` — وهو الحالُ اليَوم — لا سَقف، كَما كان. ويُنقَضُ بِحَذفِ المُتَغَيِّر.
    // وحَدٌّ لِكُلِّ IP لَم يُبنَ: مَصدَرُ العُنوانِ خَلفَ الـSpace غَيرُ مَقيس («ب» W8-2).

    /// <summary>اسمُ المُتَغَيِّرِ الَّذي يَضبِطُ سَقفَ المَنَصَّة — عَدَدٌ صَحيحٌ مُوجَب.</summary>
    public const string GlobalSendsPerHourVar = "ACOMMERCE_OTP_SENDS_PER_HOUR";

    /// <summary>السَقفُ كَما يُقرَأُ عِندَ الإقلاع — <c>null</c> = لا سَقف.</summary>
    public static int? GlobalSendsPerHour { get; } = ParseLimit(Environment.GetEnvironmentVariable(GlobalSendsPerHourVar));

    /// <summary>عَدَدٌ صَحيحٌ مُوجَبٌ وإلّا <c>null</c> — الفارِغُ والصِفرُ والسالِبُ والنَصُّ لا يَصيرُ سَقفاً يُغلِقُ كُلَّ دُخول.</summary>
    public static int? ParseLimit(string? raw)
        => int.TryParse(raw?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
           && n > 0 ? n : null;

    private static readonly ConcurrentDictionary<string, RateBucket> _globalRl = new();

    /// <summary>يَستَهلِكُ مِن سَقفِ المَنَصَّة إن ضُبِط؛ و<c>null</c> يَمُرُّ دائِماً.</summary>
    public static bool TryConsumeGlobalSendQuota(int? perHour)
        => perHour is not int max || TryConsume(_globalRl, "*", max, TimeSpan.FromHours(1));

    /// <summary>
    /// <para><b>وِجهَةُ الرَمزِ ومِفتاحُ حِصَّتِه لِلجَوّال</b> (‏2026-10-24، تَدقيقُ الإطلاق lens-security-3): رَقَمٌ سُعودِيٌّ (‏<c>05…</c> أَو
    /// <c>5…</c> أَو <c>966…</c> أَو <c>00966…</c> أَو <c>+966…</c>، بِمَسافاتٍ أَو شَرطاتٍ أَو بِدونِها) يَصيرُ <c>+9665XXXXXXXX</c>؛ وما سِواه
    /// يَبقى كَما كُتِب (رَقَمٌ خارِجَ السُعودِيَّة، أَو نَصٌّ لَيسَ رَقَماً). كانَت الحِصَّةُ تَعُدُّ <b>الكِتابَةَ</b> لا الرَقَم — ثَلاثُ كِتاباتٍ
    /// ثَلاثونَ رِسالَة — والرَمزُ يُرسَلُ إلى النَصِّ كَما كُتِب، و<c>TwilioSmsChannel</c> يُلحِقُ <c>+</c> فَـ<c>0551234567</c> ⇒ <c>+0551234567</c>.</para>
    ///
    /// <para><b>ولا تَمَسُّ الهُوِيَّة</b>: مَوضوعُ المُحاوَلَةِ ورَقَمُ المُستَخدِمِ المُخَزَّنُ يَبقَيانِ النَصَّ كَما كُتِب — حِساباتٌ قائِمَةٌ مُخَزَّنَةٌ
    /// بِـ<c>05…</c> لا تَنقَسِمُ إلى حِسابَين، و<c>StudioAuth.NormalizeSubject</c> يَبقى قَصّاً (مُطابِقاً لِمَنحِ المُشرِف). تَوحيدُ الهُوِيَّةِ
    /// تَرحيلُ بَياناتٍ («ب» W8-2).</para>
    /// </summary>
    /// <remarks>‏(‏2026-10-28، قَرارُ المالِك د57 «أ»؛ ‏ADR-044 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>والمَنطِقُ صارَ <see cref="PhoneNumber.Canonical"/>
    /// بِحَرفِه</b> — الصيغَةُ نَفسُها صارَت هُوِيَّةً أَيضاً: يُكتَبُ بِها الحِسابُ الجَديد، ويُبحَثُ بِها بَعدَ الكِتابَة. و«لا تَمَسُّ الهُوِيَّة» أَعلاه تاريخٌ.</remarks>
    public static string PhoneDestination(string? typed) => PhoneNumber.Canonical(typed);

    /// <summary>هَل تُسمَح المُحاوَلَة؟ يُحَدِّث العَدّاد ذَرّيّاً.</summary>
    /// <remarks>‏(‏2026-10-28، د58) <paramref name="at"/> ساعَةُ <see cref="OncePerWindow"/> المُعطاة؛ وبِلاها الآنَ كَما كان.</remarks>
    private static bool TryConsume(ConcurrentDictionary<string, RateBucket> rl,
        string key, int max, TimeSpan window, DateTime? at = null)
    {
        var now = at ?? DateTime.UtcNow;
        SweepExpired(rl, now, window);
        while (true)
        {
            var current = rl.GetOrAdd(key, _ => new RateBucket(0, now.Add(window)));
            // النافِذَة انتَهَت — أَعِد بَدء العَدّاد.
            if (current.ResetAt < now)
            {
                var fresh = new RateBucket(1, now.Add(window));
                if (rl.TryUpdate(key, fresh, current)) return true;
                continue;
            }
            if (current.Count >= max) return false;
            var next = current with { Count = current.Count + 1 };
            if (rl.TryUpdate(key, next, current)) return true;
        }
    }

    // ─── آليَّةُ الرَمز — مَوضِعٌ واحِدٌ لِكُلّ بابٍ في المُستَودَع ──────
    //
    // **لِماذا استُخرِجَت (‏2026-08-23)**: بابُ جَلسَةِ الاستوديو كانَ
    // يُقارِن الرَمزَ بِثابِتٍ `"123456"` بِلا شَرطِ بيئَة — أَي أَنَّه
    // **لَم يَكُن يَمُرّ بِهذا المِلَفّ إطلاقاً**. ولَو نُسِخَت الآليَّةُ
    // إلَيه لَصارَ لِلتَوليدِ والتَجزئَةِ والمُهلَةِ **تَعريفانِ
    // يَنجَرِفان** — وهو عَينُ ما تَمنَعُه القاعِدَة ٨. فَالدالّاتُ
    // الثَلاثُ أَدناه هي ما كانَ مَنثوراً في أَجسامِ المُعالِجات، ونُقِلَ
    // بِلا تَغييرِ حَرف: نَفسُ المَدى (`100000..999999`)، ونَفسُ التَجزئَة
    // (`SHA-256` سُداسيّ)، ونَفسُ العَشرِ دَقائِق. والمُعالِجاتُ أَدناه
    // تُناديها الآنَ، فَالمَوضِعُ واحِدٌ **بِالقياس** لا بِالنِيَّة.

    /// <summary>عُمرُ الرَمز الافتِراضيّ — عَشرُ دَقائِق، كَما كانَ.</summary>
    public static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(10);

    /// <summary>الرَمز: تَلميحُ المُحاكي إن وُجِد، وإلّا **عَشوائيٌّ**
    /// سُداسيّ. و`DevHintCode` لا يَكونُ غَيرَ `null` إلّا لِقَناةٍ
    /// مُحاكيَة، وتِلكَ لا تُسَجَّل خارِجَ التَطوير (‏<c>AuthChannelSelection</c>
    /// وحارِسُ الإقلاع). فَالثابِتُ لا سَبيلَ لَه إلى الإنتاج.</summary>
    /// <remarks>‏(‏2026-10-24، تَدقيقُ الإطلاق lens-security-8) <b>مِن مُوَلِّدٍ تَشفيرِيّ</b>: كانَ <c>Random.Shared.Next(100000, 999999)</c> — غَيرَ
    /// تَشفيرِيٍّ، وحَدُّه الأَعلى حَصرِيٌّ فَـ<c>999999</c> لا يُسحَبُ أَبَداً. صارَ <c>RandomNumberGenerator.GetInt32(100000, 1000000)</c>: المَدى
    /// السُداسيُّ كُلُّه ([100000، 999999]) بِمُوَلِّدِ النِظام.</remarks>
    public static string NewCode(string? devHintCode)
        => devHintCode ?? RandomNumberGenerator.GetInt32(100000, 1000000).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>يُسَجِّل مُحاوَلَةً بِرَمزٍ **مُجَزَّأ** ومُهلَة. يُعيد
    /// مُعَرِّفَ المُحاوَلَة.</summary>
    public static string IssueAttempt(
        string tenantSlug, string subject, string code, AuthKind kind,
        TimeSpan? lifetime = null)
    {
        EvictExpiredAttempts();
        // ‏(‏2026-10-24، تَدقيقُ الإطلاق lens-security-8) **الرَمزُ الجَديدُ يَحِلُّ مَحَلَّ سابِقِه** لِلمَوضوعِ نَفسِه: كانَت «أَعِد الإرسال» تُبقي
        // مُحاوَلَتَينِ حَيَّتَين، و`ConsumeAttempt` يَأخُذُ أَوَّلَ ما يَجِدُ بِتَرتيبٍ لا يَحفَظُ الإدراج — فَيُقارَنُ الجَديدُ بِتَجزِئَةِ القَديم.
        foreach (var (id, previous) in Attempts)
            if (previous.TenantSlug == tenantSlug && previous.Subject == subject && previous.Kind == kind)
                Attempts.TryRemove(id, out _);
        var attemptId = Guid.NewGuid().ToString("N");
        Attempts[attemptId] = new AuthAttempt(
            attemptId, tenantSlug, subject, Hash(code),
            DateTime.UtcNow.Add(lifetime ?? OtpLifetime), kind);
        return attemptId;
    }

    /// <summary><b>المُنتَهِيَةُ تُطرَد</b> (‏2026-10-24، تَدقيقُ الإطلاق lens-security-3): كانَ <see cref="Attempts"/> لا يُطرَدُ مِنه إلّا ما
    /// استُهلِك، فَكُلُّ رَمزٍ طُلِبَ ولَم يُستَعمَل يَبقى في الذاكِرَةِ إلى إعادَةِ التَشغيل — ومَن يُدَوِّرُ الأَرقامَ يَملَؤُها. يُنادى قَبلَ كُلِّ
    /// إصدار (رَمزٌ أَو بَدءُ نَفاذ)، فَالحَجمُ مَحدودٌ بِما يُصدَرُ في مُهلَةٍ واحِدَة.</summary>
    public static void EvictExpiredAttempts()
    {
        var now = DateTime.UtcNow;
        foreach (var (id, attempt) in Attempts)
            if (attempt.ExpiresAt < now) Attempts.TryRemove(id, out _);
    }

    /// <summary>يَستَهلِك المُحاوَلَة: مُطابَقَةُ التَجزئَة داخِلَ المُهلَة
    /// **مَرَّةً واحِدَة**. رَمزٌ خاطِئ أَو مُنتَهٍ أَو غائِبٌ ⇒
    /// <c>false</c> بِلا تَمييزٍ بَينَها في الرَدّ.</summary>
    public static bool ConsumeAttempt(
        string tenantSlug, string subject, string code, AuthKind kind)
    {
        // ‏(‏2026-10-25، مُراجَعَةُ تَيّارِ W8-2 الجَولَة ١؛ تَوصيفُه `07d8be6` — ولا يُعادُ حَرفٌ فَوقَ ما سَبَق) **كُلُّ كِتابَةٍ تُقارِنُ ما قَرَأَته
        // وتُعيدُ القِراءَةَ إن سَبَقَها غَيرُها** — نَسَقُ `TryConsume` أَعلاه: كانَ العَدُّ `TryUpdate` يُهمَلُ جَوابُه فَيَضيعُ تَخمينٌ مُتَزامِن،
        // والمَحوُ `TryRemove(id)` يُعيدُ `true` أَيّاً كانَ جَوابُه فَيَفتَحُ رَمزٌ واحِدٌ لِنِداءَينِ مُتَزامِنَين. والمَحوُ الآنَ بِالزَوجِ المَقروء،
        // فَمَن مُحِيَت مُحاوَلَتُه بَعدَ قِراءَتِه يَقرَأُ ثانِيَةً فَلا يَجِدُها.
        while (true)
        {
            var attempt = Attempts.Values
                .FirstOrDefault(a => a.TenantSlug == tenantSlug
                                  && a.Subject == subject
                                  && a.Kind == kind);
            if (attempt is null || attempt.ExpiresAt < DateTime.UtcNow) return false;
            if (Hash(code) != attempt.CodeHash)
            {
                // ‏(‏2026-10-24، تَدقيقُ الإطلاق lens-security-8) الخاطِئُ يُعَدّ، وبُلوغُ الحَدِّ يَمحو الرَمز — فَالتالي يَحتاجُ رَمزاً جَديداً (وحِصَّةَ إرسال).
                var counted = attempt with { WrongGuesses = attempt.WrongGuesses + 1 };
                var written = counted.WrongGuesses >= MaxWrongGuessesPerCode
                    ? Attempts.TryRemove(KeyValuePair.Create(attempt.Id, attempt))
                    : Attempts.TryUpdate(attempt.Id, counted, attempt);
                if (written) return false;
                continue;
            }
            if (Attempts.TryRemove(KeyValuePair.Create(attempt.Id, attempt))) return true;
        }
    }

    // ─── المُحاوَلَةُ تُسَمّى بِمُعَرِّفِها لا بِمَوضوعِها (‏2026-10-24، تَدقيقُ الإطلاق lens-security-13) ──
    //
    // كانَت تَحويلاتُ الدُخولِ تَحمِلُ الجَوّالَ والبَريدَ والهُوِيَّةَ في الاستِعلامِ لِأَنّ صَفحَةَ الرَمزِ لا تَعرِفُ مَوضوعَها إلّا مِنه. فَالرابِطُ
    // يَحمِلُ مُعَرِّفَ المُحاوَلَةِ (‏<see cref="Guid"/> عَشوائِيٌّ، يَموتُ بِمَوتِها) والصَفحَةُ تَسأَلُ عَنه هُنا.

    /// <summary>مَوضوعُ مُحاوَلَةٍ <b>حَيَّةٍ</b> بِمُعَرِّفِها في هذا المَتجَرِ ولِهذا النَوع — وإلّا <c>null</c> (مُعَرِّفٌ غائِبٌ أَو مَجهولٌ أَو مُنتَهٍ
    /// أَو مِن مَتجَرٍ آخَرَ أَو نَوعٍ آخَر).</summary>
    public static string? SubjectOf(string tenantSlug, string? attemptId, AuthKind kind)
        => attemptId is not null && Attempts.TryGetValue(attemptId, out var a)
           && a.TenantSlug == tenantSlug && a.Kind == kind && a.ExpiresAt > DateTime.UtcNow
            ? a.Subject : null;

    /// <summary>مُعَرِّفُ المُحاوَلَةِ الحَيَّةِ لِلمَوضوعِ في هذا المَتجَرِ ولِهذا النَوع — واحِدَةٌ عَلى الأَكثَر، لِأَنّ <see cref="IssueAttempt"/> يَمحو
    /// سابِقَتَها — وإلّا <c>null</c> (لَم تُصدَر، أَو استُهلِكَت، أَو أَحرَقَتها الأَخطاء، أَو انتَهَت).</summary>
    public static string? LiveAttemptOf(string tenantSlug, string subject, AuthKind kind)
        => Attempts.Values.FirstOrDefault(a => a.TenantSlug == tenantSlug && a.Subject == subject && a.Kind == kind
                                               && a.ExpiresAt > DateTime.UtcNow)?.Id;

    /// <summary>سِرّ تَوقيع الـ token — يُحَمَّل مِن ENV عِندَ بَدء التَّطبيق.
    /// لَو غابَ نَستَخدِم سِرّ التَطويرِ الافتِراضيّ؛ **وخارِجَ Development
    /// يَرمي حارِسُ الإقلاعِ** (<c>AuthTokenSecretPolicy.AssertSafeOutsideDevelopment</c>
    /// في <c>Program.cs</c>) إن بَقِيَ الافتِراضيُّ أَو فَرَغَ أَو قَصُرَ عَن ٣٢.
    /// **والاسمُ والحَرفِيَّةُ والحَدُّ في مَوضِعٍ واحِدٍ هُناك لا هُنا** (‏2026-10-11):
    /// كانَ هذا السَطرُ وَحدَه يَحمِلُها بِلا حارِسٍ ولا وَثيقَةٍ ولا فَحص — فَكُلُّ
    /// كوكي في الـSpace بِلا المُتَغَيِّرِ كانَ قابِلاً لِلتَزوير (‏P0-2).
    /// <b>والضابِطُ خاصٌّ مُنذُ ‏2026-10-11 (صِغارُ مُراجَعَةِ الموجَةِ ١)</b>: الحارِسُ
    /// يَقيسُ القيمَةَ مَرَّةً عِندَ الإقلاع، وضابِطٌ عامٌّ كانَ يَسمَحُ بِتَبديلِها
    /// بَعدَه بِلا فَحص — وإسنادُه خارِجَ هذا المِلَفِّ صِفرٌ مَقيس (‏`grep`)، فَلا
    /// مُستَهلِكَ يُكسَر. مُثَبَّتٌ في <c>AuthTokenSecretPolicyTests</c>.</summary>
    public static string TokenSecret { get; private set; } =
        Environment.GetEnvironmentVariable(AuthTokenSecretPolicy.EnvVarName)
        ?? AuthTokenSecretPolicy.DevelopmentFallback;

    // ── رَمزُ رَبطِ وَسيلَةٍ بِحِسابٍ قائِم (‏2026-10-28، قَرارُ المالِك د27) ─────
    /// <summary>
    /// <para>يُرسِلُ رَمزَ «أَضِف بَريدَك/جَوّالَك» بِآلِيَّةِ الدُخولِ نَفسِها حَرفاً — حِصَّةُ الإرسالِ لِلوِجهَة (<c>{slug}|{sendTo}</c>، المِفتاحُ نَفسُه
    /// الَّذي يَعُدُّه الدُخول) ثُمَّ سَقفُ المَنَصَّة، والرَمزُ مِن <see cref="NewCode"/>، والمُحاوَلَةُ بِـ<see cref="IssueAttempt"/>، والإرسالُ خَلفَ
    /// <c>OtpSendGuard</c> — ويُعيدُ مُعَرِّفَ المُحاوَلَة. يَرمي <see cref="InvalidOperationException"/> عِندَ الحِصَّة، كَإخوَتِه.</para>
    /// </summary>
    public static async Task<string> RequestContactCodeAsync(
        string tenantSlug, AuthKind kind, string subject, string sendTo, string? devHintCode,
        Func<string, string, CancellationToken, Task> send, CancellationToken ct)
    {
        if (kind is not (AuthKind.AddEmail or AuthKind.AddPhone or AuthKind.RemoveContact))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "رَمزُ الرَبطِ لِـAddEmail أَو AddPhone أَو RemoveContact وَحدَها.");
        if (!TryConsumeSendQuota($"{tenantSlug}|{sendTo}") || !TryConsumeGlobalSendQuota(GlobalSendsPerHour))
            throw new InvalidOperationException("rate_limited: try again in an hour");
        var code = NewCode(devHintCode);
        var attemptId = IssueAttempt(tenantSlug, subject, code, kind);
        await OtpSendGuard.SendWithinAsync(token => send(sendTo, code, token), ct);
        return attemptId;
    }

    // ── Phone OTP ─────────────────────────────────────────────────────
    // ملاحَظَة: لا [WolverinePost] هُنا. القالِب (MarketplaceTemplate) يُسَجِّل
    // المَسار عَبر MapPost ويَستَدعي هذِه الدالَّة، ثُمَّ يَكتُب الـ cookie
    // ويُعيد التَّوجيه. تَسجيل Wolverine لِنَفس المَسار كانَ يَلتَقِط طَلَبات
    // JSON ويُرجِع AuthResult بِلا cookie — فَكَسَرَ تَسجيل الدُّخول بِالكامِل.
    public static async Task<OtpRequestResult> RequestPhoneOtpHandler(
        RequestPhoneOtp cmd, ITenantContext tenantCtx,
        IOtpChannel channel, CancellationToken ct)
    {
        if (!tenantCtx.IsResolved) throw new InvalidOperationException("tenant_required");
        // حِماية مِن سپام الإرسال (تَكلِفَة SMS فِعلِيَّة). المِفتاحُ الرَقَمُ لا الكِتابَة (`PhoneDestination`)، ثُمَّ سَقفُ المَنَصَّة.
        var to = PhoneDestination(cmd.Phone);
        var rlKey = $"{tenantCtx.Slug}|{to}";
        if (!TryConsumeSendQuota(rlKey) || !TryConsumeGlobalSendQuota(GlobalSendsPerHour))
            throw new InvalidOperationException("rate_limited: try again in an hour");

        var code = NewCode(channel.DevHintCode);
        var attemptId = IssueAttempt(tenantCtx.Slug, cmd.Phone, code, AuthKind.PhoneOtp);
        // الحارِسُ في الأُنبوبِ لا في كُلِّ مُزَوِّد — راجِع `OtpSendGuard`.
        await OtpSendGuard.SendWithinAsync(
            token => channel.SendOtpAsync(to, code, token), ct);
        return new OtpRequestResult(
            AttemptId: attemptId,
            DisplayCode: channel.DevHintCode ?? "",
            Hint: channel.DevHintCode is null
                ? $"أَرسَلنا الكود إلى {cmd.Phone}"
                : $"وَضع التَطوير ({channel.ChannelName}) — الكود: {code}");
    }

    // لا [WolverinePost] — راجِع التَّعليق على RequestPhoneOtpHandler.
    public static async Task<AuthResult?> VerifyPhoneOtpHandler(
        VerifyPhoneOtp cmd, ITenantContext tenantCtx, IDocumentStore store)
    {
        if (!tenantCtx.IsResolved) return null;
        // حِماية مِن brute-force OTP (٥/دَقيقَة لِكُلّ phone+tenant).
        var rlKey = $"{tenantCtx.Slug}|{cmd.Phone}";
        if (!TryConsumeVerifyQuota(rlKey)) return null;
        if (!ConsumeAttempt(tenantCtx.Slug, cmd.Phone, cmd.Code, AuthKind.PhoneOtp)) return null;
        return await GetOrCreateUserAsync(store, tenantCtx.Slug, cmd.Phone, nationalId: null);
    }

    // ── Email OTP ─────────────────────────────────────────────────────
    // نَفس آليَّة الهاتِف حَرفيّاً: نَفس تَوليد الرَمز، نَفس التَجزئَة،
    // نَفس قاموس <see cref="Attempts"/>، نَفس نَوافِذ حَدّ المُعَدَّل.
    // المُختَلِف: العُنوان يُطَبَّع (قَصّ + تَصغير) قَبل كُلّ شَيء لِيَستَحيل
    // أَن يَطلُب المُستَخدِم بِـ Ali@X.com ويَفشَل تَحَقُّقه بِـ ali@x.com.
    // لا [WolverinePost] — راجِع التَّعليق على RequestPhoneOtpHandler.
    public static async Task<OtpRequestResult> RequestEmailOtpHandler(
        RequestEmailOtp cmd, ITenantContext tenantCtx,
        IEmailOtpChannel channel, CancellationToken ct)
    {
        if (!tenantCtx.IsResolved) throw new InvalidOperationException("tenant_required");
        var email = EmailAddress.Normalize(cmd.Email);
        if (!EmailAddress.IsValid(email))
            throw new InvalidOperationException("email_invalid");
        // حِماية مِن سپام الإرسال (سُمعَة نِطاق الإرسال تَحتَرِق بِالسپام)، ثُمَّ سَقفُ المَنَصَّة.
        var rlKey = $"{tenantCtx.Slug}|{email}";
        if (!TryConsumeSendQuota(rlKey) || !TryConsumeGlobalSendQuota(GlobalSendsPerHour))
            throw new InvalidOperationException("rate_limited: try again in an hour");

        var code = NewCode(channel.DevHintCode);
        var attemptId = IssueAttempt(tenantCtx.Slug, email, code, AuthKind.EmailOtp);
        // نَفسُ الحارِس — والعِلَّةُ المَقيسَةُ كانَت هُنا بِالضَبط:
        // ‏`POST /{slug}/auth/email/login` عَلِقَ ‏90+ ثانِيَة بِلا رَدّ.
        await OtpSendGuard.SendWithinAsync(
            token => channel.SendOtpAsync(email, code, token), ct);
        return new OtpRequestResult(
            AttemptId: attemptId,
            DisplayCode: channel.DevHintCode ?? "",
            Hint: channel.DevHintCode is null
                ? $"أَرسَلنا الكود إلى {email}"
                : $"وَضع التَطوير ({channel.ChannelName}) — الكود: {code}");
    }

    // لا [WolverinePost] — راجِع التَّعليق على RequestPhoneOtpHandler.
    public static async Task<AuthResult?> VerifyEmailOtpHandler(
        VerifyEmailOtp cmd, ITenantContext tenantCtx, IDocumentStore store)
    {
        if (!tenantCtx.IsResolved) return null;
        var email = EmailAddress.Normalize(cmd.Email);
        // حِماية مِن brute-force OTP (٥/دَقيقَة لِكُلّ email+tenant).
        var rlKey = $"{tenantCtx.Slug}|{email}";
        if (!TryConsumeVerifyQuota(rlKey)) return null;
        if (!ConsumeAttempt(tenantCtx.Slug, email, cmd.Code, AuthKind.EmailOtp)) return null;
        return await GetOrCreateUserAsync(store, tenantCtx.Slug,
            phone: "", nationalId: null, email: email);
    }

    // ── Nafath ────────────────────────────────────────────────────────
    // لا [WolverinePost] — (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-2 الجَولَة ١؛ تَوصيفُه `4618eec`؛ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق) كانَ هُنا
    // `[WolverinePost("/{slug}/auth/nafath/request")]` فَجِسمُ النُقطَةِ هذِه الدالَّةُ الَّتي تَرمي. ومَقيسٌ حَيّاً عَلى قاعِدَةٍ مَحَلِّيَّة: أَحَدَ عَشَرَ
    // طَلَبَ JSON لِـ`theme-demo` كُلُّها ‏500 بِـ`tenant_required` — النُقطَةُ لا تَرى المُستَأجِرَ أَصلاً، فَلَم تَعمَل لِأَحَدٍ قَطّ، ولا مُنادِيَ لَها في
    // المُستَودَع (لا razor ولا js ولا سكربت إلّا مِجَسُّ المَسارات). وبابُ الاستِمارَةِ `/{slug}/auth/nafath/login` يُنادي الدالَّةَ ويَلتَقِطُ رَدَّها —
    // نَسَقُ `RequestPhoneOtpHandler` أَعلاه. فَبابٌ لا يُفتَحُ لِأَحَدٍ يُخفى (القاعِدَتانِ ١ و١٢) لا يُرَمَّم.
    public static async Task<NafathPending> RequestNafathHandler(
        RequestNafath cmd, ITenantContext tenantCtx,
        INafathChannel channel, CancellationToken ct)
    {
        if (!tenantCtx.IsResolved) throw new InvalidOperationException("tenant_required");
        // ‏(‏2026-10-24، تَدقيقُ الإطلاق lens-security-3) كُلُّ بَدءٍ نِداءٌ إلى نَفاذ — كانَ بِلا حِصَّة. الحِصَّةُ نَفسُها (عَشرٌ في السّاعَةِ لِكُلِّ
        // هُوِيَّةٍ في مَتجَر — لا رَقَمَ جَديد) في العَدّادِ نَفسِه بِمِفتاحٍ لا يُصادِفُ جَوّالاً، ثُمَّ سَقفُ المَنَصَّة. وبابُ الاستِمارَةِ يَلتَقِطُ الرَدّ.
        if (!TryConsumeSendQuota($"{tenantCtx.Slug}|nafath:{cmd.NationalId?.Trim()}")
            || !TryConsumeGlobalSendQuota(GlobalSendsPerHour))
            throw new InvalidOperationException("rate_limited: try again in an hour");
        var result = await channel.StartAsync(cmd.NationalId, ct);
        EvictExpiredAttempts();
        Attempts[result.AttemptId] = new AuthAttempt(
            result.AttemptId, tenantCtx.Slug, cmd.NationalId, "",
            DateTime.UtcNow.AddMinutes(2), AuthKind.Nafath);
        return new NafathPending(result.AttemptId, result.DisplayCode, result.AutoApproveInSeconds);
    }

    [WolverinePost("/{slug}/auth/nafath/verify")]
    public static async Task<AuthResult?> VerifyNafathHandler(
        VerifyNafath cmd, ITenantContext tenantCtx,
        INafathChannel channel, IDocumentStore store, CancellationToken ct)
    {
        if (!tenantCtx.IsResolved) return null;
        if (!Attempts.TryGetValue(cmd.AttemptId, out var attempt)) return null;
        if (attempt.Kind != AuthKind.Nafath
         || attempt.TenantSlug != tenantCtx.Slug
         || attempt.Subject != cmd.NationalId) return null;
        if (attempt.ExpiresAt < DateTime.UtcNow) { Attempts.TryRemove(attempt.Id, out _); return null; }
        var approved = await channel.IsApprovedAsync(cmd.AttemptId, ct);
        if (!approved) return null;
        Attempts.TryRemove(attempt.Id, out _);
        return await GetOrCreateUserAsync(store, tenantCtx.Slug,
            phone: $"NID-{cmd.NationalId}", nationalId: cmd.NationalId);
    }

    /// <summary>مِفتاحُ اسمِ الحِسابِ الجَديد في القامُوس — يَقرَؤُه <c>Me.razor</c> سُقوطاً لِاسمٍ غائِب أَيضاً.</summary>
    public const string NewMemberNameKey = "me.profile.new_member";

    /// <summary>
    /// <para><b>اسمُ الحِسابِ الجَديدِ بِالهاتِفِ أَو البَريد — مِن القامُوسِ بِالعَرَبِيَّةِ وَحدَها</b> (‏2026-10-24، تَيّارُ S15 البَند D؛
    /// القاعِدَة ١١). كانَ حَرفِيّاً هُنا مَرَّتَين؛ والقيمَةُ <b>تُخَزَّنُ في وَثيقَةِ المُستَخدِم</b>، فَلا تَتبَعُ لُغَةَ القارِئ: العَرَبِيَّةُ
    /// صَراحَةً (‏<see cref="ACommerce.Platform.I18n.LocaleCatalog.Arabic"/>) لا لُغَةُ الطَلَب — وبايتاتُها هي ما كانَ يُكتَب (مُثَبَّتَةً في
    /// <c>NewMemberNameTests</c>).</para>
    ///
    /// <para><b>ومِفتاحٌ غائِبٌ يَرمي</b> لا يُخَزِّنُ اسمَ المِفتاحِ اسماً لِإنسان — والمُصادِقُ (`LocaleValidator`) يَمنَعُ غِيابَه في بِناءٍ
    /// سَليم. وما يَراه المُشتَري اسماً لِلبائِعِ قَرارُ مالِك («ب» S12·٤(١)) — هذا تَرحيلٌ بِلا تَغييرِ قيمَة.</para>
    /// </summary>
    internal static string NewMemberName()
        => ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, NewMemberNameKey)
           ?? throw new InvalidOperationException($"مِفتاحُ القامُوس «{NewMemberNameKey}» غائِبٌ في ar.json — لا يُخَزَّنُ اسمٌ لِلحِسابِ الجَديد.");

    private static async Task<AuthResult> GetOrCreateUserAsync(
        IDocumentStore store, string tenantSlug, string phone, string? nationalId,
        string? email = null)
    {
        await using var session = store.LightweightSession(tenantSlug);
        // البَريد مِفتاح هُويَّة مُستَقِلّ — لا يُخلَط بِبَحث الهاتِف/نَفاذ
        // كَي لا يَلتَقِط مُستَخدِمي الهاتِف ذَوي الحَقل الفارِغ.
        // ‏(‏2026-10-28، قَرارُ المالِك د57 «أ»؛ ‏ADR-044) الجَوّالُ يُبحَثُ كَما كُتِبَ أَوَّلاً ثُمَّ بِصيغَتِه المُوَحَّدَة (`PhoneNumber`) — كانَ
        // `u.Phone == phone` وَحدَه فَـ`05…` و`+966…` لِرَقَمٍ واحِدٍ حِسابان (تَوصيفُه `d72e34e`). والجَديدُ يُكتَبُ مُوَحَّداً.
        var phoneForms = PhoneNumber.LookupForms(phone).ToArray();
        var existing = email is not null
            ? await session.Query<User>().FirstOrDefaultAsync(u => u.Email == email)
            : nationalId != null
                ? await session.Query<User>().FirstOrDefaultAsync(u => u.NationalId == nationalId)
                : PhoneNumber.Pick(await session.Query<User>().Where(u => phoneForms.Contains(u.Phone)).ToListAsync(), u => u.Phone, phone);
        if (existing is null)
        {
            existing = new User
            {
                Id = Guid.NewGuid(),
                TenantSlug = tenantSlug,
                Phone = PhoneNumber.Canonical(phone),
                Email = email ?? "",
                NationalId = nationalId,
                PhoneVerified = nationalId is null && email is null,
                EmailVerified = email is not null,
                // مَن يَدخُل بِنَفاذ يَأخُذ اسماً مُشتَقّاً حَتمِيّاً مِن
                // رَقم هُوِيَّتِه بَدَل «مُستَخدِم نَفاذ» لِلجَميع — وإلّا
                // بَدَت كُلّ شاشَة فيها أَكثَر مِن طَرَف مُكَرَّرَةً.
                // التَّغيير عَلى الإنشاء وَحدَه: مُستَخدِم قائِم لا يُمَسّ.
                FullName = email is not null ? NewMemberName()
                    : nationalId is null ? NewMemberName()
                    : ACommerce.Kit.Auth.NafathNames.For(nationalId)
            };
            session.Store(existing);
            await session.SaveChangesAsync();
        }
        else if (RaiseVerified(existing, email, nationalId))
        {
            // مُستَخدِم قَديم بِبَريد غَير مُوَثَّق أَثبَتَ مِلكِيَّته الآن.
            // (‏2026-10-13، G5 M2 — ولا يُعادُ حَرفٌ فَوقَه: أَو بِجَوّالٍ غَيرِ مُوَثَّقٍ أَثبَتَه رَمزٌ إلَيه — `RaiseVerified`.)
            // ‏(‏2026-10-29، تَدقيقُ الموجَة ١١ G-1) والرَفعُ عَلى قِراءَةٍ جَديدَةٍ بِمُعَرِّفِه (`AccountWrites.UpdateAsync`) لا عَلى نُسخَةِ البَحث —
            // كانَ حِفظُها الكامِلُ يُعيدُ إصدارَ جَلسَةٍ رُفِعَ بَعدَ البَحث؛ ويُعادُ مَرَّةً إن سَبَقَه كاتِب.
            existing = await AccountWrites.UpdateAsync<User>(store, tenantSlug, existing.Id, u =>
            {
                if (!RaiseVerified(u, email, nationalId)) return false;
                u.UpdatedAt = DateTime.UtcNow;
                return true;
            }) ?? existing;
        }
        var token = MakeToken(existing.Id, tenantSlug, existing.SessionVersion);
        // حَقل العَرض: الهاتِف حَيث وُجِد، وإلّا البَريد — كَي لا تَظهَر
        // بِطاقَة مُستَخدِم بَريديّ بِمُعَرِّف فارِغ في الواجِهَة والإشعارات.
        var display = string.IsNullOrEmpty(existing.Phone) ? existing.Email : existing.Phone;
        return new AuthResult(existing.Id, existing.FullName, display, token, existing.Role);
    }

    /// <summary>
    /// <para><b>ما يَرفَعُه دُخولٌ نَجَحَ لِحِسابٍ قائِم</b> — دالَّةٌ تُعَدِّلُ الوَثيقَةَ المُمَرَّرَةَ وتُرجِعُ <c>true</c> إن تَغَيَّرَت فَتُحفَظ:
    /// البَريدُ إن أَثبَتَه رَمزٌ إلى بَريدِه (كَما كان)، <b>والجَوّالُ إن أَثبَتَه رَمزٌ إلى جَوّالِه</b> (‏2026-10-13، مُراجَعَةُ المَوجَة 3b، G5 M2).
    /// كانَ <c>PhoneVerified</c> يُكتَبُ عِندَ الإنشاءِ وَحدَه، فَحِسابٌ كَتَبَه غَيرُ الدُخولِ (المُستَورِد، بَذرَةُ الاختِبار) يَبقى غَيرَ مُتَحَقَّقٍ
    /// مِنه بَعدَ رَمزٍ ناجِحٍ إلى جَوّالِه — وبَوّابَةُ جِهَةِ التَواصُلِ (<c>VendorContactGate</c>) تَقرَؤُه. <b>ونَفاذٌ لا يَرفَعُ شَيئاً</b>: جَوّالُه
    /// <c>NID-…</c> أَو جَوّالٌ لَم يُرسَل إلَيه رَمز.</para>
    /// </summary>
    public static bool RaiseVerified(User existing, string? email, string? nationalId)
    {
        if (email is not null)
        {
            if (existing.EmailVerified) return false;
            existing.EmailVerified = true;
            return true;
        }
        if (nationalId is not null || existing.PhoneVerified) return false;
        existing.PhoneVerified = true;
        return true;
    }

    /// <summary>
    /// Token = <c>base64({userId}|{tenantSlug}|{expiresUnix}|{hmacSig})</c>.
    /// التَوقيع HMAC-SHA256 بِـ <see cref="TokenSecret"/> فَوق
    /// <c>{userId}|{tenantSlug}|{expiresUnix}</c>. كانَ بِلا تَوقيع — قابِل
    /// لِلتَّزوير. الآن ParseToken يَفحَص التَّوقيع ويَرفُض أَيّ تَعديل.
    /// </summary>
    /// <remarks>‏(‏2026-10-28، قَرارُ المالِك د28 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>وإصدارُ الجَلسَة</b>: بِـ<paramref name="sessionVersion"/>
    /// أَكبَرَ مِن صِفرٍ يَصيرُ الرَمزُ خَمسَةَ حُقول — <c>{userId}|{tenantSlug}|{exp}|{version}|{sig}</c> والتَوقيعُ فَوقَ الأَربَعَةِ الأولى — وبِالصِفرِ
    /// يَبقى بِشَكلِه القَديمِ حَرفاً. فَرُموزُ اليَومِ (بِلا إصدار) إصدارُها 0، وحِسابٌ لَم يُرفَع إصدارُه لا يَخرُجُ بِالنَشر.</remarks>
    public static string MakeToken(Guid userId, string tenantSlug, int sessionVersion = 0)
    {
        var exp = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
        var payload = sessionVersion > 0
            ? $"{userId}|{tenantSlug}|{exp}|{sessionVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : $"{userId}|{tenantSlug}|{exp}";
        var sig = Sign(payload);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{payload}|{sig}"));
    }

    public static (Guid UserId, string TenantSlug, DateTime ExpiresAt)? ParseToken(string? token)
        => ParseVersioned(token) is { } t ? (t.UserId, t.TenantSlug, t.ExpiresAt) : null;

    /// <summary>إصدارُ الجَلسَةِ في رَمزٍ صالِح (‏2026-10-28، د28) — 0 لِلرَمزِ ذي الحُقولِ الأَربَعَة، و<c>null</c> لِرَمزٍ لا يَمُرّ
    /// <see cref="ParseToken"/>.</summary>
    public static int? SessionVersionOf(string? token) => ParseVersioned(token)?.Version;

    private static (Guid UserId, string TenantSlug, DateTime ExpiresAt, int Version)? ParseVersioned(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            var parts = raw.Split('|');
            // ٤ حُقول إجباريّاً: userId, tenantSlug, exp, sig. أَيّ أَقَلّ
            // يُرفَض (تَوكينات قَديمَة بِلا تَوقيع لا تَمُرّ → تَسجيل دُخول مَرَّةً أُخرى).
            // ‏(‏2026-10-28، د28) أَو خَمسَة: الرابِعُ إصدارُ الجَلسَة — عَدَدٌ مُوجَبٌ بِلا عَلامَة، والتَوقيعُ يُغَطّيه فَلا يُبَدَّل.
            if (parts.Length is not (4 or 5)) return null;
            var payload = string.Join('|', parts[..^1]);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(parts[^1]),
                    Encoding.UTF8.GetBytes(Sign(payload))))
                return null;
            var version = 0;
            if (parts.Length == 5
                && (!int.TryParse(parts[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out version)
                    || version <= 0))
                return null;
            var exp = DateTimeOffset.FromUnixTimeSeconds(long.Parse(parts[2])).UtcDateTime;
            if (exp < DateTime.UtcNow) return null;
            return (Guid.Parse(parts[0]), parts[1], exp, version);
        }
        catch { return null; }
    }

    private static string Sign(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TokenSecret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    private static string Hash(string s)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(s)));
    }
}
