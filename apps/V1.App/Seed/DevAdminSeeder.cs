using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;

namespace ACommerce.V1.App.Seed;

/// <summary>
/// بَذر مُشرِف مَنصَّة لِلتَّطوير — يُنشِئ (أَو يُرَقّي) <see cref="StudioUser"/>
/// بِرَقم هاتِف مُعطى إلى <c>IsPlatformAdmin</c>.
///
/// <para><b>لِماذا يَلزَم أَصلاً:</b> ‏<see cref="StudioOwnershipSeeder"/> يَرُدّ
/// مُبَكِّراً عِندَ <c>orphans.Count == 0</c> — أَي قَبل كُتلَة تَرقِيَة أَوَّل
/// مُستَخدِم. فَعَلى قاعِدَة كُلّ مَتاجِرِها مَملوكَة (نُسخَة إنتاج مَثَلاً) لا
/// يُرَقّى أَحَد أَبَداً، ولا سَبيل إلى سَطح الإدارَة عَلى فَرع جَديد.</para>
///
/// <para><b>بَوّابَتان لا واحِدَة</b> — أَشَدّ مِن <see cref="TestDataSeeder"/>:
/// بيئَة <c>Development</c> **و** مُتَغَيِّر <c>DEV_ADMIN_PHONE</c> مَضبوط.
/// غِياب أَيِّهِما = لا عَمَل إطلاقاً. لا يُفَعَّل بِالخَطَأ في إنتاج.</para>
/// </summary>
public static class DevAdminSeeder
{
    public const string PhoneVar = "DEV_ADMIN_PHONE";

    /// <summary>يُعيد الهاتِف المُرَقّى، أَو <c>null</c> لَو لَم يَعمَل.</summary>
    public static async Task<string?> RunAsync(
        IDocumentStore store, IWebHostEnvironment env, CancellationToken ct = default)
    {
        // البَوّابَة الأولى: بيئَة التَّطوير حَصراً.
        if (!env.IsDevelopment()) return null;

        // البَوّابَة الثانِيَة: طَلَب صَريح بِمُتَغَيِّر بيئَة.
        var phone = Environment.GetEnvironmentVariable(PhoneVar)?.Trim();
        if (string.IsNullOrEmpty(phone)) return null;

        await using var s = store.LightweightSession(StudioAuth.Tenant);
        var user = (await s.Query<StudioUser>().Where(u => u.Phone == phone).ToListAsync(ct))
            .FirstOrDefault();

        // idempotent: مَوجود ومُرَقّى ← لا كِتابَة.
        if (user is { IsPlatformAdmin: true }) return phone;

        user ??= new StudioUser { Id = Guid.NewGuid(), Phone = phone };
        user.IsPlatformAdmin = true;
        s.Store(user);
        await s.SaveChangesAsync(ct);
        return phone;
    }
}
