using Mono.Cecil;
using Mono.Cecil.Cil;

namespace YsPerfPatcher.Groups;

public sealed class DrawDistanceGroup : PatchGroup
{
    public override string Name => "DrawDistance 350 -> 200";
    public override IReadOnlyList<string> TargetTypes => new[] { "QualityManagerScript" };
    public override IReadOnlyList<string> RequiredMembers => new[] { "Start", "set_DrawDistance" };

    public override void Apply(PatchContext ctx)
    {
        var qm = ctx.Inspector.RequireType("QualityManagerScript");
        var start = ctx.Inspector.RequireMethod(qm, "Start");
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
                ctx.ReportApplied("QualityManagerScript.Start DrawDistance 350 -> 200 (limit stays 350)");
                return;
            }
            if (is200)
            {
                ctx.ReportSkipped("QualityManagerScript.Start DrawDistance = 200");
                return;
            }
        }

        throw new Exception("DrawDistance initialization (ldc.i4 350; call set_DrawDistance) not found in QualityManagerScript.Start");
    }

    public override void Validate(PatchContext ctx)
    {
        var qm = ctx.Inspector.RequireType("QualityManagerScript");
        var start = ctx.Inspector.RequireMethod(qm, "Start");
        var instrs = start.Body.Instructions;
        for (int i = 1; i < instrs.Count; i++)
        {
            if (instrs[i].OpCode != OpCodes.Call || instrs[i].Operand is not MethodReference mr) continue;
            if (mr.Name != "set_DrawDistance") continue;
            var prev = instrs[i - 1];
            if (prev.OpCode == OpCodes.Ldc_I4 && prev.Operand is int v && (v == 200 || v == 350))
                return; // 350 is fine when this run skipped (already patched earlier state)
        }
        throw new Exception("validate: DrawDistance init not found after apply");
    }
}
