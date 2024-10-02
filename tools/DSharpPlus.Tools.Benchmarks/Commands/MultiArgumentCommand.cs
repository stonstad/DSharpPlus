using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ArgumentModifiers;

namespace DSharpPlus.Tools.Benchmarks.Commands;

public static class MultiArgumentCommand
{
    [Command("map")]
    public static ValueTask ExecuteAsync(CommandContext context, [MultiArgument(9)] IReadOnlyList<int> number) => ValueTask.CompletedTask;
}
