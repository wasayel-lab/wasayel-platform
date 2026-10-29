using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Offers;
using ACommerce.Kit.Reviews;
using ACommerce.Kit.Support;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using ACommerce.Templates.Customer.Marketplace.Services.Audit;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Identity;
using ACommerce.Templates.Customer.Marketplace.Services.Queries;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>الحَذفُ يَبلُغُ كُلَّ نُسخَةٍ تُعَدَّل</b> (‏2026-10-29، قَرارُ المالِك د52 (١) «أ»): دالّاتُ <see cref="AccountErasure"/> النَقِيَّةُ صَفّاً مُوجَباً وصَفّاً
/// سالِباً — ما لِصاحِبِ الحِسابِ يُمحى، وما لِغَيرِه بِاسمِه — ثُمَّ إخفاءُ الاسمِ عِندَ عَرضِ سِجِلِّ التَدقيق. والبُرهانُ الحَيُّ في
/// <see cref="LiveAccountErasureProofTests"/>.</para>
/// </summary>
public class AccountErasureTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private const string Name = "صاحِبُ الحِساب";

    private static Notification Notice(Guid inbox, string type, string title, string body, string? url)
        => new() { Id = Guid.NewGuid(), UserId = inbox, Type = type, Title = title, Body = body, RelatedUrl = url };

    [Fact]
    public void A_notice_about_the_users_deal_or_chat_in_someone_elses_inbox_loses_the_name()
    {
        var deal = Guid.NewGuid();
        var n = Notice(Other, "deal_accepted", "قَبِلَ " + Name, Name + " قَبِلَ طَلَبَك", $"/zz/deals/{deal}");
        var chat = Notice(Other, "chat_message", "رِسالَةٌ مِن " + Name, "مَرحَباً", $"/zz/chats/{deal}");
        Assert.True(AccountErasure.EraseFrom(n, Me, Name, new[] { $"/deals/{deal}" }));
        Assert.Equal(("قَبِلَ " + AccountDeletion.ErasedName, AccountDeletion.ErasedName + " قَبِلَ طَلَبَك"), (n.Title, n.Body));
        Assert.True(AccountErasure.EraseFrom(chat, Me, Name, new[] { $"/chats/{deal}" }));
        Assert.Equal("مَرحَباً", chat.Body);
    }

    [Fact]
    public void A_manager_notice_naming_the_user_loses_the_name_whatever_its_link()
    {
        var registered = Notice(Other, "new_user", "مُستَخدِم جَديد سَجَّل", Name, "/admin/tenants/zz/users");
        var reported = Notice(Other, "report", "بَلاغ", Name + " بَلَّغَ عَن إعلان", "/admin/tenants/zz/tickets");
        Assert.True(AccountErasure.EraseFrom(registered, Me, Name, Array.Empty<string>()));
        Assert.True(AccountErasure.EraseFrom(reported, Me, Name, Array.Empty<string>()));
        Assert.Equal(AccountDeletion.ErasedName, registered.Body);
    }

    [Fact]
    public void A_notice_about_something_else_or_in_the_users_own_inbox_or_under_a_non_identifying_name_is_untouched()
    {
        var unrelated = Notice(Other, "deal_accepted", "قَبِلَ " + Name, "—", $"/zz/deals/{Guid.NewGuid()}");   // اسمٌ يُشبِهُه في صَفقَةٍ لَيسَت لَه
        var own = Notice(Me, "deal_accepted", "قَبِلَ " + Name, "—", "/zz/deals/x");
        var deflt = Notice(Other, "new_user", "مُستَخدِم جَديد سَجَّل", new User().FullName, "/admin/tenants/zz/users");
        Assert.False(AccountErasure.EraseFrom(unrelated, Me, Name, new[] { "/deals/mine" }));
        Assert.False(AccountErasure.EraseFrom(own, Me, Name, new[] { "/deals/x" }));
        Assert.False(AccountErasure.EraseFrom(deflt, Me, new User().FullName, Array.Empty<string>()));
        Assert.Equal("قَبِلَ " + Name, unrelated.Title);
        Assert.False(AccountErasure.Identifies(""));
        Assert.False(AccountErasure.Identifies(AccountDeletion.ErasedName));
        Assert.True(AccountErasure.Identifies(Name));
    }

    // ─── اسمُ التَسجيلِ الافتِراضِيّ، ووَسيلَةُ الاتِّصالِ في إشعارِ التَسجيلِ القَديم (مُراجَعَةُ تَيّارِ S2 الجَولَة ١، ‏2026-10-29) ───
    //
    // ‏(‏تَوصيفُه `1c651b7` — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): `Identifies` كانَ يَستَثني `new User().FullName` («مُستَخدِم جَديد») ولا يَستَثني ما يُخَزَّنُ فِعلاً
    // لِلحِسابِ الجَديدِ بِالهاتِفِ أَو البَريد: `me.profile.new_member` («عُضو جَديد»)، وإشعاراتُ المُديرينَ (`new_user`/`report`) لا يَحصُرُها رابِط — فَحَذفُ
    // حِسابٍ ما زالَ اسمُه الافتِراضِيَّ كَتَبَ «حِسابٌ مَحذوف» في إشعارِ تَسجيلِ كُلِّ عُضوٍ آخَر. وإشعارُ التَسجيلِ قَبلَ `548ea4d` مَتنُه «{الاسم} · {الهاتِف}» أَو
    // «… · {البَريد}» والاسمُ يَومَئِذٍ الافتِراضِيّ، والمَحوُ كانَ بِالاسمِ وَحدَه فَبَقِيَ الهاتِفُ والبَريد. قُلِبَ الصَفّانِ بِاسمَيهِما: الافتِراضِيُّ لا يَدُلُّ
    // عَلى أَحَد، ووَسيلَةُ الاتِّصالِ تَربِطُ إشعارَ المُديرينَ بِهُوِيَّةِ صاحِبِه.

    private static string Placeholder
        => ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, ACommerce.Kit.Auth.Server.AuthHandlers.NewMemberNameKey)!;

    private const string MyPhone = "+966500000002", MyEmail = "member@example.test";

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b> — كانَ <c>Today_deleting_a_placeholder_named_account_rewrites_another_members_registration_notice</c>: الصَفُّ السالِب.
    /// إشعارُ تَسجيلِ عُضوٍ آخَرَ بِالافتِراضِيّ (الجَديدُ بِلا وَسيلَة، والقَديمُ بِوَسيلَتِه هُو)، وبَلاغُه، لا يُمَسُّ بِحَذفِ حِسابٍ يَحمِلُ الافتِراضِيَّ نَفسَه.</summary>
    [Fact]
    public void Deleting_a_placeholder_named_account_leaves_another_members_registration_notice_untouched()
    {
        var anothers = Notice(Other, "new_user", "مُستَخدِم جَديد سَجَّل", Placeholder, "/admin/tenants/zz/users");
        var anothersOld = Notice(Other, "new_user", "مُستَخدِم جَديد سَجَّل", $"{Placeholder} · +966500000001", "/admin/tenants/zz/users");
        var anothersReport = Notice(Other, "report", "بَلاغ: سَبَب", $"{Placeholder} بَلَّغَ عَن إعلان", "/admin/tenants/zz/tickets");
        foreach (var p in AccountErasure.PlaceholderNames) Assert.False(AccountErasure.Identifies(p));
        Assert.False(AccountErasure.Identifies(" " + Placeholder + " "));
        Assert.Contains(Placeholder, AccountErasure.PlaceholderNames);
        Assert.Contains(new User().FullName, AccountErasure.PlaceholderNames);
        var mine = new[] { MyPhone };
        Assert.False(AccountErasure.EraseFrom(anothers, Me, new[] { Placeholder }, Array.Empty<string>(), mine));
        Assert.False(AccountErasure.EraseFrom(anothersOld, Me, new[] { Placeholder }, Array.Empty<string>(), mine));
        Assert.False(AccountErasure.EraseFrom(anothersReport, Me, new[] { Placeholder }, Array.Empty<string>(), mine));
        Assert.Equal((Placeholder, $"{Placeholder} · +966500000001"), (anothers.Body, anothersOld.Body));
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b> — كانَ <c>Today_an_old_registration_notice_keeps_the_deleted_members_phone_and_email</c>: الصَفُّ المُوجَب.
    /// إشعارُ تَسجيلِه القَديمُ يُعرَفُ بِوَسيلَتِه — ولَو كَتَبَ اسماً بَعدَه — فَتُنزَعُ ويَصيرُ الافتِراضِيُّ في مَتنِه «حِسابٌ مَحذوف»، والعُنوانُ الثابِتُ كَما هُو.</summary>
    [Fact]
    public void An_old_registration_notice_loses_the_deleted_members_phone_or_email_and_its_name()
    {
        var byPhone = Notice(Other, "new_user", "مُستَخدِم جَديد سَجَّل", $"{Placeholder} · {MyPhone}", "/admin/tenants/zz/users");
        var byEmail = Notice(Other, "new_user", "مُستَخدِم جَديد سَجَّل", $"{Placeholder} · {MyEmail}", "/admin/tenants/zz/users");
        var contacts = new[] { MyPhone, MyEmail };
        Assert.True(AccountErasure.EraseFrom(byPhone, Me, new[] { "أَحمَدُ الفاحِص" }, Array.Empty<string>(), contacts));
        Assert.True(AccountErasure.EraseFrom(byEmail, Me, new[] { Placeholder }, Array.Empty<string>(), contacts));
        Assert.Equal(("مُستَخدِم جَديد سَجَّل", AccountDeletion.ErasedName), (byPhone.Title, byPhone.Body));
        Assert.Equal(("مُستَخدِم جَديد سَجَّل", AccountDeletion.ErasedName), (byEmail.Title, byEmail.Body));
        // ومَرَّةً ثانِيَةً لا شَيء: المَمحُوُّ لا يُكتَبُ مَرَّتَين.
        Assert.False(AccountErasure.EraseFrom(byPhone, Me, new[] { "أَحمَدُ الفاحِص" }, Array.Empty<string>(), contacts));
    }

    /// <summary>والوَسيلَةُ في غَيرِ إشعارِ المُديرين، أَو في صُندوقِه هُو، لا تَربِطُ — والمَحوُ بِالوَسيلَةِ لِإشعاراتِ التَسجيلِ والبَلاغِ وَحدَها.</summary>
    [Fact]
    public void A_contact_outside_a_manager_notice_or_in_the_users_own_inbox_does_not_tie_the_notice_to_the_user()
    {
        var dealNotice = Notice(Other, "deal_accepted", "قُبِلَ طَلَبُك", $"اتَّصِل عَلى {MyPhone}", $"/zz/deals/{Guid.NewGuid()}");
        var own = Notice(Me, "new_user", "مُستَخدِم جَديد سَجَّل", $"{Placeholder} · {MyPhone}", "/admin/tenants/zz/users");
        Assert.False(AccountErasure.EraseFrom(dealNotice, Me, Array.Empty<string>(), Array.Empty<string>(), new[] { MyPhone }));
        Assert.False(AccountErasure.EraseFrom(own, Me, Array.Empty<string>(), Array.Empty<string>(), new[] { MyPhone }));
        Assert.Contains(MyPhone, dealNotice.Body, StringComparison.Ordinal);
    }

    /// <summary>حَدّا الوَسيلَة: تُطابِقُ بِعَينِها ولا تُطابِقُ داخِلَ رَقَمٍ أَو بَريدٍ أَطوَل — مُوجَبٌ وسالِب.</summary>
    [Fact]
    public void A_contact_matches_whole_and_never_inside_a_longer_number_or_address()
    {
        Assert.True(AccountErasure.Carries($"{Placeholder} · {MyPhone}", MyPhone));
        Assert.True(AccountErasure.Carries($"{Placeholder} · {MyEmail}.", MyEmail));
        Assert.False(AccountErasure.Carries($"{Placeholder} · {MyPhone}9", MyPhone));
        Assert.False(AccountErasure.Carries($"{Placeholder} · 1{MyPhone}", MyPhone));
        Assert.False(AccountErasure.Carries($"{Placeholder} · x{MyEmail}", MyEmail));
        Assert.False(AccountErasure.Carries($"{Placeholder} · {MyEmail}.sa", MyEmail));
        Assert.False(AccountErasure.Carries(null, MyPhone));
        var u = new User { FullName = Placeholder, Phone = " " + MyPhone + " ", Email = "" };
        Assert.Equal(new[] { MyPhone }, AccountErasure.ContactsOf(u));
        u.Email = MyEmail;
        Assert.Equal(new[] { MyPhone, MyEmail }, AccountErasure.ContactsOf(u));
    }

    [Fact]
    public void A_ticket_loses_its_authors_name_on_the_ticket_and_their_replies_and_the_team_reply_keeps_its_signature()
    {
        var t = new Ticket { AuthorId = Me, AuthorName = Name, Replies = new()
        {
            new Reply { AuthorName = Name, FromStaff = false },
            new Reply { AuthorName = "دَعم التَّطبيق", FromStaff = true },
        } };
        var theirs = new Ticket { AuthorId = Other, AuthorName = "غَيري" };
        Assert.True(AccountErasure.EraseFrom(t, Me));
        Assert.Equal(new[] { AccountDeletion.ErasedName, AccountDeletion.ErasedName, "دَعم التَّطبيق" },
                     new[] { t.AuthorName }.Concat(t.Replies.Select(r => r.AuthorName)));
        Assert.False(AccountErasure.EraseFrom(theirs, Me));
        Assert.Equal("غَيري", theirs.AuthorName);
    }

    [Fact]
    public void A_listing_match_loses_the_offerers_name_and_anothers_match_is_untouched()
    {
        var mine = new ListingMatch { OffererId = Me, OffererName = Name };
        var theirs = new ListingMatch { OffererId = Other, OffererName = "غَيري" };
        Assert.True(AccountErasure.EraseFrom(mine, Me));
        Assert.False(AccountErasure.EraseFrom(theirs, Me));
        Assert.Equal((AccountDeletion.ErasedName, "غَيري"), (mine.OffererName, theirs.OffererName));
    }

    [Fact]
    public void A_deal_step_another_actor_took_keeps_its_name_and_a_deal_the_user_is_not_party_to_keeps_its_address()
    {
        var deal = new Deal
        {
            InitiatorId = Other, InitiatorName = "مُشتَرٍ", CounterpartyId = Guid.NewGuid(),
            Attributes = new() { [Deal.DeliveryAddressAttribute] = "حَيٌّ" },
            Timeline = new() { new DealEvent(DealStage.Offered, DealStage.Booked, "advanced", Me, Name, null, DateTime.UtcNow) },
        };
        Assert.True(AccountErasure.EraseFrom(deal, Me));     // خُطوَتُه هُو وَحدَها
        Assert.Equal(AccountDeletion.ErasedName, deal.Timeline[0].ActorName);
        Assert.Equal(("مُشتَرٍ", "حَيٌّ"), (deal.InitiatorName, deal.Attributes[Deal.DeliveryAddressAttribute]));
        Assert.False(AccountErasure.EraseFrom(deal, Me));    // ومَرَّةً ثانِيَةً لا شَيء
    }

    [Fact]
    public void The_audit_log_hides_an_erased_actors_name_when_shown_and_keeps_anothers()
    {
        var rows = new List<AuditEntry>
        {
            new() { ActorId = Me, ActorName = Name, Action = "shipping.oto.connect" },
            new() { ActorId = Other, ActorName = "مُدير", Action = "tenant.update" },
            new() { ActorId = null, ActorName = "paypal · webhook", Action = "billing" },
        };
        var shown = AuditLogQueries.HideErasedActors(rows, new HashSet<Guid> { Me });
        Assert.Equal(new[] { AccountDeletion.ErasedName, "مُدير", "paypal · webhook" }, shown.Select(a => a.ActorName));
        Assert.Equal(new[] { "مُدير" }, AuditLogQueries.HideErasedActors(
            new List<AuditEntry> { new() { ActorId = Other, ActorName = "مُدير" } }, new HashSet<Guid>()).Select(a => a.ActorName));
    }
}

/// <summary>
/// <para><b>بُرهانُ د52 حَيّاً عَلى Postgres</b> (‏<c>WASAYEL_LIVE_PROOF=1</c>): تَسلسُلُ نُقطَةِ الحَذفِ بِعَينِه — البَوّابَةُ بِعَدِّ الصَفَقاتِ الجارِيَة، ثُمَّ
/// <c>Erase</c> و<c>Store</c> وإبطالُ المَفاتيحِ و<see cref="AccountErasure.EraseCopiesAsync"/> وحِفظٌ واحِد — عَلى مَخزَنٍ بِتَركيبِ المُضيف (لَقَطاتُ العَرضِ
/// والتَذكِرَةِ والإعلان، وتَسجيلُ الصَفقَة، ومَفاتيحُ الرَبطِ عامَّة). ما لا يُقاسُ في الذاكِرَة: <c>Timeline.Any</c> و<c>Title.Contains</c> وقِراءَةُ وَثيقَةٍ عامَّةٍ
/// مِن جَلسَةِ مُستَأجِر — وأَنَّ الحِفظَ الواحِدَ لا يَكتُبُ مِفتاحاً مَرَّتَين.</para>
/// </summary>
[Collection(LiveProofCollection.Name)]
public class LiveAccountErasureProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static DocumentStore BuildStore() => DocumentStore.For(o =>
    {
        o.Connection(LiveDealConcurrencyProofTests.ConnectionString!.TrimEnd(';') + ";Timeout=60;Command Timeout=120");
        o.DatabaseSchemaName = "platform";
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;
        o.Schema.For<ACommerce.Kit.Tenants.Tenant>().SingleTenanted().Identity(x => x.Id);
        o.Schema.For<ApiKeyDocument>().SingleTenanted().Identity(x => x.Id);
        o.Projections.Snapshot<ACommerce.Kit.Listings.Listing>(global::JasperFx.Events.Projections.SnapshotLifecycle.Inline);
        o.Projections.Snapshot<Ticket>(global::JasperFx.Events.Projections.SnapshotLifecycle.Inline);
        o.Projections.Snapshot<Offer>(global::JasperFx.Events.Projections.SnapshotLifecycle.Inline);
        DealsService.RegisterDocument(o);
        o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
    });

    [Fact]
    public async Task Deletion_refuses_an_open_deal_then_erases_every_editable_copy_in_one_save_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = BuildStore();
        var slug = "zz-erase-" + Guid.NewGuid().ToString("N")[..8];
        const string name = "صاحِبُ الحِسابِ الحَيّ";
        var me = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = name, Phone = "966500000552" };
        var other = Guid.NewGuid();
        var open = new Deal { Id = Guid.NewGuid(), TenantSlug = slug, InitiatorId = me.Id, InitiatorName = name, CounterpartyId = other, Status = DealStatus.Active };
        var bought = new Deal { Id = Guid.NewGuid(), TenantSlug = slug, InitiatorId = me.Id, InitiatorName = name, CounterpartyId = other, CounterpartyName = "بائِع",
            Status = DealStatus.Completed, Attributes = new() { [Deal.DeliveryAddressAttribute] = "حَيُّ المِثال", [Deal.DeliveryPhoneAttribute] = "0500000552" },
            Timeline = new() { new DealEvent(DealStage.Offered, DealStage.Offered, "created", me.Id, name, null, DateTime.UtcNow) } };
        var sold = new Deal { Id = Guid.NewGuid(), TenantSlug = slug, InitiatorId = other, InitiatorName = "مُشتَرٍ", CounterpartyId = me.Id, CounterpartyName = name,
            Status = DealStatus.Cancelled, Attributes = new() { [Deal.DeliveryAddressAttribute] = "حَيُّ المُشتَري" } };
        var conv = new Conversation { Id = Guid.NewGuid(), OwnerId = other, OwnerName = "بائِع", PartnerId = me.Id, PartnerName = name };
        var review = new Review { Id = Guid.NewGuid(), AuthorId = me.Id, AuthorName = name, TargetUserId = other, TargetUserName = "بائِع" };
        var offer = new Offer { Id = Guid.NewGuid(), OffererId = me.Id, OffererName = name, Price = 10m };
        var match = new ListingMatch { Id = Guid.NewGuid(), OffererId = me.Id, OffererName = name };
        var ticket = new Ticket { Id = Guid.NewGuid(), AuthorId = me.Id, AuthorName = name, Subject = "سُؤال" };
        var aboutMe = new Notification { Id = Guid.NewGuid(), UserId = other, Type = "deal_accepted", Title = "قَبِلَ " + name, Body = "—", RelatedUrl = $"/{slug}/deals/{bought.Id}" };
        var sameNameElsewhere = new Notification { Id = Guid.NewGuid(), UserId = other, Type = "deal_accepted", Title = "قَبِلَ " + name, Body = "—", RelatedUrl = $"/{slug}/deals/{Guid.NewGuid()}" };
        var audit = new AuditEntry { Id = Guid.NewGuid(), Scope = slug, ActorId = me.Id, ActorName = name, Action = "shipping.oto.connect" };
        var keys = new ApiKeyService(store);
        var issued = new List<string>();
        try
        {
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(me); s.Store(open, bought, sold); s.Store(conv); s.Store(review); s.Store(offer); s.Store(match); s.Store(ticket);
                s.Store(aboutMe, sameNameElsewhere); s.Store(audit);
                await s.SaveChangesAsync();
            }
            var active = await keys.IssueAsync(slug, new ApiKeyValidator.IssueRequest("رَبط", me.Id, name, new[] { ApiScopeCatalog.DealsRead }, null), Guid.NewGuid());
            var old = await keys.IssueAsync(slug, new ApiKeyValidator.IssueRequest("قَديم", me.Id, name, new[] { ApiScopeCatalog.DealsRead }, null), Guid.NewGuid());
            issued.AddRange(new[] { active.Key.Id, old.Key.Id });
            await keys.RevokeAsync(slug, old.Key.Id);

            // ١) صَفقَةٌ جارِيَة ⇒ يُرَدّ، ولا شَيءَ يُكتَب.
            await using (var s = store.LightweightSession(slug))
            {
                var u = await s.LoadAsync<User>(me.Id);
                var refused = AccountDeletion.Validate(u, await AccountErasure.OpenDealsAsync(s, me.Id), "حذف", "حذف");
                output.WriteLine($"بِصَفقَةٍ جارِيَة: {refused?.Code}");
                Assert.Equal(AccountDeletion.OpenDeal, refused?.Code);
            }

            // ٢) انتَهَت ⇒ تَسلسُلُ النُقطَةِ بِعَينِه.
            await using (var s = store.LightweightSession(slug))
            {
                // تُقرَأُ الصَفقَةُ قَبلَ كِتابَتِها — الوَثيقَةُ بِتَزامُنٍ تَفاؤُلِيٍّ (ADR-036)، فَلا تُكتَبُ نُسخَةٌ لَم تُقرَأ.
                var d = await s.LoadAsync<Deal>(open.Id);
                d!.Status = DealStatus.Completed;
                s.Store(d);
                await s.SaveChangesAsync();
            }
            AccountErasure.Counts counts;
            await using (var s = store.LightweightSession(slug))
            {
                var u = await s.LoadAsync<User>(me.Id);
                Assert.Null(AccountDeletion.Validate(u, await AccountErasure.OpenDealsAsync(s, me.Id), "حذف", "حذف"));
                var now = DateTime.UtcNow;
                var nameBefore = u!.FullName;
                AccountDeletion.Erase(u, now);
                s.Store(u);
                await ApiKeyService.RevokeActorKeysAsync(s, slug, me.Id, now);
                counts = await AccountErasure.EraseCopiesAsync(s, slug, me.Id, nameBefore);
                await s.SaveChangesAsync();
            }
            output.WriteLine($"مُحِيَ: صَفَقات {counts.Deals} · مُحادَثات {counts.Conversations} · تَقييمات {counts.Reviews} · عُروض {counts.Offers} · مُطابَقات {counts.Matches} · تَذاكِر {counts.Tickets} · مَفاتيح {counts.Keys} · إشعارات {counts.Notifications}");

            await using var q = store.QuerySession(slug);
            var x = AccountDeletion.ErasedName;
            var b = await q.LoadAsync<Deal>(bought.Id);
            var so = await q.LoadAsync<Deal>(sold.Id);
            Assert.Equal((x, "بائِع", false, false), (b!.InitiatorName, b.CounterpartyName, b.Attributes.ContainsKey(Deal.DeliveryAddressAttribute), b.Attributes.ContainsKey(Deal.DeliveryPhoneAttribute)));
            Assert.Equal(x, Assert.Single(b.Timeline).ActorName);
            Assert.Equal((x, "مُشتَرٍ", "حَيُّ المُشتَري"), (so!.CounterpartyName, so.InitiatorName, so.Attributes[Deal.DeliveryAddressAttribute]));
            Assert.Equal((x, "بائِع"), ((await q.LoadAsync<Conversation>(conv.Id))!.PartnerName, (await q.LoadAsync<Conversation>(conv.Id))!.OwnerName));
            Assert.Equal(x, (await q.LoadAsync<Review>(review.Id))!.AuthorName);
            Assert.Equal(x, (await q.LoadAsync<Offer>(offer.Id))!.OffererName);
            Assert.Equal(x, (await q.LoadAsync<ListingMatch>(match.Id))!.OffererName);
            Assert.Equal(x, (await q.LoadAsync<Ticket>(ticket.Id))!.AuthorName);
            Assert.Equal("قَبِلَ " + x, (await q.LoadAsync<Notification>(aboutMe.Id))!.Title);
            Assert.Equal("قَبِلَ " + name, (await q.LoadAsync<Notification>(sameNameElsewhere.Id))!.Title);
            Assert.Equal(name, (await q.LoadAsync<AuditEntry>(audit.Id))!.ActorName);                                  // في المَخزَنِ كَما كُتِب
            Assert.Equal(x, (await new AuditLogQueries(store).RecentAsync(slug)).Single(a => a.Id == audit.Id).ActorName); // ويُخفى عِندَ العَرض
            var docs = await q.Query<ApiKeyDocument>().Where(k => k.TenantSlug == slug).ToListAsync();
            Assert.All(docs, k => Assert.Equal((ApiKeyDocument.StatusRevoked, x), (k.Status, k.ActorName)));
            Assert.Equal(2, docs.Count);
            Assert.Equal((3, 1, 1, 1, 1, 1, 1, 1), (counts.Deals, counts.Conversations, counts.Reviews, counts.Offers, counts.Matches, counts.Tickets, counts.Keys, counts.Notifications));
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            s.DeleteWhere<User>(u => u.Id != Guid.Empty); s.DeleteWhere<Deal>(d => d.Id != Guid.Empty); s.DeleteWhere<Conversation>(c => c.Id != Guid.Empty);
            s.DeleteWhere<Review>(r => r.Id != Guid.Empty); s.DeleteWhere<Offer>(o => o.Id != Guid.Empty); s.DeleteWhere<ListingMatch>(m => m.Id != Guid.Empty);
            s.DeleteWhere<Ticket>(t => t.Id != Guid.Empty); s.DeleteWhere<Notification>(n => n.Id != Guid.Empty); s.DeleteWhere<AuditEntry>(a => a.Id != Guid.Empty);
            foreach (var id in issued) s.Delete<ApiKeyDocument>(id);
            await s.SaveChangesAsync();
        }
    }

    /// <summary>
    /// <para><b>‏(2026-10-29، مُراجَعَةُ تَيّارِ S2 الجَولَة ١) إشعاراتُ المُديرينَ تُربَطُ بِهُوِيَّةِ المَحذوفِ لا بِنَصِّ اسمِه — حَيّاً</b>: ما لا يُقاسُ في الذاكِرَة —
    /// <c>Body.Contains</c> بِالوَسيلَةِ (‏<c>+</c> و<c>@</c> في <c>LIKE</c>) والحَدّانِ بَعدَه. حِسابٌ ما زالَ اسمُه الافتِراضِيّ «عُضو جَديد» وإشعارُ تَسجيلِه
    /// القَديمُ «{الاسم} · {الهاتِف}»، وإشعارا تَسجيلِ عُضوَينِ آخَرَينِ (الجَديدُ بِلا وَسيلَة، والقَديمُ بِهاتِفِه هُو). تَسلسُلُ النُقطَةِ بِعَينِه.</para>
    /// </summary>
    [Fact]
    public async Task A_placeholder_named_deletion_erases_its_own_registration_notice_by_contact_and_no_one_elses_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = BuildStore();
        var slug = "zz-erase-" + Guid.NewGuid().ToString("N")[..8];
        var placeholder = ACommerce.Platform.I18n.LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, ACommerce.Kit.Auth.Server.AuthHandlers.NewMemberNameKey)!;
        const string myPhone = "+966500000553", theirPhone = "+966500000554";
        var me = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = placeholder, Phone = myPhone };
        var admin = Guid.NewGuid();
        Notification Registered(string body) => new()
            { Id = Guid.NewGuid(), UserId = admin, Type = "new_user", Title = "مُستَخدِم جَديد سَجَّل", Body = body, RelatedUrl = $"/admin/tenants/{slug}/users" };
        var mine = Registered($"{placeholder} · {myPhone}");
        var theirsNew = Registered(placeholder);
        var theirsOld = Registered($"{placeholder} · {theirPhone}");
        try
        {
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(me); s.Store(mine, theirsNew, theirsOld);
                await s.SaveChangesAsync();
            }
            AccountErasure.Counts counts;
            await using (var s = store.LightweightSession(slug))
            {
                var u = await s.LoadAsync<User>(me.Id);
                Assert.Null(AccountDeletion.Validate(u, await AccountErasure.OpenDealsAsync(s, me.Id), "حذف", "حذف"));
                var nameBefore = ActorName.NamesOf(u!);
                var contactsBefore = AccountErasure.ContactsOf(u!);
                AccountDeletion.Erase(u!, DateTime.UtcNow);
                s.Store(u!);
                counts = await AccountErasure.EraseCopiesAsync(s, slug, me.Id, nameBefore, contactsBefore);
                await s.SaveChangesAsync();
            }
            await using var q = store.QuerySession(slug);
            var (m, tn, to) = ((await q.LoadAsync<Notification>(mine.Id))!, (await q.LoadAsync<Notification>(theirsNew.Id))!, (await q.LoadAsync<Notification>(theirsOld.Id))!);
            output.WriteLine($"إشعاراتٌ مَمحُوَّة: {counts.Notifications} · إشعارُه بِلا وَسيلَتِه: {!m.Body.Contains(myPhone, StringComparison.Ordinal)} · " +
                             $"إشعارا غَيرِه كَما كُتِبا: {tn.Body == placeholder && to.Body == $"{placeholder} · {theirPhone}"}");
            Assert.Equal(1, counts.Notifications);
            Assert.Equal(("مُستَخدِم جَديد سَجَّل", AccountDeletion.ErasedName), (m.Title, m.Body));
            Assert.Equal(placeholder, tn.Body);
            Assert.Equal($"{placeholder} · {theirPhone}", to.Body);
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            s.DeleteWhere<User>(u => u.Id != Guid.Empty); s.DeleteWhere<Notification>(n => n.Id != Guid.Empty);
            await s.SaveChangesAsync();
        }
    }

    // ─── مَجرى الأَحداثِ بَعدَ الحَذف (مُراجَعَةُ تَيّارِ S2 الجَولَة ٢، ‏2026-10-29) ───────────────────────────────────────────────
    //
    // ما لا تَراه الصُفوفُ أَعلاه: التَذكِرَةُ والعَرضُ فيهِما وَثيقَتانِ مُخَزَّنَتانِ بِلا مَجرى، والصَفحاتُ والنِقاطُ تَبني مِن المَجرى. فَهُنا يُبدَأُ
    // المَجرى كَما تَبدَؤُه نُقطَتاه (‏`StartStream` بِـ`TicketCreated` و`OfferSubmitted`) ويُقرَأُ بَعدَ الحَذفِ كَما يَقرَؤُه قارِئوه.

    /// <summary>تَسلسُلُ نُقطَةِ الحَذفِ بِعَينِه — الأَسماءُ والوَسيلَتانِ قَبلَ <c>Erase</c>، ثُمَّ المَحوُ وحِفظٌ واحِد.</summary>
    private static async Task<AccountErasure.Counts> EraseLikeTheDoorAsync(DocumentStore store, string slug, Guid userId)
    {
        await using var s = store.LightweightSession(slug);
        var u = await s.LoadAsync<User>(userId);
        Assert.Null(AccountDeletion.Validate(u, await AccountErasure.OpenDealsAsync(s, userId), "حذف", "حذف"));
        var names = ActorName.NamesOf(u!);
        var contacts = AccountErasure.ContactsOf(u!);
        AccountDeletion.Erase(u!, DateTime.UtcNow);
        s.Store(u!);
        var counts = await AccountErasure.EraseCopiesAsync(s, slug, userId, names, contacts);
        await s.SaveChangesAsync();
        return counts;
    }

    /// <summary><b>«اليَوم» حَيّاً</b>: الحَذفُ يَمحو لَقطَةَ التَذكِرَة، وصَفحَةُ تَذكِرَةِ الاستوديو (<c>TenantAdminQueries.TicketAsync</c>) تَبنيها مِن مَجراها
    /// فَتَرسُمُ الاسمَ المَحذوفَ لِصاحِبِ المَتجَر.</summary>
    [Fact]
    public async Task Today_the_studio_ticket_page_reads_the_stream_and_shows_the_deleted_authors_name_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = BuildStore();
        var slug = "zz-erase-" + Guid.NewGuid().ToString("N")[..8];
        const string name = "كاتِبُ التَذكِرَةِ الحَيّ";
        var me = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = name, Phone = "+966500000555" };
        var ticketId = Guid.NewGuid();
        try
        {
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(me);
                s.Events.StartStream<Ticket>(ticketId, new TicketCreated(ticketId, me.Id, name, "سُؤال", "نَصّ", DateTime.UtcNow));
                await s.SaveChangesAsync();
            }
            var counts = await EraseLikeTheDoorAsync(store, slug, me.Id);
            await using var q = store.QuerySession(slug);
            var snapshot = (await q.LoadAsync<Ticket>(ticketId))!;
            var page = (await new TenantAdminQueries(store).TicketAsync(slug, ticketId))!;
            output.WriteLine($"تَذاكِرُ مَمحُوَّة: {counts.Tickets} · اللَقطَةُ مَمحُوَّة: {snapshot.AuthorName == AccountDeletion.ErasedName} · " +
                             $"صَفحَةُ الاستوديو تَرسُمُ الاسمَ المَحذوف: {page.AuthorName == name}");
            Assert.Equal(1, counts.Tickets);
            Assert.Equal(AccountDeletion.ErasedName, snapshot.AuthorName);
            Assert.Equal(name, page.AuthorName);
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            s.DeleteWhere<User>(u => u.Id != Guid.Empty); s.DeleteWhere<Ticket>(t => t.Id != Guid.Empty);
            s.Events.ArchiveStream(ticketId);
            await s.SaveChangesAsync();
        }
    }

    /// <summary><b>«اليَوم» حَيّاً</b>: عَرضُ العُضوِ المَحذوفِ يَبقى «مُعَلَّقاً»، ولَقطَتُه مَمحُوَّةٌ ومَجراه — الَّذي تَبني مِنه نُقطَةُ القَبول — بِاسمِه.</summary>
    [Fact]
    public async Task Today_a_deleted_members_pending_offer_stays_pending_and_its_stream_keeps_the_name_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = BuildStore();
        var slug = "zz-erase-" + Guid.NewGuid().ToString("N")[..8];
        const string name = "مُقَدِّمُ العَرضِ الحَيّ";
        var me = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = name, Phone = "+966500000556" };
        var offerId = Guid.NewGuid();
        try
        {
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(me);
                var now = DateTime.UtcNow;
                s.Events.StartStream<Offer>(offerId, new OfferSubmitted(offerId, Guid.NewGuid(), me.Id, name, 10m, null, 24.7, 46.7, now.AddDays(3), now));
                await s.SaveChangesAsync();
            }
            var counts = await EraseLikeTheDoorAsync(store, slug, me.Id);
            await using var q = store.LightweightSession(slug);
            var snapshot = (await q.LoadAsync<Offer>(offerId))!;
            var stream = (await q.Events.AggregateStreamAsync<Offer>(offerId))!;
            output.WriteLine($"عُروضٌ مَمحُوَّة: {counts.Offers} · اللَقطَةُ مَمحُوَّة: {snapshot.OffererName == AccountDeletion.ErasedName} · " +
                             $"المَجرى بِالاسمِ المَحذوف: {stream.OffererName == name} · الحالُ في المَجرى: {stream.Status} · في اللَقطَة: {snapshot.Status}");
            Assert.Equal(1, counts.Offers);
            Assert.Equal(AccountDeletion.ErasedName, snapshot.OffererName);
            Assert.Equal((name, OfferStatus.Pending, OfferStatus.Pending), (stream.OffererName, stream.Status, snapshot.Status));
        }
        finally
        {
            await using var s = store.LightweightSession(slug);
            s.DeleteWhere<User>(u => u.Id != Guid.Empty); s.DeleteWhere<Offer>(o => o.Id != Guid.Empty);
            s.Events.ArchiveStream(offerId);
            await s.SaveChangesAsync();
        }
    }
}
