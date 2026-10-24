using ACommerce.Kit.Listings;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إعلانٌ حُذِفَ أَو أَخفاه المُشرِفُ بَعدَ وَضعِه في السَلَّة — أَيُشتَرى؟ وبِأَيِّ سِعر؟ (تَدقيقُ الإطلاق ‏2026-10-24، lens-money-integrity-3) ═══
//
// **المَقيسُ قَبلَ اللَمس** (عِندَ `dace7cd`، ومِن الرَأسِ بَعدَ كوميتِ المُطالَبَة `36b4b5a`): الحَذفُ ناعِم — `Listing.Apply(ListingDeleted)` يَضبِطُ
// `IsDeleted` والوَثيقَةُ باقِيَة، وإخفاءُ المُشرِفِ `IsHiddenByModerator` — فَشَرطُ `listing is null` في حَلقَةِ `/checkout/submit` وفي `cart/add` لا يَصدُقُ
// أَبَداً لِإعلانٍ كانَ. فَيُشتَرى المَحذوفُ والمَخفِيّ، والمَبلَغُ سِعرُ السَلَّةِ (`item.UnitPriceSar`) لا سِعرُ الإعلانِ الآن (مِجَسُّ الشَكّاك: حَذفٌ ثُمَّ
// إرسال ⇒ صَفقَةٌ بِـ200؛ سِعرٌ 100 ← 250 بَعدَ الإضافَة ⇒ صَفقَةٌ بِـ100). و‏ADR-032 §٨‑ح‑١ يَقولُ «النقطةُ تتجاوزُ بندًا حُذِفَ إعلانُه بعدَ وضعِه في السلّة».
//
// **التَوصيفُ قَبلَ التَبديل** (القاعِدَة ٣): صَفّا «اليَوم» أَدناه يُودَعانِ وَحدَهُما، ويُقلَبانِ بِاسمَيهِما في كوميتِ المُراجَعَة.
//
// **وقُلِبا بِاسمَيهِما** (‏2026-10-24؛ تَوصيفُهُما `bdd6611`؛ ‏ADR-032 §٨‑ح‑١ مُلحَقاً): `CheckoutCart.Buyable` (مَوجودٌ ولا مَحذوفٌ ولا مُخفىً) يَسأَلُها
// `cart/add` وحَلقَةُ الإتمام، و`CheckoutCart.RefreshAsync` تُراجِعُ السَلَّةَ قَبلَ قَرارِ البِطاقَة: تَحذِفُ ما لا يُشتَرى وتَضبِطُ السِعرَ عَلى سِعرِ الإعلان
// وتَكتُبُها بِنُسخَةٍ جَديدَة، فَتَعودُ النُقطَةُ إلى خُطوَةِ الدَفعِ بِـ`cart_changed` قَبلَ أَيِّ وِلادَة — لا يُشحَنُ سِعرٌ لَم يُعرَض.

public class CheckoutCartAvailabilityTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

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

    private static Listing Created(decimal price) => Apply(new Listing(), new ListingCreated(Guid.NewGuid(), "shop", "سِلعَة", null, price, "c", null, null, new(), DateTime.UtcNow));

    private static Listing Apply(Listing l, object e)
    {
        switch (e)
        {
            case ListingCreated c: l.Apply(c); break;
            case ListingDeleted d: l.Apply(d); break;
            case ListingModerated m: l.Apply(m); break;
            case ListingEdited x: l.Apply(x); break;
        }
        return l;
    }

    private static ACommerce.Kit.Cart.Cart CartOf(params (Listing Listing, decimal Price)[] lines)
    {
        var cart = new ACommerce.Kit.Cart.Cart { Id = Guid.NewGuid(), UpdatedAt = new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc) };
        foreach (var (l, p) in lines)
            cart.Items.Add(new ACommerce.Kit.Cart.CartItem { ListingId = l.Id, Title = l.Title, UnitPriceSar = p, Quantity = 1 });
        return cart;
    }

    private static Dictionary<Guid, Listing?> Index(params Listing[] listings) => listings.ToDictionary(l => l.Id, l => (Listing?)l);

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-24؛ كانَ <c>Today_a_deleted_or_hidden_listing_is_still_a_document_so_a_null_check_never_catches_it</c>، مُودَعٌ في
    /// <c>bdd6611</c>): الوَثيقَةُ قائِمَةٌ كَما كانَت، و<see cref="CheckoutCart.Buyable"/> تَرُدُّ المَحذوفَ والمَخفِيَّ والغائِب — وتَقبَلُ القائِمَ وما تَبَدَّلَ سِعرُه
    /// (السِعرُ لِلمُراجَعَةِ لا لِلبابِ). مُوجِبٌ وسالِب.</para>
    /// </summary>
    [Fact]
    public void Buyable_refuses_a_deleted_a_hidden_or_an_absent_listing_and_accepts_a_live_one()
    {
        var deleted = Apply(Created(200m), new ListingDeleted(Guid.NewGuid(), DateTime.UtcNow));
        var hidden = Apply(Created(200m), new ListingModerated(Guid.NewGuid(), true, "مُخالَفَة", Guid.NewGuid(), DateTime.UtcNow));
        var shown = Apply(Apply(Created(200m), new ListingModerated(Guid.NewGuid(), true, "x", Guid.NewGuid(), DateTime.UtcNow)),
            new ListingModerated(Guid.NewGuid(), false, "رُفِع", Guid.NewGuid(), DateTime.UtcNow));
        var repriced = Apply(Created(100m), new ListingEdited(Guid.NewGuid(), null, null, 250m, null, null, null, null, DateTime.UtcNow));
        Assert.True(deleted is { IsDeleted: true });
        Assert.False(CheckoutCart.Buyable(deleted));
        Assert.False(CheckoutCart.Buyable(hidden));
        Assert.False(CheckoutCart.Buyable(null));
        Assert.True(CheckoutCart.Buyable(Created(200m)));
        Assert.True(CheckoutCart.Buyable(shown));
        Assert.True(CheckoutCart.Buyable(repriced));
        output.WriteLine("مَحذوفٌ ومَخفِيٌّ وغائِب ⇒ لا · قائِمٌ ومُظهَرٌ بَعدَ إخفاءٍ ومُعادُ التَسعير ⇒ نَعَم");
    }

    /// <summary>مُراجَعَةٌ لا تُغَيِّرُ سَلَّةً تَصدُقُ عَلى إعلاناتِها — <c>false</c> والسَلَّةُ بِحَرفِها (سالِبُ <c>cart_changed</c>).</summary>
    [Fact]
    public void Reconcile_leaves_a_cart_that_matches_its_listings_untouched()
    {
        var a = Created(100m);
        var b = Created(40m);
        var cart = CartOf((a, 100m), (b, 40m));
        Assert.False(CheckoutCart.Reconcile(cart, Index(a, b)));
        Assert.Equal(new[] { 100m, 40m }, cart.Items.Select(i => i.UnitPriceSar));
    }

    /// <summary>مُوجِبُ <c>cart_changed</c>: بَندٌ حُذِفَ إعلانُه وآخَرُ أُخفِيَ وثالِثٌ غابَ تُحذَفُ، وسِعرٌ تَبَدَّلَ يُضبَطُ عَلى سِعرِ الإعلانِ (‏100 ← 250 بِحَرفِ مِجَسِّ الشَكّاك).</summary>
    [Fact]
    public void Reconcile_drops_unavailable_lines_and_reprices_changed_ones()
    {
        var deleted = Apply(Created(200m), new ListingDeleted(Guid.NewGuid(), DateTime.UtcNow));
        var hidden = Apply(Created(30m), new ListingModerated(Guid.NewGuid(), true, "مُخالَفَة", Guid.NewGuid(), DateTime.UtcNow));
        var gone = Created(10m);
        var repriced = Apply(Created(100m), new ListingEdited(Guid.NewGuid(), null, null, 250m, null, null, null, null, DateTime.UtcNow));
        var kept = Created(5m);
        var cart = CartOf((deleted, 200m), (hidden, 30m), (gone, 10m), (repriced, 100m), (kept, 5m));

        Assert.True(CheckoutCart.Reconcile(cart, Index(deleted, hidden, repriced, kept)));
        Assert.Equal(new[] { repriced.Id, kept.Id }, cart.Items.Select(i => i.ListingId));
        Assert.Equal(new[] { 250m, 5m }, cart.Items.Select(i => i.UnitPriceSar));
        output.WriteLine($"خَمسَةُ بُنود ⇒ {cart.Items.Count}: حُذِفَ المَحذوفُ والمَخفِيُّ والغائِب، و100 ← 250");
    }

    /// <summary>
    /// <para><b>المُراجَعَةُ عَلى جَلسَةٍ</b> (عالَمُ الوَثائِق): سَلَّةٌ تَغَيَّرَت ⇒ تُكتَبُ بِنُسخَةٍ جَديدَةٍ وحِفظٍ واحِد؛ وسَلَّةٌ لَم تَتَغَيَّر ⇒ صِفرُ كِتابَة.</para>
    /// </summary>
    [Fact]
    public async Task RefreshAsync_writes_the_reviewed_cart_with_a_new_version_only_when_it_changed()
    {
        var world = new DocWorld();
        var live = Created(80m);
        var deleted = Apply(Created(200m), new ListingDeleted(Guid.NewGuid(), DateTime.UtcNow));
        world.Put(live).Put(deleted);

        var same = CartOf((live, 80m));
        world.Put(same);
        Assert.False(await CheckoutCart.RefreshAsync(world.Session, same));
        Assert.Equal(0, world.SaveCalls);

        var stale = CartOf((live, 80m), (deleted, 200m));
        var before = stale.UpdatedAt;
        Assert.True(await CheckoutCart.RefreshAsync(world.Session, stale));
        Assert.Equal(1, world.SaveCalls);
        var stored = world.Read<ACommerce.Kit.Cart.Cart>(stale.Id.ToString())!;
        Assert.Single(stored.Items);
        Assert.True(stored.UpdatedAt > before);
    }

    // ═══ البابُ في المَصدَر ═════════════════════════════════════════════════════════════════════════════════════

    /// <summary>الفاحِص: المُراجَعَةُ في جِسمِ الإتمامِ بَعدَ قِراءَةِ السَلَّةِ وقَبلَ قَرارِ البِطاقَةِ وقَبلَ الحَلقَة، والحَلقَةُ و<c>cart/add</c> تَسأَلانِ
    /// <see cref="CheckoutCart.Buyable"/>، وما مِن شَرطِ غِيابٍ وَحدَه. يُعيدُ ما خالَف.</summary>
    private static List<string> ReviewViolations(string submit, string add)
    {
        var v = new List<string>();
        var read = submit.IndexOf("var cart = await s.LoadAsync<ACommerce.Kit.Cart.Cart>(userId);", StringComparison.Ordinal);
        var review = submit.IndexOf("if (await Services.Deals.CheckoutCart.RefreshAsync(s, cart)) return Results.Redirect(", StringComparison.Ordinal);
        var facts = submit.IndexOf("gateway.CardFactsAsync(", StringComparison.Ordinal);
        var loop = submit.IndexOf("foreach (var item in cart.Items)", StringComparison.Ordinal);
        if (review < 0) v.Add("لا مُراجَعَةَ لِلسَلَّة");
        else if (!(read >= 0 && read < review && (facts < 0 || review < facts) && (loop < 0 || review < loop))) v.Add("المُراجَعَةُ في غَيرِ مَوضِعِها");
        if (!submit.Contains("Services.Deals.CheckoutCart.Changed", StringComparison.Ordinal)) v.Add("الرُجوعُ بِلا cart_changed");
        if (!submit.Contains("if (!Services.Deals.CheckoutCart.Buyable(listing)) { line++; continue; }", StringComparison.Ordinal)) v.Add("الحَلقَةُ لا تَسأَلُ Buyable");
        if (!add.Contains("if (!Services.Deals.CheckoutCart.Buyable(listing)) return Results.Redirect(", StringComparison.Ordinal)) v.Add("cart/add لا يَسأَلُ Buyable");
        if (submit.Contains("listing is null", StringComparison.Ordinal) || add.Contains("listing is null", StringComparison.Ordinal)) v.Add("شَرطُ غِيابٍ وَحدَه باقٍ");
        return v;
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-24؛ كانَ <c>Today_the_checkout_and_cart_add_refuse_only_an_absent_listing_and_charge_the_cart_price</c>، مُودَعٌ في
    /// <c>bdd6611</c>): الإتمامُ يُراجِعُ السَلَّةَ قَبلَ قَرارِ البِطاقَةِ والحَلقَةُ و<c>cart/add</c> يَسأَلانِ <see cref="CheckoutCart.Buyable"/>، وصَفحَةُ الإتمامِ تَرسُمُ
    /// <c>cart_changed</c> مِن القامُوسِ بِحَرفِه، و‏ADR-032 §٨‑ح‑١ مُلحَقٌ بِتَصحيحٍ مُؤَرَّخ (وجُملَتُه الأولى باقِيَةٌ بِحَرفِها).</para>
    /// </summary>
    [Fact]
    public void The_checkout_reviews_the_cart_before_the_card_decision_and_both_doors_ask_Buyable()
    {
        var endpoints = Endpoints();
        var submit = Segment(endpoints, "app.MapPost(\"/{slug}/checkout/submit\"", ".RequireStoreWritable();");
        var add = Segment(endpoints, "app.MapPost(\"/{slug}/listings/{id:guid}/cart/add\"", ".RequireStoreWritable();");
        var violations = ReviewViolations(submit, add);
        Assert.True(violations.Count == 0, string.Join(" · ", violations));

        var page = WriteEndpointGuardTests.StripMarkupComments(Source(Template, "Components", "Pages", "CheckoutPage.razor"));
        Assert.Contains("CheckoutCart.Changed => L[\"checkout.payment.err_cart_changed\"],", page, StringComparison.Ordinal);
        Assert.Equal("تَغَيَّرَ في سَلَّتِكَ شَيءٌ مُنذُ وَضَعتَه — مُنتَجٌ لَم يَعُد مُتاحاً أَو تَبَدَّلَ سِعرُه — فَحَدَّثناها ولَم يُطلَب شَيء: راجِعِ المَجموعَ ثُمَّ أَتِمَّ الطَّلَب.",
            ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "checkout.payment.err_cart_changed"));

        var adr = Source("docs", "ADR-032-THE-PRODUCT-IS-A-MULTI-VENDOR-STORE-SAAS-PAID-BY-SUBSCRIPTION.md");
        Assert.Contains("النقطةُ تتجاوزُ بندًا حُذِفَ إعلانُه بعدَ وضعِه في السلّة", adr, StringComparison.Ordinal);
        Assert.Contains("تصحيحُ §٨‑ح‑١ — يُلحَقُ ‏2026-10-24", adr, StringComparison.Ordinal);
        output.WriteLine($"فُحِصَ بابا الإتمامِ وcart/add وصَفحَةُ الإتمامِ والقامُوسُ والـADR · مُخالَفات: {violations.Count}");
    }

    /// <summary><b>والفاحِصُ يُحمِرُ</b> (القاعِدَة ١٠): عَيِّنَةٌ بِشَكلِ البابَينِ عِندَ <c>bdd6611</c> — شَرطُ غِيابٍ وَحدَه ولا مُراجَعَة — بِمُخالَفاتِها، وعَيِّنَةٌ
    /// تُراجِعُ بَعدَ قَرارِ البِطاقَة بِـ«في غَيرِ مَوضِعِها».</summary>
    [Fact]
    public void The_review_checker_goes_red_on_the_shape_the_doors_had_before()
    {
        const string SubmitBefore = "var cart = await s.LoadAsync<ACommerce.Kit.Cart.Cart>(userId); gateway.CardFactsAsync(x); foreach (var item in cart.Items) { if (listing is null) { line++; continue; } }";
        const string AddBefore = "if (listing is null) return Results.Redirect(Link(req, slug, \"explore\"));";
        var before = ReviewViolations(SubmitBefore, AddBefore);
        Assert.Equal(new[] { "لا مُراجَعَةَ لِلسَلَّة", "الرُجوعُ بِلا cart_changed", "الحَلقَةُ لا تَسأَلُ Buyable", "cart/add لا يَسأَلُ Buyable", "شَرطُ غِيابٍ وَحدَه باقٍ" }, before);

        const string Late = "var cart = await s.LoadAsync<ACommerce.Kit.Cart.Cart>(userId); gateway.CardFactsAsync(x); "
            + "if (await Services.Deals.CheckoutCart.RefreshAsync(s, cart)) return Results.Redirect(Services.Deals.CheckoutCart.Changed); "
            + "foreach (var item in cart.Items) { if (!Services.Deals.CheckoutCart.Buyable(listing)) { line++; continue; } }";
        const string AddAfter = "if (!Services.Deals.CheckoutCart.Buyable(listing)) return Results.Redirect(Link(req, slug, \"explore\"));";
        Assert.Equal(new[] { "المُراجَعَةُ في غَيرِ مَوضِعِها" }, ReviewViolations(Late, AddAfter));
        output.WriteLine($"عَيِّنَةُ ما قَبلُ: {before.Count} مُخالَفات · عَيِّنَةُ المُراجَعَةِ المُتَأَخِّرَة: 1");
    }
}
