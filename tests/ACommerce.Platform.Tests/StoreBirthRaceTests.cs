using System.Text.RegularExpressions;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ المَتجَرُ يولَدُ مَرَّةً: مُعَرِّفٌ واحِدٌ يَكتُبُه كاتِبٌ واحِد، والخاسِرُ يُجابُ كَمَن جاءَ ثانِياً (‏2026-11-07، الموجَة ١٣ — مُراجَعَةُ C1 · W13-1·F2) ═══
//
// **المَقيسُ قَبل** (عِندَ `71ca286`، إقلاعٌ بارِد): كُلُّ بابٍ يُنشِئُ مَتجَراً يَسأَلُ «أَمَوجودٌ؟» ثُمَّ يَكتُبُ بِـ`Store` — وهُوَ upsert، فَما بَينَ السُؤالِ
// والكِتابَةِ نافِذَةٌ يَدهَسُ فيها الثاني الأَوَّل: 12/12 `built=1` لِحِسابَينِ في سِتِّ جَولات، وصَفٌّ واحِدٌ لِكُلِّ مُعَرِّف، وعَدّادُ كُلٍّ مِنهُما 6؛
// ونَقرَتانِ مِن صاحِبٍ واحِدٍ تَعُدّانِ 2. وبابُ المُشرِفِ وأَداةُ الوَكيلِ بِالشَكلِ نَفسِه.
// **والعِلاجُ في الجَذر**: مِفتاحُ الوَثيقَةِ قُفلٌ حَقيقيٌّ والسُؤالُ قَبلَه ظَنّ (‏`ApiIdempotency.TryBeginAsync` بِحَرفِه) — فَالأَبوابُ الثَلاثَةُ تَكتُبُ
// بِـ`Insert`، وMarten يَرمي `JasperFx.DocumentAlreadyExistsException` لِلثاني، فَيُجابُ بِرَمزِ `slug_taken` الَّذي يُجابُ بِه الخاسِرُ المُتَتابِعُ بِحَرفِه
// (‏`LiveStoreBirthOnceCharacterizationTests` — لا يُمَسّ)، ولا يُعَدُّ لَه مَتجَر. والوَثيقَةُ المَكتوبَةُ هِيَ هِيَ: بَصمَةُ التَوصيفِ لا تَتَحَرَّك.

public class StoreBirthRaceTests(ITestOutputHelper output)
{
    private const string Template = "libs/templates/ACommerce.Templates.Customer.Marketplace";

    private static string Read(string rel) => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, rel));

    /// <summary>جِسمُ دالَّةٍ مِن تَوقيعِها إلى ما يَليها.</summary>
    private static string Slice(string code, string from, string to)
    {
        var start = code.IndexOf(from, StringComparison.Ordinal);
        var end = code.IndexOf(to, start + 1, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, $"«{from}» لَم يوجَد — الأَداةُ عَمياء.");
        return code[start..end];
    }

    /// <summary>كاتِبُ المَتجَرِ في كُلِّ باب: الكِتابَةُ (<c>Insert</c> أَو <c>Store</c>) لِوَثيقَةِ <c>Tenant</c> الجَديدَة، ومَوضِعُ التِقاطِ التَصادُم،
    /// ومَوضِعُ جَوابِه بِـ<c>slug_taken</c>.</summary>
    internal sealed record Writer(string Door, int Insert, int Upsert, int Catch, int Answer);

    private static readonly Regex Upsert = new(@"\.Store\((?:tenant|new (?:ACommerce\.Kit\.Tenants\.)?Tenant\b)", RegexOptions.Compiled);
    private static readonly Regex Insert = new(@"\.Insert\((?:tenant|new (?:ACommerce\.Kit\.Tenants\.)?Tenant\b)", RegexOptions.Compiled);
    private const string Collision = "catch (JasperFx.DocumentAlreadyExistsException)";

    internal static Writer Scan(string door, string body, string answer)
    {
        int At(Regex r) => r.Match(body) is { Success: true } m ? m.Index : -1;
        var c = body.IndexOf(Collision, StringComparison.Ordinal);
        var a = c < 0 ? -1 : body.IndexOf(answer, c, StringComparison.Ordinal);
        return new Writer(door, At(Insert), At(Upsert), c, a);
    }

    /// <summary>كاتِبٌ سَليم: يُدرِجُ ولا يَدهَس، ويَلتَقِطُ التَصادُمَ ويُجيبُ بَعدَه بِـ<c>slug_taken</c>.</summary>
    internal static bool Sound(Writer w) => w.Insert >= 0 && w.Upsert < 0 && w.Catch >= 0 && w.Answer > w.Catch;

    internal static IReadOnlyList<Writer> Writers()
    {
        var ext = Read($"{Template}/MarketplaceTemplateExtensions.cs");
        var studio = WriteEndpointGuardTests.AllMinimalApiEndpoints().Single(e => e.Route == StudioStoreCreate.Endpoint).Body;
        var factory = Slice(Read($"{Template}/Services/Incubator/TenantFromAnalysisFactory.cs"), "public async Task<Tenant> CreateAsync(", "// ─── Helpers");
        var admin = Slice(ext, "app.MapPost(\"/admin/tenants/create\"", "app.MapPost(\"/admin/tenants/{slug}/users/");
        var agent = AgentStoreLimitRefusalTests.CreateTenantBody();
        // المَصنَعُ يُدرِجُ ولا يَلتَقِط — بابُه يَلتَقِطُ ويُجيب؛ فَيُقاسانِ مَعاً كاتِباً واحِداً: الإدراجُ مِن المَصنَعِ والجَوابُ مِن الباب.
        var f = Scan("factory", factory, "");
        var s = Scan(StudioStoreCreate.Endpoint, studio, "TenantFromAnalysisFactory.SlugTaken");
        return
        [
            new Writer($"{StudioStoreCreate.Endpoint} ← factory.CreateAsync", f.Insert, Math.Max(f.Upsert, s.Upsert), s.Catch, s.Answer),
            Scan("/admin/tenants/create", admin, "Back(\"slug_taken\")"),
            Scan("create_tenant", agent, "(false, taken)"),
        ];
    }

    [Fact]
    public void Every_door_that_births_a_store_inserts_it_and_answers_a_collision_with_slug_taken()
    {
        var writers = Writers();
        foreach (var w in writers) output.WriteLine($"{w.Door}: Insert@{w.Insert} · Store@{w.Upsert} · catch@{w.Catch} · slug_taken@{w.Answer} ⇒ {(Sound(w) ? "سَليم" : "يَدهَس")}");
        Assert.Equal(3, writers.Count);
        var broken = writers.Where(w => !Sound(w)).Select(w => w.Door).ToList();
        Assert.True(broken.Count == 0, "بابٌ يَلِدُ مَتجَراً بِكِتابَةٍ تَدهَسُ أَو بِلا جَوابٍ لِلتَصادُم: " + string.Join("، ", broken));
    }

    /// <summary>وفي بابِ الاستوديو: التَصادُمُ يُجابُ <b>قَبلَ</b> العَدّ — لا حِصَّةَ تُنفَقُ عَلى مَتجَرٍ لَم يُكتَب.</summary>
    [Fact]
    public void The_studio_door_answers_the_collision_before_it_counts_the_store()
    {
        var body = WriteEndpointGuardTests.AllMinimalApiEndpoints().Single(e => e.Route == StudioStoreCreate.Endpoint).Body;
        var create = body.IndexOf("factory.CreateAsync(", StringComparison.Ordinal);
        var collision = body.IndexOf(Collision, StringComparison.Ordinal);
        var count = body.IndexOf("tier.RecordStoreBuiltAsync(ownerId)", StringComparison.Ordinal);
        output.WriteLine($"الإنشاء@{create} ← التِقاطُ التَصادُم@{collision} ← العَدّ@{count}");
        Assert.True(create > 0 && collision > create && count > collision, "العَدُّ قَبلَ جَوابِ التَصادُمِ أَو بِلا جَواب.");
    }

    /// <summary><b>الماسِحُ يُقاسُ قَبلَ أَن يُوثَقَ بِه</b> (القاعِدَة ١٠): الكاتِبُ السَليمُ يَمُرّ، وكُلُّ عَيبٍ مَحقونٍ يُحمِرُّه.</summary>
    [Theory]
    [InlineData("s.Insert(new Tenant { Id = x }); try { await s.SaveChangesAsync(); } catch (JasperFx.DocumentAlreadyExistsException) { return Back(\"slug_taken\"); }", true)]
    [InlineData("s.Store(new Tenant { Id = x }); try { await s.SaveChangesAsync(); } catch (JasperFx.DocumentAlreadyExistsException) { return Back(\"slug_taken\"); }", false)]
    [InlineData("s.Insert(new ACommerce.Kit.Tenants.Tenant { Id = x }); await s.SaveChangesAsync();", false)]
    [InlineData("s.Insert(new Tenant { Id = x }); try { await s.SaveChangesAsync(); } catch (JasperFx.DocumentAlreadyExistsException) { return Ok(); }", false)]
    [InlineData("try { await s.SaveChangesAsync(); } catch (JasperFx.DocumentAlreadyExistsException) { return Back(\"slug_taken\"); }", false)]
    [InlineData("ws.Insert(tenant); ws.Store(tenant); try { } catch (JasperFx.DocumentAlreadyExistsException) { return Back(\"slug_taken\"); }", false)]
    public void The_writer_scanner_goes_red_on_an_upsert_or_an_unanswered_collision(string body, bool sound)
        => Assert.Equal(sound, Sound(Scan("عَيِّنَة", body, "Back(\"slug_taken\")")));
}

// ─── البُرهانُ عَلى Postgres ────────────────────────────────────────────────────────────────────────────────

[Collection(LiveProofCollection.Name)]
public class LiveStoreBirthRaceProofTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary><b>المُنافِسُ يَكتُبُ بَينَ سُؤالِ الباب وكِتابَتِه</b> — قَبلَ أَوَّلِ حِفظٍ يَحمِلُ وَثيقَةَ <c>Tenant</c> بِهذا المُعَرِّف، تُكتَبُ وَثيقَةُ المُنافِسِ
    /// في جَلسَةٍ أُخرى ثُمَّ يُتابِعُ الحِفظ. فَالسِباقُ تَرتيبٌ حَتمِيٌّ لا حَظّ، والخاسِرُ هُوَ صاحِبُ الطَلَب.</summary>
    internal sealed class CompetitorBeforeSave(string slug) : Marten.DocumentSessionListenerBase
    {
        private int _fired;
        public Func<Task> Write { get; set; } = () => Task.CompletedTask;

        public override async Task BeforeSaveChangesAsync(Marten.IDocumentSession session, CancellationToken token)
        {
            if (session.PendingChanges.AllChangedFor<Tenant>().Any(t => t.Id == slug) && Interlocked.Exchange(ref _fired, 1) == 0)
                await Write();
        }
    }

    private static Func<Task> Competitor(Func<Marten.IDocumentStore> store, string slug, Guid owner, string name) => async () =>
    {
        await using var s = store().LightweightSession();
        s.Insert(new Tenant { Id = slug, Name = name, OwnerUserId = owner, CreatedAt = DateTime.UtcNow });
        await s.SaveChangesAsync();
    };

    /// <summary>بابُ الاستوديو: المُنافِسُ كَتَبَ بَعدَ سُؤالِ الباب — صاحِبُ الطَلَبِ يُجابُ بِـ<c>slug_taken</c> بِحَرفِ التَوصيف، ومَتجَرُ المُنافِسِ لَه بِاسمِه، وعَدّادُ
    /// صاحِبِ الطَلَبِ صِفر.</summary>
    [Fact]
    public async Task A_competitor_that_writes_between_the_check_and_the_insert_wins_and_the_studio_door_answers_slug_taken_on_Postgres()
    {
        if (!Enabled) return;
        var slug = NewSlug("w13r-");
        var listener = new CompetitorBeforeSave(slug);
        await using var host = HostWithPhone(o => o.Listeners.Add(listener));
        var a = await UserAsync(host);
        var b = await UserAsync(host);
        listener.Write = Competitor(() => host.Store, slug, b.User.Id, "مَتجَرُ ب");
        try
        {
            var (status, location) = await StudioCreateAsync(host, a, slug, "مَتجَرُ أ");
            var t = await TenantAsync(host, slug);
            output.WriteLine($"[live] أ ⇒ {status} {location} · المَتجَرُ «{t?.Name}» مالِكُه ب={t?.OwnerUserId == b.User.Id} · عَدّادُ أ {await StoresBuiltAsync(host, a)}");
            Assert.Equal((302, StudioSlugTaken(slug, "مَتجَرُ أ")), (status, location));
            Assert.Equal(("مَتجَرُ ب", b.User.Id), (t!.Name, t.OwnerUserId));
            Assert.Equal(0, await StoresBuiltAsync(host, a));
        }
        finally { await CleanAsync(host, [a, b], slug); }
    }

    /// <summary>سِباقٌ حَقيقيٌّ بِلا تَرتيب: حِسابانِ مُوافِقانِ يُرسِلانِ مُعَرِّفاً واحِداً مَعاً، سِتَّ جَولات — في كُلِّ جَولَةٍ مَتجَرٌ واحِدٌ و<c>built=1</c> واحِدٌ
    /// و<c>slug_taken</c> واحِد، والمالِكُ هُوَ مَن قيلَ لَه «بُني»، ومَجموعُ العَدّادَينِ عَدَدُ الجَولات.</summary>
    [Fact]
    public async Task Two_owners_racing_one_slug_get_one_store_one_built_and_one_slug_taken_every_round_on_Postgres()
    {
        if (!Enabled) return;
        const int rounds = 6;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        var b = await UserAsync(host);
        var slugs = Enumerable.Range(1, rounds).Select(_ => NewSlug("w13x-")).ToArray();
        try
        {
            foreach (var slug in slugs)
            {
                var both = await Task.WhenAll(StudioCreateAsync(host, a, slug, "أ"), StudioCreateAsync(host, b, slug, "ب"));
                var t = await TenantAsync(host, slug);
                output.WriteLine($"[live] {slug}: أ ⇒ {both[0].Location} · ب ⇒ {both[1].Location} · المالِك {(t?.OwnerUserId == a.User.Id ? "أ" : t?.OwnerUserId == b.User.Id ? "ب" : "؟")}");
                var built = $"/studio/apps/{slug}?built=1";
                var aWon = both[0] == (302, built);
                Assert.True(aWon ^ (both[1] == (302, built)), "جَولَةٌ بِلا فائِزٍ أَو بِفائِزَين.");
                Assert.Equal((302, aWon ? StudioSlugTaken(slug, "ب") : StudioSlugTaken(slug, "أ")), aWon ? both[1] : both[0]);
                Assert.Equal(aWon ? a.User.Id : b.User.Id, t!.OwnerUserId);
                Assert.Equal(aWon ? "أ" : "ب", t.Name);
            }
            var (ba, bb) = (await StoresBuiltAsync(host, a), await StoresBuiltAsync(host, b));
            output.WriteLine($"[live] العَدّادان أ={ba} ب={bb} · الجَولات {rounds}");
            Assert.Equal(rounds, ba + bb);
        }
        finally { await CleanAsync(host, [a, b], slugs); }
    }

    /// <summary>نَقرَتانِ مُتَزامِنَتانِ مِن صاحِبٍ واحِد: مَتجَرٌ واحِد، و<c>built=1</c> واحِدٌ و<c>slug_taken</c> واحِد، والعَدّادُ 1.</summary>
    [Fact]
    public async Task A_double_submit_by_one_owner_builds_one_store_and_counts_one_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        var slug = NewSlug("w13d-");
        try
        {
            var both = await Task.WhenAll(StudioCreateAsync(host, a, slug, "أ"), StudioCreateAsync(host, a, slug, "أ"));
            var built = await StoresBuiltAsync(host, a);
            output.WriteLine($"[live] النَقرَتان ⇒ {both[0].Location} · {both[1].Location} · العَدّاد {built}");
            Assert.Equal(1, both.Count(r => r == (302, $"/studio/apps/{slug}?built=1")));
            Assert.Equal(1, both.Count(r => r == (302, StudioSlugTaken(slug, "أ"))));
            Assert.Equal(1, built);
        }
        finally { await CleanAsync(host, [a], slug); }
    }

    /// <summary>بابُ المُشرِف: المُنافِسُ كَتَبَ بَينَ سُؤالِ البابِ وكِتابَتِه — الصَفحَةُ بِـ<c>slug_taken</c> بِحَرفِ التَوصيف، ومَتجَرُ المُنافِسِ باقٍ بِاسمِه.</summary>
    [Fact]
    public async Task A_competitor_that_writes_between_the_check_and_the_insert_wins_and_the_admin_door_answers_slug_taken_on_Postgres()
    {
        if (!Enabled) return;
        var slug = NewSlug("w13q-");
        var listener = new CompetitorBeforeSave(slug);
        await using var host = HostWithPhone(o => o.Listeners.Add(listener));
        var admin = await UserAsync(host, consented: false, platformAdmin: true);
        var other = Guid.NewGuid();
        listener.Write = Competitor(() => host.Store, slug, other, "مَتجَرُ المُنافِس");
        try
        {
            var (status, location) = await AdminCreateAsync(host, admin, slug, "مَتجَرُ المُشرِف");
            var t = await TenantAsync(host, slug);
            output.WriteLine($"[live] المُشرِف ⇒ {status} {location} · المَتجَرُ «{t?.Name}»");
            Assert.Equal((302, AdminSlugTaken(slug, "مَتجَرُ المُشرِف")), (status, location));
            Assert.Equal(("مَتجَرُ المُنافِس", other), (t!.Name, t.OwnerUserId));
        }
        finally { await CleanAsync(host, [admin], slug); }
    }

    /// <summary>أَداةُ الوَكيل: المُنافِسُ كَتَبَ بَينَ سُؤالِها وكِتابَتِها — رِسالَةُ «مَوجود بِالفِعل» بِحَرفِ التَوصيف، ولا يُعَدُّ مَتجَر.</summary>
    [Fact]
    public async Task A_competitor_that_writes_between_the_check_and_the_insert_wins_and_create_tenant_answers_slug_taken_on_Postgres()
    {
        if (!Enabled) return;
        var slug = NewSlug("w13v-");
        var listener = new CompetitorBeforeSave(slug);
        await using var host = HostWithPhone(o => o.Listeners.Add(listener));
        var a = await UserAsync(host);
        var other = Guid.NewGuid();
        listener.Write = Competitor(() => host.Store, slug, other, "مَتجَرُ المُنافِس");
        try
        {
            var exec = new AgentToolExecutor(host.Store, new TenantRoleService(host.Store));
            var result = await exec.ExecuteAsync("create_tenant", AgentPayload(slug, "الأول"), a.User.Id);
            var t = await TenantAsync(host, slug);
            output.WriteLine($"[live] create_tenant ⇒ {result} · المَتجَرُ «{t?.Name}» · العَدّاد {await StoresBuiltAsync(host, a)}");
            Assert.Equal((false, AgentSlugTaken(slug)), result);
            Assert.Equal(("مَتجَرُ المُنافِس", other), (t!.Name, t.OwnerUserId));
            Assert.Equal(0, await StoresBuiltAsync(host, a));
        }
        finally { await CleanAsync(host, [a], slug); }
    }
}
