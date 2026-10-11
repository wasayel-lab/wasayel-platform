using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ رَفضُ النُقطَةِ يُقرَأُ عَلى صَفحَةِ الصَفقَةِ لا يُبتلَع ═══════════════════
//
// **المَقيسُ قَبلَ هذا الكوميت** (مُراجَعَةُ المَوجَة ١؛ مُثَبَّتٌ في
// `DealCashPathCharacterizationTests` ‏5554975): `/accept` و`/cancel` تَرتَدّانِ إلى
// `deals/{id}?err=<code>` وفي `MyDealDetail.razor` **صِفرُ** `SupplyParameterFromQuery` —
// فَالنَقرَةُ المَرفوضَةُ تَعودُ بِصَفحَةٍ كَأَنَّ شَيئاً لَم يَكُن.
//
// **والقَرار**: مُعامِلُ `err` ودالَّةٌ نَقِيَّةٌ `DealRefusalTexts.KeyFor` تَحُلُّ الرَمزَ
// مِفتاحاً مِن مَعجَمَي التَخويلِ وأُنبوبِ المُحادَثَة، والكَلامُ في `ar.json` (القاعِدَة
// ١١) — نَسَقُ `store.locked.write_denied` في `TenantHome.razor`. **لِكُلِّ رَمزٍ مِفتاحٌ
// حَيٌّ**، وما خَرَجَ عَن المَعجَمِ لا يُرسَمُ لَه شَيء.

public class DealRefusalTextsTests
{
    [Fact]
    public void The_lexicon_is_the_union_of_both_authorizations_and_the_chat_pipe_code_with_shared_codes_once()
    {
        var expected = DealAcceptAuthorization.All
            .Concat(DealCancelAuthorization.All)
            .Append(ChatStartReturn.PartnerMissing)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal);
        Assert.Equal(expected, DealRefusalTexts.Codes.OrderBy(c => c, StringComparer.Ordinal));
        // 6 + 3 − 2 مُشتَرَكَين (deal_not_found، deal_not_active) + 1 = 8
        Assert.Equal(8, DealRefusalTexts.Codes.Count);
        Assert.Equal(DealRefusalTexts.Codes.Count, DealRefusalTexts.Codes.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary><b>المُوجِب لِكُلِّ رَمز</b>: مِفتاحٌ بِالبادِئَةِ، ونَصٌّ عَرَبِيٌّ حَيٌّ في
    /// القامُوس لَيسَ نائِباً ولا المِفتاحَ خاماً.</summary>
    [Fact]
    public void Every_code_of_both_lexicons_and_the_chat_pipe_has_a_live_arabic_text()
    {
        var checked_ = 0;
        foreach (var code in DealAcceptAuthorization.All.Concat(DealCancelAuthorization.All).Append(ChatStartReturn.PartnerMissing))
        {
            var key = DealRefusalTexts.KeyFor(code);
            Assert.Equal(DealRefusalTexts.Prefix + code, key);
            var text = LocaleCatalog.Text(LocaleCatalog.Arabic, key!);
            Assert.NotEqual(key, text);
            Assert.False(LocaleCatalog.IsPlaceholder(text), $"‏{key} نائِب.");
            Assert.True(text.Length > 10, $"‏{key} بِنَصٍّ أَقصَرَ مِن أَن يُفَسِّر.");
            checked_++;
        }
        Assert.Equal(10, checked_);   // 6 + 3 + 1 — المُشتَرَكُ يُفحَصُ مَرَّتَين ويَصِلُ مِفتاحاً واحِداً
    }

    /// <summary><b>السالِب</b>: رَمزٌ خارِجَ المَعجَم، أَو غِيابٌ، أَو فَراغ — لا مِفتاحَ،
    /// فَلا شَريطَ ولا مِفتاحَ خاماً عَلى الشاشَة.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("expired")]
    [InlineData("deal_not_found ")]
    [InlineData("DEAL_NOT_FOUND")]
    [InlineData("pay_card_unavailable")]
    public void A_code_outside_the_lexicon_has_no_key(string? code)
        => Assert.Null(DealRefusalTexts.KeyFor(code));

    // ─── والصَفحَةُ تَقرَأُ المُعامِلَ وتَرسُمُ الشَريطَ مِن الدالَّةِ نَفسِها ──────

    [Fact]
    public void The_deal_page_reads_err_and_renders_the_strip_through_the_lexicon()
    {
        var page = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "MyDealDetail.razor"));
        Assert.True(page.Length > 1000, "أَداةٌ عَمياء: `MyDealDetail.razor` لَم يُقرَأ.");

        Assert.Contains("[SupplyParameterFromQuery(Name = \"err\")] public string? Err { get; set; }", page, StringComparison.Ordinal);
        Assert.Contains("@if (DealRefusalTexts.KeyFor(Err) is { } errKey)", page, StringComparison.Ordinal);
        Assert.Contains("<AcAlert Variant=\"danger\" Message=\"@L[errKey]\" />", page, StringComparison.Ordinal);
        // ولا نَصَّ رَفضٍ مَكتوباً بِاليَد في الصَفحَة — المِفتاحُ مِن الدالَّة.
        Assert.DoesNotContain("deals.detail.err_", page, StringComparison.Ordinal);
        // واصطِلاحُ المِفتاحِ ثَلاثَةُ مَقاطِعَ (LocaleValidator.KeyPattern) — الرَمزُ في الثالِث.
        Assert.Equal("deals.detail.err_", DealRefusalTexts.Prefix);
        Assert.All(DealRefusalTexts.Codes, c => Assert.Equal(3, DealRefusalTexts.KeyFor(c)!.Split('.').Length));

        // والنُقطَتانِ تَرتَدّانِ بِالرَمزِ نَفسِه الَّذي تَقرَؤُه الصَفحَة.
        var endpoints = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates",
            "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs"));
        Assert.Equal(2, endpoints.Split("$\"?err={res.Violation!.Code}\"").Length - 1);
    }
}
