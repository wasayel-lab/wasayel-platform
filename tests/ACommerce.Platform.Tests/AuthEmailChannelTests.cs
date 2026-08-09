using ACommerce.Kit.Auth;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── مَنطِق قَناة البَريد القابِل لِلاختِبار بِلا قاعِدَة بَيانات ──────
// كُلّ ما دونَ الإرسال الفِعليّ والتَخزين: صيغَة البَريد، تَطبيعه،
// وتَعداد قَنَوات المُستَأجِر. هذِه هي البَوّابات الَّتي إن انزاحَت
// صامِتَةً ضاعَت رُموز OTP أَو ارتَدَّت قَناة صالِحَة إلى "phone".

public class EmailAddressTests
{
    [Theory]
    [InlineData("user@example.com")]
    [InlineData("first.last@sub.example.co.uk")]
    [InlineData("user+tag@example.org")]
    [InlineData("u@e.io")]
    [InlineData("UPPER@EXAMPLE.COM")]
    public void ValidAddresses_Pass(string email)
        => Assert.True(EmailAddress.IsValid(email), $"يَجِب قَبول «{email}»");

    [Theory]
    [InlineData(null)]                       // غائِب
    [InlineData("")]                         // فارِغ
    [InlineData("   ")]                      // مَسافات فَقَط
    [InlineData("plainstring")]              // بِلا @
    [InlineData("@example.com")]             // بِلا جُزء مَحَلِّيّ
    [InlineData("user@")]                    // بِلا نِطاق
    [InlineData("user@@example.com")]        // @ مُكَرَّرَة
    [InlineData("user@localhost")]           // نِطاق بِلا نُقطَة
    [InlineData("user@example.")]            // نِطاق يَنتَهي بِنُقطَة
    [InlineData("user@.example.com")]        // نِطاق يَبدَأ بِنُقطَة
    [InlineData("user@example..com")]        // نُقطَتان مُتَتالِيَتان
    [InlineData("user@example.c")]           // لاحِقَة أَقصَر مِن حَرفَين
    [InlineData("user@example.12")]          // لاحِقَة غَير حَرفيَّة
    [InlineData("us er@example.com")]        // مَسافَة داخِليَّة
    [InlineData("user@exam ple.com")]        // مَسافَة في النِطاق
    public void InvalidAddresses_Fail(string? email)
        => Assert.False(EmailAddress.IsValid(email), $"يَجِب رَفض «{email ?? "null"}»");

    [Fact]
    public void OverlongAddress_Fails()
    {
        var local = new string('a', 300);
        Assert.False(EmailAddress.IsValid($"{local}@example.com"));
    }

    [Fact]
    public void LocalPartOver64Chars_Fails()
    {
        var local = new string('a', 65);
        Assert.False(EmailAddress.IsValid($"{local}@example.com"));
    }

    // التَطبيع هو ما يَجعَل الطَلَب والتَحَقُّق يَلتَقِيان: لَو طَلَبَ
    // المُستَخدِم بِـ " Ali@Example.COM " وتَحَقَّقَ بِـ "ali@example.com"
    // فَالمُحاوَلَة المُخَزَّنَة يَجِب أَن تُطابِق في الحالَتَين.
    [Theory]
    [InlineData("  Ali@Example.COM  ", "ali@example.com")]
    [InlineData("ali@example.com", "ali@example.com")]
    [InlineData(null, "")]
    public void Normalize_TrimsAndLowercases(string? input, string expected)
        => Assert.Equal(expected, EmailAddress.Normalize(input));

    [Fact]
    public void Normalize_IsIdempotent()
    {
        var once = EmailAddress.Normalize("  Ali@Example.COM ");
        Assert.Equal(once, EmailAddress.Normalize(once));
    }
}

public class AuthChannelsTests
{
    [Theory]
    [InlineData("phone")]
    [InlineData("nafath")]
    [InlineData("email")]
    public void SupportedChannels_AreAccepted(string channel)
        => Assert.True(AuthChannels.IsSupported(channel));

    [Theory]
    [InlineData("telegram")]
    [InlineData("whatsapp")]
    [InlineData("EMAIL")]     // حَسّاس لِحالَة الأَحرُف عَمداً — القيمَة تُخَزَّن صَغيرَة
    [InlineData("")]
    [InlineData(null)]
    public void UnsupportedChannels_AreRejected(string? channel)
        => Assert.False(AuthChannels.IsSupported(channel));

    // هذا هو الفَخّ الَّذي تَحرُسه الدالَّة: قيمَة صالِحَة تُبتَلَع صامِتَةً
    // وتَرتَدّ إلى "phone" لِأَنَّ مَوضِعاً واحِداً نَسِيَ التَعداد.
    [Fact]
    public void NormalizeOrDefault_KeepsEmail()
        => Assert.Equal("email", AuthChannels.NormalizeOrDefault("email"));

    [Theory]
    [InlineData("telegram")]
    [InlineData("")]
    [InlineData(null)]
    public void NormalizeOrDefault_FallsBackToPhone(string? channel)
        => Assert.Equal("phone", AuthChannels.NormalizeOrDefault(channel));

    [Fact]
    public void All_MatchesTheSchemaEnum()
    {
        // نَفس القائِمَة المُعلَنَة في مُخَطَّطَي create_tenant/set_branding.
        Assert.Equal(new[] { "phone", "nafath", "email" }, AuthChannels.All);
    }
}
