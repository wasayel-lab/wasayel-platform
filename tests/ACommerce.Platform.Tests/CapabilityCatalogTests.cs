using ACommerce.Kit.Subscriptions;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>بَوّابَة مَعجَم القُدُرات</b> — لِكُلّ رَمز خَرق اختِبار
/// مُوجِب واختِبار سالِب (القاعِدَة ٤)، ومَعَهُما البُرهان الَّذي
/// يُمَيِّز هذا المَعجَم عَن سَلَفِه: <b>الطَرَف الآخَر مُغلَق</b> —
/// لا يُقبَل رَمز خارِج المَعجَم في مَوضِع فَحص، لا وَقتَ تَركيب ولا
/// في نَصّ المُستودَع.</para>
/// </summary>
public class CapabilityCatalogTests
{
    // ─── المَعجَم نَفسُه — مُثَبَّت بِعَدَدِه وبِأَسمائِه ─────────────

    /// <summary>
    /// <para><b>سِتّ قُدُرات، ولا سابِعَة.</b> تَغَيُّر هذه القائِمَة
    /// يَعني إضافَةَ حَدٍّ أَو سَحبَه — وكِلاهُما قَرار مُنتَج يَستَحِقّ
    /// نَظَرَ إنسان، لا تَعديلَ سَطر يَمُرّ في مُراجَعَة.</para>
    ///
    /// <para><b>ولِماذا لَم تَدخُل <c>studio.custom_pattern</c></b>:
    /// وَصَفَ تَصميمُ هذه الطَبَقَة رايَةً «تُباع ولا تُفحَص».
    /// القِياس يَنفيها: <c>AllowCustomPattern</c> سُحِبَ مِن
    /// <c>TierLimits</c> ومِن الشاشَتَين، ولَم يَبقَ مِنه إلّا
    /// تَعليق يَشرَح سَحبَه. المَعجَم يَصِف ما يُحَدّ
    /// <b>اليَوم</b>.</para>
    ///
    /// <para><b>والسابِعَةُ — <c>tenant.write</c></b> — دَخَلَت يَومَ
    /// ‏2026-08-23 (‏ADR-003)، وهي أَوَّلُ قُدرَةٍ <b>على المُستَأجِر لا
    /// على المُستَخدِم</b>: مَصدَرُ حَدِّها وَثيقَةُ <c>TenantPlan</c>،
    /// وتُفحَص في تَوقيعِ كُلّ نُقطَةِ كِتابَةٍ في مَتجَر، ولا تُباع
    /// في شاشَة.</para>
    ///
    /// <para><b>والسادِسَةُ الَّتي دَخَلَت — <c>api.call</c></b> —
    /// دَخَلَت بِالشَرط نَفسِه مَقلوباً: <b>تُفحَص ولا تُباع</b>.
    /// <c>ApiKeyFilter</c> يَسأَلُها على كُلّ نُقطَةٍ تَحتَ
    /// <c>/api/v1</c>، ولا تُذكَر في شاشَةِ باقاتٍ واحِدَة.</para>
    /// </summary>
    [Fact]
    public void Exactly_seven_capabilities_and_they_are_these()
        => Assert.Equal(
            new[]
            {
                "api.call",
                "listing.create",
                "studio.analyze",
                "studio.build",
                "studio.export",
                "studio.refine",
                "tenant.write",
            },
            CapabilityCatalog.Codes);

    /// <summary>الثَوابِت المُعلَنَة هي عَينُ ما في القائِمَة — فَلا
    /// يَنحَرِف ثابِتٌ عَن مَدخَلَتِه.</summary>
    [Fact]
    public void The_constants_are_the_catalog_entries()
    {
        Assert.Equal(
            new[]
            {
                CapabilityCatalog.ApiCall,
                CapabilityCatalog.ListingCreate,
                CapabilityCatalog.StudioAnalyze,
                CapabilityCatalog.StudioBuild,
                CapabilityCatalog.StudioExport,
                CapabilityCatalog.StudioRefine,
                CapabilityCatalog.TenantWrite,
            },
            CapabilityCatalog.Codes);
    }

    /// <summary><c>studio.custom_pattern</c> لا يَعود إلّا بِقَرار —
    /// وهذا الاختِبار هو ما يُحَمِّر عَودَتَه صامِتَةً.</summary>
    [Fact]
    public void The_withdrawn_custom_pattern_capability_is_not_in_the_vocabulary()
    {
        Assert.False(CapabilityCatalog.Contains("studio.custom_pattern"));
        Assert.DoesNotContain("studio.custom_pattern", CapabilityCatalog.Codes);
    }

    /// <summary>كُلّ نَوع حَدّ مِن مَعجَمِه المُغلَق، وكُلّ قُدرَة
    /// تُعلِن مَصدَرَها المَقيس — الشَرط الَّذي يَمنَع نُمُوَّ المَعجَم
    /// بِالخَيال.</summary>
    [Fact]
    public void Every_capability_declares_a_known_kind_and_a_measured_source()
    {
        foreach (var c in CapabilityCatalog.All)
        {
            Assert.True(CapabilityKinds.Contains(c.Kind),
                $"القُدرَة «{c.Code}» تُعلِن نَوعاً خارِج المَعجَم: «{c.Kind}».");
            Assert.False(string.IsNullOrWhiteSpace(c.SourceRef),
                $"القُدرَة «{c.Code}» بِلا مَصدَر مَقيس.");
        }
    }

    /// <summary>أَربَع حِصَص ورايَتان، ولا تُستَهلَك رايَة:
    /// <c>api.call</c> رايَةٌ لِأَنّ حِصَّتَها العَدَدِيَّة تَحتاج
    /// رَقماً لا وُجودَ لَه (القاعِدَة ١٦)، و<c>studio.export</c>
    /// رايَةٌ لِأَنّ مَصدَرَها <c>AllowExport</c> ثُنائيٌّ
    /// أَصلاً.</summary>
    [Fact]
    public void Four_are_quotas_and_exactly_three_are_flags()
    {
        Assert.Equal(
            new[] { "listing.create", "studio.analyze", "studio.build", "studio.refine" },
            CapabilityCatalog.All.Where(c => c.Kind == CapabilityKinds.Quota)
                .Select(c => c.Code).ToArray());

        Assert.Equal(
            new[] { "api.call", "studio.export", "tenant.write" },
            CapabilityCatalog.All.Where(c => c.Kind == CapabilityKinds.Flag)
                .Select(c => c.Code).ToArray());

        Assert.True(CapabilityCatalog.IsQuota("listing.create"));
        Assert.False(CapabilityCatalog.IsQuota("studio.export"));
        Assert.False(CapabilityCatalog.IsQuota("api.call"));
        Assert.False(CapabilityCatalog.IsQuota("tenant.write"));
        Assert.False(CapabilityCatalog.IsQuota("nope"));

        // ونِطاقٌ ثانٍ دَخَلَ مَعَها (‏ADR-003): سِتٌّ على المُستَخدِم
        // وواحِدَةٌ على المُستَأجِر. والفَرقُ لَيسَ تَصنيفاً: المُرَشِّحُ
        // يَشتَرِط جَلسَةً لِلأولى ولا يَشتَرِطُها لِلثانِيَة.
        Assert.True(CapabilityCatalog.IsTenantScoped("tenant.write"));
        Assert.False(CapabilityCatalog.IsTenantScoped("listing.create"));
        Assert.False(CapabilityCatalog.IsTenantScoped("nope"));
        Assert.Equal(
            new[] { "tenant.write" },
            CapabilityCatalog.All.Where(c => c.Scope == CapabilityScopes.Tenant)
                .Select(c => c.Code).ToArray());
    }

    /// <summary>المَعجَم بِلا تَكرار، ومُرَتَّب أَبجَدِيّاً —
    /// فَالمُقارَنَة بِه حَتمِيَّة.</summary>
    [Fact]
    public void The_vocabulary_is_distinct_and_ordinally_sorted()
    {
        Assert.Equal(CapabilityCatalog.Codes.Distinct(StringComparer.Ordinal), CapabilityCatalog.Codes);
        Assert.Equal(
            CapabilityCatalog.Codes.OrderBy(c => c, StringComparer.Ordinal).ToArray(),
            CapabilityCatalog.Codes);
    }

    // ─── رُموز الخَرق: مُوجِب وسالِب لِكُلٍّ ──────────────────────────

    [Fact]
    public void Every_catalog_member_validates_clean()
    {
        foreach (var code in CapabilityCatalog.Codes)
        {
            Assert.Empty(CapabilityCatalog.Validate(code));
            Assert.True(CapabilityCatalog.IsValid(code));
            Assert.Equal(code, CapabilityCatalog.Require(code));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Capability_empty_fires_on_blank(string? capability)
        => Assert.Equal(
            new[] { "capability_empty" },
            CapabilityCatalog.Validate(capability).Select(v => v.Code).ToArray());

    [Theory]
    [InlineData("listing.publish_everywhere")]   // مُختَلَق
    [InlineData("studio.custom_pattern")]        // سُحِبَ بِقَرار
    [InlineData("Listing.Create")]               // حالَة الحَرف تَهُمّ
    [InlineData("listing.create ")]              // مَسافَة لاحِقَة
    [InlineData("chat.start")]                   // صَلاحِيَّة دَور لا قُدرَة باقَة
    public void Capability_out_of_vocabulary_fires_on_everything_else(string capability)
    {
        Assert.Equal(
            new[] { "capability_out_of_vocabulary" },
            CapabilityCatalog.Validate(capability).Select(v => v.Code).ToArray());
        Assert.False(CapabilityCatalog.IsValid(capability));
    }

    /// <summary><b>الخَلط الَّذي يَمنَعُه المَعجَم</b>: صَلاحِيّات
    /// الأَدوار تُشبِه القُدُرات شَكلاً — و<c>listing.create</c> عُضوٌ في
    /// المَعجَمَين مَعاً. تَشابُهُ الشَكل هو بِعَينِه سَبَبُ إغلاق
    /// الطَرَفَين: <c>chat.start</c> صَلاحِيَّة ولَيسَت حِصَّةً تُشترى،
    /// و<c>studio.analyze</c> حِصَّة ولَيسَت صَلاحِيَّةَ دَور.</summary>
    [Fact]
    public void Role_permissions_and_capabilities_overlap_in_exactly_one_code()
    {
        var shared = ACommerce.Kit.Roles.PermissionCatalog.All
            .Intersect(CapabilityCatalog.Codes, StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "listing.create" }, shared);
    }

    // ─── الطَرَف الَّذي تَرَكَه PermissionCatalog مَفتوحاً ─────────────

    /// <summary>
    /// <para><b>يَرمي عِندَ التَركيب لا عِندَ الطَلَب</b> — فَرَمز
    /// مَجهول يُفشِل الإقلاع بِرِسالَتِه، ولا يَمُرّ صامِتاً لِيُكتَشَف
    /// مِن سِجِلّ خَطَأ لَيلاً.</para>
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("listing.publsh")]               // خَطَأ إملائيّ
    [InlineData("studio.custom_pattern")]
    public void Require_throws_on_anything_outside_the_vocabulary(string capability)
    {
        var ex = Assert.Throws<ArgumentException>(() => CapabilityCatalog.Require(capability));
        Assert.Contains("capability_", ex.Message);
    }

    /// <summary>والرِسالَة تَحمِل المَعجَم كامِلاً — فَمَن أَخطَأَ
    /// يَقرَأ البَديل في نَفس السَطر بَدَل أَن يَبحَث عَنه.</summary>
    [Fact]
    public void The_out_of_vocabulary_message_names_the_whole_vocabulary()
    {
        var msg = CapabilityCatalog.Validate("listing.publsh").Single().MessageAr;
        foreach (var code in CapabilityCatalog.Codes)
            Assert.Contains(code, msg);
    }
}
