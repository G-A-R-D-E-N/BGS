using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Avalonia.Threading;
using BehaviourStudio.App;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.UiSmoke;

public static class AuditLifecycleSmoke
{
    public static void Run()
    {
        SaveAndContinueRefusesDuplicateStateIds();
        CombinedSaveKeepsAnimationEdits();
        ClosingRunsPendingFieldCommitsBeforeDiscard();
    }

    private static void SaveAndContinueRefusesDuplicateStateIds()
    {
        Console.WriteLine("\nsave-and-continue uses the same graph refusal as toolbar save");
        string path = Path.Combine(Path.GetTempPath(), "bgs-audit-save-continue.hkx");
        File.WriteAllBytes(path, Smoke.OneMachineBytes());
        byte[] before = File.ReadAllBytes(path);

        var window = new MainWindow();
        window.Show();
        window.Open(path);
        Dispatcher.UIThread.RunJobs();

        string xml = window.LoadedXml;
        string dupes = xml.Replace("<hkparam name=\"states\" numelements=\"0\">\n</hkparam>",
                                   "<hkparam name=\"states\" numelements=\"2\">#91 #92</hkparam>")
                          .Replace("</hksection>",
                              "\n            <hkobject class=\"hkbStateMachineStateInfo\" name=\"#91\" " +
                              "signature=\"0x39d76713\">\n" +
                              "                <hkparam name=\"name\">A</hkparam>\n" +
                              "                <hkparam name=\"stateId\">0</hkparam>\n" +
                              "                <hkparam name=\"generator\">#93</hkparam>\n" +
                              "                <hkparam name=\"transitions\">null</hkparam>\n" +
                              "            </hkobject>\n" +
                              "            <hkobject class=\"hkbStateMachineStateInfo\" name=\"#92\" " +
                              "signature=\"0x39d76713\">\n" +
                              "                <hkparam name=\"name\">Dup</hkparam>\n" +
                              "                <hkparam name=\"stateId\">0</hkparam>\n" +
                              "                <hkparam name=\"generator\">#94</hkparam>\n" +
                              "                <hkparam name=\"transitions\">null</hkparam>\n" +
                              "            </hkobject>\n" +
                              "            <hkobject class=\"hkbClipGenerator\" name=\"#93\" signature=\"0xd4cc9f6\">\n" +
                              "                <hkparam name=\"name\">ClipA</hkparam>\n" +
                              "                <hkparam name=\"animationName\">a.hkx</hkparam>\n" +
                              "                <hkparam name=\"triggers\">null</hkparam>\n" +
                              "            </hkobject>\n" +
                              "            <hkobject class=\"hkbClipGenerator\" name=\"#94\" signature=\"0xd4cc9f6\">\n" +
                              "                <hkparam name=\"name\">ClipDup</hkparam>\n" +
                              "                <hkparam name=\"animationName\">dup.hkx</hkparam>\n" +
                              "                <hkparam name=\"triggers\">null</hkparam>\n" +
                              "            </hkobject>\n" +
                              "        </hksection>");
        window.SetXmlForTest(dupes);
        window.SaveForTest();
        Dispatcher.UIThread.RunJobs();
        Smoke.CheckTrue("toolbar save leaves duplicate-state bytes untouched",
            File.ReadAllBytes(path).SequenceEqual(before));

        window.DiscardDecision = () => DiscardChoice.Save;
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Smoke.CheckTrue("save-and-continue keeps the window open", window.IsVisible);
        Smoke.CheckTrue("and does not write the duplicate state IDs",
            File.ReadAllBytes(path).SequenceEqual(before));
        Smoke.CheckTrue("and the document stays dirty", window.IsDirty);
        Smoke.CloseForTest(window);
    }

    private static void CombinedSaveKeepsAnimationEdits()
    {
        Console.WriteLine("\nsaving metadata and animation together keeps both edits");
        string path = Path.Combine(Path.GetTempPath(), "bgs-audit-mixed-save.hkx");
        (byte[] source, HkxAnimationData animation) = MakeAnimation();
        File.WriteAllBytes(path, source);
        var written = AnimationSaveTransaction.Commit(
            path, animation, DocumentSourceStamp.Capture(path), editedTrack: 0, editedFrame: 1);
        Smoke.CheckTrue("the interleaved animation fixture wrote", written.Committed);

        var window = new MainWindow();
        window.Show();
        window.Open(path);
        Dispatcher.UIThread.RunJobs();
        Smoke.CheckTrue("the animation fixture has an editable frame", window.PickFrame(0, 1));
        window.TypeFramePosition("123, 456, 789");
        var model = BehaviourGraphModel.Parse(window.LoadedXml);
        var binding = model.Objects.First(o => o.Class == "hkaAnimationBinding");
        window.SetXmlForTest(HkxTextEdit.SetParam(window.LoadedXml, binding.Id, "originalSkeletonName", "audit skeleton"));
        Smoke.CheckTrue("animation frame and metadata can both be dirty",
            window.IsDirty && window.AnimationEdited);

        window.DiscardDecision = () => DiscardChoice.Save;
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Smoke.CheckTrue("combined save closes", !window.IsVisible);
        var reread = new HkxBinaryReader().ReadAnimation(path);
        Smoke.CheckTrue("the edited frame is on disk",
            reread.Tracks[0].Translations[1] == new Vector3(123, 456, 789));
        Smoke.CheckTrue("and the metadata edit is on disk",
            HkxTextEdit.TextOf(path).Contains("audit skeleton", StringComparison.Ordinal));
    }

    private static void ClosingRunsPendingFieldCommitsBeforeDiscard()
    {
        Console.WriteLine("\nclosing runs pending field commits before discard evaluation");
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        bool committed = false;
        int decisions = 0;
        window.QueuePendingFieldCommitForTest(() =>
        {
            committed = true;
            window.MarkAnimationEditedForTest();
        });
        window.DiscardDecision = () =>
        {
            decisions++;
            return DiscardChoice.Cancel;
        };
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Smoke.CheckTrue("close runs the pending field commit first", committed);
        Smoke.CheckTrue("and then consults discard because the commit dirtied the document",
            decisions > 0);
        Smoke.CheckTrue("and cancel keeps the window open", window.IsVisible);
        Smoke.CloseForTest(window);
    }

    private static (byte[] Source, HkxAnimationData Animation) MakeAnimation()
    {
        const int frames = 5;
        string sourceClass = NativeAnimation.SplineClass;
        var animation = new HkxAnimationData
        {
            AnimationClass = sourceClass,
            NumFrames = frames,
            NumTracks = 1,
            FrameDuration = 1f / 30f,
            Duration = (frames - 1) / 30f,
        };
        var track = new HkxTrackData { RotationAnimated = true };
        track.TranslationAnimated[0] = true;
        track.TranslationAnimated[1] = true;
        track.TranslationAnimated[2] = true;
        for (int frame = 0; frame < frames; frame++)
        {
            track.Translations.Add(new Vector3(frame, frame * 2, -frame));
            track.Rotations.Add(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, frame * 0.1f));
            track.Scales.Add(Vector3.One);
        }
        animation.Tracks.Add(track);

        var image = new PackfileImage { Predicates = new byte[16] };
        image.Sections.Add(new PackfileSection { TagBytes = Tag("__classnames__") });
        image.Sections.Add(new PackfileSection { TagBytes = Tag("__types__") });
        image.Sections.Add(new PackfileSection { TagBytes = Tag("__data__") });
        var source = NativeAppend.Object(image, sourceClass);
        var binding = NativeAppend.Object(image, "hkaAnimationBinding");
        NativeAppend.Attach(image, binding.Id, "animation", source.Id);
        var duration = HavokClassTypes.Shipped.Members(sourceClass)
            .Single(member => member.Name == "duration");
        var data = image.Section("__data__")!;
        BitConverter.GetBytes(animation.Duration).CopyTo(data.Data, source.Offset + duration.Offset);
        FixupOrder.Reorder(image);
        return (image.Rebuild(), animation);
    }

    private static byte[] Tag(string name)
    {
        var bytes = new byte[20];
        System.Text.Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
        return bytes;
    }
}
