using System.Security.Cryptography;
using System.Text;

namespace ACommerce.Templates.Customer.Marketplace.Services.Deals;

/// <summary>
/// <para><b>المُطالَبَةُ بِالسَلَّة — مُعَرِّفُ صَفقَةِ كُلِّ بَندٍ مِن نُسخَةِ السَلَّةِ الَّتي قُرِئَت</b> (‏2026-10-24، تَدقيقُ الإطلاق lens-money-integrity-1؛
/// ‏ADR-036 §١٤). كانَ <c>POST /{slug}/checkout/submit</c> يَقرَأُ السَلَّةَ ويَلِدُ كُلَّ صَفقَةٍ بِمُعَرِّفٍ جَديدٍ ولا يَحذِفُها إلّا آخِراً، فَإرسالانِ
/// مُتَداخِلانِ يَقرَآنِ السَلَّةَ نَفسَها ويَلِدانِ مَجموعَتَين (تَوصيفُه <c>94a7ef0</c>).</para>
///
/// <para><b>والكِتابَةُ الشَرطِيَّةُ هي وِلادَةُ الصَفقَةِ نَفسُها — لا وَثيقَةٌ جَديدَةٌ ولا جَدوَل</b> (القاعِدَتانِ ١ و٨): وَثيقَةُ <see cref="Deal"/> بِتَزامُنٍ
/// تَفاؤُلِيّ (‏ADR-036)، و<c>Store</c> صَفقَةٍ جَديدَةٍ بِمُعَرِّفٍ قائِمٍ يُرَدُّ بِـ<c>JasperFx.ConcurrencyException</c> (مَقيسٌ عَلى Postgres المَحَلِّيِّ
/// ‏2026-10-24: «store-of-new over existing: JasperFx.ConcurrencyException» والمَخزونُ لَم يَتَغَيَّر). فَمُعَرِّفٌ تُحَدِّدُه نُسخَةُ السَلَّةِ
/// (<c>UpdatedAt</c> — يُكتَبُ عِندَ كُلِّ تَعديل) ومَوضِعُ البَندِ ومُعَرِّفُ الإعلان: يَلِدُه الإرسالُ الأَوَّلُ، ويُرَدُّ الثاني عِندَ بَندِه الأَوَّلِ قَبلَ أَيِّ حَجزٍ
/// أَو فاتورَةٍ أَو إشعار. ومِفتاحُ الحَجزِ <c>deal_{id}</c> يَصيرُ ثابِتاً لِلنُسخَةِ نَفسِها — فَالمُزَوِّدُ يَحرُسُ مِن جِهَتِه أَيضاً.</para>
///
/// <para><b>دالَّةٌ نَقِيَّة</b> — بِلا قاعِدَةٍ ولا وَقتٍ ولا عَشوائِيَّة: المَدخَلاتُ نَفسُها تُعطي المُعَرِّفَ نَفسَه، وأَيُّ اختِلافٍ يُعطي غَيرَه.</para>
/// </summary>
public static class CheckoutClaim
{
    /// <summary>مُعَرِّفُ صَفقَةِ البَندِ <paramref name="line"/> مِن سَلَّةِ <paramref name="buyerId"/> في <paramref name="tenantSlug"/> بِنُسخَتِها
    /// <paramref name="cartUpdatedAt"/> — أَوَّلُ ‏16 بايتاً مِن SHA-256، بِرايَةِ الإصدارِ 8 (مُعَرِّفٌ مُشتَقٌّ، RFC 9562) والمُتَغَيِّر.</summary>
    public static Guid DealIdFor(string tenantSlug, Guid buyerId, DateTime cartUpdatedAt, int line, Guid listingId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"checkout|{tenantSlug}|{buyerId:N}|{cartUpdatedAt.Ticks}|{line}|{listingId:N}"));
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}
