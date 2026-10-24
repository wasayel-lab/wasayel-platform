using ACommerce.Platform.I18n;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ «عُضو جَديد» — اسمُ كُلِّ حِسابٍ جَديدٍ بِالهاتِفِ أَو البَريد، حَرفِيٌّ في العُدَّة (القاعِدَة ١١) ═══
//
// **المَقيس** (RELEASE-READINESS §١ البَند ٥ و§٧·١، «ب» S12·٤(١)): `AuthHandlers.GetOrCreateUserAsync` يَكتُبُ `FullName = "عُضو جَديد"`
// حَرفِيّاً في مَوضِعَين (`AuthHandlers.cs`)، وفي `ar.json` المِفتاحُ `me.profile.new_member` بِالبايتاتِ نَفسِها (يَقرَؤُه `Me.razor` سُقوطاً
// لِاسمٍ غائِب). **والقيمَةُ مُخَزَّنَةٌ في وَثيقَةِ المُستَخدِم** — فَالتَرحيلُ يَجِبُ أَن يَكتُبَ البايتاتِ نَفسَها: مُثَبَّتَةٌ هُنا بِتِسعَةَ عَشَرَ
// بايتاً سِتّ عَشرِيّاً لا بِنَصٍّ يُنسَخ. والاسمُ يَبلُغُ المُشتَري اسماً لِلبائِع — مَقيسٌ ‏2026-10-24 عَلى `wasayel_s15r1` (`rewalk-241501`):
// إشعارُ «… قَبِلَ طَلَبَكَ» (1)، وصَفحَةُ الصَفقَة (اسمُ الطَرَفِ وسُطورُ السِجِلّ)، وصَفحَةُ البائِع («إعلانات عُضو جَديد») — «ب» في سِجِلِّ المالِك.
public class NewMemberNameTests(ITestOutputHelper output)
{
    private const string AuthHandlersPath = "libs/kits/Auth/ACommerce.Kit.Auth.Server/AuthHandlers.cs";

    /// <summary>البايتاتُ المُخَزَّنَةُ اليَومَ لِاسمِ الحِسابِ الجَديد (‏UTF-8) — مَقروءَةً مِن قاعِدَةٍ مَحَلِّيَّة (`mt_doc_user.FullName`).</summary>
    private const string StoredNameUtf8Hex = "d8b9d98fd8b6d98820d8acd98ed8afd98ad8af";

    private static string Read(string relative)
    {
        var path = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {relative}");
        return File.ReadAllText(path);
    }

    private static string Hex(string s) => Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(s)).ToLowerInvariant();

    /// <summary><b>تَوصيفُ «اليَوم» — يُقلَبُ بِاسمِه في كوميتِ التَرحيل</b>: الاسمُ حَرفِيٌّ في مَوضِعَينِ مِن العُدَّة.</summary>
    [Fact]
    public void Today_the_new_member_name_is_a_literal_twice_in_the_auth_handlers()
    {
        var code = Read(AuthHandlersPath);
        var literal = "\"عُضو جَديد\"";
        Assert.Equal(2, code.Split(literal).Length - 1);
        Assert.DoesNotContain("LocaleCatalog", code, StringComparison.Ordinal);
    }

    /// <summary><b>ويَبقى بِحَرفِه</b>: مِفتاحُ القامُوسِ يَحمِلُ البايتاتِ المُخَزَّنَةَ نَفسَها — فَالتَرحيلُ لا يُغَيِّرُ ما يُكتَبُ في الوَثيقَة.</summary>
    [Fact]
    public void The_dictionary_key_carries_the_stored_bytes()
    {
        var value = LocaleCatalog.Find("ar", "me.profile.new_member");
        Assert.NotNull(value);
        output.WriteLine($"me.profile.new_member = {value} ({Hex(value!)})");
        Assert.Equal(StoredNameUtf8Hex, Hex(value!));
    }
}
