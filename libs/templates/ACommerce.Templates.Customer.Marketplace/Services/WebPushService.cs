using ACommerce.Kit.Auth;
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
/// </summary>
public sealed class WebPushService
{
    private readonly WebPushClient _client = new();
    private readonly VapidDetails _vapid;
    private readonly ILogger<WebPushService> _log;
    public string PublicKey => _vapid.PublicKey;
    public bool IsConfigured { get; }

    public WebPushService(IConfiguration cfg, ILogger<WebPushService> log)
    {
        _log = log;
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
                "VAPID keys missing — generated ephemeral pair. Save these in " +
                "appsettings.Local.json under \"WebPush\": {{ \"Subject\": \"{Subject}\", " +
                "\"PublicKey\": \"{Public}\", \"PrivateKey\": \"<keep secret>\" }}",
                subject, pub);
            IsConfigured = false;
        }
        else IsConfigured = true;

        _vapid = new VapidDetails(subject, pub, priv);
    }

    /// <summary>
    /// أَرسِل إشعار push لِكُلّ اشتِراكات المُستَخدِم. آمِنَة لِلاستِدعاء —
    /// لا تَرمي إن كانَ هناك خَطَأ في إرسال فَردي (نُسَجِّله فَقَط). إن كانَ
    /// الاشتِراك مُنتَهي (HTTP 404/410) نَحذِفه مِن User doc.
    /// </summary>
    public async Task SendAsync(
        IDocumentStore store, string tenantSlug, Guid userId,
        string title, string body, string? url = null, string? tag = null,
        CancellationToken ct = default)
    {
        await using var s = store.LightweightSession(tenantSlug);
        var user = await s.LoadAsync<User>(userId, ct);
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
            try
            {
                var ps = new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
                await _client.SendNotificationAsync(ps, payload, _vapid, ct);
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
            foreach (var sub in stale) user.PushSubscriptions.Remove(sub);
            s.Store(user);
            await s.SaveChangesAsync(ct);
        }
    }
}
