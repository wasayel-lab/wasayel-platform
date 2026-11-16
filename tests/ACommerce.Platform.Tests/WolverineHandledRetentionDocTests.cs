using System.Text.RegularExpressions;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «ثُمَّ `Handled` بَعدَه» يَصدُقُ خَمسَ دَقائِق — مُدَّةُ إبقاءِ الرِسالَةِ المُعالَجَةِ في DEPLOY §٢·ح مَقيسَةً عَلى Wolverine المُرَكَّبِ لا بِاليَد (‏2026-11-05، الموجَة ١٢ G3 · ١٠) ═══
//
// **ما كانَ**: الموجَة ١١ (تَيّارُ F5، H-docs-4) قاسَت `DurabilitySettings.KeepAfterMessageHandling` = ‏`00:05:00` و`HandledMessageCleanupPollingTime` = ‏`00:01:00` بِقِراءَةِ
// `new DurabilitySettings()` بِاليَد وكَتَبَتهُما في DEPLOY §٢·ح، وإجراءُ المالِك هـ-3 يَقرَأُ النَجاحَ مِن تِلكَ النافِذَة («صَفٌّ `Handled` بَعدَ وَقتِه»). ولا شَيءَ يُمسِكُ الرَقَمَ
// أَمامَ نُسخَةِ الحُزمَة: تَرقِيَةُ WolverineFx تُغَيِّرُ الافتِراضَ بِصَمت (القاعِدَة ٢). **فَهذا الصَفّ**: يَقرَأُ الرَقَمَينِ مِن الوَثيقَةِ نَفسِها، ومِن خِياراتِ Wolverine كَما يُرَكِّبُها
// المُضيفُ لِرِسالَةِ المُهلَة (`GatewayInvoiceExpiry.Configure` نَفسُه)، ويَقيسُ أَنْ لا كودَ في `apps` و`libs` يُغَيِّرُهُما — ويَطبَعُ الطَرَفَين؛ وأَداةُ المُقارَنَةِ تُقاسُ بِعَيبٍ مَحقون.

public class WolverineHandledRetentionDocTests(ITestOutputHelper output)
{
    private static string Root => ThemeZeroEquivalenceTests.RepoRoot;

    /// <summary>قِسمُ §٢·ح مِن DEPLOY — مِن عُنوانِه إلى العُنوانِ التالي مِن مُستَواه.</summary>
    internal static string DeploySection(string deploy)
    {
        var at = deploy.IndexOf("## ٢·ح.", StringComparison.Ordinal);
        Assert.True(at >= 0, "لا قِسمَ §٢·ح في DEPLOY.md");
        var section = deploy[at..];
        var next = section.IndexOf("\n## ", 3, StringComparison.Ordinal);
        return next < 0 ? section : section[..next];
    }

    /// <summary>القيمَةُ الَّتي تَقولُها الوَثيقَةُ لِخاصِّيَّةٍ مِن <c>DurabilitySettings</c> — «`DurabilitySettings.X` = ‏`hh:mm:ss`» أَو «`X` = ‏`hh:mm:ss`» — و<c>null</c> حينَ لا تَقولُها.</summary>
    internal static TimeSpan? StatedIn(string section, string property)
    {
        var m = Regex.Match(section, $@"`(?:DurabilitySettings\.)?{Regex.Escape(property)}`\s*=\s*\u200f?`(\d\d:\d\d:\d\d)`");
        return m.Success ? TimeSpan.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    /// <summary>خِياراتُ Wolverine كَما يُرَكِّبُها المُضيفُ لِرِسالَةِ المُهلَة — <see cref="GatewayInvoiceExpiry.Configure"/> نَفسُه — بِلا إقلاع.</summary>
    private static WolverineOptions Composed()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.UseWolverine(opts =>
        {
            opts.UseRuntimeCompilation();
            opts.Discovery.DisableConventionalDiscovery();
            GatewayInvoiceExpiry.Configure(opts);
        });
        using var host = builder.Build();
        return host.Services.GetRequiredService<WolverineOptions>();
    }

    /// <summary><b>المُوجِب</b>: الرَقَمانِ في §٢·ح هُما ما في خِياراتِ Wolverine المُرَكَّبَةِ وافتِراضِ الحُزمَةِ نَفسِها — ولا كودَ يُغَيِّرُهُما (‏0 مَوضِعٍ في مِلَفّاتِ <c>apps</c> و<c>libs</c>
    /// المَفحوصَة، والعَدَدُ يُطبَعُ ويَفشَلُ عَلى الصِفر — القاعِدَة ١٠).</summary>
    [Fact]
    public void The_handled_retention_deploy_states_is_the_composed_wolverine_value_and_no_code_overrides_it()
    {
        var section = DeploySection(File.ReadAllText(Path.Combine(Root, "docs", "DEPLOY.md")));
        var durability = Composed().Durability;
        var defaults = new Wolverine.DurabilitySettings();

        var checkedProps = 0;
        foreach (var (name, composed, library) in new[]
                 {
                     (nameof(Wolverine.DurabilitySettings.KeepAfterMessageHandling), durability.KeepAfterMessageHandling, defaults.KeepAfterMessageHandling),
                     (nameof(Wolverine.DurabilitySettings.HandledMessageCleanupPollingTime), durability.HandledMessageCleanupPollingTime, defaults.HandledMessageCleanupPollingTime),
                 })
        {
            var stated = StatedIn(section, name);
            output.WriteLine($"{name}: DEPLOY §٢·ح = {stated?.ToString() ?? "—"} · Wolverine المُرَكَّب = {composed} · افتِراضُ الحُزمَة = {library}");
            Assert.NotNull(stated);
            Assert.Equal(stated, composed);
            Assert.Equal(stated, library);
            checkedProps++;
        }
        Assert.Equal(2, checkedProps);

        // والنُسخَةُ الَّتي تُسَمّيها الوَثيقَةُ هِيَ المُثَبَّتَةُ في `Directory.Packages.props` — فَتَرقِيَةٌ تُعيدُ هذا الصَفَّ عَلى نُسخَتِها ويَتبَعُها نَصُّ الوَثيقَة.
        var docVersion = Regex.Match(section, @"Wolverine\s*\u200f?(\d+\.\d+\.\d+)\s*المُثَبَّت");
        var pinned = Regex.Match(File.ReadAllText(Path.Combine(Root, "Directory.Packages.props")), @"Include=""WolverineFx""\s+Version=""(\d+\.\d+\.\d+)""");
        output.WriteLine($"نُسخَةُ Wolverine: DEPLOY §٢·ح = {(docVersion.Success ? docVersion.Groups[1].Value : "—")} · Directory.Packages.props = {(pinned.Success ? pinned.Groups[1].Value : "—")} · " +
                         $"المُحَمَّلَة = {typeof(WolverineOptions).Assembly.GetName().Version}");
        Assert.True(docVersion.Success && pinned.Success, "نُسخَةٌ لا تُقرَأ — الأَداةُ عَمياء.");
        Assert.Equal(pinned.Groups[1].Value, docVersion.Groups[1].Value);

        var files = new[] { "apps", "libs" }.SelectMany(d => Directory.EnumerateFiles(Path.Combine(Root, d), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
        var overrides = files.Where(f => File.ReadAllText(f) is var t
                                         && (t.Contains("KeepAfterMessageHandling", StringComparison.Ordinal) || t.Contains("HandledMessageCleanupPollingTime", StringComparison.Ordinal)))
            .ToArray();
        output.WriteLine($"مِلَفّاتُ C# المَفحوصَةُ في apps وlibs: {files.Length} · ما يُغَيِّرُ المُدَّتَين: {overrides.Length}");
        Assert.True(files.Length > 0, "صِفرُ مِلَفٍّ مَفحوص — الأَداةُ عَمياء.");
        Assert.Empty(overrides);
    }

    /// <summary><b>الأَداةُ تُقاسُ قَبلَ أَن يُوثَقَ بِها</b> (القاعِدَة ١٠): رَقَمٌ مَحقونٌ في نَصِّ الوَثيقَةِ (‏`00:10:00` بَدَلَ ‏`00:05:00`) يُقرَأُ ولا يُساوي المُرَكَّب — فَيُحمِرُّ الصَفُّ أَعلاه؛
    /// وقِسمٌ بِلا السَطرِ لا يُقرَأُ رَقَماً.</summary>
    [Fact]
    public void An_injected_mismatch_in_the_deploy_text_is_read_and_differs_from_the_composed_value()
    {
        var section = DeploySection(File.ReadAllText(Path.Combine(Root, "docs", "DEPLOY.md")));
        var stated = StatedIn(section, nameof(Wolverine.DurabilitySettings.KeepAfterMessageHandling))!.Value;
        var injected = section.Replace($"`{stated:hh\\:mm\\:ss}`", "`00:10:00`", StringComparison.Ordinal);
        Assert.NotEqual(section, injected);
        var read = StatedIn(injected, nameof(Wolverine.DurabilitySettings.KeepAfterMessageHandling));
        output.WriteLine($"المَحقون: {read} · المُرَكَّب: {Composed().Durability.KeepAfterMessageHandling}");
        Assert.Equal(TimeSpan.FromMinutes(10), read);
        Assert.NotEqual(read, Composed().Durability.KeepAfterMessageHandling);
        Assert.Null(StatedIn("## ٢·ح. بِلا سَطر", nameof(Wolverine.DurabilitySettings.KeepAfterMessageHandling)));
    }
}
