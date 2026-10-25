using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;
using YsPerfPatcher;

namespace YsPerfPatcher.Tests;

public class PatcherTests
{
    static (int Rc, string Output) RunPatcher(string dllPath, bool dryRun = false)
    {
        var sw = new StringWriter();
        var old = Console.Out;
        Console.SetOut(sw);
        try
        {
            var patcher = new Patcher(dllPath, Path.GetDirectoryName(dllPath)!);
            int rc = dryRun ? patcher.RunDryRun() : patcher.Run();
            return (rc, sw.ToString());
        }
        finally
        {
            Console.SetOut(old);
        }
    }

    static AssemblyDefinition Open(string path)
        => AssemblyDefinition.ReadAssembly(path);

    // ---------- full run ----------

    [Fact]
    public void FullPatch_AppliesAllGroups_WritesBackupAndManifest()
    {
        using var fx = new FixtureGame();
        byte[] original = File.ReadAllBytes(fx.DllPath);

        var (rc, output) = RunPatcher(fx.DllPath);

        Assert.Equal(0, rc);
        Assert.Contains("Summary: 11/11 patch groups applied.", output);
        Assert.Contains("[APPLIED] OptionGlobals.Vsync first-run default = 1", output);
        Assert.NotEqual(Convert.ToHexString(original), Convert.ToHexString(File.ReadAllBytes(fx.DllPath)));
        Assert.True(File.Exists(fx.DllPath + ".orig"));
        Assert.True(File.Exists(fx.DllPath + ".backup.json"));

        var manifest = BackupManager.TryReadManifest(fx.DllPath);
        Assert.NotNull(manifest);
        Assert.Equal(BackupManager.Md5Of(original), manifest!.Md5Original);
        Assert.Equal(11, manifest.AppliedGroups.Count);
    }

    [Fact]
    public void SecondRun_IsNoOp()
    {
        using var fx = new FixtureGame();
        var (rc1, _) = RunPatcher(fx.DllPath);
        Assert.Equal(0, rc1);

        var (rc2, output2) = RunPatcher(fx.DllPath);
        Assert.Equal(2, rc2);
        Assert.Contains("Nothing to do - assembly already patched.", output2);
    }

    [Fact]
    public void DryRun_ReportsButDoesNotTouchFile()
    {
        using var fx = new FixtureGame();
        byte[] before = File.ReadAllBytes(fx.DllPath);

        var (rc, output) = RunPatcher(fx.DllPath, dryRun: true);

        Assert.Equal(0, rc);
        Assert.Contains("Dry run: 11/11 patch groups would be applied.", output);
        Assert.Equal(before, File.ReadAllBytes(fx.DllPath));
        Assert.False(File.Exists(fx.DllPath + ".orig"));
        Assert.False(File.Exists(fx.DllPath + ".backup.json"));
    }

    [Fact]
    public void IncompatibleLayout_AbortsWithClearMessage()
    {
        using var fx = new FixtureGame(includeTitleScreen: false);
        var ex = Assert.Throws<Exception>(() => RunPatcher(fx.DllPath));
        Assert.Contains("layout incompatible", ex.Message);
        Assert.Contains("NewTitleScreenScript", ex.Message);
    }

    // ---------- per-group IL verification ----------

    [Fact]
    public void GetterDefaults_InjectHasKeyGuardAndDefault()
    {
        using var fx = new FixtureGame();
        RunPatcher(fx.DllPath);

        using var asm = Open(fx.DllPath);
        var og = asm.MainModule.GetType("OptionGlobals");
        foreach (var prop in new[] { "Vsync", "DisableBloom", "DisableObscurance", "FPSIndex", "ParticleCount", "LowDetailStudents" })
        {
            var get = og.Methods.First(m => m.Name == "get_" + prop);
            Assert.Contains(get.Body.Instructions, i =>
                (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
                && i.Operand is MethodReference mr && mr.Name == "HasKey");
        }
    }

    [Fact]
    public void DrawDistance_Is200()
    {
        using var fx = new FixtureGame();
        RunPatcher(fx.DllPath);

        using var asm = Open(fx.DllPath);
        var qm = asm.MainModule.GetType("QualityManagerScript");
        var start = qm.Methods.First(m => m.Name == "Start");
        var instrs = start.Body.Instructions;
        bool found = false;
        for (int i = 1; i < instrs.Count; i++)
        {
            if (instrs[i].OpCode == OpCodes.Call && instrs[i].Operand is MethodReference mr
                && mr.Name == "set_DrawDistance")
            {
                Assert.Equal(OpCodes.Ldc_I4, instrs[i - 1].OpCode);
                Assert.Equal(200, instrs[i - 1].Operand);
                found = true;
            }
        }
        Assert.True(found, "set_DrawDistance call not found in Start");
    }

    [Fact]
    public void Shadows_SoftSitesBecomeHard()
    {
        using var fx = new FixtureGame();
        RunPatcher(fx.DllPath);

        using var asm = Open(fx.DllPath);
        var qm = asm.MainModule.GetType("QualityManagerScript");
        var method = qm.Methods.First(m => m.Name == "ApplyShadowQuality");
        var instrs = method.Body.Instructions;
        for (int i = 0; i < instrs.Count; i++)
        {
            if (instrs[i].OpCode == OpCodes.Call && instrs[i].Operand is MethodReference mr
                && mr.Name == "set_shadows")
            {
                Assert.True(i > 0);
                Assert.Equal(OpCodes.Ldc_I4_1, instrs[i - 1].OpCode); // Hard, was Soft (2)
            }
        }
    }

    [Fact]
    public void ProfileSync_InjectsBloomAoCalls()
    {
        using var fx = new FixtureGame();
        RunPatcher(fx.DllPath);

        using var asm = Open(fx.DllPath);
        var qm = asm.MainModule.GetType("QualityManagerScript");
        var start = qm.Methods.First(m => m.Name == "Start");
        int bloomCalls = start.Body.Instructions.Count(i =>
            i.OpCode == OpCodes.Call && i.Operand is MethodReference mr
            && mr.Name == "get_DisableBloom");
        Assert.True(bloomCalls >= 1, "injected get_DisableBloom call missing");
    }

    [Fact]
    public void DiscordTimestamps_AddsCachedStartField()
    {
        using var fx = new FixtureGame();
        RunPatcher(fx.DllPath);

        using var asm = Open(fx.DllPath);
        var t = asm.MainModule.GetType("DiscordRPC");
        Assert.Contains(t.Fields, f => f.Name == "_presenceStart");
        var upd = t.Methods.First(m => m.Name == "UpdateActivity");
        Assert.Contains(upd.Body.Instructions, i =>
            i.OpCode == OpCodes.Call && i.Operand is MethodReference mr
            && mr.FullName == "System.Int64 System.DateTime::get_Ticks()");
    }

    [Fact]
    public void LowSpecKey_AddsF10MarkerAndNotifFields()
    {
        using var fx = new FixtureGame();
        RunPatcher(fx.DllPath);

        using var asm = Open(fx.DllPath);
        var t = asm.MainModule.GetType("NewTitleScreenScript");
        foreach (var f in new[] { "F10NotifTimer", "F10NotifOriginal", "F10NotifLabel", "F10NotifWasActive" })
            Assert.Contains(t.Fields, fd => fd.Name == f);
        var upd = t.Methods.First(m => m.Name == "Update");
        Assert.Contains(upd.Body.Instructions, i =>
            i.OpCode == OpCodes.Ldstr && (i.Operand as string) == "f10");
    }

    // ---------- backup / restore ----------

    [Fact]
    public void Restore_ReturnsOriginalBytes()
    {
        using var fx = new FixtureGame();
        byte[] original = File.ReadAllBytes(fx.DllPath);
        RunPatcher(fx.DllPath);

        Assert.True(BackupManager.Restore(fx.DllPath, out var message), message);
        Assert.Equal(original, File.ReadAllBytes(fx.DllPath));
    }

    [Fact]
    public void Restore_FailsWithoutBackup()
    {
        using var fx = new FixtureGame();
        File.Delete(fx.DllPath + ".orig");
        Assert.False(BackupManager.Restore(fx.DllPath, out var message));
        Assert.Contains("No backup found", message);
    }

    // ---------- negative paths ----------

    [Fact]
    public void Restore_FailsClosed_OnInvalidManifest()
    {
        using var fx = new FixtureGame();
        RunPatcher(fx.DllPath);
        File.WriteAllText(fx.DllPath + ".backup.json", "{ not json");

        Assert.False(BackupManager.Restore(fx.DllPath, out var message));
        Assert.Contains("Manifest invalid", message);
    }

    [Fact]
    public void Restore_Fails_OnTamperedOrig()
    {
        using var fx = new FixtureGame();
        RunPatcher(fx.DllPath);
        File.WriteAllBytes(fx.DllPath + ".orig", new byte[] { 1, 2, 3 });

        Assert.False(BackupManager.Restore(fx.DllPath, out var message));
        Assert.Contains("Backup validation failed", message);
    }

    [Fact]
    public void Restore_Works_WithoutManifest()
    {
        using var fx = new FixtureGame();
        byte[] original = File.ReadAllBytes(fx.DllPath);
        RunPatcher(fx.DllPath);
        File.Delete(fx.DllPath + ".backup.json"); // pre-1.1.0 install shape

        Assert.True(BackupManager.Restore(fx.DllPath, out var message), message);
        Assert.Equal(original, File.ReadAllBytes(fx.DllPath));
    }

    [Fact]
    public void PartialPatch_AppliesOnlyRemainingGroups()
    {
        using var fx = new FixtureGame();

        // simulate a half-patched state: draw distance already 200, rest vanilla
        using (var asm = Open(fx.DllPath))
        {
            var qm = asm.MainModule.GetType("QualityManagerScript");
            var start = qm.Methods.First(m => m.Name == "Start");
            var instrs = start.Body.Instructions;
            for (int i = 1; i < instrs.Count; i++)
            {
                if (instrs[i].OpCode == OpCodes.Call && instrs[i].Operand is MethodReference mr
                    && mr.Name == "set_DrawDistance")
                {
                    Assert.Equal(350, instrs[i - 1].Operand);
                    instrs[i - 1].Operand = 200;
                }
            }
            asm.Write(fx.DllPath);
        }

        var (rc, output) = RunPatcher(fx.DllPath);
        Assert.Equal(0, rc);
        Assert.Contains("Summary: 10/11 patch groups applied.", output);
        Assert.Contains("[already] QualityManagerScript.Start DrawDistance = 200", output);
    }

    [Fact]
    public void CorruptedAssembly_Throws()
    {
        using var fx = new FixtureGame();
        File.WriteAllBytes(fx.DllPath, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        Assert.ThrowsAny<Exception>(() => RunPatcher(fx.DllPath));
    }

    [Fact]
    public void SignatureMismatch_AbortsWithParamCount()
    {
        using var fx = new FixtureGame(breakDiscordSignature: true);
        var ex = Assert.Throws<Exception>(() => RunPatcher(fx.DllPath));
        Assert.Contains("layout incompatible", ex.Message);
        Assert.Contains("DiscordRPC.UpdateActivity (0 params)", ex.Message);
    }

    [Fact]
    public void Status_ReportsUnreadableManifest()
    {
        using var fx = new FixtureGame();
        RunPatcher(fx.DllPath);
        File.WriteAllText(fx.DllPath + ".backup.json", "{ not json");

        Assert.Contains("unreadable", BackupManager.DescribeStatus(fx.DllPath));
    }

    [Fact]
    public void FindExistingField_FindsDeclaredFields()
    {
        using var fx = new FixtureGame();
        var inspector = new AssemblyInspector(fx.DllPath);

        Assert.NotNull(inspector.FindExistingField(fr => fr.Name == "Yandere"));
        Assert.Null(inspector.FindExistingField(fr => fr.Name == "__no_such_field__"));
        var label = inspector.RequireExistingField(fr => fr.Name == "DebugLog", "DebugLog");
        Assert.Equal("DebugLog", label.Name);
    }

    // ---------- CLI return codes ----------

    static int RunCli(string[] args)
    {
        var sw = new StringWriter();
        var old = Console.Out;
        Console.SetOut(sw);
        try
        {
            return Program.Main(args);
        }
        finally
        {
            Console.SetOut(old);
        }
    }

    [Fact]
    public void Cli_Version_Returns0()
    {
        Assert.Equal(0, RunCli(new[] { "--version" }));
    }

    [Fact]
    public void Cli_MissingFile_Returns1()
    {
        Assert.Equal(1, RunCli(new[] { Path.Combine(Path.GetTempPath(), "no-such-yspf.dll") }));
    }

    [Fact]
    public void Cli_IncompatibleLayout_Returns3()
    {
        using var fx = new FixtureGame(includeTitleScreen: false);
        Assert.Equal(3, RunCli(new[] { fx.DllPath }));
        Assert.Equal(3, RunCli(new[] { "--dry-run", fx.DllPath }));
    }
}
