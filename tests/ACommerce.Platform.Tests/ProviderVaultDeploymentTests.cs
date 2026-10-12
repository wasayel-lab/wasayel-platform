using System.Text.RegularExpressions;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ مِفتاحُ الخِزانَةِ في النَشر — الغِيابُ يُخفي ويَرُدُّ، ولا يُسقِط (‏ADR-032 الخُطوَة 11) ═══
//
// **المَقيسُ قَبل (‏2026-10-12)**: `grep Kek docs/DEPLOY.md` = صِفر (موجَزُ الفَجَوات P1-7)،
// ولا فَحصَ في `verify-production-boot.sh`. والقاعِدَةُ في صَفِّ الخُطوَة 11 بِحَرفِها:
// «غِيابُ KEK **يُخفي** بِطاقَةَ المُزَوِّدِ ولا يَعرِضُها ويَرفُضُها».
//
// **وما يُقاسُ هُنا، لا يُدَّعى**: (١) القاعِدَةُ مِن التَسجيلِ الحَقيقيِّ في القالِب — تَهيئَةٌ
// بِلا مِفتاحٍ أَو بِمِفتاحٍ تالِف ⇒ خِزانَةٌ غَيرُ مُتاحَة ⇒ البِطاقَةُ لا تُعرَضُ والرَبطُ يُرَدُّ قَبلَ
// أَوَّلِ حَقل، والرابِطُ المُستَضافُ كَما كان؛ (٢) `DEPLOY.md` يُسَمّي المُتَغَيِّرَ بِحَرفِ الكودِ
// ويُعطي أَمراً لا قيمَة؛ (٣) البَوّابَةُ تُنَبِّهُ بِالحَدِّ والرَمزِ نَفسَيهِما ولا تَخرُجُ بِهِما.
public class ProviderVaultDeploymentTests(ITestOutputHelper output)
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Read(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path}");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 1000, $"أَداةٌ عَمياء: {path} لَم يُقرَأ.");
        return text;
    }

    private static ProviderVault RegisteredVault(string? kek)
    {
        var pairs = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (kek is not null) pairs[ProviderVault.KekConfigKey] = kek;

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(pairs).Build());
        services.AddCustomerMarketplaceTemplate();
        using var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<ProviderVault>();
    }

    // ─── ١. الغِيابُ يُخفي ويَرُدُّ — مِن التَسجيلِ الحَقيقيّ ─────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 !!")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==")]          // ‏16 بايتاً — ‏AES-128 لا يُقبَل
    public void An_absent_or_malformed_kek_hides_the_secret_key_card_and_refuses_its_bind(string? kek)
    {
        var vault = RegisteredVault(kek);
        Assert.False(vault.IsAvailable);

        var offered = ProviderBindSurface.Offered(ProviderCapabilities.Payments, vault)
            .Select(d => d.Slug).ToArray();
        Assert.Equal(new[] { "moyasar_hosted" }, offered);

        var reads = 0;
        var refused = ProviderBindSurface.FromForm("moyasar_api",
            _ => { reads++; return "sk_live_9f3b7c2d41aa58e6"; }, "theme-demo", vault);
        Assert.Equal(ProviderVault.Unavailable, refused.RefusalCode);
        Assert.Equal(0, reads);

        // والرابِطُ لا يَحتاجُ خِزانَة — يُربَطُ كَما كان، ولا يَسقُطُ شَيء.
        var link = ProviderBindSurface.FromForm("moyasar_hosted",
            code => code == "invoice_url" ? "https://moyasar.com/i/inv_1" : null, "theme-demo", vault);
        Assert.Null(link.RefusalCode);
    }

    [Fact]
    public void A_32_byte_kek_shows_the_card_and_seals_the_bind()
    {
        var vault = RegisteredVault(Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(ProviderVault.KekBytes)) + "\n");
        Assert.True(vault.IsAvailable);

        Assert.Contains("moyasar_api",
            ProviderBindSurface.Offered(ProviderCapabilities.Payments, vault).Select(d => d.Slug));

        var read = ProviderBindSurface.FromForm("moyasar_api", _ => "sk_live_9f3b7c2d41aa58e6", "theme-demo", vault);
        Assert.Null(read.RefusalCode);
        Assert.True(Assert.Single(read.Values).Value.IsSealed);
    }

    // ─── ٢. `DEPLOY.md` يُسَمّي المُتَغَيِّرَ بِحَرفِ الكود، ويُعطي أَمراً لا قيمَة ─────

    [Fact]
    public void DEPLOY_names_the_kek_variable_gives_a_command_not_a_value_and_says_absence_hides()
    {
        var deploy = Read("docs", "DEPLOY.md");
        var name = ProviderVault.KekEnvironmentVariable;
        Assert.Equal("Providers__Kek__v1", name);

        var mentions = Regex.Matches(deploy, Regex.Escape(name)).Count;
        output.WriteLine($"`{name}` يَرِدُ في DEPLOY.md ‏{mentions} مَرَّة.");
        Assert.True(mentions >= 4, $"أَداةٌ عَمياء: {mentions} وُروداً — جَدوَلُ الاختِيارِيّاتِ والفِقرَةُ وجَدوَلُها وأَعطالُ §٥.");

        // صَفٌّ في جَدوَلِ المُتَغَيِّراتِ الاختِيارِيَّة، وفِقرَتُه.
        Assert.Contains($"| `{name}` | **مِفتاحُ خِزانَةِ أَسرارِ المُستَأجِرين**", deploy, StringComparison.Ordinal);
        Assert.Contains($"## ٢·ح. خِزانَةُ أَسرارِ المُستَأجِرين — `{name}`", deploy, StringComparison.Ordinal);

        // أَمرٌ يُوَلِّدُ القيمَة — لا قيمَة: لا ‏44 مِحرَفاً مِن base64 بَعدَ اسمِ المُتَغَيِّر.
        Assert.Contains("openssl rand -base64 32", deploy, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(Regex.Escape(name) + @"\s*=\s*""?[A-Za-z0-9+/]{43}="), deploy);

        // والقاعِدَة: الغِيابُ يُخفي ولا يُسقِط، بِرَمزِها.
        Assert.Contains(ProviderVault.Unavailable, deploy, StringComparison.Ordinal);
        Assert.Contains("ولا يَسقُطُ الإقلاع", deploy, StringComparison.Ordinal);
    }

    // ─── ٣. البَوّابَةُ تُنَبِّهُ ولا تُفشِل ─────────────────────────────────

    /// <summary>كُتلَةُ المِفتاحِ في <c>verify-production-boot.sh</c> — مِن تَرويسَتِها إلى
    /// بَصمَةِ القاعِدَة.</summary>
    private static string KekBlock(string script)
    {
        var start = script.IndexOf("# ─── مِفتاحُ الخِزانَة (", StringComparison.Ordinal);
        Assert.True(start > 0, "كُتلَةُ مِفتاحِ الخِزانَةِ غائِبَةٌ عَن البَوّابَة.");
        var end = script.IndexOf("# بَصمَةُ القاعِدَة", start, StringComparison.Ordinal);
        Assert.True(end > start, "نِهايَةُ الكُتلَةِ غائِبَة — تَغَيَّرَ تَرتيبُ البَوّابَة.");
        return script[start..end];
    }

    [Fact]
    public void VerifyProductionBoot_warns_on_an_absent_or_malformed_kek_and_never_exits_for_it()
    {
        var script = Read("scripts", "verify-production-boot.sh");
        var block = KekBlock(script);

        // الاسمُ والحَدُّ والرَمزُ بِحَرفِ الكود.
        Assert.Contains($"${{{ProviderVault.KekEnvironmentVariable}:-}}", block, StringComparison.Ordinal);
        Assert.Contains($"[ \"$KEK_BYTES\" = \"{ProviderVault.KekBytes}\" ]", block, StringComparison.Ordinal);
        Assert.Contains(ProviderVault.Unavailable, block, StringComparison.Ordinal);

        // تُنَبِّهُ ولا تُفشِل: لا `exit` في الكُتلَة.
        Assert.DoesNotMatch(new Regex(@"(^|\s)exit(\s|$)", RegexOptions.Multiline), block);

        // ولا تَطبَعُ القيمَة: لا سَطرَ `say`/`echo` يَحمِلُ تَوسيعَ المُتَغَيِّر.
        var printing = block.Split('\n')
            .Where(l => l.TrimStart().StartsWith("say ", StringComparison.Ordinal)
                     || l.TrimStart().StartsWith("echo ", StringComparison.Ordinal))
            .ToArray();
        output.WriteLine($"سَطرُ طِباعَةٍ في كُتلَةِ المِفتاح: {printing.Length}");
        Assert.True(printing.Length >= 3, $"أَداةٌ عَمياء: {printing.Length} سَطرَ طِباعَة.");
        Assert.All(printing, l => Assert.DoesNotContain("$" + "{" + ProviderVault.KekEnvironmentVariable + "}", l, StringComparison.Ordinal));
        Assert.All(printing, l => Assert.DoesNotContain("$" + ProviderVault.KekEnvironmentVariable, l, StringComparison.Ordinal));

        // وتَقَعُ بَعدَ فَحصِ سِرِّ الجَلسَةِ وقَبلَ النَشر — تَهيئَةٌ تُقرَأُ قَبلَ دَقيقَةِ البِناء.
        Assert.True(script.IndexOf("SECRET_LEN=", StringComparison.Ordinal)
                    < script.IndexOf("# ─── مِفتاحُ الخِزانَة (", StringComparison.Ordinal));
        Assert.True(script.IndexOf("# ─── مِفتاحُ الخِزانَة (", StringComparison.Ordinal)
                    < script.IndexOf("dotnet publish", StringComparison.Ordinal));
    }
}
