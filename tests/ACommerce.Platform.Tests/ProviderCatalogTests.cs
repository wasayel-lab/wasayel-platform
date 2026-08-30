using ACommerce.Platform.Providers;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ الكاتالوج — كُلُّ مِلَفٍّ يُحَمَّل ويُصادَق، وكُلُّ نَوعٍ يُعلِنُه مِلَفّ ═══
//
// **العِلَّةُ المَقيسَة**: سَبعَةُ مَشاريعِ مُزَوِّدين في المُستَودَعِ
// تُبنى ولا يُحيلُها أَيُّ `csproj` — مَقبَرَةٌ في الكود. ومِلَفُّ
// تَعريفٍ بِلا تَنفيذٍ حَيّ يُنشِئ المَقبَرَةَ نَفسَها **في البَيانات**.
// فَلِكُلّ تَعريفٍ هُنا **مِرساةٌ مَقيسَةٌ في المَصدَر**، أَو سَطرٌ
// يَقول لِماذا لا مِرساةَ لَه.
public class ProviderCatalogTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    // ─── ١. التَحميلُ بَوّابَةٌ لا نَقل ───────────────────────────────

    [Fact]
    public void The_catalog_loads_and_every_definition_passes_the_validator()
    {
        var defs = ProviderCatalog.Definitions;
        Assert.True(defs.Count == 7,
            $"أَداة عَمياء: حُمِّلَ {defs.Count} تَعريفاً — والمَقيس ٧.");

        foreach (var d in defs)
            Assert.True(ProviderDefinitionValidator.IsValid(d),
                $"«{d.Slug}» لا يَجتاز المُصادَقَة: " +
                string.Join(" | ", ProviderDefinitionValidator.Validate(d).Select(v => v.Code)));

        Assert.Equal(defs.Count, defs.Select(d => d.Slug).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_index_and_the_files_on_disk_are_the_same_set()
    {
        var dir = Path.Combine(RepoRoot, "libs", "core",
            "ACommerce.Platform.Providers", "Definitions");
        Assert.True(Directory.Exists(dir), $"مُجَلَّدُ التَعريفات مَفقود: {dir}");

        var onDisk = Directory.GetFiles(dir, "*.provider.json")
            .Select(f => Path.GetFileName(f).Replace(".provider.json", "", StringComparison.Ordinal))
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();

        var loaded = ProviderCatalog.Definitions
            .Select(d => d.Slug).OrderBy(s => s, StringComparer.Ordinal).ToArray();

        Assert.True(onDisk.Length > 0, "أَداة عَمياء: صِفرُ مِلَفٍّ عَلى القُرص.");
        Assert.Equal(onDisk, loaded);
    }

    // ─── ٢. كُلُّ نَوعٍ في المَعجَمِ يُعلِنُه مِلَفّ ──────────────────

    [Fact]
    public void Every_enforced_credential_kind_is_declared_by_at_least_one_definition()
    {
        var declared = ProviderCatalog.Definitions
            .Select(d => d.Credential.Kind)
            .Concat(ProviderCatalog.Definitions.SelectMany(d => d.Credential.Fields).Select(f => f.Kind))
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(declared.Count > 0, "أَداة عَمياء: صِفرُ نَوعٍ مُعلَن.");

        var orphans = CredentialKinds.All.Where(k => !declared.Contains(k)).ToArray();
        Assert.True(orphans.Length == 0,
            "نَوعٌ في المَعجَمِ المُلزِمِ لا يُعلِنُه مِلَفُّ مُزَوِّدٍ واحِد — " +
            "وهذا هُوَ بِعَينِه نُمُوُّ المَعجَمِ بِالخَيال:\n  " + string.Join("\n  ", orphans));
    }

    [Fact]
    public void No_definition_declares_a_kind_whose_vault_is_not_built_yet()
    {
        var used = ProviderCatalog.Definitions
            .Select(d => d.Credential.Kind)
            .Concat(ProviderCatalog.Definitions.SelectMany(d => d.Credential.Fields).Select(f => f.Kind))
            .ToArray();

        Assert.True(used.Length > 0, "أَداة عَمياء: صِفرُ نَوعٍ مَقروء.");

        var smuggled = used.Where(CredentialKinds.NotYetInVocabulary.Contains).Distinct().ToArray();
        Assert.True(smuggled.Length == 0,
            "مِلَفُّ تَعريفٍ يُعلِن نَوعاً لَم تُبنَ خِزانَتُه — والقارِئُ " +
            "كانَ سَيُفشِلُ الإقلاع:\n  " + string.Join("\n  ", smuggled));
    }

    // ─── ٣. لِكُلّ تَعريفٍ مِرساةٌ حَيَّة ─────────────────────────────

    private sealed record Anchor(string Slug, string SourceFile, string Needle, string WhyAr);

    /// <summary>المِرساةُ سَطرٌ فِعليٌّ في المَصدَرِ يُثبِتُ أَنّ
    /// التَعريفَ يَصِف شَيئاً حَيّاً — لا اسماً في مِلَفّ.</summary>
    private static readonly Anchor[] Anchors =
    {
        new("mock_payments",
            "libs/kits/Payments/ACommerce.Kit.Payments.Core/MockPaymentProvider.cs",
            "AddSingleton<IPaymentProvider, MockPaymentProvider>",
            "التَنفيذُ القائِمُ المُسَجَّلُ بِلا شَرطٍ في Program.cs."),

        new("mock_maps",
            "libs/kits/Maps/ACommerce.Kit.Maps.Core/MockMapsProvider.cs",
            "AddSingleton<IMapsProvider, MockMapsProvider>",
            "التَنفيذُ القائِمُ المُسَجَّلُ بِلا شَرطٍ في Program.cs."),

        new("mock_delivery",
            "libs/kits/Delivery/ACommerce.Kit.Delivery.Core/MockDeliveryProvider.cs",
            "AddSingleton<IDeliveryProvider, MockDeliveryProvider>",
            "التَنفيذُ القائِمُ المُسَجَّلُ بِلا شَرطٍ في Program.cs."),

        new("local_files",
            "libs/kits/Files/ACommerce.Kit.Files.Core/LocalFileStorage.cs",
            "AddSingleton<IFileStorage, LocalFileStorage>",
            "التَنفيذُ القائِمُ المُسَجَّلُ بِلا شَرطٍ في Program.cs."),

        new("paypal_subscriptions",
            "apps/V1.App/Program.cs",
            "AddPayPalSubscriptions",
            "مُزَوِّدُ فَوتَرَةِ المَنَصَّةِ القائِم — سِرُّه سِرُّنا لا سِرُّ مُستَأجِر."),

        new("paddle_billing",
            "apps/V1.App/Program.cs",
            "AddPaddleBilling",
            "مُزَوِّدُ فَوتَرَةٍ ثانٍ بِجِوارِ الأَوَّلِ — قائِمٌ ومَقيس."),

        // ← الوَحيدُ بِلا مِرساةِ كود، وذلكَ **هُوَ** الدَعوى.
        new("moyasar_hosted",
            "", "",
            "رابِطٌ مُستَضافٌ لا يَحتاج سَطرَ كودٍ واحِداً: صَفحَةُ الدَفعِ " +
            "تُصَيِّرُ رابِطاً مُخَزَّناً، والزَبونُ يَدفَعُ عِندَ مُيَسِّر " +
            "إلى حِسابِ التاجِر. وهذا بِعَينِه ما يُثبِتُه التَصميم: " +
            "نَوعٌ لا يَحمِلُ سِرّاً يُكَلِّف ‏~25 سَطرَ بَياناتٍ وصِفرَ سَطرِ كود."),
    };

    [Fact]
    public void Every_definition_has_a_live_anchor_or_a_written_reason()
    {
        Assert.Equal(ProviderCatalog.Definitions.Count, Anchors.Length);

        var catalogSlugs = ProviderCatalog.Definitions.Select(d => d.Slug)
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var anchorSlugs = Anchors.Select(a => a.Slug)
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();
        Assert.Equal(catalogSlugs, anchorSlugs);

        var checkedAnchors = 0;
        var breaches = new List<string>();

        foreach (var a in Anchors)
        {
            Assert.True(a.WhyAr.Length > 30, $"«{a.Slug}» بِلا سَبَبٍ مَقروء.");

            if (a.SourceFile.Length == 0) continue;   // بِلا مِرساةِ كود — بِسَبَبِها

            var path = Path.Combine(RepoRoot, a.SourceFile.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) { breaches.Add($"{a.Slug}: مِلَفُّ المِرساةِ مَفقود {a.SourceFile}"); continue; }

            checkedAnchors++;
            if (!File.ReadAllText(path).Contains(a.Needle, StringComparison.Ordinal))
                breaches.Add($"{a.Slug}: «{a.Needle}» لَم يَعُد في {a.SourceFile}");
        }

        Assert.True(checkedAnchors == 6,
            $"أَداة عَمياء: فُحِصَت {checkedAnchors} مِرساة — والمَقيس ٦.");
        Assert.True(breaches.Count == 0,
            "تَعريفُ مُزَوِّدٍ بِلا تَنفيذٍ حَيّ — مَقبَرَةٌ في البَيانات:\n  "
            + string.Join("\n  ", breaches));
    }

    // ─── ٤. ما يُرسَم لِلمُستَأجِر ───────────────────────────────────

    [Fact]
    public void Only_a_provider_a_tenant_can_actually_fill_is_offered()
    {
        // ‏`none` وَصفٌ لِما هُوَ قائِم، و`platform_key` مِن جَيبِنا —
        // فَلا يُعرَضُ واحِدٌ مِنهُما لِلرَبط.
        foreach (var d in ProviderCatalog.Definitions)
        {
            var expected = d.Credential.Kind is not
                (CredentialKinds.None or CredentialKinds.PlatformKey);
            Assert.Equal(expected, d.IsTenantBindable);
        }

        var bindable = ProviderCatalog.BindableCapabilities;
        Assert.Equal(new[] { ProviderCapabilities.Payments }, bindable);

        var offered = ProviderCatalog.TenantBindable(ProviderCapabilities.Payments);
        Assert.Equal(new[] { "moyasar_hosted" }, offered.Select(d => d.Slug).ToArray());

        // وقُدرَةٌ بِلا مُزَوِّدٍ مُتاحٍ لا تُرسَم إطلاقاً — ولا «قَريباً».
        foreach (var c in ProviderCapabilities.All.Where(c => c != ProviderCapabilities.Payments))
            Assert.Empty(ProviderCatalog.TenantBindable(c));
    }

    // ─── ٥. تَفاصيلُ الرابِطِ المُستَضاف ─────────────────────────────

    [Fact]
    public void The_hosted_link_provider_declares_its_fence_and_its_physical_goods_limit()
    {
        var d = ProviderCatalog.Find("moyasar_hosted");
        Assert.NotNull(d);
        Assert.Equal(ProviderCapabilities.Payments, d!.Capability);
        Assert.Equal(CredentialKinds.HostedLink, d.Credential.Kind);

        // مُستَأجِرٌ يَبيع مُحتَوىً رَقَمِيّاً يُسقِط إعفاءَ الدَفعِ
        // لِلبِناءِ المُشتَرَكِ كُلِّه — فَالحَدُّ حَقلٌ يُصادَق.
        Assert.True(d.PhysicalGoodsOnly);

        var field = Assert.Single(d.Credential.Fields);
        Assert.Equal("invoice_url", field.Code);
        Assert.Equal(new[] { "moyasar.com" }, field.HostAllowlist.ToArray());
        Assert.True(field.IsRequired);
        Assert.Null(d.Webhook);
        Assert.Equal("مُيَسِّر — رابِط دَفع", ProviderText.Get(d.Label, "ar"));
    }

    [Fact]
    public void Both_platform_billing_providers_carry_no_tenant_field_at_all()
    {
        foreach (var slug in new[] { "paypal_subscriptions", "paddle_billing" })
        {
            var d = ProviderCatalog.Find(slug);
            Assert.NotNull(d);
            Assert.Equal(CredentialKinds.PlatformKey, d!.Credential.Kind);
            Assert.Empty(d.Credential.Fields);
            Assert.False(d.IsTenantBindable);
        }
    }

    // ─── ٦. سُقوطُ التَوطين ──────────────────────────────────────────

    [Fact]
    public void Localized_text_is_an_open_map_and_falls_back_to_arabic()
    {
        var d = ProviderCatalog.Find("moyasar_hosted")!;
        Assert.Equal(ProviderText.Get(d.Label, "ar"), ProviderText.Get(d.Label, "en"));
        Assert.Equal(ProviderText.Get(d.Label, "ar"), ProviderText.Get(d.Label, "fr"));
        Assert.Equal("", ProviderText.Get(ProviderText.Empty, "ar"));

        foreach (var def in ProviderCatalog.Definitions)
        {
            Assert.True(ProviderText.HasArabic(def.Label), $"«{def.Slug}» بِلا تَسمِيَةٍ عَرَبِيَّة.");
            Assert.True(ProviderText.HasArabic(def.Description), $"«{def.Slug}» بِلا وَصفٍ عَرَبيّ.");
            Assert.True(ProviderText.HasArabic(def.Revocation), $"«{def.Slug}» بِلا نَصّ سَحب.");
        }
    }

    // ─── ٦-ب. مَسارُ الإضافَة — بَياناتٌ لا كود ──────────────────────
    //
    // **هذا هُوَ اختِبارُ نَجاحِ التَجريدِ لا دَعواه**: مُزَوِّدٌ جَديدٌ
    // بِنَوعٍ لا يَحمِلُ سِرّاً يُكَلِّف مِلَفَّ بَياناتٍ وسَطراً في
    // الفِهرِس — و**صِفرَ سَطرِ كود**. فَلا `Program.cs` ولا خِزانَةَ
    // ولا شاشَةَ ولا نُقطَةَ كِتابَةٍ ولا تَدقيق.
    //
    // ويُقاسُ بِالنَفي: لَو ذَكَرَ مَوضِعٌ واحِدٌ في المَصدَرِ سلاجَ
    // مُزَوِّدٍ بِعَينِه، لَاحتاجَ المُزَوِّدُ التالي تَعديلَ ذلكَ
    // المَوضِع — وهُوَ بِعَينِه ما يَجعَل الكاتالوجَ زينَةً.

    [Fact]
    public void No_source_file_outside_the_catalogue_names_a_provider_slug()
    {
        var slugs = ProviderCatalog.Definitions.Select(d => d.Slug).ToArray();
        Assert.True(slugs.Length > 0, "أَداة عَمياء: صِفرُ سلاجٍ مَفحوص.");

        var scanned = 0;
        var breaches = new List<string>();

        foreach (var root in new[] { "libs", "apps" })
        {
            var dir = Path.Combine(RepoRoot, root);
            if (!Directory.Exists(dir)) continue;

            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var norm = f.Replace('\\', '/');
                if (norm.Contains("/obj/", StringComparison.Ordinal) ||
                    norm.Contains("/bin/", StringComparison.Ordinal)) continue;
                if (!norm.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                    !norm.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)) continue;

                scanned++;
                var text = File.ReadAllText(f);
                foreach (var slug in slugs)
                    if (text.Contains(slug, StringComparison.Ordinal))
                        breaches.Add($"{Path.GetRelativePath(RepoRoot, f).Replace('\\', '/')} ← «{slug}»");
            }
        }

        Assert.True(scanned > 300, $"أَداة عَمياء: مُسِحَ {scanned} مِلَفّاً — والمَقيس مِئات.");
        Assert.True(breaches.Count == 0,
            "سلاجُ مُزَوِّدٍ مَكتوبٌ في الكود — فَالمُزَوِّدُ التالي يَحتاج تَعديلَه:\n  "
            + string.Join("\n  ", breaches));
    }

    [Fact]
    public void A_new_hosted_link_provider_needs_only_a_data_file()
    {
        // مُزَوِّدٌ وَهمِيٌّ يُكتَبُ كَما يُكتَبُ الحَقيقيّ — ويَمُرّ
        // بِنَفسِ القارِئِ ونَفسِ المُصادِق، ويَخرُج «يَربِطُه
        // مُستَأجِر» بِلا سَطرِ كودٍ واحِد.
        const string json = """
            {
              "slug": "paytabs_paylink",
              "capability": "payments",
              "label": { "ar": "بَي تابس — رابِط دَفع", "en": null },
              "description": { "ar": "رابِطُ دَفعٍ تُنشِئُه مِن لَوحَتِك.", "en": null },
              "docsUrl": null,
              "physicalGoodsOnly": true,
              "credential": {
                "kind": "hosted_link",
                "fields": [
                  { "code": "paylink_url",
                    "kind": "hosted_link",
                    "label": { "ar": "رابِط الدَفع", "en": null },
                    "isRequired": true,
                    "pattern": null,
                    "hostAllowlist": ["paytabs.com"] }
                ]
              },
              "webhook": null,
              "revocation": { "ar": "احذِف الرابِطَ مِن لَوحَتِك ثُمَّ اسحَب الرَبطَ هُنا.", "en": null }
            }
            """;

        var d = ProviderDefinitionLoader.ParseDefinition(json);

        Assert.Empty(ProviderDefinitionValidator.Validate(d));
        Assert.True(d.IsTenantBindable);
        Assert.Equal(ProviderCapabilities.Payments, d.Capability);
        Assert.Equal(CredentialKinds.HostedLink, d.HighestFieldKind);

        // والقيمَةُ تُفحَص بِسياجِها هي، لا بِسياجِ جارِها.
        var field = d.Credential.Fields[0];
        Assert.Null(ProviderValueValidator.Refuse(field, "https://paytabs.com/pay/1"));
        Assert.Equal(ProviderValueValidator.HostNotAllowed,
            ProviderValueValidator.Refuse(field, "https://moyasar.com/i/1"));
    }

    // ─── ٧. القارِئُ يَرفُض ما لا يَعرِف ─────────────────────────────

    [Fact]
    public void An_unknown_key_in_a_definition_file_is_an_explicit_error()
    {
        const string json = """
            {
              "slug": "x_provider",
              "capability": "payments",
              "label": { "ar": "س" },
              "description": { "ar": "و" },
              "revocation": { "ar": "ر" },
              "credential": { "kind": "none", "fields": [] },
              "surpriseKey": true
            }
            """;

        Assert.ThrowsAny<Exception>(() => ProviderDefinitionLoader.ParseDefinition(json));
    }

    [Fact]
    public void A_well_formed_definition_parses_through_the_same_reader()
    {
        const string json = """
            {
              "slug": "x_provider",
              "capability": "payments",
              "label": { "ar": "س", "en": null },
              "description": { "ar": "و", "en": null },
              "docsUrl": null,
              "physicalGoodsOnly": false,
              "credential": { "kind": "none", "fields": [] },
              "webhook": null,
              "revocation": { "ar": "ر", "en": null }
            }
            """;

        var d = ProviderDefinitionLoader.ParseDefinition(json);
        Assert.Equal("x_provider", d.Slug);
        Assert.Equal(CredentialKinds.None, d.Credential.Kind);
        Assert.True(ProviderDefinitionValidator.IsValid(d));
    }
}
