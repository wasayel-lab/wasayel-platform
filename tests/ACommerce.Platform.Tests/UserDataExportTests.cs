using System.Text;
using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Listings;
using ACommerce.Kit.Offers;
using ACommerce.Kit.Reviews;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Queries;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>«بَياناتي» — حَقُّ الاطِّلاعِ والنَقل</b> (‏2026-10-29، قَرارُ المالِك د53 «أ/ب/ج»): صَفحَةٌ يَقرَؤُها الإنسانُ ومِلَفٌّ تَقرَؤُه البَرامِج،
/// يَقرَآنِ الاستِعلامَ نَفسَه. والإسقاطُ (<see cref="UserDataExport.Project"/>) دالَّةٌ نَقِيَّةٌ تُقاسُ هُنا بِلا قاعِدَة: ما يَخُصُّ صاحِبَ الطَلَبِ يَخرُج،
/// وما يَخُصُّ غَيرَه لا يَخرُج — ولِكُلِّ قاعِدَةٍ صَفٌّ مُوجَبٌ وصَفٌّ سالِب. ثُمَّ الحارِسُ في التَوقيع، والمَدخَلُ في «حِسابي»، والنُصوص.</para>
/// </summary>
public class UserDataExportTests(ITestOutputHelper output)
{
    private const string TemplateRoot = "libs/templates/ACommerce.Templates.Customer.Marketplace";
    private static readonly DateTime Now = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static User Me() => new()
    {
        Id = Guid.NewGuid(), TenantSlug = "zz-data", FullName = "صاحِبُ الطَلَب", Phone = "966500000530", Email = "",
        NationalId = null, ActiveRole = "customer", AcceptedTermsVersion = 2, AcceptedTermsAt = Now.AddDays(-3),
        AttributesJson = new() { ["city"] = "الرياض" },
        PushSubscriptions = new() { new PushSubscription { Endpoint = "https://fcm.googleapis.com/x", P256dh = "k", Auth = "a" } },
        SessionVersion = 4,
    };

    private static UserDataExport Export(User me, IEnumerable<Deal>? deals = null, IEnumerable<Conversation>? convs = null,
        IEnumerable<Message>? messages = null, IEnumerable<Review>? reviews = null, IEnumerable<Offer>? offers = null, IEnumerable<Listing>? listings = null)
        => UserDataExport.Project("zz-data", Now, me, deals ?? [], convs ?? [], messages ?? [], reviews ?? [], offers ?? [], listings ?? [])!;

    // ─── الحِساب ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_account_carries_what_the_user_document_stores_and_no_operational_secret()
    {
        var me = Me();
        var x = Export(me);
        var json = Encoding.UTF8.GetString(UserDataExport.ToJsonBytes(x));
        output.WriteLine($"الحِساب: سِماتٌ {x.Account.Attributes.Count} · أَجهِزَةُ الإشعار {x.Account.PushDevices} · بايتات JSON {json.Length}");
        Assert.Equal((me.Id, "صاحِبُ الطَلَب", "966500000530", 1, 2), (x.Account.Id, x.Account.FullName, x.Account.Phone, x.Account.PushDevices, x.Account.AcceptedTermsVersion));
        Assert.Equal("الرياض", x.Account.Attributes["city"]);
        // مُوجَب: المَعروفُ يُكتَبُ بِحُروفِه؛ سالِب: مَفاتيحُ الإشعارِ وإصدارُ الجَلسَةِ لا تُكتَب.
        Assert.Contains("\"fullName\": \"صاحِبُ الطَلَب\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("fcm.googleapis.com", json, StringComparison.Ordinal);
        Assert.DoesNotContain("p256dh", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionVersion", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_missing_or_deleted_account_exports_nothing()
    {
        var deleted = Me();
        AccountDeletion.Erase(deleted, Now);
        Assert.Null(UserDataExport.Project("zz-data", Now, null, [], [], [], [], [], []));
        Assert.Null(UserDataExport.Project("zz-data", Now, deleted, [], [], [], [], [], []));
        Assert.NotNull(UserDataExport.Project("zz-data", Now, Me(), [], [], [], [], [], []));
    }

    // ─── الصَفَقات ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Deals_the_user_is_party_to_or_owns_the_listing_of_are_exported_and_foreign_ones_are_not()
    {
        var me = Me();
        var other = Guid.NewGuid();
        var bought = new Deal { Id = Guid.NewGuid(), InitiatorId = me.Id, CounterpartyId = other, CounterpartyName = "بائِع", ListingTitle = "اشتَرَيتُه" };
        var sold = new Deal { Id = Guid.NewGuid(), InitiatorId = other, InitiatorName = "مُشتَرٍ", CounterpartyId = me.Id, ListingTitle = "بِعتُه" };
        var incoming = new Deal { Id = Guid.NewGuid(), InitiatorId = other, ListingTitle = "عَرضٌ وارِد", Refs = new() { [DealsService.ListingOwnerRef] = me.Id.ToString() } };
        var foreign = new Deal { Id = Guid.NewGuid(), InitiatorId = other, CounterpartyId = Guid.NewGuid(), ListingTitle = "لِغَيري" };
        var foreignOffer = new Deal { Id = Guid.NewGuid(), InitiatorId = other, ListingTitle = "عَرضٌ عَلى إعلانِ غَيري", Refs = new() { [DealsService.ListingOwnerRef] = Guid.NewGuid().ToString() } };

        var x = Export(me, deals: [bought, sold, incoming, foreign, foreignOffer, bought]);
        output.WriteLine($"صَفَقاتٌ داخِلَة: {x.Deals.Count} — {string.Join(" · ", x.Deals.Select(d => $"{d.ListingTitle}/{d.Side}/{d.OtherPartyName}"))}");
        Assert.Equal(new[] { bought.Id, incoming.Id, sold.Id }.OrderBy(i => i), x.Deals.Select(d => d.Id).OrderBy(i => i));
        Assert.Equal(UserDataDealSide.Initiator, x.Deals.Single(d => d.Id == bought.Id).Side);
        Assert.Equal("بائِع", x.Deals.Single(d => d.Id == bought.Id).OtherPartyName);
        Assert.Equal(UserDataDealSide.Counterparty, x.Deals.Single(d => d.Id == sold.Id).Side);
        Assert.Equal("مُشتَرٍ", x.Deals.Single(d => d.Id == sold.Id).OtherPartyName);
        Assert.Equal(UserDataDealSide.Counterparty, x.Deals.Single(d => d.Id == incoming.Id).Side);
    }

    [Fact]
    public void The_delivery_address_and_phone_reach_the_buyer_who_typed_them_and_never_the_seller()
    {
        var me = Me();
        var other = Guid.NewGuid();
        var attrs = new Dictionary<string, string> { [Deal.DeliveryAddressAttribute] = "حَيُّ المِثال", [Deal.DeliveryPhoneAttribute] = "0500000000" };
        var mine = new Deal { Id = Guid.NewGuid(), InitiatorId = me.Id, CounterpartyId = other, Attributes = new(attrs) };
        var theirs = new Deal { Id = Guid.NewGuid(), InitiatorId = other, CounterpartyId = me.Id, Attributes = new(attrs) };

        var x = Export(me, deals: [mine, theirs]);
        var json = Encoding.UTF8.GetString(UserDataExport.ToJsonBytes(Export(me, deals: [theirs])));
        Assert.Equal(("حَيُّ المِثال", "0500000000"), (x.Deals.Single(d => d.Id == mine.Id).DeliveryAddress, x.Deals.Single(d => d.Id == mine.Id).DeliveryPhone));
        Assert.Equal(((string?)null, (string?)null), (x.Deals.Single(d => d.Id == theirs.Id).DeliveryAddress, x.Deals.Single(d => d.Id == theirs.Id).DeliveryPhone));
        Assert.DoesNotContain("حَيُّ المِثال", json, StringComparison.Ordinal);
        Assert.DoesNotContain("0500000000", json, StringComparison.Ordinal);
    }

    [Fact]
    public void The_timeline_says_which_steps_were_the_users_own()
    {
        var me = Me();
        var deal = new Deal
        {
            Id = Guid.NewGuid(), InitiatorId = me.Id,
            Timeline = new()
            {
                new DealEvent(DealStage.Offered, DealStage.Offered, "created", me.Id, me.FullName, null, Now),
                new DealEvent(DealStage.Offered, DealStage.Booked, "advanced", Guid.NewGuid(), "بائِع", "مُلاحَظَةٌ داخِلِيَّة", Now),
            },
        };
        var steps = Export(me, deals: [deal]).Deals.Single().Timeline;
        Assert.Equal(new[] { ("created", true), ("advanced", false) }, steps.Select(s => (s.Action, s.ByYou)));
        Assert.DoesNotContain("مُلاحَظَةٌ داخِلِيَّة", Encoding.UTF8.GetString(UserDataExport.ToJsonBytes(Export(me, deals: [deal]))), StringComparison.Ordinal);
    }

    // ─── المُحادَثاتُ والتَقييماتُ والعُروضُ والإعلانات ─────────────────────────────────────────────

    [Fact]
    public void Conversations_carry_their_own_messages_and_a_foreign_conversation_or_message_does_not_leak()
    {
        var me = Me();
        var other = Guid.NewGuid();
        var c = new Conversation { Id = Guid.NewGuid(), OwnerId = other, OwnerName = "مالِكُ الإعلان", PartnerId = me.Id, PartnerName = me.FullName };
        var foreign = new Conversation { Id = Guid.NewGuid(), OwnerId = other, PartnerId = Guid.NewGuid() };
        var msgs = new[]
        {
            new Message { Id = Guid.NewGuid(), ConversationId = c.Id, SenderId = me.Id, Body = "مَرحَباً", SentAt = Now.AddMinutes(-2) },
            new Message { Id = Guid.NewGuid(), ConversationId = c.Id, SenderId = other, Body = "أَهلاً", SentAt = Now.AddMinutes(-1) },
            new Message { Id = Guid.NewGuid(), ConversationId = foreign.Id, SenderId = other, Body = "سِرٌّ لِغَيرِك", SentAt = Now },
        };
        var x = Export(me, convs: [c, foreign], messages: msgs);
        var conv = Assert.Single(x.Conversations);
        Assert.Equal("مالِكُ الإعلان", conv.OtherPartyName);
        Assert.Equal(new[] { ("مَرحَباً", true), ("أَهلاً", false) }, conv.Messages.Select(m => (m.Body, m.FromYou)));
        Assert.DoesNotContain("سِرٌّ لِغَيرِك", Encoding.UTF8.GetString(UserDataExport.ToJsonBytes(x)), StringComparison.Ordinal);
    }

    [Fact]
    public void Reviews_written_and_received_are_exported_with_their_direction_and_foreign_reviews_are_not()
    {
        var me = Me();
        var written = new Review { Id = Guid.NewGuid(), AuthorId = me.Id, TargetUserId = Guid.NewGuid(), TargetUserName = "بائِع", Rating = 5 };
        var received = new Review { Id = Guid.NewGuid(), AuthorId = Guid.NewGuid(), AuthorName = "مُشتَرٍ", TargetUserId = me.Id, Rating = 4 };
        var foreign = new Review { Id = Guid.NewGuid(), AuthorId = Guid.NewGuid(), TargetUserId = Guid.NewGuid(), Rating = 1 };
        var x = Export(me, reviews: [written, received, foreign]);
        Assert.Equal(2, x.Reviews.Count);
        Assert.Equal((UserDataReviewDirection.Written, "بائِع"), (x.Reviews.Single(r => r.Id == written.Id).Direction, x.Reviews.Single(r => r.Id == written.Id).OtherPartyName));
        Assert.Equal((UserDataReviewDirection.Received, "مُشتَرٍ"), (x.Reviews.Single(r => r.Id == received.Id).Direction, x.Reviews.Single(r => r.Id == received.Id).OtherPartyName));
    }

    [Fact]
    public void Offers_and_listings_are_the_users_own_and_a_deleted_listing_is_still_exported_with_its_mark()
    {
        var me = Me();
        var mineOffer = new Offer { Id = Guid.NewGuid(), OffererId = me.Id, Price = 30m, Status = OfferStatus.Pending };
        var theirOffer = new Offer { Id = Guid.NewGuid(), OffererId = Guid.NewGuid(), Price = 99m };
        var owner = ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingEditService.OwnerAttribute;
        var live = new Listing { Id = Guid.NewGuid(), Title = "إعلاني", Attributes = new() { [owner] = me.Id.ToString() } };
        var gone = new Listing { Id = Guid.NewGuid(), Title = "إعلانٌ حَذَفتُه", IsDeleted = true, Attributes = new() { [owner] = me.Id.ToString() } };
        var theirs = new Listing { Id = Guid.NewGuid(), Title = "إعلانُ غَيري", Attributes = new() { [owner] = Guid.NewGuid().ToString() } };
        var orphan = new Listing { Id = Guid.NewGuid(), Title = "بِلا مالِك" };
        var x = Export(me, offers: [mineOffer, theirOffer], listings: [live, gone, theirs, orphan]);
        Assert.Equal(new[] { mineOffer.Id }, x.Offers.Select(o => o.Id));
        Assert.Equal(new[] { gone.Id, live.Id }.OrderBy(i => i), x.Listings.Select(l => l.Id).OrderBy(i => i));
        Assert.True(x.Listings.Single(l => l.Id == gone.Id).IsDeleted);
    }

    // ─── الصيغَة ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_machine_format_is_camel_case_json_with_named_values_and_readable_arabic()
    {
        var me = Me();
        var deal = new Deal { Id = Guid.NewGuid(), InitiatorId = me.Id, Stage = DealStage.Booked, Status = DealStatus.Active, ListingTitle = "سِلعَةٌ <b>" };
        var json = Encoding.UTF8.GetString(UserDataExport.ToJsonBytes(Export(me, deals: [deal])));
        output.WriteLine(json[..Math.Min(json.Length, 400)]);
        foreach (var key in new[] { "\"tenantSlug\"", "\"account\"", "\"deals\"", "\"conversations\"", "\"reviews\"", "\"offers\"", "\"listings\"" })
            Assert.Contains(key, json, StringComparison.Ordinal);
        Assert.Contains("\"stage\": \"booked\"", json, StringComparison.Ordinal);
        Assert.Contains("\"side\": \"initiator\"", json, StringComparison.Ordinal);
        Assert.Contains("سِلعَةٌ", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u0633", json, StringComparison.Ordinal);   // العَرَبِيَّةُ حُروفٌ لا رُموز
        Assert.DoesNotContain("<b>", json, StringComparison.Ordinal);       // ومِحرَفاتُ HTML مُرَمَّزَة
    }

    [Theory]
    [InlineData("2026-10-28T20:59:00Z", "my-data-zz-data-2026-10-28.json")]
    [InlineData("2026-10-28T21:00:00Z", "my-data-zz-data-2026-10-29.json")]
    public void The_file_name_names_the_store_and_the_local_day_and_no_person(string utc, string expected)
    {
        var at = DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
        Assert.Equal(expected, UserDataExport.FileName("zz-data", at, ACommerce.Kit.Culture.CultureTime.DefaultTimeZone));
    }

    // ─── البابان: الاستِعلامُ الواحِد، والحارِسُ في التَوقيع، والمَدخَل ────────────────────────────────

    private static string DownloadBody()
    {
        var mte = WriteEndpointGuardTests.StripComments(Read($"{TemplateRoot}/MarketplaceTemplateExtensions.cs"));
        var at = mte.IndexOf("app.MapGet(\"/{slug}/me/data/download\"", StringComparison.Ordinal);
        Assert.True(at > 0, "لا نُقطَةَ تَنزيلٍ لِـ«بَياناتي» — الأَداةُ عَمياء.");
        var end = mte.IndexOf(';', mte.IndexOf("}).", at, StringComparison.Ordinal));
        return mte[at..(end + 1)];
    }

    [Fact]
    public void The_page_and_the_download_read_the_one_query_behind_a_session_guard_declared_up_front()
    {
        var body = DownloadBody();
        var page = WriteEndpointGuardTests.StripMarkupComments(Read($"{TemplateRoot}/Components/Pages/MyData.razor"));
        var pageCalls = Regex.Matches(page, @"\bData\.ExportAsync\(").Count;
        var bodyCalls = Regex.Matches(body, @"\bdata\.ExportAsync\(").Count;
        output.WriteLine($"الصَفحَة: ExportAsync ×{pageCalls} · التَنزيل: ExportAsync ×{bodyCalls} · حارِسُ السِلسِلَة: {body.TrimEnd().EndsWith(".RequireAuth();", StringComparison.Ordinal)}");

        Assert.Equal((1, 1), (pageCalls, bodyCalls));
        Assert.Contains("Services.Queries.UserDataQueries data", body, StringComparison.Ordinal);
        Assert.Contains("@inject ACommerce.Templates.Customer.Marketplace.Services.Queries.UserDataQueries Data", page, StringComparison.Ordinal);
        // الصَفحَةُ لا تَقرَأُ غَيرَه — فَلا تَقولُ ما لا يَقولُه المِلَفّ.
        Assert.DoesNotContain("AccountQueries", page, StringComparison.Ordinal);
        Assert.DoesNotContain("DealsService", page, StringComparison.Ordinal);
        // الحارِسُ في التَوقيعِ لا في الجِسم (القاعِدَة ٦)، ومُعَرِّفُ صاحِبِ البَياناتِ مِن الجَلسَةِ لا مِن الطَلَب.
        Assert.EndsWith(".RequireAuth();", body.TrimEnd(), StringComparison.Ordinal);
        Assert.Contains("http.UserId()", body, StringComparison.Ordinal);
        Assert.DoesNotContain("req.Query", body, StringComparison.Ordinal);
        Assert.Contains("CacheControl = \"no-store\"", body, StringComparison.Ordinal);
        Assert.Contains("<GatedPage Slug=\"@Slug\" Role=\"@Role\" RequireAuth>", page, StringComparison.Ordinal);
        Assert.Contains("if (!Auth.IsAuthenticated) return;", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_account_page_reaches_my_data_by_a_click_and_the_page_reaches_its_download()
    {
        var me = WriteEndpointGuardTests.StripMarkupComments(Read($"{TemplateRoot}/Components/Pages/Me.razor"));
        var page = WriteEndpointGuardTests.StripMarkupComments(Read($"{TemplateRoot}/Components/Pages/MyData.razor"));
        Assert.Contains("Label=\"@(L[\"me.action.my_data\"])\"", me, StringComparison.Ordinal);
        Assert.Contains("AuthSession.LinkFor(Slug, Role, $\"me/data\")", me, StringComparison.Ordinal);
        Assert.Contains("href=\"@($\"/{Slug}/me/data/download\")\" download", page, StringComparison.Ordinal);
        Assert.Contains("/{slug}/me/data", ComplianceInspectorTests.RepoRoutes);
        Assert.Contains("/{slug}/me/data/download", ComplianceInspectorTests.RepoRoutes);
    }

    [Fact]
    public void The_privacy_texts_send_access_and_download_to_the_my_data_page_not_to_an_email()
    {
        var s7 = AccountDeletionRetentionTests.Bare(LocaleCatalog.Find(LocaleCatalog.Arabic, "platform.privacy.s7_body")!);
        var s7En = LocaleCatalog.Find("en", "platform.privacy.s7_body")!;
        var store = AccountDeletionRetentionTests.Bare(LocaleCatalog.Find(LocaleCatalog.Arabic, "legal.privacy.access")!);
        output.WriteLine($"s7: {s7}\nen: {s7En}\nالمَتجَر: {store}");
        Assert.Contains("«حسابي» ثم «بياناتي» بلا مراسلة", s7, StringComparison.Ordinal);
        Assert.Contains("on the Account page, then My data, without writing to us", s7En, StringComparison.Ordinal);
        Assert.Contains("«حسابي» ثم «بياناتي»", store, StringComparison.Ordinal);
        Assert.Equal("بياناتي", AccountDeletionRetentionTests.Bare(LocaleCatalog.Find(LocaleCatalog.Arabic, "me.action.my_data")!));
        var legal = WriteEndpointGuardTests.StripMarkupComments(Read($"{TemplateRoot}/Components/Pages/LegalHub.razor"));
        Assert.True(legal.IndexOf("legal.privacy.access", StringComparison.Ordinal) is var a && a > 0
                    && a < legal.IndexOf("legal.privacy.deletion", StringComparison.Ordinal));
    }

    /// <summary>كُلُّ مِفتاحٍ تَقرَؤُه الصَفحَةُ في القامُوسِ العَرَبيّ (القاعِدَة ١١) — ويُعَدُّ ما فُحِص، والصِفرُ يُحمِر (القاعِدَة ١٠).</summary>
    [Fact]
    public void Every_key_the_page_reads_exists_in_the_arabic_dictionary()
    {
        var page = WriteEndpointGuardTests.StripMarkupComments(Read($"{TemplateRoot}/Components/Pages/MyData.razor"));
        var keys = Regex.Matches(page, @"L(?:\.Markup|\.Format)?\[?\(?""(?<k>[a-z_.]+)""").Select(m => m.Groups["k"].Value).Distinct().ToList();
        var missing = keys.Where(k => LocaleCatalog.Find(LocaleCatalog.Arabic, k) is null).ToList();
        output.WriteLine($"مَفاتيحُ الصَفحَة: {keys.Count} · غائِبَة: {missing.Count}");
        Assert.True(keys.Count >= 30, $"أَداةٌ عَمياء: {keys.Count} مِفتاحاً.");
        Assert.Empty(missing);
    }
}

/// <summary>
/// <para><b>بُرهانُ د53 حَيّاً عَلى Postgres</b> (‏<c>WASAYEL_LIVE_PROOF=1</c>): الاستِعلامُ (<see cref="UserDataQueries.ExportAsync"/>) يُسأَلُ عَن
/// <c>List&lt;Guid&gt;.Contains</c> ومُستَأجِرِ الجَلسَةِ وثَمانِيَةِ أَنواعٍ مُتَعَدِّدَةِ الإيجار — ما لا يُقاسُ في الذاكِرَة. فَيُبذَرُ مَتجَرٌ وآخَرُ بِجِوارِه،
/// ويُقرَأُ ما لِصاحِبِ الطَلَبِ في الأَوَّلِ وَحدَه.</para>
/// </summary>
[Collection(LiveProofCollection.Name)]
public class LiveUserDataExportProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    /// <summary>تَركيبُ المَخزَنِ لِما يَلمِسُه الصَفّ — كَما يُرَكِّبُه المُضيف (<c>HostingExtensions</c>): الإعلانُ والعَرضُ لَقطَتا مَجرى (<c>Snapshot</c>
    /// بِلا مِفتاحِ إصدارٍ uuid)، والصَفقَةُ بِدالَّةِ القالِب (<see cref="DealsService.RegisterDocument"/>) — وإلّا طَلَبَ Marten مُخَطَّطاً غَيرَ مُخَطَّطِ المُضيف.</summary>
    private static DocumentStore BuildStore() => DocumentStore.For(o =>
    {
        o.Connection(LiveDealConcurrencyProofTests.ConnectionString!.TrimEnd(';') + ";Timeout=60;Command Timeout=120");
        o.DatabaseSchemaName = "platform";
        o.Policies.AllDocumentsAreMultiTenanted();
        o.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;
        o.Schema.For<ACommerce.Kit.Tenants.Tenant>().SingleTenanted().Identity(x => x.Id);
        o.Projections.Snapshot<Listing>(global::JasperFx.Events.Projections.SnapshotLifecycle.Inline);
        o.Projections.Snapshot<Offer>(global::JasperFx.Events.Projections.SnapshotLifecycle.Inline);
        DealsService.RegisterDocument(o);
        o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
    });

    [Fact]
    public async Task The_export_query_reads_the_users_rows_in_its_store_alone_on_Postgres()
    {
        if (!Enabled) return;

        await using var store = BuildStore();
        var slug = "zz-data-" + Guid.NewGuid().ToString("N")[..8];
        var neighbour = slug + "-n";
        var me = new User { Id = Guid.NewGuid(), TenantSlug = slug, FullName = "صاحِبُ الطَلَب", Phone = "966500000531" };
        var other = Guid.NewGuid();
        var owner = ACommerce.Templates.Customer.Marketplace.Services.Listings.ListingEditService.OwnerAttribute;
        var conv = new Conversation { Id = Guid.NewGuid(), OwnerId = other, OwnerName = "مالِك", PartnerId = me.Id, PartnerName = me.FullName };
        try
        {
            await using (var s = store.LightweightSession(slug))
            {
                s.Store(me);
                s.Store(new Deal { Id = Guid.NewGuid(), TenantSlug = slug, InitiatorId = me.Id, CounterpartyId = other, ListingTitle = "اشتَرَيتُه" });
                s.Store(new Deal { Id = Guid.NewGuid(), TenantSlug = slug, InitiatorId = other, ListingTitle = "عَرضٌ وارِد",
                    Refs = new() { [DealsService.ListingOwnerRef] = me.Id.ToString() } });
                s.Store(new Deal { Id = Guid.NewGuid(), TenantSlug = slug, InitiatorId = other, CounterpartyId = Guid.NewGuid(), ListingTitle = "لِغَيري" });
                s.Store(conv);
                s.Store(new Message { Id = Guid.NewGuid(), ConversationId = conv.Id, SenderId = me.Id, Body = "مَرحَباً", SentAt = DateTime.UtcNow });
                s.Store(new Review { Id = Guid.NewGuid(), AuthorId = other, AuthorName = "مُشتَرٍ", TargetUserId = me.Id, Rating = 5 });
                s.Store(new Offer { Id = Guid.NewGuid(), OffererId = me.Id, Price = 40m });
                s.Store(new Listing { Id = Guid.NewGuid(), TenantSlug = slug, Title = "إعلاني", Attributes = new() { [owner] = me.Id.ToString() } });
                s.Store(new Listing { Id = Guid.NewGuid(), TenantSlug = slug, Title = "إعلانُ غَيري", Attributes = new() { [owner] = other.ToString() } });
                await s.SaveChangesAsync();
            }
            // وفي المَتجَرِ المُجاوِرِ صَفقَةٌ بِاسمِه نَفسِه — لا تَعبُرُ الإيجار.
            await using (var s = store.LightweightSession(neighbour))
            {
                s.Store(new Deal { Id = Guid.NewGuid(), TenantSlug = neighbour, InitiatorId = me.Id, ListingTitle = "في مَتجَرٍ آخَر" });
                await s.SaveChangesAsync();
            }

            var x = await new UserDataQueries(store).ExportAsync(slug, me.Id, DateTime.UtcNow);
            output.WriteLine($"صَفَقات {x!.Deals.Count} · مُحادَثات {x.Conversations.Count} (رَسائِل {x.Conversations.Sum(c => c.Messages.Count)}) · تَقييمات {x.Reviews.Count} · عُروض {x.Offers.Count} · إعلانات {x.Listings.Count}");
            Assert.Equal(new[] { "اشتَرَيتُه", "عَرضٌ وارِد" }.OrderBy(t => t, StringComparer.Ordinal), x.Deals.Select(d => d.ListingTitle).OrderBy(t => t, StringComparer.Ordinal));
            Assert.Equal("مَرحَباً", Assert.Single(Assert.Single(x.Conversations).Messages).Body);
            Assert.Equal(UserDataReviewDirection.Received, Assert.Single(x.Reviews).Direction);
            Assert.Equal(40m, Assert.Single(x.Offers).Price);
            Assert.Equal("إعلاني", Assert.Single(x.Listings).Title);
            Assert.Null(await new UserDataQueries(store).ExportAsync(neighbour, me.Id, DateTime.UtcNow));
        }
        finally
        {
            foreach (var t in new[] { slug, neighbour })
            {
                await using var s = store.LightweightSession(t);
                s.DeleteWhere<User>(x => x.Id != Guid.Empty); s.DeleteWhere<Deal>(x => x.Id != Guid.Empty); s.DeleteWhere<Conversation>(x => x.Id != Guid.Empty);
                s.DeleteWhere<Message>(x => x.Id != Guid.Empty); s.DeleteWhere<Review>(x => x.Id != Guid.Empty); s.DeleteWhere<Offer>(x => x.Id != Guid.Empty);
                s.DeleteWhere<Listing>(x => x.Id != Guid.Empty);
                await s.SaveChangesAsync();
            }
        }
    }
}
