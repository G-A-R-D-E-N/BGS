using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace BehaviourStudio.App;

internal static class AssistantPrivateFiles
{
    private const UnixFileMode FileModeBits = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    internal static void EnsureDirectory(string path)
    {
        RejectLink(path);
        if (OperatingSystem.IsWindows())
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(identity.User!);
            security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            var directory = new DirectoryInfo(path);
            directory.Create(security);
            RejectLink(path);
            directory.SetAccessControl(security);
        }
        else
        {
            Directory.CreateDirectory(path, FileModeBits | UnixFileMode.UserExecute);
            RejectLink(path);
            File.SetUnixFileMode(path, FileModeBits | UnixFileMode.UserExecute);
        }
    }

    internal static string SessionDirectory() =>
        Path.Combine(AssistantChatStore.Folder, "sessions", Guid.NewGuid().ToString("N"));

    internal static void EnsureSessionDirectory(string path)
    {
        AssistantChatStore.EnsureFolder();
        EnsureDirectory(Path.GetDirectoryName(path)!);
        EnsureDirectory(path);
    }

    internal static FileStream CreateFile(string path, FileMode mode = FileMode.CreateNew)
    {
        RejectLink(path);
        if (OperatingSystem.IsWindows())
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            var security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(identity.User!);
            security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl,
                AccessControlType.Allow));
            return new FileInfo(path).Create(mode, FileSystemRights.FullControl, FileShare.None,
                4096, FileOptions.WriteThrough, security);
        }
        return new FileStream(path, new FileStreamOptions
        {
            Mode = mode, Access = FileAccess.Write, Share = FileShare.None,
            Options = FileOptions.WriteThrough, UnixCreateMode = FileModeBits,
        });
    }

    internal static void WriteAllText(string path, string text)
    {
        using var writer = new StreamWriter(CreateFile(path, FileMode.Create));
        writer.Write(text);
    }

    private static void RejectLink(string path)
    {
        if (new FileInfo(path).LinkTarget is not null ||
            (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
            throw new IOException("Assistant private storage cannot be a symbolic link.");
    }
}
