using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveRoleOnboardingSaveCharacterizationTests;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ أَوَّلُ كِتابَةٍ لِعُضوٍ جَديدٍ عَلى مُضيفِ الدَورِ تَبلُغُ زِرَّ القَبولِ عَلى المُضيفِ نَفسِه — بِسِلسِلَةِ الحُرّاسِ المُسَجَّلَة (‏2026-11-07، مُراجَعَةُ C2b الجَولَةُ الأُولى · C2b-P-01) ═══
//
// **المَقيسُ قَبل** (حَيّاً عِندَ a9bc2cf): `POST /{slug}/listings/create` مِن `{slug}-vendor.localhost` ⇒ 302 `http://{slug}.localhost/{slug}/terms?…` — مُضيفُ المَتجَر، والكَعكَةُ
// لا تُرسَلُ إلَيه: صَفحَةُ شُروطٍ بِلا زِرِّ قَبول، فَلا يُنشَأُ الإعلانُ أَبَداً. **والآن** كُلُّ تَحويلٍ في السِلسِلَةِ `StoreUrl.For` عَلى المُضيفِ الَّذي يَخدِمُ الطَلَب:
// الشُروطُ `/{slug}/terms?returnUrl=…` نِسبِيَّة، والقَبولُ يُكتَبُ ويَعودُ إلى `/`، وبِلا جَلسَةٍ `/login?returnUrl=…` — كُلُّها عَلى مُضيفِ الدَور. يُنادى البابُ بِمُرَشِّحاتِه
// المُسَجَّلَة (`LiveTemplateDoorHost`) بِالرَأسِ `Host` وعَناصِرِ الحاقِن. يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres.

[Collection(LiveProofCollection.Name)]
public class LiveRoleHostWriteGateProofTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);
    private const string Base = "example.test";

    private static Action<HttpContext> OnRoleHost(string slug) => ctx =>
    {
        ctx.Request.Scheme = "https";
        ctx.Request.Host = new HostString($"{slug}-vendor.{Base}");
        ctx.Items[StoreHostItems.Apex] = Base;
        ctx.Items[StoreHostItems.Slug] = slug;
        ctx.Items[StoreHostItems.Role] = "vendor";
    };

    [Fact]
    public async Task A_new_vendors_first_write_on_the_role_host_reaches_the_accept_button_there_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var slug = NewSlug("w13rhw-");
        await PutStoreAsync(host, slug);
        var vendor = await PutMemberAsync(host, slug, "vendor");
        try
        {
            var route = new Dictionary<string, string> { ["slug"] = slug };
            var token = ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(vendor.Id, slug, vendor.SessionVersion);
            var generic = $"{AuthSession.CookieName(slug)}={token}";
            var create = await host.PostFormOnHostAsync("/{slug}/listings/create", route, generic, OnRoleHost(slug), ("as", "vendor"), ("title", "x"));
            var signedOut = await host.PostFormOnHostAsync("/{slug}/listings/create", route, null, OnRoleHost(slug), ("as", "vendor"), ("title", "x"));
            var accept = await host.PostFormOnHostAsync("/{slug}/terms/accept", route, generic, OnRoleHost(slug));
            var accepted = (await MemberAsync(host, slug, vendor.Id))!.AcceptedTermsVersion;
            output.WriteLine($"[live] POST {slug}-vendor.{Base}/listings/create ⇒ {create.Status} {create.Location} · بِلا جَلسَة ⇒ {signedOut.Status} {signedOut.Location}"
                + $" · /terms/accept ⇒ {accept.Status} {accept.Location} · الإصدارُ المَقبول: {accepted}");
            Assert.Equal((302, $"/{slug}/terms?returnUrl=%2F{slug}%2Flistings%2Fcreate"), create);
            Assert.Equal((302, $"/login?returnUrl=%2F{slug}%2Flistings%2Fcreate"), signedOut);
            Assert.Equal((302, "/"), accept);
            Assert.Equal(ACommerce.Templates.Customer.Marketplace.Gates.TermsPolicy.CurrentVersion, accepted);
        }
        finally { await DropAsync(host, slug, vendor); }
    }
}
