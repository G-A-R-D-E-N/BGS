using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// What the properties panel shows for one object, read from the file's own bytes.
//
// Both halves of that used to come from hkxpack. The values moved to the bytes first; the list of
// names stayed behind, because a class dump read out of the game says nothing about which of a
// class's fields are ever written to a file, nor what class a struct written inline is. Those are
// in the class table now, so the list comes from the table and the file, and hkxpack is left as a
// fallback for one field at a time.
//
// The XML is still passed in and is still worth having: it is what a field falls back to when the
// bytes cannot answer, and its length is the check that the table and the file agree about what is
// in front of us. If the two lists are not the same length then one of them is wrong about this
// file, and the honest thing is to show hkxpack's and say nothing was read.
//
// This is shared with `symrm panel` on purpose. Checking the reader against hkxpack says nothing
// about the window unless the window is going through the same code, so it is this code that both
// of them call.
public static class PanelFields
{
    public enum Source
    {
        /// Read from the file's own bytes.
        Bytes,

        /// The byte reader could not render it, so hkxpack's text is shown instead.
        Fallback,

        /// Changed in this session and not yet saved, so the bytes on disk are out of date and the
        /// edit is what belongs on screen.
        Edited,
    }

    /// `Value` is what goes in the box. `Raw` is what the reader produced before it was made fit to
    /// read: for an enum that is the number as well as the name, which is the only form a
    /// comparison can meet hkxpack in, because hkxpack prints one or the other as it pleases.
    public sealed record Field(string Name, string Value, Source From, string Raw, string Owner = "")
    {
        public override string ToString() => $"{Name} = {Value}" + (From == Source.Bytes ? "" : $"  ({From})");
    }

    /// The fields for one object, in the order the panel shows them.
    public static List<Field> For(PackfileObjects objects, PackfileObjects.Instance instance,
                                  IReadOnlyList<(string Name, string Value)> xml,
                                  FieldRender.Reference reference,
                                  ISet<string>? edited = null,
                                  HavokClassTypes? types = null)
    {
        var found = ClassFields.Of(objects, instance, types);

        // Degrading the same way the load path does. A class the table has no entry for, a struct
        // it cannot name, or a list that does not line up with hkxpack's, and the panel goes back
        // to being what it was before any of this: hkxpack's names and hkxpack's values.
        if (found == null || found.Count != xml.Count)
            return xml.Select(p => new Field(p.Name, p.Value, Source.Fallback, p.Value)).ToList();

        var fields = new List<Field>(found.Count);
        for (int i = 0; i < found.Count; i++)
        {
            var field = found[i];
            string text = xml[i].Value;

            if (edited != null && edited.Contains(field.Name))
            {
                fields.Add(new Field(field.Name, text, Source.Edited, text, field.Owner));
                continue;
            }

            string? shown = FieldRender.Render(objects, field.At, field.Owner, field.Member,
                                               reference, text, field.Element, types);

            fields.Add(shown == null
                ? new Field(field.Name, text, Source.Fallback, text, field.Owner)
                : new Field(field.Name, Shown(shown), Source.Bytes, shown, field.Owner));
        }

        return fields;
    }

    /// What a person should see, rather than what a comparison wants. An enum carries its number as
    /// well as its name so the two can be checked against each other; only the name belongs in a box
    /// somebody is about to type into. A null string is an empty box, which is how hkxpack writes it
    /// and how the editor has always accepted it back.
    public static string Shown(string rendered) =>
        rendered == "∅" ? "" : FieldRender.Plain(rendered);
}
