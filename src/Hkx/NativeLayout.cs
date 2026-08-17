using System;

namespace OpenCommonwealth.Services.Hkx;

// Every native mutation path (NativeSave, NativePaste, NativeAnimation) lays 64-bit
// structures: array headers carry the count at +8, pointer and struct-array strides are
// eight bytes, and pointer-sized integers are eight bytes. Reading a 4-byte file is
// supported, but mutating one through any of these writers would put 64-bit structures
// over 32-bit ones. This is the shared gate: NativeSave.Apply alone is not enough, because
// the GUI reaches NativePaste and the animation writers directly. Callers that need only to
// read can stay ungated; callers that write must require an 8-byte layout first.
internal static class NativeLayout
{
    public static void RequireWritable(PackfileImage image)
    {
        if (image.Layout.PointerSize != 8)
            throw new NotSupportedException(
                $"Native editing supports only the 8-byte packfile layout; this file uses a " +
                $"{image.Layout.PointerSize}-byte layout. Convert it to 8 bytes before editing.");
    }
}
