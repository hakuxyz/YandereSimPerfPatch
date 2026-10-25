using System;
using System.IO;
using YsPerfPatcher;

static class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0)
            return Usage();

        switch (args[0])
        {
            case "--version":
                Console.WriteLine($"YsPerfPatcher {Patcher.Version}");
                return 0;

            case "--help":
            case "-h":
                return Usage();

            case "--restore":
                return RunRestore(args);

            case "--status":
                return RunStatus(args);

            case "--dry-run":
                return RunPatch(args, dryRun: true);

            default:
                return RunPatch(args, dryRun: false);
        }
    }

    static int Usage()
    {
        Console.WriteLine("Usage: YsPerfPatcher <path>/Assembly-CSharp.dll");
        Console.WriteLine("       YsPerfPatcher --dry-run <path>/Assembly-CSharp.dll");
        Console.WriteLine("       YsPerfPatcher --restore <path>/Assembly-CSharp.dll");
        Console.WriteLine("       YsPerfPatcher --status  <path>/Assembly-CSharp.dll");
        Console.WriteLine("       YsPerfPatcher --version");
        return 1;
    }

    static int RunPatch(string[] args, bool dryRun)
    {
        if (args.Length < (dryRun ? 2 : 1))
            return Usage();

        string path = Path.GetFullPath(dryRun ? args[1] : args[0]);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 1;
        }

        string dir = Path.GetDirectoryName(path)!;
        try
        {
            var patcher = new Patcher(path, dir);
            return dryRun ? patcher.RunDryRun() : patcher.Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"PATCH ABORTED (file untouched): {ex.Message}");
            return 3;
        }
    }

    static int RunRestore(string[] args)
    {
        if (args.Length < 2)
            return Usage();

        string path = Path.GetFullPath(args[1]);
        if (!File.Exists(path) && !File.Exists(path + ".orig"))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 1;
        }

        try
        {
            if (BackupManager.Restore(path, out var message))
            {
                Console.WriteLine(message);
                return 0;
            }
            Console.Error.WriteLine($"RESTORE FAILED: {message}");
            return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"RESTORE FAILED: {ex.Message}");
            return 3;
        }
    }

    static int RunStatus(string[] args)
    {
        if (args.Length < 2)
            return Usage();

        string path = Path.GetFullPath(args[1]);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 1;
        }

        try
        {
            Console.WriteLine(BackupManager.DescribeStatus(path));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"STATUS FAILED: {ex.Message}");
            return 3;
        }
    }
}
