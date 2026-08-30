using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace ACommerce.Platform.Hosting;

/// <summary>
/// <para><b>ما نُصَدِّقُه مِن رُؤوسِ <c>X-Forwarded-*</c> — قَرارٌ
/// واحِدٌ في مَوضِعٍ واحِد.</b> كانَ مَكتوباً داخِلَ لامدا في
/// <c>Program.cs</c>، فَلَم يَكُن لَه اختِبارٌ واحِد: مَشروعُ
/// الاختِباراتِ لا يُحيلُ إلى <c>V1.App</c>، فَما يُكتَبُ هُناكَ
/// <b>لا يُقاسُ إلّا بِمَسحِ المَصدَر</b>. والنَقلُ إلى هُنا هُوَ
/// نَفسُ عِلَّةِ <see cref="BuildIdentity"/> حَرفاً (القاعِدَة ٢:
/// الحَدُّ الَّذي لا يُقاسُ آلِيّاً يَنهار).</para>
///
/// <para><b>والنَقلُ هُنا نَقلٌ بِلا تَغييرِ حَرف</b> — نَفسُ
/// الأَعلامِ الثَلاثَةِ ونَفسُ التَفريغَين. تَغييرُ السُلوكِ يَأتي
/// بَعدَ أَن يَحمَرَّ ما يَقيسُه، لا مَعَه.</para>
/// </summary>
public static class ForwardedHeadersPolicy
{
    /// <summary>
    /// <para>خَلفَ وَسيطٍ (‏Hugging Face Spaces, Cloudflare, …) نَحتاجُ
    /// قِراءَةَ <c>X-Forwarded-*</c> لِيَكشِفَ <c>Request.IsHttps</c>
    /// الصَحيح — وإلّا حَسِبَ <c>AuthSession</c> الاتِّصالَ HTTP
    /// فَكَسَرَ كوكي <c>Secure</c> في الإنتاج.</para>
    /// </summary>
    public static void Apply(ForwardedHeadersOptions opts)
    {
        opts.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor |
            ForwardedHeaders.XForwardedProto |
            ForwardedHeaders.XForwardedHost;

        // proxy المُستَضيف قَد لا يَكون في 127.0.0.1 — اِقبَل مِن أَيّ مَصدَر.
        // آمِن لِأَنّ الـ middleware يَكتُب Request.Scheme فَقَط، لا الـ IP.
        opts.KnownNetworks.Clear();
        opts.KnownProxies.Clear();
    }
}
