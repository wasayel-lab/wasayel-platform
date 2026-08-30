using System.IO.Compression;
using System.Text;
using ACommerce.Kit.Files;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Audit;
using ACommerce.Templates.Customer.Marketplace.Services.Export;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ بُرهانُ التَخارُجِ على قاعِدَةٍ حَيَّة — قِراءَةٌ خالِصَة ═══════
//
// هذا **لَيسَ اختِبارَ وَحدَة**، ولا يَعمَلُ في التَشغيلِ العادِيّ:
// يَتَخَطّى نَفسَه ما لَم تُضبَط `WASAYEL_LIVE_PROOF=1` و
// `ConnectionStrings__Postgres`. نَفسُ عَقدِ `LiveThemeProofTests`
// و`LiveRoleDefinitionProofTests` حَرفاً — حَقيبَةُ الاختِباراتِ
// تَبقى نَقِيَّةً وبِلا شَبَكَة.
//
// ─── ولِماذا يَلزَمُ بُرهانٌ حَيٌّ رَغمَ ‏39 اختِباراً أَخضَر ──────
//
// اختِباراتُ `TenantExportTests` تَقيسُ **الحارِسَ والسِجِلَّ
// والكاتِب** بِبَياناتٍ مَصنوعَة. وثَلاثَةُ أَشياءَ لا تَبلُغُها
// بِطَبيعَتِها:
//
//   ١. **الإرسالُ بِالنَوعِ وَقتَ التَشغيل**: السِجِلُّ يَحمِلُ
//      `Type` ويُنادى بِـ`MakeGenericMethod`. خَطَأٌ هُناكَ لا يَظهَر
//      إلّا عِندَ استِعلامٍ حَقيقيّ.
//   ٢. **صِنفٌ حَيٌّ بِلا جَدوَلٍ في الإنتاج**: مَقيسٌ أَنَّ أَربَعَةَ
//      أَصنافٍ كَذلك. والسُلوكُ المَقصودُ أَن يُكتَبَ سَبَبُ
//      التَعَذُّرِ لا أَن يُبتَلَع.
//   ٣. **العَزلُ في Postgres نَفسِه**: أَنّ جَلسَةَ السلاجِ لا تَرُدُّ
//      صَفَّ مُستَأجِرٍ آخَرَ — وذاكَ عَقدُ Marten لا عَقدُنا،
//      ويُقاسُ ولا يُفتَرَض.
//
// **ولا يَكتُب حَرفاً**: يُنادي `CollectAsync` لا `ProduceAsync`
// (‏الأَخيرَةُ تَكتُبُ قَيدَ تَدقيق)، و`AutoCreateSchemaObjects = None`
// فَلا مُخَطَّطَ يُنشَأ ولا يُعَدَّل.
public class LiveTenantExportProofTests(ITestOutputHelper output)
{
    private static bool Enabled =>
        Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1";

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");

    [Fact]
    public async Task ExportEveryOwnedTenantAndProveNoForeignRowLeaves()
    {
        if (!Enabled || string.IsNullOrEmpty(ConnectionString)) return;

        using var store = DocumentStore.For(o =>
        {
            o.Connection(ConnectionString!);
            o.DatabaseSchemaName = "platform";
            o.AutoCreateSchemaObjects = JasperFx.AutoCreate.None;

            o.Policies.AllDocumentsAreMultiTenanted();
            o.Events.TenancyStyle = global::JasperFx.MultiTenancy.TenancyStyle.Conjoined;

            // نَفسُ الثَمانِيَةِ المُسَجَّلَةِ في `HostingExtensions`
            // و`MarketplaceTemplateExtensions` — والقائِمَةُ مَقيسَةٌ
            // بِـ`No_globally_registered_document_is_exported_as_a_table`.
            o.Schema.For<Tenant>().SingleTenanted().Identity(x => x.Id);
            o.Schema.For<ACommerce.Kit.Subscriptions.TenantPlan>().SingleTenanted().Identity(x => x.Id);
            o.Schema.For<ACommerce.Kit.Subscriptions.PlatformSettings>().SingleTenanted().Identity(x => x.Id);
            o.Schema.For<ACommerce.Kit.Subscriptions.PlatformPlanPayPal>().SingleTenanted().Identity(x => x.Id);
            o.Schema.For<ACommerce.Kit.Subscriptions.PayPalWebhookRecord>().SingleTenanted().Identity(x => x.Id);
            o.Schema.For<ACommerce.Kit.Subscriptions.PayPalOrderRecord>().SingleTenanted().Identity(x => x.Id);
            o.Schema.For<ACommerce.Kit.Subscriptions.PaddleTransactionRecord>().SingleTenanted().Identity(x => x.Id);
            o.Schema.For<ACommerce.Templates.Customer.Marketplace.Services.Api.ApiKeyDocument>()
                .SingleTenanted().Identity(x => x.Id);

            // وَثائِقُ مُعَرِّفُها نَصّ — تُعلَن كَما تُعلَن هُناك.
            o.Schema.For<ACommerce.Kit.Roles.TenantRoleDefinition>().Identity(x => x.Id);
            o.Schema.For<ACommerce.Kit.Theme.TenantThemeDefinition>().Identity(x => x.Id);
            o.Schema.For<ACommerce.Kit.Subscriptions.TenantPlanDefinition>().Identity(x => x.Id);
            o.Schema.For<ACommerce.Platform.Shared.ImportedRecord>().Identity(x => x.Id);
            o.Schema.For<ACommerce.Platform.Providers.TenantProviderBinding>().Identity(x => x.Id);
        });

        var service = new TenantExportService(store, new UnavailableFileStorage(), new AuditWriter(store));

        List<Tenant> tenants;
        await using (var s = store.QuerySession())
            tenants = (await s.Query<Tenant>().ToListAsync()).ToList();

        output.WriteLine($"[tenants] {tenants.Count} في السِجِلّ: " +
                         string.Join("، ", tenants.Select(t => t.Id)));

        var owned = tenants.Where(t => t.OwnerUserId != Guid.Empty).ToList();
        Assert.True(owned.Count > 0, "صِفرُ مَتجَرٍ لَه مالِك — لا شَيءَ يُقاس.");

        var allSlugs = tenants.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var tenant in owned)
        {
            var content = await service.CollectAsync(tenant, tenant.OwnerUserId);

            using var ms = new MemoryStream();
            TenantExportPackageWriter.Write(ms, content);   // الكاتِبُ هُوَ الحارِس — يَرمي عِندَ أَيِّ خَرق
            ms.Position = 0;

            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

            var rows = content.Tables.Sum(t => t.Rows.Count);
            var failed = content.Tables.Where(t => t.ReadErrorAr is not null).ToArray();

            output.WriteLine(
                $"[{tenant.Id}] {content.Tables.Count} جَدوَلاً · {rows} صَفّاً · " +
                $"{zip.Entries.Count} مَدخَلاً · {ms.Length} بايت · " +
                $"{failed.Length} تَعَذَّرَت قِراءَتُه");

            foreach (var f in failed)
                output.WriteLine($"    ⚠ {f.TypeName}: {f.ReadErrorAr}");

            // ‏١) كُلُّ صِنفٍ حاضِرٌ — النَقصُ ضَرَرٌ كَالتَسريب.
            Assert.Equal(TenantExportLedger.Exported.Count, content.Tables.Count);

            // ‏٢) ولا سلاجَ آخَرَ يَظهَرُ في نَصِّ الحَقيبَةِ كُلِّها.
            // **ومَداخِلُ البَياناتِ وَحدَها، لا `manifest.json`**: الفَهرَسُ
            // الآلِيُّ يُعَدِّدُ الحُقولَ المَحذوفَةَ بِأَسمائِها —
            // فَوُجودُ «‏PushSubscriptions» فيه **إعلانُ حَذفٍ لا
            // تَسريب**، ومَسحُه هُنا كانَ سَيَتَّهِمُ الحَقيبَةَ بِما
            // يُبَرِّئُها. (وَقَعَ فِعلاً في أَوَّلِ تَشغيل.)
            var foreign = allSlugs.Where(s => !string.Equals(s, tenant.Id, StringComparison.Ordinal));
            var dataEntries = zip.Entries
                .Where(e => e.FullName.EndsWith(".json", StringComparison.Ordinal))
                .Where(e => e.FullName.StartsWith("data/", StringComparison.Ordinal)
                         || e.FullName.StartsWith("owner/", StringComparison.Ordinal))
                .ToArray();
            Assert.Equal(TenantExportLedger.Exported.Count, dataEntries.Length);

            foreach (var e in dataEntries)
            {
                using var reader = new StreamReader(e.Open(), Encoding.UTF8);
                var text = await reader.ReadToEndAsync();

                foreach (var other in foreign)
                    Assert.DoesNotContain($"\"tenantSlug\":\"{other}\"", text, StringComparison.Ordinal);

                foreach (var forbidden in TenantExportRedaction.ForbiddenAnywhere)
                    Assert.DoesNotContain($"\"{forbidden}\"", text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
