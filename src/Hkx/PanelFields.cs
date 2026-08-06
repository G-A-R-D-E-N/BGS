using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Hkx;

// What the properties panel shows for one object, read from the file's own bytes.
//
// The window used to take every value from hkxpack's XML. It reads them from the bytes now, and
// falls back to the XML for one field at a time rather than for the whole panel: the only fields the
// byte reader cannot attribute a class to are structs written inline, and there is no reason for a
// handful of those to decide where the other forty values come from.
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
    public sealed record Field(string Name, string Value, Source From, string Raw)
    {
        public override string ToString() => $"{Name} = {Value}" + (From == Source.Bytes ? "" : $"  ({From})");
    }

    /// The fields for one object, in the order the panel shows them.
    ///
    /// `xml` is what hkxpack says about the same object, and it decides which fields appear, because
    /// the class layout also holds fields the engine never writes out and offering those for editing
    /// would put values in a file that vanilla does not have. It is also where a fallback comes from.
    public static List<Field> For(PackfileObjects objects, PackfileObjects.Instance instance,
                                  IReadOnlyList<(string Name, string Value, bool Own)> xml,
                                  FieldRender.Reference reference,
                                  ISet<string>? edited = null)
    {
        var layout = HavokClasses.Shipped.Members(instance.ClassName)
                                 .ToDictionary(m => m.Name, m => m, StringComparer.Ordinal);

        var fields = new List<Field>(xml.Count);
        foreach (var (name, value, own) in xml)
        {
            if (edited != null && edited.Contains(name))
            {
                fields.Add(new Field(name, value, Source.Edited, value));
                continue;
            }

            // A field of an object written inline is not at any offset this object's class
            // describes, so reading its name off this object would find a different field that
            // happens to share the name. `hkbStateMachine` and the `hkbEvent` inside it both have
            // an `id`, and they are not the same `id`.
            string? shown = own && layout.TryGetValue(name, out var member)
                ? FieldRender.Render(objects, instance, member, reference, value)
                : null;

            fields.Add(shown == null
                ? new Field(name, value, Source.Fallback, value)
                : new Field(name, Shown(shown), Source.Bytes, shown));
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
