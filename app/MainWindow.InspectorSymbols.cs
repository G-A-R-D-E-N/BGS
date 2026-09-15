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
    private void AddSymbolSection(Inspector panel, string objectId, BehaviourGraphModel model)
    {
        var events = UsagesOf(true, objectId);
        var variables = UsagesOf(false, objectId);
        if (events.Count == 0 && variables.Count == 0) return;

        var eventNames = SymbolEditor.EventNames(model);
        var variableNames = SymbolEditor.VariableNames(model);
        panel.Add(Ux.SectionTitle("symbols this node touches"));

        foreach (var (use, names, kind) in
                 events.Select(u => (u, eventNames, "event"))
                       .Concat(variables.Select(u => (u, variableNames, "variable"))))
        {
            string name = use.Index >= 0 && use.Index < names.Count
                ? names[use.Index]
                : $"index {use.Index}, which this graph does not declare";

            var text = Ux.Label($"{use.Member} -> {kind} {name}");
            text.TextWrapping = TextWrapping.Wrap;
            if (use.Index >= names.Count) text.Foreground = Ux.BadBrush;
            panel.Add(text);
        }
    }

    private void AddBindingSection(Inspector panel, string objectId, BehaviourGraphModel model)
    {
        var owner = model.Get(objectId);
        if (owner == null) return;

        var names = BindingEditor.VariableNames(model);
        panel.Add(Ux.SectionTitle("variable bindings"));

        foreach (var b in BindingEditor.BindingsOf(model, owner))
        {
            string varName = b.VariableIndex >= 0 && b.VariableIndex < names.Count
                ? names[b.VariableIndex]
                : "index " + b.VariableIndex;

            string setId = b.SetId;
            int index = b.Index;
            var remove = Ux.Secondary("Remove");
            remove.Click += (_, _) => RemoveBinding(setId, index, objectId);

            var text = Ux.Label($"{b.MemberPath} <- {varName}");
            text.TextWrapping = TextWrapping.Wrap;

            var row = new DockPanel();
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            row.Children.Add(text);
            panel.Add(row);
        }

        var member = Ux.Field("member, e.g. userControlledTimeFraction");
        var variable = Ux.Field("variable name");
        var bind = Ux.Secondary("Bind");
        bind.HorizontalAlignment = HorizontalAlignment.Right;
        bind.Click += (_, _) => AddBinding(objectId, (member.Text ?? "").Trim(), (variable.Text ?? "").Trim());

        panel.Add(member);
        panel.Add(variable);
        panel.Add(bind);
    }

    private void EnsurePapyrus()
    {
        if (_papyrusScanned) return;

        string folder = Settings.Get("scripts");
        if (folder.Length == 0)
        {
            _papyrusScanned = true;
            return;
        }

        _ = ScanPapyrusFolder(folder, null);
    }

    private void BuildSymbols(BehaviourGraphModel model)
    {
        EnsurePapyrus();
        _symbols.Clear();

        var names = SymbolEditor.VariableNames(model);
        var types = SymbolEditor.VariableTypes(model);
        var values = SymbolEditor.VariableValues(model);
        var events = SymbolEditor.EventNames(model);

        var counts = SymbolEditor.Audit(model);
        _symbolAudit.Text = counts.ToString();
        _symbolAudit.Foreground = counts.VariablesConsistent && counts.EventsConsistent
            ? Ux.MetaBrush : Ux.BadBrush;

        var readers = UsersByIndex(events: false);
        var listeners = UsersByIndex(events: true);
        var variableSites = Usages(events: false);

        for (int i = 0; i < names.Count; i++)
        {
            var type = i < types.Count ? types[i] : SymbolEditor.VariableType.Int32;
            var row = Paint(_symbols.Add(null, type.ToString().ToLowerInvariant(), i.ToString(), names[i],
                                         i < values.Count ? SymbolEditor.DecodeValue(type, values[i]) : "",
                                         Users(readers, events: false, i)).Tag($"v:{i}"));

            var sites = variableSites.Where(u => u.Index == i)
                                     .GroupBy(u => (u.ObjectId, u.Owner, u.Member)).ToList();
            if (sites.Count == 0) continue;

            row.Collapse();
            foreach (var site in sites)
                AddUsageRow(row, "reads it", site.Key.ObjectId, site.Key.Owner, site.Key.Member,
                            site.Count(), "");
        }

        var usage = _xmlText.Length > 0 ? EventUsage.ByEvent(_xmlText)
                  : _bytes != null ? EventUsage.ByEvent(_bytes)
                  : new Dictionary<int, List<EventUsage.Line>>();

        for (int i = 0; i < events.Count; i++)
        {
            usage.TryGetValue(i, out var lines);
            string scripts = PapyrusEvents.Describe(_papyrus, events[i]);
            string summary = lines is { Count: > 0 } ? EventUsage.Summarise(lines) : Users(listeners, events: true, i);
            var row = Paint(_symbols.Add(null, "event", i.ToString(), events[i], "",
                                         scripts.Length > 0 ? $"{summary}; {scripts}" : summary))
                .Tag($"e:{i}");

            if (lines != null)
            {
                row.Collapse();
                foreach (var line in lines)
                {
                    string what = EventUsage.Describe(line.Role);
                    if (line.ObjectIds.Count == 0)
                    {
                        _symbols.Add(row, what, line.Count > 1 ? $"x{line.Count}" : "", line.Site, "", line.Note)
                                .Colour(0, line.Role == EventUsage.Role.Raised ? Ux.MetaBrush : Ux.MutedBrush)
                                .Colour(1, Ux.DisabledBrush).Colour(2, Ux.CodeBrush).Colour(4, Ux.MutedBrush);
                        continue;
                    }

                    foreach (string id in line.ObjectIds)
                        AddUsageRow(row, what, id, line.Site, "", 0, line.Note);
                }
            }

            if (scripts.Length == 0) continue;
            if (lines == null) row.Collapse();
            _symbols.Add(row, "papyrus", "", scripts, "", "scripts address events by name, not by index")
                    .Colour(0, Ux.MutedBrush).Colour(2, Ux.MetaBrush).Colour(4, Ux.DisabledBrush);
        }

        if (names.Count == 0 && events.Count == 0)
            _symbols.Add(null, "", "", "this graph declares no variables or events").Colour(2, Ux.DisabledBrush);
    }

    private void AddUsageRow(HkRow parent, string what, string objectId, string owner, string member,
                             int count, string note)
    {
        string where = member.Length > 0 ? $"{owner}.{member}" : owner;
        string named = objectId.Length > 0 ? $"#{objectId} {NameOf(objectId)}" : "";

        _symbols.Add(parent, what, count > 1 ? $"x{count}" : "", where, named, note)
                .Colour(0, Ux.MutedBrush).Colour(1, Ux.DisabledBrush).Colour(2, Ux.CodeBrush)
                .Colour(3, Ux.TitleBrush).Colour(4, Ux.MutedBrush)
                .Tag(objectId.Length > 0 ? "#" + objectId : "");
    }

    private string NameOf(string objectId)
    {
        foreach (var p in HkxTextEdit.ReadParams(_xmlText, objectId))
            if (p.Name == "name" && p.Value.Length > 0) return p.Value;
        return HkxTextEdit.ClassOf(_xmlText, objectId);
    }

    private static HkRow Paint(HkRow row) => row
        .Colour(0, Ux.MutedBrush).Colour(1, Ux.DisabledBrush).Colour(2, Ux.TitleBrush).Colour(3, Ux.CodeBrush)
        .Colour(4, row.Text(4).StartsWith("nothing") ? Ux.DisabledBrush : Ux.MetaBrush);

    private List<SymbolIndexFixup.Usage> Usages(bool events) =>
        _xmlText.Length > 0 ? SymbolIndexFixup.Usages(_xmlText, events)
        : _bytes != null ? SymbolIndexFixup.Usages(_bytes, events)
        : new List<SymbolIndexFixup.Usage>();

    private List<SymbolIndexFixup.Usage> UsagesOf(bool events, string objectId) =>
        _xmlText.Length > 0 ? SymbolIndexFixup.UsagesOf(_xmlText, events, objectId)
        : _bytes != null ? SymbolIndexFixup.UsagesOf(_bytes, events, objectId)
        : new List<SymbolIndexFixup.Usage>();

    private Dictionary<int, List<string>> UsersByIndex(bool events)
    {
        var map = new Dictionary<int, List<string>>();
        var references = _xmlText.Length > 0 ? SymbolIndexFixup.References(_xmlText, events)
                       : _bytes != null ? SymbolIndexFixup.References(_bytes, events)
                       : new List<SymbolIndexFixup.EventReference>();

        foreach (var reference in references)
        {
            if (!map.TryGetValue(reference.Index, out var list))
                map[reference.Index] = list = new List<string>();
            list.Add(reference.ToString());
        }
        return map;
    }

    private string Users(Dictionary<int, List<string>> map, bool events, int index)
    {
        if (_xmlText.Length == 0) return "";
        var users = map.TryGetValue(index, out var found) ? found : new List<string>();
        if (users.Count == 0)
            return events
                ? "nothing in this file listens for it; game code and scripts can still send it by name"
                : "nothing in this file reads it; game code can still set and read it by name";

        return string.Join(", ", users.GroupBy(u => u).OrderByDescending(g => g.Count())
            .Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key).Take(4));
    }
}
