namespace ACommerce.Templates.Customer.Marketplace;

/// <summary>
/// i18n خَفيف. قاموسان ثابِتان داخل القالَب (ar + en) — لا حاجَة
/// لـ ResourceManager في هذه المَرحَلَة. الاستِخدام:
///   @inject L L
///   @(L["home.title"])
///   L.Lang = "en"   (يُحَدِّث لكلّ الصَفحَة)
/// </summary>
public sealed class L
{
    public string Lang { get; set; } = "ar";

    public string this[string key] =>
        Lang == "en"
            ? (En.TryGetValue(key, out var en) ? en : key)
            : (Ar.TryGetValue(key, out var ar) ? ar : key);

    private static readonly Dictionary<string, string> Ar = new()
    {
        ["nav.home"]       = "الرَئيسيّة",
        ["nav.explore"]    = "استِكشاف",
        ["nav.chats"]      = "رَسائل",
        ["nav.notifs"]     = "إشعارات",
        ["nav.account"]    = "حِسابي",
        ["nav.login"]      = "دُخول",
        ["common.loading"] = "جارٍ التَحميل…",
        ["common.empty"]   = "لا توجَد بَيانات بَعد.",
        ["common.back"]    = "رُجوع",
        ["auth.login"]     = "تَسجيل دُخول",
        ["auth.logout"]    = "تَسجيل خُروج",
        ["listings.contact"] = "تَواصُل مَع المُعلِن",
        ["listings.views"] = "مَشاهَدات",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["nav.home"]       = "Home",
        ["nav.explore"]    = "Explore",
        ["nav.chats"]      = "Messages",
        ["nav.notifs"]     = "Alerts",
        ["nav.account"]    = "Me",
        ["nav.login"]      = "Login",
        ["common.loading"] = "Loading…",
        ["common.empty"]   = "Nothing here yet.",
        ["common.back"]    = "Back",
        ["auth.login"]     = "Sign in",
        ["auth.logout"]    = "Sign out",
        ["listings.contact"] = "Contact seller",
        ["listings.views"] = "views",
    };
}
