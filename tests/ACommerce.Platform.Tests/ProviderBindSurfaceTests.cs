using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ ما يُكتَب عِندَ الرَبط — فَحصُ المُدخَلِ وسياجُ المُضيفين ═══
//
// **العِلَّةُ الَّتي كَتَبَت هذا المِلَفّ**: سياجُ المُضيفينَ في مِلَفِّ
// التَعريفِ لا يُساوي شَيئاً إن لَم يُرَدَّ بِه طَلَب. وسابِقَتُه
// مَقيسَةٌ في المُستَودَع: `AllowCustomPattern` تُعرَض في بِطاقَةِ
// الأَسعارِ **وصِفرُ مَوضِعٍ يَفحَصُها** (القاعِدَة ١٢). فَالفَحصُ هُنا
// عَلى الدالَّةِ النَقِيَّةِ نَفسِها الَّتي تُناديها النُقطَة.
public class ProviderBindSurfaceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static ProviderFieldDefinition Link(params string[] hosts) => new()
    {
        Code = "invoice_url",
        Kind = CredentialKinds.HostedLink,
        IsRequired = true,
        HostAllowlist = hosts,
    };

    // ─── ١. سياجُ المُضيفين — بِالعُنقودِ لا بِـEndsWith ─────────────

    [Theory]
    [InlineData("moyasar.com", true)]
    [InlineData("MOYASAR.COM", true)]
    [InlineData("www.moyasar.com", true)]
    [InlineData("pay.sub.moyasar.com", true)]
    [InlineData("moyasar.com.", true)]
    public void An_allowed_host_or_its_subdomain_passes(string host, bool expected)
        => Assert.Equal(expected, ProviderValueValidator.HostAllowed(host, new[] { "moyasar.com" }));

    [Theory]
    // **الحالَةُ الَّتي يَسقُط فيها `EndsWith`**: نِطاقٌ يَنتَهي
    // بِالاسمِ ولَيسَ مِنه.
    [InlineData("evilmoyasar.com")]
    [InlineData("moyasar.com.attacker.io")]
    [InlineData("moyasar.co")]
    [InlineData("localhost")]
    [InlineData("")]
    public void A_lookalike_host_is_refused(string host)
        => Assert.False(ProviderValueValidator.HostAllowed(host, new[] { "moyasar.com" }));

    [Fact]
    public void An_empty_allowlist_allows_nothing_at_all()
        => Assert.False(ProviderValueValidator.HostAllowed("moyasar.com", Array.Empty<string>()));

    // ─── ٢. رُموزُ الرَفضِ الأَربَعَة — موجِبٌ وسالِبٌ لِكُلٍّ ────────

    [Fact]
    public void field_required_fires_only_on_an_empty_required_field()
    {
        Assert.Equal(ProviderValueValidator.Required,
            ProviderValueValidator.Refuse(Link("moyasar.com"), "   "));
        Assert.Null(ProviderValueValidator.Refuse(
            Link("moyasar.com") with { IsRequired = false }, ""));
        Assert.Null(ProviderValueValidator.Refuse(
            Link("moyasar.com"), "https://moyasar.com/i/1"));
    }

    [Theory]
    [InlineData("http://moyasar.com/i/1")]        // لا https
    [InlineData("/i/1")]                          // لَيسَ مُطلَقاً
    [InlineData("javascript:alert(1)")]
    [InlineData("moyasar.com/i/1")]
    public void field_not_absolute_https_fires_on_anything_but_an_absolute_https_url(string v)
        => Assert.Equal(ProviderValueValidator.NotHttps,
            ProviderValueValidator.Refuse(Link("moyasar.com"), v));

    [Fact]
    public void field_not_absolute_https_does_not_fire_on_a_proper_url()
        => Assert.Null(ProviderValueValidator.Refuse(
            Link("moyasar.com"), "https://moyasar.com/i/inv_1?x=2"));

    [Theory]
    [InlineData("https://evil.example/i/1")]
    [InlineData("https://evilmoyasar.com/i/1")]
    public void field_host_not_allowed_fires_outside_the_fence(string v)
        => Assert.Equal(ProviderValueValidator.HostNotAllowed,
            ProviderValueValidator.Refuse(Link("moyasar.com"), v));

    [Fact]
    public void field_host_not_allowed_does_not_fire_inside_the_fence()
        => Assert.Null(ProviderValueValidator.Refuse(
            Link("moyasar.com"), "https://pay.moyasar.com/i/1"));

    [Fact]
    public void field_pattern_mismatch_fires_only_against_a_declared_pattern()
    {
        var keyed = new ProviderFieldDefinition
        {
            Code = "publishable_key",
            Kind = CredentialKinds.HostedLink,
            IsRequired = true,
            HostAllowlist = new[] { "moyasar.com" },
            Pattern = "^https://moyasar\\.com/",
        };

        Assert.Equal(ProviderValueValidator.PatternMismatch,
            ProviderValueValidator.Refuse(keyed, "https://pay.moyasar.com/i/1"));
        Assert.Null(ProviderValueValidator.Refuse(keyed, "https://moyasar.com/i/1"));

        // وبِلا نَمَطٍ مُعلَنٍ لا يُفحَص شَيء.
        Assert.Null(ProviderValueValidator.Refuse(
            Link("moyasar.com"), "https://pay.moyasar.com/i/1"));
    }

    [Fact]
    public void Four_codes_are_declared_and_all_four_are_reachable()
    {
        Assert.Equal(4, ProviderValueValidator.Codes.Count);
        Assert.Equal(4, ProviderValueValidator.Codes.Distinct(StringComparer.Ordinal).Count());
    }

    // ─── ٣. النِصفُ النَقِيُّ مِن سَطحِ الرَبط ───────────────────────

    [Fact]
    public void An_unknown_provider_slug_is_refused_before_any_field_is_read()
    {
        var read = ProviderBindSurface.FromForm("provider_from_the_future", _ => "anything", tenantSlug: "", vault: null);

        Assert.Equal(ProviderBindSurface.ProviderUnknown, read.RefusalCode);
        Assert.Null(read.Definition);
        Assert.Empty(read.Values);
        Assert.False(read.NeedsPlatformAdmin);
    }

    [Fact]
    public void A_platform_key_provider_declares_that_it_needs_the_platform_guard()
    {
        var read = ProviderBindSurface.FromForm("paypal_subscriptions", _ => "", tenantSlug: "", vault: null);

        // النَوعُ يُعلَنُ **قَبلَ** الرَفض — فَالنُقطَةُ تَسأَلُ حارِسَ
        // المَنَصَّةِ ثُمَّ تَرُدّ، ولا يُعرَفُ بِالرَدِّ أَنَّ التَعريفَ
        // مَوجود.
        Assert.True(read.NeedsPlatformAdmin);
        Assert.Equal(ProviderBindSurface.ProviderNotBindable, read.RefusalCode);
        Assert.Empty(read.Values);
    }

    [Fact]
    public void A_none_kind_provider_is_not_bindable_either()
    {
        var read = ProviderBindSurface.FromForm("mock_payments", _ => "", tenantSlug: "", vault: null);
        Assert.False(read.NeedsPlatformAdmin);
        Assert.Equal(ProviderBindSurface.ProviderNotBindable, read.RefusalCode);
    }

    [Fact]
    public void A_hosted_link_binding_is_read_validated_and_stored_explicitly()
    {
        const string link = "https://moyasar.com/i/inv_9";
        var read = ProviderBindSurface.FromForm("moyasar_hosted",
            code => code == "invoice_url" ? link : null, tenantSlug: "", vault: null);

        Assert.Null(read.RefusalCode);
        Assert.Equal(ProviderCapabilities.Payments, read.Capability);
        Assert.Equal("moyasar_hosted", read.ProviderSlug);

        var v = Assert.Contains("invoice_url", read.Values);
        Assert.Equal(CredentialKinds.HostedLink, v.Kind);
        Assert.Equal(link, v.Plain);
        Assert.Equal(0, v.KekVersion);
    }

    [Fact]
    public void A_link_outside_the_declared_fence_never_reaches_storage()
    {
        var read = ProviderBindSurface.FromForm("moyasar_hosted",
            _ => "https://evilmoyasar.com/i/1", tenantSlug: "", vault: null);

        Assert.Equal(ProviderValueValidator.HostNotAllowed, read.RefusalCode);
        Assert.Empty(read.Values);
    }

    // ─── ٤. سَطرُ التَدقيقِ مُعَتَّمٌ ولَو حَمَلَ سِرّاً ─────────────

    [Fact]
    public void The_audit_line_of_a_binding_never_carries_a_secret()
    {
        const string secret = "sk_live_9f3b7c2d41aa58e6";
        var b = new TenantProviderBinding
        {
            Id = ProviderCapabilities.Payments,
            Slug = ProviderCapabilities.Payments,
            ProviderSlug = "moyasar_hosted",
            Status = TenantProviderBinding.StatusActive,
            Values =
            {
                ["invoice_url"] = new StoredValue
                {
                    Kind = CredentialKinds.HostedLink, Plain = "https://moyasar.com/i/1",
                },
                ["webhook_secret"] = new StoredValue
                {
                    Kind = CredentialKinds.SharedSecret, Plain = secret,
                },
            },
        };

        var line = ProviderBindSurface.AuditLine(b);

        Assert.Contains("provider=moyasar_hosted", line, StringComparison.Ordinal);
        Assert.Contains("invoice_url=https://moyasar.com/i/1", line, StringComparison.Ordinal);
        for (var i = 0; i + 4 <= secret.Length; i++)
            Assert.DoesNotContain(secret.Substring(i, 4), line, StringComparison.Ordinal);

        Assert.Equal("provider=-; status=-", ProviderBindSurface.AuditLine(null));
    }

    // ─── ٥. ما يَحتاجُ الخِزانَةَ لا يُرسَمُ ولا يُكتَبُ ما دامَت لا تَبلُغُ السَطح ──
    //
    // ‏2026-10-12 (‏ADR-032 الخُطوَتانِ 5 و6): دَخَلَ مِفتاحُ البَوّابَةِ السِرِّيُّ
    // الكاتالوجَ، ولا خِزانَةَ تَبلُغُ هذا السَطحَ في الكوميتِ نَفسِه — فَلَولا هذا
    // الحَدُّ لَرُسِمَت بِطاقَتُه ولَبَلَغَ السِرُّ `StoredValue.Explicit` فَرَمَت:
    // ‏500 عِندَ النَقر. البِطاقَةُ لا تُرسَم، والنُقطَةُ تَرُدُّ قَبلَ أَوَّلِ حَقل.

    [Fact]
    public void A_provider_whose_field_the_vault_seals_is_refused_before_any_field_is_read()
    {
        var reads = 0;
        var read = ProviderBindSurface.FromForm("moyasar_api", _ =>
        {
            reads++;
            return "sk_live_9f3b7c2d41aa58e6";
        }, tenantSlug: "", vault: null);

        Assert.Equal(ProviderBindSurface.VaultUnavailable, read.RefusalCode);
        Assert.Equal("vault_unavailable", read.RefusalCode);
        Assert.Equal(0, reads);
        Assert.Empty(read.Values);
        Assert.False(read.NeedsPlatformAdmin);
        Assert.Equal("moyasar_api", read.ProviderSlug);
        Assert.True(ProviderBindSurface.NeedsVault(read.Definition!));
    }

    [Fact]
    public void The_screen_offers_what_the_surface_can_store_and_the_endpoint_refuses_the_rest()
    {
        var catalog = ProviderCatalog.TenantBindable(ProviderCapabilities.Payments)
            .Select(d => d.Slug).ToArray();
        var offered = ProviderBindSurface.Offered(ProviderCapabilities.Payments)
            .Select(d => d.Slug).ToArray();

        Assert.Equal(new[] { "moyasar_hosted", "moyasar_api" }, catalog);
        Assert.Equal(new[] { "moyasar_hosted" }, offered);
        // ‏(‏2026-10-25، lens-identity-rules-1؛ ولا يُعادُ حَرفٌ فَوقَه: كانَ `OfferedCapabilities()` وحُذِف — والقِسمُ بِلا رَبطٍ هُوَ جَوابُه بِالبِناء.)
        Assert.Equal(new[] { ProviderCapabilities.Payments }, ProviderBindSurface.SectionCapabilities(null, _ => false).ToArray());

        // والمُسنَدُ واحِد: ما لا يُرسَمُ هُوَ بِعَينِه ما تَرُدُّه النُقطَةُ بِرَمزِ الخِزانَة.
        var compared = 0;
        foreach (var d in ProviderCatalog.TenantBindable(ProviderCapabilities.Payments))
        {
            compared++;
            var refusedForVault = ProviderBindSurface.FromForm(d.Slug, _ => "", tenantSlug: "", vault: null).RefusalCode
                                  == ProviderBindSurface.VaultUnavailable;
            Assert.Equal(!offered.Contains(d.Slug), refusedForVault);
        }
        Assert.True(compared == 2, $"أَداةٌ عَمياء: قورِنَ {compared} مُزَوِّداً.");
    }

    /// <summary>والشاشَةُ تَقرَأُ قائِمَةَ السَطحِ لا قائِمَةَ الكاتالوج — وإلّا رَسَمَت
    /// بِطاقَةً تَرُدُّها النُقطَة (القاعِدَة ١٢). مَقيسٌ نَصّاً لِأَنّ الصَفحَةَ لا تُصَيَّرُ
    /// في وَحدَة. **ومُنذُ الخُطوَة 7 (‏2026-10-12) تُمَرِّرُ الخِزانَةَ المُسَجَّلَة**،
    /// فَصارَ الحَرفانِ المَفحوصانِ بِوَسيطِها.</summary>
    [Fact]
    public void The_studio_screen_reads_the_surface_list_not_the_catalog_list()
    {
        var razor = StudioProvidersRazor();

        Assert.Contains("ProviderBindSurface.Offered(capability, Vault)", razor, StringComparison.Ordinal);
        // (‏2026-10-13، G4 (xxii)؛ ولا يُعادُ حَرفٌ فَوقَه: الأَقسامُ صارَت `SectionCapabilities(Vault, …)` — قائِمَةُ السَطحِ بِالخِزانَةِ نَفسِها،
        // بِطاقَةٌ مَعروضَةٌ أَو رَبطٌ قائِم — لا `OfferedCapabilities(Vault)`؛ والشاشَةُ ما زالَت لا تَقرَأُ الكاتالوجَ مُباشَرَة.)
        Assert.Contains("ProviderBindSurface.SectionCapabilities(Vault,", razor, StringComparison.Ordinal);
        Assert.Contains("@inject ProviderVault Vault", razor, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderCatalog.TenantBindable(", razor, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderCatalog.BindableCapabilities", razor, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (xxii)): كانَ
    /// <c>Today_the_studio_providers_page_draws_and_loads_bindings_only_for_capabilities_with_an_offered_card</c> (مُودَعٌ في <c>5cdff1a</c>).
    /// وصارَت الصَفحَةُ تُحَمِّلُ الرَبطَ القائِمَ لِكُلِّ قُدرَةٍ يَربِطُها مُستَأجِر (<see cref="ProviderBindSurface.BindingCapabilities"/>)، وتَرسُمُ
    /// الأَقسامَ بِـ<see cref="ProviderBindSurface.SectionCapabilities"/> — بِطاقَةٌ مَعروضَةٌ أَو رَبطٌ قائِم — والبِطاقاتُ بِـ<c>Offered</c> وَحدَها.</para>
    /// </summary>
    [Fact]
    public void The_studio_providers_page_loads_every_bindable_capabilitys_binding_and_gates_only_the_cards_on_the_vault()
    {
        var razor = StudioProvidersRazor();
        Assert.Contains("@foreach (var capability in Services.Providers.ProviderBindSurface.SectionCapabilities(Vault, bindings.ContainsKey))", razor, StringComparison.Ordinal);
        Assert.Contains("foreach (var capability in Services.Providers.ProviderBindSurface.BindingCapabilities)\n        {\n            var doc = await Providers.CurrentAsync(Slug, capability);",
            razor.Replace("\r", ""), StringComparison.Ordinal);
        Assert.Contains("var offered = Services.Providers.ProviderBindSurface.Offered(capability, Vault);", razor, StringComparison.Ordinal);
        Assert.DoesNotContain("OfferedCapabilities(", razor, StringComparison.Ordinal);

        // والكاتالوجُ اليَوم: «مَدفوعات» قِسمٌ بِخِزانَةٍ وبِدونِها، بِرَبطٍ وبِدونِه.
        foreach (var vault in new[] { ProviderVault.FromKek(null), AvailableVault() })
        foreach (var bound in new[] { false, true })
            Assert.Equal(new[] { ProviderCapabilities.Payments },
                ProviderBindSurface.SectionCapabilities(vault, _ => bound).ToArray());
        Assert.Equal(ProviderCatalog.BindableCapabilities, ProviderBindSurface.BindingCapabilities);
    }

    [Fact]
    public void A_capability_whose_providers_are_all_sealed_keeps_its_section_while_bound_and_drops_it_when_not()
    {
        // قُدرَةٌ مَصنوعَةٌ في الصَفّ: «x» كُلُّ مُزَوِّديها مَختومونَ فَلا بِطاقَةَ بِلا خِزانَة، و«y» لَها بِطاقَةٌ دائِماً.
        var caps = new[] { "x", "y" };
        Func<string, bool> offeredNoVault = c => c == "y";
        Func<string, bool> offeredWithVault = _ => true;

        Assert.Equal(new[] { "x", "y" }, ProviderBindSurface.Sections(caps, offeredNoVault, c => c == "x").ToArray());   // رَبطٌ مَختومٌ يَبقى
        Assert.Equal(new[] { "y" }, ProviderBindSurface.Sections(caps, offeredNoVault, _ => false).ToArray());           // لا قِسمَ فارِغ
        Assert.Equal(new[] { "x", "y" }, ProviderBindSurface.Sections(caps, offeredWithVault, _ => false).ToArray());    // بِخِزانَةٍ كَما كان
        Assert.Empty(ProviderBindSurface.Sections(caps, _ => false, _ => false));
    }

    private static string StudioProvidersRazor()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "StudioProviders.razor");
        var razor = File.ReadAllText(path);
        Assert.True(razor.Length > 1000, "أَداةٌ عَمياء: الصَفحَةُ لَم تُقرَأ.");
        return razor;
    }

    // ─── ٦. الخِزانَةُ تَبلُغُ السَطح (‏ADR-032 الخُطوَة 7) ─────────────────
    //
    // مِفتاحُ البَوّابَةِ يُختَمُ في النُقطَةِ بِسِياقِ مَتجَرِه ولا يُحمَلُ صَريحاً
    // بَعدَ الحَلقَة؛ والبِطاقَةُ تُرسَمُ حينَ تَختِمُ الخِزانَةُ وتُخفى حينَ لا
    // تَختِم (الخُطوَة 11)، والرَفضُ `vault_unavailable` رِسالَةُ نَموذَجٍ مِن القامُوس.

    private const string SecretKey = "sk_test_9f3b7c2d41aa58e6";

    private static ProviderVault AvailableVault() =>
        ProviderVault.FromKek(Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));

    [Fact]
    public void A_secret_key_binding_is_sealed_through_the_vault_and_never_held_in_clear()
    {
        var vault = AvailableVault();
        var read = ProviderBindSurface.FromForm("moyasar_api",
            code => code == "secret_key" ? $"  {SecretKey}  " : null, "theme-demo", vault);

        Assert.Null(read.RefusalCode);
        Assert.Equal(ProviderCapabilities.Payments, read.Capability);
        Assert.Equal("moyasar_api", read.ProviderSlug);

        var v = Assert.Contains("secret_key", read.Values);
        Assert.True(v.IsSealed);
        Assert.Null(v.Plain);
        Assert.Equal(CredentialKinds.SecretKey, v.Kind);
        Assert.Equal(ProviderVault.KekVersion, v.KekVersion);
        Assert.Equal("theme-demo|payments|secret_key", v.Aad);
        Assert.Equal(ProviderBindSurface.AadFor("theme-demo", ProviderCapabilities.Payments, "secret_key"), v.Aad);
        Assert.Equal("58e6", v.Tail4);
        Assert.Equal(ProviderSecrecy.Mask + "58e6", v.Censored);

        // مَقصوصاً كَما يُفحَصُ النَمَط — ويُفَكُّ بِسِياقِ مَتجَرِه وَحدَه.
        Assert.Equal(SecretKey, v.Unseal(vault,
            ProviderBindSurface.AadFor("theme-demo", ProviderCapabilities.Payments, "secret_key")));
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => v.Unseal(vault,
            ProviderBindSurface.AadFor("ashare", ProviderCapabilities.Payments, "secret_key")));
    }

    [Fact]
    public void vault_unavailable_is_a_form_refusal_when_the_vault_holds_no_key()
    {
        // الموجِب: خِزانَةٌ مُسَجَّلَةٌ بِلا مِفتاحٍ رَئيس — يُرَدُّ قَبلَ قِراءَةِ أَوَّلِ حَقل.
        var reads = 0;
        var read = ProviderBindSurface.FromForm("moyasar_api",
            _ => { reads++; return SecretKey; }, "theme-demo", ProviderVault.FromKek(null));

        Assert.Equal(ProviderBindSurface.VaultUnavailable, read.RefusalCode);
        Assert.Equal(0, reads);
        Assert.Empty(read.Values);

        // والشاشَةُ تَقرَؤُه رِسالَةً مِن القامُوس لا رَمزاً خاماً.
        var razor = StudioProvidersRazor();
        Assert.Contains("ProviderBindSurface.VaultUnavailable => \"studio.providers.err_vault_unavailable\"",
            razor, StringComparison.Ordinal);
        var ar = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "core", "ACommerce.Platform.I18n", "Locales", "ar.json"));
        Assert.Contains("\"studio.providers.err_vault_unavailable\": \"", ar, StringComparison.Ordinal);
    }

    [Fact]
    public void vault_unavailable_does_not_fire_when_the_vault_seals_or_when_nothing_needs_it()
    {
        // السالِب الأَوَّل: الخِزانَةُ تَختِم ⇒ لا رَفض.
        Assert.Null(ProviderBindSurface.FromForm("moyasar_api", _ => SecretKey, "t", AvailableVault()).RefusalCode);

        // والثاني: الرابِطُ لا يَحتاجُ خِزانَةً — يُربَطُ بِلا مِفتاحٍ رَئيسٍ كَما كان.
        var link = ProviderBindSurface.FromForm("moyasar_hosted",
            code => code == "invoice_url" ? "https://moyasar.com/i/inv_9" : null, "t", ProviderVault.FromKek(null));
        Assert.Null(link.RefusalCode);
        Assert.False(Assert.Contains("invoice_url", link.Values).IsSealed);
    }

    [Theory]
    [InlineData("pk_live_9f3b7c2d41aa58e6")]   // المِفتاحُ المَنشورُ لا السِرِّيّ
    [InlineData("sk_prod_9f3b7c2d41aa58e6")]
    [InlineData("9f3b7c2d41aa58e6")]
    public void A_value_outside_the_declared_key_pattern_is_refused_and_nothing_is_sealed(string pasted)
    {
        var read = ProviderBindSurface.FromForm("moyasar_api", _ => pasted, "t", AvailableVault());

        Assert.Equal(ProviderValueValidator.PatternMismatch, read.RefusalCode);
        Assert.Empty(read.Values);
    }

    [Fact]
    public void The_screen_offers_the_secret_key_card_exactly_when_the_vault_can_seal()
    {
        string[] Slugs(ProviderVault? v) =>
            ProviderBindSurface.Offered(ProviderCapabilities.Payments, v).Select(d => d.Slug).ToArray();

        Assert.Equal(new[] { "moyasar_hosted", "moyasar_api" }, Slugs(AvailableVault()));
        Assert.Equal(new[] { "moyasar_hosted" }, Slugs(ProviderVault.FromKek(null)));
        Assert.Equal(new[] { "moyasar_hosted" }, Slugs(ProviderVault.FromKek("not base64 !!")));
        Assert.Equal(new[] { "moyasar_hosted" }, Slugs(null));

        // والقُدرَةُ تُرسَمُ في الحالَين — الرابِطُ مَعروضٌ دائِماً.
        // ‏(‏2026-10-25، lens-identity-rules-1؛ ولا يُعادُ حَرفٌ فَوقَه: كانَ `OfferedCapabilities(vault)` وحُذِف.)
        Assert.Equal(new[] { ProviderCapabilities.Payments },
            ProviderBindSurface.SectionCapabilities(ProviderVault.FromKek(null), _ => false).ToArray());
    }

    [Fact]
    public void Only_a_field_the_vault_seals_renders_as_a_password_input()
    {
        // (‏2026-10-29، قَرارُ المالِك د5 (٢) — ولا يُعادُ حَرفٌ فَوقَه: لِلتَعريفِ حَقلٌ ثانٍ تُصدِرُه وَسايِل — `IsFilledByOwner` تُسقِطُه مِن النَموذَج — فَالمِفتاحُ بِنَوعِه.)
        var secret = ProviderCatalog.Find("moyasar_api")!.Credential.Fields.Single(f => f.Kind == CredentialKinds.SecretKey);
        var link = ProviderCatalog.Find("moyasar_hosted")!.Credential.Fields.Single();

        Assert.Equal("password", ProviderBindSurface.InputType(secret));
        // (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (xviii)؛ ولا يُعادُ حَرفٌ فَوقَه: «off» صارَ «new-password» — المُتَصَفِّحُ يَتَجاهَلُ «off» في حَقلِ
        // كَلِمَةِ مُرور، فَيَملَؤُه بِالمَحفوظِ ويَعرِضُ حِفظَ مِفتاحِ التاجِر. والرابِطُ أَدناه بايتاً بِبايتٍ كَما كان.)
        Assert.Equal("new-password", ProviderBindSurface.InputAutocomplete(secret));

        // والرابِطُ بِلا سِمَةٍ أَصلاً — فَيُصَيَّرُ حَقلُه بايتاً بِبايتٍ كَما كان.
        Assert.Null(ProviderBindSurface.InputType(link));
        Assert.Null(ProviderBindSurface.InputAutocomplete(link));

        var razor = StudioProvidersRazor();
        Assert.Contains("type=\"@Services.Providers.ProviderBindSurface.InputType(f)\"", razor, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"@Services.Providers.ProviderBindSurface.InputAutocomplete(f)\"", razor, StringComparison.Ordinal);
        // والمَحفوظُ يُرسَمُ بِصورَتِه المُعَتَّمَة — ذَيلُ المَختومِ بَعدَ قِناع.
        Assert.Contains("v.Censored", razor, StringComparison.Ordinal);
    }

    /// <summary><b>ولا حَقلَ كَلِمَةِ مُرورٍ في أَيِّ صَفحَةٍ بِـ<c>autocomplete="off"</c></b> (‏2026-10-13، G4 (xviii)): حُقولُ ما تَختِمُه الخِزانَةُ
    /// كُلُّها — مِفتاحُ مَتجَرٍ في الاستوديو، ورَمزُ حِسابِ شَحنِ البائِعِ في «إعلاناتي» — <c>new-password</c> أَو بِدالَّةِ السَطح. فاحِصٌ يَعُدُّ ما فَحَص.</summary>
    [Fact]
    public void No_password_input_in_any_page_asks_the_browser_with_autocomplete_off()
    {
        var pages = 0;
        var passwordInputs = 0;
        var offenders = new List<string>();
        foreach (var root in new[] { "libs", "apps" })
        foreach (var f in Directory.EnumerateFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, root), "*.razor", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            pages++;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), @"<input\b[^>]*>"))
            {
                var tag = m.Value;
                if (!tag.Contains("type=\"password\"", StringComparison.Ordinal) && !tag.Contains("ProviderBindSurface.InputType(f)", StringComparison.Ordinal)) continue;
                passwordInputs++;
                if (tag.Contains("autocomplete=\"off\"", StringComparison.Ordinal)) offenders.Add($"{Path.GetFileName(f)}: {tag}");
            }
        }
        // ‏(‏2026-10-24 — مُراجَعَةُ الموجَة ٤، بَندُ P؛ القاعِدَة ١٠) يُطبَعُ ما عُدَّ في كُلِّ تَشغيلٍ لا عِندَ الفَشَلِ وَحدَه.
        output.WriteLine($"صَفَحاتٌ مَفحوصَة: {pages} · حُقولُ كَلِمَةِ مُرور: {passwordInputs} · بِـautocomplete=\"off\": {offenders.Count}");
        foreach (var o in offenders) output.WriteLine($"  مُخالِف: {o}");
        Assert.True(pages > 100, $"أَداةٌ عَمياء: فُحِصَت {pages} صَفحَة.");
        Assert.True(passwordInputs >= 2, $"أَداةٌ عَمياء: حُقولُ كَلِمَةِ المُرورِ المَفحوصَة {passwordInputs} — المُتَوَقَّعُ اثنانِ عَلى الأَقَلّ (الاستوديو و«إعلاناتي»).");
        Assert.True(offenders.Count == 0, "حَقلُ سِرٍّ بِـautocomplete=\"off\": " + string.Join("؛ ", offenders));
        Assert.Contains("<input name=\"refresh_token\" type=\"password\" autocomplete=\"new-password\"",
            File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace",
                "Components", "Pages", "MyListings.razor")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_bind_endpoint_reads_the_form_through_the_registered_vault()
    {
        var text = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs"));
        var at = text.IndexOf("app.MapPost(\"/studio/apps/{slug}/providers/bind\"", StringComparison.Ordinal);
        Assert.True(at > 0, "أَداةٌ عَمياء: نُقطَةُ الرَبطِ غَيرُ مَوجودَة.");
        var body = text.Substring(at, text.IndexOf(".DisableAntiforgery();", at, StringComparison.Ordinal) - at);

        Assert.Contains("ACommerce.Platform.Providers.ProviderVault vault", body, StringComparison.Ordinal);
        Assert.Contains("ProviderBindSurface.FromForm(req, slug, vault)", body, StringComparison.Ordinal);

        // والحارِسُ ما زالَ قَبلَ قِراءَةِ النَموذَج (القاعِدَة ٦).
        Assert.True(body.IndexOf("TenantAdminGuard.CanAdministerAsync", StringComparison.Ordinal)
                    < body.IndexOf("ProviderBindSurface.FromForm", StringComparison.Ordinal));

        // والخِزانَةُ مُسَجَّلَةٌ مِن مِفتاحِ التَهيئَةِ المُسَمّى.
        Assert.Contains("ProviderVault.FromKek(", text, StringComparison.Ordinal);
        Assert.Contains("ProviderVault.KekConfigKey]", text, StringComparison.Ordinal);
    }

    /// <summary><b>الخِزانَةُ تُسَجَّلُ مَرَّةً مِن مِفتاحِ التَهيئَةِ المُسَمّى</b> — بِقالِبٍ
    /// حَقيقيٍّ لا بِتَسجيلٍ يُكتَبُ في الاختِبار: بِالمِفتاحِ تَختِم، وبِغِيابِه تُسَجَّلُ
    /// غَيرَ مُتاحَةٍ ولا يَسقُطُ شَيء (‏ADR-032 الخُطوَة 11).</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_template_registers_one_vault_read_from_the_kek_configuration_key(bool withKek)
    {
        var pairs = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (withKek)
            pairs[ProviderVault.KekConfigKey] = Convert.ToBase64String(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(pairs).Build());
        services.AddCustomerMarketplaceTemplate();

        using var sp = services.BuildServiceProvider();
        var vault = sp.GetRequiredService<ProviderVault>();

        Assert.Equal(withKek, vault.IsAvailable);
        Assert.Same(vault, sp.GetRequiredService<ProviderVault>());
        if (!withKek)
            Assert.Equal(ProviderVault.Unavailable, Assert.Single(vault.Violations).Code);
    }

    [Fact]
    public void The_audit_line_of_a_sealed_binding_shows_its_tail_and_nothing_more()
    {
        var vault = AvailableVault();
        var read = ProviderBindSurface.FromForm("moyasar_api", _ => SecretKey, "t", vault);
        var b = new TenantProviderBinding
        {
            Id = ProviderCapabilities.Payments,
            Slug = ProviderCapabilities.Payments,
            ProviderSlug = read.ProviderSlug,
            Status = TenantProviderBinding.StatusActive,
            Values = new Dictionary<string, StoredValue>(read.Values, StringComparer.Ordinal),
        };

        var line = ProviderBindSurface.AuditLine(b);
        Assert.Equal($"provider=moyasar_api; status=active; secret_key={ProviderSecrecy.Mask}58e6", line);
        // كُلُّ نافِذَةٍ مِن أَربَعَةِ مَحارِفَ إلّا الذَيلَ المُعلَن.
        var checkedWindows = 0;
        for (var i = 0; i + 4 <= SecretKey.Length; i++)
        {
            var w = SecretKey.Substring(i, 4);
            if (w == SecretKey[^4..]) continue;
            checkedWindows++;
            Assert.DoesNotContain(w, line, StringComparison.Ordinal);
        }
        Assert.True(checkedWindows == 20, $"أَداةٌ عَمياء: {checkedWindows} نافِذَة.");
    }
}
