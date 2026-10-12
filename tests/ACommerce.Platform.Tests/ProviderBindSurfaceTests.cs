using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services.Providers;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ ما يُكتَب عِندَ الرَبط — فَحصُ المُدخَلِ وسياجُ المُضيفين ═══
//
// **العِلَّةُ الَّتي كَتَبَت هذا المِلَفّ**: سياجُ المُضيفينَ في مِلَفِّ
// التَعريفِ لا يُساوي شَيئاً إن لَم يُرَدَّ بِه طَلَب. وسابِقَتُه
// مَقيسَةٌ في المُستَودَع: `AllowCustomPattern` تُعرَض في بِطاقَةِ
// الأَسعارِ **وصِفرُ مَوضِعٍ يَفحَصُها** (القاعِدَة ١٢). فَالفَحصُ هُنا
// عَلى الدالَّةِ النَقِيَّةِ نَفسِها الَّتي تُناديها النُقطَة.
public class ProviderBindSurfaceTests
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
        var read = ProviderBindSurface.FromForm("provider_from_the_future", _ => "anything");

        Assert.Equal(ProviderBindSurface.ProviderUnknown, read.RefusalCode);
        Assert.Null(read.Definition);
        Assert.Empty(read.Values);
        Assert.False(read.NeedsPlatformAdmin);
    }

    [Fact]
    public void A_platform_key_provider_declares_that_it_needs_the_platform_guard()
    {
        var read = ProviderBindSurface.FromForm("paypal_subscriptions", _ => "");

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
        var read = ProviderBindSurface.FromForm("mock_payments", _ => "");
        Assert.False(read.NeedsPlatformAdmin);
        Assert.Equal(ProviderBindSurface.ProviderNotBindable, read.RefusalCode);
    }

    [Fact]
    public void A_hosted_link_binding_is_read_validated_and_stored_explicitly()
    {
        const string link = "https://moyasar.com/i/inv_9";
        var read = ProviderBindSurface.FromForm("moyasar_hosted",
            code => code == "invoice_url" ? link : null);

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
            _ => "https://evilmoyasar.com/i/1");

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
        });

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
        Assert.Equal(new[] { ProviderCapabilities.Payments }, ProviderBindSurface.OfferedCapabilities.ToArray());

        // والمُسنَدُ واحِد: ما لا يُرسَمُ هُوَ بِعَينِه ما تَرُدُّه النُقطَةُ بِرَمزِ الخِزانَة.
        var compared = 0;
        foreach (var d in ProviderCatalog.TenantBindable(ProviderCapabilities.Payments))
        {
            compared++;
            var refusedForVault = ProviderBindSurface.FromForm(d.Slug, _ => "").RefusalCode
                                  == ProviderBindSurface.VaultUnavailable;
            Assert.Equal(!offered.Contains(d.Slug), refusedForVault);
        }
        Assert.True(compared == 2, $"أَداةٌ عَمياء: قورِنَ {compared} مُزَوِّداً.");
    }

    /// <summary>والشاشَةُ تَقرَأُ قائِمَةَ السَطحِ لا قائِمَةَ الكاتالوج — وإلّا رَسَمَت
    /// بِطاقَةً تَرُدُّها النُقطَة (القاعِدَة ١٢). مَقيسٌ نَصّاً لِأَنّ الصَفحَةَ لا تُصَيَّرُ
    /// في وَحدَة.</summary>
    [Fact]
    public void The_studio_screen_reads_the_surface_list_not_the_catalog_list()
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "StudioProviders.razor");
        var razor = File.ReadAllText(path);
        Assert.True(razor.Length > 1000, "أَداةٌ عَمياء: الصَفحَةُ لَم تُقرَأ.");

        Assert.Contains("ProviderBindSurface.Offered(capability)", razor, StringComparison.Ordinal);
        Assert.Contains("ProviderBindSurface.OfferedCapabilities", razor, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderCatalog.TenantBindable(", razor, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderCatalog.BindableCapabilities", razor, StringComparison.Ordinal);
    }
}
