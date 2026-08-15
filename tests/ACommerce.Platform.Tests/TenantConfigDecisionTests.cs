using ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>دَوالّ القَرار النَقِيَّة — تُنادى بِلا قاعِدَةِ بَيانات
/// وبِلا خادِم.</b> وهذا هُوَ الرِبح الحَقيقيّ مِن إخراج المَنطِق مِن
/// أَجسام النِقاط: قَبلَ اليَوم، لِفَحص «هَل يُرفَض لَونٌ بِلا
/// <c>#</c>؟» كانَ يَلزَم إقلاعُ مُضيفٍ وجَلسَةُ مُشرِف
/// و<c>curl</c>. الآنَ سَطر.</para>
///
/// <para><b>ولِكُلّ رَمز خَرقٍ اختِبارٌ مُوجَبٌ وسالِب</b> (القاعِدَة ٤):
/// المُوجَب يُثبِت أَنّ الرَمز يَقَع حَيثُ يَجِب، والسالِب أَنَّه لا
/// يَقَع حَيثُ لا يَجِب — ومُصادِقٌ يَرُدّ الرَمزَ دائِماً يَعبُر
/// نِصفَ الاختِبار وَحدَه.</para>
/// </summary>
public class TenantConfigDecisionTests
{
    // ─── الهُوِيَّة البَصَرِيَّة ────────────────────────────────────

    private static BrandingSaveRequest Branding(
        string name = "مَتجَر", string color = "#1A2B3C", string? channel = null) =>
        new(name, "شِعار", "الرِياض", color, channel);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Branding_rejects_a_blank_name(string name) =>
        Assert.Equal(TenantConfigCodes.NameRequired,
            BrandingSaveService.WhyInvalid(Branding(name: name)));

    [Theory]
    [InlineData("1A2B3C")]     // بِلا #
    [InlineData("#1A2B3")]     // خَمسَة
    [InlineData("#1A2B3CD")]   // سَبعَة
    [InlineData("#GGHHII")]    // خارِج السِتَّ عَشرَة
    [InlineData("")]
    public void Branding_rejects_a_colour_that_is_not_six_hex_digits(string color) =>
        Assert.Equal(TenantConfigCodes.ColorInvalid,
            BrandingSaveService.WhyInvalid(Branding(color: color)));

    [Theory]
    [InlineData("#1a2b3c")]
    [InlineData("#ABCDEF")]
    [InlineData("  #123456  ")]   // التَشذيب مِن الخِدمَة لا مِن السَطح
    public void Branding_accepts_a_valid_request(string color) =>
        Assert.Null(BrandingSaveService.WhyInvalid(Branding(color: color)));

    /// <summary><b>والاسم يَسبِق اللَون</b> — لِأَنّ نَموذَجاً بِلا اسم
    /// ولَونٍ فاسِد كانَ يُعطي <c>name_required</c> في المَسارَين قَبلَ
    /// التَوحيد، فَلا يَتَغَيَّر ما يَراه المُستَخدِم.</summary>
    [Fact]
    public void Branding_reports_the_missing_name_before_the_bad_colour() =>
        Assert.Equal(TenantConfigCodes.NameRequired,
            BrandingSaveService.WhyInvalid(Branding(name: "", color: "nope")));

    // ─── الفِئات ───────────────────────────────────────────────────

    [Theory]
    [InlineData("sale")]                 // عَمودٌ واحِد
    [InlineData("| اسم")]                // slug فارِغ
    [InlineData("sale |")]               // تَسمِيَة فارِغَة
    public void Categories_reject_a_row_that_is_not_two_columns(string raw) =>
        Assert.Equal(TenantConfigCodes.BadFormat, CategoriesSaveService.Parse(raw).Code);

    [Theory]
    [InlineData("")]
    [InlineData("\n\n   \n")]
    public void Categories_reject_an_input_with_no_row_at_all(string raw) =>
        Assert.Equal(TenantConfigCodes.Empty, CategoriesSaveService.Parse(raw).Code);

    /// <summary><b>الأَيقونَةُ الافتِراضِيَّة حُسِمَت</b>: 🏷️ لا 🏠.
    /// وثَلاثَةُ مَداخِل تُعطيها: عَمودانِ فَقَط، وعَمودٌ ثالِثٌ
    /// فارِغ، وعَمودٌ ثالِثٌ بِمَسافاتٍ وَحدَها.</summary>
    [Theory]
    [InlineData("sale | لِلبَيع")]
    [InlineData("sale | لِلبَيع |")]
    [InlineData("sale | لِلبَيع |   | rent")]
    public void Categories_default_the_icon_to_the_tag_not_the_house(string raw)
    {
        var (cats, code) = CategoriesSaveService.Parse(raw);
        Assert.Null(code);
        Assert.Equal(CategoriesSaveService.DefaultIcon, cats![0].Icon);
        Assert.Equal("🏷️", cats[0].Icon);
    }

    [Fact]
    public void Categories_keep_an_explicit_icon_and_normalise_slug_and_kind()
    {
        var (cats, code) = CategoriesSaveService.Parse("  SALE | لِلبَيع | 🚚 | RENT  \n\n  cars | سَيّارات  ");
        Assert.Null(code);
        Assert.Equal(2, cats!.Count);

        Assert.Equal("sale", cats[0].Slug);          // مُصَغَّر
        Assert.Equal("لِلبَيع", cats[0].Label);
        Assert.Equal("🚚", cats[0].Icon);            // الصَريحُ يَبقى
        Assert.Equal("rent", cats[0].Kind);          // مُصَغَّر
        Assert.Equal(0, cats[0].SortOrder);

        // التَشذيب: سَطرٌ بِمَسافَةٍ بادِئَة لا يُنتِج slug بِفَراغ.
        Assert.Equal("cars", cats[1].Slug);
        Assert.Equal(1, cats[1].SortOrder);
    }
}
