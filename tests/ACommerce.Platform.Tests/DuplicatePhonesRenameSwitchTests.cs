using ACommerce.Kit.Auth;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إعادَةُ كَتبِ الجَوّالِ تُعيدُ مَرَّةً عَلى تَعارُضٍ وتُبقي ما كَتَبَه الآخَر — بَعدَ التَبديل (‏2026-11-05، الموجَة ١٢ G2 · ٣؛ A-SESS-W12-1) ═══════════════
//
// قُلِبَ ما طَبَعَه التَوصيف (`DuplicatePhonesRenameTests.cs`، `33745f3`، والمِلَفُّ بِبَصمَتِه): «User ⇒ يَرمي ConcurrencyException · الجَوّال كَما كان» و«StudioUser ⇒
// لا رَمي · إصدارُ الجَلسَة 0 (ضاعَ رَفعُه)». والآن مَخزَنُ السكربتِ يُسَجِّلُ `StudioUser` بِالتَزامُنِ التَفاؤُلِيِّ كَالمُضيف، و`RenameAsync` يُعيدُ مَرَّةً مِن قِراءَةٍ جَديدَة —
// نَسَقُ `ApplyAsync` و`AccountWrites.UpdateAsync`: مُحاوَلَتان، والثانِيَةُ إن رُدَّت تَرمي ولا يُكتَبُ حَرف.

public class DuplicatePhonesRenameSwitchTests(ITestOutputHelper output)
{
    /// <summary>عالَمٌ فيه حِسابُ استوديو بِجَوّالٍ غَيرِ مُوَحَّد، وحِفظُه يَرُدُّه كاتِبٌ مُوازٍ <paramref name="conflicts"/> مَرَّة: يَضَعُ نُسخَتَه (إصدارُ الجَلسَةِ +1) ثُمَّ
    /// يَرمي <c>ConcurrencyException</c> كَما يَفعَلُ Marten حينَ تَتَغَيَّرُ النُسخَةُ بَعدَ القِراءَة.</summary>
    private static (DocWorld World, StudioUser User) World(int conflicts)
    {
        var user = new StudioUser { Id = Guid.NewGuid(), Phone = "0500000781", SessionVersion = 0 };
        var world = new DocWorld().Put(user);
        var saves = 0;
        world.BeforeMember = m =>
        {
            if (m != "SaveChangesAsync" || ++saves > conflicts) return;
            world.Put(new StudioUser { Id = user.Id, Phone = "0500000781", SessionVersion = saves });
            throw new JasperFx.ConcurrencyException(typeof(StudioUser), user.Id);
        };
        return (world, user);
    }

    /// <summary>صيغَةُ الجَوّالِ المُخَزَّنِ لا الجَوّالُ نَفسُه — لا يُكتَبُ حَقلٌ شَخصِيٌّ إلى الخَرج (<c>TestOutputPersonalDataTests</c>).</summary>
    private static string Shape(StudioUser u) => u.Phone.StartsWith("+966", StringComparison.Ordinal) ? "مُوَحَّد" : "كَما كان";

    private static Task<int> RenameAsync(DocWorld world, StudioUser user, Action onSet)
        => DuplicatePhones.RenameAsync<StudioUser>(world.Store, StudioAuth.Tenant, [new DuplicatePhones.Rename(user.Id, "+966500000781")],
            (u, p) => { onSet(); u.Phone = p; });

    /// <summary><b>المُوجَب</b>: تَعارُضٌ واحِدٌ ⇒ تُعادُ القِراءَةُ والكَتبُ مَرَّة، فَالجَوّالُ مُوَحَّدٌ وما كَتَبَه الآخَرُ (إصدارُ الجَلسَة 1) باقٍ، بِلا رَمي.</summary>
    [Fact]
    public async Task One_conflict_is_retried_from_a_fresh_read_and_the_parallel_write_is_kept()
    {
        var (world, user) = World(conflicts: 1);
        var sets = 0;
        var ex = await Record.ExceptionAsync(async () => Assert.Equal(1, await RenameAsync(world, user, () => sets++)));
        var stored = world.Read<StudioUser>(user.Id.ToString())!;
        output.WriteLine($"تَعارُضٌ واحِد ⇒ {ex?.GetType().Name ?? "لا رَمي"} · الجَوّال {Shape(stored)} · إصدارُ الجَلسَة {stored.SessionVersion} · setPhone {sets} · الحِفظ {world.SaveCalls}");
        Assert.Null(ex);
        Assert.Equal("+966500000781", stored.Phone);
        Assert.Equal(1, stored.SessionVersion);
        Assert.Equal(2, sets);
        Assert.Equal(1, world.SaveCalls);
    }

    /// <summary><b>السالِب</b>: تَعارُضانِ مُتَتالِيانِ ⇒ يَرمي بَعدَ مُحاوَلَتَين ولا يَبقى إلّا ما كَتَبَه الآخَر — لا إعادَةَ ثالِثَة ولا كِتابَةَ فَوقَه.</summary>
    [Fact]
    public async Task Two_conflicts_in_a_row_throw_and_write_nothing_over_the_other_writer()
    {
        var (world, user) = World(conflicts: 2);
        var sets = 0;
        var ex = await Record.ExceptionAsync(() => RenameAsync(world, user, () => sets++));
        var stored = world.Read<StudioUser>(user.Id.ToString())!;
        output.WriteLine($"تَعارُضان ⇒ {ex?.GetType().Name ?? "لا رَمي"} · الجَوّال {Shape(stored)} · إصدارُ الجَلسَة {stored.SessionVersion} · setPhone {sets} · الحِفظ {world.SaveCalls}");
        Assert.IsType<JasperFx.ConcurrencyException>(ex);
        Assert.Equal("0500000781", stored.Phone);
        Assert.Equal(2, stored.SessionVersion);
        Assert.Equal(2, sets);
        Assert.Equal(0, world.SaveCalls);
    }

    /// <summary>سكربتُ الدَمجِ يَحمِلُ سَطرَ <c>StudioUser</c> (ومِنه يَقرَأُ صَفُّ التَوصيفِ مَخزَنَه)، وسُطورُ المَخزَنِ المُثَبَّتَةُ تَحمِلُه.</summary>
    [Fact]
    public void The_merge_script_maps_the_studio_account_like_the_host()
    {
        output.WriteLine($"السكربتُ يُسَجِّلُ StudioUser {LiveDuplicatePhonesRenameProofTests.ScriptMapsStudioUser()} · في سُطورِ المَخزَنِ المُثَبَّتَة {DuplicatePhonesTests.StoreLines.Contains(LiveDuplicatePhonesRenameProofTests.StudioConcurrencyLine)}");
        Assert.True(LiveDuplicatePhonesRenameProofTests.ScriptMapsStudioUser());
        Assert.Contains(LiveDuplicatePhonesRenameProofTests.StudioConcurrencyLine, DuplicatePhonesTests.StoreLines);
        Assert.Contains("opts.Schema.For<Services.Incubator.StudioUser>().Identity(x => x.Id).UseOptimisticConcurrency(true);",
            StudioConsentCharacterizationTests.Source("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs"), StringComparison.Ordinal);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveDuplicatePhonesRenameSwitchProofTests(ITestOutputHelper output)
{
    /// <summary><b>قُلِبَ ما طَبَعَه التَوصيف</b> حَيّاً بِمَخزَنِ السكربت: كاتِبٌ مُوازٍ مَرَّةً ⇒ في النَوعَينِ «لا رَمي · أُعيدَ 1 · الجَوّال مُوَحَّد · إصدارُ الجَلسَة 1 ·
    /// نِداءاتُ setPhone 2» (أُعيدَ مَرَّةً وبَقِيَ ما كَتَبَه الآخَر)؛ ومَرَّتَينِ ⇒ «يَرمي ConcurrencyException · أُعيدَ 0 · الجَوّال كَما كان · إصدارُ الجَلسَة 2».</summary>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task A_parallel_writer_is_retried_once_and_kept_and_a_second_one_fails_closed_on_Postgres(bool studio, int parallelWrites)
    {
        if (!LiveDuplicatePhonesRenameProofTests.Enabled) return;
        var outcome = await LiveDuplicatePhonesRenameProofTests.RunAsync(studio, parallelWrites);
        output.WriteLine($"{(studio ? "StudioUser" : "User")} · كاتِبٌ مُوازٍ {parallelWrites} · السكربتُ يُسَجِّلُ StudioUser {LiveDuplicatePhonesRenameProofTests.ScriptMapsStudioUser()} ⇒ {outcome}");
        Assert.Equal(2, outcome.Calls);
        Assert.Equal(parallelWrites, outcome.SessionVersion);
        if (parallelWrites == 1)
        {
            Assert.Equal("", outcome.Thrown);
            Assert.Equal(1, outcome.Done);
            Assert.StartsWith("+966", outcome.Phone, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(nameof(JasperFx.ConcurrencyException), outcome.Thrown);
            Assert.Equal(0, outcome.Done);
            Assert.StartsWith("05", outcome.Phone, StringComparison.Ordinal);
        }
    }
}
