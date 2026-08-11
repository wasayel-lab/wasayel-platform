using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;

namespace ACommerce.V1.App.Seed;

/// <summary>
/// مَنح صَلاحِيَّة مُشرِف المَنصَّة **صَراحَةً**: يُنشِئ (أَو يُرَقّي)
/// <see cref="StudioUser"/> بِرَقم هاتِف مُعطى إلى <c>IsPlatformAdmin</c>.
///
/// <para><b>لِماذا يَلزَم:</b> ‏<see cref="StudioOwnershipSeeder"/> كانَ
/// يُرَقّي أَوَّل مُستَخدِم ضِمناً — فَأَوَّل مَن يُسَجِّل يَملِك المَنصَّة
/// بِلا فِعل واعٍ. حُذِفَ ذلكَ، وهذا بَديلُه: لا تُمنَح الصَلاحِيَّة إلّا
/// بِقَرار مَن يَملِك الخادِم ومُتَغَيِّراتِ بيئَتِه.</para>
///
/// <para><b>بَوّابَتان دائِماً:</b>
/// ‏(أ) <c>PLATFORM_ADMIN_PHONE</c> — مَن. غِيابُه = لا عَمَل إطلاقاً.
/// ‏(ب) بيئَة <c>Development</c>، أَو <c>PLATFORM_ADMIN_BOOTSTRAP=1</c>
/// خارِجَها — إقرار صَريح أَنّ المَنح مَقصود في بيئَة حَقيقِيَّة.</para>
///
/// <para>‏idempotent: مُستَخدِم مَوجود ومُرَقّى ← لا كِتابَة.</para>
/// </summary>
public static class PlatformAdminSeeder
{
    public const string PhoneVar     = "PLATFORM_ADMIN_PHONE";
    public const string BootstrapVar = "PLATFORM_ADMIN_BOOTSTRAP";

    /// <summary>يُعيد الهاتِف المَمنوح، أَو <c>null</c> لَو لَم يَعمَل.</summary>
    public static async Task<string?> RunAsync(
        IDocumentStore store, IWebHostEnvironment env, CancellationToken ct = default)
    {
        // البَوّابَة الأولى: طَلَب صَريح بِرَقم.
        var phone = Environment.GetEnvironmentVariable(PhoneVar)?.Trim();
        if (string.IsNullOrEmpty(phone)) return null;

        // البَوّابَة الثانِيَة: خارِج التَطوير يَلزَم إقرار مُنفَصِل.
        if (!env.IsDevelopment() &&
            Environment.GetEnvironmentVariable(BootstrapVar) != "1") return null;

        await using var s = store.LightweightSession(StudioAuth.Tenant);
        var user = (await s.Query<StudioUser>().Where(u => u.Phone == phone).ToListAsync(ct))
            .FirstOrDefault();

        if (user is { IsPlatformAdmin: true }) return phone;

        user ??= new StudioUser { Id = Guid.NewGuid(), Phone = phone };
        user.IsPlatformAdmin = true;
        s.Store(user);
        await s.SaveChangesAsync(ct);
        return phone;
    }
}
