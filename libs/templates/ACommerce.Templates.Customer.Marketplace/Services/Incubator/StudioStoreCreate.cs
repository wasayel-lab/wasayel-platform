using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace ACommerce.Templates.Customer.Marketplace.Services.Incubator;

/// <summary>
/// <para><b>إنشاءُ مَتجَرٍ بِاليَدِ مِن الاستوديو — مُعجَمُ البابِ ومَساراتُه</b> (‏2026-11-07، الموجَة ١٣ W13-1·٣ — ADR-049 §٤).</para>
///
/// <para><b>العِلَّةُ المَقيسَة</b>: كانَ البابُ الوَحيدُ الَّذي يَملِكُ مِنه رائِدُ الأَعمالِ مَتجَراً <c>/studio/s/{id}/build</c>، يَشتَرِطُ جَلسَةَ تَحليلٍ
/// <c>Completed</c> لا يَكتُبُها إلّا نِداءُ نَموذَجٍ ناجِح — فَبِلا مِفتاحٍ لا مَتجَر. والتَحليلُ لا يُعطي المَتجَرَ إلّا تِلكَ البَوّابَةَ ومَلءَ سَطرِ الوَصف.
/// فَصارَ <see cref="Page"/> استِمارَةَ الإنشاءِ و<see cref="Endpoint"/> بابَها، بِالمَصنَعِ نَفسِه (<see cref="TenantFromAnalysisFactory.CreateAsync"/>)
/// ونَمَطٍ وقِطاعٍ فارِغَين — فَالمَتجَرُ هُوَ ما كانَ <c>/build</c> يَكتُبُه لِجَلسَةٍ بِالوَصفِ وَحدَه بايتاً، عَدا المُعَرِّفِ والوَقتِ والمَصدَرِ
/// (‏<c>StudioStoreBirthCharacterizationTests.GoldenSha256</c>). ولا مُنتَقيَ أَنماط: المُنتَجُ المَتجَرُ مُتَعَدِّدُ البائِعين (‏ADR-032).</para>
///
/// <para><b>والحارِسُ في أَوَّلِ أَسطُرِ البابِ بِتَرتيبِه</b> (القاعِدَة ٦): الهُوِيَّة ← المُوافَقَة ← حِصَّةُ المَتاجِر ← المُعَرِّف ← الاسم ← اللَون
/// ← قَناةُ الدُخول — ولا يَقرَأُ حَقلاً قَبلَ التَخويل. ويُفحَصُ بِالمَصدَرِ في <c>StudioStoreCreateTests</c>.</para>
/// </summary>
public static class StudioStoreCreate
{
    /// <summary>صَفحَةُ الاستِمارَة — المَسارُ نَفسُه الَّذي كانَت تَرسُمُه صَفحَةُ الفِكرَة، فَتَبقى روابِطُها وسَطرُ خَطِّ المَساراتِ الأَساسِيّ.</summary>
    public const string Page = "/studio/new";

    /// <summary>بابُ الكِتابَة.</summary>
    public const string Endpoint = "/studio/apps/create";

    /// <summary>صَفحَةُ المُوافَقَةِ بِوِجهَةِ عَودَةٍ إلى الاستِمارَة — يُرسَلُ إلَيها مَن لَم يَقبَل الإصدارَ القائِم، مِن الصَفحَةِ ومِن البابِ مَعاً.</summary>
    public const string ConsentPage = "/studio/consent?returnUrl=/studio/new";

    public const string NameRequired = "name_required";
    public const string ColorInvalid = "color_invalid";

    /// <summary><b>حَدّا الطولِ في الخادِم</b> (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·SEC-C1-3 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): الاستِمارَةُ تَقولُهُما
    /// (`maxlength` ‏60 و120) والخادِمُ لا يَفرِضُهُما — فَاسمٌ بِمِئَتَي أَلفِ حَرفٍ وُلِدَ بِه مَتجَرٌ صَفحَتُه العامَّةُ عِشرونَ مِليونَ بايت، ورَفضٌ يَنسَخُه في
    /// العُنوانِ ضَيَّعَ `Location`. والرَقَمانِ رَقَما الاستِمارَةِ لا جَديد (القاعِدَة ١٦).</summary>
    public const int NameMax = 60, TagLineMax = 120;
    public const string NameTooLong = "name_too_long";
    public const string TagLineTooLong = "tagline_too_long";

    /// <summary><b>وحَدُّ المَدينَة، وأَبوابُ الهُوِيَّةِ الأُخرى</b> (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 الجَولَةُ الثانِيَة · W13-1·SEC2-C1-2 — يُلحَقُ ولا يُعادُ
    /// حَرفٌ فَوقَه): الحَدّانِ أَعلاه كانا في بابِ الإنشاءِ وَحدَه، وحِفظُ الهُوِيَّةِ (`BrandingSaveService`) وبابُ المُشرِفِ وأَداتا الوَكيلِ (`create_tenant`،
    /// `set_branding`) بِلا حَدّ — فَاسمٌ بِمِئَتَي أَلفِ حَرفٍ يُحفَظُ مِن «الهُوِيَّة» بَعدَ إنشاءٍ قَصير (صَفحَةُ المَتجَرِ ‏15,049,017 بايتاً، والصَفحَةُ الأُولى
    /// ‏1,441,598 لِكُلِّ زائِرٍ بَعدَ إعلانٍ واحِد). والآنَ يَسأَلُها كُلُّها بِهذِه الأَرقامِ نَفسِها؛ و<see cref="CityMax"/> رَقمُ حَقلِ المَدينَةِ في استِمارَتَي
    /// الهُوِيَّةِ واستِمارَةِ المُشرِف (<c>maxlength="40"</c>) لا رَقَمٌ جَديد (القاعِدَة ١٦) — والمَدينَةُ تُرسَمُ في واجِهَةِ الصَفحَةِ الأُولى كَالاسم.</summary>
    public const int CityMax = 40;
    public const string CityTooLong = "city_too_long";

    /// <summary>رَمزُ أَوَّلِ حَقلِ هُوِيَّةٍ فاتَ حَدَّه — الاسمُ ثُمَّ السَطرُ ثُمَّ المَدينَة — أَو <c>null</c>؛ يَسأَلُه بابُ المُشرِفِ وأَداتا الوَكيل (‏W13-1·SEC2-C1-2).</summary>
    public static string? LengthRefusal(string name, string tagLine, string city)
        => name.Length > NameMax ? NameTooLong : tagLine.Length > TagLineMax ? TagLineTooLong : city.Length > CityMax ? CityTooLong : null;

    /// <summary>مِفتاحُ قامُوسِ رَفضِ الطولِ عَلى كُلِّ سَطحِ هُوِيَّة: الاسمُ والسَطرُ بِنَصِّ استِمارَةِ الإنشاءِ لِلرَمزِ نَفسِه — مَعناهُ واحِد — والمَدينَةُ بِمِفتاحِها.</summary>
    public static string LengthMessageKey(string code) => code == CityTooLong ? "studio.app_branding.err_city_too_long" : MessageKey(code);

    /// <summary>لَونُ <c>marketplace</c> — ما كانَت استِمارَةُ البِناءِ تُعَبِّئُه سَلَفاً لِجَلسَةٍ بِالوَصفِ وَحدَه.</summary>
    public const string DefaultColor = "#2563eb";

    /// <summary><b>رُموزُ الرَفضِ الَّتي تَرسُمُها الاستِمارَة — مُعجَمٌ مُغلَق</b>، كُلٌّ بِمِفتاحِ قامُوسٍ (<see cref="MessageKey"/>). ورَفضُ الحِصَّةِ
    /// لَيسَ مِنها: رَمزُ بَوّابَتِه القائِمُ (<c>?upgrade=…</c>) تَرسُمُه نافِذَةُ التَرقِيَة.</summary>
    public static readonly IReadOnlyList<string> Refusals =
    [
        TenantFromAnalysisFactory.SlugRequired,
        TenantFromAnalysisFactory.SlugFormat,
        TenantFromAnalysisFactory.SlugReserved,
        TenantFromAnalysisFactory.SlugTaken,
        NameRequired,
        ColorInvalid,
        TenantAuthChannelDoor.NoChannel,
        NameTooLong,
        TagLineTooLong,
    ];

    public static string MessageKey(string code) => "studio.create.err_" + code;

    /// <summary><b>وِجهَةُ ما بَعدَ الدُخول</b> (‏W13-1·٤ — ADR-049 §٨): حِسابُ استوديو لا يَملِكُ مَتجَراً يَبلُغُ استِمارَةَ الإنشاءِ مُباشَرَةً، فَدَعوَةُ
    /// الصَفحَةِ الأُولى «أَنشِئ مَتجَرَك» دُخولٌ ثُمَّ الاستِمارَة؛ ومَن يَملِكُ مَتجَراً يَبلُغُ لَوحَتَه كَما كان.</summary>
    public static string AfterSignIn(int ownedStores) => ownedStores == 0 ? Page : "/studio";

    /// <summary>حُقولُ الاستِمارَةِ كَما قُرِئَت — المُعَرِّفُ بِحُروفٍ صَغيرَة كَما كانَ <c>/build</c> يَقرَؤُه.</summary>
    public sealed record Form(string Slug, string Name, string Color, string TagLine);

    public static Form Read(IFormCollection f) => new(
        f["slug"].ToString().Trim().ToLowerInvariant(),
        f["name"].ToString().Trim(),
        f["color"].ToString().Trim(),
        f["tagline"].ToString().Trim());

    public static bool ColorValid(string color) => Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$");

    /// <summary>‏(‏2026-11-07، W13-1·SEC-C1-3) رَمزُ أَوَّلِ حَقلٍ فاتَ حَدَّه — الاسمُ ثُمَّ السَطر — أَو <c>null</c>.</summary>
    public static string? LengthRefusal(Form form)
        => form.Name.Length > NameMax ? NameTooLong : form.TagLine.Length > TagLineMax ? TagLineTooLong : null;

    /// <summary>العَودَةُ إلى الاستِمارَةِ بِرَمزِ الرَفضِ وبِما كُتِبَ فيها — فَلا يُعيدُ صاحِبُها كِتابَةَ حُقولٍ صَحيحَةٍ لِأَجلِ حَقلٍ واحِد.</summary>
    /// <remarks>‏(‏2026-11-07، W13-1·SEC-C1-3) ويَنسَخُ ما كانَ في حَدِّه وَحدَه — حَقلٌ فَوقَ حَدِّه يَعودُ فارِغاً، فَيَبقى العُنوانُ مَحدوداً و`Location` حاضِراً.</remarks>
    public static string Refused(string code, Form form)
        => $"{Page}?err={Uri.EscapeDataString(code)}&slug={Uri.EscapeDataString(Within(form.Slug, SlugMax))}&name={Uri.EscapeDataString(Within(form.Name, NameMax))}"
         + $"&color={Uri.EscapeDataString(Within(form.Color, ColorMax))}&tagline={Uri.EscapeDataString(Within(form.TagLine, TagLineMax))}";

    private const int SlugMax = 63, ColorMax = 7;   // مِلصَقُ DNS (‏SlugRefusal) و`#RRGGBB` (‏ColorValid)
    private static string Within(string value, int max) => value.Length <= max ? value : "";

    /// <summary>
    /// <para><b>أَهذا الطَلَبُ استِمارَةُ صاحِبِ المَتجَرِ نَفسُها، أُرسِلَت ثانِيَةً بَعدَ أَن وَلَدَته؟</b> (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 الجَولَةُ الثانِيَة ·
    /// W13-1·SEC2-C1-1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) — المَتجَرُ القائِمُ مالِكُه صاحِبُ الطَلَبِ، واسمُه ولَونُه وسَطرُه ما في الاستِمارَةِ حَرفاً.</para>
    ///
    /// <para><b>العِلَّةُ المَقيسَة</b> (تَقريرُ العَدَسَة، Chromium بِوَكيلٍ يُؤَخِّرُ الجَوابَ 1.2 ثانِيَة): نَقرَتانِ عَلى «أَنشِئ المَتجَر» بَينَهُما 150ms ثُمَّ 400ms
    /// — يَصِلُ الطَلَبُ الثاني بَعدَ أَن حُفِظَ الأَوَّلُ فَيُرَدُّ <c>slug_taken</c>، والمُتَصَفِّحُ يُبقي الجَوابَ الأَخير: «هذا المُعَرِّف مُستَخدَم مِن قَبل.» عَن مَتجَرِه
    /// هُوَ؛ فَإن أَعادَ بِمُعَرِّفٍ آخَرَ أَنفَقَ مَتجَراً ثانِياً مِن عَدّادٍ مَدى الحَياة. <b>والآن يُجابُ بِمَتجَرِه</b> (<see cref="StorePage"/>) ولا يُعَدُّ لَه شَيء.</para>
    ///
    /// <para><b>ولِماذا الاستِمارَةُ حَرفاً لا المالِكُ وَحدَه</b>: صاحِبٌ يَكتُبُ مُعَرِّفاً يَملِكُه بِاسمٍ أَو لَونٍ أَو سَطرٍ آخَرَ يُريدُ مَتجَراً جَديداً اختارَ لَه
    /// مُعَرِّفاً مُستَعمَلاً — و«مُستَخدَم مِن قَبل» تَصدُقُ فيه؛ وإدخالُه مَتجَرَه القَديمَ يَبتَلِعُ ما كَتَبَ بِلا كَلِمَة. وهُوَ ما ثَبَّتَه التَوصيفُ قَبلَ هذا
    /// التَبديلِ ولا يُمَسّ (<c>LiveStoreBirthOnceCharacterizationTests</c>، <c>LiveStoreRepeatCharacterizationTests</c>). والتَكرارُ مَعرِفَةٌ بِما كُتِبَ لا
    /// بِزَمَنِه: لا نافِذَةَ ثَوانٍ تُختَرَعُ (القاعِدَة ١٦)، والاستِمارَةُ نَفسُها بَعدَ يَومٍ تُدخِلُ صاحِبَها مَتجَرَه كَذلك — وهُوَ ما وَصَفَتهُ حَرفاً.</para>
    /// </summary>
    public static bool Repeats(ACommerce.Kit.Tenants.Tenant? held, Guid ownerId, Form form)
        => held is not null && ownerId != Guid.Empty && held.OwnerUserId == ownerId
           && held.Name == form.Name && held.BrandColor == form.Color && held.TagLine == form.TagLine;

    /// <summary>لَوحَةُ المَتجَرِ بِلا <c>?built=1</c> — جَوابُ التَكرار: المَتجَرُ وُلِدَ في طَلَبٍ سَبَقَه، وهذا الطَلَبُ لَم يَبنِ شَيئاً.</summary>
    public static string StorePage(string slug) => $"/studio/apps/{slug}";
}
