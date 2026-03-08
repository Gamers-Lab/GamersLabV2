// tiny helper POCO

public record SupabaseConfigModels
{
    public required string Url { get; init; }
    public required string Key { get; init; }
}