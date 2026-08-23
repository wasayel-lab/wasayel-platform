namespace ACommerce.Kit.Auth;

// ═══ مُهلَةُ إرسالِ الرَمز — الحارِسُ في الأُنبوبِ لا في كُلِّ مُزَوِّد ══
//
// **العِلَّةُ المَقيسَةُ في الإنتاج (‏2026-08-23، ‏Hugging Face Space،
// ‏`Auth__Email__Provider=smtp` على المَنفَذ ‏587)**:
//
//     POST /studio/auth/email/login   ⇒  لا رَدَّ بَعدَ ‏90 ثانِيَة
//     POST /ejar/auth/email/login     ⇒  لا رَدَّ بَعدَ ‏90 ثانِيَة
//     (‏`curl --max-time 90` يُرجِع الرَمزَ `000` — أَي انقِطاعاً بِلا رَدّ)
//
// والسَبَب: الـSpace يَحجُب مَنافِذَ SMTP الصادِرَة، و`ConnectAsync` **بِلا
// مُهلَة** — فَالطَلَبُ يَعلَق، ومَعَه المُستَخدِم أَمامَ صَفحَةٍ تَدور
// بِلا رِسالَةِ خَطَإ. **وتَعليقٌ يَبدو عَمَلاً أَسوَأُ مِن خَطَإ**:
// الخَطَأُ يَقول «جَرِّب طَريقَةً أُخرى»، والتَعليقُ لا يَقولُ شَيئاً.
//
// **ولِماذا هُنا لا في `SmtpEmailChannel` وَحدَه** (القاعِدَة ٦: الحِراسَةُ
// في التَوقيعِ لا في الجِسم): المُهلَةُ داخِلَ مُزَوِّدٍ تَحرُسُ **ذلِكَ
// المُزَوِّدَ وَحدَه**، والمُزَوِّدُ التالي يُكتَب بِلا حارِسٍ ولا يُلاحَظ
// — وهذا بِالضَبطِ ما وَقَع: قَناةُ SMTP كُتِبَت بِلا مُهلَة، ولَم يَقُل
// ذلِكَ شَيءٌ حَتّى عَلِقَ الإنتاج. فَالحارِسُ يُوضَع على **مَسارِ
// الاستِدعاءِ المُشتَرَك** الَّذي تَمُرّ بِه كُلُّ قَناة.
//
// **ولِماذا `Task.WhenAny` ولَيسَ الرَمزَ وَحدَه**: تَمريرُ
// `CancellationToken` يَحرُسُ مُزَوِّداً **يَحتَرِمُه**. ومُزَوِّدٌ
// يَبتَلِعُ رَمزَه (أَو يَعلَق في نِداءٍ مُتَزامِنٍ داخِلَ مَكتَبَةٍ
// خارِجِيَّة) يَبقى مُعَلَّقاً إلى الأَبَد رَغمَ الرَمز. فَالمُهلَةُ
// تُقاس **على المُنتَظِر** لا على المُنَفِّذ: يُلغى الرَمزُ ويُرفَع
// الخَطَأُ في وَقتِه سَواءٌ استَجابَ المُزَوِّدُ أَم لا.

/// <summary>
/// يُشَغِّل إرسالَ رَمزٍ ضِمنَ مُهلَةٍ صارِمَة، ويُحَوِّلُ التَجاوُزَ إلى
/// <see cref="InvalidOperationException"/> — وهي الَّتي تَلتَقِطُها نِقاطُ
/// الدُخول فَتُحَوِّل إلى <c>err=send_failed</c>. أَي: **فَشَلٌ صَريحٌ
/// مَقروءٌ بَدَلَ تَعليق**.
/// </summary>
public static class OtpSendGuard
{
    /// <summary>عَشرُ ثَوانٍ. لَيسَ رَقماً اعتِباطِيّاً: نافِذَةُ الطَلَب
    /// أَمامَ مُستَخدِمٍ يَنتَظِر، ومُزَوِّدُ بَريدٍ سَليمٌ يَرُدّ في
    /// أَقَلَّ مِن ثانِيَة. وما تَجاوَزَها فَهُوَ عُطلٌ لا بُطء.</summary>
    public const int DefaultTimeoutSeconds = 10;

    public static readonly TimeSpan DefaultTimeout =
        TimeSpan.FromSeconds(DefaultTimeoutSeconds);

    /// <summary>
    /// نافِذَةُ الأُنبوب: مُهلَةُ المُزَوِّدِ **زائِدَ ثانِيَتَي سَماح**.
    ///
    /// <para><b>ولِماذا لا تُساويها — بِقياسٍ حَيّ</b>: حينَ تَساوَتا
    /// تَسابَقَ حارِسانِ على العَشرِ نَفسِها، فَمَرَّةً يَسبِق المُزَوِّدُ
    /// (فَيُكتَب في اللوغ «المُضيفُ كَذا لَم يَرُدّ» — وهي السَطرُ الَّذي
    /// يُشَخِّص) ومَرَّةً يَسبِق الأُنبوبُ (فَيُكتَب سَطرٌ عامٌّ لا يُسَمّي
    /// المُضيف). الرَدُّ لِلمُستَخدِمِ واحِدٌ في الحالَتَين، **واللوغُ
    /// لَيسَ كَذلِك** — وهو ما سَيُقرَأ حينَ يَعود العُطل.</para>
    ///
    /// <para>فَالدَورانِ مَفصولان: المُزَوِّدُ يَقطَع أَوَّلاً ويَقولُ
    /// **لِماذا**، والأُنبوبُ سَقفٌ أَخيرٌ لِمُزَوِّدٍ بِلا مُهلَةٍ
    /// إطلاقاً. ومَعناهُ أَنّ <c>TimeoutSeconds</c> أَكبَرَ مِن هذِه
    /// النافِذَةِ **يُقَصّ عِندَها** — فَالمُستَخدِمُ لا يَنتَظِر أَكثَرَ
    /// مِمّا يَحتَمِلُه طَلَبٌ أَمامَ شاشَة.</para>
    /// </summary>
    public static readonly TimeSpan PipelineTimeout =
        DefaultTimeout + TimeSpan.FromSeconds(2);

    /// <summary>يُطَبِّع قيمَةَ التَهيئَة: الصِفرُ والسالِبُ والغائِبُ
    /// كُلُّها تَرتَدّ إلى الافتِراضيّ. **ولا تَعني «بِلا مُهلَة»** —
    /// لِأَنّ «بِلا مُهلَة» هي العِلَّةُ الَّتي كَتَبَت هذا المِلَفّ،
    /// فَلا يُترَك لَها بابٌ في التَهيئَة.</summary>
    public static TimeSpan Timeout(int seconds)
        => seconds > 0 ? TimeSpan.FromSeconds(seconds) : DefaultTimeout;

    /// <summary>رِسالَةُ التَجاوُز. تَبدَأ بِـ<c>send_timeout</c> لِتُقرَأ
    /// في اللوغ بِلا لَبس، ولا تَحمِل عُنوانَ المُستَخدِمِ ولا الرَمز.</summary>
    public static string TimeoutMessage(TimeSpan window)
        => $"send_timeout: تَجاوَزَ الإرسالُ {window.TotalSeconds:0.#} ثانِيَة.";

    /// <summary>
    /// يُنَفِّذ <paramref name="send"/> ضِمنَ <paramref name="timeout"/>.
    /// يَرمي <see cref="InvalidOperationException"/> عِندَ التَجاوُز أَو
    /// عِندَ فَشَلِ المُزَوِّد، ويُمَرِّر
    /// <see cref="OperationCanceledException"/> **وَحدَها** حينَ يَكون
    /// الإلغاءُ مِن المُتَّصِلِ نَفسِه (انقِطاعُ الطَلَب) — فَذاكَ لَيسَ
    /// فَشَلَ إرسالٍ ولا يُقال لِلمُستَخدِمِ إنَّه كَذلِك.
    /// </summary>
    public static async Task SendWithinAsync(
        Func<CancellationToken, Task> send,
        CancellationToken ct,
        TimeSpan? timeout = null)
    {
        var window = timeout ?? PipelineTimeout;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var sendTask = send(cts.Token);
        var winner = await Task.WhenAny(sendTask, Task.Delay(window, cts.Token))
            .ConfigureAwait(false);

        // يُنهي المُؤَقِّتَ في الحالَتَين — وإلّا بَقِيَ مُعَلَّقاً عَشرَ
        // ثَوانٍ بَعدَ كُلِّ إرسالٍ ناجِح.
        cts.Cancel();

        if (!ReferenceEquals(winner, sendTask))
        {
            ct.ThrowIfCancellationRequested();   // انقِطاعُ الطَلَب لا تَجاوُزُ مُهلَة
            // المُهمَّةُ المَتروكَة قَد تَرمي بَعدَ حينٍ فَتَصير استِثناءً
            // غَيرَ مُلاحَظ يُسقِط العَمَلِيَّة. تُلاحَظ ولا يُنتَظَر.
            _ = sendTask.ContinueWith(
                t => { _ = t.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
            throw new InvalidOperationException(TimeoutMessage(window));
        }

        try
        {
            await sendTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // المُزَوِّدُ احتَرَمَ الرَمزَ فَأَلغى — وهذا تَجاوُزُ مُهلَة
            // لا انقِطاعُ طَلَب. يُقال بِاسمِه.
            throw new InvalidOperationException(TimeoutMessage(window));
        }
    }
}
