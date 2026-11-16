using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ نَقرَتانِ عَلى استِمارَةٍ واحِدَة — أَتُحَرِّكانِ الصَفقَةَ مَرحَلَتَين؟ (تَدقيقُ الإطلاق ‏2026-10-24، brief-P0-6 / lens-money-integrity-2) ═══
//
// **المَقيسُ قَبلَ اللَمس** (عِندَ `dace7cd`): أَبوابُ «تَأكيد: …» الثَلاثَة — بابُ العُضو `POST /{slug}/deals/{id}/advance` (استِمارَةُ
// `MyDealDetail`)، والمَسارُ الإداريّ `POST /studio/apps/{slug}/deals/{id}/advance` (استِمارَةُ `StudioAppDealDetail`)، و`POST /api/v1/deals/{id}/advance`
// — لا تَحمِلُ المَرحَلَةَ الَّتي قَرَأَها الناقِر، فَتُنادي `DealsService.AdvanceAsync` بِلا `from:`؛ والحارِسُ قائِمٌ في الخِدمَةِ اختِيارِيّاً
// (`from is { } expected && deal.Stage != expected` ⇒ `deal_changed_meanwhile`) ومُنادِياه الشَحنُ والوارِدُ مِن OTO وَحدَهُما. و‏ADR-036 يَرُدُّ
// الكِتابَتَينِ **المُتَداخِلَتَين** (نُسخَةٌ قُرِئَت ثُمَّ تَغَيَّرَت) لا **المُتَتالِيَتَين**: النَقرَةُ الثانِيَةُ طَلَبٌ جَديدٌ يَقرَأُ ما كَتَبَته الأولى ويَمضي
// مِنه. فَنَقرَةٌ مُزدَوِجَةٌ أَو لِسانٌ قَديمٌ يُحَرِّكُ صَفقَةَ النَقدِ مِن «حَجز» إلى «شَحن» بِلا بوليصَة (ويُغلَقُ بابُ OTO لِطَلَبِها)، أَو مِن «شَحن»
// إلى «تَسليم» بِسَطرِ «استُلِمَ النَقد» ثُمَّ إلى «تَقييم» مُكتَمِلَة.
//
// **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صَفّا «اليَوم» أَدناه يُودَعانِ وَحدَهُما، ويُقلَبانِ بِاسمَيهِما في كوميتِ الحَقلِ المَخفِيّ.
//
// **وقُلِبا بِاسمَيهِما** (‏2026-10-24؛ تَوصيفُهُما `23c362a`؛ ‏ADR-036 §١٣): صارَت الاستِمارَتانِ تَحمِلانِ `from` = المَرحَلَةَ الَّتي رُسِمَتا عَلَيها،
// والأَبوابُ الثَلاثَةُ تُمَرِّرُها `from:` بِـ`DealsService.ExpectedStage` — فَالنَقرَةُ الثانِيَةُ تُرَدُّ بِـ`deal_changed_meanwhile` والصَفقَةُ مُتَقَدِّمَةٌ مَرحَلَةً
// واحِدَة. ولِكُلِّ بابٍ صَفٌّ سالِبٌ بِتَسَلسُلِ نُقطَتِه، وفاحِصُ المَصدَرِ يُقاسُ بِعَيِّنَةٍ مَحقونَةٍ تُحمِرُه (القاعِدَة ١٠).

public class DealAdvanceExpectedStageTests(ITestOutputHelper output)
{
    private const string Slug = "zz-advance-expected-stage";
    private static readonly Guid Buyer  = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private static readonly Guid Seller = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    /// <summary>مُزَوِّدٌ يَرمي عِندَ أَيِّ نِداء — صَفَقاتُ هذا المِلَفِّ نَقدٌ بِلا مُعَرِّفِ دَفع.</summary>
    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("صَفقَةُ نَقدٍ لا تَمَسُّ المال — نودِيَ مُزَوِّدُ الدَفع.");
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

    private static string Endpoints() => WriteEndpointGuardTests.StripComments(Source(Template, "MarketplaceTemplateExtensions.cs"));
    private static string Api() => WriteEndpointGuardTests.StripComments(Source(Template, "Api", "ApiV1Endpoints.cs"));
    private static string Page(string name) => WriteEndpointGuardTests.StripMarkupComments(Source(Template, "Components", "Pages", name));

    /// <summary>صَفقَةُ سَلَّةٍ نَقداً عِندَ <paramref name="stage"/> — مُشتَرٍ وبائِعٌ مَربوطٌ مِن الوِلادَة — في عالَمِ وَثائِقَ جَديدٍ تَمشيه
    /// <see cref="DealsService"/> الحَقيقِيَّة.</summary>
    private static (DealsService Deals, DocWorld World, Deal Deal) CashAt(DealStage stage)
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());
        var deal = new Deal
        {
            Id = Guid.NewGuid(), TenantSlug = Slug, Pattern = "marketplace", Stage = stage, AmountSar = 120m,
            ListingTitle = "سِلعَة", InitiatorId = Buyer, InitiatorName = "مُشتَرٍ", CounterpartyId = Seller, CounterpartyName = "بائِع",
        };
        deal.Attributes[CheckoutPayMethods.AttributeKey] = CheckoutPayMethods.CashOnDelivery;
        world.Put(deal);
        return (deals, world, deal);
    }

    /// <summary>ما يُرسِلُه البابُ مِن استِمارَةٍ رُسِمَت عَلى <paramref name="rendered"/> — حَقلُها المَخفِيُّ بِحَرفِ <c>@deal.Stage</c>.</summary>
    private static DealStage? FormFrom(DealStage rendered) => DealsService.ExpectedStage(rendered.ToString());

    // ═══ ١) الأَثَرُ عَلى الخِدمَةِ الحَقيقِيَّة — بابُ العُضو ═══════════════════════════════════════════════════════════

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-24؛ كانَ <c>Today_two_posts_of_one_rendered_advance_form_move_a_cash_deal_two_stages</c>، مُودَعٌ في
    /// <c>23c362a</c>): البائِعُ يَنقُرُ «تَأكيد: تَأكيد» مَرَّتَين عَلى استِمارَةٍ رُسِمَت عِندَ «حَجز» — والبابُ يُمَرِّرُ ما فيها <c>from:</c> — فَتَمضي
    /// الأولى إلى «تَأكيد» وتُرَدُّ الثانِيَةُ بِـ<c>deal_changed_meanwhile</c> ولا يُكتَبُ شَيء؛ ومِن «شَحن» تَقِفُ عِندَ «تَسليم» بِسَطرِ نَقدٍ واحِدٍ والصَفقَةُ
    /// فَعّالَة. والإعادَةُ مِن صَفحَةٍ رُسِمَت بَعدَها تَمضي خُطوَةً واحِدَة.</para>
    /// </summary>
    [Fact]
    public void Two_posts_of_one_rendered_advance_form_move_a_cash_deal_one_stage_and_the_second_is_refused()
    {
        var (deals, world, deal) = CashAt(DealStage.Booked);
        var form = FormFrom(deal.Stage);
        var r1 = Now(deals.AdvanceAsync(Slug, deal.Id, Seller, "بائِع", "", from: form));
        var saves = world.SaveCalls;
        var r2 = Now(deals.AdvanceAsync(Slug, deal.Id, Seller, "بائِع", "", from: form));
        Assert.True(r1.Ok);
        Assert.False(r2.Ok);
        Assert.Equal(DealsService.ChangedMeanwhile, r2.Code);
        Assert.Equal(saves, world.SaveCalls);
        var stored = world.Read<Deal>(deal.Id.ToString())!;
        Assert.Equal(DealStage.Confirmed, stored.Stage);
        Assert.Equal(1, stored.Timeline.Count(e => e.Action == "advanced"));
        output.WriteLine($"نَقدٌ عِندَ «حَجز»، نَقرَتانِ لِلبائِع: r1.Ok={r1.Ok} r2.Ok={r2.Ok} code={r2.Code} stage={stored.Stage}");

        // والإعادَةُ مِن صَفحَةٍ رُسِمَت بَعدَ الرَدّ — حَقلُها «تَأكيد» — تَمضي خُطوَةً واحِدَة.
        var retry = Now(deals.AdvanceAsync(Slug, deal.Id, Seller, "بائِع", "", from: FormFrom(stored.Stage)));
        Assert.True(retry.Ok);
        Assert.Equal(DealStage.Shipping, world.Read<Deal>(deal.Id.ToString())!.Stage);

        var (deals2, world2, shipping) = CashAt(DealStage.Shipping);
        var sForm = FormFrom(shipping.Stage);
        var s1 = Now(deals2.AdvanceAsync(Slug, shipping.Id, Seller, "بائِع", "", from: sForm));
        var s2 = Now(deals2.AdvanceAsync(Slug, shipping.Id, Seller, "بائِع", "", from: sForm));
        var done = world2.Read<Deal>(shipping.Id.ToString())!;
        Assert.True(s1.Ok);
        Assert.False(s2.Ok);
        Assert.Equal(DealsService.ChangedMeanwhile, s2.Code);
        Assert.Equal(DealStage.Delivered, done.Stage);
        Assert.Equal(DealStatus.Active, done.Status);
        Assert.Equal(1, done.Timeline.Count(e => e.Action == DealEvent.CashOnDelivery));
        output.WriteLine($"نَقدٌ عِندَ «شَحن»، نَقرَتانِ لِلبائِع: stage={done.Stage} status={done.Status} cash_line=1 code={s2.Code}");
    }

    // ═══ ٢) بابُ الاستوديو — بِتَسَلسُلِ نُقطَتِه ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// <para><b>المَسارُ الإداريُّ بِتَسَلسُلِ نُقطَتِه</b>: <see cref="DealsService.AdminPathRefusal"/> عَلى المَقروء، ثُمَّ الفاعِلُ
    /// <c>CounterpartyId ?? appOwner</c>، ثُمَّ <see cref="DealsService.AdvanceAsync"/> بِـ<c>from:</c> الاستِمارَة. نَقرَتانِ عَلى «→ تَأكيد» رُسِمَ عِندَ
    /// «حَجز»: الأولى تَمضي، والثانِيَةُ يُجيزُها المُسنَدُ عَلى «تَأكيد» (تاليها «شَحن» لِلطَرَف) ثُمَّ يَرُدُّها الحارِسُ — والصَفقَةُ مُتَقَدِّمَةٌ مَرحَلَةً واحِدَة.</para>
    /// </summary>
    [Fact]
    public void Two_posts_of_one_studio_advance_form_move_the_deal_one_stage_and_the_second_is_refused()
    {
        var appOwner = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
        var (deals, world, deal) = CashAt(DealStage.Booked);
        var form = FormFrom(deal.Stage);
        DealAdvanceResult Post()
        {
            var read = Now(deals.LoadAsync(Slug, deal.Id))!;
            Assert.Null(DealsService.AdminPathRefusal(read, appOwner));
            return Now(deals.AdvanceAsync(Slug, deal.Id, read.CounterpartyId ?? appOwner, "مالِك التَّطبيق (إداريّ)", "", from: form));
        }
        var r1 = Post();
        var r2 = Post();
        Assert.True(r1.Ok);
        Assert.False(r2.Ok);
        Assert.Equal(DealsService.ChangedMeanwhile, r2.Code);
        Assert.NotNull(ACommerce.Templates.Customer.Marketplace.Services.Deals.DealRefusalTexts.StudioKeyFor(r2.Code));
        Assert.Equal(DealStage.Confirmed, world.Read<Deal>(deal.Id.ToString())!.Stage);
        output.WriteLine($"الاستوديو، نَقرَتانِ عَلى «→ تَأكيد»: r2.code={r2.Code} stage={world.Read<Deal>(deal.Id.ToString())!.Stage}");
    }

    // ═══ ٣) بابُ الـAPI — جِسمُ الطَلَبِ وجَوابُه ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// <para><b>الـAPI</b>: <c>expected_stage</c> يُقرَأُ مِن الجِسمِ بِخِياراتِ <c>ApiJson</c> نَفسِها، ويُمَرَّرُ <c>from:</c>؛ فَطَلَبانِ بِمِفتاحَي مَرَّةٍ‑واحِدَةٍ
    /// مُختَلِفَينِ وبِالمَرحَلَةِ نَفسِها يَمضي أَوَّلُهُما ويُجابُ الثاني ‏409 <c>deal_changed_meanwhile</c> بِمَرحَلَةِ المَخزَن. وغِيابُ الحَقلِ لا يُسأَلُ عَنه كَما كان.</para>
    /// </summary>
    [Fact]
    public void Two_api_advances_with_one_expected_stage_move_the_deal_one_stage_and_the_second_is_a_409()
    {
        var body = System.Text.Json.JsonSerializer.Deserialize<ACommerce.Templates.Customer.Marketplace.Api.AdvanceDealRequest>(
            "{\"note\":\"x\",\"expected_stage\":\"Shipping\"}", ACommerce.Templates.Customer.Marketplace.Api.ApiJson.Options)!;
        Assert.Equal("Shipping", body.expected_stage);
        var bare = System.Text.Json.JsonSerializer.Deserialize<ACommerce.Templates.Customer.Marketplace.Api.AdvanceDealRequest>(
            "{\"note\":\"x\"}", ACommerce.Templates.Customer.Marketplace.Api.ApiJson.Options)!;
        Assert.Null(bare.expected_stage);
        Assert.Null(DealsService.ExpectedStage(bare.expected_stage));

        var (deals, world, deal) = CashAt(DealStage.Shipping);
        var a1 = Now(deals.AdvanceAsync(Slug, deal.Id, Seller, "بائِع", body.note, CancellationToken.None, from: DealsService.ExpectedStage(body.expected_stage)));
        var a2 = Now(deals.AdvanceAsync(Slug, deal.Id, Seller, "بائِع", body.note, CancellationToken.None, from: DealsService.ExpectedStage(body.expected_stage)));
        var o1 = ACommerce.Templates.Customer.Marketplace.Api.DealApi.FromAdvance(a1);
        var o2 = ACommerce.Templates.Customer.Marketplace.Api.DealApi.FromAdvance(a2);
        Assert.Equal(200, o1.Status);
        Assert.Equal(409, o2.Status);
        var json = ACommerce.Templates.Customer.Marketplace.Api.ApiJson.Serialize(o2.Payload);
        Assert.Contains("\"code\":\"deal_changed_meanwhile\"", json, StringComparison.Ordinal);
        Assert.Contains("\"stage\":\"Delivered\"", json, StringComparison.Ordinal);
        Assert.Equal(DealStage.Delivered, world.Read<Deal>(deal.Id.ToString())!.Stage);
        output.WriteLine($"الـAPI، طَلَبانِ بِـexpected_stage=Shipping: {o1.Status} ثُمَّ {o2.Status} · stage=Delivered");
    }

    // ═══ ٤) الدالَّةُ النَقِيَّة ═════════════════════════════════════════════════════════════════════════════

    public static IEnumerable<object[]> EveryStage() => Enum.GetValues<DealStage>().Select(s => new object[] { s });

    /// <summary>مُوجِب: اسمُ كُلِّ مَرحَلَةٍ بِحَرفِ <c>ToString()</c> — ما تَرسُمُه <c>@deal.Stage</c> — يُعيدُها.</summary>
    [Theory]
    [MemberData(nameof(EveryStage))]
    public void ExpectedStage_reads_the_exact_name_the_form_renders(DealStage stage)
        => Assert.Equal(stage, DealsService.ExpectedStage(stage.ToString()));

    /// <summary>سالِب: الغِيابُ والفَراغُ وما لَيسَ اسمَ مَرحَلَةٍ بِحَرفِه — حالَةُ حَرفٍ أُخرى، رَقَم، مَسافَة، رايَتانِ مَضمومَتان، اسمٌ مَجهول — <c>null</c>،
    /// فَلا يُسأَلُ عَن مَرحَلَةٍ كَما كانَ قَبلَه.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("booked")]
    [InlineData("1")]
    [InlineData(" Booked")]
    [InlineData("Booked ")]
    [InlineData("Offered,Booked")]
    [InlineData("Nope")]
    [InlineData("99")]
    public void ExpectedStage_is_null_for_anything_but_an_exact_stage_name(string? raw)
        => Assert.Null(DealsService.ExpectedStage(raw));

    // ═══ ٥) الأَبوابُ الثَلاثَة — كُلٌّ يَحمِلُ المَرحَلَةَ الَّتي قُرِئَت ═══════════════════════════════════════════

    private const string MemberCall = "var res = await deals.AdvanceAsync(slug, id, userId, userName, note, from: Services.Deals.DealsService.ExpectedStage(req.Form[\"from\"].ToString()));";
    private const string StudioCall = "var result = await deals.AdvanceAsync(slug, id, actorId, \"مالِك التَّطبيق (إداريّ)\", note, from: Services.Deals.DealsService.ExpectedStage(req.Form[\"from\"].ToString()));";
    private const string ApiCall = "from: DealsService.ExpectedStage(body?.expected_stage));";
    private const string HiddenField = "<input type=\"hidden\" name=\"from\" value=\"@deal.Stage\" />";

    /// <summary>
    /// <para><b>الفاحِص</b>: لِكُلِّ بابٍ — استِمارَتُه (إن كانَت لَه) تَحمِلُ الحَقلَ المَخفِيَّ بِحَرفِه، ونُقطَتُه تُمَرِّرُ <c>from:</c> بِالدالَّةِ الواحِدَة،
    /// ونِداءُ <c>AdvanceAsync</c> فيها واحِدٌ لا ثانٍ بِلاه. يُعيدُ ما خالَفَ؛ والفارِغُ «لا مُخالَفَة».</para>
    /// </summary>
    private static List<string> DoorsWithoutTheReadStage(IEnumerable<(string Door, string? Form, string Endpoint, string Call)> doors)
    {
        var violations = new List<string>();
        foreach (var (door, form, endpoint, call) in doors)
        {
            if (form is not null && !form.Contains(HiddenField, StringComparison.Ordinal))
                violations.Add($"{door}: الاستِمارَةُ بِلا حَقلِ المَرحَلَة");
            if (!endpoint.Contains(call, StringComparison.Ordinal))
                violations.Add($"{door}: النُقطَةُ لا تُمَرِّرُ from:");
            if (endpoint.Split("deals.AdvanceAsync(").Length - 1 != 1)
                violations.Add($"{door}: نِداءُ AdvanceAsync لَيسَ واحِداً");
        }
        return violations;
    }

    private static List<(string Door, string? Form, string Endpoint, string Call)> Doors() => new()
    {
        ("العُضو",
            Segment(Page("MyDealDetail.razor"), "action=\"@($\"/{Slug}/deals/{deal.Id}/advance\")\"", "</form>"),
            Segment(Endpoints(), "\"/{slug}/deals/{id:guid}/advance\"", ".RequireStoreWritable();"),
            MemberCall),
        ("الاستوديو",
            Segment(Page("StudioAppDealDetail.razor"), "action=\"@($\"/studio/apps/{Slug}/deals/{deal.Id}/advance\")\"", "</form>"),
            Segment(Endpoints(), "\"/studio/apps/{slug}/deals/{id:guid}/advance\"", ".DisableAntiforgery();"),
            StudioCall),
        ("الـAPI",
            null,
            Segment(Api(), "\"/api/v1/deals/{id:guid}/advance\"", ".RequireApiKey(ApiScopeCatalog.DealsWrite);"),
            ApiCall),
    };

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-24؛ كانَ <c>Today_no_advance_door_carries_the_stage_its_clicker_read</c>، مُودَعٌ في <c>23c362a</c>): استِمارَتا
    /// «تَأكيد: …» و«→ التالي» تَحمِلانِ <c>from</c>، والنُقاطُ الثَلاثُ تُمَرِّرُها <c>from:</c> بِـ<see cref="DealsService.ExpectedStage"/>، وجِسمُ الـAPI يَحمِلُ
    /// <c>expected_stage</c>. ويُطبَعُ ما فُحِص، والصِفرُ يُحمِر (القاعِدَة ١٠).</para>
    /// </summary>
    [Fact]
    public void Every_advance_door_carries_the_stage_its_clicker_read()
    {
        var doors = Doors();
        Assert.Equal(3, doors.Count);
        var violations = DoorsWithoutTheReadStage(doors);
        Assert.True(violations.Count == 0, string.Join(" · ", violations));
        Assert.Contains("public sealed record AdvanceDealRequest(string? note, string? expected_stage = null);",
            Source(Template, "Api", "DealApi.cs"), StringComparison.Ordinal);
        output.WriteLine($"فُحِصَ {doors.Count} أَبواب (استِمارَتان {doors.Count(d => d.Form is not null)}) · مُخالَفات: {violations.Count}");
    }

    /// <summary><b>والفاحِصُ يُحمِرُ</b> (القاعِدَة ١٠): عَيِّنَةٌ بِحَرفِ ما كانَ عِندَ <c>dace7cd</c> — استِمارَةٌ بِلا حَقلٍ ونِداءٌ بِلا <c>from:</c> — وعَيِّنَةٌ
    /// تُضيفُ نِداءً ثانِياً بِلاه بِجِوارِ الصَحيح، كُلٌّ بِرَمزِ مُخالَفَتِه.</summary>
    [Fact]
    public void The_door_checker_goes_red_on_the_shape_the_doors_had_before()
    {
        var before = DoorsWithoutTheReadStage(new[]
        {
            ("عَيِّنَة", (string?)"<form method=\"post\"><AcButton Type=\"submit\" />",
                "var res = await deals.AdvanceAsync(slug, id, userId, userName, note);", MemberCall),
        });
        Assert.Equal(new[] { "عَيِّنَة: الاستِمارَةُ بِلا حَقلِ المَرحَلَة", "عَيِّنَة: النُقطَةُ لا تُمَرِّرُ from:" }, before);

        var twice = DoorsWithoutTheReadStage(new[]
        {
            ("عَيِّنَة", (string?)null, MemberCall + "\nvar again = await deals.AdvanceAsync(slug, id, userId, userName, note);", MemberCall),
        });
        Assert.Equal(new[] { "عَيِّنَة: نِداءُ AdvanceAsync لَيسَ واحِداً" }, twice);
        output.WriteLine($"عَيِّنَةُ ما قَبلُ: {before.Count} مُخالَفَتان · عَيِّنَةُ النِداءِ الثاني: {twice.Count}");
    }

    // ═══ ٦) غَيرُ الطَرَف — أَيَسمَعُ مَرحَلَةَ صَفقَةِ غَيرِه مِن رَدِّ `from`؟ (مُراجَعَةُ تَيّارِ W8-1 الجَولَة ٢، صُغرى؛ القاعِدَة ٦) ═══════════

    private static readonly Guid Stranger = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

    /// <summary>ما يَبلُغُ صَفحَةَ الصَفقَةِ مِن بابِ العُضو — <c>?err={code}</c> إن كانَ لِلنَتيجَةِ رَمز، ولا شَيءَ إن لَم يَكُن — بِحَرفِ سَطرِ التَحويلِ في النُقطَة
    /// (يُثَبِّتُه <c>DealConcurrencyTests.The_member_advance_endpoint_carries_the_result_code_to_the_deal_page</c>).</summary>
    private static string MemberRedirectSuffix(DealAdvanceResult res) => res.Code is { } code ? $"?err={code}" : "";

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-1 الجَولَة ٢، صُغرى؛ كانَ
    /// <c>Today_a_non_party_tells_the_stage_of_someone_elses_deal_from_the_answer_to_its_from</c>، مُودَعٌ في <c>2cde10f</c>): مُقارَنَةُ <c>from</c> كانَت
    /// تَسبِقُ سُؤالَ الفاعِلِ لِكُلِّ سائِل، فَغَيرُ الطَرَفِ يَسمَعُ <c>?err=deal_changed_meanwhile</c> لِلخاطِئَةِ ولا شَيءَ لِلصَحيحَة. <b>والآن</b>: يُسأَلُ
    /// «أَطَرَفٌ أَنت؟» قَبلَ المُقارَنَة، وغَيرُ الطَرَفِ يُجابُ ما يُجابُ بِه بِلا <c>from</c> — رَفضُ الفاعِلِ بِلا رَمزٍ ونَصُّه واحِدٌ في الحالَين — فَلا
    /// يَبلُغُه مِن مَرحَلَةِ الصَفقَةِ شَيء؛ ولا يُكتَبُ شَيء.</para>
    /// </summary>
    [Fact]
    public void A_non_party_hears_the_same_answer_whether_its_from_is_stale_or_right()
    {
        var (deals, world, deal) = CashAt(DealStage.Shipping);
        var saves = world.SaveCalls;

        var stale = Now(deals.AdvanceAsync(Slug, deal.Id, Stranger, "غَريب", "", from: DealStage.Booked));
        var right = Now(deals.AdvanceAsync(Slug, deal.Id, Stranger, "غَريب", "", from: DealStage.Shipping));
        var bare  = Now(deals.AdvanceAsync(Slug, deal.Id, Stranger, "غَريب", ""));

        Assert.False(stale.Ok);
        Assert.False(right.Ok);
        Assert.Null(stale.Code);
        Assert.Null(right.Code);
        Assert.Equal(right.Reason, stale.Reason);
        Assert.Equal(bare.Reason, stale.Reason);
        Assert.Equal("", MemberRedirectSuffix(stale));
        Assert.Equal(MemberRedirectSuffix(right), MemberRedirectSuffix(stale));
        Assert.Equal(saves, world.SaveCalls);
        Assert.Equal(DealStage.Shipping, world.Read<Deal>(deal.Id.ToString())!.Stage);
        output.WriteLine($"غَيرُ الطَرَف، from=Booked: «{MemberRedirectSuffix(stale)}» · from=Shipping: «{MemberRedirectSuffix(right)}» — جَوابٌ واحِد");
    }

    /// <summary><b>والطَرَفُ يُجابُ عَن التَقادُمِ كَما كان</b> (‏2026-10-25): البائِعُ والمُشتَري بِمَرحَلَةٍ مُتَقادِمَة — ولَو لَم يَكُن دَورَه — يُرَدّانِ
    /// بِـ<c>deal_changed_meanwhile</c> قَبلَ سُؤالِ الفاعِل، فَنَصُّ «تَغَيَّرَت الصَفقَةُ» يَبقى صادِقاً لِمَن يَراها؛ ولا يُكتَبُ شَيء.</summary>
    [Fact]
    public void A_party_with_a_stale_from_still_hears_deal_changed_meanwhile_before_the_actor_check()
    {
        var (deals, world, deal) = CashAt(DealStage.Shipping);
        var saves = world.SaveCalls;

        var seller = Now(deals.AdvanceAsync(Slug, deal.Id, Seller, "بائِع", "", from: DealStage.Booked));
        var buyer  = Now(deals.AdvanceAsync(Slug, deal.Id, Buyer,  "مُشتَرٍ", "", from: DealStage.Booked));

        Assert.Equal(DealsService.ChangedMeanwhile, seller.Code);
        Assert.Equal(DealsService.ChangedMeanwhile, buyer.Code);   // المُشتَري لَيسَ فاعِلَ «تَسليم» — ويَسمَعُ التَقادُمَ لا رَفضَ الفاعِل
        Assert.Equal("?err=deal_changed_meanwhile", MemberRedirectSuffix(buyer));
        Assert.Equal(saves, world.SaveCalls);
        Assert.Equal(DealStage.Shipping, world.Read<Deal>(deal.Id.ToString())!.Stage);
    }
}
