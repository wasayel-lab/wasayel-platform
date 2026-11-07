using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Audit;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ شَكلُ مُعَرِّفِ المَتجَرِ الواحِدُ لِلأَبوابِ الثَلاثَة، وأَقسامُ القاعِدَةِ الداخِلِيَّةُ لا تَصيرُ مَتاجِر (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·SEC-C1-2) ═══
//
// **المَقيسُ قَبل** (تَقريرُ العَدَسَة، عِندَ `cd829ab`): حِسابُ استوديو مُوافِقٌ أَنشَأَ `_studio` و`_incubator` و`_platform` و`-` بِـ`302 …?built=1` لِكُلٍّ —
// `ValidateSlugAsync` بِـ`^[a-z0-9_-]+$` والمَحجوزُ `_admin` وَحدَه مِن الأَقسام. فَعَميلٌ يَدخُلُ مَتجَرَ `_studio` يَحمِلُ رَمزاً `{id}|_studio|…` قَبِلَه
// الاستوديو جَلسَةً وكَتَبَ بِه مُوافَقَة؛ ومالِكُ `_platform` كَتَبَ سَطرَ تَدقيقٍ في نِطاقِ المَنَصَّة؛ وبابُ التَصديرِ يَرُدُّ المَتاجِرَ نَفسَها `slug_reserved`.
// وبابُ المُشرِفِ (`/admin/tenants/create`) وأَداةُ الوَكيلِ (`create_tenant`) بِالشَكلِ نَفسِه ولا قائِمَةَ مَحجوزٍ فيهِما.
// **والآن** `TenantFromAnalysisFactory.SlugRefusal` شَكلٌ واحِدٌ (`^[a-z0-9][a-z0-9-]{0,62}$`) ومَحجوزٌ واحِدٌ (`ReservedTenantSlugs` + `InternalPartitions` بِثَوابِتِها)
// يَسأَلُه الثَلاثَة.

public class SlugShapeTests(ITestOutputHelper output)
{
    /// <summary>أَقسامُ Marten الداخِلِيَّةُ بِثَوابِتِها في الكود — لا بِنُسَخٍ مِنها.</summary>
    public static TheoryData<string> Partitions() => new()
    {
        StudioAuth.Tenant, FeasibilityAnalysisService.IncubatorTenant, AuditWriter.PlatformScope, AgentService.AdminTenant,
    };

    [Fact]
    public void The_internal_partitions_are_reserved_by_their_constants()
    {
        output.WriteLine("الأَقسام: " + string.Join(" · ", TenantFromAnalysisFactory.InternalPartitions));
        Assert.Equal(new[] { StudioAuth.Tenant, FeasibilityAnalysisService.IncubatorTenant, AuditWriter.PlatformScope, AgentService.AdminTenant },
                     TenantFromAnalysisFactory.InternalPartitions);
    }

    [Theory]
    [MemberData(nameof(Partitions))]
    public async Task Every_internal_partition_is_refused_by_the_one_slug_door(string partition)
    {
        var refused = await new TenantFromAnalysisFactory(new DocWorld().Store).ValidateSlugAsync(partition);
        output.WriteLine($"{partition} ⇒ {refused}");
        Assert.Equal(TenantFromAnalysisFactory.SlugReserved, refused);
        Assert.Contains(partition, TenantFromAnalysisFactory.InternalPartitions);
    }

    [Theory]
    [InlineData("-", TenantFromAnalysisFactory.SlugFormat)]
    [InlineData("-store", TenantFromAnalysisFactory.SlugFormat)]
    [InlineData("_store", TenantFromAnalysisFactory.SlugFormat)]
    [InlineData("my_store", TenantFromAnalysisFactory.SlugFormat)]
    [InlineData("My-Store", TenantFromAnalysisFactory.SlugFormat)]
    [InlineData("pricing", TenantFromAnalysisFactory.SlugReserved)]
    [InlineData("", TenantFromAnalysisFactory.SlugRequired)]
    public void A_slug_off_the_one_shape_or_reserved_is_refused_with_its_code(string slug, string code)
        => Assert.Equal(code, TenantFromAnalysisFactory.SlugRefusal(slug));

    [Theory]
    [InlineData("a")]
    [InlineData("0")]
    [InlineData("my-store")]
    [InlineData("store-")]
    [InlineData("studio-store")]
    public void A_slug_on_the_one_shape_and_not_reserved_passes(string slug)
        => Assert.Null(TenantFromAnalysisFactory.SlugRefusal(slug));

    /// <summary>الطولُ: ‏63 حَدُّ مِلصَقِ المُضيفِ في DNS — ‏63 يَمُرّ و64 يُرَدّ.</summary>
    [Theory]
    [InlineData(63, null)]
    [InlineData(64, TenantFromAnalysisFactory.SlugFormat)]
    public void The_slug_is_one_host_label_long_at_most(int length, string? code)
        => Assert.Equal(code, TenantFromAnalysisFactory.SlugRefusal(new string('a', length)));

    /// <summary>الأَبوابُ الثَلاثَةُ تَسأَلُ الشَكلَ الواحِد — ولا نَمَطَ مُعَرِّفٍ ثانٍ في أَيٍّ مِنها.</summary>
    [Fact]
    public void The_three_store_birth_doors_ask_the_one_slug_shape()
    {
        var studio = WriteEndpointGuardTests.AllMinimalApiEndpoints().Single(e => e.Route == StudioStoreCreate.Endpoint).Body;
        var admin = WriteEndpointGuardTests.AllMinimalApiEndpoints().Single(e => e.Route == "/admin/tenants/create").Body;
        var agent = AgentStoreLimitRefusalTests.CreateTenantBody();
        foreach (var (door, code, ask) in new[] { ("studio", studio, "factory.ValidateSlugAsync("), ("admin", admin, "TenantFromAnalysisFactory.SlugRefusal("),
                                                   ("agent", agent, "TenantFromAnalysisFactory.SlugRefusal(") })
        {
            var body = WriteEndpointGuardTests.StripComments(code);
            output.WriteLine($"{door}: {ask}@{body.IndexOf(ask, StringComparison.Ordinal)}");
            Assert.Contains(ask, body, StringComparison.Ordinal);
            Assert.DoesNotContain("[a-z0-9_-]", body, StringComparison.Ordinal);
        }
    }
}

// ─── البُرهانُ عَلى Postgres ────────────────────────────────────────────────────────────────────────────────

[Collection(LiveProofCollection.Name)]
public class LiveSlugShapeProofTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static readonly string[] Partitions =
        [StudioAuth.Tenant, FeasibilityAnalysisService.IncubatorTenant, AuditWriter.PlatformScope, AgentService.AdminTenant];
    private static readonly string[] OffShape = ["-", "a_b"];
    private static string[] Refused => [.. Partitions, .. OffShape];

    [Fact]
    public async Task No_door_births_a_store_on_an_internal_partition_or_off_the_one_shape_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        var admin = await UserAsync(host, consented: false, platformAdmin: true);
        var exec = new AgentToolExecutor(host.Store, new TenantRoleService(host.Store));
        try
        {
            foreach (var slug in Refused)
            {
                var studio = await StudioCreateAsync(host, a, slug, "مَتجَر");
                var byAdmin = await AdminCreateAsync(host, admin, slug, "مَتجَر");
                var byAgent = await exec.ExecuteAsync("create_tenant", AgentPayload(slug, "مَتجَر"), a.User.Id);
                output.WriteLine($"[live] «{slug}» ⇒ الاستوديو {studio.Location} · المُشرِف {byAdmin.Location} · الوَكيل {byAgent}");
                var code = Partitions.Contains(slug) ? TenantFromAnalysisFactory.SlugReserved : TenantFromAnalysisFactory.SlugFormat;
                Assert.Equal((302, StudioStoreCreate.Refused(code, new StudioStoreCreate.Form(slug, "مَتجَر", StudioStoreCreate.DefaultColor, ""))), studio);
                Assert.Equal(302, byAdmin.Status);
                Assert.StartsWith(Partitions.Contains(slug) ? "/admin/tenants/new?err=slug_reserved&" : "/admin/tenants/new?err=slug_required&",
                                  byAdmin.Location, StringComparison.Ordinal);
                Assert.Equal((false, "slug غَير صالِح."), byAgent);
                Assert.Null(await TenantAsync(host, slug));
            }
            Assert.Equal(0, await StoresBuiltAsync(host, a));
        }
        finally { await CleanAsync(host, [a, admin], Refused); }
    }

    /// <summary>والسالِب: المَحجوزُ بِالمُعجَمِ يُرَدُّ في بابِ المُشرِفِ بِرَمزِه (كانَ يُنشَأ)، والمُعَرِّفُ عَلى الشَكلِ يولَدُ في الأَبوابِ الثَلاثَة.</summary>
    [Fact]
    public async Task A_reserved_word_is_slug_reserved_at_the_admin_door_and_an_on_shape_slug_is_born_at_all_three_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        var admin = await UserAsync(host, consented: false, platformAdmin: true);
        var (s1, s2, s3) = (NewSlug("w13s-"), NewSlug("w13s-"), NewSlug("w13s-"));
        try
        {
            var reserved = await AdminCreateAsync(host, admin, "pricing", "مَتجَر");
            var studio = await StudioCreateAsync(host, a, s1, "مَتجَر");
            var byAdmin = await AdminCreateAsync(host, admin, s2, "مَتجَر");
            var byAgent = await new AgentToolExecutor(host.Store, new TenantRoleService(host.Store))
                .ExecuteAsync("create_tenant", AgentPayload(s3, "مَتجَر"), a.User.Id);
            output.WriteLine($"[live] pricing@المُشرِف ⇒ {reserved.Location} · {s1} ⇒ {studio.Location} · {s2} ⇒ {byAdmin.Location} · {s3} ⇒ {byAgent}");
            Assert.StartsWith("/admin/tenants/new?err=slug_reserved&", reserved.Location, StringComparison.Ordinal);
            Assert.Null(await TenantAsync(host, "pricing"));
            Assert.Equal((302, $"/studio/apps/{s1}?built=1"), studio);
            Assert.Equal((302, "/admin"), byAdmin);
            Assert.Equal((true, AgentCreated(s3)), byAgent);
        }
        finally { await CleanAsync(host, [a, admin], s1, s2, s3); }
    }
}
