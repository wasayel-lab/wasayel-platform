using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Notifications;
using ACommerce.Templates.Customer.Marketplace.Services.Listings;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ الطَرَفُ الآخَرُ أَغلَقَ حِسابَه — أَبوابُ المُحادَثَةِ تَسأَلُ سُؤالاً واحِداً (‏2026-10-30، الموجَة ١٢ F2 · ٥، F2B-2) ════════════════════════════
//
// **التَوصيفُ (`9dc8354`، القاعِدَة ٣)**: الحَذفُ يُبقي وَثيقَةَ الحِسابِ بِـ`DeletedAt` واسمِ «حِسابٌ مَحذوف» (`AccountDeletion.Erase`)، فَسُؤالُ
// `partner is null` في `POST /{slug}/users/{userId}/chat` لم يَكُن يَراه — وتَعليقُ البابِ يَقولُ «يَعودُ إلَيه عِندَ الطَرَفِ المَحذوفِ بِرَمزِه». وبابُ البائِعِ
// `POST /{slug}/vendor/{vendorId}/chat` لا يَسأَلُ شَيئاً، وبابُ الإعلانِ `POST /{slug}/listings/{id}/chat` يَكتُبُ طَرَفاً مُعَرِّفُه `Guid.NewGuid()` لا عُضوَ
// لَه — فَمُحادَثَتُه لا تَصِلُ صاحِبَ الإعلانِ أَبَداً. وغُرفَةُ المُحادَثَةِ وصَفحَةُ البائِعِ تَرسُمانِ الكِتابَةَ والتَواصُلَ بِلا سُؤال. وبابُ الإرسالِ كانَ يَرمي
// قَبلَ أَيِّ حِفظٍ لِطَرَفَينِ قائِمَين (`da21e91`)، ثُمَّ يَحفَظُ رِسالَةً وإشعاراً لِلمَمحُوّ.
//
// **والتَبديل**: سُؤالٌ واحِدٌ نَقِيّ `AccountErasure.PartnerGone` (لا وَثيقَة، أَو `DeletedAt`) — و`OffererGone` اسمٌ لَه في مَوضِعِه. يَسأَلُه الأَبوابُ الأَربَعَةُ
// قَبلَ أَيِّ كِتابَة (`partner_missing`)، وتَسأَلُه الصَفَحاتُ الثَلاثُ الَّتي تَرسُمُ أَبوابَها؛ وطَرَفُ بابِ الإعلانِ مالِكُه (`CheckoutCart.OwnerOf`). صُفوفُ «Today_»
// قُلِبَت بِأَسمائِها، ولِكُلِّ بابٍ سالِبٌ يُثبِتُ أَنّ الطَرَفَ القائِمَ يَمُرّ.

/// <summary>صَفحَتا المُحادَثَةِ والبائِعِ — شَكلُ الشَرطِ الَّذي يَرسُمُ الكِتابَةَ والتَواصُل.</summary>
public class ChatPartnerGoneSourceTests(ITestOutputHelper output)
{
    private static readonly string Pages = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages");

    private static string Page(string name) => WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(Pages, name)));

    private static string Mte() => WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs")));

    /// <summary>جِسمُ بابٍ مِن حَرفِ مَسارِه إلى خاتِمَتِه — ويَفشَلُ إن لَم يَجِدْه (أَداةٌ لا تَجِدُ البابَ عَمياء).</summary>
    internal static string Door(string mte, string route)
    {
        var start = mte.IndexOf("\"" + route + "\"", StringComparison.Ordinal);
        Assert.True(start > 0, $"بابُ «{route}» غَيرُ مَوجود — الأَداةُ عَمياء.");
        var end = mte.IndexOf(".RequireStoreWritable();", start, StringComparison.Ordinal);
        Assert.True(end > start, $"خاتِمَةُ «{route}» غَيرُ مَوجودَة — الأَداةُ عَمياء.");
        return mte[start..end];
    }

    /// <summary>شَرطُ الـ<c>@if</c>/<c>else if</c> الأَقرَبِ قَبلَ <paramref name="at"/> — نَصُّه حَتّى آخِرِ السَطر.</summary>
    private static string ConditionBefore(string page, int at)
    {
        var guard = Math.Max(page.LastIndexOf("@if (", at, StringComparison.Ordinal), page.LastIndexOf("else if (", at, StringComparison.Ordinal));
        Assert.True(guard > 0, "لا شَرطَ قَبلَ المَوضِع — الأَداةُ عَمياء.");
        var open = page.IndexOf('(', guard);
        return page[(open + 1)..page.IndexOf('\n', guard)].Trim();
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b> — كانَ <c>Today_the_chat_room_draws_the_composer_on_every_unexpired_conversation</c> (‏<c>9dc8354</c>: الشَرطُ
    /// <c>conv.IsExpired</c> وَحدَه). والآن الغُرفَةُ تَسأَلُ <c>PartnerGone</c> عَن الطَرَفِ الآخَرِ أَوَّلاً فَتَرسُمُ سَطرَ القامُوسِ بَدَلَ الاستِمارَة، ثُمَّ الصَلاحِيَّةَ، ثُمَّ الاستِمارَة.</summary>
    [Fact]
    public void The_chat_room_asks_partner_gone_before_the_expiry_and_draws_its_line_instead_of_the_composer()
    {
        var room = Page("ChatRoom.razor");
        var composer = room.IndexOf("/send\")\"", StringComparison.Ordinal);
        var line = room.IndexOf("@L.Markup(\"chats.room.partner_gone\")", StringComparison.Ordinal);
        var gone = room.IndexOf("@if (partnerGone)", StringComparison.Ordinal);
        var expired = room.IndexOf("else if (conv.IsExpired)", StringComparison.Ordinal);
        output.WriteLine($"الاستِمارَة {composer} · السُؤال {gone} · السَطر {line} · الصَلاحِيَّة {expired} · شَرطُ الاستِمارَةِ الأَقرَب: {ConditionBefore(room, composer)}");
        Assert.True(gone > 0 && gone < line && line < expired && expired < composer);
        Assert.Equal("conv.IsExpired)", ConditionBefore(room, composer));
        Assert.Contains("partnerGone = ACommerce.Templates.Customer.Marketplace.Services.Identity.AccountErasure.PartnerGone(", room, StringComparison.Ordinal);
        Assert.Contains("await Account.ProfileAsync(Slug, conv.OwnerId == Auth.UserId ? conv.PartnerId : conv.OwnerId)", room, StringComparison.Ordinal);
        Assert.NotNull(ACommerce.Platform.I18n.LocaleCatalog.Text(ACommerce.Platform.I18n.LocaleCatalog.Arabic, "chats.room.partner_gone"));
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b> — كانَ <c>Today_the_vendor_page_draws_the_contact_button_for_any_vendor_but_oneself</c>. والآن الصَفحَةُ تَسأَلُ
    /// <c>PartnerGone(vendor)</c> قَبلَ الزِرِّ فَتَرسُمُ سَطرَ القامُوس، والزِرُّ في فَرعِ <c>else if</c> بِشَرطِه القَديم.</summary>
    [Fact]
    public void The_vendor_page_asks_partner_gone_before_drawing_the_contact_button()
    {
        var page = Page("VendorProfile.razor");
        var form = page.IndexOf("/chat\")\"", StringComparison.Ordinal);
        var gone = page.IndexOf("@if (ACommerce.Templates.Customer.Marketplace.Services.Identity.AccountErasure.PartnerGone(vendor))", StringComparison.Ordinal);
        var line = page.IndexOf("@L.Markup(\"vendors.profile.contact_gone\")", StringComparison.Ordinal);
        output.WriteLine($"السُؤال {gone} · السَطر {line} · الزِرّ {form} · شَرطُه: {ConditionBefore(page, form)}");
        Assert.True(gone > 0 && gone < line && line < form);
        Assert.Equal("Auth.IsAuthenticated && vendor.Id != Auth.UserId)", ConditionBefore(page, form));
        Assert.Contains("else if (Auth.IsAuthenticated && vendor.Id != Auth.UserId)", page, StringComparison.Ordinal);
    }

    /// <summary><b>مُوجَب</b>: صَفحَةُ الإعلانِ تَرسُمُ زِرَّي المُحادَثَةِ كِلَيهِما تَحتَ <c>chatOpens</c> وَحدَه، و<c>chatOpens</c> سُؤالُ البابِ بِعَينِه (<c>PartnerGone</c>
    /// عَلى حِسابِ المالِك)، والصَفحَةُ تَقرَأُ <c>partner_missing</c> بِسَطرٍ مِن القامُوس. <b>والعَدُّ</b>: زِرّانِ في الصَفحَةِ لا أَقَلّ — صِفرٌ أَداةٌ عَمياء.</summary>
    [Fact]
    public void The_listing_page_draws_both_chat_buttons_under_the_doors_own_question_and_reads_partner_missing()
    {
        var page = Page("TenantListingDetail.razor");
        var forms = System.Text.RegularExpressions.Regex.Matches(page, @"action=""@\(\$""/\{Slug\}/listings/\{Id\}/chat""\)""").Select(m => m.Index).ToList();
        var conditions = forms.Select(f => ConditionBefore(page, f)).ToList();
        output.WriteLine($"أَزرارُ المُحادَثَة: {forms.Count} · شُروطُها: {string.Join(" | ", conditions)}");
        Assert.Equal(2, forms.Count);
        Assert.All(conditions, c => Assert.Equal("chatOpens)", c));
        // ‏(‏2026-10-30، F2 · ٨) حِسابُ المالِكِ يُقرَأُ مَرَّةً لِقِسمِ التَواصُل (`ownerAccount`) ويَسأَلُه سُؤالا الزِرِّ والاستِمارَتَين — كانَ يُقرَأُ هُنا وَحدَه.
        Assert.Contains("chatOpens = !ACommerce.Templates.Customer.Marketplace.Services.Identity.AccountErasure.PartnerGone(ownerAccount);", page, StringComparison.Ordinal);
        Assert.Contains("var ownerAccount = ownerId is { } seller && (showOffers || contact) ? await Accounts.ProfileAsync(Slug, seller) : null;", page, StringComparison.Ordinal);
        Assert.Contains("else if (Err == ACommerce.Templates.Customer.Marketplace.Services.ChatStartReturn.PartnerMissing)", page, StringComparison.Ordinal);
        Assert.Contains("L[\"listings.detail.err_partner_missing\"]", page, StringComparison.Ordinal);
    }

    /// <summary><b>مُوجَب — سُؤالٌ واحِدٌ قَبلَ أَيِّ كِتابَة</b>: كُلٌّ مِن الأَبوابِ الأَربَعَةِ يَسأَلُ <c>AccountErasure.PartnerGone</c> قَبلَ أَوَّلِ <c>s.Store(</c>، ويَرُدُّ
    /// بِـ<c>ChatStartReturn.PartnerMissing</c>؛ ولا يَبقى في المَصدَرِ <c>partner is null</c> ولا الطَرَفُ الوَهمِيّ «صاحِب الإعلان». والعَدُّ أَربَعَة.</summary>
    [Fact]
    public void Every_chat_door_asks_partner_gone_before_its_first_write_and_answers_partner_missing()
    {
        var mte = Mte();
        var routes = new[] { "/{slug}/listings/{id:guid}/chat", "/{slug}/users/{userId:guid}/chat", "/{slug}/chats/{conversationId:guid}/send", "/{slug}/vendor/{vendorId:guid}/chat" };
        var asked = 0;
        foreach (var route in routes)
        {
            var body = Door(mte, route);
            var ask = body.IndexOf("Services.Identity.AccountErasure.PartnerGone(", StringComparison.Ordinal);
            var write = body.IndexOf("s.Store(", StringComparison.Ordinal);
            output.WriteLine($"{route}: السُؤالُ عِندَ {ask} · أَوَّلُ كِتابَةٍ عِندَ {write}");
            Assert.True(ask > 0 && write > ask, route);
            Assert.Contains("Services.ChatStartReturn.PartnerMissing", body, StringComparison.Ordinal);
            asked++;
        }
        Assert.Equal(4, asked);
        Assert.DoesNotContain("partner is null", mte, StringComparison.Ordinal);
        Assert.DoesNotContain("PartnerId = Guid.NewGuid()", mte, StringComparison.Ordinal);
        Assert.DoesNotContain("\"صاحِب الإعلان\"", mte, StringComparison.Ordinal);
    }

    /// <summary><b>مُوجَب وسالِب — الدالَّةُ النَقِيَّة</b>: غائِبٌ (<c>null</c>) ومَمحُوٌّ بِـ<c>AccountDeletion.Erase</c> ومَحذوفٌ بِتاريخٍ وَحدَه ⇒ غائِب؛ والقائِمُ —
    /// ولو اسمُه «حِسابٌ مَحذوف» حَرفاً — لَيسَ غائِباً (الاسمُ لا يَحكُم). و<c>OffererGone</c> يُجيبُ مِثلَه في كُلِّ حالَة.</summary>
    [Fact]
    public void Partner_gone_is_a_missing_or_deleted_account_and_offerer_gone_answers_the_same()
    {
        var live = new User { Id = Guid.NewGuid(), FullName = "عُضوٌ قائِم" };
        var erased = AccountDeletion.Erase(new User { Id = Guid.NewGuid(), FullName = "عُضوٌ يُغلِق" }, DateTime.UtcNow);
        var dated = new User { Id = Guid.NewGuid(), FullName = "عُضو", DeletedAt = DateTime.UtcNow };
        var namedLikeErased = new User { Id = Guid.NewGuid(), FullName = AccountDeletion.ErasedName };
        var cases = new (User? U, bool Gone)[] { (null, true), (erased, true), (dated, true), (live, false), (namedLikeErased, false) };
        foreach (var (u, gone) in cases)
        {
            Assert.Equal(gone, ACommerce.Templates.Customer.Marketplace.Services.Identity.AccountErasure.PartnerGone(u));
            Assert.Equal(gone, ACommerce.Templates.Customer.Marketplace.Services.Identity.AccountErasure.OffererGone(u));
        }
        output.WriteLine($"حالات {cases.Length} · غائِب {cases.Count(c => c.Gone)} · قائِم {cases.Count(c => !c.Gone)}");
    }
}

/// <summary>
/// <para><b>الأَبوابُ الأَربَعَةُ بِأَجسامِها المُسَجَّلَةِ عَلى Postgres</b> (<see cref="LiveTemplateDoorHost"/>): عُضوٌ يَطلُبُ مُحادَثَةً أَو يُرسِلُ رِسالَةً إلى حِسابٍ
/// مَحوُهُ بِـ<see cref="AccountDeletion.Erase"/> — الوَثيقَةُ باقِيَةٌ بِـ<c>DeletedAt</c>، كَما يَترُكُها بابُ الحَذف.</para>
/// </summary>
[Collection(LiveProofCollection.Name)]
public class LiveChatPartnerGoneProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private const string UserDoor = "/{slug}/users/{userId:guid}/chat";
    private const string VendorDoor = "/{slug}/vendor/{vendorId:guid}/chat";
    private const string ListingDoor = "/{slug}/listings/{id:guid}/chat";
    private const string SendDoor = "/{slug}/chats/{conversationId:guid}/send";

    private static User Member(string slug, string name, string phone) => new()
    {
        Id = Guid.NewGuid(), TenantSlug = slug, FullName = name, Phone = phone, PhoneVerified = true,
        AcceptedTermsAt = DateTime.UtcNow, AcceptedTermsVersion = ACommerce.Templates.Customer.Marketplace.Gates.TermsPolicy.CurrentVersion,
    };

    /// <summary>مُشتَرٍ قائِمٌ وبائِعٌ — مَمحُوٌّ بِـ<see cref="AccountDeletion.Erase"/> حينَ يُطلَب — ورَمزُ جَلسَةِ المُشتَري.</summary>
    private static async Task<(User Buyer, User Seller, string Token)> SeedAsync(IDocumentStore store, string slug, bool erased)
    {
        var buyer = Member(slug, "مُشتَرٍ يُراسِل", "+966500000571");
        var seller = Member(slug, "بائِعٌ يُغلِقُ حِسابَه", "+966500000572");
        if (erased) seller = AccountDeletion.Erase(seller, DateTime.UtcNow);
        await using var s = store.LightweightSession(slug);
        s.Store(buyer, seller);
        await s.SaveChangesAsync();
        return (buyer, seller, ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(buyer.Id, slug, buyer.SessionVersion));
    }

    private static async Task<Guid> SeedListingAsync(IDocumentStore store, string slug, Guid? owner)
    {
        var id = Guid.NewGuid();
        var attributes = owner is { } o ? new Dictionary<string, string> { [ListingEditService.OwnerAttribute] = o.ToString() } : new Dictionary<string, string>();
        await using var s = store.LightweightSession(slug);
        s.Events.StartStream<ACommerce.Kit.Listings.Listing>(id, new ACommerce.Kit.Listings.ListingCreated(id, slug, "سِلعَةُ البائِع", null, 100m, "c",
            null, null, attributes, DateTime.UtcNow));
        await s.SaveChangesAsync();
        return id;
    }

    private static async Task<Conversation> SeedConversationAsync(IDocumentStore store, string slug, User owner, User partner)
    {
        var conv = new Conversation
        {
            Id = Guid.NewGuid(), OwnerId = owner.Id, OwnerName = owner.FullName, PartnerId = partner.Id, PartnerName = partner.FullName,
            Subject = "تَواصُل", LastAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
        };
        await using var s = store.LightweightSession(slug);
        s.Store(conv);
        await s.SaveChangesAsync();
        return conv;
    }

    private static async Task<(List<Conversation> Conversations, List<Message> Messages, List<Notification> Notices)> ReadAsync(IDocumentStore store, string slug)
    {
        await using var q = store.QuerySession(slug);
        return ((await q.Query<Conversation>().ToListAsync()).ToList(),
                (await q.Query<Message>().ToListAsync()).ToList(),
                (await q.Query<Notification>().ToListAsync()).ToList());
    }

    private static async Task CleanAsync(IDocumentStore store, string slug, params Guid[] streams)
    {
        await using (var s = store.LightweightSession(slug))
        {
            s.DeleteWhere<Conversation>(c => c.Id != Guid.Empty);
            s.DeleteWhere<Message>(m => m.Id != Guid.Empty);
            s.DeleteWhere<Notification>(n => n.Id != Guid.Empty);
            s.DeleteWhere<ACommerce.Kit.Listings.Listing>(l => l.Id != Guid.Empty);
            foreach (var id in streams) s.Events.ArchiveStream(id);
            await s.SaveChangesAsync();
        }
        await LiveZeroRoleAdminProofTests.CleanAsync(store, slug);
    }

    private static string NewSlug() => "zz-chatgone-" + Guid.NewGuid().ToString("N")[..8];

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b> — كانَ <c>Today_the_user_chat_door_opens_a_conversation_with_an_erased_member_on_Postgres</c> (‏<c>9dc8354</c>:
    /// ‏302 إلى <c>chats/{id}</c> ومُحادَثَةٌ طَرَفُها المَمحُوّ). والآن البابُ يَعودُ إلى مَنشَئِه بِـ<c>partner_missing</c> ولا يَكتُبُ شَيئاً.</summary>
    [Fact]
    public async Task The_user_chat_door_sends_the_member_back_with_partner_missing_for_an_erased_member_and_writes_nothing_on_Postgres()
    {
        if (!Enabled) return;
        var slug = NewSlug();
        await using var host = LiveTemplateDoorHost.Build();
        var (_, seller, token) = await SeedAsync(host.Store, slug, erased: true);
        try
        {
            var origin = $"/{slug}/deals/{Guid.NewGuid()}";
            var (status, location) = await host.PostFormAsync(UserDoor, new Dictionary<string, string> { ["slug"] = slug, ["userId"] = seller.Id.ToString() },
                slug, token, ("returnUrl", origin));
            var (convs, _, _) = await ReadAsync(host.Store, slug);
            output.WriteLine($"البابُ: {status} → {location} · مُحادَثات {convs.Count}");
            Assert.Equal((302, origin + "?err=" + ACommerce.Templates.Customer.Marketplace.Services.ChatStartReturn.PartnerMissing), (status, location));
            Assert.Empty(convs);
        }
        finally { await CleanAsync(host.Store, slug); }
    }

    /// <summary><b>السالِب</b>: طَرَفٌ قائِمٌ — البابُ يَفتَحُ المُحادَثَةَ كَما كان، وطَرَفُها هُوَ.</summary>
    [Fact]
    public async Task The_user_chat_door_still_opens_a_conversation_with_a_live_member_on_Postgres()
    {
        if (!Enabled) return;
        var slug = NewSlug();
        await using var host = LiveTemplateDoorHost.Build();
        var (buyer, seller, token) = await SeedAsync(host.Store, slug, erased: false);
        try
        {
            var (status, location) = await host.PostFormAsync(UserDoor, new Dictionary<string, string> { ["slug"] = slug, ["userId"] = seller.Id.ToString() },
                slug, token, ("returnUrl", $"/{slug}/deals/{Guid.NewGuid()}"));
            var (convs, _, _) = await ReadAsync(host.Store, slug);
            var conv = Assert.Single(convs);
            output.WriteLine($"البابُ: {status} → {location} · {conv.PartnerName}");
            Assert.Equal((302, $"/{slug}/chats/{conv.Id}"), (status, location));
            Assert.Equal((buyer.Id, seller.Id, seller.FullName), (conv.OwnerId, conv.PartnerId, conv.PartnerName));
        }
        finally { await CleanAsync(host.Store, slug); }
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b> — كانَ <c>Today_the_vendor_chat_door_opens_a_conversation_with_an_erased_member_on_Postgres</c>. والآن البابُ
    /// يَعودُ إلى صَفحَةِ البائِعِ بِـ<c>partner_missing</c> ولا يَكتُبُ شَيئاً.</summary>
    [Fact]
    public async Task The_vendor_chat_door_sends_the_member_back_to_the_vendor_page_for_an_erased_vendor_and_writes_nothing_on_Postgres()
    {
        if (!Enabled) return;
        var slug = NewSlug();
        await using var host = LiveTemplateDoorHost.Build();
        var (_, seller, token) = await SeedAsync(host.Store, slug, erased: true);
        try
        {
            var (status, location) = await host.PostFormAsync(VendorDoor, new Dictionary<string, string> { ["slug"] = slug, ["vendorId"] = seller.Id.ToString() },
                slug, token);
            var (convs, _, _) = await ReadAsync(host.Store, slug);
            output.WriteLine($"البابُ: {status} → {location} · مُحادَثات {convs.Count}");
            Assert.Equal((302, $"/{slug}/vendor/{seller.Id}?err=" + ACommerce.Templates.Customer.Marketplace.Services.ChatStartReturn.PartnerMissing), (status, location));
            Assert.Empty(convs);
        }
        finally { await CleanAsync(host.Store, slug); }
    }

    /// <summary><b>السالِب</b>: بائِعٌ قائِم — المُحادَثَةُ تُفتَحُ كَما كانَت.</summary>
    [Fact]
    public async Task The_vendor_chat_door_still_opens_a_conversation_with_a_live_vendor_on_Postgres()
    {
        if (!Enabled) return;
        var slug = NewSlug();
        await using var host = LiveTemplateDoorHost.Build();
        var (buyer, seller, token) = await SeedAsync(host.Store, slug, erased: false);
        try
        {
            var (status, location) = await host.PostFormAsync(VendorDoor, new Dictionary<string, string> { ["slug"] = slug, ["vendorId"] = seller.Id.ToString() },
                slug, token);
            var (convs, _, _) = await ReadAsync(host.Store, slug);
            var conv = Assert.Single(convs);
            output.WriteLine($"البابُ: {status} → {location} · {conv.PartnerName}");
            Assert.Equal((302, $"/{slug}/chats/{conv.Id}"), (status, location));
            Assert.Equal((buyer.Id, seller.Id), (conv.OwnerId, conv.PartnerId));
        }
        finally { await CleanAsync(host.Store, slug); }
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b> — كانَ <c>Today_the_listing_chat_door_names_a_partner_who_is_no_member_on_Postgres</c> (‏<c>9dc8354</c>: طَرَفٌ
    /// <c>Guid.NewGuid()</c> بِلا وَثيقَةٍ لِمالِكٍ قائِمٍ ومَمحُوٍّ سَواء). <b>المُوجَب</b>: الطَرَفُ مالِكُ الإعلانِ بِاسمِه — فَالمُحادَثَةُ تَصِلُه.</summary>
    [Fact]
    public async Task The_listing_chat_door_names_the_listing_owner_as_the_partner_on_Postgres()
    {
        if (!Enabled) return;
        var slug = NewSlug();
        await using var host = LiveTemplateDoorHost.Build();
        var (buyer, seller, token) = await SeedAsync(host.Store, slug, erased: false);
        var listing = await SeedListingAsync(host.Store, slug, seller.Id);
        try
        {
            var route = new Dictionary<string, string> { ["slug"] = slug, ["id"] = listing.ToString() };
            var first = await host.PostFormAsync(ListingDoor, route, slug, token);
            var again = await host.PostFormAsync(ListingDoor, route, slug, token);
            var (convs, _, _) = await ReadAsync(host.Store, slug);
            var conv = Assert.Single(convs);
            output.WriteLine($"البابُ: {first.Status} → {first.Location} ثُمَّ {again.Location} · الطَرَفُ هُوَ المالِك: {conv.PartnerId == seller.Id} · {conv.PartnerName}");
            Assert.Equal((302, $"/{slug}/chats/{conv.Id}"), first);
            Assert.Equal(first, again);
            Assert.Equal((buyer.Id, seller.Id, seller.FullName, (Guid?)listing), (conv.OwnerId, conv.PartnerId, conv.PartnerName, conv.ListingId));
        }
        finally { await CleanAsync(host.Store, slug, listing); }
    }

    /// <summary><b>السالِب</b>: مالِكٌ مَمحُوٌّ، أَو إعلانٌ لا يُسَمّي مالِكاً — البابُ يَعودُ إلى صَفحَةِ الإعلانِ بِـ<c>partner_missing</c> ولا يَكتُبُ مُحادَثَة؛ ومالِكُ الإعلانِ
    /// يَعودُ إلَيها بِلا مُحادَثَةٍ مَعَ نَفسِه.</summary>
    [Theory]
    [InlineData("erased")]
    [InlineData("ownerless")]
    [InlineData("self")]
    public async Task The_listing_chat_door_writes_nothing_for_an_erased_or_missing_owner_or_the_owner_himself_on_Postgres(string kind)
    {
        if (!Enabled) return;
        var slug = NewSlug();
        await using var host = LiveTemplateDoorHost.Build();
        var (_, seller, token) = await SeedAsync(host.Store, slug, erased: kind == "erased");
        var listing = await SeedListingAsync(host.Store, slug, kind == "ownerless" ? null : seller.Id);
        if (kind == "self") token = ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(seller.Id, slug, seller.SessionVersion);
        try
        {
            var (status, location) = await host.PostFormAsync(ListingDoor, new Dictionary<string, string> { ["slug"] = slug, ["id"] = listing.ToString() }, slug, token);
            var (convs, _, _) = await ReadAsync(host.Store, slug);
            output.WriteLine($"{kind}: {status} → {location} · مُحادَثات {convs.Count}");
            var back = $"/{slug}/listings/{listing}";
            Assert.Equal((302, kind == "self" ? back : back + "?err=" + ACommerce.Templates.Customer.Marketplace.Services.ChatStartReturn.PartnerMissing), (status, location));
            Assert.Empty(convs);
        }
        finally { await CleanAsync(host.Store, slug, listing); }
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-30)</b> — كانَ <c>Today_the_send_door_throws_on_its_recent_notice_query_and_stores_nothing_on_Postgres</c>
    /// (‏<c>9dc8354</c>: <c>InvalidUtcDateTimeUsageException</c> عِندَ <c>n.At &gt; since</c>، صِفرُ رِسالَةٍ وصِفرُ إشعار). والحَدُّ الآنَ بِـ<c>ModelCallRecord.Instant</c>.
    /// <b>المُوجَب</b>: رِسالَتانِ مُتَتالِيَتانِ تُحفَظانِ، وإشعارٌ واحِدٌ لِلمُستَلِم — الثانِيَةُ في نافِذَةِ الـ‏30 ثانِيَةً لا تَكتُبُ ثانِياً.</summary>
    [Fact]
    public async Task The_send_door_stores_two_messages_and_one_notice_for_the_partner_within_thirty_seconds_on_Postgres()
    {
        if (!Enabled) return;
        var slug = NewSlug();
        await using var host = LiveTemplateDoorHost.Build();
        var (buyer, seller, token) = await SeedAsync(host.Store, slug, erased: false);
        var conv = await SeedConversationAsync(host.Store, slug, buyer, seller);
        try
        {
            var route = new Dictionary<string, string> { ["slug"] = slug, ["conversationId"] = conv.Id.ToString() };
            var first = await host.PostFormAsync(SendDoor, route, slug, token, ("body", "هَل ما زالَ مُتاحاً؟"));
            var second = await host.PostFormAsync(SendDoor, route, slug, token, ("body", "وكَم التَوصيل؟"));
            var (_, messages, notices) = await ReadAsync(host.Store, slug);
            output.WriteLine($"البابُ: {first.Status} ثُمَّ {second.Status} · رَسائِل {messages.Count} · إشعاراتٌ لِلبائِع {notices.Count(n => n.UserId == seller.Id)}");
            Assert.Equal((302, $"/{slug}/chats/{conv.Id}"), first);
            Assert.Equal(first, second);
            Assert.Equal(2, messages.Count(m => m.SenderId == buyer.Id));
            Assert.Equal(("chat_message", seller.Id), (Assert.Single(notices).Type, notices[0].UserId));
        }
        finally { await CleanAsync(host.Store, slug); }
    }

    /// <summary><b>السالِب</b>: إشعارُ مُحادَثَةٍ أَقدَمُ مِن ‏30 ثانِيَةً لا يَحجُبُ إشعارَ الرِسالَةِ الجَديدَة — فَالحَدُّ يُقارِنُ الوَقتَ فِعلاً لا «أَيَّ إشعار».</summary>
    [Fact]
    public async Task A_chat_notice_older_than_thirty_seconds_does_not_hold_back_the_next_one_on_Postgres()
    {
        if (!Enabled) return;
        var slug = NewSlug();
        await using var host = LiveTemplateDoorHost.Build();
        var (buyer, seller, token) = await SeedAsync(host.Store, slug, erased: false);
        var conv = await SeedConversationAsync(host.Store, slug, buyer, seller);
        try
        {
            await using (var s = host.Store.LightweightSession(slug))
            {
                s.Store(new Notification { Id = Guid.NewGuid(), UserId = seller.Id, Type = "chat_message", Title = "قَديم", Body = "—",
                    RelatedUrl = $"/{slug}/chats/{conv.Id}", At = DateTime.UtcNow.AddSeconds(-90) });
                await s.SaveChangesAsync();
            }
            var sent = await host.PostFormAsync(SendDoor, new Dictionary<string, string> { ["slug"] = slug, ["conversationId"] = conv.Id.ToString() },
                slug, token, ("body", "ما زِلتُ مُهتَمّاً"));
            var (_, messages, notices) = await ReadAsync(host.Store, slug);
            output.WriteLine($"البابُ: {sent.Status} · رَسائِل {messages.Count} · إشعاراتٌ لِلبائِع {notices.Count(n => n.UserId == seller.Id)}");
            Assert.Equal(302, sent.Status);
            Assert.Single(messages);
            Assert.Equal(2, notices.Count(n => n.UserId == seller.Id && n.Type == "chat_message"));
        }
        finally { await CleanAsync(host.Store, slug); }
    }

    /// <summary><b>قُلِبَ «Today_» بِاسمِه</b> — كانَ <c>Today_the_send_door_stores_a_message_and_a_notice_for_an_erased_partner_on_Postgres</c> (‏<c>da21e91</c>:
    /// رِسالَةٌ وإشعارٌ لِلمَمحُوّ). والآن البابُ يَرُدُّ بِـ<c>partner_missing</c> قَبلَ الرِسالَةِ والإشعار — وسالِبُه صَفُّ الطَرَفِ القائِمِ أَعلاه (رِسالَتانِ وإشعار).
    /// ومُحادَثَةٌ طَرَفُها بِلا وَثيقَةٍ أَصلاً (ما كَتَبَه بابُ الإعلانِ قَبلُ) تُرَدُّ بِالسُؤالِ نَفسِه.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_send_door_refuses_a_gone_partner_with_partner_missing_and_stores_no_message_or_notice_on_Postgres(bool erasedNotMissing)
    {
        if (!Enabled) return;
        var slug = NewSlug();
        await using var host = LiveTemplateDoorHost.Build();
        var (buyer, seller, token) = await SeedAsync(host.Store, slug, erased: true);
        var partner = erasedNotMissing ? seller : new User { Id = Guid.NewGuid(), FullName = "صاحِب الإعلان" };
        var conv = await SeedConversationAsync(host.Store, slug, buyer, partner);
        try
        {
            var (status, location) = await host.PostFormAsync(SendDoor, new Dictionary<string, string> { ["slug"] = slug, ["conversationId"] = conv.Id.ToString() },
                slug, token, ("body", "هَل ما زالَ مُتاحاً؟"));
            var (convs, messages, notices) = await ReadAsync(host.Store, slug);
            output.WriteLine($"{(erasedNotMissing ? "مَمحُوّ" : "بِلا وَثيقَة")}: {status} → {location} · رَسائِل {messages.Count} · إشعارات {notices.Count} · آخِرُ رِسالَة {convs[0].LastMessage ?? "—"}");
            Assert.Equal((302, $"/{slug}/chats/{conv.Id}?err=" + ACommerce.Templates.Customer.Marketplace.Services.ChatStartReturn.PartnerMissing), (status, location));
            Assert.Equal((0, 0), (messages.Count, notices.Count));
            Assert.Null(convs[0].LastMessage);
        }
        finally { await CleanAsync(host.Store, slug); }
    }
}
