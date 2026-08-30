namespace Sideclip.History;

internal sealed class PersistedClip
{
    public string? Text { get; set; }
    public string? ImageFile { get; set; }
    public string? Owner { get; set; }
    public DateTime CopiedAt { get; set; }
}
