namespace DSharpPlus.Entities.Interaction.Components;

/// <summary>
/// Represents an unfurled url; can be arbitrary URL or attachment:// schema. 
/// </summary>
public sealed class DiscordUnfurledMediaItem
{
    /// <summary>
    /// Gets the URL of the media item.
    /// </summary>
    public string Url { get; internal set; }

    public DiscordUnfurledMediaItem(string url)
    {
        this.Url = url;
    }
}
