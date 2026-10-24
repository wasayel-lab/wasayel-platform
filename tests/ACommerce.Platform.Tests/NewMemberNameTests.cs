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

    /// <summary><b>قُلِبَ «Today_» بِاسمِه (‏2026-10-24، S15 D)</b>: لا حَرفِيَّةَ في العُدَّة — المَوضِعانِ يَقرَآنِ القامُوسَ بِالعَرَبِيَّةِ صَراحَةً.</summary>
    [Fact]
    public void The_new_member_name_is_read_from_the_arabic_dictionary_in_both_places()
    {
        var code = Read(AuthHandlersPath);
        Assert.DoesNotContain("\"عُضو جَديد\"", code, StringComparison.Ordinal);
        Assert.Equal(2, code.Split("? NewMemberName()").Length - 1);
        Assert.Contains("LocaleCatalog.Find(ACommerce.Platform.I18n.LocaleCatalog.Arabic, NewMemberNameKey)", code, StringComparison.Ordinal);
        Assert.Equal("me.profile.new_member", ACommerce.Kit.Auth.Server.AuthHandlers.NewMemberNameKey);
    }

    /// <summary>والمُخَزَّنُ بَعدَ التَرحيلِ بايتاتُ ما قَبلَه (موجِب)، ولا يَتبَعُ لُغَةً أُخرى (سالِب: الإنجليزِيَّةُ لا تُسأَل).</summary>
    [Fact]
    public void The_stored_name_after_the_migration_is_the_same_bytes_and_arabic_only()
    {
        var name = (string)typeof(ACommerce.Kit.Auth.Server.AuthHandlers)
            .GetMethod("NewMemberName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, null)!;
        Assert.Equal(StoredNameUtf8Hex, Hex(name));
        Assert.DoesNotContain("LocaleCatalog.Text(", Read(AuthHandlersPath), StringComparison.Ordinal);
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
