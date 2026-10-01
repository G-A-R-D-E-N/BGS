using System.Text.Json;

namespace BehaviourStudio.App;

public partial class GraphView
{
    internal int AssistantItemCount => _nodes.Values.Count(node => IsDrawnAtCurrentDetail(node.Id));
    internal string[] AssistantItems(int offset, int limit = 200) => _nodes.Values
        .Where(node => IsDrawnAtCurrentDetail(node.Id)).Skip(offset).Take(limit).Select(node => JsonSerializer.Serialize(new
        {
            node.Id, node.Name, node.Class,
            Position = Coordinates(ToScreen(node.Bounds.TopLeft)),
            Width = node.Bounds.Width * _zoom, Height = node.Bounds.Height * _zoom,
            Input = Coordinates(ToScreen(node.InPort)),
            Outputs = node.Slots.Select((slot, index) => new { slot.Field, slot.Targets, Position = Coordinates(ToScreen(node.OutPort(index))) }),
            Chevron = Coordinates(ChevronRect(node).Center), Collapsed = IsCollapsed(node.Id),
        })).ToArray();

    internal string AssistantInteractionStamp => JsonSerializer.Serialize(new
    {
        _zoom, Pan = Coordinates(_pan), Layout = _layoutMode,
        Nodes = _nodes.Values.Select(node => new { node.Id, Position = Coordinates(node.Bounds.TopLeft) }),
        Collapsed = _collapsed.OrderBy(id => id).ToArray(),
    });

    private static double[] Coordinates(Avalonia.Point point) => new[] { point.X, point.Y };
}
