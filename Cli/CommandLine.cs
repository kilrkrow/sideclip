using System.IO;
using Sideclip.Screenshot;

namespace Sideclip.Cli;

internal static class CommandLine
{
    public static bool TryRunAndExit(string[] args)
    {
        if (args.Length == 0)
            return false;

        var cmd = args[0].Trim();
        if (cmd.StartsWith('/') || cmd.StartsWith('-'))
            cmd = cmd.TrimStart('/', '-');

        ConsoleAttach.Ensure();

        try
        {
            return Run(cmd, args);
        }
        catch (IOException ex)
        {
            try { Console.Error.WriteLine(ex.Message); } catch { }
            Environment.ExitCode = 1;
            return true;
        }
    }

    private static bool Run(string cmd, string[] args)
    {
        switch (cmd.ToLowerInvariant())
        {
            case "help":
            case "h":
            case "?":
                PrintHelp();
                return true;
            case "shots":
            case "list":
                PrintShots(ParseN(args, defaultN: 3));
                return true;
            case "newest":
            case "latest":
                PrintNewest();
                return true;
            case "picker":
                Forward("picker", timeoutMs: 8_000, expectPath: false);
                return true;
            case "pick":
                Forward("pick", timeoutMs: 0, expectPath: true);
                return true;
            default:
                Console.Error.WriteLine("Unknown command: " + args[0]);
                PrintHelp();
                Environment.ExitCode = 2;
                return true;
        }
    }

    private static int ParseN(string[] args, int defaultN)
    {
        if (args.Length < 2)
            return defaultN;
        if (int.TryParse(args[1], out var n) && n > 0)
            return n;
        return defaultN;
    }

    private static void PrintShots(int n)
    {
        var files = ScreenshotFiles.Newest(n);
        if (files.Count == 0)
        {
            Console.WriteLine("[]");
            if (!Directory.Exists(ScreenshotFiles.Folder))
                Console.Error.WriteLine("Screenshots folder is missing: " + ScreenshotFiles.Folder);
            return;
        }

        Console.WriteLine(ScreenshotFiles.ToJson(files));
    }

    private static void PrintNewest()
    {
        var newest = ScreenshotFiles.NewestOne();
        if (newest is null)
        {
            Console.Error.WriteLine("No screenshots yet.");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine(newest.FullName);
    }

    private static void Forward(string cmd, int timeoutMs, bool expectPath)
    {
        var reply = IpcServer.TrySend(cmd, timeoutMs);
        if (reply is null || !reply.Ok)
        {
            Console.Error.WriteLine(reply?.Error ?? "Sideclip is not running. Start the tray app first.");
            Environment.ExitCode = 2;
            return;
        }

        if (!expectPath)
        {
            Console.WriteLine("ok");
            return;
        }

        if (string.Equals(reply.Kind, "cancel", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("{\"ok\":true,\"kind\":\"cancel\"}");
            return;
        }

        if (string.Equals(reply.Kind, "image", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(reply.Path))
        {
            Console.WriteLine("{\"ok\":true,\"kind\":\"image\",\"path\":" + JsonString(reply.Path) + "}");
            return;
        }

        Console.WriteLine("{\"ok\":true,\"kind\":\"text\",\"text\":" + JsonString(reply.Text ?? "") + "}");
    }

    private static string JsonString(string value)
        => System.Text.Json.JsonSerializer.Serialize(value);

    private static void PrintHelp()
    {
        Console.WriteLine("""
            Sideclip CLI

            sideclip --shots [n]   List the n newest files in Pictures\Screenshots as JSON (default 3)
            sideclip --newest      Print the newest screenshot path
            sideclip --picker      Open the clipboard picker (tray app must be running)
            sideclip --pick        Open the picker and print the chosen clip as JSON
            sideclip --help        This help
            """);
    }
}
