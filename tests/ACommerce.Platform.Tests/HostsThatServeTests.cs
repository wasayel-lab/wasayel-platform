using System.Text.RegularExpressions;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.MultiTenancy;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ المُضيفاتُ الَّتي لا تَخدِم، والمُعَرِّفاتُ الَّتي لا تَصيرُ مُضيفاً (‏2026-11-11، W13-4·C2z·4 — ADR-050 §٣ و§١٣) ═══
//
// **المَقيسُ قَبل** (عِندَ المُراجِعين، BaseDomain=localhost): `admin.localhost`، `studio.localhost`، `api.localhost` … ← `/studio/auth` 200 و`/admin` 200 و`/{store}` 200 —
// نُسَخٌ كامِلَةٌ مِن المَنَصَّة؛ ومَتجَرا `new` و`create` قُبِلا، و`POST /admin/tenants/create` عَلى `create.localhost` بَلَغَ بابَ الإنشاء؛ ومَتجَرٌ اسمُه `explore` جَعَلَ
// `explore.localhost/explore` جَذرَه؛ ودَورٌ مُؤَلَّفٌ اسمُه `admin` يَجتاز. **والآن** ما في هذا المِلَفّ؛ وما لا يَتَغَيَّرُ في `HostsThatServeCharacterizationTests` بِبايتاتِه.

public class HostsThatServeTests(ITestOutputHelper output)
{
    private const string Base = "example.test";

    [Theory]
    [InlineData("admin")]
    [InlineData("studio")]
    [InlineData("api")]
    [InlineData("cdn")]
    [InlineData("blog")]
    [InlineData("support")]
    [InlineData("uploads")]
    [InlineData("lang")]
    [InlineData("pricing")]
    // ‏(‏2026-11-11، مُراجَعَةُ إغلاقِ W13-4 الجَولَةُ الثانِيَة · SEC-C2z-R2-2) وmail — كانَ يَمُرُّ (ApexAliases) فَيَخدِمُ الاستوديو والإشرافَ ومَتاجِرَ المَسارِ عَلى أَصلٍ ثانٍ
    [InlineData("mail")]
    public void A_reserved_label_other_than_the_apex_aliases_serves_nothing(string label)
    {
        foreach (var (method, path) in new[] { ("GET", "/"), ("GET", "/studio/auth"), ("GET", "/admin"), ("GET", "/ashare"), ("POST", "/admin/tenants/create") })
        {
            var a = TenantResolverMiddleware.AnswerStoreHost($"{label}.{Base}", Base, method, path, "", "https");
            Assert.Equal(StoreHostVerdict.NotFound, a.Verdict);
        }
        output.WriteLine($"{label}.{Base} ⇒ NotFound ×5");
    }

    /// <summary>‏(‏2026-11-11، SEC-C2z-R2-2) كانَ «www وmail» — والقَرارُ (G4، A2 «www.R → R») سَمّى www وَحدَه؛ mail.R بِسِجِلٍّ صَريحٍ لا يَبلُغُ الأَصلَ أَصلاً، وبِلا
    /// سِجِلٍّ (MX وَحدَه) كانَ يَبلُغُه عَبرَ `*` فَيَصيرُ أَصلاً ثانِياً لِلمَنَصَّة.</summary>
    [Fact]
    public void Only_www_passes_among_the_reserved_labels()
    {
        var passing = ReservedTenantSlugs.All
            .Where(l => Regex.IsMatch(l, "^[a-z0-9]([a-z0-9-]*[a-z0-9])?$"))
            .Where(l => TenantResolverMiddleware.AnswerStoreHost($"{l}.{Base}", Base, "GET", "/", "", "https").Verdict == StoreHostVerdict.Pass)
            .Order(StringComparer.Ordinal).ToList();
        var labels = ReservedTenantSlugs.All.Count(l => Regex.IsMatch(l, "^[a-z0-9]([a-z0-9-]*[a-z0-9])?$"));
        output.WriteLine($"مِلصَقاتٌ مَحجوزَةٌ صالِحَة: {labels} · تَمُرّ: {string.Join(" ", passing)}");
        Assert.True(labels >= 40, $"فَحصٌ أَعمى: {labels}");
        Assert.Equal(["www"], passing);
        Assert.Equal(["www"], TenantResolverMiddleware.ApexAliases.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("explore")]
    [InlineData("deals")]
    [InlineData("cart")]
    [InlineData("login")]
    [InlineData("me")]
    [InlineData("checkout")]
    [InlineData("create-listing")]
    [InlineData("vendor")]
    [InlineData("payments")]
    [InlineData("new")]
    [InlineData("create")]
    [InlineData("NEW")]
    public void A_page_segment_or_an_admin_literal_is_reserved_as_a_host_name(string slug)
    {
        var codes = StoreHostName.Validate(slug).Select(v => v.Code).ToList();
        output.WriteLine($"{slug} ⇒ {string.Join(" ", codes)}");
        Assert.Contains(StoreHostName.Reserved, codes);
        // ولا تَدخُلُ قائِمَةَ المَسار: مَتجَرٌ قائِمٌ بِها يَبقى بِمَسارِه
        Assert.False(ReservedTenantSlugs.Contains(slug.ToLowerInvariant()));
    }

    [Fact]
    public void A_store_named_after_a_page_segment_gets_no_host_but_keeps_its_path()
    {
        Assert.Null(TenantResolverMiddleware.StoreSlugFromHost("explore.example.test", Base));
        Assert.Equal("explore", TenantResolverMiddleware.SlugFromPath("/explore/listings"));
        Assert.Null(TenantResolverMiddleware.StoreSlugFromHost("new.example.test", Base));
        Assert.Equal(StoreHostVerdict.NotFound, TenantResolverMiddleware.AnswerStoreHost("create.example.test", Base, "POST", "/admin/tenants/create", "", "https").Verdict);
    }

    [Fact]
    public void A_tenant_authored_role_named_after_a_role_host_label_is_refused_with_its_code()
    {
        var admin = new RoleDefinition
        {
            Slug = "admin", Icon = "🛡", HomeRoute = "/manage",
            Label = new LocalizedText("مُدير"), Description = new LocalizedText("مُديرُ المَتجَر."),
            Permissions = ["listing.browse"], Fields = [], Composition = new RoleComposition(),
        };
        var codes = RoleDefinitionValidator.ValidateTenantDefinition(admin).Select(v => v.Code).ToList();
        output.WriteLine($"admin ⇒ {string.Join(" ", codes)}");
        Assert.Contains(RoleDefinitionValidator.SlugShadowsRoleHost, codes);
        // وموجِب: اسمٌ لَيسَ اسمَ مُضيفِ دَورٍ يَجتاز، واسمُ الكَتالوجِ يُرَدُّ بِرَمزِه هُوَ لا بِهذا
        Assert.DoesNotContain(RoleDefinitionValidator.ValidateTenantDefinition(admin with { Slug = "admins" }), v => v.Code == RoleDefinitionValidator.SlugShadowsRoleHost);
        Assert.DoesNotContain(RoleDefinitionValidator.ValidateTenantDefinition(admin with { Slug = "vendor" }), v => v.Code == RoleDefinitionValidator.SlugShadowsRoleHost);
        // ومَنصَّةُ الكَتالوجِ لا تَرى الرَمز (تَعريفاتُها تَمُرُّ بِـValidate وَحدَها)
        Assert.DoesNotContain(RoleDefinitionValidator.Validate(admin), v => v.Code == RoleDefinitionValidator.SlugShadowsRoleHost);
    }

    [Fact]
    public void The_approval_door_answers_with_the_tenant_gate_codes()
    {
        var src = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs/templates/ACommerce.Templates.Customer.Marketplace/Services/TenantRoleService.cs"));
        var body = src[src.IndexOf("ValidateBeforeApprove(TenantRoleDefinition doc)", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("AlreadyApprovedAr", StringComparison.Ordinal)];
        Assert.Contains("RoleDefinitionValidator.ValidateTenantDefinition(parsed)", body, StringComparison.Ordinal);
        Assert.Contains("violations.Select(v => v.Code)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_slug_messages_say_the_true_reason_and_read_the_length_from_the_validator()
    {
        var ar = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs/core/ACommerce.Platform.I18n/Locales/ar.json"));
        string Text(string key) => Regex.Match(ar, $"\"{Regex.Escape(key)}\": \"(?<t>[^\"]*)\"").Groups["t"].Value;
        var format = Text("studio.create.err_slug_format");
        var reserved = Text("studio.create.err_slug_reserved");
        output.WriteLine($"الصيغَة: {format}\nالمَحجوز: {reserved}");
        Assert.Contains("{0}", format, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\d{2}", format);                    // لا رَقَمَ مَكتوبٌ — MaxLength يُمَرَّرُ عِندَ الرَسم
        Assert.Contains("-vendor", reserved, StringComparison.Ordinal);   // لاحِقَةُ الدَورِ سَبَبٌ مُسَمّى
        Assert.Contains("explore", reserved, StringComparison.Ordinal);   // ومَقطَعُ الصَفحَة
        var page = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs/templates/ACommerce.Templates.Customer.Marketplace/Components/Pages/StudioNewStore.razor"));
        Assert.Contains("@if (Err == Services.Incubator.TenantFromAnalysisFactory.SlugFormat) { @L.Markup(StudioStoreCreate.MessageKey(Err), ACommerce.Kit.Tenants.StoreHostName.MaxLength) }", page, StringComparison.Ordinal);
        Assert.Equal(53, StoreHostName.MaxLength);
    }
}
