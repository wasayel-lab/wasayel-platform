using System.Reflection;
using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Delivery;
using ACommerce.Kit.Offers;
using ACommerce.Kit.Reviews;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ حَذفُ الحِسابِ يَترُكُ الاسمَ والهاتِفَ والعُنوانَ عَلى كُلِّ صَفقَة — ونُصوصٌ ثَلاثَةٌ تَعِدُ بِسِجِلٍّ «بِلا اسمٍ ولا وَسيلَةِ اتِّصال» ═══
//
// تَدقيقُ الإطلاق ‏brief-P3-5a (حاجِبَة، ‏2026-10-24 عِندَ `dace7cd`؛ أُعيدَ قِياسُه عِندَ `8fabd61`): `AccountDeletion.Erase` يَمَسُّ وَثيقَةَ
// `User` وَحدَها، والنُقطَةُ `/{slug}/me/delete/confirm` لا تَحفَظُ غَيرَها — ولا مُستَمِعَ ولا `Patch` يَبلُغُ `Deal`. والصَفقَةُ تَحمِلُ
// مُنذُ وِلادَتِها اسمَ المُشتَري (`InitiatorName`) وعُنوانَ التَوصيلِ وهاتِفَه (`Attributes["addr"/"phone"]`): يَقرَؤُها البائِعُ في
// `MyDealDetail` بِلا شَرطِ مَرحَلَة، ويَعرِضُ الاستوديو الاسمَ لِصاحِبِ المَتجَر، وتَحمِلُ مُسَوَّدَةُ OTO الثَلاثَة. ونُسَخُ الاسمِ قائِمَةٌ
// كَذلِكَ في المُحادَثاتِ والتَقييماتِ والعُروض. وحِسابُ الاستوديو (`StudioUser` بِهاتِفٍ وبَريدٍ واسم) لا بابَ لِحَذفِه بَينَ مَساراتِ
// `/studio` — و`/privacy` تَعِدُ بِالحَذفِ «مِن داخِلِ التَطبيق» بِلا تَمييز.
//
// **يُثَبَّتُ هُنا قَبلَ التَبديلِ** (القاعِدَة ٣): الصُفوفُ الأَربَعَةُ الأولى وَقائِعُ الكودِ — تَبقى بِلا تَعديلِ حَرفٍ بَعدَ الخِيارِ (ب)
// (تَصحيحِ النُصوص)، فَهي ما يَقولُه النَصُّ الجَديد؛ والخامِسُ (`Today_…`) يَنقَلِبُ بِاسمِه في كوميتِ الصِياغَة. ويَومَ يُختارُ الخِيارُ
// (أ) (مَدُّ الحَذفِ إلى الصَفَقات) يُحمِرُّ الأَوَّلُ والثاني بِقَصدٍ ويُكتَبُ النَصُّ مَعَهُما.
public class AccountDeletionRetentionTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Tpl(params string[] relative) => File.ReadAllText(Path.Combine(
        new[] { RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace" }.Concat(relative).ToArray()));

    /// <summary>جِسمُ نُقطَةِ الحَذفِ — مِن تَسجيلِها إلى مُرَشِّحِها الأَخير، بِلا تَعليقات.</summary>
    internal static string DeleteEndpointBody()
    {
        var src = WriteEndpointGuardTests.StripComments(Tpl("MarketplaceTemplateExtensions.cs"));
        var start = src.IndexOf("\"/{slug}/me/delete/confirm\"", StringComparison.Ordinal);
        Assert.True(start > 0, "لا نُقطَةَ حَذفٍ في MarketplaceTemplateExtensions.cs — الأَداةُ عَمياء.");
        var end = src.IndexOf("RequireStoreWritable", start, StringComparison.Ordinal);
        Assert.True(end > start, "جِسمُ نُقطَةِ الحَذفِ بِلا نِهايَة — الأَداةُ عَمياء.");
        return src[start..end];
    }

    // ─── ١. الحَذفُ يَمَسُّ وَثيقَةَ المُستَخدِمِ وَحدَها ──────────────────────────────────────────────────

    [Fact]
    public void Deletion_writes_the_user_document_alone_and_nothing_else_reaches_a_deal()
    {
        var body = DeleteEndpointBody();
        var stores = Regex.Matches(body, @"\.Store\((?<arg>[^)]*)\)").Select(m => m.Groups["arg"].Value.Trim()).ToList();
        var patches = Regex.Matches(body, @"\.Patch<").Count;
        var dealMentions = Regex.Matches(body, @"\bDeal\b|Deals\.|DealsService").Count;

        // ومَن يُنادي `Erase` في الكودِ كُلِّه، ومَن يَقرَأُ `DeletedAt` في خِدماتِ الصَفَقات.
        var erasers = EntitlementContractTests.SourceFiles()
            .Where(f => WriteEndpointGuardTests.StripComments(f.Text).Contains("AccountDeletion.Erase(", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f.File)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var dealsDir = Path.Combine(RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Deals");
        var dealFiles = Directory.GetFiles(dealsDir, "*.cs", SearchOption.AllDirectories);
        var deletionAware = dealFiles
            .Where(f => Regex.IsMatch(WriteEndpointGuardTests.StripComments(File.ReadAllText(f)), @"DeletedAt|AccountDeletion"))
            .Select(Path.GetFileName).ToList();

        output.WriteLine($"Store(…) في النُقطَة: {stores.Count} ({string.Join(", ", stores)}) · Patch: {patches} · ذِكرُ الصَفقَة: {dealMentions}" +
                         $" · مَن يُنادي Erase: {string.Join(", ", erasers)} · مِلَفّاتُ الصَفَقاتِ المَفحوصَة: {dealFiles.Length}، تَعرِفُ الحَذف: {deletionAware.Count}");

        Assert.Equal(new[] { "user!" }, stores);
        Assert.Equal(0, patches);
        Assert.Equal(0, dealMentions);
        Assert.Equal(new[] { "MarketplaceTemplateExtensions.cs" }, erasers);
        Assert.True(dealFiles.Length >= 10, $"أَداةٌ عَمياء: {dealFiles.Length} مِلَفّاً في خِدماتِ الصَفَقات.");
        Assert.Empty(deletionAware);

        // و`Erase` لا يَأخُذُ إلّا الوَثيقَة: لا جَلسَةَ ولا مُعَرِّفَ يَبلُغُ بِه غَيرَها.
        var erase = typeof(AccountDeletion).GetMethod(nameof(AccountDeletion.Erase))!;
        Assert.Equal(new[] { typeof(User), typeof(DateTime) }, erase.GetParameters().Select(p => p.ParameterType));
    }

    // ─── ٢. الصَفقَةُ تَحمِلُ الاسمَ والهاتِفَ والعُنوان، ويَراها ثَلاثَة ──────────────────────────────────

    [Fact]
    public void A_deal_carries_the_buyer_name_phone_and_address_and_the_vendor_the_studio_and_the_carrier_read_them()
    {
        // الكاتِب: بابُ السَلَّةِ يَكتُبُ الثابِتَينِ عِندَ الوِلادَة.
        var mte = WriteEndpointGuardTests.StripComments(Tpl("MarketplaceTemplateExtensions.cs"));
        Assert.Contains("[Services.Deals.Deal.DeliveryAddressAttribute] = addr", mte, StringComparison.Ordinal);
        Assert.Contains("[Services.Deals.Deal.DeliveryPhoneAttribute] = phone", mte, StringComparison.Ordinal);
        Assert.Contains("initiatorId: userId, initiatorName: name", mte, StringComparison.Ordinal);

        // القارِئُ الأَوَّل — البائِع: العُنوانُ والهاتِفُ تَحتَ «لَستُ المُشتَري وأَنا البائِع» وَحدَه، بِلا شَرطِ مَرحَلَة.
        var vendor = UpgradePromptPriceTests.Code("libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Components", "Pages", "MyDealDetail.razor");
        var at = vendor.IndexOf("buyerAddr  = deal.Attributes.TryGetValue(Deal.DeliveryAddressAttribute", StringComparison.Ordinal);
        Assert.True(at > 0, "لا قِراءَةَ لِعُنوانِ التَوصيلِ في MyDealDetail — الأَداةُ عَمياء.");
        Assert.Equal(new[] { "!isInitiator && isListingOwner" }, UpgradePromptPriceTests.EnclosingIfConditions(vendor, at));

        // القارِئُ الثاني — صاحِبُ المَتجَرِ في الاستوديو: الاسم.
        var studio = UpgradePromptPriceTests.Code("libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "Components", "Pages", "StudioAppDealDetail.razor");
        Assert.Contains("@deal.InitiatorName", studio, StringComparison.Ordinal);
        Assert.Contains("t.OwnerUserId != Auth.UserId!.Value", studio, StringComparison.Ordinal);

        // القارِئُ الثالِث — شَرِكَةُ الشَحن: المُسَوَّدَةُ الَّتي تُرسَلُ إلى OTO تَحمِلُ الثَلاثَة.
        var deal = new Deal
        {
            InitiatorName = "مُشتَرٍ",
            Attributes = new() { [Deal.DeliveryAddressAttribute] = "حَيُّ المِثال", [Deal.DeliveryPhoneAttribute] = "0500000000" },
        };
        var draft = DealShipping.DraftFor(deal, "Riyadh");
        output.WriteLine($"مُسَوَّدَةُ OTO: {draft.Customer}");
        Assert.Equal(new OtoCustomer("مُشتَرٍ", "0500000000", "حَيُّ المِثال", "Riyadh", "SA"), draft.Customer);
    }

    // ─── ٣. ونُسَخُ الاسمِ خارِجَ وَثيقَةِ المُستَخدِم ──────────────────────────────────────────────────────

    /// <summary>كُلُّ حَقلٍ يَحمِلُ نُسخَةً مِن اسمِ شَخصٍ في وَثيقَةٍ غَيرِ <see cref="User"/> — مَكتوبَةٌ وَقتَ الفِعلِ ولا يَمَسُّها الحَذف.</summary>
    internal static readonly (Type Doc, string Field)[] NameCopies =
    {
        (typeof(Deal), nameof(Deal.InitiatorName)),
        (typeof(DealEvent), nameof(DealEvent.ActorName)),
        (typeof(Conversation), nameof(Conversation.OwnerName)),
        (typeof(Conversation), nameof(Conversation.PartnerName)),
        (typeof(Review), nameof(Review.AuthorName)),
        (typeof(Review), nameof(Review.TargetUserName)),
        (typeof(Offer), nameof(Offer.OffererName)),
    };

    [Fact]
    public void Copies_of_the_name_live_on_deals_conversations_reviews_and_offers_and_the_eraser_takes_none_of_them()
    {
        foreach (var (doc, field) in NameCopies)
            Assert.True(doc.GetProperty(field, BindingFlags.Public | BindingFlags.Instance)?.PropertyType is { } t
                        && (t == typeof(string)),
                $"{doc.Name}.{field} لَيسَ حَقلَ نَصٍّ — الجَردُ تَغَيَّر.");

        var body = DeleteEndpointBody();
        var touched = NameCopies.Where(c => body.Contains(c.Doc.Name, StringComparison.Ordinal)).ToList();
        output.WriteLine($"نُسَخُ الاسمِ المَجرودَة: {NameCopies.Length} ({string.Join(", ", NameCopies.Select(c => $"{c.Doc.Name}.{c.Field}"))}) · يَمَسُّها الحَذف: {touched.Count}");
        Assert.True(NameCopies.Length >= 5, $"أَداةٌ عَمياء: {NameCopies.Length} نُسخَة.");
        Assert.Empty(touched);
    }

    // ─── ٣ب. الجَردُ بِالمَسحِ لا بِاليَد ─────────────────────────────────────────────────────────────────────
    //
    // مُراجَعَةُ تَيّارِ W8-4 (‏2026-10-25، كُبرى، عَلى b52ea8d/ce026d5): `NameCopies` أَعلاه كُتِبَت بِاليَدِ ووُصِفَت «مَجرودَة»، وحارِسُها
    // `NameCopies.Length >= 5` صادِقٌ دائِماً لِمَصفوفَةٍ ثابِتَة — فَلا يَعمى ولا يَرى نُسخَةً جَديدَة (القاعِدَة ١٠). وفاتَها مِن الكودِ نَفسِه:
    // `Deal.CounterpartyName` (اسمُ البائِعِ عَلى كُلِّ صَفقَة)، و`ListingMatch.OffererName` وحَدَثُ `OfferSubmitted` في مَجرى الأَحداث،
    // وتَذاكِرُ الدَعمِ وأَحداثُها (والبَلاغُ عَن إعلانٍ تَذكِرَة)، و`AuditEntry.ActorName` و`ApiKeyDocument.ActorName` — وإشعاراتٌ مُخَزَّنَةٌ في
    // صَناديقِ غَيرِه تَحمِلُ اسمَه، وإشعارُ تَسجيلِه لِمُديري المَتجَرِ يَحمِلُ اسمَه **وهاتِفَه أَو بَريدَه**.
    //
    // فَالجَردُ هُنا يُشتَقُّ مِن الكود: (١) ما يُخَزَّن — كُلُّ نَوعٍ يُسَمّيه `Schema.For<…>`/`Query<…>`/`LoadAsync<…>`/`AggregateStreamAsync<…>`/
    // `StartStream<…>`/`Store(new …)`/`Events.Append(… new …)`، وأَحداثُ ما يُجَمِّعُ نَفسَه بِـ`Apply`، وكُلُّ ما تَبلُغُه خَصائِصُها؛ ثُمَّ كُلُّ
    // خاصِّيَّةِ نَصٍّ اسمُها يَنتَهي بِـ`Name` فيها. (٢) ومَن يَكتُبُ `Notification` — كُلُّ مُهَيِّئٍ فيه `RelatedUrl =` وكُلُّ نِداءٍ لِـ`NotifyAdminsAsync` —
    // وما في عُنوانِه ومَتنِه مِن اسمٍ أَو هاتِفٍ أَو بَريد. وكُلُّ ما يَجِدُه المَسحُ مُصَنَّفٌ بِسَبَبِه، والتَصنيفُ ثُنائِيُّ الاتِّجاه: جَديدٌ بِلا
    // تَصنيفٍ يُحمِرّ، وتَصنيفٌ لا يَجِدُه المَسحُ يُحمِرّ. والصَفُّ أَعلاه يَبقى بِحَرفِه (القاعِدَة ٣) — صادِقٌ عَن سَبعٍ، وهُنا يُفحَصُ أَنّه بَعضُ ما
    // يَجِدُه المَسح.

    /// <summary>ما هي خاصِّيَّةُ الاسمِ لِلحَذف — مَعجَمٌ مُغلَق.</summary>
    internal enum NameKind
    {
        /// <summary>نُسخَةٌ مِن اسمِ شَخصٍ تُكتَبُ وَقتَ الفِعلِ ولا يَمَسُّها الحَذف.</summary>
        Copy,
        /// <summary>الاسمُ في الوَثيقَةِ الَّتي يَمحوها <c>AccountDeletion.Erase</c> نَفسُها.</summary>
        Erased,
        /// <summary>حِسابُ الاستوديو — لا بابَ لِحَذفِه، ويَقولُ النَصُّ ذلِكَ بِاسمِه (الصَفّ ٤).</summary>
        StudioAccount,
        /// <summary>يُخَزَّنُ إن كُتِب، ولا كاتِبَ لَه اليَوم — يُفحَصُ الغِياب.</summary>
        Unwritten,
        /// <summary>اسمُ شَيءٍ لا شَخص.</summary>
        NotAPerson,
    }

    /// <summary>العِبارَةُ الَّتي تَقولُ النُسخَةَ في نَصِّ الحَذف — مَعجَمٌ مُغلَق، ولِكُلٍّ عِباراتُه في <see cref="DisclosurePhrases"/>.</summary>
    internal enum Disclosure { None, Deal, Records, Notifications, AdminNotices, StoreRecords }

    internal sealed record NameField(NameKind Kind, Disclosure Said, string WhyAr);

    /// <summary>كُلُّ خاصِّيَّةِ اسمٍ يَجِدُها <see cref="StoredNameFields"/> — بِتَصنيفِها وسَبَبِه. يُعَدَّلُ في الكوميتِ الَّذي يُغَيِّرُ ما يُخَزَّن.</summary>
    internal static readonly IReadOnlyDictionary<string, NameField> PersonNameFields = new Dictionary<string, NameField>(StringComparer.Ordinal)
    {
        ["Deal.InitiatorName"]           = new(NameKind.Copy, Disclosure.Deal, "اسمُ المُشتَري كَما كُتِبَ في الطَلَب (مِن الاستِمارَةِ في السَلَّة، ومِن الحِسابِ في العَرض)."),
        ["Deal.CounterpartyName"]        = new(NameKind.Copy, Disclosure.Deal, "اسمُ البائِعِ يَومَ رُبِطَ أَو قَبِلَ — `DealsService` عِندَ التَعيين."),
        ["DealEvent.ActorName"]          = new(NameKind.Copy, Disclosure.Deal, "سَطرُ الخَطِّ الزَمَنيِّ لِكُلِّ فِعلٍ عَلى الصَفقَةِ بِاسمِ فاعِلِه."),
        ["Conversation.OwnerName"]       = new(NameKind.Copy, Disclosure.Records, "طَرَفا المُحادَثَةِ بِاسمَيهِما عِندَ بَدئِها."),
        ["Conversation.PartnerName"]     = new(NameKind.Copy, Disclosure.Records, "طَرَفا المُحادَثَةِ بِاسمَيهِما عِندَ بَدئِها."),
        ["Review.AuthorName"]            = new(NameKind.Copy, Disclosure.Records, "كاتِبُ التَقييم."),
        ["Review.TargetUserName"]        = new(NameKind.Copy, Disclosure.Records, "مَن قُيِّمَ — اسمُه عَلى تَقييمِ غَيرِه لَه."),
        ["Offer.OffererName"]            = new(NameKind.Copy, Disclosure.Records, "مُقَدِّمُ العَرض — مُسقَطٌ مِن حَدَثِه."),
        ["OfferSubmitted.OffererName"]   = new(NameKind.Copy, Disclosure.Records, "حَدَثُ تَقديمِ العَرضِ في مَجرى الأَحداث — لا يُحذَفُ بِحَذفِ الوَثيقَة."),
        ["ListingMatch.OffererName"]     = new(NameKind.Copy, Disclosure.Records, "صاحِبُ العَرضِ المَقبولِ عَلى مُطابَقَةِ الإعلان (ويُرسَلُ في إشعارِ «وَصَلَ السائِق»)."),
        ["Ticket.AuthorName"]            = new(NameKind.Copy, Disclosure.Records, "كاتِبُ تَذكِرَةِ الدَعم — والبَلاغُ عَن إعلانٍ تَذكِرَةٌ بِاسمِه."),
        ["TicketCreated.AuthorName"]     = new(NameKind.Copy, Disclosure.Records, "حَدَثُ فَتحِ التَذكِرَةِ في مَجرى الأَحداث."),
        ["Reply.AuthorName"]             = new(NameKind.Copy, Disclosure.Records, "كاتِبُ الرَدّ — كاتِبُه الوَحيدُ اليَومَ رَدُّ الاستوديو بِـ«دَعم التَّطبيق»، ويُعَدُّ نُسخَةً لِأَنّ الحَقلَ لِلاسم."),
        ["TicketReplied.AuthorName"]     = new(NameKind.Copy, Disclosure.Records, "حَدَثُ الرَدِّ في مَجرى الأَحداث — كَسابِقِه."),
        ["AuditEntry.ActorName"]         = new(NameKind.Copy, Disclosure.StoreRecords, "سِجِلُّ تَدقيقِ المَتجَر: البائِعُ عِندَ رَبطِ الشَحن، وفَريقُ الإدارَةِ عِندَ تَغييرِ الإعدادات."),
        ["ApiKeyDocument.ActorName"]     = new(NameKind.Copy, Disclosure.StoreRecords, "الفاعِلُ الَّذي يُصدِرُ لَه صاحِبُ المَتجَرِ مِفتاحَ رَبط."),
        ["User.FullName"]                = new(NameKind.Erased, Disclosure.None, "الوَثيقَةُ الَّتي يَمحوها `Erase` (الصَفّ ١)."),
        ["StudioUser.FullName"]          = new(NameKind.StudioAccount, Disclosure.None, "حِسابُ الاستوديو — لا بابَ لِحَذفِه (الصَفّ ٤)."),
        ["IncubatorSession.OwnerName"]   = new(NameKind.StudioAccount, Disclosure.None, "جَلسَةُ تَحليلِ الاستوديو لِصاحِبِها."),
        ["Report.ReporterName"]          = new(NameKind.Unwritten, Disclosure.None, "`ReportHandlers` يَكتُبُه مِن `SubmitReport`، ولا مُصدِرَ لِـ`SubmitReport` — والبَلاغُ الحَيُّ تَذكِرَة."),
        ["Tenant.Name"]                  = new(NameKind.NotAPerson, Disclosure.None, "اسمُ المَتجَر."),
        ["Plan.Name"]                    = new(NameKind.NotAPerson, Disclosure.None, "اسمُ باقَة."),
        ["PlatformPlanPayPal.Name"]      = new(NameKind.NotAPerson, Disclosure.None, "اسمُ خُطَّةِ PayPal."),
        ["Role.PwaName"]                 = new(NameKind.NotAPerson, Disclosure.None, "اسمُ تَطبيقِ الدَور."),
        ["AgentToolCall.Name"]           = new(NameKind.NotAPerson, Disclosure.None, "اسمُ أَداةِ الوَكيل."),
        ["ApiKeyDocument.Name"]          = new(NameKind.NotAPerson, Disclosure.None, "تَسمِيَةُ المِفتاحِ يَكتُبُها المالِك."),
    };

    /// <summary>كُلُّ نَوعٍ مُحَمَّلٍ مِن تَجميعاتِ <c>ACommerce</c> بِجِوارِ الاختِبار — نَمَطُ <c>AppliedEventEmitterTests</c>.</summary>
    private static IReadOnlyList<Type> ACommerceTypes()
    {
        var types = new List<Type>();
        foreach (var dll in Directory.EnumerateFiles(AppContext.BaseDirectory, "ACommerce*.dll"))
        {
            try { types.AddRange(Assembly.LoadFrom(dll).GetTypes()); }
            catch (ReflectionTypeLoadException ex) { types.AddRange(ex.Types.OfType<Type>()); }
            catch (BadImageFormatException) { }
        }
        return types.Where(t => !t.IsGenericTypeDefinition && !t.IsEnum && !t.Name.Contains('<') && (t.IsClass || t.IsValueType)).ToList();
    }

    private static readonly Regex StoredGeneric = new(
        @"\b(?:Schema\.For|Query|LoadAsync|LoadManyAsync|AggregateStreamAsync|FetchForWriting|FetchForExclusiveWriting|FetchLatest|StartStream|Delete|DeleteWhere|HardDelete)\s*<\s*(?<t>[A-Za-z_][\w.]*)\s*>",
        RegexOptions.Compiled);
    private static readonly Regex StoredNew = new(@"\.(?:Store|Insert|Update)\s*\(\s*new\s+(?<t>[A-Za-z_][\w.]*)\s*[({]", RegexOptions.Compiled);
    private static readonly Regex AppendStatement = new(@"\.Events\.(?:Append|StartStream)\b[^;]*", RegexOptions.Compiled);
    private static readonly Regex NewCall = new(@"new\s+(?<t>[A-Za-z_][\w.]*)\s*\(", RegexOptions.Compiled);

    /// <summary>أَسماءُ الأَنواعِ الَّتي يُخَزِّنُها سَطرٌ مِن الكود (بِلا تَعليقات) — كَما كُتِبَت، مُؤَهَّلَةً أَو لا.</summary>
    internal static IEnumerable<string> StoredTypeNames(string code)
    {
        foreach (Match m in StoredGeneric.Matches(code)) yield return m.Groups["t"].Value;
        foreach (Match m in StoredNew.Matches(code)) yield return m.Groups["t"].Value;
        foreach (Match m in AppendStatement.Matches(code))
            foreach (Match n in NewCall.Matches(m.Value)) yield return n.Groups["t"].Value;
    }

    /// <summary>(١) ما يُخَزَّنُ ويَبلُغُه — والجَذرُ مِن الكود، ثُمَّ أَحداثُ <c>Apply</c> وأَنواعُ الخَصائِصِ حَتّى لا جَديد.</summary>
    internal static (IReadOnlyList<Type> Roots, IReadOnlyList<Type> Closure, IReadOnlyList<(string Key, Type Type)> Names) StoredNameFields()
    {
        var byName = ACommerceTypes().GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var roots = new HashSet<Type>();
        foreach (var (_, text) in EntitlementContractTests.SourceFiles())
            foreach (var raw in StoredTypeNames(WriteEndpointGuardTests.StripComments(text)))
                if (byName.TryGetValue(raw.Split('.')[^1], out var hits))
                    foreach (var h in raw.Contains('.') ? hits.Where(h => (h.FullName ?? "").EndsWith(raw, StringComparison.Ordinal)) : hits)
                        roots.Add(h);

        var closure = new HashSet<Type>(roots);
        var queue = new Queue<Type>(roots);
        void Reach(Type t)
        {
            if (t.IsArray) { Reach(t.GetElementType()!); return; }
            if (t.IsGenericType) foreach (var a in t.GetGenericArguments()) Reach(a);
            if (t.IsEnum || t.IsPrimitive || t == typeof(string) || t.IsGenericType) return;
            if (!(t.Assembly.GetName().Name ?? "").StartsWith("ACommerce", StringComparison.Ordinal)) return;
            if (closure.Add(t)) queue.Enqueue(t);
        }
        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                if (m.Name == "Apply" && m.GetParameters() is { Length: 1 } ps) Reach(ps[0].ParameterType);
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) Reach(p.PropertyType);
        }

        var names = closure
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(string) && p.Name.EndsWith("Name", StringComparison.Ordinal))
                .Select(p => ($"{t.Name}.{p.Name}", t)))
            .OrderBy(x => x.Item1, StringComparer.Ordinal).ToList();
        return (roots.OrderBy(t => t.FullName, StringComparer.Ordinal).ToList(),
                closure.OrderBy(t => t.FullName, StringComparer.Ordinal).ToList(), names);
    }

    [Fact]
    public void Every_person_name_field_on_a_stored_type_is_classified_and_the_eraser_takes_no_copy()
    {
        var (roots, closure, names) = StoredNameFields();
        var found = names.Select(n => n.Key).ToList();
        var unclassified = found.Where(k => !PersonNameFields.ContainsKey(k)).ToList();
        var stale = PersonNameFields.Keys.Where(k => !found.Contains(k)).ToList();
        var copies = found.Where(k => PersonNameFields.TryGetValue(k, out var f) && f.Kind == NameKind.Copy).ToList();
        output.WriteLine($"جُذورٌ مُخَزَّنَة: {roots.Count} · ما تَبلُغُه: {closure.Count} نَوعاً · خَصائِصُ اسم: {found.Count} · نُسَخٌ لا يَمَسُّها الحَذف: {copies.Count}");
        foreach (var (key, _) in names)
            output.WriteLine($"  {key} — {(PersonNameFields.TryGetValue(key, out var f) ? $"{f.Kind}/{f.Said}" : "غَيرُ مُصَنَّف")}");

        Assert.True(roots.Count >= 30 && closure.Count >= roots.Count, $"أَداةٌ عَمياء: {roots.Count} جَذراً، {closure.Count} نَوعاً.");
        Assert.True(unclassified.Count == 0, $"خَصائِصُ اسمٍ بِلا تَصنيف: {string.Join(", ", unclassified)}");
        Assert.True(stale.Count == 0, $"تَصنيفٌ لا يَجِدُه المَسح: {string.Join(", ", stale)}");
        Assert.Equal(16, copies.Count);
        Assert.Equal(found.Count, found.Distinct(StringComparer.Ordinal).Count());

        // القائِمَةُ اليَدَوِيَّةُ (الصَفّ ٣) بَعضُ ما يَجِدُه المَسح — حَدٌّ أَدنى لا جَرد.
        Assert.All(NameCopies, c => Assert.Contains($"{c.Doc.Name}.{c.Field}", copies));

        // ولا يَمَسُّ الحَذفُ نَوعاً واحِداً يَحمِلُ نُسخَة.
        var body = DeleteEndpointBody();
        var copyTypes = names.Where(n => copies.Contains(n.Key)).Select(n => n.Type.Name).Distinct().ToList();
        Assert.Empty(copyTypes.Where(t => Regex.IsMatch(body, $@"\b{t}\b")));

        // و«لا كاتِبَ لَه» مَقيسٌ لا مَظنون: لا `new SubmitReport(` خارِجَ مِلَفِّ تَعريفِه.
        var reportEmitters = EntitlementContractTests.SourceFiles()
            .Where(f => !f.File.EndsWith("Report.cs", StringComparison.Ordinal))
            .Count(f => Regex.IsMatch(WriteEndpointGuardTests.StripComments(f.Text), @"new\s+(?:[\w.]+\.)?SubmitReport\s*\("));
        Assert.Equal(0, reportEmitters);
    }

    [Theory]
    [InlineData("var d = await s.LoadAsync<Deal>(id, ct);", "Deal")]
    [InlineData("var c = await s.Query<ACommerce.Kit.Chat.Conversation>().ToListAsync();", "ACommerce.Kit.Chat.Conversation")]
    [InlineData("s.Store(new ACommerce.Kit.Notifications.Notification\n{", "ACommerce.Kit.Notifications.Notification")]
    [InlineData("s.Events.Append(id, new TicketReplied(id, Guid.NewGuid(), n, true, b, at));", "TicketReplied")]
    [InlineData("opts.Schema.For<ACommerce.Kit.Auth.User>().Identity(x => x.Id);", "ACommerce.Kit.Auth.User")]
    [InlineData("s.Events.StartStream<ACommerce.Kit.Support.Ticket>(ev.Id, ev);", "ACommerce.Kit.Support.Ticket")]
    public void The_stored_type_patterns_see_what_they_count(string line, string expected)
        => Assert.Contains(expected, StoredTypeNames(line));

    // ─── ٣ج. مَن يَكتُبُ `Notification` وما يَحمِلُه ─────────────────────────────────────────────────────────

    internal sealed record NoticeWriter(string File, int Line, string Label, IReadOnlyList<string> Personal)
    {
        public string Key => $"{Label}|{string.Join(",", Personal)}";
    }

    private static readonly Regex PersonalToken = new(
        @"(?<![\w""])(?:[A-Za-z_]\w*\.)*(?<id>(?:[A-Za-z_]\w*)?(?:Name|Phone|Email|name|phone|email))\b", RegexOptions.Compiled);

    internal static IReadOnlyList<string> PersonalTokens(string expr)
        => PersonalToken.Matches(expr).Select(m => m.Groups["id"].Value).ToList();

    /// <summary>مِن <paramref name="open"/> (قَوسٌ فاتِح) إلى مُقابِلِه، بِمُوازَنَةِ ‏() و{} و[].</summary>
    private static int Closing(string code, int open)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] is '(' or '{' or '[') depth++;
            else if (code[i] is ')' or '}' or ']' && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>وَسائِطُ النِداءِ عَلى المُستَوى الأَعلى — الفاصِلَةُ داخِلَ قَوسٍ لا تَفصِل.</summary>
    private static List<string> TopLevelArgs(string inner)
    {
        var args = new List<string>();
        var depth = 0; var start = 0;
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] is '(' or '{' or '[') depth++;
            else if (inner[i] is ')' or '}' or ']') depth--;
            else if (inner[i] == ',' && depth == 0) { args.Add(inner[start..i].Trim()); start = i + 1; }
        }
        args.Add(inner[start..].Trim());
        return args;
    }

    /// <summary>(٢) كُتّابُ <c>Notification</c> في نَصٍّ واحِد: كُلُّ مُهَيِّئٍ سَطرُه <c>RelatedUrl =</c> (عُنوانُه ومَتنُه)، وكُلُّ نِداءٍ لِـ
    /// <c>NotifyAdminsAsync</c> (وَسيطاه الرابِعُ والخامِس) — والتَسمِيَةُ قيمَةُ <c>Type</c>.</summary>
    internal static IEnumerable<NoticeWriter> NoticeWriters(string file, string code)
    {
        int LineOf(int at) => code.Take(at).Count(c => c == '\n') + 1;
        foreach (Match m in Regex.Matches(code, @"(?m)^[ \t]*RelatedUrl\s*=(?!=)"))
        {
            var depth = 0; var open = -1;
            for (var i = m.Index; i >= 0 && open < 0; i--)
            {
                if (code[i] == '}') depth++;
                else if (code[i] == '{' && depth-- == 0) open = i;
            }
            if (open < 0) continue;
            var close = Closing(code, open);
            var block = close > open ? code[open..close] : code[open..];
            string Field(string f) => Regex.Match(block, $@"(?m)^\s*{f}\s*=\s*(?<e>.+?),?\s*$") is { Success: true } x ? x.Groups["e"].Value : "";
            yield return new NoticeWriter(file, LineOf(m.Index), Field("Type"), PersonalTokens(Field("Title") + " " + Field("Body")));
        }
        foreach (Match m in Regex.Matches(code, @"(?<!Task\s)\bNotifyAdminsAsync\s*\("))
        {
            var open = m.Index + m.Length - 1;
            var close = Closing(code, open);
            if (close < 0) continue;
            var args = TopLevelArgs(code[(open + 1)..close]);
            if (args.Count < 5) continue;
            yield return new NoticeWriter(file, LineOf(m.Index), args[2], PersonalTokens(args[3] + " " + args[4]));
        }
    }

    /// <summary>الكُتّابُ الَّذينَ يَضَعونَ اسماً أَو هاتِفاً أَو بَريداً في إشعارٍ مُخَزَّنٍ في صُندوقِ غَيرِ صاحِبِه — وما يَقولُه نَصُّ الحَذفِ عَنهُم.</summary>
    internal static readonly IReadOnlyDictionary<string, Disclosure> PersonalNoticeWriters = new Dictionary<string, Disclosure>(StringComparer.Ordinal)
    {
        ["AcceptedType|acceptorName"]    = Disclosure.Notifications,   // ‏DealNotices.AcceptedFor — لِلمُشتَري: قَبِلَ فُلان
        ["CancelledType|cancellerName"]  = Disclosure.Notifications,   // ‏DealNotices.CancelledFor — لِلمُشتَري: أَلغى فُلان
        ["\"offer_accepted\"|acceptorName"] = Disclosure.Notifications, // ‏لِلسائِق: قَبِلَ فُلانٌ عَرضَك
        ["\"driver_arrived\"|OffererName"]  = Disclosure.Notifications, // ‏لِلراكِب: وَصَلَ فُلان
        ["\"chat_message\"|senderName"]     = Disclosure.Notifications, // ‏لِلطَرَفِ الآخَر: رِسالَةٌ مِن فُلان
        ["\"new_user\"|FullName,Phone"]     = Disclosure.AdminNotices,  // ‏لِمُديري المَتجَر: سَجَّلَ فُلانٌ · هاتِفُه
        ["\"new_user\"|FullName,Email"]     = Disclosure.AdminNotices,  // ‏لِمُديري المَتجَر: سَجَّلَ فُلانٌ · بَريدُه
        ["\"report\"|userName"]             = Disclosure.AdminNotices,  // ‏لِمُديري المَتجَر: فُلانٌ بَلَّغَ عَن إعلان
    };

    internal static IReadOnlyList<NoticeWriter> AllNoticeWriters()
        => EntitlementContractTests.SourceFiles()
            .SelectMany(f => NoticeWriters(Path.GetFileName(f.File), WriteEndpointGuardTests.StripComments(f.Text)))
            .ToList();

    [Fact]
    public void Every_stored_notification_that_carries_a_name_a_phone_or_an_email_is_pinned()
    {
        var writers = AllNoticeWriters();
        var personal = writers.Where(w => w.Personal.Count > 0).ToList();
        output.WriteLine($"كُتّابُ Notification: {writers.Count} · يَحمِلُ اسماً أَو هاتِفاً أَو بَريداً: {personal.Count}");
        foreach (var w in writers) output.WriteLine($"  {w.File}:{w.Line} {w.Label} [{string.Join(", ", w.Personal)}]");

        Assert.True(writers.Count >= 10, $"أَداةٌ عَمياء: {writers.Count} كاتِباً لِلإشعار.");
        Assert.Equal(PersonalNoticeWriters.Keys.OrderBy(k => k, StringComparer.Ordinal),
                     personal.Select(w => w.Key).OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(2, personal.Count(w => w.Personal.Any(p => p.EndsWith("Phone", StringComparison.Ordinal) || p.EndsWith("Email", StringComparison.Ordinal))));
    }

    [Theory]
    [InlineData("new Notification\n{\n    Type = \"chat_message\",\n    Title = l.Format(\"notifications.chat_message.title\", senderName),\n    Body = conv.LastMessage ?? \"—\",\n    RelatedUrl = u,\n}", "\"chat_message\"|senderName")]
    [InlineData("await NotifyAdminsAsync(store, slug, \"new_user\",\n    \"t\",\n    $\"{user.FullName} · {user.Phone}\",\n    \"/u\");", "\"new_user\"|FullName,Phone")]
    [InlineData("await NotifyAdminsAsync(store, slug, \"x\", \"t\", $\"{name} · {phone}\", \"/u\");", "\"x\"|name,phone")]
    [InlineData("new()\n{\n    Type = NewDealType,\n    Title = l[\"notifications.deal_new.title\"],\n    Body = l.Format(\"notifications.deal_new.body\", deal.ListingTitle, deal.AmountSar),\n    RelatedUrl = u,\n};", "NewDealType|")]
    public void The_notice_writer_pattern_sees_what_it_counts(string code, string expected)
        => Assert.Equal(expected, Assert.Single(NoticeWriters("probe.cs", code)).Key);

    // ─── ٤. حِسابُ الاستوديو بِلا بابِ حَذف ───────────────────────────────────────────────────────────────

    [Fact]
    public void The_studio_account_holds_a_phone_an_email_and_a_name_and_no_studio_route_deletes_it()
    {
        var personal = new[] { "Phone", "Email", "FullName" }
            .Where(p => typeof(ACommerce.Templates.Customer.Marketplace.Services.Incubator.StudioUser).GetProperty(p) is not null).ToList();
        var studio = ComplianceInspectorTests.RepoRoutes.Where(r => r.StartsWith("/studio", StringComparison.Ordinal)).ToList();
        var deletes = studio.Where(r => Regex.IsMatch(r, "delete|erase|remove|close-account", RegexOptions.IgnoreCase)).ToList();
        output.WriteLine($"حُقولُ StudioUser الشَخصِيَّة: {string.Join(", ", personal)} · مَساراتُ /studio: {studio.Count} · بابُ حَذفٍ فيها: {deletes.Count} ({string.Join(", ", deletes)})");

        Assert.Equal(new[] { "Phone", "Email", "FullName" }, personal);
        Assert.True(studio.Count >= 20, $"أَداةٌ عَمياء: {studio.Count} مَساراً تَحتَ /studio.");
        Assert.Empty(deletes);
    }

    // ─── ٥. «اليَوم»: النُصوصُ تَعِدُ بِما لا يَقَع — يَنقَلِبُ بِاسمِه في كوميتِ الصِياغَة ─────────────────────

    internal const string StorePrivacyKey = "legal.privacy.deletion";
    internal const string PlatformRetentionKey = "platform.privacy.s6_body";
    internal const string PlatformRightsKey = "platform.privacy.s7_body";
    internal const string RetainedNoteKey = "account.delete.retained_note";

    private static string Ar(string key) => LocaleCatalog.Find(LocaleCatalog.Arabic, key)
        ?? throw new Xunit.Sdk.XunitException($"لا مِفتاحَ «{key}» في ar.json.");

    private static string En(string key) => LocaleCatalog.Find("en", key)
        ?? throw new Xunit.Sdk.XunitException($"لا مِفتاحَ «{key}» في en.json.");

    /// <summary>القيَمُ قَبلَ كوميتِ الصِياغَةِ بِحَرفِها (‏<c>b52ea8d</c> وما قَبلَه) — لِيُقاسَ أَنّ الفَرقَ مَحصورٌ في الجُملَةِ الكاذِبَة.</summary>
    internal static readonly IReadOnlyDictionary<string, string> Before = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [StorePrivacyKey] = "لِحَذف حِسابك بِالكامِل: صَفحَة «حِسابي» ثُمَّ «حَذفُ الحِساب». يَقَع الحَذف فَور تَأكيدك، ويَبقى سِجِلّ الصَّفَقات والفَواتير بِلا اسم لِالتِزام نِظاميّ ومُحاسَبيّ.",
        [PlatformRetentionKey] = "نَحتَفِظُ بِبَياناتِكَ ما دامَ حِسابُكَ قائِماً وما تَقتَضيهِ المُتَطَلَّباتُ النِظامِيَّةُ والمُحاسَبِيَّة. ويُمكِنُكَ حَذفُ حِسابِكَ وبَياناتِكَ الشَخصِيَّةَ مِن داخِلِ التَطبيقِ نَفسِه: صَفحَةُ «حِسابي» ثُمَّ «حَذفُ الحِساب»، ويَقَعُ الحَذفُ فَورَ تَأكيدِك. ويَبقى سِجِلُّ الصَفَقاتِ والفَواتيرِ بِلا اسمٍ ولا وَسيلَةِ اتِّصالٍ ما اقتَضاهُ التِزامٌ نِظامِيٌّ ومُحاسَبِيّ.",
        [RetainedNoteKey] = "يَبقى سِجِلُّ الصَفَقاتِ والفَواتيرِ مَنسوباً إلى حِسابٍ بِلا اسمٍ ولا وَسيلَةِ اتِّصال. والاحتِفاظُ بِه التِزامٌ نِظامِيٌّ ومُحاسَبِيّ: المادَّةُ الخامِسَةُ تُجيزُه صَراحَةً «دونَ إخلالٍ بِما يَقضي بِه نِظامٌ آخَر».",
    };

    /// <summary>صَدرُ كُلِّ قيمَةٍ قَبلَ أَوَّلِ تَبديلٍ مَقصود — يَبقى بِحَرفِه: في الخُصوصِيَّتَينِ إلى «والفَواتير»/«حِسابِكَ» (ثُمَّ «في المَتجَرِ»)،
    /// وفي شاشَةِ الحَذفِ إلى «والفَواتير» (ثُمَّ الكَسرَةُ الَّتي كانَت تَصِلُها بِـ«مَنسوباً»).</summary>
    internal static readonly IReadOnlyDictionary<string, string> Unchanged = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [StorePrivacyKey] = "لِحَذف حِسابك بِالكامِل: صَفحَة «حِسابي» ثُمَّ «حَذفُ الحِساب». يَقَع الحَذف فَور تَأكيدك، ويَبقى سِجِلّ الصَّفَقات والفَواتير ",
        [PlatformRetentionKey] = "نَحتَفِظُ بِبَياناتِكَ ما دامَ حِسابُكَ قائِماً وما تَقتَضيهِ المُتَطَلَّباتُ النِظامِيَّةُ والمُحاسَبِيَّة. ويُمكِنُكَ حَذفُ حِسابِكَ ",
        [RetainedNoteKey] = "يَبقى سِجِلُّ الصَفَقاتِ والفَواتير",
    };

    /// <summary>النَصُّ بِلا حَرَكات — فَالفاحِصُ يَقرَأُ الكَلِمَةَ لا تَشكيلَها (‏<c>LegalHub</c> بِتَشكيلٍ أَخَفَّ مِن صَفحَةِ المَنَصَّة).</summary>
    internal static string Bare(string s) => Regex.Replace(s, "[\u064B-\u0652\u0670]", "");

    /// <summary>ما يَقولُه كُلُّ نَصٍّ عَمّا يَبقى ومَن يَراه — بِالكَلِماتِ الَّتي تُقابِلُ وَقائِعَ الصُفوفِ ١–٣ (بِلا حَرَكات).</summary>
    internal static readonly string[] RetainedFacts =
        { "الاسم الذي كان لك وقتها", "عنوان التوصيل", "الهاتف", "بائع الصفقة", "صاحب المتجر الاسم", "شركة الشحن" };

    /// <summary><b>تَوصيف</b> (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-4 الجَولَة ١، صُغرى — القاعِدَة ٣: يَنقَلِبُ بِاسمِه في كوميتِ النَصّ): النُصوصُ الثَلاثَةُ
    /// تَقولُ إنّ الصَفقَةَ تُبقي «الاسمَ الَّذي كانَ لَكَ وَقتَها»، وصَفقَةُ إتمامِ الشِراءِ تَكتُبُ في <c>InitiatorName</c> الاسمَ الَّذي كَتَبَه المُشتَري
    /// في استِمارَةِ الطَلَبِ (<c>req.Form["name"]</c>) لا اسمَ حِسابِه — والعُنوانُ والهاتِفُ مِن الاستِمارَةِ نَفسِها يُقالُ فيهِما «كَتَبتَهُما في الطَلَب».</summary>
    [Fact]
    public void Today_the_deletion_texts_call_the_deal_name_the_one_you_had_while_checkout_keeps_the_name_typed_in_the_order()
    {
        var mte = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs")));
        var submit = mte.IndexOf("\"/{slug}/checkout/submit\"", StringComparison.Ordinal);
        var typed = mte.IndexOf("var name  = req.Form[\"name\"].ToString().Trim();", submit, StringComparison.Ordinal);
        var kept = mte.IndexOf("initiatorId: userId, initiatorName: name,", submit, StringComparison.Ordinal);
        var texts = new[] { StorePrivacyKey, PlatformRetentionKey, RetainedNoteKey }.ToDictionary(k => k, k => Bare(Ar(k)));
        output.WriteLine($"إتمامُ الشِراء: الاسمُ مِن الاستِمارَة {typed > submit} · يُكتَبُ InitiatorName {kept > typed} · " +
                         string.Join(" · ", texts.Select(t => $"{t.Key}: «كان لك وقتها» {t.Value.Contains("الاسم الذي كان لك وقتها", StringComparison.Ordinal)}، «كتبته في الطلب» {t.Value.Contains("كتبته في الطلب", StringComparison.Ordinal)}")));
        Assert.True(submit > 0 && typed > submit && kept > typed);
        foreach (var (k, t) in texts)
        {
            Assert.Contains("الاسم الذي كان لك وقتها", t, StringComparison.Ordinal);
            Assert.DoesNotContain("كتبته في الطلب", t, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("or entered in the order", En(PlatformRetentionKey), StringComparison.Ordinal);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ الصِياغَة — الخِيارُ (ب) مِن brief-P3-5a)</b> — كانَ
    /// <c>Today_three_deletion_texts_promise_deal_records_without_a_name_or_contact_and_the_policy_promises_every_account_an_in_app_deletion</c>
    /// (‏<c>b52ea8d</c>): ثَلاثَةُ نُصوصٍ تَقولُ «بِلا اسم»، وs6/s7 لا تُمَيِّزانِ حِسابَ الاستوديو. <b>والآن</b> يَقولُ كُلٌّ مِنها ما تَبقيه الصَفقَةُ
    /// ومَن يَراه — وهو بِعَينِه ما تَقيسُه الصُفوفُ ١–٣ مِن الكود (فَإن وَقَعَ الخِيارُ (أ) أَحمَرَت تِلكَ وكُتِبَ النَصُّ مَعَها) — والفَرقُ
    /// مَحصورٌ: صَدرُ كُلِّ قيمَةٍ إلى الجُملَةِ الكاذِبَةِ بِحَرفِه. والصِياغَةُ مِن كِتابَتي حَتّى يَكتُبَها المالِكُ («ب» W8-4 · ١ في سِجِلِّه).</summary>
    [Fact]
    public void Deletion_texts_say_what_each_deal_keeps_and_who_sees_it_and_tell_the_studio_account_apart()
    {
        var keys = new[] { StorePrivacyKey, PlatformRetentionKey, RetainedNoteKey };
        foreach (var k in keys)
        {
            var now = Ar(k);
            var was = Before[k];
            var shared = 0;
            while (shared < now.Length && shared < was.Length && now[shared] == was[shared]) shared++;
            output.WriteLine($"{k}: قَبلُ {was.Length} حَرفاً · بَعدُ {now.Length} · الصَدرُ المُشتَرَك {shared} :: {now}");

            Assert.DoesNotContain("بلا اسم", Bare(now), StringComparison.Ordinal);
            Assert.DoesNotContain("وسيلة اتصال", Bare(now), StringComparison.Ordinal);
            foreach (var fact in RetainedFacts)
                Assert.True(Bare(now).Contains(fact, StringComparison.Ordinal), $"{k} لا يَقولُ «{fact}»: {now}");
            // الفَرقُ مَحصور: صَدرُ القيمَةِ القَديمَةِ إلى أَوَّلِ تَبديلٍ مَقصودٍ بِحَرفِه في الجَديدَة.
            Assert.StartsWith(Unchanged[k], was, StringComparison.Ordinal);
            Assert.StartsWith(Unchanged[k], now, StringComparison.Ordinal);
            Assert.True(shared >= Unchanged[k].Length, $"{k}: الفَرقُ يَبدَأُ عِندَ {shared} قَبلَ الصَدرِ المُعلَن ({Unchanged[k].Length}).");
        }
        Assert.DoesNotContain("without a name", En(PlatformRetentionKey), StringComparison.Ordinal);
        Assert.Contains("the deal's seller sees them, the store owner sees the name", En(PlatformRetentionKey), StringComparison.Ordinal);

        // وحِسابُ الاستوديو يُقالُ بِاسمِه: لا بابَ لِحَذفِه — وهو ما يَقيسُه الصَفُّ ٤.
        Assert.Contains("أَمّا حِسابُ الاستوديو (لِأَصحابِ المَتاجِر) فَلا بابَ لِحَذفِه داخِلَ التَطبيقِ اليَوم.", Ar(PlatformRetentionKey), StringComparison.Ordinal);
        Assert.Contains("A studio account (for store owners) has no deletion door inside the app today.", En(PlatformRetentionKey), StringComparison.Ordinal);
        Assert.Contains("وحَذفُ حِسابِ المَتجَرِ يَقَعُ مِن داخِلِ التَطبيق", Ar(PlatformRightsKey), StringComparison.Ordinal);
        Assert.Contains("Store account deletion happens inside the app", En(PlatformRightsKey), StringComparison.Ordinal);
    }
    // ─── ٥ب. «اليَوم»: ما يَجِدُه المَسحانِ ولا تَقولُه النُصوص — يَنقَلِبُ بِاسمِه في كوميتِ الصِياغَة ──────────

    /// <summary>عِباراتُ كُلِّ إفصاحٍ بِلا حَرَكات — يَقولُها كُلٌّ مِن نُصوصِ الحَذفِ الثَلاثَة.</summary>
    internal static readonly IReadOnlyDictionary<Disclosure, string[]> DisclosurePhrases = new Dictionary<Disclosure, string[]>
    {
        [Disclosure.Deal]          = new[] { "وفي كل صفقة" },
        [Disclosure.Records]       = new[] { "محادثاتك وتقييماتك وعروضك وتذاكر دعمك وبلاغاتك" },
        [Disclosure.Notifications] = new[] { "إشعارات من تعاملت معهم" },
        [Disclosure.AdminNotices]  = new[] { "إشعارات مديري المتجر", "هاتفك أو بريدك" },
        [Disclosure.StoreRecords]  = new[] { "سجل التدقيق ومفاتيح الربط" },
    };

    /// <summary>كُلُّ إفصاحٍ يَحتاجُه ما يَجِدُه المَسحان — مِن خَصائِصِ الاسمِ ومِن كُتّابِ الإشعار.</summary>
    internal static IReadOnlyList<Disclosure> NeededDisclosures()
        => PersonNameFields.Values.Where(f => f.Kind == NameKind.Copy).Select(f => f.Said)
            .Concat(PersonalNoticeWriters.Values).Distinct().OrderBy(d => d).ToList();

    internal static IReadOnlyList<Disclosure> Unsaid(string text)
        => NeededDisclosures().Where(d => !DisclosurePhrases[d].All(p => Bare(text).Contains(p, StringComparison.Ordinal))).ToList();

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ الصِياغَة — مُراجَعَةُ تَيّارِ W8-4)</b> — كانَ
    /// <c>Today_the_deletion_texts_leave_unsaid_tickets_notifications_and_store_records_and_the_store_privacy_omits_conversations</c> (‏<c>e262f90</c>):
    /// النُصوصُ الثَلاثَةُ تَقولُ الصَفقَةَ وَحدَها مِن خَمسَةِ إفصاحاتٍ يَحتاجُها المَسحان. <b>والآن</b> يَقولُ كُلٌّ مِنها الخَمسَة — وإفصاحٌ سادِسٌ
    /// يَحتاجُه تَصنيفٌ جَديدٌ في <see cref="PersonNameFields"/> أَو <see cref="PersonalNoticeWriters"/> يُحمِرُّ هذا الصَفَّ حَتّى يُكتَبَ في النُصوص.
    /// والصِياغَةُ مِن كِتابَتي حَتّى يَكتُبَها المالِكُ أَو مُحاميه («ب» W8-4 · ١).</summary>
    [Fact]
    public void Every_copy_the_scans_find_is_said_by_each_deletion_text()
    {
        var needed = NeededDisclosures();
        output.WriteLine($"إفصاحاتٌ يَحتاجُها المَسحان: {string.Join(", ", needed)}");
        Assert.Equal(new[] { Disclosure.Deal, Disclosure.Records, Disclosure.Notifications, Disclosure.AdminNotices, Disclosure.StoreRecords }, needed);
        foreach (var k in new[] { StorePrivacyKey, PlatformRetentionKey, RetainedNoteKey })
        {
            var unsaid = Unsaid(Ar(k));
            output.WriteLine($"{k}: لا يَقولُ {unsaid.Count} :: {Ar(k)}");
            Assert.Empty(unsaid);
        }
        // والإنجليزيُّ لِـs6 يَقولُ ما يَقولُه العَرَبِيّ (en.json لِصَفَحاتِ المَنَصَّةِ وَحدَها — لا مُقابِلَ لِنَصَّيِ المَتجَر).
        foreach (var phrase in new[] { "each deal keeps", "conversations, reviews, offers, support tickets and reports",
                                       "the notifications of the people you dealt with", "the store managers' notifications",
                                       "phone number or email", "the audit log and the integration keys" })
            Assert.Contains(phrase, En(PlatformRetentionKey), StringComparison.Ordinal);
        Assert.Null(LocaleCatalog.Find("en", StorePrivacyKey));
        Assert.Null(LocaleCatalog.Find("en", RetainedNoteKey));
    }
}
