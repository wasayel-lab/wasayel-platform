using ACommerce.Kit.Auth;
using ACommerce.Platform.I18n;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ نَصُّ رِسالَةِ الرَمز — تَثبيتٌ بايتِيّ قَبلَ أَن يَنسَخَه نَقلٌ ثانٍ ══
//
// القاعِدَة ٣: قَبلَ نَقلِ سُلوكٍ مِن مَوضِعٍ إلى آخَر، يُثَبَّت السُلوكُ
// القائِمُ ويُخضَرّ ثُمَّ يُبَدَّل — والاختِبارُ يَبقى أَخضَرَ بِلا تَعديلِ
// حَرف. والقيَمُ أَدناه **مَنقولَةٌ حَرفِيّاً مِن `HEAD` قَبلَ الاستِخراج**
// (‏`git show HEAD:…/SmtpEmailChannel.cs`)، لا مُعادَ كِتابَتُها.
//
// **ولِماذا بايتِيّاً لا «يَبدو نَفسَه»**: النَصُّ عَرَبيٌّ **مُشَكَّل**،
// والتَشكيلُ يَسقُط صامِتاً في النَسخِ واللَصق — ولا يُرى في الـdiff ولا
// بِالعَين، ويُرى في صُندوقِ بَريدِ المُستَخدِمِ وَحدَه. (‏نَفسُ عِلَّةِ
// «كُلُّ دَفعَةِ تَرحيلٍ تُبرهَن بِمُقارَنَةٍ بايتِيَّة» في القاعِدَة ١١.)

public class OtpEmailMessageTests
{
    [Fact]
    public void TextBody_IsByteIdenticalToWhatSmtpSentBefore()
    {
        var code = "123456";
        var expected =
            $"رَمز التَّحَقُّق: {code}\nصالِح لِعَشر دَقائِق. إن لَم تَطلُبه فَتَجاهَل هذِه الرِّسالَة.";
        Assert.Equal(expected, OtpEmailMessage.Text("123456"));
    }

    [Fact]
    public void HtmlBody_IsByteIdenticalToWhatSmtpSentBefore()
    {
        var code = "123456";
        var expected =
            $"""
            <html dir="rtl"><body style="font-family:Tahoma,Arial;line-height:1.6;">
            <p>رَمز التَّحَقُّق الخاصّ بِك:</p>
            <p style="font-size:28px;font-weight:700;letter-spacing:.3em;direction:ltr;">{code}</p>
            <p style="color:#6b7280;font-size:13px;">صالِح لِعَشر دَقائِق. إن لَم تَطلُبه فَتَجاهَل هذِه الرِّسالَة.</p>
            </body></html>
            """;
        Assert.Equal(expected, OtpEmailMessage.Html("123456"));
    }

    /// <summary>والرَمزُ يَظهَر في الجِسمَين — وإلّا كانَت الرِسالَةُ
    /// سَليمَةَ الصيغَةِ وفارِغَةً مِن سَبَبِها.</summary>
    [Fact]
    public void BothBodies_CarryTheCode()
    {
        Assert.Contains("904517", OtpEmailMessage.Text("904517"));
        Assert.Contains("904517", OtpEmailMessage.Html("904517"));
    }

    /// <summary>مِفتاحُ العُنوانِ مَوجودٌ في القامُوسِ فِعلاً. مِفتاحٌ
    /// غائِبٌ يُرسِل رِسالَةً بِعُنوانٍ فارِغٍ أَو بِالمِفتاحِ نَفسِه —
    /// ويُقرَأ في صُندوقِ بَريدِ المُستَخدِمِ لا في اللوغ.</summary>
    [Fact]
    public void SubjectKey_ResolvesInTheArabicCatalogue()
    {
        Assert.Equal("auth.email.otp_subject", OtpEmailMessage.SubjectKey);
        var subject = LocaleCatalog.Text(LocaleCatalog.Arabic, OtpEmailMessage.SubjectKey);
        Assert.False(string.IsNullOrWhiteSpace(subject));
        Assert.NotEqual(OtpEmailMessage.SubjectKey, subject);
    }
}
