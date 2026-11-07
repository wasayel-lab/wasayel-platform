using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using ACommerce.Templates.Customer.Marketplace.Services.Patterns;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ مَولِدِ المَتجَرِ قَبلَ أَن يَخرُجَ التَحليلُ مِن الرِحلَة (‏2026-11-07، الموجَة ١٣ W13-1·٢ — ADR-049) ═══
//
// **ما يُزالُ ولا يُثَبَّت**: بابُ `/studio/s/{id}/build` — يَشتَرِطُ جَلسَةَ تَحليلٍ `Completed` لا يَكتُبُها إلّا نِداءُ نَموذَجٍ ناجِح.
// **وما يَجِبُ أَن يَنجوَ — وهُوَ ما يُثَبِّتُه هذا المِلَفُّ، ولا يُعادُ حَرفٌ مِنه حَتّى آخِرِ الموجَة**:
//   (١) المَتجَرُ الَّذي يَكتُبُه المَصنَعُ بِمُدخَلاتِ `/build` لِجَلسَةٍ بِالوَصفِ وَحدَه — نَصُّه القانونيُّ عَدا المُعَرِّفِ والوَقتِ والمَصدَرِ والمالِك،
//       وبَصمَتُه، وسَلَّتُه. وبابُ الإنشاءِ اليَدَوِيِّ (W13-1·٣) يُقاسُ بِهذِه البَصمَةِ بِعَينِها.
//   (٢) تَرتيبُ الحَرَسِ المُشتَرَكِ في كُلِّ نُقطَةِ استوديو تُنشِئُ مَتجَراً: الهُوِيَّة ← حِصَّةُ المَتاجِر ← المُعَرِّف ← الاسم ← اللَون ← قَناةُ الدُخول
//       ← الإنشاء ← العَدّ. عامٌّ لا بِاسمِ `/build`: يَصدُقُ عَلَيه اليَوم، وعَلى بابِ الإنشاءِ غَداً، وعَلى الباقي وَحدَه بَعدَ الإزالَة.
//   (٣) بَصمَةُ git لِثَلاثَةِ مِلَفّاتٍ تَبقى بايتاً كَما هي طَوالَ الموجَة — أَرضِيَّةُ الأَدوار، ونَمَطُ المَتجَر، وبابُ الوَكيل.
// والصِلَةُ بِالبابِ الحَيِّ نَفسِه (لا بِقِراءَتي لِمُعامِلاتِه) في `LiveStudioBuildBirthProofTests`: يُنادي `/build` المُسَجَّلَ عَلى Postgres
// ويُقارِنُ ما كَتَبَه بِالبَصمَةِ نَفسِها — ويَخرُجُ مَعَ البابِ في W13-1·٥، ويَحِلُّ مَحَلَّه بُرهانا الإنشاءِ اليَدَوِيّ.

public class StudioStoreBirthCharacterizationTests(ITestOutputHelper output)
{
    // ─── الاستِمارَةُ المَرجِعِيَّة — ما يَكتُبُه صاحِبُ الفِكرَةِ في حُقولِ البِناء ─────────────────────────────

    internal const string RefName = "مَتجَرُ المَرجِع";
    /// <summary>لَونُ <c>marketplace</c> — ما تُعَبِّئُه الاستِمارَةُ سَلَفاً لِجَلسَةٍ بِالوَصفِ وَحدَه (<c>DeriveSuggestion</c>).</summary>
    internal const string RefColor = "#2563eb";
    internal const string RefTagLine = "سوقٌ لِبائِعينَ كُثُر";
    /// <summary>فارِغَةٌ لِجَلسَةٍ بِالوَصفِ وَحدَه: لا جَوابَ <c>geo</c> تُشتَقُّ مِنه.</summary>
    internal const string RefCity = "";
    internal const string RefChannel = "phone";

    /// <summary>بَصمَةُ SHA-256 لِلنَصِّ القانونيِّ (<see cref="Canonical"/>) — مَقيسَةٌ مِن المَصنَعِ عِندَ `ea5d94d`، ويَطبَعُها الصَفُّ أَدناه بِنَصِّها.</summary>
    internal const string GoldenSha256 = "d082c1cf9d6d7d804cfa75c974d9c13b88d4bf4e4d029b7908dfbf8a9685b3cd";

    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>حُقولٌ تَختَلِفُ بَينَ كُلِّ إنشاءٍ وآخَرَ بِطَبيعَتِها، فَتُقاسُ وَحدَها لا في النَصّ: المُعَرِّفُ (و<c>Slug</c> ظِلُّه)، والوَقت،
    /// والمَصدَرُ (‏<c>/build</c> يَكتُبُ الجَلسَة، والإنشاءُ اليَدَوِيُّ لا شَيء)، والمالِكُ (صاحِبُ الجَلسَةِ في كُلِّ تَشغيل).</summary>
    internal static readonly string[] Varying = ["Id", "Slug", "CreatedAt", "SourceAnalysisId", "OwnerUserId"];

    /// <summary>النَصُّ القانونيُّ لِلمَتجَر: كُلُّ حَقلٍ بِتَرتيبِ تَعريفِه عَدا <see cref="Varying"/> — فَأَيُّ حَقلٍ يُضافُ إلى <c>Tenant</c> يَدخُلُه.</summary>
    internal static string Canonical(Tenant t)
    {
        var node = JsonSerializer.SerializeToNode(t, Json)!.AsObject();
        foreach (var k in Varying) node.Remove(k);
        return node.ToJsonString(Json);
    }

    internal static string Sha256(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    /// <summary>المَتجَرُ بِمُدخَلاتِ <c>/build</c> لِجَلسَةٍ بِالوَصفِ وَحدَه: النَمَطُ <c>session.SuggestedPattern</c> كَما يَترُكُه مَسارُ العَميل،
    /// والقِطاعُ <c>Answers["sector"]</c> الغائِبُ ⇒ <c>""</c>.</summary>
    private static async Task<(Tenant Tenant, DocWorld World)> BirthAsync(Guid owner)
    {
        var world = new DocWorld();
        var session = new IncubatorSession { Id = Guid.NewGuid(), OwnerUserId = owner, Status = IncubatorStatus.Completed };
        var sector = session.Answers.TryGetValue("sector", out var sec) ? sec : "";
        var tenant = await new TenantFromAnalysisFactory(world.Store).CreateAsync(
            "birth-probe", RefName, RefColor, RefTagLine, RefCity, session.SuggestedPattern, sector, RefChannel, owner, session.Id);
        return (tenant, world);
    }

    [Fact]
    public async Task The_store_the_factory_writes_for_a_description_only_session_is_pinned_byte_for_byte_minus_id_time_source_owner()
    {
        var owner = Guid.NewGuid();
        var (_, world) = await BirthAsync(owner);
        var stored = world.Read<Tenant>("birth-probe");
        Assert.NotNull(stored);
        var canonical = Canonical(stored!);
        output.WriteLine($"النَصُّ القانونيّ ({Encoding.UTF8.GetByteCount(canonical)} بايتاً):\n{canonical}\nSHA-256 = {Sha256(canonical)}");

        Assert.Equal(1, world.Wrote<Tenant>());
        Assert.Equal(1, world.SaveCalls);
        Assert.Equal(owner, stored!.OwnerUserId);
        Assert.Equal(GoldenSha256, Sha256(canonical));
    }

    /// <summary>وما تَعنيه البَصمَةُ مَقروءاً: فِئَتا <c>products</c>/<c>deals</c> بِكِندٍ فارِغ، والأَدوارُ الثَلاثَةُ والافتِراضيُّ أَوَّلُها،
    /// والقَناةُ والهُوِيَّةُ كَما كُتِبَت، و<c>PatternOf</c> سوقٌ بِسَلَّةٍ (‏FLGAP-04 — ADR-034).</summary>
    [Fact]
    public async Task The_pinned_store_is_a_marketplace_with_a_cart_three_catalog_roles_and_the_form_it_was_given()
    {
        var (tenant, _) = await BirthAsync(Guid.NewGuid());

        Assert.Equal(new[] { "products", "deals" }, tenant.Categories.Select(c => c.Slug).ToArray());
        Assert.All(tenant.Categories, c => Assert.Equal("", c.Kind));
        Assert.Equal(new[] { "customer", "vendor", "tenant_admin" }, tenant.Roles.Select(r => r.CatalogSlug).ToArray());
        Assert.Equal("customer", tenant.Roles.Single(r => r.IsDefault).CatalogSlug);
        Assert.Equal((RefName, RefColor, RefTagLine, RefCity, RefChannel), (tenant.Name, tenant.BrandColor, tenant.TagLine, tenant.City, tenant.AuthChannel));
        Assert.Equal(AppPattern.Marketplace, PatternProfileResolver.PatternOf(tenant));
        Assert.True(PatternProfileResolver.Of(tenant).UsesCart);
        Assert.Null(tenant.CartMode);
        Assert.False(tenant.IsSuspended);
    }

    // ─── (٢) تَرتيبُ الحَرَسِ المُشتَرَكِ في كُلِّ نُقطَةِ استوديو تُنشِئُ مَتجَراً ─────────────────────────────

    /// <summary>عَلاماتُ الحَرَسِ المُشتَرَكِ بِتَرتيبِها — كُلٌّ نَمَطٌ يُطابِقُ أَوَّلَ ظُهورٍ لَه في جِسمِ النُقطَة. وما يَخُصُّ <c>/build</c>
    /// وَحدَه (مِلكِيَّةُ الجَلسَةِ و<c>Completed</c>) لَيسَ مِنها: يَخرُجُ مَعَ البابِ ولا يُنسَخ.</summary>
    internal static readonly (string Name, Regex Marker)[] SharedGuardOrder =
    [
        ("الهُوِيَّة",        new(@"\bauth\.IsAuthenticated\b")),
        ("حِصَّةُ المَتاجِر",  new(@"\.CheckBuildAsync\(")),
        ("المُعَرِّف",        new(@"\.ValidateSlugAsync\(")),
        ("الاسم",           new(@"name_required|\bNameRequired\b")),
        ("اللَون",          new(@"color_invalid|\bColorInvalid\b")),
        ("قَناةُ الدُخول",    new(@"\bTenantAuthChannelDoor\.Default\(")),
        ("الإنشاء",         new(@"\.CreateAsync\(")),
        ("العَدّ",           new(@"\.RecordStoreBuiltAsync\(")),
    ];

    /// <summary>مَواضِعُ العَلاماتِ في جِسمٍ — <c>-1</c> لِلغائِبَة.</summary>
    internal static int[] GuardPositions(string body)
        => SharedGuardOrder.Select(g => g.Marker.Match(body) is { Success: true } m ? m.Index : -1).ToArray();

    /// <summary>أَمُرَتَّبٌ الحَرَسُ؟ — كُلُّ عَلامَةٍ حاضِرَةٌ وبَعدَ سابِقَتِها.</summary>
    internal static bool InOrder(int[] at) => at.All(p => p >= 0) && at.Zip(at.Skip(1)).All(p => p.First < p.Second);

    /// <summary>نِقاطُ الاستوديو الَّتي تُنشِئُ مَتجَراً بِالمَصنَع — مَصدَرُها ماسِحُ نِقاطِ minimal API الواحِد (<see cref="WriteEndpointGuardTests"/>).</summary>
    internal static IReadOnlyList<WriteEndpointGuardTests.Endpoint> StoreBirthEndpoints()
        => WriteEndpointGuardTests.AllMinimalApiEndpoints()
            .Where(e => e.Route.StartsWith("/studio", StringComparison.Ordinal)
                     && e.Body.Contains("TenantFromAnalysisFactory", StringComparison.Ordinal)
                     && e.Body.Contains(".CreateAsync(", StringComparison.Ordinal))
            .ToList();

    [Fact]
    public void Every_studio_endpoint_that_births_a_store_keeps_the_shared_guard_order()
    {
        var endpoints = StoreBirthEndpoints();
        foreach (var e in endpoints)
            output.WriteLine($"{e.Route} ({e.File}): " + string.Join(" ← ", SharedGuardOrder.Select(g => g.Name).Zip(GuardPositions(e.Body), (n, p) => $"{n}@{p}")));

        Assert.True(endpoints.Count >= 1, $"أَداةٌ عَمياء: {endpoints.Count} نُقطَةَ استوديو تُنشِئُ مَتجَراً — والمَقيسُ 1 فَأَكثَر.");
        var broken = endpoints.Where(e => !InOrder(GuardPositions(e.Body))).Select(e => e.Route).ToList();
        Assert.True(broken.Count == 0, "نُقطَةُ استوديو تُنشِئُ مَتجَراً بِحَرَسٍ ناقِصٍ أَو مَقلوب: " + string.Join("، ", broken));
    }

    /// <summary><b>الماسِحُ يُقاسُ قَبلَ أَن يُوثَقَ بِه</b> (القاعِدَة ١٠): الجِسمُ المُرَتَّبُ يَمُرّ، وكُلُّ عَلامَةٍ تُحذَفُ أَو تُقَدَّمُ عَلى سابِقَتِها تُحمِرُّه.</summary>
    [Theory]
    [InlineData("ordered", true)]
    [InlineData("ordered-with-consent-between", true)]
    [InlineData("no-identity", false)]
    [InlineData("quota-after-slug", false)]
    [InlineData("channel-before-color", false)]
    [InlineData("record-before-create", false)]
    [InlineData("no-record", false)]
    public void The_guard_order_scanner_goes_red_on_a_missing_or_swapped_guard(string sample, bool ordered)
    {
        const string identity = "if (!auth.IsAuthenticated) return R;";
        const string consent = "if (!consented) return C;";
        const string quota = "var g = await tier.CheckBuildAsync(o);";
        const string slug = "var e = await factory.ValidateSlugAsync(s);";
        const string name = "if (n == \"\") return \"name_required\";";
        const string color = "if (!ok) return \"color_invalid\";";
        const string channel = "var ch = TenantAuthChannelDoor.Default(x);";
        const string create = "await factory.CreateAsync(a);";
        const string record = "await tier.RecordStoreBuiltAsync(o);";
        var body = sample switch
        {
            "ordered" => identity + quota + slug + name + color + channel + create + record,
            "ordered-with-consent-between" => identity + consent + quota + slug + name + color + channel + create + record,
            "no-identity" => quota + slug + name + color + channel + create + record,
            "quota-after-slug" => identity + slug + quota + name + color + channel + create + record,
            "channel-before-color" => identity + quota + slug + name + channel + color + create + record,
            "record-before-create" => identity + quota + slug + name + color + channel + record + create,
            "no-record" => identity + quota + slug + name + color + channel + create,
            _ => throw new ArgumentOutOfRangeException(nameof(sample)),
        };
        Assert.Equal(ordered, InOrder(GuardPositions(body)));
    }

    // ─── (٣) ثَلاثَةُ مِلَفّاتٍ تَبقى بايتاً كَما هي طَوالَ الموجَة ──────────────────────────────────────────

    /// <summary>بَصمَةُ <c>git hash-object</c> — ‏SHA-1 عَلى <c>blob {الطول}\0</c> ثُمَّ البايتات، كَما يَحسُبُها git لِمِلَفٍّ بِلا تَحويلِ أَسطُر.</summary>
    internal static string GitBlobHash(byte[] bytes)
    {
        var head = Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
        return Convert.ToHexString(SHA1.HashData([.. head, .. bytes])).ToLowerInvariant();
    }

    /// <summary>المِلَفّاتُ الثَلاثَةُ وبَصَماتُها عِندَ `ea5d94d`. <b>شَرطُ رَفعِ الصَفّ</b>: نِهايَةُ الموجَة ١٣ — ومَن عَدَّلَ مِلَفّاً مِنها قَبلَها
    /// كَسَرَ بُرهانَ «أَبقِ» (أَرضِيَّةُ الأَدوار، ونَمَطُ المَتجَر، وبابُ الوَكيلِ واختِباراتُه)، فَيَحمَرُّ هُنا ويُقالُ في كوميتِه.</summary>
    internal static readonly (string File, string Hash)[] FrozenThroughWave13 =
    [
        ("tests/ACommerce.Platform.Tests/StudioStorePatternTests.cs", "6f7c6a2afeaac8da389fc24ab0fda03a06f0edce"),
        ("tests/ACommerce.Platform.Tests/StudioBuildRoleFloorTests.cs", "a4947df959c795f88be5fdbb03e8d077e77f4aaf"),
        ("tests/ACommerce.Platform.Tests/StudioAgentDoorTests.cs", "110fde0c82048ba83ac91795bf294a5808f52670"),
    ];

    [Fact]
    public void The_three_anchor_test_files_stay_byte_identical_through_the_wave()
    {
        var changed = new List<string>();
        foreach (var (file, hash) in FrozenThroughWave13)
        {
            var actual = GitBlobHash(File.ReadAllBytes(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, file)));
            output.WriteLine($"{file}: {actual}");
            if (actual != hash) changed.Add($"{file} ({hash} ← {actual})");
        }
        Assert.Equal(3, FrozenThroughWave13.Length);
        Assert.True(changed.Count == 0, "مِلَفُّ مِرساةٍ تَغَيَّرَ في الموجَة: " + string.Join("، ", changed));
    }

    /// <summary>والحاسِبُ يُقاسُ بِقيمَةٍ يَعرِفُها git لِكُلِّ مُستَودَع: البَصمَةُ المَعروفَةُ لِلمِلَفِّ الفارِغ، ولِسَطرٍ واحِد.</summary>
    [Theory]
    [InlineData("", "e69de29bb2d1d6434b8b29ae775ad8c2e48c5391")]
    [InlineData("hello\n", "ce013625030ba8dba906f756967f9e9ca394464a")]
    public void The_blob_hasher_matches_git(string content, string expected)
        => Assert.Equal(expected, GitBlobHash(Encoding.UTF8.GetBytes(content)));
}
