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
    public void CommitPendingFields()
    {
        foreach (var commit in _fieldCommits.ToList()) commit();
    }

    private void Apply(string objectId, string address, TextBox field, string original)
    {
        if (field.Text == original || _xmlText.Length == 0) return;
        if (!SetParam(objectId, address, field.Text ?? "")) field.Text = original;
    }

    private bool SetParam(string objectId, string address, string value)
    {
        if (_xmlText.Length == 0) return false;

        try
        {
            Commit(HkxTextEdit.SetParamAt(_xmlText, objectId, address, value));
            _editedFields.Add(objectId + "." + address);
            SetStatus($"#{objectId}.{address} = {value}   (unsaved)", Ux.CodeBrush);

            if (address == "playbackSpeed" && objectId == _selectedId && _clock != null)
            {
                _playback.SetSpeed(SelectedPlaybackSpeed());
                _clock.Interval = _playback.Interval;
            }

            return true;
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message.Split('\n')[0], Ux.MutedBrush);
            return false;
        }
    }

    private void Validate()
    {
        if (_xmlText.Length == 0 && _reading.Objects.Count == 0)
        {
            SetStatus("Nothing loaded to check.", Ux.MutedBrush);
            return;
        }

        var findings = _xmlText.Length > 0
            ? GraphValidator.Check(_xmlText, _projectChain)
            : GraphValidator.Check(_reading, _projectChain, _bytes);
        var errors = findings.Where(f => f.Level == GraphValidator.Level.Error).ToList();
        var warnings = findings.Where(f => f.Level == GraphValidator.Level.Warning).ToList();

        foreach (var f in findings) Console.WriteLine("check  " + f);

        _graph.Mark(GraphValidator.ByObject(findings));

        _problems.Clear();
        foreach (var f in errors.Concat(warnings))
        {
            bool error = f.Level == GraphValidator.Level.Error;
            _problems.Add(null, error ? "error" : "warning", f.Where, f.What)
                      .Colour(0, error ? Ux.BadBrush : Ux.WarnBrush)
                      .Colour(1, Ux.CodeBrush)
                      .Colour(2, Ux.MetaBrush)
                      .Tag(f.ObjectId);
        }

        bool any = findings.Count > 0;
        _problems.IsVisible = any;
        _problemBar.IsVisible = any;
        _problemBar.Text = any
            ? $"{errors.Count} error{(errors.Count == 1 ? "" : "s")}, " +
              $"{warnings.Count} warning{(warnings.Count == 1 ? "" : "s")}. " +
              "Click one to jump to it on the canvas. Errors are outlined red, warnings amber."
            : "";

        if (!any)
        {
            SetStatus("Checked: nothing wrong found. That is not a promise the game will load it.", Ux.MetaBrush);
            return;
        }

        SetStatus($"{errors.Count} errors, {warnings.Count} warnings. " +
                  $"First: {(errors.Count > 0 ? errors[0] : warnings[0])}",
                  errors.Count > 0 ? Ux.BadBrush : Ux.MutedBrush);
    }

    private async Task ValidateProject()
    {
        var chain = _projectChain;
        if (chain == null || chain.Root.Length == 0)
        {
            SetStatus("No project resolved for this file, so there is no chain to check. See Project → Chain.",
                      Ux.MutedBrush);
            return;
        }

        _problems.Clear();
        _problems.IsVisible = _problemBar.IsVisible = true;
        _problemBar.Text = "Reading the project...";

        long stamp = CaptureStamp();
        var outcome = await _analysis.ValidateProject(
            chain, stamp,
            s => SetStatus("Checking " + s, Ux.MutedBrush),
            ValidateProjectRunner);
        if (outcome.Stale) return;
        if (outcome.Failed)
        {
            Console.Error.WriteLine($"Project check failed: {outcome.Error}");
            _problemBar.Text = "Project check failed.";
            SetStatus("Project check failed. The project files could not be read.", Ux.BadBrush);
            return;
        }

        ProjectCheck.Result result = outcome.Value!;

        foreach (var file in result.Files.Where(f => f.Error.Length > 0 || f.Findings.Count > 0))
        {
            var head = _problems.Add(null, file.Error.Length > 0 ? "unread" : $"{file.Errors}e {file.Warnings}w",
                                     file.Name, file.Error.Length > 0 ? file.Error : "")
                                .Colour(0, file.Error.Length > 0 || file.Errors > 0 ? Ux.BadBrush : Ux.WarnBrush)
                                .Colour(1, Ux.TitleBrush);
            if (file.Findings.Count > 30) head.Collapse();

            foreach (var f in file.Findings.OrderBy(f => f.Level))
                _problems.Add(head, f.Level == GraphValidator.Level.Error ? "error" : "warning", f.Where, f.What)
                         .Colour(0, f.Level == GraphValidator.Level.Error ? Ux.BadBrush : Ux.WarnBrush)
                         .Colour(1, Ux.CodeBrush).Colour(2, Ux.MetaBrush)
                         .Tag(file.Path == _hkxPath ? f.ObjectId : "");
        }

        if (result.Files.All(f => f.Error.Length == 0 && f.Findings.Count == 0))
            _problems.Add(null, "", "nothing wrong found", "across every behaviour in this project")
                     .Colour(2, Ux.MutedBrush);

        _problemBar.Text = result + ". Only findings in the open file can be jumped to on the canvas.";
        SetStatus("Project checked. " + result, result.Errors > 0 ? Ux.BadBrush : Ux.MetaBrush);
    }

    private void OnWindowKey(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Escape && _tour.IsActive)
        {
            _tour.Skip();
            e.Handled = true;
            return;
        }

        bool control = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);
        if (!control) return;
        bool shift = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift);

        if (e.Key == Avalonia.Input.Key.Z && !shift) { Undo(); e.Handled = true; }
        else if (e.Key == Avalonia.Input.Key.Y || (e.Key == Avalonia.Input.Key.Z && shift)) { Redo(); e.Handled = true; }
    }

    private void Commit(string newXml)
    {
        if (newXml == _xmlText) return;

        _documentStamp++;
        _undo.Add(_xmlText);
        if (_undo.Count > UndoDepth) _undo.RemoveAt(0);
        _redo.Clear();
        _xmlText = newXml;
        RefreshDirty();
    }

    private void ResetHistory()
    {
        _undo.Clear();
        _redo.Clear();
        _savedXml = _xmlText;
        RefreshDirty();
    }

    private void RefreshDirty()
    {
        _dirty = _xmlText.Length > 0 && _xmlText != _savedXml;

        _saveButton.IsEnabled = _dirty && !_readOnly;
        if (_readOnly) ToolTip.SetTip(_saveButton, _readOnlyWhy);
        _undoButton.IsEnabled = _undo.Count > 0;
        _redoButton.IsEnabled = _redo.Count > 0;
        _bridgeSave.IsEnabled = _saveButton.IsEnabled;
        _bridgeUndo.IsEnabled = _undoButton.IsEnabled;
        _bridgeRedo.IsEnabled = _redoButton.IsEnabled;
    }

    private void Undo()
    {
        if (_undo.Count == 0) { SetStatus("Nothing to undo.", Ux.MutedBrush); return; }
        _redo.Add(_xmlText);
        _xmlText = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        AfterHistoryMove("Undone");
    }

    private void Redo()
    {
        if (_redo.Count == 0) { SetStatus("Nothing to redo.", Ux.MutedBrush); return; }
        _undo.Add(_xmlText);
        _xmlText = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        AfterHistoryMove("Redone");
    }

    private void AfterHistoryMove(string what)
    {
        _documentStamp++;
        RefreshDirty();

        var model = Model();
        _objectIds = HkxTextEdit.ObjectIds(_xmlText);
        _emptyStates = GraphValidator.StatesWithNoGenerator(model);
        RebuildTree();
        _graph.Show(model);
        BuildSymbols(model);
        ClearProps();
        if (_selectedId.Length > 0 && model.Get(_selectedId) != null) ShowProps(_selectedId, model);

        SetStatus($"{what}. {_undo.Count} step{(_undo.Count == 1 ? "" : "s")} back, " +
                  $"{_redo.Count} forward." + (_dirty ? "   (unsaved)" : "   this now matches the file on disk"),
                  Ux.MetaBrush);
    }

    private void SetSummary(string text, IBrush brush)
    {
        _summary.Text = text;
        _summary.Foreground = brush;
        _bridgeFile.Text = text;
        _bridgeFile.Foreground = brush;
    }

    private void SetStatus(string text, IBrush brush)
    {
        _status.Text = text;
        _status.Foreground = brush;
        _bridgeLastAction.Text = text;
        _bridgeLastAction.Foreground = brush;
    }
}
