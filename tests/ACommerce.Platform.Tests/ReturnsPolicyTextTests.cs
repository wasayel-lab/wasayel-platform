using System.Text.RegularExpressions;
using ACommerce.Kit.Compliance;
using ACommerce.Platform.I18n;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ نَصُّ الاستِرجاعِ المَنشورُ كانَ يَعِدُ بِما لا يوجَد ═════════════════
//
// **المَقيسُ** (موجَزُ الفَجَوات P0-5، ‏2026-10-10): `legal.returns.how` يُصَيَّرُ
// عَلى `/{slug}/legal` لِكُلِّ مَتجَر، وكانَ يَقول: «الاستِرجاع عَبر صَفحَة
// «طَلَباتي» → اِختَر الطَّلَب → «اِستِرجاع». المَنصَّة تَستَرجِع بِكَامِل المَبلَغ
// في ٣-٥ أَيّام عَمَل.» — وثَلاثُ كَذِباتٍ في جُملَتَين: لا صَفحَةَ «طَلَباتي»
// لِلمُشتَرَيات، ولا زِرَّ «اِستِرجاع»، والمَنصَّةُ لا تَمسِكُ ثَمَنَ البَيعِ
// أَصلاً (‏ق‑٦؛ `platform.terms.s2_note`: «لا تَكونُ طَرَفاً في أَيِّ صَفقَة»).
// وكانَ فاحِصُ الالتِزامِ يَرى التَناقُضَ (`refund_liability_contradicts_platform_terms`
// في `tenant_returns_art13`) **ولا اختِبارَ يَقيسُه عَلى القامُوسِ الحَيّ**.
//
// **القَرارُ المُنَفَّذُ تَحتَ التَفويض** (DECISIONS-PENDING م‑٢ الاتِّجاه (أ)،
// مُسَجَّلٌ في سِجِلِّ أَسئِلَةِ المالِك): المَنصَّةُ وَسيطٌ والبائِعُ هُوَ
// المُلتَزِم — فَالنَصُّ يَصِفُ الواقِع: المُشتَري يَطلُبُ الاستِرجاعَ مِن البائِع
// عَبرَ صَفحَةِ الصَفقَةِ أَو مُراسَلَتِه، ولا استِرجاعَ مِن المَنصَّة.
//
// **وما بَقِيَ أَحمَرَ بِقَصد**: `return_action_reachable` (المَسارُ
// `/{slug}/deals/{id}/return`) يَنتَظِرُ قَرارَ المالِكِ م‑٣ — لا يُبنى هُنا
// ولا يُخفى: يُثَبَّتُ أَحمَرَ حَتّى يُقرَّر، فَيَومَ يُبنى يَحمَرُّ هذا الفَحصُ
// ويُرفَعُ بِقَرارٍ مَرئِيّ.
// (‏2026-10-29، قَرارُ المالِك د30 «١» — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): **وقَرَّرَ المالِكُ فَبُني** — «زرّ «طلب استرجاع» على صفحة الصفقة، يظهر
// للمشتري بعد التسليم ولمدّة سبعة أيام منه»؛ فَقُلِبَ صَفُّه بِاسمِه أَدناه، والمادَّةُ خَضراء.
//
// **والدَورَةُ الثانِيَة (مُراجَعَةُ ‏2026-10-11)**: النَصُّ الأَوَّلُ البَديلُ قالَ
// «تَواصَل مَع البائِع مِن صَفحَة الصَّفقَة … أَو بِمُراسَلَتِه» — **وصَفحَةُ
// الصَفقَةِ كانَت بِلا أَيِّ مَدخَلٍ إلى البائِع** (صِفرُ `href`/`form` إلى
// `vendor/` أَو `chat` في `MyDealDetail.razor`؛ والمَدخَلُ الوَحيدُ مِن جِهَةِ
// المُشتَري في `TenantListingDetail` مَشروطٌ بِتَقييمٍ واحِدٍ فَأَكثَر) — أَي
// عَينُ عَيبِ P0-5 بِصِياغَةٍ أُخرى، والفَحصُ هُنا كانَ يَقيسُ «صَفقاتي» وَحدَها.
// فَصارَت الصَفحَةُ تَحمِلُ زِرَّ «تَواصَل مَع البائِع» (مِفتاحُ صَفحَةِ البائِعِ
// نَفسُه، عَلى أُنبوبِ `users/{id}/chat` القائِم)، وصارَ النَصُّ يُسَمّي الصَفحَةَ
// والزِرَّ بِحَرفِ قامُوسِهِما — **وكُلُّ «…» في النَصِّ يُقاسُ أَنَّه قيمَةُ
// مِفتاحٍ حَيّ، وأَنّ الصَفحَةَ تَرسُمُه، وأَنّ أُنبوبَه وغُرفَتَه مُسَجَّلان**.

public class ReturnsPolicyTextTests
{
    private const string ObligationId = "tenant_returns_art13";

    /// <summary>الالتِزامُ بِعَينِه كَما يَقرَؤُه الكاتالوج — لا نُسخَة.</summary>
    private static ObligationDefinition Obligation()
    {
        var o = ObligationCatalog.Find(ObligationId);
        Assert.NotNull(o);
        return o!;
    }

    /// <summary>لَقطَةٌ حَقيقِيَّة: القامُوسُ العَرَبيُّ المَضمونُ وجَدوَلُ
    /// المَساراتِ المَمسوحُ مِن المَصدَر — نَفسُ ما تَفعَلُه
    /// <c>ComplianceInspectorTests.RepoSubject</c>.</summary>
    private static ComplianceSubject LiveSubject(IDictionary<string, string?>? overrides = null)
    {
        var texts = Obligation().Evidence
            .Where(e => EvidenceKinds.ReadsText(e.Kind))
            .Select(e => e.Target)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(k => k, k => LocaleCatalog.Find(LocaleCatalog.Arabic, k), StringComparer.Ordinal);
        if (overrides is not null)
            foreach (var (k, v) in overrides) texts[k] = v;

        Assert.True(texts.Count >= 3, $"أَداةٌ عَمياء: {texts.Count} مِفتاحاً فَقَط.");
        Assert.True(ComplianceInspectorTests.RepoRoutes.Count > 100, "أَداةٌ عَمياء: جَدوَلُ المَساراتِ فارِغ.");

        return new ComplianceSubject(ComplianceLevels.Tenant, "theme-demo", "قِياس", texts,
            ComplianceInspectorTests.RepoRoutes);
    }

    private static ComplianceReport Inspect(ComplianceSubject subject)
        => ComplianceInspector.Inspect([Obligation()], subject);

    // ─── الأَخضَرُ الجَديد ──────────────────────────────────────────────

    /// <summary><b>هذا هُوَ الفَحصُ الَّذي كانَ أَحمَرَ قَبلَ هذا الكوميت</b>
    /// عَلى القامُوسِ الحَيّ: النَصُّ المَنشورُ لا يَعِدُ بِأَنّ المَنصَّةَ
    /// تَستَرجِع.</summary>
    [Fact]
    public void The_published_returns_text_no_longer_promises_a_platform_refund()
    {
        var report = Inspect(LiveSubject());
        Assert.DoesNotContain("refund_liability_contradicts_platform_terms", report.RejectionCodes);
    }

    /// <summary>ويُسَمّي البائِعَ مُلتَزِماً، ولا يَعِدُ بِصَفحَةٍ أَو زِرٍّ لا
    /// وُجودَ لَهُما — ويُسَمّي الزِرَّ المَوجودَ بِحَرفِه.</summary>
    [Fact]
    public void The_text_names_the_vendor_as_the_liable_party_and_promises_no_button()
    {
        var how = LocaleCatalog.Find(LocaleCatalog.Arabic, "legal.returns.how");
        Assert.False(string.IsNullOrWhiteSpace(how));

        Assert.Contains("البائِع", how!, StringComparison.Ordinal);
        Assert.Contains("صَفقاتي", how, StringComparison.Ordinal);            // الصَفحَةُ المَوجودَةُ فِعلاً
        Assert.Contains("«تَواصَل مَع البائِع»", how, StringComparison.Ordinal); // الزِرُّ المَوجودُ فِعلاً عَلى صَفحَةِ الصَفقَة
        Assert.DoesNotContain("طَلَباتي", how, StringComparison.Ordinal);     // صَفحَةٌ لا وُجودَ لَها لِلمُشتَرَيات
        Assert.DoesNotContain("«اِستِرجاع»", how, StringComparison.Ordinal);  // زِرٌّ لا وُجودَ لَه
        Assert.DoesNotContain("تَستَرجِع", how, StringComparison.Ordinal);
        Assert.DoesNotContain("أَيّام عَمَل", how, StringComparison.Ordinal); // مُدَّةٌ تَعِدُ بِها المَنصَّةُ عَن غَيرِها

        // والوَثيقَةُ الأُخرى الحَيَّةُ الَّتي كانَ يُناقِضُها ما زالَت تَقولُ
        // إنّ المَنصَّةَ لَيسَت طَرَفاً — فَلا تَناقُضَ بَينَ وَثيقَتَين.
        var note = LocaleCatalog.Find(LocaleCatalog.Arabic, "platform.terms.s2_note");
        Assert.Contains("لا تَكونُ طَرَفاً", note!, StringComparison.Ordinal);
    }

    /// <summary><b>والعَكسُ مَقيس</b>: لَو عادَ النَصُّ إلى حَرفِه القَديم
    /// لَاحمَرَّ الفاحِصُ — وبِدونِ هذا الطَرَفِ يَبقى الفَحصُ أَعلاهُ دَعوى
    /// «لَم أَجِد» لا «فَحَصتُ فَلَم أَجِد».</summary>
    [Fact]
    public void The_exact_old_wording_would_still_be_caught_today()
    {
        var report = Inspect(LiveSubject(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["legal.returns.how"] =
                "الاستِرجاع عَبر صَفحَة «طَلَباتي» → اِختَر الطَّلَب → «اِستِرجاع».\n" +
                "                   المَنصَّة تَستَرجِع بِكَامِل المَبلَغ في ٣-٥ أَيّام عَمَل.",
        }));
        Assert.Contains("refund_liability_contradicts_platform_terms", report.RejectionCodes);
    }

    // ─── البابُ الَّذي يُسَمّيه النَصُّ مَوجودٌ ويُفضي ─────────────────

    private static readonly Regex QuotedLabel = new("«([^»]+)»", RegexOptions.Compiled);

    /// <summary><b>كُلُّ «…» في النَصِّ المَنشورِ قيمَةُ مِفتاحٍ حَيّ</b> — لا
    /// اسمَ شاشَةٍ أَو زِرٍّ مُختَرَع. والعَدُّ يُطبَع: صِفرُ تَسمِيَةٍ يَعني أَداةً
    /// عَمياء لا نَصّاً بَريئاً.</summary>
    [Fact]
    public void Every_label_the_returns_text_quotes_is_a_live_dictionary_value()
    {
        var how = LocaleCatalog.Find(LocaleCatalog.Arabic, "legal.returns.how")!;
        var quoted = QuotedLabel.Matches(how).Select(m => m.Groups[1].Value).ToList();
        Assert.True(quoted.Count >= 2, $"أَداةٌ عَمياء: {quoted.Count} تَسمِيَةً مُقتَبَسَةً فَقَط في النَصّ.");

        var values = LocaleCatalog.Raw(LocaleCatalog.Arabic).Select(e => e.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var label in quoted)
            Assert.True(values.Contains(label), $"«{label}» لَيسَت قيمَةَ أَيِّ مِفتاحٍ في القامُوس — بابٌ مُسَمّىً لا وُجودَ لَه.");

        // والتَسمِيَتانِ بِعَينِهِما: صَفحَةُ «صَفقاتي» وزِرُّ صَفحَةِ البائِع.
        Assert.Equal(LocaleCatalog.Find(LocaleCatalog.Arabic, "deals.mine.title"), quoted[0]);
        Assert.Equal(LocaleCatalog.Find(LocaleCatalog.Arabic, "vendors.profile.contact"), quoted[1]);
    }

    /// <summary>
    /// <para><b>وصَفحَةُ الصَفقَةِ تَحمِلُ الزِرَّ الَّذي يُسَمّيه النَصّ، وبابُه
    /// يُفضي</b>: المِفتاحُ نَفسُه يُرسَمُ في <c>MyDealDetail.razor</c> لِلمُبادِرِ
    /// حَيثُ يُعرَفُ البائِع، عَلى أُنبوبِ <c>users/{id}/chat</c> القائِم، والأُنبوبُ
    /// وغُرفَةُ المُحادَثَةِ مُسَجَّلانِ بِالماسِحِ الواحِد (القاعِدَة ٨). هذا هُوَ
    /// الفَحصُ الَّذي كانَ سَيَحمَرُّ عَلى الصَفحَةِ قَبلَ هذا الكوميت.</para>
    /// </summary>
    [Fact]
    public void The_deal_page_carries_the_contact_button_the_text_names_and_its_pipe_and_room_exist()
    {
        var page = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "MyDealDetail.razor"));
        Assert.True(page.Length > 1000, "أَداةٌ عَمياء: `MyDealDetail.razor` لَم يُقرَأ.");

        Assert.Contains("@L.Markup(\"vendors.profile.contact\")", page, StringComparison.Ordinal);
        Assert.Contains("@if (isInitiator && vendorId is { } vid)", page, StringComparison.Ordinal);
        Assert.Contains("action=\"@($\"/{Slug}/users/{vid}/chat\")\"", page, StringComparison.Ordinal);

        // والبائِعُ يُقرَأُ مِن المَرجِعِ الَّذي تَكتُبُه نُقاطُ العَرضِ والسَلَّة، لا مِن سِلسِلَةٍ ثانِيَة.
        Assert.Contains("vendorId = Guid.TryParse(ownerRef, out var ownerGuid) ? ownerGuid : deal.CounterpartyId;", page, StringComparison.Ordinal);

        var routes = ComplianceInspectorTests.RepoRoutes;
        Assert.Contains("/{slug}/users/{userId}/chat", routes);
        Assert.Contains("/{slug}/chats/{conversationId}", routes);
    }

    // ─── الأَحمَرُ المُعلَن ─────────────────────────────────────────────

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-29، قَرارُ المالِك د30 «١»؛ كانَ <c>The_return_route_is_still_absent_and_that_is_declared_not_hidden</c> — الشاهِدُ
    /// <c>return_action_reachable</c> أَحمَرُ بِقَصدٍ حَتّى يُقَرِّرَ المالِك): بُنِيَ زِرُّ «طَلَب استِرجاع» وبابُه <c>/{slug}/deals/{id}/return</c> — فَالمادَّةُ ١٣
    /// خَضراءُ عَلى المُستَودَعِ الحَيّ بِلا رَمزِ رَفضٍ واحِد، والمَسارُ في جَدوَلِ المَساراتِ المَمسوح، وصَفحَةُ الصَفقَةِ تَرسُمُه بِبَوّابَتِه.</para>
    /// </summary>
    [Fact]
    public void The_return_route_is_registered_and_the_article_is_met()
    {
        var report = Inspect(LiveSubject());
        Assert.Contains("/{slug}/deals/{id}/return", ComplianceInspectorTests.RepoRoutes);
        Assert.DoesNotContain("return_action_absent", report.RejectionCodes);
        Assert.Empty(report.RejectionCodes);
        var page = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/MyDealDetail.razor"));
        Assert.Contains("<form method=\"post\" action=\"@($\"/{Slug}/deals/{deal.Id}/return\")\">", page, StringComparison.Ordinal);
    }

    // ─── مَن يَرُدُّ ثَمَنَ البِطاقَة (تَدقيقُ الإطلاق brief-P0-5، ‏2026-10-25) ───────────────────────────────
    //
    // **المَقيس** (‏2026-10-24 عِندَ `dace7cd`؛ أُعيدَ عِندَ `8965745`): النَصُّ يَقولُ «البائِعُ … يَرُدّ المَبلَغ بِالطَّريقَة الَّتي دُفِع بِها»،
    // والبِطاقَةُ تُقبَضُ بِمِفتاحِ ميسّر الَّذي لَصَقَه **صاحِبُ المَتجَر** (`GatewayPayments`، ‏ADR-032 الخُطُوات 8–10) — فَثَمَنُ طَلَبٍ مُلغًى دُفِعَ
    // بِالبِطاقَةِ يَستَرجِعُه صاحِبُ المَتجَرِ مِن لَوحَةِ بَوّابَتِه («ب» S9·٨: يَدَوِيّ)، وصَفحَةُ الصَفقَةِ تَقولُه بِسَطرِ `GatewayRefundDue`
    // (`deals.detail.event_gateway_refund_due`). والنَقدُ وَحدَه يَرُدُّه البائِعُ الَّذي قَبَضَه عِندَ التَسليم. يُثَبَّتُ «اليَوم» هُنا (القاعِدَة ٣)
    // ويُقلَبُ بِاسمِه في كوميتِ الصِياغَة؛ والصُفوفُ أَعلاه تَبقى بِلا تَعديلِ حَرف.

    /// <summary>ما تَقولُه صَفحَةُ الصَفقَةِ عَن طَلَبٍ مُلغًى دُفِعَ بِالبِطاقَة — والكاتِبُ سَطرُ <c>GatewayRefundDue</c> وحدَه.</summary>
    /// <remarks><b>قُلِبَ بِاسمِه</b> (‏2026-10-29، قَرارُ المالِك د3 «د»: «سطر «الاسترجاع المستحقّ» موجود اليوم في صفحتَي المشتري والاستوديو، فلا يتغيّر فيه إلّا نصّ المشتري،
    /// لأن نصّه الحالي يحيله إلى لوحة لا يملكها»؛ تَوصيفُه <c>b55fead</c>) — كانَ <c>The_deal_page_says_a_paid_cancelled_card_order_is_refunded_from_the_store_gateway</c>
    /// بِنَصِّ «دُفِعَ ثَمَنُ طَلَبٍ مُلغًى: استِرجاعُه مِن لَوحَةِ بَوّابَةِ المَتجَر». والآن يُطَمأَنُ المُشتَري عَلى مالِه، وصاحِبُ المَتجَرِ يُخبَرُ بِإشعارٍ يُعطيه الفِعل.</remarks>
    [Fact]
    public void The_deal_page_tells_the_buyer_of_a_paid_cancelled_card_order_that_the_store_was_told_to_refund_his_card()
    {
        var due = LocaleCatalog.Find(LocaleCatalog.Arabic, "deals.detail.event_gateway_refund_due");
        Assert.Equal("أُلغِيَ الطَّلَبُ بَعدَ دَفعِه — أُبلِغَ المَتجَرُ بِرَدِّ المَبلَغِ إلى بِطاقَتِك.", due);
        Assert.DoesNotContain("لوحة", AccountDeletionRetentionTests.Bare(due!), StringComparison.Ordinal);
        var page = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "MyDealDetail.razor"));
        Assert.Contains("DealEvent.GatewayRefundDue => L[\"deals.detail.event_gateway_refund_due\"]", page, StringComparison.Ordinal);
    }

    /// <summary>صَدرُ النَصِّ قَبلَ الجُملَةِ الَّتي تَبَدَّلَت، وعَجُزُه بَعدَها — بِحَرفِهِما (‏<c>236982d</c> وما قَبلَه).</summary>
    internal const string HowLead = "لِطَلَب الاستِرجاع افتَح الصَّفقَة مِن «صَفقاتي» واضغَط «تَواصَل مَع البائِع»؛ البائِعُ هُوَ مَن يَقبَل الاستِرجاع";
    internal const string HowTail = "، والمَنصَّةُ وَسيطٌ لا تَقبِض ثَمَنَ الصَّفقَة ولا تَرُدّه.";

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ الصِياغَة)</b> — كانَ
    /// <c>Today_the_returns_text_says_the_vendor_refunds_by_the_method_paid_even_for_card_orders_the_store_gateway_collected</c> (‏<c>236982d</c>).
    /// <b>والآن</b> النَقدُ يَرُدُّه البائِعُ الَّذي قَبَضَه، والبِطاقَةُ يَرُدُّها صاحِبُ المَتجَرِ مِن لَوحَةِ البَوّابَةِ الَّتي قَبَضَتها — وهي العِبارَةُ
    /// نَفسُها الَّتي تَقولُها صَفحَةُ الصَفقَةِ لِسَطرِ <c>GatewayRefundDue</c> (الصَفُّ أَعلاه)؛ والفَرقُ مَحصورٌ بَينَ الصَدرِ والعَجُزِ بِحَرفِهِما.</summary>
    [Fact]
    public void The_returns_text_says_cash_is_refunded_by_the_vendor_and_card_by_the_store_owner_from_its_gateway()
    {
        var how = LocaleCatalog.Find(LocaleCatalog.Arabic, "legal.returns.how")!;
        Assert.StartsWith(HowLead, how, StringComparison.Ordinal);
        Assert.EndsWith(HowTail, how, StringComparison.Ordinal);
        var middle = how[HowLead.Length..^HowTail.Length];
        Assert.Equal("، وما دُفِع نَقداً عِندَ التَّسليم يَرُدّه البائِع، وما دُفِع بِالبِطاقَة يَرُدّه صاحِبُ المَتجَر مِن لَوحَة بَوّابَة الدَّفع الَّتي قَبَضَته", middle);
        Assert.DoesNotContain("بِالطَّريقَة الَّتي دُفِع بِها", how, StringComparison.Ordinal);

        // وصَفحَةُ الصَفقَةِ والنَصُّ يَقولانِ الشَيءَ نَفسَه: لَوحَةُ بَوّابَةِ المَتجَر — بِلا حَرَكات.
        // (‏2026-10-29، قَرارُ المالِك د3 «د» — ولا يُعادُ حَرفٌ فَوقَه: صارَ سَطرُ المُشتَري يَقولُ ما عَلى المَتجَرِ لا أَينَ يَفعَلُه — «أُبلِغَ المَتجَرُ بِرَدِّ المَبلَغِ
        // إلى بِطاقَتِك» — والاتِّفاقُ بَينَهُما أَنّ المَتجَرَ يَرُدُّ ثَمَنَ البِطاقَة؛ واللَوحَةُ في إشعارِ صاحِبِ المَتجَرِ وسَطرِ الاستوديو.)
        var due = AccountDeletionRetentionTests.Bare(LocaleCatalog.Find(LocaleCatalog.Arabic, "deals.detail.event_gateway_refund_due")!);
        Assert.Contains("ابلغ المتجر برد المبلغ الى بطاقتك", due.Replace("أ", "ا").Replace("إ", "ا"), StringComparison.Ordinal);
        Assert.Contains("لوحة بوابة الدفع", AccountDeletionRetentionTests.Bare(how), StringComparison.Ordinal);
        Assert.Contains("صاحب المتجر", AccountDeletionRetentionTests.Bare(how), StringComparison.Ordinal);
    }
}
