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

    /// <summary>خِدمَةُ السَطحِ عَلى عالَمِ الشَحنِ نَفسِه: مَداخِلُه وخِزانَتُه ومُعالِجُ OTO.</summary>
    private static VendorShippingService Surface(DealShipTests.World w, OtoOptions? opts = null, ProviderVault? vault = null)
    {
        var options = Options.Create(opts ?? Opts());
        return new VendorShippingService(w.Providers, vault ?? w.Vault,
            new OtoClient(new HttpClient(w.Oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance), options);
    }

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
        var asked = page.IndexOf("if (myListings.Count > 0)", StringComparison.Ordinal);
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

    /// <summary>لِكُلِّ رَمزٍ يَصِلُ الصَفحَةَ نَصٌّ عَرَبِيٌّ حَيّ — مَعجَما البَوّابَةِ والخِدمَةِ بِلا تَداخُل.</summary>
    [Fact]
    public void Every_refusal_code_of_the_vendor_surface_has_an_arabic_text()
    {
        var codes = ListingVendorGate.All.Concat(VendorShippingService.Codes).ToArray();
        output.WriteLine($"رُموزُ سَطحِ البائِع: {codes.Length} — {string.Join(" · ", codes)}");
        Assert.Equal(5, codes.Length);
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
        Assert.Equal(vendor ? id![..id.IndexOf(':')] : "", capability);
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
        Assert.Equal((DealShipTests.VendorToken, (string?)DealShipTests.WebhookSecret, true),
            (carrier.RefreshToken, carrier.WebhookSecret, carrier.FromVendorEntry));
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
        Assert.Equal((DealShipTests.ConfigToken, (string?)null, false), (carrier.RefreshToken, carrier.WebhookSecret, carrier.FromVendorEntry));

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
        Assert.False(await shipping.CarrierReadyForStoreAsync("ashare"));
        Assert.True(await shipping.CarrierReadyForStoreAsync(Slug));      // رَمزُ التَهيئَةِ لِمَتجَرِ العَرضِ يَفتَحُ الوارِدَ كَما كان
        Assert.False(await (await DealShipTests.ConfirmedCashDeal(environment: OtoEnvironment.Production)).Shipping.CarrierReadyForStoreAsync(Slug));
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
}

// ═══ البُرهانُ الحَيّ — Postgres (‏WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres) ═══════════════════════
//
// عالَمُ الوَثائِقِ بِلا إيجارٍ ولا استِعلام، فَما يَحتاجُهُما يُقاسُ هُنا: المَدخَلُ يُكتَبُ ويُقرَأُ بِمِفتاحِه في مُستَأجِرِه وَحدَه، ومُجموعَةُ
// المَتجَرِ المَقروءَةُ مِن Postgres تَتَجاوَزُه، و«أَلِلمَتجَرِ ناقِل؟» — سُؤالُ الوارِدِ قَبلَ قِراءَةِ الجِسم — نَعَم حَيثُ مَدخَلٌ فَعّال
// ولا حَيثُ لا مَدخَل أَو رَبطُ مَتجَرٍ وَحدَه أَو مَدخَلٌ مَسحوب. مُستَأجِراتٌ مُؤَقَّتَةٌ تُمسَحُ بَعدَه.

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
                new OtoClient(new HttpClient(oto), options, new OtoTokenCache(), NullLogger<OtoClient>.Instance), options);
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
}
