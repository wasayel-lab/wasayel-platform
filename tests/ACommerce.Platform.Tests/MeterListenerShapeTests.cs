using System.Text.RegularExpressions;
using ACommerce.Platform.Hosting;
using ACommerce.Templates.Customer.Marketplace;
using ACommerce.Templates.Customer.Marketplace.Services.Metering;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ G4 — مُستَمِعُ العَدِّ لا يَقرَأُ حالَةً مَخزونَة، وهُوَ واحِدٌ في التَركيب ═══
// (‏2026-11-12، الموجَة ١٣ W13-6·٣ — ADR-051 §١١؛ التَصميم §5.1؛ القاعِدَة ٧: «مَن يَقبَلُ IDocumentSession ومَن لا يَقبَلُها لا يَجتَمِعان»).
// المُحَوِّلُ يَقبَلُ الجَلسَةَ لِأَنّه مُستَمِعُها — فَما يَمَسُّه مِنها مُسَمّى: التَغييراتُ المُعَلَّقَةُ ونُسخَتُها ومُستَأجِرُها وتَرحيلُ الجُملَة،
// واللَمسَةُ الوَحيدَةُ المَأذونَةُ لِلقاعِدَة `Database.EnsureStorageExistsAsync` (RULES-18، مُخَبَّأَةٌ بَعدَ الإقلاع). وكُلُّ قِراءَةٍ أُخرى صِفر.

public class MeterListenerShapeTests(ITestOutputHelper output)
{
    /// <summary>ما يَجوزُ لِلمُحَوِّلِ أَن يَمَسَّه مِن الجَلسَة — بِحَرفِ التَصميم §5.1.</summary>
    internal static readonly string[] Allowed =
        ["PendingChanges", "VersionFor", "TenantId", "QueueSqlCommand", "Database.EnsureStorageExistsAsync"];

    /// <summary>ما يَقرَأُ حالَةً مَخزونَةً أَو يَفتَحُ طَريقاً إلَيها — صِفرٌ في المُحَوِّل.</summary>
    internal static readonly Regex Forbidden = new(
        @"\b(Query|QueryAsync|LoadAsync|Load|LoadManyAsync|AdvancedSql|Connection|QuerySession|LightweightSession|OpenSession|FetchStreamAsync|AggregateStreamAsync)\b",
        RegexOptions.Compiled);

    private static readonly Regex SessionAccess = new(@"\bsession\.(?<m>[A-Za-z]+(?:\.[A-Za-z]+(?=\())?)", RegexOptions.Compiled);

    private static string Source() => File.ReadAllText(Path.Combine(ThemeZeroEquivalenceTests.RepoRoot,
        "libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Services", "Metering", "UsageListener.cs"));

    /// <summary>الكودُ بِلا تَعليقات — فَما يُفحَصُ هُوَ ما يُشَغَّلُ لا شَرحُه (الرَأسُ يُسَمّي المَمنوعَ لِيَقولَ إنَّه لا يَفعَلُه).</summary>
    internal static string CodeOf(string source)
        => string.Join('\n', source.Split('\n').Select(l => { var i = l.IndexOf("//", StringComparison.Ordinal); return i < 0 ? l : l[..i]; }));

    internal static (IReadOnlyList<string> Calls, IReadOnlyList<string> NotAllowed, IReadOnlyList<string> Forbidden) Scan(string source)
    {
        var code = CodeOf(source);
        var calls = SessionAccess.Matches(code).Select(m => m.Groups["m"].Value).ToList();
        var notAllowed = calls.Where(c => !Allowed.Contains(c, StringComparer.Ordinal)).Distinct().ToList();
        var forbidden = Forbidden.Matches(code).Select(m => m.Value).ToList();
        return (calls, notAllowed, forbidden);
    }

    [Fact]
    public void The_listener_touches_only_the_named_session_members_and_reads_no_stored_state()
    {
        var (calls, notAllowed, forbidden) = Scan(Source());
        output.WriteLine($"[G4] نِداءاتُ الجَلسَةِ في المُحَوِّل {calls.Count}: {string.Join(", ", calls.Distinct())} · خارِجَ المَسموح {notAllowed.Count} · مَمنوعَة {forbidden.Count}");
        Assert.True(calls.Count >= 4, $"أَداةٌ عَمياء: {calls.Count} نِداءَ جَلسَة.");
        Assert.Contains("Database.EnsureStorageExistsAsync", calls);
        Assert.Contains("QueueSqlCommand", calls);
        Assert.Empty(notAllowed);
        Assert.Empty(forbidden);
    }

    /// <summary><b>حَقنُ العَيب</b> (القاعِدَة ١٠): نُسخَةٌ مِن المُحَوِّلِ تَقرَأُ الصَفقَةَ المَخزونَة — الفاحِصُ يُمسِكُ النِداءَ والكَلِمَة.</summary>
    [Fact]
    public void An_injected_stored_read_in_a_copy_of_the_listener_is_caught()
    {
        var source = Source();
        // ‏(‏2026-11-12، W13-6·٧) المِرساةُ سَطرُ الخُروجِ المُبَكِّرِ كَما صارَ بِقَناةِ المَجرى — والحَقنُ بَعدَه كَما كانَ.
        // ‏(‏2026-11-12، W13-6·٨) وكَما صارَ بِوَثائِقِ الوَحَداتِ الأَربَع: الخُروجُ حينَ لا وِلادَةَ ولا مَجرىً يَبدَأ — والحَقنُ نَفسُه بَعدَه.
        const string anchor = "if (!documents.Any(d => d.Created) && !streams.Any(s => s.Started)) return;";
        Assert.Contains(anchor, source, StringComparison.Ordinal);
        var injected = source.Replace(anchor, anchor + "\n        var stored = await session.LoadAsync<Deal>(Guid.Empty, token);", StringComparison.Ordinal);
        var (_, notAllowed, forbidden) = Scan(injected);
        output.WriteLine($"[G4] الحَقن: خارِجَ المَسموح {string.Join(", ", notAllowed)} · مَمنوعَة {string.Join(", ", forbidden)}");
        Assert.Equal(new[] { "LoadAsync" }, notAllowed);
        Assert.Equal(new[] { "LoadAsync" }, forbidden);
        Assert.Equal(new[] { "Connection" }, Scan(source + "\nvar c = session.Connection;").Forbidden);
    }

    /// <summary><b>مُستَمِعٌ واحِدٌ في التَركيب</b> — كانَ صِفراً (التَصميم §5.1: <c>Listeners.Count = 0</c> مَقيساً)، وصارَ واحِداً هُوَ هذا؛ ومُستَمِعٌ ثانٍ
    /// يُضاعِفُ العَدَّ أَو يُنازِعُه.</summary>
    [Fact]
    public async Task The_composed_store_carries_exactly_one_listener_and_it_is_the_meter()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Configuration["ConnectionStrings:Postgres"] = "Host=localhost;Database=compose_only;Username=u;Password=p";
        builder.Host.UseDefaultServiceProvider(o => { o.ValidateOnBuild = false; o.ValidateScopes = false; });
        builder.AddPlatformHost();
        builder.Services.AddCustomerMarketplaceTemplate();
        await using var app = builder.Build();
        var store = (DocumentStore)app.Services.GetRequiredService<IDocumentStore>();

        var listeners = store.Options.Listeners.Select(l => l.GetType().Name).ToList();
        var objects = store.Storage.AllObjects().Select(o => o.Identifier.QualifiedName).Where(n => n.StartsWith("meter.", StringComparison.Ordinal)).ToList();
        output.WriteLine($"[G4] مُستَمِعو المَخزَنِ المُرَكَّب {listeners.Count}: {string.Join(", ", listeners)} · كائِناتُ meter {objects.Count}: {string.Join(", ", objects)}");
        Assert.Equal(new[] { nameof(UsageListener) }, listeners);
        Assert.Equal(new[] { "meter.agg_cursor", "meter.agg_daily", "meter.generation", "meter.unit_epoch", "meter.usage_entry" }, objects.Order(StringComparer.Ordinal));
    }
}
