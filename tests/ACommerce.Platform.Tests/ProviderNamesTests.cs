using System.Reflection;
using System.Text.RegularExpressions;
using ACommerce.Platform.Providers;
using ACommerce.Platform.Shared.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>بُعدُ المُزَوِّدِ مُغلَق</b> (‏2026-11-13، الموجَة ١٣ W13-6·١٦ — ‏ADR-051 §٢٠؛ التَصميم §7: «The provider dimension is still closed … and a test pins
/// every <c>ProviderCall</c> site to it. The 8 are a shrink-only list»). عَمودُ <c>provider</c> في الدَفتَرِ يَكتُبُه المُنادي الصادِرُ مِن
/// <see cref="ProviderNames"/>؛ وهذا المِلَفُّ يُثَبِّتُ أَنّ تِلكَ الأَسماءَ مَعجَمٌ مُغلَقٌ يُرَدُّ كُلُّ اسمٍ فيه إلى مَصدَرِه، وأَنّ كُلَّ مَوضِعٍ يُسَمّي مُزَوِّداً
/// لِنِداءٍ يُسَمّيه مِنه.</para>
///
/// <para><b>والفَرقُ عَن التَصميمِ يُقالُ ولا يُبتَلَع</b>: التَصميمُ كَتَبَ «سلاجاتُ الكاتالوج ∪ الثَمانِيَة»، والكودُ عِندَ dba78be يَكتُبُ <b>اسمَ العُدَّة</b> لا سلاجَ
/// التَعريف (<c>moyasar</c> لا سلاجُ تَعريفِ المِفتاحِ السِرِّيّ) — لِأَنّ سلاجاً في الكودِ يُحَمِّرُ
/// <c>ProviderCatalogTests.No_source_file_outside_the_catalogue_names_a_provider_slug</c> (البُنودُ ١٣–١٥، و«أ» في سِجِلِّ المُؤَسِّس). فَصارَ الإغلاقُ
/// جَدوَلَين هُنا: <b>ثَماني قَنَواتٍ بِلا مِلَفِّ تَعريف</b> (تَنكَمِشُ ولا تَنمو: قَناةٌ يُكتَبُ لَها تَعريفٌ تَنتَقِلُ إلى الجَدوَلِ الثاني)، و<b>اسمُ عُدَّةٍ لِكُلِّ
/// تَعريفٍ يُغادِرُ نِداؤُه العَمَلِيَّة</b>؛ وكُلُّ تَعريفٍ في الكاتالوجِ إمّا مَردودٌ إلى اسمِ عُدَّتِه أَو مُعلَنٌ بِلا نِداءٍ صادِر.</para>
/// </summary>
public class ProviderNamesTests(ITestOutputHelper output)
{
    /// <summary>القَنَواتُ الصادِرَةُ بِلا مِلَفِّ تَعريفٍ في الكاتالوج — <b>قائِمَةٌ تَنكَمِشُ ولا تَنمو</b> (التَصميم §7)، ولِكُلٍّ سَبَبُه.</summary>
    private static readonly IReadOnlyDictionary<string, string> ChannelsWithoutDefinition = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["twilio"] = "الرَسائِلُ القَصيرَة — اعتِمادُ المَنَصَّةِ في مُتَغَيِّراتِ النَشر، ولا شاشَةَ رَبط",
        ["brevo"] = "البَريدُ عَبرَ الواجِهَة — اعتِمادُ المَنَصَّة",
        ["smtp"] = "البَريدُ عَبرَ خادِمٍ مَضبوط — اعتِمادُ المَنَصَّة",
        ["nafath"] = "التَحَقُّقُ مِن الهُوِيَّة — اعتِمادُ المَنَصَّة",
        ["webpush"] = "إشعارُ المُتَصَفِّح — مَفاتيحُ VAPID لِلمَنَصَّة",
        ["anthropic"] = "خَلفِيَّةُ نَموذَج — مِفتاحُ المَنَصَّة",
        ["gemini"] = "خَلفِيَّةُ نَموذَج — مِفتاحُ المَنَصَّة",
        ["openai_compatible"] = "خَلفِيَّةُ نَموذَجٍ مُتَوافِقَة — مِفتاحُ المَنَصَّة",
    };

    /// <summary>اسمُ العُدَّةِ الَّذي يَكتُبُه المُنادي ← تَعريفُ الكاتالوجِ الَّذي يَخدُمُه نِداؤُه.</summary>
    private static readonly IReadOnlyDictionary<string, string> KitNameOfDefinition = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["s3"] = "s3_object_storage",
        ["moyasar"] = "moyasar_api",
        ["paypal"] = "paypal_subscriptions",
        ["paddle"] = "paddle_billing",
        ["oto"] = "oto_vendor_account",
    };

    /// <summary>تَعريفاتٌ في الكاتالوجِ لا يُغادِرُ لَها نِداءٌ العَمَلِيَّة — ولِكُلٍّ سَبَبُه.</summary>
    private static readonly IReadOnlyDictionary<string, string> DefinitionsWithoutOutboundCall = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["mock_payments"] = "مُحاكٍ داخِلَ العَمَلِيَّة",
        ["local_files"] = "قُرصُ الحاوِيَة داخِلَ العَمَلِيَّة",
        ["moyasar_hosted"] = "رابِطُ دَفعٍ يَلصَقُه التاجِر — لا نِداءَ مِنّا",
    };

    private static IReadOnlyDictionary<string, string> DeclaredNames() =>
        typeof(ProviderNames).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!, StringComparer.Ordinal);

    [Fact]
    public void Every_provider_name_is_a_channel_without_a_definition_or_the_kit_of_a_catalogue_definition()
    {
        var declared = DeclaredNames().Values.ToHashSet(StringComparer.Ordinal);
        var pinned = ChannelsWithoutDefinition.Keys.Concat(KitNameOfDefinition.Keys).ToHashSet(StringComparer.Ordinal);
        var slugs = ProviderCatalog.Definitions.Select(d => d.Slug).ToList();
        output.WriteLine($"[ProviderNames] مُعلَنَة {declared.Count} = قَنَواتٌ بِلا تَعريف {ChannelsWithoutDefinition.Count} + أَسماءُ عُدَدِ تَعريفات {KitNameOfDefinition.Count}" +
                         $" · الكاتالوج {slugs.Count} = بِاسمِ عُدَّة {KitNameOfDefinition.Count} + بِلا نِداءٍ صادِر {DefinitionsWithoutOutboundCall.Count}");
        Assert.True(declared.Count > 0 && slugs.Count > 0, "أَداةٌ عَمياء: صِفرُ اسمٍ أَو تَعريف.");

        var unpinned = declared.Except(pinned).ToList();
        var dead = pinned.Except(declared).ToList();
        Assert.True(unpinned.Count == 0, "اسمُ مُزَوِّدٍ جَديدٌ بِلا صَفٍّ هُنا — يُرَدُّ إلى تَعريفِه أَو يُعلَنُ قَناةً بِسَبَبِها: " + string.Join("، ", unpinned));
        Assert.True(dead.Count == 0, "صَفٌّ لِاسمٍ لَم يَعُد في ProviderNames — يُحذَفُ (القائِمَةُ تَنكَمِش): " + string.Join("، ", dead));

        // الكاتالوجُ مُقَسَّمٌ بِلا بَقِيَّةٍ ولا تَكرار، وقَناةٌ صارَ لَها تَعريفٌ لا تَبقى قَناة.
        var accounted = KitNameOfDefinition.Values.Concat(DefinitionsWithoutOutboundCall.Keys).ToList();
        Assert.Equal(accounted.Count, accounted.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(slugs.Order(StringComparer.Ordinal), accounted.Order(StringComparer.Ordinal));
        Assert.Empty(ChannelsWithoutDefinition.Keys.Intersect(slugs, StringComparer.Ordinal));
        Assert.Empty(ChannelsWithoutDefinition.Keys.Intersect(KitNameOfDefinition.Keys, StringComparer.Ordinal));
    }

    // ─── كُلُّ مَوضِعٍ يُسَمّي مُزَوِّداً لِنِداءٍ يُسَمّيه مِن ProviderNames ───────────────────────

    private static readonly Regex ByCall = new(@"\.By\((?<arg>[^()]*)\)", RegexOptions.Compiled);
    private static readonly Regex HelperNew = new(@"new ProviderHelper\((?<arg>[^,()]*),", RegexOptions.Compiled);
    private static readonly Regex HelperTargetTyped = new(@"\bProviderHelper\s+\w+\s*=\s*new\((?<arg>[^,()]*),", RegexOptions.Compiled);
    private static readonly Regex CallNew = new(@"new ProviderCall\([^,()]*,\s*(?<arg>[^,()]*),", RegexOptions.Compiled);
    private static readonly Regex Named = new(@"^ProviderNames\.(?<member>\w+)$", RegexOptions.Compiled);

    /// <summary>
    /// <para>مَواضِعُ تَسمِيَةِ المُزَوِّدِ في نَصٍّ: <c>call.By(…)</c>، و<c>new ProviderHelper(…, reason)</c> بِشَكلَيه، و<c>new ProviderCall(unit, …)</c> —
    /// ووُسَطاؤُها. وسَطرٌ يَحمِلُ عَلامَةَ مَوضِعٍ ولا يَقرَؤُه النَمَطُ (نِداءٌ مَكسورٌ عَلى سَطرَين) يُعَدُّ «غَيرَ مَقروء» فَيَحمَرّ: الأَداةُ لا تَتَخَطّى ما لا تَفهَم.</para>
    /// </summary>
    internal static IReadOnlyList<(int Line, string Arg)> ProviderArguments(string text, out IReadOnlyList<int> unread)
    {
        var found = new List<(int, string)>();
        var missed = new List<int>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            if (line.StartsWith("//", StringComparison.Ordinal)) continue;
            var marks = Regex.Matches(line, @"\.By\(|new ProviderHelper\(|\bProviderHelper\s+\w+\s*=\s*new\(|new ProviderCall\(").Count;
            var hits = ByCall.Matches(line).Concat(HelperNew.Matches(line)).Concat(HelperTargetTyped.Matches(line)).Concat(CallNew.Matches(line)).ToList();
            foreach (var m in hits) found.Add((i + 1, m.Groups["arg"].Value.Trim()));
            if (hits.Count < marks) missed.Add(i + 1);
        }
        unread = missed;
        return found;
    }

    [Fact]
    public void Every_site_that_names_a_provider_for_a_call_names_it_from_ProviderNames()
    {
        var members = DeclaredNames();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var breaches = new List<string>();
        int scanned = 0, sites = 0, files = 0;
        foreach (var root in new[] { "libs", "apps" })
            foreach (var f in Directory.EnumerateFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, root), "*.cs", SearchOption.AllDirectories))
            {
                var norm = f.Replace('\\', '/');
                if (norm.Contains("/obj/", StringComparison.Ordinal) || norm.Contains("/bin/", StringComparison.Ordinal)) continue;
                scanned++;
                var rel = Path.GetRelativePath(ThemeZeroEquivalenceTests.RepoRoot, f).Replace('\\', '/');
                var args = ProviderArguments(File.ReadAllText(f), out var unread);
                if (args.Count > 0) files++;
                sites += args.Count;
                breaches.AddRange(unread.Select(l => $"{rel}:{l} ← مَوضِعٌ لا يَقرَؤُه الفاحِص"));
                foreach (var (line, arg) in args)
                {
                    var m = Named.Match(arg);
                    if (m.Success && members.ContainsKey(m.Groups["member"].Value)) used.Add(m.Groups["member"].Value);
                    else breaches.Add($"{rel}:{line} ← «{arg}»");
                }
            }

        var unused = members.Keys.Except(used).ToList();
        output.WriteLine($"[ProviderNames] مِلَفّاتٌ مَفحوصَة {scanned} · مَواضِع {sites} في {files} مِلَفّاً · أَسماءٌ مُستَعمَلَة {used.Count} مِن {members.Count} · خُروق {breaches.Count}");
        Assert.True(scanned > 300 && sites > 0, $"أَداةٌ عَمياء: {scanned} مِلَفّاً و{sites} مَوضِعاً.");
        Assert.True(breaches.Count == 0, "مَوضِعٌ يُسَمّي مُزَوِّداً لِنِداءٍ مِن خارِجِ ProviderNames:\n  " + string.Join("\n  ", breaches));
        Assert.True(unused.Count == 0, "اسمٌ في ProviderNames بِلا مَوضِعٍ يَكتُبُه (القاعِدَة ١) — يُحذَفُ أَو يُوصَل: " + string.Join("، ", unused));
    }

    /// <summary><b>الفاحِصُ يُقاسُ قَبلَ أَن يُوثَقَ بِه</b> (القاعِدَة ١٠): نَصٌّ حَرفِيّ، ومُتَغَيِّر، وعُضوٌ غَيرُ مُعلَن، ونِداءٌ مَكسورٌ عَلى سَطرَين — كُلٌّ يُمسَك؛
    /// والشَكلُ الحَقيقيُّ بِأَنواعِه الأَربَعَةِ يَمُرّ.</summary>
    [Fact]
    public void The_site_scanner_catches_each_injected_shape()
    {
        const string injected = """
            var a = call.By("twilio");
            var b = new ProviderHelper(name, ProviderHelperReasons.OauthToken);
            var c = new ProviderCall(OutboundUnits.PushSend, ProviderNames.Pigeon, slug, id);
            var d = call.By(
                ProviderNames.Twilio);
            // var e = call.By("ignored in a comment");
            private static readonly ProviderHelper Token = new(ProviderNames.Nafath, ProviderHelperReasons.OauthToken);
            using var resp = await _http.SendAsync(call.By(ProviderNames.Twilio), request, ct);
            """;
        var args = ProviderArguments(injected, out var unread);
        var members = DeclaredNames();
        var bad = args.Where(a => Named.Match(a.Arg) is not { Success: true } m || !members.ContainsKey(m.Groups["member"].Value)).Select(a => a.Arg).ToList();
        output.WriteLine($"[ProviderNames] الحَقن: مَواضِع {args.Count} · خُروق {string.Join(" | ", bad)} · غَيرُ مَقروءَة عِندَ الأَسطُر {string.Join(",", unread)}");
        Assert.Equal(new[] { "\"twilio\"", "name", "ProviderNames.Pigeon" }, bad);
        Assert.Equal(new[] { 4 }, unread);
        Assert.Equal(5, args.Count);
    }
}
