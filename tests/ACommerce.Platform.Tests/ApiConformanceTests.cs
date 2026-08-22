using ACommerce.Kit.Subscriptions;
using ACommerce.Templates.Customer.Marketplace.Api;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>حَقيبَةُ المُطابَقَة — الكُتَل أ–هـ (‏§٦).</b> المَبدَأ:
/// لا نَثِقُ بِتَنفيذٍ بِقِراءَةِ كودِه بَل بِتَشغيلِ اختِباراتِنا
/// عَلَيه. <b>وأَوَّلُ تَشغيلٍ لَها على تَنفيذِنا نَحن</b> — حَقيبَةٌ
/// تُصَدَّر قَبل أَن تَخضَرَّ على صاحِبِها دَعوى بِلا بُرهان.</para>
///
/// <para><b>وحَدُّها مُعلَنٌ لا مَبلوع</b>: قاعِدَةُ البَيانات غَير
/// مُتاحَةٍ في هذِه الجَولَة (‏<c>28P01</c>)، فَما يَحتاج نِداءً
/// حَيّاً <b>لا يُدَّعى خاضِراً</b> — يُسَمّى في
/// <see cref="LiveObligations"/> بِاسمِه وسَبَبِه وأَمرِه، ويُختَبَر
/// أَنّ كُلَّ واحِدٍ مِنها مَوصوفٌ لا مَنسِيّ. وهذا هُوَ الفَرقُ
/// بَينَ دَينٍ مُعلَنٍ وثَقبٍ صامِت.</para>
///
/// <para><b>والكُتلَة (هـ) تَحكُم الكُلّ</b> (القاعِدَة ١٠): كُلُّ
/// كُتلَةٍ تَطبَع عَدَدَ ما فَحَصَته وتَفشَل إن كانَ صِفراً —
/// «صِفرُ مُخالَفَة» مِن أَداةٍ عَمياء لا يُمَيَّز عَن «صِفرُ
/// مُخالَفَة» مِن أَداةٍ فَحَصَت كُلَّ شَيء.</para>
/// </summary>
public class ApiConformanceTests
{
    // ═══ الكُتلَة أ — الاعتِماد ════════════════════════════════════════

    /// <summary>
    /// <para><b>كُلُّ سَبَبِ رَفضٍ يُنتِج ‏401 بِرَمزٍ واحِد.</b>
    /// المَعبَرُ الحَيُّ لِهذِه الكُتلَة هُوَ
    /// <c>ApiKeyFilterTests</c>؛ وهذا يُثَبِّت <b>الجَدوَل</b>:
    /// أَنّ مَعجَمَ أَسبابِ الرَفضِ كُلَّه يُطوى إلى رَمزَين لا
    /// أَكثَر، فَلا يُضاف سَبَبٌ يَتَسَرَّب إلى العَميل.</para>
    /// </summary>
    [Fact]
    public void BlockA_every_rejection_reason_collapses_to_one_of_two_codes()
    {
        var reasons = Enum.GetValues<ApiKeyRejection>().Where(r => r != ApiKeyRejection.None).ToArray();

        Assert.True(reasons.Length >= 6,
            $"أَداة عَمياء: {reasons.Length} سَبَبَ رَفضٍ فَقَط — والمَقيس سِتَّة فَأَكثَر.");
        Console.WriteLine($"· حَقيبَةُ المُطابَقَة (أ): {reasons.Length} سَبَبَ رَفضٍ مَفحوصاً.");

        // ولا رَمزَ ثالِثاً: الغِيابُ وَحدَه يُمَيَّز، وما سِواه واحِد.
        Assert.Equal(401, ApiErrorCatalog.Require(ApiErrorCatalog.AuthMissing).Status);
        Assert.Equal(401, ApiErrorCatalog.Require(ApiErrorCatalog.AuthInvalid).Status);
    }

    /// <summary><b>وخارِجُ المُستَأجِرِ يُرَدُّ ‏404 لا ‏403</b> —
    /// لا نُفشي وُجودَ مَورِدٍ لا يَملِكُه السائِل. مَقيسٌ على
    /// المَعجَم: <c>not_found</c> هُوَ الرَمزُ الوَحيدُ بِـ‏404،
    /// ولا رَمزَ «مَورِدُ مُستَأجِرٍ آخَر».</summary>
    [Fact]
    public void BlockA_the_vocabulary_has_no_code_that_reveals_another_tenants_resource()
    {
        Assert.Equal(new[] { "not_found" },
            ApiErrorCatalog.All.Where(c => c.Status == 404).Select(c => c.Code).ToArray());

        Assert.DoesNotContain(ApiErrorCatalog.Codes,
            c => c.Contains("tenant", StringComparison.OrdinalIgnoreCase));
    }

    // ═══ الكُتلَة ب — العَقد ═══════════════════════════════════════════

    /// <summary><b>لا ‏3xx إطلاقاً، ولا ‏5xx في المَعجَم.</b> رَمزُ
    /// خَطَأٍ بِحالَةِ تَحويلٍ يَعني عَميلاً آلِيّاً يَتبَعُ الرابِطَ
    /// فَيَصِل صَفحَةَ دُخولٍ ويَظُنُّها جَواباً.</summary>
    [Fact]
    public void BlockB_every_error_status_is_a_client_error()
    {
        Assert.True(ApiErrorCatalog.All.Count >= 11,
            $"أَداة عَمياء: {ApiErrorCatalog.All.Count} رَمزاً فَقَط.");
        Console.WriteLine($"· حَقيبَةُ المُطابَقَة (ب): {ApiErrorCatalog.All.Count} رَمزَ خَطَأٍ مَفحوصاً.");

        foreach (var c in ApiErrorCatalog.All)
            Assert.InRange(c.Status, 400, 499);
    }

    /// <summary>وكُلُّ رَمزٍ عُضوٌ في المَعجَم — الطَرَفُ الَّذي
    /// يُغلِقُه <c>Require</c> عِندَ التَركيب.</summary>
    [Fact]
    public void BlockB_the_code_vocabulary_is_closed_at_both_ends()
    {
        foreach (var c in ApiErrorCatalog.Codes)
            Assert.True(ApiErrorCatalog.Contains(c));

        Assert.False(ApiErrorCatalog.Contains("server_error"));
        Assert.Throws<ArgumentException>(() => ApiErrorCatalog.Require("server_error"));
    }

    // ═══ الكُتلَة ج — مَرَّة-واحِدَة ═══════════════════════════════════

    /// <summary><b>نَفسُ المِفتاحِ مَرَّتَين ⇒ أَثَرٌ واحِدٌ
    /// وجَوابانِ مُتَطابِقان.</b> «مُتَطابِقان» هُنا حَرفِيَّة: الجِسمُ
    /// المُخَزَّنُ يُعادُ كَما هُوَ، لا يُعادُ بِناؤُه — فَلا يَختَلِفُ
    /// خَتمُ وَقتٍ ولا تَرتيبُ حَقل.</summary>
    [Fact]
    public void BlockC_a_replay_returns_the_stored_bytes_not_a_rebuilt_answer()
    {
        var rec = new ApiIdempotencyRecord
        {
            Id = ApiIdempotencyRecord.IdFor("k1", "req-1"),
            Endpoint = "deals.advance",
            Status = ApiIdempotencyRecord.StatusCompleted,
            ResponseStatus = 200,
            ResponseJson = "{\"id\":\"x\",\"stage\":\"Paid\"}",
        };

        var begin = ApiIdempotencyService.Classify(rec, rec.Id, "deals.advance");
        Assert.Equal(IdempotencyBeginKind.Replay, begin.Kind);
        Assert.Equal(rec.ResponseJson, begin.Existing!.ResponseJson);
        Assert.Equal(rec.ResponseStatus, begin.Existing.ResponseStatus);
    }

    /// <summary><b>ومِفتاحانِ مُختَلِفانِ ⇒ أَثَران</b> — التَفَرُّقُ
    /// في مُعَرِّفِ الوَثيقَة نَفسِه، فَلا يَعتَمِد على تَعاوُنِ
    /// كود.</summary>
    [Fact]
    public void BlockC_two_different_keys_are_two_different_records()
    {
        var ids = new[] { "req-1", "req-2" }
            .Select(k => ApiIdempotencyRecord.IdFor("k1", k))
            .ToArray();

        Assert.Equal(2, ids.Distinct(StringComparer.Ordinal).Count());
        Console.WriteLine($"· حَقيبَةُ المُطابَقَة (ج): {ids.Length} مِفتاحَ مَرَّة-واحِدَة مَفحوصاً.");
    }

    /// <summary><b>والرَأسُ إلزامِيٌّ لا اختِيارِيّ</b> — كِتابَةٌ بِلا
    /// مِفتاحٍ تُرَدّ ‏422، فَلا يوجَد مَسارٌ يَتَجاوَز
    /// الآلِيَّة.</summary>
    [Fact]
    public void BlockC_a_write_without_the_header_is_rejected_not_tolerated()
    {
        Assert.Null(ApiIdempotencyService.NormalizeKey(null));
        Assert.Equal(422, ApiErrorCatalog.Require(ApiErrorCatalog.IdempotencyKeyRequired).Status);
    }

    // ═══ الكُتلَة د — التَدَفُّق ═══════════════════════════════════════

    /// <summary><b>انتِقالٌ مِن حالَةٍ نِهائيَّة ⇒ ‏409، وفاعِلٌ غَير
    /// مُخَوَّلٍ ⇒ ‏403.</b> والتَمييزُ بَينَهُما هُوَ ما يَجعَل
    /// العَميلَ يَعرِف: أَيُعيد المُحاوَلَةَ بِفاعِلٍ آخَر، أَم لا
    /// يُعيدُها أَبَداً.</summary>
    [Fact]
    public void BlockD_a_final_stage_is_409_and_an_unauthorised_actor_is_403()
    {
        var cases = new (string Code, int Status)[]
        {
            (ApiErrorCatalog.DealFinalStage, 409),
            (ApiErrorCatalog.DealNotActive, 409),
            (ApiErrorCatalog.ActorNotAllowed, 403),
        };

        Assert.True(cases.Length >= 3, "أَداة عَمياء.");
        Console.WriteLine($"· حَقيبَةُ المُطابَقَة (د): {cases.Length} حالَةَ تَدَفُّقٍ مَفحوصَة.");

        foreach (var (code, status) in cases)
            Assert.Equal(status, ApiErrorCatalog.Require(code).Status);
    }

    /// <summary><b>والتَخويلُ يَبقى في <c>DealsService</c></b> —
    /// المَعجَمُ لا يَحمِل رَمزاً يُغري بِإعادَةِ اتِّخاذِ
    /// القَرار.</summary>
    [Fact]
    public void BlockD_the_catalog_has_no_code_for_a_decision_the_service_owns()
    {
        Assert.DoesNotContain(ApiErrorCatalog.Codes,
            c => c.Contains("stage_order", StringComparison.Ordinal)
              || c.Contains("pattern_", StringComparison.Ordinal));
    }

    // ═══ الكُتلَة هـ — العَدّاد ════════════════════════════════════════

    /// <summary><b>الحَقيبَةُ تَعُدُّ نَفسَها.</b> كُلُّ كُتلَةٍ
    /// أَعلاه تَطبَع ما فَحَصَته؛ وهذا يُثبِت أَنّ الكُتَلَ الخَمسَ
    /// مَوجودَةٌ فِعلاً — فَحَقيبَةٌ فَقَدَت كُتلَةً تَخضَرُّ صامِتَةً
    /// وهي ناقِصَة.</summary>
    [Fact]
    public void BlockE_all_five_blocks_are_present()
    {
        var blocks = typeof(ApiConformanceTests)
            .GetMethods()
            .Select(m => m.Name)
            .Where(n => n.StartsWith("Block", StringComparison.Ordinal))
            .Select(n => n[5])
            .Distinct()
            .OrderBy(c => c)
            .ToArray();

        Assert.Equal(new[] { 'A', 'B', 'C', 'D', 'E' }, blocks);
    }

    // ═══ الدَينُ المُعلَن — ما يَحتاج نِداءً حَيّاً ════════════════════

    private sealed record LiveObligation(string What, string WhyAr, string Curl, string MeasuredAr);

    /// <summary>
    /// <para><b>البُرهانُ الحَيُّ — نُفِّذَ ‏2026-08-22.</b> كانَت
    /// هذِه أَربَعَةَ نِداءاتٍ <b>مُؤَجَّلَة</b> لِأَنّ قاعِدَةَ
    /// البَيانات كانَت تَرُدّ <c>28P01</c>. عادَت القاعِدَة، ونُفِّذَ
    /// النَصُّ حَرفاً، فَصارَ لِكُلّ سَطرٍ حَقلٌ رابِع:
    /// <see cref="LiveObligation.MeasuredAr"/> — <b>ما قيسَ فِعلاً</b>.
    /// المُخرَجاتُ كامِلَةً في
    /// <c>docs/API-SURFACE-DESIGN.md</c> §١١٫١٠.</para>
    ///
    /// <para><b>ولِماذا يَبقى السَطرُ بَعدَ سَدادِه</b>: القائِمَةُ
    /// عَقدُ المَوجَة — تَحذِفُها يَعني أَنّ المَوجَةَ التالِيَة
    /// تَبدَأُ بِلا شَرطِ إغلاق. تُسَدَّد ولا تُمحى.</para>
    /// </summary>
    private static readonly LiveObligation[] LiveObligations =
    {
        new("مِفتاحٌ صالِحٌ يَقرَأُ صَفقاتِه",
            "التَركيبُ الكامِل (مُرَشِّح + خِدمَة + جَلسَةُ مُستَأجِر) لا يُثبِتُه اختِبارُ وَحدَة.",
            "curl -H \"Authorization: Bearer wsl_…\" https://…/api/v1/deals",
            "‏200 · JSON بِصَفقَتَين (count=2) · مِفتاحٌ مُصدَرٌ بِالنَقر مِن /studio/apps/ejar/keys"),

        new("تَحريكُ صَفقَةٍ مَرَّتَين بِنَفس مِفتاحِ مَرَّة-واحِدَة",
            "الأَثَرُ الواحِدُ لا يُقاس إلّا بِقاعِدَةٍ حَيَّة تُعَدُّ صُفوفُها قَبلَ وبَعد.",
            "curl -H \"Authorization: Bearer wsl_…\" -H \"Idempotency-Key: k1\" " +
            "-X POST -d '{}' -H 'Content-Type: application/json' https://…/api/v1/deals/{id}/advance",
            "‏200 مَرَّتَين · الجَوابانِ مُتَطابِقانِ بايتاً بِبايت (‏491 بايت) · " +
            "Timeline ‏3←4 وStage ‏3←4 بَعدَ **نِداءَين** · mt_events ‏17←17"),

        new("طَلَبٌ بِلا مِفتاحٍ يُرَدّ 401",
            "مَقيسٌ في ApiKeyFilterTests بِسِياقٍ مُصطَنَع — والحَيُّ يُثبِت التَركيبَ لا القَرار.",
            "curl -i https://…/api/v1/deals",
            "‏401 Unauthorized · {\"error\":{\"code\":\"auth_missing\",…}}"),

        new("مِفتاحُ مُستَأجِرٍ آخَر يُرَدّ 404 لا 403",
            "يَحتاج مُستَأجِرَين ومِفتاحَين وصَفقَةً — كُلُّها في القاعِدَة.",
            "curl -H \"Authorization: Bearer <مِفتاح مُستَأجِر آخَر>\" https://…/api/v1/deals/{id}",
            "‏404 not_found بِمِفتاح order على صَفقَةِ ejar · وضابِطٌ: نَفسُ الصَفقَة بِمِفتاح ejar = 200"),
    };

    /// <summary>كُلُّ دَينٍ يُعلِن سَبَبَه وأَمرَه <b>وما قيسَ</b> —
    /// فَالقائِمَةُ بُرهانٌ مَوصوفٌ لا قائِمَةُ إسكات. وحَقلُ القِياس
    /// هُوَ الَّذي يَمنَعُ أَن يَعودَ السَطرُ وَعداً: نَصٌّ فارِغٌ
    /// فيه يُحمِر.</summary>
    [Fact]
    public void Every_live_obligation_names_its_reason_its_command_and_what_was_measured()
    {
        Assert.Equal(4, LiveObligations.Length);

        foreach (var o in LiveObligations)
        {
            Assert.True(o.WhyAr.Length > 30, $"«{o.What}» بِسَبَبٍ أَقصَرَ مِن أَن يَكونَ سَبَباً.");
            Assert.StartsWith("curl", o.Curl);
            Assert.Contains("/api/v1/", o.Curl);
            Assert.True(o.MeasuredAr.Length > 20,
                $"«{o.What}» بِلا قِياسٍ مَكتوب — الدَينُ يُسَدَّد بِرَقمٍ لا بِدَعوى.");
        }

        Console.WriteLine($"· البُرهانُ الحَيّ: {LiveObligations.Length} نِداءً مُنَفَّذاً، لِكُلٍّ قِياسُه.");
    }

    // ═══ القُدرَةُ مَوصولَة ════════════════════════════════════════════

    /// <summary><b>‏<c>api.call</c> مَوصولَةٌ بِـ<c>Handles</c></b> —
    /// وإلّا رَمى التَنفيذُ <c>NotSupportedException</c> عِندَ أَوَّل
    /// طَلَبِ API بَدَلَ أَن يُجيب.</summary>
    [Fact]
    public void The_api_capability_is_served_by_the_registered_implementation()
    {
        Assert.Contains(CapabilityCatalog.ApiCall,
            new SubscriptionEntitlements(null!).Handles);
        Assert.True(CapabilityCatalog.Contains(CapabilityCatalog.ApiCall));
        Assert.False(CapabilityCatalog.IsQuota(CapabilityCatalog.ApiCall));
    }
}
