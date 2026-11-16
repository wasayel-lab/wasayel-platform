using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ACommerce.Kit.Roles;
using ACommerce.Platform.Providers;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>بَصَماتُ التَعريفاتِ — تَوصيفٌ قَبلَ أَن يَحمِلَ تَعريفٌ مِتراً</b> (‏2026-11-13، الموجَة ١٣ W13-6·١٦؛ التَصميم §7 «صِفرُ تَغييرٍ في وَثائِقِ
/// المُستَأجِرِ والبايتِ المُصَيَّر»، وقِراءَةُ التَعريفات §١·٧). البُنودُ ١٦–١٨ تُضيفُ حَقلَ <c>meter</c> إلى تَعريفاتِ المُزَوِّدِ والدَورِ والتَدَفُّق؛ وهذا المِلَفُّ
/// يُثَبِّتُ قَبلَها ما لا يَجوزُ أَن يَتَغَيَّرَ بِها: أَربَعُ بَصَماتٍ قيسَت عَلى نُسخَةٍ مُرَقَّعَةٍ خارِجَ المُستَودَعِ فَثَبَتَت، وتَصيرُ هُنا حارِساً يَحمَرُّ إن
/// تَسَرَّبَ الحَقلُ إلى ما يُجَسَّدُ لِلمُستَأجِرِ أَو يُعرَضُ لَه.</para>
///
/// <para><b>البَصمَةُ</b> أَوَّلُ ستّةَ عَشَرَ مِحرَفاً سِتَّ عَشرِيّاً مِن ‏SHA-256 عَلى تَسلسُلِ <c>System.Text.Json</c> الافتِراضيِّ بِلا مَسافات — الأَداةُ نَفسُها
/// الَّتي قاسَت القِيَمَ الأَربَعَ عِندَ التَصميم، وتُقاسُ هُنا قَبلَ أَن يُوثَقَ بِها (القاعِدَة ١٠): حَرفٌ واحِدٌ في المُدخَلِ يُغَيِّرُها.</para>
///
/// <para><b>يُكتَبُ ويَخضَرُّ ويُودَعُ وَحدَه قَبلَ الحَقل، ولا يُمَسُّ حَرفٌ مِنه بَعدَه</b> (القاعِدَة ٣) — وبُرهانُه بَصمَةُ المِلَفِّ قَبلُ وبَعدُ في رِسالَةِ
/// كُلِّ بَندٍ يَمَسُّ التَعريفات.</para>
/// </summary>
public class DefinitionFingerprintCharacterizationTests(ITestOutputHelper output)
{
    /// <summary>أَوَّلُ ‏16 مِحرَفاً مِن ‏SHA-256 عَلى التَسلسُلِ الافتِراضيّ.</summary>
    private static string Fingerprint(object o) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(o, new JsonSerializerOptions { WriteIndented = false }))))[..16];

    /// <summary>
    /// <para><b>وَثيقَةُ الدَورِ المُؤَلَّفِ المَبذورَة</b> — حَرفُ <c>AppearanceBaselineSeeder.AuthoredRoleJson("mandoob")</c> كَما قيسَ بِه: الدَورُ المُعتَمَدُ
    /// الَّذي تَسرُدُه شاشَةُ أَدوارِ المَتجَرِ ويُجَسَّدُ فَوقَ أَدوارِه المُخَزَّنَة. مَنسوخٌ هُنا لا مَقروءٌ مِن البَذر: التَوصيفُ يُثَبِّتُ ما يَصيرُ إلَيه نَصٌّ
    /// بِعَينِه، لا ما يَكتُبُه البَذرُ غَداً.</para>
    /// </summary>
    private const string SeededAuthoredRole = """
        {
          "slug": "mandoob",
          "icon": "🧾",
          "homeRoute": "",
          "label": { "ar": "mandoob", "en": null },
          "description": { "ar": "دَورٌ مُؤَلَّفٌ — عَيِّنَةُ بِنيَة.", "en": null },
          "permissions": [ "listing.browse" ],
          "fields": [],
          "composition": {
            "home": "defaultHome",
            "createListing": "defaultCreateForm",
            "nav": "defaultNav",
            "explore": "defaultExplore",
            "publicProfile": null,
            "extras": []
          },
          "dealPatternAffinity": null
        }
        """;

    /// <summary>ما يُعرَضُ مِن تَعريفِ المُزَوِّد — الحُقولُ الَّتي تَقرَؤُها شاشَةُ الاستوديو وسَطحُ الرَبطِ بِأَسمائِها (السلاجُ والقُدرَةُ والتَسمِيَةُ والوَصفُ
    /// وقابِلِيَّةُ الرَبطِ ونَوعُ الاعتِماد).</summary>
    private static object ProviderDisplay(IEnumerable<ProviderDefinition> definitions) =>
        definitions.Select(d => new { d.Slug, d.Capability, d.Label, d.Description, d.IsTenantBindable, d.Credential.Kind }).ToList();

    [Fact]
    public void The_role_catalogue_templates_keep_their_fingerprint()
    {
        var fingerprint = Fingerprint(RoleCatalog.All);
        output.WriteLine($"[fingerprint] RoleCatalog.All: قَوالِب {RoleCatalog.All.Count} · تَعريفات {RoleCatalog.Definitions.Count} · {fingerprint}");
        Assert.Equal(10, RoleCatalog.All.Count);
        Assert.Equal(10, RoleCatalog.Definitions.Count);
        Assert.Equal("60107FDD2430AFAF", fingerprint);
    }

    [Fact]
    public void The_ten_instantiated_catalogue_roles_keep_their_fingerprint()
    {
        var roles = RoleCatalog.All.Select((t, i) => RoleCatalog.InstantiateRole(t, i)).ToList();
        var fingerprint = Fingerprint(roles);
        output.WriteLine($"[fingerprint] InstantiateRole ×{roles.Count}: {fingerprint}");
        Assert.Equal(10, roles.Count);
        Assert.Equal("34E8C9352BD85474", fingerprint);
    }

    [Fact]
    public void The_seeded_authored_role_materialises_with_the_same_fingerprint_and_passes_both_doors()
    {
        var doc = new TenantRoleDefinition
        {
            Id = "mandoob", Slug = "mandoob", DefinitionJson = SeededAuthoredRole,
            Status = TenantRoleStatuses.Approved, CreatedAt = new DateTime(2026, 8, 11, 14, 54, 0, DateTimeKind.Utc),
        };
        var set = TenantRoleSet.FromDocuments("theme-demo", [doc]);
        var vendor = RoleCatalog.InstantiateRole(RoleCatalog.Find("vendor")!, 0);
        var materialised = set.Materialize(new List<Role> { vendor });
        var fingerprint = Fingerprint(materialised);
        var writeDoor = RoleDefinitionValidator.ValidateTenantDefinition(set.TenantAuthored[0]).Select(v => v.Code).ToArray();
        var readBelt = RoleDefinitionValidator.ValidateStoredTenantDefinition(set.TenantAuthored[0]).Select(v => v.Code).ToArray();
        output.WriteLine($"[fingerprint] mandoob: مَقروءَة {set.TenantAuthored.Count} · مُجَسَّدَة {materialised.Count} · " +
                         $"بابُ الكِتابَة [{string.Join(",", writeDoor)}] · حِزامُ القِراءَة [{string.Join(",", readBelt)}] · {fingerprint}");

        Assert.Single(set.TenantAuthored);
        Assert.Equal(2, materialised.Count);
        Assert.Empty(writeDoor);
        Assert.Empty(readBelt);
        Assert.Equal("A2CD5A5254EAA12F", fingerprint);
        // والتَكافُؤُ الصِفرِيُّ بِالمَرجِع: مَتجَرٌ بِلا وَثيقَةٍ يَقرَأُ الكاتالوجَ نَفسَه.
        Assert.Same(RoleCatalog.Definitions, TenantRoleSet.FromDocuments("x", Array.Empty<TenantRoleDefinition>()).Definitions);
    }

    [Fact]
    public void The_provider_display_keeps_its_fingerprint()
    {
        var fingerprint = Fingerprint(ProviderDisplay(ProviderCatalog.Definitions));
        output.WriteLine($"[fingerprint] عَرضُ المُزَوِّدين: تَعريفات {ProviderCatalog.Definitions.Count} · رُموزُ المُصادِق {ProviderDefinitionValidator.Codes.Count} · {fingerprint}");
        Assert.Equal(8, ProviderCatalog.Definitions.Count);
        Assert.Equal(12, ProviderDefinitionValidator.Codes.Count);
        Assert.Equal("802A9F21D2611019", fingerprint);
    }

    /// <summary><b>الأَداةُ تُقاسُ قَبلَ أَن يُوثَقَ بِها</b> (القاعِدَة ١٠): حَرفٌ واحِدٌ في وَصفِ مُزَوِّدٍ، أَو تَرتيبٌ آخَرُ لِلأَدوار، أَو دَورٌ مُجَسَّدٌ
    /// زائِد — كُلٌّ يُغَيِّرُ البَصمَة. فَبَصمَةٌ ثابِتَةٌ بَعدَ البُنودِ ١٦–١٨ دَليلٌ لا صُدفَة.</summary>
    [Fact]
    public void The_fingerprint_moves_on_a_one_character_change()
    {
        var first = ProviderCatalog.Definitions[0];
        var touched = ProviderCatalog.Definitions.Skip(1)
            .Prepend(first with { Description = new Dictionary<string, string?> { ["ar"] = ProviderText.Get(first.Description, "ar") + ".", ["en"] = null } })
            .ToList();
        var reordered = Fingerprint(RoleCatalog.All.Reverse().ToList());
        var extra = Fingerprint(RoleCatalog.All.Select((t, i) => RoleCatalog.InstantiateRole(t, i + 1)).ToList());
        var shifted = Fingerprint(ProviderDisplay(touched));
        output.WriteLine($"[fingerprint] الحَقن: وَصفٌ بِحَرفٍ زائِد {shifted} · قَوالِبُ مَعكوسَة {reordered} · تَرتيبُ تَجسيدٍ مُزاح {extra}");
        Assert.NotEqual("802A9F21D2611019", shifted);
        Assert.NotEqual("60107FDD2430AFAF", reordered);
        Assert.NotEqual("34E8C9352BD85474", extra);
    }
}
