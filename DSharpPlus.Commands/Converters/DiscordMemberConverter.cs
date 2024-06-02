using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.TextCommands;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DSharpPlus.Commands.Converters;

public partial class DiscordMemberConverter : ISlashArgumentConverter<DiscordMember>, ITextArgumentConverter<DiscordMember>
{
    [GeneratedRegex("""^<@!?(\d+?)>$""", RegexOptions.Compiled | RegexOptions.ECMAScript)]
    private static partial Regex getMemberRegex();

    public DiscordApplicationCommandOptionType ParameterType => DiscordApplicationCommandOptionType.User;
    public string ReadableName => "Discord Server Member";
    public bool RequiresText => true;
    private readonly ILogger<DiscordMemberConverter> logger;

    public DiscordMemberConverter(ILogger<DiscordMemberConverter>? logger = null) => this.logger = logger ?? NullLogger<DiscordMemberConverter>.Instance;

    public async Task<Optional<DiscordMember>> ConvertAsync(TextConverterContext context, MessageCreatedEventArgs eventArgs)
    {
        if (context.Guild is null)
        {
            return Optional.FromNoValue<DiscordMember>();
        }

        if (!ulong.TryParse(context.Argument, CultureInfo.InvariantCulture, out ulong memberId))
        {
            Match match = getMemberRegex().Match(context.Argument);
            if (!match.Success || !ulong.TryParse(match.Groups[1].ValueSpan, NumberStyles.Number, CultureInfo.InvariantCulture, out memberId))
            {
                // Attempt to find a member by name, case sensitive.
                DiscordMember? namedMember = context.Guild.Members.Values.FirstOrDefault(member => member.DisplayName.Equals(context.Argument, StringComparison.Ordinal));
                return namedMember is not null ? Optional.FromValue(namedMember) : Optional.FromNoValue<DiscordMember>();
            }
        }

        try
        {
            DiscordMember? possiblyCachedMember = await context.Guild.GetMemberAsync(memberId);
            return possiblyCachedMember is not null ? Optional.FromValue(possiblyCachedMember) : Optional.FromNoValue<DiscordMember>();
        }
        catch (DiscordException error)
        {
            this.logger.LogError(error, "Failed to get member from guild.");
            return Optional.FromNoValue<DiscordMember>();
        }
    }

    public Task<Optional<DiscordMember>> ConvertAsync(InteractionConverterContext context, InteractionCreatedEventArgs eventArgs) => context.Interaction.Data.Resolved is null
        || !ulong.TryParse(context.Argument.RawValue, CultureInfo.InvariantCulture, out ulong memberId)
        || !context.Interaction.Data.Resolved.Members.TryGetValue(memberId, out DiscordMember? member)
            ? Task.FromResult(Optional.FromNoValue<DiscordMember>())
            : Task.FromResult(Optional.FromValue(member));
}
