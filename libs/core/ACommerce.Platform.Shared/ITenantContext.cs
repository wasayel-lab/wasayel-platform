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
    /// <summary>"phone" | "nafath" | "email" — القَناة المُعلَنَة في وَثيقَة
    /// المُستَأجِر، تُمَرَّر كَما هي بِلا تَعداد مُغلَق هُنا (الطَبَقَة
    /// المُشتَرَكَة لا تَعرِف عُدَّة Auth). الافتِراضيّ "phone".</summary>
    string AuthChannel { get; }

    /// <summary>الشِعار النَصّيّ لِلمَتجَر — يَدخُل في <c>&lt;title&gt;</c>
    /// وَوَصف meta. حَقل عَرض بَحت، يُقرَأ مَع بَقيَّة تَعريف المُستَأجِر
    /// في نَفس تَحميل الـ middleware (بِلا استِعلام إضافيّ).</summary>
    string TagLine { get; }

    /// <summary>مَدينَة المَتجَر — تَدخُل في وَصف meta و JSON-LD.</summary>
    string City { get; }

    bool IsResolved { get; }
}

public sealed class HttpItemTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _http;

    public HttpItemTenantContext(IHttpContextAccessor http) => _http = http;

    public bool IsResolved => _http.HttpContext?.Items.ContainsKey(TenantKeys.Slug) == true;
    public string Slug        => (string?)_http.HttpContext?.Items[TenantKeys.Slug]  ?? "";
    public string Name        => (string?)_http.HttpContext?.Items[TenantKeys.Name]  ?? "";
    public string BrandColor  => (string?)_http.HttpContext?.Items[TenantKeys.Color] ?? "#000000";
    public string AuthChannel => (string?)_http.HttpContext?.Items[TenantKeys.AuthChannel] ?? "phone";
    public string TagLine     => (string?)_http.HttpContext?.Items[TenantKeys.TagLine] ?? "";
    public string City        => (string?)_http.HttpContext?.Items[TenantKeys.City]    ?? "";
}

public static class TenantKeys
{
    public const string Slug  = "Tenant.Slug";
    public const string Name  = "Tenant.Name";
    public const string Color = "Tenant.Color";
    public const string AuthChannel = "Tenant.AuthChannel";
    public const string TagLine = "Tenant.TagLine";
    public const string City    = "Tenant.City";
}

public static class TenantContextExtensions
{
    /// <summary><paramref name="tagLine"/> و <paramref name="city"/>
    /// اختِيارِيّان لِيَبقَى كُلّ مُستَدعٍ قائِم صالِحاً بِلا تَعديل.</summary>
    public static void SetTenant(this HttpContext ctx, string slug, string name,
        string brandColor, string authChannel, string tagLine = "", string city = "")
    {
        ctx.Items[TenantKeys.Slug]  = slug;
        ctx.Items[TenantKeys.Name]  = name;
        ctx.Items[TenantKeys.Color] = brandColor;
        ctx.Items[TenantKeys.AuthChannel] = authChannel;
        ctx.Items[TenantKeys.TagLine] = tagLine;
        ctx.Items[TenantKeys.City]    = city;
    }
}
