using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مُوافَقَةُ الاستوديو، الإصدارُ 4 — لا جُملَةَ عَن تَحليلٍ أَو نَموذَج، وما بَقِيَ لَه طَرَفٌ مَنشور ═══
//
// ‏(‏2026-11-07، الموجَة ١٣ W13-1·٨ — ADR-049 §٥). **المَقيسُ قَبلَه** (الإصدارُ 3): البَندُ الأَوَّلُ «الدِراسات يُولِّدها نَموذَج ذَكاء اصطِناعيّ»،
// والثاني «تَبنيه عَلى التَّحليل»، والثالِثُ «(هاتِف، أَفكار، تَحاليل) … مُزَوِّدِ نَموذَجِ اللُغَةِ لِتَوليدِ الدِراسَة»، والرابِعُ «التَحاليلُ والتَحسيناتُ
// تَتَجَدَّدُ كُلَّ ٣٠ يَوماً»، والخامِسُ «التَّحاليل القَديمَة تَبقى صالِحَة ٦ أَشهُر» — خَمسَةُ بُنودٍ كُلُّها تَعِدُ أَو تُفصِحُ عَمّا خَرَجَ مِن الرِحلَة.
//
// **ويُحرَسُ الحَدُّ بِماسِحٍ لا بِقائِمَةِ جُمَل**: كُلُّ قيمَةٍ في `studio.consent.*` تُجَرَّدُ مِن التَشكيلِ وتُفحَصُ بِجُذورِ التَحليلِ والنَموذَج — والماسِحُ
// يَطبَعُ ما عَدَّه ويَفشَلُ عَلى الصِفر، ويُقاسُ بِعَيبٍ مَحقون. وجُمَلُ الاشتِراكِ في الرابِعِ بِحَرفِ الإصدارِ 3، والأَوَّلُ مِن شُروطِ المَنَصَّةِ المَنشورَة.
public class StudioConsentFourthVersionTests(ITestOutputHelper output)
{
    /// <summary>جُذورٌ مُجَرَّدَةٌ مِن التَشكيلِ لا تَقولُها مُوافَقَةُ الإصدارِ 4. («تَحسين الخِدمَة» في الثالِثِ لَيسَ «التَحسينات» — المُفرَدُ لا يُحظَر.)</summary>
    internal static readonly string[] Gone = ["تحليل", "تحاليل", "دراس", "ذكاء", "نموذج", "فكرة", "فكرت", "افكار", "التحسينات"];

    /// <summary>ما يَقولُ مِن <see cref="Gone"/> كُلُّ قيمَة: (المِفتاح، الجَذر).</summary>
    internal static IReadOnlyList<(string Key, string Root)> Offenders(IEnumerable<LocaleEntry> entries)
        => entries.Where(e => e.Key.StartsWith("studio.consent.", StringComparison.Ordinal))
                  .SelectMany(e => Gone.Where(g => StudioEntryCopyTests.Bare(e.Value).Contains(g, StringComparison.Ordinal)).Select(g => (e.Key, g)))
                  .ToList();

    private static string Ar(string key)
    {
        var v = LocaleCatalog.Find(LocaleCatalog.Arabic, key);
        Assert.False(string.IsNullOrWhiteSpace(v), $"«{key}» بِلا قيمَةٍ عَرَبِيَّة.");
        return v!;
    }

    [Fact]
    public void No_consent_value_speaks_of_analysis_studies_ideas_or_a_model()
    {
        var consent = LocaleCatalog.Raw(LocaleCatalog.Arabic).Where(e => e.Key.StartsWith("studio.consent.", StringComparison.Ordinal)).ToList();
        var offenders = Offenders(consent);
        output.WriteLine($"قِيَمُ studio.consent.* المَفحوصَة: {consent.Count} · الجُذور: {Gone.Length} · المُخالِف: {offenders.Count}");
        Assert.True(consent.Count >= 14, $"أَداةٌ عَمياء: {consent.Count} قيمَة.");
        Assert.Empty(offenders);
    }

    /// <summary>الماسِحُ يَرى — نَصُّ الإصدارِ 3 بِحَرفِه يُمسَكُ في البُنودِ الخَمسَة، وجُملَةٌ مَحقونَةٌ في مِفتاحٍ واحِدٍ تُمسَكُ بِاسمِه.</summary>
    [Fact]
    public void The_scanner_catches_the_version_three_text_and_an_injected_sentence()
    {
        var v3 = new[]
        {
            new LocaleEntry("studio.consent.item1_body", "الدِراسات يُولِّدها نَموذَج ذَكاء اصطِناعيّ بِناءً على بيانات السوق المُتاحَة. قَد تَحتَوي عَلى تَخمينات."),
            new LocaleEntry("studio.consent.item2_body", "أَنتَ المَسؤول الوَحيد عَن أَيّ قَرار استِثماريّ أَو تَشغيليّ تَبنيه عَلى التَّحليل."),
            new LocaleEntry("studio.consent.item3_body", "نَحفَظ بَياناتِكَ (هاتِف، أَفكار، تَحاليل) لِاستِخدامِها في تَحسين الخِدمَة."),
            new LocaleEntry("studio.consent.item4_body", "لِكُلِّ باقَةٍ في الاستوديو حُدود: التَحاليلُ والتَحسيناتُ تَتَجَدَّدُ كُلَّ ٣٠ يَوماً."),
            new LocaleEntry("studio.consent.item5_body", "التَّحاليل القَديمَة تَبقى صالِحَة ٦ أَشهُر."),
            new LocaleEntry("other.key", "دِراسَةُ جَدوى"),
        };
        var caught = Offenders(v3).Select(o => o.Key).Distinct().ToArray();
        Assert.Equal(new[] { "studio.consent.item1_body", "studio.consent.item2_body", "studio.consent.item3_body", "studio.consent.item4_body", "studio.consent.item5_body" }, caught);

        var live = LocaleCatalog.Raw(LocaleCatalog.Arabic).ToList();
        var injected = live.Select(e => e.Key == "studio.consent.item2_body" ? e with { Value = e.Value + " والدِراسَةُ تَقديرِيَّة." } : e);
        Assert.Equal(new[] { ("studio.consent.item2_body", "دراس") }, Offenders(injected).ToArray());
    }

    /// <summary><b>البَندُ الأَوَّلُ لا يَختَرِعُ دَعوى</b> (القاعِدَة ١٦): جُملَتاه بَعدَ الأُولى مِن شُروطِ المَنَصَّةِ المَنشورَة — البَندُ ٧ (المُحتَوى مِلكٌ لِأَصحابِه
    /// ومَسؤولِيَّتُهم) وحاشِيَةُ البَندِ ٢ (المَنَصَّةُ تُوَفِّرُ الأَداةَ ولا تَكونُ طَرَفاً). والأُولى «تُنشِئُ مَتجَرَكَ وتَضبُطُه بِنَفسِك» هِيَ بابُ ·٣.</summary>
    [Fact]
    public void The_first_item_says_what_the_published_platform_terms_say_about_content()
    {
        var body = Ar("studio.consent.item1_body");
        Assert.Equal("١. مَتجَرُكَ ومُحتَواه.", Ar("studio.consent.item1_title"));
        foreach (var (said, termsKey) in new[]
        {
            ("مِلكٌ لِأَصحابِه ومَسؤولِيَّتُهم", "platform.terms.s7_body"),
            ("تُوَفِّرُ الأَداةَ ولا تَكونُ طَرَفاً", "platform.terms.s2_note"),
        })
        {
            Assert.Contains(said, body, StringComparison.Ordinal);
            Assert.Contains(said, Ar(termsKey), StringComparison.Ordinal);
        }
        Assert.StartsWith("تُنشِئُ مَتجَرَكَ وتَضبُطُه بِنَفسِك.", body, StringComparison.Ordinal);
        Assert.Equal("/studio/new", StudioStoreCreate.Page);
    }

    /// <summary>نَصُّ البَندِ الرابِعِ في الإصدارِ 3 بِحَرفِه — يُقارَنُ بِه ذَيلُه.</summary>
    private const string Item4AtVersionThree =
        "لِكُلِّ باقَةٍ في الاستوديو حُدود: التَحاليلُ والتَحسيناتُ تَتَجَدَّدُ كُلَّ ٣٠ يَوماً، وعَدَدُ التَطبيقاتِ حَدٌّ ثابِت. ويُتَّفَقُ عَلى اشتِراكِ مَتجَرِكَ وسِعرِه ومُدَّتِه مَعَ المَنَصَّة، ويُعرَضُ لَكَ في الاستوديو قَبلَ أَيِّ دَفع. وعِندَ انتِهاءِ المُدَّةِ دونَ تَجديدٍ يَتَوَقَّفُ النَشرُ ويَبقى المَتجَرُ ظاهِراً خِلالَ مُهلَةِ السَماحِ المُتَّفَقِ عَلَيها، ثُمَّ يُخفى حَتّى التَجديد؛ ولِلمَنَصَّةِ تَعليقُه عِندَ عَدَمِ السَداد. ولا يُحذَفُ شَيءٌ مِن بَياناتِه في الحالَين. والتَفصيلُ في شُروطِ المَنَصَّة: البَندُ ٤ (الباقاتُ والفَوتَرَةُ والتَجديد) والبَندُ ٩ (التَعليقُ والإنهاء)، وهي المَرجِعُ عِندَ الاختِلاف.";

    /// <summary><b>جُمَلُ الاشتِراكِ والسَماحِ والتَعليقِ بِحَرفِها</b> (‏ADR-049 §٥): ما بَعدَ الجُملَةِ الأُولى في الإصدارِ 4 هو ما بَعدَها في 3 بايتاً، والأُولى
    /// وَحدَها تَبَدَّلَت — مِن حُدودٍ تَتَجَدَّدُ إلى حَدِّ المَتاجِرِ الثابِت.</summary>
    [Fact]
    public void The_fourth_item_keeps_every_subscription_grace_and_suspension_sentence_of_version_three()
    {
        var now = Ar("studio.consent.item4_body");
        static string Tail(string s) => s[(s.IndexOf(". ", StringComparison.Ordinal) + 2)..];
        output.WriteLine($"الذَيلُ: {Tail(now).Length} مِحرَفاً · الجُملَةُ الأُولى: «{now[..now.IndexOf(". ", StringComparison.Ordinal)]}»");
        Assert.True(Tail(now).Length > 300, "أَداةٌ عَمياء: الذَيلُ قَصير.");
        Assert.Equal(Tail(Item4AtVersionThree), Tail(now));
        Assert.StartsWith("لِكُلِّ باقَةٍ في الاستوديو حَدٌّ ثابِتٌ لِعَدَدِ المَتاجِر. ", now, StringComparison.Ordinal);
        Assert.NotEqual(Item4AtVersionThree, now);
    }

    /// <summary>والبَندانِ الثاني والخامِسُ أَسقَطا جُملَةَ التَحليلِ وَحدَها — والباقي بِحَرفِه.</summary>
    [Fact]
    public void The_second_and_fifth_items_drop_only_their_analysis_clause()
    {
        Assert.Equal("أَنتَ المَسؤول الوَحيد عَن أَيّ قَرار استِثماريّ أَو تَشغيليّ تَتَّخِذُه في مَتجَرِك. المَنصَّة لَيسَت بَديلاً عَن مَشورَة قانونيَّة/مُحاسَبيَّة.",
            Ar("studio.consent.item2_body"));
        Assert.Equal("قَد نُحَدِّث الشُروط. التَّحديث يَتَطَلَّب قَبولاً جَديداً.", Ar("studio.consent.item5_body"));
        Assert.Equal(4, ConsentPolicy.CurrentVersion);
    }
}
