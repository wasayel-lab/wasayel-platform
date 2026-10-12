using System.Text.RegularExpressions;
using ACommerce.Platform.Hosting;
using ACommerce.Templates.Customer.Marketplace;
using JasperFx;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ فِعلُ المُخَطَّط (`scripts/marten-schema.cs`) — يَعرِضُ ويُطَبِّقُ ما يُشَغِّلُه التَطبيقُ لا نُسخَةً مِنه (‏P3-1) ═══
//
// **الكَلفَةُ الَّتي كَتَبَته** (‏2026-10-12): `DEPLOY.md` §٥ كانَ يَقولُ «والإنتاجُ لا يُنشِئ مُخَطَّطاً» وعِلاجُه
// «تُطَبَّقُ تَغييراتُ المُخَطَّطِ قَبلَ النَشر» بِلا أَمر؛ و`grep ApplyAllConfiguredChangesToDatabaseAsync|
// AssertDatabaseMatchesConfiguration` في `libs/ apps/ scripts/ docs/` = صِفر. والمَقيسُ خِلافُ الدَعوى: خارِجَ
// Development يَبقى افتِراضُ Marten ‏`CreateOrUpdate`، فَالإنتاجُ يُغَيِّرُ مُخَطَّطَه عِندَ أَوَّلِ استِعمالٍ بِلا أَن يُرى.
//
// **والحَدُّ الَّذي يَحرُسُه هذا المِلَفّ** (القاعِدَة ٢): الفِعلُ يُرَكِّبُ إعدادَ Marten بِنِداءَي التَطبيقِ نَفسَيهِما.
// فَإن ظَهَرَ مَوضِعُ إعدادٍ ثالِثٌ لا يَمُرُّ بِهِما عَرَضَ الفِعلُ مُخَطَّطاً غَيرَ الَّذي يُشَغَّل — فَيَحمَرُّ هُنا قَبلَ ذلك.
// والبُرهانُ الحَيُّ لِلفِعلِ نَفسِه (عَرضٌ وتَطبيقٌ وتَوكيدٌ ولَقطَةٌ عَلى Postgres) في رِسالَةِ الكوميتِ و§٤‑د.

public class MartenSchemaVerbTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Read(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path}");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 1000, $"أَداةٌ عَمياء: {path} طولُه {text.Length} — لَم يُقرَأ.");
        return text;
    }

    private static string Verb() => Read("scripts", "marten-schema.cs");

    /// <summary>الفِعلُ بِلا تَعليقاتِه ولا تَوجيهاتِ <c>#:</c> — فَما يُفحَصُ «لا يَكتُبُ» هُوَ الكودُ لا شَرحُه
    /// (الرَأسُ يَذكُرُ <c>AutoCreate.All</c> و<c>Schema.For&lt;…&gt;</c> لِيَقولَ إنَّه لا يَفعَلُهُما).</summary>
    private static string VerbCode() => string.Join('\n', Verb().Split('\n')
        .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)
                 && !l.StartsWith("#", StringComparison.Ordinal)));

    /// <summary>تَركيبُ المَخزَنِ كَما يُرَكِّبُه التَطبيقُ والفِعلُ: النِداءانِ نَفسُهُما، وسِلسِلَةٌ لا يُتَّصَلُ بِها —
    /// <c>AllObjects</c> والإعدادُ يُقرَآنِ مِن التَهيئَةِ وَحدَها.</summary>
    private static async Task<T> WithComposedStore<T>(string environment, Func<DocumentStore, T> read)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Configuration["ConnectionStrings:Postgres"] = "Host=localhost;Database=compose_only;Username=u;Password=p";
        // ‏Development يَتَحَقَّقُ مِن الحاوِيَةِ عِندَ البِناء، وخِدماتُ القالِبِ تَنتَظِرُ ما يُسَجِّلُه `Program.cs` (مُزَوِّدُ الدَفعِ
        // والتَخزينِ وعَميلُ OTO) — والمَقيسُ هُنا إعدادُ Marten وَحدَه، فَيُرفَعُ التَحَقُّقُ في البِناءِ لا في الإعداد.
        builder.Host.UseDefaultServiceProvider(o => { o.ValidateOnBuild = false; o.ValidateScopes = false; });
        builder.AddPlatformHost();
        builder.Services.AddCustomerMarketplaceTemplate();
        await using var app = builder.Build();
        return read((DocumentStore)app.Services.GetRequiredService<IDocumentStore>());
    }

    /// <summary><b>افتِراضُ Marten ‏<c>CreateOrUpdate</c>، وتَركيبُ Production يُبقيه</b> — فَالإنتاجُ يُنشِئُ ويُعَدِّلُ
    /// المُخَطَّطَ بِنَفسِه عِندَ أَوَّلِ استِعمال. هذا ما صَحَّحَ §٥ وكَتَبَ §٤‑د؛ فَإن تَبَدَّلَ الافتِراضُ بِتَرقِيَةٍ حَمِرَ هُنا
    /// وأُعيدَت كِتابَةُ الفِقرَتَين.</summary>
    [Fact]
    public async Task ProductionComposition_LeavesMartensDefault_CreateOrUpdate()
    {
        Assert.Equal(AutoCreate.CreateOrUpdate, new StoreOptions().AutoCreateSchemaObjects);
        Assert.Equal(AutoCreate.CreateOrUpdate,
            await WithComposedStore(Environments.Production, s => s.Options.AutoCreateSchemaObjects));
    }

    /// <summary>والطَرَفُ المُقابِل: Development وَحدَه <c>All</c> (يَحذِفُ ويُعيدُ عِندَ الاختِلاف) — ولِذلك يُرَكِّبُ الفِعلُ
    /// Production صَراحَةً ويُطَبِّقُ بِـ<c>CreateOrUpdate</c> صَراحَةً.</summary>
    [Fact]
    public async Task DevelopmentComposition_SetsAll()
        => Assert.Equal(AutoCreate.All,
            await WithComposedStore(Environments.Development, s => s.Options.AutoCreateSchemaObjects));

    /// <summary><b>الكائِناتُ المُهَيَّأَةُ تَحمِلُ وَثائِقَ القالِبِ ولا تَحمِلُ غَيرَ المُسَجَّل</b>: <c>Deal</c> و<c>TenantProviderBinding</c>
    /// يُسَجِّلُهُما <c>ConfigureMarten</c> القالِب (فَالفِعلُ يَبلُغُهُما بِالنِداءِ الثاني)، و<c>IncubatorSession</c> و<c>AuditEntry</c>
    /// لا يُسَجَّلانِ فَيُنشِئُهُما التَطبيقُ عِندَ أَوَّلِ استِعمال — وهُما المُسَمَّيانِ في رَأسِ الفِعلِ و§٤‑د. فَإن سُجِّلا يَوماً
    /// حَمِرَ هذا الصَفُّ لِتُصَحَّحَ الجُملَتان.</summary>
    [Fact]
    public async Task ConfiguredObjects_CarryTheTemplatesDocuments_AndNotTheUnregisteredOnes()
    {
        var ids = await WithComposedStore(Environments.Production,
            s => s.Storage.AllObjects().Select(o => o.Identifier.QualifiedName).ToList());

        Assert.True(ids.Count > 0, "أَداةٌ عَمياء: صِفرُ كائِنٍ مُهَيَّأ.");
        Assert.Contains("platform.mt_doc_deal", ids);
        Assert.Contains("platform.mt_doc_tenantproviderbinding", ids);
        Assert.Contains("platform.mt_doc_tenant", ids);
        Assert.DoesNotContain("platform.mt_doc_incubatorsession", ids);
        Assert.DoesNotContain("platform.mt_doc_auditentry", ids);
        Assert.DoesNotContain(ids, i => i.Contains("wolverine_", StringComparison.Ordinal));
    }

    /// <summary><b>الفِعلُ يُنادي نِداءَي التَطبيقِ بِعَينِهِما، ولا يَكتُبُ مُخَطَّطاً مِن عِندِه</b>.</summary>
    [Fact]
    public void TheVerb_ComposesTheAppsTwoCalls_AndDeclaresNoSchemaOfItsOwn()
    {
        var verb = Verb();
        var code = VerbCode();
        var program = Read("apps", "V1.App", "Program.cs");

        foreach (var call in new[] { "builder.AddPlatformHost(", "builder.Services.AddCustomerMarketplaceTemplate()" })
        {
            Assert.Contains(call, program, StringComparison.Ordinal);
            Assert.Contains(call, code, StringComparison.Ordinal);
        }

        foreach (var own in new[] { "Schema.For<", "RegisterDocumentType", "Projections.", "DocumentStore.For(", "ConfigureMarten(" })
            Assert.DoesNotContain(own, code, StringComparison.Ordinal);

        var projects = Regex.Matches(verb, @"^#:project (?<p>\S+)$", RegexOptions.Multiline)
            .Select(m => m.Groups["p"].Value).ToList();
        Assert.Equal(2, projects.Count);
        Assert.All(projects, p => Assert.True(
            File.Exists(Path.GetFullPath(Path.Combine(RepoRoot, "scripts", p))), $"#:project يُحيلُ إلى مَفقود: {p}"));
    }

    /// <summary><b>كُلُّ مَوضِعٍ يُهَيِّئُ Marten في <c>libs/</c> و<c>apps/</c> داخِلَ النِداءَينِ اللَّذَينِ يُناديهِما الفِعل</b> —
    /// <c>AddMarten</c> في النَواة، و<c>ConfigureMarten</c> في القالِب، و<c>DealsService.RegisterDocument</c> الَّتي يُناديها
    /// القالِبُ مِن داخِلِه. مَوضِعٌ رابِعٌ يَحمَرُّ هُنا بِاسمِه: إمّا أَن يُنادى مِن أَحَدِ النِداءَينِ أَو يُضافَ إلى الفِعل.
    /// والعَدّادانِ يُطبَعانِ في الرِسالَةِ ويَفشَلانِ عَلى الصِفر.</summary>
    [Fact]
    public void EveryMartenConfigurationSite_IsInsideTheTwoCallsTheVerbMakes()
    {
        var files = new[] { "libs", "apps" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(RepoRoot, d), "*.cs", SearchOption.AllDirectories))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.True(files.Count > 100, $"أَداةٌ عَمياء: {files.Count} مِلَفّاً مَقروءاً.");

        var site = new Regex(@"\bAddMarten\s*\(|\bConfigureMarten\s*\(|\bIConfigureMarten\b|\bDocumentStore\.For\s*\(|\(\s*StoreOptions\s+\w+\s*\)");
        // ‏الأَداةُ تُقاسُ قَبلَ أَن يُوثَقَ بِها: الأَشكالُ الخَمسَةُ تُمسَك، والإشارَةُ في تَعليقِ XML لا.
        foreach (var form in new[] { "services.AddMarten(opts =>", "services.ConfigureMarten(o =>",
                                     "class X : IConfigureMarten", "DocumentStore.For(o =>", "static void R(StoreOptions opts)" })
            Assert.Matches(site, form);
        Assert.DoesNotMatch(site, "/// (<c>ConfigureMarten</c> في <c>MarketplaceTemplateExtensions</c>)");

        var sites = files.Where(p => site.IsMatch(File.ReadAllText(p)))
            .Select(p => Path.GetRelativePath(RepoRoot, p).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var expected = new[]
        {
            "libs/core/ACommerce.Platform.Hosting/HostingExtensions.cs",
            "libs/templates/ACommerce.Templates.Customer.Marketplace/MarketplaceTemplateExtensions.cs",
            "libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Deals/DealsService.cs"
        };
        Assert.True(expected.SequenceEqual(sites),
            $"مَواضِعُ إعدادِ Marten ({sites.Count} مِن {files.Count} مِلَفّاً): {string.Join(", ", sites)} — "
          + "مَوضِعٌ خارِجَ AddPlatformHost/AddCustomerMarketplaceTemplate لا يَراه scripts/marten-schema.cs.");

        Assert.Contains("Services.Deals.DealsService.RegisterDocument(opts);",
            Read("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs"),
            StringComparison.Ordinal);
    }

    /// <summary><b>يُطَبِّقُ بِـ<c>CreateOrUpdate</c> ولا يَحذِف، ويُوَكِّدُ بِـ<c>AssertDatabaseMatchesConfiguration</c></b>،
    /// ويُرَكِّبُ Production صَراحَةً.</summary>
    [Fact]
    public void TheVerb_AppliesWithCreateOrUpdate_AssertsWithMarten_AndNeverAll()
    {
        var code = VerbCode();
        Assert.Contains("ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate.CreateOrUpdate)", code, StringComparison.Ordinal);
        Assert.Contains("AssertDatabaseMatchesConfigurationAsync()", code, StringComparison.Ordinal);
        Assert.Contains("EnvironmentName = Environments.Production", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoCreate.All", code, StringComparison.Ordinal);
        Assert.Contains("SchemaPatchDifference.Invalid", code, StringComparison.Ordinal);
    }

    /// <summary><b>لا يُطبَعُ حَرفٌ مِن السِلسِلَة</b>، والبَصمَةُ بِعَينِ بَوّابَةِ الإقلاع (‏sha256 لِلمُضيفِ، ثَمانِيَةُ أَحرُف)،
    /// وكَلِمَةُ المُرورِ لِـ<c>pg_dump</c> في البيئَةِ لا في سَطرِ الأَمر.</summary>
    [Fact]
    public void TheVerb_NeverPrintsTheConnectionString_AndPrintsTheGatesFingerprint()
    {
        var code = VerbCode();
        // ‏المُتَغَيِّرُ `cs` لا اسمُ المِلَفّ (`marten-schema.cs`): ما يَسبِقُه نُقطَةٌ أَو حَرفٌ لَيسَ إيّاه.
        var printsCs = new Regex(@"Console\.(Error\.)?Write(Line)?\([^;]*(?<![\w.-])cs\b");
        Assert.Matches(printsCs, "Console.WriteLine($\"x {cs}\");");
        Assert.DoesNotMatch(printsCs, "Console.WriteLine($\"dotnet run scripts/marten-schema.cs -- --apply\");");
        Assert.DoesNotMatch(printsCs, code);

        Assert.Contains("SHA256.HashData(Encoding.UTF8.GetBytes(host))", code, StringComparison.Ordinal);
        Assert.Contains("[..8]", code, StringComparison.Ordinal);
        Assert.Contains("sha256sum | cut -c1-8", Read("scripts", "verify-production-boot.sh"), StringComparison.Ordinal);

        Assert.Contains("[\"PGPASSWORD\"]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("--password", code, StringComparison.Ordinal);
    }

    /// <summary><b>رُموزُ الخُروجِ كَما يَقولُها الرَأسُ و§٤‑د</b>: ‏3 لِلتَهيئَةِ النّاقِصَةِ في مَواضِعِها الثَلاثَة (سِلسِلَةٌ
    /// غائِبَة، أَو لا تُقرَأ، أَو `pg_dump` غائِبَة)، و2 لِلفَرقِ القائِمِ عَرضاً وتَوكيداً.</summary>
    [Fact]
    public void TheVerb_ExitCodes_MatchItsHeader()
    {
        var verb = Verb();
        var code = VerbCode();
        Assert.Equal(3, Regex.Matches(code, @"return 3;").Count);
        Assert.Equal(2, Regex.Matches(code, @"return 2;").Count);
        foreach (var header in new[] { "//   0 — ", "//   1 — ", "//   2 — ", "//   3 — " })
            Assert.Contains(header, verb, StringComparison.Ordinal);
    }

    /// <summary><b>§٤‑د يَحمِلُ الإجراءَ بِتَرتيبِه</b>: لَقطَةٌ، ثُمَّ عَلى الفَرعِ عَرضٌ فَتَطبيقٌ فَتَوكيدٌ فَبَوّابَةُ الإقلاع، ثُمَّ
    /// الإنتاجُ بِالأَوامِرِ نَفسِها. و§٥ لا يَقولُ «والإنتاجُ لا يُنشِئ مُخَطَّطاً» إلّا مَشطوباً.</summary>
    [Fact]
    public void DeployDoc_CarriesTheProcedureInOrder_SnapshotBranchAssertThenProduction()
    {
        var doc = Read("docs", "DEPLOY.md");
        var start = doc.IndexOf("#### المُخَطَّطُ قَبلَ النَشر", StringComparison.Ordinal);
        Assert.True(start > 0, "§٤‑د بِلا فِقرَةِ المُخَطَّط.");
        var end = doc.IndexOf("## ٥.", start, StringComparison.Ordinal);
        Assert.True(end > start, "§٤‑د لا يَنتَهي قَبلَ §٥.");
        var section = doc[start..end];

        var steps = new[]
        {
            "-- --snapshot",
            "/path/to/branch-connection.txt",
            "dotnet run scripts/marten-schema.cs   ",
            "-- --apply --confirm N",
            "-- --assert",
            "./scripts/verify-production-boot.sh",
            "/path/to/production-connection.txt)\"\ndotnet run scripts/marten-schema.cs\n",
            "-- --apply --confirm N\ndotnet run scripts/marten-schema.cs -- --assert\n# ثُمَّ الدَمجُ"
        };
        var at = 0;
        foreach (var step in steps)
        {
            var i = section.IndexOf(step, at, StringComparison.Ordinal);
            Assert.True(i >= 0, $"خُطوَةٌ غائِبَةٌ أَو في غَيرِ مَوضِعِها بَعدَ المِحرَف {at}: «{step}»");
            at = i + step.Length;
        }

        foreach (var line in doc.Split('\n').Where(l => l.Contains("والإنتاجُ لا يُنشِئ مُخَطَّطاً", StringComparison.Ordinal)))
            Assert.True(line.Contains("~~والإنتاجُ لا يُنشِئ مُخَطَّطاً~~", StringComparison.Ordinal)
                     || line.Contains("(«والإنتاجُ لا يُنشِئ مُخَطَّطاً»)", StringComparison.Ordinal),
                $"الدَعوى المَنقوضَةُ باقِيَةٌ غَيرَ مَشطوبَة: {line.Trim()}");
    }
}
