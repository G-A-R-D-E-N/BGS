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
    private void StartRun(string note = "Started at the graph's root.")
    {
        var model = Model();
        IReadOnlyDictionary<string, ClipTiming.Clip>? timings = null;

        if (model.Objects.Count > 0 && _bytes != null && _hkxPath.Length > 0)
        {
            try
            {
                timings = ClipTiming.All(_bytes, SymbolEditor.EventNames(model),
                                         ClipTiming.FromDisk(_hkxPath));
            }
            catch (Exception)
            {
            }
        }

        var view = _runSession.Start(model, timings, note);
        _runEvents.ItemsSource = view.Ready ? view.Events : null;
        _runVariables.ItemsSource = view.Ready ? view.Variables : null;

        _runEvents.SelectedIndex = view.Events.Count > 0 ? 0 : -1;
        _runVariables.SelectedIndex = view.Variables.Count > 0 ? 0 : -1;
        if (view.Variables.Count > 0) ShowRunVariable();
        else _runValue.Text = "";

        RenderRun(view);
    }

    private void SendRunEvent()
    {
        RenderRun(_runSession.Send(_runEvents.SelectedItem as string));
    }

    private void ShowRunVariable()
    {
        _runValue.Text = _runSession.ValueText(_runVariables.SelectedItem as string);
    }

    private void SetRunVariable()
    {
        RenderRun(_runSession.SetVariable(_runVariables.SelectedItem as string, _runValue.Text));
    }

    private void RenderRun(GraphRunSession.View view)
    {
        _graph.ShowActive(view.Active.Select(active => active.StateId));
        SetMachineNavigatorActive(view.Active.Where(active => !active.Fading)
                                             .Select(active => active.MachineId));

        _running.Clear();
        foreach (var active in view.Active)
        {
            string machine = active.MachineName.Length > 0 ? active.MachineName : "#" + active.MachineId;
            if (active.Fading) machine = "leaving " + machine;

            var row = _running.Add(null,
                machine,
                active.StateName.Length > 0 ? active.StateName : "#" + active.StateId,
                $"{active.Weight * 100:F0}%")
                .Tag(active.StateId);

            if (active.Fading)
                row.Colour(0, Ux.MutedBrush).Colour(1, Ux.MutedBrush).Colour(2, Ux.MutedBrush);
        }
        _running.IsVisible = view.Ready;
        _step.IsEnabled = view.Blending;

        _runStopsGrid.Clear();
        foreach (var stop in view.Stops)
            _runStopsGrid.Add(null, stop.ClassName, stop.Why).Tag(stop.ObjectId);

        _runHeldBackGrid.Clear();
        foreach (var held in view.HeldBack)
            _runHeldBackGrid.Add(null, held.Event + " to " +
                                      (held.ToStateName.Length > 0 ? held.ToStateName : "#" + held.ToStateId),
                                      held.Condition).Tag(held.ToStateId);

        _runLog.Clear();
        foreach (var entry in view.Log)
        {
            var row = _runLog.Add(null, entry.Text);
            if (entry.TargetStateId.Length > 0) row.Tag(entry.TargetStateId);
        }

        _runOutput.Text = string.Join(Environment.NewLine, view.Output);
        SetRunSummary(view.Summary, RunBrush(view.Kind));
    }

    private static IBrush RunBrush(GraphRunSession.MessageKind kind) => kind switch
    {
        GraphRunSession.MessageKind.Status => Ux.MetaBrush,
        GraphRunSession.MessageKind.Error => Ux.BadBrush,
        _ => Ux.MutedBrush,
    };

    private void SetRunSummary(string text, IBrush brush)
    {
        _runSummary.Text = text;
        _runSummary.Foreground = brush;
        _runtimeStatus.Text = text;
        _runtimeStatus.Foreground = brush;
    }
}
