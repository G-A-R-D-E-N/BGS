using System;
using System.Threading.Tasks;

namespace OpenCommonwealth.Services.Hkx;

internal sealed class BehaviourCompareSession
{
    internal sealed record Outcome(bool Stale, BehaviourDiff.Result? Value, string Error)
    {
        internal bool Failed => Error.Length > 0;
    }

    private readonly Func<long> _currentRevision;

    internal BehaviourCompareSession(Func<long> currentRevision) =>
        _currentRevision = currentRevision;

    internal Func<string, string>? ReadComparableForTest { get; set; }

    internal async Task<Outcome> Compare(string mine, string otherPath, long revision)
    {
        BehaviourDiff.Result result;
        try
        {
            result = await Task.Run(() => CompareNow(mine, otherPath, ReadComparableForTest));
        }
        catch (Exception error)
        {
            return revision != _currentRevision()
                ? new Outcome(true, null, "")
                : new Outcome(false, null, error.Message.Split('\n')[0]);
        }

        return revision != _currentRevision()
            ? new Outcome(true, null, "")
            : new Outcome(false, result, "");
    }

    internal static BehaviourDiff.Result CompareNow(
        string mine,
        string otherPath,
        Func<string, string>? readComparable = null)
    {
        string theirs = (readComparable ?? ReadComparable)(otherPath);
        if (theirs.Length == 0)
            throw new InvalidOperationException(
                "this file's classes are not ones this build describes");

        return CompareText(mine, theirs);
    }

    internal static BehaviourDiff.Result CompareText(string mine, string theirs) =>
        BehaviourDiff.Compare(RepackCheck.Take(mine), RepackCheck.Take(theirs));

    private static string ReadComparable(string path)
    {
        try
        {
            var bytes = InputFilePolicy.ReadHkx(path);
            var objects = new PackfileObjects(PackfileImage.Read(bytes));

            if (HavokClassTypes.Shipped.SignatureProblems(objects.ClassNames()).Count == 0)
                return NativeXml.From(bytes);
        }
        catch (Exception)
        {
        }

        return "";
    }
}
