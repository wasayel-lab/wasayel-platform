using System.Diagnostics;
using System.Text;
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
// ويُعطي أَمراً لا قيمَة؛ (٣) البَوّابَةُ تُنَبِّهُ بِالحَدِّ والرَمزِ نَفسَيهِما ولا تَخرُجُ بِهِما؛
// (٤) — مُراجَعَةُ ‏2026-10-12 — والمَضبوطُ الصالِحُ يُنَبَّهُ عَلَيه أَيضاً ما دامَ المِفتاحُ المَختومُ
// لا يَقبِض، بِسَبَبٍ يُقاسُ مِن الكودِ لا يُكتَبُ ثابِتاً.
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

    // ─── ٤. والمَضبوطُ الصالِحُ يُنَبَّهُ عَلَيه ما دامَ المَختومُ لا يَقبِض (مُراجَعَةُ ‏2026-10-12) ───
    //
    // **المَقيسُ قَبل**: فَرعُ «يُفَكُّ إلى ‏32 بايتاً» كانَ يَطبَعُ «مَضبوط» وَحدَه — سَطرَ نَجاحٍ بِلا ⚠
    // لِلحالِ الَّتي يَنهى عَنها DEPLOY.md §٢·ح: ضَبطُ المِفتاحِ يُظهِرُ بِطاقَةَ المِفتاحِ السِرِّيّ،
    // ورَبطُها يَحِلُّ مَحَلَّ رابِطِ الدَفعِ المُستَضاف (`BindProviderAsync`: الوَثيقَةُ بِمُعَرِّفِ القُدرَة)
    // ولا يَقبِضُ بِه شَيءٌ قَبلَ عَميلِ البَوّابَة (‏ADR-032 الخُطوَة 8). **والسَبَبُ لا يُكتَبُ هُنا ثابِتاً
    // بَل يُقاس**: رَبطُ المِفتاحِ وَحدَه كَما تَكتُبُه نُقطَةُ الرَبط (`ProviderSealedKeyDoorsTests.SealedOnly`)
    // أَيَقبِض؟ — والتَنبيهُ وفِقرَةُ DEPLOY.md يَتبَعانِه في الاتِّجاهَين: يَحمَرُّ الصَفُّ إن سَقَطا
    // والمَختومُ لا يَقبِض، أَو بَقِيا بَعدَ أَن يَقبِض. فَكوميتُ الخُطوَة 8 الَّذي يَقلِبُ `CollectsMoney`
    // يَحذِفُهُما مَعَه أَو يَحمَرُّ هذا الصَفُّ بِاسمِه.

    /// <summary>سُطورُ <c>say</c> في فَرعِ «مَضبوطٍ يُفَكُّ إلى ‏32 بايتاً» — مِن مُقارَنَةِ الحَدِّ إلى
    /// <c>else</c> الَّتي تَليها.</summary>
    private static string[] SetAndValidSays(string block)
    {
        var then = block.IndexOf($"[ \"$KEK_BYTES\" = \"{ProviderVault.KekBytes}\" ]; then", StringComparison.Ordinal);
        Assert.True(then > 0, "فَرعُ المَضبوطِ الصالِحِ غائِبٌ عَن كُتلَةِ المِفتاح.");
        var otherwise = block.IndexOf("\n    else", then, StringComparison.Ordinal);
        Assert.True(otherwise > then, "نِهايَةُ فَرعِ المَضبوطِ غائِبَة — تَغَيَّرَ شَكلُ الكُتلَة.");
        return block[then..otherwise].Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("say ", StringComparison.Ordinal))
            .ToArray();
    }

    [Fact]
    public void VerifyProductionBoot_warns_on_a_valid_kek_exactly_while_a_sealed_key_collects_nothing()
    {
        var sealedCollects = ProviderSealedKeyDoorsTests.SealedOnly().CollectsMoney;

        var says = SetAndValidSays(KekBlock(Read("scripts", "verify-production-boot.sh")));
        var warnings = says.Where(l => l.StartsWith("say \"⚠", StringComparison.Ordinal)).ToArray();
        output.WriteLine($"المِفتاحُ المَختومُ وَحدَه يَقبِض: {sealedCollects} · سُطورُ فَرعِ المَضبوط: {says.Length} · مِنها ⚠: {warnings.Length}");
        Assert.True(says.Length >= 1, "أَداةٌ عَمياء: فَرعُ المَضبوطِ بِلا سَطرِ طِباعَة.");

        // الحالُ نَفسُه يُقالُ بِاسمِه أَوَّلاً في الحالَين — «مَضبوط» لا يَختَفي.
        Assert.StartsWith("say \"مِفتاحُ الخِزانَة: مَضبوط", says[0], StringComparison.Ordinal);

        // والتَنبيهُ يَتبَعُ سَبَبَه في الاتِّجاهَين، ويُسَمّي الأَثَرَ والفِقرَة.
        Assert.Equal(sealedCollects ? 0 : 1, warnings.Length);
        Assert.All(warnings, w =>
        {
            Assert.Contains("عَميلُ البَوّابَةِ غَيرُ مَبنِيّ", w, StringComparison.Ordinal);
            Assert.Contains("يَحِلُّ مَحَلَّ رابِطِ الدَفع", w, StringComparison.Ordinal);
            Assert.Contains("DEPLOY.md §٢·ح", w, StringComparison.Ordinal);
        });

        // وفِقرَةُ DEPLOY.md الَّتي يُحيلُ إلَيها تَتبَعُ السَبَبَ نَفسَه — لا تَبقى بَعدَه ولا تَسقُطُ قَبلَه.
        var deploy = Read("docs", "DEPLOY.md");
        Assert.Equal(!sealedCollects,
            deploy.Contains("### ⚠️ لا يُضبَطُ في الإنتاجِ قَبلَ عَميلِ البَوّابَة", StringComparison.Ordinal));
        Assert.Equal(!sealedCollects,
            deploy.Contains("**ومَعَه سَطرُ ⚠ «وعَميلُ البَوّابَةِ غَيرُ مَبنِيّ»**", StringComparison.Ordinal));
    }

    // ─── ٥. الكُتلَةُ تُشَغَّلُ لا تُقرَأُ فَحَسب — حُكمُها حُكمُ الخِزانَة (مُراجَعَةُ ‏2026-10-12) ───
    //
    // الصَفّانِ ٣ و٤ يَقرَآنِ نَصَّ السِكربت، والنَصُّ لا يُري ما تَطبَعُه الكُتلَةُ فِعلاً: الإسنادُ تَحتَ
    // `pipefail` لا يُرى إلّا بِالتَشغيل. فَهُنا تُشَغَّلُ الكُتلَةُ نَفسُها — مَقطوعَةً بِـ`KekBlock` كَما هِيَ،
    // بَعدَ ما يَسبِقُها في البَوّابَةِ وتَحتاجُه (`set -uo pipefail` و`say`) — بِـbash لِكُلِّ شَكلِ مُدخَل،
    // ويُقارَنُ حُكمُها («مَضبوط» أَم تَنبيهٌ) بِحُكمِ `ProviderVault.FromKek` — الحَقيقَةِ الَّتي تُنَبِّهُ عَنها.
    // **وعَلى Windows لا bash مَعلومَ المَوضِع**: يُطبَعُ ذلكَ ويُرجَع، عَلى نَسَقِ الصُفوفِ الحَيَّةِ المُبَوَّبَة؛
    // وCI (‏ubuntu) يُشَغِّلُه دائِماً، وغِيابُ `/bin/bash` عَلى غَيرِ Windows يُفشِلُ بِاسمِ الأَداة (القاعِدَة ١٤).
    // (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (xxv)؛ ولا يُعادُ حَرفٌ فَوقَه: صارَ Windows يُسأَلُ عَن Git Bash — `BashPath` — فَيُشَغِّلُ الكُتلَةَ بِه إن
    // وُجِد، ويَطبَعُ «لَم يُقَس» بِالأَداةِ النّاقِصَةِ إن لَم يوجَد — `NotMeasuredHere` — فَلا يُقرَأُ «Passed» قِياساً. وكانَت الصُفوفُ تَعودُ عَلى Windows دائِماً.)

    private static readonly string ValidKek =
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(ProviderVault.KekBytes));

    /// <summary>شَكلُ المُدخَلِ بِاسمِه — والقيمَةُ تُبنى هُنا لا تُكتَب: لا مِفتاحَ ثابِتاً في المُستَودَع.</summary>
    private static string? KekOf(string shape) => shape switch
    {
        "absent"        => null,
        "empty"         => "",
        "blank"         => " \t ",
        "valid"         => ValidKek,
        "valid+newline" => ValidKek + "\n",
        "not-base64"    => "not base64 !!",
        "16-bytes"      => "AAAAAAAAAAAAAAAAAAAAAA==",          // ‏AES-128 لا يُقبَل
        "unpadded-43"   => ValidKek.TrimEnd('='),               // ‏43 مِحرَفاً بِلا حَشو
        "url-safe"      => Convert.ToBase64String(Enumerable.Repeat((byte)0xFF, ProviderVault.KekBytes).ToArray())
                               .Replace('/', '_').Replace('+', '-'),
        // (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (xxiii)): أَشكالٌ خارِجَ التِسعَةِ المَقيسَة — بَياضٌ غَيرُ ASCII (الخِزانَةُ تَقُصُّه بِـ`Trim()`
        // والبَوّابَةُ لا)، وحَشوٌ في الوَسَط، وبَياضٌ داخِلِيّ.
        "nbsp-trailing" => ValidKek + "\u00A0",
        "nbsp-only"     => "\u00A0",
        "vtab-trailing" => ValidKek + "\v",
        "ff-leading"    => "\f" + ValidKek,
        "pad-in-middle" => ValidKek[..20] + "=" + ValidKek[21..],
        "inner-space"   => ValidKek[..20] + " " + ValidKek[20..],
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "شَكلٌ لا يَعرِفُه الصَفّ."),
    };

    /// <summary>
    /// <para><b>أَينَ bash؟</b> (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (xxv)): خارِجَ Windows ‏<c>/bin/bash</c> — وغِيابُه يُفشِلُ بِاسمِ الأَداة (القاعِدَة ١٤)؛
    /// وعَلى Windows ‏Git Bash في <c>%ProgramFiles%\Git\bin\bash.exe</c> (أَو <c>%ProgramW6432%</c>) — فَتُشَغَّلُ الكُتلَةُ هُناكَ أَيضاً. و<c>null</c> حَيثُ
    /// لا يوجَد. دالَّةٌ نَقِيَّةٌ بِمُدخَلَيها (البيئَة، والمِلَفّاتُ المَوجودَة) فَتُقاسُ عَلى Linux.</para>
    /// </summary>
    internal static string? BashPath(bool isWindows, Func<string, string?> env, Func<string, bool> exists)
    {
        if (!isWindows) return exists("/bin/bash") ? "/bin/bash" : null;
        foreach (var root in new[] { env("ProgramFiles"), env("ProgramW6432") })
        {
            if (string.IsNullOrEmpty(root)) continue;
            var candidate = Path.Combine(root, "Git", "bin", "bash.exe");
            if (exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// <para><b>«لَم يُقَس» لا «نَجَح»</b> (‏2026-10-13، G4 (xxv)): عَلى Windows بِلا Git Bash يَعودُ الصَفُّ — xUnit 2 لا يُعلِّمُ صَفّاً «مُتَخَطّى» في
    /// وَقتِ التَشغيل — فَيُطبَعُ «لَم يُقَس» بِالأَداةِ النّاقِصَةِ تَحديداً كَي لا يُقرَأَ «Passed» قِياساً. وكانَت الصُفوفُ تَعودُ عَلى Windows دائِماً
    /// بِسَطرِ «يُقاسُ في CI» ولو وُجِدَ Git Bash. و<c>false</c> خارِجَ ذلك: الصَفُّ يَمضي ويُشَغِّلُ الكُتلَة.</para>
    /// </summary>
    private bool NotMeasuredHere(string label)
    {
        if (BashPath(OperatingSystem.IsWindows(), Environment.GetEnvironmentVariable, File.Exists) is not null || !OperatingSystem.IsWindows())
            return false;
        output.WriteLine($"[{label}] لَم يُقَس — Windows بِلا Git Bash (‏%ProgramFiles%\\Git\\bin\\bash.exe غائِب)؛ «Passed» هُنا لَيسَ قِياساً، والقِياسُ في CI (‏ubuntu).");
        return true;
    }

    /// <summary>يُشَغِّلُ كُتلَةَ المِفتاحِ وَحدَها بِـbash بِمُتَغَيِّرٍ مَضبوطٍ أَو مَمحُوّ — ويُرجِعُ رَمزَ خُروجِها
    /// ومُخرَجَها (الخَطَأُ مَدموجٌ فيه).</summary>
    private static (int Status, string Text) RunKekBlock(string? kek)
    {
        var bash = BashPath(OperatingSystem.IsWindows(), Environment.GetEnvironmentVariable, File.Exists);
        Assert.True(bash is not null, "أَداةٌ غائِبَة: /bin/bash — لا تُشَغَّلُ الكُتلَةُ بِدونِها.");
        var block = KekBlock(Read("scripts", "verify-production-boot.sh"));

        var dir = Directory.CreateTempSubdirectory("wasayel-kek-block-");
        try
        {
            var file = Path.Combine(dir.FullName, "kek-block.sh");
            File.WriteAllText(file, "set -uo pipefail\nexec 2>&1\nsay() { echo \"  $*\"; }\n" + block,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var psi = new ProcessStartInfo(bash!, new[] { file.Replace('\\', '/') })
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
            };
            psi.Environment.Remove(ProviderVault.KekEnvironmentVariable);
            if (kek is not null) psi.Environment[ProviderVault.KekEnvironmentVariable] = kek;

            using var p = Process.Start(psi)!;
            var text = p.StandardOutput.ReadToEnd();
            Assert.True(p.WaitForExit(10_000), "كُتلَةُ المِفتاحِ لَم تَنتَهِ في عَشرِ ثَوانٍ.");
            return (p.ExitCode, text);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("empty")]
    [InlineData("blank")]
    [InlineData("valid")]
    [InlineData("valid+newline")]
    [InlineData("not-base64")]
    [InlineData("16-bytes")]
    [InlineData("unpadded-43")]
    [InlineData("url-safe")]
    // (‏2026-10-13، G4 (xxiii)): خارِجَ التِسعَة — بَياضٌ غَيرُ ASCII، وحَشوٌ في الوَسَط، وبَياضٌ داخِلِيّ.
    [InlineData("nbsp-trailing")]
    [InlineData("nbsp-only")]
    [InlineData("vtab-trailing")]
    [InlineData("ff-leading")]
    [InlineData("pad-in-middle")]
    [InlineData("inner-space")]
    public void The_kek_block_run_by_bash_gives_the_vaults_verdict_exits_zero_and_never_prints_the_key(string shape)
    {
        if (NotMeasuredHere(shape)) return;

        var kek = KekOf(shape);
        var (status, text) = RunKekBlock(kek);
        var available = ProviderVault.FromKek(kek).IsAvailable;
        var set = text.Contains("مِفتاحُ الخِزانَة: مَضبوط", StringComparison.Ordinal);
        output.WriteLine($"[{shape}] رَمزُ الخُروج {status} · الخِزانَة: {(available ? "مُتاحَة" : "غَيرُ مُتاحَة")} · البَوّابَة: {(set ? "مَضبوط" : "تَنبيه")}");
        output.WriteLine(text);

        Assert.False(string.IsNullOrWhiteSpace(text), "أَداةٌ عَمياء: الكُتلَةُ لَم تَطبَع شَيئاً.");

        // تُنَبِّهُ ولا تُفشِل — في كُلِّ شَكل.
        Assert.Equal(0, status);

        // وحُكمُها حُكمُ الخِزانَةِ الَّتي تُنَبِّهُ عَنها: «مَضبوط» حَيثُ تَختِمُ وَحدَه، ورَمزُ الخَرقِ حَيثُ لا.
        Assert.Equal(available, set);
        if (!available) Assert.Contains(ProviderVault.Unavailable, text, StringComparison.Ordinal);

        // ولا تَطبَعُ المِفتاح.
        var key = kek?.Trim() ?? "";
        if (key.Length >= 8) Assert.DoesNotContain(key, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>قُلِبَ بِاسمِه</b> (‏2026-10-13، مُراجَعَةُ المَوجَة 3a — G4 (xxiii)): كانَ
    /// <c>Today_the_kek_block_and_the_vault_disagree_on_non_ascii_whitespace_around_a_valid_key</c> (مُودَعٌ في <c>ab6db14</c>) — «الخِزانَة: مُتاحَة ·
    /// البَوّابَة: تَنبيه». وصارَت الخِزانَةُ تَقُصُّ البَياضَ الأَربَعَةَ وَحدَها (<see cref="ProviderVault.KekWhitespaceChars"/>) والبَوّابَةُ تَشتَرِطُ أَبجَدِيَّةَ
    /// base64 بَعدَ قَصِّها — فَيَتَّفِقانِ: «لَيسَ base64» هُنا وهُناك.</para>
    /// </summary>
    [Theory]
    [InlineData("nbsp-trailing")]
    [InlineData("vtab-trailing")]
    [InlineData("ff-leading")]
    public void The_kek_block_and_the_vault_agree_that_non_ascii_whitespace_around_a_key_is_not_base64(string shape)
    {
        if (NotMeasuredHere(shape)) return;
        var kek = KekOf(shape);
        var (status, text) = RunKekBlock(kek);
        var reason = Assert.Single(ProviderVault.Validate(kek)).MessageAr;
        output.WriteLine($"[{shape}] الخِزانَة: {reason}");
        Assert.Equal(0, status);
        Assert.False(ProviderVault.FromKek(kek).IsAvailable);
        Assert.Contains("لَيسَ base64", reason, StringComparison.Ordinal);
        Assert.Contains("المَقيس: لَيسَ base64 صالِحاً)", text, StringComparison.Ordinal);
    }

    /// <summary><b>قُلِبَ بِاسمِه</b> (‏2026-10-13، G4 (xxiii)): كانَ <c>Today_non_ascii_whitespace_alone_is_absent_in_the_vault_and_not_base64_in_the_gate</c>.
    /// وصارَ بَياضٌ غَيرُ ASCII وَحدَه «لَيسَ base64» في الاثنَين — الغِيابُ «فارِغٌ بَعدَ قَصِّ البَياضِ الأَربَعَة» بِحَرفٍ واحِد.</summary>
    [Fact]
    public void Non_ascii_whitespace_alone_is_not_base64_in_both_and_ascii_whitespace_alone_is_absent_in_both()
    {
        if (NotMeasuredHere("nbsp-only")) return;
        var kek = KekOf("nbsp-only");
        var (_, text) = RunKekBlock(kek);
        var reason = Assert.Single(ProviderVault.Validate(kek)).MessageAr;
        output.WriteLine($"الخِزانَة: {reason}");
        Assert.Contains("لَيسَ base64", reason, StringComparison.Ordinal);
        Assert.Contains("المَقيس: لَيسَ base64 صالِحاً)", text, StringComparison.Ordinal);

        // والسالِب: البَياضُ الأَربَعَةُ وَحدَها غِيابٌ هُنا وهُناك.
        var (_, blank) = RunKekBlock(KekOf("blank"));
        Assert.Contains("غائِب", Assert.Single(ProviderVault.Validate(KekOf("blank"))).MessageAr, StringComparison.Ordinal);
        Assert.Contains("غائِب —", blank, StringComparison.Ordinal);
    }

    /// <summary><b>والتَعريفُ واحِد</b>: مَحارِفُ القَصِّ في الخِزانَةِ هي مَحارِفُ <c>tr -d</c> في الكُتلَةِ بِحَرفِها، والكُتلَةُ تَشتَرِطُ أَبجَدِيَّةَ base64
    /// قَبلَ <c>base64 -d</c> — فَإن تَغَيَّرَ أَحَدُهُما وَحدَه احمَرَّ هذا.</summary>
    [Fact]
    public void The_vault_defines_the_trimmed_whitespace_and_the_gate_strips_exactly_it_then_checks_the_alphabet()
    {
        Assert.Equal(" \t\r\n", ProviderVault.KekWhitespaceChars);
        var block = KekBlock(Read("scripts", "verify-production-boot.sh"));
        Assert.Equal(2, block.Split("tr -d ' \\t\\r\\n'", StringSplitOptions.None).Length - 1);
        Assert.Contains("grep -Eq '^[A-Za-z0-9+/]+={0,2}$'", block, StringComparison.Ordinal);
        Assert.True(block.IndexOf("grep -Eq '^[A-Za-z0-9+/]+={0,2}$'", StringComparison.Ordinal)
                    < block.IndexOf("| base64 -d 2>/dev/null", StringComparison.Ordinal));
        Assert.Contains("unset KEK_CLEAN", block, StringComparison.Ordinal);
    }

    // ─── ٦. وما تَقولُه سَبَبُ الخِزانَةِ نَفسُه — بَعدَ فَصلِ حالِ الفَكِّ عَن العَدّ (مُراجَعَةُ ‏2026-10-12) ───
    //
    // **المَقيسُ قَبل** (الكُتلَةُ مُشَغَّلَةً عِندَ `ddbf8c5`): «not base64 !!» ⇒ «المَقيس: 6»، و‏43 مِحرَفاً بِلا
    // حَشوٍ ⇒ «لا يُفَكُّ إلى 32 بايتاً … المَقيس: 32»، والبَياضُ وأَبجَدِيَّةُ URL ⇒ «المَقيس: 0». الحُكمُ صَحيحٌ
    // (الصَفُّ ٥) والسَبَبُ المَطبوعُ لا — لِأَنّ الإسنادَ تَحتَ `pipefail` كانَ يَأخُذُ عَدَّ `wc -c` ولَو سَقَطَ الفَكّ.
    // فَهُنا يُشتَرَطُ أَن تَقولَ الكُتلَةُ سَبَبَ الخِزانَةِ نَفسَه — رِسالَةُ `ProviderVault.Validate` ونَظيرُها:
    // غائِبٌ ↔ غائِب، «لَيسَ base64» ↔ «لَيسَ base64 صالِحاً»، «يُفَكُّ إلى N بايتاً» ↔ «المَقيس: N».
    [Theory]
    [InlineData("absent",        "غائِب",                "غائِب —")]
    [InlineData("empty",         "غائِب",                "غائِب —")]
    [InlineData("blank",         "غائِب",                "غائِب —")]
    [InlineData("valid",         null,                    "مَضبوط (يُفَكُّ إلى 32 بايتاً")]
    [InlineData("valid+newline", null,                    "مَضبوط (يُفَكُّ إلى 32 بايتاً")]
    [InlineData("not-base64",    "لَيسَ base64",          "المَقيس: لَيسَ base64 صالِحاً)")]
    [InlineData("16-bytes",      "يُفَكُّ إلى 16 بايتاً", "المَقيس: 16)")]
    [InlineData("unpadded-43",   "لَيسَ base64",          "المَقيس: لَيسَ base64 صالِحاً)")]
    [InlineData("url-safe",      "لَيسَ base64",          "المَقيس: لَيسَ base64 صالِحاً)")]
    [InlineData("nbsp-trailing", "لَيسَ base64",          "المَقيس: لَيسَ base64 صالِحاً)")]
    [InlineData("nbsp-only",     "لَيسَ base64",          "المَقيس: لَيسَ base64 صالِحاً)")]
    [InlineData("vtab-trailing", "لَيسَ base64",          "المَقيس: لَيسَ base64 صالِحاً)")]
    [InlineData("ff-leading",    "لَيسَ base64",          "المَقيس: لَيسَ base64 صالِحاً)")]
    [InlineData("pad-in-middle", "لَيسَ base64",          "المَقيس: لَيسَ base64 صالِحاً)")]
    [InlineData("inner-space",   null,                    "مَضبوط (يُفَكُّ إلى 32 بايتاً")]
    public void The_kek_block_says_the_vaults_own_reason_absent_not_base64_or_the_decoded_length(
        string shape, string? vaultReason, string blockSays)
    {
        if (NotMeasuredHere(shape)) return;

        var kek = KekOf(shape);
        var violations = ProviderVault.Validate(kek);
        var (_, text) = RunKekBlock(kek);
        output.WriteLine($"[{shape}] الخِزانَة: {(violations.Count == 0 ? "مُتاحَة" : violations[0].MessageAr)}");
        output.WriteLine(text);

        // سَبَبُ الخِزانَةِ أَوَّلاً — فَالصَفُّ يُقارِنُ بِالحَقيقَةِ لا بِتَوَقُّعٍ مَكتوبٍ وَحدَه.
        if (vaultReason is null) Assert.Empty(violations);
        else Assert.Contains(vaultReason, Assert.Single(violations).MessageAr, StringComparison.Ordinal);

        // ونَظيرُه في الكُتلَة.
        Assert.Contains(blockSays, text, StringComparison.Ordinal);

        // ولا عَدَدٌ جُزئِيٌّ لِما لا يُفَكّ.
        if (vaultReason == "لَيسَ base64")
            Assert.DoesNotMatch(new Regex(@"المَقيس: \d"), text);
    }

    /// <summary><b>أَداةُ القِياسِ مَقيسَة</b> (‏2026-10-13، G4 (xxv)): المِسبارُ بِمُدخَلاتٍ مَصنوعَة — Linux بِـ<c>/bin/bash</c> وبِدونِه، وWindows بِـGit Bash
    /// في <c>ProgramFiles</c> أَو <c>ProgramW6432</c> وبِدونِهِما.</summary>
    [Fact]
    public void BashPath_finds_bin_bash_off_windows_and_git_bash_on_windows_and_nothing_else()
    {
        Assert.Equal("/bin/bash", BashPath(false, _ => null, p => p == "/bin/bash"));
        Assert.Null(BashPath(false, _ => null, _ => false));

        var pf = Path.Combine("C:", "Program Files");
        var git = Path.Combine(pf, "Git", "bin", "bash.exe");
        Assert.Equal(git, BashPath(true, v => v == "ProgramFiles" ? pf : null, p => p == git));
        Assert.Equal(git, BashPath(true, v => v == "ProgramW6432" ? pf : null, p => p == git));
        Assert.Null(BashPath(true, v => v == "ProgramFiles" ? pf : null, _ => false));
        Assert.Null(BashPath(true, _ => null, _ => true));
        Assert.Null(BashPath(true, v => "", _ => true));
    }

    /// <summary>ولا صَفَّ في المِلَفِّ يَعودُ عَلى Windows إلّا عَبرَ المِسبار — لا سَطرَ «يُقاسُ في CI» يَقولُ «Passed» بِلا قِياس.</summary>
    [Fact]
    public void Every_bash_row_returns_on_windows_only_through_the_probe()
    {
        var self = Read("tests", "ACommerce.Platform.Tests", "ProviderVaultDeploymentTests.cs");
        var code = WriteEndpointGuardTests.StripComments(self);

        // المِلَفُّ يَمسَحُ نَفسَه — فَالإبَرُ تُبنى بِالوَصلِ كَي لا يَجِدَ الماسِحُ حُروفَه هُوَ فَيَعُدَّها.
        var oldCiLine  = "يُقاسُ هذا الصَفُّ " + "في CI";
        var oldWinLine = "Windows — " + "يُقاسُ في CI.";
        var probeCall  = "if (" + "NotMeasuredHere(";
        var runCall    = "RunKek" + "Block(";
        var printed    = "لَم يُقَس — " + "Windows بِلا Git Bash";
        static int Count(string text, string needle) => text.Split(needle, StringSplitOptions.None).Length - 1;

        Assert.DoesNotContain(oldCiLine, code, StringComparison.Ordinal);
        Assert.DoesNotContain(oldWinLine, code, StringComparison.Ordinal);
        var uses = Count(code, probeCall);
        var runs = Count(code, runCall) - 1;   // ناقِصاً التَعريف
        output.WriteLine($"صُفوفٌ تَسأَلُ المِسبار: {uses} · نِداءاتُ تَشغيلِ الكُتلَة: {runs}");
        Assert.Equal(5, uses);
        Assert.True(runs >= uses, $"أَداةٌ عَمياء: نِداءاتُ التَشغيلِ {runs} أَقَلُّ مِن الصُفوفِ {uses}.");
        Assert.Equal(1, Count(code, printed));
    }

    // ─── ٧. وما يُخفيه الغِيابُ اليَومَ — ثَلاثَةُ أَبوابٍ لا بِطاقَةٌ واحِدَة (‏2026-10-12، تَيّارُ S10، «ب» S8·١٥) ───
    //
    // **المَقيسُ قَبل**: سَطرُ ⚠ الغِيابِ في البَوّابَةِ وفِقرَةُ «ما يَقَعُ بِلا ضَبطٍ» في §٢·ح يُسَمِّيانِ بِطاقَةَ المِفتاحِ السِرِّيِّ
    // في «التَكامُلات» وَحدَها — كَما كانَ يَومَ الخُطوَة 11. ثُمَّ صارَ المِفتاحُ شَرطَ بابَينِ آخَرَين: البِطاقَةُ في إتمامِ الشِراءِ
    // بِمِفتاحِ المَتجَر (‏ADR-032 الخُطُوات 8–10: `CheckoutCardFacts.ChargerCanCharge` يَطلُبُ خِزانَةً تَفُكّ) ومَدخَلا شَحنِ البائِعِ
    // عِندَ OTO (‏ADR-033 الخُطوَتانِ 7 و8: `VendorShippingSurface.InputFor` لا يَرسُمُ شَيئاً بِلا خِزانَة). فَالمُشَغِّلُ الَّذي يَقرَأُ
    // ⚠ يَظُنُّ الغِيابَ يُخفي بِطاقَةً في الإعداداتِ — وهُوَ يُطفِئُ الدَفعَ بِالبِطاقَةِ وشَحنَ البائِعين. **والسَبَبُ يُقاسُ لا يُكتَب**:
    // الدالَّتانِ النَقِيَّتانِ تُسأَلانِ بِخِزانَةٍ مُتاحَةٍ وأُخرى لا، والنَصُّ يَتبَعُهُما في الاتِّجاهَين.

    private static readonly ProviderVault AvailableVault = ProviderVault.FromKek(ValidKek);
    private static readonly ProviderVault NoVault = ProviderVault.FromKek(null);

    /// <summary>أَتُعرَضُ البِطاقَةُ بِمِفتاحِ المَتجَرِ بِخِزانَةٍ تَفُكُّ ولا تُعرَضُ بِدونِها؟</summary>
    private static bool CheckoutCardNeedsTheVault()
    {
        static bool Offered(bool vault) => new ACommerce.Templates.Customer.Marketplace.Services.Deals.CheckoutCardFacts(
            StoreCollects: false, SealedKeyBound: true, VaultCanUnseal: vault, GatewayClientRegistered: true,
            InstanceCanCharge: false).CardOffered;
        return Offered(true) && !Offered(false);
    }

    /// <summary>أَيُرسَمُ لِلبائِعِ مَدخَلُ شَحنِه بِبيئَةِ OTO مَفتوحَةٍ وخِزانَةٍ تَختِم، ولا يُرسَمُ بِدونِها؟</summary>
    private static bool VendorShippingNeedsTheVault()
    {
        var open = new ACommerce.Kit.Delivery.OtoOptions { Environment = ACommerce.Kit.Delivery.OtoEnvironment.Staging };
        static ACommerce.Templates.Customer.Marketplace.Services.Shipping.VendorShippingInput Input(
            ACommerce.Kit.Delivery.OtoOptions o, ProviderVault v)
            => ACommerce.Templates.Customer.Marketplace.Services.Shipping.VendorShippingSurface.InputFor(o, v);
        return Input(open, AvailableVault) != ACommerce.Templates.Customer.Marketplace.Services.Shipping.VendorShippingInput.None
            && Input(open, NoVault) == ACommerce.Templates.Customer.Marketplace.Services.Shipping.VendorShippingInput.None;
    }

    /// <summary>سُطورُ ⚠ الغِيابِ والتَلَفِ في كُتلَةِ المِفتاح — اثنانِ بِالضَبط.</summary>
    private static string[] AbsentAndMalformedWarnings(string block)
    {
        var lines = block.Split('\n').Select(l => l.Trim())
            .Where(l => l.StartsWith("say \"⚠ مِفتاحُ الخِزانَةِ Providers__Kek__v1 ", StringComparison.Ordinal)
                     && l.Contains(ProviderVault.Unavailable, StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, lines.Length);
        return lines;
    }

    [Fact]
    public void VerifyProductionBoot_the_absent_kek_warning_names_every_door_the_vault_closes_measured_from_the_code()
    {
        var card = CheckoutCardNeedsTheVault();
        var vendor = VendorShippingNeedsTheVault();
        output.WriteLine($"البِطاقَةُ في إتمامِ الشِراءِ تَطلُبُ الخِزانَة: {card} · مَدخَلُ شَحنِ البائِعِ يَطلُبُها: {vendor}");

        foreach (var w in AbsentAndMalformedWarnings(KekBlock(Read("scripts", "verify-production-boot.sh"))))
        {
            Assert.Contains("«التَكامُلات»", w, StringComparison.Ordinal);
            Assert.Equal(card, w.Contains("إتمامِ الشِراءِ بِمِفتاحِ المَتجَر", StringComparison.Ordinal));
            Assert.Equal(vendor, w.Contains("مَدخَلُ شَحنِه عِندَ OTO", StringComparison.Ordinal));
            Assert.Contains("ولا يَسقُطُ الإقلاع (DEPLOY.md §٢·ح)", w, StringComparison.Ordinal);
        }

        // ومُشَغَّلَةً: الغِيابُ يَطبَعُ الأَبوابَ بِأَسمائِها (والتَلَفُ في صُفوفِ ٥ و٦ بِرَمزِه وسَبَبِه).
        if (NotMeasuredHere("absent")) return;
        var (status, text) = RunKekBlock(null);
        Assert.Equal(0, status);
        Assert.Equal(card, text.Contains("إتمامِ الشِراءِ بِمِفتاحِ المَتجَر", StringComparison.Ordinal));
        Assert.Equal(vendor, text.Contains("مَدخَلُ شَحنِه عِندَ OTO", StringComparison.Ordinal));
    }

    /// <summary><b>و§٢·ح يَقولُ الأَبوابَ نَفسَها</b> — في فِقرَةِ «ما يَقَعُ بِلا ضَبط»، بِالسَبَبِ المَقيسِ نَفسِه.</summary>
    [Fact]
    public void DeployDoc_vault_section_names_every_door_the_absence_closes()
    {
        var deploy = Read("docs", "DEPLOY.md");
        var start = deploy.IndexOf("### ما يَقَع بِلا ضَبطٍ — وهو الحالُ الآن", deploy.IndexOf("## ٢·ح.", StringComparison.Ordinal),
            StringComparison.Ordinal);
        var end = deploy.IndexOf("### يُضبَطُ مَعَ عَميلِ البَوّابَة", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "فِقرَةُ «ما يَقَعُ بِلا ضَبط» في §٢·ح غائِبَةٌ أَو تَغَيَّرَ تَرتيبُها.");
        var section = deploy[start..end];

        Assert.Equal(CheckoutCardNeedsTheVault(), section.Contains("إتمامِ الشِراءِ بِمِفتاحِ المَتجَر", StringComparison.Ordinal));
        Assert.Equal(VendorShippingNeedsTheVault(), section.Contains("مَدخَلا شَحنِ البائِعِ عِندَ OTO", StringComparison.Ordinal));
    }

    /// <summary><b>ورَأسُ البَوّابَةِ لا يَقولُ إنّها تُنَبِّهُ عَلى المِفتاحِ الصالِح</b> بَعدَ أَن صارَ المَختومُ يَقبِض — الجُملَةُ القَديمَةُ
    /// باقِيَةٌ تاريخاً، ويَليها تَصحيحُها المُؤَرَّخُ حينَ يَقبِضُ المَختومُ وَحدَه (الاتِّجاهانِ كَصَفِّ ٤).</summary>
    [Fact]
    public void VerifyProductionBoot_header_closes_the_valid_kek_warning_once_a_sealed_key_collects()
    {
        var script = Read("scripts", "verify-production-boot.sh");
        var header = script[..script.IndexOf("set -uo pipefail", StringComparison.Ordinal)];
        var sealedCollects = ProviderSealedKeyDoorsTests.SealedOnly().CollectsMoney;

        Assert.Equal(sealedCollects, header.Contains("**ولَم يَعُد يُنَبَّهُ عَلَيه**", StringComparison.Ordinal));
        Assert.Equal(sealedCollects, header.Contains("مَدخَلَ شَحنِه عِندَ OTO", StringComparison.Ordinal));
    }

    // ─── ٨. ورَفعُ السَبَبِ القَديمِ لا يَعني «لا عائِق» — §٢·ح يُسَمّي بَواقِيَ الأَبوابِ الَّتي يَفتَحُها الضَبط (‏2026-10-12، مُراجَعَةُ تَيّارِ S10) ───
    //
    // **المَقيسُ قَبل**: إلحاقُ `47986c8` في §٢·ح خَتَمَ بِـ«ولا عائِقَ في الكودِ بَعدُ دونَ ضَبطِه في الإنتاج — مَتى يُضبَطُ قَرارُك»،
    // والبابُ الَّذي يَفتَحُه الضَبطُ — البِطاقَةُ بِمِفتاحِ المَتجَر (الصَفُّ ٧: `CheckoutCardNeedsTheVault`) — تُحصي عَلَيه ‏ADR-032 §٨‑ز
    // بَواقِيَ لا يُغلِقُها الكود، وبُرهانُه الحَيُّ «ج» S9·١٠ لَم يُشَغَّل؛ وأَسقَطَت الجُملَةُ شَرطَ «بعدَ البرهانِ الحيِّ» الَّذي في رَأسِ
    // ‏ADR-032. فَصارَت مَشطوبَةً تاريخاً، وقِسمُ «قَبلَ «مَتى»» يُسَمّي البَواقِيَ وقَراراتِها. **والشَرطُ يُقاسُ لا يُكتَب**: الدالَّتانِ
    // النَقِيَّتانِ تَقولانِ أَيُّ البابَينِ يَطلُبُ الخِزانَة، وبَواقي §٨‑ز تُعَدُّ مِن ‏ADR-032 نَفسِه (وصِفرُها أَداةٌ عَمياء — القاعِدَة ١٠).
    // **والاتِّجاهُ واحِد** عَن قَصد: الوَثائِقُ تُلحَقُ ولا تُمحى، فَلا يُطلَبُ مِنها حَذفُ الإحالَةِ يَومَ يَسقُطُ الشَرط.

    /// <summary>قِسمُ §٢·ح كُلُّه — مِن عُنوانِه إلى عُنوانِ §٢·ط.</summary>
    private static string VaultSection(string deploy)
    {
        var start = deploy.IndexOf("## ٢·ح.", StringComparison.Ordinal);
        var end = deploy.IndexOf("## ٢·ط.", start + 1, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "§٢·ح أَو §٢·ط غائِبٌ أَو تَغَيَّرَ تَرتيبُهُما.");
        return deploy[start..end];
    }

    /// <summary>بَواقي ‏ADR-032 §٨‑ز بِعَناوينِها — البُنودُ المُرَقَّمَةُ تَحتَ «وما لا يُغلِقُه» حَتّى «البرهان».</summary>
    private static string[] Adr032SectionZResiduals()
    {
        var file = Directory.GetFiles(Path.Combine(RepoRoot, "docs"), "ADR-032-*.md");
        Assert.Single(file);
        var adr = Read("docs", Path.GetFileName(file[0]));

        var eight = adr.IndexOf("## ٨. إلحاقُ", StringComparison.Ordinal);
        var z = adr.IndexOf("### ز. إلحاقُ", Math.Max(eight, 0), StringComparison.Ordinal);
        Assert.True(eight > 0 && z > eight, "‏ADR-032 §٨‑ز غائِبٌ أَو تَغَيَّرَ عُنوانُه.");
        var open = adr.IndexOf("- **وما لا يُغلِقُه — ويُقالُ ولا يُبتلَع**:", z, StringComparison.Ordinal);
        var proof = open < 0 ? -1 : adr.IndexOf("- **البرهان**:", open, StringComparison.Ordinal);
        Assert.True(open > z && proof > open, "قائِمَةُ «وما لا يُغلِقُه» في ‏ADR-032 §٨‑ز غائِبَةٌ أَو تَغَيَّرَ تَرتيبُها.");

        return Regex.Matches(adr[open..proof], @"^[ \t]+\d+\.[ \t]+\*\*([^*\n]+)\*\*", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value.Trim())
            .ToArray();
    }

    [Fact]
    public void DeployDoc_vault_section_names_the_open_residuals_of_the_doors_the_kek_opens_and_says_no_obstacle_only_struck()
    {
        var card = CheckoutCardNeedsTheVault();
        var vendor = VendorShippingNeedsTheVault();
        var residuals = Adr032SectionZResiduals();
        output.WriteLine($"البِطاقَةُ في إتمامِ الشِراءِ تَطلُبُ الخِزانَة: {card} · مَدخَلُ شَحنِ البائِعِ يَطلُبُها: {vendor}");
        output.WriteLine($"بَواقي ‏ADR-032 §٨‑ز: {residuals.Length} — {string.Join(" · ", residuals)}");
        Assert.True(residuals.Length > 0, "أَداةٌ عَمياء: صِفرُ بَواقٍ في ‏ADR-032 §٨‑ز.");

        var section = VaultSection(Read("docs", "DEPLOY.md"));
        var unstruck = Regex.Replace(section, "~~.*?~~", string.Empty, RegexOptions.Singleline);

        // «لا عائِق» باقِيَةٌ تاريخاً — مَشطوبَةً لا غَير.
        // ‏(‏2026-10-24) والجُملَةُ تُقارَنُ بِلا تَشكيلٍ ولا فَرقِ مَسافاتٍ عَلى الطَرَفَين — «ولا عائق في» بِلا حَرَكاتٍ أَو مَقسومَةً عَلى سَطرَين هي هي.
        const string noObstacle = "ولا عائِقَ في";
        var all = NoObstacleCount(section, noObstacle);
        output.WriteLine($"«{noObstacle}» في §٢·ح (بِلا تَشكيل): {all} · بِلا شَطب: {NoObstacleCount(unstruck, noObstacle)}");
        Assert.True(all > 0, "أَداةٌ عَمياء: الجُملَةُ المَشطوبَةُ نَفسُها غائِبَة — والوَثائِقُ تُلحَقُ ولا تُمحى.");
        Assert.Equal(0, NoObstacleCount(unstruck, noObstacle));

        // ‏(‏2026-10-24) و«1–N» الَّتي يُحيلُ بِها §٢·ح إلى بَواقي §٨‑ز تُساوي ما يُعَدُّ مِنها — بَندٌ ثامِنٌ يُلحَقُ بِـADR-032 لا يَبقى خارِجَ الإحالَة.
        var ranges = ResidualRanges(unstruck);
        output.WriteLine($"«1–N» في §٢·ح: [{string.Join(", ", ranges)}] · المَعدودُ في ‏ADR-032 §٨‑ز: {residuals.Length}");
        Assert.True(ranges.Count > 0, "أَداةٌ عَمياء: §٢·ح لا يُحيلُ إلى «وما لا يُغلِقُه» 1–N.");
        Assert.All(ranges, n => Assert.Equal(residuals.Length, n));

        if (card)
            foreach (var cite in new[] { "ADR-032 §٨‑ز", "«ب» S9·١٤", "«ب» S9·١٦", "«ج» S9·١٠" })
                Assert.True(unstruck.Contains(cite, StringComparison.Ordinal),
                    $"البِطاقَةُ تَطلُبُ الخِزانَةَ و§٢·ح لا يُسَمّي {cite} — فَـ«مَتى» تُقالُ بِلا بَواقيها.");
        if (vendor)
            foreach (var cite in new[] { "ADR-033 §٧‑ف", "«ب» S8·٢١", "«ج» S8·١١", "Shipping__Oto__Environment" })
                Assert.True(unstruck.Contains(cite, StringComparison.Ordinal),
                    $"مَدخَلُ البائِعِ يَطلُبُ الخِزانَةَ و§٢·ح لا يُسَمّي {cite}.");
    }

    /// <summary>النَصُّ بِلا تَشكيلٍ (‏U+064B–U+0652) وبِمَسافاتٍ مَطوِيَّة — يُطَبَّقُ عَلى الطَرَفَينِ قَبلَ المُقارَنَة.</summary>
    internal static string Bare(string text) => Regex.Replace(Regex.Replace(text, "[\u064B-\u0652]", ""), @"\s+", " ");

    /// <summary>كَم مَرَّةً تَرِدُ <paramref name="phrase"/> في <paramref name="text"/> بَعدَ <see cref="Bare"/> عَلى الطَرَفَين.</summary>
    internal static int NoObstacleCount(string text, string phrase) => Regex.Matches(Bare(text), Regex.Escape(Bare(phrase))).Count;

    /// <summary>كُلُّ N في «‏ADR-032 §٨‑ز «وما لا يُغلِقُه» 1–N» — بِشَرطَةٍ قَصيرَةٍ أَو عادِيَّة، وبَعدَ <see cref="Bare"/>.</summary>
    internal static IReadOnlyList<int> ResidualRanges(string text)
        => Regex.Matches(Bare(text), Bare("ADR-032 §٨‑ز «وما لا يُغلِقُه» ") + @"1[–-](?<n>\d+)")
            .Select(m => int.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

    /// <summary><b>حَقنُ عَيب — فاحِصا §٨ يُحمِرّانِ فِعلاً</b> (‏2026-10-24 — مُراجَعَةُ الموجَة ٤، بَندُ I): «لا عائِق» بِلا تَشكيلٍ أَو مَقسومَةً
    /// عَلى سَطرَينِ تُعَدّ (وكانَت المُقارَنَةُ الحَرفِيَّةُ تَعمى عَنها)، ومَشطوبَتُها لا تُعَدّ؛ و«1–N» تُقرَأُ بِشَرطَتَيها، وعَدٌّ لا يُساوي
    /// البَواقي يُرى.</summary>
    [Fact]
    public void The_section_eight_checkers_can_actually_go_red()
    {
        const string Phrase = "ولا عائِقَ في";
        var counted = new (string Text, int Expected, bool OldSaw)[]
        {
            ("~~**ولا عائِقَ في\nالكودِ بَعدُ**~~", 0, false),
            ("**ولا عائِقَ في الكودِ بَعدُ**", 1, true),
            ("ولا عائق في الكود بعد", 1, false),
            ("ولا عائِقَ\nفي الكودِ بَعدُ", 1, false),
            ("ولا عَائِقَ فِي الكُودِ", 1, false),
        };
        var wrong = new List<string>();
        foreach (var (text, expected, oldSaw) in counted)
        {
            var unstruck = Regex.Replace(text, "~~.*?~~", string.Empty, RegexOptions.Singleline);
            if (NoObstacleCount(unstruck, Phrase) != expected) wrong.Add($"«{text}»: {NoObstacleCount(unstruck, Phrase)} لا {expected}");
            if (unstruck.Contains(Phrase, StringComparison.Ordinal) != oldSaw) wrong.Add($"«{text}»: قِياسُ المُقارَنَةِ الحَرفِيَّةِ تَغَيَّر");
        }
        var blindOld = counted.Count(c => c.Expected > 0 && !c.OldSaw);
        output.WriteLine($"«لا عائِق»: حُقِنَ {counted.Length} — والمُقارَنَةُ الحَرفِيَّةُ تَعمى عَن {blindOld}");
        Assert.Equal(3, blindOld);

        Assert.Equal(new[] { 7 }, ResidualRanges("**\u200fADR-032 §٨‑ز «وما لا يُغلِقُه» 1–7** (وفيها …)"));
        Assert.Equal(new[] { 6 }, ResidualRanges("ADR-032 §٨‑ز «وما لا يُغلِقُه» 1-6"));
        Assert.Equal(new[] { 8 }, ResidualRanges("ADR-032 §٨‑ز «وما لا يغلقه» 1–8"));
        Assert.Empty(ResidualRanges("ADR-032 §٨‑ز «وما لا يُغلِقُه» كُلُّها"));
        Assert.Empty(ResidualRanges("ADR-032 §٨‑هـ «وما لا يُغلِقُه» 1–7"));

        Assert.True(wrong.Count == 0, $"حُقِنَ {counted.Length} نَصّاً، وأَخطَأَ العَدُّ في {wrong.Count}:\n  " + string.Join("\n  ", wrong));
    }
}
