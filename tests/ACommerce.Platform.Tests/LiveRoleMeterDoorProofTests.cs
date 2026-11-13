using System.Reflection;
using System.Text.Json;
using ACommerce.Kit.Roles;
using ACommerce.Templates.Customer.Marketplace.Services;
using Marten;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

/// <summary>
/// <para><b>بُرهانٌ حَيٌّ: بابا الكِتابَةِ يَرُدّانِ مِفتاحَ المِترِ ولا يَرُدُّه حِزامُ القِراءَة</b> (‏2026-11-13، الموجَة ١٣ W13-6·١٧ — ‏ADR-051 §٢١؛
/// التَصميم §7). <c>RoleMeterValidatorTests</c> يُثبِتُ الدالَّةَ ومَن يُناديها نَصّاً؛ وهذا يُثبِتُ الوَصلَ عَلى Postgres بِالمُنَفِّذِ الحَقيقيِّ وخِدمَةِ الأَدوارِ
/// الحَقيقِيَّة (كَأَخيه <c>LiveRoleDefinitionProofTests</c>)، في مُستَأجِرٍ يُنشِئُه لِكُلِّ جَولَة:</para>
/// <list type="number">
///   <item><b>بابُ الاقتِراح</b> (<c>define_role</c>) بِطَبَقَتَيه: مُخَطَّطُ الأَداةِ يَرُدُّ تَعريفاً يَحمِلُ <c>"meter"</c> قَبلَ أَيِّ تَنفيذ، و<c>DefineRoleAsync</c>
///   نَفسُه — مُنادىً مُباشَرَةً — يَرُدُّه بِـ<c>meter_authored_by_tenant</c>؛ <b>ولا تُكتَبُ لَه وَثيقَة</b> في الحالَتَين، والتَعريفُ نَفسُه بِلا المِفتاحِ يُكتَبُ مُعَلَّقاً.</item>
///   <item><b>بابُ الاعتِماد</b> (<c>DecideAsync</c>): وَثيقَةٌ مُعَلَّقَةٌ كُتِبَت بِاليَدِ وتَحمِلُ المِفتاحَ يُرَدُّ اعتِمادُها بِرَمزِه <b>وتَبقى مُعَلَّقَة</b>؛ والمُقتَرَحُ
///   بِلا مِفتاحٍ يُعتَمَد.</item>
///   <item><b>حِزامُ القِراءَة</b>: وَثيقَةٌ مُعتَمَدَةٌ تَحمِلُ المِفتاحَ (<c>"none"</c>) — كُتِبَت قَبلَ البابِ أَو بِاليَد — تُقرَأُ ولا تَسقُط، ومِترُها مِن المَنَصَّةِ
///   (<see cref="TenantRoleSet.MeterOf"/> = <c>presence.role</c>) لا مِنها.</item>
/// </list>
/// </summary>
[Collection(LiveProofCollection.Name)]
public class LiveRoleMeterDoorProofTests(ITestOutputHelper output)
{
    private const string TenantPrefix = "live-role-meter-";

    private static bool Enabled =>
        Environment.GetEnvironmentVariable("WASAYEL_LIVE_PROOF") == "1"
        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__Postgres"));

    private static string Definition(string slug, string? meterJson) =>
        TenantRoleDefinitionToolTests.KhayyatJson
            .Replace("\"slug\": \"khayyat\"", $"\"slug\": \"{slug}\"")
            .Replace("\"dealPatternAffinity\": null", meterJson is null ? "\"dealPatternAffinity\": null" : $"\"dealPatternAffinity\": null,\n  \"meter\": {meterJson}");

    [Fact]
    public async Task The_write_doors_refuse_the_meter_key_and_the_read_belt_keeps_what_was_approved()
    {
        if (!Enabled) return;

        using var store = DocumentStore.For(o =>
        {
            o.Connection(Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")!);
            o.DatabaseSchemaName = "platform";
            o.Policies.AllDocumentsAreMultiTenanted();
            o.Schema.For<ACommerce.Kit.Tenants.Tenant>().SingleTenanted().Identity(x => x.Id);
            o.Schema.For<TenantRoleDefinition>().Identity(x => x.Id);
            o.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
        });
        var roles = new TenantRoleService(store);
        var exec = new AgentToolExecutor(store, roles);

        var tenant = TenantPrefix + Guid.NewGuid().ToString("N")[..8];
        await using (var w = store.LightweightSession())
        {
            w.Insert(new ACommerce.Kit.Tenants.Tenant { Id = tenant, Name = tenant });
            await w.SaveChangesAsync();
        }
        output.WriteLine($"[tenant] {tenant}");

        async Task<TenantRoleDefinition?> Load(string slug)
        {
            await using var s = store.QuerySession(tenant);
            return await s.LoadAsync<TenantRoleDefinition>(slug);
        }

        // ─── ١. بابُ الاقتِراح — بِطَبَقَتَيه ─────────────────────────────────
        // ‏(أ) المُنَفِّذُ كَما يُناديه الوَكيل: مُخَطَّطُ الأَداةِ (`additionalProperties: false`) يَرُدُّ المِفتاحَ قَبلَ أَيِّ تَنفيذ؛
        // ‏(ب) والطَبَقَةُ الثانِيَةُ في `DefineRoleAsync` نَفسِها — تُنادى مُباشَرَةً بِالانعِكاس فَيُقاسُ رَدُّها إن ارتَخى المُخَطَّط.
        var defineRole = typeof(AgentToolExecutor).GetMethod("DefineRoleAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(defineRole);
        foreach (var (slug, value) in new[] { ("mutawwa", "\"presence.role\""), ("mutawwa_null", "null") })
        {
            var payload = $$"""{"slug":"{{tenant}}","definition":{{Definition(slug, value)}}}""";
            var (ok, msg) = await exec.ExecuteAsync("define_role", payload);
            output.WriteLine($"[propose/schema meter={value}] ok={ok} :: {msg}");
            Assert.False(ok);
            Assert.Contains("meter", msg, StringComparison.Ordinal);

            using var doc = JsonDocument.Parse(payload);
            var (innerOk, innerMsg) = await (Task<(bool, string)>)defineRole!.Invoke(exec, [doc.RootElement, CancellationToken.None])!;
            output.WriteLine($"[propose/door meter={value}] ok={innerOk} :: {innerMsg}");
            Assert.False(innerOk);
            Assert.Contains(RoleMeterValidator.MeterAuthoredByTenant, innerMsg, StringComparison.Ordinal);
            Assert.Null(await Load(slug));
        }
        var (bareOk, bareMsg) = await exec.ExecuteAsync("define_role", $$"""{"slug":"{{tenant}}","definition":{{Definition("khayyat", null)}}}""");
        output.WriteLine($"[propose/bare] ok={bareOk} :: {bareMsg}");
        Assert.True(bareOk, bareMsg);
        Assert.Equal(TenantRoleStatuses.Pending, (await Load("khayyat"))!.Status);

        // ─── ٢. بابُ الاعتِماد ───────────────────────────────────────────
        await using (var w = store.LightweightSession(tenant))
        {
            w.Store(new TenantRoleDefinition
            {
                Id = "rassam", Slug = "rassam", DefinitionJson = Definition("rassam", "\"presence.role\""),
                Status = TenantRoleStatuses.Pending, CreatedBy = "live-proof:hand-written", CreatedAt = DateTime.UtcNow,
            });
            await w.SaveChangesAsync();
        }
        var (refusedOk, refusedMsg) = await roles.DecideAsync(tenant, "rassam", TenantRoleStatuses.Approved, "live-proof");
        output.WriteLine($"[approve/meter] ok={refusedOk} :: {refusedMsg}");
        Assert.False(refusedOk);
        Assert.Contains(RoleMeterValidator.MeterAuthoredByTenant, refusedMsg, StringComparison.Ordinal);
        Assert.Equal(TenantRoleStatuses.Pending, (await Load("rassam"))!.Status);

        var (approvedOk, approvedMsg) = await roles.DecideAsync(tenant, "khayyat", TenantRoleStatuses.Approved, "live-proof");
        output.WriteLine($"[approve/bare] ok={approvedOk} :: {approvedMsg}");
        Assert.True(approvedOk, approvedMsg);

        // ─── ٣. حِزامُ القِراءَة ──────────────────────────────────────────
        await using (var w = store.LightweightSession(tenant))
        {
            w.Store(new TenantRoleDefinition
            {
                Id = "naqqash", Slug = "naqqash", DefinitionJson = Definition("naqqash", "\"none\""),
                Status = TenantRoleStatuses.Approved, CreatedBy = "live-proof:before-the-door", CreatedAt = DateTime.UtcNow,
            });
            await w.SaveChangesAsync();
        }
        var set = await TenantRoleService.ReadUncachedAsync(store, tenant);
        var authored = set.TenantAuthored.Select(d => d.Slug).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        output.WriteLine($"[belt] مُؤَلَّفَةٌ مَقروءَة: {string.Join(" ", authored)} · مِترُ naqqash في الوَثيقَة «{set.FindDefinition("naqqash")?.Meter}» ⇐ MeterOf «{set.MeterOf("naqqash")}» · khayyat ⇐ «{set.MeterOf("khayyat")}»");
        Assert.Equal(new[] { "khayyat", "naqqash" }, authored);
        Assert.Equal("none", set.FindDefinition("naqqash")!.Meter);
        Assert.Equal(RoleMeterUnits.Derived, set.MeterOf("naqqash"));
        Assert.Equal(RoleMeterUnits.Derived, set.MeterOf("khayyat"));
        Assert.Equal("presence.role", set.MeterOf("vendor"));
    }
}
