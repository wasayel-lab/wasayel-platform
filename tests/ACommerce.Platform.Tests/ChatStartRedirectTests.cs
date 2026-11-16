using System.Text.RegularExpressions;
using Xunit;

namespace ACommerce.Platform.Tests;

// ═══ أَنابيبُ بَدءِ المُحادَثَةِ تَرتَدُّ إلى صَفحَةٍ مَوجودَة ═════════════
//
// **المَقيسُ** (‏2026-10-11، أَثناءَ سَدِّ P0-5): `POST /{slug}/vendor/{vendorId}/chat`
// — زِرُّ «تَواصَل مَع البائِع» في `VendorProfile.razor` — كانَ يُنشِئُ
// المُحادَثَةَ ثُمَّ يَرتَدُّ إلى `chat/{id}`، **ومِفرَدُ `chat` لا صَفحَةَ لَه**:
// صِفرُ `@page` يَبدَأُ بِـ`/{slug}/chat/` في المُستَودَع، وغُرفَةُ المُحادَثَةِ
// مُسَجَّلَةٌ بِـ`/{slug}/chats/{conversationId:guid}` (`ChatRoom.razor:1`).
// فَالنَقرَةُ كانَت تَكتُبُ وَثيقَةً وتُسقِطُ الناقِرَ عَلى «غَيرُ مَوجود» —
// بابٌ يَفتَحُ عَلى لا شَيء (القاعِدَة ١٢)، ولا فاحِصَ يَراه: `PinnedRoutes`
// يُثَبِّتُ المَسارَ لا وِجهَةَ ارتِدادِه.
//
// وأُنبوبا البَدءِ الآخَران (`listings/{id}/chat` و`users/{userId}/chat`) كانا
// يَرتَدّانِ إلى `chats/` أَصلاً — فَالعَطَبُ في واحِدٍ مِن ثَلاثَة، وهذا
// المِلَفُّ يَحرُسُ الثَلاثَةَ مَعاً: **كُلُّ ارتِدادٍ إلى غُرفَةٍ يُسَمّي
// المَسارَ المُسَجَّل**، ويَعُدُّ ما فَحَصَ قَبلَ أَن يَقولَ «سَليم» (القاعِدَة ١٠).
public class ChatStartRedirectTests
{
    private static string RepoRoot => ThemeZeroEquivalenceTests.RepoRoot;

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    private static string Endpoints()
    {
        var text = Read("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "MarketplaceTemplateExtensions.cs");
        Assert.True(text.Length > 5000, "أَداةٌ عَمياء: مِلَفُّ النِقاطِ لَم يُقرَأ.");
        return text;
    }

    private static readonly Regex ChatRoomRedirect =
        new(@"Results\.Redirect\(Link\(req, slug, \$""chats/\{", RegexOptions.Compiled);

    private static readonly Regex SingularChatRedirect =
        new(@"\$""chat/\{", RegexOptions.Compiled);

    /// <summary><b>هذا هُوَ الفَحصُ الَّذي كانَ سَيَحمَرُّ قَبلَ هذا الكوميت</b>:
    /// ارتِدادانِ إلى <c>chat/{…}</c> في أُنبوبِ البائِع. والعَدُّ يُطبَعُ —
    /// صِفرُ <c>chats/</c> يَعني أَداةً عَمياء لا مُستَودَعاً سَليماً.</summary>
    [Fact]
    public void Every_redirect_into_a_chat_room_names_the_registered_plural_route()
    {
        var text = Endpoints();
        var plural = ChatRoomRedirect.Matches(text).Count;
        var singular = SingularChatRedirect.Matches(text).Count;

        Assert.True(plural >= 3, $"أَداةٌ عَمياء: {plural} ارتِداداً إلى chats/ فَقَط — والمَعروفُ ثَلاثَةُ أَنابيبِ بَدءٍ عَلى الأَقَلّ.");
        Assert.True(singular == 0,
            $"{singular} ارتِداداً إلى `chat/{{…}}` — مَسارٌ لا صَفحَةَ لَه؛ الغُرفَةُ `chats/{{conversationId}}`.");
    }

    /// <summary>والمَسارُ الَّذي تَرتَدُّ إلَيه الأَنابيبُ <b>مُسَجَّلٌ فِعلاً</b>
    /// — بِالماسِحِ الواحِدِ لِلمَسارات (القاعِدَة ٨)، ولا مَسارَ بِالمِفرَد.</summary>
    [Fact]
    public void The_chat_room_route_exists_and_no_singular_twin_does()
    {
        var routes = ComplianceInspectorTests.RepoRoutes;
        Assert.True(routes.Count > 100, "أَداةٌ عَمياء: جَدوَلُ المَساراتِ فارِغ.");

        Assert.Contains("/{slug}/chats/{conversationId}", routes);
        Assert.DoesNotContain(routes, r => r.StartsWith("/{slug}/chat/", StringComparison.Ordinal));
    }

    /// <summary>وأَنابيبُ البَدءِ الثَلاثَةُ ما زالَت حَيثُ تَنشُرُ إلَيها
    /// الشاشات — فَالإصلاحُ غَيَّرَ الوِجهَةَ لا الباب.</summary>
    [Fact]
    public void The_three_chat_start_pipes_are_still_registered_where_the_screens_post()
    {
        var routes = ComplianceInspectorTests.RepoRoutes;
        foreach (var pipe in new[] { "/{slug}/vendor/{vendorId}/chat", "/{slug}/users/{userId}/chat", "/{slug}/listings/{id}/chat" })
            Assert.Contains(pipe, routes);

        var vendorPage = Read("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "VendorProfile.razor");
        Assert.Contains("/vendor/{VendorId}/chat", vendorPage, StringComparison.Ordinal);

        var room = Read("libs", "templates", "ACommerce.Templates.Customer.Marketplace", "Components", "Pages", "ChatRoom.razor");
        Assert.Contains("@page \"/{slug}/chats/{conversationId:guid}\"", room, StringComparison.Ordinal);
    }

    /// <summary><b>والعَكسُ مَقيس</b>: الحَرفُ القَديمُ بِعَينِه يُضبَطُ — وإلّا
    /// كانَ الفَحصُ الأَوَّلُ دَعوى «لَم أَجِد» لا «فَحَصتُ فَلَم أَجِد».</summary>
    [Fact]
    public void The_exact_old_redirect_would_still_be_caught_today()
    {
        const string old = "return Results.Redirect(Link(req, slug, $\"chat/{existing.Id}\"));";
        Assert.Equal(1, SingularChatRedirect.Matches(old).Count);
        Assert.Equal(0, ChatRoomRedirect.Matches(old).Count);
    }
}
