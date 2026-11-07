using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ أَجوِبَةِ بابِ المُعَرِّفِ الواحِدِ عِندَ الأَبوابِ الثَلاثَةِ قَبلَ أَن يَسأَلَ مُصادِقَ اسمِ المُضيف (‏2026-11-07، ADR-050 §٣ · W13-4·C2a·3 — القاعِدَة ٣) ═══
//
// **ما يُثَبَّتُ هُنا ما يَنجو لا ما يَتَغَيَّر**: `SlugRefusal` عِندَ `8a8b8c0` شَكلُه `^[a-z0-9][a-z0-9-]{0,62}$` والمَحجوزُ قَبلَه. وبَعدَ أَن يَسأَلَ
// `StoreHostName` تَتَغَيَّرُ عَمداً خَمسُ فِئاتٍ لا تَرِدُ هُنا: شَرطَةٌ في الآخِر (`store-`)، وشَرطَتانِ مُتَتالِيَتانِ (`ab--cd`، `xn--…`)، وطولٌ بَينَ 54 و63،
// ولاحِقَةُ اسمِ دَورٍ (`best-vendor`)، والجُذورُ الثَلاثَةُ الَّتي تُحجَزُ في البَندِ ٤ (`lang`، `sw.js`، `offline.html`). وكُلُّ ما سِواها جَوابُه هُنا قَبلُ
// وبَعدُ بِحَرفِه — والمِلَفُّ يَبقى بِبَصمَتِه نَفسِها بَعدَ التَبديل.

public class SlugDoorHostShapeCharacterizationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(null, TenantFromAnalysisFactory.SlugRequired)]
    [InlineData("", TenantFromAnalysisFactory.SlugRequired)]
    [InlineData("pricing", TenantFromAnalysisFactory.SlugReserved)]
    [InlineData("www", TenantFromAnalysisFactory.SlugReserved)]
    [InlineData("studio", TenantFromAnalysisFactory.SlugReserved)]
    [InlineData("api", TenantFromAnalysisFactory.SlugReserved)]
    [InlineData("Terms", TenantFromAnalysisFactory.SlugReserved)]
    [InlineData("_studio", TenantFromAnalysisFactory.SlugReserved)]
    [InlineData("_platform", TenantFromAnalysisFactory.SlugReserved)]
    [InlineData("-", TenantFromAnalysisFactory.SlugFormat)]
    [InlineData("-store", TenantFromAnalysisFactory.SlugFormat)]
    [InlineData("_store", TenantFromAnalysisFactory.SlugFormat)]
    [InlineData("my_store", TenantFromAnalysisFactory.SlugFormat)]
    [InlineData("My-Store", TenantFromAnalysisFactory.SlugFormat)]
    [InlineData("a b", TenantFromAnalysisFactory.SlugFormat)]
    [InlineData("a.b", TenantFromAnalysisFactory.SlugFormat)]
    [InlineData("مَتجَر", TenantFromAnalysisFactory.SlugFormat)]
    [InlineData(" ", TenantFromAnalysisFactory.SlugFormat)]
    [InlineData("a", null)]
    [InlineData("0", null)]
    [InlineData("my-store", null)]
    [InlineData("studio-store", null)]
    [InlineData("theme-demo", null)]
    [InlineData("owner-test", null)]
    [InlineData("ashare", null)]
    [InlineData("vendors", null)]
    [InlineData("vendor", null)]
    [InlineData("a-b-c", null)]
    public void The_one_slug_door_answers_the_surviving_inputs_as_today(string? slug, string? code)
    {
        output.WriteLine($"«{slug}» ⇒ {TenantFromAnalysisFactory.SlugRefusal(slug) ?? "يَمُرّ"}");
        Assert.Equal(code, TenantFromAnalysisFactory.SlugRefusal(slug));
    }

    /// <summary>وأَطوَلُ ما يَمُرُّ بَعدَ التَبديلِ (53) يَمُرُّ اليَوم، وما فَوقَ 63 يُرَدُّ اليَومَ وبَعدَه.</summary>
    [Theory]
    [InlineData(1, null)]
    [InlineData(53, null)]
    [InlineData(64, TenantFromAnalysisFactory.SlugFormat)]
    [InlineData(200, TenantFromAnalysisFactory.SlugFormat)]
    public void The_lengths_both_shapes_agree_on(int length, string? code)
        => Assert.Equal(code, TenantFromAnalysisFactory.SlugRefusal(new string('a', length)));
}

// ─── الأَبوابُ الثَلاثَةُ عَلى Postgres ────────────────────────────────────────────────────────────────────────────

[Collection(LiveProofCollection.Name)]
public class LiveSlugDoorHostShapeCharacterizationProofTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>جَوابُ كُلِّ بابٍ اليَومَ لِمُدخَلاتٍ يَبقى جَوابُها بَعدَ التَبديل: الاستوديو يُعيدُ الاستِمارَةَ بِرَمزِ الرَفض، والمُشرِفُ يُعيدُ صَفحَتَه
    /// بِـ<c>slug_reserved</c> لِلمَحجوزِ و<c>slug_required</c> لِما سِواه (رَمزُه القائِم)، والوَكيلُ «slug غَير صالِح.» — ولا مَتجَرَ يولَد.
    /// والاستوديو والمُشرِفُ يُصَغِّرانِ الحُروفَ ويَقُصّانِ المَسافاتِ قَبلَ السُؤال، والوَكيلُ يُصَغِّرُ وَحدَه؛ ومُخَطَّطُ أَداةِ الوَكيلِ (<c>^[A-Za-z0-9_-]+$</c>) يَرُدُّ
    /// قَبلَ البابِ ما فيه نُقطَةٌ أَو مَسافَة (مَقيس: «a.b» ⇐ «رُفِضَت الحُمولَة — مُخالَفَة مُخَطَّط…»)، فَالصُفوفُ هُنا مِمّا يَبلُغُ البابَ في الثَلاثَة.</summary>
    [Fact]
    public async Task The_three_doors_refuse_the_surviving_inputs_with_todays_answers_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        var admin = await UserAsync(host, consented: false, platformAdmin: true);
        var exec = new AgentToolExecutor(host.Store, new TenantRoleService(host.Store));
        // (المُدخَل، رَمزُ الاستوديو، رَمزُ المُشرِف)
        (string Slug, string Studio, string Admin)[] rows =
        [
            ("pricing", TenantFromAnalysisFactory.SlugReserved, TenantFromAnalysisFactory.SlugReserved),
            ("www", TenantFromAnalysisFactory.SlugReserved, TenantFromAnalysisFactory.SlugReserved),
            ("-store", TenantFromAnalysisFactory.SlugFormat, TenantFromAnalysisFactory.SlugRequired),
            ("my_store", TenantFromAnalysisFactory.SlugFormat, TenantFromAnalysisFactory.SlugRequired),
            ("_store", TenantFromAnalysisFactory.SlugFormat, TenantFromAnalysisFactory.SlugRequired),
        ];
        try
        {
            foreach (var (slug, studioCode, adminCode) in rows)
            {
                var studio = await StudioCreateAsync(host, a, slug, "مَتجَر");
                var byAdmin = await AdminCreateAsync(host, admin, slug, "مَتجَر");
                var byAgent = await exec.ExecuteAsync("create_tenant", AgentPayload(slug, "مَتجَر"), a.User.Id);
                output.WriteLine($"[live] «{slug}» ⇒ الاستوديو {studio.Location} · المُشرِف {byAdmin.Location} · الوَكيل {byAgent}");
                Assert.Equal((302, StudioStoreCreate.Refused(studioCode, new StudioStoreCreate.Form(slug, "مَتجَر", StudioStoreCreate.DefaultColor, ""))), studio);
                Assert.Equal(302, byAdmin.Status);
                Assert.StartsWith($"/admin/tenants/new?err={adminCode}&slug={Uri.EscapeDataString(slug)}&", byAdmin.Location, StringComparison.Ordinal);
                Assert.Equal((false, "slug غَير صالِح."), byAgent);
                Assert.Null(await TenantAsync(host, slug));
            }
            Assert.Equal(0, await StoresBuiltAsync(host, a));
        }
        finally { await CleanAsync(host, [a, admin], [.. rows.Select(r => r.Slug)]); }
    }

    /// <summary>ومُعَرِّفٌ بِالشَرطَةِ في وَسَطِه يولَدُ في الأَبوابِ الثَلاثَة، وتَكرارُه مَأخوذٌ في كُلٍّ مِنها بِجَوابِه القائِم.</summary>
    [Fact]
    public async Task A_hyphenated_slug_is_born_at_each_door_and_taken_on_repeat_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        var admin = await UserAsync(host, consented: false, platformAdmin: true);
        var exec = new AgentToolExecutor(host.Store, new TenantRoleService(host.Store));
        var (s1, s2, s3) = (NewSlug("w13c-a-"), NewSlug("w13c-b-"), NewSlug("w13c-c-"));
        try
        {
            var studio = await StudioCreateAsync(host, a, s1, "مَتجَر");
            var byAdmin = await AdminCreateAsync(host, admin, s2, "مَتجَر");
            var byAgent = await exec.ExecuteAsync("create_tenant", AgentPayload(s3, "مَتجَر"), a.User.Id);
            var studioAgain = await StudioCreateAsync(host, a, s2, "مَتجَر");
            var adminAgain = await AdminCreateAsync(host, admin, s3, "مَتجَر");
            var agentAgain = await exec.ExecuteAsync("create_tenant", AgentPayload(s1, "مَتجَر"), a.User.Id);
            output.WriteLine($"[live] {s1} ⇒ {studio.Location} · {s2} ⇒ {byAdmin.Location} · {s3} ⇒ {byAgent} · التَكرار: {studioAgain.Location} · {adminAgain.Location} · {agentAgain}");
            Assert.Equal((302, $"/studio/apps/{s1}?built=1"), studio);
            Assert.Equal((302, "/admin"), byAdmin);
            Assert.Equal((true, AgentCreated(s3)), byAgent);
            Assert.Equal((302, StudioSlugTaken(s2, "مَتجَر")), studioAgain);
            Assert.Equal((302, AdminSlugTaken(s3, "مَتجَر")), adminAgain);
            Assert.Equal((false, AgentSlugTaken(s1)), agentAgain);
        }
        finally { await CleanAsync(host, [a, admin], s1, s2, s3); }
    }
}
