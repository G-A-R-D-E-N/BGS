using System.IO;
using Avalonia.Controls;

namespace BehaviourStudio.App;

public partial class MainWindow
{
    internal bool AssistantExportPathAllowed(string path)
    {
        try
        {
            string root = _projectChain?.Root ?? Path.GetDirectoryName(_hkxPath) ?? "";
            string canonical = Path.GetFullPath(path);
            if (root.Length == 0 || !Within(root, canonical) || Directory.Exists(canonical)) return false;
            var file = new FileInfo(canonical);
            if (file.LinkTarget is not null || file.Exists && file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return false;
            for (DirectoryInfo? parent = file.Directory; parent is not null; parent = parent.Parent)
                if (!parent.Exists || parent.LinkTarget is not null || parent.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    return false;
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    internal async void AssistantFile(Window target, string operation, string path)
    {
        try
        {
            if (target is RigAuthoringWindow rig)
            {
                switch (operation)
                {
                    case "skeleton": rig.OpenSkeleton(path); break;
                    case "skin": rig.OpenSkin(path); break;
                    case "ragdoll": rig.OpenPhysics(path); break;
                    default: throw new ArgumentException("Use skeleton, skin or ragdoll in the rig editor.");
                }
                return;
            }
            switch (operation)
            {
                case "open": await AssistantOpen(path); break;
                case "mesh": OpenMesh(path); break;
                case "compare": CompareLoadedWith(path); break;
                case "archive": await OpenArchive(path); break;
                case "scripts": await ScanPapyrusFolder(path, RememberSetting("scripts", path, "The scripts folder was selected")); break;
                case "game_data": _dataField.Text = path; ApplyGameData(); break;
                case "mods": _modsField.Text = path; ApplyMods(); break;
                case "export_diff_text": await WriteDiff(path, false, true); break;
                case "export_diff_json": await WriteDiff(path, true, true); break;
                default: throw new ArgumentException("Unknown file operation.");
            }
        }
        catch (Exception error) { SetStatus("Assistant file action refused: " + error.Message, Ux.WarnBrush); }
    }

    private async Task AssistantOpen(string path)
    {
        CommitPendingFields();
        if (!_dirty && !_animationEdited) { Open(path); return; }
        var choice = await ShowDiscardDialogAsync("open this file");
        if (choice == DiscardChoice.Cancel || choice == DiscardChoice.Save && !SavePendingChanges()) return;
        try { _reloading = true; Open(path); }
        finally { _reloading = false; }
    }
}
