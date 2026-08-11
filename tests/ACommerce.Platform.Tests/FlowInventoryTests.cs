using ACommerce.Platform.Flows;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>الوَصف يُطابِق الكود</b> — كُلّ مِلَفّ <c>*.flow.json</c>
/// يَصِف تَدَفُّقاً قائِماً، وهذه الاختِبارات تُثَبِّت أَنّ الوَصف
/// <b>لَم يَنحَرِف</b> عَنه: مَجموعَة الحالات في المِلَفّ = قيَم التَعداد
/// أَو ثَوابِت النَصّ، حَرفاً وعَدَداً.</para>
///
/// <para><b>الحَدّ مُعلَناً</b>: <b>لا شَيء في وَقت التَشغيل يَقرَأ هذه
/// المِلَفّات لِيُقَرِّر</b> — عَدا <c>tenant_role</c> وَ<c>tenant_theme</c>
/// بَعدَ تَوحيدِهِما. قيمَة البَقيَّة اليَوم: تَوثيق مَقيس، ومُصادَقَة
/// تَكشِف ما لا تَراه العَين، وشَكل جاهِز. الاعتِماد يَأتي تَدَفُّقاً
/// تَدَفُّقاً بِتَوصيف يَخضَرّ أَوَّلاً.</para>
///
/// <para><b>ولِماذا تُثَبَّت الخُروق بَدَل أَن تُصَحَّح</b>: المِلَفّات
/// تَصِف <b>الواقِع</b>، والواقِع فيه خَمس حالات مَيِّتَة. تَصحيحُها
/// في المِلَفّ كانَ سَيُنتِج وَثيقَةً جَميلَةً كاذِبَة؛ وحَذفُها كانَ
/// سَيُخفي المُشكِلَة. الثالِثَة — وهي المُختارَة — أَن يُوصَف الواقِع
/// كَما هو، ويُثَبَّت خَرقُه بِرَمزِه: فَإن أُصلِحَ الكود يَحمَرّ هذا
/// الاختِبار ويُطالِب بِتَحديث الوَصف. <b>الخَرق المُثَبَّت دَين
/// مَرئيّ؛ والخَرق المَحذوف دَين مَنسيّ.</b></para>
/// </summary>
public class FlowInventoryTests
{
    private static IReadOnlyList<string> CodesOf(string flow)
        => FlowDefinitionValidator.Validate(FlowDefinitionLoader.Load(flow))
            .Select(v => v.Code).OrderBy(c => c, StringComparer.Ordinal).ToArray();

    private static IReadOnlyList<string> StatesOf(string flow)
        => FlowDefinitionLoader.Load(flow).StateKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    private static IReadOnlyList<string> EnumNames<T>() where T : struct, Enum
        => Enum.GetNames<T>().OrderBy(n => n, StringComparer.Ordinal).ToArray();

    // ─── كُلّ المِلَفّات تُقرَأ ─────────────────────────────────────────

    [Fact]
    public void Every_described_flow_parses_and_names_itself_after_its_file()
    {
        var names = FlowDefinitionLoader.Names();
        Assert.NotEmpty(names);

        foreach (var name in names)
        {
            var d = FlowDefinitionLoader.Load(name);
            Assert.Equal(name, d.Flow);              // اسم المِلَفّ = اسم التَدَفُّق
            Assert.NotEmpty(d.States);
            Assert.NotEmpty(d.Transitions);
        }
    }

    /// <summary>الجَرد وَصَفَ عَشَرَة تَدَفُّقات. تَغَيُّر العَدَد
    /// يَعني إضافَةً أَو حَذفاً — كِلاهُما يَستَحِقّ نَظَرَ إنسان.</summary>
    [Fact]
    public void Ten_flows_are_described()
    {
        Assert.Equal(
            new[]
            {
                "agent_tool_call", "deal_status", "incubator", "offer", "report",
                "subscription", "tenant_role", "tenant_theme", "ticket", "trip",
            },
            FlowDefinitionLoader.Names());
    }

    // ─── مُقابَلَة الحالات بِالكود ─────────────────────────────────────

    [Fact]
    public void Offer_states_match_the_enum()
        => Assert.Equal(EnumNames<ACommerce.Kit.Offers.OfferStatus>(), StatesOf("offer"));

    [Fact]
    public void Trip_states_match_the_enum()
        => Assert.Equal(EnumNames<ACommerce.Kit.Offers.TripStatus>(), StatesOf("trip"));

    [Fact]
    public void Report_states_match_the_enum()
        => Assert.Equal(EnumNames<ACommerce.Kit.Reports.ReportStatus>(), StatesOf("report"));

    [Fact]
    public void Deal_status_states_match_the_enum()
        => Assert.Equal(EnumNames<DealStatus>(), StatesOf("deal_status"));

    [Fact]
    public void Incubator_states_match_the_enum()
        => Assert.Equal(EnumNames<IncubatorStatus>(), StatesOf("incubator"));

    [Fact]
    public void Tenant_role_states_match_the_closed_vocabulary()
        => Assert.Equal(
            ACommerce.Kit.Roles.TenantRoleStatuses.All.OrderBy(s => s, StringComparer.Ordinal).ToArray(),
            StatesOf("tenant_role"));

    [Fact]
    public void Tenant_theme_states_match_the_closed_vocabulary()
        => Assert.Equal(
            ACommerce.Kit.Theme.TenantThemeStatuses.All.OrderBy(s => s, StringComparer.Ordinal).ToArray(),
            StatesOf("tenant_theme"));

    /// <summary>لا مَعجَم مُغلَق لِهذَين — القيَم مَكتوبَة نَصّاً في
    /// الكود، فَالمُقابَلَة بِالقيَم المُوَثَّقَة في التَعليق. وهذا
    /// بِعَينِه ما تُصلِحُه اللُغَة حينَ تُعتَمَد.</summary>
    [Fact]
    public void Ticket_and_subscription_states_match_their_documented_string_values()
    {
        Assert.Equal(new[] { "answered", "closed", "open" }, StatesOf("ticket"));
        Assert.Equal(new[] { "active", "cancelled", "expired" }, StatesOf("subscription"));
    }

    /// <summary>أَربَع حالات لا ثَلاث، و<c>applied</c> لا
    /// <c>approved</c> — التَصحيح المَقيس في §٣ مِن الجَرد.</summary>
    [Fact]
    public void Agent_tool_call_has_four_states_and_is_not_an_approval_flow()
    {
        Assert.Equal(new[] { "applied", "error", "pending", "rejected" }, StatesOf("agent_tool_call"));
        Assert.DoesNotContain("approved", StatesOf("agent_tool_call"));
    }

    // ─── التَدَفُّقات السَليمَة: صِفر خَرق ──────────────────────────────

    [Theory]
    [InlineData("tenant_role")]
    [InlineData("tenant_theme")]
    [InlineData("agent_tool_call")]
    [InlineData("trip")]
    [InlineData("deal_status")]
    [InlineData("ticket")]
    public void Sound_flows_validate_clean(string flow)
    {
        var codes = CodesOf(flow);
        Assert.True(codes.Count == 0,
            $"التَدَفُّق «{flow}» أَطلَقَ خُروقاً: {string.Join(" | ", codes)}");
    }

    // ─── الخُروق المُثَبَّتَة: دَين مَرئيّ لا مَنسيّ ────────────────────

    /// <summary><c>Expired</c> حالَة مُعلَنَة في التَعداد ولا حَدَث
    /// يَكتُبُها: <c>OfferExpired</c> مُعَرَّف ولَه <c>Apply</c> ولا
    /// مُصدِر لَه في المُستَودَع. الانتِهاء يُعرَض بِمُقارَنَة
    /// <c>ExpiresAt &gt; now</c> في الواجِهَة.</summary>
    [Fact]
    public void Offer_expired_is_declared_but_never_written()
    {
        Assert.Equal(new[] { "state_unreachable" }, CodesOf("offer"));
        var d = FlowDefinitionLoader.Load("offer");
        Assert.True(d.HasState("Expired"));
        Assert.DoesNotContain("Expired", d.ReachableStates());
        Assert.DoesNotContain(d.Transitions, t => t.To == "Expired");
    }

    /// <summary>
    /// <para><c>UnderReview</c> تُقرَأ في مُرَشِّح استِعلام واحِد
    /// (<c>ReportHandlers.cs:15</c>) ولا سَطر يُسنِدُها.</para>
    ///
    /// <para><b>وخَرقان لا واحِد</b> — والثاني لَم يَكُن مُتَوَقَّعاً
    /// عِندَ كِتابَة الوَصف، وكَشَفَهُ المُصادِق: الحالَة لَيسَت
    /// نِهائيَّة ولا مَخرَج لَها. أَي أَنَّها لَو بُلِغَت يَوماً
    /// <b>لَعَلِقَ التَبليغ فيها إلى الأَبَد</b>. عَيبانِ مُستَقِلّان
    /// في حالَة واحِدَة.</para>
    /// </summary>
    [Fact]
    public void Report_under_review_is_read_never_written_and_would_be_a_trap()
    {
        Assert.Equal(new[] { "dead_end_not_declared", "state_unreachable" }, CodesOf("report"));
        Assert.DoesNotContain("UnderReview", FlowDefinitionLoader.Load("report").ReachableStates());
    }

    /// <summary><c>Abandoned</c> لا يَكتُبُها أَحَد ولا يَقرَؤُها أَحَد.
    /// و<c>Failed</c> لَيسَت نِهائيَّة (يُعاد التَحليل مِنها) فَلا
    /// تُطلِق خَرقاً.</summary>
    [Fact]
    public void Incubator_abandoned_is_entirely_dead()
    {
        Assert.Equal(new[] { "state_unreachable" }, CodesOf("incubator"));
        var d = FlowDefinitionLoader.Load("incubator");
        Assert.DoesNotContain("Abandoned", d.ReachableStates());
        Assert.Contains("Failed", d.ReachableStates());
    }

    /// <summary>
    /// <para>أَغلى خَرق مُثَبَّت: <c>SubscriptionCancelled</c> و
    /// <c>SubscriptionExpired</c> و<c>SubscriptionRenewed</c> أَحداث
    /// مُعَرَّفَة بِـ <c>Apply</c> كامِل، و<c>CancelSubscription</c>
    /// أَمر مُعَرَّف بِلا مُعالِج — فَلا مُصدِر لِواحِدٍ مِنها.</para>
    ///
    /// <para><b>ثَلاثَة خُروق</b>: الحالَتان المَيِّتَتان، <b>وثالِث
    /// كَشَفَهُ المُصادِق</b> — <c>active</c> نَفسُها طَريق مَسدود غَير
    /// مُعلَن. وهذا هو التَوصيف الدَقيق لِلعَطَب: لَيسَ «حالَتان لا
    /// تُبلَغان» بَل <b>اشتِراك يُنشَأ فَعّالاً ولا مَخرَج لَه إلى
    /// الأَبَد</b> — وهو ما تُؤَكِّدُه <c>MySubscription</c> إذ
    /// تُرَشِّح بِـ <c>Status == "active"</c> وَحدَها.</para>
    /// </summary>
    [Fact]
    public void Subscription_is_created_active_and_can_never_leave()
    {
        Assert.Equal(
            new[] { "dead_end_not_declared", "state_unreachable", "state_unreachable" },
            CodesOf("subscription"));
        Assert.Equal(new[] { "active" }, FlowDefinitionLoader.Load("subscription").ReachableStates());
    }

    /// <summary>الحَصر: ثَلاثَة تَدَفُّقات تَحمِل خُروقاً، وخَمس
    /// حالات مَيِّتَة مَجموعاً. هذا الرَقَم هو ما يَنقُص حينَ يُصلَح
    /// الكود.</summary>
    [Fact]
    public void Exactly_five_dead_states_across_three_flows()
    {
        var dead = FlowDefinitionLoader.LoadAll()
            .SelectMany(d => d.StateKeys
                .Except(d.ReachableStates(), StringComparer.Ordinal)
                .Select(s => $"{d.Flow}.{s}"))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "incubator.Abandoned",
                "offer.Expired",
                "report.UnderReview",
                "subscription.cancelled",
                "subscription.expired",
            },
            dead);
    }
}
