using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.DuplicatePhonesRenameStalePlanTests;

namespace ACommerce.Platform.Tests;

// ═══ إعادَةُ كَتبِ الجَوّالِ تَسأَلُ الخُطَّةَ عَلى كُلِّ قِراءَة — بَعدَ التَبديل (‏2026-11-05، الموجَة ١٢ G2، الجَولَةُ ٢ — G2R2-1) ═══════════════════════════
//
// قُلِبَ ما طَبَعَه التَوصيف (`DuplicatePhonesRenameStalePlanTests.cs`، `050f739`، والمِلَفُّ بِبَصمَتِه): «أُعيدَ 1 · الجَوّال مُوَحَّدُ الخُطَّة» لِحِسابٍ مُحِيَ بَينَ القِراءَةِ
// والحِفظ، ولِحِسابٍ مُحِيَ قَبلَ `RenameAsync`، ولِحِسابٍ كَتَبَ فيه آخَرُ جَوّالاً جَديداً. والآن `RenameOnceAsync` يَسأَلُ `DuplicatePhones.StillApplies` عَلى كُلِّ قِراءَةٍ
// قَبلَ `setPhone`: الجَوّالُ المُخَزَّنُ صيغَتُه `To` نَفسُها، و`User` غَيرُ مَحذوف — وما سِوى ذلِكَ يُتَخَطّى ولا يُعَدّ، والسكربتُ يَطبَعُ ما تُخُطِّي.
// وما بَقِيَ مِن `2158fed` بِعَينِه: كاتِبٌ لا يَمَسُّ الجَوّالَ (إصدارُ الجَلسَة) يُعادُ ويَبقى (`DuplicatePhonesRenameSwitchTests`).

public class DuplicatePhonesRenameStalePlanSwitchTests(ITestOutputHelper output)
{
    private static readonly DuplicatePhones.Rename Plan = new(Guid.Empty, To);

    /// <summary>صُفوفُ الشَرطِ النَقِيّ: لِكُلِّ شَرطٍ مُوجَبٌ وسالِب — الجَوّالُ في الخُطَّة، وصيغَتُه <c>To</c>، و<c>DeletedAt</c> لِـ<c>User</c> وَحدَه.</summary>
    public static TheoryData<string, object, bool> Rows() => new()
    {
        { "User · الجَوّالُ كَما قُرِئ", new User { Phone = Typed }, true },
        { "User · الجَوّالُ مُوَحَّداً (الصيغَةُ نَفسُها)", new User { Phone = To }, true },
        { "User · DeletedAt والجَوّالُ باقٍ", new User { Phone = Typed, DeletedAt = At }, false },
        { "User · مَمحُوّ (Erase)", AccountDeletion.Erase(new User { Phone = Typed }, At), false },
        { "User · جَوّالٌ آخَر", new User { Phone = Other }, false },
        { "User · حِسابُ نَفاذ (NID-)", new User { Phone = "NID-1000000001" }, false },
        { "StudioUser · الجَوّالُ كَما قُرِئ", new StudioUser { Phone = Typed }, true },
        { "StudioUser · جَوّالٌ آخَر", new StudioUser { Phone = Other }, false },
        { "StudioUser · فارِغ", new StudioUser { Phone = "" }, false },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void The_plan_still_applies_only_to_the_same_live_account_with_the_same_number(string row, object account, bool expected)
    {
        var applies = DuplicatePhones.StillApplies(account, Plan);
        output.WriteLine($"{row} ⇒ {(applies ? "تَنطَبِق" : "لا تَنطَبِق")}");
        Assert.Equal(expected, applies);
    }

    /// <summary>نَوعٌ لا تُعرَفُ حالُه يَرمي قَبلَ أَيِّ كِتابَة — لا يُكتَبُ جَوّالٌ في وَثيقَةٍ لا يُسأَلُ عَنها.</summary>
    [Fact]
    public async Task An_unknown_account_type_throws_before_any_write()
    {
        var world = new DocWorld().Put(new Probe { Id = Guid.NewGuid() });
        var probe = world.Docs.Values.OfType<Probe>().Single();
        var ex = Record.Exception(() => DuplicatePhones.StillApplies(probe, Plan));
        var calls = 0;
        var thrown = await Record.ExceptionAsync(() => DuplicatePhones.RenameAsync<Probe>(world.Store, "zz-g2sp", [new(probe.Id, To)], (_, _) => calls++));
        output.WriteLine($"نَوعٌ ثالِث ⇒ {ex?.GetType().Name ?? "لا رَمي"} · RenameAsync ⇒ {thrown?.GetType().Name ?? "لا رَمي"} · setPhone {calls} · كُتِب {world.Stored.Count} · الحِفظ {world.SaveCalls}");
        Assert.IsType<NotSupportedException>(ex);
        Assert.IsType<NotSupportedException>(thrown);
        Assert.Equal(0, calls);
        Assert.Empty(world.Stored);
        Assert.Equal(0, world.SaveCalls);
    }

    public sealed class Probe
    {
        public Guid Id { get; set; }
    }

    /// <summary>ما يُنتَظَرُ بَعدَ التَبديل: لا يُعادُ شَيء، والجَوّالُ ما تَرَكَه الكاتِبُ الآخَر، و<c>setPhone</c> لا يُنادى عَلى قِراءَةٍ لا تَنطَبِقُ عَلَيها الخُطَّة.</summary>
    internal static void AssertSwitched(StaleOutcome o, Writer writer)
    {
        AssertWhatStays(o, writer);
        Assert.Equal(0, o.Done);
        Assert.Equal(writer == Writer.OtherPhoneBetween ? Other : "", o.Phone);
        Assert.Equal(writer == Writer.EraseBefore ? 0 : 1, o.Calls);
    }

    [Theory]
    [InlineData(false, Writer.EraseBetween)]
    [InlineData(false, Writer.EraseBefore)]
    [InlineData(false, Writer.OtherPhoneBetween)]
    [InlineData(true, Writer.OtherPhoneBetween)]
    public async Task A_rename_whose_account_changed_after_the_plan_is_skipped_in_the_document_world(bool studio, Writer writer)
    {
        var outcome = await InWorldAsync(studio, writer);
        output.WriteLine($"{(studio ? "StudioUser" : "User")} · {writer} ⇒ {outcome}");
        AssertSwitched(outcome, writer);
    }

    private static string Code() => string.Join('\n', StudioConsentCharacterizationTests.Source("scripts", "merge-duplicate-phones.cs").Split('\n')
        .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && !l.StartsWith("#", StringComparison.Ordinal)));

    /// <summary>السكربتُ يَعُدُّ ما تُخُطِّيَ مِن كُلِّ نِداءٍ لِـ<c>RenameAsync</c> ويَطبَعُه، ويَخرُجُ بِـ0 — التَخَطّي لَيسَ خَطَأ.</summary>
    [Fact]
    public void The_merge_script_prints_what_each_rename_skipped_and_still_exits_zero()
    {
        var code = Code();
        var calls = Regex.Matches(code, @"var (?<v>\w+) = await DuplicatePhones\.RenameAsync<(?<t>\w+)>\(store, [^,]+, (?<plan>\w+)\.Renames,").ToList();
        var counted = calls.Where(m => code.Contains($"skipped += {m.Groups["plan"].Value}.Renames.Count - {m.Groups["v"].Value};", StringComparison.Ordinal)
                                    && code.Contains($"تُخُطِّيَ {{{m.Groups["plan"].Value}.Renames.Count - {m.Groups["v"].Value}}}", StringComparison.Ordinal)).ToList();
        var all = Regex.Matches(code, @"DuplicatePhones\.RenameAsync<").Count;
        output.WriteLine($"نِداءاتُ RenameAsync: {all} · يُعَدُّ تَخَطّيها ويُطبَع: {counted.Count} ({string.Join(" · ", counted.Select(m => m.Groups["t"].Value))})");
        Assert.True(all > 0, "صِفرُ نِداءٍ مَفحوص — الأَداةُ عَمياء.");
        Assert.Equal(all, counted.Count);
        Assert.Contains("وتُخُطِّيَ {skipped}", code, StringComparison.Ordinal);
        Assert.EndsWith("return 0;", code.TrimEnd(), StringComparison.Ordinal);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveDuplicatePhonesRenameStalePlanSwitchProofTests(ITestOutputHelper output)
{
    /// <summary><b>قُلِبَ ما طَبَعَه التَوصيف</b> حَيّاً بِمَخزَنِ السكربت: المَمحُوُّ يَبقى «فارِغاً» بِلا جَوّال، وجَوّالُ الآخَرِ يَبقى، و«أُعيدَ 0» في الأَربَعَة.</summary>
    [Theory]
    [InlineData(false, Writer.EraseBetween)]
    [InlineData(false, Writer.EraseBefore)]
    [InlineData(false, Writer.OtherPhoneBetween)]
    [InlineData(true, Writer.OtherPhoneBetween)]
    public async Task A_rename_whose_account_changed_after_the_plan_is_skipped_on_Postgres(bool studio, Writer writer)
    {
        if (!LiveDuplicatePhonesRenameProofTests.Enabled) return;
        var outcome = await LiveAsync(studio, writer);
        output.WriteLine($"{(studio ? "StudioUser" : "User")} · {writer} · مَخزَنُ السكربت ⇒ {outcome}");
        DuplicatePhonesRenameStalePlanSwitchTests.AssertSwitched(outcome, writer);
    }
}
