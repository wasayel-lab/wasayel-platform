using System.Security.Cryptography;
using System.Text;
using ACommerce.Kit.Auth.Server;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ سِرِّ تَوقيعِ الجَلسَة — قَبلَ حارِسِ الإقلاع ═══════════════════
//
// **المَقيسُ قَبل (‏2026-10-11، موجَزُ الفَجَوات P0-2)**: `AuthHandlers.TokenSecret`
// يُقرَأُ مِن `ACOMMERCE_AUTH_SECRET` **وإلّا** مِن حَرفِيَّةٍ عَلَنِيَّةٍ مَكتوبَةٍ في
// الكود (‏`AuthHandlers.cs:121-123`)، والتَوقيعُ HMAC-SHA256 فَوقَ
// `{userId}|{tenantSlug}|{exp}` بِهذا السِرّ (‏`MakeToken`/`Sign`). فَمَن عَرَفَ
// الحَرفِيَّةَ — وهي في مُستودَعٍ يُقرَأ — و`userId` و`slug` صاغَ كوكي أَيِّ
// مُستَخدِمٍ في أَيِّ مُستَأجِر؛ وكوكي الاستوديو يُصنَعُ بِالدالَّةِ نَفسِها
// (‏`StudioAuth.cs:134`). ولا حارِسَ إقلاعٍ (الرَميُ الوَحيدُ في `HostingExtensions`
// لِسِلسِلَةِ Postgres)، ولا صَفَّ في `DEPLOY.md`، ولا فَحصَ في
// `verify-production-boot.sh` («المُتَغَيِّرُ الإلزامِيُّ الوَحيد»)، والاختِبارُ
// الوَحيدُ الَّذي يُسَمّيه يَقول «لا نَمَسّ AuthHandlers.TokenSecret»
// (‏`AuthSessionCookieResolutionTests.cs:28`) — فَلا اختِبارَ سالِب.
//
// هذا المِلَفُّ **يُثَبِّتُ البابَ كَما هُوَ** قَبلَ أَن يُغلِقَه الكوميتُ التالي
// بِحارِسٍ خارِجَ التَطوير (القاعِدَة ٣). وما فيه يَبقى أَخضَرَ بَعدَه **بِلا تَعديلِ
// حَرف**: الحارِسُ يَرمي عِندَ الإقلاعِ خارِجَ Development، ولا يُغَيِّرُ ما يَفعَلُه
// `AuthHandlers` في عَمَلِيَّةِ اختِبارٍ بِلا مُتَغَيِّر. وبُرهانُه بَصمَةُ هذا
// المِلَفِّ قَبلَ الكوميتِ التالي وبَعدَه.
//
// **ولا يَمَسُّ `TokenSecret` كِتابَةً** — لِلسَبَبِ المَكتوبِ في
// `AuthSessionCookieResolutionTests`: حالَةٌ عامَّةٌ (static) وxunit يُوازي بَينَ
// الأَصناف. القِراءَةُ وَحدَها.

public class AuthTokenSecretCharacterizationTests
{
    /// <summary>الحَرفِيَّةُ كَما هي في `AuthHandlers.cs` يَومَ التَوصيف — تُكتَبُ
    /// هُنا **بِحَرفِها عَمداً** لا تُقرَأُ مِن ثابِتٍ في الكودِ المَفحوص، كَي
    /// يُحمِرَّ الاختِبارُ لَو تَبَدَّلَت بِصَمت (ذَهَبِيَّةٌ لا مُشتَقَّة).</summary>
    private const string PublicFallback =
        "dev-only-secret-please-set-ACOMMERCE_AUTH_SECRET-in-production-32+";

    private const string EnvVar = "ACOMMERCE_AUTH_SECRET";

    /// <summary>السِرُّ الَّذي يُوَقِّعُ فِعلاً في هذِه العَمَلِيَّة: المُتَغَيِّرُ
    /// إن وُجِد، وإلّا الحَرفِيَّة. هذِه هي صيغَةُ `TokenSecret` اليَومَ بِعَينِها.</summary>
    private static string SecretInUse => Environment.GetEnvironmentVariable(EnvVar) ?? PublicFallback;

    // ─── ١) الصيغَة: المُتَغَيِّرُ وإلّا الحَرفِيَّة ──────────────────────────

    [Fact]
    public void TokenSecret_is_the_variable_or_else_the_public_fallback()
        => Assert.Equal(SecretInUse, AuthHandlers.TokenSecret);

    // ─── ٢) الحَرفِيَّةُ تَمُرُّ مِن فَحصِ الطولِ وَحدَه ──────────────────────

    /// <summary>الحَرفِيَّةُ ‏66 مِحرَفاً — أَي أَنّ فَحصَ الطولِ وَحدَه («٣٢+» كَما
    /// يَقولُ تَعليقُها في `AuthHandlers.cs:120`) **كانَ سَيُمَرِّرُها**. فَأَيُّ
    /// حارِسٍ قادِمٍ يَجِبُ أَن يُقارِنَ القيمَةَ لا الطولَ فَقَط — وهذا الرَقَمُ هُوَ
    /// سَبَبُ ذلكَ القَرار، مَقيساً لا مَظنوناً.</summary>
    [Fact]
    public void The_public_fallback_is_long_enough_to_pass_a_length_only_check()
    {
        Assert.Equal(66, PublicFallback.Length);
        Assert.True(PublicFallback.Length >= 32);
    }

    // ─── ٣) البابُ بِعَينِه: تَزويرٌ بِالحَرفِيَّةِ يَمُرّ ───────────────────

    /// <summary>كوكي يُصاغُ **خارِجَ** `AuthHandlers` بِالحَرفِيَّةِ العَلَنِيَّة —
    /// بِلا سِرٍّ ولا قاعِدَةٍ ولا دُخول — يَقبَلُه `ParseToken` **تَماماً حينَ يَكونُ
    /// السِرُّ المُستَعمَلُ هُوَ الحَرفِيَّة** (أَي حينَ يَغيبُ المُتَغَيِّر). والمُساواةُ
    /// تُثبِتُ الطَرَفَين: يُقبَلُ بِغِيابِه، ويُرفَضُ حينَ يَكونُ السِرُّ غَيرَها.
    /// هذا هُوَ مَعنى «قابِلٌ لِلتَزوير» في P0-2، مَكتوباً اختِباراً.</summary>
    [Fact]
    public void A_cookie_forged_with_the_public_fallback_passes_exactly_when_the_secret_in_use_is_that_fallback()
    {
        var userId = Guid.NewGuid();
        var exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
        var payload = $"{userId}|ashare|{exp}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(PublicFallback));
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        var forged = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{payload}|{sig}"));

        var parsed = AuthHandlers.ParseToken(forged);

        Assert.Equal(SecretInUse == PublicFallback, parsed is not null);
        if (parsed is not null)
        {
            Assert.Equal(userId, parsed.Value.UserId);
            Assert.Equal("ashare", parsed.Value.TenantSlug);
        }
    }

    /// <summary>ومُقابِلُه: نَفسُ الكوكي بِحَرفِيَّةٍ **أُخرى** لا يَمُرّ أَبَداً —
    /// فَالعَطَبُ لَيسَ في `ParseToken` (التَوقيعُ يُفحَص) بَل في **سِرٍّ يَعرِفُه
    /// الجَميع**. يُثَبِّتُ أَنّ الحارِسَ القادِمَ عَلى السِرِّ لا عَلى المُحَلِّل.</summary>
    [Fact]
    public void The_same_forgery_with_any_other_key_is_refused()
    {
        var userId = Guid.NewGuid();
        var exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
        var payload = $"{userId}|ashare|{exp}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(PublicFallback + "-but-not-the-key-in-use"));
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        var forged = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{payload}|{sig}"));

        Assert.Null(AuthHandlers.ParseToken(forged));
    }
}
