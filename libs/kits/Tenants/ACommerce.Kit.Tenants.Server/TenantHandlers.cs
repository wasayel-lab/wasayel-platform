using Marten;
using Wolverine.Http;
using Wolverine.Marten;

namespace ACommerce.Kit.Tenants.Server;

/// <summary>
/// Handlers لِكيت المُستَأجِرين. تُسَجَّل تلقائيّاً عَبر Wolverine
/// assembly scan. الـ <c>[WolverinePost]</c>/<c>[WolverineGet]</c>
/// تَكشِف الـ method كَ HTTP endpoint بدون controller منفصل.
/// </summary>
public static class TenantHandlers
{
    [WolverinePost("/admin/tenants")]
    public static async Task<Tenant> Create(CreateTenant cmd, IDocumentSession session)
    {
        var tenant = new Tenant
        {
            Id = cmd.Slug,
            Name = cmd.Name,
            BrandColor = cmd.BrandColor,
            City = cmd.City,
            TagLine = cmd.TagLine
        };
        session.Store(tenant);
        await session.SaveChangesAsync();
        return tenant;
    }

    [WolverinePost("/admin/tenants/{slug}/categories")]
    public static async Task<Tenant?> AddCategoryHandler(
        string slug, AddCategory cmd, IDocumentSession session)
    {
        var tenant = await session.LoadAsync<Tenant>(slug);
        if (tenant is null) return null;
        if (tenant.Categories.Any(c => c.Slug == cmd.CategorySlug)) return tenant;

        tenant.Categories.Add(new Category
        {
            Slug = cmd.CategorySlug,
            Label = cmd.Label,
            Icon = cmd.Icon,
            Attributes = cmd.Attributes ?? new()
        });
        session.Store(tenant);
        await session.SaveChangesAsync();
        return tenant;
    }

    // ملاحَظَة: كانَت هُنا GET endpoints لِـ /admin/tenants و
    // /admin/tenants/{slug} كَـ JSON، لكِنَّها صارَت تَتَعارَض مَع
    // صَفَحات Blazor SSR (@page) في القَالِب. الـ POSTs أَعلاه باقِيَة
    // لِأَنَّها مُختَلِفَة في الـ verb.
}
