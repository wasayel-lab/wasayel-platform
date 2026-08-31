using System.Text.RegularExpressions;
using Xunit;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>الدَورُ الإداريُّ لا يُمنَحُ ذاتِيّاً — والبابانِ يُسَدّانِ
/// مَعاً.</b> نُقطَةُ <c>POST /{slug}/me/save</c> كانَت تَكتُب
/// <c>user.ActiveRole</c> مِن الاستِمارَةِ <b>بِلا حارِسٍ إطلاقاً</b>،
/// بَينَما أُختُها <c>POST /{slug}/me/role/save</c> تَمنَعُ التَرَقِّيَ
/// صَراحَةً بِرَمزِ خَرقٍ ثابِت. <b>فَالتَناقُضُ بَينَ الأُختَينِ هُوَ
/// الثَغرَة، والسُلوكُ المَقصودُ مَكتوبٌ في إحداهُما</b> — لا
/// يُختَرَع.</para>
///
/// <para><b>الكِلفَةُ المَقيسَة (‏2026-08-31)</b>: عُضوٌ عادِيٌّ رَفَعَ
/// نَفسَه إلى مُديرِ مَتجَرٍ بِطَلَبٍ واحِد، ثُمَّ <b>قَرَأَ صَفحَةَ
/// الأَعضاءِ وفيها رَقمُ هاتِف</b>، <b>وكَتَبَ في هُوِيَّةِ المَتجَرِ
/// بِنَجاح</b>. ويُبلَغُ مِن المُتَصَفِّح: صَفحَةُ <c>/me/edit</c>
/// تَعرِضُ خِيارَ <c>tenant_admin</c> <b>لِكُلِّ عُضو</b>.</para>
///
/// <para><b>ولِماذا الطَرَفانِ لا طَرَف</b>: حارِسٌ بِلا إخفاءٍ يَترُكُ
/// الخِيارَ مُغرِياً على الشاشَة، وإخفاءٌ بِلا حارِسٍ لا يَمنَعُ
/// <c>curl</c>. فَالفَحصُ هُنا فَحصانِ: واحِدٌ لِلنُقطَةِ وواحِدٌ
/// لِلشاشَة.</para>
///
/// <para><b>وثالِثُهُما جَردٌ لا حالَة</b> (القاعِدَة ٢): بابانِ وُجِدا
/// في يَومٍ واحِد، فَالسُؤالُ «أَثَمَّةَ ثالِث؟» يُحَوَّلُ إلى فاحِصٍ
/// يَحمَرُّ عِندَ الرابِع بَدَلَ أَن يُترَكَ لِلمُراجِع. كُلُّ مَوضِعٍ
/// يَكتُبُ <c>ActiveRole</c> في المُستودَع: إمّا يُعلِنُ
/// <c>SelfGrantPolicy</c> في كُتلَتِه، وإمّا يُثَبَّتُ بِاسمِه
/// وسَبَبِه.</para>
///
/// <para><b>وما لا يَدَّعيه</b>: مَسحُ نَصٍّ لا يُثبِتُ أَنَّ الحارِسَ
/// <b>يَرُدّ</b>، ولا أَنَّه الحارِسُ الصَحيح — نَفسُ حُدودِ
/// <c>WriteEndpointGuardTests</c> المُعلَنَة. وهُوَ يُثبِتُ الشَيءَ
/// الَّذي سَقَطَ فِعلاً: <b>الغِيابَ التامّ</b>.</para>
/// </summary>
public class AdminSelfGrantTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    /// <summary>الرَمزُ المُشتَرَك — اسمُ السِياسَةِ النَقِيَّةِ الَّتي
    /// يُنادِيها الطَرَفان. رَمزُ حارِسٍ لا يَعرِفُه الفاحِصُ حارِسٌ لا
    /// يُرى، فَالمَعرِفَةُ هُنا مَقصودَة.</summary>
    private const string PolicyToken = "SelfGrantPolicy";

    /// <summary>إسنادٌ إلى <c>ActiveRole</c> — لا مُقارَنَة (<c>==</c>)
    /// ولا تَصريحُ خاصِّيَّة (<c>{ get; set; }</c>).</summary>
    private static readonly Regex ActiveRoleWrite =
        new(@"ActiveRole\s*=(?!=)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>بِدايَةُ الكُتلَةِ الَّتي يَعيشُ فيها الإسناد: نُقطَةُ
    /// HTTP أَو تَصريحُ دالَّة. النافِذَةُ مِنها إلى الإسنادِ هي ما
    /// يُفَتَّشُ فيه عَن الحارِس — فَحارِسٌ في نُقطَةٍ أُخرى لا
    /// يُحسَب.</summary>
    private static readonly Regex BlockStart =
        new(@"^\s*(app\.Map\w+\(|(private|public|internal|protected)\s|static\s)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private sealed record Pinned(string File, int Line, string WhyAr);

    /// <summary>
    /// <para><b>مَواضِعُ كِتابَةِ الدَورِ المُثَبَّتَة</b> — وكُلٌّ
    /// بِسَبَبِه. ونُموُّ هذِه القائِمَةِ <b>قَرارٌ مَرئيٌّ في
    /// مُراجَعَة</b> لا نَتيجَةُ نِسيان.</para>
    /// </summary>
    private static readonly Pinned[] PinnedWrites =
    {
        new("libs/templates/ACommerce.Templates.Customer.Marketplace/MarketplaceTemplateExtensions.cs", 2018,
            "مَنحُ الإداريِّ مِن `/admin/tenants/{slug}/users` — مَحروسٌ بِـ`TenantAdminGuard.CanAdministerAsync` ومُسَجَّلٌ في التَدقيق. هذِه هي القَناةُ المُعتَمَدَةُ لِلمَنح، فَحارِسُ المَنعِ الذاتيِّ عَلَيها خَطَأ."),
        new("libs/templates/ACommerce.Templates.Customer.Marketplace/MarketplaceTemplateExtensions.cs", 2046,
            "سَحبُ الإداريِّ مِن نَفسِ الصَفحَة — مَحروسٌ بِنَفسِ الحارِسِ ومُسَجَّل. والسَحبُ يَنزِلُ بِالصَلاحِيَّةِ ولا يَرفَعُها."),
        new("apps/V1.App/Seed/AppearanceBaselineSeeder.cs", 506,
            "بَذرَةُ لَقطاتِ المَظهَر — تَعمَلُ في التَطويرِ وَحدَه ولا يَبلُغُها طَلَبُ HTTP، والدَورُ فيها ثابِتٌ في الكودِ لا مِن استِمارَة."),
        new("apps/V1.App/Seed/TestDataSeeder.cs", 80,
            "بَذرَةُ بَياناتِ التَجرِبَة — نَفسُ الحُجَّة: لا مَدخَلَ HTTP، والدَورُ مُشتَقٌّ مِن سلاجِ المُستَأجِرِ لا مِن مُستَخدِم."),
        new("apps/V1.App/Seed/TestDataSeeder.cs", 90,
            "نَفسُ البَذرَة، فَرعُ التَحديث — نَفسُ الحُجَّة حَرفاً."),
    };

    private sealed record Write(string File, int Line, string Window);

    private static IEnumerable<string> SourceFiles()
    {
        foreach (var root in new[] { "libs", "apps" })
        {
            var dir = Path.Combine(RepoRoot, root);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(f);
                if (ext is not (".cs" or ".razor")) continue;
                var rel = Rel(f);
                if (rel.Contains("/obj/", StringComparison.Ordinal) ||
                    rel.Contains("/bin/", StringComparison.Ordinal)) continue;
                yield return f;
            }
        }
    }

    private static string Rel(string path) =>
        Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');

    private static List<Write> ActiveRoleWrites()
    {
        var hits = new List<Write>();
        foreach (var file in SourceFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!ActiveRoleWrite.IsMatch(lines[i])) continue;
                var j = i;
                while (j > 0 && !BlockStart.IsMatch(lines[j])) j--;
                hits.Add(new(Rel(file), i + 1, string.Join('\n', lines[j..(i + 1)])));
            }
        }
        return hits;
    }

    // ─── الفَحصُ الأَوَّل: النُقطَة ────────────────────────────────────

    /// <summary>
    /// <para><b>يَحمَرُّ لَو استَطاعَ عُضوٌ رَفعَ نَفسِه عَبرَ
    /// <c>/me/save</c>.</b> النُقطَةُ تَقرَأُ <c>activeRole</c> مِن
    /// الاستِمارَةِ وتَكتُبُه، فَإن لَم تُنادِ السِياسَةَ قَبلَ
    /// الكِتابَةِ فَلا شَيءَ يَقِفُ بَينَ عُضوٍ عادِيٍّ
    /// و<c>tenant_admin</c>.</para>
    /// </summary>
    [Fact]
    public void Profile_save_refuses_the_admin_role_like_its_sister()
    {
        var writes = ActiveRoleWrites()
            .Where(w => w.File.EndsWith("MarketplaceTemplateExtensions.cs", StringComparison.Ordinal))
            .ToList();

        // عَدّاد: أَداةٌ تَفحَصُ صِفراً أَداةٌ عَمياء (القاعِدَة ١٠).
        Assert.True(writes.Count >= 6,
            $"أَداةٌ عَمياء: وُجِدَ {writes.Count} مَوضِعَ كِتابَةٍ لِلدَورِ في مِلَفِّ النِقاط — والمَقيسُ ٦ فَأَكثَر.");

        var formFed = writes
            .Where(w => w.Window.Contains("req.Form[\"activeRole\"]", StringComparison.Ordinal))
            .ToList();

        Assert.True(formFed.Count >= 1,
            "أَداةٌ عَمياء: لَم يُعثَر عَلى مَوضِعٍ يَكتُبُ الدَورَ مِن حَقلِ `activeRole` في الاستِمارَة.");

        var naked = formFed
            .Where(w => !w.Window.Contains(PolicyToken, StringComparison.Ordinal))
            .Select(w => $"{w.File}:{w.Line}")
            .ToArray();

        Assert.True(naked.Length == 0,
            "نُقطَةٌ تَكتُبُ الدَورَ مِن الاستِمارَةِ بِلا حارِسِ مَنعٍ ذاتيّ:\n  " +
            string.Join("\n  ", naked) +
            $"\nالسُلوكُ المَقصودُ مَكتوبٌ في الأُخت POST /{{slug}}/me/role/save — يُنادى بِـ{PolicyToken} لا يُعادُ كِتابَتُه.");
    }

    // ─── الفَحصُ الثاني: الشاشَة ───────────────────────────────────────

    /// <summary>
    /// <para><b>يَحمَرُّ لَو عُرِضَ الخِيارُ في الشاشَةِ لِمَن لا
    /// يَملِكُه.</b> كُلُّ شاشَةٍ تُصَيِّرُ حَقلَ اختِيارِ دَورٍ
    /// (<c>activeRole</c> أَو <c>role</c>) تُصَفّي الأَدوارَ
    /// بِالسِياسَةِ نَفسِها الَّتي تَحرُسُ النُقطَة — <b>وإلّا بَقِيَ
    /// البابُ مُغرِياً وإن كانَ مُقفَلاً</b>.</para>
    /// </summary>
    [Fact]
    public void Every_screen_that_offers_a_role_filters_the_admin_role()
    {
        var offering = SourceFiles()
            .Where(f => Path.GetExtension(f) == ".razor")
            .Select(f => (File: Rel(f), Text: File.ReadAllText(f)))
            .Where(x => x.Text.Contains("name=\"activeRole\"", StringComparison.Ordinal)
                     || x.Text.Contains("name=\"role\"", StringComparison.Ordinal))
            .ToList();

        // عَدّاد (القاعِدَة ١٠).
        Assert.True(offering.Count >= 2,
            $"أَداةٌ عَمياء: وُجِدَت {offering.Count} شاشَةً تَعرِضُ حَقلَ دَور — والمَقيسُ اثنَتانِ فَأَكثَر.");

        var naked = offering
            .Where(x => !x.Text.Contains(PolicyToken, StringComparison.Ordinal))
            .Select(x => x.File)
            .ToArray();

        Assert.True(naked.Length == 0,
            "شاشَةٌ تَعرِضُ خِيارَ دَورٍ بِلا تَصفِيَةِ الدَورِ الإداريّ:\n  " +
            string.Join("\n  ", naked) +
            $"\nالتَصفِيَةُ بِـ{PolicyToken} — نَفسُ السِياسَةِ الَّتي تَرُدُّ النُقطَة، فَلا يَنحَرِفُ طَرَفٌ عَن طَرَف.");
    }

    // ─── الفَحصُ الثالِث: الجَرد ───────────────────────────────────────

    /// <summary>
    /// <para><b>بابانِ وُجِدا اليَوم، والثالِثُ يُبحَثُ عَنه لا
    /// يُنتَظَر.</b> كُلُّ مَوضِعٍ يَكتُبُ <c>ActiveRole</c> في
    /// <c>libs/</c> و<c>apps/</c> يُعلِنُ السِياسَةَ في كُتلَتِه أَو
    /// يُثَبَّتُ بِسَبَبِه — فَالمَوضِعُ الرابِعُ يَحمَرُّ يَومَ
    /// يُكتَب، لا يَومَ يُكتَشَف.</para>
    /// </summary>
    [Fact]
    public void Every_write_of_the_active_role_declares_the_policy_or_is_pinned()
    {
        var writes = ActiveRoleWrites();

        // عَدّاد (القاعِدَة ١٠).
        Assert.True(writes.Count >= 9,
            $"أَداةٌ عَمياء: وُجِدَ {writes.Count} مَوضِعَ كِتابَةٍ لِلدَور — والمَقيسُ ٩ فَأَكثَر.");

        var pinned = PinnedWrites
            .Select(p => $"{p.File}:{p.Line}")
            .ToHashSet(StringComparer.Ordinal);

        var breaches = writes
            .Where(w => !w.Window.Contains(PolicyToken, StringComparison.Ordinal))
            .Where(w => !pinned.Contains($"{w.File}:{w.Line}"))
            .Select(w => $"{w.File}:{w.Line}")
            .ToArray();

        Assert.True(breaches.Length == 0,
            $"كِتابَةُ دَورٍ بِلا سِياسَةِ مَنعٍ ذاتيٍّ وغَيرُ مُثَبَّتَة ({writes.Count} مَوضِعاً مَفحوصاً):\n  " +
            string.Join("\n  ", breaches) +
            $"\nإمّا نِداءُ {PolicyToken} في نَفسِ الكُتلَة، وإمّا سَطرٌ في PinnedWrites يَقولُ لِماذا — في نَفسِ الكوميت.");
    }

    /// <summary>ونِصفُه الآخَر — الاتِّجاهُ المُعاكِس: مُثَبَّتٌ زالَ أَو
    /// صارَ يُعلِنُ السِياسَةَ يَحمَرُّ حَتّى يُرفَع، وسَبَبٌ قَصيرٌ
    /// يَحمَرّ. <b>فَالقائِمَةُ تَصِفُ الواقِعَ أَو تَرِثُّ حَتّى تَصيرَ
    /// قائِمَةَ إسكات.</b></summary>
    [Fact]
    public void No_pinned_role_write_outlives_its_reason()
    {
        var writes = ActiveRoleWrites();
        var all = writes.Select(w => $"{w.File}:{w.Line}").ToHashSet(StringComparer.Ordinal);
        var declaring = writes
            .Where(w => w.Window.Contains(PolicyToken, StringComparison.Ordinal))
            .Select(w => $"{w.File}:{w.Line}")
            .ToHashSet(StringComparer.Ordinal);

        var gone = PinnedWrites
            .Where(p => !all.Contains($"{p.File}:{p.Line}"))
            .Select(p => $"{p.File}:{p.Line}")
            .ToArray();

        var covered = PinnedWrites
            .Where(p => declaring.Contains($"{p.File}:{p.Line}"))
            .Select(p => $"{p.File}:{p.Line}")
            .ToArray();

        Assert.True(gone.Length == 0,
            "مَوضِعٌ مُثَبَّتٌ لا وُجودَ لَه — يُرفَعُ أَو يُصَحَّحُ رَقمُه:\n  " + string.Join("\n  ", gone));
        Assert.True(covered.Length == 0,
            "مَوضِعٌ مُثَبَّتٌ صارَ يُعلِنُ السِياسَةَ — يُرفَعُ مِن القائِمَة:\n  " + string.Join("\n  ", covered));

        foreach (var p in PinnedWrites)
            Assert.True(p.WhyAr.Length > 30, $"استِثناءٌ بِلا سَبَبٍ مَقروء: {p.File}:{p.Line}");

        Assert.Equal(PinnedWrites.Length,
            PinnedWrites.Select(p => $"{p.File}:{p.Line}").Distinct(StringComparer.Ordinal).Count());
    }
}
