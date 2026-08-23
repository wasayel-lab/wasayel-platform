namespace ACommerce.Kit.Tenants;

/// <summary>
/// سِجِلّ المُستَأجِرين — وَثيقَة Marten عَلى مُستَوى المنصّة (غير
/// مَحصورَة بـ tenancy). تُمَثِّل المُسَمَّيات الإداريّة لكلّ tenant
/// (slug، اسم، لَون، فِئات). كلّ بَيانات المُستَأجِر الأَخرى (الإعلانات،
/// الرَسائِل…) تَعيش في streams مَحصورَة بـ slug عَبر Marten conjoined
/// tenancy.
/// </summary>
public sealed class Tenant
{
    /// <summary>Marten primary key. نَستَخدِم الـ slug نَفسه لِيَكون
    /// التَوصُّل مُباشَراً عَبر URL slug بدون فَهرَسَة ثانيَة.</summary>
    public string Id { get; set; } = "";

    public string Slug => Id;
    public string Name { get; set; } = "";
    public string BrandColor { get; set; } = "#7C3AED";
    public string City { get; set; } = "";
    public string TagLine { get; set; } = "";
    /// <summary>"phone" أو "nafath" أو "email" — يَختار التَطبيق طَريقَة
    /// الدُخول المُتاحَة لِهذا المُستَأجِر. صَفحَة Login تَقرَأ هذه القيمَة
    /// وتَعرِض واجهَة واحِدَة (لا tabs).</summary>
    public string AuthChannel { get; set; } = "phone";

    /// <summary>
    /// <para><b>هَل لِهذا المَتجَر مُزَوِّدُ دَفعٍ خاصٌّ بِه مَضبوط؟</b>
    /// وما دامَ <c>false</c> — وهو حالُ كُلّ مَتجَرٍ اليَوم — فَالباقاتُ
    /// <b>بِسِعرٍ لا تُعرَض ولا تُقبَل</b> في <c>/{slug}/plans</c>،
    /// والمَجّانِيَّةُ تَبقى ذاتِيَّةً كَما كانَت.</para>
    ///
    /// <para><b>القَرارُ الَّذي كَتَبَ هذا الحَقل (‏2026-08-23)، حَرفيّاً
    /// مِن المالِك</b>: «لا تَسمَح لِلتاجِر بِاستِلام حَوالات» و«إمّا
    /// بَيعٌ بِلا رُسوم أَو تَكامُلُ بَوّابَةِ دَفعٍ خاصَّةٍ بِه
    /// لاحِقاً». فَما سَبَقَ — حَقلُ «تَعليمات التَحويل البَنكيّ» ودَورَةُ
    /// طَلَبِ الاشتِراك المُعَلَّق — كانَ يَجعَل التاجِرَ يَقبِض
    /// بِحَوالاتٍ إلى حِسابِه؛ وهو ما لا يُراد. حُذِفَ الحَقلُ ومَعَه
    /// الدَورَة، وحَلَّ مَحَلَّهُما هذا السَطر.</para>
    ///
    /// <para><b>ولا كاتِبَ لَه اليَوم، وذلك مَقولٌ لا مَبتولَع</b>: لا
    /// بَوّابَةَ دَفعٍ مُدمَجَةً في المُستَودَع، فَالقيمَةُ الصادِقَةُ
    /// الوَحيدَةُ <c>false</c>. ويَومَ يُدمَج مُزَوِّدٌ فِعليٌّ يَكتُبُه
    /// ذلك التَكامُلُ نَفسُه مَعَ مَسار الشِراء — <b>ولا يُفتَح زِرُّ
    /// إدارَةٍ يَقلِبُه قَبلَ أَن يوجَدَ ما يَقبِض</b>، وإلّا عادَت
    /// الباقَةُ المَدفوعَةُ تُمنَح مَجّاناً بِنَقرَة.</para>
    /// </summary>
    public bool PaymentProviderConfigured { get; set; }

    public List<Category> Categories { get; set; } = new();

    /// <summary>الأَدوار المُتاحَة في هذا المَتجَر (سائِق/راكِب، مالِك/باحِث…).
    /// فارِغ = نَمَط user-فَرد بِلا تَمييز أَدوار. تَفاصيلها في
    /// <c>ACommerce.Kit.Roles.Role</c> وَالـ csproj يَعتَمِد عَلَيه.</summary>
    public List<ACommerce.Kit.Roles.Role> Roles { get; set; } = new();

    /// <summary>رائِد الأَعمال المالِك لِهذا التَّطبيق على المَنصَّة
    /// (<c>StudioUser.Id</c>). <c>Guid.Empty</c> = بِلا مالِك مُعَيَّن
    /// (مَتاجِر قَبل ميزَة المِلكِيَّة، يُرَبَط لاحِقاً بِأَوَّل مُستَخدِم
    /// عَبر <c>StudioOwnershipSeeder</c>).</summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>جَلسَة التَّحليل الَّتي أَنشَأت هذا المَتجَر (إن وُجِدَت).
    /// تُمَكِّن إظهار «هذا المَتجَر مَبني على فِكرَة س» في console.</summary>
    public Guid? SourceAnalysisId { get; set; }

    /// <summary>مَتجَر مُعَلَّق إداريّاً مِن مَنصَّة (مَخالَفَة، تَأخُّر
    /// دَفع، …). يَختَفي مِن الواجِهَة العامَّة، لكِنّ بَياناتُه تَبقى.</summary>
    public bool IsSuspended { get; set; }
    public string? SuspensionReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Category
{
    public string Slug { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Icon { get; set; }

    /// <summary>تَجميع رَئيسي (residential / commercial / events / vehicles / leisure / …).
    /// يُستَخدَم لِعَرض الفِئات على شَكل شَجَرَة مَجموعَة بِالـ Kind في
    /// المُستَأجِرين الَّذين يَحتَوون فِئات كَثيرَة (مَثَل إيجار). فارِغ
    /// يَعني فِئَة رَئيسيَّة بِلا تَجميع.</summary>
    public string Kind { get; set; } = "";

    /// <summary>slug الفِئَة الأَب — لِشَجَرَة حَقيقيَّة (parent → leaves).
    /// null = جَذر.</summary>
    public string? ParentSlug { get; set; }

    public int SortOrder { get; set; }

    public List<AttributeField> Attributes { get; set; } = new();
}

/// <summary>سِمَة ديناميكيّة في قَالِب الفِئَة (مثلاً غُرَف نَوم، مَساحَة).</summary>
public sealed class AttributeField
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>"text" | "number" | "bool" | "select"</summary>
    public string Type { get; set; } = "text";
    public List<string> Options { get; set; } = new();
}

// ─── Commands ─────────────────────────────────────────────────────────
public sealed record CreateTenant(
    string Slug, string Name, string BrandColor, string City, string TagLine);

public sealed record AddCategory(
    string TenantSlug, string CategorySlug, string Label, string? Icon,
    List<AttributeField>? Attributes);
