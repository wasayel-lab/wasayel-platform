using ACommerce.Kit.Subscriptions;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ باقَةُ المُستَأجِر في وَسايِل — الاشتِقاقُ هُوَ الحَدّ ═══════════
//
// **لِماذا الاشتِقاقُ مِن الوَقت لا حالَةٌ مَحفوظَة**: الحالَةُ
// المَحفوظَةُ تَحتاج وَظيفَةً دَورِيَّةً تَقلِبُها عِندَ الانتِهاء —
// وتِلكَ آلَةٌ إن تَوَقَّفَت بَقِيَ مَتجَرٌ مُنتَهٍ يَكتُب شَهراً بِلا
// أَن يَشتَكِيَ فاحِص. وهذِه الاختِبارات هي البُرهانُ الوَحيدُ المُتاح
// بِلا قاعِدَةِ بَيانات: كُلُّ دالَّةٍ هُنا نَقِيَّةٌ والوَقتُ يُمَرَّر.

public class TenantPlanPolicyTests
{
    private static readonly DateTime Now = new(2026, 08, 23, 12, 0, 0, DateTimeKind.Utc);

    private static TenantPlan Plan(int daysToExpiry, int grace = 14,
        string status = "active", string planId = "manual") => new()
    {
        Id        = "demo",
        PlanId    = planId,
        Status    = status,
        StartsAt  = Now.AddDays(-30),
        ExpiresAt = Now.AddDays(daysToExpiry),
        GraceDays = grace,
        Price     = 100m,
    };

    // ─── التَكافُؤُ الصِفريّ ─────────────────────────────────────────

    /// <summary><b>هذا هُوَ العَقد</b>: كُلُّ مَتجَرٍ قائِمٍ اليَومَ بِلا
    /// وَثيقَةِ باقَة. فَلَو أَعطى <c>null</c> حالَةً غَيرَ
    /// <see cref="TenantPlanState.None"/> لَأُغلِقَت المَنَصَّةُ كُلُّها
    /// في أَوَّل نَشر.</summary>
    [Fact]
    public void NoPlanDocument_MeansNoneAndEverythingOpen()
    {
        Assert.Equal(TenantPlanState.None, TenantPlanPolicy.Derive(null, Now));
        Assert.True(TenantPlanPolicy.AllowsWrite(TenantPlanState.None));
        Assert.True(TenantPlanPolicy.IsVisible(TenantPlanState.None));
        Assert.Null(TenantPlanPolicy.HiddenAt(null));
    }

    // ─── الاشتِقاق ───────────────────────────────────────────────────

    [Fact]
    public void BeforeExpiry_ItIsActive()
        => Assert.Equal(TenantPlanState.Active, TenantPlanPolicy.Derive(Plan(+1), Now));

    /// <summary>وحَدُّ الانتِهاءِ نَفسُه سارٍ — <c>&lt;=</c> لا
    /// <c>&lt;</c>: يَومُ الانتِهاءِ مَدفوعٌ ثَمَنُه.</summary>
    [Fact]
    public void OnTheExpiryInstant_ItIsStillActive()
        => Assert.Equal(TenantPlanState.Active, TenantPlanPolicy.Derive(Plan(0), Now));

    [Theory]
    [InlineData(-1)]
    [InlineData(-13)]
    [InlineData(-14)]
    public void WithinGrace_ItIsGrace(int days)
        => Assert.Equal(TenantPlanState.Grace, TenantPlanPolicy.Derive(Plan(days), Now));

    [Theory]
    [InlineData(-15)]
    [InlineData(-400)]
    public void PastGrace_ItIsSuspended(int days)
        => Assert.Equal(TenantPlanState.Suspended, TenantPlanPolicy.Derive(Plan(days), Now));

    /// <summary>ومُهلَةٌ صِفريَّةٌ تَعني إخفاءً مِن يَومِ الانتِهاء —
    /// بِلا مَرحَلَةِ قِراءَة.</summary>
    [Fact]
    public void ZeroGrace_SkipsTheReadOnlyPhaseEntirely()
    {
        Assert.Equal(TenantPlanState.Active,    TenantPlanPolicy.Derive(Plan(0, grace: 0), Now));
        Assert.Equal(TenantPlanState.Suspended, TenantPlanPolicy.Derive(Plan(-1, grace: 0), Now));
    }

    /// <summary>والإيقافُ اليَدَوِيُّ <b>يَسبِق</b> حِسابَ التَواريخ —
    /// فَإيقافُ باقَةٍ سارِيَةٍ يَقَع مِن لَحظَتِه.</summary>
    [Fact]
    public void AStoppedPlan_IsSuspendedEvenWhileItsDatesAreValid()
        => Assert.Equal(TenantPlanState.Suspended,
            TenantPlanPolicy.Derive(Plan(+30, status: PlatformPlanStatuses.Stopped), Now));

    // ─── ما تَعنيه كُلّ حالَة ────────────────────────────────────────

    /// <summary><b>هذا هُوَ قَرارُ المالِك حَرفاً</b>: «قِراءَةٌ فَقَط
    /// لِمُدَّةِ `GraceDays` — كُلّ كِتابَةٍ تُرفَض».</summary>
    [Fact]
    public void Grace_ReadsYes_WritesNo()
    {
        Assert.False(TenantPlanPolicy.AllowsWrite(TenantPlanState.Grace));
        Assert.True(TenantPlanPolicy.IsVisible(TenantPlanState.Grace));
    }

    [Fact]
    public void Suspended_HidesTheStore_AndStillWritesNothing()
    {
        Assert.False(TenantPlanPolicy.AllowsWrite(TenantPlanState.Suspended));
        Assert.False(TenantPlanPolicy.IsVisible(TenantPlanState.Suspended));
    }

    [Fact]
    public void Active_IsFullyOpen()
    {
        Assert.True(TenantPlanPolicy.AllowsWrite(TenantPlanState.Active));
        Assert.True(TenantPlanPolicy.IsVisible(TenantPlanState.Active));
    }

    [Fact]
    public void HiddenAt_IsExpiryPlusGrace()
        => Assert.Equal(Now.AddDays(+10).AddDays(14),
            TenantPlanPolicy.HiddenAt(Plan(+10, grace: 14)));

    // ─── البَوّابَة ──────────────────────────────────────────────────

    [Fact]
    public void AValidPlan_PassesTheValidator()
        => Assert.True(TenantPlanPolicy.IsValid(Plan(+30)));

    [Fact]
    public void APlanOutsideTheCatalog_IsRefused()
        => Assert.Contains(TenantPlanPolicy.Validate(Plan(+30, planId: "enterprise")),
            v => v.Code == TenantPlanPolicy.PlanUnknown);

    [Fact]
    public void AnUnknownStatus_IsRefused()
        => Assert.Contains(TenantPlanPolicy.Validate(Plan(+30, status: "paused")),
            v => v.Code == TenantPlanPolicy.StatusUnknown);

    /// <summary>باقَةٌ تَنتَهي قَبلَ أَن تَبدَأ — والمُشرِفُ يَكتُب
    /// التاريخَينِ بِيَدِه، فَقَلبُهُما خَطَأُ إدخالٍ لا حالَةٌ
    /// نادِرَة.</summary>
    [Fact]
    public void AnInvertedPeriod_IsRefused()
    {
        var p = Plan(+30);
        p.ExpiresAt = p.StartsAt.AddDays(-1);
        Assert.Contains(TenantPlanPolicy.Validate(p), v => v.Code == TenantPlanPolicy.PeriodInvalid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(91)]
    public void GraceOutsideTheRange_IsRefused(int grace)
        => Assert.Contains(TenantPlanPolicy.Validate(Plan(+30, grace: grace)),
            v => v.Code == TenantPlanPolicy.GraceNegative);

    [Fact]
    public void ANegativePrice_IsRefused()
    {
        var p = Plan(+30);
        p.Price = -1m;
        Assert.Contains(TenantPlanPolicy.Validate(p), v => v.Code == TenantPlanPolicy.PriceNegative);
    }

    // ─── قِراءَةُ النَموذَج — دالَّةٌ نَقِيَّةٌ لا تَعرِف HTTP ───────

    [Fact]
    public void ReadSetting_ParsesWhatTheAdminTyped()
    {
        var (planId, starts, expires, grace, price) = TenantPlanPolicy.ReadSetting(
            " manual ", "2026-09-01", "2026-12-01", "30", "1500.50", Now);

        Assert.Equal("manual", planId);
        Assert.Equal(new DateTime(2026, 9, 1), starts.Date);
        Assert.Equal(new DateTime(2026, 12, 1), expires.Date);
        Assert.Equal(30, grace);
        Assert.Equal(1500.50m, price);
    }

    /// <summary><b>وانتِهاءٌ غائِبٌ لا يُخترَعُ لَه تاريخ</b>: يَسقُط إلى
    /// <c>MinValue</c> فَيُرَدّ بِـ<see cref="TenantPlanPolicy.PeriodInvalid"/>.
    /// و«سَنَةٌ افتِراضِيَّة» كانَت سَتُعطي مَتجَراً باقَةً لَم يَدفَع
    /// ثَمَنَها.</summary>
    [Fact]
    public void AMissingExpiry_FallsToSomethingTheValidatorRefuses()
    {
        var (_, _, expires, grace, price) =
            TenantPlanPolicy.ReadSetting("manual", null, null, null, null, Now);

        Assert.Equal(DateTime.MinValue, expires);
        Assert.Equal(PlatformPlanCatalog.DefaultGraceDays, grace);
        Assert.Equal(0m, price);
    }

    // ─── الكاتالوج بَياناتٌ لا كود ───────────────────────────────────

    /// <summary>يُحَمَّل مِن مِلَفٍّ مُضَمَّن على نَمَط
    /// <c>*.role.json</c>، ويَبدَأ بِواحِدَةٍ لا أَكثَر — <b>ولا سِعرَ
    /// فيها</b> (القاعِدَة ١٦).</summary>
    [Fact]
    public void TheCatalog_LoadsFromItsEmbeddedFiles()
    {
        Assert.NotEmpty(PlatformPlanCatalog.All);
        Assert.Contains("manual", PlatformPlanCatalog.Slugs);
        Assert.True(PlatformPlanCatalog.Contains("manual"));
        Assert.False(PlatformPlanCatalog.Contains("enterprise"));
    }

    [Fact]
    public void EveryCatalogEntry_CarriesArabicAndASaneGrace()
    {
        foreach (var d in PlatformPlanCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(d.LabelAr), $"«{d.Slug}» بِلا تَسمِيَة عَرَبِيَّة.");
            Assert.InRange(d.DefaultGraceDays, 0, TenantPlanPolicy.MaxGraceDays);
        }
    }

    /// <summary>مُهلَةُ السَماحِ الافتِراضِيَّةُ <b>أَربَعَةَ عَشَرَ
    /// يَوماً</b> — قَرارُ صَباحٍ مَكتوب. تَغييرُها قَرارٌ يُرى، لا
    /// انزِياحٌ صامِت.</summary>
    [Fact]
    public void TheDefaultGrace_IsFourteenDays()
    {
        Assert.Equal(14, PlatformPlanCatalog.DefaultGraceDays);
        Assert.Equal(14, PlatformPlanCatalog.Find("manual")!.DefaultGraceDays);
    }

    /// <summary>ورَمزا الحالَة يُقرَآنِ في وَثائِقَ مُخَزَّنَة —
    /// فَيُثَبَّتان.</summary>
    [Fact]
    public void TheStatusVocabulary_IsPinned()
    {
        Assert.Equal("active",  PlatformPlanStatuses.Active);
        Assert.Equal("stopped", PlatformPlanStatuses.Stopped);
        Assert.Equal(2, PlatformPlanStatuses.All.Count);
        Assert.False(PlatformPlanStatuses.Contains("paused"));
    }
}

// ═══ الاستِحقاقُ على مُستَوى المَتجَر — الأُنبوبُ القائِمُ يَحمِلُه ═══

public class TenantPlanEntitlementsTests
{
    private static readonly DateTime Now = new(2026, 08, 23, 12, 0, 0, DateTimeKind.Utc);

    private static TenantPlan Plan(int daysToExpiry, int grace = 14) => new()
    {
        Id = "demo", PlanId = "manual", Status = PlatformPlanStatuses.Active,
        StartsAt = Now.AddDays(-30), ExpiresAt = Now.AddDays(daysToExpiry), GraceDays = grace,
    };

    private static EntitlementResult Decide(TenantPlan? plan)
        => TenantPlanEntitlements.Decide(plan, CapabilityCatalog.TenantWrite, Now);

    /// <summary><b>التَكافُؤُ الصِفريّ عِندَ الحارِس نَفسِه</b>: كُلُّ
    /// مَتجَرٍ قائِمٍ بِلا وَثيقَةِ باقَة، فَلَو رَدَّ هذا السَطرُ
    /// مَنعاً لَأُغلِقَت المَنَصَّةُ كُلُّها في أَوَّل نَشر.</summary>
    [Fact]
    public void NoPlan_Allows()
    {
        var r = Decide(null);
        Assert.True(r.Allowed);
        Assert.Null(r.ReasonAr);
    }

    [Fact]
    public void ActivePlan_Allows()
        => Assert.True(Decide(Plan(+1)).Allowed);

    /// <summary>وفي السَماحِ يُمنَع — <b>مَعَ سَبَبٍ مَقروء</b>. رَفضٌ
    /// بِلا سَبَبٍ يَبدو عُطلاً.</summary>
    [Fact]
    public void InGrace_Denies_WithAReason()
    {
        var r = Decide(Plan(-1));
        Assert.False(r.Allowed);
        Assert.False(string.IsNullOrWhiteSpace(r.ReasonAr));
    }

    [Fact]
    public void PastGrace_Denies()
        => Assert.False(Decide(Plan(-30)).Allowed);

    /// <summary>ورايَةٌ لا حِصَّة: الرَصيدُ «بِلا حَدّ» دائِماً — فَلا
    /// شاشَةَ تَعرِض عَدّاداً لِشَيءٍ لا يُعَدّ.</summary>
    [Fact]
    public void ItIsAFlag_NotAQuota()
    {
        Assert.Equal(Entitlements.Unlimited, Decide(Plan(+1)).Remaining);
        Assert.False(CapabilityCatalog.IsQuota(CapabilityCatalog.TenantWrite));
    }

    /// <summary><b>ولا يَخدِمُ ما لَيسَ لَه</b>: «سَمَحتُ لِأَنّي لا
    /// أَعرِف» هُوَ عَينُ العَطَب الَّذي يَرميه
    /// <c>SubscriptionEntitlements</c> — ونَفسُ الحارِسِ هُنا.</summary>
    [Fact]
    public async Task ItRefusesCapabilitiesItDoesNotServe()
    {
        var sut = new TenantPlanEntitlements(null!);
        Assert.Single(sut.Handles);
        Assert.Contains(CapabilityCatalog.TenantWrite, sut.Handles);
        await Assert.ThrowsAsync<NotSupportedException>(
            () => sut.PeekAsync("demo", Guid.Empty, CapabilityCatalog.ListingCreate));
    }
}
