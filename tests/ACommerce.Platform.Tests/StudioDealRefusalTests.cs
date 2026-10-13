using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ رَفضُ المَسارِ الإداريِّ في الاستوديو — يُقرَأُ عَلى صَفحَةِ الصَفقَةِ أَم يُبتلَع؟ (مُراجَعَةُ المَوجَة 3a، G4 (i)) ═══
//
// **المَقيسُ قَبلَ اللَمس** (‏2026-10-13، أَساسُ التَيّار `4e32559`): نِقاطُ صَفَقاتِ الاستوديو الثَلاث — `advance` و`cancel` و`dispute` —
// تَعودُ إلى `StudioAppDealDetail` بِلا مُعامِل، والصَفحَةُ (ومَعَها قائِمَةُ `StudioAppDeals`) بِلا `SupplyParameterFromQuery` واحِد. والرَفضُ
// قائِمٌ بِرَمزِه في الخِدمَة: تَعارُضُ النُسخَةِ يُعيدُ `deal_changed_meanwhile` مِن `AdvanceAsync` و`CancelAsync` (‏ADR-036 §٢‑٢: «لا يُكتَب،
// وتَعودُ الصَفحَةُ بِالحالِ القائِم — بِلا شَريط»)، و`AdminPathCanMove` تَرُدُّ صَفقَةَ البَذرَةِ التَجريبِيَّةِ «→ حَجز» (‏ADR-033 §٦‑ط‑٨: «وطَلَبٌ
// مَصنوعٌ يَعودُ بِلا كَلِمَة»). فَالنَقرَةُ المَرفوضَةُ تَعودُ بِصَفحَةٍ كَأَنَّ شَيئاً لَم يَكُن — ما أُغلِقَ لِصَفحَةِ العُضوِ في المَوجَة ١ (القاعِدَة ١٢).
//
// **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صَفُّ «اليَوم» أَدناه يُودَعُ وَحدَه، ويُقلَبُ بِاسمِه في الكوميتِ الَّذي يَحمِلُ الرَمزَ ويَرسُمُ الشَريط.

public class StudioDealRefusalTests(ITestOutputHelper output)
{
    private const string Slug = "zz-studio-deal-refusal";
    private static readonly Guid Buyer    = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller   = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
    private static readonly Guid AppOwner = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    /// <summary>مُزَوِّدٌ يَرمي عِندَ أَيِّ نِداء — صَفَقاتُ هذا المِلَفِّ بِلا مُعَرِّفِ دَفع.</summary>
    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("لا مالَ في صَفَقاتِ هذا المِلَفّ — نودِيَ مُزَوِّدُ الدَفع.");
        public string ProviderName => "forbidden";
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default) => throw Touched();
        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Touched();
        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Touched();
    }

    /// <summary>عالَمُ الوَثائِقِ يُتِمُّ كُلَّ انتِظارٍ في مَكانِه — وإن لَم يَكتَمِل فَالأَداةُ تَكذِب.</summary>
    private static T Now<T>(Task<T> task)
    {
        Assert.True(task.IsCompleted, "نِداءٌ لَم يَكتَمِل في عالَمٍ مُتَزامِن — الأَداةُ تَكذِب.");
        return task.GetAwaiter().GetResult();
    }

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    private static string Segment(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"‏`{from}` غَيرُ مَوجود — الأَداةُ عَمياء.");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"‏`{to}` لا يَتلو `{from}` — الأَداةُ عَمياء.");
        return source.Substring(start, end - start);
    }

    /// <summary>جِسمُ نُقطَةِ صَفَقاتِ الاستوديو بِاسمِها الأَخير (<c>advance</c>، <c>cancel</c>، <c>dispute</c>) بِلا تَعليقات.</summary>
    private static string StudioEndpoint(string verb)
        => WriteEndpointGuardTests.StripComments(Segment(Source(Template, "MarketplaceTemplateExtensions.cs"),
            $"\"/studio/apps/{{slug}}/deals/{{id:guid}}/{verb}\"", ".DisableAntiforgery();"));

    private static string Page(string name)
        => WriteEndpointGuardTests.StripMarkupComments(Source(Template, "Components", "Pages", name));

    /// <summary>الحِفظُ التالي يَرمي ما يَرميه Marten حينَ كَتَبَ غَيرُ الجَلسَةِ الصَفقَةَ بَعدَ قِراءَتِها — مَرَّةً واحِدَة.</summary>
    private static void ConflictOnNextSave(DocWorld world, Guid id)
    {
        var armed = true;
        world.BeforeMember = name =>
        {
            if (name != "SaveChangesAsync" || !armed) return;
            armed = false;
            throw new JasperFx.ConcurrencyException(typeof(Deal), id);
        };
    }

    /// <summary>صَفقَةُ سَلَّةٍ نَقدِيَّةٌ مَربوطٌ بائِعُها عِندَ الوِلادَة — بِتَسَلسُلِ <c>/checkout/submit</c>.</summary>
    private static (DocWorld World, DealsService Deals, Guid Id) CartDeal()
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());
        var deal = Now(deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", null, "سِلعَة", 100m,
            new() { [CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery }));
        Now(deals.AttachRefAsync(Slug, deal.Id, DealsService.ListingOwnerRef, Seller.ToString()));
        Now(deals.BindListingOwnerAsync(Slug, deal.Id, Seller.ToString()));
        return (world, deals, deal.Id);
    }

    // ═══ ١) الرَفضُ قائِمٌ بِرَمزِه في الخِدمَة، والنُقطَةُ تَحمِلُه، والصَفحَةُ تَرسُمُه مِن القامُوس ═════════════

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (i)): كانَ
    /// <c>Today_the_studio_deal_endpoints_drop_the_refusal_code_and_the_studio_deal_pages_read_none</c> (مُودَعٌ في <c>f2be846</c>) —
    /// النِقاطُ تَعودُ بِلا <c>?err=</c> والصَفحَتانِ بِلا مُعامِل. وصارَ بابا <c>advance</c> و<c>cancel</c> يَحمِلانِ الرَمزَ، وصَفحَتا الاستوديو
    /// تَرسُمانِه بِمَعجَمِهِما (<see cref="DealRefusalTexts.StudioKeyFor"/>). وبابُ <c>dispute</c> بِلا قَناةِ رَفضٍ بَعد — <c>DisputeAsync</c>
    /// تُرجِعُ <c>Deal?</c> — فَصَفُّه يُبقيه كَما هُوَ هُنا، ويُقلَبُ في بَندِه (G4 (ii)).</para>
    /// </summary>
    [Fact]
    public void The_studio_advance_and_cancel_endpoints_carry_the_refusal_code_and_both_studio_deal_pages_draw_it_from_the_dictionary()
    {
        // الخِدمَةُ تَرُدُّ بِرَمز: تَعارُضُ النُسخَةِ في التَقَدُّمِ والإلغاء.
        var (world, deals, id) = CartDeal();
        ConflictOnNextSave(world, id);
        var advanced = Now(deals.AdvanceAsync(Slug, id, Seller, "مالِك التَّطبيق (إداريّ)", ""));
        Assert.False(advanced.Ok);
        Assert.Equal(DealsService.ChangedMeanwhile, advanced.Code);

        ConflictOnNextSave(world, id);
        var cancelled = Now(deals.CancelAsync(Slug, id, new DealCanceller(AppOwner, "مالِك التَّطبيق", IsStoreAdmin: true), "إلغاء إداريّ"));
        Assert.False(cancelled.Ok);
        Assert.Equal(DealsService.ChangedMeanwhile, cancelled.Violation!.Code);

        // وصَفقَةُ البَذرَةِ التَجريبِيَّةِ — مُبادِرُها مالِكُ التَطبيق — يَرُدُّها المَسارُ الإداريُّ بِرَمزِه.
        var seed = Now(deals.StartAsync(Slug, "marketplace", AppOwner, "صاحِب الفِكرَة", null, "عَمَلِيَّة تَجريبيَّة — marketplace", 250m));
        Assert.False(DealsService.AdminPathCanMove(seed, AppOwner));
        Assert.Equal(DealsService.AdminPathCannotMove, DealsService.AdminPathRefusal(seed, AppOwner));

        // والبابانِ يَحمِلانِ الرَمزَ إلى الصَفحَة — بِحَرفِ نَسَقِ بابِ العُضو.
        var advance = StudioEndpoint("advance");
        var cancel = StudioEndpoint("cancel");
        Assert.Contains("if (deal is null) return Results.Redirect($\"/studio/apps/{slug}/deals?err={Services.Deals.DealCancelAuthorization.DealNotFound}\");",
            advance, StringComparison.Ordinal);
        Assert.Contains("if (Services.Deals.DealsService.AdminPathRefusal(deal, auth.UserId!.Value) is { } refused) return Results.Redirect($\"/studio/apps/{slug}/deals/{id}?err={refused}\");",
            advance, StringComparison.Ordinal);
        Assert.Contains("return Results.Redirect($\"/studio/apps/{slug}/deals/{id}\" + (result.Code is { } code ? $\"?err={code}\" : \"\"));",
            advance, StringComparison.Ordinal);
        Assert.Contains("return Results.Redirect($\"/studio/apps/{slug}/deals/{id}\" + (res.Ok ? \"\" : $\"?err={res.Violation!.Code}\"));",
            cancel, StringComparison.Ordinal);

        // والصَفحَتانِ تَقرَآنِ المُعامِلَ وتَرسُمانِه بِالدالَّةِ الواحِدَة.
        foreach (var name in new[] { "StudioAppDealDetail.razor", "StudioAppDeals.razor" })
        {
            var page = Page(name);
            Assert.Contains("[SupplyParameterFromQuery(Name = \"err\")] public string? Err { get; set; }", page, StringComparison.Ordinal);
            Assert.Contains("@if (DealRefusalTexts.StudioKeyFor(Err) is { } errKey)", page, StringComparison.Ordinal);
            Assert.Contains("<div class=\"st-chip st-chip-danger\" style=\"margin-bottom:14px;\">@L.Markup(errKey)</div>", page, StringComparison.Ordinal);
            output.WriteLine($"{name}: يَقرَأُ ?err= ويَرسُمُه مِن القامُوس.");
        }
    }

    // ═══ ١‑ب) نُقطَةُ النِزاع — سَطرُ التَدقيقِ يُكتَبُ ولَو لَم يُكتَب النِزاع (G4 (ii)؛ ‏ADR-036 §٧‑٣(ب) «قائِمٌ ويُقال») ═══════
    //
    // **المَقيسُ قَبلَ اللَمس**: `DisputeAsync` تُرجِعُ `Deal?` — المَخزونَ حينَ لا تَكتُب (صَفقَةٌ غَيرُ فَعّالَة، أَو تَعارُضُ نُسخَة) —
    // فَلا يَعرِفُ مُناديها أَكَتَبَت أَم لا؛ ونُقطَةُ الاستوديو تُسقِطُ ما تُرجِعُه وتَكتُبُ سَطرَ `deal.dispute` بِلا شَرط: نِزاعٌ لَم يَقَع
    // مُسَجَّلٌ في دَفتَرِ التَدقيقِ بِاسمِ مالِكِ التَطبيق. صَفُّ «اليَوم» يُودَعُ وَحدَه ويُقلَبُ بِاسمِه في كوميتِ الإصلاح.

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (ii)؛ ‏ADR-036 §١٢‑٢): كانَ
    /// <c>Today_the_studio_dispute_endpoint_writes_its_audit_line_even_when_DisputeAsync_wrote_nothing</c> (مُودَعٌ في <c>1b73c48</c>).
    /// وصارَت <c>DisputeAsync</c> تُرجِعُ <see cref="DealDisputeResult"/> — «كُتِبَ» وَحدَه <c>Ok</c>، وما لَم يُكتَب بِرَمزِه — والنُقطَةُ
    /// تَشتَرِطُ سَطرَ التَدقيقِ بِه وتَحمِلُ الرَمزَ إلى صَفحَتِها.</para>
    /// </summary>
    [Fact]
    public void The_studio_dispute_endpoint_audits_only_a_written_dispute_and_carries_the_refusal_code()
    {
        // تَعارُضُ النُسخَة: لا حِفظ، والرَمزُ `deal_changed_meanwhile`.
        var (world, deals, id) = CartDeal();
        var saves = world.SaveCalls;
        ConflictOnNextSave(world, id);
        var conflicted = Now(deals.DisputeAsync(Slug, id, AppOwner, "مالِك التَّطبيق", "نِزاع"));
        Assert.False(conflicted.Ok);
        Assert.Equal(DealsService.ChangedMeanwhile, conflicted.Code);
        Assert.NotNull(conflicted.Deal);
        Assert.Equal(saves, world.SaveCalls);

        // وصَفقَةٌ غَيرُ فَعّالَة: لا حِفظ، والرَمزُ `deal_not_active`. (بِصَفقَةٍ ثانِيَة: العالَمُ يُعيدُ الكائِنَ نَفسَه، فَطَفرَةُ المُحاوَلَةِ
        // المَردودَةِ أَعلاه باقِيَةٌ عَلَيه — فِقرَةُ `DealConcurrencyRefusalTests`.)
        var (world2, deals2, id2) = CartDeal();
        var cancelled = Now(deals2.CancelAsync(Slug, id2, new DealCanceller(AppOwner, "مالِك التَّطبيق", IsStoreAdmin: true), "إلغاء"));
        Assert.True(cancelled.Ok);
        saves = world2.SaveCalls;
        var refused = Now(deals2.DisputeAsync(Slug, id2, AppOwner, "مالِك التَّطبيق", "نِزاع"));
        Assert.False(refused.Ok);
        Assert.Equal(DealCancelAuthorization.DealNotActive, refused.Code);
        Assert.Equal(DealStatus.Cancelled, refused.Deal!.Status);
        Assert.Equal(saves, world2.SaveCalls);

        // وغائِبَة: `deal_not_found` بِلا صَفقَة.
        var missing = Now(deals2.DisputeAsync(Slug, Guid.NewGuid(), AppOwner, "مالِك التَّطبيق", "نِزاع"));
        Assert.False(missing.Ok);
        Assert.Equal(DealCancelAuthorization.DealNotFound, missing.Code);
        Assert.Null(missing.Deal);

        // والمُوجِب: نِزاعٌ يُكتَبُ — `Ok` بِلا رَمز، والصَفقَةُ «نِزاع» بِحِفظٍ واحِد.
        var (world3, deals3, id3) = CartDeal();
        saves = world3.SaveCalls;
        var written = Now(deals3.DisputeAsync(Slug, id3, AppOwner, "مالِك التَّطبيق", "نِزاع"));
        Assert.True(written.Ok);
        Assert.Null(written.Code);
        Assert.Equal(DealStatus.Disputed, written.Deal!.Status);
        Assert.Equal(saves + 1, world3.SaveCalls);

        // ولِكُلِّ رَمزِ رَفضٍ نَصٌّ في مَعجَمِ الاستوديو.
        foreach (var code in new[] { conflicted.Code, refused.Code, missing.Code })
            Assert.NotNull(DealRefusalTexts.StudioKeyFor(code));

        // والنُقطَةُ تَشتَرِطُ التَدقيقَ بِالكِتابَةِ وتَحمِلُ الرَمز.
        var dispute = StudioEndpoint("dispute").Replace("\r", "");
        Assert.Contains("var res = await deals.DisputeAsync(slug, id, auth.UserId!.Value, \"مالِك التَّطبيق\", reason);\n            if (res.Ok) await audit.WriteAsync(slug, auth.UserId, \"مالِك التَّطبيق\",\n                \"deal.dispute\",",
            dispute, StringComparison.Ordinal);
        Assert.Contains("return Results.Redirect($\"/studio/apps/{slug}/deals/{id}\" + (res.Ok ? \"\" : $\"?err={res.Code}\"));", dispute, StringComparison.Ordinal);
        Assert.Contains("public async Task<DealDisputeResult> DisputeAsync(", Source(Template, "Services", "Deals", "DealsService.cs"), StringComparison.Ordinal);
    }

    // ═══ ٢) مَعجَمُ الاستوديو — مُغلَق، ولِكُلِّ رَمزٍ نَصٌّ حَيّ، وما سِواه لا يُرسَم ═══════════════════════════════════

    [Fact]
    public void Every_studio_refusal_code_has_a_live_arabic_text_under_the_studio_prefix()
    {
        var checked_ = 0;
        foreach (var code in DealRefusalTexts.StudioCodes)
        {
            var key = DealRefusalTexts.StudioKeyFor(code);
            Assert.Equal(DealRefusalTexts.StudioPrefix + code, key);
            var text = ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, key!);
            Assert.NotEqual(key, text);
            Assert.False(ACommerce.Platform.I18n.LocaleCatalog.IsPlaceholder(text), $"‏{key} نائِب.");
            Assert.True(text.Length > 10, $"‏{key} بِنَصٍّ أَقصَرَ مِن أَن يُفَسِّر.");
            output.WriteLine($"{code} → {key}");
            checked_++;
        }
        // (‏2026-10-13، G5 M4): +`deal_invoice_shared` — إلغاءُ صَفقَةٍ واحِدَةٍ مِن فاتورَةِ بَوّابَةٍ مَفتوحَةٍ مُشتَرَكَةٍ يُرَدُّ بِه: 6 ← 7.
        Assert.Equal(7, checked_);
        Assert.Equal(DealRefusalTexts.StudioCodes.Count, DealRefusalTexts.StudioCodes.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-target")]                          // رَمزُ نُقطَةِ التَقييمِ الإدارِيَّة — يُرسَلُ ولا مِفتاحَ لَه
    [InlineData("actor_not_party")]                    // رَمزُ صَفحَةِ العُضو لا يَبلُغُ الاستوديو
    [InlineData("oto_refused")]
    [InlineData("studio.deal.err_deal_not_active")]    // المِفتاحُ خاماً لا يُقبَلُ رَمزاً
    public void A_code_outside_the_studio_lexicon_draws_nothing(string? code)
        => Assert.Null(DealRefusalTexts.StudioKeyFor(code));

    // ═══ ٣) «لِماذا» المَسارِ الإداريّ — رَمزٌ لِكُلِّ شَرط، و«لا رَمز» هِيَ «يُحَرِّك» ═════════════════════════════════

    private static Deal At(string pattern, string? pay, DealStage stage, Guid initiator, Guid? counterparty,
        DealStatus status = DealStatus.Active, Action<Deal>? more = null)
    {
        var deal = new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = Slug, Pattern = pattern, Stage = stage, Status = status,
            InitiatorId = initiator, InitiatorName = "مُبادِر", CounterpartyId = counterparty, AmountSar = 100m,
        };
        if (pay is not null) deal.Attributes[CheckoutPayMethods.AttributeKey] = pay;
        more?.Invoke(deal);
        return deal;
    }

    [Fact]
    public void AdminPathRefusal_names_each_reason_and_its_absence_is_exactly_AdminPathCanMove()
    {
        // المُوجِب لِكُلِّ رَمز — وكُلٌّ بِصَفقَةٍ مُسَمّاة.
        var cases = new (string Name, Deal Deal, string? Code)[]
        {
            ("cancelled",           At("marketplace", CheckoutPayMethods.CashOnDelivery, DealStage.Booked, Buyer, Seller, DealStatus.Cancelled), DealCancelAuthorization.DealNotActive),
            ("studio_seed_offered", At("marketplace", null, DealStage.Offered, AppOwner, null), DealsService.AdminPathCannotMove),
            ("buyer_pays",          At("marketplace", CheckoutPayMethods.Card, DealStage.Confirmed, Buyer, Seller), DealsService.AdminPathCannotMove),
            ("last_stage",          At("classifieds", null, DealStage.Confirmed, Buyer, Seller), DealsService.AdminPathCannotMove),
            ("order_at_oto",        At("marketplace", CheckoutPayMethods.CashOnDelivery, DealStage.Confirmed, Buyer, Seller,
                                        more: d => d.Refs[DealShipping.OrderRef] = d.Id.ToString("N")), DealShipping.DealAwaitsCarrier),
            ("unpaid_invoice_seed", At("marketplace", CheckoutPayMethods.Card, DealStage.Confirmed, AppOwner, null,
                                        more: d => d.Refs[GatewayPaymentProof.InvoiceRef] = "inv_1"), GatewayPayments.DealAwaitsPayment),
            // والسالِب: ما يُحَرِّكُه المَسارُ — «لا رَمز».
            ("cart_bound_offered",  At("marketplace", CheckoutPayMethods.CashOnDelivery, DealStage.Offered, Buyer, Seller), null),
            ("cash_ship",           At("marketplace", CheckoutPayMethods.CashOnDelivery, DealStage.Confirmed, Buyer, Seller), null),
        };
        foreach (var (name, deal, code) in cases)
        {
            var refusal = DealsService.AdminPathRefusal(deal, AppOwner);
            output.WriteLine($"{name}: {refusal ?? "—"}");
            Assert.True(refusal == code, $"«{name}»: الرَمز {refusal ?? "—"}، والمُتَوَقَّع {code ?? "—"}.");
            Assert.Equal(code is null, DealsService.AdminPathCanMove(deal, AppOwner));
            if (code is not null) Assert.NotNull(DealRefusalTexts.StudioKeyFor(code));
        }
        Assert.Equal(8, cases.Length);
    }
}
