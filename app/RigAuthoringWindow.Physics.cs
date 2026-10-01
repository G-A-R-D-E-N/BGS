using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OpenCommonwealth.Services;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.App;

public sealed partial class RigAuthoringWindow
{
    private HavokRagdollModel? _physics;
    private RagdollSimulation? _simulation;
    private readonly Dictionary<int, float> _masses = new();
    private readonly Dictionary<int, HavokConstraint> _previewJoints = new();
    private readonly SkeletonView _physicsView = new() { ShowBodies = true, ShowConstraints = true };
    private readonly ComboBox _physicsBodies = new() { MinWidth = 220 };
    private readonly ComboBox _physicsJoints = new() { MinWidth = 220 };
    private readonly TextBox _mass = Ux.Field("positive mass", 110);
    private readonly TextBox _gravity = Ux.Field("gravity", 100);
    private readonly TextBox _ground = Ux.Field("ground Z", 100);
    private readonly TextBox _impulse = Ux.Field("X Y Z impulse", 180);
    private readonly ComboBox _jointAxis = new() { ItemsSource = new[] { "X", "Y", "Z" }, SelectedIndex = 0, Width = 70 };
    private readonly ComboBox _jointRef = new() { ItemsSource = new[] { "X", "Y", "Z" }, SelectedIndex = 1, Width = 70 };
    private readonly TextBox _angleMin = Ux.Field("min radians", 100);
    private readonly TextBox _angleMax = Ux.Field("max radians", 100);
    private readonly TextBox _cone = Ux.Field("cone radians", 100);
    private readonly DispatcherTimer _physicsTimer = new() { Interval = TimeSpan.FromSeconds(1d / 60) };
    private readonly Stopwatch _physicsClock = new();
    private double _lastTick;

    private Control PhysicsPanel()
    {
        _mass.Name = "SimulationMass";
        _gravity.Name = "SimulationGravity";
        _ground.Name = "SimulationGround";
        _gravity.Text = "600";
        _ground.Text = "0";
        _impulse.Text = "0 0 100";
        _angleMin.Text = "-0.5";
        _angleMax.Text = "0.5";
        _cone.Text = "0.8";
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(Row(Button("Open ragdoll HKX", () => _ = BrowsePhysics()),
            Button("Start / resume simulation", () => Run(StartPhysics)),
            Button("Pause simulation", PausePhysics), Button("Reset simulation", ResetPhysics),
            Button("Step physics", () => Run(() => { EnsureSimulation(); PausePhysics(); AdvancePhysics(1f / 60); }))));
        panel.Children.Add(new TextBlock { Text = "Interactive BEPU physics with gravity, hull collisions and preview joints. Simulation settings stay in this window; the HKX is unchanged. This does not reproduce the game's Havok solver.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Row(Ux.Label("Body"), _physicsBodies, Ux.Label("Simulation mass"), _mass,
            Button("Set body mass", () => Run(() => SetMass(false))), Button("Set all masses", () => Run(() => SetMass(true)))));
        panel.Children.Add(new TextBlock { Text = "Inertia is calculated from each measured collision hull assuming uniform density. Directly connected bodies do not collide; other bodies and the ground do.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Row(Ux.Label("Joint"), _physicsJoints, Ux.Label("Axis"), _jointAxis, Ux.Label("Reference"), _jointRef));
        panel.Children.Add(Row(Ux.Label("Preview limits (radians)"), _angleMin, _angleMax, Ux.Label("Cone"), _cone,
            Button("Set preview joint", () => Run(() => SetJoints(false))), Button("Set all preview joints", () => Run(() => SetJoints(true)))));
        panel.Children.Add(new TextBlock { Text = "Choose preview joint limits explicitly. Source joint frames are retained; source angular atoms, motors and mass settings are not imported. These settings do not edit the file.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Row(Ux.Label("Gravity (file units/s²; reset to edit)"), _gravity, Ux.Label("Ground Z"), _ground,
            Ux.Label("Impulse"), _impulse, Button("Push selected body", () => Run(() => {
                EnsureSimulation(); float[] impulse = Values(_impulse, 3);
                _simulation!.Impulse(SelectedBody(), new Vector3(impulse[0], impulse[1], impulse[2]));
            }))));
        _physicsTimer.Tick += (_, _) => {
            try
            {
                double now = _physicsClock.Elapsed.TotalSeconds;
                AdvancePhysics((float)(now - _lastTick));
                _lastTick = now;
            }
            catch (Exception error) { StopPhysics(); Say("Simulation stopped: " + error.Message); }
        };
        _physicsBodies.SelectionChanged += (_, _) => {
            if (_physics == null || _physicsBodies.SelectedIndex < 0) return;
            _mass.Text = _masses.TryGetValue(SelectedBody(), out float mass) ? Text(mass) : "";
        };
        return AuthoringPanel(panel, _physicsView);
    }

    public void OpenPhysics(string path)
    {
        var model = HavokPhysicsExtractor.TryExtract(InputFilePolicy.ReadHkx(path));
        if (model == null || model.Bodies.Count == 0) throw new NotSupportedException("This HKX has no readable ragdoll.");
        StopPhysics();
        _physics = model;
        _physicsView.ShowMesh(null);
        _masses.Clear(); _previewJoints.Clear();
        _physicsBodies.ItemsSource = model.Bodies.Select(body => $"{body.Id}: {body.Name}").ToArray();
        _physicsBodies.SelectedIndex = 0;
        _mass.Text = "";
        _physicsJoints.ItemsSource = model.Constraints.Select(joint => $"{joint.Id}: {joint.Kind}").ToArray();
        _physicsJoints.SelectedIndex = model.Constraints.Count > 0 ? 0 : -1;
        _physicsView.SetBodies(model);
        _physicsView.Frame();
        Say($"Loaded {model.Bodies.Count} bodies and {model.Constraints.Count} joints. Set simulation masses and preview limits before starting.");
    }

    private async Task BrowsePhysics()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open ragdoll HKX",
                FileTypeFilter = new[] { new FilePickerFileType("Havok") { Patterns = new[] { "*.hkx" } } } });
            string? path = files.FirstOrDefault()?.TryGetLocalPath();
            if (path != null) OpenPhysics(path);
        }
        catch (Exception error) { Say(error.Message); }
    }

    private HavokRagdollModel RequiredPhysics() => _physics ?? throw new InvalidOperationException("Open a ragdoll HKX first.");
    private int SelectedBody() => _physicsBodies.SelectedIndex >= 0 ? RequiredPhysics().Bodies[_physicsBodies.SelectedIndex].Id : throw new InvalidOperationException("Choose a body.");

    private void SetMass(bool all)
    {
        var model = RequiredPhysics();
        float mass = Values(_mass, 1)[0];
        if (mass < 0.0001f || mass > 100000) throw new ArgumentException("Mass must be between 0.0001 and 100000.");
        ResetPhysics();
        foreach (var body in model.Bodies.Where(body => all || body.Id == SelectedBody())) _masses[body.Id] = mass;
        Say("Simulation mass set. Inertia will be calculated from the collision hull.");
    }

    private void SetJoints(bool all)
    {
        var model = RequiredPhysics();
        float min = Values(_angleMin, 1)[0], max = Values(_angleMax, 1)[0], cone = Values(_cone, 1)[0];
        if (min < -MathF.PI || max > MathF.PI || min > max || cone < 0 || cone > MathF.PI || _jointAxis.SelectedIndex == _jointRef.SelectedIndex)
            throw new ArgumentException("Choose distinct axes and ordered limits within [-pi, pi], with cone between 0 and pi.");
        if (!all && _physicsJoints.SelectedIndex < 0) throw new InvalidOperationException("Choose a joint.");
        ResetPhysics();
        foreach (var joint in model.Constraints.Where((joint, i) => all || i == _physicsJoints.SelectedIndex))
            _previewJoints[joint.Id] = new HavokConstraint { Id = joint.Id, Kind = joint.Kind, BodyA = joint.BodyA, BodyB = joint.BodyB,
                FrameA = joint.FrameA, FrameB = joint.FrameB, MinAngle = min, MaxAngle = max,
                TwistMinAngle = min, TwistMaxAngle = max, ConeMaxAngle = cone,
                LimitAxis = _jointAxis.SelectedIndex, TwistAxis = _jointAxis.SelectedIndex, TwistRefAxis = _jointRef.SelectedIndex,
                ConeTwistAxis = _jointAxis.SelectedIndex, ConeRefAxis = _jointAxis.SelectedIndex, HasPreviewLimits = true };
        Say("Preview joint settings applied. Source angular atoms are not imported or changed.");
    }

    private void EnsureSimulation()
    {
        if (_simulation != null) return;
        var source = RequiredPhysics();
        var preview = new HavokRagdollModel();
        preview.Shapes.AddRange(source.Shapes); preview.Bodies.AddRange(source.Bodies);
        foreach (var joint in source.Constraints)
        {
            if (!_previewJoints.TryGetValue(joint.Id, out var settings))
                throw new InvalidOperationException($"Set explicit preview settings for joint {joint.Id} first.");
            preview.Constraints.Add(settings);
        }
        _simulation = new RagdollSimulation(preview, _masses, Values(_gravity, 1)[0], Values(_ground, 1)[0]);
        _gravity.IsReadOnly = _ground.IsReadOnly = true;
        _physicsView.ShowSimulationFrames(_simulation.Frames);
        _physicsView.ShowMesh(_simulation.HullEdges);
        _physicsView.Frame();
    }

    private void StartPhysics()
    {
        EnsureSimulation();
        _physicsClock.Restart(); _lastTick = 0;
        _physicsTimer.Start();
        Say("Physics simulation running. Pause, step, push a body or reset to the source pose.");
    }

    private void PausePhysics() { _physicsTimer.Stop(); _physicsClock.Stop(); }
    private void AdvancePhysics(float seconds) { _simulation!.Advance(seconds); _physicsView.ShowSimulationFrames(_simulation.Frames); _physicsView.ShowMesh(_simulation.HullEdges); }
    private void StopPhysics() { PausePhysics(); _simulation?.Dispose(); _simulation = null; _gravity.IsReadOnly = _ground.IsReadOnly = false; }
    private void ResetPhysics() { StopPhysics(); _physicsView.ShowMesh(null); _physicsView.ShowSimulationFrames(null); if (_physics != null) _physicsView.SetBodies(_physics); }
}
