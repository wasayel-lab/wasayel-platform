using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ تَوصيفُ ما يَجِبُ أَن يَنجوَ حينَ تَصيرُ استِمارَةُ صاحِبِ المَتجَرِ المُكَرَّرَةُ دُخولاً إلى مَتجَرِه (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 الجَولَةُ الثانِيَة · W13-1·SEC2-C1-1) ═══
//
// **المَقيسُ قَبل** (تَقريرُ العَدَسَة، Chromium خَلفَ وَكيلٍ يُؤَخِّرُ جَوابَ الإنشاءِ 1.2 ثانِيَة): صاحِبٌ وافَقَ نَقَرَ «أَنشِئ المَتجَر» مَرَّتَينِ بَينَهُما
// 150ms ثُمَّ 400ms — خَرَجَ طَلَبان، ووَصَلَ الثاني بَعدَ أَن حُفِظَ الأَوَّل، فَرَدَّه سُؤالُ `ValidateSlugAsync` بِـ`slug_taken`. والمُتَصَفِّحُ يُبقي الجَوابَ
// الأَخيرَ: انتَهى عَلى `/studio/new?err=slug_taken&slug=mz150…` بِرِسالَةِ «هذا المُعَرِّف مُستَخدَم مِن قَبل.» — عَن مَتجَرِه هُوَ (مالِكُه هُوَ، والعَدّادُ 1).
// فَإن أَعادَ بِمُعَرِّفٍ آخَرَ أَنفَقَ مَتجَراً ثانِياً مِن عَدّادٍ مَدى الحَياة.
//
// **وما يُثَبِّتُه هذا المِلَفُّ — وهُوَ ما يَجِبُ أَن يَنجوَ مِن الإصلاح، ولا يُعادُ حَرفٌ مِنه بَعدَه** (القاعِدَة ٣): **ما لَيسَ تَكراراً يَبقى رَفضاً بِحَرفِه.**
// (١) مالِكٌ آخَرُ يُرسِلُ الاستِمارَةَ نَفسَها حَرفاً ⇒ `slug_taken`؛ (٢) الصاحِبُ نَفسُه بِالاسمِ نَفسِه وسَطرٍ أَو لَونٍ آخَر ⇒ `slug_taken`؛ (٣) مُنافِسٌ يَكتُبُ
// بَينَ سُؤالِ البابِ وإدراجِه — مالِكٌ آخَرُ بِالاستِمارَةِ نَفسِها، أَو الصاحِبُ نَفسُه بِاسمٍ آخَر ⇒ `slug_taken` والعَدّادُ صِفر. والمَتجَرُ القائِمُ في كُلِّها
// كَما كُتِب. وأَخَواتُه مُثَبَّتَةٌ قَبلَه ولا تُمَسّ: الصاحِبُ بِاسمٍ آخَرَ مُتَتابِعاً (`LiveStoreBirthOnceCharacterizationTests`)، ومالِكٌ آخَرُ بِاسمٍ آخَرَ مُتَتابِعاً
// ومُتَسابِقاً (هُوَ و`LiveStoreBirthRaceProofTests`)، ومَن بَلَغَ السَقفَ يُرَدُّ قَبلَ أَن يُقرَأَ حَقل (`LiveStoreQuotaCharacterizationTests`).
// يَتَخَطّى نَفسَه ما لَم تُضبَط WASAYEL_LIVE_PROOF=1 وConnectionStrings__Postgres — عَقدُ إخوَتِه، وفي مَجموعَتِهِم التَسلسُلِيَّة.

[Collection(LiveProofCollection.Name)]
public class LiveStoreRepeatCharacterizationTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static Task<(int Status, string Location)> CreateAsync(LiveTemplateDoorHost host, Probe p, StudioStoreCreate.Form f)
        => host.PostStudioFormAsync(StudioStoreCreate.Endpoint, new Dictionary<string, string>(), p.Token,
            ("slug", f.Slug), ("name", f.Name), ("color", f.Color), ("tagline", f.TagLine));

    private static Func<Task> Competitor(LiveTemplateDoorHost host, Tenant written) => async () =>
    {
        await using var s = host.Store.LightweightSession();
        s.Insert(written);
        await s.SaveChangesAsync();
    };

    /// <summary>مالِكٌ آخَرُ بِالاستِمارَةِ نَفسِها حَرفاً: <c>slug_taken</c>، والمَتجَرُ لِصاحِبِه، والعَدّادان 1 و0.</summary>
    [Fact]
    public async Task Another_owner_sending_the_very_same_form_is_slug_taken_and_counts_nothing_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        var b = await UserAsync(host);
        var form = new StudioStoreCreate.Form(NewSlug("w13s-"), "مَتجَرٌ واحِد", StudioStoreCreate.DefaultColor, "");
        try
        {
            var first = await CreateAsync(host, a, form);
            var second = await CreateAsync(host, b, form);
            var t = await TenantAsync(host, form.Slug);
            output.WriteLine($"[live] أ ⇒ {first.Location} · ب بِالاستِمارَةِ نَفسِها ⇒ {second.Location} · المالِكُ أ={t?.OwnerUserId == a.User.Id} · العَدّادان {await StoresBuiltAsync(host, a)}/{await StoresBuiltAsync(host, b)}");
            Assert.Equal((302, $"/studio/apps/{form.Slug}?built=1"), first);
            Assert.Equal((302, StudioStoreCreate.Refused(TenantFromAnalysisFactory.SlugTaken, form)), second);
            Assert.Equal((form.Name, a.User.Id), (t!.Name, t.OwnerUserId));
            Assert.Equal((1, 0), (await StoresBuiltAsync(host, a), await StoresBuiltAsync(host, b)));
        }
        finally { await CleanAsync(host, [a, b], form.Slug); }
    }

    /// <summary>الصاحِبُ نَفسُه بِالاسمِ نَفسِه وسَطرٍ آخَر، ثُمَّ بِلَونٍ آخَر: كُلٌّ <c>slug_taken</c> بِما كُتِب، والمَتجَرُ كَما وُلِدَ، والعَدّادُ 1.</summary>
    [Fact]
    public async Task The_owner_resending_the_name_with_another_tagline_or_colour_is_slug_taken_and_counts_once_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        var born = new StudioStoreCreate.Form(NewSlug("w13s-"), "مَتجَري", StudioStoreCreate.DefaultColor, "");
        var tagline = born with { TagLine = "سَطرٌ جَديد" };
        var colour = born with { Color = "#111111" };
        try
        {
            var first = await CreateAsync(host, a, born);
            var byTagline = await CreateAsync(host, a, tagline);
            var byColour = await CreateAsync(host, a, colour);
            var t = await TenantAsync(host, born.Slug);
            output.WriteLine($"[live] الأُولى ⇒ {first.Location} · بِسَطرٍ آخَر ⇒ {byTagline.Location} · بِلَونٍ آخَر ⇒ {byColour.Location} · العَدّاد {await StoresBuiltAsync(host, a)}");
            Assert.Equal((302, $"/studio/apps/{born.Slug}?built=1"), first);
            Assert.Equal((302, StudioStoreCreate.Refused(TenantFromAnalysisFactory.SlugTaken, tagline)), byTagline);
            Assert.Equal((302, StudioStoreCreate.Refused(TenantFromAnalysisFactory.SlugTaken, colour)), byColour);
            Assert.Equal((born.Name, born.Color, born.TagLine, a.User.Id), (t!.Name, t.BrandColor, t.TagLine, t.OwnerUserId));
            Assert.Equal(1, await StoresBuiltAsync(host, a));
        }
        finally { await CleanAsync(host, [a], born.Slug); }
    }

    /// <summary>مُنافِسٌ — مالِكٌ آخَرُ بِالاستِمارَةِ نَفسِها — يَكتُبُ بَينَ سُؤالِ البابِ وإدراجِه: صاحِبُ الطَلَبِ يُجابُ <c>slug_taken</c>، والمَتجَرُ لِلمُنافِس، وعَدّادُه صِفر.</summary>
    [Fact]
    public async Task Another_owner_writing_the_very_same_form_between_the_check_and_the_insert_wins_and_the_door_answers_slug_taken_on_Postgres()
    {
        if (!Enabled) return;
        var form = new StudioStoreCreate.Form(NewSlug("w13s-"), "مَتجَرٌ واحِد", StudioStoreCreate.DefaultColor, "");
        var listener = new LiveStoreBirthRaceProofTests.CompetitorBeforeSave(form.Slug);
        await using var host = HostWithPhone(o => o.Listeners.Add(listener));
        var a = await UserAsync(host);
        var other = Guid.NewGuid();
        listener.Write = Competitor(host, new Tenant
            { Id = form.Slug, Name = form.Name, BrandColor = form.Color, TagLine = form.TagLine, OwnerUserId = other, CreatedAt = DateTime.UtcNow });
        try
        {
            var answer = await CreateAsync(host, a, form);
            var t = await TenantAsync(host, form.Slug);
            output.WriteLine($"[live] أ ⇒ {answer.Status} {answer.Location} · المالِكُ المُنافِس={t?.OwnerUserId == other} · عَدّادُ أ {await StoresBuiltAsync(host, a)}");
            Assert.Equal((302, StudioStoreCreate.Refused(TenantFromAnalysisFactory.SlugTaken, form)), answer);
            Assert.Equal(other, t!.OwnerUserId);
            Assert.Equal(0, await StoresBuiltAsync(host, a));
        }
        finally { await CleanAsync(host, [a], form.Slug); }
    }

    /// <summary>والصاحِبُ نَفسُه بِاسمٍ آخَرَ كَتَبَ بَينَ سُؤالِ البابِ وإدراجِه: الطَلَبُ يُجابُ <c>slug_taken</c> بِما كُتِب، والمَتجَرُ بِاسمِه الأَوَّل، والعَدّادُ صِفر.</summary>
    [Fact]
    public async Task The_owner_racing_a_store_of_another_name_is_slug_taken_and_counts_nothing_on_Postgres()
    {
        if (!Enabled) return;
        var form = new StudioStoreCreate.Form(NewSlug("w13s-"), "الاسمُ الجَديد", StudioStoreCreate.DefaultColor, "");
        var listener = new LiveStoreBirthRaceProofTests.CompetitorBeforeSave(form.Slug);
        await using var host = HostWithPhone(o => o.Listeners.Add(listener));
        var a = await UserAsync(host);
        listener.Write = Competitor(host, new Tenant
            { Id = form.Slug, Name = "الاسمُ الأَوَّل", BrandColor = form.Color, TagLine = form.TagLine, OwnerUserId = a.User.Id, CreatedAt = DateTime.UtcNow });
        try
        {
            var answer = await CreateAsync(host, a, form);
            var t = await TenantAsync(host, form.Slug);
            output.WriteLine($"[live] أ ⇒ {answer.Status} {answer.Location} · الاسم «{t?.Name}» · العَدّاد {await StoresBuiltAsync(host, a)}");
            Assert.Equal((302, StudioStoreCreate.Refused(TenantFromAnalysisFactory.SlugTaken, form)), answer);
            Assert.Equal(("الاسمُ الأَوَّل", a.User.Id), (t!.Name, t.OwnerUserId));
            Assert.Equal(0, await StoresBuiltAsync(host, a));
        }
        finally { await CleanAsync(host, [a], form.Slug); }
    }
}
