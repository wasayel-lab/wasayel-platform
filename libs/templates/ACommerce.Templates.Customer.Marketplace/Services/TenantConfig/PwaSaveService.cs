using ACommerce.Kit.Tenants;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;

/// <summary>
/// <para>أَيقونَةٌ مَرفوعَة — <b>نَوعُها وحَجمُها وطَريقَةُ
/// قِراءَتِها</b>، لا <c>IFormFile</c>. والدالَّةُ في التَوقيع
/// مَقصودَة: تُتيح رَفضَ الحَجم <b>قَبلَ</b> قِراءَة البايتات، فَلا
/// يُحمَّل مِلَفٌّ كَبيرٌ في الذاكِرَة لِيُرفَض بَعدَها.</para>
///
/// <para>وسَطحٌ آخَر (‏API، تَطبيقٌ أَصيل) يُعطي نَفسَ الثَلاثَة مِن
/// مَصدَرِه: تَيّار مِلَفّ، أَو <c>base64</c> مُفَكَّك.</para>
/// </summary>
public sealed record UploadedIcon(
    string ContentType,
    long Length,
    Func<CancellationToken, Task<byte[]>> ReadBytesAsync);

/// <summary>ما يُرسَل لِدَورٍ واحِد في صَفحَة PWA: اسمُ التَطبيق،
/// وهَل تُمسَح الأَيقونَة، وأَيقونَةٌ جَديدَة إن رُفِعَت.</summary>
public sealed record PwaRoleInput(string RoleSlug, string? Name, bool ClearIcon, UploadedIcon? Icon);

/// <summary>حِفظُ صَفحَة PWA كامِلَةً — مَدخَلٌ لِكُلّ دَور.</summary>
public sealed record PwaSaveRequest(IReadOnlyList<PwaRoleInput> Roles);

/// <summary>
/// <para><b>أَسماءُ تَطبيقات PWA وأَيقوناتُها — والزَوجُ الَّذي كانَ
/// مُتَطابِقاً حَرفاً.</b> قاسَت وَثيقَة القَرار المِعماريّ هذا
/// الزَوجَ فَوَجَدَت الجِسمَين مُتَطابِقَين «عَدا الحارِس والتَدقيق
/// والمَسار» — ‏46 سَطراً و‏43. فَالتَوحيدُ هُنا <b>بِلا خاسِر</b>،
/// والمَحسومُ الوَحيد أَنّ التَدقيق يُكتَب في الطَرَفَين.</para>
///
/// <para><b>وهذا بِالضَبط ما يَجعَلُه جَديراً بِالإخراج</b>: نُسخَتانِ
/// مُتَطابِقَتانِ اليَومَ هُما نُسخَتانِ مُتَبايِنَتانِ بَعدَ أَوَّل
/// تَعديلٍ يُصيب إحداهُما — كَما وَقَعَ في الفِئات والأَدوار
/// والمَناطِق. والتَطابُقُ حالٌ لا ضَمان.</para>
///
/// <para><b>وسَقفُ الحَجم والأَنواع سِياسَةٌ لا نَقل</b>، فَسَكَنَت
/// هُنا: نُقطَةُ الويب تُسَلِّم النَوعَ والطول ودالَّةَ القِراءَة،
/// والخِدمَةُ تَقبَل أَو تَرفُض. ولَو سَكَنَت في النُقطَة لَكانَ
/// عَلى كُلّ سَطحٍ جَديد أَن يَعرِفَها — أَو يَنساها.</para>
/// </summary>
public static class PwaSaveService
{
    public const string AuditAction = "tenant.pwa_save";

    /// <summary>‏256 كيلوبايت — لِيَبقى حَجمُ وَثيقَة
    /// <see cref="Tenant"/> مَعقولاً؛ الأَيقونَةُ تُخزَّن
    /// <c>data:</c> داخِلَها.</summary>
    public const long MaxIconBytes = 256 * 1024;

    public static readonly IReadOnlySet<string> AllowedContentTypes =
        new HashSet<string>(StringComparer.Ordinal) { "image/png", "image/svg+xml", "image/webp" };

    /// <summary><b>دالَّةُ القَرار، نَقِيَّة</b> (ق٣): أَيقونَةٌ
    /// تُقبَل أَو تُرَدّ بِرَمز — بِلا قِراءَةِ بايتٍ واحِد.</summary>
    public static string? WhyIconRejected(UploadedIcon icon)
    {
        if (icon.Length > MaxIconBytes) return TenantConfigCodes.IconTooLarge;
        if (!AllowedContentTypes.Contains(icon.ContentType.ToLowerInvariant()))
            return TenantConfigCodes.IconBadType;
        return null;
    }

    public static async Task<TenantConfigResult> SaveAsync(
        IDocumentSession session, string slug, PwaSaveRequest r,
        CancellationToken ct = default)
    {
        var t = await session.LoadAsync<Tenant>(slug, ct);
        if (t is null) return TenantConfigResult.TenantMissing;

        var byRole = r.Roles.ToDictionary(x => x.RoleSlug, StringComparer.Ordinal);

        foreach (var role in t.Roles)
        {
            // دَورٌ لا مَدخَلَ لَه في الطَلَب لا يُمَسّ. والنُقطَةُ
            // تُرسِل مَدخَلاً لِكُلّ دَورٍ ظَهَرَ في النَموذَج،
            // فَالسُلوكُ عَبرَ الويب لَم يَتَغَيَّر؛ والفَرقُ يَظهَر
            // لِنِداءٍ جُزئيّ مِن API لاحِقاً — وحينَها «لا تَمَسّ ما
            // لَم أَذكُر» أَسلَمُ مِن «امحُ كُلّ ما لَم أَذكُر».
            if (!byRole.TryGetValue(role.Slug, out var input)) continue;

            role.PwaName = string.IsNullOrEmpty(input.Name?.Trim()) ? null : input.Name!.Trim();

            if (input.ClearIcon) role.PwaIconDataUrl = null;

            if (input.Icon is not { } icon || icon.Length <= 0) continue;

            if (WhyIconRejected(icon) is { } code) return TenantConfigResult.Reject(code);

            var bytes = await icon.ReadBytesAsync(ct);
            role.PwaIconDataUrl =
                $"data:{icon.ContentType.ToLowerInvariant()};base64,{Convert.ToBase64String(bytes)}";
        }

        session.Store(t);
        return TenantConfigResult.Saved;
    }
}
