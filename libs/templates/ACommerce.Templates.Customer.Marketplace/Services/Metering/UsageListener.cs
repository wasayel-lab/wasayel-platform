using ACommerce.Kit.Auth;
using ACommerce.Kit.Chat;
using ACommerce.Kit.Reviews;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.Shared.Metering;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Marten;
using Marten.Services;

namespace ACommerce.Templates.Customer.Marketplace.Services.Metering;

/// <summary>
/// <para><b>مُستَمِعُ الحِفظِ — مُحَوِّلٌ رَقيق</b> (‏ADR-051، التَصميم §5.1). في <see cref="BeforeSaveChangesAsync"/>: يُثَبِّتُ التَغييراتِ المُعَلَّقَة،
/// ويَسأَلُ المُصَنِّفَ النَقِيَّ (<see cref="MeterRules"/>)، ويَضمَنُ الجَدوَلَ بِنِداءٍ مُخَبَّأ، ويُرَحِّلُ جُملَةَ إدخالٍ لِكُلِّ سَطر
/// (<c>QueueSqlCommand</c>) — <b>فَتُودَعُ في مُعامَلَةِ الكِتابَةِ نَفسِها</b> (مَقيس D2: <c>writer_xid</c> السَطرِ = <c>xmin</c> صَفِّ العَمَل)
/// وتَتَراجَعُ مَعَها (D3).</para>
/// <para><b>لا يَقرَأُ حالَةً مَخزونَة</b> (القاعِدَة ٧ — قابِلٌ لِلفَحصِ بِالتَوقيع): التَغييراتُ المُعَلَّقَةُ والحامِلُ وَحدَهُما. والنِداءاتُ المَسموحَةُ
/// مُسَمّاةٌ في <c>MeterListenerShapeTests</c>: <c>PendingChanges</c> و<c>VersionFor</c> و<c>TenantId</c> و<c>QueueSqlCommand</c>
/// و<c>Database.EnsureStorageExistsAsync</c> — والأَخيرُ <b>اللَمسَةُ الوَحيدَةُ المَأذونَةُ لِلقاعِدَةِ في مَسارِ الحِفظ</b> (RULES-18): يُخَبِّئُه
/// Marten لِكُلِّ عَمَلِيَّةٍ فَلا يَعمَلُ بَعدَ <c>MeterBoot</c>، وهُوَ البَديلُ لِتَركيبٍ لا يَمُرُّ بِـ<c>Program.cs</c>.</para>
/// <para><b>ولا مِفتاحَ إطفاء</b> (§5.5): يَفتَحُ الكِتابَةَ بِلا عَدٍّ الَّتي طَلَبَ المالِكُ إغلاقَها.</para>
/// <para>‏(‏2026-11-12، W13-6·٧ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>والمَجاري المُعَلَّقَةُ مُدخَلٌ ثالِث</b>: <c>PendingChanges.Streams()</c> — مِن التَغييراتِ
/// المُعَلَّقَةِ نَفسِها، فَلا نِداءَ جَلسَةٍ جَديد — قِيَماً بِـ<see cref="MeterRules.StreamsOf"/>، فَيَعُدُّ المُصَنِّفُ الحَدَثَ الَّذي يَبدَأُ بِه مَجراه
/// (وَحَداتُ الأَحداثِ الأَربَع). ويَخرُجُ مُبَكِّراً حينَ لا صَفقَةَ ولا مَجرىً يَبدَأ.</para>
/// <para>‏(‏2026-11-12، W13-6·٨ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>والوَثائِقُ المَعدودَةُ بِشَكلِ وِلادَتِها</b> (دَفتَرُ التَصنيف): المُتَفائِلَةُ — <c>Deal</c>
/// و<c>User</c> و<c>StudioUser</c> — مِن <c>Updates</c> و<c>Inserts</c> مَعاً و«وِلادَةٌ» = <c>VersionFor == null</c> (D6)؛ وما بِلا تَزامُنٍ تَفاؤُلِيٍّ —
/// <c>Tenant</c> و<c>Review</c> — مِن <c>Inserts</c> وَحدَها، فَـ<c>Store</c> مِنها (تَحديثٌ أَو كِتابَةُ نَسخٍ قائِم) لا يَبلُغُ المُصَنِّفَ أَصلاً: نُسخَتُها لا
/// تُمَيِّزُ الوِلادَة. ولِذلكَ قُلِبَت وِلاداتُ <c>Tenant</c> الثَلاثُ في البَذرِ <c>Insert</c>. ويَخرُجُ مُبَكِّراً حينَ لا وِلادَةَ ولا مَجرىً يَبدَأ.</para>
/// <para>‏(‏2026-11-12، W13-6·٩ — يُلحَقُ ولا يُعادُ حَرفٌ فَوقَه) <b>و<c>Message</c> مِن <c>Inserts</c> وَحدَها</b> كَـ<c>Tenant</c> و<c>Review</c>: لا تَزامُنَ تَفاؤُلِيّاً
/// لَها، فَقُلِبَ <c>Store</c>ها <c>Insert</c> في بابِ الإرسالِ وفي البَذرَين (خَلفَ تَوصيفِ البابِ <c>LiveChatMessageSendCharacterizationProofTests</c>).</para>
/// </summary>
public sealed class UsageListener : DocumentSessionListenerBase
{
    private int _generation;

    /// <summary>الجيلُ الَّذي خَبَّأَه الإقلاعُ لِهذا المَخزَن (<c>MeterBoot</c>)؛ و<c>0</c> قَبلَه — فَيَأخُذُ الإدخالُ آخِرَ جيلٍ مَفتوح (<see cref="MeterSql.Insert"/>).</summary>
    public int Generation => Volatile.Read(ref _generation);

    internal void CacheGeneration(int generation) => Volatile.Write(ref _generation, generation);

    public override async Task BeforeSaveChangesAsync(IDocumentSession session, CancellationToken token)
    {
        var changes = session.PendingChanges;
        var documents = new List<PendingDocument>();
        Optimistic(changes.UpdatesFor<Deal>().Concat(changes.InsertsFor<Deal>()));
        Optimistic(changes.UpdatesFor<User>().Concat(changes.InsertsFor<User>()));
        Optimistic(changes.UpdatesFor<StudioUser>().Concat(changes.InsertsFor<StudioUser>()));
        documents.AddRange(changes.InsertsFor<Tenant>().Select(t => new PendingDocument(t, Created: true)));
        documents.AddRange(changes.InsertsFor<Review>().Select(r => new PendingDocument(r, Created: true)));
        documents.AddRange(changes.InsertsFor<Message>().Select(m => new PendingDocument(m, Created: true)));
        var streams = MeterRules.StreamsOf(changes.Streams());
        if (!documents.Any(d => d.Created) && !streams.Any(s => s.Started)) return;

        var pending = new PendingSnapshot(session.TenantId, documents, streams);
        var entries = MeterRules.Classify(pending, MeterScope.View(), MeterUnitCatalog.Default);
        if (entries.Count == 0) return;

        await session.Database.EnsureStorageExistsAsync(typeof(UsageLedgerFeature), token);
        var generation = Generation;
        foreach (var e in entries)
            session.QueueSqlCommand(MeterSql.Insert,
                e.Id, e.Unit, e.Quantity, e.App, e.Entrepreneur, e.Role, e.Feature, e.Provider, e.SourceType, e.SourceId, generation);

        // وَثيقَةٌ بِتَزامُنٍ تَفاؤُلِيّ: «وِلادَةٌ» = لا نُسخَةَ لَها في الجَلسَة (مَقيس D6).
        void Optimistic<T>(IEnumerable<T> docs) where T : notnull
        {
            foreach (var d in docs) documents.Add(new PendingDocument(d, session.VersionFor(d) is null));
        }
    }
}
