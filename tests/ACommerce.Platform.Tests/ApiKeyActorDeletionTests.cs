using ACommerce.Kit.Auth;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>مِفتاحُ رَبطٍ بِاسمِ مَن حَذَفَ حِسابَه — قَرارُ المالِك د56 (‏2026-10-28).</b> «حَذفُ الحِسابِ يُبطِلُ، في العَمَلِيَّةِ نَفسِها، كُلَّ
/// مِفتاحِ رَبطٍ صَدَرَ بِاسمِ هذا المُستَخدِم، ويَرى صاحِبُ المَتجَرِ المِفتاحَ «مُبطَلاً». ومَعَه فَحصٌ ثانٍ عِندَ استِعمالِ أَيِّ مِفتاح: يُرفَضُ
/// المِفتاحُ إن كانَ صاحِبُه مَحذوفاً.»</para>
///
/// <para><b>«اليَوم» قَبلَ التَبديل</b> (القاعِدَة ٣): <c>ApiKeyService.AuthenticateAsync</c> لا يَسأَلُ عَن حِسابِ الفاعِل، ومَعجَمُ أَسبابِ الرَفضِ
/// ثَمانِيَةٌ لا سَبَبَ فيها لِفاعِلٍ مَحذوف، ونُقطَةُ الحَذفِ تَحفَظُ وَثيقَةَ <c>User</c> وَحدَها ولا تَلمِسُ مِفتاحاً — فَالمِفتاحُ يُصادِقُ بَعدَ الحَذفِ
/// بِاسمِ صاحِبِه ويُحَرِّكُ صَفَقاتِه. وكُلُّ صَفٍّ «Today_» يَنقَلِبُ بِاسمِه في كوميتِ التَبديل.</para>
/// </summary>
public class ApiKeyActorDeletionTests(ITestOutputHelper output)
{
    internal const string Slug = "keys-shop";

    /// <summary>عالَمٌ في الذاكِرَة: حِسابُ مَتجَرٍ ومِفتاحٌ أَصدَرَه المالِكُ بِاسمِه بِمَسارِ الإصدارِ الحَقيقيّ (<c>IssueAsync</c>).</summary>
    internal static async Task<(DocWorld World, User Actor, ApiKeyIssued Issued)> IssuedForAsync()
    {
        var world = new DocWorld();
        var actor = new User { Id = Guid.NewGuid(), TenantSlug = Slug, FullName = "سائِقُ الاختِبار", Phone = "0500000000" };
        world.Put(actor);
        var issued = await new ApiKeyService(world.Store).IssueAsync(Slug,
            new ApiKeyValidator.IssueRequest("رَبطُ الناقِل", actor.Id, "", new[] { ApiScopeCatalog.DealsRead }, null),
            issuedByStudioUserId: Guid.NewGuid());
        return (world, actor, issued);
    }

    /// <summary>جِسمُ نُقطَةِ حَذفِ الحِسابِ بِلا تَعليق — مِن مَسارِها إلى النُقطَةِ الَّتي تَليها.</summary>
    internal static string DeletionBody()
    {
        var mte = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs")));
        var at = mte.IndexOf("\"/{slug}/me/delete/confirm\"", StringComparison.Ordinal);
        Assert.True(at > 0, "لا نُقطَةَ حَذفٍ — الأَداةُ عَمياء.");
        var end = mte.IndexOf("app.Map", at, StringComparison.Ordinal);
        Assert.True(end > at, "لا نُقطَةَ بَعدَ الحَذف — الأَداةُ لا تَعرِفُ أَينَ يَنتَهي الجِسم.");
        return mte[at..end];
    }

    [Fact]
    public async Task Today_a_key_whose_actor_deleted_their_account_still_authenticates_in_their_name()
    {
        var (world, actor, issued) = await IssuedForAsync();
        AccountDeletion.Erase(actor, DateTime.UtcNow);

        var result = await new ApiKeyService(world.Store).AuthenticateAsync(issued.Presented);

        output.WriteLine($"الفاعِلُ مَحذوف: {actor.DeletedAt is not null} · الاعتِماد: {(result.Ok ? "مَقبول" : result.Rejection)} · " +
                         $"الفاعِلُ في الهُوِيَّة: {result.Principal?.ActorUserId == actor.Id}");
        Assert.NotNull(actor.DeletedAt);
        Assert.True(result.Ok);
        Assert.Equal(actor.Id, result.Principal!.ActorUserId);
    }

    [Fact]
    public void Today_the_rejection_lexicon_has_no_reason_for_a_deleted_actor()
    {
        var names = Enum.GetNames<ApiKeyRejection>();
        output.WriteLine($"أَسبابُ الرَفض ({names.Length}): {string.Join(", ", names)}");
        Assert.Equal(new[] { "None", "Missing", "Malformed", "Unknown", "SecretMismatch", "Revoked", "Expired", "TenantGone" }, names);
    }

    [Fact]
    public void Today_the_deletion_endpoint_saves_the_user_alone_and_touches_no_key()
    {
        var body = DeletionBody();
        var saves = body.Split("SaveChangesAsync(").Length - 1;
        output.WriteLine($"جِسمُ الحَذف: {body.Length} حَرفاً · SaveChangesAsync {saves} · يَذكُرُ ApiKey {body.Contains("ApiKey", StringComparison.Ordinal)}");
        Assert.True(body.Length > 400, $"أَداةٌ عَمياء: {body.Length} حَرفاً.");
        Assert.Equal(1, saves);
        Assert.Contains("session.Store(user!);", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ApiKey", body, StringComparison.Ordinal);
    }
}
