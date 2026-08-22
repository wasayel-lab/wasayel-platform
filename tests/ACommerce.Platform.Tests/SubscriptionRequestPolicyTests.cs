using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Subscriptions;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.Flows;
using ACommerce.Templates.Customer.Marketplace.Services;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>إغلاقُ تَسريب الباقَة المَجّانِيَّة</b> — والعَيبُ الَّذي
/// كَتَبَ هذا المِلَفّ مَقيسٌ لا مَظنون:
/// <c>POST /{slug}/plans/{planId}/subscribe</c> كانَ يُحَمِّل
/// <see cref="Plan"/>، <b>يَتَجاهَل <see cref="Plan.Price"/></b>،
/// ويَفتَح <see cref="SubscriptionCreated"/> لِأَيّ مُستَخدِمٍ
/// مُسَجَّلٍ بِنَقرَة. فَحِصَّةُ الإعلانات — وهي عَينُ ما يَحرُسُه
/// الاستِحقاقُ على <c>listings/create</c> — كانَت تُمنَح مَجّاناً مِن
/// زِرٍّ مَعروض، والمالِكُ يَقبِض حَوالاتٍ بَنكِيَّةً يَدَوِيَّة.</para>
///
/// <para><b>ولِماذا وَحَداتٌ نَقِيَّة لا نِداءٌ حَيّ</b>: قاعِدَةُ
/// البَيانات غَير مُتاحَة في هذِه الجَولَة (الاعتِمادُ مَرفوض
/// <c>28P01</c>)، والقَرارُ نَفسُه لا يَحتاجُها: هُوَ دالّاتٌ بِلا
/// Marten ولا وَقتٍ ولا عَشوائيَّة. وما لا تَبلُغُه هذِه الوَحَدات —
/// أَنّ الوَثيقَةَ تُخَزَّن فِعلاً وأَنّ المَجرى يُفتَح — مَقيسٌ
/// نَصِّيّاً في <see cref="WriteEndpointGuardTests"/> و
/// <see cref="AppliedEventEmitterTests"/>، ويَبقى البُرهانُ الحَيّ
/// دَيناً مُعلَناً لا مُدَّعى.</para>
/// </summary>
public class SubscriptionRequestPolicyTests
{
    private static Plan PlanWith(decimal price) => new()
    {
        Id = "pro", Name = "المُحتَرِف", Price = price,
        ListingsQuota = 50, DaysPeriod = 30, IsActive = true
    };

    private static SubscriptionRequest Pending(decimal price = 199m)
        => SubscriptionRequestPolicy.Open(
            PlanWith(price), Guid.NewGuid(), "أَبو خالِد", "أَبو خالِد · x",
            new DateTime(2026, 8, 22, 0, 0, 0, DateTimeKind.Utc),
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Guid.Parse("99999999-8888-7777-6666-555555555555"));

    // ─── ١. باقَةٌ بِسِعر: طَلَبٌ لا اشتِراك ──────────────────────────

    [Theory]
    [InlineData(0.01)]
    [InlineData(1)]
    [InlineData(199)]
    [InlineData(35000)]
    public void PricedPlan_OpensARequest_NotASubscription(decimal price)
    {
        Assert.Equal(SubscribeRoute.OpenRequest, SubscriptionRequestPolicy.Route(PlanWith(price)));
        Assert.True(SubscriptionRequestPolicy.RequiresApproval(PlanWith(price)));

        var request = Pending(price);
        Assert.Equal(SubscriptionRequestStatuses.Pending, request.Status);
        Assert.Null(request.DecidedBy);
        Assert.Null(request.DecidedAt);
    }

    /// <summary>ولَقطَةُ الباقَة تُحفَظ في الطَلَب — الاعتِمادُ يَقَع
    /// بَعدَ أَيّام، والسِعرُ والحِصَّةُ قَد يَتَغَيَّرانِ بَينَهُما.
    /// المَمنوحُ هُوَ ما دَفَعَ المُستَخدِمُ مُقابِلَه.</summary>
    [Fact]
    public void TheRequest_CarriesThePlanSnapshot()
    {
        var request = Pending(199m);
        Assert.Equal("pro", request.PlanId);
        Assert.Equal("المُحتَرِف", request.PlanName);
        Assert.Equal(199m, request.Price);
        Assert.Equal(50, request.ListingsQuota);
        Assert.Equal(30, request.DaysPeriod);
    }

    /// <summary>ورَقمُ المَرجِع حَتمِيّ بِمُدخَلِه — فَالعَشوائيَّةُ
    /// عِندَ النُقطَة، وهذا يُختَبَر.</summary>
    [Fact]
    public void TheReference_IsDeterministicAndReadable()
    {
        var seed = Guid.Parse("11111111-2222-3333-4444-555555555555");
        Assert.Equal("SR-11111111", SubscriptionRequestPolicy.NewReference(seed));
        Assert.Equal(SubscriptionRequestPolicy.NewReference(seed), Pending().Id);
        Assert.Matches(new Regex("^SR-[0-9A-F]{8}$"), Pending().Id);
    }

    // ─── ٢. باقَةٌ مَجّانِيَّة: تَبقى ذاتِيَّة ────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]   // سِعرٌ سالِبٌ يُعامَل مَجّانِيّاً لا مَدفوعاً
    public void FreePlan_StaysSelfServe(decimal price)
    {
        Assert.Equal(SubscribeRoute.GrantNow, SubscriptionRequestPolicy.Route(PlanWith(price)));
        Assert.False(SubscriptionRequestPolicy.RequiresApproval(PlanWith(price)));
    }

    // ─── ٣. الاعتِماد يَمنَح مَرَّةً واحِدَة ──────────────────────────

    [Fact]
    public void Approval_GrantsTheSubscription()
    {
        var request = Pending();
        var decision = SubscriptionRequestPolicy.Decide(request, SubscriptionRequestStatuses.Approved);

        Assert.True(decision.Ok);
        Assert.True(decision.Grants);

        var at = new DateTime(2026, 8, 23, 0, 0, 0, DateTimeKind.Utc);
        SubscriptionRequestPolicy.Stamp(request, SubscriptionRequestStatuses.Approved, "مُشرِف", at);
        Assert.Equal(SubscriptionRequestStatuses.Approved, request.Status);
        Assert.Equal("مُشرِف", request.DecidedBy);
        Assert.Equal(at, request.DecidedAt);
    }

    /// <summary><b>النَقرَةُ الثانِيَة لا تَمنَح حِصَّةً ثانِيَة.</b>
    /// وهذا هُوَ العَطَبُ الَّذي يُميتُ إغلاقَ التَسريب لَو أُهمِل:
    /// اعتِمادٌ يُكَرَّر = اشتِراكانِ بِحَوالَةٍ واحِدَة.</summary>
    [Fact]
    public void ASecondApproval_IsRefused()
    {
        var request = Pending();
        SubscriptionRequestPolicy.Stamp(
            request, SubscriptionRequestStatuses.Approved, "مُشرِف", DateTime.UtcNow);

        var again = SubscriptionRequestPolicy.Decide(request, SubscriptionRequestStatuses.Approved);
        Assert.False(again.Ok);
        Assert.False(again.Grants);
        Assert.Equal(SubscriptionRequestPolicy.AlreadyDecided, again.Code);
    }

    /// <summary>ومُعَرِّفُ المَجرى مِن الوَثيقَة لا مِن اللَحظَة —
    /// فَنِداءانِ يُعطِيانِ نَفسَ الحَدَث، وMarten يَرُدّ الثانِيَ
    /// بِمَجرىً قائِمٍ بَدَلَ أَن يَفتَحَ ثانِياً صامِتاً.</summary>
    [Fact]
    public void TheGrantedEvent_IsDerivedFromTheRequest_NotFromTheClick()
    {
        var request = Pending();
        var at = new DateTime(2026, 8, 23, 0, 0, 0, DateTimeKind.Utc);

        var first  = SubscriptionRequestPolicy.ToCreatedEvent(request, at);
        var second = SubscriptionRequestPolicy.ToCreatedEvent(request, at);

        Assert.Equal(first, second);
        Assert.Equal(request.SubscriptionId, first.Id);
        Assert.Equal(request.UserId, first.UserId);
        Assert.Equal(request.PlanId, first.PlanId);
        Assert.Equal(request.ListingsQuota, first.Quota);
        Assert.Equal(request.DaysPeriod, first.DaysPeriod);
    }

    // ─── ٤. الرَفض لا يُنشِئ شَيئاً ───────────────────────────────────

    [Fact]
    public void Rejection_GrantsNothing()
    {
        var request = Pending();
        var decision = SubscriptionRequestPolicy.Decide(request, SubscriptionRequestStatuses.Rejected);

        Assert.True(decision.Ok);
        Assert.False(decision.Grants);
    }

    [Fact]
    public void AMissingRequest_AndAnUnknownVerdict_AreRefused()
    {
        Assert.Equal(SubscriptionRequestPolicy.NotFound,
            SubscriptionRequestPolicy.Decide(null, SubscriptionRequestStatuses.Approved).Code);

        Assert.Equal(SubscriptionRequestPolicy.BadVerdict,
            SubscriptionRequestPolicy.Decide(Pending(), "applied").Code);

        // و«مُعَلَّق» لَيسَ قَراراً — لا انتِقالَ مِن pending إلى pending.
        Assert.Equal(SubscriptionRequestPolicy.BadVerdict,
            SubscriptionRequestPolicy.Decide(Pending(), SubscriptionRequestStatuses.Pending).Code);
    }

    /// <summary>والمَعجَمُ مُحالٌ لا مَنسوخ — لَو أُضيفَت حالَةٌ رابِعَة
    /// إلى <see cref="ApprovalFlow"/> عَرَفَها هذا المَسارُ مِن
    /// يَومِها.</summary>
    [Fact]
    public void TheStatusVocabulary_IsTheOneSharedDefinition()
    {
        Assert.Equal(ApprovalFlow.Pending,  SubscriptionRequestStatuses.Pending);
        Assert.Equal(ApprovalFlow.Approved, SubscriptionRequestStatuses.Approved);
        Assert.Equal(ApprovalFlow.Rejected, SubscriptionRequestStatuses.Rejected);
        Assert.Equal(ApprovalFlow.All,      SubscriptionRequestStatuses.All);
    }

    // ─── ٥. مُستَخدِمٌ عادِيّ لا يَعتَمِد طَلَبَ نَفسِه ────────────────

    /// <summary>
    /// <para><b>نِصفُ الحارِس النَقِيّ</b>: صاحِبُ الطَلَب دَورُه
    /// <c>customer</c> — لا <c>tenant.manage</c> فيه — فَلا يَجوز لَه
    /// إدارَةُ المَتجَر، ونُقطَةُ القَرار مَحروسَةٌ بِهذا القَرار
    /// بِعَينِه.</para>
    /// </summary>
    [Fact]
    public void AnOrdinaryUser_CannotAdminister_AndSoCannotApprove()
    {
        var tenant = new Tenant
        {
            Id = "ashare", Name = "عَشير", OwnerUserId = Guid.NewGuid(),
            Roles = new()
            {
                new Role { Slug = "customer",     Label = "عَميل",  Permissions = new() { "listings.create" } },
                new Role { Slug = "tenant_admin", Label = "مُشرِف", Permissions = new() { "tenant.manage" } },
            }
        };

        var requester = new User { Id = Guid.NewGuid(), TenantSlug = "ashare", ActiveRole = "customer" };
        var admin     = new User { Id = Guid.NewGuid(), TenantSlug = "ashare", ActiveRole = "tenant_admin" };

        Assert.False(TenantAdminGuard.HasTenantManage(tenant, requester));
        Assert.False(TenantAdminGuard.IsStudioOwner(tenant, requester.Id));
        Assert.True(TenantAdminGuard.HasTenantManage(tenant, admin));
    }

    /// <summary>
    /// <para><b>والحارِسُ مَوصولٌ بِالنُقطَة فِعلاً — يُقاس نَصّاً.</b>
    /// الوَحَدَةُ أَعلاه تُثبِت أَنّ القَرارَ يَرفُض العادِيّ؛ وهذِه
    /// تُثبِت أَنّ النُقطَةَ <b>تَسأَلُه</b>، وأَنَّها تَسأَلُه
    /// <b>قَبلَ</b> أَوَّل كِتابَة. وبِلا هذا السَطر كانَت الوَحَدَةُ
    /// أَعلاه تُصادِق على حارِسٍ لا يُنادى.</para>
    ///
    /// <para><b>ونَفسُ ماسِح <see cref="WriteEndpointGuardTests"/> لا
    /// ماسِحٌ ثانٍ</b> (القاعِدَة ٨).</para>
    /// </summary>
    [Fact]
    public void TheDecideEndpoint_AsksTheTenantAdminGuard_BeforeItWrites()
    {
        var endpoint = WriteEndpointGuardTests.AllMinimalApiEndpoints()
            .SingleOrDefault(e => e.Route ==
                "/admin/tenants/{slug}/subscriptions/{reference}/decide");

        Assert.False(endpoint is null,
            "أَداة عَمياء: لَم تُوجَد نُقطَةُ قَرار طَلَب الاشتِراك أَصلاً.");

        var guardAt = endpoint!.Body.IndexOf(
            "TenantAdminGuard.CanAdministerAsync", StringComparison.Ordinal);
        Assert.True(guardAt >= 0, "نُقطَةُ القَرار بِلا حارِس.");

        foreach (var write in new[] { "SaveChangesAsync", "Events.StartStream", ".Store(" })
        {
            var at = endpoint.Body.IndexOf(write, StringComparison.Ordinal);
            if (at < 0) continue;
            Assert.True(guardAt < at,
                $"الحارِسُ بَعدَ «{write}» — حارِسٌ بَعدَ الكِتابَة لَيسَ حارِساً.");
        }
    }

    /// <summary>
    /// <para><b>والمَسارُ العامّ لَم يَتَغَيَّر</b> — نَفسُ
    /// <c>POST /{slug}/plans/{planId}/subscribe</c> حَرفاً. كَسرُ
    /// العُنوان كانَ سَيَكسِر كُلّ نَموذَجٍ يُشير إلَيه، والإغلاقُ
    /// شَرطُه أَلّا يُكَسِّر.</para>
    /// </summary>
    [Fact]
    public void TheSubscribeRoute_IsUnchanged()
        => Assert.Contains(
            WriteEndpointGuardTests.AllMinimalApiEndpoints(),
            e => e.Route == "/{slug}/plans/{planId}/subscribe");
}
