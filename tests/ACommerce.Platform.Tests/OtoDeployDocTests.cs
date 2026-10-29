using ACommerce.Kit.Delivery;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Shipping;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>دَليلُ النَشرِ يَحمِلُ ما يَقرَؤُه الكودُ حَرفاً</b> (‏ADR-033 الخُطوَتانِ 3 و10؛ `docs/DEPLOY.md` §٢·ط): اسما
/// المُتَغَيِّرَينِ كَما يُتَرجِمُهُما .NET، وعُنوانا البيئَتَين، ومَسارُ الوارِدِ الَّذي يُسَجَّلُ عِندَ OTO. نَفسُ حُجَّةِ
/// <see cref="PaddleRouteTests"/>: اسمٌ مَنسوخٌ بِخَطَإِ حَرفٍ يَعني رَمزاً لا يُقرَأ — فَلا استِمارَةَ ولا سَبَبَ يُرى —
/// وعُنواناً يُسَجَّلُ عِندَ OTO فَتَذهَبُ إشعاراتُه إلى ‏404 بِصَمت.</para>
/// </summary>
public class OtoDeployDocTests
{
    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative));

    private static string Section()
    {
        var doc = Read("docs/DEPLOY.md");
        var start = doc.IndexOf("## ٢·ط. الشَحنُ عَبرَ OTO", StringComparison.Ordinal);
        var end = doc.IndexOf("\n## ", start + 10, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "فِقرَةُ الشَحنِ غائِبَةٌ عَن دَليلِ النَشر — الأَداةُ عَمياء.");
        return doc[start..end];
    }

    [Fact]
    public void The_deploy_section_names_both_variables_as_dotnet_reads_them()
    {
        var section = Section();
        Assert.Equal("Shipping__Oto__Environment", OtoEnvironment.EnvVarName(OtoEnvironment.EnvironmentKey));
        Assert.Equal("Shipping__Oto__RefreshToken", OtoEnvironment.EnvVarName(OtoEnvironment.RefreshTokenKey));
        Assert.Contains($"`{OtoEnvironment.EnvVarName(OtoEnvironment.EnvironmentKey)}`", section, StringComparison.Ordinal);
        Assert.Contains($"`{OtoEnvironment.EnvVarName(OtoEnvironment.RefreshTokenKey)}`", section, StringComparison.Ordinal);
        Assert.Contains($"`{OtoEnvironment.Staging}`", section, StringComparison.Ordinal);
        Assert.Contains(OtoEnvironment.StagingBaseUrl, section, StringComparison.Ordinal);
        Assert.Contains(OtoEnvironment.ProductionBaseUrl, section, StringComparison.Ordinal);
        // والمَفتوحُ اليَومَ هُوَ ما تَقولُه الفِقرَة: staging وَحدَها.
        Assert.True(OtoEnvironment.IsOpen(OtoEnvironment.Staging));
        Assert.False(OtoEnvironment.IsOpen(OtoEnvironment.Production));
    }

    [Fact]
    public void The_deploy_section_carries_the_inbound_path_that_resolves_to_its_store()
    {
        var section = Section();
        var demoUrlPath = OtoShippingEndpoints.OrderStatusPath.Replace("{slug}", DealShipping.DemoStore, StringComparison.Ordinal);
        Assert.Contains($"`{OtoShippingEndpoints.OrderStatusPath}`", section, StringComparison.Ordinal);
        Assert.Contains(demoUrlPath, section, StringComparison.Ordinal);
        // تَحتَ مَتجَرِه لا تَحتَ مَقطَعٍ مَحجوز: الوَسيطُ يَحُلُّ المُستَأجِرَ مِن المَسارِ نَفسِه.
        Assert.Equal(DealShipping.DemoStore, TenantResolverMiddleware.SlugFromPath(demoUrlPath));
        Assert.Contains("\"webhookType\": \"orderStatus\"", section, StringComparison.Ordinal);
    }

    /// <summary>وما يَقَعُ بِلا ضَبطٍ مَكتوبٌ بِرُموزِه كَما يُطَبِّقُها الكود: الاستِمارَةُ لا تُرسَم، والنُقطَةُ تَرُدُّ
    /// <c>carrier_unavailable</c>، والوارِدُ ‏404 — والرُموزُ مِن ثَوابِتِها لا مَنسوخَة.</summary>
    [Fact]
    public void The_deploy_section_says_absence_hides_with_the_codes_the_code_returns()
    {
        var section = Section();
        Assert.Contains(DealShipAuthorization.CarrierUnavailable, section, StringComparison.Ordinal);
        Assert.Equal(404, OtoOrderStatusGate.StatusFor(OtoOrderStatusGate.CarrierUnavailable));
        Assert.Equal(400, OtoOrderStatusGate.StatusFor(OtoOrderStatusGate.NoticeUnreadable));
        Assert.Contains("‏404", section, StringComparison.Ordinal);
        Assert.Contains("‏`400`", section, StringComparison.Ordinal);
        Assert.Contains("لا تُرسَمُ", section, StringComparison.Ordinal);
        Assert.Contains("ولا «قَريباً»", section, StringComparison.Ordinal);
        Assert.Contains("DealShipAuthorization.IsAllowed", section, StringComparison.Ordinal);
    }

    /// <summary>والمُضيفُ يُسَجِّلُ العَميلَ مِن التَهيئَةِ نَفسِها، ويُحيلُ إلى هذِه الفِقرَة.</summary>
    [Fact]
    public void The_host_registers_the_client_from_configuration_and_points_at_the_section()
    {
        var program = Read("apps/V1.App/Program.cs");
        Assert.Contains("builder.Services.AddOtoShipping(builder.Configuration);", program, StringComparison.Ordinal);
        Assert.Contains("`docs/DEPLOY.md` §٢·ط", program, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>الرَمزُ السوقِيُّ في الدَليلِ بِاسمِه كَما يَقرَؤُه .NET، وأَثَرُ غِيابِه بِرَمزَي النُقطَتَين</b> (‏ADR-033 الخُطوَة 10، تَيّارُ S8،
    /// ‏2026-10-12): المُتَغَيِّرُ <c>Shipping__Oto__MarketplaceToken</c> سِرّاً، ومَسارا المَدخَلَينِ حَرفاً، و«غِيابُه يُخفي التَسجيلَ ويُبقي
    /// اللَصق» بِـ<c>vendor_register_closed</c> و<c>vendor_paste_closed</c> كَما تُعيدُهُما الخِدمَة، والنَموذَجُ — حِسابُ البائِعِ ومَحفَظَتُه
    /// وعَقدُه — في فِقرَتِه. والقيَمُ مِن الكودِ لا مَنسوخَة: اسمٌ يَفتَرِقُ يَحمَرُّ هُنا.</para>
    /// </summary>
    [Fact]
    public void The_deploy_section_names_the_marketplace_token_and_says_its_absence_hides_register_and_keeps_paste()
    {
        var section = Section();
        Assert.Equal("Shipping__Oto__MarketplaceToken", OtoEnvironment.EnvVarName(OtoEnvironment.MarketplaceTokenKey));
        Assert.Contains($"| `{OtoEnvironment.EnvVarName(OtoEnvironment.MarketplaceTokenKey)}` |", section, StringComparison.Ordinal);
        Assert.Contains("| **Secret** |", section[section.IndexOf("Shipping__Oto__MarketplaceToken", StringComparison.Ordinal)..],
            StringComparison.Ordinal);
        Assert.Contains("`POST /{slug}/me/shipping/oto`", section, StringComparison.Ordinal);
        Assert.Contains("`POST /{slug}/me/shipping/oto/register`", section, StringComparison.Ordinal);
        Assert.Contains("غِيابُه يُخفي مَسارَ التَسجيلِ ويُبقي اللَصق", section, StringComparison.Ordinal);
        Assert.Contains($"`{ACommerce.Templates.Customer.Marketplace.Services.Shipping.VendorShippingService.RegisterClosed}`", section, StringComparison.Ordinal);
        Assert.Contains($"`{ACommerce.Templates.Customer.Marketplace.Services.Shipping.VendorShippingService.PasteClosed}`", section, StringComparison.Ordinal);
        Assert.Contains("الشَحنُ حِسابُ البائِعِ نَفسِه عِندَ OTO", section, StringComparison.Ordinal);
        foreach (var word in new[] { "مَحفَظَتِه", "عَقدُ النَقلِ", "لا تَمُرُّ بِمالِه" })
            Assert.Contains(word, section, StringComparison.Ordinal);
        Assert.Contains($"`{OtoOrderStatusGate.NoticeUnverifiable}`", section, StringComparison.Ordinal);
        Assert.Contains("‏`401`", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>والدَليلُ يَقولُ النافِذَةَ كَما يُطَبِّقُها الكود</b> (‏2026-10-12، مُراجَعَةُ تَيّارِ S8، الجَولَة 1؛ ‏ADR-033 §٧‑ع): لا يُرَدُّ إشعارٌ عَلى عُمرِه،
    /// و<c>signature_stale</c> لِخَتمٍ يَسبِقُ الساعَةَ بِأَكثَرَ مِن خَمسِ ثَوانٍ وَحدَه — والرَمزُ مِن ثابِتِه، والخَمسُ مِن <c>ToleranceSeconds</c>، وكَلِمَةُ اللوغِ
    /// كَما تَكتُبُها الخِدمَة. دَليلٌ يَعودُ إلى «خِلالَ خَمسِ ثَوانٍ» بِلا إلحاقِه، أَو كودٌ يَعودُ إلى الطَرَفَين، يَحمَرُّ أَحَدُهُما هُنا أَو في <c>OtoClientTests</c>.</para>
    /// </summary>
    [Fact]
    public void The_deploy_section_says_the_window_refuses_only_a_stamp_ahead_of_our_clock()
    {
        var section = Section();
        Assert.Equal(5, OtoWebhookSignature.ToleranceSeconds);
        Assert.False(OtoWebhookSignature.IsAhead(600_000));
        Assert.True(OtoWebhookSignature.IsAhead(-5001));
        Assert.Contains($"`{OtoOrderStatusGate.SignatureStale}`", section, StringComparison.Ordinal);
        Assert.Contains("لَم يَعُد شَرطاً عَلى عُمرِ الإشعار", section, StringComparison.Ordinal);
        Assert.Contains("إلّا خَتمٌ يَسبِقُ ساعَتَنا بِأَكثَرَ مِن خَمسِ ثَوانٍ", section, StringComparison.Ordinal);
        Assert.Contains("`← verified (age N ms)`", section, StringComparison.Ordinal);
        var service = File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
            "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Deals", "DealShipping.cs"));
        Assert.Contains("← verified (age {Age} ms)", service, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>والدَليلُ يَقولُ ما يُرسَلُ في التَسجيلِ كَما يُرسِلُه الكود</b> (‏2026-10-12، مُراجَعَةُ تَيّارِ S8، الجَولَة 2؛ ‏ADR-033 §٧‑ف): جِهَةُ التَواصُلِ
    /// المُتَحَقَّقُ مِنها مُقفَلَة، ومَن لا جِهَةَ لَه يُرَدُّ بِرَمزِه، والمُحاوَلاتُ مَحدودَة — والرَمزانِ مِن ثابِتَيهِما، فَرَمزٌ يُعادُ تَسمِيَتُه بِلا الدَليلِ
    /// يَحمَرُّ هُنا.</para>
    /// </summary>
    [Fact]
    public void The_deploy_section_says_register_sends_the_verified_contact_and_bounds_attempts()
    {
        var section = Section();
        Assert.Contains($"`{ACommerce.Templates.Customer.Marketplace.Services.Shipping.VendorContactGate.ContactUnverified}`", section, StringComparison.Ordinal);
        Assert.Contains($"`{ACommerce.Templates.Customer.Marketplace.Services.Shipping.VendorShippingService.RegisterRateLimited}`", section, StringComparison.Ordinal);
        Assert.Contains("«عَن نَفسِه» صارَ يُلزَم", section, StringComparison.Ordinal);
        Assert.Contains("مَرسوماً في النَموذَجِ مُقفَلاً", section, StringComparison.Ordinal);
        Assert.Contains("عَشرٌ في السّاعَةِ", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>والدَليلُ يَقولُ إنّ الـSpace هُوَ الإنتاجُ وإنّ <c>staging</c> فيه يَفتَحُ قِسمَ الشَحنِ لِكُلِّ مَتجَر</b> (‏2026-10-25، تَدقيقُ الإطلاق brief-P1b-1):
    /// الحُجَّةُ مِن الكودِ لا مِن الوَثيقَة — <c>VendorShippingSurface.InputFor</c> بِلا مُعامِلِ مَتجَر، فَإن صارَ لَه يَوماً (قَصرُ <c>staging</c> عَلى
    /// <c>theme-demo</c>) يَحمَرُّ هذا الصَفُّ حَتّى تُصَحَّحَ الفِقرَة؛ ورَمزُ التَهيئَةِ الاحتِياطيُّ وَحدَه مَقصورٌ عَلى <see cref="DealShipping.DemoStore"/>.</para>
    /// </summary>
    [Fact]
    public void The_deploy_section_says_staging_on_the_space_opens_every_store_and_the_proof_goes_through_a_tunnel()
    {
        var input = typeof(ACommerce.Templates.Customer.Marketplace.Services.Shipping.VendorShippingSurface).GetMethod("InputFor");
        Assert.NotNull(input);
        Assert.DoesNotContain(input!.GetParameters(), p => p.ParameterType == typeof(string));
        var staging = new OtoOptions { Environment = OtoEnvironment.Staging, RefreshToken = "t" };
        Assert.Equal("t", DealShipping.VendorTokenFor(staging, DealShipping.DemoStore));
        Assert.Null(DealShipping.VendorTokenFor(staging, "any-other-store"));

        var section = Section();
        var at = section.IndexOf("brief-P1b-1", StringComparison.Ordinal);
        Assert.True(at > 0, "تَصحيحُ brief-P1b-1 غائِبٌ عَن §٢·ط.");
        var fix = section[at..];
        foreach (var phrase in new[] { "الـSpace هُوَ الإنتاج", "لِكُلِّ بائِعٍ في كُلِّ مَتجَر", "بِلا مُعامِلِ مَتجَر", "نَفَقٍ", "فَرعِ Neon مَنسوخ",
                     "ولا يُضبَطُ `staging` في\nالـSpace", "«ج» S8·١١", "«ب» W8-6 · ٦", "`oto_environment`" })
            Assert.Contains(phrase, fix, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>والدَليلُ يَقولُ إنّ <c>staging</c> خارِجَ التَطويرِ يُرفَضُ إقلاعُه وإنّ التَجرِبَةَ في وَضعِ التَطوير</b> (قَرارُ المالِك د44 «ج»، ‏2026-10-29):
    /// الرَمزُ بِحَرفِه مِن <see cref="OtoBootPolicy"/> لا مَنسوخاً، والجُملَتانِ اللَّتانِ نَقَضَهُما القَرارُ مُسَمّاتانِ في الإلحاق.</para>
    /// </summary>
    [Fact]
    public void The_deploy_section_says_staging_is_refused_outside_development_and_the_proof_runs_in_development()
    {
        var section = Section();
        var at = section.IndexOf("قَرارُ المالِك د44", StringComparison.Ordinal);
        Assert.True(at > 0, "إلحاقُ د44 غائِبٌ عَن §٢·ط.");
        var fix = section[at..];
        foreach (var phrase in new[] { $"`{OtoBootPolicy.StagingOutsideDevelopment}`", "`OtoBootPolicy.AssertSafeOutsideDevelopment`", "‏**3**",
                     "ASPNETCORE_ENVIRONMENT=Development", "فَرعِ Neon\nمَنسوخ", "ولا يُضبَطُ `staging` في الـSpace", "ولا يَتَغَيَّرُ رَمزُ خُروجِها", "`exit=3`", "`exit=0`" })
            Assert.Contains(phrase, fix, StringComparison.Ordinal);
    }

    private const string OtoGateStart = "# ─── بِدايَةُ oto_environment ───";
    private const string OtoGateEnd = "# ─── نِهايَةُ oto_environment ───";

    /// <summary>يُشَغِّلُ دالَّةَ bash بِسِجِلٍّ وبِقيمَةِ مُتَغَيِّرٍ في بيئَتِها، ويُرجِعُ ما طَبَعَته — ورَمزُ خُروجِها <paramref name="exit"/>
    /// (‏0 افتِراضاً؛ و3 لِكُتلَةٍ تَرفُضُ <c>staging</c> مُنذُ قَرارِ المالِك د44).</summary>
    private static string RunBash(string fn, string call, string? log, string? value, int exit = 0)
    {
        Assert.True(File.Exists("/bin/bash"), "أَداةٌ غائِبَة: /bin/bash.");
        var dir = Directory.CreateTempSubdirectory("wasayel-oto-gate-");
        try
        {
            var logPath = Path.Combine(dir.FullName, "app.log");
            File.WriteAllText(logPath, log ?? "", new System.Text.UTF8Encoding(false));
            var script = Path.Combine(dir.FullName, "run.sh");
            File.WriteAllText(script, "set -uo pipefail\n" + fn + "\n" + call + "\n", new System.Text.UTF8Encoding(false));
            var psi = new System.Diagnostics.ProcessStartInfo("/bin/bash", new[] { script, logPath })
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            };
            psi.Environment.Remove("Shipping__Oto__Environment");
            if (value is not null) psi.Environment["Shipping__Oto__Environment"] = value;
            using var p = System.Diagnostics.Process.Start(psi)!;
            var text = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            Assert.True(p.WaitForExit(10_000));
            Assert.Equal(exit, p.ExitCode);
            return text.Trim();
        }
        finally { dir.Delete(recursive: true); }
    }

    /// <summary>الكُتلَةُ بِحَرفِها مِن البَوّابَة.</summary>
    private static string OtoGateFunction()
    {
        var gate = Read("scripts/verify-production-boot.sh");
        var a = gate.IndexOf(OtoGateStart, StringComparison.Ordinal);
        Assert.True(a > 0, "كُتلَةُ oto_environment غائِبَة.");
        Assert.Equal(a, gate.LastIndexOf(OtoGateStart, StringComparison.Ordinal));
        var b = gate.IndexOf(OtoGateEnd, a, StringComparison.Ordinal);
        Assert.True(b > a);
        return gate[a..(b + OtoGateEnd.Length)];
    }

    /// <summary>سَطرُ الإقلاعِ كَما يَكتُبُه التَطبيقُ لِقيمَةِ المُتَغَيِّر — مِن <see cref="OtoEnvironment.BootLogValue"/> نَفسِها، لا مَنسوخاً.</summary>
    private static string AppLine(string? value)
        => "info: Program[0]\n      " + OtoEnvironment.BootLogPrefix + OtoEnvironment.BootLogValue(value) + "\n";

    /// <summary>الكُتلَةُ عَلى سِجِلٍّ فيه سَطرُ التَطبيقِ لِهذِه القيمَة — والقيمَةُ في البيئَةِ أَيضاً، والكُتلَةُ لا تَقرَؤُها.</summary>
    private static string RunOtoGateBlock(string? value)
        => RunBash(OtoGateFunction(), "oto_environment \"$1\"", "[06:48:20 INF] Now listening on: http://127.0.0.1:5171\n" + AppLine(value), value,
                   OtoEnvironment.IsOpen(value) ? 3 : 0);

    /// <summary>أَتُعَلِّمُ الكُتلَةُ <c>staging</c>؟ — سَطرُ ⚠ القَديم (قَبلَ د44) أَو سَطرُ الرَفضِ ✗ (بَعدَه): الحُكمُ واحِدٌ عَلى الشَكلَين، فَتُقاسُ
    /// بِه الكُتلَةُ الحَيَّةُ والعَيِّنَةُ المَحقونَةُ مَعاً.</summary>
    private static bool FlagsStaging(string text)
        => text.StartsWith("⚠ Shipping__Oto__Environment=staging", StringComparison.Ordinal)
        || text.StartsWith("✗ Shipping__Oto__Environment=staging", StringComparison.Ordinal);

    /// <summary>
    /// <para>انقَلَبَ بِاسمِه (كانَ <c>The_boot_gate_warns_on_staging_exactly_where_the_code_opens_it</c>؛ قَرارُ المالِك د44 «ج»، ‏2026-10-29): <b>بَوّابَةُ الإقلاعِ
    /// تَرفُضُ <c>staging</c> بِرَمزِ 3</b> بَدَلَ سَطرِ ⚠ ورَمزِ ‏0 — وحُكمُ الكُتلَةِ (رَفضٌ أَم لا) يُساوي <see cref="OtoEnvironment.IsOpen"/> شَكلاً شَكلاً كَما كان: بِقَصٍّ
    /// وأَحرُفٍ كَبيرَة، والإنتاجُ، والفَراغ، وأَشكالُ توصيفِ <c>c1f2e29</c>. والكُتلَةُ تُنادى مَرَّةً بَعدَ الإقلاعِ عَلى سِجِلِّه، ورَمزُها يُحفَظُ ويُقرَأُ بَعدَ حُكمِ
    /// المَسارات (فَلا يَحجُبُ كَسراً في مَسار) وقَبلَ «✅ عَبَرَت».</para>
    /// </summary>
    [Theory]
    [InlineData("staging")]
    [InlineData(" Staging ")]
    [InlineData("STAGING")]
    [InlineData("production")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("\vstaging")]
    [InlineData(" staging ")]
    [InlineData("sta ging")]
    public void The_boot_gate_refuses_staging_exactly_where_the_code_opens_it(string? value)
    {
        if (OperatingSystem.IsWindows()) return;
        var text = RunOtoGateBlock(value);
        var refuses = text.StartsWith("✗ Shipping__Oto__Environment=staging", StringComparison.Ordinal);
        Assert.Equal(OtoEnvironment.IsOpen(value), refuses);
        Assert.Equal(OtoEnvironment.IsOpen(value), FlagsStaging(text));
        Assert.DoesNotContain("⚠ Shipping__Oto__Environment=staging", text, StringComparison.Ordinal);
        if (refuses)
        {
            Assert.Contains("د44", text, StringComparison.Ordinal);
            Assert.Contains("DEPLOY.md §٢·ط", text, StringComparison.Ordinal);
        }
        else
            Assert.StartsWith("الشَحنُ عَبرَ OTO: مُغلَق", text, StringComparison.Ordinal);
        var gate = Read("scripts/verify-production-boot.sh");
        Assert.Equal(1, gate.Split("\noto_environment \"$APP_LOG\" || OTO_VERDICT=$?\n").Length - 1);
        Assert.Equal(0, gate.Split("\noto_environment \"$APP_LOG\"\n").Length - 1);
        Assert.Equal(0, gate.Split("\noto_environment\n").Length - 1);
        var call = gate.IndexOf("\noto_environment \"$APP_LOG\" || OTO_VERDICT=$?\n", StringComparison.Ordinal);
        var routes = gate.IndexOf("if [ \"$ROUTE_FAILED\" -ne 0 ]; then", StringComparison.Ordinal);
        var verdict = gate.IndexOf("if [ \"$OTO_VERDICT\" -ne 0 ]; then\n", StringComparison.Ordinal);
        var passed = gate.IndexOf("echo \"✅ عَبَرَت", StringComparison.Ordinal);
        Assert.True(call > 0 && call < routes, "الكُتلَةُ تُنادى بَعدَ الحُكم — لا تُطبَعُ حينَ يَسقُطُ مَسار.");
        Assert.True(routes < verdict && verdict < passed, "رَمزُ الكُتلَةِ لا يُقرَأُ بَينَ حُكمِ المَساراتِ و«عَبَرَت».");
        Assert.Contains("    exit \"$OTO_VERDICT\"\n", gate[verdict..passed], StringComparison.Ordinal);
    }

    /// <summary>وسَطرُ الإقلاعِ مِن الكود: <c>Program.cs</c> يَكتُبُه بِالقَرارِ عَلى <c>OtoOptions.Environment</c> كَما يَقرَؤُه العَميل، والكُتلَةُ لا تَقرَأُ المُتَغَيِّر.</summary>
    [Fact]
    public void The_app_writes_the_oto_decision_on_boot_and_the_gate_block_does_not_read_the_variable()
    {
        var program = Read("apps/V1.App/Program.cs").Replace("\r\n", "\n");
        Assert.Contains("OtoEnvironment.BootLogPrefix + \"{Oto}\"", program, StringComparison.Ordinal);
        Assert.Contains("OtoEnvironment.BootLogValue(\n        app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<OtoOptions>>().Value.Environment)",
            program, StringComparison.Ordinal);
        Assert.True(program.IndexOf("OtoEnvironment.BootLogPrefix", StringComparison.Ordinal) > program.IndexOf("var app = builder.Build();", StringComparison.Ordinal));
        Assert.DoesNotContain("Shipping__Oto__Environment:-", OtoGateFunction(), StringComparison.Ordinal);
        foreach (var shape in new[] { "staging", " Staging ", "\vstaging", " staging ", "production", "", "sta ging" })
            Assert.Equal(OtoEnvironment.IsOpen(shape) ? OtoEnvironment.OpenBootValue : OtoEnvironment.ClosedBootValue, OtoEnvironment.BootLogValue(shape));
    }

    /// <summary>
    /// <para>انقَلَبَ بِاسمِه (كانَ <c>Today_a_boot_outside_development_with_oto_staging_is_not_refused</c>، <c>73506af</c>؛ قَرارُ المالِك د44 «ج»): إقلاعٌ خارِجَ
    /// التَطويرِ بِـ<c>Shipping__Oto__Environment=staging</c> <b>يُرفَضُ قَبلَ البَذرِ وأَوَّلِ طَلَب</b> — <c>AddOtoShipping</c> ما زالَ لا يَرمي لِلقيمَة (لا يَعرِفُ
    /// البيئَة)، والرَفضُ في <c>Program.cs</c>: <see cref="OtoBootPolicy.AssertSafeOutsideDevelopment"/> يَقرَأُ <c>IOptions&lt;OtoOptions&gt;</c> بَينَ بِناءِ المُضيفِ
    /// وسَطرِ الإقلاع، بَعدَ حارِسِ المَلَفّاتِ وقَبلَ <c>PlatformSeed.RunAsync</c>، عَلى <c>app.Environment.IsDevelopment()</c>؛ وكُتلَةُ البَوّابَةِ عَلى <c>open</c> تَرفُضُ بِـ3.</para>
    /// </summary>
    [Fact]
    public void A_boot_outside_development_with_oto_staging_is_refused_before_seeding()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [OtoEnvironment.EnvironmentKey] = OtoEnvironment.Staging }).Build();
        new ServiceCollection().AddOtoShipping(config);
        Assert.Equal(OtoEnvironment.OpenBootValue, OtoEnvironment.BootLogValue(OtoEnvironment.Staging));
        var ex = Assert.Throws<InvalidOperationException>(() => OtoBootPolicy.AssertSafeOutsideDevelopment(false, OtoEnvironment.Staging));
        Assert.Contains(OtoBootPolicy.StagingOutsideDevelopment, ex.Message, StringComparison.Ordinal);

        var program = Read("apps/V1.App/Program.cs").Replace("\r\n", "\n");
        var build = program.IndexOf("var app = builder.Build();", StringComparison.Ordinal);
        var bootLine = program.IndexOf("OtoEnvironment.BootLogPrefix + \"{Oto}\"", StringComparison.Ordinal);
        Assert.True(build > 0 && bootLine > build, "سَطرا البِناءِ والإقلاعِ غائِبانِ عَن Program.cs — الأَداةُ عَمياء.");
        Assert.Equal(1, program[build..bootLine].Split("IOptions<OtoOptions>").Length - 1);

        var guard = program.IndexOf("OtoBootPolicy.AssertSafeOutsideDevelopment(", StringComparison.Ordinal);
        var files = program.IndexOf("FileStorageSelection.AssertNoStubsOutsideDevelopment(", StringComparison.Ordinal);
        var seed = program.IndexOf("await PlatformSeed.RunAsync(", StringComparison.Ordinal);
        Assert.True(files > build && guard > files && seed > guard, "حارِسُ بيئَةِ OTO لَيسَ بَينَ حارِسِ المَلَفّاتِ والبَذر.");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(program, @"OtoBootPolicy\.AssertSafeOutsideDevelopment\("));
        var call = program[guard..program.IndexOf(';', guard)];
        Assert.Contains("app.Environment.IsDevelopment()", call, StringComparison.Ordinal);
        Assert.Contains("IOptions<OtoOptions>>().Value.Environment", call, StringComparison.Ordinal);

        if (OperatingSystem.IsWindows()) return;
        Assert.StartsWith("✗ Shipping__Oto__Environment=staging", RunOtoGateBlock(OtoEnvironment.Staging), StringComparison.Ordinal);
    }

    /// <summary>وبِلا سَطرٍ في السِجِلِّ تَقولُ «لَم تُقَس» — ولا «مُغلَق» ولا ⚠ staging — ولَو كانَ المُتَغَيِّرُ staging في بيئَتِها.</summary>
    [Fact]
    public void The_gate_block_says_unmeasured_when_the_app_wrote_no_line()
    {
        if (OperatingSystem.IsWindows()) return;
        var text = RunBash(OtoGateFunction(), "oto_environment \"$1\"", "[06:48:20 INF] Now listening on: http://127.0.0.1:5171\n", "staging");
        Assert.StartsWith("⚠ بيئَةُ OTO: لَم تُقَس", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// <para>انقَلَبَ بِاسمِه (كانَ <c>Today_the_gate_block_renormalizes_the_value_and_parts_from_the_code</c>، <c>c1f2e29</c>): الكُتلَةُ كَما كانَت عِندَ
    /// <c>c1f2e29</c> بِحَرفِها — تَقرَأُ المُتَغَيِّرَ وتُطَبِّعُه بِـ<c>tr</c> — <b>عَيِّنَةٌ مَحقونَة</b>: حُكمُ الصَفِّ أَعلاه (⚠ حَيثُ يَفتَحُ الكودُ وَحدَه) يَحمَرُّ
    /// عَلَيها في الأَشكالِ الثَلاثَة، فَهُوَ لَيسَ أَعمى.</para>
    /// <para>(‏2026-10-29، قَرارُ المالِك د44): الحُكمُ صارَ «تُعَلِّمُ staging» (<c>FlagsStaging</c>: ⚠ القَديمُ أَو ✗ الرَفض) فَيُقاسُ بِه الصَفُّ الحَيُّ
    /// وهذِه العَيِّنَةُ مَعاً — والعَيِّنَةُ بِحَرفِها لَم تَتَغَيَّر.</para>
    /// </summary>
    [Theory]
    [InlineData("\vstaging")]
    [InlineData(" staging ")]
    [InlineData("sta ging")]
    public void The_staging_parity_check_goes_red_on_the_block_that_renormalized(string value)
    {
        if (OperatingSystem.IsWindows()) return;
        const string before = """
            oto_environment() {
                local env
                env="$(printf '%s' "${Shipping__Oto__Environment:-}" | tr -d ' \t\r\n' | tr 'A-Z' 'a-z')"
                case "$env" in
                    staging)
                        echo "  ⚠ Shipping__Oto__Environment=staging في إقلاعٍ إنتاجيّ — بيئَةُ OTO التَجريبِيَّة: مَعَ الخِزانَةِ يُرسَمُ «حِسابُ الشَّحن عِندَ OTO» لِكُلِّ مالِكِ إعلانٍ في كُلِّ مَتجَر، وتُنشَأُ بوليصاتُ مَن يَلصِقُ رَمزَه عَلى staging — لا تُضبَطُ في الـSpace؛ البُرهانُ الحَيُّ عَلى نَفَقٍ إلى فَرعِ Neon مَنسوخ (DEPLOY.md §٢·ط)." ;;
                    production)
                        echo "  الشَحنُ عَبرَ OTO: production مُغلَقٌ في الكود (OtoEnvironment.IsOpen) — يُساوي الغِياب" ;;
                    "")
                        echo "  الشَحنُ عَبرَ OTO: مُغلَق (لا Shipping__Oto__Environment)" ;;
                    *)
                        echo "  الشَحنُ عَبرَ OTO: قيمَةٌ خارِجَ المَعجَم — يَرمي بِها الإقلاعُ بِاسمِ المُتَغَيِّر (boot_verdict ⇒ 3)" ;;
                esac
                return 0
            }
            """;
        var text = RunBash(before, "oto_environment", AppLine(value), value);
        Assert.NotEqual(OtoEnvironment.IsOpen(value), FlagsStaging(text));
    }

    /// <summary>مَواضِعُ نِداءِ <c>CheckOtoDeliveryFeeAsync(</c> في نَصٍّ — بِلا سَطرِ تَعريفِه وبِلا أَسطُرِ التَعليق.</summary>
    internal static int FeeCallers(string source)
        => source.Split('\n').Count(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)
                                         && l.Contains("CheckOtoDeliveryFeeAsync(", StringComparison.Ordinal)
                                         && !l.Contains("public async Task", StringComparison.Ordinal));

    /// <summary>
    /// <para><b>‏<c>checkOTODeliveryFee</c> أَوَّلُ تَسلسُلِ القاعِدَة ٩ ولَيسَ في مَسارِ التَشغيل — والـADR يَقولُه</b> (‏2026-10-25، تَدقيقُ الإطلاق brief-P1b-5):
    /// صِفرُ مُنادٍ في <c>libs/</c> و<c>apps/</c>، و‏ADR-033 §٧‑ق يُسَمّي ذلكَ وسَبَبَه ومُستَهلِكَه. فَإن نادَتها نُقطَةٌ يَوماً حَمِرَ هذا حَتّى يُلحَقَ الـADR.
    /// والعَدّادُ يُقاسُ بِعَيِّنَةٍ مَحقونَة: مَصدَرٌ يُناديها يُعَدّ، وسَطرُ التَعريفِ والتَعليقُ لا.</para>
    /// </summary>
    [Fact]
    public void The_fee_call_has_no_runtime_caller_and_adr033_says_why()
    {
        var sources = new[] { "libs", "apps" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, d), "*.*", SearchOption.AllDirectories))
            .Where(f => (f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.True(sources.Count > 300, $"أَداةٌ عَمياء: {sources.Count} مِلَفّاً.");
        var callers = sources.Sum(f => FeeCallers(File.ReadAllText(f)));
        Assert.Equal(0, callers);
        Assert.Equal(1, FeeCallers("var fees = await oto.CheckOtoDeliveryFeeAsync(token, q, ct);\n// CheckOtoDeliveryFeeAsync(x)\n" +
                                   "public async Task<OtoResult<IReadOnlyList<OtoDeliveryOption>>> CheckOtoDeliveryFeeAsync(\n"));

        var adr = Read(Path.Combine("docs", Path.GetFileName(Directory.GetFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "docs"), "ADR-033-*.md").Single())));
        var q = adr.IndexOf("### ق. إلحاقُ ‏2026-10-25 (تدقيقُ الإطلاق brief-P1b-5)", StringComparison.Ordinal);
        Assert.True(q > 0, "‏ADR-033 §٧‑ق غائِب.");
        foreach (var phrase in new[] { "لا منادِيَ له في `libs/` ولا `apps/`", "If deliveryOptionId is not sent on the request", "أمرُ البرهانِ الحيِّ" })
            Assert.Contains(phrase, adr[q..], StringComparison.Ordinal);
    }

    /// <summary>
    /// <para><b>مَصدَرُ الجُملَةِ المُقتَبَسَةِ في ‏ADR-033 §٧‑ق البَند ٣ يُقالُ بِحَرفِه</b> (‏2026-10-25، مُراجَعَةُ تَيّارِ W8-6 الجَولَة ٢): لَيسَت في
    /// <c>24-SHIPPING.md</c> — مَصدَرُها في المُستَودَعِ تَعليقُ <c>OtoClient.CheckOtoDeliveryFeeAsync</c>، والصَفحَةُ <c>SH-05</c>. فَالدِقَّةُ في الـADR
    /// تُسَمّي الاثنَين، والتَعليقُ يَحمِلُ الجُملَة — فَإن سَقَطَ بَقِيَت الإحالَةُ إلى ما لا وُجودَ لَه، وهذا يَحمَرّ.</para>
    /// </summary>
    [Fact]
    public void Adr033_7q_names_SH05_and_the_client_comment_as_the_source_of_the_quoted_sentence()
    {
        var adr = Read(Path.Combine("docs", Path.GetFileName(Directory.GetFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "docs"), "ADR-033-*.md").Single())));
        var q = adr.IndexOf("### ق. إلحاقُ ‏2026-10-25 (تدقيقُ الإطلاق brief-P1b-5)", StringComparison.Ordinal);
        Assert.True(q > 0, "‏ADR-033 §٧‑ق غائِب.");
        var precision = adr.IndexOf("**دقّةٌ ‏2026-10-25 (مراجعةُ تيّارِ W8-6، الجولة ٢ — يُلحَقُ ولا يُعادُ حرفٌ فوقَ البند ٣)**", q, StringComparison.Ordinal);
        Assert.True(precision > q, "دِقَّةُ مَصدَرِ الجُملَةِ تَحتَ §٧‑ق غائِبَة.");
        foreach (var phrase in new[] { "`SH-05`", "`OtoClient.cs`", "`ec807a6`", "ليسا في `24-SHIPPING.md` بحرفِهما", "«ب» W9-1 · ٣" })
            Assert.Contains(phrase, adr[precision..], StringComparison.Ordinal);
        // ‏2026-10-25 (مُراجَعَةُ تَيّارِ W9-1 الجَولَة ١): «ولا يَتَّكِئُ كودٌ عَلَيها» في هذِه الدِقَّةِ مَقلوب — فَلا تَبقى بِلا الدِقَّةِ الَّتي تُصَحِّحُه.
        Assert.True(adr.IndexOf(SecondPrecision, precision, StringComparison.Ordinal) > precision,
            "دِقَّةُ «‏/ship يَتَّكِئُ عَلى الجُملَة» لا تَتلو الدِقَّةَ الَّتي قالَت إنَّ كوداً لا يَتَّكِئُ عَلَيها.");
        var client = Read("libs/kits/Delivery/ACommerce.Kit.Delivery.Core/OtoClient.cs");
        var flat = System.Text.RegularExpressions.Regex.Replace(client.Replace("///", " "), @"\s+", " ");
        Assert.Contains("If deliveryOptionId is not sent on the request, the assignment is made to the delivery company that complies with your feasibility rule",
            flat, StringComparison.Ordinal);
    }

    private const string SecondPrecision =
        "**دقّةٌ ثانيةٌ ‏2026-10-25 (مراجعةُ تيّارِ W9-1، الجولة ١ — يُلحَقُ ولا يُعادُ حرفٌ فوقَ الدقّةِ أعلاه)**";

    /// <summary>
    /// <para><b>‏<c>/ship</c> يَتَّكِئُ عَلى جُملَةِ الإسنادِ اليَوم، وأَسماءُ قارِئِ الأُجرَةِ تُسَمّى بِمَصدَرِها</b> (‏2026-10-25، مُراجَعَةُ تَيّارِ W9-1 الجَولَة ١):
    /// الدِقَّةُ الأولى تَحتَ ‏ADR-033 §٧‑ق قالَت «لا يَتَّكِئُ كودٌ عَلَيها»، و<c>/ship</c> يُغفِلُ <c>deliveryOptionId</c> اتِّكاءً عَلَيها. فَالدِقَّةُ
    /// الثانِيَةُ تُشتَرَطُ ويُقابَلُ ما تَقولُه بِالكود: <c>ShipAsync</c> يَبلُغُ <c>CreateOrderAsync</c> عَبرَ <c>PlaceAsync</c>، وتَعليقُ
    /// <c>CreateOrderAsync</c> يُحيلُ إلى الجُملَة، والأَسماءُ الَّتي يَقرَؤُها جِسمُ <c>CheckOtoDeliveryFeeAsync</c> هِيَ الخَمسَةُ الَّتي تُسَمّيها
    /// الفِقرَة — فَاسمٌ سادِسٌ يَقرَؤُه القارِئُ ولا تُسَمّيهِ يَحمَرّ. والعَدّادُ يُقاسُ بِعَيِّنَةٍ مَحقونَةٍ تَقرَأُ اسماً سادِساً.</para>
    /// </summary>
    [Fact]
    public void Adr033_7q_says_ship_relies_on_the_sentence_and_names_the_fee_reader_fields_by_their_source()
    {
        var adr = Read(Path.Combine("docs", Path.GetFileName(Directory.GetFiles(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "docs"), "ADR-033-*.md").Single())));
        var q = adr.IndexOf("### ق. إلحاقُ ‏2026-10-25 (تدقيقُ الإطلاق brief-P1b-5)", StringComparison.Ordinal);
        Assert.True(q > 0, "‏ADR-033 §٧‑ق غائِب.");
        var second = adr.IndexOf(SecondPrecision, q, StringComparison.Ordinal);
        Assert.True(second > q, "الدِقَّةُ الثانِيَةُ تَحتَ §٧‑ق غائِبَة.");
        var text = adr[second..];
        foreach (var phrase in new[]
                 {
                     "**`/ship` يتّكئُ على هذه الجملةِ اليوم**", "«ج» S7·٨ الخطوة 4", "«Shipping Prices › Check OTO Delivery Fee»", "«ب→تكملة» W9-1 · ٣",
                     "If you do not have a feasibility rule setting, the order will be automatically assigned to the first delivery company added.",
                     "`OtoClientTests.Create_order_sends_createShipment_true_with_the_cod_amount_and_returns_both_ids`",
                 })
            Assert.Contains(phrase, text, StringComparison.Ordinal);

        // المَسارُ الَّذي تُسَمّيه الفِقرَة: ShipAsync ← PlaceAsync ← CreateOrderAsync، والاختِبارُ الَّذي تُسَمّيه قائِم.
        var shipping = Read("libs/templates/ACommerce.Templates.Customer.Marketplace/Services/Deals/DealShipping.cs");
        Assert.Contains("await PlaceAsync(", MethodBody(shipping, "public async Task<DealShipResult> ShipAsync("), StringComparison.Ordinal);
        Assert.Contains("_oto.CreateOrderAsync(", MethodBody(shipping, "private async Task<OtoResult<OtoCreatedOrder>> PlaceAsync("), StringComparison.Ordinal);
        Assert.Contains("public async Task Create_order_sends_createShipment_true_with_the_cod_amount_and_returns_both_ids()",
            Read("tests/ACommerce.Platform.Tests/OtoClientTests.cs"), StringComparison.Ordinal);

        var client = Read("libs/kits/Delivery/ACommerce.Kit.Delivery.Core/OtoClient.cs");
        var flat = System.Text.RegularExpressions.Regex.Replace(client.Replace("///", " "), @"\s+", " ");
        Assert.Contains("لا <c>deliveryOptionId</c> (التَعيينُ لِقاعِدَةِ حِسابِ البائِعِ عِندَ OTO — أَعلاه)", flat, StringComparison.Ordinal);

        var fields = FeeReaderFields(MethodBody(client, "public async Task<OtoResult<IReadOnlyList<OtoDeliveryOption>>> CheckOtoDeliveryFeeAsync("));
        Assert.True(fields.Count > 0, "أَداةٌ عَمياء: صِفرُ اسمٍ في جِسمِ قارِئِ الأُجرَة.");
        Assert.Equal(new[] { "currency", "deliveryCompany", "deliveryOptionId", "deliveryOptionName", "price" }, fields);
        foreach (var f in fields)
            Assert.True(text.Contains($"`{f}`", StringComparison.Ordinal), $"قارِئُ الأُجرَةِ يَقرَأُ `{f}` والدِقَّةُ لا تُسَمّيه ({fields.Count} اسماً مَقروءاً).");

        // العَيِّنَة: اسمٌ سادِسٌ يُعَدّ، وما يُرسَلُ في الطَلَبِ وسَبَبُ «لا يُقرَأ» لا يُعَدّان.
        Assert.Equal(new[] { "codCharge", "deliveryCompany", "deliveryOptionId" },
            FeeReaderFields("[\"originCity\"] = q.OriginCity,\nif (!root.TryGetProperty(\"deliveryCompany\", out var list)) return Unreadable<X>(\"checkOTODeliveryFee\", \"deliveryCompany\");\n" +
                            "if (Text(o, \"deliveryOptionId\") is not { } id) continue;\nvar fee = Number(o, \"codCharge\");\n"));
    }

    /// <summary>جِسمُ عُضوٍ مِن تَوقيعِه إلى قَوسِه الخِتامِيِّ عَلى مَسافَةِ الأَعضاء (أَربَع) — والتَوقيعُ الغائِبُ يَحمَرّ لا يُعيدُ فَراغاً.</summary>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"التَوقيعُ غائِب: {signature}");
        var end = source.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        Assert.True(end > start, $"لا قَوسَ خِتامِيّاً بَعدَ: {signature}");
        return source[start..end];
    }

    /// <summary>الأَسماءُ الَّتي يَقرَؤُها جِسمٌ مِن رَدِّ OTO — <c>TryGetProperty("…")</c> و<c>Text(o, "…")</c> و<c>Number(o, "…")</c> — مُرَتَّبَةً بِلا تَكرار.</summary>
    internal static IReadOnlyList<string> FeeReaderFields(string body)
        => System.Text.RegularExpressions.Regex.Matches(body, @"\b(?:TryGetProperty|Text|Number)\(\s*(?:[A-Za-z_][A-Za-z0-9_]*\s*,\s*)?""(?<n>[A-Za-z_][A-Za-z0-9_]*)""")
            .Select(m => m.Groups["n"].Value).Distinct().Order(StringComparer.Ordinal).ToList();
}
