namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>
/// <para><b>مُنتَجاتُ البائِعِ الواحِدِ مِن سَلَّةٍ واحِدَةٍ تُشحَنُ بِطَلَبٍ واحِدٍ وبوليصَةٍ واحِدَةٍ عِندَ OTO</b> (‏2026-10-29، قَرارُ المالِك د13 «ب»؛ تَوصيفُه
/// <c>ee46c53</c>؛ ‏ADR-033 §٣‑٩ و§٧‑ش البَند ٧). كانَت <c>/checkout/submit</c> تَلِدُ صَفقَةً لِكُلِّ بَندٍ ولا شَيءَ يَربِطُ صَفَقاتِ البائِعِ الواحِد، و<c>ShipAsync</c>
/// يُنشِئُ لِكُلِّ صَفقَةٍ طَلَبَها: ثَلاثَةُ مُنتَجاتٍ ثَلاثُ بوليصاتٍ وثَلاثُ أُجرَةِ شَحنٍ وثَلاثُ دَفَعاتٍ لِلمَندوب.</para>
///
/// <para><b>والتَجميعُ عِندَ الشَحنِ لا في نَموذَجِ الصَفقَة</b> («أ» S4 · ١٠): الصَفقَةُ بَندٌ واحِدٌ (<see cref="Deal.ListingId"/>) وعَلَيها تَقومُ أَبوابُ
/// القَبولِ والإلغاءِ والتَقييمِ والفاتورَة — فَإعادَةُ كِتابَتِها صَفقَةً بِبُنودٍ مَوجَةٌ لا بَند. فَتُكتَبُ عِندَ الوِلادَةِ لِكُلِّ صَفقَةٍ مِن مَجموعَةِ بائِعٍ (اثنَتانِ
/// فَأَكثَر) أَخَواتُها في <see cref="Ref"/> — نَسَقُ <see cref="GatewayPayments.InvoiceDealsRef"/> بِحَرفِه — ويَجمَعُها <c>DealShipping.ShipAsync</c> في طَلَبٍ واحِد.
/// <b>وصَفقَةٌ وَحدَها لِبائِعِها لا يُكتَبُ لَها شَيء</b>: سَلَّةٌ بِبَندٍ واحِدٍ لِكُلِّ بائِعٍ تَبقى بايتاً كَما كانَت.</para>
///
/// <para><b>والرابِطُ قائِمَةُ مُعَرِّفاتٍ لا مُعَرِّفُ سَلَّةٍ يُستَعلَمُ بِه</b> («أ» S4 · ١٠): الشَحنُ يُحَمِّلُ الأَخَواتِ بِمُعَرِّفاتِها كَما يُحَمِّلُ الصَفقَة، بِلا
/// استِعلامٍ ولا فِهرِس. <b>ونَمَطُ السَلَّةِ</b> (‏S3 د2) لا يُغَيِّرُ شَيئاً: في «لِكُلِّ بائِعٍ سَلَّة» مَجموعَةٌ واحِدَة، وفي «المُختَلِطَة» مَجموعَةٌ لِكُلِّ بائِع.</para>
/// </summary>
public static class DealShipGroup
{
    /// <summary>أَخَواتُ الصَفقَةِ مِن السَلَّةِ نَفسِها عِندَ بائِعِها — المُعَرِّفاتُ بِصيغَةِ <c>D</c> مَفصولَةً بِفاصِلَةٍ بِتَرتيبِ وِلادَتِها، والصَفقَةُ مِنها.
    /// لا يُسَمّى في <see cref="DealRefLabels"/>: داخِلِيٌّ لا يُرسَمُ لِلطَرَفَين، ويَراه صاحِبُ المَتجَرِ في قائِمَةِ المَراجِع.</summary>
    public const string Ref = "oto_ship_group";

    /// <summary>
    /// <para><b>مَجموعاتُ السَلَّةِ — دالَّةٌ نَقِيَّة</b>: صَفَقاتُ ما وُلِدَ مِنها بِمالِكِ إعلانِها بِتَرتيبِ الوِلادَة ⇒ لِكُلِّ صَفقَةٍ في مَجموعَةِ بائِعٍ مِن اثنَتَينِ
    /// فَأَكثَر قيمَةُ <see cref="Ref"/>. والبائِعُ بِمُعَرِّفِه كَما كُتِبَ (<c>owner_id</c>) بِمُقارَنَةٍ حَرفِيَّة؛ وفارِغُه لا يُجمَع.</para>
    /// </summary>
    public static IReadOnlyDictionary<Guid, string> ForCheckout(IReadOnlyList<(Guid DealId, string Vendor)> born)
    {
        var groups = new Dictionary<Guid, string>();
        foreach (var vendor in born.Where(b => !string.IsNullOrWhiteSpace(b.Vendor)).GroupBy(b => b.Vendor, StringComparer.Ordinal))
        {
            var ids = vendor.Select(b => b.DealId).Distinct().ToArray();
            if (ids.Length < 2) continue;
            var value = ValueFor(ids);
            foreach (var id in ids) groups[id] = value;
        }
        return groups;
    }

    /// <summary>يَكتُبُ <see cref="Ref"/> لِكُلِّ صَفقَةٍ مِن مَجموعَةِ بائِعٍ بِأُنبوبِ الإلحاقِ القائِم — بَعدَ أَن نَجَحَ طَلَبُ السَلَّة (<c>/checkout/submit</c>)، ولا كِتابَةَ
    /// لِسَلَّةٍ لا مَجموعَةَ فيها.</summary>
    public static async Task WriteAsync(DealsService deals, string tenantSlug, IReadOnlyList<(Guid DealId, string Vendor)> born, CancellationToken ct = default)
    {
        foreach (var (dealId, group) in ForCheckout(born))
            await deals.AttachRefAsync(tenantSlug, dealId, Ref, group, ct);
    }

    /// <summary>القيمَةُ المَكتوبَة — المُعَرِّفاتُ بِصيغَةِ <c>D</c> مَفصولَةً بِفاصِلَة (نَسَقُ <see cref="GatewayInvoiceShare.ValueFor"/>).</summary>
    public static string ValueFor(IReadOnlyList<Guid> dealIds) => string.Join(',', dealIds.Select(id => id.ToString("D")));

    /// <summary>
    /// <para><b>المَجموعَةُ كَما كُتِبَت، والصَفقَةُ مِنها</b> — أَو الصَفقَةُ وَحدَها حينَ لا مَرجِع، أَو لا يُقرَأ، أَو يَتَكَرَّرُ مُعَرِّف، أَو لا يُسَمّيها:
    /// ما لا يُقرَأُ يُشحَنُ وَحدَه كَما كانَ قَبلَ القَرار، لا يُجمَعُ بِتَخمين.</para>
    /// </summary>
    public static IReadOnlyList<Guid> MembersOf(Deal deal) => Parse(deal.Refs.GetValueOrDefault(Ref), deal.Id);

    /// <summary>كَم مُنتَجاً آخَرَ مَعَ الصَفقَةِ في مَجموعَتِها — تَقرَؤُه صَفحَةُ الصَفقَةِ فَوقَ زِرِّ الشَحن.</summary>
    public static int OthersOf(Deal deal) => MembersOf(deal).Count - 1;

    internal static IReadOnlyList<Guid> Parse(string? raw, Guid self)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new[] { self };
        var ids = new List<Guid>();
        foreach (var part in raw.Split(','))
        {
            if (!Guid.TryParse(part, out var id)) return new[] { self };
            ids.Add(id);
        }
        return ids.Distinct().Count() == ids.Count && ids.Contains(self) ? ids : new[] { self };
    }
}
