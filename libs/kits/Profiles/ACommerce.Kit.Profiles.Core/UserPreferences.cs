namespace ACommerce.Kit.Profiles;

/// <summary>
/// تَفضيلات المُستَخدِم — وَثيقَة مُنفَصِلَة عن User.
/// Id = userId.ToString("N").
/// </summary>
public sealed class UserPreferences
{
    public string Id { get; set; } = "";
    public Guid UserId { get; set; }
    public string Language { get; set; } = "ar";
    public string Theme { get; set; } = "light";
    public bool NotifyChat { get; set; } = true;
    public bool NotifyPromotions { get; set; } = false;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
