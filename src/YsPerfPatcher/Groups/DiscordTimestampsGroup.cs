using Mono.Cecil;
using Mono.Cecil.Cil;

namespace YsPerfPatcher.Groups;

public sealed class DiscordTimestampsGroup : PatchGroup
{
    public override string Name => "Discord presence timestamps";
    public override IReadOnlyList<string> TargetTypes => new[] { "DiscordRPC" };
    public override IReadOnlyList<string> RequiredMembers => new[] { "UpdateActivity" };

    public override bool CanApply(PatchContext ctx, out string skipReason)
    {
        var t = ctx.Inspector.RequireType("DiscordRPC");
        var m = ctx.Inspector.RequireMethod(t, "UpdateActivity", 0);
        if (ctx.Inspector.HasMarker(m, "ToUnixTimeSeconds") || ctx.Inspector.HasMarker(m, "get_Ticks"))
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
        var t = inspector.RequireType("DiscordRPC");
        var m = inspector.RequireMethod(t, "UpdateActivity", 0);
        var module = inspector.Module;

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
        var attRef = module.ImportReference(tsFieldDef.FieldType);
        var fTimestamps = module.ImportReference(tsFieldDef);
        var fAttStart = new FieldReference("Start", module.TypeSystem.Int64, attRef);

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
        var fStart = inspector.FindField(t, "_presenceStart");
        if (fStart == null)
        {
            fStart = new FieldDefinition("_presenceStart",
                FieldAttributes.Static | FieldAttributes.Private, module.TypeSystem.Int64);
            t.Fields.Add(fStart);
        }

        // Entry guard: if (_presenceStart == 0) _presenceStart = (DateTime.Now.Ticks - epochTicks)/10^7.
        // IMPORTANT: reuse member refs the game itself already uses (e.g. CalendarScript::Start).
        // Hand-made TypeReference/MethodReference rows for System.DateTime members cause
        // BadImageFormatException ("Expected reference type but got type kind 17") in Unity's mono;
        // only the game's pre-existing refs resolve correctly (DateTime vs DateTimeOffset irrelevant).
        var getNowMr = inspector.RequireExistingCall(
            mr => mr.FullName == "System.DateTime System.DateTime::get_Now()",
            "DateTime.get_Now");
        var getTicksMr = inspector.RequireExistingCall(
            mr => mr.FullName == "System.Int64 System.DateTime::get_Ticks()",
            "DateTime.get_Ticks");

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

        ctx.ReportApplied("DiscordRPC.UpdateActivity sets Activity.Timestamps.Start (elapsed timer, cached once)");
    }

    public override void Validate(PatchContext ctx)
    {
        var t = ctx.Inspector.RequireType("DiscordRPC");
        var m = ctx.Inspector.RequireMethod(t, "UpdateActivity", 0);
        if (!ctx.Inspector.HasMarker(m, "get_Ticks"))
            throw new Exception("validate: Discord timestamp marker missing after apply");
        if (ctx.Inspector.FindField(t, "_presenceStart") == null)
            throw new Exception("validate: DiscordRPC._presenceStart field missing after apply");
    }
}
