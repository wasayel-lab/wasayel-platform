using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Notifications;
using Marten;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ بابُ إرسالِ الرِسالَةِ بِما يَكتُبُه — يُثَبَّتُ قَبلَ أَن يَصيرَ `Store` الرِسالَةِ `Insert` (‏2026-11-12، الموجَة ١٣ W13-6·٩ — ADR-051 §٧ صَفُّ «٨ و٩») ═══
//
// **لِماذا الآن** (القاعِدَتانِ ٣ و١٣؛ التَصميم §3.4 و§5.2): الكوميتُ التالي يَعُدُّ `chat.message` مِن `Message` في `Inserts`، فَيَقلِبُ `s.Store(msg)` في
// `POST /{slug}/chats/{conversationId}/send` (‏`MarketplaceTemplateExtensions.cs:2277` عِندَ `b83fd69`؛ التَصميمُ `:2269` قَبلَ ثَمانِيَةِ أَسطُر) إلى
// `s.Insert(msg)`. والرِسالَةُ وَثيقَةٌ بِلا تَزامُنٍ تَفاؤُلِيّ: `Store` إدراجٌ-أَو-تَحديث (`mt_upsert_message`) و`Insert` إدراجٌ يَرمي عَلى مُعَرِّفٍ قائِم —
// والمُعَرِّفُ `Guid.NewGuid()` في كُلِّ إرسال. فَالدَعوى «لا يَتَغَيَّرُ شَيء» تُقاسُ ولا تُقال: هذا المِلَفُّ يُثَبِّتُ ما يَكتُبُه البابُ في `platform` —
// التَحويل، وصَفُّ الرِسالَةِ بِأَعمِدَتِه وحُقولِه ومُستَأجِرِه ونَوعِه، وتَحديثُ المُحادَثَة، والإشعارُ وحَدُّ الثَلاثينَ ثانِيَة، ومُعامَلَةُ الحِفظِ الواحِدَة
// (‏`xmin` الرِسالَةِ = `xmin` المُحادَثَةِ والإشعار) — ويُودَعُ وَحدَه، ثُمَّ يَبقى أَخضَرَ بَعدَ التَبديلِ **بِلا مَسِّ حَرف** (بَصمَتُه في رِسالَةِ كوميتِ التَبديل).
//
// **والبابُ بِجِسمِه المُسَجَّلِ لا نُسخَةٌ مِنه** (`LiveTemplateDoorHost`: تَركيبُ `AddPlatformHost` + `AddCustomerMarketplaceTemplate` بِعَينِه، ومُرَشِّحاتُ البابِ
// `RequireAuth` و`RequireTerms` و`RequireStoreWritable`). ويُقرَأُ الصَفُّ الخامُّ بِـNpgsql لا بِـMarten: ما يُقارَنُ هُوَ ما في الجَدوَل، لا ما يُعيدُه المُسَلسِل.
// ولا يُقرَأُ `meter.*` هُنا أَصلاً: سَطرُ العَدِّ يُثبِتُه بُرهانُه (`LiveMeterPersistenceProofTests`)، وهذا يُثبِتُ أَنّ التَبديلَ لا يَمَسُّ ما سِواه.
//
// **والرَفضُ لا يَكتُبُ شَيئاً**: جِسمٌ فارِغ، ومُحادَثَةٌ غائِبَة، وغَريبٌ عَنها، ومُحادَثَةٌ مُنتَهِيَة — كُلٌّ بِجَوابِه، وصِفرُ رِسالَةٍ وصِفرُ إشعار، والمُحادَثَةُ بِمُعامَلَتِها.

[Collection(LiveProofCollection.Name)]
public class LiveChatMessageSendCharacterizationProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private const string SendDoor = "/{slug}/chats/{conversationId:guid}/send";

    /// <summary>أَعمِدَةُ <c>platform.mt_doc_message</c> بِتَرتيبِها — مَقيسَةً عِندَ <c>b83fd69</c>.</summary>
    private const string MessageColumns = "tenant_id,id,data,mt_last_modified,mt_version,mt_dotnet_type";

    /// <summary>مَفاتيحُ <c>data</c> في صَفِّ الرِسالَة — مَقيسَةً عِندَ <c>b83fd69</c>.</summary>
    private const string MessageKeys = "Body,ConversationId,Id,SenderId,SentAt";

    private const string MessageType = "ACommerce.Kit.Chat.Message";

    private static string NewSlug() => "w13m9c-" + Guid.NewGuid().ToString("N")[..8];

    private static string NewPhone() => "+9665" + Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static User Member(string slug, string name) => new()
    {
        Id = Guid.NewGuid(), TenantSlug = slug, FullName = name, Phone = NewPhone(), PhoneVerified = true,
        AcceptedTermsAt = DateTime.UtcNow, AcceptedTermsVersion = ACommerce.Templates.Customer.Marketplace.Gates.TermsPolicy.CurrentVersion,
    };

    private static string Token(User u, string slug) => ACommerce.Kit.Auth.Server.AuthHandlers.MakeToken(u.Id, slug, u.SessionVersion);

    private static Dictionary<string, string> Route(string slug, Guid conversationId)
        => new() { ["slug"] = slug, ["conversationId"] = conversationId.ToString() };

    /// <summary>مُشتَرٍ (صاحِبُ المُحادَثَة) وبائِعٌ (طَرَفُها) وغَريبٌ — كُلُّهُم قَبِلوا الشُروطَ — ومُحادَثَةٌ بَينَ الأَوَّلَين، ومُنتَهِيَةٌ إن طُلِبَت.</summary>
    private static async Task<(User Buyer, User Seller, User Stranger, Conversation Conv)> SeedAsync(IDocumentStore store, string slug, DateTime? expiresAt = null)
    {
        var buyer = Member(slug, "مُشتَرٍ يُراسِل");
        var seller = Member(slug, "بائِعٌ يَرُدّ");
        var stranger = Member(slug, "غَريبٌ عَن المُحادَثَة");
        var conv = new Conversation
        {
            Id = Guid.NewGuid(), OwnerId = buyer.Id, OwnerName = buyer.FullName, PartnerId = seller.Id, PartnerName = seller.FullName,
            Subject = "تَواصُل", LastAt = DateTime.UtcNow.AddHours(-1), CreatedAt = DateTime.UtcNow.AddHours(-1), ExpiresAt = expiresAt,
        };
        await using var s = store.LightweightSession(slug);
        s.Store(buyer, seller, stranger);
        s.Store(conv);
        await s.SaveChangesAsync();
        return (buyer, seller, stranger, conv);
    }

    /// <summary>صَفُّ رِسالَةٍ خامّ: المُعَرِّفُ والمُستَأجِرُ والنَوعُ ومَفاتيحُ <c>data</c> ومُعامَلَتُه ونُسخَتُه ووَقتُ تَعديلِه.</summary>
    private sealed record RawRow(Guid Id, string Tenant, string DotnetType, string Keys, string Xmin, bool HasVersion, DateTime LastModified);

    private static async Task<List<RawRow>> RawMessagesAsync(string slug)
    {
        await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select id, tenant_id, mt_dotnet_type, (select string_agg(k, ',' order by k collate \"C\") from jsonb_object_keys(data) k), "
            + "xmin::text, mt_version is not null, mt_last_modified from platform.mt_doc_message where tenant_id = @t order by mt_last_modified, id", conn);
        cmd.Parameters.AddWithValue("t", slug);
        var rows = new List<RawRow>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            rows.Add(new RawRow(r.GetGuid(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetBoolean(5), r.GetDateTime(6)));
        return rows;
    }

    /// <summary><c>xmin</c> صَفٍّ و<c>mt_last_modified</c>ه في جَدوَلٍ مِن <c>platform</c> — مُعامَلَةُ آخِرِ كِتابَةٍ لَه.</summary>
    private static async Task<(string Xmin, DateTime LastModified)> TxOfAsync(string table, Guid id)
    {
        await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"select xmin::text, mt_last_modified from platform.{table} where id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync(), $"لا صَفَّ في platform.{table} بِالمُعَرِّف {id}");
        return (r.GetString(0), r.GetDateTime(1));
    }

    private static async Task<List<string>> NoticeXminsAsync(string slug)
    {
        await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select xmin::text from platform.mt_doc_notification where tenant_id = @t order by mt_last_modified, id", conn);
        cmd.Parameters.AddWithValue("t", slug);
        var xmins = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) xmins.Add(r.GetString(0));
        return xmins;
    }

    private static async Task<string> ColumnsAsync()
    {
        await using var conn = new NpgsqlConnection(LiveDealConcurrencyProofTests.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "select string_agg(column_name, ',' order by ordinal_position) from information_schema.columns where table_schema = 'platform' and table_name = 'mt_doc_message'", conn);
        return Convert.ToString(await cmd.ExecuteScalarAsync()) ?? "";
    }

    private static async Task<(Conversation Conv, List<Message> Messages, List<Notification> Notices)> ReadAsync(IDocumentStore store, string slug, Guid conversationId)
    {
        await using var q = store.QuerySession(slug);
        return ((await q.LoadAsync<Conversation>(conversationId))!,
                [.. (await q.Query<Message>().ToListAsync()).OrderBy(m => m.SentAt)],
                [.. (await q.Query<Notification>().ToListAsync()).OrderBy(n => n.At)]);
    }

    private static async Task CleanAsync(IDocumentStore store, string slug)
    {
        await using var s = store.LightweightSession(slug);
        s.DeleteWhere<Message>(m => m.Id != Guid.Empty);
        s.DeleteWhere<Conversation>(c => c.Id != Guid.Empty);
        s.DeleteWhere<Notification>(n => n.Id != Guid.Empty);
        s.DeleteWhere<User>(u => u.Id != Guid.Empty);
        await s.SaveChangesAsync();
    }

    /// <summary><b>إرسالٌ واحِد</b>: تَحويلٌ إلى المُحادَثَة، وصَفُّ رِسالَةٍ واحِدٌ بِأَعمِدَتِه ومَفاتيحِه ومُستَأجِرِه ونَوعِه، وجِسمُه مُشَذَّباً، ومُرسِلُه الصاحِب،
    /// ووَقتُه بَينَ ما قَبلَ الطَلَبِ وما بَعدَه؛ والمُحادَثَةُ آخِرُ رِسالَتِها هي ووَقتُها وَقتُها وغَيرُ المَقروءِ لِلطَرَفِ واحِد؛ وإشعارٌ واحِدٌ لِلطَرَفِ بِعُنوانِه
    /// ورابِطِه ووَقتِه — <b>والثَلاثَةُ في مُعامَلَةٍ واحِدَة</b> (‏<c>xmin</c> واحِد، و<c>mt_last_modified</c> الرِسالَةِ والمُحادَثَةِ واحِد).</summary>
    [Fact]
    public async Task One_send_stores_one_message_with_its_fields_updates_the_conversation_and_notifies_the_partner_in_one_transaction()
    {
        if (!Enabled) return;
        var slug = NewSlug();
        await using var host = LiveTemplateDoorHost.Build();
        var (buyer, seller, _, conv) = await SeedAsync(host.Store, slug);
        try
        {
            const string body = "  السَّلامُ عَلَيكُم، هَل ما زالَ مُتاحاً؟  ";
            var before = DateTime.UtcNow;
            var sent = await host.PostFormAsync(SendDoor, Route(slug, conv.Id), slug, Token(buyer, slug), ("body", body));
            var after = DateTime.UtcNow;

            var raw = await RawMessagesAsync(slug);
            var (stored, messages, notices) = await ReadAsync(host.Store, slug, conv.Id);
            var convTx = await TxOfAsync("mt_doc_conversation", conv.Id);
            var noticeTx = await NoticeXminsAsync(slug);
            var columns = await ColumnsAsync();
            output.WriteLine($"[send] {sent.Status} {sent.Location} · صُفوفُ الرِسالَة {raw.Count}: {string.Join(" | ", raw)}");
            output.WriteLine($"[send] الأَعمِدَة «{columns}» · المُحادَثَة xmin {convTx.Xmin} @ {convTx.LastModified:O} · الإشعارات xmin {string.Join(",", noticeTx)}");
            output.WriteLine($"[send] المُحادَثَة: «{stored.LastMessage}» {stored.LastAt:O} · غَيرُ مَقروءٍ لِلصاحِب {stored.OwnerUnread} · لِلطَرَف {stored.PartnerUnread}");

            Assert.Equal((302, $"/{slug}/chats/{conv.Id}"), sent);
            Assert.Equal(MessageColumns, columns);
            var row = Assert.Single(raw);
            var message = Assert.Single(messages);
            Assert.Equal((message.Id, slug, MessageType, MessageKeys, true), (row.Id, row.Tenant, row.DotnetType, row.Keys, row.HasVersion));
            Assert.Equal(4, message.Id.Version);
            Assert.Equal((conv.Id, buyer.Id, body.Trim()), (message.ConversationId, message.SenderId, message.Body));
            Assert.InRange(message.SentAt, before, after);

            Assert.Equal((body.Trim(), message.SentAt, 0, 1), (stored.LastMessage, stored.LastAt, stored.OwnerUnread, stored.PartnerUnread));
            Assert.Equal((buyer.Id, seller.Id, "تَواصُل", conv.CreatedAt, (DateTime?)null), (stored.OwnerId, stored.PartnerId, stored.Subject, stored.CreatedAt, stored.ExpiresAt));

            var notice = Assert.Single(notices);
            Assert.Equal((seller.Id, "chat_message", $"رِسالَة مِن {buyer.FullName}", body.Trim(), $"/{slug}/chats/{conv.Id}", message.SentAt, false),
                         (notice.UserId, notice.Type, notice.Title, notice.Body, notice.RelatedUrl, notice.At, notice.IsRead));

            Assert.Equal(convTx.Xmin, row.Xmin);
            Assert.Equal([row.Xmin], noticeTx);
            Assert.Equal(convTx.LastModified, row.LastModified);
        }
        finally { await CleanAsync(host.Store, slug); }
    }

    /// <summary><b>إرسالٌ مُتَكَرِّر</b>: الجِسمُ نَفسُه مَرَّتَينِ مِن الصاحِبِ رِسالَتانِ بِمُعَرِّفَينِ ومُعامَلَتَين (النَقرَتانِ رِسالَتان)، والإشعارُ الثاني لِلطَرَفِ
    /// يَحجُبُه حَدُّ الثَلاثينَ ثانِيَة؛ ثُمَّ الطَرَفُ يَرُدُّ بِجِسمٍ مِن 150 حَرفاً: يُحفَظُ كامِلاً، وآخِرُ رِسالَةٍ في المُحادَثَةِ أَوَّلُ مِئَةِ حَرفٍ مِنه، وإشعارٌ لِلصاحِب.</summary>
    [Fact]
    public async Task Repeated_sends_store_one_message_each_notify_once_per_thirty_seconds_and_cut_the_last_message_at_one_hundred_characters()
    {
        if (!Enabled) return;
        var slug = NewSlug();
        await using var host = LiveTemplateDoorHost.Build();
        var (buyer, seller, _, conv) = await SeedAsync(host.Store, slug);
        try
        {
            const string same = "هَل يُمكِنُ المُعايَنَةُ غَداً؟";
            var longBody = string.Concat(Enumerable.Repeat("أَهلاً بِك، ", 20))[..150];
            var first = await host.PostFormAsync(SendDoor, Route(slug, conv.Id), slug, Token(buyer, slug), ("body", same));
            var second = await host.PostFormAsync(SendDoor, Route(slug, conv.Id), slug, Token(buyer, slug), ("body", same));
            var reply = await host.PostFormAsync(SendDoor, Route(slug, conv.Id), slug, Token(seller, slug), ("body", longBody));

            var raw = await RawMessagesAsync(slug);
            var (stored, messages, notices) = await ReadAsync(host.Store, slug, conv.Id);
            output.WriteLine($"[send×3] {first} · {second} · {reply} · صُفوف {raw.Count} · مُعامَلات {raw.Select(r => r.Xmin).Distinct().Count()} · "
                + $"إشعاراتٌ لِلطَرَف {notices.Count(n => n.UserId == seller.Id)} · لِلصاحِب {notices.Count(n => n.UserId == buyer.Id)} · "
                + $"غَيرُ مَقروءٍ {stored.OwnerUnread}/{stored.PartnerUnread} · آخِرُ رِسالَة {stored.LastMessage?.Length} حَرفاً");

            var back = (302, $"/{slug}/chats/{conv.Id}");
            Assert.Equal([back, back, back], new[] { first, second, reply });
            Assert.Equal(3, raw.Count);
            Assert.Equal(3, raw.Select(r => r.Id).Distinct().Count());
            Assert.Equal(3, raw.Select(r => r.Xmin).Distinct().Count());
            Assert.All(raw, r => Assert.Equal((slug, MessageType, MessageKeys, true), (r.Tenant, r.DotnetType, r.Keys, r.HasVersion)));
            Assert.Equal([(buyer.Id, same), (buyer.Id, same), (seller.Id, longBody)], messages.Select(m => (m.SenderId, m.Body)));
            Assert.Equal(150, messages[2].Body.Length);

            Assert.Equal((longBody[..100], messages[2].SentAt, 1, 2), (stored.LastMessage, stored.LastAt, stored.OwnerUnread, stored.PartnerUnread));
            Assert.Equal([(seller.Id, same, messages[0].SentAt), (buyer.Id, longBody[..100], messages[2].SentAt)],
                         notices.Select(n => (n.UserId, n.Body, n.At)));
            Assert.Equal($"رِسالَة مِن {seller.FullName}", notices[1].Title);
        }
        finally { await CleanAsync(host.Store, slug); }
    }

    /// <summary><b>الرَفضُ لا يَكتُبُ شَيئاً</b>: جِسمٌ مِن مَسافاتٍ يُعادُ إلى المُحادَثَة، ومُحادَثَةٌ غائِبَةٌ إلى القائِمَة، والغَريبُ <c>403</c> بِلا تَحويل،
    /// والمُنتَهِيَةُ إلى المُحادَثَةِ بِـ<c>err=expired</c> — وفي كُلِّها صِفرُ رِسالَةٍ وصِفرُ إشعار، والمُحادَثَةُ بِمُعامَلَتِها كَما بُذِرَت.</summary>
    [Fact]
    public async Task An_empty_body_a_missing_conversation_a_stranger_and_an_expired_conversation_store_nothing()
    {
        if (!Enabled) return;
        var slug = NewSlug();
        var expiredSlug = NewSlug();
        await using var host = LiveTemplateDoorHost.Build();
        var (buyer, _, stranger, conv) = await SeedAsync(host.Store, slug);
        var (expiredBuyer, _, _, expired) = await SeedAsync(host.Store, expiredSlug, expiresAt: DateTime.UtcNow.AddHours(-1));
        try
        {
            var convTx = await TxOfAsync("mt_doc_conversation", conv.Id);
            var expiredTx = await TxOfAsync("mt_doc_conversation", expired.Id);
            var blank = await host.PostFormAsync(SendDoor, Route(slug, conv.Id), slug, Token(buyer, slug), ("body", "   "));
            var missing = await host.PostFormAsync(SendDoor, Route(slug, Guid.NewGuid()), slug, Token(buyer, slug), ("body", "رِسالَةٌ إلى لا أَحَد"));
            var foreign = await host.PostFormAsync(SendDoor, Route(slug, conv.Id), slug, Token(stranger, slug), ("body", "رِسالَةُ غَريب"));
            var late = await host.PostFormAsync(SendDoor, Route(expiredSlug, expired.Id), expiredSlug, Token(expiredBuyer, expiredSlug), ("body", "بَعدَ الانتِهاء"));

            var raw = (await RawMessagesAsync(slug)).Concat(await RawMessagesAsync(expiredSlug)).ToList();
            var (stored, _, notices) = await ReadAsync(host.Store, slug, conv.Id);
            var (storedExpired, _, expiredNotices) = await ReadAsync(host.Store, expiredSlug, expired.Id);
            var convAfter = await TxOfAsync("mt_doc_conversation", conv.Id);
            var expiredAfter = await TxOfAsync("mt_doc_conversation", expired.Id);
            output.WriteLine($"[refuse] فارِغ {blank} · غائِبَة {missing} · غَريب {foreign} · مُنتَهِيَة {late} · رَسائِل {raw.Count} · إشعارات {notices.Count + expiredNotices.Count} · "
                + $"المُحادَثَتانِ بِمُعامَلَتِهِما {convAfter == convTx}/{expiredAfter == expiredTx}");

            Assert.Equal((302, $"/{slug}/chats/{conv.Id}"), blank);
            Assert.Equal((302, $"/{slug}/chats"), missing);
            Assert.Equal((403, ""), foreign);
            Assert.Equal((302, $"/{expiredSlug}/chats/{expired.Id}?err=expired"), late);
            Assert.Empty(raw);
            Assert.Empty(notices);
            Assert.Empty(expiredNotices);
            Assert.Equal(convTx, convAfter);
            Assert.Equal(expiredTx, expiredAfter);
            Assert.Equal(((string?)null, 0, 0), (stored.LastMessage, stored.OwnerUnread, stored.PartnerUnread));
            Assert.Equal(((string?)null, 0, 0), (storedExpired.LastMessage, storedExpired.OwnerUnread, storedExpired.PartnerUnread));
        }
        finally
        {
            await CleanAsync(host.Store, slug);
            await CleanAsync(host.Store, expiredSlug);
        }
    }
}
