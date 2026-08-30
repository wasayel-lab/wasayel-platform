using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ بابُ قَناةِ المُستَأجِر — البابُ الَّذي بَناهُ العَميلُ ولَم
//     يَستَطِع أَحَدٌ دُخولَه ══════════════════════════════════════════
//
// **المَقيسُ قَبل (‏2026-08-30)**: المَسارُ الذاتِيُّ كامِلٌ ويَعمَل —
// يُسَجِّلُ العَميلُ، ويُجيبُ أَسئِلَةَ الاكتِشاف، ويَنقُرُ «ابنِ»،
// فَيَصيرُ مَتجَرُه حَيّاً على `/{slug}` في ثَوانٍ بِلا نَشرٍ ولا
// نِطاقٍ ولا لَمسَةٍ مِن المالِك. **وبابُه مُغلَق**:
//
//   ‏١. `TenantFromAnalysisFactory.CreateAsync` يَكتُب
//      `AuthChannel = "phone"` **ثابِتَةً مَكتوبَة**.
//   ‏٢. واستِمارَةُ هُوِيَّةِ الاستوديو أَربَعَةُ حُقولٍ بِلا حَقلِ
//      قَناةٍ إطلاقاً — بَينَما نَظيرَتُها الإدارِيَّةُ فيها الأَربَعَةُ
//      **وثَلاثَةُ أَزرارِ راديو**.
//   ‏٣. و`docs/DEPLOY.md` §٢·ب يُوصي بِقَناةِ البَريد (`brevo`) لِأَنّ
//      المُستَضيفَ **يَحجُبُ مَنافِذَ SMTP** — قياسٌ حَيّ: لا استِجابَةَ
//      بَعدَ ‏90 ثانِيَة.
//
// ⇒ فَعَلى نُسخَةٍ مَضبوطَةٍ بِالبَريدِ وَحدَه — وهي تَوصِيَةُ
// وَثيقَتِها — **كُلُّ مَتجَرٍ يَبنيه عَميلٌ بِنَفسِه يُولَدُ على
// قَناةٍ غَيرِ مُسَجَّلَة**، فَتَرُدُّ `Login.razor` لافِتَةً حَمراءَ
// بَدَلَ النَموذَج، ولا يَفتَحُه إلّا المالِكُ بِيَدِه مِن `/admin`.
// وهي الخُطوَةُ اليَدَوِيَّةُ **الحاجِبَةُ** الوَحيدَةُ بَينَ بِناءِ
// العَميلِ لِمَتجَرِه وأَوَّلِ طَلَبٍ فيه.
//
// ─── ولِماذا الافتِراضيُّ يُشتَقُّ ولا يُكتَب ────────────────────────
//
// ‏`AuthChannels.Default` ثابِتٌ عالَمِيٌّ لَه مُستَهلِكونَ لا عَلاقَةَ
// لَهُم بِالاستوديو (مُستَوردات، بَذّارات، أَداةُ الوَكيل)، ومُثَبَّتٌ
// في `AuthEmailChannelTests`. فَالاشتِقاقُ **دالَّةٌ جَديدَةٌ فَوقَ
// فَحصِ الوِعاء** لا تَبديلُ ثابِت — عَلى غِرارِ `StudioAuthDoor`
// حَرفاً: جَدوَلٌ نَقِيٌّ بِلا I/O، وحافَّةٌ واحِدَةٌ تَقرَأُ أَثَرَ
// `AuthChannelSelection.Decide` مِن الوِعاء.
//
// ─── حارِسُ العَمى (القاعِدَة ١٠) ────────────────────────────────────
// كُلُّ فاحِصٍ نَصِّيٍّ هُنا يَطبَعُ عَدَدَ ما فَحَص ويَحمَرُّ عِندَ
// الصِفر — «صِفرُ مُخالَفَة» بِلا عَدّادٍ لا يُمَيَّزُ عَن أَداةٍ عَمياء.
public class TenantAuthChannelDoorTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private const string TemplateRoot =
        "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(
            RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>البابُ كَما تَراهُ نُسخَةٌ مَضبوطَةٌ بِهذِه التَهيئَة —
    /// يُرَكِّبُ <see cref="AuthChannelSelection.Decide"/> مَعَ الجَدوَل
    /// بَدَلَ أَن يَنسَخَ نَتيجَةَ الأُولى نَصّاً. نَفسُ قالَبِ
    /// <c>StudioAuthDoorTests.DoorIn</c> (القاعِدَة ٨).</summary>
    private static IReadOnlyList<string> DoorIn(
        bool isDevelopment, string? sms, string? nafath, string? email)
        => TenantAuthChannelDoor.Offered(
            phone:  AuthChannelSelection.Decide(AuthChannelKind.Sms,    sms,    isDevelopment)
                        != AuthChannelProvider.None,
            nafath: AuthChannelSelection.Decide(AuthChannelKind.Nafath, nafath, isDevelopment)
                        != AuthChannelProvider.None,
            email:  AuthChannelSelection.Decide(AuthChannelKind.Email,  email,  isDevelopment)
                        != AuthChannelProvider.None);

    // ═════════════════════════════════════════════════════════════════
    //  ١) الافتِراضيُّ مُشتَقٌّ لا مَكتوب — **وهذا هُوَ العَطَبُ بِعَينِه**
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>حالَةُ المالِكِ المَنصوصَة</b>: نُسخَةٌ إنتاجِيَّةٌ بِـ<c>brevo</c>
    /// وَحدَها (‏`docs/DEPLOY.md` §٢·ب). القَناةُ الافتِراضِيَّةُ
    /// لِمَتجَرٍ يُبنى الآنَ يَجِبُ أَن تَكونَ <b>البَريد</b> — ولَو
    /// عادَت <c>"phone"</c> فَذلكَ العَطَبُ نَفسُه عائِداً.
    /// </summary>
    [Fact]
    public void OnAnEmailOnlyDeployment_TheDefaultIsEmail_NotTheWrittenConstant()
    {
        var door = DoorIn(isDevelopment: false, sms: null, nafath: null, email: "brevo");

        Assert.Equal(new[] { AuthChannels.Email }, door);
        Assert.Equal(AuthChannels.Email, TenantAuthChannelDoor.Default(door));
        Assert.NotEqual(AuthChannels.Default, TenantAuthChannelDoor.Default(door));
    }

    /// <summary>ونَفسُ الشَيءِ بِـ<c>smtp</c> — القيمَتانِ نَقلانِ لا
    /// مُزَوِّدانِ، والجَدوَلُ يَقرَؤُهُما مِن
    /// <see cref="AuthChannelSelection.RealProviders"/> لا مِن نَسخَة.</summary>
    [Fact]
    public void OnAnSmtpOnlyDeployment_TheDefaultIsEmailToo()
        => Assert.Equal(AuthChannels.Email,
            TenantAuthChannelDoor.Default(
                DoorIn(isDevelopment: false, sms: null, nafath: null, email: "smtp")));

    /// <summary>‏Twilio وَحدَها ⇒ الهاتِف. وهُوَ نَفسُ الجَوابِ القَديم
    /// — <b>لكِن لِسَبَبٍ لا لِمُصادَفَة</b>.</summary>
    [Fact]
    public void OnAnSmsOnlyDeployment_TheDefaultIsPhone_BecauseItIsConfigured()
        => Assert.Equal(AuthChannels.Phone,
            TenantAuthChannelDoor.Default(
                DoorIn(isDevelopment: false, sms: "twilio", nafath: null, email: null)));

    /// <summary><b>القاعِدَةُ الَّتي لا تُخرَق مَهما كانَت التَهيئَة</b>:
    /// الافتِراضيُّ إمّا <c>null</c> وإمّا عُضوٌ في المَعروض. تُفحَصُ
    /// **الثَمانِ** تَركيباتٍ لا واحِدَة.</summary>
    [Fact]
    public void TheDefaultIsNeverAChannelThatIsNotOffered()
    {
        var checkedCombinations = 0;
        foreach (var phone in new[] { false, true })
        foreach (var nafath in new[] { false, true })
        foreach (var email in new[] { false, true })
        {
            var offered = TenantAuthChannelDoor.Offered(phone, nafath, email);
            var def = TenantAuthChannelDoor.Default(offered);

            if (offered.Count == 0) Assert.Null(def);
            else Assert.Contains(def, offered);

            checkedCombinations++;
        }

        output.WriteLine($"· تَركيباتٌ مَفحوصَة: {checkedCombinations}");
        Assert.Equal(8, checkedCombinations);
    }

    /// <summary>والمَعروضُ لا يَختَرِعُ قيمَةً خارِجَ
    /// <see cref="AuthChannels.All"/> — فَقيمَةٌ خارِجَها تَبتَلِعُها
    /// <c>NormalizeOrDefault</c> صامِتَةً وتَرتَدُّ إلى «هاتِف».</summary>
    [Fact]
    public void EverythingOffered_IsASupportedChannelValue()
    {
        var seen = 0;
        foreach (var phone in new[] { false, true })
        foreach (var nafath in new[] { false, true })
        foreach (var email in new[] { false, true })
            foreach (var c in TenantAuthChannelDoor.Offered(phone, nafath, email))
            {
                Assert.True(AuthChannels.IsSupported(c), $"قيمَةٌ خارِجَ المَعجَم: {c}");
                Assert.Equal(c, AuthChannels.NormalizeOrDefault(c));
                seen++;
            }

        output.WriteLine($"· قِيَمٌ مَفحوصَة: {seen}");
        Assert.True(seen > 0, "أَداة عَمياء: لَم تُفحَص قيمَةٌ واحِدَة.");
    }

    /// <summary>التَرتيبُ مُثَبَّتٌ ولا يُترَكُ لِصُدفَةِ تَعداد — فَهُوَ
    /// الَّذي يُقَرِّرُ الافتِراضيَّ حينَ تُهَيَّأُ قَناتان.</summary>
    [Fact]
    public void TheOfferedOrderIsPinned_BecauseItDecidesTheDefault()
    {
        Assert.Equal(
            new[] { AuthChannels.Phone, AuthChannels.Nafath, AuthChannels.Email },
            TenantAuthChannelDoor.Offered(phone: true, nafath: true, email: true));

        // ونَفسُ تَرتيبِ المَعجَمِ المُثَبَّتِ في AuthEmailChannelTests.
        Assert.Equal(AuthChannels.All,
            TenantAuthChannelDoor.Offered(true, true, true));
    }

    /// <summary>‏Development كَما كانَ: الثَلاثَةُ مَعروضَة، والافتِراضيُّ
    /// الهاتِف — فَلا تَنكَسِرُ الطَبَقَةُ الحَيَّة.</summary>
    [Fact]
    public void InDevelopment_AllThreeAreOffered_AndTheDefaultIsPhone_AsBefore()
    {
        var door = DoorIn(isDevelopment: true, null, null, null);
        Assert.Equal(AuthChannels.All, door);
        Assert.Equal(AuthChannels.Phone, TenantAuthChannelDoor.Default(door));
    }

    /// <summary>والقَناةُ القائِمَةُ لِلمُستَأجِرِ تَغلِبُ الافتِراضيَّ ما
    /// دامَت مَعروضَة — فَحِفظُ الاسمِ لا يَنقُلُ مَتجَراً عَن نَفاذ.</summary>
    [Fact]
    public void AnAlreadyChosenChannel_WinsWhileItIsStillOffered()
    {
        var door = DoorIn(isDevelopment: true, null, null, null);
        Assert.Equal(AuthChannels.Nafath, TenantAuthChannelDoor.Choose(AuthChannels.Nafath, door));
        Assert.Equal(AuthChannels.Email,  TenantAuthChannelDoor.Choose(AuthChannels.Email, door));
    }

    /// <summary>وقَناةٌ لَم تَعُد مُهَيَّأَةً لا تُعادُ كِتابَتُها —
    /// يُرتَدُّ إلى المَعروضِ الأَوَّل. «لا زِرَّ يَقودُ إلى رَفضٍ حَيثُ
    /// يُمكِنُ إخفاؤُه» (القاعِدَة ١٢).</summary>
    [Fact]
    public void AChannelThatIsNoLongerConfigured_FallsBackToAnOfferedOne()
    {
        var door = DoorIn(isDevelopment: false, sms: null, nafath: null, email: "brevo");
        Assert.Equal(AuthChannels.Email, TenantAuthChannelDoor.Choose(AuthChannels.Phone, door));
        Assert.Equal(AuthChannels.Email, TenantAuthChannelDoor.Choose("mystery", door));
        Assert.Equal(AuthChannels.Email, TenantAuthChannelDoor.Choose(null, door));
    }

    // ═════════════════════════════════════════════════════════════════
    //  ٢) لا مَتجَرَ بِبابٍ مُغلَق — رِسالَةٌ صَريحَةٌ لا صَمت
    // ═════════════════════════════════════════════════════════════════

    /// <summary>إنتاجٌ بِلا تَهيئَة: <b>لا بابَ إطلاقاً</b>، فَلا
    /// افتِراضيَّ يُكتَب. وهذا هُوَ السَطرُ الَّذي كانَ كاذِباً:
    /// <c>"phone"</c> كانَت تُكتَبُ هُنا بِثِقَة.</summary>
    [Fact]
    public void ProductionWithoutAnyConfiguredChannel_HasNoDefault_AtAll()
    {
        var door = DoorIn(isDevelopment: false, null, null, null);
        Assert.Empty(door);
        Assert.Null(TenantAuthChannelDoor.Default(door));
        Assert.Null(TenantAuthChannelDoor.Choose(AuthChannels.Phone, door));
    }

    /// <summary>و<c>mock</c> مَكتوبَةً صَراحَةً خارِجَ التَطوير لا
    /// تَفتَحُ باباً — نَفسُ قاعِدَةِ أَبوابِ المُستَأجِرين.</summary>
    [Fact]
    public void ProductionWithExplicitMock_HasNoDefault()
        => Assert.Null(TenantAuthChannelDoor.Default(DoorIn(
            isDevelopment: false,
            AuthChannelSelection.MockValue,
            AuthChannelSelection.MockValue,
            AuthChannelSelection.MockValue)));

    /// <summary><b>ولِلرَفضِ رَمزٌ يُقال</b> — مِن مَعجَمٍ مُغلَقٍ على
    /// غِرارِ <see cref="TenantFromAnalysisFactory.SlugRequired"/>
    /// وأَخَواتِها، لا رِسالَةً عَرَبِيَّةً مَكتوبَةً في نُقطَة.</summary>
    [Fact]
    public void TheRefusalCode_IsAClosedLexiconCode()
    {
        Assert.Equal("no_auth_channel", TenantAuthChannelDoor.NoChannel);
        Assert.Matches("^[a-z_]+$", TenantAuthChannelDoor.NoChannel);
    }

    /// <summary>ولِلرَمزِ رِسالَةٌ في القامُوسِ يَراها العَميل — رَمزٌ
    /// بِلا مِفتاحٍ يُعرَضُ خاماً بِالإنجليزيَّةِ على شاشَةٍ عَرَبِيَّة.</summary>
    [Fact]
    public void TheRefusalCode_HasAnArabicMessage_TheClientCanRead()
    {
        var msg = LocaleCatalog.Find("ar", "studio.study.err_no_auth_channel");
        Assert.False(string.IsNullOrWhiteSpace(msg),
            "رَمزُ الرَفضِ بِلا رِسالَة — العَميلُ يَرى `no_auth_channel`.");

        // والشاشَةُ تُتَرجِمُه فِعلاً، لا القامُوسُ وَحدَه يَحمِلُه.
        var study = Read($"{TemplateRoot}/Components/Pages/StudioStudy.razor");
        Assert.Contains("studio.study.err_no_auth_channel", study, StringComparison.Ordinal);
    }

    /// <summary>وصَفحَةُ الهُوِيَّةِ تَقولُ العِلَّةَ حينَ لا قَناةَ
    /// مُهَيَّأَة — لا تَعرِضُ أَزراراً تُؤَدّي إلى بابٍ مُغلَق.</summary>
    [Fact]
    public void TheBrandingPage_SpeaksWhenNoChannelIsConfigured()
    {
        var msg = LocaleCatalog.Find("ar", "studio.app_branding.no_channel");
        Assert.False(string.IsNullOrWhiteSpace(msg));

        var page = Read($"{TemplateRoot}/Components/Pages/StudioAppBranding.razor");
        Assert.Contains("studio.app_branding.no_channel", page, StringComparison.Ordinal);
    }

    // ═════════════════════════════════════════════════════════════════
    //  ٣) الحافَّةُ الوَحيدَةُ الَّتي تَقرَأُ الوِعاء
    // ═════════════════════════════════════════════════════════════════

    private sealed class FakeSms : IOtpChannel
    {
        public string ChannelName => "fake-sms";
        public string? DevHintCode => null;
        public Task SendOtpAsync(string phone, string code, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeEmail : IEmailOtpChannel
    {
        public string ChannelName => "fake-email";
        public string? DevHintCode => null;
        public Task SendOtpAsync(string email, string code, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeNafath : INafathChannel
    {
        public string ChannelName => "fake-nafath";
        public Task<NafathStartResult> StartAsync(string nationalId, CancellationToken ct)
            => Task.FromResult(new NafathStartResult("a", "00", 0));
        public Task<bool> IsApprovedAsync(string attemptId, CancellationToken ct)
            => Task.FromResult(false);
    }

    /// <summary>
    /// <b>الوَصلَةُ الاسمِيَّةُ تُكتَبُ مَرَّةً واحِدَة</b>:
    /// <c>AuthChannelKind.Sms</c> ↔ قيمَةُ المُستَأجِرِ <c>"phone"</c> —
    /// اسمانِ لِشَيءٍ واحِد، ونَسخُهُما في مَوضِعَين هُوَ عَينُ ما وُضِعَ
    /// الجَدوَلُ لِيَمنَعَه. فَيُقاسُ هُنا بِوِعاءٍ حَقيقيّ: قَناةُ
    /// بَريدٍ وَحدَها مُسَجَّلَة ⇒ البَريدُ وَحدَه مَعروض.
    /// </summary>
    [Fact]
    public void OfferedIn_ReadsTheContainer_NotTheConfiguration()
    {
        var emailOnly = new ServiceCollection()
            .AddSingleton<IEmailOtpChannel>(new FakeEmail())
            .BuildServiceProvider();
        Assert.Equal(new[] { AuthChannels.Email }, TenantAuthChannelDoor.OfferedIn(emailOnly));

        var smsOnly = new ServiceCollection()
            .AddSingleton<IOtpChannel>(new FakeSms())
            .BuildServiceProvider();
        Assert.Equal(new[] { AuthChannels.Phone }, TenantAuthChannelDoor.OfferedIn(smsOnly));

        var nafathOnly = new ServiceCollection()
            .AddSingleton<INafathChannel>(new FakeNafath())
            .BuildServiceProvider();
        Assert.Equal(new[] { AuthChannels.Nafath }, TenantAuthChannelDoor.OfferedIn(nafathOnly));

        var none = new ServiceCollection().BuildServiceProvider();
        Assert.Empty(TenantAuthChannelDoor.OfferedIn(none));
        Assert.Null(TenantAuthChannelDoor.Default(TenantAuthChannelDoor.OfferedIn(none)));
    }

    // ═════════════════════════════════════════════════════════════════
    //  ٤) المَصنَعُ لا يَكتُبُ قَناةً مِن عِندِه
    // ═════════════════════════════════════════════════════════════════

    /// <summary><b>القَناةُ مُعامَلٌ لا ثابِت.</b> ولَو عادَت مَكتوبَةً
    /// في جِسمِ المَصنَعِ يَومَاً فَهذا الاختِبارُ هُوَ الَّذي
    /// يَقولُها — وهُوَ سَبَبُ وُجودِه.</summary>
    [Fact]
    public void TheFactory_TakesTheChannel_AsAParameter()
    {
        var create = typeof(TenantFromAnalysisFactory)
            .GetMethod(nameof(TenantFromAnalysisFactory.CreateAsync));
        Assert.NotNull(create);

        var p = create!.GetParameters()
            .FirstOrDefault(x => x.Name == "authChannel");
        Assert.True(p is not null,
            "‏CreateAsync بِلا مُعامِلِ قَناة — أَي أَنَّها تَكتُبُ واحِدَةً مِن عِندِها.");
        Assert.Equal(typeof(string), p!.ParameterType);
    }

    /// <summary><b>وصِفرُ قَناةٍ مَكتوبَةٍ في مَصدَرِ المَصنَع.</b>
    /// الفَحصُ عَلى الغِيابِ نَفسِه — نَفسُ شَكلِ
    /// <c>StudioAuthDoorTests.StudioAuth_HasNoConstantLoginCode</c>.</summary>
    [Fact]
    public void TheFactorySource_CarriesNoWrittenAuthChannel()
    {
        var src = Read($"{TemplateRoot}/Services/Incubator/TenantFromAnalysisFactory.cs");
        var code = StripComments(src);

        var written = Regex.Matches(code, @"AuthChannel\s*=\s*""(?<v>[^""]*)""")
            .Select(m => m.Groups["v"].Value)
            .ToArray();

        output.WriteLine($"· أَسطُرُ مَصدَرٍ مَفحوصَة: {code.Split('\n').Length}");
        Assert.True(code.Contains("AuthChannel", StringComparison.Ordinal),
            "أَداة عَمياء: المَصدَرُ لا يَذكُرُ AuthChannel إطلاقاً — أَتَغَيَّرَ المَسار؟");
        Assert.True(written.Length == 0,
            "قَناةٌ مَكتوبَةٌ في المَصنَع: " + string.Join("، ", written));
    }

    // ═════════════════════════════════════════════════════════════════
    //  ٥) نُقطَةُ البِناء — تَشتَقُّ، وتَرفُضُ بِصَوتٍ عِندَ الفَراغ
    // ═════════════════════════════════════════════════════════════════

    private static string BuildEndpointBody()
    {
        var body = WriteEndpointGuardTests.AllMinimalApiEndpoints()
            .FirstOrDefault(e => e.Route == "/studio/s/{id:guid}/build")?.Body;
        Assert.False(string.IsNullOrEmpty(body),
            "أَداة عَمياء: نُقطَةُ البِناءِ لَم يَجِدها الماسِح.");
        return body!;
    }

    /// <summary>جِسمُ النُقطَةِ يَشتَقُّ القَناةَ مِن البابِ — ولا
    /// يَحمِلُ قيمَةَ قَناةٍ مَكتوبَة.</summary>
    [Fact]
    public void TheBuildEndpoint_DerivesTheChannel_FromWhatIsConfigured()
    {
        var body = BuildEndpointBody();
        output.WriteLine($"· أَسطُرُ الجِسم: {body.Split('\n').Length}");

        Assert.Contains(nameof(TenantAuthChannelDoor), body, StringComparison.Ordinal);

        foreach (var literal in new[] { AuthChannels.Phone, AuthChannels.Nafath, AuthChannels.Email })
            Assert.DoesNotContain($"\"{literal}\"", body, StringComparison.Ordinal);
    }

    /// <summary><b>ولا مَتجَرَ يُبنى بِلا قَناة</b>: الفَراغُ يَرُدُّ
    /// رَمزَ الرَفضِ قَبلَ أَن يُنادى المَصنَع — رِسالَةٌ صَريحَةٌ
    /// لِلعَميلِ لا بابٌ مُغلَقٌ يَكتَشِفُه بَعدَ أَوَّلِ زائِر.</summary>
    [Fact]
    public void TheBuildEndpoint_RefusesOutLoud_WhenNoChannelIsConfigured()
    {
        var body = BuildEndpointBody();

        Assert.Contains($"{nameof(TenantAuthChannelDoor)}.{nameof(TenantAuthChannelDoor.NoChannel)}",
            body, StringComparison.Ordinal);

        // والرَفضُ قَبلَ الإنشاء، لا بَعدَه.
        var refusal = body.IndexOf(nameof(TenantAuthChannelDoor.NoChannel), StringComparison.Ordinal);
        var create  = body.IndexOf("CreateAsync", StringComparison.Ordinal);
        Assert.True(refusal >= 0 && create > refusal,
            "الرَفضُ لا يَسبِقُ الإنشاء — فَالمَتجَرُ يُولَدُ ثُمَّ يُقالُ إنَّه لا يَصلُح.");
    }

    // ═════════════════════════════════════════════════════════════════
    //  ٦) الحَقلُ يَصِل — مِن الاستِمارَةِ إلى وَثيقَةِ المُستَأجِر
    // ═════════════════════════════════════════════════════════════════

    /// <summary><b>الاستِمارَةُ تُرسِلُ <c>channel</c>.</b> وكانَت
    /// أَربَعَةَ حُقولٍ لا خامِس، بَينَما النُقطَةُ والخِدمَةُ
    /// تَقبَلانِ القيمَةَ مُنذُ التَوحيد — فَالناقِصُ كانَ المُدخَلَ
    /// وَحدَه.</summary>
    [Fact]
    public void TheStudioBrandingForm_PostsAChannelField()
    {
        var page = Read($"{TemplateRoot}/Components/Pages/StudioAppBranding.razor");
        Assert.Contains("name=\"channel\"", page, StringComparison.Ordinal);
        Assert.Contains("type=\"radio\"", page, StringComparison.Ordinal);
    }

    /// <summary><b>ولا تَعرِضُ إلّا المُهَيَّأ.</b> زِرٌّ لِقَناةٍ غَيرِ
    /// مُهَيَّأَةٍ يُعيدُ إنتاجَ العَطَبِ مِن داخِلِ الإصلاح:
    /// <c>NormalizeOrDefault</c> تَقبَلُ القيمَةَ وتَكتُبُها، فَيُغلَقُ
    /// البابُ بِنَقرَةٍ مِن صاحِبِه.</summary>
    [Fact]
    public void TheStudioBrandingForm_OffersOnlyConfiguredChannels()
    {
        var page = Read($"{TemplateRoot}/Components/Pages/StudioAppBranding.razor");

        Assert.Contains(nameof(TenantAuthChannelDoor), page, StringComparison.Ordinal);

        // لا قيمَةَ قَناةٍ مَكتوبَةً في زِرّ — القيمُ مِن المَعروضِ وَحدَه.
        foreach (var literal in new[] { AuthChannels.Phone, AuthChannels.Nafath, AuthChannels.Email })
            Assert.DoesNotContain($"value=\"{literal}\"", page, StringComparison.Ordinal);
    }

    /// <summary><b>والقيمَةُ تَعبُرُ المُهايِئ</b> — تُقاسُ بِنَموذَجٍ
    /// حَقيقيّ لا بِنَظَر: نَموذَجٌ يَحمِلُ <c>channel</c> يُعطي
    /// القيمَة، ونَموذَجٌ بِلا المِفتاحِ يُعطي <c>null</c> = «لا
    /// تُغَيِّر» (وذاكَ العَقدُ الَّذي يَحمي مُستَأجِراً على نَفاذ).</summary>
    [Theory]
    [InlineData("email")]
    [InlineData("nafath")]
    [InlineData("phone")]
    public void TheChosenChannel_CrossesTheSurface_IntoTheSaveRequest(string chosen)
    {
        var withChannel = TenantConfigSurface.ReadBranding(
            FormRequest(("name", "مَتجَر"), ("color", "#2563eb"), ("channel", chosen)));
        Assert.Equal(chosen, withChannel.AuthChannel);

        var without = TenantConfigSurface.ReadBranding(
            FormRequest(("name", "مَتجَر"), ("color", "#2563eb")));
        Assert.Null(without.AuthChannel);
    }

    /// <summary><b>وتَصِلُ الوَثيقَة</b>: الخِدمَةُ المُوَحَّدَةُ تُسنِدُ
    /// القيمَةَ المُرسَلَةَ إلى <c>Tenant.AuthChannel</c>. يُقاسُ
    /// المَصدَرُ لِأَنّ الإسنادَ يَحتاجُ جَلسَةَ Marten — والحَلقَتانِ
    /// قَبلَه (الاستِمارَة والمُهايِئ) مَقيسَتانِ فِعلاً أَعلاه.</summary>
    [Fact]
    public void TheSaveService_StillWritesTheSubmittedChannel_OntoTheDocument()
    {
        var src = StripComments(Read($"{TemplateRoot}/Services/TenantConfig/BrandingSaveService.cs"));
        Assert.Matches(@"t\.AuthChannel\s*=\s*AuthChannels\.NormalizeOrDefault\(\s*r\.AuthChannel", src);
        Assert.Contains("r.AuthChannel is not null", src, StringComparison.Ordinal);
    }

    // ─── أَدَوات ────────────────────────────────────────────────────

    private static Microsoft.AspNetCore.Http.HttpRequest FormRequest(
        params (string Key, string Value)[] fields)
    {
        var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.Request.ContentType = "application/x-www-form-urlencoded";
        var dict = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(
            StringComparer.Ordinal);
        foreach (var (k, v) in fields) dict[k] = new Microsoft.Extensions.Primitives.StringValues(v);
        ctx.Request.Form = new Microsoft.AspNetCore.Http.FormCollection(dict);
        return ctx.Request;
    }

    /// <summary>يُبَيِّضُ التَعليقاتِ كَي لا يُتَّهَمَ شَرحٌ بِأَنَّه
    /// كود — نَفسُ مَبدَإ <c>WriteEndpointGuardTests.StripComments</c>.</summary>
    private static string StripComments(string text)
    {
        text = Regex.Replace(text, @"/\*.*?\*/", m => new string(' ', m.Length),
            RegexOptions.Singleline);
        text = Regex.Replace(text, @"//[^\n]*", m => new string(' ', m.Length));
        return text;
    }
}
