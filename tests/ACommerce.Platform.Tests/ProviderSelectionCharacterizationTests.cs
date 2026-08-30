using ACommerce.Kit.Auth;
using ACommerce.Kit.Files;
using ACommerce.Kit.Payments;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ اختِيارِ المُزَوِّد — «أَيُّ تَنفيذٍ يُسَجَّل لِأَيّ تَهيئَة» ═══
//
// **يُكتَب ويَخضَرّ ويُودَع في كوميتٍ مُستَقِلّ قَبلَ تَبديلِ حَرف، ثُمَّ
// لا يُمَسّ** (القاعِدَة ٣). فَمُرورُه بَعدَ مَوجَةِ رَبطِ المُزَوِّدين
// هُوَ بُرهانُ أَنّ **كُلَّ مُستَأجِرٍ قائِمٍ اليَوم لا يَمُرّ بِسَطرِ
// قَرارٍ إضافيّ**، وأَنّ سُلوكَ `Program.cs` كَما هُوَ حَرفاً.
//
// ─── ما يُثَبَّت، ولِماذا هذا الشَكل ─────────────────────────────────
//
// ‏`AuthChannelSelection.Decide` مَقيسٌ بِجَدوَلٍ سَلَفاً لِثَلاثِ قُدُرات؛
// وهذا المِلَفّ **يَمُدّ الشَكلَ نَفسَه إلى التِسع كُلِّها** — بِما فيها
// السِتّ الَّتي لا دالَّةَ قَرارٍ لَها اليَوم. ولِلسِتِّ صورَتانِ لا
// ثالِثَ لَهُما، وكِلتاهُما تُقاسانِ هُنا:
//
//   • **بِلا شَرطٍ إطلاقاً** — سَطرُ تَسجيلٍ عارٍ في `Program.cs`
//     (‏`AddMockMaps` و`AddMockDelivery` و`AddLocalFileStorage`).
//     فَالمُحاكي هُوَ الجَوابُ في الإنتاجِ كَما في التَطوير.
//
// ─── تَعديلٌ مُعلَنٌ — ‏2026-08-30، صَفُّ `payments` وَحدَه ──────────
//
// **هذا المِلَفُّ يَصِف «جَوابَ اليَوم»، وجَوابُ اليَومِ لِلدَفعِ تَغَيَّر
// عَمداً** — فَيُعَدَّل الصَفُّ ويُقالُ لِماذا، ولا يُترَك يَصِف ماضِياً.
// كانَ `AddMockPayments()` سَطراً عارِياً والمُحاكي يَقول «نَجَحَ الدَفع»
// دائِماً، فَكانَت `‏/studio/billing/select` تَقرَأُ نَجاحاً وتَكتُب
// `Tier = "scale"` (‏999 ريالاً) بِلا قَبض. صارَ القَرارُ دالَّةً
// نَقِيَّةً (`PaymentProviderSelection.Decide`) وحارِسَ إقلاعٍ يَرمي —
// نَفسُ آلِيَّةِ قَنَواتِ الدُخولِ حَرفاً. التَفصيلُ في
// `docs/ADR-014-THE-PAYMENT-STUB-STOPS-AT-DEVELOPMENT.md`.
//
// **والعارِيَةُ صارَت ثَلاثاً لا أَربَعاً**، والمَشروطَةُ أَربَعاً لا
// ثَلاثاً. والرَقمانِ مُثَبَّتانِ أَدناه فَلا يَنزِلانِ صامِتَين.
//   • **بِلا تَسجيلٍ أَصلاً** — ‏`INotificationChannel` و`ICache`:
//     واجِهَتانِ قائِمَتانِ لا يَبلُغُهُما `Program.cs` ولا يُحيلُ
//     مَشروعَ تَنفيذِهِما أَيُّ `csproj` في التَطبيق. وذلك **مَقيسٌ
//     لا مَظنون**، وهُوَ بِعَينِه ما تَصِفُه القاعِدَةُ ١ في
//     `CLAUDE.md` («‏527 سَطر تَجريد تَراسُل لا يَبلُغُها
//     `Program.cs`»).
//
// ─── حارِسُ العَمى (القاعِدَة ١٠) ────────────────────────────────────
// كُلُّ فَحصٍ يَطبَع عَدَدَ ما فَحَص ويَحمَرُّ عِندَ الصِفر: «صِفر
// مُخالَفَة» مِن أَداةٍ فَحَصَت صِفراً لا يُمَيَّز عَن أَداةٍ فَحَصَت
// كُلَّ شَيء.
public class ProviderSelectionCharacterizationTests
{
    /// <summary>صَفٌّ واحِدٌ = قُدرَةٌ واحِدَة، بِجَوابِها اليَوم.</summary>
    /// <param name="Capability">اسمُ القُدرَة — مَعجَمٌ مُغلَق.</param>
    /// <param name="Interface">الواجِهَةُ القائِمَة في العُدَّة.</param>
    /// <param name="RegistrationToday">نِداءُ التَسجيلِ في
    /// <c>Program.cs</c> — والفارِغُ يَعني «لا تَسجيلَ إطلاقاً».</param>
    /// <param name="ConfigKeyToday">مِفتاحُ التَهيئَةِ الَّذي يَقرِّر —
    /// و<c>null</c> يَعني «بِلا شَرط»، و<see cref="ByEnvironment"/> تَعني
    /// «مَشروطٌ بِالبيئَةِ وَحدَها، بِلا مِفتاح».</param>
    /// <param name="RegistrationSource">مِلَفُّ العُدَّةِ الَّذي فيه
    /// سَطرُ <c>AddSingleton</c>، ليُقاس مَدى الحَياة لا يُدَّعى.</param>
    private sealed record CapabilityToday(
        string Capability,
        string Interface,
        string RegistrationToday,
        string? ConfigKeyToday,
        string RegistrationSource);

    /// <summary>«مَشروطٌ بِالبيئَةِ لا بِمِفتاحِ تَهيئَة» — قيمَةٌ
    /// مُمَيَّزَةٌ لا تُساوي أَيَّ مِفتاحٍ حَقيقيّ، فَلا تُقرَأُ يَوماً
    /// مِفتاحاً.</summary>
    private const string ByEnvironment = "(environment)";

    private static readonly CapabilityToday[] Rows =
    {
        // ‏2026-08-30 (‏ADR-025): صارَ السَطرُ يُمَرِّرُ قيمَةَ التَهيئَةِ
        // أَيضاً، وصارَ لِلدَفعِ **مِفتاح** بَعدَ أَن لَم يَكُن. وشَرطُ
        // ‏ADR-014 §٢-ج لَم يُنقَض بَل استُوفِيَ: «يُضافُ المِفتاحُ يَومَ
        // يوجَد لَه مُزَوِّدٌ يَختارُه» — ووُجِدَ
        // (`SimulatedPaymentProvider`). والغِيابُ ما زالَ يُعطي المُحاكيَ
        // في التَطويرِ والفَشَلَ المُغلَقَ خارِجَه، بِلا حَرفٍ مُبَدَّل.
        new("payments", "IPaymentProvider",
            "builder.Services.AddPaymentProvider(", "Payments:Provider",
            "libs/kits/Payments/ACommerce.Kit.Payments.Core/MockPaymentProvider.cs"),


        new("sms_otp", "IOtpChannel",
            "builder.Services.AddMockSmsChannel();", "Auth:Sms:Provider",
            "libs/kits/Auth/ACommerce.Kit.Auth.Providers.MockSms/MockSmsChannel.cs"),

        new("email_otp", "IEmailOtpChannel",
            "builder.Services.AddMockEmailChannel();", "Auth:Email:Provider",
            "libs/kits/Auth/ACommerce.Kit.Auth.Providers.MockEmail/MockEmailChannel.cs"),

        new("nafath", "INafathChannel",
            "builder.Services.AddMockNafathChannel(", "Auth:Nafath:Provider",
            "libs/kits/Auth/ACommerce.Kit.Auth.Providers.MockNafath/MockNafathChannel.cs"),

        new("maps", "IMapsProvider",
            "builder.Services.AddMockMaps();", null,
            "libs/kits/Maps/ACommerce.Kit.Maps.Core/MockMapsProvider.cs"),

        new("delivery", "IDeliveryProvider",
            "builder.Services.AddMockDelivery();", null,
            "libs/kits/Delivery/ACommerce.Kit.Delivery.Core/MockDeliveryProvider.cs"),

        // ─── تَعديلٌ مُعلَنٌ ثانٍ — ‏2026-08-30، صَفُّ `files` ─────────
        // كانَ `AddLocalFileStorage(…)` سَطراً عارِياً يَكتُب على قُرصِ
        // الحاوِيَةِ الزائِل، فَتَذهَب صُوَرُ المُستَأجِرينَ عِندَ أَوَّلِ
        // إعادَةِ نَشرٍ **ويَبقى رابِطُها في القاعِدَة**. صارَ القَرارُ
        // دالَّةً نَقِيَّةً (`FileStorageSelection.Decide`) بِمِفتاحِ
        // تَهيئَةٍ حَقيقيّ وحارِسَ إقلاعٍ يَرمي. التَفصيلُ في
        // `docs/ADR-017-TENANT-IMAGES-OUTLIVE-THE-CONTAINER.md`.
        //
        // **وبِخِلافِ `payments`، لِلمِلَفّاتِ مِفتاحُ تَهيئَةٍ حَقيقيّ**
        // ويُقالُ لِماذا: هُنا يوجَد تَنفيذٌ فِعليٌّ **يَبلُغُه
        // التَطبيق** (‏`ACommerce.Kit.Files.Providers.S3` مُحالٌ إلَيه مِن
        // `V1.App.csproj`)، فَالمِفتاحُ يَقبَل قيمَةً **تُسَجِّل شَيئاً**
        // — وذلك بِعَينِه الشَرطُ الَّذي افتَقَدَه الدَفعُ فَتُرِكَ
        // بِالبيئَةِ وَحدَها.
        new("files", "IFileStorage",
            "builder.Services.AddLocalFileStorage(", FileStorageSelection.EndpointKey,
            "libs/kits/Files/ACommerce.Kit.Files.Core/LocalFileStorage.cs"),

        // ─── الواجِهَتانِ اللَّتانِ لا يَبلُغُهُما التَطبيق ──────────
        //
        // **‏2026-08-30 (‏ADR-018): المَصدَرُ صارَ مِلَفَّ الواجِهَةِ
        // نَفسِه** — وكانَ مِلَفَّ تَنفيذٍ. والسَبَبُ أَنّ التَنفيذَينِ
        // **حُذِفا**: ‏`SmtpNotificationChannel` (‏87 سَطراً) و
        // `FirebaseNotificationChannel` (‏158) و`RedisCache` (‏97) —
        // ثَلاثَتُها بِصِفرِ إحالَةٍ مِن أَيّ `csproj` مَشحون، أَي أَنّها
        // **تُبنى ولا تَبلُغ الـSpace**.
        //
        // **والقِياسُ صارَ أَقوى لا أَضعَف**: كانَ يَسأَل «أَيوجَد
        // `AddSingleton` في مِلَفِّ تَنفيذٍ ما؟» — وذلك يَخضَرُّ لِتَنفيذٍ
        // مَيِّت. وصارَ يَسأَل **«أَتوجَد الواجِهَةُ وهَل يُسَجِّلُها
        // أَحَد؟»**، فَيُثَبِّت الحُكمَ الفِعليّ: قُدرَةٌ مُعلَنَةٌ
        // بِصِفرِ تَنفيذٍ مَشحون.
        new("notifications", "INotificationChannel",
            "", null,
            "libs/kits/Notifications/ACommerce.Kit.Notifications.Core/Channels.cs"),

        // و`ICache` بَقِيَ — ويُقالُ لِماذا (‏ADR-018 §٤): ‏`InMemoryCache`
        // فيه لا يَطلُب خادِماً خارِجِيّاً، والمَحذوفُ هُوَ `RedisCache`
        // الَّذي يَطلُبُ خادِمَ Redis لا وُجودَ لَه في النَشر.
        new("cache", "ICache",
            "", null,
            "libs/kits/Cache/ACommerce.Kit.Cache.Core/ICache.cs"),
    };

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string ProgramText =>
        File.ReadAllText(Path.Combine(RepoRoot, "apps", "V1.App", "Program.cs"));

    private static string AppCsprojText =>
        File.ReadAllText(Path.Combine(RepoRoot, "apps", "V1.App", "V1.App.csproj"));

    // ─── ١. الجَدوَلُ نَفسُه ─────────────────────────────────────────

    [Fact]
    public void Nine_capabilities_are_pinned_and_none_repeats()
    {
        Assert.Equal(9, Rows.Length);
        Assert.Equal(9, Rows.Select(r => r.Capability).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(9, Rows.Select(r => r.Interface).Distinct(StringComparer.Ordinal).Count());
    }

    // ─── ٢. التَسجيلُ في `Program.cs` — حَرفاً ────────────────────────

    [Fact]
    public void Every_registered_capability_appears_in_Program_exactly_once()
    {
        var text = ProgramText;
        Assert.True(text.Length > 4000,
            $"أَداة عَمياء: `Program.cs` طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");

        var checkedRows = 0;
        var breaches = new List<string>();

        foreach (var r in Rows.Where(r => r.RegistrationToday.Length > 0))
        {
            checkedRows++;
            var count = CountOccurrences(text, r.RegistrationToday);
            if (count != 1)
                breaches.Add($"{r.Capability}: «{r.RegistrationToday}» ظَهَرَ {count} مَرَّة لا مَرَّةً واحِدَة.");
        }

        Assert.True(checkedRows == 7, $"أَداة عَمياء: فُحِصَ {checkedRows} صَفّاً — والمَقيس ٧.");
        Assert.True(breaches.Count == 0,
            "اختِيارُ المُزَوِّدِ اليَومَ لَيسَ ما وُصِف:\n  " + string.Join("\n  ", breaches));
    }

    [Fact]
    public void The_two_unreached_capabilities_have_no_registration_and_no_project_reference()
    {
        var program = ProgramText;
        var csproj = AppCsprojText;

        // ‏(أ) لا نِداءَ تَسجيلٍ في `Program.cs`.
        foreach (var call in new[]
                 {
                     "AddSmtpNotifications", "AddFirebaseNotifications",
                     "AddRedisCache", "AddInMemoryCache",
                 })
            Assert.False(program.Contains(call, StringComparison.Ordinal),
                $"«{call}» صارَ في `Program.cs` — التَوصيفُ قالَ إنّ القُدرَةَ لا يَبلُغُها التَطبيق.");

        // ‏(ب) ولا إحالَةَ مَشروعٍ تَحمِلُها إلى الـSpace.
        foreach (var fragment in new[] { "Notifications.Providers.", @"kits\Cache\" })
            Assert.False(csproj.Contains(fragment, StringComparison.Ordinal),
                $"إحالَةُ «{fragment}» صارَت في `V1.App.csproj` — والتَوصيفُ قاسَ غِيابَها.");

        // ‏(ج) **ولا تَنفيذَ في الشَجَرَةِ يُسَجِّلُهُما إطلاقاً** —
        // أُضيفَ يَومَ ‏2026-08-30 (‏ADR-018) بَعدَ حَذفِ التَنفيذاتِ
        // الثَلاثَة (‏`SmtpNotificationChannel` و
        // `FirebaseNotificationChannel` و`RedisCache`). **والفَرقُ أَنّ
        // الشَطرَينِ أَعلاه يَقيسانِ التَركيبَ والشَحن، وهذا يَقيسُ
        // الشَجَرَةَ نَفسَها**: مَشروعٌ جَديدٌ يُنشَأ ولا يُحالُ إلَيه
        // يُحمِرُّ هُنا فَوراً، بَدَلَ أَن يَنتَظِرَ جَولَةَ جَردٍ بَعدَ
        // سَنَة. وهذا بِعَينِه ما مَنَعَ اكتِشافَ العَشَرَةِ الَّتي
        // حُذِفَت.
        var libs = Path.Combine(RepoRoot, "libs");
        var scanned = 0;
        var registrars = new List<string>();
        foreach (var file in Directory.EnumerateFiles(libs, "*.cs", SearchOption.AllDirectories))
        {
            var sep = Path.DirectorySeparatorChar;
            if (file.Contains($"{sep}obj{sep}", StringComparison.Ordinal)
             || file.Contains($"{sep}bin{sep}", StringComparison.Ordinal)) continue;
            scanned++;
            var text = File.ReadAllText(file);
            foreach (var iface in new[] { "INotificationChannel", "ICache" })
                if (text.Contains($"AddSingleton<{iface}", StringComparison.Ordinal)
                    && !file.EndsWith("ICache.cs", StringComparison.Ordinal))
                    registrars.Add($"{iface} ← {Path.GetFileName(file)}");
        }
        Assert.True(scanned > 200, $"أَداة عَمياء: مُسِحَ {scanned} مِلَفّاً تَحتَ `libs/` — والمُتَوَقَّعُ مِئات.");
        Assert.True(registrars.Count == 0,
            "تَنفيذٌ يُسَجِّل قُدرَةً وُصِفَت بِأَنّ التَطبيقَ لا يَبلُغُها:\n  "
            + string.Join("\n  ", registrars));

        // وحارِسُ العَمى: المِلَفّانِ قُرِئا فِعلاً.
        Assert.True(program.Length > 4000 && csproj.Length > 2000,
            "أَداة عَمياء: أَحَدُ المِلَفَّينِ لَم يُقرَأ.");
    }

    // ─── ٣. المَشروطُ يَمُرّ بِالجَدوَل، وغَيرُ المَشروطِ عارٍ ────────

    [Theory]
    [InlineData("sms_otp", AuthChannelKind.Sms)]
    [InlineData("email_otp", AuthChannelKind.Email)]
    [InlineData("nafath", AuthChannelKind.Nafath)]
    public void Conditional_capabilities_go_through_the_measured_table(
        string capability, AuthChannelKind kind)
    {
        var row = Rows.Single(r => r.Capability == capability);
        Assert.NotNull(row.ConfigKeyToday);
        Assert.Equal(row.ConfigKeyToday, AuthChannelSelection.ConfigKey(kind));

        Assert.Contains($"AuthChannelSelection.Decide(AuthChannelKind.{kind}",
            ProgramText, StringComparison.Ordinal);
    }

    [Fact]
    public void Unconditional_capabilities_are_registered_with_no_condition_at_all()
    {
        var text = ProgramText.Replace("\r\n", "\n", StringComparison.Ordinal);
        var bare = 0;
        var breaches = new List<string>();

        foreach (var r in Rows.Where(r => r.ConfigKeyToday is null && r.RegistrationToday.Length > 0))
        {
            // سَطرٌ عارٍ = يَبدَأ عِندَ العَمودِ صِفر، أَي خارِجَ أَيّ
            // `switch` أَو `if`. وهذا هُوَ **بِعَينِه** ما يَجعَل
            // المُحاكِيَ جَوابَ الإنتاج.
            if (text.Contains("\n" + r.RegistrationToday, StringComparison.Ordinal)) bare++;
            else breaches.Add($"{r.Capability}: «{r.RegistrationToday}» لَم يَعُد سَطراً عارِياً.");
        }

        // **اثنانِ لا ثَلاثَة مُنذُ ‏2026-08-30 (المَوجَةُ الثانِيَة)**:
        // خَرَجَ `payments` مِن العُري إلى قَرارِ البيئَة، ثُمَّ خَرَجَ
        // `files` إلى قَرارِ التَهيئَة. والباقي `maps` و`delivery`
        // — وحُكمُهُما مَقيسٌ ومَكتوبٌ في `docs/PROVIDER-STUB-DEBT.md`
        // (‏صِفرُ مُستَهلِكٍ في وَقتِ التَشغيل، فَخَطَرُهُما صِفر).
        // والرَقمُ مُثَبَّتٌ فَلا يَنزِل صامِتاً — ولا يَرتَفِع كَذلك.
        Assert.True(bare == 2, $"أَداة عَمياء: وُجِدَ {bare} تَسجيلاً عارِياً — والمَقيس ٢.");
        Assert.True(breaches.Count == 0, string.Join("\n  ", breaches));
    }

    /// <summary>
    /// <para><b>والدَفعُ يَمُرُّ بِقَرارِ بيئَةٍ مَقيس</b> — لا بِمِفتاحِ
    /// تَهيئَة، ويُقالُ لِماذا: لا تَنفيذَ فِعليّاً لِـ
    /// <c>IPaymentProvider</c> يَبلُغُه التَطبيقُ اليَوم، فَمِفتاحٌ
    /// يَقبَل قيمَةً لا تُسَجِّل شَيئاً شَرطٌ لا يَكذِبُ أَبَداً.</para>
    /// </summary>
    [Fact]
    public void The_payment_capability_is_decided_by_environment_and_guarded_at_boot()
    {
        var row = Rows.Single(r => r.Capability == "payments");
        Assert.Equal(PaymentProviderSelection.ProviderKey, row.ConfigKeyToday);

        var text = ProgramText;
        Assert.Contains("builder.Services.AddPaymentProvider(", text, StringComparison.Ordinal);
        Assert.Contains("PaymentProviderSelection.AssertNoStubsOutsideDevelopment",
            text, StringComparison.Ordinal);
        // وحارِسٌ مَعكوسٌ بِجِوارِه: التَجرِبَةُ لا تَقَعُ بِالغِياب.
        Assert.Contains("PaymentProviderSelection.AssertSimulationIsExplicit",
            text, StringComparison.Ordinal);

        // والقَرارُ نَفسُه، بِطَرَفَيه — **بِلا حَرفٍ مُبَدَّل**.
        Assert.Equal(PaymentProviderChoice.Mock, PaymentProviderSelection.Decide(true));
        Assert.Equal(PaymentProviderChoice.Unavailable, PaymentProviderSelection.Decide(false));

        // وبِالمِفتاحِ الصَريحِ وَحدَه تَقَعُ التَجرِبَة.
        Assert.Equal(PaymentProviderChoice.Simulation,
            PaymentProviderSelection.Decide(false, SimulatedPaymentProvider.ConfiguredValue));
        Assert.Equal(PaymentProviderChoice.Unavailable,
            PaymentProviderSelection.Decide(false, "mock"));

    }

    // ─── ٤. مَدى الحَياة — مَرَّةً لِكُلّ عَمَلِيَّةِ تَشغيل ──────────

    [Fact]
    public void Every_capability_is_resolved_once_per_process_not_per_tenant()
    {
        var checkedRows = 0;
        var breaches = new List<string>();

        foreach (var r in Rows)
        {
            var path = Path.Combine(RepoRoot, r.RegistrationSource.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"مَصدَرُ تَسجيلِ «{r.Capability}» مَفقود: {r.RegistrationSource}");

            var src = File.ReadAllText(path);
            checkedRows++;

            // **الصُفوفُ المُسَجَّلَةُ**: يُقاسُ مَدى الحَياةِ في مِلَفِّ
            // التَنفيذ. **والصُفوفُ بِلا تَسجيل**: يُقاسُ أَنّ الواجِهَةَ
            // مُعلَنَةٌ **وأَنّ لا تَنفيذَ مَشحوناً يُسَجِّلُها** — وهذا
            // ما تَغَيَّرَ يَومَ ‏2026-08-30 (‏ADR-018) حينَ حُذِفَت
            // تَنفيذاتٌ لا يَبلُغُها البِناء.
            if (r.RegistrationToday.Length > 0)
            {
                if (!src.Contains($"AddSingleton<{r.Interface}", StringComparison.Ordinal))
                    breaches.Add($"{r.Capability}: لا `AddSingleton<{r.Interface}` في {r.RegistrationSource}.");
            }
            else
            {
                if (!src.Contains($"interface {r.Interface}", StringComparison.Ordinal))
                    breaches.Add($"{r.Capability}: لا إعلانَ `interface {r.Interface}` في {r.RegistrationSource}.");
            }

            // ولا واحِدٌ مِنها يَرى المُستَأجِر — وهذِه هي العِلَّةُ
            // الَّتي تَأتي المَوجَةُ لِأَجلِها، فَتُقاس قَبلَها.
            if (src.Contains("ITenantContext", StringComparison.Ordinal))
                breaches.Add($"{r.Capability}: صارَ يَذكُر ITenantContext في تَسجيلِه.");
        }

        Assert.True(checkedRows == 9, $"أَداة عَمياء: فُحِصَ {checkedRows} مَصدَراً — والمَقيس ٩.");
        Assert.True(breaches.Count == 0,
            "مَدى حَياةِ المُزَوِّدِ لَيسَ ما وُصِف:\n  " + string.Join("\n  ", breaches));
    }

    // ─── ٥. جَدوَلُ القَرارِ الثُلاثيّ — كامِلاً ──────────────────────
    //
    // نَفسُ الدالَّةِ الَّتي يَقيسُها `AuthChannelSelectionTests`،
    // مُعادَةً هُنا **في إطارِ القُدُرات** لِأَنّ هذا المِلَفَّ هُوَ
    // الطَرَفُ المُقارَنُ بَعدَ المَوجَة: مَن يَقرَؤُه وَحدَه يَعرِف
    // جَوابَ التِسعِ كامِلَةً بِلا أَن يَفتَحَ مِلَفّاً آخَر.

    [Theory]
    // SMS
    [InlineData(AuthChannelKind.Sms, null, true, AuthChannelProvider.Mock)]
    [InlineData(AuthChannelKind.Sms, null, false, AuthChannelProvider.None)]
    [InlineData(AuthChannelKind.Sms, "", true, AuthChannelProvider.Mock)]
    [InlineData(AuthChannelKind.Sms, "twilio", false, AuthChannelProvider.Twilio)]
    [InlineData(AuthChannelKind.Sms, "TWILIO", false, AuthChannelProvider.Twilio)]
    [InlineData(AuthChannelKind.Sms, "mock", false, AuthChannelProvider.None)]
    [InlineData(AuthChannelKind.Sms, "unifonic", false, AuthChannelProvider.None)]
    // Email
    [InlineData(AuthChannelKind.Email, null, true, AuthChannelProvider.Mock)]
    [InlineData(AuthChannelKind.Email, null, false, AuthChannelProvider.None)]
    [InlineData(AuthChannelKind.Email, "smtp", false, AuthChannelProvider.Smtp)]
    [InlineData(AuthChannelKind.Email, "brevo", false, AuthChannelProvider.Brevo)]
    [InlineData(AuthChannelKind.Email, "moyasar", false, AuthChannelProvider.None)]
    // Nafath
    [InlineData(AuthChannelKind.Nafath, null, true, AuthChannelProvider.Mock)]
    [InlineData(AuthChannelKind.Nafath, null, false, AuthChannelProvider.None)]
    [InlineData(AuthChannelKind.Nafath, "nafath", false, AuthChannelProvider.Nafath)]
    [InlineData(AuthChannelKind.Nafath, "mock", true, AuthChannelProvider.Mock)]
    public void The_three_conditional_capabilities_decide_exactly_as_today(
        AuthChannelKind kind, string? configured, bool isDev, AuthChannelProvider expected)
        => Assert.Equal(expected, AuthChannelSelection.Decide(kind, configured, isDev));

    // ─── أَدَوات ─────────────────────────────────────────────────────

    private static int CountOccurrences(string haystack, string needle)
    {
        var n = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal);
             i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }
}
