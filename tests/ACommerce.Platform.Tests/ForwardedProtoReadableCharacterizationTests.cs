using ACommerce.Platform.Hosting;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَنجو قَبلَ أَن يُقرَأَ X-Forwarded-Proto بِأَنّه يَبني عُنواناً بِمُضيفٍ ومَنفَذ (‏2026-11-11، الإغلاقُ الأَخيرُ لِـW13-4 · W13-4·C2f-R1·1 —
//     SEC-C2f-R1-1 وC2f-PATHS-R1-01) ═══
//
// يُثَبِّتُ قَرارَ الواجِهَةِ الأَمامِيَّةِ (`HostHeaderGuard.ForwardedRefusal`) اليَومَ لِما لا يَتَغَيَّر — ويَبقى المِلَفُّ بِبَصمَتِه بَعدَ التَبديل (القاعِدَة ٣):
// (١) كُلُّ مُخَطَّطٍ يَبني اليَومَ صَفحَةً عَلى كُلِّ مُضيفٍ مَقروء — مُسَجَّلاً عِندَ System.Uri (http، https، ws، ftp، gopher، mailto، news، nntp، uuid، telnet،
//     ldap، net.tcp) أَو عامّاً (javascript، data، urn، ab، a1، a.b، a+b، x-y) — يَمُرُّ بِلا رَدٍّ عَلى مُضيفٍ بِمَنفَذٍ وبِلاه، مُفرَداً ومَعَ https قَبلَه وبَعدَه؛
// (٢) وما يُرَدُّ اليَومَ (1http، 9، +x، java:script، ومَعَ https) يُرَدُّ بِالرَمزِ والرَأسِ والطولِ وعَدَدِ القيَمِ والصِنفِ نَفسِها.
// وما يَتَغَيَّرُ عَمداً لا يُثَبَّتُ هُنا: الحَرفُ الواحِد (a، Z — System.Uri يَقرَأُ «a://h/x» مَسارَ قُرص) وfile وnet.pipe وvsmacros (لا تَبني عُنواناً بِمُضيفٍ
// ومَنفَذ) ← 500 عَلى كُلِّ صَفحَةٍ اليَوم، وقائِمَةٌ تَحمِلُ أَحَدَها.

public class ForwardedProtoReadableCharacterizationTests
{
    private static readonly string[] Hosts = ["localhost:5050", "127.0.0.1:5150", "ashare.localhost:5162", "ashare-vendor.example.test", "[::1]:5050", "example.test:443"];

    private static readonly string[] Readable =
    [
        "http", "https", "HTTPS", "ws", "wss", "ftp", "gopher", "mailto", "news", "nntp", "uuid", "telnet", "ldap", "net.tcp",
        "javascript", "data", "urn", "ab", "a1", "AB", "a.b", "a+b", "x-y",
    ];

    public static IEnumerable<object[]> ReadableOnEveryHost()
        => from host in Hosts
           from proto in Readable
           from shape in new[] { "{0}", "https, {0}", "{0}, https", "{0},http" }
           select new object[] { host, string.Format(System.Globalization.CultureInfo.InvariantCulture, shape, proto) };

    [Theory]
    [MemberData(nameof(ReadableOnEveryHost))]
    public void A_scheme_that_forms_a_page_today_passes_the_front(string host, string proto)
        => Assert.Null(HostHeaderGuard.ForwardedRefusal(new ForwardedHeadersPolicy(), host, null, proto));

    [Theory]
    [InlineData("1http", 5, 1, "other")]
    [InlineData("9", 1, 1, "other")]
    [InlineData("+x", 2, 1, "other")]
    [InlineData("java:script", 11, 1, "port")]
    [InlineData("https, 1http", 12, 2, "other")]
    [InlineData("1http, https", 12, 2, "other")]
    [InlineData("h ttp", 5, 1, "other")]
    public void What_is_refused_today_is_refused_with_the_same_line(string proto, int length, int values, string shape)
    {
        foreach (var host in Hosts)
            Assert.Equal(new HostHeaderGuard.ForwardedFrontRefusal(HostHeaderGuard.ForwardedProtoUnreadable, "X-Forwarded-Proto", length, values, shape),
                HostHeaderGuard.ForwardedRefusal(new ForwardedHeadersPolicy(), host, null, proto));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(",")]
    [InlineData("https,")]
    public void An_absent_or_empty_value_passes_as_today(string? proto)
    {
        foreach (var host in Hosts)
            Assert.Null(HostHeaderGuard.ForwardedRefusal(new ForwardedHeadersPolicy(), host, null, proto));
    }
}
