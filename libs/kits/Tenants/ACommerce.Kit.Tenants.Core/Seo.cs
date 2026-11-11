using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace ACommerce.Kit.Tenants;

/// <summary>
/// مُدخَل واحِد في <c>sitemap.xml</c>.
/// </summary>
/// <param name="Loc">الرابِط المُطلَق (يَجِب أَن يَكون absolute — قاعِدَة
/// بروتوكول sitemaps).</param>
/// <param name="LastModified">آخِر تَعديل فِعليّ لِلمُحتَوى. نَتركُه
/// <c>null</c> حينَ لا نَملِك قيمَة صادِقَة — <c>lastmod</c> كاذِب أَسوَأ
/// مِن غيابِه لِأَنَّ الزاحِف يُصَدِّقُه ويُؤَجِّل إعادَة الزَحف.</param>
/// <param name="ChangeFrequency">daily | weekly | …</param>
/// <param name="Priority">0.0 – 1.0 (نِسبيَّة داخِل المَوقِع نَفسِه).</param>
public sealed record SitemapEntry(
    string Loc,
    DateTime? LastModified = null,
    string? ChangeFrequency = null,
    double? Priority = null);

/// <summary>
/// <para>بِناء وَثائِق الـ SEO — <c>robots.txt</c>، <c>sitemap.xml</c>،
/// و JSON-LD لِلصَفحَة الرَئيسيَّة. كُلّ الدَوالّ هُنا <b>نَقِيَّة</b>:
/// تَأخُذ مُستَأجِرين وَرابِطاً أَساسيّاً وَتُعيد نَصّاً — بِلا قاعِدَة
/// بَيانات وَلا <c>HttpContext</c>، فَتُختَبَر مُباشَرَةً.</para>
///
/// <para>الطَبَقَة الَّتي تَجلِب المُستَأجِرين مِن Marten تَعيش في
/// <c>ACommerce.Kit.Tenants.Server.SeoHandlers</c> — نَفس تَقسيم
/// Core/Server في بَقيَّة العُدَد (META-MODEL §2).</para>
/// </summary>
/// <summary>
/// <para><b>أَيُّ مَتجَرٍ تَعرِضُه المَنَصَّةُ واجِهَةً لَها</b> (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·C1D-3): خَريطَةُ المَوقِعِ في هذِه العُدَّةِ تَسأَلُه إن
/// سُجِّل، والقالِبُ يُجيبُه (‏<c>TenantShowcase</c>: إعلانٌ عَلى الأَقَلّ، وباقَةٌ لا في السَماحِ ولا مُعَلَّقَة) — العُدَّةُ لا تَعرِفُ الإعلاناتِ ولا الباقات.
/// ومُستَهلِكاه في وَقتِ التَشغيلِ ثَلاثَة: الخَريطَةُ هُنا، وقائِمَةُ الصَفحَةِ الأُولى وإحصاءاتُها في القالِب (القاعِدَة ١).</para>
/// </summary>
public interface ITenantShowcase
{
    Task<IReadOnlyList<Tenant>> ShowcaseAsync(IReadOnlyList<Tenant> tenants, CancellationToken ct = default);
}

public static class SeoDocuments
{
    /// <summary>مَسارات المَنصَّة الَّتي لا يُسمَح لِلزاحِف بِها: لَوحَة
    /// الإدارَة، الاستوديو، نِقاط الـ API، وَبِنيَة Blazor التَحتيَّة.</summary>
    public static readonly string[] DisallowedPrefixes =
    {
        "/admin", "/_admin", "/studio", "/api/", "/_blazor", "/_framework", "/realtime"
    };

    /// <summary>مَسارات خاصَّة بِالمُستَخدِم داخِل كُلّ مُستَأجِر. تُكتَب
    /// بِنَمَط <c>/*/…</c> (اِمتِداد wildcard تَدعَمُه Google و Bing) لِأَنّ
    /// الـ slug مُتَغَيِّر.</summary>
    public static readonly string[] DisallowedTenantPaths =
    {
        "login", "me", "deals", "chats", "favorites", "cart", "checkout",
        "notifications", "manage", "create-listing"
    };

    // ‏Slugs مَحجوزَة لا تُمَثِّل مُستَأجِراً عامّاً —
    // <see cref="ReservedTenantSlugs.All"/>، **لا نُسخَةً هُنا**.
    //
    // **وكانَت نُسخَةً، والانجِرافُ وَقَعَ فِعلاً**: عَشرَةُ أَسماءٍ
    // هُنا مُقابِلَ ثَمانِيَةَ عَشَرَ في الوَسيط — بِلا `terms` ولا
    // `billing` ولا إخوانِهِما. فَمَتجَرٌ سلاجُه `terms` كانَ
    // **يُدرَجُ في خَريطَةِ المَوقِعِ لِلزاحِفِ وهُوَ لا يُفتَحُ
    // أَصلاً** (الوَسيطُ يَحجِزُ الاسمَ فَلا يُحَلُّ المُستَأجِر).
    // والاتِّجاهُ الوَحيدُ لِلتَغيير: أَسماءٌ **تُستَبعَدُ** مِن
    // الخَريطَة، لا أَسماءٌ تُضاف.

    // ─── الرابِط الأَساسيّ ───────────────────────────────────────────────

    /// <summary>يَحذِف الشَرطَة الأَخيرَة لِيُصبِح الوَصل بِـ
    /// <c>$"{baseUrl}/{path}"</c> آمِناً بِلا شَرطَة مُزدَوَجَة.</summary>
    public static string NormalizeBaseUrl(string? baseUrl)
        => string.IsNullOrWhiteSpace(baseUrl) ? "" : baseUrl.TrimEnd('/');

    // ─── هَل المُستَأجِر عامّ؟ ───────────────────────────────────────────

    /// <summary>مُستَأجِر يَظهَر لِلزاحِف: لَه slug صالِح، غَير مَحجوز،
    /// غَير مُعَلَّق إداريّاً، وَلا يَبدَأ بِـ <c>_</c> (اِصطِلاح المَتاجِر
    /// الداخِليَّة مِثل <c>_admin</c>).</summary>
    public static bool IsPublic(Tenant t)
        => t is not null
           && !string.IsNullOrWhiteSpace(t.Slug)
           && !t.IsSuspended
           && !t.Slug.StartsWith('_')
           && !ReservedTenantSlugs.Contains(t.Slug);

    // ─── خَريطَة المَوقِع ────────────────────────────────────────────────

    /// <summary>
    /// <para><b>خَريطَةُ مَوقِعِ مُضيفِ المَتجَر</b> (‏2026-11-07، ADR-050 §٩ · W13-4·C2a·6 — G13): عَلى <c>{slug}.R</c> المُضيفُ جَذرُ المَتجَر، فَالمُدخَلاتُ
    /// عَناوينُه النَظيفَة — <c>{base}/</c> و<c>{base}/explore</c> وفِئاتُه — <b>ومَتجَرُه وَحدَه</b>. <b>والعِلَّةُ المَقيسَة</b>: كانَت الخَريطَةُ نَفسُها
    /// تُخدَمُ عَلى كُلِّ مُضيفٍ فَتَسرُدُ كُلَّ مَتجَرٍ بِمَسارِه تَحتَ مُضيفِ غَيرِه (‏29 مِن 33 رابِطاً مَيِّتاً عَلى مُضيفِ <c>theme-demo</c>، 2026-08-30).
    /// والمَتجَرُ غَيرُ العامِّ (<see cref="IsPublic"/>) أَو الغائِبُ لا مُدخَلَ لَه.</para>
    /// </summary>
    /// <param name="tenant">مَتجَرُ المُضيف (<c>null</c> أَو غَيرُ العامِّ ← لا مُدخَل).</param>
    /// <param name="baseUrl">أَصلُ المُضيفِ الظاهِر، مَثَلاً <c>https://{slug}.R</c>.</param>
    /// <param name="includeCategories">إدراج روابِط الفِئات.</param>
    public static IReadOnlyList<SitemapEntry> StoreHostEntries(Tenant? tenant, string baseUrl, bool includeCategories = true)
    {
        if (tenant is null || !IsPublic(tenant)) return Array.Empty<SitemapEntry>();
        var b = NormalizeBaseUrl(baseUrl);
        var entries = new List<SitemapEntry>
        {
            new($"{b}/", null, "daily", 1.0),
            new($"{b}/explore", null, "daily", 0.8),
        };
        if (includeCategories)
            foreach (var c in tenant.Categories.Where(c => !string.IsNullOrWhiteSpace(c.Slug)).OrderBy(c => c.SortOrder)
                                               .DistinctBy(c => c.Slug, StringComparer.Ordinal))
                entries.Add(new($"{b}/explore?category={Uri.EscapeDataString(c.Slug)}", null, "weekly", 0.6));
        return entries;
    }

    /// <summary>
    /// يُحَوِّل قائِمَة مُستَأجِرين إلى مُدخَلات خَريطَة المَوقِع: رَئيسيَّة
    /// كُلّ مُستَأجِر، ثُمّ صَفحَة الاستِكشاف، ثُمّ فِئاتُه (الفِئات مَقروءَة
    /// مِن وَثيقَة المُستَأجِر نَفسِها — بِلا أَيّ استِعلام إضافيّ).
    /// </summary>
    /// <param name="tenants">كُلّ المُستَأجِرين؛ تُصَفَّى داخِلاً بِـ
    /// <see cref="IsPublic"/>.</param>
    /// <param name="baseUrl">مَثَلاً <c>https://wasayel.app</c>.</param>
    /// <param name="includeCategories">إدراج روابِط الفِئات.</param>
    public static IReadOnlyList<SitemapEntry> TenantEntries(
        IEnumerable<Tenant> tenants, string baseUrl, bool includeCategories = true)
    {
        var b = NormalizeBaseUrl(baseUrl);
        var entries = new List<SitemapEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string loc, double priority, string freq)
        {
            if (seen.Add(loc)) entries.Add(new SitemapEntry(loc, null, freq, priority));
        }

        foreach (var t in (tenants ?? Array.Empty<Tenant>()).Where(IsPublic))
        {
            var slug = Uri.EscapeDataString(t.Slug);
            Add($"{b}/{slug}", 1.0, "daily");
            Add($"{b}/{slug}/explore", 0.8, "daily");

            if (!includeCategories) continue;

            foreach (var c in t.Categories.Where(c => !string.IsNullOrWhiteSpace(c.Slug))
                                          .OrderBy(c => c.SortOrder))
                Add($"{b}/{slug}/explore?category={Uri.EscapeDataString(c.Slug)}", 0.6, "weekly");
        }

        return entries;
    }

    /// <summary>يُسَلسِل المُدخَلات إلى XML بِمَخطَّط sitemaps.org 0.9.</summary>
    public static string BuildSitemapXml(IEnumerable<SitemapEntry> entries)
    {
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var root = new XElement(ns + "urlset");

        foreach (var e in entries ?? Array.Empty<SitemapEntry>())
        {
            if (string.IsNullOrWhiteSpace(e.Loc)) continue;
            var url = new XElement(ns + "url", new XElement(ns + "loc", e.Loc));
            if (e.LastModified is { } lm)
                url.Add(new XElement(ns + "lastmod", lm.ToString("yyyy-MM-dd")));
            if (!string.IsNullOrEmpty(e.ChangeFrequency))
                url.Add(new XElement(ns + "changefreq", e.ChangeFrequency));
            if (e.Priority is { } p)
                url.Add(new XElement(ns + "priority",
                    p.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)));
            root.Add(url);
        }

        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Environment.NewLine + root;
    }

    // ─── robots.txt ─────────────────────────────────────────────────────

    /// <summary>يَبني <c>robots.txt</c>: مَنع لَوحات الإدارَة والاستوديو
    /// وَالـ API وَصَفَحات المُستَخدِم الخاصَّة، مَع الإشارَة إلى خَريطَة
    /// المَوقِع.</summary>
    /// <summary><b>robots.txt عَلى مُضيفِ الدَور</b> (‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 · C2b-P3-01): لا زَحفَ ولا خَريطَة — تَطبيقُ الدَورِ لَيسَ نُسخَةً تُفَهرَسُ
    /// مِن واجِهَةِ المَتجَر، ومُضيفُ المَتجَرِ <c>{slug}.R</c> النُسخَةُ الوَحيدَة.</summary>
    public const string RoleHostRobotsTxt = "User-agent: *\nDisallow: /\n";

    public static string BuildRobotsTxt(string baseUrl)
    {
        var b = NormalizeBaseUrl(baseUrl);
        var sb = new StringBuilder();
        sb.Append("User-agent: *").Append('\n');

        foreach (var p in DisallowedPrefixes)
            sb.Append("Disallow: ").Append(p).Append('\n');

        // صَفَحات داخِل المُستَأجِر — تُكتَب **بِالشَكلَين مَعاً**.
        //
        // **العِلَّةُ (‏ADR-022)**: الزاحِفُ يُطابِقُ القاعِدَةَ على
        // **المَسارِ الَّذي يَراه**، لا على المَسارِ الَّذي يَصِلُ
        // الخادِمَ بَعدَ حَقنِ السلاج. فَتَحتَ `{slug}.wasayel.tld`
        // يَرى الزاحِفُ `/login` لا `/ashare/login`، و
        // `Disallow: /*/login` **لا يُطابِقُها** — فَتُفتَحُ
        // صَفَحاتُ الدُخولِ والسَلَّةِ والدَفعِ لِلزَحفِ **بِلا أَن
        // يَحمَرَّ شَيء**. والسَطرانِ مَعاً يُغَطِّيانِ المِحوَرَين،
        // وزِيادَةُ `/{p}` غَيرُ ضارَّةٍ في وَضعِ المَسار: لا صَفحَةَ
        // عامَّةً بِهذِه الأَسماءِ على الجَذر.
        foreach (var p in DisallowedTenantPaths)
        {
            sb.Append("Disallow: /*/").Append(p).Append('\n');
            sb.Append("Disallow: /").Append(p).Append('\n');
        }

        if (!string.IsNullOrEmpty(b))
            sb.Append('\n').Append("Sitemap: ").Append(b).Append("/sitemap.xml").Append('\n');

        return sb.ToString();
    }

    // ─── نُصوص التَرويسَة ───────────────────────────────────────────────

    /// <summary>عُنوان الصَفحَة الرَئيسيَّة: اِسم المَتجَر + شِعارُه النَصّيّ.</summary>
    public static string HomeTitle(Tenant? t)
    {
        if (t is null || string.IsNullOrWhiteSpace(t.Name)) return "وَسايِل · Wasayel";
        return string.IsNullOrWhiteSpace(t.TagLine)
            ? t.Name.Trim()
            : $"{t.Name.Trim()} · {t.TagLine.Trim()}";
    }

    /// <summary>وَصف meta: الشِعار النَصّيّ + المَدينَة. لا نَخترِع نَصّاً —
    /// كُلّ جُزء مَقروء مِن وَثيقَة المُستَأجِر، وما نَقَص يُحذَف.</summary>
    public static string HomeDescription(Tenant? t)
    {
        if (t is null) return "";
        var tag = (t.TagLine ?? "").Trim();
        var city = (t.City ?? "").Trim();
        var name = (t.Name ?? "").Trim();

        var head = tag.Length > 0 ? tag : name;
        if (head.Length == 0) return "";
        return city.Length > 0 ? $"{head} — {city}" : head;
    }

    // ─── JSON-LD ────────────────────────────────────────────────────────

    /// <summary>
    /// <para><b>وَسم <c>&lt;script&gt;</c> كامِلاً — نَصّاً واحِداً</b>،
    /// لِأَنّ نَوع المُحتَوى <c>application/ld+json</c> <b>رَمز ثابِت</b> لا
    /// قيمَةَ مُستَخدِم، ويَجِب أَن يَصِل إلى المُستَهلِك حَرفاً بِحَرف.</para>
    ///
    /// <para><b>ولِماذا لا يُكتَب الوَسم في Razor مُباشَرَةً؟</b> لِأَنّ
    /// عُنصُراً فيه تَعبير ديناميكيّ يُصَيَّر عَبر شَجَرَة العَرض، فَتَمُرّ
    /// قيمَة كُلّ خاصِّيَّة عَلى <c>HtmlEncoder</c> الافتِراضيّ — وهو
    /// يَهرُب <c>+</c> إلى <c>&amp;#x2B;</c>. فَيَخرُج
    /// <c>type="application/ld&amp;#x2B;json"</c>: يَفُكُّه مُحَلِّل HTML
    /// مُطابِق لِلمُواصَفَة، ولا يَراه أَيّ مُستَهلِك يُطابِق النَصّ
    /// حَرفِيّاً — وأَكثَر زَواحِف المُشارَكَة والتَّحَقُّق كَذلك. المَخرَج
    /// هُنا <c>MarkupString</c> واحِدَة فَلا تَمُرّ بِذلك المُرَمِّز.</para>
    ///
    /// <para><b>وهي آمِنَة رَغم أَنَّها نَصّ خام</b>: الجِسم مَخرَج
    /// <see cref="BuildHomeJsonLd"/> بِالمُرَمِّز الافتِراضيّ الَّذي يَهرُب
    /// <c>&lt;</c> و <c>&gt;</c> و <c>&amp;</c> إلى <c>\uXXXX</c> — فَلا
    /// يَستَطيع اِسم مَتجَر أَن يُغلِق الوَسم. والغِلاف نَفسُه ثَوابِت.</para>
    /// </summary>
    public static string BuildHomeJsonLdScript(Tenant t, string baseUrl)
        => "<script type=\"application/ld+json\">"
           + BuildHomeJsonLd(t, baseUrl)
           + "</script>";

    /// <summary>
    /// <para>يَبني <c>@graph</c> يَحوي <c>Organization</c> + <c>WebSite</c>
    /// لِلصَفحَة الرَئيسيَّة، مُسَلسَلاً بِـ <c>System.Text.Json</c> —
    /// المُرَمِّز الافتِراضيّ يَهرُب <c>&lt;</c> و <c>&gt;</c> و <c>&amp;</c>
    /// فَلا يُمكِن لِاسم مَتجَر أَن يَكسِر وَسم <c>&lt;script&gt;</c>.</para>
    ///
    /// <para><c>addressCountry: "SA"</c> ثابِت: المَنصَّة سُعوديَّة بِكامِل
    /// طَبَقاتِها (نَفاذ، الريال، ضَريبَة ZATCA، مُزَوِّدو الدَفع) — لَيسَت
    /// بَيانات مُخترَعَة بَل خاصِّيَّة مُعلَنَة لِلمَنصَّة.</para>
    /// </summary>
    public static string BuildHomeJsonLd(Tenant t, string baseUrl)
    {
        var b = NormalizeBaseUrl(baseUrl);
        var slug = Uri.EscapeDataString(t.Slug);
        var home = $"{b}/{slug}";
        var name = string.IsNullOrWhiteSpace(t.Name) ? t.Slug : t.Name.Trim();
        var description = HomeDescription(t);

        var org = new Dictionary<string, object?>
        {
            ["@type"] = "Organization",
            ["@id"]   = $"{home}#organization",
            ["name"]  = name,
            ["url"]   = home
        };
        if (description.Length > 0) org["description"] = description;
        if (!string.IsNullOrWhiteSpace(t.City))
            org["address"] = new Dictionary<string, object?>
            {
                ["@type"] = "PostalAddress",
                ["addressLocality"] = t.City.Trim(),
                ["addressCountry"]  = "SA"
            };
        if (b.Length > 0)
        {
            var logo = $"{b}/api/{slug}/icon.svg";
            org["logo"]  = logo;
            org["image"] = logo;
        }

        var site = new Dictionary<string, object?>
        {
            ["@type"]      = "WebSite",
            ["@id"]        = $"{home}#website",
            ["name"]       = name,
            ["url"]        = home,
            ["inLanguage"] = "ar",
            ["publisher"]  = new Dictionary<string, object?> { ["@id"] = $"{home}#organization" },
            ["potentialAction"] = new Dictionary<string, object?>
            {
                ["@type"] = "SearchAction",
                ["target"] = new Dictionary<string, object?>
                {
                    ["@type"]       = "EntryPoint",
                    ["urlTemplate"] = $"{home}/explore?q={{search_term_string}}"
                },
                ["query-input"] = "required name=search_term_string"
            }
        };

        var graph = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@graph"]   = new object[] { org, site }
        };

        return JsonSerializer.Serialize(graph, JsonOpts);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        // المُرَمِّز الافتِراضيّ (لا UnsafeRelaxed) — يَهرُب < > & لِأَمان الحَقن.
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}
