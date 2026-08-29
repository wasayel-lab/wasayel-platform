using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ACommerce.Kit.Payments.Providers.Paddle;

/// <summary>
/// <para><b>البابُ الَّذي تَراهُ الشاشاتُ والنُقاط — ويُجيبُ «لا» بِلا
/// انفِجار.</b></para>
///
/// <para><b>ولِماذا غِلافٌ لا حَقنُ المُزَوِّدِ مُباشَرَةً</b>:
/// المُزَوِّدُ <b>لا يُسَجَّل إطلاقاً</b> بِلا مِفتاحٍ مَضبوط —
/// وتَسجيلُه بِلا مِفتاحٍ يَعني صِنفاً يَرمي عِندَ أَوَّلِ حَلٍّ لَه،
/// وصَفحَةٌ تَطلُب خِدمَةً غَيرَ مُسَجَّلَةٍ تَنفَجِر عِندَ
/// التَصيير. فَهذا الغِلافُ مُسَجَّلٌ <b>دائِماً</b> ويُجيبُ
/// <see cref="CanSell"/> بِـ<c>false</c> — <b>فَتُخفي الشاشَةُ
/// البِطاقَةَ بَدَلَ أَن تَعرِضَ زِرّاً يَقول «قَريباً»</b>
/// (القاعِدَة ١٢). نَفسُ نَمَطِ <c>PayPalGateway</c> حَرفاً.</para>
/// </summary>
public sealed class PaddleGateway
{
    private readonly PaddleOptions _opts;
    private readonly PaddlePaymentProvider? _provider;

    public PaddleGateway(PaddleOptions opts, PaddlePaymentProvider? provider)
    {
        _opts = opts;
        _provider = provider;
    }

    /// <summary>أَيُمكِن نِداءُ الواجِهَة؟ (مِفتاحٌ + بيئَةٌ
    /// مَعروفَة.)</summary>
    public bool IsConfigured => _provider is not null && PaddleEnvironment.IsConfigured(_opts);

    /// <summary>أَتُقبَلُ رِسالَةُ Webhook؟ يَزيدُ سِرَّ
    /// الوِجهَة.</summary>
    public bool CanVerifyWebhooks => PaddleEnvironment.CanVerifyWebhooks(_opts);

    /// <summary><b>أَنَبيعُ فِعلاً؟</b> — وهذا وَحدَه ما يَرسُم
    /// البِطاقَةَ في <c>/admin</c> ويَفتَح الزِرَّ في
    /// الاستوديو.</summary>
    public bool CanSell => _provider is not null && PaddleEnvironment.CanSell(_opts);

    /// <summary>الخِيارات — لِتُمَرَّرَ إلى
    /// <see cref="PaddleWebhookGuard.Gate"/>. <b>ولا سِرَّ يُقرَأُ
    /// مِنها في شاشَة</b>: البابُ يُسأَل <see cref="CanSell"/>،
    /// والخِياراتُ تُمَرَّر إلى دالَّةِ القَرارِ وَحدَها.</summary>
    public PaddleOptions Options => _opts;

    /// <summary>رَمزُ العَميلِ لِصَفحَةِ الدَفعِ الساكِنَة —
    /// <b>عَلَنيٌّ بِالتَصميم</b>، ويُقرَأُ مِن نُقطَةٍ عامَّةٍ
    /// يَفتَحُها المُتَصَفِّح.</summary>
    public string ClientToken => _opts.ClientToken ?? "";

    /// <summary>البيئَةُ كَما ضُبِطَت — تَقرَؤُها صَفحَةُ الدَفعِ
    /// لِتُبَدِّلَ <c>Paddle.Environment</c>، وبِلا ذلك تُنادي
    /// نُسخَةُ الاختِبارِ مُضيفَ الإنتاج.</summary>
    public string Environment => (_opts.Environment ?? "").Trim().ToLowerInvariant();

    /// <summary>يُنشِئ مُعامَلَةً — و«لا» بِلا انفِجارٍ حينَ لا
    /// مُزَوِّدَ مُسَجَّلاً.</summary>
    public Task<PaddleTransactionResult> CreateTransactionAsync(
        PaddleTransactionDraft draft, string reference, CancellationToken ct = default)
        => _provider is null
            ? Task.FromResult(new PaddleTransactionResult(
                "", "", null, "Paddle غَير مُهَيَّأ في هذِه النُسخَة."))
            : _provider.CreateTransactionAsync(draft, reference, ct);
}

public static class PaddleExtensions
{
    /// <summary>
    /// <para><b>يُسَجِّل Paddle — أَو لا يُسَجِّلُه، والقَرارُ
    /// بِالتَهيئَة.</b> نَفسُ نَمَطِ <c>AddPayPalSubscriptions</c>:
    /// مِفتاحٌ مَضبوطٌ ⇒ مُزَوِّدٌ حَقيقيّ؛ وغِيابُه ⇒ <b>لا
    /// تَسجيل</b>، والغِلافُ يَقول «لا» بِلا انفِجار.</para>
    ///
    /// <para><b>وقيمَةُ بيئَةٍ خارِجَ المَعجَمِ تُفشِلُ الإقلاعَ
    /// هُنا — ويُقالُ لِماذا لا تُتَجاهَل</b>: الفَراغُ يَعني «لا
    /// Paddle»، وهُوَ حالُ كُلِّ نُسخَةِ تَطويرٍ فَلا يَرمي. أَمّا
    /// <c>Payments__Paddle__Environment=sanbdox</c> — خَطَأُ إملاءٍ في
    /// مُتَغَيِّرِ الـSpace — فَـ<b>نِيَّةٌ مُعلَنَةٌ لَم تَتَحَقَّق</b>:
    /// تَجاهُلُها يُقلِعُ الخادِمَ ويُخفي البِطاقَةَ صامِتاً،
    /// فَيَبحَثُ المالِكُ عَن زِرٍّ لا يَظهَر. <b>وإقلاعٌ يَشتَكي
    /// بِاسمِ المُتَغَيِّرِ أَرخَصُ مِن ذلك بِمَراحِل.</b></para>
    /// </summary>
    public static IServiceCollection AddPaddleBilling(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PaddleOptions>(configuration.GetSection(PaddleEnvironment.SectionKey));

        var options = new PaddleOptions();
        configuration.GetSection(PaddleEnvironment.SectionKey).Bind(options);

        if (PaddleEnvironment.IsMisconfiguredEnvironment(options.Environment))
            throw new InvalidOperationException(
                $"‏{PaddleEnvironment.EnvVarName(PaddleEnvironment.EnvironmentKey)} " +
                $"قيمَتُه «{options.Environment}» وهي خارِجَ " +
                $"«{PaddleEnvironment.Sandbox}»/«{PaddleEnvironment.Live}» — " +
                "اضبِطها أَو احذِفها. ولا تُخمَّن بيئَةُ دَفع.");

        if (PaddleEnvironment.IsConfigured(options))
        {
            services.AddHttpClient<PaddlePaymentProvider>(client =>
            {
                // سَقفٌ احتِياطيٌّ **ضِعفُ** مُهلَتِنا الداخِلِيَّة، لِتَسبِقَ
                // مُهلَتُنا دائِماً فَتَثبُتَ رِسالَةُ الخَطَإ بَدَلَ أَن
                // تَتَبَدَّلَ بِسِباق. نَفسُ حُجَّةِ `AddPayPalSubscriptions`.
                client.Timeout = PaddleEnvironment.Timeout(options.TimeoutSeconds) * 2;
            });
        }

        services.AddScoped(sp => new PaddleGateway(
            sp.GetRequiredService<IOptions<PaddleOptions>>().Value,
            sp.GetService<PaddlePaymentProvider>()));

        return services;
    }
}
