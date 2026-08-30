using ACommerce.Templates.Customer.Marketplace.Services.Ux;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «مَن يَحمِلُ manifest» — كَما هُوَ اليَوم ═══
//
// القاعِدَة ٣: القَرارُ نُقِلَ مِن `App.razor` إلى دالَّةٍ نَقِيَّةٍ
// **بِلا تَغييرِ حَرفٍ في جَوابِه**، وهذا الجَدوَلُ يُثَبِّتُ الجَوابَ
// قَبلَ أَن يُمَسّ.
//
// **ولِماذا جَدوَلٌ لا لَقطَةُ صَفحَة**: أَثَرُ هذِه القاعِدَةِ لا يُرى
// إلّا بِفَتحِ الصَفحَةِ عَلى جِهاز — والجَدوَلُ يَقيسُها بِلا
// مُتَصَفِّحٍ ولا خادِمٍ ولا قاعِدَةِ بَيانات، فَيُصبِح أَيُّ تَعديلٍ
// لاحِقٍ **مَحصوراً حَيثُ قُصِد بِبُرهان** (القاعِدَة ١٣).
public class PwaManifestDecisionTests
{
    // ─── ١. المَسارات العابِرَة — لا تُثَبَّت ────────────────────────

    [Theory]
    [InlineData("/ashare/login")]
    [InlineData("/ashare/verify")]
    [InlineData("/ashare/terms")]
    [InlineData("/ashare/logout")]
    [InlineData("/ashare/auth/phone/login")]
    [InlineData("/admin/tenants/ashare")]
    public void A_transient_page_carries_no_manifest(string path)
    {
        Assert.True(PwaManifestDecision.IsTransient(path));
        Assert.Null(PwaManifestDecision.Resolve(path, true, "ashare", null));
    }

    [Theory]
    [InlineData("/ashare")]
    [InlineData("/ashare/explore")]
    [InlineData("/injez/r/rider")]
    public void A_normal_page_is_not_transient(string path)
        => Assert.False(PwaManifestDecision.IsTransient(path));

    // ─── ٢. بِلا مُستَأجِرٍ مَحلولٍ لا manifest إطلاقاً ──────────────

    [Theory]
    [InlineData("/")]
    [InlineData("/studio")]
    [InlineData("/studio/apps/ashare")]
    public void A_platform_page_carries_no_manifest(string path)
        => Assert.Null(PwaManifestDecision.Resolve(path, false, "", null));

    // ─── ٣. البَوّابَة — المَقيسُ اليَوم ─────────────────────────────

    [Fact]
    public void A_single_segment_path_is_the_launcher()
    {
        Assert.True(PwaManifestDecision.IsLauncher("/ashare"));
        Assert.True(PwaManifestDecision.IsLauncher("/ashare/"));
        Assert.False(PwaManifestDecision.IsLauncher("/ashare/explore"));
        Assert.False(PwaManifestDecision.IsLauncher("/"));
    }

    /// <summary><b>هذا هُوَ السَطرُ الَّذي كَتَبَ العَطَب</b>: بَوّابَةُ
    /// المَتجَرِ — وهي الصَفحَةُ الَّتي يُشارِكُها صاحِبُه ويَفتَحُها
    /// الزائِر — تَخرُج بِصِفرِ وَسمِ تَثبيت، بَينَما صَفحَةٌ داخِلِيَّةٌ
    /// تَخرُج بِواحِد.</summary>
    [Fact]
    public void The_launcher_carries_no_manifest_today()
        => Assert.Null(PwaManifestDecision.Resolve("/ashare", true, "ashare", null));

    [Theory]
    [InlineData("/ashare/explore")]
    [InlineData("/ashare/listings/x")]
    [InlineData("/ashare/me")]
    public void An_inner_page_of_a_role_less_store_carries_the_slug_manifest(string path)
        => Assert.Equal("/api/ashare",
            PwaManifestDecision.Resolve(path, true, "ashare", null));

    // ─── ٤. مَسارُ الدَور يَفوزُ دائِماً ────────────────────────────

    [Theory]
    [InlineData("/injez/r/rider", "rider")]
    [InlineData("/injez/r/driver/explore", "driver")]
    public void A_role_path_carries_the_role_manifest(string path, string role)
        => Assert.Equal($"/api/injez/r/{role}",
            PwaManifestDecision.Resolve(path, true, "injez", role));

    [Fact]
    public void A_role_path_that_is_transient_still_carries_nothing()
        => Assert.Null(PwaManifestDecision.Resolve(
            "/injez/r/rider/login", true, "injez", "rider"));
}
