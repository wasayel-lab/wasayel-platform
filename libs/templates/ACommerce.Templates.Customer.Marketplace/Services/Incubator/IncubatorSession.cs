namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// جلسة "حاضنة" واحدة لصاحب مشروع: من وصف الفكرة الغامض حتى دراسة
/// الجدوى. وثيقة Marten مُخزَّنة تحت tenant ثابت "_incubator" (مشاريع
/// قبل أن يكون لها متجر). تجمع المراحل الثلاث (اكتشاف، تكوين مُقترَح،
/// تحليل) في وثيقة واحدة لتبسيط البنية الحالية بدل جداول منفصلة.
/// </summary>
public sealed class IncubatorSession
{
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public string OwnerName { get; set; } = "";

    public IncubatorStatus Status { get; set; } = IncubatorStatus.Discovery;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ─── Discovery ──────────────────────────────────────────────────
    /// <summary>إجابات أسئلة الاكتشاف: key = question id، value = إجابة.</summary>
    public Dictionary<string, string> Answers { get; set; } = new();
    /// <summary>وصف حر للفكرة بكلمات صاحب المشروع.</summary>
    public string ProjectDescription { get; set; } = "";

    // ─── Pattern suggestion ─────────────────────────────────────────
    public string SuggestedPattern { get; set; } = "";
    public string PatternConfidence { get; set; } = "";   // high|medium|low
    public string PatternReasoning { get; set; } = "";

    // ─── Analysis result ────────────────────────────────────────────
    /// <summary>نتيجة التحليل كاملةً (JSON من الـ LLM، مُتحقَّق منه).</summary>
    public string? AnalysisJson { get; set; }
    public int AnalysisQualityScore { get; set; }
    public string? AnalysisError { get; set; }
    public string PromptVersion { get; set; } = "";

    /// <summary>تَقييم المُستَخدِم لِكُلّ قِسم (key = section name,
    /// value = "up" | "down"). يُغَذّي تَحسينات الـ prompt لاحِقاً.</summary>
    public Dictionary<string, string> SectionFeedback { get; set; } = new();
}

public enum IncubatorStatus
{
    Discovery,            // يجيب على الأسئلة
    PatternSuggested,     // ظهر النمط المقترح، ينتظر القبول
    Analyzing,            // التحليل قيد التنفيذ
    Completed,            // اكتمل التحليل
    Failed,               // فشل التحليل
    Abandoned
}
