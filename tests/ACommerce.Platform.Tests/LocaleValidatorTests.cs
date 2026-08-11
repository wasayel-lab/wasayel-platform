using ACommerce.Templates.Customer.Marketplace.I18n;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── بَوّابَة قَوامِيس النُصوص — مُوجَب وسالِب لِكُلّ رَمز ──────────────
//
// نَفس مَنهَج <c>ThemeDefinitionValidatorTests</c>: كُلّ رَمز خَرق لَه
// حالَة تُنتِجُه، وحالَةٌ سَليمَة لا تُنتِجُه. والفَحص يَمُرّ **مِن
// النَّصّ** (‏ReadEntries ← Validate) لا مِن قامُوس يُبنى في الاختِبار
// ويَتَخَطّى القارِئ — وهذا شَرط لا تَجميل: خَرق التَكرار **لا يُرى
// أَصلاً** بَعدَ الطَيّ في قامُوس، فَاختِبارٌ يَبني قامُوساً كانَ
// سَيُصادِق على بَوّابَة لا تَعمَل.

public class LocaleValidatorTests
{
    private static IReadOnlyDictionary<string, IReadOnlyList<LocaleEntry>> Catalog(
        params (string Lang, string Json)[] files)
        => files.ToDictionary(
            f => f.Lang,
            f => LocaleCatalog.ReadEntries(System.Text.Encoding.UTF8.GetBytes(f.Json)),
            StringComparer.Ordinal);

    private static string[] Codes(IReadOnlyList<LocaleViolation> v)
        => v.Select(x => x.Code).ToArray();

    private const string HealthyAr = """
    {
      "shell.nav.home": "الرَئيسيّة",
      "common.action.back": "رُجوع"
    }
    """;

    private const string HealthyEn = """
    {
      "shell.nav.home": "Home",
      "common.action.back": "Back"
    }
    """;

    // ─── المُوجَب — القامُوس السَليم يَمُرّ ────────────────────────────

    [Fact]
    public void HealthyPair_PassesTheGate()
    {
        var c = Catalog(("ar", HealthyAr), ("en", HealthyEn));
        Assert.Empty(LocaleValidator.Validate(c));
        Assert.True(LocaleValidator.IsValid(c));
    }

    /// <summary>العَرَبِيَّة وَحدَها قامُوسٌ كامِل — وهذا عَينُ الحالَة
    /// الَّتي تُرَحَّل إلَيها الشاشات اليَوم: تَرحيلٌ بِالعَرَبِيَّة
    /// فَقَط، ولا يُنتَظَر بِه إنجليزيَّة.</summary>
    [Fact]
    public void ArabicOnly_PassesTheGate()
    {
        var c = Catalog(("ar", HealthyAr));
        Assert.Empty(LocaleValidator.Validate(c));
    }

    /// <summary>لُغَة **جُزئيَّة** تَمُرّ: المِفتاح الناقِص يَسقُط إلى
    /// العَرَبِيَّة، وهذا سُلوك مَقصود لا خَرق.</summary>
    [Fact]
    public void PartialSecondLanguage_PassesTheGate()
    {
        var c = Catalog(("ar", HealthyAr), ("en", """{ "shell.nav.home": "Home" }"""));
        Assert.Empty(LocaleValidator.Validate(c));
    }

    /// <summary>والقَوامِيس المَشحونَة فِعلاً تَجتاز — وإلّا فَالبَوّابَة
    /// تَحرُس اختِباراً لا مُنتَجاً.</summary>
    [Fact]
    public void ShippedCatalog_PassesTheGate()
    {
        var violations = LocaleValidator.ValidateShipped();
        Assert.Empty(violations);
    }

    // ─── السالِب — رَمزٌ رَمزاً ───────────────────────────────────────

    [Fact]
    public void MissingArabicCatalog_IsAViolation()
    {
        var c = Catalog(("en", HealthyEn));
        Assert.Contains("catalog_arabic_missing", Codes(LocaleValidator.Validate(c)));
    }

    [Fact]
    public void EmptyArabicValue_IsAViolation()
    {
        var c = Catalog(("ar", """{ "shell.nav.home": "  " }"""));
        var codes = Codes(LocaleValidator.Validate(c));
        Assert.Contains("key_no_arabic", codes);
        Assert.DoesNotContain("value_empty", codes);   // العَرَبِيَّة لَها رَمزُها
    }

    [Fact]
    public void KeyPresentOnlyInSecondLanguage_IsAViolation()
    {
        var c = Catalog(("ar", HealthyAr),
                        ("en", """{ "shell.nav.home": "Home", "shell.nav.ghost": "Ghost" }"""));
        var v = LocaleValidator.Validate(c);
        Assert.Contains("key_out_of_lexicon", Codes(v));
        Assert.Contains(v, x => x.MessageAr.Contains("shell.nav.ghost"));
    }

    /// <summary>التَكرار — الخَرق الَّذي لا يَراه إلّا قارِئٌ لا
    /// يَطوي.</summary>
    [Fact]
    public void DuplicateKeyInOneFile_IsAViolation()
    {
        var c = Catalog(("ar", """
        {
          "shell.nav.home": "الرَئيسيّة",
          "shell.nav.home": "الرَئيسيَّة"
        }
        """));
        Assert.Contains("key_duplicate", Codes(LocaleValidator.Validate(c)));
    }

    [Theory]
    [InlineData("navhome")]          // مَقطَع واحِد
    [InlineData("nav.home")]         // مَقطَعان
    [InlineData("a.b.c.d")]          // أَربَعَة
    [InlineData("Shell.Nav.Home")]   // كَبير
    [InlineData("shell.nav-home.x")] // شَرطَة وُسطى
    [InlineData("shell..home")]      // مَقطَع فارِغ
    public void KeyOutsideConvention_IsAViolation(string key)
    {
        var c = Catalog(("ar", $$"""{ "{{key}}": "نَصّ" }"""));
        Assert.Contains("key_malformed", Codes(LocaleValidator.Validate(c)));
    }

    [Fact]
    public void EmptyValueInSecondLanguage_IsAViolation()
    {
        var c = Catalog(("ar", HealthyAr),
                        ("en", """{ "shell.nav.home": "" }"""));
        var codes = Codes(LocaleValidator.Validate(c));
        Assert.Contains("value_empty", codes);
        Assert.DoesNotContain("key_no_arabic", codes);
    }

    /// <summary>القيمَة تُكتَب بِلا تَرميز في عُقَد النَصّ — فَالمَحرَف
    /// الَّذي يُغَيِّر بِنيَة المُستَند يُرَدّ هُنا، عِندَ البَوّابَة، لا
    /// عِندَ الاستِعمال.</summary>
    [Theory]
    [InlineData("<b>مُهِمّ</b>")]
    [InlineData("زَيد & عَمرو")]
    [InlineData("أَكبَر > أَصغَر")]
    public void UnsafeMarkupInValue_IsAViolation(string value)
    {
        var c = Catalog(("ar", $$"""{ "shell.nav.home": "{{value}}" }"""));
        Assert.Contains("value_unsafe_markup", Codes(LocaleValidator.Validate(c)));
    }

    /// <summary>والسالِب المُقابِل: نَصّ عَرَبيّ بِمَحارِف التَرقيم
    /// المُستَعمَلَة فِعلاً (‏«» — … ·) لا يُثير الرَمز، وإلّا كانَت
    /// البَوّابَة تَمنَع الكِتابَة الَّتي وُجِدَت لِتَحرُسَها.</summary>
    [Fact]
    public void ArabicPunctuation_IsNotUnsafe()
    {
        var c = Catalog(("ar", """
        { "listings.explore.title": "«كُلّ الإعلانات» — بَحث… · جَديد" }
        """));
        Assert.DoesNotContain("value_unsafe_markup", Codes(LocaleValidator.Validate(c)));
    }
}
