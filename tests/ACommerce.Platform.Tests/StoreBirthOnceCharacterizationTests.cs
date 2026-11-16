using ACommerce.Kit.Auth.Server;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «المَتجَرُ يولَدُ مَرَّةً» قَبلَ أَن يُغلَقَ سِباقُ المُعَرِّفِ الواحِد (‏2026-11-07، الموجَة ١٣ — مُراجَعَةُ تَيّارِ C1 · W13-1·F2) ═══
//
// **المَقيسُ قَبل** (عِندَ `71ca286`، عَلى إقلاعٍ بارِدٍ بِقاعِدَةٍ فارِغَة): البابُ يَسأَلُ `ValidateSlugAsync` ثُمَّ يَكتُبُ بِـ`Store` — وهُوَ upsert. فَحِسابانِ
// وافَقا ونَشَرا `POST /studio/apps/create` بِمُعَرِّفٍ واحِدٍ في اللَحظَةِ نَفسِها، سِتَّ جَولات: الرُدودُ الاثنا عَشَرَ كُلُّها `302 …?built=1`، وفي Postgres
// صَفٌّ واحِدٌ لِكُلِّ مُعَرِّف (المالِكُ الأَوَّلُ لِأَربَعٍ والثاني لِاثنَتَين)، و`StoresBuilt` لِكُلٍّ مِنهُما 6. ونَقرَتانِ مُتَزامِنَتانِ مِن صاحِبٍ واحِدٍ: كِلتاهُما
// `built=1` والعَدّادُ +2. وبابُ المُشرِفِ (`/admin/tenants/create`) بِالنَمَطِ نَفسِه: أَربَعُ جَولاتٍ مُتَزامِنَة، كُلُّها `302 /admin` مَرَّتَين وصَفٌّ واحِدٌ
// يَحمِلُ اسمَ آخِرِ كاتِب. وأَداةُ الوَكيلِ `create_tenant` تَسأَلُ `LoadAsync` ثُمَّ `Store` كَذلك.
//
// **وما يُثَبِّتُه هذا المِلَفُّ — وهُوَ ما يَجِبُ أَن يَنجوَ مِن الإصلاح، ولا يُعادُ حَرفٌ مِنه بَعدَه** (القاعِدَة ٣): **جَوابُ الخاسِرِ المُتَتابِع** في
// الأَبوابِ الثَلاثَة — مَن جاءَ ثانِياً بَعدَ أَن كُتِبَ المَتجَرُ يُرَدُّ بِرَمزِ `slug_taken` بِحَرفِ وِجهَتِه أَو رِسالَتِه، ولا يُمَسُّ المَتجَرُ القائِم،
// ولا يَزيدُ عَدّادُه. والإصلاحُ (`Insert` لا `Store`) يَجعَلُ الخاسِرَ المُتَزامِنَ يُجابُ بِهذا الجَوابِ بِعَينِه — فَهُوَ مِرآتُه.
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres — عَقدُ إخوَتِه، وفي مَجموعَتِهِم التَسلسُلِيَّة.

[Collection(LiveProofCollection.Name)]
public class LiveStoreBirthOnceCharacterizationTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    internal sealed record Probe(StudioUser User, string Token);

    internal static async Task<Probe> UserAsync(LiveTemplateDoorHost host, bool consented = true, bool platformAdmin = false)
    {
        var user = new StudioUser
        {
            Id = Guid.NewGuid(), Phone = "zz-w13r-" + Guid.NewGuid().ToString("N")[..8], FullName = "صاحِبُ المُعَرِّف",
            Tier = "spark", IsPlatformAdmin = platformAdmin,
        };
        await using var s = host.Store.LightweightSession(StudioAuth.Tenant);
        s.Store(user);
        if (consented)
            s.Store(new ConsentRecord { Id = Guid.NewGuid(), UserId = user.Id, Version = ConsentPolicy.CurrentVersion, At = DateTime.UtcNow });
        await s.SaveChangesAsync();
        return new Probe(user, AuthHandlers.MakeToken(user.Id, StudioAuth.Tenant, user.SessionVersion));
    }

    internal static async Task CleanAsync(LiveTemplateDoorHost host, IEnumerable<Probe> people, params string[] slugs)
    {
        await using (var s = host.Store.LightweightSession()) { foreach (var slug in slugs) s.Delete<Tenant>(slug); await s.SaveChangesAsync(); }
        await using (var s = host.Store.LightweightSession(StudioAuth.Tenant))
        {
            foreach (var p in people)
            {
                s.DeleteWhere<ConsentRecord>(c => c.UserId == p.User.Id);
                s.Delete<StudioUser>(p.User.Id);
            }
            await s.SaveChangesAsync();
        }
    }

    internal static async Task<int> StoresBuiltAsync(LiveTemplateDoorHost host, Probe p)
    {
        await using var q = host.Store.QuerySession(StudioAuth.Tenant);
        return (await q.LoadAsync<StudioUser>(p.User.Id))!.StoresBuilt;
    }

    internal static async Task<Tenant?> TenantAsync(LiveTemplateDoorHost host, string slug)
    {
        await using var q = host.Store.QuerySession();
        return await q.LoadAsync<Tenant>(slug);
    }

    internal static LiveTemplateDoorHost HostWithPhone(Action<Marten.StoreOptions>? marten = null)
        => LiveTemplateDoorHost.Build(marten, s => s.AddSingleton<ACommerce.Kit.Auth.IOtpChannel>(new OtpSendQuotaTests.RecordingSms()));

    internal static string NewSlug(string prefix) => (prefix + Guid.NewGuid().ToString("N"))[..18];

    // ─── بابُ الاستوديو ─────────────────────────────────────────────────────────────────────────────────────

    internal static (string, string)[] StudioForm(string slug, string name) =>
        [("slug", slug), ("name", name), ("color", StudioStoreCreate.DefaultColor), ("tagline", "")];

    internal static Task<(int Status, string Location)> StudioCreateAsync(LiveTemplateDoorHost host, Probe p, string slug, string name)
        => host.PostStudioFormAsync(StudioStoreCreate.Endpoint, new Dictionary<string, string>(), p.Token, StudioForm(slug, name));

    /// <summary>جَوابُ الخاسِرِ في بابِ الاستوديو بِحَرفِه: الاستِمارَةُ بِرَمزِ <c>slug_taken</c> وبِما كُتِب.</summary>
    internal static string StudioSlugTaken(string slug, string name)
        => StudioStoreCreate.Refused(TenantFromAnalysisFactory.SlugTaken, new StudioStoreCreate.Form(slug, name, StudioStoreCreate.DefaultColor, ""));

    /// <summary>نَقرَتانِ مُتَتالِيَتانِ مِن صاحِبٍ واحِد: الأُولى تَبني، والثانِيَةُ تُرَدُّ إلى الاستِمارَةِ بِـ<c>slug_taken</c> — مَتجَرٌ واحِدٌ لَه، وعَدّادُه 1.</summary>
    [Fact]
    public async Task A_second_create_of_the_same_slug_by_its_owner_is_slug_taken_and_counts_once_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        var slug = NewSlug("w13o-");
        try
        {
            var first = await StudioCreateAsync(host, a, slug, "الأَوَّل");
            var second = await StudioCreateAsync(host, a, slug, "الثاني");
            var built = await StoresBuiltAsync(host, a);
            var t = await TenantAsync(host, slug);
            output.WriteLine($"[live] الأُولى ⇒ {first.Status} {first.Location} · الثانِيَة ⇒ {second.Status} {second.Location} · العَدّاد {built} · الاسم {t?.Name}");
            Assert.Equal((302, $"/studio/apps/{slug}?built=1"), first);
            Assert.Equal((302, StudioSlugTaken(slug, "الثاني")), second);
            Assert.Equal(1, built);
            Assert.Equal(("الأَوَّل", a.User.Id), (t!.Name, t.OwnerUserId));
        }
        finally { await CleanAsync(host, [a], slug); }
    }

    /// <summary>مُعَرِّفٌ يَملِكُه آخَر: يُرَدُّ بِـ<c>slug_taken</c>، والمَتجَرُ لِصاحِبِه بِاسمِه، وعَدّادُ الثاني صِفر.</summary>
    [Fact]
    public async Task A_create_of_a_slug_another_owner_holds_is_slug_taken_and_leaves_the_store_and_both_counters_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        var b = await UserAsync(host);
        var slug = NewSlug("w13h-");
        try
        {
            var first = await StudioCreateAsync(host, a, slug, "مَتجَرُ الأَوَّل");
            var second = await StudioCreateAsync(host, b, slug, "مَتجَرُ الثاني");
            var t = await TenantAsync(host, slug);
            output.WriteLine($"[live] أ ⇒ {first.Location} · ب ⇒ {second.Location} · المالِكُ أ={t?.OwnerUserId == a.User.Id} · العَدّادان {await StoresBuiltAsync(host, a)}/{await StoresBuiltAsync(host, b)}");
            Assert.Equal((302, $"/studio/apps/{slug}?built=1"), first);
            Assert.Equal((302, StudioSlugTaken(slug, "مَتجَرُ الثاني")), second);
            Assert.Equal(("مَتجَرُ الأَوَّل", a.User.Id), (t!.Name, t.OwnerUserId));
            Assert.Equal((1, 0), (await StoresBuiltAsync(host, a), await StoresBuiltAsync(host, b)));
        }
        finally { await CleanAsync(host, [a, b], slug); }
    }

    // ─── بابُ المُشرِف ──────────────────────────────────────────────────────────────────────────────────────

    internal const string AdminCreate = "/admin/tenants/create";
    internal const string AdminCategories = "products | مُنتَجات | 🛍️ |";

    internal static (string, string)[] AdminForm(string slug, string name) =>
        [("slug", slug), ("name", name), ("tagline", ""), ("color", StudioStoreCreate.DefaultColor), ("city", ""), ("channel", "phone"),
         ("categories", AdminCategories)];

    internal static Task<(int Status, string Location)> AdminCreateAsync(LiveTemplateDoorHost host, Probe admin, string slug, string name)
        => host.PostStudioFormAsync(AdminCreate, new Dictionary<string, string>(), admin.Token, AdminForm(slug, name));

    /// <summary>جَوابُ الخاسِرِ في بابِ المُشرِفِ بِحَرفِه: الصَفحَةُ بِرَمزِ <c>slug_taken</c> وبِما كُتِب.</summary>
    internal static string AdminSlugTaken(string slug, string name)
        => "/admin/tenants/new?err=slug_taken&slug=" + Uri.EscapeDataString(slug) + "&name=" + Uri.EscapeDataString(name)
         + "&tagline=&color=" + Uri.EscapeDataString(StudioStoreCreate.DefaultColor) + "&city=&channel=phone&categories=" + Uri.EscapeDataString(AdminCategories);

    [Fact]
    public async Task A_second_admin_create_of_the_same_slug_returns_slug_taken_and_keeps_the_first_store_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var admin = await UserAsync(host, consented: false, platformAdmin: true);
        var slug = NewSlug("w13a-");
        try
        {
            var first = await AdminCreateAsync(host, admin, slug, "الأَوَّل");
            var second = await AdminCreateAsync(host, admin, slug, "الثاني");
            var t = await TenantAsync(host, slug);
            output.WriteLine($"[live] الأُولى ⇒ {first.Status} {first.Location} · الثانِيَة ⇒ {second.Status} {second.Location} · الاسم {t?.Name}");
            Assert.Equal((302, "/admin"), first);
            Assert.Equal((302, AdminSlugTaken(slug, "الثاني")), second);
            Assert.Equal(("الأَوَّل", admin.User.Id), (t!.Name, t.OwnerUserId));
        }
        finally { await CleanAsync(host, [admin], slug); }
    }

    // ─── أَداةُ الوَكيل ─────────────────────────────────────────────────────────────────────────────────────

    internal static string AgentPayload(string slug, string name)
        => $$"""{"slug":"{{slug}}","name":"{{name}}","color":"#1d4ed8","channel":"phone","categories":[{"slug":"general","label":"عامّ"}]}""";

    internal static string AgentCreated(string slug) => $"تَمّ إنشاء «{slug}» بِـ 1 فِئَات.";
    internal static string AgentSlugTaken(string slug) => $"الـ slug «{slug}» مَوجود بِالفِعل.";

    [Fact]
    public async Task A_second_create_tenant_of_the_same_slug_is_refused_and_counts_once_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        var slug = NewSlug("w13t-");
        try
        {
            var exec = new AgentToolExecutor(host.Store, new TenantRoleService(host.Store));
            var first = await exec.ExecuteAsync("create_tenant", AgentPayload(slug, "الأول"), a.User.Id);
            var second = await exec.ExecuteAsync("create_tenant", AgentPayload(slug, "الثاني"), a.User.Id);
            var built = await StoresBuiltAsync(host, a);
            var t = await TenantAsync(host, slug);
            output.WriteLine($"[live] الأُولى ⇒ {first} · الثانِيَة ⇒ {second} · العَدّاد {built} · الاسم {t?.Name}");
            Assert.Equal((true, AgentCreated(slug)), first);
            Assert.Equal((false, AgentSlugTaken(slug)), second);
            Assert.Equal(1, built);
            Assert.Equal(("الأول", a.User.Id), (t!.Name, t.OwnerUserId));
        }
        finally { await CleanAsync(host, [a], slug); }
    }
}
