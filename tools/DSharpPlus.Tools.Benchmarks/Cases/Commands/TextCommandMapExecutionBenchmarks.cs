using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Net.Serialization;
using Newtonsoft.Json.Linq;

namespace DSharpPlus.Tools.Benchmarks.Cases;

public class TextCommandMapExecutionBenchmarks
{
    private static readonly DiscordMessage message = DiscordJson.ToDiscordObject<DiscordMessage>(JToken.Parse("""{"type":0,"tts":false,"timestamp":"2024-10-02T01:04:48.604-05:00","pinned":false,"nonce":"1290917384118337536","mentions":[],"mention_roles":[],"mention_everyone":false,"member":{"roles":["402444174223343617","1252238597004853379","573612339924959270","379397810476417064","973818122211590154","1204470581274353674","750044518849445909","1059190345688158358","1057954598683414619","1163786594138992670"],"premium_since":null,"pending":false,"nick":null,"mute":false,"joined_at":"2024-09-26T23:50:07.584-05:00","flags":11,"deaf":false,"communication_disabled_until":null,"banner":null,"avatar":null},"id":"1290917384726515742","flags":0,"embeds":[],"edited_timestamp":null,"content":"!enum Monday","components":[],"channel_id":"379379415475552276","author":{"username":"oolunar","public_flags":4194560,"id":"336733686529654798","global_name":"Lunar","discriminator":"0","clan":{"tag":"Moon","identity_guild_id":"832354798153236510","identity_enabled":true,"badge":"f09dccb8074d3c1a3bccadff9ceee10b"},"avatar_decoration_data":null,"avatar":"cb52688afd66f14e8a433396cd84c7c7"},"attachments":[],"guild_id":"379378609942560770"}"""));
    private static MessageCreatedEventArgs mapCommandNoArgs;
    private static MessageCreatedEventArgs mapCommandOneArgs;
    private static MessageCreatedEventArgs mapCommandTwoArgs;
    private static MessageCreatedEventArgs mapCommandThreeArgs;
    private static MessageCreatedEventArgs mapCommandFourArgs;
    private static MessageCreatedEventArgs mapCommandFiveArgs;
    private static MessageCreatedEventArgs mapCommandSixArgs;
    private static MessageCreatedEventArgs mapCommandSevenArgs;
    private static MessageCreatedEventArgs mapCommandEightArgs;
    private static MessageCreatedEventArgs mapCommandNineArgs;
    private static MessageCreatedEventArgs mapCommandTenArgs;

    [GlobalSetup]
    public void Setup()
    {
        bool isConnected = DiscordData.IsConnected;
        DiscordData.SetupStaticVariablesAsync().GetAwaiter().GetResult();
        if (!isConnected)
        {
            mapCommandNoArgs = TextCommandUtilities.CreateFakeMessageEventArgsAsync(message, "!map", DiscordData.Client, DiscordData.Client.CurrentUser, DiscordData.Channel, DiscordData.Guild).GetAwaiter().GetResult();
            mapCommandOneArgs = TextCommandUtilities.CreateFakeMessageEventArgsAsync(message, "!map 1", DiscordData.Client, DiscordData.Client.CurrentUser, DiscordData.Channel, DiscordData.Guild).GetAwaiter().GetResult();
            mapCommandTwoArgs = TextCommandUtilities.CreateFakeMessageEventArgsAsync(message, "!map 1 2", DiscordData.Client, DiscordData.Client.CurrentUser, DiscordData.Channel, DiscordData.Guild).GetAwaiter().GetResult();
            mapCommandThreeArgs = TextCommandUtilities.CreateFakeMessageEventArgsAsync(message, "!map 1 2 3", DiscordData.Client, DiscordData.Client.CurrentUser, DiscordData.Channel, DiscordData.Guild).GetAwaiter().GetResult();
            mapCommandFourArgs = TextCommandUtilities.CreateFakeMessageEventArgsAsync(message, "!map 1 2 3 4", DiscordData.Client, DiscordData.Client.CurrentUser, DiscordData.Channel, DiscordData.Guild).GetAwaiter().GetResult();
            mapCommandFiveArgs = TextCommandUtilities.CreateFakeMessageEventArgsAsync(message, "!map 1 2 3 4 5", DiscordData.Client, DiscordData.Client.CurrentUser, DiscordData.Channel, DiscordData.Guild).GetAwaiter().GetResult();
            mapCommandSixArgs = TextCommandUtilities.CreateFakeMessageEventArgsAsync(message, "!map 1 2 3 4 5 6", DiscordData.Client, DiscordData.Client.CurrentUser, DiscordData.Channel, DiscordData.Guild).GetAwaiter().GetResult();
            mapCommandSevenArgs = TextCommandUtilities.CreateFakeMessageEventArgsAsync(message, "!map 1 2 3 4 5 6 7", DiscordData.Client, DiscordData.Client.CurrentUser, DiscordData.Channel, DiscordData.Guild).GetAwaiter().GetResult();
            mapCommandEightArgs = TextCommandUtilities.CreateFakeMessageEventArgsAsync(message, "!map 1 2 3 4 5 6 7 8", DiscordData.Client, DiscordData.Client.CurrentUser, DiscordData.Channel, DiscordData.Guild).GetAwaiter().GetResult();
            mapCommandNineArgs = TextCommandUtilities.CreateFakeMessageEventArgsAsync(message, "!map 1 2 3 4 5 6 7 8 9", DiscordData.Client, DiscordData.Client.CurrentUser, DiscordData.Channel, DiscordData.Guild).GetAwaiter().GetResult();
            mapCommandTenArgs = TextCommandUtilities.CreateFakeMessageEventArgsAsync(message, "!map 1 2 3 4 5 6 7 8 9 10", DiscordData.Client, DiscordData.Client.CurrentUser, DiscordData.Channel, DiscordData.Guild).GetAwaiter().GetResult();
        }
    }

    [Benchmark, ArgumentsSource(nameof(GetDiscordClient))]
    public async ValueTask ExecuteNoArgsParamsCommandAsync(DiscordClient client) => await DiscordData.TextCommandProcessor.ExecuteTextCommandAsync(client, mapCommandNoArgs);

    [Benchmark, ArgumentsSource(nameof(GetDiscordClient))]
    public async ValueTask ExecuteOneArgParamsCommandAsync(DiscordClient client) => await DiscordData.TextCommandProcessor.ExecuteTextCommandAsync(client, mapCommandOneArgs);

    [Benchmark, ArgumentsSource(nameof(GetDiscordClient))]
    public async ValueTask ExecuteTwoArgsParamsCommandAsync(DiscordClient client) => await DiscordData.TextCommandProcessor.ExecuteTextCommandAsync(client, mapCommandTwoArgs);

    [Benchmark, ArgumentsSource(nameof(GetDiscordClient))]
    public async ValueTask ExecuteThreeArgsParamsCommandAsync(DiscordClient client) => await DiscordData.TextCommandProcessor.ExecuteTextCommandAsync(client, mapCommandThreeArgs);

    [Benchmark, ArgumentsSource(nameof(GetDiscordClient))]
    public async ValueTask ExecuteFourArgsParamsCommandAsync(DiscordClient client) => await DiscordData.TextCommandProcessor.ExecuteTextCommandAsync(client, mapCommandFourArgs);

    [Benchmark, ArgumentsSource(nameof(GetDiscordClient))]
    public async ValueTask ExecuteFiveArgsParamsCommandAsync(DiscordClient client) => await DiscordData.TextCommandProcessor.ExecuteTextCommandAsync(client, mapCommandFiveArgs);

    [Benchmark, ArgumentsSource(nameof(GetDiscordClient))]
    public async ValueTask ExecuteSixArgsParamsCommandAsync(DiscordClient client) => await DiscordData.TextCommandProcessor.ExecuteTextCommandAsync(client, mapCommandSixArgs);

    [Benchmark, ArgumentsSource(nameof(GetDiscordClient))]
    public async ValueTask ExecuteSevenArgsParamsCommandAsync(DiscordClient client) => await DiscordData.TextCommandProcessor.ExecuteTextCommandAsync(client, mapCommandSevenArgs);

    [Benchmark, ArgumentsSource(nameof(GetDiscordClient))]
    public async ValueTask ExecuteEightArgsParamsCommandAsync(DiscordClient client) => await DiscordData.TextCommandProcessor.ExecuteTextCommandAsync(client, mapCommandEightArgs);

    [Benchmark, ArgumentsSource(nameof(GetDiscordClient))]
    public async ValueTask ExecuteNineArgsParamsCommandAsync(DiscordClient client) => await DiscordData.TextCommandProcessor.ExecuteTextCommandAsync(client, mapCommandNineArgs);

    [Benchmark, ArgumentsSource(nameof(GetDiscordClient))]
    public async ValueTask ExecuteTenArgsParamsCommandAsync(DiscordClient client) => await DiscordData.TextCommandProcessor.ExecuteTextCommandAsync(client, mapCommandTenArgs);

    public IEnumerable<object> GetDiscordClient()
    {
        Setup();

        yield return DiscordData.Client;
    }
}
