using System.Globalization;
using ACommerce.Kit.Roles;
using ACommerce.Kit.Subscriptions;
using ACommerce.Kit.Tenants;
using ACommerce.Kit.Theme;
using ACommerce.Platform.Flows;
using ACommerce.Platform.Providers;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using Marten;

namespace ACommerce.Templates.Customer.Marketplace.Services.Metering;

/// <summary>
/// <para><b>وَحَداتُ جانِبِ التِقنِيَةِ التِسع</b> (‏2026-11-13، الموجَة ١٣ W13-6·١٩ — ‏ADR-051 §٢٣؛ التَصميم §3.4 و§8): ما فَعَّلَه المَتجَرُ يُعَدُّ حُضوراً
/// لِكُلِّ مَتجَرٍ في اليَوم — رُموزُها في <c>meter-units.json</c> (الحَدُّ <c>presence</c>، الجانِبُ <c>technology</c>)، ومُصدِرُها الوَحيدُ <see cref="PresenceReader.Entries"/>.</para>
/// </summary>
public static class PresenceUnits
{
    /// <summary>المَتجَرُ نَفسُه حاضِراً — كُلُّ مَتجَرٍ غَيرِ مُعَلَّق (<c>Tenant.IsSuspended</c>)، كَمِّيَّتُه 1.</summary>
    public const string Store = "presence.store";
    /// <summary>دَورٌ مُجَسَّدٌ في المَتجَرِ مِترُه المُشتَقُّ هذه الوَحدَة (<see cref="TenantRoleSet.MeterOf"/>) — سَطرٌ لِكُلِّ دَور.</summary>
    public const string Role = RoleMeterUnits.PresenceRole;
    /// <summary>تَعريفاتُ الأَدوارِ الَّتي أَلَّفَها المَتجَرُ واعتُمِدَت — كَمِّيَّتُها عَدَدُها.</summary>
    public const string AuthoredRole = "presence.authored_role";
    /// <summary>تَعريفاتُ الثيمِ المُعتَمَدَة — كَمِّيَّتُها عَدَدُها.</summary>
    public const string AuthoredTheme = "presence.authored_theme";
    /// <summary>تَعريفاتُ باقاتِ الأَعضاءِ المُعتَمَدَة — كَمِّيَّتُها عَدَدُها.</summary>
    public const string MemberPlan = "presence.member_plan";
    /// <summary>رَبطُ مُزَوِّدٍ نَشِطٌ لِلمَتجَر (لا مَدخَلَ بائِع) مِترُ تَعريفِه <see cref="ProviderMeterPresences.StoreBinding"/> — سَطرٌ لِكُلِّ رَبط.</summary>
    public const string ProviderBinding = "presence.provider_binding";
    /// <summary>مَداخِلُ البائِعينَ النَشِطَةُ عِندَ مُزَوِّدٍ مِترُه <see cref="ProviderMeterPresences.VendorBinding"/> — سَطرٌ لِكُلِّ مُزَوِّدٍ كَمِّيَّتُه عَدَدُها.</summary>
    public const string VendorBinding = "presence.vendor_binding";
    /// <summary>مَفاتيحُ API النَشِطَةُ لِلمَتجَر — كَمِّيَّتُها عَدَدُها.</summary>
    public const string ApiKey = "presence.api_key";
    /// <summary>قَناةُ دُخولِ المَتجَر (<c>Tenant.AuthChannel</c>) — كَمِّيَّتُها 1 ومَصدَرُها القَناة.</summary>
    public const string AuthChannel = "presence.auth_channel";

    public static readonly IReadOnlyList<string> All =
        [Store, Role, AuthoredRole, AuthoredTheme, MemberPlan, ProviderBinding, VendorBinding, ApiKey, AuthChannel];
}

/// <summary>دَورٌ مُجَسَّدٌ في مَتجَر — سلاجُه في المَتجَرِ وسلاجُ كاتالوجِه (ما يَسأَلُ عَنه <see cref="TenantRoleSet.MeterOf"/>).</summary>
public sealed record PresenceRoleFact(string Slug, string CatalogSlug);

/// <summary>رَبطُ مُزَوِّدٍ نَشِط — مُعَرِّفُه (القُدرَةُ، أَو <c>{capability}:{listing_owner}</c> لِمَدخَلِ البائِع) وسلاجُ تَعريفِه.</summary>
public sealed record PresenceBindingFact(string Id, string ProviderSlug)
{
    /// <summary>مَدخَلُ بائِعٍ بِالقاعِدَةِ المُسَمّاةِ نَفسِها (<see cref="TenantProviderBinding.IsVendorEntryId"/>).</summary>
    public bool IsVendorEntry => TenantProviderBinding.IsVendorEntryId(Id, out _, out _);
}

/// <summary>
/// <para><b>ما قُرِئَ لِمَتجَرٍ واحِد</b> — الحَقيقَةُ المَخزونَةُ وَحدَها (التَصميم §8: «المَتاجِرُ الخامِلَةُ تُعَدُّ أَيضاً»): وَثيقَةُ المَتجَر، وأَدوارُه المُجَسَّدَةُ
/// (<c>Tenant.Roles</c> ومَعَها أَدوارُه المُؤَلَّفَةُ المُعتَمَدَةُ بِـ<see cref="TenantRoleSet.Materialize"/> — كَما يُجَسِّدُها <c>TenantRoleService.LoadAsync</c> في الذاكِرَة)،
/// وعَدَدُ تَعريفاتِه المُعتَمَدَةِ الثَلاثَة، ورَبطاتُه النَشِطَة، ومَفاتيحُه النَشِطَة.</para>
/// </summary>
public sealed record PresenceStoreFacts(
    string Slug, Guid OwnerUserId, bool IsSuspended, string AuthChannel,
    TenantRoleSet RoleSet, IReadOnlyList<PresenceRoleFact> Roles,
    int AuthoredRoles, int AuthoredThemes, int MemberPlans,
    IReadOnlyList<PresenceBindingFact> Bindings, int ApiKeys);

/// <summary>
/// <para><b>التَعريفاتُ الَّتي تَختارُ وَحدَةَ كُلِّ سَطر</b> (التَصميم §7 و§3.4، RULES-10): مِترُ حُضورِ المُزَوِّدِ مِن تَعريفِه
/// (<see cref="ProviderDefinition.Meter"/>) ومِترُ الدَورِ المُشتَقُّ (<see cref="TenantRoleSet.MeterOf"/>). <see cref="Platform"/> ما يُقلِعُ بِه التَطبيق — كاتالوجُ
/// المُزَوِّدينَ المَضمونُ ومِترُ الكاتالوج؛ ونُسخَةٌ مِنهُما تُقاسُ بِها «مِترٌ تَغَيَّرَ في التَعريفِ فَتَغَيَّرَ ما يُعَدّ» (G12/G15).</para>
/// </summary>
public sealed record PresenceDefinitions(IReadOnlyList<ProviderDefinition> Providers, Func<TenantRoleSet, string, string> RoleMeter)
{
    public static readonly PresenceDefinitions Platform =
        new(ProviderCatalog.Definitions, static (set, catalogSlug) => set.MeterOf(catalogSlug));
}

/// <summary>
/// <para><b>قارِئُ الحُضور — يَقرَأُ ثُمَّ يُخَطِّطُ، ولا يَكتُب</b> (‏2026-11-13، الموجَة ١٣ W13-6·١٩ — ‏ADR-051 §٢٣؛ التَصميم §8 الخُطوَة ٢): <see cref="ReadAsync"/>
/// جَلَساتُ قِراءَةٍ وَحدَها (<c>QuerySession</c>) — المَتاجِرُ والمَفاتيحُ مِن الجَلسَةِ العامَّة (وَثيقَتانِ وَحيدَتا الإيجار)، وما سِواهُما بِجَلسَةِ كُلِّ مَتجَرٍ فَالعَزلُ
/// بُنيَوِيٌّ (لا <c>AnyTenant</c>). <b>وقِراءَةٌ تَرمي تَرمي</b>: لا سُقوطَ إلى «لا شَيء» كَما يَسقُطُ حِزامُ القِراءَةِ لِلصَفحَة — لَقطَةٌ بِنِصفِ الحَقيقَةِ تُطالِبُ
/// بِاليَومِ فَلا يُعادُ؛ والرِسالَةُ تُعادُ بِسِياسَةِ Wolverine.</para>
/// <para><b><see cref="Entries"/> دالَّةٌ نَقِيَّة</b>: الوَحدَةُ مُشتَقَّةٌ مِن التَعريفاتِ لا ثابِتَةٌ في الكود — مِترُ الدَور (<c>presence.role</c> أَو <c>none</c>) لِكُلِّ دَورٍ
/// مُجَسَّد، ومِترُ حُضورِ المُزَوِّدِ (<c>store_binding</c> | <c>vendor_binding</c> | <c>platform</c> | <c>none</c>) لِكُلِّ رَبط؛ والمَتجَرُ المُعَلَّقُ لا سَطرَ لَه.</para>
/// <para><b>والأَبعاد</b>: المَتجَرُ سلاجُه؛ ورائِدُ الأَعمالِ مالِكُه (<see cref="MeterScope.OwnerOf"/>: المُعَرِّف أَو <c>_unowned</c>)؛ والدَورُ <c>_system</c> (عَمَلٌ مُجَدوَلٌ
/// بِلا طالِب) إلّا سَطرَ <c>presence.role</c> فَدَورُه الدَورُ المَعدودُ نَفسُه — بُعدُ «كُلِّ دَور» في قَرارِ المالِك؛ والمِيزَةُ مِن صَفِّ الوَحدَةِ ومِرساةُ
/// <c>@provider_capability</c> قُدرَةُ تَعريفِ المُزَوِّد؛ والمُزَوِّدُ سلاجُه أَو <c>_none</c>. <b>ومُعَرِّفُ البائِعِ لا يَدخُلُ الدَفتَر</b>: مَدخَلُ البائِعِ
/// <c>{capability}:{listing_owner}</c> يُعَدُّ بِمُزَوِّدِه لا بِمُعَرِّفِه (G17).</para>
/// <para><b>والمُعَرِّف</b> <c>{unit}:{app}:{day}:{subject}</c> (التَصميم §2) — اليَومُ <c>yyyy-MM-dd</c> بِمِنطَقَةِ المَنَصَّة، فَإعادَةُ اليَومِ نَفسِه لا تُضاعِف.</para>
/// </summary>
public static class PresenceReader
{
    /// <summary>أَنواعُ مَصدَرِ سُطورِ الحُضور — ما عُدَّ، ومُعَرِّفُه في <c>source_id</c>.</summary>
    public const string StoreSource = "store";
    public const string RoleSource = "role";
    public const string AuthoredRoleSource = "tenant_role_definition";
    public const string AuthoredThemeSource = "tenant_theme_definition";
    public const string MemberPlanSource = "tenant_plan_definition";
    public const string ProviderBindingSource = "provider_binding";
    public const string VendorBindingSource = "vendor_binding";
    public const string ApiKeySource = "api_key";
    public const string AuthChannelSource = "auth_channel";

    /// <summary>كُلُّ المَتاجِر، مُرَتَّبَةً بِسلاجِها — والمُعَلَّقُ يُقرَأُ بِوَثيقَتِه وَحدَها (لا سَطرَ لَه).</summary>
    public static async Task<IReadOnlyList<PresenceStoreFacts>> ReadAsync(IDocumentStore store, CancellationToken ct = default)
    {
        IReadOnlyList<Tenant> tenants;
        IReadOnlyList<ApiKeyDocument> keys;
        await using (var q = store.QuerySession())
        {
            tenants = await q.Query<Tenant>().ToListAsync(ct);
            keys = await q.Query<ApiKeyDocument>().Where(k => k.Status == ApiKeyDocument.StatusActive).ToListAsync(ct);
        }

        var facts = new List<PresenceStoreFacts>(tenants.Count);
        foreach (var t in tenants.OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            if (t.IsSuspended)
            {
                facts.Add(new(t.Id, t.OwnerUserId, true, t.AuthChannel, TenantRoleSet.Platform, [], 0, 0, 0, [], 0));
                continue;
            }
            await using var s = store.QuerySession(t.Id);
            var roleDocs = await s.Query<TenantRoleDefinition>().Where(d => d.Status == ApprovalFlow.Approved).ToListAsync(ct);
            var themes = await s.Query<TenantThemeDefinition>().Where(d => d.Status == ApprovalFlow.Approved).CountAsync(ct);
            var plans = await s.Query<TenantPlanDefinition>().Where(d => d.Status == ApprovalFlow.Approved).CountAsync(ct);
            var bindings = await s.Query<TenantProviderBinding>().Where(b => b.Status == TenantProviderBinding.StatusActive).ToListAsync(ct);

            var set = TenantRoleSet.FromDocuments(t.Id, roleDocs);
            var roles = set.Materialize(t.Roles).Select(r => new PresenceRoleFact(r.Slug, r.CatalogSlug)).ToList();
            facts.Add(new(t.Id, t.OwnerUserId, false, t.AuthChannel, set, roles, set.TenantAuthored.Count, themes, plans,
                [.. bindings.OrderBy(b => b.Id, StringComparer.Ordinal).Select(b => new PresenceBindingFact(b.Id, b.ProviderSlug))],
                keys.Count(k => string.Equals(k.TenantSlug, t.Id, StringComparison.Ordinal))));
        }
        return facts;
    }

    /// <summary>اليَومُ في المُعَرِّفِ ومَصدَرِه — صيغَةُ ISO لِلتاريخ (<c>"O"</c> لِـ<see cref="DateOnly"/> = <c>yyyy-MM-dd</c>) بِثَقافَةٍ ثابِتَة. <b>يَومٌ لا لَحظَة</b>: قُطِعَ بِمِنطَقَةِ
    /// المَنَصَّةِ قَبلَ أَن يَصِلَ هُنا (<see cref="PresenceSnapshot.DayOf"/>)، فَلا مِنطَقَةَ تُطَبَّقُ عَلَيه ثانِيَة.</summary>
    public static string DayKey(DateOnly day) => day.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>
    /// <para><b>سُطورُ يَومٍ واحِدٍ لِلمَتاجِرِ المَقروءَة</b> — بِتَرتيبِها (المَتجَرُ ثُمَّ الوَحَداتُ بِتَرتيبِ <see cref="PresenceUnits.All"/>). وَحدَةٌ لا تُكتَبُ إلّا وهِيَ
    /// في المُعجَمِ حُضوراً، وكَمِّيَّةٌ صِفرٌ لا سَطرَ لَها، ومِترٌ <c>none</c> لا سَطرَ لَه.</para>
    /// <para><b>ورَبطٌ بِلا تَعريفٍ في الكاتالوج</b> (سلاجٌ لا يَعرِفُه المُعطى) لا يُخمَّنُ لَه مِتر: يُعادُ في <paramref name="unresolved"/> بِاسمِه، ويُطبَعُ سَطراً.</para>
    /// </summary>
    public static IReadOnlyList<UsageEntry> Entries(DateOnly day, IEnumerable<PresenceStoreFacts> stores, PresenceDefinitions definitions,
        MeterUnitCatalog units, ICollection<string>? unresolved = null)
    {
        var dayKey = DayKey(day);
        var entries = new List<UsageEntry>();
        foreach (var s in stores)
        {
            if (s.IsSuspended) continue;
            var owner = MeterScope.OwnerOf(s.OwnerUserId);

            void Add(string unit, long quantity, string role, string? feature, string provider, string sourceType, string subject)
            {
                if (quantity <= 0 || units.Find(unit) is not { Boundary: MeterBoundaries.Presence } row) return;
                feature ??= row.Feature!;
                entries.Add(new UsageEntry(UsageEntryId.For(unit, s.Slug, $"{dayKey}:{subject}"), unit, quantity, s.Slug, owner, role,
                    feature, provider, sourceType, subject));
            }

            Add(PresenceUnits.Store, 1, MeterSentinels.SystemJob, null, MeterSentinels.None, StoreSource, s.Slug);

            foreach (var role in s.Roles)
            {
                var meter = definitions.RoleMeter(s.RoleSet, role.CatalogSlug);
                if (!string.Equals(meter, RoleMeterUnits.None, StringComparison.Ordinal))
                    Add(meter, 1, role.Slug, null, MeterSentinels.None, RoleSource, role.Slug);
            }

            Add(PresenceUnits.AuthoredRole, s.AuthoredRoles, MeterSentinels.SystemJob, null, MeterSentinels.None, AuthoredRoleSource, s.Slug);
            Add(PresenceUnits.AuthoredTheme, s.AuthoredThemes, MeterSentinels.SystemJob, null, MeterSentinels.None, AuthoredThemeSource, s.Slug);
            Add(PresenceUnits.MemberPlan, s.MemberPlans, MeterSentinels.SystemJob, null, MeterSentinels.None, MemberPlanSource, s.Slug);

            var vendors = new SortedDictionary<string, (long Count, string Capability)>(StringComparer.Ordinal);
            foreach (var b in s.Bindings)
            {
                var definition = definitions.Providers.FirstOrDefault(d => string.Equals(d.Slug, b.ProviderSlug, StringComparison.Ordinal));
                if (definition is null)
                {
                    unresolved?.Add($"{s.Slug}:{(b.IsVendorEntry ? VendorBindingSource : b.Id)}:{b.ProviderSlug}");
                    continue;
                }
                var presence = definition.Meter?.Presence;
                if (b.IsVendorEntry)
                {
                    if (presence == ProviderMeterPresences.VendorBinding)
                        vendors[definition.Slug] = (vendors.TryGetValue(definition.Slug, out var v) ? v.Count + 1 : 1, definition.Capability);
                }
                else if (presence == ProviderMeterPresences.StoreBinding)
                    Add(PresenceUnits.ProviderBinding, 1, MeterSentinels.SystemJob, definition.Capability, definition.Slug, ProviderBindingSource, b.Id);
            }
            foreach (var (provider, (count, capability)) in vendors)
                Add(PresenceUnits.VendorBinding, count, MeterSentinels.SystemJob, capability, provider, VendorBindingSource, provider);

            Add(PresenceUnits.ApiKey, s.ApiKeys, MeterSentinels.SystemJob, null, MeterSentinels.None, ApiKeySource, s.Slug);
            if (!string.IsNullOrWhiteSpace(s.AuthChannel))
                Add(PresenceUnits.AuthChannel, 1, MeterSentinels.SystemJob, null, MeterSentinels.None, AuthChannelSource, s.AuthChannel);
        }
        return entries;
    }
}
