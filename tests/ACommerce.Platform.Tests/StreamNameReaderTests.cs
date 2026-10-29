using System.Reflection;
using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مَن يَقرَأُ اسماً مِن مَجرى أَحداثٍ لا يَمحوه الحَذف — مُراجَعَةُ تَيّارِ S2 الجَولَة ٢ (‏2026-10-29، حاجِبَةٌ وكُبرى) ═══
//
// **المَقيس** (عِندَ `5c6bede`، وحَيّاً عِندَ المُراجِعِ عَلى Postgres مَحَلِّيّ): `AccountErasure.EraseCopiesAsync` يَكتُبُ «حِسابٌ مَحذوف» عَلى **لَقطَةِ**
// التَذكِرَةِ والعَرض (وَثيقَتا الإسقاطِ المُباشِر `Snapshot<Ticket>`/`Snapshot<Offer>` في `HostingExtensions`) ولا يَمَسُّ مَجرى أَحداثِهِما — والنُصوصُ
// الخَمسَةُ تَقولُ إنّ المَجرى «لا تَعرِضُه شاشَة». **ونِداءانِ يَبنِيانِ مِن المَجرى ويُسَلِّمانِ الاسم**: (١) `TenantAdminQueries.TicketAsync` يُعيدُ
// `AggregateStreamAsync<Ticket>` كامِلاً إلى `StudioAppTicketDetail.razor` فَيَرسُمُ صاحِبُ المَتجَرِ اسمَ العُضوِ المَحذوفِ خَمسَ مَرّات؛ (٢) قَبولُ العَرضِ
// (`POST /{slug}/offers/{id}/accept`) يَبنيه مِن مَجراه ويَنسَخُ `OffererName` إلى `ListingMatch` ومُحادَثَةٍ جَديدَتَين — نُسَخٌ تُعَدَّلُ وُلِدَت بَعدَ الحَذف.
//
// **الماسِح** (القاعِدَة ٢ — الحَدُّ الَّذي لا يُقاسُ يَنهار): كُلُّ `AggregateStreamAsync<T>` في `libs`/`apps` (بِلا تَعليقات) لِنَوعٍ يَحمِلُ خاصِّيَّةَ اسمٍ
// يَمحوها الحَذفُ (`NameKind.Renamed` في `AccountDeletionRetentionTests.PersonNameFields`، أَو مَجموعَةً مِن نَوعٍ يَحمِلُها كَـ`Ticket.Replies`)، **إلّا**
// نَوعاً يَحمِلُ المَحوُ نَفسُه إلى مَجراه (لَه `Apply(E)` و`AccountErasure` يُنشِئُ `E`). ونَتيجَتُه مُخالَفَةٌ حينَ تُعادُ (`return await …`) أَو تُسَلَّمُ
// كامِلَة، أَو تُقرَأُ مِنها خاصِّيَّةُ اسم. ويَطبَعُ ما عَدَّه ويَفشَلُ إن كانَ صِفراً (القاعِدَة ١٠)، ولِكُلِّ شَكلٍ عَيِّنَةٌ مَحقونَة.
//
// **يُثَبَّتُ قَبلَ التَبديلِ** (القاعِدَة ٣): الصَفّانِ `Today_…` أَدناه يَنقَلِبانِ بِاسمَيهِما — كُلٌّ في كوميتِ إصلاحِه.
public class StreamNameReaderTests(ITestOutputHelper output)
{
    internal sealed record Site(string File, int Line, string Type, bool CarriesErasableName, bool StreamCarriesErasure);

    internal sealed record Violation(string File, int Line, string Type, string How);

    private static readonly Regex StreamRead = new(
        @"(?:\bvar\s+(?<v>\w+)\s*=\s*|(?<ret>\breturn)\s+)?await\s+[\w.]*?Events\s*\.\s*AggregateStreamAsync\s*<\s*(?<t>[A-Za-z_][\w.]*)\s*>\s*\(",
        RegexOptions.Compiled);

    // نِهايَةُ نِطاقِ المُتَغَيِّر: النُقطَةُ التالِيَةُ أَو الدالَّةُ التالِيَة — ما بَعدَها نِطاقٌ آخَر.
    private static readonly Regex ScopeEnd = new(@"\bapp\s*\.\s*Map\w*\s*\(|\n[ \t]*(?:public|private|internal|protected)\s", RegexOptions.Compiled);

    private static IReadOnlyList<Type>? _types;

    /// <summary>كُلُّ نَوعٍ مِن تَجميعاتِ <c>ACommerce</c> بِجِوارِ الاختِبار — نَمَطُ <c>AccountDeletionRetentionTests.StoredNameFields</c>.</summary>
    private static IReadOnlyList<Type> Types()
    {
        if (_types is not null) return _types;
        var types = new List<Type>();
        foreach (var dll in Directory.EnumerateFiles(AppContext.BaseDirectory, "ACommerce*.dll"))
        {
            try { types.AddRange(Assembly.LoadFrom(dll).GetTypes()); }
            catch (ReflectionTypeLoadException ex) { types.AddRange(ex.Types.OfType<Type>()); }
            catch (BadImageFormatException) { }
        }
        return _types = types.Where(t => !t.IsGenericTypeDefinition && !t.Name.Contains('<')).ToList();
    }

    private static Type? Resolve(string raw)
    {
        var hits = Types().Where(t => t.Name == raw.Split('.')[^1]).ToList();
        if (raw.Contains('.')) hits = hits.Where(h => (h.FullName ?? "").EndsWith(raw, StringComparison.Ordinal)).ToList();
        return hits.FirstOrDefault();
    }

    private static bool HasRenamedField(Type t)
        => AccountDeletionRetentionTests.PersonNameFields.Any(kv => kv.Value.Kind == AccountDeletionRetentionTests.NameKind.Renamed
                                                                 && kv.Key.StartsWith(t.Name + ".", StringComparison.Ordinal));

    /// <summary>خَصائِصُ <paramref name="t"/> الَّتي تَحمِلُ اسماً يَمحوه الحَذف: اسمٌ مُصَنَّفٌ <c>Renamed</c>، أَو مَجموعَةٌ مِن نَوعٍ فيه مِثلُه.</summary>
    internal static IReadOnlySet<string> NameBearing(Type t)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (AccountDeletionRetentionTests.PersonNameFields.TryGetValue($"{t.Name}.{p.Name}", out var f)
                && f.Kind == AccountDeletionRetentionTests.NameKind.Renamed) set.Add(p.Name);
            var inner = p.PropertyType.IsArray ? new[] { p.PropertyType.GetElementType()! }
                      : p.PropertyType.IsGenericType ? p.PropertyType.GetGenericArguments() : Array.Empty<Type>();
            if (inner.Any(HasRenamedField)) set.Add(p.Name);
        }
        return set;
    }

    /// <summary>هَل يَحمِلُ المَحوُ نَفسُه إلى مَجرى <paramref name="t"/>؟ — لَه <c>Apply(E)</c> و<paramref name="erasureCode"/> يُنشِئُ <c>E</c>.</summary>
    internal static bool StreamCarriesErasure(Type t, string erasureCode)
        => t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "Apply" && m.GetParameters().Length == 1)
            .Select(m => m.GetParameters()[0].ParameterType.Name)
            .Any(e => Regex.IsMatch(erasureCode, $@"\bnew\s+(?:[\w.]+\.)?{Regex.Escape(e)}\s*\("));

    /// <summary>مَسحُ نُصوصٍ (بِلا تَعليقات): كُلُّ مَوضِعٍ، وما يُسَلِّمُ مِنها اسماً لا يَمحوه مَجراه.</summary>
    internal static (IReadOnlyList<Site> Sites, IReadOnlyList<Violation> Violations) Scan(
        IEnumerable<(string File, string Code)> files, string erasureCode)
    {
        var sites = new List<Site>();
        var violations = new List<Violation>();
        foreach (var (file, code) in files)
        {
            int LineOf(int at) => code.Take(at).Count(c => c == '\n') + 1;
            foreach (Match m in StreamRead.Matches(code))
            {
                var type = Resolve(m.Groups["t"].Value);
                var bearing = type is null ? new HashSet<string>() : NameBearing(type);
                var safe = type is not null && StreamCarriesErasure(type, erasureCode);
                var name = type?.Name ?? m.Groups["t"].Value;
                sites.Add(new Site(file, LineOf(m.Index), name, bearing.Count > 0, safe));
                if (bearing.Count == 0 || safe) continue;

                if (m.Groups["ret"].Success || !m.Groups["v"].Success)
                {
                    violations.Add(new Violation(file, LineOf(m.Index), name, "returned"));
                    continue;
                }
                var v = m.Groups["v"].Value;
                var from = m.Index + m.Length;
                var end = ScopeEnd.Match(code, from) is { Success: true } e ? e.Index : code.Length;
                var scope = code[from..end];
                // قِراءَةُ خاصِّيَّة (‏`v.F` و`v?.F` و`v!.F`)، أَو تَسليمُه كامِلاً (يَليه ما يُنهي تَعبيراً: ‏`)` `,` `;` `}` `]`) — وما سِواهُما
                // (‏`v is null`، و`"offer-{id}"` نَصّاً) لا يُسَلِّمُ شَيئاً.
                foreach (Match u in Regex.Matches(scope, $@"(?<![\w.]){Regex.Escape(v)}\b(?:(?<acc>\s*!?\s*(?:\?\s*)?\.\s*(?<f>\w+))|(?<whole>\s*[),;}}\]]))?"))
                {
                    if (u.Groups["whole"].Success)
                        violations.Add(new Violation(file, LineOf(from + u.Index), name, $"handed {v}"));
                    else if (u.Groups["f"].Success && bearing.Contains(u.Groups["f"].Value))
                        violations.Add(new Violation(file, LineOf(from + u.Index), name, $"reads {v}.{u.Groups["f"].Value}"));
                }
            }
        }
        return (sites, violations);
    }

    internal static string ErasureCode()
        => WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Identity", "AccountErasure.cs")));

    internal static (IReadOnlyList<Site> Sites, IReadOnlyList<Violation> Violations) ScanRepository()
        => Scan(EntitlementContractTests.SourceFiles().Select(f => (Path.GetFileName(f.File), WriteEndpointGuardTests.StripComments(f.Text))), ErasureCode());

    private void Print(IReadOnlyList<Site> sites, IReadOnlyList<Violation> violations)
    {
        output.WriteLine($"مَواضِعُ AggregateStreamAsync: {sites.Count} · لِنَوعٍ يَحمِلُ اسماً يُمحى: {sites.Count(s => s.CarriesErasableName)} · " +
                         $"مَجراه يَحمِلُ المَحو: {sites.Count(s => s.CarriesErasableName && s.StreamCarriesErasure)} · تُسَلِّمُ الاسم: {violations.Count}");
        foreach (var v in violations) output.WriteLine($"  {v.File}:{v.Line} — {v.Type} — {v.How}");
    }

    /// <summary><b>«اليَوم»</b>: صَفحَةُ تَذكِرَةِ الاستوديو تَأخُذُ التَذكِرَةَ مَبنِيَّةً مِن مَجراها — فَيَعودُ اسمُ كاتِبِها المَحذوفِ إلى صاحِبِ المَتجَر.</summary>
    [Fact]
    public void Today_the_studio_ticket_detail_returns_the_ticket_rebuilt_from_its_stream_with_the_authors_name()
    {
        var (sites, violations) = ScanRepository();
        Print(sites, violations);
        Assert.True(sites.Count > 0, "أَداةٌ عَمياء: صِفرُ مَوضِعٍ لِـAggregateStreamAsync.");
        Assert.Contains(violations, v => v.File == "TenantAdminQueries.cs" && v.Type == "Ticket" && v.How == "returned");
    }

    /// <summary><b>«اليَوم»</b>: قَبولُ العَرضِ يَبنيه مِن مَجراه ويَقرَأُ <c>OffererName</c> — فَيَنسَخُ الاسمَ المَحذوفَ إلى مُطابَقَةٍ ومُحادَثَةٍ جَديدَتَين.</summary>
    [Fact]
    public void Today_accepting_an_offer_copies_the_offerers_name_from_the_offer_stream()
    {
        var (sites, violations) = ScanRepository();
        Print(sites, violations);
        Assert.True(sites.Count > 0, "أَداةٌ عَمياء: صِفرُ مَوضِعٍ لِـAggregateStreamAsync.");
        Assert.Contains(violations, v => v.File == "MarketplaceTemplateExtensions.cs" && v.Type == "Offer" && v.How == "reads offer.OffererName");
    }

    /// <summary>عَيِّناتٌ مَحقونَة (القاعِدَة ١٠): كُلُّ شَكلٍ يَراه الماسِحُ بِمُخالَفَتِه أَو بِغِيابِها — والتَذكِرَةُ مِثالُ نَوعٍ لا يَحمِلُ مَجراه المَحو.</summary>
    [Theory]
    [InlineData("return await s.Events.AggregateStreamAsync<ACommerce.Kit.Support.Ticket>(id, token: ct);", "returned")]
    [InlineData("var t = await s.Events.AggregateStreamAsync<ACommerce.Kit.Support.Ticket>(id);\nShow(t.AuthorName);", "reads t.AuthorName")]
    [InlineData("var t = await s.Events.AggregateStreamAsync<ACommerce.Kit.Support.Ticket>(id);\nvar rs = t!.Replies;", "reads t.Replies")]
    [InlineData("var t = await s.Events.AggregateStreamAsync<ACommerce.Kit.Support.Ticket>(id);\nreturn Results.Ok(t);", "handed t")]
    [InlineData("var t = await s.Events.AggregateStreamAsync<ACommerce.Kit.Support.Ticket>(id);\nif (t is null) return;\nvar s2 = t?.Subject;", null)]
    [InlineData("var l = await s.Events.AggregateStreamAsync<ACommerce.Kit.Listings.Listing>(id);\nreturn Results.Ok(l);", null)]
    public void The_scanner_sees_each_shape_of_a_stream_read(string code, string? expected)
    {
        var (sites, violations) = Scan(new[] { ("sample.cs", code) }, ErasureCode());
        output.WriteLine($"مَواضِع: {sites.Count} · مُخالَفات: {string.Join(" | ", violations.Select(v => v.How))}");
        Assert.Single(sites);
        if (expected is null) Assert.Empty(violations);
        else Assert.Equal(expected, Assert.Single(violations).How);
    }
}
