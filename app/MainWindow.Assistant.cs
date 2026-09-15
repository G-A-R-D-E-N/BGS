using System;
using System.IO;
using Avalonia.Controls;
using Microsoft.Extensions.AI;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.App;

public partial class MainWindow : Window, IAssistantEditorDocument
{
    internal bool AssistantDrawerOpen => _assistantUi?.IsOpen == true;
    internal AssistantUi AssistantUiForTest =>
        _assistantUi ?? throw new InvalidOperationException("assistant UI is unavailable");

    internal AssistantContext AssistantContextSnapshot => new(
        DocumentId: _hkxPath.Length == 0 ? "" : Path.GetFullPath(_hkxPath),
        DocumentStamp: _documentStamp.ToString(),
        Path: _hkxPath,
        ActiveActivity: SelectedActivity,
        SelectedObjectId: _selectedId,
        SelectedObjectClass: _reading.Get(_selectedId)?.Class ?? "",
        IsDirty: _dirty,
        AnimationEdited: _animationEdited,
        ProblemCount: _problems.RowCount,
        ProjectRoot: _projectChain?.Root ?? "",
        AnimationClass: _animationData?.AnimationClass ?? "",
        SelectedTrack: _editTrack,
        SelectedFrame: _editFrame);

    internal AssistantTools CreateAssistantTools()
    {
        return new AssistantTools(
            new AssistantInspection(),
            new AssistantClipMutation(this),
            AuthorizeAssistantPath);
    }

    internal AssistantSession CreateAssistantSession(IChatClient client, AssistantTools tools) =>
        new(client, tools.Functions, () => tools.HasPendingApproval);

    AssistantEditorSnapshot IAssistantEditorDocument.Snapshot() => new(
        _hkxPath.Length == 0 ? "" : Path.GetFullPath(_hkxPath),
        _documentStamp,
        _xmlText,
        _readOnly,
        _dirty,
        _undo.Count,
        _projectChain);

    bool IAssistantEditorDocument.TryCommit(string xml, out string failure)
    {
        if (_xmlText.Length == 0)
        {
            failure = "the active document has no editable text model";
            return false;
        }
        if (xml == _xmlText)
        {
            failure = "the proposed edit makes no change";
            return false;
        }

        Commit(xml);
        failure = "";
        return true;
    }

    private bool AuthorizeAssistantPath(
        string requested, out string canonical, out string code, out string message)
    {
        canonical = "";
        code = "ok";
        message = "";
        if (string.IsNullOrWhiteSpace(requested))
        {
            code = "invalid_argument";
            message = "path is required";
            return false;
        }

        try { canonical = Path.GetFullPath(requested); }
        catch (Exception)
        {
            code = "path_not_allowed";
            message = "path is not allowed";
            return false;
        }

        if (!File.Exists(canonical))
        {
            code = "path_not_found";
            message = "path was not found";
            return false;
        }

        string root = _projectChain?.Root ?? Path.GetDirectoryName(_hkxPath) ?? "";
        if (root.Length == 0 || !Within(root, canonical))
        {
            code = "path_not_allowed";
            message = "path is outside the active project";
            return false;
        }
        return true;
    }

    private static bool Within(string root, string target)
    {
        string fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(fullRoot, target, comparison) ||
               target.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
    }
}
