namespace Instancium.Vectra.Cli;

/// <summary>
/// Lightweight CLI argument parser. Supports positional arguments,
/// long flags (--flag value, --flag=value), short flags (-f value),
/// and boolean flags. Case-insensitive flag lookup.
/// </summary>
internal sealed class CommandLineParser
{
    public string? Command { get; }
    public IReadOnlyList<string> PositionalArgs { get; }
    public IReadOnlyDictionary<string, string> Flags { get; }

    private CommandLineParser(
        string? command,
        IReadOnlyList<string> positional,
        IReadOnlyDictionary<string, string> flags)
    {
        Command = command;
        PositionalArgs = positional;
        Flags = flags;
    }

    public static CommandLineParser Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? command = null;
        var positional = new List<string>();
        var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (i == 0 && command is null && !IsFlagToken(arg))
            {
                command = arg.ToLowerInvariant();
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal) && arg.Length > 2)
            {
                ParseLongFlag(arg, args, ref i, flags);
            }
            else if (arg.StartsWith("-", StringComparison.Ordinal) && arg.Length > 1 && !IsFlagToken(arg))
            {
                ParseShortFlag(arg, args, ref i, flags);
            }
            else if (arg.StartsWith("-", StringComparison.Ordinal) && arg.Length == 2)
            {
                ParseShortFlag(arg, args, ref i, flags);
            }
            else
            {
                positional.Add(arg);
            }
        }

        return new CommandLineParser(command, positional, flags);
    }

    private static void ParseLongFlag(string arg, string[] args, ref int index, Dictionary<string, string> flags)
    {
        int equalsIndex = arg.IndexOf('=');
        if (equalsIndex >= 0)
        {
            string key = arg[2..equalsIndex];
            string value = arg[(equalsIndex + 1)..];
            if (key.Length > 0)
            {
                flags[key] = value;
            }
            return;
        }

        string name = arg[2..];
        if (name.Length == 0)
        {
            return;
        }

        if (index + 1 < args.Length && !IsFlagToken(args[index + 1]))
        {
            flags[name] = args[++index];
        }
        else
        {
            flags[name] = string.Empty;
        }
    }

    private static void ParseShortFlag(string arg, string[] args, ref int index, Dictionary<string, string> flags)
    {
        string name = arg[1..];
        if (name.Length == 0)
        {
            return;
        }

        if (index + 1 < args.Length && !IsFlagToken(args[index + 1]))
        {
            flags[name] = args[++index];
        }
        else
        {
            flags[name] = string.Empty;
        }
    }

    private static bool IsFlagToken(string arg) => arg.StartsWith('-');

    public string? GetFlag(string name) =>
        Flags.TryGetValue(name, out string? value) ? value : null;

    public bool HasFlag(string name) => Flags.ContainsKey(name);
}
