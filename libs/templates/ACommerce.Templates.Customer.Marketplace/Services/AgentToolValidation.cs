using System.Text.Json;
using Json.Schema;

namespace ACommerce.Templates.Customer.Marketplace.Services;

// ─── بَوّابَة مُصادَقَة المُخَطَّط ────────────────────────────────────────
// المُخَطَّطات المُعلَنَة في BuildAbstractTools كانَت تُقَيِّد تَوليد
// النَّموذَج فَقَط ولا تُفرَض عِندَ التَّنفيذ (الفَجوَة المُوَثَّقَة في
// AGENT-TOOLS §5). هذا الصِّنف يُجَمِّع المُخَطَّطات مَرَّةً واحِدَة
// ويُصادِق كُلّ حُمولَة قَبل أَيّ تَنفيذ — بِذلِك يُصبِح ضَمان
// «رَفض ما يُخالِف المُخَطَّط» مُبرهَناً لا مُفتَرَضاً (TESTING-PROTOCOL §T3).

/// <summary>نَتيجَة مُصادَقَة حُمولَة أَداة ضِدّ مُخَطَّطها المُعلَن.</summary>
public sealed record AgentToolValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static AgentToolValidationResult Success { get; } =
        new(true, Array.Empty<string>());

    public static AgentToolValidationResult Failure(params string[] errors) =>
        new(false, errors);
}

/// <summary>مُصادِق حُمولات الأَدَوات: خَريطَة اسم الأَداة ← مُخَطَّط
/// مُجَمَّع (تُبنى مَرَّةً واحِدَة)، مَصدَرُها نَفس تَعريفات
/// <see cref="AgentService.BuildAbstractTools"/> — لا مَصدَر ثانٍ
/// لِلمُخَطَّطات كَي لا يَنحَرِفا.</summary>
public static class AgentToolValidator
{
    private static readonly IReadOnlyDictionary<string, JsonSchema> Schemas = BuildSchemas();

    private static Dictionary<string, JsonSchema> BuildSchemas()
    {
        var map = new Dictionary<string, JsonSchema>(StringComparer.Ordinal);
        foreach (var tool in AgentService.BuildAbstractTools())
            map[tool.Name] = JsonSchema.FromText(tool.InputSchemaJson);
        return map;
    }

    /// <summary>مُصادَقَة <paramref name="inputJson"/> ضِدّ مُخَطَّط
    /// <paramref name="toolName"/>. اسم أَداة غَير مَعروف = فَشَل،
    /// وَ JSON تالِف = فَشَل — لا استِثناءات تَتَسَرَّب.</summary>
    public static AgentToolValidationResult Validate(string toolName, string inputJson)
    {
        if (!Schemas.TryGetValue(toolName, out var schema))
            return AgentToolValidationResult.Failure($"أَداة غَير مَعروفَة: {toolName}");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(inputJson);
        }
        catch (JsonException ex)
        {
            return AgentToolValidationResult.Failure("JSON تالِف: " + ex.Message);
        }

        using (doc)
        {
            var results = schema.Evaluate(doc.RootElement, new EvaluationOptions
            {
                OutputFormat = OutputFormat.List
            });
            if (results.IsValid) return AgentToolValidationResult.Success;

            var errors = new List<string>();
            foreach (var detail in results.Details)
            {
                if (detail.Errors is not { Count: > 0 } errs) continue;
                var loc = detail.InstanceLocation.ToString();
                var prefix = string.IsNullOrEmpty(loc) ? "" : loc + ": ";
                foreach (var err in errs)
                    errors.Add(prefix + err.Value);
            }
            if (errors.Count == 0)
                errors.Add("الحُمولَة لا تُطابِق مُخَطَّط الأَداة.");
            return new AgentToolValidationResult(false, errors);
        }
    }
}
