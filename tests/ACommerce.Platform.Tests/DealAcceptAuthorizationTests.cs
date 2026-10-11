using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>الثَغرَةُ P0-3 مُغلَقَةٌ في الخِدمَة، ومَقيسَةٌ مِن طَرَفَيها.</b>
/// كانَت <c>POST /{slug}/deals/{id}/accept</c> تَفحَصُ الجَلسَةَ وَحدَها ثُمَّ
/// <c>AssignCounterpartyAsync(slug, id, userId, …)</c> — فَأَيُّ مُسَجَّلٍ في
/// المَتجَرِ يَصيرُ الطَرَفَ المُقابِلَ لِأَيِّ صَفقَة: مَن «يُؤَكِّد» و«يَشحَن»
/// ويُدفَعُ لَه نَقداً عِندَ الاستِلام (موجَزُ الفَجَوات ‏2026-10-10؛ ‏ADR-033
/// §٢‑ج‑أ). والشاشَةُ تُخفي الزِرَّ عَن غَيرِ المالِك — و<c>curl</c> لا يَرى
/// الشاشَة.</para>
///
/// <para><b>ولِماذا هُنا لا في اختِبارِ تَكامُل</b>: القَرارُ كُلُّه دالَّةٌ
/// نَقِيَّة (<c>Validate</c>)، فَقِياسُه لا يَحتاجُ قاعِدَةَ بَيانات — نَفسُ
/// شَكلِ <c>DealCancelAuthorizationTests</c> حَرفاً. ولِكُلِّ رَمزٍ في المَعجَمِ
/// <b>اختِبارٌ مُوجِبٌ وسالِب</b> (القاعِدَة ٤).</para>
///
/// <para><b>والأَحمَرُ قَبلَ الأَخضَر</b>: فَحصُ النُقطَةِ في آخِرِ المِلَفِّ
/// كُتِبَ وهي ما زالَت تُعَيِّنُ الناقِرَ بِنَفسِها، فَأَحمَرَّ، ثُمَّ وُصِلَت
/// بِـ<c>AcceptAsync</c> فَاخضَرّ — في الكوميتِ نَفسِه، لِأَنّ الحُزمَةَ لا
/// تُودَعُ حَمراء.</para>
///
/// <para><b>والقَلبُ المَرئِيُّ الثاني (مُراجَعَةُ ‏2026-10-11)</b>: النُسخَةُ
/// الأولى أَبقَت لِلصَفقَةِ بِلا <c>Refs[listing_owner]</c> السُلوكَ القَديمَ
/// (أَيُّ مُسَجَّلٍ غَيرِ المُبادِرِ يَقبَل) وثَبَّتَته هُنا اختِباراً. والمَقيسُ
/// أَنّ كُلَّ مَسارِ إنشاءِ إعلانٍ يَكتُبُ <c>owner_id</c>، فَالاستِثناءُ كانَ
/// الثَغرَةَ نَفسَها لِوَثائِقٍ قَديمَةٍ غَيرِ مَعدودَة، وخُروجاً عَن ‏ADR-033
/// §٢‑ج‑أ بِلا ADR. فَصارَ الغِيابُ رَمزاً سادِساً <c>listing_owner_missing</c>
/// — والصُفوفُ الَّتي كانَت تُثبِّتُ القَديمَ قُلِبَت هُنا إلى نَقيضِها بِاسمِها،
/// لا حُذِفَت صامِتَة.</para>
/// </summary>
public class DealAcceptAuthorizationTests
{
    private static readonly Guid Owner     = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Offerer   = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Stranger  = Guid.Parse("33333333-3333-4333-8333-333333333333");

    /// <summary>عَرضٌ وارِدٌ نَموذَجيّ: بِلا طَرَفٍ مُقابِل، بادَرَ بِه غَيرُ
    /// المالِك، ومَرجِعُ مالِكِ الإعلانِ يُشيرُ إلى المالِك — كَما يُلحِقُه
    /// مَسارا العَرضِ والسَلَّة.</summary>
    private static Deal Incoming(DealStatus status = DealStatus.Active, bool withOwnerRef = true)
    {
        var d = new Deal
        {
            Id = Guid.NewGuid(), Pattern = "marketplace",
            Stage = DealStage.Offered, Status = status, AmountSar = 100,
            InitiatorId = Offerer, CounterpartyId = null,
        };
        if (withOwnerRef) d.Refs[DealsService.ListingOwnerRef] = Owner.ToString();
        return d;
    }

    private static DealAcceptor By(Guid id) => new(id, "فاعِل");

    // ─── المَعجَم ──────────────────────────────────────────────────────

    /// <summary>سِتَّةٌ مُنذُ المُراجَعَة (كانَت خَمسَة): السادِسُ
    /// <c>listing_owner_missing</c> بَدَلَ استِثناءِ <c>IsLegacyUnowned</c>
    /// الَّذي حُذِف — نُمُوُّ المَعجَمِ قَرارٌ مَرئِيٌّ هُنا لا انزِلاق.</summary>
    [Fact]
    public void Exactly_six_violation_codes_and_they_are_these()
        => Assert.Equal(
            new[] { "deal_not_found", "listing_owner_missing", "actor_not_listing_owner", "actor_is_initiator", "deal_not_active", "deal_already_accepted" },
            DealAcceptAuthorization.All);

    [Fact]
    public void A_code_outside_the_vocabulary_throws_at_composition_time()
        => Assert.Throws<ArgumentException>(() => DealAcceptAuthorization.Require("actor_is_nice"));

    [Fact]
    public void Every_code_in_the_vocabulary_is_accepted()
    {
        foreach (var c in DealAcceptAuthorization.All)
            Assert.Equal(c, DealAcceptAuthorization.Require(c));
        Assert.Equal(6, DealAcceptAuthorization.All.Count);
    }

    // ─── مُوجِب — مَن يَملِكُ القَبول ──────────────────────────────────

    [Fact]
    public void The_listing_owner_may_accept_an_incoming_offer()
    {
        Assert.Null(DealAcceptAuthorization.Validate(Incoming(), By(Owner)));
        Assert.True(DealAcceptAuthorization.IsAllowed(Incoming(), By(Owner)));
    }

    /// <summary><b>قُلِبَ في المُراجَعَة — كانَ يُثبِّتُ النَقيض</b>: صَفقَةٌ بِلا
    /// <c>Refs[listing_owner]</c> كانَت تُقبَلُ مِن أَيِّ غَريب (<c>Assert.Null</c>
    /// عَلى <c>Validate(legacy, By(Stranger))</c>)، وصارَت مُغلَقَةً عَلى الجَميعِ
    /// بِرَمزِها — الغَريبِ والمُبادِرِ مَعاً، لِأَنّ الغِيابَ صِفَةُ الصَفقَةِ لا
    /// الفاعِل. ومَرجِعٌ فارِغٌ كَغِيابِه. مَسارُها الإداريُّ وَحدَه.</summary>
    [Fact]
    public void A_deal_without_a_listing_owner_ref_is_closed_to_everyone_with_its_own_code()
    {
        var orphan = Incoming(withOwnerRef: false);
        Assert.Equal(DealAcceptAuthorization.ListingOwnerMissing, DealAcceptAuthorization.Validate(orphan, By(Stranger))!.Code);
        Assert.Equal(DealAcceptAuthorization.ListingOwnerMissing, DealAcceptAuthorization.Validate(orphan, By(Offerer))!.Code);
        Assert.Equal(DealAcceptAuthorization.ListingOwnerMissing, DealAcceptAuthorization.Validate(orphan, By(Owner))!.Code);
        Assert.False(DealAcceptAuthorization.IsAllowed(orphan, By(Stranger)));

        // ومَرجِعٌ فارِغٌ كَغِيابِه — لا يُقرَأُ فَراغٌ مالِكاً.
        var blank = Incoming(withOwnerRef: false);
        blank.Refs[DealsService.ListingOwnerRef] = "   ";
        Assert.Equal(DealAcceptAuthorization.ListingOwnerMissing, DealAcceptAuthorization.Validate(blank, By(Stranger))!.Code);

        // والغِيابُ يَسبِقُ الحالَةَ كَسائِرِ التَخويل — صَفقَةٌ يَتيمَةٌ مُكتَمِلَةٌ
        // بِطَرَفٍ لا تُجيبُ «مَقبولَةٌ أَصلاً» بَل «لا مالِك».
        var settled = Incoming(DealStatus.Completed, withOwnerRef: false);
        settled.CounterpartyId = Owner;
        Assert.Equal(DealAcceptAuthorization.ListingOwnerMissing, DealAcceptAuthorization.Validate(settled, By(Stranger))!.Code);
    }

    /// <summary>والسالِب: صَفقَةٌ تَحمِلُ المَرجِعَ لا تُجيبُ هذا الرَمزَ أَبَداً —
    /// المالِكُ يُخَوَّل، والغَريبُ يُرَدُّ بِـ«لَستَ المالِك» لا بِـ«لا مالِك».</summary>
    [Fact]
    public void A_deal_with_a_listing_owner_ref_never_answers_listing_owner_missing()
    {
        Assert.Null(DealAcceptAuthorization.Validate(Incoming(), By(Owner)));
        var v = DealAcceptAuthorization.Validate(Incoming(), By(Stranger));
        Assert.NotEqual(DealAcceptAuthorization.ListingOwnerMissing, v!.Code);
        Assert.Equal(DealAcceptAuthorization.ActorNotListingOwner, v.Code);
    }

    // ─── سالِب — الثَغرَةُ نَفسُها، ورَمزٌ لِكُلِّ مِحوَر ─────────────────

    /// <summary><b>هذا هُوَ الاختِبارُ الَّذي كانَ سَيَحمَرُّ قَبلَ هذا
    /// الكوميت.</b> مُسَجَّلٌ في المَتجَر، جَلسَتُه صالِحَة، ولَيسَ مالِكَ
    /// الإعلان — وكانَ يَصيرُ الطَرَفَ المُقابِلَ بِنَقرَةٍ أَو <c>curl</c>.</summary>
    [Fact]
    public void A_logged_in_stranger_may_not_accept_an_offer_on_someone_elses_listing()
    {
        var v = DealAcceptAuthorization.Validate(Incoming(), By(Stranger));
        Assert.NotNull(v);
        Assert.Equal(DealAcceptAuthorization.ActorNotListingOwner, v!.Code);
        Assert.False(DealAcceptAuthorization.IsAllowed(Incoming(), By(Stranger)));
    }

    [Fact]
    public void The_initiator_may_not_accept_their_own_offer()
    {
        // بِمَرجِعٍ يُشيرُ إلَيه (أَضافَ إعلانَه إلى سَلَّتِه).
        var own = Incoming();
        own.Refs[DealsService.ListingOwnerRef] = Offerer.ToString();
        Assert.Equal(DealAcceptAuthorization.ActorIsInitiator, DealAcceptAuthorization.Validate(own, By(Offerer))!.Code);

        // وبِلا مَرجِع: كانَ يُجابُ «أَنتَ المُبادِر» (الصَفُّ القَديمُ هُنا)،
        // وصارَ يُجابُ «لا مالِك» — الغِيابُ صِفَةُ الصَفقَةِ ويَسبِقُ هُوِيَّةَ
        // الفاعِل. قُلِبَ بِقَرارٍ مَرئِيّ في المُراجَعَة؛ والمُبادِرُ مَردودٌ في
        // الحالَين.
        Assert.Equal(DealAcceptAuthorization.ListingOwnerMissing,
            DealAcceptAuthorization.Validate(Incoming(withOwnerRef: false), By(Offerer))!.Code);
    }

    [Fact]
    public void A_missing_deal_answers_deal_not_found()
        => Assert.Equal(DealAcceptAuthorization.DealNotFound, DealAcceptAuthorization.Validate(null, By(Owner))!.Code);

    [Fact]
    public void A_deal_that_left_Active_answers_deal_not_active()
        => Assert.Equal(DealAcceptAuthorization.DealNotActive,
            DealAcceptAuthorization.Validate(Incoming(DealStatus.Cancelled), By(Owner))!.Code);

    /// <summary>قَبولٌ ثانٍ — نَقرَةٌ مُكَرَّرَةٌ أَو مُتَسابِقَة — يُرَدُّ بِرَمزٍ لا
    /// يُبتلَعُ صامِتاً كَما كانَ (<c>AssignCounterpartyAsync</c> تُرجِعُ الصَفقَةَ
    /// بِلا تَغييرٍ ثُمَّ يُحاوَلُ التَقَدُّمُ بِفاعِلٍ خاطِئ).</summary>
    [Fact]
    public void A_deal_that_already_has_a_counterparty_answers_deal_already_accepted()
    {
        var accepted = Incoming();
        accepted.CounterpartyId = Owner;
        Assert.Equal(DealAcceptAuthorization.DealAlreadyAccepted, DealAcceptAuthorization.Validate(accepted, By(Owner))!.Code);
    }

    // ─── التَرتيب — القاعِدَة ٦: التَخويلُ يَسبِقُ فَحصَ الحالَة ────────

    /// <summary>غَريبٌ يَسأَلُ عَن صَفقَةٍ مَقبولَةٍ أَصلاً يُقالُ لَه «لَستَ
    /// المالِك» لا «قُبِلَت» — وإلّا صارَ خَطَأُ التَحَقُّقِ قِناعاً يُخبِرُ
    /// بِحالَةِ صَفقَةِ غَيرِه.</summary>
    [Fact]
    public void Authorization_is_answered_before_state_so_state_does_not_leak()
    {
        var accepted = Incoming(DealStatus.Completed);
        accepted.CounterpartyId = Owner;
        Assert.Equal(DealAcceptAuthorization.ActorNotListingOwner, DealAcceptAuthorization.Validate(accepted, By(Stranger))!.Code);
    }

    [Fact]
    public void Absence_is_answered_first_because_it_reveals_nothing()
        => Assert.Equal(DealAcceptAuthorization.DealNotFound, DealAcceptAuthorization.Validate(null, By(Stranger))!.Code);

    [Fact]
    public void Among_state_checks_inactivity_is_answered_before_a_prior_acceptance()
    {
        var d = Incoming(DealStatus.Cancelled);
        d.CounterpartyId = Owner;
        Assert.Equal(DealAcceptAuthorization.DealNotActive, DealAcceptAuthorization.Validate(d, By(Owner))!.Code);
    }

    // ─── الرِسالَة — لِلمُراجِعِ البَشَريّ لا لِلوغ وَحدَه ─────────────

    [Fact]
    public void Every_violation_carries_an_arabic_message_that_says_why()
    {
        var accepted = Incoming(); accepted.CounterpartyId = Owner;
        // المُبادِرُ يَقبَلُ عَرضَه عَلى إعلانِه هُوَ (المَرجِعُ يُشيرُ إلَيه) —
        // فَيَمُرُّ فَحصا المَرجِعِ والمالِكِ ويَقِفُ عِندَ «أَنتَ المُبادِر».
        var self = Incoming();
        self.Refs[DealsService.ListingOwnerRef] = Offerer.ToString();
        var cases = new (Deal? Deal, DealAcceptor By)[]
        {
            (null,                               By(Owner)),
            (Incoming(withOwnerRef: false),      By(Stranger)),
            (Incoming(),                         By(Stranger)),
            (self,                               By(Offerer)),
            (Incoming(DealStatus.Disputed),      By(Owner)),
            (accepted,                           By(Owner)),
        };

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (deal, by) in cases)
        {
            var v = DealAcceptAuthorization.Validate(deal, by);
            Assert.NotNull(v);
            Assert.True(DealAcceptAuthorization.Contains(v!.Code), $"رَمزٌ خارِجَ المَعجَم: {v.Code}");
            Assert.True(v.MessageAr.Length > 10, $"«{v.Code}» بِرِسالَةٍ أَقصَرَ مِن أَن تُفَسِّر.");
            seen.Add(v.Code);
        }
        // الحالاتُ السِتُّ تُخرِجُ الرُموزَ السِتَّةَ كُلَّها — لا رَمزَ بِلا مَسار.
        Assert.Equal(DealAcceptAuthorization.All.OrderBy(c => c, StringComparer.Ordinal), seen.OrderBy(c => c, StringComparer.Ordinal));
    }

    // ─── الحارِسُ في التَوقيع — مَقيسٌ بِالانعِكاسِ لا بِالدَعوى ─────────

    /// <summary>لِـ<c>AcceptAsync</c> <b>تَوقيعٌ واحِد</b>، وفيه وَسيطٌ مِن نَوعِ
    /// <see cref="DealAcceptor"/> إلزامِيّ، ولا حِملٌ قَديمٌ بِـ<c>Guid actorId</c>.
    /// والرَفضُ يَصِلُ المُنادي: النَوعُ المُعادُ يَحمِلُه.</summary>
    [Fact]
    public void AcceptAsync_has_one_signature_and_it_demands_the_authority()
    {
        var overloads = typeof(DealsService).GetMethods()
            .Where(m => m.Name == nameof(DealsService.AcceptAsync)).ToArray();
        Assert.Single(overloads);

        var ps = overloads[0].GetParameters();
        Assert.Contains(ps, p => p.ParameterType == typeof(DealAcceptor));
        Assert.DoesNotContain(ps, p => p.ParameterType == typeof(Guid) && p.Name == "actorId");
        Assert.Equal(typeof(Task<DealAcceptResult>), overloads[0].ReturnType);
    }

    /// <summary>
    /// <para><b>والنُقطَةُ تَمُرُّ بِالخِدمَةِ لا حَولَها</b> — هذا هُوَ الفَحصُ
    /// الَّذي أَحمَرَّ أَوَّلاً: جِسمُ <c>/{slug}/deals/{id:guid}/accept</c> يُنشِئُ
    /// <c>DealAcceptor</c> ويُنادي <c>AcceptAsync</c>، ولا يُعَيِّنُ الطَرَفَ ولا
    /// يُحَرِّكُ المَرحَلَةَ بِنَفسِه. والتَعليقُ القَديمُ «يَدخُل Deals
    /// بِمَرحَلَة Booked» زال — فَالكودُ يُدخِلُها <c>Offered</c>
    /// (<c>DealsService.StartAsync</c>).</para>
    /// </summary>
    [Fact]
    public void The_accept_endpoint_passes_the_authority_and_does_not_assign_the_clicker_itself()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500, "أَداةٌ عَمياء: مِلَفُّ النِقاطِ لَم يُقرَأ.");

        var start = text.IndexOf("\"/{slug}/deals/{id:guid}/accept\"", StringComparison.Ordinal);
        Assert.True(start > 0, "نُقطَةُ القَبولِ غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        var end = text.IndexOf(".RequireStoreWritable();", start, StringComparison.Ordinal);
        var body = text.Substring(start, end - start);

        Assert.Contains("Services.Deals.DealAcceptor(", body, StringComparison.Ordinal);
        Assert.Contains("deals.AcceptAsync(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("AssignCounterpartyAsync(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("AdvanceAsync(", body, StringComparison.Ordinal);

        // والرَفضُ يَصِلُ الشاشَةَ بِرَمزِه — نَسَقُ نُقطَةِ الإلغاءِ نَفسُه.
        Assert.Contains("res.Violation!.Code", body, StringComparison.Ordinal);

        Assert.DoesNotContain("يَدخُل Deals بِمَرحَلَة Booked", text, StringComparison.Ordinal);
    }
}
