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
    };

    /// <summary>عَميلٌ صادِرٌ لا يُسَمّيه نَصُّ السِياسَةِ اليَوم — بِسَبَبِه، وصِياغَتُه قَرارُ المالِك («ب» W8-4 · ٥).</summary>
    internal static readonly IReadOnlyDictionary<string, string> PinnedUnlisted = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AnthropicBackend"] = "نَصُّ الفِكرَةِ ومُحادَثَةُ الوَكيلِ إلى مُزَوِّدِ نَموذَجِ اللُغَة",
        ["GeminiBackend"]    = "نَصُّ الفِكرَةِ ومُحادَثَةُ الوَكيلِ إلى مُزَوِّدِ نَموذَجِ اللُغَة",
        ["OpenAIBackend"]    = "نَصُّ الفِكرَةِ ومُحادَثَةُ الوَكيلِ إلى مُزَوِّدِ نَموذَجِ اللُغَة",
        ["WebPushService"]   = "عُنوانُ اشتِراكِ الجِهازِ وحُمولَةٌ مُشَفَّرَةٌ إلى خِدمَةِ إشعاراتِ المُتَصَفِّح",
        // ‏(2026-10-25، مِن `Listed`): بَوّابَةُ المَتجَر — فاتورَةٌ بِمَبلَغٍ ووَصفٍ ومُعَرِّفاتِ صَفَقاتٍ لا بَيانٍ شَخصِيّ، والمُشتَري يَكتُبُ بِطاقَتَه
        // عِندَها. و`share_payment` يُسَمّي «الاشتِراكَ» وَحدَه؛ يَعودُ إلى `Listed` يَومَ يُسَمّي نَصُّه دَفعَ الطَلَبات («ب» W8-4 · ٥ البَند ٤).
        ["MoyasarClient"]    = "فاتورَةُ دَفعِ الطَلَبِ (مَبلَغٌ ووَصفٌ ومُعَرِّفاتُ صَفَقات) إلى بَوّابَةِ دَفعِ المَتجَر",
    };

    private static readonly Regex HoldsClient = new(@"(?<!\w)(HttpClient|WebPushClient)\s+[_A-Za-z]\w*\s*[;=,)]", RegexOptions.Compiled);
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
            Assert.Contains($"@doc.Markup(\"{key}\")", page, StringComparison.Ordinal);
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
    [InlineData("services.AddHttpClient<MoyasarClient>(c => c.Timeout = t);", false)]
    [InlineData("_http.SendAsync(req)", false)]
    public void The_client_pattern_sees_what_it_counts(string code, bool holds)
        => Assert.Equal(holds, HoldsClient.IsMatch(code));

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ المِفتاح)</b> — كانَ <c>Today_the_privacy_share_list_renders_six_keys_and_none_names_the_carrier</c>
    /// (‏<c>5ad4e05</c>). <b>والآن سَبعَة</b>: <c>share_carrier</c> بَعدَ مُزَوِّدِ الدَفع، والسِتُّ بِتَرتيبِها؛ ونَصُّه يُسَمّي ما يُرسِلُه الكودُ حَرفاً —
    /// حُقولَ <c>OtoCustomer</c> الأَربَعَ الشَخصِيَّةَ وحُقولَ تَسجيلِ البائِع — وOTO خارِجَ القائِمَةِ المُثَبَّتَة (أَربَعَةٌ لا خَمسَة).</summary>
    [Fact]
    public void The_privacy_share_list_renders_seven_keys_and_names_the_carrier_with_what_the_code_sends_it()
    {
        var page = PrivacyPage();
        var keys = Regex.Matches(page, @"@doc\.Markup\(""(?<k>platform\.privacy\.share_\w+)""\)").Select(m => m.Groups["k"].Value).ToList();
        output.WriteLine($"مَفاتيحُ share_* في /privacy: {keys.Count} ({string.Join(", ", keys)}) · مُثَبَّتونَ غَيرَ مُسَمَّين: {PinnedUnlisted.Count}");
        Assert.Equal(new[]
        {
            "platform.privacy.share_otp", "platform.privacy.share_nafath", "platform.privacy.share_payment", "platform.privacy.share_carrier",
            "platform.privacy.share_fonts", "platform.privacy.share_maps", "platform.privacy.share_legal",
        }, keys);
        Assert.DoesNotContain("OtoClient", PinnedUnlisted.Keys);
        // ‏(2026-10-25) خَمسَةٌ لا أَربَعَة: `MoyasarClient` نُقِلَ إلى المُثَبَّتَة — `share_payment` لا يُسَمّي دَفعَ الطَلَبات.
        Assert.Equal(5, PinnedUnlisted.Count);

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
}
