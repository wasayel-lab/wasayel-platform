using ACommerce.Kit.Tenants;
using ACommerce.Templates.Customer.Marketplace.Services.Incubator;
using Xunit;
using Xunit.Abstractions;
using static ACommerce.Platform.Tests.LiveStoreBirthOnceCharacterizationTests;

namespace ACommerce.Platform.Tests;

// ═══ استِمارَةُ صاحِبِ المَتجَرِ المُكَرَّرَةُ تُدخِلُه مَتجَرَه (‏2026-11-07، مُراجَعَةُ عَدَساتِ C1 الجَولَةُ الثانِيَة · W13-1·SEC2-C1-1) ═══
//
// **المَقيسُ قَبل** (تَقريرُ العَدَسَة): نَقرَتانِ عَلى «أَنشِئ المَتجَر» بَينَهُما 150ms أَو 400ms، والجَوابُ يَتَأَخَّرُ 1.2 ثانِيَة — يَصِلُ الطَلَبُ الثاني
// بَعدَ أَن حُفِظَ الأَوَّل، فَيَرُدُّه سُؤالُ المُعَرِّفِ بِـ`slug_taken`، والمُتَصَفِّحُ يُبقي الجَوابَ الأَخير: «هذا المُعَرِّف مُستَخدَم مِن قَبل.» عَن مَتجَرِه.
// **والآن**: رَفضٌ عَلى مُعَرِّفٍ مَكتوب — عِندَ سُؤالِ المُعَرِّفِ أَو عِندَ وِلادَةٍ لَم تَقَع — يَسأَلُ `StudioStoreCreate.Repeats`: المَتجَرُ القائِمُ مالِكُه
// صاحِبُ الطَلَبِ واسمُه ولَونُه وسَطرُه ما في الاستِمارَةِ حَرفاً ⇒ لَوحَةُ المَتجَر (`StorePage`) بِلا عَدّ. وما لَيسَ تَكراراً يَبقى رَفضاً بِحَرفِه
// (‏`LiveStoreRepeatCharacterizationTests` ‏24838e59، و`LiveStoreBirthOnceCharacterizationTests` ‏9de318ac — بايتاً قَبلَ التَبديلِ وبَعدَه).

public class StoreRepeatTests(ITestOutputHelper output)
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly StudioStoreCreate.Form Born = new("w13-repeat", "مَتجَري", StudioStoreCreate.DefaultColor, "سَطري");

    private static Tenant Held(Guid owner, string name = "مَتجَري", string color = StudioStoreCreate.DefaultColor, string tagline = "سَطري")
        => new() { Id = Born.Slug, Name = name, BrandColor = color, TagLine = tagline, OwnerUserId = owner };

    /// <summary>الموجَبُ والسالِب (القاعِدَة ٤): التَكرارُ مالِكٌ واحِدٌ واستِمارَةٌ واحِدَةٌ حَرفاً — وكُلُّ فَرقٍ في واحِدٍ مِنها يَجعَلُه طَلَباً آخَر.</summary>
    [Theory]
    [InlineData("same", true)]
    [InlineData("other-owner", false)]
    [InlineData("other-name", false)]
    [InlineData("other-colour", false)]
    [InlineData("other-tagline", false)]
    [InlineData("no-store", false)]
    [InlineData("no-owner", false)]
    public void Only_the_owners_very_same_form_repeats_a_born_store(string sample, bool repeats)
    {
        var (held, owner) = sample switch
        {
            "same" => (Held(Owner), Owner),
            "other-owner" => (Held(Guid.NewGuid()), Owner),
            "other-name" => (Held(Owner, name: "مَتجَرٌ آخَر"), Owner),
            "other-colour" => (Held(Owner, color: "#111111"), Owner),
            "other-tagline" => (Held(Owner, tagline: ""), Owner),
            "no-store" => ((Tenant?)null, Owner),
            "no-owner" => (Held(Guid.Empty), Guid.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(sample)),
        };
        Assert.Equal(repeats, StudioStoreCreate.Repeats(held, owner, Born));
    }

    [Fact]
    public void The_repeat_answer_is_the_store_page_without_the_built_banner()
        => Assert.Equal("/studio/apps/w13-repeat", StudioStoreCreate.StorePage(Born.Slug));

    /// <summary>البابُ يَسأَلُ التَكرارَ بَعدَ الرَفضِ وَحدَه — عَلى <c>slug_taken</c> مِن سُؤالِ المُعَرِّفِ وعَلى وِلادَةٍ لَم تَقَع — وبَعدَ الهُوِيَّةِ والمُوافَقَةِ
    /// والحِصَّة؛ فَلا يُقرَأُ حَقلٌ قَبلَ التَخويلِ ولا يَصيرُ التَكرارُ باباً حَولَ السَقف.</summary>
    [Fact]
    public void The_create_door_asks_the_repeat_only_after_a_refusal_and_after_the_quota()
    {
        var body = WriteEndpointGuardTests.AllMinimalApiEndpoints().Single(e => e.Route == StudioStoreCreate.Endpoint).Body;
        string[] order =
        [
            "tier.CheckBuildAsync(ownerId)",
            "req.Form",
            "slugRefused == Services.Incubator.TenantFromAnalysisFactory.SlugTaken && await factory.RepeatOfAsync(ownerId, form) is { } own",
            "tier.RecordStoreBuiltAsync(ownerId, s => s.Insert(tenant))",
            "birth.Outcome != Services.Incubator.StudioTierService.StoreBirthOutcome.Born && await factory.RepeatOfAsync(ownerId, form) is { } ownStore",
        ];
        var at = order.Select(o => body.IndexOf(o, StringComparison.Ordinal)).ToArray();
        output.WriteLine(string.Join(" ← ", order.Zip(at, (o, p) => $"{o}@{p}")));
        Assert.All(at, p => Assert.True(p >= 0));
        Assert.True(at.Zip(at.Skip(1)).All(p => p.First < p.Second), "سُؤالُ التَكرارِ قَبلَ الحِصَّةِ أَو قَبلَ الرَفض.");
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(body, @"factory\.RepeatOfAsync\(").Count);
        Assert.DoesNotContain("FindAsync(", body, StringComparison.Ordinal);
    }
}

// ─── البُرهانُ عَلى Postgres ────────────────────────────────────────────────────────────────────────────────

[Collection(LiveProofCollection.Name)]
public class LiveStoreRepeatProofTests(ITestOutputHelper output)
{
    private static bool Enabled => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static Task<(int Status, string Location)> CreateAsync(LiveTemplateDoorHost host, Probe p, StudioStoreCreate.Form f)
        => host.PostStudioFormAsync(StudioStoreCreate.Endpoint, new Dictionary<string, string>(), p.Token,
            ("slug", f.Slug), ("name", f.Name), ("color", f.Color), ("tagline", f.TagLine));

    /// <summary>النَقرَةُ الثانِيَةُ بَعدَ أَن حُفِظَت الأُولى — ما قاسَتهُ العَدَسَةُ في المُتَصَفِّح: الأُولى <c>built=1</c>، والثانِيَةُ لَوحَةُ المَتجَر، والعَدّادُ 1،
    /// والمَتجَرُ هُوَ هُوَ (وَقتُ وِلادَتِه لَم يَتَحَرَّك).</summary>
    [Fact]
    public async Task The_owner_resending_the_very_same_form_after_the_store_was_born_lands_in_it_and_counts_once_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = HostWithPhone();
        var a = await UserAsync(host);
        var form = new StudioStoreCreate.Form(NewSlug("w13p-"), "مَتجَري", StudioStoreCreate.DefaultColor, "سَطرُ مَتجَري");
        try
        {
            var first = await CreateAsync(host, a, form);
            var born = (await TenantAsync(host, form.Slug))!.CreatedAt;
            var second = await CreateAsync(host, a, form);
            var t = await TenantAsync(host, form.Slug);
            output.WriteLine($"[live] الأُولى ⇒ {first.Status} {first.Location} · الثانِيَة ⇒ {second.Status} {second.Location} · العَدّاد {await StoresBuiltAsync(host, a)}");
            Assert.Equal((302, $"/studio/apps/{form.Slug}?built=1"), first);
            Assert.Equal((302, StudioStoreCreate.StorePage(form.Slug)), second);
            Assert.Equal(1, await StoresBuiltAsync(host, a));
            Assert.Equal((form.Name, a.User.Id, born), (t!.Name, t.OwnerUserId, t.CreatedAt));
        }
        finally { await CleanAsync(host, [a], form.Slug); }
    }

    /// <summary>والتَكرارُ يَسبِقُ إدراجَ صاحِبِه: الاستِمارَةُ نَفسُها لِصاحِبِها كُتِبَت بَينَ سُؤالِ البابِ وإدراجِه — فَرعُ الوِلادَةِ الَّتي لَم تَقَع ⇒ لَوحَةُ المَتجَر،
    /// ولا يُعَدُّ لِهذا الطَلَبِ شَيء.</summary>
    [Fact]
    public async Task The_owners_very_same_form_written_between_the_check_and_the_insert_lands_in_the_store_and_counts_nothing_on_Postgres()
    {
        if (!Enabled) return;
        var form = new StudioStoreCreate.Form(NewSlug("w13p-"), "مَتجَري", StudioStoreCreate.DefaultColor, "");
        var listener = new LiveStoreBirthRaceProofTests.CompetitorBeforeSave(form.Slug);
        await using var host = HostWithPhone(o => o.Listeners.Add(listener));
        var a = await UserAsync(host);
        listener.Write = async () =>
        {
            await using var s = host.Store.LightweightSession();
            s.Insert(new Tenant { Id = form.Slug, Name = form.Name, BrandColor = form.Color, TagLine = form.TagLine, OwnerUserId = a.User.Id, CreatedAt = DateTime.UtcNow });
            await s.SaveChangesAsync();
        };
        try
        {
            var answer = await CreateAsync(host, a, form);
            output.WriteLine($"[live] أ ⇒ {answer.Status} {answer.Location} · العَدّاد {await StoresBuiltAsync(host, a)}");
            Assert.Equal((302, StudioStoreCreate.StorePage(form.Slug)), answer);
            Assert.Equal(a.User.Id, (await TenantAsync(host, form.Slug))!.OwnerUserId);
            Assert.Equal(0, await StoresBuiltAsync(host, a));
        }
        finally { await CleanAsync(host, [a], form.Slug); }
    }
}
