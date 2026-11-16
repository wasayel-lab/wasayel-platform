using ACommerce.Kit.Tenants;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// مُهَيِّئ مرَّة-واحِدَة: لَو وُجِدَ <see cref="StudioUser"/> واحِد على
/// الأَقَلّ ولَدَيه مَتاجِر بِلا <c>OwnerUserId</c>، يُسنِدها لِأَوَّل
/// مُستَخدِم (الأَقدَم). يَعمَل عِندَ بَدء التَطبيق + بَعد كُلّ
/// تَسجيل دُخول جَديد (لِيَلتَقِط أَيّ تَسجيل أَوَّل بَعد إطلاق الميزَة).
///
/// <para><b>لا يُرَقّي أَحَداً.</b> كانَ يَرفَع أَوَّل مُستَخدِم إلى
/// <c>IsPlatformAdmin</c> ضِمناً — أَي أَنّ أَوَّل مَن يُسَجِّل عَلى قاعِدَة
/// فارِغَة يَملِك المَنصَّة، بِلا فِعل واعٍ مِن أَحَد. حُذِفَت التَرقِيَة:
/// صَلاحِيَّة المَنصَّة تُمنَح صَراحَةً بِـ
/// <c>PlatformAdminSeeder</c> (مُتَغَيِّر بيئَة + بَوّابَة ثانِيَة)، لا
/// كَأَثَر جانِبِيّ لِتَسجيل دُخول.</para>
///
/// <para>مُهِمّ: لا نَستَخدِم LINQ <c>Where(t => t.OwnerUserId == Guid.Empty)</c>
/// لِأَنّ Marten يُتَرجِمها لـ <c>CAST(data ->> 'OwnerUserId' as uuid) = $1</c>،
/// والمَتاجِر القَديمَة لا يَملِكون الحَقل في JSONB → القيمَة NULL → لا
/// تُطابِق. نَجلِب الكُلّ ونُصَفّي في الذاكِرَة (Marten deserializer يَملَأ
/// الحُقول المَفقودَة بِالقيمَة الافتراضِيَّة).</para>
///
/// <para><b>‏2026-10-25 — لِمُشرِفِ المَنَصَّةِ وَحدَه، وعِندَ الإقلاعِ وَحدَه (تَدقيقُ الإطلاق، تَيّارُ W8-3 — <c>lens-security-14</c>؛ يُلحَقُ
/// ولا يُعادُ حَرفٌ فَوقَ ما سَبَق)</b>: «أَوَّل مُستَخدِم (الأَقدَم)» أَعلاه كانَ أَيَّ مُستَخدِم، و«بَعد كُلّ تَسجيل دُخول» كانَ يُنَفَّذُ — فَعَلى قاعِدَةٍ
/// بِلا مُشرِفٍ مَلَكَ أَوَّلُ مَن سَجَّلَ نَفسَه في الاستوديو <c>ashare</c> و<c>ejar</c> و<c>order</c> عِندَ أَوَّلِ دُخولِه، ولَوحاتِ إدارَتِها ومُستَخدِميها
/// (مَقيسٌ حَيّاً، تَوصيفُه <c>44b77d3</c>). <b>صارَ</b>: المالِكُ أَقدَمُ مُستَخدِمٍ <see cref="StudioUser.IsPlatformAdmin"/> — صاحِبُ المَنَصَّةِ لا أَوَّلُ
/// زائِر — وبِلا مُشرِفٍ لا يُسنَدُ شَيءٌ ويَبقى اليَتيمُ يَتيماً حَتّى يُمنَحَ مُشرِف (‏<c>PlatformAdminSeeder</c>) ويُعادَ الإقلاع؛ ونُقطَةُ
/// <c>/studio/auth/verify</c> لا تُناديه. ولا يَلِدُ يَتيماً بَعدَ اليَومِ إلّا بَذرُ المَنَصَّة (<c>PlatformSeed</c>): وَكيلُ الإدارَةِ صارَ يَكتُبُ مُشرِفَه
/// (<c>AgentToolExecutor.ExecuteAsPlatformAdminAsync</c>).</para>
/// </summary>
public static class StudioOwnershipSeeder
{
    public static async Task RunAsync(IDocumentStore store, CancellationToken ct = default)
    {
        // أَقدَمُ مُشرِفِ مَنَصَّة — لا أَقدَمُ مُستَخدِم (‏2026-10-25، lens-security-14).
        await using var studioQs = store.QuerySession(StudioAuth.Tenant);
        var firstUser = (await studioQs.Query<StudioUser>()
            .Where(u => u.IsPlatformAdmin)
            .OrderBy(u => u.CreatedAt).Take(1).ToListAsync(ct)).FirstOrDefault();
        if (firstUser is null) return;   // لا مُشرِفَ بَعد — لا يُسنَدُ يَتيمٌ لِأَحَد

        // ── تَبَنّي المَتاجِر اليَتيمَة ──
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
    }
}

