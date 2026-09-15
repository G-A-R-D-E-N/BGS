using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCommonwealth.Services.Archive;
using OpenCommonwealth.Services.Hkx;
using OpenCommonwealth.Services.Nif;
using OpenCommonwealth.Services;

namespace BehaviourStudio.App;

public partial class MainWindow : Window
{
    private async Task Browse()
    {
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a Havok file",
            AllowMultiple = false,
            SuggestedStartLocation = await StartFolder(),
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Havok files")
                {
                    Patterns = new[] { "*.hkx", "*.HKX", "*.hkt", "*.HKT" },
                },
                FilePickerFileTypes.All,
            },
        });

        string? path = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        if (path == null) return;

        Open(path);
        if (RememberSetting("last_folder", Path.GetDirectoryName(path) ?? "", "The file opened") is { } warning)
            SetStatus(warning, Ux.WarnBrush);
    }

    private async Task<IStorageFolder?> StartFolder()
    {
        string[] candidates =
        {
            Settings.Get("last_folder"),
            Path.GetDirectoryName(_pathField.Text ?? "") ?? "",
        };

        foreach (string dir in candidates)
            if (dir.Length > 0 && Directory.Exists(dir))
                return await StorageProvider.TryGetFolderFromPathAsync(dir);

        return null;
    }

    public void Open(string path)
    {
        _pathField.Text = path;
        Load();
    }

    private static string? RememberSetting(string key, string value, string action)
    {
        return Settings.TrySet(key, value, out string failure)
            ? null
            : $"{action}, but the preference was not saved: {failure}";
    }

    private void SaveCurrentGraphLayout()
    {
        if (_hkxPath.Length == 0 || _graph.LayoutMode != GraphLayoutMode.Freeform) return;
        if (!Settings.TrySetGraphLayout(_hkxPath, _graph.SnapshotFreeformPositions(), out string failure))
            SetStatus($"Could not save graph layout: {failure}", Ux.WarnBrush);
    }

    public void OpenMesh(string nifPath) => LoadMesh(nifPath);

    public int MeshEdges => _skeleton.DrawnEdges;

    public static string? RefuseReason(string path)
    {
        string name = Path.GetFileName(path);
        bool looksHavok = name.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase) ||
                          name.EndsWith(".hkt", StringComparison.OrdinalIgnoreCase);
        if (!looksHavok)
            return $"{name} does not look like a Havok behaviour file. " +
                   "Behaviour Graph Studio opens Fallout 4 .hkx behaviour files.";
        if (!HkxBinaryReader.IsFo4Hkx(path))
            return $"{name} is not a Fallout 4 hk_2014.1.0-r1 packfile.";
        return null;
    }
    private void Load()
    {
        string path = (_pathField.Text ?? "").Trim().Trim('"');
        if (path.Length == 0) { SetSummary("Enter the path to a .hkx file.", Ux.MutedBrush); return; }
        if (!File.Exists(path))
        {
            string full = Path.GetFullPath(path);
            SetSummary(full == path ? "Not found: " + path
                                    : $"Not found: {path}, which from here means {full}", Ux.BadBrush);
            if (_hkxPath.Length > 0) _pathField.Text = _hkxPath;
            return;
        }
        if (RefuseReason(path) is { } reason)
        {
            SetSummary(reason, Ux.BadBrush);
            if (_hkxPath.Length > 0) _pathField.Text = _hkxPath;
            return;
        }

        if (!ConfirmDiscard("open this file"))
        {
            if (_hkxPath.Length > 0) _pathField.Text = _hkxPath;
            return;
        }

        DocumentSourceStamp sourceStamp;
        try
        {
            sourceStamp = DocumentSourceStamp.Capture(path);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException)
        {
            SetSummary("Could not read the file consistently enough to open it: " + e.Message.Split('\n')[0],
                       Ux.BadBrush);
            return;
        }

        _documentStamp++;
        _sourceStamp = sourceStamp;
        _tree.Clear();
        _clips.Clear();
        ClearProps();
        _offsetToIndex.Clear();
        _objectIds = new List<string>();
        _bytes = null;
        _editedFields.Clear();
        _xmlText = "";
        _xmlPath = "";

        _reading = new BehaviourGraphModel();
        _selectedId = "";
        _projectChain = null;
        _emptyStates = new HashSet<string>();

        _graph.Reset();
        BuildMachineNavigator(new BehaviourGraphModel());
        SetMachineNavigatorActive(Array.Empty<string>());
        ClearPose();
        ClearMesh();
        ClearProjectSearch();
        ResetHistory();
        _readOnly = false;
        _readOnlyWhy = "";

        if (NonEightByteLayout(path) is int layoutWidth)
        {
            _hkxPath = path;
            _root = null;
            _objects = new List<HkxBehaviorParser.BehaviorNode>();
            _classWarning = "";
            _animation.Clear();
            _animationData = null;
            _animationSummary.Text = "";
            RememberRecent(path);
            RefreshRecents();
            RememberSetting("last_path", path, "The file opened");
            SetSummary(
                $"{Path.GetFileName(path)}   uses the {layoutWidth}-byte pointer layout, which BGS cannot " +
                "open for editing yet. Convert it to the 8-byte layout first (symrm convert), then open the result.",
                Ux.WarnBrush);
            BuildClipList(new BehaviourGraphModel());
            RefreshPasteSlots();
            RefreshTemplates();
            return;
        }

        TryLoadRagdoll(path);

        bool isAnimation = BuildAnimation(path);

        var root = HkxBehaviorParser.ParseBehavior(path);
        if (root == null)
        {
            _hkxPath = path;
            RememberRecent(path);
            RefreshRecents();
            string? rootSettingsWarning = RememberSetting("last_path", path, "The file opened") ??
                                          RememberSetting("last_folder", Path.GetDirectoryName(path) ?? "",
                                                          "The file opened");
            SetSummary(isAnimation
                ? $"{Path.GetFileName(path)}   an animation, not a behaviour. See Animation and Animation → Playback."
                : "Parsed as FO4 hkx, but no root object was resolved.", Ux.MutedBrush);
            SetStatus(rootSettingsWarning ?? _animationSummary.Text ?? "",
                      rootSettingsWarning == null ? _animationSummary.Foreground ?? Ux.MutedBrush : Ux.WarnBrush);

            BuildClipList(new BehaviourGraphModel());

            if (_animationData != null)
            {
                LoadPose(path, Path.GetFileName(path));
                FindMeshForFile();
                AppendRagdollSourceNote();
            }
            return;
        }

        if (!isAnimation)
        {
            _animationSummary.Text = "This is a behaviour file. It holds no animation.";
            _animationSummary.Foreground = Ux.MutedBrush;
            _animation.Clear();
            _animationData = null;
        }

        _hkxPath = path;
        _root = root;
        _objects = new List<HkxBehaviorParser.BehaviorNode>(HkxBehaviorParser.LastObjects);
        for (int i = 0; i < _objects.Count; i++) _offsetToIndex[_objects[i].Offset] = i;

        _classWarning = "";
        try
        {
            var bytes = new PackfileObjects(PackfileImage.Read(path));

            var problems = HavokClassTypes.Shipped.SignatureProblems(bytes.ClassNames());
            if (problems.Count > 0)
            {
                _classWarning = $"Unsupported class signature: {problems[0]}" +
                                (problems.Count > 1 ? $", and {problems.Count - 1} more like it" : "") +
                                ". Values are not read from the bytes when the classes do not match.";
                _bytes = null;
            }
            else _bytes = bytes;
        }
        catch (Exception) { _bytes = null; }

        RefreshTemplates();
        _applyPredefinedTemplate.IsEnabled = _bytes != null && !_readOnly;

        var classes = new HashSet<string>();
        int clips = 0;
        foreach (var o in _objects)
        {
            classes.Add(o.ClassName);
            if (!string.IsNullOrEmpty(o.AnimationName)) clips++;
        }

        SetSummary(isAnimation
                       ? $"{Path.GetFileName(path)}   an animation, not a behaviour. See Animation and Animation → Playback."
                       : $"{Path.GetFileName(path)}   root {root.ClassName}   {_objects.Count} objects   " +
                         $"{classes.Count} classes   {clips} clip references" +
                         (_classWarning.Length > 0 ? "   —   " + _classWarning : ""),
                   isAnimation ? Ux.MutedBrush : _classWarning.Length > 0 ? Ux.WarnBrush : Ux.TitleBrush);

        RebuildTree();
        RememberRecent(path);
        RefreshRecents();
        string? settingsWarning = RememberSetting("last_path", path, "The file opened") ??
                                  RememberSetting("last_folder", Path.GetDirectoryName(path) ?? "", "The file opened");
        PrepareEditing();

        if (_animationData != null)
        {
            LoadPose(path, Path.GetFileName(path));
            AppendRagdollSourceNote();
        }
        if (settingsWarning != null) SetStatus(settingsWarning, Ux.WarnBrush);
    }

    private void AppendRagdollSourceNote()
    {
        if (_ragdollSourceNote == null) return;
        SetPlaybackSummary(_summaryBaseText + "  |  " + _ragdollSourceNote, Ux.MetaBrush);
    }

    private static int? NonEightByteLayout(string path)
    {
        try
        {
            int size = PackfileImage.Read(path).Layout.PointerSize;
            return size == 8 ? null : size;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
