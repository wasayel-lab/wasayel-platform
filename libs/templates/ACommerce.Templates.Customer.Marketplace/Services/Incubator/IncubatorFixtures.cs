namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// ثَوابِت اختِبار (المَرحَلَة ٩) — ٢٠ فِكرَة مَشروع مُتَنَوِّعَة لِفَحص
/// أَنّ خَطّ التَّحليل كامِل يَعمَل عَلى أَنواع مُختَلِفَة مِن الأَفكار.
/// يُشَغَّل مِن لوحَة admin (/admin/monitor) عَبر زِرّ "اِفحَص الـ pipeline".
/// </summary>
public sealed record FeasibilityFixture(
    string Description,
    string ExpectedSector,
    string ExpectedPattern,
    int MinQuality);

public static class IncubatorFixtures
{
    public static readonly IReadOnlyList<FeasibilityFixture> All = new[]
    {
        // ─── e-commerce (5) ──────────────────────────────────
        new FeasibilityFixture("مَتجَر مَلابِس نِسائيَّة سُعوديَّة عَصرِيَّة بِتَوصيل سَريع", "ecommerce", "marketplace", 70),
        new FeasibilityFixture("مَنصَّة بَيع كُتُب عَرَبيَّة مُستَعمَلَة بَين الأَفراد", "ecommerce", "classifieds", 60),
        new FeasibilityFixture("مَتجَر إلِكترونيّ لِمُنتَجات العِنايَة الطَّبيعيَّة", "ecommerce", "marketplace", 70),
        new FeasibilityFixture("مَنصَّة لِشِراء قِطَع غِيار السَيّارات الأَصلِيَّة بَين التُجّار والوُرَش", "ecommerce", "marketplace", 65),
        new FeasibilityFixture("مَتجَر إكسسوارات هَدايا مُخَصَّصَة بِأَسماء العُملاء", "ecommerce", "marketplace", 65),

        // ─── marketplaces (5) ────────────────────────────────
        new FeasibilityFixture("مَنصَّة تَربِط الحِرَفِيين بِأَصحاب المَنازِل لِلتَجديدات", "services", "classifieds", 65),
        new FeasibilityFixture("سوق إعلانات لِبَيع وشِراء العَقار في الرياض بَين الأَفراد", "realestate", "classifieds", 70),
        new FeasibilityFixture("مَنصَّة تَربِط الفنّانين بِمُنَظِّمي الفَعالِيّات لِلحَفَلات", "services", "classifieds", 60),
        new FeasibilityFixture("سوق قِطَع غِيار الأَجهِزَة الكَهرَبائيَّة المُستَعمَلَة", "ecommerce", "classifieds", 60),
        new FeasibilityFixture("مَنصَّة تَجمَع المُدَرِّبين الشَخصِيين بِالعُملاء", "services", "classifieds", 65),

        // ─── services (5) ────────────────────────────────────
        new FeasibilityFixture("تَطبيق خَدَمات منزليَّة فَوريَّة (سِباكَة، كَهرُباء، تَنظيف)", "services", "ondemand", 70),
        new FeasibilityFixture("مَنصَّة حَجز جَلَسات استِشارَة قانونيَّة عَن بُعد", "services", "marketplace", 65),
        new FeasibilityFixture("تَطبيق حَجز خَدَمات صالونات وسپا لِلسَيّدات", "services", "marketplace", 70),
        new FeasibilityFixture("خَدَمَة تَوصيل غاز المَنازِل عِندَ الطَّلَب", "services", "ondemand", 65),
        new FeasibilityFixture("تَطبيق تَأجير سَيّارات بَين الأَفراد", "transport", "rental", 70),

        // ─── edge cases (5) ──────────────────────────────────
        new FeasibilityFixture("مَنصَّة تَبادُل كُتُب بَين القُرّاء بِنِظام نُقاط (لا بَيع)", "other", "custom", 50),
        new FeasibilityFixture("شَبَكَة اجتِماعيَّة لِلمُهتَمّين بِالاستِثمار في العُملات الرَّقمِيَّة", "other", "custom", 50),
        new FeasibilityFixture("مَنصَّة تَعَلُّم تِقَنيّ بِنِظام اشتِراك شَهريّ", "ecommerce", "marketplace", 60),
        new FeasibilityFixture("سوق إعلانات لِتَأجير الشُقَق المَفروشَة في مَكَّة لِلحُجّاج", "realestate", "rental", 70),
        new FeasibilityFixture("تَطبيق تَوصيل وَجَبات نَباتيَّة صِحِّيَّة بِاشتِراك أُسبوعيّ", "fnb", "marketplace", 65),
    };
}
