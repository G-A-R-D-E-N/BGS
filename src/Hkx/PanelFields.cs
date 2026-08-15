using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

public static class PanelFields
{
    public enum Source
    {

        Bytes,

        Fallback,

        Edited,
    }

    public sealed record Field(string Name, string Value, Source From, string Raw, string Owner = "",
                               IReadOnlyList<string>? Choices = null, string Path = "", string Group = "")
    {
        public IReadOnlyList<string> Options => Choices ?? Array.Empty<string>();

        public string Address => Path.Length > 0 ? Path : Name;

        public override string ToString() => $"{Name} = {Value}" + (From == Source.Bytes ? "" : $"  ({From})");
    }

    public static List<Field> For(PackfileObjects objects, PackfileObjects.Instance instance,
                                  IReadOnlyList<(string Name, string Value)> xml,
                                  FieldRender.Reference reference,
                                  ISet<string>? edited = null,
                                  HavokClassTypes? types = null)
    {
        var found = ClassFields.Of(objects, instance, types);

        if (found == null || found.Count != xml.Count)
            return xml.Select(p => new Field(p.Name, p.Value, Source.Fallback, p.Value)).ToList();

        var fields = new List<Field>(found.Count);
        for (int i = 0; i < found.Count; i++)
        {
            var field = found[i];
            string text = xml[i].Value;

            if (edited != null && edited.Contains(field.Path))
            {
                fields.Add(new Field(field.Name, text, Source.Edited, text, field.Owner,
                                     null, field.Path, field.Group));
                continue;
            }

            string? shown = FieldRender.Render(objects, field.At, field.Owner, field.Member,
                                               reference, text, field.Element, types);

            if (shown == null)
            {
                fields.Add(new Field(field.Name, text, Source.Fallback, text, field.Owner,
                                     null, field.Path, field.Group));
                continue;
            }

            string value = Shown(shown);
            fields.Add(new Field(field.Name, value, Source.Bytes, shown, field.Owner,
                                 Choices(field.Owner, field.Member, value, types),
                                 field.Path, field.Group));
        }

        return fields;
    }

    private static IReadOnlyList<string>? Choices(string owner, HavokClassTypes.Member member,
                                                  string value, HavokClassTypes? types)
    {
        if (member.VType != "TYPE_ENUM" || member.EType == null) return null;

        types ??= HavokClassTypes.Shipped;
        var declared = types.Enum(owner, member.EType);
        if (declared == null || declared.Count == 0) return null;

        var names = declared.OrderBy(v => v.Value).Select(v => v.Key).ToList();
        return names.Contains(value, StringComparer.Ordinal) ? names : null;
    }

    public static string Shown(string rendered) =>
        rendered == "∅" ? "" : FieldRender.Plain(rendered);
}
