using System.Text.RegularExpressions;
using ACommerce.Kit.Auth;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>حَذفُ الحِسابِ وصَفقَةٌ جارِيَة</b> (‏2026-10-29، قَرارُ المالِك د52 (١) «أ» — «لا يُقبَلُ طَلَبُ الحَذفِ ما دامَت لِلمُستَخدِمِ صَفقَةٌ جارِيَة،
/// فَيُكمِلُها أَو يُلغيها (أَو يَطلُبُ ذلكَ مِن المَتجَر) أَوَّلاً»). <b>تَوصيفُ «اليَوم» قَبلَ التَبديل</b> (القاعِدَة ٣): المَعجَمُ أَربَعَةُ رُموز،
/// و<c>Validate</c> لا يَسأَلُ عَن صَفقَة، وجِسمُ النُقطَةِ لا يَقرَأُ صَفقَةً قَبلَ <c>Erase</c> — فَحِسابٌ لَه صَفقَةٌ في مُنتَصَفِها يُحذَفُ كَأَيِّ حِساب،
/// ويَبقى الطَرَفُ الآخَرُ بِصَفقَةٍ طَرَفُها مَمحُوّ. يُقلَبُ الصَفُّ بِاسمِه في كوميتِ التَبديل.</para>
/// </summary>
public class AccountDeletionOpenDealTests(ITestOutputHelper output)
{
    [Fact]
    public void Today_an_account_with_an_open_deal_is_deleted_like_any_other()
    {
        var validate = typeof(AccountDeletion).GetMethod(nameof(AccountDeletion.Validate))!;
        var body = AccountDeletionRetentionTests.DeleteEndpointBody();
        var dealReads = Regex.Matches(body, @"\bDeal\b|Query<[^>]*Deal|OpenDeal").Count;
        var user = new User { Id = Guid.NewGuid(), TenantSlug = "zz-del", FullName = "طَرَفٌ في صَفقَة" };
        output.WriteLine($"رُموزُ المَعجَم: {AccountDeletion.All.Count} ({string.Join(", ", AccountDeletion.All)}) · وَسائِطُ Validate: {validate.GetParameters().Length} · قِراءَةُ صَفقَةٍ في جِسمِ الحَذف: {dealReads}");

        Assert.Equal(4, AccountDeletion.All.Count);
        Assert.DoesNotContain(AccountDeletion.All, c => c.Contains("deal", StringComparison.Ordinal));
        Assert.Equal(new[] { typeof(User), typeof(string), typeof(string) }, validate.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(0, dealReads);
        // لا مَعلومَةَ صَفقَةٍ تَبلُغُ البَوّابَة — فَطَرَفٌ في صَفقَةٍ جارِيَةٍ يَمضي.
        Assert.Null(AccountDeletion.Validate(user, "حذف", "حذف"));
    }
}
