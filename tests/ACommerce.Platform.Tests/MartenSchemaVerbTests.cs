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

public class MartenSchemaVerbTests(Xunit.Abstractions.ITestOutputHelper output)
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

    // ─── اللَقطَةُ خارِجَ شَجَرَةِ العَمَل (‏2026-10-12، مُراجَعَةُ تَيّارِ S10 — الجَولَة ١) ─────────────────────────────
    //
    // **المَقيسُ قَبل**: §٤‑د كَتَبَ اللَقطَةَ بِمَسارٍ نِسبِيٍّ (`"pre-deploy-$(git rev-parse --short HEAD).dump"`) — جَذرَ
    // المُستَودَعِ في كُلِّ أَمرٍ هُناك — و`SnapshotAsync` لا تَرفُضُ إلّا مِلَفّاً قائِماً، و`git check-ignore -v
    // pre-deploy-abc1234.dump` ⇒ ‏1 (غَيرُ مُتَجاهَل). واللَقطَةُ نُسخَةُ الإنتاجِ بِهَواتِفِ مُستَخدِميه وبَريدِهِم وصَفَقاتِهِم:
    // `git add .` واحِدٌ يَرفَعُها إلى تاريخِ GitHub. فَثَلاثَةُ حَواجِزَ لِكُلٍّ صَفُّه: الفِعلُ يَرفُضُ قَبلَ أَيِّ أَداة، و`.gitignore`
    // بِقِياسِ git نَفسِه، و§٤‑د يَكتُبُ في `$HOME/wasayel-snapshots/`.

    private static string SnapshotFunction()
    {
        var code = VerbCode();
        var start = code.IndexOf("static async Task<int> SnapshotAsync(", StringComparison.Ordinal);
        Assert.True(start > 0, "SnapshotAsync غائِبَةٌ عَن الفِعل.");
        var end = code.IndexOf("\nstatic ", start + 10, StringComparison.Ordinal);
        Assert.True(end > start, "نِهايَةُ SnapshotAsync غائِبَة.");
        return code[start..end];
    }

    /// <summary><b>الفِعلُ يَرفُضُ مَساراً داخِلَ شَجَرَةِ عَمَلِ git قَبلَ أَيِّ أَداةٍ وأَيِّ كِتابَة</b> — الرَفضُ أَوَّلُ ما في
    /// <c>SnapshotAsync</c> بَعدَ سَطرَيِ الهُوِيَّة، قَبلَ «قائِم؟» وقَبلَ <c>pg_dump --version</c>، ويَخرُجُ 1؛ والصُعودُ يَعرِفُ
    /// <c>.git</c> مُجَلَّداً ومِلَفّاً (شَجَرَةٌ مُلحَقَة).</summary>
    [Fact]
    public void TheSnapshot_RefusesAPathInsideAGitWorkTree_BeforeAnyToolOrWrite()
    {
        var snap = SnapshotFunction();
        var full = snap.IndexOf("var full = Path.GetFullPath(file);", StringComparison.Ordinal);
        var refuse = snap.IndexOf("if (EnclosingWorkTree(full) is { } root)", StringComparison.Ordinal);
        var exists = snap.IndexOf("if (File.Exists(file))", StringComparison.Ordinal);
        var tool = snap.IndexOf("RunAsync(\"pg_dump\"", StringComparison.Ordinal);
        Assert.True(full > 0 && refuse > full && exists > refuse && tool > exists,
            $"تَرتيبُ اللَقطَة: المَسار {full} · الرَفض {refuse} · «قائِم؟» {exists} · الأَداة {tool} — الرَفضُ قَبلَ أَيِّ أَداة.");
        Assert.Contains("return 1;", snap[refuse..exists], StringComparison.Ordinal);
        Assert.Contains("$HOME/wasayel-snapshots", snap[refuse..exists], StringComparison.Ordinal);

        var code = VerbCode();
        var walk = code[code.IndexOf("static string? EnclosingWorkTree(string fullPath)", StringComparison.Ordinal)..];
        walk = walk[..walk.IndexOf("\n}", StringComparison.Ordinal)];
        Assert.Contains("dir = Path.GetDirectoryName(dir)", walk, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(dir, \".git\")", walk, StringComparison.Ordinal);
        Assert.Contains("Directory.Exists(dotGit) || File.Exists(dotGit)", walk, StringComparison.Ordinal);
    }

    // ─── وgit نَفسُه يُسأَلُ حينَ يوجَد (‏2026-10-24 — مُراجَعَةُ الموجَة ٤، بَندُ M) ───────────────────────────────────────────
    //
    // **المَقيسُ قَبل** (`077e802`، Postgres مَحَلِّيّ): مُستَودَعٌ عارٍ يَتَتَبَّعُ «البَيت» بِـ`GIT_DIR`/`GIT_WORK_TREE` — إعدادُ dotfiles الشائِع —
    // و`--snapshot <البَيت>/wasayel-snapshots/x.dump` ⇒ ‏0 «✓ اللَقطَة …» والمِلَفُّ في الشَجَرَة: الصُعودُ لا يَجِدُ `.git` فَوقَ المَسار.

    /// <summary><b>بَعدَ الصُعودِ يُسأَلُ git، ويُرفَضُ عَلى أَيِّ شَكّ</b> — قَبلَ «قائِم؟» وقَبلَ أَيِّ أَداةِ قاعِدَة: <c>git -C &lt;مُجَلَّد&gt; rev-parse
    /// --show-toplevel</c> بِلُغَةِ C والبيئَةِ الموروثَة؛ لا يُقبَلُ إلّا ‏128 «not a git repository» أَو غِيابُ git، وما سِواهُما ‏1.</summary>
    [Fact]
    public void TheSnapshot_AsksGitItselfAfterTheWalk_AndRefusesOnAnyDoubt()
    {
        var snap = SnapshotFunction();
        var walk = snap.IndexOf("if (EnclosingWorkTree(full) is { } root)", StringComparison.Ordinal);
        var asked = snap.IndexOf("if (await GitWorkTreeDoubtAsync(full) is { } doubt)", StringComparison.Ordinal);
        var exists = snap.IndexOf("if (File.Exists(file))", StringComparison.Ordinal);
        var tool = snap.IndexOf("RunAsync(\"pg_dump\"", StringComparison.Ordinal);
        Assert.True(walk > 0 && asked > walk && exists > asked && tool > exists,
            $"تَرتيبُ اللَقطَة: الصُعود {walk} · git {asked} · «قائِم؟» {exists} · الأَداة {tool}.");
        Assert.Contains("return 1;", snap[asked..exists], StringComparison.Ordinal);

        var code = VerbCode();
        var fn = code[code.IndexOf("static async Task<string?> GitWorkTreeDoubtAsync(string fullPath)", StringComparison.Ordinal)..];
        fn = fn[..fn.IndexOf("\n}", StringComparison.Ordinal)];
        foreach (var piece in new[]
        {
            "while (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) dir = Path.GetDirectoryName(dir);",
            "RunAsync(\"git\", [\"-C\", dir, \"rev-parse\", \"--show-toplevel\"],",
            "[\"LC_ALL\"] = \"C\"",
            "if (git is null) return null;",
            "if (code == 128 && stderr.Contains(\"not a git repository\", StringComparison.OrdinalIgnoreCase)) return null;",
        })
            Assert.Contains(piece, fn, StringComparison.Ordinal);
        // ‏ولا «return null» ثالِث: كُلُّ جَوابٍ آخَرَ شَكٌّ يُرفَضُ بِه.
        Assert.Equal(2, Regex.Matches(fn, @"return null;").Count);
    }

    /// <summary><b>والإعدادُ الَّذي فاتَ الصُعودَ يَراه git نَفسُه</b> — بِقِياسِه لا بِوَصفِه: مُستَودَعٌ عارٍ في مُجَلَّدٍ مُؤَقَّت، و«بَيتٌ» بِلا
    /// <c>.git</c> فَوقَه، و<c>git -C &lt;البَيت&gt;/wasayel-snapshots rev-parse --show-toplevel</c>: بِـ<c>GIT_DIR</c>/<c>GIT_WORK_TREE</c> ⇒ ‏0 وجَذرُه
    /// البَيت؛ وبِـ<c>GIT_DIR</c> وَحدَه ⇒ ‏128 «must be run in a work tree» (شَكٌّ)؛ وبِدونِهِما ⇒ ‏128 «not a git repository» — فَالأَداةُ تُمَيِّز.</summary>
    [Fact]
    public void A_bare_dotfiles_repository_over_home_is_seen_by_git_and_not_by_the_walk()
    {
        var root = Directory.CreateTempSubdirectory("wasayel-dotfiles-");
        try
        {
            var home = Directory.CreateDirectory(Path.Combine(root.FullName, "home", "wasayel-snapshots")).Parent!.FullName;
            var bare = Path.Combine(root.FullName, "dotfiles.git");
            Assert.Equal(0, Git(root.FullName, null, "init", "-q", "--bare", bare).Code);

            for (var dir = Path.Combine(home, "wasayel-snapshots"); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
                Assert.False(Directory.Exists(Path.Combine(dir, ".git")) || File.Exists(Path.Combine(dir, ".git")),
                    $"{dir} يَحمِلُ .git — المُجَلَّدُ المُؤَقَّتُ داخِلَ شَجَرَة، فَالصَفُّ لا يَقيسُ ما يَدَّعي.");

            var snaps = Path.Combine(home, "wasayel-snapshots");
            var both = Git(snaps, new() { ["GIT_DIR"] = bare, ["GIT_WORK_TREE"] = home }, "rev-parse", "--show-toplevel");
            var bareOnly = Git(snaps, new() { ["GIT_DIR"] = bare }, "rev-parse", "--show-toplevel");
            var none = Git(snaps, new(), "rev-parse", "--show-toplevel");
            output.WriteLine($"GIT_DIR+GIT_WORK_TREE → {both.Code} · GIT_DIR → {bareOnly.Code} {bareOnly.Err.Trim()} · لا شَيء → {none.Code} {none.Err.Trim()}");

            Assert.Equal(0, both.Code);
            Assert.Equal(Path.GetFullPath(home).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(both.Out.Trim()).TrimEnd(Path.DirectorySeparatorChar));
            Assert.Equal(128, bareOnly.Code);
            Assert.DoesNotContain("not a git repository", bareOnly.Err, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(128, none.Code);
            Assert.Contains("not a git repository", none.Err, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static (int Code, string Out, string Err) Git(string dir, Dictionary<string, string>? env, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in new[] { "-C", dir }.Concat(args)) psi.ArgumentList.Add(a);
        psi.Environment.Remove("GIT_DIR");
        psi.Environment.Remove("GIT_WORK_TREE");
        psi.Environment["LC_ALL"] = "C";
        psi.Environment["LANGUAGE"] = "C";
        foreach (var (k, v) in env ?? new()) psi.Environment[k] = v;

        System.Diagnostics.Process p;
        try { p = System.Diagnostics.Process.Start(psi)!; }
        catch (System.ComponentModel.Win32Exception) { Assert.Fail("أَداةٌ غائِبَة: git — لا يُقاسُ اكتِشافُه بِدونِه."); return default; }
        using (p)
        {
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            Assert.True(p.WaitForExit(15_000), "git لَم يَنتَهِ في خَمسَ عَشرَةَ ثانِيَة.");
            return (p.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }
    }

    /// <summary><b>و<c>*.dump</c> مُتَجاهَلٌ بِقِياسِ git نَفسِه</b> — <c>git check-ignore</c> مُشَغَّلاً في جَذرِ المُستَودَع: اسمُ
    /// §٤‑د القَديمُ وأَيُّ لَقطَةٍ في أَيِّ مُجَلَّدٍ ⇒ مُتَجاهَل (0)، واسمٌ آخَرُ ⇒ غَيرُ مُتَجاهَل (1) — فَالأَداةُ تُمَيِّز.</summary>
    [Theory]
    [InlineData("pre-deploy-abc1234.dump", 0)]
    [InlineData("scripts/pre-deploy-abc1234.dump", 0)]
    [InlineData("docs/any/depth/production.dump", 0)]
    [InlineData("pre-deploy-abc1234.txt", 1)]
    public void GitIgnore_KeepsSnapshotsOut_MeasuredByGitItself(string path, int expected)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in new[] { "check-ignore", "-q", "--no-index", "--", path }) psi.ArgumentList.Add(a);

        System.Diagnostics.Process p;
        try { p = System.Diagnostics.Process.Start(psi)!; }
        catch (System.ComponentModel.Win32Exception) { Assert.Fail("أَداةٌ غائِبَة: git — لا يُقاسُ .gitignore بِدونِها."); return; }
        using (p)
        {
            var err = p.StandardError.ReadToEnd();
            Assert.True(p.WaitForExit(15_000), "git check-ignore لَم يَنتَهِ في خَمسَ عَشرَةَ ثانِيَة.");
            Assert.True(p.ExitCode is 0 or 1, $"git check-ignore خَرَجَ {p.ExitCode}: {err.Trim()} — لَيسَ شَجَرَةَ عَمَلٍ أَو الأَداةُ مُعَطَّلَة.");
            Assert.Equal(expected, p.ExitCode);
        }
    }

    /// <summary><b>و§٤‑د يَكتُبُ اللَقطَةَ خارِجَ المُستَودَع</b>: كُلُّ <c>--snapshot</c> في كُتَلِ الأَوامِرِ بِمَسارٍ تَحتَ
    /// <c>$HOME/wasayel-snapshots/</c>، ويَسبِقُه <c>mkdir -p</c> لَه — والمَسارُ النِسبِيُّ القَديمُ لا يَرِدُ إلّا مُقتَبَساً في التَصحيح.</summary>
    [Fact]
    public void DeployDoc_SnapshotStep_WritesOutsideTheWorkTree()
    {
        var doc = Read("docs", "DEPLOY.md");
        var start = doc.IndexOf("#### المُخَطَّطُ قَبلَ النَشر", StringComparison.Ordinal);
        var end = doc.IndexOf("## ٥.", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "§٤‑د بِلا فِقرَةِ المُخَطَّط.");
        var blocks = Regex.Matches(doc[start..end], "```bash\n(?<body>.*?)```", RegexOptions.Singleline)
            .Select(m => m.Groups["body"].Value).ToArray();
        Assert.NotEmpty(blocks);
        var code = string.Join('\n', blocks);

        var snaps = SnapshotPaths(code);
        Assert.NotEmpty(snaps);
        Assert.Equal(Regex.Matches(code, "--snapshot").Count, snaps.Count);   // ‏(‏2026-10-24) كُلُّ --snapshot يُقرَأُ مَسارُه، بِعَلامَتَي تَنصيصٍ أَو بِدونِهِما
        Assert.All(snaps, s => Assert.StartsWith("$HOME/wasayel-snapshots/", s, StringComparison.Ordinal));
        // ‏(‏2026-10-24، بَندُ L) والمُجَلَّدُ لِصاحِبِه وَحدَه: `-m 700` وَ`chmod 700` لِمُجَلَّدٍ قائِم.
        var mkdir = code.IndexOf("mkdir -m 700 -p \"$HOME/wasayel-snapshots\" && chmod 700 \"$HOME/wasayel-snapshots\"", StringComparison.Ordinal);
        Assert.True(mkdir >= 0 && mkdir < code.IndexOf("--snapshot", StringComparison.Ordinal), "mkdir -m 700 -p لا يَسبِقُ أَوَّلَ --snapshot.");
    }

    /// <summary><b>واللَقطَةُ لِصاحِبِها وَحدَه</b> (‏2026-10-24 — مُراجَعَةُ الموجَة ٤، بَندُ L؛ مَقيساً قَبلُ بِـumask ‏0022: المُجَلَّدُ ‏755
    /// والمِلَفُّ ‏644): الفِعلُ يُضَيِّقُ المِلَفَّ إلى <c>UserRead | UserWrite</c> عَلى غَيرِ وِندوز بَعدَ نَجاحِ <c>pg_dump</c> وقَبلَ أَوَّلِ
    /// قارِئٍ لَه (<c>pg_restore --list</c>)، ويَطبَعُ صَلاحِيَّتَه؛ و§٤‑د يَقولُ مَتى تُحذَفُ بِأَمرِها، وتَلميحُ الرَفضِ يَقولُ <c>-m 700</c>.</summary>
    [Fact]
    public void TheSnapshot_IsOwnerOnly_OnTheFileAndInTheStep_AndIsDeletedOnceTheDeployIsVerified()
    {
        var snap = SnapshotFunction();
        var dumpFailed = snap.IndexOf("if (dump is null || dump.Value.Code != 0)", StringComparison.Ordinal);
        const string Narrow = "if (!OperatingSystem.IsWindows())\n        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);";
        var narrow = snap.IndexOf(Narrow, StringComparison.Ordinal);
        var firstReader = snap.IndexOf("RunAsync(\"pg_restore\"", StringComparison.Ordinal);
        Assert.True(dumpFailed > 0 && narrow > dumpFailed && firstReader > narrow,
            $"تَرتيبُ اللَقطَة: فَشَلُ pg_dump {dumpFailed} · التَضييق {narrow} · أَوَّلُ قارِئ {firstReader} — يُضَيَّقُ بَعدَ الكِتابَةِ وقَبلَ أَيِّ قِراءَة.");
        Assert.Single(Regex.Matches(snap, Regex.Escape("File.SetUnixFileMode(")));
        Assert.Contains("\" · الصَلاحِيَّة \" + Convert.ToString((int)File.GetUnixFileMode(file), 8)", snap, StringComparison.Ordinal);
        Assert.Contains("mkdir -m 700 -p \\\"$HOME/wasayel-snapshots\\\"", snap, StringComparison.Ordinal);
        Assert.DoesNotContain("mkdir -p \\\"$HOME", snap, StringComparison.Ordinal);

        var doc = Read("docs", "DEPLOY.md");
        var start = doc.IndexOf("#### المُخَطَّطُ قَبلَ النَشر", StringComparison.Ordinal);
        var end = doc.IndexOf("## ٥.", start, StringComparison.Ordinal);
        var section = doc[start..end];
        Assert.Contains("`rm \"$HOME/wasayel-snapshots/pre-deploy-<sha>.dump\"`", section, StringComparison.Ordinal);
        Assert.Contains("**وتُحذَفُ حينَ يُتَحَقَّقُ النَشر**", section, StringComparison.Ordinal);
    }

    // ─── ‏2026-10-24 (تَيّارُ S16، مُراجَعَةُ S13 الجَولَة ١): 600 مُنذُ أَوَّلِ بايتٍ لا بَعدَ آخِرِه ─────────────────────────────
    //
    // **المَقيسُ قَبل** (`1c72873`، Postgres مَحَلِّيّ، بِـumask ‏0022، وpg_dump مُغَلَّفٌ يَنامُ ‏6ث بَعدَ الكِتابَة): «mid-dump mode: 644
    // size=151979» ثُمَّ «الصَلاحِيَّة 600» — التَضييقُ بَعدَ خُروجِ الأَداة، وطَوالَ الكِتابَةِ يَقرَؤُها كُلُّ حِساب. **وبَعدُ**: «mid-dump mode: 600».

    /// <summary>ما يُخالِفُ «يُنشَأُ المِلَفُّ ‏600 قَبلَ pg_dump» في جِسمِ <c>SnapshotAsync</c> — فارِغَةٌ إن كانَ كَما اليَوم: <c>new FileStream</c>
    /// واحِدٌ بِـ<c>FileMode.CreateNew</c> و<c>UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite</c> بِحَرفِه، داخِلَ
    /// <c>if (!OperatingSystem.IsWindows())</c>، بَعدَ «قائِم؟» و<c>pg_dump --version</c> وقَبلَ نِداءِ الكِتابَة؛ و«قائِم» يُرَدُّ بِـ1 لا يُبتَلَع.</summary>
    internal static IReadOnlyList<string> CreatedOwnerOnlyViolations(string snap)
    {
        var v = new List<string>();
        var streams = Regex.Matches(snap, @"new FileStream\(file, new FileStreamOptions\s*\{(?<o>[^}]*)\}\)");
        if (streams.Count != 1) { v.Add($"create_count: {streams.Count}"); return v; }
        var opts = Regex.Replace(streams[0].Groups["o"].Value, @"\s+", " ");
        if (!opts.Contains("Mode = FileMode.CreateNew,", StringComparison.Ordinal)) v.Add("not_create_new");
        if (!opts.Contains("UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,", StringComparison.Ordinal)
            || Regex.IsMatch(opts, @"Group|Other")) v.Add("mode_not_owner_only");
        var at = streams[0].Index;
        var exists = snap.IndexOf("if (File.Exists(file))", StringComparison.Ordinal);
        var version = snap.IndexOf("RunAsync(\"pg_dump\", [\"--version\"]", StringComparison.Ordinal);
        var write = snap.IndexOf("RunAsync(\"pg_dump\", [\"--format=custom\"", StringComparison.Ordinal);
        if (!(exists > 0 && version > exists && at > version && write > at)) v.Add($"order: قائِم {exists} · الأَداة {version} · الإنشاء {at} · الكِتابَة {write}");
        var guard = snap.LastIndexOf("if (!OperatingSystem.IsWindows())", at, StringComparison.Ordinal);
        if (guard < 0 || snap[guard..at].Contains('}')) v.Add("not_in_windows_guard");
        var tail = snap[at..(write > at ? write : snap.Length)];
        if (!tail.Contains("catch (IOException) when (File.Exists(file))", StringComparison.Ordinal) || !tail.Contains("return 1;", StringComparison.Ordinal))
            v.Add("exists_race_swallowed");
        return v;
    }

    /// <summary><b>المِلَفُّ يُنشَأُ ‏600 قَبلَ أَن يَكتُبَ pg_dump حَرفاً</b> — بِالشَكلِ أَعلاه، ونَمَطُ الإنشاءِ يُقاسُ هُنا بِالخِيارِ نَفسِه عَلى
    /// مِلَفٍّ مُؤَقَّتٍ تَحتَ قِناعِ العَمَلِيَّة (عَلى غَيرِ وِندوز): ‏600، و<c>CreateNew</c> عَلى مِلَفٍّ قائِمٍ يَرمي ولا يَقطَعُه.</summary>
    [Fact]
    public void TheSnapshot_IsCreatedOwnerOnly_BeforePgDumpWritesAByte()
    {
        var snap = SnapshotFunction();
        var violations = CreatedOwnerOnlyViolations(snap);
        output.WriteLine($"مُخالَفاتُ «يُنشَأُ 600 قَبلَ pg_dump»: {violations.Count}{(violations.Count > 0 ? " — " + string.Join("؛ ", violations) : "")}");
        Assert.True(violations.Count == 0, string.Join("\n", violations));
        if (OperatingSystem.IsWindows()) { output.WriteLine("Windows — لا نَمَطَ يونِكسِيّ؛ يُقاسُ النَمَطُ في CI (‏ubuntu)."); return; }

        var dir = Directory.CreateTempSubdirectory("wasayel-snapshot-mode-");
        try
        {
            var file = Path.Combine(dir.FullName, "x.dump");
            using (new FileStream(file, new FileStreamOptions
                   { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite }))
            { }
            var mode = File.GetUnixFileMode(file);
            output.WriteLine($"نَمَطُ المِلَفِّ عِندَ إنشائِه: {Convert.ToString((int)mode, 8)}");
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
            File.WriteAllText(file, "قائِم");
            Assert.Throws<IOException>(() => new FileStream(file, new FileStreamOptions
                { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite }));
            Assert.Equal("قائِم", File.ReadAllText(file));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary><b>حَقنُ عَيب — فاحِصُ «يُنشَأُ 600 قَبلَ pg_dump» يُحمِرُّ فِعلاً</b>: الإنشاءُ بَعدَ الكِتابَة، أَو بِقِراءَةِ المَجموعَة، أَو
    /// بِـ<c>Create</c> الَّذي يَقطَعُ القائِم، أَو بِلا حارِسِ وِندوز، أَو «قائِم» مُبتَلَعاً، أَو مَحذوفاً — كُلٌّ يُرى؛ والحَيُّ يُقبَل.</summary>
    [Fact]
    public void TheCreatedOwnerOnlyChecker_CanActuallyGoRed()
    {
        var snap = SnapshotFunction();
        const string Opts = "UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,";
        string With(string from, string to)
        {
            Assert.Single(Regex.Matches(snap, Regex.Escape(from)));
            return snap.Replace(from, to, StringComparison.Ordinal);
        }
        var block = Regex.Match(snap, @"    if \(!OperatingSystem\.IsWindows\(\)\)\n    \{\n        try\n        \{\n            using var created = new FileStream\(file,[\s\S]*?\n    \}\n");
        Assert.True(block.Success, "كُتلَةُ الإنشاءِ الحَيَّةُ لَم تُقرَأ — الأَداةُ عَمياء.");
        var dumpLine = "    var dump = await RunAsync(\"pg_dump\", [\"--format=custom\"";
        var rejected = new (string Name, string Code)[]
        {
            ("بَعدَ الكِتابَة", With(block.Value, "").Replace("    if (dump is null || dump.Value.Code != 0)", block.Value + "    if (dump is null || dump.Value.Code != 0)", StringComparison.Ordinal)),
            ("قِراءَةُ المَجموعَة", With(Opts, "UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead,")),
            ("Create يَقطَعُ القائِم", With("Mode = FileMode.CreateNew,", "Mode = FileMode.Create,")),
            ("بِلا حارِسِ وِندوز", With(block.Value, block.Value.Replace("if (!OperatingSystem.IsWindows())", "if (true)", StringComparison.Ordinal))),
            ("«قائِم» مُبتَلَع", With("catch (IOException) when (File.Exists(file))", "catch (IOException)")),
            ("بِلا إنشاء", With(block.Value, "")),
        };
        Assert.Contains(dumpLine, snap, StringComparison.Ordinal);
        var wrong = new List<string>();
        if (CreatedOwnerOnlyViolations(snap).Count != 0) wrong.Add("رُفِضَ الحَيّ: " + string.Join("؛ ", CreatedOwnerOnlyViolations(snap)));
        foreach (var (name, code) in rejected)
        {
            var v = CreatedOwnerOnlyViolations(code);
            output.WriteLine($"«{name}» → {(v.Count == 0 ? "قُبِل" : string.Join("؛ ", v))}");
            if (v.Count == 0) wrong.Add($"قُبِلَ «{name}»");
        }
        output.WriteLine($"حُقِنَ {rejected.Length} عَيباً؛ أَخطَأَ الفاحِصُ في {wrong.Count}");
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary>مَسارُ كُلِّ <c>--snapshot</c> في نَصِّ أَوامِر — بَينَ عَلامَتَي تَنصيصٍ أَو بِدونِهِما (‏2026-10-24 — مُراجَعَةُ الموجَة ٤،
    /// بَندُ H: كانَ الصَفُّ أَعلاه يَقرَأُ المُقتَبَسَ وَحدَه، فَـ<c>--snapshot pre-deploy.dump</c> — جَذرَ المُستَودَعِ مَرَّةً أُخرى — لا يُعَدُّ
    /// ويَمُرُّ ما دامَ في الكُتلَةِ مَسارٌ مُقتَبَسٌ واحِدٌ صَحيح).</summary>
    internal static IReadOnlyList<string> SnapshotPaths(string code)
        => Regex.Matches(code, @"--snapshot[ \t]+(?:""(?<q>[^""]+)""|(?<u>[^\s""]\S*))")
            .Select(m => m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["u"].Value)
            .ToList();

    /// <summary><b>حَقنُ عَيب — قارِئُ مَسارِ اللَقطَةِ يُحمِرُّ فِعلاً</b> (‏2026-10-24): خُطوَةُ §٤‑د الحَيَّةُ تُضافُ إلَيها سُطورٌ بِمَسارٍ
    /// غَيرِ مُقتَبَسٍ داخِلَ المُستَودَع، أَو مُقتَبَسٍ بِعَلامَةٍ مُفرَدَة، أَو بِلا مَسار — فَيُشتَرَطُ أَن يُرى كُلٌّ مِنها خَرقاً؛ والحَيُّ
    /// ومَسارٌ غَيرُ مُقتَبَسٍ تَحتَ <c>$HOME/wasayel-snapshots/</c> يُقبَلان.</summary>
    [Fact]
    public void TheSnapshotPathReader_CanActuallyGoRed()
    {
        const string Live = "mkdir -p \"$HOME/wasayel-snapshots\"\n"
            + "dotnet run scripts/marten-schema.cs -- --snapshot \"$HOME/wasayel-snapshots/pre-deploy-$(git rev-parse --short HEAD).dump\"\n";
        static bool Outside(string code)
        {
            var paths = SnapshotPaths(code);
            return paths.Count > 0 && paths.Count == Regex.Matches(code, "--snapshot").Count
                && paths.All(p => p.StartsWith("$HOME/wasayel-snapshots/", StringComparison.Ordinal));
        }
        var accepted = new[]
        {
            Live,
            Live + "dotnet run scripts/marten-schema.cs -- --snapshot $HOME/wasayel-snapshots/second.dump\n",
        };
        var rejected = new[]
        {
            Live + "dotnet run scripts/marten-schema.cs -- --snapshot pre-deploy-$(git rev-parse --short HEAD).dump\n",
            Live + "dotnet run scripts/marten-schema.cs -- --snapshot ./backups/x.dump   # تَعليق\n",
            Live + "dotnet run scripts/marten-schema.cs -- --snapshot '$HOME/wasayel-snapshots/x.dump'\n",
            Live + "dotnet run scripts/marten-schema.cs -- --snapshot\n",
            "dotnet run scripts/marten-schema.cs -- --snapshot pre-deploy.dump\n",
        };
        Assert.Equal(new[] { "$HOME/wasayel-snapshots/pre-deploy-$(git rev-parse --short HEAD).dump", "pre-deploy.dump" },
            SnapshotPaths(Live + "x --snapshot pre-deploy.dump").ToArray());
        var wrong = accepted.Where(c => !Outside(c)).Select(c => "رُفِضَ حَيّ: " + c)
            .Concat(rejected.Where(Outside).Select(c => "قُبِلَ: " + c)).ToList();
        Assert.True(wrong.Count == 0,
            $"حُقِنَ {rejected.Length} سَطراً و{accepted.Length} حَيّاً، وأَخطَأَ القارِئُ في {wrong.Count}:\n  " + string.Join("\n  ", wrong));
        // ‏والقارِئُ القَديمُ (المُقتَبَسُ وَحدَه) كانَ يَقبَلُ مِنها:
        var oldAccepts = rejected.Count(c =>
        {
            var q = Regex.Matches(c, @"--snapshot\s+""(?<path>[^""]+)""").Select(m => m.Groups["path"].Value).ToArray();
            return q.Length > 0 && q.All(p => p.StartsWith("$HOME/wasayel-snapshots/", StringComparison.Ordinal));
        });
        Assert.Equal(4, oldAccepts);
    }
}
