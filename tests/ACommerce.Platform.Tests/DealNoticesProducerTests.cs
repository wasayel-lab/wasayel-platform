using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مُنتِجٌ واحِدٌ لِإشعاراتِ الصَفَقات — يُقاسُ ولا يُعَدَّدُ مُنادوه في تَعليق (تَدقيقُ الإطلاق ‏2026-10-24، brief-P1a-2) ═══
//
// **المَقيسُ عِندَ الرَأس** (`1bd4020`): تَعليقُ `Deal.cs` كانَ يَقولُ إنّ `DealNotices` «يُنادى مِن نُقطَتَي `/checkout/submit` و`/accept` وَحدَهُما»،
// ومَواضِعُ نِداءِ `notices.Notify…Async(` في القالِبِ تِسعَة: `/checkout/submit`، وبابُ العَرضِ (`/listings/{id}/deal`)، و`/accept`، و`/cancel`، والاستوديو (القَبولُ
// والإلغاء)، ونُقطَتا الـAPI، و`/payments/verify`. فَالقائِمَةُ في التَعليقِ تَشيخُ مَعَ كُلِّ بابٍ يُضاف (القاعِدَة ٢: الحَدُّ الَّذي لا يُقاسُ يَنهار).
//
// **فَالدَعوى الَّتي تَبقى صادِقَةً هِيَ ما يُقاس هُنا**: «`DealNotices` المُنتِجُ الوَحيدُ لِإشعاراتِ الصَفَقات» — لا مِلَفَّ سِواه يَكتُبُ إشعاراً
// بِنَوعٍ مِن أَنواعِها الأَربَعَة، والقارِئُ الوَحيدُ لِحُروفِها شاشَةُ الإشعارات (مِفتاحُ الأَيقونَة). ومَن نادى الأُنبوبَ يَعُدُّه `DealNoticeDoorsTests`.

public class DealNoticesProducerTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Producer = "libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Deals/DealNotices.cs";
    private const string Reader = "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/Notifications.razor";

    private static readonly string[] Literals = { "\"deal_advanced\"", "\"deal_cancelled\"", "\"deal_paid\"" };
    private static readonly Regex Writes = new(
        @"Type\s*=\s*(""deal(_advanced|_cancelled|_paid)?""|(Services\.Deals\.)?DealNotices\.(NewDealType|AcceptedType|CancelledType|PaidType)\b)",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// <para><b>الفاحِصُ النَقِيّ</b>: لِكُلِّ مِلَفٍّ غَيرِ المُنتِجِ — كِتابَةُ نَوعٍ مِن أَنواعِ الصَفَقات (<c>Type = "deal…"</c> أَو <c>Type = DealNotices.…Type</c>)
    /// خَرق، وحَرفُ نَوعٍ مِنها خارِجَ القارِئِ خَرق. يُرجِعُ «المِلَفّ: ما وُجِد».</para>
    /// </summary>
    internal static List<string> Breaches(IEnumerable<(string Path, string Text)> files)
    {
        var breaches = new List<string>();
        foreach (var (path, text) in files)
        {
            if (path == Producer) continue;
            foreach (Match m in Writes.Matches(text)) breaches.Add($"{path}: {m.Value}");
            if (path == Reader) continue;
            foreach (var lit in Literals)
                if (text.Contains(lit, StringComparison.Ordinal)) breaches.Add($"{path}: {lit}");
        }
        return breaches;
    }

    private static List<(string Path, string Text)> Sources()
    {
        var files = new List<(string, string)>();
        foreach (var root in new[] { "libs", "apps" })
            foreach (var full in Directory.EnumerateFiles(Path.Combine(RepoRoot, root), "*.*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(RepoRoot, full).Replace('\\', '/');
                if (rel.Contains("/bin/") || rel.Contains("/obj/")) continue;
                if (rel.EndsWith(".cs", StringComparison.Ordinal))
                    files.Add((rel, WriteEndpointGuardTests.StripComments(File.ReadAllText(full))));
                else if (rel.EndsWith(".razor", StringComparison.Ordinal))
                    files.Add((rel, WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(full)))));
            }
        return files;
    }

    /// <summary>
    /// <para><b>المُنتِجُ وَحدَه يَكتُبُ</b>: صِفرُ خَرقٍ في كُلِّ <c>.cs</c> و<c>.razor</c> تَحتَ <c>libs/</c> و<c>apps/</c>؛ والمُنتِجُ يَبني إشعاراتِه السِتَّةَ
    /// بِالثَوابِتِ الأَربَعَة (لِكُلِّ بانٍ <c>public static Notification</c> سَطرُ <c>Type =</c> واحِد)؛ والقارِئُ يَحمِلُ الحُروفَ الثَلاثَةَ مَرَّةً واحِدَة.
    /// والعَدّادُ يُطبَعُ ويَفشَلُ تَحتَ ‏100 مِلَفّ (القاعِدَة ١٠).</para>
    /// </summary>
    [Fact]
    public void DealNotices_is_the_only_writer_of_deal_notification_types()
    {
        var files = Sources();
        Assert.True(files.Count >= 100, $"أَداةٌ عَمياء: {files.Count} مِلَفّاً مَفحوصاً.");
        var breaches = Breaches(files);
        Assert.True(breaches.Count == 0, "إشعارُ صَفقَةٍ يُكتَبُ خارِجَ DealNotices:\n" + string.Join("\n", breaches));

        var producer = files.Single(f => f.Path == Producer).Text;
        var builders = Regex.Matches(producer, @"public static Notification \w+For\(").Count;
        var typed = Regex.Matches(producer, @"Type = (NewDealType|AcceptedType|CancelledType|PaidType),").Count;
        // وسَبعَةٌ مُنذُ ‏2026-10-29 (قَرارُ المالِك د8): `CancelledToVendorFor` سابِعُها — بِنَوعِ الإلغاءِ نَفسِه.
        // وتِسعَةٌ مُنذُ ‏2026-10-29 (قَرارُ المالِك د3): `RefundDueFor` و`InvoiceLeftOpenFor` لِصاحِبِ المَتجَر — بِنَوعِ الإلغاءِ نَفسِه.
        // وعَشَرَةٌ مُنذُ ‏2026-10-29 (قَرارُ المالِك د5 (٤)): `PaymentExpiredFor` لِلمُشتَري حينَ تُلغي المَنصَّةُ طَلَبَه بِالمُهلَة — بِنَوعِ الإلغاءِ نَفسِه.
        // وأَحَدَ عَشَرَ مُنذُ ‏2026-10-29 (قَرارُ المالِك د9 (١) «أ»): `InterventionFor` لِلبائِعِ وصاحِبِ المَتجَرِ — بِنَوعِ الطَلَبِ الجَديدِ نَفسِه (لا نَوعَ خامِس).
        // وثَلاثَةَ عَشَرَ مُنذُ ‏2026-10-29 (قَرارُ المالِك د38 (٣) «أ»): `DisputeCancelledFor` و`DisputeResumedFor` لِطَرَفَي الصَفقَةِ بِحَسمِ النِزاع — بِنَوعِ الإلغاءِ
        // وبِنَوعِ الطَلَبِ الجَديدِ (لا نَوعَ جَديد).
        // وأَربَعَةَ عَشَرَ مُنذُ ‏2026-10-29 (قَرارُ المالِك د30): `ReturnRequestedFor` لِلبائِعِ بِطَلَبِ استِرجاع — بِنَوعِ الطَلَبِ الجَديدِ نَفسِه.
        // وخَمسَةَ عَشَرَ مُنذُ ‏2026-10-29 (قَرارُ المالِك د38 (٢)؛ مُراجَعَةُ تَيّارِ S5 الأولى): `CashRefundDueFor` لِصاحِبِ المَتجَرِ بِرَدٍّ نَقدِيٍّ مُستَحَقّ — بِنَوعِ الإلغاءِ نَفسِه.
        // وسِتَّةَ عَشَرَ مُنذُ ‏2026-10-30 (تَدقيقُ الموجَة ١١ CO2): `ExpiryFailedFor` لِصاحِبِ المَتجَرِ حينَ يَيأَسُ مُعالِجُ المُهلَة — بِنَوعِ الإلغاءِ نَفسِه.
        Assert.Equal(16, builders);
        Assert.Equal(builders, typed);

        var reader = files.Single(f => f.Path == Reader).Text;
        foreach (var lit in Literals)
            Assert.Equal(1, Regex.Matches(reader, Regex.Escape(lit)).Count);
        output.WriteLine($"مِلَفّاتٌ مَفحوصَة: {files.Count} · خُروق: {breaches.Count} · بُناةُ المُنتِج: {builders} بِـ{typed} سَطرِ نَوع");
    }

    /// <summary>
    /// <para><b>الفاحِصُ يَحمَرُّ</b> (القاعِدَة ١٠): عَيِّناتٌ مَحقونَة — نَوعٌ حَرفِيٌّ في نُقطَة، وثابِتٌ مِن <c>DealNotices</c> في خِدمَةٍ أُخرى، وحَرفُ نَوعٍ
    /// في مِلَفٍّ غَيرِ القارِئ — تُرَدُّ كُلُّها؛ وما في المُنتِجِ والقارِئِ وإشعارٌ بِنَوعٍ آخَرَ لا يُرَدّ.</para>
    /// </summary>
    [Fact]
    public void The_scanner_flags_injected_samples_and_passes_the_producer_the_reader_and_other_types()
    {
        var flagged = Breaches(new[]
        {
            ("libs/x/Endpoints.cs", "s.Store(new Notification { Type = \"deal_paid\", UserId = u });"),
            ("libs/x/Other.cs", "var n = new Notification { Type = Services.Deals.DealNotices.AcceptedType };"),
            ("libs/x/Seed.cs", "var n = new Notification { Type = \"deal\" };"),
            ("libs/x/Page.razor", "@if (n.Type == \"deal_cancelled\") { }"),
        });
        var files = flagged.Select(b => b[..b.IndexOf(':')]).Distinct().OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "libs/x/Endpoints.cs", "libs/x/Other.cs", "libs/x/Page.razor", "libs/x/Seed.cs" }, files);

        var passed = Breaches(new[]
        {
            (Producer, "Type = AcceptedType, Type = \"deal_paid\""),
            (Reader, "\"deal\" or \"deal_advanced\" or \"deal_cancelled\" or \"deal_paid\" => \"shopping-bag\","),
            ("libs/x/Endpoints.cs", "s.Store(new Notification { Type = \"offer_accepted\" }); var t = \"dealer\";"),
        });
        Assert.Empty(passed);
        output.WriteLine($"المَحقون: {files.Length} مِن 4 مِلَفّاتٍ مَردود ({flagged.Count} خَرقاً) · السَليم: {passed.Count} خَرق");
    }
}
