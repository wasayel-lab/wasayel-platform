namespace ACommerce.Platform.Shared;

/// <summary>
/// مُستَنَد عامّ لِكُلّ صَفّ مُستَورَد مِن قاعِدَة بَيانات تَطبيق سابِق
/// لا يَملِك تَعويض typed في platform-v1. يَحفَظه الـ Importer كَ
/// document Marten داخِل الـ tenant، ويَستَهلِكه التَطبيق لِبِناء
/// واجِهات ديناميكِيَّة (مَثَلاً <c>AttributeDefinitions</c> +
/// <c>CategoryAttributeMappings</c> + <c>AttributeValues</c>).
///
/// <para>الـ <see cref="Id"/> مُرَكَّب: <c>"{Table}/{SourceId}"</c>
/// لِيَكون فَريداً عَبر الجَداوِل وقابِلاً لِلتَّصفِيَة بِالبِدايَة.</para>
/// </summary>
public sealed class ImportedRecord
{
    public string Id { get; set; } = "";
    public string Table { get; set; } = "";
    public string SourceId { get; set; } = "";
    public DateTime ImportedAt { get; set; }
    public Dictionary<string, object?> Data { get; set; } = new();
}
