using System;
using System.IO;

namespace OpenCommonwealth.Services.Hkx;

internal static class GraphMutationTransaction
{
    internal enum Outcome
    {
        Refused,
        Committed,
    }

    internal sealed record Mutation(
        byte[] Bytes,
        int RootId,
        int AddedObjects,
        string RootClass,
        string Summary);

    internal sealed record Result(Outcome State, Mutation? Change, string Message)
    {
        internal bool Committed => State == Outcome.Committed;
    }

    internal static Result Commit(
        string path,
        DocumentSourceStamp? openedStamp,
        Func<byte[], Mutation> build,
        Func<Exception>? verificationFault = null,
        Action? beforeSourceRecheck = null)
    {
        FileSafety.RecoverInterrupted(path);

        byte[] source;
        try
        {
            source = InputFilePolicy.ReadHkx(path);
        }
        catch (Exception error)
        {
            return Refused(DocumentSourceStamp.ReadFailure(error));
        }

        if (openedStamp != null && !openedStamp.Matches(source, out string externalChange))
            return Refused(externalChange);

        string? blocked = HkxTextEdit.WhyNotWritable(path);
        if (blocked != null) return Refused(blocked);

        var transactionStamp = DocumentSourceStamp.Capture(source);

        Mutation change;
        try
        {
            change = build(source);
        }
        catch (Exception error)
        {
            return Refused(error.Message);
        }

        try
        {
            Verify(source, change);
            if (verificationFault != null) throw verificationFault();
        }
        catch (Exception error)
        {
            return Refused("the rebuilt file failed verification: " + error.Message);
        }

        beforeSourceRecheck?.Invoke();
        if (!transactionStamp.Matches(path, out externalChange))
            return Refused(externalChange);

        try
        {
            FileSafety.ReplaceChecked(path, change.Bytes, transactionStamp);
        }
        catch (Exception error)
        {
            return Refused("the file could not be written: " + error.Message);
        }

        string message = change.Summary +
                         $" The file before this is kept as {Path.GetFileName(path + ".bak")}.";
        return new Result(Outcome.Committed, change, message);
    }

    private static Result Refused(string message) =>
        new(Outcome.Refused, null, message);

    private static void Verify(byte[] source, Mutation change)
    {
        if (change.Bytes == null || change.Bytes.Length == 0)
            throw new InvalidDataException("the mutation produced no HKX bytes");
        if (change.AddedObjects <= 0)
            throw new InvalidDataException("the mutation did not declare any added objects");

        var before = new PackfileObjects(PackfileImage.Read(source), HavokClasses.Shipped);
        var after = new PackfileObjects(PackfileImage.Read(change.Bytes), HavokClasses.Shipped);

        var signatureProblems = HavokClassTypes.Shipped.SignatureProblems(after.ClassNames());
        if (signatureProblems.Count > 0)
            throw new InvalidDataException("unsupported class signature: " + signatureProblems[0]);

        int expected = checked(before.Instances.Count + change.AddedObjects);
        if (after.Instances.Count != expected)
            throw new InvalidDataException(
                $"expected {expected} objects after adding {change.AddedObjects}, but found {after.Instances.Count}");

        int root = change.RootId - NativeGraphModel.FirstId;
        if (root < 0 || root >= after.Instances.Count)
            throw new InvalidDataException($"the reported root #{change.RootId} is not in the rebuilt file");

        string actualRootClass = after.Instances[root].ClassName;
        if (change.RootClass.Length > 0 && actualRootClass != change.RootClass)
            throw new InvalidDataException(
                $"the reported root #{change.RootId} is {actualRootClass}, not {change.RootClass}");

        if (NativeGraphModel.From(after) == null)
            throw new InvalidDataException("the rebuilt file could not be modeled");
    }
}
