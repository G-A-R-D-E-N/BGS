using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using BehaviourStudio.App;
using Xunit;

namespace BehaviourStudio.Tests;

[Collection("AssistantChatStore")]
public sealed class AssistantPrivateFileTests
{
    [Fact]
    public void LinkedStorageDirectoryIsRejectedOnUnix()
    {
        if (OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "bgs-link-" + Guid.NewGuid().ToString("N"));
        string target = root + "-target";
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(root, target);
        try
        {
            Assert.Throws<IOException>(() => AssistantPrivateFiles.EnsureDirectory(root));
            Assert.Empty(Directory.GetFiles(target));
        }
        finally
        {
            Directory.Delete(root);
            Directory.Delete(target);
        }
    }

    [Fact]
    public void FailedStagedWritePreservesPreviousChatAndRemovesTemporaryFile()
    {
        string previous = AssistantChatStore.Folder;
        string root = Path.Combine(Path.GetTempPath(), "bgs-stage-" + Guid.NewGuid().ToString("N"));
        AssistantChatStore.Folder = root;
        try
        {
            AssistantChat chat = AssistantChatStore.CreateEmpty().WithTitle("original");
            AssistantChatStore.Save(chat);
            AssistantChatStore.SaveFailureForTest = () =>
            {
                Assert.Single(Directory.GetFiles(root, "*.tmp"));
                throw new IOException("simulated staged write failure");
            };
            Assert.Throws<IOException>(() => AssistantChatStore.Save(chat.WithTitle("replacement")));
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
            Assert.Equal("original", AssistantChatStore.Load(chat.Id)!.Title);
        }
        finally
        {
            AssistantChatStore.SaveFailureForTest = null;
            AssistantChatStore.Folder = previous;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task InvalidStorageReturnsProviderErrorWithoutStartingCommand(bool opencode)
    {
        string previous = AssistantChatStore.Folder;
        string occupied = Path.GetTempFileName();
        AssistantChatStore.Folder = occupied;
        var previousRunner = AssistantCommandRunner.RunForTest;
        int commands = 0;
        AssistantCommandRunner.RunForTest = (_, _, _, _, _) =>
        {
            commands++;
            return System.Threading.Tasks.Task.FromResult(new AssistantCommandResult(0, "{\"mcp\":{}}", ""));
        };
        try
        {
            using BgsMcpBridge bridge = BgsMcpBridge.Start(BgsMcpProtocolTests.Tools());
            using IAssistantSession session = opencode
                ? new OpencodeAssistantSession(new AssistantCli("opencode", "configured"), bridge, "")
                : new ClaudeAssistantSession(new AssistantCli("claude", "configured"), bridge, "");
            AssistantReply reply = await session.SendAsync("hello", CodexSessionFixture.Context());
            Assert.Equal("provider_error", reply.Status);
            Assert.Equal(opencode ? 1 : 0, commands);
        }
        finally
        {
            AssistantCommandRunner.RunForTest = previousRunner;
            AssistantChatStore.Folder = previous;
            File.Delete(occupied);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedTranscriptsHaveOwnerOnlyPermissions(bool existingFolder)
    {
        string previous = AssistantChatStore.Folder;
        string root = Path.Combine(Path.GetTempPath(), "bgs-private-" + Guid.NewGuid().ToString("N"));
        AssistantChatStore.Folder = root;
        try
        {
            if (existingFolder)
            {
                Directory.CreateDirectory(root);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                        UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            }
            AssistantChat chat = AssistantChatStore.CreateEmpty();
            AssistantChatStore.Save(chat);
            AssertOwnerOnly(root, true);
            AssertOwnerOnly(AssistantChatStore.FilePathFor(chat.Id), false);
            AssistantChatStore.Save(chat.WithTitle("replacement"));
            AssertOwnerOnly(AssistantChatStore.FilePathFor(chat.Id), false);
        }
        finally
        {
            AssistantChatStore.Folder = previous;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    internal static void AssertOwnerOnly(string path, bool directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            UnixFileMode expected = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            if (directory) expected |= UnixFileMode.UserExecute;
            Assert.Equal(expected, File.GetUnixFileMode(path));
            return;
        }
        FileSystemSecurity security = directory
            ? new DirectoryInfo(path).GetAccessControl()
            : new FileInfo(path).GetAccessControl();
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        Assert.True(security.AreAccessRulesProtected);
        FileSystemAccessRule rule = Assert.Single(security.GetAccessRules(true, true,
            typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>());
        Assert.Equal(identity.User, rule.IdentityReference);
        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights);
    }
}
