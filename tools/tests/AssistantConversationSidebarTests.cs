using System;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class AssistantConversationSidebarTests
{
    [Fact]
    public void ARowSummarisesItsMessageCountAndAge()
    {
        Assert.Equal("1 message \u00B7 just now",
            AssistantConversationSidebar.Describe(
                new AssistantChatSummary("id", "Title", DateTime.UtcNow, 1)));
        Assert.Equal("3 messages \u00B7 5m ago",
            AssistantConversationSidebar.Describe(
                new AssistantChatSummary("id", "Title", DateTime.UtcNow.AddMinutes(-5), 3)));
    }

    [Fact]
    public void AgesStepUpThroughMinutesHoursAndDays()
    {
        Assert.Equal("just now", AssistantConversationSidebar.Relative(DateTime.UtcNow.AddSeconds(-30)));
        Assert.Equal("9m ago", AssistantConversationSidebar.Relative(DateTime.UtcNow.AddMinutes(-9)));
        Assert.Equal("1h ago", AssistantConversationSidebar.Relative(DateTime.UtcNow.AddMinutes(-90)));
        Assert.Equal("3d ago", AssistantConversationSidebar.Relative(DateTime.UtcNow.AddDays(-3)));
    }

    [Fact]
    public void AnOldChatShowsADateRatherThanAnAge()
    {
        string shown = AssistantConversationSidebar.Relative(DateTime.UtcNow.AddDays(-30));

        Assert.DoesNotContain("ago", shown, StringComparison.Ordinal);
        Assert.NotEmpty(shown);
    }

    [Fact]
    public void AFutureTimestampIsReportedAsJustNowInsteadOfNegativeAge()
    {
        Assert.Equal("just now", AssistantConversationSidebar.Relative(DateTime.UtcNow.AddMinutes(5)));
    }
}
