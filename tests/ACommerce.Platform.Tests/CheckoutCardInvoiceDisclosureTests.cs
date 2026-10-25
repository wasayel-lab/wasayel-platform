using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Cart;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Components.Pages;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إفصاحُ السَلَّةِ يَقولُ إنّ كُلَّ بَندٍ مُستَقِلٌّ ويُدفَعُ عِندَ تَسليمِه — ولَو دُفِعَ بِالبِطاقَةِ في فاتورَةٍ واحِدَةٍ لا يُلغى بَندُها وَحدَه ═══
//
// تَدقيقُ الإطلاق ‏lens-money-integrity-8 (صُغرى، ‏2026-10-24 عِندَ `dace7cd`؛ أُعيدَ قِياسُه عِندَ `2ad660c`): `checkout.payment.per_item_note`
// («كُلُّ مُنتَجٍ … طَلَبٌ مُستَقِلٌّ … والدَفعُ عِندَ الاستِلامِ عِندَ تَسليمِ كُلٍّ مِنها») يُرسَمُ في الخُطوَةِ ٢ بِلا شَرط. وطَريقُ البِطاقَةِ عَبرَ بَوّابَةِ
// المَتجَر (`Facts.ThroughGateway`) يَفتَحُ **فاتورَةً واحِدَةً** لِصَفَقاتِ السَلَّةِ كُلِّها (`GatewayPayments.OpenInvoiceAsync`: مَبلَغُها مَجموعُها
// و`InvoiceDealsRef` أَخَواتُها)، والإلغاءُ يَرُدُّ بَندَ فاتورَةٍ مَفتوحَةٍ غَيرِ مَدفوعَةٍ بِـ`deal_invoice_shared` (`DealsService.CancelCoreAsync`).
//
// **والصَفحَةُ مَرسومَةٌ لا مَقروءَة** (نَمَطُ `StudioConsentReturnUrlTests`): `HtmlRenderer` بِصِنفٍ مُشتَقٍّ يُبَدِّلُ `OnInitializedAsync` وَحدَه فَيَضَعُ
// السَلَّةَ والحَقائِقَ بَدَلَ سُؤالِ القاعِدَة. **يُثَبَّتُ قَبلَ التَبديلِ** (القاعِدَة ٣): صَفُّ «بِلا بَوّابَة» يَطبَعُ بَصمَةَ الصَفحَةِ ويَبقى بِلا
// تَعديلِ حَرف (الجُملَةُ الجَديدَةُ لا تُرسَمُ إلّا تَحتَ البَوّابَة)، و`Today_…` يَنقَلِبُ بِاسمِه في كوميتِ الجُملَة.
public class CheckoutCardInvoiceDisclosureTests(ITestOutputHelper output)
{
    private sealed class NoProvider : IPaymentProvider
    {
        public string ProviderName => "none";
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>الصَفحَةُ نَفسُها بِلا سُؤالِ القاعِدَة: السَلَّةُ والحَقائِقُ في حَقلَيها الخاصَّين، والخُطوَةُ ٢ بِحُقولِ العُنوان.</summary>
    private sealed class CheckoutProbe : CheckoutPage
    {
        [Parameter] public CheckoutCardFacts? ProbeFacts { get; set; }
        [Parameter] public int Items { get; set; } = 2;

        protected override Task OnInitializedAsync()
        {
            var cart = new Cart();
            for (var i = 0; i < Items; i++)
                cart.Items.Add(new CartItem { ListingId = new Guid($"00000000-0000-0000-0000-00000000000{i + 1}"), Title = $"سِلعَة {i + 1}", UnitPriceSar = 50m, Quantity = 1 });
            Set("cart", cart);
            Set("Facts", ProbeFacts!);
            Step = 2; Name = "SAMPLE"; Phone = "0512345678"; Addr = "SAMPLE-ADDRESS";
            return Task.CompletedTask;
        }

        private void Set(string field, object value)
        {
            var f = typeof(CheckoutPage).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(f is not null, $"لا حَقلَ `{field}` في CheckoutPage — الأَداةُ عَمياء.");
            f!.SetValue(this, value);
        }
    }

    /// <summary>بِطاقَةٌ تَمُرُّ بِبَوّابَةِ المَتجَر: مِفتاحٌ مَختومٌ وخِزانَةٌ تَفُكُّ وعَميلٌ مُسَجَّل.</summary>
    internal static readonly CheckoutCardFacts ThroughGateway = new(StoreCollects: true, SealedKeyBound: true, VaultCanUnseal: true,
        GatewayClientRegistered: true, InstanceCanCharge: false);

    /// <summary>نَقدٌ وَحدَه: لا مِفتاحَ ولا قُدرَةَ لِمُزَوِّدِ النُسخَة — حالُ كُلِّ مَتجَرٍ اليَوم بِلا KEK.</summary>
    internal static readonly CheckoutCardFacts CashOnly = new(StoreCollects: false, SealedKeyBound: false, VaultCanUnseal: false,
        GatewayClientRegistered: false, InstanceCanCharge: false);

    internal static async Task<string> RenderPayStepAsync(CheckoutCardFacts facts, int items = 2)
    {
        var http = new DefaultHttpContext();
        http.Request.Path = "/theme-demo/checkout";
        http.Request.Headers.Cookie = $"{AuthSession.CookieName("theme-demo")}={AuthHandlers.MakeToken(Guid.NewGuid(), "theme-demo")}";
        var auth = new AuthSession();
        auth.Load(http, "theme-demo");
        Assert.True(auth.IsAuthenticated, "الجَلسَةُ لَم تُحَمَّل — الأَداةُ عَمياء.");

        await using var services = new ServiceCollection()
            .AddSingleton(new L(new HttpContextAccessor()))
            .AddSingleton(auth)
            .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = http })
            .AddSingleton<IPaymentProvider>(new NoProvider())
            // لا تُسأَلُ أَبَداً: المِسبارُ يُبَدِّلُ السُؤالَ الوَحيدَ الَّذي يَبلُغُها.
            .AddSingleton((ACommerce.Templates.Customer.Marketplace.Services.Queries.AccountQueries)RuntimeHelpers.GetUninitializedObject(typeof(ACommerce.Templates.Customer.Marketplace.Services.Queries.AccountQueries)))
            .AddSingleton((ACommerce.Templates.Customer.Marketplace.Services.Queries.TenantDirectory)RuntimeHelpers.GetUninitializedObject(typeof(ACommerce.Templates.Customer.Marketplace.Services.Queries.TenantDirectory)))
            .AddSingleton((GatewayPayments)RuntimeHelpers.GetUninitializedObject(typeof(GatewayPayments)))
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<CheckoutProbe>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(CheckoutProbe.Slug)] = "theme-demo",
                [nameof(CheckoutProbe.ProbeFacts)] = facts,
                [nameof(CheckoutProbe.Items)] = items,
            }))).ToHtmlString());
    }

    private void Print(string label, string html) =>
        output.WriteLine($"{label}: {html.Length} حَرفاً · sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html)))[..16]}");

    internal static string PerItemNote => LocaleCatalog.Find(LocaleCatalog.Arabic, "checkout.payment.per_item_note")!;

    /// <summary>بِلا بَوّابَة: البِطاقَةُ لا تُعرَض، والإفصاحُ مَرَّةً — يَبقى بِلا تَعديلِ حَرفٍ بَعدَ الجُملَة (وبَصمَتُه مَطبوعَةٌ لِلمُقارَنَة).</summary>
    [Fact]
    public async Task Without_the_gateway_the_pay_step_offers_cash_and_discloses_one_order_per_item_once()
    {
        var html = await RenderPayStepAsync(CashOnly);
        Print("بِلا بَوّابَة", html);
        Assert.Contains("name=\"pay\" value=\"cash\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"pay\" value=\"card\"", html, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(html, Regex.Escape(PerItemNote)));
    }

    /// <summary>الحَقيقَةُ الَّتي تَعنيها الجُملَة: طَريقُ البَوّابَةِ فاتورَةٌ واحِدَةٌ لِصَفَقاتِ السَلَّةِ كُلِّها، وإلغاءُ بَندِها وَحدَه مَرفوضٌ ما دامَت مَفتوحَة.</summary>
    [Fact]
    public void The_gateway_route_opens_one_invoice_for_every_deal_of_the_cart_and_refuses_a_single_item_cancel_while_unpaid()
    {
        var gateway = UpgradePromptPriceTests.Code("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Deals", "GatewayPayments.cs");
        Assert.Contains("var amount = MoyasarClient.ToSmallestUnit(deals.Sum(d => d.AmountSar));", gateway, StringComparison.Ordinal);
        Assert.Contains("await _deals.AttachRefAsync(tenantSlug, d.Id, InvoiceDealsRef, siblings, ct);", gateway, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(LocaleCatalog.Find(LocaleCatalog.Arabic, "deals.detail.err_deal_invoice_shared")));
        Assert.True(ThroughGateway.ThroughGateway && ThroughGateway.CardOffered, "حَقائِقُ «عَبرَ البَوّابَة» لا تَعرِضُ البِطاقَة — الأَداةُ عَمياء.");
        Assert.False(CashOnly.CardOffered);
    }

    internal const string CardNoteKey = "checkout.payment.per_item_card_note";

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-25، كوميتُ الجُملَة)</b> — كانَ <c>Today_the_card_through_the_store_gateway_carries_only_the_cash_per_item_note</c>
    /// (‏<c>7f5ce5c</c>، 4621 حَرفاً). <b>والآن</b> تَحتَ بِطاقَةِ البَوّابَةِ جُملَةُ <c>checkout.payment.per_item_card_note</c> بَعدَ الإفصاحِ النَقدِيِّ مُباشَرَةً،
    /// وشَرطُها <c>CardOffered &amp;&amp; Facts.ThroughGateway</c> بِحَرفِه — والفَرقُ مَحصورٌ فيها: الصَفحَةُ بِدونِ عُنصُرِها هي صَفحَةُ «اليَوم»
    /// (يُقارَنُ بِبَصمَتِها، 4621 حَرفاً sha256=445BCC8DE5E35237). وصَفُّ «بِلا بَوّابَة» أَعلاه بِلا تَعديلِ حَرفٍ وبَصمَتُه كَما هي.</summary>
    [Fact]
    public async Task The_card_through_the_store_gateway_says_one_invoice_pays_every_order_and_none_is_cancelled_alone_before_payment()
    {
        var html = await RenderPayStepAsync(ThroughGateway);
        Print("عَبرَ البَوّابَة", html);
        var note = LocaleCatalog.Find(LocaleCatalog.Arabic, CardNoteKey)!;
        var element = $"<p class=\"ac-muted\" style=\"margin:4px 0 0;font-size:13px;\">{note}</p>";
        Assert.Single(Regex.Matches(html, Regex.Escape(element)));
        Assert.True(html.IndexOf(PerItemNote, StringComparison.Ordinal) < html.IndexOf(element, StringComparison.Ordinal), "الجُملَةُ قَبلَ الإفصاحِ النَقدِيّ.");

        var before = html.Replace(element, "", StringComparison.Ordinal);
        Assert.Equal(4621, before.Length);
        Assert.Equal("445BCC8DE5E35237", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(before)))[..16]);

        var bare = AccountDeletionRetentionTests.Bare(note);
        foreach (var said in new[] { "بالبطاقة", "فاتورة واحدة", "بوابة المتجر", "لا يلغى طلب منها وحده قبل دفعها" })
            Assert.True(bare.Contains(said, StringComparison.Ordinal), $"الجُملَةُ لا تَقولُ «{said}»: {note}");
        Assert.DoesNotMatch("[0-9٠-٩]", note);

        var page = UpgradePromptPriceTests.Code("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "CheckoutPage.razor");
        Assert.Equal("CardOffered && Facts.ThroughGateway",
            UpgradePromptPriceTests.EnclosingIfConditions(page, page.IndexOf(CardNoteKey, StringComparison.Ordinal)).Last());
        Assert.Single(Regex.Matches(page, Regex.Escape(CardNoteKey)));
    }
}
