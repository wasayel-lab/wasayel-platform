using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>
/// <para><b>بَوّابَةُ إصدارِ الجَلسَة — «اخرج من كل الأجهزة»</b> (‏2026-10-28، قَرارُ المالِك د28 «ب»، مَرجِعُه OWASP Session Management و
/// NIST 800-63B: أَن يُنهي المُستَخدِمُ جَلَساتِه كُلَّها مِن جِهَةِ الخادِم، فَلا يَحتاجُ هاتِفٌ مَسروقٌ إلى تَبديلِ السِرِّ العامِّ وإخراجِ الجَميع).
/// كانَ الرَمزُ HMAC بِثَلاثينَ يَوماً و<c>ParseToken</c> يَفحَصُ التَوقيعَ وَحدَه، فَلا شَيءَ يُسقِطُ رَمزاً صَدَر — ولا حَذفُ الحِساب
/// (تَوصيفُه <c>f22108d</c>).</para>
///
/// <para><b>ما يَفعَلُه</b>: لِكُلِّ طَلَبٍ يَحمِلُ كوكيَّ جَلسَة (المَتجَرُ <c>.acommerce.auth.*</c> والاستوديو <c>.acommerce.studio</c>) يُقارِنُ
/// إصدارَ الرَمزِ (<see cref="AuthHandlers.SessionVersionOf"/>) بِإصدارِ الحِسابِ المُخَزَّن (<c>User.SessionVersion</c> /
/// <c>StudioUser.SessionVersion</c>)؛ فَإن كانَ أَصغَرَ نُزِعَ الكوكيُّ مِن الطَلَبِ قَبلَ أَن يَقرَأَه أَحَد — صَفحَةً أَو نُقطَةً أَو مُرَشِّحاً —
/// ومُحِيَ مِن المُتَصَفِّح. فَمَوضِعُ القَرارِ واحِدٌ لِكُلِّ قارِئٍ لِلجَلسَة (القاعِدَة ٨)، لا سَطرٌ في كُلِّ جِسم.</para>
///
/// <para><b>وكَلفَتُه عَلى القاعِدَةِ مَحدودَة</b>: الجَلسَةُ لَم تَكُن تَقرَأُ القاعِدَةَ أَصلاً، فَالإصدارُ يُحفَظُ في ذاكِرَةِ الخادِمِ
/// <see cref="CacheFor"/> (دَقيقَةٌ واحِدَة — رَقمُ القَرار) لِكُلِّ حِساب، ويُمحى مِنها فَورَ الرَفعِ (<see cref="Forget"/>). وحِسابٌ لا وَثيقَةَ
/// لَه لا يُسقِطُ رَمزَه هُنا — ذاكَ سُلوكُ اليَومِ لِكُلِّ قارِئ، ولا يَتَغَيَّر. <b>وحَدُّه</b>: دائِرَةُ Blazor التَفاعُلِيَّةُ المَفتوحَةُ تَبقى حَتّى
/// تُعادَ الصَفحَة (الصَفَحاتُ المَحروسَةُ كُلُّها SSR ثابِت).</para>
/// </summary>
public static class SessionVersionGate
{
    /// <summary>مُدَّةُ حِفظِ الإصدارِ في الذاكِرَة — دَقيقَةٌ واحِدَة (قَرارُ المالِك د28).</summary>
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    private const string AuthCookiePrefix = ".acommerce.auth.";

    /// <summary>أَسقَطَ رَفعُ الإصدارِ هذا الرَمز؟ — نَقِيَّة: إصدارُ الرَمزِ أَصغَرُ مِن إصدارِ الحِسابِ المُخَزَّن. وحِسابٌ لا يُقرَأ
    /// (<c>null</c>) لا يُسقِطُ شَيئاً.</summary>
    public static bool IsRevoked(int tokenVersion, int? accountVersion)
        => accountVersion is int stored && tokenVersion < stored;

    /// <summary>أَسماءُ كوكيّاتِ الجَلسَةِ في الطَلَب — المَتجَرُ بِكُلِّ أَدوارِه والاستوديو، بِلا كوكيّاتِ الاسمِ (<c>.name</c>).</summary>
    /// <remarks>‏(‏2026-10-29، I-SEC-1) «كوكيُّ الاسمِ» قَرارُ <see cref="AuthSession.IsNameCookie"/> نَفسُه الَّذي يَسأَلُه الحَلّ — كانَ هُنا نَصّاً مُكَرَّراً.</remarks>
    public static IReadOnlyList<string> SessionCookieNames(IEnumerable<string> cookieNames)
        => cookieNames.Where(n => InFamily(n) && !AuthSession.IsNameCookie(n)).ToList();

    /// <summary>
    /// <para><b>ما تَفحَصُه البَوّابَة</b> (‏2026-10-29، تَدقيقُ الموجَة ١١ I-SEC-1؛ تَوصيفُه <c>cf0f9bc</c>): كوكيّاتُ الجَلسَةِ
    /// (<see cref="SessionCookieNames"/>) <b>وكُلُّ كوكيٍّ مِن العائِلَةِ يَحمِلُ رَمزاً يَقبَلُه <c>ParseToken</c> أَيّاً كانَ اسمُه</b> — فَكوكيُّ «الاسم»
    /// الَّذي زُرِعَ فيه رَمزٌ ساقِطٌ يُفحَصُ ويُنزَع، وكوكيُّ اسمِ العَرضِ الحَقيقيُّ (قيمَتُه اسمٌ لا رَمز) لا يُمَسّ. كانَ الفَحصُ بِالاسمِ وَحدَه، والحَلُّ
    /// يَقرَأُ ذلِكَ الاسمَ لِدَورٍ اسمُه <c>name</c>.</para>
    /// </summary>
    public static IReadOnlyList<string> CheckedCookieNames(IEnumerable<KeyValuePair<string, string>> cookies)
    {
        var all = cookies.ToList();
        return SessionCookieNames(all.Select(c => c.Key))
            .Concat(all.Where(c => InFamily(c.Key) && AuthSession.IsNameCookie(c.Key) && AuthHandlers.ParseToken(c.Value) is not null)
                       .Select(c => c.Key))
            .ToList();
    }

    /// <summary>عائِلَةُ كوكيّاتِ الدُخول: كوكيُّ الاستوديو بِعَينِه، وكُلُّ ما يَبدَأُ بِـ<c>.acommerce.auth.</c> — كَما كانَت.</summary>
    private static bool InFamily(string name)
        => name == StudioAuth.CookieName || name.StartsWith(AuthCookiePrefix, StringComparison.Ordinal);

    /// <summary>رَأسُ <c>Cookie</c> بِلا الأَسماءِ المُسقَطَة (وكوكيّاتِ أَسمائِها) — نَقِيَّة، فَتُقاسُ بِلا خادِم.</summary>
    public static string WithoutCookies(string header, IReadOnlyCollection<string> names)
    {
        var drop = names.SelectMany(n => new[] { n, n + ".name" }).ToHashSet(StringComparer.Ordinal);
        return string.Join("; ", header.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(pair => !drop.Contains(pair.Split('=', 2)[0].Trim())));
    }

    private static string Key(string tenant, Guid userId) => $"session-version|{tenant}|{userId:N}";

    /// <summary>إصدارُ الحِسابِ المُخَزَّن — مِن الذاكِرَةِ إن حُفِظَ خِلالَ <see cref="CacheFor"/>، وإلّا مِن وَثيقَتِه: <c>StudioUser</c> لِلاستوديو
    /// و<c>User</c> في جَلسَةِ مُستَأجِرِه. <c>null</c> لِحِسابٍ لا وَثيقَةَ لَه.</summary>
    public static async Task<int?> AccountVersionAsync(IDocumentStore store, IMemoryCache cache, string tenant, Guid userId)
    {
        if (cache.TryGetValue(Key(tenant, userId), out int? cached)) return cached;
        await using var q = store.QuerySession(tenant);
        int? version = tenant == StudioAuth.Tenant
            ? (await q.LoadAsync<StudioUser>(userId))?.SessionVersion
            : (await q.LoadAsync<User>(userId))?.SessionVersion;
        cache.Set(Key(tenant, userId), version, CacheFor);
        return version;
    }

    /// <summary>يَمحو الإصدارَ المَحفوظ — فَورَ الرَفع، فَلا يَبقى رَمزٌ مُسقَطٌ يَعبُرُ دَقيقَةَ الحِفظ.</summary>
    public static void Forget(IMemoryCache cache, string tenant, Guid userId) => cache.Remove(Key(tenant, userId));

    /// <summary>«اخرج من كل الأجهزة» لِحِسابِ مَتجَر — يَرفَعُ الإصدارَ ويَحفَظُه ويَمحو المَحفوظ. <c>false</c> = لا حِساب.</summary>
    public static async Task<bool> RevokeTenantAsync(IDocumentStore store, IMemoryCache cache, string slug, Guid userId)
    {
        await using var s = store.LightweightSession(slug);
        var user = await s.LoadAsync<User>(userId);
        if (user is null) return false;
        user.SessionVersion++;
        user.UpdatedAt = DateTime.UtcNow;
        s.Store(user);
        await s.SaveChangesAsync();
        Forget(cache, slug, userId);
        return true;
    }

    /// <summary>نَظيرُها لِحِسابِ الاستوديو — ومِنه مُشرِفُ المَنَصَّة.</summary>
    public static async Task<bool> RevokeStudioAsync(IDocumentStore store, IMemoryCache cache, Guid userId)
    {
        await using var s = store.LightweightSession(StudioAuth.Tenant);
        var user = await s.LoadAsync<StudioUser>(userId);
        if (user is null) return false;
        user.SessionVersion++;
        s.Store(user);
        await s.SaveChangesAsync();
        Forget(cache, StudioAuth.Tenant, userId);
        return true;
    }

    /// <summary>جِسمُ البَوّابَة: يَنزِعُ مِن الطَلَبِ كُلَّ كوكيِّ جَلسَةٍ أَسقَطَه رَفعُ الإصدار، ويَمحوه مِن المُتَصَفِّح.</summary>
    public static async Task ApplyAsync(HttpContext ctx)
    {
        var names = CheckedCookieNames(ctx.Request.Cookies);
        if (names.Count == 0) return;
        var store = ctx.RequestServices.GetRequiredService<IDocumentStore>();
        var cache = ctx.RequestServices.GetRequiredService<IMemoryCache>();
        var revoked = new List<string>();
        foreach (var name in names)
        {
            var token = ctx.Request.Cookies[name];
            if (AuthHandlers.ParseToken(token) is not { } parsed || AuthHandlers.SessionVersionOf(token) is not int version) continue;
            if (IsRevoked(version, await AccountVersionAsync(store, cache, parsed.TenantSlug, parsed.UserId))) revoked.Add(name);
        }
        if (revoked.Count == 0) return;
        ctx.Request.Headers.Cookie = WithoutCookies(ctx.Request.Headers.Cookie.ToString(), revoked);
        var opts = new CookieOptions { Path = "/", Secure = AuthSession.SecureFor(ctx.Request) };
        foreach (var name in revoked)
        {
            ctx.Response.Cookies.Delete(name, opts);
            ctx.Response.Cookies.Delete(name + ".name", opts);
        }
    }

    /// <summary>تُسَجَّلُ قَبلَ نِقاطِ القالِبِ وصَفَحاتِه (‏<c>Program.cs</c>) — فَكُلُّ قارِئٍ لِلجَلسَةِ بَعدَها يَرى الكوكيَّ المُسقَطَ غائِباً.</summary>
    public static IApplicationBuilder UseSessionVersionGate(this IApplicationBuilder app)
        => app.Use(async (ctx, next) => { await ApplyAsync(ctx); await next(ctx); });
}
