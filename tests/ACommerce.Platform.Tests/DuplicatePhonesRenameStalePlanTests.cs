using ACommerce.Kit.Auth;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ إعادَةُ كَتبِ الجَوّالِ عَلى قِراءَةٍ جَديدَةٍ بِخُطَّةٍ مِن قِراءَةٍ قَديمَة — تَوصيفٌ قَبلَ التَبديل (‏2026-11-05، الموجَة ١٢ G2، الجَولَةُ ٢ — G2R2-1) ═══════
//
// `DuplicatePhones.Rename.To` هُوَ الصيغَةُ المُوَحَّدَةُ لِلجَوّالِ الَّذي قُرِئَ حينَ بُنِيَت الخُطَّة. و`RenameOnceAsync` يُحَمِّلُ الوَثيقَةَ ويَكتُبُ `To` فيها ولا يَسأَل:
// أَما زالَت الحِسابَ نَفسَه بِالجَوّالِ نَفسِه؟ ومُنذُ `2158fed` يُعيدُ نَفسَه مَرَّةً مِن قِراءَةٍ جَديدَةٍ عَلى `ConcurrencyException` — فَالكاتِبُ المُوازي الَّذي رُدَّ
// الحِفظُ بِسَبَبِه يُكتَبُ فَوقَه: `AccountDeletion.Erase` (يُفرِغُ `Phone` ويَضَعُ `DeletedAt` ويَرفَعُ إصدارَ الجَلسَةِ ويُبقي الوَثيقَة) فَيَعودُ الجَوّالُ إلى حِسابٍ
// مَمحُوّ، وجَوّالٌ جَديدٌ مِن «حِسابي» فَيَرجِعُ الرَقَمُ القَديم — وتَعليقُ `RenameAsync` يَقولُ «يُبقي ما كَتَبَه الآخَر». وقَبلَ `2158fed` كانَ التَعارُضُ يَرمي فَيَبقى المَحو.
// ونافِذَةٌ أَوسَعُ قائِمَةٌ قَبلَ هذا المَدى: حِسابٌ مُحِيَ بَعدَ بِناءِ الخُطَّةِ وقَبلَ `RenameAsync` يَأخُذُ الجَوّالَ كَذلِك — لا تَعارُضَ فيه أَصلاً.
//
// **كَيفَ يُقاسُ هُنا**: (أ) عالَمُ وَثائِقَ (`DocWorld`): أَوَّلُ حِفظٍ يَضَعُ نُسخَةَ الكاتِبِ المُوازي ثُمَّ يَرمي `ConcurrencyException` كَما يَفعَلُ Marten حينَ تَتَغَيَّرُ
// النُسخَةُ بَعدَ القِراءَة؛ (ب) حَيّاً عَلى Postgres بِمَخزَنِ السكربتِ (`LiveDuplicatePhonesRenameProofTests.ScriptStore`): الكاتِبُ المُوازي يَكتُبُ في جَلسَةٍ أُخرى داخِلَ
// أَوَّلِ نِداءٍ لِـ`setPhone` (بَعدَ القِراءَةِ وقَبلَ الحِفظ). والكُتّابُ ثَلاثَة (<see cref="Writer"/>)، و`setPhone` شَكلُ السكربتِ بِعَينِه. ولا يُطبَعُ جَوّال: صيغَتُه وَحدَها.
//
// **ما يُثَبَّتُ هُنا لِيَبقى** (القاعِدَة ٣): لا رَمي، والمَحوُ باقٍ (`DeletedAt` وإصدارُ الجَلسَةِ 1)، وجَوّالُ الآخَرِ لا يَجعَلُ الحِسابَ مَحذوفاً، ونودِيَ `setPhone`
// إلّا حَيثُ مُحِيَ الحِسابُ قَبلَ النِداء. **وما يُطبَعُ ولا يُثَبَّت**: صيغَةُ الجَوّالِ المُخَزَّنِ وما أُعيدَ كَتبُه — ويَتَغَيَّرانِ في كوميتِ التَبديل، والمِلَفُّ يَبقى بِبَصمَتِه.

public class DuplicatePhonesRenameStalePlanTests(ITestOutputHelper output)
{
    /// <summary>الكاتِبُ المُوازي: مَحوٌ بَينَ القِراءَةِ والحِفظ، أَو مَحوٌ قَبلَ <c>RenameAsync</c>، أَو جَوّالٌ آخَرُ بَينَ القِراءَةِ والحِفظ.</summary>
    public enum Writer { EraseBetween, EraseBefore, OtherPhoneBetween }

    internal const string Typed = "0500000783";
    internal const string To = "+966500000783";
    internal const string Other = "+966500000784";
    internal static readonly DateTime At = new(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>صيغَةُ الجَوّالِ المُخَزَّنِ لا الجَوّالُ نَفسُه (<c>TestOutputPersonalDataTests</c>).</summary>
    internal static string ShapeOf(string phone)
        => phone == "" ? "فارِغ" : phone == To ? "مُوَحَّدُ الخُطَّة" : phone == Other ? "ما كَتَبَه الآخَر" : phone == Typed ? "كَما كان" : "غَيرُها";

    /// <summary>ما بَعدَ إعادَةِ الكَتب: هَل رَمى، وما أَعادَه، وهَل الحِسابُ مَحذوف، والجَوّالُ المُخَزَّن، وإصدارُ الجَلسَة، وكَم مَرَّةً نودِيَ <c>setPhone</c>.</summary>
    internal sealed record StaleOutcome(string Thrown, int Done, bool Deleted, string Phone, int SessionVersion, int Calls)
    {
        public override string ToString()
            => $"{(Thrown == "" ? "لا رَمي" : "يَرمي " + Thrown)} · أُعيدَ {Done} · مَحذوف {Deleted} · الجَوّال {ShapeOf(Phone)} · إصدارُ الجَلسَة {SessionVersion} · نِداءاتُ setPhone {Calls}";
    }

    /// <summary>نُسخَةُ الكاتِبِ المُوازي — مِن الحالِ الَّتي قُرِئَت (جَوّالٌ غَيرُ مُوَحَّدٍ وإصدارٌ 0)، لا مِن الكائِنِ الَّذي يُعَدِّلُه <c>setPhone</c>.</summary>
    private static object ParallelCopy(bool studio, Writer writer, Guid id, string tenant)
        => studio
            ? new StudioUser { Id = id, Phone = Other }
            : writer == Writer.OtherPhoneBetween
                ? new User { Id = id, TenantSlug = tenant, Phone = Other, UpdatedAt = At }
                : AccountDeletion.Erase(new User { Id = id, TenantSlug = tenant, Phone = Typed }, At);

    /// <summary>(أ) في عالَمِ الوَثائِق: حِسابٌ بِجَوّالٍ غَيرِ مُوَحَّد، والكاتِبُ المُوازي بِحَسَبِ <paramref name="writer"/>، ثُمَّ <c>RenameAsync</c> بِشَكلِ السكربت.</summary>
    internal static async Task<StaleOutcome> InWorldAsync(bool studio, Writer writer)
    {
        const string tenant = "zz-g2sp";
        var id = Guid.NewGuid();
        var world = new DocWorld();
        if (studio) world.Put(new StudioUser { Id = id, Phone = Typed });
        else world.Put(new User { Id = id, TenantSlug = tenant, Phone = Typed });
        if (writer == Writer.EraseBefore) world.Put(ParallelCopy(studio, writer, id, tenant));
        else
        {
            var saves = 0;
            world.BeforeMember = m =>
            {
                if (m != "SaveChangesAsync" || ++saves > 1) return;
                world.Put(ParallelCopy(studio, writer, id, tenant));
                throw new JasperFx.ConcurrencyException(studio ? typeof(StudioUser) : typeof(User), id);
            };
        }
        var calls = 0;
        var renames = new[] { new DuplicatePhones.Rename(id, To) };
        string thrown = "";
        var done = 0;
        try
        {
            done = studio
                ? await DuplicatePhones.RenameAsync<StudioUser>(world.Store, StudioAuth.Tenant, renames, (u, p) => { calls++; u.Phone = p; })
                : await DuplicatePhones.RenameAsync<User>(world.Store, tenant, renames, (u, p) => { calls++; u.Phone = p; u.UpdatedAt = At; });
        }
        catch (Exception e) { thrown = e.GetType().Name; }
        if (studio)
        {
            var su = world.Read<StudioUser>(id.ToString())!;
            return new(thrown, done, false, su.Phone, su.SessionVersion, calls);
        }
        var mu = world.Read<User>(id.ToString())!;
        return new(thrown, done, mu.DeletedAt is not null, mu.Phone, mu.SessionVersion, calls);
    }

    /// <summary>(ب) حَيّاً بِمَخزَنِ السكربت: الحِسابُ في مَتجَرٍ مُؤَقَّتٍ (أَو <c>_studio</c>)، والكاتِبُ المُوازي في جَلسَةٍ أُخرى — داخِلَ أَوَّلِ <c>setPhone</c>، أَو قَبلَ
    /// <c>RenameAsync</c>. والحِسابُ يُمسَحُ بَعدَه.</summary>
    internal static async Task<StaleOutcome> LiveAsync(bool studio, Writer writer)
    {
        await using var store = LiveDuplicatePhonesRenameProofTests.ScriptStore();
        var tenant = studio ? StudioAuth.Tenant : "zz-g2sp-" + Guid.NewGuid().ToString("N")[..8];
        var id = Guid.NewGuid();
        await using (var s = store.LightweightSession(tenant))
        {
            if (studio) s.Store(new StudioUser { Id = id, Phone = Typed });
            else s.Store(new User { Id = id, TenantSlug = tenant, Phone = Typed });
            await s.SaveChangesAsync();
        }
        async Task ParallelAsync()
        {
            await using var p = store.LightweightSession(tenant);
            if (studio)
            {
                var u = (await p.LoadAsync<StudioUser>(id))!;
                u.Phone = Other;
                p.Store(u);
            }
            else
            {
                var u = (await p.LoadAsync<User>(id))!;
                if (writer == Writer.OtherPhoneBetween) (u.Phone, u.UpdatedAt) = (Other, At);
                else AccountDeletion.Erase(u, At);
                p.Store(u);
            }
            await p.SaveChangesAsync();
        }
        try
        {
            if (writer == Writer.EraseBefore) await ParallelAsync();
            var calls = 0;
            void Between() { if (++calls == 1 && writer != Writer.EraseBefore) ParallelAsync().GetAwaiter().GetResult(); }
            var renames = new[] { new DuplicatePhones.Rename(id, To) };
            string thrown = "";
            var done = 0;
            try
            {
                done = studio
                    ? await DuplicatePhones.RenameAsync<StudioUser>(store, tenant, renames, (u, p) => { Between(); u.Phone = p; })
                    : await DuplicatePhones.RenameAsync<User>(store, tenant, renames, (u, p) => { Between(); u.Phone = p; u.UpdatedAt = At; });
            }
            catch (Exception e) { thrown = e.GetType().Name; }
            await using var q = store.QuerySession(tenant);
            if (studio)
            {
                var su = (await q.LoadAsync<StudioUser>(id))!;
                return new(thrown, done, false, su.Phone, su.SessionVersion, calls);
            }
            var mu = (await q.LoadAsync<User>(id))!;
            return new(thrown, done, mu.DeletedAt is not null, mu.Phone, mu.SessionVersion, calls);
        }
        finally
        {
            await using var s = store.LightweightSession(tenant);
            if (studio) s.Delete<StudioUser>(id); else s.Delete<User>(id);
            await s.SaveChangesAsync();
        }
    }

    /// <summary><b>ما يَبقى</b> في الشَكلَين: لا رَمي، والمَحوُ باقٍ بِإصدارِه المَرفوع، وجَوّالُ الآخَرِ لا يَمحو، و<c>setPhone</c> نودِيَ إلّا حَيثُ مُحِيَ الحِسابُ قَبلَه.
    /// <b>وما يُطبَع</b>: صيغَةُ الجَوّالِ وما أُعيدَ كَتبُه.</summary>
    internal static void AssertWhatStays(StaleOutcome o, Writer writer)
    {
        Assert.Equal("", o.Thrown);
        if (writer == Writer.OtherPhoneBetween) Assert.False(o.Deleted);
        else
        {
            Assert.True(o.Deleted);
            Assert.Equal(1, o.SessionVersion);
        }
        if (writer != Writer.EraseBefore) Assert.True(o.Calls >= 1);
    }

    [Theory]
    [InlineData(false, Writer.EraseBetween)]
    [InlineData(false, Writer.EraseBefore)]
    [InlineData(false, Writer.OtherPhoneBetween)]
    [InlineData(true, Writer.OtherPhoneBetween)]
    public async Task A_rename_whose_account_changed_after_the_plan_is_measured_in_the_document_world(bool studio, Writer writer)
    {
        var outcome = await InWorldAsync(studio, writer);
        output.WriteLine($"{(studio ? "StudioUser" : "User")} · {writer} ⇒ {outcome}");
        AssertWhatStays(outcome, writer);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveDuplicatePhonesRenameStalePlanProofTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, DuplicatePhonesRenameStalePlanTests.Writer.EraseBetween)]
    [InlineData(false, DuplicatePhonesRenameStalePlanTests.Writer.EraseBefore)]
    [InlineData(false, DuplicatePhonesRenameStalePlanTests.Writer.OtherPhoneBetween)]
    [InlineData(true, DuplicatePhonesRenameStalePlanTests.Writer.OtherPhoneBetween)]
    public async Task A_rename_whose_account_changed_after_the_plan_is_measured_on_Postgres(bool studio, DuplicatePhonesRenameStalePlanTests.Writer writer)
    {
        if (!LiveDuplicatePhonesRenameProofTests.Enabled) return;
        var outcome = await DuplicatePhonesRenameStalePlanTests.LiveAsync(studio, writer);
        output.WriteLine($"{(studio ? "StudioUser" : "User")} · {writer} · مَخزَنُ السكربت ⇒ {outcome}");
        DuplicatePhonesRenameStalePlanTests.AssertWhatStays(outcome, writer);
    }
}
