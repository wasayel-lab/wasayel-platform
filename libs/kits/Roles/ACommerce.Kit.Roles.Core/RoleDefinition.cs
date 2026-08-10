namespace ACommerce.Kit.Roles;

/// <summary>
/// <para><b>حاوِيَة نَصّ قابِل لِلتَّوطين</b>. كُلّ سِلسِلَة يَراها
/// المُستَخدِم في تَعريف الدَور (التَّسمِيَة، الوَصف، تَسمِيَة الحَقل،
/// تَسمِيَة الخِيار) تُخزَّن هُنا لا كَسِلسِلَة عارِيَة.</para>
///
/// <para><b>العَرَبيَّة إلزامِيَّة</b> — مُلِئَت مِن القِيَم القائِمَة
/// بِتَشكيلِها حَرفِيّاً. <b>والإنجليزيَّة بُنيَة جاهِزَة فارِغَة</b>
/// (<c>null</c>) — مَوضِعُها مَحجوز ولا شَيء يَقرَؤُها.</para>
///
/// <para><b>حَدّ هذه المَوجَة، مُعلَناً</b>: القِراءَة تَبقى العَرَبيَّة
/// كَما اليَوم — <see cref="Current"/> يُرجِع <see cref="Ar"/> دائِماً.
/// خِدمَة التَّوطين <c>L</c> وزِرّ اللُغَة قائِمان في المُستودَع
/// و<b>لَم يُربَطا</b> هُنا: الرَّبط تَغيير سُلوكيّ، والمَوجَة شَرطُها
/// ألّا يَتَغَيَّر السُلوك بَتّاً. اللُغَة الثانِيَة تُملَأ ويُربَط
/// الاختِيار في مَوجَة لاحِقَة، بِلا تَغيير شَكل.</para>
/// </summary>
/// <param name="Ar">العَرَبيَّة — إلزامِيَّة، بِتَشكيلِها.</param>
/// <param name="En">الإنجليزيَّة — <c>null</c> اليَوم في كُلّ القَوالِب.</param>
public sealed record LocalizedText(string? Ar, string? En = null)
{
    public static readonly LocalizedText Empty = new(null, null);

    /// <summary>النَّصّ المَقروء اليَوم — العَرَبيَّة حَصراً (انظُر
    /// حَدّ المَوجَة أَعلاه). <c>null</c> يُعامَل سِلسِلَة فارِغَة
    /// لِيَبقى العَقد مَع <c>RoleTemplate</c> غَير قابِل لِلـ null.
    /// <b>مُستَثنىً مِن التَّسَلسُل</b> لِأَنَّه مُشتَقّ مِن
    /// <see cref="Ar"/> لا بَيان مُستَقِلّ.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Current => Ar ?? "";
}

/// <summary>خِيار واحِد في حَقل اختِياريّ — قيمَتُه ASCII ثابِتَة
/// (تُخزَّن في <c>User.RoleAttributesJson</c>) وتَسمِيَتُه مُوَطَّنَة.</summary>
public sealed record RoleFieldOptionDefinition
{
    public string Value { get; init; } = "";
    public LocalizedText Label { get; init; } = LocalizedText.Empty;
}

/// <summary>حَقل بَيانات في تَعريف دَور — نَفس <see cref="RoleField"/>
/// شَكلاً، والفَرق الوَحيد أَنّ التَّسمِيات حاوِيات تَوطين.</summary>
public sealed record RoleFieldDefinition
{
    public string Code { get; init; } = "";
    public LocalizedText Label { get; init; } = LocalizedText.Empty;

    /// <summary>مِن <see cref="RoleFieldTypes.All"/> حَصراً — مُتَوافِق
    /// مَع <c>DynFieldType</c>.</summary>
    public string Type { get; init; } = "Text";

    public bool IsRequired { get; init; }
    public IReadOnlyList<RoleFieldOptionDefinition> Options { get; init; } = [];
}

/// <summary>
/// <para><b>تَركيب واجِهَة الدَور كَبَيانات مَوصوفَة</b> — التِقاط لِما
/// يَفعَلُه الكود اليَوم، لا اقتِراح لِما قَد يَفعَلُه. كُلّ قيمَة هُنا
/// مَقروءَة مِن فَرع فِعليّ في الشَجَرَة (مَراجِعُها في
/// <see cref="RoleComponents"/>).</para>
///
/// <para><b>حَدّ هذه المَوجَة، مُعلَناً وبِلا تَلميع</b>: <b>لا شَيء
/// يَقرَأ هذا القِسم بَعد</b>. التَّصيير ما يَزال يَتَفَرَّع بِـ
/// <c>switch</c> عَلى <c>CatalogSlug</c> في <c>TenantHome.razor</c>
/// و<c>CreateListing.razor</c> و<c>MainLayout.BuildNav</c>
/// و<c>TenantExplore.razor</c>. قيمَتُه الآن ثَلاثَة أَشياء بِلا رابِع:
/// تَوثيق التَّركيب في مَوضِع واحِد بَدَل سِتَّة، ومُصادَقَة المَعجَم
/// (مُكَوِّن خارِج القائِمَة يُرفَض)، وجاهِزيَّة المَوجَة الثانِيَة الَّتي
/// تَجعَل التَّصيير يَقرَؤُه. رَبطُه بِالتَّصيير الآن تَغيير سُلوكيّ،
/// والمَوجَة شَرطُها ألّا يَتَغَيَّر السُلوك بَتّاً.</para>
/// </summary>
/// <param name="Home">قالِب الصَفحَة الرَّئيسِيَّة (<c>TenantHome.razor</c>).</param>
/// <param name="CreateListing">نَموذَج إنشاء الإعلان (<c>CreateListing.razor</c>).</param>
/// <param name="Nav">فَرع شَريط التَّنَقُّل (<c>MainLayout.BuildNav</c>).</param>
/// <param name="Explore">وَضع صَفحَة الاستِكشاف (<c>TenantExplore.razor</c>).</param>
/// <param name="PublicProfile">الصَفحَة العامَّة لِحامِل الدَور، أَو
/// <c>null</c> لِمَن لا صَفحَة عامَّة لَه. <b>وهي مَوضِع التَّقييمات
/// الوَحيد</b> — انظُر التَّفصيل في <see cref="RoleComponents"/>.</param>
/// <param name="Extras">سُطوح إضافِيَّة يَبلُغُها هذا الدَور تَحديداً.</param>
public sealed record RoleComposition
{
    public string Home { get; init; } = RoleComponents.DefaultHome;
    public string CreateListing { get; init; } = RoleComponents.DefaultCreateForm;
    public string Nav { get; init; } = RoleComponents.DefaultNav;
    public string Explore { get; init; } = RoleComponents.DefaultExplore;
    public string? PublicProfile { get; init; }
    public IReadOnlyList<string> Extras { get; init; } = [];

    /// <summary>كُلّ المُكَوِّنات المُشار إلَيها — لِلمُصادَقَة.</summary>
    public IEnumerable<string> AllComponents()
    {
        yield return Home;
        yield return CreateListing;
        yield return Nav;
        yield return Explore;
        if (PublicProfile is not null) yield return PublicProfile;
        foreach (var e in Extras) yield return e;
    }
}

/// <summary>
/// <para><b>تَعريف دَور كَبَيانات</b> — الأَثَر التَّصريحيّ الَّذي حَلَّ
/// مَحَلّ القالِب المُجَمَّع في <see cref="RoleCatalog"/> (META-MODEL §6).
/// سِجِلّ خالِص: لا Marten ولا HTTP ولا أَيّ خِدمَة — يُقرَأ اليَوم مِن
/// مِلَفّ JSON مَضمون في العُدَّة، ويُقرَأ غَداً مِن وَثيقَة لِكُلّ
/// مُستَأجِر بِلا تَغيير في شَكلِه.</para>
///
/// <para>الصِحَّة البُنيَوِيَّة تُفحَص بِدَوالّ نَقِيَّة في
/// <see cref="RoleDefinitionValidator"/>، وهي مَفروضَة <b>بَوّابَةً</b>
/// عِندَ التَّحميل: تَعريف فاسِد يُفشِل الإقلاع بِرَمز الخَرق، لا يَمُرّ
/// صامِتاً.</para>
///
/// <para><b>التَّقييمات — ما وُجِدَ لا ما يُشتَهى</b>: مَسح عُدَّة
/// <c>ACommerce.Kit.Reviews</c> كامِلَةً أَظهَرَ أَنّ <c>Review</c>
/// <b>مُحايِد الدَور تَماماً</b>: يَستَهدِف <c>TargetUserId</c> (مُستَخدِماً
/// لا دَوراً)، ولا حَقل دَور فيه، ولا فَرع دَور في
/// <c>ReviewsService</c> ولا في <c>ReviewSummary</c>. ما فيه سِياقاً هو
/// <c>DealPattern</c> — نَمَط الصَفقَة لا الدَور. لِذلك <b>لا تَهيِئَة
/// تَقييمات في هذا التَّعريف</b>: اختِراع مِفتاح لا يَستَهلِكُه شَيء
/// أَسوَأ مِن غِيابِه. عَلاقَة الدَور بِالتَّقييم قائِمَة في مَوضِع واحِد
/// فَقَط وهو <b>مَوصوف هُنا فِعلاً</b>: <see cref="RoleComposition.PublicProfile"/>
/// — صَفحَة <c>vendorProfile</c> هي الوَحيدَة الَّتي تَعرِض النُّجوم
/// وعَدّاد التَّقييمات، وهي صَفحَة الدَورَين <c>vendor</c> و<c>host</c>
/// دونَ سِواهُما. (وثَمَّة عَدَم تَماثُل مُوَثَّق: السائِق هَدَف تَقييم
/// صَريح في تَعليق <c>Review</c> نَفسِه، ومَع ذلك <c>driversList</c>
/// لا يَعرِض لَه نَجمَةً واحِدَة — امتِداد مُستَقبَليّ مُعَلَّم، لا
/// تَهيِئَة اليَوم.)</para>
/// </summary>
public sealed record RoleDefinition
{
    /// <summary>مُعَرِّف ASCII فَريد — بِنَمَط <c>^[a-z][a-z0-9_]*$</c>.</summary>
    public string Slug { get; init; } = "";

    /// <summary>رَمز تَعبيريّ افتِراضيّ. يُحَوَّل إلى أَيقونَة عَبر
    /// <c>AcIconMap.Resolve</c> في التَّصيير.</summary>
    public string Icon { get; init; } = "";

    /// <summary>المَسار بَعد الدُخول. فارِغ = الصَفحَة الافتِراضِيَّة،
    /// وإلّا يَبدَأ بِـ <c>/</c>.</summary>
    public string HomeRoute { get; init; } = "";

    public LocalizedText Label { get; init; } = LocalizedText.Empty;
    public LocalizedText Description { get; init; } = LocalizedText.Empty;

    /// <summary>مِن <see cref="PermissionCatalog.All"/> حَصراً،
    /// <b>بِتَرتيبِها</b> — التَّرتيب جُزء مِن اللَّقطَة المُوَصَّفَة.</summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];

    public IReadOnlyList<RoleFieldDefinition> Fields { get; init; } = [];

    public RoleComposition Composition { get; init; } = new();

    /// <summary>
    /// <para><b>نَمَط الصَفقَة الَّذي يَجُرّ إلَيه هذا الدَور</b> — مِن
    /// <see cref="RoleDealPatternAffinity.All"/> حَصراً، أَو <c>null</c>
    /// لِمَن لا يَجُرّ. مَصدَرُه شُروط <c>PatternFromTenant</c>
    /// المُتَناثِرَة، وقَد ثَبَتَ بِالقِراءَة أَنَّها قائِمَة عَلى
    /// <b>أَدوار مُفرَدَة</b> لا عَلى تَركيبات مَجموعات، فَمَوضِعُها
    /// الطَبيعيّ حَقل في مِلَفّ الدَور لا جَدوَل قَواعِد.</para>
    ///
    /// <para><b>وهو نَمَط التَدَفُّق لا شَخصِيَّة الواجِهَة</b> — نَفس
    /// التَّنبيه في META-MODEL §5: <c>AppPattern</c>
    /// (<c>PatternProfileResolver</c>) مَعجَم آخَر بِقَواعِد أُخرى
    /// يَقرَأ الفِئات أَيضاً، ولا يُشتَقّ مِن هذا الحَقل.</para>
    ///
    /// <para><b>وعَدَم تَماثُل مُوَثَّق لا مُصَحَّح</b>: <c>shipper</c>
    /// بِلا انجِذاب رَغم أَنَّه دَور سائِق في كُلّ فَتحَة تَركيب
    /// (<c>driverHome</c>/<c>driverNav</c>/<c>driverExplore</c>)، لِأَنّ
    /// <c>PatternFromTenant</c> لَم يَكُن يَعُدُّه <c>trip</c>. تَصحيحُه
    /// تَغيير سُلوك، والمَوجَة شَرطُها ألّا يَتَغَيَّر السُلوك بَتّاً —
    /// فَنُقِلَ كَما هو ومَعَه اختِبار يُثَبِّتُه.</para>
    /// </summary>
    public string? DealPatternAffinity { get; init; }
}

/// <summary>
/// <para><b>اشتِقاق نَمَط الصَفقَة مِن أَدوار المُستَأجِر — مَوضِع
/// واحِد</b>. كانَ شُروطاً مُتَناثِرَة في <c>PatternFromTenant</c>
/// تَذكُر أَسماء أَدوار بِأَعيانِها؛ صارَ الانجِذاب حَقلاً في مِلَفّ كُلّ
/// دَور (<see cref="RoleDefinition.DealPatternAffinity"/>) وهذه الدالَّة
/// تَجمَعُه. إضافَة دَور جَديد يَجُرّ نَمَطاً لَم تَعُد تَمَسّ كوداً.</para>
///
/// <para>نَفس نَمَط <c>DealPatternCatalog</c>: بَيانات في مَوضِع واحِد،
/// ودالَّة نَقِيَّة فَوقَها، ومَعجَم مُغلَق يَحرُسُها.</para>
/// </summary>
public static class RoleDealPatternAffinity
{
    /// <summary>النَّمَط حينَ لا يَجُرّ أَيّ دَور — قيمَة الراحَة.
    /// مُستَأجِر بِلا أَدوار، أَو بِأَدوار كُلُّها بِلا انجِذاب، أَو
    /// بِأَدوار خارِج الكاتالوج: كُلُّها هُنا.</summary>
    public const string Fallback = "marketplace";

    /// <summary>المَعجَم المُغلَق لِقيَم الانجِذاب المَسموحَة. و
    /// <see cref="Fallback"/> <b>ليسَ مِنه</b> بِقَصد: هو ما يُعطى حينَ
    /// لا انجِذاب، فَإسنادُه إلى دَور لا يَعني شَيئاً.</summary>
    public static readonly IReadOnlyList<string> All = new[] { "trip", "rental" };

    /// <summary>تَرتيب الغَلَبَة عِندَ اجتِماع انجِذابَين في مُستَأجِر
    /// واحِد — <c>trip</c> قَبل <c>rental</c>، مَنقولاً حَرفِيّاً مِن
    /// تَرتيب الشُروط في <c>PatternFromTenant</c> (سائِق + مالِك سَكَن
    /// في مَتجَر واحِد = <c>trip</c>).</summary>
    public static readonly IReadOnlyList<string> Priority = new[] { "trip", "rental" };

    private static readonly HashSet<string> Set = new(All, StringComparer.Ordinal);
    public static bool Contains(string affinity) => Set.Contains(affinity);

    /// <summary>النَّمَط المُشتَقّ مِن مَجموعَة <c>CatalogSlug</c>.
    /// المُقارَنَة حَسّاسَة لِلحالَة كَما كانَت.</summary>
    public static string Resolve(IEnumerable<string?> catalogSlugs) =>
        Resolve(catalogSlugs, RoleCatalog.FindDefinition);

    /// <summary>
    /// <para>نَفس الاشتِقاق بِبَحث مَحقون — أُضيفَ في مَوجَة «الأَدوار
    /// وَثائِق» لِيَرى <see cref="TenantRoleSet"/> أَدوار مُستَأجِرِه
    /// المُؤَلَّفَة. <b>الجِسم واحِد لا نُسخَتان</b>: التَّحميل الزائِد
    /// القَديم يُفَوِّض إلى هذا بِبَحث الكاتالوج، فَتَرتيب الغَلَبَة
    /// وقاعِدَة السُقوط مَكتوبانِ مَرَّةً واحِدَة.</para>
    /// </summary>
    public static string Resolve(
        IEnumerable<string?> catalogSlugs, Func<string, RoleDefinition?> lookup)
    {
        var pulled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slug in catalogSlugs)
        {
            if (string.IsNullOrEmpty(slug)) continue;
            var affinity = lookup(slug)?.DealPatternAffinity;
            if (!string.IsNullOrEmpty(affinity)) pulled.Add(affinity);
        }

        foreach (var pattern in Priority)
            if (pulled.Contains(pattern)) return pattern;

        return Fallback;
    }
}

/// <summary>أَنواع الحُقول المُغلَقَة — مُطابِقَة حَرفِيّاً لِمُفرَدات
/// <c>DynFieldType</c> في <c>DynamicAttributesService</c>، لِأَنّ حُقول
/// الدَور تُصَيَّر بِنَفس المُحَرِّك. القَوالِب السَبعَة تَستَخدِم ثَلاثَة
/// مِنها اليَوم (<c>Text</c>, <c>Number</c>, <c>SingleSelect</c>)
/// والبَقِيَّة مُتاحَة ولا يَستَخدِمُها أَحَد — وذلك مَقصود: المَعجَم
/// يَتبَع المُحَرِّك لا الاستِعمال.</summary>
public static class RoleFieldTypes
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "Text", "LongText", "Number", "Boolean",
        "SingleSelect", "MultiSelect", "Date",
    };

    private static readonly HashSet<string> Set = new(All, StringComparer.Ordinal);
    public static bool Contains(string type) => Set.Contains(type);

    /// <summary>الأَنواع الَّتي تَلزَمُها قائِمَة خِيارات غَير فارِغَة.</summary>
    public static bool RequiresOptions(string type) =>
        type is "SingleSelect" or "MultiSelect";
}

/// <summary>
/// <para><b>مَعجَم مُكَوِّنات التَّركيب المُغلَق</b> — كُلّ اسم هُنا
/// يُقابِل فَرعاً أَو مِلَفّاً <b>قائِماً فِعلاً</b> في
/// <c>ACommerce.Templates.Customer.Marketplace</c>، وقَد حُصِرَ بِمَسح
/// كُلّ مَواضِع <c>CatalogSlug</c> في الشَجَرَة. لا اسم مُخترَع.</para>
/// </summary>
public static class RoleComponents
{
    // ─── الرَّئيسِيَّة: فُروع TenantHome.razor ────────────────────────
    /// <summary>الفَرع الافتِراضيّ في <c>TenantHome.razor</c> — hero + بَحث
    /// + فِئات + مُمَيَّز + أَحدَث. وهو أَيضاً قالِب المَتاجِر بِلا أَدوار.</summary>
    public const string DefaultHome = "defaultHome";
    /// <summary><c>Components/RoleHomes/RiderHome.razor</c>.</summary>
    public const string RiderHome = "riderHome";
    /// <summary><c>Components/RoleHomes/DriverHome.razor</c> — لِـ driver وshipper.</summary>
    public const string DriverHome = "driverHome";
    /// <summary><c>Components/RoleHomes/SellerHome.razor</c> — لِـ vendor وhost.</summary>
    public const string SellerHome = "sellerHome";

    // ─── إنشاء الإعلان: فُروع CreateListing.razor ────────────────────
    /// <summary>النَموذَج الكامِل ذو الخَطوَتَين (فِئَة ← تَفاصيل).</summary>
    public const string DefaultCreateForm = "defaultCreateForm";
    /// <summary><c>Components/RoleHomes/RiderCreateRequest.razor</c>.</summary>
    public const string RiderCreateRequest = "riderCreateRequest";

    // ─── التَّنَقُّل: فُروع MainLayout.BuildNav ────────────────────────
    /// <summary>٥ تَبويبات: رَئيسيّة + استِكشاف + مُفَضَّلَة + رَسائِل + حِسابي.</summary>
    public const string DefaultNav = "defaultNav";
    /// <summary>٤ تَبويبات: رَئيسيّة + طَلَباتي + رَسائِل + حِسابي.</summary>
    public const string RiderNav = "riderNav";
    /// <summary>٥ تَبويبات: رَئيسيّة + مَشاوير + عُروضي + رَسائِل + حِسابي.</summary>
    public const string DriverNav = "driverNav";
    /// <summary>٥ تَبويبات: رَئيسيّة + إعلاناتي + صَفقات + رَسائِل + حِسابي.</summary>
    public const string VendorNav = "vendorNav";
    /// <summary>٤ تَبويبات: رَئيسيّة + إدارَة + رَسائِل + حِسابي.</summary>
    public const string AdminNav = "adminNav";

    // ─── الاستِكشاف: فَرعا TenantExplore.razor ────────────────────────
    /// <summary>الاستِكشاف بِلا فَلتَرَة دَور.</summary>
    public const string DefaultExplore = "defaultExplore";
    /// <summary><c>driverMode</c> — لا يَرى إلّا الإعلانات الَّتي تَقبَل
    /// عُروضاً، مَع مِرساة السائِق ونِطاقِه.</summary>
    public const string DriverExplore = "driverExplore";

    // ─── سُطوح مُستَقِلَّة ─────────────────────────────────────────────
    /// <summary><c>Components/Pages/VendorProfile.razor</c> — الصَفحَة
    /// العامَّة لِلبائِع/المالِك. <b>المَوضِع الوَحيد في المُستودَع الَّذي
    /// يَعرِض تَقييماً</b>: مُتَوَسِّط النُّجوم وعَدّاد التَّقييمات
    /// (استِعلام <c>Review</c> بِـ <c>TargetUserId</c>).</summary>
    public const string VendorProfile = "vendorProfile";
    /// <summary><c>Components/Pages/DriverArea.razor</c> — مِرساة السائِق
    /// ونِطاقُه (<c>/me/area</c>).</summary>
    public const string DriverArea = "driverArea";
    /// <summary><c>Components/Pages/TenantDrivers.razor</c> — قائِمَة
    /// السائِقين (<c>/drivers</c>)، يَبلُغُها الراكِب مِن
    /// <c>RiderHome</c> ومِن مُختَصَر الـ PWA. <b>لا تَعرِض تَقييماً</b>.</summary>
    public const string DriversList = "driversList";
    /// <summary><c>Components/RoleHomeHero.razor</c> — <b>مَوجود في
    /// الشَجَرَة ولا يُصَيِّرُه أَحَد</b>: بَحث نَصّيّ في كُلّ
    /// <c>.razor</c> و<c>.cs</c> لا يَجِد مَرجِعاً واحِداً لَه. أُبقِيَ في
    /// المَعجَم تَوثيقاً لِلواقِع (المُكَوِّن قائِم) ولا يُسنَد إلى أَيّ
    /// دَور — لِأَنّ إسنادَه ادِّعاء تَركيب لا يَقَع.</summary>
    public const string RoleHomeHero = "roleHomeHero";

    public static readonly IReadOnlyList<string> All = new[]
    {
        DefaultHome, RiderHome, DriverHome, SellerHome,
        DefaultCreateForm, RiderCreateRequest,
        DefaultNav, RiderNav, DriverNav, VendorNav, AdminNav,
        DefaultExplore, DriverExplore,
        VendorProfile, DriverArea, DriversList, RoleHomeHero,
    };

    private static readonly HashSet<string> Set = new(All, StringComparer.Ordinal);
    public static bool Contains(string component) => Set.Contains(component);
}
