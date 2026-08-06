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
    /// `Choices` holds an enum's declared value names when the panel can safely offer them instead
    /// of a text box, and is empty otherwise. Empty is the common case and the safe one.
    public sealed record Field(string Name, string Value, Source From, string Raw, string Owner = "",
                               IReadOnlyList<string>? Choices = null)
    {
        public IReadOnlyList<string> Options => Choices ?? Array.Empty<string>();

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

            if (shown == null)
            {
                fields.Add(new Field(field.Name, text, Source.Fallback, text, field.Owner));
                continue;
            }

            string value = Shown(shown);
            fields.Add(new Field(field.Name, value, Source.Bytes, shown, field.Owner,
                                 Choices(field.Owner, field.Member, value, types)));
        }

        return fields;
    }

    /// The names a field's value is allowed to take, when offering them is safe.
    ///
    /// Three things have to hold, and each of them is a way this goes wrong:
    ///
    /// It has to be an enum and not a flags field. A flags value is a combination of bits, so it is
    /// usually not any one of the declared names, and a list of single values would quietly replace
    /// a combination with whichever one the user picked.
    ///
    /// The table has to declare values for it. Nothing is invented here: these are the names the
    /// game itself registers, read out of the class database, and a field whose enum is unknown
    /// stays a plain box rather than gaining a guess.
    ///
    /// The value in the file has to be one of them. A file holding a number no name covers is a file
    /// saying something the enum does not describe, and turning that box into a list would offer no
    /// way to keep what is already there. That case stays typeable.
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

    /// What a person should see, rather than what a comparison wants. An enum carries its number as
    /// well as its name so the two can be checked against each other; only the name belongs in a box
    /// somebody is about to type into. A null string is an empty box, which is how hkxpack writes it
    /// and how the editor has always accepted it back.
    public static string Shown(string rendered) =>
        rendered == "∅" ? "" : FieldRender.Plain(rendered);
}
