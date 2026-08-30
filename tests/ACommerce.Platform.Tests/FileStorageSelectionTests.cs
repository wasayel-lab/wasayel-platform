using System.Net;
using System.Text;
using ACommerce.Kit.Files;
using ACommerce.Kit.Files.Providers.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ اختِيارُ مَخزَنِ المِلَفّات — جَدوَلٌ نَقِيٌّ وحارِسٌ وبُرهانٌ حَيّ ═══
//
// ثَلاثُ طَبَقاتٍ لا واحِدَة، ولِكُلٍّ ما لا تَقولُه الأُخرى:
//
// **‏(أ) الجَدوَل** — دالَّةٌ نَقِيَّةٌ بِمُدخَلَين، تُقاس بِكُلِّ
// تَقاطُعاتِها. وهذا ما لا يَقولُه سَطرُ تَسجيلٍ في `Program.cs` أَبَداً
// (القاعِدَة ٢: الحَدُّ الَّذي لا يُقاس آلِيّاً يَنهار).
//
// **‏(ب) الحارِس** — يُقاس بِطَرَفَيه: يَصمُت على المَضبوط، ويَرمي على
// المُحاكي. **وحارِسٌ يَصمُت دائِماً حارِسٌ لا يُقاس.**
//
// **‏(ج) البُرهانُ الحَيّ** — العَميلُ يُوَجَّه إلى مُستَمِعِ HTTP
// مَحَلِّيٍّ فَيُقاسُ الطَلَبُ الَّذي أَرسَلَه فِعلاً: تَوقيعُ ‏SigV4
// **مِن الحُزمَةِ لا مِن يَدِنا**، والدَلوُ والمِفتاحُ في المَسار. وهذا
// ما لا يَقولُه أَيُّ فَحصِ نَصٍّ على المَصدَر.
//
// **وما لَم يُبرهَن، ويُقالُ لِماذا**: لا نِداءَ فِعليٌّ إلى Cloudflare
// R2 — لا اعتِمادَ في هذا الجِهاز. فَالمَقيسُ هُنا **ما نُرسِلُه**، وما
// تَقولُه R2 عَنه يُعرَف مِن أَوَّلِ رَفعٍ حَقيقيّ (`docs/ADR-017` §٦).
public class FileStorageSelectionTests
{
    private static S3StorageSettings Complete => new(
        "https://acct.r2.cloudflarestorage.com", "wasayel", "AKIA_TEST", "SECRET_TEST",
        "https://files.wasayel.test");

    // ═══ ‏(أ) الجَدوَل — بِكُلِّ تَقاطُعاتِه ═══════════════════════════

    [Theory]
    // غِيابٌ تامّ: التَطويرُ يَكتُب على القُرص، والإنتاجُ لا يَكتُب شَيئاً.
    [InlineData(true,  false, false, FileStorageChoice.Local)]
    [InlineData(false, false, false, FileStorageChoice.Unavailable)]
    // تَهيئَةٌ كامِلَة: المَخزَنُ الدائِمُ في البيئَتَين.
    [InlineData(true,  true,  false, FileStorageChoice.S3)]
    [InlineData(false, true,  false, FileStorageChoice.S3)]
    // تَهيئَةٌ ناقِصَة: الإقلاعُ يَتَوَقَّف في البيئَتَين.
    [InlineData(true,  false, true,  FileStorageChoice.Misconfigured)]
    [InlineData(false, false, true,  FileStorageChoice.Misconfigured)]
    public void The_storage_decision_is_a_pure_table(
        bool isDev, bool complete, bool partial, FileStorageChoice expected)
    {
        var settings = complete ? Complete
            : partial ? Complete with { SecretAccessKey = null, PublicBaseUrl = "" }
            : S3StorageSettings.None;

        Assert.Equal(expected, FileStorageSelection.Decide(isDev, settings));
    }

    /// <summary>‏<c>null</c> يُساوي «لا شَيءَ مَضبوط» — ولا يَنفَجِر.</summary>
    [Fact]
    public void A_null_configuration_is_read_as_absent_not_as_a_crash()
    {
        Assert.Equal(FileStorageChoice.Local, FileStorageSelection.Decide(true, null));
        Assert.Equal(FileStorageChoice.Unavailable, FileStorageSelection.Decide(false, null));
    }

    /// <summary><b>الفَراغُ لَيسَ قيمَة</b>: مُتَغَيِّرُ بيئَةٍ مَضبوطٌ
    /// إلى سِلسِلَةٍ فارِغَةٍ (وهُوَ ما تُنتِجُه لَوحاتُ النَشرِ عِندَ
    /// حَقلٍ مَتروك) يُعَدُّ غِياباً لا حُضوراً.</summary>
    [Fact]
    public void A_whitespace_only_value_counts_as_absent_not_as_configured()
    {
        var blank = new S3StorageSettings("  ", "", null, "\t", "");
        Assert.True(blank.IsAbsent);
        Assert.Equal(FileStorageChoice.Local, FileStorageSelection.Decide(true, blank));
    }

    /// <summary>ورِسالَةُ النَقصِ تَذكُر المِفتاحَ **بِحَرفِه**، فَلا
    /// يُبحَثُ عَنه في وَثيقَة.</summary>
    [Fact]
    public void A_partial_configuration_stops_boot_and_names_the_missing_key()
    {
        var partial = Complete with { SecretAccessKey = null };

        var ex = Assert.Throws<InvalidOperationException>(
            () => FileStorageSelection.AssertConfigurationIsCompleteOrAbsent(partial));

        Assert.Contains(FileStorageSelection.SecretAccessKeyKey, ex.Message, StringComparison.Ordinal);
        Assert.Contains(FileStorageSelection.BucketKey, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_complete_or_absent_configuration_does_not_stop_boot()
    {
        FileStorageSelection.AssertConfigurationIsCompleteOrAbsent(Complete);
        FileStorageSelection.AssertConfigurationIsCompleteOrAbsent(S3StorageSettings.None);
        FileStorageSelection.AssertConfigurationIsCompleteOrAbsent(null);
    }

    /// <summary>المَفاتيحُ الخَمسَةُ مُثَبَّتَةٌ بِحَرفِها — تُقرَأُ في
    /// `Program.cs` وتُكتَب في `docs/DEPLOY.md`، فَانحِرافُ أَحَدِهِما
    /// يُخفي مِفتاحاً لا يُقرَأ.</summary>
    [Fact]
    public void The_five_configuration_keys_are_pinned_by_name()
    {
        Assert.Equal(
            new[]
            {
                "Files:S3:Endpoint", "Files:S3:Bucket", "Files:S3:AccessKeyId",
                "Files:S3:SecretAccessKey", "Files:S3:PublicBaseUrl",
            },
            FileStorageSelection.ConfigKeys);
    }

    // ═══ ‏(ب) الحارِس — بِطَرَفَيه ═══════════════════════════════════════

    [Fact]
    public void Boot_stops_when_the_ephemeral_disk_is_registered_outside_development()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalFileStorage(o => o.RootPath = Path.Combine(Path.GetTempPath(), "wasayel-guard-test"));
        using var sp = services.BuildServiceProvider();

        var described = FileStorageSelection.Describe(sp.GetService<IFileStorage>());
        Assert.NotNull(described);
        Assert.True(described!.IsDevelopmentStub);

        var ex = Assert.Throws<InvalidOperationException>(
            () => FileStorageSelection.AssertNoStubsOutsideDevelopment(false, new[] { described }));
        Assert.Contains("قُرصٍ زائِل", ex.Message, StringComparison.Ordinal);
    }

    /// <summary><b>وحارِسٌ لا يَصمُت أَبَداً حارِسٌ مَكسور</b> — الطَرَفُ
    /// المُقابِلُ يُقاسُ هُنا: نَفسُ التَسجيلِ في التَطوير، والفَشَلُ
    /// المُغلَقُ خارِجَه.</summary>
    [Fact]
    public void Boot_is_silent_for_the_stub_inside_development_and_for_the_closed_failure_outside()
    {
        var stub = new RegisteredFileStorage("Local", IsDevelopmentStub: true);
        FileStorageSelection.AssertNoStubsOutsideDevelopment(true, new[] { stub });

        var services = new ServiceCollection();
        services.AddUnavailableFileStorage();
        using var sp = services.BuildServiceProvider();
        var described = FileStorageSelection.Describe(sp.GetRequiredService<IFileStorage>());

        Assert.False(described!.IsDevelopmentStub);
        FileStorageSelection.AssertNoStubsOutsideDevelopment(false, new[] { described });
    }

    /// <summary>وِعاءٌ بِلا تَسجيلٍ يُوصَف <c>null</c> ولا يَنفَجِر —
    /// فَالحارِسُ يَقيسُ ما سُجِّلَ لا ما تُوُقِّع.</summary>
    [Fact]
    public void An_empty_container_is_described_as_nothing_rather_than_throwing()
        => Assert.Null(FileStorageSelection.Describe(null));

    // ═══ ‏(ج) الفَشَلُ المُغلَق — الرَفضُ يَقَع عِندَ الكِتابَة ══════════

    /// <summary><b>هذا هُوَ السُقوطُ الآمِنُ بِعَينِه</b>: الرَفضُ عِندَ
    /// الكِتابَةِ يَمنَع الرابِطَ المُعَلَّقَ مِن الوُجود، فَلا صورَةَ
    /// مَكسورَةٌ تُرسَم بَعدَ شَهر.</summary>
    [Fact]
    public async Task Writing_without_a_durable_store_is_refused_so_no_dangling_link_is_ever_stored()
    {
        var storage = new UnavailableFileStorage();
        using var body = new MemoryStream(Encoding.UTF8.GetBytes("x"));

        var ex = await Assert.ThrowsAsync<FileStorageException>(
            () => storage.UploadAsync("tenants/t/listings/1/0.jpg", body, "image/jpeg"));
        Assert.Equal(UnavailableFileStorage.Reason, ex.Message);

        // والقِراءَةُ تَهدَأ — ما لَم يُكتَب لا يُقرَأ، والانفِجارُ عِندَ
        // عَرضِ صَفحَةٍ عُطلٌ بِلا فائِدَة.
        Assert.Null(await storage.ReadAsync("anything"));
        Assert.False(await storage.ExistsAsync("anything"));
        Assert.False(await storage.DeleteAsync("anything"));
        Assert.Equal("", await storage.GetPublicUrlAsync("anything"));
    }

    // ═══ ‏(د) البُرهانُ الحَيّ — الطَلَبُ المُرسَلُ فِعلاً ══════════════

    /// <summary>
    /// <para>مُستَمِعُ HTTP مَحَلِّيٌّ يَقوم مَقامَ نُقطَةِ R2، والعَميلُ
    /// يُوَجَّه إلَيه. فَيُقاسُ ما أَرسَلَه فِعلاً:</para>
    /// <list type="number">
    /// <item><b>تَوقيعٌ ‏SigV4 كامِلٌ مِن الحُزمَة</b> — والمُستودَعُ
    /// فيه جارانِ يَكتُبانِ تَوقيعَهُما بِاليَد، فَالفَرقُ يُقاسُ لا
    /// يُدَّعى.</item>
    /// <item><b>الدَلوُ في المَسار</b> — ‏<c>ForcePathStyle</c>، وبِدونِه
    /// يُبنى مُضيفٌ لا يُحَلُّ عِندَ R2.</item>
    /// <item><b>الإقليمُ <c>auto</c> داخِلَ نِطاقِ الاعتِماد</b> — وهُوَ
    /// الَّذي تَقبَلُه R2 وَحدَه.</item>
    /// <item><b>والرابِطُ المُرجَعُ مِن الجَذرِ العامِّ لا مِن نُقطَةِ
    /// التَوقيع</b> — وخَلطُهُما يَكتُب في القاعِدَةِ رابِطاً يَطلُب
    /// تَوقيعاً لِيُقرَأ.</item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task The_durable_client_signs_with_sigv4_from_the_package_and_addresses_the_bucket_by_path()
    {
        using var listener = new HttpListener();
        var port = FreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        string? authorization = null, method = null, rawUrl = null;
        var captured = 0;
        var pump = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            captured++;
            authorization = ctx.Request.Headers["Authorization"];
            method = ctx.Request.HttpMethod;
            rawUrl = ctx.Request.RawUrl;
            ctx.Response.StatusCode = 200;
            ctx.Response.Close();
        });

        var storage = new S3FileStorage(
            Microsoft.Extensions.Options.Options.Create(new S3FileStorageOptions
            {
                Endpoint = prefix.TrimEnd('/'),
                Bucket = "wasayel-media",
                AccessKeyId = "AKIAEXAMPLE",
                SecretAccessKey = "wJalrXUtnFEMI",
                PublicBaseUrl = "https://files.wasayel.test",
            }),
            LoggerFactory.Create(b => { }).CreateLogger<S3FileStorage>());

        using var body = new MemoryStream(Encoding.UTF8.GetBytes("fake-jpeg-bytes"));
        var stored = await storage.UploadAsync(
            "tenants/ejar/listings/7/0.jpg", body, "image/jpeg");

        await pump.WaitAsync(TimeSpan.FromSeconds(20));
        listener.Stop();
        storage.Dispose();

        Assert.True(captured == 1, $"أَداة عَمياء: التُقِطَ {captured} طَلَباً — والمَقيس واحِد.");

        Assert.Equal("PUT", method);
        Assert.StartsWith("AWS4-HMAC-SHA256 ", authorization, StringComparison.Ordinal);
        Assert.Contains("/auto/s3/aws4_request", authorization!, StringComparison.Ordinal);
        Assert.Contains("Signature=", authorization!, StringComparison.Ordinal);

        Assert.Equal("/wasayel-media/tenants/ejar/listings/7/0.jpg", rawUrl);

        // والرابِطُ مِن الجَذرِ العامِّ لا مِن نُقطَةِ التَوقيع.
        Assert.Equal(
            "https://files.wasayel.test/tenants/ejar/listings/7/0.jpg", stored.PublicUrl);
        Assert.Equal("tenants/ejar/listings/7/0.jpg", stored.Key);
        Assert.Equal(15, stored.SizeBytes);
    }

    /// <summary>الحارِسُ في الباني يَذكُر المِفتاحَ الناقِصَ — فَمَن بَنى
    /// الصِنفَ في سُكريبتٍ لا يَقرَأُ «فَشِلَ الاتِّصال».</summary>
    [Fact]
    public void The_durable_client_refuses_to_construct_with_a_missing_key()
    {
        var ex = Assert.Throws<FileStorageException>(() => new S3FileStorage(
            Microsoft.Extensions.Options.Options.Create(new S3FileStorageOptions
            {
                Endpoint = "https://acct.r2.cloudflarestorage.com",
                Bucket = "wasayel",
                AccessKeyId = "k",
                SecretAccessKey = "",
                PublicBaseUrl = "https://files.wasayel.test",
            }),
            LoggerFactory.Create(b => { }).CreateLogger<S3FileStorage>()));

        Assert.Contains("SecretAccessKey", ex.Message, StringComparison.Ordinal);
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
