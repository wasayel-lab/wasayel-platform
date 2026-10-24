using ACommerce.Kit.Tenants;
using ACommerce.Platform.I18n;

namespace ACommerce.Templates.Customer.Marketplace.Services.Patterns;

/// <summary>
/// نَمَط التَّطبيق — يَحسِم لِكُلّ مُستَأجِر شَخصِيَّةَ تَجرِبَتِه. مَرَّ نَمَط
/// الـ Deal pipeline (الـ DealsPolicy) قَبلَه؛ هذا يُكمِلُه عَلى مُستَوى الـ UI:
/// تَركيب الرَئيسيَّة، شَكل البِطاقَة، السِمات المَعروضَة، الـ CTA الرَئيسيّ،
/// والـ nav. نَشتَقُّ النَّمَط مِن أَدوار/فِئات المُستَأجِر — لا اختِيار يَدَويّ.
/// </summary>
public enum AppPattern
{
    /// <summary>سَكَن مُشتَرَك — يَملِك سَكَناً أَو يَدور شَريك. لا سَلَّة. لا شَحن.
    /// CTA = «تَواصَل». السِمات: شَخصِيَّة الشَريك، نَوع السَكَن، شَهرِيّ/جامِعيّ.</summary>
    Roommate,
    /// <summary>إيجار عَقاريّ — شَقَّة/فيلا/مَكتَب. لا سَلَّة. CTA = «اطلُب مُعايَنَة».
    /// السِمات: غُرَف، حَمّامات، أَثاث، عَقد، تَأمين، خَدَمات.</summary>
    Rental,
    /// <summary>مَتجَر/مَقهى — مُنتَجات قابِلَة لِلسَلَّة. CTA = «أَضِف لِلسَلَّة».
    /// السِمات: مَكَوِّنات، حَجم، خِيارات.</summary>
    Marketplace,
    /// <summary>مَشاوير — راكِب يَنشُر، سائِق يَقبَل. لا سَلَّة. CTA = «قَدِّم عَرضاً».</summary>
    Trip,
    /// <summary>خَدَمات — قَهوَة مَنزِليَّة، تَنظيف، صِيانَة. لا سَلَّة. CTA = «احجِز».</summary>
    Service
}

/// <summary>
/// شَخصِيَّة كامِلَة لِلتَّطبيق — تُحَدِّد كُلّ القَرارات التَّصميمِيَّة
/// الَّتي تَختَلِف بَين تَطبيق وآخَر. لَو نَمَطان يَتَشارَكان نَفس الشَّخصِيَّة،
/// لا حاجَة لِكود مُكَرَّر.
/// </summary>
public sealed record PatternProfile(
    AppPattern Pattern,
    // ─── السَلَّة وَالـ CTA ─────────────────────────────────────────────
    /// <summary>هَل النَّمَط يَدعَم سَلَّة شِراء؟ سَكَن/إيجار/مَشاوير = false.</summary>
    bool UsesCart,
    /// <summary>نَصّ زِرّ الـ CTA الرَئيسيّ في صَفحَة التَّفاصيل.</summary>
    string PrimaryCtaLabel,
    string PrimaryCtaIcon,
    // ─── الرَئيسيَّة ───────────────────────────────────────────────────
    /// <summary>عَنوان الـ hero.</summary>
    string HomeHeroTitlePattern,
    /// <summary>هَل نَعرِض كاروسيل «المُمَيَّز»؟</summary>
    bool ShowFeaturedCarousel,
    /// <summary>هَل نَعرِض كاروسيل «الجَديد»؟</summary>
    bool ShowLatestCarousel,
    /// <summary>هَل نَعرِض شَجَرَة الفِئات في الرَئيسيَّة؟</summary>
    bool ShowCategoryTree,
    // ─── السِمات (تَظهَر تَحتَ السِعر في البِطاقَة + شَبَكَة التَفاصيل) ─
    /// <summary>سِمات تَظهَر في البِطاقَة (≤ ٣) — مَفاتيح <c>Attributes</c>.</summary>
    string[] CardChipKeys,
    /// <summary>سِمات تَظهَر في شَبَكَة تَفاصيل الإعلان (مُرَتَّبَة).</summary>
    string[] DetailGridKeys,
    // ─── تَحذيرات أَو شارات خاصَّة بِالنَّمَط ─────────────────────────
    /// <summary>تَحذير احتِيال يَظهَر في صَفحَة التَّفاصيل (سَكَن/إيجار).</summary>
    string? FraudWarning,
    /// <summary>هَل نَعرِض خَريطَة في صَفحَة التَّفاصيل لَو يَملِك الإعلان lat/lng؟</summary>
    bool ShowDetailMap
);

public static class PatternProfileResolver
{
    /// <summary>اِشتِقاق النَّمَط مِن أَدوار/فِئات المُستَأجِر. لا حاجَة لِحَقل
    /// مُنفَصِل في الـ Tenant — البِنيَة الَّتي أَدخَلها صاحِب المَتجَر تَدُلّ
    /// عَلى نَمَطِه.</summary>
    public static AppPattern PatternOf(Tenant? t)
    {
        if (t is null) return AppPattern.Marketplace;
        var kinds = t.Categories.Select(c => c.Kind).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roles = t.Roles.Select(r => r.CatalogSlug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (kinds.Contains("roommate")) return AppPattern.Roommate;
        if (roles.Contains("rider") || roles.Contains("driver") || roles.Contains("shipper")) return AppPattern.Trip;
        if (kinds.Contains("residential") || kinds.Contains("commercial") || roles.Contains("host"))
            return AppPattern.Rental;
        if (kinds.Contains("menu")) return AppPattern.Marketplace;
        return AppPattern.Marketplace;
    }

    /// <summary><para><b>نُصوصُ الشَخصِيَّةِ مِن القامُوس</b> — مَفاتيحُ <c>pattern.{نَمَط}.{cta|hero|fraud_warning}</c> في
    /// <c>ar.json</c> (القاعِدَة ١١؛ رُحِّلَت ‏2026-10-13 بِحَرفِها، وبُرهانُها <c>PatternProfileTextsCharacterizationTests</c> أَخضَرُ
    /// بِلا تَعديلِ حَرفٍ ولَقطَتا البَطَلِ بايتاً).</para>
    /// <para><b>وبِالعَرَبِيَّةِ صَراحَةً</b>: الشَخصِيَّةُ تُبنى بِلا طَلَب — <see cref="Profile"/> دالَّةٌ نَقِيَّةٌ يَقرَؤُها مُستَهلِكاها
    /// (‏<c>TenantHome.razor</c> و<c>TenantListingDetail.razor</c>) وفُحوصُها — والنَصُّ اليَومَ عَرَبِيٌّ لِكُلِّ زائِر، فَالمُخرَجُ
    /// كَما كانَ بايتاً. ويَومَ تُشحَنُ لُغَةٌ ثانِيَةٌ يُمَرَّرُ إلى هُنا رَمزُ لُغَةِ الطَلَبِ مِن المُستَهلِكَين — لا قَبلَه (القاعِدَة ١).</para></summary>
    private static string Text(string key) => LocaleCatalog.Text(LocaleCatalog.Arabic, key);

    public static PatternProfile Profile(AppPattern p) => p switch
    {
        AppPattern.Roommate => new(p,
            UsesCart: false,
            PrimaryCtaLabel: Text("pattern.roommate.cta"),
            PrimaryCtaIcon:  "message",
            HomeHeroTitlePattern: Text("pattern.roommate.hero"),
            ShowFeaturedCarousel: true,
            ShowLatestCarousel:   true,
            ShowCategoryTree:     true,
            CardChipKeys:         new[] { "gender_pref", "occupation_pref", "rent_split" },
            DetailGridKeys:       new[] { "gender_pref", "occupation_pref", "smoking", "rent_split", "deposit", "available_from", "min_stay" },
            FraudWarning:         Text("pattern.roommate.fraud_warning"),
            ShowDetailMap:        true),

        AppPattern.Rental => new(p,
            UsesCart: false,
            PrimaryCtaLabel: Text("pattern.rental.cta"),
            PrimaryCtaIcon:  "calendar",
            HomeHeroTitlePattern: Text("pattern.rental.hero"),
            ShowFeaturedCarousel: true,
            ShowLatestCarousel:   true,
            ShowCategoryTree:     true,
            CardChipKeys:         new[] { "bedrooms", "area_m2", "furnished" },
            DetailGridKeys:       new[] { "bedrooms", "bathrooms", "area_m2", "furnished", "lease_term", "deposit", "payment_schedule", "utilities" },
            FraudWarning:         Text("pattern.rental.fraud_warning"),
            ShowDetailMap:        true),

        AppPattern.Marketplace => new(p,
            UsesCart: true,
            PrimaryCtaLabel: Text("pattern.marketplace.cta"),
            PrimaryCtaIcon:  "package",
            HomeHeroTitlePattern: Text("pattern.marketplace.hero"),
            ShowFeaturedCarousel: true,
            ShowLatestCarousel:   true,
            ShowCategoryTree:     true,
            CardChipKeys:         new[] { "size", "ingredients_count" },
            DetailGridKeys:       new[] { "size", "ingredients", "calories", "prep_time" },
            FraudWarning:         null,
            ShowDetailMap:        false),

        AppPattern.Trip => new(p,
            UsesCart: false,
            PrimaryCtaLabel: Text("pattern.trip.cta"),
            PrimaryCtaIcon:  "send",
            HomeHeroTitlePattern: Text("pattern.trip.hero"),
            ShowFeaturedCarousel: false,
            ShowLatestCarousel:   true,
            ShowCategoryTree:     false,
            CardChipKeys:         new[] { "from", "to", "when" },
            DetailGridKeys:       new[] { "from", "to", "when", "weight", "notes" },
            FraudWarning:         null,
            ShowDetailMap:        true),

        AppPattern.Service => new(p,
            UsesCart: false,
            PrimaryCtaLabel: Text("pattern.service.cta"),
            PrimaryCtaIcon:  "calendar",
            HomeHeroTitlePattern: Text("pattern.service.hero"),
            ShowFeaturedCarousel: true,
            ShowLatestCarousel:   true,
            ShowCategoryTree:     true,
            CardChipKeys:         new[] { "duration", "location_type" },
            DetailGridKeys:       new[] { "duration", "location_type", "team_size", "warranty" },
            FraudWarning:         null,
            ShowDetailMap:        false),

        _ => Profile(AppPattern.Marketplace)
    };

    /// <summary>اختِصار: مُستَأجِر → شَخصِيَّتُه.</summary>
    public static PatternProfile Of(Tenant? t) => Profile(PatternOf(t));
}

/// <summary>
/// <para><b>بابُ السَلَّة — دالَّةٌ نَقِيَّةٌ تَسأَلُها صَفحَةُ الإعلانِ ونُقطَةُ <c>cart/add</c> مَعاً</b> (‏2026-10-24، تَيّارُ S15 البَند L؛ عَلى سابِقَةِ
/// <c>StudioAnalysisDoor</c> و<c>CheckoutPaymentPolicy.CardOffered</c>).</para>
///
/// <para><b>العِلَّةُ المَقيسَة</b> (تَوصيفُها <c>CartDoorTests</c>): الصَفحَةُ لا تَرسُمُ استِمارَةَ <c>cart/add</c> إلّا لِنَمَطٍ يَستَعمِلُ السَلَّة
/// (<see cref="PatternProfile.UsesCart"/>)، والنُقطَةُ لا تَسأَلُ — فَطَلَبٌ مَصنوعٌ عَلى مَتجَرِ إيجارٍ أَو سَكَنٍ أَو مَشاويرَ يَكتُبُ سَلَّةً لا تَعرِضُها
/// الواجِهَة، ثُمَّ <c>checkout/submit</c> يُحَوِّلُها صَفَقات. فَصارَ البابُ <b>واحِداً</b>: ما يُرسَمُ هُوَ ما يُقبَل (القاعِدَة ١٢).</para>
///
/// <para><b>ولا يَقرَأُ حالَةً مُخَزَّنَة</b> (القاعِدَة ٧): مُدخَلُه شَخصِيَّةُ النَمَطِ المَحسوبَةُ مِن المُستَأجِر — والقِراءَةُ عِندَ المُنادي.</para>
/// </summary>
public static class CartDoor
{
    /// <summary>رَمزُ الرَدّ — يَرتَدُّ بِه <c>cart/add</c> إلى صَفحَةِ الإعلانِ (<c>?err=cart_closed</c>)، ونَصُّه
    /// <c>listings.detail.err_cart_closed</c> في القامُوس.</summary>
    public const string ClosedCode = "cart_closed";

    /// <summary>أَيُفتَحُ بابُ السَلَّة؟ — نَعَم لِنَمَطٍ يَستَعمِلُها، ولا لِغَيرِه ولا لِشَخصِيَّةٍ لَم تُحسَب.</summary>
    public static bool Opens(PatternProfile? profile) => profile?.UsesCart == true;
}
