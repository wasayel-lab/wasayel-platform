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

    [Fact]
    public void Today_three_deletion_texts_promise_deal_records_without_a_name_or_contact_and_the_policy_promises_every_account_an_in_app_deletion()
    {
        var keys = new[] { StorePrivacyKey, PlatformRetentionKey, RetainedNoteKey };
        foreach (var k in keys) output.WriteLine($"{k}: {Ar(k)}");
        output.WriteLine($"en {PlatformRetentionKey}: {En(PlatformRetentionKey)}");

        Assert.Contains("بِلا اسم لِالتِزام", Ar(StorePrivacyKey), StringComparison.Ordinal);
        Assert.Contains("بِلا اسمٍ ولا وَسيلَةِ اتِّصال", Ar(PlatformRetentionKey), StringComparison.Ordinal);
        Assert.Contains("بِلا اسمٍ ولا وَسيلَةِ اتِّصال", Ar(RetainedNoteKey), StringComparison.Ordinal);
        Assert.Contains("without a name or contact details", En(PlatformRetentionKey), StringComparison.Ordinal);

        // والسِياسَةُ لا تُمَيِّزُ حِسابَ المَتجَرِ مِن حِسابِ الاستوديو.
        Assert.DoesNotContain("الاستوديو", Ar(PlatformRetentionKey), StringComparison.Ordinal);
        Assert.Contains("وحَذفُ الحِسابِ يَقَعُ مِن داخِلِ التَطبيق", Ar(PlatformRightsKey), StringComparison.Ordinal);
        Assert.Contains("Account deletion happens inside the app", En(PlatformRightsKey), StringComparison.Ordinal);
    }
}
