using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

static class Program
{
    const float NearM = 30f;
    const float FarM = 70f;

    static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: YsPerfPatcher <path>/Assembly-CSharp.dll");
            return 1;
        }

        string path = Path.GetFullPath(args[0]);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 1;
        }

        string dir = Path.GetDirectoryName(path)!;
        try
        {
            var patcher = new Patcher(path, dir);
            return patcher.Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"PATCH ABORTED (file untouched): {ex.Message}");
            return 3;
        }
    }
}

sealed class Patcher
{
    const float NearM = 30f;
    const float FarM = 70f;

    readonly string _path;
    readonly string _dir;
    readonly byte[] _bytes;
    readonly AssemblyDefinition _asm;
    readonly ModuleDefinition _module;
    readonly AssemblyDefinition _core;

    readonly List<string> _applied = new();
    readonly List<string> _skipped = new();

    public Patcher(string path, string dir)
    {
        _path = path;
        _dir = dir;
        _bytes = File.ReadAllBytes(path);

        string corePath = Path.Combine(dir, "UnityEngine.CoreModule.dll");
        if (!File.Exists(corePath))
            throw new Exception($"UnityEngine.CoreModule.dll not found in {dir}");

        _core = AssemblyDefinition.ReadAssembly(corePath);

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(dir);
        _asm = AssemblyDefinition.ReadAssembly(
            new MemoryStream(_bytes),
            new ReaderParameters { AssemblyResolver = resolver, ReadingMode = ReadingMode.Immediate });
        _module = _asm.MainModule;
    }

    public int Run()
    {
        var student = RequireType("StudentScript");
        var qm = RequireType("QualityManagerScript");

        bool t1a = PatchUpdateThrottle(student);
        bool t1b = PatchLateUpdateThrottle(student);

        int t2 = 0;
        foreach (var (prop, val) in GetterDefaults())
            t2 += PatchGetter(RequireType("OptionGlobals"), prop, val) ? 1 : 0;

        int t3 = PatchShadowsGlobal();
        bool t4 = PatchDrawDistance(qm);
        bool t5 = PatchProfileSync(qm);
        PatchDiscordTimestamps();
        PatchTitleLowSpecKey();

        Console.WriteLine("=== YsPerfPatcher ===");
        foreach (var l in _applied) Console.WriteLine($"  [APPLIED] {l}");
        foreach (var l in _skipped) Console.WriteLine($"  [already] {l}");

        int appliedCount = _applied.Count;
        int expected = 2 + GetterDefaults().Length + 5; // throttle(2) + getters(6) + shadows + drawdist + profile + timestamps + F10 key

        if (appliedCount == 0)
        {
            Console.WriteLine("Nothing to do - assembly already patched.");
            return 2;
        }

        string orig = _path + ".orig";
        if (!File.Exists(orig))
            File.WriteAllBytes(orig, _bytes);

        using var ms = new MemoryStream();
        _asm.Write(ms);
        File.WriteAllBytes(_path, ms.ToArray());
        Console.WriteLine($"Wrote {_path} (backup: {orig})");
        Console.WriteLine($"Summary: {appliedCount}/{expected} patch groups applied.");
        return 0;
    }

    TypeDefinition RequireType(string name)
    {
        var t = _module.GetType(name);
        if (t == null) throw new Exception($"type {name} not found");
        return t;
    }

    static (string, int)[] GetterDefaults() => new[]
    {
        ("Vsync", 1),
        ("DisableBloom", 1),
        ("DisableObscurance", 1),
        ("FPSIndex", 2),
        ("ParticleCount", 2),
        ("LowDetailStudents", 2),
    };

    // ---------- UnityEngine references ----------

    MethodReference CoreMethod(string type, string method, int paramCount = -1)
    {
        var t = _core.MainModule.GetType("UnityEngine." + type);
        if (t == null) throw new Exception($"UnityEngine.{type} not found");
        var m = t.Methods.FirstOrDefault(x => x.Name == method
            && (paramCount < 0 || x.Parameters.Count == paramCount));
        if (m == null) throw new Exception($"UnityEngine.{type}.{method} not found");
        return _module.ImportReference(m);
    }

    // ---------- patch 1a: StudentScript.Update ----------

    bool PatchUpdateThrottle(TypeDefinition student)
    {
        var m = student.Methods.FirstOrDefault(x => x.Name == "Update" && x.Parameters.Count == 0);
        if (m == null) throw new Exception("StudentScript.Update not found");
        if (HasMarker(m, "get_frameCount")) { _skipped.Add("StudentScript.Update throttle"); return false; }

        var idField = student.Fields.FirstOrDefault(f => f.Name == "StudentID" && f.FieldType.FullName == "System.Int32")
            ?? throw new Exception("StudentScript.StudentID not found");
        var yandereField = student.Fields.FirstOrDefault(f => f.Name == "Yandere")
            ?? throw new Exception("StudentScript.Yandere not found");

        var getTransform = CoreMethod("Component", "get_transform", 0);
        var getPosition = CoreMethod("Transform", "get_position", 0);
        var distance = CoreMethod("Vector3", "Distance", 2);
        var frameCount = CoreMethod("Time", "get_frameCount", 0);

        var body = m.Body;
        var il = body.GetILProcessor();
        var orig = body.Instructions[0];

        var locD = new VariableDefinition(_module.TypeSystem.Single);
        var locStride = new VariableDefinition(_module.TypeSystem.Int32);
        body.Variables.Add(locD);
        body.Variables.Add(locStride);

        var tTwo = Instruction.Create(OpCodes.Ldc_I4_2);
        var tSt = Instruction.Create(OpCodes.Stloc, locStride);

        var seq = new[]
        {
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Callvirt, getTransform),
            Instruction.Create(OpCodes.Callvirt, getPosition),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, yandereField),
            Instruction.Create(OpCodes.Callvirt, getTransform),
            Instruction.Create(OpCodes.Callvirt, getPosition),
            Instruction.Create(OpCodes.Call, distance),
            Instruction.Create(OpCodes.Stloc, locD),

            Instruction.Create(OpCodes.Ldloc, locD),
            Instruction.Create(OpCodes.Ldc_R4, NearM),
            Instruction.Create(OpCodes.Blt, orig),

            Instruction.Create(OpCodes.Ldloc, locD),
            Instruction.Create(OpCodes.Ldc_R4, FarM),
            Instruction.Create(OpCodes.Blt, tTwo),

            Instruction.Create(OpCodes.Ldc_I4_3),
            Instruction.Create(OpCodes.Br, tSt),

            tTwo,
            tSt,

            Instruction.Create(OpCodes.Call, frameCount),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, idField),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Ldloc, locStride),
            Instruction.Create(OpCodes.Rem),
            Instruction.Create(OpCodes.Brfalse, orig),
            Instruction.Create(OpCodes.Ret),
        };

        foreach (var i in seq) il.InsertBefore(orig, i);
        if (body.MaxStackSize < 8) body.MaxStackSize = 8;

        _applied.Add($"StudentScript.Update throttle (>= {NearM}m every 2nd frame, >= {FarM}m every 3rd)");
        return true;
    }

    // ---------- patch 1b: StudentScript.LateUpdate ----------

    bool PatchLateUpdateThrottle(TypeDefinition student)
    {
        var m = student.Methods.FirstOrDefault(x => x.Name == "LateUpdate");
        if (m == null) throw new Exception("StudentScript.LateUpdate not found");
        if (HasMarker(m, "get_frameCount")) { _skipped.Add("StudentScript.LateUpdate throttle"); return false; }

        var idField = student.Fields.FirstOrDefault(f => f.Name == "StudentID" && f.FieldType.FullName == "System.Int32")
            ?? throw new Exception("StudentScript.StudentID not found");
        var distField = student.Fields.FirstOrDefault(f => f.Name == "DistanceToPlayer")
            ?? throw new Exception("StudentScript.DistanceToPlayer not found");

        var frameCount = CoreMethod("Time", "get_frameCount", 0);

        var body = m.Body;
        var il = body.GetILProcessor();
        var orig = body.Instructions[0];

        var locStride = new VariableDefinition(_module.TypeSystem.Int32);
        body.Variables.Add(locStride);

        var tTwo = Instruction.Create(OpCodes.Ldc_I4_2);
        var tSt = Instruction.Create(OpCodes.Stloc, locStride);

        var seq = new[]
        {
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, distField),
            Instruction.Create(OpCodes.Ldc_R4, NearM),
            Instruction.Create(OpCodes.Blt, orig),

            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, distField),
            Instruction.Create(OpCodes.Ldc_R4, FarM),
            Instruction.Create(OpCodes.Blt, tTwo),

            Instruction.Create(OpCodes.Ldc_I4_3),
            Instruction.Create(OpCodes.Br, tSt),

            tTwo,
            tSt,

            Instruction.Create(OpCodes.Call, frameCount),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, idField),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Ldloc, locStride),
            Instruction.Create(OpCodes.Rem),
            Instruction.Create(OpCodes.Brfalse, orig),
            Instruction.Create(OpCodes.Ret),
        };

        foreach (var i in seq) il.InsertBefore(orig, i);
        if (body.MaxStackSize < 8) body.MaxStackSize = 8;

        _applied.Add("StudentScript.LateUpdate throttle (staggered)");
        return true;
    }

    // ---------- patch 2: OptionGlobals first-run defaults ----------

    bool PatchGetter(TypeDefinition optionGlobals, string prop, int defVal)
    {
        var get = optionGlobals.Methods.FirstOrDefault(x => x.Name == "get_" + prop);
        if (get == null) throw new Exception($"OptionGlobals.get_{prop} not found");
        if (HasMarker(get, "HasKey")) { _skipped.Add($"OptionGlobals.{prop} default"); return false; }
        if (!get.HasBody || get.Body.Instructions.Count == 0)
            throw new Exception($"get_{prop} has no body");

        var instrs = get.Body.Instructions;

        // expected shape: [build key][GetBool|GetInt][ret]
        int valueIdx = -1;
        for (int i = 0; i < instrs.Count; i++)
        {
            if (instrs[i].OpCode == OpCodes.Call
                && instrs[i].Operand is MethodReference mr
                && (mr.Name == "GetBool" || mr.Name == "GetInt"))
            {
                valueIdx = i;
                break;
            }
        }
        if (valueIdx < 1)
            throw new Exception($"get_{prop}: key-build sequence not found");
        if (valueIdx != instrs.Count - 2 || instrs[^1].OpCode != OpCodes.Ret)
            throw new Exception($"get_{prop}: unexpected method shape");

        string suffix = instrs.Take(valueIdx)
            .Select(i => i.Operand)
            .OfType<string>()
            .FirstOrDefault(s => s.StartsWith("_")) ?? "?";

        var keyInstrs = instrs.Take(valueIdx).ToList();

        static Instruction CloneKeyInstr(Instruction src)
        {
            if (src.Operand is Instruction || src.Operand is Instruction[])
                throw new Exception("branch inside key build - unsupported");
            var c = Instruction.Create(OpCodes.Nop);
            c.OpCode = src.OpCode;
            c.Operand = src.Operand;
            return c;
        }

        var hasKey = CoreMethod("PlayerPrefs", "HasKey", 1);
        var setInt = CoreMethod("PlayerPrefs", "SetInt", 2);
        var save = CoreMethod("PlayerPrefs", "Save", 0);

        var body = get.Body;
        var il = body.GetILProcessor();
        var first = body.Instructions[0];

        var brfalse = Instruction.Create(OpCodes.Brfalse, first);
        foreach (var i in keyInstrs)
            il.InsertBefore(first, CloneKeyInstr(i));
        il.InsertBefore(first, Instruction.Create(OpCodes.Call, hasKey));
        il.InsertBefore(first, brfalse);

        Instruction? firstAppended = null;
        foreach (var i in keyInstrs)
        {
            var c = CloneKeyInstr(i);
            il.Append(c);
            firstAppended ??= c;
        }
        il.Append(Instruction.Create(OpCodes.Ldc_I4, defVal));
        il.Append(Instruction.Create(OpCodes.Call, setInt));
        il.Append(Instruction.Create(OpCodes.Call, save));
        il.Append(Instruction.Create(OpCodes.Ldc_I4, defVal));
        il.Append(Instruction.Create(OpCodes.Ret));
        brfalse.Operand = firstAppended!;

        _applied.Add($"OptionGlobals.{prop} first-run default = {defVal} (Profile_*{suffix})");
        return true;
    }

    // ---------- patch 3: Soft -> Hard shadows everywhere ----------

    int PatchShadowsGlobal()
    {
        int n = 0;
        foreach (var type in _module.GetTypes())
        {
            foreach (var m in type.Methods)
            {
                if (!m.HasBody) continue;
                var instrs = m.Body.Instructions;
                for (int i = 0; i < instrs.Count; i++)
                {
                    var ins = instrs[i];
                    if (ins.OpCode != OpCodes.Call) continue;
                    if (ins.Operand is not MethodReference mr) continue;
                    if (mr.Name != "get_EnableShadows") continue;

                    int limit = Math.Min(instrs.Count - 2, i + 12);
                    for (int j = i + 1; j <= limit; j++)
                    {
                        var c = instrs[j];
                        bool isTwo = c.OpCode == OpCodes.Ldc_I4_2
                            || (c.OpCode == OpCodes.Ldc_I4 && c.Operand is int iv && iv == 2);
                        if (!isTwo) continue;

                        var next = instrs[j + 1];
                        if ((next.OpCode == OpCodes.Call || next.OpCode == OpCodes.Callvirt)
                            && next.Operand is MethodReference sm && sm.Name == "set_shadows")
                        {
                            c.OpCode = OpCodes.Ldc_I4_1;
                            c.Operand = null;
                            n++;
                            break;
                        }
                    }
                }
            }
        }

        if (n > 0) _applied.Add($"shadows Soft->Hard at {n} site(s)");
        else _skipped.Add("shadows Soft->Hard (no Soft sites found)");
        return n;
    }

    // ---------- patch 4: QualityManagerScript.Start DrawDistance 350 -> 200 ----------

    bool PatchDrawDistance(TypeDefinition qm)
    {
        var start = qm.Methods.FirstOrDefault(x => x.Name == "Start");
        if (start == null) throw new Exception("QualityManagerScript.Start not found");
        var instrs = start.Body.Instructions;

        for (int i = 1; i < instrs.Count; i++)
        {
            var ins = instrs[i];
            if (ins.OpCode != OpCodes.Call) continue;
            if (ins.Operand is not MethodReference mr) continue;
            if (mr.Name != "set_DrawDistance") continue;

            var prev = instrs[i - 1];
            bool is350 = prev.OpCode == OpCodes.Ldc_I4 && prev.Operand is int v && v == 350;
            bool is200 = prev.OpCode == OpCodes.Ldc_I4 && prev.Operand is int v2 && v2 == 200;
            if (is350)
            {
                prev.Operand = 200;
                _applied.Add("QualityManagerScript.Start DrawDistance 350 -> 200 (limit stays 350)");
                return true;
            }
            if (is200)
            {
                _skipped.Add("QualityManagerScript.Start DrawDistance = 200");
                return false;
            }
        }

        throw new Exception("DrawDistance initialization (ldc.i4 350; call set_DrawDistance) not found in QualityManagerScript.Start");
    }

    // ---------- patch 5: inject Profile.bloom / ambientOcclusion sync into QM.Start ----------

    bool PatchProfileSync(TypeDefinition qm)
    {
        var start = qm.Methods.FirstOrDefault(x => x.Name == "Start");
        if (start == null) throw new Exception("QualityManagerScript.Start not found");
        if (HasMarker(start, "get_DisableBloom"))
        {
            _skipped.Add("QualityManagerScript.Start Profile bloom/AO sync");
            return false;
        }

        var instrs = start.Body.Instructions;
        int a = -1;
        for (int i = 0; i < instrs.Count; i++)
        {
            if (instrs[i].OpCode == OpCodes.Call
                && instrs[i].Operand is MethodReference mr && mr.Name == "get_MotionBlur")
            {
                a = i;
                break;
            }
        }
        if (a < 0) throw new Exception("anchor (call OptionGlobals::get_MotionBlur) not found in QualityManagerScript.Start");
        if (a + 2 >= instrs.Count) throw new Exception("anchor too close to end of method");

        static FieldReference LdfldAt(Instruction i, string name)
        {
            if (i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference f && f.Name == name) return f;
            throw new Exception($"expected ldfld {name} near anchor, got {i.OpCode} {(i.Operand ?? "")}");
        }

        var fYandere = LdfldAt(instrs[a - 5], "Yandere");
        var fPauseScreen = LdfldAt(instrs[a - 4], "PauseScreen");
        var fNewSettings = LdfldAt(instrs[a - 3], "NewSettings");
        var fProfile = LdfldAt(instrs[a - 2], "Profile");

        var setEnabledDef = ((MethodReference)instrs[a + 1].Operand).Resolve()
            ?? throw new Exception("cannot resolve set_enabled");
        var setEnabled = _module.ImportReference(setEnabledDef);

        var profileType = fProfile.FieldType.Resolve()
            ?? throw new Exception("cannot resolve PostProcessingProfile type");
        var fBloom = profileType.Fields.FirstOrDefault(f => f.Name == "bloom")
            ?? throw new Exception("PostProcessingProfile.bloom not found");
        var fAo = profileType.Fields.FirstOrDefault(f => f.Name == "ambientOcclusion")
            ?? throw new Exception("PostProcessingProfile.ambientOcclusion not found");

        var optionGlobals = RequireType("OptionGlobals");
        var getBloom = optionGlobals.Methods.First(x => x.Name == "get_DisableBloom");
        var getAo = optionGlobals.Methods.First(x => x.Name == "get_DisableObscurance");

        var insertBefore = instrs[a + 2];
        var il = start.Body.GetILProcessor();

        void EmitSync(FieldReference model, MethodDefinition getter)
        {
            var chain = new[]
            {
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Ldfld, fYandere),
                Instruction.Create(OpCodes.Ldfld, fPauseScreen),
                Instruction.Create(OpCodes.Ldfld, fNewSettings),
                Instruction.Create(OpCodes.Ldfld, fProfile),
                Instruction.Create(OpCodes.Ldfld, model),
                Instruction.Create(OpCodes.Call, getter),
                Instruction.Create(OpCodes.Ldc_I4_0),
                Instruction.Create(OpCodes.Ceq),
                Instruction.Create(OpCodes.Callvirt, setEnabled),
            };
            foreach (var i in chain) il.InsertBefore(insertBefore, i);
        }

        EmitSync(fBloom, getBloom);
        EmitSync(fAo, getAo);

        if (start.Body.MaxStackSize < 6) start.Body.MaxStackSize = 6;

        _applied.Add("QualityManagerScript.Start injects Profile.bloom/ambientOcclusion sync");
        return true;
    }

    // ---------- patch 6: DiscordRPC.UpdateActivity -> Activity.Timestamps.Start (elapsed) ----------

    bool PatchDiscordTimestamps()
    {
        var t = RequireType("DiscordRPC");
        var m = t.Methods.FirstOrDefault(x => x.Name == "UpdateActivity" && x.Parameters.Count == 0)
            ?? throw new Exception("DiscordRPC.UpdateActivity not found");
        if (HasMarker(m, "ToUnixTimeSeconds") || HasMarker(m, "get_Ticks"))
        {
            _skipped.Add("DiscordRPC.UpdateActivity presence start timestamp");
            return false;
        }

        var body = m.Body;
        var il = body.GetILProcessor();
        var instrs = body.Instructions;

        // Discord.Activity / ActivityTimestamps live in Assembly-CSharp-firstpass;
        // recover the type refs from existing member refs in this method.
        Instruction? assetsLdflda = null;
        foreach (var i in instrs)
        {
            if (i.OpCode == OpCodes.Ldflda && i.Operand is FieldReference fr && fr.Name == "Assets")
            {
                assetsLdflda = i;
                break;
            }
        }
        if (assetsLdflda == null)
            throw new Exception("anchor (ldflda Discord.Activity::Assets) not found in DiscordRPC.UpdateActivity");

        var activityDef = ((FieldReference)assetsLdflda.Operand).DeclaringType.Resolve()
            ?? throw new Exception("cannot resolve Discord.Activity");
        var tsFieldDef = activityDef.Fields.FirstOrDefault(f => f.Name == "Timestamps")
            ?? throw new Exception("Discord.Activity.Timestamps field not found");
        var attRef = _module.ImportReference(tsFieldDef.FieldType);
        var fTimestamps = _module.ImportReference(tsFieldDef);
        var fAttStart = new FieldReference("Start", _module.TypeSystem.Int64, attRef);

        // Activity temp local: [ldloca.s V_x][initobj Discord.Activity]
        VariableDefinition? actLoc = null;
        for (int i = 1; i < instrs.Count; i++)
        {
            if (instrs[i].OpCode == OpCodes.Initobj
                && instrs[i].Operand is TypeReference tr && tr.FullName == "Discord.Activity"
                && instrs[i - 1].OpCode == OpCodes.Ldloca_S
                && instrs[i - 1].Operand is VariableDefinition vd)
            {
                actLoc = vd;
                break;
            }
        }
        if (actLoc == null)
            throw new Exception("Activity temp local (ldloca.s; initobj Discord.Activity) not found");

        // cached static start time (unix seconds), set once per process
        var fStart = t.Fields.FirstOrDefault(f => f.Name == "_presenceStart");
        if (fStart == null)
        {
            fStart = new FieldDefinition("_presenceStart",
                FieldAttributes.Static | FieldAttributes.Private, _module.TypeSystem.Int64);
            t.Fields.Add(fStart);
        }

        // Entry guard: if (_presenceStart == 0) _presenceStart = (DateTime.Now.Ticks - epochTicks)/10^7.
        // IMPORTANT: reuse member refs the game itself already uses (e.g. CalendarScript::Start).
        // Hand-made TypeReference/MethodReference rows for System.DateTime members cause
        // BadImageFormatException ("Expected reference type but got type kind 17") in Unity's mono;
        // only the game's pre-existing refs resolve correctly (DateTime vs DateTimeOffset irrelevant).
        MethodReference? getNowMr = null, getTicksMr = null;
        foreach (var tt in _module.Types)
        foreach (var mm in tt.Methods)
        {
            if (!mm.HasBody || getNowMr != null && getTicksMr != null) continue;
            foreach (var ii in mm.Body.Instructions)
            {
                if (ii.OpCode != OpCodes.Call || ii.Operand is not MethodReference mrx) continue;
                if (mrx.FullName == "System.DateTime System.DateTime::get_Now()") getNowMr ??= mrx;
                else if (mrx.FullName == "System.Int64 System.DateTime::get_Ticks()") getTicksMr ??= mrx;
            }
        }
        if (getNowMr == null || getTicksMr == null)
            throw new Exception("existing DateTime member refs not found in game assembly");

        var locDt = new VariableDefinition(getNowMr.DeclaringType);
        body.Variables.Add(locDt);
        var first = body.Instructions[0];
        il.InsertBefore(first, Instruction.Create(OpCodes.Ldsfld, fStart));
        il.InsertBefore(first, Instruction.Create(OpCodes.Brtrue_S, first));
        il.InsertBefore(first, Instruction.Create(OpCodes.Call, getNowMr));
        il.InsertBefore(first, Instruction.Create(OpCodes.Stloc, locDt));
        il.InsertBefore(first, Instruction.Create(OpCodes.Ldloca, locDt));
        il.InsertBefore(first, Instruction.Create(OpCodes.Call, getTicksMr));
        il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I8, 621355968000000000L));
        il.InsertBefore(first, Instruction.Create(OpCodes.Sub));
        il.InsertBefore(first, Instruction.Create(OpCodes.Ldc_I8, 10000000L));
        il.InsertBefore(first, Instruction.Create(OpCodes.Div));
        il.InsertBefore(first, Instruction.Create(OpCodes.Stsfld, fStart));

        // anchor: stfld Discord.Activity::State (last initializer assignment before the struct is loaded)
        int stateIdx = -1;
        for (int i = 0; i < instrs.Count; i++)
        {
            if (instrs[i].OpCode == OpCodes.Stfld
                && instrs[i].Operand is FieldReference sf
                && sf.Name == "State"
                && sf.DeclaringType.FullName == "Discord.Activity")
            {
                stateIdx = i;
                break;
            }
        }
        if (stateIdx < 0 || stateIdx + 1 >= instrs.Count)
            throw new Exception("anchor (stfld Discord.Activity::State) not found in DiscordRPC.UpdateActivity");

        var locTs = new VariableDefinition(attRef);
        body.Variables.Add(locTs);
        var target = instrs[stateIdx + 1];
        var seq = new[]
        {
            Instruction.Create(OpCodes.Ldloca, actLoc),
            Instruction.Create(OpCodes.Ldloca, locTs),
            Instruction.Create(OpCodes.Initobj, attRef),
            Instruction.Create(OpCodes.Ldloca, locTs),
            Instruction.Create(OpCodes.Ldsfld, fStart),
            Instruction.Create(OpCodes.Stfld, fAttStart),
            Instruction.Create(OpCodes.Ldloc, locTs),
            Instruction.Create(OpCodes.Stfld, fTimestamps),
        };
        foreach (var i in seq) il.InsertBefore(target, i);

        if (body.MaxStackSize < 4) body.MaxStackSize = 4;

        _applied.Add("DiscordRPC.UpdateActivity sets Activity.Timestamps.Start (elapsed timer, cached once)");
        return true;
    }

    // ---------- patch 13: NewTitleScreenScript F10 low spec key ----------

    bool PatchTitleLowSpecKey()
    {
        var t = RequireType("NewTitleScreenScript");
        var m = t.Methods.FirstOrDefault(x => x.Name == "Update" && x.Parameters.Count == 0);
        if (m == null) throw new Exception("NewTitleScreenScript.Update not found");
        if (!m.HasBody) throw new Exception("NewTitleScreenScript.Update has no body");
        if (m.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && (i.Operand as string) == "f10"))
        {
            _skipped.Add("NewTitleScreenScript.Update F10 low spec key");
            return false;
        }

        var instrs = m.Body.Instructions;

        // steal member refs from this method (or module), the game already calls all of them
        MethodReference? getKey = null, setInt = null, save = null, setText = null;

        foreach (var i in instrs)
        {
            if ((i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is MethodReference mr)
            {
                if (mr.Name == "GetKeyDown" && mr.DeclaringType.FullName == "UnityEngine.Input"
                    && mr.Parameters.Count == 1 && mr.Parameters[0].ParameterType.FullName == "System.String")
                    getKey ??= mr;
                else if (mr.Name == "SetInt" && mr.DeclaringType.FullName == "UnityEngine.PlayerPrefs")
                    setInt ??= mr;
            }
        }

        // refs not used in this method: scan the whole module (same trick as PlayerPrefs.Save)
        foreach (var tt in _module.Types)
        foreach (var mm in tt.Methods)
        {
            if (!mm.HasBody) continue;
            foreach (var ii in mm.Body.Instructions)
            {
                if (ii.OpCode != OpCodes.Call && ii.OpCode != OpCodes.Callvirt) continue;
                if (ii.Operand is not MethodReference mr) continue;
                if (save == null && mr.FullName == "System.Void UnityEngine.PlayerPrefs::Save()") save = mr;
                else if (setText == null && mr.Name == "set_text" && mr.Parameters.Count == 1
                    && mr.Parameters[0].ParameterType.FullName == "System.String"
                    && mr.DeclaringType.Name == "UILabel") setText = mr;
            }
        }

        var pbField = t.Fields.FirstOrDefault(f => f.Name == "PromptBar" && f.FieldType.Name == "PromptBarScript");
        var pbType = pbField?.FieldType.Resolve();
        var mClear = pbType?.Methods.FirstOrDefault(x => x.Name == "ClearButtons");
        var mUpdate = pbType?.Methods.FirstOrDefault(x => x.Name == "UpdateButtons");
        var fLabel = pbType?.Fields.FirstOrDefault(f => f.Name == "Label" && f.FieldType.IsArray);
        var fShow = pbType?.Fields.FirstOrDefault(f => f.Name == "Show" && f.FieldType.FullName == "System.Boolean");

        var missing = new List<string>();
        if (getKey == null) missing.Add("GetKeyDown");
        if (setInt == null) missing.Add("SetInt");
        if (save == null) missing.Add("Save");
        if (setText == null) missing.Add("UILabel.set_text");
        if (pbField == null) missing.Add("NewTitleScreenScript.PromptBar");
        if (mClear == null) missing.Add("PromptBarScript.ClearButtons");
        if (mUpdate == null) missing.Add("PromptBarScript.UpdateButtons");
        if (fLabel == null) missing.Add("PromptBarScript.Label");
        if (fShow == null) missing.Add("PromptBarScript.Show");
        if (missing.Count > 0)
            throw new Exception("member refs not found: " + string.Join(", ", missing));

        // vanilla OptionGlobals getters build every option key as "Profile_0_<name>"
        // (ldc.i4.0 in Assembly-CSharp.dll.orig): options are global, not per save
        (string Key, int Value)[] values =
        {
            ("Profile_0_Vsync", 1),
            ("Profile_0_FPSIndex", 2),
            ("Profile_0_DisableBloom", 1),
            ("Profile_0_DisableObscurance", 1),
            ("Profile_0_ParticleCount", 2),
            ("Profile_0_LowDetailStudents", 2),
            ("Profile_0_DrawDistance", 200),
            ("Profile_0_EnableShadows", 0),
            ("Profile_0_MotionBlur", 0),
            ("Profile_0_DepthOfField", 0),
            ("Profile_0_DisablePostAliasing", 0),
            ("Profile_0_RimLight", 0),
            ("Profile_0_Fog", 0),
            ("Profile_0_DisableFarAnimations", 1),
            ("Profile_0_ToggleGrass", 0),
            ("Profile_0_DisableMirrors", 1),
            ("Profile_0_DisableOutlines", 1),
            ("Profile_0_OpaqueWindows", 1),
            ("Profile_0_ColorGrading", 0),
        };

        var body = m.Body;
        var il = body.GetILProcessor();
        var orig = body.Instructions[0];

        var seq = new List<Instruction>
        {
            Instruction.Create(OpCodes.Ldstr, "f10"),
            Instruction.Create(OpCodes.Call, getKey),
            Instruction.Create(OpCodes.Brfalse, orig),
        };
        foreach (var (key, v) in values)
        {
            seq.Add(Instruction.Create(OpCodes.Ldstr, key));
            seq.Add(Instruction.Create(OpCodes.Ldc_I4, v));
            seq.Add(Instruction.Create(OpCodes.Call, setInt));
        }
        seq.Add(Instruction.Create(OpCodes.Call, save));

        seq.Add(Instruction.Create(OpCodes.Ldarg_0));
        seq.Add(Instruction.Create(OpCodes.Ldfld, pbField));
        seq.Add(Instruction.Create(OpCodes.Brfalse, orig));
        seq.Add(Instruction.Create(OpCodes.Ldarg_0));
        seq.Add(Instruction.Create(OpCodes.Ldfld, pbField));
        seq.Add(Instruction.Create(OpCodes.Callvirt, mClear));
        seq.Add(Instruction.Create(OpCodes.Ldarg_0));
        seq.Add(Instruction.Create(OpCodes.Ldfld, pbField));
        seq.Add(Instruction.Create(OpCodes.Ldfld, fLabel));
        seq.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        seq.Add(Instruction.Create(OpCodes.Ldelem_Ref));
        seq.Add(Instruction.Create(OpCodes.Ldstr, "Low Spec Preset Applied"));
        seq.Add(Instruction.Create(OpCodes.Callvirt, setText));
        seq.Add(Instruction.Create(OpCodes.Ldarg_0));
        seq.Add(Instruction.Create(OpCodes.Ldfld, pbField));
        seq.Add(Instruction.Create(OpCodes.Callvirt, mUpdate));
        seq.Add(Instruction.Create(OpCodes.Ldarg_0));
        seq.Add(Instruction.Create(OpCodes.Ldfld, pbField));
        seq.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        seq.Add(Instruction.Create(OpCodes.Stfld, fShow));

        foreach (var i in seq) il.InsertBefore(orig, i);
        if (body.MaxStackSize < 4) body.MaxStackSize = 4;

        _applied.Add("NewTitleScreenScript.Update F10 writes low spec preset to Profile_0_* (global options) + prompt bar feedback");
        return true;
    }

    // ---------- helpers ----------

    static bool HasMarker(MethodDefinition m, string callName)
    {
        if (!m.HasBody) return false;
        return m.Body.Instructions.Any(i =>
            (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
            && i.Operand is MethodReference mr && mr.Name == callName);
    }
}
