using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>أَنواعُ مَوانِعِ دَمجِ الجَوّالِ المُكَرَّر</b> (‏2026-10-30، الموجَة ١٢ F1 البَند ١١ — A-SESS-3). سكربتُ العَدِّ كانَ يَطبَعُ «مانِعٌ في {جَدوَل}» بِلا نَوعِ
/// المانِع، وتَعليماتُ المالِكِ تَقرَأُ كُلَّ سَطرٍ «يَفصِلُ البائِعُ رَبطَه ثُمَّ يُدمَج» (‏ADR-044 §٤ G-6) — ومَدخَلُ البائِعِ لا بابَ يَفصِلُه، وسائِرُ الأَنواعِ
/// (مُعَرِّفٌ بِلا شَرطات، وصَفٌّ يَجمَعُ الحِسابَين، ومَجرى أَحداث، وحَدَثٌ خارِجَ مَجاري الإعلانات) لا يَرفَعُها البائِعُ أَصلاً.</para>
/// <para>فَصارَ لِكُلِّ سَطرِ مانِعٍ نَوعٌ مِن مَعجَمٍ مُغلَق (<see cref="DuplicatePhones.BlockKinds"/>) يُقرَأُ بِعِبارَتِه الخِتامِيَّة (<see cref="DuplicatePhones.KindOf"/>)،
/// والعَدُّ يَطبَعُ الجَدوَلَ والنَوعَ مَعاً والمَعجَمَ كُلَّه ويَدُلُّ عَلى عَرضِ الدَمج. صَفٌّ لِكُلِّ نَوع، وصَفٌّ يَربِطُ المَعجَمَ بِكُلِّ <c>blocks.Add</c> في
/// <see cref="DuplicatePhones.ScanAsync"/> — فَسَطرٌ ثامِنٌ يُضافُ غَداً بِلا نَوعٍ يَحمَرّ.</para>
/// </summary>
public class DuplicatePhoneBlockKindsTests(ITestOutputHelper output)
{
    private static string Script(string name)
        => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "scripts", name));

    private static string ScanBody()
    {
        var src = StudioConsentCharacterizationTests.Source("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Identity", "DuplicatePhones.cs");
        var start = src.IndexOf("public static async Task<Scan> ScanAsync(", StringComparison.Ordinal);
        var end = src.IndexOf("// ─── الكِتابَة", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "لا جِسمَ لِـScanAsync — الأَداةُ عَمياء.");
        return src[start..end];
    }

    /// <summary>عِباراتُ <c>blocks.Add($"…")</c> في <see cref="DuplicatePhones.ScanAsync"/> — ما بَعدَ أَوَّلِ <c>}</c> مِن العَدَد.</summary>
    private static List<string> AddedLines() => Regex.Matches(ScanBody(), @"blocks\.Add\(\$""(?<line>[^""]*)""\)").Select(m => m.Groups["line"].Value).ToList();

    public static TheoryData<string> Kinds()
    {
        var data = new TheoryData<string>();
        foreach (var k in DuplicatePhones.BlockKinds) data.Add(k.Code);
        return data;
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-30، كوميتُ التَبديل)</b> — كانَ <c>Today_the_count_keys_block_reasons_by_table_alone</c> (‏<c>5ad739d</c>):
    /// «العَدُّ يُفَهرِسُ المَوانِعَ بِالجَدوَلِ وَحدَه True · يَطبَعُ نَوعَ المانِع False»، ومَطبوعُه عَلى قاعِدَةٍ مَحَلِّيَّةٍ بِمَجموعَتَين مَزروعَتَين:
    /// «مانِعٌ في mt_doc_notification: 1» و«مانِعٌ في mt_doc_tenantproviderbinding: 1» بِلا نَوع. والآن «مانِعٌ في {جَدوَل} · {نَوع}» والمَعجَمُ كُلُّه وأَمرُ العَرض.</summary>
    [Fact]
    public void The_count_keys_block_reasons_by_table_and_kind_prints_the_closed_kind_list_and_points_to_the_dry_run()
    {
        var count = Script("count-duplicate-phones.cs");
        var byTable = count.Contains("reasons[b[..b.IndexOf(':')]] = reasons.GetValueOrDefault(b[..b.IndexOf(':')]) + 1;", StringComparison.Ordinal);
        var kind = count.Contains("kinds[(table, DuplicatePhones.KindOf(b))] = kinds.GetValueOrDefault((table, DuplicatePhones.KindOf(b))) + 1;", StringComparison.Ordinal);
        output.WriteLine($"بَعدَ التَبديل: يُفَهرِسُ بِالجَدوَلِ وَحدَه {byTable} · بِالجَدوَلِ والنَوع {kind}");
        Assert.False(byTable);
        Assert.True(kind);
        Assert.DoesNotContain("Console.WriteLine($\"  مانِعٌ في {why}: {n} مَجموعَة\");", count, StringComparison.Ordinal);
        Assert.Contains("Console.WriteLine($\"  مانِعٌ في {table} · {kind}: {n} مَجموعَة\");", count, StringComparison.Ordinal);
        Assert.Contains("foreach (var k in DuplicatePhones.BlockKinds)", count, StringComparison.Ordinal);
        Assert.Contains("k.LiftableToday ?", count, StringComparison.Ordinal);
        Assert.Contains("dotnet run scripts/merge-duplicate-phones.cs   (عَرضٌ بِلا --apply", count, StringComparison.Ordinal);
        // ‏والعَرضُ يُسَمّي كُلَّ مَجموعَةٍ بِسُطورِ مَوانِعِها كامِلَةً — العِبارَةُ الَّتي يَقرَأُها KindOf.
        Assert.Contains("مُمتَنِعَة — {string.Join(\" · \", scan.Blocks)}", Script("merge-duplicate-phones.cs"), StringComparison.Ordinal);
    }

    /// <summary><b>صَفٌّ لِكُلِّ نَوع</b>: سَطرٌ بِعِبارَتِه يُقرَأُ بِرَمزِه وجَدوَلِه، وعِبارَتُه في <c>ScanAsync</c> مَرَّةً واحِدَةً بِالضَبط.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public void Each_block_kind_is_read_back_from_its_scan_line(string code)
    {
        var k = Assert.Single(DuplicatePhones.BlockKinds, x => x.Code == code);
        var table = k.Code == "admin_account" ? DuplicatePhones.AdminBlock : "mt_doc_probe";
        var line = $"{table}: 3 {k.Phrase}";
        var inScan = AddedLines().Count(l => l.EndsWith(" " + k.Phrase, StringComparison.Ordinal));
        output.WriteLine($"{code}: KindOf={DuplicatePhones.KindOf(line)} · TableOf={DuplicatePhones.TableOf(line)} · سُطورُه في ScanAsync={inScan} · يُرفَعُ اليَوم={k.LiftableToday}");
        Assert.Equal(code, DuplicatePhones.KindOf(line));
        Assert.Equal(table, DuplicatePhones.TableOf(line));
        Assert.Equal(1, inScan);
    }

    /// <summary><b>المَعجَمُ مُغلَقٌ عَلى ما يَكتُبُه القِياس</b> (القاعِدَة ١٠ — يَطبَعُ ما فَحَصَه ويَفشَلُ عَلى صِفر): كُلُّ <c>blocks.Add</c> يَنتَهي بِعِبارَةِ نَوعٍ واحِد،
    /// وعَدَدُها عَدَدُ المَعجَم، والرُموزُ والعِباراتُ مُتَفَرِّدَة، ولا عِبارَةَ ذَيلٌ لِأُخرى (فَلا يَلتَبِسُ <c>EndsWith</c>).</summary>
    [Fact]
    public void Every_block_the_scan_writes_ends_with_exactly_one_phrase_of_the_closed_lexicon()
    {
        var lines = AddedLines();
        var kinds = DuplicatePhones.BlockKinds;
        output.WriteLine($"سُطورُ مانِعٍ في ScanAsync: {lines.Count} · أَنواعُ المَعجَم: {kinds.Count}");
        Assert.True(lines.Count > 0, "أَداةٌ عَمياء: صِفرُ blocks.Add.");
        Assert.Equal(kinds.Count, lines.Count);
        foreach (var l in lines)
            Assert.Single(kinds, k => l.EndsWith(k.Phrase, StringComparison.Ordinal));
        Assert.Equal(kinds.Count, kinds.Select(k => k.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(kinds.Count, kinds.Select(k => k.Phrase).Distinct(StringComparer.Ordinal).Count());
        foreach (var a in kinds)
            Assert.DoesNotContain(kinds, b => !ReferenceEquals(a, b) && a.Phrase.EndsWith(b.Phrase, StringComparison.Ordinal));
        Assert.DoesNotContain(kinds, k => k.Code == "unknown");
    }

    /// <summary><b>الصَفُّ السالِب</b>: سَطرٌ خارِجَ المَعجَمِ يُقرَأُ <c>unknown</c> (فَيَطبَعُه العَدُّ «✗ مانِعٌ خارِجَ المَعجَم»)، وسَطرٌ بِلا نُقطَتَينِ جَدوَلُه فارِغ.</summary>
    [Theory]
    [InlineData("mt_doc_probe: 3 صَفّاً لا نَوعَ لَه", "unknown", "mt_doc_probe")]
    [InlineData("mt_doc_probe: 3 صَفّاً يَجمَعُ الحِسابَينِ مَعاً", "unknown", "mt_doc_probe")]
    [InlineData("بِلا جَدوَل", "unknown", "")]
    public void A_line_outside_the_lexicon_reads_unknown(string line, string kind, string table)
    {
        Assert.Equal(kind, DuplicatePhones.KindOf(line));
        Assert.Equal(table, DuplicatePhones.TableOf(line));
    }

    /// <summary><b>لا يُرفَعُ اليَومَ بِبابٍ قائِمٍ إلّا الإداريّ</b> (‏grant-admin/revoke-admin)، وسَطرُه سَطرُ <see cref="DuplicatePhones.AdminBlock"/> نَفسُه.</summary>
    [Fact]
    public void Only_the_admin_kind_is_liftable_today_and_it_is_the_admin_block_line()
    {
        var liftable = Assert.Single(DuplicatePhones.BlockKinds, k => k.LiftableToday);
        Assert.Equal("admin_account", liftable.Code);
        Assert.Contains(AddedLines(), l => l.StartsWith("{AdminBlock}:", StringComparison.Ordinal) && l.EndsWith(liftable.Phrase, StringComparison.Ordinal));
        var entry = Assert.Single(DuplicatePhones.BlockKinds, k => k.Code == "row_id_holds_loser");
        Assert.False(entry.LiftableToday);
    }
}
