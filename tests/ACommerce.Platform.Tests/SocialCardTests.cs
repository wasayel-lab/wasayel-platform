using ACommerce.Kit.Tenants;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// بِطاقَة المُشارَكَة ومُرَمِّزُها. الاختِبار يَقرَأ البايتات نَفسَها —
/// لا مَكتَبَة صُوَر هُنا تُصَدِّق عَلى مَكتَبَة صُوَر أُخرى.
/// </summary>
public class SocialCardTests
{
    private static readonly byte[] Signature =
        { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static uint BeAt(byte[] b, int i)
        => (uint)((b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3]);

    private static string TypeAt(byte[] b, int i)
        => new((char[])[(char)b[i], (char)b[i + 1], (char)b[i + 2], (char)b[i + 3]]);

    /// <summary>يَمشي عَلى الكُتَل كَما يَمشي أَيّ قارِئ PNG: طول ثُمَّ
    /// نَوع ثُمَّ جِسم ثُمَّ CRC.</summary>
    private static List<string> Chunks(byte[] png)
    {
        var names = new List<string>();
        var i = Signature.Length;
        while (i + 8 <= png.Length)
        {
            var len = (int)BeAt(png, i);
            names.Add(TypeAt(png, i + 4));
            i += 12 + len;
        }
        return names;
    }

    // ─── المُرَمِّز ──────────────────────────────────────────────────

    [Fact]
    public void EncodeRgb_WritesSignature_AndTheThreeMandatoryChunks()
    {
        var png = Png.EncodeRgb(2, 2, new byte[2 * 2 * 3]);

        Assert.Equal(Signature, png.Take(8));
        Assert.Equal(new[] { "IHDR", "IDAT", "IEND" }, Chunks(png));
    }

    [Fact]
    public void EncodeRgb_HeaderCarriesTheDimensions_AndTruecolorDepth8()
    {
        var png = Png.EncodeRgb(7, 3, new byte[7 * 3 * 3]);

        Assert.Equal(7u, BeAt(png, 16));   // العَرض
        Assert.Equal(3u, BeAt(png, 20));   // الاِرتِفاع
        Assert.Equal(8, png[24]);          // عُمق البِت
        Assert.Equal(2, png[25]);          // نَوع اللَون: RGB
        Assert.Equal(0, png[28]);          // بِلا تَشبيك
    }

    /// <summary>الخَتم هو ما يَجعَل المِلَفّ مَقروءاً — قارِئ مُطابِق
    /// لِلمُواصَفَة يَرفُض كُتلَةً خَتمُها خاطِئ.</summary>
    [Fact]
    public void EncodeRgb_EveryChunkCrcMatchesItsBody()
    {
        var png = Png.EncodeRgb(5, 5, new byte[5 * 5 * 3]);

        var i = Signature.Length;
        var checked_ = 0;
        while (i + 8 <= png.Length)
        {
            var len = (int)BeAt(png, i);
            var stored = BeAt(png, i + 8 + len);
            Assert.Equal(Crc32(png.AsSpan(i + 4, 4 + len)), stored);
            checked_++;
            i += 12 + len;
        }
        Assert.Equal(3, checked_);
    }

    [Fact]
    public void EncodeRgb_RejectsMismatchedBufferLength()
        => Assert.Throws<ArgumentException>(() => Png.EncodeRgb(4, 4, new byte[10]));

    // ─── البِطاقَة ───────────────────────────────────────────────────

    [Fact]
    public void RenderPng_IsAValidPngAtTheConventionalShareSize()
    {
        var png = SocialCard.RenderPng("#7a288a");

        Assert.Equal(Signature, png.Take(8));
        Assert.Equal((uint)SocialCard.Width, BeAt(png, 16));
        Assert.Equal((uint)SocialCard.Height, BeAt(png, 20));
        Assert.Equal(1200, SocialCard.Width);
        Assert.Equal(630, SocialCard.Height);
    }

    /// <summary>الحَتمِيَّة قاعِدَة في هذا المَشروع: نَفس اللَون ← نَفس
    /// البايتات، فَالكاش والمُقارَنَة بِلا هامِش.</summary>
    [Fact]
    public void RenderPng_IsDeterministic_AndColorSensitive()
    {
        Assert.Equal(SocialCard.RenderPng("#7a288a"), SocialCard.RenderPng("#7a288a"));
        Assert.NotEqual(SocialCard.RenderPng("#7a288a"), SocialCard.RenderPng("#1D4ED8"));
    }

    [Theory]
    [InlineData("#7a288a", 0x7a, 0x28, 0x8a)]
    [InlineData("7a288a",  0x7a, 0x28, 0x8a)]
    [InlineData("#abc",    0xaa, 0xbb, 0xcc)]
    public void ParseColor_ReadsBothHexForms_WithOrWithoutHash(
        string input, int r, int g, int b)
        => Assert.Equal(((byte)r, (byte)g, (byte)b), SocialCard.ParseColor(input));

    /// <summary>لَون فاسِد في وَثيقَة مُستَأجِر لا يُسقِط النُقطَة —
    /// يَسقُط عَلى لَون المَنصَّة.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rebeccapurple")]
    [InlineData("#12345")]
    [InlineData("#zzzzzz")]
    public void ParseColor_FallsBackToPlatformBlue_OnAnythingElse(string? input)
        => Assert.Equal(((byte)0x1D, (byte)0x4E, (byte)0xD8), SocialCard.ParseColor(input));

    [Fact]
    public void RenderPng_SurvivesAnInvalidBrandColor()
        => Assert.Equal(SocialCard.RenderPng(SocialCard.FallbackColor),
                        SocialCard.RenderPng("not-a-color"));

    // نُسخَة مُستَقِلَّة مِن CRC-32 لِلاختِبار — لا تَستَدعي المُرَمِّز
    // كَي لا يُصادِق الكود عَلى نَفسِه.
    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            c ^= b;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFFu;
    }
}
