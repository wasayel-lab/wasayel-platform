using ACommerce.Kit.Payments;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ الدَفعُ عِندَ الاستِلامِ كانَ يَمُرُّ بِمُزَوِّدِ الدَفع ═══════════
//
// **العَطَبُ المَقيسُ يَومَ ‏2026-08-30، بِطَرَفَيه**:
//   • جِسمُ `POST /{slug}/checkout/submit` يَشتَرِط `if (pay != "cod")`.
//   • واستِمارَةُ `CheckoutPage.razor` تُرسِل `card` أَو `cash`.
//   • والرَمزُ `"cod"` وَرَدَ **مَرَّةً واحِدَةً في المُستَودَعِ كُلِّه**
//     — في ذلكَ الشَرطِ نَفسِه، أَي بِلا كاتِبٍ ولا قارِئٍ آخَر.
//
// فَالشَرطُ لا يَكذِبُ أَبَداً: كُلُّ طَلَبٍ يَستَدعي `AuthorizeAsync`،
// وأَوَّلُها الدَفعُ عِندَ الاستِلام. وفي الإنتاجِ يَرُدُّ
// `UnavailablePaymentProvider` بِـ`Failed` فَيُختَمُ
// `payment_status = Failed` عَلى صَفقَةِ نَقدٍ عِندَ الباب — **خَتمٌ
// يَكذِب**.
//
// وأَسوَأُ مِنه: خِيارُ البِطاقَةِ هُوَ **المُؤَشَّرُ افتِراضِيّاً**
// (`checked="@(Pay != "cash")"`) بِلا مُزَوِّدٍ خَلفَه وبِلا أَن تَفحَصَ
// الشاشَةُ `Tenant.PaymentProviderConfigured` — زِرٌّ يَقودُ إلى فَشَلٍ
// مُبتلَع (القاعِدَة ١٢).
//
// **وهذا البَندُ يَسبِقُ وَضعَ التَجرِبَةِ ولا يُؤَجَّل**: بِلا تَوحيدِ
// الرَمزِ أَوَّلاً سَيَمُرُّ الدَفعُ عِندَ الاستِلامِ بِمُزَوِّدِ
// المُحاكاةِ ويُختَمُ بِمَرجِعٍ مُحاكًى — أَي أَنّ وَضعَ التَجرِبَةِ
// يَزيدُ العَطَبَ سوءاً بَدَلَ أَن يَكشِفَه.
public class CheckoutPaymentTests
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

    private static string Endpoints() => Source(
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
        "MarketplaceTemplateExtensions.cs");

    private static string CheckoutRazor() => Source(
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
        "Components", "Pages", "CheckoutPage.razor");

    // ═══ ١) الرَمزُ الَّذي لا يُطابِقُ شَيئاً ═══════════════════════════

    /// <summary><b>لا رَمزَ دَفعٍ بِلا طَرَفٍ ثانٍ.</b> ‏<c>"cod"</c>
    /// كانَ يَرِدُ مَرَّةً واحِدَةً، فَهُوَ بِالتَعريفِ شَرطٌ بِلا
    /// كاتِب.</summary>
    [Fact]
    public void No_payment_code_may_exist_that_nothing_in_the_repository_ever_sends()
    {
        var roots = new[] { Path.Combine(RepoRoot, "libs"), Path.Combine(RepoRoot, "apps") };

        // ─── استِثناءٌ مُسَمّى — ‏2026-10-12 (‏ADR-033 الخُطوَة 3) ─────────────────
        // ‏`"cod"` هُنا **مُفرَدَةُ OTO لا رَمزُ إتمامِ الشِراء**: قيمَةُ `payment_method` في
        // طَلَبِ `createOrder` كَما تَقولُها وَثيقَتُهم («‏Possible values: cod, paid»)،
        // ويُرسِلُها العَميلُ نَفسُه إلى OTO — فَلَها كاتِبٌ، وهُوَ ما يَسأَلُ عَنه هذا
        // الفاحِص. والمِلَفُّ مُسَمّى بِحَرفِه لا بِنَمَط، وإن زالَ مِنه الحَرفُ أَحمَرَّ الصَفُّ
        // حَتّى يُرفَعَ الاستِثناء.
        var pinned = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["libs/kits/Delivery/ACommerce.Kit.Delivery.Core/OtoClient.cs"] =
                "قيمَةُ payment_method عِندَ OTO — يُرسِلُها OtoClient.CreateOrderAsync نَفسُه.",
        };

        var scanned = 0;
        var hits = new List<string>();
        var pinnedSeen = new List<string>();

        foreach (var root in roots)
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".cs", StringComparison.Ordinal) &&
                !file.EndsWith(".razor", StringComparison.Ordinal)) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;

            scanned++;
            var text = File.ReadAllText(file);
            if (!text.Contains("\"cod\"", StringComparison.Ordinal)) continue;
            var rel = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');
            if (pinned.ContainsKey(rel)) pinnedSeen.Add(rel);
            else hits.Add(rel);
        }

        Assert.True(scanned > 200, $"أَداةٌ عَمياء: فُحِصَ {scanned} مِلَفّاً.");
        Assert.True(pinnedSeen.Count == pinned.Count,
            "استِثناءٌ مُسَمّى لَم يَعُد يَحمِلُ الحَرف — يُرفَع: "
            + string.Join("، ", pinned.Keys.Except(pinnedSeen)));
        Assert.True(hits.Count == 0,
            "الرَمزُ \"cod\" ما زالَ في المَصدَر، ولا تُرسِلُه استِمارَةٌ: "
            + string.Join("، ", hits));
    }

    /// <summary><b>والنُقطَةُ تَقرَأُ المَعجَمَ لا حَرفِيَّةً.</b></summary>
    [Fact]
    public void The_checkout_endpoint_branches_on_the_shared_vocabulary()
    {
        var text = Endpoints();
        var start = text.IndexOf("/{slug}/checkout/submit", StringComparison.Ordinal);
        Assert.True(start > 0, "نُقطَةُ إتمامِ الشِراءِ غَير مَوجودَة — الأَداةُ عَمياء.");

        var body = text.Substring(start, Math.Min(4000, text.Length - start));

        Assert.True(body.Contains("CheckoutPaymentPolicy", StringComparison.Ordinal),
            "جِسمُ النُقطَةِ ما زالَ يُقَرِّرُ بِنَفسِه بَدَلَ أَن يَقرَأَ "
            + "الدالَّةَ النَقِيَّة — فَالشاشَةُ والنُقطَةُ يَنجَرِفان.");
    }

    /// <summary><b>والاستِمارَةُ تُرسِلُ قِيَمَ المَعجَمِ لا
    /// نُسَخاً.</b></summary>
    [Fact]
    public void The_checkout_form_sends_the_vocabulary_values_not_copies_of_them()
    {
        var razor = CheckoutRazor();

        Assert.True(razor.Contains("CheckoutPayMethods", StringComparison.Ordinal),
            "‏`CheckoutPage.razor` ما زالَت تَكتُب `card` و`cash` حَرفِيّاً — "
            + "وهُما طَرَفُ مَعجَمٍ مُغلَقٍ بِلا تَعريفٍ واحِد.");
    }

    // ═══ ٢) زِرٌّ يَقودُ إلى فَشَلٍ مُبتلَع ═════════════════════════════

    /// <summary><b>لا تُعرَضُ البِطاقَةُ في مَتجَرٍ لا يَقبِض.</b> نَفسُ
    /// ما تَفعَلُه <c>Plans.razor</c> بِـ<c>PlanPurchasePolicy.Visible</c>
    /// حَرفاً.</summary>
    [Fact]
    public void The_checkout_screen_hides_card_when_the_store_does_not_collect_money()
    {
        var razor = CheckoutRazor();

        Assert.True(razor.Contains("PaymentProviderConfigured", StringComparison.Ordinal),
            "‏`CheckoutPage.razor` لا تَفحَصُ `Tenant.PaymentProviderConfigured` — "
            + "فَتَرسُمُ خِيارَ بِطاقَةٍ بِلا مُزَوِّدٍ خَلفَه، وهو زِرٌّ "
            + "يَقودُ إلى فَشَلٍ مُبتلَع.");
    }

    /// <summary>
    /// <para><b>والرَفضُ يُرى.</b> رَمزُ الخَرقِ يُقرَأُ في الشاشَةِ
    /// لِيُختارَ نَصُّ القامُوس — نَفسُ سابِقَةِ
    /// <c>StudioBilling.razor</c>.</para>
    ///
    /// <para><b>ويُبحَثُ عَن الثابِتِ لا عَن قيمَتِه</b>: الشاشَةُ
    /// الَّتي تَكتُب <c>"pay_card_unavailable"</c> حَرفِيّاً هي
    /// بِعَينِها المَعجَمُ المُغلَقُ ذو الطَرَفَينِ الَّذي كَتَبَ هذا
    /// المِلَفّ. فَالمَطلوبُ أَن تَقرَأَ <b>الثابِت</b>.</para>
    /// </summary>
    [Fact]
    public void A_refused_card_choice_must_say_so_on_screen_and_not_be_swallowed()
    {
        var razor = CheckoutRazor();

        Assert.True(razor.Contains(nameof(CheckoutPayMethods.CardUnavailable), StringComparison.Ordinal),
            "الشاشَةُ لا تَقرَأُ رَمزَ الرَفض — فَالنُقطَةُ تَرُدُّ "
            + "والمُستَخدِمُ يَرى الصَفحَةَ نَفسَها بِلا كَلِمَة.");

        Assert.False(razor.Contains($"\"{CheckoutPayMethods.CardUnavailable}\"", StringComparison.Ordinal),
            "الشاشَةُ تَكتُبُ الرَمزَ حَرفِيّاً — وذاكَ طَرَفٌ يَنجَرِف.");
    }

    /// <summary><b>ومَسارُ العَودَةِ يَحمِلُ ما مَلَأَه المُشتَري</b> —
    /// فَلا يُعيدُ كِتابَةَ عُنوانِه بَعدَ رَفضٍ لَيسَ مِن
    /// صُنعِه.</summary>
    [Fact]
    public void The_refusal_path_carries_back_everything_the_buyer_typed()
    {
        var path = CheckoutPaymentPolicy.RefusalPath(
            CheckoutPayMethods.CardUnavailable, "أَبو خالِد", "+966500000000", "الرياض، حَيّ النَخيل");

        Assert.StartsWith("checkout?step=2&", path, StringComparison.Ordinal);
        Assert.Contains($"err={CheckoutPayMethods.CardUnavailable}", path, StringComparison.Ordinal);
        Assert.Contains("name=", path, StringComparison.Ordinal);
        Assert.Contains("phone=", path, StringComparison.Ordinal);
        Assert.Contains("addr=", path, StringComparison.Ordinal);

        // مُرَمَّزٌ لا خام — فاصِلَةٌ أَو مِسافَةٌ في العُنوانِ لا تَكسِرُه.
        Assert.DoesNotContain(" ", path, StringComparison.Ordinal);
    }

    // ═══ ٣) الجَدوَلُ النَقِيّ — بِطَرَفَيه ═════════════════════════════

    [Theory]
    [InlineData("card", true)]
    [InlineData("cash", false)]
    [InlineData("cod",  false)]   // الرَمزُ المَيِّت: يُقرَأُ نَقداً لا بِطاقَة
    [InlineData("",     false)]
    [InlineData(null,   false)]
    public void Only_the_card_method_ever_calls_the_payment_provider(string? raw, bool calls)
        => Assert.Equal(calls,
            CheckoutPaymentPolicy.CallsProvider(CheckoutPaymentPolicy.Normalize(raw)));

    [Fact]
    public void Cash_on_delivery_never_reaches_the_provider_in_any_store()
    {
        foreach (var collects in new[] { true, false })
        foreach (var canCharge in new[] { true, false })
        {
            var (method, refusal) = CheckoutPaymentPolicy.Decide("cash", collects, canCharge);
            Assert.Equal(CheckoutPayMethods.CashOnDelivery, method);
            Assert.Null(refusal);
            Assert.False(CheckoutPaymentPolicy.CallsProvider(method));
        }
    }

    [Fact]
    public void Card_in_a_store_that_collects_reaches_the_provider()
    {
        var (method, refusal) = CheckoutPaymentPolicy.Decide("card", paymentProviderConfigured: true, providerCanCharge: true);
        Assert.Equal(CheckoutPayMethods.Card, method);
        Assert.Null(refusal);
        Assert.True(CheckoutPaymentPolicy.CallsProvider(method));
    }

    /// <summary><b>وبِطاقَةٌ في مَتجَرٍ لا يَقبِضُ تُرَدُّ بِرَمزٍ، ولا
    /// تُبَدَّلُ صامِتَةً.</b></summary>
    [Fact]
    public void Card_in_a_store_that_does_not_collect_is_refused_with_a_code()
    {
        var (method, refusal) = CheckoutPaymentPolicy.Decide("card", paymentProviderConfigured: false, providerCanCharge: true);
        Assert.Equal(CheckoutPayMethods.CardUnavailable, refusal);
        Assert.False(CheckoutPaymentPolicy.CallsProvider(method));
    }

    // ═══ ٤) المُزَوِّدُ يُسأَلُ عَن قُدرَتِه قَبلَ أَن يُنادى — ‏ADR-032 §٢‑د ═══
    //
    // **المَدخَلُ الَّذي كانَ يَضُرّ** (موجَزُ الفَجَوات P0-1، ‏2026-10-10): رابِطٌ
    // مُستَضافٌ مَلصوقٌ يَقلِبُ `PaymentProviderConfigured` فَتُعرَضُ البِطاقَةُ
    // وتُنادي `IPaymentProvider` العامَّ — وهُوَ في الإنتاجِ
    // `UnavailablePaymentProvider` يَرُدُّ `Failed` دائِماً. فَيُختَمُ
    // `payment_status = Failed` عَلى الصَفقَةِ وتَبقى `Offered`. وكانَ بِلا
    // اختِبارٍ يُحمِرُّ إن عاد — وهذِه الأَربَعَةُ هي ذاكَ الاختِبار.

    /// <summary><b>الموجِبُ خَلِيَّةٌ واحِدَة</b>: المَتجَرُ يَقبِضُ
    /// والمُزَوِّدُ يَستَطيع. <b>والسالِبُ ثَلاث</b> — ومِنها الخَلِيَّةُ
    /// الَّتي كانَت مَفتوحَة: رابِطٌ مَلصوقٌ ومُزَوِّدٌ لا يَقبِض.</summary>
    [Theory]
    [InlineData(true,  true,  true)]
    [InlineData(true,  false, false)]   // P0-1 بِعَينِه: كانَ `true`
    [InlineData(false, true,  false)]
    [InlineData(false, false, false)]
    public void Card_is_offered_only_when_the_store_collects_and_the_provider_can_charge(
        bool collects, bool canCharge, bool offered)
        => Assert.Equal(offered, CheckoutPaymentPolicy.CardOffered(collects, canCharge));

    /// <summary>وطَلَبُ بِطاقَةٍ في تِلكَ الخَلِيَّةِ <b>يُرَدُّ بِرَمزٍ</b>
    /// ولا يُبَدَّلُ صامِتاً ولا يُنادى المُزَوِّد.</summary>
    [Fact]
    public void Card_is_refused_with_a_code_when_the_platform_provider_cannot_charge_even_with_a_pasted_link()
    {
        var (method, refusal) = CheckoutPaymentPolicy.Decide("card", paymentProviderConfigured: true, providerCanCharge: false);
        Assert.Equal(CheckoutPayMethods.CashOnDelivery, method);
        Assert.Equal(CheckoutPayMethods.CardUnavailable, refusal);
        Assert.False(CheckoutPaymentPolicy.CallsProvider(method));
    }

    /// <summary><b>مَن يَستَطيعُ القَبضَ ومَن لا</b>: الفَشَلُ المُغلَقُ
    /// (مُزَوِّدُ الإنتاجِ بِلا تَهيئَة) لا؛ والمُحاكي والتَجرِبَةُ المُعلَنَةُ
    /// نَعَم — وهُما لا يُسَجَّلانِ خارِجَ التَطويرِ إلّا بِكِتابَةٍ صَريحَة
    /// (‏ADR-025).</summary>
    [Fact]
    public void The_closed_failure_provider_cannot_charge_and_the_development_providers_can()
    {
        Assert.False(PaymentChargeSurface.CanCharge(new UnavailablePaymentProvider()));
        Assert.False(PaymentChargeSurface.CanCharge(null));
        Assert.True(PaymentChargeSurface.CanCharge(new MockPaymentProvider()));
        Assert.True(PaymentChargeSurface.CanCharge(new SimulatedPaymentProvider()));
    }

    /// <summary>والشاشَةُ والنُقطَةُ تَسأَلانِ المُسنَدَ نَفسَه — فَلا تَعرِضُ
    /// الشاشَةُ خِياراً سَتَرُدُّه النُقطَة.</summary>
    [Fact]
    public void Both_readers_ask_the_provider_whether_it_can_charge()
    {
        Assert.Contains("PaymentChargeSurface.CanCharge(Payments)", CheckoutRazor(), StringComparison.Ordinal);

        var text = Endpoints();
        var start = text.IndexOf("/{slug}/checkout/submit", StringComparison.Ordinal);
        Assert.True(start > 0, "نُقطَةُ إتمامِ الشِراءِ غَير مَوجودَة — الأَداةُ عَمياء.");
        var body = text.Substring(start, Math.Min(4000, text.Length - start));
        Assert.Contains("PaymentChargeSurface.CanCharge(payments)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_offered_methods_are_a_closed_two_value_vocabulary()
    {
        Assert.Equal(2, CheckoutPayMethods.All.Count);
        Assert.True(CheckoutPayMethods.Contains("card"));
        Assert.True(CheckoutPayMethods.Contains("cash"));
        Assert.False(CheckoutPayMethods.Contains("cod"));
        Assert.False(CheckoutPayMethods.Contains(null));
    }
}
