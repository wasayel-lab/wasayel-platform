using ACommerce.Kit.Auth;
using ACommerce.Kit.Subscriptions;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ حَذفُ الحِسابِ في مَتجَرٍ انتَهَت باقَتُه — تَوصيفٌ قَبلَ التَبديل (‏2026-11-04، الموجَة ١٢ G1 · ٦؛ XS-3) ══════════════════════════════════════
//
// **ما يُثَبَّتُ هُنا لِيَبقى** (القاعِدَة ٣): بابُ الحَذفِ بِجِسمِه ومُرَشِّحاتِه يُجيبُ تَحويلاً (302) في كُلِّ حالٍ، وفي مَتجَرٍ باقَتُه سارِيَةٌ يَحذِفُ ويَعودُ إلى
// `/{slug}?deleted=1`؛ والشِلُّ (`MainLayout`) يَستَبدِلُ صَفحَةَ «المَتجَرُ مُعَلَّق» بِكُلِّ صَفحَةٍ في المَتجَرِ المُخفى.
// **وما يُطبَعُ ولا يُثَبَّت**: اليَومَ — في «السَماح» وفي «التَعليق» — يَرُدُّ `RequireStoreWritable` البابَ بِـ`?err=store_locked` ولا يُمحى شَيء، والصَفحَةُ تَرسُمُ
// الاستِمارَةَ في «السَماح» (القاعِدَة ١٢)؛ وفي «التَعليق» لا تُرسَمُ صَفحَتا «حَذفُ الحِساب» و«بَياناتي» أَصلاً، وأَبوابُ أَمانِ الحِسابِ الأُخرى مُثَبَّتَةٌ بِلا بَوّابَة.
// وهذا يَتَغَيَّرُ في كوميتِ التَبديل؛ والمِلَفُّ يَبقى بِبَصمَتِه.

public class AccountDeletionLapsedStoreTests(ITestOutputHelper output)
{
    internal static string LayoutPath => Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Layout", "MainLayout.razor");

    /// <summary>الشِلُّ يُخفي المَتجَرَ المُعَلَّقَ بِشَرطٍ واحِدٍ يَبدَأُ بِـ<c>Tenant.IsResolved &amp;&amp; storeSuspended</c> — ويُطبَعُ هَل يَستَثني صَفحَتَي صاحِبِ البَيانات.</summary>
    [Fact]
    public void The_shell_hides_a_suspended_store_behind_one_condition()
    {
        var layout = WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(LayoutPath));
        var at = layout.IndexOf("@if (Tenant.IsResolved && storeSuspended", StringComparison.Ordinal);
        Assert.True(at > 0, "شَرطُ الإخفاءِ غَيرُ مَوجود — الأَداةُ عَمياء.");
        var line = layout[at..layout.IndexOf('\n', at)];
        output.WriteLine($"شَرطُ الإخفاء: «{line.Trim()}» · يَستَثني صَفحَتَي صاحِبِ البَيانات: {line.Contains("me/delete") || line.Contains("OwnDataPage")}");
        Assert.Contains("storeSuspended = !ACommerce.Kit.Subscriptions.TenantPlanPolicy.IsVisible(", layout, StringComparison.Ordinal);
        Assert.Contains("Title=\"@(L[\"store.locked.title\"])\"", layout, StringComparison.Ordinal);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveAccountDeletionLapsedStoreProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>مَتجَرٌ بِباقَةٍ في الحالِ المَطلوبَة (لا وَثيقَة = سارِيَةٌ بِالتَكافُؤِ الصِفرِيّ؛ «سَماح»: انتَهَت أَمسِ ومُهلَتُها ١٤ يَوماً؛ «تَعليق»: أَوقَفَها المُشرِف)،
    /// وعُضوٌ فيه. يُعيدُ الحالَ المُشتَقَّةَ لِيُطبَع.</summary>
    internal static async Task<(User Member, TenantPlanState State)> SeedAsync(IDocumentStore store, string slug, TenantPlanState want)
    {
        var member = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "عُضوٌ في مَتجَرٍ مُنتَهٍ", Phone = "+966500000621", PhoneVerified = true };
        var now = DateTime.UtcNow;
        TenantPlan? plan = want switch
        {
            TenantPlanState.Grace => new TenantPlan { Id = slug, PlanId = "manual", StartsAt = now.AddDays(-31), ExpiresAt = now.AddDays(-1), GraceDays = 14 },
            TenantPlanState.Suspended => new TenantPlan { Id = slug, PlanId = "manual", StartsAt = now.AddDays(-31), ExpiresAt = now.AddDays(10), Status = PlatformPlanStatuses.Stopped },
            _ => null,
        };
        await using (var g = store.LightweightSession())
        {
            if (plan is not null) g.Store(plan);
            await g.SaveChangesAsync();
        }
        await using (var s = store.LightweightSession(slug)) { s.Store(member); await s.SaveChangesAsync(); }
        return (member, TenantPlanPolicy.Derive(plan, now));
    }

    internal static async Task CleanAsync(IDocumentStore store, string slug)
    {
        await using (var g = store.LightweightSession()) { g.Delete<TenantPlan>(slug); await g.SaveChangesAsync(); }
        await LiveZeroRoleAdminProofTests.CleanAsync(store, slug);
    }

    internal static async Task<(int Status, string Location, User After)> DeleteAsync(LiveTemplateDoorHost host, string slug, User member)
    {
        var token = ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(member.Id, slug, member.SessionVersion);
        var confirm = ("confirm", ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "account.delete.confirm_word"));
        var (status, at) = await host.PostFormAsync(LiveAccountErasureFilesProofTests.DeleteDoor, new Dictionary<string, string> { ["slug"] = slug }, slug, token, confirm);
        await using var q = host.Store.QuerySession(slug);
        return (status, at, (await q.LoadAsync<User>(member.Id))!);
    }

    /// <summary>البابُ في الأَحوالِ الثَلاث: تَحويلٌ في كُلٍّ؛ والسارِيَةُ تَحذِفُ وتَعودُ إلى <c>?deleted=1</c>. ويُطبَعُ ما يُجيبُ بِه «السَماحُ» و«التَعليقُ» وهَل مُحِيَ الحِساب.</summary>
    [Fact]
    public async Task The_deletion_door_redirects_in_every_plan_state_and_deletes_in_a_writable_store_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var results = new List<string>();
        foreach (var want in new[] { TenantPlanState.None, TenantPlanState.Grace, TenantPlanState.Suspended })
        {
            var slug = "zz-dellapse-" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                var (member, state) = await SeedAsync(host.Store, slug, want);
                var (status, at, after) = await DeleteAsync(host, slug, member);
                results.Add($"{state}: {status} → {at.Replace(slug, "{slug}")} · مَمحُوّ {after.DeletedAt is not null}");
                Assert.Equal(want, state);
                Assert.Equal(302, status);
                if (want == TenantPlanState.None)
                {
                    Assert.Equal($"/{slug}?deleted=1", at);
                    Assert.NotNull(after.DeletedAt);
                }
            }
            finally { await CleanAsync(host.Store, slug); }
        }
        output.WriteLine(string.Join(" | ", results));
    }
}
