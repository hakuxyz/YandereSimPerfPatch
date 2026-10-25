using System.Security.Cryptography;
using System.Text.Json;

namespace YsPerfPatcher;

public sealed record BackupBundle(
    string DllPath,
    string OrigPath,
    string Md5Original,
    string? Md5Patched,
    string TimestampUtc,
    string PatcherVersion,
    IReadOnlyList<string> AppliedGroups);

public static class BackupManager
{
    public static string OrigPathFor(string dllPath) => dllPath + ".orig";
    public static string ManifestPathFor(string dllPath) => dllPath + ".backup.json";

    public static string Md5Of(byte[] bytes)
        => Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();

    public static BackupBundle Create(string dllPath, byte[] originalBytes,
        IReadOnlyList<string> appliedGroups, string patcherVersion)
    {
        string orig = OrigPathFor(dllPath);
        if (!File.Exists(orig))
            File.WriteAllBytes(orig, originalBytes);

        var bundle = new BackupBundle(
            DllPath: dllPath,
            OrigPath: orig,
            Md5Original: Md5Of(originalBytes),
            Md5Patched: Md5Of(File.ReadAllBytes(dllPath)),
            TimestampUtc: DateTime.UtcNow.ToString("o"),
            PatcherVersion: patcherVersion,
            AppliedGroups: appliedGroups.ToList());

        File.WriteAllText(ManifestPathFor(dllPath),
            JsonSerializer.Serialize(bundle, new JsonSerializerOptions { WriteIndented = true }));
        return bundle;
    }

    public static BackupBundle? TryReadManifest(string dllPath)
    {
        string manifest = ManifestPathFor(dllPath);
        if (!File.Exists(manifest)) return null;
        try
        {
            return JsonSerializer.Deserialize<BackupBundle>(File.ReadAllText(manifest));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Catches well-formed JSON that does not actually describe a backup:
    /// wrong property names, hand-edited fields, partial writes. Returns null
    /// when the manifest is complete enough to trust for validation.
    /// </summary>
    public static string? ValidateStructure(BackupBundle manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Md5Original)
            || manifest.Md5Original.Length != 32
            || !manifest.Md5Original.All(Uri.IsHexDigit))
            return "Md5Original missing or not a 32-char md5";
        if (string.IsNullOrWhiteSpace(manifest.PatcherVersion))
            return "PatcherVersion missing";
        if (string.IsNullOrWhiteSpace(manifest.TimestampUtc))
            return "TimestampUtc missing";
        if (string.IsNullOrWhiteSpace(manifest.OrigPath))
            return "OrigPath missing";
        if (manifest.AppliedGroups == null)
            return "AppliedGroups missing";
        return null;
    }

    public static bool Restore(string dllPath, out string message)
    {
        string orig = OrigPathFor(dllPath);
        if (!File.Exists(orig))
        {
            message = $"No backup found: {orig}";
            return false;
        }

        // a manifest that exists but cannot be read fails closed:
        // never restore from an unverified backup when metadata is present but broken.
        // installs predating the manifest (no .backup.json file at all) still restore.
        BackupBundle? manifest = null;
        string manifestPath = ManifestPathFor(dllPath);
        if (File.Exists(manifestPath))
        {
            try
            {
                manifest = JsonSerializer.Deserialize<BackupBundle>(File.ReadAllText(manifestPath));
            }
            catch (Exception ex)
            {
                message = $"Manifest invalid ({ex.GetType().Name}): {manifestPath}";
                return false;
            }
            if (manifest == null)
            {
                message = $"Manifest invalid (empty): {manifestPath}";
                return false;
            }
            var structureError = ValidateStructure(manifest);
            if (structureError != null)
            {
                message = $"Manifest invalid ({structureError}): {manifestPath}";
                return false;
            }
            if (!string.Equals(Path.GetFullPath(manifest.OrigPath), Path.GetFullPath(orig),
                    StringComparison.Ordinal))
            {
                message = $"Manifest invalid (OrigPath {manifest.OrigPath} does not match {orig}): {manifestPath}";
                return false;
            }
        }

        string origMd5 = Md5Of(File.ReadAllBytes(orig));
        if (manifest != null && manifest.Md5Original != origMd5)
        {
            message = $"Backup validation failed: {orig} md5 {origMd5} does not match manifest {manifest.Md5Original}";
            return false;
        }

        File.Copy(orig, dllPath, overwrite: true);
        message = $"Restored {dllPath} from {orig} (md5 {origMd5})";
        return true;
    }

    public static string DescribeStatus(string dllPath)
    {
        string current = File.Exists(dllPath) ? Md5Of(File.ReadAllBytes(dllPath)) : "?";
        string manifestPath = ManifestPathFor(dllPath);
        BackupBundle? manifest = null;
        bool manifestBroken = false;
        if (File.Exists(manifestPath))
        {
            try
            {
                manifest = JsonSerializer.Deserialize<BackupBundle>(File.ReadAllText(manifestPath));
                manifestBroken = manifest == null;
            }
            catch
            {
                manifestBroken = true;
            }
        }
        if (manifestBroken)
            return $"Patch manifest present but unreadable. Current md5: {current}";
        if (manifest != null && ValidateStructure(manifest) != null)
            return $"Patch manifest present but incomplete. Current md5: {current}";
        if (manifest == null)
            return $"No patch manifest. Current md5: {current}";
        return $"Patched by YsPerfPatcher {manifest.PatcherVersion} at {manifest.TimestampUtc} "
             + $"({manifest.AppliedGroups.Count} groups). Original md5: {manifest.Md5Original}, current md5: {current}";
    }
}
