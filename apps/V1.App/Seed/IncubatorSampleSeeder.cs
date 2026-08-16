using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;

namespace ACommerce.V1.App.Seed;

/// <summary>
/// <para><b>بَذرَةٌ لا مِفتاح.</b> ثَلاثُ شاشاتٍ ثَقيلَة —
/// <c>StudioStudy</c> و<c>IncubatorStudy</c> و<c>StudioStudyPrint</c> —
/// تَقرَأُ كُلُّها <see cref="IncubatorSession.AnalysisJson"/>، ولا
/// تُصَيِّرُ حَرفاً بِلا جَلسَةٍ مُكتَمِلَة. وصِفرُ جَلسَةٍ في القاعِدَة
/// (مَقيس)، وتَوليدُ الدِراسَة يَمُرّ بِمُزَوِّد LLM حَقيقيّ — لا مُهايِئَ
/// وَهمِيّاً في <c>AgentBackendFactory</c>.</para>
///
/// <para>فَالبابُ لَه طَريقان: جَولَةٌ بِمِفتاح API تُنتِج دِراسَةً
/// حَقيقِيَّة، أَو <b>بَذرَةُ بِنيَة</b>. والثانِيَة هي المُنَفَّذَة هُنا:
/// أَرخَص، ولا تَحتاج مُزَوِّداً، ولا شَبَكَة، ولا مِفتاحاً.</para>
///
/// <para><b>وما تَبذُرُه بِنيَةٌ لا مَعرِفَة.</b> القاعِدَة ١٦ تَمنَع
/// اختِراعَ بَيانات مُنتَج، ودِراسَةُ الجَدوى بِعَينِها بَياناتُ مُنتَج:
/// رَقمٌ يَبدو حَقيقِيّاً في حَقلِ «حَجم السوق» يَصير مَرجِعاً لِمَن
/// يَقرَؤُه. فَكُلُّ قيمَةٍ هُنا تَبدَأ بِـ<see cref="Mark"/> ولا تَحمِل
/// رَقَماً واحِداً يُشبِه التَقدير. المَقصودُ أَنّ **كُلَّ قِسمٍ يُصَيَّر**،
/// لا أَن يُقرَأ ما فيه.</para>
///
/// <para><b>خَلفَ مُتَغَيِّر بيئَة</b>، على نَمَط
/// <see cref="TestDataSeeder"/> تَماماً:</para>
/// <code>
/// export INCUBATOR_SAMPLE_SEED=1
/// dotnet run --project apps/V1.App --urls=http://localhost:5050
/// </code>
/// <para>بِلا المُتَغَيِّر **لا يَعمَل شَيء** — لا قِراءَة ولا كِتابَة.
/// ومَعَه يُكتَب مُستَنَدٌ واحِدٌ بِـ<see cref="SampleId"/> ثابِت،
/// فَالتَشغيلُ مَرَّتَين لا يُنتِج جَلسَتَين. وحَذفُها سَطرٌ واحِد:
/// <c>delete from platform.mt_doc_incubatorsession where id = '…'</c>.</para>
///
/// <para><b>ومالِكُها لَيسَ Guid مَكتوباً</b>: يُبحَث عَن
/// <see cref="StudioUser"/> صاحِبِ <c>PLATFORM_ADMIN_PHONE</c>، فَإن لَم
/// يوجَد فَأَقدَمُ مُستَخدِمِ استوديو. وبِلا مُستَخدِمِ استوديو
/// **لا تُبذَر** — جَلسَةٌ بِمالِكٍ لا وُجودَ لَه لا تَفتَح شاشَة، إذ
/// <c>StudioStudy</c> يَشتَرِط <c>OwnerUserId == Auth.UserId</c>.</para>
/// </summary>
public static class IncubatorSampleSeeder
{
    /// <summary>مُعَرِّفٌ ثابِت — بِه تَصير البَذرَةُ idempotent، وبِه
    /// تُكتَب عَناوينُ اللَقطَة في <c>capture-appearance.sh</c> بِلا
    /// اكتِشافٍ في وَقت التَشغيل.</summary>
    public static readonly Guid SampleId =
        Guid.Parse("5a3e1d00-0000-4000-8000-00000000ec01");

    /// <summary>وَسمُ العَيِّنَة. يَتَصَدَّر **كُلّ** قيمَةٍ مَنصوصَة،
    /// فَلا تُقرَأ سَطراً مِن دِراسَةٍ ولَو بِالخَطَأ.</summary>
    private const string Mark = "عَيِّنَة بِنيَة — لا تَحليلَ هُنا";

    public static async Task RunAsync(IServiceProvider services)
    {
        var store = services.GetRequiredService<IDocumentStore>();

        // ── المالِك: صاحِبُ هاتِف المُشرِف، وإلّا أَقدَمُ مُستَخدِمِ استوديو ──
        await using var studioQs = store.QuerySession(StudioAuth.Tenant);
        var adminPhone = Environment.GetEnvironmentVariable("PLATFORM_ADMIN_PHONE");
        StudioUser? owner = null;
        if (!string.IsNullOrWhiteSpace(adminPhone))
            owner = (await studioQs.Query<StudioUser>()
                .Where(u => u.Phone == adminPhone).Take(1).ToListAsync()).FirstOrDefault();
        owner ??= (await studioQs.Query<StudioUser>()
            .OrderBy(u => u.CreatedAt).Take(1).ToListAsync()).FirstOrDefault();

        if (owner is null)
        {
            Console.WriteLine("[IncubatorSample] لا مُستَخدِمَ استوديو — لا بَذرَة.");
            return;
        }

        // ‏**مُستَأجِرانِ لا واحِد، وهذا مَوضِعُ زَلَّةٍ وَقَعَت**: مُستَخدِمو
        // الاستوديو في `_studio`، وجَلَساتُ الحاضِنَة في `_incubator`.
        // البَذرَةُ الأولى كَتَبَت الجَلسَةَ في `_studio` فَخَرَجَت
        // الصَفحَةُ «الجَلسَة غَير مَوجودَة» — والقِراءَةُ هي الحَكَم:
        // ‏`FeasibilityAnalysisService.LoadAsync` تَفتَح `IncubatorTenant`.
        await using var session = store.LightweightSession(
            ACommerce.Templates.Customer.Marketplace.Services.Incubator
                .FeasibilityAnalysisService.IncubatorTenant);
        var existing = await session.LoadAsync<IncubatorSession>(SampleId);
        if (existing is not null)
        {
            Console.WriteLine($"[IncubatorSample] قائِمَة أَصلاً: {SampleId}");
            return;
        }

        // زَمَنٌ ثابِت لا `UtcNow`: الشاشاتُ تَطبَع التَواريخ، ولَقطَةُ
        // الأَساس تُقارَن **بايتاً بِبايت** — فَوَقتُ الإقلاع كانَ
        // سَيَجعَل كُلّ التِقاطٍ مُختَلِفاً عَن سابِقِه.
        var at = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        session.Store(new IncubatorSession
        {
            Id = SampleId,
            OwnerUserId = owner.Id,
            OwnerName = owner.Phone,
            Status = IncubatorStatus.Completed,
            StartedAt = at,
            UpdatedAt = at,
            ProjectDescription = $"{Mark} — وَصفُ الفِكرَة",
            Answers = new Dictionary<string, string>
            {
                ["sector"]    = "other",
                ["stage"]     = "idea",
                ["audience"]  = "b2c",
                ["budget"]    = "unknown",
            },
            SuggestedPattern  = "custom",
            PatternConfidence = "low",
            PatternReasoning  = $"{Mark} — سَبَبُ النَمَط",
            PromptVersion     = FeasibilityPromptBuilder.Version,
            AnalysisQualityScore = 0,
            AnalysisJson = BuildStructureOnlyJson()
        });
        await session.SaveChangesAsync();
        Console.WriteLine($"[IncubatorSample] ✅ بُذِرَت جَلسَةُ عَيِّنَة {SampleId} لِـ{owner.Id}");
    }

    /// <summary><para>‏JSON بِـ<b>كامِل</b> بِنيَة
    /// <see cref="FeasibilityPromptBuilder.OutputSchema"/>: ثَلاثَةَ عَشَرَ
    /// مِفتاحاً أَعلى، وعُنصُرانِ في كُلّ مَصفوفَة (واحِدٌ لا يَكشِف
    /// تَكرارَ الحَلَقَة، وثَلاثَةٌ زيادَةٌ بِلا فائِدَة).</para>
    ///
    /// <para>و<c>durationWeeks</c> رَقمٌ بِالمُخَطَّط، فَيُكتَب <c>0</c>:
    /// أَيُّ رَقَمٍ آخَر تَقديرُ مُدَّةٍ مُختَرَع.</para></summary>
    private static string BuildStructureOnlyJson()
    {
        // مَكتوبٌ بِيَدٍ لا بِمُسَلسِل: هذا **عَقدُ مُخَطَّط** يُقرَأ
        // ويُقابَل بِـ`OutputSchema` سَطراً بِسَطر، لا كائِنُ نَقلٍ لَه
        // نَوع. وأَيُّ حَقلٍ يُضاف هُناكَ يُضاف هُنا بِالعَين.
        const string m = Mark;
        return $$"""
        {
          "summary": {
            "marketSizeSar": "{{m}}",
            "estimatedCustomersYear1": "{{m}}",
            "requiredInvestmentSar": "{{m}}",
            "verdict": "{{m}}"
          },
          "marketSizing": {
            "tam": "{{m}}",
            "sam": "{{m}}",
            "som": "{{m}}",
            "reasoning": "{{m}}"
          },
          "customerSegments": [
            { "name": "{{m}} — شَريحَة أولى", "description": "{{m}}", "sizeEstimate": "{{m}}" },
            { "name": "{{m}} — شَريحَة ثانِيَة", "description": "{{m}}", "sizeEstimate": "{{m}}" }
          ],
          "competitors": [
            { "name": "{{m}} — مُنافِس أَوَّل", "positioning": "{{m}}", "gap": "{{m}}" },
            { "name": "{{m}} — مُنافِس ثانٍ", "positioning": "{{m}}", "gap": "{{m}}" }
          ],
          "revenueModel": {
            "model": "{{m}}",
            "pricingSar": "{{m}}",
            "unitEconomics": "{{m}}"
          },
          "costStructure": {
            "launchSar": "{{m}}",
            "monthlyOpsSar": "{{m}}",
            "cacSar": "{{m}}",
            "ltvSar": "{{m}}"
          },
          "financialProjection": {
            "year1RevenueSar": "{{m}}",
            "breakEvenMonths": "{{m}}",
            "scenario": "{{m}}"
          },
          "risks": [
            { "title": "{{m}} — خَطَر أَوَّل", "category": "market", "impact": "low", "likelihood": "low", "mitigation": "{{m}}" },
            { "title": "{{m}} — خَطَر ثانٍ", "category": "operational", "impact": "low", "likelihood": "low", "mitigation": "{{m}}" }
          ],
          "lessonsFromFailures": [
            "{{m}} — دَرسٌ أَوَّل",
            "{{m}} — دَرسٌ ثانٍ"
          ],
          "roadmap": [
            { "phase": "{{m}} — مَرحَلَة أولى", "durationWeeks": 0, "goal": "{{m}}" },
            { "phase": "{{m}} — مَرحَلَة ثانِيَة", "durationWeeks": 0, "goal": "{{m}}" }
          ],
          "kpis": [
            { "name": "{{m}} — مُؤَشِّر أَوَّل", "target": "{{m}}" },
            { "name": "{{m}} — مُؤَشِّر ثانٍ", "target": "{{m}}" }
          ],
          "recommendedPattern": {
            "pattern": "custom",
            "reasoning": "{{m}}",
            "timeToLaunch": "{{m}}"
          },
          "recommendations": [
            "{{m}} — تَوصِيَة أولى",
            "{{m}} — تَوصِيَة ثانِيَة"
          ]
        }
        """;
    }
}
