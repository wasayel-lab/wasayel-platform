using System.Text;
using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ مَرحَلَةِ الدَفعِ في `DealsService.AdvanceAsync` — قَبلَ التَبديل ══
//
// ‏ADR-032 §٢‑هـ الخُطوَة 1: «كوميتُ تَوصيفٍ مُستَقِلّ … `AdvanceAsync` يَحسُبُ
// العَمولَةَ ويَستَدعي `CaptureAsync` مَرَّة». وهذا هُوَ الكوميت. يُثَبِّتُ ما
// يَفعَلُه الكودُ **اليَوم** (‏2026-10-11، `DealsService.cs:161-189`) حَرفاً،
// ثُمَّ يُسقَطُ مِنه بِقَرارٍ مَرئِيٍّ ما يَمَسُّ العَمولَةَ وَحدَها في كوميتِ
// الخُطوَة 3 (‏§٢‑ب) — ويَبقى ما سِواها أَخضَرَ بِلا تَعديلِ حَرف (القاعِدَة ٣).
//
// **القياسُ الَّذي كَتَبَ هذا المِلَفّ**: `grep CommissionSar|AdvanceAsync|
// PlatformCommissionRate tests/` = **صِفرُ مِلَفّ** يَومَ ‏2026-10-10 — سَبعَةُ
// مِلَفّاتٍ تَذكُرُ `DealsService` ولا يَستَدعي واحِدٌ مِنها `AdvanceAsync` إلى
// `Paid`. فَالسُلوكُ الَّذي سَيُبَدَّلُ لَم يَكُن مَوصوفاً، وتَبديلُ غَيرِ
// المَوصوفِ لا يُقاس.
//
// **ولِماذا شَطرانِ لا شَطر**:
//   • **تَوصيفٌ مِن المَصدَر** يَعمَلُ في كُلِّ تَشغيل — يَعُدُّ مَواضِعَ الكِتابَةِ
//     والنِداءِ وحُرّاسَها نَصّاً، كَما تَفعَلُ `CheckoutPaymentTests` و
//     `WriteEndpointGuardTests`. وهُوَ الحَكَمُ في CI.
//   • **بُرهانٌ حَيٌّ** عَلى Postgres بِنَفسِ عَقدِ `Live*ProofTests` (يَتَخَطّى
//     نَفسَه بِلا `WASAYEL_LIVE_PROOF=1` و`ConnectionStrings__Postgres`) — لِأَنّ
//     `AdvanceAsync` تَفتَحُ جَلسَةَ Marten ولا تُنادى بِلا قاعِدَة. **ولَم يُشَغَّل
//     في الجَلسَةِ الَّتي كَتَبَته** (لا اعتِمادَ قاعِدَةٍ مُتاحاً لَها)، فَيُقالُ
//     ذلك ولا يُدَّعى: الحَكَمُ المَقيسُ هُنا هُوَ شَطرُ المَصدَر.

public class DealPaidStageCharacterizationTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 500,
            $"أَداةٌ عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    private static string DealsServiceSource() => Source(
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
        "Services", "Deals", "DealsService.cs");

    private static string EndpointsSource() => Source(
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
        "MarketplaceTemplateExtensions.cs");

    private static string SeederSource() => Source(
        "apps", "V1.App", "Seed", "TestDataSeeder.cs");

    /// <summary>جِسمُ <c>AdvanceAsync</c> وَحدَه — مِن تَوقيعِها إلى تَوقيعِ
    /// الدالَّةِ التالِيَة. يَفشَلُ إن لَم يُوجَد، فَلا يُقاسُ فَراغ.</summary>
    private static string AdvanceBody()
    {
        var text = DealsServiceSource();
        var start = text.IndexOf("AdvanceAsync(", StringComparison.Ordinal);
        var end = text.IndexOf("CancelAsync(", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "‏`AdvanceAsync` غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        return text.Substring(start, end - start);
    }

    /// <summary>كُتلَةُ <c>if (next == DealStage.Paid …) { … }</c> بِأَقواسِها —
    /// مُطابَقَةُ الأَقواسِ لا تَخمينُ طول.</summary>
    private static string PaidBlock()
    {
        var body = AdvanceBody();
        var at = body.IndexOf("next == DealStage.Paid", StringComparison.Ordinal);
        Assert.True(at > 0, "لا شَرطَ `next == DealStage.Paid` في `AdvanceAsync` — الأَداةُ عَمياء.");
        return BlockFrom(body, at);
    }

    /// <summary>مِن أَوَّلِ <c>{</c> بَعدَ المَوضِعِ إلى قَوسِه المُقابِل.</summary>
    private static string BlockFrom(string text, int at)
    {
        var open = text.IndexOf('{', at);
        Assert.True(open > at, "لا كُتلَةَ بَعدَ الشَرط.");
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0)
                return text.Substring(open, i - open + 1);
        }
        Assert.Fail("كُتلَةٌ بِلا قَوسٍ مُقابِل.");
        return "";
    }

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    // ═══ ١) العَمولَة — أُسقِطَت في الخُطوَة 3، والتَوصيفُ انقَلَبَ بِقَرارٍ مَرئِيّ ═══
    //
    // **ما كانَ هُنا قَبلَ ‏2026-10-11** (كوميتُ التَوصيف ‏522f706): أَربَعَةُ فُحوصٍ
    // تُثَبِّتُ `PlatformCommissionRate == 0.025m`، وكِتابَةَ `CommissionSar` مَرَّةً
    // داخِلَ كُتلَةِ Paid بِحارِسَيها، وسَطرَ «عمولة» بِاسمِ «المَنصَّة»، وبَذرَةً
    // تَكتُبُ ‏٥٪. **وهذِه هي بِعَينِها الأَسطُرُ الَّتي يُسقِطُها ADR-032 §٢‑ب**،
    // فَهي الوَحيدَةُ الَّتي مُسَّت — وانقَلَبَت إلى نَقيضِها لِتَحرُسَ أَلّا تَعودَ
    // العَمولَةُ صامِتَةً. وشَطرُ القَبضِ أَدناه لَم يُمَسَّ حَرفاً (القاعِدَة ٣).

    /// <summary><b>لا ثابِتَ نِسبَةٍ ولا حَقلَ عَمولَةٍ في النَوعَين</b> —
    /// بِالانعِكاسِ لا بِالدَعوى.</summary>
    [Fact]
    public void No_commission_rate_constant_and_no_commission_field_remain_on_the_types()
    {
        Assert.Null(typeof(DealsService).GetField("PlatformCommissionRate"));
        Assert.Null(typeof(Deal).GetProperty("CommissionSar"));
        Assert.DoesNotContain(typeof(Deal).GetProperties(),
            p => p.Name.Contains("Commission", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary><b>ولا كاتِبَ ولا قارِئَ لِعَمولَةٍ في وَقتِ التَشغيل</b>: كُلُّ
    /// `.cs`/`.razor` تَحتَ <c>libs/</c> و<c>apps/</c> خالٍ مِن
    /// <c>CommissionSar</c> خارِجَ التَعليقات — والعَدّادُ يُطبَعُ
    /// فَلا يُقالُ «صِفر» بِلا عَدٍّ (القاعِدَة ١٠).</summary>
    [Fact]
    public void No_runtime_code_writes_or_reads_a_commission_any_more()
    {
        var scanned = 0;
        var hits = new List<string>();
        foreach (var root in new[] { "libs", "apps" })
        foreach (var f in Directory.EnumerateFiles(Path.Combine(RepoRoot, root), "*.*", SearchOption.AllDirectories))
        {
            if (!f.EndsWith(".cs", StringComparison.Ordinal) && !f.EndsWith(".razor", StringComparison.Ordinal)) continue;
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            scanned++;
            var code = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(f)));
            if (code.Contains("CommissionSar", StringComparison.Ordinal) ||
                code.Contains("PlatformCommissionRate", StringComparison.Ordinal))
                hits.Add(Path.GetRelativePath(RepoRoot, f));
        }

        Assert.True(scanned > 200, $"أَداةٌ عَمياء: فُحِصَ {scanned} مِلَفّاً.");
        Assert.True(hits.Count == 0, "عَمولَةٌ عادَت إلى الكود: " + string.Join("، ", hits));
    }

    /// <summary>كُتلَةُ <c>Paid</c> لا تُلحِقُ سَطرَ «عمولة» ولا تَكتُبُ
    /// بِاسمِ «المَنصَّة»؛ وحارِسُ <c>AmountSar &gt; 0</c> باقٍ عَلى
    /// شَرطِها كَما كان.</summary>
    [Fact]
    public void The_Paid_block_no_longer_announces_a_commission_and_keeps_its_amount_guard()
    {
        var block = WriteEndpointGuardTests.StripComments(PaidBlock());
        Assert.DoesNotContain("عمولة", block, StringComparison.Ordinal);
        Assert.DoesNotContain("\"المَنصَّة\"", block, StringComparison.Ordinal);

        var body = AdvanceBody();
        var at = body.IndexOf("next == DealStage.Paid", StringComparison.Ordinal);
        var condition = body.Substring(at, body.IndexOf('{', at) - at);
        Assert.Contains("deal.AmountSar > 0", condition, StringComparison.Ordinal);
    }

    /// <summary>والبَذرَةُ لا تَكتُبُ عَمولَةً — فَلا شاشَةَ تَجرِبَةٍ تَعرِضُ
    /// ما لا يَحسُبُه الكود (كانَت ‏٥٪ مُقابِلَ ‏٢٫٥٪).</summary>
    [Fact]
    public void The_seed_no_longer_writes_a_commission()
    {
        var seed = SeederSource();
        Assert.DoesNotContain("CommissionSar", seed, StringComparison.Ordinal);
        Assert.Contains("AmountSar = 100,", seed, StringComparison.Ordinal);
        Assert.Contains("AmountSar = 250,", seed, StringComparison.Ordinal);
    }

    /// <summary>والقامُوسُ لا يَعِدُ بِعَمولَةٍ في شُروطِ أَيِّ مَتجَر، ولا
    /// يَحمِلُ مِفتاحَ عَرضِها اليَتيم.</summary>
    [Fact]
    public void The_dictionary_no_longer_declares_a_commission_in_any_stores_terms()
    {
        var ar = Source("libs", "core", "ACommerce.Platform.I18n", "Locales", "ar.json");
        Assert.DoesNotContain("\"legal.terms.item_commission\"", ar, StringComparison.Ordinal);
        Assert.DoesNotContain("\"deals.detail.commission_label\"", ar, StringComparison.Ordinal);
        Assert.DoesNotContain("بِجَمع عَمولَة", ar, StringComparison.Ordinal);
        // ‏2026-10-11 (S5·٥): كانَ هُنا `Assert.Contains` لِبَندِ «الاشتِراكِ وَحدَه» الَّذي حَلَّ مَحَلَّ
        // العَمولَة — وحُذِفَ مِن شُروطِ المُشتَري لِأَنَّه نَموذَجُ إيرادِ المَنَصَّةِ لا شَرطٌ عَلَيه، ولا
        // مِفتاحَ في `platform.terms.*` يَتَكَلَّمُ في الإيرادِ لِيُنقَلَ إلَيه (`LegalCheckoutTextsCharacterizationTests`).
        Assert.DoesNotContain("\"legal.terms.item_subscription_only\"", ar, StringComparison.Ordinal);
    }

    // ═══ ٢) القَبضُ — ما يَبقى بَعدَ الخُطوَة 3 بِلا تَعديلِ حَرف ═══════════

    /// <summary><b><c>CaptureAsync</c> يُنادى مَرَّةً واحِدَةً في الخِدمَةِ
    /// كُلِّها</b>، داخِلَ كُتلَةِ <c>Paid</c>، وبَعدَ حارِسِ
    /// <c>Refs["payment_id"]</c> — فَلا قَبضَ بِلا إذنٍ مَحجوزٍ قَبلَه.
    /// و<c>AuthorizeAsync</c> لا تُنادى هُنا إطلاقاً: الحَجزُ يَقَعُ في
    /// <c>/checkout/submit</c> لا في الخِدمَة.</summary>
    [Fact]
    public void Capture_is_called_exactly_once_and_only_when_a_payment_id_exists()
    {
        var service = DealsServiceSource();
        Assert.Equal(1, Count(service, "CaptureAsync("));
        Assert.Equal(0, Count(service, "AuthorizeAsync("));

        var block = PaidBlock();
        var guard = block.IndexOf("deal.Refs.TryGetValue(\"payment_id\", out var pid)", StringComparison.Ordinal);
        var call  = block.IndexOf("_payments.CaptureAsync(pid, deal.AmountSar, ct)", StringComparison.Ordinal);
        Assert.True(guard > 0, "حارِسُ `payment_id` غَيرُ مَوجودٍ في كُتلَةِ Paid.");
        Assert.True(call > guard, "نِداءُ القَبضِ يَسبِقُ حارِسَه أَو يَقَعُ خارِجَ كُتلَتِه.");

        // والنِداءُ داخِلَ كُتلَةِ الحارِسِ نَفسِها، لا بَعدَها.
        var guarded = BlockFrom(block, guard);
        Assert.Contains("_payments.CaptureAsync(", guarded, StringComparison.Ordinal);
    }

    /// <summary>ونَتيجَةُ القَبضِ تُكتَبُ في <c>Refs["payment_status"]</c>
    /// وسَطرِ <c>captured</c>؛ وفَشَلُه <b>لا يَكسِرُ التَقَدُّم</b>:
    /// يُسَجَّلُ في <c>Refs["capture_error"]</c> وسَطرِ
    /// <c>capture_failed</c> وتَمضي المَرحَلَة.
    /// <para><b>وقُلِبَ مِنه بِاسمِه ما يَصِفُ التَرتيبَ وَحدَه</b> (‏ADR-036 الخُطوَة 2، ‏2026-10-12 — ولا يُعادُ حَرفٌ فَوقَه): كانَت
    /// النَتيجَةُ تُكتَبُ عَلى <c>deal</c> — نُسخَةِ الانتِقالِ قَبلَ حِفظِه — وكانَ «الحِفظُ بَعدَ الكُتلَة». وصارَ الانتِقالُ يُحفَظُ أَوَّلاً
    /// (المُطالَبَة)، والكُتلَةُ بَعدَه في <c>CaptureClaimedAsync</c>، ونَتيجَتُها عَلى الصَفقَةِ المَقروءَةِ ثانِيَةً <c>stored</c>. والمَعنى
    /// الَّذي كَتَبَ الصَفَّ باقٍ بِحَرفِه: النَتيجَةُ في <c>payment_status</c> وسَطرِ <c>captured</c>، والفَشَلُ في <c>capture_error</c>
    /// وسَطرِ <c>capture_failed</c>، والمَرحَلَةُ تُحفَظُ في الحالَين — قَبلَ القَبضِ لا بَعدَه. والسُلوكُ في <c>DealMoneyClaimFirstTests</c>.</para></summary>
    [Fact]
    public void A_capture_result_is_recorded_and_a_failure_does_not_block_the_stage()
    {
        var block = PaidBlock();
        Assert.Contains("stored.Refs[\"payment_status\"] = cr.Status.ToString();", block, StringComparison.Ordinal);
        Assert.Contains("\"captured\"", block, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", block, StringComparison.Ordinal);
        Assert.Contains("stored.Refs[\"capture_error\"] = ex.Message;", block, StringComparison.Ordinal);
        Assert.Contains("\"capture_failed\"", block, StringComparison.Ordinal);

        // والحِفظُ قَبلَ الكُتلَةِ لا بَعدَها — قُلِبَ بِاسمِه (‏ADR-036 الخُطوَة 2): كانَ «بَعدَ الكُتلَةِ لا داخِلَ `catch`». فَالانتِقالُ
        // يُحفَظُ في `MoveAsync` داخِلَ التِقاطِ التَعارُض، ثُمَّ يُنادى القَبضُ بَعدَ الالتِقاطِ لا داخِلَه — فَالمَرحَلَةُ تُحفَظُ في الحالَين،
        // ومَن رُدَّ حِفظُه لا يَقبِض.
        var body = AdvanceBody();
        var claim   = body.IndexOf("moved = await MoveAsync(s, deal, next.Value, actorId, actorName, note, ct);", StringComparison.Ordinal);
        var caught  = body.IndexOf("catch (JasperFx.ConcurrencyException)", Math.Max(claim, 0), StringComparison.Ordinal);
        var capture = body.IndexOf("await CaptureClaimedAsync(tenantSlug, moved,", StringComparison.Ordinal);
        Assert.True(claim > 0 && caught > claim && capture > caught,
            $"تَرتيبُ المُطالَبَة: الحِفظ={claim} الالتِقاط={caught} القَبض={capture}");
        Assert.Contains("await s.SaveChangesAsync(ct);",
            body.Substring(body.IndexOf("private async Task<Deal> MoveAsync(", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    /// <summary><b>الدَفعُ عِندَ الاستِلامِ لا يَبلُغُ القَبضَ أَبَداً</b> —
    /// بِطَرَفَيه: <c>CallsProvider("cash") == false</c> (نَقِيٌّ)، وفي
    /// <c>/checkout/submit</c> يُلحَقُ <c>payment_id</c> <b>داخِلَ</b>
    /// كُتلَةِ <c>CallsProvider(payMethod)</c> وَحدَها. فَصَفقَةُ نَقدٍ
    /// بِلا <c>payment_id</c>، وحارِسُ القَبضِ أَعلاه يَتَجاوَزُها.</summary>
    [Fact]
    public void Cash_on_delivery_never_acquires_a_payment_id_so_Paid_never_captures_for_it()
    {
        Assert.False(CheckoutPaymentPolicy.CallsProvider(CheckoutPaymentPolicy.Normalize("cash")));
        Assert.False(CheckoutPaymentPolicy.CallsProvider(CheckoutPaymentPolicy.Normalize(null)));

        var text = EndpointsSource();
        var start = text.IndexOf("/{slug}/checkout/submit", StringComparison.Ordinal);
        Assert.True(start > 0, "نُقطَةُ إتمامِ الشِراءِ غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        var body = text.Substring(start, text.IndexOf(".RequireStoreWritable();", start, StringComparison.Ordinal) - start);

        const string attach = "AttachRefAsync(slug, deal.Id, \"payment_id\",";
        Assert.Equal(1, Count(body, attach));

        var gate = body.IndexOf("if (Services.Deals.CheckoutPaymentPolicy.CallsProvider(payMethod))", StringComparison.Ordinal);
        Assert.True(gate > 0, "حارِسُ `CallsProvider` غَيرُ مَوجودٍ في النُقطَة.");
        Assert.Contains(attach, BlockFrom(body, gate), StringComparison.Ordinal);
    }
}

// ─── البُرهانُ الحَيّ — الجانِبُ الَّذي يَلزَمُه Postgres ───────────────────
//
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 و ConnectionStrings__Postgres،
// بِنَفسِ عَقدِ `LiveModelUsageMeteringProofTests` حَرفاً — ومِنه نُسِخَ تَركيبُ
// المَخزَن. يَمشي صَفقَتَين مِن `Offered` ثَلاثَ خُطُواتٍ ثُمَّ خُطوَةً بَعدَها:
// واحِدَةٌ بِـ`payment_id` (بِطاقَة) تَبلُغُ `Paid` وتُقبَض، وواحِدَةٌ بِلا (نَقد)
// — **تَتَخَطّى `Paid` إلى `Shipping` مُنذُ ‏ADR-033 §٦** (كانَت تَبلُغُها بِلا دَفع)
// وتَكتُبُ سَطرَ القَبضِ النَقديِّ عِندَ `Delivered` — ويَعُدُّ نِداءاتَ `CaptureAsync`
// عَلى مُزَوِّدٍ مُسَجِّلٍ لا مُحاكٍ.
//
// **ما مُسَّ هُنا في كوميتِ الإسقاط** (‏ADR-032 §٢‑ب): تَأكيداتُ
// `CommissionSar == 2.50m` الثَلاثُ وَحدَها — حُذِفَت مَعَ الحَقل. والقَبضُ
// وعَدُّه وسَطرُ `captured` كَما كُتِبَت أَوَّلَ مَرَّة.

[Collection(LiveProofCollection.Name)]
public class LiveDealPaidStageProofTests(ITestOutputHelper output)
{
    private static bool Enabled =>
        Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1";

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    private static string ResilientConnection =>
        ConnectionString!.TrimEnd(';') + ";Timeout=60;Command Timeout=120";

    private static DocumentStore BuildStore() => DocumentStore.For(o =>
    {
        o.Connection(ResilientConnection);
        o.DatabaseSchemaName = "platform";
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;
        o.Schema.For<ACommerce.Kit.Tenants.Tenant>().SingleTenanted().Identity(x => x.Id);
        o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
    });

    /// <summary>مُزَوِّدٌ يُسَجِّلُ ما نودِيَ بِه ويَرُدُّ نَجاحاً — لِيُعَدَّ
    /// النِداءُ لا لِيُحاكى الدَفع.</summary>
    private sealed class RecordingPaymentProvider : IPaymentProvider
    {
        public readonly List<string> Captured = new();
        public string ProviderName => "recording";

        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default)
            => Task.FromResult(new PaymentResult("auth_" + idempotencyKey, PaymentStatus.Authorized, req.AmountSar));

        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default)
        {
            Captured.Add(paymentId);
            return Task.FromResult(new PaymentResult(paymentId, PaymentStatus.Captured, amount ?? 0m));
        }

        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default)
            => Task.FromResult(new PaymentResult(paymentId, PaymentStatus.Refunded, amount));

        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default)
            => Task.FromResult(new SubscriptionResult("sub", true, DateTime.UtcNow.AddMonths(1)));

        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default)
            => Task.FromResult<Invoice?>(null);
    }

    [Fact]
    public async Task Entering_Paid_captures_once_and_only_with_a_payment_id()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        var evidence = new StringBuilder();
        void Log(string line) { evidence.AppendLine(line); output.WriteLine(line); }

        await using var store = BuildStore();
        var payments = new RecordingPaymentProvider();
        var deals = new DealsService(store, payments);

        // مُستَأجِرٌ عَشوائِيٌّ يَخُصُّ هذا التَشغيلَ وَحدَه — لا `ashare`
        // ولا مَتجَرَ لَقطَة.
        var slug = "zz-proof-" + Guid.NewGuid().ToString("N")[..8];
        var buyer = Guid.NewGuid();
        var seller = Guid.NewGuid();

        try
        {
            var card = await deals.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "بِطاقَة", 100m,
                new() { ["pay_method"] = CheckoutPayMethods.Card });
            await deals.AttachRefAsync(slug, card.Id, "payment_id", "auth_live");
            var cash = await deals.StartAsync(slug, "marketplace", buyer, "مُشتَرٍ", null, "نَقد", 100m,
                new() { ["pay_method"] = CheckoutPayMethods.CashOnDelivery });

            // **قُلِبَ فاعِلو المَشي** (‏ADR-033 §٦‑ط، ‏2026-10-11): كانَ المُشتَري يُخرِجُ `Offered` ويُدخِلُ
            // النَقدَ «شَحن»، لِأَنّ الخِدمَةَ كانَت تَسأَلُ فاعِلَ المَرحَلَةِ الحالِيَّة. وتَسأَلُ اليَومَ فاعِلَ
            // المَدخولَة: البائِعُ يَحجِز، والمُشتَري يَدفَع (بِطاقَة)، والبائِعُ يَشحَن (نَقد). والمَقيسُ — القَبضُ
            // مَرَّةً عِندَ دُخولِ `Paid` بِمُعَرِّفِ دَفعٍ وَحدَه — بِحَرفِه.
            foreach (var id in new[] { card.Id, cash.Id })
            {
                await deals.AssignCounterpartyAsync(slug, id, seller, "بائِع");
                Assert.True((await deals.AdvanceAsync(slug, id, seller, "بائِع")).Ok);     // Offered → Booked
                Assert.True((await deals.AdvanceAsync(slug, id, seller, "بائِع")).Ok);     // Booked → Confirmed
            }
            Assert.True((await deals.AdvanceAsync(slug, card.Id, buyer, "مُشتَرٍ")).Ok);    // Confirmed → Paid (بِطاقَة: المُشتَري يَدفَع)
            Assert.False((await deals.AdvanceAsync(slug, cash.Id, buyer, "مُشتَرٍ")).Ok);   // Confirmed → Shipping لَيسَ لِلمُشتَري
            Assert.True((await deals.AdvanceAsync(slug, cash.Id, seller, "بائِع")).Ok);     // Confirmed → Shipping (نَقد: البائِعُ يَشحَن)

            var cardPaid = (await deals.LoadAsync(slug, card.Id))!;
            var cashPaid = (await deals.LoadAsync(slug, cash.Id))!;
            Log($"بِطاقَة: Stage={cardPaid.Stage} status={cardPaid.Refs.GetValueOrDefault("payment_status")}");
            Log($"نَقد:    Stage={cashPaid.Stage} captures={payments.Captured.Count}");

            Assert.Equal(DealStage.Paid, cardPaid.Stage);
            // **قُلِبَ بِقَرارٍ مَرئِيّ** (‏ADR-033 §٦، ‏2026-10-11): كانَ `Paid` — صَفقَةُ
            // النَقدِ كانَت تَزورُ «دَفع» بِلا دَفع. وصارَت تَتَخَطّاها إلى `Shipping`؛
            // والقَبضُ لِلبِطاقَةِ وَحدَها كَما كان — وهُوَ ما يَحرُسُه هذا البُرهان.
            Assert.Equal(DealStage.Shipping, cashPaid.Stage);
            Assert.DoesNotContain(cashPaid.Timeline, e => e.StageAfter == DealStage.Paid);
            Assert.Equal(new[] { "auth_live" }, payments.Captured);
            Assert.Equal(nameof(PaymentStatus.Captured), cardPaid.Refs["payment_status"]);
            Assert.False(cashPaid.Refs.ContainsKey("payment_status"));

            // خُطوَةٌ بَعدَ Paid — لا كِتابَةَ ثانِيَةً ولا قَبضَ ثانِياً. (والشاحِنُ البائِعُ — قُلِبَ، §٦‑ط.)
            Assert.False((await deals.AdvanceAsync(slug, card.Id, buyer, "مُشتَرٍ")).Ok);  // Paid → Shipping لَيسَ لِلمُشتَري
            Assert.True((await deals.AdvanceAsync(slug, card.Id, seller, "بائِع")).Ok);    // Paid → Shipping
            var after = (await deals.LoadAsync(slug, card.Id))!;
            Assert.Single(payments.Captured);
            Assert.Equal(1, after.Timeline.Count(e => e.Action == "captured"));
            Log($"بَعدَ Paid: Stage={after.Stage} captures={payments.Captured.Count} — قُبِضَت مَرَّةً لا مَرَّتَين.");

            // والنَقدُ: البائِعُ يُسَلِّمُ (Shipping → Delivered) فَيُكتَبُ سَطرُ القَبضِ
            // النَقديِّ بِاسمِه — ولا نِداءَ لِمُزَوِّدِ الدَفعِ قَطّ.
            Assert.True((await deals.AdvanceAsync(slug, cash.Id, seller, "بائِع")).Ok);     // Shipping → Delivered
            var delivered = (await deals.LoadAsync(slug, cash.Id))!;
            Assert.Equal(DealStage.Delivered, delivered.Stage);
            Assert.Equal(1, delivered.Timeline.Count(e => e.Action == DealEvent.CashOnDelivery));
            Assert.Single(payments.Captured);
            Log($"نَقدٌ عِندَ التَسليم: Stage={delivered.Stage} سَطرُ القَبض={delivered.Timeline.Count(e => e.Action == DealEvent.CashOnDelivery)} captures={payments.Captured.Count}");
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            foreach (var d in await s.Query<Deal>().ToListAsync()) s.Delete(d);
            await s.SaveChangesAsync();
        }
    }
}
