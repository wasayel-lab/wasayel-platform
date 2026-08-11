using System.IO.Compression;

namespace ACommerce.Kit.Tenants;

/// <summary>
/// <para><b>مُرَمِّز PNG أَدنى — بِلا أَيّ حُزمَة</b>. المِلَفّ توقيع ثُمَّ
/// ثَلاث كُتَل: <c>IHDR</c> و<c>IDAT</c> و<c>IEND</c>، وكُلّ كُتلَة
/// مَختومَة بِـ CRC-32. والضَغط <c>zlib</c> وهو في المَكتَبَة القِياسيَّة
/// (<see cref="ZLibStream"/>) — فَلا SkiaSharp ولا ImageSharp ولا
/// <c>System.Drawing</c> المُقَيَّد بِويندوز.</para>
///
/// <para><b>ولِماذا نَكتُبُه بِاليَد بَدَل حُزمَة؟</b> لِأَنّ المَطلوب
/// صورَة مُصمَتَة بِتَدَرُّج وأَشكال — لا رَسم خُطوط ولا نَصّ. حُزمَة
/// رُسوم كامِلَة لِهذا القَدر ثَمَنُها عَشَرات الميغابايت أَصلِيَّة
/// لِكُلّ مِنصَّة نَشر، مُقابِل نَحو مِئَة سَطر هُنا.</para>
/// </summary>
public static class Png
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc(ReadOnlySpan<byte> data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static void BeUInt32(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8));  s.WriteByte((byte)v);
    }

    private static void Chunk(Stream s, string type, ReadOnlySpan<byte> body)
    {
        var payload = new byte[4 + body.Length];
        for (var i = 0; i < 4; i++) payload[i] = (byte)type[i];
        body.CopyTo(payload.AsSpan(4));

        BeUInt32(s, (uint)body.Length);
        s.Write(payload, 0, payload.Length);
        BeUInt32(s, Crc(payload));
    }

    /// <summary>يُرَمِّز صورَة RGB (‏٣ بايت لِلبِكسِل، صَفّاً بَعدَ صَفّ)
    /// إلى PNG بِعُمق ٨ بِت ولَون حَقيقيّ بِلا شَفافيَّة.</summary>
    /// <param name="rgb">طولُه يَجِب أَن يُساوي <c>width*height*3</c>.</param>
    public static byte[] EncodeRgb(int width, int height, byte[] rgb)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (rgb.Length != width * height * 3)
            throw new ArgumentException("طول المَصفوفَة لا يُطابِق الأَبعاد.", nameof(rgb));

        using var outp = new MemoryStream();
        outp.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        // IHDR: عَرض، اِرتِفاع، عُمق ٨، نَوع ٢ (RGB)، ضَغط ٠، فَلتَرَة ٠، بِلا تَشبيك.
        using (var ihdr = new MemoryStream())
        {
            BeUInt32(ihdr, (uint)width);
            BeUInt32(ihdr, (uint)height);
            ihdr.WriteByte(8); ihdr.WriteByte(2);
            ihdr.WriteByte(0); ihdr.WriteByte(0); ihdr.WriteByte(0);
            Chunk(outp, "IHDR", ihdr.ToArray());
        }

        // كُلّ صَفّ مَسبوق بِبايت الفَلتَرَة ٠ (None) — أَبسَط ما تَقبَلُه
        // المُواصَفَة، والتَدَرُّج الرَأسيّ يَنضَغِط جَيِّداً بِه أَصلاً.
        var raw = new byte[height * (1 + width * 3)];
        for (var y = 0; y < height; y++)
        {
            var dst = y * (1 + width * 3);
            raw[dst] = 0;
            Buffer.BlockCopy(rgb, y * width * 3, raw, dst + 1, width * 3);
        }

        using (var comp = new MemoryStream())
        {
            using (var z = new ZLibStream(comp, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(raw, 0, raw.Length);
            Chunk(outp, "IDAT", comp.ToArray());
        }

        Chunk(outp, "IEND", ReadOnlySpan<byte>.Empty);
        return outp.ToArray();
    }
}

/// <summary>
/// <para><b>بِطاقَة المُشارَكَة</b> — صورَة نُقَطِيَّة ‏1200×630 (النِسبَة
/// العُرفيَّة 1.91:1 الَّتي تَطلُبُها فيسبوك وواتساب وتويتر ولينكدإن).
/// خَلفِيَّتُها لَون المَتجَر بِتَدَرُّج رَأسيّ، وفَوقَها مُرَبَّعان
/// مُستَديرا الزَوايا مُتَّحِدا المَركَز — نَفس هَندَسَة أَيقونَة الـ PWA.</para>
///
/// <para><b>ولا نَصّ فيها عَمداً</b>: رَسم النَصّ يَلزَمُه مُنَقِّط خُطوط،
/// والعَرَبيَّة تَلزَمُها فَوقَه مُشَكِّل وصَل وقَلب اتِّجاه. أَيّ مُحاكاة
/// لِذلِك بِأَشكال هَندَسِيَّة تُخرِج حُروفاً مَكسورَة — وبِطاقَة نَظيفَة
/// بِلَون المَتجَر أَصدَق مِن اسم مَرسوم خَطَأً. الاسم والوَصف يَصِلان
/// المُشارِك أَصلاً مِن <c>og:title</c> و<c>og:description</c>.</para>
/// </summary>
public static class SocialCard
{
    /// <summary>العَرض العُرفيّ لِبِطاقَة المُشارَكَة.</summary>
    public const int Width = 1200;

    /// <summary>الاِرتِفاع العُرفيّ — 1200×630 ≈ 1.91:1.</summary>
    public const int Height = 630;

    /// <summary>لَون المَنصَّة حينَ لا يُعطي المَتجَر لَوناً صالِحاً.</summary>
    public const string FallbackColor = "#1D4ED8";

    /// <summary>يَقرَأ <c>#RGB</c> أَو <c>#RRGGBB</c> (والشَرطَة اختِيارِيَّة).
    /// أَيّ نَصّ آخَر ← <see cref="FallbackColor"/>، فَلَون فاسِد في وَثيقَة
    /// مُستَأجِر لا يُسقِط النُقطَة.</summary>
    public static (byte R, byte G, byte B) ParseColor(string? hex)
    {
        var h = (hex ?? "").Trim().TrimStart('#');

        if (h.Length == 3 && IsHex(h))
            return ((byte)(Nib(h[0]) * 17), (byte)(Nib(h[1]) * 17), (byte)(Nib(h[2]) * 17));

        if (h.Length == 6 && IsHex(h))
            return ((byte)(Nib(h[0]) * 16 + Nib(h[1])),
                    (byte)(Nib(h[2]) * 16 + Nib(h[3])),
                    (byte)(Nib(h[4]) * 16 + Nib(h[5])));

        // السُقوط مَكتوب قيمَةً لا نِداءً راجِعاً — فَلا مَسار عَودَة
        // مُمكِن مَهما كانَ المُدخَل.
        return (0x1D, 0x4E, 0xD8);

        static bool IsHex(string s) => s.All(Uri.IsHexDigit);
        static int Nib(char c) => Convert.ToInt32(c.ToString(), 16);
    }

    /// <summary>
    /// يَرسُم البِطاقَة ويُعيدُها PNG جاهِزاً. <b>حَتمِيَّة تامَّة</b>: نَفس
    /// اللَون ونَفس الأَبعاد ← نَفس البايتات، فَتُكاش بِأَمان وتُقارَن في
    /// اختِبار بِلا هامِش.
    /// </summary>
    public static byte[] RenderPng(string? brandColor, int width = Width, int height = Height)
    {
        var (br, bg, bb) = ParseColor(brandColor);
        var px = new byte[width * height * 3];

        // مُرَبَّعان مُتَّحِدا المَركَز — نِسَبُهُما مِن الاِرتِفاع فَتَثبُت
        // النَتيجَة لَو تَغَيَّرَ المَقاس.
        var cx = width / 2.0;
        var cy = height / 2.0;
        var outerHalf = height * 0.285;
        var outerR    = height * 0.070;
        var innerHalf = height * 0.160;
        var innerR    = height * 0.040;

        for (var y = 0; y < height; y++)
        {
            // تَدَرُّج رَأسيّ: لَون المَتجَر أَعلى، وأَغمَق بِـ٢٢٪ أَسفَل.
            var f = 1.0 - 0.22 * (y / (double)(height - 1));
            var rowR = br * f; var rowG = bg * f; var rowB = bb * f;

            for (var x = 0; x < width; x++)
            {
                var r = rowR; var g = rowG; var b = rowB;

                // طَبَقَتا أَبيَض شَفّاف فَوق الخَلفِيَّة، بِتَغطِيَة
                // مُحسوبَة مِن دالَّة المَسافَة فَتَخرُج الحَواف ناعِمَة.
                r = Over(r, 255, 0.12 * Coverage(x, y, cx, cy, outerHalf, outerR));
                g = Over(g, 255, 0.12 * Coverage(x, y, cx, cy, outerHalf, outerR));
                b = Over(b, 255, 0.12 * Coverage(x, y, cx, cy, outerHalf, outerR));

                var ci = 0.22 * Coverage(x, y, cx, cy, innerHalf, innerR);
                r = Over(r, 255, ci); g = Over(g, 255, ci); b = Over(b, 255, ci);

                var i = (y * width + x) * 3;
                px[i]     = (byte)Math.Clamp(Math.Round(r), 0, 255);
                px[i + 1] = (byte)Math.Clamp(Math.Round(g), 0, 255);
                px[i + 2] = (byte)Math.Clamp(Math.Round(b), 0, 255);
            }
        }

        return Png.EncodeRgb(width, height, px);
    }

    private static double Over(double under, double over, double alpha)
        => under + (over - under) * alpha;

    /// <summary>تَغطِيَة البِكسِل داخِل مُرَبَّع مُستَدير الزَوايا، مِن
    /// دالَّة المَسافَة المُوَقَّعَة، مُنَعَّمَة عَلى بِكسِل واحِد.</summary>
    private static double Coverage(
        int x, int y, double cx, double cy, double half, double radius)
    {
        var dx = Math.Abs(x + 0.5 - cx) - (half - radius);
        var dy = Math.Abs(y + 0.5 - cy) - (half - radius);
        var outside = Math.Sqrt(Math.Max(dx, 0) * Math.Max(dx, 0)
                              + Math.Max(dy, 0) * Math.Max(dy, 0));
        var d = outside + Math.Min(Math.Max(dx, dy), 0) - radius;
        return Math.Clamp(0.5 - d, 0.0, 1.0);
    }
}
