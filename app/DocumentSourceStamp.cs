using System;
using System.IO;
using System.Security.Cryptography;

namespace BehaviourStudio.App;

internal sealed class DocumentSourceStamp
{
    private readonly byte[] _sha256;

    private DocumentSourceStamp(byte[] sha256) => _sha256 = sha256;

    public static DocumentSourceStamp Capture(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return new DocumentSourceStamp(SHA256.HashData(stream));
    }

    public bool Matches(string path, out string failure)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            byte[] current = SHA256.HashData(stream);
            if (CryptographicOperations.FixedTimeEquals(_sha256, current))
            {
                failure = "";
                return true;
            }

            failure = "the file changed on disk after it was opened. Reload it before saving so another editor's changes are not overwritten";
            return false;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            failure = "the source file could not be re-read before saving: " + e.Message.Split('\n')[0];
            return false;
        }
    }
}
