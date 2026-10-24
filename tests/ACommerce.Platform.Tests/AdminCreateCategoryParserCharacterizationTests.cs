using ACommerce.Templates.Customer.Marketplace.Services.TenantConfig;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ «اليَوم» — مُحَلِّلُ الفِئاتِ داخِلَ `POST /admin/tenants/create` يَختَلِفُ عَن `CategoriesSaveService.Parse` في أَربَعَة ═══
//
// **المَقيس** (‏2026-10-24، عِندَ `2939229`؛ «ب» S11·١٣ و`ADR-034` §٣‑٢ مُلحَقُ ‏2026-10-13): نُقطَةُ إنشاءِ المَتجَرِ مِن لَوحَةِ المَنَصَّةِ
// — الكاتِبُ الثالِثُ لِكِندِ الفِئات — تُحَلِّلُ أَسطُرَ «slug | الاسم | أَيقونَة | تَجميع» في جِسمِها لا بِالمُحَلِّلِ المُشتَرَك، وتَختَلِفُ
// عَنه في أَربَعَةٍ مُسَمّاة: (١) الأَيقونَةُ الافتِراضِيَّة، (٢) عَمودُ أَيقونَةٍ فارِغ، (٣) سَطرُ المَسافات (ومِنه السَطرُ الفارِغُ في
// استِمارَةٍ تُرسِلُ CRLF)، (٤) رَمزا الرَفض. **تُثَبَّتُ كَما هي ولا تُوَحَّد**: التَوحيدُ يُغَيِّرُ سُلوكاً مَرئِيّاً، وقَرارُه مَعَ ل‑١
// («ب» في سِجِلِّ المالِك) — ومَن يُوَحِّدُ يُحمِرُّ هذِه الصُفوفَ بِقَصدٍ ويَقلِبُها بِأَسمائِها في كوميتِه.
//
// **وحَدُّ ما يُقاسُ هُنا يُقال**: شَطرُ المُحَلِّلِ المُشتَرَكِ سُلوكٌ يُنادى، وشَطرُ النُقطَةِ نَصُّ جِسمِها (لا دالَّةَ تُنادى بِلا مُضيفِ القالِبِ
// كُلِّه — القاعِدَة ١: لا يُستَخرَجُ مِن أَجلِ اختِبار)؛ وسُلوكُ النُقطَةِ مَقيسٌ حَيّاً بِـ`curl` في جِسمِ كوميتِ هذا المِلَفّ.

public class AdminCreateCategoryParserCharacterizationTests
{
    /// <summary>جِسمُ <c>POST /admin/tenants/create</c> — مِن تَسجيلِه إلى التَسجيلِ الَّذي يَليه.</summary>
    internal static string EndpointBody()
    {
        var ext = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs"));
        var at = ext.IndexOf("app.MapPost(\"/admin/tenants/create\"", StringComparison.Ordinal);
        Assert.True(at > 0, "لا نُقطَةَ /admin/tenants/create — الأَداةُ عَمياء.");
        var end = ext.IndexOf("app.Map", at + 10, StringComparison.Ordinal);
        Assert.True(end > at, "لا نُقطَةَ بَعدَها — الأَداةُ عَمياء.");
        return ext[at..end];
    }

    /// <summary>حَلقَةُ الفِئاتِ في الجِسم — مِن رَأسِها إلى فَحصِ «لا فِئات».</summary>
    internal static string EndpointLoop()
    {
        var body = EndpointBody();
        const string head = "foreach (var line in catsRaw.Split('\\n', StringSplitOptions.RemoveEmptyEntries))";
        var at = body.IndexOf(head, StringComparison.Ordinal);
        var end = body.IndexOf("if (categories.Count == 0)", StringComparison.Ordinal);
        Assert.True(at > 0 && end > at, "حَلقَةُ الفِئاتِ في النُقطَةِ لَم توجَد — الأَداةُ عَمياء.");
        return body[at..end];
    }

    [Fact]
    public void Difference_1_the_default_icon_is_a_house_at_the_endpoint_and_a_tag_in_the_parser()
    {
        Assert.Contains("Icon  = parts.Length > 2 ? parts[2].Trim() : \"🏠\",", EndpointLoop(), StringComparison.Ordinal);

        var (cats, code) = CategoriesSaveService.Parse("a | أ");
        Assert.Null(code);
        Assert.Equal("🏷️", CategoriesSaveService.DefaultIcon);
        Assert.Equal(CategoriesSaveService.DefaultIcon, Assert.Single(cats!).Icon);
    }

    [Fact]
    public void Difference_2_an_empty_icon_column_stays_empty_at_the_endpoint_and_takes_the_default_in_the_parser()
    {
        // ‏النُقطَة: لا سُؤالَ عَن فَراغِ العَمودِ الثالِث — «a | أ |» ⇒ `parts[2]` = "" ⇒ الأَيقونَةُ "".
        var loop = EndpointLoop();
        Assert.Contains("parts.Length > 2 ? parts[2].Trim() :", loop, StringComparison.Ordinal);
        Assert.DoesNotContain("IsNullOrEmpty(parts[2])", loop, StringComparison.Ordinal);

        foreach (var raw in new[] { "a | أ |", "a | أ |  | " })
        {
            var (cats, code) = CategoriesSaveService.Parse(raw);
            Assert.Null(code);
            Assert.Equal(CategoriesSaveService.DefaultIcon, Assert.Single(cats!).Icon);
        }
    }

    [Fact]
    public void Difference_3_a_whitespace_only_line_is_refused_at_the_endpoint_and_skipped_by_the_parser()
    {
        // ‏النُقطَة: أَوَّلُ ما في الحَلقَةِ تَقسيمُ السَطرِ خاماً — لا `Trim` ولا `continue` قَبلَه — فَسَطرُ المَسافاتِ (أَو `"\r"`
        // مِن استِمارَةٍ تُرسِلُ CRLF وبَينَ سَطرَيها فَراغ) جُزءٌ واحِدٌ ⇒ `bad_categories`.
        var loop = EndpointLoop();
        var firstStatement = loop[(loop.IndexOf('{') + 1)..].TrimStart();
        Assert.StartsWith("var parts = line.Split('|', StringSplitOptions.TrimEntries);", firstStatement, StringComparison.Ordinal);
        Assert.Contains("if (parts.Length < 2) return Results.Redirect(Back(\"bad_categories\"));", loop, StringComparison.Ordinal);
        Assert.DoesNotContain("continue;", loop, StringComparison.Ordinal);

        foreach (var raw in new[] { "a | أ\n   \nb | ب", "a | أ\r\n\r\nb | ب" })
        {
            var (cats, code) = CategoriesSaveService.Parse(raw);
            Assert.Null(code);
            Assert.Equal(new[] { "a", "b" }, cats!.Select(c => c.Slug));
        }
    }

    [Fact]
    public void Difference_4_the_refusal_codes_are_bad_categories_and_no_categories_at_the_endpoint_and_bad_format_and_empty_in_the_parser()
    {
        var body = EndpointBody();
        Assert.Equal(2, CountOf(body, "Back(\"bad_categories\")"));
        Assert.Equal(1, CountOf(body, "Back(\"no_categories\")"));

        var page = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "Admin", "CreateTenant.razor"));
        Assert.Contains("\"no_categories\" => L[\"admin.new.err_no_categories\"]", page, StringComparison.Ordinal);
        Assert.Contains("\"bad_categories\" => L[\"admin.new.err_bad_categories\"]", page, StringComparison.Ordinal);

        Assert.Equal(("bad_format", "empty"), (TenantConfigCodes.BadFormat, TenantConfigCodes.Empty));
        Assert.Equal(TenantConfigCodes.BadFormat, CategoriesSaveService.Parse("x").Code);
        Assert.Equal(TenantConfigCodes.BadFormat, CategoriesSaveService.Parse(" | أ").Code);
        Assert.Equal(TenantConfigCodes.Empty, CategoriesSaveService.Parse("").Code);
        Assert.Equal(TenantConfigCodes.Empty, CategoriesSaveService.Parse("\n  \n").Code);
    }

    private static int CountOf(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)) n++;
        return n;
    }
}
