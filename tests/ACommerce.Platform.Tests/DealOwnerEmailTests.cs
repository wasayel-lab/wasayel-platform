using ACommerce.Kit.Auth;
using ACommerce.Kit.Notifications;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ بَريدُ صاحِبِ المَتجَرِ بِما عَلَيه أَن يَفعَل (‏2026-10-30، تَدقيقُ الموجَة ١١ CO3) ════════════════════════════════════════════════
//
// **الحالُ المَقيسَة**: `DealNotices.NotifyStoreOwnerAsync` يَكتُبُ إشعارَ صاحِبِ المَتجَرِ (استِرجاعٌ مُستَحَقّ، فاتورَةٌ لَم تُلغَ، رَدٌّ نَقدِيّ، مُهلَةٌ لَم
// تُلغَ) ويَقرَعُ الجَرَسَ ويَدفَعُ إلى المُتَصَفِّح — ولا بَريد. وهذِه أَفعالٌ في المال: صاحِبُ مَتجَرٍ لا يَفتَحُ الاستوديو لا يَعلَمُ أَنّ عَلَيه أَن يَستَرجِعَ
// لِمُشتَرٍ أَو يُلغِيَ فاتورَة. وقَناةُ البَريدِ تَعرِفُ «إشعاراً لا رَمزاً» مُنذُ F1 (`IEmailOtpChannel.SendNoticeAsync`) ولِصاحِبِ الاستوديو بَريدٌ يَدخُلُ بِه.

public class DealOwnerEmailTests(ITestOutputHelper output)
{
    private const string Slug = "owner-mail";
    private static readonly Guid Owner = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
    private const string OwnerEmail = "owner@store.example";
    private const string BuyerPhone = "+966500000123";
    private const string BuyerAddress = "حَيُّ النَّخيل، شارِعُ ١٢";

    internal sealed class Capture : IEmailOtpChannel
    {
        public string ChannelName => "capture";
        public string? DevHintCode => null;
        public Exception? Throws;
        public readonly List<(string To, string Subject, string Text)> Notices = new();
        public Task SendOtpAsync(string email, string code, CancellationToken ct) => Task.CompletedTask;
        public Task SendNoticeAsync(string email, string subject, string text, CancellationToken ct)
        {
            if (Throws is not null) throw Throws;
            Notices.Add((email, subject, text));
            return Task.CompletedTask;
        }
    }

    /// <summary>عالَمٌ بِمَتجَرٍ لَه صاحِبٌ في الاستوديو وصَفقَةٍ مُلغاةٍ مَدفوعَة — و<see cref="DealNotices"/> مِن الحاوِيَةِ كَما يُسَجِّلُها القالِب
    /// (<c>AddScoped&lt;DealNotices&gt;</c>)، ومَعَها قَناةُ البَريدِ إن مُرِّرَت.</summary>
    private static (DocWorld Docs, DealNotices Notices, Deal Deal) World(IEmailOtpChannel? channel, string ownerEmail = OwnerEmail)
    {
        var docs = new DocWorld();
        docs.Put(new ACommerce.Kit.Tenants.Tenant { Id = Slug, Name = "مَتجَرُ النَّخلَة", OwnerUserId = Owner });
        docs.Put(new StudioUser { Id = Owner, Email = ownerEmail, Phone = "+966511111111", FullName = "صاحِبُ المَتجَر" });
        var deal = new Deal
        {
            Id = Guid.NewGuid(), Pattern = "marketplace", Status = DealStatus.Cancelled, InitiatorId = Guid.NewGuid(),
            ListingTitle = "مِصباحُ نُحاس", AmountSar = 150m,
        };
        deal.Refs["payment_id"] = "pay_1";
        deal.Refs[DealsService.PaymentCurrencyRef] = "SAR";
        deal.Refs["delivery_phone"] = BuyerPhone;
        deal.Refs["delivery_address"] = BuyerAddress;
        docs.Put(deal);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        services.AddSingleton<IDocumentStore>(docs.Store);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<WebPushService>();
        if (channel is not null) services.AddSingleton(channel);
        services.AddScoped<DealNotices>();
        var notices = services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<DealNotices>();
        return (docs, notices, deal);
    }

    private static int OwnerNotices(DocWorld docs) => docs.Stored.OfType<Notification>().Count(n => n.UserId == Owner);

    /// <summary><b>قُلِبَ بِاسمِه</b> (تَوصيفُه <c>a1fd5d2</c> — كانَ <c>Today_the_store_owner_with_an_email_gets_the_studio_notice_and_no_email</c>: إشعارانِ وصِفرُ بَريد):
    /// صاحِبُ مَتجَرٍ لَه بَريدٌ وقَناةُ البَريدِ مُسَجَّلَة ⇒ <b>بَريدٌ واحِدٌ لِكُلِّ إشعار</b> إلى بَريدِه في الاستوديو؛ عُنوانُه اسمُ المَتجَرِ وعُنوانُ الإشعار،
    /// ومَتنُه مَتنُ الإشعارِ والمَتجَرُ والطَلَبُ وسَطرُ المالِ وصَفحَةُ الطَلَبِ في الاستوديو — <b>ولا هاتِفَ مُشتَرٍ ولا عُنوانَه</b>.</summary>
    [Fact]
    public async Task The_store_owner_with_an_email_gets_one_email_per_notice_without_buyer_data()
    {
        var mail = new Capture();
        var (docs, notices, deal) = World(mail);
        var l = new L(new HttpContextAccessor());

        Assert.True(await notices.NotifyStoreOwnerAsync(Slug, deal, DealNotices.OwnerAction.RefundDue, l));
        Assert.True(await notices.NotifyStoreOwnerAsync(Slug, deal, DealNotices.OwnerAction.InvoiceLeftOpen, l));

        output.WriteLine($"إشعاراتُ صاحِبِ المَتجَر: {OwnerNotices(docs)} · رَسائِلُ البَريد: {mail.Notices.Count}");
        foreach (var m in mail.Notices) output.WriteLine($"→ {m.To} · {m.Subject}\n{m.Text}");
        Assert.Equal(2, OwnerNotices(docs));
        Assert.Equal(2, mail.Notices.Count);
        Assert.All(mail.Notices, m => Assert.Equal(OwnerEmail, m.To));
        var stored = docs.Stored.OfType<Notification>().Where(n => n.UserId == Owner).ToArray();
        for (var i = 0; i < stored.Length; i++)
        {
            var (subject, text) = mail.Notices[i] is var m ? (m.Subject, m.Text) : default;
            Assert.Equal(l.Format("notifications.owner_email.subject", "مَتجَرُ النَّخلَة", stored[i].Title), subject);
            Assert.Equal(DealNotices.OwnerEmailFor("مَتجَرُ النَّخلَة", deal, stored[i], l).Text, text);
            Assert.Contains(stored[i].Body, text, StringComparison.Ordinal);
            Assert.Contains("مِصباحُ نُحاس", text, StringComparison.Ordinal);
            Assert.Contains($"{150m:N2} SAR", text, StringComparison.Ordinal);
            Assert.Contains(DealNotices.StudioDealUrl(Slug, deal.Id), text, StringComparison.Ordinal);
            Assert.DoesNotContain(BuyerPhone, text, StringComparison.Ordinal);
            Assert.DoesNotContain(BuyerAddress, text, StringComparison.Ordinal);
            Assert.DoesNotContain(BuyerPhone, subject, StringComparison.Ordinal);
        }
    }

    /// <summary><b>السالِب</b>: صاحِبُ مَتجَرٍ دَخَلَ بِالهاتِفِ (لا بَريد) ⇒ الإشعارُ يُكتَبُ ولا بَريد؛ وبِلا قَناةِ بَريدٍ مُسَجَّلَة ⇒ كَذلِك؛ وقَناةٌ تَرمي ⇒ الإشعارُ
    /// مَكتوبٌ والنِداءُ يُجيبُ «أُشعِر» — البَريدُ أَثَرٌ لا شَرط.</summary>
    [Theory]
    [InlineData("no_email")]
    [InlineData("no_channel")]
    [InlineData("channel_throws")]
    public async Task No_email_no_channel_or_a_failing_channel_still_writes_the_notice_and_sends_nothing(string row)
    {
        var mail = new Capture { Throws = row == "channel_throws" ? new InvalidOperationException("smtp down") : null };
        var (docs, notices, deal) = World(row == "no_channel" ? null : mail, ownerEmail: row == "no_email" ? "" : OwnerEmail);
        var l = new L(new HttpContextAccessor());

        Assert.True(await notices.NotifyStoreOwnerAsync(Slug, deal, DealNotices.OwnerAction.CashRefundDue, l));

        output.WriteLine($"{row}: إشعارات={OwnerNotices(docs)} · بَريد={mail.Notices.Count}");
        Assert.Equal(1, OwnerNotices(docs));
        Assert.Empty(mail.Notices);
    }
}
