namespace YsPerfPatcher;

public sealed class PatchContext
{
    public AssemblyInspector Inspector { get; }
    public GameBuildProfile Profile { get; }

    public List<string> Applied { get; } = new();
    public List<string> Skipped { get; } = new();

    public PatchContext(AssemblyInspector inspector, GameBuildProfile profile)
    {
        Inspector = inspector;
        Profile = profile;
    }

    public void ReportApplied(string message) => Applied.Add(message);
    public void ReportSkipped(string message) => Skipped.Add(message);
}

public abstract class PatchGroup
{
    public abstract string Name { get; }
    public abstract IReadOnlyList<string> TargetTypes { get; }
    public abstract IReadOnlyList<string> RequiredMembers { get; }

    /// <summary>Safety check: returns false when the group is already applied or the layout does not match.</summary>
    public virtual bool CanApply(PatchContext ctx, out string skipReason)
    {
        skipReason = "";
        return true;
    }

    public abstract void Apply(PatchContext ctx);

    /// <summary>Post-apply sanity check; throws when the IL change did not stick as expected.</summary>
    public virtual void Validate(PatchContext ctx) { }
}
