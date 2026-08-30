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

    /// <summary>
    /// <para><b>كَم مَرَّةً حُجِزَت هذِه الجَلسَةُ لِلتَحليل</b> —
    /// عَدّادٌ تَصاعُديٌّ يَصنَعُ <b>مِفتاحَ الفَرادَة</b> لِكُلِّ
    /// تَشغيلَة (<see cref="AnalysisRunClaim"/>)، لا إحصاءٌ لِلعَرض.</para>
    ///
    /// <para><b>ولِماذا عَلى الجَلسَةِ لا في مَكانٍ آخَر</b>: هُوَ
    /// نِصفُ المِفتاح، والنِصفُ الآخَرُ مُعَرِّفُ الجَلسَة. فَطَلَبانِ
    /// مُتَوازِيانِ يَقرَآنِ نَفسَ العَدَدِ ويُحاوِلانِ نَفسَ
    /// المِفتاح — و<b>Postgres يَختارُ واحِداً</b>. أَمّا لَو ضاعَت
    /// زِيادَةُ العَدّادِ فَالأَسوَأُ رَفضُ تَشغيلَةٍ لاحِقَةٍ مَرَّةً:
    /// <b>يَفشَلُ مُغلَقاً</b> لا مَفتوحاً.</para>
    ///
    /// <para>حَقلٌ جَديد: وَثائِقُ سابِقَةٌ لا تَحمِلُه تُقرَأُ بِـ
    /// <c>0</c>، فَأَوَّلُ حَجزٍ لَها يَأخُذُ المِفتاحَ <c>#0</c>.</para>
    /// </summary>
    public int AnalysisRuns { get; set; }
}

/// <summary>
/// <para><b>مِفتاحُ فَرادَةٍ لِتَشغيلَةِ تَحليلٍ واحِدَة</b> — وَثيقَةٌ
/// لا مَعنى لَها إلّا مُعَرِّفُها: وُجودُه يَعني «هذِه التَشغيلَةُ
/// حُجِزَت»، ومُحاوَلَةُ إدخالِه ثانِيَةً تَصطَدِمُ بِالمِفتاحِ
/// الأَوَّلِ في Postgres (‏<c>23505</c>).</para>
///
/// <para><b>ولِماذا مِفتاحٌ لا عَلَمٌ عَلى الجَلسَة</b> (وهذا هُوَ
/// القَرارُ كُلُّه): «اِقرَأِ الحالَةَ ثُمَّ اقلِبها» فَحصٌ ثُمَّ
/// كِتابَةٌ بِنافِذَةٍ بَينَهُما — وخَمسونَ طَلَباً مُتَوازِياً
/// يَعبُرونَها جَميعاً. أَمّا مِفتاحُ الفَرادَةِ فَ<b>القَرارُ فيه
/// هُوَ الكِتابَةُ نَفسُها</b>، فَلا نافِذَةَ أَصلاً. وهذا بِعَينِه
/// نَمَطُ مَسارِ المال: هُناكَ فَرادَةُ <c>(stream_id, version)</c>،
/// وهُنا فَرادَةُ المُعَرِّف — والحَكَمُ في الحالَتَينِ قاعِدَةُ
/// البَياناتِ لا الكود.</para>
///
/// <para><b>ولا يُحذَفُ عِندَ الانتِهاء</b>: حَجزٌ يُحذَفُ يَحتاجُ
/// مَن يَحذِفُه إن ماتَتِ العَمَلِيَّةُ في المُنتَصَف — فَيَلزَمُه
/// مُهلَةُ تَقادُمٍ وقَرارٌ حَولَها. والعَدّادُ يُغني عَن ذلك: كُلُّ
/// تَشغيلَةٍ مِفتاحُها، والسِجِلُّ يَبقى أَثَراً لِمَن أَرادَ
/// عَدَّها.</para>
/// </summary>
public sealed class AnalysisRunClaim
{
    /// <summary><c>{sessionId:N}#{attempt}</c> — يُبنى بِـ
    /// <c>FeasibilityAnalysisService.ClaimId</c> لا بِاليَد.</summary>
    public string Id { get; set; } = "";
    public Guid SessionId { get; set; }
    public int Attempt { get; set; }
    public DateTime ClaimedAt { get; set; } = DateTime.UtcNow;
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
