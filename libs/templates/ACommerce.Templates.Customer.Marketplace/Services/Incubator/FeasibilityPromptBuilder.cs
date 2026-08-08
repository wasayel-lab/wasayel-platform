using System.Text;
using System.Text.Json;

namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// يبني system prompt هيكلي للتحليل الاستثماري: دور + سياق سعودي محقون
/// + منهجية + قيود (لا تخترع أرقاماً) + schema المخرجات. نسخة الـ prompt
/// مُعرّفة بـ <see cref="Version"/> لتتبّع الجودة وA/B لاحقاً.
/// </summary>
public sealed class FeasibilityPromptBuilder
{
    public const string Version = "feasibility-v1";
    private readonly SaudiDataProvider _data;

    public FeasibilityPromptBuilder(SaudiDataProvider data) => _data = data;

    /// <summary>schema المخرجات — حقول واضحة لا نصوص حرة، ليُحلَّل JSON بثقة.</summary>
    public const string OutputSchema = """
    {
      "summary": { "marketSizeSar": "string", "estimatedCustomersYear1": "string", "requiredInvestmentSar": "string", "verdict": "string (go|caution|no-go) + سطر تبرير" },
      "marketSizing": { "tam": "string", "sam": "string", "som": "string", "reasoning": "string" },
      "customerSegments": [ { "name": "string", "description": "string", "sizeEstimate": "string" } ],
      "competitors": [ { "name": "string", "positioning": "string", "gap": "string (الفجوة التي تستغلها)" } ],
      "revenueModel": { "model": "string", "pricingSar": "string", "unitEconomics": "string" },
      "costStructure": { "launchSar": "string", "monthlyOpsSar": "string", "cacSar": "string", "ltvSar": "string" },
      "financialProjection": { "year1RevenueSar": "string", "breakEvenMonths": "string", "scenario": "string (متحفظ/متوسط/متفائل)" },
      "risks": [ { "title": "string", "category": "string (market|operational|financial|regulatory)", "impact": "string (high|medium|low)", "likelihood": "string (high|medium|low)", "mitigation": "string" } ],
      "lessonsFromFailures": [ "string (درس من فشل مشابه ينطبق على هذه الفكرة)" ],
      "roadmap": [ { "phase": "string", "durationWeeks": "number", "goal": "string" } ],
      "kpis": [ { "name": "string", "target": "string" } ],
      "recommendedPattern": { "pattern": "string", "reasoning": "string", "timeToLaunch": "string" },
      "recommendations": [ "string" ]
    }
    """;

    public string Build(IncubatorSession session, string failuresForSector)
    {
        var sb = new StringBuilder();
        sb.AppendLine("أنت محلّل أعمال خبير متخصص في السوق السعودي. مهمتك إنتاج دراسة جدوى صريحة وموضوعية لفكرة مشروع.");
        sb.AppendLine();
        sb.AppendLine("# المنهجية");
        sb.AppendLine("حلّل الفكرة عبر الأقسام: حجم السوق (TAM/SAM/SOM)، شرائح العملاء، المنافسون والفجوة، نموذج الإيراد، هيكل التكلفة، التوقعات المالية، المخاطر، دروس من فشل مشابه، خارطة الطريق، مؤشرات الأداء، التوصيات.");
        sb.AppendLine();
        sb.AppendLine("# سياق السوق السعودي (استخدم هذه الأرقام، لا تخترع غيرها)");
        sb.AppendLine("## بيانات السوق:");
        sb.AppendLine(_data.MarketContextJson);
        sb.AppendLine("## المنافسون:");
        sb.AppendLine(_data.CompetitorsJson);
        sb.AppendLine("## مقاييس التكلفة:");
        sb.AppendLine(_data.CostBenchmarksJson);
        sb.AppendLine();
        sb.AppendLine("# دروس من مشاريع فشلت في نفس القطاع (أبرزها كمخاطر حقيقية)");
        sb.AppendLine(failuresForSector);
        sb.AppendLine();
        sb.AppendLine("# قدرات منصة ACommerce (اقترح النمط المناسب إن لاءم الفكرة)");
        sb.AppendLine(_data.CapabilitiesMarkdown);
        sb.AppendLine();
        sb.AppendLine("# قيود صارمة");
        sb.AppendLine("- لا تخترع أرقاماً. استخدم البيانات المعطاة. إن لم تعرف رقماً، قل 'يحتاج بحثاً ميدانياً'.");
        sb.AppendLine("- صراحة وموضوعية: أبرز المخاطر بقدر ما تبرز الفرص. لا تتملّق الفكرة.");
        sb.AppendLine("- كل الأرقام بالريال السعودي.");
        sb.AppendLine("- اربط المخاطر بدروس الفشل المعطاة حيثما أمكن.");
        sb.AppendLine();
        sb.AppendLine("# صيغة المخرجات");
        sb.AppendLine("أعد JSON صالحاً فقط (بلا أي نص قبله أو بعده) مطابقاً لهذا الـ schema تماماً:");
        sb.AppendLine(OutputSchema);
        return sb.ToString();
    }

    /// <summary>رسالة المستخدم: وصف الفكرة + إجابات الاكتشاف + النمط المقترح.</summary>
    public static string BuildUserMessage(IncubatorSession s)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## وصف الفكرة:");
        sb.AppendLine(string.IsNullOrWhiteSpace(s.ProjectDescription) ? "(لم يُكتب وصف)" : s.ProjectDescription);
        sb.AppendLine();
        sb.AppendLine("## إجابات الاكتشاف:");
        foreach (var q in DiscoveryQuestionBank.Questions)
        {
            if (q.Type == "text") continue;
            if (!s.Answers.TryGetValue(q.Id, out var ans) || string.IsNullOrEmpty(ans)) continue;
            var label = q.Options.FirstOrDefault(o => o.Value == ans)?.LabelAr ?? ans;
            sb.AppendLine($"- {q.QuestionAr} → {label}");
        }
        sb.AppendLine();
        if (!string.IsNullOrEmpty(s.SuggestedPattern))
            sb.AppendLine($"## النمط المقترح أولياً: {s.SuggestedPattern} ({s.PatternConfidence}) — {s.PatternReasoning}");
        return sb.ToString();
    }

    /// <summary>نَصّ سياق السوق الخام — يُستَخدَم في prompt الـ refine
    /// المُختَصَر بَدَلَ تَحميل كُلّ شَيء.</summary>
    internal string _dataMarket() => _data.MarketContextJson;

    /// <summary>يستخرج حالات الفشل لقطاع محدد من ملف الفشل الكامل.</summary>
    public string FailuresForSector(string sector)
    {
        try
        {
            using var doc = JsonDocument.Parse(_data.FailuresJson);
            if (!doc.RootElement.TryGetProperty("failures", out var arr)) return _data.FailuresJson;
            var matched = new List<string>();
            var generic = new List<string>();
            foreach (var f in arr.EnumerateArray())
            {
                var fSector = f.TryGetProperty("sector", out var sv) ? sv.GetString() ?? "" : "";
                var cause   = f.TryGetProperty("cause", out var cv) ? cv.GetString() ?? "" : "";
                var lesson  = f.TryGetProperty("lesson", out var lv) ? lv.GetString() ?? "" : "";
                var line = $"- [{fSector}] السبب: {cause} | الدرس: {lesson}";
                if (fSector == sector) matched.Add(line); else generic.Add(line);
            }
            // القطاع المطابق أولاً ثم عيّنة عامة.
            var picked = matched.Concat(generic.Take(3)).ToList();
            return picked.Count > 0 ? string.Join("\n", picked) : _data.FailuresJson;
        }
        catch { return _data.FailuresJson; }
    }
}
