namespace ACommerce.Kit.Files;

// ═══ اختِيار مُزَوِّد التَخزين — قَرارٌ بِالتَهيئَة، وفَشَلٌ مُغلَق ════
//
// **العِلَّة المَقيسَة (‏2026-08-30)**: ‏`Program.cs` كانَ يُسَجِّل
// `AddLocalFileStorage(…)` **بِلا شَرطِ بيئَة**، على `wwwroot/uploads`
// داخِلَ الحاوِيَة. وقُرصُ الـSpace **زائِل** — فَكُلُّ ما رُفِعَ
// يَختَفي عِندَ أَوَّلِ إعادَةِ نَشر. ومُستَهلِكاه حَيّان: صُوَرُ
// الإعلاناتِ والصُوَرُ الشَخصِيَّة.
//
// **وأَسوَأُ مِن الفَقدِ الصامِت**: الرابِطُ يَبقى في القاعِدَةِ
// ويُصَيَّر في الصَفحَة، فَتُرسَم **صورَةٌ مَكسورَة**. مَقيسٌ حَيّاً
// على النُسخَةِ المَنشورَة: الجِذرُ ‏200 و`/uploads/<أَيُّ مَسار>` ‏404.
//
// **ولا أُنبوبَ رابِع** (القاعِدَة ٨): هذا المِلَفُّ نَظيرَةُ
// `AuthChannelSelection` و`PaymentProviderSelection` **بِنَفسِ الشَكلِ
// حَرفاً** — عَلامَةٌ على المُحاكي، ودالَّةُ قَرارٍ نَقِيَّةٌ تُقاس
// بِجَدوَل، وحارِسُ إقلاعٍ يَرمي قَبلَ أَوَّلِ طَلَب.
//
// **ولِماذا نَظيرَةٌ ثالِثَةٌ لا تَعميمُ القائِمَتَين**: العَلامَتانِ
// تَسكُنانِ في `ACommerce.Kit.Auth.Core` و`ACommerce.Kit.Payments.Core`،
// وجَعلُ عُدَّةِ المِلَفّاتِ تُحيلُ إلى إحداهُما حَدٌّ مَقلوب. أَمّا
// تَوحيدُ الثَلاثِ في مِلَفٍّ رابِعٍ فَـ**هُوَ ما تَأذَنُ بِه القاعِدَةُ ١
// الآن** (ثَلاثَةُ مُستَهلِكينَ قَبلَ الاستِخراج) — ويُستَخرَج يَومَ
// يوجَد ما يَستَحِقُّ الاستِخراج: المُشتَرَكُ بَينَها اليَومَ ثَلاثَةُ
// أَسطُرٍ مِن `Where` و`Select`، والاستِخراجُ يَشُدُّ ثَلاثَ عُدَدٍ
// مُستَقِلَّةٍ إلى مَشروعٍ مُشتَرَكٍ خامِس مُقابِلَ ذلك. **يُقاسُ
// المُشتَرَكُ يَومَ يَكبُر، ولا يُستَخرَج بِعَدِّ الثَلاثَةِ وَحدَه.**

/// <summary>
/// عَلامَةٌ يَحمِلُها مَخزَنُ المِلَفّاتِ التَطويريّ — أَي الَّذي
/// يَكتُب على قُرصٍ **زائِل**. يُنَفِّذُها
/// <see cref="LocalFileStorage"/>، ويَقرَؤُها حارِسُ الإقلاع
/// (<see cref="FileStorageSelection.StubViolations"/>).
///
/// <para><b>ولِماذا عَلامَةٌ لا فَحصُ الاسم</b>: ‏<c>ProviderName ==
/// "Local"</c> نَصٌّ يُبَدَّل بِإعادَةِ تَسمِيَةٍ صامِتَة، والعَلامَةُ
/// جُزءٌ مِن نَوعِ الصِنف — تَسقُط بِحَذفٍ مَرئيٍّ في مُراجَعَة. نَفسُ
/// حُجَّةِ <c>IDevelopmentStubPaymentProvider</c> حَرفاً.</para>
///
/// <para><b>و«تَطويريّ» هُنا وَصفُ الدَوامِ لا وَصفُ التَلفيق</b>:
/// ‏<c>LocalFileStorage</c> لا يَكذِب — يَكتُب ويَقرَأُ صِدقاً. عَيبُه
/// أَنّ ما كَتَبَه يَذهَب. وهذا يَكفي: مُحاكي الدَفعِ يُخسِرُ المالِكَ
/// اشتِراكاً، وهذا يُخسِرُ المُستَخدِمَ صورَتَه — وكِلاهُما «يُجيبُ
/// بِنَجاحٍ عَن شَيءٍ لَم يَقَع».</para>
/// </summary>
public interface IDevelopmentStubFileStorage { }

/// <summary>المَخزَنُ المُختار.</summary>
public enum FileStorageChoice
{
    /// <summary>
    /// <para>لا مَخزَنَ دائِمَ مَضبوطٍ خارِجَ التَطوير — يُسَجَّل
    /// <see cref="UnavailableFileStorage"/>، فَتُرَدُّ كُلُّ كِتابَةٍ
    /// بِفَشَلٍ صَريحٍ ذي سَبَب. <b>هذا هُوَ الفَشَلُ المُغلَق، وهُوَ
    /// نَفسُه السُقوطُ الآمِن</b>: الرَفضُ عِندَ الكِتابَةِ يَمنَع
    /// الرابِطَ المُعَلَّقَ مِن الوُجودِ أَصلاً، فَلا صورَةَ مَكسورَةٌ
    /// تُرسَم لاحِقاً. وذلك أَقوى مِن أَيِّ بَديلٍ يُعرَض عِندَ
    /// القِراءَة، لِأَنَّه يَقَع <b>قَبلَ</b> أَن تُكتَبَ الكَذِبَةُ في
    /// القاعِدَة.</para>
    ///
    /// <para><b>ولِماذا يُسَجَّلُ بَديلٌ ولا يُترَكُ الوِعاءُ فارِغاً</b>:
    /// ‏<c>IFileStorage</c> وَسيطٌ في جِسمَي
    /// <c>POST /{slug}/listings/create</c> و<c>POST /{slug}/me/save</c>.
    /// فَغِيابُ التَسجيلِ يَعني انفِجارَ حَلٍّ عِندَ أَوَّلِ إعلانٍ
    /// يُنشَر — <b>عُطلٌ عامٌّ في مَسارٍ لا عَلاقَةَ لَه بِالصُوَر</b>،
    /// بَدَلَ رَفضٍ واحِدٍ مَوضِعيّ. نَفسُ حُجَّةِ
    /// <c>UnavailablePaymentProvider</c> حَرفاً.</para>
    /// </summary>
    Unavailable,

    /// <summary>قُرصٌ مَحَلِّيٌّ زائِل. مَسموحٌ في <c>Development</c>
    /// وَحدَها.</summary>
    Local,

    /// <summary>مَخزَنُ كائِناتٍ مُتَوافِقٌ مَع S3 (‏Cloudflare R2،
    /// ‏B2، ‏MinIO، ‏Wasabi…). دائِمٌ وخارِجَ الحاوِيَة.</summary>
    S3,

    /// <summary>
    /// <para>تَهيئَةُ S3 **ناقِصَة** — بَعضُ المَفاتيحِ مَضبوطٌ وبَعضُها
    /// لا. والإقلاعُ يَتَوَقَّف.</para>
    ///
    /// <para><b>ولِماذا لا يُتَجاهَلُ الناقِصُ ويُسقَطُ إلى
    /// <see cref="Unavailable"/></b>: هذا بِعَينِه ما قَرَّرَته
    /// <c>AddPaddleBilling</c> قَبلَها — «خَطَأُ إملاءٍ في مُتَغَيِّرٍ
    /// يُخفي البِطاقَةَ صامِتاً، فَيَبحَثُ المالِكُ عَن زِرٍّ لا
    /// يَظهَر». ومَن ضَبَطَ أَربَعَةً مِن خَمسَةٍ **قَصَدَ التَشغيل**،
    /// فَإسقاطُه صامِتاً إلى «لا مَخزَن» يُخفي مِفتاحاً مَكتوباً
    /// بِخَطَإٍ خَلفَ سُلوكٍ يَبدو مَقصوداً.</para>
    /// </summary>
    Misconfigured
}

/// <summary>مَخزَنُ مِلَفّاتٍ مُسَجَّلٌ فِعلاً في وِعاءِ الخِدَمات — كَما
/// يَراهُ حارِسُ الإقلاع.</summary>
/// <param name="ProviderName">اسمُ المُزَوِّد (لِلتَشخيصِ والرِسالَة).</param>
/// <param name="IsDevelopmentStub">أَيَكتُب على قُرصٍ زائِل؟</param>
public sealed record RegisteredFileStorage(string ProviderName, bool IsDevelopmentStub);

/// <summary>
/// المَفاتيحُ الخَمسَةُ كَما تُقرَأُ مِن التَهيئَة — **قيمٌ لا
/// مُسنَدات**، فَالدالَّةُ تَبقى نَقِيَّةً وتُقاسُ بِجَدوَل.
/// </summary>
public sealed record S3StorageSettings(
    string? Endpoint,
    string? Bucket,
    string? AccessKeyId,
    string? SecretAccessKey,
    string? PublicBaseUrl)
{
    /// <summary>لا شَيءَ مَضبوط — الغِيابُ التامّ، وهُوَ حالَةٌ
    /// مَشروعَةٌ لا خَطَأ.</summary>
    public bool IsAbsent => Present.Count == 0;

    /// <summary>الخَمسَةُ كامِلَة.</summary>
    public bool IsComplete => Missing.Count == 0;

    /// <summary>أَسماءُ المَفاتيحِ المَضبوطَة.</summary>
    public IReadOnlyList<string> Present => Pairs
        .Where(p => !string.IsNullOrWhiteSpace(p.Value))
        .Select(p => p.Key).ToList();

    /// <summary>أَسماءُ المَفاتيحِ الناقِصَة — تُطبَع في رِسالَةِ
    /// الإقلاعِ بِحَرفِها، فَلا يُبحَثُ عَنها.</summary>
    public IReadOnlyList<string> Missing => Pairs
        .Where(p => string.IsNullOrWhiteSpace(p.Value))
        .Select(p => p.Key).ToList();

    private IEnumerable<KeyValuePair<string, string?>> Pairs =>
    [
        new(FileStorageSelection.EndpointKey,        Endpoint),
        new(FileStorageSelection.BucketKey,          Bucket),
        new(FileStorageSelection.AccessKeyIdKey,     AccessKeyId),
        new(FileStorageSelection.SecretAccessKeyKey, SecretAccessKey),
        new(FileStorageSelection.PublicBaseUrlKey,   PublicBaseUrl),
    ];

    /// <summary>لا شَيءَ مَضبوط.</summary>
    public static S3StorageSettings None { get; } = new(null, null, null, null, null);
}

public static class FileStorageSelection
{
    // ─── المَفاتيح — بِحَرفِها، ومَوضِعٌ واحِدٌ يَقرَؤُه المُنتِجُ
    //     والمُختَبِرُ والوَثيقَة ───────────────────────────────────────
    public const string EndpointKey        = "Files:S3:Endpoint";
    public const string BucketKey          = "Files:S3:Bucket";
    public const string AccessKeyIdKey     = "Files:S3:AccessKeyId";
    public const string SecretAccessKeyKey = "Files:S3:SecretAccessKey";
    public const string PublicBaseUrlKey   = "Files:S3:PublicBaseUrl";

    /// <summary>المَفاتيحُ الخَمسَةُ بِتَرتيبِها — لِلوَثيقَةِ
    /// ولِلاختِبارِ الَّذي يَمنَع نُمُوَّها صامِتَةً.</summary>
    public static IReadOnlyList<string> ConfigKeys =>
    [
        EndpointKey, BucketKey, AccessKeyIdKey, SecretAccessKeyKey, PublicBaseUrlKey
    ];

    /// <summary>
    /// <para><b>القَرار — دالَّةٌ نَقِيَّةٌ بِمُدخَلَين.</b></para>
    ///
    /// <list type="bullet">
    /// <item>تَهيئَةٌ كامِلَة → <see cref="FileStorageChoice.S3"/>
    /// <b>في التَطويرِ كَما في الإنتاج</b>: مَن ضَبَطَ المِفتاحَ
    /// مَحَلِّيّاً يُريد أَن يُجَرِّبَ المَخزَنَ الحَقيقيَّ قَبلَ النَشر،
    /// وحِرمانُه مِنه يَجعَل أَوَّلَ تَجرِبَةٍ حَقيقِيَّةٍ في
    /// الإنتاج.</item>
    /// <item>تَهيئَةٌ ناقِصَة → <see cref="FileStorageChoice.Misconfigured"/>
    /// <b>في البيئَتَين</b> — خَطَأُ الإملاءِ لا يُغتَفَر في التَطويرِ
    /// لِيُكتَشَفَ في الإنتاج.</item>
    /// <item>غِيابٌ تامّ + تَطوير → <see cref="FileStorageChoice.Local"/>.</item>
    /// <item>غِيابٌ تامّ + خارِجَ التَطوير →
    /// <see cref="FileStorageChoice.Unavailable"/>.</item>
    /// </list>
    /// </summary>
    public static FileStorageChoice Decide(bool isDevelopment, S3StorageSettings? s3)
    {
        var settings = s3 ?? S3StorageSettings.None;
        if (settings.IsComplete) return FileStorageChoice.S3;
        if (!settings.IsAbsent)  return FileStorageChoice.Misconfigured;
        return isDevelopment ? FileStorageChoice.Local : FileStorageChoice.Unavailable;
    }

    /// <summary>يَرمي إن كانَت تَهيئَةُ S3 ناقِصَة — <b>قَبلَ</b> بِناءِ
    /// المُضيف، فَلا يُقلِعُ التَطبيقُ بِمِفتاحٍ مَكتوبٍ بِخَطَإٍ يَبدو
    /// «لا مَخزَنَ مَقصوداً».</summary>
    public static void AssertConfigurationIsCompleteOrAbsent(S3StorageSettings? s3)
    {
        var settings = s3 ?? S3StorageSettings.None;
        if (Decide(true, settings) != FileStorageChoice.Misconfigured) return;
        throw new InvalidOperationException(
            "تَهيئَةُ مَخزَنِ المِلَفّاتِ ناقِصَة. المَضبوط: "
            + string.Join("، ", settings.Present)
            + ". والناقِص: " + string.Join("، ", settings.Missing)
            + ". ومَن ضَبَطَ بَعضَها قَصَدَ التَشغيل — فَالإقلاعُ يَتَوَقَّف "
            + "هُنا بَدَلَ أَن يُسقَطَ صامِتاً إلى «لا مَخزَن» ويُبحَثَ عَن "
            + "صُوَرٍ لا تُرفَع.");
    }

    // ─── حارِسُ الإقلاع ───────────────────────────────────────────────

    /// <summary>
    /// مَخزَنٌ زائِلُ القُرصِ مُسَجَّلاً خارِجَ التَطوير — واحِدٌ يَكفي
    /// لِتَذهَبَ صُوَرُ المُستَأجِرينَ عِندَ أَوَّلِ إعادَةِ نَشر.
    ///
    /// <para><b>و«خارِجَ التَطوير» لا «في الإنتاج» فَقَط</b>: بيئَةٌ
    /// ثالِثَة (‏Staging) لَيسَت Development ولا Production، وهي
    /// بِالضَبطِ الشَقُّ الَّذي يُنسى. نَفسُ حَرفِ
    /// <c>PaymentProviderSelection.StubViolations</c>.</para>
    /// </summary>
    public static IReadOnlyList<string> StubViolations(
        bool isDevelopment, IEnumerable<RegisteredFileStorage> registered)
    {
        if (isDevelopment) return Array.Empty<string>();
        return registered
            .Where(p => p.IsDevelopmentStub)
            .Select(p => p.ProviderName)
            .ToList();
    }

    /// <summary>يَرمي إن سُجِّلَ مَخزَنٌ زائِلُ القُرصِ خارِجَ التَطوير.
    /// يُستَدعى <b>بَعدَ</b> بِناءِ المُضيفِ وقَبلَ أَوَّلِ طَلَب.</summary>
    public static void AssertNoStubsOutsideDevelopment(
        bool isDevelopment, IEnumerable<RegisteredFileStorage> registered)
    {
        var violations = StubViolations(isDevelopment, registered);
        if (violations.Count == 0) return;
        throw new InvalidOperationException(
            "مَخزَنُ مِلَفّاتٍ على قُرصٍ زائِلٍ مُسَجَّلٌ خارِجَ بيئَة التَطوير: "
            + string.Join("، ", violations)
            + ". وقُرصُ الحاوِيَةِ يَذهَب عِندَ أَوَّلِ إعادَةِ نَشر، والرابِطُ "
            + "يَبقى في القاعِدَةِ فَتُرسَم صورَةٌ مَكسورَة — فَالإقلاعُ "
            + "يَتَوَقَّف هُنا بَدَلَ أَن تُفقَدَ صُوَرُ مُستَخدِمين. اضبِط "
            + string.Join("، ", ConfigKeys) + ".");
    }

    /// <summary>وَصفُ ما حُلَّ فِعلاً مِن الوِعاء — <c>null</c> إن لَم
    /// يُسَجَّل شَيء.</summary>
    public static RegisteredFileStorage? Describe(IFileStorage? storage)
        => storage is null
            ? null
            : new(storage.ProviderName, storage is IDevelopmentStubFileStorage);
}
