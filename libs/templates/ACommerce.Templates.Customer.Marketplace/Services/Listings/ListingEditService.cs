using ACommerce.Kit.Listings;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Listings;

/// <summary>
/// <para>طَلَبُ تَحريرِ إعلانٍ واحِد — <b>نَوعٌ مُدخَل لا
/// <c>HttpRequest</c></b>. وهذا ما يُصَيِّر الخِدمَةَ صالِحَةً لِنُقطَةِ
/// JSON ولِتَطبيقٍ أَصيلٍ غَداً؛ ونُقطَةُ الويب تَقرَأ النَموذَج
/// وتَبني هذا.</para>
///
/// <para><b>و<c>PriceRaw</c> سِلسِلَة لا <c>decimal</c> عَمداً</b>:
/// «‏السِعر لَيسَ رَقماً» و«السِعر صِفر» رَفضانِ مُختَلِفانِ في
/// المَعنى ومُتَّحِدانِ في الرَمز، وتَحويلُ النَصّ في المُهايِئ
/// يَبتَلِع الأَوَّل صامِتاً (‏<c>TryParse</c> الفاشِلَة تُعطي صِفراً).
/// فَالتَحويل يَقَع حَيثُ يَقَع الحُكم.</para>
/// </summary>
public sealed record ListingEditRequest(
    Guid ListingId,
    Guid ActorId,
    string Title,
    string Description,
    string PriceRaw,
    string City,
    string District);

/// <summary>حَذفُ إعلانٍ بِيَدِ مالِكِه — نَفسُ التَخويل، وأَثَرٌ
/// واحِد.</summary>
public sealed record ListingDeleteRequest(Guid ListingId, Guid ActorId);

/// <summary>
/// <para><b>تَحريرُ إعلانٍ وحَذفُه — العَمَلِيَّتانِ اللَتانِ لَم يَكُن
/// لَهُما سَطحٌ شَرعيّ.</b> ‏<c>ListingEdited</c> صارَ يَتيماً يَومَ
/// حُذِفَت <c>POST /{slug}/api/listings/{id}/edit</c> المَكشوفَة
/// (‏<c>a7e0352d</c>)، وذاكَ وَحدَه بُرهانٌ أَنَّها لَم تَكُن مَساراً
/// شَرعِيّاً: لا شاشَةَ تَحرير في المُنتَج، فَما كانَ أَحَدٌ يَبلُغُ
/// التَحرير إلّا بِطَلَبٍ مَجهول. وهذا المِلَفّ يَسُدُّ الفَجوَة مِن
/// الجِهَة الصَحيحَة — <b>شاشَة مَحروسَة</b> لا نُقطَةٌ تُعاد.</para>
///
/// <para><b>والشَكل هُوَ شَكلُ <c>Services/TenantConfig/*</c></b>
/// (‏<c>docs/ARCHITECTURE-ENFORCEMENT.md</c> §٥.١): تَأخُذ
/// <see cref="IDocumentSession"/> ولا تَفتَحُها ولا تُودِع، وتَأخُذ
/// <c>record</c> لا <c>HttpRequest</c>، وتُرجِع نَتيجَةً بِرَمزٍ مِن
/// مُعجَمٍ مُغلَق. والمُعامَلَةُ لِلنُقطَة — فَالحَدَثُ يُلحَق في
/// <b>نَفس الجَلسَة</b> ولا يُنادى المَخزَن مَرَّةً ثانِيَة.</para>
///
/// <para><b>والتَخويلُ يَسبِق تَحَقُّقَ الحُقول</b> (القاعِدَة ٦):
/// <see cref="Decide"/> يَفحَص المِلكِيَّة قَبلَ العُنوان والسِعر. ولَو
/// عُكِسَ التَرتيب لَصارَ خَطَأُ التَحَقُّق قِناعاً لِلثَغرَة —
/// فَغَيرُ المالِك يَرى «العُنوان قَصير» فَيَستَنتِج أَنّ الإعلانَ
/// قابِلٌ لِلتَحرير لَو أَحسَنَ الحَقل.</para>
///
/// <para><b>وما لا تُعَدِّلُه هذِه المَوجَة — مُعلَناً لا مَنسِيّاً</b>:
/// الفِئَة، والصُوَر، والخَصائِص الديناميكِيَّة. والسَبَب مَقيسٌ في
/// النَوع نَفسِه: <c>Apply(ListingEdited)</c> <b>يَستَبدِل قامُوس
/// الخَصائِص كامِلاً</b> — وفيه <c>owner_id</c> و<c>photos</c>.
/// فَتَحريرٌ نِصفيّ لِلخَصائِص يَمحو مالِكَ الإعلان وصُوَرَه بِسَطرٍ
/// واحِد. دَمجُها قَرارٌ لَه مَوجَتُه، وتَركُها <c>null</c> هُنا
/// يَعني «لا تُغَيِّر» بِنَصّ <c>Apply</c>.</para>
/// </summary>
public static class ListingEditService
{
    /// <summary>حَدُّ العُنوان — نَفسُ حَدّ الإنشاء حَرفاً
    /// (<c>title.Length &lt; 3</c> في <c>POST /{slug}/listings/create</c>).
    /// شاشَتانِ بِحَدَّينِ مُختَلِفَين تُنتِجانِ إعلاناً يُنشَأ ولا
    /// يُحَرَّر.</summary>
    public const int MinTitleLength = 3;

    /// <summary>مِفتاحُ المالِك في <c>Attributes</c>. الإنشاء يَكتُبُه
    /// (<c>dynAttrs["owner_id"]</c>) لِأَنّ <c>ListingCreated</c> بِلا
    /// حَقل مالِكٍ مُهَيكَل، و«إعلاناتي» تُفَلتِر بِه. فَالتَخويل هُنا
    /// يَقرَأ <b>نَفسَ</b> المِفتاح — لا مَصدَرَ ثانٍ يَنجَرِف.</summary>
    public const string OwnerAttribute = "owner_id";

    /// <summary>خاصِّيَّةُ «يَقبَل العُروض». إعلانٌ يَقبَلُها يَجوز
    /// سِعرُه صِفراً (الراكِب يَترُك السِعر، والسائِق يُحَدِّدُه في
    /// عَرضِه) — وهذا نَصُّ قاعِدَةِ الإنشاء نَفسِها.</summary>
    public const string AcceptsOffersAttribute = "accepts_offers";

    // ─── دالَّتا القَرار، نَقِيَّتان ───────────────────────────────────

    /// <summary><b>هَل يَملِك هذا الفاعِلُ هذا الإعلان؟</b> دالَّةٌ
    /// نَقِيَّة يَقرَؤُها <b>ثَلاثَة</b>: المُرَشِّح الَّذي يَحرُس
    /// النُقطَة، والخِدمَةُ الَّتي تُقَرِّر، والصَفحَةُ الَّتي تَعرِض.
    /// وتَعريفٌ واحِد لِلمِلكِيَّة هُوَ ما يَمنَع «شاشَةً تَفتَح
    /// لِمَن لا تَقبَلُه النُقطَة».</summary>
    public static bool IsOwnedBy(Listing listing, Guid actorId) =>
        listing.Attributes.TryGetValue(OwnerAttribute, out var owner) &&
        Guid.TryParse(owner, out var ownerId) &&
        ownerId == actorId;

    /// <summary>
    /// <para><b>القَرار كامِلاً بِلا قاعِدَة بَيانات</b>: الحالَةُ
    /// الراهِنَة والطَلَب ← حَدَثٌ يُلحَق، أَو رَمزُ رَفض. لا شَيءَ
    /// هُنا يَحتاج جَلسَةً، فَكُلّ رَمزٍ لَه اختِبارٌ مُوجَبٌ وسالِب
    /// بِلا مُضيفٍ ولا خادِم.</para>
    ///
    /// <para><b>و«لا فَرق» رَفضٌ لا نَجاح</b>: إلحاقُ
    /// <c>ListingEdited</c> بِلا حَقلٍ مُتَبَدِّل يُحَرِّك
    /// <c>UpdatedAt</c> ويُطيل التَيار بِلا مَعنىً — فَيَكذِب على كُلّ
    /// قارِئٍ يُرَتِّب بِالتَحديث.</para>
    /// </summary>
    public static (ListingEdited? Event, string? Code) Decide(
        Listing current, ListingEditRequest r, DateTime at)
    {
        // ١. التَخويل أَوَّلاً — قَبلَ أَن يُقرَأ حَقلٌ واحِد.
        if (!IsOwnedBy(current, r.ActorId))
            return (null, ListingEditCodes.NotOwner);

        // ٢. ثُمَّ الحُقول.
        var title = r.Title.Trim();
        if (title.Length < MinTitleLength)
            return (null, ListingEditCodes.TitleShort);

        if (!decimal.TryParse(r.PriceRaw.Trim(), out var price))
            return (null, ListingEditCodes.PriceInvalid);

        var acceptsOffers =
            current.Attributes.TryGetValue(AcceptsOffersAttribute, out var ao) &&
            ao.Equals("true", StringComparison.OrdinalIgnoreCase);
        if (acceptsOffers ? price < 0 : price <= 0)
            return (null, ListingEditCodes.PriceInvalid);

        var description = r.Description.Trim();
        var city        = r.City.Trim();
        var district    = r.District.Trim();

        // ٣. الفَرق حَقلاً حَقلاً — و`null` تَعني «لا تُغَيِّر» بِنَصّ
        //    `Listing.Apply(ListingEdited)`.
        var newTitle       = title       == current.Title              ? null : title;
        var newDescription = description == (current.Description ?? "") ? null : description;
        decimal? newPrice  = price       == current.Price              ? null : price;
        var newCity        = city        == (current.City ?? "")        ? null : city;
        var newDistrict    = district    == (current.District ?? "")    ? null : district;

        if (newTitle is null && newDescription is null && newPrice is null &&
            newCity is null && newDistrict is null)
            return (null, ListingEditCodes.NoChange);

        return (new ListingEdited(
            current.Id, newTitle, newDescription, newPrice,
            CategorySlug: null,        // الفِئَة خارِج هذِه المَوجَة — مُعلَنٌ أَعلاه.
            newCity, newDistrict,
            Attributes: null,          // ولا تُمَسّ الخَصائِص: `Apply` يَستَبدِلُها كامِلَةً.
            at), null);
    }

    // ─── العَمَلِيَّتان — تَأخُذانِ الجَلسَة ولا تَملِكانِها ────────────

    /// <summary>تُلحِق <see cref="ListingEdited"/> بِتَيار الإعلان في
    /// <b>نَفس الجَلسَة</b>. الإيداعُ على النُقطَة — فَتَستَطيع أَن
    /// تَضُمَّ عَمَلِيَّةً ثانِيَة إلى نَفس المُعامَلَة غَداً بِلا
    /// تَعديل حَرفٍ هُنا.</summary>
    public static async Task<ListingEditResult> EditAsync(
        IDocumentSession session, ListingEditRequest r, CancellationToken ct = default)
    {
        var current = await session.LoadAsync<Listing>(r.ListingId, ct);
        if (current is null || current.IsDeleted) return ListingEditResult.Missing;

        var (ev, code) = Decide(current, r, DateTime.UtcNow);
        if (code is not null) return ListingEditResult.Reject(code);

        session.Events.Append(r.ListingId, ev!);
        return ListingEditResult.Applied;
    }

    /// <summary>
    /// <para>تُلحِق <see cref="ListingDeleted"/> — <b>حَذفٌ لَيِّن</b>:
    /// <c>Apply</c> يَرفَع <c>IsDeleted</c> ولا يُزيل تَياراً. وهذا
    /// بِعَينِه ما يَجعَل الزِرَّ قابِلاً لِلعَكس بِحَدَثٍ مُقابِل
    /// يَومَ يُطلَب.</para>
    ///
    /// <para><b>ولا تُرَدّ حِصَّة</b>: <c>QuotaRefunded</c> يَتيمٌ
    /// مُثَبَّت بِسَبَبِه في <c>AppliedEventEmitterTests</c>، ورَدُّ
    /// الحِصَّة هُنا يَرفَعُه بِقَرارٍ لَم يُتَّخَذ — فَالحَذف يَحذِف
    /// ولا يُقَرِّر في الاستِحقاق.</para>
    /// </summary>
    public static async Task<ListingEditResult> DeleteAsync(
        IDocumentSession session, ListingDeleteRequest r, CancellationToken ct = default)
    {
        // نَفسُ تَرتيب `EditAsync` حَرفاً: لا إعلانَ حَيّاً ⇒ `Missing`،
        // ثُمَّ التَخويل. وحَذفٌ ثانٍ يَقَع في الفَرع الأَوَّل — جَوابٌ
        // واحِدٌ لِـ«لا إعلانَ حَيّاً هُنا»، ولا رَمزَ ثانِياً لِنَفس
        // المَعنى (‏انظُر `ListingEditCodes.All`: الخامِسُ حَذَفَه
        // القِياسُ الحَيّ).
        var current = await session.LoadAsync<Listing>(r.ListingId, ct);
        if (current is null || current.IsDeleted) return ListingEditResult.Missing;
        if (!IsOwnedBy(current, r.ActorId)) return ListingEditResult.Reject(ListingEditCodes.NotOwner);

        session.Events.Append(r.ListingId, new ListingDeleted(r.ListingId, DateTime.UtcNow));
        return ListingEditResult.Applied;
    }
}
