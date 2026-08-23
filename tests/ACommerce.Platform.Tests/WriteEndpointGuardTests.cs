using System.Text.RegularExpressions;
using Xunit;

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
public class WriteEndpointGuardTests
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
        new("/{slug}/auth/nafath/request:w", "‏[WolverinePost] بَدء نَفاذ — يُنشِئ الجَلسَة، ولا جَلسَةَ قَبلَه."),
        new("/{slug}/auth/nafath/verify:w",  "‏[WolverinePost] تَأكيد نَفاذ — يُنشِئ الجَلسَة، ولا جَلسَةَ قَبلَه."),
        new("/studio/auth/login",         "طَلَب رَمز دُخول الـ studio — هذا هُوَ الدُخول نَفسُه."),
        new("/studio/auth/email/login",   "نَظيرُ سابِقَتِها بِالبَريد — نَفسُ القَرار: الدُخول نَفسُه، ولا جَلسَةَ قَبلَه."),
        new("/studio/auth/verify",        "تَحَقُّق رَمز الـ studio — يُنشِئ الجَلسَة، فَلا جَلسَةَ قَبلَه."),
        new("/studio/begin",              "بَدء جَلسَة studio مَجهولَة قَبل الدُخول — يَكتُب cookie لا قاعِدَة بَيانات."),

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
    };

    /// <summary>حارِسٌ في الجِسم — مَقبول لِأَنَّه الواقِع الغالِب في
    /// هذا المُستَودَع، ومَرصود بِفَحص التَرتيب أَدناه.</summary>
    private static readonly string[] BodyGuards =
    {
        "PlatformAdminGuard.EvaluateAsync", "TenantAdminGuard.CanAdministerAsync",
        "StudioOwnsAsync", "auth.IsAuthenticated", "AuthSession.ResolveToken",
    };

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

        // عَدّاد: أَداةٌ تَفحَص صِفراً أَداةٌ عَمياء (القاعِدَة ١٠).
        Assert.True(endpoints.Count >= 88,
            $"أَداة عَمياء: وُجِدَت {endpoints.Count} نُقطَة كِتابَة — والمَقيس ٨٨.");

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

    /// <summary>مَوضِعُ عَرضٍ يَكتُب، مُثَبَّتٌ بِاسمِه وسَبَبِه.</summary>
    private sealed record DisplayWriter(string Where, string WhyAr);

    /// <summary>
    /// <para><b>فارِغَةٌ عَمداً، وهذا هُوَ الخَبَر.</b> كانَت ثَلاثَةً
    /// قَبلَ المَوجَة ٧: <c>ChatRoom.razor</c> تُصَفِّر عَدّادَ غَير
    /// المَقروء، و<c>Notifications.razor</c> تُعَلِّم المَعروضَ مَقروءاً،
    /// و<c>TenantListingDetail.razor</c> تُلحِق <c>ListingViewed</c> —
    /// كُلُّها في تَصيير صَفحَةٍ يُطلَب بِـ<c>GET</c>.</para>
    ///
    /// <para><b>وكُلُّ إضافَةٍ هُنا قَرارٌ يُتَّخَذ بِاليَد</b> في نَفس
    /// الكوميت الَّذي يَخرِقُ القاعِدَة — لا نَتيجَةُ نِسيان.</para>
    /// </summary>
    private static readonly DisplayWriter[] PinnedDisplayWriters =
        Array.Empty<DisplayWriter>();

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
            $"أَداة عَمياء: وُجِدَ {pages.Count} مِلَفّ .razor — والمَقيس ‏147.");

        var pinned = PinnedDisplayWriters.Select(p => p.Where).ToHashSet(StringComparer.Ordinal);

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

        var pinned = PinnedDisplayWriters.Select(p => p.Where).ToHashSet(StringComparer.Ordinal);

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

        var settled = PinnedDisplayWriters
            .Where(p => !writing.Contains(p.Where)).Select(p => p.Where).ToArray();

        Assert.True(settled.Length == 0,
            "إدخالَةٌ مُثَبَّتَة لَم تَعُد تَكتُب في مَسار عَرض — اِرفَعها:\n  " +
            string.Join("\n  ", settled));
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
    private static readonly Regex MapAny =
        new(@"\.Map(?:Get|Post|Put|Delete|Patch)\s*\(\s*""(?<route>[^""]+)""", RegexOptions.Compiled);

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
    private static readonly Regex MapRead =
        new(@"\.MapGet\s*\(\s*""(?<route>[^""]+)""", RegexOptions.Compiled);

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
    private static int SkipLiteral(string code, int i)
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
