using Mono.Cecil;
using Mono.Cecil.Cil;

namespace YsPerfPatcher.Groups;

public sealed class LowSpecKeyGroup : PatchGroup
{
    public override string Name => "F10 low-spec key";
    public override IReadOnlyList<string> TargetTypes => new[] { "NewTitleScreenScript" };
    public override IReadOnlyList<string> RequiredMembers => new[] { "Update", "DebugLog", "PromptBar" };

    sealed record Refs(
        MethodReference GetKey,
        MethodReference SetInt,
        MethodReference Save,
        MethodReference SetText,
        MethodReference GetText,
        MethodReference GetTimeDelta,
        MethodReference DebugLog,
        MethodReference GetGameObject,
        MethodReference GetActiveSelf,
        MethodReference SetActive,
        MethodReference ObjectInequality,
        FieldDefinition DbgLogField,
        FieldDefinition PbField,
        MethodDefinition PbClear,
        MethodDefinition PbUpdate,
        FieldDefinition PbLabel,
        FieldDefinition PbShow);

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

        var refs = ResolveRefs(inspector, t, m);
        var notif = AddNotificationStateFields(t, refs.SetText);
        var seq = BuildSequence(refs, notif, ctx.Profile.LowSpecPreset, m.Body.Instructions[0]);

        var body = m.Body;
        var il = body.GetILProcessor();
        var orig = body.Instructions[0];
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

    // ---------- internals ----------

    static Refs ResolveRefs(AssemblyInspector inspector, TypeDefinition t, MethodDefinition m)
    {
        // steal member refs from this method (or module), the game already calls all of them
        MethodReference? getKey = null, setInt = null;
        foreach (var i in m.Body.Instructions)
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

        return new Refs(getKey, setInt, save, setText, getText, getTimeDelta, debugLog,
            getGameObject, getActiveSelf, setAct, objInequality,
            dbgLogField, pbField, mClear, mUpdate, fLabel, fShow);
    }

    static (FieldDefinition Timer, FieldDefinition Original, FieldDefinition Label, FieldDefinition WasActive)
        AddNotificationStateFields(TypeDefinition t, MethodReference setText)
    {
        var module = t.Module;

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
        return (fNotifTimer, fNotifOrig, fNotifLabel, fNotifWasActive);
    }

    static List<Instruction> BuildSequence(Refs r,
        (FieldDefinition Timer, FieldDefinition Original, FieldDefinition Label, FieldDefinition WasActive) n,
        IReadOnlyList<PresetKey> preset, Instruction orig)
    {
        // branch targets for the every-frame timer countdown
        var lblDecrement = Instruction.Create(OpCodes.Nop);
        var lblContinue = Instruction.Create(OpCodes.Nop);

        var seq = new List<Instruction>
        {
            // --- every frame: notification timer countdown + restore ---
            Instruction.Create(OpCodes.Ldsfld, n.Timer),
            Instruction.Create(OpCodes.Ldc_R4, 0f),
            Instruction.Create(OpCodes.Bgt_Un, lblDecrement),
            Instruction.Create(OpCodes.Br, lblContinue),

            lblDecrement,
            Instruction.Create(OpCodes.Ldsfld, n.Timer),
            Instruction.Create(OpCodes.Call, r.GetTimeDelta),
            Instruction.Create(OpCodes.Sub),
            Instruction.Create(OpCodes.Stsfld, n.Timer),

            Instruction.Create(OpCodes.Ldsfld, n.Timer),
            Instruction.Create(OpCodes.Ldc_R4, 0f),
            Instruction.Create(OpCodes.Bgt, lblContinue),
            Instruction.Create(OpCodes.Ldc_R4, 0f),
            Instruction.Create(OpCodes.Stsfld, n.Timer),

            // Unity-aware: destroyed objects must compare == null
            Instruction.Create(OpCodes.Ldsfld, n.Label),
            Instruction.Create(OpCodes.Ldnull),
            Instruction.Create(OpCodes.Call, r.ObjectInequality),
            Instruction.Create(OpCodes.Brfalse, lblContinue),
            Instruction.Create(OpCodes.Ldsfld, n.Original),
            Instruction.Create(OpCodes.Brfalse, lblContinue),

            Instruction.Create(OpCodes.Ldsfld, n.Label),
            Instruction.Create(OpCodes.Ldsfld, n.Original),
            Instruction.Create(OpCodes.Callvirt, r.SetText),
            Instruction.Create(OpCodes.Ldsfld, n.Label),
            Instruction.Create(OpCodes.Callvirt, r.GetGameObject),
            Instruction.Create(OpCodes.Ldsfld, n.WasActive),
            Instruction.Create(OpCodes.Callvirt, r.SetActive),
            Instruction.Create(OpCodes.Ldnull),
            Instruction.Create(OpCodes.Stsfld, n.Original),

            lblContinue,

            // --- F10 key check ---
            Instruction.Create(OpCodes.Ldstr, "f10"),
            Instruction.Create(OpCodes.Call, r.GetKey),
            Instruction.Create(OpCodes.Brfalse, orig),
        };

        foreach (var (key, v) in preset)
        {
            seq.Add(Instruction.Create(OpCodes.Ldstr, key));
            seq.Add(Instruction.Create(OpCodes.Ldc_I4, v));
            seq.Add(Instruction.Create(OpCodes.Call, r.SetInt));
        }
        seq.Add(Instruction.Create(OpCodes.Call, r.Save));

        // Top notification via the scene-serialized DebugLog UILabel (y=530 = top of screen,
        // parent Panel always active; the GO itself starts inactive, only the debug key
        // sequence ever turns it on, and nothing ever turns it off)
        var lblSameLabel = Instruction.Create(OpCodes.Nop);
        var lblSkipOrig = Instruction.Create(OpCodes.Nop);
        var lblPromptBar = Instruction.Create(OpCodes.Nop);

        // if (F10NotifLabel != this.DebugLog) F10NotifOriginal = null; (scene reload: resave)
        seq.Add(Instruction.Create(OpCodes.Ldsfld, n.Label));
        seq.Add(Instruction.Create(OpCodes.Ldarg_0));
        seq.Add(Instruction.Create(OpCodes.Ldfld, r.DbgLogField));
        seq.Add(Instruction.Create(OpCodes.Ceq));
        seq.Add(Instruction.Create(OpCodes.Brtrue, lblSameLabel));
        seq.Add(Instruction.Create(OpCodes.Ldnull));
        seq.Add(Instruction.Create(OpCodes.Stsfld, n.Original));
        seq.Add(lblSameLabel);

        seq.Add(Instruction.Create(OpCodes.Ldarg_0));
        seq.Add(Instruction.Create(OpCodes.Ldfld, r.DbgLogField));
        seq.Add(Instruction.Create(OpCodes.Stsfld, n.Label));

        // brfalse pops its value
        seq.Add(Instruction.Create(OpCodes.Ldsfld, n.Label));
        seq.Add(Instruction.Create(OpCodes.Brfalse, lblPromptBar));
        seq.Add(Instruction.Create(OpCodes.Ldstr, "YsPerf F10: top label (DebugLog) OK"));
        seq.Add(Instruction.Create(OpCodes.Call, r.DebugLog));

        seq.Add(Instruction.Create(OpCodes.Ldsfld, n.Original));
        seq.Add(Instruction.Create(OpCodes.Brtrue, lblSkipOrig));
        seq.Add(Instruction.Create(OpCodes.Ldsfld, n.Label));
        seq.Add(Instruction.Create(OpCodes.Callvirt, r.GetText));
        seq.Add(Instruction.Create(OpCodes.Stsfld, n.Original));
        seq.Add(Instruction.Create(OpCodes.Ldsfld, n.Label));
        seq.Add(Instruction.Create(OpCodes.Callvirt, r.GetGameObject));
        seq.Add(Instruction.Create(OpCodes.Callvirt, r.GetActiveSelf));
        seq.Add(Instruction.Create(OpCodes.Stsfld, n.WasActive));
        seq.Add(lblSkipOrig);

        seq.Add(Instruction.Create(OpCodes.Ldsfld, n.Label));
        seq.Add(Instruction.Create(OpCodes.Callvirt, r.GetGameObject));
        seq.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        seq.Add(Instruction.Create(OpCodes.Callvirt, r.SetActive));

        seq.Add(Instruction.Create(OpCodes.Ldsfld, n.Label));
        seq.Add(Instruction.Create(OpCodes.Ldstr, "Low Spec Preset Applied!"));
        seq.Add(Instruction.Create(OpCodes.Callvirt, r.SetText));
        seq.Add(Instruction.Create(OpCodes.Ldc_R4, 4f));
        seq.Add(Instruction.Create(OpCodes.Stsfld, n.Timer));
        seq.Add(Instruction.Create(OpCodes.Br, orig));

        // Fallback: bottom PromptBar if the top label is missing (stack is clean here)
        seq.Add(lblPromptBar);
        seq.Add(Instruction.Create(OpCodes.Ldstr, "YsPerf F10: NewTitleScreenScript.DebugLog label is null"));
        seq.Add(Instruction.Create(OpCodes.Call, r.DebugLog));
        seq.Add(Instruction.Create(OpCodes.Ldarg_0));
        seq.Add(Instruction.Create(OpCodes.Ldfld, r.PbField));
        seq.Add(Instruction.Create(OpCodes.Brfalse, orig));
        seq.Add(Instruction.Create(OpCodes.Ldarg_0));
        seq.Add(Instruction.Create(OpCodes.Ldfld, r.PbField));
        seq.Add(Instruction.Create(OpCodes.Callvirt, r.PbClear));
        seq.Add(Instruction.Create(OpCodes.Ldarg_0));
        seq.Add(Instruction.Create(OpCodes.Ldfld, r.PbField));
        seq.Add(Instruction.Create(OpCodes.Ldfld, r.PbLabel));
        seq.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        seq.Add(Instruction.Create(OpCodes.Ldelem_Ref));
        seq.Add(Instruction.Create(OpCodes.Ldstr, "Low Spec Preset Applied"));
        seq.Add(Instruction.Create(OpCodes.Callvirt, r.SetText));
        seq.Add(Instruction.Create(OpCodes.Ldarg_0));
        seq.Add(Instruction.Create(OpCodes.Ldfld, r.PbField));
        seq.Add(Instruction.Create(OpCodes.Callvirt, r.PbUpdate));
        seq.Add(Instruction.Create(OpCodes.Ldarg_0));
        seq.Add(Instruction.Create(OpCodes.Ldfld, r.PbField));
        seq.Add(Instruction.Create(OpCodes.Ldc_I4_1));
        seq.Add(Instruction.Create(OpCodes.Stfld, r.PbShow));

        return seq;
    }
}
