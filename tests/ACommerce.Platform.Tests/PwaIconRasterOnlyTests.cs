using ACommerce.Kit.Roles;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ أَيقونَةُ الـPWA صورَةٌ نُقَطِيَّةٌ بِبايتاتِها — سِياسَةٌ واحِدَةٌ تَسأَلُها الأَبوابُ الثَلاثَةُ وجَوابُ الأَيقونَة (‏2026-11-07، مُراجَعَةُ C2a الجَولَةُ الأُولى · SEC-C2a-1) ═══
//
// **المَقيسُ قَبل** (عِندَ `14c6f29`): SVG بِسكربتٍ رُفِعَت مِن الاستوديو فَحُفِظَت، وأُجيبَت `image/svg+xml` عَلى أَصلِ الجَذرِ بِلا CSP ولا `nosniff`؛ وأَداةُ
// الوَكيلِ تَقبَلُ أَيَّ `data:image/…`. **والآن** في `PwaSaveService`: القائِمَةُ PNG وWebP، والنَوعُ يُطابِقُ بَصمَةَ البايتات (`RasterTypeOf`)، والمُخزَّنُ
// لا يُجابُ إلّا نُقَطِيّاً بِبَصمَتِه (`StoredRaster`)، ورابِطُ الوَكيلِ يُطَبَّعُ أَو يُرَدّ (`RasterDataUrl`). ولِكُلِّ قَرارٍ موجَبٌ وسالِب (القاعِدَة ٤)؛ والأَبوابُ
// نَفسُها بِأَبوابِ القالِبِ عَلى Postgres في `LivePwaIconRasterOnlyProofTests`.

public class PwaIconRasterOnlyTests(ITestOutputHelper output)
{
    private static readonly byte[] Png = LivePwaIconCharacterizationTests.Png;
    private static readonly byte[] Webp = LivePwaIconCharacterizationTests.Webp;
    internal static readonly byte[] Svg = System.Text.Encoding.UTF8.GetBytes(
        "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(document.domain)</script></svg>");

    private static string Data(string type, byte[] bytes) => $"data:{type};base64,{Convert.ToBase64String(bytes)}";

    [Fact]
    public void The_allowed_set_is_png_and_webp_only()
        => Assert.Equal(["image/png", "image/webp"], PwaSaveService.AllowedContentTypes.Order());

    [Fact]
    public void The_type_of_a_raster_is_read_from_its_signature()
    {
        Assert.Equal("image/png", PwaSaveService.RasterTypeOf(Png));
        Assert.Equal("image/webp", PwaSaveService.RasterTypeOf(Webp));
        Assert.Null(PwaSaveService.RasterTypeOf(Svg));
        Assert.Null(PwaSaveService.RasterTypeOf(LivePwaIconCharacterizationTests.Gif));
        Assert.Null(PwaSaveService.RasterTypeOf(System.Text.Encoding.ASCII.GetBytes("RIFF0000WAVE")));
        Assert.Null(PwaSaveService.RasterTypeOf(Png.AsSpan(0, 7)));
        Assert.Null(PwaSaveService.RasterTypeOf([]));
    }

    /// <summary>جَوابُ المُخزَّن: نُقَطِيٌّ بِبَصمَتِه يُجاب، وما سِواه (SVG مُخزَّنَةٌ قَبلَ اليَوم، نَوعٌ يُخالِفُ بايتاتِه، base64 تالِف، شَكلٌ آخَر) لا يُجاب.</summary>
    [Theory]
    [InlineData("png", true)]
    [InlineData("webp", true)]
    [InlineData("svg-as-svg", false)]
    [InlineData("svg-as-png", false)]
    [InlineData("png-as-webp", false)]
    [InlineData("bad-base64", false)]
    [InlineData("no-base64-token", false)]
    [InlineData("upper-type", false)]
    [InlineData("empty", false)]
    [InlineData("http", false)]
    public void A_stored_icon_is_answered_only_as_a_signed_raster(string kind, bool answered)
    {
        var url = kind switch
        {
            "png" => Data("image/png", Png),
            "webp" => Data("image/webp", Webp),
            "svg-as-svg" => Data("image/svg+xml", Svg),
            "svg-as-png" => Data("image/png", Svg),
            "png-as-webp" => Data("image/webp", Png),
            "bad-base64" => "data:image/png;base64,@@@",
            "no-base64-token" => "data:image/png," + Convert.ToBase64String(Png),
            "upper-type" => Data("IMAGE/PNG", Png),
            "empty" => "",
            _ => "https://example.test/i.png",
        };
        var raster = PwaSaveService.StoredRaster(url);
        output.WriteLine($"{kind} ⇒ {raster?.ContentType ?? "(المُوَلَّدَة)"}");
        Assert.Equal(answered, raster is not null);
        if (raster is { } r) Assert.Equal(PwaSaveService.RasterTypeOf(r.Bytes), r.ContentType);
    }

    /// <summary>بابُ رابِطِ الوَكيل: PNG/WebP بِبايتاتِهِما يُطَبَّعانِ (النَوعُ بِحُروفٍ صَغيرَة)، وSVG ونَوعٌ يُخالِفُ بايتاتِه وما فَوقَ السَقفِ يُرَدّ.</summary>
    [Fact]
    public void The_agent_data_url_door_keeps_a_signed_raster_and_refuses_svg()
    {
        Assert.Equal(Data("image/png", Png), PwaSaveService.RasterDataUrl(Data("image/png", Png)));
        Assert.Equal(Data("image/png", Png), PwaSaveService.RasterDataUrl(Data("IMAGE/PNG", Png)));
        Assert.Equal(Data("image/webp", Webp), PwaSaveService.RasterDataUrl(Data("image/webp", Webp)));
        Assert.Null(PwaSaveService.RasterDataUrl(Data("image/svg+xml", Svg)));
        Assert.Null(PwaSaveService.RasterDataUrl(Data("image/png", Svg)));
        Assert.Null(PwaSaveService.RasterDataUrl(Data("image/png", Png.Concat(new byte[PwaSaveService.MaxIconBytes]).ToArray())));
        Assert.Null(PwaSaveService.RasterDataUrl("data:image/png"));
        Assert.Null(PwaSaveService.RasterDataUrl(null));
    }

    private static (DocWorld World, Tenant Tenant) Store()
    {
        var t = new Tenant { Id = "icons", Name = "أَيقونات", Roles = [new Role { Slug = "customer", CatalogSlug = "customer", Label = "عَميل" }] };
        return (new DocWorld().Put(t), t);
    }

    private static PwaSaveRequest Upload(string type, byte[] bytes)
        => new([new PwaRoleInput("customer", "", false, new UploadedIcon(type, bytes.Length, _ => Task.FromResult(bytes)))]);

    /// <summary>الحِفظ: SVG تُرَدُّ بِرَمزِ النَوعِ قَبلَ قِراءَةِ بايتٍ، وSVG بِرَأسِ <c>image/png</c> تُرَدُّ بَعدَ مُطابَقَةِ البَصمَة — ولا يُكتَبُ شَيء؛ وPNG حَقيقِيٌّ يُحفَظ.</summary>
    [Theory]
    [InlineData("image/svg+xml", "svg", TenantConfigCodes.IconBadType)]
    [InlineData("image/png", "svg", TenantConfigCodes.IconBadType)]
    [InlineData("image/webp", "png", TenantConfigCodes.IconBadType)]
    [InlineData("image/png", "png", null)]
    [InlineData("image/webp", "webp", null)]
    public async Task The_save_keeps_only_a_raster_whose_bytes_match_its_type(string declared, string bytesOf, string? code)
    {
        var (world, t) = Store();
        var bytes = bytesOf switch { "svg" => Svg, "png" => Png, _ => Webp };
        var result = await PwaSaveService.SaveAsync(world.Session, "icons", Upload(declared, bytes));
        output.WriteLine($"{declared} ← {bytesOf} ⇒ {result.Status} {result.Code} · مَكتوب {world.Stored.Count}");
        if (code is null)
        {
            Assert.True(result.Ok);
            Assert.Equal(Data(declared, bytes), t.Roles[0].PwaIconDataUrl);
        }
        else
        {
            Assert.Equal(code, result.Code);
            Assert.Empty(world.Stored);
            Assert.Null(t.Roles[0].PwaIconDataUrl);
        }
    }
}
