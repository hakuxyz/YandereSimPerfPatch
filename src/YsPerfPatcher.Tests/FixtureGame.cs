using Mono.Cecil;
using Mono.Cecil.Cil;

namespace YsPerfPatcher.Tests;

/// <summary>
/// Builds a minimal Assembly-CSharp.dll + UnityEngine.CoreModule.dll fixture that
/// mirrors the IL shapes the patcher anchors on, so every patch group can be
/// applied and verified without the real game.
/// </summary>
public sealed class FixtureGame : IDisposable
{
    public string Dir { get; }
    public string DllPath => Path.Combine(Dir, "Assembly-CSharp.dll");

    public FixtureGame(bool includeTitleScreen = true)
    {
        Dir = Path.Combine(Path.GetTempPath(), "yspf-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);
        BuildUnityCore();
        BuildGameAssembly(includeTitleScreen);
    }

    public void Dispose()
    {
        try { Directory.Delete(Dir, true); } catch { /* best effort */ }
    }

    // ---------- UnityEngine.CoreModule.dll stubs ----------

    void BuildUnityCore()
    {
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("UnityEngine.CoreModule", new Version(1, 0)),
            "UnityEngine.CoreModule", ModuleKind.Dll);
        var m = asm.MainModule;

        TypeDefinition TypeDef(string name, TypeReference? baseType = null)
        {
            var t = new TypeDefinition("UnityEngine", name,
                TypeAttributes.Public | TypeAttributes.Class, baseType ?? m.TypeSystem.Object);
            m.Types.Add(t);
            return t;
        }

        MethodDefinition StaticMethod(TypeDefinition t, string name, TypeReference ret, params TypeReference[] pars)
        {
            var md = new MethodDefinition(name,
                MethodAttributes.Public | MethodAttributes.Static, ret);
            foreach (var p in pars) md.Parameters.Add(new ParameterDefinition(p));
            md.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
            t.Methods.Add(md);
            return md;
        }

        MethodDefinition InstanceMethod(TypeDefinition t, string name, TypeReference ret, params TypeReference[] pars)
        {
            var md = new MethodDefinition(name,
                MethodAttributes.Public | MethodAttributes.HideBySig, ret);
            foreach (var p in pars) md.Parameters.Add(new ParameterDefinition(p));
            md.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
            t.Methods.Add(md);
            return md;
        }

        var obj = TypeDef("Object");
        var go = TypeDef("GameObject");
        var comp = TypeDef("Component", obj);
        var prefs = TypeDef("PlayerPrefs");
        var input = TypeDef("Input");
        var time = TypeDef("Time");
        var debug = TypeDef("Debug");
        var label = TypeDef("UILabel");

        StaticMethod(obj, "op_Inequality", m.TypeSystem.Boolean, obj, obj);

        InstanceMethod(go, "get_activeSelf", m.TypeSystem.Boolean);
        InstanceMethod(go, "SetActive", m.TypeSystem.Void, m.TypeSystem.Boolean);

        InstanceMethod(comp, "get_gameObject", go);

        StaticMethod(prefs, "HasKey", m.TypeSystem.Boolean, m.TypeSystem.String);
        StaticMethod(prefs, "SetInt", m.TypeSystem.Void, m.TypeSystem.String, m.TypeSystem.Int32);
        StaticMethod(prefs, "GetInt", m.TypeSystem.Int32, m.TypeSystem.String);
        StaticMethod(prefs, "Save", m.TypeSystem.Void);

        StaticMethod(input, "GetKeyDown", m.TypeSystem.Boolean, m.TypeSystem.String);
        StaticMethod(time, "get_deltaTime", m.TypeSystem.Single);
        StaticMethod(debug, "Log", m.TypeSystem.Void, m.TypeSystem.Object);

        InstanceMethod(label, "get_text", m.TypeSystem.String);
        InstanceMethod(label, "set_text", m.TypeSystem.Void, m.TypeSystem.String);

        asm.Write(Path.Combine(Dir, "UnityEngine.CoreModule.dll"));
    }

    // ---------- Assembly-CSharp.dll fixture ----------

    void BuildGameAssembly(bool includeTitleScreen)
    {
        var asm = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("Assembly-CSharp", new Version(1, 0)),
            "Assembly-CSharp", ModuleKind.Dll);
        var m = asm.MainModule;

        var core = AssemblyDefinition.ReadAssembly(Path.Combine(Dir, "UnityEngine.CoreModule.dll"));
        TypeReference CoreType(string name) => m.ImportReference(core.MainModule.GetType("UnityEngine." + name));
        MethodReference CoreMethod(string type, string name)
        {
            var t = core.MainModule.GetType("UnityEngine." + type);
            return m.ImportReference(t.Methods.First(x => x.Name == name));
        }

        TypeDefinition TypeDef(string name)
        {
            var t = new TypeDefinition("", name,
                TypeAttributes.Public | TypeAttributes.Class, m.TypeSystem.Object);
            m.Types.Add(t);
            return t;
        }

        MethodDefinition InstanceMethod(TypeDefinition t, string name, TypeReference ret, Action<ILProcessor> body)
        {
            var md = new MethodDefinition(name,
                MethodAttributes.Public | MethodAttributes.HideBySig, ret);
            t.Methods.Add(md);
            body(md.Body.GetILProcessor());
            return md;
        }

        MethodDefinition StaticVoid(TypeDefinition t, string name, Action<ILProcessor>? body = null)
        {
            var md = new MethodDefinition(name,
                MethodAttributes.Public | MethodAttributes.Static, m.TypeSystem.Void);
            t.Methods.Add(md);
            var il = md.Body.GetILProcessor();
            if (body != null) body(il); else il.Append(Instruction.Create(OpCodes.Ret));
            return md;
        }

        FieldDefinition FieldDef(TypeDefinition t, string name, TypeReference ft)
        {
            var f = new FieldDefinition(name, FieldAttributes.Public, ft);
            t.Fields.Add(f);
            return f;
        }

        // --- OptionGlobals: six getters with vanilla key-build shape + set_DrawDistance ---
        var og = TypeDef("OptionGlobals");
        foreach (var prop in new[] { "Vsync", "DisableBloom", "DisableObscurance", "FPSIndex", "ParticleCount", "LowDetailStudents" })
        {
            var get = new MethodDefinition("get_" + prop,
                MethodAttributes.Public | MethodAttributes.Static, m.TypeSystem.Int32);
            og.Methods.Add(get);
            var il = get.Body.GetILProcessor();
            il.Append(Instruction.Create(OpCodes.Ldstr, "_" + prop)); // key suffix
            il.Append(Instruction.Create(OpCodes.Call, CoreMethod("PlayerPrefs", "GetInt")));
            il.Append(Instruction.Create(OpCodes.Ret));
        }
        StaticVoid(og, "set_DrawDistance");
        var getMotionBlur = new MethodDefinition("get_MotionBlur",
            MethodAttributes.Public | MethodAttributes.Static, m.TypeSystem.Int32);
        getMotionBlur.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ldc_I4_0));
        getMotionBlur.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        og.Methods.Add(getMotionBlur);

        // --- post-processing chain QualityManagerScript.Yandere.PauseScreen.NewSettings.Profile ---
        var bloomModel = TypeDef("BloomModel");
        var setEnabled = new MethodDefinition("set_enabled",
            MethodAttributes.Public | MethodAttributes.HideBySig, m.TypeSystem.Void);
        setEnabled.Parameters.Add(new ParameterDefinition(m.TypeSystem.Boolean));
        setEnabled.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        bloomModel.Methods.Add(setEnabled);

        var postFx = TypeDef("PostFx");
        FieldDef(postFx, "bloom", bloomModel);
        FieldDef(postFx, "ambientOcclusion", bloomModel);

        var newSettingsT = TypeDef("NewSettingsHolder");
        FieldDef(newSettingsT, "Profile", postFx);

        var pauseScreenT = TypeDef("PauseScreenHolder");
        FieldDef(pauseScreenT, "NewSettings", newSettingsT);

        var yandereT = TypeDef("YandereChain");
        FieldDef(yandereT, "PauseScreen", pauseScreenT);

        var qm = TypeDef("QualityManagerScript");
        FieldDef(qm, "Yandere", yandereT);
        FieldDef(qm, "PauseScreen", pauseScreenT);
        FieldDef(qm, "NewSettings", newSettingsT);
        FieldDef(qm, "Profile", postFx);

        var getShadows = new MethodDefinition("get_EnableShadows",
            MethodAttributes.Public | MethodAttributes.Static, m.TypeSystem.Boolean);
        getShadows.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ldc_I4_1));
        getShadows.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
        qm.Methods.Add(getShadows);
        var setShadows = StaticVoid(qm, "set_shadows");

        // shadows site: call get_EnableShadows; pop; ldc.i4.2; call set_shadows; ret
        InstanceMethod(qm, "ApplyShadowQuality", m.TypeSystem.Void, il =>
        {
            il.Append(Instruction.Create(OpCodes.Call, getShadows));
            il.Append(Instruction.Create(OpCodes.Pop));
            il.Append(Instruction.Create(OpCodes.Ldc_I4_2));
            il.Append(Instruction.Create(OpCodes.Call, setShadows));
            il.Append(Instruction.Create(OpCodes.Ret));
        });

        // Start: DrawDistance init + profile-sync anchor chain
        var start = new MethodDefinition("Start",
            MethodAttributes.Public | MethodAttributes.HideBySig, m.TypeSystem.Void);
        qm.Methods.Add(start);
        {
            var il = start.Body.GetILProcessor();
            var fYandere = qm.Fields.First(f => f.Name == "Yandere");
            var fPause = yandereT.Fields.First(f => f.Name == "PauseScreen");
            var fNew = pauseScreenT.Fields.First(f => f.Name == "NewSettings");
            var fProfile = newSettingsT.Fields.First(f => f.Name == "Profile");
            var fBloom = postFx.Fields.First(f => f.Name == "bloom");

            il.Append(Instruction.Create(OpCodes.Ldc_I4, 350));
            il.Append(Instruction.Create(OpCodes.Call, og.Methods.First(x => x.Name == "set_DrawDistance")));
            il.Append(Instruction.Create(OpCodes.Ldarg_0));
            il.Append(Instruction.Create(OpCodes.Ldfld, fYandere));
            il.Append(Instruction.Create(OpCodes.Ldfld, fPause));
            il.Append(Instruction.Create(OpCodes.Ldfld, fNew));
            il.Append(Instruction.Create(OpCodes.Ldfld, fProfile));
            il.Append(Instruction.Create(OpCodes.Ldfld, fBloom));                 // a-1
            il.Append(Instruction.Create(OpCodes.Call, getMotionBlur));           // a
            il.Append(Instruction.Create(OpCodes.Call, setEnabled));              // a+1 (resolvable)
            il.Append(Instruction.Create(OpCodes.Ret));                           // a+2
        }

        // --- Discord RPC with Activity/Timestamps shapes ---
        var atts = new TypeDefinition("Discord", "ActivityTimestamps",
            TypeAttributes.Public | TypeAttributes.SequentialLayout | TypeAttributes.Sealed, m.TypeSystem.Object);
        m.Types.Add(atts);
        FieldDef(atts, "Start", m.TypeSystem.Int64);

        var activity = new TypeDefinition("Discord", "Activity",
            TypeAttributes.Public | TypeAttributes.SequentialLayout | TypeAttributes.Sealed, m.TypeSystem.Object);
        m.Types.Add(activity);
        FieldDef(activity, "Timestamps", atts);
        FieldDef(activity, "Assets", m.TypeSystem.Int32);
        FieldDef(activity, "State", m.TypeSystem.String);

        var discord = TypeDef("DiscordRPC");
        var upd = new MethodDefinition("UpdateActivity",
            MethodAttributes.Public | MethodAttributes.Static, m.TypeSystem.Void);
        discord.Methods.Add(upd);
        {
            var body = upd.Body;
            body.InitLocals = true;
            var locAct = new VariableDefinition(activity);
            body.Variables.Add(locAct);
            var il = body.GetILProcessor();

            il.Append(Instruction.Create(OpCodes.Ldloca_S, locAct));
            il.Append(Instruction.Create(OpCodes.Initobj, activity));
            il.Append(Instruction.Create(OpCodes.Ldloca_S, locAct));
            il.Append(Instruction.Create(OpCodes.Ldflda, activity.Fields.First(f => f.Name == "Assets")));
            il.Append(Instruction.Create(OpCodes.Pop));
            il.Append(Instruction.Create(OpCodes.Ldloca_S, locAct));
            il.Append(Instruction.Create(OpCodes.Ldstr, "playing"));
            il.Append(Instruction.Create(OpCodes.Stfld, activity.Fields.First(f => f.Name == "State")));
            il.Append(Instruction.Create(OpCodes.Ret));
        }

        // DateTime member refs must exist somewhere in the module (Discord patcher requirement)
        var utils = TypeDef("Utils");
        var nowTicks = new MethodDefinition("NowTicks",
            MethodAttributes.Public | MethodAttributes.Static, m.TypeSystem.Int64);
        utils.Methods.Add(nowTicks);
        {
            var il = nowTicks.Body.GetILProcessor();
            var locDt = new VariableDefinition(m.ImportReference(typeof(DateTime)));
            nowTicks.Body.Variables.Add(locDt);
            il.Append(Instruction.Create(OpCodes.Call,
                m.ImportReference(typeof(DateTime).GetProperty("Now")!.GetMethod!)));
            il.Append(Instruction.Create(OpCodes.Stloc, locDt));
            il.Append(Instruction.Create(OpCodes.Ldloca, locDt));
            il.Append(Instruction.Create(OpCodes.Call,
                m.ImportReference(typeof(DateTime).GetProperty("Ticks")!.GetMethod!)));
            il.Append(Instruction.Create(OpCodes.Ret));
        }

        // --- NewTitleScreenScript + PromptBarScript ---
        if (includeTitleScreen)
        {
            var pb = TypeDef("PromptBarScript");
            FieldDef(pb, "Label", new ArrayType(m.TypeSystem.String));
            FieldDef(pb, "Show", m.TypeSystem.Boolean);
            InstanceMethod(pb, "ClearButtons", m.TypeSystem.Void, il => il.Append(Instruction.Create(OpCodes.Ret)));
            InstanceMethod(pb, "UpdateButtons", m.TypeSystem.Void, il => il.Append(Instruction.Create(OpCodes.Ret)));

            var nts = TypeDef("NewTitleScreenScript");
            FieldDef(nts, "DebugLog", CoreType("UILabel"));
            FieldDef(nts, "PromptBar", pb);

            InstanceMethod(nts, "Update", m.TypeSystem.Void, il =>
            {
                il.Append(Instruction.Create(OpCodes.Ldstr, "menu"));
                il.Append(Instruction.Create(OpCodes.Call, CoreMethod("Input", "GetKeyDown")));
                il.Append(Instruction.Create(OpCodes.Pop));
                il.Append(Instruction.Create(OpCodes.Ldstr, "Profile_0_Test"));
                il.Append(Instruction.Create(OpCodes.Ldc_I4_0));
                il.Append(Instruction.Create(OpCodes.Call, CoreMethod("PlayerPrefs", "SetInt")));
                il.Append(Instruction.Create(OpCodes.Pop));
                il.Append(Instruction.Create(OpCodes.Ret));
            });

            // seed module-wide member refs the F10 patch reuses (UILabel, Time, Debug, GameObject, ...)
            InstanceMethod(nts, "SeedRefs", m.TypeSystem.Void, il =>
            {
                il.Append(Instruction.Create(OpCodes.Call, CoreMethod("PlayerPrefs", "Save")));
                il.Append(Instruction.Create(OpCodes.Ldnull));
                il.Append(Instruction.Create(OpCodes.Ldstr, "x"));
                il.Append(Instruction.Create(OpCodes.Call, CoreMethod("UILabel", "set_text")));
                il.Append(Instruction.Create(OpCodes.Ldnull));
                il.Append(Instruction.Create(OpCodes.Call, CoreMethod("UILabel", "get_text")));
                il.Append(Instruction.Create(OpCodes.Pop));
                il.Append(Instruction.Create(OpCodes.Call, CoreMethod("Time", "get_deltaTime")));
                il.Append(Instruction.Create(OpCodes.Pop));
                il.Append(Instruction.Create(OpCodes.Ldnull));
                il.Append(Instruction.Create(OpCodes.Call, CoreMethod("Debug", "Log")));
                il.Append(Instruction.Create(OpCodes.Ldnull));
                il.Append(Instruction.Create(OpCodes.Call, CoreMethod("Component", "get_gameObject")));
                il.Append(Instruction.Create(OpCodes.Call, CoreMethod("GameObject", "get_activeSelf")));
                il.Append(Instruction.Create(OpCodes.Pop));
                il.Append(Instruction.Create(OpCodes.Ldnull));
                il.Append(Instruction.Create(OpCodes.Ldc_I4_1));
                il.Append(Instruction.Create(OpCodes.Call, CoreMethod("GameObject", "SetActive")));
                il.Append(Instruction.Create(OpCodes.Ldnull));
                il.Append(Instruction.Create(OpCodes.Ldnull));
                il.Append(Instruction.Create(OpCodes.Call, CoreMethod("Object", "op_Inequality")));
                il.Append(Instruction.Create(OpCodes.Pop));
                il.Append(Instruction.Create(OpCodes.Ret));
            });
        }

        asm.Write(DllPath);
    }
}
