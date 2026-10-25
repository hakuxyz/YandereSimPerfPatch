using Mono.Cecil;
using Mono.Cecil.Cil;

namespace YsPerfPatcher;

public sealed class AssemblyInspector
{
    public AssemblyDefinition GameAssembly { get; }
    public ModuleDefinition Module => GameAssembly.MainModule;
    public string AssemblyPath { get; }
    public byte[] OriginalBytes { get; }

    readonly string _dir;
    readonly AssemblyDefinition _core;

    public AssemblyInspector(string assemblyPath)
    {
        AssemblyPath = assemblyPath;
        _dir = Path.GetDirectoryName(assemblyPath)!;

        string corePath = Path.Combine(_dir, "UnityEngine.CoreModule.dll");
        if (!File.Exists(corePath))
            throw new Exception($"UnityEngine.CoreModule.dll not found in {_dir}");
        _core = AssemblyDefinition.ReadAssembly(corePath);

        OriginalBytes = File.ReadAllBytes(assemblyPath);

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(_dir);
        GameAssembly = AssemblyDefinition.ReadAssembly(
            new MemoryStream(OriginalBytes),
            new ReaderParameters { AssemblyResolver = resolver, ReadingMode = ReadingMode.Immediate });
    }

    public IReadOnlyList<TypeDefinition> AllTypes => Module.GetTypes().ToList();

    // ---------- type / member discovery ----------

    public TypeDefinition? FindType(string name) => Module.GetType(name);

    public TypeDefinition RequireType(string name)
        => FindType(name) ?? throw new Exception($"type {name} not found");

    public MethodDefinition? FindMethod(TypeDefinition type, string name, int paramCount = -1)
        => type.Methods.FirstOrDefault(m => m.Name == name
            && (paramCount < 0 || m.Parameters.Count == paramCount));

    public MethodDefinition RequireMethod(TypeDefinition type, string name, int paramCount = -1)
        => FindMethod(type, name, paramCount)
            ?? throw new Exception($"{type.Name}.{name} not found");

    public FieldDefinition? FindField(TypeDefinition type, string name)
        => type.Fields.FirstOrDefault(f => f.Name == name);

    public FieldDefinition RequireField(TypeDefinition type, string name)
        => FindField(type, name)
            ?? throw new Exception($"{type.Name}.{name} not found");

    public FieldDefinition RequireField(TypeDefinition type, string name, string fieldTypeName)
        => type.Fields.FirstOrDefault(f => f.Name == name && f.FieldType.Name == fieldTypeName)
            ?? throw new Exception($"{type.Name}.{name} ({fieldTypeName}) not found");

    // ---------- UnityEngine.CoreModule imports ----------

    public MethodReference ImportUnityMethod(string typeName, string methodName, int paramCount = -1)
    {
        var t = _core.MainModule.GetType("UnityEngine." + typeName)
            ?? throw new Exception($"UnityEngine.{typeName} not found");
        var m = t.Methods.FirstOrDefault(x => x.Name == methodName
                && (paramCount < 0 || x.Parameters.Count == paramCount))
            ?? throw new Exception($"UnityEngine.{typeName}.{methodName} not found");
        return Module.ImportReference(m);
    }

    // ---------- IL helpers ----------

    public bool HasMarker(MethodDefinition m, string callName)
    {
        if (!m.HasBody) return false;
        return m.Body.Instructions.Any(i =>
            (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt)
            && i.Operand is MethodReference mr && mr.Name == callName);
    }

    /// <summary>
    /// Finds an existing call/callvirt reference anywhere in the game module.
    /// Hand-built MemberRefs for BCL types break Unity's mono; always reuse
    /// references the game itself already uses.
    /// </summary>
    public MethodReference? FindExistingCall(Func<MethodReference, bool> predicate)
    {
        foreach (var type in Module.GetTypes())
        foreach (var method in type.Methods)
        {
            if (!method.HasBody) continue;
            foreach (var instr in method.Body.Instructions)
            {
                if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt) continue;
                if (instr.Operand is MethodReference mr && predicate(mr)) return mr;
            }
        }
        return null;
    }

    public MethodReference RequireExistingCall(Func<MethodReference, bool> predicate, string description)
        => FindExistingCall(predicate)
            ?? throw new Exception($"existing {description} member ref not found in game assembly");
}
