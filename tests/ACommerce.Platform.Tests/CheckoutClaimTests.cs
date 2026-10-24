using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إرسالانِ مُتَداخِلانِ لِسَلَّةٍ واحِدَة — أَيَلِدانِ مَجموعَتَينِ مِن الطَلَبات؟ (تَدقيقُ الإطلاق ‏2026-10-24، lens-money-integrity-1) ═══
//
// **المَقيسُ قَبلَ اللَمس** (عِندَ `dace7cd`): `POST /{slug}/checkout/submit` يَقرَأُ السَلَّةَ أَوَّلاً، ويُودِعُ كُلَّ صَفقَةٍ فَوراً (`StartAsync` بِمُعَرِّفٍ جَديدٍ
// في كُلِّ نِداء)، ولا يَحذِفُ السَلَّةَ إلّا في آخِرِه — و`UseOptimisticConcurrency` عَلى `Deal` وَحدَها. فَإرسالانِ مُتَداخِلانِ يَقرَآنِ السَلَّةَ نَفسَها
// ويَلِدانِ مَجموعَتَين (مِجَسُّ الشَكّاك: «trials with a duplicate order: 5/5 · deal notifications +10»، وبِفاصِلِ ‏150 ms ‏0 مِن 3)؛ وعَلى البِطاقَةِ
// فاتورَتَين. وتَعليقٌ في الكودِ يَقولُ «idempotency-key بِـ dealId يَمنَع تَكرار الـ authorize لَو أَعادَ المُستَخدِم submit بِنَفس النَّموذَج» —
// والمُعَرِّفُ جَديدٌ في كُلِّ إرسال، فَالمِفتاحُ لا يَمنَعُ شَيئاً.
//
// **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صَفّا «اليَوم» أَدناه يُودَعانِ وَحدَهُما، ويُقلَبانِ بِاسمَيهِما في كوميتِ المُطالَبَة.

public class CheckoutClaimTests(ITestOutputHelper output)
{
    private const string Slug = "zz-checkout-claim";
    private static readonly Guid Buyer = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("الوِلادَةُ لا تَمَسُّ المال — نودِيَ مُزَوِّدُ الدَفع.");
        public string ProviderName => "forbidden";
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default) => throw Touched();
        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Touched();
        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Touched();
    }

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

    /// <summary>جِسمُ <c>/checkout/submit</c> بِتَعليقاتِه (التَعليقُ الكاذِبُ جُزءٌ مِمّا يُثَبَّت).</summary>
    private static string Submit()
        => Segment(Source(Template, "MarketplaceTemplateExtensions.cs"), "app.MapPost(\"/{slug}/checkout/submit\"", ".RequireStoreWritable();");

    /// <summary>
    /// <para><b>«اليَوم»</b>: بَندُ السَلَّةِ نَفسُه — بِالمُدخَلاتِ الَّتي يُمَرِّرُها البابُ لِإرسالَين — يَلِدُ صَفقَتَينِ بِمُعَرِّفَينِ مُختَلِفَين، فَمِفتاحُ
    /// الحَجزِ <c>deal_{id}</c> مُختَلِف.</para>
    /// </summary>
    [Fact]
    public void Today_two_submits_of_one_cart_line_birth_two_deals_with_fresh_ids()
    {
        var world = new DocWorld();
        var deals = new DealsService(world.Store, new ForbiddenPaymentProvider());
        var listing = Guid.NewGuid();
        Deal Born() => Now(deals.StartAsync(Slug, "marketplace", Buyer, "مُشتَرٍ", listing, "سِلعَة", 100m));

        var first = Born();
        var second = Born();

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, world.Wrote<Deal>());
        Assert.NotEqual($"deal_{first.Id}", $"deal_{second.Id}");
        output.WriteLine($"بَندٌ واحِدٌ وإرسالان: صَفَقاتٌ مَكتوبَة={world.Wrote<Deal>()} · مِفتاحا الحَجزِ مُختَلِفان");
    }

    /// <summary>
    /// <para><b>«اليَوم» في المَصدَر</b>: السَلَّةُ تُقرَأُ أَوَّلاً، و<c>StartAsync</c> بِلا مُعَرِّفٍ داخِلَ الحَلقَة، وحَذفُ السَلَّةِ بَعدَ الحَلقَة —
    /// وتَعليقُ «يَمنَع تَكرار الـ authorize» قائِم.</para>
    /// </summary>
    [Fact]
    public void Today_the_cart_is_read_first_each_deal_is_born_with_a_fresh_id_and_the_cart_is_deleted_last()
    {
        var submit = Submit();
        var read = submit.IndexOf("var cart = await s.LoadAsync<ACommerce.Kit.Cart.Cart>(userId);", StringComparison.Ordinal);
        var born = submit.IndexOf("var deal = await deals.StartAsync(slug, pattern,", StringComparison.Ordinal);
        var deleted = submit.IndexOf("s.Delete<ACommerce.Kit.Cart.Cart>(userId);", StringComparison.Ordinal);
        Assert.True(read > 0 && born > read && deleted > born, $"read={read} born={born} deleted={deleted}");
        Assert.DoesNotContain("id: ", Segment(submit, "var deal = await deals.StartAsync(slug, pattern,", "if (firstDealId == Guid.Empty)"), StringComparison.Ordinal);
        Assert.Contains("// idempotency-key بِـ dealId يَمنَع تَكرار الـ authorize", submit, StringComparison.Ordinal);
        Assert.Contains("Id = Guid.NewGuid(),", Segment(Source(Template, "Services", "Deals", "DealsService.cs"),
            "public async Task<Deal> StartAsync(", "public async Task<Deal?> AssignCounterpartyAsync("), StringComparison.Ordinal);
        output.WriteLine($"السَلَّةُ تُقرَأُ عِندَ {read}، والوِلادَةُ عِندَ {born}، والحَذفُ عِندَ {deleted}");
    }
}
