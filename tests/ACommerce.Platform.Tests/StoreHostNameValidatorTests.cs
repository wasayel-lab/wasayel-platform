using System.Text.Json;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ اسمُ مُضيفِ المَتجَر — سَبعَةُ رُموزٍ ثابِتَة، ولِكُلٍّ موجَبٌ وسالِب (‏2026-11-07، ADR-050 §٣ · W13-4·C2a·2 — G1 وG4) ═══
//
// **لِماذا**: المَتجَرُ `{slug}.R` ودَورُه `{slug}-{rolehost}.R` مِلصَقانِ في المُستَوى الأَوَّل. والشَكلُ الواحِدُ عِندَ `f81840c`
// (`^[a-z0-9][a-z0-9-]{0,62}$`) يَقبَلُ `shop-` و`ab--cd` و`xn--…` وطولاً لا يَتَّسِعُ مَعَه اسمُ دَورٍ و`best-vendor` (مُضيفُ دَورِ `best`).
// والمُصادِقُ يُجيبُ بِكُلِّ خُروقِه (ADR-038)، وسَقفُه مُشتَقٌّ مِن أَطوَلِ اسمِ مُضيفِ دَور.

public class StoreHostNameValidatorTests(ITestOutputHelper output)
{
    /// <summary>(الرَمز، سالِبٌ يَخرِقُه، موجَبٌ لا يَخرِقُه) — والموجَبُ صالِحٌ كُلُّه، فَالسَطرُ يَقيسُ الرَمزَ وَحدَه.</summary>
    public static TheoryData<string, string, string> Rows() => new()
    {
        { StoreHostName.Empty,        "",                                   "ashare" },
        { StoreHostName.TooLong,      new string('a', StoreHostName.MaxLength + 1), new string('a', StoreHostName.MaxLength) },
        { StoreHostName.Charset,      "shop_1",                             "shop1" },
        { StoreHostName.EdgeHyphen,   "shop-",                              "my-shop" },
        { StoreHostName.DoubleHyphen, "xn--mgbh0fb",                        "ab-cd" },
        { StoreHostName.Reserved,     "www",                                "ashare" },
        { StoreHostName.RoleSuffix,   "best-vendor",                        "vendors" },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void The_negative_example_carries_its_code(string code, string negative, string _)
    {
        var found = StoreHostName.Validate(negative).Select(v => v.Code).ToArray();
        output.WriteLine($"{code} ⇐ «{(negative.Length > 16 ? negative[..16] + "…" + negative.Length : negative)}»: {string.Join(" · ", found)}");
        Assert.Contains(code, found);
        Assert.False(StoreHostName.IsValid(negative));
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void The_positive_example_is_a_valid_host_name(string code, string _, string positive)
    {
        var found = StoreHostName.Validate(positive);
        output.WriteLine($"{code} ⇐ «{(positive.Length > 16 ? positive[..16] + "…" + positive.Length : positive)}»: {found.Count} خَرق");
        Assert.DoesNotContain(found, v => v.Code == code);
        Assert.True(StoreHostName.IsValid(positive));
    }

    /// <summary>والجَدوَلُ يُغَطّي المُعجَمَ كُلَّه — رَمزٌ يُضافُ بِلا سَطرٍ يُحمِّرُ هذا.</summary>
    [Fact]
    public void Every_code_has_a_row()
    {
        var rows = Rows().Select(r => (string)r[0]).ToArray();
        output.WriteLine($"الرُموز: {StoreHostName.Codes.Count} · الصُفوف: {rows.Length}");
        Assert.Equal(7, StoreHostName.Codes.Count);
        Assert.Equal(StoreHostName.Codes.OrderBy(c => c), rows.OrderBy(c => c));
    }

    /// <summary>ويُجيبُ بِكُلِّ خُروقِه لا بِأَوَّلِها (ADR-038).</summary>
    [Fact]
    public void A_name_breaking_several_rules_gets_every_code()
    {
        var found = StoreHostName.Validate("-Shop--x-").Select(v => v.Code).ToArray();
        output.WriteLine(string.Join(" · ", found));
        Assert.Equal(new[] { StoreHostName.Charset, StoreHostName.EdgeHyphen, StoreHostName.DoubleHyphen }, found);
    }

    [Theory]
    [InlineData("Shop")]
    [InlineData("متجر")]
    [InlineData("a.b")]
    [InlineData("a b")]
    public void Capitals_and_non_latin_and_dots_are_off_the_charset(string slug)
        => Assert.Contains(StoreHostName.Validate(slug), v => v.Code == StoreHostName.Charset);

    /// <summary>الشَرطَةُ في الوَسَطِ مَقبولَة، والاسمُ الَّذي هُوَ اسمُ دَورٍ وَحدَه مَتجَرٌ صالِح (<c>vendor.R</c> مُضيفُ مَتجَر، لا مُضيفُ دَور).</summary>
    [Theory]
    [InlineData("theme-demo")]
    [InlineData("owner-test")]
    [InlineData("ejar")]
    [InlineData("injez")]
    [InlineData("vendor")]
    [InlineData("a")]
    [InlineData("0")]
    public void Seed_and_plain_names_are_valid(string slug)
        => Assert.True(StoreHostName.IsValid(slug), string.Join(" · ", StoreHostName.Validate(slug).Select(v => v.Code)));

    /// <summary><b>مَقاطِعُ API المَنَصَّةِ الحَرفِيَّةُ مَحجوزَةٌ اسمَ مُضيف</b> (‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الثانِيَة · SEC-C2a-R2-1): بِرَمزِ الحَجزِ نَفسِه، وبِلا
    /// حالَةِ أَحرُف — وهي خارِجَ <see cref="ReservedTenantSlugs.All"/> فَلا يَفقِدُ مَتجَرٌ قائِمٌ بِها مَسارَه. وجارُها يَمُرّ.</summary>
    [Theory]
    [InlineData("push", true)]
    [InlineData("v1", true)]
    [InlineData("billing", true)]
    [InlineData("PUSH", true)]
    [InlineData("pushes", false)]
    [InlineData("v2", false)]
    [InlineData("vapid-key", false)]
    public void Platform_api_segments_are_reserved_host_names(string slug, bool reserved)
    {
        var codes = StoreHostName.Validate(slug).Select(v => v.Code).ToArray();
        output.WriteLine($"«{slug}»: {string.Join(" · ", codes)} · في All: {ReservedTenantSlugs.Contains(slug)}");
        Assert.Equal(reserved, codes.Contains(StoreHostName.Reserved));
        Assert.Equal(reserved, ReservedTenantSlugs.IsPlatformApiSegment(slug));
        if (slug is "push" or "v1") Assert.False(ReservedTenantSlugs.Contains(slug));
    }

    /// <summary><b>السَقفُ مُشتَقٌّ ويُطبَعُ اشتِقاقُه</b>: ‏63 − 1 − أَطوَلُ اسمِ مُضيفِ دَور.</summary>
    [Fact]
    public void The_max_length_is_derived_from_the_longest_role_host_name()
    {
        var longest = RoleHostNames.Values.OrderByDescending(v => v.Length).ThenBy(v => v, StringComparer.Ordinal).First();
        output.WriteLine($"MaxLength = {StoreHostName.DnsLabelMax} − 1 − {longest.Length} («{longest}») = {StoreHostName.MaxLength}");
        Assert.Equal(StoreHostName.DnsLabelMax - 1 - longest.Length, StoreHostName.MaxLength);
        Assert.Equal(53, StoreHostName.MaxLength);
        // وأَطوَلُ مُعَرِّفٍ صالِحٍ مَعَ أَطوَلِ دَورٍ مِلصَقُ DNS واحِد.
        Assert.Equal(StoreHostName.DnsLabelMax, (new string('a', StoreHostName.MaxLength) + "-" + longest).Length);
    }
}

public class RoleHostNamesTests(ITestOutputHelper output)
{
    /// <summary><b>G4</b>: كُلُّ دَورٍ في الكَتالوجِ لَه اسمُ مُضيف، ولا اسمَ لِدَورٍ لَيسَ في الكَتالوج — والعَدَدُ يُطبَع.</summary>
    [Fact]
    public void Role_host_lexicon_covers_the_catalog()
    {
        var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs/kits/Roles/ACommerce.Kit.Roles.Core/Definitions/roles.index.json")))
            .RootElement.GetProperty("roles").EnumerateArray().Select(e => e.GetString()!).ToArray();
        var catalog = RoleCatalog.Definitions.Select(d => d.Slug).ToArray();
        output.WriteLine($"الفِهرِس {index.Length} · الكَتالوج {catalog.Length} · المُعجَم {RoleHostNames.ByCatalogSlug.Count}: "
                         + string.Join(" · ", RoleHostNames.ByCatalogSlug.Select(p => p.Key == p.Value ? p.Key : $"{p.Key}←{p.Value}")));
        Assert.NotEmpty(index);
        Assert.Equal(index.OrderBy(s => s, StringComparer.Ordinal), catalog.OrderBy(s => s, StringComparer.Ordinal));
        Assert.Equal(index.OrderBy(s => s, StringComparer.Ordinal), RoleHostNames.ByCatalogSlug.Keys.OrderBy(s => s, StringComparer.Ordinal));
    }

    /// <summary>كُلُّ قيمَةٍ مِلصَقُ DNS صالِحٌ <b>بِلا شَرطَة</b> (فَتَحليلُ <c>{slug}-{rolehost}</c> بِآخِرِ شَرطَةٍ لا يَلتَبِس)، والقِيَمُ مُتَمايِزَة.</summary>
    [Fact]
    public void Every_role_host_name_is_a_plain_dns_label()
    {
        foreach (var v in RoleHostNames.ByCatalogSlug.Values)
        {
            output.WriteLine(v);
            Assert.InRange(v.Length, 1, StoreHostName.DnsLabelMax);
            Assert.All(v, c => Assert.True(c is >= 'a' and <= 'z' or >= '0' and <= '9', $"«{v}»: «{c}»"));
        }
        Assert.Equal(RoleHostNames.ByCatalogSlug.Count, RoleHostNames.Values.Count);
    }

    /// <summary>والمُعَرِّفُ الصالِحُ لا يُنتِجُ مُضيفَ دَور: كُلُّ <c>{مَتجَر}-{قيمَة}</c> يَرُدُّه المُصادِقُ بِرَمزِ اللاحِقَة.</summary>
    [Fact]
    public void No_valid_store_slug_is_a_role_host_label()
    {
        var checkedLabels = 0;
        foreach (var store in new[] { "ashare", "theme-demo", "a" })
            foreach (var v in RoleHostNames.Values)
            {
                var label = $"{store}-{v}";
                Assert.Contains(StoreHostName.Validate(label), x => x.Code == StoreHostName.RoleSuffix);
                checkedLabels++;
            }
        output.WriteLine($"مُضيفاتُ أَدوارٍ مَفحوصَة: {checkedLabels}");
        Assert.Equal(3 * RoleHostNames.Values.Count, checkedLabels);
    }
}
