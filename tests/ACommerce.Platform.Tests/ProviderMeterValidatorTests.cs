using System.Text.Json;
using ACommerce.Platform.Providers;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>G7 — مِترُ المُزَوِّد</b> (‏2026-11-13، الموجَة ١٣ W13-6·١٦ — ‏ADR-051 §٢٠؛ التَصميم §7 و§12): لِكُلِّ رَمزِ خَرقٍ اختِبارٌ موجِبٌ وسالِب
/// (القاعِدَة ٤)، والمَصفوفَةُ كامِلَةً — كُلُّ شَكلِ اعتِمادٍ مِن المَعجَمِ المُلزِمِ × كُلُّ قيمَةِ حُضور، صَفّاً صَفّاً — بِجَدوَلٍ مَكتوبٍ هُنا بِاليَدِ لا مَنسوخٍ
/// مِن المُصادِق (فَلا يَخضَرُّ اختِبارٌ يُعيدُ قَولَ ما يَختَبِرُه). والكاتالوجُ يُفحَصُ تَعريفاً تَعريفاً ويَطبَعُ عَدَدَه، ويَفشَلُ عَلى الصِفر.</para>
/// </summary>
public class ProviderMeterValidatorTests(ITestOutputHelper output)
{
    private static IReadOnlyDictionary<string, string?> Ar(string text) =>
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["ar"] = text, ["en"] = null };

    private static ProviderFieldDefinition Field(string code, string kind) => new()
    {
        Code = code, Kind = kind, Label = Ar("حَقل"), IsRequired = true,
        HostAllowlist = CredentialKinds.IsLink(kind) ? new[] { "example.test" } : Array.Empty<string>(),
    };

    /// <summary>
    /// <para><b>أَشكالُ الاعتِماد</b> — جانِبُ المَصفوفَةِ الأَوَّل، مِن المَعجَمِ المُلزِمِ (<see cref="CredentialKinds.All"/>) نَوعاً نَوعاً، و<c>platform_key</c>
    /// مَرَّتَين: بِلا حَقل، وتَحتَه حَقلا بائِعٍ سِرِّيّانِ (شَكلُ حِسابِ الشَحن: رَمزُ تَحديثٍ ورَمزُ وارِد). والمَقبولُ في كُلِّ شَكلٍ مَكتوبٌ هُنا بِاليَد.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (Func<ProviderDefinition> Build, string[] Allowed)> Shapes =
        new Dictionary<string, (Func<ProviderDefinition>, string[])>(StringComparer.Ordinal)
        {
            [CredentialKinds.None] = (() => Shape(CredentialKinds.None), ["none"]),
            [CredentialKinds.HostedLink] = (() => Shape(CredentialKinds.HostedLink, Field("invoice_url", CredentialKinds.HostedLink)), ["store_binding", "none"]),
            [CredentialKinds.IssuedSecret] = (() => Shape(CredentialKinds.IssuedSecret, Field("webhook_secret", CredentialKinds.IssuedSecret)), ["store_binding", "none"]),
            [CredentialKinds.SecretKey] = (() => Shape(CredentialKinds.SecretKey, Field("secret_key", CredentialKinds.SecretKey)), ["store_binding", "none"]),
            [CredentialKinds.PlatformKey] = (() => Shape(CredentialKinds.PlatformKey), ["platform", "none"]),
            [PlatformKeyWithVendorFields] = (() => Shape(CredentialKinds.PlatformKey,
                Field("refresh_token", CredentialKinds.SecretKey), Field("webhook_secret", CredentialKinds.IssuedSecret)), ["vendor_binding", "none"]),
        };

    private const string PlatformKeyWithVendorFields = "platform_key+vendor_fields";

    private static ProviderDefinition Shape(string kind, params ProviderFieldDefinition[] fields) => new()
    {
        Slug = "meter_probe",
        Capability = ProviderCapabilities.Payments,
        Label = Ar("مُزَوِّدُ مِجَسّ"),
        Description = Ar("وَصف."),
        Revocation = Ar("سَحب."),
        Credential = new ProviderCredentialDefinition { Kind = kind, Fields = fields },
    };

    private static ProviderDefinition With(ProviderDefinition d, string? presence) => d with { Meter = new ProviderMeterDefinition { Presence = presence } };

    private static string[] CodesOf(ProviderDefinition d) => ProviderMeterValidator.Validate(d).Select(v => v.Code).ToArray();

    /// <summary>صُفوفُ المَصفوفَة: كُلُّ شَكلٍ × كُلُّ قيمَةٍ في المَعجَم — وما يُنتَظَرُ مِن الجَدوَلِ المَكتوبِ أَعلاه.</summary>
    public static IEnumerable<object[]> MatrixRows() =>
        from shape in Shapes.Keys
        from presence in ProviderMeterPresences.All
        select new object[] { shape, presence, Shapes[shape].Allowed.Contains(presence) };

    // ─── المَعجَمُ والرُموز ─────────────────────────────────────────────

    [Fact]
    public void Three_codes_and_four_presences_are_declared_and_none_repeats()
    {
        Assert.Equal(new[] { "meter_missing", "meter_presence_out_of_vocabulary", "meter_presence_contradicts_credential" }, ProviderMeterValidator.Codes);
        Assert.Equal(new[] { "store_binding", "vendor_binding", "platform", "none" }, ProviderMeterPresences.All);
        Assert.Equal(ProviderMeterPresences.All.Count, ProviderMeterPresences.All.Distinct(StringComparer.Ordinal).Count());
        // والمُصادِقُ القائِمُ لا يُمَسّ: رُموزُه اثنا عَشَر (ProviderDefinitionValidatorTests:61-62) ولا رَمزَ مِتر بَينَها.
        Assert.Equal(12, ProviderDefinitionValidator.Codes.Count);
        Assert.Empty(ProviderDefinitionValidator.Codes.Intersect(ProviderMeterValidator.Codes));
    }

    // ─── ١. meter_missing ─────────────────────────────────────────────

    [Fact]
    public void meter_missing_fires_on_an_absent_meter_or_an_empty_presence_and_only_then()
    {
        var bare = Shape(CredentialKinds.None);
        Assert.Equal(new[] { "meter_missing" }, CodesOf(bare));
        Assert.Equal(new[] { "meter_missing" }, CodesOf(bare with { Meter = new ProviderMeterDefinition() }));
        Assert.Equal(new[] { "meter_missing" }, CodesOf(With(bare, "")));
        Assert.Equal(new[] { "meter_missing" }, CodesOf(With(bare, "  ")));
        Assert.Contains("meter_probe", ProviderMeterValidator.Validate(bare)[0].MessageAr, StringComparison.Ordinal);

        Assert.DoesNotContain("meter_missing", CodesOf(With(bare, "none")));
        Assert.True(ProviderMeterValidator.IsValid(With(bare, "none")));
    }

    // ─── ٢. meter_presence_out_of_vocabulary ───────────────────────────

    [Theory]
    [InlineData("operations")]
    [InlineData("per_call")]
    [InlineData("Store_Binding")]
    [InlineData("store-binding")]
    public void meter_presence_out_of_vocabulary_fires_on_any_value_outside_the_four(string presence)
    {
        var tenantBindable = Shapes[CredentialKinds.SecretKey].Build();
        Assert.Equal(new[] { "meter_presence_out_of_vocabulary" }, CodesOf(With(tenantBindable, presence)));
        Assert.Contains(presence, ProviderMeterValidator.Validate(With(tenantBindable, presence))[0].MessageAr, StringComparison.Ordinal);
    }

    [Fact]
    public void meter_presence_out_of_vocabulary_never_fires_on_a_value_of_the_lexicon()
    {
        foreach (var shape in Shapes.Values)
            foreach (var presence in ProviderMeterPresences.All)
                Assert.DoesNotContain("meter_presence_out_of_vocabulary", CodesOf(With(shape.Build(), presence)));
    }

    // ─── ٣. meter_presence_contradicts_credential ──────────────────────

    [Fact]
    public void meter_presence_contradicts_credential_fires_on_a_store_key_counted_as_the_platforms_and_not_on_its_own_cell()
    {
        var tenantBindable = Shapes[CredentialKinds.SecretKey].Build();
        Assert.Equal(new[] { "meter_presence_contradicts_credential" }, CodesOf(With(tenantBindable, "platform")));
        Assert.Contains("store_binding", ProviderMeterValidator.Validate(With(tenantBindable, "platform"))[0].MessageAr, StringComparison.Ordinal);
        Assert.Empty(CodesOf(With(tenantBindable, "store_binding")));
    }

    /// <summary><b>المَصفوفَةُ كامِلَةً، خانَةً خانَة</b> — صَفٌّ لِكُلِّ شَكلٍ × قيمَة: المَقبولُ بِلا خَرق، والمَردودُ بِرَمزِ التَناقُضِ وَحدَه.</summary>
    [Theory]
    [MemberData(nameof(MatrixRows))]
    public void Matrix_row(string shape, string presence, bool allowed)
    {
        var codes = CodesOf(With(Shapes[shape].Build(), presence));
        if (allowed) Assert.Empty(codes);
        else Assert.Equal(new[] { "meter_presence_contradicts_credential" }, codes);
    }

    /// <summary><b>حارِسُ العَمى لِلمَصفوفَة</b>: الأَشكالُ تُغَطّي المَعجَمَ المُلزِمَ كُلَّه (نَوعٌ يَدخُلُه بِلا صَفٍّ هُنا يُحَمِّرُ)، ولِكُلِّ شَكلٍ مَقبولٌ
    /// ومَردود، ويُطبَعُ العَدّ.</summary>
    [Fact]
    public void The_matrix_covers_every_bound_credential_kind_and_every_presence_with_both_outcomes_per_cell()
    {
        var rows = MatrixRows().ToList();
        var accepted = rows.Count(r => (bool)r[2]);
        output.WriteLine($"[G7] المَصفوفَة: أَشكال {Shapes.Count} × حُضور {ProviderMeterPresences.All.Count} = صُفوف {rows.Count} · مَقبولَة {accepted} · مَردودَة {rows.Count - accepted}");
        Assert.True(rows.Count > 0, "أَداةٌ عَمياء: صِفرُ صَفّ.");
        Assert.Equal(CredentialKinds.All.OrderBy(k => k, StringComparer.Ordinal),
                     Shapes.Keys.Where(k => k != PlatformKeyWithVendorFields).OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (name, shape) in Shapes)
        {
            Assert.Contains(ProviderMeterPresences.All, p => shape.Allowed.Contains(p));
            Assert.Contains(ProviderMeterPresences.All, p => !shape.Allowed.Contains(p));
            // والمُصادِقُ يُدخِلُ الشَكلَ خانَتَه المَكتوبَةَ فيه.
            Assert.Equal(shape.Allowed, ProviderMeterValidator.CellOf(shape.Build()).Allowed);
            output.WriteLine($"[G7]   {name,-28} ⇐ {ProviderMeterValidator.CellOf(shape.Build()).Name,-26} يَقبَل: {string.Join(" · ", shape.Allowed)}");
        }
        // وكُلُّ خانَةٍ في جَدوَلِ المُصادِقِ يَبلُغُها شَكلٌ هُنا — لا خانَةَ نائِمَة.
        Assert.Equal(ProviderMeterValidator.Matrix.Select(c => c.Name).Order(StringComparer.Ordinal),
                     Shapes.Values.Select(s => ProviderMeterValidator.CellOf(s.Build()).Name).Distinct().Order(StringComparer.Ordinal));
    }

    /// <summary><b>خانَةُ حِسابِ الشَحنِ عَلى تَعريفِه الحَقيقيّ</b>: ‏<c>platform_key</c> وتَحتَه حَقلا بائِع — <c>vendor_binding</c> مَقبول، و<c>store_binding</c>
    /// و<c>platform</c> مَردودان (التَصميم §7: «The OTO cell's negative rows are store_binding and platform»).</summary>
    [Fact]
    public void The_shipping_account_definition_is_counted_per_vendor_and_refuses_store_and_platform()
    {
        var oto = ProviderCatalog.Find("oto_vendor_account")!;
        Assert.NotNull(oto);
        Assert.Equal((CredentialKinds.PlatformKey, 2), (oto.Credential.Kind, oto.Credential.Fields.Count));
        Assert.Same(ProviderMeterValidator.PlatformKeyWithVendorFields, ProviderMeterValidator.CellOf(oto));
        Assert.Equal("vendor_binding", oto.Meter?.Presence);
        Assert.Empty(CodesOf(oto));
        Assert.Equal(new[] { "meter_presence_contradicts_credential" }, CodesOf(With(oto, "store_binding")));
        Assert.Equal(new[] { "meter_presence_contradicts_credential" }, CodesOf(With(oto, "platform")));
        Assert.Empty(CodesOf(With(oto, "none")));
    }

    // ─── حارِسُ العَمى لِلرُموز ──────────────────────────────────────────

    /// <summary>كُلُّ رَمزٍ مُعلَنٍ يُنتِجُه مُدخَلٌ خارِق — والقائِمَةُ مِن نَتائِجِ المُدخَلاتِ لا مِن نَصٍّ بِاليَد (نَمَطُ
    /// <c>ProviderDefinitionValidatorTests.Every_declared_code_is_actually_produced_by_some_breach</c>).</summary>
    [Fact]
    public void Every_declared_code_is_actually_produced_by_some_breach()
    {
        var breaching = new[]
        {
            Shape(CredentialKinds.None),
            With(Shape(CredentialKinds.None), "operations"),
            With(Shapes[CredentialKinds.HostedLink].Build(), "platform"),
        };
        var produced = breaching.SelectMany(CodesOf).ToHashSet(StringComparer.Ordinal);
        Assert.True(produced.Count > 0, "أَداةٌ عَمياء: صِفرُ خَرقٍ أُنتِج.");
        var never = ProviderMeterValidator.Codes.Where(c => !produced.Contains(c)).ToArray();
        Assert.True(never.Length == 0, "رَمزُ خَرقٍ مُعلَنٌ لا يُنتِجُه أَيُّ مُدخَل: " + string.Join("، ", never));
    }

    // ─── الكاتالوج ─────────────────────────────────────────────────────

    /// <summary>
    /// <para><b>كُلُّ تَعريفٍ في الكاتالوجِ يَحمِلُ مِتراً تَقبَلُه خانَتُه</b> — والقيمَةُ لِكُلِّ مِلَفٍّ مُثَبَّتَةٌ هُنا، فَتَبديلُها صَفٌّ أَحمَرُ يُرى في
    /// المُراجَعَة. والمُحَمِّلُ نَفسُه يَرفُضُ عِندَ الإقلاع (<c>ProviderDefinitionLoader</c>)، فَهذا الصَفُّ يَقرَأُ ما مَرَّ مِنه.</para>
    /// </summary>
    [Fact]
    public void Every_catalogue_definition_carries_the_meter_its_credential_allows()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mock_payments"] = "none",
            ["local_files"] = "none",
            ["s3_object_storage"] = "platform",
            ["paypal_subscriptions"] = "platform",
            ["paddle_billing"] = "platform",
            ["moyasar_hosted"] = "store_binding",
            ["moyasar_api"] = "store_binding",
            ["oto_vendor_account"] = "vendor_binding",
        };
        var checkedCount = 0;
        var violations = new List<string>();
        foreach (var d in ProviderCatalog.Definitions)
        {
            checkedCount++;
            violations.AddRange(ProviderMeterValidator.Validate(d).Select(v => $"{d.Slug}: {v.Code}"));
            output.WriteLine($"[G7]   {d.Slug,-22} {d.Credential.Kind,-12} حُقول {d.Credential.Fields.Count} ⇐ {ProviderMeterValidator.CellOf(d).Name,-26} حُضور {d.Meter?.Presence}");
        }
        output.WriteLine($"[G7] تَعريفاتٌ مَفحوصَة {checkedCount} · خُروق {violations.Count}");
        Assert.True(checkedCount > 0, "أَداةٌ عَمياء: صِفرُ تَعريفٍ مَفحوص.");
        Assert.Empty(violations);
        Assert.Equal(expected.OrderBy(p => p.Key, StringComparer.Ordinal),
                     ProviderCatalog.Definitions.ToDictionary(d => d.Slug, d => d.Meter!.Presence!).OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    /// <summary><b>الشَكلُ مُغلَق</b>: <c>"meter": { "presence": … }</c> يُقرَأ، ومِفتاحٌ آخَرُ تَحتَه — <c>operations</c> الَّذي أَسقَطَه التَصميم، أَو سِعرٌ —
    /// يُفشِلُ القِراءَة (<c>UnmappedMemberHandling.Disallow</c> عَلى النَوعِ المُتَداخِلِ أَيضاً)، وغِيابُه <c>null</c> يَرُدُّه المُصادِقُ بِـ<c>meter_missing</c>.</summary>
    [Fact]
    public void The_meter_shape_is_closed_to_operations_and_to_any_other_key()
    {
        static string Json(string meter) => $$"""
            {
              "slug": "x_provider",
              "capability": "payments",
              "label": { "ar": "س", "en": null },
              "description": { "ar": "و", "en": null },
              "docsUrl": null,
              "physicalGoodsOnly": false,
              "credential": { "kind": "none", "fields": [] },
              "webhook": null,{{meter}}
              "revocation": { "ar": "ر", "en": null }
            }
            """;

        var read = ProviderDefinitionLoader.ParseDefinition(Json("\n  \"meter\": { \"presence\": \"none\" },"));
        Assert.Equal("none", read.Meter?.Presence);
        Assert.True(ProviderMeterValidator.IsValid(read));

        var absent = ProviderDefinitionLoader.ParseDefinition(Json(""));
        Assert.Null(absent.Meter);
        Assert.Equal(new[] { "meter_missing" }, CodesOf(absent));
        Assert.True(ProviderDefinitionValidator.IsValid(absent), "المُصادِقُ القائِمُ لا يَسأَلُ عَن المِتر — دالَّةٌ مُنفَصِلَةٌ لا عَلَم.");

        Assert.Throws<JsonException>(() => ProviderDefinitionLoader.ParseDefinition(Json("\n  \"meter\": { \"presence\": \"none\", \"operations\": [\"x\"] },")));
        Assert.Throws<JsonException>(() => ProviderDefinitionLoader.ParseDefinition(Json("\n  \"meter\": { \"presence\": \"none\", \"unitPrice\": 1 },")));
    }

    // ─── القاعِدَة ١٦: عَدٌّ لا سِعر ─────────────────────────────────────

    /// <summary><b>لا عُضوَ ولا مِفتاحَ ولا قيمَةَ ولا رِسالَةَ في مِترِ المُزَوِّدِ تُسَمّي سِعراً</b> — بِالمُعجَمَينِ الإنجليزيِّ والعَرَبِيِّ نَفسِهِما
    /// (<c>MeterUnitCatalogTests.PriceIsh</c>/<c>NamesAPrice</c>)، والأَداةُ تُقاسُ أَوَّلاً عَلى اسمٍ مَحقون.</summary>
    [Fact]
    public void No_member_key_value_or_message_of_the_provider_meter_names_a_price()
    {
        Assert.Matches(MeterUnitCatalogTests.PriceIsh, "PresenceFee");
        Assert.True(MeterUnitCatalogTests.NamesAPrice("حُضورٌ بِسِعرِه"));

        var types = new[] { typeof(ProviderMeterPresences), typeof(ProviderMeterValidator), typeof(ProviderMeterDefinition), typeof(ProviderMeterCell), typeof(ProviderMeterViolation) };
        var names = MeterUnitCatalogTests.MemberNames(types);

        var dir = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "core", "ACommerce.Platform.Providers", "Definitions");
        var data = new List<string>();
        foreach (var file in Directory.GetFiles(dir, "*.provider.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var meter = doc.RootElement.GetProperty("meter");
            foreach (var p in meter.EnumerateObject()) { data.Add(p.Name); data.Add(p.Value.GetString() ?? ""); }
        }

        var texts = ProviderMeterValidator.Matrix.Select(c => c.CredentialAr)
            .Concat(new[] { Shape(CredentialKinds.None), With(Shape(CredentialKinds.None), "operations"), With(Shapes[CredentialKinds.SecretKey].Build(), "platform") }
                .SelectMany(d => ProviderMeterValidator.Validate(d).Select(v => v.MessageAr)))
            .ToList();

        var priced = names.Where(n => MeterUnitCatalogTests.PriceIsh.IsMatch(n))
            .Concat(data.Where(MeterUnitCatalogTests.NamesAPrice))
            .Concat(texts.Where(MeterUnitCatalogTests.NamesAPrice)).ToList();
        output.WriteLine($"[G7] قاعِدَة ١٦: أَنواع {types.Length} · أَسماء {names.Count} · مَفاتيحُ وقِيَمُ المِترِ في المِلَفّات {data.Count} · نُصوص {texts.Count} · تُسَمّي سِعراً {priced.Count}");
        Assert.True(names.Count > 20 && data.Count == 16 && texts.Count >= 7, $"أَداةٌ عَمياء: {names.Count} اسماً و{data.Count} مِفتاحاً وقيمَة و{texts.Count} نَصّاً.");
        Assert.True(priced.Count == 0, "مِترُ المُزَوِّدِ يُسَمّي سِعراً: " + string.Join("، ", priced));
    }
}
