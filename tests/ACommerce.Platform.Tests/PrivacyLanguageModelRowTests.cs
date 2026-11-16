using System.Text.RegularExpressions;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Components.Pages.Platform;
using ACommerce.Templates.Customer.Marketplace.Services;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ سَطرُ مُزَوِّدِ النَموذَجِ في سِياسَةِ الخُصوصِيَّةِ يُرسَمُ ما دامَت ميزَةُ نَموذَجٍ تُواجِهُ رائِدَ الأَعمالِ مَفتوحَة ═══
//
// ‏(‏2026-11-07، الموجَة ١٣ W13-1·٨ — ADR-049 §٢·٨). **المَقيسُ قَبلَه**: `/privacy` يَرسُمُ دائِماً «مُزَوِّدُ نَموذَجِ اللُغَة (…) — يَصِلُه نَصُّ فِكرَتِكَ
// وتَحليلُها في الاستوديو ومُحادَثَتُكَ مَعَ الوَكيل، لِتَوليدِ الدِراسَةِ والرَدّ»، بِاسمَي مُزَوِّدَي وَكيلَي `Analysis` و`Studio`. وبَعدَ ·٥ لا يَصِلُ
// التَحليلَ نَصُّ مُستَخدِم، وبَعدَ ·٦ وَكيلُ التَصميمِ خَلفَ مِفتاحٍ مُطفَإ — فَالسَطرُ إفصاحٌ عَمّا لا يَحدُث.
//
// **والآن**: السَطرُ خَلفَ `StudioAgentSwitch.Enabled`، بِاسمِ مُزَوِّدِ وَكيلِ `Studio` وَحدَه، ونَصُّه عَن مُحادَثَةِ وَكيلِ التَصميم. والقِياسُ بِالتَصيير
// (`HtmlRenderer`) لا بِالمَصدَر: المِفتاحُ مُطفَأً لا سَطرَ والقائِمَةُ باقِيَة (عَدّاد)، ومُضاءً سَطرٌ واحِدٌ بِاسمِ المُزَوِّد — بِاللُغَتَين.
public class PrivacyLanguageModelRowTests(ITestOutputHelper output)
{
    private static async Task<string> RenderAsync(bool switchOn, string? lang)
    {
        await using var services = new ServiceCollection()
            .AddSingleton(new L(new HttpContextAccessor()))
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton<IAgentBackendProvider>(new ProbeAgentBackends())
            .AddSingleton(new StudioAgentSwitch(switchOn))
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        var parameters = lang is null
            ? ParameterView.Empty
            : ParameterView.FromDictionary(new Dictionary<string, object?> { ["Lang"] = lang });
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<PlatformPrivacy>(parameters)).ToHtmlString());
    }

    /// <summary>بادِئَةُ السَطرِ المَرسومِ بِلُغَتِه — ما قَبلَ «({0})» مِن القامُوس.</summary>
    private static string Lead(string locale)
    {
        var v = LocaleCatalog.Find(locale, "platform.privacy.share_llm");
        Assert.False(string.IsNullOrWhiteSpace(v), $"«platform.privacy.share_llm» بِلا قيمَةٍ في {locale}.");
        var at = v!.IndexOf("({0})", StringComparison.Ordinal);
        Assert.True(at > 5, "السَطرُ بِلا ({0}) — الأَداةُ عَمياء.");
        return v[..at].TrimEnd();
    }

    /// <summary>صُفوفُ قائِمَةِ «مَن نُشارِكُه»: القائِمَةُ الَّتي فيها سَطرُ الاستِضافَة (<c>share_hosting</c> — لا شَرطَ عَلَيه) — لا أَوَّلُ قائِمَةٍ في الصَفحَة.</summary>
    private static int Rows(string html)
    {
        var lists = Regex.Matches(html, @"<ul class=""wsl-doc-list"">(?<rows>.*?)</ul>", RegexOptions.Singleline);
        var hosting = HostingMark(html);
        var shares = lists.Where(l => l.Groups["rows"].Value.Contains(hosting, StringComparison.Ordinal)).ToList();
        Assert.True(lists.Count >= 2 && shares.Count == 1, $"أَداةٌ عَمياء: {lists.Count} قائِمَة، و{shares.Count} مِنها فيها سَطرُ الاستِضافَة.");
        return Regex.Matches(shares[0].Groups["rows"].Value, "<li>").Count;
    }

    /// <summary>أَوَّلُ ثَلاثينَ مِحرَفاً مِن سَطرِ الاستِضافَةِ بِلُغَةِ الصَفحَة.</summary>
    private static string HostingMark(string html)
    {
        var lang = html.Contains("lang=\"en\"", StringComparison.Ordinal) ? "en" : LocaleCatalog.Arabic;
        var v = LocaleCatalog.Find(lang, "platform.privacy.share_hosting")!;
        return v[..Math.Min(30, v.Length)];
    }

    [Theory]
    [InlineData(null, "ar")]
    [InlineData("en", "en")]
    public async Task With_the_agent_switch_off_the_policy_names_no_language_model_and_keeps_every_other_party(string? lang, string locale)
    {
        var off = await RenderAsync(switchOn: false, lang);
        var on = await RenderAsync(switchOn: true, lang);
        var lead = locale == "en" ? Lead("en") : Lead(LocaleCatalog.Arabic);
        output.WriteLine($"{locale}: صُفوفُ القائِمَةِ مُطفَأً {Rows(off)} · مُضاءً {Rows(on)} · السَطرُ مُطفَأً {Regex.Matches(off, Regex.Escape(lead)).Count} · مُضاءً {Regex.Matches(on, Regex.Escape(lead)).Count}");

        Assert.True(Rows(off) >= 9, $"أَداةٌ عَمياء: {Rows(off)} صَفّاً في قائِمَةِ المُطفَإ.");
        Assert.DoesNotContain(lead, off, StringComparison.Ordinal);
        Assert.DoesNotContain(ProbeAgentBackends.Name, off, StringComparison.Ordinal);

        // والطَرَفُ المُوجَب — بِه يُعرَفُ أَنّ الغِيابَ أَعلاهُ لَيسَ عَمى: صَفٌّ واحِدٌ زائِدٌ بِاسمِ المُزَوِّد.
        Assert.Equal(Rows(off) + 1, Rows(on));
        Assert.Single(Regex.Matches(on, Regex.Escape(lead)));
        Assert.Contains($"({ProbeAgentBackends.Name})", on, StringComparison.Ordinal);
    }

    /// <summary>السَطرُ يُسَمّي مُزَوِّدَ وَكيلِ <c>Studio</c> وَحدَه: الصَفحَةُ لا تَقرَأُ وَكيلَ <c>Analysis</c> بَعدَ اليَوم — يَبلُغُه مُشرِفُ المَنَصَّةِ في حاضِنَتِه
    /// بِنَصِّه هُوَ، لا مُستَخدِم.</summary>
    [Fact]
    public void The_row_names_the_design_agent_provider_only()
    {
        var code = WriteEndpointGuardTests.StripComments(WriteEndpointGuardTests.StripMarkupComments(PrivacyShareInventoryTests.PrivacyPage()));
        Assert.Contains("PrivacyRecipients.LanguageModels([Agents.For(AgentNames.Studio).ProviderName])", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentNames.Analysis", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, Regex.Escape("platform.privacy.share_llm")));
    }

    /// <summary>ونَصُّه عَمّا يَحدُثُ حينَ يُرسَم: مُحادَثَةُ وَكيلِ التَصميم — لا فِكرَةَ ولا تَحليلَ ولا دِراسَة، بِاللُغَتَين.</summary>
    [Fact]
    public void The_row_text_speaks_of_the_design_agent_and_no_longer_of_ideas_analyses_or_studies()
    {
        var ar = StudioEntryCopyTests.Bare(LocaleCatalog.Find(LocaleCatalog.Arabic, "platform.privacy.share_llm")!);
        var en = LocaleCatalog.Find("en", "platform.privacy.share_llm")!;
        output.WriteLine($"ar: {ar}\nen: {en}");
        foreach (var gone in new[] { "فكرت", "تحليل", "الدراسة" })
            Assert.DoesNotContain(gone, ar, StringComparison.Ordinal);
        foreach (var gone in new[] { "idea", "analysis", "study" })
            Assert.DoesNotContain(gone, en, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("وكيل التصميم", ar, StringComparison.Ordinal);
        Assert.Contains("design agent", en, StringComparison.Ordinal);
    }
}
