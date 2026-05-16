using ACommerce.Chat.Client.Blazor;
using ACommerce.Client.Operations;
using ACommerce.ClientHost;
using ACommerce.ClientHost.Auth;
using ACommerce.ClientHost.Culture;
using ACommerce.ClientHost.KitApi;
using ACommerce.ClientHost.Operations;
using ACommerce.ClientHost.Preferences;
using ACommerce.Compositions.Customer.Chat.Realtime;
using ACommerce.Compositions.Customer.Favorites.Realtime;
using ACommerce.Compositions.Customer.L10n.Resx;
using ACommerce.Compositions.Customer.Marketplace.Home;
using ACommerce.Compositions.Customer.Notifications.Realtime;
using ACommerce.Compositions.Customer.Timezone.Js;
using ACommerce.Compositions.Customer.Unread;
using ACommerce.Culture.Abstractions;
using ACommerce.Culture.Defaults;
using ACommerce.Culture.Interceptors;
using ACommerce.Kits.Auth.Frontend.Customer.Stores;
using ACommerce.Kits.Chat.Frontend.Customer.Stores;
using ACommerce.Kits.Discovery.Frontend.Customer.Stores;
using ACommerce.Kits.Favorites.Frontend.Customer.Stores;
using ACommerce.Kits.Listings.Frontend.Customer.Stores;
using ACommerce.Kits.Notifications.Frontend.Customer.Stores;
using ACommerce.Kits.Profiles.Frontend.Customer.Stores;
using ACommerce.Kits.Reports.Frontend.Customer.Stores;
using ACommerce.Kits.Subscriptions.Frontend.Customer.Stores;
using ACommerce.Kits.Support.Frontend.Customer.Stores;
using ACommerce.Kits.Versions.Templates;
using ACommerce.L10n.Blazor;
using ACommerce.OperationEngine.Interceptors;
using ACommerce.OperationEngine.Interceptors.Extensions;
using ACommerce.Subscriptions.Templates.Extensions;
using Ejar.Customer.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ejar.Customer.UI;

/// <summary>
/// تَسجيل DI الموحَّد لِكلّ خَدَمات قالَب Customer.Marketplace — pure-OAM
/// بَعد F71. كلّ V1 services (AppStore، ApiReader، UnreadService،
/// AppStateApplier، EjarOps، AppStorePersistence) مَحذوفَة. القالَب يَحوي
/// الآن: Pages + Resources/.resx + EjarCustomerUiExtensions (هذا المَلَفّ)
/// فَقَط — كلّ المَنطِق في kits + compositions + ClientHost.
/// </summary>
public static class EjarCustomerUiExtensions
{
    public static IServiceCollection AddEjarCustomerUI(this IServiceCollection services)
    {
        // ─── Auth machinery (ClientHost.Auth) ──────────────────────────
        services.AddClientAuth(o =>
        {
            o.HttpClientName = "ejar";
            o.StorageKey     = "ejar.auth";
            o.Scheme         = "EjarAuth";
        });

        // ─── UI Preferences + ITemplateStore adapter ──────────────────
        // ejar.ui يَحفَظ Theme + Language + City + RecentSearches + ActiveQuickFilterIds
        // عَبر LocalStorageUiPersistence. ClientStateTemplateStore adapter يَكشِف
        // IClientAuthState + IUiPreferences كَ ITemplateStore لِيَستَهلِكها widgets
        // (AcMarketplaceHomePage، AcListingExplorePage) دون لَمس AppStore المَحذوف.
        services.AddUiPreferences("ejar.ui");
        services.AddScoped<ITemplateStore, ClientStateTemplateStore>();

        // ─── Culture: ICultureContext + ILanguageContext من IUiPreferences ─
        // كانَت سابِقاً مِن AppStoreCultureContext. الآن adapter داخِليّ يَقرأ
        // مِن IUiPreferences (Language) + StaticCultureContext لِلباقي.
        services.AddScoped<ICultureContext, UiPreferencesCultureContext>();
        services.AddScoped<ILanguageContext>(sp => (ILanguageContext)sp.GetRequiredService<ICultureContext>());

        // ─── L10n: layered translation (G3) ──────────────────────────────
        // طَبَقَة القالَب neutral (الأَدنى — apps يُعَلون فَوقها بِـ AddTranslationLayer
        // إضافيّ مِن Program.cs الخاصّ بِالتَطبيق). كلّ layer مُعَرَّف بِـ
        // .resx + companion class مُولَّدَة بِـ Source Generator (TemplateStrings).
        services.AddTranslationLayer(
            typeof(EjarCustomerUiExtensions).Assembly,
            baseName: "Ejar.Customer.UI.Resources.Strings");
        services.AddLayeredTranslation();   // wraps كلّ ITranslationProvider في wrapper وَحيد
        services.AddScoped<L>();

        // ─── Culture utilities (timezone composition / numerals) ───────
        services.AddJsTimezoneProvider();
        services.AddSingleton<INumeralNormalizer, DefaultNumeralNormalizer>();
        // افتِراضي MarketplaceUiOptions: كُلّ القَوائِم مَعروضَة. التَطبيق
        // يَتَجاوَز بِـ AddSingleton(new MarketplaceUiOptions { ... }) قَبل
        // AddAshareV3Customer أَو ما يُماثِله ⇒ TryAdd يَستَسلِم.
        services.TryAddSingleton<MarketplaceUiOptions>(_ => new MarketplaceUiOptions());
        // Default open gate — تَطبيقات تَفرِض دَفع/اشتِراك تَتَجاوَزه بِـ AddScoped.
        services.TryAddScoped<IListingPublishGate, OpenPublishGate>();
        // Default precheck — تَطبيقات الاشتِراك تَتَجاوَزها بِفَحص اشتِراك نَشِط.
        services.TryAddScoped<IListingCreatePrecheck, OpenCreatePrecheck>();

        // ─── HTTP handlers مِن الكيتس ─────────────────────────────────
        services.AddTransient<CultureHeadersHandler>();
        services.AddScoped<CultureInterceptor>();

        // ─── Versions Kit (Templates + Poll + Headers handler) ─────────
        services.AddVersionsTemplates(httpClientName: "ejar");

        // ─── Subscriptions Kit (frontend) ─────────────────────────────
        services.AddSubscriptionsTemplates();

        // ─── OAM client engine + kit routes ────────────────────────────
        services.AddClientOpEngine();
        services.AddAuthRoutes();
        services.AddListingsRoutes();
        services.AddChatRoutes();
        services.AddNotificationsRoutes();
        services.AddProfilesRoutes();
        services.AddSubscriptionsRoutes();
        services.AddSupportRoutes();
        services.AddFavoritesRoutes();
        services.AddDiscoveryRoutes();
        services.AddReportsRoutes();
        services.AddCustomerMarketplaceHomeComposition();

        // ─── Listing draft (Listings kit) — لِصَفحَة CreateListing ────
        services.AddListingDraft();

        // ─── Discovery store (cities/amenities/categories) ────────────
        services.AddDiscoveryStore();

        // ─── Culture controller — OAM-driven setLanguage/setTheme/setCity
        services.AddClientCultureController();

        // ─── FavoritesSync + FirebasePush (compositions/providers) ────
        services.AddScoped<FavoritesSync>();
        services.AddScoped<FirebasePushService>();

        // ─── KitApi pipeline ────────────────────────────────────────────
        services.AddKitApiPipeline(sp =>
            sp.GetRequiredService<AuthenticatedHttpClient>().Client);

        // ─── Login UI: افتِراضي = PhoneOtp. التَطبيق يَستَطيع تَجاوُزه
        //     بِـ services.AddSingleton<IAuthLoginUi>(new StaticAuthLoginUi(...))
        //     بَعد هذا التَسجيل لِيَختار Nafath أَو غَيره.
        services.TryAddSingleton<Ejar.Customer.UI.Components.Pages.Auth.IAuthLoginUi>(
            new Ejar.Customer.UI.Components.Pages.Auth.StaticAuthLoginUi(
                typeof(Ejar.Customer.UI.Components.Pages.Auth.PhoneOtpLoginContent)));

        // ─── kit ApiClients ────────────────────────────────────────────
        services.AddScoped<IAuthApiClient,          HttpAuthApiClient>();
        services.AddScoped<IListingsApiClient,      HttpListingsApiClient>();
        services.AddScoped<IChatApiClient,          HttpChatApiClient>();
        services.AddScoped<INotificationsApiClient, HttpNotificationsApiClient>();
        services.AddScoped<IProfileApiClient,       HttpProfileApiClient>();
        services.AddScoped<ISubscriptionsApiClient, HttpSubscriptionsApiClient>();
        services.AddScoped<ISupportApiClient,       HttpSupportApiClient>();
        services.AddScoped<IFavoritesApiClient,     HttpFavoritesApiClient>();

        // ─── kit IXxxStore bindings (Default<Kit>Stores OAM-driven) ────
        services.AddScoped<IAuthStore,          DefaultAuthStore>();
        services.AddScoped<IListingsStore,      DefaultListingsStore>();
        services.AddScoped<IChatStore,          DefaultChatStore>();
        services.AddScoped<INotificationsStore, DefaultNotificationsStore>();
        services.AddScoped<IProfileStore,       DefaultProfileStore>();
        services.AddScoped<ISubscriptionsStore, DefaultSubscriptionsStore>();
        services.AddScoped<ISupportStore,       DefaultSupportStore>();
        services.AddScoped<IFavoritesStore,     DefaultFavoritesStore>();

        // ─── Realtime + Chat client (Customer.SignalR composition) ────
        services.AddScoped<EjarRealtimeService>();
        services.AddScoped<IChatClient, EjarChatClient>();

        // ─── Compositions (cross-kit + realtime ingestors) ─────────────
        services.AddCustomerUnreadComposition();
        services.AddChatRealtimeIngestor();
        services.AddNotificationsRealtimeComposition();
        services.AddFavoritesRealtimeComposition();

        // ─── OAM-seam cross-cutting interceptors ───────────────────────
        services.AddScoped<IOperationInterceptor, CultureLocalizationInterceptor>();
        services.AddOperationInterceptors();

        return services;
    }
}

/// <summary>
/// Adapter يَكشِف <see cref="IUiPreferences"/> + StaticCultureContext كَـ
/// <see cref="ICultureContext"/> + <see cref="ILanguageContext"/>. حَلَّ
/// مَحَلّ <c>AppStoreCultureContext</c> الـ V1 المَحذوف. يَقرأ Language مِن
/// <see cref="IUiPreferences"/>؛ تَفاصيل timezone/currency تَأتي مِن defaults
/// (يُمكِن لاحِقاً جَعلها مِن IUiPreferences أيضاً عِند الحاجة).
/// </summary>
internal sealed class UiPreferencesCultureContext : ICultureContext, ILanguageContext
{
    private readonly IUiPreferences _prefs;
    public UiPreferencesCultureContext(IUiPreferences prefs) => _prefs = prefs;

    public string Language       => string.IsNullOrEmpty(_prefs.Language) ? "ar" : _prefs.Language;
    public string TimeZoneId     => "Asia/Aden";
    public string NumeralSystem  => Language == "ar" ? "arabic-indic" : "latin";
    public TimeZoneInfo TimeZone => StaticCultureContext.ResolveTz(TimeZoneId);
    public string Currency       => "YER";
    public bool   IsRtl          => Language == "ar";
}
