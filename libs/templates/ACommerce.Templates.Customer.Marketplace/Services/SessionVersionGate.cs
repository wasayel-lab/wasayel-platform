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
    /// <para><b>(‏2026-10-30، مُراجَعَةُ الموجَة ١١ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه؛ تَوصيفُه <c>4ef835a</c>) والعائِلَةُ لَم تَكُن تَكفي</b>: <c>Request.Cookies</c>
    /// يَبحَثُ بِالاسمِ بِلا حالَةِ أَحرُف، والعائِلَةُ تُقارِنُ بِحالَتِها — فَرَمزٌ ساقِطٌ تَحتَ <c>.ACOMMERCE.AUTH.{slug}</c> أَو <c>.ACOMMERCE.STUDIO</c> لا يُفحَصُ
    /// ويَقرَؤُه كُلُّ قارِئ. فَصارَ الشِقُّ الثاني <b>كُلَّ كوكيٍّ يَحمِلُ رَمزاً، مِن العائِلَةِ أَو مِن غَيرِها وبِأَيِّ حالَة</b>: الرَمزُ لا الاسمُ هُوَ ما يَفتَحُ
    /// الجَلسَة، والقيمَةُ الَّتي لَيسَت رَمزاً يَرُدُّها فَكُّ الترميزِ أَو عَدَدُ الحُقولِ أَو التَوقيعُ بِلا قِراءَةِ قاعِدَة. والعائِلَةُ وكوكيُّ الاسمِ بِمُقارَنَةِ القارِئِ نَفسِها (<c>OrdinalIgnoreCase</c>).</para>
    /// </summary>
    public static IReadOnlyList<string> CheckedCookieNames(IEnumerable<KeyValuePair<string, string>> cookies)
    {
        var all = cookies.ToList();
        var sessions = SessionCookieNames(all.Select(c => c.Key));
        return sessions
            .Concat(all.Where(c => !sessions.Contains(c.Key) && AuthHandlers.ParseToken(c.Value) is not null).Select(c => c.Key))
            .ToList();
    }

    /// <summary>عائِلَةُ كوكيّاتِ الدُخول: كوكيُّ الاستوديو بِعَينِه، وكُلُّ ما يَبدَأُ بِـ<c>.acommerce.auth.</c> — كَما كانَت، و(‏2026-10-30) بِلا حالَةِ أَحرُف
    /// كَما يَبحَثُ <c>Request.Cookies</c>.</summary>
    private static bool InFamily(string name)
        => name.Equals(StudioAuth.CookieName, StringComparison.OrdinalIgnoreCase)
           || name.StartsWith(AuthCookiePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>رَأسُ <c>Cookie</c> بِلا الأَسماءِ المُسقَطَة (وكوكيّاتِ أَسمائِها) — نَقِيَّة، فَتُقاسُ بِلا خادِم.</summary>
    /// <remarks>‏(‏2026-10-30، مُراجَعَةُ الموجَة ١١) <b>بِلا حالَةِ أَحرُف</b>: المُحَلِّلُ يُبقي آخِرَ المُكَرَّرِ بِحالَتَين، فَنَزعُ الاسمِ بِحالَتِه وَحدَها
    /// كانَ يُظهِرُ النُسخَةَ المَحجوبَةَ بِرَمزِها الساقِط.</remarks>
    public static string WithoutCookies(string header, IReadOnlyCollection<string> names)
    {
        var drop = DropSet(names);
        return string.Join("; ", Pairs(header).Where(pair => !drop.Contains(NameOf(pair))));
    }

    /// <summary>الأَسماءُ كَما كُتِبَت في الرَأسِ الَّتي يَنزِعُها <see cref="WithoutCookies"/> — لِيُمحى كُلٌّ مِنها مِن المُتَصَفِّحِ بِحالَتِه (والمُتَصَفِّحُ
    /// يُفَرِّقُ بَينَ الحالات). نَقِيَّة.</summary>
    public static IReadOnlyList<string> DroppedNames(string header, IReadOnlyCollection<string> names)
    {
        var drop = DropSet(names);
        return Pairs(header).Select(NameOf).Where(drop.Contains).Distinct(StringComparer.Ordinal).ToList();
    }

    private static HashSet<string> DropSet(IReadOnlyCollection<string> names)
        => names.SelectMany(n => new[] { n, n + ".name" }).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string[] Pairs(string header)
        => header.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string NameOf(string pair) => pair.Split('=', 2)[0].Trim();

    private static string Key(string tenant, Guid userId) => $"session-version|{tenant}|{userId:N}";

    /// <summary>
    /// <para><b>الإصدارُ الَّذي تُقارِنُ بِه البَوّابَةُ حِساباً في مَتجَر</b> — نَقِيَّة. <c>null</c> لِحِسابٍ لا وَثيقَةَ لَه (لا يُسقِطُ شَيئاً، كَما كان)؛
    /// <b>و<see cref="int.MaxValue"/> لِحِسابٍ مَحذوف</b> فَيَسقُطُ كُلُّ رَمزٍ لَه؛ وإلّا <c>SessionVersion</c>.</para>
    /// <para>(‏2026-10-29، تَدقيقُ الموجَة ١١ A4؛ تَوصيفُه <c>ca9e8b3</c>) كانَ الحَذفُ يُسقِطُ الرُموزَ بِرَفعِ الإصدارِ وَحدَه — وذاكَ مُنذُ <c>2e65882</c>
    /// (د28)؛ فَحِسابٌ حُذِفَ قَبلَه إصدارُه ‏0 ورَمزُه القَديمُ ‏0 لَيسَ أَصغَرَ مِنه، فَيَعبُرُ ثَلاثينَ يَوماً مِن إصدارِه. والحَذفُ حالَةٌ لا عَدَد:
    /// <c>DeletedAt</c> يَكفي، فَلا يَتَوَقَّفُ إسقاطُ رُموزِ المَحذوفِ عَلى رَفعٍ قَد يَكونُ فاتَه. <c>StudioUser</c> لا حَذفَ لَه اليَومَ (لا
    /// <c>DeletedAt</c> فيه)، فَإصدارُه كَما هُوَ.</para>
    /// </summary>
    public static int? GateVersion(User? account)
        => account is null ? null : account.DeletedAt is not null ? int.MaxValue : account.SessionVersion;

    /// <summary>إصدارُ الحِسابِ المُخَزَّن — مِن الذاكِرَةِ إن حُفِظَ خِلالَ <see cref="CacheFor"/>، وإلّا مِن وَثيقَتِه: <c>StudioUser</c> لِلاستوديو
    /// و<c>User</c> في جَلسَةِ مُستَأجِرِه بِـ<see cref="GateVersion"/>. <c>null</c> لِحِسابٍ لا وَثيقَةَ لَه.</summary>
    public static async Task<int?> AccountVersionAsync(IDocumentStore store, IMemoryCache cache, string tenant, Guid userId)
    {
        if (cache.TryGetValue(Key(tenant, userId), out int? cached)) return cached;
        await using var q = store.QuerySession(tenant);
        int? version = tenant == StudioAuth.Tenant
            ? (await q.LoadAsync<StudioUser>(userId))?.SessionVersion
            : GateVersion(await q.LoadAsync<User>(userId));
        cache.Set(Key(tenant, userId), version, CacheFor);
        return version;
    }

    /// <summary>يَمحو الإصدارَ المَحفوظ — فَورَ الرَفع، فَلا يَبقى رَمزٌ مُسقَطٌ يَعبُرُ دَقيقَةَ الحِفظ.</summary>
    public static void Forget(IMemoryCache cache, string tenant, Guid userId) => cache.Remove(Key(tenant, userId));

    /// <summary>
    /// <para><b>أَثَرُ «اخرج من كل الأجهزة» عَلى وَثيقَةِ حِسابِ المَتجَر</b> — نَقِيّ: يَرفَعُ الإصدارَ، <b>ويَمحو اشتِراكاتِ الإشعارِ كُلَّها</b>
    /// (‏2026-10-29، تَدقيقُ الموجَة ١١ I-SEC-2 + A5). كانَ يَرفَعُ الإصدارَ وَحدَه و<c>WebPushService</c> يُرسِلُ إلى كُلِّ اشتِراكٍ مَحفوظٍ بِلا جَلسَة —
    /// فَالهاتِفُ المَسروقُ يَبقى يَعرِضُ نَصَّ المُحادَثاتِ وإشعاراتِ الصَفَقاتِ عَلى شاشَتِه المُقفَلَة بِلا حَدّ (الاشتِراكُ لا يَنتَهي بِثَلاثينَ يَوماً).
    /// وكُلُّ جِهازٍ — هذا مِنها — يَشتَرِكُ مِن جَديدٍ بِنِداءِ <c>App.razor</c> القائِمِ بَعدَ دُخولِه التالي.</para>
    /// </summary>
    public static void Revoke(User user, DateTime now)
    {
        user.SessionVersion++;
        user.PushSubscriptions.Clear();
        user.UpdatedAt = now;
    }

    /// <summary>«اخرج من كل الأجهزة» لِحِسابِ مَتجَر — يُطَبِّقُ <see cref="Revoke"/> ويَحفَظُه ويَمحو المَحفوظ. <c>false</c> = لا حِساب.</summary>
    /// <remarks>‏(‏2026-10-29، G-1) بِـ<see cref="AccountWrites.UpdateAsync{T}"/>: كاتِبٌ سَبَقَه يُعادُ عَلَيه الرَفعُ مِن قِراءَةٍ جَديدَة، ولا يَكتُبُ
    /// أَحَدٌ فَوقَه نُسخَةً قُرِئَت قَبلَه.</remarks>
    public static async Task<bool> RevokeTenantAsync(IDocumentStore store, IMemoryCache cache, string slug, Guid userId)
    {
        var user = await AccountWrites.UpdateAsync<User>(store, slug, userId, u => { Revoke(u, DateTime.UtcNow); return true; });
        if (user is null) return false;
        Forget(cache, slug, userId);
        return true;
    }

    /// <summary>نَظيرُها لِحِسابِ الاستوديو — ومِنه مُشرِفُ المَنَصَّة.</summary>
    public static async Task<bool> RevokeStudioAsync(IDocumentStore store, IMemoryCache cache, Guid userId)
    {
        var user = await AccountWrites.UpdateAsync<StudioUser>(store, StudioAuth.Tenant, userId, u => { u.SessionVersion++; return true; });
        if (user is null) return false;
        Forget(cache, StudioAuth.Tenant, userId);
        return true;
    }

    /// <summary>جِسمُ البَوّابَة: يَنزِعُ مِن الطَلَبِ كُلَّ كوكيِّ جَلسَةٍ أَسقَطَه رَفعُ الإصدار، ويَمحوه مِن المُتَصَفِّح.</summary>
    /// <remarks>
    /// <para>‏(‏2026-10-30، مُراجَعَةُ الموجَة ١١ — I-SEC-1 الجَولَةُ الأُولى؛ تَوصيفُه <c>4ef835a</c>) <b>والنَتيجَةُ تُقاسُ بِمُحَلِّلِ القارِئِ نَفسِه</b>: بَعدَ
    /// كُلِّ نَزعٍ يُعادُ الفَحصُ عَلى <c>Request.Cookies</c> المُعادِ تَحليلُه — فَإن بَقِيَ رَمزٌ ساقِطٌ يَراه القارِئ (نُسخَةٌ كانَت مَحجوبَة، أَو رَأسٌ لا يُطابِقُ
    /// تَقسيمُه تَحليلَه) ولَم يُغَيِّر النَزعُ شَيئاً أَو بَلَغَ <see cref="Passes"/>، <b>يُمحى رَأسُ <c>Cookie</c> كُلُّه</b> فَيَصيرُ الطَلَبُ مَجهولاً. الشَرطُ
    /// «لا رَمزَ ساقِطاً بَعدَ البَوّابَةِ عِندَ أَيِّ قارِئ» يُفحَصُ بِالبِناءِ لا بِحُجَّةٍ عَن تَطابُقِ مُحَلِّلَين.</para>
    /// </remarks>
    public static async Task ApplyAsync(HttpContext ctx)
    {
        if (CheckedCookieNames(ctx.Request.Cookies).Count == 0) return;
        var store = ctx.RequestServices.GetRequiredService<IDocumentStore>();
        var cache = ctx.RequestServices.GetRequiredService<IMemoryCache>();
        var deleted = new List<string>();
        for (var pass = 1; ; pass++)
        {
            var revoked = await RevokedNamesAsync(ctx.Request.Cookies, store, cache);
            if (revoked.Count == 0) break;
            var header = ctx.Request.Headers.Cookie.ToString();
            var stripped = WithoutCookies(header, revoked);
            deleted.AddRange(revoked.SelectMany(n => new[] { n, n + ".name" }).Concat(DroppedNames(header, revoked)));
            if (pass >= Passes || stripped == header) { ctx.Request.Headers.Cookie = string.Empty; break; }
            ctx.Request.Headers.Cookie = stripped;
        }
        if (deleted.Count == 0) return;
        var opts = new CookieOptions { Path = "/", Secure = AuthSession.SecureFor(ctx.Request) };
        foreach (var name in deleted.Distinct(StringComparer.Ordinal))
            ctx.Response.Cookies.Delete(name, opts);
    }

    /// <summary>عَدَدُ مَرّاتِ النَزعِ قَبلَ مَحوِ الرَأسِ كُلِّه — نَزعٌ ثُمَّ فَحصٌ يَكفيانِ لِكُلِّ رَأسٍ مَقيس، والثالِثَةُ لا تَصِلُ إلّا بِتَحليلٍ لا يُطابِقُه التَقسيم.</summary>
    public const int Passes = 3;

    /// <summary>ما أَسقَطَه رَفعُ الإصدارِ مِن <see cref="CheckedCookieNames"/> — بِالرَمزِ الَّذي يَقرَؤُه القارِئُ تَحتَ ذلِكَ الاسم.</summary>
    private static async Task<List<string>> RevokedNamesAsync(IRequestCookieCollection cookies, IDocumentStore store, IMemoryCache cache)
    {
        var revoked = new List<string>();
        foreach (var name in CheckedCookieNames(cookies))
        {
            var token = cookies[name];
            if (AuthHandlers.ParseToken(token) is not { } parsed || AuthHandlers.SessionVersionOf(token) is not int version) continue;
            if (IsRevoked(version, await AccountVersionAsync(store, cache, parsed.TenantSlug, parsed.UserId))) revoked.Add(name);
        }
        return revoked;
    }

    /// <summary>تُسَجَّلُ قَبلَ نِقاطِ القالِبِ وصَفَحاتِه (‏<c>Program.cs</c>) — فَكُلُّ قارِئٍ لِلجَلسَةِ بَعدَها يَرى الكوكيَّ المُسقَطَ غائِباً.</summary>
    public static IApplicationBuilder UseSessionVersionGate(this IApplicationBuilder app)
        => app.Use(async (ctx, next) => { await ApplyAsync(ctx); await next(ctx); });
}
