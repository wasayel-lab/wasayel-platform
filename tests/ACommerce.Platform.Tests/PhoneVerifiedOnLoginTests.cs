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
        // ‏(‏2026-10-28، د28) المِرساةُ بِلا قَوسِ الإغلاق: صارَ النِداءُ يُمَرِّرُ إصدارَ الجَلسَة (`existing.SessionVersion`).
        var end = code.IndexOf("var token = MakeToken(existing.Id, tenantSlug", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, $"أَداةٌ عَمياء: لَم يوجَد الجِسم ({start}/{end}).");
        return code[start..end];
    }

    /// <summary><b>قُلِبَ</b> (‏2026-10-13، G5 M2) — كانَ <c>Today_an_existing_account_gets_email_verified_raised_and_never_phone_verified</c> (‏<c>31f21c0</c>):
    /// الحِسابُ القائِمُ يُرفَعُ لَه ما أَثبَتَه دُخولُه بِدالَّةٍ واحِدَة — <c>RaiseVerified</c> — والجِسمُ لا يَكتُبُ عَلَماً بِنَفسِه بَعدَ الإنشاء.</summary>
    [Fact]
    public void An_existing_account_gets_the_channel_its_login_proved_raised_through_one_pure_function()
    {
        var body = GetOrCreateBody();
        output.WriteLine($"طولُ الجِسم: {body.Length} · ذِكرُ PhoneVerified: {body.Split("PhoneVerified").Length - 1}");
        Assert.Contains("else if (RaiseVerified(existing, email, nationalId))", body, StringComparison.Ordinal);
        Assert.DoesNotContain("existing.PhoneVerified", body, StringComparison.Ordinal);
        Assert.DoesNotContain("existing.EmailVerified", body, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(body, @"\bPhoneVerified\b"));   // سَطرُ الإنشاءِ وَحدَه
    }

    private static User Account(bool phoneVerified = false, bool emailVerified = false) => new()
    {
        Id = Guid.NewGuid(), TenantSlug = "zz", Phone = "966500000001", Email = "seller@example.com",
        PhoneVerified = phoneVerified, EmailVerified = emailVerified,
    };

    /// <summary><b>المُوجِب</b>: رَمزٌ إلى الجَوّالِ يَرفَعُ <c>PhoneVerified</c> لِحِسابٍ قائِمٍ بِدونِه — ولا يَمَسُّ البَريد؛ ورَمزٌ إلى البَريدِ يَرفَعُ
    /// <c>EmailVerified</c> — ولا يَمَسُّ الجَوّال (كَما كان).</summary>
    [Fact]
    public void A_login_raises_only_the_channel_it_proved_on_an_existing_account()
    {
        var byPhone = Account();
        Assert.True(AuthHandlers.RaiseVerified(byPhone, email: null, nationalId: null));
        Assert.Equal((true, false), (byPhone.PhoneVerified, byPhone.EmailVerified));

        var byEmail = Account();
        Assert.True(AuthHandlers.RaiseVerified(byEmail, email: "seller@example.com", nationalId: null));
        Assert.Equal((false, true), (byEmail.PhoneVerified, byEmail.EmailVerified));
    }

    /// <summary><b>السالِب</b>: ما رُفِعَ لا يُكتَبُ ثانِيَةً (لا حِفظ)، ونَفاذٌ لا يَرفَعُ شَيئاً — جَوّالُه لَم يُرسَل إلَيه رَمز.</summary>
    [Fact]
    public void A_login_raises_nothing_already_raised_and_nafath_raises_nothing()
    {
        var phoneDone = Account(phoneVerified: true);
        Assert.False(AuthHandlers.RaiseVerified(phoneDone, email: null, nationalId: null));
        var emailDone = Account(emailVerified: true);
        Assert.False(AuthHandlers.RaiseVerified(emailDone, email: "seller@example.com", nationalId: null));

        var nafath = Account();
        Assert.False(AuthHandlers.RaiseVerified(nafath, email: null, nationalId: "1000000001"));
        Assert.Equal((false, false), (nafath.PhoneVerified, nafath.EmailVerified));
    }
}

// ═══ البُرهانُ الحَيّ — Postgres (‏WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres) ═══════════════════════
//
// المُعالِجُ نَفسُه الَّذي تُناديه نُقطَةُ `/{slug}/auth/phone/verify`، عَلى حِسابٍ بِشَكلِ ما يَكتُبُه المُستَورِد (مُعَرِّفٌ واسمٌ وجَوّالٌ بِلا عَلَمِ
// تَحَقُّق) في مُستَأجِرٍ مُؤَقَّتٍ يُمسَحُ بَعدَه — ثُمَّ بَوّابَةُ جِهَةِ التَواصُلِ بِوَثيقَتِه كَما تُقرَأُ بَعدَ الدُخول.

[Collection(LiveProofCollection.Name)]
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

    /// <summary><b>قُلِبَ</b> (‏2026-10-13، G5 M2) — كانَ <c>Today_an_importer_shaped_vendor_who_signs_in_by_phone_stays_unverified_and_the_contact_gate_refuses_on_Postgres</c>:
    /// الدُخولُ بِالحِسابِ القائِمِ نَفسِه يَرفَعُ <c>PhoneVerified</c>، وبَوّابَةُ جِهَةِ التَواصُلِ تَقبَلُ البائِع.</summary>
    [Fact]
    public async Task An_importer_shaped_vendor_who_signs_in_by_phone_is_verified_and_the_contact_gate_admits_them_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "pv-zz-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var (imported, signedIn, verified, gate) = await ImporterShapedPhoneLoginAsync(store, slug, "966500000001");
            output.WriteLine($"الدُخول: {(signedIn == imported ? "الحِسابُ نَفسُه" : signedIn?.ToString() ?? "مَردود")} · PhoneVerified: {verified} · البَوّابَة: {gate ?? "تَقبَل"}");
            Assert.Equal(imported, signedIn);
            Assert.True(verified);
            Assert.Null(gate);
        }
        finally { await CleanAsync(store, slug); }
    }

    private sealed class ApprovingNafath : INafathChannel
    {
        public string ChannelName => "approving-nafath";
        public Task<NafathStartResult> StartAsync(string nationalId, CancellationToken ct)
            => Task.FromResult(new NafathStartResult(Guid.NewGuid().ToString("N"), "00", 0));
        public Task<bool> IsApprovedAsync(string attemptId, CancellationToken ct) => Task.FromResult(true);
    }

    /// <summary><b>والسالِبُ حَيّاً</b>: حِسابٌ بِشَكلِ المُستَورِدِ لَه هُوِيَّةٌ وجَوّال، يَدخُلُ بِنَفاذ — الحِسابُ نَفسُه، ولا يُرفَعُ <c>PhoneVerified</c>
    /// والبَوّابَةُ تَرُدّ: نَفاذٌ لا يُثبِتُ جَوّالاً.</summary>
    [Fact]
    public async Task An_importer_shaped_vendor_who_signs_in_by_nafath_stays_phone_unverified_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = LiveDealConcurrencyProofTests.BuildStore();
        var slug = "pv-zz-n-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var imported = new User
            {
                Id = Guid.NewGuid(), TenantSlug = slug, FullName = "بائِعٌ مُستَورَد",
                Phone = "966500000002", NationalId = "1000000001", CreatedAt = DateTime.UtcNow,
            };
            await using (var s = store.LightweightSession(slug)) { s.Store(imported); await s.SaveChangesAsync(); }

            var nafath = new ApprovingNafath();
            // ‏(‏2026-10-30، الموجَة ١٢ F1 البَند ٥ — A-SESS-1؛ التَبديلُ `022f18a`) مَتجَرُ نَفاذ: كانَ `ResolvedTenant` (قَناتُه `phone`)، ومُعالِجُ التَأكيدِ
            // صارَ يَرُدُّ مَتجَراً لا يُدخِلُ نَفاذاً (`AuthHandlers.NafathSignsIn`) — فَالدُخولُ بِنَفاذٍ لا يَكونُ إلّا في مَتجَرٍ قَناتُه نَفاذ، وهو ما يَقيسُه الصَفّ.
            var tenant = new LoginChannelsTests.ChannelTenant(slug, ACommerce.Kit.Auth.AuthChannels.Nafath);
            var pending = await AuthHandlers.RequestNafathHandler(new RequestNafath("1000000001"), tenant, nafath, CancellationToken.None);
            var result = await AuthHandlers.VerifyNafathHandler(new VerifyNafath(pending.AttemptId, "1000000001"), tenant, nafath, store, CancellationToken.None);

            await using var q = store.QuerySession(slug);
            var after = (await q.LoadAsync<User>(imported.Id))!;
            var gate = VendorContactGate.Admit(ListingVendorGate.Admit(imported.Id, imported.FullName, 1).Vendor!, after).Violation?.Code;
            output.WriteLine($"الدُخول: {(result?.UserId == imported.Id ? "الحِسابُ نَفسُه" : result?.UserId.ToString() ?? "مَردود")} · PhoneVerified: {after.PhoneVerified} · البَوّابَة: {gate ?? "تَقبَل"}");
            Assert.Equal(imported.Id, result?.UserId);
            Assert.False(after.PhoneVerified);
            Assert.Equal(VendorContactGate.ContactUnverified, gate);
        }
        finally { await CleanAsync(store, slug); }
    }
}
