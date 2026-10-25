using ACommerce.Kit.Delivery;
using ACommerce.Platform.MultiTenancy;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Shipping;
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

    private const string OtoGateStart = "# ─── بِدايَةُ oto_environment ───";
    private const string OtoGateEnd = "# ─── نِهايَةُ oto_environment ───";

    private static string RunOtoGateBlock(string? value)
    {
        var gate = Read("scripts/verify-production-boot.sh");
        var a = gate.IndexOf(OtoGateStart, StringComparison.Ordinal);
        Assert.True(a > 0, "كُتلَةُ oto_environment غائِبَة.");
        Assert.Equal(a, gate.LastIndexOf(OtoGateStart, StringComparison.Ordinal));
        var b = gate.IndexOf(OtoGateEnd, a, StringComparison.Ordinal);
        Assert.True(b > a);
        Assert.True(File.Exists("/bin/bash"), "أَداةٌ غائِبَة: /bin/bash.");
        var dir = Directory.CreateTempSubdirectory("wasayel-oto-gate-");
        try
        {
            var script = Path.Combine(dir.FullName, "run.sh");
            File.WriteAllText(script, "set -uo pipefail\n" + gate[a..(b + OtoGateEnd.Length)] + "\noto_environment\n", new System.Text.UTF8Encoding(false));
            var psi = new System.Diagnostics.ProcessStartInfo("/bin/bash", new[] { script })
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            };
            psi.Environment.Remove("Shipping__Oto__Environment");
            if (value is not null) psi.Environment["Shipping__Oto__Environment"] = value;
            using var p = System.Diagnostics.Process.Start(psi)!;
            var text = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            Assert.True(p.WaitForExit(10_000));
            Assert.Equal(0, p.ExitCode);
            return text.Trim();
        }
        finally { dir.Delete(recursive: true); }
    }

    /// <summary>
    /// <para><b>بَوّابَةُ الإقلاعِ تُنَبِّهُ عَلى <c>staging</c> في إقلاعِها الإنتاجيِّ ولا تُغَيِّرُ رَمزَها</b> — وحُكمُ الكُتلَةِ (⚠ أَم لا) يُساوي
    /// <see cref="OtoEnvironment.IsOpen"/> شَكلاً شَكلاً: بِقَصٍّ وأَحرُفٍ كَبيرَة، والإنتاجُ، والفَراغ. والكُتلَةُ تُنادى مَرَّةً قَبلَ النَشر.</para>
    /// </summary>
    [Theory]
    [InlineData("staging")]
    [InlineData(" Staging ")]
    [InlineData("STAGING")]
    [InlineData("production")]
    [InlineData("")]
    [InlineData(null)]
    public void The_boot_gate_warns_on_staging_exactly_where_the_code_opens_it(string? value)
    {
        if (OperatingSystem.IsWindows()) return;
        var text = RunOtoGateBlock(value);
        var warns = text.StartsWith("⚠ Shipping__Oto__Environment=staging", StringComparison.Ordinal);
        Assert.Equal(OtoEnvironment.IsOpen(value), warns);
        if (warns)
        {
            Assert.Contains("لِكُلِّ مالِكِ إعلانٍ في كُلِّ مَتجَر", text, StringComparison.Ordinal);
            Assert.Contains("DEPLOY.md §٢·ط", text, StringComparison.Ordinal);
        }
        var gate = Read("scripts/verify-production-boot.sh");
        Assert.Equal(1, gate.Split("\noto_environment\n").Length - 1);
        Assert.True(gate.IndexOf("\noto_environment\n", StringComparison.Ordinal) < gate.IndexOf("if ! dotnet publish \"$PROJECT\"", StringComparison.Ordinal));
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
}
