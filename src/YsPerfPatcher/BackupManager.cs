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

    public static bool Restore(string dllPath, out string message)
    {
        string orig = OrigPathFor(dllPath);
        if (!File.Exists(orig))
        {
            message = $"No backup found: {orig}";
            return false;
        }

        var manifest = TryReadManifest(dllPath);
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
        var manifest = TryReadManifest(dllPath);
        string current = File.Exists(dllPath) ? Md5Of(File.ReadAllBytes(dllPath)) : "?";
        if (manifest == null)
            return $"No patch manifest. Current md5: {current}";
        return $"Patched by YsPerfPatcher {manifest.PatcherVersion} at {manifest.TimestampUtc} "
             + $"({manifest.AppliedGroups.Count} groups). Original md5: {manifest.Md5Original}, current md5: {current}";
    }
}
