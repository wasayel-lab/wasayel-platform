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
}
