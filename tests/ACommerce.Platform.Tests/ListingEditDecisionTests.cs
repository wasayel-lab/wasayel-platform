using ACommerce.Kit.Listings;
using ACommerce.Templates.Customer.Marketplace.Services.Listings;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>قَرارُ تَحرير الإعلان — نَقِيٌّ، فَيُفحَص بِلا قاعِدَةِ
/// بَيانات وبِلا خادِم.</b> ونَفسُ عَقدِ
/// <see cref="TenantConfigDecisionTests"/>: لِكُلّ رَمزِ رَفضٍ
/// اختِبارٌ مُوجَبٌ وسالِب — المُوجَبُ يُثبِت أَنّ الرَمزَ يَقَع
/// حَيثُ يَجِب، والسالِبُ أَنَّه لا يَقَع حَيثُ لا يَجِب.
/// ومُصادِقٌ يَرُدّ الرَمزَ دائِماً يَعبُر نِصفَ الاختِبار
/// وَحدَه.</para>
///
/// <para><b>وأَثقَلُ ما يَحرُسُه هذا المِلَفّ لَيسَ حَقلاً بَل
/// تَرتيباً</b>: التَخويلُ يَسبِق تَحَقُّقَ الحُقول (القاعِدَة ٦).
/// غَيرُ المالِك بِعُنوانٍ فاسِدٍ يَجِب أَن يَسمَعَ
/// <c>not_owner</c> لا <c>title_short</c> — وإلّا صارَ خَطَأُ
/// التَحَقُّق قِناعاً لِلثَغرَة: يُخبِرُه أَنّ الإعلانَ قابِلٌ
/// لِلتَحرير لَو أَحسَنَ الحَقل.</para>
/// </summary>
public class ListingEditDecisionTests
{
    private static readonly Guid Owner   = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Someone = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ListingId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime At = new(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);

    private static Listing Current(bool acceptsOffers = false, Guid? owner = null)
    {
        var attrs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ListingEditService.OwnerAttribute] = (owner ?? Owner).ToString(),
            ["photos"] = "[\"/uploads/a.jpg\"]",
        };
        if (acceptsOffers) attrs[ListingEditService.AcceptsOffersAttribute] = "true";

        return new Listing
        {
            Id = ListingId,
            TenantSlug = "theme-demo",
            Title = "شَقَّة قَريبَة مِن الجامِعَة",
            Description = "وَصفٌ قَديم",
            Price = 1000m,
            CategorySlug = "apartments",
            City = "الرِياض",
            District = "المَلَز",
            Attributes = attrs,
        };
    }

    private static ListingEditRequest Request(
        Guid? actor = null,
        string title = "عُنوانٌ جَديد",
        string description = "وَصفٌ قَديم",
        string price = "1000",
        string city = "الرِياض",
        string district = "المَلَز") =>
        new(ListingId, actor ?? Owner, title, description, price, city, district);

    // ─── التَخويل ───────────────────────────────────────────────────

    [Fact]
    public void Rejects_an_actor_who_does_not_own_the_listing() =>
        Assert.Equal(ListingEditCodes.NotOwner,
            ListingEditService.Decide(Current(), Request(actor: Someone), At).Code);

    /// <summary><b>والتَخويلُ يَسبِق الحُقول</b> — هذا هُوَ الاختِبار
    /// الَّذي يَمنَع عَودَةَ شَكل العَطَب المَوصوف في القاعِدَة ٦.</summary>
    [Fact]
    public void Reports_the_wrong_owner_before_any_field_problem() =>
        Assert.Equal(ListingEditCodes.NotOwner,
            ListingEditService.Decide(
                Current(), Request(actor: Someone, title: "أ", price: "-5"), At).Code);

    /// <summary>ومالِكٌ بِلا خاصِّيَّة مالِك أَصلاً (إعلانٌ قَديم
    /// أُنشِئَ قَبلَ <c>owner_id</c>) يُرَدّ — لا يُفتَح لِلجَميع.</summary>
    [Fact]
    public void Rejects_when_the_listing_carries_no_owner_attribute()
    {
        var orphan = Current();
        orphan.Attributes.Remove(ListingEditService.OwnerAttribute);
        Assert.False(ListingEditService.IsOwnedBy(orphan, Owner));
        Assert.Equal(ListingEditCodes.NotOwner,
            ListingEditService.Decide(orphan, Request(), At).Code);
    }

    [Fact]
    public void Accepts_the_owner() =>
        Assert.Null(ListingEditService.Decide(Current(), Request(), At).Code);

    // ─── العُنوان ───────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("أب")]
    [InlineData("  أب  ")]        // التَشذيب مِن الخِدمَة لا مِن السَطح
    public void Rejects_a_title_shorter_than_the_creation_limit(string title) =>
        Assert.Equal(ListingEditCodes.TitleShort,
            ListingEditService.Decide(Current(), Request(title: title), At).Code);

    [Theory]
    [InlineData("أبج")]
    [InlineData("  عُنوانٌ جَديد  ")]
    public void Accepts_a_title_at_or_above_the_limit(string title) =>
        Assert.Null(ListingEditService.Decide(Current(), Request(title: title), At).Code);

    // ─── السِعر ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("غالي")]
    [InlineData("0")]
    [InlineData("-1")]
    public void Rejects_a_price_that_is_not_a_positive_number(string price) =>
        Assert.Equal(ListingEditCodes.PriceInvalid,
            ListingEditService.Decide(Current(), Request(price: price), At).Code);

    /// <summary><b>وصِفرٌ مَقبولٌ حَيثُ قَبِلَه الإنشاء</b>: إعلانٌ
    /// يَقبَل العُروض (طَلَبُ مِشوار) يَترُك السِعرَ لِلسائِق.
    /// شاشَتانِ بِحَدَّينِ مُختَلِفَين تُنتِجانِ إعلاناً يُنشَأ ولا
    /// يُحَرَّر.</summary>
    [Fact]
    public void Accepts_zero_when_the_listing_accepts_offers() =>
        Assert.Null(ListingEditService.Decide(
            Current(acceptsOffers: true), Request(price: "0"), At).Code);

    [Fact]
    public void Rejects_a_negative_price_even_when_the_listing_accepts_offers() =>
        Assert.Equal(ListingEditCodes.PriceInvalid,
            ListingEditService.Decide(
                Current(acceptsOffers: true), Request(price: "-1"), At).Code);

    // ─── «لا فَرق» ──────────────────────────────────────────────────

    [Fact]
    public void Rejects_a_request_that_changes_nothing() =>
        Assert.Equal(ListingEditCodes.NoChange,
            ListingEditService.Decide(
                Current(),
                Request(title: "شَقَّة قَريبَة مِن الجامِعَة", description: "وَصفٌ قَديم",
                        price: "1000", city: "الرِياض", district: "المَلَز"),
                At).Code);

    /// <summary>وحَقلٌ واحِدٌ يَكفي — والسالِبُ هُنا هُوَ أَنّ
    /// <c>no_change</c> لا يَقَع عِندَ أَدنى فَرق.</summary>
    [Fact]
    public void Accepts_a_request_that_changes_exactly_one_field() =>
        Assert.Null(ListingEditService.Decide(
            Current(),
            Request(title: "شَقَّة قَريبَة مِن الجامِعَة", description: "وَصفٌ قَديم",
                    price: "1200", city: "الرِياض", district: "المَلَز"),
            At).Code);

    // ─── شَكلُ الحَدَث — وهذا أَخطَرُ ما في المِلَفّ ────────────────

    /// <summary>
    /// <para><b>الحُقولُ غَيرُ المُتَبَدِّلَة تَخرُج <c>null</c></b>،
    /// و<c>null</c> تَعني «لا تُغَيِّر» بِنَصّ
    /// <c>Listing.Apply(ListingEdited)</c>. فَحَدَثٌ يُعيد كِتابَةَ
    /// كُلّ حَقلٍ بِقيمَتِه يَكذِب على قارِئ التَيار.</para>
    /// </summary>
    [Fact]
    public void Only_the_changed_fields_travel_in_the_event()
    {
        var (ev, code) = ListingEditService.Decide(
            Current(),
            Request(title: "عُنوانٌ جَديد", description: "وَصفٌ قَديم",
                    price: "1000", city: "الرِياض", district: "المَلَز"),
            At);

        Assert.Null(code);
        Assert.NotNull(ev);
        Assert.Equal("عُنوانٌ جَديد", ev!.Title);
        Assert.Null(ev.Description);
        Assert.Null(ev.Price);
        Assert.Null(ev.City);
        Assert.Null(ev.District);
        Assert.Equal(ListingId, ev.Id);
        Assert.Equal(At, ev.At);
    }

    /// <summary>
    /// <para><b>ولا الفِئَةُ ولا الخَصائِصُ تُمَسّان — وهذا لَيسَ
    /// نَقصَ ميزَة بَل حِمايَةٌ مَقيسَة.</b>
    /// <c>Apply(ListingEdited)</c> يَكتُب
    /// <c>Attributes = new(e.Attributes)</c> — أَي <b>استِبدالٌ
    /// كامِل</b>. وفي القامُوس <c>owner_id</c> و<c>photos</c>.
    /// فَحَدَثٌ يَحمِل خَصائِصَ النَموذَجِ وَحدَها يَمحو مالِكَ
    /// الإعلانِ وصُوَرَه في سَطر — ويَختَفي الإعلانُ مِن
    /// «إعلاناتي» بِلا أَن يُحذَف.</para>
    /// </summary>
    [Fact]
    public void The_event_never_carries_a_category_or_an_attribute_bag()
    {
        var (ev, _) = ListingEditService.Decide(Current(), Request(), At);
        Assert.NotNull(ev);
        Assert.Null(ev!.CategorySlug);
        Assert.Null(ev.Attributes);

        // والبُرهانُ حَيّ لا نَصّيّ: طَبِّق الحَدَثَ وتَأَكَّد أَنّ
        // المالِكَ والصُوَرَ والفِئَةَ نَجَت.
        var listing = Current();
        listing.Apply(ev);
        Assert.Equal(Owner.ToString(), listing.Attributes[ListingEditService.OwnerAttribute]);
        Assert.Equal("[\"/uploads/a.jpg\"]", listing.Attributes["photos"]);
        Assert.Equal("apartments", listing.CategorySlug);
        Assert.Equal("عُنوانٌ جَديد", listing.Title);
    }

    // ─── المُعجَم والقامُوس — تَقابُلٌ في الاتِّجاهَين ──────────────

    /// <summary>
    /// <para><b>لِكُلّ رَمزِ رَفضٍ رِسالَةٌ في القامُوس، ولِكُلّ
    /// رِسالَةٍ رَمزٌ حَيّ.</b> والاتِّجاهُ الثاني هُوَ الَّذي
    /// يَكسِب: <c>listings.edit.err_already_deleted</c> بَقِيَ في
    /// القامُوس بَعدَ أَن حَذَفَ <b>القِياسُ الحَيّ</b> رَمزَه
    /// (<c>curl</c> على حَذفٍ مُكَرَّر أَعطى <c>not_owner</c>: المُرَشِّح
    /// يَرُدّ قَبلَ الخِدمَة، فَالرَمزُ لا يَبلُغُه أَحَد). مِفتاحٌ
    /// بِلا رَمز نَصٌّ مُتَرجَمٌ لا يُعرَض أَبَداً — وهُوَ نَفسُ
    /// عائِلَة العَطَب الَّتي يَحرُسُها سِجِلّ اليَتامى، مِن جِهَة
    /// النَصّ لا مِن جِهَة الحَدَث.</para>
    ///
    /// <para>ويَقرَأ <b>نَصَّ</b> المِلَفّ لا قامُوساً يُبنى في
    /// الاختِبار — نَفسُ عِلَّة <c>key_duplicate</c> في
    /// <c>LocaleValidator</c>: القامُوسُ يَبتَلِع المُكَرَّر
    /// صامِتاً.</para>
    /// </summary>
    [Fact]
    public void Every_rejection_code_has_a_message_key_and_no_key_outlives_its_code()
    {
        var dict = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "I18n", "Locales", "ar.json");
        Assert.True(File.Exists(dict), $"أَداة عَمياء: لا قامُوس في {dict}.");

        const string prefix = "listings.edit.err_";
        var text = File.ReadAllText(dict);
        var keys = System.Text.RegularExpressions.Regex
            .Matches(text, "\"" + System.Text.RegularExpressions.Regex.Escape(prefix) + "([a-z0-9_]+)\"\\s*:")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(keys.Count > 0, "أَداة عَمياء: صِفر مِفتاح رِسالَةِ رَفض في القامُوس.");

        var missing = ListingEditCodes.All.Except(keys, StringComparer.Ordinal).ToArray();
        Assert.True(missing.Length == 0,
            "رَمزُ رَفضٍ بِلا رِسالَة في القامُوس — يُعرَض رَمزاً خاماً أَو لا يُعرَض:\n  " +
            string.Join("\n  ", missing.Select(c => prefix + c)));

        var stale = keys.Except(ListingEditCodes.All, StringComparer.Ordinal).ToArray();
        Assert.True(stale.Length == 0,
            "مِفتاحُ رِسالَةٍ لِرَمزٍ لَم يَعُد في المُعجَم — اِرفَعه:\n  " +
            string.Join("\n  ", stale.Select(c => prefix + c)));
    }

    /// <summary>وحَقلٌ يُفرَّغ يَصِل <c>""</c> لا <c>null</c> —
    /// فَالفَرقُ بَينَهُما هُوَ الفَرقُ بَينَ «امحُه» و«لا
    /// تُغَيِّره».</summary>
    [Fact]
    public void Clearing_an_optional_field_travels_as_an_empty_string_not_null()
    {
        var (ev, code) = ListingEditService.Decide(
            Current(),
            Request(title: "شَقَّة قَريبَة مِن الجامِعَة", description: "",
                    price: "1000", city: "الرِياض", district: "المَلَز"),
            At);

        Assert.Null(code);
        Assert.Equal("", ev!.Description);

        var listing = Current();
        listing.Apply(ev);
        Assert.Equal("", listing.Description);
    }
}
