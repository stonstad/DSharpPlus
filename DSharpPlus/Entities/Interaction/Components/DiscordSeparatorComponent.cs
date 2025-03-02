namespace DSharpPlus.Entities;

/// <summary>
/// Represents a division between components. Can optionally be rendered as a dividing line.
/// </summary>
public class DiscordSeparatorComponent : DiscordComponent
{
    
    /// <summary>
    /// Whether the separator renders as a dividing line.
    /// </summary>
    public bool Divider { get; internal set; }
    
    /// <summary>
    /// The spacing for the separator. Defaults to <see cref="DiscordSeparatorComponent"/>
    /// </summary>
    public DiscordSeparatorSpacing Spacing { get; internal set; }

    public DiscordSeparatorComponent(bool divider = false, DiscordSeparatorSpacing spacing = DiscordSeparatorSpacing.Small)
    {
        this.Divider = divider;
        this.Spacing = spacing;
    }
    
    
    internal DiscordSeparatorComponent() => this.Type = DiscordComponentType.Separator;

}
