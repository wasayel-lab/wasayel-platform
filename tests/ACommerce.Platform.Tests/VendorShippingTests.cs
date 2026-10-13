using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ACommerce.Kit.Delivery;
using ACommerce.Kit.Payments;
using ACommerce.Platform.I18n;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Providers;
using ACommerce.Templates.Customer.Marketplace.Services.Shipping;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ حِسابُ الشَحنِ لِلبائِعِ داخِلَ المَتجَر — ‏ADR-033 الخُطوَة 7 (تَيّارُ S8، ‏2026-10-12) ═══════════════════════
//
// البائِعُ يَلصِقُ رَمزَ تَحديثِ حِسابِه عِندَ OTO في «إعلاناتي»، فَيُختَمُ في مَدخَلِه `delivery:{listing_owner}`، ونُصدِرُ لَه سِرَّ وارِدٍ
// نُسَجِّلُه عِندَ OTO بِرَمزِه هُوَ. ويُقاسُ هُنا:
//   ١) «أَيُّ المَدخَلَين» دالَّةٌ نَقِيَّة — لَصقٌ حَتّى يُمنَحَ الرَمزُ السوقِيّ، ثُمَّ تَسجيل، ولا شَيءَ بِلا بيئَةٍ أَو خِزانَة — وفاحِصٌ
//      نَصِّيٌّ يَحمَرُّ إن رُسِمَ المَدخَلانِ مَعاً (مُعَيَّنٌ في ‏24-SHIPPING §٥ الصَفّ ٤: «فاحصٌ يفشل إن ظهر المدخلان معًا»).
//   ٢) بَوّابَةُ هُوِيَّةِ البائِع — مُوجَبٌ وسالِبٌ لِكُلِّ رَمز، والتَرتيب؛ ونَصٌّ عَرَبِيٌّ لِكُلِّ رَمزٍ يَصِلُ الصَفحَة.
//   ٣) مُعَرِّفُ المَدخَلِ وقاعِدَةُ التَجاوُزِ المُسَمّاةُ في مُجموعَةِ المَتجَر — مُوجَبٌ وسالِب.
//   ٤) الحِفظُ عَبرَ الخِزانَةِ الحَقيقِيَّةِ بِمِفتاحِ اختِبارٍ ومُعالِجٍ يُعيدُ رُدودَ وَثيقَةِ OTO: الخَتمُ والفَكّ، وتَسجيلُ الوارِد (‏POST ثُمَّ
//      ‏PUT)، والسِرُّ لا يُستَبدَلُ إلّا حينَ تَقبَلُه OTO — وما أُعطِيَته OTO هُوَ ما يَتَحَقَّقُ بِه الوارِد، مِن اللَصقِ إلى التَسليم.
//   ٥) النُقطَة: حارِسُها قَبلَ الحَقل، وكِتابَتُها مُدَقَّقَة، ولا حارِسَ إدارَةِ مَنَصَّة.
//   ٦) البُرهانُ الحَيُّ عَلى Postgres: المَدخَلُ يُكتَبُ ويُقرَأُ بِمِفتاحِه، ومُجموعَةُ المَتجَرِ تَتَجاوَزُه، و«أَلِلمَتجَرِ ناقِل؟» بِطَرَفَيه.

public class VendorShippingTests(ITestOutputHelper output)
{
    private const string Slug = DealShipTests.Slug;
    private static readonly Guid Seller = DealShipTests.Seller;
    private static readonly Guid Stranger = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
    private const string Pasted = "RT-pasted-7d2f";
    private const string Inbound = "https://wasayel.example/theme-demo/shipping/oto/order-status";

    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static ListingVendor Vendor(Guid? id = null) => ListingVendorGate.Admit(id ?? Seller, "بائِع", 1).Vendor!;

    private static string RefreshAad(string tenant = Slug, Guid? owner = null)
        => VendorShippingSurface.AadFor(tenant, VendorShippingSurface.RefreshField!, owner ?? Seller);

    private static string WebhookAad(string tenant = Slug, Guid? owner = null)
        => VendorShippingSurface.AadFor(tenant, VendorShippingSurface.WebhookField!, owner ?? Seller);

    private static OtoOptions Opts(string environment = OtoEnvironment.Staging, string marketplaceToken = "") => new()
    {
        Environment = environment,
        RefreshToken = DealShipTests.ConfigToken,
        MarketplaceToken = marketplaceToken,
    };

    /// <summary>خِدمَةُ السَطحِ عَلى عالَمِ الشَحنِ نَفسِه: مَداخِلُه وخِزانَتُه ومُعالِجُ OTO — ومُنذُ الخُطوَة 8 (‏2026-10-12) كاتِبُ الأَثَرِ
    /// عَلى مَخزَنِ العالَمِ ووِعاءُ عَرضٍ مَرَّةً في ذاكِرَةِ الاختِبار (يُمَرَّرُ لِيُقرَأَ مِنه رابِطُ التَفعيلِ بِخِدمَةٍ ثانِيَة).</summary>
    private static VendorShippingService Surface(DealShipTests.World w, OtoOptions? opts = null, ProviderVault? vault = null,
        ACommerce.Templates.Customer.Marketplace.Services.Api.ApiKeyRevealStore? reveal = null)
    {
        var options = Options.Create(opts ?? Opts());
        return new VendorShippingService(w.Providers, vault ?? w.Vault,
            new OtoClient(new HttpClient(w.Oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance), options,
            new ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditWriter(w.Docs.Store), reveal ?? NewReveal());
    }

    private static ACommerce.Templates.Customer.Marketplace.Services.Api.ApiKeyRevealStore NewReveal()
        => new(new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));

    private static TenantProviderBinding? Entry(DealShipTests.World w, Guid? owner = null)
        => w.Docs.Read<TenantProviderBinding>(TenantProviderBinding.VendorEntryId(ProviderCapabilities.Delivery, owner ?? Seller));

    private static (HttpMethod Method, JsonElement Body)[] WebhookCalls(OtoRouteHandler oto)
        => oto.Calls.Select((c, i) => (c, i)).Where(x => x.c.Path == OtoClient.WebhookPath)
            .Select(x => (oto.Methods[x.i], JsonDocument.Parse(x.c.Body!).RootElement)).ToArray();

    // ═══ ١. أَيُّ المَدخَلَين — واحِدٌ لا اثنان ═══════════════════════════════════════════

    [Theory]
    [InlineData(OtoEnvironment.Staging, true, "", VendorShippingInput.Paste)]
    [InlineData(OtoEnvironment.Staging, true, "MKT-token", VendorShippingInput.Register)]
    [InlineData(OtoEnvironment.Staging, false, "", VendorShippingInput.None)]
    [InlineData(OtoEnvironment.Staging, false, "MKT-token", VendorShippingInput.None)]
    [InlineData(OtoEnvironment.Production, true, "", VendorShippingInput.None)]
    [InlineData("", true, "MKT-token", VendorShippingInput.None)]
    public void One_input_is_drawn_paste_until_the_marketplace_token_then_register_and_none_without_environment_or_vault(
        string environment, bool vaultAvailable, string marketplaceToken, VendorShippingInput expected)
    {
        var vault = vaultAvailable ? DealShipTests.NewVault() : ProviderVault.FromKek(null);
        Assert.Equal(expected, VendorShippingSurface.InputFor(Opts(environment, marketplaceToken), vault));
    }

    [Fact]
    public void No_options_or_no_vault_draw_nothing_and_the_definition_is_read_by_capability()
    {
        Assert.Equal(VendorShippingInput.None, VendorShippingSurface.InputFor(null, DealShipTests.NewVault()));
        Assert.Equal(VendorShippingInput.None, VendorShippingSurface.InputFor(Opts(), null));
        var def = VendorShippingSurface.Definition;
        Assert.NotNull(def);
        Assert.Equal(ProviderCapabilities.Delivery, def!.Capability);
        Assert.Equal(CredentialKinds.SecretKey, def.Credential.Fields.Single(f => f.Code == VendorShippingSurface.RefreshField).Kind);
        Assert.Equal(CredentialKinds.IssuedSecret, def.Credential.Fields.Single(f => f.Code == VendorShippingSurface.WebhookField).Kind);
        Assert.Equal(Inbound, VendorShippingSurface.InboundUrl("https://wasayel.example/", Slug));
    }

    /// <summary>
    /// <para><b>المَدخَلانِ لا يُرسَمانِ مَعاً — فاحِصٌ نَصِّيٌّ عَلى «إعلاناتي»</b>: سِلسِلَةُ فُروعٍ واحِدَةٌ عَلى <c>shippingInput</c> —
    /// <c>@if (… Paste)</c> ثُمَّ <c>else if (… Register)</c> لا غَير — وكُلُّ حَقلِ لَصقٍ داخِلَ فَرعِ اللَصق، وكُلُّ مَدخَلِ تَسجيلٍ داخِلَ فَرعِ
    /// التَسجيل. ويَطبَعُ عَدَدَ ما فَحَصَه ويَفشَلُ عَلى الصِفر (القاعِدَة ١٠). والصَفحَةُ تَقرَأُ المَدخَلَ مِن <c>Vendors.Input</c> وَحدَه.</para>
    /// </summary>
    [Fact]
    public void The_two_inputs_never_render_together_on_the_vendor_page()
    {
        var page = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(
            RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "MyListings.razor"))));

        const string Enum = "ACommerce.Templates.Customer.Marketplace.Services.Shipping.VendorShippingInput.";
        var branches = Regex.Matches(page, @"(@if|else if) \(shippingInput == " + Regex.Escape(Enum) + @"(\w+)\)");
        Assert.Equal(new[] { "@if:Paste", "else if:Register" },
            branches.Select(m => $"{m.Groups[1].Value}:{m.Groups[2].Value}").ToArray());

        var pasteAt = branches[0].Index;
        var registerAt = branches[1].Index;
        var registerBody = Block(page, registerAt);

        // ما يُعَدُّ مَدخَلاً: حَقلُ رَمزِ التَحديثِ لِلَّصق، ونَموذَجٌ إلى مَسارِ التَسجيلِ لِلتَسجيل.
        var pasteInputs = Regex.Matches(page, "name=\"refresh_token\"").Select(m => m.Index).ToArray();
        var registerInputs = Regex.Matches(page, Regex.Escape("/me/shipping/oto/register")).Select(m => m.Index).ToArray();
        output.WriteLine($"مَدخَلاتُ اللَصق: {pasteInputs.Length} · مَدخَلاتُ التَسجيل: {registerInputs.Length} · فُروع: {branches.Count}");
        Assert.True(pasteInputs.Length + registerInputs.Length > 0, "أَداةٌ عَمياء: صِفرُ مَدخَل.");

        Assert.All(pasteInputs, i => Assert.True(i > pasteAt && i < registerAt, $"حَقلُ لَصقٍ خارِجَ فَرعِه عِندَ {i}"));
        Assert.All(registerInputs, i => Assert.True(i > registerAt && i < registerAt + registerBody.Length,
            $"مَدخَلُ تَسجيلٍ خارِجَ فَرعِه عِندَ {i}"));

        Assert.Contains("shippingInput = Vendors.Input;", page, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(page, @"shippingInput = "));
    }

    /// <summary>الكُتلَةُ مِن أَوَّلِ <c>{</c> بَعدَ <paramref name="from"/> إلى قَوسِها المُقابِل.</summary>
    private static string Block(string code, int from)
    {
        var open = code.IndexOf('{', from);
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0) return code[from..(i + 1)];
        }
        throw new InvalidOperationException("كُتلَةٌ بِلا قَوسٍ مُقابِل — الأَداةُ عَمياء.");
    }

    /// <summary>والقِسمُ في فَرعِ «لَه إعلانات» وَحدَه، والمَدخَلُ يُسأَلُ هُناكَ وَحدَه — هُوِيَّةُ البائِعِ الَّتي تَسأَلُها النُقطَة؛ والصَفحَةُ
    /// لا تَفتَحُ جَلسَة.</summary>
    [Fact]
    public void The_vendor_section_is_drawn_for_a_listing_owner_only_and_the_page_reads_no_session()
    {
        var page = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(
            RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "MyListings.razor"))));
        var hasListings = page.IndexOf("else if (myListings.Count == 0)", StringComparison.Ordinal);
        var section = page.IndexOf("<section id=\"vendor-shipping\"", StringComparison.Ordinal);
        // (‏2026-10-13، G5 (iv)): والفَرعُ يُسأَلُ بِبَوّابَةِ النُقطَةِ بِاسمِها — كانَ «if (myListings.Count > 0)»، والبَوّابَةُ بِلا مُستَهلِكٍ وَقتَ التَشغيل؛
        // ولا يَبقى الشَرطُ مَكتوباً بِعَدِّه (تَكافُؤُهُما `VendorSectionGateTests`).
        var asked = page.IndexOf("if (Vs.ListingVendorGate.IsAllowed(Auth.UserId, myListings.Count))", StringComparison.Ordinal);
        Assert.DoesNotContain("myListings.Count > 0", page, StringComparison.Ordinal);
        var input = page.IndexOf("shippingInput = Vendors.Input;", StringComparison.Ordinal);
        Assert.True(hasListings > 0 && section > hasListings, $"القِسمُ قَبلَ فَرعِ «لَه إعلانات»: {hasListings}/{section}");
        Assert.True(asked > 0 && input > asked, $"المَدخَلُ يُسأَلُ خارِجَ «لَه إعلانات»: {asked}/{input}");
        Assert.DoesNotContain("IDocumentSession", page, StringComparison.Ordinal);
        Assert.DoesNotContain("IDocumentStore", page, StringComparison.Ordinal);
        Assert.Contains("type=\"password\"", page, StringComparison.Ordinal);
    }

    // ═══ ٢. بَوّابَةُ هُوِيَّةِ البائِع ═════════════════════════════════════════════

    [Fact]
    public void Vendor_signed_out_positive_and_negative()
    {
        Assert.Equal(ListingVendorGate.SignedOut, ListingVendorGate.Admit(null, null, 3).Violation!.Code);
        Assert.Equal(ListingVendorGate.SignedOut, ListingVendorGate.Admit(Guid.Empty, "x", 3).Violation!.Code);
        Assert.Null(ListingVendorGate.Admit(null, null, 3).Vendor);
        Assert.Null(ListingVendorGate.Admit(Seller, "بائِع", 3).Violation);
    }

    [Fact]
    public void Vendor_owns_no_listing_positive_and_negative()
    {
        Assert.Equal(ListingVendorGate.OwnsNoListing, ListingVendorGate.Admit(Seller, "بائِع", 0).Violation!.Code);
        Assert.Equal(ListingVendorGate.OwnsNoListing, ListingVendorGate.Admit(Seller, "بائِع", -1).Violation!.Code);
        var admitted = ListingVendorGate.Admit(Seller, "بائِع", 1);
        Assert.Null(admitted.Violation);
        Assert.Equal((Seller, "بائِع"), (admitted.Vendor!.UserId, admitted.Vendor.Name));
        Assert.True(ListingVendorGate.IsAllowed(Seller, 1));
        Assert.False(ListingVendorGate.IsAllowed(Seller, 0));
        Assert.False(ListingVendorGate.IsAllowed(null, 1));
    }

    /// <summary>الجَلسَةُ قَبلَ الإعلانات: مَن لا جَلسَةَ لَه يُجابُ «سَجِّل دُخولَك» ولَو كانَ العَدُّ صِفراً.</summary>
    [Fact]
    public void The_identity_gate_asks_the_session_before_the_listings()
        => Assert.Equal(ListingVendorGate.SignedOut, ListingVendorGate.Admit(null, null, 0).Violation!.Code);

    /// <summary>لِكُلِّ رَمزٍ يَصِلُ الصَفحَةَ نَصٌّ عَرَبِيٌّ حَيّ — مَعجَما البَوّابَةِ والخِدمَةِ بِلا تَداخُل. <b>ومُنذُ الخُطوَة 8 (‏2026-10-12)</b>:
    /// ومَعجَمُ مُدَقِّقِ استِمارَةِ التَسجيلِ (‏14) ومَعجَمُ فَشَلِ عَميلِ OTO (‏5، رَدُّ «Register») — والخِدمَةُ ‏3 ← 5 (`vendor_register_closed`،
    /// `vendor_entry_unreadable`)؛ فَالعَدُّ ‏5 ← 26. <b>ومُنذُ مُراجَعَةِ تَيّارِ S8، الجولة 2 (‏2026-10-12؛ ‏ADR-033 §٧‑ف)</b>: مَعجَمُ بَوّابَةِ جِهَةِ التَواصُلِ
    /// (‏1: `vendor_contact_unverified`)، والخِدمَةُ ‏5 ← 6 (`vendor_register_rate_limited`)؛ فَالعَدُّ ‏26 ← 28.</summary>
    [Fact]
    public void Every_refusal_code_of_the_vendor_surface_has_an_arabic_text()
    {
        var codes = ListingVendorGate.All.Concat(VendorShippingService.Codes).Concat(VendorContactGate.All)
            .Concat(VendorRegistrationValidator.All).Concat(OtoFailures.All).ToArray();
        output.WriteLine($"رُموزُ سَطحِ البائِع: {codes.Length} — {string.Join(" · ", codes)}");
        // (‏2026-10-13، G5 (ii)): والخِدمَةُ ‏6 ← 7 (`vendor_token_refused` — رَمزٌ مَلصوقٌ تَرُدُّه OTO لا يُحفَظ)؛ فَالعَدُّ ‏28 ← 29.
        // (‏2026-10-13، G5 (iii)): والخِدمَةُ ‏7 ← 8 (`vendor_entry_read_failed` — قِراءَةُ المَدخَلِ تَرمي فَلا تَسجيلَ ولا لَصق)؛ فَالعَدُّ ‏29 ← 30.
        Assert.Equal(30, codes.Length);
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        foreach (var code in codes)
        {
            var key = "shipping.vendor.err_" + code;
            var text = LocaleCatalog.Text(LocaleCatalog.Arabic, key);
            Assert.NotEqual(key, text);
            Assert.Matches("[؀-ۿ]", text);
        }
    }

    // ═══ ٣. مُعَرِّفُ المَدخَلِ وقاعِدَةُ التَجاوُز ═══════════════════════════════════════

    [Fact]
    public void The_vendor_entry_id_is_the_capability_a_colon_and_the_owner_in_D_format()
    {
        Assert.Equal("delivery:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
            TenantProviderBinding.VendorEntryId(ProviderCapabilities.Delivery, Seller));
        Assert.ThrowsAny<ArgumentException>(() => TenantProviderBinding.VendorEntryId("teleport", Seller));
    }

    [Theory]
    [InlineData("delivery:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", true)]
    [InlineData("payments:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", true)]
    [InlineData("delivery", false)]
    [InlineData("payments", false)]
    [InlineData("delivery:", false)]
    [InlineData("delivery:not-a-guid", false)]
    [InlineData("delivery:bbbbbbbbbbbb4bbb8bbbbbbbbbbbbbbb", false)]
    [InlineData("teleport:bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", false)]
    [InlineData(":bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Is_vendor_entry_id_positive_and_negative(string? id, bool vendor)
    {
        Assert.Equal(vendor, TenantProviderBinding.IsVendorEntryId(id, out var capability, out var owner));
        Assert.Equal(vendor ? id![..id!.IndexOf(':')] : "", capability);
        Assert.Equal(vendor ? Seller : Guid.Empty, owner);
    }

    private static TenantProviderBinding StoreBinding() => new()
    {
        Id = ProviderCapabilities.Payments,
        Slug = ProviderCapabilities.Payments,
        TenantSlug = Slug,
        ProviderSlug = ProviderCatalog.ForCapability(ProviderCapabilities.Payments)
            .First(d => d.Credential.Fields.Any(f => CredentialKinds.IsLink(f.Kind))).Slug,
        Status = TenantProviderBinding.StatusActive,
        Values = new(StringComparer.Ordinal),
        BoundAt = DateTime.UtcNow,
    };

    /// <summary><b>المُوجَب</b>: مَدخَلُ بائِعٍ لا يَدخُلُ مُجموعَةَ المَتجَر — ومَتجَرٌ لَيسَ فيه إلّا مَداخِلُ بائِعينَ يَبقى <c>Platform</c> بِمَرجِعِه
    /// (التَكافُؤُ الصِفريّ). <b>والسالِب</b>: رَبطُ المَتجَرِ بِجانِبِه يَبقى كَما هُوَ.</summary>
    [Fact]
    public void The_store_set_skips_a_vendor_entry_by_the_named_rule_and_keeps_the_store_binding()
    {
        var vault = DealShipTests.NewVault();
        var vendorEntry = DealShipTests.VendorEntry(vault, Slug, Seller);

        Assert.Same(TenantProviderSet.Platform, TenantProviderSet.FromDocuments(Slug, new[] { vendorEntry }));

        var set = TenantProviderSet.FromDocuments(Slug, new[] { vendorEntry, StoreBinding() });
        Assert.NotSame(TenantProviderSet.Platform, set);
        Assert.Equal(ProviderCapabilities.Payments, Assert.Single(set.Bound).Capability);
        Assert.Null(set.For(ProviderCapabilities.Delivery));
    }

    /// <summary>ما يُكتَبُ في مَجرى الخَطَإِ أَثناءَ <paramref name="act"/> — مُتَزامِناً، ويُعادُ المَجرى الأَصلِيُّ بَعدَه.</summary>
    private static string StderrOf(Action act)
    {
        var original = Console.Error;
        var captured = new StringWriter();
        Console.SetError(TextWriter.Synchronized(captured));
        try { act(); }
        finally { Console.SetError(original); }
        return captured.ToString();
    }

    /// <summary>نَظيرُه لِمَسارٍ لا مُتَزامِن (‏2026-10-12، مُراجَعَةُ تَيّارِ S8، الجولة 2) — ومَسارُ عالَمِ الوَثائِقِ يَتِمُّ مُتَزامِناً، فَلا يَخرُجُ سَطرُه
    /// بَعدَ الاستِعادَة. ولا يُنادى إلّا مِن هذا الصِنف: صُفوفُه تَتَتابَعُ فَلا يَتَسابَقُ مُحَوِّلانِ لِلمَجرى.</summary>
    private static async Task<string> StderrOfAsync(Func<Task> act)
    {
        var original = Console.Error;
        var captured = new StringWriter();
        Console.SetError(TextWriter.Synchronized(captured));
        try { await act(); }
        finally { Console.SetError(original); }
        return captured.ToString();
    }

    /// <summary>كَم مَرَّةً سُئِلَ عالَمُ الوَثائِقِ استِعلاماً (<c>Query</c>) — «لَم تُسأَلْ مَداخِلُ المَتجَر» شَرطٌ يُعَدّ لا يُستَنتَج.</summary>
    private static int Asked(DealShipTests.World w) => w.Docs.Members.Count(m => m == "Query");

    /// <summary>
    /// <para><b>والقاعِدَةُ هي ما يُسكِتُ القِراءَة — مَقيسَة</b>: بِلا القاعِدَةِ كانَ المَدخَلُ يَسقُطُ أَيضاً (مُعَرِّفُه لا يُساوي قُدرَةً) ولَكِن
    /// بِسَطرِ خَطَإٍ «يَحمِلُ مُزَوِّدَ قُدرَةٍ أُخرى» في كُلِّ قِراءَةٍ لِلمَتجَر — فَصَفُّ المَجموعَةِ وَحدَه أَعمى عَنها (طَفرَةُ حَذفِها تَمُرُّ خَضراء،
    /// مَقيسٌ ‏2026-10-12). <b>المُوجَب</b>: مَدخَلُ بائِعٍ يُتَجاوَزُ صامِتاً. <b>والسالِب</b>: مُعَرِّفٌ يُشبِهُه ولَيسَ مَدخَلاً (لاحِقَةٌ لَيسَت مُعَرِّفاً)
    /// لا تَبتَلِعُه القاعِدَة — يَسقُطُ بِسَطرِه كَما كان.</para>
    /// </summary>
    [Fact]
    public void The_named_rule_is_what_skips_a_vendor_entry_silently_and_it_swallows_nothing_else()
    {
        var vendorEntry = DealShipTests.VendorEntry(DealShipTests.NewVault(), Slug, Seller);
        var lookalike = DealShipTests.VendorEntry(DealShipTests.NewVault(), Slug, Seller);
        lookalike.Id = "delivery:not-a-listing-owner";

        var silent = StderrOf(() => Assert.Same(TenantProviderSet.Platform, TenantProviderSet.FromDocuments(Slug, new[] { vendorEntry })));
        var loud = StderrOf(() => Assert.Same(TenantProviderSet.Platform, TenantProviderSet.FromDocuments(Slug, new[] { lookalike })));

        output.WriteLine($"المَدخَل: «{silent.Trim()}» · الشَّبيه: «{loud.Trim()}»");
        Assert.DoesNotContain(vendorEntry.Id, silent, StringComparison.Ordinal);
        Assert.Contains(lookalike.Id, loud, StringComparison.Ordinal);
    }

    // ═══ ٤. الحِفظُ عَبرَ الخِزانَةِ الحَقيقِيَّةِ وتَسجيلُ الوارِد ═════════════════════════════════

    /// <summary>
    /// <para><b>اللَصقُ يُختَمُ ويُسَجِّلُ الوارِدَ بِسِرٍّ جَديدٍ مَختوم</b>: الرَمزُ مُشَذَّباً مَختوماً بِسِياقِ <c>tenant|delivery|field|listing_owner</c>
    /// وذَيلُه وَحدَه ظاهِر؛ و<c>POST /rest/v2/webhook</c> بِرَمزِ البائِعِ نَفسِه وعُنوانِ وارِدِ المَتجَرِ وسِرٍّ مِن ‏64 مِحرَفاً سِتَّ عَشَرِيّاً —
    /// والسِرُّ المَختومُ هُوَ ما أُعطِيَته OTO حَرفاً، بِلا ذَيل؛ ومُعَرِّفُ التَسجيلِ مَحفوظ. ولا قيمَةَ صَريحَةَ في المَدخَل.</para>
    /// </summary>
    [Fact]
    public async Task A_pasted_token_is_sealed_and_the_inbound_is_registered_with_a_new_sealed_secret()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var stored = w.Docs.Wrote<TenantProviderBinding>();

        var res = await Surface(w).SaveTokenAsync(Slug, Vendor(), "  " + Pasted + "  ", Inbound);

        Assert.True(res.Ok, res.Code);
        Assert.Null(res.Before);
        var entry = Entry(w)!;
        Assert.Same(entry, res.After);
        Assert.Equal(2, w.Docs.Wrote<TenantProviderBinding>() - stored);   // قَبلَ التَسجيلِ وبَعدَه
        Assert.Equal((ProviderCapabilities.Delivery, Slug, TenantProviderBinding.StatusActive, Seller.ToString("D")),
            (entry.Slug, entry.TenantSlug, entry.Status, entry.BoundBy));
        Assert.All(entry.Values.Values, v => { Assert.True(v.IsSealed); Assert.Null(v.Plain); });

        var token = entry.Values[VendorShippingSurface.RefreshField!];
        Assert.Equal(Pasted, token.Unseal(w.Vault, RefreshAad()));
        Assert.Equal("7d2f", token.Tail4);

        var (method, body) = Assert.Single(WebhookCalls(w.Oto));
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal(Inbound, body.GetProperty("url").GetString());
        Assert.Equal(OtoClient.OrderStatusWebhookType, body.GetProperty("webhookType").GetString());
        var given = body.GetProperty("secretKey").GetString()!;
        Assert.Matches("^[0-9a-f]{64}$", given);
        Assert.Contains(Pasted, Assert.Single(w.Oto.Calls, c => c.Path == OtoClient.RefreshTokenPath).Body!, StringComparison.Ordinal);

        var secret = entry.Values[VendorShippingSurface.WebhookField!];
        Assert.Equal(given, secret.Unseal(w.Vault, WebhookAad()));
        Assert.Null(secret.Tail4);
        Assert.Equal(("59", (string?)null), (entry.WebhookId, entry.WebhookRefusal));

        // ولا يَفتَحُ في مَتجَرٍ آخَرَ ولا لِبائِعٍ آخَر: السِياقُ يَربِطُه بِهِما.
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => token.Unseal(w.Vault, RefreshAad("ashare")));
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => secret.Unseal(w.Vault, WebhookAad(owner: Stranger)));

        var view = await Surface(w).ViewAsync(Slug, Seller);
        Assert.Equal(new VendorShippingView("7d2f", "59", null), view);
        Assert.Null(await Surface(w).ViewAsync(Slug, Stranger));
    }

    /// <summary>
    /// <para><b>مِن اللَصقِ إلى التَسليم — ما أُعطِيَته OTO هُوَ ما يَتَحَقَّقُ بِه الوارِد</b>: بائِعٌ بِلا مَدخَلٍ يَلصِقُ رَمزَه؛ فَتُشحَنُ صَفقَتُه
    /// بِرَمزِه لا بِرَمزِ التَهيئَة؛ وإشعارٌ تُوَقِّعُه «OTO» بِالسِرِّ الَّذي سُلِّمَ لَها يَمُرُّ ويُكتَبُ التَسليم — وآخَرُ بِسِرٍّ غَيرِه يُرَدّ.</para>
    /// </summary>
    [Fact]
    public async Task What_OTO_was_given_at_registration_is_what_the_inbound_verifies_with_from_paste_to_delivery()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        Assert.True((await Surface(w).SaveTokenAsync(Slug, Vendor(), Pasted, Inbound)).Ok);
        var given = Assert.Single(WebhookCalls(w.Oto)).Body.GetProperty("secretKey").GetString()!;
        w.Oto.Calls.Clear();
        w.Oto.Methods.Clear();

        var shipped = await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh");
        Assert.True(shipped.Ok, shipped.Code);
        var refresh = Assert.Single(w.Oto.Calls, c => c.Path == OtoClient.RefreshTokenPath).Body!;
        Assert.Contains(Pasted, refresh, StringComparison.Ordinal);
        Assert.DoesNotContain(DealShipTests.ConfigToken, refresh, StringComparison.Ordinal);

        w.Oto.Routes[OtoClient.OrderStatusPath] = body => OtoRouteHandler.Tracking(OtoRouteHandler.OrderIdIn(body), status: "delivered");
        var forged = await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.Id, secret: DealShipTests.WebhookSecret));
        Assert.Equal(OtoOrderStatusGate.SignatureMismatch, forged.Code);
        Assert.Equal(DealStage.Shipping, (await w.Deals.LoadAsync(Slug, w.Id))!.Stage);

        var signed = await w.Shipping.ReceiveAsync(Slug, OtoOrderStatusTests.Notice(w.Id, secret: given));
        Assert.Equal(OtoOrderStatusGate.Delivered, signed.Code);
        Assert.Equal(DealStage.Delivered, (await w.Deals.LoadAsync(Slug, w.Id))!.Stage);
    }

    /// <summary><b>الحِفظُ الثاني يُحَدِّثُ التَسجيلَ بِمُعَرِّفِه</b> (‏<c>PUT</c> بِـ<c>id</c>) بِسِرٍّ جَديد، ويُستَبدَلُ السِرُّ المَختومُ بِما قَبِلَته OTO
    /// — والمُعَرِّفُ هُوَ هُوَ؛ والرَمزُ الجَديدُ يَحِلُّ مَحَلَّ القَديم.</summary>
    [Fact]
    public async Task Saving_again_updates_the_registration_by_its_id_and_swaps_the_secret_OTO_accepted()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var surface = Surface(w);
        Assert.True((await surface.SaveTokenAsync(Slug, Vendor(), Pasted, Inbound)).Ok);
        var first = Entry(w)!.Values[VendorShippingSurface.WebhookField!].Unseal(w.Vault, WebhookAad());

        var again = await surface.SaveTokenAsync(Slug, Vendor(), "RT-pasted-91aa", Inbound);

        Assert.True(again.Ok, again.Code);
        Assert.Equal("59", again.Before!.WebhookId);
        var calls = WebhookCalls(w.Oto);
        Assert.Equal(new[] { HttpMethod.Post, HttpMethod.Put }, calls.Select(c => c.Method).ToArray());
        Assert.Equal("59", calls[1].Body.GetProperty("id").GetString());
        var second = calls[1].Body.GetProperty("secretKey").GetString()!;
        Assert.NotEqual(first, second);
        var entry = Entry(w)!;
        Assert.Equal(second, entry.Values[VendorShippingSurface.WebhookField!].Unseal(w.Vault, WebhookAad()));
        Assert.Equal("RT-pasted-91aa", entry.Values[VendorShippingSurface.RefreshField!].Unseal(w.Vault, RefreshAad()));
        Assert.Equal(("59", (string?)null), (entry.WebhookId, entry.WebhookRefusal));
    }

    /// <summary><b>تَحديثٌ تَرُدُّه OTO يُنشَأُ مِن جَديد</b> — حِسابٌ آخَرُ لا يَعرِفُ المُعَرِّفَ القَديم: <c>PUT</c> مَرفوضٌ ثُمَّ <c>POST</c> بِالسِرِّ
    /// نَفسِه، ويُحفَظُ المُعَرِّفُ الجَديدُ والسِرُّ الَّذي قُبِل.</summary>
    [Fact]
    public async Task An_update_OTO_refuses_is_registered_anew_and_the_new_id_and_secret_are_kept()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var surface = Surface(w);
        Assert.True((await surface.SaveTokenAsync(Slug, Vendor(), Pasted, Inbound)).Ok);
        w.Oto.Routes[OtoClient.WebhookPath] = body => JsonDocument.Parse(body!).RootElement.TryGetProperty("id", out _)
            ? OtoRouteHandler.Json(HttpStatusCode.BadRequest, "{\"success\": false, \"otoErrorCode\": \"OTO1001\"}")
            : OtoRouteHandler.Json(HttpStatusCode.OK, "{\"success\": true, \"id\": \"60\", \"message\": \"webhook has been created\"}");

        var again = await surface.SaveTokenAsync(Slug, Vendor(), "RT-other-account-3c3c", Inbound);

        Assert.True(again.Ok, again.Code);
        var calls = WebhookCalls(w.Oto);
        Assert.Equal(new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Post }, calls.Select(c => c.Method).ToArray());
        var secret = calls[2].Body.GetProperty("secretKey").GetString()!;
        Assert.Equal(secret, calls[1].Body.GetProperty("secretKey").GetString());
        var entry = Entry(w)!;
        Assert.Equal(("60", (string?)null), (entry.WebhookId, entry.WebhookRefusal));
        Assert.Equal(secret, entry.Values[VendorShippingSurface.WebhookField!].Unseal(w.Vault, WebhookAad()));
    }

    /// <summary>
    /// <para><b>تَسجيلٌ لا تَقبَلُه OTO لا يَمَسُّ السِرَّ القائِم</b>: يُحفَظُ الرَمزُ الجَديد، ويَبقى السِرُّ الَّذي تُوَقِّعُ بِه OTO الآنَ ومُعَرِّفُه،
    /// ويُكتَبُ رَمزُ فَشَلِها — فَيَبقى الوارِدُ يَتَحَقَّقُ بِالسِرِّ القَديم. <b>وأَوَّلُ تَسجيلٍ لا يُقبَل</b>: رَمزٌ مَختومٌ بِلا سِرٍّ ولا مُعَرِّف،
    /// فَيُشحَنُ بِه ولا يُصَدَّقُ لَه إشعار (<c>notice_unverifiable</c>).</para>
    /// </summary>
    [Fact]
    public async Task A_registration_OTO_does_not_accept_keeps_the_old_secret_and_records_the_refusal()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var surface = Surface(w);
        Assert.True((await surface.SaveTokenAsync(Slug, Vendor(), Pasted, Inbound)).Ok);
        var kept = Entry(w)!.Values[VendorShippingSurface.WebhookField!].Unseal(w.Vault, WebhookAad());
        w.Oto.Routes[OtoClient.WebhookPath] = _ => OtoRouteHandler.Json(HttpStatusCode.ServiceUnavailable, "<html/>");

        var again = await surface.SaveTokenAsync(Slug, Vendor(), "RT-pasted-91aa", Inbound);

        Assert.True(again.Ok, again.Code);
        var entry = Entry(w)!;
        Assert.Equal("RT-pasted-91aa", entry.Values[VendorShippingSurface.RefreshField!].Unseal(w.Vault, RefreshAad()));
        Assert.Equal(kept, entry.Values[VendorShippingSurface.WebhookField!].Unseal(w.Vault, WebhookAad()));
        Assert.Equal(("59", (string?)OtoFailures.Unreachable), (entry.WebhookId, entry.WebhookRefusal));
        Assert.Equal(new VendorShippingView("91aa", "59", OtoFailures.Unreachable), await surface.ViewAsync(Slug, Seller));
        var carrier = await w.Shipping.CarrierForAsync(Slug, (await w.Deals.LoadAsync(Slug, w.Id))!);
        Assert.Equal(("RT-pasted-91aa", kept), (carrier!.RefreshToken, carrier.WebhookSecret));

        // وأَوَّلُ تَسجيلٍ لا يُقبَل — لِبائِعٍ آخَر.
        var stranger = await surface.SaveTokenAsync(Slug, Vendor(Stranger), "RT-first-0b0b", Inbound);
        Assert.True(stranger.Ok, stranger.Code);
        var first = Entry(w, Stranger)!;
        Assert.False(first.Values.ContainsKey(VendorShippingSurface.WebhookField!));
        Assert.Equal(((string?)null, (string?)OtoFailures.Unreachable), (first.WebhookId, first.WebhookRefusal));
        var strangerDeal = (await w.Deals.LoadAsync(Slug, w.Id))!;
        strangerDeal.Refs[DealsService.ListingOwnerRef] = Stranger.ToString();
        var strangerCarrier = await w.Shipping.CarrierForAsync(Slug, strangerDeal);
        Assert.Equal(("RT-first-0b0b", (string?)null), (strangerCarrier!.RefreshToken, strangerCarrier.WebhookSecret));
        Assert.Equal(OtoOrderStatusGate.NoticeUnverifiable,
            OtoOrderStatusGate.Verify(OtoOrderStatusTests.Notice(w.Id), strangerCarrier.WebhookSecret, DealShipTests.Now)!.Code);
    }

    // ─── رُموزُ الخِدمَة — لِكُلٍّ مُوجَبٌ وسالِب، والرَفضُ لا يَكتُبُ ولا يُنادي ─────────────────────────

    private static async Task AssertRefused(DealShipTests.World w, VendorShippingResult res, string code)
    {
        Assert.Equal(code, res.Code);
        Assert.False(res.Ok);
        Assert.Null(Entry(w));
        Assert.Empty(w.Oto.Calls);
        Assert.Equal(0, w.Docs.Wrote<TenantProviderBinding>());
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData(OtoEnvironment.Staging, "MKT-token")]
    [InlineData(OtoEnvironment.Production, "")]
    public async Task Vendor_paste_closed_positive(string environment, string marketplaceToken)
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var res = await Surface(w, Opts(environment, marketplaceToken)).SaveTokenAsync(Slug, Vendor(), Pasted, Inbound);
        await AssertRefused(w, res, VendorShippingService.PasteClosed);
    }

    [Fact]
    public async Task Vendor_paste_closed_negative_the_paste_input_saves()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        Assert.Equal(VendorShippingInput.Paste, Surface(w).Input);
        Assert.True((await Surface(w).SaveTokenAsync(Slug, Vendor(), Pasted, Inbound)).Ok);
    }

    [Fact]
    public async Task Vault_unavailable_positive_and_negative()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var res = await Surface(w, vault: ProviderVault.FromKek(null)).SaveTokenAsync(Slug, Vendor(), Pasted, Inbound);
        await AssertRefused(w, res, VendorShippingService.VaultUnavailable);
        Assert.Equal(ProviderVault.Unavailable, VendorShippingService.VaultUnavailable);
        Assert.True((await Surface(w).SaveTokenAsync(Slug, Vendor(), Pasted, Inbound)).Ok);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Vendor_token_missing_positive(string? token)
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        await AssertRefused(w, await Surface(w).SaveTokenAsync(Slug, Vendor(), token, Inbound), VendorShippingService.TokenMissing);
    }

    [Fact]
    public async Task Vendor_token_missing_negative_one_character_is_a_token()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        Assert.True((await Surface(w).SaveTokenAsync(Slug, Vendor(), "x", Inbound)).Ok);
        Assert.Equal("x", Entry(w)!.Values[VendorShippingSurface.RefreshField!].Unseal(w.Vault, RefreshAad()));
    }

    /// <summary>سَطرُ التَدقيقِ قَبلُ وبَعدُ مُعَتَّم: لا رَمزَ ولا سِرَّ فيه — الدالَّةُ الَّتي يَكتُبُ بِها الاستوديو سَطرَه.</summary>
    [Fact]
    public async Task The_audit_lines_of_a_save_carry_neither_the_token_nor_the_secret()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var surface = Surface(w);
        await surface.SaveTokenAsync(Slug, Vendor(), Pasted, Inbound);
        var res = await surface.SaveTokenAsync(Slug, Vendor(), "RT-pasted-91aa", Inbound);
        var secrets = WebhookCalls(w.Oto).Select(c => c.Body.GetProperty("secretKey").GetString()!).ToArray();
        var lines = new[] { ProviderBindSurface.AuditLine(res.Before), ProviderBindSurface.AuditLine(res.After) };
        output.WriteLine(string.Join("\n", lines));
        Assert.All(lines, l => Assert.False(string.IsNullOrEmpty(l)));
        foreach (var line in lines)
        {
            Assert.DoesNotContain(Pasted, line!, StringComparison.Ordinal);
            Assert.DoesNotContain("RT-pasted-91aa", line!, StringComparison.Ordinal);
            Assert.All(secrets, s => Assert.DoesNotContain(s, line!, StringComparison.Ordinal));
        }
    }

    // ═══ ٥. ناقِلُ بائِعِ الصَفقَة — مَدخَلُه، أَو احتِياطُ مَتجَرِ العَرضِ لِمَن لا مَدخَلَ لَه وَحدَه ═══════════════════

    [Fact]
    public async Task With_a_vendor_entry_the_order_is_created_with_the_vendors_own_token_and_never_the_configured_one()
    {
        var w = await DealShipTests.ConfirmedCashDeal();
        var res = await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh");
        Assert.True(res.Ok, res.Code);
        var refresh = Assert.Single(w.Oto.Calls, c => c.Path == OtoClient.RefreshTokenPath).Body!;
        Assert.Contains(DealShipTests.VendorToken, refresh, StringComparison.Ordinal);
        Assert.DoesNotContain(DealShipTests.ConfigToken, refresh, StringComparison.Ordinal);

        var carrier = (await w.Shipping.CarrierForAsync(Slug, (await w.Deals.LoadAsync(Slug, w.Id))!))!;
        // (‏2026-10-13، G5 (iv)): سَقَطَ `FromVendorEntry` (لا قارِئَ وَقتَ التَشغيل) — والمَصدَرُ رَمزُ البائِعِ نَفسُه وسِرُّه.
        Assert.Equal((DealShipTests.VendorToken, (string?)DealShipTests.WebhookSecret),
            (carrier.RefreshToken, carrier.WebhookSecret));
        Assert.DoesNotContain(DealShipTests.VendorToken, carrier.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(DealShipTests.WebhookSecret, carrier.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_vendor_entry_only_the_demo_store_falls_back_to_the_configured_token_and_it_carries_no_secret()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;
        Assert.True(await w.Shipping.CarrierReadyForAsync(Slug, deal));
        var carrier = (await w.Shipping.CarrierForAsync(Slug, deal))!;
        // (‏2026-10-13، G5 (iv)): بِلا `FromVendorEntry` — رَمزُ التَهيئَةِ بِلا سِرٍّ هُوَ الاحتِياط.
        Assert.Equal((DealShipTests.ConfigToken, (string?)null), (carrier.RefreshToken, carrier.WebhookSecret));

        Assert.False(await w.Shipping.CarrierReadyForAsync("ashare", deal));
        Assert.Null(await w.Shipping.CarrierForAsync("ashare", deal));
        Assert.False(await w.Shipping.CarrierReadyForAsync(Slug, null));
    }

    /// <summary><b>مَدخَلٌ لا يُفَكُّ لا يَسقُطُ إلى الاحتِياط</b> — مِفتاحٌ رَئيسٌ تَبَدَّل: يُقرَأُ المَدخَلُ بِلا فَكٍّ فَتُرسَمُ الاستِمارَة، ثُمَّ لا
    /// يُفَكُّ فَيُرَدُّ <c>carrier_unavailable</c> قَبلَ OTO ولا يُكتَب — ورَمزُ التَهيئَةِ مَضبوطٌ لِمَتجَرِ العَرضِ ولا يُستَعمَل.</summary>
    [Fact]
    public async Task A_vendor_entry_that_does_not_open_refuses_the_ship_before_OTO_and_never_falls_back()
    {
        var w = await DealShipTests.ConfirmedCashDeal();
        w.Docs.Put(DealShipTests.VendorEntry(DealShipTests.NewVault(), Slug, Seller));
        var saves = w.Docs.SaveCalls;
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;
        Assert.True(await w.Shipping.CarrierReadyForAsync(Slug, deal));
        Assert.Null(await w.Shipping.CarrierForAsync(Slug, deal));

        var res = await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh");

        Assert.Equal(DealShipAuthorization.CarrierUnavailable, res.Code);
        Assert.Empty(w.Oto.Calls);
        Assert.Equal(saves, w.Docs.SaveCalls);
    }

    /// <summary>والمَدخَلُ لا يُفَكُّ إلّا في مَتجَرِه ولِبائِعِه: عالَمُ الوَثائِقِ بِلا إيجار، فَيُقرَأُ المَدخَلُ نَفسُه بِاسمِ مَتجَرٍ آخَر — والسِياقُ يَرُدُّه؛
    /// ومَنسوخاً إلى مُعَرِّفِ بائِعٍ آخَر — والسِياقُ يَرُدُّه.</summary>
    [Fact]
    public async Task A_vendor_entry_opens_only_in_its_own_store_and_for_its_own_listing_owner()
    {
        var w = await DealShipTests.ConfirmedCashDeal();
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;
        Assert.NotNull(await w.Shipping.CarrierForAsync(Slug, deal));
        Assert.Null(await w.Shipping.CarrierForAsync("ashare", deal));

        var copied = DealShipTests.VendorEntry(w.Vault, Slug, Seller);
        copied.Id = TenantProviderBinding.VendorEntryId(ProviderCapabilities.Delivery, Stranger);
        w.Docs.Put(copied);
        deal.Refs[DealsService.ListingOwnerRef] = Stranger.ToString();
        Assert.True(await w.Shipping.CarrierReadyForAsync(Slug, deal));
        Assert.Null(await w.Shipping.CarrierForAsync(Slug, deal));
    }

    /// <summary>مَدخَلٌ مَسحوبٌ قائِمٌ يَحكُم: لا ناقِلَ، ورَمزُ التَهيئَةِ لا يَحِلُّ مَحَلَّه — الاحتِياطُ لِمَن لا مَدخَلَ لَه وَحدَه.</summary>
    [Fact]
    public async Task A_revoked_vendor_entry_is_no_carrier_and_the_configured_token_does_not_replace_it()
    {
        var w = await DealShipTests.ConfirmedCashDeal();
        w.Docs.Put(DealShipTests.VendorEntry(w.Vault, Slug, Seller, status: TenantProviderBinding.StatusRevoked));
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;

        Assert.False(await w.Shipping.CarrierReadyForAsync(Slug, deal));
        Assert.Null(await w.Shipping.CarrierForAsync(Slug, deal));
        Assert.Equal(DealShipAuthorization.CarrierUnavailable, (await w.Shipping.ShipAsync(Slug, w.Id, DealShipTests.Vendor, "Riyadh")).Code);
        Assert.Empty(w.Oto.Calls);
    }

    /// <summary>
    /// <para><b>وصارَ «ولا يُفتَحُ المَتجَرُ بِالمَداخِل» مَقيساً ‏2026-10-12 (مُراجَعَةُ تَيّارِ S8، الجولة 2)</b> — كانَ الاسمُ يَقولُه ولا يَفحَصُه: عالَمُ الوَثائِقِ
    /// يَرمي عِندَ الاستِعلامِ عَمداً، و<c>HasVendorEntriesAsync</c> يَبتَلِعُ الرَميَ «لا»، فَكانَ <c>False</c> لِـ«ashare» يَصدُقُ بِالخِزانَةِ وبِغَيرِها —
    /// وحَذفُ شَرطِ الخِزانَةِ مِن <c>CarrierReadyForStoreAsync</c> يَمُرُّ أَخضَرَ في ‏3485 صَفّاً (مَقيس). <b>فَالمَقيسُ الآن ما يَنفَردُ بِه شَرطُ
    /// الخِزانَة</b>: بِلا خِزانَةٍ لا تُسأَلُ مَداخِلُ المَتجَرِ أَصلاً — صِفرُ استِعلامٍ وصِفرُ سَطرِ فَشَل — ومُوجَبُه الصَفُّ التالي.</para>
    /// </summary>
    [Fact]
    public async Task Without_a_vault_a_vendor_entry_is_no_carrier_and_the_store_is_not_opened_by_entries()
    {
        var w = await DealShipTests.ConfirmedCashDeal();
        var options = Options.Create(Opts());
        var shipping = new DealShipping(w.Deals,
            new OtoClient(new HttpClient(w.Oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance),
            options, null, w.Providers, ProviderVault.FromKek(null), new FixedClock(DealShipTests.Now));
        var deal = (await w.Deals.LoadAsync(Slug, w.Id))!;

        Assert.False(await shipping.CarrierReadyForAsync(Slug, deal));
        Assert.Null(await shipping.CarrierForAsync(Slug, deal));
        var asked = Asked(w);
        var stderr = await StderrOfAsync(async () => Assert.False(await shipping.CarrierReadyForStoreAsync("ashare")));
        Assert.Equal(asked, Asked(w));                                      // مَداخِلُ المَتجَرِ لَم تُسأَل — لا خِزانَةَ تَفُكُّ ما فيها
        Assert.DoesNotContain("«ashare»", stderr, StringComparison.Ordinal);    // ولا سَطرَ فَشَل: لَم يُسأَلْ شَيء
        Assert.True(await shipping.CarrierReadyForStoreAsync(Slug));      // رَمزُ التَهيئَةِ لِمَتجَرِ العَرضِ يَفتَحُ الوارِدَ كَما كان
        Assert.False(await (await DealShipTests.ConfirmedCashDeal(environment: OtoEnvironment.Production)).Shipping.CarrierReadyForStoreAsync(Slug));
    }

    /// <summary>
    /// <para><b>والمُوجَب: بِخِزانَةٍ تُسأَلُ مَداخِلُ المَتجَر</b> (‏2026-10-12، مُراجَعَةُ تَيّارِ S8، الجولة 2) — مَتجَرٌ غَيرُ مَتجَرِ العَرضِ فَلا رَمزَ تَهيئَةٍ
    /// يُجيبُ قَبلَها. <b>وعالَمُ الوَثائِقِ لا يُجيبُ الاستِعلام</b> (<c>Query</c> يَرمي فيه عَمداً) — فَهذا الصَفُّ لا يَرى الجَوابَ ولا يَدَّعيه: يَرى أَنّ
    /// السُؤالَ وَقَع، وأَنّ استِعلاماً يَفشَلُ «لا ناقِل» (فَشَلٌ مُغلَق: ‏404 بِلا قِراءَة) <b>بِسَطرِ فَشَلِه في مَجرى الخَطَإ يُسَمّي المَتجَر</b> — فَلا
    /// يَلتَبِسُ بِـ«لا مَداخِل». و«نَعَم» حَيثُ مَدخَلٌ فَعّالٌ يُقاسُ عَلى Postgres وَحدَه
    /// (<c>LiveVendorShippingProofTests.A_store_with_only_vendor_entries_opens_the_inbound_with_a_vault_and_not_without_one_on_Postgres</c>).</para>
    /// </summary>
    [Fact]
    public async Task With_a_vault_a_store_asks_its_vendor_entries_and_a_query_that_fails_is_no_carrier_with_its_failure_line()
    {
        const string store = "owner-test";
        var w = await DealShipTests.ConfirmedCashDeal();
        Assert.Null(DealShipping.VendorTokenFor(Opts(), store));
        var asked = Asked(w);

        var stderr = await StderrOfAsync(async () => Assert.False(await w.Shipping.CarrierReadyForStoreAsync(store)));

        output.WriteLine($"سُؤالُ المَداخِل: {Asked(w) - asked} · مَجرى الخَطَإ: «{stderr.Trim()}»");
        Assert.Equal(asked + 1, Asked(w));
        Assert.Contains("[providers]", stderr, StringComparison.Ordinal);
        Assert.Contains($"«{store}»", stderr, StringComparison.Ordinal);
        Assert.Contains("«Query»", stderr, StringComparison.Ordinal);       // جَلسَةُ الاختِبارِ لا تُنَفِّذُ الاستِعلام — CI لا يَرى الجَواب
    }

    // ═══ ٦. النُقطَة ═══════════════════════════════════════════════════

    /// <summary>
    /// <para><b>الحارِسُ قَبلَ الحَقل</b> (القاعِدَة ٦): الجَلسَةُ ثُمَّ <c>ListingVendorGate.Admit</c> بِعَدِّ إعلاناتِه، ولا يُقرَأُ
    /// <c>refresh_token</c> قَبلَهُما؛ و<c>RequireStoreWritable</c> في السِلسِلَة؛ وسَطرُ التَدقيقِ بَعدَ الحِفظِ النّاجِحِ بِالدالَّةِ المُعَتَّمَة؛
    /// <b>ولا حارِسَ إدارَةِ مَنَصَّة</b> — البائِعُ لَيسَ مُديراً. والمَسارُ حَرفِيٌّ.</para>
    /// </summary>
    [Fact]
    public void The_vendor_endpoint_admits_the_listing_owner_before_reading_the_token_and_is_store_writable()
    {
        var code = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Shipping", "VendorShippingEndpoints.cs")));
        var start = code.IndexOf("app.MapPost(\"/{slug}/me/shipping/oto\"", StringComparison.Ordinal);
        Assert.True(start > 0, "النُقطَةُ لَيسَت بِمَسارِها الحَرفِيّ — الأَداةُ عَمياء.");
        var body = code[start..code.IndexOf(".RequireStoreWritable();", start, StringComparison.Ordinal)];

        var session = body.IndexOf("AuthSession.ResolveToken(", StringComparison.Ordinal);
        var admit = body.IndexOf("Services.Shipping.ListingVendorGate.Admit(", StringComparison.Ordinal);
        var field = body.IndexOf("req.Form[\"refresh_token\"]", StringComparison.Ordinal);
        var save = body.IndexOf("vendors.SaveTokenAsync(", StringComparison.Ordinal);
        var audit = body.IndexOf("audit.WriteAsync(", StringComparison.Ordinal);
        output.WriteLine($"جَلسَة={session} · بَوّابَة={admit} · حَقل={field} · حِفظ={save} · تَدقيق={audit}");
        Assert.True(session > 0 && admit > session && field > admit && save > admit && audit > save,
            $"التَرتيبُ مَكسور: {session}/{admit}/{field}/{save}/{audit}");
        Assert.Contains("ProviderBindSurface.AuditLine(res.Before)", body, StringComparison.Ordinal);
        Assert.Contains("ProviderBindSurface.AuditLine(res.After)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("PlatformAdminGuard", body, StringComparison.Ordinal);
        Assert.DoesNotContain("IDocumentSession", body, StringComparison.Ordinal);
        Assert.Contains(".DisableAntiforgery()", code[start..], StringComparison.Ordinal);
    }

    // ═══ ٧. مَسارُ التَسجيلِ عِندَ OTO — خَلفَ الرَمزِ السوقِيّ (‏ADR-033 الخُطوَة 8، تَيّارُ S8، ‏2026-10-12) ═══════════════════

    private const string Marketplace = "MKT-token";

    /// <summary>استِمارَةٌ صالِحَةٌ لِبائِعٍ غَيرِ مُسَجَّلٍ في الضَريبَة — وكُلُّ صَفٍّ يُغَيِّرُ فيها شَيئاً واحِداً.</summary>
    internal static VendorRegistrationForm Form(bool vat = false) => new(
        FirstName: "سارَة", LastName: "البائِعَة", Email: "Vendor.Test@Example.com", Mobile: "+966 500000000",
        City: "Riyadh", BillingAddress: "حَيُّ المِثال، شارِعُ الاختِبار", IsVatRegistered: vat,
        CompanyName: vat ? "مَنشَأَةُ الاختِبار" : "", CrNumber: vat ? "1010101010" : "", VatNumber: vat ? "300000000000003" : "",
        District: vat ? "العُلَيّا" : "", ZipCode: vat ? "12345" : "", BuildingNo: vat ? "1234" : "",
        StreetName: vat ? "شارِعُ المِثال" : "", Consent: true);

    private static string[] Codes(VendorRegistrationForm f) => VendorRegistrationValidator.Validate(f).Select(v => v.Code).ToArray();

    /// <summary>الاستِمارَةُ الصالِحَةُ بِالشَكلَينِ لا تُنتِجُ رَمزاً — سالِبُ المَعجَمِ كُلِّه.</summary>
    [Fact]
    public void A_complete_form_with_or_without_the_vat_mark_is_valid()
    {
        Assert.Empty(Codes(Form()));
        Assert.Empty(Codes(Form(vat: true)));
        Assert.True(VendorRegistrationValidator.IsValid(Form()));
        Assert.Equal(14, VendorRegistrationValidator.All.Count);
        Assert.Equal(14, VendorRegistrationValidator.All.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>لِكُلِّ رَمزٍ مُوجَب: استِمارَةٌ تَخرِقُ قاعِدَتَه وَحدَها تُنتِجُه — وتُنتِجُه وَحدَه.</summary>
    [Theory]
    [InlineData(VendorRegistrationValidator.FirstNameInvalid)]
    [InlineData(VendorRegistrationValidator.LastNameInvalid)]
    [InlineData(VendorRegistrationValidator.EmailInvalid)]
    [InlineData(VendorRegistrationValidator.MobileInvalid)]
    [InlineData(VendorRegistrationValidator.CityMissing)]
    [InlineData(VendorRegistrationValidator.BillingAddressMissing)]
    [InlineData(VendorRegistrationValidator.CompanyNameMissing)]
    [InlineData(VendorRegistrationValidator.CrNumberInvalid)]
    [InlineData(VendorRegistrationValidator.VatNumberInvalid)]
    [InlineData(VendorRegistrationValidator.DistrictMissing)]
    [InlineData(VendorRegistrationValidator.ZipCodeInvalid)]
    [InlineData(VendorRegistrationValidator.BuildingNoInvalid)]
    [InlineData(VendorRegistrationValidator.StreetNameMissing)]
    [InlineData(VendorRegistrationValidator.ConsentMissing)]
    public void Every_registration_code_positive(string code)
    {
        var bad = code switch
        {
            VendorRegistrationValidator.FirstNameInvalid => Form() with { FirstName = new string('س', 31) },
            VendorRegistrationValidator.LastNameInvalid => Form() with { LastName = "" },
            VendorRegistrationValidator.EmailInvalid => Form() with { Email = "vendor-at-example.com" },
            VendorRegistrationValidator.MobileInvalid => Form() with { Mobile = "05-000-0000" },
            VendorRegistrationValidator.CityMissing => Form() with { City = "" },
            VendorRegistrationValidator.BillingAddressMissing => Form() with { BillingAddress = "" },
            VendorRegistrationValidator.CompanyNameMissing => Form(vat: true) with { CompanyName = "" },
            VendorRegistrationValidator.CrNumberInvalid => Form(vat: true) with { CrNumber = "101010101" },
            VendorRegistrationValidator.VatNumberInvalid => Form(vat: true) with { VatNumber = "200000000000003" },
            VendorRegistrationValidator.DistrictMissing => Form(vat: true) with { District = "" },
            VendorRegistrationValidator.ZipCodeInvalid => Form(vat: true) with { ZipCode = "1234a" },
            VendorRegistrationValidator.BuildingNoInvalid => Form(vat: true) with { BuildingNo = "12345" },
            VendorRegistrationValidator.StreetNameMissing => Form(vat: true) with { StreetName = "" },
            VendorRegistrationValidator.ConsentMissing => Form() with { Consent = false },
            _ => throw new ArgumentOutOfRangeException(nameof(code)),
        };
        Assert.Equal(new[] { code }, Codes(bad));
        Assert.False(VendorRegistrationValidator.IsValid(bad));
    }

    /// <summary>
    /// <para><b>ولِكُلِّ رَمزٍ سالِب</b> عِندَ حَدِّه: الاسمُ ثَلاثونَ مِحرَفاً بِالضَبط، والأَرقامُ بِطولِها بِالضَبطِ وبِأَرقامٍ عَرَبِيَّةٍ هِندِيَّةٍ تُقرَأُ
    /// لاتينِيَّة، و«+» والمَسافاتُ في الجَوّالِ تَسقُط، والرَقَمُ الضَريبِيُّ أَوَّلُه ٣ — <b>وحُقولُ الضَريبَةِ الفارِغَةُ أَو الخاطِئَةُ لا تُسأَلُ بِلا عَلامَتِها</b>.</para>
    /// </summary>
    [Theory]
    [InlineData(VendorRegistrationValidator.FirstNameInvalid)]
    [InlineData(VendorRegistrationValidator.LastNameInvalid)]
    [InlineData(VendorRegistrationValidator.EmailInvalid)]
    [InlineData(VendorRegistrationValidator.MobileInvalid)]
    [InlineData(VendorRegistrationValidator.CityMissing)]
    [InlineData(VendorRegistrationValidator.BillingAddressMissing)]
    [InlineData(VendorRegistrationValidator.CompanyNameMissing)]
    [InlineData(VendorRegistrationValidator.CrNumberInvalid)]
    [InlineData(VendorRegistrationValidator.VatNumberInvalid)]
    [InlineData(VendorRegistrationValidator.DistrictMissing)]
    [InlineData(VendorRegistrationValidator.ZipCodeInvalid)]
    [InlineData(VendorRegistrationValidator.BuildingNoInvalid)]
    [InlineData(VendorRegistrationValidator.StreetNameMissing)]
    [InlineData(VendorRegistrationValidator.ConsentMissing)]
    public void Every_registration_code_negative_at_its_edge(string code)
    {
        var edge = code switch
        {
            VendorRegistrationValidator.FirstNameInvalid => Form() with { FirstName = new string('س', 30) },
            VendorRegistrationValidator.LastNameInvalid => Form() with { LastName = new string('ع', 30) },
            VendorRegistrationValidator.EmailInvalid => Form() with { Email = "a@b.sa" },
            VendorRegistrationValidator.MobileInvalid => Form() with { Mobile = VendorRegistrationForm.LatinDigits("+٩٦٦ ٥٠٠٠٠٠٠٠٠") },
            VendorRegistrationValidator.CityMissing => Form() with { City = "الرياض" },
            VendorRegistrationValidator.BillingAddressMissing => Form() with { BillingAddress = "ص.ب ١" },
            VendorRegistrationValidator.CompanyNameMissing => Form() with { CompanyName = "" },
            VendorRegistrationValidator.CrNumberInvalid => Form(vat: true) with { CrNumber = VendorRegistrationForm.LatinDigits("١٠١٠١٠١٠١٠") },
            VendorRegistrationValidator.VatNumberInvalid => Form(vat: true) with { VatNumber = "399999999999999" },
            VendorRegistrationValidator.DistrictMissing => Form() with { District = "" },
            VendorRegistrationValidator.ZipCodeInvalid => Form() with { ZipCode = "not-a-zip" },
            VendorRegistrationValidator.BuildingNoInvalid => Form(vat: true) with { BuildingNo = VendorRegistrationForm.LatinDigits("۱۲۳۴") },
            VendorRegistrationValidator.StreetNameMissing => Form() with { StreetName = "" },
            VendorRegistrationValidator.ConsentMissing => Form(vat: true) with { Consent = true },
            _ => throw new ArgumentOutOfRangeException(nameof(code)),
        };
        Assert.DoesNotContain(code, Codes(edge));
        Assert.Empty(Codes(edge));
    }

    /// <summary>القارِئُ يَقرَأُ الحُقولَ بِأَسمائِها مُشَذَّبَة، والأَرقامَ العَرَبِيَّةَ لاتينِيَّة، والمُرَبَّعَينِ بِقيمَتِهِما المَكتوبَة لا بِحُضورِهِما.</summary>
    [Fact]
    public void The_form_is_read_by_its_field_names_trimmed_with_latin_digits_and_explicit_checkboxes()
    {
        var read = VendorRegistrationForm.Read(new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            [VendorRegistrationForm.FirstNameField] = "  سارَة ",
            [VendorRegistrationForm.LastNameField] = "البائِعَة",
            [VendorRegistrationForm.EmailField] = " v@example.com ",
            [VendorRegistrationForm.MobileField] = "+٩٦٦٥٠٠٠٠٠٠٠٠",
            [VendorRegistrationForm.CityField] = "Riyadh",
            [VendorRegistrationForm.BillingAddressField] = "عُنوان",
            [VendorRegistrationForm.IsVatField] = VendorRegistrationForm.Checked,
            [VendorRegistrationForm.CrNumberField] = "١٠١٠١٠١٠١٠",
            [VendorRegistrationForm.ConsentField] = "on",
        }));
        Assert.Equal(("سارَة", "v@example.com", "+966500000000", "966500000000"), (read.FirstName, read.Email, read.Mobile, read.MobileDigits));
        Assert.True(read.IsVatRegistered);
        Assert.Equal("1010101010", read.CrNumber);
        Assert.False(read.Consent);   // «on» لَيسَت القيمَةَ المَكتوبَة — لا مُوافَقَةَ بِافتِراضِ المُتَصَفِّح
        Assert.Equal("", read.StreetName);
        Assert.DoesNotContain("سارَة", read.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>جِسمُ «Register» بِأَسماءِ جَدوَلِ الوَثيقَة</b>: الحُقولُ الثَمانِيَةُ لِغَيرِ المُسَجَّلِ في الضَريبَةِ وَحدَها — ولا حَقلَ ضَريبَةٍ ولَو مُلِئَ —
    /// والسَبعَةُ مَعَها لِلمُسَجَّل؛ والبَلَدُ <c>SA</c>، والبَريدُ مُطَبَّعاً بِدالَّةِ المِنَصَّة، والجَوّالُ أَرقاماً. والرَمزُ السوقِيُّ يُبادَلُ بِرَمزِ وُصولٍ
    /// ثُمَّ <c>Bearer</c>.</para>
    /// </summary>
    [Fact]
    public async Task The_register_body_carries_the_documented_fields_and_the_vat_ones_only_with_the_vat_mark()
    {
        var handler = new OtoScriptedHandler()
            .ThenToken("ACCESS-MKT")
            .Then(HttpStatusCode.OK, OtoRouteHandler.RegisterAnswer)
            .Then(HttpStatusCode.OK, OtoRouteHandler.RegisterAnswer);
        var client = new OtoClient(new HttpClient(handler), Options.Create(Opts()), new OtoTokenCache(), NullLogger<OtoClient>.Instance);

        var plain = await client.RegisterVendorAsync(Marketplace, (Form() with { CrNumber = "999" }).ToOto());
        var vat = await client.RegisterVendorAsync(Marketplace, Form(vat: true).ToOto());

        Assert.Equal(new OtoRegisteredVendor("_refreshToken_", "https://login.tryoto.com/XYZ"), plain.Value);
        Assert.True(vat.Ok);
        Assert.Contains(Marketplace, handler.Bodies[0]!, StringComparison.Ordinal);
        Assert.Equal(OtoEnvironment.StagingBaseUrl + OtoClient.RegisterPath, handler.Requests[1].RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.Equal("Bearer ACCESS-MKT", handler.Requests[1].Headers.Authorization!.ToString());

        var body = JsonDocument.Parse(handler.Bodies[1]!).RootElement;
        Assert.Equal(new[] { "country", "city", "email", "mobileNumber", "firstName", "lastName", "isVatRegistered", "billingAddress" },
            body.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(("SA", "vendor.test@example.com", "966500000000", false),
            (body.GetProperty("country").GetString(), body.GetProperty("email").GetString(),
             body.GetProperty("mobileNumber").GetString(), body.GetProperty("isVatRegistered").GetBoolean()));

        var vatBody = JsonDocument.Parse(handler.Bodies[2]!).RootElement;
        Assert.Equal(new[] { "country", "city", "email", "mobileNumber", "firstName", "lastName", "isVatRegistered", "billingAddress",
                             "companyName", "vatNumber", "CRNumber", "district", "zipCode", "buildingNo", "streetName" },
            vatBody.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("1010101010", vatBody.GetProperty("CRNumber").GetString());
        Assert.Equal("RegisterVendorAsync", nameof(OtoClient.RegisterVendorAsync));
        Assert.Equal("/rest/v2/register", OtoClient.RegisterPath);
    }

    /// <summary>رَدٌّ بِلا رَمزٍ أَو بِرابِطٍ لَيسَ <c>https</c> مُطلَقاً لا يُقرَأ — ورَفضٌ بِشَكلِ الوَثيقَة <c>oto_refused</c>، ولا شَيءَ بِلا بيئَة. ولا تَصِلُ
    /// بَياناتُ البائِعِ ولا الرَمزُ السوقِيُّ سَطرَ لوغ.</summary>
    [Fact]
    public async Task A_register_answer_without_a_token_or_an_https_link_is_unreadable_and_nothing_personal_is_logged()
    {
        var log = new OtoCapturingLogger();
        var handler = new OtoScriptedHandler()
            .ThenToken()
            .Then(HttpStatusCode.OK, "{\"success\": true, \"activationLink\": \"https://login.tryoto.com/XYZ\"}")
            .Then(HttpStatusCode.OK, "{\"success\": true, \"activationLink\": \"http://login.tryoto.com/XYZ\", \"refreshToken\": \"RT-secret-77\"}")
            .Then(HttpStatusCode.OK, "{\"success\": true, \"refreshToken\": \"RT-secret-77\"}")
            .Then(HttpStatusCode.BadRequest, "{\"success\": false, \"otoErrorCode\": \"OTO1001\"}");
        var client = new OtoClient(new HttpClient(handler), Options.Create(Opts()), new OtoTokenCache(), log);

        for (var i = 0; i < 3; i++)
            Assert.Equal(OtoFailures.Unreadable, (await client.RegisterVendorAsync(Marketplace, Form().ToOto())).Failure!.Code);
        Assert.Equal(OtoFailures.Refused, (await client.RegisterVendorAsync(Marketplace, Form().ToOto())).Failure!.Code);
        var closed = new OtoClient(new HttpClient(handler), Options.Create(Opts(environment: "")), new OtoTokenCache(), log);
        Assert.Equal(OtoFailures.NotConfigured, (await closed.RegisterVendorAsync(Marketplace, Form().ToOto())).Failure!.Code);

        Assert.True(log.Lines.Count >= 4, $"أَداةٌ عَمياء: {log.Lines.Count} سَطرَ لوغ.");
        foreach (var line in log.Lines)
            foreach (var secret in new[] { Marketplace, "سارَة", "vendor.test@example.com", "966500000000", "RT-secret-77", "login.tryoto.com" })
                Assert.DoesNotContain(secret, line, StringComparison.Ordinal);
        Assert.DoesNotContain("_refreshToken_", new OtoRegisteredVendor("_refreshToken_", "https://x").ToString(), StringComparison.Ordinal);
    }

    // ─── الخِدمَة: التَسجيلُ عَبرَ الخِزانَةِ الحَقيقِيَّةِ ومُعالِجٍ يُعيدُ رُدودَ الوَثيقَة ───────────────────────────

    private static OtoOptions Registering() => Opts(marketplaceToken: Marketplace);

    /// <summary>
    /// <para><b>بائِعٌ يُسَجَّلُ بِجِهَةِ تَواصُلٍ تَحَقَّقنا مِنها</b> (مُراجَعَةُ تَيّارِ S8، الجولة 2، ‏2026-10-12: تَوقيعُ <c>RegisterAsync</c> يَطلُبُ السِجِلَّ لا
    /// البائِع) — بِالبَوّابَةِ نَفسِها لا بِمُنشِئٍ داخِلِيّ. وافتِراضُه جَوّالٌ دَخَلَ بِه هُوَ جَوّالُ <see cref="Form"/> أَرقاماً (‏966500000000)، والبَريدُ
    /// يَكتُبُه — فَما تُرسِلُه الصُفوفُ القائِمَةُ إلى OTO لا يَتَغَيَّرُ بِحَرف.</para>
    /// </summary>
    internal static VendorRegistrant Registrant(Guid? id = null, string? email = null, string? phone = "966500000000")
        => VendorContactGate.Admit(Vendor(id), Account(id, email, phone)).Registrant!;

    /// <summary>وَثيقَةُ مُستَخدِمٍ كَما يَكتُبُها الدُخول: البَريدُ أَو الجَوّالُ المُعطى مُتَحَقَّقٌ مِنه، والغائِبُ فارِغٌ غَيرُ مُتَحَقَّق.</summary>
    internal static ACommerce.Kit.Auth.User Account(Guid? id = null, string? email = null, string? phone = null) => new()
    {
        Id = id ?? Seller, TenantSlug = Slug,
        Email = email ?? "", EmailVerified = email is not null,
        Phone = phone ?? "", PhoneVerified = phone is not null,
    };

    /// <summary>
    /// <para><b>المُوجَب</b>: سَطرُ الأَثَرِ مَكتوبٌ <b>قَبلَ أَوَّلِ نِداءٍ إلى OTO</b> (يُقاسُ عِندَ النِداءِ نَفسِه)؛ و«Register» بِالرَمزِ السوقِيّ؛ ورَمزُ الحِسابِ
    /// الجَديدُ مَختوماً في <c>delivery:{listing_owner}</c> بِسِياقِه؛ والوارِدُ مُسَجَّلاً بِرَمزِ البائِعِ الجَديدِ لا السوقِيّ؛ ورابِطُ التَفعيلِ
    /// <b>مَرَّةً واحِدَة</b> لِلبائِعِ نَفسِه.</para>
    /// </summary>
    [Fact]
    public async Task Registering_writes_the_audit_line_before_calling_OTO_seals_the_new_token_and_shows_the_link_once()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        int? auditsAtFirstCall = null;
        w.Oto.Before = _ => auditsAtFirstCall ??= w.Docs.Wrote<ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditEntry>();
        var reveal = NewReveal();
        var surface = Surface(w, Registering(), reveal: reveal);
        Assert.Equal(VendorShippingInput.Register, surface.Input);

        var res = await surface.RegisterAsync(Slug, Registrant(), Form(), Inbound, ip: "203.0.113.7");

        Assert.True(res.Ok, res.Code);
        Assert.Equal(1, auditsAtFirstCall);
        var audit = Assert.Single(w.Docs.Stored.OfType<ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditEntry>());
        Assert.Equal((VendorShippingService.RegisterAuditAction, "register", Seller, "203.0.113.7"),
            (audit.Action, audit.Note, audit.ActorId, audit.Ip));
        Assert.DoesNotContain("vendor.test@example.com", audit.Note + audit.Before + audit.After, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(new[] { OtoClient.RefreshTokenPath, OtoClient.RegisterPath, OtoClient.RefreshTokenPath, OtoClient.WebhookPath },
            w.Oto.Calls.Select(c => c.Path).ToArray());
        Assert.Contains(Marketplace, w.Oto.Calls[0].Body!, StringComparison.Ordinal);
        Assert.Contains("_refreshToken_", w.Oto.Calls[2].Body!, StringComparison.Ordinal);

        var entry = Entry(w)!;
        Assert.Equal("_refreshToken_", entry.Values[VendorShippingSurface.RefreshField!].Unseal(w.Vault, RefreshAad()));
        Assert.Equal(("59", (string?)null), (entry.WebhookId, entry.WebhookRefusal));
        Assert.All(entry.Values.Values, v => Assert.Null(v.Plain));

        Assert.Null(surface.TakeActivationLink(Slug, Stranger));
        Assert.Equal("https://login.tryoto.com/XYZ", surface.TakeActivationLink(Slug, Seller));
        Assert.Null(surface.TakeActivationLink(Slug, Seller));
    }

    /// <summary><b>والسالِبُ: فَشَلُ OTO لا يَحفَظُ شَيئاً</b> — رَمزُه يَعود، ولا مَدخَلَ ولا رابِطَ ولا نِداءَ وارِد؛ وسَطرُ الأَثَرِ قائِم: المُحاوَلَةُ
    /// نَفسُها فِعلٌ يُسَجَّل.</summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "{\"success\": false, \"otoErrorCode\": \"OTO1001\"}", OtoFailures.Refused)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "<html/>", OtoFailures.Unreachable)]
    [InlineData(HttpStatusCode.OK, "{\"success\": true}", OtoFailures.Unreadable)]
    public async Task A_register_OTO_does_not_accept_stores_nothing_and_answers_its_code(HttpStatusCode status, string answer, string code)
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        w.Oto.Routes[OtoClient.RegisterPath] = _ => OtoRouteHandler.Json(status, answer);
        var surface = Surface(w, Registering());

        var res = await surface.RegisterAsync(Slug, Registrant(), Form(), Inbound);

        Assert.Equal(code, res.Code);
        Assert.Null(Entry(w));
        Assert.Equal(0, w.Docs.Wrote<TenantProviderBinding>());
        Assert.Equal(1, w.Docs.Wrote<ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditEntry>());
        Assert.Equal(0, w.Oto.CallsTo(OtoClient.WebhookPath));
        Assert.Null(surface.TakeActivationLink(Slug, Seller));
        // (‏2026-10-13، G5 (iv)): بِلا `FromVendorEntry` — الناقِلُ احتِياطُ التَهيئَة: رَمزُها ولا سِرّ.
        var fallback = (await w.Shipping.CarrierForAsync(Slug, (await w.Deals.LoadAsync(Slug, w.Id))!))!;
        Assert.Equal((DealShipTests.ConfigToken, (string?)null), (fallback.RefreshToken, fallback.WebhookSecret));
    }

    /// <summary>استِمارَةٌ لا تُدَقَّقُ تُرَدُّ بِرَمزِها <b>قَبلَ الأَثَرِ وقَبلَ OTO</b> — لا سَطرَ ولا نِداءَ ولا مَدخَل.</summary>
    [Fact]
    public async Task An_invalid_form_is_refused_by_its_code_before_the_audit_and_before_OTO()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var stored = w.Docs.Stored.Count;
        var res = await Surface(w, Registering()).RegisterAsync(Slug, Registrant(), Form(vat: true) with { CrNumber = "" }, Inbound);

        Assert.Equal(VendorRegistrationValidator.CrNumberInvalid, res.Code);
        Assert.Empty(w.Oto.Calls);
        Assert.Equal(stored, w.Docs.Stored.Count);
    }

    /// <summary><b><c>vendor_register_closed</c> مُوجَباً وسالِباً</b>: بِلا رَمزٍ سوقِيٍّ (اللَصقُ هُوَ المَدخَل) أَو بِلا بيئَة — لا أَثَرَ ولا نِداءَ ولا حِفظ؛
    /// وبِالرَمزِ السوقِيِّ يَمضي (الصُفوفُ أَعلاه). وبِلا خِزانَةٍ رَمزُها.</summary>
    [Theory]
    [InlineData(OtoEnvironment.Staging, "", true, VendorShippingService.RegisterClosed)]
    [InlineData(OtoEnvironment.Production, Marketplace, true, VendorShippingService.RegisterClosed)]
    [InlineData(OtoEnvironment.Staging, Marketplace, false, VendorShippingService.VaultUnavailable)]
    public async Task Vendor_register_closed_and_vault_unavailable_positive(string environment, string marketplaceToken, bool vault, string code)
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var stored = w.Docs.Stored.Count;
        var res = await Surface(w, Opts(environment, marketplaceToken), vault ? null : ProviderVault.FromKek(null))
            .RegisterAsync(Slug, Registrant(), Form(), Inbound);

        Assert.Equal(code, res.Code);
        Assert.Empty(w.Oto.Calls);
        Assert.Equal(stored, w.Docs.Stored.Count);
    }

    /// <summary>
    /// <para><b>مَن لَه مَدخَلٌ لا يُسَجَّلُ لَه حِسابٌ ثانٍ</b>: النِداءُ نَفسُه يُعيدُ تَسجيلَ وارِدِه بِرَمزِه المَختوم — <c>PUT</c> بِمُعَرِّفِه —
    /// بَعدَ سَطرِ أَثَرٍ <c>inbound</c>، ولا «Register» ولا استِمارَةٌ تُسأَل. ومَدخَلٌ لا يُفَكُّ يُرَدُّ <c>vendor_entry_unreadable</c> بِلا نِداء.</para>
    /// </summary>
    [Fact]
    public async Task A_vendor_with_an_entry_is_not_registered_twice_and_only_their_inbound_is_registered_again()
    {
        var w = await DealShipTests.ConfirmedCashDeal();
        int? auditsAtFirstCall = null;
        w.Oto.Before = _ => auditsAtFirstCall ??= w.Docs.Wrote<ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditEntry>();
        var surface = Surface(w, Registering());

        var res = await surface.RegisterAsync(Slug, Registrant(), Form() with { Consent = false, Email = "" }, Inbound);

        Assert.True(res.Ok, res.Code);
        Assert.Equal(1, auditsAtFirstCall);
        Assert.Equal("inbound", Assert.Single(w.Docs.Stored.OfType<ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditEntry>()).Note);
        Assert.Equal(0, w.Oto.CallsTo(OtoClient.RegisterPath));
        var (method, body) = Assert.Single(WebhookCalls(w.Oto));
        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal("59", body.GetProperty("id").GetString());
        Assert.Contains(DealShipTests.VendorToken, w.Oto.Calls[0].Body!, StringComparison.Ordinal);
        Assert.Equal(body.GetProperty("secretKey").GetString(),
            Entry(w)!.Values[VendorShippingSurface.WebhookField!].Unseal(w.Vault, WebhookAad()));
        Assert.Null(surface.TakeActivationLink(Slug, Seller));

        // ومَدخَلٌ لا يُفَكّ — مَختومٌ بِمِفتاحٍ آخَر.
        var other = await DealShipTests.ConfirmedCashDeal();
        other.Docs.Put(DealShipTests.VendorEntry(DealShipTests.NewVault(), Slug, Seller));
        var unreadable = await Surface(other, Registering()).RegisterAsync(Slug, Registrant(), Form(), Inbound);
        Assert.Equal(VendorShippingService.EntryUnreadable, unreadable.Code);
        Assert.Empty(other.Oto.Calls);
    }

    // ─── جِهَةُ التَواصُلِ المُتَحَقَّقُ مِنها وحَدُّ المُحاوَلات — مُراجَعَةُ تَيّارِ S8، الجولة 2 (‏2026-10-12؛ ‏ADR-033 §٧‑ف) ───
    //
    // الخُطوَة 8 تُسقِطُ حارِسَ المُديرِ لِأَنّ «بَياناتُ التَسجيلِ بَياناتُ البائِعِ فَالفِعلُ فِعلُه» — وكانَ الكودُ يَقولُها ولا يُلزِمُها (تَوصيفُه `dcfebfe`:
    // أَربَعَةُ صُفوفِ «اليَوم» قُلِبَت هُنا بِأَسمائِها). صارَ: بَوّابَةٌ نَقِيَّةٌ تَبني مِن وَثيقَةِ صاحِبِ الجَلسَةِ سِجِلّاً يَطلُبُه تَوقيعُ الخِدمَة، فَيُرسَلُ في كُلِّ
    // خانَةٍ ما تَحَقَّقنا مِنه لا ما كُتِب؛ وحَدُّ مُحاوَلاتٍ مِن حِصَّةِ الإرسالِ القائِمَة (عَشرٌ في السّاعَة) بَعدَ التَدقيقِ وقَبلَ الأَثَر. والصُفوفُ الجَديدَةُ
    // بِبائِعينَ جُدُدٍ (`Guid.NewGuid()`): الحِصَّةُ عَدّادٌ واحِدٌ في العَمَلِيَّة، فَلا يَأكُلُ صَفٌّ حَدَّ صَفّ.

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> — كانَ <c>Today_the_register_sends_the_typed_email_and_mobile_whoever_they_belong_to</c> (‏<c>dcfebfe</c>): يُرسَلُ ما كُتِبَ
    /// ولِلبائِعِ بَريدٌ مُتَحَقَّقٌ مِنه. <b>والآن</b>: البَريدُ المُتَحَقَّقُ مِنه يُرسَلُ في خانَتِه ولَو كُتِبَ غَيرُه، والجَوّالُ — غَيرُ المُتَحَقَّقِ مِنه — كَما
    /// كُتِب؛ وبِالعَكسِ لِمَن دَخَلَ بِجَوّالِه.</para>
    /// </summary>
    [Fact]
    public async Task The_register_sends_the_vendors_verified_contact_in_place_of_a_typed_one()
    {
        var typed = Form() with { Email = "someone.else@example.com", Mobile = "966511111111" };

        var byEmail = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        Assert.True((await Surface(byEmail, Registering())
            .RegisterAsync(Slug, Registrant(Guid.NewGuid(), email: "Seller.Own@Example.com", phone: null), typed, Inbound)).Ok);
        var byPhone = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        Assert.True((await Surface(byPhone, Registering())
            .RegisterAsync(Slug, Registrant(Guid.NewGuid(), phone: "966533333333"), typed, Inbound)).Ok);

        static (string?, string?) Sent(DealShipTests.World w)
        {
            var body = JsonDocument.Parse(w.Oto.Calls.Single(c => c.Path == OtoClient.RegisterPath).Body!).RootElement;
            return (body.GetProperty("email").GetString(), body.GetProperty("mobileNumber").GetString());
        }
        Assert.Equal(("seller.own@example.com", "966511111111"), Sent(byEmail));
        Assert.Equal(("someone.else@example.com", "966533333333"), Sent(byPhone));
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> — كانَ <c>Today_failed_registers_have_no_bound_and_each_reaches_OTO_with_the_marketplace_token</c> (‏<c>dcfebfe</c>): إحدى عَشرَةَ
    /// مُحاوَلَةً تَبلُغُ OTO. <b>والآن — <c>vendor_register_rate_limited</c> مُوجَباً</b>: العَشرُ الأولى تَبلُغُها (حِصَّةُ الإرسالِ القائِمَة)، والحادِيَةَ عَشرَةَ
    /// تُرَدُّ بِرَمزِها <b>قَبلَ الأَثَرِ وقَبلَ OTO</b> — عَشرَةُ نِداءاتٍ وعَشرَةُ أَسطُرِ أَثَر، ولا حِفظ.</para>
    /// </summary>
    [Fact]
    public async Task Registers_beyond_the_send_quota_answer_rate_limited_before_the_audit_and_OTO()
    {
        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        w.Oto.Routes[OtoClient.RegisterPath] = _ => OtoRouteHandler.Json(HttpStatusCode.BadRequest, "{\"success\": false, \"otoErrorCode\": \"OTO1001\"}");
        var surface = Surface(w, Registering());
        var registrant = Registrant(Guid.NewGuid());

        var codes = new List<string?>();
        for (var i = 0; i < 11; i++)
            codes.Add((await surface.RegisterAsync(Slug, registrant, Form(), Inbound)).Code);

        output.WriteLine(string.Join(" · ", codes));
        Assert.Equal(Enumerable.Repeat<string?>(OtoFailures.Refused, 10).Append(VendorShippingService.RegisterRateLimited), codes);
        Assert.Equal(10, w.Oto.CallsTo(OtoClient.RegisterPath));
        Assert.Equal(10, w.Docs.Wrote<ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditEntry>());
        Assert.Equal(0, w.Docs.Wrote<TenantProviderBinding>());
    }

    /// <summary><b><c>vendor_register_rate_limited</c> سالِباً</b>: الحَدُّ لِبائِعٍ في مَتجَر بِمِفتاحِ الحِصَّةِ «tenant|subject» — <b>بائِعٌ آخَرُ ومَتجَرٌ آخَرُ لا
    /// يَرِثانِه</b>؛ واستِمارَةٌ تُرَدُّ لا تَستَهلِكُ مُحاوَلَة (إحدى عَشرَةَ مَردودَةً ثُمَّ الصالِحَةُ تَمضي)؛ ومَن لَه مَدخَلٌ يُعيدُ تَسجيلَ وارِدِه بِرَمزِه هُوَ فَلا
    /// يَمَسُّه الحَدّ.</summary>
    [Fact]
    public async Task Vendor_register_rate_limited_negative_another_vendor_another_store_a_refused_form_and_a_kept_entry_are_not_bounded()
    {
        var owner = Guid.NewGuid();
        var key = VendorShippingService.RegisterAttemptKey(Slug, owner);
        Assert.Equal($"{Slug}|oto-register:{owner:D}", key);
        // (‏2026-10-13، G5 M1): المُحاوَلاتُ السابِقَةُ تُستَهلَكُ مِن عَدّادِ الأَفعال — عَدّادِ التَسجيلِ مُنذُ اليَوم؛ وكانَت مِن حِصَّةِ الإرسال.
        for (var i = 0; i < 10; i++) Assert.True(ACommerce.Kit.Auth.Server.AuthHandlers.TryConsumeActionQuota(key));

        var w = await DealShipTests.ConfirmedCashDeal(vendorEntry: false);
        var surface = Surface(w, Registering());
        Assert.Equal(VendorShippingService.RegisterRateLimited, (await surface.RegisterAsync(Slug, Registrant(owner), Form(), Inbound)).Code);
        Assert.Empty(w.Oto.Calls);

        Assert.True((await surface.RegisterAsync(Slug, Registrant(Guid.NewGuid()), Form(), Inbound)).Ok);        // بائِعٌ آخَرُ في المَتجَر
        Assert.True((await surface.RegisterAsync("owner-test", Registrant(owner), Form(), Inbound)).Ok);          // البائِعُ نَفسُه في مَتجَرٍ آخَر

        var fresh = Guid.NewGuid();
        for (var i = 0; i < 11; i++)
            Assert.Equal(VendorRegistrationValidator.ConsentMissing,
                (await surface.RegisterAsync(Slug, Registrant(fresh), Form() with { Consent = false }, Inbound)).Code);
        Assert.True((await surface.RegisterAsync(Slug, Registrant(fresh), Form(), Inbound)).Ok);                // المَردودَةُ لَم تَستَهلِك

        w.Docs.Put(DealShipTests.VendorEntry(w.Vault, Slug, owner));                                             // مَدخَلُه في هذا المَتجَر
        var inbound = await surface.RegisterAsync(Slug, Registrant(owner), Form(), Inbound);
        Assert.True(inbound.Ok, inbound.Code);
        Assert.Equal("inbound", w.Docs.Stored.OfType<ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditEntry>().Last().Note);
    }

    /// <summary>
    /// <para><b><c>vendor_contact_unverified</c> مُوجَباً وسالِباً</b> — مُوجَبُه: لا وَثيقَة، أَو وَثيقَةُ غَيرِه، أَو مَحذوفَة، أَو بَريدٌ وجَوّالٌ غَيرُ مُتَحَقَّقٍ
    /// مِنهُما، أَو مَن دَخَلَ بِنَفاذ (<c>NID-…</c> بِلا <c>PhoneVerified</c> — كَما يَكتُبُه <c>AuthHandlers</c>)، أَو «مُتَحَقَّقٌ» لَيسَ بَريداً ولا أَرقاماً.
    /// وسالِبُه: بَريدٌ مُتَحَقَّقٌ وَحدَه، أَو جَوّالٌ وَحدَه، أَو كِلاهُما — والسِجِلُّ يَحمِلُ ما تَحَقَّقنا مِنه وَحدَه، والبَريدُ مُطَبَّعاً بِدالَّةِ الدُخولِ نَفسِها،
    /// والجَوّالُ أَرقاماً بِلا «+» ولا مَسافات؛ ولا يُطبَعُ شَيءٌ مِنهُما.</para>
    /// </summary>
    [Fact]
    public void Vendor_contact_unverified_positive_and_negative()
    {
        var vendor = Vendor();
        var deleted = Account(email: "seller@example.com");
        deleted.DeletedAt = DealShipTests.Now.UtcDateTime;
        var refused = new ACommerce.Kit.Auth.User?[]
        {
            null,
            Account(Stranger, email: "seller@example.com"),
            deleted,
            new() { Id = Seller, Email = "seller@example.com", Phone = "966500000000" },
            new() { Id = Seller, Phone = "NID-1010101010", NationalId = "1010101010" },
            new() { Id = Seller, Email = "not-an-email", EmailVerified = true, Phone = "NID-1010101010", PhoneVerified = true },
        };
        foreach (var account in refused)
        {
            var admission = VendorContactGate.Admit(vendor, account);
            Assert.Null(admission.Registrant);
            Assert.Equal(VendorContactGate.ContactUnverified, admission.Violation!.Code);
            Assert.False(VendorContactGate.IsAllowed(vendor, account));
        }

        var byEmail = VendorContactGate.Admit(vendor, Account(email: " Seller@Example.com ")).Registrant!;
        var byPhone = VendorContactGate.Admit(vendor, Account(phone: "+966 500000000")).Registrant!;
        var both = VendorContactGate.Admit(vendor, Account(email: "seller@example.com", phone: "966500000000")).Registrant!;
        Assert.Equal(("seller@example.com", (string?)null), (byEmail.VerifiedEmail, byEmail.VerifiedMobile));
        Assert.Equal(((string?)null, "966500000000"), (byPhone.VerifiedEmail, byPhone.VerifiedMobile));
        Assert.Equal(("seller@example.com", "966500000000"), (both.VerifiedEmail, both.VerifiedMobile));
        Assert.Same(vendor, both.Vendor);
        Assert.Null(VendorContactGate.Admit(vendor, Account(email: "seller@example.com")).Violation);
        Assert.Equal(new[] { VendorContactGate.ContactUnverified }, VendorContactGate.All);
        Assert.DoesNotContain("seller@example.com", both.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("966500000000", both.ToString(), StringComparison.Ordinal);
    }

    /// <summary><b>الرَبطُ دالَّةٌ نَقِيَّة</b>: كُلُّ جِهَةٍ مُتَحَقَّقٍ مِنها تَحِلُّ مَحَلَّ ما في خانَتِها والأُخرى كَما كُتِبَت، وسائِرُ الحُقولِ بِحَرفِها — فَالمُدَقِّقُ يَرى
    /// ما يُرسَل: خانَةٌ مُقفَلَةٌ جاءَت فارِغَةً لا تُنتِجُ رَمزاً، وخانَةٌ مَفتوحَةٌ فارِغَةٌ تُنتِجُ رَمزَها.</summary>
    [Fact]
    public void The_registrant_binds_each_verified_channel_and_leaves_the_other_as_typed()
    {
        var typed = Form() with { Email = "", Mobile = "" };

        var byEmail = Registrant(email: "seller@example.com", phone: null).Bind(typed);
        var byPhone = Registrant().Bind(typed);
        var both = Registrant(email: "seller@example.com").Bind(typed);

        Assert.Equal(typed with { Email = "seller@example.com" }, byEmail);
        Assert.Equal(new[] { VendorRegistrationValidator.MobileInvalid }, Codes(byEmail));
        Assert.Equal(typed with { Mobile = "966500000000" }, byPhone);
        Assert.Equal(new[] { VendorRegistrationValidator.EmailInvalid }, Codes(byPhone));
        Assert.Equal(typed with { Email = "seller@example.com", Mobile = "966500000000" }, both);
        Assert.Empty(Codes(both));
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> — كانَ <c>Today_the_register_endpoint_asks_the_vendors_account_nothing_before_the_form</c> (‏<c>dcfebfe</c>). <b>والآن</b>: الجَلسَةُ ثُمَّ
    /// <c>ListingVendorGate.Admit</c> ثُمَّ وَثيقَةُ صاحِبِ الجَلسَةِ وبَوّابَةُ جِهَةِ التَواصُل — <b>قَبلَ قِراءَةِ الاستِمارَة</b> (القاعِدَة ٦) — ثُمَّ الخِدمَةُ
    /// بِالسِجِلِّ لا بِالبائِع، ورَدُّ البَوّابَةِ بِرَمزِها إلى «إعلاناتي».</para>
    /// </summary>
    [Fact]
    public void The_register_endpoint_admits_the_verified_contact_after_the_vendor_and_before_the_form()
    {
        var body = RegisterEndpointBody();
        var vendor = body.IndexOf("Services.Shipping.ListingVendorGate.Admit(", StringComparison.Ordinal);
        var account = body.IndexOf("accounts.ProfileAsync(slug, userId)", StringComparison.Ordinal);
        var contact = body.IndexOf("Services.Shipping.VendorContactGate.Admit(vendor,", StringComparison.Ordinal);
        var form = body.IndexOf("VendorRegistrationForm.Read(req.Form)", StringComparison.Ordinal);
        var call = body.IndexOf("vendors.RegisterAsync(slug, registrant,", StringComparison.Ordinal);
        output.WriteLine($"بائِع={vendor} · وَثيقَة={account} · جِهَة={contact} · استِمارَة={form} · نِداء={call}");
        Assert.True(vendor > 0 && account > vendor && contact > vendor && form > account && form > contact && call > contact,
            $"التَرتيبُ مَكسور: {vendor}/{account}/{contact}/{form}/{call}");
        Assert.Contains("shipping_err={contact.Violation!.Code}", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> — كانَ <c>Today_the_register_form_leaves_email_and_mobile_for_anyone_to_type</c> (‏<c>dcfebfe</c>). <b>والآن</b>: كُلٌّ مِن الخانَتَينِ
    /// تُرسَمُ بِقيمَةِ السِجِلِّ مُقفَلَةً حَيثُ تَحَقَّقنا مِنها (<c>value</c> و<c>readonly</c> مِنه، و<c>null</c> لا يُرسَمُ فَتَبقى مَفتوحَة) — والنُقطَةُ لا تَقرَأُ
    /// المُقفَلَةَ أَصلاً (<c>VendorRegistrant.Bind</c>).</para>
    /// </summary>
    [Fact]
    public void The_register_form_draws_each_verified_channel_prefilled_and_locked()
    {
        foreach (var (field, held) in new[] { ("EmailField", "VerifiedEmail"), ("MobileField", "VerifiedMobile") })
        {
            var input = RegisterInput(field);
            output.WriteLine(input);
            Assert.Contains($"value=\"@verified.{held}\"", input, StringComparison.Ordinal);
            Assert.Contains($"readonly=\"@(verified.{held} is not null)\"", input, StringComparison.Ordinal);
        }
    }

    /// <summary><b>بائِعٌ تَرُدُّه بَوّابَةُ جِهَةِ التَواصُلِ لا تُرسَمُ لَه استِمارَةٌ ولا زِرُّ إعادَة</b> (القاعِدَة ١٢ — لا بابَ يُرَدّ): الصَفحَةُ تَسأَلُ البَوّابَةَ نَفسَها
    /// بِوَثيقَتِه؛ نَموذَجُ الإعادَةِ داخِلَ <c>@if (registrant is not null)</c>، واستِمارَةُ التَسجيلِ داخِلَ <c>else if (registrant is { } verified)</c>، والقِسمُ كُلُّه
    /// لا يُرسَمُ بِلا رابِطٍ ولا مَدخَلٍ ولا سِجِلّ.</summary>
    [Fact]
    public void A_vendor_the_contact_gate_refuses_is_drawn_neither_the_register_form_nor_its_retry()
    {
        var page = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(
            RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "MyListings.razor"))));
        const string Enum = "ACommerce.Templates.Customer.Marketplace.Services.Shipping.VendorShippingInput.";
        var branch = Block(page, page.IndexOf("else if (shippingInput == " + Enum + "Register)", StringComparison.Ordinal));
        var action = Regex.Escape("action=\"@($\"/{Slug}/me/shipping/oto/register\")\"");

        const string SectionGuard = "@if (activationLink is not null || shippingView is not null || registrant is not null)";
        const string RetryGuard = "@if (registrant is not null)";
        const string FormGuard = "else if (registrant is { } verified)";
        var section = Block(branch, branch.IndexOf(SectionGuard, StringComparison.Ordinal) + SectionGuard.Length);
        var retry = Block(branch, branch.IndexOf(RetryGuard, StringComparison.Ordinal) + RetryGuard.Length);
        var form = Block(branch, branch.IndexOf(FormGuard, StringComparison.Ordinal) + FormGuard.Length);

        output.WriteLine($"قِسم={section.Length} · إعادَة={retry.Length} · استِمارَة={form.Length}");
        Assert.Contains("<section id=\"vendor-shipping\"", section, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(retry, action));
        Assert.Single(Regex.Matches(form, action));
        Assert.Equal(2, Regex.Matches(section, action).Count);
        Assert.Contains("registrant = Vs.VendorContactGate.Admit(", page, StringComparison.Ordinal);
        Assert.Contains("await Accounts.ProfileAsync(Slug, Auth.UserId!.Value)", page, StringComparison.Ordinal);
    }

    /// <summary>جِسمُ نُقطَةِ التَسجيلِ نَصّاً — مِن مَسارِها الحَرفِيِّ إلى سِلسِلَتِها، بِلا تَعليقات.</summary>
    private static string RegisterEndpointBody()
    {
        var code = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Shipping", "VendorShippingEndpoints.cs")));
        var start = code.IndexOf("app.MapPost(\"/{slug}/me/shipping/oto/register\"", StringComparison.Ordinal);
        Assert.True(start > 0, "النُقطَةُ لَيسَت بِمَسارِها الحَرفِيّ — الأَداةُ عَمياء.");
        return code[start..code.IndexOf(".RequireStoreWritable();", start, StringComparison.Ordinal)];
    }

    /// <summary>وَسمُ مُدخَلِ حَقلٍ مِن حُقولِ القارِئِ في «إعلاناتي» — مِن <c>&lt;input</c> إلى <c>/&gt;</c> — بِلا تَعليقات.</summary>
    private static string RegisterInput(string fieldConstant)
    {
        var page = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(
            RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "MyListings.razor"))));
        var at = page.IndexOf($"name=\"@Vs.VendorRegistrationForm.{fieldConstant}\"", StringComparison.Ordinal);
        Assert.True(at > 0, $"لا مُدخَلَ لِـ{fieldConstant} — الأَداةُ عَمياء.");
        Assert.Equal(-1, page.IndexOf($"name=\"@Vs.VendorRegistrationForm.{fieldConstant}\"", at + 1, StringComparison.Ordinal));
        var open = page.LastIndexOf("<input", at, StringComparison.Ordinal);
        return page[open..(page.IndexOf("/>", at, StringComparison.Ordinal) + 2)];
    }

    /// <summary><b>ولا استِهلاكَ لِحِصَّة</b>: لا الخِدمَةُ ولا نُقطَتاها تَمَسّانِ الاستِحقاقات — فاحِصٌ نَصِّيٌّ يَعُدُّ ما قَرَأ.
    /// <para>(‏2026-10-13، مُراجَعَةُ المَوجَة 3b، G5 (xi) — ولا يُعادُ حَرفٌ فَوقَه: <b>ومَساراتُ الشَحنِ الخَمسَة</b> — اللَصقُ والتَسجيلُ والبوليصَةُ والوارِدُ
    /// والعَميل — كانَ يَقيسُها صَفٌّ مُكَرَّرٌ في <c>ProviderCatalogTests</c> (‏d99e393)، فَانتَقَلَت إلى هُنا وبَقِيَ هُناكَ قِراءَةُ الوَصف. <b>وهو قِياسٌ لِحالِ الكودِ
    /// لا قَرار</b>: أَيُعَدُّ شَيءٌ مِن الشَحنِ عَلى باقَةِ المُستَأجِرِ لا تُقَرِّرُه وَثيقَة — سِجِلُّ المالِكِ «ب» S11b·٢٩؛ ويَومَ يُقَرَّرُ يَحمَرُّ هذا بِقَرارٍ مَرئِيّ.)</para></summary>
    [Fact]
    public void The_vendor_shipping_paths_consume_no_quota()
    {
        string Template(params string[] parts) => Path.Combine(new[] { RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace" }.Concat(parts).ToArray());
        var files = new[]
        {
            Path.Combine(RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Shipping", "VendorShipping.cs"),
            Path.Combine(RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Shipping", "VendorShippingEndpoints.cs"),
            Template("Services", "Deals", "DealShipping.cs"),
            Template("Shipping", "OtoShippingEndpoints.cs"),
            Path.Combine(RepoRoot, "libs", "kits", "Delivery", "ACommerce.Kit.Delivery.Core", "OtoClient.cs"),
        };
        var read = 0;
        foreach (var f in files)
        {
            var code = WriteEndpointGuardTests.StripComments(File.ReadAllText(f));
            read += code.Length > 500 ? 1 : 0;
            foreach (var quota in new[] { "ConsumeAsync", "IEntitlements", "RequireEntitlement", "EntitlementFilter", "QuotaConsumed" })
                Assert.DoesNotContain(quota, code, StringComparison.Ordinal);
        }
        output.WriteLine($"مِلَفّاتٌ مَقروءَة: {read} مِن {files.Length}");
        Assert.Equal(5, files.Length);
        Assert.Equal(files.Length, read);
    }

    /// <summary>
    /// <para><b>الاستِمارَةُ في فَرعِ التَسجيلِ بِأَسماءِ القارِئِ نَفسِه</b>: كُلُّ ثابِتِ حَقلٍ في <see cref="VendorRegistrationForm"/> اسمُ مُدخَلٍ في الصَفحَة،
    /// ونَموذَجا الفَرعِ يُرسِلانِ إلى المَسارِ الحَرفِيّ؛ ومُرَبَّعُ المُوافَقَةِ <c>required</c>؛ ولا مُدخَلَ رَمزٍ في الفَرع — المَدخَلانِ لا يَلتَقِيان.</para>
    /// </summary>
    [Fact]
    public void The_register_branch_carries_every_reader_field_by_name_and_posts_to_the_literal_route()
    {
        var page = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(
            RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "MyListings.razor"))));
        const string Enum = "ACommerce.Templates.Customer.Marketplace.Services.Shipping.VendorShippingInput.";
        var at = page.IndexOf("else if (shippingInput == " + Enum + "Register)", StringComparison.Ordinal);
        Assert.True(at > 0, "فَرعُ التَسجيلِ غائِب — الأَداةُ عَمياء.");
        var branch = Block(page, at);

        var fields = typeof(VendorRegistrationForm).GetFields()
            .Where(f => f.IsLiteral && f.Name.EndsWith("Field", StringComparison.Ordinal)).Select(f => f.Name).ToArray();
        output.WriteLine($"حُقولُ القارِئ: {fields.Length} — {string.Join(" · ", fields)}");
        Assert.Equal(15, fields.Length);
        Assert.All(fields, f => Assert.Contains($"name=\"@Vs.VendorRegistrationForm.{f}\"", branch, StringComparison.Ordinal));
        Assert.Equal(2, Regex.Matches(branch, Regex.Escape("action=\"@($\"/{Slug}/me/shipping/oto/register\")\"")).Count);
        Assert.Contains("name=\"@Vs.VendorRegistrationForm.ConsentField\" value=\"@Vs.VendorRegistrationForm.Checked\" required", branch, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh_token", branch, StringComparison.Ordinal);
        Assert.Contains("activationLink = Vendors.TakeActivationLink(Slug, Auth.UserId!.Value);", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>نُقطَةُ التَسجيل</b>: الجَلسَةُ ثُمَّ <c>ListingVendorGate.Admit</c> قَبلَ قِراءَةِ الاستِمارَة؛ <c>RequireStoreWritable</c>؛ ونِداءٌ واحِدٌ
    /// لِلخِدمَةِ بِعُنوانِ الطالِب — والأَثَرُ قَبلَ OTO في الخِدمَةِ لا في الجِسم؛ <b>ولا حارِسَ إدارَةِ مَنَصَّةٍ ولا مَتجَر</b>.</para>
    /// </summary>
    [Fact]
    public void The_register_endpoint_admits_the_listing_owner_before_reading_the_form_and_declares_no_admin_guard()
    {
        var code = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Shipping", "VendorShippingEndpoints.cs")));
        var start = code.IndexOf("app.MapPost(\"/{slug}/me/shipping/oto/register\"", StringComparison.Ordinal);
        Assert.True(start > 0, "النُقطَةُ لَيسَت بِمَسارِها الحَرفِيّ — الأَداةُ عَمياء.");
        var body = code[start..code.IndexOf(".RequireStoreWritable();", start, StringComparison.Ordinal)];

        var session = body.IndexOf("AuthSession.ResolveToken(", StringComparison.Ordinal);
        var admit = body.IndexOf("Services.Shipping.ListingVendorGate.Admit(", StringComparison.Ordinal);
        var form = body.IndexOf("VendorRegistrationForm.Read(req.Form)", StringComparison.Ordinal);
        var call = body.IndexOf("vendors.RegisterAsync(", StringComparison.Ordinal);
        output.WriteLine($"جَلسَة={session} · بَوّابَة={admit} · استِمارَة={form} · نِداء={call}");
        Assert.True(session > 0 && admit > session && form > admit && call > admit, $"التَرتيبُ مَكسور: {session}/{admit}/{form}/{call}");
        foreach (var guard in new[] { "PlatformAdminGuard", "TenantAdminGuard", "IDocumentSession", "audit.WriteAsync(" })
            Assert.DoesNotContain(guard, body, StringComparison.Ordinal);
        Assert.Contains("RemoteIpAddress", body, StringComparison.Ordinal);
    }
}

// ═══ البُرهانُ الحَيّ — Postgres (‏WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres) ═══════════════════════
//
// عالَمُ الوَثائِقِ بِلا إيجارٍ ولا استِعلام، فَما يَحتاجُهُما يُقاسُ هُنا: المَدخَلُ يُكتَبُ ويُقرَأُ بِمِفتاحِه في مُستَأجِرِه وَحدَه، ومُجموعَةُ
// المَتجَرِ المَقروءَةُ مِن Postgres تَتَجاوَزُه، و«أَلِلمَتجَرِ ناقِل؟» — سُؤالُ الوارِدِ قَبلَ قِراءَةِ الجِسم — نَعَم حَيثُ مَدخَلٌ فَعّال
// ولا حَيثُ لا مَدخَل أَو رَبطُ مَتجَرٍ وَحدَه أَو مَدخَلٌ مَسحوب. مُستَأجِراتٌ مُؤَقَّتَةٌ تُمسَحُ بَعدَه.

[Collection(LiveProofCollection.Name)]
public class LiveVendorShippingProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private sealed class ForbiddenPaymentProvider : IPaymentProvider
    {
        private static InvalidOperationException Touched() => new("لا مالَ هُنا — نودِيَ مُزَوِّدُ الدَفع.");
        public string ProviderName => "forbidden";
        public Task<PaymentResult> AuthorizeAsync(PaymentRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> CaptureAsync(string paymentId, decimal? amount = null, CancellationToken ct = default) => throw Touched();
        public Task<PaymentResult> RefundAsync(string paymentId, decimal amount, string reason, CancellationToken ct = default) => throw Touched();
        public Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest req, string idempotencyKey, CancellationToken ct = default) => throw Touched();
        public Task<bool> CancelSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => throw Touched();
        public Task<Invoice?> GetInvoiceAsync(string paymentId, CancellationToken ct = default) => throw Touched();
    }

    [Fact]
    public async Task A_vendor_entry_is_kept_by_its_key_in_its_tenant_and_opens_the_inbound_there_only_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        string Temp(string tag) => "vs-zz-" + tag + "-" + Guid.NewGuid().ToString("N")[..8];
        var (withEntry, empty, storeOnly, revoked) = (Temp("e"), Temp("n"), Temp("s"), Temp("r"));
        var seller = Guid.NewGuid();
        try
        {
            var providers = new TenantProviderService(store);
            var vault = DealShipTests.NewVault();

            // يُكتَبُ بِخِدمَةِ السَطحِ نَفسِها عَلى Postgres، والرَدُّ مِن OTO مُعالِجٌ يُعيدُ رَدَّ الوَثيقَة.
            var oto = new OtoRouteHandler();
            var options = Options.Create(new OtoOptions { Environment = OtoEnvironment.Staging });
            var surface = new VendorShippingService(providers, vault,
                new OtoClient(new HttpClient(oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance), options,
                new ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditWriter(store),
                new ACommerce.Templates.Customer.Marketplace.Services.Api.ApiKeyRevealStore(
                    new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())));
            var vendor = ListingVendorGate.Admit(seller, "بائِع", 1).Vendor!;
            Assert.True((await surface.SaveTokenAsync(withEntry, vendor, "RT-live-vendor-5e5e", "https://wasayel.example/x")).Ok);

            var read = (await providers.VendorEntryAsync(withEntry, ProviderCapabilities.Delivery, seller))!;
            Assert.Equal(TenantProviderBinding.VendorEntryId(ProviderCapabilities.Delivery, seller), read.Id);
            Assert.Equal("RT-live-vendor-5e5e", read.Values[VendorShippingSurface.RefreshField!]
                .Unseal(vault, VendorShippingSurface.AadFor(withEntry, VendorShippingSurface.RefreshField!, seller)));
            Assert.Null(await providers.VendorEntryAsync(empty, ProviderCapabilities.Delivery, seller));   // في مُستَأجِرِه وَحدَه

            await providers.BindProviderAsync(storeOnly, ProviderCapabilities.Payments,
                ProviderCatalog.ForCapability(ProviderCapabilities.Payments).First(d => d.Credential.Fields.Any(f => CredentialKinds.IsLink(f.Kind))).Slug,
                new Dictionary<string, StoredValue>(), "live");
            var entry = DealShipTests.VendorEntry(vault, revoked, seller, status: TenantProviderBinding.StatusRevoked);
            await using (var s = store.LightweightSession(revoked)) { s.Store(entry); await s.SaveChangesAsync(); }

            var set = await TenantProviderService.ReadUncachedAsync(store, withEntry);
            var answers = new Dictionary<string, bool>();
            foreach (var slug in new[] { withEntry, empty, storeOnly, revoked })
                answers[slug] = await providers.HasVendorEntriesAsync(slug, ProviderCapabilities.Delivery);

            var shipping = new DealShipping(new DealsService(store, new ForbiddenPaymentProvider()),
                new OtoClient(new HttpClient(oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance), options,
                null, providers, vault, new FixedClock(DealShipTests.Now));
            var open = new Dictionary<string, bool>();
            foreach (var slug in answers.Keys) open[slug] = await shipping.CarrierReadyForStoreAsync(slug);

            output.WriteLine($"مَداخِل: {string.Join(" · ", answers.Select(a => $"{a.Key[..8]}={a.Value}"))} · " +
                             $"وارِد: {string.Join(" · ", open.Select(a => $"{a.Key[..8]}={a.Value}"))} · " +
                             $"مُجموعَةُ المَتجَرِ ذي المَدخَل: {(ReferenceEquals(set, TenantProviderSet.Platform) ? "Platform" : set.Bound.Count.ToString())}");
            Assert.Same(TenantProviderSet.Platform, set);
            Assert.Equal(new[] { true, false, false, false }, answers.Values.ToArray());
            Assert.Equal(new[] { true, false, false, false }, open.Values.ToArray());
        }
        finally
        {
            foreach (var slug in new[] { withEntry, empty, storeOnly, revoked })
            {
                await using var s = store.LightweightSession(slug);
                foreach (var b in await s.Query<TenantProviderBinding>().ToListAsync()) s.Delete(b);
                await s.SaveChangesAsync();
            }
        }
    }

    /// <summary>
    /// <para><b>شَرطُ الخِزانَةِ في «أَلِلمَتجَرِ ناقِل؟» عَلى Postgres</b> (‏2026-10-12، مُراجَعَةُ تَيّارِ S8، الجولة 2): مَتجَرٌ لَيسَ فيه إلّا مَدخَلُ بائِعٍ فَعّال —
    /// لا رَمزَ تَهيئَةٍ لَه — <b>مَداخِلُه «نَعَم» بِالخِزانَةِ وبِغَيرِها، والوارِدُ مَفتوحٌ بِخِزانَةٍ ومُغلَقٌ بِلاها</b>: مَدخَلٌ لا يُفَكُّ لا يَفتَحُ وارِداً.
    /// وما لا يَراه عالَمُ الوَثائِقِ (جَوابُ الاستِعلام) يُقاسُ هُنا — وكانَ الصَفُّ الحَيُّ أَعلاه بِخِزانَةٍ وَحدَها، فَحَذفُ الشَرطِ يَمُرُّ أَخضَرَ فيه.</para>
    /// </summary>
    [Fact]
    public async Task A_store_with_only_vendor_entries_opens_the_inbound_with_a_vault_and_not_without_one_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "vs-zz-v-" + Guid.NewGuid().ToString("N")[..8];
        var seller = Guid.NewGuid();
        try
        {
            var providers = new TenantProviderService(store);
            var vault = DealShipTests.NewVault();
            var entry = DealShipTests.VendorEntry(vault, slug, seller);
            await providers.BindVendorAsync(slug, ProviderCapabilities.Delivery, seller, entry.ProviderSlug,
                entry.Values, entry.WebhookId, null, seller.ToString("D"));

            var options = Options.Create(new OtoOptions { Environment = OtoEnvironment.Staging, RefreshToken = "RT-live-proof" });
            DealShipping ShippingWith(ProviderVault keys) => new(new DealsService(store, new ForbiddenPaymentProvider()),
                new OtoClient(new HttpClient(new OtoRouteHandler()), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance), options,
                null, providers, keys, new FixedClock(DealShipTests.Now));
            Assert.Null(DealShipping.VendorTokenFor(options.Value, slug));

            var entries = await providers.HasVendorEntriesAsync(slug, ProviderCapabilities.Delivery);
            var withVault = await ShippingWith(vault).CarrierReadyForStoreAsync(slug);
            var withoutVault = await ShippingWith(ProviderVault.FromKek(null)).CarrierReadyForStoreAsync(slug);

            output.WriteLine($"مَداخِل: {entries} · الوارِدُ بِخِزانَة: {withVault} · بِلا خِزانَة: {withoutVault}");
            Assert.Equal((true, true, false), (entries, withVault, withoutVault));
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            foreach (var b in await s.Query<TenantProviderBinding>().ToListAsync()) s.Delete(b);
            await s.SaveChangesAsync();
        }
    }

    /// <summary>
    /// <para><b>التَسجيلُ عَلى Postgres</b> (‏ADR-033 الخُطوَة 8، ‏2026-10-12): بائِعٌ يُسَجَّلُ (مُعالِجٌ يُعيدُ رَدَّ الوَثيقَة) فَيُكتَبُ مَدخَلُه
    /// مَختوماً في مُستَأجِرِه وسَطرُ أَثَرِه في مَساحَتِه؛ وبائِعٌ تَرُدُّ OTO تَسجيلَه لا يُكتَبُ لَه مَدخَل — وسَطرُ مُحاوَلَتِه قائِم.</para>
    /// </summary>
    [Fact]
    public async Task A_registered_vendor_is_sealed_in_their_tenant_with_an_audit_line_and_a_refused_one_stores_nothing_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "vs-zz-g-" + Guid.NewGuid().ToString("N")[..8];
        var (accepted, refused) = (Guid.NewGuid(), Guid.NewGuid());
        try
        {
            var providers = new TenantProviderService(store);
            var vault = DealShipTests.NewVault();
            var oto = new OtoRouteHandler();
            var options = Options.Create(new OtoOptions { Environment = OtoEnvironment.Staging, MarketplaceToken = "MKT-live" });
            var surface = new VendorShippingService(providers, vault,
                new OtoClient(new HttpClient(oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance), options,
                new ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditWriter(store),
                new ACommerce.Templates.Customer.Marketplace.Services.Api.ApiKeyRevealStore(
                    new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())));

            var ok = await surface.RegisterAsync(slug, VendorShippingTests.Registrant(accepted),
                VendorShippingTests.Form(), "https://wasayel.example/x");
            oto.Routes[OtoClient.RegisterPath] = _ => OtoRouteHandler.Json(HttpStatusCode.BadRequest, "{\"success\": false}");
            var no = await surface.RegisterAsync(slug, VendorShippingTests.Registrant(refused),
                VendorShippingTests.Form(), "https://wasayel.example/x");

            var entry = (await providers.VendorEntryAsync(slug, ProviderCapabilities.Delivery, accepted))!;
            await using var q = store.QuerySession(slug);
            var audits = await q.Query<ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditEntry>().ToListAsync();
            output.WriteLine($"المَقبول: {(ok.Ok ? "ok" : ok.Code)} · المَردود: {no.Code} · أَثَر: {audits.Count} · مَدخَلُ المَردود: " +
                             $"{((await providers.VendorEntryAsync(slug, ProviderCapabilities.Delivery, refused)) is null ? "لا" : "نَعَم")}");

            Assert.True(ok.Ok, ok.Code);
            Assert.Equal("_refreshToken_", entry.Values[VendorShippingSurface.RefreshField!]
                .Unseal(vault, VendorShippingSurface.AadFor(slug, VendorShippingSurface.RefreshField!, accepted)));
            Assert.Equal(OtoFailures.Refused, no.Code);
            Assert.Null(await providers.VendorEntryAsync(slug, ProviderCapabilities.Delivery, refused));
            Assert.Equal(2, audits.Count(a => a.Action == VendorShippingService.RegisterAuditAction && a.Note == "register"));
            Assert.Equal("https://login.tryoto.com/XYZ", surface.TakeActivationLink(slug, accepted));
            Assert.Null(surface.TakeActivationLink(slug, refused));
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            foreach (var b in await s.Query<TenantProviderBinding>().ToListAsync()) s.Delete(b);
            foreach (var a in await s.Query<ACommerce.Templates.Customer.Marketplace.Services.Audit.AuditEntry>().ToListAsync()) s.Delete(a);
            await s.SaveChangesAsync();
        }
    }
}
