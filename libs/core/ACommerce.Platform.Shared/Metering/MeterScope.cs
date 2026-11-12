namespace ACommerce.Platform.Shared.Metering;

/// <summary>
/// <para><b>حامِلُ النِطاقِ لِطَلَبٍ واحِد — كائِنٌ مَرجِعِيٌّ بِحُقولٍ تُملَأ</b> (R1): الشَكلُ نَفسُه الَّذي يَستَعمِلُه <c>HttpContextAccessor</c>
/// لِلسَبَبِ نَفسِه. ما تَكتُبُه دالَّةٌ مُساعِدَةٌ مُنتَظَرَةٌ فيه يُرى بَعدَ عَودَتِها؛ وقيمَةٌ ثابِتَةٌ عَلى <c>AsyncLocal</c> تَضيعُ هُناك (مَقيس:
/// <c>role at save = _none_yet</c>).</para>
/// <para><b>قَواعِدُ الكِتابَة</b> (التَصميم §5.4) — كُلُّها في <see cref="Fill"/> وَحدَها:</para>
/// <list type="bullet">
/// <item>الحَقلُ يَنتَقِلُ مِن غَيرِ مَضبوطٍ إلى قيمَةٍ مَرَّةً واحِدَة (الكاتِبُ الأَوَّلُ يَفوز)؛</item>
/// <item><see cref="App"/> لا يُكتَبُ فَوقَه أَبَداً؛</item>
/// <item>والمُساعِدُ يَملَأُ الدَورَ ورائِدَ الأَعمالِ <b>فَقَط</b> حينَ يُساوي مَتجَرُه <see cref="App"/>، أَو حينَ يَضَعُ <see cref="App"/> غَيرَ المَضبوطِ
/// مِن مَسارِه هُوَ. فَنِطاقُ المَتجَرِ أ لا يَنسِبُ كِتابَةَ المَتجَرِ ب.</item>
/// </list>
/// <para><b>وحامِلُ البَذر</b> (<see cref="IsSeed"/>) لا يُملأ: دَورُه ورائِدُ أَعمالِه <see cref="MeterSentinels.Seed"/> لِكُلِّ مَتجَر.</para>
/// </summary>
public sealed class MeterScopeHolder
{
    private readonly object _gate = new();

    internal MeterScopeHolder(bool seed)
    {
        IsSeed = seed;
        if (seed) { Entrepreneur = MeterSentinels.Seed; Role = MeterSentinels.Seed; }
    }

    public bool IsSeed { get; }
    public string? App { get; private set; }
    public string? Entrepreneur { get; private set; }
    public string? Role { get; private set; }

    /// <summary>يَملَأُ ما لَم يُملأ لِلمَتجَرِ <paramref name="app"/> — ويُعيدُ <c>false</c> إن كانَ الحامِلُ لِمَتجَرٍ آخَرَ أَو حامِلَ بَذر
    /// (فَلا يُكتَبُ شَيء).</summary>
    public bool Fill(string app, string? entrepreneur = null, string? role = null)
    {
        if (IsSeed || string.IsNullOrEmpty(app)) return false;
        lock (_gate)
        {
            if (App is null) App = app;
            else if (!string.Equals(App, app, StringComparison.OrdinalIgnoreCase)) return false;
            if (Entrepreneur is null && !string.IsNullOrEmpty(entrepreneur)) Entrepreneur = entrepreneur;
            if (Role is null && !string.IsNullOrEmpty(role)) Role = role;
            return true;
        }
    }

    /// <summary>
    /// <para>‏(‏2026-11-12، W13-6·٨ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>دَورُ فاعِلٍ عَلى مُستَوى المَنَصَّة</b> — مُشرِفُ المَنَصَّةِ عَلى مَسارٍ لا مَتجَرَ فيه
    /// (<c>/admin/tenants/create</c>، <c>/admin/agent/…</c>): يَضَعُ <see cref="Role"/> إن لَم يُضبَط (الكاتِبُ الأَوَّلُ يَفوز)، و<b>لا يَمَسُّ</b>
    /// <see cref="App"/> ولا <see cref="Entrepreneur"/> — فَلا مَتجَرَ يُدَّعى، ولا يُنسَبُ مَتجَرٌ إلى غَيرِ مالِكِه.</para>
    /// <para>وما دامَ الحامِلُ بِلا مَتجَرٍ فَالدَورُ لِكُلِّ كِتابَةٍ في الطَلَب — الفاعِلُ واحِدٌ أَيّاً كانَ المَتجَرُ الَّذي يَكتُبُ فيه (يَقرَؤُه
    /// <c>MeterRules.FromScope</c>)؛ وإن مَلَأَه بَعدَه مُساعِدٌ لِمَتجَرٍ صارَ لِذلكَ المَتجَرِ وَحدَه كَأَيِّ حامِل. ويُعيدُ <c>false</c> لِحامِلِ البَذر.</para>
    /// </summary>
    public bool FillPlatformRole(string role)
    {
        if (IsSeed || string.IsNullOrEmpty(role)) return false;
        lock (_gate)
        {
            Role ??= role;
            return true;
        }
    }

    /// <summary>لَقطَةٌ ثابِتَةٌ لِما في الحامِلِ لَحظَةَ الحِفظ — يَقرَؤُها المُصَنِّفُ النَقِيّ.</summary>
    public MeterScopeView View()
    {
        lock (_gate) return new MeterScopeView(App, Entrepreneur, Role, IsSeed);
    }
}

/// <summary>ما في الحامِلِ لَحظَةَ الحِفظ — قيمَةٌ لا تَتَغَيَّرُ يَقرَؤُها <c>MeterRules</c>.</summary>
public sealed record MeterScopeView(string? App, string? Entrepreneur, string? Role, bool IsSeed);

/// <summary>
/// <para><b>الحامِلُ الجاري</b> — <c>AsyncLocal</c> يَحمِلُ <b>مَرجِعَ</b> الحامِل لا قيمَتَه، فَيَعبُرُ إلى ما بَعدَ الوَسيطِ ويُرى ما يُكتَبُ فيه
/// داخِلَ الدَوالِّ المُنتَظَرَة.</para>
/// <para><b>فاتِحاه اثنان</b>: <see cref="Open"/> في أَوَّلِ سَطرٍ مِن وَسيطِ المُستَأجِرِ لِكُلِّ طَلَب، و<see cref="Seed"/> حَولَ كُتلَةِ البَذرِ في
/// <c>Program.cs</c> — ويُعادُ ما قَبلَه عِندَ التَخَلُّص، فَلا يَرِثُه ما يَبدَأُ بَعدَ الكُتلَة (خِدماتُ الخَلفِيَّةِ وطَلَباتُ الخادِم).</para>
/// </summary>
public static class MeterScope
{
    private static readonly AsyncLocal<MeterScopeHolder?> CurrentHolder = new();

    public static MeterScopeHolder? Current => CurrentHolder.Value;

    /// <summary>لَقطَةُ الحامِلِ الجاري، أَو <c>null</c> بِلا حامِل.</summary>
    public static MeterScopeView? View() => CurrentHolder.Value?.View();

    /// <summary>‏(‏2026-11-12، W13-6·٨) <b>رائِدُ أَعمالِ مَتجَرٍ مِن مالِكِه</b>: المُعَرِّفُ بِصيغَةِ <c>D</c>، و<see cref="MeterSentinels.Unowned"/> لِمَتجَرٍ بِلا مالِك
    /// (<c>Guid.Empty</c>) — شَكلُ وَسيطِ المُستَأجِرِ نَفسُه، يَقرَؤُه المُصَنِّفُ (<c>store.create</c>) وحارِسُ إدارَةِ المَتجَر.</summary>
    public static string OwnerOf(Guid ownerUserId) => ownerUserId == Guid.Empty ? MeterSentinels.Unowned : ownerUserId.ToString("D");

    /// <summary>يَفتَحُ حامِلاً جَديداً فارِغاً لِلطَلَبِ الجاري ويُعيدُه.</summary>
    public static MeterScopeHolder Open()
    {
        var holder = new MeterScopeHolder(seed: false);
        CurrentHolder.Value = holder;
        return holder;
    }

    /// <summary>يَفتَحُ <b>حامِلَ البَذر</b> (دَورٌ ورائِدُ أَعمالٍ <see cref="MeterSentinels.Seed"/> لِكُلِّ مَتجَر)، ويُعيدُ ما قَبلَه عِندَ التَخَلُّص.</summary>
    public static IDisposable Seed()
    {
        var previous = CurrentHolder.Value;
        CurrentHolder.Value = new MeterScopeHolder(seed: true);
        return new Restore(previous);
    }

    private sealed class Restore(MeterScopeHolder? previous) : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            CurrentHolder.Value = previous;
        }
    }
}
