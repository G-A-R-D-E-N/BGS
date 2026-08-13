using System;
using System.IO;

namespace OpenCommonwealth.Services.Hkx;

internal static class DocumentSaveTransaction
{
    internal enum Outcome
    {
        Refused,
        Unchanged,
        Committed,
    }

    internal sealed record Result(Outcome State, NativeSave.Plan? Plan, string Message)
    {
        internal bool Committed => State == Outcome.Committed;
        internal bool Unchanged => State == Outcome.Unchanged;
    }

    internal static Result Commit(
        string path,
        string savedXml,
        string currentXml,
        DocumentSourceStamp? openedStamp,
        Func<Exception>? verificationFault = null,
        Action? beforeSourceRecheck = null)
    {
        byte[] source;
        try
        {
            source = InputFilePolicy.ReadHkx(path);
        }
        catch (Exception error)
        {
            return Refused("Not saved: " + DocumentSourceStamp.ReadFailure(error));
        }

        if (openedStamp != null && !openedStamp.Matches(source, out string externalChange))
            return Refused("Not saved: " + externalChange);

        var transactionStamp = DocumentSourceStamp.Capture(source);

        NativeSave.Plan plan;
        try
        {
            plan = NativeSave.Compare(savedXml, currentXml);
        }
        catch (Exception error)
        {
            return Refused(
                "Could not work out what changed, so nothing was written: " + error.Message);
        }

        if (!plan.Possible)
            return Refused(plan.Refusal ?? "native save does not support this edit yet");
        if (plan.Empty)
            return new Result(Outcome.Unchanged, plan, "Nothing to save.");

        string? blocked = HkxTextEdit.WhyNotWritable(path);
        if (blocked != null)
            return Refused("Cannot save: " + blocked);

        byte[] rebuilt;
        try
        {
            rebuilt = NativeSave.Apply(source, plan);
        }
        catch (Exception error)
        {
            return Refused(
                "Not saved, and the original is untouched: " + error.Message);
        }

        try
        {
            SaveVerifier.Verify(source, rebuilt, plan);
            if (verificationFault != null) throw verificationFault();
        }
        catch (Exception error)
        {
            return Refused(
                "The rebuilt file failed verification, so nothing was written: " + error.Message);
        }

        beforeSourceRecheck?.Invoke();
        if (!transactionStamp.Matches(path, out externalChange))
            return Refused("Not saved: " + externalChange);

        try
        {
            FileSafety.Backup(path);
            FileSafety.Replace(path, rebuilt);
        }
        catch (Exception error)
        {
            return Refused("Not saved: the file could not be written: " + error.Message);
        }

        return new Result(Outcome.Committed, plan, SuccessMessage(path, plan));
    }

    private static Result Refused(string message) =>
        new(Outcome.Refused, null, message);

    private static string SuccessMessage(string path, NativeSave.Plan plan)
    {
        string how = plan.Gone.Count > 0
            ? $"and took out {plan.Gone.Count} object{(plan.Gone.Count == 1 ? "" : "s")}, " +
              "so the file was laid out again and everything after them has moved. Object " +
              "numbers above the ones deleted have changed. "
            : plan.Grows
                ? "with anything that grew added on the end so nothing already in it moved. "
                : "leaving every other byte as it was. ";

        return $"Saved {plan.Changes.Count} " +
               $"change{(plan.Changes.Count == 1 ? "" : "s")} straight into the file, " + how +
               $"The original is kept as {Path.GetFileName(path + ".bak")}.";
    }
}
