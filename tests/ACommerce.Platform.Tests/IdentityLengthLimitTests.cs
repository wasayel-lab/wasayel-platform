using System.Text.RegularExpressions;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ حُدودُ الهُوِيَّةِ في كُلِّ بابٍ يَكتُبُها، لا في بابِ الإنشاءِ وَحدَه (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 الجَولَةُ الثانِيَة · W13-1·SEC2-C1-2) ═══
//
// **المَقيسُ قَبل** (تَقريرُ العَدَسَة، قاعِدَةٌ جَديدَة): صاحِبٌ أَنشَأَ `big1` بِاسمٍ قَصير (‏302 ?built=1)، ثُمَّ أَرسَلَ إلى
// `POST /studio/apps/big1/branding/save` اسماً بِمِئَتَي أَلفِ حَرفٍ فَأُجيبَ `302 ?saved=1` وطولُ الاسمِ في القاعِدَةِ 200000: `GET /big1` لِلمَجهولِ
// ‏15,049,017 بايتاً، و`/studio` لِصاحِبِه ‏1,447,240، وبَعدَ إعلانٍ واحِدٍ صارَت الصَفحَةُ الأُولى `/` ‏1,441,598 بايتاً لِكُلِّ زائِر (كانَت 41,128).
// وهُوَ عَينُ ما قيلَ إنّ SEC-C1-3 أَغلَقَه — حَدّا 60 و120 كانا في بابِ الإنشاءِ وَحدَه، وبابُ المُشرِفِ وأَداتا الوَكيلِ بِلا حَدٍّ كَذلك.
// **والآن** الأَرقامُ في مَوضِعٍ واحِد (`StudioStoreCreate.NameMax`، `TagLineMax`، و`CityMax` رَقمُ حَقلِ المَدينَةِ في استِمارَتَي الهُوِيَّةِ والمُشرِف)، يَسأَلُها
// حِفظُ الهُوِيَّةِ (`BrandingSaveService.WhyInvalid` — والسَطحانِ مَعاً) وبابُ المُشرِفِ و`create_tenant` و`set_branding`؛ ولِكُلِّ رَمزٍ موجَبٌ وسالِب (القاعِدَة ٤).

public class IdentityLengthLimitTests(ITestOutputHelper output)
{
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";
    private static string Read(string rel) => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, rel));

    private static BrandingSaveRequest Branding(int name = 5, int tagline = 0, int city = 0)
        => new(new string('ن', name), new string('س', tagline), new string('م', city), "#2563eb", null);

    /// <summary>الموجَبُ والسالِبُ لِكُلِّ رَمز: عَلى الحَدِّ يُقبَل، وفَوقَه بِحَرفٍ يُرَدُّ بِرَمزِه — والاسمُ ثُمَّ السَطرُ ثُمَّ المَدينَة.</summary>
    [Theory]
    [InlineData(60, 120, 40, null)]
    [InlineData(61, 0, 0, TenantConfigCodes.NameTooLong)]
    [InlineData(5, 121, 0, TenantConfigCodes.TagLineTooLong)]
    [InlineData(5, 0, 41, TenantConfigCodes.CityTooLong)]
    [InlineData(61, 121, 41, TenantConfigCodes.NameTooLong)]
    [InlineData(5, 121, 41, TenantConfigCodes.TagLineTooLong)]
    public void The_identity_save_refuses_the_first_field_past_the_form_limit(int name, int tagline, int city, string? code)
        => Assert.Equal(code, BrandingSaveService.WhyInvalid(Branding(name, tagline, city)));

    [Fact]
    public void The_identity_save_asks_the_limits_after_the_name_and_colour_and_the_codes_are_the_create_codes()
    {
        Assert.Equal(TenantConfigCodes.NameRequired, BrandingSaveService.WhyInvalid(Branding(name: 0, tagline: 500)));
        Assert.Equal(TenantConfigCodes.ColorInvalid, BrandingSaveService.WhyInvalid(Branding(name: 500) with { BrandColor = "blue" }));
        Assert.Equal((StudioStoreCreate.NameTooLong, StudioStoreCreate.TagLineTooLong, StudioStoreCreate.CityTooLong),
            (TenantConfigCodes.NameTooLong, TenantConfigCodes.TagLineTooLong, TenantConfigCodes.CityTooLong));
        foreach (var c in new[] { TenantConfigCodes.NameTooLong, TenantConfigCodes.TagLineTooLong, TenantConfigCodes.CityTooLong })
            Assert.Contains(c, TenantConfigCodes.All);
    }

    [Theory]
    [InlineData(60, 120, 40, null)]
    [InlineData(61, 0, 0, StudioStoreCreate.NameTooLong)]
    [InlineData(0, 121, 0, StudioStoreCreate.TagLineTooLong)]
    [InlineData(0, 0, 41, StudioStoreCreate.CityTooLong)]
    public void The_shared_length_refusal_names_the_first_field_past_its_limit(int name, int tagline, int city, string? code)
        => Assert.Equal(code, StudioStoreCreate.LengthRefusal(new string('a', name), new string('b', tagline), new string('c', city)));

    /// <summary>الأَرقامُ أَرقامُ الاستِمارات (القاعِدَة ١٦): كُلُّ حَقلِ اسمٍ وسَطرٍ ومَدينَةٍ في استِمارَاتِ الهُوِيَّةِ يَقولُ <c>maxlength</c> الثابِتِ نَفسِه.</summary>
    [Theory]
    [InlineData("Components/Pages/StudioAppBranding.razor", 3)]
    [InlineData("Components/Pages/Admin/TenantBranding.razor", 3)]
    [InlineData("Components/Pages/Admin/CreateTenant.razor", 3)]
    [InlineData("Components/Pages/StudioNewStore.razor", 2)]
    public void Every_identity_field_states_the_server_limit(string page, int fields)
    {
        var code = Read($"{Template}/{page}");
        var limits = new Dictionary<string, int>
            { ["name"] = StudioStoreCreate.NameMax, ["tagline"] = StudioStoreCreate.TagLineMax, ["city"] = StudioStoreCreate.CityMax };
        var seen = 0;
        foreach (Match m in Regex.Matches(code, @"<input\b[^>]*\bname=""(?<n>name|tagline|city)""[^>]*>", RegexOptions.Singleline))
        {
            var max = Regex.Match(m.Value, @"\bmaxlength=""(?<v>\d+)""");
            output.WriteLine($"{page} {m.Groups["n"].Value}: maxlength={max.Groups["v"].Value}");
            Assert.True(max.Success, $"{page}: حَقلُ {m.Groups["n"].Value} بِلا maxlength");
            Assert.Equal(limits[m.Groups["n"].Value], int.Parse(max.Groups["v"].Value));
            seen++;
        }
        Assert.Equal(fields, seen);
    }

    /// <summary>والسُطوحُ الثَلاثَةُ تَقولُ الرُموزَ الثَلاثَةَ مِن القامُوس — والمَفاتيحُ مَوجودَةٌ بِالعَرَبِيَّة.</summary>
    [Theory]
    [InlineData("Components/Pages/StudioAppBranding.razor")]
    [InlineData("Components/Pages/Admin/TenantBranding.razor")]
    [InlineData("Components/Pages/Admin/CreateTenant.razor")]
    public void Every_identity_surface_draws_the_three_length_codes_from_the_dictionary(string page)
    {
        var code = Read($"{Template}/{page}");
        Assert.Contains("Services.Incubator.StudioStoreCreate.NameTooLong or Services.Incubator.StudioStoreCreate.TagLineTooLong or Services.Incubator.StudioStoreCreate.CityTooLong", code, StringComparison.Ordinal);
        Assert.Contains("Services.Incubator.StudioStoreCreate.LengthMessageKey(", code, StringComparison.Ordinal);
        foreach (var c in new[] { StudioStoreCreate.NameTooLong, StudioStoreCreate.TagLineTooLong, StudioStoreCreate.CityTooLong })
        {
            var text = LocaleCatalog.Text(LocaleCatalog.Arabic, StudioStoreCreate.LengthMessageKey(c));
            Assert.False(string.IsNullOrWhiteSpace(text) || text == StudioStoreCreate.LengthMessageKey(c), $"{c}: لا نَصَّ عَرَبِيّ");
            Assert.Matches("[؀-ۿ]", text);
        }
    }

    /// <summary>وبابُ المُشرِفِ وأَداتا الوَكيلِ يَسأَلونَ الحُدودَ نَفسَها قَبلَ الكِتابَة.</summary>
    [Fact]
    public void The_admin_door_and_both_agent_tools_ask_the_same_limits_before_they_write()
    {
        var admin = WriteEndpointGuardTests.AllMinimalApiEndpoints().Single(e => e.Route == "/admin/tenants/create").Body;
        int At(string body, string s) => body.IndexOf(s, StringComparison.Ordinal);
        var (color, limit, write) = (At(admin, "Back(\"color_invalid\")"), At(admin, "StudioStoreCreate.LengthRefusal(name, tagline, city)"), At(admin, "s.Insert("));
        output.WriteLine($"/admin/tenants/create: اللَون@{color} ← الحُدود@{limit} ← الإدراج@{write}");
        Assert.True(color > 0 && limit > color && write > limit, "بابُ المُشرِفِ لا يَسأَلُ الحُدودَ قَبلَ الإدراج.");

        var agent = Read($"{Template}/Services/AgentService.cs");
        var create = agent[At(agent, "private async Task<(bool, string)> CreateTenantAsync(")..At(agent, "private async Task<(bool, string)> SetCategoriesAsync(")];
        var brand = agent[At(agent, "private async Task<(bool, string)> SetBrandingAsync(")..];
        brand = brand[..At(brand, "s.SaveChangesAsync(")];
        Assert.True(At(create, "LengthRefused(name, tagline, city)") is > 0 and var c && c < At(create, "CheckBuildAsync("), "create_tenant لا يَسأَلُ الحُدودَ قَبلَ الحِصَّة.");
        Assert.True(At(brand, "LengthRefused(name, tagline, city)") > 0, "set_branding لا يَسأَلُ الحُدودَ قَبلَ الحِفظ.");
    }
}

// ─── البُرهانُ عَلى Postgres ────────────────────────────────────────────────────────────────────────────────

[Collection(LiveProofCollection.Name)]
public class LiveIdentityLengthLimitProofTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static Task<(int Status, string Location)> BrandingAsync(LiveTemplateDoorHost host, Probe p, string slug, string name, string tagline = "", string city = "")
        => host.PostStudioFormAsync("/studio/apps/{slug}/branding/save", new Dictionary<string, string> { ["slug"] = slug }, p.Token,
            ("name", name), ("tagline", tagline), ("city", city), ("color", StudioStoreCreate.DefaultColor));

    /// <summary>ما قاسَتهُ العَدَسَة: مَتجَرٌ بِاسمٍ قَصيرٍ ثُمَّ «الهُوِيَّة» بِاسمٍ طَويل — يُرَدُّ بِرَمزِه ولا يُكتَب؛ وعَلى الحُدودِ الثَلاثَةِ يُحفَظ.</summary>
    [Fact]
    public async Task The_identity_save_refuses_a_value_past_its_limit_and_writes_nothing_and_saves_one_on_it_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        var slug = NewSlug("w13l-");
        try
        {
            Assert.Equal((302, $"/studio/apps/{slug}?built=1"), await StudioCreateAsync(host, a, slug, "قَصير"));
            var big = await BrandingAsync(host, a, slug, new string('ن', 200_000));
            var tag = await BrandingAsync(host, a, slug, "قَصير", tagline: new string('س', StudioStoreCreate.TagLineMax + 1));
            var city = await BrandingAsync(host, a, slug, "قَصير", city: new string('م', StudioStoreCreate.CityMax + 1));
            var afterRefusals = (await TenantAsync(host, slug))!;
            var edge = await BrandingAsync(host, a, slug, new string('ن', StudioStoreCreate.NameMax),
                tagline: new string('س', StudioStoreCreate.TagLineMax), city: new string('م', StudioStoreCreate.CityMax));
            var t = (await TenantAsync(host, slug))!;
            output.WriteLine($"[live] 200000 ⇒ {big.Location} · سَطرٌ 121 ⇒ {tag.Location} · مَدينَةٌ 41 ⇒ {city.Location} · بَعدَها الاسمُ {afterRefusals.Name.Length} · عَلى الحُدود ⇒ {edge.Location} ({t.Name.Length}/{t.TagLine.Length}/{t.City.Length})");
            Assert.Equal((302, $"/studio/apps/{slug}/branding?err={TenantConfigCodes.NameTooLong}"), big);
            Assert.Equal((302, $"/studio/apps/{slug}/branding?err={TenantConfigCodes.TagLineTooLong}"), tag);
            Assert.Equal((302, $"/studio/apps/{slug}/branding?err={TenantConfigCodes.CityTooLong}"), city);
            Assert.Equal(("قَصير", "", ""), (afterRefusals.Name, afterRefusals.TagLine, afterRefusals.City));
            Assert.Equal((302, $"/studio/apps/{slug}/branding?saved=1"), edge);
            Assert.Equal((StudioStoreCreate.NameMax, StudioStoreCreate.TagLineMax, StudioStoreCreate.CityMax), (t.Name.Length, t.TagLine.Length, t.City.Length));
        }
        finally { await CleanAsync(host, [a], slug); }
    }

    /// <summary>بابُ المُشرِف: اسمٌ فَوقَ حَدِّه ⇒ الصَفحَةُ بِرَمزِه والحَقلُ فارِغٌ في العُنوان، ولا مَتجَر.</summary>
    [Fact]
    public async Task The_admin_create_door_refuses_an_over_long_name_with_a_bounded_location_and_writes_nothing_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var admin = await UserAsync(host, consented: false, platformAdmin: true);
        var slug = NewSlug("w13l-");
        try
        {
            var (status, location) = await AdminCreateAsync(host, admin, slug, new string('ن', 200_000));
            output.WriteLine($"[live] المُشرِفُ بِاسمٍ 200000 ⇒ {status} {location}");
            Assert.Equal(302, status);
            Assert.StartsWith($"/admin/tenants/new?err={StudioStoreCreate.NameTooLong}&slug={slug}&name=&", location, StringComparison.Ordinal);
            Assert.True(location.Length < 1000, $"العُنوانُ {location.Length} حَرفاً");
            Assert.Null(await TenantAsync(host, slug));
        }
        finally { await CleanAsync(host, [admin], slug); }
    }

    /// <summary>أَداتا الوَكيل: <c>create_tenant</c> بِاسمٍ فَوقَ حَدِّه ⇒ نَصُّ الحَدِّ ولا مَتجَرَ ولا عَدّ؛ و<c>set_branding</c> ⇒ نَصُّ الحَدِّ والاسمُ كَما كان.</summary>
    [Fact]
    public async Task Both_agent_tools_refuse_an_over_long_name_with_the_dictionary_text_and_write_nothing_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        var (refused, born) = (NewSlug("w13l-"), NewSlug("w13l-"));
        var limitText = LocaleCatalog.Text(LocaleCatalog.Arabic, StudioStoreCreate.LengthMessageKey(StudioStoreCreate.NameTooLong));
        try
        {
            var exec = new AgentToolExecutor(host.Store, new TenantRoleService(host.Store));
            var create = await exec.ExecuteAsync("create_tenant", AgentPayload(refused, new string('ن', StudioStoreCreate.NameMax + 1)), a.User.Id);
            Assert.Equal((true, AgentCreated(born)), await exec.ExecuteAsync("create_tenant", AgentPayload(born, "قَصير"), a.User.Id));
            var brand = await exec.ExecuteAsync("set_branding", $$"""{"slug":"{{born}}","name":"{{new string('ن', 200_000)}}"}""", a.User.Id);
            var t = (await TenantAsync(host, born))!;
            output.WriteLine($"[live] create_tenant ⇒ {create} · set_branding ⇒ {brand} · الاسم «{t.Name}» · العَدّاد {await StoresBuiltAsync(host, a)}");
            Assert.Equal((false, limitText), create);
            Assert.Null(await TenantAsync(host, refused));
            Assert.Equal((false, limitText), brand);
            Assert.Equal("قَصير", t.Name);
            Assert.Equal(1, await StoresBuiltAsync(host, a));
        }
        finally { await CleanAsync(host, [a], refused, born); }
    }
}
