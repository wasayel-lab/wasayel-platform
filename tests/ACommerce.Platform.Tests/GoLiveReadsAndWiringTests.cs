using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>قِراءاتُ المالِكِ وسِلكُ الـcanonical يُقاسانِ لا يُفتَرَضان</b> (‏2026-11-11، W13-4·C2z·7 — مُراجَعَةُ C2a · SEC-C2a-R2-2 وC2a-P3-02).</para>
///
/// <para><b>(أ) قِراءَةُ أَيقونَةِ الـPWA قَبلَ ضَبطِ النِطاق</b>: كانَت <c>curl -sI …/icon.svg | grep -i '^content-security-policy'</c> — و<c>-sI</c> طَلَبُ
/// <c>HEAD</c>، والنُقطَةُ <c>MapGet</c> تُجيبُه <b>405 بِلا CSP</b> (مَقيسٌ عِندَ المُراجِع)، فَالقِراءَةُ تَقولُ «قَبلَ الإصلاح» عَلى بِناءٍ مُصلَح ولا تَنجَحُ أَبَداً.
/// الآن <c>GET</c> بِرُؤوسِه (<c>-s -D - -o /dev/null</c>) — وهِيَ أَعلامُ <c>PWA_READ</c> في <c>scripts/prove-store-hosts.sh</c> حَرفاً (المَجموعَةُ ٢٢)، وهذا يَحمَرُّ
/// إن افتَرَقَت، أَو عادَ <c>-I</c>.</para>
/// <para><b>(ب) سِلكُ G10</b>: كانَ الحارِسُ يَسأَلُ «أَيُنادى <c>CanonicalPath(</c>؟» فَقَط — فَتَبديلُ وَسيطِه الثاني إلى <c>Tenant.Slug</c> (يُسقِطُ <c>/{slug}</c> مِن
/// canonical كُلِّ صَفحَةِ مَتجَرٍ في وَضعِ المَسار) مَرَّ بِالمَجموعَةِ كُلِّها خَضراء (مَقيسٌ عِندَ المُراجِع: «Failed: 14» كَما بِلا تَبديل). الآن يُثَبَّتُ الوَسيطُ: مَتجَرُ
/// المُضيفِ مِن <c>HostSlug(HttpCtx.HttpContext)</c>، وهُوَ وَحدَه ما يُمَرَّرُ مَعَ المَسارِ الظاهِر.</para>
/// </summary>
public class GoLiveReadsAndWiringTests(ITestOutputHelper output)
{
    private static string Read(string rel) => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, rel));

    private static readonly Regex DocIconRead = new(@"`curl (?<flags>[^`]*?) https://R/api/<مَتجَر>/icon\.svg \| tr -d '\\r' \| grep -i '\^content-security-policy'`", RegexOptions.CultureInvariant);
    private static readonly Regex ScriptIconRead = new(@"^PWA_READ=\((?<flags>[^)\n]*)\)$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    [Fact]
    public void The_pwa_icon_read_is_a_get_that_prints_headers_and_the_host_proof_sends_the_same_flags()
    {
        var doc = Read("docs/DEPLOY.md");
        var reads = DocIconRead.Matches(doc).Select(m => m.Groups["flags"].Value).ToList();
        var script = ScriptIconRead.Matches(Read("scripts/prove-store-hosts.sh")).Select(m => m.Groups["flags"].Value).ToList();
        output.WriteLine($"قِراءاتُ الأَيقونَةِ في الدَليل: {reads.Count} ({string.Join(" · ", reads)}) · PWA_READ: {script.Count}");
        Assert.NotEmpty(reads);
        Assert.Single(script);
        Assert.All(reads, f => Assert.Equal(script[0], f));
        Assert.All(reads, f => Assert.DoesNotMatch(@"(^|\s)-[a-zA-Z]*I", f));   // لا HEAD — النُقطَةُ تُجيبُه 405
        Assert.Matches(@"(^|\s)-[a-zA-Z]*I", "-sI");                            // والفاحِصُ يَرى ما كانَ مَكتوباً
    }

    [Fact]
    public void The_canonical_is_wired_from_the_host_store_alone()
    {
        var app = Read("libs/templates/ACommerce.Templates.Customer.Marketplace/Components/App.razor");
        var code = Regex.Replace(app, @"@\*.*?\*@", "", RegexOptions.Singleline);
        Assert.Contains("var seoHostSlug  = ACommerce.Templates.Customer.Marketplace.Services.Ux.StoreHostPage.HostSlug(HttpCtx.HttpContext);", code, StringComparison.Ordinal);
        var calls = Regex.Matches(code, @"StoreHostPage\.CanonicalPath\(\s*(?<a>[^;]*?)\);", RegexOptions.Singleline).Select(m => Regex.Replace(m.Groups["a"].Value, @"\s+", " ")).ToList();
        output.WriteLine($"نِداءاتُ CanonicalPath في الصَدَفَة: {calls.Count} — {string.Join(" | ", calls)}");
        var call = Assert.Single(calls);
        Assert.Equal("ACommerce.Templates.Customer.Marketplace.Services.Ux.StoreHostPage.VisiblePath(HttpCtx.HttpContext), seoHostSlug", call);
        // وحَقنُ العَيبِ الَّذي وَصَفَه المُراجِعُ يُرى
        Assert.NotEqual("ACommerce.Templates.Customer.Marketplace.Services.Ux.StoreHostPage.VisiblePath(HttpCtx.HttpContext), Tenant.IsResolved ? Tenant.Slug : seoHostSlug", call);
    }
}
