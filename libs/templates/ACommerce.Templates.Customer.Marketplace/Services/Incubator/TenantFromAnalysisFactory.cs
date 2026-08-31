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
        if (string.IsNullOrEmpty(slug)) return SlugRequired;
        if (!System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9_-]+$"))
            return SlugFormat;
        if (ReservedTenantSlugs.Contains(slug)) return SlugReserved;

        await using var s = _store.QuerySession();
        var existing = await s.LoadAsync<Tenant>(slug, ct);
        return existing is null ? null : SlugTaken;
    }

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
    /// </summary>
    public async Task<Tenant> CreateAsync(
        string slug, string name, string color, string tagLine, string city,
        string pattern, string sector, string authChannel,
        Guid ownerId, Guid? sourceAnalysisId,
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

        await using var ws = _store.LightweightSession();
        ws.Store(tenant);
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
            "ecommerce" => new[] {
                C("products",   "مُنتَجات",  "🛍️", "commercial", 0),
                C("deals",      "عُروض",     "🏷️", "commercial", 1)
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
