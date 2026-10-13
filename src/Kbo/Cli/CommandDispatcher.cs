namespace Kbo.Cli;

internal static class CommandDispatcher
{
    public static async Task<int> RunAsync(
        string[] args,
        IReadOnlyList<KeyValuePair<string, Func<string[], Task<int>>>> commands,
        TextWriter error)
    {
        Func<string[], Task<int>>? handler = null;
        if (args.Length > 0)
        {
            for (int index = 0; index < commands.Count; index++)
            {
                if (string.Equals(commands[index].Key, args[0], StringComparison.Ordinal))
                {
                    handler = commands[index].Value;
                    break;
                }
            }
        }

        if (handler is null)
        {
            await error.WriteLineAsync(BuildUsage(commands)).ConfigureAwait(false);
            return 1;
        }

        return await handler(args[1..]).ConfigureAwait(false);
    }

    private static string BuildUsage(IReadOnlyList<KeyValuePair<string, Func<string[], Task<int>>>> commands)
    {
        string[] names = new string[commands.Count];
        for (int index = 0; index < commands.Count; index++)
        {
            names[index] = commands[index].Key;
        }

        return "usage: kbo <" + string.Join(" | ", names) + "> ...";
    }
}
