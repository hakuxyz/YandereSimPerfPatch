using Mono.Cecil;
using Mono.Cecil.Cil;

namespace YsPerfPatcher.Groups;

public sealed class LowSpecKeyGroup : PatchGroup
{
    public override string Name => "F10 low-spec key";
    public override IReadOnlyList<string> TargetTypes => new[] { "NewTitleScreenScript" };
    public override IReadOnlyList<string> RequiredMembers => new[] { "Update", "DebugLog", "PromptBar" };

    public override bool CanApply(PatchContext ctx, out string skipReason)
    {
        var t = ctx.Inspector.RequireType("NewTitleScreenScript");
        var m = ctx.Inspector.RequireMethod(t, "Update", 0);
        if (!m.HasBody) throw new Exception("NewTitleScreenScript.Update has no body");
        if (m.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && (i.Operand as string) == "f10"))
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
        var module = inspector.Module;
        var t = inspector.RequireType("NewTitleScreenScript");
        var m = inspector.RequireMethod(t, "Update", 0);
        var instrs = m.Body.Instructions;

        // steal member refs from this method (or module), the game already calls all of them
        MethodReference? getKey = null, setInt = null;
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

        // refs not used in this method: scan the whole module
        // steal existing MethodReferences: hand-building one produces a broken MemberRef token
        var save = inspector.RequireExistingCall(
            mr => mr.FullName == "System.Void UnityEngine.PlayerPrefs::Save()", "PlayerPrefs.Save");
        var setText = inspector.RequireExistingCall(
            mr => mr.Name == "set_text" && mr.Parameters.Count == 1
                && mr.Parameters[0].ParameterType.FullName == "System.String"
                && mr.DeclaringType.Name == "UILabel", "UILabel.set_text");
        var getText = inspector.RequireExistingCall(
            mr => mr.Name == "get_text" && mr.Parameters.Count == 0
                && mr.ReturnType.FullName == "System.String"
                && mr.DeclaringType.Name == "UILabel", "UILabel.get_text");
        var getTimeDelta = inspector.RequireExistingCall(
            mr => mr.FullName == "System.Single UnityEngine.Time::get_deltaTime()", "Time.get_deltaTime");
        var debugLog = inspector.RequireExistingCall(
            mr => mr.FullName == "System.Void UnityEngine.Debug::Log(System.Object)", "Debug.Log");
        var getGameObject = inspector.RequireExistingCall(
            mr => mr.Name == "get_gameObject" && mr.HasThis
                && mr.Parameters.Count == 0 && mr.ReturnType.FullName == "UnityEngine.GameObject"
                && mr.DeclaringType.FullName == "UnityEngine.Component", "Component.get_gameObject");
        var getActiveSelf = inspector.RequireExistingCall(
            mr => mr.FullName == "System.Boolean UnityEngine.GameObject::get_activeSelf()", "GameObject.get_activeSelf");
        var setAct = inspector.RequireExistingCall(
            mr => mr.FullName == "System.Void UnityEngine.GameObject::SetActive(System.Boolean)", "GameObject.SetActive");
        var objInequality = inspector.RequireExistingCall(
            mr => mr.Name == "op_Inequality" && mr.Parameters.Count == 2
                && mr.Parameters[0].ParameterType.FullName == "UnityEngine.Object"
                && mr.Parameters[1].ParameterType.FullName == "UnityEngine.Object"
                && mr.ReturnType.FullName == "System.Boolean"
                && mr.DeclaringType.FullName == "UnityEngine.Object", "Object.op_Inequality");

        if (getKey == null || setInt == null)
        {
            var missing = new List<string>();
            if (getKey == null) missing.Add("GetKeyDown");
            if (setInt == null) missing.Add("SetInt");
            throw new Exception("member refs not found: " + string.Join(", ", missing));
        }

        var dbgLogField = inspector.RequireField(t, "DebugLog", "UILabel");
        var pbField = inspector.RequireField(t, "PromptBar");
        var pbType = pbField.FieldType.Resolve() ?? throw new Exception("cannot resolve PromptBarScript");
        var mClear = inspector.RequireMethod(pbType, "ClearButtons");
        var mUpdate = inspector.RequireMethod(pbType, "UpdateButtons");
        var fLabel = inspector.RequireField(pbType, "Label");
        if (!fLabel.FieldType.IsArray) throw new Exception("PromptBarScript.Label is not an array");
        var fShow = inspector.RequireField(pbType, "Show", "Boolean");

        // static state for the top notification (timer + cached label + original text)
        var lblTypeRef = (TypeReference)setText.DeclaringType;
        var fNotifTimer = new FieldDefinition("F10NotifTimer", FieldAttributes.Static | FieldAttributes.Private, module.TypeSystem.Single);
        var fNotifOrig = new FieldDefinition("F10NotifOriginal", FieldAttributes.Static | FieldAttributes.Private, module.TypeSystem.String);
        var fNotifLabel = new FieldDefinition("F10NotifLabel", FieldAttributes.Static | FieldAttributes.Private, lblTypeRef);
        var fNotifWasActive = new FieldDefinition("F10NotifWasActive", FieldAttributes.Static | FieldAttributes.Private, module.TypeSystem.Boolean);
        t.Fields.Add(fNotifTimer);
        t.Fields.Add(fNotifOrig);
        t.Fields.Add(fNotifLabel);
        t.Fields.Add(fNotifWasActive);

        var body = m.Body;
        var il = body.GetILProcessor();
        var orig = body.Instructions[0];

        // branch targets for the every-frame timer countdown
        var lblDecrement = Instruction.Create(OpCodes.Nop);
        var lblContinue = Instruction.Create(OpCodes.Nop);

        var seq = new List<Instruction>
        {
            // --- every frame: notification timer countdown + restore ---
            Instruction.Create(OpCodes.Ldsfld, fNotifTimer),
            Instruction.Create(OpCodes.Ldc_R4, 0f),
            Instruction.Create(OpCodes.Bgt_Un, lblDecrement),
            Instruction.Create(OpCodes.Br, lblContinue),

            lblDecrement,
            Instruction.Create(OpCodes.Ldsfld, fNotifTimer),
            Instruction.Create(OpCodes.Call, getTimeDelta),
            Instruction.Create(OpCodes.Sub),
            Instruction.Create(OpCodes.Stsfld, fNotifTimer),

            Instruction.Create(OpCodes.Ldsfld, fNotifTimer),
            Instruction.Create(OpCodes.Ldc_R4, 0f),
            Instruction.Create(OpCodes.Bgt, lblContinue),
            Instruction.Create(OpCodes.Ldc_R4, 0f),
            Instruction.Create(OpCodes.Stsfld, fNotifTimer),

            // Unity-aware: destroyed objects must compare == null
            Instruction.Create(OpCodes.Ldsfld, fNotifLabel),
            Instruction.Create(OpCodes.Ldnull),
            Instruction.Create(OpCodes.Call, objInequality),
            Instruction.Create(OpCodes.Brfalse, lblContinue),
            Instruction.Create(OpCodes.Ldsfld, fNotifOrig),
            Instruction.Create(OpCodes.Brfalse, lblContinue),

            Instruction.Create(OpCodes.Ldsfld, fNotifLabel),
            Instruction.Create(OpCodes.Ldsfld, fNotifOrig),
            Instruction.Create(OpCodes.Callvirt, setText),
            Instruction.Create(OpCodes.Ldsfld, fNotifLabel),
            Instruction.Create(OpCodes.Callvirt, getGameObject),
            Instruction.Create(OpCodes.Ldsfld, fNotifWasActive),
            Instruction.Create(OpCodes.Callvirt, setAct),
            Instruction.Create(OpCodes.Ldnull),
            Instruction.Create(OpCodes.Stsfld, fNotifOrig),

            lblContinue,

            // --- F10 key check ---
            Instruction.Create(OpCodes.Ldstr, "f10"),
            Instruction.Create(OpCodes.Call, getKey),
            Instruction.Create(OpCodes.Brfalse, orig),
        };

        foreach (var (key, v) in ctx.Profile.LowSpecPreset)
        {
            seq.Add(Instruction.Create(OpCodes.Ldstr, key));
            seq.Add(Instruction.Create(OpCodes.Ldc_I4, v));
            seq.Add(Instruction.Create(OpCodes.Call, setInt));
        }
        seq.Add(Instruction.Create(OpCodes.Call, save));

        // Top notification via the scene-serialized DebugLog UILabel (y=530 = top of screen,
        // parent Panel always active; the GO itself starts inactive, only the debug key
        // sequence ever turns it on, and nothing ever turns it off)
        var lblSameLabel = Instruction.Create(OpCodes.Nop);
        var lblSkipOrig = Instruction.Create(OpCodes.Nop);
        var lblPromptBar = Instruction.Create(OpCodes.Nop);

        // if (F10NotifLabel != this.DebugLog) F10NotifOriginal = null; (scene reload: resave)
        seq.Add(Instruction.Create(OpCodes.Ldsfld, fNotifLabel));
        seq.Add(Instruction.Create(OpCodes.Ldarg_0));
        seq.Add(Instruction.Create(OpCodes.Ldfld, dbgLogField));
        seq.Add(Instruction.Create(OpCodes.Ceq));
        seq.Add(Instruction.Create(OpCodes.Brtrue, lblSameLabel));
        seq.Add(Instruction.Create(OpCodes.Ldnull));
        seq.Add(Instruction.Create(OpCodes.Stsfld, fNotifOrig));
        seq.Add(lblSameLabel);

        seq.Add(Instruction.Create(OpCodes.Ldarg_0));
        seq.Add(Instruction.Create(OpCodes.Ldfld, dbgLogField));
        seq.Add(Instruction.Create(OpCodes.Stsfld, fNotifLabel));

        // brfalse pops its value
        seq.Add(Instruction.Create(OpCodes.Ldsfld, fNotifLabel));
        seq.Add(Instruction.Create(OpCodes.Brfalse, lblPromptBar));
        seq.Add(Instruction.Create(OpCodes.Ldstr, "YsPerf F10: top label (DebugLog) OK"));
        seq.Add(Instruction.Create(OpCodes.Call, debugLog));

        seq.Add(Instruction.Create(OpCodes.Ldsfld, fNotifOrig));
        seq.Add(Instruction.Create(OpCodes.Brtrue, lblSkipOrig));
        seq.Add(Instruction.Create(OpCodes.Ldsfld, fNotifLabel));
        seq.Add(Instruction.Create(OpCodes.Callvirt, getText));
        seq.Add(Instruction.Create(OpCodes.Stsfld, fNotifOrig));
        seq.Add(Instruction.Create(OpCodes.Ldsfld, fNotifLabel));
        seq.Add(Instruction.Create(OpCodes.Callvirt, getGameObject));
        seq.Add(Instruction.Create(OpCodes.Callvirt, getActiveSelf));
        seq.Add(Instruction.Create(OpCodes.Stsfld, fNotifWasActive));
        seq.Add(lblSkipOrig);

        seq.Add(Instruction.Create(OpCodes.Ldsfld, fNotifLabel));
        seq.Add(Instruction.Create(OpCodes.Callvirt, getGameObject));
        seq.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        seq.Add(Instruction.Create(OpCodes.Callvirt, setAct));

        seq.Add(Instruction.Create(OpCodes.Ldsfld, fNotifLabel));
        seq.Add(Instruction.Create(OpCodes.Ldstr, "Low Spec Preset Applied!"));
        seq.Add(Instruction.Create(OpCodes.Callvirt, setText));
        seq.Add(Instruction.Create(OpCodes.Ldc_R4, 4f));
        seq.Add(Instruction.Create(OpCodes.Stsfld, fNotifTimer));
        seq.Add(Instruction.Create(OpCodes.Br, orig));

        // Fallback: bottom PromptBar if the top label is missing (stack is clean here)
        seq.Add(lblPromptBar);
        seq.Add(Instruction.Create(OpCodes.Ldstr, "YsPerf F10: NewTitleScreenScript.DebugLog label is null"));
        seq.Add(Instruction.Create(OpCodes.Call, debugLog));
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
        if (body.MaxStackSize < 8) body.MaxStackSize = 8;

        ctx.ReportApplied("NewTitleScreenScript.Update F10 writes low spec preset to Profile_0_* (global options) + top notification");
    }

    public override void Validate(PatchContext ctx)
    {
        var t = ctx.Inspector.RequireType("NewTitleScreenScript");
        var m = ctx.Inspector.RequireMethod(t, "Update", 0);
        if (!m.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldstr && (i.Operand as string) == "f10"))
            throw new Exception("validate: F10 key marker missing after apply");
        foreach (var f in new[] { "F10NotifTimer", "F10NotifOriginal", "F10NotifLabel", "F10NotifWasActive" })
            if (ctx.Inspector.FindField(t, f) == null)
                throw new Exception($"validate: {f} field missing after apply");
    }
}
