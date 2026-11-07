#!/usr/bin/env dotnet
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false
#:project ../libs/templates/ACommerce.Templates.Customer.Marketplace/ACommerce.Templates.Customer.Marketplace.csproj
// ═══════════════════════════════════════════════════════════════════════
//  المَتاجِرُ القائِمَةُ الَّتي لا يَقبَلُ مُعَرِّفَها شَكلُ المُعَرِّفِ الواحِد — عَرضٌ فَقَط
// ───────────────────────────────────────────────────────────────────────
//  **لِماذا هذا المِلَفّ** (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 · W13-1·SEC-C1-2؛ «ب» في سِجِلِّ المالِك): صارَ لِأَبوابِ الإنشاءِ الثَلاثَةِ شَكلٌ
//  واحِد (`TenantFromAnalysisFactory.SlugRefusal`: يَبدَأُ بِحَرفٍ أَو رَقَم، ثُمَّ حُروفٌ وأَرقامٌ وشَرطَة، 63 عَلى الأَكثَر، ولا مَحجوزَ ولا قِسمَ
//  قاعِدَة). وقَبلَه كانَ `^[a-z0-9_-]+$` بِلا حَدٍّ ولا قائِمَةِ أَقسام — فَقَد يَكونُ في الإنتاجِ مَتجَرٌ وُلِدَ عَلى غَيرِ الشَكل (`_studio`،
//  `-`، `my_store`)، وعَدَدُه غَيرُ مَقيس: القاعِدَةُ مَحظورَةٌ عَلى الوَكيل.
//
//  **ولا يَكتُبُ شَيئاً**: لا بابَ يُغَيِّرُ مُعَرِّفَ مَتجَر (وَثائِقُه في قِسمٍ بِاسمِه)، فَما يُفعَلُ بِكُلِّ مَتجَرٍ يُطبَعُ هُنا قَرارُ المالِكِ
//  بِحُضورِه — والافتِراضُ إلى أَن يَقول: يُترَكُ كَما هُوَ ويُعرَف. ويُطبَعُ المُعَرِّفُ ورَمزُ الرَدِّ ووُجودُ مالِكٍ وَحدَها — لا جَوّالَ ولا بَريدَ
//  ولا اسم.
//
// ─── الاستِعمال ────────────────────────────────────────────────────────
//   export ConnectionStrings__Postgres="…"        # ولا تُطبَع أَبَداً
//   dotnet run scripts/list-off-shape-store-slugs.cs
//
// ─── رُموزُ الخُروج ────────────────────────────────────────────────────
//   0 — كُلُّ مَتجَرٍ عَلى الشَكل.
//   2 — وُجِدَ مَتجَرٌ عَلى غَيرِ الشَكل (لَم يُكتَب شَيء).
//   1 — خَطَأ، أَو صِفرُ مَتجَرٍ مَفحوص (أَداةٌ عَمياء — القاعِدَة ١٠).
// ═══════════════════════════════════════════════════════════════════════

using System.Security.Cryptography;
using System.Text;
using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using JasperFx;
using Marten;

var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
if (string.IsNullOrWhiteSpace(cs))
{
    Console.Error.WriteLine("خَطَأ: ‏ConnectionStrings__Postgres غَير مَضبوط.");
    return 1;
}
var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cs))).ToLowerInvariant()[..12];

using var store = DocumentStore.For(o =>
{
    o.Connection(cs);
    o.DatabaseSchemaName = "platform";
    o.AutoCreateSchemaObjects = AutoCreate.None;   // ← لا يُمَسُّ المُخَطَّط
    o.Policies.AllDocumentsAreMultiTenanted();
    o.Schema.For<Tenant>().SingleTenanted().Identity(x => x.Id);
});

List<Tenant> tenants;
await using (var qs = store.QuerySession())
    tenants = (await qs.Query<Tenant>().ToListAsync()).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();

Console.WriteLine($"القاعِدَة: بَصمَة {fingerprint} · المَتاجِرُ المَفحوصَة: {tenants.Count}");
Console.WriteLine("الوَضع:   عَرضٌ فَقَط — بِلا كِتابَة");
Console.WriteLine(new string('-', 78));
if (tenants.Count == 0)
{
    Console.Error.WriteLine("✗ BLIND CHECK: صِفرُ مَتجَرٍ مَفحوص — لا يُقال «كُلُّها عَلى الشَكل» عَن قاعِدَةٍ لَم تُقرَأ.");
    return 1;
}

var off = tenants.Select(t => (Tenant: t, Code: TenantFromAnalysisFactory.SlugRefusal(t.Id))).Where(x => x.Code is not null).ToList();
Console.WriteLine($"عَلى غَيرِ الشَكل: {off.Count}");
foreach (var (t, code) in off)
    Console.WriteLine($"  # {t.Id}  ← {code} · مالِك {(t.OwnerUserId == Guid.Empty ? "لا" : "نَعَم")} · مُعَلَّق {(t.IsSuspended ? "نَعَم" : "لا")}");
if (off.Count == 0)
{
    Console.WriteLine("كُلُّ مَتجَرٍ عَلى الشَكل.");
    return 0;
}
Console.WriteLine();
Console.WriteLine("عَرضٌ فَقَط — لَم يُكتَب شَيء. ما يُفعَلُ بِكُلِّ مَتجَرٍ قَرارُ المالِك (لا بابَ يُغَيِّرُ المُعَرِّف).");
return 2;
