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

    // ═══ ١) العَمولَة — ما سَيُسقَطُ في الخُطوَة 3، مُثَبَّتاً أَوَّلاً ═════════

    /// <summary><b>النِسبَةُ ثابِتٌ صَلبٌ ‏2.5%</b> — بِلا سَنَدٍ في
    /// <c>docs/</c> (‏ADR-029 §٧·د)، ويُقرَأُ هُنا مِن الكودِ لا يُكتَبُ
    /// بِاليَد، فَهذا السَطرُ هُوَ ما يَكسِرُ البِناءَ يَومَ يُحذَفُ
    /// الثابِت — <b>وذلكَ مَقصود</b>: التَوصيفُ يُمَسُّ يَومَها بِقَرارٍ
    /// مَرئِيٍّ في كوميتٍ يَقولُ لِماذا.</summary>
    [Fact]
    public void The_commission_rate_today_is_two_and_a_half_percent_hard_coded()
        => Assert.Equal(0.025m, DealsService.PlatformCommissionRate);

    /// <summary><b>العَمولَةُ تُكتَبُ مَرَّةً واحِدَةً في وَقتِ التَشغيل</b> —
    /// مَوضِعٌ واحِدٌ في كُلِّ <c>libs/</c>، وهُوَ داخِلَ كُتلَةِ
    /// <c>Paid</c> وَحدَها، مَحروساً بِـ<c>CommissionSar is null</c>
    /// (مَرَّةً لا مَرَّتَين) و<c>AmountSar &gt; 0</c>، ومُدَوَّراً إلى
    /// هَلَلَتَين.</summary>
    [Fact]
    public void The_commission_is_written_exactly_once_and_only_on_entering_Paid()
    {
        var service = DealsServiceSource();
        Assert.Equal(1, Count(service, "CommissionSar ="));

        var block = PaidBlock();
        Assert.Contains("deal.CommissionSar = Math.Round(deal.AmountSar * PlatformCommissionRate, 2);",
            block, StringComparison.Ordinal);

        // الحُرّاسُ في الشَرطِ نَفسِه — لا في سَطرٍ لاحِق.
        var body = AdvanceBody();
        var at = body.IndexOf("next == DealStage.Paid", StringComparison.Ordinal);
        var condition = body.Substring(at, body.IndexOf('{', at) - at);
        Assert.Contains("deal.CommissionSar is null", condition, StringComparison.Ordinal);
        Assert.Contains("deal.AmountSar > 0", condition, StringComparison.Ordinal);

        // ولا كاتِبَ آخَرَ في وَقتِ التَشغيل: كُلُّ `.cs`/`.razor` تَحتَ `libs/`.
        var writers = Directory.EnumerateFiles(Path.Combine(RepoRoot, "libs"), "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("CommissionSar =", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(RepoRoot, f))
            .ToArray();
        Assert.Equal(
            new[] { Path.Combine("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Deals", "DealsService.cs") },
            writers);
    }

    /// <summary>وسَطرُ الخَطِّ الزَمَنيِّ «عمولة ‏2.5% = …» يُلحَقُ مَعَها
    /// بِاسمِ «المَنصَّة» فاعِلاً.</summary>
    [Fact]
    public void A_timeline_note_announces_the_commission_in_the_platforms_name()
    {
        var block = PaidBlock();
        Assert.Contains("$\"عمولة {PlatformCommissionRate:P1} = {deal.CommissionSar} ر.س\"", block, StringComparison.Ordinal);
        Assert.Contains("\"المَنصَّة\"", block, StringComparison.Ordinal);
    }

    /// <summary><b>والبَذرَةُ تُناقِضُ الكود</b> (‏ADR-029 §٧·د، مَقيسٌ):
    /// <c>TestDataSeeder</c> تَكتُبُ ‏5 مِن ‏100 و‏12.5 مِن ‏250 — أَي
    /// <b>‏٥٪</b> لا ‏٢٫٥٪. يُثَبَّتُ التَناقُضُ كَما هُوَ، لِأَنّ
    /// الخُطوَةَ 3 تَحذِفُ الطَرَفَينِ مَعاً.</summary>
    [Fact]
    public void The_seed_writes_a_five_percent_commission_that_contradicts_the_code()
    {
        var seed = SeederSource();
        Assert.Contains("AmountSar = 100, CommissionSar = 5,", seed, StringComparison.Ordinal);
        Assert.Contains("AmountSar = 250, CommissionSar = 12.5m,", seed, StringComparison.Ordinal);
        Assert.NotEqual(5m, Math.Round(100m * DealsService.PlatformCommissionRate, 2));
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
    /// <c>capture_failed</c> وتَمضي المَرحَلَة.</summary>
    [Fact]
    public void A_capture_result_is_recorded_and_a_failure_does_not_block_the_stage()
    {
        var block = PaidBlock();
        Assert.Contains("deal.Refs[\"payment_status\"] = cr.Status.ToString();", block, StringComparison.Ordinal);
        Assert.Contains("\"captured\"", block, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", block, StringComparison.Ordinal);
        Assert.Contains("deal.Refs[\"capture_error\"] = ex.Message;", block, StringComparison.Ordinal);
        Assert.Contains("\"capture_failed\"", block, StringComparison.Ordinal);

        // والحِفظُ بَعدَ الكُتلَةِ لا داخِلَ `catch` — فَالمَرحَلَةُ تُحفَظُ في الحالَين.
        var body = AdvanceBody();
        Assert.True(body.LastIndexOf("SaveChangesAsync(ct)", StringComparison.Ordinal)
                  > body.IndexOf("capture_failed", StringComparison.Ordinal));
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
// المَخزَن. يَمشي صَفقَتَين مِن `Offered` إلى `Paid` ثُمَّ خُطوَةً بَعدَها:
// واحِدَةٌ بِـ`payment_id` (بِطاقَة) وواحِدَةٌ بِلا (نَقد) — ويَعُدُّ نِداءاتَ
// `CaptureAsync` عَلى مُزَوِّدٍ مُسَجِّلٍ لا مُحاكٍ.

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
    public async Task Entering_Paid_writes_the_commission_once_and_captures_once_only_with_a_payment_id()
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

            foreach (var id in new[] { card.Id, cash.Id })
            {
                await deals.AssignCounterpartyAsync(slug, id, seller, "بائِع");
                Assert.True((await deals.AdvanceAsync(slug, id, buyer,  "مُشتَرٍ")).Ok);   // Offered → Booked
                Assert.True((await deals.AdvanceAsync(slug, id, seller, "بائِع")).Ok);     // Booked → Confirmed
                Assert.True((await deals.AdvanceAsync(slug, id, buyer,  "مُشتَرٍ")).Ok);   // Confirmed → Paid
            }

            var cardPaid = (await deals.LoadAsync(slug, card.Id))!;
            var cashPaid = (await deals.LoadAsync(slug, cash.Id))!;
            Log($"بِطاقَة: Stage={cardPaid.Stage} Commission={cardPaid.CommissionSar} status={cardPaid.Refs.GetValueOrDefault("payment_status")}");
            Log($"نَقد:    Stage={cashPaid.Stage} Commission={cashPaid.CommissionSar} captures={payments.Captured.Count}");

            Assert.Equal(DealStage.Paid, cardPaid.Stage);
            Assert.Equal(2.50m, cardPaid.CommissionSar);
            Assert.Equal(2.50m, cashPaid.CommissionSar);
            Assert.Equal(new[] { "auth_live" }, payments.Captured);
            Assert.Equal(nameof(PaymentStatus.Captured), cardPaid.Refs["payment_status"]);
            Assert.False(cashPaid.Refs.ContainsKey("payment_status"));

            // خُطوَةٌ بَعدَ Paid — لا كِتابَةَ ثانِيَةً ولا قَبضَ ثانِياً.
            Assert.True((await deals.AdvanceAsync(slug, card.Id, buyer, "مُشتَرٍ")).Ok);   // Paid → Shipping
            var after = (await deals.LoadAsync(slug, card.Id))!;
            Assert.Equal(2.50m, after.CommissionSar);
            Assert.Single(payments.Captured);
            Assert.Equal(1, after.Timeline.Count(e => e.Action == "captured"));
            Log($"بَعدَ Paid: Stage={after.Stage} captures={payments.Captured.Count} — كُتِبَت مَرَّةً وقُبِضَت مَرَّة.");
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            foreach (var d in await s.Query<Deal>().ToListAsync()) s.Delete(d);
            await s.SaveChangesAsync();
        }
    }
}
