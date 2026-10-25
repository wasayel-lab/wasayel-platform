using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>كُلّ نُقطَة كِتابَة تُعلِن حارِسَها — أَو تُثَبَّت بِاسمِها
/// وسَبَبِها.</b> هذا الفَحص الآليّ لِلقاعِدَة ٦، وقَد كُتِبَ بَعدَ
/// كِلفَتِه لا قَبلَها: في ‏2026-08-15 قاسَ <c>curl</c> مَجهول
/// <b>خَمساً وعِشرينَ</b> نُقطَةً بِلا حارِس، مِنها ثَمانٍ رَدَّت
/// <c>200</c> <b>وكَتَبَت فِعلاً</b> — أُنشِئَ إعلان، وعُدِّلَ سِعرُ
/// آخَر، ورُدَّ عَلى تَذكِرَة بِـ <c>FromStaff: true</c>، وأُنشِئَ
/// مُستَأجِرٌ حَقيقيّ في المَنصَّة. وثَلاثَة إعلانات دَخيلَة وَقَعَت في
/// <c>ashare</c> قَبل أَن يُكشَف الأَمر.</para>
///
/// <para><b>ولِماذا لَم يُمسِكها شَيء طَوال ذلك:</b> لَم يَكُن في
/// المُستَودَع فَحصٌ يَسأَل «هَل لِهذِه النُقطَة حارِس؟». كانَ السُؤال
/// يُطرَح عَلى المُراجِع، والمُراجِع يَقرَأ ما كُتِبَ لا ما نُسِيَ.
/// وهذا الفَحص يَقلِب الافتِراض: <b>الغِياب يَحمَرّ، والاستِثناء
/// يُكتَب</b>.</para>
///
/// <para><b>يَفحَص التَوقيع لا النِيَّة</b> (القاعِدَة ٦): يَبحَث عَن
/// <b>رَمز حارِسٍ مَعروف</b> في سِلسِلَة النُقطَة أَو جِسمِها، ولا
/// يَحكُم عَلى صِحَّة المَنطِق. ويَتَحَقَّق زِيادَةً أَنَّ الحارِس
/// <b>يَسبِق أَوَّل كِتابَة</b> نَصّيّاً — فَحارِسٌ بَعدَ
/// <c>SaveChangesAsync</c> لَيسَ حارِساً.</para>
///
/// <para><b>وحُدودُه مُعلَنَة، ولا يُدَّعى ما لا يَفعَل:</b></para>
/// <list type="bullet">
///   <item>لا يُثبِت أَنّ الحارِس <b>يَرُدّ</b> عِندَ الفَشَل — قَد
///   يُستَدعى ويُهمَل ناتِجُه. مَسحُ نَصّ لا يَبلُغ ذلك، وتَحليل
///   التَدَفُّق أَغلى مِن قيمَتِه هُنا.</item>
///   <item>لا يُثبِت أَنّ الحارِس <b>هُوَ الصَحيح</b> لِهذِه النُقطَة —
///   <c>TenantAdminGuard</c> عَلى نُقطَةٍ تَستَحِقّ
///   <c>PlatformAdminGuard</c> يَمُرّ. تِلكَ مُراجَعَة لا فَحص.</item>
///   <item>لا يَرى هُوِيَّةَ الفاعِل: نُقطَةٌ مَحروسَة تَأخُذ
///   <c>UserId</c> مِن جِسم الطَلَب تَمُرّ هُنا وهي مَعطوبَة. وهذا
///   بِعَينِه ما جَعَلَ حَذفَ نِقاط <c>api/*</c> أَصَحَّ مِن
///   حِراسَتِها.</item>
/// </list>
/// </summary>
// (‏2026-10-24، مُراجَعَةُ تَيّارِ S15 الجَولَة 1): ويَأخُذُ `ITestOutputHelper` لِيَطبَعَ صَفُّ بابِ السَلَّةِ ما عَدَّه (القاعِدَة ١٠)؛ ولا نُسخَةَ مِن
// الصِنفِ تُنشَأُ خارِجَ xUnit — قُرّاؤه مِن الخارِجِ دالّاتُه الساكِنَة.
public class WriteEndpointGuardTests(Xunit.Abstractions.ITestOutputHelper output)
{
    /// <summary>نُقطَة كِتابَة بِلا حارِس، مُثَبَّتَة بِسَبَبِها.</summary>
    private sealed record Unguarded(string Route, string WhyAr);

    /// <summary>
    /// <para><b>الاستِثناءات المُثَبَّتَة</b> — كُلُّها نِقاطٌ
    /// <b>عُمومِيَّةٌ بِالتَصميم</b>: إمّا أَنَّها <b>هي</b> مَسار
    /// الدُخول نَفسُه (فَحارِسُ الجَلسَة عَلَيها دَور)، أَو أَنَّها لا
    /// تَمَسّ قاعِدَة البَيانات أَصلاً.</para>
    ///
    /// <para>ونُمو هذِه القائِمَة <b>قَرارٌ مَرئيّ في مُراجَعَة</b> لا
    /// نَتيجَةُ نِسيان.</para>
    /// </summary>
    private static readonly Unguarded[] PinnedPublic =
    {
        // ─── مَسارات الدُخول نَفسُها ──────────────────────────────────
        new("/{slug}/auth/phone/login",   "طَلَب رَمز OTP — هذا هُوَ الدُخول. مَحميّ بِحَدّ مُعَدَّل ١٠/ساعَة لا بِجَلسَة."),
        new("/{slug}/auth/phone/verify",  "تَحَقُّق OTP — يُنشِئ الجَلسَة، فَلا جَلسَةَ قَبلَه. مَحميّ بِحَدّ ٥/دَقيقَة."),
        new("/{slug}/auth/email/login",   "طَلَب رَمز بَريديّ — نَفس عِلَّة الهاتِف."),
        new("/{slug}/auth/email/verify",  "تَحَقُّق بَريديّ — يُنشِئ الجَلسَة."),
        new("/{slug}/auth/nafath/login",  "بَدء دُخول بِنَفاذ — يُنشِئ الجَلسَة، فَلا جَلسَةَ قَبلَه."),
        new("/{slug}/auth/nafath/verify", "تَأكيد نَفاذ — يُنشِئ الجَلسَة، فَلا جَلسَةَ قَبلَه."),
        // (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-2؛ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق: كانَ هُنا صَفُّ "/{slug}/auth/nafath/request:w" — ونُزِعَ وَسمُ
        // [WolverinePost] عَن RequestNafathHandler لِأَنّ النُقطَةَ مَقيسَةٌ مَيتَةً (كُلُّ طَلَبٍ ‏500 tenant_required) وبِلا مُنادٍ؛ فَصَفٌّ بِلا نُقطَةٍ يُحمِرُّ
        // No_pinned_exception_outlives_its_reason.)
        new("/{slug}/auth/nafath/verify:w",  "‏[WolverinePost] تَأكيد نَفاذ — يُنشِئ الجَلسَة، ولا جَلسَةَ قَبلَه."),
        new("/studio/auth/login",         "طَلَب رَمز دُخول الـ studio — هذا هُوَ الدُخول نَفسُه."),
        new("/studio/auth/email/login",   "نَظيرُ سابِقَتِها بِالبَريد — نَفسُ القَرار: الدُخول نَفسُه، ولا جَلسَةَ قَبلَه."),
        new("/studio/auth/verify",        "تَحَقُّق رَمز الـ studio — يُنشِئ الجَلسَة، فَلا جَلسَةَ قَبلَه."),

        // ─── عامَّةٌ بِالتَصميمِ ومَحدودَة (‏2026-10-24، تَدقيقُ الإطلاق lens-security-5) ──────────
        new("/{slug}/listings/{id:guid}/view",
            "عَدّادُ مُشاهَدَة — مُشاهَدَةُ الزائِرِ المَجهولِ أَغلَبُ المُشاهَدات فَلا جَلسَةَ تُشتَرَط؛ وحَدُّها مُشاهَدَةٌ واحِدَةٌ لِكُلِّ زائِرٍ في الإعلانِ في السّاعَةِ (`ListingViews.Counts`) قَبلَ أَيِّ قِراءَةٍ أَو كِتابَة، مَفروضٌ في `The_view_door_is_pinned_public_and_bounded_before_it_reads_or_appends`."),

        // ─── لا تَمَسّ قاعِدَة البَيانات ──────────────────────────────
        new("/{slug}/auth/logout", "يَمسَح الـ cookie فَقَط. الخُروج بِلا جَلسَة لا مَعنى لِمَنعِه."),
        new("/studio/logout",      "يَمسَح الـ cookie فَقَط — لا كِتابَةَ في قاعِدَة البَيانات."),
        new("/lang/{lang}",        "يَكتُب تَفضيل اللُغَة في cookie — لا كِتابَةَ في قاعِدَة البَيانات."),
    };

    // ─── رُموز الحُرّاس المَعروفَة ─────────────────────────────────────

    /// <summary>حارِسٌ مُعلَن في السِلسِلَة — الشَكل الَّذي تُفَضِّلُه
    /// القاعِدَة ٦ (في التَوقيع لا في الجِسم).</summary>
    private static readonly string[] ChainGuards =
    {
        ".RequireAuth(", ".RequireAuthApi(", ".RequireTerms(",
        ".RequirePermission(", ".RequireEntitlement(",
        // المَوجَة ٤: الحارِسُ الوَحيد الَّذي يَسأَل عَن **المَفعولِ
        // بِه** لا عَن الفاعِل — مِلكِيَّةُ الإعلان. ويُسَجَّل هُنا
        // لِأَنّ رَمزَ حارِسٍ لا يَعرِفُه الفاحِصُ حارِسٌ لا يُرى:
        // نُقطَةٌ مَحروسَةٌ بِه وَحدَه كانَت سَتُعَدّ مَكشوفَة.
        ".RequireListingOwner(",

        // مَوجَةُ سَطح الـAPI: الحارِسُ الوَحيد تَحتَ `/api/v1` —
        // اعتِمادُ المِفتاح، والنِطاق، واستِحقاق `api.call` في سَطرٍ
        // واحِد. ويُسَجَّل هُنا لِلسَبَب نَفسِه الَّذي سُجِّلَ لَه
        // `RequireListingOwner`: **رَمزُ حارِسٍ لا يَعرِفُه الفاحِصُ
        // حارِسٌ لا يُرى** — والنُقطَتانِ المَحروستانِ بِه وَحدَه
        // كانَتا ستُعَدّانِ مَكشوفَتَين.
        ".RequireApiKey(",

        // ‏ADR-003: باقَةُ المَتجَرِ سارِيَة. وهُوَ `RequireEntitlement`
        // بِقُدرَةٍ مِن المَعجَم نَفسِه — يُسَجَّل هُنا بِاسمِه المُختَصَر
        // لِلسَبَب نَفسِه: **رَمزُ حارِسٍ لا يَعرِفُه الفاحِصُ حارِسٌ لا
        // يُرى**.
        ".RequireStoreWritable(",
    };

    // ─── الحَدُّ الَّذي كَتَبَته ‏ADR-003 ──────────────────────────────
    /// <summary>
    /// <para><b>نُقاطُ الكِتابَةِ في مَتجَرٍ الَّتي لا تُفحَص باقَتُه</b> —
    /// وكُلٌّ بِسَبَبِه. القائِمَةُ <b>ثُنائِيَّةُ الاتِّجاه</b>: مَسارٌ
    /// جَديدٌ تَحتَ <c>/{slug}/</c> بِلا <c>RequireStoreWritable</c> وهُوَ
    /// غَيرُ مُثَبَّتٍ يَحمَرّ، ومُثَبَّتٌ صارَ مَحروساً أَو زالَ
    /// يَحمَرّ.</para>
    ///
    /// <para><b>والمِحوَرُ واحِد</b>: هَل هذا <b>مُحتَوىً يُنشَأ في
    /// المَتجَر</b>؟ الدُخولُ لَيسَ كَذلك (وبِمَنعِه يُمنَع صاحِبُ
    /// المَتجَرِ نَفسُه مِن رُؤيَةِ لافِتَةِ التَجديد)، وعَلاماتُ
    /// القِراءَةِ والمُشاهَداتِ أَثَرُ <b>قِراءَة</b> نُقِلَ إلى نُقطَةٍ
    /// (المَوجَة ٧) — ومَنعُها كانَ يُبقي عَدّاداتٍ حَمراءَ إلى الأَبَد
    /// في مَتجَرٍ يُقرَأ ولا يُكتَب، وذاكَ عَينُ ما تَسمَح بِه مُهلَةُ
    /// السَماح.</para>
    /// </summary>
    private sealed record UngatedStoreWrite(string Route, string WhyAr);

    private static readonly UngatedStoreWrite[] PinnedUngatedStoreWrites =
    {
        new("/{slug}/auth/phone/login",   "الدُخولُ لَيسَ مُحتَوىً — ومَنعُه يَحجُب صاحِبَ المَتجَرِ عَن لافِتَةِ التَجديد."),
        new("/{slug}/auth/phone/verify",  "نَفسُه — تَأكيدُ الرَمز يُنشِئ الجَلسَةَ ولا يُنشِئ مُحتَوىً في المَتجَر."),
        new("/{slug}/auth/email/login",   "دُخولٌ بِالبَريد — نَفسُ حُجَّةِ مَسارِ الهاتِف: جَلسَةٌ لا مُحتَوى."),
        new("/{slug}/auth/email/verify",  "تَأكيدُ رَمزِ البَريد — نَفسُ حُجَّةِ مَسارِ الهاتِف: جَلسَةٌ لا مُحتَوى."),
        new("/{slug}/auth/nafath/login",  "دُخولٌ بِنَفاذ — نَفسُ حُجَّةِ مَسارِ الهاتِف: جَلسَةٌ لا مُحتَوى."),
        new("/{slug}/auth/nafath/verify", "تَأكيدُ نَفاذ — نَفسُ حُجَّةِ مَسارِ الهاتِف: جَلسَةٌ لا مُحتَوى."),
        new("/{slug}/auth/logout",        "خُروجٌ — لا يَلمِس قاعِدَةَ البَيانات أَصلاً، ومَنعُه يَحبِس الداخِلَ في مَتجَرٍ مُغلَق."),
        new("/{slug}/terms/accept",       "قَبولُ الشُروط شَرطُ **القِراءَة** نَفسِها؛ ومَنعُه يُغلِق المَتجَرَ في مُهلَةِ السَماح."),
        new("/{slug}/chats/{conversationId:guid}/read",  "عَلامَةُ قِراءَة — أَثَرُ قِراءَةٍ نُقِلَ إلى نُقطَة (المَوجَة ٧)، لا مُحتَوى."),
        new("/{slug}/notifications/read", "عَلامَةُ قِراءَةِ الإشعارات — أَثَرُ قِراءَةٍ نُقِلَ إلى نُقطَة، لا مُحتَوى."),
        new("/{slug}/listings/{id:guid}/view", "عَدّادُ مُشاهَدَة — أَثَرُ قِراءَةٍ نُقِلَ إلى نُقطَة، لا مُحتَوى."),
        // ‏ADR-033 الخُطوَة 3 (‏2026-10-12): مُنادِيها خادِمُ OTO لا صاحِبُ المَتجَر، ورَدُّ المُرَشِّحِ تَحويلٌ إلى صَفحَةٍ لا يَقرَؤُها أَحَد.
        new("/{slug}/shipping/oto/order-status",
            "تَسليمٌ وَقَعَ في العالَمِ تُؤَكِّدُه OTO حينَ نَسأَلُها — خَبَرٌ لا مُحتَوىً يَكتُبُه المَتجَر؛ ومَنعُه بِباقَةٍ مُنتَهِيَةٍ يُبقي طَلَباً سُلِّمَ «قَيدَ الشَحن» إلى الأَبَد."),
        // ‏ADR-032 الخُطوَة 9 (تَيّارُ S9، ‏2026-10-12): مُنادِيها المُشتَري بَعدَ أَن دَفَعَ عِندَ بَوّابَةِ المَتجَر، ورَدُّ المُرَشِّحِ تَحويلٌ يُسقِطُ إثباتَ دَفعٍ وَقَع.
        new("/{slug}/payments/verify",
            "دَفعٌ وَقَعَ عِندَ بَوّابَةِ المَتجَرِ يُثبَتُ ويُسَجَّل — خَبَرٌ عَن مالٍ قُبِضَ لا مُحتَوىً يَكتُبُه المَتجَر؛ ومَنعُه بِباقَةٍ مُنتَهِيَةٍ يَترُكُ مُشتَرِياً دَفَعَ بِلا أَثَرٍ عَلى طَلَبِه."),
    };

    /// <summary>حارِسٌ في الجِسم — مَقبول لِأَنَّه الواقِع الغالِب في
    /// هذا المُستَودَع، ومَرصود بِفَحص التَرتيب أَدناه.</summary>
    private static readonly string[] BodyGuards =
    {
        "PlatformAdminGuard.EvaluateAsync", "TenantAdminGuard.CanAdministerAsync",
        "StudioOwnsAsync", "auth.IsAuthenticated", "AuthSession.ResolveToken",

        // ─── ‏ADR-004: حارِسٌ لا جَلسَةَ خَلفَه — تَوقيعُ المُرسِل ─────
        // نُقطَةُ رِسالَةِ PayPal **لا يُمكِن أَن تَحمِلَ حارِسَ
        // جَلسَة**: مُنادِيها خادِمُ PayPal لا مُتَصَفِّحُ إنسان، ولا
        // كوكي لَه ولا مِفتاحَ API. وحارِسُها الفِعليُّ **تَوقيعٌ
        // مُعَمّى يُتَحَقَّقُ مِنه عِندَ PayPal نَفسِها** قَبلَ أَن
        // يُقرَأَ الجِسمُ كَبَيانات — وهُوَ أَشَدُّ لا أَضعَف: لا
        // يُزَوَّر بِسَرِقَةِ كوكي.
        //
        // ويُسَجَّل هُنا لِلسَبَب نَفسِه الَّذي سُجِّلَ لَه
        // `RequireListingOwner` و`RequireApiKey`: **رَمزُ حارِسٍ لا
        // يَعرِفُه الفاحِصُ حارِسٌ لا يُرى**. والرَمزُ اسمُ دالَّةٍ
        // نَقِيَّةٍ بِعَينِها (‏`PayPalBillingPolicy.Gate`)، لا سِلسِلَةٌ
        // عامَّة — فَلا يُمَرِّرُ غَيرَها.
        //
        // **والتَرتيبُ مَفروضٌ فَوقَ ذلك** بِفَحصِ «الحارِسُ يَسبِقُ
        // أَوَّلَ كِتابَة» أَدناه، وهُوَ ما يَجعَل «صِفرُ كِتابَةٍ عِندَ
        // تَوقيعٍ فاشِل» مَقيساً لا مَوعوداً.
        "PayPalBillingPolicy.Gate",

        // ─── ونَظيرَتُها عِندَ Paddle — **نَفسُ القَرارِ لا تَثبيتٌ
        //     جَديد** ────────────────────────────────────────────────
        // نُقطَةُ `‏/api/billing/paddle` مُنادِيها خادِمُ Paddle، ولا
        // كوكي لَه ولا مِفتاحَ API. وحارِسُها **بَصمَةُ
        // <c>HMAC-SHA256</c> تُحسَبُ عِندَنا** على الجِسمِ الخامِّ
        // قَبلَ أَن يُقرَأَ كَبَيانات — <b>وهي أَشَدُّ مِن حارِسِ
        // جارَتِها لا أَضعَف</b>: تِلكَ تَسأَل مُضيفَ PayPal
        // فَتَعتَمِد على شَبَكَةٍ ومُضيفٍ ثالِث، وهذِه لا تُغادِر
        // العَمَلِيَّة.
        //
        // والرَمزُ اسمُ دالَّةٍ نَقِيَّةٍ بِعَينِها لا سِلسِلَةٌ
        // عامَّة، والتَرتيبُ مَفروضٌ فَوقَه بِفَحصِ «الحارِسُ يَسبِقُ
        // أَوَّلَ كِتابَة».
        "PaddleWebhookGuard.Gate",

        // ─── ‏ADR-033 §٢‑ج‑أ: سُلطَةُ قَبولِ العَرضِ في تَوقيعِ الخِدمَة ──────
        // نُقطَةُ `/{slug}/deals/{id:guid}/accept` كانَت تَحمِلُ
        // `AuthSession.ResolveToken` **فَعَدَّها هذا الفاحِصُ مَحروسَة** — وهي
        // تُعَيِّنُ الناقِرَ نَفسَه طَرَفاً مُقابِلاً لِأَيِّ صَفقَة (موجَزُ
        // الفَجَوات P0-3). فَالجَلسَةُ تَقولُ «مَن أَنت» ولا تَقولُ «أَأَنتَ
        // مالِكُ الإعلان». الحارِسُ الحَقيقيُّ سِجِلُّ السُلطَةِ
        // `DealAcceptor` الَّذي يَطلُبُه تَوقيعُ `DealsService.AcceptAsync`
        // ولا يُنادى بِدونِه، وقَرارُه في `DealAcceptAuthorization.Validate`
        // النَقِيَّة — عَلى نَسَقِ `DealCanceller`.
        //
        // ويُسَجَّلُ هُنا لِلسَبَبِ نَفسِه الَّذي سُجِّلَ لَه
        // `PayPalBillingPolicy.Gate`: **رَمزُ حارِسٍ لا يَعرِفُه الفاحِصُ
        // حارِسٌ لا يُرى**. والرَمزُ اسمُ النَوعِ بِعَينِه لا سِلسِلَةٌ عامَّة.
        // وبُرهانُ أَنّ النُقطَةَ تَمُرُّ بِه لا حَولَه في
        // `DealAcceptAuthorizationTests` (فَحصٌ أَحمَرَّ أَوَّلاً).
        "Services.Deals.DealAcceptor(",

        // ─── ‏ADR-033 الخُطوَة 3 (‏2026-10-12): سُلطَةُ الشَحنِ في تَوقيعِ الخِدمَة ─────────────
        // نُقطَةُ `/{slug}/deals/{id:guid}/ship` تُنشِئُ طَلَباً بِحِسابِ البائِعِ عِندَ OTO ثُمَّ تُحَرِّكُ
        // الصَفقَة. والجَلسَةُ تَقولُ «مَن أَنت» وَحدَها؛ والحارِسُ الحَقيقيُّ سِجِلُّ السُلطَةِ `DealShipper`
        // في تَوقيعِ `DealShipping.ShipAsync`، وقَرارُه في `DealShipAuthorization.Validate` النَقِيَّة — بِهُوِيَّةِ
        // مالِكِ الإعلانِ مِن دالَّةِ `/accept` نَفسِها، لا أُنبوبٌ جَديد. ويُسَجَّلُ هُنا لِلسَبَبِ نَفسِه:
        // **رَمزُ حارِسٍ لا يَعرِفُه الفاحِصُ حارِسٌ لا يُرى**. وبُرهانُ أَنّ البَوّابَةَ تَسبِقُ OTO في `DealShipTests`.
        "Services.Deals.DealShipper(",

        // ─── ‏ADR-033 الخُطوَة 3 (‏2026-10-12): وارِدُ حالِ الطَلَبِ مِن OTO — بَوّابَةٌ نَقِيَّةٌ بِاسمِها ───────────────
        // نُقطَةُ `/{slug}/shipping/oto/order-status` مُنادِيها خادِمُ OTO، ولا كوكي لَه. وحارِسُها `OtoOrderStatusGate.Admit`
        // — عَلى سابِقَةِ `PaddleWebhookGuard.Gate` في الشَكل — و`DealShipping.ReceiveAsync` لا يُنادى إلّا بِإشعارٍ أَخرَجَتهُ
        // (مُنشِئُه داخِلِيّ). **والمَضمونُ أَشَدُّ مِن تَوقيع**: الإشعارُ إشارَةٌ، وحالُ الطَلَبِ يُسأَلُ عَنه OTO بِرَمزِ البائِعِ
        // قَبلَ أَيِّ كِتابَة (‏ADR-033 §٢‑هـ القاعِدَة ٦). والبُرهانُ في `OtoOrderStatusTests`.
        "Services.Deals.OtoOrderStatusGate.Admit(",

        // ─── ‏ADR-033 الخُطوَة 7 (تَيّارُ S8، ‏2026-10-12): هُوِيَّةُ البائِعِ في المَتجَر — بَوّابَةٌ نَقِيَّةٌ بِاسمِها ───────────────
        // نُقطَةُ `/{slug}/me/shipping/oto` يَحفَظُ بِها البائِعُ رَمزَ حِسابِه عِندَ OTO ويُسَجَّلُ وارِدُه. والجَلسَةُ تَقولُ «مَن أَنت»
        // وَحدَها؛ والحارِسُ سِجِلُّ السُلطَةِ `ListingVendor` — لا يُبنى إلّا بِـ`ListingVendorGate.Admit` بِعَدِّ إعلاناتِه في المَتجَر
        // (‏ADR-033 §٢‑ج‑٣: «حارسًا مسمًّى يُعلَنُ في توقيعِ نقطتَي الخطوتَين 7 و8») — ويَطلُبُه تَوقيعُ `VendorShippingService.SaveTokenAsync`.
        // **ولا حارِسَ إدارَةِ مَنَصَّة**: البائِعُ لَيسَ مُديراً. والبُرهانُ أَنّ البَوّابَةَ تَسبِقُ الحَقلَ في `VendorShippingTests`.
        "Services.Shipping.ListingVendorGate.Admit(",

        // ─── مُراجَعَةُ تَيّارِ S8، الجولة 2 (‏2026-10-12؛ ‏ADR-033 §٧‑ف): جِهَةُ تَواصُلِ البائِعِ المُتَحَقَّقُ مِنها — بَوّابَةٌ نَقِيَّةٌ بِاسمِها ───────
        // نُقطَةُ `/{slug}/me/shipping/oto/register` تُرسِلُ إلى OTO بِالرَمزِ السوقِيِّ بَريداً وجَوّالاً — و«الفِعلُ فِعلُه» لا يَصدُقُ إلّا بِجِهَةِ تَواصُلٍ
        // تَحَقَّقنا أَنَّها لِصاحِبِ الجَلسَة. فَبَعدَ `ListingVendorGate.Admit` تَبني `VendorContactGate.Admit` مِن وَثيقَتِه سِجِلَّ السُلطَةِ `VendorRegistrant`
        // الَّذي يَطلُبُه تَوقيعُ `VendorShippingService.RegisterAsync` — قَبلَ قِراءَةِ الاستِمارَة. ويُسَجَّلُ هُنا لِلسَبَبِ نَفسِه: **رَمزُ حارِسٍ لا يَعرِفُه
        // الفاحِصُ حارِسٌ لا يُرى**. والبُرهانُ في `VendorShippingTests` (التَرتيبُ في الجِسم، والبَوّابَةُ مُوجَباً وسالِباً).
        "Services.Shipping.VendorContactGate.Admit(",

        // ─── ‏ADR-032 الخُطوَة 9 (تَيّارُ S9، ‏2026-10-12): الدافِعُ في تَوقيعِ الخِدمَة ───────────────────────────────────────────────
        // نُقطَةُ `/{slug}/payments/verify` تَفُكُّ مِفتاحَ المَتجَرِ وتَسأَلُ بَوّابَتَه عَن فاتورَةِ صَفقَة. والجَلسَةُ تَقولُ «مَن أَنت» وَحدَها؛
        // والحارِسُ سِجِلُّ السُلطَةِ `DealPayer` — يُبنى مِن الجَلسَةِ **قَبلَ قِراءَةِ الحَقل** — ويَطلُبُه تَوقيعُ `GatewayPayments.VerifyAsync`،
        // وقَرارُه في `DealPayAuthorization.Validate` النَقِيَّةِ قَبلَ فَكِّ المِفتاحِ وقَبلَ البَوّابَة. ويُسَجَّلُ هُنا لِلسَبَبِ نَفسِه: **رَمزُ حارِسٍ
        // لا يَعرِفُه الفاحِصُ حارِسٌ لا يُرى**. والبُرهانُ سُلوكاً في `TenantGatewayEndpointsTests` (جِسمٌ لَيسَ استِمارَةً بِلا جَلسَةٍ ⇒ ‏302 لا ‏500).
        // ‏(‏2026-10-24 — تَيّارُ S16، يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) والجُملَةُ قَبلَ `88a424d`: مُنذُه **استِمارَةٌ** بِلا جَلسَةٍ ⇒ ‏302 (المِجَسُّ صارَ
        // استِمارَة)، وجِسمٌ JSON ⇒ ‏415 مِن `.RequireFormBody()` قَبلَ الحارِس — والتَرتيبُ قَرارٌ مُسَمّى في ‏ADR-037.
        "Services.Deals.DealPayer(",
    };

    // ─── ‏2026-10-24 (تَيّارُ S15 البَند C): بَوّابَةُ نَوعِ الجِسم — مُسَجَّلَةٌ هُنا ولَيسَت حارِسَ تَخويل ─────────────────────
    // ‏`.RequireFormBody()` تَرُدُّ ‏415 لِما لَيسَ استِمارَةً قَبلَ أَيِّ `req.Form[…]` (كانَ ‏500). **ولا تُضافُ إلى `ChainGuards`**:
    // رَمزٌ في تِلكَ القائِمَةِ يَجعَلُ نُقطَةً بِلا حارِسٍ غَيرِه «مَحروسَة» — وهي تَسأَلُ نَوعَ الجِسمِ لا صاحِبَه. فَتُفحَصُ بِصَفٍّ مُستَقِلٍّ
    // بِالماسِحِ نَفسِه، والتَصنيفُ («تَقرَأُ الاستِمارَة» و«تَقبَلُ JSON أَيضاً») مِن `FormBodyGateTests` لا نُسخَةٌ ثانِيَة.

    /// <summary><b>كُلُّ نُقطَةِ كِتابَةٍ تَقرَأُ الاستِمارَةَ تُعلِنُ بَوّابَةَ نَوعِ الجِسم</b> — إلّا ما يَقبَلُ JSON بِقَصدٍ ويَسأَلُ
    /// <c>HasFormContentType</c> قَبلَ القِراءَة. ويُطبَعُ العَدّ، والصِفرُ يُحمِر (القاعِدَة ١٠).</summary>
    [Fact]
    public void Every_form_reading_write_endpoint_declares_the_form_body_gate()
    {
        var form = MinimalApiWriteEndpoints().Where(e => FormBodyGateTests.ReadsForm(e.Body)).ToList();
        Assert.True(form.Count > 50, $"أَداةٌ عَمياء: {form.Count} نُقطَةً تَقرَأُ الاستِمارَة.");
        var missing = form
            .Where(e => !FormBodyGateTests.AcceptsJsonToo(e.Body) && !e.Body.Contains(".RequireFormBody(", StringComparison.Ordinal))
            .Select(e => $"{e.File}: {e.Route}").ToList();
        Assert.True(missing.Count == 0,
            $"نُقطَةٌ تَقرَأُ الاستِمارَةَ بِلا .RequireFormBody() ({form.Count} مَفحوصَة) — جِسمٌ لَيسَ استِمارَةً يَرمي فَيُرَدُّ 500:\n"
          + string.Join("\n", missing));
        Assert.DoesNotContain(".RequireFormBody(", ChainGuards);
    }

    // ─── ‏2026-10-24 (مُراجَعَةُ تَيّارِ S15 الجَولَة 1): بابُ السَلَّة — مُسَجَّلٌ هُنا ولَيسَ حارِسَ تَخويل ─────────────────────────────
    // ‏`CartDoor.Opens` (البَند L، ‏ADR-034 §٣ البَند ٦) تَسأَلُ نَمَطَ المَتجَرِ لا صاحِبَ الطَلَب، فَلا تُضافُ إلى `BodyGuards` ولا `ChainGuards`:
    // رَمزٌ هُناكَ يَجعَلُ نُقطَةً بِلا حارِسٍ غَيرِه «مَحروسَة». وكانَت سَطراً في جِسمِ `cart/add` لا يَراه ماسِحُ الحُرّاس، وصَفُّها في `CartDoorTests`
    // وَحدَه بِمَسارٍ مَكتوب — فَنُقطَةٌ ثانِيَةٌ تَلِدُ سَلَّةً غَداً كانَت تَمُرُّ بِلا سُؤال. **فَالفَحصُ بِالفِعلِ لا بِالمَسار**: كُلُّ نُقطَةِ كِتابَةٍ تَلِدُ سَلَّةً
    // (`new ACommerce.Kit.Cart.Cart`) تَسأَلُ البابَ بَعدَ الهُوِيَّةِ وقَبلَ أَوَّلِ قِراءَةٍ أَو كِتابَة؛ وما يُعَدِّلُ سَلَّةً قائِمَةً مُثَبَّتٌ بِسَبَبِه (قَرارُ
    // «أ» S15·١٠(٣))، والقائِمَةُ ثُنائِيَّةُ الاتِّجاه. و`CartHandlers.Handle(AddToCart …)` في العُدَّةِ يَلِدُ سَلَّةً أَيضاً بِلا مَسارِ HTTP وبِصِفرِ مُرسِل
    // (`grep AddToCart` = التَعريفُ والمُعالِجُ وَحدَهُما) — فَلَيسَ باباً هُنا.

    private const string CartDocument = "ACommerce.Kit.Cart.Cart";
    private const string CartBirth = "new ACommerce.Kit.Cart.Cart";
    private const string CartDoorCall = "CartDoor.Opens(";

    /// <summary>ما يَقرَأُ أَو يَكتُبُ قَبلَه البابُ في نُقطَةٍ تَلِدُ سَلَّة.</summary>
    private static readonly string[] CartBirthTouches = { "LoadAsync<", "req.Form[", "SaveChangesAsync", ".Store(", "Events.Append", "Events.StartStream" };

    /// <summary>نُقطَةٌ تُعَدِّلُ سَلَّةً قائِمَةً بِلا بابِ السَلَّة — مُثَبَّتَةٌ بِسَبَبِها.</summary>
    private sealed record CartEdit(string Route, string WhyAr);

    private static readonly CartEdit[] PinnedCartEdits =
    {
        new("/{slug}/cart/{listingId:guid}/qty",
            "تُعَدِّلُ كَمِّيَّةَ سَلَّةٍ قائِمَة؛ وسَلَّةٌ كُتِبَت في مَتجَرٍ لا يَبيعُ بِها قَبلَ البابِ تَبقى قابِلَةً لِلتَعديل — قَرارُ مُنتَجٍ في «أ» S15·١٠(٣)."),
        new("/{slug}/cart/clear",
            "تَحذِفُ السَلَّة — إغلاقُها في مَتجَرٍ لا يَبيعُ بِها يُبقي ما لا تَعرِضُه الواجِهَةُ بِلا مَخرَج."),
        new("/{slug}/checkout/submit",
            "تُحَوِّلُ سَلَّةً قائِمَةً إلى صَفَقاتٍ ثُمَّ تَحذِفُها — قَرارُ مُنتَجٍ في «أ» S15·١٠(٣): أَيُحتَرَمُ ما في سَلَّةِ العَميلِ أَم يُسقَط؟"),
    };

    /// <summary>كُلُّ نُقطَةٍ تَكتُبُ سَلَّة: الَّتي تَلِدُها وما تُخالِفُ مِنها، والَّتي تُعَدِّلُ قائِمَة.</summary>
    private static (List<string> Births, List<string> Edits, List<string> Violations) CartWriters(IEnumerable<Endpoint> endpoints)
    {
        var births = new List<string>();
        var edits = new List<string>();
        var violations = new List<string>();
        foreach (var e in endpoints)
        {
            if (!e.Body.Contains(CartDocument, StringComparison.Ordinal)) continue;
            if (!ContainsAny(e.Body, WriteCalls) && !e.Body.Contains($".Delete<{CartDocument}>", StringComparison.Ordinal)) continue;
            if (!e.Body.Contains(CartBirth, StringComparison.Ordinal)) { edits.Add(e.Route); continue; }

            births.Add(e.Route);
            var door = e.Body.IndexOf(CartDoorCall, StringComparison.Ordinal);
            var identity = FirstIndexOfAny(e.Body, BodyGuards);
            var touch = FirstIndexOfAny(e.Body, CartBirthTouches);
            if (door < 0) violations.Add($"{e.Route}: تَلِدُ سَلَّةً بِلا {CartDoorCall}");
            else if (identity < 0 || identity > door) violations.Add($"{e.Route}: البابُ قَبلَ الهُوِيَّة");
            else if (touch >= 0 && touch < door) violations.Add($"{e.Route}: قِراءَةٌ أَو كِتابَةٌ قَبلَ الباب");
        }
        return (births, edits, violations);
    }

    /// <summary><b>كُلُّ نُقطَةٍ تَلِدُ سَلَّةً تَسأَلُ بابَ السَلَّةِ بَعدَ الهُوِيَّةِ وقَبلَ أَيِّ قِراءَةٍ أَو كِتابَة</b> — والدالَّةُ نَفسُها الَّتي تَرسُمُ بِها صَفحَةُ
    /// الإعلانِ الاستِمارَة. وما يُعَدِّلُ سَلَّةً قائِمَةً هُوَ المُثَبَّتُ بِحَرفِه لا أَكثَرَ ولا أَقَلّ. ويُطبَعُ العَدّ، والصِفرُ يُحمِر (القاعِدَة ١٠).</summary>
    [Fact]
    public void Every_endpoint_that_births_a_cart_asks_the_cart_door_before_it_reads_or_writes()
    {
        var (births, edits, violations) = CartWriters(MinimalApiWriteEndpoints());
        output.WriteLine($"نِقاطٌ تَلِدُ سَلَّة: {births.Count} ({string.Join(" · ", births)}) · تُعَدِّلُ قائِمَة: {edits.Count} ({string.Join(" · ", edits)}) · مُخالَفات: {violations.Count}");

        Assert.True(births.Count > 0, "أَداةٌ عَمياء: صِفرُ نُقطَةٍ تَلِدُ سَلَّة.");
        Assert.True(violations.Count == 0, "بابُ السَلَّةِ غائِبٌ أَو مُتَأَخِّر:\n  " + string.Join("\n  ", violations));
        Assert.Equal(PinnedCartEdits.Select(p => p.Route).OrderBy(r => r, StringComparer.Ordinal),
            edits.OrderBy(r => r, StringComparer.Ordinal));
        Assert.All(PinnedCartEdits, p => Assert.False(string.IsNullOrWhiteSpace(p.WhyAr)));
        Assert.DoesNotContain(BodyGuards, g => g.Contains("CartDoor", StringComparison.Ordinal));
        Assert.DoesNotContain(ChainGuards, g => g.Contains("CartDoor", StringComparison.Ordinal));
    }

    /// <summary><b>والفاحِصُ يَحمَرّ</b> (القاعِدَة ١٠): أَربَعُ نِقاطٍ مَحقونَةٍ تَلِدُ سَلَّة — بِلا باب، وبِبابٍ بَعدَ الحِفظ، وبِبابٍ قَبلَ الهُوِيَّة،
    /// وسَليمَة — والثَلاثُ الأولى وَحدَها مُخالَفات؛ ونُقطَةٌ تُعَدِّلُ قائِمَةً تُعَدُّ تَعديلاً لا وِلادَة.</summary>
    [Fact]
    public void The_cart_door_scanner_catches_an_injected_birth_without_the_door_late_or_before_identity()
    {
        const string id = "var parsed = AuthHandlers.ParseToken(AuthSession.ResolveToken(req, slug)); ";
        const string door = "if (!Services.Patterns.CartDoor.Opens(profile)) return Results.Redirect(\"x\"); ";
        const string birth = "var cart = await s.LoadAsync<ACommerce.Kit.Cart.Cart>(userId) ?? new ACommerce.Kit.Cart.Cart { Id = userId }; s.Store(cart); await s.SaveChangesAsync(); ";
        var injected = new[]
        {
            new Endpoint("/x/no-door", id + birth, "injected.cs"),
            new Endpoint("/x/late", id + birth + door, "injected.cs"),
            new Endpoint("/x/before-identity", door + id + birth, "injected.cs"),
            new Endpoint("/x/sound", id + door + birth, "injected.cs"),
            new Endpoint("/x/edit", id + "var cart = await s.LoadAsync<ACommerce.Kit.Cart.Cart>(userId); s.Store(cart); await s.SaveChangesAsync();", "injected.cs"),
        };
        var (births, edits, violations) = CartWriters(injected);
        output.WriteLine($"مَحقونَة: وِلادات {births.Count} · تَعديلات {edits.Count} · مُخالَفات {violations.Count}:\n  " + string.Join("\n  ", violations));

        Assert.Equal(new[] { "/x/no-door", "/x/late", "/x/before-identity", "/x/sound" }, births);
        Assert.Equal(new[] { "/x/edit" }, edits);
        Assert.Equal(3, violations.Count);
        Assert.DoesNotContain(violations, v => v.StartsWith("/x/sound", StringComparison.Ordinal));
    }

    // ─── ‏2026-10-24 (مُراجَعَةُ تَيّارِ S15 الجَولَة 2، minor عَلى 428fc59): والماسِحُ يَرى السَلَّةَ بِاسمِها الكامِلِ وَحدَه ───────────────
    // ‏`CartWriters` يَعُدُّ نُقطَةً تَكتُبُ سَلَّةً إن حَمَلَ جِسمُها `ACommerce.Kit.Cart.Cart`، فَنُقطَةٌ في مِلَفٍّ يَستَورِدُ `using ACommerce.Kit.Cart;` (أَو
    // يَسكُنُ فَضاءَه، أَو يُسَمّيه بِاسمٍ مُستَعار) وتَكتُبُ `new Cart { … }` و`LoadAsync<Cart>` — وهذا شَكلُ `CartHandlers.cs` في العُدَّة — لا هِيَ وِلادَةٌ
    // ولا تَعديل، فَلا تَراها القائِمَةُ الثُنائِيَّة. فَبَدَلَ مُطارَدَةِ الاسمِ المُختَصَرِ في كُلِّ جِسمٍ يُمنَعُ مَنبَعُه: لا مِلَفَّ يَحمِلُ نُقطَةً (minimal API أَو
    // ‏Wolverine) يُسَمّي فَضاءَ السَلَّةِ قَصيراً. وفاحِصُ الوِلادَةِ يَبقى بِحَرفِه.
    //
    // ‏(‏2026-10-24، تَيّارُ S16 — C(28)، مُراجَعَةُ تَيّارِ S15 الجَولَة 3؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): **وفاتَته خَمسُ صُوَر** — المَقيسُ في
    // المُستَودَعِ اليَومَ صِفرٌ مِن كُلِّها، فَلا نُقطَةَ تَعبُرُ مِنها، لكِنَّ الصَفَّ يَقولُ «كُلَّ طَريق»: (١) `global using ACommerce.Kit.Cart;`
    // (التَعبيرُ كانَ يَشتَرِطُ أَن يَبدَأَ السَطرُ بِـ`using`)؛ (٢) `global using` في مِلَفٍّ غَيرِ مِلَفِّ نِقاطٍ مِن المَشروعِ نَفسِه (`GlobalUsings.cs`)، أَو
    // ‏`<Using Include="ACommerce.Kit.Cart" />` في `csproj`/`Directory.Build.*` — يَسري عَلى المَشروعِ كُلِّه ولا يُقرَأُ إلّا هُناك؛ (٣) `using ACommerce.Kit;`
    // ثُمَّ `new Cart.Cart()` — وأَوسَعُ مِنه **بِلا استيرادٍ أَصلاً**: مِلَفٌّ في فَضاءٍ تَحتَ `ACommerce.Kit` يَرى `Cart.Cart`، وتَحتَ `ACommerce` يَرى
    // ‏`Kit.Cart.Cart` (بَحثُ C# في الفَضاءاتِ الأُمّ)؛ (٤) `using C = global::ACommerce.Kit.Cart.Cart;`. فَالتَعبيرُ الآنَ يَمسِكُ استيرادَ أَيِّ بادِئَةٍ مِن
    // ‏`ACommerce.Kit.Cart.Cart` أَو اسمَها المُستَعار (بِـ`global`/`static`/`global::` اختِيارِيَّة)، والفَضاءَ، و**الاسمَ المُؤَهَّلَ جُزئِيّاً** في الكود
    // (`Cart.Cart` أَو `Kit.Cart.Cart` لا تَسبِقُهُما نُقطَةٌ ولا مُعَرِّف)؛ ومَمَرٌّ ثانٍ يَقرَأُ كُلَّ `.cs` في كُلِّ مَشروعٍ يَحمِلُ نِقاطاً بَحثاً عَن
    // ‏`global using`، ومِلَفّاتِ `MSBuild` فَوقَه بَحثاً عَن `<Using Include=…>`.

    /// <summary>كُلُّ بادِئَةٍ يَصيرُ بِها اسمُ السَلَّةِ قَصيراً — مِن <c>ACommerce</c> إلى <c>ACommerce.Kit.Cart.Cart</c>.</summary>
    private const string CartPrefix = @"(?:global::)?ACommerce(?:\.Kit(?:\.Cart(?:\.Cart)?)?)?";

    private static readonly System.Text.RegularExpressions.Regex CartShortName = new(
        @"^\s*(?:global\s+)?using\s+(?:static\s+)?(?:\w+\s*=\s*)?" + CartPrefix + @"\s*;"
      + @"|^\s*namespace\s+ACommerce\.Kit\.Cart\b"
      + @"|(?<![\w.:])(?:Kit\.)?Cart\.Cart\b",
        System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>ما يَسري عَلى المَشروعِ كُلِّه مِن مِلَفٍّ واحِد: <c>global using</c> لِبادِئَةٍ مِن السَلَّة.</summary>
    private static readonly System.Text.RegularExpressions.Regex GlobalCartUsing = new(
        @"^\s*global\s+using\s+(?:static\s+)?(?:\w+\s*=\s*)?" + CartPrefix + @"\s*;",
        System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>ومِن مِلَفِّ <c>MSBuild</c>: <c>&lt;Using Include="…"&gt;</c> يولِّدُ <c>global using</c> في كُلِّ مِلَفٍّ مِن المَشروع.</summary>
    private static readonly System.Text.RegularExpressions.Regex MsBuildCartUsing = new(
        @"<Using\s+Include\s*=\s*""" + CartPrefix + @"""",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string StripXmlComments(string xml) =>
        System.Text.RegularExpressions.Regex.Replace(xml, "<!--.*?-->", "", System.Text.RegularExpressions.RegexOptions.Singleline);

    /// <summary>ما يُسَمّي السَلَّةَ قَصيراً عَلى مُستَوى المَشروع: <c>global using</c> في أَيِّ <c>.cs</c> مِنه، و<c>&lt;Using&gt;</c> في مِلَفّاتِ <c>MSBuild</c> فَوقَه.</summary>
    private static List<string> ProjectWideShortCartNames(IEnumerable<(string Path, string Text)> csFiles, IEnumerable<(string Path, string Text)> msBuildFiles) =>
        csFiles.Where(f => GlobalCartUsing.IsMatch(StripComments(f.Text))).Select(f => f.Path)
            .Concat(msBuildFiles.Where(f => MsBuildCartUsing.IsMatch(StripXmlComments(f.Text))).Select(f => f.Path))
            .ToList();

    /// <summary>مَشروعاتُ مِلَفّاتِ النِقاط: أَقرَبُ <c>csproj</c> فَوقَ كُلِّ مِلَف، وكُلُّ <c>.cs</c> تَحتَه (بِلا <c>bin</c>/<c>obj</c>)، ومِلَفّاتُ <c>MSBuild</c>
    /// الَّتي يَقرَؤُها — <c>csproj</c> نَفسُه و<c>Directory.*.props</c>/<c>Directory.*.targets</c> (ومِنها <c>Directory.Packages.props</c>) في مَجلِدِه وكُلِّ
    /// أُمٍّ حَتّى جَذرِ المُستَودَع.</summary>
    private static (List<string> Projects, List<(string Path, string Text)> Cs, List<(string Path, string Text)> MsBuild) EndpointProjects(IEnumerable<string> endpointFiles)
    {
        var root = Path.GetFullPath(ThemeZeroEquivalenceTests.RepoRoot);
        var projects = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var f in endpointFiles)
        {
            var dir = Path.GetDirectoryName(Path.Combine(root, f.Replace('/', Path.DirectorySeparatorChar)));
            while (dir is not null && dir.Length >= root.Length && Directory.GetFiles(dir, "*.csproj").Length == 0) dir = Path.GetDirectoryName(dir);
            Assert.True(dir is not null && dir.Length >= root.Length, $"مِلَفُّ نِقاطٍ بِلا مَشروع: {f}");
            projects.Add(dir!);
        }
        static bool Built(string p) => p.Split(Path.DirectorySeparatorChar).Any(seg => seg is "bin" or "obj");
        var cs = projects.SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(p => !Built(p)).Distinct(StringComparer.Ordinal).Select(p => (Rel(p), File.ReadAllText(p))).ToList();
        var msBuild = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var d in projects)
        {
            foreach (var proj in Directory.GetFiles(d, "*.csproj")) msBuild.Add(proj);
            for (var up = d; up is not null && up.Length >= root.Length; up = Path.GetDirectoryName(up))
                foreach (var pattern in new[] { "Directory.*.props", "Directory.*.targets" })
                    foreach (var f in Directory.GetFiles(up, pattern)) msBuild.Add(f);
        }
        return (projects.Select(Rel).ToList(), cs, msBuild.Select(p => (Rel(p), File.ReadAllText(p))).ToList());
    }

    /// <summary>أَيُسَمّي هذا المَصدَرُ فَضاءَ السَلَّةِ بِاسمٍ قَصيرٍ لا يَراه <see cref="CartWriters"/>؟ — بَعدَ تَبييضِ التَعليقات.</summary>
    private static bool NamesTheCartShort(string source) => CartShortName.IsMatch(StripComments(source));

    /// <summary><b>لا مِلَفَّ نِقاطٍ يُسَمّي السَلَّةَ قَصيراً</b> — فَكُلُّ قِراءَةٍ أَو كِتابَةٍ لِسَلَّةٍ في جِسمِ نُقطَةٍ تَحمِلُ الاسمَ الكامِلَ الَّذي يَعُدُّه
    /// ماسِحُ بابِ السَلَّة. ويَطبَعُ عَدَدَ المِلَفّاتِ المَفحوصَة، والصِفرُ يُحمِر (القاعِدَة ١٠).</summary>
    [Fact]
    public void No_endpoint_file_names_the_cart_by_a_short_name_the_cart_door_scanner_cannot_see()
    {
        var files = AllMinimalApiEndpoints().Concat(WolverineWriteEndpoints()).Select(e => e.File)
            .Distinct(StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToList();
        var shortNamed = files.Where(f => NamesTheCartShort(
            File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, f.Replace('/', Path.DirectorySeparatorChar))))).ToList();
        output.WriteLine($"مِلَفّاتُ نِقاطٍ مَفحوصَة: {files.Count} · تُسَمّي السَلَّةَ قَصيراً: {shortNamed.Count} ({string.Join(" · ", shortNamed)})");

        Assert.True(files.Count > 5, $"أَداةٌ عَمياء: {files.Count} مِلَفّاً.");
        Assert.Empty(shortNamed);
    }

    /// <summary><b>ولا مَشروعَ نِقاطٍ يُسَمّي السَلَّةَ قَصيراً لِكُلِّ مِلَفّاتِه</b> (‏2026-10-24، تَيّارُ S16 — C(28)): <c>global using</c> في أَيِّ <c>.cs</c>
    /// مِن مَشروعٍ يَحمِلُ نِقاطاً، أَو <c>&lt;Using Include&gt;</c> في <c>csproj</c>/<c>Directory.Build.*</c> فَوقَه، يَصِلُ إلى مِلَفِّ النِقاطِ بِلا سَطرٍ فيه.
    /// ويَطبَعُ عَدَدَ المَشروعاتِ والمِلَفّات، والصِفرُ يُحمِر (القاعِدَة ١٠).</summary>
    [Fact]
    public void No_endpoint_project_names_the_cart_short_for_all_its_files()
    {
        var files = AllMinimalApiEndpoints().Concat(WolverineWriteEndpoints()).Select(e => e.File).Distinct(StringComparer.Ordinal).ToList();
        var (projects, cs, msBuild) = EndpointProjects(files);
        var found = ProjectWideShortCartNames(cs, msBuild);
        output.WriteLine($"مَشروعاتُ نِقاط: {projects.Count} ({string.Join(" · ", projects)}) · ‏.cs مَقروءَة: {cs.Count} · مِلَفّاتُ MSBuild: {msBuild.Count} "
            + $"({string.Join(" · ", msBuild.Select(m => m.Path))}) · تُسَمّي السَلَّةَ قَصيراً: {found.Count} ({string.Join(" · ", found)})");

        Assert.True(projects.Count > 1, $"أَداةٌ عَمياء: {projects.Count} مَشروعاً.");
        Assert.True(cs.Count > files.Count, $"أَداةٌ عَمياء: {cs.Count} مِلَفَّ .cs لِـ{files.Count} مِلَفَّ نِقاط.");
        Assert.True(msBuild.Count >= projects.Count, $"أَداةٌ عَمياء: {msBuild.Count} مِلَفَّ MSBuild لِـ{projects.Count} مَشروعاً.");
        Assert.Empty(found);
    }

    /// <summary><b>والفاحِصُ يَحمَرّ</b>: الاستيرادُ والاسمُ المُستَعارُ والفَضاءُ وما تَحتَه (<c>Cart.Server</c> يَرى <c>Cart</c> قَصيراً) يُمسَكون، والاسمُ الكامِلُ والتَعليقُ وفَضاءٌ يُشبِهُه اسماً لا.</summary>
    [Fact]
    public void The_short_cart_name_check_catches_an_import_an_alias_and_the_namespace_and_passes_the_full_name()
    {
        Assert.True(NamesTheCartShort("using System;\nusing ACommerce.Kit.Cart;\nclass X { }"));
        Assert.True(NamesTheCartShort("using C = ACommerce.Kit.Cart.Cart;\nclass X { }"));
        Assert.True(NamesTheCartShort("namespace ACommerce.Kit.Cart.Server;\nclass X { }"));
        Assert.False(NamesTheCartShort("class X { void M() { var c = new ACommerce.Kit.Cart.Cart(); } }"));
        Assert.False(NamesTheCartShort("// using ACommerce.Kit.Cart;\nclass X { }"));
        Assert.False(NamesTheCartShort("using ACommerce.Kit.CartExtras;\nclass X { }"));

        // ‏(‏2026-10-24، تَيّارُ S16 — C(28)) الصُوَرُ الَّتي فاتَته، وما يُشبِهُها ولَيسَ مِنها.
        var caught = new[]
        {
            "global using ACommerce.Kit.Cart;\nclass X { }",                                     // (١)
            "  global   using global::ACommerce.Kit.Cart;\nclass X { }",
            "using ACommerce.Kit;\nclass X { object M() => new Cart.Cart(); }",                  // (٣) بِالاستيراد
            "using ACommerce;\nclass X { object M() => new Kit.Cart.Cart(); }",
            "using K = ACommerce.Kit;\nclass X { }",
            "namespace ACommerce.Kit.Tenants.Server;\nclass X { object M() => new Cart.Cart(); }", // (٣) بِلا استيراد: الفَضاءُ الأُمّ
            "namespace ACommerce.Templates.X;\nclass X { Task M(IQuerySession s) => s.LoadAsync<Kit.Cart.Cart>(1); }",
            "using C = global::ACommerce.Kit.Cart.Cart;\nclass X { }",                           // (٤)
            "using static ACommerce.Kit.Cart.Cart;\nclass X { }",
        };
        var passed = new[]
        {
            "global using ACommerce.Kit.CartExtras;\nclass X { }",
            "using ACommerce.Kitchen;\nclass X { }",
            "using ACommerce.Kit.Tenants;\nclass X { }",
            "using C = global::ACommerce.Kit.Cart.CartHandlers;\nclass X { }",
            "class X { object M() => new global::ACommerce.Kit.Cart.Cart(); }",
            "class X { object M() => new ACommerce.Kit.Cart.CartLine(); }",
            "class X { object M() => cart.CartLines; }",
            "// global using ACommerce.Kit.Cart;\nclass X { /* new Cart.Cart() */ }",
        };
        var missed = caught.Where(c => !NamesTheCartShort(c)).ToList();
        var falseHits = passed.Where(NamesTheCartShort).ToList();
        output.WriteLine($"يُمسَك: {caught.Length - missed.Count}/{caught.Length} · يَمُرّ: {passed.Length - falseHits.Count}/{passed.Length}");
        Assert.Empty(missed);
        Assert.Empty(falseHits);
    }

    /// <summary><b>والمَمَرُّ عَلى مُستَوى المَشروعِ يَحمَرّ</b> (‏2026-10-24، تَيّارُ S16 — C(28)، الصورَة ٢): <c>global using</c> في مِلَفٍّ لَيسَ مِلَفَّ نِقاط،
    /// و<c>&lt;Using Include&gt;</c> في <c>csproj</c> وفي <c>Directory.Build.props</c> يُمسَكون؛ و<c>using</c> غَيرُ عامٍّ في مِلَفٍّ آخَر (لا يَسري عَلى
    /// مِلَفِّ النِقاط)، وفَضاءٌ يُشبِهُه اسماً، والتَعليقُ لا.</summary>
    [Fact]
    public void The_project_wide_check_catches_a_global_using_in_another_file_and_an_msbuild_using()
    {
        var cs = new[]
        {
            ("p/GlobalUsings.cs", "global using ACommerce.Kit.Cart;\n"),
            ("p/Aliases.cs", "global using C = global::ACommerce.Kit.Cart.Cart;\n"),
            ("p/Other.cs", "using ACommerce.Kit.Cart;\nclass Other { }"),
            ("p/Extras.cs", "global using ACommerce.Kit.CartExtras;\n"),
            ("p/Commented.cs", "// global using ACommerce.Kit.Cart;\n"),
        };
        var msBuild = new[]
        {
            ("p/P.csproj", "<Project><ItemGroup><Using Include=\"ACommerce.Kit.Cart\" /></ItemGroup></Project>"),
            ("Directory.Build.props", "<Project><ItemGroup><Using Include=\"ACommerce.Kit\" Alias=\"K\" /></ItemGroup></Project>"),
            ("q/Q.csproj", "<Project><ItemGroup><Using Include=\"ACommerce.Kit.Tenants\" /></ItemGroup></Project>"),
            ("q/Directory.Build.targets", "<Project><!-- <Using Include=\"ACommerce.Kit.Cart\" /> --></Project>"),
        };
        var found = ProjectWideShortCartNames(cs, msBuild);
        output.WriteLine($"مَحقونَة: ‏{cs.Length} .cs و{msBuild.Length} MSBuild · مُمسَكَة: {found.Count} ({string.Join(" · ", found)})");
        Assert.Equal(new[] { "p/GlobalUsings.cs", "p/Aliases.cs", "p/P.csproj", "Directory.Build.props" }, found);
    }

    /// <summary>أَوَّل ما يُعَدّ كِتابَةً — لِفَحص «الحارِس يَسبِق».</summary>
    private static readonly string[] WriteCalls =
    {
        "SaveChangesAsync", "Events.Append", "Events.StartStream", ".Store(",
    };

    // ─── الفَحص ───────────────────────────────────────────────────────

    /// <summary>
    /// <para><b>كُلّ <c>MapPost</c>/<c>MapPut</c>/<c>MapDelete</c> يَحمِل
    /// حارِساً أَو يُثَبَّت.</b> الفَحص بِالفِعل لا بِالنِيَّة: كُلّ
    /// نُقطَةٍ فِعلُها كِتابَة تُسأَل، سَواءٌ لَمَسَت قاعِدَة البَيانات
    /// أَم لا — فَتَمييزُ «هَل تَكتُب فِعلاً؟» حُكمٌ، والحُكم هُوَ ما
    /// سَقَطَ أَوَّل مَرَّة.</para>
    /// </summary>
    [Fact]
    public void Every_minimal_api_write_endpoint_declares_a_guard()
    {
        var endpoints = MinimalApiWriteEndpoints().ToList();

        // عَدّاد: أَداةٌ تَفحَص صِفراً أَداةٌ عَمياء (القاعِدَة ١٠). ‏(‏2026-10-24، تَدقيقُ الإطلاق lens-security-5) والأَرضِيَّةُ ‏88 ← ‏119 —
        // المَقيسُ اليَوم — وتُطبَعُ: أَرضِيَّةٌ تَحتَ الواقِعِ بِثَلاثينَ نُقطَةً لا تَرى نُقطَةً تَختَفي.
        output.WriteLine($"نِقاطُ الكِتابَة: {endpoints.Count} · المُثَبَّتَةُ عامَّة: {PinnedPublic.Length}");
        Assert.True(endpoints.Count >= 119,
            $"أَداة عَمياء: وُجِدَت {endpoints.Count} نُقطَة كِتابَة — والمَقيس ‏119.");

        var pinned = PinnedPublic.Select(p => p.Route).ToHashSet(StringComparer.Ordinal);
        var breaches = endpoints
            .Where(e => !HasGuard(e.Body) && !pinned.Contains(e.Route))
            .Select(e => e.Route)
            .ToArray();

        Assert.True(breaches.Length == 0,
            "نُقطَة كِتابَة بِلا حارِس مُعلَن وغَير مُثَبَّتَة:\n  " +
            string.Join("\n  ", breaches) +
            "\nإمّا أَن تُحرَس بِقَرار نَظيرَتِها، أَو تُثَبَّت بِسَبَبِها في نَفس الكوميت.");
    }

    // ─── تَدقيقُ الإطلاق lens-security-5 — نُقطَةُ المُشاهَدَةِ عامَّةٌ ويُعَدُّ لَها حارِس (‏2026-10-24) ─────
    //
    // **المَقيس** (عِندَ `bb79574`): `POST /{slug}/listings/{id}/view` تَقرَأُ `AuthSession.ResolveToken` **لِنِسبَةِ المُشاهَدَةِ لا لِمَنعِها**
    // (`viewerId = null` بِلا رُجوع)، ورَمزُ القِراءَةِ ذاك في `BodyGuards` — فَيَعُدُّها الفاحِصُ مَحروسَةً ولا تُثَبَّتُ عامَّة. ثُمَّ
    // `Events.Append(ListingViewed)` لِكُلِّ طَلَبٍ بِلا حَدّ، وكُلُّ حَدَثٍ يُبطِّئُ كُلَّ عَرضٍ لاحِق (`AggregateStreamAsync<Listing>`). والأَرضِيَّةُ
    // ‏88 والمَقيسُ ‏119 ولا يُطبَعُ العَدَد. يُثَبَّتُ قَبلَ العِلاج (القاعِدَة ٣) ويَنقَلِبُ بِاسمِه.
    // **العِلاج** (‏2026-10-24): النِسبَةُ في `ListingViews.ViewerOf`، والنُقطَةُ في `PinnedPublic` بِسَبَبِها، و`ListingViews.Counts` (مُشاهَدَةٌ
    // واحِدَةٌ لِكُلِّ زائِرٍ في الإعلانِ في السّاعَة) قَبلَ القِراءَةِ والكِتابَة؛ والأَرضِيَّةُ ‏119 مَطبوعَة. الصَفُّ انقَلَبَ بِاسمِه.

    private const string ViewRoute = "/{slug}/listings/{id:guid}/view";

    private static string ViewBody()
    {
        var views = MinimalApiWriteEndpoints().Where(e => e.Route == ViewRoute).ToList();
        Assert.True(views.Count == 1, $"نُقاطُ المُشاهَدَة: {views.Count} — الأَداةُ عَمياء.");
        return views[0].Body;
    }

    /// <summary><b>انقَلَبَ بِاسمِه</b> (‏2026-10-24) مِن <c>Today_the_view_door_is_counted_guarded_by_a_token_read_and_appends_an_event_on_every_post</c>:
    /// لا رَمزَ حارِسٍ في الجِسم (النِسبَةُ في <c>ListingViews.ViewerOf</c>) فَهِيَ مُثَبَّتَةٌ عامَّةً بِسَبَبِها، و<c>ListingViews.Counts</c> يَسبِقُ
    /// القِراءَةَ والكِتابَة، ورَدُّه المُبكِّرُ بَينَهُما.</summary>
    [Fact]
    public void The_view_door_is_pinned_public_and_bounded_before_it_reads_or_appends()
    {
        var body = ViewBody();
        var writes = MinimalApiWriteEndpoints().Count();
        output.WriteLine($"نِقاطُ الكِتابَة: {writes} · المُشاهَدَةُ مَحروسَةٌ في عَينِ الفاحِص: {HasGuard(body)} · مُثَبَّتَةٌ عامَّة: {PinnedPublic.Any(p => p.Route == ViewRoute)}");
        Assert.False(HasGuard(body));
        Assert.DoesNotContain("AuthSession.ResolveToken", body, StringComparison.Ordinal);
        Assert.Contains(PinnedPublic, p => p.Route == ViewRoute);

        var bound = body.IndexOf("Services.ListingViews.Counts(", StringComparison.Ordinal);
        var early = body.IndexOf("return Results.NoContent();", bound + 1, StringComparison.Ordinal);
        var read = body.IndexOf("AggregateStreamAsync<Listing>(", StringComparison.Ordinal);
        var append = body.IndexOf("Events.Append(", StringComparison.Ordinal);
        Assert.True(bound > 0 && early > bound && read > early && append > read,
            $"الحَدّ@{bound} الرُجوع@{early} القِراءَة@{read} الكِتابَة@{append}");
        Assert.Single(Regex.Matches(body, @"Events\.Append\("));
    }

    // ─── ‏ADR-003: الكِتابَةُ في مَتجَرٍ تَسأَل عَن باقَتِه ────────────

    /// <summary>
    /// <para><b>كُلُّ نُقطَةِ كِتابَةٍ تَحتَ <c>/{slug}/</c> تُعلِن
    /// <c>RequireStoreWritable</c> — أَو تُثَبَّت بِسَبَبِها.</b> وهذا
    /// هُوَ الفَرقُ بَينَ «سِياسَةٍ مَكتوبَةٍ في وَثيقَة» و«حَدٍّ
    /// يُقاس»: مَتجَرٌ انتَهَت باقَتُه ونُقطَةٌ نُسِيَت ⇒ يَكتُب
    /// اللاعِبُ فيها ولا يَشتَكي شَيء (القاعِدَة ٢).</para>
    ///
    /// <para><b>والفَحصُ يَطبَع ما فَحَصَه ويَفشَل إن كانَ صِفراً</b>
    /// (القاعِدَة ١٠) — و«صِفرُ مُخالَفَة» بِلا عَدّادٍ لا يُمَيَّز مِن
    /// أَداةٍ عَمياء.</para>
    /// </summary>
    [Fact]
    public void Every_store_write_endpoint_declares_the_plan_gate()
    {
        var storeWrites = MinimalApiWriteEndpoints()
            .Where(e => e.Route.StartsWith("/{slug}/", StringComparison.Ordinal))
            .ToList();

        Assert.True(storeWrites.Count >= 40,
            $"أَداة عَمياء: وُجِدَت {storeWrites.Count} نُقطَةَ كِتابَةٍ في مَتجَر — والمَقيس ‏40 فَأَكثَر.");

        var pinned = PinnedUngatedStoreWrites.Select(p => p.Route).ToHashSet(StringComparer.Ordinal);
        var gated  = storeWrites.Where(e => e.Body.Contains(".RequireStoreWritable(", StringComparison.Ordinal))
                                .Select(e => e.Route).ToHashSet(StringComparer.Ordinal);

        Assert.True(gated.Count > 0, "أَداة عَمياء: صِفرُ نُقطَةٍ تُعلِن الحارِس.");

        var breaches = storeWrites
            .Where(e => !gated.Contains(e.Route) && !pinned.Contains(e.Route))
            .Select(e => e.Route).ToArray();

        Assert.True(breaches.Length == 0,
            $"نُقطَةُ كِتابَةٍ في مَتجَرٍ لا تَسأَل عَن باقَتِه ({gated.Count} مَحروسَة، " +
            $"{pinned.Count} مُثَبَّتَة، مِن {storeWrites.Count}):\n  " +
            string.Join("\n  ", breaches) +
            "\nإمّا `.RequireStoreWritable()` في التَوقيع، أَو سَطرٌ في " +
            "`PinnedUngatedStoreWrites` يَقول لِماذا — في نَفس الكوميت.");
    }

    /// <summary>ونِصفُها الآخَر: مُثَبَّتٌ صارَ مَحروساً أَو زالَ يَحمَرّ،
    /// وسَبَبٌ فارِغٌ أَو قَصيرٌ يَحمَرّ — <b>فَالقائِمَةُ تَصِف الواقِعَ
    /// أَو تَرِثّ حَتّى تَصيرَ قائِمَةَ إسكات.</b></summary>
    [Fact]
    public void No_pinned_store_write_outlives_its_reason()
    {
        var storeWrites = MinimalApiWriteEndpoints()
            .Where(e => e.Route.StartsWith("/{slug}/", StringComparison.Ordinal))
            .ToList();

        var all = storeWrites.Select(e => e.Route).ToHashSet(StringComparer.Ordinal);
        var gated = storeWrites.Where(e => e.Body.Contains(".RequireStoreWritable(", StringComparison.Ordinal))
                               .Select(e => e.Route).ToHashSet(StringComparer.Ordinal);

        var gone    = PinnedUngatedStoreWrites.Where(p => !all.Contains(p.Route)).Select(p => p.Route).ToArray();
        var covered = PinnedUngatedStoreWrites.Where(p => gated.Contains(p.Route)).Select(p => p.Route).ToArray();

        Assert.True(gone.Length == 0,
            "مَسارٌ مُثَبَّتٌ لا وُجودَ لَه — يُرفَع:\n  " + string.Join("\n  ", gone));
        Assert.True(covered.Length == 0,
            "مَسارٌ مُثَبَّتٌ صارَ يُعلِن الحارِس — يُرفَع مِن القائِمَة:\n  "
            + string.Join("\n  ", covered));

        foreach (var p in PinnedUngatedStoreWrites)
            Assert.True(p.WhyAr.Length > 30,
                $"استِثناءٌ بِلا سَبَبٍ مَقروء: {p.Route}");

        Assert.Equal(PinnedUngatedStoreWrites.Length,
            PinnedUngatedStoreWrites.Select(p => p.Route).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// <para><b>ونِصفُه الآخَر — الاتِّجاه المُعاكِس:</b> إدخالَةٌ
    /// حُرِسَت أَو زالَت ولَم تُرفَع تَحمَرّ. فَالقائِمَة تَصِف الواقِع
    /// أَو تَرِثّ حَتّى تَصيرَ قائِمَةَ إسكات.</para>
    /// </summary>
    [Fact]
    public void No_pinned_exception_outlives_its_reason()
    {
        var all = MinimalApiWriteEndpoints().Select(e => e.Route)
            .Concat(WolverineWriteEndpoints().Select(e => e.Route + ":w"))
            .ToHashSet(StringComparer.Ordinal);

        var guarded = MinimalApiWriteEndpoints()
            .Where(e => HasGuard(e.Body)).Select(e => e.Route)
            .ToHashSet(StringComparer.Ordinal);

        var gone    = PinnedPublic.Where(p => !all.Contains(p.Route)).Select(p => p.Route).ToArray();
        var covered = PinnedPublic.Where(p => guarded.Contains(p.Route)).Select(p => p.Route).ToArray();

        Assert.True(gone.Length == 0,
            "إدخالَة مُثَبَّتَة لِنُقطَة لَم تَعُد مَوجودَة — اِرفَعها:\n  " + string.Join("\n  ", gone));
        Assert.True(covered.Length == 0,
            "إدخالَة مُثَبَّتَة صارَت مَحروسَة — اِرفَعها:\n  " + string.Join("\n  ", covered));
    }

    /// <summary>
    /// <para><b>لا نُقطَة HTTP كاتِبَة في عُدَّة (kit) إلّا مُثَبَّتَة.</b>
    /// وهذا هو القَفل الَّذي يُغلِق <b>صِنفَ</b> العَطَب لا حالَتَه:
    /// العُدَد تَحت <c>libs/kits</c> <b>لا تَستَطيع</b> أَن تَرى حُرّاسَ
    /// القالَب (‏<c>RequireAuth</c> وإخوانَه) — فَالاعتِماد يَمشي مِن
    /// القالَب إلى العُدَّة لا العَكس. فَكُلّ <c>[WolverinePost]</c>
    /// يُكتَب في عُدَّة يُولَد <b>بِلا إمكانِ حِراسَة</b> أَصلاً.</para>
    ///
    /// <para>وهذا بِعَينِه ما وَقَعَ: ‏٢٥ نُقطَةً كُتِبَت في عُدَدٍ لا
    /// تَملِك حارِساً، فَبَقِيَت مَفتوحَة لا لِأَنّ أَحَداً نَسِيَ
    /// الحارِس، بَل لِأَنَّه <b>لَم يَكُن في مُتَناوَلِها</b>.</para>
    /// </summary>
    [Fact]
    public void No_kit_declares_an_unpinned_http_write_endpoint()
    {
        var wolverine = WolverineWriteEndpoints().ToList();
        var pinned = PinnedPublic.Select(p => p.Route).ToHashSet(StringComparer.Ordinal);

        var breaches = wolverine
            .Where(e => !pinned.Contains(e.Route + ":w"))
            .Select(e => $"{e.Route}   ({e.File})")
            .ToArray();

        Assert.True(breaches.Length == 0,
            "‏[Wolverine*] كاتِبَة في عُدَّة لا تَبلُغُها الحُرّاس:\n  " +
            string.Join("\n  ", breaches) +
            "\nالمَسار الصَحيح: نُقطَة في القالَب مَحروسَة بِالسِلسِلَة، " +
            "أَو تَثبيتٌ هُنا بِسَبَبٍ صَريح.");
    }

    /// <summary>الحارِس يَسبِق أَوَّل كِتابَة نَصّيّاً — وإلّا فَلَيسَ
    /// حارِساً (القاعِدَة ٦: التَخويل يَسبِق).</summary>
    [Fact]
    public void The_guard_precedes_the_first_write()
    {
        var late = new List<string>();
        var checkedCount = 0;

        foreach (var e in MinimalApiWriteEndpoints())
        {
            var guardAt = FirstIndexOfAny(e.Body, ChainGuards.Concat(BodyGuards));
            var writeAt = FirstIndexOfAny(e.Body, WriteCalls);
            if (guardAt < 0 || writeAt < 0) continue;

            checkedCount++;
            // الحارِس المُعلَن في السِلسِلَة يَقَع نَصّيّاً بَعدَ الجِسم
            // كُلِّه، ويَعمَل قَبلَه — فَلا يُقاس بِالتَرتيب.
            if (ContainsAny(e.Body, ChainGuards)) continue;
            if (guardAt > writeAt) late.Add(e.Route);
        }

        Assert.True(checkedCount > 0, "أَداة عَمياء: لَم تُقارَن نُقطَة واحِدَة.");
        Assert.True(late.Count == 0,
            "حارِسٌ يَقَع بَعدَ أَوَّل كِتابَة — فَلا يَمنَعُها:\n  " + string.Join("\n  ", late));
    }

    /// <summary>
    /// <para><b>الأَداةُ تُقاس قَبل أَن يُوثَق بِها</b> (القاعِدَة ١٠).
    /// النُسخَة الأُولى مِن الماسِح كانَت تَبتَلِع ١٣ نُقطَة بِسَبَب
    /// تَجريد تَعليقاتٍ بِـ <c>Regex</c> — <b>مِنها النُقطَة الَّتي
    /// كُتِبَ هذا الفَحص لِأَجلِها</b>. فَهذا الاختِبار يُثَبِّت أَنّ
    /// العَمى لا يَعود: عَيِّنَةٌ مَعروفَة تَقَع في مَناطِق كانَت
    /// مَبلوعَة، ويُشتَرَط أَن يَراها الماسِح.</para>
    /// </summary>
    [Fact]
    public void The_scanner_is_not_blind_to_endpoints_after_a_slash_star_in_a_string()
    {
        var routes = MinimalApiWriteEndpoints().Select(e => e.Route).ToHashSet(StringComparer.Ordinal);

        // كُلُّها كانَت غائِبَةً عَن النُسخَة الأُولى مِن الماسِح.
        foreach (var r in new[]
                 {
                     "/{slug}/auth/phone/login",
                     "/studio/s/{id:guid}/analyze",
                     "/studio/s/{id:guid}/build",
                     "/admin/incubator/start",
                     "/studio/apps/{slug}/roles/save",
                 })
            Assert.Contains(r, routes);

        // والتَبييض يَحفَظ الطول — وعَلَيه يَقوم فَحص «الحارِس يَسبِق».
        const string sample = "var a = \"/*\"; /* تَعليق */ var b = 1; // ذَيل";
        Assert.Equal(sample.Length, StripComments(sample).Length);
        Assert.Contains("\"/*\"", StripComments(sample));
        Assert.DoesNotContain("تَعليق", StripComments(sample));
        Assert.DoesNotContain("ذَيل",   StripComments(sample));
    }

    /// <summary>
    /// <para><b>ونُقطَةٌ تُعلَن <c>GET</c> وتَكتُب هي نُقطَةُ كِتابَة.</b>
    /// هذا هُوَ الثَقب الَّذي تَرَكَته النُسخَة الأُولى: كانَت تَسأَل
    /// <c>MapPost|Put|Delete|Patch</c> وَحدَها — أَي أَنَّها كانَت
    /// تَقرَأ <b>الفِعل المُعلَن</b> لا <b>الفِعل الواقِع</b>.
    /// و<c>MapGet</c> يُكتَب داخِلَه <c>SaveChangesAsync</c> بِلا أَن
    /// يَحمَرّ شَيء.</para>
    ///
    /// <para><b>ولِماذا يُوَسَّع الفاحِص القائِم ولا يُنشَأ ثانٍ</b>
    /// (القاعِدَة ٨): الحُرّاس ورُموزُهُم وقائِمَةُ التَثبيت هُنا،
    /// وفاحِصٌ ثانٍ بِقائِمَةٍ ثانِيَة يَنجَرِف عَنها — وهذا بِعَينِه
    /// عَطَبُ «تَعريفَين لِقَرارٍ واحِد» الَّذي حَذَفَت مَوجَةُ الثَغرَة
    /// ‏25 نُقطَةً لِإزالَتِه.</para>
    /// </summary>
    [Fact]
    public void Every_get_endpoint_that_writes_is_treated_as_a_write_endpoint()
    {
        var writeVerbRoutes = MinimalApiWriteEndpoints()
            .Select(e => e.Route).ToHashSet(StringComparer.Ordinal);

        var all = AllMinimalApiEndpoints().ToList();

        // عَدّاد: أَداةٌ تَفحَص صِفراً أَداةٌ عَمياء (القاعِدَة ١٠).
        Assert.True(all.Count >= 99,
            $"أَداة عَمياء: وُجِدَت {all.Count} نُقطَة minimal API — والمَقيس ‏99 فَأَكثَر.");

        var pinned = PinnedPublic.Select(p => p.Route).ToHashSet(StringComparer.Ordinal);

        var writingGets = all
            .Where(e => !writeVerbRoutes.Contains(e.Route))     // فِعلُها GET
            .Where(e => ContainsAny(e.Body, WriteCalls))        // وتَكتُب فِعلاً
            .Where(e => !HasGuard(e.Body) && !pinned.Contains(e.Route))
            .Select(e => $"{e.Route}   ({e.File})")
            .ToArray();

        Assert.True(writingGets.Length == 0,
            "‏GET يَكتُب وبِلا حارِس — الفِعل المُعلَن لَيسَ الفِعل الواقِع:\n  " +
            string.Join("\n  ", writingGets) +
            "\nإمّا أَن يُحرَس، أَو يُثَبَّت هُنا بِسَبَبِه في نَفس الكوميت.");
    }

    // ─── المَوجَة ٧: مَسارُ العَرض لا يُبَدِّل حالَة ────────────────────

    /// <summary>مَوضِعُ عَرضٍ يَكتُب، مُثَبَّتٌ بِاسمِه وسَبَبِه. و(‏2026-10-25، مُراجَعَةُ W8-5 الجَولَة 1) <c>InHandlers</c> يُضَيِّقُ التَثبيتَ إلى
    /// مُعالِجاتٍ مُسَمّاةٍ في صَفحَةٍ تَفاعُلِيَّة: لا يُعفى إلّا جِسمُها، وكُلُّ كاتِبٍ خارِجَها — ورُموزُ Marten المُباشِرَةُ في الصَفحَةِ كُلِّها — يُسأَلُ
    /// كَما يُسأَلُ في غَيرِها؛ و<see cref="Every_pinned_display_handler_is_an_event_binding_that_writes"/> يَشتَرِطُ أَنّ كُلَّ مُعالِجٍ مَربوطٌ بِحَدَثٍ
    /// ويَكتُبُ ولا يُنادى مِن غَيرِه.</summary>
    private sealed record DisplayWriter(string Where, string WhyAr, string[]? InHandlers = null);

    /// <summary>
    /// <para><b>فارِغَةٌ عَمداً، وهذا هُوَ الخَبَر.</b> كانَت ثَلاثَةً
    /// قَبلَ المَوجَة ٧: <c>ChatRoom.razor</c> تُصَفِّر عَدّادَ غَير
    /// المَقروء، و<c>Notifications.razor</c> تُعَلِّم المَعروضَ مَقروءاً،
    /// و<c>TenantListingDetail.razor</c> تُلحِق <c>ListingViewed</c> —
    /// كُلُّها في تَصيير صَفحَةٍ يُطلَب بِـ<c>GET</c>.</para>
    ///
    /// <para><b>وكُلُّ إضافَةٍ هُنا قَرارٌ يُتَّخَذ بِاليَد</b> في نَفس
    /// الكوميت الَّذي يَخرِقُ القاعِدَة — لا نَتيجَةُ نِسيان.</para>
    ///
    /// <para>(‏2026-10-25، مُراجَعَةُ W8-5 الجَولَة 1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>ولا مَوضِعَ عَرضٍ فيها بَعدُ</b>: الإدخالَتانِ أَدناه
    /// لَيستا تَصييراً يَكتُب، بَل صَفحَتا الوَكيلِ التَفاعُلِيَّتانِ تَكتُبانِ في مُعالِجاتِ نَقرٍ عَبرَ دارَةِ SignalR — كَشَفَهُما أَوَّلُ تَشغيلٍ لِلفاحِصِ عَلى
    /// صَفَحاتِ razor كُلِّها (‏167 صَفحَة، خَرقانِ اثنانِ لا غَير)، فَثُبِّتَتا بِمُعالِجاتِهِما المُسَمّاةِ لا بِالصَفحَة.</para>
    /// </summary>
    private static readonly DisplayWriter[] PinnedDisplayWriters =
    {
        new("libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/StudioAgent.razor",
            "صَفحَةٌ تَفاعُلِيَّةٌ (`@rendermode RenderMode.InteractiveServer`): كاتِباتُها الخَمسُ (`AgentService.AskAsync`/`UpdateToolStatusAsync`/" +
            "`ContinueAfterToolAsync`/`ResetAsync` و`AgentToolExecutor.ExecuteAsync`) في مُعالِجاتِ النَقرِ الأَربَعَةِ وَحدَها، تُنادى عَبرَ دارَةِ SignalR " +
            "بَعدَ التَصيير لا في طَلَبِ `GET`؛ وتَصييرُها (`OnInitializedAsync`) يُنادي `LoadSessionAsync` وهي قِراءَة. والحارِسُ " +
            "(`StudioAgentDoor.Opens`) سَطرٌ في جِسمِ كُلِّ مُعالِجٍ لا في تَوقيعِه — دَينُ القاعِدَة ٦ مَقولٌ هُنا لا مُبتَلَع.",
            ["SendAsync", "ApplyAsync", "RejectAsync", "ResetAsync"]),
        new("libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/Admin/AgentChatPanel.razor",
            "جَزيرَةٌ تَفاعُلِيَّةٌ تَرسُمُها `AgentChat.razor` بِـ`@rendermode=\"RenderMode.InteractiveServer\"` داخِلَ `RequirePlatformAdmin`: كاتِباتُها " +
            "(`AgentService.AskAsync`/`UpdateToolStatusAsync`/`ContinueAfterToolAsync`/`ResetAsync` و`AgentToolExecutor.ExecuteAsPlatformAdminAsync`) " +
            "في مُعالِجاتِ النَقرِ الأَربَعَةِ وَحدَها عَبرَ دارَةِ SignalR، وتَصييرُها يُنادي `LoadSessionAsync` وهي قِراءَة.",
            ["SendAsync", "ApplyAsync", "RejectAsync", "ResetAsync"]),
    };

    /// <summary>مَواضِعُ التَثبيتِ الكامِل (بِلا <c>InHandlers</c>) — وَحدَها تُعفي مَوضِعَها كُلَّه.</summary>
    private static HashSet<string> FullyPinnedDisplayWriters() =>
        PinnedDisplayWriters.Where(p => p.InHandlers is null).Select(p => p.Where).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// <para><b>لا صَفحَةَ <c>.razor</c> تُبَدِّل حالَةً مُخَزَّنَة.</b>
    /// وهذا لَيسَ تَكراراً لِلطَبَقَة الثامِنَة بَل سَدُّ ثَغرَتِها:
    /// تِلكَ تَعُدّ مَن <b>يَفتَح</b> جَلسَةً، وصَفحَةٌ تَحقِن
    /// <c>IDocumentSession</c> مِن الحاوي ثُمَّ تُودِع <b>لا تَفتَح
    /// شَيئاً</b> فَلا يَراها المُصَنِّف إطلاقاً. فَالمِقياسُ هُنا
    /// <b>فِعلُ الكِتابَة نَفسُه</b> لا مِلكِيَّةُ الجَلسَة.</para>
    ///
    /// <para><b>ولِماذا الرُموزُ هي <see cref="WriteCalls"/> نَفسُها
    /// ولا تُوسَّع</b>: ‏Marten لا يُثَبِّت شَيئاً بِلا
    /// <c>SaveChangesAsync</c> أَو إلحاقِ حَدَث — فَالقائِمَةُ كافِيَةٌ
    /// لِلسُؤال «هَل يَنتُج عَن هذا العَرض أَثَرٌ باقٍ؟». وقائِمَةٌ
    /// ثانِيَة أَوسَع تَنجَرِف عَن الأولى، وذاكَ عَطَبُ «تَعريفَين
    /// لِقَرارٍ واحِد» (القاعِدَة ٨).</para>
    ///
    /// <para><b>ويَفحَص المَوضِعَ لا النِيَّة</b>: مِلَفٌّ لاحِقَتُه
    /// <c>.razor</c> — أَي مَسارُ تَصيير بِالتَعريف — يَحوي رَمزَ
    /// كِتابَة. ولا يَسأَل لِماذا، ولا مَتى تُنَفَّذ.</para>
    /// </summary>
    [Fact]
    public void No_razor_page_writes_state()
    {
        var pages = EntitlementContractTests.SourceFiles()
            .Where(f => f.File.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // عَدّاد: أَداةٌ تَفحَص صِفراً أَداةٌ عَمياء (القاعِدَة ١٠).
        Assert.True(pages.Count >= 140,
            $"أَداة عَمياء: وُجِدَ {pages.Count} مِلَفّ .razor — والمَقيس ‏147 (‏167 في ‏2026-10-25).");

        // ‏تَثبيتُ المُعالِجاتِ (`InHandlers`) لا يُعفي الصَفحَةَ مِن رُموزِ Marten المُباشِرَة — الإعفاءُ لِلتَثبيتِ الكامِلِ وَحدَه.
        var pinned = FullyPinnedDisplayWriters();

        var breaches = new List<string>();
        foreach (var (file, text) in pages)
        {
            var code = StripComments(StripMarkupComments(text));
            var hits = WriteCalls.Where(w => code.Contains(w, StringComparison.Ordinal)).ToArray();
            if (hits.Length == 0) continue;

            var rel = Rel(file);
            if (pinned.Contains(rel)) continue;
            breaches.Add($"{rel}   ← {string.Join(", ", hits)}");
        }

        Assert.True(breaches.Count == 0,
            "صَفحَةُ عَرضٍ تُبَدِّل حالَةً مُخَزَّنَة — والطَلَبُ الَّذي يَرسُمُها `GET`:\n  " +
            string.Join("\n  ", breaches) +
            "\nالزاحِفُ والجالِبُ المُسبَق وأَداةُ التَحَقُّق كُلُّها تُطلِق هذا. " +
            "المَسار الصَحيح: أَمرٌ صَريح (`POST`) يُنادى بَعدَ التَصيير، " +
            "أَو حَذفُ الكِتابَة إن لَم يَكُن لَها مُستَهلِكٌ مَقيس.");
    }

    /// <summary>
    /// <para><b>ولا نُقطَةَ <c>MapGet</c> تُبَدِّل حالَةً مُخَزَّنَة —
    /// ولَو كانَت مَحروسَة.</b> وهذا هُوَ الفَرقُ عَن
    /// <see cref="Every_get_endpoint_that_writes_is_treated_as_a_write_endpoint"/>
    /// أَعلاه: ذاكَ يَسأَل «هَل لِهذِه الكِتابَة حارِس؟»، وهذا يَسأَل
    /// <b>«ولِمَ تَكتُب أَصلاً وفِعلُها عَرض؟»</b>. فَنُقطَةُ
    /// <c>GET</c> مَحروسَةٌ تَكتُب تَعبُر ذاكَ وتَحمَرّ هُنا — وهي
    /// الحالَةُ الَّتي كانَت مَفتوحَة.</para>
    ///
    /// <para>والمِقياس <b>مَوضِعٌ</b> لا نِيَّة: رَمزُ كِتابَةٍ يَقَع
    /// داخِلَ عِبارَة <c>.MapGet(</c>.</para>
    /// </summary>
    [Fact]
    public void No_get_endpoint_writes_state()
    {
        var gets = MinimalApiGetEndpoints().ToList();

        // عَدّاد: أَداةٌ تَفحَص صِفراً أَداةٌ عَمياء (القاعِدَة ١٠).
        Assert.True(gets.Count >= 10,
            $"أَداة عَمياء: وُجِدَت {gets.Count} نُقطَة MapGet — والمَقيس ‏11.");

        var pinned = FullyPinnedDisplayWriters();

        var breaches = gets
            .Where(e => ContainsAny(e.Body, WriteCalls))
            .Where(e => !pinned.Contains(e.Route))
            .Select(e => $"{e.Route}   ({e.File})")
            .ToArray();

        Assert.True(breaches.Length == 0,
            "نُقطَةُ `GET` تُبَدِّل حالَةً مُخَزَّنَة — الفِعلُ المُعلَن عَرضٌ والواقِعُ كِتابَة:\n  " +
            string.Join("\n  ", breaches) +
            "\nإمّا أَن تَصيرَ `POST` صَريحاً، أَو تُثَبَّت هُنا بِسَبَبِها في نَفس الكوميت.");
    }

    /// <summary><b>والنِصفُ الآخَر</b>: إدخالَةٌ مُثَبَّتَة لَم تَعُد
    /// تَكتُب — أَو لَم يَعُد مَوضِعُها مَوجوداً — تَحمَرّ حَتّى تُرفَع.
    /// وكُلُّ إدخالَةٍ تُعلِن سَبَبَها، فَالقائِمَةُ دَينٌ مَوصوف لا
    /// قائِمَةُ إسكات.</summary>
    [Fact]
    public void No_pinned_display_writer_outlives_its_reason()
    {
        foreach (var p in PinnedDisplayWriters)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.WhyAr), $"«{p.Where}» بِلا سَبَب.");
            Assert.True(p.WhyAr.Length > 30, $"سَبَب «{p.Where}» أَقصَرُ مِن أَن يَكونَ سَبَباً.");
        }

        Assert.Equal(
            PinnedDisplayWriters.Select(p => p.Where).Distinct(StringComparer.Ordinal).Count(),
            PinnedDisplayWriters.Length);

        var writing = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (file, text) in EntitlementContractTests.SourceFiles()
                     .Where(f => f.File.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)))
            if (ContainsAny(StripComments(StripMarkupComments(text)), WriteCalls))
                writing.Add(Rel(file));

        foreach (var e in MinimalApiGetEndpoints())
            if (ContainsAny(e.Body, WriteCalls))
                writing.Add(e.Route);

        // ‏(‏2026-10-25، مُراجَعَةُ W8-5 الجَولَة 1) ومَن يَكتُبُ عَبرَ خِدمَةٍ في أَيِّ سَطحِ عَرض — وإلّا حَمَّرَ هذا الصَفُّ تَثبيتاً صَحيحاً لِصَفحَةٍ
        // أَو `[WolverineGet]` يُناديانِ كاتِبَةً بِأَنَّه «لَم يَعُد يَكتُب»، فَيَستَحيلُ التَثبيتُ الَّذي يَدعو إلَيه الفاحِصُ نَفسُه.
        var sources = EntitlementContractTests.SourceFiles().ToList();
        var writers = ServiceWriters(sources);
        var typesByFile = TypesByFile(sources);
        foreach (var s in DisplaySurfaces(sources))
            if (SurfaceWrites(s, writers, typesByFile.GetValueOrDefault(s.File, [])).Count > 0)
                writing.Add(s.Where);

        var settled = PinnedDisplayWriters
            .Where(p => !writing.Contains(p.Where)).Select(p => p.Where).ToArray();

        Assert.True(settled.Length == 0,
            "إدخالَةٌ مُثَبَّتَة لَم تَعُد تَكتُب في مَسار عَرض — اِرفَعها:\n  " +
            string.Join("\n  ", settled));
    }

    // ─── ‏2026-10-25 (تَدقيقُ الإطلاق lens-checkers-ci-4، كُبرى): `GET` يَكتُبُ عَبرَ خِدمَة ───────────────────────────────────────
    //
    // **المَقيسُ قَبل** (`f5c292a`): الصَفّانِ أَعلاه يَسأَلانِ جِسمَ النُقطَةِ عَن رُموزِ Marten المُباشِرَة (`WriteCalls`) — ونُقطَةُ `GET`
    // تُنادي `deals.CancelAsync(…)` (الطَفرَةُ M2c عَلى `GET /api/v1/deals/{id}`، بِنِطاقِ القِراءَة `DealsRead`، بِعَدَدِ الأَسطُرِ نَفسِه) لا تَحمِلُ
    // رَمزاً مِنها، فَتَمُرُّ الحَقيبَةُ والطَبَقَةُ ٩ («none touches storage») — و‏ADR-020 §٥ يَقولُ إنّ الفاحِصَ يَفرِضُ الفَرق. **وصارَ**: كُلُّ طَريقَةٍ
    // في كُلِّ نَوعٍ في `libs/`/`apps/` يَحمِلُ جِسمُها رَمزَ كِتابَة — أَو يُنادي طَريقَةً كاتِبَةً مِن نَوعِه — **كاتِبَة**؛ وكُلُّ نُقطَةِ `MapGet`/`MapMethods`
    // وكُلُّ نُقطَةٍ بِـ`RequireApiKey(ApiScopeCatalog.*Read)` أَيّاً كانَ فِعلُها تُسأَلُ: أَتُنادي كاتِبَةً عَلى وَسيطٍ مُعلَنِ النَوعِ في لامدَتِها، أَو عَلى
    // خِدمَةٍ مِن `GetRequiredService<T>`، أَو عَلى النَوعِ نَفسِه ساكِناً، أَو طَريقَةً كاتِبَةً مِن نَوعٍ في مِلَفِّها؟ **وحَدُّه يُقال**: النَصُّ لا الدَلالَة —
    // كاتِبٌ يُستَدعى عَبرَ واجِهَةٍ أَو مُفَوَّضٍ أَو نَوعٍ ثالِثٍ (خِدمَةٌ تُنادي خِدمَة) لا يُرى؛ والأَسماءُ تُدمَجُ بِاسمِ النَوعِ لا بِفَضائِه (حَذَرٌ لا تَساهُل).

    private static readonly Regex TypeDecl = new(@"\b(?:class|record|struct)\s+(?<name>\w+)", RegexOptions.Compiled);

    private static readonly Regex MethodDecl = new(
        @"(?:public|internal|private|protected)\s+(?:(?:static|async|virtual|override|sealed|new|readonly|partial)\s+)*[\w<>\[\]?,.() ]+?\s+(?<name>\w+)\s*(?:<[^<>()]*>)?\s*\(",
        RegexOptions.Compiled);

    /// <summary>فِهرِسُ القَوسِ المُغلِقِ لِلقَوسِ المَفتوحِ عِندَ <paramref name="open"/> — بِتَخَطّي السَلاسِل.</summary>
    private static int Closing(string code, int open, char o, char c)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] is '"' or '\'') { i = SkipLiteral(code, i); continue; }
            if (code[i] == o) depth++;
            else if (code[i] == c && --depth == 0) return i;
        }
        return code.Length - 1;
    }

    /// <summary>حُقولُ النَوعِ ووُسَطاءُ مُنشِئِه الأَوَّليِّ: الاسمُ ← اسمُ النَوعِ القَصير. يَقرَأُ جِسمَ النَوعِ كُلَّه، فَمُتَغَيِّرٌ مَحَلِّيٌّ بِنَوعٍ
    /// مُعلَنٍ يُعَدُّ حَقلاً — حَذَرٌ لا تَساهُل: يَزيدُ الكاتِباتِ ولا يُنقِصُها.</summary>
    private static readonly Regex FieldDecl = new(
        @"(?<![\w.<])(?<type>[A-Z][\w.]*)(?:<[^<>;=()]*>)?\??\s+(?<name>_?[a-z]\w*)\s*(?:[;=)]|,\s*[A-Z])", RegexOptions.Compiled);

    /// <summary><b>الطَرائِقُ الكاتِبَةُ لِكُلِّ نَوع</b>: اسمُ النَوعِ ← أَسماءُ طَرائِقِه الَّتي يَحمِلُ جِسمُها رَمزاً مِن <see cref="WriteCalls"/>، أَو
    /// يُنادي طَريقَةً كاتِبَة — مِن نَوعِه (بِلا مُستَقبِلٍ أَو بِـ<c>this.</c>)، أَو مِن نَوعِ حَقلٍ أَو وَسيطِ مُنشِئٍ أَوَّليٍّ لَه (<c>_audit.WriteAsync(</c>
    /// و<c>Audit.AuditWriter _audit</c>) — حَتّى الثَبات عَبرَ الأَنواعِ كُلِّها. والأَنواعُ الجُزئِيَّةُ تُدمَجُ بِاسمِها.</summary>
    internal static IReadOnlyDictionary<string, HashSet<string>> ServiceWriters(IEnumerable<(string File, string Text)> sources)
    {
        var bodies = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);
        var fields = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (file, text) in sources)
        {
            if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
            var code = StripComments(text);
            foreach (Match t in TypeDecl.Matches(code))
            {
                var name = t.Groups["name"].Value;
                var open = code.IndexOf('{', t.Index + t.Length);
                var semi = code.IndexOf(';', t.Index + t.Length);
                if (open < 0 || (semi >= 0 && semi < open)) continue;          // ‏`record X(…);` بِلا جِسم
                var body = code[(open + 1)..Closing(code, open, '{', '}')];
                var methods = bodies.TryGetValue(name, out var m) ? m : bodies[name] = new(StringComparer.Ordinal);
                var known = fields.TryGetValue(name, out var f) ? f : fields[name] = new(StringComparer.Ordinal);
                foreach (Match fd in FieldDecl.Matches(code[(t.Index + t.Length)..(open + 1 + body.Length)]))
                    known.TryAdd(fd.Groups["name"].Value, fd.Groups["type"].Value.Split('.')[^1]);
                foreach (Match d in MethodDecl.Matches(body))
                {
                    var paren = d.Index + d.Length - 1;
                    var after = Closing(body, paren, '(', ')') + 1;
                    var head = Regex.Match(body[after..Math.Min(body.Length, after + 400)], @"^\s*(?:where[^{=;]*)?(?<kind>\{|=>)");
                    if (!head.Success) continue;
                    var at = after + head.Groups["kind"].Index;
                    var text2 = head.Groups["kind"].Value == "{" ? body[at..(Closing(body, at, '{', '}') + 1)] : StatementFrom(body, at);
                    (methods.TryGetValue(d.Groups["name"].Value, out var list) ? list : methods[d.Groups["name"].Value] = []).Add(text2);
                }
            }
        }

        var writers = bodies.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Where(mv => mv.Value.Any(b => ContainsAny(b, WriteCalls))).Select(mv => mv.Key).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
        bool Writes(string type, string body)
        {
            foreach (Match c in BareCall.Matches(body))
                if (writers[type].Contains(c.Groups["m"].Value)) return true;
            foreach (Match c in MemberCall.Matches(body))
            {
                var recv = c.Groups["recv"].Value;
                var target = recv == "this" ? type : fields[type].TryGetValue(recv, out var ft) ? ft : null;
                if (target is not null && writers.TryGetValue(target, out var w) && w.Contains(c.Groups["m"].Value)) return true;
            }
            return false;
        }
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var (type, methods) in bodies)
                foreach (var (method, list) in methods)
                    if (!writers[type].Contains(method) && list.Any(b => Writes(type, b)))
                    {
                        writers[type].Add(method);
                        grew = true;
                    }
        }
        return writers.Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
    }

    private static readonly Regex ReadScope = new(@"\.RequireApiKey\s*\(\s*ApiScopeCatalog\.\w+Read\s*\)", RegexOptions.Compiled);
    private static readonly Regex ResolvedService = new(@"\bvar\s+(?<name>\w+)\s*=\s*[\w.]*GetRequiredService\s*<\s*(?<type>[\w.]+)\s*>", RegexOptions.Compiled);
    private static readonly Regex MemberCall = new(@"(?<![\w.])(?<recv>\w+)\s*\.\s*(?<m>\w+)\s*(?:<[^<>()]*>)?\s*\(", RegexOptions.Compiled);
    private static readonly Regex BareCall = new(@"(?<![\w.])(?<m>\w+)\s*(?:<[^<>()]*>)?\s*\(", RegexOptions.Compiled);

    /// <summary>وُسَطاءُ لامدَةِ النُقطَة: الاسمُ ← اسمُ النَوعِ القَصير (بِلا فَضاءٍ ولا <c>?</c> ولا وُسَطاءِ نَوعٍ ولا سِماتٍ)، ومَعَها
    /// <c>var x = …GetRequiredService&lt;T&gt;()</c> في الجِسم.</summary>
    internal static IReadOnlyDictionary<string, string> HandlerParameters(string body)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var route = Regex.Match(body, @"^\.Map\w*\s*\(\s*""[^""]*""\s*,\s*(?:async\s*)?(?<open>\()");
        if (route.Success)
        {
            var open = route.Groups["open"].Index;
            foreach (var (name, type) in ParameterTypes(body[(open + 1)..Closing(body, open, '(', ')')])) map[name] = type;
        }
        foreach (Match r in ResolvedService.Matches(body)) map[r.Groups["name"].Value] = r.Groups["type"].Value.Split('.')[^1];
        return map;
    }

    /// <summary>قائِمَةُ وُسَطاءَ بَينَ قَوسَين (بِلا القَوسَين): الاسمُ ← اسمُ النَوعِ القَصير — لِلامدَةِ <c>Map…</c> ولِطَريقَةِ
    /// <c>[WolverineGet]</c> مَعاً، فَيُقرَأُ الشَكلانِ بِمُحَلِّلٍ واحِد. (‏2026-10-25، مُراجَعَةُ W8-5 الجَولَة 1: استُخرِجَت مِن
    /// <see cref="HandlerParameters"/> بِلا تَغييرِ حَرفٍ مِن مَنطِقِها.)</summary>
    private static Dictionary<string, string> ParameterTypes(string list)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var (depth, from) = (0, 0);
        var parts = new List<string>();
        for (var i = 0; i <= list.Length; i++)
        {
            if (i == list.Length || (list[i] == ',' && depth == 0)) { parts.Add(list[from..i]); from = i + 1; continue; }
            if (list[i] is '<' or '(' or '[') depth++;
            else if (list[i] is '>' or ')' or ']') depth--;
        }
        foreach (var p in parts)
        {
            var clean = Regex.Replace(p, @"\[[^\]]*\]", " ").Trim();
            var m = Regex.Match(clean, @"(?<type>[\w.]+)(?:<[^>]*>)?\??\s+(?<name>\w+)\s*(?:=.*)?$");
            if (m.Success) map[m.Groups["name"].Value] = m.Groups["type"].Value.Split('.')[^1];
        }
        return map;
    }

    /// <summary><b>ما تُناديه النُقطَةُ مِن الكاتِبات</b> — «نَوع.طَريقَة» لِكُلِّ نِداءٍ عَلى وَسيطٍ أَو خِدمَةٍ مَحلولَةٍ أَو نَوعٍ ساكِن، أَو نِداءٍ بِلا
    /// مُستَقبِلٍ لِكاتِبَةٍ مِن نَوعٍ في <paramref name="fileTypes"/> (مِلَفِّ النُقطَة). و<paramref name="receivers"/> مُستَقبِلاتٌ مُعلَنَةُ النَوعِ
    /// خارِجَ اللامدَة — <c>@inject</c> صَفحَةِ razor ووُسَطاءُ طَريقَةِ <c>[WolverineGet]</c> (‏2026-10-25، مُراجَعَةُ W8-5 الجَولَة 1)؛ وما في اللامدَةِ
    /// والجِسمِ يَغلِبُها.</summary>
    internal static IReadOnlyList<string> WritersCalled(string body, IReadOnlyDictionary<string, HashSet<string>> writers, IReadOnlyCollection<string> fileTypes,
        IReadOnlyDictionary<string, string>? receivers = null)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, type) in receivers ?? new Dictionary<string, string>()) parameters[name] = type;
        foreach (var (name, type) in HandlerParameters(body)) parameters[name] = type;
        var found = new List<string>();
        foreach (Match c in MemberCall.Matches(body))
        {
            var recv = c.Groups["recv"].Value;
            var type = parameters.TryGetValue(recv, out var t) ? t : writers.ContainsKey(recv) ? recv : null;
            if (type is not null && writers.TryGetValue(type, out var w) && w.Contains(c.Groups["m"].Value)) found.Add($"{type}.{c.Groups["m"].Value}");
        }
        foreach (Match c in BareCall.Matches(body))
            foreach (var type in fileTypes)
                if (writers.TryGetValue(type, out var w) && w.Contains(c.Groups["m"].Value)) found.Add($"{type}.{c.Groups["m"].Value}");
        return found.Distinct(StringComparer.Ordinal).ToList();
    }

    // ─── ‏2026-10-25 (مُراجَعَةُ تَيّارِ W8-5 الجَولَة 1، كُبرى): صَفحَةُ razor و`[WolverineGet]` تَكتُبانِ عَبرَ خِدمَة ───────────────
    //
    // **المَقيسُ قَبل** (`4acc669`): الفاحِصُ أَدناه يَسأَلُ `MapGet`/`MapMethods` ونِطاقَ القِراءَةِ وَحدَهُما — وصَفحَةُ `.razor` تُرسَمُ بِـ`GET`
    // (وهي ما يُسَمّيه هذا المِلَفُّ «مَسارَ العَرض») لا تُسأَلُ إلّا عَن رُموزِ Marten المُباشِرَة (`No_razor_page_writes_state`)، و`[WolverineGet]`
    // لا يُسأَلُ عَن شَيء. فَسَطرٌ واحِدٌ في `MyDealDetail.razor` بَعدَ `deal = await Deals.LoadAsync(Slug, Id);` —
    // `if (deal is not null) await Deals.CancelAsync(Slug, deal.Id, uid);` — يَتَرُكُ `WriteEndpointGuardTests` ‏24/24 (مَقيسٌ عَلى الشَجَرَةِ
    // الحَقيقِيَّةِ بِنُسخَةٍ احتِياطِيَّةٍ أُعيدَت بِبَصمَتِها)، وجَدوَلُ «قَواعِدُ رُفِعَت» في CLAUDE.md يَستَنِدُ إلى هذا المِلَفِّ بِعَينِه.
    // **وصارَ**: الفاحِصُ نَفسُه (الكاتِباتُ نَفسُها، وقائِمَةُ التَثبيتِ نَفسُها — القاعِدَة ٨) يَسأَلُ كُلَّ صَفحَةِ `.razor` بِنَصِّها كُلِّه —
    // مُستَقبِلاتُها كُلُّ `@inject T N` و`[Inject] T N {` وكُلُّ تَصريحٍ مُعلَنِ النَوعِ فيها — وكُلَّ `[WolverineGet]` بِوُسَطاءِ طَريقَتِه وجِسمِها،
    // وهذا الأَخيرُ عَن رُموزِ Marten المُباشِرَةِ أَيضاً (لا صَفَّ غَيرُه يَسأَلُه). **ويَفحَصُ المَوضِعَ لا التَوقيت**، كَـ`No_razor_page_writes_state`:
    // كاتِبٌ في مُعالِجِ نَقرٍ لِصَفحَةٍ تَفاعُلِيَّةٍ يُسأَلُ، ويُثَبَّتُ بِمُعالِجِه المُسَمّى (`InHandlers`) لا بِصَفحَتِه — وأَوَّلُ تَشغيلٍ أَمسَكَ
    // اثنَتَينِ بِالضَبط: `StudioAgent.razor` و`AgentChatPanel.razor`، كاتِباتُهُما في `SendAsync`/`ApplyAsync`/`RejectAsync`/`ResetAsync`. **وحَدُّه**: نِداءٌ عَلى نَتيجَةِ
    // `GetRequiredService<T>()` مُباشَرَةً بِلا مُتَغَيِّر، أَو عَلى `[Parameter]` بِنَوعِ خِدمَة، أَو في مِلَفِّ `.razor.cs` (صِفرٌ مِنها اليَوم)، لا يُرى.

    /// <summary>سَطحُ عَرضٍ يُسأَلُ عَن الكاتِبات: نَوعُه، ومَوضِعُه (المَسارُ، أَو المِلَفُّ لِصَفحَةِ razor — مِفتاحُ <see cref="PinnedDisplayWriters"/>)،
    /// ومِلَفُّه، ونَصُّه بِلا تَعليقات، ومُستَقبِلاتُه المُعلَنَةُ النَوعِ خارِجَ اللامدَة؛ و<c>AskDirect</c> حينَ لا يَسأَلُ صَفٌّ آخَرُ نَصَّه عَن
    /// <see cref="WriteCalls"/> المُباشِرَة.</summary>
    internal sealed record DisplaySurface(string Kind, string Where, string File, string Body, IReadOnlyDictionary<string, string> Receivers, bool AskDirect);

    private const string SurfaceGet = "MapGet", SurfaceReadScoped = "ReadScope", SurfaceWolverineGet = "WolverineGet", SurfaceRazor = "razor";

    private static readonly IReadOnlyDictionary<string, string> NoReceivers = new Dictionary<string, string>();

    /// <summary>البادِئَةُ المُؤَهَّلَةُ اختِيارِيَّةٌ كَما في <see cref="WolverineWrite"/> — وبِالعِلَّةِ نَفسِها.</summary>
    private static readonly Regex WolverineRead =
        new(@"\[(?:[A-Za-z_][A-Za-z0-9_]*\s*\.\s*)*WolverineGet\s*\(\s*""(?<route>[^""]+)""", RegexOptions.Compiled);

    /// <summary><c>@inject T N</c> في سَطرِه، و<c>[Inject] T N {</c> في <c>@code</c> — الشَكلانِ اللَّذانِ تَحقِنُ بِهِما صَفحَةُ razor خِدمَة.</summary>
    private static readonly Regex RazorInject = new(
        @"^[ \t]*@inject[ \t]+(?<type>[\w.]+)(?:<[^>\r\n]*>)?\??[ \t]+(?<name>\w+)[ \t]*\r?$" +
        @"|\[Inject\]\s*(?:(?:public|private|protected|internal|required)\s+)*(?<type>[\w.]+)(?:<[^<>{};]*>)?\??\s+(?<name>\w+)\s*\{",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>مُستَقبِلاتُ صَفحَةِ razor: الاسمُ ← اسمُ النَوعِ القَصير — كُلُّ تَصريحٍ مُعلَنِ النَوعِ (<see cref="FieldDecl"/>، حَذَرٌ لا تَساهُل)، ثُمَّ
    /// كُلُّ حَقنٍ (<see cref="RazorInject"/>) يَغلِبُه.</summary>
    internal static IReadOnlyDictionary<string, string> RazorReceivers(string page)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match d in FieldDecl.Matches(page)) map.TryAdd(d.Groups["name"].Value, d.Groups["type"].Value.Split('.')[^1]);
        foreach (Match i in RazorInject.Matches(page)) map[i.Groups["name"].Value] = i.Groups["type"].Value.Split('.')[^1];
        return map;
    }

    /// <summary>كُلُّ صَفحَةِ <c>.razor</c> سَطحُ عَرض: نَصُّها كُلُّه بَعدَ تَبييضِ تَعليقاتِ الصِنفَين، ومَوضِعُها مِلَفُّها.</summary>
    private static IEnumerable<DisplaySurface> RazorPages(IEnumerable<(string File, string Text)> sources)
    {
        foreach (var (file, text) in sources)
        {
            if (!file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)) continue;
            var code = StripComments(StripMarkupComments(text));
            yield return new(SurfaceRazor, Rel(file), Rel(file), code, RazorReceivers(code), AskDirect: false);
        }
    }

    /// <summary>كُلُّ <c>[WolverineGet]</c> سَطحُ عَرض: مِن السِمَةِ حَتّى آخِرِ جِسمِ طَريقَتِه، ومُستَقبِلاتُه وُسَطاؤها. وطَريقَةٌ لا يَقرَؤها الماسِحُ
    /// (لا تَتلو السِمَةَ إلّا سِماتٌ، أَو بِلا جِسم) تُعادُ بِنَصٍّ فارِغ — فَيُحمِرُّها الفاحِصُ بِاسمِها لا يُسقِطُها (القاعِدَة ١٠).</summary>
    private static IEnumerable<DisplaySurface> WolverineGetHandlers(IEnumerable<(string File, string Text)> sources)
    {
        foreach (var (file, text) in sources)
        {
            if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
            var code = StripComments(text);
            foreach (Match m in WolverineRead.Matches(code))
            {
                var route = m.Groups["route"].Value;
                var attrEnd = Closing(code, m.Index, '[', ']') + 1;
                var d = MethodDecl.Match(code, attrEnd);
                var adjacent = d.Success && Regex.IsMatch(code[attrEnd..d.Index], @"^\s*(?:\[[^\]]*\]\s*)*$");
                var paren = adjacent ? d.Index + d.Length - 1 : -1;
                var close = paren < 0 ? -1 : Closing(code, paren, '(', ')');
                var head = close < 0 ? Match.Empty : Regex.Match(code[(close + 1)..Math.Min(code.Length, close + 401)], @"^\s*(?:where[^{=;]*)?(?<kind>\{|=>)");
                if (!head.Success) { yield return new(SurfaceWolverineGet, route, Rel(file), "", NoReceivers, AskDirect: true); continue; }
                var at = close + 1 + head.Groups["kind"].Index;
                var end = head.Groups["kind"].Value == "{" ? Closing(code, at, '{', '}') + 1 : at + StatementFrom(code, at).Length;
                yield return new(SurfaceWolverineGet, route, Rel(file), code[m.Index..end], ParameterTypes(code[(paren + 1)..close]), AskDirect: true);
            }
        }
    }

    /// <summary>سُطوحُ العَرضِ الأَربَعَة: <c>MapGet</c>/<c>MapMethods</c>، ونِطاقُ القِراءَةِ في الـAPI أَيّاً كانَ فِعلُه، و<c>[WolverineGet]</c>، وصَفَحاتُ
    /// <c>.razor</c>.</summary>
    private static List<DisplaySurface> DisplaySurfaces(IReadOnlyList<(string File, string Text)> sources) =>
        MinimalApiGetEndpoints().Select(e => new DisplaySurface(SurfaceGet, e.Route, e.File, e.Body, NoReceivers, AskDirect: false))
            .Concat(AllMinimalApiEndpoints().Where(e => ReadScope.IsMatch(e.Body))
                .Select(e => new DisplaySurface(SurfaceReadScoped, e.Route, e.File, e.Body, NoReceivers, AskDirect: false)))
            .Concat(WolverineGetHandlers(sources))
            .Concat(RazorPages(sources))
            .ToList();

    /// <summary>الأَنواعُ المُعلَنَةُ في كُلِّ مِلَفٍّ (المِفتاحُ مَسارُه النِسبِيّ) — لِنِداءِ كاتِبَةٍ بِلا مُستَقبِل.</summary>
    private static Dictionary<string, IReadOnlyCollection<string>> TypesByFile(IEnumerable<(string File, string Text)> sources) =>
        sources.ToDictionary(f => Rel(f.File),
            f => (IReadOnlyCollection<string>)TypeDecl.Matches(StripComments(f.Text)).Select(m => m.Groups["name"].Value).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

    /// <summary>ما يَكتُبُه السَطح: رُموزُ <see cref="WriteCalls"/> في نَصِّه حينَ <c>AskDirect</c>، ثُمَّ ما يُناديه مِن الكاتِبات (<see cref="WritersCalled"/>).</summary>
    internal static IReadOnlyList<string> SurfaceWrites(DisplaySurface s, IReadOnlyDictionary<string, HashSet<string>> writers, IReadOnlyCollection<string> fileTypes) =>
        (s.AskDirect ? WriteCalls.Where(w => s.Body.Contains(w, StringComparison.Ordinal)) : [])
            .Concat(WritersCalled(s.Body, writers, fileTypes, s.Receivers))
            .ToList();

    /// <summary>تَعريفُ مُعالِجٍ مُسَمّىً في <c>@code</c> صَفحَةِ razor — بِلا مُعَدِّلِ وُصولٍ عادَةً (<c>async Task SendAsync()</c>)، فَلا يُقرَأُ بِـ<see cref="MethodDecl"/>.</summary>
    private static Regex HandlerDecl(string name) =>
        new(@"(?:(?:private|protected|public|internal)\s+)?(?:async\s+)?(?:Task|ValueTask|void)\s+" + Regex.Escape(name) + @"\s*\(");

    /// <summary>رَبطُ حَدَثٍ في التَرميز — <c>@onclick="…"</c> و<c>@onkeydown:preventDefault="…"</c> — بِقيمَتِه.</summary>
    private static readonly Regex EventBinding = new(@"@on\w+(?::\w+)?\s*=\s*""[^""]*""", RegexOptions.Compiled);

    /// <summary>مَدى كُلِّ تَعريفٍ لِلمُعالِجِ <paramref name="name"/> في الصَفحَة: مِن رَأسِه حَتّى آخِرِ جِسمِه.</summary>
    private static List<(int Start, int End)> HandlerSpans(string page, string name)
    {
        var spans = new List<(int, int)>();
        foreach (Match d in HandlerDecl(name).Matches(page))
        {
            var paren = d.Index + d.Length - 1;
            var close = Closing(page, paren, '(', ')');
            var head = Regex.Match(page[(close + 1)..Math.Min(page.Length, close + 401)], @"^\s*(?<kind>\{|=>)");
            if (!head.Success) continue;
            var at = close + 1 + head.Groups["kind"].Index;
            spans.Add((d.Index, head.Groups["kind"].Value == "{" ? Closing(page, at, '{', '}') + 1 : at + StatementFrom(page, at).Length));
        }
        return spans;
    }

    /// <summary>النَصُّ بِمَداه مُبَيَّضاً — ويُبقي الطولَ والأَسطُر.</summary>
    private static string Blank(string text, IEnumerable<(int Start, int End)> spans)
    {
        var b = text.ToCharArray();
        foreach (var (start, end) in spans)
            for (var i = start; i < end && i < b.Length; i++)
                if (b[i] is not ('\n' or '\r')) b[i] = ' ';
        return new string(b);
    }

    /// <summary>السَطحُ بَعدَ تَبييضِ تَعريفاتِ <paramref name="handlers"/> فيه — وما سِواها يُسأَلُ كَما هُوَ.</summary>
    internal static DisplaySurface WithoutHandlers(DisplaySurface s, IReadOnlyCollection<string>? handlers) =>
        handlers is null ? s : s with { Body = Blank(s.Body, handlers.SelectMany(h => HandlerSpans(s.Body, h))) };

    /// <summary>السَطحُ بَعدَ تَثبيتِ مُعالِجاتِه: جِسمُ كُلِّ مُعالِجٍ مُسَمّىً في <c>InHandlers</c> لِمَوضِعِه مُبَيَّض.</summary>
    private static DisplaySurface WithoutPinnedHandlers(DisplaySurface s) =>
        WithoutHandlers(s, PinnedDisplayWriters.FirstOrDefault(p => p.InHandlers is not null && p.Where == s.Where)?.InHandlers);

    /// <summary><b>خُروقُ تَثبيتِ مُعالِجٍ في صَفحَة</b> — بِرُموزٍ ثابِتَة: <c>handler_absent</c> (لا تَعريفَ لَه)، <c>handler_unbound</c> (لا رَبطَ حَدَثٍ
    /// يُسَمّيه)، <c>handler_silent</c> (لا يُنادي كاتِبَةً — فَالتَثبيتُ عاشَ سَبَبَه)، <c>handler_called_outside</c> (يُذكَرُ خارِجَ تَعريفِه ورَبطِ حَدَثٍ
    /// — فَقَد يُنادى في التَصيير). الصَفحَةُ نَصُّها بَعدَ تَبييضِ التَعليقات.</summary>
    internal static IReadOnlyList<string> HandlerPinBreaches(string page, IReadOnlyCollection<string> handlers,
        IReadOnlyDictionary<string, HashSet<string>> writers, IReadOnlyCollection<string> fileTypes)
    {
        var breaches = new List<string>();
        var receivers = RazorReceivers(page);
        var bindings = EventBinding.Matches(page).Select(m => (m.Index, m.Index + m.Length)).ToList();
        var allSpans = handlers.SelectMany(h => HandlerSpans(page, h)).ToList();
        var rest = Blank(page, allSpans.Concat(bindings));
        foreach (var h in handlers)
        {
            var spans = HandlerSpans(page, h);
            if (spans.Count == 0) { breaches.Add($"handler_absent:{h}"); continue; }
            var word = new Regex(@"\b" + Regex.Escape(h) + @"\b");
            if (!bindings.Any(b => word.IsMatch(page[b.Item1..b.Item2]))) breaches.Add($"handler_unbound:{h}");
            if (spans.All(sp => WritersCalled(page[sp.Start..sp.End], writers, fileTypes, receivers).Count == 0)) breaches.Add($"handler_silent:{h}");
            if (word.IsMatch(rest)) breaches.Add($"handler_called_outside:{h}");
        }
        return breaches;
    }

    /// <summary><b>ولا سَطحَ عَرضٍ يَكتُبُ عَبرَ خِدمَة</b>: كُلُّ <c>MapGet</c>/<c>MapMethods</c>، وكُلُّ نُقطَةٍ بِنِطاقِ قِراءَةٍ في الـAPI، و(‏2026-10-25،
    /// مُراجَعَةُ W8-5 الجَولَة 1) كُلُّ <c>[WolverineGet]</c> وكُلُّ صَفحَةِ <c>.razor</c> — لا تُنادي طَريقَةً كاتِبَة. والعَدّاداتُ (الطَرائِقُ الكاتِبَة،
    /// والسُطوحُ المَسؤولَةُ بِأَنواعِها، والصَفَحاتُ الَّتي تَحقِنُ نَوعاً كاتِباً) تُطبَعُ وتَفشَلُ تَحتَ أَرضِيَّتِها. الاستِثناءُ <see cref="PinnedDisplayWriters"/> نَفسُه.</summary>
    [Fact]
    public void No_get_or_read_scoped_endpoint_writes_through_a_service()
    {
        var sources = EntitlementContractTests.SourceFiles().ToList();
        var writers = ServiceWriters(sources);
        var methodCount = writers.Sum(kv => kv.Value.Count);
        var surfaces = DisplaySurfaces(sources);
        int Of(string kind) => surfaces.Count(s => s.Kind == kind);
        var razor = surfaces.Where(s => s.Kind == SurfaceRazor).ToList();
        var injectingWriters = razor.Where(s => s.Receivers.Values.Any(writers.ContainsKey)).Select(s => s.Where).ToList();
        output.WriteLine($"أَنواعٌ بِطَرائِقَ كاتِبَة: {writers.Count} · طَرائِقُ كاتِبَة: {methodCount} · نِقاطُ عَرض: {Of(SurfaceGet)} · بِنِطاقِ قِراءَة: {Of(SurfaceReadScoped)}" +
                         $" · [WolverineGet]: {Of(SurfaceWolverineGet)} · صَفَحاتُ razor: {razor.Count} (مِنها {injectingWriters.Count} تَحقِنُ نَوعاً كاتِباً)");
        Assert.True(methodCount >= 100, $"أَداةٌ عَمياء: {methodCount} طَريقَةً كاتِبَة — والمَقيسُ ‏147 (‏2026-10-25).");
        Assert.True(writers.TryGetValue("DealsService", out var deals) && deals.Contains("CancelAsync") && deals.Contains("AdvanceAsync"),
            "أَداةٌ عَمياء: DealsService.CancelAsync/AdvanceAsync لا يُعَدّانِ كاتِبَين.");
        // ‏ومِرساةُ ‏ADR-020 §٥ بِعَينِها: التَصديرُ يَكتُبُ قَيدَ تَدقيقٍ عَبرَ حَقلِه `_audit` (نَوعٌ ثالِث) — فَهُوَ كاتِبٌ هُنا.
        Assert.True(writers.TryGetValue("TenantExportService", out var export) && export.Contains("ProduceAsync"),
            "أَداةٌ عَمياء: TenantExportService.ProduceAsync (قَيدُ التَدقيقِ عَبرَ AuditWriter) لا يُعَدُّ كاتِباً.");
        Assert.True(Of(SurfaceGet) >= 10 && Of(SurfaceReadScoped) >= 2,
            $"أَداةٌ عَمياء: {Of(SurfaceGet)} نُقطَةَ عَرض و{Of(SurfaceReadScoped)} بِنِطاقِ قِراءَة.");
        Assert.True(razor.Count >= 140, $"أَداةٌ عَمياء: {razor.Count} صَفحَةَ razor — والمَقيسُ ‏167 (‏2026-10-25).");
        Assert.True(Of(SurfaceWolverineGet) >= 2, $"أَداةٌ عَمياء: {Of(SurfaceWolverineGet)} نُقطَةَ [WolverineGet] — والمَقيسُ ‏2 (SeoHandlers).");
        var unread = surfaces.Where(s => s.Kind == SurfaceWolverineGet && s.Body.Length == 0).Select(s => $"{s.Where} ({s.File})").ToArray();
        Assert.True(unread.Length == 0, "‏[WolverineGet] لَم يَقرَأ الماسِحُ طَريقَتَه — فَلا يُسأَلُ عَن شَيء:\n  " + string.Join("\n  ", unread));
        // ‏ومِرساةُ هذِه الجَولَة: الصَفحَةُ الَّتي حُقِنَ فيها العَيبُ تُقرَأُ مُستَقبِلاً بِنَوعِه — وإلّا فَالعَدُّ أَعلاه عَدُّ مِلَفّاتٍ لا قِراءَتُها.
        Assert.True(injectingWriters.Count >= 45 && injectingWriters.Any(w => w.EndsWith("/MyDealDetail.razor", StringComparison.Ordinal)),
            $"أَداةٌ عَمياء: {injectingWriters.Count} صَفحَةً تَحقِنُ نَوعاً كاتِباً — والمَقيسُ ‏49 (‏2026-10-25)، ومِنها MyDealDetail.razor (‏Deals ← DealsService).");

        var typesByFile = TypesByFile(sources);
        var pinned = FullyPinnedDisplayWriters();
        var breaches = surfaces
            .Where(s => !pinned.Contains(s.Where))
            .Select(WithoutPinnedHandlers)
            .Select(s => (s, Called: SurfaceWrites(s, writers, typesByFile.GetValueOrDefault(s.File, []))))
            .Where(x => x.Called.Count > 0)
            .Select(x => $"{x.s.Where} ({x.s.File}) ← {string.Join(", ", x.Called)}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.True(breaches.Length == 0,
            "سَطحُ عَرضٍ (`GET`، أَو صَفحَةُ razor، أَو `[WolverineGet]`) أَو نِطاقُ قِراءَةٍ يَكتُبُ عَبرَ خِدمَة — الفِعلُ المُعلَنُ قِراءَةٌ والواقِعُ كِتابَة:\n  " +
            string.Join("\n  ", breaches) +
            "\nإمّا أَن تَصيرَ `POST` صَريحاً بِنِطاقِ كِتابَة (ولِلصَفحَة: أَمرٌ صَريحٌ يُنادى بَعدَ التَصيير كَـ`POST …/view`)، " +
            "أَو تُثَبَّتَ في PinnedDisplayWriters بِسَبَبِها في نَفسِ الكوميت.");
    }

    /// <summary><b>حَقنُ عَيب — الفاحِصُ أَعلاه يُحمِرُّ فِعلاً</b>: الطَفرَةُ M2c بِعَينِها، وكاتِبٌ مُتَعَدٍّ (طَريقَةٌ تُنادي كاتِبَةً مِن نَوعِها)،
    /// وخِدمَةٌ مَحلولَةٌ مِن الحاوي، ونِداءٌ ساكِن، وكاتِبٌ مِن نَوعِ المِلَفِّ بِلا مُستَقبِل — كُلٌّ يُمسَك؛ وقِراءَةٌ خالِصَةٌ وكاتِبٌ مُسَمّىً في تَعليقٍ لا يُمسَكان.</summary>
    [Fact]
    public void The_service_writer_scan_can_actually_go_red()
    {
        const string Service = """
            public sealed class FakeAudit(IDocumentSession s)
            {
                public async Task WriteAsync(string what) { s.Store(new { what }); await s.SaveChangesAsync(); }
                public Task<int> CountAsync() => Task.FromResult(0);
            }
            public sealed class FakeExport
            {
                private readonly FakeAudit _audit;
                public async Task ProduceAsync() => await _audit.WriteAsync("export");
                public async Task<int> PeekAsync() => await _audit.CountAsync();
            }
            public sealed class FakeDealsService
            {
                public async Task<Deal?> LoadAsync(Guid id) => await _q.LoadAsync<Deal>(id);
                public async Task CancelAsync(Guid id) { var d = await LoadAsync(id); _s.Store(d); await _s.SaveChangesAsync(); }
                public Task ArchiveAsync(Guid id) => CancelAsync(id);
                private static void Touch(IDocumentSession s) => s.Events.Append(Guid.Empty, new object());
                public static void Stamp(IDocumentSession s) { Touch(s); }
            }
            """;
        var writers = ServiceWriters([("x/FakeDealsService.cs", Service)]);
        Assert.Equal(new[] { "ArchiveAsync", "CancelAsync", "Stamp", "Touch" }, writers["FakeDealsService"].Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "ProduceAsync" }, writers["FakeExport"].ToArray());                 // ‏عَبرَ حَقلٍ مِن نَوعٍ كاتِب — و«Peek» قِراءَة
        Assert.Equal(new[] { "WriteAsync" }, writers["FakeAudit"].ToArray());

        const string Head = ".MapGet(\"/api/v1/deals/{id:guid}\", async (Guid id, HttpContext http, FakeDealsService deals) =>\n{\n";
        const string Tail = "\n}).RequireApiKey(ApiScopeCatalog.DealsRead);";
        var file = new[] { "Routes", "FakeDealsService" };
        var cases = new (string Name, string Body, string[] Expected)[]
        {
            ("M2c: GET يُنادي CancelAsync", Head + "var deal = await deals.LoadAsync(id); await deals.CancelAsync(id);" + Tail, ["FakeDealsService.CancelAsync"]),
            ("كاتِبٌ مُتَعَدٍّ", Head + "await deals.ArchiveAsync(id);" + Tail, ["FakeDealsService.ArchiveAsync"]),
            ("خِدمَةٌ مِن الحاوي", ".MapGet(\"/x\", async (HttpContext http) =>\n{ var svc = http.RequestServices.GetRequiredService<FakeDealsService>(); await svc.CancelAsync(Guid.Empty); });", ["FakeDealsService.CancelAsync"]),
            ("نِداءٌ ساكِن", ".MapGet(\"/x\", (IDocumentSession s) => { FakeDealsService.Stamp(s); return Results.Ok(); });", ["FakeDealsService.Stamp"]),
            ("بِلا مُستَقبِلٍ مِن نَوعِ المِلَفّ", ".MapGet(\"/x\", (IDocumentSession s) => { Stamp(s); return Results.Ok(); });", ["FakeDealsService.Stamp"]),
            ("قِراءَةٌ خالِصَة", Head + "var deal = await deals.LoadAsync(id); return Results.Ok(deal);" + Tail, []),
            ("GET يُصَدِّرُ عَبرَ خِدمَةٍ تَكتُبُ عَبرَ حَقلِها", ".MapGet(\"/x/export\", async (FakeExport export) => { await export.ProduceAsync(); });", ["FakeExport.ProduceAsync"]),
            ("GET يَقرَأُ عَبرَ الخِدمَةِ نَفسِها", ".MapGet(\"/x/peek\", async (FakeExport export) => await export.PeekAsync());", []),
            ("وَسيطٌ بِاسمٍ آخَر لا يُخلَط", ".MapGet(\"/x\", async (Guid id, OtherService deals) => await deals.CancelAsync(id));", []),
        };
        var wrong = new List<string>();
        foreach (var c in cases)
        {
            var got = WritersCalled(StripComments(c.Body), writers, file);
            output.WriteLine($"«{c.Name}» → {(got.Count == 0 ? "لا شَيء" : string.Join(", ", got))}");
            if (!got.SequenceEqual(c.Expected)) wrong.Add($"«{c.Name}»: [{string.Join(", ", got)}] لا [{string.Join(", ", c.Expected)}]");
        }
        // ‏وكاتِبٌ مُسَمّىً في تَعليقٍ لا يُعَدُّ نِداءً — الجِسمُ يُقرَأُ بَعدَ `StripComments` كَما في الفاحِص.
        Assert.Empty(WritersCalled(StripComments(Head + "// await deals.CancelAsync(id);\nreturn Results.Ok();" + Tail), writers, file));
        output.WriteLine($"حُقِنَ {cases.Length} جِسماً؛ أَخطَأَ الفاحِصُ في {wrong.Count}");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
        Assert.Matches(ReadScope, Head + Tail);
    }

    /// <summary><b>وتَثبيتُ المُعالِجِ يُقاسُ لا يُصَدَّق</b> (‏2026-10-25، مُراجَعَةُ W8-5 الجَولَة 1): كُلُّ مُعالِجٍ في <c>InHandlers</c> مُعَرَّفٌ في صَفحَتِه،
    /// ومَربوطٌ بِحَدَثٍ في تَرميزِها، ويُنادي كاتِبَةً، ولا يُذكَرُ خارِجَ تَعريفِه ورَبطِه — وإلّا فَإمّا أَنّ التَثبيتَ عاشَ سَبَبَه (فَيُرفَع)، أَو أَنّ
    /// الكِتابَةَ صارَت تُنادى مِن التَصيير (فَالتَثبيتُ يَستُرُ ما كُتِبَ لِيَكشِفَه).</summary>
    [Fact]
    public void Every_pinned_display_handler_is_an_event_binding_that_writes()
    {
        var sources = EntitlementContractTests.SourceFiles().ToList();
        var writers = ServiceWriters(sources);
        var typesByFile = TypesByFile(sources);
        var pages = RazorPages(sources).ToDictionary(s => s.Where, StringComparer.Ordinal);
        var pins = PinnedDisplayWriters.Where(p => p.InHandlers is not null).ToList();
        output.WriteLine($"تَثبيتاتُ مُعالِجات: {pins.Count} · مُعالِجاتٌ مُسَمّاة: {pins.Sum(p => p.InHandlers!.Length)} · صَفَحاتٌ مَقروءَة: {pages.Count}");
        Assert.True(pages.Count >= 140, $"أَداةٌ عَمياء: {pages.Count} صَفحَةَ razor — والمَقيسُ ‏167 (‏2026-10-25).");

        var wrong = new List<string>();
        foreach (var p in pins)
        {
            Assert.True(p.InHandlers!.Length > 0, $"«{p.Where}»: تَثبيتُ مُعالِجاتٍ بِلا مُعالِج — يَصيرُ تَثبيتاً لِلصَفحَةِ كُلِّها خِلسَة.");
            if (!pages.TryGetValue(p.Where, out var page)) { wrong.Add($"{p.Where}: لا صَفحَةَ بِهذا المَسار"); continue; }
            foreach (var b in HandlerPinBreaches(page.Body, p.InHandlers, writers, typesByFile.GetValueOrDefault(page.File, [])))
                wrong.Add($"{p.Where}: {b}");
        }
        Assert.True(wrong.Count == 0,
            "تَثبيتُ مُعالِجٍ لا يَصدُق — `handler_absent`/`handler_unbound`/`handler_silent` تَعني أَنّه عاشَ سَبَبَه فَيُرفَع، " +
            "و`handler_called_outside` أَنّ الكِتابَةَ قَد تُنادى في التَصيير:\n  " + string.Join("\n  ", wrong));
    }

    /// <summary><b>حَقنُ عَيب — صَفَحاتُ razor و<c>[WolverineGet]</c> وتَثبيتُ المُعالِجِ تُحمِرُّ فِعلاً</b> (‏2026-10-25، مُراجَعَةُ W8-5 الجَولَة 1):
    /// الطَفرَةُ الَّتي عَبَرَت (إلغاءٌ في <c>OnInitializedAsync</c> بَعدَ <c>LoadAsync</c>)، وكاتِبٌ في تَعبيرِ تَرميز، و<c>[Inject]</c>، وخِدمَةٌ مِن الحاوي —
    /// تُمسَك؛ وقِراءَةٌ خالِصَة، وكاتِبٌ في تَعليقِ razor، ووَسيطٌ بِالاسمِ نَفسِه مِن نَوعٍ آخَر — لا تُمسَك. و<c>[WolverineGet]</c> بِوَسيطِه، وبِسِمَةٍ
    /// مُؤَهَّلَةٍ وجِسمٍ بِسَهم، وبِرَمزِ Marten مُباشِر — تُمسَك؛ وقِراءَتُه الخالِصَةُ لا تُمسَك، وسِمَتُه عَلى غَيرِ طَريقَةٍ تُعادُ فارِغَةً لِيُحمِرَّها الفاحِص.
    /// ولِكُلِّ رَمزِ خَرقٍ في <see cref="HandlerPinBreaches"/> عَيِّنَةٌ تُحمِرُّه وَحدَه، والتَبييضُ لا يَستُرُ كاتِباً في التَصيير.</summary>
    [Fact]
    public void The_razor_page_and_wolverine_get_scan_can_actually_go_red()
    {
        const string Service = """
            public sealed class FakeDealsService(IDocumentSession s)
            {
                public Task<Deal?> LoadAsync(Guid id) => s.LoadAsync<Deal>(id);
                public async Task CancelAsync(Guid id) { s.Store(new { id }); await s.SaveChangesAsync(); }
                public Task ArchiveAsync(Guid id) => CancelAsync(id);
            }
            """;
        var writers = ServiceWriters([("x/FakeDealsService.cs", Service)]);
        Assert.Equal(new[] { "ArchiveAsync", "CancelAsync" }, writers["FakeDealsService"].Order(StringComparer.Ordinal).ToArray());
        static string At(string rel) => Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, rel);
        const string Inject = "@page \"/x/{id:guid}\"\n@inject Services.Deals.FakeDealsService Deals\n";
        var wrong = new List<string>();
        void Check(string name, DisplaySurface s, string[] expected)
        {
            var got = SurfaceWrites(s, writers, TypeDecl.Matches(s.Body).Select(m => m.Groups["name"].Value).ToHashSet(StringComparer.Ordinal));
            output.WriteLine($"«{name}» → {(got.Count == 0 ? "لا شَيء" : string.Join(", ", got))}");
            if (!got.SequenceEqual(expected)) wrong.Add($"«{name}»: [{string.Join(", ", got)}] لا [{string.Join(", ", expected)}]");
        }

        var pages = new (string Name, string Text, string[] Expected)[]
        {
            ("إلغاءٌ في OnInitializedAsync بَعدَ LoadAsync (طَفرَةُ MyDealDetail)",
                Inject + "<h1>@deal?.Id</h1>\n@code {\n    Deal? deal;\n    protected override async Task OnInitializedAsync()\n    {\n        deal = await Deals.LoadAsync(Id);\n" +
                "        if (deal is not null) await Deals.CancelAsync(deal.Id);\n    }\n}\n", ["FakeDealsService.CancelAsync"]),
            ("كاتِبٌ في تَعبيرِ تَرميز", Inject + "<p>@(Deals.ArchiveAsync(Id).IsCompleted)</p>\n", ["FakeDealsService.ArchiveAsync"]),
            ("[Inject] في @code", "@page \"/x\"\n@code {\n    [Inject] public Services.Deals.FakeDealsService Svc { get; set; } = default!;\n" +
                "    protected override Task OnInitializedAsync() => Svc.CancelAsync(Id);\n}\n", ["FakeDealsService.CancelAsync"]),
            ("خِدمَةٌ مِن الحاوي", "@page \"/x\"\n@inject IServiceProvider Sp\n@code {\n    protected override async Task OnInitializedAsync()\n    {\n" +
                "        var svc = Sp.GetRequiredService<FakeDealsService>();\n        await svc.CancelAsync(Id);\n    }\n}\n", ["FakeDealsService.CancelAsync"]),
            ("قِراءَةٌ خالِصَة", Inject + "@code {\n    protected override async Task OnInitializedAsync() => deal = await Deals.LoadAsync(Id);\n}\n", []),
            ("كاتِبٌ في تَعليقِ razor", Inject + "@* await Deals.CancelAsync(Id); *@\n<p>@deal</p>\n", []),
            ("وَسيطٌ بِالاسمِ نَفسِه مِن نَوعٍ آخَر", "@page \"/x\"\n@inject OtherService Deals\n@code {\n    protected override Task OnInitializedAsync() => Deals.CancelAsync(Id);\n}\n", []),
        };
        var n = 0;
        foreach (var (name, text, expected) in pages)
            Check(name, RazorPages([(At($"x/P{n++}.razor"), text)]).Single(), expected);

        var handlers = new (string Name, string Text, string[] Expected)[]
        {
            ("[WolverineGet] يُلغي عَبرَ وَسيطِه", "public static class H\n{\n    [WolverineGet(\"/x/{id}\")]\n" +
                "    public static async Task Get(Guid id, FakeDealsService deals) { await deals.CancelAsync(id); }\n}\n", ["FakeDealsService.CancelAsync"]),
            ("سِمَةٌ مُؤَهَّلَةٌ بَعدَ سِمَةٍ وجِسمٌ بِسَهم", "public static class H\n{\n    [NotTenanted]\n    [Wolverine.Http.WolverineGet(\"/y\")]\n" +
                "    public static Task Y(FakeDealsService d, Guid id) => d.ArchiveAsync(id);\n}\n", ["FakeDealsService.ArchiveAsync"]),
            ("رَمزُ Marten مُباشِرٌ — لا صَفَّ غَيرُه يَسأَلُه", "public static class H\n{\n    [WolverineGet(\"/z\")]\n" +
                "    public static async Task Z(IDocumentSession session) { session.Store(new { }); await session.SaveChangesAsync(); }\n}\n", ["SaveChangesAsync", ".Store("]),
            ("قِراءَةٌ خالِصَة", "public static class H\n{\n    [WolverineGet(\"/r\")]\n    public static Task<Deal?> R(Guid id, FakeDealsService deals) => deals.LoadAsync(id);\n}\n", []),
        };
        foreach (var (name, text, expected) in handlers)
            Check(name, WolverineGetHandlers([(At("x/H.cs"), text)]).Single(), expected);
        var unread = WolverineGetHandlers([(At("x/U.cs"), "public static class U\n{\n    [WolverineGet(\"/u\")]\n    public static Func<Task> Field = () => Task.CompletedTask;\n}\n")]).Single();
        output.WriteLine($"«سِمَةٌ عَلى غَيرِ طَريقَة» → نَصٌّ بِطولِ {unread.Body.Length}");
        if (unread.Body.Length != 0) wrong.Add("«سِمَةٌ عَلى غَيرِ طَريقَة»: قُرِئَت طَريقَةً — فَالفاحِصُ لا يُحمِرُّ ما لا يَقرَؤه");

        // ‏تَثبيتُ المُعالِج: صَفحَةٌ تَفاعُلِيَّةٌ نَظيفَة، ثُمَّ عَيِّنَةٌ لِكُلِّ رَمزِ خَرق.
        const string Interactive = "@inject Services.Deals.FakeDealsService Deals\n<button @onclick=\"SendAsync\">x</button>\n" +
            "<button @onclick=\"() => DropAsync(Id)\">y</button>\n@code {\n    protected override async Task OnInitializedAsync() => deal = await Deals.LoadAsync(Id);\n" +
            "    async Task SendAsync() { await Deals.CancelAsync(Id); }\n    async Task DropAsync(Guid id) { await Deals.ArchiveAsync(id); }\n}\n";
        var pinCases = new (string Name, string Text, string[] Handlers, string[] Expected)[]
        {
            ("مُعالِجانِ مَربوطانِ يَكتُبان", Interactive, ["SendAsync", "DropAsync"], []),
            ("مُعالِجٌ مُسَمّىً لا تَعريفَ لَه", Interactive, ["SendAsync", "GoneAsync"], ["handler_absent:GoneAsync"]),
            ("مُعالِجٌ بِلا رَبطِ حَدَث", Interactive.Replace("@onclick=\"SendAsync\"", "title=\"x\""), ["SendAsync"], ["handler_unbound:SendAsync"]),
            ("مُعالِجٌ لا يَكتُب", Interactive.Replace("await Deals.CancelAsync(Id);", "await Deals.LoadAsync(Id);"), ["SendAsync"], ["handler_silent:SendAsync"]),
            ("مُعالِجٌ يُنادى في التَصيير", Interactive.Replace("deal = await Deals.LoadAsync(Id);", "await SendAsync();"), ["SendAsync"], ["handler_called_outside:SendAsync"]),
        };
        foreach (var (name, text, names, expected) in pinCases)
        {
            var page = RazorPages([(At("x/I.razor"), text)]).Single();
            var got = HandlerPinBreaches(page.Body, names, writers, []);
            output.WriteLine($"«{name}» → {(got.Count == 0 ? "لا خَرق" : string.Join(", ", got))}");
            if (!got.SequenceEqual(expected)) wrong.Add($"«{name}»: [{string.Join(", ", got)}] لا [{string.Join(", ", expected)}]");
        }
        // ‏والتَبييضُ يُعفي المُعالِجَينِ المُسَمَّيَينِ وَحدَهُما: الصَفحَةُ النَظيفَةُ لا شَيء، وكاتِبٌ يُضافُ إلى التَصييرِ يُمسَكُ رَغمَ التَثبيت.
        var clean = WithoutHandlers(RazorPages([(At("x/I.razor"), Interactive)]).Single(), ["SendAsync", "DropAsync"]);
        Check("صَفحَةٌ تَفاعُلِيَّةٌ بَعدَ تَبييضِ مُعالِجَيها", clean, []);
        var leak = WithoutHandlers(RazorPages([(At("x/I.razor"), Interactive.Replace("deal = await Deals.LoadAsync(Id);", "await Deals.ArchiveAsync(Id);"))]).Single(),
            ["SendAsync", "DropAsync"]);
        Check("كاتِبٌ في التَصييرِ لا يَستُرُه تَثبيتُ المُعالِجات", leak, ["FakeDealsService.ArchiveAsync"]);

        output.WriteLine($"حُقِنَ {pages.Length} صَفحَةً و{handlers.Length + 1} [WolverineGet] و{pinCases.Length + 2} تَثبيتاً؛ أَخطَأَ الفاحِصُ في {wrong.Count}");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary><b>والطَفرَةُ الَّتي عَبَرَت، عَلى الصَفحَةِ الحَقيقِيَّةِ بِكاتِباتِ الشَجَرَةِ الحَقيقِيَّة</b> (‏2026-10-25، مُراجَعَةُ W8-5 الجَولَة 1):
    /// <c>MyDealDetail.razor</c> كَما هِيَ لا تُنادي كاتِبَة، وبِسَطرِ <c>Deals.CancelAsync</c> بَعدَ <c>LoadAsync</c> تُمسَكُ بِـ<c>DealsService.CancelAsync</c> —
    /// فَلا يُقالُ إنّ الفاحِصَ يَرى الصَفحَةَ وهو يَرى عَيِّنَتَها وَحدَها. وإن تَغَيَّرَ سَطرُ المِرساةِ حَمَرَ الصَفُّ لِيُنقَلَ مَعَه.
    /// <para><b>‏2026-10-25 (مُراجَعَةُ تَيّارِ W8-5 الجَولَة ٢، صُغرى)</b>: المِرساةُ تَنتَهي بِـ<c>\n</c> وكانَت تُطابَقُ عَلى <c>File.ReadAllText</c> خاماً، و<c>.razor</c>
    /// بِلا سِمَةِ <c>-text</c> — فَنُسخَةُ Windows بِـ<c>autocrlf</c> (بيئَةُ التَطويرِ في CLAUDE.md) تَحمِلُ CRLF فَيُحمِرُّ الصَفُّ كاذِباً. يُطَبَّعُ النَصُّ أَوَّلاً
    /// (نَسَقُ <c>DeployDirectionTests</c>)، ويَجري الصَفُّ عَلى نُسخَةِ CRLF مِن الصَفحَةِ أَيضاً.</para></summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_real_deal_page_goes_red_when_it_cancels_while_rendering(bool windowsCheckout)
    {
        var sources = EntitlementContractTests.SourceFiles().ToList();
        var writers = ServiceWriters(sources);
        var (file, raw) = sources.Single(f => f.File.Replace('\\', '/').EndsWith("/Components/Pages/MyDealDetail.razor", StringComparison.Ordinal));
        if (windowsCheckout) raw = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
        var text = raw.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string Anchor = "        deal = await Deals.LoadAsync(Slug, Id);\n";
        Assert.True(text.Split(Anchor).Length == 2, $"سَطرُ المِرساةِ «{Anchor.Trim()}» لَيسَ في {Rel(file)} مَرَّةً واحِدَة — اِنقُل الصَفَّ مَعَه.");
        var mutated = text.Replace(Anchor, Anchor + "        if (deal is not null) await Deals.CancelAsync(Slug, deal.Id, uid);\n");
        var types = TypesByFile([(file, text)])[Rel(file)];

        var before = SurfaceWrites(RazorPages([(file, text)]).Single(), writers, types);
        var after = SurfaceWrites(RazorPages([(file, mutated)]).Single(), writers, types);
        output.WriteLine($"{Rel(file)}: قَبلَ الحَقن → {(before.Count == 0 ? "لا شَيء" : string.Join(", ", before))} · بَعدَه → {string.Join(", ", after)}");
        Assert.Empty(before);
        Assert.Equal(new[] { "DealsService.CancelAsync" }, after);
    }

    // ─── تَوسِعَةُ ‏2026-08-30 (‏ADR-012): سُلطَةٌ فَوقَ سُلطَة ───────
    //
    // **ولِماذا تَوسِعَةٌ لا مِلَفٌّ جَديد** (القاعِدَة ٨): السُؤالُ هُوَ
    // سُؤالُ هذا المِلَفِّ بِعَينِه — «هَل تُعلِنُ نُقطَةُ الكِتابَةِ
    // حارِسَها؟» — بِزِيادَةِ دَرَجَة: نُقطَةٌ تَمَسُّ اعتِماداً
    // **يَصرِفُ مِن جَيبِنا** (`platform_key`) لا يَكفيها حارِسُ
    // المَتجَر. ومِلَفٌّ ثانٍ لِنَفسِ السُؤالِ يَنجَرِف عَن أَوَّلِه.

    private sealed record TwoTierWrite(string Route, string TenantGuard, string PlatformGuard);

    private static readonly TwoTierWrite[] TwoTierWrites =
    {
        new("/studio/apps/{slug}/providers/bind",
            "TenantAdminGuard.CanAdministerAsync", "PlatformAdminGuard.EvaluateAsync"),
    };

    /// <summary><b>نُقطَةٌ تَربِط مُزَوِّداً مِن نَوع
    /// <c>platform_key</c> تُعلِنُ حارِسَ المَنَصَّةِ فَوقَ حارِسِ
    /// المَتجَر</b> — وإلّا رَبَطَ صاحِبُ مَتجَرٍ مُزَوِّداً نَدفَعُ
    /// نَحنُ فاتورَتَه.</summary>
    [Fact]
    public void A_write_that_can_spend_platform_money_declares_both_guards()
    {
        var endpoints = MinimalApiWriteEndpoints()
            .ToDictionary(e => e.Route, e => e.Body, StringComparer.Ordinal);

        Assert.True(endpoints.Count >= 88,
            $"أَداة عَمياء: وُجِدَت {endpoints.Count} نُقطَة كِتابَة — والمَقيس ٨٨.");
        Assert.True(TwoTierWrites.Length > 0, "أَداة عَمياء: لا نُقطَةَ ذاتَ سُلطَتَين.");

        var breaches = new List<string>();

        foreach (var t in TwoTierWrites)
        {
            if (!endpoints.TryGetValue(t.Route, out var body))
            {
                breaches.Add($"{t.Route}: لا وُجودَ لَها — تُرفَع أَو يُصَحَّحُ المَسار.");
                continue;
            }

            if (!body.Contains(t.TenantGuard, StringComparison.Ordinal))
                breaches.Add($"{t.Route}: بِلا «{t.TenantGuard}».");

            if (!body.Contains(t.PlatformGuard, StringComparison.Ordinal))
                breaches.Add($"{t.Route}: بِلا «{t.PlatformGuard}» — " +
                             "و`platform_key` يَصرِفُ مِن جَيبِ المَنَصَّة.");

            // والتَرتيبُ جُزءٌ مِن الحُكم: حارِسُ المَتجَرِ أَوَّلاً،
            // فَلا يُعرَفُ بِالرَدِّ مَن يَملِكُ ماذا.
            var tenantAt = body.IndexOf(t.TenantGuard, StringComparison.Ordinal);
            var platformAt = body.IndexOf(t.PlatformGuard, StringComparison.Ordinal);
            if (tenantAt >= 0 && platformAt >= 0 && tenantAt > platformAt)
                breaches.Add($"{t.Route}: حارِسُ المَنَصَّةِ سَبَقَ حارِسَ المَتجَر.");
        }

        Assert.True(breaches.Count == 0,
            "سُلطَةٌ غَيرُ مُعلَنَةٍ في نُقطَةِ كِتابَة:\n  " + string.Join("\n  ", breaches));
    }

    // ─── ‏ADR-033 الخُطوَة 8 (تَيّارُ S8، ‏2026-10-12): الاستِثناءُ المُسَمّى مِن «سُلطَتَين» ───────
    //
    // نُقطَةُ `register` تَصرِفُ اعتِماداً مِن نَوعِ `platform_key` (الرَمزَ السوقِيَّ في التَهيئَة) **ولا تُعلِنُ `PlatformAdminGuard`
    // ولا `TenantAdminGuard`** — عَمداً: بَياناتُ التَسجيلِ بَياناتُ البائِعِ الشَخصِيَّةُ تُرسَلُ إلى طَرَفٍ ثالِثٍ وتُنشِئُ حِساباً بِاسمِه
    // وتُخَزِّنُ سِرَّه الدائِم، فَالفِعلُ فِعلُه لا فِعلُ مُديرٍ يَنوبُ عَنه (‏ADR-033 الخُطوَة 8؛ نَقضٌ مُعلَنٌ في تَرويسَتِه لِقاعِدَةِ ‏ADR-012
    // §٢‑ب «وتُعلِنُ نُقطَتُه PlatformAdminGuard»). **فَالاستِثناءُ صَفٌّ مُسَمّى بِسَبَبِه وحارِسِه** لا غِيابٌ صامِت: النُقطَةُ تُعلِنُ
    // حارِسَ هُوِيَّةِ صاحِبِ البَياناتِ (البائِع)، ولا تُعلِنُ حارِسَ مُدير، ولا تَكونُ في قائِمَةِ السُلطَتَينِ مَعاً.

    private sealed record PlatformKeySpentByItsDataOwner(string Route, string IdentityGuard, string WhyAr);

    private static readonly PlatformKeySpentByItsDataOwner[] PlatformKeySpentByDataOwners =
    {
        new("/{slug}/me/shipping/oto/register", "Services.Shipping.ListingVendorGate.Admit(",
            "‏ADR-033 الخُطوَة 8: تُنفِقُ الرَمزَ السوقِيَّ خَلفَ جَلسَةِ البائِعِ وهُوِيَّتِه وَحدَهُما — بَياناتُه الشَخصِيَّةُ تُرسَلُ إلى OTO " +
            "وتُنشِئُ حِساباً بِاسمِه، فَلا يَنوبُ عَنه مُديرُ مَتجَرٍ ولا مَنَصَّة (‏PDPL؛ نَقضُ ‏ADR-012 §٢‑ب المُعلَنُ في تَرويسَةِ ‏ADR-033)."),
    };

    [Fact]
    public void A_platform_key_spent_by_its_data_owner_is_a_named_exception_with_its_identity_guard_and_no_admin_guard()
    {
        var endpoints = MinimalApiWriteEndpoints()
            .ToDictionary(e => e.Route, e => e.Body, StringComparer.Ordinal);
        Assert.True(PlatformKeySpentByDataOwners.Length > 0, "أَداة عَمياء: لا صَفَّ استِثناء.");

        var breaches = new List<string>();
        foreach (var x in PlatformKeySpentByDataOwners)
        {
            if (!endpoints.TryGetValue(x.Route, out var body))
            {
                breaches.Add($"{x.Route}: لا وُجودَ لَها — تُرفَعُ أَو يُصَحَّحُ المَسار.");
                continue;
            }
            if (!body.Contains(x.IdentityGuard, StringComparison.Ordinal))
                breaches.Add($"{x.Route}: بِلا حارِسِ هُوِيَّةِ صاحِبِ البَيانات «{x.IdentityGuard}».");
            foreach (var admin in new[] { "PlatformAdminGuard", "TenantAdminGuard" })
                if (body.Contains(admin, StringComparison.Ordinal))
                    breaches.Add($"{x.Route}: تُعلِنُ «{admin}» — المُديرُ لا يَنوبُ عَن صاحِبِ البَيانات.");
            if (TwoTierWrites.Any(t => t.Route == x.Route))
                breaches.Add($"{x.Route}: في قائِمَةِ السُلطَتَينِ والاستِثناءِ مَعاً.");
            if (x.WhyAr.Length <= 30)
                breaches.Add($"{x.Route}: سَبَبٌ أَقصَرُ مِن أَن يَكونَ سَبَباً.");
        }

        Assert.True(breaches.Count == 0,
            "استِثناءُ «سُلطَتَين» غَيرُ مُثبَت:\n  " + string.Join("\n  ", breaches));
    }

    /// <summary>كُلّ استِثناء يُعلِن سَبَبَه — فَالقائِمَة دَينٌ مَوصوف
    /// لا قائِمَةُ إسكات.</summary>
    [Fact]
    public void Every_pinned_exception_declares_a_reason()
    {
        foreach (var p in PinnedPublic)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.WhyAr), $"«{p.Route}» بِلا سَبَب.");
            Assert.True(p.WhyAr.Length > 30, $"سَبَب «{p.Route}» أَقصَر مِن أَن يَكون سَبَباً.");
        }

        Assert.Equal(
            PinnedPublic.Select(p => p.Route).Distinct(StringComparer.Ordinal).Count(),
            PinnedPublic.Length);
    }

    // ─── الأَدَوات ────────────────────────────────────────────────────

    /// <summary><c>internal</c> لا <c>private</c>: فاحِصا البَند ٣ و‏٥
    /// (‏<c>EndpointStoreInjectionTests</c> و
    /// <c>EndpointBodyBleedTests</c>) يَقرَآنِ نَفس النِقاط.
    /// و**ماسِحٌ واحِد مَقيس أَصَحّ مِن ثَلاثَة يُظَنّ بِها** — القاعِدَة ٨:
    /// «لا أُنبوب رابِع: استَعمِل القائِم أَو أَصلِحه». وهذا الماسِح
    /// بِعَينِه هُوَ الَّذي كَشَفَ حَقنُ عَيبٍ فيه أَنَّه كانَ يَبتَلِع
    /// ‏13 نُقطَة — فَتَكرارُه يَعني تَكرارَ ذلكَ العَمى.</summary>
    internal sealed record Endpoint(string Route, string Body, string File);

    private static bool HasGuard(string body) =>
        ContainsAny(body, ChainGuards) || ContainsAny(body, BodyGuards);

    private static bool ContainsAny(string s, IEnumerable<string> needles) =>
        needles.Any(n => s.Contains(n, StringComparison.Ordinal));

    private static int FirstIndexOfAny(string s, IEnumerable<string> needles)
    {
        var best = -1;
        foreach (var n in needles)
        {
            var i = s.IndexOf(n, StringComparison.Ordinal);
            if (i >= 0 && (best < 0 || i < best)) best = i;
        }
        return best;
    }

    private static readonly Regex MapWrite =
        new(@"\.Map(?:Post|Put|Delete|Patch)\s*\(\s*""(?<route>[^""]+)""", RegexOptions.Compiled);

    /// <summary>كُلّ نُقطَة minimal API بِأَيّ فِعل — بِما فيها
    /// <c>MapGet</c>. يَلزَم فاحِصَي البَند ٣ و‏٥، ويَلزَم
    /// <c>Every_get_endpoint_that_writes_is_treated_as_a_write_endpoint</c>:
    /// نُقطَةٌ تُعلَن <c>GET</c> وتَكتُب هي نُقطَةُ كِتابَة مَهما قالَ
    /// فِعلُها.</summary>
    /// <remarks><b>و<c>MapMethods</c> كانَ ثَقباً مَقيساً في الماسِح</b>:
    /// نُقطَةٌ تُسَجَّلُ بِه لا يَراها أَيٌّ مِن الفاحِصَينِ إطلاقاً —
    /// فَلا تُعَدُّ في النَزيفِ ولا تُسأَلُ عَن حارِس. كُشِفَ يَومَ
    /// ‏2026-08-30 حينَ سُجِّلَت <c>/health</c> بِـ<c>MapMethods</c>
    /// (‏لِتُجيبَ HEAD)، فَاختَفَت مِن جَدوَلِ النَزيفِ كُلِّه بَينَما
    /// هي حاضِرَةٌ تَخدِمُ على HTTP. **الماسِحُ الَّذي يَعمى عَن شَكلِ
    /// تَسجيلٍ قائِمٍ يَقولُ «صِفرُ مُخالَفَة» ولا يُميَّزُ مِن أَداةٍ
    /// عَمياء** (القاعِدَة ١٠).</remarks>
    private static readonly Regex MapAny =
        new(@"\.Map(?:Get|Post|Put|Delete|Patch|Methods)\s*\(\s*""(?<route>[^""]+)""", RegexOptions.Compiled);

    /// <summary>البادِئَة المُؤَهَّلَة اختِيارِيَّة عَمداً: حَقنُ عَيبٍ
    /// مُصطَنَع بِـ <c>[Wolverine.Http.WolverinePost(…)]</c> عَبَرَ
    /// النُسخَة الأُولى مِن هذا النَمَط بِلا أَن تَحمَرّ. (القاعِدَة ١٠.)</summary>
    private static readonly Regex WolverineWrite =
        new(@"\[(?:[A-Za-z_][A-Za-z0-9_]*\s*\.\s*)*Wolverine(?:Post|Put|Delete|Patch)\s*\(\s*""(?<route>[^""]+)""",
            RegexOptions.Compiled);

    /// <summary>نِقاط الـ minimal API الكاتِبَة — مَع جِسمِها الكامِل
    /// مِن <c>Map…(</c> حَتّى فاصِلَة السَطر المُنهِيَة، فَتَدخُل
    /// السِلسِلَة المُعلَنَة بَعد الجِسم.</summary>
    private static IEnumerable<Endpoint> MinimalApiWriteEndpoints() => Scan(MapWrite);

    /// <summary>نِقاطُ <c>MapGet</c> وَحدَها — مَصدَرُ فاحِصِ «مَسارُ
    /// العَرض لا يَكتُب». وتُقرَأ بِنَمَطٍ خاصٍّ بِها لا بِطَرحِ
    /// الكاتِبَة مِن الكُلّ: الطَرحُ بِالمَسار يُخطِئ لَو حَمَلَ مَسارٌ
    /// واحِد فِعلَين.</summary>
    ///
    /// <para><b>و<c>MapMethods</c> يَدخُلُ هُنا مُتَحَفِّظاً</b>: قائِمَةُ
    /// أَفعالِه لا تُقرَأُ بِتَعبيرٍ نَمَطِيّ، فَإدخالُها يَجعَلُ
    /// نُقطَةَ <c>MapMethods</c> تَكتُبُ تَحمَرُّ هُنا ولَو أَعلَنَت
    /// <c>POST</c>. وهذا هُوَ الخَطَأُ المُحتَمَلُ الصَحيح: تُسأَلُ
    /// صَراحَةً بَدَلَ أَن تَمُرَّ صامِتَة. والبَديلُ — استِثناؤُها —
    /// يُعيدُ الثَقبَ الَّذي كُشِفَ اليَوم.</para>
    private static readonly Regex MapRead =
        new(@"\.Map(?:Get|Methods)\s*\(\s*""(?<route>[^""]+)""", RegexOptions.Compiled);

    private static IEnumerable<Endpoint> MinimalApiGetEndpoints() => Scan(MapRead);

    /// <summary>كُلّ نُقطَة minimal API بِأَيّ فِعل — المَصدَر الوَحيد
    /// لِفاحِصَي البَند ٣ و‏٥.</summary>
    internal static IEnumerable<Endpoint> AllMinimalApiEndpoints() => Scan(MapAny);

    private static IEnumerable<Endpoint> Scan(Regex pattern)
    {
        foreach (var (file, text) in EntitlementContractTests.SourceFiles())
        {
            var code = StripComments(text);
            foreach (Match m in pattern.Matches(code))
            {
                var body = StatementFrom(code, m.Index);
                yield return new Endpoint(m.Groups["route"].Value, body, Rel(file));
            }
        }
    }

    private static IEnumerable<Endpoint> WolverineWriteEndpoints()
    {
        foreach (var (file, text) in EntitlementContractTests.SourceFiles())
        {
            var code = StripComments(text);
            foreach (Match m in WolverineWrite.Matches(code))
                yield return new Endpoint(m.Groups["route"].Value, "", Rel(file));
        }
    }

    /// <summary>مِن مَوضِع البِدايَة حَتّى <c>;</c> الَّتي تُغلِق
    /// العِبارَة — بِعَدّ الأَقواس والأَقواس المَعقوفَة، وتَخَطّي
    /// السَلاسِل النَصّيَّة كَي لا يَخدَعَها قَوسٌ داخِلَ نَصّ.</summary>
    private static string StatementFrom(string code, int start)
    {
        int depth = 0, i = start;
        for (; i < code.Length; i++)
        {
            var c = code[i];
            if (c is '"' or '\'')
            {
                i = SkipLiteral(code, i);
                continue;
            }
            if (c is '(' or '{' or '[') depth++;
            else if (c is ')' or '}' or ']') depth--;
            else if (c == ';' && depth == 0) break;
        }
        return code[start..Math.Min(i + 1, code.Length)];
    }

    /// <summary>يَتَخَطّى سِلسِلَةً نَصّيَّة (عاديَّة أَو حَرفِيَّة
    /// <c>@""</c> أَو مُدرَجَة <c>$""</c>) ويُعيد فِهرِس آخِر مِحرَف
    /// فيها.</summary>
    internal static int SkipLiteral(string code, int i)
    {
        var quote = code[i];
        var verbatim = i > 0 && code[i - 1] == '@';
        for (var j = i + 1; j < code.Length; j++)
        {
            if (!verbatim && code[j] == '\\') { j++; continue; }
            if (code[j] != quote) continue;
            if (verbatim && j + 1 < code.Length && code[j + 1] == quote) { j++; continue; }
            return j;
        }
        return code.Length - 1;
    }

    /// <summary>
    /// <para><b>يُبَيِّض التَعليقات ويُبقي الطول</b> — فَتَبقى الفَهارِس
    /// صالِحَةً لِفَحص «الحارِس يَسبِق الكِتابَة».</para>
    ///
    /// <para><b>ولِماذا ماسِحٌ يَدَوِيّ لا <c>Regex</c>:</b> النُسخَة
    /// الأُولى هُنا كانَت <c>/\*.*?\*/</c> على نَمَط بَقِيَّة
    /// المُستَودَع، وقياسُها كَشَفَ أَنَّها <b>تَبتَلِع ١٣ نُقطَة
    /// حَقيقِيَّة</b> — مِنها <c>/studio/s/{id}/analyze</c> نَفسُها،
    /// أَي أَنّ الأَداة كانَت عَمياءَ عَن المَوضِع الَّذي كُتِبَت
    /// لَه. السَبَب: <c>/*</c> داخِلَ سِلسِلَة نَصّيَّة يَفتَح تَعليقاً
    /// وَهمِيّاً يَبتَلِع ما بَعدَه. فَالماسِح هُنا يَتَخَطّى السَلاسِل
    /// أَوَّلاً. (القاعِدَة ١٠: الأَداةُ تُقاس قَبل أَن يُوثَق بِها.)</para>
    /// </summary>
    internal static string StripComments(string s)
    {
        var b = s.ToCharArray();
        for (var i = 0; i < b.Length; i++)
        {
            var c = b[i];

            if (c is '"' or '\'')
            {
                i = SkipLiteral(s, i);
                continue;
            }

            if (c != '/' || i + 1 >= b.Length) continue;

            if (b[i + 1] == '/')
            {
                while (i < b.Length && b[i] is not ('\n' or '\r')) b[i++] = ' ';
                i--;
            }
            else if (b[i + 1] == '*')
            {
                var end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? b.Length : end + 2;
                for (; i < end; i++) if (b[i] is not ('\n' or '\r')) b[i] = ' ';
                i--;
            }
        }
        return new string(b);
    }

    /// <summary>
    /// <para><b>يُبَيِّض تَعليقات Razor و‏HTML</b> — <c>@*…*@</c> و
    /// <c>&lt;!--…--&gt;</c> — ويُبقي الطول. ويَسبِق
    /// <see cref="StripComments"/> لِأَنّ صَفحَةَ <c>.razor</c> فيها
    /// الصِنفانِ مَعاً.</para>
    ///
    /// <para><b>ولَيسَ تَرَفاً</b>: نَفسُ عِلَّةِ المُصَنِّف في الطَبَقَة
    /// الثامِنَة — شَرحٌ يَذكُر <c>SaveChangesAsync</c> لِيَقولَ «هذا ما
    /// كانَ يَقَع هُنا» يَجعَل الفاحِصَ يَتَّهِمُ الوَثيقَةَ بِأَنَّها
    /// كود. وثَلاثُ صَفَحاتٍ في هذِه المَوجَة تَحمِل بِالضَبط ذلك
    /// الشَرح.</para>
    /// </summary>
    internal static string StripMarkupComments(string s)
    {
        var b = s.ToCharArray();
        Blank("@*", "*@");
        Blank("<!--", "-->");
        return new string(b);

        void Blank(string open, string close)
        {
            var i = 0;
            while (true)
            {
                var start = new string(b).IndexOf(open, i, StringComparison.Ordinal);
                if (start < 0) return;
                var end = new string(b).IndexOf(close, start + open.Length, StringComparison.Ordinal);
                end = end < 0 ? b.Length : end + close.Length;
                for (var j = start; j < end; j++)
                    if (b[j] is not ('\n' or '\r')) b[j] = ' ';
                i = end;
            }
        }
    }

    private static string Rel(string path) =>
        Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, path).Replace('\\', '/');
}
