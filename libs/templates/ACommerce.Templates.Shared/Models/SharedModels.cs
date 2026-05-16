using Microsoft.AspNetCore.Components;

namespace ACommerce.Templates.Shared.Models;

// ── Extension-space DTOs ─────────────────────────────────────────────────
// Every DTO here follows Principle P-2 from TEMPLATES-ROADMAP.md:
//   1. Strongly-typed fields for the known shape.
//   2. `Extra` bag for vertical-specific or app-specific scalar values.
//   3. Optional `ExtraContent` / `ExtraMeta` RenderFragments for display-time
//      injection that the template doesn't need to understand.
//
// This is the data-side twin of the visual flex slots on the templates.

/// <summary>
/// Single conversation row for <see cref="AcMessagesListPage"/>.
/// </summary>
public sealed record ConversationRowDto
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Emoji { get; init; }
    public string? AvatarUrl { get; init; }
    public string? LastMessageSnippet { get; init; }
    public DateTime? LastMessageAt { get; init; }
    public int Unread { get; init; }
    /// <summary>Vertical-specific scalar fields (e.g. vendor type, location). Never read by the template.</summary>
    public Dictionary<string, object?>? Extra { get; init; }
}

/// <summary>
/// Single chat message for <see cref="AcChatPage"/>.
/// </summary>
public sealed record ChatMessageDto
{
    public required string Id { get; init; }
    public required string Content { get; init; }
    public required string SenderId { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? SenderName { get; init; }
    public string? AttachmentUrl { get; init; }
    public Dictionary<string, object?>? Extra { get; init; }
}

/// <summary>
/// Notification row for <see cref="AcNotificationsPage"/>.
/// </summary>
public sealed record NotificationRowDto
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Body { get; init; }
    /// <summary>Free-form type key used for styling (e.g. "order", "promo", "message", "general"). Template maps known keys, falls back to generic icon.</summary>
    public string? Type { get; init; }
    public bool IsRead { get; init; }
    public DateTime CreatedAt { get; init; }
    public Dictionary<string, object?>? Extra { get; init; }
}

/// <summary>
/// User card for <see cref="AcProfilePage"/>.
/// </summary>
public sealed record ProfileUserDto
{
    public required string Id { get; init; }
    public string? FullName { get; init; }
    public string? PhoneNumber { get; init; }
    public string? Email { get; init; }
    public string? AvatarUrl { get; init; }
    public string? Initial { get; init; }
    public Dictionary<string, object?>? Extra { get; init; }
}

/// <summary>
/// A stat cell inside a profile hero (e.g. Orders: 12, Favorites: 3).
/// </summary>
public sealed record ProfileStatDto(string Label, string Value);

/// <summary>
/// A menu section inside <see cref="AcProfilePage"/> or <see cref="AcSettingsPage"/>.
/// </summary>
public sealed record MenuSectionDto
{
    public string? Title { get; init; }
    public required IReadOnlyList<MenuItemDto> Items { get; init; }
}

/// <summary>
/// A single row inside a menu section.
/// </summary>
public sealed record MenuItemDto
{
    public required string Label { get; init; }
    /// <summary>AcIcon name (e.g. "package", "heart"). Preferred over Icon.</summary>
    public string? IconName { get; init; }
    /// <summary>Legacy: emoji or raw markup. Ignored when <see cref="IconName"/> is set.</summary>
    public string? Icon { get; init; }
    public string? Href { get; init; }
    public string? Badge { get; init; }
    public EventCallback? OnClick { get; init; }
    public Dictionary<string, object?>? Extra { get; init; }
}

/// <summary>
/// Bottom-nav entry for <see cref="AcBottomNav"/>.
/// </summary>
public sealed record BottomNavItemDto
{
    public required string Href { get; init; }
    public required string Label { get; init; }
    /// <summary>AcIcon name (e.g. "home", "cart"). Preferred over Emoji and Icon.</summary>
    public string? IconName { get; init; }
    /// <summary>Legacy: emoji fallback. Ignored when <see cref="IconName"/> is set.</summary>
    public string? Emoji { get; init; }
    /// <summary>Legacy: CSS icon class (e.g. "bi bi-house"). Ignored when <see cref="IconName"/> or <see cref="Emoji"/> is set.</summary>
    public string? Icon { get; init; }
    public int? Badge { get; init; }
    /// <summary>Exact match for NavLinkMatch.All (for root-level items like "/"). Defaults to false (prefix match).</summary>
    public bool Exact { get; init; }
}

/// <summary>
/// Theme and language preferences passed to <see cref="AcSettingsPage"/>.
/// </summary>
public sealed record SettingsSnapshot
{
    public required string Theme { get; init; }   // "light" | "dark" | "system"
    public required string Language { get; init; } // "ar" | "en"
    public string? AppVersion { get; init; }
    public string? BackendInfo { get; init; }
    public string? FrontendInfo { get; init; }
}
