using ACommerce.Kit.Tenants;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// مُهَيِّئ مرَّة-واحِدَة: لَو وُجِدَ <see cref="StudioUser"/> واحِد على
/// الأَقَلّ ولَدَيه مَتاجِر بِلا <c>OwnerUserId</c>، يُسنِدها لِأَوَّل
/// مُستَخدِم (الأَقدَم). يَعمَل عِندَ بَدء التَطبيق + بَعد كُلّ
/// تَسجيل دُخول جَديد (لِيَلتَقِط أَيّ تَسجيل أَوَّل بَعد إطلاق الميزَة).
///
/// <para>مُهِمّ: لا نَستَخدِم LINQ <c>Where(t => t.OwnerUserId == Guid.Empty)</c>
/// لِأَنّ Marten يُتَرجِمها لـ <c>CAST(data ->> 'OwnerUserId' as uuid) = $1</c>،
/// والمَتاجِر القَديمَة لا يَملِكون الحَقل في JSONB → القيمَة NULL → لا
/// تُطابِق. نَجلِب الكُلّ ونُصَفّي في الذاكِرَة (Marten deserializer يَملَأ
/// الحُقول المَفقودَة بِالقيمَة الافتراضِيَّة).</para>
/// </summary>
public static class StudioOwnershipSeeder
{
    public static async Task RunAsync(IDocumentStore store, CancellationToken ct = default)
    {
        // أَوَّل StudioUser (الأَقدَم).
        await using var studioQs = store.QuerySession(StudioAuth.Tenant);
        var firstUser = (await studioQs.Query<StudioUser>()
            .OrderBy(u => u.CreatedAt).Take(1).ToListAsync(ct)).FirstOrDefault();
        if (firstUser is null) return;   // لا مُستَخدِمين بَعد — لا شَيء لِنَربِطه

        // جَلب كُلّ المَتاجِر ثُمَّ التَّصفِيَة في الذاكِرَة (انظر التَّعليق).
        await using var qs = store.QuerySession();
        var all = (await qs.Query<Tenant>().ToListAsync(ct)).ToList();
        var orphans = all.Where(t => t.OwnerUserId == Guid.Empty).ToList();
        if (orphans.Count == 0) return;

        await using var ws = store.LightweightSession();
        foreach (var t in orphans)
        {
            t.OwnerUserId = firstUser.Id;
            ws.Store(t);
        }
        await ws.SaveChangesAsync(ct);

        // أَوَّل مُستَخدِم يُصبِح platform admin تِلقائيّاً (لِلتَّجرِبَة).
        if (!firstUser.IsPlatformAdmin)
        {
            await using var us = store.LightweightSession(StudioAuth.Tenant);
            var u = await us.LoadAsync<StudioUser>(firstUser.Id, ct);
            if (u is not null)
            {
                u.IsPlatformAdmin = true;
                us.Store(u);
                await us.SaveChangesAsync(ct);
            }
        }
    }
}

