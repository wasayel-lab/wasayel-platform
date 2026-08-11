using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── تَوصيف قَرار «إلى أَينَ يَجوز أَن نُحَوِّل» ────────────────────────
//
// الدَعوى المَفحوصَة: وِجهَةٌ تُقرَأ مِن الطَّلَب لا تَخرُج بِالمُستَخدِم
// مِن أَصلِنا — أَيّاً كانَ شَكل الالتِفاف. والاختِبارات السالِبَة هي
// المَقصودَة هُنا: الموجَب كانَ يَمُرّ قَبل الإصلاح وبَعدَه، والسالِب
// وَحدَه هُوَ ما تَغَيَّر.
public class LocalRedirectTests
{
    // ─── ١. السالِب — كُلّ صيغَة تَخرُج مِن الأَصل ─────────────────────

    [Theory]
    // مُضيف خارِجيّ بِمُخَطَّط صَريح
    [InlineData("https://example.com")]
    [InlineData("http://evil.com/path")]
    [InlineData("HTTPS://EVIL.COM")]
    // بِلا مُخَطَّط — وهو ما كانَ StartsWith("/") يُمَرِّرُه
    [InlineData("//evil.com")]
    [InlineData("//evil.com/path?x=1")]
    [InlineData(@"/\evil.com")]
    [InlineData(@"/\/evil.com")]
    // مُخَطَّطات أُخرى
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>1</script>")]
    [InlineData("mailto:a@b.c")]
    // مَحرَف تَحَكُّم يَحذِفُه المُتَصَفِّح قَبل التَحليل
    [InlineData("/\tevil")]
    [InlineData("/\nhttps://evil.com")]
    [InlineData("/ /evil.com")]
    // مَسار نِسبيّ بِلا جِذر — يُحَلّ مُقابِل الصَفحَة الحالِيَّة لا مُقابِل الأَصل
    [InlineData("evil.com")]
    [InlineData("../admin")]
    // فارِغ
    [InlineData("")]
    [InlineData(null)]
    public void ForeignDestinations_AreRejected(string? candidate)
    {
        Assert.False(LocalRedirect.IsLocal(candidate));
        Assert.Equal("/", LocalRedirect.Resolve(candidate, "/"));
    }

    [Fact]
    public void ARejectedDestination_FallsBackToWhatTheCallerNamed_NotToARoot()
    {
        // السُقوط لَيسَ ثابِتاً: كُلّ نُقطَة تُسَمّي وِجهَتَها الآمِنَة.
        Assert.Equal("/ashare/listings/7",
            LocalRedirect.Resolve("https://evil.com", "/ashare/listings/7"));
        Assert.Equal("/ashare/me",
            LocalRedirect.Resolve("//evil.com", "/ashare/me"));
    }

    // ─── ٢. المُوجَب — مَسار مَحَلِّيّ يَمُرّ كَما هُوَ ────────────────────

    [Theory]
    [InlineData("/")]
    [InlineData("/ashare")]
    [InlineData("/ashare/explore")]
    [InlineData("/ashare/r/broker/me")]
    [InlineData("/ashare/listings/3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    [InlineData("/ashare/explore?cat=cars&page=2")]
    [InlineData("/ashare/me#tab")]
    [InlineData("/%D8%A5%D8%B9%D9%84%D8%A7%D9%86")]
    public void LocalPaths_PassUnchanged(string candidate)
    {
        Assert.True(LocalRedirect.IsLocal(candidate));
        Assert.Equal(candidate, LocalRedirect.Resolve(candidate, "/"));
    }

    [Fact]
    public void AnEncodedSlashIsNotASlash()
    {
        // ‏%2f لا يَصير فاصِلَ سُلطَة عِندَ المُتَصَفِّح، فَلا يُرفَض.
        // مَكتوب صَراحَةً لِأَنَّه المَوضِع الَّذي يُغري بِمَنعٍ زائِد.
        Assert.True(LocalRedirect.IsLocal("/%2f%2fevil.com"));
    }

    // ─── ٣. الحَدّ نَفسُه — لا نُسخَة ثانِيَة مِن القَرار ────────────────

    [Fact]
    public void NoEndpointDecidesRedirectLocalityOnItsOwn()
    {
        // القاعِدَة ٢ في CLAUDE.md: الحَدّ الَّذي لا يُقاس آليّاً يَنهار.
        // فَهُنا يُقاس: أَيّ عَودَة إلى «‏StartsWith("/")» كَشَرط تَحويل
        // تُسقِط هذا الاختِبار — والبَديل مَوضِع واحِد اسمُه LocalRedirect.
        var raw = File.ReadAllText(Path.Combine(
            ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
            "MarketplaceTemplateExtensions.cs"));

        // التَعليقات تُقَصّ قَبل الفَحص: سَطرٌ يَشرَح الشَرط القَديم لَيسَ
        // الشَرط القَديم — وأَداةٌ لا تُفَرِّق بَينَهُما تَكذِب في
        // الاتِّجاهَين.
        var source = Regex.Replace(raw, @"//[^\r\n]*", string.Empty);

        var handRolled = Regex.Matches(source, @"StartsWith\(""/""\)").Count;
        Assert.Equal(0, handRolled);

        // وكُلّ قِراءَة لِوِجهَة مِن الطَّلَب مَوصولَة بِالقَرار الواحِد.
        var reads = Regex.Matches(source,
            @"(?:req\.Form|req\.Query)\[""(?:return|returnUrl)""\]").Count;
        var guarded = Regex.Matches(source,
            @"LocalRedirect\.Resolve\(\s*(?:req\.Form|req\.Query)\[""(?:return|returnUrl)""\]").Count;
        Assert.True(reads > 0, "لا قِراءَة وِجهَة أَصلاً — الاختِبار أَعمى.");
        Assert.Equal(reads, guarded);
    }
}
