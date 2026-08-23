using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;

namespace ACommerce.V1.App.Seed;

/// <summary>ما فُعِلَ فِعلاً في هذا الإقلاع — لِيُقالَ في اللوغ بِاسمِه.</summary>
public readonly record struct PlatformAdminGrantResult(
    string? Phone, string? Email, bool EmailRejected)
{
    public bool Any => Phone is not null || Email is not null;
}

/// <summary>
/// مَنح صَلاحِيَّة مُشرِف المَنصَّة **صَراحَةً**: يُنشِئ (أَو يُرَقّي)
/// <see cref="StudioUser"/> بِرَقم هاتِف و/أَو بَريدٍ مُعطى إلى
/// <c>IsPlatformAdmin</c>.
///
/// <para><b>لِماذا يَلزَم:</b> ‏<see cref="StudioOwnershipSeeder"/> كانَ
/// يُرَقّي أَوَّل مُستَخدِم ضِمناً — فَأَوَّل مَن يُسَجِّل يَملِك المَنصَّة
/// بِلا فِعل واعٍ. حُذِفَ ذلكَ، وهذا بَديلُه: لا تُمنَح الصَلاحِيَّة إلّا
/// بِقَرار مَن يَملِك الخادِم ومُتَغَيِّراتِ بيئَتِه.</para>
///
/// <para><b>ولِماذا البَريدُ بِجِوار الهاتِف (‏2026-08-23):</b> بَعدَ
/// <c>cd43b366</c> تُغلَق قَناةُ الرَسائِل في الإنتاج بِلا
/// <c>Auth__Sms__Provider</c>، والمالِكُ سَيَضبُط SMTP وَحدَه. فَمُعَرِّفٌ
/// هاتِفيٌّ حَصريٌّ يَعني أَنّ كُلّ دُخولٍ بِالبَريد مُستَخدِمٌ آخَرُ بِلا
/// صَلاحِيَّة.</para>
///
/// <para><b>البَوّابَتان والتَطبيعُ في جَدوَلٍ نَقِيٍّ يُختَبَر</b> —
/// <see cref="PlatformAdminGrant.Decide"/>. وهذا المِلَفُّ نِصفُ الـI/O
/// وَحدَه: بَحثٌ، إنشاءٌ عِندَ الغِياب، تَرقِيَة.</para>
///
/// <para>‏idempotent: مُستَخدِم مَوجود ومُرَقّى ← لا كِتابَة.</para>
///
/// <para><b>وحَدُّ ما يَفعَلُه هذا المِلَفّ — يُقال ولا يُبتَلَع:</b> المَنحُ
/// يَكتُب الصَلاحِيَّة، <b>ولا يَفتَح باباً</b>. والبابُ نَفسُه صارَ
/// مَبنِيّاً (‏2026-08-23، <c>942539b8</c>): <c>POST /studio/auth/email/login</c>
/// ثُمَّ <c>/studio/auth/verify</c> عَبر <c>IEmailOtpChannel</c> المَضبوطَة،
/// ومَدخَلُ «إدارَة المَنَصَّة» في شَريطِ الاستوديو يَقود إلى
/// <c>/admin</c>. فَالمَمنوحُ هُنا <b>يُبلَغ بِنَقرَة</b> — بِشَرطِ قَناةٍ
/// مَضبوطَة خارِجَ Development، وإلّا فَلا بابَ لِأَحَد.</para>
///
/// <para><b>وما زالَ حَدّاً</b>: هذا المِلَفّ لا يَعرِف بِالقَناةِ شَيئاً.
/// مَنحٌ بِلا قَناةٍ مَضبوطَة = صَلاحِيَّةٌ مَكتوبَةٌ لا يُدخَل بِها.</para>
/// </summary>
public static class PlatformAdminSeeder
{
    // مُحالَةٌ إلى الجَدوَل النَقِيّ — اسمُ المُتَغَيِّرِ يُكتَب مَرَّةً واحِدَة.
    public const string PhoneVar     = PlatformAdminGrant.PhoneVar;
    public const string EmailVar     = PlatformAdminGrant.EmailVar;
    public const string BootstrapVar = PlatformAdminGrant.BootstrapVar;

    /// <summary>يُعيد ما مُنِحَ فِعلاً، أَو حَقلَين فارِغَين لَو لَم يَعمَل.</summary>
    public static async Task<PlatformAdminGrantResult> RunAsync(
        IDocumentStore store, IWebHostEnvironment env, CancellationToken ct = default)
    {
        var request = PlatformAdminGrant.Decide(
            Environment.GetEnvironmentVariable(PhoneVar),
            Environment.GetEnvironmentVariable(EmailVar),
            env.IsDevelopment(),
            Environment.GetEnvironmentVariable(BootstrapVar));

        if (request.IsEmpty)
            return new(null, null, request.EmailRejected);

        await using var s = store.LightweightSession(StudioAuth.Tenant);
        var dirty = false;

        if (request.Phone is { } phone)
        {
            var byPhone = (await s.Query<StudioUser>()
                .Where(u => u.Phone == phone).ToListAsync(ct)).FirstOrDefault();
            if (Promote(ref byPhone, () => new StudioUser { Id = Guid.NewGuid(), Phone = phone }))
            { s.Store(byPhone!); dirty = true; }
        }

        // البَريدُ مِفتاحُ هُوِيَّةٍ مُستَقِلّ — لا يُخلَط بِبَحث الهاتِف كَي
        // لا يَلتَقِط مُستَخدِمي الهاتِف ذَوي الحَقل الفارِغ (نَفسُ عِلَّة
        // <c>AuthHandlers.GetOrCreateUserAsync</c>). والقيمَةُ هُنا مُطَبَّعَةٌ
        // سَلَفاً، والمُخَزَّنُ مُطَبَّعٌ سَلَفاً — فَالمُساواةُ في Postgres
        // كافِيَةٌ ولا تَحتاج دالَّةَ حالَةِ أَحرُف.
        if (request.Email is { } email)
        {
            var byEmail = (await s.Query<StudioUser>()
                .Where(u => u.Email == email).ToListAsync(ct)).FirstOrDefault();
            if (Promote(ref byEmail, () => new StudioUser { Id = Guid.NewGuid(), Email = email }))
            { s.Store(byEmail!); dirty = true; }
        }

        if (dirty) await s.SaveChangesAsync(ct);
        return new(request.Phone, request.Email, request.EmailRejected);
    }

    /// <summary>‏<c>true</c> = يَحتاج كِتابَة. مُستَخدِمٌ مَوجودٌ ومُرَقّى
    /// يُعيد <c>false</c> بِلا لَمسِ الوَثيقَة (الـidempotency).</summary>
    private static bool Promote(ref StudioUser? user, Func<StudioUser> create)
    {
        if (user is { IsPlatformAdmin: true }) return false;
        user ??= create();
        user.IsPlatformAdmin = true;
        return true;
    }
}
