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
    private Control BuildSymbolsTab()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        bar.Children.Add(_symbolName);
        bar.Children.Add(_symbolValue);
        bar.Children.Add(_symbolMin);
        bar.Children.Add(_symbolMax);

        foreach (var (label, type) in new (string, SymbolEditor.VariableType)[]
                 {
                     ("+ real", SymbolEditor.VariableType.Real),
                     ("+ int", SymbolEditor.VariableType.Int32),
                     ("+ bool", SymbolEditor.VariableType.Bool),
                 })
        {
            var captured = type;
            var button = Ux.Secondary(label);
            button.Click += (_, _) => AddSymbolVariable(captured);
            bar.Children.Add(button);
        }

        foreach (var (label, action) in new (string, Action)[]
                 {
                     ("+ event", AddSymbolEvent),
                     ("Rename", RenameSymbol),
                     ("Set value", SetSymbolValue),
                     ("Set bounds", SetSymbolBounds),
                     ("Remove", RemoveSymbol),
                 })
        {
            var captured = action;
            var button = Ux.Secondary(label);
            button.Click += (_, _) => captured();
            bar.Children.Add(button);
        }

        var papyrus = Ux.Secondary("Scripts folder...");
        papyrus.Click += async (_, _) => await PickScriptsFolder();
        ToolTip.SetTip(papyrus, "A folder of .psc sources, to show which scripts send each event");
        bar.Children.Add(papyrus);

        bar.Children.Add(Ux.Pill(_symbolAudit));

        var panel = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        bar.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(bar);
        panel.Children.Add(_symbols);
        return panel;
    }

    private async Task PickScriptsFolder()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Where the Papyrus .psc sources are",
            AllowMultiple = false,
        });

        string? folder = picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
        if (folder == null) return;

        string? settingsWarning = RememberSetting("scripts", folder, "The scripts folder was selected");
        await ScanPapyrusFolder(folder, settingsWarning);
    }

    private async Task ScanPapyrusFolder(string folder, string? settingsWarning)
    {
        _papyrusScanned = true;
        long stamp = CaptureStamp();
        var outcome = await _analysis.ScanPapyrus(folder, stamp, PapyrusScanRunner);
        if (outcome.Stale) return;
        if (outcome.Failed)
        {
            Console.Error.WriteLine($"Scripts scan failed: {outcome.Error}");
            SetStatus("Scripts scan failed. The Papyrus sources could not be read.", Ux.BadBrush);
            return;
        }

        _papyrus = outcome.Value!;
        SetStatus(settingsWarning ?? _papyrus.ToString(),
                  settingsWarning == null && _papyrus.ScriptsRead > 0 ? Ux.MetaBrush : Ux.MutedBrush);

        if (_xmlText.Length > 0) BuildSymbols(Model());
    }


    public void QueuePendingFieldCommitForTest(Action commit) => _fieldCommits.Add(commit);

    private bool SavePendingChanges()
    {
        bool document = _dirty;
        bool animation = _animationEdited;
        if (document && !WriteDocument()) return false;
        if (animation && !WriteAnimation()) return false;
        if (!document && !animation) return true;
        return ReloadAfterWrite(animation);
    }

    private bool ConfirmDiscard(string what)
    {
        CommitPendingFields();
        if (_reloading) return true;
        if (!_dirty && !_animationEdited) return true;
        return (DiscardDecision ?? (() => ShowDiscardDialog(what)))() switch
        {
            DiscardChoice.Discard => true,
            DiscardChoice.Save => SavePendingChanges(),
            _ => false,
        };
    }

    private DiscardChoice ShowDiscardDialog(string what)
    {
        var dialog = new DiscardDialog(what);
        dialog.ShowDialog(this);
        while (dialog.IsVisible)
        {
            Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(10);
        }
        return dialog.Choice;
    }

    private async Task<DiscardChoice> ShowDiscardDialogAsync(string what)
    {
        var dialog = new DiscardDialog(what);
        return await dialog.ShowDialog<DiscardChoice>(this);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || _closeApproved) return;

        CommitPendingFields();
        if (!_dirty && !_animationEdited)
        {
            _closeApproved = true;
            return;
        }

        e.Cancel = true;
        _ = CloseAfterDecision();
    }

    protected override void OnClosed(EventArgs e)
    {
        _gameData?.Dispose();
        _gameData = null;
        base.OnClosed(e);
    }

    private async Task CloseAfterDecision()
    {
        DiscardChoice choice;
        if (DiscardDecision is { } decide) choice = decide();
        else choice = await ShowDiscardDialogAsync("close the window");

        bool proceed = choice switch
        {
            DiscardChoice.Discard => true,
            DiscardChoice.Save => SavePendingChanges(),
            _ => false,
        };
        if (proceed)
        {
            _closeApproved = true;
            Close();
        }
    }

    private bool SaveCurrent()
    {
        if (!WriteDocument()) return false;
        return ReloadAfterWrite(animation: false);
    }

    private void Save()
    {
        CommitPendingFields();
        if (!_dirty || _xmlText.Length == 0) return;
        SaveCurrent();
    }

    private bool SaveAnimation()
    {
        if (!WriteAnimation()) return false;
        return ReloadAfterWrite(animation: true);
    }

    private bool WriteDocument()
    {
        if (_readOnly) { SetStatus("Not saved: " + _readOnlyWhy, Ux.BadBrush); return false; }

        if (_xmlText.Length > 0)
        {
            string? refusal = GraphValidator.SaveRefusal(_xmlText, _savedXml, includeRepackLosses: false);
            if (refusal != null) { SetStatus(refusal, Ux.BadBrush); return false; }
        }

        var result = DocumentSaveTransaction.Commit(
            _hkxPath, _savedXml, _xmlText, _sourceStamp, VerifyFaultForTest);
        if (!result.Committed)
        {
            SetStatus(result.Message, result.Unchanged ? Ux.MutedBrush : Ux.BadBrush);
            return false;
        }

        ResetHistory();
        SetStatus(result.Message, Ux.MetaBrush);
        RefreshSourceStamp();
        return true;
    }

    private bool WriteAnimation()
    {
        var anim = _animationData;
        if (anim == null)
        {
            _frameEditAnswer.Text = "This is not an animation file.";
            _frameEditAnswer.Foreground = Ux.BadBrush;
            return false;
        }

        if (_readOnly)
        {
            _frameEditAnswer.Text = "Not saved: " + _readOnlyWhy;
            _frameEditAnswer.Foreground = Ux.BadBrush;
            return false;
        }

        var result = AnimationSaveTransaction.Commit(
            _hkxPath, anim, _sourceStamp, _editTrack, _editFrame,
            verificationFault: VerifyFaultForTest);
        if (!result.Committed)
        {
            _frameEditAnswer.Text = result.Message;
            _frameEditAnswer.Foreground = Ux.BadBrush;
            return false;
        }

        _animationEdited = false;
        _frameEditAnswer.Text = result.Message;
        _frameEditAnswer.Foreground = Ux.MetaBrush;
        SetStatus(result.Message, Ux.MetaBrush);
        RefreshSourceStamp();
        return true;
    }

    private void RefreshSourceStamp()
    {
        try { _sourceStamp = DocumentSourceStamp.Capture(_hkxPath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _sourceStamp = null;
        }
    }

    private bool ReloadAfterWrite(bool animation)
    {
        try
        {
            _reloading = true;
            Load();
            if (ReloadFaultForTest is { } fault) throw fault();
        }
        catch (Exception e)
        {
            string said = "The file was saved, but the editor could not reload it: " + e.Message;
            if (animation)
            {
                _frameEditAnswer.Text = said;
                _frameEditAnswer.Foreground = Ux.BadBrush;
            }
            SetStatus(said, Ux.BadBrush);
            return false;
        }
        finally
        {
            _reloading = false;
        }
        return true;
    }


    private BehaviourGraphModel Model() =>
        _xmlText.Length > 0 ? BehaviourGraphModel.Parse(_xmlText) : _reading;

    private BehaviourGraphModel _reading = new();

    private void PrepareEditing()
    {
        var reading = _bytes == null ? null : NativeGraphModel.From(_bytes);

        bool own = false;
        if (_bytes != null && reading != null)
        {
            try
            {
                string work = Path.Combine(Path.GetTempPath(), "bgs_edit", TempDirKey(_hkxPath));
                HkxTextEdit.ResetDirectory(work);

                _xmlPath = Path.Combine(work, Path.GetFileNameWithoutExtension(_hkxPath) + ".xml");

                _xmlText = NativeXml.From(InputFilePolicy.ReadHkx(_hkxPath));
                File.WriteAllText(_xmlPath, _xmlText);

                _objectIds = HkxTextEdit.ObjectIds(_xmlText);
                own = _objectIds.Count == _objects.Count;

                if (!own)
                {
                    _xmlText = "";
                    _objectIds = new List<string>();
                }
            }
            catch
            {
                _xmlText = "";
                _objectIds = new List<string>();
            }
        }

        ResetHistory();

        var model = reading ?? (_xmlText.Length > 0 ? Model() : null);
        if (model == null)
        {
            SetStatus("Read only, so Graph, Inspect → Symbols, Project → Chain and Animation stay empty: " +
                      "this file holds a class this build cannot describe. The tree is read straight " +
                      "from the binary. Save stays off for this file.", Ux.WarnBrush);
            return;
        }

        if (_objectIds.Count == 0) _objectIds = model.Objects.Select(o => o.Id).ToList();
        _reading = model;

        _emptyStates = GraphValidator.StatesWithNoGenerator(model);
        RebuildTree();

        _graph.RestoreFreeformPositions(Settings.GetGraphLayout(_hkxPath));
        _graph.Show(model);
        _graph.FrameAll();
        BuildMachineNavigator(model);
        BuildSymbols(model);
        BuildClipList(model);
        BuildChain();
        StartRun();

        string source = reading != null ? "read from the file itself" : "read by the internal developer fallback";
        SetStatus(_xmlText.Length > 0
            ? $"Editable. {_objectIds.Count} objects mapped, {_graph.DrawnCount} drawn, {source}."
            : $"{_objectIds.Count} objects mapped, {_graph.DrawnCount} drawn, {source}. " +
              "This file holds a class this build cannot describe, so it is read only.",
            _xmlText.Length > 0 ? Ux.MetaBrush : Ux.WarnBrush);
    }

    private bool IsEmptyState(int offset) =>
        _emptyStates.Count > 0
        && _offsetToIndex.TryGetValue(offset, out int index)
        && index < _objectIds.Count
        && _emptyStates.Contains(_objectIds[index]);

}
