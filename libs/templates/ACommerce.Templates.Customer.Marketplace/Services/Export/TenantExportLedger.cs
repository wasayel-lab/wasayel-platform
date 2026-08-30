using ACommerce.Kit.Auth;
using ACommerce.Kit.Cart;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Favorites;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Offers;
using ACommerce.Kit.Reports;
using ACommerce.Kit.Reviews;
using ACommerce.Kit.Roles;
using ACommerce.Kit.SavedSearches;
using ACommerce.Kit.Subscriptions;
using ACommerce.Kit.Support;
using ACommerce.Kit.Tenants;
using ACommerce.Kit.Theme;
using ACommerce.Platform.Providers;
using ACommerce.Platform.Shared;
using ACommerce.Templates.Customer.Marketplace.Services.Audit;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;

namespace ACommerce.Templates.Customer.Marketplace.Services.Export;

/// <summary>ماذا يَفعَل التَخارُجُ بِهذا النَوع.</summary>
public enum ExportDisposition
{
    /// <summary>بَياناتُ المُستَأجِر — تَخرُجُ كَجَدوَلٍ في <c>data/</c>.</summary>
    Export,

    /// <summary>وَثيقَةُ المُستَأجِرِ نَفسِها — تَخرُجُ بِصَفِّها هُوَ
    /// وَحدَه، ومُعَرِّفُها هُوَ السلاج.</summary>
    ExportSelf,

    /// <summary>بَياناتُ صاحِبِ المَتجَر (لا بَياناتُ المَتجَر) —
    /// تَخرُجُ في <c>owner/</c> مُرَشَّحَةً بِمُعَرِّفِه.</summary>
    ExportOwner,

    /// <summary>وَثيقَةٌ عامَّة — جَدوَلُها بِلا <c>tenant_id</c>،
    /// فَصُفوفُها صُفوفُ كُلِّ المُستَأجِرين.</summary>
    ExcludeGlobal,

    /// <summary>اعتِمادٌ أَو تَجزئَتُه.</summary>
    ExcludeSecret,

    /// <summary>آلِيَّةٌ داخِلِيَّةٌ لا بَياناتِ عَميلٍ فيها.</summary>
    ExcludeInternal,
}

/// <summary>إدخالَةُ سِجِلٍّ واحِدَة — نَوعٌ، ومَصيرُه، وسَبَبُه.</summary>
/// <param name="ClrType">صِنفُ الوَثيقَة — <b>النَوعُ لا اسمُه</b>،
/// فَإعادَةُ تَسمِيَةٍ تَكسِرُ البِناءَ ولا تُسقِطُ الإدخالَةَ
/// صامِتَةً.</param>
/// <param name="Entry">اسمُ المِلَفِّ داخِلَ الحَقيبَة — ‏ASCII،
/// لِأَنّ اسمَ المَدخَلِ العَرَبيَّ في zip يُشَوَّهُ عِندَ أَدَواتٍ
/// لا تَقرَأُ رايَةَ UTF-8.</param>
public sealed record ExportedType(
    Type ClrType, string Entry, ExportDisposition Disposition, string WhyAr)
{
    public string TypeName => ClrType.Name;

    /// <summary>مَسارُ الـJSON داخِلَ الأَرشيف.</summary>
    public string JsonPath => Disposition == ExportDisposition.ExportOwner
        ? $"owner/{Entry}.json" : $"data/{Entry}.json";

    /// <summary>مَسارُ الـCSV داخِلَ الأَرشيف.</summary>
    public string CsvPath => Disposition == ExportDisposition.ExportOwner
        ? $"owner/{Entry}.csv" : $"tables/{Entry}.csv";
}

/// <summary>
/// <para><b>سِجِلُّ التَخارُج — قائِمَةٌ بَيضاءُ لا سَوداء.</b> يَخرُجُ
/// ما وَرَدَ اسمُه هُنا بِـ<see cref="ExportDisposition.Export"/>، ولا
/// يَخرُجُ ما سِواه. فَنَوعٌ يُسَجَّلُ غَداً في مَوجَةٍ أُخرى
/// <b>لا يَخرُجُ تِلقائِيّاً</b> — والفَشَلُ مُغلَق.</para>
///
/// <para><b>ولا يَكفي أَن يَكونَ مُغلَقاً</b>: القائِمَةُ البَيضاءُ
/// تَمنَعُ التَسريبَ ولا تَكشِفُ النَقص. فَالسِجِلُّ <b>يُصَنِّفُ كُلَّ
/// نَوع</b> — يَخرُج أَو يُستَثنى بِسَبَبٍ مَكتوب — و
/// <c>TenantExportTests.Every_marten_document_type_in_the_repo_is_classified_in_the_export_ledger</c>
/// يَحمَرُّ عِندَ نَوعٍ لَم يُصَنَّف. <b>وتَخارُجٌ مَنقوصٌ أَسوَأُ مِن
/// لا تَخارُج، لِأَنَّه يُطَمئِنُ كَذِباً.</b></para>
///
/// <para><b>والحَدُّ الحاكِمُ عَمودٌ لا حَقلٌ في JSON</b>:
/// <c>AllDocumentsAreMultiTenanted()</c> تَضَعُ <c>tenant_id</c> في
/// كُلِّ صَفّ إلّا ما سُجِّلَ <c>SingleTenanted()</c> صَراحَةً —
/// وتِلكَ جَداوِلُ <b>بِلا عَمودِ عَزلٍ أَصلاً</b>، فَتَرُدُّ صُفوفَ
/// كُلِّ المُستَأجِرينَ لِأَيِّ جَلسَة. ولِذلك كُلُّها هُنا
/// <c>Exclude*</c> — إلّا وَثيقَةَ المُستَأجِرِ نَفسِه، وهي تُحَمَّلُ
/// بِمُعَرِّفِها الَّذي هُوَ السلاج.</para>
/// </summary>
public static class TenantExportLedger
{
    public static IReadOnlyList<ExportedType> All { get; } = new ExportedType[]
    {
        // ═══ وَثيقَةُ المَتجَرِ نَفسِه ══════════════════════════════
        new(typeof(Tenant), "store", ExportDisposition.ExportSelf,
            "تَعريفُ المَتجَر — اسمُه وفِئاتُه وأَدوارُه وقَناةُ دُخولِه. " +
            "أَثمَنُ ما يَخرُجُ بِه العَميل. وجَدوَلُها عامٌّ، فَتُحَمَّلُ " +
            "بِمُعَرِّفِها الَّذي هُوَ السلاج، ولا تُستَعلَمُ جُملَةً."),

        // ═══ بَياناتُ المُستَأجِر ═══════════════════════════════════
        new(typeof(User), "users", ExportDisposition.Export,
            "قاعِدَةُ عُملاءِ المَتجَر. تَخرُجُ بِهُوِيّاتِها ووَسائِلِ " +
            "اتِّصالِها — قائِمَةُ عُملاءٍ بِلا هاتِفٍ ولا بَريدٍ لَيسَت " +
            "قائِمَة. واعتِمادُ الدَفعِ يُحذَف (‏TenantExportRedaction)."),

        new(typeof(Listing), "listings", ExportDisposition.Export,
            "إعلاناتُ المَتجَر. مَسقَطٌ Inline لِمَجرى أَحداث، فَالوَثيقَةُ " +
            "هي الحَقيقَةُ الحالِيَّةُ كامِلَةً بِعَدّادِ المُشاهَداتِ " +
            "وشاراتِ الإشراف."),

        new(typeof(Deal), "deals", ExportDisposition.Export,
            "صَفَقاتُ المَتجَر، ومَعَها `Timeline` — سِجِلُّ التَحَوُّلاتِ " +
            "كامِلاً. وهُوَ سِجِلُّ العَميلِ التِجارِيُّ لا سِجِلُّنا."),

        new(typeof(Conversation), "conversations", ExportDisposition.Export,
            "مُحادَثاتُ المَتجَر — أَطرافُها وآخِرُ رِسالَةٍ فيها."),

        new(typeof(Message), "messages", ExportDisposition.Export,
            "نُصوصُ الرَسائِلِ خاماً. بَياناتٌ شَخصِيَّةٌ لِطَرَفَين، " +
            "ومَسؤولِيَّتُها تَنتَقِلُ بِالاستِلام — ومَنصوصٌ عَلى ذلك " +
            "في `README` داخِلَ الحَقيبَة."),

        new(typeof(Notification), "notifications", ExportDisposition.Export,
            "إشعاراتُ مُستَخدِمي المَتجَر — نَصُّها وحالَةُ قِراءَتِها."),

        new(typeof(Cart), "carts", ExportDisposition.Export,
            "سَلّاتُ الشِراءِ المُعَلَّقَة — نِيَّةُ شِراءٍ لَم تَكتَمِل، " +
            "وهي بَياناتُ المَتجَرِ التِجارِيَّة."),

        new(typeof(Ticket), "tickets", ExportDisposition.Export,
            "تَذاكِرُ دَعمِ المَتجَرِ ورُدودُها. مَسقَطٌ Inline."),

        new(typeof(Offer), "offers", ExportDisposition.Export,
            "العُروضُ المُقَدَّمَةُ على الإعلانات. مَسقَطٌ Inline."),

        new(typeof(ListingMatch), "listing_matches", ExportDisposition.Export,
            "مُطابَقاتُ الإعلاناتِ بِالطَلَبات — نَتيجَةُ عَمَلٍ يَملِكُها " +
            "المَتجَرُ لا نَحن."),

        new(typeof(Subscription), "subscriptions", ExportDisposition.Export,
            "اشتِراكاتُ **زَبائِنِ المَتجَرِ في باقاتِه هُوَ** — لا " +
            "اشتِراكُه هُوَ في وَسايِل (ذاكَ `TenantPlan` وهُوَ عامّ)."),

        new(typeof(Plan), "plans", ExportDisposition.Export,
            "باقاتُ المَتجَرِ لِزَبائِنِه — تَسعيرُه هُوَ، لا تَسعيرُنا."),

        new(typeof(Favorite), "favorites", ExportDisposition.Export,
            "مُفَضَّلاتُ مُستَخدِمي المَتجَر."),

        new(typeof(SavedSearch), "saved_searches", ExportDisposition.Export,
            "عَمَلِيّاتُ البَحثِ المَحفوظَةُ وتَنبيهاتُها — نِيَّةُ شِراءٍ " +
            "مُصَرَّحٌ بِها، وهي مِن أَثمَنِ ما في القاعِدَة."),

        new(typeof(Review), "reviews", ExportDisposition.Export,
            "التَقييماتُ المُتَبادَلَةُ بَعدَ الصَفَقات — سُمعَةُ المَتجَرِ " +
            "المَبنِيَّةُ عِندَه."),

        new(typeof(Report), "reports", ExportDisposition.Export,
            "بَلاغاتُ المُستَخدِمينَ عَن مُحتَوىً أَو مُستَخدِم، وقَرارُ " +
            "الإشرافِ عَلَيها. سِجِلُّ إشرافِ المَتجَرِ على نَفسِه."),

        new(typeof(TenantRoleDefinition), "role_definitions", ExportDisposition.Export,
            "تَعريفاتُ أَدوارِ المَتجَر — **تَعريفُ التَطبيقِ نَفسِه** " +
            "لا بَياناتٍ فيه. مَحصورَةٌ بِـ`tenant_id` بِالسِياسَةِ العامَّة."),

        new(typeof(TenantThemeDefinition), "theme_definitions", ExportDisposition.Export,
            "ثيماتُ المَتجَر — **تَعريفُ التَطبيقِ نَفسِه**: أَلوانُه " +
            "وأَنصافُ أَقطارِه. مَحصورَةٌ بِـ`tenant_id`."),

        new(typeof(TenantPlanDefinition), "plan_definitions", ExportDisposition.Export,
            "تَعريفاتُ باقاتِ المَتجَرِ المُقتَرَحَةُ ودَورَةُ اعتِمادِها. " +
            "مَحصورَةٌ بِـ`tenant_id`."),

        new(typeof(AuditEntry), "audit", ExportDisposition.Export,
            "سِجِلُّ تَدقيقِ المَتجَر — مَن فَعَلَ ماذا ومَتى. يَخرُجُ " +
            "بِفِعلِه لا بِعُنوانِ صاحِبِه: `Ip` و`UserAgent` يُحذَفان، " +
            "وقُيودُ فَوتَرَةِ المَنَصَّةِ (`paypal ·` / `paddle ·`) تُحجَب."),

        new(typeof(TenantProviderBinding), "provider_bindings", ExportDisposition.Export,
            "رَبطُ المَتجَرِ بِمُزَوِّديه. يَخرُجُ مُعَتَّماً عَبرَ " +
            "`ProviderSecrecy` — النَوعُ الَّذي يُعرَضُ يَخرُجُ كامِلاً، " +
            "وأَعمِدَةُ الظَرفِ لا تَخرُجُ أَبَداً."),

        new(typeof(ImportedRecord), "imported_records", ExportDisposition.Export,
            "الصُفوفُ الخامُّ المَنقولَةُ مِن قاعِدَةِ المَتجَرِ السابِقَة. " +
            "مَحصورَةٌ بِالمُستَأجِرِ بِنيَوِيّاً، **ومَجهولَةُ الأَعمِدَة**: " +
            "أُخِذَت بِـ`SELECT *`. فَتَخرُجُ إلّا جَدوَلَي رُموزِ " +
            "الأَجهِزَة، وعَدَدُ المَحجوبِ مَكتوبٌ في الفَهرَس."),

        // ═══ بَياناتُ صاحِبِ المَتجَرِ نَفسِه ══════════════════════
        //
        // تَقَعُ في أَقسامٍ ثابِتَةٍ (`_studio`, `_incubator`) لا في
        // قِسمِ المَتجَر، فَتَرشيحُ السلاجِ لا يَراها — **وهذا نَقصٌ لا
        // أَمان**. وتُقرَأُ بِمُرَشِّحِ المالِكِ لا جُملَةً، وتُسَلَّمُ
        // في مُجَلَّدٍ مُسَمّىً لِئَلّا تُخلَطَ بِبَياناتِ المَتجَر.
        new(typeof(StudioUser), "owner_profile", ExportDisposition.ExportOwner,
            "حِسابُ صاحِبِ المَتجَرِ على المَنَصَّة — هُوِيَّتُه وباقَتُه " +
            "وحِصَصُه. يُحَمَّلُ بِمُعَرِّفِه وَحدَه، لا بِاستِعلامٍ على القِسم."),

        new(typeof(ConsentRecord), "owner_consent", ExportDisposition.ExportOwner,
            "مُوافَقَةُ صاحِبِ المَتجَرِ على شُروطِ الحاضِنَة وإصدارُها — " +
            "أَثَرٌ يَملِكُه. و`Ip`/`UserAgent` يُحذَفان."),

        new(typeof(IncubatorSession), "owner_analyses", ExportDisposition.ExportOwner,
            "دِراساتُ الجَدوى الَّتي أَنتَجَها صاحِبُ المَتجَر — أَغلى ما " +
            "يَملِك. مُرَشَّحَةٌ بِـ`OwnerUserId`، ولا يُستَعلَمُ القِسمُ جُملَةً."),

        // ═══ عامٌّ — جَدوَلٌ بِلا عَمودِ عَزل ═══════════════════════
        new(typeof(TenantPlan), "-", ExportDisposition.ExcludeGlobal,
            "باقَةُ المُستَأجِرِ **في وَسايِل** وسِعرُها ومُعَرِّفُ " +
            "اشتِراكِه عِندَ المُزَوِّد. جَدوَلٌ بِلا `tenant_id`، وصُفوفُه " +
            "قائِمَةُ عُملاءِ وَسايِلَ التِجارِيَّة. وحالَةُ الباقَةِ " +
            "يَراها المالِكُ في شاشَةِ الفَوتَرَةِ لا في حَقيبَةِ بَياناتِه."),

        new(typeof(PlatformSettings), "-", ExportDisposition.ExcludeGlobal,
            "إعداداتُ المَنَصَّةِ نَفسِها — لَيسَت بَياناتِ مُستَأجِرٍ " +
            "بِأَيِّ وَجه، وجَدوَلُها بِلا `tenant_id`."),

        new(typeof(PlatformPlanPayPal), "-", ExportDisposition.ExcludeGlobal,
            "رَبطُ باقاتِ المَنَصَّةِ بِخُطَطِ PayPal — خَصيصَةُ الباقَةِ " +
            "لا خَصيصَةُ مَتجَر، وجَدوَلُها بِلا `tenant_id`."),

        new(typeof(PayPalWebhookRecord), "-", ExportDisposition.ExcludeGlobal,
            "أَثَرُ مَنعِ تَكرارِ رِسائِلِ PayPal — مُعَرِّفاتُ رِسائِلِ " +
            "مُزَوِّدِ المَنَصَّة، لا بَياناتِ عَميل. وجَدوَلُه بِلا `tenant_id`."),

        new(typeof(PayPalOrderRecord), "-", ExportDisposition.ExcludeGlobal,
            "طَلَبُ دَفعٍ مُعَلَّقٌ في حِسابِ المَنَصَّةِ التِجارِيّ، " +
            "بِمُعَرِّفِ قَبضٍ عِندَ المُزَوِّد. جَدوَلٌ بِلا `tenant_id` " +
            "يَحمِلُ مُعامَلاتِ كُلِّ المُستَأجِرين."),

        new(typeof(PaddleTransactionRecord), "-", ExportDisposition.ExcludeGlobal,
            "نَظيرَتُها عِندَ Paddle — نَفسُ العِلَّةِ حَرفاً: جَدوَلٌ " +
            "بِلا `tenant_id` فيه مُعامَلاتُ كُلِّ المُستَأجِرين."),

        // ═══ اعتِماد ═══════════════════════════════════════════════
        new(typeof(Services.Api.ApiKeyDocument), "-", ExportDisposition.ExcludeSecret,
            "مَفاتيحُ الـAPI بِتَجزئاتِها (`SecretHash`). **جَدوَلٌ بِلا " +
            "`tenant_id`** فَلا شَبَكَةَ أَمانٍ بِنيَوِيَّةً تَحتَه، " +
            "والتَجزئَةُ لا تَنفَعُ المُستَلِمَ إذ لا تُعكَس. " +
            "والمَفاتيحُ تُدارُ مِن شاشَةِ المَفاتيحِ ذاتِها."),

        // ═══ آلِيَّةٌ داخِلِيَّة ═══════════════════════════════════
        new(typeof(Marketplace.Api.ApiIdempotencyRecord), "-", ExportDisposition.ExcludeInternal,
            "سِجِلُّ «مَرَّةً واحِدَة» لِنِداءاتِ الـAPI — أَثَرُ آلِيَّةٍ " +
            "لا بَياناتِ عَميل، ولا مَعنى لَه خارِجَ عَمَلِيَّتِنا."),

        new(typeof(AnalysisRunClaim), "-", ExportDisposition.ExcludeInternal,
            "مِفتاحُ حَجزِ تَشغيلَةِ تَحليل — وَثيقَةٌ لا مَعنى لَها إلّا " +
            "مُعَرِّفُها، وُجودُه يَمنَعُ تَشغيلَةً مُكَرَّرَة."),

        new(typeof(AgentSession), "-", ExportDisposition.ExcludeInternal,
            "مُحادَثَةُ كونسولِ وَكيلِ المَنَصَّةِ في قِسم `_admin`. " +
            "والفَرعُ الَّذي يُعطي جَلسَةً لِكُلِّ رائِدِ أَعمالٍ " +
            "(`scope:…`) **بِصِفرِ مُنادٍ اليَوم** — فَالمَوجودُ فِعلاً " +
            "جَلسَةٌ واحِدَةٌ مُشتَرَكَةٌ لِمُشرِفِ المَنَصَّة، وهي " +
            "بَياناتُنا لا بَياناتُ العَميل."),
    };

    /// <summary>ما يَخرُجُ فِعلاً — بِتَرتيبِ السِجِلّ.</summary>
    public static IReadOnlyList<ExportedType> Exported { get; } = All
        .Where(e => e.Disposition is ExportDisposition.Export
                                  or ExportDisposition.ExportSelf
                                  or ExportDisposition.ExportOwner)
        .ToArray();

    /// <summary>ما لا يَخرُج — ويُعلَنُ في الفَهرَسِ بِسَبَبِه، لا
    /// يُسقَطُ صامِتاً.</summary>
    public static IReadOnlyList<ExportedType> Excluded { get; } = All
        .Where(e => e.Disposition is ExportDisposition.ExcludeGlobal
                                  or ExportDisposition.ExcludeSecret
                                  or ExportDisposition.ExcludeInternal)
        .ToArray();

    private static readonly Dictionary<string, ExportedType> ByName =
        All.ToDictionary(e => e.TypeName, StringComparer.Ordinal);

    public static ExportedType? Find(string typeName)
        => ByName.TryGetValue(typeName, out var e) ? e : null;

    public static bool IsExported(string typeName)
        => Find(typeName) is { } e && Exported.Contains(e);
}
