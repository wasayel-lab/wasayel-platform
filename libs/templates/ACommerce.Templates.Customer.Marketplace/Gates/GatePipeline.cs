using ACommerce.Kit.Auth;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Gates;

/// <summary>
/// dispatcher مُصَغَّر: يَأخُذ command، يَفحَص marker interfaces المُنَفَّذَة،
/// يُشَغِّل الـ gate المُناسِبَة بِترتيب (auth → tenant → terms → permission)،
/// ثُمَّ يَستَدعي الـ handler. عَلى فَشَل أَيّ gate يَرمي
/// <see cref="GateDeniedException"/> — الـ caller يُحَوِّلها لِـ HTTP result
/// أَو error UI حَسَب السياق.
///
/// <para>هذا النَّمَط يَنطَبِق عَلى أَيّ command — سَواء جاء مِن HTTP form
/// أَو background job أَو Wolverine handler. الـ commands نَفسها لا تَعرِف
/// أَيّ شَيء عَن HTTP أَو cookies.</para>
///
/// <para>الانتِقال لِـ Wolverine لاحِقاً: تُستَبدَل الفُحوصات هُنا بِـ
/// <c>WolverineOptions.Policies.ForMessagesImplementing&lt;IRequireAuth&gt;()
/// .AddMiddleware&lt;AuthMiddleware&gt;()</c> — Wolverine يُولِّد كود
/// مُحَدَّد لِكُلّ handler عِندَ الـ startup، صِفر reflection في الـ hot path.</para>
/// </summary>
public sealed class GatePipeline
{
    private readonly IDocumentStore _store;

    public GatePipeline(IDocumentStore store) => _store = store;

    public async Task<TResult> ExecuteAsync<TResult>(
        object command, Func<Task<TResult>> handler, CancellationToken ct = default)
    {
        await CheckGatesAsync(command, ct);
        return await handler();
    }

    public async Task ExecuteAsync(
        object command, Func<Task> handler, CancellationToken ct = default)
    {
        await CheckGatesAsync(command, ct);
        await handler();
    }

    private async Task CheckGatesAsync(object command, CancellationToken ct)
    {
        // ١. تَوثيق — أَوَّل عائِق، الباقي يَفتَرِض وُجود userId.
        if (command is IRequireAuth { UserId: null })
            throw new GateDeniedException("auth", "تَوثيق مَطلوب.");

        // ٢. tenant scope — مُهِمّ لِأَنّ Marten conjoined tenancy يَحتاجه.
        if (command is IRequireTenant t && string.IsNullOrEmpty(t.TenantSlug))
            throw new GateDeniedException("tenant", "المَتجَر غَير مُحَدَّد.");

        // ٣. شُروط مَقبولَة — يَجلِب User مَرَّة واحِدَة، يُعيد استِخدامه لِـ
        //    permission. حاليّاً جَلبَتان مُنفَصِلَتان لِبَساطَة الكود؛ Wolverine
        //    middleware لاحِقاً يُمكِنه تَخزينه في الـ MessageContext.
        if (command is IRequireAcceptedTerms terms)
        {
            await using var s = _store.QuerySession(terms.TenantSlug);
            var user = await s.LoadAsync<User>(terms.UserId!.Value, ct);
            if (user is null)
                throw new GateDeniedException("auth", "المُستَخدِم غَير مَوجود.");
            var ok = user.AcceptedTermsAt is not null
                  && user.AcceptedTermsVersion >= TermsPolicy.CurrentVersion;
            if (!ok)
                throw new GateDeniedException("terms", "قَبول الشُروط مَطلوب.");
        }

        // ٤. صَلاحِيَّة — أَخيراً، يَفتَرِض tenant وَ user مَوجودَين.
        if (command is IRequirePermission perm)
        {
            await using var g = _store.QuerySession();
            var tenant = await g.LoadAsync<Tenant>(perm.TenantSlug, ct);
            if (tenant is null)
                throw new GateDeniedException("tenant", "المَتجَر غَير مَوجود.");
            if (tenant.Roles.Count == 0) return;   // legacy mode — مَفتوح

            await using var ts = _store.QuerySession(perm.TenantSlug);
            var user = await ts.LoadAsync<User>(perm.UserId!.Value, ct);
            if (user is null)
                throw new GateDeniedException("auth", "المُستَخدِم غَير مَوجود.");
            var activeRole = perm.ActiveRole ?? user.ActiveRole;
            if (!RolePermissions.Has(tenant.Roles, activeRole, perm.Permission))
                throw new GateDeniedException("permission",
                    $"يَتَطَلَّب صَلاحِيَّة «{perm.Permission}».");
        }
    }
}

/// <summary>يُرمَى عِندَ رَفض gate. <see cref="GateName"/> يُعَرِّف أَيّ gate
/// (auth/tenant/terms/permission) لِيُحَوِّلها الـ caller لِـ HTTP redirect
/// أَو رِسالَة مُلائِمَة.</summary>
public sealed class GateDeniedException : Exception
{
    public string GateName { get; }
    public GateDeniedException(string gate, string message) : base(message)
        => GateName = gate;
}
