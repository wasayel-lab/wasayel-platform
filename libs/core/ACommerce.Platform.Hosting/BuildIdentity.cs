using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ACommerce.Platform.Hosting;

/// <summary>
/// <para><b>الثُنائِيُّ يَحمِلُ إيداعَه</b> — نُقطَةُ <c>/health</c> تُجيبُ
/// «أَيُّ إيداعٍ يَخدِمُ الآن؟» مِن العَمَلِيَّةِ نَفسِها، وهو ما لا
/// يُجيبُه API الـSpace ولا رَأسُ الوَسيط.</para>
///
/// <para><b>العِلَّةُ المَقيسَة (‏2026-08-30)</b>: اكتَمَلَ نَشرٌ ناجِح،
/// ورَدَّ المَوقِعُ ‏200 على تِسعَةِ مَسارات — وتَعَذَّرَ إثباتُ أَيِّ
/// إيداعٍ يَخدِمُه الـSpace. ‏<c>huggingface.co/api/spaces/…</c> يَرُدُّ
/// ‏401 بِلا رَمز؛ ورَأسُ <c>x-proxied-replica</c> يُثبِتُ <b>تَبَدُّلَ
/// حاوِيَةٍ لا هُوِيَّةَ بِناء</b>؛ و<c>runtime.stage</c> — بِنَصِّ
/// وَظيفَةِ النَشرِ نَفسِها — <b>خَبَرٌ لا بُرهان</b>.</para>
///
/// <para><b>والقَيدُ الحاكِم</b>: نُقطَةٌ تَقرَأُ حالَتَها وَقتَ التَشغيلِ
/// <b>تَكذِب</b>. لِذلك تُقرَأُ البَصمَةُ مِن
/// <c>AssemblyInformationalVersionAttribute</c> <b>وَحدَها</b> — سِمَةٌ
/// يَبُثُّها المُصَرِّفُ في <c>V1.App.dll</c> حينَ يُمَرَّر
/// <c>-p:SourceRevisionId=</c>. فَتَغييرُ الجَوابِ يَقتَضي ثُنائِيّاً
/// جَديداً، وذاكَ يَقتَضي <c>dotnet publish</c> جَديداً، وذاكَ يَقتَضي
/// بِناءَ صورَةٍ جَديداً. <b>ولا سَبيلَ وَقتَ‑تَشغيلِيٍّ إلى تَحريكِه.</b></para>
///
/// <para><b>وما قيسَ على هذا المَشروعِ بِعَينِه</b> (‏SDK ‏10.0.302):
/// <c>-p:SourceRevisionId=b4bd8885…</c> ⇒ <c>1.0.0+b4bd8885…</c>، و
/// <c>-p:SourceRevisionId=0000…0001</c> ⇒ <c>1.0.0+0000…0001</c>. أَي أَنّ
/// القيمَةَ المُمَرَّرَةَ هي الَّتي تَبلُغُ الثُنائِيَّ لا سِواها.</para>
///
/// <para><b>وهذِه نُقطَةُ هُوِيَّةٍ وحَياة، لا نُقطَةُ جاهِزِيَّة</b>:
/// تَرُدُّ ‏200 دائِماً ولا تَفحَصُ قاعِدَةً ولا خِدمَةً. الجاهِزِيَّةُ —
/// إن لَزِمَت يَوماً — <b>مَسارٌ آخَر</b> يَرُدُّ ‏503 مَعَ
/// <c>Retry-After</c> ولا يَرُدُّ ‏200 وهُوَ غَير جاهِز. والتَفصيلُ
/// وحُدودُ ما لا تَحرُسُه هذِه النُقطَةُ في
/// <c>docs/ADR-019-THE-BINARY-CARRIES-ITS-OWN-COMMIT.md</c>.</para>
///
/// <para><b>ولِماذا هُنا لا في <c>Program.cs</c></b>: مَشروعُ الاختِبارات
/// يُحيلُ إلى <c>ACommerce.Platform.Hosting</c> ولا يُحيلُ إلى
/// <c>V1.App</c>. فَنُقطَةٌ مَكتوبَةٌ في <c>Program.cs</c> <b>غَير
/// قابِلَةٍ لِلاختِبارِ إلّا بِمَسحِ المَصدَر</b>، والمَكتوبَةُ هُنا
/// تُختَبَرُ على HTTP حَقيقيّ.</para>
///
/// <para><b>وشَرطُ الاستِخراجِ إن نَمَت</b> (القاعِدَة ١): تَبقى هُنا ما
/// دامَ لَها <b>مُستَهلِكٌ تَشغيليٌّ واحِد</b> (<c>apps/V1.App</c>).
/// ولا تُنقَلُ إلى حُزمَةٍ مُستَقِلَّةٍ قَبلَ <b>ثَلاثَةِ مُستَهلِكين</b>
/// — والمُضيفُ الثاني لَم يوجَد بَعد.</para>
/// </summary>
public static class BuildIdentity
{
    /// <summary><b>سَبَبُ الرَباعِيَّةِ لا <c>no-store</c> وَحدَه</b>:
    /// <c>no-store</c> كافٍ بِنَصِّ RFC 9111، والوَسائِطُ في الواقِعِ
    /// لَيسَت كُلُّها بِنَصِّ RFC. والأَحرُفُ الزائِدَةُ ثَمَنٌ لا
    /// يُذكَرُ مُقابِلَ وَسيطٍ واحِدٍ مُتَساهِل.
    ///
    /// <para><b>ولِماذا صَراحَةً — مَقيسٌ لا مَفتَرَض</b>: مَسحُ
    /// <c>Cache-Control</c> على <c>apps/</c> و<c>libs/</c> أَعطى
    /// <b>مَوضِعاً واحِداً يَتيماً</b> قيمَتُه <c>public, max-age=300</c>
    /// — أَي <b>صِفرُ <c>no-store</c> في المُستَودَعِ كُلِّه</b>. و
    /// <c>no-cache, no-store</c> الَّذي يَظهَرُ على الصَفَحاتِ الحَيَّةِ
    /// يَأتي مِن مُصَيِّرِ Razor Components، <b>ونُقطَةُ
    /// <c>MapGet</c> لا تَرِثُه</b>. فَبِلا كِتابَةٍ بِاليَدِ تَخرُجُ
    /// الاستِجابَةُ بِلا رَأسِ تَخزينٍ إطلاقاً، وأَيُّ وَسيطٍ حُرٌّ في
    /// تَخزينِها استِدلالِيّاً — <b>ورَدٌّ مُخَزَّنٌ على <c>/health</c>
    /// هو بِالضَبطِ الكَذِبَةُ الَّتي بُنِيَت النُقطَةُ لِمَنعِها</b>.</para>
    ///
    /// <para><b>وما لا يَدَّعيه هذا الرَأس</b>: وُجودُه <b>في
    /// الرَدّ</b> لا يُثبِتُ أَنّ الرَدَّ لَم يُخَزَّن — الرَأسُ
    /// يُسافِرُ مَعَ الجِسمِ المُخَزَّن. نَقضُ التَخزينِ بِالطَلَبِ
    /// وفَحصُ <c>age</c>/<c>etag</c>/<c>x-cache</c> يَقَعانِ في بَوّابَةِ
    /// النَشر، لا هُنا.</para></summary>
    private const string NoStore = "no-store, no-cache, must-revalidate, max-age=0";

    /// <summary>
    /// <para>البَصمَةُ مِن <c>AssemblyInformationalVersionAttribute</c>:
    /// ما بَعدَ <b>أَوَّلِ</b> <c>+</c> إن كانَ <b>أَربَعينَ مِحرَفاً
    /// سِتّينِيّاً</b> بِالضَبط، وإلّا <c>null</c>.</para>
    ///
    /// <para><b>ولِماذا أَربَعونَ لا اثنا عَشَر</b>: البَوّابَةُ تُقارِنُ
    /// بِمُساواةِ سَلاسِل. والبادِئَةُ القَصيرَةُ تُدخِلُ قاعِدَةَ
    /// مُطابَقَةٍ جُزئِيَّةٍ بِلا أَيِّ مَكسَبٍ أَمنيّ — اثنا عَشَرَ
    /// مِحرَفاً مُعَرِّفَةٌ تَماماً كَالأَربَعين، والفَرقُ أَنّ
    /// «‏يَبدَأُ بِـ» قاعِدَةٌ تَحتاجُ حُكماً و«‏يُساوي» لا تَحتاج.</para>
    /// </summary>
    public static string? CommitFrom(string? informationalVersion)
    {
        if (string.IsNullOrEmpty(informationalVersion)) return null;

        var plus = informationalVersion.IndexOf('+');
        if (plus < 0) return null;

        var suffix = informationalVersion[(plus + 1)..];
        if (suffix.Length != 40) return null;

        // ‏`IsAsciiHexDigitLower` تَشمَلُ ‏0–9 وa–f. والحَرفُ الكَبيرُ
        // مَرفوضٌ عَمداً: ‏git يُخرِجُ صَغيراً دائِماً، وقَبولُ
        // الحالَتَينِ يُدخِلُ تَطبيعاً في مُقارَنَةٍ وُضِعَت لِتَكونَ
        // مُساواةَ سَلسِلَتَينِ بِلا حُكم.
        foreach (var c in suffix)
            if (!char.IsAsciiHexDigitLower(c))
                return null;

        return suffix;
    }

    /// <summary>
    /// <para>يُسَجِّلُ <c>GET /health</c> — حَقلانِ اثنان لا أَكثَر.</para>
    ///
    /// <para><b><c>commit</c></b> — أَربَعونَ مِحرَفاً سِتّينِيّاً، أَو
    /// <c>"unknown"</c> حَرفِيّاً حينَ لا يَحمِلُ الثُنائِيُّ بَصمَة. هو
    /// <b>الرَقَمُ الأَوَّل</b> في «مُقارَنَةِ رَقمَين»: يُقارَنُ حَرفاً
    /// بِـ<c>git rev-parse HEAD</c>، بِمُساواةِ سَلسِلَتَينِ بِلا تَفسيرٍ
    /// ولا اشتِقاق.</para>
    ///
    /// <para><b><c>startedAt</c></b> — لَحظَةُ إقلاعِ العَمَلِيَّةِ لا
    /// لَحظَةُ الطَلَب، بِدِقَّةِ الثانِيَة و<c>InvariantCulture</c>.
    /// <b>ولَيسَ زينَة</b>: بِه وَحدَه يُفَرَّقُ «بِناءٌ جَديدٌ وَصَل» عَن
    /// «نَفسُ البِناءِ أُعيدَ تَشغيلُه» — وهُما يُعطِيانِ <c>commit</c>
    /// مُتَطابِقاً. وعَلَيه تَقومُ بَوّابَةُ النَشرِ في تَمييزِ إعادَةِ
    /// التَشغيلِ مِن الدَوَران.</para>
    ///
    /// <para><b>ولِماذا تُمَرَّرُ السِلسِلَةُ وَسيطاً لا تُقرَأُ مِن
    /// <c>Assembly.GetEntryAssembly()</c></b>: التَجميعَةُ الداخِلَةُ
    /// تَصيرُ صَريحَةً وقابِلَةً لِلتَثبيتِ في الاختِبار؛ و
    /// <c>GetEntryAssembly()</c> في مُشَغِّلِ الاختِباراتِ يُعطي
    /// <c>testhost</c> فَيَصيرُ الاختِبارُ يَقيسُ شَيئاً آخَر.</para>
    ///
    /// <para><b>و<c>status: "ok"</c> مُستَبعَدٌ عَمداً</b> وإن اقتَرَحَته
    /// <c>docs/DEPLOY.md</c>: ثابِتٌ بِالبِناء، فَمَعلوماتُه صِفر — لا
    /// مَسارَ يُصدِرُه غَيرَ <c>"ok"</c>. وهذِه بِالضَبطِ سُلالَةُ
    /// «خَبَرٌ لا بُرهان» الَّتي كُتِبَت هذِه النُقطَةُ لِقَتلِها.
    /// و«العَمَلِيَّةُ أَجابَت» يَقولُها ‏200 نَفسُه.</para>
    /// </summary>
    public static IEndpointRouteBuilder MapBuildIdentity(
        this IEndpointRouteBuilder endpoints,
        string? informationalVersion,
        DateTimeOffset startedAt)
    {
        var commit = CommitFrom(informationalVersion) ?? "unknown";
        var started = startedAt.ToUniversalTime()
            .ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);

        endpoints.MapGet("/health", (HttpResponse response) =>
        {
            response.Headers.CacheControl = NoStore;
            response.Headers.Pragma = "no-cache";
            return Results.Json(new { commit, startedAt = started });
        });

        return endpoints;
    }
}
