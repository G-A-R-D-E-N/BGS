
using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio;

public partial class StudioRoot : Control
{
    private const string SettingsPath = "user://behavior_graph_tool.cfg";
    private const int MaxTreeRows = 20000;

    private LineEdit _pathField = null!;
    private Label _summary = null!;
    private Label _status = null!;
    private Tree _tree = null!;
    private VBoxContainer _props = null!;
    private GraphCanvas _canvas = null!;
    private Tree _variables = null!;
    private Tree _chain = null!;
    private Button _saveButton = null!;

    private string _hkxPath = "";
    private string _xmlPath = "";
    private string _xmlText = "";
    private List<string> _objectIds = new();
    private readonly Dictionary<int, int> _offsetToIndex = new();
    private string _selectedId = "";
    private bool _dirty;
    private LineEdit _filter = null!;
    private HkxBehaviorParser.BehaviorNode? _root;
    private List<HkxBehaviorParser.BehaviorNode> _objects = new();

    public override void _Ready()
    {
        AnchorRight = 1;
        AnchorBottom = 1;
        DisplayServer.WindowSetTitle("Behaviour Graph Studio");

        var root = new PanelContainer { AnchorRight = 1, AnchorBottom = 1 };
        root.AddThemeStyleboxOverride("panel", Ux.Fill(Ux.Base, Ux.Border, 0, 0));
        AddChild(root);

        var pad = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            pad.AddThemeConstantOverride(side, Ux.Px(14));
        root.AddChild(pad);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", Ux.Px(10));
        pad.AddChild(column);

        column.AddChild(Ux.SectionTitle("Havok behaviour file"));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", Ux.Px(8));
        column.AddChild(row);

        _pathField = Ux.Field("Absolute path to a .hkx behaviour, character or project file");
        _pathField.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _pathField.Text = LoadSetting("last_path");
        _pathField.TextSubmitted += _ => Load();
        row.AddChild(_pathField);

        var openButton = Ux.PrimaryButton("Open");
        openButton.Pressed += Load;
        row.AddChild(openButton);

        _summary = Ux.StatusPill("No file loaded.");
        column.AddChild(_summary);

        var toolRow = new HBoxContainer();
        toolRow.AddThemeConstantOverride("separation", Ux.Px(8));
        column.AddChild(toolRow);

        _filter = Ux.Field("Filter objects by name, class or animation");
        _filter.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _filter.TextChanged += _ => RebuildTree();
        toolRow.AddChild(_filter);

        var expandAll = Ux.SecondaryButton("Expand all");
        expandAll.Pressed += () => SetAllCollapsed(false);
        toolRow.AddChild(expandAll);

        var collapseAll = Ux.SecondaryButton("Collapse all");
        collapseAll.Pressed += () => SetAllCollapsed(true);
        toolRow.AddChild(collapseAll);

        var tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        column.AddChild(tabs);

        var split = new HSplitContainer { Name = "Tree", SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        split.SplitOffset = Ux.Px(620);
        tabs.AddChild(split);

        _tree = new Tree
        {
            Columns = 4,
            ColumnTitlesVisible = true,
            HideRoot = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _tree.SetColumnTitle(0, "Node");
        _tree.SetColumnTitle(1, "Havok class");
        _tree.SetColumnTitle(2, "Animation");
        _tree.SetColumnTitle(3, "Offset");
        _tree.SetColumnExpandRatio(0, 4);
        _tree.SetColumnExpandRatio(1, 3);
        _tree.SetColumnExpandRatio(2, 4);
        _tree.SetColumnExpandRatio(3, 1);
        Ux.StyleGrid(_tree);
        _tree.ItemSelected += OnItemSelected;
        split.AddChild(_tree);

        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        right.CustomMinimumSize = new Vector2(Ux.Px(340), 0);
        right.AddThemeConstantOverride("separation", Ux.Px(8));
        split.AddChild(right);

        right.AddChild(Ux.SectionTitle("Properties"));

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
        };
        right.AddChild(scroll);

        _props = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _props.AddThemeConstantOverride("separation", Ux.Px(6));
        scroll.AddChild(_props);

        _canvas = new GraphCanvas { Name = "Graph" };
        _canvas.ObjectSelected += SelectObjectId;
        _canvas.FieldEdited += ApplyDirect;
        tabs.AddChild(_canvas);

        _variables = new Tree
        {
            Name = "Variables",
            Columns = 4,
            ColumnTitlesVisible = true,
            HideRoot = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _variables.SetColumnTitle(0, "Index");
        _variables.SetColumnTitle(1, "Variable");
        _variables.SetColumnTitle(2, "Initial value");
        _variables.SetColumnTitle(3, "Driven members");
        _variables.SetColumnExpandRatio(0, 1);
        _variables.SetColumnExpandRatio(1, 3);
        _variables.SetColumnExpandRatio(2, 2);
        _variables.SetColumnExpandRatio(3, 5);
        Ux.StyleGrid(_variables);
        tabs.AddChild(_variables);

        _chain = new Tree
        {
            Name = "Chain",
            Columns = 4,
            ColumnTitlesVisible = true,
            HideRoot = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _chain.SetColumnTitle(0, "Role");
        _chain.SetColumnTitle(1, "Declared in the file");
        _chain.SetColumnTitle(2, "On disk");
        _chain.SetColumnTitle(3, "Notes");
        _chain.SetColumnExpandRatio(0, 1);
        _chain.SetColumnExpandRatio(1, 4);
        _chain.SetColumnExpandRatio(2, 1);
        _chain.SetColumnExpandRatio(3, 3);
        Ux.StyleGrid(_chain);
        tabs.AddChild(_chain);

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", Ux.Px(8));
        column.AddChild(footer);

        _status = Ux.StatusPill("");
        _status.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(_status);

        _saveButton = Ux.PrimaryButton("Save to .hkx");
        _saveButton.Disabled = true;
        _saveButton.Pressed += Save;
        footer.AddChild(_saveButton);

        OpenFromCommandLine();
    }

    private void OpenFromCommandLine()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (!File.Exists(arg)) continue;
            OpenFile(arg);
            GD.Print(_summary.Text);
            GD.Print(_status.Text);

            var item = _variables.GetRoot()?.GetFirstChild();
            while (item != null)
            {
                GD.Print($"var {item.GetText(0),-3} {item.GetText(1),-24} value {item.GetText(2),-10} {item.GetText(3)}");
                item = item.GetNext();
            }

            RunDirectives();
            return;
        }
    }

    // Headless entry points, so an edit can be made and checked without clicking anything.
    //   chain                                  print the project chain
    //   bind=<objectId>,<member>,<variable>    add a binding, declaring the variable if it is new
    //   unbind=<setId>,<index>                 remove one
    //   save=<path>                            repack and write the result there
    private void RunDirectives()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "chain") PrintChain();
            else if (arg.StartsWith("bind=")) Directive(arg[5..], 3, p => { AddBinding(p[0], p[1], p[2]); });
            else if (arg.StartsWith("unbind=")) Directive(arg[7..], 2, p => { RemoveBinding(p[0], int.Parse(p[1]), p[0]); });
            else if (arg.StartsWith("save=")) SaveAs(arg[5..]);
        }
    }

    private void Directive(string payload, int expected, Action<string[]> run)
    {
        var parts = payload.Split(',');
        if (parts.Length != expected) { GD.Print($"directive needs {expected} comma separated values"); return; }
        run(parts);
        GD.Print("directive: " + _status.Text);
    }

    private void PrintChain()
    {
        var item = _chain.GetRoot()?.GetFirstChild();
        if (item == null) { GD.Print("chain: nothing resolved"); return; }
        while (item != null)
        {
            GD.Print($"chain {item.GetText(0),-11} {item.GetText(1),-44} {item.GetText(2),-8} {item.GetText(3)}");
            item = item.GetNext();
        }
    }

    private void SaveAs(string target)
    {
        try
        {
            File.WriteAllText(_xmlPath, _xmlText);
            string? java = HkxTextEdit.FindJava(LoadSetting("java"));
            string? jar = HkxTextEdit.FindHkxPack(LoadSetting("hkxpack"), ProjectSettings.GlobalizePath("res://"));
            string packed = HkxTextEdit.Repack(java!, jar!, _xmlPath);
            File.Copy(packed, target, true);
            GD.Print($"save: wrote {target} ({new FileInfo(target).Length} bytes)");
        }
        catch (Exception ex)
        {
            GD.Print("save failed: " + ex.Message.Split('\n')[0]);
        }
    }

    public void OpenFile(string path)
    {
        _pathField.Text = path;
        Load();
    }

    private void Load()
    {
        _tree.Clear();
        ClearProps();
        _offsetToIndex.Clear();
        _objectIds.Clear();
        _xmlText = "";
        _xmlPath = "";
        _selectedId = "";
        SetDirty(false);

        string path = _pathField.Text.Trim().Trim('"');
        if (string.IsNullOrEmpty(path)) { SetSummary("Enter the path to a .hkx file.", Ux.TextMuted); return; }
        if (!File.Exists(path)) { SetSummary($"Not found: {path}", Ux.TextMuted); return; }
        if (!HkxBinaryReader.IsFo4Hkx(path)) { SetSummary("Not a Fallout 4 hk_2014.1.0-r1 packfile.", Ux.TextMuted); return; }

        var root = HkxBehaviorParser.ParseBehavior(path);
        if (root == null) { SetSummary("Parsed as FO4 hkx, but no root object was resolved.", Ux.TextMuted); return; }

        _hkxPath = path;

        var objects = HkxBehaviorParser.LastObjects;
        for (int i = 0; i < objects.Count; i++)
            _offsetToIndex[objects[i].Offset] = i;

        _root = root;
        _objects = new List<HkxBehaviorParser.BehaviorNode>(objects);

        var classes = new HashSet<string>();
        int clips = 0;
        foreach (var o in _objects)
        {
            classes.Add(o.ClassName);
            if (!string.IsNullOrEmpty(o.AnimationName)) clips++;
        }

        SetSummary(
            $"{Path.GetFileName(path)}   root {root.ClassName}   " +
            $"{_objects.Count} objects   {classes.Count} classes   {clips} clip references",
            Ux.TextTitle);

        RebuildTree();
        SaveSetting("last_path", path);
        PrepareEditing();
    }

    private void RebuildTree()
    {
        _tree.Clear();
        ClearProps();
        if (_root == null) return;

        string needle = _filter.Text.Trim();
        if (needle.Length == 0)
        {
            var seen = new HashSet<int>();
            int rows = 0, clips = 0;
            var classes = new HashSet<string>();
            AddNode(_root, null, seen, classes, ref rows, ref clips);
            return;
        }

        var listRoot = _tree.CreateItem(null);
        listRoot.SetText(0, $"matches for \"{needle}\"");
        listRoot.SetCustomColor(0, Ux.TextTitle);

        int hits = 0;
        foreach (var o in _objects)
        {
            if (!Matches(o, needle)) continue;
            var item = _tree.CreateItem(listRoot);
            item.SetText(0, string.IsNullOrEmpty(o.NodeName) ? o.ClassName : o.NodeName);
            item.SetText(1, o.ClassName);
            item.SetText(2, o.AnimationName);
            item.SetText(3, "0x" + o.Offset.ToString("X"));
            item.SetMetadata(0, o.Offset);
            item.SetCustomColor(1, Ux.TextMeta);
            item.SetCustomColor(2, Ux.TextCode);
            item.SetCustomColor(3, Ux.TextDisabled);
            hits++;
            if (hits >= 2000) break;
        }
        listRoot.SetText(1, $"{hits} objects");
    }

    private static bool Matches(HkxBehaviorParser.BehaviorNode o, string needle)
    {
        return o.ClassName.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || o.NodeName.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || o.AnimationName.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private void SetAllCollapsed(bool collapsed)
    {
        var root = _tree.GetRoot();
        if (root == null) return;
        var stack = new Stack<TreeItem>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            if (item != root) item.Collapsed = collapsed;
            var child = item.GetFirstChild();
            while (child != null) { stack.Push(child); child = child.GetNext(); }
        }
    }

    private void PrepareEditing()
    {
        string projectRoot = ProjectSettings.GlobalizePath("res://");
        string? java = HkxTextEdit.FindJava(LoadSetting("java"));
        string? jar = HkxTextEdit.FindHkxPack(LoadSetting("hkxpack"), projectRoot);

        if (java == null || jar == null)
        {
            SetStatus("Read-only: need a Java runtime and hkxpack-cli.jar to edit.", Ux.TextMuted);
            _canvas.ShowMessage("Graph needs a Java runtime and hkxpack-cli.jar to read this file's links.");
            return;
        }

        try
        {
            string work = Path.Combine(Path.GetTempPath(), "oc_behaviour_edit",
                                       Path.GetFileNameWithoutExtension(_hkxPath));
            if (Directory.Exists(work)) Directory.Delete(work, true);

            _xmlPath = HkxTextEdit.Unpack(java, jar, _hkxPath, work);
            _xmlText = File.ReadAllText(_xmlPath);
            _objectIds = HkxTextEdit.ObjectIds(_xmlText);

            if (_objectIds.Count != HkxBehaviorParser.LastObjects.Count)
            {
                _xmlText = "";
                SetStatus($"Read-only: object counts disagree " +
                          $"({HkxBehaviorParser.LastObjects.Count} binary vs {_objectIds.Count} xml).",
                          Ux.TextMuted);
                return;
            }

            SetStatus($"Editable. {_objectIds.Count} objects mapped.", Ux.TextMeta);
            var model = BehaviourGraphModel.Parse(_xmlText);
            _canvas.Build(model);
            BuildVariables(model);
            BuildChain();
        }
        catch (Exception ex)
        {
            _xmlText = "";
            SetStatus("Read-only: " + ex.Message.Split('\n')[0], Ux.TextMuted);
            _canvas.ShowMessage("Graph needs the text form of the file: " + ex.Message.Split('\n')[0]);
        }
    }

    private void OnItemSelected()
    {
        ClearProps();
        _selectedId = "";

        var item = _tree.GetSelected();
        if (item == null || string.IsNullOrEmpty(_xmlText)) return;

        var meta = item.GetMetadata(0);
        if (meta.VariantType != Variant.Type.Int) return;

        if (!_offsetToIndex.TryGetValue((int)meta, out int index)) return;
        if (index < 0 || index >= _objectIds.Count) return;

        ShowProps(_objectIds[index], item.GetText(1));
    }

    private void SelectObjectId(string objectId)
    {
        ClearProps();
        _selectedId = "";
        if (string.IsNullOrEmpty(_xmlText)) return;
        ShowProps(objectId, "");
    }

    private void ShowProps(string objectId, string className)
    {
        _selectedId = objectId;
        var parameters = HkxTextEdit.ReadParams(_xmlText, _selectedId);

        var header = Ux.FieldLabel($"#{_selectedId}   {className}   {parameters.Count} editable fields");
        _props.AddChild(header);

        foreach (var p in parameters)
        {
            var box = new HBoxContainer();
            box.AddThemeConstantOverride("separation", Ux.Px(6));
            _props.AddChild(box);

            var label = Ux.FieldLabel(p.Name);
            label.CustomMinimumSize = new Vector2(Ux.Px(150), 0);
            box.AddChild(label);

            var field = Ux.Field();
            field.CustomMinimumSize = new Vector2(Ux.Px(150), 0);
            field.Text = p.Value;
            field.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

            string paramName = p.Name;
            string original = p.Value;
            string owner = _selectedId;
            field.TextSubmitted += _ => Apply(owner, paramName, field, original);
            field.FocusExited += () => Apply(owner, paramName, field, original);
            box.AddChild(field);
        }

        AddBindingSection(objectId);
    }

    private void BuildVariables(BehaviourGraphModel model)
    {
        _variables.Clear();
        var root = _variables.CreateItem();

        var strings = model.Objects.FirstOrDefault(o => o.Class == "hkbBehaviorGraphStringData");
        var names = strings?.Strings("variableNames") ?? new List<string>();

        var values = new List<string>();
        var valueSet = model.Objects.FirstOrDefault(o => o.Class == "hkbVariableValueSet");
        if (valueSet != null && valueSet.StructLists.TryGetValue("wordVariableValues", out var wordRows))
            foreach (var row in wordRows)
                values.Add(row.TryGetValue("value", out var v) ? v : "");

        var driven = new Dictionary<int, List<string>>();
        foreach (var obj in model.Objects)
        {
            if (obj.Class != "hkbVariableBindingSet") continue;
            if (!obj.StructLists.TryGetValue("bindings", out var rows)) continue;
            foreach (var row in rows)
            {
                if (!row.TryGetValue("variableIndex", out var raw) || !int.TryParse(raw, out int index)) continue;
                row.TryGetValue("memberPath", out var path);
                if (!driven.TryGetValue(index, out var list)) driven[index] = list = new List<string>();
                if (path != null && !list.Contains(path)) list.Add(path);
            }
        }

        for (int i = 0; i < names.Count; i++)
        {
            var item = _variables.CreateItem(root);
            item.SetText(0, i.ToString());
            item.SetText(1, names[i]);
            item.SetText(2, i < values.Count ? values[i] : "");
            item.SetText(3, driven.TryGetValue(i, out var paths) ? string.Join(", ", paths) : "no variable binding in this file");
            item.SetCustomColor(0, Ux.TextDisabled);
            item.SetCustomColor(1, Ux.TextTitle);
            item.SetCustomColor(2, Ux.TextCode);
            item.SetCustomColor(3, driven.ContainsKey(i) ? Ux.TextMeta : Ux.TextDisabled);
        }

        if (names.Count == 0)
        {
            var item = _variables.CreateItem(root);
            item.SetText(1, "this graph declares no variables");
            item.SetCustomColor(1, Ux.TextDisabled);
        }
    }

    private void BuildChain()
    {
        _chain.Clear();
        var root = _chain.CreateItem();

        string? java = HkxTextEdit.FindJava(LoadSetting("java"));
        string? jar = HkxTextEdit.FindHkxPack(LoadSetting("hkxpack"), ProjectSettings.GlobalizePath("res://"));
        if (java == null || jar == null)
        {
            var warn = _chain.CreateItem(root);
            warn.SetText(0, "unavailable");
            warn.SetText(1, "the chain needs a Java runtime and hkxpack-cli.jar");
            warn.SetCustomColor(1, Ux.TextMuted);
            return;
        }

        var chain = ProjectChain.Resolve(_hkxPath, java, jar);

        foreach (var link in chain.Links)
        {
            var item = _chain.CreateItem(root);
            item.SetText(0, link.Role);
            item.SetText(1, link.Declared);
            item.SetText(2, link.Exists ? "found" : "MISSING");
            item.SetText(3, link.Note);
            item.SetCustomColor(0, Ux.TextMuted);
            item.SetCustomColor(1, Ux.TextTitle);
            item.SetCustomColor(2, link.Exists ? Ux.TextMeta : Color.FromHtml("FF5555"));
            item.SetCustomColor(3, Ux.TextMeta);
        }

        AddChainGroup(root, "animations", $"{chain.Animations.Count} declared by the character", chain.Animations, Ux.TextCode);
        AddChainGroup(root, "bones", $"{chain.Bones.Count} in the skeleton", chain.Bones, Ux.TextMeta);

        foreach (string problem in chain.Problems)
        {
            var item = _chain.CreateItem(root);
            item.SetText(0, "problem");
            item.SetText(1, problem);
            item.SetCustomColor(0, Color.FromHtml("FF5555"));
            item.SetCustomColor(1, Color.FromHtml("FF5555"));
        }
    }

    private void AddChainGroup(TreeItem root, string role, string summary, List<string> values, Color colour)
    {
        if (values.Count == 0) return;
        var head = _chain.CreateItem(root);
        head.SetText(0, role);
        head.SetText(1, summary);
        head.SetCustomColor(0, Ux.TextMuted);
        head.SetCustomColor(1, Ux.TextTitle);
        head.Collapsed = true;
        foreach (string v in values)
        {
            var item = _chain.CreateItem(head);
            item.SetText(1, v);
            item.SetCustomColor(1, colour);
        }
    }

    private void AddBindingSection(string objectId)
    {
        if (string.IsNullOrEmpty(_xmlText)) return;

        var model = BehaviourGraphModel.Parse(_xmlText);
        var owner = model.Get(objectId);
        if (owner == null) return;

        var names = BindingEditor.VariableNames(model);
        _props.AddChild(Ux.SectionTitle("variable bindings"));

        foreach (var b in BindingEditor.BindingsOf(model, owner))
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", Ux.Px(6));
            _props.AddChild(row);

            string varName = b.VariableIndex >= 0 && b.VariableIndex < names.Count
                ? names[b.VariableIndex]
                : "index " + b.VariableIndex;

            var label = Ux.FieldLabel($"{b.MemberPath} <- {varName}");
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(label);

            string setId = b.SetId;
            int index = b.Index;
            var remove = Ux.SecondaryButton("Remove");
            remove.Pressed += () => RemoveBinding(setId, index, objectId);
            row.AddChild(remove);
        }

        var addRow = new HBoxContainer();
        addRow.AddThemeConstantOverride("separation", Ux.Px(6));
        _props.AddChild(addRow);

        var member = Ux.Field("member, e.g. userControlledTimeFraction");
        addRow.AddChild(member);
        var variable = Ux.Field("variable name");
        addRow.AddChild(variable);

        var bind = Ux.SecondaryButton("Bind");
        bind.Pressed += () => AddBinding(objectId, member.Text.Trim(), variable.Text.Trim());
        addRow.AddChild(bind);
    }

    private void AddBinding(string objectId, string memberPath, string variableName)
    {
        try
        {
            var names = BindingEditor.VariableNames(BehaviourGraphModel.Parse(_xmlText));
            int index = names.FindIndex(n => n.Equals(variableName, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
            {
                _xmlText = BindingEditor.AddVariable(_xmlText, variableName, out index);
                SetStatus($"declared variable '{variableName}' at index {index}", Ux.TextCode);
            }

            _xmlText = BindingEditor.AddBinding(_xmlText, objectId, memberPath, index);
            SetDirty(true);
            SetStatus($"#{objectId}.{memberPath} driven by {variableName}   (unsaved)", Ux.TextCode);
            RefreshAfterEdit(objectId);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message.Split('\n')[0], Ux.TextMuted);
        }
    }

    private void RemoveBinding(string setId, int index, string objectId)
    {
        try
        {
            _xmlText = BindingEditor.RemoveBinding(_xmlText, setId, index);
            SetDirty(true);
            SetStatus($"removed binding {index} from #{setId}   (unsaved)", Ux.TextCode);
            RefreshAfterEdit(objectId);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message.Split('\n')[0], Ux.TextMuted);
        }
    }

    private void RefreshAfterEdit(string objectId)
    {
        var model = BehaviourGraphModel.Parse(_xmlText);
        _canvas.Build(model);
        BuildVariables(model);
        ClearProps();
        ShowProps(objectId, HkxTextEdit.ClassOf(_xmlText, objectId));
    }

    private void ApplyDirect(string objectId, string paramName, string value)
    {
        if (string.IsNullOrEmpty(_xmlText))
        {
            SetStatus("Read-only: no text form loaded, so edits cannot be saved.", Ux.TextMuted);
            return;
        }

        try
        {
            _xmlText = HkxTextEdit.SetParam(_xmlText, objectId, paramName, value);
            SetDirty(true);
            SetStatus($"#{objectId}.{paramName} = {value}   (unsaved)", Ux.TextCode);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, Ux.TextMuted);
        }
    }

    private void Apply(string objectId, string paramName, LineEdit field, string original)
    {
        if (field.Text == original || string.IsNullOrEmpty(_xmlText)) return;

        try
        {
            _xmlText = HkxTextEdit.SetParam(_xmlText, objectId, paramName, field.Text);
            SetDirty(true);
            SetStatus($"#{objectId}.{paramName} = {field.Text}   (unsaved)", Ux.TextCode);
        }
        catch (Exception ex)
        {
            field.Text = original;
            SetStatus(ex.Message, Ux.TextMuted);
        }
    }

    private void Save()
    {
        if (!_dirty || string.IsNullOrEmpty(_xmlText)) return;

        string projectRoot = ProjectSettings.GlobalizePath("res://");
        string? java = HkxTextEdit.FindJava(LoadSetting("java"));
        string? jar = HkxTextEdit.FindHkxPack(LoadSetting("hkxpack"), projectRoot);
        if (java == null || jar == null) { SetStatus("Cannot save: java or hkxpack missing.", Ux.TextMuted); return; }

        try
        {
            File.WriteAllText(_xmlPath, _xmlText);
            string packed = HkxTextEdit.Repack(java, jar, _xmlPath);

            string backup = _hkxPath + ".bak";
            if (!File.Exists(backup)) File.Copy(_hkxPath, backup);
            File.Copy(packed, _hkxPath, true);

            SetDirty(false);
            SetStatus($"Saved. Original kept as {Path.GetFileName(backup)}.", Ux.TextMeta);
            Load();
        }
        catch (Exception ex)
        {
            SetStatus("Save failed: " + ex.Message.Split('\n')[0], Ux.TextMuted);
        }
    }

    private void AddNode(HkxBehaviorParser.BehaviorNode node, TreeItem? parent,
                         HashSet<int> seen, HashSet<string> classes, ref int rows, ref int clips)
    {
        if (rows >= MaxTreeRows) return;

        bool repeat = !seen.Add(node.Offset);
        var item = _tree.CreateItem(parent);
        rows++;
        classes.Add(node.ClassName);
        if (!string.IsNullOrEmpty(node.AnimationName)) clips++;

        string label = string.IsNullOrEmpty(node.NodeName) ? node.ClassName : node.NodeName;
        item.SetText(0, repeat ? label + "  (shown above)" : label);
        item.SetText(1, node.ClassName);
        item.SetText(2, node.AnimationName);
        item.SetText(3, "0x" + node.Offset.ToString("X"));
        item.SetMetadata(0, node.Offset);

        item.SetCustomColor(1, Ux.TextMeta);
        item.SetCustomColor(2, Ux.TextCode);
        item.SetCustomColor(3, Ux.TextDisabled);
        if (repeat) item.SetCustomColor(0, Ux.TextDisabled);
        if (parent == null) item.SetCustomColor(0, Ux.TextTitle);
        item.Collapsed = parent != null && parent.GetParent() != null;

        if (repeat) return;

        foreach (var child in node.Children)
            AddNode(child, item, seen, classes, ref rows, ref clips);
    }

    private void ClearProps()
    {
        // QueueFree alone is deferred, so successive selections would stack their rows.
        foreach (var child in _props.GetChildren())
        {
            _props.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        if (_saveButton != null) _saveButton.Disabled = !dirty;
    }

    private void SetSummary(string text, Color color)
    {
        _summary.Text = text;
        _summary.AddThemeColorOverride("font_color", color);
    }

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", color);
    }

    private static string LoadSetting(string key)
    {
        var cfg = new ConfigFile();
        return cfg.Load(SettingsPath) == Error.Ok ? (string)cfg.GetValue("behaviour", key, "") : "";
    }

    private static void SaveSetting(string key, string value)
    {
        var cfg = new ConfigFile();
        cfg.Load(SettingsPath);
        cfg.SetValue("behaviour", key, value);
        cfg.Save(SettingsPath);
    }
}

