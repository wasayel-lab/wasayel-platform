using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>نَتيجَة قَرار مُشرِف المَنصَّة — ثَلاث حالات لا اثنَتان، لِأَنّ
/// الواجِهَة تُفَرِّق بَينَ «لَم تُسَجِّل دُخولاً» (تَعرِض زِرّ الدُخول)
/// وَ«سَجَّلتَ وَلَستَ مُشرِفاً» (تَعرِض المَنع). طَرَف الكِتابَة يَطوي
/// الحالَتَين في <c>403</c> واحِدَة.</summary>
public enum PlatformAdminOutcome
{
    /// <summary>لا جَلسَة studio أَصلاً.</summary>
    Anonymous,
    /// <summary>جَلسَة صالِحَة، لَكِنّ صاحِبَها لَيسَ مُشرِف مَنصَّة.</summary>
    NotAdmin,
    /// <summary>مُشرِف مَنصَّة مُؤَكَّد.</summary>
    Allowed
}

/// <summary>القَرار مَع صاحِبِه — نِقاط الـ POST تَحتاج الـ
/// <see cref="StudioUser"/> بَعدَ القَبول لِتَكتُب سَطر الـ audit بِاسمِه،
/// فَلا تَستَعلِم عَنه مَرَّةً ثانِيَة.</summary>
public readonly record struct PlatformAdminDecision(
    PlatformAdminOutcome Outcome, StudioUser? User)
{
    public bool Allowed => Outcome == PlatformAdminOutcome.Allowed;
}

/// <summary>
/// بَوّابَة إدارَة المَنصَّة — <b>التَّعريف الوَحيد</b> لِقَرار «هذا مُشرِف
/// مَنصَّة» في المُستَودَع. تَقرَؤُه صَفَحات <c>/admin</c> على مُستَوى
/// المَنصَّة (عَبر <c>RequirePlatformAdmin</c>) وَنِقاط الـ POST
/// المُقابِلَة مَعاً.
///
/// <para><b>الطَّبَقَة الثانِيَة، لا بَديل عَن الأولى:</b> هذه البَوّابَة
/// لِلإجراءات الَّتي تَملِك المَنصَّة كُلَّها — إنشاء مَتجَر، تَعليقُه،
/// الحاضِنَة، الوَكيل الإداريّ. أَمّا إدارَة مَتجَر بِعَينِه فَلَها
/// <see cref="TenantAdminGuard"/>، وَهُما لا يَتَداخَلان.</para>
///
/// <para><b>لِماذا وُجِدَت:</b> كانَ القَرار مَكتوباً مَرَّتَين — مَرَّة في
/// <c>RequirePlatformAdmin.razor</c> وَمَرَّة داخِل نُقطَة
/// <c>/admin/tenants/{slug}/suspend</c> — وَغائِباً عَن كُلّ ما عَداهُما.
/// فَكانَت خَمس صَفَحات مَنصَّة تُصَيَّر كامِلَةً لِطَلَب مَجهول (مِنها
/// <c>/admin/agent</c> بِسِجِلّ مُحادَثات صاحِب المَنصَّة نَفسِه)، وَتِسع
/// نِقاط كِتابَة تَعمَل بِلا أَيّ تَخويل — أَخطَرُها
/// <c>POST /admin/tenants/create</c>: مَجهول يُنشِئ مُستَأجِراً حَقيقيّاً
/// في المَنصَّة بِطَلَب واحِد.</para>
///
/// <para><b>القَرار لَم يُمَسّ بِحَرف عِندَ النَّقل</b> — هُوَ ذاتُه:
/// جَلسَة studio صالِحَة، وَصاحِبُها <see cref="StudioUser.IsPlatformAdmin"/>.
/// وَما عَدا ذلكَ رَفض.</para>
/// </summary>
public sealed class PlatformAdminGuard
{
    private readonly IDocumentStore _store;
    private readonly StudioAuth _studioAuth;
    private readonly IHttpContextAccessor _http;

    // ذاكِرَة القَرار لِلطَّلَب الواحِد — نَفس عِلَّة TenantAdminGuard:
    // الصَفحَة قَد تَسأَل مَرَّتَين (تَصيير + مَنع تَحميل البَيانات).
    // المِفتاح هُوَ الـ HttpContext نَفسُه: لَو غابَ (circuit تَفاعُليّ)
    // نَرفُض بَدَل تَسليم قَرار قَديم. الصَفَحات المَحروسَة كُلُّها SSR ثابِت.
    private HttpContext? _cachedContext;
    private PlatformAdminDecision? _cached;

    public PlatformAdminGuard(IDocumentStore store, StudioAuth studioAuth, IHttpContextAccessor http)
    {
        _store = store;
        _studioAuth = studioAuth;
        _http = http;
    }

    /// <summary>نُسخَة الحَقن — تَقرَأ الطَّلَب الحاليّ مِن
    /// <see cref="IHttpContextAccessor"/>. تَستَخدِمها صَفَحات Razor.
    /// بِلا طَلَب حاليّ → رَفض (الافتِراض الآمِن).</summary>
    public async Task<PlatformAdminDecision> EvaluateAsync()
    {
        var ctx = _http.HttpContext;
        if (ctx is null) return new(PlatformAdminOutcome.Anonymous, null);

        if (!ReferenceEquals(ctx, _cachedContext)) { _cachedContext = ctx; _cached = null; }
        if (_cached is { } hit) return hit;

        var decision = await EvaluateAsync(_store, _studioAuth);
        _cached = decision;
        return decision;
    }

    /// <summary>القَرار نَفسُه — تَستَخدِمه نِقاط الـ POST الَّتي تَستَلِم
    /// الخِدمات مِن minimal API مُباشَرَةً.
    ///
    /// <para><see cref="StudioAuth.Load"/> يَقرَأ cookie الطَّلَب الحاليّ
    /// عَبر الـ accessor، فَلا نَحتاج تَمرير <c>HttpRequest</c>.</para></summary>
    public static async Task<PlatformAdminDecision> EvaluateAsync(
        IDocumentStore docStore, StudioAuth studioAuth)
    {
        studioAuth.Load();
        if (!studioAuth.IsAuthenticated) return new(PlatformAdminOutcome.Anonymous, null);

        await using var qs = docStore.QuerySession(StudioAuth.Tenant);
        var user = await qs.LoadAsync<StudioUser>(studioAuth.UserId!.Value);
        return new(Decide(user), user);
    }

    /// <summary>نِصف القَرار النَقِيّ، بِلا I/O — عَلَيه تَقَع الوَحَدات.
    /// <c>null</c> = الـ cookie يَحمِل مُعَرِّفاً لا مُستَخدِمَ لَه (حُذِفَ
    /// أَو زُوِّرَ) → لَيسَ مُشرِفاً.</summary>
    public static PlatformAdminOutcome Decide(StudioUser? user)
        => user?.IsPlatformAdmin == true
            ? PlatformAdminOutcome.Allowed
            : PlatformAdminOutcome.NotAdmin;
}
