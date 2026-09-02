using System.Text.Json;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// محرّك التحليل الاستثماري. يبني الـ prompt، يستدعي الـ LLM backend
/// الموجود، يتحقق من JSON (مع إعادة محاولة)، يحسب درجة جودة، ويحفظ على
/// <see cref="IncubatorSession"/>. يُخزَّن تحت tenant ثابت "_incubator".
/// </summary>
public sealed class FeasibilityAnalysisService
{
    public const string IncubatorTenant = "_incubator";

    private readonly IDocumentStore _store;
    private readonly IAgentBackend _backend;
    private readonly string _model;
    private readonly FeasibilityPromptBuilder _prompt;

    /// <summary>كاتِبُ سُطورِ القياس. يُبنى مِن نَفسِ المَخزَنِ لا
    /// يُحقَن — تَماماً كَما يَفعَل <c>AgentService.CreateTenantAsync</c>
    /// مَعَ بَوّابَةِ المَتاجِر: <see cref="StudioTierService"/> غِلافٌ
    /// بِلا حالَةٍ فَوقَ <c>IDocumentStore</c>، وحَقنُه كانَ سَيَربِطَ
    /// عُمرَ هذِه الخِدمَةِ بِعُمرِه بِلا مُقابِل.</summary>
    private readonly StudioTierService _tier;

    // مِلَفّ «Analysis» — وَكيل التَحليل يَستَحِقّ نَموذجاً أَذكى مِن وَكيل
    // الاستوديو (دِراسَة جَدوى كامِلَة بِـ JSON مُهَيكَل)، فَلَه مِلَفُّه
    // المُستَقِلّ: مُزَوِّد ومِفتاح وعُنوان ونَموذج. بِلا تَهيئَة مُسَمّاة
    // يَسقُط إلى Agent:* القَديم.
    public FeasibilityAnalysisService(
        IDocumentStore store, IAgentBackendProvider agents, FeasibilityPromptBuilder prompt)
    {
        _store = store;
        _backend = agents.For(AgentNames.Analysis);
        _model = agents.ModelFor(AgentNames.Analysis);
        _prompt = prompt;
        _tier = new StudioTierService(store);
    }

    /// <summary>النَموذج الفِعليّ لِوَكيل التَحليل (لِلعَرض والتَحَقُّق).</summary>
    public string ModelName => _model;

    public bool IsConfigured => _backend.IsConfigured;

    // ─── Session lifecycle ──────────────────────────────────────────
    public async Task<IncubatorSession> StartAsync(Guid userId, string userName, CancellationToken ct = default)
    {
        var s = new IncubatorSession
        {
            Id = Guid.NewGuid(), OwnerUserId = userId, OwnerName = userName,
            Status = IncubatorStatus.Discovery
        };
        await using var session = _store.LightweightSession(IncubatorTenant);
        session.Store(s);
        await session.SaveChangesAsync(ct);
        return s;
    }

    public async Task<IncubatorSession?> LoadAsync(Guid id, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession(IncubatorTenant);
        return await session.LoadAsync<IncubatorSession>(id, ct);
    }

    public async Task<List<IncubatorSession>> ListForUserAsync(Guid userId, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession(IncubatorTenant);
        return (await session.Query<IncubatorSession>()
            .Where(x => x.OwnerUserId == userId)
            .OrderByDescending(x => x.UpdatedAt).ToListAsync(ct)).ToList();
    }

    public async Task SaveAnswerAsync(Guid id, string questionId, string answer, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession(IncubatorTenant);
        var s = await session.LoadAsync<IncubatorSession>(id, ct);
        if (s is null) return;
        if (questionId == "description") s.ProjectDescription = answer;
        else s.Answers[questionId] = answer;
        s.UpdatedAt = DateTime.UtcNow;

        // عند آخر سؤال، احسب النمط المقترح وبدّل الحالة.
        var answeredCount = s.Answers.Count + (string.IsNullOrEmpty(s.ProjectDescription) ? 0 : 1);
        if (answeredCount >= DiscoveryQuestionBank.Count)
        {
            var suggestion = PatternMatcher.Match(s.Answers);
            s.SuggestedPattern = suggestion.Pattern;
            s.PatternConfidence = suggestion.Confidence;
            s.PatternReasoning = suggestion.ReasoningAr;
            s.Status = IncubatorStatus.PatternSuggested;
        }
        session.Store(s);
        await session.SaveChangesAsync(ct);
    }

    /// <summary>يعيّن الحالة Analyzing فوراً (يُستدعى متزامناً قبل إطلاق
    /// التحليل في الخلفية، حتى تعرض صفحة الدراسة مؤشّر الانتظار بلا سباق).</summary>
    public Task MarkAnalyzingAsync(Guid id, CancellationToken ct = default)
        => SetStatusAsync(id, IncubatorStatus.Analyzing, ct);

    // ─── الحَجز: مَرَّةٌ واحِدَةٌ في نَفسِ المُعامَلَة ─────────────────

    /// <summary>جَوابُ مُحاوَلَةِ الحَجز — مَعجَمٌ مُغلَقٌ بِأَربَعَةٍ
    /// لا خامِسَ لَها.</summary>
    public enum ClaimOutcome
    {
        /// <summary>حُجِزَت — ولِهذا الطَلَبِ وَحدَه أَن يُطلِقَ التَحليل.</summary>
        Claimed,
        /// <summary>لا جَلسَةَ بِهذا المُعَرِّف.</summary>
        NotFound,
        /// <summary>قَيدَ التَحليلِ سَلَفاً — لا تُعادُ ولا تُنفِقُ نِداءً.</summary>
        AlreadyRunning,
        /// <summary>خَسِرَ السِباقَ عَلى نَفسِ التَشغيلَة — وغَيرُه يُطلِقُها.</summary>
        LostRace,
    }

    /// <summary>
    /// <para><b>يَحجُزُ الجَلسَةَ لِتَشغيلَةِ تَحليلٍ واحِدَة — والحَجزُ
    /// والقَلبُ في مُعامَلَةٍ واحِدَة.</b></para>
    ///
    /// <para><b>العِلَّةُ الَّتي كَتَبَت هذِه الدالَّة (‏2026-08-30)</b>:
    /// كانَ المَسارُ <c>MarkAnalyzingAsync</c> ثُمَّ <c>Task.Run</c> —
    /// أَي <b>فَحصٌ ثُمَّ إطلاق</b> بِنافِذَةٍ بَينَهُما. فَخَمسونَ
    /// طَلَباً مُتَوازِياً عَلى <b>نَفسِ المُعَرِّف</b> تَعبُرُ
    /// جَميعاً، وتُطلِقُ خَمسينَ تَحليلاً = <b>مِئَةَ</b> نِداءِ
    /// نَموذَجِ لُغَةٍ (لِأَنّ <see cref="RunAnalysisAsync"/> يُحاوِلُ
    /// مَرَّتَين) عَلى مِفتاحِ المالِك.</para>
    ///
    /// <para><b>والقَرارُ هُنا هُوَ الكِتابَةُ نَفسُها</b>: إدخالُ
    /// <see cref="AnalysisRunClaim"/> بِمِفتاحٍ مُشتَقٍّ مِن
    /// (المُعَرِّف، رَقمِ التَشغيلَة) — و<c>Insert</c> لا
    /// <c>Store</c>، لِأَنّ <c>Store</c> يَكتُبُ فَوقَ المَوجودِ فَلا
    /// يَصطَدِمُ بِأَحَد. الخاسِرُ يَرتَدُّ بِمُعامَلَتِه كامِلَةً،
    /// فَلا يُقلَبُ لَه شَيءٌ ولا يُنفَقُ لَه نِداء.</para>
    ///
    /// <para><b>ويَفشَلُ مُغلَقاً</b>: كُلُّ ما لَيسَ
    /// <see cref="ClaimOutcome.Claimed"/> يَعني «لا تُطلِق».</para>
    /// </summary>
    public async Task<ClaimOutcome> TryClaimAnalysisAsync(Guid id, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession(IncubatorTenant);
        var s = await session.LoadAsync<IncubatorSession>(id, ct);
        if (s is null) return ClaimOutcome.NotFound;

        // جَلسَةٌ قَيدَ التَحليلِ لا تُعاد — ولا نُصَعِّدُ الأَمرَ إلى
        // اصطِدامِ مِفتاحٍ لِنَقولَ ما تَقولُه الحالَةُ نَفسُها.
        if (s.Status == IncubatorStatus.Analyzing && !IsStale(s, DateTime.UtcNow))
            return ClaimOutcome.AlreadyRunning;

        var attempt = s.AnalysisRuns;
        session.Insert(new AnalysisRunClaim
        {
            Id = ClaimId(id, attempt), SessionId = id, Attempt = attempt,
        });

        s.AnalysisRuns = attempt + 1;
        s.Status = IncubatorStatus.Analyzing;
        s.UpdatedAt = DateTime.UtcNow;
        session.Store(s);

        try
        {
            await session.SaveChangesAsync(ct);
            return ClaimOutcome.Claimed;
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            return ClaimOutcome.LostRace;
        }
    }

    /// <summary>مِفتاحُ تَشغيلَةٍ واحِدَة. <c>N</c> بِلا شَرَطات
    /// لِيَبقى المِفتاحُ قَصيراً وثابِتَ الشَكل.</summary>
    public static string ClaimId(Guid sessionId, int attempt)
        => $"{sessionId:N}#{attempt}";

    /// <summary>
    /// <para><b>مَتى تُعتَبَرُ تَشغيلَةٌ «قَيدَ التَحليل» مَهجورَة</b> —
    /// و<b>الرَقَمُ مَحسوبٌ مِنَ الكودِ لا مُختَرَعاً</b> (القاعِدَة
    /// ١٦): مُهلَةُ عَميلِ HTTP في الخَلفِيّاتِ الثَلاثِ جَميعاً
    /// <c>60</c> ثانِيَة (‏<c>AgentBackends.cs</c>)، و
    /// <see cref="RunAnalysisAsync"/> يُحاوِلُ <b>مَرَّتَين</b> —
    /// فَأَقصى عُمرٍ مُمكِنٍ لِتَشغيلَةٍ حَيَّةٍ دَقيقَتان. والضِعفُ
    /// هامِشُ أَمانٍ لِلشَبَكَةِ وقاعِدَةِ البَيانات.</para>
    ///
    /// <para><b>ولِماذا يوجَدُ هذا أَصلاً</b>: بِلا مُهلَةِ تَقادُمٍ،
    /// عَمَلِيَّةٌ تَموتُ في مُنتَصَفِ التَحليلِ تَترُكُ الدِراسَةَ
    /// <c>Analyzing</c> <b>إلى الأَبَد</b> — فَيَصيرُ مَنعُ
    /// التَكرارِ حَبساً، ونَقرَةُ «إعادَة» زِرّاً لا يَفعَلُ شَيئاً.
    /// وذاكَ عَطَبٌ أَسوَأُ مِنَ الَّذي جاءَ يُعالِجُه.</para>
    ///
    /// <para><b>ولا يَفتَحُ هذا نافِذَةَ سِباقٍ ثانِيَة</b>: التَقادُمُ
    /// يُقَرِّرُ مَن <b>يُحاوِل</b>، والفَرادَةُ تُقَرِّرُ مَن
    /// <b>يَفوز</b> — فَخَمسونَ طَلَباً عَلى جَلسَةٍ مُتَقادِمَةٍ
    /// يَقرَؤونَ نَفسَ رَقمِ التَشغيلَةِ ويَتَصادَمونَ عَلى نَفسِ
    /// المِفتاح.</para>
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(4);

    internal static bool IsStale(IncubatorSession s, DateTime nowUtc)
        => nowUtc - s.UpdatedAt >= StaleAfter;

    /// <summary>
    /// <para><b>أَخَرقُ فَرادَةٍ هذا؟</b> — ويُفحَصُ بِشَكلَينِ لا
    /// بِواحِد، و<b>الثاني كَشَفَه القياسُ لا التَخمين</b>.</para>
    ///
    /// <para><b>الكِلفَةُ الَّتي كَتَبَت هذا التَعليق (‏2026-08-30)</b>:
    /// كانَت النُسخَةُ الأولى تَفحَصُ
    /// <c>Npgsql.PostgresException{SqlState:"23505"}</c> وَحدَه — وهُوَ
    /// الشَكلُ المَكتوبُ في <c>MarketplaceTemplateExtensions</c>
    /// لِتَضارُبِ التَيّار، فَبَدا القِياسُ عَلَيه سَليماً. وأَوَّلُ
    /// تَصادُمٍ حَقيقيٍّ مَحقونٍ في البُرهانِ الحَيِّ أَظهَرَ أَنّ
    /// Marten <b>يُحَوِّلُ</b> الاستِثناءَ عِندَ الإدخالِ إلى
    /// <c>JasperFx.DocumentAlreadyExistsException</c>
    /// (‏<c>ExceptionTransformExtensions.TransformAndThrow</c>) —
    /// و<c>InnerException</c> لا يَحمِلُ الأَصل. فَالمُرَشِّحُ كانَ
    /// <b>لا يُطابِقُ شَيئاً أَبَداً</b>، والخاسِرُ كانَ سَيَرتَدُّ
    /// بِـ<c>500</c> بَدَلَ رَفضٍ نَظيف.</para>
    ///
    /// <para><b>والدَرسُ في سَطر</b>: الفَرعُ الَّذي لَم يُنَفَّذ
    /// دَعوى. والبُرهانُ الأَوَّلُ أَعطى «فائِزٌ واحِد» وهُوَ
    /// <c>lost=0</c> — أَي أَنّ الفَرعَ الَّذي يَقومُ عَلَيه العِلاجُ
    /// كُلُّه لَم يَمُرَّ بِه أَحَد. (القاعِدَة ١٠.)</para>
    ///
    /// <para><b>ويُفحَصُ بِالاسمِ لا بِالنَوع</b>: النَوعُ في
    /// <c>JasperFx.Core</c> ولا يُشيرُ إلَيه هذا المَشروعُ
    /// مُباشَرَةً، وإضافَةُ مَرجِعٍ لِأَجلِ <c>catch</c> واحِدٍ
    /// تَجريدٌ يَسبِقُ مُستَهلِكَه. ونَفسُ شَكلِ
    /// <c>IsStreamVersionConflict</c> في مِلَفِّ النِقاط.</para>
    ///
    /// <para><b>وما لا يَبتَلِعُه</b>: أَيُّ فَشَلٍ آخَرَ يُرفَعُ كَما
    /// هُوَ. مُرَشِّحٌ يَبتَلِعُ ما لا يَفهَمُ يَجعَلُ انقِطاعَ
    /// الشَبَكَةِ يُقرَأُ «خَسِرَ السِباق» — فَتَبدو الجَلسَةُ
    /// مَحجوزَةً لِأَحَدٍ لا وُجودَ لَه.</para>
    /// </summary>
    private static bool IsDuplicateKey(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e.GetType().Name == "DocumentAlreadyExistsException") return true;
            if (e is Npgsql.PostgresException { SqlState: "23505" }) return true;
        }
        return false;
    }

    /// <summary>
    /// يُعيد توليد قِسم واحِد مِن الدِراسَة (مَثَلاً <c>risks</c> أَو
    /// <c>marketSizing</c>) بِناءً على مُلاحَظَة المُستَخدِم، ويَدمُجه في
    /// JSON الدِراسَة الحاليّ. يَكون أَسرَع وأَرخَص مِن إعادَة تَوليد الكُلّ
    /// لِأَنّ الـ output أَصغَر.
    /// </summary>
    public async Task RefineSectionAsync(
        Guid id, string sectionKey, string feedback, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sectionKey)) return;
        var s = await LoadAsync(id, ct);
        if (s is null || s.Status != IncubatorStatus.Completed || s.AnalysisJson is null) return;

        var sector = s.Answers.TryGetValue("sector", out var sec) ? sec : "other";
        var systemPrompt =
            $"أنت محلّل أعمال خبير في السوق السعودي. لَدَيكَ دِراسَة جَدوى مَوجودَة، " +
            $"والمَطلوب إعادَة تَوليد قِسم واحِد فَقَط ({sectionKey}) بِناءً على مُلاحَظَة المُستَخدِم. " +
            "حافِظ على نَفس بِنيَة JSON لِهذا القِسم (انظر القِسم الحاليّ). " +
            "أَجِب بِـ JSON يَحتَوي مِفتاحاً واحِداً هو نَفس اسم القِسم وقيمَتُه القيمَة الجَديدَة فَقَط، بِلا شَيء آخَر. " +
            "اِستَخدِم بيانات السوق المُتاحَة وتَجَنَّب اختِراع أَرقام.\n\n" +
            "# سياق السوق:\n" + _prompt._dataMarket() +
            "\n# دروس فشل سعوديَّة:\n" + _prompt.FailuresForSector(sector);

        var userMsg =
            $"## القِسم الحاليّ ({sectionKey}):\n" + ExtractSection(s.AnalysisJson, sectionKey) +
            $"\n\n## مُلاحَظَة لِإعادَة التَّوليد:\n{feedback}" +
            $"\n\nأَجِب بِـ JSON بِالشَّكل: {{ \"{sectionKey}\": ... }}";

        var messages = new List<AgentMessage>
        {
            new("user", userMsg, null, null)
        };
        var req = new AgentRequest(systemPrompt, messages,
            Array.Empty<AgentToolDef>(), _model, MaxTokens: 3000);
        var resp = await _backend.CallAsync(req, ct);

        // القياسُ **قَبلَ** فَرعِ الخَطَأ: تَحسينٌ فَشِلَ أَنفَقَ
        // توكناتٍ كَتَحسينٍ نَجَح، وسِجِلٌّ يُسقِطُ الفاشِلَ يُخفي
        // إنفاقاً وَقَع.
        await _tier.RecordModelCallAsync(Metering.ModelCallRecord.For(
            IncubatorTenant, s.OwnerUserId, _backend.ProviderName, _model,
            Metering.ModelCallOperation.Refine, resp.Usage, resp.Error is null), ct);

        if (resp.Error is not null) return;
        var newJson = ExtractJson(resp.Text);
        if (newJson is null) return;

        // ادمُج: استَبدِل المِفتاح فَقَط، احفَظ الباقي كَما هو.
        var merged = MergeSection(s.AnalysisJson, newJson, sectionKey);
        if (merged is null) return;

        await using var session = _store.LightweightSession(IncubatorTenant);
        var fresh = await session.LoadAsync<IncubatorSession>(id, ct) ?? s;
        fresh.AnalysisJson = merged;
        fresh.AnalysisQualityScore = ScoreQuality(merged, sector);
        fresh.UpdatedAt = DateTime.UtcNow;
        session.Store(fresh);
        await session.SaveChangesAsync(ct);
    }

    internal static string ExtractSection(string json, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(key, out var v))
                return v.GetRawText();
        }
        catch { }
        return "{}";
    }

    /// <summary>يَستَبدِل قِسماً واحِداً في JSON بِالقيمَة الجَديدَة (الَّتي
    /// تَأتي كَ كائِن بِمِفتاح واحِد). يُعيد JSON كامِلاً جَديداً أَو null.</summary>
    internal static string? MergeSection(string originalJson, string newSectionJson, string key)
    {
        try
        {
            using var origDoc = JsonDocument.Parse(originalJson);
            using var newDoc = JsonDocument.Parse(newSectionJson);
            if (!newDoc.RootElement.TryGetProperty(key, out var newValue))
                return null;

            using var stream = new MemoryStream();
            using (var w = new System.Text.Json.Utf8JsonWriter(stream,
                new System.Text.Json.JsonWriterOptions { Indented = false }))
            {
                w.WriteStartObject();
                foreach (var prop in origDoc.RootElement.EnumerateObject())
                {
                    if (prop.Name == key)
                    {
                        w.WritePropertyName(key);
                        newValue.WriteTo(w);
                    }
                    else prop.WriteTo(w);
                }
                // إن كانَ المِفتاح غَير مَوجود أَصلاً، أَضِفه.
                if (!origDoc.RootElement.TryGetProperty(key, out _))
                {
                    w.WritePropertyName(key);
                    newValue.WriteTo(w);
                }
                w.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch { return null; }
    }

    // ─── Analysis ───────────────────────────────────────────────────
    /// <summary>يشغّل التحليل: prompt → LLM → JSON صالح → حفظ. يُعيد الجلسة المُحدَّثة.</summary>
    public async Task<IncubatorSession> RunAnalysisAsync(Guid id, CancellationToken ct = default)
    {
        var s = await LoadAsync(id, ct);
        if (s is null) throw new InvalidOperationException("session not found");

        await SetStatusAsync(id, IncubatorStatus.Analyzing, ct);

        var sector = s.Answers.TryGetValue("sector", out var sec) ? sec : "other";
        var systemPrompt = _prompt.Build(s, _prompt.FailuresForSector(sector));
        var userMsg = FeasibilityPromptBuilder.BuildUserMessage(s);

        string? json = null;
        string? lastError = null;
        for (var attempt = 0; attempt < 2 && json is null; attempt++)
        {
            var messages = new List<AgentMessage>
            {
                new("user", attempt == 0 ? userMsg
                    : userMsg + "\n\n[تذكير: أعد JSON صالحاً فقط مطابقاً للـ schema، بلا أي نص آخر.]",
                    null, null)
            };
            var req = new AgentRequest(systemPrompt, messages,
                Array.Empty<AgentToolDef>(), _model, MaxTokens: 8000);
            var resp = await _backend.CallAsync(req, ct);

            // ─── سَطرُ قياسٍ **لِكُلِّ مُحاوَلَة**، الفاشِلَةِ قَبلَ
            //     الناجِحَة ──────────────────────────────────────────
            // وهذا أَهَمُّ مَوضِعٍ في المَوجَةِ كُلِّها: الحَلقَةُ
            // تُحاوِلُ **مَرَّتَين**، والمُحاوَلَةُ الأولى الفاشِلَةُ
            // أَنفَقَت `MaxTokens: 8000` مِثلَ الثانِيَة تَماماً — بَل
            // رُبَّما أَكثَر، فَرَدٌّ غَيرُ صالِحِ JSON هو رَدٌّ
            // **مُكتَمِلُ التَوليد**. فَتَسجيلُ الناجِحَةِ وَحدَها
            // يُخفي حَتّى نِصفَ الفاتورَة، ويَجعَلُ التَقريرَ يَقولُ
            // «تَحليلٌ واحِدٌ بِكَذا» عَن نِدائَينِ اثنَين.
            //
            // ولِذلك يَقَعُ التَسجيلُ **قَبلَ** `continue` لا بَعدَه.
            // ويَقيسُ التَرتيبَ
            // `ModelUsageMeteringTests.The_analysis_loop_records_before_it_skips_a_failed_attempt`.
            await _tier.RecordModelCallAsync(Metering.ModelCallRecord.For(
                IncubatorTenant, s.OwnerUserId, _backend.ProviderName, _model,
                Metering.ModelCallOperation.Analyze, resp.Usage, resp.Error is null), ct);

            if (resp.Error is not null) { lastError = resp.Error; continue; }
            json = ExtractJson(resp.Text);
            if (json is null) lastError = "الردّ لم يكن JSON صالحاً.";
        }

        await using var session = _store.LightweightSession(IncubatorTenant);
        var fresh = await session.LoadAsync<IncubatorSession>(id, ct) ?? s;
        fresh.PromptVersion = FeasibilityPromptBuilder.Version;
        fresh.UpdatedAt = DateTime.UtcNow;
        if (json is null)
        {
            fresh.Status = IncubatorStatus.Failed;
            fresh.AnalysisError = FormatError(lastError);
        }
        else
        {
            fresh.AnalysisJson = json;
            fresh.AnalysisQualityScore = ScoreQuality(json, sector);
            fresh.AnalysisError = null;
            fresh.Status = IncubatorStatus.Completed;
        }
        session.Store(fresh);
        await session.SaveChangesAsync(ct);
        return fresh;
    }

    private async Task SetStatusAsync(Guid id, IncubatorStatus status, CancellationToken ct)
    {
        await using var session = _store.LightweightSession(IncubatorTenant);
        var s = await session.LoadAsync<IncubatorSession>(id, ct);
        if (s is null) return;
        s.Status = status; s.UpdatedAt = DateTime.UtcNow;
        session.Store(s);
        await session.SaveChangesAsync(ct);
    }

    /// <summary>يُحَوِّل خَطَأ الـ provider الخام لِرِسالَة مَفهومَة + يَقصّ
    /// الطول. يُبرِز حالات الحِصَّة (429) بِشكل صَريح.</summary>
    internal static string FormatError(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "فشل غير معروف.";
        if (raw.Contains("429") || raw.Contains("quota", StringComparison.OrdinalIgnoreCase)
                                || raw.Contains("rate", StringComparison.OrdinalIgnoreCase))
            return "تَجاوُز حِصَّة مُزَوِّد الـ LLM. تَحَقَّق مِن باقَة الـ API لَدَيك أَو بَدِّل المُزَوِّد في الإعدادات (Agent:Provider).";
        const int max = 300;
        return raw.Length <= max ? raw : raw[..max] + "…";
    }

    // ─── Helpers ─────────────────────────────────────────────────────
    /// <summary>يستخرج كتلة JSON من ردّ قد يحوي markdown fences أو نصاً حوله.</summary>
    internal static string? ExtractJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        // أزل ```json ... ``` إن وُجِدت.
        if (t.StartsWith("```"))
        {
            var firstNl = t.IndexOf('\n');
            if (firstNl > 0) t = t[(firstNl + 1)..];
            if (t.EndsWith("```")) t = t[..^3];
            t = t.Trim();
        }
        var start = t.IndexOf('{');
        var end = t.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        var candidate = t.Substring(start, end - start + 1);
        try { using var _ = JsonDocument.Parse(candidate); return candidate; }
        catch { return null; }
    }

    /// <summary>درجة جودة 0-100: اكتمال الأقسام + استخدام السياق السعودي.</summary>
    internal static int ScoreQuality(string json, string sector)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string[] required = { "summary", "marketSizing", "customerSegments", "competitors",
                "revenueModel", "costStructure", "financialProjection", "risks",
                "lessonsFromFailures", "roadmap", "kpis", "recommendations" };
            var present = required.Count(k => root.TryGetProperty(k, out _));
            var completeness = (int)(present / (double)required.Length * 70);

            // إشارات السياق المحلي: ذكر "ريال" أو "السعودي" أو مخاطر تنظيمية.
            var raw = json;
            var localSignals = 0;
            if (raw.Contains("ريال")) localSignals += 10;
            if (raw.Contains("regulatory") || raw.Contains("تنظيم")) localSignals += 10;
            if (root.TryGetProperty("lessonsFromFailures", out var lf)
                && lf.ValueKind == JsonValueKind.Array && lf.GetArrayLength() > 0) localSignals += 10;

            return Math.Min(100, completeness + localSignals);
        }
        catch { return 0; }
    }
}
