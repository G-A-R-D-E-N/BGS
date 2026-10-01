using System;
using System.IO;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public static partial class NativeSave
{
    private static void RequireUnaliasedArrays(PackfileImage image, PackfileObjects objects, Plan plan)
    {
        var data = image.Section("__data__") ?? throw new InvalidDataException("No data section.");
        int section = image.Sections.IndexOf(data);
        foreach (var group in plan.Changes.Where(c => c.Grow || c.InElement || c.Array && c.Text)
                     .GroupBy(c => (c.Id, c.Field)))
        {
            Change change = group.First();
            int index = change.Id - NativeGraphModel.FirstId;
            if (index >= objects.Instances.Count) continue;
            var instance = Resolve(objects, change);
            var member = HavokClassTypes.Shipped.Members(change.ClassName).FirstOrDefault(m => m.Name == change.Field);
            if (member?.VType != "TYPE_ARRAY") continue;
            int stride = member.VSub == "TYPE_STRUCT" && member.CType != null
                ? HavokClassTypes.Shipped[member.CType]?.Size ?? 0
                : image.Layout.PointerSize;
            if (stride <= 0) throw new InvalidDataException("Array ownership has no known element size.");
            int header = objects.FieldAt(instance, change.Field) ?? throw new InvalidDataException("Array ownership has no known header.");
            var array = objects.ArrayAt(header, stride);
            if (array == null || array.Count == 0) continue;
            long end = (long)array.At + (long)array.Count * stride;
            bool Inside(int at) => at >= array.At && at < end;
            if (data.Locals().Any(f => f.Source != header && Inside(f.Destination)) ||
                image.Sections.Any(owner => owner.Globals().Any(f =>
                    (owner != data || f.Source != header) && f.Section == section && Inside(f.Destination))) ||
                data.Virtuals().Any(f => Inside(f.Source)))
                throw new InvalidDataException($"{change.ClassName}.{change.Field} has shared array storage; the edit was refused to preserve its other owners.");
        }
    }
}
