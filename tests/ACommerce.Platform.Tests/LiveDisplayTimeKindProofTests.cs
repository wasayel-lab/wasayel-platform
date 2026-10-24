using ACommerce.Kit.Notifications;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ ما يُعيدُه Marten في `DateTime` — مَقيسٌ عَلى Postgres لا مُفتَرَض (‏2026-10-24، تَيّارُ S15 البَند B) ═══
//
// دالَّةُ العَرضِ تُحَوِّلُ «لَحظَةً مُخَزَّنَةً بِـUTC» إلى مِنطَقَةِ الثَقافَة؛ و`DateTimeKind` الَّذي يَعودُ مِن القاعِدَةِ يُقَرِّرُ أَيُعامَلُ
// كَـUTC أَم يُفسَّرُ بِمِنطَقَةِ الخادِم. فَيُقاسُ هُنا بِالمُسلسَلِ نَفسِه (System.Text.Json في Marten): لَحظَةٌ `Utc` تُكتَبُ بِـ`Z` وتَعودُ
// `Utc`، ولَحظَةٌ `Unspecified` (كاتِبٌ نَسِيَ `UtcNow`) تُكتَبُ بِلا لاحِقَةٍ وتَعودُ `Unspecified` — ولِذلكَ تُعامِلُها الدالَّةُ
// كَـUTC صَراحَةً. ولا يَعمَلُ في التَشغيلِ العادِيّ: يَتَخَطّى نَفسَه ما لَم تُضبَط `WASAYEL_LIVE_PROOF=1` و`ConnectionStrings__Postgres`
// (نَفسُ عَقدِ `LiveThemeProofTests`)، ويَكتُبُ وَثيقَتَينِ في مُستَأجِرِ اختِبارٍ مُسَمّىً بِالجَولَةِ ثُمَّ يَحذِفُهُما.
[Collection(LiveProofCollection.Name)]
public class LiveDisplayTimeKindProofTests(ITestOutputHelper output)
{
    private static bool Enabled => Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1";
    private static string? ConnectionString => Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    [Fact]
    public async Task Marten_returns_utc_for_a_utc_instant_and_unspecified_for_an_unspecified_one()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        using var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionString!);
            o.DatabaseSchemaName = "platform";
            o.Policies.AllDocumentsAreMultiTenanted();
            o.Schema.For<Notification>().Identity(x => x.Id);
            o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
        });

        var tenant = $"live-time-kind-{Guid.NewGuid():N}"[..28];
        var utc = new Notification { Id = Guid.NewGuid(), At = DisplayTimeZoneTests.FixedUtc };
        var unspecified = new Notification
        {
            Id = Guid.NewGuid(),
            At = DateTime.SpecifyKind(DisplayTimeZoneTests.FixedUtc, DateTimeKind.Unspecified),
        };

        await using (var w = store.LightweightSession(tenant))
        {
            w.Store(utc, unspecified);
            await w.SaveChangesAsync();
        }

        try
        {
            await using var r = store.QuerySession(tenant);
            var a = await r.LoadAsync<Notification>(utc.Id);
            var b = await r.LoadAsync<Notification>(unspecified.Id);
            var rawA = await r.QueryAsync<string>("select data->>'At' from platform.mt_doc_notification where id = ?", utc.Id);
            var rawB = await r.QueryAsync<string>("select data->>'At' from platform.mt_doc_notification where id = ?", unspecified.Id);
            output.WriteLine($"Utc ⇒ مُخَزَّن «{rawA.Single()}» ⇒ يَعودُ {a!.At.Kind} {a.At:O}");
            output.WriteLine($"Unspecified ⇒ مُخَزَّن «{rawB.Single()}» ⇒ يَعودُ {b!.At.Kind} {b.At:O}");

            Assert.EndsWith("Z", rawA.Single());
            Assert.Equal(DateTimeKind.Utc, a.At.Kind);
            Assert.Equal(DisplayTimeZoneTests.FixedUtc, a.At);

            Assert.DoesNotContain("Z", rawB.Single());
            Assert.Equal(DateTimeKind.Unspecified, b.At.Kind);
            Assert.Equal(DisplayTimeZoneTests.FixedUtc.Ticks, b.At.Ticks);
        }
        finally
        {
            await using var d = store.LightweightSession(tenant);
            d.Delete<Notification>(utc.Id);
            d.Delete<Notification>(unspecified.Id);
            await d.SaveChangesAsync();
        }
    }
}
