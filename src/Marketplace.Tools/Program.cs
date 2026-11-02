using System.Globalization;
using Marketplace.Tools.Seeding;

// Marketplace.Tools (section 5.2): development and benchmarking commands.
//   seed-load [--orders N] [--sellers N] [--buyers N] [--products N] [--seed N] [--now ISO-8601] [--connection CS]
//   seed-demo [--now ISO-8601] [--connection CS] [--blob CS]
// Connection strings default to the ConnectionStrings__Database and ConnectionStrings__BlobStorage environment
// variables. Seeded users sign in with the password in SeedOptions.Password.
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

if (args.Length == 0 || args[0] is not ("seed-load" or "seed-demo"))
{
    await Console.Error.WriteLineAsync("Usage: Marketplace.Tools seed-load|seed-demo [options]. See Program.cs for the options.");
    return 2;
}

try
{
    var options = ToolArguments.Parse(args.AsSpan(1));
    var now = options.TryGetValue("now", out var nowText)
        ? DateTimeOffset.Parse(nowText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)
        : DateTimeOffset.UtcNow;
    var connection = options.GetValueOrDefault("connection") ?? Environment.GetEnvironmentVariable("ConnectionStrings__Database")
        ?? throw new ArgumentException("Pass --connection or set ConnectionStrings__Database.");

    if (args[0] == "seed-load")
    {
        var defaults = SeedOptions.Load(ToolArguments.Int(options, "seed", 42), now);
        var seedOptions = defaults with
        {
            Orders = ToolArguments.Int(options, "orders", defaults.Orders),
            Sellers = ToolArguments.Int(options, "sellers", defaults.Sellers),
            Buyers = ToolArguments.Int(options, "buyers", defaults.Buyers),
            Products = ToolArguments.Int(options, "products", defaults.Products),
        };
        await SeedCommands.LoadAsync(seedOptions, connection, Console.Out, cancellation.Token);
    }
    else
    {
        var blob = options.GetValueOrDefault("blob") ?? Environment.GetEnvironmentVariable("ConnectionStrings__BlobStorage")
            ?? throw new ArgumentException("Pass --blob or set ConnectionStrings__BlobStorage.");
        await SeedCommands.DemoAsync(SeedOptions.Demo(now), connection, blob, Console.Out, cancellation.Token);
    }

    return 0;
}
catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
{
    await Console.Error.WriteLineAsync(exception.Message);
    return 1;
}

internal static class ToolArguments
{
    public static Dictionary<string, string> Parse(ReadOnlySpan<string> args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                throw new ArgumentException($"Expected --name value pairs, got '{args[i]}'.");
            }

            options[args[i][2..]] = args[i + 1];
        }

        return options;
    }

    public static int Int(Dictionary<string, string> options, string name, int fallback) =>
        options.TryGetValue(name, out var text) ? int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture) : fallback;
}
