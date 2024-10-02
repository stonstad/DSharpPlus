using System.Threading.Tasks;
using DSharpPlus.Commands;
using DSharpPlus.Commands.ArgumentModifiers;

namespace DSharpPlus.Tools.Benchmarks.Commands;

public static class VarArgsCommand
{
    [Command("varargs")]
    public static ValueTask ExecuteAsync(CommandContext context, [MultiArgument(9)] int[] number) => ValueTask.CompletedTask;
}
