using ACommerce.Kit.Auth.Server;
using Marten;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// مُصادَقَة studio على مُستَوى المَنصَّة (مُنفَصِلَة عَن مُصادَقَة المَتاجِر).
/// وَهميَّة: أَيّ رَقم + الرَّمز "123456". تُخَزِّن <see cref="StudioUser"/>
/// تَحت tenant "_studio" وَتَكتُب cookie ".acommerce.studio". تُعيد استِخدام
/// <c>AuthHandlers.MakeToken/ParseToken</c> بِـ tenant = "_studio".
/// </summary>
public sealed class StudioAuth
{
    public const string Tenant = "_studio";
    public const string CookieName = ".acommerce.studio";
    public const string DevCode = "123456";

    private readonly IHttpContextAccessor _http;
    public StudioAuth(IHttpContextAccessor http) => _http = http;

    public Guid? UserId { get; private set; }
    public string? UserName { get; private set; }
    public bool IsAuthenticated => UserId.HasValue;

    /// <summary>يُحَمِّل الحالَة مِن cookie الطَّلَب الحاليّ.</summary>
    public void Load()
    {
        var ctx = _http.HttpContext;
        if (ctx is null) return;
        var token = ctx.Request.Cookies[CookieName];
        var parsed = AuthHandlers.ParseToken(token);
        if (parsed is null || parsed.Value.TenantSlug != Tenant) { Clear(); return; }
        UserId = parsed.Value.UserId;
        UserName = ctx.Request.Cookies[CookieName + ".name"];
    }

    public void Clear() { UserId = null; UserName = null; }

    /// <summary>تَحَقُّق وهميّ: الرَّمز يَجِب أَن يُساوي "123456". يُنشِئ أَو
    /// يُحَمِّل المُستَخدِم بِرَقم الهاتِف، يَكتُب الـ cookie، يُعيد الـ user.</summary>
    public static async Task<StudioUser?> VerifyAsync(
        IDocumentStore store, HttpResponse res, string phone, string code)
    {
        if (code.Trim() != DevCode) return null;
        phone = phone.Trim();
        if (string.IsNullOrEmpty(phone)) return null;

        await using var s = store.LightweightSession(Tenant);
        var user = (await s.Query<StudioUser>().Where(u => u.Phone == phone).ToListAsync())
            .FirstOrDefault();
        var isNew = user is null;
        user ??= new StudioUser { Id = Guid.NewGuid(), Phone = phone };
        user.LastLoginAt = DateTime.UtcNow;
        s.Store(user);
        await s.SaveChangesAsync();

        WriteCookie(res, user);
        return user;
    }

    public static void WriteCookie(HttpResponse res, StudioUser user)
    {
        var token = AuthHandlers.MakeToken(user.Id, Tenant);
        var opts = new CookieOptions
        {
            HttpOnly = true, IsEssential = true, SameSite = SameSiteMode.Lax,
            Path = "/", Expires = DateTimeOffset.UtcNow.AddDays(30)
        };
        res.Cookies.Append(CookieName, token, opts);
        res.Cookies.Append(CookieName + ".name", user.FullName,
            new CookieOptions { IsEssential = true, Path = "/", Expires = opts.Expires });
    }

    public static void DeleteCookie(HttpResponse res)
    {
        res.Cookies.Delete(CookieName);
        res.Cookies.Delete(CookieName + ".name");
    }
}
