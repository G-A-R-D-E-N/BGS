using System;
using System.IO;

namespace OpenCommonwealth.Services.Hkx;

public static class FileSafety
{
    // Invariant: .bak is the immediately previous version, .bak.1 one version
    // older, .bak.2 two versions older; with keep = 3 there is never a .bak.3.
    public static void Backup(string path, int keep = 3)
    {
        if (!File.Exists(path)) return;
        if (keep <= 1)
        {
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            File.Copy(path, path + ".bak");
            return;
        }

        // Drop the oldest first: with keep=3 the retained set is {.bak, .bak.1, .bak.2}.
        // Shifting .bak.2 to .bak.3 (as a naive loop does) retains keep+1 backups.
        string oldest = path + ".bak." + (keep - 1);
        if (File.Exists(oldest)) File.Delete(oldest);
        for (int i = keep - 1; i >= 2; i--)
        {
            string older = path + ".bak." + (i - 1);
            if (File.Exists(older)) File.Move(older, path + ".bak." + i, overwrite: true);
        }
        if (File.Exists(path + ".bak")) File.Move(path + ".bak", path + ".bak.1", overwrite: true);
        File.Copy(path, path + ".bak");
    }

    // Stage under a unique name in the same directory so two saves cannot
    // collide, then move the staged file over the target. On POSIX the final
    // rename is atomic; on Windows, MoveFileEx(REPLACE_EXISTING) is not
    // guaranteed crash-atomic, so callers should not promise more than
    // "staged and moved into place". The staging file is removed on failure.
    public static void Replace(string path, byte[] bytes)
    {
        string staging = path + "." + Guid.NewGuid().ToString("N") + ".writing";
        try
        {
            File.WriteAllBytes(staging, bytes);
            File.Move(staging, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(staging)) File.Delete(staging); } catch (IOException) { }
            throw;
        }
    }
}
