using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using ACommerce.Kit.Cart;
using ACommerce.Kit.Favorites;
using ACommerce.Kit.Listings;
using ACommerce.Templates.Customer.Marketplace.Services.Api;
using Marten;
using Npgsql;

namespace ACommerce.Templates.Customer.Marketplace.Services.Identity;

/// <summary>
/// <para><b>حِساباتٌ مُكَرَّرَةٌ بِصيغَتَي الرَقَم — العَدُّ ثُمَّ الدَمج</b> (‏2026-10-29، قَرارُ المالِك د57 (٣) «أ»؛ ‏ADR-044 §٤). قَبلَ <c>c5325e4</c> كانَ
/// كُلُّ بابٍ يَكتُبُ الجَوّالَ كَما كُتِب، فَمَن دَخَلَ مَرَّةً بِـ<c>05…</c> ومَرَّةً بِـ<c>+966…</c> لَه حِسابان، ولِكُلٍّ إعلاناتُه وصَفَقاتُه. وهذا المِلَفُّ
/// ما يَحتاجُه سكربتا <c>scripts/count-duplicate-phones.cs</c> (قِراءَةٌ فَقَط) و<c>scripts/merge-duplicate-phones.cs</c> (عَرضٌ افتِراضِيّ، ولا يَكتُبُ
/// إلّا بِـ<c>--apply --confirm N</c>) — مُستَهلِكانِ في وَقتِ التَشغيلِ وصَفٌّ حَيٌّ عَلى Postgres.</para>
///
/// <para><b>الخُطَّة نَقِيَّة</b> (<see cref="Build"/>): يُجمَعُ مَن لَم يُحذَف بِصيغَتِه المُوَحَّدَة (<see cref="PhoneNumber.Canonical"/>)؛ المُفرَدُ الَّذي لَيسَ
/// مُوَحَّداً يُعادُ كَتبُ جَوّالِه، والمَجموعَةُ يَبقى أَقدَمُها (‏<c>CreatedAt</c>، ثُمَّ المُعَرِّف) ويُدمَجُ فيه الباقون. و<c>NID-…</c> (حِسابُ نَفاذٍ
/// هُوِيَّتُه الهُوِيَّةُ الوَطَنِيَّة) والفارِغُ خارِجَها.</para>
///
/// <para><b>والدَمجُ يَقيسُ قَبلَ أَن يَكتُب</b> (<see cref="ScanAsync"/>): كُلُّ جَدوَلِ وَثائِقَ مَحصورٍ بِمُستَأجِرٍ يُسأَلُ عَن مُعَرِّفِ المُندَمِجِ نَصّاً، فَما
/// كانَ وَثيقَةً عادِيَّةً (صَفقَة، مُحادَثَة، رِسالَة، إشعار، مُفَضَّلَة، بَحثٌ مَحفوظ، تَقييم، …) يُعادُ تَوجيهُه إلى الباقي في المُعامَلَةِ نَفسِها؛
/// والإعلانُ — مَجرى أَحداث — يُلحَقُ بِه <c>ListingEdited</c> بِمالِكِه الجَديد؛ والسَلَّةُ (مُعَرِّفُها مُعَرِّفُ صاحِبِها) تُنقَلُ إن لَم تَكُن لِلباقي سَلَّة؛
/// والمُفَضَّلَةُ (مُعَرِّفُها <c>Favorite.MakeId</c> بِمُعَرِّفِ صاحِبِها بِلا شَرطات) تُعادُ بِمُعَرِّفِ الباقي، ومُكَرَّرُها عِندَه يُسقَط.
/// <b>ويُمتَنَعُ عَن المَجموعَةِ كُلِّها</b> (يُطبَعُ سَبَبُه ولا يُكتَبُ فيها حَرف) إن وُجِدَ: صَفٌّ يَجمَعُ الحِسابَين (صَفقَةُ المَرءِ مَعَ نَفسِه)، أَو وَثيقَةُ
/// مَجرى أَحداثٍ غَيرِ الإعلان (عَرض، تَذكِرَة، اشتِراك — أَحداثُها لا تُعادُ كِتابَتُها)، أَو حَدَثٌ يَذكُرُه خارِجَ مَجاري الإعلانات، أَو مُعَرِّفُه بِصيغَةٍ
/// بِلا شَرطات. وسِجِلُّ التَدقيقِ تاريخٌ لا يُعادُ تَوجيهُه.</para>
///
/// <para><b>ومِفتاحُ الرَبطِ يُنقَلُ بِحَقلِه لا بِالمَسح</b> (‏2026-10-29، مُراجَعَةُ <c>9f00eac</c>؛ تَوصيفُه <c>c11fd8a</c>): <see cref="ApiKeyDocument"/>
/// وَثيقَةٌ <b>عامَّة</b> (‏<c>SingleTenanted</c> — تُحَمَّلُ قَبلَ أَن يُعرَفَ المُستَأجِر)، فَلا عَمودَ <c>tenant_id</c> في جَدوَلِها ولا يَراها
/// <see cref="TenantTablesAsync"/>. وكانَ مِفتاحٌ صَدَرَ بِاسمِ المُندَمِجِ يَبقى «فَعّالاً» في شاشَةِ المَفاتيحِ ويُرَدُّ <c>ActorGone</c> بَعدَ مَحوِه (د56) —
/// مِفتاحٌ مَيِّتٌ يَبدو حَيّاً. فَالآن: <see cref="ScanAsync"/> يَعُدُّها بِحَقلَيها (<c>TenantSlug</c> و<c>ActorUserId</c>)، و<see cref="ApplyAsync"/>
/// يَكتُبُ <c>ActorUserId</c> الباقي في المُعامَلَةِ نَفسِها — كُلَّ مِفتاحٍ بِاسمِه أَيّاً كانَت حالُه، كَما يُعادُ كُلُّ صَفٍّ يَذكُرُه — ويَبقى <c>ActorName</c>
/// كَما كَتَبَه صاحِبُ المَتجَر. فَالرَبطُ الَّذي يُحَرِّكُ صَفَقاتِ الشَخصِ يَبقى يُحَرِّكُها، وهِيَ صارَت صَفَقاتِ الباقي.
/// والوَثائِقُ العامَّةُ الأُخرى لا تَحمِلُ حِسابَ عُضو (مُثَبَّتٌ بِأَسمائِها في <c>DuplicatePhonesTests</c>).</para>
///
/// <para><b>والمُندَمِجُ يُمحى كَما يُمحى الحِسابُ المَحذوف</b> (<see cref="AccountDeletion.Erase"/> بِعَينِها — ومِنها رَفعُ إصدارِ الجَلسَة، فَتَسقُطُ رُموزُه)،
/// والباقي يَأخُذُ جَوّالَه مُوَحَّداً ويَرِثُ ما لَيسَ لَه: التَحَقُّقَ مِن الجَوّال، والبَريدَ والهُوِيَّةَ الوَطَنِيَّةَ إن كانَ بِلاهُما.
/// ‏(‏2026-10-29، A1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>وما سِواها</b>: خَصائِصُ الأَدوارِ والحِساب، والدَورُ، والاسم، والشُروط، والإشعار — <see cref="Inherit"/>؛
/// وحِسابٌ إداريٌّ في المَجموعَةِ يَمنَعُها (<see cref="AdminBlock"/>).</para>
///
/// <para><b>ولا يُرَكِّبُ مَخزَناً</b>: يَأخُذُ <see cref="IDocumentStore"/> مِن مُستَدعيه، وكُلُّ سكربتٍ يُرَكِّبُ مَخزَنَه في مِلَفِّه (كَـ<c>count-listing-owners.cs</c>
/// و<c>repair-role-floor.cs</c>) — فَمَواضِعُ إعدادِ Marten في <c>libs/</c> تَبقى الثَلاثَةَ الَّتي يَراها <c>scripts/marten-schema.cs</c>
/// (‏<c>MartenSchemaVerbTests.EveryMartenConfigurationSite_IsInsideTheTwoCallsTheVerbMakes</c>). ومَخزَنُ الدَمجِ يُعيدُ مِن تَركيبِ المُضيفِ ما يَمَسُّه:
/// الإيجارُ المُقتَرِنُ لِلأَحداث، ونُسخَةُ <c>Listing</c> المُحَدَّثَةُ في المُعامَلَة (‏<c>HostingExtensions</c>)، و<see cref="ApiKeyDocument"/> عامَّةً
/// (‏<c>MarketplaceTemplateExtensions</c>) — مُثَبَّتٌ في <c>DuplicatePhonesTests</c>.</para>
/// </summary>
public static class DuplicatePhones
{
    public sealed record Account(Guid Id, string Phone, DateTime CreatedAt, bool Deleted);
    public sealed record Rename(Guid Id, string To);
    public sealed record Group(string Canonical, Guid Survivor, IReadOnlyList<Guid> Losers);
    public sealed record Plan(int Considered, IReadOnlyList<Rename> Renames, IReadOnlyList<Group> Groups);

    /// <summary>جَوّالٌ يَدخُلُ الخُطَّة: غَيرُ فارِغٍ ولا <c>NID-…</c>.</summary>
    public static bool Eligible(string? phone)
        => !string.IsNullOrWhiteSpace(phone) && !phone.Trim().StartsWith("NID-", StringComparison.Ordinal);

    /// <summary>الخُطَّةُ النَقِيَّة — راجِع رَأسَ المِلَفّ.</summary>
    public static Plan Build(IEnumerable<Account> accounts)
    {
        var live = accounts.Where(a => !a.Deleted && Eligible(a.Phone)).ToList();
        var renames = new List<Rename>();
        var groups = new List<Group>();
        foreach (var g in live.GroupBy(a => PhoneNumber.Canonical(a.Phone), StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var ordered = g.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id.ToString("N"), StringComparer.Ordinal).ToList();
            if (ordered.Count == 1)
            {
                if (!string.Equals(ordered[0].Phone, g.Key, StringComparison.Ordinal)) renames.Add(new(ordered[0].Id, g.Key));
            }
            else groups.Add(new(g.Key, ordered[0].Id, ordered.Skip(1).Select(a => a.Id).ToList()));
        }
        return new(live.Count, renames, groups);
    }

    // ─── ما يَرِثُه الباقي — نَقِيّ ─────────────────────────────────────────────────────────

    /// <summary>
    /// <para><b>الباقي يَرِثُ ما لَيسَ لَه، وما لَه يَبقى لَه</b> (‏2026-10-29، تَدقيقُ الموجَة ١١ A1؛ تَوصيفُه <c>9a7735b</c>). كانَ الدَمجُ يُوَرِّثُ
    /// التَحَقُّقَ والبَريدَ والهُوِيَّةَ وَحدَها ثُمَّ يَمحو المُندَمِجَ (<see cref="AccountDeletion.Erase"/>) — فَيَضيعُ دَورُه واسمُ نَشاطِه واسمُه وقَبولُه
    /// لِلشُروطِ واشتِراكُ إشعارِه، والصَفَقاتُ والإعلاناتُ الَّتي نُقِلَت إلى الباقي تُعرَضُ بِلا اسمِ النَشاطِ الَّذي عُرِفَت بِه.</para>
    /// <para><b>والقاعِدَة</b> (P1 — لا يُكتَبُ فَوقَ ما يَملِكُه الباقي): خَصائِصُ كُلِّ دَورٍ وخَصائِصُ الحِسابِ العامَّةُ تُضَمُّ مِفتاحاً مِفتاحاً والباقي يَغلِبُ
    /// عِندَ التَعارُض؛ والدَورُ النَشِطُ والصورَةُ والمَنطِقَةُ لِلباقي إن كانَت لَه وإلّا لِلمُندَمِج (<b>إلّا الدَورَ الإداريّ</b> — لا يُوَرَّثُ صَلاحِيَّةً، والقِياسُ
    /// يَمنَعُ مَجموعَتَه أَصلاً)؛ والاسمُ لِلمُندَمِجِ إن كانَ الباقي بِلا اسم (<see cref="Queries.ActorName.NeedsName"/>)؛ والشُروطُ أَحدَثُها إصداراً
    /// (ثُمَّ تاريخاً) بِتاريخِه؛ واشتِراكاتُ الإشعارِ تُضَمُّ بِـ<see cref="PushSubscriptionDoor.Keep"/> (الجِهازُ نَفسُه مَرَّة، وأَحدَثُ الحَدّ)؛ والبَريدُ
    /// المُضافُ بِبابِ الرَبطِ يَبقى «مُضافاً» حينَ يُوَرَّث.</para>
    /// </summary>
    public static void Inherit(User survivor, User loser)
    {
        survivor.PhoneVerified |= loser.PhoneVerified;
        if (string.IsNullOrEmpty(survivor.Email) && !string.IsNullOrEmpty(loser.Email))
        {
            (survivor.Email, survivor.EmailVerified) = (loser.Email, loser.EmailVerified);
            if (survivor.AddedContactKind.Length == 0 && loser.AddedContactKind == ContactLink.Email) survivor.AddedContactKind = ContactLink.Email;
        }
        if (string.IsNullOrEmpty(survivor.NationalId) && !string.IsNullOrEmpty(loser.NationalId))
            survivor.NationalId = loser.NationalId;

        foreach (var (role, attrs) in loser.RoleAttributesJson)
        {
            if (!survivor.RoleAttributesJson.TryGetValue(role, out var mine)) survivor.RoleAttributesJson[role] = mine = new();
            foreach (var (k, v) in attrs) mine.TryAdd(k, v);
        }
        foreach (var (k, v) in loser.AttributesJson) survivor.AttributesJson.TryAdd(k, v);
        if (string.IsNullOrEmpty(survivor.ActiveRole) && !Gates.SelfGrantPolicy.IsAdminSlug(loser.ActiveRole))
            survivor.ActiveRole = loser.ActiveRole;
        if (Queries.ActorName.NeedsName(survivor) && !Queries.ActorName.NeedsName(loser))
            survivor.FullName = loser.FullName;
        if (string.IsNullOrEmpty(survivor.AvatarUrl) && !string.IsNullOrEmpty(loser.AvatarUrl))
            survivor.AvatarUrl = loser.AvatarUrl;
        if (!survivor.HasAnchor && loser.HasAnchor)
            (survivor.AnchorLat, survivor.AnchorLng, survivor.RadiusKm) = (loser.AnchorLat, loser.AnchorLng, loser.RadiusKm);
        if (loser.AcceptedTermsVersion > survivor.AcceptedTermsVersion
            || (loser.AcceptedTermsVersion == survivor.AcceptedTermsVersion && loser.AcceptedTermsAt > (survivor.AcceptedTermsAt ?? DateTime.MinValue)))
            (survivor.AcceptedTermsAt, survivor.AcceptedTermsVersion) = (loser.AcceptedTermsAt, loser.AcceptedTermsVersion);
        foreach (var sub in loser.PushSubscriptions.OrderBy(p => p.CreatedAt))
            PushSubscriptionDoor.Keep(survivor.PushSubscriptions,
                new PushSubscription { Endpoint = sub.Endpoint, P256dh = sub.P256dh, Auth = sub.Auth, CreatedAt = sub.CreatedAt });
    }

    /// <summary>ما يَختَلِفُ بَينَ حِساباتِ مَجموعَةٍ ويُراجِعُه صاحِبُ القَرارِ قَبلَ الدَمج — أَعلامٌ لا قيَم (سكربتُ العَدِّ لا يَطبَعُ بَياناً شَخصِيّاً).</summary>
    public sealed record Diff(bool Role, bool RealName, bool BusinessName);

    /// <summary>
    /// <para>(‏2026-10-29، تَدقيقُ الموجَة ١١ G-4) <b>الدَور</b>: مُندَمِجٌ دَورُه النَشِطُ غَيرُ دَورِ الباقي، أَو لَه دَورٌ بِخَصائِصَ لَيسَ لِلباقي؛
    /// <b>الاسمُ الحَقيقيّ</b>: مُندَمِجٌ لَه اسمٌ والباقي بِلا اسمٍ أَو بِاسمٍ آخَر؛ <b>اسمُ النَشاط</b> (<see cref="Queries.ActorName.BusinessName"/>):
    /// مُندَمِجٌ لَه اسمُ نَشاطٍ غَيرُ اسمِ نَشاطِ الباقي. نَقِيَّة.</para>
    /// </summary>
    public static Diff Differences(User survivor, IEnumerable<User> losers)
    {
        bool role = false, name = false, business = false;
        var survivorName = Queries.ActorName.NeedsName(survivor) ? null : survivor.FullName.Trim();
        var survivorBusiness = Queries.ActorName.BusinessName(survivor);
        foreach (var l in losers)
        {
            role |= (l.ActiveRole.Length > 0 && l.ActiveRole != survivor.ActiveRole)
                    || l.RoleAttributesJson.Keys.Any(k => !survivor.RoleAttributesJson.ContainsKey(k));
            name |= !Queries.ActorName.NeedsName(l) && l.FullName.Trim() != survivorName;
            business |= Queries.ActorName.BusinessName(l) is { } b && b != survivorBusiness;
        }
        return new(role, name, business);
    }

    // ─── القِياس ──────────────────────────────────────────────────────────────────────────

    /// <summary>جَدوَلُ وَثائِقَ مَحصورٌ بِمُستَأجِر. <c>Aggregate</c> = نُسخَةُ مَجرى أَحداثٍ (‏<c>mt_version</c> عَدَدِيّ، لا <c>uuid</c>).</summary>
    public sealed record Table(string Name, bool Aggregate);

    /// <summary>ما يُعادُ تَوجيهُه لِمُندَمِجٍ في جَدوَل.</summary>
    public sealed record Repoint(Guid Loser, string Table, long Rows);

    /// <summary>قِياسُ مَجموعَةٍ قَبلَ دَمجِها. تُدمَجُ إن خَلَت <see cref="Blocks"/>. و<see cref="Keys"/> مَفاتيحُ الرَبطِ بِاسمِ المُندَمِجينَ في المَتجَر.</summary>
    public sealed record Scan(Group Group, IReadOnlyList<Repoint> Repoints, IReadOnlyList<Guid> Listings, int LoserCarts, bool SurvivorCart,
        int Favorites, int Keys, IReadOnlyList<string> Blocks)
    {
        public bool Mergeable => Blocks.Count == 0;
    }

    /// <summary>جَدوَلُ <see cref="ApiKeyDocument"/> كَما يُسَمّيه Marten — عامٌّ بِلا <c>tenant_id</c>، فَلا يَبلُغُه <see cref="TenantTablesAsync"/>.
    /// مُطابَقَتُه لِتَسمِيَةِ Marten مُثَبَّتَةٌ في <c>DuplicatePhonesTests</c>.</summary>
    public const string KeysTable = "mt_doc_apikeydocument";

    /// <summary>جَداوِلُ لا يُعادُ تَوجيهُها نَصّاً: المُستَخدِمُ والسَلَّةُ والمُفَضَّلَةُ (مُعَرِّفاتُها مِن مُعَرِّفِ صاحِبِها — تُعالَجُ بِـMarten)، والإعلانُ
    /// (بِحَدَث)، والتَدقيقُ (تاريخ).</summary>
    public static readonly IReadOnlyList<string> NotRepointed = ["mt_doc_user", "mt_doc_cart", "mt_doc_favorite", "mt_doc_listing", "mt_doc_auditentry"];

    /// <summary>بادِئَةُ مانِعِ «حِسابٌ إداريٌّ في المَجموعَة» — يَعُدُّها سكربتُ العَدِّ كَما يَعُدُّ أَسماءَ الجَداوِل.</summary>
    public const string AdminBlock = "tenant_admin";

    private static readonly Regex TableName = new("^mt_doc_[a-z0-9_]+$", RegexOptions.CultureInvariant);

    /// <summary>كُلُّ جَدوَلِ وَثائِقَ في <c>platform</c> لَه عَمودُ <c>tenant_id</c> — مِن <c>information_schema</c> لا مِن قائِمَةٍ مَكتوبَة.</summary>
    public static async Task<IReadOnlyList<Table>> TenantTablesAsync(NpgsqlConnection conn, CancellationToken ct = default)
    {
        const string sql = """
            select c.table_name, coalesce(v.data_type, '')
            from information_schema.columns c
            left join information_schema.columns v
              on v.table_schema = c.table_schema and v.table_name = c.table_name and v.column_name = 'mt_version'
            where c.table_schema = 'platform' and c.column_name = 'tenant_id' and c.table_name like 'mt_doc_%'
            order by 1
            """;
        var list = new List<Table>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var name = r.GetString(0);
            if (!TableName.IsMatch(name)) throw new InvalidOperationException($"اسمُ جَدوَلٍ خارِجَ الشَكل: {name}");
            list.Add(new(name, r.GetString(1) != "uuid"));
        }
        return list;
    }

    /// <summary>يَقيسُ مَجموعَةً — قِراءَةٌ خالِصَة.</summary>
    public static async Task<Scan> ScanAsync(NpgsqlConnection conn, string tenant, Group group, IReadOnlyList<Table> tables, CancellationToken ct = default)
    {
        var survivor = group.Survivor.ToString();
        var repoints = new List<Repoint>();
        var listings = new List<Guid>();
        var blocks = new List<string>();
        var loserCarts = 0;
        var favorites = 0;
        var keys = 0;
        // ‏قاعِدَةٌ لَم يُصدَر فيها مِفتاحٌ قَطّ قَد لا يَكونُ فيها جَدوَلُه — والقِراءَةُ لا تُنشِئُ مُخَطَّطاً، فَغِيابُه صِفرُ مَفاتيح.
        var keysTable = await CountAsync(conn, $"select count(*) from pg_catalog.pg_tables where schemaname = 'platform' and tablename = '{KeysTable}'", ct) > 0;
        foreach (var loser in group.Losers)
        {
            var l = loser.ToString();
            if (keysTable)
                keys += (int)await CountAsync(conn, $"select count(*) from platform.{KeysTable} where data->>'TenantSlug' = @t and data->>'ActorUserId' = @u", ct,
                    ("t", tenant), ("u", l));
            foreach (var t in tables)
            {
                if (t.Name is "mt_doc_user" or "mt_doc_auditentry") continue;
                if (t.Name == "mt_doc_favorite")
                {
                    favorites += (int)await CountAsync(conn, "select count(*) from platform.mt_doc_favorite where tenant_id = @t and data->>'UserId' = @u", ct,
                        ("t", tenant), ("u", l));
                    continue;
                }
                var nForm = await CountAsync(conn, $"select count(*) from platform.{t.Name} where tenant_id = @t and data::text like @p", ct,
                    ("t", tenant), ("p", $"%{loser:N}%"));
                if (nForm > 0) blocks.Add($"{t.Name}: {nForm} صَفّاً بِالمُعَرِّفِ بِلا شَرطات");
                if (t.Name == "mt_doc_cart")
                {
                    loserCarts += (int)await CountAsync(conn, "select count(*) from platform.mt_doc_cart where tenant_id = @t and id = @i", ct, ("t", tenant), ("i", loser));
                    continue;
                }
                var hits = await CountAsync(conn, $"select count(*) from platform.{t.Name} where tenant_id = @t and data::text like @p", ct,
                    ("t", tenant), ("p", $"%{l}%"));
                if (hits == 0) continue;
                if (t.Name == "mt_doc_listing")
                {
                    var owned = await IdsAsync(conn, "select id from platform.mt_doc_listing where tenant_id = @t and data->'Attributes'->>'owner_id' = @o", ct,
                        ("t", tenant), ("o", l));
                    listings.AddRange(owned);
                    if (hits > owned.Count) blocks.Add($"mt_doc_listing: {hits - owned.Count} إعلاناً يَذكُرُه في غَيرِ مالِكِه");
                    continue;
                }
                var both = await CountAsync(conn, $"select count(*) from platform.{t.Name} where tenant_id = @t and data::text like @p and data::text like @q", ct,
                    ("t", tenant), ("p", $"%{l}%"), ("q", $"%{survivor}%"));
                if (both > 0) blocks.Add($"{t.Name}: {both} صَفّاً يَجمَعُ الحِسابَين");
                if (t.Aggregate) blocks.Add($"{t.Name}: {hits} صَفّاً مِن مَجرى أَحداثٍ غَيرِ الإعلان");
                else repoints.Add(new(loser, t.Name, hits));
            }
            var events = await CountAsync(conn, """
                select count(*) from platform.mt_events e
                where e.tenant_id = @t and e.data::text like @p
                  and not exists (select 1 from platform.mt_doc_listing d where d.id = e.stream_id and d.tenant_id = e.tenant_id)
                """, ct, ("t", tenant), ("p", $"%{l}%"));
            if (events > 0) blocks.Add($"mt_events: {events} حَدَثاً خارِجَ مَجاري الإعلانات");
        }
        // ‏(‏2026-10-29، تَدقيقُ الموجَة ١١ A1) حِسابٌ إداريٌّ في المَجموعَةِ يَمنَعُها: الدَمجُ لا يُنقِلُ صَلاحِيَّةً ولا يَمحو مَن يَملِكُها — يُحسَمُ بِبابَي
        // grant-admin/revoke-admin المُدَقَّقَينِ أَوَّلاً. الإداريُّ دَورُه النَشِطُ tenant_admin، أَو دَورٌ في المَتجَرِ رَمزُ كَتالوجِه tenant_admin.
        var tenantTable = await CountAsync(conn, "select count(*) from pg_catalog.pg_tables where schemaname = 'platform' and tablename = 'mt_doc_tenant'", ct) > 0;
        var members = group.Losers.Prepend(group.Survivor).ToArray();
        var admins = await CountAsync(conn, tenantTable
            ? """
              select count(*) from platform.mt_doc_user u
              where u.tenant_id = @t and u.id = any(@ids)
                and (u.data->>'ActiveRole' = @admin
                     or exists (select 1 from platform.mt_doc_tenant d, jsonb_array_elements(coalesce(d.data->'Roles', '[]'::jsonb)) r
                                where d.id = @t and r->>'Slug' = u.data->>'ActiveRole' and r->>'CatalogSlug' = @admin))
              """
            : "select count(*) from platform.mt_doc_user u where u.tenant_id = @t and u.id = any(@ids) and u.data->>'ActiveRole' = @admin",
            ct, ("t", tenant), ("ids", members), ("admin", Gates.SelfGrantPolicy.AdminSlug));
        if (admins > 0) blocks.Add($"{AdminBlock}: {admins} حِساباً إدارِيّاً في المَجموعَة");
        var survivorCart = await CountAsync(conn, "select count(*) from platform.mt_doc_cart where tenant_id = @t and id = @i", ct,
            ("t", tenant), ("i", group.Survivor)) > 0;
        return new(group, repoints, listings, loserCarts, survivorCart, favorites, keys, blocks);
    }

    // ─── الكِتابَة ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <para>يَدمِجُ مَجموعَةً قيسَت ولا مانِعَ فيها — <b>في مُعامَلَةٍ واحِدَة</b> (‏<c>SaveChangesAsync</c> واحِد): إعادَةُ التَوجيهِ النَصِّيَّةُ
    /// (‏<c>QueueSqlCommand</c>، ومَعَها <c>mt_version</c> جَديدٌ فَيُرَدُّ كُلُّ حِفظٍ قَرَأَ قَبلَها بِالتَزامُنِ التَفاؤُلِيِّ القائِم)، و<c>ListingEdited</c> لِكُلِّ
    /// إعلانٍ يَملِكُه مُندَمِج، ونَقلُ السَلَّة، ومَفاتيحُ الرَبطِ بِاسمِه، والمُستَخدِمون. يُعيدُ عَدَدَ ما أُعيدَ تَوجيهُه.</para>
    /// <para><b>ومَخزَنُه يُسَجِّلُ <see cref="ApiKeyDocument"/> عامَّةً كَما يُسَجِّلُها القالِب</b>: بِغَيرِه يَسأَلُها Marten بِـ<c>tenant_id</c> لا عَمودَ لَه
    /// فَيَرتَدُّ الاستِعلامُ قَبلَ الحِفظ ولا يُكتَبُ حَرف. ولا يُسأَلُ جَدوَلُها إلّا إن عَدَّ القِياسُ فيه مِفتاحاً.</para>
    /// </summary>
    /// <remarks>‏(‏2026-10-29، تَدقيقُ الموجَة ١١ G-1) <b>ويُعادُ مَرَّةً مِن قِراءَةٍ جَديدَة</b> إن حَفِظَ كاتِبٌ آخَرُ أَحَدَ الحِساباتِ بَعدَ قِراءَتِها هُنا
    /// (<c>User</c> بِتَزامُنٍ تَفاؤُلِيٍّ، فَيَرمي الحِفظُ <c>ConcurrencyException</c> والمُعامَلَةُ كُلُّها تُرَدّ) — نَسَقُ <c>AccountWrites.UpdateAsync</c>:
    /// مُحاوَلَتانِ، والثانِيَةُ إن رُدَّت تَرمي ولا يُكتَبُ حَرف.</remarks>
    public static async Task<long> ApplyAsync(IDocumentStore store, string tenant, Scan scan, DateTime now, CancellationToken ct = default)
    {
        if (!scan.Mergeable) throw new InvalidOperationException("merge_blocked: " + string.Join(" · ", scan.Blocks));
        for (var attempt = 1; ; attempt++)
        {
            try { return await ApplyOnceAsync(store, tenant, scan, now, ct); }
            catch (JasperFx.ConcurrencyException) when (attempt < ACommerce.Kit.Auth.Server.AccountWrites.Attempts) { }
        }
    }

    private static async Task<long> ApplyOnceAsync(IDocumentStore store, string tenant, Scan scan, DateTime now, CancellationToken ct)
    {
        var g = scan.Group;
        await using var s = store.LightweightSession(tenant);
        var survivor = await s.LoadAsync<User>(g.Survivor, ct) ?? throw new InvalidOperationException("survivor_gone");
        var losers = new List<User>();
        foreach (var id in g.Losers)
            losers.Add(await s.LoadAsync<User>(id, ct) ?? throw new InvalidOperationException("loser_gone"));

        long moved = 0;
        foreach (var rp in scan.Repoints)
        {
            s.QueueSqlCommand(
                $"update platform.{rp.Table} set data = replace(data::text, ?, ?)::jsonb, mt_last_modified = transaction_timestamp(), " +
                "mt_version = md5(random()::text || clock_timestamp()::text)::uuid where tenant_id = ? and data::text like ?",
                rp.Loser.ToString(), g.Survivor.ToString(), tenant, $"%{rp.Loser}%");
            moved += rp.Rows;
        }

        foreach (var id in scan.Listings)
        {
            var listing = await s.Events.AggregateStreamAsync<Listing>(id, token: ct) ?? throw new InvalidOperationException("listing_gone");
            var attrs = new Dictionary<string, string>(listing.Attributes) { [Listings.ListingEditService.OwnerAttribute] = g.Survivor.ToString() };
            s.Events.Append(id, new ListingEdited(id, null, null, null, null, null, null, attrs, now));
            moved++;
        }

        var survivorHasCart = scan.SurvivorCart;
        var staged = new HashSet<string>(StringComparer.Ordinal);
        foreach (var loser in losers)
        {
            foreach (var fav in await s.Query<Favorite>().Where(f => f.UserId == loser.Id).ToListAsync(ct))
            {
                var id = Favorite.MakeId(g.Survivor, fav.ListingId);
                if (staged.Add(id) && await s.LoadAsync<Favorite>(id, ct) is null)
                {
                    s.Store(new Favorite { Id = id, UserId = g.Survivor, ListingId = fav.ListingId, At = fav.At });
                    moved++;
                }
                s.Delete<Favorite>(fav.Id);
            }
            if (await s.LoadAsync<Cart>(loser.Id, ct) is { } cart)
            {
                if (!survivorHasCart)
                {
                    s.Store(new Cart { Id = g.Survivor, Items = cart.Items, UpdatedAt = cart.UpdatedAt, Checkout = cart.Checkout });
                    survivorHasCart = true;
                    moved++;
                }
                s.Delete<Cart>(loser.Id);
            }
            if (scan.Keys > 0)
            {
                // ‏الوَثيقَةُ عامَّة: تُسأَلُ بِحَقلِ مُستَأجِرِها لا بِجَلسَتِه. والاسمُ في خَطِّ الصَفقَةِ (<c>ActorName</c>) لا يُمَسّ.
                foreach (var key in await s.Query<ApiKeyDocument>().Where(k => k.TenantSlug == tenant && k.ActorUserId == loser.Id).ToListAsync(ct))
                {
                    key.ActorUserId = g.Survivor;
                    s.Store(key);
                    moved++;
                }
            }

            Inherit(survivor, loser);
            s.Store(AccountDeletion.Erase(loser, now));
        }
        survivor.Phone = g.Canonical;
        survivor.UpdatedAt = now;
        s.Store(survivor);
        await s.SaveChangesAsync(ct);
        return moved;
    }

    /// <summary>يُعيدُ كَتبَ جَوّالِ المُفرَدينَ مُوَحَّداً — وَثيقَةٌ وَثيقَة، في مُعامَلَةٍ واحِدَةٍ لِلمُستَأجِر.</summary>
    public static async Task<int> RenameAsync<T>(IDocumentStore store, string tenant, IReadOnlyList<Rename> renames, Action<T, string> setPhone,
        CancellationToken ct = default) where T : class
    {
        if (renames.Count == 0) return 0;
        await using var s = store.LightweightSession(tenant);
        var done = 0;
        foreach (var r in renames)
        {
            if (await s.LoadAsync<T>(r.Id, ct) is not { } doc) continue;
            setPhone(doc, r.To);
            s.Store(doc);
            done++;
        }
        await s.SaveChangesAsync(ct);
        return done;
    }

    private static async Task<long> CountAsync(NpgsqlConnection conn, string sql, CancellationToken ct, params (string, object)[] ps)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct) ?? 0L, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<List<Guid>> IdsAsync(NpgsqlConnection conn, string sql, CancellationToken ct, params (string, object)[] ps)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        var ids = new List<Guid>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) ids.Add(r.GetGuid(0));
        return ids;
    }
}
