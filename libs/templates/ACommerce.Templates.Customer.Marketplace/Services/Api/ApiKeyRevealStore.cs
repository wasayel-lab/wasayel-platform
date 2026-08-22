using Microsoft.Extensions.Caching.Memory;

namespace ACommerce.Templates.Customer.Marketplace.Services.Api;

/// <summary>
/// <para><b>السِرُّ يُعرَض مَرَّةً — وهذا هُوَ وِعاؤُه بَينَ النُقطَةِ
/// والشاشَة.</b> نُقطَةُ الإصدار <c>POST</c> ثُمَّ تُحَوِّل، والشاشَةُ
/// تُرسَم بِطَلَبٍ ثانٍ — فَبَينَهُما طَلَبان، ولا بُدَّ مِن وِعاء.</para>
///
/// <para><b>ولِماذا لا في المَسار ولا في الوَثيقَة</b> — والاثنانِ
/// كانا أَقصَر:</para>
/// <list type="bullet">
///   <item><b>لا في سِلسِلَة الاستِعلام</b>: <c>UseSerilogRequestLogging</c>
///   مُفَعَّل في <c>UsePlatformHost</c>، فَالمَسارُ يُكتَب في السِجِلّ
///   كامِلاً. سِرٌّ في <c>?key=…</c> يَصير سِرّاً في مِلَفّ لوغ،
///   وفي تاريخ المُتَصَفِّح، وفي <c>Referer</c> لِأَيّ أَصلٍ
///   خارِجيّ تَطلُبُه الصَفحَة.</item>
///   <item><b>ولا في الوَثيقَة</b>: تَخزينُ السِرّ خاماً يُبطِل
///   السَبَبَ الَّذي جُزِّئَ لِأَجلِه — تَسريبُ قاعِدَةِ البَيانات
///   يُعطي مَفاتيحَ صالِحَة.</item>
/// </list>
///
/// <para><b>وحَدُّه مُعلَنٌ لا مُبتَلَع</b>: الوِعاءُ ذاكِرَةُ
/// العَمَلِيَّة (<see cref="IMemoryCache"/> المُسَجَّل في
/// <c>AddPlatformMultiTenancy</c> — الأُنبوبُ القائِم، لا رابِع).
/// فَلَو صارَ المُضيفُ نُسَخاً مُتَعَدِّدَة بِلا لُصوقٍ، أَو أُعيدَ
/// إقلاعُه بَينَ الطَلَبَين، ضاعَ العَرضُ ولَزِمَ إصدارُ مِفتاحٍ
/// جَديد — والمُصدَرُ يَبقى صالِحاً ويُبطَل بِزِرِّه. نُسخَةٌ واحِدَة
/// اليَوم، وهذا مَقيسٌ في <c>docs/DEPLOY.md</c>، فَالحَدُّ لا
/// يُصيب أَحَداً؛ ويَومَ تَتَعَدَّد النُسَخ يُنقَل الوِعاءُ إلى
/// كوكي مُوَقَّعَة أَو إلى Redis — والمُستَهلِكانِ هُما هُما.</para>
/// </summary>
public sealed class ApiKeyRevealStore
{
    private readonly IMemoryCache _cache;

    public ApiKeyRevealStore(IMemoryCache cache) => _cache = cache;

    /// <summary>نافِذَةُ العَرض. دَقائِقُ مَعدودَة تَكفي لِتَحويلٍ
    /// وتَصييرِ صَفحَة، ولا تَترُك السِرَّ في الذاكِرَة يَوماً.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    private static string Key(string tenantSlug, string keyId) => $"apikey.reveal:{tenantSlug}:{keyId}";

    public void Stash(string tenantSlug, string keyId, string presented) =>
        _cache.Set(Key(tenantSlug, keyId), presented, Window);

    /// <summary><b>مَرَّةً واحِدَة</b>: القِراءَةُ تَحذِف. فَتَحديثُ
    /// الصَفحَة لا يُعيدُ عَرضَ السِرّ، وهذا هُوَ العَقدُ المُعلَن
    /// لِلمُستَخدِم لا وَعدٌ في نَصّ.</summary>
    public string? TakeOnce(string tenantSlug, string keyId)
    {
        var k = Key(tenantSlug, keyId);
        if (!_cache.TryGetValue(k, out string? presented)) return null;
        _cache.Remove(k);
        return presented;
    }
}
