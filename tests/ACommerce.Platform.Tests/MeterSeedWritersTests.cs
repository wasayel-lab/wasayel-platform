using System.Text;
using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G18 — كُلُّ كِتابَةٍ في البَذرِ والسكربتاتِ والمُستَورِدِ مُثَبَّتَةٌ بِمَصيرِها، ولا يولَدُ فيها نَوعٌ مَعدودٌ بِلا عَدّ ═══
// (‏2026-11-12، الموجَة ١٣ W13-6·٨ — ADR-051 §١٥؛ التَصميم §5.1 و§5.2 و§12 G18، RULES-06). المُستَمِعُ يَبلُغُ كُلَّ جَلسَةٍ مِن المَخزَنِ المُرَكَّب —
// والبَذّاراتُ مِنها تَحتَ حامِلِ البَذر — ولا يَبلُغُ السكربتاتِ العَشرَةَ (`DocumentStore.For`) ولا المُستَورِد (`TargetWriter.cs:41`) بِبِنائِها. فَيَحرُسُ هذا:
//   ١. **البَذر**: كُلُّ كِتابَةٍ لِنَوعٍ مَعدودٍ وِلادَةٌ يَراها المُستَمِعُ (`Insert`، أَو `Store` لِنَوعٍ مُتَفائِلٍ بِلا نُسخَة، أَو بِدءُ مَجرى) أَو تَحديثٌ
//      لِما حُمِّلَ في الجَلسَة — و**`Store(new T` لِنَوعٍ يُعَدُّ بِـ`Insert` وِلادَةٌ لا يَراها** (`store_of_insert_counted`)؛
//   ٢. **السكربتات**: لا تَلِدُ نَوعاً مَعدوداً (`script_creates_counted`) — تُحَدِّثُ ما حَمَّلَت أَو تَكتُبُ ما لا يُعَدّ؛
//   ٣. **المُستَورِد**: ما يَكتُبُه مِن نَوعٍ مَعدودٍ استِثناءٌ مُسَمّى في المُطابَقَة (التَصميم §9) لا عَدّ (`importer_not_excluded`)؛
//   ٤. وكُلُّ مَوضِعِ كِتابَةٍ في المِلَفّاتِ المَفحوصَةِ مُثَبَّتٌ بِنَوعِه ومَصيرِه (`unpinned_write`/`stale_pin`)، والنَوعُ المُعلَنُ يُطابِقُ ما يُقرَأُ
//      مِن المَوضِعِ حينَ يُقرَأ (`type_mismatch`)، والمَصيرُ يُوافِقُ نَوعَه (`fate_contradicts_type`)، وكُلُّ بَذّارٍ في كُتلَةِ البَذرِ مِلَفُّه مَفحوص
//      (`seed_block_unscanned`).
// والمَعدودُ يُقرَأُ مِن دَفتَرِ التَصنيفِ (`MeterClassification.All`) لا مِن قائِمَةٍ هُنا. **وحَدُّ الأَداةِ يُقال**: الماسِحُ نَصِّيٌّ عَلى نِداءاتِ الكِتابَةِ في المِلَفِّ
// نَفسِه، فَكِتابَةُ دالَّةٍ في مَكتَبَةٍ يُناديها سكربت (`DuplicatePhones.RenameAsync`، `AccountErasure`) خارِجَه — وتِلكَ تُحَدِّثُ ما حَمَّلَت.

public class MeterSeedWritersTests(ITestOutputHelper output)
{
    /// <summary>مَصيرُ مَوضِعِ كِتابَة.</summary>
    internal enum Fate
    {
        /// <summary>بَذّارٌ يَلِدُ نَوعاً مَعدوداً بِشَكلٍ يَراه المُستَمِع — سَطرٌ تَحتَ <c>_seed</c> (وقَد يُحَدِّثُ أَيضاً إن وَجَدَ الوَثيقَة: المُستَمِعُ يُفَرِّقُ بِالنُسخَة).</summary>
        SeedBirth,
        /// <summary>الوَثيقَةُ حُمِّلَت في الجَلسَةِ نَفسِها، أَو الحَدَثُ يُلحَقُ بِمَجرىً قائِم — لا وِلادَةَ أَبَداً.</summary>
        UpdateOnly,
        /// <summary>النَوعُ لا يُعَدّ (دَفتَرُ التَصنيف).</summary>
        NotCounted,
        /// <summary>المُستَورِدُ يَكتُبُ نَوعاً مَعدوداً مُتَجاوِزاً المُستَمِعَ بِبِنائِه — استِثناءٌ مُسَمّى في المُطابَقَة (التَصميم §9).</summary>
        ImporterExcluded,
    }

    internal enum Group { Seeder, Script, Importer }

    /// <summary>مَوضِعٌ مُثَبَّت: المِلَفُّ، ومِفتاحُ النِداء (المُستَقبِلُ والنِداءُ ورَأسُ الوَسيط)، والنَوعُ المَكتوب، ومَصيرُه. لِكُلِّ تَكرارٍ صَفّ، بِتَرتيبِه في المِلَفّ.</summary>
    internal sealed record Pin(string File, string Key, string Type, Fate Fate);

    /// <summary>مَوضِعٌ مَقيس: المِلَفُّ وسَطرُه ومِفتاحُه، والنَوعُ إن قُرِئَ مِن النَصّ، ونِداؤُه.</summary>
    internal sealed record Site(string File, Group Group, int Line, string Key, string? Resolved, string Call, bool ArgIsNew);

    // ─── الماسِح ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>الكودُ بِلا تَعليقاتٍ ولا مُحتَوى نُصوص — مَعَ الحِفاظِ عَلى الطولِ والأَسطُر (تُستَبدَلُ بِمَسافات، والأَقواسُ تَبقى): <c>//</c> داخِلَ نَصٍّ لَيسَ
    /// تَعليقاً، ونِداءٌ مَكتوبٌ داخِلَ نَصٍّ لَيسَ نِداءً.</summary>
    internal static string StripComments(string src)
    {
        var sb = new StringBuilder(src);
        var i = 0;
        while (i < src.Length)
        {
            var c = src[i];
            if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
            {
                while (i < src.Length && src[i] != '\n') { sb[i] = ' '; i++; }
            }
            else if (c == '/' && i + 1 < src.Length && src[i + 1] == '*')
            {
                while (i < src.Length && !(src[i] == '*' && i + 1 < src.Length && src[i + 1] == '/')) { if (src[i] != '\n') sb[i] = ' '; i++; }
                if (i < src.Length) { sb[i] = ' '; sb[i + 1] = ' '; i += 2; }
            }
            else if (c == '"' && i + 2 < src.Length && src[i + 1] == '"' && src[i + 2] == '"')
            {
                // نَصٌّ خامّ (<c>"""…"""</c>): يُفرَغُ إلى الثُلاثِيِّ التالي.
                var close = src.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                if (close < 0) close = src.Length - 3;
                for (var k = i + 3; k < close; k++) if (src[k] != '\n') sb[k] = ' ';
                i = close + 3;
            }
            else if (c == '"')
            {
                var verbatim = i > 0 && src[i - 1] == '@' || i > 1 && src[i - 1] == '$' && src[i - 2] == '@';
                i++;
                while (i < src.Length)
                {
                    if (!verbatim && src[i] == '\\') { sb[i] = ' '; if (i + 1 < src.Length && src[i + 1] != '\n') sb[i + 1] = ' '; i += 2; continue; }
                    if (src[i] == '"') { if (verbatim && i + 1 < src.Length && src[i + 1] == '"') { sb[i] = ' '; sb[i + 1] = ' '; i += 2; continue; } i++; break; }
                    if (src[i] != '\n') sb[i] = ' ';
                    i++;
                }
            }
            else if (c == '\'' && i + 2 < src.Length)
            {
                var end = src[i + 1] == '\\' ? i + 3 : i + 2;
                for (var k = i + 1; k < end && k < src.Length; k++) sb[k] = ' ';
                i = end + 1;
            }
            else i++;
        }
        return sb.ToString();
    }

    /// <summary>نِداءُ كِتابَة: <c>x.Store(</c> و<c>x.Insert(</c> و<c>x.StoreObjects(</c> و<c>x.InsertObjects(</c>، و<c>x.Events.StartStream(</c>
    /// و<c>x.Events.Append(</c> — والحَدَثانِ بِـ<c>Events.</c> وَحدَها (فَـ<c>StringBuilder.Append</c> لا يُعَدّ).</summary>
    private static readonly Regex WriteCall = new(
        @"(?<recv>\b[A-Za-z_]\w*)\s*\.\s*(?:(?<events>Events)\s*\.\s*(?<ecall>StartStream|Append)|(?<dcall>StoreObjects|InsertObjects|Store|Insert))\s*(?<gen><[^<>();]*>)?\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>نَصُّ الوُسَطاءِ حَتّى القَوسِ المُقابِل — عَلى كودٍ نُصوصُه مُفرَغَة (<see cref="StripComments"/>).</summary>
    internal static string Arguments(string code, int open)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            var c = code[i];
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}')
            {
                if (--depth == 0) return code[(open + 1)..i];
            }
        }
        return code[(open + 1)..];
    }

    /// <summary>أَوَّلُ وَسيطٍ أَعلى المُستَوى، والباقي — بِالفاصِلَةِ في العُمقِ صِفر.</summary>
    internal static (string First, string Others) SplitFirst(string args)
    {
        var depth = 0;
        for (var i = 0; i < args.Length; i++)
        {
            var c = args[i];
            if (c is '(' or '[' or '{' or '<') depth++;
            else if (c is ')' or ']' or '}' or '>') depth--;
            else if (c == ',' && depth == 0) return (args[..i].Trim(), args[(i + 1)..].Trim());
        }
        return (args.Trim(), "");
    }

    private static readonly Regex NewType = new(@"^new\s+(?<t>[A-Za-z_][\w.]*)", RegexOptions.Compiled);
    private static readonly Regex Identifier = new(@"^(?<id>[A-Za-z_]\w*)!?$", RegexOptions.Compiled);
    private static readonly HashSet<string> Containers = new(StringComparer.Ordinal) { "List", "Dictionary", "HashSet", "Queue", "Stack" };

    private static string Last(string qualified) => qualified[(qualified.LastIndexOf('.') + 1)..];

    /// <summary>نَوعُ مُتَغَيِّرٍ مِن آخِرِ إسنادٍ أَو إعلانٍ قَبلَ النِداءِ في المِلَفِّ نَفسِه: <c>new T</c> أَو <c>LoadAsync&lt;T&gt;</c> أَو <c>Query&lt;T&gt;</c>،
    /// أَو إعلانٌ بِنَوعِه (<c>T x</c>) — وإلّا <c>null</c> (يُعلَنُ في الصَفِّ ويُطبَعُ «مُعلَن»).</summary>
    internal static string? ResolveIdentifier(string code, int before, string id)
    {
        var assign = new Regex($@"(?:\bvar\s+|\b[A-Z][\w.<>]*\??\s+)?\b{Regex.Escape(id)}\s*(?:\?\?=|=(?![=>]))\s*(?<rhs>[^;]+);", RegexOptions.CultureInvariant);
        string? found = null;
        foreach (Match m in assign.Matches(code[..before]))
        {
            var rhs = m.Groups["rhs"].Value;
            var n = Regex.Match(rhs, @"\bnew\s+(?<t>[A-Za-z_][\w.]*)");
            var g = Regex.Match(rhs, @"\b(?:LoadAsync|Query)\s*<\s*(?<t>[A-Za-z_][\w.]*)\s*>");
            var t = g.Success ? Last(g.Groups["t"].Value) : n.Success ? Last(n.Groups["t"].Value) : null;
            if (t is not null && !Containers.Contains(t)) found = t;
        }
        if (found is not null) return found;
        var decl = Regex.Matches(code[..before], $@"\b(?<t>[A-Z][\w.]*)\??\s+{Regex.Escape(id)}\s*[;,)=]");
        return decl.Count > 0 ? Last(decl[^1].Groups["t"].Value) : null;
    }

    /// <summary>كُلُّ مَوضِعِ كِتابَةٍ في نَصِّ مِلَفّ.</summary>
    internal static IReadOnlyList<Site> Scan(string file, Group group, string source)
    {
        var code = StripComments(source);
        var sites = new List<Site>();
        foreach (Match m in WriteCall.Matches(code))
        {
            var open = m.Index + m.Length - 1;
            var args = Arguments(code, open);
            var isEvent = m.Groups["events"].Success;
            var call = isEvent ? m.Groups["ecall"].Value : m.Groups["dcall"].Value;
            var (first, rest) = SplitFirst(args);
            var written = isEvent ? rest : first;
            var head = Regex.Replace(written, @"\s+", " ");
            var n = NewType.Match(head);
            string headKey;
            string? resolved = null;
            if (n.Success) { resolved = Last(n.Groups["t"].Value); headKey = "new " + resolved; }
            else
            {
                var token = Regex.Match(head, @"^[A-Za-z_][\w.]*!?").Value;
                headKey = token.Length > 0 ? token : head[..Math.Min(head.Length, 24)];
                var id = Identifier.Match(token);
                if (id.Success) resolved = ResolveIdentifier(code, m.Index, id.Groups["id"].Value);
            }
            var key = $"{m.Groups["recv"].Value}.{(isEvent ? "Events." : "")}{call}{m.Groups["gen"].Value}({headKey}";
            var line = code.AsSpan(0, m.Index).Count('\n') + 1;
            sites.Add(new Site(file, group, line, key, resolved, call, n.Success));
        }
        return sites;
    }

    // ─── المِلَفّات ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static string Root => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Rel(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    /// <summary>بَذّاراتُ كُتلَةِ البَذرِ في <c>Program.cs</c> — مِن فاتِحِ حامِلِ البَذرِ إلى نِهايَةِ الكُتلَة: كُلُّ <c>await X.RunAsync(</c>.</summary>
    internal static IReadOnlyList<string> SeedBlockSeeders(string program)
    {
        var start = program.IndexOf("MeterScope.Seed()", StringComparison.Ordinal);
        Assert.True(start >= 0, "فاتِحُ حامِلِ البَذرِ غائِبٌ عَن Program.cs — الأَداةُ عَمياء.");
        var end = program.IndexOf("app.UsePlatformForwardedHeaders", start, StringComparison.Ordinal);
        Assert.True(end > start, "نِهايَةُ كُتلَةِ البَذرِ غائِبَة — الأَداةُ عَمياء.");
        return [.. Regex.Matches(StripComments(program[start..end]), @"await\s+(?:[\w.\s]+?\.\s*)?(?<c>[A-Z]\w*)\s*\.\s*RunAsync\s*\(")
            .Select(m => m.Groups["c"].Value).Distinct()];
    }

    internal static IReadOnlyList<(string File, Group Group, string Text)> Files()
    {
        var files = new List<(string, Group, string)>();
        var seedDir = Path.Combine(Root, "apps", "V1.App", "Seed");
        foreach (var f in Directory.EnumerateFiles(seedDir, "*.cs").Order(StringComparer.Ordinal)) files.Add((Rel(f), Group.Seeder, File.ReadAllText(f)));
        var ownership = Path.Combine(Root, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Incubator", "StudioOwnershipSeeder.cs");
        files.Add((Rel(ownership), Group.Seeder, File.ReadAllText(ownership)));
        foreach (var f in Directory.EnumerateFiles(Path.Combine(Root, "scripts"), "*.cs").Order(StringComparer.Ordinal)) files.Add((Rel(f), Group.Script, File.ReadAllText(f)));
        foreach (var f in Directory.EnumerateFiles(Path.Combine(Root, "tools", "ACommerce.Importer"), "*.cs").Order(StringComparer.Ordinal)) files.Add((Rel(f), Group.Importer, File.ReadAllText(f)));
        return files;
    }

    // ─── المُثَبَّت ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private const string Seed = "apps/V1.App/Seed/";
    private const string Ownership = "libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Incubator/StudioOwnershipSeeder.cs";
    private const string Importer = "tools/ACommerce.Importer/TargetWriter.cs";

    /// <summary><b>كُلُّ مَوضِعِ كِتابَةٍ مُثَبَّتٌ بِنَوعِه ومَصيرِه</b> (‏مَقيسٌ عِندَ W13-6·٨: 47 مَوضِعاً — بَذر 35 · سكربت 5 · مُستَورِد 7). مَوضِعٌ جَديدٌ يَحمَرُّ حَتّى
    /// يُثَبَّتَ هُنا بِقَرارٍ مَرئِيٍّ في مُراجَعَة، فَلا يولَدُ في البَذرِ نَوعٌ مَعدودٌ بِشَكلٍ لا يَراه المُستَمِع ولا يُعَرَفُ ذلك.</summary>
    internal static readonly IReadOnlyList<Pin> Pinned =
    [
        // ─── AppearanceBaselineSeeder ───
        new(Seed + "AppearanceBaselineSeeder.cs", "global.Insert(new Tenant", "Tenant", Fate.SeedBirth),                 // theme-demo — قُلِبَ في هذا البَند
        new(Seed + "AppearanceBaselineSeeder.cs", "s.Events.StartStream<Offer>(new OfferSubmitted", "OfferSubmitted", Fate.SeedBirth),
        new(Seed + "AppearanceBaselineSeeder.cs", "global.Insert(new Tenant", "Tenant", Fate.SeedBirth),                 // owner-test — قُلِبَ في هذا البَند
        new(Seed + "AppearanceBaselineSeeder.cs", "s.Store(new Ticket", "Ticket", Fate.NotCounted),                      // وَثيقَةٌ بِلا مَجرى؛ اللَقطَةُ مُشتَقَّة
        new(Seed + "AppearanceBaselineSeeder.cs", "s.Events.StartStream<Listing>(new ListingCreated", "ListingCreated", Fate.SeedBirth),
        new(Seed + "AppearanceBaselineSeeder.cs", "s.Store(new Deal", "Deal", Fate.SeedBirth),
        new(Seed + "AppearanceBaselineSeeder.cs", "s.Store(new Deal", "Deal", Fate.SeedBirth),
        new(Seed + "AppearanceBaselineSeeder.cs", "s.Store(new Conversation", "Conversation", Fate.NotCounted),
        new(Seed + "AppearanceBaselineSeeder.cs", "s.Store(new Message", "Message", Fate.NotCounted),                    // البَند ٩ يَقلِبُه
        new(Seed + "AppearanceBaselineSeeder.cs", "s.Store(new TenantRoleDefinition", "TenantRoleDefinition", Fate.NotCounted),
        new(Seed + "AppearanceBaselineSeeder.cs", "s.Events.StartStream<Listing>(events.ToArray", "ListingCreated", Fate.SeedBirth), // مُعلَن: قائِمَةٌ أَوَّلُها ListingCreated
        new(Seed + "AppearanceBaselineSeeder.cs", "s.Store(new User", "User", Fate.SeedBirth),
        new(Seed + "AppearanceBaselineSeeder.cs", "s.Store(t", "Tenant", Fate.UpdateOnly),                                // المِلكِيَّة: حُمِّلَ بِـLoadAsync في الجَلسَةِ نَفسِها
        new(Seed + "AppearanceBaselineSeeder.cs", "s.Store(new TenantThemeDefinition", "TenantThemeDefinition", Fate.NotCounted),
        new(Seed + "AppearanceBaselineSeeder.cs", "s.Store(r", "ImportedRecord", Fate.NotCounted),                       // مُعلَن: RegionsSaveService.ToRecords
        // ─── بَذّاراتُ العَيِّنات ───
        new(Seed + "CartSampleSeeder.cs", "session.Store(new Cart", "Cart", Fate.NotCounted),
        new(Seed + "IncubatorSampleSeeder.cs", "session.Store(new IncubatorSession", "IncubatorSession", Fate.NotCounted),
        // ─── PlatformAdminSeeder: يَلِدُ الحِسابَ إن غابَ ويُرَقّيه إن وُجِد — والمُستَمِعُ يُفَرِّقُ بِالنُسخَة ───
        new(Seed + "PlatformAdminSeeder.cs", "s.Store(byPhone!", "StudioUser", Fate.SeedBirth),
        new(Seed + "PlatformAdminSeeder.cs", "s.Store(byEmail!", "StudioUser", Fate.SeedBirth),
        // ─── PlatformSeed ───
        new(Seed + "PlatformSeed.cs", "s.Store(new Plan", "Plan", Fate.NotCounted),
        new(Seed + "PlatformSeed.cs", "s.Store(new Plan", "Plan", Fate.NotCounted),
        new(Seed + "PlatformSeed.cs", "s.Store(new Plan", "Plan", Fate.NotCounted),
        new(Seed + "PlatformSeed.cs", "globalSession.Insert(tenant", "Tenant", Fate.SeedBirth),                           // قُلِبَ في هذا البَند
        new(Seed + "PlatformSeed.cs", "tenantSession.Events.StartStream<Listing>(ev", "ListingCreated", Fate.SeedBirth),
        new(Seed + "PlatformSeed.cs", "tenantSession.Events.Append(new ListingFlagsSet", "ListingFlagsSet", Fate.NotCounted),
        // ─── TestDataSeeder ───
        new(Seed + "TestDataSeeder.cs", "globalSession.Store(tenant", "Tenant", Fate.UpdateOnly),                         // أَدوارٌ تُضافُ إلى مَتجَرٍ قائِم
        new(Seed + "TestDataSeeder.cs", "tenantSession.Store(user", "User", Fate.SeedBirth),                              // مُستَخدِمٌ جَديد
        new(Seed + "TestDataSeeder.cs", "tenantSession.Store(user", "User", Fate.UpdateOnly),                             // الدَورُ الفَعّالُ لِمَن قُرِئَ بِـQuery
        new(Seed + "TestDataSeeder.cs", "s.Events.StartStream<Listing>(ev", "ListingCreated", Fate.SeedBirth),
        new(Seed + "TestDataSeeder.cs", "s.Store(new Notification", "Notification", Fate.NotCounted),
        new(Seed + "TestDataSeeder.cs", "s.Store(new Conversation", "Conversation", Fate.NotCounted),
        new(Seed + "TestDataSeeder.cs", "s.Store(new Message", "Message", Fate.NotCounted),                               // البَند ٩ يَقلِبُه
        new(Seed + "TestDataSeeder.cs", "s.Store(new Deal", "Deal", Fate.SeedBirth),
        new(Seed + "TestDataSeeder.cs", "s.Store(new Deal", "Deal", Fate.SeedBirth),
        // ─── StudioOwnershipSeeder: يَتامى قُرِئوا في جَلسَةِ قِراءَةٍ ويُكتَبونَ في أُخرى — Tenant لا يُعَدُّ إلّا مِن Inserts، فَهذا تَحديثٌ لا وِلادَة ───
        new(Ownership, "ws.Store(t", "Tenant", Fate.UpdateOnly),                                                           // مُعلَن: foreach عَلى Query<Tenant>
        // ─── السكربتات: لا تَلِدُ مَعدوداً ───
        new("scripts/repair-commercial-categories.cs", "s.Store(live", "Tenant", Fate.UpdateOnly),
        new("scripts/repair-plan-day-end.cs", "s.Store(live", "TenantPlan", Fate.NotCounted),
        new("scripts/repair-role-floor.cs", "s.Store(live", "Tenant", Fate.UpdateOnly),
        new("scripts/repair-studio-tier.cs", "s.Store(live", "StudioUser", Fate.UpdateOnly),
        new("scripts/withdraw-erased-owner-listings.cs", "n.Store(DealNotices.ClosedByErasureFor", "Notification", Fate.NotCounted), // مُعلَن: يُعيدُ Notification
        // ─── المُستَورِد: استِثناءٌ مُسَمّى في المُطابَقَة (التَصميم §9) ───
        new(Importer, "s.Store(t", "Tenant", Fate.ImporterExcluded),
        new(Importer, "s.Store(d", "User", Fate.ImporterExcluded),                                                         // مُعلَن: UpsertAsync<T> — User والمُفَضَّلَةُ والمُحادَثاتُ والرَسائِلُ والإشعاراتُ والباقات
        new(Importer, "s.Events.Append(new ListingEdited", "ListingEdited", Fate.NotCounted),
        new(Importer, "s.Events.StartStream<Listing>(new ListingCreated", "ListingCreated", Fate.ImporterExcluded),
        new(Importer, "s.Events.StartStream<Subscription>(new SubscriptionCreated", "SubscriptionCreated", Fate.ImporterExcluded),
        new(Importer, "s.Events.StartStream<Ticket>(new TicketCreated", "TicketCreated", Fate.ImporterExcluded),
        new(Importer, "s.Store(d", "ImportedRecord", Fate.NotCounted),                                                   // مُعلَن: الجَداوِلُ الخامُّ
    ];

    // ─── الحُكم ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>الأَنواعُ المَعدودَةُ بِشَكلِ وِلادَتِها — مِن دَفتَرِ التَصنيف.</summary>
    internal static IReadOnlyDictionary<string, MeterCreation> CountedTypes(IEnumerable<MeterClassificationRow> rows)
        => rows.Where(r => r.Disposition == MeterDisposition.Counted && r.Creation is not null)
               .ToDictionary(r => r.TypeName, r => r.Creation!.Value, StringComparer.Ordinal);

    /// <summary>كُلُّ خَرقٍ بِرَمزِه — والقائِمَةُ الفارِغَةُ وَحدَها خَضراء.</summary>
    internal static IReadOnlyList<string> Breaches(IReadOnlyList<Site> sites, IReadOnlyList<Pin> pins,
        IReadOnlyDictionary<string, MeterCreation> counted, IReadOnlyList<string> seedBlock, IReadOnlyCollection<string> scannedFiles)
    {
        var breaches = new List<string>();
        var pinQueues = pins.GroupBy(p => (p.File, p.Key)).ToDictionary(g => g.Key, g => new Queue<Pin>(g));
        foreach (var site in sites)
        {
            if (!pinQueues.TryGetValue((site.File, site.Key), out var q) || q.Count == 0)
            {
                breaches.Add($"unpinned_write:{site.File}:{site.Line}:{site.Key}");
                if (site.ArgIsNew && site.Call is "Store" or "StoreObjects" && site.Resolved is { } t0
                    && counted.TryGetValue(t0, out var c0) && c0 == MeterCreation.Insert && site.Group != Group.Importer)
                    breaches.Add($"store_of_insert_counted:{site.File}:{site.Line}:{t0}");
                continue;
            }
            var pin = q.Dequeue();
            if (site.Resolved is { } r && r != pin.Type) breaches.Add($"type_mismatch:{site.File}:{site.Line}:{site.Key} ({r}≠{pin.Type})");
            var isCounted = counted.TryGetValue(pin.Type, out var creation);
            if (site.Call is "Store" or "StoreObjects" && isCounted && creation == MeterCreation.Insert && site.Group != Group.Importer
                && (site.ArgIsNew || pin.Fate == Fate.SeedBirth))
                breaches.Add($"store_of_insert_counted:{site.File}:{site.Line}:{pin.Type}");
            if (site.Group == Group.Script && isCounted && pin.Fate != Fate.UpdateOnly)
                breaches.Add($"script_creates_counted:{site.File}:{site.Line}:{pin.Type}");
            if (site.Group == Group.Importer && isCounted && pin.Fate != Fate.ImporterExcluded)
                breaches.Add($"importer_not_excluded:{site.File}:{site.Line}:{pin.Type}");
            var fateFits = pin.Fate switch
            {
                Fate.NotCounted => !isCounted,
                Fate.SeedBirth => isCounted && site.Group == Group.Seeder
                                  && (creation != MeterCreation.StartStream || site.Call == "StartStream")
                                  && (creation != MeterCreation.Insert || site.Call is "Insert" or "InsertObjects"),
                Fate.UpdateOnly => isCounted,
                Fate.ImporterExcluded => isCounted && site.Group == Group.Importer,
                _ => false,
            };
            if (!fateFits && !breaches.Any(b => b.Contains($":{site.File}:{site.Line}:", StringComparison.Ordinal)))
                breaches.Add($"fate_contradicts_type:{site.File}:{site.Line}:{pin.Type}@{pin.Fate}");
        }
        foreach (var ((file, key), q) in pinQueues)
            foreach (var stale in q) breaches.Add($"stale_pin:{file}:{key}");
        foreach (var seeder in seedBlock.Where(s => !scannedFiles.Any(f => Path.GetFileNameWithoutExtension(f) == s)))
            breaches.Add($"seed_block_unscanned:{seeder}");
        return breaches;
    }

    internal static IReadOnlyList<string> Codes { get; } =
        ["unpinned_write", "stale_pin", "type_mismatch", "store_of_insert_counted", "script_creates_counted", "importer_not_excluded",
         "fate_contradicts_type", "seed_block_unscanned"];

    private static string CodeOf(string breach) => breach[..breach.IndexOf(':')];

    // ─── الصُفوف ────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Every_write_in_the_seeders_scripts_and_importer_is_pinned_with_its_fate_and_no_counted_type_is_born_uncounted()
    {
        var files = Files();
        var sites = files.SelectMany(f => Scan(f.File, f.Group, f.Text)).ToList();
        var counted = CountedTypes(MeterClassification.All);
        var seedBlock = SeedBlockSeeders(File.ReadAllText(Path.Combine(Root, "apps", "V1.App", "Program.cs")));
        var breaches = Breaches(sites, Pinned, counted, seedBlock, files.Select(f => f.File).ToList());

        foreach (var s in sites)
        {
            var pin = Pinned.FirstOrDefault(p => p.File == s.File && p.Key == s.Key);
            output.WriteLine($"[G18] {s.File}:{s.Line} {s.Key} → {s.Resolved ?? "؟"} · {pin?.Fate.ToString() ?? "غَيرُ مُثَبَّت"}");
        }
        output.WriteLine("[G18] المَقيسُ بِشَكلِ المِلَفّ:\n" + string.Join("\n", sites.Select(s =>
            $"        new(\"{s.File}\", \"{s.Key.Replace("\"", "\\\"")}\", \"{s.Resolved ?? "?"}\", Fate.{(s.Resolved is { } t && counted.ContainsKey(t) ? "SeedBirth" : "NotCounted")}),")));
        var byGroup = sites.GroupBy(s => s.Group).ToDictionary(g => g.Key, g => g.Count());
        output.WriteLine($"[G18] مِلَفّات {files.Count} (بَذر {files.Count(f => f.Group == Group.Seeder)} · سكربت {files.Count(f => f.Group == Group.Script)} · مُستَورِد {files.Count(f => f.Group == Group.Importer)}) · "
            + $"مَواضِعُ الكِتابَة {sites.Count} (بَذر {byGroup.GetValueOrDefault(Group.Seeder)} · سكربت {byGroup.GetValueOrDefault(Group.Script)} · مُستَورِد {byGroup.GetValueOrDefault(Group.Importer)}) · "
            + $"مَقروءُ النَوع {sites.Count(s => s.Resolved is not null)} · مُعلَن {sites.Count(s => s.Resolved is null)} · مُثَبَّت {Pinned.Count}");
        output.WriteLine($"[G18] المَعدودُ مِن الدَفتَر {counted.Count}: {string.Join(", ", counted.Select(c => $"{c.Key}@{c.Value}"))} · "
            + $"مَصائِرُ المَعدود: {string.Join(" · ", Pinned.Where(p => counted.ContainsKey(p.Type)).GroupBy(p => p.Fate).Select(g => $"{g.Key} {g.Count()}"))}");
        output.WriteLine($"[G18] بَذّاراتُ الكُتلَة {seedBlock.Count}: {string.Join(", ", seedBlock)} · خُروق {breaches.Count}" + (breaches.Count == 0 ? "" : ":\n" + string.Join("\n", breaches)));

        Assert.True(files.Count > 10 && sites.Count > 20 && counted.Count >= 5 && seedBlock.Count >= 5,
            $"أَداةٌ عَمياء: {files.Count} مِلَفّاً · {sites.Count} مَوضِعاً · {counted.Count} نَوعاً مَعدوداً · {seedBlock.Count} بَذّاراً.");
        Assert.Contains("StudioOwnershipSeeder", seedBlock);
        Assert.Empty(breaches);
    }

    /// <summary><b>الحَقنُ المُسَمّى في البَند</b>: نُسخَةٌ مِن <c>AppearanceBaselineSeeder.cs</c> تَعودُ فيها وِلادَةُ «theme-demo» إلى <c>Store(new Tenant</c> —
    /// وِلادَةٌ لا يَراها المُستَمِع — تَحمَرُّ بِـ<c>store_of_insert_counted</c> (ومَعَه المَوضِعُ غَيرُ المُثَبَّتِ والصَفُّ الَّذي فَقَدَ مَوضِعَه).</summary>
    [Fact]
    public void An_injected_Store_of_a_new_Tenant_in_a_copy_of_a_seeder_is_red()
    {
        const string file = Seed + "AppearanceBaselineSeeder.cs";
        var text = File.ReadAllText(Path.Combine(Root, file));
        const string born = "global.Insert(new Tenant";
        var at = text.IndexOf(born, StringComparison.Ordinal);
        Assert.True(at > 0, "وِلادَةُ المَتجَرِ في بَذرِ المَظهَرِ غائِبَة — الأَداةُ عَمياء.");
        var injected = text[..at] + "global.Store(new Tenant" + text[(at + born.Length)..];
        var counted = CountedTypes(MeterClassification.All);
        var pins = Pinned.Where(p => p.File == file).ToList();
        var real = Breaches(Scan(file, Group.Seeder, text), pins, counted, [], [file]);
        var breaches = Breaches(Scan(file, Group.Seeder, injected), pins, counted, [], [file]);
        output.WriteLine($"[G18·حَقن] الأَصل {real.Count} خَرقاً · المَحقون {breaches.Count}: {string.Join(" | ", breaches)}");
        Assert.Empty(real);
        Assert.Contains(breaches, b => b.StartsWith("store_of_insert_counted:" + file + ":", StringComparison.Ordinal) && b.EndsWith(":Tenant", StringComparison.Ordinal));
        Assert.Equal(["stale_pin", "store_of_insert_counted", "unpinned_write"], breaches.Select(CodeOf).Distinct().Order(StringComparer.Ordinal));
    }

    /// <summary><b>لِكُلِّ رَمزٍ مُدخَلٌ يُنتِجُه وَحدَه</b> (القاعِدَة ٤: مُوجِبٌ وسالِب) — والشَجَرَةُ الحَقيقِيَّةُ (الصَفُّ الأَوَّل) هِيَ المُوجِبُ لِلثَمانِيَة؛
    /// وحارِسُ عَمى: كُلُّ رَمزٍ في <see cref="Codes"/> يُنتِجُه صَفٌّ هُنا.</summary>
    [Fact]
    public void Each_breach_code_is_produced_by_its_own_breaching_input()
    {
        var counted = CountedTypes(MeterClassification.All);
        const string seeder = Seed + "X.cs", script = "scripts/x.cs", importer = "tools/ACommerce.Importer/X.cs";
        IReadOnlyList<Site> S(string file, Group g, string code) => Scan(file, g, code);
        var cases = new (string Code, IReadOnlyList<Site> Sites, IReadOnlyList<Pin> Pins, IReadOnlyList<string> Block)[]
        {
            ("unpinned_write", S(seeder, Group.Seeder, "s.Store(new Plan { Id = \"x\" });"), [], []),
            ("stale_pin", S(seeder, Group.Seeder, "var nothing = 1;"), [new(seeder, "s.Store(new Plan", "Plan", Fate.NotCounted)], []),
            ("type_mismatch", S(seeder, Group.Seeder, "s.Store(new Plan { });"), [new(seeder, "s.Store(new Plan", "Cart", Fate.NotCounted)], []),
            ("store_of_insert_counted", S(seeder, Group.Seeder, "global.Store(new Tenant { Id = \"x\" });"),
                [new(seeder, "global.Store(new Tenant", "Tenant", Fate.SeedBirth)], []),
            ("script_creates_counted", S(script, Group.Script, "s.Store(new Deal { Id = Guid.NewGuid() });"),
                [new(script, "s.Store(new Deal", "Deal", Fate.SeedBirth)], []),
            ("importer_not_excluded", S(importer, Group.Importer, "s.Events.StartStream<Listing>(id, new ListingCreated(id));"),
                [new(importer, "s.Events.StartStream<Listing>(new ListingCreated", "ListingCreated", Fate.UpdateOnly)], []),
            ("fate_contradicts_type", S(seeder, Group.Seeder, "s.Events.Append(id, new ListingCreated(id));"),
                [new(seeder, "s.Events.Append(new ListingCreated", "ListingCreated", Fate.SeedBirth)], []),
            ("seed_block_unscanned", [], [], ["GhostSeeder"]),
        };
        foreach (var (code, sites, pins, block) in cases)
        {
            var breaches = Breaches(sites, pins, counted, block, [seeder, script, importer]);
            output.WriteLine($"[G18·رَمز] {code}: {string.Join(" | ", breaches)}");
            Assert.Equal([code], breaches.Select(CodeOf));
        }
        Assert.Equal(Codes.Order(StringComparer.Ordinal), cases.Select(c => c.Code).Order(StringComparer.Ordinal));
    }

    /// <summary><b>الماسِحُ يُقاسُ قَبلَ أَن يُوثَقَ بِه</b> (القاعِدَة ١٠): يَرى الأَشكالَ الَّتي في المِلَفّات — الوَسيطَ <c>new</c>، والمُتَغَيِّرَ مِن <c>new</c>
    /// و<c>LoadAsync&lt;T&gt;</c> و<c>Query&lt;T&gt;</c>، والحَدَثَ بَعدَ المُعَرِّف، والسَطرَ المُنكَسِر — ولا يَرى التَعليقَ ولا النَصَّ ولا <c>StringBuilder.Append</c>.</summary>
    [Fact]
    public void The_scanner_sees_the_repository_shapes_and_ignores_comments_strings_and_string_builders()
    {
        const string code = """
            var a = new Tenant { Id = "x" };
            s.Insert(a);
            var live = await s.LoadAsync<StudioUser>(id);
            s.Store(live);
            var u = (await s.Query<User>().ToListAsync()).FirstOrDefault();
            s.Store(u!);
            s.Events.StartStream<Listing>(id,
                new ListingCreated(id, "a (b"));
            // s.Store(new Deal { });
            var t = "s.Store(new Deal";
            sb.Append("x");
            s.Store(new ACommerce.Kit.Subscriptions.Plan { Id = "y" });
            """;
        var sites = Scan("f.cs", Group.Seeder, code);
        output.WriteLine("[G18·ماسِح] " + string.Join(" | ", sites.Select(x => $"{x.Line}:{x.Key}→{x.Resolved}")));
        Assert.Equal(["s.Insert(a→Tenant", "s.Store(live→StudioUser", "s.Store(u!→User", "s.Events.StartStream<Listing>(new ListingCreated→ListingCreated", "s.Store(new Plan→Plan"],
                     sites.Select(x => $"{x.Key}→{x.Resolved}"));
        Assert.Equal([2, 4, 6, 7, 12], sites.Select(x => x.Line));
    }
}
