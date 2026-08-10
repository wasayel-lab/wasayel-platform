using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// بَوّابَة إدارَة المَنصَّة — نِصف القَرار النَّقِيّ.
///
/// <para><b>العَيب المُلتَقَط:</b> القَرار كانَ مَكتوباً مَرَّتَين — في
/// <c>RequirePlatformAdmin.razor</c> وَفي نُقطَة
/// <c>/admin/tenants/{slug}/suspend</c> — وَغائِباً عَمّا سِواهُما. فَكانَت
/// خَمس صَفَحات مَنصَّة تُصَيَّر كامِلَةً لِطَلَب <c>curl</c> مَجهول (مِنها
/// <c>/admin/agent</c> بِسِجِلّ مُحادَثات صاحِب المَنصَّة)، وَتِسع نِقاط
/// كِتابَة تَعمَل بِلا تَخويل — أَخطَرُها <c>POST /admin/tenants/create</c>
/// الَّتي تُنشِئ مُستَأجِراً حَقيقيّاً لِمَجهول.</para>
///
/// <para>هذِه الوَحَدات تَحرُس التَّعريف الواحِد بَعدَ استِخراجِه إلى
/// <see cref="PlatformAdminGuard"/>. المَنطِق المُتَبَقّي في
/// <c>EvaluateAsync</c> هُوَ I/O مَحض (تَحميل <see cref="StudioUser"/> مِن
/// Marten وَقِراءَة الـ cookie)، وَحَلّ الـ cookie مُغَطّى في
/// <see cref="AuthSessionCookieResolutionTests"/> — فَلا نُكَرِّرُه هُنا.</para>
///
/// <para><b>الطَّبَقَتان لا تَتَداخَلان:</b> هذه لِإجراءات المَنصَّة كُلِّها،
/// وَ<see cref="TenantAdminGuard"/> لِمَتجَر بِعَينِه — وَتَغطِيَتُهُما
/// مُنفَصِلَة كَما هُما.</para>
/// </summary>
public class PlatformAdminGuardTests
{
    private static StudioUser UserWith(bool isPlatformAdmin)
        => new() { Id = Guid.NewGuid(), Phone = "05xxxxxxxx", IsPlatformAdmin = isPlatformAdmin };

    // ─── المُوجِب ────────────────────────────────────────────────────────

    [Fact]
    public void PlatformAdmin_IsAllowed()
    {
        Assert.Equal(PlatformAdminOutcome.Allowed,
            PlatformAdminGuard.Decide(UserWith(isPlatformAdmin: true)));
    }

    // ─── السالِب: مُسَجَّل دُخولاً وَلَيسَ مُشرِفاً ────────────────────────

    [Fact]
    public void StudioUser_WithoutPlatformAdmin_IsRejected()
    {
        // رائِد أَعمال عادِيّ في الاستوديو — لَه جَلسَة صالِحَة تَماماً،
        // وَلا يَملِك المَنصَّة. هذا هُوَ الفَرق عَن الحالَة المَجهولَة:
        // الواجِهَة تَعرِض لَه «صَلاحيَّة غَير كافِيَة» لا زِرّ الدُخول.
        Assert.Equal(PlatformAdminOutcome.NotAdmin,
            PlatformAdminGuard.Decide(UserWith(isPlatformAdmin: false)));
    }

    // ─── السالِب: لا مُستَخدِم أَصلاً ─────────────────────────────────────

    [Fact]
    public void MissingStudioUser_IsRejected()
    {
        // الـ cookie يَحمِل مُعَرِّفاً لا مُستَخدِمَ لَه (حُذِفَ، أَو زُوِّرَ
        // التوكِن). الافتِراض الآمِن: لَيسَ مُشرِفاً.
        Assert.Equal(PlatformAdminOutcome.NotAdmin, PlatformAdminGuard.Decide(null));
    }

    [Fact]
    public void Decide_NeverReturnsAnonymous()
    {
        // Anonymous حالَة الـ cookie الغائِب وَحدَها، وَتُحسَم قَبل بُلوغ
        // Decide. لَو تَسَرَّبَت مِن هُنا يَوماً لَاختَلَط «لَم يَدخُل» بِـ
        // «دَخَلَ وَلَيسَ مُشرِفاً» في الواجِهَة.
        Assert.NotEqual(PlatformAdminOutcome.Anonymous, PlatformAdminGuard.Decide(null));
        Assert.NotEqual(PlatformAdminOutcome.Anonymous,
            PlatformAdminGuard.Decide(UserWith(isPlatformAdmin: false)));
        Assert.NotEqual(PlatformAdminOutcome.Anonymous,
            PlatformAdminGuard.Decide(UserWith(isPlatformAdmin: true)));
    }

    // ─── العَقد الَّذي تَقرَؤُه نِقاط الكِتابَة ────────────────────────────

    [Fact]
    public void Decision_Allowed_TracksOutcome()
    {
        // نِقاط الـ POST تَسأَل .Allowed وَحدَها. لَو انفَصَلَت عَن الـ
        // Outcome يَوماً لَمَرَّت كِتابَة بِلا تَخويل — وَهُوَ العَيب عَينُه
        // الَّذي أُغلِقَ.
        Assert.True(new PlatformAdminDecision(PlatformAdminOutcome.Allowed, null).Allowed);
        Assert.False(new PlatformAdminDecision(PlatformAdminOutcome.NotAdmin, null).Allowed);
        Assert.False(new PlatformAdminDecision(PlatformAdminOutcome.Anonymous, null).Allowed);
    }

    [Fact]
    public void Decision_CarriesUser_ForAuditLine()
    {
        // نُقطَة suspend تَكتُب سَطر audit بِاسم الفاعِل مِن نَفس القَرار،
        // بِلا استِعلام ثانٍ.
        var user = UserWith(isPlatformAdmin: true);
        var decision = new PlatformAdminDecision(PlatformAdminGuard.Decide(user), user);
        Assert.True(decision.Allowed);
        Assert.Same(user, decision.User);
    }
}
