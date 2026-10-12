using System.Text.Json.Serialization;
using ACommerce.Platform.Flows;

namespace ACommerce.Platform.Providers;

/// <summary>
/// <para><b>قيمَةٌ مُخَزَّنَةٌ لِحَقلٍ واحِد</b> — صَريحَةٌ لِنَوعٍ يُعرَض
/// (<see cref="Explicit"/>)، أَو <b>مَختومَةٌ</b> لِسِرٍّ تَختِمُه الخِزانَة
/// (<see cref="Sealed"/>، ‏<c>ProviderVault</c>). وأَعمِدَةُ الظَرف
/// (<c>Nonce</c>, <c>Cipher</c>, <c>Tag</c>, <c>KekVersion</c>, <c>Aad</c>)
/// <b>أُعلِنَت مِن اليَومِ الأَوَّلِ (‏ADR-012 §هـ) وتُملَأُ مُنذُ ‏2026-10-12</b>
/// (‏ADR-032 §٢‑هـ الخُطوَة 4) — تَعبِئَةٌ لا إعادَةُ تَصميم: لا عَمودَ مِنها
/// تَغَيَّرَ اسمُه ولا نَوعُه. والسَبَبُ الَّذي أَعلَنَها قَبلَ استِعمالِها
/// مَقيسٌ لا ذَوقيّ: إضافَتُها لاحِقاً <b>تَرحيلُ جَدوَلٍ حَيٍّ يَحمِل
/// اعتِماداتِ عُمَلاء</b>.</para>
///
/// <para><b>و<c>KekVersion</c> يَحمِلُ الدَوَران مِن اليَوم</b>: نُسخَتانِ
/// تَتَعايَشانِ في نَفسِ الوَثيقَة، فَالتَرحيلُ إعادَةُ تَغليفٍ في
/// الخَلفِيَّةِ لا انقِطاع. ومُثبَتٌ بِاختِبار لا بِدَعوى.</para>
///
/// <para><b>والفَشَلُ مُغلَق</b>: <see cref="Explicit"/> <b>تَرمي</b>
/// لِنَوعٍ يُشبِهُ السِرّ — فَلا سَبيلَ إلى كِتابَةِ سِرٍّ صَريحٍ في
/// عَمودِ نَصّ، ولَو أَرادَ الكاتِب؛ والسِرُّ يَمُرُّ مِن الخِزانَةِ وَحدَها.</para>
/// </summary>
public sealed class StoredValue
{
    public string Kind { get; set; } = CredentialKinds.None;

    /// <summary>النَصُّ الصَريح — لِلأَنواعِ الَّتي تُعرَض وَحدَها. و<c>null</c>
    /// دائِماً لِقيمَةٍ مَختومَة.</summary>
    public string? Plain { get; set; }

    // ─── أَعمِدَةُ الظَرف — مُعلَنَةٌ مِن اليَومِ الأَوَّل، وتَملَؤُها الخِزانَة ──
    public string? Nonce { get; set; }
    public string? Cipher { get; set; }
    public string? Tag { get; set; }
    public int KekVersion { get; set; }
    public string? Aad { get; set; }

    /// <summary>
    /// <para><b>آخِرُ أَربَعَةِ مَحارِفَ مِن سِرٍّ مَختوم — لِلعَرضِ وَحدَه</b>
    /// (‏ADR-032 الخُطوَة 4 تُسَمّيه؛ و«يُعرَضُ مِنه آخِرُ أَربَعَةِ مَحارِف» في جَدوَلِ
    /// ‏ADR-012 §٢‑ب لِـ<c>secret_key</c>). يُكتَبُ عِندَ الخَتمِ بِقاعِدَةِ
    /// <c>ProviderSecrecy.TailOf</c> نَفسِها الَّتي تُقَنِّعُ الصَريح، فَلا يَحتاجُ
    /// العَرضُ فَكّاً. و<c>null</c> لِسِرٍّ لا ذَيلَ لَه أَو لِنَوعٍ لا يُعرَضُ مِنه شَيء.
    /// <b>عَمودٌ سادِسٌ مُضاف</b> بِجِوارِ الخَمسَة، لا بَدَلٌ مِن أَحَدِها.</para>
    /// </summary>
    public string? Tail4 { get; set; }

    /// <summary>أَمَختومَةٌ هذِه القيمَة؟ — مُشتَقٌّ لا يُخَزَّن.</summary>
    [JsonIgnore]
    public bool IsSealed => Cipher is not null;

    /// <summary>قيمَةٌ صَريحَةٌ مِن نَوعٍ يُعرَض — والنَوعُ يُفحَص
    /// بِالمَعجَمِ المُلزِمِ أَوَّلاً.</summary>
    public static StoredValue Explicit(string kind, string value)
    {
        CredentialKinds.Require(kind);

        if (CredentialKinds.IsSecretLike(kind))
            throw new InvalidOperationException(
                $"النَوع «{kind}» سِرٌّ — لا يُخَزَّنُ صَريحاً أَبَداً؛ يُختَمُ " +
                "بِـStoredValue.Sealed في الخِزانَة أَو لا يُخَزَّن. (‏ADR-012، ADR-032)");

        return new StoredValue { Kind = kind, Plain = value, KekVersion = 0 };
    }

    /// <summary>
    /// <para><b>قيمَةٌ مَختومَةٌ بِالخِزانَة</b> — تَملَأُ أَعمِدَةَ الظَرفِ
    /// و<see cref="Tail4"/>، و<see cref="Plain"/> فارِغٌ أَبَداً. <c>aad</c> سِياقُ
    /// القيمَةِ يَبنيه المُنادي (<c>ProviderVault.AadOf</c>)، ولا تُفَكُّ إلّا
    /// بِه.</para>
    /// <para>يَرمي لِنَوعٍ لا تَختِمُه الخِزانَة، ولِخِزانَةٍ غَيرِ مُتاحَة
    /// (‏<c>vault_unavailable</c>) — <b>لا خَتمَ بِلا مِفتاح</b>.</para>
    /// </summary>
    public static StoredValue Sealed(ProviderVault vault, string kind, string clear, string aad)
        => vault.Seal(kind, clear, aad);

    /// <summary><b>النَصُّ الصَريحُ لِمَن يَطلُبُ بِه فِعلاً</b> — بِسِياقِه هُوَ
    /// لا بِعَمودِ <c>Aad</c> المُخَزَّن. يَرمي <c>CryptographicException</c>
    /// لِمِفتاحٍ آخَرَ أَو سِياقٍ آخَرَ أَو عَمودٍ مُعبَثٍ بِه، ولا يُعيدُ
    /// شَيئاً حينَها.</summary>
    public string Unseal(ProviderVault vault, string aad) => vault.Open(this, aad);

    /// <summary>الصورَةُ الَّتي يَجوز أَن تُعرَض — ولِلمَختومَةِ ذَيلُها
    /// المَحفوظُ أَو القِناعُ وَحدَه: لا نَصَّ صَريحاً بَينَ يَدَيها أَصلاً.</summary>
    [JsonIgnore]
    public string Censored => IsSealed
        ? ProviderSecrecy.CensorSealed(Kind, Tail4)
        : ProviderSecrecy.Censor(Kind, Plain);

    /// <summary><b>وَجهٌ مُعَتَّم</b>: النَوعُ والصورَةُ المَعروضَة — فَلا يُفشي
    /// لوغٌ أَو رِسالَةُ خَطَإٍ تُنادي <c>ToString</c> سِرّاً.</summary>
    public override string ToString() => $"{Kind}:{Censored}";
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

    // ─── مَدخَلُ البائِع — الوَثيقَةُ نَفسُها بِمُعَرِّفٍ مُرَكَّب (‏ADR-033 §٢‑ج‑٢ والخُطوَة 7؛ تَيّارُ S8، ‏2026-10-12) ─────
    //
    // **لا وَثيقَةَ ثانِيَة** (القاعِدَة ٨): قيَمُ البائِعِ في المَتجَرِ — رَمزُ تَحديثِ حِسابِه عِندَ الناقِلِ ورَمزُ وارِدِه — تَسكُنُ
    // هذِه الوَثيقَةَ بِأَعمِدَةِ ظَرفِها وإيجارِها المُقتَرِن، بِمُعَرِّفِ `{capability}:{listing_owner}` و`Slug` القُدرَة. ولا يَقرَؤُها
    // `TenantProviderSet.For` — مُجموعَةُ المَتجَرِ تَتَجاوَزُها بِقاعِدَةٍ مُسَمّاة (`IsVendorEntryId`) — بَل استِعلامٌ بِمِفتاحِ البائِع.

    /// <summary>فاصِلُ مُعَرِّفِ مَدخَلِ البائِع — لا يَحمِلُه مُعَرِّفُ قُدرَةٍ (المَعجَمُ أَسماءٌ بِلا <c>:</c>).</summary>
    public const char VendorEntrySeparator = ':';

    /// <summary><b>مُعَرِّفُ مَدخَلِ البائِع</b>: <c>{capability}:{listing_owner}</c> — والمالِكُ بِصيغَةِ <c>D</c> كَما يُكتَبُ
    /// في مَرجِعِ <c>listing_owner</c> عَلى الصَفقَة. القُدرَةُ مِن المَعجَمِ وإلّا يُرمى.</summary>
    public static string VendorEntryId(string capability, Guid listingOwner)
        => $"{ProviderCapabilities.Require(capability)}{VendorEntrySeparator}{listingOwner:D}";

    /// <summary>
    /// <para><b>أَهذا مُعَرِّفُ مَدخَلِ بائِع؟ — القاعِدَةُ المُسَمّاة</b>: قُدرَةٌ مِن المَعجَمِ ثُمَّ <c>:</c> ثُمَّ مُعَرِّفٌ بِصيغَةِ
    /// <c>D</c>. وما سِواه — ومِنه قُدرَةٌ خارِجَ المَعجَمِ أَو لاحِقَةٌ لَيسَت مُعَرِّفاً — لَيسَ مَدخَلَ بائِع.</para>
    /// </summary>
    public static bool IsVendorEntryId(string? id, out string capability, out Guid listingOwner)
    {
        capability = "";
        listingOwner = Guid.Empty;
        if (string.IsNullOrEmpty(id)) return false;

        var at = id.IndexOf(VendorEntrySeparator);
        if (at <= 0 || !ProviderCapabilities.Contains(id[..at])) return false;
        if (!Guid.TryParseExact(id[(at + 1)..], "D", out var owner)) return false;

        (capability, listingOwner) = (id[..at], owner);
        return true;
    }

    /// <summary><b>تَسجيلُ الوارِدِ عِندَ المُزَوِّدِ لِمَدخَلِ البائِع</b> — مُعَرِّفُ التَسجيلِ كَما أَعادَه (‏<c>"id"</c> في رَدِّ
    /// «Webhook» عِندَ OTO)، و<c>null</c> حَيثُ لَم يُسَجَّل بَعد. يَقرَؤُه سَطحُ البائِعِ فَيَقولُ أَتَصِلُ الإشعارات.</summary>
    public string? WebhookId { get; set; }

    /// <summary>رَمزُ فَشَلِ آخِرِ مُحاوَلَةِ تَسجيلٍ لِلوارِد (مِن مَعجَمِ فَشَلِ العَميل)، و<c>null</c> حينَ نَجَحَت أَو لَم تُحاوَل.
    /// والسِرُّ الَّذي يَتَحَقَّقُ بِه الوارِدُ لا يُبَدَّلُ إلّا حينَ تَنجَح — فَلا يَفتَرِقُ ما عِندَنا عَمّا عِندَ المُزَوِّد.</summary>
    public string? WebhookRefusal { get; set; }
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

    /// <summary>
    /// <para><b>أَيَقبِضُ هذا الرَبطُ فِعلاً؟</b> — دَفعٌ، وبِرابِطٍ
    /// مَملوءٍ يَنقُرُه الزَبون. والسُؤالُ يُطرَح هُنا مَرَّةً واحِدَةً
    /// لِأَنّ عَلَيه يَتَوَقَّف ظُهورُ الباقاتِ المَدفوعَة.</para>
    /// <para><b>والمِفتاحُ السِرِّيُّ المَختومُ لا يُعَدُّ هُنا بَعد — يُقالُ ولا يُبتلَع
    /// (‏2026-10-12، ‏ADR-032 §٧‑ج)</b>: الخُطوَة 7 قالَت «رابِطٌ مَملوءٌ <b>أَو</b>
    /// حَقلٌ سِرِّيٌّ مَختوم»، ولا عَميلَ يَطلُبُ بِالمِفتاحِ قَبلَ الخُطوَة 8 — فَعَدُّه
    /// يَقلِبُ <c>PaymentProviderConfigured</c> بِلا قابِض: تَظهَرُ الباقاتُ المَدفوعَةُ
    /// ويَبلُغُ زِرُّها <c>PlanPay</c> بِلا رابِطٍ يُصَيِّرُه (بابٌ يُجيبُ «غَيرُ مُتاح»).
    /// يُضافُ مَعَ العَميلِ بِسَطرٍ هُنا، ويَنقَلِبُ مَعَه صَفُّ
    /// <c>A_sealed_key_alone_collects_nothing_yet_so_no_card_and_no_paid_plan_opens</c>.</para>
    /// </summary>
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
            // **مَدخَلُ بائِعٍ لَيسَ رَبطَ مَتجَر — قاعِدَةٌ مُسَمّاةٌ لا فَرعُ تَحذير** (‏ADR-033 §٢‑ج‑٢، تَيّارُ S8، ‏2026-10-12):
            // مُعَرِّفُه `{capability}:{listing_owner}` ويُقرَأُ بِمِفتاحِ البائِع، فَيُتَجاوَزُ هُنا صامِتاً — وكانَ يَقَعُ في
            // «يَحمِلُ مُزَوِّدَ قُدرَةٍ أُخرى» بِسَطرِ خَطَإٍ لِكُلِّ قِراءَة. ومَتجَرٌ لَيسَ فيه إلّا مَداخِلُ بائِعينَ يَبقى `Platform` نَفسَه.
            if (TenantProviderBinding.IsVendorEntryId(doc.Id, out _, out _)) continue;

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
