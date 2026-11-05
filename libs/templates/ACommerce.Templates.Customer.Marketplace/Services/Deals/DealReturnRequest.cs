using ACommerce.Kit.Culture;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Patterns;

namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>خَرقٌ واحِدٌ في «طَلَبِ الاستِرجاع» — <c>Code</c> مِن <see cref="DealReturnRequest.All"/>، و<c>MessageAr</c> لِلمُراجِعِ البَشَريّ.
/// شَكلُ <see cref="DealCancelViolation"/> بِحَرفِه (القاعِدَة ٤؛ عائِلَةُ «first» في ‏ADR-038).</summary>
public sealed record DealReturnViolation(string Code, string MessageAr);

/// <summary>مَن يَطلُبُ الاستِرجاع — الحارِسُ في تَوقيعِ <c>DealsService.RequestReturnAsync</c> (القاعِدَة ٦)، نَسَقُ <see cref="DealReviewer"/>: السُلطَةُ
/// مَكتوبَةٌ عَلى الصَفقَةِ نَفسِها (مُشتَريها)، فَالقَرارُ مُقارَنَةُ هُوِيَّة.</summary>
public sealed record DealReturnRequester(Guid ActorId, string ActorName);

/// <summary>
/// <para><b>زِرُّ «طَلَب استِرجاع» — مَتى يَظهَرُ ولِمَن</b> (‏2026-10-29، قَرارُ المالِك د30 «١»): «في متاجر بيع السلع زرّ «طلب استرجاع» على صفحة الصفقة،
/// يظهر للمشتري بعد التسليم ولمدّة سبعة أيام منه، وهي المدّة التي تنصّ عليها المادّة ١٣. الزرّ يُشعر البائع ويُسجَّل في سجلّ الصفقة، ولا يحرّك مالًا. والردّ
/// والاسترداد بين البائع والمشتري، وصاحب المتجر يحدّد سياسته». <b>والوَقتُ يُمَرَّرُ إلى القاعِدَة</b> (<paramref name="now"/> في <see cref="Validate"/>) —
/// فَتَبقى دالَّةً نَقِيَّةً تُقاسُ بِلا ساعَة.</para>
/// <para><b>بَوّابَةٌ عَلى حالَةٍ مَخزونَة تُجيبُ بِأَوَّلِ خَرقٍ بِتَرتيبٍ ثابِت</b> (‏ADR-038، القاعِدَة ٦): الغِياب، ثُمَّ الهُوِيَّة (المُشتَري وَحدَه)، ثُمَّ
/// النَمَط (بَيعُ السِلَعِ وَحدَه)، ثُمَّ التَسليم، ثُمَّ المُدَّة، ثُمَّ «طُلِبَ مِن قَبل».</para>
/// <para><b>ولا إعدادَ لِلمَتجَرِ الآن</b> (القاعِدَة ١): يَومَ يَطلُبُ مَتجَرٌ إيقافَ الزِرِّ أَو تَغييرَ مُدَّتِه يُبنى إعدادٌ بِقائِمَةٍ مُغلَقَةٍ ومُصادِق، والافتِراضُ
/// «مُفَعَّل، سَبعَةُ أَيّام» — هذِه القيمَة.</para>
/// </summary>
public static class DealReturnRequest
{
    /// <summary>نَمَطُ بَيعِ السِلَع (<see cref="DealPatternCatalog"/>) — وَحدَه يَحمِلُ الزِرّ: «يظهر الزرّ في نمط بيع السلع وحده».</summary>
    public const string GoodsPattern = "marketplace";

    /// <summary>سَبعَةُ أَيّامٍ مِن التَسليم — «وهي المدّة التي تنصّ عليها المادّة ١٣» («خلال الأيام السبعة التالية لتاريخ تسلّمه المنتج»).</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    public const string DealNotFound = DealCancelAuthorization.DealNotFound;

    /// <summary>الفاعِلُ لَيسَ مُشتَرِيَ الصَفقَة — الاستِرجاعُ حَقُّه وَحدَه.</summary>
    public const string ActorNotBuyer = "actor_not_buyer";

    /// <summary>الصَفقَةُ لَيسَت بَيعَ سِلعَة (رِحلَة، إيجار، خِدمَة، إعلان، …).</summary>
    public const string PatternNotGoods = "pattern_not_goods";

    /// <summary>لَم تُسَلَّم السِلعَةُ بَعد (لا انتِقالَ إلى «تَسليم» في سِجِلِّها)، أَو أُلغِيَت الصَفقَةُ أَو هي في نِزاع.</summary>
    public const string DealNotDelivered = "deal_not_delivered";

    /// <summary>مَضَت سَبعَةُ الأَيّامِ مِن التَسليم.</summary>
    public const string WindowClosed = "return_window_closed";

    /// <summary>طَلَبَ المُشتَري الاستِرجاعَ لِهذِه الصَفقَةِ مِن قَبل — يُخبَرُ البائِعُ مَرَّة.</summary>
    public const string AlreadyRequested = "return_already_requested";

    /// <summary>المَعجَمُ المُغلَق — سِتَّةٌ لا سابِعَ لَها.</summary>
    public static readonly IReadOnlyList<string> All =
        new[] { DealNotFound, ActorNotBuyer, PatternNotGoods, DealNotDelivered, WindowClosed, AlreadyRequested };

    /// <summary>
    /// <para><b>وُلِدَت مِن سَلَّةِ الشِراء — المُمَيِّزُ الَّذي تَحمِلُه صَفقَةُ بَيعِ السِلَعِ الحَيَّة</b> (‏2026-10-30، تَدقيقُ الموجَة ١١ E-deals-1): <c>pay_method</c> في خَصائِصِها
    /// (<see cref="CheckoutPayMethods.AttributeKey"/>) — يَكتُبُه <c>checkout/submit</c> وَحدَه، والسَلَّةُ لا تُفتَحُ إلّا في واجِهَةِ نَمَطِ «بَيع» (<c>CartDoor.Opens</c> ←
    /// <c>PatternProfile.UsesCart</c>). وصَفقَةُ العَرضِ (<c>/{slug}/listings/{id}/deal</c>) لا تَحمِلُه — إيجاراً كانَت أَو سَكَناً أَو خِدمَةً أَو عَقاراً. دالَّةٌ نَقِيَّة.</para>
    /// </summary>
    public static bool CartBorn(Deal deal)
        => deal.Attributes.TryGetValue(CheckoutPayMethods.AttributeKey, out var method) && !string.IsNullOrWhiteSpace(method);

    // ─── تَجميعُ فِئَةِ الإعلانِ عِندَ الوِلادَة (‏2026-11-05، الموجَة ١٢ G3 · ٨ — D-6؛ تَوصيفُه `1ae136e` — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق) ───────────
    //
    // النَمَطُ مِن المَتجَرِ كُلِّه، والسَلَّةُ تُفتَحُ في مَتجَرِ «بَيع» وإن كانَت فيه فِئَةُ «خِدمات» أَو «فَعّالِيّات» (`PatternOf` يَقرَؤُهُما بَيعاً) — فَكانَ «طَلَب استِرجاع»
    // يُرسَمُ عَلى صَفقَةِ تَنظيفٍ أَو حَفلَةٍ سُلِّمَت. يُناقِضُ د30 «في متاجر بيع السلع». فَتُكتَبُ عَلى الصَفقَةِ عِندَ وِلادَتِها مِن السَلَّةِ تَجميعُ فِئَةِ إعلانِها، وتَقرَؤُه البَوّابَة.

    /// <summary>خاصِّيَّةُ الصَفقَةِ الَّتي يَكتُبُ فيها <c>/checkout/submit</c> تَجميعَ فِئَةِ الإعلان (<see cref="CategoryKinds"/>) — حينَ يَكونُ لَها تَجميع.</summary>
    public const string CategoryKindAttribute = "category_kind";

    /// <summary>التَجميعانِ اللَّذانِ لَيسا بَيعَ سِلعَة — «خِدمات» و«فَعّالِيّات». وما سِواهُما (ومِنه الفارِغ) سِلعَةٌ كَما كان: لا يُختَرَعُ تَصنيفٌ لِتَجميعٍ لَم يُسأَل عَنه.</summary>
    public static readonly IReadOnlyList<string> NotGoodsKinds = new[] { CategoryKinds.Services, CategoryKinds.Events };

    /// <summary>
    /// <para><b>تَجميعُ فِئَةِ الإعلان — دالَّةٌ نَقِيَّة</b>: فِئَةُ المَتجَرِ بِسلاجِ الإعلانِ فَتَجميعُها، فَإن كانَ فارِغاً فَتَجميعُ أَقرَبِ أَبٍ لَه تَجميع (فِئَةٌ فَرعِيَّةٌ تَحتَ «خِدمات»
    /// خِدمَة)؛ و<c>null</c> لِفِئَةٍ لا وُجودَ لَها أَو بِلا تَجميعٍ في سِلسِلَتِها. ولا تَدورُ عَلى شَجَرَةٍ فيها حَلقَة: كُلُّ سلاجٍ يُزارُ مَرَّة.</para>
    /// </summary>
    public static string? CategoryKindOf(IEnumerable<Category>? categories, string? categorySlug)
    {
        if (categories is null || string.IsNullOrWhiteSpace(categorySlug)) return null;
        var bySlug = new Dictionary<string, Category>(StringComparer.Ordinal);
        foreach (var c in categories) bySlug.TryAdd(c.Slug, c);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var slug = categorySlug;
        while (slug is not null && seen.Add(slug) && bySlug.TryGetValue(slug, out var category))
        {
            if (!string.IsNullOrEmpty(category.Kind)) return category.Kind;
            slug = category.ParentSlug;
        }
        return null;
    }

    /// <summary>سِماتُ الوِلادَةِ كَما بَناها المُنادي، ومَعَها <see cref="CategoryKindAttribute"/> حينَ لِلإعلانِ تَجميع — والقاموسُ نَفسُه يُعادُ (نَسَقُ
    /// <see cref="GatewayPayments.MarkedAtBirth"/>). وبِلا تَجميعٍ لا مِفتاحَ يُضاف.</summary>
    public static Dictionary<string, string> KindAtBirth(Dictionary<string, string> attributes, string? kind)
    {
        if (!string.IsNullOrWhiteSpace(kind)) attributes[CategoryKindAttribute] = kind;
        return attributes;
    }

    /// <summary>وُلِدَت عَلى إعلانٍ في فِئَةِ خِدمَةٍ أَو فَعّالِيَّة (<see cref="NotGoodsKinds"/>) — دالَّةٌ نَقِيَّة. وصَفقَةٌ بِلا الخاصِّيَّةِ (وُلِدَت قَبلَها، أَو فِئَتُها بِلا تَجميع) لا.</summary>
    public static bool BornOnNonGoods(Deal deal)
        => deal.Attributes.TryGetValue(CategoryKindAttribute, out var kind) && NotGoodsKinds.Contains(kind, StringComparer.Ordinal);

    /// <summary>لَحظَةُ التَسليم: أَوَّلُ انتِقالٍ إلى <see cref="DealStage.Delivered"/> في سِجِلِّ الصَفقَة (يَكتُبُه البائِعُ أَو وارِدُ OTO)، و<c>null</c> حينَ لا
    /// تَسليم. دالَّةٌ نَقِيَّة.</summary>
    public static DateTime? DeliveredAt(Deal deal)
        => deal.Timeline.FirstOrDefault(e => e.Action == "advanced" && e.StageAfter == DealStage.Delivered)?.At;

    /// <summary>آخِرُ لَحظَةٍ يُقبَلُ فيها الطَلَب — التَسليمُ وسَبعَةُ أَيّام — أَو <c>null</c> حينَ لا تَسليم. تَقرَؤُها الصَفحَةُ لِتَقولَ «حَتّى مَتى».</summary>
    /// <remarks>(‏2026-10-30، تَدقيقُ الموجَة ١١ E-deals-3؛ تَوصيفُه `7b17822` — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): <b>آخِرُ اليَومِ السابِعِ بِتَوقيتِ الرِياض</b> — «خلال الأيام السبعة التالية
    /// لتاريخ تسلّمه» أَيّامٌ تَقويمِيَّةٌ مِن تاريخِ التَسَلُّم، بِقاعِدَةِ المالِكِ لِلتَواريخِ في د51 (<see cref="CultureTime.EndOfDayUtc"/>)؛ كانَ <c>at + Window</c> — ‏168 ساعَةً
    /// مِن النَقرَة — فَتَسليمٌ ‏09:00 يُغلِقُ ‏09:00 اليَومَ الثامِنَ ونَصُّ الرَدِّ «مَضَت سَبعَةُ الأَيّام» كاذِبٌ بَقِيَّةَ يَومِه. والصَفحَةُ والبَوّابَةُ تَقرَآنِها.</remarks>
    public static DateTime? WindowEndsAt(Deal deal)
        => DeliveredAt(deal) is { } at
            ? CultureTime.EndOfDayUtc(DateOnly.FromDateTime(CultureTime.InZone(at)).AddDays(Window.Days))
            : null;

    /// <summary><c>null</c> يَعني مُخَوَّل. والتَرتيبُ مَقصود: الغِياب، ثُمَّ الهُوِيَّة، ثُمَّ النَمَط، ثُمَّ التَسليم، ثُمَّ المُدَّة، ثُمَّ التَكرار.</summary>
    public static DealReturnViolation? Validate(Deal? deal, DealReturnRequester by, DateTime now)
    {
        if (deal is null)
            return new(DealNotFound, "لا صَفقَةَ بِهذا المُعَرِّف.");

        if (deal.InitiatorId != by.ActorId)
            return new(ActorNotBuyer, "لا يَطلُبُ الاستِرجاعَ إلّا مُشتَري الصَفقَة.");

        // (‏2026-10-30، تَدقيقُ الموجَة ١١ E-deals-1؛ تَوصيفُه `d2fed52` — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): **ووُلِدَت مِن سَلَّة** (<see cref="CartBorn"/>) — النَمَطُ
        // وَحدَه لا يُمَيِّز: `marketplace` احتِياطُ المُحَلِّلِ لِكُلِّ مَتجَرٍ بِلا سائِقٍ ولا مُضيف، فَصَفقَةُ عَرضٍ عَلى «شقة» كانَت تَطلُبُ «استِرجاعَ السِّلعَة».
        if (!string.Equals(deal.Pattern, GoodsPattern, StringComparison.Ordinal) || !CartBorn(deal))
            return new(PatternNotGoods, $"صَفقَةُ «{deal.Pattern}» {(CartBorn(deal) ? "" : "بِلا سَلَّةِ شِراءٍ ")}لَيسَت بَيعَ سِلعَة — زِرُّ الاستِرجاعِ لِبَيعِ السِلَعِ وَحدَه.");

        // (‏2026-11-05، الموجَة ١٢ G3 · ٨ — D-6؛ تَوصيفُه `1ae136e` — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): **وإعلانُها في فِئَةِ سِلعَة** — صَفقَةُ خِدمَةٍ أَو فَعّالِيَّةٍ وَلَدَتها السَلَّةُ
        // في مَتجَرِ «بَيع» لَيسَت بَيعَ سِلعَة (د30)؛ والرَمزُ نَفسُه — المَعجَمُ لا يَكبُر.
        if (BornOnNonGoods(deal))
            return new(PatternNotGoods, $"صَفقَةٌ عَلى إعلانٍ في فِئَةِ «{deal.Attributes[CategoryKindAttribute]}» لَيسَت بَيعَ سِلعَة — زِرُّ الاستِرجاعِ لِبَيعِ السِلَعِ وَحدَه.");

        if (deal.Status is not (DealStatus.Active or DealStatus.Completed) || DeliveredAt(deal) is null)
            return new(DealNotDelivered, $"الصَفقَةُ في حالَة {deal.Status} عِندَ «{deal.Stage}» — لَم تُسَلَّم السِلعَة.");

        if (now > WindowEndsAt(deal)!.Value)   // (‏2026-10-30، E-deals-3): الحَدُّ الواحِدُ الَّذي تَقولُه الصَفحَة — كانَ `delivered + Window`
            return new(WindowClosed, "مَضَت سَبعَةُ الأَيّامِ مِن تَسليمِ السِلعَة.");

        if (deal.Timeline.Any(e => e.Action == DealEvent.ReturnRequested))
            return new(AlreadyRequested, "طَلَبتَ استِرجاعَ هذِه الصَفقَةِ مِن قَبل — وأُخبِرَ البائِع.");

        return null;
    }

    public static bool IsAllowed(Deal? deal, DealReturnRequester by, DateTime now) => Validate(deal, by, now) is null;
}

/// <summary>ناتِجُ طَلَبِ الاستِرجاع — شَكلُ <see cref="DealCancelResult"/>: ما كُتِب، أَو أَوَّلُ رَدٍّ أَوقَفَه (ومِنه تَعارُضُ النُسخَة).</summary>
public sealed record DealReturnResult(bool Ok, Deal? Deal, DealReturnViolation? Violation)
{
    public static DealReturnResult Refused(DealReturnViolation v, Deal? deal = null) => new(false, deal, v);

    public static DealReturnResult Requested(Deal deal) => new(true, deal, null);
}
