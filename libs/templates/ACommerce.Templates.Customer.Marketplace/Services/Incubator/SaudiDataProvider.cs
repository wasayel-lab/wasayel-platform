using System.Reflection;

namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// يُحَمِّل بيانات السوق السعودي المُضمَّنة (embedded resources) مرة واحدة
/// ويُخَزِّنها. تُحقَن في prompt التحليل الاستثماري لإعطاء أرقام محلية
/// بدلاً من تعميمات. القراءة كسولة (lazy) لأن المحتوى ثابت طوال العمر.
/// </summary>
public sealed class SaudiDataProvider
{
    private static readonly Assembly Asm = typeof(SaudiDataProvider).Assembly;
    private const string Ns = "ACommerce.Templates.Customer.Marketplace.Data.";

    private readonly Lazy<string> _market   = Embedded("saudi-market-context.json");
    private readonly Lazy<string> _competit = Embedded("saudi-competitors.json");
    private readonly Lazy<string> _costs     = Embedded("saudi-cost-benchmarks.json");
    private readonly Lazy<string> _failures  = Embedded("saudi-startup-failures.json");
    private readonly Lazy<string> _caps      = Embedded("acommerce-capabilities.md");

    public string MarketContextJson => _market.Value;
    public string CompetitorsJson   => _competit.Value;
    public string CostBenchmarksJson => _costs.Value;
    public string FailuresJson       => _failures.Value;
    public string CapabilitiesMarkdown => _caps.Value;

    private static Lazy<string> Embedded(string fileName) => new(() =>
    {
        using var stream = Asm.GetManifestResourceStream(Ns + fileName);
        if (stream is null) return "{}";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });
}
