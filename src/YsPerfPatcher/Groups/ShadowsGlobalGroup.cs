using Mono.Cecil;
using Mono.Cecil.Cil;

namespace YsPerfPatcher.Groups;

public sealed class ShadowsGlobalGroup : PatchGroup
{
    public override string Name => "shadows Soft->Hard";
    public override IReadOnlyList<string> TargetTypes => new[] { "*" }; // module-wide scan
    public override IReadOnlyList<string> RequiredMembers => new[] { "get_EnableShadows", "set_shadows" };

    public override void Apply(PatchContext ctx)
    {
        int n = 0;
        foreach (var type in ctx.Inspector.AllTypes)
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

        if (n > 0) ctx.ReportApplied($"shadows Soft->Hard at {n} site(s)");
        else ctx.ReportSkipped("shadows Soft->Hard (no Soft sites found)");
    }
}
