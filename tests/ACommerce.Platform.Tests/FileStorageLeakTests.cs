using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ صُوَرُ المُستَأجِرينَ تُكتَبُ على قُرصٍ زائِل — سِتَّةُ اختِبارات ═══
//
// **كُلُّ اختِبارٍ هُنا كُتِبَ أَحمَرَ قَبلَ حَرفٍ واحِدٍ مِن العِلاج**
// (القاعِدَة ٣)، واسمُه يَقول **الأَثَرَ على المُستَخدِم** لا اسمَ
// الدالَّة.
//
// ─── العَطَبُ، كَما قيسَ يَومَ ‏2026-08-30 ───────────────────────────
//
// **١) `AddLocalFileStorage(…)` سَطرٌ عارٍ في `Program.cs`** — بِلا شَرطِ
// بيئَة، على `wwwroot/uploads` **داخِلَ الحاوِيَة**. وقُرصُ الـSpace
// **زائِل**: كُلُّ ما رُفِعَ يَختَفي عِندَ أَوَّلِ إعادَةِ نَشر.
// والعَطَبُ **مُسَجَّلٌ سَلَفاً** في `docs/DEPLOY.md` §٥ ولَم يُطلَب
// عِلاجُه — فَهُوَ دَينٌ مُعلَنٌ لا اكتِشاف
// (`docs/PROVIDER-STUB-DEBT.md` §١).
//
// **٢) ومُستَهلِكاه حَيّانِ كِلاهُما**: ‏`POST /{slug}/listings/create`
// (صُوَرُ الإعلانات) و`POST /{slug}/me/save` (الصورَةُ الشَخصِيَّة).
//
// **٣) وأَسوَأُ مِن الفَقدِ الصامِت**: الرابِطُ يَبقى في القاعِدَةِ
// ويُصَيَّر في الصَفحَة، فَتُرسَم **صورَةٌ مَكسورَة** لا فَراغٌ يُفهَم.
// **وذلك مَقيسٌ حَيّاً لا مُستَنتَج**: ‏
// `GET https://acommerceecommerce-acommerce-ecommerce.hf.space/uploads/<أَيُّ مَسار>`
// يَرُدُّ **404** (‏2026-08-30) — والجِذرُ يَرُدُّ ‏200 في النِداءِ
// نَفسِه، فَالتَطبيقُ حَيٌّ والمِلَفُّ وَحدَه ذاهِب.
//
// ─── والقِياسُ الَّذي يُحَدِّد شَكلَ العِلاج ─────────────────────────
//
// مُسِحَت قاعِدَةُ الإنتاج: **‏35 جَدوَلَ وَثائِق، وصِفرُ رابِطِ
// `/uploads/` في كُلِّها** (‏41 إعلاناً بِصِفرِ صورَة، و22 مُستَخدِماً
// بِصِفرِ صورَةٍ شَخصِيَّة). أَي **لا هِجرَةَ تُنفَّذ ولا صَفَّ يُصلَح**
// — والعِلاجُ **وِقائيٌّ بِالكامِل**: يُغلَق البابُ قَبلَ أَوَّلِ صورَةٍ
// حَقيقِيَّة، لا بَعدَها.
//
// ─── حارِسُ العَمى (القاعِدَة ١٠) ────────────────────────────────────
// كُلُّ فاحِصٍ يَطبَع عَدَدَ ما فَحَص ويَحمَرُّ عِندَ الصِفر.
public class FileStorageLeakTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Source(params string[] parts)
    {
        var path = Path.Combine(RepoRoot, Path.Combine(parts));
        Assert.True(File.Exists(path), $"مَصدَرٌ مَفقود: {path} — الأَداةُ عَمياءُ بِلا طَرَفٍ مَقروء.");
        var text = File.ReadAllText(path);
        Assert.True(text.Length > 200, $"أَداة عَمياء: {path} طولُه {text.Length} مِحرَفاً — لَم يُقرَأ.");
        return text;
    }

    // ═══ ١) القُرصُ الزائِلُ هُوَ تَخزينُ الإنتاج ═══════════════════════

    /// <summary>سَطرُ تَسجيلٍ عارٍ عِندَ العَمودِ صِفر = خارِجَ أَيّ
    /// `switch` أَو `if` = القُرصُ الزائِلُ هُوَ جَوابُ الإنتاجِ كَما
    /// هُوَ جَوابُ التَطوير.</summary>
    [Fact]
    public void Production_must_not_store_tenant_images_on_the_ephemeral_container_disk()
    {
        var program = Source("apps", "V1.App", "Program.cs")
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.False(program.Contains("\nbuilder.Services.AddLocalFileStorage(", StringComparison.Ordinal),
            "‏`AddLocalFileStorage(…)` سَطرٌ عارٍ في `Program.cs` — فَصُوَرُ المُستَأجِرينَ "
            + "تُكتَب على قُرصِ الحاوِيَةِ الزائِل، وتَختَفي عِندَ أَوَّلِ إعادَةِ نَشر. "
            + "نَفسُ عَطَبِ `AddMockPayments()` حَرفاً، ونَفسُ العِلاج: قَرارٌ بِالتَهيئَة "
            + "وحارِسُ إقلاعٍ يَرمي.");
    }

    /// <summary>حارِسُ الإقلاعِ لِلتَخزين — كَجارَيه في الدَفعِ وقَنَواتِ
    /// الدُخول، ويَرمي **قَبلَ أَوَّلِ طَلَب** لا بَعدَ أَوَّلِ صورَةٍ
    /// ضائِعَة.</summary>
    [Fact]
    public void Boot_must_refuse_to_start_when_ephemeral_disk_storage_is_registered_outside_development()
    {
        var program = Source("apps", "V1.App", "Program.cs");

        Assert.Contains("FileStorageSelection.AssertNoStubsOutsideDevelopment",
            program, StringComparison.Ordinal);
    }

    /// <summary>العَلامَةُ جُزءٌ مِن نَوعِ الصِنف — تَسقُط بِحَذفٍ مَرئيٍّ
    /// في مُراجَعَةٍ لا بِتَحريرِ سِلسِلَة. نَفسُ حُجَّةِ
    /// <c>IDevelopmentStubPaymentProvider</c> حَرفاً.</summary>
    [Fact]
    public void The_ephemeral_disk_storage_must_declare_itself_development_only_in_its_type()
    {
        var local = typeof(ACommerce.Kit.Files.LocalFileStorage);
        var marks = local.GetInterfaces()
            .Where(i => i.Name == "IDevelopmentStubFileStorage")
            .ToArray();

        Assert.True(local.GetInterfaces().Length > 0,
            "أَداة عَمياء: صِفرُ واجِهَةٍ على `LocalFileStorage`.");
        Assert.True(marks.Length == 1,
            "‏`LocalFileStorage` لا يَحمِل `IDevelopmentStubFileStorage` — فَحارِسُ الإقلاعِ "
            + "لا يَملِك ما يُمسِكُه بِه، وفَحصُ الاسمِ نَصٌّ يُبَدَّل بِإعادَةِ تَسمِيَةٍ صامِتَة.");
    }

    // ═══ ٢) والبَديلُ الدائِمُ لا بُدَّ أَن يُشحَن فِعلاً ═══════════════

    /// <summary><b>المَشروعُ غَيرُ المُحالِ إلَيه لا يَبلُغ الـSpace
    /// أَصلاً</b> — ‏`deploy-manifest.sh` يَمشي `ProjectReference`
    /// تَعَدِّيّاً، وسَبعَةُ مَشاريعِ مُزَوِّدينَ تُبنى ولا تَبلُغُه هُوَ
    /// الدَرسُ المَكتوبُ في `V1.App.csproj` نَفسِه.</summary>
    [Fact]
    public void A_durable_object_store_must_exist_and_be_shipped_in_the_binary()
    {
        var csproj = Source("apps", "V1.App", "V1.App.csproj");

        Assert.Contains("ACommerce.Kit.Files.Providers.S3", csproj, StringComparison.Ordinal);

        var s3 = Path.Combine(RepoRoot, "libs", "kits", "Files",
            "ACommerce.Kit.Files.Providers.S3", "S3FileStorage.cs");
        Assert.True(File.Exists(s3),
            "لا مُزَوِّدَ تَخزينٍ دائِمٍ في المُستودَع — و`LocalFileStorage` وَحدَه "
            + "يَعني أَنّ إغلاقَ البابِ يُغلِق الرَفعَ كُلَّه.");
    }

    /// <summary><b>لا تَوقيعَ يُكتَب بِيَد</b> — وهذا لَيسَ ذَوقاً:
    /// الجارانِ في المُجَلَّدِ نَفسِه يَفعَلانِها، ‏`AliyunOssFileStorage`
    /// بِـ<c>HMACSHA1</c> يَدَوِيَّة و`GoogleCloudFileStorage` بِتَوقيعِ
    /// ‏JWT بِـ<c>RSA.SignData</c> + مِلَفِّ JSON على القُرص. وكِلاهُما
    /// ‏351 سَطراً تُبنى وتُشحَن ولا تُنادى.</summary>
    [Fact]
    public void The_durable_store_must_not_hand_roll_its_own_request_signature()
    {
        var s3 = Source("libs", "kits", "Files", "ACommerce.Kit.Files.Providers.S3", "S3FileStorage.cs");
        var csproj = Source("libs", "kits", "Files", "ACommerce.Kit.Files.Providers.S3",
            "ACommerce.Kit.Files.Providers.S3.csproj");

        Assert.Contains("AWSSDK.S3", csproj, StringComparison.Ordinal);

        // **التَعليقُ يُنزَع قَبلَ الفَحص**: المِلَفُّ يَشرَح لِماذا لا
        // يَفعَل ما يَفعَلُه الجاران، فَيَذكُر `HMACSHA1` و`RSA.SignData`
        // بِاسمِهِما. وأَداةٌ تَقرَأُ الشَرحَ كَأَنَّه فِعلٌ **أَداةٌ
        // تَكذِب** — وقَد كَذَبَت فِعلاً في أَوَّلِ تَشغيلٍ لَها
        // (القاعِدَة ١٠: الأَداةُ تُقاس قَبلَ أَن يُوثَقَ بِها).
        var code = string.Join('\n', s3
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Where(l =>
            {
                var t = l.TrimStart();
                return !t.StartsWith("//", StringComparison.Ordinal)
                    && !t.StartsWith("*", StringComparison.Ordinal);
            }));
        Assert.True(code.Length > 1000,
            $"أَداة عَمياء: بَقِيَ {code.Length} مِحرَفاً بَعدَ نَزعِ التَعليق.");

        var handRolled = new[] { "HMACSHA1", "HMACSHA256", "RSA.Create", "SignData", "AWS4-HMAC" }
            .Where(n => code.Contains(n, StringComparison.Ordinal))
            .ToArray();
        Assert.True(handRolled.Length == 0,
            "تَوقيعٌ مَكتوبٌ بِاليَد في مُزَوِّدِ التَخزين: " + string.Join("، ", handRolled)
            + " — والحُزمَةُ تَفعَلُها، وهذا تَكامُلٌ لا بِناء.");
    }

    // ═══ ٣) ولا رابِطَ يُكتَبُ حينَ لا مَخزَنَ دائِماً ══════════════════

    /// <summary><b>الفَشَلُ المُغلَقُ هُوَ السُقوطُ الآمِن</b>: حينَ لا
    /// مَخزَنَ مَضبوطاً، الرَفضُ عِندَ **الكِتابَة** يَمنَع الرابِطَ
    /// المُعَلَّقَ مِن الوُجود — فَلا صورَةَ مَكسورَةٌ تُرسَم لاحِقاً.
    /// وذلك أَقوى مِن أَيِّ بَديلٍ يُعرَض عِندَ القِراءَة، لِأَنَّه
    /// يَقَع قَبلَ أَن تُكتَبَ الكَذِبَةُ في القاعِدَة.</summary>
    [Fact]
    public void No_dangling_link_may_be_written_when_no_durable_store_is_configured()
    {
        var path = Path.Combine(RepoRoot, "libs", "kits", "Files",
            "ACommerce.Kit.Files.Core", "UnavailableFileStorage.cs");
        Assert.True(File.Exists(path),
            "لا `UnavailableFileStorage` — فَالوِعاءُ الفارِغُ يَعني انفِجارَ حَلٍّ في "
            + "نُقطَتَينِ حَيَّتَين، لا رَفضاً مَقروءاً.");

        var src = File.ReadAllText(path);
        Assert.True(src.Length > 200, $"أَداة عَمياء: طولُه {src.Length} مِحرَفاً.");
        Assert.Contains("throw new FileStorageException", src, StringComparison.Ordinal);
    }
}
