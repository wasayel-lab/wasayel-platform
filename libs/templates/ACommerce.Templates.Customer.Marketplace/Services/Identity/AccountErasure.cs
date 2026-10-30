using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Offers;
using ACommerce.Kit.Reviews;
using ACommerce.Kit.Support;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using System.Text.RegularExpressions;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Identity;

/// <summary>
/// <para><b>الحَذفُ يَبلُغُ كُلَّ نُسخَةٍ تُعَدَّل</b> (‏2026-10-29، قَرارُ المالِك د52 (١) «أ»؛ ‏ADR-045 §٦). قَبلَه كانَ <see cref="AccountDeletion.Erase"/> يَمحو
/// وَثيقَةَ <see cref="User"/> وَحدَها، ونُسَخُ الاسمِ خارِجَها (‏16 نُسخَةً مَقيسَةً في <c>AccountDeletionRetentionTests</c> §٣ب) تَبقى. والآن، في مُعامَلَةِ
/// نُقطَةِ الحَذفِ نَفسِها: الاسمُ «حِسابٌ مَحذوف» في صَفَقاتِه (طَرَفاً وفي خَطِّها الزَمَنِيِّ) ومُحادَثاتِه وتَقييماتِه (كَتَبَها أَو كُتِبَت عَنه) وعُروضِه
/// ومُطابَقاتِ إعلاناتِه وتَذاكِرِه ورُدودِه عَلَيها ومَفاتيحِ رَبطٍ مُبطَلَةٍ بِاسمِه، وفي الإشعاراتِ المُخَزَّنَةِ في صَناديقِ غَيرِه؛ ويُمحى عُنوانُ التَوصيلِ
/// وهاتِفُه مِن صَفَقاتٍ بادَرَ بِها (كَتَبَهُما هُو — وعُنوانُ مُشتَرٍ في صَفقَةٍ باعَها لا يُمَسّ: لَيسَ بَيانَه).</para>
///
/// <para><b>وما لا يُعَدَّلُ بِطَبيعَتِه يُقالُ ولا يُكتَبُ فَوقَه</b>: مَجرى الأَحداثِ (‏<c>OfferSubmitted</c> و<c>TicketCreated</c> و<c>TicketReplied</c>) سِجِلٌّ
/// لا يُعادُ كَتبُه ولا تَعرِضُه شاشَة — الشاشاتُ تَقرَأُ اللَقَطاتِ المُمحُوَّة؛ وسِجِلُّ التَدقيقِ (<c>AuditEntry</c>) تاريخٌ يُخفى فيه الاسمُ عِندَ العَرض
/// (<c>AuditLogQueries.HideErasedActors</c>) لا في المَخزَن.</para>
///
/// <para><b>‏(‏2026-10-29، مُراجَعَةُ تَيّارِ S2 الجَولَة ٢) و«لا تَعرِضُه شاشَة» مَقيسٌ لا مَظنون</b>: كانَت صَفحَةُ تَذكِرَةِ الاستوديو تَبني مِن المَجرى (صارَت تَقرَأُ
/// اللَقطَة)، وقَبولُ العَرضِ يَبني مِن مَجراه فَيَنسَخُ الاسمَ ويَقبَلُ عَرضَ مَن حُذِف. فَالعَرضُ يُمحى **في مَجراه** (<see cref="EraseEvents"/>: <c>OffererRenamed</c>،
/// ومَعَه <c>OfferWithdrawn</c> لِلمُعَلَّق — حِسابٌ لا وُجودَ لَه لا يُربَطُ بِرِحلَة)، والقَبولُ يَرُدُّ عَرضَ مَن حُذِفَ قَبلَ هذا (<see cref="OffererGone"/>).
/// والحارِسُ <c>StreamNameReaderTests</c>: لا مَجرى لِنَوعٍ يَحمِلُ اسماً يُمحى يُسَلِّمُ اسمَه إلّا أَن يَحمِلَ المَجرى المَحوَ نَفسَه.</para>
///
/// <para><b>والصَفقَةُ الجارِيَةُ تَمنَعُ الحَذفَ قَبلَ أَيِّ مَحو</b> (<see cref="AccountDeletion.OpenDeal"/>): <see cref="IsOpen"/> — فَعّالَةٌ أَو في نِزاع
/// — لِصَفقَةٍ هُوَ طَرَفُها (مُبادِراً أَو مُقابِلاً). وعَرضٌ وارِدٌ عَلى إعلانِه لَم يَقبَلْه بَعدُ لا يَمنَع: لَيسَ طَرَفاً فيه حَتّى يَقبَل. والسُؤالُ واحِدٌ
/// تَسأَلُه النُقطَةُ وصَفحَةُ الحَذفِ (<see cref="OpenDealsAsync"/>)، فَلا تَرسُمُ الصَفحَةُ استِمارَةً تَرُدُّها النُقطَة (القاعِدَة ١٢).</para>
///
/// <para><b>‏(‏2026-10-30، تَدقيقُ الموجَة ١١ B-PRIV-1 + G-5؛ تَوصيفُه <c>0e95601</c>) وإغلاقُ الحِسابِ يَسحَبُ إعلاناتِه</b> (قَرارُ «أ» F2·١، P1): كانَ إعلانُ
/// البائِعِ المَحذوفِ يَبقى في الواجِهَةِ يُشتَرى ويُعرَضُ عَلَيه، فَتولَدُ صَفقَةٌ مَربوطَةٌ بِحِسابٍ مَمحُوٍّ لا يُنَفِّذُها أَحَد — الحالُ الَّتي بُنِيَ <c>open_deal</c>
/// لِيَمنَعَها. فَفي مُعامَلَةِ الحَذفِ نَفسِها: <c>ListingDeleted</c> لِكُلِّ إعلانٍ قائِمٍ لَه (<see cref="Withdraws"/>)، و<c>OfferRejected</c> لِكُلِّ عَرضٍ مُعَلَّقٍ
/// عَلَيها، وتُعادُ صَفَقاتُ مَسارِ العَرضِ الوارِدَةُ الَّتي لَم يَقبَلْها (<see cref="IncomingUnaccepted"/>) لِتُلغيها النُقطَةُ بَعدَ الحِفظِ بِاسمِ المَنصَّة
/// (<see cref="ClosingCanceller"/>) ويُخبَرَ صاحِبُ كُلٍّ مِنها. ولِحِسابٍ حُذِفَ قَبلَ هذا: <c>CheckoutCart.OwnerGone</c> في بابَي السَلَّةِ والعَرض.</para>
/// </summary>
public static class AccountErasure
{
    /// <summary>صَفقَةٌ لَم تَنتَهِ — فَعّالَة، أَو في نِزاعٍ يَنتَظِرُ الحَلّ. المُكتَمِلَةُ والمُلغاةُ مُنتَهِيَتان.</summary>
    public static bool IsOpen(Deal deal) => deal.Status is DealStatus.Active or DealStatus.Disputed;

    /// <summary>هُوَ طَرَفُ الصَفقَة: مُبادِرُها أَو طَرَفُها المُقابِل.</summary>
    public static bool IsParty(Deal deal, Guid userId) => deal.InitiatorId == userId || deal.CounterpartyId == userId;

    /// <summary>عَدَدُ صَفَقاتِه الجارِيَة — دالَّةٌ نَقِيَّةٌ عَلى ما جُلِب، يَسأَلُها الاستِعلامُ نَفسُه.</summary>
    public static int OpenDeals(IEnumerable<Deal> deals, Guid userId) => deals.Count(d => IsParty(d, userId) && IsOpen(d));

    /// <summary>الاستِعلامُ الواحِدُ لِلصَفَقاتِ الجارِيَة — تَسأَلُه نُقطَةُ الحَذفِ بِجَلسَتِها وصَفحَةُ الحَذفِ بِجَلسَةِ قِراءَة.</summary>
    public static async Task<int> OpenDealsAsync(IQuerySession s, Guid userId, CancellationToken ct = default)
        => OpenDeals(await s.Query<Deal>().Where(d => d.InitiatorId == userId || d.CounterpartyId == userId).ToListAsync(ct), userId);

    /// <summary>اسمٌ لا يَدُلُّ عَلى أَحَد — فارِغ، أَو الافتِراضِيُّ قَبلَ أَن يُكتَبَ اسم، أَو المَمحُوُّ نَفسُه — فَلا يُبحَثُ عَنه في نَصِّ إشعار.
    /// ‏(‏2026-10-29، مُراجَعَةُ تَيّارِ S2 الجَولَة ١) والافتِراضِيّانِ كِلاهُما (<see cref="PlaceholderNames"/>) لا قيمَةُ <c>User</c> وَحدَها: «عُضو جَديد» هُوَ
    /// ما يُخَزَّنُ لِلحِسابِ الجَديدِ بِالهاتِفِ أَو البَريد، وكانَ يُعَدُّ مُعَرِّفاً فَيَكتُبُ حَذفُ حِسابٍ بِه «حِسابٌ مَحذوف» في إشعارِ تَسجيلِ كُلِّ عُضوٍ آخَر.</summary>
    public static bool Identifies(string? name)
        => !string.IsNullOrWhiteSpace(name) && !PlaceholderNames.Contains(name.Trim(), StringComparer.Ordinal)
           && name != AccountDeletion.ErasedName && name.Trim() != "—";

    /// <summary>
    /// <para><b>الأَسماءُ الافتِراضِيَّةُ الَّتي لا تَدُلُّ عَلى أَحَد</b>: قيمَةُ <c>User.FullName</c> الابتِدائِيَّة، واسمُ الحِسابِ الجَديدِ بِالهاتِفِ أَو البَريد
    /// (<c>AuthHandlers.NewMemberNameKey</c> بِالعَرَبِيَّةِ صَراحَةً كَما يُخَزَّن — وهُوَ ما يَسأَلُه <c>ActorName.NeedsName</c>). اسمٌ مِنهُما يَحمِلُه كُلُّ
    /// حِسابٍ لَم يَكتُب اسمَه، فَلا يُبحَثُ عَنه — إلّا في إشعارٍ ثَبَتَ أَنّه لِصاحِبِه بِوَسيلَةِ اتِّصالِه (<see cref="EraseFrom(Notification, Guid, IReadOnlyCollection{string}, IReadOnlyCollection{string}, IReadOnlyCollection{string}, IReadOnlySet{Guid})"/>).</para>
    /// </summary>
    public static IReadOnlyList<string> PlaceholderNames { get; } = new[]
        {
            new User().FullName,
            ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, ACommerce.Kit.Auth.Server.AuthHandlers.NewMemberNameKey),
        }
        .Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// <para><b>وَسيلَتا اتِّصالِه قَبلَ <c>Erase</c></b> (‏2026-10-29، مُراجَعَةُ تَيّارِ S2 الجَولَة ١؛ قَرارُ المالِك د53 (٤): «بلا اسم ولا وسيلة اتّصال»): الهاتِفُ
    /// والبَريدُ كَما خُزِّنا — وهُما ما كَتَبَه إشعارُ التَسجيلِ قَبلَ <c>548ea4d</c> («{الاسم} · {الهاتِف}» و«… · {البَريد}»)، فَتُنزَعانِ مِن نُسَخِه في صَناديقِ
    /// المُديرين. ويُؤخَذانِ قَبلَ <c>Erase</c> لِأَنّها تُفرِغُهُما.</para>
    /// </summary>
    public static IReadOnlyList<string> ContactsOf(User user)
        => new[] { user.Phone, user.Email }
            .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.Ordinal).ToList();

    // حَدّا الوَسيلَةِ في النَصّ: لا حَرفَ ولا رَقَمَ ولا رَمزَ عُنوانٍ قَبلَها أَو بَعدَها — فَهاتِفٌ لا يُطابِقُ داخِلَ رَقَمٍ أَطوَل، وبَريدٌ لا يُطابِقُ داخِلَ بَريدٍ أَطوَل،
    // ونُقطَةُ آخِرِ الجُملَةِ بَعدَه لا تَمنَع.
    private const string ContactBefore = @"(?<![\p{L}\p{N}@._+\-])";
    private const string ContactAfter = @"(?![\p{L}\p{N}@_+\-]|\.[\p{L}\p{N}])";

    /// <summary>هَل يَحمِلُ النَصُّ هذِه الوَسيلَةَ بِعَينِها؟</summary>
    public static bool Carries(string? text, string contact)
        => !string.IsNullOrEmpty(text) && Regex.IsMatch(text, ContactBefore + Regex.Escape(contact) + ContactAfter);

    /// <summary>تُنزَعُ الوَسيلَةُ وفاصِلُها «·» قَبلَها إن كانَ — كَما كَتَبَها إشعارُ التَسجيل.</summary>
    private static string StripContact(string text, string contact)
    {
        var stripped = Regex.Replace(text, @"(?:\s*·\s*)?" + ContactBefore + Regex.Escape(contact) + ContactAfter, "");
        return stripped == text ? text : stripped.Trim();
    }

    // ─── الأَثَرُ عَلى كُلِّ نَوع — دالّاتٌ نَقِيَّةٌ تُعيدُ «تَغَيَّرَ أَم لا» ─────────────────────────────────

    public static bool EraseFrom(Deal d, Guid userId)
    {
        var changed = false;
        if (d.InitiatorId == userId)
        {
            changed |= Set(d.InitiatorName, v => d.InitiatorName = v);
            changed |= d.Attributes.Remove(Deal.DeliveryAddressAttribute);
            changed |= d.Attributes.Remove(Deal.DeliveryPhoneAttribute);
        }
        if (d.CounterpartyId == userId) changed |= Set(d.CounterpartyName, v => d.CounterpartyName = v);
        for (var i = 0; i < d.Timeline.Count; i++)
            if (d.Timeline[i].ActorId == userId && d.Timeline[i].ActorName != AccountDeletion.ErasedName)
            {
                d.Timeline[i] = d.Timeline[i] with { ActorName = AccountDeletion.ErasedName };
                changed = true;
            }
        return changed;
    }

    public static bool EraseFrom(Conversation c, Guid userId)
        => (c.OwnerId == userId && Set(c.OwnerName, v => c.OwnerName = v))
         | (c.PartnerId == userId && Set(c.PartnerName, v => c.PartnerName = v));

    public static bool EraseFrom(Review r, Guid userId)
        => (r.AuthorId == userId && Set(r.AuthorName, v => r.AuthorName = v))
         | (r.TargetUserId == userId && Set(r.TargetUserName, v => r.TargetUserName = v));

    public static bool EraseFrom(Offer o, Guid userId) => o.OffererId == userId && Set(o.OffererName, v => o.OffererName = v);

    /// <summary>
    /// <para><b>عَرضُه يُمحى في مَجراه</b> (‏2026-10-29، مُراجَعَةُ تَيّارِ S2 الجَولَة ٢): ما يُلحَقُ بِمَجرى عَرضِه في مُعامَلَةِ الحَذف — اسمُه «حِسابٌ مَحذوف»
    /// (<c>OffererRenamed</c> بِما يَكتُبُه <see cref="EraseFrom(Offer, Guid)"/>)، و<c>OfferWithdrawn</c> إن كانَ مُعَلَّقاً. ولا شَيءَ لِعَرضِ غَيرِه.</para>
    /// <para><b>ولِماذا لا <c>Store</c> كَبَقِيَّةِ النُسَخ</b>: اللَقطَةُ إسقاطٌ مُباشِر، وسَحبُ المُعَلَّقِ حَدَثٌ في مَجراه — وحَدَثٌ ولَقطَةٌ مُخَزَّنَةٌ يَدَوِيّاً في جَلسَةٍ
    /// واحِدَةٍ يَكتُبُ فيهِما الإسقاطُ فَوقَ المُخَزَّنِ مِن المَخزَن فَيَعودُ الاسم (مَقيسٌ عَلى Postgres، بِأَيِّ تَرتيب).</para>
    /// </summary>
    public static IReadOnlyList<object> EraseEvents(Offer o, Guid userId, DateTime at)
    {
        if (o.OffererId != userId) return Array.Empty<object>();
        var events = new List<object>();
        if (EraseFrom(o, userId)) events.Add(new OffererRenamed(o.Id, o.OffererName, at));
        if (o.Status == OfferStatus.Pending) events.Add(new OfferWithdrawn(o.Id, at));
        return events;
    }

    /// <summary>رَمزُ رَدِّ القَبولِ حينَ يَكونُ صاحِبُ العَرضِ قَد حَذَفَ حِسابَه — تَقرَؤُه صَفحَةُ الإعلان (<c>listings.detail.err_offer_gone</c>).</summary>
    public const string OffererGoneCode = "offer_gone";

    /// <summary>صاحِبُ العَرضِ حَذَفَ حِسابَه (أَو لا حِسابَ لَه) — فَلا يُقبَلُ عَرضُه: عَرضٌ مُعَلَّقٌ مِن حَذفٍ سَبَقَ <see cref="EraseEvents"/> لا يُنشِئُ مُطابَقَةً
    /// ولا مُحادَثَةً مَعَ حِسابٍ لا وُجودَ لَه.</summary>
    public static bool OffererGone(User? offerer) => offerer is null || offerer.DeletedAt is not null;

    /// <summary>إعلانٌ يَسحَبُه حَذفُ حِسابِه (‏2026-10-30، B-PRIV-1): قائِمٌ لَم يُحذَف، ومالِكُه هُوَ بِالمِفتاحِ الَّذي يَقرَؤُه كُلُّ بابٍ يَسأَلُ المِلكِيَّة
    /// (<see cref="ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingEditService.IsOwnedBy"/>).</summary>
    public static bool Withdraws(Listing listing, Guid userId)
        => !listing.IsDeleted && ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingEditService.IsOwnedBy(listing, userId);

    /// <summary>صَفقَةُ مَسارِ العَرضِ الوارِدَةُ عَلى إعلانِه ولَم يَقبَلْها (‏2026-10-30، B-PRIV-1): فَعّالَةٌ، ومَرجِعُ مالِكِها هُو، ولَيسَ طَرَفاً فيها — فَلا يَمنَعُ
    /// حَذفَه (<see cref="IsOpen"/> لِلطَرَفِ وَحدَه) ويُلغيها بَعدَه، بَدَلَ أَن تَبقى مَربوطَةً بِحِسابٍ لا يَقبَلُ ولا يَرفُض.</summary>
    public static bool IncomingUnaccepted(Deal deal, Guid userId)
        => deal.Status == DealStatus.Active && DealAcceptAuthorization.OwnerIdOf(deal) == userId && !IsParty(deal, userId);

    /// <summary>مُلغي تِلكَ الصَفَقاتِ — المَنصَّةُ لا شَخص (بِلا مُعَرِّفٍ وبِاسمِ <see cref="DealPatternCatalog.PlatformActor"/>، فَتُسَمّيه صَفحَتا الصَفقَةِ مِن
    /// القامُوس)، وبِسُلطَةِ صاحِبِ المَتجَرِ في <see cref="DealCancelAuthorization.Validate"/> — نَسَقُ <c>GatewayPayments.ExpiryCanceller</c>.</summary>
    public static readonly DealCanceller ClosingCanceller = new(Guid.Empty, DealPatternCatalog.PlatformActor, IsStoreAdmin: true);

    public static bool EraseFrom(ListingMatch m, Guid userId) => m.OffererId == userId && Set(m.OffererName, v => m.OffererName = v);

    /// <summary>تَذكِرَتُه: اسمُه كاتِباً، واسمُه عَلى رُدودِه هُو (<c>FromStaff = false</c>) — ورَدُّ الفَريقِ يَبقى بِتَوقيعِه.</summary>
    public static bool EraseFrom(Ticket t, Guid userId)
    {
        if (t.AuthorId != userId) return false;
        var changed = Set(t.AuthorName, v => t.AuthorName = v);
        foreach (var r in t.Replies.Where(r => !r.FromStaff)) changed |= Set(r.AuthorName, v => r.AuthorName = v);
        return changed;
    }

    /// <summary>مِفتاحُ رَبطٍ بِاسمِه أُبطِلَ قَبلَ الحَذف — والفَعّالُ يُبطِلُه ويُسَمّيه <c>ApiKeyService.RevokeForDeletedActor</c> في المُعامَلَةِ نَفسِها.</summary>
    public static bool EraseFrom(ApiKeyDocument k, string tenantSlug, Guid userId)
        => string.Equals(k.TenantSlug, tenantSlug, StringComparison.Ordinal) && k.ActorUserId == userId
           && !string.Equals(k.Status, ApiKeyDocument.StatusActive, StringComparison.Ordinal)
           && Set(k.ActorName, v => k.ActorName = v);

    /// <summary>
    /// <para><b>إشعارٌ في صُندوقِ غَيرِه يَحمِلُ اسمَه</b>: يُستَبدَلُ الاسمُ في العُنوانِ والمَتنِ — إن كانَ الإشعارُ عَن شَيءٍ يَخُصُّه (رابِطُه صَفقَةٌ أَو
    /// مُحادَثَةٌ أَو إعلانٌ مِن <paramref name="relatedUrls"/>). والحَصرُ بِالرابِطِ لِكَي لا يُمَسَّ اسمُ شَخصٍ آخَرَ يُشبِهُ اسمَه في إشعارٍ لا عَلاقَةَ لَه بِه.
    /// ‏(2026-10-29، قَرارُ المالِك د40) والأَسماءُ كُلُّها — اسمُه واسمُ نَشاطِه (<c>ActorName.NamesOf</c>)، الأَطوَلُ أَوَّلاً.</para>
    ///
    /// <para><b>‏(2026-10-29، مُراجَعَةُ تَيّارِ S2 الجَولَة ١) ووَسيلَةُ اتِّصالِه تَربِطُ إشعارَ التَسجيلِ بِهُوِيَّتِه</b>: إشعارُ <c>new_user</c> قَبلَ <c>548ea4d</c>
    /// مَتنُه «{الاسم} · {الهاتِف}» أَو «… · {البَريد}» والاسمُ يَومَئِذٍ الافتِراضِيّ — فَإشعارُ مُديرينَ يَحمِلُ وَسيلَتَه (<see cref="Carries"/>) تُنزَعُ مِنه الوَسيلَةُ
    /// وفاصِلُها؛ وإن كانَ إشعارَ تَسجيلٍ فَهُوَ إشعارُه بِعَينِه: الافتِراضِيُّ في مَتنِه «حِسابٌ مَحذوف»، والعُنوانُ الثابِتُ كَما هُو.</para>
    ///
    /// <para><b>‏(2026-10-29، مُراجَعَةُ تَيّارِ S2 الجَولَة ٢) ولا إشعارَ مُديرينَ «عَنه» بِنَوعِه</b>: كانَ كُلُّ إشعارِ <c>new_user</c>/<c>report</c> يُعَدُّ عَنه، وكُلُّ اسمٍ
    /// حَقيقِيٍّ يُستَبدَلُ نَصّاً داخِلَ نَصّ — فَحَذفُ «عَلي» كَتَبَ «حِسابٌ مَحذوف» في بَلاغِ «عَلي حَسَن» وفي بَلاغِ سَمِيِّه، وقَطَعَ «عَلِيّ» مِن «عَلِيّان». والآن:
    /// إشعارُ المُديرينَ عَنه بِهُوِيَّتِه وَحدَها — وَسيلَتُه (أَعلاه)، أَو بَلاغُه هُو (<paramref name="ownReports"/>: يُعلِنُ تَذكِرَةَ بَلاغٍ كَتَبَها،
    /// <see cref="ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingReport.Announces"/>)؛ وبَلاغُ غَيرِه يَقتَبِسُ وَسيلَتَه تُنزَعُ مِنه الوَسيلَةُ وَحدَها.
    /// والاسمُ يُستَبدَلُ كَلِمَةً تامَّةً لا جُزءَ كَلِمَة (<see cref="ReplaceWhole"/>).</para>
    /// </summary>
    public static bool EraseFrom(Notification n, Guid userId, IReadOnlyCollection<string> oldNames, IReadOnlyCollection<string> relatedUrls,
        IReadOnlyCollection<string> contacts, IReadOnlySet<Guid> ownReports)
    {
        if (n.UserId == userId) return false;
        var names = oldNames.Where(Identifies).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).OrderByDescending(x => x.Length).ToList();
        var marks = contacts.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim())
            .Distinct(StringComparer.Ordinal).OrderByDescending(c => c.Length).ToList();
        var linked = n.RelatedUrl is { } url && relatedUrls.Any(r => url.EndsWith(r, StringComparison.Ordinal));
        var reported = ownReports.Contains(n.Id);
        var byContact = AdminNoticeTypes.Contains(n.Type) && marks.Any(c => Carries(n.Title, c) || Carries(n.Body, c));
        var registration = byContact && n.Type == NewUserNoticeType;
        if (!linked && !reported && !byContact) return false;
        var (title, body) = (n.Title, n.Body);
        if (byContact)
        {
            foreach (var c in marks) { title = StripContact(title, c); body = StripContact(body, c); }
            if (registration)
            {
                foreach (var p in PlaceholderNames.OrderByDescending(x => x.Length)) body = ReplaceWhole(body, p);
                if (string.IsNullOrWhiteSpace(body)) body = AccountDeletion.ErasedName;
            }
        }
        if (linked || reported || registration)
            foreach (var name in names) { title = ReplaceWhole(title, name); body = ReplaceWhole(body, name); }
        if (title == n.Title && body == n.Body) return false;
        (n.Title, n.Body) = (title, body);
        return true;
    }

    // حَدّا الاسمِ في النَصّ: لا حَرفَ ولا حَرَكَةَ ولا رَقَمَ قَبلَه أَو بَعدَه — فَـ«عَلِيّ» لا يُطابِقُ داخِلَ «عَلِيّان»، ولا «عَلي» داخِلَ «عَليٌّ».
    private const string NameBefore = @"(?<![\p{L}\p{M}\p{N}])";
    private const string NameAfter = @"(?![\p{L}\p{M}\p{N}])";

    /// <summary>الاسمُ كَلِمَةً تامَّةً يَصيرُ «حِسابٌ مَحذوف» — لا جُزءَ كَلِمَةٍ أَطوَل.</summary>
    public static string ReplaceWhole(string text, string name)
        => string.IsNullOrEmpty(text) || string.IsNullOrEmpty(name) ? text
         : Regex.Replace(text, NameBefore + Regex.Escape(name) + NameAfter, _ => AccountDeletion.ErasedName);

    /// <summary>نَوعُ إشعارِ التَسجيلِ لِمُديري المَتجَر — «{الاسم}» (وقَبلَ <c>548ea4d</c> «{الاسم} · {الوَسيلَة}»).</summary>
    public const string NewUserNoticeType = "new_user";

    /// <summary>إشعاراتُ مُديري المَتجَرِ الَّتي تُسَمّي العُضوَ في مَتنِها — كاتِباها في <c>MarketplaceTemplateExtensions</c> (التَسجيلُ والبَلاغ).</summary>
    public static readonly IReadOnlySet<string> AdminNoticeTypes = new HashSet<string>(StringComparer.Ordinal)
        { NewUserNoticeType, ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingReport.NoticeType };

    private static bool Set(string? current, Action<string> set)
    {
        if (current == AccountDeletion.ErasedName) return false;
        set(AccountDeletion.ErasedName);
        return true;
    }

    /// <summary>ما مُحِيَ في كُلِّ نَوع — يُطبَعُ في البُرهانِ الحَيّ.</summary>
    public sealed record Counts(int Deals, int Conversations, int Reviews, int Offers, int Matches, int Tickets, int Keys, int Notifications)
    {
        public int Total => Deals + Conversations + Reviews + Offers + Matches + Tickets + Keys + Notifications;

        /// <summary>‏(‏2026-10-30، B-PRIV-1) إعلاناتُه المَسحوبَة (<c>ListingDeleted</c>)، والعُروضُ المُعَلَّقَةُ المَرفوضَةُ عَلَيها (<c>OfferRejected</c>) — في المُعامَلَةِ نَفسِها.</summary>
        public int Listings { get; init; }
        public int RejectedOffers { get; init; }

        /// <summary>صَفَقاتُ مَسارِ العَرضِ الوارِدَةُ الَّتي لَم يَقبَلْها (<see cref="IncomingUnaccepted"/>) — تُلغيها النُقطَةُ بَعدَ الحِفظ (<c>DealsService.CancelAsync</c> تَفتَحُ
        /// جَلسَتَها وتَحفَظ) ويُخبَرُ صاحِبُ كُلٍّ مِنها. لا تُعَدُّ في <see cref="Total"/>: لَم تُكتَب هُنا.</summary>
        public IReadOnlyList<Guid> IncomingDeals { get; init; } = Array.Empty<Guid>();
    }

    /// <summary>
    /// <para><b>الأَثَرُ في الجَلسَة — بِلا حِفظ</b>: يَقرَأُ ما يَذكُرُه بِمُعَرِّفِه ويُعَلِّمُ المُتَغَيِّرَ لِلحِفظِ (<c>Store</c>)، والحِفظُ الواحِدُ لِلنُقطَةِ بَعدَه —
    /// فَالمَحوُ ووَثيقَةُ الحِسابِ ومَفاتيحُه في مُعامَلَةٍ واحِدَة: يَقَعُ كُلُّه أَو لا شَيء. و<paramref name="oldNames"/> أَسماؤُه قَبلَ <c>Erase</c> (‏د40: اسمُه واسمُ
    /// نَشاطِه، <c>ActorName.NamesOf</c>)، و<paramref name="contacts"/> وَسيلَتاه قَبلَه (<see cref="ContactsOf"/> — مُراجَعَةُ تَيّارِ S2 الجَولَة ١).</para>
    /// <para><b>‏(2026-10-29، مُراجَعَةُ تَيّارِ S2 الجَولَة ٢) وصيغَةٌ واحِدَة</b>: كانَت ثَلاثاً تُحيلُ الأولَيانِ مِنها إلى الثالِثَةِ بِوَسيطٍ فارِغ، ولا مُستَهلِكَ لَهُما
    /// إلّا الاختِبار (القاعِدَة ١) — وتَسمَحانِ لِنِداءٍ قادِمٍ بِأَن يَتَخَطّى الوَسيلَتَينِ صامِتاً. فَالوَسيلَتانِ وَسيطٌ إلزامِيّ.</para>
    /// </summary>
    public static async Task<Counts> EraseCopiesAsync(IDocumentSession s, string tenantSlug, Guid userId, IReadOnlyCollection<string> oldNames,
        IReadOnlyCollection<string> contacts, CancellationToken ct = default)
    {
        int Mark<T>(IEnumerable<T> rows, Func<T, bool> erase) where T : notnull
        {
            var n = 0;
            foreach (var row in rows) if (erase(row)) { s.Store(row); n++; }
            return n;
        }
        // العُروضُ بِأَحداثٍ في مَجاريها لا بِـ`Store` (<see cref="EraseEvents"/>) — والإسقاطُ المُباشِرُ يَكتُبُ لَقطَتَها في الحِفظِ نَفسِه.
        int Withdraw(IEnumerable<Offer> rows)
        {
            var n = 0;
            var at = DateTime.UtcNow;
            foreach (var o in rows)
                if (EraseEvents(o, userId, at) is { Count: > 0 } events) { s.Events.Append(o.Id, events.ToArray()); n++; }
            return n;
        }

        var deals = await s.Query<Deal>().Where(d => d.InitiatorId == userId || d.CounterpartyId == userId).ToListAsync(ct);
        var dealIds = deals.Select(d => d.Id).ToHashSet();
        // وخُطوَةٌ فَعَلَها في صَفقَةٍ لَيسَ طَرَفَها (فاعِلٌ بِمِفتاحِ رَبطٍ مَثَلاً) — اسمُه عَلى سَطرِ خَطِّها الزَمَنِيّ.
        var others = (await s.Query<Deal>().Where(d => d.Timeline.Any(e => e.ActorId == userId)).ToListAsync(ct))
            .Where(d => !dealIds.Contains(d.Id));
        var convs = await s.Query<Conversation>().Where(c => c.OwnerId == userId || c.PartnerId == userId).ToListAsync(ct);
        var reviews = await s.Query<Review>().Where(r => r.AuthorId == userId || r.TargetUserId == userId).ToListAsync(ct);
        var offers = await s.Query<Offer>().Where(o => o.OffererId == userId).ToListAsync(ct);
        var matches = await s.Query<ListingMatch>().Where(m => m.OffererId == userId).ToListAsync(ct);
        var tickets = await s.Query<Ticket>().Where(t => t.AuthorId == userId).ToListAsync(ct);
        var keys = await s.Query<ApiKeyDocument>().Where(k => k.TenantSlug == tenantSlug && k.ActorUserId == userId).ToListAsync(ct);

        var urls = deals.Select(d => $"/deals/{d.Id}")
            .Concat(convs.Select(c => $"/chats/{c.Id}"))
            .Concat(matches.Select(m => $"/listings/{m.Id}"))
            .ToList();
        var notices = new List<Notification>();
        // ‏(2026-10-29، مُراجَعَةُ تَيّارِ S2 الجَولَة ٢) بَلاغاتُه بِتَذاكِرِه لا بِنَصِّ اسمِه: كُلُّ تَذكِرَةِ بَلاغٍ كَتَبَها — بِاسمِه يَومَئِذٍ، قَبلَ مَحوِها أَدناه — وإشعاراتُ
        // المُديرينَ الَّتي تُعلِنُها (`ListingReport.Announces`: بَعدَها وقَبلَ التالِيَةِ بِالمَوضوعِ نَفسِه في المَتجَر).
        var ownReports = new HashSet<Guid>();
        var reportNames = new List<string>();
        foreach (var t in tickets.Where(t => ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingReport.ReasonOf(t.Subject) is not null).ToList())
        {
            var reason = ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingReport.ReasonOf(t.Subject)!;
            var title = ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingReport.NoticeTitle(reason);
            var body = ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingReport.NoticeBody(t.AuthorName);
            var (subject, at) = (t.Subject, t.CreatedAt);
            // المُقارَنَةُ بِالوَقتِ في الذاكِرَة لا في الاستِعلام: Npgsql يَرفُضُ `DateTime` بِـUTC وَسيطاً لِحَقلٍ مِن وَثيقَة (مَقيسٌ حَيّاً: InvalidUtcDateTimeUsageException)،
            // والمُرَشَّحونَ قَليل — تَذاكِرُ بِالمَوضوعِ نَفسِه، وإشعاراتٌ بِالعُنوانِ والمَتنِ نَفسَيهِما.
            var next = (await s.Query<Ticket>().Where(x => x.Subject == subject).ToListAsync(ct))
                .Where(x => x.CreatedAt > at).Select(x => (DateTime?)x.CreatedAt).OrderBy(x => x).FirstOrDefault();
            foreach (var n in await s.Query<Notification>().Where(n => n.UserId != userId && n.Title == title && n.Body == body).ToListAsync(ct))
                if (ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingReport.Announces(n, t, next))
                {
                    ownReports.Add(n.Id);
                    if (notices.All(x => x.Id != n.Id)) notices.Add(n);
                }
            reportNames.Add(t.AuthorName);
        }
        var names = oldNames.Concat(reportNames).ToList();
        // كُلُّ اسمٍ بِاستِعلامِه، والنَتائِجُ وَثيقَةٌ واحِدَةٌ لِكُلِّ مُعَرِّف — فَلا تُخَزَّنُ نُسخَتانِ مِن إشعارٍ يَحمِلُ الاسمَين.
        foreach (var oldName in oldNames.Where(Identifies).Distinct(StringComparer.Ordinal))
            foreach (var n in await s.Query<Notification>().Where(n => n.UserId != userId && (n.Title.Contains(oldName) || n.Body.Contains(oldName))).ToListAsync(ct))
                if (notices.All(x => x.Id != n.Id)) notices.Add(n);
        // ووَسيلَتا اتِّصالِه — والحَصرُ في إشعاراتِ المُديرينَ وحُدودِ الوَسيلَةِ لِـ`EraseFrom` لا لِلاستِعلام.
        foreach (var contact in contacts.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.Ordinal))
            foreach (var n in await s.Query<Notification>().Where(n => n.UserId != userId && (n.Title.Contains(contact) || n.Body.Contains(contact))).ToListAsync(ct))
                if (notices.All(x => x.Id != n.Id)) notices.Add(n);

        // ‏(‏2026-10-30، B-PRIV-1) إعلاناتُه تُسحَبُ بِمَجاريها والعُروضُ المُعَلَّقَةُ عَلَيها تُرفَضُ بِحَدَثِ القَبولِ نَفسِه لِأَخَواتِه (`OfferRejected`)، والوارِدَةُ الَّتي
        // لَم يَقبَلْها تُعادُ لِلنُقطَة. و`Attributes` لا يُرَشِّحُها Marten في LINQ (`AccountQueries.MyListingsAsync`)، فَالمالِكُ يُرَشَّحُ في الذاكِرَة.
        var closedAt = DateTime.UtcNow;
        var withdrawn = (await s.Query<Listing>().Where(l => !l.IsDeleted).ToListAsync(ct)).Where(l => Withdraws(l, userId)).Select(l => l.Id).ToHashSet();
        foreach (var id in withdrawn) s.Events.Append(id, new ListingDeleted(id, closedAt));
        var rejected = (await s.Query<Offer>().Where(o => o.Status == OfferStatus.Pending && o.OffererId != userId).ToListAsync(ct))
            .Where(o => withdrawn.Contains(o.ListingId)).ToList();
        foreach (var o in rejected) s.Events.Append(o.Id, new OfferRejected(o.Id, closedAt));
        var incoming = (await s.Query<Deal>().Where(d => d.Status == DealStatus.Active).ToListAsync(ct))
            .Where(d => IncomingUnaccepted(d, userId)).Select(d => d.Id).ToList();

        return new Counts(
            Deals: Mark(deals.Concat(others), d => EraseFrom(d, userId)),
            Conversations: Mark(convs, c => EraseFrom(c, userId)),
            Reviews: Mark(reviews, r => EraseFrom(r, userId)),
            Offers: Withdraw(offers),
            Matches: Mark(matches, m => EraseFrom(m, userId)),
            Tickets: Mark(tickets, t => EraseFrom(t, userId)),
            Keys: Mark(keys, k => EraseFrom(k, tenantSlug, userId)),
            Notifications: Mark(notices, n => EraseFrom(n, userId, names, urls, contacts, ownReports)))
        {
            Listings = withdrawn.Count,
            RejectedOffers = rejected.Count,
            IncomingDeals = incoming,
        };
    }
}
