using System.Text.Json;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// يُنشِئ <see cref="Tenant"/> فِعليّ مِن جَلسَة تَحليل مُكتَمِلَة:
/// - يَستَخرِج النَّمَط (marketplace/classifieds/rental/ondemand) → أَدوار جاهِزَة مِن RoleCatalog
/// - يُولِّد فِئات أَوَّلِيَّة مِن القِطاع
/// - يُسَكِّن المَدينَة مِن إجابات الاكتِشاف
/// - يُسنِد المِلكِيَّة (<c>OwnerUserId</c>) + المَصدَر (<c>SourceAnalysisId</c>)
/// - يَتَحَقَّق مِن عَدَم تَكرار الـ slug
/// </summary>
public sealed class TenantFromAnalysisFactory
{
    private readonly IDocumentStore _store;
    public TenantFromAnalysisFactory(IDocumentStore store) => _store = store;

    // ─── رُموزُ الخَرق — مَعجَمٌ مُغلَقٌ يَقرَؤُه القامُوسُ والاختِبار ─
    //
    // **وكانَت رَسائِلَ عَرَبِيَّةً مَكتوبَةً في الخِدمَة** تُمَرَّرُ
    // في مُعامِلِ عُنوانٍ ثُمَّ تُعرَض كَما هي — نَصٌّ يَراهُ
    // المُستَخدِم خارِجَ القامُوس (القاعِدَة ١١)، وأُختاهُ في نَفسِ
    // النُقطَة (`name_required`, `color_invalid`) رَمزان. فَصارَت
    // الأَربَعَةُ رُموزاً، وتُتَرجَم في الشاشَة.

    public const string SlugRequired = "slug_required";
    public const string SlugFormat   = "slug_format";
    public const string SlugTaken    = "slug_taken";
    public const string SlugReserved = "slug_reserved";

    /// <summary>
    /// <para><b>يَفحَص مَدى صَلاحِيَّةِ الـslug — شَكلاً وحَجزاً
    /// وتَفَرُّداً.</b> يُعيد <b>رَمزَ خَرقٍ</b> أَو <c>null</c>.</para>
    ///
    /// <para><b>والحَجزُ شَرطٌ ثالِثٌ لَم يَكُن هُنا</b>: كانَ الفَحصُ
    /// شَكلاً وتَفَرُّداً وَحدَهُما، و<c>ReservedPaths</c> مُستَهلِكُها
    /// الوَحيدُ الوَسيط. فَمَتجَرٌ سلاجُه <c>pricing</c> أَو
    /// <c>terms</c> أَو <c>contact</c> <b>يُنشَأُ بِنَجاحٍ ثُمَّ لا
    /// يُحَلُّ أَبَداً</b> — واجِهَةُ مَتجَرٍ لا تُبلَغ، بِلا رِسالَةِ
    /// خَطَإٍ ولا سَطرِ لوغ. وذاكَ أَسوَأُ مِن رَفضٍ صَريح: صاحِبُه
    /// يَظُنُّ أَنَّه بَنى (القاعِدَة ١٢).</para>
    ///
    /// <para><b>والحَجزُ قَبلَ التَفَرُّد</b>: لا مَعنى لِرِحلَةِ
    /// قاعِدَةِ بَياناتٍ لِاسمٍ لَن يُقبَلَ على أَيِّ حال.</para>
    /// </summary>
    public async Task<string?> ValidateSlugAsync(string slug, CancellationToken ct = default)
    {
        if (SlugRefusal(slug) is { } refused) return refused;

        await using var s = _store.QuerySession();
        var existing = await s.LoadAsync<Tenant>(slug, ct);
        return existing is null ? null : SlugTaken;
    }

    /// <summary><b>المَتجَرُ القائِمُ بِهذا المُعَرِّف، أَو <c>null</c></b> (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 الجَولَةُ الثانِيَة · W13-1·SEC2-C1-1 — يُلحَقُ ولا يُعادُ
    /// حَرفٌ فَوقَه): يَسأَلُه بابُ الاستوديو بَعدَ أَن يُرَدَّ الطَلَبُ عَلى مُعَرِّفٍ مَكتوب، لِيَعرِفَ مالِكَه وما كُتِبَ فيه — فَاستِمارَةُ صاحِبِه نَفسُها
    /// مُكَرَّرَةً (نَقرَتانِ يَصِلُ ثانيهِما بَعدَ أَن حُفِظَ الأَوَّل) تُجابُ بِمَتجَرِه لا بِـ<see cref="SlugTaken"/> عَنه (<c>StudioStoreCreate.Repeats</c>).</summary>
    public async Task<Tenant?> FindAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(slug)) return null;
        await using var s = _store.QuerySession();
        return await s.LoadAsync<Tenant>(slug, ct);
    }

    /// <summary>لَوحَةُ المَتجَرِ إن كانَت الاستِمارَةُ تَكرارَ صاحِبِه لِما وَلَدَه (<see cref="StudioStoreCreate.Repeats"/>)، أَو <c>null</c> — يَسأَلُه بابُ الاستوديو
    /// بَعدَ الرَفضِ وَحدَه، فَلا يَكبُرُ جِسمُ البابِ بِمَنطِقِه (‏W13-1·SEC2-C1-1).</summary>
    public async Task<string?> RepeatOfAsync(Guid ownerId, StudioStoreCreate.Form form, CancellationToken ct = default)
        => StudioStoreCreate.Repeats(await FindAsync(form.Slug, ct), ownerId, form) ? StudioStoreCreate.StorePage(form.Slug) : null;

    /// <summary>
    /// <para><b>شَكلُ مُعَرِّفِ المَتجَرِ الواحِد</b> (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·SEC-C1-2 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): يَبدَأُ بِحَرفٍ
    /// لاتينيٍّ صَغيرٍ أَو رَقَم، ثُمَّ حُروفٌ وأَرقامٌ وشَرطَة، وطولُه 63 عَلى الأَكثَر — حَدُّ مِلصَقِ المُضيفِ في DNS (‏RFC 1035)، وهو ما يَلزَمُ
    /// <c>{slug}.R</c> في W13-4. وهُوَ شَكلُ بابِ التَصديرِ (<c>TenantExportAuthorization</c>) بِحَدِّ طول.</para>
    ///
    /// <para><b>العِلَّةُ المَقيسَة</b>: كانَ <c>^[a-z0-9_-]+$</c> بِلا حَدّ، والمَحجوزُ <c>_admin</c> وَحدَه مِن أَقسامِ القاعِدَة — فَحِسابُ استوديو
    /// مُوافِقٌ أَنشَأَ <c>_studio</c> و<c>_incubator</c> و<c>_platform</c> و<c>-</c>، كُلٌّ بِـ<c>302 …?built=1</c>: وَثائِقُ مَتجَرِ <c>_studio</c>
    /// تُشارِكُ قِسمَ حِساباتِ الاستوديو، وعَميلٌ يَدخُلُه يَحمِلُ رَمزاً قَبِلَه الاستوديو جَلسَةً؛ ومالِكُ <c>_platform</c> كَتَبَ سَطرَ تَدقيقٍ في نِطاقِ
    /// المَنَصَّة؛ وبابُ التَصديرِ يَرُدُّ هذِه المَتاجِرَ نَفسَها <c>slug_reserved</c>. والآن بابٌ واحِدٌ لِلأَبوابِ الثَلاثَة (الاستوديو والمُشرِفُ وأَداةُ
    /// الوَكيل)، وأَقسامُ القاعِدَةِ الداخِلِيَّةُ مَحجوزَةٌ بِأَسمائِها في الكود (<see cref="InternalPartitions"/>) لا بِنُسَخٍ مِنها.</para>
    /// </summary>
    /// <remarks>والمَحجوزُ قَبلَ الشَكل: <c>_studio</c> و<c>_admin</c> يُقالُ لِصاحِبِهِما «مَحجوزٌ لِلمَنَصَّة» لا «حُروفٌ وأَرقامٌ وشَرطَة» — الرَدُّ
    /// هُوَ هُوَ، والرِسالَةُ تَصدُقُ في سَبَبِه؛ وتَرتيبُ الفَحصِ القائِمُ (‏<c>ReservedTenantSlugTests</c>: كُلُّ اسمٍ مَحجوزٍ بِلا نُقطَةٍ يُجابُ
    /// <c>slug_reserved</c>) باقٍ.
    /// <para><b>ويَسأَلُ مُصادِقَ اسمِ المُضيف</b> (‏2026-11-07، ADR-050 §٣ · W13-4·C2a·3 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): الشَكلُ
    /// <c>^[a-z0-9][a-z0-9-]{0,62}$</c> كانَ يَقبَلُ <c>shop-</c> و<c>ab--cd</c> و<c>xn--…</c> وطولاً بَينَ 54 و63 و<c>best-vendor</c> — والمَتجَرُ
    /// <c>{slug}.R</c> ودَورُه <c>{slug}-{rolehost}.R</c> مِلصَقانِ لا يَصلُحُ لَهُما أَيٌّ مِنها. فَصارَ الشَكلُ <see cref="StoreHostName"/> نَفسَه،
    /// وخُروقُه تُترجَمُ إلى رُموزِ الواجِهَةِ القائِمَة: المَحجوزُ ولاحِقَةُ اسمِ الدَورِ <see cref="SlugReserved"/> (اسمٌ تَملِكُه المَنَصَّةُ لِمُضيفِ
    /// دَورِ مَتجَرٍ آخَر)، وما سِواهُما <see cref="SlugFormat"/>. والمَحجوزُ قَبلَ الشَكلِ كَما كان، وأَقسامُ القاعِدَةِ قَبلَ الكُلّ. تَوصيفُه قَبلَ التَبديل
    /// <c>SlugDoorHostShapeCharacterizationTests</c> (‏5a70efa) بِبَصمَتِه.</para></remarks>
    public static string? SlugRefusal(string? slug)
    {
        if (string.IsNullOrEmpty(slug)) return SlugRequired;
        if (InternalPartitions.Contains(slug, StringComparer.OrdinalIgnoreCase)) return SlugReserved;
        var violations = StoreHostName.Validate(slug);
        if (violations.Count == 0) return null;
        return violations.Any(v => v.Code is StoreHostName.Reserved or StoreHostName.RoleSuffix) ? SlugReserved : SlugFormat;
    }

    /// <summary>أَقسامُ Marten الداخِلِيَّةُ الَّتي لا وَثيقَةَ مُستَأجِرٍ لَها — بِثَوابِتِها لا بِنُسَخِها، فَقِسمٌ يُعادُ تَسمِيَتُه يَبقى مَحجوزاً. والشَكلُ
    /// أَعلاه يَرُدُّها اليَومَ (تَبدَأُ بِـ<c>_</c>)، والقائِمَةُ تَحجُزُها ولَو اتَّسَعَ الشَكلُ يَوماً.</summary>
    public static readonly IReadOnlyList<string> InternalPartitions =
    [
        StudioAuth.Tenant,
        FeasibilityAnalysisService.IncubatorTenant,
        Audit.AuditWriter.PlatformScope,
        AgentService.AdminTenant,
    ];

    // ─── النَمَط: مَعجَمٌ مُغلَقٌ وسُقوطٌ مَكتوبٌ صَراحَةً ──────────────

    /// <summary><b>الأَنماطُ الَّتي يَعرِفُها هذا المَصنَعُ فِعلاً</b> —
    /// لا قائِمَةَ تَوثيقٍ بَل المَصدَرُ الَّذي يَقرَؤُهُ
    /// <see cref="RolesFor"/> والاختِبار.</summary>
    public static readonly IReadOnlyList<string> KnownPatterns =
        new[] { "marketplace", "classifieds", "rental", "ondemand" };

    /// <summary>
    /// <para><b>النَمَطُ الَّذي تَعِدُ بِه الشاشَةُ أَصلاً.</b> لَيسَ
    /// اختِياراً جَديداً: <see cref="DeriveSuggestion"/> كانَت تَسقُطُ
    /// إلَيهِ حَرفِيّاً مُنذُ كُتِبَت، و<c>StudioStudy.razor</c> يَملأُ
    /// استِمارَةَ البِناءِ مِنها — فَلَونُ العَلامَةِ المُعبَّأُ سَلَفاً
    /// (<c>#2563eb</c>) هُوَ لَونُ <c>marketplace</c> بِعَينِه.</para>
    /// </summary>
    public const string FallbackPattern = "marketplace";

    /// <summary>
    /// <para><b>يَرُدُّ نَمَطاً يَعرِفُهُ المَصنَعُ دائِماً.</b> الفارِغُ
    /// والمَجهولُ يَسقُطانِ إلى <see cref="FallbackPattern"/>.</para>
    ///
    /// <para><b>الكِلفَةُ الَّتي كَتَبَت هذِه الدالَّة (‏2026-08-31)</b>:
    /// كانَ الفَراغُ يَمُرُّ إلى <c>RolesFor</c> فَيَقَعُ على فَرعِ
    /// السُقوطِ الصامِتِ <c>Array.Empty&lt;Role&gt;()</c>، فَيُكتَبُ
    /// مَتجَرٌ <b>بِصِفرِ أَدوار</b>، فَيَدخُلُ «الوَضعَ الموروث» في
    /// <c>RolePermissions.Has</c> (<c>Count == 0 ⇒ true</c>): كُلُّ
    /// شَيءٍ مَسموحٌ لِكُلِّ أَحَد. وقِيسَ حَيّاً أَنّ عُضواً سَجَّلَ
    /// رَقمَ هاتِفٍ للتَوِّ قَرَأَ هَواتِفَ الأَعضاءِ وأَعادَ كِتابَةَ
    /// هُوِيَّةِ المَتجَرِ وكَتالوجِ أَدوارِه.</para>
    ///
    /// <para><b>ولِماذا هُنا لا عِندَ المُنادي</b>: المُنادونَ ثَلاثَةٌ
    /// بِعُقودٍ مُختَلِفَة — مُعالِجٌ إداريٌّ يَملأُ سَبعَةَ حُقولٍ ثُمَّ
    /// يَشتَقّ، ومَسارُ عَميلٍ يَملأُ حَقلاً واحِداً، وبَذّارُ عَيِّنَةٍ
    /// يَكتُبُ <c>"custom"</c> حَرفِيّاً. والعَطَبُ لَيسَ في أَيٍّ
    /// مِنها مُنفَرِداً بَل في أَنّ المَصنَعَ <b>كانَ يَقبَلُ نَمَطاً
    /// فارِغاً أَصلاً</b> — فَالعِلاجُ عِندَه، وإلّا تَكَرَّرَ مَعَ
    /// رابِع.</para>
    ///
    /// <para><b>ولا يُلمَسُ الوَضعُ الموروثُ نَفسُه</b>: لَه مُستَهلِكٌ
    /// حَيٌّ مَقصود (<c>theme-demo</c> يُبذَرُ بِـ<c>Roles = new()</c>
    /// عَمداً لِلَقطَةِ المَظهَر)، وأَداةُ الوَكيلِ <c>set_roles</c>
    /// تَعرِضُهُ نَصّاً. فَالمَمنوعُ <b>بُلوغُهُ مِن مَسارِ البِناء</b>،
    /// وحَذفُهُ قَرارٌ مُنتَجِيٌّ لِصاحِبِ المَشروع.</para>
    ///
    /// <para><b>(‏2026-10-28، ‏ADR-040، قَرارُ المالِك د62 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه)</b>: اتَّخَذَه المالِك — صِفرُ الأَدوارِ لا يُعطي
    /// <c>tenant.manage</c> لِأَحَد (<c>RolePermissions.Has</c>)، فَما وَصَفَه «قيسَ حَيّاً» أَعلاه لا يَقَعُ اليَومَ ولَو بَلَغَ مَتجَرٌ صِفرَ أَدوار.
    /// والأَرضِيَّةُ هُنا باقِيَة: مَتجَرٌ بِلا أَدوارٍ ما زالَ بِلا دَورٍ يَبيعُ مِنه أَحَدٌ بِاختِيار.</para>
    /// </summary>
    public static string NormalizePattern(string? pattern)
        => pattern is not null && KnownPatterns.Contains(pattern)
            ? pattern
            : FallbackPattern;

    /// <summary>اِقتِراحات مَبدَئيَّة مِن تَحليل لِيَملَأ نَموذَج الإنشاء.</summary>
    public sealed record BuildSuggestion(
        string Name, string Color, string TagLine, string City, string Pattern);

    public BuildSuggestion DeriveSuggestion(IncubatorSession session)
    {
        // نَفسُ الدالَّةِ الَّتي يَقرَؤُها <c>CreateAsync</c> — فَما
        // تُبنى عَلَيهِ الاستِمارَةُ هُوَ ما يُكتَب، بِالبِنيَةِ لا
        // بِالانضِباط. كانَ هُنا سُقوطٌ مَحَلِّيٌّ يَرى الفارِغَ وَحدَه
        // ولا يَرى <c>"custom"</c>.
        var pattern = NormalizePattern(session.SuggestedPattern);

        var rawName = (session.ProjectDescription ?? "").Split('\n').FirstOrDefault()?.Trim() ?? "تَطبيقي";
        var name = rawName.Length > 24 ? rawName[..24] + "…" : rawName;
        if (string.IsNullOrWhiteSpace(name)) name = "تَطبيقي";

        var tagLine = TryExtractFromJson(session.AnalysisJson, "summary", "verdict", maxLen: 120) ?? "";
        var color = ColorForPattern(pattern);
        var city  = Get(session.Answers, "geo") switch
        {
            "city" => "الرياض", "national" => "السعودية", _ => ""
        };

        return new BuildSuggestion(name, color, tagLine, city, pattern);
    }

    /// <summary>
    /// <para>يُنشِئ Tenant فِعليّ + يَربِطه بِالـ analysis والمالِك.
    /// يَفتَرِض أَنّ الـ slug صالِح وغَير مُستَخدَم (نادِيِ
    /// ValidateSlugAsync أَوَّلاً).</para>
    ///
    /// <para><b>و<c>authChannel</c> مُعامَلٌ لا ثابِت — وهذا هُوَ
    /// الفَرقُ بَينَ مَتجَرٍ يُبنى ويُدخَل وآخَرَ يُبنى ولا يُدخَل.</b>
    /// كانَ السَطرُ هُنا <c>AuthChannel = "phone"</c> مَكتوبَةً، وعَلى
    /// نُسخَةٍ مَضبوطَةٍ بِالبَريدِ وَحدَه — وهي تَوصِيَةُ
    /// <c>docs/DEPLOY.md</c> §٢·ب لِأَنّ المُستَضيفَ يَحجُبُ مَنافِذَ
    /// SMTP — كانَ ذلكَ يُنتِج مَتجَراً على قَناةٍ غَيرِ مُسَجَّلَة:
    /// لافِتَةٌ حَمراءُ بَدَلَ نَموذَجِ الدُخول، ولا يَفتَحُه إلّا
    /// المالِكُ بِيَدِه. القَناةُ الآنَ تُشتَقُّ في النُقطَةِ مِن
    /// <see cref="TenantAuthChannelDoor"/>، والمَصنَعُ يَكتُبُ ما
    /// أُعطِيَ ولا يَختَرِعُ.</para>
    ///
    /// <para><b>ويُدرِجُ ولا يَدهَس</b> (‏2026-11-07، مُراجَعَةُ تَيّارِ C1 · W13-1·F2 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): كانَ <c>ws.Store(tenant)</c> — upsert —
    /// فَسُؤالُ <see cref="ValidateSlugAsync"/> قَبلَه ظَنٌّ لا قُفل: حِسابانِ بِمُعَرِّفٍ واحِدٍ في اللَحظَةِ نَفسِها قيلَ لِكِلَيهِما «بُني» (‏12/12 في سِتِّ جَولاتٍ مَقيسَة)،
    /// وبَقِيَ صَفٌّ واحِدٌ مالِكُه آخِرُ كاتِب، وعُدَّ لِكِلَيهِما مَتجَر. والآنَ <c>Insert</c>: مِفتاحُ الوَثيقَةِ هُوَ القُفل، والثاني يَرمي
    /// <c>JasperFx.DocumentAlreadyExistsException</c> عِندَ الحِفظ — ويُجيبُه البابُ بِـ<see cref="SlugTaken"/> كَما يُجيبُ مَن جاءَ بَعدَ الكِتابَة.
    /// والوَثيقَةُ المَكتوبَةُ هِيَ هِيَ بايتاً (‏<c>StudioStoreBirthCharacterizationTests.GoldenSha256</c>).</para>
    ///
    /// <para><b>و<paramref name="write"/> <c>false</c> يُعيدُ الوَثيقَةَ ولا يَكتُبُها</b> (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·SEC-C1-1 — يُلحَقُ ولا يُعادُ
    /// حَرفٌ فَوقَه): بابُ الاستوديو يُدرِجُها في حِفظِ <see cref="StudioTierService.RecordStoreBuiltAsync"/> نَفسِه مَعَ عَدّادِ صاحِبِها وسُؤالِ سَقفِه —
    /// فَلا مَتجَرَ بِلا عَدٍّ ولا عَدَّ بِلا مَتجَر. والوَثيقَةُ هِيَ هِيَ في الحالَين.</para>
    /// </summary>
    public async Task<Tenant> CreateAsync(
        string slug, string name, string color, string tagLine, string city,
        string pattern, string sector, string authChannel,
        Guid ownerId, Guid? sourceAnalysisId, bool write = true,
        CancellationToken ct = default)
    {
        // **النَمَطُ يُسَوّى قَبلَ أَن يُشتَقَّ مِنهُ شَيء.** ولا يُغَيِّرُ
        // هذا الفِئات: <c>PatternToSector</c> تُعطي <c>"ecommerce"</c>
        // لِلفارِغِ ولِـ<c>"marketplace"</c> على السَواء — فَالأَثَرُ
        // مَحصورٌ في الأَدوارِ حَيثُ قُصِد.
        var normalized = NormalizePattern(pattern);
        var categories = CategoriesForSector(sector, normalized).ToList();
        var roles = RolesFor(normalized).ToList();

        var tenant = new Tenant
        {
            Id = slug, Name = name, BrandColor = color, TagLine = tagLine, City = city,
            AuthChannel = AuthChannels.NormalizeOrDefault(authChannel),
            Categories = categories,
            Roles = roles,
            OwnerUserId = ownerId,
            SourceAnalysisId = sourceAnalysisId,
            CreatedAt = DateTime.UtcNow
        };
        if (!write) return tenant;

        await using var ws = _store.LightweightSession();
        ws.Insert(tenant);
        await ws.SaveChangesAsync(ct);
        return tenant;
    }

    // ─── Helpers ──────────────────────────────────────────────────
    private static string Get(IReadOnlyDictionary<string, string> a, string k)
        => a.TryGetValue(k, out var v) ? v : "";

    private static string ColorForPattern(string pattern) => pattern switch
    {
        "marketplace" => "#2563eb",
        "classifieds" => "#0891b2",
        "rental"      => "#16a34a",
        "ondemand"    => "#ea580c",
        _             => "#7c3aed"
    };

    private static IReadOnlyList<Category> CategoriesForSector(string sector, string pattern)
    {
        // إن لَم نَعرِف القِطاع، خَمِّن مِن النَّمَط.
        var s = string.IsNullOrEmpty(sector) ? PatternToSector(pattern) : sector;
        return s switch
        {
            // **كِندٌ فارِغٌ لا `"commercial"`** (‏ADR-034، ‏2026-10-11). كانَت
            // الفِئَتانِ هُنا بِكِندِ `"commercial"` — وهي في مُعجَمِ المُستَودَعِ
            // **عَقارٌ تِجارِيّ** (مَحَلّاتُ قِطاعِ `realestate` أَدناه، ومَكاتِبُ
            // `ejar` ومَحَلّاتُه) — و`PatternProfileResolver.PatternOf` يَقرَؤُها
            // إيجاراً بِلا سَلَّة، وكَذلكَ نُسخَتُه في `MainLayout.razor`. وهذا
            // الفَرعُ مَقصِدُ مَسارِ العَميلِ كُلِّه (القِطاعُ `""` والنَمَطُ يَسقُطُ
            // إلى `marketplace`)، فَكانَ كُلُّ مَتجَرٍ مِن `/studio/begin` يولَدُ
            // بِلا استِمارَةٍ إلى `cart/add` ولا تَبويبِ سَلَّة (‏FLGAP-04). والفارِغُ
            // هُوَ ما تُبذَرُ بِه `theme-demo` وما يَكتُبُه فَرعُ السُقوطِ أَدناه:
            // «فِئَةٌ رَئيسِيَّةٌ بِلا تَجميع» (‏`Category.Kind`). والقارِئُ لَم يُمَسّ.
            "ecommerce" => new[] {
                C("products",   "مُنتَجات",  "🛍️", "", 0),
                C("deals",      "عُروض",     "🏷️", "", 1)
            },
            "fnb" => new[] {
                C("restaurants", "مَطاعِم", "🍽️", "fnb", 0),
                C("cafes",       "مَقاهي", "☕",  "fnb", 1)
            },
            "realestate" => new[] {
                C("apartments", "شُقَق",   "🏢", "residential", 0),
                C("villas",     "فِلَل",   "🏠", "residential", 1),
                C("shops",      "مَحَلّات", "🏪", "commercial",  2)
            },
            "services" => new[] {
                C("home", "خَدَمات منزليَّة", "🧹", "services", 0),
                C("auto", "خَدَمات السيارَة", "🔧", "services", 1)
            },
            "transport" => new[] {
                C("rides",    "مَشاوير", "🚗", "transport", 0),
                C("delivery", "تَوصيل",  "📦", "transport", 1)
            },
            _ => new[] { C("general", "عامّ", "📋", "", 0) }
        };
    }

    private static Category C(string slug, string label, string icon, string kind, int order)
        => new() { Slug = slug, Label = label, Icon = icon, Kind = kind, SortOrder = order };

    private static string PatternToSector(string p) => p switch
    {
        "rental" or "classifieds" => "realestate",
        "ondemand" => "transport",
        _ => "ecommerce"
    };

    public static IReadOnlyList<Role> RolesFor(string? pattern)
    {
        Role MakeRole(string slug, int order, bool isDefault = false)
        {
            var tmpl = RoleCatalog.Find(slug);
            if (tmpl is null) return new Role { Slug = slug, Label = slug, SortOrder = order, IsDefault = isDefault };
            var r = RoleCatalog.InstantiateRole(tmpl, order);
            r.IsDefault = isDefault;
            return r;
        }

        // **الفَرعُ الأَخيرُ لَم يَعُد سُقوطاً صامِتاً**: كانَ
        // <c>_ => Array.Empty&lt;Role&gt;()</c> يَبتَلِعُ الفارِغَ
        // و<c>"custom"</c> — وهُوَ مُخرَجٌ مُعلَنٌ لِـ<c>PatternMatcher</c>
        // ويَبذُرُهُ <c>IncubatorSampleSeeder</c> — فَيُنتِجُ مَتجَراً
        // مَكشوفاً. والتَسوِيَةُ تَسبِقُ الفَرزَ الآنَ، فَما مِن نَمَطٍ
        // يُخرِجُ صِفرَ أَدوار.
        return NormalizePattern(pattern) switch
        {
            "ondemand" => new[] {
                MakeRole("rider", 0, isDefault: true),
                MakeRole("driver", 1),
                MakeRole("tenant_admin", 2)
            },
            "classifieds" => new[] {
                MakeRole("customer", 0, isDefault: true),
                MakeRole("vendor", 1),
                MakeRole("tenant_admin", 2)
            },
            "rental" => new[] {
                MakeRole("customer", 0, isDefault: true),
                MakeRole("host", 1),
                MakeRole("tenant_admin", 2)
            },
            // FallbackPattern — وهُوَ مَقصِدُ كُلِّ فارِغٍ ومَجهول.
            _ => new[] {
                MakeRole("customer", 0, isDefault: true),
                MakeRole("vendor", 1),
                MakeRole("tenant_admin", 2)
            }
        };
    }

    private static string? TryExtractFromJson(string? json, string parent, string child, int maxLen)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(parent, out var p)) return null;
            if (!p.TryGetProperty(child, out var c)) return null;
            var s = c.GetString();
            if (string.IsNullOrEmpty(s)) return null;
            return s.Length <= maxLen ? s : s[..maxLen] + "…";
        }
        catch { return null; }
    }
}
