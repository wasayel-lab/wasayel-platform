using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Api;

/// <summary>
/// <para><b>سِجِلُّ مَرَّة-واحِدَة</b> — وَثيقَةٌ بِمُعَرِّفٍ مُرَكَّب
/// <c>{keyId}|{idempotencyKey}</c>. التَفَرُّدُ مِن <b>مِفتاح
/// الوَثيقَة</b> نَفسِه لا مِن فَهرَسٍ ثانٍ ولا مِن قُفلٍ في
/// التَطبيق: مُحاوَلَةُ إدراجٍ ثانِيَة تَرتَدّ مِن Postgres.</para>
///
/// <para><b>والمِفتاحُ يَحمِل <c>keyId</c> عَمداً</b>: مُستَأجِرانِ
/// يَختارانِ نَفسَ <c>Idempotency-Key</c> لا يَتَصادَمان، ومِفتاحانِ
/// في مُستَأجِرٍ واحِد كَذلك. والعَزلُ فَوقَ ذلك مَجّانيّ: الوَثيقَةُ
/// تَحتَ <c>AllDocumentsAreMultiTenanted</c>.</para>
/// </summary>
public sealed class ApiIdempotencyRecord
{
    public string Id { get; set; } = "";

    /// <summary>ما نُودِيَ بِه — يُقارَن عِندَ الإعادَة، فَنَفسُ
    /// المِفتاحِ على نُقطَةٍ أُخرى خَطَأُ عَميلٍ لا إعادَةُ
    /// مُحاوَلَة.</summary>
    public string Endpoint { get; set; } = "";

    public string Status { get; set; } = StatusInProgress;

    public const string StatusInProgress = "in_progress";
    public const string StatusCompleted  = "completed";

    public int ResponseStatus { get; set; }

    /// <summary>جِسمُ الجَواب حَرفاً — فَالإعادَةُ تُعطي
    /// <b>نَفسَ</b> الجَواب لا جَواباً مُكافِئاً.</summary>
    public string ResponseJson { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public static string IdFor(string keyId, string idempotencyKey) => $"{keyId}|{idempotencyKey}";
}

/// <summary>نَتيجَةُ مُحاوَلَةِ البَدء: إمّا بَدَأنا، أَو الجَوابُ
/// مَحفوظٌ فَيُعاد، أَو نُسخَةٌ سابِقَةٌ ما زالَت تَجري.</summary>
public enum IdempotencyBeginKind { Started, Replay, InProgress, EndpointMismatch }

public sealed record IdempotencyBegin(
    IdempotencyBeginKind Kind, string Id, ApiIdempotencyRecord? Existing);

/// <summary>
/// <para><b>مَرَّة-واحِدَة على الكِتابَة</b> (‏§٤٫٥) —
/// <b>بِانحِرافٍ مَقيسٍ عَن الوَثيقَة، مُعلَنٍ هُنا لا مَبلوع</b>.</para>
///
/// <para><b>ما وَصَفَته الوَثيقَة</b>: تُلحَق الوَثيقَةُ في <b>جَلسَة
/// العَمَلِيَّة نَفسِها</b> قَبل <c>SaveChangesAsync</c> الواحِدَة —
/// فَإمّا تُكتَب العَمَلِيَّةُ ومِفتاحُها مَعاً أَو لا يُكتَب
/// شَيء.</para>
///
/// <para><b>وما يَمنَعُه القِياس</b>: <c>DealsService.AdvanceAsync</c>
/// و<c>CancelAsync</c> <b>تَفتَحانِ جَلسَتَهُما بِأَنفُسِهِما
/// وتُودِعان</b> (<c>DealsService.cs</c>: <c>LightweightSession</c> ثُمَّ
/// <c>SaveChangesAsync</c>). فَلا سَبيلَ إلى مُشارَكَةِ جَلسَتِهِما
/// إلّا بِتَعديل تَوقيعِهِما — و<b>الخُطَّةُ تَشتَرِط أَنّ استِخراجَ
/// المَنطِقِ في هذِه المَوجَة صِفرُ سَطر</b>، أَي أَنّ الخِدمَةَ
/// الأَنضَجَ في المُستَودَع تُغَلَّف ولا تُمَسّ.</para>
///
/// <para><b>فَالثَمَنُ المَدفوع، مُسَمّىً</b>: مُعامَلَتانِ لا واحِدَة
/// — حَجزٌ قَبلَ العَمَلِيَّة، وإتمامٌ بَعدَها. والنافِذَةُ الَّتي
/// يَفتَحُها ذلك <b>واحِدَةٌ ومُحَدَّدَة</b>: انقِطاعٌ بَينَ
/// الحَجزِ والإتمام يَترُك السِجِلَّ <c>in_progress</c>، فَتُرَدّ
/// إعادَةُ المُحاوَلَة بِـ‏409 بَدَلَ أَن تُكَرِّرَ الأَثَر.
/// <b>وهذا هُوَ الاتِّجاهُ الصَحيح لِلفَشَل</b>: العَقدُ يَعِد
/// بِـ«أَثَرٌ واحِدٌ على الأَكثَر»، ولا يَعِد بِأَن تَنجَحَ كُلُّ
/// إعادَةٍ تِلقائيّاً.</para>
///
/// <para><b>والبَديلُ مُسَعَّرٌ لا مَسكوتٌ عَنه</b>: مُعامَلَةٌ
/// واحِدَةٌ تَعني تَعديلَ <c>DealsService</c> لِيَقبَلَ
/// <c>IDocumentSession</c> — وهو الشَكلُ الَّذي تُفَضِّلُه
/// المِعمارِيَّةُ فِعلاً (‏<c>IEntitlements.ConsumeAsync</c> حُجَّتُه
/// المَقيسَة). فَيَومَ يُرَحَّل <c>DealsService</c> إلى ذلك
/// التَوقيع، يَسقُط هذا الصَنفُ إلى نِداءَين داخِلَ الجَلسَة —
/// <b>والمُستَهلِكُ لا يَتَغَيَّر</b>.</para>
/// </summary>
public sealed class ApiIdempotencyService
{
    private readonly IDocumentStore _store;

    public ApiIdempotencyService(IDocumentStore store) => _store = store;

    /// <summary>اسمُ الرَأس — مَوضِعٌ واحِد يَقرَؤُه المُنتِجُ
    /// والمُختَبِر.</summary>
    public const string HeaderName = "Idempotency-Key";

    /// <summary>حَدُّ طول المِفتاح — رَقمٌ لِمَنعِ إساءَةٍ لا حَدُّ
    /// مُنتَج: مِفتاحُ العَميلِ عادَةً <c>uuid</c> أَو أَقصَر.</summary>
    public const int MaxKeyLength = 200;

    public async Task<IdempotencyBegin> TryBeginAsync(
        string tenantSlug, string keyId, string idempotencyKey, string endpoint,
        CancellationToken ct = default)
    {
        var id = ApiIdempotencyRecord.IdFor(keyId, idempotencyKey);

        await using var s = _store.LightweightSession(tenantSlug);
        var existing = await s.LoadAsync<ApiIdempotencyRecord>(id, ct);
        if (existing is not null) return Classify(existing, id, endpoint);

        s.Insert(new ApiIdempotencyRecord
        {
            Id = id, Endpoint = endpoint,
            Status = ApiIdempotencyRecord.StatusInProgress,
            CreatedAt = DateTime.UtcNow,
        });

        try
        {
            await s.SaveChangesAsync(ct);
            return new IdempotencyBegin(IdempotencyBeginKind.Started, id, null);
        }
        catch (Exception)
        {
            // خَسِرَ السِباقَ عِندَ الإدراج — فَالفائِزُ كَتَبَ الصَفَّ
            // ونَحنُ نَقرَؤُه. وهذا هُوَ المَوضِعُ الَّذي يَجعَل
            // التَفَرُّدَ حَقيقِيّاً لا مَظنوناً: فَحصُ الوُجودِ وَحدَه
            // نافِذَةُ سِباقٍ، ومِفتاحُ الوَثيقَةِ قُفلٌ حَقيقيّ.
            await using var s2 = _store.QuerySession(tenantSlug);
            var winner = await s2.LoadAsync<ApiIdempotencyRecord>(id, ct);
            return winner is null
                ? new IdempotencyBegin(IdempotencyBeginKind.InProgress, id, null)
                : Classify(winner, id, endpoint);
        }
    }

    public async Task CompleteAsync(
        string tenantSlug, string id, int status, string responseJson,
        CancellationToken ct = default)
    {
        await using var s = _store.LightweightSession(tenantSlug);
        var rec = await s.LoadAsync<ApiIdempotencyRecord>(id, ct);
        if (rec is null) return;

        rec.Status         = ApiIdempotencyRecord.StatusCompleted;
        rec.ResponseStatus = status;
        rec.ResponseJson   = responseJson;
        rec.CompletedAt    = DateTime.UtcNow;
        s.Store(rec);
        await s.SaveChangesAsync(ct);
    }

    /// <summary><b>دالَّةٌ نَقِيَّة</b> — تُختَبَر بِمُوجِبٍ وسالِبٍ بِلا
    /// قاعِدَةِ بَيانات. نَفسُ المِفتاح على نُقطَةٍ أُخرى
    /// <c>EndpointMismatch</c>: العَميلُ أَعادَ استِعمالَ مِفتاحٍ لا
    /// يَخُصُّ هذا الطَلَب، وإعادَةُ جَوابِ نُقطَةٍ أُخرى أَسوَأُ مِن
    /// رَفضِه.</summary>
    public static IdempotencyBegin Classify(
        ApiIdempotencyRecord existing, string id, string endpoint)
    {
        if (!string.Equals(existing.Endpoint, endpoint, StringComparison.Ordinal))
            return new IdempotencyBegin(IdempotencyBeginKind.EndpointMismatch, id, existing);

        return string.Equals(existing.Status, ApiIdempotencyRecord.StatusCompleted, StringComparison.Ordinal)
            ? new IdempotencyBegin(IdempotencyBeginKind.Replay, id, existing)
            : new IdempotencyBegin(IdempotencyBeginKind.InProgress, id, existing);
    }

    /// <summary><b>بَوّابَةُ الرَأس</b> — دالَّةٌ نَقِيَّة. فارِغٌ أَو
    /// أَطوَلُ مِن الحَدّ ⇒ <c>null</c>، والنُقطَةُ تَرُدّ
    /// <c>idempotency_key_required</c>.</summary>
    public static string? NormalizeKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var k = raw.Trim();
        return k.Length is 0 or > MaxKeyLength ? null : k;
    }
}
