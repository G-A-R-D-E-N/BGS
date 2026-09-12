using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public static class TagfileClasses
{
    public sealed record Member(string Name, uint Offset, string TypeName)
    {
        public override string ToString() => $"+{Offset} {Name} {TypeName}";
    }

    public sealed record Class(
        string Name,
        string Parent,
        uint Size,
        uint Alignment,
        IReadOnlyList<Member> Members)
    {
        public Member? this[string member] =>
            Members.FirstOrDefault(m => string.Equals(m.Name, member, StringComparison.Ordinal));

        public override string ToString() =>
            $"{Name} size {Size}, {Members.Count} member(s)";
    }

    public static IReadOnlyDictionary<string, Class> Of(TagfileImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var byName = new Dictionary<string, Class>(StringComparer.Ordinal);

        foreach (var layout in image.Layouts)
        {
            if (layout.Size == 0) continue;

            string name = Named(image, layout.TypeIndex);
            if (name.Length == 0 || byName.ContainsKey(name)) continue;

            var members = layout.Members
                .Select(m => new Member(m.Name, m.Offset, Named(image, m.TypeIndex)))
                .ToList();

            byName[name] = new Class(
                name,
                Named(image, layout.ParentIndex),
                layout.Size,
                layout.Alignment,
                members);
        }

        return byName;
    }

    private static string Named(TagfileImage image, uint type)
    {
        if (type == 0 || type > image.Types.Count) return "";
        return image.Types[(int)type - 1].Name;
    }
}
