using System.Security.Cryptography;
using ACommerce.Kit.Payments;
using ACommerce.Platform.Providers;
using ACommerce.Templates.Customer.Marketplace.Services.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

// ═══ التَحَقُّقُ مِن دَفعِ المُشتَري عِندَ بَوّابَةِ المَتجَر — ما وَراءَ زِرِّ صَفحَةِ العَودَة (‏ADR-032 §٢‑هـ الخُطوَة 9) ═══════════
//
// المُشتَري يَدفَعُ عَلى صَفحَةِ البَوّابَةِ المُستَضافَةِ ويَعودُ إلى `/{slug}/pay/return` — صَفحَةٌ **تَقرَأُ ولا تَكتُب** — فَيَضغَطُ زِرّاً
// واحِداً يُرسِلُ `POST /{slug}/payments/verify`. وهذِه الخِدمَةُ ما وَراءَ الزِرّ: تَسأَلُ أَهذِه صَفقَةُ صاحِبِ الجَلسَةِ وعَلَيها فاتورَة
// (`DealPayAuthorization` النَقِيَّة) **قَبلَ أَيِّ نِداءٍ خارِجيّ**، ثُمَّ تَفُكُّ مِفتاحَ المَتجَرِ مِن الخِزانَةِ لِهذا النِداءِ وَحدَه، وتَجلِبُ
// الفاتورَةَ بِه، وتَعرِضُها مَعَ صَفَقاتِها عَلى `GatewayPaymentProof.Gate`. **ولا تَكتُبُ شَيئاً في هذِه الخُطوَة**: كاتِبُ «مَدفوع» عَلى
// الصَفقَةِ خُطوَةُ ‏ADR-032 العاشِرَة (`ConfirmGatewayPaymentAsync`) ويَطلُبُ في تَوقيعِه ما تُخرِجُه البَوّابَة (`ProvenGatewayPayment`).
//
// **والعَودَةُ لَيسَت بُرهاناً، والزِرُّ لَيسَ بُرهاناً**: يَضغَطُه مَن دَفَعَ ومَن لَم يَدفَع — والبُرهانُ جَوابُ البَوّابَةِ بِمِفتاحِ المَتجَر.

/// <summary>
/// <para><b>مَن يَدفَعُ — سِجِلُّ سُلطَةٍ مِن الجَلسَة</b> (القاعِدَة ٦): يَبنيه سَطرُ النُقطَةِ مِن صاحِبِ الجَلسَةِ <b>قَبلَ أَن يُقرَأَ
/// حَقل</b>، ويَطلُبُه تَوقيعُ <see cref="GatewayPayments.VerifyAsync"/> — والقَرارُ «أَهذِه صَفقَتُه؟» في
/// <see cref="DealPayAuthorization.Validate"/> النَقِيَّة. نَسَقُ <c>DealShipper</c> و<c>DealAcceptor</c>.</para>
/// </summary>
public sealed record DealPayer(Guid ActorId);

/// <summary>خَرقٌ في سُلطَةِ الدافِع — <c>Code</c> مِن <see cref="DealPayAuthorization.All"/>.</summary>
public sealed record DealPayViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>أَيَتَحَقَّقُ هذا الدافِعُ مِن دَفعِ هذِه الصَفقَة؟</b> — دالَّةٌ نَقِيَّةٌ تَقرَؤُها صَفحَةُ العَودَةِ لِتَرسُمَ الزِرّ،
/// والخِدمَةُ قَبلَ أَن تَفُكَّ مِفتاحاً أَو تُنادي البَوّابَة: فَما يُرسَمُ هُوَ ما يُقبَل.</para>
/// </summary>
public static class DealPayAuthorization
{
    /// <summary>المُشتَرَكُ مَعَ القَبولِ بِحَرفِه — مِفتاحُ نَصٍّ واحِدٌ في كُلِّ مُعجَم.</summary>
    public const string DealNotFound = DealAcceptAuthorization.DealNotFound;

    /// <summary>الصَفقَةُ لَيسَت طَلَبَ صاحِبِ الجَلسَة — مُبادِرُها غَيرُه. (لا يُعادُ <c>actor_not_party</c>: نَصُّه في الإلغاء.)</summary>
    public const string ActorNotPayer = "actor_not_payer";

    /// <summary>لا فاتورَةَ بَوّابَةٍ مُسَجَّلَةً عَلى الصَفقَة (<see cref="GatewayPaymentProof.InvoiceRef"/>) — لَم تُدفَع عِندَ البَوّابَةِ أَصلاً.</summary>
    public const string InvoiceMissing = "gateway_invoice_missing";

    /// <summary>المَعجَمُ المُغلَق — ثَلاثَةٌ لا رابِعَ لَها.</summary>
    public static readonly IReadOnlyList<string> All = new[] { DealNotFound, ActorNotPayer, InvoiceMissing };

    /// <summary><c>null</c> يَعني «تَحَقَّق». ولا يَسأَلُ عَن حالِ الصَفقَة: دَفعٌ وَقَعَ عَلى صَفقَةٍ أُلغِيَت بَعدَه يُثبَتُ ولا يُبتلَع.</summary>
    public static DealPayViolation? Validate(Deal? deal, DealPayer by)
    {
        if (deal is null)
            return new(DealNotFound, "لا صَفقَةَ بِهذا المُعَرِّف.");

        if (deal.InitiatorId != by.ActorId)
            return new(ActorNotPayer, "الصَفقَةُ لَيسَت طَلَبَ صاحِبِ الجَلسَة — لا يَتَحَقَّقُ مِن دَفعِها غَيرُ مُشتَريها.");

        if (!deal.Refs.TryGetValue(GatewayPaymentProof.InvoiceRef, out var invoice) || string.IsNullOrWhiteSpace(invoice))
            return new(InvoiceMissing, "لا فاتورَةَ بَوّابَةٍ مُسَجَّلَةً عَلى الصَفقَة — لَم تُدفَع عِندَ بَوّابَةِ المَتجَر.");

        return null;
    }

    public static bool IsAllowed(Deal? deal, DealPayer by) => Validate(deal, by) is null;
}

/// <summary>نَتيجَةُ التَحَقُّق: دَفعٌ ثَبَتَ (<see cref="Payment"/>)، أَو رَمزٌ مِن <see cref="GatewayReturn.Codes"/> — ولا ثالِث.
/// و<see cref="DealId"/> الصَفقَةُ المَرجِعُ حَيثُ وُجِدَت، فَيَعودُ إلَيها الرَدّ.</summary>
public sealed record GatewayVerifyResult(Guid? DealId, ProvenGatewayPayment? Payment, string? Code)
{
    public bool Ok => Payment is not null;
}

/// <summary>
/// <para><b>عَقدُ صَفحَةِ العَودَةِ — مَوضِعٌ واحِدٌ يَقرَؤُه كاتِبُ الرابِطِ والصَفحَةُ والنُقطَة</b>: اسمُ المُعامِلِ الَّذي يَحمِلُ
/// الصَفقَةَ المَرجِع، وقِراءَتُه، ومَسارُ الرَدِّ بَعدَ التَحَقُّق، ومَعجَمُ رُموزِ الشَريطِ ومَفاتيحُها في القامُوس (نَسَقُ
/// <c>DealRefusalTexts</c>: الرَمزُ مِن الجَدوَلِ والكَلامُ في المَعجَم — القاعِدَة ١١).</para>
/// </summary>
public static class GatewayReturn
{
    /// <summary>مَسارُ صَفحَةِ العَودَةِ داخِلَ المَتجَر — بِلا شَرطَةٍ أُولى، كَما يَبنيه <c>AuthSession.LinkFor</c>.</summary>
    public const string ReturnPath = "pay/return";

    /// <summary>اسمُ المُعامِلِ الَّذي يَحمِلُ الصَفقَةَ المَرجِع — في رابِطِ العَودَةِ وفي حَقلِ استِمارَةِ التَحَقُّقِ مَعاً.</summary>
    public const string DealKey = "deal";

    /// <summary>
    /// <para><b>الصَفقَةُ المَرجِعُ مِن قيمَةِ المُعامِل</b> — المُعَرِّفُ بِصيغَةِ <c>N</c> (‏32 مِحرَفاً) <b>في رَأسِ القيمَة</b>، وما بَعدَه
    /// يُتَجاوَز. والسَبَبُ مَقروءٌ لا مُتَوَهَّم: وَثيقَةُ «Create Invoice» (مَقروءَةً ‏2026-10-12) تَقولُ إنّ <c>success_url</c> «An endpoint
    /// where the payer will be redirected to when the invoice is paid» و<c>back_url</c> «used for redirect when the user clicks on the
    /// back button» — <b>ولا تَقولُ أَتُلحِقُ البَوّابَةُ بِالرابِطِ مُعامِلاتٍ أَم لا، ولا كَيف</b>. فَإلحاقٌ يَلصَقُ «?…» بِقيمَتِنا لا
    /// يُضَيِّعُ عَلى مُشتَرٍ دَفَعَ صَفقَتَه. ولا يُمنَحُ بِهذا شَيء: الصَفقَةُ تُحَمَّلُ ثُمَّ تُسأَلُ <see cref="DealPayAuthorization"/>.</para>
    /// </summary>
    public static Guid? DealIdOf(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var head = raw.Length > 32 ? raw[..32] : raw;
        return Guid.TryParseExact(head, "N", out var id) ? id : null;
    }

    /// <summary>مَسارُ صَفحَةِ العَودَةِ بِصَفقَتِها ورَمزِها — والرَمزُ مِن مَعجَمٍ مُغلَقٍ بِحُروفٍ لا تَحتاجُ تَرميزاً.</summary>
    public static string PathFor(Guid? dealId, string? code)
    {
        var query = new List<string>(2);
        if (dealId is { } id) query.Add($"{DealKey}={id:N}");
        if (!string.IsNullOrEmpty(code)) query.Add($"err={code}");
        return query.Count == 0 ? ReturnPath : ReturnPath + "?" + string.Join('&', query);
    }

    /// <summary>
    /// <para><b>أَينَ يَعودُ الزِرّ</b>: دَفعٌ ثَبَتَ ← صَفحَةُ الصَفقَة؛ وما سِواه ← صَفحَةُ العَودَةِ بِرَمزِه، فَيُقرَأُ الرَفضُ ولا يُبتلَع.</para>
    /// </summary>
    public static string AfterVerify(GatewayVerifyResult r)
        => r.Ok && r.DealId is { } id ? $"deals/{id}" : PathFor(r.DealId, r.Code);

    /// <summary>بادِئَةُ مَفاتيحِ الشَريطِ — اصطِلاحُ القامُوسِ ثَلاثَةُ مَقاطِع (<c>LocaleValidator.KeyPattern</c>)، فَالرَمزُ في الثالِث.</summary>
    public const string TextPrefix = "pay.return.err_";

    /// <summary>كُلُّ رَمزٍ قَد يَبلُغُ الصَفحَةَ — سُلطَةُ الدافِع، ثُمَّ مَعجَمُ فَشَلِ العَميلِ الخُماسيّ، ثُمَّ رُموزُ بَوّابَةِ البُرهانِ
    /// الخَمسَة. ثَلاثَةَ عَشَرَ لا رابِعَ عَشَر.</summary>
    public static readonly IReadOnlyList<string> Codes =
        DealPayAuthorization.All
            .Concat(MoyasarFailures.All)
            .Concat(GatewayPaymentProof.Codes)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>مِفتاحُ النَصِّ لِرَمزٍ مَعروف، و<c>null</c> لِما سِواه — فَلا يُرسَمُ شَريطٌ لِرَمزٍ لا نَصَّ لَه، ولا مِفتاحٌ خام.</summary>
    public static string? KeyFor(string? code)
        => code is not null && Codes.Contains(code, StringComparer.Ordinal) ? TextPrefix + code : null;
}

/// <summary>
/// <para><b>الدَفعُ عِندَ بَوّابَةِ المَتجَر — مِن جِهَةِ الصَفقَة</b>. تَقرَأُ صَفقَةً، وتَفُكُّ مِفتاحَ المَتجَرِ لِلنِداءِ الواحِد،
/// وتَسأَلُ البَوّابَة. <b>ولا تَكتُبُ في هذِه الخُطوَة</b> — وصِفرُ كِتابَةٍ مَقيسٌ في <c>TenantGatewayEndpointsTests</c>.</para>
/// </summary>
public sealed class GatewayPayments
{
    private readonly DealsService _deals;
    private readonly TenantProviderService _providers;
    private readonly ProviderVault _vault;
    private readonly MoyasarClient _client;
    private readonly ILogger<GatewayPayments> _log;

    public GatewayPayments(DealsService deals, TenantProviderService providers, ProviderVault vault, MoyasarClient client,
        ILogger<GatewayPayments>? log = null)
    {
        _deals = deals;
        _providers = providers;
        _vault = vault;
        _client = client;
        _log = log ?? NullLogger<GatewayPayments>.Instance;
    }

    /// <summary>
    /// <para><b>حَقلُ المِفتاحِ المَختومِ في رَبطِ الدَفع</b> — الحَقلُ الَّذي يُعلِنُه التَعريفُ بِنَوعِ <c>secret_key</c> وقيمَتُه مَختومَة؛
    /// و<c>null</c> لِرَبطٍ غائِبٍ أَو لِقُدرَةٍ أُخرى أَو بِلا مِفتاحٍ مَختوم. <b>بِالنَوعِ لا بِسلاجِ المُزَوِّد</b> — الكاتالوجُ بَيانات،
    /// و<c>Exactly_one_payments_definition_carries_a_sealed_secret_key_field</c> يَحمَرُّ يَومَ يَصيرُ فيه ثانٍ.</para>
    /// </summary>
    public static ProviderFieldDefinition? SealedKeyFieldOf(ResolvedProvider? payments)
        => payments is not null && payments.Capability == ProviderCapabilities.Payments
            ? payments.Definition.Credential.Fields.FirstOrDefault(f =>
                f.Kind == CredentialKinds.SecretKey && payments.Values.TryGetValue(f.Code, out var v) && v.IsSealed)
            : null;

    /// <summary>
    /// <para><b>التَحَقُّقُ مِن دَفعِ صَفقَةٍ عِندَ البَوّابَة — بِالتَرتيب، ولا يُقلَب</b>:</para>
    /// <list type="number">
    ///   <item><b>السُلطَةُ أَوَّلاً</b>: الصَفقَةُ المَرجِعُ صَفقَةُ <paramref name="by"/> وعَلَيها فاتورَة — وإلّا رَمزُها، <b>بِلا فَكِّ
    ///   مِفتاحٍ ولا نِداء</b>.</item>
    ///   <item>مِفتاحُ المَتجَرِ يُفَكُّ لِهذا النِداءِ وَحدَه — وغِيابُه (لا رَبط، لا خِزانَة، ظَرفٌ لا يُفتَحُ بِسِياقِه) <see cref="MoyasarFailures.NotConfigured"/>.</item>
    ///   <item>الفاتورَةُ تُجلَبُ بِه بِمُعَرِّفِها المُسَجَّلِ عَلى الصَفقَة — وفَشَلُ الجَلبِ رَمزُه مِن مَعجَمِ العَميل.</item>
    ///   <item>والصَفَقاتُ الَّتي سَمَّتها بَياناتُ الفاتورَةِ — مِن صَفَقاتِ هذا الدافِعِ وَحدَه، ومَعَها المَرجِع — تُعرَضُ عَلى
    ///   <see cref="GatewayPaymentProof.Gate"/>: فاتورَةٌ تُسَمّي صَفقَةَ غَيرِه أَو لا تُسَمّي المَرجِعَ لا تَعبُر.</item>
    /// </list>
    /// <para><b>ولا يُمَرَّرُ إلغاءُ الطَلَبِ مِن النُقطَة</b> (نَسَقُ <c>/ship</c>): مُهلَةُ العَميلِ تَحُدُّ النِداء.</para>
    /// </summary>
    public async Task<GatewayVerifyResult> VerifyAsync(
        string tenantSlug, DealPayer by, string? dealRef, CancellationToken ct = default)
    {
        var deal = GatewayReturn.DealIdOf(dealRef) is { } id ? await _deals.LoadAsync(tenantSlug, id, ct) : null;
        if (DealPayAuthorization.Validate(deal, by) is { } refusal)
            return Logged(tenantSlug, new(deal?.Id, null, refusal.Code));

        if (await SecretKeyAsync(tenantSlug, ct) is not { } key)
            return Logged(tenantSlug, new(deal!.Id, null, MoyasarFailures.NotConfigured));

        var fetched = await _client.FetchInvoiceAsync(key, deal!.Refs[GatewayPaymentProof.InvoiceRef], ct);
        if (!fetched.Ok)
            return Logged(tenantSlug, new(deal.Id, null, fetched.Failure!.Code));

        var invoice = fetched.Value!;
        var admitted = GatewayPaymentProof.Gate(invoice, await InvoiceDealsAsync(tenantSlug, deal, invoice, by, ct));
        return Logged(tenantSlug, admitted.Payment is { } proven
            ? new(deal.Id, proven, null)
            : new(deal.Id, null, admitted.Violation!.Code));
    }

    /// <summary>
    /// <para><b>مِفتاحُ المَتجَرِ صَريحاً — لِلنِداءِ الواحِدِ ولا يُحفَظ</b>. الرَبطُ يُقرَأُ بِمُعَرِّفِه (<c>CurrentAsync</c> — قِراءَةٌ حَيَّةٌ
    /// لا لَقطَةُ الكاش: رَبطٌ سُحِبَ الآنَ لا يُطلَبُ بِه) ويُحَلُّ بِـ<c>TenantProviderSet.FromDocuments</c> نَفسِها الَّتي تَحُلُّ لَقطَةَ
    /// المَتجَر — فَلا قاعِدَةَ قَبولٍ ثانِيَة. ويُفَكُّ بِسِياقِ <c>tenant|payments|field</c> الَّذي خَتَمَه بِه سَطحُ الرَبط
    /// (<see cref="ProviderBindSurface.AadFor"/>)؛ وظَرفٌ لا يُفتَحُ بِسِياقِه — مَنسوخٌ مِن مَتجَرٍ آخَر، أَو مِفتاحٌ رَئيسٌ آخَر — «لا مِفتاح».</para>
    /// <para><b>واللوغُ لا يَحمِلُ المِفتاحَ ولا ذَيلَه</b>: المَتجَرُ وَحدَه.</para>
    /// </summary>
    private async Task<string?> SecretKeyAsync(string tenantSlug, CancellationToken ct)
    {
        if (!_vault.IsAvailable) return null;

        var current = await _providers.CurrentAsync(tenantSlug, ProviderCapabilities.Payments, ct);
        var payments = TenantProviderSet
            .FromDocuments(tenantSlug, current is null ? Array.Empty<TenantProviderBinding>() : new[] { current })
            .For(ProviderCapabilities.Payments);
        if (SealedKeyFieldOf(payments) is not { } field) return null;

        try
        {
            return payments!.Values[field.Code].Unseal(_vault, ProviderBindSurface.AadFor(tenantSlug, payments.Capability, field.Code));
        }
        catch (CryptographicException)
        {
            _log.LogWarning("[gateway] {Slug} payments key does not open — no key", tenantSlug);
            return null;
        }
    }

    /// <summary>
    /// <para><b>الصَفَقاتُ الَّتي تُعرَضُ عَلى البَوّابَة</b>: المَرجِعُ أَوَّلاً، ثُمَّ ما سَمَّتهُ بَياناتُ الفاتورَةِ مِن صَفَقاتِ هذا
    /// الدافِعِ وَحدَه — وغَيرُ صَفَقاتِه لا تُحَمَّلُ في القائِمَة، فَتَحمَرُّ البَوّابَةُ بِـ<see cref="GatewayPaymentProof.DealsMismatch"/>.
    /// وبَياناتٌ لا تُقرَأُ أَو أَطوَلُ مِن فاتورَةٍ واحِدَةٍ (<see cref="GatewayPaymentProof.MaxDealsPerInvoice"/>) لا يُحَمَّلُ مِنها شَيء —
    /// فَالتَحميلُ مَحدودٌ بِحَدِّ الوَثيقَة.</para>
    /// </summary>
    private async Task<IReadOnlyList<Deal>> InvoiceDealsAsync(
        string tenantSlug, Deal referenced, MoyasarInvoice invoice, DealPayer by, CancellationToken ct)
    {
        var deals = new List<Deal> { referenced };
        var listed = GatewayPaymentProof.DealIdsIn(invoice.Metadata);
        if (listed is null || listed.Count > GatewayPaymentProof.MaxDealsPerInvoice) return deals;

        foreach (var id in listed)
        {
            if (id == referenced.Id) continue;
            if (await _deals.LoadAsync(tenantSlug, id, ct) is { } d && d.InitiatorId == by.ActorId) deals.Add(d);
        }
        return deals;
    }

    /// <summary>سَطرُ اللوغ: المَتجَرُ والصَفقَةُ والرَمز — لا مِفتاحٌ ولا رابِطُ فاتورَة.</summary>
    private GatewayVerifyResult Logged(string tenantSlug, GatewayVerifyResult r)
    {
        _log.LogInformation("[gateway] verify {Slug} deal {Deal} ← {Code}",
            tenantSlug, r.DealId?.ToString("D") ?? "—", r.Ok ? "proven" : r.Code);
        return r;
    }
}
