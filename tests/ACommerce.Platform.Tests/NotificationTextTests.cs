using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.I18n;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── مُتونُ الإشعارات — نَصٌّ يَراهُ المُستَخدِم خارِجَ `.razor` ───────
//
// عَشرُ سَلاسِلَ في `MarketplaceTemplateExtensions` تُكتَب في وَثيقَة
// `Notification` و`Conversation` وفي حُمولَة الـweb-push، فَتُعرَض على
// شاشَةِ الإشعارات وفي المُحادَثات وفي إشعارِ النِظام. **نَصُّ واجِهَة
// بِكُلّ مَعنى، ولا يَعُدُّه عَدّادُ الطَبَقَة السابِعَة** لِأَنَّه لا
// يَقَع في `.razor`. وهذا حَدُّ المِقياس، قيلَ في §٨-د ولَم يُخفَ.
//
// **وبُرهانُ تَرحيلِها لَيسَ بايتِيّاً — ويُقال لِماذا**: هذِه السَلاسِل
// لا تُصَيَّر في أَيّ صَفحَةٍ مَلقوطَة؛ تُكتَب في قاعِدَة البَيانات عِندَ
// `POST`. فَالبُرهانُ المُتاح هو **تَثبيتُ المُخرَج**: كُلُّ تَأكيدٍ
// أَدناه يُقابِل `l.Format(...)` بِـ**نَفس السِلسِلَة المُدرَجَة الَّتي
// حَلَّ مَحَلَّها حَرفاً بِحَرف**. ولِذلك يُكتَب التَعبيرُ القَديم في
// الاختِبار صَريحاً لا يُنسَخ ناتِجُه: `$"…{p:N0}…"` يَتبَع ثَقافَةَ
// وَقتِ التَشغيل، فَلَو ثُبِّتَ الناتِجُ نَصّاً لَانكَسَرَ الاختِبارُ على
// آلَةٍ بِثَقافَةٍ أُخرى **وهو صَحيح**. المُقابَلَةُ بَينَ التَعبيرَين
// تَنجو مِن ذلك لِأَنّ الطَرَفَين يَمُرّانِ بِنَفس المُنَسِّق.

public class NotificationTextTests
{
    private static L Fresh() => new(new HttpContextAccessor());

    [Fact]
    public void SavedSearchMatch_KeepsItsExactText()
    {
        var l = Fresh();
        var label = "شَقَق الرياض";

        Assert.Equal($"إعلان جَديد يُطابِق «{label}»",
            l.Format("notifications.saved_search.title", label));
        Assert.Equal("إعلان جَديد يُطابِق بَحثكَ",
            l["notifications.saved_search.push_title"]);
    }

    [Theory]
    [InlineData(350000)]
    [InlineData(0)]
    [InlineData(12.5)]
    public void OfferAccepted_KeepsItsExactText(decimal price)
    {
        var l = Fresh();
        var acceptorName = "أَبو خالِد";

        Assert.Equal($"تَنسيق عَرض بِـ {price:N0} ريال",
            l.Format("chats.offer.subject", price));
        Assert.Equal("تَمّ قَبول عَرضكَ ✓",
            l["notifications.offer_accepted.title"]);
        Assert.Equal($"{acceptorName} قَبِلَ عَرضكَ بِـ {price:N0} ريال. افتَح المُحادَثَة لِلتَنسيق.",
            l.Format("notifications.offer_accepted.body", acceptorName, price));
        Assert.Equal($"{acceptorName} قَبِلَ عَرضكَ بِـ {price:N0} ريال.",
            l.Format("notifications.offer_accepted.push_body", acceptorName, price));
    }

    [Fact]
    public void DriverArrived_KeepsItsExactText()
    {
        var l = Fresh();
        var offererName = "سالِم";

        Assert.Equal("السائِق وَصَل ✓", l["notifications.driver_arrived.title"]);
        Assert.Equal($"{offererName} في نُقطَة الانطِلاق.",
            l.Format("notifications.driver_arrived.body", offererName));
    }

    [Fact]
    public void ChatSubjects_KeepTheirExactText()
    {
        var l = Fresh();
        var partnerName = "نورَة";
        var senderName = "فَهد";

        Assert.Equal($"تَواصُل مَع {partnerName}",
            l.Format("chats.direct.subject", partnerName));
        Assert.Equal($"رِسالَة مِن {senderName}",
            l.Format("notifications.chat_message.title", senderName));
    }

    /// <summary>حارِسُ العَمى (القاعِدَة ١٠): مِفتاحٌ ناقِصٌ مِن المَعجَم
    /// يَجعَل <c>L</c> تُرجِع <b>المِفتاحَ نَفسَه</b> — فَيَمُرّ اختِبارٌ
    /// يُقارِن سِلسِلَةً بِسِلسِلَة إن كانَ الطَرَفانِ مَعاً مَفقودَين.
    /// فَيُعَدُّ المَشحونُ أَوَّلاً.</summary>
    [Fact]
    public void EveryNotificationKey_ExistsInTheArabicLexicon()
    {
        string[] keys =
        [
            "notifications.saved_search.title",
            "notifications.saved_search.push_title",
            "chats.offer.subject",
            "notifications.offer_accepted.title",
            "notifications.offer_accepted.body",
            "notifications.offer_accepted.push_body",
            "notifications.driver_arrived.title",
            "notifications.driver_arrived.body",
            "chats.direct.subject",
            "notifications.chat_message.title",
        ];

        Assert.Equal(10, keys.Length);

        var missing = keys.Where(k => LocaleCatalog.Find(LocaleCatalog.Arabic, k) is null).ToArray();
        Assert.True(missing.Length == 0,
            "مَفاتيحُ إشعارات بِلا قيمَة: " + string.Join("، ", missing));
    }
}
