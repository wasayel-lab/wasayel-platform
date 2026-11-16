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

    // ═══ (ج) كُلُّ قِراءَةٍ نافِذَةٍ في §٢·ي لِصَفحَةٍ أَو نُقطَةِ GET طَلَبُ GET (‏2026-11-11، W13-4·C2f·1 — C2z-PATHS-R3-01) ═══
    //
    // **المَقيسُ قَبل**: قِراءَةُ النَفَقِ في الخُطوَةِ ٨ (`curl -sI https://<slug>.R/` و`<slug>-<role>.R/` ← «200 بِـcf-ray») طَلَبُ HEAD، والتَطبيقُ يُجيبُ HEAD
    // عَلى صَفَحاتِ المَتجَرِ والدَورِ والجَذرِ بِـ**405** (مَقيسٌ عَلى خادِمٍ بِـ`MultiTenancy__BaseDomain=localhost`: GET ← 200) — فَلا تَنجَحُ أَبَداً، وتُرسِلُ
    // المالِكَ إلى (ب) أَو (ج) عَن نَفَقٍ يَعمَل. والعَيبُ نَفسُه الَّذي أَصلَحَته (أ) لِلأَيقونَة وَحدَها. **والقاعِدَة**: لِكُلِّ قِراءَةٍ (العُنوانُ وما بَعدَه بِلا
    // `tr -d '\r'`) آخِرُ ظُهورٍ لَها في القِسمِ — والقِسمُ يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه، فَالتَصحيحُ قِراءَةٌ بِالمِفتاحِ نَفسِه بَعدَها — لا يَحمِلُ `-I`/`--head`
    // ولا `-X HEAD`. **والمُستَثنى بِاسمِه**: قِراءاتُ تَحويلِ الحافَّة — الخُطوَةُ ٣·ب (`http://` لِلعائِلاتِ الثَلاث، Always Use HTTPS) والخُطوَةُ ٢ (`www`) —
    // تُجيبُها Cloudflare بِـ301 قَبلَ أَن يَبلُغَ الطَلَبُ الأَصل.
    private static readonly Regex DocRead = new(@"`(?<cmd>curl [^`]+)`", RegexOptions.CultureInvariant);
    private static readonly Regex HeadFlag = new(@"(^|\s)(-[a-zA-Z]*I|--head|-X\s*HEAD)(\s|$)", RegexOptions.CultureInvariant);

    /// <summary>قِراءاتُ تَحويلِ الحافَّةِ — المُستَثناةُ بِأَسمائِها.</summary>
    public static readonly IReadOnlyList<string> EdgeRedirectReads =
    [
        "http://R/", "http://<slug>.R/", "http://<slug>-<role>.R/",   // الخُطوَةُ ٣·ب
        "https://www.R/pricing",                                        // الخُطوَةُ ٢
    ];

    /// <summary>قِسمُ نِطاقاتِ المَتاجِر (§٢·ي) مِن <c>DEPLOY.md</c> — مِن عُنوانِه إلى القِسمِ التالي.</summary>
    public static string HostsSection(string deploy)
    {
        var start = deploy.IndexOf("\n## ٢·ي.", StringComparison.Ordinal);
        Assert.True(start >= 0, "قِسمُ §٢·ي غائِب");
        var end = deploy.IndexOf("\n## ", start + 5, StringComparison.Ordinal);
        return end < 0 ? deploy[start..] : deploy[start..end];
    }

    /// <summary>القِراءاتُ بِتَرتيبِها: (الأَمرُ، العُنوانُ، المِفتاح).</summary>
    public static IReadOnlyList<(string Command, string Url, string Key)> CurlReads(string section)
        => DocRead.Matches(section).Select(m =>
        {
            var cmd = m.Groups["cmd"].Value.Trim();
            var url = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(t => t.StartsWith("http://", StringComparison.Ordinal)
                          || t.StartsWith("https://", StringComparison.Ordinal) || t.StartsWith("'http", StringComparison.Ordinal))?.Trim('\'') ?? "";
            var pipe = cmd.IndexOf('|') is var bar and >= 0 ? cmd[bar..] : "";
            var key = url + " " + Regex.Replace(pipe.Replace(@"| tr -d '\r' ", "", StringComparison.Ordinal), @"\s+", " ").Trim();
            return (cmd, url, key);
        }).Where(r => r.url.Length > 0).ToList();

    [Fact]
    public void Every_read_in_force_in_the_hosts_section_that_targets_a_page_or_a_get_endpoint_is_a_get()
    {
        var reads = CurlReads(HostsSection(Read("docs/DEPLOY.md")));
        var inForce = reads.GroupBy(r => r.Key).Select(g => g.Last()).ToList();
        var excepted = inForce.Where(r => EdgeRedirectReads.Contains(r.Url)).ToList();
        var judged = inForce.Except(excepted).ToList();
        var superseded = reads.Count - inForce.Count;
        var head = judged.Where(r => HeadFlag.IsMatch(r.Command)).ToList();
        output.WriteLine($"قِراءاتُ curl في §٢·ي: {reads.Count} · نافِذَة: {inForce.Count} · مَنسوخَةٌ بِتَصحيحٍ لاحِق: {superseded} · مُستَثناةٌ بِاسمِها: {excepted.Count} · مَحكومَة: {judged.Count} · HEAD: {head.Count}");
        foreach (var r in head) output.WriteLine($"  ✗ HEAD عَلى صَفحَةٍ أَو نُقطَةِ GET: {r.Command}");

        Assert.True(judged.Count > 0, "صِفرُ قِراءَةٍ مَحكومَة — الماسِحُ أَعمى (القاعِدَة ١٠)");
        Assert.All(EdgeRedirectReads, u => Assert.Contains(excepted, r => r.Url == u));   // المُستَثنى مَوجودٌ لا اسمٌ بِلا قِراءَة
        Assert.Empty(head);
        // والفاحِصُ يَرى ما كانَ مَكتوباً
        Assert.Matches(HeadFlag, "curl -sI https://x/");
        Assert.Matches(HeadFlag, "curl --head https://x/");
        Assert.DoesNotMatch(HeadFlag, "curl -s -o /dev/null -D - https://x/");
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
