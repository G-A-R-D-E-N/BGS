using System;
using System.IO;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public static class FileSafety
{
    private static readonly TimeSpan InterruptedArtifactAge = TimeSpan.FromMinutes(10);

    // Invariant: .bak is the immediately previous version, .bak.1 one version
    // older, .bak.2 two versions older; with keep = 3 there is never a .bak.3.
    public static void Backup(string path, int keep = 3)
    {
        if (!File.Exists(path)) return;
        RotateBackups(path, keep);
        File.Copy(path, path + ".bak");
    }

    // Stage under a unique name in the same directory so two saves cannot
    // collide, then move the staged file over the target.
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
            TryDelete(staging);
            throw;
        }
    }

    internal static void ReplaceChecked(
        string path,
        byte[] bytes,
        DocumentSourceStamp expected,
        int keep = 3,
        Action? beforeReplace = null,
        Action? afterReplace = null)
    {
        RecoverInterrupted(path, keep);

        string id = Guid.NewGuid().ToString("N");
        string staging = path + "." + id + ".writing";
        string saving = path + "." + id + ".saving";
        var replacement = DocumentSourceStamp.Capture(bytes);
        string previous = path + "." + replacement.Token + "." + id + ".previous";

        try
        {
            File.WriteAllBytes(staging, bytes);
            if (!expected.Matches(path, out string changed))
                throw new IOException(changed);

            beforeReplace?.Invoke();
            File.WriteAllBytes(saving, Array.Empty<byte>());
            File.Replace(staging, path, previous);
            afterReplace?.Invoke();
            TryTouch(previous);
            TryTouch(saving);

            bool previousWasExpected = expected.Matches(previous, out _);
            bool currentIsReplacement = replacement.Matches(path, out _);

            if (!previousWasExpected)
            {
                if (currentIsReplacement)
                {
                    try
                    {
                        File.Replace(previous, path, null);
                    }
                    catch (Exception restoreError)
                        when (restoreError is IOException or UnauthorizedAccessException)
                    {
                        PromotePrevious(path, previous, keep);
                        throw new IOException(
                            DocumentSourceStamp.ChangedFailure +
                            ". Automatic restoration failed; the displaced version was kept as a backup: " +
                            restoreError.Message,
                            restoreError);
                    }
                }
                else
                {
                    PromotePrevious(path, previous, keep);
                }

                throw new IOException(DocumentSourceStamp.ChangedFailure);
            }

            if (!currentIsReplacement)
            {
                PromotePrevious(path, previous, keep);
                throw new IOException(
                    "the file changed on disk while the save was being committed; the pre-save version " +
                    "was kept as a backup");
            }

            // The replacement is in place and verified by this point, so a backup that
            // could not be rotated must not report the save as failed. The sidecar is
            // left where it is and the next save recovers it.
            TryPromotePrevious(path, previous, keep);
        }
        catch
        {
            TryDelete(staging);
            throw;
        }
        finally
        {
            TryDelete(saving);
        }
    }

    internal static void RecoverInterrupted(string path, int keep = 3)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return;
        }

        string? directory = Path.GetDirectoryName(full);
        if (directory == null || !Directory.Exists(directory)) return;

        string name = Path.GetFileName(full);
        DateTime cutoff = DateTime.UtcNow - InterruptedArtifactAge;

        foreach (string writing in Artifacts(directory, name + ".*.writing"))
            if (IsStale(writing, cutoff)) TryDelete(writing);

        foreach (string saving in Artifacts(directory, name + ".*.saving"))
            if (IsStale(saving, cutoff)) TryDelete(saving);

        var previous = Artifacts(directory, name + ".*.previous")
            .Select(file => (File: file, Written: LastWriteUtc(file)))
            .Where(item => item.Written is DateTime written && written <= cutoff)
            .Where(item => !HasFreshSavingMarker(full, item.File, cutoff))
            .OrderByDescending(item => item.Written)
            .Select(item => item.File)
            .ToList();
        if (previous.Count == 0) return;

        if (!File.Exists(full))
        {
            string? restored = previous.FirstOrDefault(file => TryMove(file, full));
            if (restored == null) return;

            foreach (string stale in previous)
                if (!string.Equals(stale, restored, StringComparison.Ordinal)) TryDelete(stale);
            return;
        }

        string currentToken;
        try { currentToken = DocumentSourceStamp.Capture(full).Token; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return;
        }

        string marker = "." + currentToken + ".";
        string? newestMatching = previous.FirstOrDefault(file =>
            Path.GetFileName(file).Contains(marker, StringComparison.OrdinalIgnoreCase));

        if (newestMatching != null && !TryPromoteRecovered(full, newestMatching, keep)) return;

        foreach (string stale in previous)
        {
            if (string.Equals(stale, newestMatching, StringComparison.Ordinal)) continue;
            TryDelete(stale);
        }
    }

    private static bool HasFreshSavingMarker(string path, string previous, DateTime cutoff)
    {
        string fileName = Path.GetFileName(previous);
        const string suffix = ".previous";
        if (!fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;

        string stem = fileName[..^suffix.Length];
        int dot = stem.LastIndexOf('.');
        if (dot < 0 || dot == stem.Length - 1) return false;

        string id = stem[(dot + 1)..];
        string saving = path + "." + id + ".saving";
        DateTime? written = LastWriteUtc(saving);
        return written is DateTime at && at > cutoff;
    }

    private static bool TryPromoteRecovered(string path, string previous, int keep)
    {
        try
        {
            RotateBackups(path, keep);
            File.Move(previous, path + ".bak");
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string[] Artifacts(string directory, string pattern)
    {
        try { return Directory.GetFiles(directory, pattern); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static DateTime? LastWriteUtc(string path)
    {
        try { return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsStale(string path, DateTime cutoff) =>
        LastWriteUtc(path) is DateTime written && written <= cutoff;

    private static bool TryMove(string source, string destination)
    {
        try
        {
            File.Move(source, destination);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static void TryTouch(string path)
    {
        try { if (File.Exists(path)) File.SetLastWriteTimeUtc(path, DateTime.UtcNow); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static void TryPromotePrevious(string path, string previous, int keep)
    {
        try { PromotePrevious(path, previous, keep); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static void PromotePrevious(string path, string previous, int keep)
    {
        if (!File.Exists(previous)) return;
        RotateBackups(path, keep);
        File.Move(previous, path + ".bak");
    }

    private static void RotateBackups(string path, int keep)
    {
        if (keep <= 1)
        {
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            return;
        }

        string oldest = path + ".bak." + (keep - 1);
        if (File.Exists(oldest)) File.Delete(oldest);
        for (int i = keep - 1; i >= 2; i--)
        {
            string older = path + ".bak." + (i - 1);
            if (File.Exists(older)) File.Move(older, path + ".bak." + i, overwrite: true);
        }
        if (File.Exists(path + ".bak")) File.Move(path + ".bak", path + ".bak.1", overwrite: true);
    }
}
