using ACommerce.Kit.Auth;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ CO10 (تَدقيقُ الموجَة ١١، ‏2026-10-29): مِفتاحٌ يولَدُ مَيِّتاً — فاعِلُه مَحذوفٌ أَو لا حِسابَ لَه ═══
//
// د56 (‏2026-10-28) جَعَلَ المِفتاحَ يُرَدُّ عِندَ الاستِعمالِ بِـ`ApiKeyRejection.ActorGone` إن رَحَلَ فاعِلُه، وأَخفى «حِسابي» المَحذوفَ مِن قائِمَةِ الفاعِلين
// في `StudioAppKeys.razor`. لَكِنَّ بابَ الإصدار `POST /studio/apps/{slug}/keys/create` يَقرَأُ `actor` مِن الاستِمارَةِ ولا يَسأَلُ الحالَ نَفسَها — فَطَلَبٌ
// بِمُعَرِّفِ حِسابٍ مَحذوفٍ (أَو بِلا حِساب) يُصدِرُ مِفتاحاً «فَعّالاً» في الشاشَةِ يُرَدُّ عِندَ أَوَّلِ استِعمال.
public class ApiKeyIssueActorTests(ITestOutputHelper output)
{
    private const string CreateRoute = "/studio/apps/{slug}/keys/create";

    private static string CreateBody()
    {
        var doors = WriteEndpointGuardTests.AllMinimalApiEndpoints().Where(e => e.Route == CreateRoute).ToList();
        Assert.True(doors.Count == 1, $"«{CreateRoute}»: {doors.Count} باباً — الأَداةُ عَمياء.");
        return doors[0].Body;
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، CO10 — كوميتُ التَبديل)</b> — كانَ
    /// <c>Today_the_issue_door_asks_nothing_about_the_actor_between_validation_and_issue</c> (‏<c>e4f5a4f</c>): «البابُ يَسأَلُ عَن الفاعِلِ قَبلَ الإصدار: False ·
    /// الصَفحَةُ تُسَمّي رَحيلَه: False». والآن البابُ يَسأَلُ <c>ValidateIssueAsync</c> بَعدَ حارِسِه وقَبلَ الإصدار، والصَفحَةُ تُتَرجِمُ الرَمزَ مِن القامُوس.</summary>
    [Fact]
    public void The_issue_door_asks_the_actor_state_after_its_guard_and_before_issue_and_the_page_names_it()
    {
        var body = CreateBody();
        var guard = body.IndexOf("StudioOwnsAsync(store, auth, slug)", StringComparison.Ordinal);
        var asks = body.IndexOf("await keys.ValidateIssueAsync(slug, request)", StringComparison.Ordinal);
        var issue = body.IndexOf("keys.IssueAsync(", StringComparison.Ordinal);
        var page = StudioConsentCharacterizationTests.Source("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "StudioAppKeys.razor");
        var named = page.Contains("ApiKeyService.ActorGoneCode => \"studio.keys.err_actor_gone\"", StringComparison.Ordinal);
        output.WriteLine($"بَعدَ التَبديل: الحارِس@{guard} سُؤالُ الفاعِل@{asks} الإصدار@{issue} · الصَفحَةُ تُسَمّي رَحيلَه: {named}");
        Assert.True(guard > 0 && asks > guard && issue > asks);
        Assert.DoesNotContain("ApiKeyValidator.Validate(request)", body, StringComparison.Ordinal);
        Assert.True(named);
        Assert.False(string.IsNullOrWhiteSpace(ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "studio.keys.err_actor_gone")));
        Assert.Equal("apikey_actor_gone", ApiKeyService.ActorGoneCode);
    }

    private static ApiKeyValidator.IssueRequest For(Guid actor, string name = "رَبطُ الناقِل")
        => new(name, actor, "", new[] { ApiScopeCatalog.DealsRead }, null);

    /// <summary>الرَمزُ بِحالاتِه — مُوجَب: فاعِلٌ مَحذوفٌ، وفاعِلٌ بِلا حِساب؛ سالِب: فاعِلٌ قائِمٌ يُصدَرُ لَه، وطَلَبٌ يَرُدُّه المُصادِقُ يُرَدُّ بِرَمزِه هُوَ
    /// قَبلَ أَن تُقرَأَ حالٌ (المَخزَنُ البَديلُ يَرمي عِندَ أَيِّ نِداءٍ غَيرِ مَقصود).</summary>
    [Theory]
    [InlineData("deleted", "apikey_actor_gone")]
    [InlineData("missing", "apikey_actor_gone")]
    [InlineData("live", null)]
    [InlineData("invalid", "name_empty")]
    public async Task Issue_validation_refuses_a_gone_actor_and_nothing_else(string @case, string? code)
    {
        var user = new User { Id = Guid.NewGuid(), TenantSlug = "zz-co10", DeletedAt = @case == "deleted" ? DateTime.UtcNow : null };
        var keys = new ApiKeyService(AccountStub.Store(user));
        var request = @case switch
        {
            "missing" => For(Guid.NewGuid()),
            "invalid" => For(user.Id, name: ""),
            _ => For(user.Id),
        };
        var violations = await keys.ValidateIssueAsync("zz-co10", request);
        Assert.Equal(code, violations.FirstOrDefault()?.Code);
        Assert.All(violations, v => Assert.False(string.IsNullOrWhiteSpace(v.MessageAr)));
    }
}

/// <summary><b>CO10 حَيّاً عَلى Postgres</b> (‏<c>WASAYEL_LIVE_PROOF=1</c>): الإصدارُ بِمَخزَنِ <see cref="LiveApiKeyActorDeletionProofTests.BuildStore"/>.</summary>
[Collection(LiveProofCollection.Name)]
public class LiveApiKeyIssueActorProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static ApiKeyValidator.IssueRequest For(Guid actor)
        => new("رَبطُ الناقِل", actor, "", new[] { ApiScopeCatalog.DealsRead }, null);

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-29، CO10 — كوميتُ التَبديل)</b> — كانَ
    /// <c>Today_a_key_issued_to_a_deleted_or_missing_actor_is_born_active_and_refused_on_use_on_Postgres</c> (‏<c>e4f5a4f</c>): «مِفتاحُ فاعِلٍ مَحذوف active ثُمَّ
    /// ActorGone · مِفتاحُ فاعِلٍ بِلا حِساب active ثُمَّ ActorGone». والآن سُؤالُ البابِ يَرُدُّهُما قَبلَ الإصدار، والفاعِلُ القائِمُ يُصدَرُ لَه ويُصادِق.</summary>
    [Fact]
    public async Task A_deleted_or_missing_actor_is_refused_before_issue_and_a_live_actor_is_issued_on_Postgres()
    {
        if (!Enabled) return;
        await using var store = LiveApiKeyActorDeletionProofTests.BuildStore();
        var slug = "ak-co10-" + Guid.NewGuid().ToString("N")[..8];
        var issued = new List<string>();
        try
        {
            var gone = new User { Id = Guid.NewGuid(), TenantSlug = slug, Phone = "966500000571" };
            var live = new User { Id = Guid.NewGuid(), TenantSlug = slug, Phone = "966500000572" };
            await using (var s = store.LightweightSession(slug))
            {
                AccountDeletion.Erase(gone, DateTime.UtcNow);
                s.Store(gone, live);
                await s.SaveChangesAsync();
            }
            var keys = new ApiKeyService(store);
            var deleted = (await keys.ValidateIssueAsync(slug, For(gone.Id))).FirstOrDefault()?.Code;
            var missing = (await keys.ValidateIssueAsync(slug, For(Guid.NewGuid()))).FirstOrDefault()?.Code;
            var ok = await keys.ValidateIssueAsync(slug, For(live.Id));
            var key = await keys.IssueAsync(slug, For(live.Id), Guid.NewGuid());
            issued.Add(key.Key.Id);
            var use = (await keys.AuthenticateAsync(key.Presented)).Rejection;
            output.WriteLine($"بَعدَ التَبديل: فاعِلٌ مَحذوف ⇒ {deleted} · فاعِلٌ بِلا حِساب ⇒ {missing} · فاعِلٌ قائِم ⇒ مَوانِع {ok.Count} ثُمَّ {key.Key.Status} ثُمَّ {use}");
            Assert.Equal(ApiKeyService.ActorGoneCode, deleted);
            Assert.Equal(ApiKeyService.ActorGoneCode, missing);
            Assert.Empty(ok);
            Assert.Equal((ApiKeyDocument.StatusActive, ApiKeyRejection.None), (key.Key.Status, use));
        }
        finally
        {
            await using (var g = store.LightweightSession())
            {
                foreach (var id in issued) g.Delete<ApiKeyDocument>(id);
                await g.SaveChangesAsync();
            }
            await LiveZeroRoleAdminProofTests.CleanAsync(store, slug);
        }
    }
}
