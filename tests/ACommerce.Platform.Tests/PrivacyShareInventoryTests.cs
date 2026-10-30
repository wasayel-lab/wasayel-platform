using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «مَن نُشارِكُه» في سِياسَةِ الخُصوصِيَّةِ يَقولُ إنّه «مَقيسٌ مِن الكود» — والقائِمَةُ لَم يَكُن يَقيسُها شَيء ═══
//
// تَدقيقُ الإطلاق ‏brief-P3-5c (صُغرى، ‏2026-10-24 عِندَ `dace7cd`؛ أُعيدَ قِياسُه عِندَ `150753f`): `platform.privacy.s5_intro` يَقولُ «وكُلُّها
// مَقيسَةٌ مِن الكودِ لا مَذكورَةٌ احتِياطاً»، والقائِمَةُ سِتَّةُ مَفاتيحِ `share_*` لَيسَ فيها الناقِل — و`DealShipping.DraftFor` يُرسِلُ إلى OTO
// اسمَ المُشتَري وهاتِفَه وعُنوانَه ومَدينَتَه (مَقيسٌ في `AccountDeletionRetentionTests`)، و`OtoClient.RegisterVendorAsync` بَياناتِ البائِع.
//
// **فَالقِياسُ هُنا جَردُ كُلِّ عَميلٍ صادِرٍ في `libs`** (صِنفٌ يَحمِلُ `HttpClient` أَو `WebPushClient` حَقلاً أَو وَسيطاً — الكودُ لا التَعليق)،
// ولِكُلٍّ مِنها إمّا مِفتاحُ `share_*` يُرسَمُ في `/privacy`، وإمّا سَطرٌ في <see cref="PinnedUnlisted"/> بِسَبَبِه — **قائِمَةٌ تَتَقَلَّصُ ولا
// تَنمو إلّا بِقَرارٍ مَرئِيٍّ في مُراجَعَة** (القاعِدَة ٢). وعَميلٌ جَديدٌ لا يُذكَرُ في أَيٍّ مِنهُما يُحمِّرُ الصَفّ.
public class PrivacyShareInventoryTests(ITestOutputHelper output)
{
    /// <summary>عَميلٌ صادِرٌ مُسَمّىً في قائِمَةِ «مَن نُشارِكُه» — بِمِفتاحِه.</summary>
    internal static readonly IReadOnlyDictionary<string, string> Listed = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["BrevoEmailChannel"]     = "platform.privacy.share_otp",
        ["TwilioSmsChannel"]      = "platform.privacy.share_otp",
        ["NafathChannel"]         = "platform.privacy.share_nafath",
        ["PayPalPaymentProvider"] = "platform.privacy.share_payment",
        ["PaddlePaymentProvider"] = "platform.privacy.share_payment",
        // ‏(2026-10-25، مُراجَعَةُ تَيّارِ W8-4 الجَولَة ١ — نُقِلَ إلى `PinnedUnlisted`): `MoyasarClient` كانَ هُنا تَحتَ `share_payment`، ونَصُّه
        // يُسَمّي «عَمَلِيَّةَ الاشتِراكِ» وَحدَها — فَالجَردُ كانَ يَعُدُّ مُسَمّىً ما لا يُسَمّيه النَصّ.
        // ‏(2026-10-25، كوميتُ المِفتاح — نُقِلَ مِن `PinnedUnlisted`): المُستَلِمُ في `DraftFor` والبائِعُ في `RegisterVendorAsync`.
        ["OtoClient"]             = "platform.privacy.share_carrier",
        // ‏(2026-10-29، قَرارُ المالِك د20 «أ» — نُقِلَت مِن `PinnedUnlisted` حَتّى فَرَغَت): كَتَبَ المالِكُ سَطرَ كُلِّ جِهَةٍ بِاسمِها وفِئَتِها.
        ["AnthropicBackend"]      = "platform.privacy.share_llm",
        ["GeminiBackend"]         = "platform.privacy.share_llm",
        ["OpenAIBackend"]         = "platform.privacy.share_llm",
        ["WebPushService"]        = "platform.privacy.share_push",
        ["MoyasarClient"]         = "platform.privacy.share_gateway",
        // ‏(2026-10-29، د20) والجَردُ كانَ أَعمى عَن عَميلَينِ لا يَحمِلانِ `HttpClient` (القاعِدَة ١٠ — صَفُّ النَمَطِ أَدناه): قَناةُ SMTP تُرسِلُ
        // البَريدَ والرَمزَ إلى مُضيفِها بِـ`SmtpClient`، ومَخزَنُ الصُوَرِ يَرفَعُ الصورَةَ الشَخصِيَّةَ وصُوَرَ الإعلاناتِ بِـ`IAmazonS3` — وهو
        // في الإنتاجِ إلزامِيّ (`docs/DEPLOY.md` §٢·ز: «بِدونِها لا تُرفَع صورَةٌ»). فَلَم يَكُن في `PinnedUnlisted` ولا هُنا، لِأَنَّ الأَداةَ لَم تَرَه.
        ["SmtpEmailChannel"]      = "platform.privacy.share_otp",
        ["S3FileStorage"]         = "platform.privacy.share_files",
    };

    /// <summary>عَميلٌ صادِرٌ لا يُسَمّيه نَصُّ السِياسَةِ اليَوم — بِسَبَبِه، وصِياغَتُه قَرارُ المالِك («ب» W8-4 · ٥).
    /// <para><b>‏(2026-10-29، قَرارُ المالِك د20) فارِغَة</b>: الخَمسَةُ الَّتي كانَت هُنا — <c>AnthropicBackend</c> و<c>GeminiBackend</c>
    /// و<c>OpenAIBackend</c> («نَصُّ الفِكرَةِ ومُحادَثَةُ الوَكيلِ إلى مُزَوِّدِ نَموذَجِ اللُغَة»)، و<c>WebPushService</c> («عُنوانُ اشتِراكِ
    /// الجِهازِ وحُمولَةٌ مُشَفَّرَةٌ إلى خِدمَةِ إشعاراتِ المُتَصَفِّح»)، و<c>MoyasarClient</c> («فاتورَةُ دَفعِ الطَلَبِ … إلى بَوّابَةِ دَفعِ
    /// المَتجَر») — صارَت سُطوراً في <see cref="Listed"/>. <b>والقائِمَةُ تَبقى</b>: عَميلٌ صادِرٌ جَديدٌ لا يُسَمّيه النَصُّ يَدخُلُها بِسَبَبِه
    /// في مُراجَعَةٍ مَرئِيَّة، أَو يُحمِّرُ الصَفّ.</para></summary>
    internal static readonly IReadOnlyDictionary<string, string> PinnedUnlisted = new Dictionary<string, string>(StringComparer.Ordinal);

    // ‏(2026-10-29، د20): وعَميلا SMTP وS3 — المَحمولُ حَقلاً (`IAmazonS3 _client;`) والمُنشَأُ مَحَلِّيّاً (`using var c = new SmtpClient {`).
    private static readonly Regex HoldsClient = new(
        @"(?<!\w)(?:(?:HttpClient|WebPushClient|SmtpClient|IAmazonS3|AmazonS3Client)\s+[_A-Za-z]\w*\s*[;=,)]|new\s+(?:SmtpClient|AmazonS3Client)\s*[({])",
        RegexOptions.Compiled);
    private static readonly Regex TypeDecl = new(@"\b(?:class|record)\s+(\w+)", RegexOptions.Compiled);

    /// <summary>كُلُّ صِنفٍ في <c>libs</c> يَحمِلُ عَميلَ شَبَكَةٍ صادِراً — بِاسمِه ومِلَفِّه.</summary>
    internal static IReadOnlyList<(string Type, string File)> OutboundClients()
    {
        var found = new List<(string, string)>();
        var libs = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs");
        foreach (var f in Directory.EnumerateFiles(libs, "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            var code = WriteEndpointGuardTests.StripComments(File.ReadAllText(f));
            var decls = TypeDecl.Matches(code);
            for (var i = 0; i < decls.Count; i++)
            {
                var end = i + 1 < decls.Count ? decls[i + 1].Index : code.Length;
                if (HoldsClient.IsMatch(code[decls[i].Index..end]))
                    found.Add((decls[i].Groups[1].Value, Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, f)));
            }
        }
        return found;
    }

    internal static string PrivacyPage() => UpgradePromptPriceTests.Code("libs", "templates", "ACommerce.Templates.Customer.Marketplace",
        "Components", "Pages", "Platform", "PlatformPrivacy.razor");

    [Fact]
    public void Every_outbound_client_is_either_named_in_the_privacy_share_list_or_pinned_as_unlisted()
    {
        var clients = OutboundClients();
        var names = clients.Select(c => c.Type).ToHashSet(StringComparer.Ordinal);
        output.WriteLine($"عُمَلاءُ صادِرونَ في libs: {clients.Count} · مُسَمَّونَ في السِياسَة: {names.Count(Listed.ContainsKey)}" +
                         $" · مُثَبَّتونَ غَيرَ مُسَمَّين: {names.Count(PinnedUnlisted.ContainsKey)} ({string.Join(", ", PinnedUnlisted.Keys)})");

        Assert.True(clients.Count >= 8, $"أَداةٌ عَمياء: {clients.Count} عَميلاً صادِراً.");
        Assert.Empty(Listed.Keys.Intersect(PinnedUnlisted.Keys));
        var unaccounted = names.Where(n => !Listed.ContainsKey(n) && !PinnedUnlisted.ContainsKey(n)).ToList();
        Assert.True(unaccounted.Count == 0, "عَميلٌ صادِرٌ لا تُسَمّيه السِياسَةُ ولا القائِمَةُ المُثَبَّتَة: " + string.Join("، ", unaccounted));
        var stale = Listed.Keys.Concat(PinnedUnlisted.Keys).Where(n => !names.Contains(n)).ToList();
        Assert.True(stale.Count == 0, "سَطرٌ لِعَميلٍ لَم يَعُد في الكود (القائِمَةُ تَتَقَلَّص): " + string.Join("، ", stale));

        var page = PrivacyPage();
        foreach (var key in Listed.Values.Distinct())
        {
            Assert.NotNull(LocaleCatalog.Find(LocaleCatalog.Arabic, key));
            Assert.NotNull(LocaleCatalog.Find("en", key));
            // ‏(2026-10-29، د20) والمِفتاحُ قَد يَحمِلُ وُسَطاءَ (أَسماءً مِن الإعداد) — فَالبادِئَةُ لا القَوسُ المُغلَق.
            Assert.Contains($"@doc.Markup(\"{key}\"", page, StringComparison.Ordinal);
        }
    }

    /// <summary>الفاحِصُ يَرى ما يَعُدّ (القاعِدَة ١٠): أَشكالُ الحَملِ تُلتَقَط، والذِكرُ في تَعليقٍ أَو تَسجيلٍ لا يُعَدّ.</summary>
    [Theory]
    [InlineData("private readonly HttpClient _http;", true)]
    [InlineData("public OtoClient(HttpClient http, IOptions<OtoOptions> o)", true)]
    [InlineData("private static readonly HttpClient Http = new()", true)]
    [InlineData("var c = new WebPushClient(); WebPushClient client = c;", true)]
    // ‏الاسمُ المُؤَهَّلُ يُعَدّ — مَقيسٌ: صِنفٌ مَحقونٌ بِـ`System.Net.Http.HttpClient _http` مَرَّ مِن النَمَطِ الأَوَّلِ (`(?<![\w.])`) أَخضَر.
    [InlineData("private readonly System.Net.Http.HttpClient _http = new();", true)]
    // ‏(2026-10-29، د20) — مَقيسٌ: صِفّانِ أَعمَيانِ قَبلَها، `SmtpEmailChannel` و`S3FileStorage`.
    [InlineData("using var client = new SmtpClient { Timeout = 5000 };", true)]
    [InlineData("private readonly IAmazonS3 _client;", true)]
    [InlineData("_client = new AmazonS3Client(creds, cfg);", true)]
    [InlineData("// SmtpClient.Timeout وَحدَه لا يَكفي", false)]
    [InlineData("services.AddHttpClient<MoyasarClient>(c => c.Timeout = t);", false)]
    [InlineData("_http.SendAsync(req)", false)]
    public void The_client_pattern_sees_what_it_counts(string code, bool holds)
        => Assert.Equal(holds, HoldsClient.IsMatch(code));

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-29، قَرارُ المالِك د20)</b> — كانَ <c>The_privacy_share_list_renders_seven_keys_and_names_the_carrier_with_what_the_code_sends_it</c>
    /// (‏2026-10-25، كوميتُ المِفتاح؛ وقَبلَه <c>Today_the_privacy_share_list_renders_six_keys_and_none_names_the_carrier</c> في ‏<c>5ad4e05</c>): «سَبعَةُ مَفاتيح
    /// · مُثَبَّتونَ غَيرَ مُسَمَّين: 5». <b>والآن اثنا عَشَر والمُثَبَّتَةُ فارِغَة</b>: بَوّابَةُ المَتجَرِ بَعدَ دَفعِ الاشتِراك، ونَموذَجُ اللُغَةِ وإشعاراتُ
    /// المُتَصَفِّحِ والاستِضافَةُ ومَخزَنُ الصُوَرِ بَعدَ الناقِل، والسِتُّ القَديمَةُ بِتَرتيبِها. والمِفتاحُ قَد يَحمِلُ وُسَطاءَ فَيُقرَأُ حَتّى الفاصِلَة.
    /// ونَصُّ الناقِلِ لَم يُمَسّ — يُقاسُ أَدناهُ كَما كان.</summary>
    [Fact]
    public void The_privacy_share_list_renders_twelve_keys_and_pins_no_party_unnamed()
    {
        var page = PrivacyPage();
        var keys = Regex.Matches(page, @"@doc\.Markup\(""(?<k>platform\.privacy\.share_\w+)""[,)]").Select(m => m.Groups["k"].Value).ToList();
        output.WriteLine($"مَفاتيحُ share_* في /privacy: {keys.Count} ({string.Join(", ", keys)}) · مُثَبَّتونَ غَيرَ مُسَمَّين: {PinnedUnlisted.Count}");
        Assert.Equal(new[]
        {
            "platform.privacy.share_otp", "platform.privacy.share_nafath", "platform.privacy.share_payment", "platform.privacy.share_gateway",
            "platform.privacy.share_carrier", "platform.privacy.share_llm", "platform.privacy.share_push", "platform.privacy.share_hosting",
            "platform.privacy.share_files", "platform.privacy.share_fonts", "platform.privacy.share_maps", "platform.privacy.share_legal",
        }, keys);
        Assert.DoesNotContain("OtoClient", PinnedUnlisted.Keys);
        Assert.Empty(PinnedUnlisted);

        var ar = AccountDeletionRetentionTests.Bare(LocaleCatalog.Find(LocaleCatalog.Arabic, "platform.privacy.share_carrier")!);
        foreach (var said in new[] { "OTO", "اسمك", "هاتفك", "عنوان التوصيل", "مدينته", "بريده", "جواله", "عنوان فوترته" })
            Assert.True(ar.Contains(said, StringComparison.Ordinal), $"share_carrier لا يَقولُ «{said}»: {ar}");
        // وما يَقولُه هُوَ ما يُرسَل: الحُقولُ الشَخصِيَّةُ في الجِسمَينِ بِأَسمائِها.
        var oto = UpgradePromptPriceTests.Code("libs", "kits", "Delivery", "ACommerce.Kit.Delivery.Core", "OtoClient.cs");
        foreach (var field in new[] { "[\"name\"]    = draft.Customer.Name", "[\"mobile\"]  = draft.Customer.Mobile", "[\"address\"] = draft.Customer.Address",
                                      "[\"email\"]           = vendor.Email", "[\"mobileNumber\"]    = vendor.MobileNumber", "[\"billingAddress\"]  = vendor.BillingAddress" })
            Assert.Contains(field, oto, StringComparison.Ordinal);
        Assert.Contains("OTO", LocaleCatalog.Find("en", "platform.privacy.share_carrier")!, StringComparison.Ordinal);
    }

    // ─── مَن يَطلُبُ مِنه المُتَصَفِّحُ مَباشَرَةً (‏2026-10-30، تَدقيقُ الموجَة ١١ B-PRIV-5) ─────────────────────────────────────────────
    //
    // الجَردُ أَعلاه يَعُدُّ عُمَلاءَ الخادِمِ الصادِرينَ وَحدَهُم، فَما يَطلُبُه مُتَصَفِّحُ الزائِرِ مِن مُضيفٍ خارِجِيٍّ — الخَطُّ ومَكتَبَةُ الاتِّصالِ الحَيّ — لا يَراه:
    // العَمى نَفسُه الَّذي أَصلَحَه د20 لِـSMTP وR2. وd20 «تُسمّى كل الجهات بأسمائها»، و`share_fonts` السَطرُ الوَحيدُ بِلا جِهَةٍ مُسَمّاة.

    /// <summary>جِهَةُ كُلِّ مُضيفٍ خارِجِيٍّ يَطلُبُ مِنه المُتَصَفِّحُ مِلَفّاً — مَعجَمٌ مُغلَق: مُضيفٌ جَديدٌ بِلا سَطرٍ هُنا يُحمِرُّ الصَفّ.</summary>
    internal static readonly IReadOnlyDictionary<string, string> BrowserHostParties = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["fonts.googleapis.com"] = "Google Fonts",
        ["fonts.gstatic.com"]    = "Google Fonts",
        ["cdn.jsdelivr.net"]     = "jsDelivr",
        ["cdn.paddle.com"]       = "Paddle",
    };

    private static readonly Regex BrowserFetch = new(
        @"<(?:link|script|img|iframe|source)\b[^>]*?\b(?:href|src)\s*=\s*[""']https?://(?<h>[A-Za-z0-9.-]+)|url\(\s*['""]?https?://(?<h>[A-Za-z0-9.-]+)|@import\s+['""]https?://(?<h>[A-Za-z0-9.-]+)",
        RegexOptions.Compiled);

    /// <summary>كُلُّ مُضيفٍ خارِجِيٍّ في وُسومِ <c>link/script/img/iframe/source</c> وفي <c>url(…)</c>/<c>@import</c> — في كُلِّ <c>.razor</c>/<c>.html</c>/<c>.css</c>
    /// تَحتَ <c>libs</c> و<c>apps</c> (‏<c>App.razor</c> وأَوراقُ الأَنماطِ مِنها) — بِمِلَفِّه.</summary>
    internal static IReadOnlyList<(string Host, string File)> BrowserHosts()
    {
        var found = new List<(string, string)>();
        foreach (var root in new[] { "libs", "apps" })
            foreach (var f in Directory.EnumerateFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, root), "*.*", SearchOption.AllDirectories))
            {
                var sep = Path.DirectorySeparatorChar;
                if (f.Contains($"{sep}bin{sep}") || f.Contains($"{sep}obj{sep}") || !(f.EndsWith(".razor") || f.EndsWith(".html") || f.EndsWith(".css"))) continue;
                foreach (Match m in BrowserFetch.Matches(File.ReadAllText(f)))
                    found.Add((m.Groups["h"].Value, Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, f)));
            }
        return found;
    }

    /// <summary>الجِهاتُ الَّتي لا يُسَمّيها نَصُّ <c>share_*</c> بِالعَرَبِيَّةِ أَو بِالإنجليزِيَّة.</summary>
    internal static IReadOnlyList<string> UnnamedBrowserParties(IEnumerable<string> hosts)
    {
        var page = PrivacyPage();
        var keys = Regex.Matches(page, @"@doc\.Markup\(""(?<k>platform\.privacy\.share_\w+)""[,)]").Select(m => m.Groups["k"].Value).Distinct().ToList();
        var ar = string.Join(" ", keys.Select(k => LocaleCatalog.Find(LocaleCatalog.Arabic, k) ?? ""));
        var en = string.Join(" ", keys.Select(k => LocaleCatalog.Find("en", k) ?? ""));
        return hosts.Select(h => BrowserHostParties.TryGetValue(h, out var p) ? p : $"«{h}» بِلا جِهَة")
            .Distinct(StringComparer.Ordinal)
            .Where(p => !ar.Contains(p, StringComparison.Ordinal) || !en.Contains(p, StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-30، B-PRIV-5)</b> — كانَ <c>Today_the_browser_fetches_from_google_fonts_and_jsdelivr_and_no_share_line_names_them</c>
    /// (‏<c>410fc75</c>: «جِهاتٌ غَيرُ مُسَمّاة: Google Fonts, jsDelivr»). والآن كُلُّ مُضيفٍ يَطلُبُ مِنه المُتَصَفِّحُ لَه جِهَةٌ في <see cref="BrowserHostParties"/>، وكُلُّ جِهَةٍ مُسَمّاةٌ
    /// في سَطرِ <c>share_*</c> بِالعَرَبِيَّةِ والإنجليزِيَّة — وسَطرُ الخَطِّ يُسَمّي الاثنَين.</summary>
    [Fact]
    public void Every_host_the_browser_fetches_from_has_its_party_named_in_a_share_line()
    {
        var hosts = BrowserHosts();
        var distinct = hosts.Select(h => h.Host).Distinct(StringComparer.Ordinal).OrderBy(h => h, StringComparer.Ordinal).ToList();
        var unnamed = UnnamedBrowserParties(distinct);
        output.WriteLine($"طَلَباتُ مُتَصَفِّحٍ إلى مُضيفٍ خارِجِيّ: {hosts.Count} في {hosts.Select(h => h.File).Distinct().Count()} مِلَفّ · مُضيفون: {distinct.Count} " +
                         $"({string.Join(", ", distinct)}) · جِهاتٌ غَيرُ مُسَمّاة: {unnamed.Count}");
        Assert.True(hosts.Count > 0, "أَداةٌ عَمياء: صِفرُ طَلَبٍ خارِجِيّ.");
        Assert.Contains(hosts, h => h.File.EndsWith("App.razor", StringComparison.Ordinal));
        Assert.Contains(hosts, h => h.File.EndsWith("app.css", StringComparison.Ordinal));
        Assert.True(unnamed.Count == 0, "جِهَةٌ يَطلُبُ مِنها المُتَصَفِّحُ ولا يُسَمّيها سَطر: " + string.Join("، ", unnamed));
        var fonts = (LocaleCatalog.Find(LocaleCatalog.Arabic, "platform.privacy.share_fonts")!, LocaleCatalog.Find("en", "platform.privacy.share_fonts")!);
        foreach (var party in new[] { "Google Fonts", "jsDelivr" })
            Assert.True(fonts.Item1.Contains(party, StringComparison.Ordinal) && fonts.Item2.Contains(party, StringComparison.Ordinal), $"share_fonts لا يُسَمّي {party}.");
        var stale = BrowserHostParties.Keys.Where(h => !distinct.Contains(h)).ToList();
        Assert.True(stale.Count == 0, "سَطرٌ لِمُضيفٍ لَم يَعُد يُطلَبُ مِنه (المَعجَمُ يَتَقَلَّص): " + string.Join("، ", stale));
    }

    /// <summary>الفاحِصُ يَرى ما يَعُدّ (القاعِدَة ١٠): الوُسومُ والاستيرادُ يُلتَقَطان، والرابِطُ لِلتَصَفُّحِ وفَضاءُ أَسماءِ SVG لا.</summary>
    [Theory]
    [InlineData("<link rel=\"stylesheet\" href=\"https://fonts.googleapis.com/css2?family=Cairo\" />", "fonts.googleapis.com")]
    [InlineData("<script src=\"https://cdn.jsdelivr.net/npm/x.js\"></script>", "cdn.jsdelivr.net")]
    [InlineData("@import url('https://fonts.googleapis.com/css2?family=Rubik');", "fonts.googleapis.com")]
    [InlineData("background: url(https://tiles.example.org/a.png);", "tiles.example.org")]
    [InlineData("<a href=\"https://moyasar.com/docs\">", "")]
    [InlineData("background: url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg'%3E\");", "")]
    public void The_browser_fetch_pattern_sees_what_it_counts(string code, string host)
        => Assert.Equal(host, BrowserFetch.Matches(code).Select(m => m.Groups["h"].Value).FirstOrDefault() ?? "");

    // ─── والرابِطُ الَّذي يَبنيه الكود (‏2026-10-30، الموجَة ١٢ F2 · ٢ — مُراجَعَةُ c644737 الأَخيرَة) ─────────────────────────────────────
    //
    // `BrowserFetch` يَحتاجُ `src="https://…` حَرفِيّاً في الوَسم، فَما يَبنيه الكودُ لا يَراه: `AcMapPin.razor` يَكتُبُ
    // `<iframe src="@ACommerce.Kit.Maps.OsmLinks.EmbedUrl(...)">` فَيَطلُبُ مُتَصَفِّحُ كُلِّ زائِرٍ لِصَفحَةِ إعلانٍ بِمَوقِع `www.openstreetmap.org`
    // وفي الرابِطِ إحداثِيّاتُ الإعلانِ (الصُندوقُ والعَلامَة) — والجَردُ عَدَّ أَربَعَةَ مُضيفينَ والمُتَصَفِّحُ يَبلُغُ خَمسَة (القاعِدَة ١٠: أَداةٌ عَمياء).
    // فَهُنا: كُلُّ `src`/`href` في وُسومِ الجَلبِ قيمَتُه نِداءُ دالَّة (`@Ns.Type.Method(`) يُحَلُّ إلى جِسمِ الدالَّةِ في مَصدَرِ `libs`/`apps`، ومُضيفُ كُلِّ
    // سِلسِلَةٍ `"https://…` فيه يُعَدّ. ونِداءٌ لا يُحَلُّ يُعَدُّ ويُقال — لا يُبتَلَع.

    private static readonly Regex CodeBuiltFetch = new(
        @"<(?:link|script|img|iframe|source)\b[^>]*?\b(?:href|src)\s*=\s*[""']@(?<call>[A-Za-z_][\w.]*)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex UrlLiteral = new(@"""https?://(?<h>[A-Za-z0-9.-]+)", RegexOptions.Compiled);

    /// <summary>جِسمُ الدالَّةِ <paramref name="method"/> (تُعيدُ <c>string</c>) في النَوعِ <paramref name="type"/> — مِن تَوقيعِها إلى العُضوِ التالي. نَقِيَّة.</summary>
    private static string? MethodBody(string code, string type, string method)
    {
        var decl = Regex.Match(code, $@"\b(?:class|record|struct)\s+{Regex.Escape(type)}\b");
        if (!decl.Success) return null;
        var sig = Regex.Match(code[decl.Index..], $@"\bstring\s+{Regex.Escape(method)}\s*\(");
        if (!sig.Success) return null;
        var start = decl.Index + sig.Index + sig.Length;
        var next = Regex.Match(code[start..], @"\n\s*(?:public|private|internal|protected)\s|\n\}");
        return code[start..(next.Success ? start + next.Index : code.Length)];
    }

    /// <summary><b>المُضيفونَ في روابِطَ يَبنيها الكود</b> — لِكُلِّ نِداءِ دالَّةٍ في <c>src</c>/<c>href</c> وُسومِ الجَلب: الدالَّةُ في <paramref name="code"/>
    /// ومُضيفُ كُلِّ سِلسِلَةٍ <c>"https://…</c> في جِسمِها؛ والنِداءُ الَّذي لا تُوجَدُ دالَّتُه في <c>Unresolved</c>. دالَّةٌ نَقِيَّةٌ عَلى نُصوصٍ مُعطاة.</summary>
    internal static (IReadOnlyList<(string Host, string File, string Call)> Hosts, int Calls, IReadOnlyList<string> Unresolved) CodeBuiltHosts(
        IEnumerable<(string File, string Text)> markup, IEnumerable<(string File, string Text)> code)
    {
        var sources = code.Select(c => WriteEndpointGuardTests.StripComments(c.Text)).ToList();
        var hosts = new List<(string, string, string)>();
        var unresolved = new List<string>();
        var calls = 0;
        foreach (var (file, text) in markup)
            foreach (Match m in CodeBuiltFetch.Matches(text))
            {
                calls++;
                var call = m.Groups["call"].Value;
                var parts = call.Split('.');
                var bodies = parts.Length < 2 ? new List<string>()
                    : sources.Select(c => MethodBody(c, parts[^2], parts[^1])).OfType<string>().ToList();
                if (bodies.Count == 0) { unresolved.Add(call); continue; }
                foreach (var body in bodies)
                    foreach (Match u in UrlLiteral.Matches(body))
                        hosts.Add((u.Groups["h"].Value, file, call));
            }
        return (hosts, calls, unresolved);
    }

    private static IEnumerable<(string File, string Text)> RepoFiles(params string[] extensions)
    {
        var sep = Path.DirectorySeparatorChar;
        foreach (var root in new[] { "libs", "apps" })
            foreach (var f in Directory.EnumerateFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, root), "*.*", SearchOption.AllDirectories))
                if (!f.Contains($"{sep}bin{sep}") && !f.Contains($"{sep}obj{sep}") && extensions.Any(e => f.EndsWith(e, StringComparison.Ordinal)))
                    yield return (Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, f), File.ReadAllText(f));
    }

    /// <summary>ما يَبنيه الكودُ في المُستَودَع: وُسومُ <c>.razor</c>/<c>.html</c> ودَوالُّ <c>.cs</c>.</summary>
    internal static (IReadOnlyList<(string Host, string File, string Call)> Hosts, int Calls, IReadOnlyList<string> Unresolved) CodeBuiltBrowserHosts()
        => CodeBuiltHosts(RepoFiles(".razor", ".html"), RepoFiles(".cs"));

    /// <summary>المُحَلِّلُ يَرى ما يَعُدّ (القاعِدَة ١٠) — عَيِّنَةٌ مَحقونَة: <c>src="@Helper.Url(...)"</c> يُحَلُّ إلى مُضيفِ سِلسِلَةِ الدالَّة، ووَسمٌ مُتَعَدِّدُ الأَسطُر
    /// كَـ<c>AcMapPin</c>؛ ورابِطُ التَصَفُّحِ (<c>&lt;a href&gt;</c>) لا يُعَدّ، ونِداءٌ بِلا دالَّةٍ يُقالُ «لا يُحَلّ»، ودالَّةٌ تَبني رابِطاً نِسبِيّاً بِلا مُضيف.</summary>
    [Theory]
    [InlineData("<img src=\"@Helper.Url(x)\" />", "cdn.example.org", 1, 0)]
    [InlineData("<iframe loading=\"lazy\"\n        src=\"@Ns.Helper.Url(1, 2)\"></iframe>", "cdn.example.org", 1, 0)]
    [InlineData("<a href=\"@Helper.Url(x)\">", "", 0, 0)]
    [InlineData("<script src=\"@Missing.Url()\"></script>", "", 1, 1)]
    [InlineData("<img src=\"@Helper.Local(x)\" />", "", 1, 0)]
    public void The_code_built_url_resolver_sees_what_it_counts(string markup, string host, int calls, int unresolved)
    {
        const string helper = "public static class Helper\n{\n    public static string Url(int x) => $\"https://cdn.example.org/{x}\";\n    public static string Local(int x) => $\"/uploads/{x}\";\n}\n";
        var found = CodeBuiltHosts(new[] { ("x.razor", markup) }, new[] { ("Helper.cs", helper) });
        Assert.Equal(host, found.Hosts.Select(h => h.Host).FirstOrDefault() ?? "");
        Assert.Equal((calls, unresolved), (found.Calls, found.Unresolved.Count));
    }

    /// <summary><b>تَوصيفٌ قَبلَ التَبديل (القاعِدَة ٣)</b>: المُتَصَفِّحُ يَبلُغُ <c>www.openstreetmap.org</c> بِرابِطٍ يَبنيه <c>OsmLinks.EmbedUrl</c>، والجَردُ
    /// (<see cref="BrowserHosts"/>) لا يَراه، ولا جِهَةَ لَه في <see cref="BrowserHostParties"/>، وسَطرُ <c>share_maps</c> العَرَبِيُّ لا يُسَمّي «OpenStreetMap».
    /// يَنقَلِبُ بِاسمِه في كوميتِ التَبديل.</summary>
    [Fact]
    public void Today_the_browser_reaches_openstreetmap_through_a_code_built_url_the_inventory_cannot_see()
    {
        var literal = BrowserHosts().Select(h => h.Host).Distinct(StringComparer.Ordinal).ToList();
        var built = CodeBuiltBrowserHosts();
        var builtHosts = built.Hosts.Select(h => h.Host).Distinct(StringComparer.Ordinal).ToList();
        var unnamed = UnnamedBrowserParties(literal.Concat(builtHosts).Distinct(StringComparer.Ordinal));
        var ar = LocaleCatalog.Find(LocaleCatalog.Arabic, "platform.privacy.share_maps")!;
        output.WriteLine($"مُضيفونَ حَرفِيّون: {literal.Count} · نِداءاتُ دالَّةٍ في وُسومِ الجَلب: {built.Calls} (لا يُحَلّ: {built.Unresolved.Count}) · مُضيفونَ يَبنيهِم الكود: " +
                         $"{builtHosts.Count} ({string.Join(", ", built.Hosts.Select(h => $"{h.Host} ← {h.Call} في {Path.GetFileName(h.File)}").Distinct())}) · " +
                         $"جِهاتٌ غَيرُ مُسَمّاة: {string.Join("، ", unnamed)} · share_maps بِالعَرَبِيَّةِ يُسَمّي OpenStreetMap: {ar.Contains("OpenStreetMap", StringComparison.Ordinal)}");
        Assert.Equal(4, literal.Count);
        Assert.DoesNotContain("www.openstreetmap.org", literal);
        Assert.Equal(new[] { "www.openstreetmap.org" }, builtHosts);
        Assert.Empty(built.Unresolved);
        Assert.False(BrowserHostParties.ContainsKey("www.openstreetmap.org"));
        Assert.Equal(new[] { "«www.openstreetmap.org» بِلا جِهَة" }, unnamed);
        Assert.DoesNotContain("OpenStreetMap", ar, StringComparison.Ordinal);
    }

    /// <summary>حُقولُ تَسجيلِ البائِعِ المُسَجَّلِ في الضَريبَةِ كَما يَكتُبُها <c>OtoClient.RegisterVendorAsync</c> تَحتَ <c>if (vendor.IsVatRegistered)</c>.</summary>
    internal static readonly string[] VatRegistrationFields =
    [
        "body[\"companyName\"] = vendor.CompanyName;", "body[\"vatNumber\"]   = vendor.VatNumber;", "body[\"CRNumber\"]    = vendor.CrNumber;",
        "body[\"district\"]    = vendor.District;", "body[\"zipCode\"]     = vendor.ZipCode;", "body[\"buildingNo\"]  = vendor.BuildingNo;",
        "body[\"streetName\"]  = vendor.StreetName;",
    ];

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b> (‏2026-10-25؛ كانَ <c>Today_share_carrier_does_not_name_the_vat_registration_fields_the_vendor_registration_sends</c>،
    /// مُودَعٌ في <c>61b220e</c>: «حُقولُ الضَريبَة 7/7 · ar يَذكُرُ «الضريبي»: False · en يَذكُرُ VAT: False»). والآن النَصّانِ يُسَمّيانِ الحُقولَ السَبعَةَ
    /// بِأَسمائِها لِلبائِعِ المُسَجَّلِ في الضَريبَة — والكودُ ما زالَ يُرسِلُها كُلَّها تَحتَ شَرطِها، فَالنَصُّ والجِسمُ يُقاسانِ في صَفٍّ واحِد.</summary>
    [Fact]
    public void Share_carrier_names_the_vat_registration_fields_the_vendor_registration_sends()
    {
        var oto = UpgradePromptPriceTests.Code("libs", "kits", "Delivery", "ACommerce.Kit.Delivery.Core", "OtoClient.cs");
        var vat = oto.IndexOf("if (vendor.IsVatRegistered)", StringComparison.Ordinal);
        var sent = VatRegistrationFields.Count(f => oto.IndexOf(f, StringComparison.Ordinal) > vat);
        var ar = AccountDeletionRetentionTests.Bare(LocaleCatalog.Find(LocaleCatalog.Arabic, "platform.privacy.share_carrier")!);
        var en = LocaleCatalog.Find("en", "platform.privacy.share_carrier")!;
        string[] arSaid = ["ضريبة القيمة المضافة", "اسم منشأته", "رقمه الضريبي", "سجله التجاري", "الحي", "الرمز البريدي", "رقم المبنى", "الشارع"];
        string[] enSaid = ["VAT", "business name", "VAT number", "commercial registration number", "district", "postal code", "building number", "street"];
        var arMissing = arSaid.Where(w => !ar.Contains(w, StringComparison.Ordinal)).ToList();
        var enMissing = enSaid.Where(w => !en.Contains(w, StringComparison.Ordinal)).ToList();
        output.WriteLine($"حُقولُ الضَريبَةِ في RegisterVendorAsync: {sent}/{VatRegistrationFields.Length} · ما لا يَقولُه ar: {arMissing.Count} · ما لا يَقولُه en: {enMissing.Count}");
        Assert.True(vat > 0);
        Assert.Equal(7, sent);
        Assert.Empty(arMissing);
        Assert.Empty(enMissing);
    }
}
