using System.Text.Json;

namespace BehaviourStudio.App;

public partial class SkeletonView
{
    internal string AssistantCameraStamp => JsonSerializer.Serialize(new { _yaw, _pitch, _zoom, Pan = new[] { _pan.X, _pan.Y }, Bounds.Width, Bounds.Height });
    internal int AssistantItemCount => (_pose?.Bones.Count ?? 0) + (ShowBodies ? _bodies?.Bodies.Count ?? 0 : 0);
    internal string[] AssistantItems(int offset, int limit = 200)
    {
        var bones = _pose?.Bones.Select(bone =>
        {
            var at = Project(bone.Position);
            return JsonSerializer.Serialize(new { Kind = "bone", bone.Index, bone.Name, bone.Parent, Position = new[] { at.X, at.Y } });
        }) ?? Enumerable.Empty<string>();
        var bodies = ShowBodies ? _bodies?.Bodies.Select(body =>
        {
            var at = Project(BodyFrame(_bodies, body).Position);
            return JsonSerializer.Serialize(new { Kind = "body", body.Id, body.Name, Position = new[] { at.X, at.Y } });
        }) ?? Enumerable.Empty<string>() : Enumerable.Empty<string>();
        return bones.Concat(bodies).Skip(offset).Take(limit).ToArray();
    }
    internal string AssistantInteractionStamp => AssistantCameraStamp + string.Join("\n", AssistantItems(0, int.MaxValue));
}
