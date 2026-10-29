using ACommerce.Kit.Files;

namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>
/// <para><b>أَسماءُ الجِهاتِ الَّتي تُسَمّيها سِياسَةُ الخُصوصِيَّة — مِن المَوضِعِ الَّذي يَختارُها لا مِن نَصٍّ يُنسَخ</b>
/// (‏2026-10-29، قَرارُ المالِك د20 «أ»: «واسمُ مُزَوِّدِ النَموذَجِ ومُزَوِّدِ الرَسائِلِ يُقرَآنِ مِن الإعدادِ نَفسِه الَّذي
/// يَختارُهُما فَلا تَكذِبُ السِياسَةُ إن بُدِّلا»). ثَلاثُ دَوالَّ نَقِيَّةٍ بِلا I/O، والصَفحَةُ تُمَرِّرُ لَها ما سُجِّلَ فِعلاً:</para>
/// <list type="bullet">
/// <item><see cref="SignInSenders"/> — قَناتا رَمزِ الدُخولِ <b>المُسَجَّلَتان</b> (أَثَرُ <c>AuthChannelSelection.Decide</c> في
/// <c>Program.cs</c>، والقِراءَةُ نَفسُها الَّتي يَقرَؤُها بابُ الاستوديو)، والمُحاكي لا يُسَمّى لِأَنَّه لا يُرسِلُ إلى أَحَد.</item>
/// <item><see cref="LanguageModels"/> — <c>ProviderName</c> خَلفِيَّتَي الوَكيلَين كَما يَبنيهِما <c>AgentBackendProvider</c>.</item>
/// <item><see cref="FileStore"/> — مَخزَنُ الصُوَرِ حينَ يَختارُ <see cref="FileStorageSelection.Decide"/> مَخزَنَ S3.</item>
/// </list>
/// <para><b>والمَعاجِمُ هُنا مُغلَقَة، والمَجهولُ يُسَمّى بِحَرفِه لا يُخفى</b>: مُزَوِّدٌ يُضافُ غَداً بِاسمٍ لا يَعرِفُه الجَدوَلُ
/// يَظهَرُ بِاسمِه الخامِ في السِياسَة — فَالخَطَأُ المُمكِنُ قُبحُ تَسمِيَةٍ لا جِهَةٌ تَستَلِمُ البَياناتِ ولا تُذكَر.</para>
/// <para><b>شَرطُ الاستِخراج</b> (القاعِدَة ١): مُستَهلِكٌ واحِدٌ في وَقتِ التَشغيل (<c>PlatformPrivacy.razor</c>)، واستُخرِجَ لِيُقاسَ
/// بِجَدوَلٍ بِلا مُضيف (<c>PrivacyRecipientsTests</c>) — فَلَو صارَ لِلقَرارِ مَوضِعٌ ثانٍ يَقرَؤُه بَقِيَ هُنا، ولَو سَقَطَ الاختِبارُ
/// عادَ جِسماً في الصَفحَة.</para>
/// </summary>
public static class PrivacyRecipients
{
    /// <summary>اسمُ قَناةِ SMTP كَما تُعلِنُه (<c>SmtpEmailChannel.ChannelName</c>) — يُسَمّى بِمُضيفِه المَضبوطِ لا بِالبُروتوكول.</summary>
    public const string SmtpChannelName = "Smtp";

    /// <summary>مِفتاحُ مُضيفِ SMTP — القِسمُ نَفسُه الَّذي يَربِطُه <c>Program.cs</c> بِخِياراتِ القَناة.</summary>
    public const string SmtpHostKey = "Auth:Email:Host";

    /// <summary>مَن يَستَلِمُ رَقمَ الهاتِفِ أَو البَريدَ مَعَ رَمزِ الدُخول: كُلُّ قَناةٍ مُسَجَّلَةٍ لَيسَت مُحاكِياً، بِاسمِها؛ وSMTP بِمُضيفِه.</summary>
    public static IReadOnlyList<string> SignInSenders(
        IEnumerable<(string ChannelName, bool IsDevelopmentStub)> registered, string? smtpHost)
    {
        var names = new List<string>();
        foreach (var (channel, stub) in registered)
        {
            if (stub || string.IsNullOrWhiteSpace(channel)) continue;
            var name = string.Equals(channel, SmtpChannelName, StringComparison.OrdinalIgnoreCase)
                ? (string.IsNullOrWhiteSpace(smtpHost) ? "SMTP" : smtpHost.Trim())
                : channel.Trim();
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
        }
        return names;
    }

    /// <summary>أَسماءُ مُزَوِّدي النَماذِجِ المَعروفَة — <c>ProviderName</c> كَما تُعلِنُه الخَلفِيّاتُ الثَلاث (‏<c>AgentBackends.cs</c>،
    /// وخَلفِيَّةُ OpenAI تَستَنتِجُه مِن العُنوان).</summary>
    public static readonly IReadOnlyDictionary<string, string> LanguageModelNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["anthropic"]  = "Anthropic",
            ["gemini"]     = "Google Gemini",
            ["openai"]     = "OpenAI",
            ["groq"]       = "Groq",
            ["cerebras"]   = "Cerebras",
            ["openrouter"] = "OpenRouter",
            ["ollama"]     = "Ollama",
        };

    /// <summary>مَن يَستَلِمُ نَصَّ الفِكرَةِ ومُحادَثَةَ الوَكيل: مُزَوِّدُ كُلِّ وَكيلٍ بِلا تَكرار، والمَجهولُ بِحَرفِه.</summary>
    public static IReadOnlyList<string> LanguageModels(IEnumerable<string> providerNames)
    {
        var names = new List<string>();
        foreach (var p in providerNames)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var name = LanguageModelNames.TryGetValue(p.Trim(), out var known) ? known : p.Trim();
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
        }
        return names;
    }

    /// <summary>ذُيولُ مُضيفاتِ مَخازِنِ S3 المَعروفَة — والمَقصودُ اليَومَ Cloudflare R2 (‏<c>S3FileStorage</c>، <c>docs/DEPLOY.md</c> §٢·ز).</summary>
    public static readonly IReadOnlyList<(string HostSuffix, string Name)> FileStoreNames =
    [
        (".r2.cloudflarestorage.com", "Cloudflare R2"),
        (".amazonaws.com",            "Amazon S3"),
        (".backblazeb2.com",          "Backblaze B2"),
        (".wasabisys.com",            "Wasabi"),
    ];

    /// <summary>مَخزَنُ الصُوَرِ المَرفوعَة حينَ يَكونُ مَخزَنَ S3 — <c>null</c> حينَ تُخَزَّنُ عَلى المُضيفِ نَفسِه أَو لا تُخَزَّن.</summary>
    public static string? FileStore(S3StorageSettings settings)
    {
        if (FileStorageSelection.Decide(isDevelopment: false, settings) != FileStorageChoice.S3) return null;
        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var uri)) return settings.Endpoint!.Trim();
        var host = uri.Host.ToLowerInvariant();
        foreach (var (suffix, name) in FileStoreNames)
            if (host.EndsWith(suffix, StringComparison.Ordinal)) return name;
        return host;
    }

    /// <summary>يَجمَعُ الأَسماءَ بِصيغَتَي القامُوس: <paramref name="pair"/> لِلأَخيرَين (‏«{0} و{1}») و<paramref name="comma"/> لِما قَبلَهُما.</summary>
    public static string Join(IReadOnlyList<string> names, string pair, string comma)
    {
        if (names.Count == 0) return "";
        var joined = names[0];
        for (var i = 1; i < names.Count; i++)
            joined = string.Format(i == names.Count - 1 ? pair : comma, joined, names[i]);
        return joined;
    }
}
