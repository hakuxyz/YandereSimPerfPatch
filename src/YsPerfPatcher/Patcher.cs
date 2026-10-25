using YsPerfPatcher.Groups;

namespace YsPerfPatcher;

public sealed class Patcher
{
    public const string Version = "1.1.0";

    static readonly PatchGroup[] Groups =
    {
        new GetterDefaultsGroup(),
        new ShadowsGlobalGroup(),
        new DrawDistanceGroup(),
        new ProfileSyncGroup(),
        new DiscordTimestampsGroup(),
        new LowSpecKeyGroup(),
    };

    readonly string _path;
    readonly AssemblyInspector _inspector;
    readonly GameBuildProfile _profile;

    public Patcher(string path, string dir)
    {
        _ = dir; // kept for API compatibility with the launcher
        _path = path;
        _profile = GameBuildProfile.Current;
        _inspector = new AssemblyInspector(path);
    }

    public int Run() => Run(write: true);
    public int RunDryRun() => Run(write: false);

    int Run(bool write)
    {
        var missing = _profile.CheckCompatibility(_inspector);
        if (missing.Count > 0)
            throw new Exception($"assembly layout incompatible with profile '{_profile.Name}': "
                + string.Join(", ", missing));

        var ctx = new PatchContext(_inspector, _profile);

        foreach (var group in Groups)
        {
            if (!group.CanApply(ctx, out var skipReason))
            {
                ctx.ReportSkipped($"{group.Name} ({skipReason})");
                continue;
            }
            group.Apply(ctx);
            group.Validate(ctx);
        }

        Console.WriteLine("=== YsPerfPatcher ===");
        foreach (var l in ctx.Applied) Console.WriteLine($"  [APPLIED] {l}");
        foreach (var l in ctx.Skipped) Console.WriteLine($"  [already] {l}");

        int appliedCount = ctx.Applied.Count;
        int expected = _profile.ExpectedPatchUnits;

        if (appliedCount == 0)
        {
            Console.WriteLine("Nothing to do - assembly already patched.");
            return 2;
        }

        if (!write)
        {
            Console.WriteLine($"Dry run: {appliedCount}/{expected} patch groups would be applied. File untouched.");
            return 0;
        }

        using var ms = new MemoryStream();
        _inspector.GameAssembly.Write(ms);
        File.WriteAllBytes(_path, ms.ToArray());

        var bundle = BackupManager.Create(_path, _inspector.OriginalBytes, ctx.Applied, Version);
        Console.WriteLine($"Wrote {_path} (backup: {bundle.OrigPath})");
        Console.WriteLine($"Summary: {appliedCount}/{expected} patch groups applied.");
        return 0;
    }
}
