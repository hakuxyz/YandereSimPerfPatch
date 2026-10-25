using Mono.Cecil;
using Mono.Cecil.Cil;

namespace YsPerfPatcher.Groups;

public sealed class GetterDefaultsGroup : PatchGroup
{
    public override string Name => "OptionGlobals first-run defaults";
    public override IReadOnlyList<string> TargetTypes => new[] { "OptionGlobals" };
    public override IReadOnlyList<string> RequiredMembers => new[]
    {
        "get_Vsync", "get_DisableBloom", "get_DisableObscurance",
        "get_FPSIndex", "get_ParticleCount", "get_LowDetailStudents",
    };

    public override void Apply(PatchContext ctx)
    {
        var optionGlobals = ctx.Inspector.RequireType("OptionGlobals");
        foreach (var setting in ctx.Profile.FirstRunDefaults)
            PatchGetter(ctx, optionGlobals, setting);
    }

    public override void Validate(PatchContext ctx)
    {
        var optionGlobals = ctx.Inspector.RequireType("OptionGlobals");
        foreach (var setting in ctx.Profile.FirstRunDefaults)
        {
            var get = ctx.Inspector.RequireMethod(optionGlobals, "get_" + setting.PropertyName);
            if (!ctx.Inspector.HasMarker(get, "HasKey"))
                throw new Exception($"validate: OptionGlobals.get_{setting.PropertyName} missing HasKey marker after apply");
        }
    }

    static void PatchGetter(PatchContext ctx, TypeDefinition optionGlobals, OptionSetting setting)
    {
        string prop = setting.PropertyName;
        int defVal = setting.DefaultValue;
        var inspector = ctx.Inspector;

        var get = inspector.RequireMethod(optionGlobals, "get_" + prop);
        if (inspector.HasMarker(get, "HasKey")) { ctx.ReportSkipped($"OptionGlobals.{prop} default"); return; }
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
            .FirstOrDefault(s => s.StartsWith(setting.KeyPrefix)) ?? "?";

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

        var hasKey = inspector.ImportUnityMethod("PlayerPrefs", "HasKey", 1);
        var setInt = inspector.ImportUnityMethod("PlayerPrefs", "SetInt", 2);
        var save = inspector.ImportUnityMethod("PlayerPrefs", "Save", 0);

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

        ctx.ReportApplied($"OptionGlobals.{prop} first-run default = {defVal} (Profile_*{suffix})");
    }
}
