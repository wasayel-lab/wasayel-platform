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
/// اللَقطَة)، وقَبولُ العَرضِ يَبني مِن مَجراه فَيَنسَخُ الاسمَ ويَقبَلُ عَرضَ مَن حُذِف. فَالعَرضُ يُمحى **في مَجراه** (<see cref="EraseEvents(Offer, Guid, DateTime)"/>: <c>OffererRenamed</c>،
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
    /// <summary>
    /// <para><b>صَفحَتا صاحِبِ البَياناتِ في مَتجَرِه</b> (‏2026-11-04، الموجَة ١٢ G1 · ٦ — XS-3؛ تَوصيفُه <c>365dfdc</c>): «بَياناتي» و«حَذفُ الحِساب» —
    /// <c>/{slug}/me/data</c> و<c>/{slug}/me/delete</c> وبِبادِئَةِ الدَور <c>/{slug}/r/{role}/…</c>. يَسأَلُها الشِلُّ (<c>MainLayout</c>) فَلا يُخفيهِما في مَتجَرٍ مُعَلَّق:
    /// الاطِّلاعُ والتَنزيلُ والحَذفُ أَبوابٌ يَفتَحُها صاحِبُها فَتُنَفَّذُ دونَ انتِظار (د53) — التِزامُ وَسايِل لا خِيارُ باقَةِ المَتجَر (P1). نَقِيَّة.</para>
    /// </summary>
    public static bool OwnDataPage(string slug, string path)
    {
        var p = path.TrimEnd('/');
        var root = $"/{slug}/";
        if (!p.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
        var rest = p[root.Length..].Split('/');
        if (rest.Length == 4 && rest[0].Equals("r", StringComparison.OrdinalIgnoreCase) && rest[1].Length > 0) rest = rest[2..];
        return rest.Length == 2 && rest[0].Equals("me", StringComparison.OrdinalIgnoreCase)
               && (rest[1].Equals("data", StringComparison.OrdinalIgnoreCase) || rest[1].Equals("delete", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>صَفقَةٌ لَم تَنتَهِ — فَعّالَة، أَو في نِزاعٍ يَنتَظِرُ الحَلّ. المُكتَمِلَةُ والمُلغاةُ مُنتَهِيَتان.</summary>
    public static bool IsOpen(Deal deal) => deal.Status is DealStatus.Active or DealStatus.Disputed;

    /// <summary>هُوَ طَرَفُ الصَفقَة: مُبادِرُها أَو طَرَفُها المُقابِل.</summary>
    public static bool IsParty(Deal deal, Guid userId) => deal.InitiatorId == userId || deal.CounterpartyId == userId;

    /// <summary>عَدَدُ صَفَقاتِه الجارِيَة — دالَّةٌ نَقِيَّةٌ عَلى ما جُلِب، يَسأَلُها الاستِعلامُ نَفسُه.</summary>
    public static int OpenDeals(IEnumerable<Deal> deals, Guid userId) => deals.Count(d => IsParty(d, userId) && IsOpen(d));

    /// <summary>الاستِعلامُ الواحِدُ لِلصَفَقاتِ الجارِيَة — تَسأَلُه نُقطَةُ الحَذفِ بِجَلسَتِها وصَفحَةُ الحَذفِ بِجَلسَةِ قِراءَة.</summary>
    public static async Task<int> OpenDealsAsync(IQuerySession s, Guid userId, CancellationToken ct = default)
        => OpenDeals(await s.Query<Deal>().Where(d => d.InitiatorId == userId || d.CounterpartyId == userId).ToListAsync(ct), userId);

    // ─── رِحلَةٌ جارِيَة (‏2026-10-30، الموجَة ١٢ F2 · ٣ — F2B-1؛ تَوصيفُه 7c4dad0؛ قَرارُ «أ»: د52 (١) مُطَبَّقاً) ─────────────────────────────

    /// <summary>
    /// <para><b>رِحلَةٌ حَيَّة</b>: مُطابَقَةٌ فَعّالَةٌ (<see cref="TripStatus.Active"/>) عَلى إعلانِها القائِم. ومُطابَقَةٌ عَلى إعلانٍ مَحذوفٍ — حَذفٌ صَريحٌ أَو حَذفُ حِسابٍ قَبلَ
    /// هذا — لا رِحلَةَ فيها: صَفحَتُه تَرسُمُ «حُذِف» وَحدَه بِلا استِمارَةِ إنهاءٍ ولا إلغاء، فَلا تَمنَعُ عَرضاً جَديداً ولا حَذفاً. نَقِيَّة.</para>
    /// </summary>
    public static bool LiveTrip(ListingMatch match, Listing? listing)
        => match.Status == TripStatus.Active && listing is { IsDeleted: false } && listing.Id == match.Id;

    /// <summary>رِحلَتُه الجارِيَة: <see cref="LiveTrip"/>، وهُوَ سائِقُها (<c>OffererId</c>) أَو مالِكُ إعلانِها بِالمِفتاحِ الَّذي يَقرَؤُه كُلُّ بابٍ يَسأَلُ المِلكِيَّة.</summary>
    public static bool OpenTrip(ListingMatch match, Listing? listing, Guid userId)
        => LiveTrip(match, listing)
           && (match.OffererId == userId || ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingEditService.IsOwnedBy(listing!, userId));

    /// <summary>الاستِعلامُ الواحِدُ لِرِحلاتِه الجارِيَة، أَحدَثُها أَوَّلاً — تَسأَلُه نُقطَةُ الحَذفِ بِجَلسَتِها وصَفحَةُ الحَذفِ بِجَلسَةِ قِراءَة (<c>UserDataQueries.OpenTripsAsync</c>).
    /// المُطابَقاتُ الفَعّالَةُ في المَتجَرِ وإعلاناتُها، والقَرارُ لِـ<see cref="OpenTrip"/> في الذاكِرَة: المالِكُ في <c>Attributes</c> لا يُرَشِّحُه Marten.</summary>
    public static async Task<IReadOnlyList<ListingMatch>> OpenTripsAsync(IQuerySession s, Guid userId, CancellationToken ct = default)
    {
        var active = await s.Query<ListingMatch>().Where(m => m.Status == TripStatus.Active).ToListAsync(ct);
        if (active.Count == 0) return Array.Empty<ListingMatch>();
        var listings = (await s.LoadManyAsync<Listing>(ct, active.Select(m => m.Id).Distinct().ToArray())).ToDictionary(l => l.Id);
        return active.Where(m => OpenTrip(m, listings.GetValueOrDefault(m.Id), userId)).OrderByDescending(m => m.MatchedAt).ToList();
    }

    // ─── ثَمَنٌ نَقدِيٌّ يَرُدُّه البائِع (‏2026-10-30، تَدقيقُ الموجَة ١١ G-7؛ تَوصيفُه 228b1be) ───────────────────────────────────────────────

    /// <summary>أَسطُرُ المالِ في سِجِلِّ الصَفقَة — ما يَكتُبُه القَبضُ والاستِرجاعُ والإلغاءُ عَن مال؛ وآخِرُها حالُ المالِ الآن.</summary>
    private static readonly HashSet<string> MoneyLines = new(StringComparer.Ordinal)
    {
        DealEvent.CashOnDelivery, DealEvent.CashRefundDue, DealEvent.GatewayPaid, DealEvent.GatewayRefundDue, DealEvent.GatewayInvoiceLeftOpen,
        DealPaymentLines.Captured, DealPaymentLines.CaptureFailed, DealPaymentLines.Refunded, DealPaymentLines.RefundFailed,
    };

    /// <summary>
    /// <para><b>صَفقَةٌ أُلغِيَت ولِلمُشتَري عَلى البائِعِ ثَمَنٌ نَقدِيٌّ يَرُدُّه</b> (قَرارُ «أ» F2·٧، P1+P2): المُشتَري (<c>InitiatorId</c>) هُوَ صاحِبُ الحِساب، والصَفقَةُ
    /// <see cref="DealStatus.Cancelled"/>، وآخِرُ سَطرِ مالٍ في سِجِلِّها <see cref="DealEvent.CashRefundDue"/>. والآخِرُ لا الأَوَّل كَـ<c>DealNotices.OwnerActionOf</c>:
    /// سَطرُ مالٍ بَعدَه يَقولُ حالاً أَحدَث. وما رُدَّ إلى البِطاقَةِ (<see cref="DealEvent.GatewayRefundDue"/>، <c>refunded</c>) لا يُحتاجُ فيه إلى وَسيلَةِ اتِّصال. دالَّةٌ نَقِيَّة.</para>
    /// <para><b>ولِماذا تَحذيرٌ لا مَنع</b>: لا كاتِبَ لِـ«رُدَّ النَقد» (S3 OI1)، فَمَنعٌ بِه لا يُرفَعُ أَبَداً — يَحبِسُ صاحِبَ الحِسابِ في حِسابٍ لا يَستَطيعُ حَذفَه.
    /// فَتَرسُمُ صَفحَةُ الحَذفِ كُلَّ صَفقَةٍ مِنها (البائِع، السِلعَة، المَبلَغ) بِجُملَةِ أَنّ الحَذفَ يُزيلُ الوَسيلَةَ الَّتي يَرُدُّ بِها البائِع؛ وخُطوَةُ التَأكيدِ القائِمَةُ هِيَ الإقرار.</para>
    /// </summary>
    /// <remarks>‏(‏2026-11-04، الموجَة ١٢ G1 · ٥ — B-2؛ تَوصيفُه <c>495d7b0</c>؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>والطَرَفانِ لا المُشتَري وَحدَه</b>: <c>DealsService</c> يَكتُبُ
    /// الرَدَّ عَلى البائِعِ — الطَرَفِ المُقابِل — فَبائِعٌ عَلَيه ثَمَنٌ يَرُدُّه كانَ يَحذِفُ حِسابَه بِلا كَلِمَة («البائِع: False»)، وبَعدَه يَرُدُّ <see cref="PartnerGone"/> المُشتَريَ
    /// عَن كُلِّ بابِ مُحادَثَة. فَالدالَّةُ تَقبَلُ المُبادِرَ (يُرَدُّ إلَيه) والطَرَفَ المُقابِلَ (يَرُدُّ هُو)، وتَحذيرٌ لا مَنعٌ كَما كان (G-7).</remarks>
    public static bool CashRefundOwed(Deal deal, Guid userId)
    {
        if ((deal.InitiatorId != userId && deal.CounterpartyId != userId) || deal.Status != DealStatus.Cancelled) return false;
        for (var i = deal.Timeline.Count - 1; i >= 0; i--)
            if (MoneyLines.Contains(deal.Timeline[i].Action)) return deal.Timeline[i].Action == DealEvent.CashRefundDue;
        return false;
    }

    /// <summary>صَفَقاتُه الَّتي لَه فيها ثَمَنٌ نَقدِيٌّ يَرُدُّه البائِع (<see cref="CashRefundOwed"/>)، أَحدَثُها أَوَّلاً.</summary>
    public static IReadOnlyList<Deal> CashRefundsOwed(IEnumerable<Deal> deals, Guid userId)
        => deals.Where(d => CashRefundOwed(d, userId)).OrderByDescending(d => d.UpdatedAt).ToList();

    /// <summary>الاستِعلامُ الواحِدُ لِما سَبَق — تَسأَلُه صَفحَةُ الحَذفِ بِجَلسَةِ قِراءَة (<c>UserDataQueries.CashRefundsOwedAsync</c>).</summary>
    public static async Task<IReadOnlyList<Deal>> CashRefundsOwedAsync(IQuerySession s, Guid userId, CancellationToken ct = default)
        => CashRefundsOwed(await s.Query<Deal>().Where(d => (d.InitiatorId == userId || d.CounterpartyId == userId) && d.Status == DealStatus.Cancelled).ToListAsync(ct), userId);

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

    /// <remarks>(‏2026-10-30، الموجَة ١٢ F2 · ٦ — XS-2؛ تَوصيفُه <c>cb88a77</c>) <b>سَطرٌ فاعِلُه الاستوديو أَو المَنصَّةُ لا يُعادُ اسمُه</b>
    /// (<see cref="DealEvent.IsByStudio"/>، <see cref="DealEvent.IsByPlatform"/>): بابُ «→ التالي» يَكتُبُ سَطرَه بِمُعَرِّفِ البائِعِ المُقابِلِ واسمِ
    /// <see cref="DealEvent.StudioActor"/> — والاسمُ هُوَ ما يُمَيِّزُه (ADR-047 §٨ (٢)) — فَكانَت تَسمِيَتُه «حِسابٌ مَحذوف» تُصَيِّرُه خُطوَةَ البائِعِ
    /// (<c>DealReviewAuthorization.TookAStep</c>) ولَم يَنقُر شَيئاً. ولا اسمَ لِلمَحذوفِ في السَطرَين: اسمُ صاحِبِ المَتجَرِ وثابِتُ المَنصَّة.</remarks>
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
            if (d.Timeline[i].ActorId == userId && d.Timeline[i].ActorName != AccountDeletion.ErasedName
                && !DealEvent.IsByStudio(d.Timeline[i]) && !DealEvent.IsByPlatform(d.Timeline[i]))
            {
                d.Timeline[i] = d.Timeline[i] with { ActorName = AccountDeletion.ErasedName };
                changed = true;
            }
        return changed;
    }

    public static bool EraseFrom(Conversation c, Guid userId)
        => (c.OwnerId == userId && Set(c.OwnerName, v => c.OwnerName = v))
         | (c.PartnerId == userId && Set(c.PartnerName, v => c.PartnerName = v));

    /// <summary>
    /// <para><b>واسمُه داخِلَ مَوضوعِ المُحادَثَة</b> (‏2026-10-30، تَدقيقُ الموجَة ١١ B-PRIV-2؛ تَوصيفُه <c>1075fc9</c>): بابُ المُحادَثَةِ المُباشِرَةِ يَكتُبُ
    /// «تَواصُل مَع {اسمِه}» في <c>Subject</c>، وقائِمَةُ المُحادَثاتِ تَعرِضُه ما لَم تُرسَل رِسالَة، و«بَياناتي» تَحمِلُه. فَفي مُحادَثَةٍ هُوَ طَرَفُها يُستَبدَلُ كُلُّ اسمٍ لَه
    /// كَلِمَةً تامَّةً (<see cref="ReplaceWhole"/>، الأَطوَلُ أَوَّلاً) — والاسمُ الافتِراضِيُّ لا يُبحَثُ عَنه (<see cref="Identifies"/>). ومُحادَثَةُ غَيرِه لا تُمَسّ.</para>
    /// </summary>
    public static bool EraseSubject(Conversation c, Guid userId, IReadOnlyCollection<string> oldNames)
    {
        if ((c.OwnerId != userId && c.PartnerId != userId) || string.IsNullOrEmpty(c.Subject)) return false;
        var subject = c.Subject;
        foreach (var name in oldNames.Where(Identifies).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).OrderByDescending(x => x.Length))
            subject = ReplaceWhole(subject, name);
        if (subject == c.Subject) return false;
        c.Subject = subject;
        return true;
    }

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

    /// <summary>صاحِبُ العَرضِ حَذَفَ حِسابَه (أَو لا حِسابَ لَه) — فَلا يُقبَلُ عَرضُه: عَرضٌ مُعَلَّقٌ مِن حَذفٍ سَبَقَ <see cref="EraseEvents(Offer, Guid, DateTime)"/> لا يُنشِئُ مُطابَقَةً
    /// ولا مُحادَثَةً مَعَ حِسابٍ لا وُجودَ لَه. القاعِدَةُ <see cref="PartnerGone"/> بِعَينِها — اسمٌ لِمَوضِعِها لا قاعِدَةٌ ثانِيَة.</summary>
    public static bool OffererGone([System.Diagnostics.CodeAnalysis.NotNullWhen(false)] User? offerer) => PartnerGone(offerer);

    /// <summary>
    /// <para><b>الطَرَفُ الآخَرُ غائِب</b> (‏2026-10-30، الموجَة ١٢ F2 · ٥، F2B-2؛ تَوصيفُه <c>9dc8354</c>) — دالَّةٌ نَقِيَّةٌ واحِدَة: لا وَثيقَةَ لَه، أَو أَغلَقَ حِسابَه
    /// (<c>DeletedAt</c>). الحَذفُ يُبقي الوَثيقَةَ بِاسمِ «حِسابٌ مَحذوف» (<see cref="ACommerce.Kit.Auth.AccountDeletion.Erase"/>)، فَسُؤالُ <c>is null</c> وَحدَه لا يَراه.</para>
    /// <para>يَسأَلُها كُلُّ بابٍ يَفتَحُ مُحادَثَةً أَو يُرسِلُ فيها (<c>users/{userId}/chat</c>، <c>vendor/{vendorId}/chat</c>، <c>listings/{id}/chat</c>،
    /// <c>chats/{id}/send</c>) قَبلَ أَيِّ كِتابَة، وتَسأَلُها الصَفَحاتُ الَّتي تَرسُمُ أَبوابَها (غُرفَةُ المُحادَثَة، صَفحَةُ البائِع، صَفحَةُ الإعلان) — فَلا يُرسَمُ بابٌ يَرُدُّه.</para>
    /// <para>‏(‏2026-11-04، الموجَة ١٢ G1 · ٤ — XS-2؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>وصَفحَةٌ رابِعَةٌ ومَسارٌ</b> فاتا <c>af95a46</c>: صَفحَةُ الصَفقَةِ لِلمُشتَري
    /// (<c>MyDealDetail</c>) تَرسُمُ «تَواصَل مَع البائِع» تَحتَ <c>!vendorGone</c> وسَطرَ الغِيابِ في بَديلِه، وأُنبوبُ طَلَبِ الاستِرجاعِ (<c>DealNotices.NotifyReturnRequestedAsync</c>)
    /// يَسأَلُها عَلى البائِعِ فَيُخبِرُ إدارَةَ المَتجَرِ بَدَلَ صُندوقِ المَمحُوّ.</para>
    /// </summary>
    public static bool PartnerGone([System.Diagnostics.CodeAnalysis.NotNullWhen(false)] User? partner) => partner is null || partner.DeletedAt is not null;

    /// <summary>إعلانٌ يَسحَبُه حَذفُ حِسابِه (‏2026-10-30، B-PRIV-1): قائِمٌ لَم يُحذَف، ومالِكُه هُوَ بِالمِفتاحِ الَّذي يَقرَؤُه كُلُّ بابٍ يَسأَلُ المِلكِيَّة
    /// (<see cref="ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingEditService.IsOwnedBy"/>).</summary>
    public static bool Withdraws(Listing listing, Guid userId)
        => !listing.IsDeleted && ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingEditService.IsOwnedBy(listing, userId);

    /// <summary>صَفقَةُ مَسارِ العَرضِ الوارِدَةُ عَلى إعلانِه ولَم يَقبَلْها (‏2026-10-30، B-PRIV-1): فَعّالَةٌ، ومَرجِعُ مالِكِها هُو، ولَيسَ طَرَفاً فيها — فَلا يَمنَعُ
    /// حَذفَه (<see cref="IsOpen"/> لِلطَرَفِ وَحدَه) ويُلغيها بَعدَه، بَدَلَ أَن تَبقى مَربوطَةً بِحِسابٍ لا يَقبَلُ ولا يَرفُض.
    /// <para>‏(‏2026-10-30، الموجَة ١٢ F2 · ٤ — XS-1 + H-SEC-1؛ تَوصيفُه <c>07a7948</c>؛ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>و«لَم يَقبَلْها» بِحَرفِه</b>: عِندَ «عَرض»
    /// وبِلا طَرَفٍ مُقابِل — <see cref="DealsService.IsIncomingOfferFor"/> الدالَّةُ القائِمَةُ لِلعَرضِ الوارِد (القاعِدَة ٨). كانَت لا تَسأَلُ المَرحَلَةَ ولا الطَرَف، وصاحِبُ
    /// المَتجَرِ يَقبَلُ مِن الاستوديو بِأَن يَصيرَ طَرَفَها المُقابِلَ ويُحَرِّكَها — فَكانَ حَذفُ البائِعِ يُلغيها في أَيِّ مَرحَلَةٍ بِسَبَبِ «قَبلَ أَن يَقبَلَ الطَلَب».</para></summary>
    public static bool IncomingUnaccepted(Deal deal, Guid userId)
        => deal.Status == DealStatus.Active && deal.Stage == DealStage.Offered && DealsService.IsIncomingOfferFor(deal, userId);

    /// <summary>
    /// <para><b>إلغاءُ الوارِدَةِ بَعدَ حِفظِ الحَذف</b> (‏2026-10-30، الموجَة ١٢ F2 · ٤ — XS-1 و8357c95 (ب)): لِكُلِّ صَفقَةٍ أَعادَها <see cref="EraseCopiesAsync"/> — قَبلَ كُلِّ
    /// مُحاوَلَةٍ قِراءَةٌ جَديدَةٌ و<see cref="IncomingUnaccepted"/> عَلَيها، فَقَبولٌ وَقَعَ بَعدَ الحِفظِ يُخرِجُها فَلا تُمَسّ؛ ثُمَّ <c>DealsService.CancelAsync</c> بِـ<see cref="ClosingCanceller"/>
    /// والسَبَب، وتَعارُضُ نُسخَةٍ (<see cref="DealsService.ChangedMeanwhile"/>) يُعادُ مَرَّةً عَلى ما كُتِب (<c>AccountWrites.Attempts</c>) — كانَ يُترَكُ فَتَبقى الصَفقَةُ فَعّالَةً بِمالِكٍ
    /// مَمحُوّ. يُعيدُ ما أُلغِيَ لِيُخبِرَ البابُ أَصحابَه بِالأُنبوبِ القائِم.</para>
    /// <para><b>ولِماذا لا <c>CancelWholeInvoiceAsync(…, stillDue:)</c></b>: قائِمَةُ أَهدافِه مِن الفاتورَة، ومَعَ الشَرطِ لا رُجوعَ إلى الصَفقَةِ وَحدَها — فَصَفقَةُ العَرضِ (بِلا فاتورَةٍ
    /// ولا شُحنَة) لا تُلغى ويُجابُ <c>deal_no_longer_due</c> (مَقيسٌ حَيّاً في <c>07a7948</c>). <b>وما لا يُدَّعى</b>: قَبولٌ يَقَعُ بَينَ القِراءَةِ الجَديدَةِ هُنا وقِراءَةِ
    /// <c>CancelAsync</c> نَفسِها لا يُعادُ عَلَيه الشَرط — يُغلِقُه أَن يُلغِيَ ذلكَ الأُنبوبُ الصَفقَةَ وَحدَها حينَ لا فاتورَةَ ولا شُحنَة (طَلَبٌ لِـ<c>Services/Deals</c>).</para>
    /// </summary>
    public static async Task<IReadOnlyList<Deal>> CloseIncomingAsync(DealsService deals, string tenantSlug, Guid userId, IEnumerable<Guid> dealIds, string reason,
        CancellationToken ct = default)
    {
        var closed = new List<Deal>();
        foreach (var id in dealIds)
            for (var attempt = 1; attempt <= ACommerce.Kit.Auth.Server.AccountWrites.Attempts; attempt++)
            {
                if (await deals.LoadAsync(tenantSlug, id, ct) is not { } fresh || !IncomingUnaccepted(fresh, userId)) break;
                var result = await deals.CancelAsync(tenantSlug, id, ClosingCanceller, reason, ct);
                if (result.Ok) { closed.Add(result.Deal!); break; }
                if (result.Violation?.Code != DealsService.ChangedMeanwhile) break;
            }
        return closed;
    }

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

    /// <summary>
    /// <para><b>تَذكِرَتُه تُمحى في مَجراها</b> (‏2026-10-30، تَدقيقُ الموجَة ١١ CO16؛ تَوصيفُه <c>13b1754</c>) — نَسَقُ <see cref="EraseEvents(Offer, Guid, DateTime)"/>:
    /// كانَ المَحوُ يَكتُبُ لَقطَتَها وَحدَها (<c>Store</c>)، فَإعادَةُ بِنائِها مِن المَجرى تُعيدُ اسمَه. والآن <c>TicketAuthorRenamed</c> بِما يَكتُبُه
    /// <see cref="EraseFrom(Ticket, Guid)"/> — يُطَبِّقُه <c>Ticket.Apply</c> عَلى التَذكِرَةِ ورُدودِه، والإسقاطُ المُباشِرُ يَكتُبُ اللَقطَةَ في الحِفظِ نَفسِه.
    /// ولا شَيءَ لِتَذكِرَةِ غَيرِه، ولا لِتَذكِرَةٍ مَمحُوَّةٍ قَبلُ.</para>
    /// </summary>
    public static IReadOnlyList<object> EraseEvents(Ticket t, Guid userId, DateTime at)
        => EraseFrom(t, userId) ? new object[] { new TicketAuthorRenamed(t.Id, t.AuthorName, at) } : Array.Empty<object>();

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
        // ‏(‏2026-10-30، تَدقيقُ الموجَة ١١ CO7) وإشعارُ المُديرينَ لا يُربَطُ بِرابِطِه: رابِطُ البَلاغِ صارَ الإعلانَ المُبَلَّغَ عَنه (`ListingReport.NoticeUrl`)، ومُطابَقَتُه
        // عَلى ذلكَ الإعلانِ كانَت سَتَجعَلُ بَلاغَ سَمِيِّه «يَخُصُّه» فَيُمحى اسمُه مِنه (تَوصيفُه `390eb0d`). رَبطُه بِهُوِيَّتِه وَحدَها كَما كانَ: بَلاغُه أَو وَسيلَتُه.
        var linked = !AdminNoticeTypes.Contains(n.Type) && n.RelatedUrl is { } url && relatedUrls.Any(r => url.EndsWith(r, StringComparison.Ordinal));
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

    /// <summary>
    /// <para><b>روابِطُ ما يَخُصُّه</b> — إشعارٌ رابِطُه مِنها عَنه (<see cref="EraseFrom(Notification, Guid, IReadOnlyCollection{string}, IReadOnlyCollection{string}, IReadOnlyCollection{string}, IReadOnlySet{Guid})"/>):
    /// كُلُّ صَفقَةٍ في <paramref name="touched"/>، ومُحادَثاتُه، ومُطابَقاتُ إعلاناتِه. نَقِيَّة.</para>
    /// <para><b>‏(‏2026-10-30، تَدقيقُ الموجَة ١١ G-2؛ تَوصيفُه <c>fcd9efe</c>) و«الصَفقَةُ» صَفقَةٌ فَعَلَ فيها لا صَفقَةُ طَرَفٍ وَحدَها</b>: كانَت الروابِطُ تُبنى مِن صَفَقاتِ
    /// المُبادِرِ والمُقابِل، فَمالِكُ إعلانٍ رَفَضَ عِندَ «عَرض» طَلَباً لَم يَصِر طَرَفَه (د31 «١»)، وفاعِلٌ بِمِفتاحِ رَبطٍ أَلغى صَفقَةَ غَيرِه، يُمحى سَطرُه في خَطِّها الزَمَنِيِّ
    /// ويَبقى اسمُه في إشعارِ مُشتَريها «{الاسم} أَلغى طَلَبَكَ…». فَالمُنادي يُمَرِّرُ صَفَقاتِ الطَرَفِ والَّتي في خَطِّها سَطرٌ لَه مَعاً.</para>
    /// </summary>
    public static IReadOnlyList<string> RelatedUrls(IEnumerable<Deal> touched, IEnumerable<Conversation> convs, IEnumerable<ListingMatch> matches)
        => touched.Select(d => $"/deals/{d.Id}")
            .Concat(convs.Select(c => $"/chats/{c.Id}"))
            .Concat(matches.Select(m => $"/listings/{m.Id}"))
            .Distinct(StringComparer.Ordinal).ToList();

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

        /// <summary>‏(‏2026-11-04، الموجَة ١٢ G1 · ٣ — H-SEC-W12-1) إعلاناتُه — القائِمَةُ والمَحذوفَةُ قَبلُ — الَّتي مُحِيَت صُوَرُها أَو إحداثِيّاتُها (<see cref="ScrubEvents"/>).</summary>
        public int Scrubbed { get; init; }

        /// <summary>مِفاتيحُ مِلَفّاتِه في المَخزَنِ (<see cref="FileKeys"/>) — تُحذَفُ بَعدَ الحِفظِ بِـ<see cref="DeleteFilesAsync"/> لا في المُعامَلَة: المَخزَنُ خارِجَ القاعِدَة.
        /// لا تُعَدُّ في <see cref="Total"/>: لَم تُكتَب هُنا.</summary>
        public ErasedFiles Files { get; init; } = ErasedFiles.None;
    }

    // ─── مِلَفّاتُه في المَخزَن، وصُوَرُ إعلاناتِه ومَواقِعُها (‏2026-11-04، الموجَة ١٢ G1 · ٣ — H-SEC-W12-1 + B-5؛ تَوصيفُه 7f2f776) ─────────────

    /// <summary>
    /// <para><b>خاصِّيَّةُ مَوقِعٍ في إعلان</b>: <c>lat</c>/<c>lng</c> أَو ما يَنتَهي بِـ<c>_lat</c>/<c>_lng</c> — ما يَكتُبُه <c>AcLocationInput</c> (‏<c>{Name}_lat</c>
    /// و<c>{Name}_lng</c>: <c>pickup_*</c> و<c>dropoff_*</c> اليَوم) وما يُسَمّيه <c>ACommerce.Kit.Maps.Location</c>. نَقِيَّة.</para>
    /// </summary>
    public static bool IsLocationAttribute(string key)
        => key is "lat" or "lng" || key.EndsWith("_lat", StringComparison.Ordinal) || key.EndsWith("_lng", StringComparison.Ordinal);

    /// <summary>
    /// <para><b>إعلانُه بِلا صُوَرِه ولا مَوقِعِه</b>: <c>ListingMediaSet</c> بِقائِمَةٍ فارِغَةٍ إن كانَت لَه صورَة، و<c>ListingEdited</c> بِخَصائِصِه ناقِصَةً خَصائِصَ المَوقِعِ
    /// (<see cref="IsLocationAttribute"/>) إن كانَ لَه مَوقِع — والمالِكُ وما سِواه يَبقى، فَصَفُّ السِجِلِّ يَبقى بِلا ما يَدُلُّ عَلى مَكانِه (د53 «أ»). وإعلانٌ بِلا صورَةٍ ولا مَوقِعٍ
    /// لا حَدَثَ لَه. نَقِيَّة — حَدَثانِ في مَجراه لا <c>Store</c> (الإسقاطُ المُباشِرُ يَكتُبُ لَقطَتَه، مُلاحَظَةُ <see cref="EraseEvents(Offer, Guid, DateTime)"/>).</para>
    /// </summary>
    public static IReadOnlyList<object> ScrubEvents(Listing listing, DateTime at)
    {
        var events = new List<object>();
        if (listing.MediaUrls.Count > 0) events.Add(new ListingMediaSet(listing.Id, new List<string>(), at));
        if (listing.Attributes.Keys.Any(IsLocationAttribute))
            events.Add(new ListingEdited(listing.Id, null, null, null, null, null, null,
                listing.Attributes.Where(a => !IsLocationAttribute(a.Key)).ToDictionary(a => a.Key, a => a.Value), at));
        return events;
    }

    /// <summary>صورَتُه في المَخزَن: المِفتاحُ الحَتمِيُّ الَّذي يَكتُبُه بابُ تَعديلِ المِلَفِّ (<c>tenants/{slug}/avatars/{userId}.{ext}</c>) بِامتِداداتِه الثَلاثَة — صورَةٌ رُفِعَت
    /// بِنَوعٍ ثُمَّ بِآخَرَ تَبقى بِمِفتاحَين. ويَحرُسُ تَطابُقَ الشَكلِ صَفٌّ يَقرَأُ نَصَّ البابِ (القاعِدَة ٢).</summary>
    public static IReadOnlyList<string> AvatarKeys(string tenantSlug, Guid userId)
        => AvatarExtensions.Select(ext => $"tenants/{tenantSlug}/avatars/{userId}.{ext}").ToList();

    /// <summary>امتِداداتُ الصورَةِ الَّتي يَقبَلُها بابُ تَعديلِ المِلَفّ (<c>image/png</c>، <c>image/jpeg</c> ← <c>jpg</c>، <c>image/webp</c>).</summary>
    public static IReadOnlyList<string> AvatarExtensions { get; } = new[] { "png", "jpg", "webp" };

    /// <summary>مِفاتيحُ مِلَفّاتِه، وعَدَدُ ما تُرِكَ مِن الروابِطِ لِأَنّه خارِجَ مَوضِعِه.</summary>
    public sealed record ErasedFiles(IReadOnlyList<string> Keys, int Outside)
    {
        public static readonly ErasedFiles None = new(Array.Empty<string>(), 0);
    }

    /// <summary>
    /// <para><b>مِفاتيحُ مِلَفّاتِه</b>: صورَتُه (<see cref="AvatarKeys"/>)، وكُلُّ صورَةٍ لِإعلانٍ لَه (<paramref name="listings"/>) مِفتاحُها في مَجَلَّدِ ذلكَ الإعلانِ بِعَينِه
    /// (<c>tenants/{slug}/listings/{id}/</c> — ما يَكتُبُه بابُ النَشرِ وَحدَه). والمِفتاحُ يُشتَقُّ مِن الرابِطِ بِالبادِئَةِ (<c>TenantExportService.KeyFromUrl</c> — الدالَّةُ
    /// الَّتي تَقرَأُ بِها الحَقيبَةُ المِلَفَّ نَفسَه، القاعِدَة ٨)، ورابِطٌ خارِجَ مَجَلَّدِ إعلانِه لا يُحذَف ويُعَدّ: مِلَفٌّ في مَخزَنٍ آخَر، أَو في مَوضِعٍ قَد يَعرِضُه غَيرُه.
    /// ولا مِفتاحَ لِغَيرِه. نَقِيَّة.</para>
    /// </summary>
    public static ErasedFiles FileKeys(string tenantSlug, Guid userId, IEnumerable<Listing> listings)
    {
        var keys = new List<string>(AvatarKeys(tenantSlug, userId));
        var outside = 0;
        foreach (var l in listings)
            foreach (var url in l.MediaUrls)
            {
                var key = ACommerce.Templates.Customer.Marketplace.Services.Export.TenantExportService.KeyFromUrl(url, tenantSlug);
                if (key is not null && key.StartsWith($"tenants/{tenantSlug}/listings/{l.Id}/", StringComparison.Ordinal)) keys.Add(key);
                else outside++;
            }
        return new ErasedFiles(keys.Distinct(StringComparer.Ordinal).ToList(), outside);
    }

    /// <summary>
    /// <para><b>حَذفُ مِلَفّاتِه بَعدَ حِفظِ الحَذف — بِأَفضَلِ جُهد</b>: لِكُلِّ مِفتاحٍ <c>IFileStorage.DeleteAsync</c>، وما يَرمي يُكتَبُ في السِجِلِّ بِمِفتاحِه ويُعَدّ ولا يُسقِطُ
    /// الحَذف — الحِسابُ مَمحُوٌّ في القاعِدَةِ قَبلَه، ومَخزَنٌ لا يُبلَغُ لا يُعيدُه. يُعيدُ عَدَدَ ما حُذِفَ وما تَعَذَّر.</para>
    /// </summary>
    public static async Task<(int Deleted, int Failed)> DeleteFilesAsync(ACommerce.Kit.Files.IFileStorage files, IReadOnlyCollection<string> keys,
        Microsoft.Extensions.Logging.ILogger log, CancellationToken ct = default)
    {
        var (deleted, failed) = (0, 0);
        foreach (var key in keys)
            try { await files.DeleteAsync(key, ct); deleted++; }
            catch (Exception ex)
            {
                failed++;
                Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(log, ex, "حَذفُ الحِساب: تَعَذَّرَ حَذفُ المِلَفِّ {Key} ({Kind})", key, ex.GetType().Name);
            }
        return (deleted, failed);
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
        // ‏(‏2026-10-30، CO16) والتَذاكِرُ كَالعُروض: حَدَثٌ في مَجرى كُلٍّ مِنها لا `Store` — حَدَثٌ ولَقطَةٌ مُخَزَّنَةٌ يَدَوِيّاً في جَلسَةٍ واحِدَةٍ يَكتُبُ فيهِما الإسقاطُ
        // فَوقَ المُخَزَّن (مُلاحَظَةُ `EraseEvents(Offer)`). وتَذكِرَةٌ بِلا مَجرى (وَثيقَةٌ خُزِّنَت مُباشَرَةً — عَيِّنَةُ البَذر) لا إسقاطَ لَها يُعادُ بِناؤه:
        // وَثيقَتُها هِيَ الحَقيقَة، فَتُكتَبُ كَما كانَت. وإلحاقُ حَدَثٍ بِمُعَرِّفٍ لا مَجرى لَه يَبدَأُ مَجرىً بِحَدَثِ التَسمِيَةِ وَحدَه فَتولَدُ لَقطَةٌ فارِغَة.
        async Task<int> Rename(IEnumerable<Ticket> rows)
        {
            var n = 0;
            var at = DateTime.UtcNow;
            foreach (var t in rows)
            {
                if (await s.Events.FetchStreamStateAsync(t.Id, ct) is null) { if (EraseFrom(t, userId)) { s.Store(t); n++; } }
                else if (EraseEvents(t, userId, at) is { Count: > 0 } events) { s.Events.Append(t.Id, events.ToArray()); n++; }
            }
            return n;
        }
        // العُروضُ بِأَحداثٍ في مَجاريها لا بِـ`Store` (<see cref="EraseEvents(Offer, Guid, DateTime)"/>) — والإسقاطُ المُباشِرُ يَكتُبُ لَقطَتَها في الحِفظِ نَفسِه.
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

        // ‏(‏2026-10-30، تَدقيقُ الموجَة ١١ G-2) والصَفقَةُ الَّتي فَعَلَ فيها ولَيسَ طَرَفَها (`others`) تَربِطُ إشعاراتِها كَصَفقَةِ الطَرَف — `RelatedUrls`.
        var urls = RelatedUrls(deals.Concat(others), convs, matches);
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
        // ‏(‏2026-10-30، G-2) واسمُه عَلى مَفاتيحِ رَبطِه كَما كُتِبَ يَومَ أُصدِرَت — يَكتُبُه بابُ الـAPI في إشعارِ الإلغاءِ (`p.ActorName`) ولو تَبَدَّلَ اسمُه بَعدَها.
        var names = oldNames.Concat(reportNames).Concat(keys.Select(k => k.ActorName)).ToList();
        // كُلُّ اسمٍ بِاستِعلامِه، والنَتائِجُ وَثيقَةٌ واحِدَةٌ لِكُلِّ مُعَرِّف — فَلا تُخَزَّنُ نُسخَتانِ مِن إشعارٍ يَحمِلُ الاسمَين.
        // ‏(‏2026-10-30، G-2) بِالأَسماءِ كُلِّها — ومِنها اسمُه عَلى مَفاتيحِه — لا بِاسمِ الحِسابِ وَحدَه: المُرَشَّحونَ أَوسَع، والقَرارُ لِـ`EraseFrom` بِرابِطِه.
        foreach (var oldName in names.Where(Identifies).Distinct(StringComparer.Ordinal))
            foreach (var n in await s.Query<Notification>().Where(n => n.UserId != userId && (n.Title.Contains(oldName) || n.Body.Contains(oldName))).ToListAsync(ct))
                if (notices.All(x => x.Id != n.Id)) notices.Add(n);
        // ووَسيلَتا اتِّصالِه — والحَصرُ في إشعاراتِ المُديرينَ وحُدودِ الوَسيلَةِ لِـ`EraseFrom` لا لِلاستِعلام.
        foreach (var contact in contacts.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.Ordinal))
            foreach (var n in await s.Query<Notification>().Where(n => n.UserId != userId && (n.Title.Contains(contact) || n.Body.Contains(contact))).ToListAsync(ct))
                if (notices.All(x => x.Id != n.Id)) notices.Add(n);

        // ‏(‏2026-10-30، B-PRIV-1) إعلاناتُه تُسحَبُ بِمَجاريها والعُروضُ المُعَلَّقَةُ عَلَيها تُرفَضُ بِحَدَثِ القَبولِ نَفسِه لِأَخَواتِه (`OfferRejected`)، والوارِدَةُ الَّتي
        // لَم يَقبَلْها تُعادُ لِلنُقطَة. و`Attributes` لا يُرَشِّحُها Marten في LINQ (`AccountQueries.MyListingsAsync`)، فَالمالِكُ يُرَشَّحُ في الذاكِرَة.
        // ‏(‏2026-10-30، الموجَة ١٢ F2 · ٨) والسُؤالُ والأَثَرُ دالَّتانِ يَقرَؤُهُما سكربتُ حِساباتٍ حُذِفَت قَبلَ هذا (`scripts/withdraw-erased-owner-listings.cs`) — لا نُسخَةٌ ثانِيَة.
        var closedAt = DateTime.UtcNow;
        var plan = await WithdrawalPlanAsync(s, userId, ct);
        AppendWithdrawal(s, plan, closedAt);
        // ‏(‏2026-10-30، CO16) وتَذاكِرُه بَعدَ أَن قُرِئَ اسمُه مِنها لِبَلاغاتِه أَعلاه.
        var renamedTickets = await Rename(tickets);

        return new Counts(
            Deals: Mark(deals.Concat(others), d => EraseFrom(d, userId)),
            Conversations: Mark(convs, c => EraseFrom(c, userId) | EraseSubject(c, userId, names)),
            Reviews: Mark(reviews, r => EraseFrom(r, userId)),
            Offers: Withdraw(offers),
            Matches: Mark(matches, m => EraseFrom(m, userId)),
            Tickets: renamedTickets,
            Keys: Mark(keys, k => EraseFrom(k, tenantSlug, userId)),
            Notifications: Mark(notices, n => EraseFrom(n, userId, names, urls, contacts, ownReports)))
        {
            Listings = plan.Listings.Count,
            RejectedOffers = plan.PendingOffers.Count,
            IncomingDeals = plan.IncomingDeals,
            Scrubbed = plan.Scrubbed.Count,
            Files = FileKeys(tenantSlug, userId, plan.Scrubbed),
        };
    }

    /// <summary>ما يَسحَبُه حَذفُ حِسابٍ مِن مَتجَرِه: إعلاناتُه القائِمَة، والعُروضُ المُعَلَّقَةُ عَلَيها مِن غَيرِه، وصَفَقاتُ العَرضِ الوارِدَةُ الَّتي لَم يَقبَلْها أَحَد.</summary>
    public sealed record WithdrawalPlan(IReadOnlyList<Guid> Listings, IReadOnlyList<Guid> PendingOffers, IReadOnlyList<Guid> IncomingDeals)
    {
        /// <summary>‏(‏2026-11-04، الموجَة ١٢ G1 · ٣ — H-SEC-W12-1) إعلاناتُه — القائِمَةُ الَّتي تُسحَبُ والمَحذوفَةُ قَبلُ — الَّتي لَها صورَةٌ أَو مَوقِع (<see cref="ScrubEvents"/>):
        /// كانَ <c>ListingDeleted</c> يَضبِطُ <c>IsDeleted</c> وَحدَه، فَتَبقى الصُوَرُ وإحداثِيّاتُ الالتِقاطِ والإنزالِ في الوَثيقَةِ وفي حَقيبَةِ التَخارُج.</summary>
        public IReadOnlyList<Listing> Scrubbed { get; init; } = Array.Empty<Listing>();

        public int Count => Listings.Count + PendingOffers.Count + IncomingDeals.Count + Scrubbed.Count;
    }

    /// <summary>
    /// <para><b>السُؤال</b> (‏2026-10-30، B-PRIV-1؛ دالَّةً مُنذُ الموجَة ١٢ F2 · ٨): قِراءَةٌ لا تَكتُب — <see cref="Withdraws"/> لِلإعلان، والعَرضُ مُعَلَّقٌ ولَيسَ عَرضَه وإعلانُه
    /// مِن المَسحوب، و<see cref="IncomingUnaccepted"/> لِلصَفقَة. يَسأَلُها <see cref="EraseCopiesAsync"/> في مُعامَلَةِ الحَذف، ويَسأَلُها السكربتُ لِحِسابٍ حُذِفَ قَبلَ
    /// السَحبِ (عَرضاً ثُمَّ تَنفيذاً). و<c>Attributes</c> لا يُرَشِّحُها Marten في LINQ، فَالمالِكُ يُرَشَّحُ في الذاكِرَة.</para>
    /// </summary>
    public static async Task<WithdrawalPlan> WithdrawalPlanAsync(IQuerySession s, Guid userId, CancellationToken ct = default)
    {
        // ‏(‏2026-11-04، G1 · ٣) والمَحذوفُ قَبلُ مَعَ القائِم — صُوَرُه ومَوقِعُه باقِيانِ فيه.
        var his = (await s.Query<Listing>().ToListAsync(ct))
            .Where(l => ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingEditService.IsOwnedBy(l, userId)).ToList();
        var withdrawn = his.Where(l => Withdraws(l, userId)).Select(l => l.Id).ToHashSet();
        var rejected = (await s.Query<Offer>().Where(o => o.Status == OfferStatus.Pending && o.OffererId != userId).ToListAsync(ct))
            .Where(o => withdrawn.Contains(o.ListingId)).Select(o => o.Id).ToList();
        var incoming = (await s.Query<Deal>().Where(d => d.Status == DealStatus.Active).ToListAsync(ct))
            .Where(d => IncomingUnaccepted(d, userId)).Select(d => d.Id).ToList();
        return new WithdrawalPlan(withdrawn.ToList(), rejected, incoming)
        {
            Scrubbed = his.Where(l => ScrubEvents(l, DateTime.UnixEpoch).Count > 0).ToList(),
        };
    }

    /// <summary><b>الأَثَرُ في الجَلسَة</b> بِلا حِفظ: الإعلانُ يُسحَبُ بِمَجراه (<c>ListingDeleted</c>) والعَرضُ يُرفَضُ بِحَدَثِ القَبولِ نَفسِه لِأَخَواتِه (<c>OfferRejected</c>).
    /// والصَفَقاتُ لا تُمَسُّ هُنا: تُلغى بَعدَ الحِفظِ بِـ<see cref="CloseIncomingAsync"/> — كاتِبُها الواحِدُ وقِراءَتُه الجَديدَة.
    /// ‏(‏2026-11-04، G1 · ٣) ولِكُلِّ إعلانٍ لَه صورَةٌ أَو مَوقِعٌ حَدَثا <see cref="ScrubEvents"/> في مَجراه — بَعدَ السَحب، والقائِمُ والمَحذوفُ قَبلُ سَواء.</summary>
    public static void AppendWithdrawal(IDocumentSession s, WithdrawalPlan plan, DateTime at)
    {
        foreach (var id in plan.Listings) s.Events.Append(id, new ListingDeleted(id, at));
        foreach (var id in plan.PendingOffers) s.Events.Append(id, new OfferRejected(id, at));
        foreach (var l in plan.Scrubbed) s.Events.Append(l.Id, ScrubEvents(l, at).ToArray());
    }
}
