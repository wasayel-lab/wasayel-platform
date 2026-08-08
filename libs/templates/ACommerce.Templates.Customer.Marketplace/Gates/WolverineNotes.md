# Wolverine Migration Notes

## الحاليّ (Phase 2 — in-process)

```csharp
public sealed record AcceptTermsCommand(Guid UserId, string TenantSlug, int Version)
    : IRequireAuth, IRequireTenant;

await pipeline.ExecuteAsync(cmd, () => handler.HandleAsync(cmd));
```

`GatePipeline.ExecuteAsync` يَفحَص الـ marker interfaces، يُشَغِّل الـ gates بِترتيب، يَستَدعي الـ handler. يَعمَل بِدون Wolverine، يُسَهِّل الاختِبار، صَفر تَبَعِيّات إضافيَّة.

## الانتِقال لِـ Wolverine (مُستَقبَلاً، بِدون تَعديل الـ commands)

```csharp
// Program.cs
builder.Host.UseWolverine(opts =>
{
    opts.Policies
        .ForMessagesImplementing<IRequireAuth>()
        .AddMiddleware<AuthMiddleware>();
    opts.Policies
        .ForMessagesImplementing<IRequireAcceptedTerms>()
        .AddMiddleware<TermsMiddleware>();
    opts.Policies
        .ForMessagesImplementing<IRequirePermission>()
        .AddMiddleware<PermissionMiddleware>();
});

// AuthMiddleware.cs
public static class AuthMiddleware
{
    public static HandlerContinuation Before(IRequireAuth msg)
    {
        if (msg.UserId is null) throw new GateDeniedException("auth", "…");
        return HandlerContinuation.Continue;
    }
}

public static class TermsMiddleware
{
    public static async Task<HandlerContinuation> BeforeAsync(
        IRequireAcceptedTerms msg,
        IDocumentSession session,
        CancellationToken ct)
    {
        var user = await session.LoadAsync<User>(msg.UserId!.Value, ct);
        if (user?.AcceptedTermsVersion < TermsPolicy.CurrentVersion)
            throw new GateDeniedException("terms", "…");
        return HandlerContinuation.Continue;
    }
}

// Handler يَبقى كَما هو — Wolverine يَكتَشِفه بِالاتِّفاقيَّة:
public static class AcceptTermsHandler
{
    public static async Task Handle(
        AcceptTermsCommand cmd, IDocumentSession session, CancellationToken ct)
    {
        // …
    }
}

// الـ adapter (endpoint) يَنتَقِل لِـ Wolverine.HTTP:
app.MapWolverineEndpoints();   // يَكتَشِف [WolverinePost("/{slug}/terms/accept")]
```

## الفُروق

| الجَوانِب | الحاليّ | Wolverine |
|---|---|---|
| الـ command | `record + interfaces` | نَفسه |
| الـ handler | class مُسَجَّلَة في DI | static method بِاتِّفاقيَّة |
| الـ middleware | switch on interfaces | policy + class static method |
| تَنفيذ الـ gates | reflection (cast إلى interface) | كود مُولَّد بِالـ codegen، صِفر reflection |
| اِكتِشاف الـ handler | DI lookup يَدَويّ | اِكتِشاف تِلقائيّ بِالاتِّفاقيَّة |
| Sagas | يَدَويّ | مَبني (Saga<T>) |
| HTTP transport | minimal API بِالـ adapter | `MapWolverineEndpoints` تِلقائيّ |
| Background jobs | بِحاجَة BackgroundService | Wolverine scheduler |

## مَتى الانتِقال

عِندَ ظُهور أَيّ مِن: (أ) رَغبَة في source-generated middleware (للأَداء)، (ب) sagas طَويلَة الأَمَد، (ج) durable messaging بَين خِدمات، (د) scheduled commands. حاليّاً المَنصَّة مُكتَفِيَة بِالـ in-process pipeline.
