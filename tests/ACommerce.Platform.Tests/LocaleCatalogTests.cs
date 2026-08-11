using ACommerce.Templates.Customer.Marketplace.I18n;
using Xunit;

namespace ACommerce.Platform.Tests;

// ─── قامُوس النُصوص — تَوصيفٌ يُثَبِّت ما كانَ ────────────────────────
//
// الثَلاثَةَ عَشَرَ مِفتاحاً كانَت مَكتوبَةً في <c>L.cs</c> قامُوسَين
// في الكود، وصارَت مِلَفَّي JSON. النَقل آليّ لا يَدَويّ (‏سكربت
// استَخرَجَ القيَم مِن المِلَفّ نَفسِه)، لكِنّ **الآلِيَّة لَيسَت
// بُرهاناً** — والجَدوَل أَدناه هو البُرهان: قيمَةٌ تَفقِد شَكلَةً
// واحِدَة تُحمِر هُنا.
//
// وأَسماء المَفاتيح وَحدَها تَغَيَّرَت — إلى <c>domain.feature.label</c>
// بِثَلاثَة مَقاطِع. وذلك آمِن **بِالقِياس** لا بِالظَنّ: مُؤَشِّر
// <c>L["…"]</c> لَه صِفر مَوضِع استِدعاء في المُستَودَع كُلِّه، فَلا
// مُستَهلِك لِلاسم القَديم. ولَولا ذلِك القِياس لَما جازَت إعادَة
// التَسمِيَة.

public class LocaleCatalogTests
{
    /// <summary>القيَم كَما كانَت في <c>L.cs</c> — لا يُعاد تَوليدُها،
    /// تُقارَن بِها.</summary>
    public static readonly (string Key, string Ar, string En)[] Pinned =
    {
        ("shell.nav.home", "الرَئيسيّة", "Home"),
        ("shell.nav.explore", "استِكشاف", "Explore"),
        ("shell.nav.chats", "رَسائل", "Messages"),
        ("shell.nav.notifs", "إشعارات", "Alerts"),
        ("shell.nav.account", "حِسابي", "Me"),
        ("shell.nav.login", "دُخول", "Login"),
        ("common.state.loading", "جارٍ التَحميل…", "Loading…"),
        ("common.state.empty", "لا توجَد بَيانات بَعد.", "Nothing here yet."),
        ("common.action.back", "رُجوع", "Back"),
        ("auth.action.login", "تَسجيل دُخول", "Sign in"),
        ("auth.action.logout", "تَسجيل خُروج", "Sign out"),
        ("listings.detail.contact", "تَواصُل مَع المُعلِن", "Contact seller"),
        ("listings.detail.views", "مَشاهَدات", "views"),
    };

    public static TheoryData<string, string, string> PinnedRows()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (k, ar, en) in Pinned) data.Add(k, ar, en);
        return data;
    }

    // ─── التَوصيف ────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(PinnedRows))]
    public void MigratedKeys_KeepTheirExactText(string key, string ar, string en)
    {
        Assert.Equal(ar, LocaleCatalog.Find("ar", key));
        Assert.Equal(en, LocaleCatalog.Find("en", key));
    }

    [Fact]
    public void Lexicon_IsExactlyTheArabicKeys()
    {
        Assert.Equal(
            Pinned.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            LocaleCatalog.Lexicon.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    // ─── الآلِيَّة ────────────────────────────────────────────────────

    /// <summary>العَرَبِيَّة أَوَّلاً في القائِمَة — لَيسَ تَرتيباً
    /// تَجميلِيّاً: هي لُغَة السُقوط، فَمَوضِعُها يُقرَأ.</summary>
    [Fact]
    public void Languages_StartWithArabic()
    {
        Assert.Equal("ar", LocaleCatalog.Languages[0]);
        Assert.Contains("en", LocaleCatalog.Languages);
        Assert.True(LocaleCatalog.Has("ar"));
        Assert.False(LocaleCatalog.Has("fr"));
    }

    /// <summary>لُغَةٌ لا مِلَفَّ لَها تَسقُط إلى العَرَبِيَّة — لا إلى
    /// المِفتاح الخام. وهذا هو الفَرق بَين «غَير مُتَرجَم» و«مَعطوب».</summary>
    [Fact]
    public void UnknownLanguage_FallsBackToArabic()
    {
        var key = Pinned[0].Key;
        Assert.Equal(Pinned[0].Ar, LocaleCatalog.Text("fr", key));
    }

    /// <summary>ومِفتاحٌ خارِج المَعجَم يَعود خاماً — مَسار لا يَقَع في
    /// بِناءٍ سَليم (يَمنَعُه المُصادِق)، ويَبقى لِأَنّ صَفحَةً
    /// بِمِفتاح ظاهِر أَهوَن مِن صَفحَة بِاستِثناء.</summary>
    [Fact]
    public void UnknownKey_ReturnsTheKeyItself()
    {
        Assert.Equal("no.such.key", LocaleCatalog.Text("ar", "no.such.key"));
    }

    /// <summary>القارِئ يَحفَظ التَكرار، والطَيّ يَأخُذ الأَخيرَة —
    /// وعَلى هذا الفَصل تَقوم بَوّابَة <c>key_duplicate</c>.</summary>
    [Fact]
    public void Reader_KeepsDuplicatesRaw()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            "{ \"a.b.c\": \"واحِد\", \"a.b.c\": \"اثنان\" }");
        var entries = LocaleCatalog.ReadEntries(bytes);
        Assert.Equal(2, entries.Count);
        Assert.Equal("a.b.c", entries[0].Key);
        Assert.Equal("a.b.c", entries[1].Key);
    }
}
