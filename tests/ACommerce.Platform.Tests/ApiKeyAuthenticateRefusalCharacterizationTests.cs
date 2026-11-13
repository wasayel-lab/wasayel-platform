using ACommerce.Kit.Auth;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفٌ: ما يَرُدُّ به اعتِمادُ مِفتاحِ API بِحالِه وانتِهائِه — ما لا يَتَغَيَّرُ حينَ يَصيرُ «صالِحٌ الآن» دالَّةً واحِدَةً عَلى الوَثيقَة ════════════
// (‏2026-11-13، الموجَة ١٣ W13-6·D-R2·١ — مُراجَعَةُ المَرحَلَةِ د الثانِيَة: M6D-CNT-R2-1؛ ADR-051 §٢٣؛ القاعِدَتانِ ٣ و١٣). **لِماذا هذا المِلَفّ**:
// `ApiKeyService.AuthenticateAsync` يَسأَلُ سُؤالَين مُتَتالِيَين — الحالُ (`Status != active` ⇐ `Revoked`) ثُمَّ الانتِهاءُ (`ExpiresAt <= now` ⇐ `Expired`) —
// وقارِئُ الحُضورِ يَسأَلُ الأَوَّلَ وَحدَه، فَيَعُدُّ المِفتاحَ المُنتَهِيَ «حاضِراً» كُلَّ يَومٍ بَعدَ انتِهائِه والمَنَصَّةُ تَرُدُّه.
// **والتَبديلُ التالي يَجمَعُ السُؤالَينِ في `ApiKeyDocument.IsUsableAt(at)`** يُناديهِ الاعتِمادُ والقارِئُ مَعاً — وهذا المِلَفُّ يُثَبِّتُ قَبلَه ما يَجِبُ أَلّا
// يَتَغَيَّرَ في الاعتِماد، ولا يُمَسُّ بَعدَه حَرفاً: سَبَبُ الرَفضِ لِكُلِّ حالٍ وانتِهاء، وتَرتيبُه بَينَ إخوَتِه (السِرُّ قَبلَهُما، والمُستَأجِرُ والفاعِلُ بَعدَهُما)،
// ومِفتاحٌ انتِهاؤُه اللَحظَةُ الَّتي سَبَقَت السُؤالَ مُنتَهٍ. **وما لا يُثَبِّتُه — يُقال**: الساعَةُ داخِلَ الاعتِمادِ ساعَةُ النِظام، فَالمُساواةُ التامَّةُ
// («<=» لا «<») لا تُقاسُ هُنا بِحَتم؛ تُقاسُ في الدالَّةِ النَقِيَّةِ الَّتي يَأتي بِها التَبديلُ بِلَحظَةٍ مُعطاة.
public class ApiKeyAuthenticateRefusalCharacterizationTests(ITestOutputHelper output)
{
    private const string Slug = "keys-refusal";

    /// <summary>حالُ المِفتاحِ في الصَفّ: الحالُ والانتِهاءُ (بِالساعاتِ عَن الآن، أَو <c>null</c> بِلا انتِهاء، أَو <c>0</c> = لَحظَةُ ما قَبلَ السُؤال)،
    /// وسِرٌّ خاطِئ، ومُستَأجِرٌ فارِغ، وفاعِلٌ بِلا حِساب.</summary>
    public sealed record Case(string Name, string Status, int? ExpiresInHours, bool WrongSecret, bool NoTenant, bool ActorGone, ApiKeyRejection Expected);

    private static readonly Case[] Cases =
    [
        new("نَشِطٌ بِلا انتِهاء",                         ApiKeyDocument.StatusActive,  null, false, false, false, ApiKeyRejection.None),
        new("نَشِطٌ يَنتَهي غَداً",                         ApiKeyDocument.StatusActive,  24,   false, false, false, ApiKeyRejection.None),
        new("نَشِطٌ انتَهى قَبلَ ساعَة",                    ApiKeyDocument.StatusActive,  -1,   false, false, false, ApiKeyRejection.Expired),
        new("نَشِطٌ انتِهاؤُه لَحظَةُ ما قَبلَ السُؤال",      ApiKeyDocument.StatusActive,  0,    false, false, false, ApiKeyRejection.Expired),
        new("مُبطَلٌ بِلا انتِهاء",                         ApiKeyDocument.StatusRevoked, null, false, false, false, ApiKeyRejection.Revoked),
        new("مُبطَلٌ انتَهى قَبلَ ساعَة",                   ApiKeyDocument.StatusRevoked, -1,   false, false, false, ApiKeyRejection.Revoked),
        new("مُبطَلٌ يَنتَهي غَداً",                        ApiKeyDocument.StatusRevoked, 24,   false, false, false, ApiKeyRejection.Revoked),
        new("حالٌ لا يَعرِفُها المُعجَمُ بِلا انتِهاء",      "suspended",                  null, false, false, false, ApiKeyRejection.Revoked),
        new("نَشِطٌ مُنتَهٍ بِسِرٍّ خاطِئ",                  ApiKeyDocument.StatusActive,  -1,   true,  false, false, ApiKeyRejection.SecretMismatch),
        new("مُبطَلٌ بِسِرٍّ خاطِئ",                        ApiKeyDocument.StatusRevoked, null, true,  false, false, ApiKeyRejection.SecretMismatch),
        new("نَشِطٌ مُنتَهٍ بِلا مُستَأجِر",                 ApiKeyDocument.StatusActive,  -1,   false, true,  false, ApiKeyRejection.Expired),
        new("نَشِطٌ بِلا انتِهاءٍ ولا مُستَأجِر",             ApiKeyDocument.StatusActive,  null, false, true,  false, ApiKeyRejection.TenantGone),
        new("نَشِطٌ مُنتَهٍ فاعِلُه بِلا حِساب",             ApiKeyDocument.StatusActive,  -1,   false, false, true,  ApiKeyRejection.Expired),
        new("نَشِطٌ يَنتَهي غَداً فاعِلُه بِلا حِساب",        ApiKeyDocument.StatusActive,  24,   false, false, true,  ApiKeyRejection.ActorGone),
        new("مُبطَلٌ مُنتَهٍ فاعِلُه بِلا حِساب",             ApiKeyDocument.StatusRevoked, -1,   false, false, true,  ApiKeyRejection.Revoked),
    ];

    /// <summary>المِفتاحُ بِمَسارِ الإصدارِ الحَقيقيّ (<c>IssueAsync</c>) في عالَمِ الذاكِرَة، ثُمَّ يُضبَطُ حالُه — والوَثيقَةُ المُخَزَّنَةُ هِيَ المَرجِعُ نَفسُه.</summary>
    private static async Task<ApiKeyAuthResult> AuthenticateAsync(Case c)
    {
        var world = new DocWorld();
        var actor = new User { Id = Guid.NewGuid(), TenantSlug = Slug, FullName = "فاعِلُ التَوصيف", Phone = "0500000001" };
        world.Put(actor);
        var keys = new ApiKeyService(world.Store);
        var issued = await keys.IssueAsync(Slug,
            new ApiKeyValidator.IssueRequest("مِفتاحُ التَوصيف", actor.Id, "", new[] { ApiScopeCatalog.DealsRead }, null),
            issuedByStudioUserId: Guid.NewGuid());
        var doc = issued.Key;
        doc.Status = c.Status;
        if (c.NoTenant) doc.TenantSlug = "";
        if (c.ActorGone) world.Docs.Remove(DocWorld.KeyOf(typeof(User), actor.Id.ToString()));
        var presented = issued.Presented;
        if (c.WrongSecret) presented = presented[..^1] + (presented[^1] == '0' ? '1' : '0');
        doc.ExpiresAt = c.ExpiresInHours switch
        {
            null => null,
            0 => DateTime.UtcNow,
            { } h => DateTime.UtcNow.AddHours(h),
        };
        return await keys.AuthenticateAsync(presented);
    }

    [Fact]
    public async Task Each_status_and_expiry_is_refused_with_its_reason_in_its_order()
    {
        var measured = new List<(Case Case, ApiKeyRejection Got)>();
        foreach (var c in Cases)
        {
            var r = await AuthenticateAsync(c);
            measured.Add((c, r.Rejection));
            output.WriteLine($"[تَوصيف] {c.Name}: {(r.Ok ? "مَقبول" : r.Rejection)} (المُثَبَّت {c.Expected})");
            Assert.Equal(r.Rejection == ApiKeyRejection.None, r.Ok);
        }
        output.WriteLine($"[تَوصيف] صُفوف {measured.Count} · مُطابِقَة {measured.Count(m => m.Got == m.Case.Expected)}");
        Assert.True(measured.Count >= 15, $"أَداةٌ عَمياء: {measured.Count} صَفّاً.");
        Assert.Equal(Cases.Select(c => $"{c.Name}={c.Expected}"), measured.Select(m => $"{m.Case.Name}={m.Got}"));
    }

    /// <summary>المَقبولُ يَحمِلُ هُوِيَّتَه مِن الوَثيقَة — مِفتاحٌ يَنتَهي غَداً كَمِفتاحٍ بِلا انتِهاء.</summary>
    [Fact]
    public async Task A_key_that_expires_tomorrow_authenticates_as_its_actor_in_its_store()
    {
        var r = await AuthenticateAsync(Cases[1]);
        output.WriteLine($"[تَوصيف] {Cases[1].Name}: {r.Rejection} · المُستَأجِر {r.Principal?.TenantSlug}");
        Assert.True(r.Ok);
        Assert.Equal(Slug, r.Principal!.TenantSlug);
        Assert.Equal(new[] { ApiScopeCatalog.DealsRead }, r.Principal.Scopes);
    }
}
