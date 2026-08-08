namespace ACommerce.Templates.Customer.Marketplace.Gates;

/// <summary>
/// واجِهات تَأشير عَلى الـ commands. الـ pipeline يَلتَقِطها ويُشغِّل
/// الـ gate المُناسِبَة قَبل التَّنفيذ. لا يَتَدَخَّل الـ command نَفسه في
/// مَنطِق الفَحص — يُعلِن مُتَطَلَّباته فَقَط.
///
/// <para>الانتِقال لِـ Wolverine لاحِقاً يَتِم بِسَطر واحِد:
/// <c>opts.Policies.ForMessagesImplementing&lt;IRequireAuth&gt;().AddMiddleware&lt;AuthMiddleware&gt;()</c>
/// — نَفس الـ commands، نَفس الواجِهات، middleware مُولَّد بِالـ codegen.</para>
/// </summary>
public interface IRequireAuth
{
    Guid? UserId { get; }
}

public interface IRequireTenant
{
    string TenantSlug { get; }
}

public interface IRequireAcceptedTerms : IRequireAuth, IRequireTenant { }

public interface IRequirePermission : IRequireAuth, IRequireTenant
{
    string Permission { get; }
    /// <summary>الدَور الفَعّال — مُستَخرَج مِن URL في فُلتَر HTTP،
    /// مَطلوب صَراحَةً مَع commands خارِج HTTP (Sagas، background jobs).</summary>
    string? ActiveRole { get; }
}
