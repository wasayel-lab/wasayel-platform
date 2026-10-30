using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Notifications;
using ACommerce.Templates.Customer.Marketplace.Services.Listings;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ الطَرَفُ الآخَرُ أَغلَقَ حِسابَه — أَبوابُ المُحادَثَةِ اليَوم (‏2026-10-30، الموجَة ١٢ F2 · ٥، F2B-2) ═══════════════════════════════════
//
// **تَوصيفٌ قَبلَ التَبديل (القاعِدَة ٣)**: الحَذفُ يُبقي وَثيقَةَ الحِسابِ بِـ`DeletedAt` واسمِ «حِسابٌ مَحذوف» (`AccountDeletion.Erase`)، فَسُؤالُ
// `partner is null` في `POST /{slug}/users/{userId}/chat` لا يَراه — وتَعليقُ البابِ يَقولُ «يَعودُ إلَيه عِندَ الطَرَفِ المَحذوفِ بِرَمزِه». وبابُ البائِعِ
// `POST /{slug}/vendor/{vendorId}/chat` لا يَسأَلُ شَيئاً، وبابُ الإعلانِ `POST /{slug}/listings/{id}/chat` يَكتُبُ طَرَفاً مُعَرِّفُه `Guid.NewGuid()` لا عُضوَ
// لَه — فَمُحادَثَتُه لا تَصِلُ صاحِبَ الإعلانِ أَبَداً. وغُرفَةُ المُحادَثَةِ وصَفحَةُ البائِعِ تَرسُمانِ الكِتابَةَ والتَواصُلَ بِلا سُؤال. وبابُ الإرسالِ يَرمي قَبلَ أَيِّ حِفظٍ لِطَرَفَينِ قائِمَين (سُؤالُ الإشعارِ الأَخيرِ بِوَقتٍ عالَمِيٍّ — صَفُّه أَدناه)، فَقَبلَ
// أَن يُسأَلَ عَن الطَرَفِ يُصلَحُ في كوميتٍ مُستَقِلّ. صُفوفُ «Today_» تُثَبِّتُ ذلك كَما هُو، وتُقلَبُ بِأَسمائِها في كوميتِ التَبديل.

/// <summary>صَفحَتا المُحادَثَةِ والبائِعِ — شَكلُ الشَرطِ الَّذي يَرسُمُ الكِتابَةَ والتَواصُل.</summary>
public class ChatPartnerGoneSourceTests(ITestOutputHelper output)
{
    private static readonly string Pages = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages");

    private static string Page(string name) => WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(Pages, name)));

    /// <summary><b>تَوصيف</b>: الغُرفَةُ تَرسُمُ استِمارَةَ الإرسالِ لِكُلِّ مُحادَثَةٍ لَم تَنتَهِ صَلاحِيَّتُها — لا تَسأَلُ عَن الطَرَفِ الآخَر.</summary>
    [Fact]
    public void Today_the_chat_room_draws_the_composer_on_every_unexpired_conversation()
    {
        var room = Page("ChatRoom.razor");
        var composer = room.IndexOf("/send\")\"", StringComparison.Ordinal);
        var guard = room.LastIndexOf("@if (", composer, StringComparison.Ordinal);
        var condition = room[(guard + 5)..room.IndexOf('\n', guard)].Trim();
        output.WriteLine($"شَرطُ الاستِمارَة: {condition} · سُؤالُ الطَرَف: {room.Contains("PartnerGone", StringComparison.Ordinal)}");
        Assert.True(composer > 0);
        Assert.Equal("conv.IsExpired)", condition);
        Assert.DoesNotContain("PartnerGone", room, StringComparison.Ordinal);
    }

    /// <summary><b>تَوصيف</b>: صَفحَةُ البائِعِ تَرسُمُ زِرَّ «تَواصَل مَع البائِع» لِكُلِّ بائِعٍ إلّا نَفسَك — ولَو أَغلَقَ حِسابَه.</summary>
    [Fact]
    public void Today_the_vendor_page_draws_the_contact_button_for_any_vendor_but_oneself()
    {
        var page = Page("VendorProfile.razor");
        var form = page.IndexOf("/chat\")\"", StringComparison.Ordinal);
        var guard = page.LastIndexOf("@if (", form, StringComparison.Ordinal);
        var condition = page[(guard + 5)..page.IndexOf('\n', guard)].Trim();
        output.WriteLine($"شَرطُ الزِرّ: {condition}");
        Assert.True(form > 0);
        Assert.Equal("Auth.IsAuthenticated && vendor.Id != Auth.UserId)", condition);
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

    private static async Task<Guid> SeedListingAsync(IDocumentStore store, string slug, Guid owner)
    {
        var id = Guid.NewGuid();
        await using var s = store.LightweightSession(slug);
        s.Events.StartStream<ACommerce.Kit.Listings.Listing>(id, new ACommerce.Kit.Listings.ListingCreated(id, slug, "سِلعَةُ البائِع", null, 100m, "c",
            null, null, new Dictionary<string, string> { [ListingEditService.OwnerAttribute] = owner.ToString() }, DateTime.UtcNow));
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

    /// <summary><b>تَوصيف</b>: بابُ المُحادَثَةِ المُباشِرَةِ يَفتَحُ مُحادَثَةً مَعَ حِسابٍ مَمحُوّ — طَرَفُها مُعَرِّفُه واسمُها «حِسابٌ مَحذوف» — ويَنقُلُ المُشتَري إلَيها.</summary>
    [Fact]
    public async Task Today_the_user_chat_door_opens_a_conversation_with_an_erased_member_on_Postgres()
    {
        if (!Enabled) return;
        var slug = NewSlug();
        await using var host = LiveTemplateDoorHost.Build();
        var (buyer, seller, token) = await SeedAsync(host.Store, slug, erased: true);
        try
        {
            var origin = $"/{slug}/deals/{Guid.NewGuid()}";
            var (status, location) = await host.PostFormAsync(UserDoor, new Dictionary<string, string> { ["slug"] = slug, ["userId"] = seller.Id.ToString() },
                slug, token, ("returnUrl", origin));
            var (convs, _, _) = await ReadAsync(host.Store, slug);
            output.WriteLine($"البابُ: {status} → {location} · مُحادَثات {convs.Count} · طَرَفُها {(convs.Count == 1 && convs[0].PartnerId == seller.Id ? "المَمحُوّ" : "؟")} · {convs.FirstOrDefault()?.PartnerName}");
            var conv = Assert.Single(convs);
            Assert.Equal((302, $"/{slug}/chats/{conv.Id}"), (status, location));
            Assert.Equal((buyer.Id, seller.Id, AccountDeletion.ErasedName), (conv.OwnerId, conv.PartnerId, conv.PartnerName));
        }
        finally { await CleanAsync(host.Store, slug); }
    }

    /// <summary><b>تَوصيف</b>: بابُ البائِعِ لا يَسأَلُ شَيئاً — مُحادَثَةٌ مَعَ المَمحُوِّ بِاسمِه المَمحُوّ.</summary>
    [Fact]
    public async Task Today_the_vendor_chat_door_opens_a_conversation_with_an_erased_member_on_Postgres()
    {
        if (!Enabled) return;
        var slug = NewSlug();
        await using var host = LiveTemplateDoorHost.Build();
        var (buyer, seller, token) = await SeedAsync(host.Store, slug, erased: true);
        try
        {
            var (status, location) = await host.PostFormAsync(VendorDoor, new Dictionary<string, string> { ["slug"] = slug, ["vendorId"] = seller.Id.ToString() },
                slug, token);
            var (convs, _, _) = await ReadAsync(host.Store, slug);
            output.WriteLine($"البابُ: {status} → {location} · مُحادَثات {convs.Count} · {convs.FirstOrDefault()?.PartnerName}");
            var conv = Assert.Single(convs);
            Assert.Equal((302, $"/{slug}/chats/{conv.Id}"), (status, location));
            Assert.Equal((buyer.Id, seller.Id, AccountDeletion.ErasedName), (conv.OwnerId, conv.PartnerId, conv.PartnerName));
        }
        finally { await CleanAsync(host.Store, slug); }
    }

    /// <summary><b>تَوصيف</b>: بابُ الإعلانِ يَكتُبُ طَرَفاً لا عُضوَ لَه (<c>Guid.NewGuid()</c> واسمُه «صاحِب الإعلان») — لِمالِكٍ قائِمٍ ومالِكٍ مَمحُوٍّ سَواء:
    /// المُحادَثَةُ لا تَصِلُ المالِكَ أَبَداً، ولا يَسأَلُ البابُ عَنه.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Today_the_listing_chat_door_names_a_partner_who_is_no_member_on_Postgres(bool ownerErased)
    {
        if (!Enabled) return;
        var slug = NewSlug();
        await using var host = LiveTemplateDoorHost.Build();
        var (buyer, seller, token) = await SeedAsync(host.Store, slug, ownerErased);
        var listing = await SeedListingAsync(host.Store, slug, seller.Id);
        try
        {
            var (status, location) = await host.PostFormAsync(ListingDoor, new Dictionary<string, string> { ["slug"] = slug, ["id"] = listing.ToString() },
                slug, token);
            var (convs, _, _) = await ReadAsync(host.Store, slug);
            var conv = Assert.Single(convs);
            await using var q = host.Store.QuerySession(slug);
            var partner = await q.LoadAsync<User>(conv.PartnerId);
            output.WriteLine($"المالِكُ مَمحُوّ: {ownerErased} · البابُ: {status} → {location} · الطَرَفُ هُوَ المالِك: {conv.PartnerId == seller.Id} · لَه وَثيقَة: {partner is not null} · {conv.PartnerName}");
            Assert.Equal((302, $"/{slug}/chats/{conv.Id}"), (status, location));
            Assert.Equal(buyer.Id, conv.OwnerId);
            Assert.NotEqual(seller.Id, conv.PartnerId);
            Assert.Null(partner);
            Assert.Equal("صاحِب الإعلان", conv.PartnerName);
        }
        finally { await CleanAsync(host.Store, slug, listing); }
    }

    /// <summary><b>تَوصيف — وعَطَبٌ قُرِئَ أَثناءَ القِياس</b>: بابُ الإرسالِ يَرمي قَبلَ أَن يَحفَظَ شَيئاً — ولِطَرَفَينِ قائِمَين. سُؤالُه «أَلِلمُستَلِمِ إشعارٌ في آخِرِ ‏30
    /// ثانِيَة» يُقارِنُ <c>n.At &gt; since</c> بِـ<c>since = DateTime.UtcNow…</c>، وMarten يُتَرجِمُ الحَقلَ <c>timestamp without time zone</c> فَيَرُدُّ Npgsql الوَسيطَ
    /// (<c>InvalidUtcDateTimeUsageException</c>) — فَلا رِسالَةَ تُحفَظُ مِن هذا البابِ ولا إشعار. وهِيَ عِلَّةُ <c>ModelCallRecord.Instant</c> نَفسُها (ADR-031).</summary>
    [Fact]
    public async Task Today_the_send_door_throws_on_its_recent_notice_query_and_stores_nothing_on_Postgres()
    {
        if (!Enabled) return;
        var slug = NewSlug();
        await using var host = LiveTemplateDoorHost.Build();
        var (buyer, seller, token) = await SeedAsync(host.Store, slug, erased: false);
        var conv = await SeedConversationAsync(host.Store, slug, buyer, seller);
        try
        {
            var thrown = await Assert.ThrowsAsync<Marten.Exceptions.InvalidUtcDateTimeUsageException>(() => host.PostFormAsync(SendDoor,
                new Dictionary<string, string> { ["slug"] = slug, ["conversationId"] = conv.Id.ToString() }, slug, token, ("body", "هَل ما زالَ مُتاحاً؟")));
            var (_, messages, notices) = await ReadAsync(host.Store, slug);
            output.WriteLine($"البابُ رَمى: {thrown.GetType().Name} · رَسائِل {messages.Count} · إشعارات {notices.Count}");
            Assert.Equal((0, 0), (messages.Count, notices.Count));
        }
        finally { await CleanAsync(host.Store, slug); }
    }
}
