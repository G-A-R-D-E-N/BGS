using System.Collections.Generic;

namespace OpenCommonwealth.Services.Hkx;

/// <summary>
/// Adapts the canonical round-trip report to the existing Problems grid contract. The prefix is
/// intentionally stable so project summaries/exports can keep these rows separate from graph
/// validation errors while MainWindow can render them without a second diagnostics system.
/// </summary>
public static class RoundTripFindingAdapter
{
    public const string Prefix = "round-trip: ";

    public static List<GraphValidator.Finding> For(RoundTripReport report)
    {
        var findings = new List<GraphValidator.Finding>();
        foreach (var loss in report.Losses)
        {
            string where = Prefix + loss.Kind +
                           (loss.Member.Length > 0 ? "." + loss.Member : "");
            findings.Add(new GraphValidator.Finding
            {
                Level = GraphValidator.Level.Error,
                Where = where,
                What = loss.Message,
                ObjectId = loss.ObjectId?.ToString() ?? "",
                BlocksSave = true,
            });
        }
        return findings;
    }

    public static bool IsRoundTrip(GraphValidator.Finding finding) =>
        finding.Where.StartsWith(Prefix, System.StringComparison.Ordinal);
}
