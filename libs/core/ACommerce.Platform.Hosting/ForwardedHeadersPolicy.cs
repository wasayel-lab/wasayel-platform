using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;

namespace ACommerce.Platform.Hosting;

/// <summary>
/// <para><b>ما نُصَدِّقُه مِن رُؤوسِ <c>X-Forwarded-*</c> — قَرارٌ
/// واحِدٌ في مَوضِعٍ واحِدٍ مَقيس.</b> كانَ مَكتوباً داخِلَ لامدا في
/// <c>Program.cs</c>، فَلَم يَكُن لَه اختِبارٌ واحِد: مَشروعُ
/// الاختِباراتِ لا يُحيلُ إلى <c>V1.App</c>، فَما يُكتَبُ هُناكَ
/// <b>لا يُقاسُ إلّا بِمَسحِ المَصدَر</b> (القاعِدَة ٢، ونَفسُ عِلَّةِ
/// <see cref="BuildIdentity"/> حَرفاً).</para>
///
/// <para><b>والعِلَّةُ الَّتي كَتَبَت القُفل — مَقيسَةٌ عَلى الإنتاجِ
/// الحَيّ</b> (‏2026-08-30): رَأسٌ واحِدٌ مِن زائِرٍ واحِد،
/// <c>X-Forwarded-Host: evil.example</c>، جَعَلَ الصَفحَةَ تُعلِنُ
/// <c>canonical</c> و<c>og:url</c> و<c>og:image</c> بِنِطاقٍ يَملِكُه
/// هُوَ — لِأَنَّ <c>XForwardedHost</c> كانَ مُفَعَّلاً و
/// <c>KnownProxies</c> مُفَرَّغاً، فَصارَ <b>كُلُّ نِدٍّ وَسيطاً
/// مَوثوقاً</b>، والنِدُّ خَلفَ HF هُوَ الزائِر.</para>
///
/// <para><b>والقاعِدَةُ الحاكِمَة: المُضيفُ يُصَدَّقُ إذا سُمِّي، ولا
/// يُصَدَّقُ لِأَنَّه وَصَل.</b> بِلا قائِمَةٍ مُهَيَّأَةٍ — وهُوَ
/// الوَضعُ الافتِراضيّ — <b>لا يُقرَأُ <c>X-Forwarded-Host</c>
/// إطلاقاً</b>، ويَبقى <c>Request.Host</c> رَأسَ <c>Host</c> كَما
/// وَصَل.</para>
///
/// <para><b>ولِماذا الافتِراضُ مُغلَقٌ ولا يَكسِرُ الإنتاجَ القائِم —
/// مَقيسٌ لا مَظنون</b>: ثَلاثُ قِياساتٍ عَلى الـSpace الحَيّ.
/// <c>X-Forwarded-Host: aaa, bbb</c> أَعطى <c>bbb</c>، ورَأسانِ
/// مُنفَصِلانِ أَعطَيا الأَخير ⇒ <b>الوَسيطُ يَأخُذُ آخِرَ قيمَة</b>؛
/// وقيمَةُ الزائِرِ المَحقونَةُ <b>فازَت</b> ⇒ ‏HF <b>لا يُلحِقُ
/// <c>X-Forwarded-Host</c> أَصلاً</b>. ومَعَ ذلكَ كانَ
/// <c>canonical</c> بِلا رَأسٍ صَحيحاً ⇒ <b>رَأسُ <c>Host</c> نَفسُه
/// يَحمِلُ مُضيفَ الـSpace العَلَنيّ</b>. فَإغلاقُ العَلَمِ يُبقي
/// الجَوابَ كَما هُوَ حَرفاً.</para>
///
/// <para><b>وما لا يُنزَع</b>: <c>XForwardedProto</c> — ‏HF يُنهي TLS
/// عِندَ حافَّتِه ويُكَلِّمُ الحاوِيَةَ بِـHTTP، فَبِدونِه يَحسِبُ
/// <c>AuthSession</c> الاتِّصالَ HTTP فَيَسقُطُ كوكي <c>Secure</c>.
/// وقيسَ أَنَّ قيمَةَ HF تَغلِبُ قيمَةَ العَميلِ (‏<c>Proto: http</c>
/// مَحقونٌ لَم يُنزِل الصَفحَةَ إلى HTTP). و<c>XForwardedFor</c>:
/// راجِع «ما لَم يُحسَم» في
/// <c>docs/ADR-023-A-FORWARDED-HOST-IS-TRUSTED-ONLY-IF-IT-IS-NAMED.md</c>.</para>
/// </summary>
public sealed class ForwardedHeadersPolicy
{
    /// <summary>مِفتاحُ التَهيئَة — قائِمَةٌ أَو نَصٌّ مَفصولٌ
    /// بِفَواصِل. ومُتَغَيِّرُ البيئَةِ
    /// <c>ForwardedHeaders__AllowedHosts</c> يَملَؤُه كَذلِك.</summary>
    public const string ConfigurationKey = "ForwardedHeaders:AllowedHosts";

    /// <summary>مُتَغَيِّرُ بيئَةٍ مُسَطَّحٌ — <b>لِمُستَضيفٍ لا يَقبَلُ
    /// الشَرطَتَينِ السُفلِيَّتَين</b> في أَسماءِ الأَسرار. نَفسُ
    /// اصطِلاحِ <c>ACOMMERCE_BASE_DOMAIN</c>.</summary>
    public const string EnvironmentVariable = "ACOMMERCE_FORWARDED_ALLOWED_HOSTS";

    /// <summary>المُضيفاتُ الَّتي يُقبَلُ <c>X-Forwarded-Host</c>
    /// بِها. فارِغَةً (وهُوَ الافتِراضيّ) لا يُقرَأُ الرَأسُ
    /// إطلاقاً.</summary>
    public IReadOnlyList<string> AllowedHosts { get; init; } = [];

    public bool TrustsForwardedHost => AllowedHosts.Count > 0;

    public static ForwardedHeadersPolicy FromConfiguration(IConfiguration? config)
    {
        // الشَكلُ المَصفوفيّ (`"AllowedHosts": [ … ]`) أَوَّلاً: قِراءَةُ
        // المِفتاحِ نَصّاً تُرجِعُ `null` عَلَيه فَتَبدو التَهيئَةُ
        // غائِبَةً وهي مَكتوبَة — **صَمتٌ يُغلِقُ ما قَصَدَ المُهَيِّئُ
        // فَتحَه، ولا يُقالُ لَه**.
        var section = config?.GetSection(ConfigurationKey);
        var listed = section?.GetChildren().Select(c => c.Value).ToArray();

        var raw = section?.Value;
        if (string.IsNullOrWhiteSpace(raw))
            raw = config?[EnvironmentVariable]
                  ?? Environment.GetEnvironmentVariable(EnvironmentVariable);

        var hosts = ParseAllowedHosts(listed is { Length: > 0 } ? listed : [raw]);
        return new ForwardedHeadersPolicy { AllowedHosts = hosts };
    }

    /// <summary>
    /// <para><b>دالَّةٌ نَقِيَّةٌ تُقاسُ وَحدَها</b>: تَقسِمُ عَلى
    /// الفاصِلَةِ والفاصِلَةِ المَنقوطَةِ والمَسافَة، وتُطَبِّعُ، وتَحذِفُ
    /// المُكَرَّر.</para>
    ///
    /// <para><b>وتُسقِطُ البَدائِلَ الشامِلَةَ صَراحَةً</b>: ‏<c>*</c>
    /// و<c>[::]</c> و<c>0.0.0.0</c> — لِأَنَّ
    /// <c>ForwardedHeadersMiddleware</c> يَقرَأُ أَيّاً مِنها
    /// «‏اِقبَل كُلَّ مُضيف‏»، أَي **العَطَبَ الَّذي كُتِبَ هذا
    /// الصِنفُ لِسَدِّه** مَكتوباً بِيَدِ المُهَيِّئ. و<c>*.example.com</c>
    /// لَيسَ مِنها — نِطاقٌ فَرعِيٌّ مَحدودٌ يَقبَلُه الوَسيطُ
    /// ونَقبَلُه.</para>
    /// </summary>
    public static IReadOnlyList<string> ParseAllowedHosts(params string?[] values)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hosts = new List<string>();

        foreach (var value in values ?? [])
        {
            if (string.IsNullOrWhiteSpace(value)) continue;

            foreach (var part in value.Split([',', ';', ' ', '\t', '\r', '\n'],
                                             StringSplitOptions.RemoveEmptyEntries))
            {
                var host = part.Trim().TrimEnd('.').ToLowerInvariant();
                if (host.Length == 0) continue;
                if (host is "*" or "[::]" or "0.0.0.0") continue;
                if (seen.Add(host)) hosts.Add(host);
            }
        }

        return hosts;
    }

    /// <summary>
    /// <para>خَلفَ وَسيطٍ (‏Hugging Face Spaces, Cloudflare, …) نَحتاجُ
    /// قِراءَةَ <c>X-Forwarded-Proto</c> لِيَكشِفَ <c>Request.IsHttps</c>
    /// الصَحيح — وإلّا حَسِبَ <c>AuthSession</c> الاتِّصالَ HTTP
    /// فَكَسَرَ كوكي <c>Secure</c> في الإنتاج.</para>
    ///
    /// <para><b>و<c>KnownProxies</c>/<c>KnownNetworks</c> يَبقَيانِ
    /// مُفَرَّغَين</b>: عُنوانُ حافَّةِ HF غَيرُ مَعلومٍ ولا ثابِت،
    /// فَقائِمَةُ وُكَلاءَ مَكتوبَةٌ بِالتَخمينِ تُسقِطُ الرُؤوسَ
    /// كُلَّها وتَكسِرُ الكوكي. <b>والتَعليقُ الَّذي كانَ هُنا كانَ
    /// يَقولُ إنَّ التَفريغَ «آمِنٌ لِأَنَّ الوَسيطَ يَكتُبُ
    /// <c>Request.Scheme</c> فَقَط، لا الـIP» — وكِلا شَطرَيه غَيرُ
    /// صَحيح</b>: يَكتُبُ <c>Request.Host</c> كَذلك (وهُوَ العَطَبُ
    /// بِعَينِه)، ويَكتُبُ <c>Connection.RemoteIpAddress</c> كَذلك.
    /// فَالأَمانُ لا يَأتي مِن التَفريغِ بَل مِن **حَصرِ ما يُقرَأ**:
    /// المُضيفُ بِقائِمَةٍ مُسَمّاة، والباقي مُصَرَّحٌ بِحَدِّه.</para>
    /// </summary>
    public void ApplyTo(ForwardedHeadersOptions opts)
    {
        ArgumentNullException.ThrowIfNull(opts);

        opts.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor |
            ForwardedHeaders.XForwardedProto;

        opts.AllowedHosts.Clear();
        if (TrustsForwardedHost)
        {
            opts.ForwardedHeaders |= ForwardedHeaders.XForwardedHost;
            foreach (var host in AllowedHosts)
                opts.AllowedHosts.Add(host);
        }

        opts.KnownNetworks.Clear();
        opts.KnownProxies.Clear();
    }
}
