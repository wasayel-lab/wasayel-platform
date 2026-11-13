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
/// <para>‏(‏2026-11-12، W13-6·B-R1·1 — يُلحَقُ) <b>واستِثناءٌ واحِدٌ مِن «الكاتِبُ الأَوَّلُ يَفوز» في الدَور</b>: دَورُ الوَسيطِ مُؤَقَّتٌ
/// (<see cref="FillProvisionalRole"/>، مِن الوَثيقَةِ المُخَزَّنَة)، ويَحِلُّ مَحَلَّه الدَورُ الَّذي قَبِلَ بِه حارِسُ صَلاحِيَّةٍ عَلى المَجموعَةِ المُجَسَّدَة
/// (<see cref="FillAcceptedRole"/>) — فَـ<c>_none</c> في السَطرِ ما قَرَّرَه الحارِسُ لا ما خَبَّأَه الوَسيط.</para>
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

    // ‏(‏2026-11-12، W13-6·B-R1·1 — مُراجَعَةُ المَرحَلَةِ ب · M6B-CNT-1، يُلحَقُ ولا يُعادُ حَرفٌ فَوقَ ما سَبَق) **دَورٌ مُؤَقَّتٌ ودَورٌ مَقبول**: كانَ الوَسيطُ
    // يَضَعُ `_none` بِـ`Fill` مِن `Tenant.Roles` المُخَزَّنَةِ المُخَبَّأَة، فَيَفوزُ عَلى الدَورِ الَّذي يَقبَلُ بِه حارِسا الصَلاحِيَّةِ عَلى المَجموعَةِ المُجَسَّدَة
    // (المُخَزَّنَةُ وفَوقَها المُؤَلَّفَةُ المُعتَمَدَة) — مَقيس: مَتجَرٌ بِلا أَدوارٍ مُخَزَّنَةٍ ودَورُه المُؤَلَّفُ «mandoob» يَنشُرُ بِه ⇐ `200` وسَطرُه `_none`.
    // فَدَورُ الوَسيطِ الآنَ **مُؤَقَّت** (`FillProvisionalRole`) يَحِلُّ مَحَلَّه **الدَورُ الَّذي قَبِلَ بِه حارِسُ صَلاحِيَّة** (`FillAcceptedRole`) — ولا يَحِلُّ مَحَلَّه
    // كاتِبٌ سِواه (`Fill` يَبقى «الكاتِبُ الأَوَّلُ يَفوز» بِحَرفِه)، ولا يَحِلُّ المُؤَقَّتُ مَحَلَّ دَورٍ قائِم.
    private bool _roleProvisional;

    /// <summary>
    /// <para>‏(‏2026-11-12، W13-6·B-R1·1) <b>دَورٌ مُؤَقَّتٌ مِن الوَثيقَةِ المُخَزَّنَة</b> — كاتِبُه الوَسيطُ وَحدَه: <c>_none</c> لِمَتجَرٍ <c>Tenant.Roles</c> المُخَزَّنَةُ فيه
    /// فارِغَة (مِن ذاكِرَتِه، وبِلا الأَدوارِ المُؤَلَّفَة). يُوضَعُ إن لَم يُضبَطِ الدَور، ويَحِلُّ مَحَلَّه دَورُ حارِسِ صَلاحِيَّةٍ قَبِلَ (<see cref="FillAcceptedRole"/>).
    /// وقَواعِدُ المَتجَرِ قَواعِدُ <see cref="Fill"/>: لِمَتجَرِه وَحدَه، ويَضَعُ <see cref="App"/> غَيرَ المَضبوط؛ و<c>false</c> لِحامِلِ البَذرِ ولِلفارِغ.</para>
    /// </summary>
    public bool FillProvisionalRole(string app, string role)
    {
        if (IsSeed || string.IsNullOrEmpty(app) || string.IsNullOrEmpty(role)) return false;
        lock (_gate)
        {
            if (App is null) App = app;
            else if (!string.Equals(App, app, StringComparison.OrdinalIgnoreCase)) return false;
            if (Role is null) { Role = role; _roleProvisional = true; }
            return true;
        }
    }

    /// <summary>
    /// <para>‏(‏2026-11-12، W13-6·B-R1·1) <b>الدَورُ الَّذي قَبِلَ بِه حارِسُ صَلاحِيَّة</b> — كاتِباه <c>PermissionFilter</c> و<c>HasPermissionAsync</c> وَحدَهُما، بَعدَ
    /// قَرارِهِما عَلى المَجموعَةِ المُجَسَّدَة: الدَورُ الفَعّالُ حينَ فيها أَدوار، و<c>_none</c> حينَ لا دَور. <b>يَحِلُّ مَحَلَّ الدَورِ المُؤَقَّت</b>
    /// (<see cref="FillProvisionalRole"/>) أَو يَملَأُ غَيرَ المَضبوط؛ ودَورٌ قائِمٌ غَيرُ مُؤَقَّتٍ يَبقى (الكاتِبُ الأَوَّلُ يَفوز). وقَواعِدُ المَتجَرِ قَواعِدُ
    /// <see cref="Fill"/>؛ و<c>false</c> لِمَتجَرٍ آخَرَ ولِحامِلِ البَذرِ ولِلفارِغ.</para>
    /// <para>‏(‏2026-11-12، W13-6·١٤ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>وكاتِبٌ ثالِث</b>: نُقطَةُ إشعارِ بَوّابَةِ المَتجَرِ (<c>/{slug}/payments/notify</c>) تَضَعُ
    /// <c>_provider</c> — الفاعِلُ هُناكَ المُزَوِّدُ لا عُضوُ المَتجَر، فَيَحِلُّ مَحَلَّ <c>_none</c> المُؤَقَّتِ مِن الوَسيط كَما يَحِلُّ دَورُ الحارِس.</para>
    /// </summary>
    /// <remarks>‏(‏2026-11-13، مُراجَعَةُ المَرحَلَةِ ج الأولى · M6C-RULES-R1-4 — ADR-051 §١٩ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>والكاتِبانِ الحارِسانِ وَحدَهُما ثانِيَةً</b>:
    /// «كاتِبٌ ثالِث» أَعلاه (بابُ إشعارِ البَوّابَة) ورابِعٌ لَم يُكتَب (بابُ وارِدِ OTO) كانا يَكتُبانِ <c>_provider</c> هُنا، فَصارَ «المَقبولُ» يَعني قَرارَ حارِسٍ أَو بابَ مُزَوِّد —
    /// وانتَقَلا إلى <see cref="FillProviderRole"/> بِالقَواعِدِ نَفسِها. ومَن يُنادي كُلَّ مالِئِ دَورٍ يُثَبِّتُه <c>MeterRoleWritersTests</c> بِأَنواعِه وعَدَدِ مَواضِعِه، فَكاتِبٌ جَديدٌ أَحمَر.</remarks>
    public bool FillAcceptedRole(string app, string? role) => FillOverProvisional(app, role);

    /// <summary>
    /// <para>‏(‏2026-11-13، مُراجَعَةُ المَرحَلَةِ ج الأولى · M6C-RULES-R1-4 — ADR-051 §١٩) <b>دَورُ المُزَوِّدِ عَلى بابِ إشعارِه</b> — <see cref="MeterSentinels.Provider"/> لِمَتجَرِ
    /// المَسار: الفاعِلُ هُناكَ المُزَوِّدُ لا عُضوُ المَتجَر. قَواعِدُه قَواعِدُ <see cref="FillAcceptedRole"/> بِحَرفِها (يَحِلُّ مَحَلَّ الدَورِ المُؤَقَّتِ ويَملَأُ غَيرَ المَضبوط،
    /// ولِمَتجَرِه وَحدَه، و<c>false</c> لِحامِلِ البَذرِ ولِلفارِغ) — وبِاسمٍ مُستَقِلٍّ لِيَبقى «المَقبولُ» قَرارَ حارِسِ صَلاحِيَّةٍ وَحدَه. كاتِباه بابا إشعارِ المَتجَر:
    /// بَوّابَتُه (<c>/{slug}/payments/notify</c>) وناقِلُه (<c>/{slug}/shipping/oto/order-status</c>)؛ وبابا المَنَصَّةِ (PayPal وPaddle) بِلا مَتجَرٍ فَبِـ<see cref="FillPlatformRole"/>.</para>
    /// </summary>
    public bool FillProviderRole(string app) => FillOverProvisional(app, MeterSentinels.Provider);

    /// <summary>دَورٌ يَحِلُّ مَحَلَّ المُؤَقَّتِ أَو يَملَأُ غَيرَ المَضبوط — لِمَتجَرِه وَحدَه. جِسمُ <see cref="FillAcceptedRole"/> كَما كانَ بِحَرفِه.</summary>
    private bool FillOverProvisional(string app, string? role)
    {
        if (IsSeed || string.IsNullOrEmpty(app) || string.IsNullOrEmpty(role)) return false;
        lock (_gate)
        {
            if (App is null) App = app;
            else if (!string.Equals(App, app, StringComparison.OrdinalIgnoreCase)) return false;
            if (Role is null || _roleProvisional) { Role = role; _roleProvisional = false; }
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
