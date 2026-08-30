namespace ACommerce.Templates.Customer.Marketplace.Services.Ux;

/// <summary>
/// <para><b>«أَيُّ manifest تَحمِلُ هذِه الصَفحَة، إن حَمَلَت؟»</b> —
/// دالَّةٌ نَقِيَّةٌ نُقِلَت مِن <c>App.razor</c> <b>بِلا تَغييرِ حَرفٍ
/// في جَوابِها</b>.</para>
///
/// <para><b>ولِماذا تَخرُج مِن الـrazor أَصلاً</b>: أَثَرُ هذِه
/// القاعِدَةِ لا يُرى إلّا بِفَتحِ الصَفحَةِ عَلى جِهاز، وقاعِدَةٌ
/// تُصَحَّح بِالعَينِ تُصَحَّح مَرَّتَين. فَإخراجُها إلى دالَّةٍ يَجعَل
/// الجَدوَلَ كُلَّه مَقيساً بِلا مُتَصَفِّح، ويُبقي أَيَّ تَعديلٍ
/// لاحِقٍ <b>مَحصوراً حَيثُ قُصِد بِبُرهانٍ لا بِدَعوى</b>
/// (القاعِدَة ١٣).</para>
///
/// <para><b>والقاعِدَة</b>: صَفحَةٌ عابِرَة لا تُثَبَّت؛
/// ومَسارٌ فيه <c>/r/{role}/</c> يَحمِل manifest الدَور؛ ومَسارٌ بِلا
/// دَورٍ ولَيسَ بَوّابَةً يَحمِل manifest السلاج؛ والبَوّابَةُ
/// <c>/{slug}</c> تَحمِلُه <b>إن لَم يَكُن لِلمَتجَرِ أَدوار</b>،
/// وإلّا فَهي مُنتَقٍ لِلدَورِ فَلا تُثَبَّت.</para>
/// </summary>
public static class PwaManifestDecision
{
    /// <summary>مَسارات عابِرَة: لا تُمَثِّل التَطبيقَ نَفسَه، فَلا
    /// يُثَبَّت مِنها. مَنقولَةٌ حَرفاً.</summary>
    private static readonly string[] TransientMarkers =
        { "/login", "/verify", "/terms", "/logout", "/auth/" };

    public static bool IsTransient(string path) =>
        TransientMarkers.Any(m => path.Contains(m, StringComparison.Ordinal))
        || path.StartsWith("/admin", StringComparison.Ordinal);

    /// <summary>المَسارُ الَّذي طولُه مَقطَعٌ واحِد — <c>/{slug}</c>.</summary>
    public static bool IsLauncher(string path) =>
        path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Length == 1;

    /// <summary>جَذرُ الـmanifest (<c>/api/{slug}</c> أَو
    /// <c>/api/{slug}/r/{role}</c>)، أَو <c>null</c> لِصَفحَةٍ لا
    /// تُثَبَّت.</summary>
    public static string? Resolve(
        string path, bool tenantResolved, string tenantSlug,
        string? roleFromPath, bool tenantHasRoles)
    {
        if (!tenantResolved || IsTransient(path)) return null;

        if (!string.IsNullOrEmpty(roleFromPath))
            return $"/api/{tenantSlug}/r/{roleFromPath}";

        // **السَطرُ الوَحيدُ الَّذي تَغَيَّر**: كانَ
        // `if (IsLauncher(path)) return null;` — فَبَوّابَةُ كُلّ مَتجَرٍ
        // تَخرُج بِصِفرِ وَسمِ تَثبيت، بِأَدوارٍ أَو بِلا. والصَحيحُ أَنّ
        // بَوّابَةَ مَتجَرِ الأَدوارِ **مُنتَقٍ لِلدَور** فَلا تُمَثِّل
        // التَطبيق، وبَوّابَةَ مَتجَرٍ بِلا أَدوارٍ **هي التَطبيق** —
        // وهي بِعَينِها الصَفحَةُ الَّتي يُشارِكُها صاحِبُ المَتجَر.
        if (IsLauncher(path) && tenantHasRoles) return null;

        return $"/api/{tenantSlug}";
    }
}
