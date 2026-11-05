using ACommerce.Kit.Auth;
using ACommerce.Kit.Notifications;
using ACommerce.Kit.Tenants;
using ACommerce.Platform.I18n;
using ACommerce.Templates.Customer.Marketplace.Services.Deals;
using Xunit;
using Xunit.Abstractions;

namespace ACommerce.Platform.Tests;

// ═══ طَلَبُ الاستِرجاعِ: مَن يُخبَرُ قَرارٌ واحِدٌ لِلأُنبوبِ والصَفحَة، ومَتجَرٌ بِلا صاحِبٍ يَقولُ «لَم يُخبَر أَحَد» (‏2026-11-05، الموجَة ١٢ G1b — مُراجَعَةُ الإصلاح ١، G1b-R1-1) ═══
//
// قَبلَه (`LiveChatPartnerGoneOwnerlessStoreProofTests` يَطبَعُه ويَبقى بِبَصمَتِه، تَوصيفُه d3d728a): «الإشعاراتُ كُلُّها 0 · return_told غائِب · السِجِلّ «أُخبِرَ البائِع» True ·
// رَدُّ التَكرار «أُخبِرَ البائِع» True»، والتَلميحُ قَبلَ الطَلَبِ «فَتُخبَرُ إدارَةُ المَتجَرِ بِطَلَبِك». والقَرارُ «أ» (P1 — صِدقُ ما تَقولُه المَنصَّةُ لِلمُشتَري): القيمَةُ الثالِثَةُ
// `nobody` في المَعجَمِ المُغلَق، ونَصٌّ يَقولُ إنّ أَحَداً لَم يُخبَر ويُسَمّي بابَ «التَواصُل» في سِياسَةِ خُصوصِيَّةِ المَنصَّة.

public class ChatPartnerGoneOwnerlessStoreRowsTests(ITestOutputHelper output)
{
    private static User Live() => new() { Id = Guid.NewGuid(), TenantSlug = "zz", FullName = "بائِعٌ قائِم" };
    private static User Erased() => AccountDeletion.Erase(Live(), DateTime.UtcNow);

    /// <summary><b>المُوجَب لِـ«لا أَحَد»</b>: بائِعٌ مَمحُوٌّ أَو بِلا وَثيقَةٍ ولا صاحِبَ لِلمَتجَر. <b>والسالِب</b>: البائِعُ القائِمُ يُخبَرُ بِصاحِبٍ وبِغَيرِه، والغائِبُ بِصاحِبٍ ⇐ إدارَةُ المَتجَر.</summary>
    [Fact]
    public void Nobody_is_told_only_when_the_vendor_is_gone_and_the_store_has_no_owner()
    {
        var owner = Guid.NewGuid();
        Assert.Equal(DealNotices.ReturnTold.Nobody, DealNotices.ReturnToldFor(Erased(), null));
        Assert.Equal(DealNotices.ReturnTold.Nobody, DealNotices.ReturnToldFor(null, null));
        Assert.Equal(DealNotices.ReturnTold.Store, DealNotices.ReturnToldFor(Erased(), owner));
        Assert.Equal(DealNotices.ReturnTold.Store, DealNotices.ReturnToldFor(null, owner));
        Assert.Equal(DealNotices.ReturnTold.Vendor, DealNotices.ReturnToldFor(Live(), null));
        Assert.Equal(DealNotices.ReturnTold.Vendor, DealNotices.ReturnToldFor(Live(), owner));
        // ومَتجَرٌ صاحِبُه مُعَرِّفٌ فارِغٌ لا صاحِبَ لَه — `StoreOwnerOf` نَفسُها الَّتي يَقرَؤُها الأُنبوب.
        Assert.Null(DealNotices.StoreOwnerOf(new Tenant { Id = "zz", OwnerUserId = Guid.Empty }));
        Assert.Null(DealNotices.StoreOwnerOf(null));
    }

    /// <summary>المَعجَمُ المُغلَقُ ثَلاثَة، ولِكُلٍّ قيمَتُه ذَهاباً وإياباً؛ والبائِعُ بِلا مَرجِع، وقيمَةٌ خارِجَه تُقرَأُ «البائِع» (ما كُتِبَ قَبلَ المَرجِع).</summary>
    [Fact]
    public void The_return_told_lexicon_has_three_values_and_each_reads_back_from_its_ref()
    {
        Assert.Equal(3, Enum.GetValues<DealNotices.ReturnTold>().Length);
        foreach (var told in Enum.GetValues<DealNotices.ReturnTold>())
        {
            var deal = new Deal { Id = Guid.NewGuid() };
            if (DealNotices.ReturnToldValue(told) is { } value) deal.Refs[DealNotices.ReturnToldRef] = value;
            Assert.Equal(told, DealNotices.ReturnToldOf(deal));
        }
        Assert.Null(DealNotices.ReturnToldValue(DealNotices.ReturnTold.Vendor));
        Assert.Equal("nobody", DealNotices.ReturnToldValue(DealNotices.ReturnTold.Nobody));
        var odd = new Deal { Id = Guid.NewGuid() };
        odd.Refs[DealNotices.ReturnToldRef] = "Nobody";
        Assert.Equal(DealNotices.ReturnTold.Vendor, DealNotices.ReturnToldOf(odd));
    }

    /// <summary>كُلُّ مِفتاحٍ في العائِلَةِ بِبَديلِ «لا أَحَد» حينَ لا يُخبَرُ أَحَد، ومِفتاحٌ مِن خارِجِها كَما هُوَ؛ والسِجِلُّ والتَكرارُ يَقرَآنِ المَرجِع.</summary>
    [Fact]
    public void A_return_key_takes_the_nobody_variant_only_when_nobody_is_told_and_only_inside_its_family()
    {
        foreach (var key in DealNotices.ReturnKeys)
        {
            Assert.Equal(key + DealNotices.NobodyVariant, DealNotices.ReturnKey(key, DealNotices.ReturnTold.Nobody));
            Assert.NotEqual(key + DealNotices.NobodyVariant, DealNotices.ReturnKey(key, DealNotices.ReturnTold.Store));
        }
        Assert.Equal("deals.detail.err_return_window_closed", DealNotices.ReturnKey("deals.detail.err_return_window_closed", DealNotices.ReturnTold.Nobody));
        var nobody = new Deal { Id = Guid.NewGuid() };
        nobody.Refs[DealNotices.ReturnToldRef] = DealNotices.ReturnToldNobody;
        Assert.Equal("deals.detail.event_return_requested_nobody", DealNotices.ReturnEventKey(nobody, studio: false));
        Assert.Equal("studio.deal.event_return_requested_nobody", DealNotices.ReturnEventKey(nobody, studio: true));
        Assert.Equal("deals.detail.err_return_already_requested_nobody", DealNotices.RefusalKeyFor("deals.detail.err_return_already_requested", nobody));
        Assert.Equal("deals.detail.err_return_window_closed", DealNotices.RefusalKeyFor("deals.detail.err_return_window_closed", nobody));
    }

    /// <summary>لِكُلِّ مِفتاحٍ في العائِلَةِ بَديلُ «لا أَحَد» بِنَصٍّ عَرَبِيّ: يَقولُ «لَم يُخبَر»/«لا يُخبَرُ… أَحَد»، ولا يَعِدُ بِبائِعٍ ولا بِإدارَةٍ أُخبِرَت، والثَلاثَةُ الَّتي
    /// يَقرَؤُها المُشتَري تُسَمّي «التَواصُل» في سِياسَةِ الخُصوصِيَّة. يَطبَعُ ما فَحَصَه ويَفشَلُ عَلى صِفر.</summary>
    [Fact]
    public void Every_return_key_has_a_nobody_variant_that_says_nobody_was_told_and_the_buyer_ones_name_the_privacy_contact()
    {
        var checkedKeys = 0;
        var buyerFacing = 0;
        foreach (var key in DealNotices.ReturnKeys)
        {
            var text = LocaleCatalog.Find(LocaleCatalog.Arabic, key + DealNotices.NobodyVariant);
            Assert.False(string.IsNullOrWhiteSpace(text), key + DealNotices.NobodyVariant);
            Assert.True(text!.Contains("لَم يُخبَر", StringComparison.Ordinal) || text.Contains("لا يُخبَرُ بِطَلَبِكَ أَحَد", StringComparison.Ordinal), key);
            Assert.DoesNotContain("أُخبِرَ البائِع", text, StringComparison.Ordinal);
            Assert.DoesNotContain("فَأُخبِرَت إدارَةُ المَتجَر", text, StringComparison.Ordinal);
            Assert.DoesNotContain("فَتُخبَرُ إدارَةُ المَتجَر", text, StringComparison.Ordinal);
            if (!key.StartsWith("studio.", StringComparison.Ordinal))
            {
                Assert.Contains("«التَواصُل» في سِياسَةِ خُصوصِيَّتِها", text, StringComparison.Ordinal);
                buyerFacing++;
            }
            checkedKeys++;
        }
        // والبابُ الَّذي يُسَمّيه النَصُّ قائِمٌ بِاسمِه في سِياسَةِ المَنصَّة.
        Assert.Contains("التَواصُل", LocaleCatalog.Find(LocaleCatalog.Arabic, "platform.privacy.s10_title")!, StringComparison.Ordinal);
        output.WriteLine($"بَدائِلُ «لا أَحَد» مَفحوصَة: {checkedKeys} · تُسَمّي «التَواصُل»: {buyerFacing}");
        Assert.Equal((4, 3), (checkedKeys, buyerFacing));
    }

    /// <summary>الشَكل: الأُنبوبُ يَسأَلُ القَرارَ الواحِدَ بِوَثيقَةِ البائِعِ وصاحِبِ المَتجَرِ قَبلَ أَيِّ إشعار، ويُنزِلُه «لا أَحَد» حينَ لا يَكتُبُ إشعارُ صاحِبِ المَتجَر، ويُلحِقُ المَرجِعَ
    /// بِكاتِبٍ واحِد؛ والصَفحَةُ تَسأَلُ القَرارَ نَفسَه بِبائِعِ <c>ReturnRecipient</c> وبِصاحِبِ المَتجَرِ مِن سِجِلِّ المُستَأجِرين، والتَلميحُ مِنه.</summary>
    [Fact]
    public void The_pipe_and_the_page_ask_the_one_return_told_decision()
    {
        var root = Path.Combine(ThemeZeroEquivalenceTests.RepoRoot, "libs", "templates", "ACommerce.Templates.Customer.Marketplace");
        var src = WriteEndpointGuardTests.StripComments(File.ReadAllText(Path.Combine(root, "Services", "Deals", "DealNotices.cs")));
        var pipe = src[src.IndexOf("public async Task<bool> NotifyReturnRequestedAsync(", StringComparison.Ordinal)..];
        pipe = pipe[..pipe.IndexOf("return true;\n    }", StringComparison.Ordinal)];
        int At(string s, string what) => s.IndexOf(what, StringComparison.Ordinal);
        var decide = At(pipe, "told = ReturnToldFor(await q.LoadAsync<ACommerce.Kit.Auth.User>(vendorId, ct), StoreOwnerOf(await q.LoadAsync<ACommerce.Kit.Tenants.Tenant>(slug, ct)));");
        var fallback = At(pipe, "if (told == ReturnTold.Store && !await NotifyStoreOwnerAsync(slug, deal, OwnerAction.ReturnVendorGone, l, ct)) told = ReturnTold.Nobody;");
        var attach = At(pipe, "await deals.AttachRefAsync(slug, deal.Id, ReturnToldRef, value, ct);");
        var vendor = At(pipe, "ReturnRequestedFor(slug, deal, vendorId, l, DateTime.UtcNow)");
        output.WriteLine($"القَرار {decide} · النُزولُ إلى «لا أَحَد» {fallback} · المَرجِع {attach} · إشعارُ البائِع {vendor}");
        Assert.True(decide > 0 && fallback > decide && attach > fallback && vendor > attach, "التَرتيب: القَرارُ ثُمَّ صاحِبُ المَتجَرِ ثُمَّ المَرجِعُ، وإشعارُ البائِعِ بَعدَها.");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(src, @"AttachRefAsync\(slug, deal\.Id, ReturnToldRef,"));

        var page = WriteEndpointGuardTests.StripMarkupComments(File.ReadAllText(Path.Combine(root, "Components", "Pages", "MyDealDetail.razor")));
        Assert.Contains("@inject Services.Queries.TenantDirectory Tenants", page, StringComparison.Ordinal);
        Assert.Contains("DealNotices.ReturnToldFor(await Accounts.ProfileAsync(Slug, returnVendor), DealNotices.StoreOwnerOf(await Tenants.FindAsync(Slug)));", page, StringComparison.Ordinal);
        Assert.Contains("if (isInitiator && DealNotices.ReturnRecipient(deal) is { } returnVendor)", page, StringComparison.Ordinal);
        Assert.Contains("@L.Format(DealNotices.ReturnKey(\"deals.detail.return_hint\", returnTold),", page, StringComparison.Ordinal);
        Assert.DoesNotContain("ReturnKey(\"deals.detail.return_hint\", vendorGone)", page, StringComparison.Ordinal);
    }
}

[Collection(LiveProofCollection.Name)]
public class LiveChatPartnerGoneOwnerlessStoreSwitchProofTests(ITestOutputHelper output)
{
    private static bool Enabled
        => LiveDealConcurrencyProofTests.Enabled && !string.IsNullOrEmpty(LiveDealConcurrencyProofTests.ConnectionString);

    private static string Text(string key) => LiveChatPartnerGoneOwnerlessStoreProofTests.Text(key);

    /// <summary>قُلِبَ ما طَبَعَه التَوصيف («الإشعاراتُ كُلُّها 0 · return_told غائِب · السِجِلّ «أُخبِرَ البائِع» True» والتَلميحُ يَعِدُ بِإدارَةِ المَتجَر).
    /// <b>المُوجَب</b> — مَتجَرٌ بِلا صاحِبٍ والبائِعُ مَمحُوّ: التَلميحُ قَبلَ الطَلَبِ بَديلُ «لا أَحَد» لا بَديلُ المَتجَر؛ والبابُ لا يَكتُبُ إشعاراً لِأَحَدٍ ويُلحِقُ
    /// <c>return_told=nobody</c>؛ والسِجِلُّ ورَدُّ التَكرارِ بِبَديلِهِما، ولا «أُخبِرَ البائِع».</summary>
    [Fact]
    public async Task In_a_store_with_no_owner_and_the_vendor_gone_the_deal_says_nobody_was_told_and_names_the_privacy_contact_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var gone = await LiveChatPartnerGoneOwnerlessStoreProofTests.SeedAsync(host.Store, vendorGone: true);
        try
        {
            var r = await LiveChatPartnerGoneOwnerlessStoreProofTests.RunAsync(host, gone);
            output.WriteLine($"مَتجَرٌ بِلا صاحِب · البائِعُ مَمحُوّ: قَبلَ الطَلَب: تَلميحُ «لا أَحَد» {r.Before.Contains("فَلا يُخبَرُ بِطَلَبِكَ أَحَدٌ")} · "
                + $"تَلميحُ المَتجَر {r.Before.Contains("فَتُخبَرُ إدارَةُ المَتجَرِ بِطَلَبِك")} · البابُ: {r.Status} → {r.Location} · الإشعاراتُ كُلُّها {r.Notices.Count} · "
                + $"return_told {(r.Deal.Refs.TryGetValue(DealNotices.ReturnToldRef, out var told) ? told : "غائِب")} · "
                + $"السِجِلّ «{(r.After.Contains(Text("deals.detail.event_return_requested_nobody")) ? Text("deals.detail.event_return_requested_nobody") : "؟")}» · "
                + $"رَدُّ التَكرار «لا أَحَد» {r.After.Contains(Text("deals.detail.err_return_already_requested_nobody"))} · «أُخبِرَ البائِع» {r.After.Contains("أُخبِرَ البائِع")}");
            Assert.Contains("فَلا يُخبَرُ بِطَلَبِكَ أَحَدٌ", r.Before, StringComparison.Ordinal);
            Assert.DoesNotContain("فَتُخبَرُ إدارَةُ المَتجَرِ بِطَلَبِك", r.Before, StringComparison.Ordinal);
            Assert.Equal((302, $"/{gone.Slug}/deals/{gone.Deal.Id}"), (r.Status, r.Location));
            Assert.Empty(r.Notices);
            Assert.Equal(DealNotices.ReturnToldNobody, r.Deal.Refs[DealNotices.ReturnToldRef]);
            Assert.Contains(Text("deals.detail.event_return_requested_nobody"), r.After, StringComparison.Ordinal);
            Assert.Contains(Text("deals.detail.err_return_already_requested_nobody"), r.After, StringComparison.Ordinal);
            Assert.DoesNotContain("أُخبِرَ البائِع", r.After, StringComparison.Ordinal);
            Assert.DoesNotContain("أُخبِرَت إدارَةُ المَتجَر", r.After, StringComparison.Ordinal);
        }
        finally { await LiveChatPartnerGoneReturnProofTests.CleanAsync(host.Store, gone.Slug); }
    }

    /// <summary><b>السالِب</b> — المَتجَرُ نَفسُه بِلا صاحِبٍ والبائِعُ قائِم: يُخبَرُ البائِعُ وَحدَه، ولا مَرجِع، ولا نَصَّ «لا أَحَد» قَبلَ الطَلَبِ ولا بَعدَه؛ ومَتجَرٌ <b>بِصاحِبٍ</b> والبائِعُ مَمحُوٌّ
    /// يَبقى «إدارَةُ المَتجَر» (<c>return_told=store</c>) لا «لا أَحَد».</summary>
    [Fact]
    public async Task A_live_vendor_in_a_store_with_no_owner_and_an_erased_vendor_in_a_store_with_one_never_read_nobody_on_Postgres()
    {
        if (!Enabled) return;
        await using var host = LiveTemplateDoorHost.Build();
        var live = await LiveChatPartnerGoneOwnerlessStoreProofTests.SeedAsync(host.Store, vendorGone: false);
        var owned = await LiveChatPartnerGoneReturnProofTests.SeedAsync(host.Store, vendorGone: true);
        try
        {
            var l = await LiveChatPartnerGoneOwnerlessStoreProofTests.RunAsync(host, live);
            var o = await LiveChatPartnerGoneOwnerlessStoreProofTests.RunAsync(host, owned);
            output.WriteLine($"بِلا صاحِب · البائِعُ قائِم: لِلبائِع {l.Notices.Count(n => n.UserId == live.Vendor.Id)} · الإشعاراتُ كُلُّها {l.Notices.Count} · مَرجِعُ «مَن أُخبِر» {l.Deal.Refs.ContainsKey(DealNotices.ReturnToldRef)} · "
                + $"«لا أَحَد» قَبلُ {l.Before.Contains("لَم يُخبَر") || l.Before.Contains("فَلا يُخبَرُ بِطَلَبِكَ أَحَدٌ")} بَعدُ {l.After.Contains("لَم يُخبَر")}");
            output.WriteLine($"بِصاحِب · البائِعُ مَمحُوّ: لِصاحِبِ المَتجَر {o.Notices.Count(n => n.UserId == owned.Owner)} · return_told {o.Deal.Refs.GetValueOrDefault(DealNotices.ReturnToldRef)} · "
                + $"«لا أَحَد» {o.Before.Contains("فَلا يُخبَرُ بِطَلَبِكَ أَحَدٌ") || o.After.Contains("لَم يُخبَر")}");
            var n = Assert.Single(l.Notices);
            Assert.Equal(live.Vendor.Id, n.UserId);
            Assert.False(l.Deal.Refs.ContainsKey(DealNotices.ReturnToldRef));
            Assert.DoesNotContain("فَلا يُخبَرُ بِطَلَبِكَ أَحَدٌ", l.Before, StringComparison.Ordinal);
            Assert.DoesNotContain("لَم يُخبَر", l.After, StringComparison.Ordinal);
            Assert.Single(o.Notices, x => x.UserId == owned.Owner);
            Assert.Equal(DealNotices.ReturnToldStore, o.Deal.Refs[DealNotices.ReturnToldRef]);
            Assert.Contains("فَتُخبَرُ إدارَةُ المَتجَرِ بِطَلَبِك", o.Before, StringComparison.Ordinal);
            Assert.DoesNotContain("لَم يُخبَر", o.After, StringComparison.Ordinal);
        }
        finally
        {
            await LiveChatPartnerGoneReturnProofTests.CleanAsync(host.Store, live.Slug);
            await LiveChatPartnerGoneReturnProofTests.CleanAsync(host.Store, owned.Slug);
        }
    }
}
