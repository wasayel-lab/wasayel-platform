using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Providers.MockEmail;
using ACommerce.Kit.Auth.Providers.MockNafath;
using ACommerce.Kit.Auth.Providers.MockSms;
using ACommerce.Kit.Auth.Providers.Nafath;
using ACommerce.Kit.Auth.Providers.Smtp;
using ACommerce.Kit.Auth.Providers.Twilio;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Culture;
using ACommerce.Kit.Delivery;
using ACommerce.Kit.Files;
using ACommerce.Kit.Maps;
using ACommerce.Kit.Payments;
using ACommerce.Kit.Realtime.Server;
using ACommerce.Kit.Versions;
using ACommerce.Platform.Hosting;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Components;
using ACommerce.V1.App.Seed;

var builder = WebApplication.CreateBuilder(args);

// خَلف proxy (Hugging Face Spaces, Cloudflare, …) نَحتاج قِراءَة
// X-Forwarded-* لِيَكشِف Request.IsHttps الصَّحيح — وإلّا AuthSession
// يَحسِب الاتِّصال HTTP فَيَكسِر Secure cookies في الإنتاج.
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(opts =>
{
    opts.ForwardedHeaders =
        Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor |
        Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto |
        Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost;
    // proxy المُستَضيف قَد لا يَكون في 127.0.0.1 — اِقبَل مِن أَيّ مَصدَر.
    // آمِن لِأَنّ الـ middleware يَكتُب Request.Scheme فَقَط، لا الـ IP.
    opts.KnownNetworks.Clear();
    opts.KnownProxies.Clear();
});

builder.AddPlatformHost(host => host
    .AddKitAssembly(typeof(ACommerce.Kit.Tenants.Server.TenantHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Listings.Server.ListingHandlers).Assembly)
    .AddKitAssembly(typeof(AuthHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Notifications.Server.NotificationHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Chat.Server.ChatHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Favorites.Server.FavoriteHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Subscriptions.Server.SubscriptionHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Support.Server.TicketHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Profiles.Server.ProfileHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Cart.Server.CartHandlers).Assembly)
    .AddKitAssembly(typeof(ACommerce.Kit.Reports.Server.ReportHandlers).Assembly)
    .AddKitAssembly(typeof(RealtimeBroadcastHandler).Assembly));

// نَمَط ثَقافيّ + بَوّابَة إصدار (W3 — kits ناقِصَة مَنقولَة بِنَمَط v1).
builder.Services.AddCultureContext();
builder.Services.AddVersionGate(opts =>
{
    opts.MinimumSupported = "1.0.0";
    opts.LatestSuggested = "1.0.0";
});

// ─── قَنَواتُ الدُخول — بِالتَهيئَة، وفَشَلٌ مُغلَقٌ في الإنتاج ────────
// كانَ المُحاكيان (‏SMS ونَفاذ) يُسَجَّلان **بِلا شَرطِ بيئَة**، فَرَمزُ
// الدُخول في الإنتاج ثابِتٌ `123456` لِأَيّ رَقم، ومُحاكي نَفاذ يُوافِق
// تِلقائِيّاً عَلى أَيّ هُوِيَّة. القَرارُ الآنَ دالَّةٌ مَقيسَةٌ بِجَدوَل
// (‏`AuthChannelSelection.Decide`)، والسُطورُ أَدناه أَثَرُها لا مَنطِقُها.
// والغِيابُ خارِجَ التَطوير = لا قَناة = رَفضُ طَلَبِ الرَمز بِرِسالَةٍ
// مِن القامُوس. أَسماءُ المُتَغَيِّرات في `docs/DEPLOY.md` § قَنَواتُ الدُخول.
var isDev = builder.Environment.IsDevelopment();

switch (AuthChannelSelection.Decide(AuthChannelKind.Sms,
            builder.Configuration[AuthChannelSelection.SmsProviderKey], isDev))
{
    case AuthChannelProvider.Twilio:
        builder.Services.AddTwilioSmsChannel(opts =>
            builder.Configuration.GetSection("Auth:Twilio").Bind(opts));
        break;
    case AuthChannelProvider.Mock:
        builder.Services.AddMockSmsChannel();
        break;
}

switch (AuthChannelSelection.Decide(AuthChannelKind.Nafath,
            builder.Configuration[AuthChannelSelection.NafathProviderKey], isDev))
{
    case AuthChannelProvider.Nafath:
        builder.Services.AddNafathChannel(opts =>
            builder.Configuration.GetSection("Auth:Nafath").Bind(opts));
        break;
    case AuthChannelProvider.Mock:
        builder.Services.AddMockNafathChannel(opts =>
        { opts.DisplayCode = "00"; opts.AutoApproveSeconds = 5; });
        break;
}

// قَناة البَريد — نَفسُ مِفتاح `Auth:Email:Provider` القائِم مُنذُ مَوجَةِ
// البَريد، صارَ يَمُرّ بِالجَدوَل نَفسِه. كُلّ إعدادات SMTP مِن التَهيئَة،
// لا سِرَّ في الكود؛ يَعمَل مَع أَيّ SMTP بِما فيه Azure Communication Services.
switch (AuthChannelSelection.Decide(AuthChannelKind.Email,
            builder.Configuration[AuthChannelSelection.EmailProviderKey], isDev))
{
    case AuthChannelProvider.Smtp:
        builder.Services.AddSmtpEmailChannel(opts =>
            builder.Configuration.GetSection("Auth:Email").Bind(opts));
        break;
    case AuthChannelProvider.Mock:
        builder.Services.AddMockEmailChannel();
        break;
}

// مُزَوِّدو البِنيَة (mock — استَبدِلهم لاحِقاً بِـ Moyasar/Saee/Google Maps).
builder.Services.AddMockMaps();
builder.Services.AddMockDelivery();
builder.Services.AddMockPayments();

// تَخزين مَلَفّات — Local (افتِراضيّ، صَفّ wwwroot/uploads). لِلإنتاج
// بَدِّل بِـ AddAliyunOssFileStorage(...) أو AddGoogleCloudFileStorage(...).
builder.Services.AddLocalFileStorage(opts =>
{
    opts.RootPath = Path.Combine(builder.Environment.WebRootPath ?? "wwwroot", "uploads");
    opts.PublicPathPrefix = "/uploads";
});

// القالَب — يُسَجِّل AuthSession + HttpContextAccessor
builder.Services.AddCustomerMarketplaceTemplate();

var app = builder.Build();

// ─── حارِسُ الإقلاع: لا مُحاكِيَ خارِجَ التَطوير ─────────────────────
// **الحِراسَةُ في التَركيب لا في الجِسم**: القَرارُ أَعلاه لا يُسَجِّل
// مُحاكِياً خارِجَ التَطوير — وهذا يُثبِتُ أَنَّه لَم يَحدُث. سَطرُ
// `AddMockSmsChannel()` مُباشِرٌ يَعود يَوماً سَهواً (وهو ما وَقَعَ فِعلاً)
// فَيَرمي هُنا قَبلَ أَوَّلِ طَلَب، بَدَلَ أَن يَصمُتَ ويَقبَل `123456`.
AuthChannelSelection.AssertNoStubsOutsideDevelopment(
    app.Environment.IsDevelopment(),
    new[]
    {
        Describe(AuthChannelKind.Sms,    app.Services.GetService<IOtpChannel>()),
        Describe(AuthChannelKind.Email,  app.Services.GetService<IEmailOtpChannel>()),
        Describe(AuthChannelKind.Nafath, app.Services.GetService<INafathChannel>())
    }.OfType<RegisteredAuthChannel>());

static RegisteredAuthChannel? Describe(AuthChannelKind kind, object? channel) => channel switch
{
    null => null,
    IOtpChannel c      => new(kind, c.ChannelName, c is IDevelopmentStubChannel || c.DevHintCode is not null),
    IEmailOtpChannel c => new(kind, c.ChannelName, c is IDevelopmentStubChannel || c.DevHintCode is not null),
    INafathChannel c   => new(kind, c.ChannelName, c is IDevelopmentStubChannel),
    _ => null
};

// AuthSession يَحتاج IHttpContextAccessor لِيَكشِف HTTPS فَيَضَع Secure cookie
// تِلقائيّاً (آمِن في الإنتاج، يَعمَل على HTTP المَحَلّيّ).
ACommerce.Templates.Customer.Marketplace.AuthSession.HttpAccessor =
    app.Services.GetRequiredService<IHttpContextAccessor>();

await using (var scope = app.Services.CreateAsyncScope())
{
    await PlatformSeed.RunAsync(scope.ServiceProvider);
    // اِربِط المَتاجِر القَديمَة بِأَوَّل مُستَخدِم studio (إن وُجِد).
    var docStore = scope.ServiceProvider.GetRequiredService<Marten.IDocumentStore>();
    await ACommerce.Templates.Customer.Marketplace.Services.Incubator
        .StudioOwnershipSeeder.RunAsync(docStore);

    // بَيانات اختِبار لِفَحص Layer 6 — لا تَعمَل في الإنتاج. تُفَعَّل
    // بِـ ENV TEST_DATA_SEED=1، وإلّا تُتَجاوَز.
    if (Environment.GetEnvironmentVariable("TEST_DATA_SEED") == "1")
        await TestDataSeeder.RunAsync(scope.ServiceProvider);

    // جَلسَةُ حاضِنَة **عَيِّنَةَ بِنيَة** — تَفتَح شاشات الدِراسَة الثَلاث
    // بِلا مُزَوِّد LLM ولا مِفتاح API. نَفسُ اصطِلاح البَذرَة أَعلاه:
    // ‏ENV INCUBATOR_SAMPLE_SEED=1، وإلّا لا تَعمَل. التَفصيلُ وسَبَبُ
    // كَونِ القيَم مُعَلَّمَةً في `IncubatorSampleSeeder`.
    if (Environment.GetEnvironmentVariable("INCUBATOR_SAMPLE_SEED") == "1")
        await IncubatorSampleSeeder.RunAsync(scope.ServiceProvider);

    // سَلَّةُ **عَيِّنَةِ بِنيَة** — تَفتَح جِسمَ مُعالِج الشِراء الثُلاثيّ
    // الَّذي لا يُصَيَّر بِلا سَلَّة، وكانَ في القاعِدَة صِفرُ سَلَّة.
    // نَفسُ الاصطِلاح: ‏ENV CART_SAMPLE_SEED=1، وإلّا صِفرُ قِراءَةٍ
    // وصِفرُ كِتابَة. التَفصيلُ في `CartSampleSeeder`.
    if (Environment.GetEnvironmentVariable("CART_SAMPLE_SEED") == "1")
        await CartSampleSeeder.RunAsync(scope.ServiceProvider);

    // مَنح صَلاحِيَّة مُشرِف المَنصَّة صَراحَةً — ENV PLATFORM_ADMIN_PHONE
    // و/أَو PLATFORM_ADMIN_EMAIL، وخارِج التَطوير يَلزَم
    // PLATFORM_ADMIN_BOOTSTRAP=1 مَعَهُما. البَريدُ أُضيفَ لِأَنّ الهاتِفَ
    // يُغلَق في الإنتاج بِلا Auth__Sms__Provider (‏cd43b366)، والمالِكُ
    // يَضبُط SMTP وَحدَه — فَمُعَرِّفٌ هاتِفيٌّ حَصريٌّ يَحبِسُه خارِجَ
    // إدارَتِه. الجَدوَلُ والتَطبيعُ في PlatformAdminGrant (مُختَبَران).
    var granted = await PlatformAdminSeeder.RunAsync(docStore, app.Environment);
    if (granted.Phone is not null)
        app.Logger.LogWarning(
            "[platform-admin] مُنِحَ الهاتِفُ {Phone} صَلاحِيَّةَ مُشرِف المَنصَّة", granted.Phone);
    if (granted.Email is not null)
        app.Logger.LogWarning(
            "[platform-admin] مُنِحَ البَريدُ {Email} صَلاحِيَّةَ مُشرِف المَنصَّة", granted.Email);
    // صيغَةٌ مُشَوَّهَةٌ تُغلِق ولا تَرتَدّ — تُقال بِاسم المُتَغَيِّر، وإلّا
    // بَدا الصَمتُ نَجاحاً.
    if (granted.EmailRejected)
        app.Logger.LogWarning(
            "[platform-admin] {Var} صيغَتُه غَير صالِحَة — لا مَنحَ بِالبَريد",
            PlatformAdminSeeder.EmailVar);
}

// يَجِب أَن يُطَبَّق ForwardedHeaders قَبل أَيّ middleware يَقرَأ
// Request.IsHttps / Scheme (الـ HTTPS redirect والكوكي والـ Auth).
app.UseForwardedHeaders();

app.UsePlatformHost();

// تَفعيل خِدمَة المَلَفّات المَحَلِّيَّة (Local provider فَقَط — تُتَجاهَل لَو
// السيرفِر يَستَخدِم Aliyun/GCS مَع CDN).
if (app.Services.GetService<IFileStorage>() is LocalFileStorage)
    app.UseLocalFileStorage();

// W3 middleware — Culture + Version gate.
app.UseCultureContext();
app.UseVersionGate();

// القالَب — يُسَجِّل form endpoints (auth/login/logout/chat send/favorite/...)
app.MapCustomerMarketplaceTemplate();

app.MapHub<RealtimeHub>("/realtime");

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
