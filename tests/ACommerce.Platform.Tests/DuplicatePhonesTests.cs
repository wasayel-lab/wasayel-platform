using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Cart;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Favorites;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using JasperFx.Events.Projections;
using Marten;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ د57 (٣) «أ»: الحِساباتُ المُكَرَّرَةُ بِصيغَتَي الجَوّال — الخُطَّة، والقِياس، والدَمج، والسكربتان (‏2026-10-29؛ ‏ADR-044 §٤) ═══
//
// قَرارُ المالِك: «الخُطوَةُ الأولى عَدٌّ لِلقِراءَةِ فَقَط… ثُمَّ ترحيلٌ يَكتُبُ كُلَّ رَقَمٍ بِالصيغَةِ الدَولِيَّةِ المُوَحَّدَة، ويَجمَعُ إعلاناتِ الشَخصِ وصَفَقاتِه
// في حِسابٍ واحِد». فَهُنا: الخُطَّةُ النَقِيَّةُ مُوجَبَةً وسالِبَة، والسكربتانِ بِحَدَّيهِما (العَدُّ لا يَكتُب، والدَمجُ لا يَكتُبُ بِلا --apply والعَدَدِ المُطابِق)،
// والدَمجُ حَيّاً عَلى Postgres — بِما يُعادُ تَوجيهُه وبِمانِعٍ يَحمي المَجموعَةَ كُلَّها.
public class DuplicatePhonesTests(ITestOutputHelper output)
{
    private static readonly DateTime T0 = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static DuplicatePhones.Account A(string phone, int day, bool deleted = false, Guid? id = null)
        => new(id ?? Guid.NewGuid(), phone, T0.AddDays(day), deleted);

    /// <summary>المُفرَدُ: يُعادُ كَتبُه إن لَم يَكُن مُوَحَّداً (مُوجَب)، ولا يُمَسُّ مُوَحَّدٌ ولا خارِجَ السُعودِيَّةِ ولا <c>NID-…</c> ولا فارِغٌ ولا مَحذوف (سالِب).</summary>
    [Fact]
    public void A_single_account_is_renamed_only_when_its_phone_is_not_canonical()
    {
        var local = A("0500000081", 1);
        var plan = DuplicatePhones.Build(new[]
        {
            local, A("+966500000082", 2), A("+12025550123", 3), A("NID-1052001001", 4), A("", 5), A("0500000083", 6, deleted: true),
        });
        output.WriteLine($"مَنظور: {plan.Considered} · يُعادُ كَتبُه: {plan.Renames.Count} · مَجموعات: {plan.Groups.Count}");
        Assert.Equal(3, plan.Considered);
        var rename = Assert.Single(plan.Renames);
        Assert.Equal(new DuplicatePhones.Rename(local.Id, "+966500000081"), rename);
        Assert.Empty(plan.Groups);
    }

    /// <summary>المَجموعَة: كِتاباتُ الرَقَمِ الواحِد — يَبقى أَقدَمُها، ويَندَمِجُ الباقونَ بِتَرتيبِ قِدَمِهِم؛ والتَعادُلُ في الوَقتِ يَحسِمُه المُعَرِّف؛ ونَصّانِ مُتَطابِقانِ
    /// خارِجَ السُعودِيَّةِ مَجموعَةٌ أَيضاً؛ ورَقَمانِ مُختَلِفانِ لا يَجتَمِعان (سالِب).</summary>
    [Fact]
    public void One_number_under_many_spellings_is_one_group_whose_oldest_account_stays()
    {
        var oldest = A("+966 50 000 0084", 1);
        var mid = A("0500000084", 2);
        var newest = A("966500000084", 3);
        var tieA = A("0500000085", 1, id: Guid.Parse("00000000-0000-0000-0000-00000000000a"));
        var tieB = A("+966500000085", 1, id: Guid.Parse("00000000-0000-0000-0000-00000000000b"));
        var foreignA = A("+12025550123", 2);
        var foreignB = A("+12025550123", 1);
        var other = A("0500000086", 1);
        var plan = DuplicatePhones.Build(new[] { newest, other, mid, tieB, oldest, foreignA, tieA, foreignB });
        output.WriteLine($"مَجموعات: {plan.Groups.Count} · {string.Join(" · ", plan.Groups.Select(g => $"{g.Losers.Count + 1} حِسابات"))}");

        Assert.Equal(3, plan.Groups.Count);
        var g84 = plan.Groups.Single(g => g.Canonical == "+966500000084");
        Assert.Equal(oldest.Id, g84.Survivor);
        Assert.Equal(new[] { mid.Id, newest.Id }, g84.Losers);
        var g85 = plan.Groups.Single(g => g.Canonical == "+966500000085");
        Assert.Equal((tieA.Id, tieB.Id), (g85.Survivor, Assert.Single(g85.Losers)));
        var foreign = plan.Groups.Single(g => g.Canonical == "+12025550123");
        Assert.Equal((foreignB.Id, foreignA.Id), (foreign.Survivor, Assert.Single(foreign.Losers)));
        Assert.Equal(new DuplicatePhones.Rename(other.Id, "+966500000086"), Assert.Single(plan.Renames));
    }

    // ─── السكربتان ─────────────────────────────────────────────────────────────────────────

    private static string Code(string script) => string.Join('\n', StudioConsentCharacterizationTests.Source("scripts", script).Split('\n')
        .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && !l.StartsWith("#", StringComparison.Ordinal)));

    /// <summary><b>العَدُّ لا يَكتُبُ حَرفاً</b>: لا بابَ كِتابَةٍ يَعرِفُه Marten وNpgsql، والاتِّصالُ بِـ<c>default_transaction_read_only=on</c> يُقرَأُ مِن
    /// الخادِمِ ويَخرُجُ بِـ1 إن لَم يَكُن <c>on</c>، ويَخرُجُ بِـ1 عَلى صِفرِ مَتجَر، ويَقيسُ بِدالَّتَي الدَمجِ نَفسَيهِما.</summary>
    [Fact]
    public void The_count_script_writes_nothing_reads_through_a_read_only_connection_and_fails_on_zero()
    {
        var code = Code("count-duplicate-phones.cs");
        var writes = new[]
        {
            "LightweightSession", "IdentitySession", "DirtyTrackedSession", "OpenSession", "SaveChanges", ".Store(", ".Insert(", ".Update(", ".Delete(",
            "HardDelete", ".Patch<", "Events.Append", "StartStream", "ExecuteNonQuery", "ApplyAsync", "RenameAsync", "AutoCreate.All", "AuditWriter",
        };
        var found = writes.Where(w => code.Contains(w, StringComparison.Ordinal)).ToList();
        output.WriteLine($"أَبوابُ كِتابَةٍ مَفحوصَة: {writes.Length} · مَوجودَة: {found.Count}");
        Assert.Empty(found);
        Assert.Contains("const string ReadOnlyOption = \"-c default_transaction_read_only=on\";", code, StringComparison.Ordinal);
        Assert.Contains("if (readOnly != \"on\")", code, StringComparison.Ordinal);
        Assert.Contains("o.Connection(readOnlyCs);", code, StringComparison.Ordinal);
        Assert.Contains("o.AutoCreateSchemaObjects = AutoCreate.None;", code, StringComparison.Ordinal);
        Assert.Contains("DuplicatePhones.Build(members)", code, StringComparison.Ordinal);
        Assert.Contains("DuplicatePhones.ScanAsync(conn, slug, g, tables)", code, StringComparison.Ordinal);
        var blind = Regex.Match(code, @"if \(tenants\.Count == 0\)\s*\{(?<body>[^}]*)\}");
        Assert.True(blind.Success, "حارِسُ العَمى غائِب.");
        Assert.Contains("return 1;", blind.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("فُحِصَ: {tenants.Count} مَتجَراً والاستوديو", code, StringComparison.Ordinal);
    }

    /// <summary><b>الدَمجُ عَرضٌ افتِراضاً</b>: لا كِتابَةَ قَبلَ <c>--apply</c> وعَدَدٍ يُطابِقُ ما طَبَعَه العَرض؛ والعَرضُ يَخرُجُ بِـ2 حينَ يَجِدُ تَغييراً،
    /// والمُطابَقَةُ قَبلَ أَوَّلِ كِتابَة، وكُلُّ مَجموعَةٍ تُقاسُ مِن جَديدٍ قَبلَ دَمجِها، ولا نُسخَةَ بِبَياناتٍ شَخصِيَّةٍ تُكتَبُ عَلى القُرص.</summary>
    [Fact]
    public void The_merge_script_writes_only_after_apply_and_a_matching_confirm_and_rescans_each_group()
    {
        var code = Code("merge-duplicate-phones.cs");
        int At(string s) { var i = code.IndexOf(s, StringComparison.Ordinal); Assert.True(i >= 0, $"لا «{s}» — الأَداةُ عَمياء."); return i; }
        var dryRun = At("if (!apply)");
        var dryRunExit = At("return 2;");
        var confirmGate = At("if (confirm != changes)");
        var firstWrite = new[] { At("DuplicatePhones.RenameAsync<User>("), At("DuplicatePhones.ApplyAsync(store, slug, scan, now)"), At("audit.WriteAsync(") }.Min();
        var rescan = code.IndexOf("DuplicatePhones.ScanAsync(conn, slug, before.Group, tables)", StringComparison.Ordinal);
        output.WriteLine($"العَرض@{dryRun} خُروجُه@{dryRunExit} المُطابَقَة@{confirmGate} أَوَّلُ كِتابَة@{firstWrite} إعادَةُ القِياس@{rescan}");

        Assert.True(dryRun < dryRunExit && dryRunExit < confirmGate && confirmGate < firstWrite);
        Assert.True(rescan > confirmGate && rescan < At("DuplicatePhones.ApplyAsync(store, slug, scan, now)"));
        Assert.DoesNotContain("File.Write", code, StringComparison.Ordinal);
        // ‏مَخزَنُ الدَمجِ في مِلَفِّه (لا في libs/ — ‏MartenSchemaVerbTests)، ويُعيدُ مِن تَركيبِ المُضيفِ ما يَمَسُّه: أَحداثٌ مُقتَرِنَة، ونُسخَةُ الإعلانِ في المُعامَلَة.
        foreach (var line in StoreLines)
            Assert.Contains(line, code, StringComparison.Ordinal);
        Assert.Contains("o.AutoCreateSchemaObjects = AutoCreate.None;", code, StringComparison.Ordinal);
        var host = StudioConsentCharacterizationTests.Source("libs", "core", "ACommerce.Platform.Hosting", "HostingExtensions.cs");
        Assert.Contains("opts.Projections.Snapshot<ACommerce.Kit.Listings.Listing>(SnapshotLifecycle.Inline);", host, StringComparison.Ordinal);
        Assert.Contains("TenancyStyle.Conjoined", host, StringComparison.Ordinal);
        Assert.Matches(@"For<Services\.Api\.ApiKeyDocument>\(\)\s*\.SingleTenanted\(\)", StudioConsentCharacterizationTests.Source(
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs"));
        Assert.DoesNotContain("DocumentStore.For(", StudioConsentCharacterizationTests.Source(
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Identity", "DuplicatePhones.cs"), StringComparison.Ordinal);

        // ‏والصَفُّ الحَيُّ يُرَكِّبُ مَخزَنَه بِالسُطورِ نَفسِها — مِن جِسمِ دالَّتِه لا مِن هذه المَصفوفَة.
        var live = Regex.Match(StudioConsentCharacterizationTests.Source("tests", "ACommerce.Platform.Tests", "DuplicatePhonesTests.cs"),
            @"private static DocumentStore Store\(\) => DocumentStore\.For\(o =>(?<body>.*?)\}\);", RegexOptions.Singleline);
        Assert.True(live.Success, "مَخزَنُ الصَفِّ الحَيِّ غائِب — الأَداةُ عَمياء.");
        output.WriteLine($"سُطورُ المَخزَنِ المُثَبَّتَة: {StoreLines.Length} · في السكربت وفي الصَفِّ الحَيّ");
        foreach (var line in StoreLines)
            Assert.Contains(line, live.Groups["body"].Value, StringComparison.Ordinal);
    }

    /// <summary><b>الجَدوَلُ الَّذي يَعُدُّ فيه القِياسُ المَفاتيحَ هُوَ جَدوَلُ Marten لَها</b>: <see cref="DuplicatePhones.KeysTable"/> نَصٌّ في SQL
    /// القِياس، و<see cref="ApiKeyDocument"/> بِتَسجيلِ القالِبِ (عامَّة) جَدوَلُها هذا ولا <c>tenant_id</c> فيه (مُوجَب)؛ وجَدوَلُ <see cref="User"/>
    /// بِالسِياسَةِ العامَّةِ غَيرُه ومَحصورٌ بِمُستَأجِر (سالِب) — فَاسمٌ يَنجَرِفُ أَو تَسجيلٌ يَتَغَيَّرُ يُحمِرُّ هُنا لا عَدّاً صامِتاً بِصِفر.</summary>
    [Fact]
    public void The_keys_table_the_scan_counts_is_the_single_tenanted_table_Marten_maps_the_api_key_to()
    {
        using var store = DocumentStore.For(o =>
        {
            o.Connection("Host=localhost;Database=never_opened");
            o.DatabaseSchemaName = "platform";
            o.Policies.AllDocumentsAreMultiTenanted();
            o.Schema.For<ApiKeyDocument>().SingleTenanted().Identity(x => x.Id);
        });
        // ‏جَدوَلُ الوَثيقَةِ كَما يَبنيه Marten مِن تَسجيلِها — الاسمُ والأَعمِدَة، لا نَصٌّ مَكتوبٌ بِاليَد.
        Weasel.Postgresql.Tables.Table TableOf(Type t) => Assert.Single(store.Options.Storage.FindFeature(t).Objects.OfType<Weasel.Postgresql.Tables.Table>());
        bool Tenanted(Weasel.Postgresql.Tables.Table t) => t.Columns.Any(c => c.Name == "tenant_id");
        var key = TableOf(typeof(ApiKeyDocument));
        var user = TableOf(typeof(User));
        output.WriteLine($"المِفتاح: {key.Identifier.Name} (tenant_id: {Tenanted(key)}) · المُستَخدِم: {user.Identifier.Name} (tenant_id: {Tenanted(user)})");
        Assert.Equal(DuplicatePhones.KeysTable, key.Identifier.Name);
        Assert.False(Tenanted(key));
        Assert.NotEqual(DuplicatePhones.KeysTable, user.Identifier.Name);
        Assert.True(Tenanted(user));
    }

    /// <summary>وَثائِقُ عامَّةٌ لا يَراها مَسحُ الجَداوِلِ (‏<see cref="DuplicatePhones.TenantTablesAsync"/> يَسأَلُ عَمودَ <c>tenant_id</c>): كُلُّ
    /// <c>For&lt;T&gt;().SingleTenanted()</c> في <c>libs/</c> و<c>apps/</c> مُسَمّىً هُنا — يَنقُلُه الدَمجُ بِحَقلِه، أَو لا يَحمِلُ حِسابَ عُضوٍ بِسَبَبٍ
    /// مَكتوب. وَثيقَةٌ عامَّةٌ جَديدَةٌ تُحمِرُّه حَتّى تُصَنَّف — فَلا يَعودُ مِفتاحُ الرَبطِ في صورَةٍ أُخرى. وعَيِّنَةٌ مَحقونَةٌ تُثبِتُ أَنَّ الأَداةَ تَرى.</summary>
    [Fact]
    public void Every_single_tenanted_document_is_moved_by_the_merge_or_named_as_holding_no_member_account()
    {
        var movedByTheMerge = new[] { "ApiKeyDocument" };
        var noMemberAccount = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Tenant"] = "OwnerUserId حِسابُ استوديو — ومَجموعاتُ الاستوديو تُعَدُّ ولا تُدمَج («أ» د57 · ٥)",
            ["TenantPlan"] = "SetBy مُشرِفٌ أَو رِسالَةُ مُزَوِّد",
            ["PlatformSettings"] = "UpdatedBy مُشرِف",
            ["PlatformPlanPayPal"] = "CreatedBy مُشرِف",
            ["PayPalWebhookRecord"] = "رِسالَةُ مُزَوِّدٍ بِلا حِساب",
            ["PayPalOrderRecord"] = "CreatedBy «studio · …» أَو platform-admin",
            ["PaddleTransactionRecord"] = "CreatedBy «studio · …» أَو platform-admin",
        };
        var shape = new Regex(@"For<(?<t>[\w\.]+)>\(\)\s*\.SingleTenanted\(\)", RegexOptions.CultureInvariant);
        IEnumerable<string> Found(string code) => shape.Matches(code).Select(m => m.Groups["t"].Value.Split('.')[^1]);
        bool Named(string t) => movedByTheMerge.Contains(t) || noMemberAccount.ContainsKey(t);

        // ‏عَيِّنَةٌ مَحقونَة: تَسجيلٌ عامٌّ جَديدٌ يُرى، ويُعَدُّ غَيرَ مُصَنَّف.
        var injected = Found("opts.Schema.For<Kit.Probe.ProbeDocument>()\n    .SingleTenanted()\n    .Identity(x => x.Id);").ToList();
        Assert.Equal(new[] { "ProbeDocument" }, injected);
        Assert.Equal(injected, injected.Where(t => !Named(t)));

        var root = ThemeZeroEquivalenceTests.RepoRoot;
        var files = new[] { "libs", "apps" }.SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        var found = files.SelectMany(f => Found(File.ReadAllText(f))).OrderBy(t => t, StringComparer.Ordinal).ToList();
        var unnamed = found.Where(t => !Named(t)).ToList();
        output.WriteLine($"مِلَفّاتٌ مَفحوصَة: {files.Count} · وَثائِقُ عامَّة: {found.Count} ({string.Join("، ", found)}) · يَنقُلُها الدَمج: {movedByTheMerge.Length} · "
            + $"بِلا حِسابِ عُضو: {noMemberAccount.Count} · غَيرُ مُصَنَّفَة: {unnamed.Count} · العَيِّنَةُ المَحقونَة: {injected.Count}");

        Assert.NotEmpty(found);
        Assert.Empty(unnamed);
        Assert.Equal(movedByTheMerge.Concat(noMemberAccount.Keys).OrderBy(t => t, StringComparer.Ordinal), found);
    }

    // ─── A1 + G-4 (تَدقيقُ الموجَة ١١، ‏2026-10-29): ما يَرِثُه الباقي، وما يُعَدُّ قَبلَ الدَمج ───────────────────────────────
    //
    // الدَمجُ يَمحو المُندَمِجَ (`AccountDeletion.Erase`) بَعدَ أَن يُعطِيَ الباقِيَ تَحَقُّقَ الجَوّالِ والبَريدَ والهُوِيَّةَ وَحدَها — فَأَدوارُه وخَصائِصُها
    // (ومِنها اسمُ النَشاط) وخَصائِصُه العامَّةُ ودَورُه النَشِطُ واسمُه وقَبولُه لِلشُروطِ واشتِراكاتُ إشعارِه تُمحى مَعَه، والعَدُّ لا يَقولُ إنَّها تَختَلِف.
    // ولا شَيءَ يَمنَعُ مَجموعَةً فيها حِسابٌ إداريّ. القاعِدَة ٣: يُثَبَّتُ السُلوكُ القائِمُ قَبلَ التَبديل.

    private static string MergeBody()
    {
        var code = StudioConsentCharacterizationTests.Source("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Identity", "DuplicatePhones.cs");
        var body = code[code.IndexOf("private static async Task<long> ApplyOnceAsync(", StringComparison.Ordinal)..];
        return body[..body.IndexOf("public static async Task<int> RenameAsync<T>(", StringComparison.Ordinal)];
    }

    /// <summary>حُقولُ الباقي الَّتي يَكتُبُها الدَمجُ — مِن جِسمِه لا مِن قائِمَة.</summary>
    internal static IReadOnlyList<string> SurvivorWrites(string body)
        => Regex.Matches(body, @"survivor\.(\w+)\s*(?:\|=|=(?!=))").Select(m => m.Groups[1].Value)
            .Concat(Regex.Matches(body, @"\((?<list>survivor\.\w+(?:,\s*survivor\.\w+)+)\)\s*=")
                .SelectMany(m => Regex.Matches(m.Groups["list"].Value, @"survivor\.(\w+)").Select(x => x.Groups[1].Value)))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    private static string InheritBody()
    {
        var code = StudioConsentCharacterizationTests.Source("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Identity", "DuplicatePhones.cs");
        var body = code[code.IndexOf("public static void Inherit(User survivor, User loser)", StringComparison.Ordinal)..];
        return body[..body.IndexOf("public sealed record Diff(", StringComparison.Ordinal)];
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، A1 — كوميتُ التَبديل)</b> — كانَ
    /// <c>Today_the_merge_gives_the_survivor_only_its_phone_verification_email_and_national_id</c> (‏<c>9a7735b</c>): «حُقولُ الباقي الَّتي يَكتُبُها الدَمج 6:
    /// Email,EmailVerified,NationalId,Phone,PhoneVerified,UpdatedAt». والآن الدَمجُ يَكتُبُ الجَوّالَ والتاريخَ وَحدَهُما ويُسَلِّمُ الباقي إلى
    /// <c>Inherit</c> قَبلَ مَحوِ المُندَمِج — وهِيَ تَكتُبُ ثَلاثَةَ عَشَرَ حَقلاً (وتَضُمُّ الخَصائِصَ والإشعار).</summary>
    [Fact]
    public void The_merge_hands_the_survivor_to_one_pure_inheritance_before_erasing_the_loser()
    {
        var merge = MergeBody();
        var writes = SurvivorWrites(merge);
        var inherits = SurvivorWrites(InheritBody());
        output.WriteLine($"بَعدَ التَبديل: حُقولُ الباقي في الدَمج {writes.Count}: {string.Join(",", writes)} · في Inherit {inherits.Count}: {string.Join(",", inherits)}");
        Assert.Equal(new[] { "Phone", "UpdatedAt" }, writes);
        Assert.True(merge.IndexOf("Inherit(survivor, loser);", StringComparison.Ordinal) is var at && at > 0
            && at < merge.IndexOf("AccountDeletion.Erase(loser, now)", StringComparison.Ordinal));
        Assert.Equal(new[]
        {
            "AcceptedTermsAt", "AcceptedTermsVersion", "ActiveRole", "AddedContactKind", "AnchorLat", "AnchorLng", "AvatarUrl", "Email", "EmailVerified",
            "FullName", "NationalId", "PhoneVerified", "RadiusKm",
        }, inherits);
        Assert.Contains("survivor.RoleAttributesJson[role] = mine = new();", InheritBody(), StringComparison.Ordinal);
        Assert.Contains("PushSubscriptionDoor.Keep(survivor.PushSubscriptions,", InheritBody(), StringComparison.Ordinal);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، A1 + G-4)</b> — كانَ <c>Today_the_count_names_no_role_name_or_business_name_difference_and_the_scan_asks_no_admin_role</c>:
    /// «كَلِماتُ الفُروقِ في العَدّ 0 مِن 4 · القِياسُ يَسأَلُ عَن الإداريّ: False». والآن العَدُّ يَطبَعُ ما يَختَلِفُ بِـ<c>Differences</c> (أَعلاماً) ومَجموعاتِ
    /// الإداريّ، والقِياسُ يَمنَعُ بِـ<c>AdminBlock</c> بِرَمزِ الكَتالوجِ الواحِد <c>SelfGrantPolicy.AdminSlug</c>.</summary>
    [Fact]
    public void The_count_prints_role_name_and_business_name_differences_and_the_scan_blocks_an_admin_group()
    {
        var count = Code("count-duplicate-phones.cs");
        var scan = StudioConsentCharacterizationTests.Source("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Identity", "DuplicatePhones.cs");
        var printed = new[] { "DuplicatePhones.Differences(byId[g.Survivor], g.Losers.Select(l => byId[l]))", "دَورٌ≠{roles} اسمٌ≠{names} نَشاطٌ≠{businesses}",
            "في الدَور: {roleDiff} · في الاسمِ الحَقيقيّ: {nameDiff} · في اسمِ النَشاط: {businessDiff}", "reasons.GetValueOrDefault(DuplicatePhones.AdminBlock)" };
        var inCount = printed.Count(w => count.Contains(w, StringComparison.Ordinal));
        output.WriteLine($"بَعدَ التَبديل: ما يَطبَعُه العَدّ {inCount} مِن {printed.Length} · القِياسُ يَسأَلُ عَن الإداريّ: {scan.Contains("Gates.SelfGrantPolicy.AdminSlug", StringComparison.Ordinal)}");
        Assert.Equal(printed.Length, inCount);
        Assert.Contains("(\"admin\", Gates.SelfGrantPolicy.AdminSlug)", scan, StringComparison.Ordinal);
        Assert.Contains("if (admins > 0) blocks.Add($\"{AdminBlock}:", scan, StringComparison.Ordinal);
        Assert.Equal(ACommerce.Templates.Customer.Marketplace.Gates.SelfGrantPolicy.AdminSlug, DuplicatePhones.AdminBlock);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، G-6 — كوميتُ التَبديل)</b> — كانَ <c>Today_the_scan_reads_document_bodies_and_never_a_row_id</c>
    /// (‏<c>bfc2900</c>): «أَسئِلَةُ عَمودِ id 0». والآن القِياسُ يَسأَلُ عَمودَ <c>id</c> بِصيغَتَي المُعَرِّف (D وN) فَيَمنَعُ، والمانِعُ يُسَمّي الجَدوَل.</summary>
    [Fact]
    public void The_scan_asks_each_row_id_for_the_loser_id_in_both_forms_and_blocks()
    {
        var scan = StudioConsentCharacterizationTests.Source("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Identity", "DuplicatePhones.cs");
        var body = scan[scan.IndexOf("public static async Task<Scan> ScanAsync(", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("// ─── الكِتابَة", StringComparison.Ordinal)];
        var ids = Regex.Matches(body, @"\bid::text\b").Count;
        output.WriteLine($"بَعدَ التَبديل: أَسئِلَةُ عَمودِ id في القِياس {ids}");
        Assert.Equal(2, ids);
        Assert.Contains("(\"d\", $\"%{l}%\"), (\"n\", $\"%{loser:N}%\")", body, StringComparison.Ordinal);
        Assert.Contains("if (named > 0) blocks.Add($\"{t.Name}: {named} صَفّاً مُعَرِّفُه يَحمِلُ مُعَرِّفَ المُندَمِج\");", body, StringComparison.Ordinal);
    }

    // ─── Inherit: لِكُلِّ قاعِدَةٍ مُوجَبٌ وسالِب ──────────────────────────────────────────────────────────

    private static User U(string name = "", string role = "") => new() { Id = Guid.NewGuid(), FullName = name, ActiveRole = role };

    private static string NewMember => ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic,
        ACommerce.Kit.Auth.Server.AuthHandlers.NewMemberNameKey)!;

    [Fact]
    public void Role_and_account_attributes_are_joined_key_by_key_and_the_survivor_wins_a_clash()
    {
        var survivor = U();
        survivor.RoleAttributesJson["vendor"] = new() { ["business_name"] = "أ" };
        survivor.AttributesJson["bio"] = "باقٍ";
        var loser = U();
        loser.RoleAttributesJson["vendor"] = new() { ["business_name"] = "ب", ["plate"] = "١٢٣" };
        loser.RoleAttributesJson["driver"] = new() { ["license"] = "٧" };
        loser.AttributesJson["bio"] = "مُندَمِج";
        loser.AttributesJson["city"] = "جُدَّة";
        DuplicatePhones.Inherit(survivor, loser);
        AccountDeletion.Erase(loser, T0);
        Assert.Equal(("أ", "١٢٣"), (survivor.RoleAttributesJson["vendor"]["business_name"], survivor.RoleAttributesJson["vendor"]["plate"]));
        Assert.Equal("٧", survivor.RoleAttributesJson["driver"]["license"]);
        Assert.Equal(("باقٍ", "جُدَّة"), (survivor.AttributesJson["bio"], survivor.AttributesJson["city"]));
    }

    [Theory]
    [InlineData("", "vendor", "vendor")]
    [InlineData("rider", "vendor", "rider")]
    [InlineData("", "tenant_admin", "")]
    [InlineData("", "", "")]
    public void The_active_role_goes_to_an_empty_survivor_and_never_the_admin_role(string survivorRole, string loserRole, string expected)
    {
        var survivor = U(role: survivorRole);
        DuplicatePhones.Inherit(survivor, U(role: loserRole));
        Assert.Equal(expected, survivor.ActiveRole);
    }

    [Theory]
    [InlineData("", "سارة", "سارة")]
    [InlineData("new", "سارة", "سارة")]
    [InlineData("هِند", "سارة", "هِند")]
    [InlineData("", "new", "")]
    public void The_name_goes_to_a_survivor_without_one_from_a_loser_with_one(string survivorName, string loserName, string expected)
    {
        string Of(string n) => n == "new" ? NewMember : n;
        var survivor = U(Of(survivorName));
        DuplicatePhones.Inherit(survivor, U(Of(loserName)));
        Assert.Equal(Of(expected), survivor.FullName);
    }

    [Theory]
    [InlineData(2, 3, 3)]
    [InlineData(3, 2, 3)]
    [InlineData(0, 0, 0)]
    public void The_terms_acceptance_is_the_newest_version_with_its_date(int survivorVersion, int loserVersion, int expected)
    {
        var survivor = U();
        (survivor.AcceptedTermsVersion, survivor.AcceptedTermsAt) = (survivorVersion, survivorVersion == 0 ? null : T0.AddDays(survivorVersion));
        var loser = U();
        (loser.AcceptedTermsVersion, loser.AcceptedTermsAt) = (loserVersion, loserVersion == 0 ? null : T0.AddDays(10 + loserVersion));
        DuplicatePhones.Inherit(survivor, loser);
        Assert.Equal(expected, survivor.AcceptedTermsVersion);
        Assert.Equal(expected == 0 ? null : expected == loserVersion && loserVersion > survivorVersion ? T0.AddDays(10 + loserVersion) : T0.AddDays(survivorVersion),
            survivor.AcceptedTermsAt);
    }

    [Fact]
    public void Push_subscriptions_are_joined_once_per_device_within_the_cap_and_survive_the_loser_erasure()
    {
        PushSubscription P(string e, int day) => new() { Endpoint = $"https://push.example/{e}", P256dh = "k", Auth = "a", CreatedAt = T0.AddDays(day) };
        var survivor = U();
        survivor.PushSubscriptions.AddRange(Enumerable.Range(0, 8).Select(i => P($"s{i}", i)));
        var loser = U();
        loser.PushSubscriptions.AddRange(new[] { P("s0", 20), P("l1", 21), P("l2", 22), P("l3", 23) });
        DuplicatePhones.Inherit(survivor, loser);
        AccountDeletion.Erase(loser, T0);
        output.WriteLine($"اشتِراكاتُ الباقي: {survivor.PushSubscriptions.Count} (الحَدّ {ACommerce.Templates.Customer.Marketplace.Services.PushSubscriptionDoor.MaxPerUser})");
        Assert.Equal(ACommerce.Templates.Customer.Marketplace.Services.PushSubscriptionDoor.MaxPerUser, survivor.PushSubscriptions.Count);
        Assert.Single(survivor.PushSubscriptions, p => p.Endpoint.EndsWith("/s0", StringComparison.Ordinal));
        Assert.Contains(survivor.PushSubscriptions, p => p.Endpoint.EndsWith("/l3", StringComparison.Ordinal));
        Assert.Equal(survivor.PushSubscriptions.Count, survivor.PushSubscriptions.Select(p => p.Endpoint).Distinct().Count());
    }

    [Fact]
    public void An_inherited_added_email_stays_added_and_the_survivor_contacts_photo_and_area_are_kept()
    {
        var survivor = U();
        survivor.AvatarUrl = "/a.png";
        (survivor.AnchorLat, survivor.AnchorLng, survivor.RadiusKm) = (21.5, 39.2, 10);
        var loser = U();
        (loser.Email, loser.EmailVerified, loser.AddedContactKind, loser.AvatarUrl) = ("b@example.com", true, ContactLink.Email, "/b.png");
        (loser.AnchorLat, loser.AnchorLng, loser.RadiusKm) = (24.7, 46.7, 5);
        DuplicatePhones.Inherit(survivor, loser);
        Assert.Equal(("b@example.com", true, ContactLink.Email), (survivor.Email, survivor.EmailVerified, survivor.AddedContactKind));
        Assert.Equal(("/a.png", 21.5, 10), (survivor.AvatarUrl, survivor.AnchorLat, survivor.RadiusKm));

        var bare = U();
        DuplicatePhones.Inherit(bare, loser);
        Assert.Equal(("/b.png", 24.7, 5), (bare.AvatarUrl, bare.AnchorLat, bare.RadiusKm));
        var original = U();
        original.Email = "a@example.com";
        DuplicatePhones.Inherit(original, loser);
        Assert.Equal(("a@example.com", ""), (original.Email, original.AddedContactKind));
    }

    [Fact]
    public void Differences_flag_role_real_name_and_business_name_and_nothing_for_identical_accounts()
    {
        var survivor = U("هِند", "rider");
        var same = U("هِند", "rider");
        Assert.Equal(new DuplicatePhones.Diff(false, false, false), DuplicatePhones.Differences(survivor, new[] { same }));
        Assert.Equal(new DuplicatePhones.Diff(false, false, false), DuplicatePhones.Differences(survivor, new[] { U(NewMember) }));

        var vendor = U("سارة", "vendor");
        vendor.RoleAttributesJson["vendor"] = new() { ["business_name"] = "مَتجَرُ سارة" };
        Assert.Equal(new DuplicatePhones.Diff(true, true, true), DuplicatePhones.Differences(survivor, new[] { same, vendor }));
        var named = U("سارة");
        Assert.Equal(new DuplicatePhones.Diff(false, true, false), DuplicatePhones.Differences(U(), new[] { named }));
    }

    /// <summary>سُطورُ مَخزَنِ الدَمجِ الَّتي يُعيدُها الصَفُّ الحَيُّ بِحَرفِها — فَما يُقاسُ هُوَ ما يُشَغَّل.</summary>
    internal static readonly string[] StoreLines =
    {
        "o.DatabaseSchemaName = \"platform\";",
        "o.Policies.AllDocumentsAreMultiTenanted();",
        "o.Events.TenancyStyle = JasperFx.MultiTenancy.TenancyStyle.Conjoined;",
        "o.Schema.For<Tenant>().SingleTenanted().Identity(x => x.Id);",
        "o.Schema.For<ApiKeyDocument>().SingleTenanted().Identity(x => x.Id);",
        "o.Schema.For<User>().Identity(x => x.Id).UseOptimisticConcurrency(true);",
        "o.Projections.Snapshot<Listing>(SnapshotLifecycle.Inline);",
    };
}

[Collection(LiveProofCollection.Name)]
public class LiveDuplicatePhonesProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>مَخزَنُ <c>merge-duplicate-phones.cs</c> بِسُطورِه (<see cref="DuplicatePhonesTests.StoreLines"/> مُثَبَّتَةٌ في السكربت)، ويُنشِئُ
    /// المُخَطَّطَ عَلى القاعِدَةِ المَحَلِّيَّةِ وَحدَها (‏<c>AutoCreate.All</c> — والسكربتُ <c>None</c>).</summary>
    private static DocumentStore Store() => DocumentStore.For(o =>
    {
        o.Connection(LiveDealConcurrencyProofTests.ConnectionString!);
        o.DatabaseSchemaName = "platform";
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Events.TenancyStyle = JasperFx.MultiTenancy.TenancyStyle.Conjoined;
        o.Schema.For<Tenant>().SingleTenanted().Identity(x => x.Id);
        o.Schema.For<ApiKeyDocument>().SingleTenanted().Identity(x => x.Id);
        o.Schema.For<User>().Identity(x => x.Id).UseOptimisticConcurrency(true);
        o.Projections.Snapshot<Listing>(SnapshotLifecycle.Inline);
        o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
    });

    private static async Task CleanAsync(string slug)
    {
        await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
        await conn.OpenAsync();
        foreach (var t in await DuplicatePhones.TenantTablesAsync(conn))
        {
            await using var d = new NpgsqlCommand($"delete from platform.{t.Name} where tenant_id = @t", conn);
            d.Parameters.AddWithValue("t", slug);
            await d.ExecuteNonQueryAsync();
        }
        foreach (var t in new[] { "mt_events", "mt_streams" })
        {
            await using var d = new NpgsqlCommand($"delete from platform.{t} where tenant_id = @t", conn);
            d.Parameters.AddWithValue("t", slug);
            await d.ExecuteNonQueryAsync();
        }
    }

    private static async Task<Guid> ListingAsync(IDocumentStore store, string slug, Guid owner)
    {
        var id = Guid.NewGuid();
        await using var s = store.LightweightSession(slug);
        s.Events.StartStream<Listing>(id, new ListingCreated(id, slug, "إعلان", null, 10m, "general", null, null,
            new Dictionary<string, string> { [ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingEditService.OwnerAttribute] = owner.ToString() },
            DateTime.UtcNow));
        await s.SaveChangesAsync();
        return id;
    }

    private sealed record PhoneMergeProbeEvent(Guid UserId);

    /// <summary><b>الدَمجُ حَيّاً عَلى Postgres</b>: أَ (أَقدَمُ، <c>0500000091</c>) وب (<c>+966500000091</c>) شَخصٌ واحِد — لِبٍ إعلانٌ وصَفقَةٌ مَعَ جـ
    /// ومُحادَثَةٌ ورِسالَةٌ وإشعارٌ ومُفَضَّلَةٌ وسَلَّة. بَعدَ القِياسِ والدَمج: كُلُّها لِأَ، وبٌ مَمحُوٌّ بِإصدارٍ مَرفوع، وجَوّالُ أَ مُوَحَّد، والدُخولُ بِالكِتابَتَينِ
    /// يَفتَحُ أَ. <b>ومانِعٌ يَحمي المَجموعَةَ كُلَّها</b>: د وهـ بَينَهُما صَفقَة، وهـ في مَجرى أَحداثٍ غَيرِ الإعلان ⇒ يُمتَنَعُ ولا يُكتَبُ حَرف.</summary>
    [Fact]
    public async Task A_measured_group_merges_listings_deals_chats_favorites_and_cart_into_the_oldest_account_and_a_blocked_group_is_untouched_on_Postgres()
    {
        if (!Enabled) return;
        using var store = Store();
        await using var dealStore = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "dup-ph-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var (a, b, c, d, e) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(new User { Id = a, TenantSlug = slug, Phone = "0500000091", CreatedAt = DateTime.UtcNow.AddDays(-10), PhoneVerified = false });
                s.Store(new User { Id = b, TenantSlug = slug, Phone = "+966500000091", CreatedAt = DateTime.UtcNow.AddDays(-2), PhoneVerified = true, Email = "b@example.com", EmailVerified = true });
                s.Store(new User { Id = c, TenantSlug = slug, Phone = "0500000092", CreatedAt = DateTime.UtcNow.AddDays(-5) });
                s.Store(new User { Id = d, TenantSlug = slug, Phone = "0500000093", CreatedAt = DateTime.UtcNow.AddDays(-9) });
                s.Store(new User { Id = e, TenantSlug = slug, Phone = "+966500000093", CreatedAt = DateTime.UtcNow.AddDays(-1) });
                var conv = Guid.NewGuid();
                s.Store(new Conversation { Id = conv, OwnerId = c, PartnerId = b, OwnerName = "ج", PartnerName = "ب" });
                s.Store(new Message { Id = Guid.NewGuid(), ConversationId = conv, SenderId = b, Body = "مَرحَباً" });
                s.Store(new Notification { Id = Guid.NewGuid(), UserId = b, Title = "إشعار", Body = "—" });
                s.Store(new Cart { Id = b, Items = [new CartItem { ListingId = Guid.NewGuid(), Title = "بَند", UnitPriceSar = 5m, Quantity = 2 }] });
                s.Events.StartStream(Guid.NewGuid(), new PhoneMergeProbeEvent(e));
                await s.SaveChangesAsync();
            }
            var listingA = await ListingAsync(store, slug, a);
            var listingB = await ListingAsync(store, slug, b);
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(new Favorite { Id = Favorite.MakeId(b, listingA), UserId = b, ListingId = listingA });
                await s.SaveChangesAsync();
            }
            var dealBC = Guid.NewGuid();
            var dealDE = Guid.NewGuid();
            await using (var s = dealStore.LightweightSession(slug))
            {
                s.Store(new Deal { Id = dealBC, TenantSlug = slug, InitiatorId = b, CounterpartyId = c, ListingId = listingB });
                s.Store(new Deal { Id = dealDE, TenantSlug = slug, InitiatorId = d, CounterpartyId = e });
                await s.SaveChangesAsync();
            }

            List<DuplicatePhones.Account> members;
            await using (var q = store.QuerySession(slug))
                members = (await q.Query<User>().ToListAsync()).Select(u => new DuplicatePhones.Account(u.Id, u.Phone, u.CreatedAt, u.DeletedAt is not null)).ToList();
            var plan = DuplicatePhones.Build(members);
            await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
            await conn.OpenAsync();
            var tables = await DuplicatePhones.TenantTablesAsync(conn);
            var gab = plan.Groups.Single(g => g.Survivor == a);
            var gde = plan.Groups.Single(g => g.Survivor == d);
            var scanAB = await DuplicatePhones.ScanAsync(conn, slug, gab, tables);
            var scanDE = await DuplicatePhones.ScanAsync(conn, slug, gde, tables);
            output.WriteLine($"جَداوِل: {tables.Count} · مَجموعات: {plan.Groups.Count} · يُعادُ كَتبُه: {plan.Renames.Count} · أ←ب: يُعادُ {scanAB.Repoints.Sum(r => r.Rows)} صَفّاً في "
                + $"{string.Join(",", scanAB.Repoints.Select(r => r.Table))} · إعلانات {scanAB.Listings.Count} · سِلال {scanAB.LoserCarts} · مُفَضَّلات {scanAB.Favorites} · "
                + $"مَوانِع {scanAB.Blocks.Count} · د←هـ: مَوانِع {string.Join(" | ", scanDE.Blocks)}");

            Assert.Equal(2, plan.Groups.Count);
            Assert.Single(plan.Renames);   // جـ (0500000092)
            Assert.True(scanAB.Mergeable, string.Join(" · ", scanAB.Blocks));
            Assert.False(scanDE.Mergeable);
            Assert.Contains(scanDE.Blocks, x => x.StartsWith("mt_doc_deal:", StringComparison.Ordinal) && x.Contains("يَجمَعُ الحِسابَين", StringComparison.Ordinal));
            Assert.Contains(scanDE.Blocks, x => x.StartsWith("mt_events:", StringComparison.Ordinal));
            await Assert.ThrowsAsync<InvalidOperationException>(() => DuplicatePhones.ApplyAsync(store, slug, scanDE, DateTime.UtcNow));

            var moved = await DuplicatePhones.ApplyAsync(store, slug, scanAB, DateTime.UtcNow);

            await using var r = store.QuerySession(slug);
            var ua = await r.LoadAsync<User>(a);
            var ub = await r.LoadAsync<User>(b);
            var ue = await r.LoadAsync<User>(e);
            var deal = await r.Query<Deal>().SingleAsync(x => x.Id == dealBC);
            var listing = await r.Events.AggregateStreamAsync<Listing>(listingB);
            var snapshot = await r.LoadAsync<Listing>(listingB);
            var conv2 = await r.Query<Conversation>().SingleAsync();
            var msg = await r.Query<Message>().SingleAsync();
            var note = await r.Query<Notification>().SingleAsync();
            var fav = await r.Query<Favorite>().ToListAsync();
            var cartA = await r.LoadAsync<Cart>(a);
            var cartB = await r.LoadAsync<Cart>(b);
            var signTyped = (await LivePhoneIdentityProofTests.SignInAsync(store, slug, "0500000091")).Id;
            var signIntl = (await LivePhoneIdentityProofTests.SignInAsync(store, slug, "+966500000091")).Id;
            var survivorCanonical = ua!.Phone == "+966500000091";
            var survivorInherited = ua.PhoneVerified && ua.Email == "b@example.com" && ua.EmailVerified;
            var blockedUntouched = ue!.Phone == "+966500000093" && ue.DeletedAt is null;
            output.WriteLine($"نُقِلَ: {moved} · أَ مُوَحَّد: {survivorCanonical} · وَرِثَ التَحَقُّقَ والبَريد: {survivorInherited} · بٌ مَمحُوّ: {ub!.DeletedAt is not null} "
                + $"(إصدار {ub.SessionVersion}) · المُمتَنِعَةُ كَما هِي: {blockedUntouched} · الدُخولُ بِالكِتابَتَين ⇒ أَ: {signTyped == a && signIntl == a}");

            Assert.True(survivorCanonical);
            Assert.True(survivorInherited);
            Assert.NotNull(ub.DeletedAt);
            Assert.Equal("", ub.Phone);
            Assert.True(ub.SessionVersion > 0);
            Assert.Equal((a, (Guid?)c), (deal.InitiatorId, deal.CounterpartyId));
            Assert.Equal(a.ToString(), listing!.Attributes[ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingEditService.OwnerAttribute]);
            Assert.Equal(a.ToString(), snapshot!.Attributes[ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingEditService.OwnerAttribute]);
            Assert.Equal((c, a), (conv2.OwnerId, conv2.PartnerId));
            Assert.Equal(a, msg.SenderId);
            Assert.Equal(a, note.UserId);
            var f = Assert.Single(fav);
            Assert.Equal((Favorite.MakeId(a, listingA), a), (f.Id, f.UserId));
            Assert.NotNull(cartA);
            Assert.Null(cartB);
            Assert.Equal(2, cartA!.TotalQuantity);
            Assert.Equal(a, signTyped);
            Assert.Equal(a, signIntl);
            Assert.True(blockedUntouched);
        }
        finally { await CleanAsync(slug); }
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، A1 + G-4 — كوميتُ التَبديل)</b> — كانَ
    /// <c>Today_the_merge_drops_the_merged_away_roles_name_terms_and_push_and_an_admin_group_is_mergeable_on_Postgres</c> (‏<c>9a7735b</c>): «ما وَرِثَه الباقي
    /// مِن المُندَمِج: الدَور False · … · الإشعار False · مَجموعَةُ الإداريِّ قابِلَةٌ لِلدَمج: True». والآن الباقي يَرِثُ كُلَّها، ومَجموعَةٌ فيها <c>tenant_admin</c>
    /// — بِدَورِه النَشِطِ أَو بِدَورٍ في المَتجَرِ رَمزُ كَتالوجِه <c>tenant_admin</c> — تُمنَعُ ولا يُكتَبُ فيها حَرف.</summary>
    [Fact]
    public async Task The_survivor_inherits_the_merged_away_roles_name_terms_and_push_and_an_admin_group_is_blocked_on_Postgres()
    {
        if (!Enabled) return;
        using var store = Store();
        var slug = "dup-a1-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var (a, b, c, d, e, f) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            await using (var s = store.LightweightSession())
            {
                s.Store(new Tenant { Id = slug, Roles = [new ACommerce.Kit.Roles.Role { Slug = "boss", CatalogSlug = "tenant_admin" }] });
                await s.SaveChangesAsync();
            }
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(new User { Id = a, TenantSlug = slug, Phone = "0500000095", CreatedAt = DateTime.UtcNow.AddDays(-10), FullName = "" });
                s.Store(new User
                {
                    Id = b, TenantSlug = slug, Phone = "+966500000095", CreatedAt = DateTime.UtcNow.AddDays(-2), FullName = "سارة",
                    ActiveRole = "vendor", RoleAttributesJson = new() { ["vendor"] = new() { ["business_name"] = "مَتجَرُ سارة" } },
                    AttributesJson = new() { ["bio"] = "—" }, AcceptedTermsAt = DateTime.UtcNow.AddDays(-1), AcceptedTermsVersion = 3,
                    PushSubscriptions = [new PushSubscription { Endpoint = "https://push.example/b", P256dh = "k", Auth = "a", CreatedAt = DateTime.UtcNow }],
                });
                s.Store(new User { Id = c, TenantSlug = slug, Phone = "0500000096", CreatedAt = DateTime.UtcNow.AddDays(-9) });
                s.Store(new User { Id = d, TenantSlug = slug, Phone = "+966500000096", CreatedAt = DateTime.UtcNow.AddDays(-1), ActiveRole = "tenant_admin" });
                s.Store(new User { Id = e, TenantSlug = slug, Phone = "0500000097", CreatedAt = DateTime.UtcNow.AddDays(-9), ActiveRole = "boss" });
                s.Store(new User { Id = f, TenantSlug = slug, Phone = "+966500000097", CreatedAt = DateTime.UtcNow.AddDays(-1) });
                await s.SaveChangesAsync();
            }
            List<DuplicatePhones.Account> members;
            await using (var q = store.QuerySession(slug))
                members = (await q.Query<User>().ToListAsync()).Select(u => new DuplicatePhones.Account(u.Id, u.Phone, u.CreatedAt, u.DeletedAt is not null)).ToList();
            var plan = DuplicatePhones.Build(members);
            await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
            await conn.OpenAsync();
            var tables = await DuplicatePhones.TenantTablesAsync(conn);
            var scanAB = await DuplicatePhones.ScanAsync(conn, slug, plan.Groups.Single(g => g.Survivor == a), tables);
            var scanCD = await DuplicatePhones.ScanAsync(conn, slug, plan.Groups.Single(g => g.Survivor == c), tables);
            var scanEF = await DuplicatePhones.ScanAsync(conn, slug, plan.Groups.Single(g => g.Survivor == e), tables);
            await DuplicatePhones.ApplyAsync(store, slug, scanAB, DateTime.UtcNow);
            await Assert.ThrowsAsync<InvalidOperationException>(() => DuplicatePhones.ApplyAsync(store, slug, scanCD, DateTime.UtcNow));

            User ua, ud;
            await using (var q = store.QuerySession(slug)) (ua, ud) = ((await q.LoadAsync<User>(a))!, (await q.LoadAsync<User>(d))!);
            var kept = new[]
            {
                ("الدَور", ua.ActiveRole == "vendor"), ("خَصائِصُ الدَور", ua.RoleAttributesJson.ContainsKey("vendor")), ("الخَصائِص", ua.AttributesJson.ContainsKey("bio")),
                ("الاسم", ua.FullName == "سارة"), ("الشُروط", ua.AcceptedTermsVersion == 3), ("الإشعار", ua.PushSubscriptions.Count == 1),
            };
            output.WriteLine($"بَعدَ التَبديل: ما وَرِثَه الباقي مِن المُندَمِج: {string.Join(" · ", kept.Select(k => $"{k.Item1} {k.Item2}"))} · "
                + $"مَجموعَةُ الإداريِّ بِدَورِه النَشِط: {scanCD.Mergeable} · بِدَورِ المَتجَر: {scanEF.Mergeable} · الإداريُّ كَما هُوَ: {ud.DeletedAt is null}");
            Assert.All(kept, k => Assert.True(k.Item2, k.Item1));
            Assert.Equal("مَتجَرُ سارة", ACommerce.Templates.Customer.Marketplace.Services.Queries.ActorName.BusinessName(ua));
            Assert.False(scanCD.Mergeable);
            Assert.False(scanEF.Mergeable);
            Assert.Contains(scanCD.Blocks, x => x.StartsWith(DuplicatePhones.AdminBlock + ":", StringComparison.Ordinal));
            Assert.Contains(scanEF.Blocks, x => x.StartsWith(DuplicatePhones.AdminBlock + ":", StringComparison.Ordinal));
            Assert.True(scanAB.Mergeable, string.Join(" · ", scanAB.Blocks));
            Assert.Null(ud.DeletedAt);
        }
        finally
        {
            await using (var s = store.LightweightSession())
            {
                s.Delete<Tenant>(slug);
                await s.SaveChangesAsync();
            }
            await CleanAsync(slug);
        }
    }

    /// <summary>مَدخَلُ بائِعٍ بِاسمِ <paramref name="owner"/> — يُكتَبُ بِـSQL في الجَدوَلِ القائِم (الوَثيقَةُ يُسَجِّلُها القالِبُ لا مَخزَنُ الدَمج):
    /// مُعَرِّفُ الصَفِّ <c>{capability}:{owner}</c> و<c>Id</c> في الجِسمِ نَفسُه، كَما يَكتُبُه <c>TenantProviderService</c>.</summary>
    private static async Task<string> VendorEntryAsync(NpgsqlConnection conn, string slug, Guid owner)
    {
        var id = ACommerce.Platform.Providers.TenantProviderBinding.VendorEntryId(ACommerce.Platform.Providers.ProviderCapabilities.Delivery, owner);
        await using var cmd = new NpgsqlCommand(
            "insert into platform.mt_doc_tenantproviderbinding (tenant_id, id, data) values (@t, @i, @d::jsonb)", conn);
        cmd.Parameters.AddWithValue("t", slug);
        cmd.Parameters.AddWithValue("i", id);
        cmd.Parameters.AddWithValue("d", $$"""{"Id":"{{id}}","Slug":"{{ACommerce.Platform.Providers.ProviderCapabilities.Delivery}}","Status":"active"}""");
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، G-6 — كوميتُ التَبديل)</b> — كانَ
    /// <c>Today_a_vendor_entry_named_by_the_loser_id_is_repointed_in_its_body_and_left_in_its_row_id_on_Postgres</c> (‏<c>bfc2900</c>): «قابِلَةٌ لِلدَمج: True ·
    /// عَمودُ id بِاسمِ المُندَمِج: True · المُعَرِّفُ في الجِسمِ بِاسمِ الباقي: True». والآن مَدخَلُ بائِعٍ بِاسمِ المُندَمِجِ يَمنَعُ المَجموعَةَ ولا يُكتَبُ حَرف؛
    /// ومَدخَلٌ بِاسمِ الباقي لا يَمنَع (سالِب) — المانِعُ لِمُعَرِّفِ المُندَمِجِ وَحدَه.</summary>
    [Fact]
    public async Task A_vendor_entry_named_by_the_loser_id_blocks_the_group_and_one_named_by_the_survivor_does_not_on_Postgres()
    {
        if (!Enabled) return;
        using var store = Store();
        var slug = "dup-g6-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var (a, b, c, d) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(new User { Id = a, TenantSlug = slug, Phone = "0500000098", CreatedAt = DateTime.UtcNow.AddDays(-10) });
                s.Store(new User { Id = b, TenantSlug = slug, Phone = "+966500000098", CreatedAt = DateTime.UtcNow.AddDays(-2) });
                s.Store(new User { Id = c, TenantSlug = slug, Phone = "0500000099", CreatedAt = DateTime.UtcNow.AddDays(-10) });
                s.Store(new User { Id = d, TenantSlug = slug, Phone = "+966500000099", CreatedAt = DateTime.UtcNow.AddDays(-2) });
                await s.SaveChangesAsync();
            }
            await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
            await conn.OpenAsync();
            var entry = await VendorEntryAsync(conn, slug, b);
            await VendorEntryAsync(conn, slug, c);
            List<DuplicatePhones.Account> members;
            await using (var q = store.QuerySession(slug))
                members = (await q.Query<User>().ToListAsync()).Select(u => new DuplicatePhones.Account(u.Id, u.Phone, u.CreatedAt, u.DeletedAt is not null)).ToList();
            var plan = DuplicatePhones.Build(members);
            var tables = await DuplicatePhones.TenantTablesAsync(conn);
            var scanAB = await DuplicatePhones.ScanAsync(conn, slug, plan.Groups.Single(g => g.Survivor == a), tables);
            var scanCD = await DuplicatePhones.ScanAsync(conn, slug, plan.Groups.Single(g => g.Survivor == c), tables);
            await Assert.ThrowsAsync<InvalidOperationException>(() => DuplicatePhones.ApplyAsync(store, slug, scanAB, DateTime.UtcNow));
            await using var read = new NpgsqlCommand("select data->>'Id' from platform.mt_doc_tenantproviderbinding where tenant_id = @t and id = @i", conn);
            read.Parameters.AddWithValue("t", slug);
            read.Parameters.AddWithValue("i", entry);
            var bodyId = (string?)await read.ExecuteScalarAsync();
            User? ub;
            await using (var q = store.QuerySession(slug)) ub = await q.LoadAsync<User>(b);
            output.WriteLine($"بَعدَ التَبديل: مَدخَلٌ بِاسمِ المُندَمِج ⇒ قابِلَةٌ لِلدَمج: {scanAB.Mergeable} ({string.Join(" | ", scanAB.Blocks)}) · "
                + $"الجِسمُ كَما هُوَ: {bodyId == entry} · المُندَمِجُ قائِم: {ub?.DeletedAt is null} · مَدخَلٌ بِاسمِ الباقي ⇒ قابِلَةٌ لِلدَمج: {scanCD.Mergeable}");
            Assert.False(scanAB.Mergeable);
            Assert.Contains(scanAB.Blocks, x => x.StartsWith("mt_doc_tenantproviderbinding:", StringComparison.Ordinal) && x.Contains("مُعَرِّفُه", StringComparison.Ordinal));
            Assert.Equal(entry, bodyId);
            Assert.Null(ub!.DeletedAt);
            Assert.True(scanCD.Mergeable, string.Join(" · ", scanCD.Blocks));
        }
        finally { await CleanAsync(slug); }
    }

    /// <summary><b>مِفتاحُ الرَبطِ يَنتَقِلُ إلى الباقي</b> (‏2026-10-29؛ انقَلَبَ بِاسمِه مِن تَوصيفِ <c>c11fd8a</c>): مِفتاحٌ صَدَرَ بِاسمِ بٍ (المُندَمِج)
    /// — وَثيقَةٌ عامَّةٌ لا يَراها مَسحُ الجَداوِل — يَعُدُّه القِياسُ بِحَقلِه، ويَكتُبُ الدَمجُ فاعِلَه أَ في المُعامَلَةِ نَفسِها: بَعدَ الدَمجِ يُصادِقُ بِاسمِ أَ،
    /// فَعّالاً بِلا تاريخِ إبطال، واسمُه في خَطِّ الصَفقَةِ كَما كَتَبَه صاحِبُ المَتجَر. (كانَ: يُرَدُّ <c>ActorGone</c> و«فَعّال» في الشاشَة.)</summary>
    [Fact]
    public async Task A_key_issued_to_the_merged_away_account_moves_to_the_survivor_and_authenticates_in_their_name_after_the_merge_on_Postgres()
    {
        if (!Enabled) return;
        using var store = Store();
        await using var keyStore = LiveApiKeyActorDeletionProofTests.BuildStore();
        var slug = "dup-ak-" + Guid.NewGuid().ToString("N")[..8];
        var issued = new List<string>();
        try
        {
            var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(new User { Id = a, TenantSlug = slug, Phone = "0500000094", CreatedAt = DateTime.UtcNow.AddDays(-10) });
                s.Store(new User { Id = b, TenantSlug = slug, Phone = "+966500000094", CreatedAt = DateTime.UtcNow.AddDays(-2) });
                await s.SaveChangesAsync();
            }
            var keys = new ApiKeyService(keyStore);
            var key = await keys.IssueAsync(slug, new ApiKeyValidator.IssueRequest("رَبطُ الناقِل", b, "سائِق", new[] { ApiScopeCatalog.DealsRead }, null), Guid.NewGuid());
            issued.Add(key.Key.Id);
            var before = await keys.AuthenticateAsync(key.Presented);

            List<DuplicatePhones.Account> members;
            await using (var q = store.QuerySession(slug))
                members = (await q.Query<User>().ToListAsync()).Select(u => new DuplicatePhones.Account(u.Id, u.Phone, u.CreatedAt, u.DeletedAt is not null)).ToList();
            var group = Assert.Single(DuplicatePhones.Build(members).Groups);
            await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
            await conn.OpenAsync();
            var tables = await DuplicatePhones.TenantTablesAsync(conn);
            var scan = await DuplicatePhones.ScanAsync(conn, slug, group, tables);
            var moved = await DuplicatePhones.ApplyAsync(store, slug, scan, DateTime.UtcNow);

            var after = await keys.AuthenticateAsync(key.Presented);
            ApiKeyDocument? doc;
            await using (var q = keyStore.QuerySession()) doc = await q.LoadAsync<ApiKeyDocument>(key.Key.Id);
            output.WriteLine($"جَدوَلُ المَفاتيحِ في المَسح: {tables.Any(t => t.Name == DuplicatePhones.KeysTable)} · قَبلَ الدَمج: {before.Rejection} (الفاعِلُ ب: {before.Principal?.ActorUserId == b}) · "
                + $"مَفاتيحُ في القِياس: {scan.Keys} · نُقِلَ: {moved} · بَعدَه: {after.Rejection} (الفاعِلُ أ: {after.Principal?.ActorUserId == a}) · "
                + $"الوَثيقَة: {doc?.Status}/{(doc?.RevokedAt is null ? "بِلا تاريخِ إبطال" : "مُؤَرَّخ")} · فاعِلُها أ: {doc?.ActorUserId == a} · الاسمُ كَما كُتِب: {doc?.ActorName == "سائِق"}");

            Assert.DoesNotContain(tables, t => t.Name == DuplicatePhones.KeysTable);
            Assert.Equal(ApiKeyRejection.None, before.Rejection);
            Assert.Equal(b, before.Principal!.ActorUserId);
            Assert.True(scan.Mergeable);
            Assert.Equal(1, scan.Keys);
            Assert.Equal(1, moved);
            Assert.Equal(ApiKeyRejection.None, after.Rejection);
            Assert.Equal(a, after.Principal!.ActorUserId);
            Assert.Equal((ApiKeyDocument.StatusActive, (DateTime?)null, a, "سائِق"), (doc!.Status, doc.RevokedAt, doc.ActorUserId, doc.ActorName));
        }
        finally
        {
            await using (var g = keyStore.LightweightSession())
            {
                foreach (var id in issued) g.Delete<ApiKeyDocument>(id);
                await g.SaveChangesAsync();
            }
            await CleanAsync(slug);
        }
    }
}
