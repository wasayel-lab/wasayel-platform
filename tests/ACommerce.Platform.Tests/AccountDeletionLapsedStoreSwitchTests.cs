using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ الحَذفُ و«بَياناتي» في مَتجَرٍ انتَهَت باقَتُه: بابُ صاحِبِ البَياناتِ لا بابُ المَتجَر (‏2026-11-04، الموجَة ١٢ G1 · ٦ — XS-3؛ تَوصيفُه 365dfdc) ════════
//
// قَبلَه («Grace: 302 → /{slug}/?err=store_locked · مَمحُوّ False | Suspended: … مَمحُوّ False»، والشِلُّ «يَستَثني صَفحَتَي صاحِبِ البَيانات: False» —
// `AccountDeletionLapsedStoreTests` يَطبَعُهُما ويَبقى بِبَصمَتِه). والآن البابُ بِلا `RequireStoreWritable` (مُثَبَّتٌ بِسَبَبِه)، والشِلُّ يَسأَلُ `AccountErasure.OwnDataPage`.

public class AccountDeletionLapsedStoreRowsTests
{
    [Theory]
    [InlineData("/zz/me/delete")]
    [InlineData("/zz/me/data")]
    [InlineData("/zz/me/delete/")]
    [InlineData("/ZZ/Me/Data")]
    [InlineData("/zz/r/buyer/me/delete")]
    [InlineData("/zz/r/seller/me/data")]
    public void The_own_data_pages_are_my_data_and_delete_account_with_or_without_a_role_prefix(string path)
        => Assert.True(AccountErasure.OwnDataPage("zz", path));

    [Theory]
    [InlineData("/zz")]
    [InlineData("/zz/me")]
    [InlineData("/zz/me/edit")]
    [InlineData("/zz/me/delete/confirm")]
    [InlineData("/zz/me/data/download")]
    [InlineData("/zz/listings/1/me/data")]
    [InlineData("/zz/r//me/delete")]
    [InlineData("/other/me/delete")]
    [InlineData("/zzz/me/delete")]
    public void Any_other_store_page_or_another_stores_page_is_not_an_own_data_page(string path)
        => Assert.False(AccountErasure.OwnDataPage("zz", path));

    /// <summary>الشِلُّ يُخفي المُعَلَّقَ إلّا صَفحَتَي صاحِبِ البَيانات، والدالَّةُ تُجيبُ عَن مَسارَي الصَفحَتَينِ كَما يُعلِنُهُما <c>@page</c> بِحَرفِهِما.</summary>
    [Fact]
    public void The_shell_asks_the_own_data_question_and_both_pages_routes_answer_it()
    {
        var layout = WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(AccountDeletionLapsedStoreTests.LayoutPath));
        Assert.Contains("@if (Tenant.IsResolved && storeSuspended && !ACommerce.Templates.Customer.Marketplace.Services.Identity.AccountErasure.OwnDataPage(slug, path))",
            layout, StringComparison.Ordinal);
        var pages = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages");
        var routes = Directory.GetFiles(pages, "*.razor").SelectMany(File.ReadAllLines)
            .Where(l => l.StartsWith("@page \"/{slug}/", StringComparison.Ordinal) && (l.Contains("/me/delete\"") || l.Contains("/me/data\""))).ToList();
        Assert.Equal(4, routes.Count);
        foreach (var r in routes)
            Assert.True(AccountErasure.OwnDataPage("zz", r.Split('"')[1].Replace("{slug}", "zz").Replace("{role}", "buyer")), r);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveAccountDeletionLapsedStoreSwitchProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>قُلِبَ ما طَبَعَه التَوصيف. <b>المُوجَب</b>: في «السَماح» وفي «التَعليق» يَحذِفُ البابُ ويَعودُ إلى <c>?deleted=1</c> والحِسابُ مَمحُوّ. <b>والسالِب</b>: في السارِيَةِ
    /// كَما كانَ — والبابُ نَفسُه يَرُدُّ كَلِمَةَ تَأكيدٍ خاطِئَةً في «التَعليق» بِرَمزِه ولا يَمحو (البَوّابَةُ الوَحيدَةُ الَّتي رُفِعَت بَوّابَةُ الباقَة).</summary>
    [Fact]
    public async Task The_deletion_door_deletes_in_grace_and_in_a_suspended_store_and_still_refuses_a_wrong_confirm_word_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var results = new List<string>();
        foreach (var want in new[] { TenantPlanState.None, TenantPlanState.Grace, TenantPlanState.Suspended })
        {
            var slug = "zz-dellapsw-" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                var (member, state) = await LiveAccountDeletionLapsedStoreProofTests.SeedAsync(host.Store, slug, want);
                var (status, at, after) = await LiveAccountDeletionLapsedStoreProofTests.DeleteAsync(host, slug, member);
                results.Add($"{state}: {status} → {at.Replace(slug, "{slug}")} · مَمحُوّ {after.DeletedAt is not null}");
                Assert.Equal(want, state);
                Assert.Equal((302, $"/{slug}?deleted=1"), (status, at));
                Assert.NotNull(after.DeletedAt);
            }
            finally { await LiveAccountDeletionLapsedStoreProofTests.CleanAsync(host.Store, slug); }
        }
        var wrong = "zz-dellapsw-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var (member, _) = await LiveAccountDeletionLapsedStoreProofTests.SeedAsync(host.Store, wrong, TenantPlanState.Suspended);
            var token = ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(member.Id, wrong, member.SessionVersion);
            var (status, at) = await host.PostFormAsync(LiveAccountErasureFilesProofTests.DeleteDoor, new Dictionary<string, string> { ["slug"] = wrong }, wrong, token, ("confirm", "لا"));
            await using var q = host.Store.QuerySession(wrong);
            var after = (await q.LoadAsync<ACommerce.Kit.Auth.User>(member.Id))!;
            results.Add($"تَعليقٌ بِكَلِمَةٍ خاطِئَة: {status} → {at.Replace(wrong, "{slug}")} · مَمحُوّ {after.DeletedAt is not null}");
            Assert.Equal(302, status);
            Assert.EndsWith($"me/delete?err={ACommerce.Kit.Auth.AccountDeletion.ConfirmationMismatch}", at, StringComparison.Ordinal);
            Assert.Null(after.DeletedAt);
        }
        finally { await LiveAccountDeletionLapsedStoreProofTests.CleanAsync(host.Store, wrong); }
        output.WriteLine(string.Join(" | ", results));
    }
}
