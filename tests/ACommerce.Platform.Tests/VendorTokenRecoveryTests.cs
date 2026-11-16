using ACommerce.Kit.Delivery;
using ACommerce.Platform.I18n;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Shipping;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ بائِعٌ ضاعَ رَمزُ حِسابِه عِندَ OTO — تَوصيفُ «اليَوم» قَبلَ قَرارِ المالِك د12 (‏2026-10-29) ═══════════════════════════════
//
// **(١) «أ» بِاختِيارِ المالِك**: «يُحجب رابط التفعيل حين يضيع الرمز، ويظهر بدله "أعد المحاولة"، فلا يفعّل البائع حسابًا لا نملك رمزه». **المَقيسُ
// اليَوم**: `RegisterAsync` يَضَعُ رابِطَ التَفعيلِ في وِعاءِ العَرضِ **قَبلَ** الخَتم، فَإن تَعَثَّرَ الخَتمُ `SealAttempts` مَرّاتٍ صَعِدَ الاستِثناءُ (صَفحَةُ خَطَأ)
// والرابِطُ باقٍ تَعرِضُه «إعلاناتي» في أَوَّلِ فَتحٍ بَعدَه — حِسابٌ يُفَعَّلُ ولا نَملِكُ رَمزَه. (والاستِثناءُ نَفسُه مُثَبَّتٌ في `VendorRegisterSealRetryTests`.)
//
// **(٢) «أ»**: «يُسمح للبائع بلصق رمز جديد بيده … في حالتين وحدهما: رمز محفوظ لا يُفكّ، ورمز ضاع حفظه بعد تسجيل نجح عند OTO». **المَقيسُ اليَوم**:
// في وَضعِ الرَمزِ السوقِيِّ اللَصقُ مُغلَقٌ لِكُلِّ بائِع (`vendor_paste_closed`) ولَو كانَ رَمزُه المَحفوظُ لا يُفَكّ، والتَسجيلُ يُجابُ `vendor_entry_unreadable`
// بِنَصٍّ يُحيلُ إلى «مالِكِ المَتجَر» الَّذي لا يَملِكُ سَحبَ رَمزِ البائِع. يَنقَلِبُ كُلُّ صَفٍّ بِاسمِه في كوميتِ القَرار.

public class VendorTokenRecoveryTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;
    private const string Inbound = "https://wasayel.example/theme-demo/shipping/oto/order-status";

    // **وقُلِبَ الصَفّانِ بِاسمَيهِما** (‏2026-10-29، قَرارُ المالِك د12؛ تَوصيفُهُما `d970122`؛ ‏ADR-033 §٧‑ش البَند ٦).

    private static VendorShippingView? View(VendorShippingService surface, Guid owner)
        => surface.ViewAsync(Slug, owner).GetAwaiter().GetResult();

    /// <summary><b>قُلِبَ بِاسمِه</b> (كانَ <c>Today_a_register_whose_token_is_lost_escapes_and_leaves_the_activation_link_for_the_next_view</c>): «Register» نَجَحَ والخَتمُ
    /// تَعَثَّرَ ثَلاثاً ⇒ <c>vendor_register_token_lost</c> بِلا استِثناء، <b>ولا رابِطَ تَفعيلٍ في وِعاءِ العَرض</b>، ومَدخَلٌ فَعّالٌ بِلا رَمزٍ يَفتَحُ بابَ اللَصق؛
    /// وإعادَةُ التَسجيلِ لا تَفتَحُ حِساباً ثانِياً — <c>vendor_entry_unreadable</c> بِلا نِداء.
    /// <para><b>وقُلِبَ ثانِيَةً بِاسمِه</b> (‏2026-10-29، مُراجَعَةُ تَيّارِ S4 الأولى — إتمامُ قَرارِ المالِك د12 (١) «أ»: «يُحجب رابط التفعيل … ويظهر بدله "أعد المحاولة"»؛ كانَ
    /// <c>A_register_whose_token_is_lost_answers_token_lost_withholds_the_link_and_opens_the_paste_door</c>، مُودَعٌ في <c>46170f8</c>): آخِرُ الجُملَةِ أَعلاه انقَلَب — نَصُّ
    /// الضَياعِ يَقولُ «أَعِدِ المُحاوَلَة» والإعادَةُ كانَت مُغلَقَة. صارَت <b>تَسجيلاً مِن جَديد</b>: نِداءُ «Register» ثانٍ، ورَمزُه مَختومٌ فَوقَ مَدخَلِ الضَياع، ورابِطُ تَفعيلِه
    /// في وِعاءِ العَرض، ولا بابَ لَصقٍ بَعدَه — حِسابٌ نَملِكُ رَمزَه.</para></summary>
    [Fact]
    public async Task A_register_whose_token_is_lost_withholds_the_link_opens_the_paste_door_and_a_retry_registers_again_keeping_the_new_token()
    {
        var owner = Guid.NewGuid();
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var surface = VendorRegisterBucketTests.Surface(w);
        VendorRegisterSealRetryTests.FailSaves(w.Docs, failAt: 2, failures: VendorShippingService.SealAttempts);

        var error = await Record.ExceptionAsync(async () =>
        {
            var res = await surface.RegisterAsync(Slug, VendorShippingTests.Registrant(owner), VendorShippingTests.Form(), Inbound);
            Assert.Equal(VendorShippingService.RegisterTokenLost, res.Code);
        });
        var link = surface.TakeActivationLink(Slug, owner);
        var entry = w.Docs.Read<TenantProviderBinding>(TenantProviderBinding.VendorEntryId(ProviderCapabilities.Delivery, owner));
        var lostView = View(surface, owner)!;
        var retry = await surface.RegisterAsync(Slug, VendorShippingTests.Registrant(owner), VendorShippingTests.Form(), Inbound);
        var after = w.Docs.Read<TenantProviderBinding>(TenantProviderBinding.VendorEntryId(ProviderCapabilities.Delivery, owner))!;
        var retryLink = surface.TakeActivationLink(Slug, owner);

        output.WriteLine($"الاستِثناء: {error?.GetType().Name ?? "لا"} · رابِطُ التَفعيل: {link ?? "لا"} · المَدخَل: {(entry is null ? "لا" : entry.Status + "/" + entry.Values.Count)} · " +
                         $"الإعادَة: {retry.Code ?? "ok"} · نِداءاتُ Register: {w.Oto.CallsTo(OtoClient.RegisterPath)} · رابِطُ الإعادَة: {(retryLink is null ? "لا" : "قائِم")}");
        Assert.Null(error);
        Assert.Null(link);
        Assert.NotNull(entry);
        Assert.True(entry!.IsActive);
        Assert.Empty(entry.Values);
        Assert.True(surface.TokenUnreadable(Slug, owner, entry));
        Assert.True(lostView.TokenUnreadable);
        Assert.True(lostView.TokenLost);

        Assert.True(retry.Ok, retry.Code);
        Assert.Equal(2, w.Oto.CallsTo(OtoClient.RegisterPath));
        Assert.Equal("_refreshToken_", after.Values[VendorShippingSurface.RefreshField!]
            .Unseal(w.Vault, VendorShippingSurface.AadFor(Slug, VendorShippingSurface.RefreshField!, owner)));
        Assert.NotNull(retryLink);
        Assert.False(View(surface, owner)!.TokenUnreadable);
    }

    /// <summary>‏<see cref="VendorShippingService.IsTokenLostMarker"/> مُوجَباً وسالِباً (‏2026-10-29، مُراجَعَةُ تَيّارِ S4 الأولى): فَعّالٌ بِلا رَمزٍ ⇒ نَعَم — والعَرضُ
    /// <c>TokenLost</c>؛ وفَعّالٌ بِرَمزٍ مِن خِزانَةٍ أُخرى (لا يَنفَكّ) ⇒ لا — والتَسجيلُ عَلَيه <c>vendor_entry_unreadable</c> بِلا نِداءٍ كَما كان، والعَرضُ <c>TokenUnreadable</c>
    /// بِلا <c>TokenLost</c>؛ وبِرَمزٍ يَنفَكُّ، ومَسحوبٌ بِلا رَمز، وغائِب ⇒ لا.</summary>
    [Fact]
    public async Task IsTokenLostMarker_positive_and_negative()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var bare = DealShipTests.VendorEntry(w.Vault, Slug, DealShipTests.Seller);
        bare.Values.Clear();
        var revokedBare = DealShipTests.VendorEntry(w.Vault, Slug, DealShipTests.Seller, status: TenantProviderBinding.StatusRevoked);
        revokedBare.Values.Clear();
        Assert.True(VendorShippingService.IsTokenLostMarker(bare));
        Assert.False(VendorShippingService.IsTokenLostMarker(DealShipTests.VendorEntry(DealShipTests.NewVault(), Slug, DealShipTests.Seller)));
        Assert.False(VendorShippingService.IsTokenLostMarker(DealShipTests.VendorEntry(w.Vault, Slug, DealShipTests.Seller)));
        Assert.False(VendorShippingService.IsTokenLostMarker(revokedBare));
        Assert.False(VendorShippingService.IsTokenLostMarker(null));

        var surface = VendorRegisterBucketTests.Surface(w);
        w.Docs.Put(DealShipTests.VendorEntry(DealShipTests.NewVault(), Slug, DealShipTests.Seller));
        var unopened = View(surface, DealShipTests.Seller)!;
        Assert.True(unopened.TokenUnreadable);
        Assert.False(unopened.TokenLost);
        Assert.Equal(VendorShippingService.EntryUnreadable,
            (await surface.RegisterAsync(Slug, VendorShippingTests.Registrant(DealShipTests.Seller), VendorShippingTests.Form(), Inbound)).Code);
        Assert.Equal(0, w.Oto.CallsTo(OtoClient.RegisterPath));
    }

    /// <summary>
    /// <para><b>وصَفحَةُ «إعلاناتي» تَرسُمُ البابَ الَّذي يَقولُه النَصّ</b> (‏2026-10-29، مُراجَعَةُ تَيّارِ S4 الأولى؛ القاعِدَة ١٢): استِمارَةُ التَسجيلِ — نَموذَجٌ إلى
    /// <c>/me/shipping/oto/register</c> — خَلفَ حارِسٍ يَفتَحُها بِلا مَدخَلٍ ولِمَدخَلِ الضَياعِ وَحدَه وبِبَوّابَةِ جِهَةِ التَواصُلِ في الحالَتَين، وبَعدَ بابِ اللَصق؛ ومُقَدِّمَتُها
    /// لِلضَياعِ مِفتاحُها. ونَصُّ الضَياعِ يُسَمّي الاستِمارَةَ واللَصق، ولا يَقولُ «بَعدَ قَليل» عَن بابٍ لا يَنفَتِحُ بِالانتِظار.</para>
    /// </summary>
    [Fact]
    public void The_lost_token_page_draws_the_retry_form_after_the_paste_door_and_the_text_names_both()
    {
        var page = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(
            ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "MyListings.razor"))));
        var recovery = page.IndexOf("@if (shippingView is { TokenUnreadable: true })", StringComparison.Ordinal);
        var guard = page.IndexOf("@if (shippingView is null or { TokenLost: true } && registrant is { } verified)", StringComparison.Ordinal);
        var intro = page.IndexOf("@L.Markup(shippingView is null ? \"shipping.vendor.reg_intro\" : \"shipping.vendor.reg_retry_intro\")", StringComparison.Ordinal);
        var action = page.IndexOf("action=\"@($\"/{Slug}/me/shipping/oto/register\")\"", guard, StringComparison.Ordinal);
        output.WriteLine($"بابُ اللَصق {recovery} · الحارِس {guard} · الاستِمارَة {action} · المُقَدِّمَة {intro}");
        Assert.True(recovery > 0 && guard > recovery && action > guard && intro > action, "التَرتيبُ مَكسور.");

        var lost = LocaleCatalog.Text(LocaleCatalog.Arabic, "shipping.vendor.err_" + VendorShippingService.RegisterTokenLost);
        Assert.Contains("بِاستِمارَةِ التَّسجيلِ أَدناه", lost, StringComparison.Ordinal);
        Assert.Contains("الصِقْ", lost, StringComparison.Ordinal);
        Assert.DoesNotContain("بَعدَ قَليل", lost, StringComparison.Ordinal);
        var retry = LocaleCatalog.Text(LocaleCatalog.Arabic, "shipping.vendor.reg_retry_intro");
        Assert.NotEqual("shipping.vendor.reg_retry_intro", retry);
        Assert.Contains("أَعِدِ المُحاوَلَة", retry, StringComparison.Ordinal);
    }

    /// <summary>وإن رَفَضَت القاعِدَةُ مَدخَلَ الضَياعِ أَيضاً: الرَمزُ نَفسُه ولا رابِطَ ولا استِثناء. <b>وغِيابُ المَدخَلِ يُقاسُ حَيّاً لا هُنا</b>: عالَمُ
    /// الوَثائِقِ يَكتُبُ عِندَ <c>Store</c> لا عِندَ الحِفظ، فَيَبقى فيه ما رَفَضَته القاعِدَة — والصَفُّ الحَيُّ
    /// <c>Four_failed_saves_withhold_the_link_and_leave_no_entry_on_Postgres</c> يَقيسُه.</summary>
    [Fact]
    public async Task A_lost_token_whose_marker_is_refused_too_answers_token_lost_without_a_link()
    {
        var owner = Guid.NewGuid();
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var surface = VendorRegisterBucketTests.Surface(w);
        var saves = VendorRegisterSealRetryTests.FailSaves(w.Docs, failAt: 2, failures: VendorShippingService.SealAttempts + 1);

        VendorShippingResult? res = null;
        var error = await Record.ExceptionAsync(async () =>
            res = await surface.RegisterAsync(Slug, VendorShippingTests.Registrant(owner), VendorShippingTests.Form(), Inbound));

        Assert.Null(error);
        Assert.Equal(VendorShippingService.RegisterTokenLost, res!.Code);
        Assert.Equal(1 + VendorShippingService.SealAttempts + 1, saves());
        Assert.Null(surface.TakeActivationLink(Slug, owner));
        Assert.Equal(0, w.Oto.CallsTo(OtoClient.WebhookPath));
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> (كانَ <c>Today_register_mode_refuses_a_paste_even_for_an_entry_whose_token_does_not_open_and_sends_to_the_store_owner</c>): في وَضعِ
    /// الرَمزِ السوقِيِّ مَدخَلٌ لا يُفَكّ ⇒ التَسجيلُ <c>vendor_entry_unreadable</c> بِنَصٍّ يَدُلُّ عَلى اللَصق، والعَرضُ يَرسُمُ البابَ، واللَصقُ يَحفَظُ الرَمزَ بِخِزانَةِ
    /// النُسخَةِ ويُسَجِّلُ الوارِد.</summary>
    [Fact]
    public async Task Register_mode_opens_the_paste_door_for_an_entry_whose_token_does_not_open_and_says_so()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        w.Docs.Put(DealShipTests.VendorEntry(DealShipTests.NewVault(), Slug, DealShipTests.Seller));   // مَختومٌ بِمِفتاحٍ آخَر — لا يُفَكّ هُنا
        var surface = VendorRegisterBucketTests.Surface(w);
        Assert.Equal(VendorShippingInput.Register, surface.Input);

        var register = await surface.RegisterAsync(Slug, VendorShippingTests.Registrant(DealShipTests.Seller), VendorShippingTests.Form(), Inbound);
        Assert.Equal(VendorShippingService.EntryUnreadable, register.Code);
        Assert.True(View(surface, DealShipTests.Seller)!.TokenUnreadable);

        var paste = await surface.SaveTokenAsync(Slug, ListingVendorGate.Admit(DealShipTests.Seller, "بائِع", 1).Vendor!, "RT-pasted-9d9d", Inbound);

        output.WriteLine($"التَسجيل: {register.Code} · اللَصق: {paste.Code ?? "ok"} · webhook: {w.Oto.CallsTo(OtoClient.WebhookPath)}");
        Assert.True(paste.Ok, paste.Code);
        Assert.Equal("RT-pasted-9d9d", paste.After!.Values[VendorShippingSurface.RefreshField!]
            .Unseal(w.Vault, VendorShippingSurface.AadFor(Slug, VendorShippingSurface.RefreshField!, DealShipTests.Seller)));
        Assert.Equal(1, w.Oto.CallsTo(OtoClient.WebhookPath));
        Assert.Equal(0, w.Oto.CallsTo(OtoClient.RegisterPath));
        Assert.False(View(surface, DealShipTests.Seller)!.TokenUnreadable);
        var text = LocaleCatalog.Text(LocaleCatalog.Arabic, "shipping.vendor.err_" + VendorShippingService.EntryUnreadable);
        Assert.DoesNotContain("مالِكِ المَتجَر", text, StringComparison.Ordinal);
        Assert.Contains("الصِقِ الرَّمزَ", text, StringComparison.Ordinal);
    }

    /// <summary>وسالِبُ البابِ في وَضعِ الرَمزِ السوقِيّ: بِلا مَدخَل، أَو بِمَدخَلٍ رَمزُه يَنفَكّ، أَو بِمَدخَلٍ مَسحوب — <c>vendor_paste_closed</c> بِلا نِداءٍ ولا كِتابَة،
    /// قَبلَ قِراءَةِ الرَمز (القاعِدَة ٦: حَقلٌ فارِغٌ يُجابُ رَمزَ البابِ لا رَمزَ الحَقل).</summary>
    [Fact]
    public async Task Register_mode_keeps_the_paste_closed_outside_the_two_cases_before_reading_the_field()
    {
        var vendor = ListingVendorGate.Admit(DealShipTests.Seller, "بائِع", 1).Vendor!;
        var none = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        Assert.Equal(VendorShippingService.PasteClosed, (await VendorRegisterBucketTests.Surface(none).SaveTokenAsync(Slug, vendor, "", Inbound)).Code);

        var readable = await DealShipTests.ConfirmedCashDeal();
        var surface = VendorRegisterBucketTests.Surface(readable);
        Assert.False(View(surface, DealShipTests.Seller)!.TokenUnreadable);
        Assert.Equal(VendorShippingService.PasteClosed, (await surface.SaveTokenAsync(Slug, vendor, "RT-pasted-9d9d", Inbound)).Code);

        var revoked = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        revoked.Docs.Put(DealShipTests.VendorEntry(DealShipTests.NewVault(), Slug, DealShipTests.Seller, status: TenantProviderBinding.StatusRevoked));
        Assert.Equal(VendorShippingService.PasteClosed, (await VendorRegisterBucketTests.Surface(revoked).SaveTokenAsync(Slug, vendor, "RT-pasted-9d9d", Inbound)).Code);

        foreach (var w in new[] { none, readable, revoked })
        {
            Assert.Empty(w.Oto.Calls);
            Assert.Equal(0, w.Docs.Wrote<TenantProviderBinding>());
        }
    }

    /// <summary>‏<see cref="VendorShippingService.TokenUnreadable"/> مُوجَباً وسالِباً: فَعّالٌ بِلا رَمزٍ ⇒ نَعَم، وفَعّالٌ بِرَمزٍ مِن خِزانَةٍ أُخرى ⇒ نَعَم؛ وبِرَمزٍ يَنفَكُّ،
    /// ومَسحوبٌ، وغائِب ⇒ لا.</summary>
    [Fact]
    public async Task TokenUnreadable_positive_and_negative()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var surface = VendorRegisterBucketTests.Surface(w);
        var owner = DealShipTests.Seller;
        var bare = DealShipTests.VendorEntry(w.Vault, Slug, owner);
        bare.Values.Clear();
        Assert.True(surface.TokenUnreadable(Slug, owner, bare));
        Assert.True(surface.TokenUnreadable(Slug, owner, DealShipTests.VendorEntry(DealShipTests.NewVault(), Slug, owner)));
        Assert.False(surface.TokenUnreadable(Slug, owner, DealShipTests.VendorEntry(w.Vault, Slug, owner)));
        Assert.False(surface.TokenUnreadable(Slug, owner, DealShipTests.VendorEntry(DealShipTests.NewVault(), Slug, owner, status: TenantProviderBinding.StatusRevoked)));
        Assert.False(surface.TokenUnreadable(Slug, owner, null));
        Assert.False(surface.TokenUnreadable("owner-test", owner, DealShipTests.VendorEntry(w.Vault, "owner-test", owner)));
    }

    /// <summary>والنُصوصُ في القامُوسِ صادِقَةٌ عَربِيَّةٌ حَيَّة: رَمزُ الضَياعِ يَقولُ إنّ الرابِطَ لا يُعرَضُ ويَقولُ «أَعِدِ المُحاوَلَة»، ونَصُّ بابِ الاستِردادِ يَدُلُّ عَلى اللَصق.</summary>
    [Fact]
    public void The_lost_token_texts_say_retry_and_the_recovery_door_says_paste()
    {
        var lost = LocaleCatalog.Text(LocaleCatalog.Arabic, "shipping.vendor.err_" + VendorShippingService.RegisterTokenLost);
        Assert.Contains("فَلا نَعرِضُ رابِطَ تَفعيلِه", lost, StringComparison.Ordinal);
        Assert.Contains("أَعِدِ المُحاوَلَة", lost, StringComparison.Ordinal);
        var door = LocaleCatalog.Text(LocaleCatalog.Arabic, "shipping.vendor.reg_token_unreadable");
        Assert.NotEqual("shipping.vendor.reg_token_unreadable", door);
        Assert.Contains("الصِقِ الرَّمزَ", door, StringComparison.Ordinal);
        Assert.Contains(VendorShippingService.RegisterTokenLost, VendorShippingService.Codes);
    }
}
