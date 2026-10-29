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
// **وقُلِبَت الأَربَعَةُ بِأَسمائِها** (‏2026-10-29، قَرارُ المالِك د25 (٢-أ) — كوميتُ التَوحيدِ وَحدَه، قَبلَ مُصادِقِ قيَمِ التَجميع):
// النُقطَةُ تَمُرُّ بِـ`CategoriesSaveService.Parse` فَتَأخُذُ أَيقونَتَه 🏷️، وتَملَأُ العَمودَ الفارِغَ بِها، وتَتَخَطّى سَطرَ المَسافات، وتَرُدُّ
// بِـ`bad_format`/`empty` — وصَفحَةُ `/admin/tenants/new` تَقولُهُما بِنَصَّيها القائِمَين. وكُلُّ صَفٍّ يَقيسُ الآنَ شَطرَينِ: أَنّ الجِسمَ يُنادي
// المُحَلِّلَ ولا حَلقَةَ لَه، وسُلوكَ المُحَلِّلِ في الفَرقِ نَفسِه — وما تَقولُه الصَفحَة.
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

    /// <summary>الجِسمُ يُنادي المُحَلِّلَ المُشتَرَكَ مَرَّةً ويُعيدُ رَمزَه كَما هُوَ — ولا حَلقَةَ فِئاتٍ فيه بَعدَ التَوحيد.</summary>
    internal static string EndpointCallingTheParser()
    {
        var body = EndpointBody();
        Assert.Equal(1, CountOf(body, "CategoriesSaveService.Parse(catsRaw)"));
        Assert.Contains("if (catsCode is not null) return Results.Redirect(Back(catsCode));", body, StringComparison.Ordinal);
        Assert.DoesNotContain("catsRaw.Split(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("new ACommerce.Kit.Tenants.Category", body, StringComparison.Ordinal);
        return body;
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> — كانَ <c>Difference_1_the_default_icon_is_a_house_at_the_endpoint_and_a_tag_in_the_parser</c>.</summary>
    [Fact]
    public void Unified_1_the_default_icon_is_the_parser_tag_at_the_endpoint_too()
    {
        var body = EndpointCallingTheParser();
        Assert.DoesNotContain("\"🏠\"", body, StringComparison.Ordinal);

        var (cats, code) = CategoriesSaveService.Parse("a | أ");
        Assert.Null(code);
        Assert.Equal("🏷️", CategoriesSaveService.DefaultIcon);
        Assert.Equal(CategoriesSaveService.DefaultIcon, Assert.Single(cats!).Icon);
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> — كانَ <c>Difference_2_an_empty_icon_column_stays_empty_at_the_endpoint_and_takes_the_default_in_the_parser</c>.</summary>
    [Fact]
    public void Unified_2_an_empty_icon_column_takes_the_default_at_the_endpoint_too()
    {
        var body = EndpointCallingTheParser();
        Assert.DoesNotContain("parts.Length > 2 ? parts[2].Trim() :", body, StringComparison.Ordinal);

        foreach (var raw in new[] { "a | أ |", "a | أ |  | " })
        {
            var (cats, code) = CategoriesSaveService.Parse(raw);
            Assert.Null(code);
            Assert.Equal(CategoriesSaveService.DefaultIcon, Assert.Single(cats!).Icon);
        }
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> — كانَ <c>Difference_3_a_whitespace_only_line_is_refused_at_the_endpoint_and_skipped_by_the_parser</c>.</summary>
    [Fact]
    public void Unified_3_a_whitespace_only_line_is_skipped_at_the_endpoint_too()
    {
        var body = EndpointCallingTheParser();
        Assert.DoesNotContain("Back(\"bad_categories\")", body, StringComparison.Ordinal);

        foreach (var raw in new[] { "a | أ\n   \nb | ب", "a | أ\r\n\r\nb | ب" })
        {
            var (cats, code) = CategoriesSaveService.Parse(raw);
            Assert.Null(code);
            Assert.Equal(new[] { "a", "b" }, cats!.Select(c => c.Slug));
        }
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> — كانَ <c>Difference_4_the_refusal_codes_are_bad_categories_and_no_categories_at_the_endpoint_and_bad_format_and_empty_in_the_parser</c>.
    /// والصَفحَةُ تَقولُ الرَمزَينِ الجَديدَينِ بِالنَصَّينِ نَفسَيهِما، ولا تَقرَأُ القَديمَين.</summary>
    [Fact]
    public void Unified_4_the_endpoint_refuses_with_the_parser_codes_and_the_page_says_them_with_the_same_texts()
    {
        var body = EndpointCallingTheParser();
        Assert.Equal(0, CountOf(body, "Back(\"bad_categories\")"));
        Assert.Equal(0, CountOf(body, "Back(\"no_categories\")"));

        var page = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "Admin", "CreateTenant.razor"));
        Assert.Contains("\"empty\"         => L[\"admin.new.err_no_categories\"]", page, StringComparison.Ordinal);
        Assert.Contains("\"bad_format\"    => L[\"admin.new.err_bad_categories\"]", page, StringComparison.Ordinal);
        Assert.DoesNotContain("\"no_categories\" =>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("\"bad_categories\" =>", page, StringComparison.Ordinal);

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
