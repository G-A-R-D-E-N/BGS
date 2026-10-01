using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCommonwealth.Services.Hkx;

namespace OpenCommonwealth.Services.Nif;

public static class SkinSave
{
    public static byte[] Apply(byte[] source, int shapeIndex, NifShape edited)
    {
        ArgumentNullException.ThrowIfNull(edited);
        var nif = NifFile.Parse(source, "skin source");
        if (nif.BsVersion != 130)
            throw new NotSupportedException("Skin persistence supports the Fallout 4 BSVersion 130 layout only.");
        var shapes = NifGeometry.Shapes(nif);
        if (shapeIndex < 0 || shapeIndex >= shapes.Count)
            throw new ArgumentOutOfRangeException(nameof(shapeIndex));
        NifShape original = shapes[shapeIndex];
        if (!SkinEdit.Editable(original) || original.SkinSource is not { } location)
            throw new NotSupportedException("This shape has no supported vertex skin payload.");
        if (original.Name != edited.Name || original.NodeTranslation != edited.NodeTranslation ||
            original.NodeScale != edited.NodeScale || !original.Vertices.SequenceEqual(edited.Vertices) ||
            !original.Indices.SequenceEqual(edited.Indices) || !original.BoneNames.SequenceEqual(edited.BoneNames) ||
            !original.SkinToBone.SequenceEqual(edited.SkinToBone))
            throw new NotSupportedException("Skin save accepts weight and influence edits only.");
        var errors = SkinValidator.Check(edited).Where(f => f.Level == SkeletonValidator.Level.Error).ToArray();
        if (errors.Length > 0) throw new InvalidDataException(errors[0].ToString());

        byte[] result = (byte[])source.Clone();
        int blockEnd = checked(nif.BlockStart[location.Block] + nif.BlockSize[location.Block]);
        for (int vertex = 0; vertex < edited.Vertices.Count; vertex++)
        {
            int at = checked(location.Start + vertex * location.Stride);
            if (at < nif.BlockStart[location.Block] ||
                at + location.Weights + SkinEdit.Slots * sizeof(ushort) > blockEnd ||
                at + location.Indices + SkinEdit.Slots > blockEnd)
                throw new InvalidDataException("Skin payload crosses its owning block.");
            for (int slot = 0; slot < SkinEdit.Slots; slot++)
            {
                int index = vertex * SkinEdit.Slots + slot;
                int bone = edited.BoneIndices[index];
                Half weight = (Half)edited.BoneWeights[index];
                if (bone < 0 || bone > byte.MaxValue || !Half.IsFinite(weight))
                    throw new InvalidDataException("An influence cannot be represented by this vertex layout.");
                BitConverter.TryWriteBytes(result.AsSpan(at + location.Weights + slot * sizeof(ushort)),
                    BitConverter.HalfToUInt16Bits(weight));
                result[at + location.Indices + slot] = (byte)bone;
            }
        }

        NifShape reopened = NifGeometry.Shapes(NifFile.Parse(result, "rebuilt skin"))[shapeIndex];
        for (int index = 0; index < edited.BoneWeights.Count; index++)
            if (reopened.BoneIndices[index] != edited.BoneIndices[index] ||
                reopened.BoneWeights[index] != (float)(Half)edited.BoneWeights[index])
                throw new InvalidDataException("The rebuilt skin did not retain the intended influences.");
        if (SkinValidator.Check(reopened).Any(f => f.Level == SkeletonValidator.Level.Error))
            throw new InvalidDataException("Quantized skin weights failed validation.");
        return result;
    }
}

public sealed class SkinDocument
{
    private byte[] _saved;
    private byte[] _current;
    private readonly Stack<byte[]> _undo = new();
    private readonly Stack<byte[]> _redo = new();
    private DocumentSourceStamp _stamp;

    public SkinDocument(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        _saved = InputFilePolicy.ReadNif(Path);
        _current = _saved;
        _stamp = DocumentSourceStamp.Capture(_saved);
        _ = Shapes;
    }

    public string Path { get; }
    public bool Dirty => !_current.AsSpan().SequenceEqual(_saved);
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public IReadOnlyList<NifShape> Shapes => NifGeometry.Shapes(NifFile.Parse(_current, System.IO.Path.GetFileName(Path)));

    public void Edit(int shapeIndex, Action<NifShape> edit)
    {
        var shapes = Shapes;
        if (shapeIndex < 0 || shapeIndex >= shapes.Count) throw new ArgumentOutOfRangeException(nameof(shapeIndex));
        NifShape shape = shapes[shapeIndex];
        edit(shape);
        byte[] changed = SkinSave.Apply(_current, shapeIndex, shape);
        if (changed.AsSpan().SequenceEqual(_current)) return;
        _undo.Push(_current);
        _redo.Clear();
        _current = changed;
    }

    public void Undo()
    {
        if (!_undo.TryPop(out var previous)) return;
        _redo.Push(_current);
        _current = previous;
    }

    public void Redo()
    {
        if (!_redo.TryPop(out var next)) return;
        _undo.Push(_current);
        _current = next;
    }

    public void Save()
    {
        if (!Dirty) return;
        FileSafety.ReplaceChecked(Path, _current, _stamp);
        _saved = _current;
        _stamp = DocumentSourceStamp.Capture(_saved);
    }
}
