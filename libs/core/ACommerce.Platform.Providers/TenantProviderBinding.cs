using System.Text.Json.Serialization;
using ACommerce.Platform.Flows;

namespace ACommerce.Platform.Providers;

/// <summary>
/// <para><b>قيمَةٌ مُخَزَّنَةٌ لِحَقلٍ واحِد</b> — و<b>هذِه المَوجَةُ
/// تَدعَم الأَنواعَ الصَريحَةَ وَحدَها</b>: لا خِزانَةَ ولا تَشفير.
/// وأَعمِدَةُ الظَرف (<c>Nonce</c>, <c>Cipher</c>, <c>Tag</c>,
/// <c>KekVersion</c>, <c>Aad</c>) <b>مُعلَنَةٌ وغَيرُ مُستَعمَلَة</b>،
/// والسَبَبُ مَقيسٌ لا ذَوقيّ: إضافَتُها لاحِقاً <b>تَرحيلُ جَدوَلٍ
/// حَيٍّ يَحمِل اعتِماداتِ عُمَلاء</b>. وهذا هُوَ المَوضِعُ الوَحيدُ
/// المُجازُ فيه إعلانٌ سابِقٌ لِاستِعمالِه — والقاعِدَةُ ١ عَن
/// <b>تَجريد</b> لا عَن شَكلِ حَقل.</para>
///
/// <para><b>و<c>KekVersion</c> يَحمِلُ الدَوَران مِن اليَوم</b>: نُسخَتانِ
/// تَتَعايَشانِ في نَفسِ الوَثيقَة، فَالتَرحيلُ إعادَةُ تَغليفٍ في
/// الخَلفِيَّةِ لا انقِطاع. ومُثبَتٌ بِاختِبار لا بِدَعوى.</para>
///
/// <para><b>والفَشَلُ مُغلَق</b>: <see cref="Explicit"/> <b>تَرمي</b>
/// لِنَوعٍ يُشبِهُ السِرّ — فَلا سَبيلَ إلى كِتابَةِ سِرٍّ صَريحٍ في
/// عَمودِ نَصّ، ولَو أَرادَ الكاتِب.</para>
/// </summary>
public sealed class StoredValue
{
    public string Kind { get; set; } = CredentialKinds.None;

    /// <summary>النَصُّ الصَريح — لِلأَنواعِ الَّتي تُعرَض وَحدَها.</summary>
    public string? Plain { get; set; }

    // ─── أَعمِدَةُ الظَرف — مُعلَنَةٌ مِن اليَومِ الأَوَّل ────────────
    public string? Nonce { get; set; }
    public string? Cipher { get; set; }
    public string? Tag { get; set; }
    public int KekVersion { get; set; }
    public string? Aad { get; set; }

    /// <summary>قيمَةٌ صَريحَةٌ مِن نَوعٍ يُعرَض — والنَوعُ يُفحَص
    /// بِالمَعجَمِ المُلزِمِ أَوَّلاً.</summary>
    public static StoredValue Explicit(string kind, string value)
    {
        CredentialKinds.Require(kind);

        if (CredentialKinds.IsSecretLike(kind))
            throw new InvalidOperationException(
                $"النَوع «{kind}» سِرٌّ، ولا خِزانَةَ في هذِه المَوجَة — " +
                "فَلا يُخَزَّن صَريحاً. (‏ADR-012)");

        return new StoredValue { Kind = kind, Plain = value, KekVersion = 0 };
    }

    /// <summary>الصورَةُ الَّتي يَجوز أَن تُعرَض.</summary>
    [JsonIgnore]
    public string Censored => ProviderSecrecy.Censor(Kind, Plain);
}

/// <summary>
/// <para><b>رَبطُ مُزَوِّدٍ بِمُستَأجِر — وَثيقَةُ Marten</b>.
/// مُعَرِّفُها <b>هُوَ القُدرَة</b>: رَبطٌ فَعّالٌ واحِدٌ لِكُلّ قُدرَةٍ
/// لِكُلّ مُستَأجِر.</para>
///
/// <para><b>ودَورَةُ حَياتِها <c>active|revoked</c> مُستَعارَةٌ مِن
/// <c>ApiKeyDocument</c> لا مِن <c>ApprovalFlow</c></b>:
/// <c>ApprovalFlow.Approved</c> <b>نِهائِيَّةٌ مُعلَنَة</b> — وذلكَ
/// يَصلُح لِدَورٍ أَو ثيم، ولا يَصلُح لِاعتِماد: المِفتاحُ المَسحوبُ
/// يَجِب أَن يَتَوَقَّفَ <b>الآن</b>.</para>
///
/// <para><b>والعَزلُ مَجّانِيٌّ بِنيَوِيّاً</b>: سِياسَةُ
/// <c>AllDocumentsAreMultiTenanted()</c> تَضَع <c>tenant_id</c> في كُلّ
/// صَفّ بِصِفرِ سَطر، فَلا استِعلامَ عابِراً لِلمُستَأجِرينَ مُمكِنٌ
/// أَصلاً.</para>
///
/// <para><b>ولِماذا تُنَفِّذُ <c>ITenantDefinitionDocument</c> صَراحَةً</b>:
/// لِتَرِثَ مَكانيكا <c>TenantDefinitionService</c> (الكاشُ بِمِفتاحِ
/// السلاج، والقِراءَةُ بِجَلسَةِ المُستَأجِر، والسُقوطُ الآمِن،
/// والإبطال) بِلا أَن تَرِثَ دَورَةَ الاعتِماد. و<c>DefinitionJson</c>
/// <b>تَرمي</b>: المُستَأجِرُ لا يُؤَلِّف تَعريفَ مُزَوِّد — يَختار
/// سلاجاً مِن كاتالوجِ المَنَصَّةِ ويَملَأ حُقولاً.</para>
/// </summary>
public sealed class TenantProviderBinding : ITenantDefinitionDocument
{
    public const string StatusActive  = "active";
    public const string StatusRevoked = "revoked";

    public static readonly IReadOnlyList<string> Statuses =
        new[] { StatusActive, StatusRevoked };

    /// <summary>القُدرَة — وهِيَ المُعَرِّف.</summary>
    public string Id { get; set; } = "";

    /// <summary>نَفسُ القُدرَة، بِاسمِ الواجِهَةِ الَّذي تَطلُبُه
    /// المَكانيكا المُشتَرَكَة.</summary>
    public string Slug { get; set; } = "";

    public string TenantSlug { get; set; } = "";

    /// <summary>سلاجُ التَعريفِ المُختارِ مِن كاتالوجِ المَنَصَّة.</summary>
    public string ProviderSlug { get; set; } = "";

    public string Status { get; set; } = StatusActive;

    public Dictionary<string, StoredValue> Values { get; set; } = new(StringComparer.Ordinal);

    public string BoundBy { get; set; } = "";
    public DateTime BoundAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }

    // ─── تَنفيذٌ صَريحٌ لِلمَكانيكا المُشتَرَكَة ──────────────────────
    //
    // التَنفيذُ الصَريحُ لا يُصَيَّر في JSON، فَلا عَمودَ زائِداً في
    // الوَثيقَة — والمَكانيكا تَراه لِأَنَّها تَتَعامَل مَعَ الواجِهَة.

    string ITenantDefinitionDocument.CreatedBy
    {
        get => BoundBy;
        set => BoundBy = value;
    }

    DateTime ITenantDefinitionDocument.CreatedAt
    {
        get => BoundAt;
        set => BoundAt = value;
    }

    string? ITenantDefinitionDocument.DecidedBy
    {
        get => null;
        set { /* لا دَورَةَ اعتِماد — لا فاعِلَ قَرارٍ يُخَزَّن. */ }
    }

    DateTime? ITenantDefinitionDocument.DecidedAt
    {
        get => RevokedAt;
        set => RevokedAt = value;
    }

    private const string NoAuthoringAr =
        "رَبطُ المُزَوِّدِ لَيسَ تَعريفاً يُؤَلِّفُه المُستَأجِر: " +
        "الكاتالوجُ مَنَصَّةٌ مَضمونَة، والمُستَأجِرُ يَختارُ سلاجاً " +
        "ويَملَأُ حُقولاً. ولا دَورَةَ اعتِمادٍ هُنا — `approved` " +
        "نِهائِيَّةٌ ولا تَصلُح لِاعتِمادٍ يَجِب أَن يَتَوَقَّفَ الآن.";

    string ITenantDefinitionDocument.DefinitionJson
    {
        get => throw new NotSupportedException(NoAuthoringAr);
        set => throw new NotSupportedException(NoAuthoringAr);
    }

    [JsonIgnore]
    public bool IsActive => Status == StatusActive;
}

/// <summary>مُزَوِّدٌ مَحلولٌ لِمُستَأجِر: التَعريفُ + القيَم.</summary>
public sealed record ResolvedProvider(
    ProviderDefinition Definition,
    IReadOnlyDictionary<string, StoredValue> Values)
{
    public string Capability => Definition.Capability;
    public string Slug => Definition.Slug;

    /// <summary>القيمَةُ الصَريحَةُ لِحَقلٍ — و<c>null</c> إن لَم تُملَأ
    /// أَو كانَ نَوعُها لا يُسترجَع صَريحاً.</summary>
    public string? Explicit(string fieldCode) =>
        Values.TryGetValue(fieldCode, out var v) ? v.Plain : null;

    /// <summary><b>أَيَقبِضُ هذا الرَبطُ فِعلاً؟</b> — دَفعٌ، وبِرابِطٍ
    /// مَملوءٍ يَنقُرُه الزَبون. والسُؤالُ يُطرَح هُنا مَرَّةً واحِدَةً
    /// لِأَنّ عَلَيه يَتَوَقَّف ظُهورُ الباقاتِ المَدفوعَة.</summary>
    public bool CollectsMoney =>
        Definition.Capability == ProviderCapabilities.Payments &&
        Definition.Credential.Fields
            .Where(f => CredentialKinds.IsLink(f.Kind))
            .Any(f => !string.IsNullOrWhiteSpace(Explicit(f.Code)));

    /// <summary>أَوَّلُ رابِطِ دَفعٍ مَملوء — وهُوَ ما تُصَيِّرُه صَفحَةُ
    /// الدَفع.</summary>
    public string? PaymentLink =>
        Definition.Credential.Fields
            .Where(f => CredentialKinds.IsLink(f.Kind))
            .Select(f => Explicit(f.Code))
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

/// <summary>
/// <para><b>لَقطَةٌ ساكِنَةٌ غَيرُ قابِلَةٍ لِلتَغيير</b> لِمُزَوِّدي
/// مُستَأجِرٍ واحِد.</para>
///
/// <para><b>والتَكافُؤُ الصِفريُّ هُوَ العَقد</b>: مُستَأجِرٌ بِلا
/// رَبطٍ واحِدٍ يُرجِعُ <see cref="Platform"/> <b>بِنَفسِ المَرجِعِ لا
/// نُسخَة</b> — فَكُلُّ مُستَأجِرٍ قائِمٍ اليَومَ لا يَمُرّ بِسَطرِ
/// قَرارٍ إضافيّ.</para>
/// </summary>
public sealed class TenantProviderSet
{
    public static readonly TenantProviderSet Platform =
        new(null, Array.Empty<ResolvedProvider>());

    private readonly Dictionary<string, ResolvedProvider> _byCapability;

    private TenantProviderSet(string? tenantSlug, IReadOnlyList<ResolvedProvider> bound)
    {
        TenantSlug = tenantSlug;
        Bound = bound;
        _byCapability = new Dictionary<string, ResolvedProvider>(StringComparer.Ordinal);
        foreach (var r in bound) _byCapability[r.Capability] = r;
    }

    public string? TenantSlug { get; }

    public IReadOnlyList<ResolvedProvider> Bound { get; }

    /// <summary>المُزَوِّدُ الفَعّالُ لِهذِه القُدرَة — و<c>null</c>
    /// يَعني «افتِراضُ المَنَصَّة»، وهُوَ الجَوابُ لِكُلّ مُستَأجِرٍ
    /// اليَوم.</summary>
    public ResolvedProvider? For(string capability) =>
        _byCapability.TryGetValue(capability, out var r) ? r : null;

    public bool CollectsMoney => For(ProviderCapabilities.Payments)?.CollectsMoney == true;

    public static TenantProviderSet FromDocuments(
        string? tenantSlug, IEnumerable<TenantProviderBinding> docs)
    {
        var accepted = new List<ResolvedProvider>();

        foreach (var doc in docs
                     .Where(d => d.Status == TenantProviderBinding.StatusActive)
                     .OrderBy(d => d.BoundAt)
                     .ThenBy(d => d.Id, StringComparer.Ordinal))
        {
            var def = ProviderCatalog.Find(doc.ProviderSlug);
            if (def is null)
            {
                // سلاجٌ لا يُقابِلُه تَعريف = كاتالوجٌ تَقَدَّمَ ووَثيقَةٌ
                // تَخَلَّفَت. تُجوهَل ولا تُسقِطُ الصَفحَة.
                Console.Error.WriteLine(
                    $"[providers] رَبط «{doc.Id}» لِلمُستَأجِر «{tenantSlug}» يُشير إلى " +
                    $"مُزَوِّدٍ غَير مَعروف «{doc.ProviderSlug}» — تُجوهِل.");
                continue;
            }

            if (def.Capability != doc.Id)
            {
                Console.Error.WriteLine(
                    $"[providers] رَبط «{doc.Id}» لِلمُستَأجِر «{tenantSlug}» يَحمِل مُزَوِّدَ " +
                    $"قُدرَةٍ أُخرى «{def.Capability}» — تُجوهِل.");
                continue;
            }

            accepted.Add(new ResolvedProvider(def, doc.Values));
        }

        // نَفسُ المَرجِعِ لا نُسخَة — التَكافُؤُ الصِفريُّ بِالهُوِيَّة.
        return accepted.Count == 0 ? Platform : new TenantProviderSet(tenantSlug, accepted);
    }
}
