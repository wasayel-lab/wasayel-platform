using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace;

/// <summary>
/// i18n خَفيف. قاموسان ثابِتان داخل القالَب (ar + en).
/// يَقرَأ اللُغَة المُختارَة من cookie ".acommerce.lang" عند بِنائه (scoped).
/// </summary>
public sealed class L
{
    public const string CookieName = ".acommerce.lang";

    public L(IHttpContextAccessor http)
    {
        var ctx = http.HttpContext;
        if (ctx is not null && ctx.Request.Cookies.TryGetValue(CookieName, out var lang))
        {
            if (lang == "en") Lang = "en";
        }
    }

    public string Lang { get; set; } = "ar";
    public bool IsArabic => Lang == "ar";
    public string Dir => IsArabic ? "rtl" : "ltr";

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
