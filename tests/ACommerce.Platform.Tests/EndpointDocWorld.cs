using System.Reflection;
using Marten;

namespace ACommerce.Platform.Tests;

// ═══ عالَمُ وَثائِقٍ مُشتَرَكٌ لِحُرّاسِ النِقاطِ السُلوكِيَّة ═══════════
//
// **العِلَّةُ الَّتي أَخرَجَت هذَينِ الصِنفَينِ إلى مِلَفٍّ مُستَقِلّ**:
// كانا `file`-scoped في `PayPalEndpointBehaviourTests.cs`، فَلَمّا
// وَصَلَ مُزَوِّدٌ ثانٍ بِنَفسِ الحاجَةِ بِالضَبط (مُضيفٌ مُصَغَّرٌ
// يُشَغِّل نُقطَةً، وجَلسَةُ Marten في الذاكِرَةِ تَعُدُّ ما لُمِس)
// كانَ البَديلُ **نَسخَ ‏128 سَطراً**. ونُسخَتانِ مِن أَداةِ قِياسٍ
// تَنجَرِفان — فَتَقيسُ إحداهُما ما لا تَقيسُه الأُخرى، ويَبقى
// الفَرقُ غَيرَ مَرئيّ (القاعِدَة ٢).
//
// **ومُستَهلِكانِ لا ثَلاثَة، ويُقالُ لِماذا جازَ الاستِخراج**
// (القاعِدَة ١): هذا **لَيسَ تَجريداً جَديداً** — لا واجِهَةَ ولا
// طَبَقَةَ ولا خِيارَ تَركيب. هُوَ **نَفسُ الصِنفَينِ بِحَرفِهِما**
// نُقِلا مِن نِطاقِ مِلَفٍّ إلى نِطاقِ مُجَمَّع، بِصِفرِ تَغييرٍ في
// السُلوك. والبَديلُ نَسخٌ، لا بَقاءٌ على ما كان.
// ─── عالَمُ الوَثائِق — قِراءَةٌ وكِتابَةٌ في الذاكِرَة، ومَعَهُما عَدّاد ─

/// <summary>
/// <para><b>وَثائِقُ الاختِبارِ ومَقاييسُه.</b> ‏<see cref="Docs"/> ما
/// تَقرَؤُه النُقطَة، و<see cref="Stored"/> ما كَتَبَته فِعلاً،
/// و<see cref="Members"/> كُلُّ عُضوٍ نودِيَ على الجَلسَة — <b>فَـ«صِفرُ
/// كِتابَة» تُقاس، ولا تُستَنتَج مِن غِيابِ انفِجار</b>.</para>
/// </summary>
internal sealed class DocWorld
{
    public Dictionary<string, object> Docs { get; } = new(StringComparer.Ordinal);
    public List<object> Stored { get; } = new();
    public List<string> Members { get; } = new();
    public int SaveCalls { get; set; }

    public IDocumentSession Session { get; }
    public IDocumentStore Store { get; }

    public DocWorld()
    {
        Session = MartenProxy.For<IDocumentSession>(this);
        Store   = MartenProxy.For<IDocumentStore>(this);
    }

    public static string KeyOf(Type t, string id) => $"{t.Name}|{id}";

    public static string IdOf(object doc)
        => doc.GetType().GetProperty("Id")?.GetValue(doc)?.ToString() ?? "";

    public DocWorld Put(object doc)
    {
        Docs[KeyOf(doc.GetType(), IdOf(doc))] = doc;
        return this;
    }

    public T? Read<T>(string id) where T : class
        => Docs.TryGetValue(KeyOf(typeof(T), id), out var d) ? (T)d : null;

    /// <summary>
    /// <para><b>ما لَمَسَتهُ النُقطَةُ فِعلاً</b> — <c>Members</c> بِلا
    /// دَورَةِ الحَياة.</para>
    ///
    /// <para><b>ولِماذا يُستَثنى التَخَلُّصُ ويُقالُ لِماذا</b>: الجَلسَةُ
    /// مُسَجَّلَةٌ <c>Scoped</c>، فَالحاويَةُ تُنادي <c>DisposeAsync</c>
    /// عِندَ نِهايَةِ كُلِّ طَلَبٍ <b>ولَو لَم يَقرَأ أَحَدٌ حَرفاً</b>.
    /// فَعَدُّه «لَمسَةً» يَجعَل الشَرطَ مُستَحيلاً — <b>واختِبارٌ لا
    /// يُمكِن أَن يَخضَرَّ لا يَقيسُ شَيئاً</b>. والقائِمَةُ الكامِلَةُ
    /// تَبقى في <see cref="Members"/> لِمَن أَرادَ رُؤيَةَ كُلِّ
    /// شَيء.</para>
    /// </summary>
    public IReadOnlyList<string> Touches
        => Members.Where(m => m is not ("Dispose" or "DisposeAsync")).ToList();

    /// <summary>ما كُتِبَ مِن نَوعٍ بِعَينِه — «لَم تُكتَب وَثيقَةُ
    /// طَلَبٍ» شَرطٌ يُعَدّ.</summary>
    public int Wrote<T>() => Stored.Count(x => x is T);
}

/// <summary>
/// <para><b>جَلسَةُ Marten مُوَلَّدَةٌ لا مَكتوبَة.</b> الواجِهَةُ ‏144
/// عُضواً عَبرَ سِلسِلَتِها، وكِتابَتُها بِاليَدِ ‏300 سَطرٍ مِن
/// <c>throw</c>. و<c>DispatchProxy</c> يُعطيها في أَربَعينَ سَطراً
/// <b>ويَعُدُّ ما نودِيَ عَلَيه</b> — وذاكَ ما لا يُعطيه سَتُّ مِئَةِ
/// سَطرٍ مِن الحَشو.</para>
///
/// <para><b>وكُلُّ عُضوٍ غَيرِ مُنَفَّذٍ يَرمي</b>: مَسارٌ يَستَعلِم
/// (‏<c>Query&lt;T&gt;</c>) أَو يَفتَح مُعامَلَةً يَحمَرُّ بِاسمِه، ولا
/// يَمُرُّ بِإجابَةٍ افتِراضِيَّةٍ صامِتَة.</para>
/// </summary>
internal class MartenProxy : DispatchProxy
{
    private DocWorld _world = null!;

    public static T For<T>(DocWorld world) where T : class
    {
        var proxy = Create<T, MartenProxy>();
        ((MartenProxy)(object)proxy)._world = world;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var name = method!.Name;
        _world.Members.Add(name);

        switch (name)
        {
            case "LoadAsync":
            {
                var type = method.GetGenericArguments()[0];
                var id   = args?[0]?.ToString() ?? "";
                _world.Docs.TryGetValue(DocWorld.KeyOf(type, id), out var doc);
                return typeof(Task).GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(type).Invoke(null, new[] { doc });
            }

            case "Store":
            case "Insert":
                foreach (var doc in Entities(args))
                {
                    _world.Stored.Add(doc);
                    _world.Put(doc);
                }
                return null;

            case "SaveChangesAsync":
                _world.SaveCalls++;
                return Task.CompletedTask;

            case "get_DocumentStore":  return _world.Store;
            case "get_TenantId":       return "";
            case "QuerySession":
            case "LightweightSession": return _world.Session;
            case "Dispose":            return null;
            case "DisposeAsync":       return ValueTask.CompletedTask;
        }

        throw new NotSupportedException(
            $"جَلسَةُ الاختِبارِ لا تُنَفِّذ «{name}» — المَسارُ المَفحوصُ " +
            "يَعتَمِدُ على شَيءٍ لَم يُنَفَّذ، فَيُقالُ بِصَوتٍ لا يُبتلَع.");
    }

    private static IEnumerable<object> Entities(object?[]? args)
        => args?[0] is System.Collections.IEnumerable many and not string
            ? many.Cast<object>()
            : args?[0] is { } one ? new[] { one } : Array.Empty<object>();
}

