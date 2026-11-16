using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G14 — القِراءَةُ العابِرَةُ لِلمَتاجِرِ في مِلَفّاتٍ مُثَبَّتَةٍ بِعَدَدِها، ومُطابَقَةُ العَدِّ مِنها (‏2026-11-13، الموجَة ١٣ W13-6·٢٠ — ADR-051 §٢٤؛ التَصميم §9 و§12 G14) ═══
//
// **ما يَحرُسُه**: التَصميمُ يَقول «Cross-tenant reads happen here only (G14 pins the file list)». والكودُ عِندَ الرَأسِ يَقولُ أَنّ «هُنا وَحدَها» لَم تَكُن صادِقَةً قَبلَ
// المُطابَقَة: قارِئٌ خامٌّ عَلى جَداوِلِ Marten يَتَجاوَزُ عَزلَ المُستَأجِرِ في تِسعَةِ مِلَفّاتٍ قائِمَة (دَمجُ الهَواتِفِ المُكَرَّرَة، وبَصمَةُ البَذر، وإعادَةُ المُستَورِد،
// وسكربتاتُ التَشغيل) — مَقيسٌ بِهذا الماسِحِ نَفسِه. فَالحارِسُ لا يَدَّعي «مِلَفٌّ واحِد»: يُثَبِّتُ **كُلَّ** مِلَفٍّ يَقرَأُ عَبرَ المَتاجِرِ بِعَدَدِ مَواضِعِه وسَبَبِه،
// والقائِمَةُ تَتَقَلَّصُ ولا تَنمو إلّا بِقَرارٍ مَرئِيٍّ في مُراجَعَة (القاعِدَة ١ — نَسَقُ قائِمَةِ اليَتامى).
//
// **ما يُعَدُّ قِراءَةً عابِرَة** (بَعدَ نَزعِ التَعليقاتِ وإبقاءِ النُصوص — الجُملَةُ الخامُّ نَصّ):
//   ١. مُعامِلاتُ Marten العابِرَة: `AnyTenant(` و`TenantIsOneOf(` و`QueryAllRawEvents(`؛
//   ٢. جُملَةٌ خامٌّ عَلى جَداوِلِه بِالاسم: `mt_doc_` و`mt_events` و`mt_streams`؛
//   ٣. اسمُ جَدوَلٍ مِن تَعيينِه لِجُملَةٍ خامّ: `FindOrResolveDocumentType(` و`MappingFor(` و`AllKnownEventTypes(`.
// والجَلسَةُ لِكُلِّ مَتجَرٍ في حَلقَةٍ عَلى المَتاجِرِ (`PresenceReader`) لا يَراها نَمَطٌ نَصِّيّ — فَمِلَفُّها مُثَبَّتٌ بِاسمِه هُنا (‏ADR-051 §٢٣ وَعَدَ بِه).
//
// **الحَدُّ يُقاسُ قَبلَ أَن يُوثَقَ بِه** (القاعِدَة ١٠): يَطبَعُ المِلَفّاتِ المَمسوحَةَ والمَواضِعَ لِكُلِّ نَوعٍ ولِكُلِّ مِلَفّ، ويَفشَلُ عَلى صِفرِ مِلَفٍّ أَو صِفرِ مَوضِعٍ في
// قارِئِ المُطابَقَة؛ وكُلُّ رَمزِ خَرقٍ يُنتِجُه عَيبٌ مَحقونٌ في نُسخَةٍ مِن مِلَفٍّ حَقيقِيّ — ومِنها `AnyTenant()` خارِجَ القائِمَة.
public class MeterCrossTenantReadersTests(ITestOutputHelper output)
{
    private static string Root => ThemeZeroEquivalenceTests.RepoRoot;

    /// <summary>المِلَفُّ الَّذي تَقرَأُ مِنه المُطابَقَةُ عَبرَ المَتاجِر — وصِفرُ مَوضِعٍ فيه أَداةٌ عَمياء.</summary>
    internal const string ReconcileReaders = "libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Metering/MeterSourceReaders.cs";

    /// <summary>رُموزُ الخَرق — مُعجَمٌ مُغلَق.</summary>
    internal static readonly IReadOnlyList<string> Codes = ["cross_tenant_read_unpinned", "cross_tenant_read_count_changed"];

    /// <summary>أَنواعُ القِراءَةِ العابِرَة — بِاسمٍ يُطبَع.</summary>
    internal static readonly IReadOnlyList<(string Kind, Regex Pattern)> Kinds =
    [
        ("marten_cross_tenant", new Regex(@"\b(?:AnyTenant|TenantIsOneOf|QueryAllRawEvents)\s*\(", RegexOptions.CultureInvariant)),
        ("raw_marten_table", new Regex(@"\b(?:mt_doc_|mt_events\b|mt_streams\b)", RegexOptions.CultureInvariant)),
        ("marten_table_name", new Regex(@"\b(?:FindOrResolveDocumentType|MappingFor|AllKnownEventTypes)\s*\(", RegexOptions.CultureInvariant)),
    ];

    /// <summary>
    /// <para><b>القائِمَةُ المُثَبَّتَة — مِلَفٌّ ← عَدَدُ مَواضِعِه وسَبَبُه</b> (مَقيسَةٌ عِندَ إيداعِ هذا الحارِس). العَدَدُ بِالضَبط: زِيادَتُه مَوضِعٌ جَديدٌ لَم يُقرَّر،
    /// ونُقصانُه يُنزِلُ الرَقَمَ هُنا في الكوميتِ نَفسِه (تَتَقَلَّص)، وصِفرُه دَبوسٌ مَيِّتٌ يُحذَف.</para>
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, (int Count, string Why)> Pinned = new Dictionary<string, (int, string)>(StringComparer.Ordinal)
    {
        [ReconcileReaders] = (4, "المُطابَقَة (W13-6·٢٠): مَصادِرُ كُلِّ وَحدَةٍ ومَتاجِرُ المُستَورِد — قِراءَةٌ فَقَط في لَقطَةٍ واحِدَة"),
        ["libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Metering/PresenceReader.cs"] =
            (1, "لَقطَةُ الحُضور (W13-6·١٩): جَلسَةٌ لِكُلِّ مَتجَرٍ عَلى المَتاجِرِ كُلِّها، واسمُ الجَدوَلِ لِسُؤالِ to_regclass"),
        ["libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Metering/MeterClassification.cs"] =
            (1, "نَصُّ سَبَبٍ في دَفتَرِ التَصنيف يُسَمّي mt_events — لا جُملَة"),
        ["libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Identity/DuplicatePhones.cs"] =
            (25, "دَمجُ الهَواتِفِ المُكَرَّرَة: جُمَلٌ خامٌّ مَحصورَةٌ بِمُستَأجِرٍ مُسَمّى، وحَصرُ الجَداوِلِ ذاتِ tenant_id مِن information_schema"),
        ["apps/V1.App/Seed/AppearanceBaselineSeeder.cs"] = (3, "بَصمَةُ البَذرِ قَبلَه وبَعدَه: عَدُّ صُفوفِ جَداوِلِ Marten لِكُلِّ مُستَأجِر"),
        ["tools/ACommerce.Importer/TargetWriter.cs"] = (2, "إعادَةُ الاستيراد: حَذفُ أَحداثِ المُستَأجِرِ ومَجاريه (TargetWriter.cs:96-97)"),
        ["scripts/bench-search.cs"] = (15, "قِياسُ البَحثِ عَلى نُسخَةٍ مِن mt_doc_listing في مُخَطَّطٍ مُؤَقَّت"),
        ["scripts/repair-studio-tier.cs"] = (1, "سكربتُ إصلاحٍ يُعرَضُ ثُمَّ يُطَبَّقُ بِحُضورِ المالِك"),
        ["scripts/repair-commercial-categories.cs"] = (2, "سكربتُ إصلاحٍ يُعرَضُ ثُمَّ يُطَبَّقُ بِحُضورِ المالِك"),
        ["scripts/repair-role-floor.cs"] = (4, "سكربتُ إصلاحٍ يُعرَضُ ثُمَّ يُطَبَّقُ بِحُضورِ المالِك"),
        ["scripts/repair-plan-day-end.cs"] = (2, "سكربتُ إصلاحٍ يُعرَضُ ثُمَّ يُطَبَّقُ بِحُضورِ المالِك"),
    };

    /// <summary>مَواضِعُ القِراءَةِ العابِرَةِ في نَصِّ مِلَفٍّ — لِكُلِّ نَوع، بَعدَ نَزعِ تَعليقاتِ Razor وC# (والنُصوصُ باقِيَة).</summary>
    internal static IReadOnlyDictionary<string, int> Count(string path, string text)
    {
        var code = WriteEndpointGuardTests.StripComments(path.EndsWith(".razor", StringComparison.Ordinal) ? WriteEndpointGuardTests.StripMarkupComments(text) : text);
        return Kinds.ToDictionary(k => k.Kind, k => k.Pattern.Matches(code).Count, StringComparer.Ordinal);
    }

    /// <summary>كُلُّ مِلَفٍّ مَمسوح: <c>libs/</c> و<c>apps/</c> و<c>tools/</c> و<c>scripts/</c> (‏<c>*.cs</c> و<c>*.razor</c>)، بِلا <c>bin/</c> و<c>obj/</c>.</summary>
    internal static IReadOnlyList<(string Path, string Text)> Files()
    {
        var files = new List<(string, string)>();
        foreach (var dir in new[] { "libs", "apps", "tools", "scripts" })
            foreach (var pattern in new[] { "*.cs", "*.razor" })
                foreach (var f in Directory.EnumerateFiles(Path.Combine(Root, dir), pattern, SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(Root, f).Replace('\\', '/');
                    if (rel.Contains("/bin/", StringComparison.Ordinal) || rel.Contains("/obj/", StringComparison.Ordinal)) continue;
                    files.Add((rel, File.ReadAllText(f)));
                }
        return [.. files.OrderBy(f => f.Item1, StringComparer.Ordinal)];
    }

    /// <summary>الخُروقُ مَرَّةً واحِدَةً لِكُلِّ مِلَفّ: <c>{code}:{path}:{measured}</c>.</summary>
    internal static IReadOnlyList<string> Breaches(IEnumerable<(string Path, string Text)> files)
    {
        var breaches = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, text) in files)
        {
            var total = Count(path, text).Values.Sum();
            seen.Add(path);
            if (Pinned.TryGetValue(path, out var pin))
            {
                if (total != pin.Count) breaches.Add($"cross_tenant_read_count_changed:{path}:{total}≠{pin.Count}");
            }
            else if (total > 0) breaches.Add($"cross_tenant_read_unpinned:{path}:{total}");
        }
        foreach (var path in Pinned.Keys.Where(p => !seen.Contains(p)))
            breaches.Add($"cross_tenant_read_count_changed:{path}:absent≠{Pinned[path].Count}");
        return breaches;
    }

    [Fact]
    public void Cross_tenant_reads_live_only_in_the_pinned_files_with_their_counts_and_the_reconciliation_reads_from_its_own()
    {
        var files = Files();
        var perKind = Kinds.ToDictionary(k => k.Kind, _ => 0, StringComparer.Ordinal);
        foreach (var (path, text) in files)
            foreach (var (kind, n) in Count(path, text)) perKind[kind] += n;
        var reconcile = Count(ReconcileReaders, File.ReadAllText(Path.Combine(Root, ReconcileReaders)));
        var breaches = Breaches(files);

        output.WriteLine($"[G14] مِلَفّاتٌ مَمسوحَة {files.Count} · مَواضِع: {string.Join(" · ", perKind.Select(k => $"{k.Key} {k.Value}"))} · مِلَفّاتٌ مُثَبَّتَة {Pinned.Count}");
        foreach (var (path, pin) in Pinned) output.WriteLine($"[G14]   {path} {pin.Count} — {pin.Why}");
        output.WriteLine($"[G14] قارِئُ المُطابَقَة: {string.Join(" · ", reconcile.Select(k => $"{k.Key} {k.Value}"))} · خُروق {breaches.Count}"
                         + (breaches.Count == 0 ? "" : ": " + string.Join(", ", breaches)));

        Assert.True(files.Count >= 300, $"أَداةٌ عَمياء: {files.Count} مِلَفّاً مَمسوحاً.");
        Assert.True(reconcile.Values.Sum() > 0, "أَداةٌ عَمياء: صِفرُ قِراءَةٍ عابِرَةٍ في قارِئِ المُطابَقَة — المُطابَقَةُ لا تَقرَأُ مِن مِلَفِّها.");
        Assert.True(reconcile["raw_marten_table"] + reconcile["marten_table_name"] > 0, "المُطابَقَةُ لا تَقرَأُ جَداوِلَ Marten.");
        Assert.Empty(breaches);
    }

    /// <summary><b>والحارِسُ يُقاسُ بِعَيبٍ مَحقون</b>: كُلُّ رَمزٍ يُنتِجُه حَقنٌ في نُسخَةٍ مِن مِلَفٍّ حَقيقِيّ — <c>AnyTenant()</c> في مِلَفٍّ خارِجَ القائِمَة، وجُملَةٌ
    /// خامٌّ في صَفحَةِ مَتجَر، ومَوضِعٌ زائِدٌ في مِلَفٍّ مُثَبَّت، ومِلَفٌّ مُثَبَّتٌ صارَ بِلا مَوضِع.</summary>
    [Fact]
    public void Each_breach_code_is_produced_by_an_injected_copy_of_a_real_file()
    {
        var files = Files().ToList();
        var deals = files.Single(f => f.Path.EndsWith("Services/Deals/DealsService.cs", StringComparison.Ordinal));
        var page = files.Single(f => f.Path.EndsWith("Pages/Admin/MeterStore.razor", StringComparison.Ordinal));
        var reader = files.Single(f => f.Path == ReconcileReaders);

        List<(string Path, string Text)> With((string Path, string Text) replaced)
            => [.. files.Select(f => f.Path == replaced.Path ? replaced : f)];

        var injections = new (string Code, IReadOnlyList<(string, string)> Files)[]
        {
            ("cross_tenant_read_unpinned", With((deals.Path, deals.Text + "\nstatic class Injected { static object Q(Marten.IQuerySession s) => s.Query<object>().Where(x => x.AnyTenant()); }"))),
            ("cross_tenant_read_unpinned", With((page.Path, page.Text + "\n@code { const string Sql = \"select tenant_id from platform.mt_doc_deal\"; }"))),
            ("cross_tenant_read_count_changed", With((reader.Path, reader.Text + "\nstatic class Injected { const string Sql = \"select count(*) from platform.mt_events\"; }"))),
            ("cross_tenant_read_count_changed", With((reader.Path, "namespace Emptied;"))),
        };
        var produced = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (code, injected) in injections)
        {
            var got = Breaches(injected);
            output.WriteLine($"[G14] حَقنُ {code} ⇐ {string.Join(", ", got)}");
            Assert.Contains(got, b => b.StartsWith(code + ":", StringComparison.Ordinal));
            produced.Add(code);
        }
        // ‏والتَعليقُ لا يُعَدّ: `AnyTenant()` في تَعليقٍ لَيسَ قِراءَة.
        Assert.Empty(Breaches(With((deals.Path, deals.Text + "\n// s.Query<Deal>().Where(x => x.AnyTenant())\n"))));
        output.WriteLine($"[G14] رُموزٌ مُعلَنَة {Codes.Count} · أَنتَجَها حَقنٌ {produced.Count}");
        Assert.Equal(Codes.Order(StringComparer.Ordinal), produced);
    }
}
