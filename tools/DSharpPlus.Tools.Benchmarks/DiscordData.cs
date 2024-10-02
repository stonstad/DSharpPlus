using System;
using System.Threading.Tasks;
using DSharpPlus.Commands;
using DSharpPlus.Commands.Processors.SlashCommands;
using DSharpPlus.Commands.Processors.TextCommands;
using DSharpPlus.Entities;
using DSharpPlus.Exceptions;
using DSharpPlus.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DSharpPlus.Tools.Benchmarks;

public sealed class DiscordData
{
    public static bool IsConnected => Client is not null && Client.AllShardsConnected;
    public static IServiceProvider ServiceProvider { get; private set; } = null!;
    public static DiscordClient Client { get; private set; } = null!;
    public static CommandsExtension CommandsExtension { get; private set; } = null!;
    public static TextCommandProcessor TextCommandProcessor { get; private set; } = null!;
    public static SlashCommandProcessor SlashCommandProcessor { get; private set; } = null!;
    public static DiscordGuild Guild { get; private set; } = null!;
    public static DiscordChannel Channel { get; private set; } = null!;
    public static DiscordUser User { get; private set; } = null!;

    public static async Task SetupStaticVariablesAsync()
    {
        if (IsConnected)
        {
            return;
        }

        ServiceCollection services = new();
        services.AddLogging(loggingBuilder => loggingBuilder.SetMinimumLevel(LogLevel.Trace));
        services.AddDiscordClient(Environment.GetEnvironmentVariable("DISCORD_TOKEN")!, DiscordIntents.None);
        services.AddCommandsExtension((serviceProvider, extension) =>
        {
            extension.AddCommands(typeof(Program).Assembly);

            TextCommandProcessor = new(new()
            {
                EnableCommandNotFoundException = false,
                IgnoreBots = false
            });

            SlashCommandProcessor = new SlashCommandProcessor(new()
            {
                RegisterCommands = false
            });

            extension.AddProcessors([TextCommandProcessor, SlashCommandProcessor]);
            CommandsExtension = extension;
        }, new CommandsConfiguration()
        {
            RegisterDefaultCommandProcessors = false,
            UseDefaultCommandErrorHandler = false
        });

        ServiceProvider = services.BuildServiceProvider();
        Client = ServiceProvider.GetRequiredService<DiscordClient>();
        CommandsExtension = ServiceProvider.GetRequiredService<CommandsExtension>();

        try
        {
            await Client.ConnectAsync();
            Guild = await Client.GetGuildAsync(ulong.Parse(Environment.GetEnvironmentVariable("DISCORD_GUILD_ID")!));
            Channel = await Client.GetChannelAsync(ulong.Parse(Environment.GetEnvironmentVariable("DISCORD_CHANNEL_ID")!));
            User = Client.CurrentUser;
            await CommandsExtension.RefreshAsync();
        }
        catch (DiscordException)
        {
            await Task.Delay(10000);
        }
    }
}
