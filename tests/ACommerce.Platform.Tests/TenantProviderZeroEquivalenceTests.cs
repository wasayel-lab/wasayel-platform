using System.Text.Json;
using ACommerce.Platform.Flows;
using ACommerce.Platform.Providers;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ التَكافُؤُ الصِفريّ، والفَشَلُ المُغلَق، وتَعايُشُ نُسخَتَي المِفتاح ═══
//
// الدَعوى المَفحوصَة، مُصاغَةً بِدِقَّة:
//
//     مُستَأجِرٌ بِلا رَبطٍ واحِد  ≡  TenantProviderSet.Platform
//
// و«‏≡» هُنا **هُوَ هُوَ** لا «مُتَساوِيان»: بِلا وَثيقَةٍ لا تُبنى
// لَقطَةٌ جَديدَةٌ أَصلاً — أَقوى ما يُقال وأَرخَصُ مِن أَيّ مُقارَنَة
// (نَفسُ شَكلِ `FlowInventoryTests` و`TenantRoleZeroEquivalenceTests`).
public class TenantProviderZeroEquivalenceTests
{
    // ─── ١. التَكافُؤُ الصِفريّ ──────────────────────────────────────

    [Fact]
    public void No_binding_returns_the_platform_snapshot_by_reference()
    {
        Assert.Same(TenantProviderSet.Platform,
            TenantProviderSet.FromDocuments(null, Array.Empty<TenantProviderBinding>()));

        // ‏**وبِسلاجٍ حَقيقيّ أَيضاً** — وهذا هُوَ الفَرقُ عَن الأَدوار:
        // مُستَأجِرٌ قائِمٌ بِلا رَبطٍ واحِدٍ يُعطي نَفسَ المَرجِع.
        Assert.Same(TenantProviderSet.Platform,
            TenantProviderSet.FromDocuments("ashare", Array.Empty<TenantProviderBinding>()));
    }

    [Fact]
    public void The_platform_snapshot_answers_every_capability_with_nothing()
    {
        var zero = TenantProviderSet.Platform;

        Assert.Null(zero.TenantSlug);
        Assert.Empty(zero.Bound);
        Assert.False(zero.CollectsMoney);

        foreach (var c in ProviderCapabilities.All)
            Assert.Null(zero.For(c));

        Assert.Null(zero.For("capability_from_the_future"));
    }

    [Fact]
    public void A_revoked_binding_is_the_same_as_no_binding_at_all()
    {
        var revoked = Bound("payments", "moyasar_hosted", "https://moyasar.com/i/abc");
        revoked.Status = TenantProviderBinding.StatusRevoked;
        revoked.RevokedAt = DateTime.UtcNow;

        Assert.Same(TenantProviderSet.Platform,
            TenantProviderSet.FromDocuments("t", new[] { revoked }));
    }

    [Fact]
    public void A_binding_pointing_at_an_unknown_provider_is_ignored_not_thrown()
    {
        var stale = Bound("payments", "provider_from_the_future", "x");
        Assert.Same(TenantProviderSet.Platform,
            TenantProviderSet.FromDocuments("t", new[] { stale }));
    }

    [Fact]
    public void A_binding_whose_provider_serves_another_capability_is_ignored()
    {
        // مُعَرِّفُ الوَثيقَةِ «maps» ومُزَوِّدُها مُزَوِّدُ دَفع.
        var crossed = Bound("maps", "moyasar_hosted", "https://moyasar.com/i/abc");
        Assert.Same(TenantProviderSet.Platform,
            TenantProviderSet.FromDocuments("t", new[] { crossed }));
    }

    // ─── ٢. الرَبطُ الفَعّال ─────────────────────────────────────────

    [Fact]
    public void An_active_hosted_link_binding_resolves_and_collects()
    {
        const string link = "https://moyasar.com/i/inv_123";
        var set = TenantProviderSet.FromDocuments("t",
            new[] { Bound("payments", "moyasar_hosted", link) });

        Assert.NotSame(TenantProviderSet.Platform, set);
        Assert.Equal("t", set.TenantSlug);

        var p = set.For(ProviderCapabilities.Payments);
        Assert.NotNull(p);
        Assert.Equal("moyasar_hosted", p!.Slug);
        Assert.Equal(link, p.Explicit("invoice_url"));
        Assert.Equal(link, p.PaymentLink);
        Assert.True(p.CollectsMoney);
        Assert.True(set.CollectsMoney);

        // وباقي القُدُراتِ بِلا رَبط.
        foreach (var c in ProviderCapabilities.All.Where(c => c != ProviderCapabilities.Payments))
            Assert.Null(set.For(c));
    }

    [Fact]
    public void An_active_binding_with_an_empty_link_does_not_collect()
    {
        // **وهذا هُوَ الحارِسُ الَّذي يَمنَعُ تَسريبَ الإيراد**: رَبطٌ
        // فَعّالٌ بِلا رابِطٍ مَملوءٍ لا يَقبِض، فَلا تَظهَرُ باقَةٌ
        // مَدفوعَة.
        var set = TenantProviderSet.FromDocuments("t",
            new[] { Bound("payments", "moyasar_hosted", "   ") });

        Assert.False(set.CollectsMoney);
        Assert.Null(set.For(ProviderCapabilities.Payments)!.PaymentLink);
    }

    // ─── ٣. الفَشَلُ المُغلَق في التَخزين ────────────────────────────

    [Fact]
    public void An_explicit_value_of_a_secret_kind_is_refused_outright()
    {
        // لا خِزانَةَ في هذِه المَوجَة ⇒ لا سِرَّ يُخَزَّن. ولَيسَ
        // وَعداً في تَقرير: النِداءُ نَفسُه يَرمي.
        foreach (var kind in CredentialKinds.NotYetInVocabulary)
            Assert.ThrowsAny<Exception>(() => StoredValue.Explicit(kind, "s3cr3t"));

        Assert.Throws<InvalidOperationException>(
            () => StoredValue.Explicit(CredentialKinds.PlatformKey, "ours"));

        // والموجِب: النَوعُ الَّذي يُعرَض يُقبَل.
        var ok = StoredValue.Explicit(CredentialKinds.HostedLink, "https://moyasar.com/i/1");
        Assert.Equal(CredentialKinds.HostedLink, ok.Kind);
        Assert.Equal("https://moyasar.com/i/1", ok.Plain);
    }

    [Fact]
    public void No_stored_value_in_the_shipped_catalog_can_be_a_secret()
    {
        // كُلُّ حَقلٍ في كُلّ تَعريفٍ مَشحونٍ يَقبَلُ قيمَةً صَريحَة —
        // أَي أَنّ صِفرَ حَقلٍ يَحتاجُ خِزانَةً لَم تُبنَ.
        var fields = ProviderCatalog.Definitions
            .SelectMany(d => d.Credential.Fields).ToArray();

        Assert.True(fields.Length > 0, "أَداة عَمياء: صِفرُ حَقلٍ مَفحوص.");
        foreach (var f in fields)
            Assert.False(CredentialKinds.IsSecretLike(f.Kind),
                $"الحَقل «{f.Code}» مِن نَوعٍ يَحتاج خِزانَةً غَير مَبنِيَّة.");
    }

    // ─── ٤. تَعايُشُ نُسخَتَي المِفتاح ───────────────────────────────

    [Fact]
    public void Two_key_versions_coexist_in_the_same_binding_and_survive_a_round_trip()
    {
        // الدَوَرانُ مُصَمَّمٌ مِن اليَومِ ولَو لَم يُنَفَّذ: بِنيَةُ
        // التَخزينِ تَحمِلُ مُعَرِّفَ نُسخَةِ المِفتاح، ونُسخَتانِ
        // تَتَعايَشانِ في نَفسِ الوَثيقَة — فَالتَرحيلُ إعادَةُ
        // تَغليفٍ في الخَلفِيَّةِ لا انقِطاع.
        var doc = new TenantProviderBinding
        {
            Id = ProviderCapabilities.Payments,
            Slug = ProviderCapabilities.Payments,
            TenantSlug = "t",
            ProviderSlug = "moyasar_hosted",
            Values =
            {
                ["old_field"] = new StoredValue
                {
                    Kind = CredentialKinds.HostedLink,
                    Cipher = "AAAA", Nonce = "BBBB", Tag = "CCCC",
                    Aad = "t|moyasar_hosted|old_field|1", KekVersion = 1,
                },
                ["new_field"] = new StoredValue
                {
                    Kind = CredentialKinds.HostedLink,
                    Cipher = "DDDD", Nonce = "EEEE", Tag = "FFFF",
                    Aad = "t|moyasar_hosted|new_field|2", KekVersion = 2,
                },
            },
        };

        var json = JsonSerializer.Serialize(doc);
        var back = JsonSerializer.Deserialize<TenantProviderBinding>(json)!;

        Assert.Equal(2, back.Values.Count);
        Assert.Equal(1, back.Values["old_field"].KekVersion);
        Assert.Equal(2, back.Values["new_field"].KekVersion);
        Assert.Equal("AAAA", back.Values["old_field"].Cipher);
        Assert.Equal("DDDD", back.Values["new_field"].Cipher);
        Assert.Equal("t|moyasar_hosted|old_field|1", back.Values["old_field"].Aad);

        // والنُسخَتانِ مُتَمايِزَتان — لا الأَخيرَةُ تَبتَلِعُ الأولى.
        Assert.NotEqual(back.Values["old_field"].KekVersion,
                        back.Values["new_field"].KekVersion);
    }

    // ─── ٥. الحالَةُ مُستَعارَةٌ مِن المِفتاحِ لا مِن الاعتِماد ──────

    [Fact]
    public void The_binding_lifecycle_is_active_revoked_and_never_ApprovalFlow()
    {
        Assert.Equal(new[] { "active", "revoked" }, TenantProviderBinding.Statuses.ToArray());

        foreach (var s in TenantProviderBinding.Statuses)
            Assert.False(ApprovalFlow.Contains(s),
                $"حالَةُ الرَبط «{s}» صارَت في مَعجَمِ الاعتِماد — والنِهائِيَّةُ " +
                "لا تَصلُح لِاعتِمادٍ يَجِب أَن يَتَوَقَّفَ الآن.");

        foreach (var s in ApprovalFlow.All)
            Assert.DoesNotContain(s, TenantProviderBinding.Statuses);
    }

    [Fact]
    public void The_binding_refuses_to_be_authored_as_a_definition()
    {
        ITenantDefinitionDocument doc = new TenantProviderBinding();

        Assert.Throws<NotSupportedException>(() => doc.DefinitionJson);
        Assert.Throws<NotSupportedException>(() => doc.DefinitionJson = "{}");

        // ولا تَنكَسِرُ المَكانيكا الَّتي تَستَعملُها فِعلاً.
        doc.CreatedBy = "owner";
        doc.CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal("owner", ((TenantProviderBinding)doc).BoundBy);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ((TenantProviderBinding)doc).BoundAt);
        Assert.Null(doc.DecidedBy);
    }

    // ─── أَدَوات ─────────────────────────────────────────────────────

    private static TenantProviderBinding Bound(string capability, string provider, string link)
        => new()
        {
            Id = capability,
            Slug = capability,
            TenantSlug = "t",
            ProviderSlug = provider,
            Status = TenantProviderBinding.StatusActive,
            Values = { ["invoice_url"] = new StoredValue
            {
                Kind = CredentialKinds.HostedLink, Plain = link,
            } },
            BoundBy = "owner",
            BoundAt = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc),
        };
}
