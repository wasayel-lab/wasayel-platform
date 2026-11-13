using System.Text.Json;
using ACommerce.Platform.Shared.Metering;

namespace ACommerce.Kit.Roles;

/// <summary>
/// <para><b>ما يَحمِلُه مِترُ الدَور</b> (‏2026-11-13، الموجَة ١٣ W13-6·١٧ — ‏ADR-051 §٢١؛ التَصميم §7 و§3.4) — قيمَتانِ لا ثالِثَةَ لَهُما:
/// وَحدَةُ الحُضورِ الَّتي مَوضوعُها الدَور (<see cref="PresenceRole"/>: كُلُّ دَورٍ مُجَسَّدٍ في مَتجَرٍ حُضورٌ لَه كُلَّ يَوم)، أَو <see cref="None"/>.
/// والجانِبُ جانِبُ التِقنِيَة: ما فَعَّلَه المَتجَرُ يُعَدُّ حُضوراً، لا ما يَفعَلُه.</para>
/// <para><b>قارِئُ القيمَةِ في وَقتِ التَشغيل</b> لَقطُ الحُضورِ اليَومِيُّ (البَند ١٩) عَبرَ <see cref="TenantRoleSet.MeterOf"/>، ومَعَه تَدخُلُ
/// <c>presence.role</c> مُعجَمَ الوَحَداتِ بِمَوضِعِ عَدِّها. وإلى أَن يُبنى فَقارِئُها بَوّابَةُ الإقلاعِ وَحدَها (<see cref="RoleDefinitionLoader"/>) — دَينٌ
/// مُعلَنٌ في §٢١ لا يُبتَلَع (القاعِدَة ١)، كَدَينِ مِترِ المُزَوِّدِ في §٢٠.</para>
/// </summary>
public static class RoleMeterUnits
{
    /// <summary>كُلُّ دَورٍ مُجَسَّدٍ في <c>Tenant.Roles</c> حُضورٌ لِمَتجَرِه كُلَّ يَوم (التَصميم §3.4).</summary>
    public const string PresenceRole = "presence.role";

    /// <summary>لا حُضورَ يُعَدّ — قيمَةٌ تُكتَبُ صَراحَةً لا غِياب.</summary>
    public const string None = "none";

    /// <summary>المَعجَمُ بِتَرتيبِه.</summary>
    public static readonly IReadOnlyList<string> All = [PresenceRole, None];

    /// <summary>وَحَداتُ الحُضورِ الَّتي مَوضوعُها الدَور — ما يُقبَلُ في الحَقلِ سِوى <see cref="None"/>.</summary>
    public static readonly IReadOnlyList<string> RolePresence = [PresenceRole];

    /// <summary>
    /// <para><b>مِترُ دَورٍ لا مِلَفَّ لَه في الكاتالوج</b> — الدَورُ المُؤَلَّفُ وما خُزِّنَ بِلا تَعريف: تُسنِدُه المَنَصَّةُ ولا يُؤَلِّفُه المُستَأجِر.
    /// والقيمَةُ افتِراضُ السُؤالِ ٤ في التَصميم §17 («الأَدوارُ تُعَدُّ لِكُلِّ مَتجَرٍ في اليَوم») إلى أَن يُجيبَ المالِك؛ والعَدُّ هُوَ الجانِبُ القابِلُ
    /// لِلنَقض: يَومٌ لَم يُعَدَّ لا يُعادُ عَدُّه، وسَطرٌ عُدَّ يَبقى لِمَن يَقرَؤُه لاحِقاً أَن يَتَجاهَلَه.</para>
    /// </summary>
    public const string Derived = PresenceRole;
}

/// <summary>شَكلُ القاعِدَة ٤ حَرفاً: رَمزٌ ثابِتٌ ورِسالَةٌ عَرَبِيَّة.</summary>
public sealed record RoleMeterViolation(string Code, string MessageAr);

/// <summary>
/// <para><b>مُصادِقُ مِترِ الدَور — ثَلاثَةُ رُموزٍ لِمِلَفّاتِ الكاتالوج ورَمزٌ واحِدٌ لِتَعريفِ المُستَأجِر، ولِكُلٍّ اختِبارٌ موجِبٌ وسالِب</b>
/// (القاعِدَة ٤؛ التَصميم §7). <b>دالَّتانِ مُنفَصِلَتانِ لا عَلَم</b> — بِمَبدَإِ <see cref="RoleDefinitionValidator.ValidateTenantDefinition"/> نَفسِه:
/// <see cref="RoleDefinitionValidator"/> بِرُموزِه لا يُمَسّ، فَتَعريفٌ بِلا مِترٍ يَبقى صالِحاً عِندَه (تَوصيفا <c>HostsThatServeCharacterizationTests</c>
/// و<c>RoleReadBeltCharacterizationTests</c> بِلا حَرف)، والمِترُ يُسأَلُ هُنا.</para>
///
/// <para><b><see cref="Validate"/> لِمِلَفّاتِ الكاتالوج</b>، يَسأَلُه المُحَمِّلُ عِندَ الإقلاع: القيمَةُ مِن <see cref="RoleMeterUnits.All"/>، و«لَيسَت
/// وَحدَةً» (<c>meter_unit_unknown</c>) غَيرُ «وَحدَةٌ لَيسَت حُضورَ دَور» (<c>meter_unit_not_role_presence</c> — <c>deal.create</c> عَلى دَورٍ مَثَلاً)،
/// والفَرقُ يَعرِفُه مُعجَمُ الوَحَداتِ (<see cref="MeterUnitCatalog"/>). ووَحدَةُ حُضورِ الدَورِ إن دَخَلَت المُعجَمَ (البَند ١٩) وَجَبَ أَن تَكونَ حُضوراً فيه.</para>
///
/// <para><b><see cref="ValidateTenantMeter"/> لِتَعريفِ المُستَأجِر — عِندَ بابَي الكِتابَةِ وَحدَهُما</b> (اقتِراحُ الوَكيلِ <c>AgentService.DefineRoleAsync</c>،
/// واعتِمادُ المُشرِفِ <c>TenantRoleService.ValidateBeforeApprove</c>)، <b>لا عِندَ حِزامِ القِراءَة</b> — سابِقَةُ <c>slug_shadows_role_host</c>: قاعِدَةٌ جَديدَةٌ
/// تُغلِقُ البابَ أَمامَ ما يَأتي ولا تَمحو ما اعتُمِد. ولِماذا يُرَدُّ المِفتاحُ أَصلاً (التَصميم §7، ثَلاثَةُ أَسبابٍ مَقيسَة): قَرارُ المالِكِ أَنّ تَصنيفَ ما يُعَدُّ
/// لا يَسكُنُ تَعريفاً يُؤَلِّفُه مُستَأجِرٌ أَو وَكيل؛ ومَنطِقُ «المُستَأجِرُ لا يُؤَلِّفُ تَعريفَ مُزَوِّد»؛ <b>وخَطَرُ تَراجُعِ الثُنائِيّ</b>: وَثيقَةُ مُستَأجِرٍ
/// تَحمِلُ المِفتاحَ يَقرَؤُها ثُنائِيٌّ أَقدَمُ بِلا الحَقلِ فَيَرمي (<c>UnmappedMemberHandling.Disallow</c>) ويُسقِطُ الدَورَ المُعتَمَدَ وأَعضاءَه بِسَطرِ
/// تَحذيرٍ وَحدَه. <b>ولِذلك يُفحَصُ المِفتاحُ في النَصِّ لا القيمَةُ في الكائِن</b>: <c>"meter": null</c> يُقرَأُ <c>null</c> ويُسقِطُ الثُنائِيَّ الأَقدَمَ كَغَيرِه.</para>
/// </summary>
public static class RoleMeterValidator
{
    /// <summary>رُموزُ <see cref="Validate"/> الثَلاثَة — مُعلَنَةً لِيُقاسَ أَنّ لِكُلٍّ اختِبارَين.</summary>
    public static readonly IReadOnlyList<string> Codes =
    [
        "meter_missing",
        "meter_unit_unknown",
        "meter_unit_not_role_presence",
    ];

    /// <summary>رَمزُ <see cref="ValidateTenantMeter"/> الواحِد.</summary>
    public static readonly IReadOnlyList<string> TenantCodes =
    [
        MeterAuthoredByTenant,
    ];

    /// <summary>رَمزُ خَرقِ <see cref="ValidateTenantMeter"/>: تَعريفُ مُستَأجِرٍ يَحمِلُ مِفتاحَ المِتر.</summary>
    public const string MeterAuthoredByTenant = "meter_authored_by_tenant";

    /// <summary>اسمُ المِفتاحِ في نَصِّ التَعريف — بِسِياسَةِ تَسمِيَةِ <see cref="RoleDefinitionLoader"/> (<c>camelCase</c>، حَسّاسَةً لِلحالَة؛ و«Meter»
    /// مِفتاحٌ مَجهولٌ تَرُدُّه القِراءَةُ قَبلَ هذا).</summary>
    public const string MeterKey = "meter";

    /// <summary>كُلُّ خُروقِ مِترِ تَعريفِ كاتالوج، بِمُعجَمِ الوَحَداتِ المَضمون.</summary>
    public static IReadOnlyList<RoleMeterViolation> Validate(RoleDefinition d) => Validate(d, MeterUnitCatalog.Default);

    /// <summary>
    /// <para>كُلُّ خُروقِ مِترِ تَعريفِ كاتالوجٍ بِمُعجَمٍ مُعطىً — والقائِمَةُ الفارِغَةُ مِترٌ صالِح. (‏ADR-038: مُصادِقُ بَياناتٍ يُجيبُ بِكُلِّ خُروقِه؛
    /// والثَلاثَةُ مُتَعاقِبَةٌ بِطَبعِها: لا وَحدَةَ تُسأَلُ بِلا مِتر، ولا يَكونُ المِترُ مَجهولاً وحُضوراً لِغَيرِ الدَورِ مَعاً.)</para>
    /// </summary>
    public static IReadOnlyList<RoleMeterViolation> Validate(RoleDefinition d, MeterUnitCatalog units)
    {
        var v = new List<RoleMeterViolation>();
        var meter = d.Meter;

        if (string.IsNullOrWhiteSpace(meter))
        {
            v.Add(new("meter_missing",
                $"الدَور «{d.Slug}» بِلا مِتر — كُلُّ مِلَفِّ دَورٍ يُعلِنُ كَيفَ يُعَدّ (\"meter\": \"{RoleMeterUnits.PresenceRole}\" أَو \"{RoleMeterUnits.None}\")، " +
                $"و«{RoleMeterUnits.None}» قيمَةٌ تُكتَبُ لا غِياب."));
            return v;
        }

        if (string.Equals(meter, RoleMeterUnits.None, StringComparison.Ordinal))
            return v;

        var unit = units.Find(meter);
        if (RoleMeterUnits.RolePresence.Contains(meter, StringComparer.Ordinal))
        {
            // ‏وَحدَةُ حُضورِ الدَورِ تَدخُلُ المُعجَمَ مَعَ مَوضِعِ عَدِّها (البَند ١٩)؛ فَإن دَخَلَتهُ بِحَدٍّ آخَرَ فَهِيَ لَيسَت حُضوراً.
            if (unit is not null && !string.Equals(unit.Boundary, MeterBoundaries.Presence, StringComparison.Ordinal))
                v.Add(new("meter_unit_not_role_presence",
                    $"مِترُ الدَورِ «{d.Slug}» = «{meter}»، والمُعجَمُ يَعُدُّها عِندَ الحَدِّ «{unit.Boundary}» لا حُضوراً — مِترُ الدَورِ حُضورٌ أَو «{RoleMeterUnits.None}»."));
            return v;
        }

        if (unit is null)
            v.Add(new("meter_unit_unknown",
                $"مِترُ الدَورِ «{d.Slug}» = «{meter}» لَيسَ وَحدَةً في مُعجَمِ الوَحَدات. المَقبول: {string.Join("، ", RoleMeterUnits.All)}."));
        else
            v.Add(new("meter_unit_not_role_presence",
                $"مِترُ الدَورِ «{d.Slug}» = «{meter}» وَحدَةٌ لَيسَ مَوضوعُها الدَور (الحَدّ «{unit.Boundary}»، الجانِب «{unit.Side}») — " +
                $"المَقبول: {string.Join("، ", RoleMeterUnits.All)}."));

        return v;
    }

    public static bool IsValid(RoleDefinition d) => Validate(d).Count == 0;

    /// <summary>
    /// <para><b>بابُ الكِتابَة: تَعريفُ المُستَأجِرِ لا يَحمِلُ مِفتاحَ المِتر</b> — بِأَيِّ قيمَةٍ كانَ، ومِنها <c>null</c>. يُفحَصُ النَصُّ كَما كُتِبَ (ما
    /// يُخَزَّنُ حَرفاً في <c>TenantRoleDefinition.DefinitionJson</c>)، ويُسأَلُ بَعدَ أَن قَرَأَه <see cref="RoleDefinitionLoader.ParseDefinition"/>: نَصٌّ لا
    /// يُقرَأُ رَدُّه هُناك، وهُنا يَرمي كَما يَرمي هُناك (فَصلُ «تَعَذَّرَت القِراءَة» عَن «قُرِئَ وخالَف»).</para>
    /// </summary>
    public static IReadOnlyList<RoleMeterViolation> ValidateTenantMeter(string definitionJson)
    {
        var v = new List<RoleMeterViolation>();
        using var doc = JsonDocument.Parse(definitionJson);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return v;

        if (root.EnumerateObject().Any(p => p.NameEquals(MeterKey)))
        {
            var slug = root.TryGetProperty("slug", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            v.Add(new(MeterAuthoredByTenant,
                $"تَعريفُ الدَورِ «{slug}» يَحمِلُ مِفتاحَ \"{MeterKey}\" — مِترُ الدَورِ المُؤَلَّفِ تُسنِدُه المَنَصَّةُ ولا يُؤَلِّفُه مُستَأجِرٌ ولا وَكيل. " +
                "احذِف المِفتاحَ وأَعِد الاقتِراح."));
        }
        return v;
    }

    public static bool IsValidTenantMeter(string definitionJson) => ValidateTenantMeter(definitionJson).Count == 0;
}
