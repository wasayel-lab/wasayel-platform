using Microsoft.AspNetCore.Http;

namespace ACommerce.Platform.Shared;

/// <summary>
/// المُستَأجِر النَشِط في الطَلَب. مَدعوم بـ <see cref="HttpContext.Items"/>
/// لِيَكون مَرئيّاً عَبر كلّ scopes الـ ASP.NET (Wolverine يَفتَح nested
/// scopes أحياناً — الـ Items dictionary مَلكيّة HttpContext نَفسه فلا تَتأثَّر).
/// </summary>
public interface ITenantContext
{
    string Slug { get; }
    string Name { get; }
    string BrandColor { get; }
    bool IsResolved { get; }
}

public sealed class HttpItemTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _http;

    public HttpItemTenantContext(IHttpContextAccessor http) => _http = http;

    public bool IsResolved => _http.HttpContext?.Items.ContainsKey(TenantKeys.Slug) == true;
    public string Slug       => (string?)_http.HttpContext?.Items[TenantKeys.Slug]  ?? "";
    public string Name       => (string?)_http.HttpContext?.Items[TenantKeys.Name]  ?? "";
    public string BrandColor => (string?)_http.HttpContext?.Items[TenantKeys.Color] ?? "#000000";
}

public static class TenantKeys
{
    public const string Slug  = "Tenant.Slug";
    public const string Name  = "Tenant.Name";
    public const string Color = "Tenant.Color";
}

public static class TenantContextExtensions
{
    public static void SetTenant(this HttpContext ctx, string slug, string name, string brandColor)
    {
        ctx.Items[TenantKeys.Slug]  = slug;
        ctx.Items[TenantKeys.Name]  = name;
        ctx.Items[TenantKeys.Color] = brandColor;
    }
}
