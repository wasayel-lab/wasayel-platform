using System.Reflection;
using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Delivery;
using ACommerce.Kit.Offers;
using ACommerce.Kit.Reviews;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
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
//
// **‏2026-10-29 — وَقَعَ الخِيارُ (أ) (قَرارُ المالِك د52 (١) «أ»؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق)**: «يُمَدُّ الحَذفُ إلى ما يُمكِنُ تَعديلُه». فَقُلِبَت
// بِأَسمائِها الصُفوفُ ١ و٣ و٣ب و٣ج وصُفوفُ النُصوص — النُسَخُ تُمحى في المُعامَلَة (`AccountErasure`)، وما في سِجِلٍّ لا يُعَدَّلُ يُقال — والصَفُّ ٢
// (الصَفقَةُ الحَيَّةُ تَحمِلُ الاسمَ والهاتِفَ والعُنوانَ ويَقرَؤُها ثَلاثَة) والصَفُّ ٤ (حِسابُ الاستوديو بِلا باب) بَقِيا بِحَرفِهِما: ما زالا يَصدُقان.
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
    //
    // ‏(‏2026-10-29، قَرارُ المالِك د52 (١) «أ» — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): ما عادَ كَذلك. الصَفُّ أَدناه قُلِبَ بِاسمِه: النُقطَةُ تُسَمّي وَثيقَةَ
    // الحِسابِ بِيَدِها كَما كانَت، ونُسَخُ الاسمِ يَمحوها `AccountErasure.EraseCopiesAsync` في الجَلسَةِ نَفسِها قَبلَ الحِفظِ الواحِد.

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-29، د52)</b> — كانَ <c>Deletion_writes_the_user_document_alone_and_nothing_else_reaches_a_deal</c>: «‏Store(…) في النُقطَة: 1
    /// (user!) · Patch: 0 · ذِكرُ الصَفقَة: 0». والآن الوَثيقَةُ الوَحيدَةُ الَّتي تُسَمّيها النُقطَةُ بِيَدِها ما زالَت <c>user!</c>، ويَمحو <c>AccountErasure</c>
    /// النُسَخَ في الجَلسَةِ نَفسِها — بَعدَ <c>Erase</c> (فَالاسمُ القَديمُ مَحفوظٌ قَبلَه) وقَبلَ الحِفظِ الواحِد — ولا يَعرِفُ الحَذفَ مِلَفٌّ في خِدماتِ الصَفَقات.</summary>
    [Fact]
    public void Deletion_erases_the_copies_in_the_same_save_and_still_stores_the_user_document_alone_by_hand()
    {
        var body = DeleteEndpointBody();
        var stores = Regex.Matches(body, @"\.Store\((?<arg>[^)]*)\)").Select(m => m.Groups["arg"].Value.Trim()).ToList();
        var patches = Regex.Matches(body, @"\.Patch<").Count;
        // ‏(2026-10-29، قَرارُ المالِك د40) الأَسماءُ قَبلَ Erase لا الاسمُ وَحدَه — اسمُ النَشاطِ صارَ يُكتَبُ في نُسَخٍ عِندَ غَيرِه؛ كانَ `= user!.FullName;`.
        var before = body.IndexOf("var nameBefore = Services.Queries.ActorName.NamesOf(user!);", StringComparison.Ordinal);
        var erase = body.IndexOf("AccountDeletion.Erase(user!, now)", StringComparison.Ordinal);
        // ‏(2026-10-29، مُراجَعَةُ تَيّارِ S2 الجَولَة ١) ووَسيلَتا اتِّصالِه قَبلَ Erase — إشعارُ التَسجيلِ القَديمِ يَحمِلُهُما؛ كانَ `(session, slug, userId, nameBefore)`.
        var copies = body.IndexOf("AccountErasure.EraseCopiesAsync(session, slug, userId, nameBefore, contactsBefore)", StringComparison.Ordinal);
        var contactsAt = body.IndexOf("var contactsBefore = Services.Identity.AccountErasure.ContactsOf(user!);", StringComparison.Ordinal);
        var save = body.IndexOf("SaveChangesAsync", StringComparison.Ordinal);

        var erasers = EntitlementContractTests.SourceFiles()
            .Where(f => WriteEndpointGuardTests.StripComments(f.Text).Contains("AccountDeletion.Erase(", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f.File)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var copyErasers = EntitlementContractTests.SourceFiles()
            .Where(f => WriteEndpointGuardTests.StripComments(f.Text).Contains("AccountErasure.EraseCopiesAsync(", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f.File)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var dealsDir = Path.Combine(RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Deals");
        var dealFiles = Directory.GetFiles(dealsDir, "*.cs", SearchOption.AllDirectories);
        var deletionAware = dealFiles
            .Where(f => Regex.IsMatch(WriteEndpointGuardTests.StripComments(File.ReadAllText(f)), @"DeletedAt|AccountDeletion"))
            .Select(Path.GetFileName).ToList();

        output.WriteLine($"Store(…) في النُقطَة: {stores.Count} ({string.Join(", ", stores)}) · Patch: {patches} · الاسمُ قَبلَ Erase ثُمَّ النُسَخُ ثُمَّ الحِفظ: {before < erase && erase < copies && copies < save}" +
                         $" · مَن يُنادي Erase: {string.Join(", ", erasers)} · مَن يَمحو النُسَخ: {string.Join(", ", copyErasers)} · مِلَفّاتُ الصَفَقاتِ تَعرِفُ الحَذف: {deletionAware.Count}/{dealFiles.Length}");

        Assert.Equal(new[] { "user!" }, stores);
        Assert.Equal(0, patches);
        Assert.True(before > 0 && erase > before && copies > erase && save > copies, "التَرتيب: الاسمُ القَديمُ ثُمَّ Erase ثُمَّ مَحوُ النُسَخِ ثُمَّ الحِفظُ الواحِد.");
        Assert.True(contactsAt > before && erase > contactsAt, "وَسيلَتا الاتِّصالِ تُؤخَذانِ قَبلَ Erase — هِيَ تُفرِغُهُما.");
        Assert.Equal(1, Regex.Matches(body, "SaveChangesAsync").Count);
        // ‏‏2026-10-29 (د57، ‏ADR-044 §٤): مُستَدعٍ ثانٍ بِقَرار — الدَمجُ يَمحو الحِسابَ المُندَمِجَ بِـ`Erase` بِعَينِها بَعدَ أَن يَنقُلَ صَفَقاتِه
        // وما يَحمِلُ مُعَرِّفَه إلى الباقي (`DuplicatePhones.ApplyAsync`) — فَلا يَمحو نُسَخَه (صارَت لِلباقي).
        Assert.Equal(new[] { "DuplicatePhones.cs", "MarketplaceTemplateExtensions.cs" }, erasers);
        Assert.Equal(new[] { "MarketplaceTemplateExtensions.cs" }, copyErasers);
        Assert.True(dealFiles.Length >= 10, $"أَداةٌ عَمياء: {dealFiles.Length} مِلَفّاً في خِدماتِ الصَفَقات.");
        Assert.Empty(deletionAware);

        var erasure = typeof(AccountDeletion).GetMethod(nameof(AccountDeletion.Erase))!;
        Assert.Equal(new[] { typeof(User), typeof(DateTime) }, erasure.GetParameters().Select(p => p.ParameterType));
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

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-29، د52 (١) «أ»)</b> — كانَ <c>Copies_of_the_name_live_on_deals_conversations_reviews_and_offers_and_the_eraser_takes_none_of_them</c>
    /// («يَمَسُّها الحَذف: 0»). والآن كُلُّ نُسخَةٍ مِن السَبعِ يَمحوها <see cref="AccountErasure"/> — لِصاحِبِها وَحدَه، والطَرَفُ الآخَرُ بِاسمِه.</summary>
    [Fact]
    public void Copies_of_the_name_live_on_deals_conversations_reviews_and_offers_and_the_eraser_takes_every_one_of_them()
    {
        foreach (var (doc, field) in NameCopies)
            Assert.True(doc.GetProperty(field, BindingFlags.Public | BindingFlags.Instance)?.PropertyType is { } t
                        && (t == typeof(string)),
                $"{doc.Name}.{field} لَيسَ حَقلَ نَصٍّ — الجَردُ تَغَيَّر.");

        var me = Guid.NewGuid();
        var other = Guid.NewGuid();
        var deal = new Deal
        {
            InitiatorId = me, InitiatorName = "أَنا", CounterpartyId = other, CounterpartyName = "الآخَر",
            Timeline = new() { new DealEvent(DealStage.Offered, DealStage.Offered, "created", me, "أَنا", null, DateTime.UtcNow),
                               new DealEvent(DealStage.Offered, DealStage.Booked, "advanced", other, "الآخَر", null, DateTime.UtcNow) },
        };
        var conv = new Conversation { OwnerId = me, OwnerName = "أَنا", PartnerId = other, PartnerName = "الآخَر" };
        var written = new Review { AuthorId = me, AuthorName = "أَنا", TargetUserId = other, TargetUserName = "الآخَر" };
        var received = new Review { AuthorId = other, AuthorName = "الآخَر", TargetUserId = me, TargetUserName = "أَنا" };
        var offer = new Offer { OffererId = me, OffererName = "أَنا" };
        var erased = new[] { AccountErasure.EraseFrom(deal, me), AccountErasure.EraseFrom(conv, me), AccountErasure.EraseFrom(written, me),
                             AccountErasure.EraseFrom(received, me), AccountErasure.EraseFrom(offer, me) };
        var x = AccountDeletion.ErasedName;
        output.WriteLine($"نُسَخُ الاسمِ المَجرودَة: {NameCopies.Length} ({string.Join(", ", NameCopies.Select(c => $"{c.Doc.Name}.{c.Field}"))}) · مَحاها الحَذف: {erased.Count(e => e)}/{erased.Length}");
        Assert.True(NameCopies.Length >= 5, $"أَداةٌ عَمياء: {NameCopies.Length} نُسخَة.");
        Assert.All(erased, Assert.True);
        Assert.Equal((x, "الآخَر"), (deal.InitiatorName, deal.CounterpartyName));
        Assert.Equal(new[] { x, "الآخَر" }, deal.Timeline.Select(e => e.ActorName));
        Assert.Equal((x, "الآخَر"), (conv.OwnerName, conv.PartnerName));
        Assert.Equal((x, "الآخَر"), (written.AuthorName, written.TargetUserName));
        Assert.Equal(("الآخَر", x), (received.AuthorName, received.TargetUserName));
        Assert.Equal(x, offer.OffererName);
        // ومَرَّةً ثانِيَة لا تُغَيِّرُ شَيئاً — فَلا تُعادُ كِتابَةُ وَثيقَةٍ بِلا سَبَب.
        Assert.False(AccountErasure.EraseFrom(conv, me));
        Assert.False(AccountErasure.EraseFrom(new Conversation { OwnerId = other, PartnerId = Guid.NewGuid(), OwnerName = "غَيري" }, me));
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

    /// <summary>ما هي خاصِّيَّةُ الاسمِ لِلحَذف — مَعجَمٌ مُغلَق.
    /// <para><b>‏2026-10-29 (قَرارُ المالِك د52 (١) «أ»؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه)</b>: خَرَجَ <c>Copy</c> («نُسخَةٌ لا يَمَسُّها الحَذف») ولا صِنفَ يَبقى
    /// عَلَيه — والمَعجَمُ لا يَحمِلُ قيمَةً بِلا مُستَهلِك (القاعِدَة ١). وحَلَّ مَحَلَّه ثَلاثٌ بِما يَحدُثُ لِكُلِّ نُسخَة: تُمحى في المُعامَلَة (<see cref="Renamed"/>)،
    /// أَو تَبقى في سِجِلٍّ لا يُعَدَّلُ بِطَبيعَتِه (<see cref="EventLog"/>، <see cref="AuditLog"/>) فَيَقولُها نَصُّ الحَذف.</para></summary>
    internal enum NameKind
    {
        /// <summary>‏(د52) نُسخَةٌ في وَثيقَةٍ تُعَدَّل — يَكتُبُ فَوقَها <c>AccountErasure</c> «حِسابٌ مَحذوف» في مُعامَلَةِ الحَذفِ نَفسِها.</summary>
        Renamed,
        /// <summary>‏(د52) في حَدَثٍ مِن مَجرى الأَحداث — سِجِلٌّ لا يُعادُ كَتبُه ولا تَعرِضُه شاشَة (الشاشاتُ تَقرَأُ اللَقطَةَ المَمحُوَّة).</summary>
        EventLog,
        /// <summary>‏(د52) في سِجِلِّ التَدقيق — تاريخٌ لا يُعادُ كَتبُه، ويُخفى الاسمُ عِندَ عَرضِه (<c>AuditLogQueries.HideErasedActors</c>).</summary>
        AuditLog,
        /// <summary>الاسمُ في الوَثيقَةِ الَّتي يَمحوها <c>AccountDeletion.Erase</c> نَفسُها.</summary>
        Erased,
        /// <summary>حِسابُ الاستوديو — لا بابَ لِحَذفِه، ويَقولُ النَصُّ ذلِكَ بِاسمِه (الصَفّ ٤).</summary>
        StudioAccount,
        /// <summary>يُخَزَّنُ إن كُتِب، ولا كاتِبَ لَه اليَوم — يُفحَصُ الغِياب.</summary>
        Unwritten,
        /// <summary>اسمُ شَيءٍ لا شَخص.</summary>
        NotAPerson,
        /// <summary>‏(2026-10-28، قَرارُ المالِك د60) في وَثيقَةٍ تَحذِفُها نُقطَةُ الحَذفِ نَفسُها في مُعامَلَتِها — سَلَّةُ المُشتَري بِما كَتَبَه في خُطُواتِ
        /// الإتمام (<c>Cart.Checkout</c>). لا تَبقى بَعدَ الحَذفِ فَلا تَحتاجُ إفصاحاً؛ وحَذفُها مَقيسٌ في <c>CheckoutDetailsInUrlTests</c>.</summary>
        DeletedWithAccount,
        // ‏2026-10-28 (قَرارُ المالِك د56): كانَ هُنا `LiveCredential` (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-4 الجَولَة ٢) — «نُسخَةٌ لا يَمَسُّها الحَذفُ
        // وتَعمَل»، وصِنفُها الوَحيدُ `ApiKeyDocument.ActorName`. والحَذفُ صارَ يُبطِلُ المِفتاح، فَصارَ الاسمُ نُسخَةً ساكِنَةً (`Copy`) ولا صِنفَ
        // يَبقى عَلَيه — والمَعجَمُ لا يَحمِلُ قيمَةً بِلا مُستَهلِك (القاعِدَة ١). ويَومَ يُوجَدُ اعتِمادٌ حَيٌّ آخَرُ بِاسمِ شَخصٍ يَعودُ بِصَفِّه.
        // ‏2026-10-29 (د52): ثُمَّ صارَ `Renamed` — الإبطالُ نَفسُه يَكتُبُ «حِسابٌ مَحذوف» عَلى الوَثيقَة (`ApiKeyService.RevokeForDeletedActor`).
    }

    /// <summary>ما يَبقى بَعدَ الحَذفِ ويَحتاجُ إفصاحاً: ما في سِجِلٍّ لا يُعَدَّل — صَفُّ العَدِّ وقارِئُ الإفصاحِ يَسأَلانِ هذا وَحدَه.</summary>
    internal static bool KeptAfterDeletion(NameKind kind) => kind is NameKind.EventLog or NameKind.AuditLog;

    /// <summary>العِبارَةُ الَّتي يَقولُها نَصُّ الحَذفِ عَن كُلِّ أَثَر — مَعجَمٌ مُغلَق، ولِكُلٍّ عِباراتُه في <see cref="DisclosurePhrases"/>.
    /// <para><b>‏2026-10-29 (د52)</b>: كانَت تَقولُ ما يَبقى (‏<c>Deal</c>/<c>DealSeller</c>/<c>Records</c>/<c>Notifications</c>/<c>AdminNotices</c>/<c>StoreRecords</c>)،
    /// وصارَت تَقولُ ما يُمحى وما يَبقى ولِماذا: الاسمُ المَمحُوُّ في السِجِلّاتِ وفي الإشعارات، والعُنوانُ والهاتِفُ المَمحُوّانِ مِن الطَلَبات،
    /// وسِجِلُّ الأَحداث، وسِجِلُّ التَدقيق، وما وَصَلَ قَبلَ الحَذفِ إلى الناقِلِ وأَجهِزَةِ الآخَرين، والصَفقَةُ الجارِيَةُ الَّتي تَمنَعُه.</para></summary>
    internal enum Disclosure { None, Renamed, NoticesRenamed, AddressErased, EventLog, AuditLog, Delivered, OpenDeal }

    internal sealed record NameField(NameKind Kind, Disclosure Said, string WhyAr);

    /// <summary>كُلُّ خاصِّيَّةِ اسمٍ يَجِدُها <see cref="StoredNameFields"/> — بِتَصنيفِها وسَبَبِه. يُعَدَّلُ في الكوميتِ الَّذي يُغَيِّرُ ما يُخَزَّن.</summary>
    internal static readonly IReadOnlyDictionary<string, NameField> PersonNameFields = new Dictionary<string, NameField>(StringComparer.Ordinal)
    {
        ["Deal.InitiatorName"]           = new(NameKind.Renamed, Disclosure.Renamed, "اسمُ المُشتَري كَما كُتِبَ في الطَلَب (مِن الاستِمارَةِ في السَلَّة، ومِن الحِسابِ في العَرض) — يُمحى بِحَذفِ حِسابِه."),
        ["Deal.CounterpartyName"]        = new(NameKind.Renamed, Disclosure.Renamed, "اسمُ البائِعِ يَومَ رُبِطَ أَو قَبِلَ — يُمحى بِحَذفِ حِسابِه."),
        ["DealEvent.ActorName"]          = new(NameKind.Renamed, Disclosure.Renamed, "سَطرُ الخَطِّ الزَمَنيِّ لِكُلِّ فِعلٍ بِاسمِ فاعِلِه — يُمحى ما فَعَلَه هُو."),
        ["Conversation.OwnerName"]       = new(NameKind.Renamed, Disclosure.Renamed, "طَرَفا المُحادَثَةِ بِاسمَيهِما — يُمحى اسمُه وَحدَه."),
        ["Conversation.PartnerName"]     = new(NameKind.Renamed, Disclosure.Renamed, "طَرَفا المُحادَثَةِ بِاسمَيهِما — يُمحى اسمُه وَحدَه."),
        ["Review.AuthorName"]            = new(NameKind.Renamed, Disclosure.Renamed, "كاتِبُ التَقييم."),
        ["Review.TargetUserName"]        = new(NameKind.Renamed, Disclosure.Renamed, "مَن قُيِّمَ — اسمُه عَلى تَقييمِ غَيرِه لَه."),
        ["Offer.OffererName"]            = new(NameKind.Renamed, Disclosure.Renamed, "مُقَدِّمُ العَرض — لَقطَةُ مَجرى العَرضِ (تُعَدَّل)."),
        ["OfferSubmitted.OffererName"]   = new(NameKind.EventLog, Disclosure.EventLog, "حَدَثُ تَقديمِ العَرضِ في مَجرى الأَحداث — لا يُعادُ كَتبُه ولا تَعرِضُه شاشَة."),
        // ‏(2026-10-29، مُراجَعَةُ تَيّارِ S2 الجَولَة ٢) حَدَثُ المَحوِ نَفسُه — مُصدِرُه الوَحيدُ `AccountErasure.EraseEvents` بِقيمَةِ «حِسابٌ مَحذوف»، فَلا يَحمِلُ اسمَ أَحَد.
        ["OffererRenamed.OffererName"]   = new(NameKind.NotAPerson, Disclosure.None, "حَدَثُ مَحوِ اسمِ مُقَدِّمِ العَرضِ في مَجراه — قيمَتُه «حِسابٌ مَحذوف» يَكتُبُها الحَذف."),
        ["ListingMatch.OffererName"]     = new(NameKind.Renamed, Disclosure.Renamed, "صاحِبُ العَرضِ المَقبولِ عَلى مُطابَقَةِ الإعلان (ويُرسَلُ في إشعارِ «وَصَلَ السائِق»)."),
        ["Ticket.AuthorName"]            = new(NameKind.Renamed, Disclosure.Renamed, "كاتِبُ تَذكِرَةِ الدَعم — والبَلاغُ عَن إعلانٍ تَذكِرَةٌ بِاسمِه؛ لَقطَةُ مَجرى (تُعَدَّل)."),
        ["TicketCreated.AuthorName"]     = new(NameKind.EventLog, Disclosure.EventLog, "حَدَثُ فَتحِ التَذكِرَةِ في مَجرى الأَحداث."),
        ["Reply.AuthorName"]             = new(NameKind.Renamed, Disclosure.Renamed, "كاتِبُ الرَدّ — رَدُّ صاحِبِ التَذكِرَةِ (`FromStaff = false`) يُمحى، ورَدُّ الفَريقِ بِتَوقيعِه."),
        ["TicketReplied.AuthorName"]     = new(NameKind.EventLog, Disclosure.EventLog, "حَدَثُ الرَدِّ في مَجرى الأَحداث — كَسابِقِه."),
        ["AuditEntry.ActorName"]         = new(NameKind.AuditLog, Disclosure.AuditLog, "سِجِلُّ تَدقيقِ المَتجَر: البائِعُ عِندَ رَبطِ الشَحن، وفَريقُ الإدارَةِ عِندَ تَغييرِ الإعدادات — يُخفى عِندَ العَرض."),
        ["ApiKeyDocument.ActorName"]     = new(NameKind.Renamed, Disclosure.Renamed, "الفاعِلُ الَّذي يُصدِرُ لَه صاحِبُ المَتجَرِ مِفتاحَ رَبط — يُبطَلُ المِفتاحُ بِحَذفِ حِسابِه (د56) ويُكتَبُ «حِسابٌ مَحذوف» عَلَيه (د52)."),
        ["CartCheckout.RecipientName"]   = new(NameKind.DeletedWithAccount, Disclosure.None, "اسمُ المُستَلِمِ كَما كُتِبَ في خُطوَةِ العُنوان — في السَلَّةِ الَّتي تَحذِفُها نُقطَةُ الحَذف (د60)."),
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

    /// <summary>النَوعُ الَّذي يَمحوه <see cref="AccountErasure"/> لِكُلِّ نُسخَةٍ <see cref="NameKind.Renamed"/> — والسَطرُ في الخَطِّ الزَمَنيِّ يُمحى مَع صَفقَتِه،
    /// والرَدُّ مَع تَذكِرَتِه.</summary>
    internal static Type ErasedThrough(Type doc)
        => doc == typeof(DealEvent) ? typeof(Deal) : doc == typeof(ACommerce.Kit.Support.Reply) ? typeof(ACommerce.Kit.Support.Ticket) : doc;

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-29، د52 (١) «أ»)</b> — كانَ <c>Every_person_name_field_on_a_stored_type_is_classified_and_the_eraser_takes_no_copy</c>
    /// («نُسَخٌ لا يَمَسُّها الحَذف: 16»). والآن صِفرُ نُسخَةٍ تُعَدَّلُ وتَبقى: اثنَتا عَشرَةَ يَمحوها <see cref="AccountErasure"/> (لِكُلِّ نَوعٍ دالَّتُه)، وأَربَعٌ في سِجِلٍّ
    /// لا يُعَدَّلُ بِطَبيعَتِه (ثَلاثَةُ أَحداثٍ وسِجِلُّ التَدقيق) يَقولُها نَصُّ الحَذف.</summary>
    [Fact]
    public void Every_person_name_field_on_a_stored_type_is_classified_and_no_editable_copy_survives_the_deletion()
    {
        var (roots, closure, names) = StoredNameFields();
        var found = names.Select(n => n.Key).ToList();
        var unclassified = found.Where(k => !PersonNameFields.ContainsKey(k)).ToList();
        var stale = PersonNameFields.Keys.Where(k => !found.Contains(k)).ToList();
        var renamed = names.Where(n => PersonNameFields.TryGetValue(n.Key, out var f) && f.Kind == NameKind.Renamed).ToList();
        var kept = found.Where(k => PersonNameFields.TryGetValue(k, out var f) && KeptAfterDeletion(f.Kind)).ToList();
        var erasers = typeof(AccountErasure).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == nameof(AccountErasure.EraseFrom)).Select(m => m.GetParameters()[0].ParameterType).ToHashSet();
        var unerased = renamed.Where(n => !erasers.Contains(ErasedThrough(n.Type))).Select(n => n.Key).ToList();
        output.WriteLine($"جُذورٌ مُخَزَّنَة: {roots.Count} · ما تَبلُغُه: {closure.Count} نَوعاً · خَصائِصُ اسم: {found.Count} · تُمحى: {renamed.Count} · تَبقى في سِجِلٍّ لا يُعَدَّل: {kept.Count} · بِلا دالَّةِ مَحو: {unerased.Count}");
        foreach (var (key, _) in names)
            output.WriteLine($"  {key} — {(PersonNameFields.TryGetValue(key, out var f) ? $"{f.Kind}/{f.Said}" : "غَيرُ مُصَنَّف")}");

        Assert.True(roots.Count >= 30 && closure.Count >= roots.Count, $"أَداةٌ عَمياء: {roots.Count} جَذراً، {closure.Count} نَوعاً.");
        Assert.True(unclassified.Count == 0, $"خَصائِصُ اسمٍ بِلا تَصنيف: {string.Join(", ", unclassified)}");
        Assert.True(stale.Count == 0, $"تَصنيفٌ لا يَجِدُه المَسح: {string.Join(", ", stale)}");
        Assert.Equal(12, renamed.Count);
        Assert.Equal(4, kept.Count);
        Assert.True(unerased.Count == 0, $"نُسخَةٌ تُعَدَّلُ بِلا دالَّةِ مَحو: {string.Join(", ", unerased)}");
        Assert.Equal(found.Count, found.Distinct(StringComparer.Ordinal).Count());

        // القائِمَةُ اليَدَوِيَّةُ (الصَفّ ٣) بَعضُ ما يَمحوه الحَذف — حَدٌّ أَدنى لا جَرد.
        Assert.All(NameCopies, c => Assert.Contains($"{c.Doc.Name}.{c.Field}", renamed.Select(n => n.Key)));

        // ولا يَكتُبُ الحَذفُ إلّا في الجَلسَة: مَحوُ النُسَخِ داخِلَ `EraseCopiesAsync` ولا `SaveChangesAsync` فيه — الحِفظُ الواحِدُ لِلنُقطَة.
        var erasure = WriteEndpointGuardTests.StripComments(Tpl("Services", "Identity", "AccountErasure.cs"));
        Assert.DoesNotContain("SaveChangesAsync", erasure, StringComparison.Ordinal);

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

    // ‏2026-10-25 (مُراجَعَةُ تَيّارِ W8-4 الجَولَة ٢): و`Mobile` — `OtoCustomer.Mobile` و`vendor.MobileNumber` أَسماءُ هاتِفٍ لا تَنتَهي بِـPhone.
    private static readonly Regex PersonalToken = new(
        @"(?<![\w""])(?:[A-Za-z_]\w*\.)*(?<id>(?:[A-Za-z_]\w*)?(?:Name|Phone|Email|Mobile|name|phone|email|mobile)(?:Number)?)\b", RegexOptions.Compiled);

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

    private static readonly Regex NewNotification = new(
        @"\bnew\s+(?:ACommerce\.Kit\.Notifications\.)?Notification\s*(?:\(\s*\))?\s*(?=\{)", RegexOptions.Compiled);
    private static readonly Regex NotificationFactory = new(
        @"\b(?:ACommerce\.Kit\.Notifications\.)?Notification\s+\w+\s*\([^)]*\)\s*=>\s*new\s*\(\s*\)\s*(?=\{)", RegexOptions.Compiled);

    /// <summary>أَعضاءُ مُهَيِّئِ كائِنٍ عَلى المُستَوى الأَعلى — الفاصِلَةُ داخِلَ قَوسٍ أَو نَصٍّ لا تَفصِل.</summary>
    private static List<string> InitializerMembers(string inner)
    {
        var members = new List<string>();
        var depth = 0; var start = 0; var inString = false;
        for (var i = 0; i < inner.Length; i++)
        {
            var c = inner[i];
            if (inString) { if (c == '\\') i++; else if (c == '"') inString = false; continue; }
            if (c == '"') inString = true;
            else if (c is '(' or '{' or '[') depth++;
            else if (c is ')' or '}' or ']') depth--;
            else if (c == ',' && depth == 0) { members.Add(inner[start..i]); start = i + 1; }
        }
        members.Add(inner[start..]);
        return members.Where(m => m.Trim().Length > 0).ToList();
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
    /// <c>NotifyAdminsAsync</c> (وَسيطاه الرابِعُ والخامِس) — والتَسمِيَةُ قيمَةُ <c>Type</c>.
    /// <para><b>‏2026-10-25 (مُراجَعَةُ تَيّارِ W8-4 الجَولَة ٢، صُغرى)</b>: كانَ يَجِدُ الكاتِبَ بِسَطرِ <c>RelatedUrl =</c> وَحدَه ويَقرَأُ العُنوانَ والمَتنَ سَطراً
    /// واحِداً — أَعمى عَن مُهَيِّئٍ بِلا <c>RelatedUrl</c>، وعَن تَعبيرٍ يَمتَدُّ أَسطُراً، وعَن مُهَيِّئٍ في سَطرٍ واحِد. <b>صارَ يُرسي عَلى الإنشاءِ نَفسِه</b>:
    /// <c>new (ACommerce.Kit.Notifications.)Notification {</c>، و<c>Notification X(…) => new() {</c> (مَصانِعُ <c>DealNotices</c>)، ويَبقى سَطرُ
    /// <c>RelatedUrl =</c> مِرساةً ثالِثَة؛ والمُهَيِّئُ يُقسَمُ أَعضاءً بِالفَواصِلِ العُليا (النُصوصُ والأَقواسُ مُتَوازِنَة) فَالتَعبيرُ كامِلٌ لا سَطرُه الأَوَّل.
    /// <b>وما لا يَراه — يُقال</b>: <c>return new() {</c> في دالَّةٍ جِسمُها كُتلَة، وإنشاءٌ بِوُسَطاءِ مُنشِئ.</para></summary>
    internal static IEnumerable<NoticeWriter> NoticeWriters(string file, string code)
    {
        int LineOf(int at) => code.Take(at).Count(c => c == '\n') + 1;
        var opens = new SortedSet<int>();
        foreach (Match m in NewNotification.Matches(code)) opens.Add(m.Index + m.Length);
        foreach (Match m in NotificationFactory.Matches(code)) opens.Add(m.Index + m.Length);
        foreach (Match m in Regex.Matches(code, @"(?m)^[ \t]*RelatedUrl\s*=(?!=)"))
        {
            var depth = 0; var open = -1;
            for (var i = m.Index; i >= 0 && open < 0; i--)
            {
                if (code[i] == '}') depth++;
                else if (code[i] == '{' && depth-- == 0) open = i;
            }
            if (open >= 0) opens.Add(open);
        }
        foreach (var open in opens)
        {
            var close = Closing(code, open);
            var members = InitializerMembers(close > open ? code[(open + 1)..close] : code[(open + 1)..]);
            string Field(string f) => members.Select(x => Regex.Match(x, $@"^\s*{f}\s*=\s*(?<e>[\s\S]*?)\s*$")).FirstOrDefault(x => x.Success)?.Groups["e"].Value ?? "";
            yield return new NoticeWriter(file, LineOf(open), Field("Type"), PersonalTokens(Field("Title") + " " + Field("Body")));
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

    /// <summary>الكُتّابُ الَّذينَ يَضَعونَ اسماً أَو هاتِفاً أَو بَريداً في إشعارٍ مُخَزَّنٍ في صُندوقِ غَيرِ صاحِبِه — وما يَقولُه نَصُّ الحَذفِ عَنهُم.
    /// <para><b>‏2026-10-29 (د52)</b>: كُلُّها «يَصيرُ اسمُكَ حِساباً مَحذوفاً في الإشعاراتِ المَحفوظَةِ عِندَ غَيرِك» (<see cref="AccountErasure.EraseFrom(ACommerce.Kit.Notifications.Notification, Guid, IReadOnlyCollection{string}, IReadOnlyCollection{string}, IReadOnlyCollection{string}, IReadOnlySet{Guid})"/>)،
    /// وإشعارُ التَسجيلِ لِلمُديرينَ صارَ بِالاسمِ وَحدَه (د52 (٣) «أ») — كاتِبانِ بِمِفتاحٍ واحِد.</para></summary>
    internal static readonly IReadOnlyDictionary<string, Disclosure> PersonalNoticeWriters = new Dictionary<string, Disclosure>(StringComparer.Ordinal)
    {
        ["AcceptedType|acceptorName"]    = Disclosure.NoticesRenamed,   // ‏DealNotices.AcceptedFor — لِلمُشتَري: قَبِلَ فُلان
        ["CancelledType|cancellerName"]  = Disclosure.NoticesRenamed,   // ‏DealNotices.CancelledFor — لِلمُشتَري: أَلغى فُلان
        ["\"offer_accepted\"|acceptorName"] = Disclosure.NoticesRenamed, // ‏لِلسائِق: قَبِلَ فُلانٌ عَرضَك
        ["\"driver_arrived\"|OffererName"]  = Disclosure.NoticesRenamed, // ‏لِلراكِب: وَصَلَ فُلان
        ["\"chat_message\"|senderName"]     = Disclosure.NoticesRenamed, // ‏لِلطَرَفِ الآخَر: رِسالَةٌ مِن فُلان
        ["\"new_user\"|FullName"]           = Disclosure.NoticesRenamed, // ‏لِمُديري المَتجَر: سَجَّلَ فُلان — بِالجَوّالِ وبِالبَريد، بِلا وَسيلَةِ اتِّصال
        ["\"report\"|userName"]             = Disclosure.NoticesRenamed, // ‏لِمُديري المَتجَر: فُلانٌ بَلَّغَ عَن إعلان
    };

    internal static IReadOnlyList<NoticeWriter> AllNoticeWriters()
        => EntitlementContractTests.SourceFiles()
            .SelectMany(f => NoticeWriters(Path.GetFileName(f.File), WriteEndpointGuardTests.StripComments(f.Text)))
            .ToList();

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-29، د52 (٣) «أ»)</b> — كانَ <c>Every_stored_notification_that_carries_a_name_a_phone_or_an_email_is_pinned</c>
    /// («هاتِفٌ أَو بَريد: 2» — إشعارُ «مُستَخدِم جَديد سَجَّل» بِالاسمِ والهاتِفِ أَو البَريد). والآن صِفرُ كاتِبٍ يَضَعُ هاتِفاً أَو بَريداً في صُندوقِ غَيرِ صاحِبِه،
    /// وكاتِبا التَسجيلِ بِالاسمِ وَحدَه.</summary>
    [Fact]
    public void Every_stored_notification_that_carries_a_name_is_pinned_and_none_carries_a_phone_or_an_email()
    {
        var writers = AllNoticeWriters();
        var personal = writers.Where(w => w.Personal.Count > 0).ToList();
        output.WriteLine($"كُتّابُ Notification: {writers.Count} · يَحمِلُ اسماً أَو هاتِفاً أَو بَريداً: {personal.Count}");
        foreach (var w in writers) output.WriteLine($"  {w.File}:{w.Line} {w.Label} [{string.Join(", ", w.Personal)}]");

        Assert.True(writers.Count >= 10, $"أَداةٌ عَمياء: {writers.Count} كاتِباً لِلإشعار.");
        Assert.Equal(PersonalNoticeWriters.Keys.OrderBy(k => k, StringComparer.Ordinal),
                     personal.Select(w => w.Key).Distinct().OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(2, personal.Count(w => w.Key == "\"new_user\"|FullName"));
        Assert.Equal(0, personal.Count(w => w.Personal.Any(p => p.EndsWith("Phone", StringComparison.Ordinal) || p.EndsWith("Email", StringComparison.Ordinal))));
    }

    [Theory]
    [InlineData("new Notification\n{\n    Type = \"chat_message\",\n    Title = l.Format(\"notifications.chat_message.title\", senderName),\n    Body = conv.LastMessage ?? \"—\",\n    RelatedUrl = u,\n}", "\"chat_message\"|senderName")]
    [InlineData("await NotifyAdminsAsync(store, slug, \"new_user\",\n    \"t\",\n    $\"{user.FullName} · {user.Phone}\",\n    \"/u\");", "\"new_user\"|FullName,Phone")]
    [InlineData("await NotifyAdminsAsync(store, slug, \"x\", \"t\", $\"{name} · {phone}\", \"/u\");", "\"x\"|name,phone")]
    [InlineData("new()\n{\n    Type = NewDealType,\n    Title = l[\"notifications.deal_new.title\"],\n    Body = l.Format(\"notifications.deal_new.body\", deal.ListingTitle, deal.AmountSar),\n    RelatedUrl = u,\n};", "NewDealType|")]
    public void The_notice_writer_pattern_sees_what_it_counts(string code, string expected)
        => Assert.Equal(expected, Assert.Single(NoticeWriters("probe.cs", code)).Key);

    /// <summary>القاعِدَة ١٠ — الأَشكالُ الَّتي كانَ الماسِحُ أَعمى عَنها (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-4 الجَولَة ٢): مُهَيِّئٌ بِلا <c>RelatedUrl</c>، وعُنوانٌ يَمتَدُّ
    /// سَطرَين، ومُهَيِّئٌ في سَطرٍ واحِد، و<c>Mobile</c>، ومَصنَعٌ <c>=> new()</c> بِلا <c>RelatedUrl</c>، وفاصِلَةٌ داخِلَ نَصّ — وما لَيسَ إشعاراً لا يُعَدّ.</summary>
    [Theory]
    [InlineData("s.Store(new Notification\n{\n    Type = \"x\",\n    Title = t,\n    Body = $\"{user.FullName}\",\n});", "\"x\"|FullName")]
    [InlineData("s.Store(new ACommerce.Kit.Notifications.Notification\n{\n    Type = \"y\",\n    Title = l.Format(\"k\",\n        buyer.Name),\n    RelatedUrl = u,\n});", "\"y\"|Name")]
    [InlineData("var n = new Notification { Type = \"z\", Title = \"t\", Body = vendor.Mobile, RelatedUrl = u };", "\"z\"|Mobile")]
    [InlineData("public static Notification For(Deal d, string sellerName) => new()\n{\n    Type = T,\n    Title = sellerName,\n    Body = b,\n};", "T|sellerName")]
    [InlineData("s.Store(new Notification\n{\n    Type = \"w\",\n    Body = \"a, b\" + who.Name,\n});", "\"w\"|Name")]
    [InlineData("var o = new NotificationOptions { Type = \"q\", Title = user.Name };", "")]
    [InlineData("public static NotificationRow For(Deal d) => new()\n{\n    Type = T,\n    Title = d.BuyerName,\n};", "")]
    public void The_notice_writer_pattern_sees_every_construction_and_the_whole_expression(string code, string expected)
    {
        var found = NoticeWriters("probe.cs", code).ToList();
        output.WriteLine($"مَحقونَة: {found.Count} — {string.Join(" · ", found.Select(w => w.Key))}");
        if (expected.Length == 0) Assert.Empty(found);
        else Assert.Equal(expected, Assert.Single(found).Key);
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

    // ─── ٥. النُصوص — ما يُمحى وما يَبقى ولِماذا (‏2026-10-29، قَرارُ المالِك د52؛ وقَبلَه الخِيارُ (ب) مِن brief-P3-5a) ─────────────────────

    internal const string StorePrivacyKey = "legal.privacy.deletion";
    internal const string PlatformRetentionKey = "platform.privacy.s6_body";
    internal const string PlatformRightsKey = "platform.privacy.s7_body";
    internal const string RetainedNoteKey = "account.delete.retained_note";
    internal const string RemovedItemsKey = "account.delete.removed_items";

    private static string Ar(string key) => LocaleCatalog.Find(LocaleCatalog.Arabic, key)
        ?? throw new Xunit.Sdk.XunitException($"لا مِفتاحَ «{key}» في ar.json.");

    private static string En(string key) => LocaleCatalog.Find("en", key)
        ?? throw new Xunit.Sdk.XunitException($"لا مِفتاحَ «{key}» في en.json.");

    /// <summary>النَصُّ كَما يَقرَؤُه المُستَخدِم: لِشاشَةِ الحَذفِ «ما يُحذَفُ الآن» ثُمَّ «ما يَبقى، ولِماذا» (‏2026-10-29: المَمحُوُّ في الأَوَّلِ والباقي في الثاني)،
    /// ولِلسِياسَتَينِ مِفتاحُهُما.</summary>
    internal static string Text(string key) => key == RetainedNoteKey ? Ar(RemovedItemsKey) + " " + Ar(RetainedNoteKey) : Ar(key);

    /// <summary>القيَمُ قَبلَ كوميتِ د52 بِحَرفِها (‏<c>c2637e4</c> وما قَبلَه — صِياغَةُ الخِيارِ (ب)) — لِيُقاسَ أَنّ الفَرقَ يَبدَأُ بَعدَ الصَدرِ المُعلَن.</summary>
    internal static readonly IReadOnlyDictionary<string, string> Before = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [StorePrivacyKey] = "لِحَذف حِسابك بِالكامِل: صَفحَة «حِسابي» ثُمَّ «حَذفُ الحِساب». يَقَع الحَذف فَور تَأكيدك، ويَبقى سِجِلّ الصَّفَقات والفَواتير لِالتِزام نِظاميّ ومُحاسَبيّ، وفي كُلّ صَفقَة الاسم الَّذي كانَ لَك وَقتَها أَو كَتَبتَه في الطَّلَب، وعُنوان التَّوصيل والهاتِف اللَّذان كَتَبتَهُما فيه: يَراها بائِع الصَّفقَة، ويَرى صاحِب المَتجَر الاسم، وتَصِل إلى شَرِكَة الشَّحن إن شَحَن البائِع طَلَبك عَبرَها. وإن كُنت بائِع صَفقَة بَقِي اسمك عَلَيها يَراه مُشتَريها. ويَبقى اسمك كَما كُتِب عَلى مُحادَثاتك وتَقييماتك وعُروضك وتَذاكِر دَعمك وبَلاغاتك، وفي إشعارات مَن تَعامَلت مَعَهُم، وفي إشعارات مُديري المَتجَر: اسمك وهاتِفك أَو بَريدك عِند تَسجيلك، واسمك عِند كُلّ بَلاغ. وإن عَمِلت في المَتجَر بائِعاً أَو مِن فَريق إدارَتِه بَقِي اسمك في سِجِلّ التَّدقيق ومَفاتيح الرَّبط.",
        [PlatformRetentionKey] = "نَحتَفِظُ بِبَياناتِكَ ما دامَ حِسابُكَ قائِماً وما تَقتَضيهِ المُتَطَلَّباتُ النِظامِيَّةُ والمُحاسَبِيَّة. ويُمكِنُكَ حَذفُ حِسابِكَ في المَتجَرِ وبَياناتِكَ الشَخصِيَّةَ مِن داخِلِ التَطبيقِ نَفسِه: صَفحَةُ «حِسابي» ثُمَّ «حَذفُ الحِساب»، ويَقَعُ الحَذفُ فَورَ تَأكيدِك. ويَبقى سِجِلُّ الصَفَقاتِ والفَواتيرِ ما اقتَضاهُ التِزامٌ نِظامِيٌّ ومُحاسَبِيّ، وفي كُلِّ صَفقَةٍ الاسمُ الَّذي كانَ لَكَ وَقتَها أَو كَتَبتَه في الطَلَب، وعُنوانُ التَوصيلِ والهاتِفُ اللَّذانِ كَتَبتَهُما فيه: يَراها بائِعُ الصَفقَة، ويَرى صاحِبُ المَتجَرِ الاسم، وتَصِلُ إلى شَرِكَةِ الشَحنِ إن شَحَنَ البائِعُ طَلَبَكَ عَبرَها. وإن كُنتَ بائِعَ صَفقَةٍ بَقِيَ اسمُكَ عَلَيها يَراه مُشتَريها. ويَبقى اسمُكَ كَما كُتِبَ عَلى مُحادَثاتِكَ وتَقييماتِكَ وعُروضِكَ وتَذاكِرِ دَعمِكَ وبَلاغاتِك، وفي إشعاراتِ مَن تَعامَلتَ مَعَهُم، وفي إشعاراتِ مُديري المَتجَر: اسمُكَ وهاتِفُكَ أَو بَريدُكَ عِندَ تَسجيلِك، واسمُكَ عِندَ كُلِّ بَلاغ. وإن عَمِلتَ في المَتجَرِ بائِعاً أَو مِن فَريقِ إدارَتِه بَقِيَ اسمُكَ في سِجِلِّ التَدقيقِ ومَفاتيحِ الرَبط. أَمّا حِسابُ الاستوديو (لِأَصحابِ المَتاجِر) فَلا بابَ لِحَذفِه داخِلَ التَطبيقِ اليَوم.",
        [RetainedNoteKey] = "يَبقى سِجِلُّ الصَفَقاتِ والفَواتير، وفي كُلِّ صَفقَةٍ الاسمُ الَّذي كانَ لَكَ وَقتَها أَو كَتَبتَه في الطَلَب، وعُنوانُ التَوصيلِ والهاتِفُ اللَّذانِ كَتَبتَهُما فيه: يَراها بائِعُ الصَفقَة، ويَرى صاحِبُ المَتجَرِ الاسم، وتَصِلُ إلى شَرِكَةِ الشَحنِ إن شَحَنَ البائِعُ طَلَبَكَ عَبرَها. وإن كُنتَ بائِعَ صَفقَةٍ بَقِيَ اسمُكَ عَلَيها يَراه مُشتَريها. ويَبقى اسمُكَ كَما كُتِبَ عَلى مُحادَثاتِكَ وتَقييماتِكَ وعُروضِكَ وتَذاكِرِ دَعمِكَ وبَلاغاتِك، وفي إشعاراتِ مَن تَعامَلتَ مَعَهُم، وفي إشعاراتِ مُديري المَتجَر: اسمُكَ وهاتِفُكَ أَو بَريدُكَ عِندَ تَسجيلِك، واسمُكَ عِندَ كُلِّ بَلاغ. وإن عَمِلتَ في المَتجَرِ بائِعاً أَو مِن فَريقِ إدارَتِه بَقِيَ اسمُكَ في سِجِلِّ التَدقيقِ ومَفاتيحِ الرَبط. والاحتِفاظُ بِسِجِلِّ الصَفَقاتِ التِزامٌ نِظامِيٌّ ومُحاسَبِيّ: المادَّةُ الخامِسَةُ تُجيزُه صَراحَةً «دونَ إخلالٍ بِما يَقضي بِه نِظامٌ آخَر».",
    };

    /// <summary>صَدرُ كُلِّ قيمَةٍ قَبلَ أَوَّلِ تَبديلٍ مَقصود — يَبقى بِحَرفِه: بابُ الحَذفِ ووَقتُه في السِياسَتَين، وصَدرُ «ما يَبقى» في الشاشَة.</summary>
    internal static readonly IReadOnlyDictionary<string, string> Unchanged = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [StorePrivacyKey] = "لِحَذف حِسابك بِالكامِل: صَفحَة «حِسابي» ثُمَّ «حَذفُ الحِساب». يَقَع الحَذف فَور تَأكيدك، و",
        [PlatformRetentionKey] = "نَحتَفِظُ بِبَياناتِكَ ما دامَ حِسابُكَ قائِماً وما تَقتَضيهِ المُتَطَلَّباتُ النِظامِيَّةُ والمُحاسَبِيَّة. ويُمكِنُكَ حَذفُ حِسابِكَ في المَتجَرِ وبَياناتِكَ الشَخصِيَّةَ مِن داخِلِ التَطبيقِ نَفسِه: صَفحَةُ «حِسابي» ثُمَّ «حَذفُ الحِساب»، ويَقَعُ الحَذفُ فَورَ تَأكيدِك",
        [RetainedNoteKey] = "يَبقى سِجِلُّ الصَفَقاتِ والفَواتير",
    };

    /// <summary>النَصُّ بِلا حَرَكات — فَالفاحِصُ يَقرَأُ الكَلِمَةَ لا تَشكيلَها (‏<c>LegalHub</c> بِتَشكيلٍ أَخَفَّ مِن صَفحَةِ المَنَصَّة).</summary>
    internal static string Bare(string s) => Regex.Replace(s, "[ً-ْٰ]", "");

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-29، د52)</b> — كانَ <c>The_deletion_texts_say_the_deal_keeps_the_name_you_had_or_typed_in_the_order</c> (وقَبلَه
    /// <c>Today_…</c> في <c>6a16439</c>): النُصوصُ الثَلاثَةُ قالَت «الاسمُ الَّذي كانَ لَكَ وَقتَها أَو كَتَبتَه في الطَلَب» يَبقى في الصَفقَة. والآن يَمحوه <see cref="AccountErasure"/>
    /// — الاسمُ المَكتوبُ في الاستِمارَةِ نَفسُه (`InitiatorName`) وعُنوانُ التَوصيلِ وهاتِفُه — والنُصوصُ تَقولُ «بِلا اسمِك» و«يُمحى عُنوانُ التَوصيلِ والهاتِف».</summary>
    [Fact]
    public void The_deletion_texts_say_the_deal_keeps_neither_the_name_nor_the_address_typed_in_the_order()
    {
        var me = Guid.NewGuid();
        var typed = new Deal
        {
            InitiatorId = me, InitiatorName = "اسمٌ كُتِبَ في الاستِمارَة", CounterpartyId = Guid.NewGuid(),
            Attributes = new() { [Deal.DeliveryAddressAttribute] = "حَيُّ المِثال", [Deal.DeliveryPhoneAttribute] = "0500000000", ["qty"] = "1" },
        };
        AccountErasure.EraseFrom(typed, me);
        var texts = new[] { StorePrivacyKey, PlatformRetentionKey, RetainedNoteKey }.ToDictionary(k => k, k => Bare(Text(k)));
        output.WriteLine($"بَعدَ المَحو: الاسم «{(typed.InitiatorName == AccountDeletion.ErasedName ? "مَمحُوّ" : "باقٍ")}» · خَصائِص: {string.Join(",", typed.Attributes.Keys)}");
        Assert.Equal(AccountDeletion.ErasedName, typed.InitiatorName);
        Assert.Equal(new[] { "qty" }, typed.Attributes.Keys);
        foreach (var (k, t) in texts)
        {
            Assert.DoesNotContain("الاسم الذي كان لك وقتها", t, StringComparison.Ordinal);
            Assert.True(t.Contains("بلا اسمك", StringComparison.Ordinal), $"{k} لا يَقولُ «بِلا اسمِك»: {t}");
        }
        Assert.Contains("remain without your name", En(PlatformRetentionKey), StringComparison.Ordinal);
        Assert.DoesNotContain("the name you had at the time", En(PlatformRetentionKey), StringComparison.Ordinal);
    }

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-29، د52)</b> — كانَ <c>Deletion_texts_say_what_each_deal_keeps_and_who_sees_it_and_tell_the_studio_account_apart</c>
    /// (وقَبلَه <c>Today_three_deletion_texts_promise_deal_records_without_a_name_or_contact…</c> في <c>b52ea8d</c>). والآن يَقولُ كُلٌّ مِنها ما يُمحى ولِمَن
    /// يَبقى ما يَبقى — بِعِباراتِ <see cref="DisclosurePhrases"/> — والفَرقُ مَحصور: صَدرُ القيمَةِ إلى أَوَّلِ تَبديلٍ مَقصودٍ بِحَرفِه، وحِسابُ الاستوديو بِاسمِه
    /// وطَريقِ حَذفِه (د52 (٢) «ب»: «لا بابَ الآن، ويُطلَبُ الحَذفُ بِبَريدِ التَواصُل»).</summary>
    [Fact]
    public void Deletion_texts_say_what_is_erased_and_what_remains_and_tell_the_studio_account_apart()
    {
        foreach (var k in new[] { StorePrivacyKey, PlatformRetentionKey, RetainedNoteKey })
        {
            var now = Ar(k);
            var was = Before[k];
            var shared = 0;
            while (shared < now.Length && shared < was.Length && now[shared] == was[shared]) shared++;
            output.WriteLine($"{k}: قَبلُ {was.Length} حَرفاً · بَعدُ {now.Length} · الصَدرُ المُشتَرَك {shared}");
            Assert.StartsWith(Unchanged[k], was, StringComparison.Ordinal);
            Assert.StartsWith(Unchanged[k], now, StringComparison.Ordinal);
            Assert.True(shared >= Unchanged[k].Length, $"{k}: الفَرقُ يَبدَأُ عِندَ {shared} قَبلَ الصَدرِ المُعلَن ({Unchanged[k].Length}).");
            Assert.DoesNotContain("وفي إشعارات مديري المتجر: اسمك وهاتفك أو بريدك", Bare(Text(k)), StringComparison.Ordinal);
        }
        Assert.Contains("أَمّا حِسابُ الاستوديو (لِأَصحابِ المَتاجِر) فَلا بابَ لِحَذفِه داخِلَ التَطبيقِ اليَوم، ويُطلَبُ حَذفُه مِن بَريدِ التَواصُلِ أَدناه.", Ar(PlatformRetentionKey), StringComparison.Ordinal);
        Assert.Contains("A studio account (for store owners) has no deletion door inside the app today; its deletion is requested at the contact email below.", En(PlatformRetentionKey), StringComparison.Ordinal);
        Assert.Contains("وحَذفُ حِسابِ المَتجَرِ يَقَعُ مِن داخِلِ التَطبيق", Ar(PlatformRightsKey), StringComparison.Ordinal);
        Assert.Contains("Store account deletion happens inside the app", En(PlatformRightsKey), StringComparison.Ordinal);
    }

    // ─── ٥ب. ما يَجِدُه المَسحانِ تَقولُه النُصوصُ الثَلاثَة ────────────────────────────────────────────────

    /// <summary>عِباراتُ كُلِّ إفصاحٍ بِلا حَرَكات — يَقولُها كُلٌّ مِن نُصوصِ الحَذفِ الثَلاثَة (‏2026-10-29، د52).</summary>
    internal static readonly IReadOnlyDictionary<Disclosure, string[]> DisclosurePhrases = new Dictionary<Disclosure, string[]>
    {
        [Disclosure.Renamed]        = new[] { "حساب محذوف", "في صفقاتك ومحادثاتك وتقييماتك وعروضك وتذاكر دعمك" },
        [Disclosure.NoticesRenamed] = new[] { "وفي الإشعارات المحفوظة عند غيرك" },
        [Disclosure.AddressErased]  = new[] { "ويمحى عنوان التوصيل والهاتف اللذان كتبتهما في طلباتك" },
        [Disclosure.EventLog]       = new[] { "سجل الأحداث الداخلي الذي لا يعدل ولا تعرضه شاشة" },
        [Disclosure.AuditLog]       = new[] { "سجل تدقيق المتجر", "ويخفى عند عرضه" },
        [Disclosure.Delivered]      = new[] { "إلى شركة الشحن", "إلى أجهزة غيرك في إشعارات المتصفح" },
        [Disclosure.OpenDeal]       = new[] { "صفقة جارية" },
    };

    /// <summary>كُلُّ إفصاحٍ يَحتاجُه ما يَجِدُه المَسحان — مِن خَصائِصِ الاسمِ ومِن كُتّابِ الإشعار — ومَعَها ثَلاثَةٌ لَيسَت أَسماءً: العُنوانُ والهاتِفُ
    /// المَمحُوّانِ مِن الصَفقَة (<see cref="Deal.DeliveryAddressAttribute"/>)، وما وَصَلَ قَبلَ الحَذفِ إلى الناقِلِ والأَجهِزَة، والصَفقَةُ الجارِيَة.</summary>
    internal static IReadOnlyList<Disclosure> NeededDisclosures()
        => PersonNameFields.Values.Where(f => f.Said != Disclosure.None).Select(f => f.Said)
            .Concat(PersonalNoticeWriters.Values)
            .Concat(new[] { Disclosure.AddressErased, Disclosure.Delivered, Disclosure.OpenDeal })
            .Distinct().OrderBy(d => d).ToList();

    internal static IReadOnlyList<Disclosure> Unsaid(string text)
        => NeededDisclosures().Where(d => !DisclosurePhrases[d].All(p => Bare(text).Contains(p, StringComparison.Ordinal))).ToList();

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-29، د52)</b> — كانَ <c>Every_copy_the_scans_find_is_said_by_each_deletion_text</c> بِسِتَّةِ إفصاحاتٍ عَمّا يَبقى. والآن
    /// سَبعَةٌ عَمّا يُمحى وما يَبقى ولِماذا — وإفصاحٌ ثامِنٌ يَحتاجُه تَصنيفٌ جَديدٌ في <see cref="PersonNameFields"/> أَو <see cref="PersonalNoticeWriters"/> يُحمِرُّ
    /// هذا الصَفَّ حَتّى يُكتَبَ في النُصوص. والصِياغَةُ بِالمُمارَسَةِ المُعتادَة، ومُراجَعَةُ المُحامي اختِيارِيَّة («ج» د52).</summary>
    [Fact]
    public void Every_copy_the_scans_find_is_said_by_each_deletion_text_as_erased_or_kept_with_its_reason()
    {
        var needed = NeededDisclosures();
        output.WriteLine($"إفصاحاتٌ يَحتاجُها المَسحان: {string.Join(", ", needed)}");
        Assert.Equal(new[] { Disclosure.Renamed, Disclosure.NoticesRenamed, Disclosure.AddressErased, Disclosure.EventLog, Disclosure.AuditLog, Disclosure.Delivered, Disclosure.OpenDeal }, needed);
        foreach (var k in new[] { StorePrivacyKey, PlatformRetentionKey, RetainedNoteKey })
        {
            var unsaid = Unsaid(Text(k));
            output.WriteLine($"{k}: لا يَقولُ {unsaid.Count}");
            Assert.Empty(unsaid);
        }
        // والإنجليزيُّ لِـs6 يَقولُ ما يَقولُه العَرَبِيّ (en.json لِصَفَحاتِ المَنَصَّةِ وَحدَها — لا مُقابِلَ لِنَصَّيِ المَتجَر).
        foreach (var phrase in new[] { "\"Deleted account\"", "deals, conversations, reviews, offers and support tickets", "in the notifications stored for other people",
                                       "the delivery address and phone number you entered in your orders are erased", "the internal event log, which is never edited and no screen displays",
                                       "the store's audit log", "where it is hidden when shown", "the shipping company or other people's devices in browser notifications",
                                       "while you have an open deal" })
            Assert.Contains(phrase, En(PlatformRetentionKey), StringComparison.Ordinal);
        Assert.Null(LocaleCatalog.Find("en", StorePrivacyKey));
        Assert.Null(LocaleCatalog.Find("en", RetainedNoteKey));
    }

    /// <summary>القاعِدَة ١٠ — قارِئُ الإفصاحِ (<see cref="Unsaid"/>) يُقاسُ داخِلَ الشَجَرَة: حَذفُ عِبارَةٍ واحِدَةٍ مِن نَصِّ اليَومِ يَجعَلُ القارِئَ يُسَمّي إفصاحَها
    /// وَحدَه؛ والنَصُّ قَبلَ د52 (<see cref="Before"/>) والنَصُّ الفارِغُ لا يَقولانِ شَيئاً مِن المَطلوب؛ و<c>Bare</c> لا يَمَسُّ إلّا الحَرَكات.</summary>
    [Theory]
    [InlineData(StorePrivacyKey)]
    [InlineData(PlatformRetentionKey)]
    [InlineData(RetainedNoteKey)]
    public void The_disclosure_reader_names_exactly_the_phrase_that_is_missing(string key)
    {
        var needed = NeededDisclosures();
        var today = Bare(Text(key));
        Assert.Empty(Unsaid(today));
        var removals = 0;
        foreach (var d in needed)
            foreach (var phrase in DisclosurePhrases[d])
            {
                Assert.Contains(phrase, today, StringComparison.Ordinal);
                Assert.Equal(new[] { d }, Unsaid(today.Replace(phrase, "", StringComparison.Ordinal)));
                removals++;
            }
        output.WriteLine($"{key}: حَذفُ عِبارَةٍ واحِدَةٍ {removals} مَرَّة — كُلٌّ سَمّى إفصاحَه وَحدَه · قَبلَ د52: {Unsaid(Before[key]).Count}/{needed.Count} · الفارِغ: {Unsaid("").Count}/{needed.Count}");
        Assert.True(removals >= needed.Count, $"أَداةٌ عَمياء: {removals} عِبارَة.");
        Assert.Equal(needed, Unsaid(Before[key]));
        Assert.Equal(needed, Unsaid(""));
        Assert.Equal("أو كتبته في الطلب", Bare("أَو كَتَبتَه في الطَلَب"));
        Assert.Equal(Text(key).Length - Regex.Matches(Text(key), "[ً-ْٰ]").Count, today.Length);
    }

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-29، د52)</b> — كانَ <c>A_seller_who_deletes_is_told_their_name_stays_on_each_deal_its_buyer_sees</c> (وقَبلَه <c>Today_…</c>
    /// في <c>b7f9d8f</c>): اسمُ البائِعِ يَبقى عَلى صَفقَتِه يَراه مُشتَريها. والآن يُمحى (<c>Deal.CounterpartyName</c>)، وعُنوانُ المُشتَري وهاتِفُه في صَفقَةٍ باعَها
    /// لا يُمَسّانِ — لَيسا بَيانَه — والجُملَةُ خَرَجَت مِن النُصوص.</summary>
    [Fact]
    public void A_seller_who_deletes_leaves_no_name_on_the_deals_they_sold_and_the_buyers_address_stays_the_buyers()
    {
        var me = Guid.NewGuid();
        var sold = new Deal
        {
            InitiatorId = Guid.NewGuid(), InitiatorName = "مُشتَرٍ", CounterpartyId = me, CounterpartyName = "بائِع",
            Attributes = new() { [Deal.DeliveryAddressAttribute] = "حَيُّ المُشتَري", [Deal.DeliveryPhoneAttribute] = "0500000001" },
        };
        Assert.True(AccountErasure.EraseFrom(sold, me));
        Assert.Equal((AccountDeletion.ErasedName, "مُشتَرٍ"), (sold.CounterpartyName, sold.InitiatorName));
        Assert.Equal(2, sold.Attributes.Count);
        Assert.Equal(Disclosure.Renamed, PersonNameFields["Deal.CounterpartyName"].Said);
        foreach (var k in new[] { StorePrivacyKey, PlatformRetentionKey, RetainedNoteKey })
            Assert.DoesNotContain("بقي اسمك عليها يراه مشتريها", Bare(Text(k)), StringComparison.Ordinal);
        Assert.DoesNotContain("your name stays on it and its buyer sees it", En(PlatformRetentionKey), StringComparison.Ordinal);
    }

    internal const string StoreIdentityItemKey = "legal.privacy.item_identity";

    /// <summary>أَجسامُ إشعارِ <c>new_user</c> لِمُديري المَتجَرِ في <c>MarketplaceTemplateExtensions.cs</c>: بِالاسمِ وَحدَه، وبِالاسمِ والهاتِف، وبِالاسمِ والبَريد.</summary>
    internal static (int NameOnly, int Phone, int Email) NewUserNoticeBodies()
    {
        var mte = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs")));
        int Count(string tail) => Regex.Matches(mte, @"NotifyAdminsAsync\(store, slug, ""new_user"",\s*""[^""]*"",\s*\$""\{user\.FullName\}" + tail + @"""").Count;
        return (Count(""), Count(@" · \{user\.Phone\}"), Count(@" · \{user\.Email\}"));
    }

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-29، د52 (٣) «أ»)</b> — كانَ <c>The_store_privacy_says_registration_sends_name_and_phone_or_email_to_the_store_managers</c>
    /// (وقَبلَه <c>Today_…</c> في <c>fbbb77a</c>): «إشعارُ new_user بِالهاتِف 1 · بِالبَريد 1». والآن الجِسمانِ بِالاسمِ وَحدَه، والبَندُ يَقولُ «يَصِلُ الاسمُ وَحدَه».</summary>
    [Fact]
    public void The_registration_notice_carries_the_name_alone_and_the_store_privacy_says_so()
    {
        var item = Bare(Ar(StoreIdentityItemKey));
        var bodies = NewUserNoticeBodies();
        output.WriteLine($"{StoreIdentityItemKey} = {item} · إشعارُ new_user: بِالاسمِ وَحدَه {bodies.NameOnly} · بِالهاتِف {bodies.Phone} · بِالبَريد {bodies.Email}");
        Assert.Equal((2, 0, 0), bodies);
        foreach (var said in new[] { "اسم وهاتف أو بريد عند التسجيل", "للتوثيق", "ويصل الاسم وحده إلى مديري المتجر في إشعار التسجيل" })
            Assert.Contains(said, item, StringComparison.Ordinal);
        Assert.DoesNotContain("هاتفك أو بريدك", Bare(Ar(StorePrivacyKey)), StringComparison.Ordinal);
    }

    /// <summary>جِسمُ <c>ApiKeyService.AuthenticateAsync</c> بِلا تَعليق — ما يَسأَلُه قَبلَ أَن يُعيدَ الفاعِل.</summary>
    internal static string AuthenticateBody()
    {
        var code = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Api", "ApiKeyService.cs")));
        var at = code.IndexOf("public virtual async Task<ApiKeyAuthResult> AuthenticateAsync(", StringComparison.Ordinal);
        Assert.True(at > 0, "لا AuthenticateAsync — الأَداةُ عَمياء.");
        return code[at..code.IndexOf("public static string? BearerFrom(", at, StringComparison.Ordinal)];
    }

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-29، د52)</b> — كانَ <c>An_integration_key_in_a_deleted_users_name_is_revoked_by_the_deletion_and_no_text_says_it_keeps_acting</c>
    /// (د56): الحَذفُ يُبطِلُ المِفتاحَ ويَبقى اسمُه عَلى الوَثيقَةِ المُبطَلَة تَقولُه النُصوصُ في «سِجِلِّ التَدقيقِ ومَفاتيحِ الرَبط». والآن الإبطالُ نَفسُه يَكتُبُ
    /// «حِسابٌ مَحذوف» (<c>ApiKeyService.RevokeForDeletedActor</c>)، ومِفتاحٌ أُبطِلَ قَبلَ الحَذفِ يُسَمّيه <c>AccountErasure</c> — والفَعّالُ لا يَلمَسُه هذا الأَخيرُ،
    /// فَلا تُكتَبُ وَثيقَةٌ مَرَّتَين. و<c>AuthenticateAsync</c> ما زالَ يَرُدُّ المَحذوفَ بِـ<c>ActorGone</c>.</summary>
    [Fact]
    public void An_integration_key_in_a_deleted_users_name_is_revoked_and_renamed_by_the_deletion_and_no_text_says_it_keeps_acting()
    {
        var field = PersonNameFields["ApiKeyDocument.ActorName"];
        var auth = AuthenticateBody();
        var asksUser = Regex.IsMatch(auth, @"\bLoadAsync<User>\(doc\.ActorUserId") && auth.Contains("ApiKeyRejection.ActorGone", StringComparison.Ordinal);
        var me = Guid.NewGuid();
        var active = new ACommerce.Templates.Customer.Marketplace.Services.Api.ApiKeyDocument { Id = "k1", TenantSlug = "zz", ActorUserId = me, ActorName = "سائِق",
            Status = ACommerce.Templates.Customer.Marketplace.Services.Api.ApiKeyDocument.StatusActive };
        var revoked = new ACommerce.Templates.Customer.Marketplace.Services.Api.ApiKeyDocument { Id = "k2", TenantSlug = "zz", ActorUserId = me, ActorName = "سائِق",
            Status = ACommerce.Templates.Customer.Marketplace.Services.Api.ApiKeyDocument.StatusRevoked };
        var hit = ACommerce.Templates.Customer.Marketplace.Services.Api.ApiKeyService.RevokeForDeletedActor(new[] { active, revoked }, "zz", me, DateTime.UtcNow);
        output.WriteLine($"ApiKeyDocument.ActorName ← {field.Kind}/{field.Said} · أُبطِلَ بِالحَذف: {hit.Count} · يَسأَلُ AuthenticateAsync عَن حِسابِ الفاعِل: {asksUser}");
        Assert.Equal(NameKind.Renamed, field.Kind);
        Assert.Equal(Disclosure.Renamed, field.Said);
        Assert.True(asksUser);
        Assert.Contains("doc.ActorUserId, doc.ActorName", auth, StringComparison.Ordinal);
        Assert.Equal(AccountDeletion.ErasedName, Assert.Single(hit).ActorName);
        Assert.False(AccountErasure.EraseFrom(active, "zz", me));                 // الفَعّالُ — كانَ — لِلإبطال
        Assert.True(AccountErasure.EraseFrom(revoked, "zz", me));                 // والمُبطَلُ سَلَفاً لِلمَحو
        Assert.Equal(AccountDeletion.ErasedName, revoked.ActorName);
        foreach (var k in new[] { StorePrivacyKey, PlatformRetentionKey, RetainedNoteKey })
            Assert.DoesNotContain("يبقى يعمل باسمك", Bare(Text(k)), StringComparison.Ordinal);
        Assert.DoesNotContain("keeps acting in your name", En(PlatformRetentionKey), StringComparison.Ordinal);
    }
}
