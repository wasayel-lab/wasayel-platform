using System.Text.RegularExpressions;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ بابُ المُعَرِّفِ الواحِدِ يَسأَلُ مُصادِقَ اسمِ المُضيف — G2 (‏2026-11-07، ADR-050 §٣ · W13-4·C2a·3) ═══
//
// **المَقيسُ قَبل** (`8a8b8c0`): `SlugRefusal` بِشَكلِ `^[a-z0-9][a-z0-9-]{0,62}$` يَقبَلُ `store-` و`ab--cd` و`xn--…` وطولاً بَينَ 54 و63 و`best-vendor`،
// وكُلُّها لا يَصلُحُ مُضيفاً لِلمَتجَرِ أَو لِأَدوارِه. **والآن** يَسأَلُ `StoreHostName.Validate` فَتُرَدُّ في الأَبوابِ الثَلاثَة، والتَوصيفُ
// `SlugDoorHostShapeCharacterizationTests` (‏5a70efa) يَبقى أَخضَرَ بِبَصمَتِه لِما سِواها.

public class StoreHostNameDoorTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    /// <summary>البابُ يَسأَلُ المُصادِقَ ولا يَحمِلُ نَمَطاً ثانِياً — والأَبوابُ الثَلاثَةُ تَسأَلُ البابَ (G2؛ وفَحصُ الأَبوابِ نَفسُه في <c>SlugShapeTests</c>).</summary>
    [Fact]
    public void The_one_slug_door_asks_the_host_name_validator_and_carries_no_shape_of_its_own()
    {
        var code = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(RepoRoot,
            "libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Incubator/TenantFromAnalysisFactory.cs")));
        var at = code.IndexOf("public static string? SlugRefusal(", StringComparison.Ordinal);
        Assert.True(at > 0, "SlugRefusal غائِب");
        var body = code[at..code.IndexOf("\n    }", at, StringComparison.Ordinal)];
        output.WriteLine(body);
        Assert.Contains("StoreHostName.Validate(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Regex", body, StringComparison.Ordinal);
        Assert.DoesNotContain("[a-z0-9", code, StringComparison.Ordinal);

        // ومُخَطَّطاتُ أَدَواتِ الوَكيلِ تَبقى أَوسَعَ مِن البابِ (تَقرَأُ مَتاجِرَ قائِمَةً قَد تَكونُ عَلى غَيرِ الشَكل)، والبابُ بَعدَها يَرُدّ — تُعَدُّ وتُطبَع.
        var agent = File.ReadAllText(Path.Combine(RepoRoot, "libs/templates/ACommerce.Templates.Customer.Marketplace/Services/AgentService.cs"));
        var schemas = Regex.Matches(agent, Regex.Escape("\"pattern\": \"^[A-Za-z0-9_-]+$\"")).Count;
        output.WriteLine($"أَنماطُ slug في مُخَطَّطاتِ أَدَواتِ الوَكيل: {schemas}");
        Assert.True(schemas > 0, "أَداةٌ عَمياء: صِفرُ نَمَطٍ مَعدود");
    }

    /// <summary>ما غَيَّرَه التَبديلُ عَمداً — والرَمزُ المَحجوزُ لِلاحِقَةِ اسمِ الدَور، والشَكلُ لِما سِواها.</summary>
    [Theory]
    [InlineData("store-", TenantFromAnalysisFactory.SlugFormat)]
    [InlineData("ab--cd", TenantFromAnalysisFactory.SlugFormat)]
    [InlineData("xn--mgbh0fb", TenantFromAnalysisFactory.SlugFormat)]
    [InlineData("best-vendor", TenantFromAnalysisFactory.SlugReserved)]
    [InlineData("shop-admin", TenantFromAnalysisFactory.SlugReserved)]
    [InlineData("x-organizer", TenantFromAnalysisFactory.SlugReserved)]
    public void The_host_rules_refuse_what_the_old_shape_let_through(string slug, string code)
    {
        output.WriteLine($"«{slug}» ⇒ {TenantFromAnalysisFactory.SlugRefusal(slug)} ({string.Join(" · ", StoreHostName.Validate(slug).Select(v => v.Code))})");
        Assert.Equal(code, TenantFromAnalysisFactory.SlugRefusal(slug));
    }

    [Theory]
    [InlineData(53, null)]
    [InlineData(54, TenantFromAnalysisFactory.SlugFormat)]
    [InlineData(63, TenantFromAnalysisFactory.SlugFormat)]
    public void The_slug_is_capped_at_the_derived_host_length(int length, string? code)
        => Assert.Equal(code, TenantFromAnalysisFactory.SlugRefusal(new string('a', length)));

    /// <summary>وكُلُّ رَمزٍ مِن رُموزِ المُصادِقِ يَرُدُّه البابُ — المَحجوزُ واللاحِقَةُ <c>slug_reserved</c>، والفارِغُ <c>slug_required</c>، وما سِواها <c>slug_format</c>.</summary>
    [Fact]
    public void Every_host_name_code_is_a_door_refusal()
    {
        var rows = StoreHostNameValidatorTests.Rows().Select(r => ((string)r[0], (string)r[1], (string)r[2])).ToList();
        foreach (var (code, negative, positive) in rows)
        {
            var refused = TenantFromAnalysisFactory.SlugRefusal(negative);
            var expected = code switch
            {
                StoreHostName.Empty => TenantFromAnalysisFactory.SlugRequired,
                StoreHostName.Reserved or StoreHostName.RoleSuffix => TenantFromAnalysisFactory.SlugReserved,
                _ => TenantFromAnalysisFactory.SlugFormat,
            };
            output.WriteLine($"{code}: «{(negative.Length > 12 ? negative[..12] + "…" : negative)}» ⇒ {refused} · «{(positive.Length > 12 ? positive[..12] + "…" : positive)}» ⇒ {TenantFromAnalysisFactory.SlugRefusal(positive) ?? "يَمُرّ"}");
            Assert.Equal(expected, refused);
            Assert.Null(TenantFromAnalysisFactory.SlugRefusal(positive));
        }
        Assert.Equal(StoreHostName.Codes.Count, rows.Count);
    }

    /// <summary>والاستِمارَتانِ تَقبَلانِ ما يَقبَلُه شَكلُ الخادِمِ عَلى حُدودِه الجَديدَة (53/54، الشَرطَةُ في الآخِر، الشَرطَتانِ) — والمَحجوزُ واللاحِقَةُ
    /// في الخادِمِ وَحدَه كالتَفَرُّد.</summary>
    [Theory]
    [InlineData("libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/StudioNewStore.razor")]
    [InlineData("libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/Admin/CreateTenant.razor")]
    public void The_store_forms_pattern_follows_the_host_shape_at_its_new_edges(string file)
    {
        var p = HtmlPatternVModeTests.Patterns().Single(x => x.File == file && x.Pattern.Contains("a-z0-9", StringComparison.Ordinal)).Pattern;
        var browser = new Regex("^(?:" + p + ")$");
        string[] samples = ["a", "a-b", "a-b-c", new string('a', 53), new string('a', 54), "shop-", "ab--cd", "xn--abc", "-a", "a_b"];
        foreach (var s in samples)
        {
            var server = TenantFromAnalysisFactory.SlugRefusal(s) is not (TenantFromAnalysisFactory.SlugFormat or TenantFromAnalysisFactory.SlugRequired);
            output.WriteLine($"{file.Split('/')[^1]} «{(s.Length > 12 ? s[..12] + "…" + s.Length : s)}»: المُتَصَفِّح {browser.IsMatch(s)} · الخادِم {server}");
            Assert.Equal(server, browser.IsMatch(s));
        }
        Assert.Null(HtmlPatternVModeTests.VModeBreach(p));
    }

    /// <summary>وحَدُّ حَقلِ الاستوديو هُوَ السَقفُ المُشتَقُّ نَفسُه.</summary>
    [Fact]
    public void The_studio_form_field_is_capped_at_the_derived_length()
    {
        var razor = File.ReadAllText(Path.Combine(RepoRoot, "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/StudioNewStore.razor"));
        var m = Regex.Match(razor, @"name=""slug""[^>]*maxlength=""(?<n>\d+)""");
        output.WriteLine($"maxlength={m.Groups["n"].Value} · MaxLength={StoreHostName.MaxLength}");
        Assert.True(m.Success);
        Assert.Equal(StoreHostName.MaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture), m.Groups["n"].Value);
    }
}

// ─── البُرهانُ عَلى Postgres ────────────────────────────────────────────────────────────────────────────────

[Collection(LiveProofCollection.Name)]
public class LiveStoreHostNameDoorProofTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>ما كانَ يولَدُ في الأَبوابِ الثَلاثَةِ ولا يَصلُحُ مُضيفاً يُرَدُّ الآنَ في كُلٍّ مِنها، ولا مَتجَرَ يولَد.</summary>
    [Fact]
    public async Task The_three_doors_refuse_a_slug_that_cannot_be_a_host_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        var admin = await UserAsync(host, consented: false, platformAdmin: true);
        var exec = new AgentToolExecutor(host.Store, new TenantRoleService(host.Store));
        var stem = NewSlug("w13hn");
        (string Slug, string Studio, string Admin)[] rows =
        [
            (stem + "-vendor", TenantFromAnalysisFactory.SlugReserved, TenantFromAnalysisFactory.SlugReserved),
            (stem + "-", TenantFromAnalysisFactory.SlugFormat, TenantFromAnalysisFactory.SlugRequired),
            ("ab--" + stem, TenantFromAnalysisFactory.SlugFormat, TenantFromAnalysisFactory.SlugRequired),
            (stem + new string('a', StoreHostName.MaxLength + 1 - stem.Length), TenantFromAnalysisFactory.SlugFormat, TenantFromAnalysisFactory.SlugRequired),
        ];
        try
        {
            foreach (var (slug, studioCode, adminCode) in rows)
            {
                var studio = await StudioCreateAsync(host, a, slug, "مَتجَر");
                var byAdmin = await AdminCreateAsync(host, admin, slug, "مَتجَر");
                var byAgent = await exec.ExecuteAsync("create_tenant", AgentPayload(slug, "مَتجَر"), a.User.Id);
                output.WriteLine($"[live] «{slug}» ({slug.Length}) ⇒ الاستوديو {studio.Location.Split('&')[0]} · المُشرِف {byAdmin.Location.Split('&')[0]} · الوَكيل {byAgent}");
                Assert.Equal((302, StudioStoreCreate.Refused(studioCode, new StudioStoreCreate.Form(slug, "مَتجَر", StudioStoreCreate.DefaultColor, ""))), studio);
                Assert.StartsWith($"/admin/tenants/new?err={adminCode}&slug={Uri.EscapeDataString(slug)}&", byAdmin.Location, StringComparison.Ordinal);
                Assert.Equal((false, "slug غَير صالِح."), byAgent);
                Assert.Null(await TenantAsync(host, slug));
            }
            Assert.Equal(0, await StoresBuiltAsync(host, a));
        }
        finally { await CleanAsync(host, [a, admin], [.. rows.Select(r => r.Slug)]); }
    }
}
