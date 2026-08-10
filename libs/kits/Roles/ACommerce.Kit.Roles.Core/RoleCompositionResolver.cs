namespace ACommerce.Kit.Roles;

/// <summary>
/// <para><b>قَرار تَركيب الواجِهَة لِدَور — دالَّة نَقِيَّة واحِدَة</b>.
/// كانَ هذا القَرار مَنثوراً في سِتَّة مَواضِع تَصيير، كُلّ واحِد مِنها
/// يَتَفَرَّع بِنَفسِه عَلى <c>CatalogSlug</c>؛ فَاستُخرِجَ إلى هُنا
/// <b>بِلا تَغيير سُلوك</b>، فَصارَ لِلقَرار مَوضِع واحِد يُختَبَر
/// ويُثَبَّت.</para>
///
/// <para><b>ولِماذا الاستِخراج قَبل التَّبديل</b>: هذه الدالَّة هي
/// <b>نُقطَة القَلب الوَحيدَة</b> — مَواضِع التَّصيير تَسأَلُها ولا
/// تَعرِف مِن أَينَ تُجيب. حينَ يَصير مَصدَر الجَواب مِلَفّات التَّعريف
/// بَدَل الـ <c>switch</c>، يَتَغَيَّر جِسم <see cref="Resolve"/> وَحدَه
/// ولا يَتَحَرَّك سَطر في أَيّ مَوضِع تَصيير — واختِبار التَّوصيف الَّذي
/// اخضَرَّ عَلى الـ <c>switch</c> يَبقى حاكِماً بِلا تَعديل.</para>
///
/// <para><b>الحالات الحَدِّيَّة مَحفوظَة حَرفِيّاً</b> كَما كانَت في
/// الفُروع: <c>null</c> وسِلسِلَة فارِغَة و slug غَير مَعروف — كُلُّها
/// تُعطي التَّركيب الافتِراضيّ، لِأَنّ كُلّ <c>switch</c> مِن السِتَّة
/// كانَ يَنتَهي بِفَرع <c>default</c>. هذا لَيسَ اختِياراً جَديداً بَل
/// التِقاط لِما يَقَع.</para>
/// </summary>
public static class RoleCompositionResolver
{
    /// <summary>التَّركيب الافتِراضيّ الآمِن — كُلّ فَتحَة عَلى قيمَتِها
    /// الافتِراضيّة، بِلا صَفحَة عامَّة وبِلا سُطوح إضافيَّة. وهو
    /// <b>نَفس</b> ما يُعطيه فَرع <c>default</c> في المَواضِع السِتَّة.</summary>
    public static readonly RoleComposition Fallback = new();

    /// <summary>تَركيب الواجِهَة لِـ <paramref name="catalogSlug"/>.
    /// المَجهول والفارِغ و<c>null</c> ← <see cref="Fallback"/>.</summary>
    public static RoleComposition Resolve(string? catalogSlug) => new()
    {
        // مِن فُروع TenantHome.razor.
        Home = catalogSlug switch
        {
            "rider"               => RoleComponents.RiderHome,
            "driver" or "shipper" => RoleComponents.DriverHome,
            "vendor" or "host"    => RoleComponents.SellerHome,
            _                     => RoleComponents.DefaultHome,
        },

        // مِن فَرعَي CreateListing.razor.
        CreateListing = catalogSlug switch
        {
            "rider" => RoleComponents.RiderCreateRequest,
            _       => RoleComponents.DefaultCreateForm,
        },

        // مِن switch في MainLayout.BuildNav.
        Nav = catalogSlug switch
        {
            "rider"               => RoleComponents.RiderNav,
            "driver" or "shipper" => RoleComponents.DriverNav,
            "vendor" or "host"    => RoleComponents.VendorNav,
            "tenant_admin"        => RoleComponents.AdminNav,
            _                     => RoleComponents.DefaultNav,
        },

        // مِن driverMode في TenantExplore.razor.
        Explore = catalogSlug switch
        {
            "driver" or "shipper" => RoleComponents.DriverExplore,
            _                     => RoleComponents.DefaultExplore,
        },

        // الصَفحَة العامَّة لِحامِل الدَور. <b>لا فَرع تَصيير يَقرَؤُها</b>
        // اليَوم — <c>VendorProfile.razor</c> صَفحَة مَفتوحَة بِمُعَرِّف
        // المُستَخدِم لا بِدَورِه، ولا بَوّابَة دَور فيها. القيمَة هُنا
        // تَوثيق لِمَن لَه صَفحَة عامَّة، لا حِراسَة لِمَن يَبلُغُها.
        PublicProfile = catalogSlug switch
        {
            "vendor" or "host" => RoleComponents.VendorProfile,
            _                  => null,
        },

        // سُطوح إضافيَّة — تُقرَأ في مُختَصَرات الـ PWA (BuildShortcuts).
        Extras = catalogSlug switch
        {
            "rider"               => new[] { RoleComponents.DriversList },
            "driver" or "shipper" => new[] { RoleComponents.DriverArea },
            _                     => [],
        },
    };
}

/// <summary>
/// <para><b>تَحويل قيمَة فَتحَة عَبر قاموس مُغلَق</b> — المَدخَل الوَحيد
/// الَّذي تَستَخدِمُه مَواضِع التَّصيير لِتَرجَمَة قيمَة مُعجَمِيَّة
/// (<see cref="RoleComponents"/>) إلى ما تُصَيِّرُه فِعلاً: مَندوب، أَو
/// عَلَم، أَو قائِمَة. <b>لا انعِكاس</b> — لا تَحويل اسم إلى نَوع، ولا
/// بَحث عَن مُكَوِّن بِاسمِه.</para>
///
/// <para><b>ولِماذا يَبقى السُقوط الآمِن رَغم أَنّ المُصادِق يَمنَع
/// المَجهول</b>: المُصادِق يَحرُس <b>المَعجَم</b> (قيمَة خارِج
/// <see cref="RoleComponents.All"/> تُفشِل الإقلاع)، وهذا يَحرُس
/// <b>القاموس</b> — أَن تُضاف قيمَة إلى المَعجَم ويُنسى تَسجيلُها في
/// مَوضِع تَصيير. الحارِسانِ يُغَطّيانِ خَطَرَين مُختَلِفَين، والثاني هو
/// الَّذي تَفتَحُه هذه المَوجَة بِالضَبط.</para>
/// </summary>
public static class RoleComponentMap
{
    /// <summary>يُرجِع قيمَة <paramref name="component"/> مِن
    /// <paramref name="table"/>، أَو <paramref name="fallback"/> مَع سَطر
    /// تَحذير إن لَم تَكُن مُسَجَّلَة.</summary>
    public static T Map<T>(
        IReadOnlyDictionary<string, T> table,
        string component,
        T fallback,
        string slotAr)
    {
        if (table.TryGetValue(component, out var value)) return value;

        // مَسار دِفاعيّ لا يَقَع في التَّشغيل السَليم — ولِذلك يُكتَب إلى
        // الخَطَأ القِياسيّ بِلا حاجَة إلى حَقن مُسَجِّل في عُدَّة نَقِيَّة.
        Console.Error.WriteLine(
            $"[roles] المُكَوِّن «{component}» في فَتحَة {slotAr} غَير مُسَجَّل " +
            "في قاموس التَّصيير — سُقوط إلى الافتِراضيّ.");
        return fallback;
    }
}
