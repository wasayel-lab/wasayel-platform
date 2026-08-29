using System.Text.Json;
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

    /// <summary>اِقتِراحات مَبدَئيَّة مِن تَحليل لِيَملَأ نَموذَج الإنشاء.</summary>
    public sealed record BuildSuggestion(
        string Name, string Color, string TagLine, string City, string Pattern);

    public BuildSuggestion DeriveSuggestion(IncubatorSession session)
    {
        var pattern = string.IsNullOrEmpty(session.SuggestedPattern)
            ? "marketplace" : session.SuggestedPattern;

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

    /// <summary>يُنشِئ Tenant فِعليّ + يَربِطه بِالـ analysis والمالِك.
    /// يَفتَرِض أَنّ الـ slug صالِح وغَير مُستَخدَم (نادِيِ ValidateSlugAsync أَوَّلاً).</summary>
    public async Task<Tenant> CreateAsync(
        string slug, string name, string color, string tagLine, string city,
        string pattern, string sector, Guid ownerId, Guid? sourceAnalysisId,
        CancellationToken ct = default)
    {
        var categories = CategoriesForSector(sector, pattern).ToList();
        var roles = RolesForPattern(pattern).ToList();

        var tenant = new Tenant
        {
            Id = slug, Name = name, BrandColor = color, TagLine = tagLine, City = city,
            AuthChannel = "phone",
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

    private static IReadOnlyList<Role> RolesForPattern(string pattern)
    {
        Role MakeRole(string slug, int order, bool isDefault = false)
        {
            var tmpl = RoleCatalog.Find(slug);
            if (tmpl is null) return new Role { Slug = slug, Label = slug, SortOrder = order, IsDefault = isDefault };
            var r = RoleCatalog.InstantiateRole(tmpl, order);
            r.IsDefault = isDefault;
            return r;
        }

        return pattern switch
        {
            "ondemand" => new[] {
                MakeRole("rider", 0, isDefault: true),
                MakeRole("driver", 1),
                MakeRole("tenant_admin", 2)
            },
            "marketplace" or "classifieds" => new[] {
                MakeRole("customer", 0, isDefault: true),
                MakeRole("vendor", 1),
                MakeRole("tenant_admin", 2)
            },
            "rental" => new[] {
                MakeRole("customer", 0, isDefault: true),
                MakeRole("host", 1),
                MakeRole("tenant_admin", 2)
            },
            _ => Array.Empty<Role>()
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
