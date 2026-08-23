using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Api;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>الثَغرَةُ ‏§١١٫٦ مُغلَقَةٌ في الخِدمَة، ومَقيسَةٌ مِن
/// طَرَفَيها.</b> كانَت <c>DealsService.CancelAsync</c> لا تَفحَصُ
/// الفاعِلَ إطلاقاً، فَـ<c>POST /{slug}/deals/{id}/cancel</c> يَقبَل
/// أَيَّ مُستَخدِمٍ في المَتجَر — <b>ومَعَ الإلغاءِ يَقَع
/// <c>RefundAsync</c></b>.</para>
///
/// <para><b>ولِماذا هُنا لا في اختِبارِ تَكامُل</b>: القَرارُ كُلُّه
/// دالَّةٌ نَقِيَّة (<c>Validate</c>)، فَقِياسُه لا يَحتاج قاعِدَةَ
/// بَيانات. والدالَّةُ النَقِيَّةُ هي بِعَينِها ما يَجعَل الحارِسَ
/// في <b>التَوقيع</b> لا في الجِسم: الجِسمُ يُطَبِّقُها ولا
/// يَحمِلُها.</para>
///
/// <para>ولِكُلّ رَمزٍ في المَعجَم <b>اختِبارٌ مُوجِبٌ وسالِب</b>
/// (القاعِدَة ٤).</para>
/// </summary>
public class DealCancelAuthorizationTests
{
    private static readonly Guid Initiator    = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Counterparty = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Stranger     = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static Deal DealOf(DealStatus status = DealStatus.Active) => new()
    {
        Id = Guid.NewGuid(), Pattern = "marketplace",
        Stage = DealStage.Paid, Status = status,
        AmountSar = 100,
        InitiatorId = Initiator, CounterpartyId = Counterparty,
    };

    private static DealCanceller By(Guid id, bool admin = false) => new(id, "فاعِل", admin);

    // ─── المَعجَم ──────────────────────────────────────────────────────

    [Fact]
    public void Exactly_three_violation_codes_and_they_are_these()
        => Assert.Equal(
            new[] { "deal_not_found", "actor_not_party", "deal_not_active" },
            DealCancelAuthorization.All);

    [Fact]
    public void A_code_outside_the_vocabulary_throws_at_composition_time()
        => Assert.Throws<ArgumentException>(() => DealCancelAuthorization.Require("actor_is_nice"));

    [Fact]
    public void Every_code_in_the_vocabulary_is_accepted()
    {
        foreach (var c in DealCancelAuthorization.All)
            Assert.Equal(c, DealCancelAuthorization.Require(c));
        Assert.Equal(3, DealCancelAuthorization.All.Count);
    }

    // ─── مُوجِب — مَن يَملِك الإلغاء ────────────────────────────────────

    [Fact]
    public void The_initiator_may_cancel()
        => Assert.Null(DealCancelAuthorization.Validate(DealOf(), By(Initiator)));

    [Fact]
    public void The_counterparty_may_cancel()
        => Assert.Null(DealCancelAuthorization.Validate(DealOf(), By(Counterparty)));

    /// <summary>مُشرِفُ المَتجَر يُلغي وهو لَيسَ طَرَفاً — وهذا هُوَ
    /// المَسارُ الَّذي يَستَعمِلُه <c>/studio/apps/{slug}/deals/{id}/cancel</c>
    /// بَعدَ <c>StudioOwnsAsync</c>.</summary>
    [Fact]
    public void A_store_admin_who_is_not_a_party_may_cancel()
        => Assert.Null(DealCancelAuthorization.Validate(DealOf(), By(Stranger, admin: true)));

    [Fact]
    public void IsAllowed_agrees_with_Validate_on_the_allowed_case()
        => Assert.True(DealCancelAuthorization.IsAllowed(DealOf(), By(Initiator)));

    // ─── سالِب — الثَغرَةُ نَفسُها ─────────────────────────────────────

    /// <summary><b>هذا هُوَ الاختِبارُ الَّذي كانَ سَيَحمَرّ قَبلَ هذا
    /// الكوميت.</b> مُستَخدِمٌ في المَتجَر، جَلسَتُه صالِحَة، ولَيسَ
    /// طَرَفاً في الصَفقَة — وكانَ يُلغيها ويَستَرِدُّ مالَها.</summary>
    [Fact]
    public void A_logged_in_stranger_may_not_cancel_someone_elses_deal()
    {
        var v = DealCancelAuthorization.Validate(DealOf(), By(Stranger));

        Assert.NotNull(v);
        Assert.Equal(DealCancelAuthorization.ActorNotParty, v!.Code);
        Assert.False(DealCancelAuthorization.IsAllowed(DealOf(), By(Stranger)));
    }

    [Fact]
    public void A_missing_deal_answers_deal_not_found()
    {
        var v = DealCancelAuthorization.Validate(null, By(Initiator));
        Assert.Equal(DealCancelAuthorization.DealNotFound, v!.Code);
    }

    [Fact]
    public void A_deal_that_left_Active_answers_deal_not_active()
    {
        var v = DealCancelAuthorization.Validate(DealOf(DealStatus.Completed), By(Initiator));
        Assert.Equal(DealCancelAuthorization.DealNotActive, v!.Code);
    }

    // ─── التَرتيب — القاعِدَة ٦: التَخويلُ يَسبِقُ فَحصَ الحالَة ────────

    /// <summary>
    /// <para><b>لَو سُئِلَ عَن الحالَةِ أَوَّلاً لَتَسَرَّبَت</b>:
    /// غَريبٌ يَسأَل عَن صَفقَةٍ مُكتَمِلَة يَنبَغي أَن يُقالَ لَه
    /// «لَستَ طَرَفاً» لا «هي مُكتَمِلَة» — وإلّا صارَ خَطَأُ
    /// التَحَقُّقِ قِناعاً يُخبِرُ بِحالَةِ صَفقَةِ غَيرِه.</para>
    /// </summary>
    [Fact]
    public void Authorization_is_answered_before_state_so_state_does_not_leak()
    {
        var v = DealCancelAuthorization.Validate(DealOf(DealStatus.Completed), By(Stranger));
        Assert.Equal(DealCancelAuthorization.ActorNotParty, v!.Code);
    }

    /// <summary>والغِيابُ وَحدَه يَسبِقُ التَخويل — لِأَنَّه لا يُفشي
    /// شَيئاً.</summary>
    [Fact]
    public void Absence_is_answered_first_because_it_reveals_nothing()
    {
        var v = DealCancelAuthorization.Validate(null, By(Stranger));
        Assert.Equal(DealCancelAuthorization.DealNotFound, v!.Code);
    }

    // ─── الرِسالَة — لِلمُراجِعِ البَشَريّ لا لِلوغ وَحدَه ─────────────

    [Fact]
    public void Every_violation_carries_an_arabic_message_that_says_why()
    {
        var cases = new (Deal? Deal, DealCanceller By)[]
        {
            (null,                          By(Initiator)),
            (DealOf(),                      By(Stranger)),
            (DealOf(DealStatus.Cancelled),  By(Initiator)),
        };

        foreach (var (deal, by) in cases)
        {
            var v = DealCancelAuthorization.Validate(deal, by);
            Assert.NotNull(v);
            Assert.True(DealCancelAuthorization.Contains(v!.Code), $"رَمزٌ خارِجَ المَعجَم: {v.Code}");
            Assert.True(v.MessageAr.Length > 10, $"«{v.Code}» بِرِسالَةٍ أَقصَرَ مِن أَن تُفَسِّر.");
        }
    }

    // ─── الحارِسُ في التَوقيع — مَقيسٌ بِالانعِكاس لا بِالدَعوى ─────────

    /// <summary>
    /// <para><b>لا حِملٌ زائِدٌ يَتَجاوَزُ الحارِس.</b> القاعِدَةُ ٦
    /// تَقول «الحارِسُ في التَوقيع»، ودَعواها تُقاس هُنا: لِـ
    /// <c>CancelAsync</c> <b>تَوقيعٌ واحِد</b>، وفيه وَسيطٌ مِن نَوع
    /// <see cref="DealCanceller"/> إلزامِيّ. فَلَو أُعيدَ يَوماً
    /// حِملٌ قَديمٌ بِـ<c>Guid actorId</c> — وهو الشَكلُ الَّذي حَمَلَ
    /// الثَغرَة — سَقَطَ هذا الاختِبار.</para>
    /// </summary>
    [Fact]
    public void CancelAsync_has_one_signature_and_it_demands_the_authority()
    {
        var overloads = typeof(DealsService)
            .GetMethods()
            .Where(m => m.Name == nameof(DealsService.CancelAsync))
            .ToArray();

        Assert.Single(overloads);

        var ps = overloads[0].GetParameters();
        Assert.Contains(ps, p => p.ParameterType == typeof(DealCanceller));
        Assert.DoesNotContain(ps, p => p.ParameterType == typeof(Guid) && p.Name == "actorId");

        // والرَفضُ يَصِلُ المُنادي: النَوعُ المُعاد يَحمِلُه.
        Assert.Equal(typeof(Task<DealCancelResult>), overloads[0].ReturnType);
    }

    // ─── «طَرَف» تَعريفٌ واحِد — Deal.IsParty ──────────────────────────

    /// <summary><b>المُوجِبُ والسالِبُ لِلتَعريفِ الواحِد</b>: المُبادِرُ
    /// والطَرَفُ الثاني طَرَفان؛ الغَريبُ و<c>Guid.Empty</c> لا — حَتّى
    /// على صَفقَةٍ بِلا طَرَفٍ ثانٍ (‏<c>CounterpartyId == null</c> لا
    /// يَجعَل المُعَرِّفَ الفارِغَ طَرَفاً).</summary>
    [Fact]
    public void Deal_IsParty_admits_both_parties_and_no_one_else()
    {
        var deal = DealOf();
        Assert.True(deal.IsParty(Initiator));
        Assert.True(deal.IsParty(Counterparty));
        Assert.False(deal.IsParty(Stranger));
        Assert.False(deal.IsParty(Guid.Empty));

        var offerOnly = DealOf();
        offerOnly.CounterpartyId = null;
        Assert.True(offerOnly.IsParty(Initiator));
        Assert.False(offerOnly.IsParty(Counterparty));
        Assert.False(offerOnly.IsParty(Guid.Empty));
    }

    /// <summary>
    /// <para><b>حارِسانِ لِسُؤالَين — وتَعريفٌ واحِد.</b>
    /// <c>DealApi.IsParty</c> تَحكُم ما يَراهُ السائِل، و
    /// <c>DealCancelAuthorization.Validate</c> تَحكُم ما يَفعَلُه؛ ولَو
    /// حَمَلَ كُلٌّ مِنهُما نُسخَتَه مِن «طَرَف» لانجَرَفا يَوماً
    /// فَرَأى مَن لا يُلغي أَو أَلغى مَن لا يَرى. فَهذا الاختِبارُ
    /// يُساوي بَينَهُما على مَصفوفَةِ فاعِلين × صَفقات — لا على
    /// حالَةٍ واحِدَة.</para>
    /// </summary>
    [Fact]
    public void The_view_guard_and_the_cancel_guard_read_one_definition_of_party()
    {
        var withCounterparty = DealOf();
        var offerOnly = DealOf();
        offerOnly.CounterpartyId = null;

        var actors = new[] { Initiator, Counterparty, Stranger, Guid.Empty, Guid.NewGuid() };
        var deals  = new[] { withCounterparty, offerOnly };
        var checkedPairs = 0;

        foreach (var deal in deals)
        foreach (var actor in actors)
        {
            var single = deal.IsParty(actor);
            Assert.Equal(single, DealApi.IsParty(deal, actor));
            Assert.Equal(single, DealCancelAuthorization.Validate(deal, By(actor)) is null);
            checkedPairs++;
        }

        Assert.Equal(actors.Length * deals.Length, checkedPairs);   // القاعِدَة ١٠: العَدّادُ يُطبَع
    }

    /// <summary>
    /// <para><b>الحَدُّ الَّذي لا يُقاس آلِيّاً يَنهار</b> (القاعِدَة ٢):
    /// «تَعريفٌ واحِد» دَعوى حَتّى يُمسَح المَصدَر. يُمسَح مَشروعُ
    /// السوق كُلُّه عَن شَرطِ «المُبادِرُ أَو الطَرَفُ الثاني» بِنَفسِ
    /// المُعَرِّفِ على الطَرَفَين، ويُثَبَّت أَين يُسمَح بِظُهورِه:
    /// <c>Deal.cs</c> (التَعريف) و<c>DealsService.cs</c> مَرَّةً واحِدَةً
    /// (‏<c>ListForUserAsync</c>: تَعبيرُ LINQ يُتَرجِمُه Marten إلى SQL
    /// ولا يَقبَل نِداءَ دالَّةٍ مَكانَه). ظُهورٌ ثالِثٌ — أَو انتِقالُ
    /// الثاني — يُحمِّر هذا الاختِبار، فَيُقَرَّر في مُراجَعَةٍ لا
    /// بِصَمت.</para>
    /// </summary>
    [Fact]
    public void Party_is_spelled_once_outside_the_query_Marten_must_translate()
    {
        var root = ThemeZeroEquivalenceTests.RepoRoot;
        var project = Path.Combine(root, "libs", "templates", "ACommerce.Templates.Customer.Marketplace");
        Assert.True(Directory.Exists(project), $"لا مَشروعَ في {project}");

        // المُعَرِّفُ نَفسُه على الطَرَفَين (\1) — فَلا يَلتَقِط
        // «CounterpartyId == null && InitiatorId != ownerId» (عَرضٌ وارِد)،
        // ويَلتَقِط الصِيغَتَين بِتَرتيبَيهِما.
        var forward  = new Regex(@"(\w+\.)?InitiatorId\s*[!=]=\s*([\w.]+)\s*(\|\||&&)\s*(\w+\.)?CounterpartyId\s*[!=]=\s*\2\b");
        var backward = new Regex(@"(\w+\.)?CounterpartyId\s*[!=]=\s*([\w.]+)\s*(\|\||&&)\s*(\w+\.)?InitiatorId\s*[!=]=\s*\2\b");

        var files = Directory.EnumerateFiles(project, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.True(files.Count > 0, "حارِسُ العَمى: صِفرُ مِلَفٍّ مَفحوص.");

        var hits = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in files)
        {
            var text = File.ReadAllText(f);
            var n = forward.Matches(text).Count + backward.Matches(text).Count;
            if (n > 0) hits[Path.GetRelativePath(project, f).Replace('\\', '/')] = n;
        }

        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Services/Deals/Deal.cs"]         = 1,   // التَعريفُ الواحِد
            ["Services/Deals/DealsService.cs"] = 1,   // ListForUserAsync — LINQ لِـ Marten
        };

        Assert.Equal(
            expected.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"),
            hits.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"));
    }
}
