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

namespace ACommerce.Templates.Customer.Marketplace.Services.Metering;

/// <summary>ما يَكتُبُه الحِفظ: وَثيقَةٌ تُخَزَّن، أَو حَدَثٌ يُلحَقُ بِمَجرى.</summary>
public enum MeterTypeKind { Document, Event }

/// <summary>ماذا يَفعَلُ العَدُّ بِهذا النَوع.</summary>
public enum MeterDisposition
{
    /// <summary>وِلادَتُه سَطرٌ واحِدٌ مِن وَحدَتِه — ولَه قاعِدَةٌ في <see cref="MeterRules"/>.</summary>
    Counted,

    /// <summary>وَثيقَةٌ يَكتُبُها إسقاطٌ مُباشِرٌ مِن أَحداث: الحَدَثُ هُوَ الوَحدَة، وعَدُّها مَعَه عَدٌّ مَرَّتَين (التَصميم §5.3).</summary>
    Derived,

    /// <summary>لا يُعَدُّ في هذه الموجَة — بِسَبَبٍ مَكتوب، أَو بِالبَندِ الَّذي يَعُدُّه.</summary>
    NotCounted,
}

/// <summary>شَكلُ الوِلادَةِ كَما يَراهُ المُستَمِعُ بِلا قِراءَةِ حالَةٍ مَخزونَة (التَصميم §5.2) — وهُوَ الإنشاءُ المُصطَنَعُ الَّذي يُشَغِّلُه G3.</summary>
public enum MeterCreation
{
    /// <summary>وَثيقَةٌ بِتَزامُنٍ تَفاؤُلِيٍّ تُخَزَّنُ ولا نُسخَةَ لَها في الجَلسَة (<c>VersionFor == null</c>، مَقيس D6).</summary>
    OptimisticStore,

    /// <summary>وَثيقَةٌ في <c>Inserts</c>.</summary>
    Insert,

    /// <summary>حَدَثٌ في مَجرىً يَبدَأ (<c>StreamAction.Start</c>).</summary>
    StartStream,
}

/// <summary>صَفٌّ واحِد — نَوعٌ ومَصيرُه وسَبَبُه. <b>النَوعُ لا اسمُه</b> (نَمَطُ <c>TenantExportLedger</c>): إعادَةُ تَسمِيَةٍ تَكسِرُ البِناءَ ولا تُسقِطُ الصَفَّ صامِتاً.</summary>
/// <param name="Unit">المَعدودُ: وَحدَتُه؛ والمُشتَقُّ: وَحدَةُ الحَدَثِ الَّذي يُعَدُّ مَكانَه؛ وغَيرُ المَعدودِ: الوَحدَةُ الَّتي سَتَعُدُّه إن كانَ لَها بَند — وإلّا لا شَيء.</param>
/// <param name="Creation">لِلمَعدودِ وَحدَه: شَكلُ وِلادَتِه.</param>
/// <param name="WhyAr">لِلمَعدودِ: <b>قاعِدَتُه</b> (الإشارَةُ والمَوضِعُ ومِن أَينَ المَتجَر)؛ ولِسِواه: سَبَبُه.</param>
public sealed record MeterClassificationRow(
    Type ClrType, MeterTypeKind Kind, MeterDisposition Disposition, string? Unit, MeterCreation? Creation, string WhyAr)
{
    public string TypeName => ClrType.Name;
}

/// <summary>
/// <para><b>دَفتَرُ التَصنيف — كُلُّ نَوعٍ يَكتُبُه الحِفظُ مُصَنَّف</b> (‏ADR-051 §١٤، التَصميم §5.6 و§12 G3). كُلُّ نَوعِ وَثيقَةٍ مُسَجَّلٍ أَو مُستَعمَلٍ وكُلُّ نَوعِ
/// حَدَث: <see cref="MeterDisposition.Counted"/> بِوَحدَتِه وقاعِدَتِه، أَو <see cref="MeterDisposition.Derived"/>، أَو
/// <see cref="MeterDisposition.NotCounted"/> بِسَبَبِه. فَنَوعٌ يولَدُ غَداً بِلا صَفٍّ يُحمِرُّ <c>MeterClassificationTests</c> — ولا يُكتَبُ
/// بِلا عَدٍّ ولا يُعَدُّ بِلا قَرار.</para>
/// <para><b>وهُوَ قابِلٌ لِلتَشغيل لا إعلان</b> (RULES-05): الحارِسُ يُنشِئُ كُلَّ نَوعٍ إنشاءً مُصطَنَعاً في جَلسَةٍ حَقيقِيَّةٍ ويُمَرِّرُ تَغييراتِها
/// المُعَلَّقَةَ إلى <see cref="MeterRules.Classify"/>: المَعدودُ سَطرٌ واحِدٌ مِن وَحدَتِه، وما سِواه صِفر. فَصَفٌّ «مَعدودٌ» بِلا قاعِدَةٍ يَحمَرّ،
/// وقاعِدَةٌ تَعُدُّ ما صُنِّفَ «غَيرَ مَعدودٍ» تَحمَرّ.</para>
/// <para><b>والقارِئُ هُوَ الحارِسُ لا وَقتُ التَشغيل</b>: صَفٌّ بَيانِيٌّ لا واجِهَةٌ ولا طَبَقَة (القاعِدَة ١). ومَكانُه بِجِوارِ
/// <see cref="MeterRules"/> لِأَنّ البُنودَ ٧ و٨ و٩ تَقلِبُ صُفوفَها في كوميتِ قاعِدَتِها نَفسِه — فَمَن يُغَيِّرُ المُصَنِّفَ يَرى دَفتَرَه.</para>
/// <para><b>اليَومَ المَعدودُ واحِد</b>: <c>Deal</c> ← <c>deal.create</c>. ووَحَداتُ البُنودِ التالِيَةِ «غَيرُ مَعدودَةٍ» بِوَحدَتِها الآتِيَة؛ فَإن دَخَلَت
/// المُعجَمَ قَبلَ أَن يُقلَبَ صَفُّها حَمِرَ الحارِس.</para>
/// </summary>
public static class MeterClassification
{
    /// <summary>نَوعٌ مَعدود: وَحدَتُه وشَكلُ وِلادَتِه وقاعِدَتُه.</summary>
    public static MeterClassificationRow Counted(Type type, string unit, MeterCreation creation, string ruleAr)
        => new(type, creation == MeterCreation.StartStream ? MeterTypeKind.Event : MeterTypeKind.Document,
               MeterDisposition.Counted, unit, creation, ruleAr);

    /// <summary>وَثيقَةٌ مُشتَقَّة: يَكتُبُها إسقاطٌ، والحَدَثُ وَحدَتُها.</summary>
    public static MeterClassificationRow Derived(Type type, string eventUnit, string whyAr)
        => new(type, MeterTypeKind.Document, MeterDisposition.Derived, eventUnit, null, whyAr);

    /// <summary>نَوعٌ لا يُعَدُّ في هذه الموجَة — بِسَبَبِه، وبِالوَحدَةِ الَّتي سَتَعُدُّه إن كانَ لَها بَند.</summary>
    public static MeterClassificationRow NotCounted(Type type, MeterTypeKind kind, string whyAr, string? laterUnit = null)
        => new(type, kind, MeterDisposition.NotCounted, laterUnit, null, whyAr);

    private const MeterTypeKind Doc = MeterTypeKind.Document;
    private const MeterTypeKind Ev = MeterTypeKind.Event;

    private const string Item7 = "يُعَدُّ مِن البَند ٧ (وَحَداتُ الأَحداث): وِلادَةُ مَجراه بِحَدَثِه، والمَتجَرُ مُستَأجِرُ المَجرى.";
    private const string Item8 = "يُعَدُّ مِن البَند ٨ (وَحَداتُ الوَثائِق)، بَعدَ تَوصيفِ البَذرِ وتَحويلِ وِلاداتِه المَحروسَةِ إلى Insert.";
    private const string Item9 = "يُعَدُّ مِن البَند ٩ (chat.message)، بَعدَ تَوصيفِ الإرسالِ وتَحويلِ Store إلى Insert في نُقطَتِه وفي البَذر.";
    private const string Presence = "جانِبُ التِقنِيَة: يُعَدُّ حُضوراً لِكُلِّ مَتجَرٍ في كُلِّ يَوم (البَند ١٩) لا عَمَلِيَّةً عِندَ الحِفظ — فَالحِفظُ لا يَعُدُّه.";
    private const string Transition = "انتِقالٌ لا وِلادَة: الانتِقالاتُ وَحدَةٌ لاحِقَةٌ بِمُعَرِّفِ مَوضِعِها في المَجرى (التَصميم §5.6 و§15).";
    private const string Billing = "سِجِلُّ فَوتَرَةِ المَنَصَّةِ عِندَ مُزَوِّدِها: التَحصيلُ خارِجَ الموجَة (قَرارُ المالِك «لا تهتم كثيرا بطريقة التحصيل»)، ونِداءاتُ المُزَوِّدِ نَفسُها تُعَدُّ عَلى حَدِّ الصادِر (البَند ١٤).";
    private const string Internal = "سِجِلُّ آلِيَّةٍ داخِلِيَّة لا عَمَلِيَّةُ مَتجَر (التَصميم §5.6).";
    private const string Infra = "بُنيَةُ الناقِلِ أَو عَفريتِ الأَحداث — تُسَجِّلُها مَكتَبَتُها في التَركيب، ولا يَكتُبُها مَتجَر (التَصميم §5.6).";

    public static IReadOnlyList<MeterClassificationRow> All { get; } =
    [
        // ═══ المَعدود ═════════════════════════════════════════════════════
        Counted(typeof(Deal), "deal.create", MeterCreation.OptimisticStore,
            "MeterRules.Classify: صَفقَةٌ مُعَلَّقَةٌ بِلا نُسخَةٍ في الجَلسَة (VersionFor == null، مَقيس D6) — المَوضِعُ DealsService.StartAsync، " +
            "والمَتجَرُ Deal.TenantSlug، والمَصدَرُ مُعَرِّفُ الصَفقَة. وتَحديثُها لا يُعَدّ."),

        // ═══ المُشتَقّ — لَقَطاتُ الإسقاطِ المُباشِر ══════════════════════
        Derived(typeof(Listing), "listing.create",
            "لَقطَةُ مَجرى الإعلانِ يَكتُبُها الإسقاطُ المُباشِرُ في وَحدَةِ العَمَلِ نَفسِها؛ والوَحدَةُ حَدَثُ ListingCreated."),
        Derived(typeof(Offer), "offer.submit",
            "لَقطَةُ مَجرى العَرضِ يَكتُبُها الإسقاطُ المُباشِر؛ والوَحدَةُ حَدَثُ OfferSubmitted."),
        Derived(typeof(Ticket), "ticket.open",
            "لَقطَةُ مَجرى التَذكِرَةِ يَكتُبُها الإسقاطُ المُباشِر؛ والوَحدَةُ حَدَثُ TicketCreated."),
        Derived(typeof(Subscription), "member_plan.subscribe",
            "لَقطَةُ مَجرى اشتِراكِ العُضوِ يَكتُبُها الإسقاطُ المُباشِر؛ والوَحدَةُ حَدَثُ SubscriptionCreated."),

        // ═══ وَثائِقُ تُعَدُّ في بُنودٍ تالِيَة ══════════════════════════════
        NotCounted(typeof(Tenant), Doc, Item8 + " المَتجَرُ Tenant.Id، ورائِدُ الأَعمالِ Tenant.OwnerUserId.", "store.create"),
        NotCounted(typeof(StudioUser), Doc, Item8 + " بِلا نُسخَة (VersionFor == null)، والمَتجَرُ _platform.", "studio.account.create"),
        NotCounted(typeof(User), Doc, Item8 + " بِلا نُسخَة، والمَصدَرُ اسمٌ مُستَعارٌ لا مُعَرِّفُ الشَخص.", "member.join"),
        NotCounted(typeof(Review), Doc, Item8, "review.create"),
        NotCounted(typeof(Message), Doc, Item9, "chat.message"),

        // ═══ وَثائِقُ لا تُعَدّ — بِسَبَبِها ═════════════════════════════════
        NotCounted(typeof(Conversation), Doc, "وِعاءُ الرَسائِل: الرِسالَةُ هِيَ الوَحدَة (البَند ٩)، والمُحادَثَةُ تُحَدَّثُ مَعَ كُلِّ رِسالَةٍ فَعَدُّها يُكَرِّرُها."),
        NotCounted(typeof(Notification), Doc, "إشعارٌ داخِلَ التَطبيق: لا صَفَّ لَه في الدِراسَةِ الَّتي تُسَمّي الوَحَدات (التَصميم §15)؛ وما يَخرُجُ مِنه إلى مُزَوِّدٍ يُعَدُّ عَلى حَدِّ الصادِر."),
        NotCounted(typeof(Cart), Doc, "سَلَّةٌ لَم تَكتَمِل: الطَلَبُ يُعَدُّ صَفقَةً حينَ يولَد (deal.create)، والسَلَّةُ نِيَّةٌ لا عَمَلِيَّة (التَصميم §15)."),
        NotCounted(typeof(Favorite), Doc, "مُفَضَّلَةُ مُستَخدِم: لا صَفَّ لَها في الدِراسَةِ الَّتي تُسَمّي الوَحَدات (التَصميم §15)."),
        NotCounted(typeof(SavedSearch), Doc, "بَحثٌ مَحفوظ: لا صَفَّ لَه في الدِراسَةِ الَّتي تُسَمّي الوَحَدات (التَصميم §15)."),
        NotCounted(typeof(ListingMatch), Doc, "مُطابَقَةٌ تُكتَبُ حينَ يُقبَلُ عَرض (مَعَ OfferAccepted): أَثَرُ انتِقالٍ لا وِلادَة، والعَرضُ يُعَدُّ حينَ يُقَدَّم (offer.submit)."),
        NotCounted(typeof(ImportedRecord), Doc, "صَفٌّ خامٌّ مَنقولٌ مِن قاعِدَةِ المَتجَرِ السابِقَة: نَقلُ بَيانات لا عَمَلِيَّة، والمُستَورِدُ يَتَجاوَزُ المُستَمِعَ بِبِنائِه (التَصميم §15)."),
        NotCounted(typeof(Report), Doc, "بَلاغُ مُستَخدِمٍ عَن مُحتَوىً أَو مُستَخدِم: لا صَفَّ لَه في الدِراسَةِ الَّتي تُسَمّي الوَحَدات (التَصميم §15)."),
        NotCounted(typeof(Plan), Doc, "باقَةٌ لِأَعضاءِ المَتجَر يَكتُبُها البَذرُ وَحدَه اليَوم: إعدادٌ لا عَمَلِيَّة، وحُضورُ باقاتِ الأَعضاءِ يُعَدُّ مِن تَعريفاتِها (البَند ١٩)."),
        NotCounted(typeof(TenantRoleDefinition), Doc, Presence),
        NotCounted(typeof(TenantThemeDefinition), Doc, Presence),
        NotCounted(typeof(TenantPlanDefinition), Doc, Presence),
        NotCounted(typeof(TenantProviderBinding), Doc, Presence),
        NotCounted(typeof(Services.Api.ApiKeyDocument), Doc, Presence),
        NotCounted(typeof(TenantPlan), Doc, "باقَةُ المَتجَرِ في المَنَصَّةِ نَفسِها: إعدادُ المَنَصَّةِ لِعَميلِها لا عَمَلِيَّةٌ يَفعَلُها المَتجَر."),
        NotCounted(typeof(PlatformSettings), Doc, "إعداداتُ المَنَصَّة: لا مَتجَرَ لَها ولا عَمَلِيَّة."),
        NotCounted(typeof(PlatformPlanPayPal), Doc, Billing),
        NotCounted(typeof(PayPalWebhookRecord), Doc, Billing),
        NotCounted(typeof(PayPalOrderRecord), Doc, Billing),
        NotCounted(typeof(PaddleTransactionRecord), Doc, Billing),
        NotCounted(typeof(AuditEntry), Doc, Internal),
        NotCounted(typeof(ModelCallRecord), Doc, Internal + " ونِداءُ النَموذَجِ نَفسُه يُعَدُّ عَلى حَدِّ الصادِر (model.call، البَند ١٥)."),
        NotCounted(typeof(Marketplace.Api.ApiIdempotencyRecord), Doc, Internal),
        NotCounted(typeof(AnalysisRunClaim), Doc, Internal),
        NotCounted(typeof(IncubatorSession), Doc, Internal),
        NotCounted(typeof(ConsentRecord), Doc, Internal),
        NotCounted(typeof(AgentSession), Doc, Internal),
        NotCounted(typeof(Wolverine.Envelope), Doc, Infra),
        NotCounted(typeof(JasperFx.Events.Daemon.DeadLetterEvent), Doc, Infra),

        // ═══ أَحداثٌ تُعَدُّ في البَند ٧ ═══════════════════════════════════
        NotCounted(typeof(ListingCreated), Ev, Item7, "listing.create"),
        NotCounted(typeof(OfferSubmitted), Ev, Item7, "offer.submit"),
        NotCounted(typeof(TicketCreated), Ev, Item7, "ticket.open"),
        NotCounted(typeof(SubscriptionCreated), Ev, Item7, "member_plan.subscribe"),

        // ═══ أَحداثٌ لا تُعَدّ — بِسَبَبِها ════════════════════════════════
        NotCounted(typeof(ListingViewed), Ev, "مُشاهَدَةُ صَفحَة: المُشاهَداتُ خارِجَ الموجَة (التَصميم §15)."),
        NotCounted(typeof(ListingEdited), Ev, Transition),
        NotCounted(typeof(ListingDeleted), Ev, Transition),
        NotCounted(typeof(ListingMediaSet), Ev, Transition),
        NotCounted(typeof(ListingFlagsSet), Ev, Transition),
        NotCounted(typeof(ListingModerated), Ev, Transition),
        NotCounted(typeof(OfferAccepted), Ev, Transition),
        NotCounted(typeof(OfferRejected), Ev, Transition),
        NotCounted(typeof(OfferWithdrawn), Ev, Transition),
        NotCounted(typeof(OfferExpired), Ev, Transition),
        NotCounted(typeof(OffererRenamed), Ev, "مَحوُ اسمِ صاحِبِ عَرضٍ حَذَفَ حِسابَه: أَثَرُ المَحوِ لا عَمَلِيَّةُ مَتجَر."),
        NotCounted(typeof(TicketAuthorRenamed), Ev, "مَحوُ اسمِ كاتِبِ تَذكِرَةٍ حَذَفَ حِسابَه: أَثَرُ المَحوِ لا عَمَلِيَّةُ مَتجَر."),
        NotCounted(typeof(TicketReplied), Ev, "رَدٌّ في تَذكِرَة: «دَقيقَةُ الدَعم» في الدِراسَة لا يَقيسُها مَوضِع، والتَذكِرَةُ تُعَدُّ حينَ تُفتَح (التَصميم §15)."),
        NotCounted(typeof(TicketClosed), Ev, Transition),
        NotCounted(typeof(QuotaConsumed), Ev, "قَيدُ حِصَّةِ الاستِحقاقِ داخِلَ الاشتِراك: الإعلانُ الَّذي استَهلَكَها هُوَ العَمَلِيَّة (listing.create)."),
        NotCounted(typeof(QuotaRefunded), Ev, "قَيدُ حِصَّةٍ بِلا مُصدِر اليَوم (يَتيمٌ مُثَبَّت): لا عَمَلِيَّةَ تَكتُبُه."),
        NotCounted(typeof(SubscriptionRenewed), Ev, Transition),
        NotCounted(typeof(SubscriptionCancelled), Ev, Transition),
        NotCounted(typeof(SubscriptionExpired), Ev, Transition),
    ];
}
