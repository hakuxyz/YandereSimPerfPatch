namespace YsPerfPatcher;

public sealed record OptionSetting(string PropertyName, int DefaultValue, string KeyPrefix);

public sealed record PresetKey(string KeyName, int Value);

public sealed class GameBuildProfile
{
    public string Name { get; }

    public IReadOnlyList<string> RequiredTypes { get; }
    public IReadOnlyDictionary<string, string[]> RequiredMembers { get; }

    public IReadOnlyList<OptionSetting> FirstRunDefaults { get; }
    public IReadOnlyList<PresetKey> LowSpecPreset { get; }

    public int ExpectedPatchUnits => FirstRunDefaults.Count + 5; // getters + shadows + drawdist + profile + timestamps + F10 key

    GameBuildProfile(string name)
    {
        Name = name;

        RequiredTypes = new[]
        {
            "OptionGlobals",
            "QualityManagerScript",
            "DiscordRPC",
            "NewTitleScreenScript",
        };

        RequiredMembers = new Dictionary<string, string[]>
        {
            ["OptionGlobals"] = new[]
            {
                "get_Vsync", "get_DisableBloom", "get_DisableObscurance",
                "get_FPSIndex", "get_ParticleCount", "get_LowDetailStudents",
            },
            ["QualityManagerScript"] = new[]
            {
                // PauseScreen/NewSettings/Profile live on the field chain types
                // (YandereScript -> PauseScreenScript -> ...), anchored at Apply-time
                "Start", "Yandere",
            },
            ["DiscordRPC"] = new[] { "UpdateActivity" },
            ["NewTitleScreenScript"] = new[] { "Update", "DebugLog", "PromptBar" },
        };

        // vanilla OptionGlobals getters build every option key as "Profile_N_<name>";
        // options are global, not per save (ldc.i4.0 in Assembly-CSharp.dll.orig)
        const string GlobalKey = "_";
        FirstRunDefaults = new[]
        {
            new OptionSetting("Vsync", 1, GlobalKey),
            new OptionSetting("DisableBloom", 1, GlobalKey),
            new OptionSetting("DisableObscurance", 1, GlobalKey),
            new OptionSetting("FPSIndex", 2, GlobalKey),
            new OptionSetting("ParticleCount", 2, GlobalKey),
            new OptionSetting("LowDetailStudents", 2, GlobalKey),
        };

        // F10 low-spec preset: writes the same global option keys directly
        LowSpecPreset = new[]
        {
            new PresetKey("Profile_0_Vsync", 1),
            new PresetKey("Profile_0_FPSIndex", 2),
            new PresetKey("Profile_0_DisableBloom", 1),
            new PresetKey("Profile_0_DisableObscurance", 1),
            new PresetKey("Profile_0_ParticleCount", 2),
            new PresetKey("Profile_0_LowDetailStudents", 2),
            new PresetKey("Profile_0_DrawDistance", 200),
            new PresetKey("Profile_0_EnableShadows", 0),
            new PresetKey("Profile_0_MotionBlur", 0),
            new PresetKey("Profile_0_DepthOfField", 0),
            new PresetKey("Profile_0_DisablePostAliasing", 0),
            new PresetKey("Profile_0_RimLight", 0),
            new PresetKey("Profile_0_Fog", 0),
            new PresetKey("Profile_0_DisableFarAnimations", 1),
            new PresetKey("Profile_0_ToggleGrass", 0),
            new PresetKey("Profile_0_DisableMirrors", 1),
            new PresetKey("Profile_0_DisableOutlines", 1),
            new PresetKey("Profile_0_OpaqueWindows", 1),
            new PresetKey("Profile_0_ColorGrading", 0),
        };
    }

    public static GameBuildProfile Current { get; } = new("YandereSimulator (current)");

    public IReadOnlyList<string> CheckCompatibility(AssemblyInspector inspector)
    {
        var missing = new List<string>();

        foreach (var typeName in RequiredTypes)
            if (inspector.FindType(typeName) == null)
                missing.Add($"type {typeName}");

        foreach (var (typeName, members) in RequiredMembers)
        {
            var type = inspector.FindType(typeName);
            if (type == null) continue; // already reported above
            foreach (var member in members)
            {
                bool found = type.Methods.Any(m => m.Name == member)
                    || type.Fields.Any(f => f.Name == member);
                if (!found)
                    missing.Add($"{typeName}.{member}");
            }
        }

        return missing;
    }
}
