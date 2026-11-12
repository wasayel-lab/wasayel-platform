using ACommerce.Kit.Auth;
using ACommerce.Kit.Auth.Server;
using ACommerce.Platform.Shared.Metering;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebPush;

namespace ACommerce.Templates.Customer.Marketplace.Services;

/// <summary>
/// إرسال إشعارات Web Push (VAPID) لِكُلّ الاشتِراكات المُسَجَّلَة عَلى
/// <see cref="User.PushSubscriptions"/>. مَفاتيح VAPID تُقرَأ مِن
/// configuration (<c>WebPush:Subject/PublicKey/PrivateKey</c>) — إن كانَت
/// مَفقودَة نُوَلِّد زَوجاً جَديداً مَرَّة واحِدَة عِندَ التَّشغيل وَنُسَجِّله في
/// الـ logs لِيَحفَظَه المُدير في appsettings.Local.json. هذا أَفضَل مِن
/// التَّعطيل الصامِت (الإشعارات تَعمَل في dev بِدون إعداد) وَأَفضَل مِن
/// التَّوليد كُلّ تَشغيل (الاشتِراكات المَوجودَة تُصبِح غَير قابِلَة لِلإرسال
/// لِأَنّ المُتَصَفِّح حَفِظَ الـ public key السابِق).
/// <para>(‏2026-10-25، تَدقيقُ الإطلاق lens-docs-vs-code-1 — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه): «مَرَّة واحِدَة عِندَ التَّشغيل» هي
/// <b>كُلُّ</b> تَشغيل — الخِدمَةُ <c>AddSingleton</c> — و<c>appsettings.Local.json</c> لا يَقرَؤُه المُضيف (لا <c>AddJsonFile</c>
/// في <c>Program.cs</c>)، والسَطرُ لا يَطبَعُ المِفتاحَ الخاصَّ فَلا يُحفَظُ الزَوجُ مِنه أَصلاً. فَصارَ سَطرُ الإقلاعِ يُسَمّي مُتَغَيِّراتِ
/// البيئَةِ الثَلاثَةَ بِحَرفِها (<see cref="SubjectEnvVar"/> و<see cref="PublicKeyEnvVar"/> و<see cref="PrivateKeyEnvVar"/>) ودَليلَ
/// النَشر (<c>docs/DEPLOY.md</c> §٢ الخُطوَة ٦ — الزَوجُ يُوَلَّدُ مَرَّةً عِندَ المالِكِ ويُحفَظُ سِرّاً)، ويَكتُبُ بِرَمزِه
/// <see cref="BootLogPrefix"/> <c>configured</c> أَو <c>ephemeral</c> — تَقرَؤُه بَوّابَةُ الإقلاعِ (الكُتلَة <c>webpush_keys</c> في
/// <c>scripts/verify-production-boot.sh</c>) فَتُنَبِّهُ ولا تُفشِل. والتَوليدُ عِندَ الغِيابِ باقٍ كَما كان: الإشعاراتُ تَعمَلُ في
/// التَطويرِ بِلا إعداد، والتَطبيقُ لا يَكتُبُ مِفتاحاً عَلى قُرصٍ ولا في القاعِدَة.</para>
/// <para>‏(‏2026-11-12، الموجَة ١٣ W13-6·١٣ — ADR-051 §١٨) <b>كُلُّ إرسالٍ نِداءٌ مَعدود</b>: فِعلُ <c>_client</c> داخِلَ <see cref="ProviderCalls.InvokeAsync{T}"/>
/// (الطَبَقَةُ ٨-ج تَقيسُه)، سَطرُ <see cref="OutboundUnits.PushSend"/> لِكُلِّ اشتِراكٍ قَبِلَته الخِدمَة، ومُعَرِّفُ طَلَبِه
/// <c>{notificationId}:{Pseudonym(endpoint)}</c> — الإشعارُ المَكتوبُ قَبلَه وعُنوانُ الاشتِراكِ اسماً مُستَعاراً (التَصميم §6.2). و4xx (ومِنه 404/410 الَّذي يَمحو
/// الاشتِراك) مَرفوضٌ عَلى النِيَّة، وما سِواه مِن رَميٍ مَجهول — والمَحوُ واللوغُ كَما كانا (<c>NafathS3PushRequestShapeCharacterizationTests</c>).</para>
/// </summary>
public sealed class WebPushService
{
    /// <summary>مُتَغَيِّراتُ البيئَةِ بِحَرفِها كَما يُتَرجِمُها .NET إلى <c>WebPush:Subject/PublicKey/PrivateKey</c> — يُسَمّيها سَطرُ
    /// الإقلاعِ وجَدوَلُ دَليلِ النَشر (<c>WebPushVapidKeysTests</c> يَحمَرُّ إن افتَرَقا).</summary>
    public const string SubjectEnvVar = "WebPush__Subject";
    public const string PublicKeyEnvVar = "WebPush__PublicKey";
    public const string PrivateKeyEnvVar = "WebPush__PrivateKey";

    /// <summary>سَطرُ الإقلاعِ الَّذي تَقرَؤُه بَوّابَةُ الإقلاع — بِالرَمزِ لا بِنَصٍّ يَنجَرِف (نَسَقُ
    /// <c>StudioAnalysisDoor.BootLogPrefix</c>). والخِدمَةُ تُنشَأُ عِندَ أَوَّلِ طَلَبٍ يَحتاجُها — والبَوّابَةُ تَطلُبُ
    /// <c>/api/push/vapid-key</c> في جَدوَلِ مَساراتِها.</summary>
    public const string BootLogPrefix = "[webpush] vapid_keys=";
    public const string ConfiguredValue = "configured";
    public const string EphemeralValue = "ephemeral";

    private readonly WebPushClient _client = new();
    private readonly VapidDetails _vapid;
    private readonly ILogger<WebPushService> _log;
    private readonly ProviderCalls _calls;
    public string PublicKey => _vapid.PublicKey;
    public bool IsConfigured { get; }

    /// <summary>
    /// <para>‏(‏2026-11-12، W13-6·١٣ — التَصميم §6.3) <b>مَقعَدُ اختِبار</b>: الباني القَديمُ بِمُستَدعٍ لا يُسَجِّل (<see cref="ProviderCalls.Unrecorded"/>) — فَتَبقى بُناةُ
    /// الخِدمَةِ في الاختِباراتِ (‏15 مَوضِعاً في 12 مِلَفّاً) كَما كانَت. <b>ولا يُناديه كودُ الإنتاج</b>: تَسجيلُ القالِبِ يَحُلُّ الباني الأَطوَلَ بِمُستَدعي المُضيف،
    /// و<c>MeterTestSeamTests</c> (G6c) يَحمَرُّ عَلى مُنادٍ لِهذا في <c>libs/</c> أَو <c>apps/</c>.</para>
    /// </summary>
    public WebPushService(IConfiguration cfg, ILogger<WebPushService> log)
        : this(cfg, log, ProviderCalls.Unrecorded)
    {
    }

    public WebPushService(IConfiguration cfg, ILogger<WebPushService> log, ProviderCalls calls)
    {
        _log = log;
        _calls = calls;
        var section = cfg.GetSection("WebPush");
        var subject = section["Subject"] ?? "mailto:admin@acommerce.local";
        var pub     = section["PublicKey"];
        var priv    = section["PrivateKey"];

        if (string.IsNullOrEmpty(pub) || string.IsNullOrEmpty(priv))
        {
            var keys = VapidHelper.GenerateVapidKeys();
            pub  = keys.PublicKey;
            priv = keys.PrivateKey;
            _log.LogWarning(
                BootLogPrefix + EphemeralValue + " — VAPID keys missing: generated a pair for this process only. " +
                "Every restart generates another, and the subscriptions browsers saved with the previous public key " +
                "stop receiving pushes (a row is dropped only on 404/410). Generate one pair once and set " +
                SubjectEnvVar + ", " + PublicKeyEnvVar + " and " + PrivateKeyEnvVar + " as environment variables " +
                "(Space secrets) — docs/DEPLOY.md §2 step 6. Public key of this process: {Public}",
                pub);
            IsConfigured = false;
        }
        else
        {
            _log.LogInformation(BootLogPrefix + ConfiguredValue);
            IsConfigured = true;
        }

        _vapid = new VapidDetails(subject, pub, priv);
    }

    /// <summary>
    /// أَرسِل إشعار push لِكُلّ اشتِراكات المُستَخدِم. آمِنَة لِلاستِدعاء —
    /// لا تَرمي إن كانَ هناك خَطَأ في إرسال فَردي (نُسَجِّله فَقَط). إن كانَ
    /// الاشتِراك مُنتَهي (HTTP 404/410) نَحذِفه مِن User doc.
    /// </summary>
    /// <param name="notificationId">‏(‏2026-11-12، W13-6·١٣ — ADR-051 §١٨) <b>مُعَرِّفُ الإشعارِ المَكتوبِ قَبلَ الإرسال</b> — مِفتاحُ العَمَلِيَّةِ المَعدودَة:
    /// كُلُّ اشتِراكٍ نِداءٌ بِمُعَرِّفِ <c>{notificationId}:{Pseudonym(endpoint)}</c>، فَالإشعارُ نَفسُه لِلاشتِراكِ نَفسِه سَطرٌ واحِدٌ وإن أُعيد. وفي التَوقيعِ
    /// لا في الجِسم (القاعِدَة ٦): نِسيانُه خَطَأُ بِناء. والمَتجَرُ <paramref name="tenantSlug"/> نَفسُه.</param>
    public async Task SendAsync(
        IDocumentStore store, string tenantSlug, Guid userId, Guid notificationId,
        string title, string body, string? url = null, string? tag = null,
        CancellationToken ct = default)
    {
        // ‏(‏2026-10-29، تَدقيقُ الموجَة ١١ G-1) **قِراءَةٌ لا تُحفَظ**: كانَت الجَلسَةُ نَفسُها تَقرَأُ ثُمَّ تَطرُقُ الشَبَكَةَ ثُمَّ تَحفَظُ الوَثيقَةَ كامِلَةً —
        // أَطوَلُ نافِذَةٍ بَينَ قِراءَةٍ وحِفظ، فَيُعيدُ حِفظُها إصدارَ جَلسَةٍ رُفِعَ أَو حِساباً حُذِفَ بَينَهُما. والمَحوُ أَدناه يَقرَأُ مِن جَديد.
        User? user;
        await using (var q = store.QuerySession(tenantSlug)) user = await q.LoadAsync<User>(userId, ct);
        if (user is null || user.PushSubscriptions.Count == 0) return;

        var iconUrl = $"/api/{tenantSlug}/icon.svg";
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            title, body, url = url ?? $"/{tenantSlug}",
            icon = iconUrl, badge = iconUrl, tag
        });

        var stale = new List<ACommerce.Kit.Auth.PushSubscription>();
        foreach (var sub in user.PushSubscriptions)
        {
            // ‏(‏2026-10-24، تَدقيقُ الإطلاق lens-security-12) صَفٌّ حُفِظَ قَبلَ البابِ ولا يَقبَلُه اليَومَ لا يُطرَق — يُمحى كَالمُنتَهي.
            if (!PushSubscriptionDoor.Admits(sub)) { stale.Add(sub); continue; }
            try
            {
                var ps = new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                // ‏(‏2026-11-12، W13-6·١٣) النِداءُ المَعدود: رَفضُ الخِدمَةِ (4xx) يَعودُ قيمَةً فَيُعَدُّ مَرفوضاً ثُمَّ يُرمى بِحالِه (فَالمَحوُ واللوغُ أَدناه كَما كانا)،
                // وما سِواه يَخرُجُ مِن المُستَدعي مَجهولاً.
                var call = new ProviderCall(OutboundUnits.PushSend, ProviderNames.WebPush, tenantSlug,
                    $"{notificationId:N}:{UsageEntryId.Pseudonym(OutboundUnits.PushSend, tenantSlug, sub.Endpoint)}");
                var refused = await _calls.InvokeAsync<WebPushException?>(call, async t =>
                {
                    try
                    {
                        await _client.SendNotificationAsync(ps, payload, _vapid, t);
                        return null;
                    }
                    catch (WebPushException ex) when ((int)ex.StatusCode is >= 400 and <= 499) { return ex; }
                }, r => new ProviderAcceptance(r is null ? ProviderOutcome.Accepted : ProviderOutcome.Refused), ct);
                if (refused is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(refused);
            }
            catch (WebPushException ex) when (
                ex.StatusCode == System.Net.HttpStatusCode.Gone ||
                ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                stale.Add(sub);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Push send failed for user {UserId}", userId);
            }
        }

        if (stale.Count > 0)
        {
            // المُنتَهي وَحدَه — بِعُنوانِه — مِن قِراءَةٍ جَديدَة (`AccountWrites.UpdateAsync`): ما كَتَبَه غَيرُه أَثناءَ الإرسالِ يَبقى.
            var endpoints = stale.Select(p => p.Endpoint).ToHashSet(StringComparer.Ordinal);
            await AccountWrites.UpdateAsync<User>(store, tenantSlug, userId,
                u => u.PushSubscriptions.RemoveAll(p => endpoints.Contains(p.Endpoint)) > 0, ct);
        }
    }
}
