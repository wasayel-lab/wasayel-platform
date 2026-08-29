using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ النائِبُ غَيرُ المَملوءِ لا يُعرَضُ لِزائِر ═══════════════════════
//
// **المَقيسُ مِن اللَقَطاتِ المُودَعَة قَبلَ الإصلاح**: `wsl-placeholder`
// في **‏136 صَفحَةً مِن ‏137**، و`wsl-doc-note` — الجُملَةُ المُفَسِّرَة
// — في **‏7** وَحدَها (الوَثائِقُ القانونِيَّةُ بِفَرعَيها اللُغَوِيَّين).
// أَي أَنّ الصُندوقَ الأَحمَرَ المُتَقَطِّعَ «يَملَؤُه المالِك» كانَ
// **أَسفَلَ صَفحَةِ الشِراءِ عِندَ زَبونِ التاجِر** بِلا الجُملَةِ
// الَّتي تُفَسِّرُه — لِأَنّ التَذييلَ يُصَيَّرُ مِن فُروعِ
// `MainLayout` الثَلاثَة.
//
// **والكَشفُ بِعَلامَةٍ صَريحَةٍ في القيمَةِ نَفسِها** (`[[ … ]]`) لا
// بِقائِمَةِ مَفاتيحَ تُنسى ولا بِتَخمينٍ مِن الطول: العَلامَةُ
// تَسقُطُ مَعَ القيمَةِ حينَ تُملَأ.

public class PlatformDocFieldsTests
{
    // ─── العَلامَة ───────────────────────────────────────────────────

    [Theory]
    [InlineData("[[ اسمُ الكِيانِ النِظاميّ — يَملَؤُه المالِك ]]")]
    [InlineData("[[ Legal entity name — to be filled in by the owner ]]")]
    [InlineData("  [[ x ]]  ")]
    public void AMarkedValue_IsAPlaceholder(string value)
        => Assert.True(LocaleCatalog.IsPlaceholder(value));

    /// <summary><b>وما لا يَحمِلُ العَلامَةَ لَيسَ نائِباً</b> — ولا
    /// يُخمَّنُ مِن طولٍ ولا مِن كَلِمَة. و<b>نِصفُ العَلامَةِ لا
    /// تَكفي</b>: قيمَةٌ مَملوءَةٌ فيها قَوسانِ مُضاعَفانِ في
    /// أَوَّلِها وَحدَها لَيسَت نائِبَة.</summary>
    [Theory]
    [InlineData("شَرِكَةُ وَسايِل لِلتِقنِيَة")]
    [InlineData("Wasayel Technologies LLC")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("[[ بِلا خاتِمَة")]
    [InlineData("بِلا فاتِحَة ]]")]
    public void AFilledOrUnmarkedValue_IsNotAPlaceholder(string? value)
        => Assert.False(LocaleCatalog.IsPlaceholder(value));

    // ─── حُقولُ الكِيان — والقائِمَةُ مَحروسَةٌ مِن الطَرَفَين ────────

    /// <summary>كُلُّ مِفتاحٍ مُدرَجٍ لَه نَصٌّ في العَرَبِيَّةِ
    /// وفي الإنجليزِيَّة.</summary>
    [Fact]
    public void EveryEntityKey_ExistsInBothLocales()
    {
        Assert.NotEmpty(PlatformDocFields.EntityKeys);

        foreach (var key in PlatformDocFields.EntityKeys)
        {
            Assert.False(string.IsNullOrWhiteSpace(LocaleCatalog.Find("ar", key)),
                $"لا نَصَّ عَرَبيٌّ لِلمِفتاح «{key}».");
            Assert.False(string.IsNullOrWhiteSpace(LocaleCatalog.Find("en", key)),
                $"لا نَصَّ إنجليزيٌّ لِلمِفتاح «{key}».");
        }
    }

    /// <summary>
    /// <para><b>وهذا هُوَ الحارِسُ الَّذي يَمنَعُ الانجِراف</b>:
    /// مِفتاحٌ نائِبٌ يُضافُ غَداً تَحتَ <c>platform.doc.</c> ولا
    /// يُدرَجُ في <see cref="PlatformDocFields.EntityKeys"/> كانَ
    /// سَيَمُرُّ بِلا تَحذيرِ مُشرِفٍ — <b>فَيُقَدَّمُ النِطاقُ
    /// بِوَثائِقَ ناقِصَةٍ ويُرَدّ</b>.</para>
    /// </summary>
    [Fact]
    public void EveryPlaceholderKeyInTheDictionary_IsListed()
    {
        var stray = LocaleCatalog.Lexicon
            .Where(k => k.StartsWith(PlatformDocFields.KeyPrefix, StringComparison.Ordinal))
            .Where(k => LocaleCatalog.IsPlaceholderKey("ar", k))
            .Where(k => !PlatformDocFields.EntityKeys.Contains(k, StringComparer.Ordinal))
            .ToList();

        Assert.Empty(stray);
    }

    /// <summary><b>واليَومَ الخَمسَةُ كُلُّها نائِبَة</b> — فَتَحذيرُ
    /// <c>/admin</c> ظاهِرٌ، والوَثائِقُ غَيرُ جاهِزَةٍ لِلتَقديم.
    /// وحينَ يَملَؤُها المالِكُ يَنزِلُ العَدَدُ ويَختَفي
    /// التَحذيرُ بِلا لَمسِ سَطرِ كود.</summary>
    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void TheCountIsExactlyThePlaceholdersAmongTheListedKeys(string lang)
    {
        var counted = PlatformDocFields.EntityKeys
            .Count(k => LocaleCatalog.IsPlaceholder(LocaleCatalog.Text(lang, k)));

        Assert.Equal(counted, PlatformDocFields.UnfilledCount(lang));
        Assert.Equal(PlatformDocFields.EntityKeys.Count, PlatformDocFields.UnfilledCount(lang));
    }

    // ─── النائِبُ الإنجليزيُّ يُقرَأُ مِن en.json ─────────────────────

    /// <summary>
    /// <para><b>والصَفحَةُ الإنجليزِيَّةُ تُقَرِّرُ
    /// بِـ<c>en.json</c> لا بِـ<c>ar.json</c>.</b> فَنَصُّ النائِبِ
    /// الَّذي تَراهُ عَينُ مُراجِعِ بَوّابَةِ الدَفعِ إنجليزيّ،
    /// و<c>PlatformDocLanguage</c> هي مَن يَقرَؤُه — لا
    /// <c>L</c> المَربوطَةُ بِالكوكي.</para>
    /// </summary>
    [Fact]
    public void TheEnglishDocument_ReadsTheEnglishPlaceholder()
    {
        var en = PlatformDocLanguage.FromRoute("en");
        var ar = PlatformDocLanguage.FromRoute(null);

        foreach (var key in PlatformDocFields.EntityKeys)
        {
            Assert.True(LocaleCatalog.IsPlaceholder(en[key]));
            Assert.NotEqual(ar[key], en[key]);
        }
    }

    // ─── التَذييلُ يَسقُطُ إلى اسمِ المَنَصَّة ───────────────────────

    /// <summary><b>والبَديلُ الَّذي يَسقُطُ إلَيه التَذييلُ نَصٌّ
    /// حَقيقيٌّ لا مِفتاحٌ عارٍ.</b> وكانَ
    /// <c>platform.footer.heading</c> <b>مِفتاحاً بِصِفرِ
    /// مُستَهلِك</b> (القاعِدَة ١) حَتّى صارَ هُوَ الاسمَ
    /// المَعروض.</summary>
    [Fact]
    public void TheFooterFallback_IsARealName_NotAPlaceholder()
    {
        var name = LocaleCatalog.Find("ar", "platform.footer.heading");

        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.False(LocaleCatalog.IsPlaceholder(name));
    }
}
