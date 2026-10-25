using Mono.Cecil;
using Mono.Cecil.Cil;

namespace YsPerfPatcher.Groups;

public sealed class ProfileSyncGroup : PatchGroup
{
    public override string Name => "Profile bloom/AO sync";
    public override IReadOnlyList<string> TargetTypes => new[] { "QualityManagerScript", "OptionGlobals" };
    public override IReadOnlyList<string> RequiredMembers => new[]
    {
        "Start", "Yandere", "PauseScreen", "NewSettings", "Profile",
        "get_DisableBloom", "get_DisableObscurance", "get_MotionBlur",
    };

    public override bool CanApply(PatchContext ctx, out string skipReason)
    {
        var qm = ctx.Inspector.RequireType("QualityManagerScript");
        var start = ctx.Inspector.RequireMethod(qm, "Start");
        if (ctx.Inspector.HasMarker(start, "get_DisableBloom"))
        {
            skipReason = "already applied";
            return false;
        }
        skipReason = "";
        return true;
    }

    public override void Apply(PatchContext ctx)
    {
        var inspector = ctx.Inspector;
        var qm = inspector.RequireType("QualityManagerScript");
        var start = inspector.RequireMethod(qm, "Start");
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
        var setEnabled = inspector.Module.ImportReference(setEnabledDef);

        var profileType = fProfile.FieldType.Resolve()
            ?? throw new Exception("cannot resolve PostProcessingProfile type");
        var fBloom = profileType.Fields.FirstOrDefault(f => f.Name == "bloom")
            ?? throw new Exception("PostProcessingProfile.bloom not found");
        var fAo = profileType.Fields.FirstOrDefault(f => f.Name == "ambientOcclusion")
            ?? throw new Exception("PostProcessingProfile.ambientOcclusion not found");

        var optionGlobals = inspector.RequireType("OptionGlobals");
        var getBloom = inspector.RequireMethod(optionGlobals, "get_DisableBloom");
        var getAo = inspector.RequireMethod(optionGlobals, "get_DisableObscurance");

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

        ctx.ReportApplied("QualityManagerScript.Start injects Profile.bloom/ambientOcclusion sync");
    }

    public override void Validate(PatchContext ctx)
    {
        var qm = ctx.Inspector.RequireType("QualityManagerScript");
        var start = ctx.Inspector.RequireMethod(qm, "Start");
        if (!ctx.Inspector.HasMarker(start, "get_DisableBloom"))
            throw new Exception("validate: Profile sync marker missing after apply");
    }
}
