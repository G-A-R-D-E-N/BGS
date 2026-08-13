using System;
using System.IO;
using System.Security.Cryptography;

namespace OpenCommonwealth.Services.Hkx;

internal sealed class DocumentSourceStamp
{
    internal const string ChangedFailure =
        "the file changed on disk after it was opened. Reload it before saving so another editor's changes are not overwritten";

    private readonly byte[] _sha256;

    private DocumentSourceStamp(byte[] sha256) => _sha256 = sha256;

    internal static DocumentSourceStamp Capture(string path)
    {
        using var stream = Open(path);
        return new DocumentSourceStamp(SHA256.HashData(stream));
    }

    internal static DocumentSourceStamp Capture(byte[] bytes) =>
        new(SHA256.HashData(bytes));

    internal bool Matches(byte[] bytes, out string failure) =>
        MatchesHash(SHA256.HashData(bytes), out failure);

    internal bool Matches(string path, out string failure)
    {
        try
        {
            using var stream = Open(path);
            return MatchesHash(SHA256.HashData(stream), out failure);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            failure = ReadFailure(e);
            return false;
        }
    }

    internal static string ReadFailure(Exception error) =>
        "the source file could not be re-read before saving: " + error.Message.Split('\n')[0];

    private bool MatchesHash(byte[] current, out string failure)
    {
        if (CryptographicOperations.FixedTimeEquals(_sha256, current))
        {
            failure = "";
            return true;
        }

        failure = ChangedFailure;
        return false;
    }

    private static FileStream Open(string path)
    {
        var stream = File.Open(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        try
        {
            InputFilePolicy.EnsureHkx(stream.Length, Path.GetFileName(path));
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
