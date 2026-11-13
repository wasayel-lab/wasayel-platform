using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G17 — لا مُعَرِّفَ شَخصٍ في الدَفتَرِ إلّا اسماً مُستَعاراً حَتمِيّاً ═══
// (‏2026-11-12، الموجَة ١٣ W13-6·٣ — ADR-051 §١١؛ التَصميم §2 «Privacy (corrected)»). الاسمُ المُستَعارُ أَحادِيُّ الاتِّجاه وحَتمِيّ (مَنعُ
// التَكرارِ والمُطابَقَةُ يُعيدانِ حِسابَه)، ولا مِفتاحَ فيه عَن قَصد. والفَحصُ الحَيُّ عَلى قاعِدَةٍ حَقيقِيَّة في LiveMeterPersistenceProofTests.

/// <summary>
/// <para>‏(‏2026-11-13، مُراجَعَةُ المَرحَلَةِ ج الأولى · M6C-SEC-1 — ADR-051 §١٩) <b>حُكمُ التَسَرُّبِ الواحِدُ لِلدَفتَرِ بِجَدوَلَيه</b>: سُطورُه
/// (<c>meter.usage_entry</c>: المُعَرِّفُ والمَصدَر) ونِيّاتُه (<c>meter.outbound_intent</c>: المُعَرِّفُ والطَلَب) — حَقلٌ يَحمِلُ مُعَرِّفَ مُستَخدِمٍ مَخزونٍ بِصيغَةِ
/// <c>D</c> أَو <c>N</c> (أَيّاً كانَت حالَةُ الحُروف)، أَو مِفتاحَ صورَةٍ شَخصِيَّة (<c>/avatars/</c>)، تَسَرُّب. كانَ الفَحصُ الحَيُّ يَقرَأُ السُطورَ وَحدَها والمَصدَرَ
/// بِصيغَةِ <c>D</c> وَحدَها — والصادِرُ يَبني مُعَرِّفاتِه بِصيغَةِ <c>N</c>، والمَرفوضُ والمَجهولُ لا يَبلُغانِ إلّا النِيّات (مَقيسٌ عِندَ المُراجِعِ «تَسَرُّب 0» عَلى نِيَّةٍ
/// وسَطرٍ مَحقونَين). مُستَهلِكاه: <c>LiveMeterPersistenceProofTests</c> (G17 حَيّاً وعَيبُه المَحقون) وصُفوفُ الحُكمِ أَدناه.</para>
/// </summary>
internal static class LedgerPersonScan
{
    /// <summary>صَفُّ الدَفتَر: جَدوَلُه ومُعَرِّفُه وحَقلُه الثاني باسمِه وقيمَتِه.</summary>
    internal sealed record Row(string Table, string Id, string Field, string Value);

    internal static IReadOnlyList<string> Leaks(IEnumerable<string> userIds, IEnumerable<Row> rows)
    {
        var forms = userIds.Where(u => Guid.TryParse(u, out _)).SelectMany(u => new[] { Guid.Parse(u).ToString("D"), Guid.Parse(u).ToString("N") })
                           .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        bool Carries(string v) => v.Contains("/avatars/", StringComparison.Ordinal) || forms.Any(f => v.Contains(f, StringComparison.OrdinalIgnoreCase));
        var leaks = new List<string>();
        foreach (var r in rows)
        {
            if (Carries(r.Id)) leaks.Add($"{r.Table}:{r.Id}·id");
            if (Carries(r.Value)) leaks.Add($"{r.Table}:{r.Id}·{r.Field}");
        }
        return leaks;
    }
}

public class MeterPrivacyTests(ITestOutputHelper output)
{
    [Fact]
    public void The_pseudonym_is_deterministic_hex_and_never_carries_the_raw_id()
    {
        var raw = Guid.NewGuid().ToString("D");
        var p = UsageEntryId.Pseudonym("member.join", "store-a", raw);
        output.WriteLine($"[G17] {raw} ⇒ {p}");
        Assert.Equal(p, UsageEntryId.Pseudonym("member.join", "store-a", raw));
        Assert.Matches("^[0-9a-f]{32}$", p);
        Assert.DoesNotContain(raw, p, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(raw.Replace("-", ""), p, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(p, UsageEntryId.Pseudonym("member.join", "store-b", raw));
        Assert.NotEqual(p, UsageEntryId.Pseudonym("file.upload", "store-a", raw));
        Assert.NotEqual(p, UsageEntryId.Pseudonym("member.join", "store-a", Guid.NewGuid().ToString("D")));
    }

    /// <summary>القيمَةُ مُثَبَّتَةٌ بِحَرفِها: تَغييرُ الصيغَةِ (بادِئَة، تَرتيب، طول) يَكسِرُ مُطابَقَةَ كُلِّ سَطرٍ قَديم — فَيَحمَرُّ هُنا قَبلَ أَن يَكسِرَها.
    /// المَحسوبُ خارِجَ .NET (‏Python ‏hashlib، ‏2026-11-12): أَوَّلُ ١٦ بايتاً مِن SHA-256 فَوقَ
    /// <c>meter:member.join:store-a:00000000-0000-0000-0000-000000000001</c>.</summary>
    [Fact]
    public void The_pseudonym_format_is_pinned()
        => Assert.Equal("b4297cf15c2380cd069ea713645be47d",
                        UsageEntryId.Pseudonym("member.join", "store-a", "00000000-0000-0000-0000-000000000001"));

    /// <summary><b>مَصدَرُ <c>deal.create</c> الصَفقَةُ لا أَطرافُها</b>: مُعَرِّفُ المُبادِرِ والطَرَفِ الثاني لا يَظهَرانِ في مُعَرِّفِ السَطرِ ولا مَصدَرِه.</summary>
    [Fact]
    public void A_deal_create_entry_carries_no_person_id_of_the_deal()
    {
        var deal = new Deal { Id = Guid.NewGuid(), TenantSlug = "store-a", InitiatorId = Guid.NewGuid(), CounterpartyId = Guid.NewGuid() };
        var e = Assert.Single(MeterRules.Classify(new PendingSnapshot("store-a", [new PendingDocument(deal, true)]), null, MeterUnitCatalog.Default));
        foreach (var person in new[] { deal.InitiatorId, deal.CounterpartyId!.Value })
            foreach (var field in new[] { e.Id, e.SourceId })
            {
                Assert.DoesNotContain(person.ToString("D"), field, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(person.ToString("N"), field, StringComparison.OrdinalIgnoreCase);
            }
    }

    // ─── الصادِر (‏2026-11-12، W13-6·١٣ — التَصميم §2 «Privacy»: مِفتاحُ المِلَفِّ وعُنوانُ الاشتِراكِ لا يَدخُلانِ إلّا مُستَعارَين) ─────────────────────────

    /// <summary><b>مِفتاحُ الصورَةِ لا يَدخُلُ الدَفتَرَ</b>: رَفعُ صورَةِ مَلَفٍّ شَخصِيٍّ (المِفتاحُ <c>tenants/{slug}/avatars/{userId}.png</c>) — مُعَرِّفُ النِداءِ وطَلَبُه ومَصدَرُه
    /// لا تَحمِلُ المِفتاحَ ولا مُعَرِّفَ صاحِبِه بِأَيِّ صيغَة، والمَصدَرُ اسمُ المِفتاحِ المُستَعارُ بِحَرفِه.</summary>
    [Fact]
    public async Task An_avatar_upload_carries_neither_its_key_nor_its_owner_id_in_the_ledger_ids()
    {
        await using var s3 = new NafathS3PushRequestShapeCharacterizationTests.FakeS3();
        var recorder = new ProviderCallsTests.CallRecorder();
        using var storage = new ACommerce.Kit.Files.Providers.S3.S3FileStorage(
            Microsoft.Extensions.Options.Options.Create(new ACommerce.Kit.Files.Providers.S3.S3FileStorageOptions
            {
                Endpoint = s3.Url, Bucket = NafathS3PushRequestShapeCharacterizationTests.Bucket, AccessKeyId = NafathS3PushRequestShapeCharacterizationTests.AccessKey,
                SecretAccessKey = "privacy-secret", PublicBaseUrl = NafathS3PushRequestShapeCharacterizationTests.PublicBase,
            }),
            new ProviderCalls(recorder), Microsoft.Extensions.Logging.Abstractions.NullLogger<ACommerce.Kit.Files.Providers.S3.S3FileStorage>.Instance);
        var user = Guid.NewGuid();
        var key = $"tenants/store-a/avatars/{user}.png";
        await storage.UploadAsync(ProviderCall.ForChannel(OutboundUnits.FileUpload, "store-a", ProviderCall.FreshRequestId()), key, new MemoryStream(new byte[64]), "image/png");

        var (call, acceptance) = recorder.Calls[^1];
        output.WriteLine($"[G17] رَفع ⇒ {call.Id} · مَصدَر {call.SourceType}:{call.SourceId} · {acceptance?.Quantity} بايتاً");
        foreach (var field in new[] { call.Id, call.RequestId, call.SourceId! })
        {
            Assert.DoesNotContain(user.ToString("D"), field, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(user.ToString("N"), field, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("avatars", field, StringComparison.Ordinal);
        }
        Assert.Equal((ProviderCall.FileKeySource, UsageEntryId.Pseudonym(OutboundUnits.FileUpload, "store-a", key)), (call.SourceType, call.SourceId));
    }

    /// <summary><b>عُنوانُ الاشتِراكِ لا يَدخُلُ الدَفتَر</b> (يَحمِلُ مُعَرِّفَ مُتَصَفِّحِ صاحِبِه عِندَ خِدمَةِ الإشعار): مُعَرِّفُ الإرسالِ الإشعارُ واسمُ العُنوانِ المُستَعار.</summary>
    [Fact]
    public async Task A_push_id_carries_the_subscription_endpoint_only_as_a_pseudonym()
    {
        using var subscriber = new NafathS3PushRequestShapeCharacterizationTests.Subscriber();
        var recorder = new ProviderCallsTests.CallRecorder();
        var keys = WebPush.VapidHelper.GenerateVapidKeys();
        var cfg = Microsoft.Extensions.Configuration.MemoryConfigurationBuilderExtensions.AddInMemoryCollection(new Microsoft.Extensions.Configuration.ConfigurationBuilder(),
            new Dictionary<string, string?> { ["WebPush:PublicKey"] = keys.PublicKey, ["WebPush:PrivateKey"] = keys.PrivateKey }).Build();
        var service = new ACommerce.Templates.Customer.Marketplace.Services.WebPushService(cfg,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ACommerce.Templates.Customer.Marketplace.Services.WebPushService>.Instance, new ProviderCalls(recorder));
        NafathS3PushRequestShapeCharacterizationTests.Wire(service, new NafathS3PushRequestShapeCharacterizationTests.PushRecorder());
        var world = new DocWorld();
        var user = Guid.NewGuid();
        var notice = Guid.NewGuid();
        world.Put(new ACommerce.Kit.Auth.User { Id = user, TenantSlug = "store-a", PushSubscriptions = [subscriber.Row()] });
        await service.SendAsync(world.Store, "store-a", user, notice, "t", "b");

        var call = recorder.Calls[^1].Call;
        output.WriteLine($"[G17] إشعار ⇒ {call.Id}");
        var token = subscriber.Endpoint[(subscriber.Endpoint.LastIndexOf('/') + 1)..];
        foreach (var field in new[] { call.Id, call.RequestId })
        {
            Assert.DoesNotContain(subscriber.Endpoint, field, StringComparison.Ordinal);
            Assert.DoesNotContain(token, field, StringComparison.Ordinal);
            Assert.DoesNotContain(user.ToString("N"), field, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal($"{notice:N}:{UsageEntryId.Pseudonym(OutboundUnits.PushSend, "store-a", subscriber.Endpoint)}", call.RequestId);
    }

    // ─── ‏(‏2026-11-13، M6C-SEC-1) حُكمُ التَسَرُّبِ نَفسُه يُقاسُ قَبلَ أَن يُوثَقَ بِه (القاعِدَة ١٠) — كُلُّ صيغَةٍ وكُلُّ حَقلٍ وكُلُّ جَدوَل ───────────────

    private static readonly string Person = "5ee030c5-200e-4ca0-8ec0-c25056e7287a";

    [Theory]
    [InlineData("usage_entry", "deal.create:store-a:5ee030c5-200e-4ca0-8ec0-c25056e7287a", "source_id", "x", "usage_entry:deal.create:store-a:5ee030c5-200e-4ca0-8ec0-c25056e7287a·id")]
    [InlineData("usage_entry", "member.join:store-a:x", "source_id", "5ee030c5200e4ca08ec0c25056e7287a", "usage_entry:member.join:store-a:x·source_id")]
    [InlineData("usage_entry", "member.join:store-a:x", "source_id", "5EE030C5-200E-4CA0-8EC0-C25056E7287A", "usage_entry:member.join:store-a:x·source_id")]
    [InlineData("outbound_intent", "otp.sms.send:twilio:5ee030c5200e4ca08ec0c25056e7287a", "request_id", "x", "outbound_intent:otp.sms.send:twilio:5ee030c5200e4ca08ec0c25056e7287a·id")]
    [InlineData("outbound_intent", "otp.sms.send:twilio:x", "request_id", "5ee030c5200e4ca08ec0c25056e7287a", "outbound_intent:otp.sms.send:twilio:x·request_id")]
    [InlineData("outbound_intent", "file.upload:s3:x", "request_id", "tenants/store-a/avatars/x.png", "outbound_intent:file.upload:s3:x·request_id")]
    public void The_leak_verdict_sees_each_form_in_each_field_of_each_table(string table, string id, string field, string value, string expected)
    {
        var leaks = LedgerPersonScan.Leaks([Person, Guid.NewGuid().ToString("D")], [new(table, id, field, value)]);
        output.WriteLine($"[G17] {table} {id} · {field}={value} ⇒ {string.Join(" | ", leaks)}");
        Assert.Equal(expected, Assert.Single(leaks));
    }

    [Fact]
    public void A_pseudonym_or_another_id_is_not_a_leak()
    {
        var rows = new LedgerPersonScan.Row[]
        {
            new("usage_entry", $"member.join:store-a:{UsageEntryId.Pseudonym("member.join", "store-a", Person)}", "source_id", UsageEntryId.Pseudonym("member.join", "store-a", Person)),
            new("outbound_intent", $"otp.sms.send:twilio:{Guid.NewGuid():N}", "request_id", Guid.NewGuid().ToString("N")),
            new("usage_entry", $"deal.create:store-a:{Guid.NewGuid()}", "source_id", Guid.NewGuid().ToString("D")),
        };
        var leaks = LedgerPersonScan.Leaks([Person], rows);
        output.WriteLine($"[G17] صُفوفٌ نَظيفَة {rows.Length} ⇒ تَسَرُّب {leaks.Count}");
        Assert.Empty(leaks);
    }
}
