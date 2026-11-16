using ACommerce.Kit.Notifications;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ حَسمُ النِزاعِ بِيَدِ صاحِبِ المَتجَر — قَرارُ المالِك د38 (‏2026-10-29؛ ‏ADR-047 §٢) ═══════════════════════════════════════════
//
// «من يحلّ النزاع؟ → صاحب المتجر» · «إلى أين تنتهي الصفقة؟ → الاثنان، ويختار صاحب المتجر: إلغاء مع تسجيل استرجاع مستحقّ إن كانت مدفوعة، أو
// استئناف من المرحلة نفسها» · «هل يُخبَر الطرفان؟ → نعم، إشعار لكل طرف بالنتيجة». وتَوصيفُ ما كانَ (طَريقٌ بِلا مَخرَج) `DealDisputeDeadEndTests`
// مُودَعٌ قَبلَه ومَقلوبٌ بِاسمَيه في كوميتِ هذا القَرار.
//
// **البَوّابَةُ** (`DealDisputeResolution`، عائِلَةُ «first» في ‏ADR-038): مُوجِبٌ وسالِبٌ لِكُلِّ رَمزٍ مِن الأَربَعَة، والتَرتيبُ. **والخِدمَةُ** عَلى عالَمِ
// الوَثائِقِ: الاستِئنافُ، والإلغاءُ بِأَثَرِه الماليِّ القائِم (استِرجاعٌ آلِيٌّ أَو «استِرجاعُه مُستَحَقّ»)، والنَقرَةُ الثانِيَة. **والإشعارُ** لِلطَرَفَين.
// **والبابُ والصَفحَة** بِمَصدَرِهِما.

public class DealDisputeResolutionTests(ITestOutputHelper output)
{
    private const string Slug = "zz-dispute-resolve";
    private static readonly Guid Buyer    = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller   = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid AppOwner = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    private static readonly DealCanceller Owner = new(AppOwner, "مالِك التَّطبيق", IsStoreAdmin: true);
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static T Now<T>(Task<T> task)
    {
        Assert.True(task.IsCompleted, "نِداءٌ لَم يَكتَمِل في عالَمٍ مُتَزامِن — الأَداةُ تَكذِب.");
        return task.GetAwaiter().GetResult();
    }

    private static Deal Disputed(DealStage stage = DealStage.Paid, DealStatus status = DealStatus.Disputed) => new()
    {
        Id = Guid.NewGuid(), TenantSlug = Slug, Pattern = "marketplace", Stage = stage, Status = status, AmountSar = 300m,
        ListingTitle = "سِلعَة", InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", CounterpartyId = Seller, CounterpartyName = "بائِع",
    };

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً.");
        return text;
    }

    // ═══ ١) البَوّابَةُ النَقِيَّة — مُوجِبٌ وسالِبٌ لِكُلِّ رَمز ═══════════════════════════════════════════

    [Fact]
    public void The_lexicons_are_closed_four_codes_and_two_outcomes()
    {
        Assert.Equal(new[] { "deal_not_found", "actor_not_store_admin", "deal_not_disputed", "outcome_unknown" }, DealDisputeResolution.All.ToArray());
        Assert.Equal(new[] { "cancel", "resume" }, DealDisputeResolution.Outcomes.ToArray());
        Assert.Equal(DealCancelAuthorization.DealNotFound, DealDisputeResolution.DealNotFound);
    }

    [Fact]
    public void DealNotFound_negative_no_deal_and_positive_a_stored_deal_passes_the_step()
    {
        Assert.Equal(DealDisputeResolution.DealNotFound, DealDisputeResolution.Validate(null, Owner, DealDisputeResolution.Resume)!.Code);
        Assert.Null(DealDisputeResolution.Validate(Disputed(), Owner, DealDisputeResolution.Resume));
    }

    [Theory]
    [InlineData("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa")]   // المُشتَري نَفسُه
    [InlineData("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb")]   // البائِعُ نَفسُه
    [InlineData("cccccccc-cccc-4ccc-8ccc-cccccccccccc")]   // صاحِبُ المَتجَرِ بِلا إثباتٍ مِن الحارِسِ الأَعلى
    public void ActorNotStoreAdmin_negative_a_party_or_an_unproven_owner_and_positive_the_proven_owner(string actor)
    {
        var deal = Disputed();
        var by = new DealCanceller(Guid.Parse(actor), "فاعِل", IsStoreAdmin: false);
        Assert.Equal(DealDisputeResolution.ActorNotStoreAdmin, DealDisputeResolution.Validate(deal, by, DealDisputeResolution.Cancel)!.Code);
        Assert.False(DealDisputeResolution.IsAllowed(deal, by, DealDisputeResolution.Cancel));
        Assert.True(DealDisputeResolution.IsAllowed(deal, Owner, DealDisputeResolution.Cancel));
    }

    [Theory]
    [InlineData(DealStatus.Active)]
    [InlineData(DealStatus.Cancelled)]
    [InlineData(DealStatus.Completed)]
    public void DealNotDisputed_negative_any_other_status_and_positive_disputed(DealStatus status)
    {
        Assert.Equal(DealDisputeResolution.DealNotDisputed,
            DealDisputeResolution.Validate(Disputed(status: status), Owner, DealDisputeResolution.Resume)!.Code);
        Assert.Null(DealDisputeResolution.Validate(Disputed(status: DealStatus.Disputed), Owner, DealDisputeResolution.Resume));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("refund")]
    [InlineData("Cancel")]
    public void OutcomeUnknown_negative_outside_the_lexicon_and_positive_each_outcome(string? outcome)
    {
        Assert.Equal(DealDisputeResolution.OutcomeUnknown, DealDisputeResolution.Validate(Disputed(), Owner, outcome)!.Code);
        foreach (var known in DealDisputeResolution.Outcomes)
            Assert.Null(DealDisputeResolution.Validate(Disputed(), Owner, known));
    }

    /// <summary>الغِيابُ ثُمَّ الهُوِيَّةُ ثُمَّ الحالَةُ ثُمَّ الحَقل (‏ADR-038، القاعِدَة ٦): غَيرُ صاحِبِ المَتجَرِ يُجابُ بِهُوِيَّتِه ولا يَعرِفُ حالَ صَفقَةٍ لَيسَت
    /// لَه، وحالُ الصَفقَةِ يَسبِقُ الحَقل.</summary>
    [Fact]
    public void The_gate_answers_absence_then_identity_then_status_then_the_field()
    {
        var party = new DealCanceller(Buyer, "مُشتَرٍ", IsStoreAdmin: false);
        Assert.Equal(DealDisputeResolution.DealNotFound, DealDisputeResolution.Validate(null, party, "x")!.Code);
        Assert.Equal(DealDisputeResolution.ActorNotStoreAdmin, DealDisputeResolution.Validate(Disputed(status: DealStatus.Active), party, "x")!.Code);
        Assert.Equal(DealDisputeResolution.DealNotDisputed, DealDisputeResolution.Validate(Disputed(status: DealStatus.Active), Owner, "x")!.Code);
        Assert.Equal(DealDisputeResolution.OutcomeUnknown, DealDisputeResolution.Validate(Disputed(), Owner, "x")!.Code);
    }

    // ═══ ٢) الخِدمَة — الكاتِبُ في الجِسمِ القائِم ═══════════════════════════════════════════════════

    [Fact]
    public void Resume_returns_the_deal_to_active_at_its_own_stage_with_a_dated_line_and_a_second_click_writes_nothing()
    {
        var world = new DocWorld();
        var pay = new DealMoneyClaimFirstTests.RecordingPaymentProvider();
        var deals = new DealsService(world.Store, pay);
        var deal = Disputed(DealStage.Shipping);
        world.Put(deal);

        var res = Now(deals.ResolveDisputeAsync(Slug, deal.Id, Owner, DealDisputeResolution.Resume, "اتَّفَقا"));
        Assert.True(res.Ok, res.Violation?.MessageAr);
        Assert.Equal(DealDisputeResolution.Resume, res.Outcome);
        var stored = world.Read<Deal>(deal.Id.ToString())!;
        Assert.Equal((DealStatus.Active, DealStage.Shipping), (stored.Status, stored.Stage));
        var line = stored.Timeline[^1];
        Assert.Equal((DealEvent.DisputeResumed, DealStage.Shipping, DealStage.Shipping, (Guid?)AppOwner, "اتَّفَقا"),
            (line.Action, line.StageBefore, line.StageAfter, line.ActorId, line.Note));
        Assert.Empty(pay.Calls);

        var saves = world.SaveCalls;
        var again = Now(deals.ResolveDisputeAsync(Slug, deal.Id, Owner, DealDisputeResolution.Resume, "ثانِيَة"));
        Assert.Equal(DealDisputeResolution.DealNotDisputed, again.Violation!.Code);
        Assert.Equal(saves, world.SaveCalls);
        output.WriteLine($"استِئناف: {stored.Status}/{stored.Stage} · سَطرُه {line.Action} · النَقرَةُ الثانِيَة {again.Violation.Code}");
    }

    /// <summary>الإلغاءُ بِحَسمِ نِزاعٍ عَلى صَفقَةٍ مَقبوضَةٍ بِمُزَوِّدِ النُسخَة — «إلغاء مع تسجيل استرجاع مستحقّ»: يَستَرجِعُ الكاتِبُ القائِمُ آلِيّاً مَرَّةً
    /// ويَكتُبُ سَطرَي «أُلغِيَت» و«رُدَّ المَبلَغ».</summary>
    [Fact]
    public void Cancel_on_a_captured_deal_refunds_through_the_existing_cancel_core()
    {
        var world = new DocWorld();
        var pay = new DealMoneyClaimFirstTests.RecordingPaymentProvider();
        var deals = new DealsService(world.Store, pay);
        var deal = Disputed();
        deal.Attributes[CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card;
        deal.Refs["payment_id"] = "pay_1";
        deal.Refs["payment_status"] = nameof(PaymentStatus.Captured);
        world.Put(deal);

        var res = Now(deals.ResolveDisputeAsync(Slug, deal.Id, Owner, DealDisputeResolution.Cancel, "بِضاعَةٌ مَعيبَة"));
        Assert.True(res.Ok, res.Violation?.MessageAr);
        var stored = world.Read<Deal>(deal.Id.ToString())!;
        Assert.Equal(DealStatus.Cancelled, stored.Status);
        Assert.Equal("بِضاعَةٌ مَعيبَة", stored.CancelReason);
        Assert.Equal(new[] { "refund pay_1" }, pay.Calls.ToArray());
        Assert.Contains(stored.Timeline, e => e.Action == "cancelled" && e.ActorId == AppOwner);
        Assert.Contains(stored.Timeline, e => e.Action == "refunded");
        output.WriteLine($"إلغاءٌ بِالحَسم: {stored.Status} · نِداءاتُ المُزَوِّد {string.Join(",", pay.Calls)}");
    }

    /// <summary>ودَفعٌ قَبَضَته بَوّابَةُ المَتجَرِ لا يَستَرجِعُه مُزَوِّدُ النُسخَة — يُكتَبُ «استِرجاعُه مُستَحَقٌّ عِندَ بَوّابَةِ المَتجَر» بِلا نِداء، كَما في كُلِّ إلغاء.</summary>
    [Fact]
    public void Cancel_on_a_gateway_paid_deal_records_the_refund_due_line_without_calling_the_provider()
    {
        var world = new DocWorld();
        var pay = new DealMoneyClaimFirstTests.RecordingPaymentProvider();
        var deals = new DealsService(world.Store, pay);
        var deal = Disputed();
        deal.Attributes[CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.Card;
        deal.Refs[GatewayPaymentProof.InvoiceRef] = "inv_1";
        deal.Refs["payment_id"] = "inv_1";
        deal.Refs["payment_status"] = nameof(PaymentStatus.Captured);
        world.Put(deal);
        Assert.True(GatewayPayments.CollectedByGateway(deal));

        var res = Now(deals.ResolveDisputeAsync(Slug, deal.Id, Owner, DealDisputeResolution.Cancel, "حَسم"));
        Assert.True(res.Ok, res.Violation?.MessageAr);
        var stored = world.Read<Deal>(deal.Id.ToString())!;
        Assert.Equal(DealStatus.Cancelled, stored.Status);
        Assert.Single(stored.Timeline, e => e.Action == DealEvent.GatewayRefundDue);
        Assert.Empty(pay.Calls);
        Assert.Equal(DealNotices.OwnerAction.RefundDue, DealNotices.OwnerActionOf(stored));
    }

    /// <summary>والطَرَفُ لا يَحسِمُ — لا بِالإلغاءِ ولا بِالاستِئناف — ولا يُكتَبُ شَيء؛ وبَديلٌ خارِجَ المَعجَمِ مِن صاحِبِ المَتجَرِ لا يُكتَبُ أَيضاً.</summary>
    [Fact]
    public void A_party_cannot_resolve_and_an_unknown_outcome_writes_nothing()
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new DealMoneyClaimFirstTests.RecordingPaymentProvider());
        var deal = Disputed();
        world.Put(deal);
        var saves = world.SaveCalls;

        foreach (var outcome in DealDisputeResolution.Outcomes)
        {
            var res = Now(deals.ResolveDisputeAsync(Slug, deal.Id, new DealCanceller(Seller, "بائِع", IsStoreAdmin: false), outcome, "أُريد"));
            Assert.Equal(DealDisputeResolution.ActorNotStoreAdmin, res.Violation!.Code);
        }
        Assert.Equal(DealDisputeResolution.OutcomeUnknown, Now(deals.ResolveDisputeAsync(Slug, deal.Id, Owner, "refund", "x")).Violation!.Code);
        Assert.Equal(saves, world.SaveCalls);
        Assert.Equal(DealStatus.Disputed, world.Read<Deal>(deal.Id.ToString())!.Status);
    }

    // ═══ ٣) الإشعار — «إشعار لكل طرف بالنتيجة» ═══════════════════════════════════════════════════

    private static (DocWorld Docs, DealNotices Notices, L L) NoticeWorld()
    {
        var docs = new DocWorld();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        var hub = services.BuildServiceProvider().GetRequiredService<IHubContext<ACommerce.Kit.Realtime.Server.RealtimeHub>>();
        var push = new WebPushService(new ConfigurationBuilder().Build(), NullLogger<WebPushService>.Instance);
        return (docs, new DealNotices(docs.Store, hub, push), new L(new HttpContextAccessor()));
    }

    [Fact]
    public void Both_parties_are_told_once_each_and_the_resolver_is_never_told()
    {
        var deal = Disputed();
        Assert.Equal(new[] { Buyer, Seller }, DealNotices.DisputePartiesToTell(deal, AppOwner).ToArray());
        Assert.Equal(new[] { Buyer }, DealNotices.DisputePartiesToTell(deal, Seller).ToArray());   // صاحِبُ المَتجَرِ هُوَ البائِع
        var noCounterparty = Disputed();
        noCounterparty.CounterpartyId = null;
        noCounterparty.Refs[DealsService.ListingOwnerRef] = Seller.ToString();
        Assert.Equal(new[] { Buyer, Seller }, DealNotices.DisputePartiesToTell(noCounterparty, AppOwner).ToArray());
        var selfDeal = Disputed();
        selfDeal.CounterpartyId = Buyer;
        Assert.Equal(new[] { Buyer }, DealNotices.DisputePartiesToTell(selfDeal, AppOwner).ToArray());
    }

    [Theory]
    [InlineData("resume")]
    [InlineData("cancel")]
    public void The_pipe_writes_one_notice_per_party_with_the_outcome_text(string outcome)
    {
        var (docs, notices, l) = NoticeWorld();
        var deal = Disputed(DealStage.Paid);
        var told = Now(notices.NotifyDisputeResolvedAsync(Slug, deal, AppOwner, outcome, l));
        Assert.Equal(2, told);
        var written = docs.Stored.OfType<Notification>().ToList();
        Assert.Equal(new[] { Buyer, Seller }, written.Select(n => n.UserId).ToArray());
        Assert.All(written, n => Assert.Equal($"/{Slug}/deals/{deal.Id}", n.RelatedUrl));
        var expectedBody = outcome == DealDisputeResolution.Cancel
            ? "حَسَمَت إدارَةُ المَتجَرِ النِّزاعَ عَلى «سِلعَة» بِإلغاءِ الصَّفقَة، ويُرَدُّ ما دُفِعَ إن كانَ قَد دُفِع — افتَحِ الصَّفقَة لِلتَفاصيل."
            : $"حَسَمَت إدارَةُ المَتجَرِ النِّزاعَ عَلى «سِلعَة» بِاستِئنافِ الصَّفقَةِ مِن مَرحَلَةِ «{DealsPolicy.LabelAr(DealStage.Paid)}» — افتَحِ الصَّفقَة لِمُتابَعَتِها.";
        Assert.All(written, n => Assert.Equal(expectedBody, n.Body));
        Assert.All(written, n => Assert.Equal(outcome == DealDisputeResolution.Cancel ? DealNotices.CancelledType : DealNotices.NewDealType, n.Type));
        output.WriteLine($"{outcome}: {told} إشعار · {written[0].Title}");
    }

    // ═══ ٤) البابُ والصَفحَة ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void The_studio_resolve_door_guards_first_states_its_authority_and_writes_audit_and_notices_only_on_success()
    {
        var endpoints = WriteEndpointGuardTests.StripComments(Source(Template, "MarketplaceTemplateExtensions.cs"));
        var at = endpoints.IndexOf("app.MapPost(\"/studio/apps/{slug}/deals/{id:guid}/resolve\"", StringComparison.Ordinal);
        Assert.True(at > 0, "بابُ الحَسمِ غَيرُ مَوجود.");
        var door = endpoints[at..endpoints.IndexOf("}).RequireFormBody().DisableAntiforgery();", at, StringComparison.Ordinal)];
        var guard = door.IndexOf("if (!await StudioOwnsAsync(store, auth, slug)) return Results.Redirect(\"/studio\");", StringComparison.Ordinal);
        Assert.True(guard > 0 && guard < door.IndexOf("req.Form[", StringComparison.Ordinal), "الحارِسُ لا يَسبِقُ قِراءَةَ الحُقول.");
        Assert.Contains("new Services.Deals.DealCanceller(auth.UserId!.Value, \"مالِك التَّطبيق\", IsStoreAdmin: true)", door, StringComparison.Ordinal);
        Assert.Contains("deals.ResolveDisputeAsync(slug, id, by, outcome,", door, StringComparison.Ordinal);
        var success = door.IndexOf("if (res.Ok)", StringComparison.Ordinal);
        Assert.True(success > 0 && success < door.IndexOf("\"deal.dispute_resolve\"", StringComparison.Ordinal)
                    && success < door.IndexOf("notices.NotifyDisputeResolvedAsync(", StringComparison.Ordinal), "التَدقيقُ أَو الإشعارُ خارِجَ النَجاح.");
        Assert.Contains("return Results.Redirect($\"/studio/apps/{slug}/deals/{id}\" + (res.Ok ? \"\" : $\"?err={res.Violation!.Code}\"));", door, StringComparison.Ordinal);
        Assert.Contains(DealDisputeResolution.DealNotDisputed, DealRefusalTexts.StudioCodes);
    }

    [Fact]
    public void The_studio_page_draws_the_resolve_form_for_a_disputed_deal_alone_with_both_outcomes()
    {
        var page = WriteEndpointGuardTests.StripMarkupComments(Source(Template, "Components", "Pages", "StudioAppDealDetail.razor"));
        var block = page.IndexOf("@if (deal.Status == DealStatus.Disputed)", StringComparison.Ordinal);
        var form = page.IndexOf("/deals/{deal.Id}/resolve\")", StringComparison.Ordinal);
        Assert.True(block > 0 && form > block, "استِمارَةُ الحَسمِ خارِجَ شَرطِ «نِزاع».");
        var dispute = page.IndexOf("/deals/{deal.Id}/dispute\")", StringComparison.Ordinal);
        Assert.True(dispute > 0 && dispute < block, "بابُ الحَسمِ لا يَلي بابَ الفَتح.");
        var section = page[block..(form + 1200)];
        Assert.Contains("name=\"outcome\" value=\"@DealDisputeResolution.Resume\"", section, StringComparison.Ordinal);
        Assert.Contains("name=\"outcome\" value=\"@DealDisputeResolution.Cancel\"", section, StringComparison.Ordinal);
        Assert.Contains("DealEvent.DisputeResumed => L[\"studio.deal.event_dispute_resumed\"]", page, StringComparison.Ordinal);
        Assert.Contains("DealEvent.DisputeResumed => L[\"deals.detail.event_dispute_resumed\"]",
            Source(Template, "Components", "Pages", "MyDealDetail.razor"), StringComparison.Ordinal);
        foreach (var key in new[] { "studio.deal.resolve_title", "studio.deal.resolve_hint", "studio.deal.resolve_cancel", "studio.deal.resolve_resume",
                                    "studio.deal.resolve_note_placeholder", "studio.deal.resolve_default_reason", "studio.deal.err_deal_not_disputed",
                                    "studio.deal.event_dispute_resumed", "deals.detail.event_dispute_resumed" })
            Assert.NotNull(LocaleCatalog.Find(LocaleCatalog.Arabic, key));
    }
}
