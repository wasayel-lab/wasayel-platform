using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Templates.Customer.Marketplace.Services.Shipping;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ رَمزُ دُخولٍ بِالجَوّالِ نَجَحَ لِحِسابٍ قائِم — لا يَرفَعُ `PhoneVerified` — تَوصيفُ «اليَوم» ═══
//
// مُراجَعَةُ المَوجَة 3b (‏2026-10-13، G5 M2 — قَبلَ أَن يُضبَطَ `Shipping__Oto__MarketplaceToken`): بَوّابَةُ جِهَةِ التَواصُلِ (`VendorContactGate`)
// تَقبَلُ البائِعَ بِبَريدٍ أَو جَوّالٍ «تَحَقَّقنا أَنَّه لَه» — `User.EmailVerified`/`PhoneVerified`. و`AuthHandlers.GetOrCreateUserAsync` تَرفَعُ
// `PhoneVerified` عِندَ **إنشاءِ** الحِسابِ بِالجَوّالِ وَحدَه، وتَرفَعُ `EmailVerified` لِحِسابٍ **قائِمٍ** أَثبَتَ بَريدَه — ولا تَرفَعُ `PhoneVerified`
// لِحِسابٍ قائِمٍ أَثبَتَ جَوّالَه. فَحِساباتٌ كَتَبَها غَيرُ الدُخولِ — `tools/ACommerce.Importer` (‏`AshareImporter`، `EjarImporter`) و`TestDataSeeder` —
// تَبقى `PhoneVerified=false` بَعدَ رَمزٍ ناجِحٍ إلى جَوّالِها، والدُخولُ بِالبَريدِ يَبحَثُ بِالبَريدِ فَيُنشِئُ حِساباً آخَر: لا بابَ لِلبائِعِ إلى التَسجيل.
//
// **ما يُقلَبُ بِاسمِه في كوميتِ الإصلاح**: الصَفّانِ أَدناه. لا بَياناتِ مُستَأجِرٍ حَقيقِيٍّ هُنا: الحِسابُ يُكتَبُ بِشَكلِ المُستَورِدِ في مُستَأجِرٍ مُؤَقَّت.

public class PhoneVerifiedOnLoginTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    /// <summary>جِسمُ <c>GetOrCreateUserAsync</c> بِلا تَعليقات — مِن تَوقيعِها إلى صُنعِ الرَمز.</summary>
    internal static string GetOrCreateBody()
    {
        var path = Path.Combine(RepoRoot, "libs", "kits", "Auth", "ACommerce.Kit.Auth.Server", "AuthHandlers.cs");
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path}");
        var code = WriteEndpointGuardTests.StripComments(File.ReadAllText(path));
        var start = code.IndexOf("private static async Task<AuthResult> GetOrCreateUserAsync(", StringComparison.Ordinal);
        var end = code.IndexOf("var token = MakeToken(existing.Id, tenantSlug);", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, $"أَداةٌ عَمياء: لَم يوجَد الجِسم ({start}/{end}).");
        return code[start..end];
    }

    /// <summary><b>اليَوم</b>: الحِسابُ القائِمُ يُرفَعُ لَه <c>EmailVerified</c> وَحدَه — و<c>PhoneVerified</c> لا يُكتَبُ إلّا عِندَ الإنشاء.</summary>
    [Fact]
    public void Today_an_existing_account_gets_email_verified_raised_and_never_phone_verified()
    {
        var body = GetOrCreateBody();
        output.WriteLine($"طولُ الجِسم: {body.Length} · ذِكرُ PhoneVerified: {body.Split("PhoneVerified").Length - 1}");
        Assert.Contains("else if (email is not null && !existing.EmailVerified)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("existing.PhoneVerified", body, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(body, @"\bPhoneVerified\b"));   // سَطرُ الإنشاءِ وَحدَه
    }
}

// ═══ البُرهانُ الحَيّ — Postgres (‏WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres) ═══════════════════════
//
// المُعالِجُ نَفسُه الَّذي تُناديه نُقطَةُ `/{slug}/auth/phone/verify`، عَلى حِسابٍ بِشَكلِ ما يَكتُبُه المُستَورِد (مُعَرِّفٌ واسمٌ وجَوّالٌ بِلا عَلَمِ
// تَحَقُّق) في مُستَأجِرٍ مُؤَقَّتٍ يُمسَحُ بَعدَه — ثُمَّ بَوّابَةُ جِهَةِ التَواصُلِ بِوَثيقَتِه كَما تُقرَأُ بَعدَ الدُخول.

public class LivePhoneVerifiedOnLoginProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>حِسابٌ بِشَكلِ المُستَورِدِ يَدخُلُ بِرَمزٍ إلى جَوّالِه — ويُرجِعُ ما رَآه: مُعَرِّفُ الدُخول، والعَلَم، ورَمزُ البَوّابَة.</summary>
    internal static async Task<(Guid Imported, Guid? SignedIn, bool PhoneVerified, string? GateCode)> ImporterShapedPhoneLoginAsync(
        IDocumentStore store, string slug, string phone)
    {
        var imported = new User
        {
            Id = Guid.NewGuid(),
            TenantSlug = slug,
            FullName = "بائِعٌ مُستَورَد",
            Phone = phone,
            NationalId = null,
            CreatedAt = DateTime.UtcNow,
        };
        await using (var s = store.LightweightSession(slug)) { s.Store(imported); await s.SaveChangesAsync(); }

        const string code = "424242";
        AuthHandlers.IssueAttempt(slug, phone, code, AuthHandlers.AuthKind.PhoneOtp);
        var result = await AuthHandlers.VerifyPhoneOtpHandler(new VerifyPhoneOtp(phone, code),
            new VendorRegisterBucketTests.ResolvedTenant(slug), store);

        await using var q = store.QuerySession(slug);
        var after = await q.LoadAsync<User>(imported.Id);
        var vendor = ListingVendorGate.Admit(imported.Id, imported.FullName, 1).Vendor!;
        var admission = VendorContactGate.Admit(vendor, after);
        return (imported.Id, result?.UserId, after!.PhoneVerified, admission.Violation?.Code);
    }

    internal static async Task CleanAsync(IDocumentStore store, string slug)
    {
        await using var s = store.LightweightSession(slug);
        foreach (var u in await s.Query<User>().ToListAsync()) s.Delete(u);
        await s.SaveChangesAsync();
    }

    /// <summary><b>اليَوم</b>: الدُخولُ يَنجَحُ بِالحِسابِ القائِمِ نَفسِه — والعَلَمُ باقٍ <c>false</c> والبَوّابَةُ تَرُدُّ <c>vendor_contact_unverified</c>.</summary>
    [Fact]
    public async Task Today_an_importer_shaped_vendor_who_signs_in_by_phone_stays_unverified_and_the_contact_gate_refuses_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "pv-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var (imported, signedIn, verified, gate) = await ImporterShapedPhoneLoginAsync(store, slug, "966500000001");
            output.WriteLine($"الدُخول: {(signedIn == imported ? "الحِسابُ نَفسُه" : signedIn?.ToString() ?? "مَردود")} · PhoneVerified: {verified} · البَوّابَة: {gate ?? "تَقبَل"}");
            Assert.Equal(imported, signedIn);
            Assert.False(verified);
            Assert.Equal(VendorContactGate.ContactUnverified, gate);
        }
        finally { await CleanAsync(store, slug); }
    }
}
