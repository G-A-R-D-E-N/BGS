using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenCommonwealth.Services.Nif;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class NifEmbeddedPhysicsProofTests
{
    private static readonly byte[] Payload = { 0x57, 0xE0, 0xE0, 0x57, 1, 2, 3, 4, 5, 6, 7, 8 };

    [Fact]
    public void EmbeddedRagdollPayloadCanBeReadWithoutUnpackingTheNif()
    {
        var reader = Reader();
        var nif = NifFile.Parse(PhysicsNif(Payload.Length), "ragdoll.nif");
        var result = Assert.IsAssignableFrom<IEnumerable>(reader.Invoke(null, new object[] { nif }));
        var embedded = result.Cast<object>().Single();

        Assert.Equal("bhkRagdollSystem", Property<string>(embedded, "BlockType"));
        Assert.Equal(Payload, Property<byte[]>(embedded, "Bytes"));
    }

    [Fact]
    public void EmbeddedRagdollPayloadCannotRunPastItsNifBlock()
    {
        var reader = Reader();
        var nif = NifFile.Parse(PhysicsNif(9999), "ragdoll.nif");

        var thrown = Assert.Throws<TargetInvocationException>(() =>
            reader.Invoke(null, new object[] { nif }));

        var invalid = Assert.IsType<InvalidDataException>(thrown.InnerException);
        Assert.Contains("does not fit", invalid.Message);
    }

    private static MethodInfo Reader()
    {
        var type = typeof(NifFile).Assembly.GetType("OpenCommonwealth.Services.Nif.NifPhysics");
        Assert.NotNull(type);
        var method = type!.GetMethod("Read", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        return method!;
    }

    private static T Property<T>(object instance, string name)
    {
        var property = instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        return Assert.IsType<T>(property!.GetValue(instance));
    }

    private static byte[] PhysicsNif(int claimedBytes)
    {
        using var block = new MemoryStream();
        using (var blockWriter = new BinaryWriter(block, Encoding.ASCII, leaveOpen: true))
        {
            blockWriter.Write((uint)claimedBytes);
            blockWriter.Write(Payload);
        }

        return Nif(new[] { (Type: "bhkRagdollSystem", Body: block.ToArray()) });
    }

    private static byte[] Nif((string Type, byte[] Body)[] blocks)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("Gamebryo File Format, Version 20.2.0.7\n"));
        writer.Write(0x14020007u);
        writer.Write((byte)1);
        writer.Write(12u);
        writer.Write((uint)blocks.Length);
        writer.Write(130u);
        for (int i = 0; i < 4; i++) writer.Write((byte)0);

        var types = blocks.Select(block => block.Type).Distinct().ToArray();
        writer.Write((ushort)types.Length);
        foreach (string type in types)
        {
            writer.Write((uint)type.Length);
            writer.Write(Encoding.ASCII.GetBytes(type));
        }

        foreach (var entry in blocks)
            writer.Write((ushort)Array.IndexOf(types, entry.Type));
        foreach (var entry in blocks)
            writer.Write((uint)entry.Body.Length);

        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);

        foreach (var entry in blocks)
            writer.Write(entry.Body);

        writer.Write(0u);
        return stream.ToArray();
    }
}
