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
