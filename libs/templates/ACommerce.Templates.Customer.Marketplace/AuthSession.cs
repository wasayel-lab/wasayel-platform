using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace;

/// <summary>
/// حالَة المُستَخدِم في الـ Blazor circuit. تُحَمَّل مِن cookie في كُلّ
/// طَلَب SSR. كَتابَة الـ cookie تَجري في endpoints الـ SSR forms.
///
/// <para>دَعم تَطبيقات فَرعيَّة (دَور لِكُلّ تَبويب): الـ cookie يَأخُذ
/// اسماً يَشمَل الدَور <c>.acommerce.auth.{slug}.{role}</c>، فَكُلّ دَور
/// لَه session مُستَقِلّ في نَفس المُتَصَفِّح. الـ Path يَبقَى <c>/</c>
/// (المُتَصَفِّح يُرسِل كُلّ الـ cookies)، لكِنّ AuthSession يَختار أَيُّها
/// يَقرَأ بِناءً عَلى الدَور المُستَخرَج مِن URL (نَمَط <c>/{slug}/r/{role}/…</c>).
/// إن كانَ المَسار بِلا <c>/r/{role}/</c> — وَهُوَ حالُ كُلّ نِقاط الـ POST
/// تَقريباً — يُقبَل الـ cookie العامّ <c>.acommerce.auth.{slug}</c>
/// (مَتاجِر ashare/ejar) ثُمَّ أَيّ cookie دَور لِنَفس المَتجَر.
/// التَّفصيل وَالمُبَرِّر في <see cref="ResolveCookieBase"/>.</para>
/// </summary>
public sealed class AuthSession
{
    public Guid? UserId { get; private set; }
    public string? UserName { get; private set; }
    public string? Token { get; private set; }
    public string? TenantSlug { get; private set; }
    public string? RoleScope { get; private set; }    // الدَور النَّشِط لِهذا الطَلَب
    public bool IsAuthenticated => UserId.HasValue;

    public event Action? Changed;

    public void Load(HttpContext http, string requiredTenantSlug)
    {
        RoleScope = ExtractRoleFromPath(http.Request.Path);
        var name = ResolveCookieBase(http.Request, requiredTenantSlug);
        if (name is null) { Clear(); return; }
        var token = CookieValue(http.Request, name);
        var parsed = AuthHandlers.ParseToken(token);
        if (parsed is null) { Clear(); return; }
        var (uid, slug, _) = parsed.Value;
        UserId = uid; TenantSlug = slug; Token = token;
        UserName = CookieValue(http.Request, name + ".name") ?? "—";
        Changed?.Invoke();
    }

    public void SetTenant(string slug) => TenantSlug = slug;
    public void Clear() { UserId = null; UserName = null; Token = null; }

    // ─── اسم الـ cookie ──────────────────────────────────────────────
    public static string CookieName(string tenantSlug)
        => $".acommerce.auth.{tenantSlug}";

    public static string CookieName(string tenantSlug, string? role)
        => string.IsNullOrEmpty(role)
            ? CookieName(tenantSlug)
            : $".acommerce.auth.{tenantSlug}.{role}";

    /// <summary><b>اسمُ كوكيِّ الجَلسَةِ كَما يُكتَبُ ويُقرَأُ — عَلى طَلَبٍ آمِنٍ أَو غَيرِه</b> (‏2026-11-07، ADR-050 §٥ · W13-4·C2c·2): القَرارُ الواحِدُ
    /// الَّذي يَسأَلُه كُلُّ كاتِبٍ وقارِئٍ لِكوكيِّ المَتجَر (<see cref="ResolveCookieBase"/> و<see cref="WriteCookie"/> و<see cref="UpdateNameCookie"/>
    /// و<see cref="ClearCookie"/> و<c>EffectiveRole.OwnsRole</c>) — فَلا يَختَلِفُ الكاتِبُ والقارِئُ في اسم. ومَعَه
    /// <c>StudioAuth.CookieNameFor</c> لِكوكيِّ الاستوديو.</summary>
    /// <remarks>‏(‏2026-11-07، ADR-050 §٥ · W13-4·C2c·2 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>وعَلى طَلَبٍ آمِنٍ بِالبادِئَةِ <see cref="HostPrefix"/></b>: يَفرِضُ
    /// المُتَصَفِّحُ عَلى <c>__Host-</c> أَن يَكونَ <c>Secure</c> بِـ<c>Path=/</c> وبِلا <c>Domain</c> — فَلا يَستَطيعُ مُضيفٌ شَقيقٌ تَحتَ النِطاقِ (مَتجَرٌ آخَر، أَو سِجِلٌّ
    /// لِطَرَفٍ ثالِث) زَرعَ كوكيِّ جَلسَةٍ بِـ<c>Domain=R</c> يَقرَؤُه هذا المُضيف (تَثبيتُ جَلسَة). والتَطويرُ عَلى HTTP بِالأَسماءِ القائِمَة. والقارِئُ لا يَقرَأُ
    /// الاسمَ القَديمَ عَلى HTTPS — فَالجَلَساتُ القائِمَةُ تَخرُجُ مَرَّةً واحِدَةً بِالنَشر (ADR-050 §١٥).</remarks>
    public static string CookieNameFor(bool secure, string tenantSlug, string? role = null)
        => secure ? HostPrefix + CookieName(tenantSlug, role) : CookieName(tenantSlug, role);

    /// <summary>بادِئَةُ كوكيِّ المُضيفِ (RFC 6265bis §4.1.3.2): <c>Secure</c> و<c>Path=/</c> وبِلا <c>Domain</c> — يَفرِضُها المُتَصَفِّح.</summary>
    public const string HostPrefix = "__Host-";

    /// <summary>
    /// <para><b>قيمَةُ كوكيٍّ بِاسمِه — والاسمُ بِالبادِئَةِ يُقرَأُ بِحالَةِ أَحرُفِه مِن رَأسِ الطَلَبِ نَفسِه</b> (‏2026-11-07، W13-4·C2c·2): <c>Request.Cookies</c> يَبحَثُ
    /// بِلا حالَةِ أَحرُفٍ ويُبقي آخِرَ المُكَرَّر — ومُتَصَفِّحٌ يَفحَصُ البادِئَةَ بِحالَتِها يَقبَلُ <c>__HOST-…; Domain=R</c> مِن مُضيفٍ شَقيق، فَيَغلِبُ في القامُوسِ
    /// كوكيَّ المُضيفِ الحَقيقيّ. فَالاسمُ <c>__Host-…</c> لا يُقرَأُ إلّا بِحُروفِه، والأَوَّلُ في الرَأس؛ وما سِواه كَما كان.</para>
    /// </summary>
    public static string? CookieValue(HttpRequest req, string name)
    {
        if (!name.StartsWith(HostPrefix, StringComparison.Ordinal)) return req.Cookies[name];
        foreach (var (n, v) in HeaderCookies(req))
            if (string.Equals(n, name, StringComparison.Ordinal)) return v;
        return null;
    }

    /// <summary>أَزواجُ رَأسِ <c>Cookie</c> بِتَرتيبِها وحالَةِ أَحرُفِها — القيمَةُ مَفكوكَةُ التَرميزِ كَما يَفُكُّها <c>Request.Cookies</c>.</summary>
    /// <remarks>‏(‏2026-11-07، مُراجَعَةُ C2c الجَولَةُ الأُولى · SEC-C2c-1 وC2c-P-01 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>«كَما يَفُكُّها» لَم تَصدُق، والبَوّابَةُ لَم تَكُن تَرى
    /// ما يُقرَأُ مِن هُنا</b>: كانَ يَنزِعُ عَلامَتَي التَنصيصِ والقامُوسُ يُبقيهِما (مَقيس: <c>__Host-x="QUJD"</c> ⇒ القامُوسُ <c>"QUJD"</c>)، و<c>SessionVersionGate</c>
    /// تَحكُمُ بِالقامُوسِ وَحدَه (آخِرُ المُكَرَّرِ بِلا حالَةِ أَحرُف) و<see cref="CookieValue"/> يَقرَأُ أَوَّلَ زَوجٍ مِن هُنا — فَرَمزٌ أَسقَطَه «اخرج من كل الأجهزة» أَو حَذفُ الحِسابِ
    /// كانَ يُفتَحُ عَلى HTTPS بِمُكَرَّرٍ تالِفٍ بَعدَه أَو بَينَ عَلامَتَين. <b>والآن</b>: الفَكُّ فَكُّ القامُوسِ بِحَرفِه (<see cref="Uri.UnescapeDataString"/> وَحدَه، والتَنصيصُ يَبقى
    /// فَلا يُقرَأُ رَمزاً — والكاتِبُ لا يَكتُبُه: <c>Append</c> يُرَمِّزُ <c>"</c>)، و<c>internal</c> لِتَحكُمَ البَوّابَةُ عَلى كُلِّ زَوجٍ مِنه (<c>SessionVersionGate.ReadablePairs</c>)
    /// — فَما يَقرَؤُه قارِئٌ قيمَةُ زَوجٍ حُكِمَ عَلَيه، بِالبِناء. مَحروسٌ في <c>SessionGateReadsWhatTheReaderReadsTests</c>.</remarks>
    internal static IEnumerable<(string Name, string Value)> HeaderCookies(HttpRequest req)
    {
        foreach (var header in req.Headers.Cookie)
            foreach (var part in (header ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0) continue;
                yield return (part[..eq].Trim(), Uri.UnescapeDataString(part[(eq + 1)..].Trim()));
            }
    }

    /// <summary>الاسمُ عَلى هذا الطَلَب — <see cref="CookieNameFor"/> بِقَرارِ <see cref="SecureFor"/> نَفسِه الَّذي يَضبِطُ <c>Secure</c>.</summary>
    public static string CookieNameOn(HttpRequest? request, string tenantSlug, string? role = null)
        => CookieNameFor(SecureFor(request), tenantSlug, role);

    // ─── حَلّ الجَلسَة: القاعِدَة الواحِدَة ───────────────────────────────
    //
    // كُلّ قارِئ لِلجَلسَة (AuthSession.Load، AuthFilter، وكُلّ form endpoint)
    // يَمُرّ مِن هُنا — لا يَقرَأ أَحَدٌ اسم cookie بِيَدِه. كانَ العَكس هُوَ
    // العَيب: عَشَرات النِّقاط تَقرَأ الاسم العامّ <c>.acommerce.auth.{slug}</c>
    // حَرفيّاً، بَينَما الدُخول عَبر <c>/{slug}/r/{role}/login</c> يَكتُب
    // <c>.acommerce.auth.{slug}.{role}</c> فَقَط — فَكُلّ POST عَلى مَسار بِلا
    // <c>/r/{role}/</c> (‏listings/create، ‏listings/{id}/offers، ‏cart،
    // ‏checkout، ‏deals…) يَراهُ المُستَخدِم مُسَجَّلاً وَيَراهُ الخادِم
    // مَجهولاً، فَيُعيد توجيهاً لِـ login أَو يَفشَل صامِتاً.
    //
    // القاعِدَة:
    //   • مَسار بِدَور  → cookie ذلكَ الدَور وَحدَه، بِلا سُقوط. (عَزل الأَدوار
    //     يَبقَى كَما صُمِّم: جَلسَة راكِب في تَبويب وَسائِق في آخَر.)
    //   • مَسار بِلا دَور → الـ cookie العامّ أَوَّلاً (تَوافُق مَع المَتاجِر
    //     أُحادِيَّة الدَور ashare/ejar)، ثُمَّ أَيّ cookie دَور لِنَفس المَتجَر
    //     يَحمِلُه المُتَصَفِّح أَصلاً.
    //
    // لِماذا التَّوسيع آمِن: الـ token لا يَحمِل دَوراً إطلاقاً — هُوَ
    // <c>{userId}|{tenantSlug}|{exp}|{sig}</c> (‏AuthHandlers.MakeToken). فَكُلّ
    // cookies الأَدوار لِنَفس المُستَخدِم مُتَكافِئَة هُوِيَّةً، وَقُبولُ أَيِّها
    // عَلى مَسار بِلا دَور لا يَمنَح صَلاحِيَّةً جَديدَة: التَّفويض يَجري بَعدَها
    // عَبر <c>RolePermissions.Has(tenant.Roles, user.ActiveRole, …)</c>. وَما
    // يُفحَص هُنا هِيَ cookies المُتَصَفِّح نَفسِه — لا يَستَطيع طَرَف ثالِث
    // زَرعَ جَلسَة غَيرِه فيها.

    /// <summary>اسم الـ cookie الَّذي يَحمِل جَلسَةً صالِحَة لِهذا المَتجَر في
    /// هذا الطَّلَب، أَو <c>null</c>. الاسم — لا القيمَة — لِيَقرَأ المُتَّصِل
    /// الـ token وَ<c>.name</c> مِن نَفس العائِلَة فَلا يَختَلِط اسمُ دَور
    /// بِتوكِن دَور آخَر.</summary>
    public static string? ResolveCookieBase(HttpRequest req, string tenantSlug)
    {
        var role = ExtractRoleFromPath(req.Path);
        if (role is not null)
        {
            var scoped = CookieNameOn(req, tenantSlug, role);
            // ‏(‏2026-10-29، تَدقيقُ الموجَة ١١ I-SEC-1؛ تَوصيفُه `cf0f9bc`) دَورٌ اسمُه `name` (أَو يَنتَهي بِـ`.name`) يُسَمّي كوكيَّ اسمِ العَرض —
            // كانَ يُقرَأُ جَلسَةً وتَتَخَطّاه البَوّابَة، فَرَمزٌ ساقِطٌ فيه يَفتَحُ صَفَحاتِ `/r/name/`. القَرارُ الواحِدُ `IsNameCookie`.
            if (IsNameCookie(scoped)) return null;
            return IsValidFor(CookieValue(req, scoped), tenantSlug) ? scoped : null;
        }

        var generic = CookieNameOn(req, tenantSlug);
        if (IsValidFor(CookieValue(req, generic), tenantSlug)) return generic;

        var prefix = generic + ".";
        // ‏(‏2026-11-07، W13-4·C2c·2) وبِالبادِئَةِ مِن الرَأسِ بِحُروفِه (`CookieValue`) — القامُوسُ بِلا حالَةِ أَحرُفٍ يُبقي آخِرَ المُكَرَّر.
        var cookies = generic.StartsWith(HostPrefix, StringComparison.Ordinal)
            ? HeaderCookies(req)
            : req.Cookies.Select(c => (c.Key, c.Value));
        foreach (var (cookieName, value) in cookies)
        {
            if (!cookieName.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (IsNameCookie(cookieName)) continue;
            if (IsValidFor(value, tenantSlug)) return cookieName;
        }
        return null;
    }

    /// <summary><b>كوكيُّ اسمِ العَرضِ لا كوكيُّ جَلسَة</b> (‏2026-10-29، تَدقيقُ الموجَة ١١ I-SEC-1) — الاسمُ يَنتَهي بِـ<c>.name</c>. نَقِيَّة،
    /// ويَسأَلُها الحَلُّ هُنا (لا يُقرَأُ جَلسَةً عَلى مَسارٍ ولا في السُقوط) و<c>SessionVersionGate.SessionCookieNames</c> — فَلا يَختَلِفانِ أَبَداً
    /// في أَيِّ الأَسماءِ جَلسَة.</summary>
    /// <remarks>‏(‏2026-10-30، مُراجَعَةُ الموجَة ١١ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه؛ تَوصيفُه <c>4ef835a</c>) «لا يَختَلِفانِ أَبَداً» لَم تَصدُق: القارِئُ يَبحَثُ
    /// بِـ<c>Request.Cookies</c> بِلا حالَةِ أَحرُف (<c>Cookies[name + ".name"]</c> يُجيبُ <c>.NAME</c>) والقَرارُ كانَ بِحالَتِها. صارَ بِمُقارَنَةِ القارِئِ
    /// (<c>OrdinalIgnoreCase</c>)، والبَوّابَةُ لا تَتَّكِلُ عَلَيه وَحدَه: تَفحَصُ كُلَّ كوكيٍّ يَحمِلُ رَمزاً أَيّاً كانَ اسمُه (<c>SessionVersionGate.CheckedCookieNames</c>).</remarks>
    public static bool IsNameCookie(string cookieName) => cookieName.EndsWith(".name", StringComparison.OrdinalIgnoreCase);

    /// <summary>الـ token الصالِح لِهذا الطَّلَب، أَو <c>null</c>.</summary>
    public static string? ResolveToken(HttpRequest req, string tenantSlug)
    {
        var name = ResolveCookieBase(req, tenantSlug);
        return name is null ? null : CookieValue(req, name);
    }

    // ‏(‏2026-10-24، تَدقيقُ الإطلاق lens-security-11) كانَ هُنا `ResolveUserName` — يَقرَأُ `{cookie}.name` خارِجَ رَمزِ HMAC فَتَكتُبُه
    // أَربَعَ عَشرَةَ نُقطَةً اسماً لِلفاعِل. صارَ الاسمُ مِن الوَثيقَة (`AccountQueries.ActorNameAsync`) فَحُذِفَت بِصِفرِ مُنادٍ.

    private static bool IsValidFor(string? token, string tenantSlug)
    {
        var parsed = AuthHandlers.ParseToken(token);
        return parsed is not null && parsed.Value.TenantSlug == tenantSlug;
    }

    /// <summary>يَستَخرِج الدَور مِن <c>/{slug}/r/{role}/…</c>. <c>null</c>
    /// إن كانَ المَسار قَديماً (بِلا <c>/r/</c>).</summary>
    public static string? ExtractRoleFromPath(PathString path)
    {
        var s = path.Value;
        if (string.IsNullOrEmpty(s)) return null;
        var parts = s.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // parts[0] = slug, parts[1] = "r", parts[2] = role
        if (parts.Length >= 3 && parts[1].Equals("r", StringComparison.OrdinalIgnoreCase))
            return parts[2].ToLowerInvariant();
        return null;
    }

    /// <summary>يُكتَب مِن SSR endpoint بَعد نَجاح المُصادَقَة. حِنَّ يُعطَى
    /// <paramref name="role"/>، يُكتَب cookie role-scoped؛ بِلا role يُكتَب
    /// الـ cookie القَديم (لِـ ashare/ejar).</summary>
    /// <summary>الـ HttpContext الحاليّ — يُستَخدَم لِكَشف هَل الطَّلَب على
    /// HTTPS فَنَضَع Secure تِلقائيّاً. يُحقَن مَرَّةً في Program عَبر
    /// IHttpContextAccessor.</summary>
    public static IHttpContextAccessor? HttpAccessor { get; set; }

    /// <remarks>هَل نَضَع الـ Secure flag؟ القاعِدَة الذَّكِيَّة: نَعَم فَقَط
    /// لَو الطَّلَب الحاليّ HTTPS فِعليّاً. هكذا يَعمَل HTTP المَحَلّيّ
    /// (تَطوير) تِلقائيّاً بِلا ENV، ويَبقى آمِناً خَلف HTTPS (إنتاج).
    /// كانَ الافتِراضيّ Secure=true دائِماً، فَكَسَرَ تَسجيل الدُّخول على
    /// HTTP المَحَلّيّ صامِتاً (المُتَصَفِّح يَرفُض cookie آمِناً على http).
    /// مِفتاح <c>ACOMMERCE_FORCE_INSECURE_COOKIES=1</c> يُجبِر الإطفاء.
    /// ‏(‏2026-11-07، W13-4·C2c·2 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) كانَ هذا شَرحَ <c>ShouldUseSecure</c> (القَرارُ بِطَلَبِ الوَصّالِ الساكِن)، وصارَ الكاتِبُ
    /// والماحي يَسأَلانِ <see cref="SecureFor"/> بِطَلَبِ رَدِّهِما — الطَلَبُ نَفسُه الَّذي يُقَرِّرُ الاسمَ (<see cref="CookieNameOn"/>)، فَلا يُكتَبُ <c>__Host-</c>
    /// بِلا <c>Secure</c> فَيَرُدَّه المُتَصَفِّح. فَحُذِفَت بِصِفرِ مُنادٍ.</remarks>

    /// <summary><b>القَرارُ نَفسُه لِطَلَبٍ بِعَينِه</b> (‏2026-10-24، تَدقيقُ الإطلاق lens-security-6): كوكي الاستوديو — هُوِيَّةُ مُشرِفِ المَنَصَّة —
    /// كانَ يُصدَرُ بِلا <c>Secure</c> لِأَنّ كاتِبَه لا يَسأَلُ هذا القَرار. صارَ يَسأَلُه بِطَلَبِ رَدِّه (<c>StudioAuth.WriteCookie</c>)، وكوكي
    /// المَتجَرِ بِالطَلَبِ الحاليّ كَما كان. مَوضِعٌ واحِدٌ لِلقاعِدَة: HTTPS فِعلاً، إلّا بِـ<c>ACOMMERCE_FORCE_INSECURE_COOKIES=1</c>.</summary>
    public static bool SecureFor(HttpRequest? request)
    {
        if (Environment.GetEnvironmentVariable("ACOMMERCE_FORCE_INSECURE_COOKIES") == "1")
            return false;
        return request?.IsHttps ?? false;
    }

    private static CookieOptions BuildOpts(HttpResponse res) => new()
    {
        HttpOnly    = true,
        IsEssential = true,
        Expires     = DateTimeOffset.UtcNow.AddDays(30),
        SameSite    = SameSiteMode.Lax,
        Secure      = SecureFor(res.HttpContext.Request),
        Path        = "/"
    };

    public static void WriteCookie(HttpResponse res, string tenantSlug, AuthResult auth,
                                    string? role = null)
    {
        var opts = BuildOpts(res);
        var name = CookieNameOn(res.HttpContext.Request, tenantSlug, role);
        res.Cookies.Append(name, auth.Token, opts);
        res.Cookies.Append(name + ".name", auth.FullName, opts);

        // ونُسخَة بِالاسم العامّ كَذلك. الدُخول بِدَور كانَ يَكتُب cookie الدَور
        // وَحدَه، فَيَبقَى كُلّ مَسار بِلا /r/{role}/ بِلا جَلسَة. الكِتابَة هُنا
        // تَجعَل الحَلّ حَتمِيّاً (بِلا مَسح cookies) لِكُلّ دُخول جَديد، وَلا
        // تَمَسّ عَزل الأَدوار: المَسارات المُسَوَّرَة بِدَور لا تَقرَأ العامّ
        // أَبَداً، وَالـ token بِلا دَور فَلا هُوِيَّةَ جَديدَة تُمنَح.
        // وَ ClearAllCookiesForTenant يَمسَح العامّ وَكُلّ الأَدوار مَعاً.
        if (!string.IsNullOrEmpty(role))
        {
            var generic = CookieNameOn(res.HttpContext.Request, tenantSlug);
            res.Cookies.Append(generic, auth.Token, opts);
            res.Cookies.Append(generic + ".name", auth.FullName, opts);
        }
    }

    public static void UpdateNameCookie(HttpResponse res, string tenantSlug, string newName,
                                         string? role = null)
    {
        var opts = BuildOpts(res);
        res.Cookies.Append(CookieNameOn(res.HttpContext.Request, tenantSlug, role) + ".name", newName, opts);
        if (!string.IsNullOrEmpty(role))
            res.Cookies.Append(CookieNameOn(res.HttpContext.Request, tenantSlug) + ".name", newName, opts);
    }

    public static void ClearCookie(HttpResponse res, string tenantSlug, string? role = null)
    {
        var opts = new CookieOptions { Path = "/", Secure = SecureFor(res.HttpContext.Request) };
        var name = CookieNameOn(res.HttpContext.Request, tenantSlug, role);
        res.Cookies.Delete(name, opts);
        res.Cookies.Delete(name + ".name", opts);
    }

    /// <summary>يَمسَح كُلّ cookies المُستَخدِم لِكُلّ الأَدوار المُحتَمَلَة في
    /// هذا المَتجَر — يَستَخدِمها endpoint الـ logout. كانَ يَمسَح cookie
    /// واحِد فَقَط فَيَتَسَرَّب جَلسَة /r/{role}/.</summary>
    public static void ClearAllCookiesForTenant(HttpResponse res, string tenantSlug,
                                                 IEnumerable<string> roleSlugs)
    {
        ClearCookie(res, tenantSlug, role: null);
        foreach (var r in roleSlugs) ClearCookie(res, tenantSlug, r);
    }

    /// <summary><b>أَدوارُ المَتجَرِ الَّتي يَحمِلُ هذا المُتَصَفِّحُ كوكيَّها</b> — نَقِيَّة (‏2026-11-05، الموجَة ١٢ G2 · ٢): كُلُّ اسمٍ بَعدَ <c>{CookieName(slug)}.</c>،
    /// بِلا لاحِقَةِ كوكيِّ الاسمِ (<see cref="IsNameCookie"/>) وبِلا حالَةِ أَحرُفٍ كَما يَبحَثُ القارِئ. يَسأَلُها بابُ الخُروجِ حينَ لا تُقرَأُ أَدوارُ المَتجَرِ
    /// مِن القاعِدَة، فَيُمحى ما يَحمِلُه المُتَصَفِّحُ فِعلاً بِـ<see cref="ClearAllCookiesForTenant"/>.</summary>
    public static IReadOnlyList<string> RoleCookiesIn(IEnumerable<string> cookieNames, string tenantSlug)
    {
        var prefix = CookieName(tenantSlug) + ".";
        return cookieNames
            .Select(n => n.StartsWith(HostPrefix, StringComparison.Ordinal) ? n[HostPrefix.Length..] : n)   // ‏(‏2026-11-07، W13-4·C2c·2) والاسمُ بِالبادِئَة
            .Select(n => IsNameCookie(n) ? n[..^".name".Length] : n)
            .Where(n => n.Length > prefix.Length && n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(n => n[prefix.Length..])
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>يَبني URL مُنبَثِق مِن tenant slug + role اختياريّ.
    /// <para>‏(‏2026-11-07، ADR-050 §٩ المَرحَلَةُ الثانِيَة · W13-4·C2b·1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>عارِفٌ بِمُضيفِ المَتجَرِ عَبرَ <see cref="HttpAccessor"/></b>:
    /// عَلى <c>{slug}.R</c> نَظيف (<c>/explore</c>، <c>/r/{role}/…</c>)، وما سِواه بِشَكلِ المَسارِ بِحَرفِه — الجِسمُ القَديمُ هُوَ
    /// <c>Services.Ux.StoreUrl.PathForm</c> نَفسُه، وتَوصيفُه <c>StoreLinkCharacterizationTests</c>. مِلَفٌّ واحِدٌ يَتَغَيَّرُ ومَواضِعُ استِدعائِه تَتبَعُه.</para></summary>
    /// <remarks>‏(‏2026-11-07، ADR-050 §٤ المَرحَلَةُ الثالِثَة · W13-4·C2b·4 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>ورابِطُ تَطبيقٍ آخَرَ مِن المَتجَرِ نَفسِه</b> (دَورٌ
    /// غَيرُ دَورِ المُضيف) يُسَمّي مُضيفَ ذلكَ التَطبيقِ مُطلَقاً — <c>Services.Ux.StoreUrl.Link</c>؛ وفي وَضعِ المَسارِ بِحَرفِه.</remarks>
    public static string LinkFor(string tenantSlug, string? role, string path)
        => Services.Ux.StoreUrl.Link(Services.Ux.StoreUrl.ViewOf(HttpAccessor?.HttpContext), tenantSlug, role, path);
}
