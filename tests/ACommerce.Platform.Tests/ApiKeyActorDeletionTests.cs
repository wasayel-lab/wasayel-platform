using ACommerce.Kit.Auth;
using ACommerce.Platform.I18n;
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

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-28، د56)</b> — كانَ <c>Today_a_key_whose_actor_deleted_their_account_still_authenticates_in_their_name</c>
    /// (‏<c>2fe6bf5</c>: «الاعتِماد: مَقبول»). والآن المِفتاحُ نَفسُه — بِمَسارِ الإصدارِ الحَقيقيّ، وحِسابُ فاعِلِه مَمحُوٌّ بِـ<c>Erase</c> وَحدَه بِلا إبطالٍ —
    /// يُرَدُّ بِـ<c>ActorGone</c>: الفَحصُ الثاني يَقِفُ وَحدَه ولَو فاتَ الإبطالُ مِفتاحاً.</summary>
    [Fact]
    public async Task A_key_whose_actor_deleted_their_account_is_refused_actor_gone()
    {
        var (world, actor, issued) = await IssuedForAsync();
        AccountDeletion.Erase(actor, DateTime.UtcNow);

        var result = await new ApiKeyService(world.Store).AuthenticateAsync(issued.Presented);

        output.WriteLine($"الفاعِلُ مَحذوف: {actor.DeletedAt is not null} · الاعتِماد: {(result.Ok ? "مَقبول" : result.Rejection)}");
        Assert.NotNull(actor.DeletedAt);
        Assert.False(result.Ok);
        Assert.Equal(ApiKeyRejection.ActorGone, result.Rejection);
    }

    /// <summary>السالِبُ (القاعِدَة ٤): الفاعِلُ نَفسُه قائِماً — المِفتاحُ يُصادِقُ بِاسمِه، فَالرَفضُ أَعلاه لِلحَذفِ لا لِلفَحصِ نَفسِه.</summary>
    [Fact]
    public async Task A_key_whose_actor_is_active_authenticates_in_their_name()
    {
        var (world, actor, issued) = await IssuedForAsync();
        var result = await new ApiKeyService(world.Store).AuthenticateAsync(issued.Presented);
        Assert.True(result.Ok);
        Assert.Equal(ApiKeyRejection.None, result.Rejection);
        Assert.Equal(actor.Id, result.Principal!.ActorUserId);
    }

    /// <summary>مِفتاحٌ فاعِلُه بِلا وَثيقَةٍ في مُستَأجِرِه يُرَدُّ أَيضاً — الغِيابُ رَفضٌ لا مُرور («أ» د56·١).</summary>
    [Fact]
    public async Task A_key_whose_actor_has_no_account_in_its_store_is_refused_actor_gone()
    {
        var (world, actor, issued) = await IssuedForAsync();
        world.Docs.Remove(DocWorld.KeyOf(typeof(User), actor.Id.ToString()));
        var result = await new ApiKeyService(world.Store).AuthenticateAsync(issued.Presented);
        Assert.Equal(ApiKeyRejection.ActorGone, result.Rejection);
    }

    [Fact]
    public void ActorGone_is_true_for_a_deleted_or_missing_account_and_false_for_an_active_one()
    {
        var active = new User { Id = Guid.NewGuid() };
        var deleted = new User { Id = Guid.NewGuid() };
        AccountDeletion.Erase(deleted, DateTime.UtcNow);
        Assert.False(ApiKeyService.ActorGone(active));
        Assert.True(ApiKeyService.ActorGone(deleted));
        Assert.True(ApiKeyService.ActorGone(null));
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-28، د56)</b> — كانَ <c>Today_the_rejection_lexicon_has_no_reason_for_a_deleted_actor</c> (ثَمانِيَة).
    /// والآن تِسعَة: <c>ActorGone</c> آخِرُها — والمُرَشِّحُ يَطويه إلى <c>auth_invalid</c> كَإخوَتِه (<c>ApiKeyFilterTests</c>).</summary>
    [Fact]
    public void The_rejection_lexicon_names_a_deleted_actor_as_its_ninth_reason()
    {
        var names = Enum.GetNames<ApiKeyRejection>();
        output.WriteLine($"أَسبابُ الرَفض ({names.Length}): {string.Join(", ", names)}");
        Assert.Equal(new[] { "None", "Missing", "Malformed", "Unknown", "SecretMismatch", "Revoked", "Expired", "TenantGone", "ActorGone" }, names);
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-28، د56)</b> — كانَ <c>Today_the_deletion_endpoint_saves_the_user_alone_and_touches_no_key</c>.
    /// والآن تُبطِلُ النُقطَةُ مَفاتيحَ صاحِبِها عَلى جَلسَتِها <b>قَبلَ</b> حِفظِها الواحِد — فَالحَذفُ والإبطالُ مُعامَلَةٌ واحِدَة.</summary>
    [Fact]
    public void The_deletion_endpoint_revokes_the_users_keys_on_its_session_before_its_one_save()
    {
        var body = DeletionBody();
        var saves = body.Split("SaveChangesAsync(").Length - 1;
        var revoke = body.IndexOf("ApiKeyService.RevokeActorKeysAsync(session, slug, userId, now)", StringComparison.Ordinal);
        var store = body.IndexOf("session.Store(user!);", StringComparison.Ordinal);
        var save = body.IndexOf("await session.SaveChangesAsync();", StringComparison.Ordinal);
        output.WriteLine($"جِسمُ الحَذف: SaveChangesAsync {saves} · Store(user) {store} < الإبطال {revoke} < الحِفظ {save}");
        Assert.Equal(1, saves);
        Assert.True(store > 0 && revoke > store && save > revoke);
    }

    /// <summary>الدالَّةُ النَقِيَّة: مِفتاحُ هذا الفاعِلِ في هذا المُستَأجِرِ الفَعّالُ وَحدَه يُبطَل — وما سِواه لا يُمَسّ (فاعِلٌ آخَر، مُستَأجِرٌ آخَر،
    /// ومُبطَلٌ سَلَفاً يَبقى تاريخُ إبطالِه).</summary>
    [Fact]
    public void Revoking_for_a_deleted_actor_takes_their_active_keys_in_this_store_alone()
    {
        var actor = Guid.NewGuid();
        var then = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        ApiKeyDocument Key(string id, string slug, Guid who, string status, DateTime? revokedAt = null)
            => new() { Id = id, TenantSlug = slug, ActorUserId = who, Status = status, RevokedAt = revokedAt };
        var mine = Key("mine", Slug, actor, ApiKeyDocument.StatusActive);
        var mine2 = Key("mine2", Slug, actor, ApiKeyDocument.StatusActive);
        var other = Key("other", Slug, Guid.NewGuid(), ApiKeyDocument.StatusActive);
        var elsewhere = Key("elsewhere", "another-shop", actor, ApiKeyDocument.StatusActive);
        var old = Key("old", Slug, actor, ApiKeyDocument.StatusRevoked, then);

        var hit = ApiKeyService.RevokeForDeletedActor(new[] { mine, mine2, other, elsewhere, old }, Slug, actor, now);

        output.WriteLine($"أُبطِل: {string.Join(", ", hit.Select(k => k.Id))} مِن خَمسَة");
        Assert.Equal(new[] { "mine", "mine2" }, hit.Select(k => k.Id));
        Assert.All(hit, k => Assert.Equal((ApiKeyDocument.StatusRevoked, (DateTime?)now), (k.Status, k.RevokedAt)));
        Assert.Equal((ApiKeyDocument.StatusActive, (DateTime?)null), (other.Status, other.RevokedAt));
        Assert.Equal((ApiKeyDocument.StatusActive, (DateTime?)null), (elsewhere.Status, elsewhere.RevokedAt));
        Assert.Equal((ApiKeyDocument.StatusRevoked, (DateTime?)then), (old.Status, old.RevokedAt));
        Assert.Empty(ApiKeyService.RevokeForDeletedActor(Array.Empty<ApiKeyDocument>(), Slug, actor, now));
    }

    /// <summary>صَفحَةُ المَفاتيحِ لا تَعرِضُ في قائِمَةِ الفاعِلِ حِساباً يَرُدُّه الفَحصُ عِندَ الاستِعمال — تَسأَلُ الدالَّةَ نَفسَها
    /// (<c>ApiKeyService.ActorGone</c>)، فَلا يُصدِرُ المالِكُ مِفتاحاً «فَعّالاً» لا يَعمَلُ أَبَداً (القاعِدَة ١٢؛ «أ» د56·٢).</summary>
    [Fact]
    public void The_keys_page_offers_no_actor_the_use_time_check_refuses()
    {
        var page = WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "StudioAppKeys.razor")));
        Assert.Contains("users = (await TenantAdmin.UsersAsync(Slug)).Where(u => !ApiKeyService.ActorGone(u)).ToList();", page, StringComparison.Ordinal);
        Assert.Equal(1, page.Split("TenantAdmin.UsersAsync(").Length - 1);
    }

    // ─── النُصوص: الجُملَةُ وَحدَها خَرَجَت — بُرهانٌ بايتِيّ (القاعِدَة ١٣) ─────────────────────────────

    /// <summary>كُلُّ نَصٍّ حُذِفَت مِنه الجُملَة: مِفتاحُه ولُغَتُه، والمَرساةُ الَّتي كانَت الجُملَةُ تَليها، والجُملَةُ بِحَرفِها، وبَصمَةُ SHA-256
    /// لِلقيمَةِ قَبلَ الحَذفِ (‏<c>2fe6bf5</c>).</summary>
    internal static readonly (string Key, string Lang, string Anchor, string Sentence, string ShaBefore)[] RemovedSentence =
    {
        ("legal.privacy.deletion", "ar", "ومَفاتيح الرَّبط",
            "، ومِفتاح رَبط صَدَر بِاسمك يَبقى يَعمَل بِاسمك حَتّى يُبطِلَه صاحِب المَتجَر",
            "0db1050c4a5c44c88942432731d59e99b4463524e3ffec6a900aa15c052cb9e7"),
        ("platform.privacy.s6_body", "ar", "ومَفاتيحِ الرَبط",
            "، ومِفتاحُ رَبطٍ صَدَرَ بِاسمِكَ يَبقى يَعمَلُ بِاسمِكَ حَتّى يُبطِلَه صاحِبُ المَتجَر",
            "2e3533db1f78c22b00d41e8305e15a61bda2f4dc93f972d16a3e46560cdeb9fd"),
        ("account.delete.retained_note", "ar", "ومَفاتيحِ الرَبط",
            "، ومِفتاحُ رَبطٍ صَدَرَ بِاسمِكَ يَبقى يَعمَلُ بِاسمِكَ حَتّى يُبطِلَه صاحِبُ المَتجَر",
            "e2cc0d19e657fdb869cfb76e52f1c62986627b3d0357a0a7974023217e6628c4"),
        ("platform.privacy.s6_body", "en", "the audit log and the integration keys.",
            " An integration key issued in your name keeps acting in your name until the store owner revokes it.",
            "385c00759bdca495d53c4ad7dffeb905480afdbd45539dd067a20f1453ae512f"),
    };

    /// <summary><b>قُلِبَ بِاسمِه (‏2026-10-29، قَرارُ المالِك د52 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق)</b> — كانَ <c>Each_deletion_text_lost_the_keeps_acting_sentence_and_nothing_else</c>:
    /// بُرهانٌ بايتِيٌّ يَومَ د56 أَنّ الجُملَةَ وَحدَها خَرَجَت (إعادَتُها بَعدَ مَرساتِها تُعطي بَصمَةَ ما قَبلُ — ‏<c>2fe6bf5</c>). ثُمَّ أَعادَ د52 كِتابَةَ النُصوصِ الأَربَعَةِ
    /// كُلِّها (ما يُمحى وما يَبقى — <c>AccountDeletionRetentionTests</c>) فَزالَت المَرساةُ «سِجِلِّ التَدقيقِ ومَفاتيحِ الرَبط» مَعَها، والبَصمَةُ صادِقَةٌ عَن يَومِها لا عَن اليَوم.
    /// والمَحروسُ الباقي: الجُملَةُ لَم تَعُد، ولا مَرساتُها، في النُصوصِ الأَربَعَة.</summary>
    [Fact]
    public void Each_deletion_text_lost_the_keeps_acting_sentence_and_the_d52_rewrite_did_not_bring_it_back()
    {
        var n = 0;
        foreach (var (key, lang, anchor, sentence, shaBefore) in RemovedSentence)
        {
            var now = LocaleCatalog.Find(lang == "ar" ? LocaleCatalog.Arabic : lang, key)
                ?? throw new Xunit.Sdk.XunitException($"لا مِفتاحَ «{key}» في {lang}.json.");
            output.WriteLine($"{lang}:{key} — {now.Length} حَرفاً · الجُملَة: {now.Contains(sentence, StringComparison.Ordinal)} · المَرساة: {now.Contains(anchor, StringComparison.Ordinal)} · بَصمَةُ يَومِ د56 {shaBefore[..8]}…");
            Assert.DoesNotContain(sentence, now, StringComparison.Ordinal);
            Assert.DoesNotContain(anchor, now, StringComparison.Ordinal);
            Assert.Equal(64, shaBefore.Length);
            n++;
        }
        Assert.Equal(4, n);
    }
}

/// <summary>
/// <para><b>بُرهانُ د56 حَيّاً عَلى Postgres</b> (‏<c>WASAYEL_LIVE_PROOF=1</c>): استِعلامُ <c>RevokeActorKeysAsync</c> — مِفتاحٌ عامٌّ (<c>SingleTenanted</c>) يُقرَأُ
/// مِن جَلسَةِ مُستَأجِر — لا يُقاسُ في الذاكِرَة، فَيُقاسُ هُنا بِتَسلسُلِ نُقطَةِ الحَذفِ نَفسِه: <c>Erase</c> ثُمَّ <c>Store</c> ثُمَّ الإبطالُ ثُمَّ حِفظٌ واحِد.</para>
/// </summary>
[Collection(LiveProofCollection.Name)]
public class LiveApiKeyActorDeletionProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>تَركيبُ المَخزَنِ لِما يَلمِسُه الصَفّ: <c>ApiKeyDocument</c> عامَّةٌ كَما يُسَجِّلُها القالِب، و<c>User</c> مُتَعَدِّدَةُ الإيجار.</summary>
    internal static Marten.DocumentStore BuildStore() => Marten.DocumentStore.For(o =>
    {
        o.Connection(LiveDealConcurrencyProofTests.ConnectionString!.TrimEnd(';') + ";Timeout=60;Command Timeout=120");
        o.DatabaseSchemaName = "platform";
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;
        o.Schema.For<ACommerce.Kit.Tenants.Tenant>().SingleTenanted().Identity(x => x.Id);
        o.Schema.For<ApiKeyDocument>().SingleTenanted().Identity(x => x.Id);
        o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
    });

    private static ApiKeyValidator.IssueRequest For(Guid actor)
        => new("رَبطُ الناقِل", actor, "", new[] { ApiScopeCatalog.DealsRead }, null);

    [Fact]
    public async Task Deleting_an_account_revokes_its_keys_in_the_same_save_and_leaves_another_actors_key_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = BuildStore();
        var slug = "ak-zz-" + Guid.NewGuid().ToString("N")[..8];
        var issued = new List<string>();
        try
        {
            var actor = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "سائِق", Phone = "966500000561" };
            var other = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "سائِقٌ آخَر", Phone = "966500000562" };
            await using (var s = store.LightweightSession(slug)) { s.Store(actor, other); await s.SaveChangesAsync(); }

            var keys = new ApiKeyService(store);
            var mine = await keys.IssueAsync(slug, For(actor.Id), Guid.NewGuid());
            var theirs = await keys.IssueAsync(slug, For(other.Id), Guid.NewGuid());
            issued.AddRange(new[] { mine.Key.Id, theirs.Key.Id });
            var before = (await keys.AuthenticateAsync(mine.Presented)).Rejection;

            int revoked;
            await using (var s = store.LightweightSession(slug))
            {
                var u = await s.LoadAsync<User>(actor.Id);
                var now = DateTime.UtcNow;
                AccountDeletion.Erase(u!, now);
                s.Store(u!);
                revoked = await ApiKeyService.RevokeActorKeysAsync(s, slug, actor.Id, now);
                await s.SaveChangesAsync();
            }

            var after = (await keys.AuthenticateAsync(mine.Presented)).Rejection;
            var otherAfter = (await keys.AuthenticateAsync(theirs.Presented)).Rejection;
            ApiKeyDocument? doc;
            await using (var q = store.QuerySession()) doc = await q.LoadAsync<ApiKeyDocument>(mine.Key.Id);
            output.WriteLine($"قَبلَ الحَذف: {before} · أُبطِل: {revoked} · بَعدَه: {after} · الوَثيقَة: {doc?.Status}/{(doc?.RevokedAt is null ? "بِلا تاريخ" : "مُؤَرَّخ")} · مِفتاحُ الآخَر: {otherAfter}");
            Assert.Equal(ApiKeyRejection.None, before);
            Assert.Equal(1, revoked);
            Assert.Equal(ApiKeyRejection.Revoked, after);
            Assert.Equal((ApiKeyDocument.StatusRevoked, true), (doc!.Status, doc.RevokedAt is not null));
            Assert.Equal(ApiKeyRejection.None, otherAfter);
        }
        finally { await CleanAsync(store, slug, issued); }
    }

    [Fact]
    public async Task A_key_that_missed_the_revocation_is_refused_actor_gone_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = BuildStore();
        var slug = "ak-zz-" + Guid.NewGuid().ToString("N")[..8];
        var issued = new List<string>();
        try
        {
            var actor = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "سائِق", Phone = "966500000563" };
            await using (var s = store.LightweightSession(slug)) { s.Store(actor); await s.SaveChangesAsync(); }
            var keys = new ApiKeyService(store);
            var key = await keys.IssueAsync(slug, For(actor.Id), Guid.NewGuid());
            var ghost = await keys.IssueAsync(slug, For(Guid.NewGuid()), Guid.NewGuid());
            issued.AddRange(new[] { key.Key.Id, ghost.Key.Id });

            await using (var s = store.LightweightSession(slug))
            {
                var u = await s.LoadAsync<User>(actor.Id);
                AccountDeletion.Erase(u!, DateTime.UtcNow);
                s.Store(u!);
                await s.SaveChangesAsync();   // بِلا إبطال — مِفتاحٌ فاتَه
            }

            var gone = (await keys.AuthenticateAsync(key.Presented)).Rejection;
            var noAccount = (await keys.AuthenticateAsync(ghost.Presented)).Rejection;
            output.WriteLine($"مِفتاحُ حِسابٍ مَحذوفٍ فاتَه الإبطال: {gone} · مِفتاحُ فاعِلٍ بِلا حِساب: {noAccount}");
            Assert.Equal(ApiKeyRejection.ActorGone, gone);
            Assert.Equal(ApiKeyRejection.ActorGone, noAccount);
        }
        finally { await CleanAsync(store, slug, issued); }
    }

    private static async Task CleanAsync(Marten.IDocumentStore store, string slug, IEnumerable<string> keyIds)
    {
        await using (var g = store.LightweightSession())
        {
            foreach (var id in keyIds) g.Delete<ApiKeyDocument>(id);
            await g.SaveChangesAsync();
        }
        await LiveZeroRoleAdminProofTests.CleanAsync(store, slug);
    }
}
