using System.IO;
using Avalonia;
using Avalonia.Input;
using BehaviourStudio.App;
using OpenCommonwealth.Services.Hkx;
using Xunit;

namespace BehaviourStudio.Tests;

public sealed class FrameDistancePointerLifecycleTests
{
    private const string Skeleton =
        "Meshes/Actors/Character/CharacterAssets/skeleton.hkx";

    private static string Fixture(string relative) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures/vanilla",
                     relative.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void BodyFrameDistanceSurvivesFrameHideAndPointerMove()
    {
        var model = HavokPhysicsExtractor.TryExtract(File.ReadAllBytes(Fixture(Skeleton)));
        Assert.NotNull(model);

        var view = new PointerProbeSkeletonView
        {
            EngineeringFrames = true,
        };
        view.SetBodies(model);
        view.ToggleBodyPinForTest(0);
        view.ToggleBodyPinForTest(1);
        Assert.Equal((0, 1), view.FrameDistanceBodies);

        view.EngineeringFrames = false;
        view.MovePointer(new Point(0, 0));

        Assert.Empty(view.PinnedBodyIds);
        Assert.Equal((0, 1), view.FrameDistanceBodies);

        view.EngineeringFrames = true;
        view.ToggleBodyPinForTest(1);
        Assert.Equal((0, 1), view.FrameDistanceBodies);

        view.ToggleBodyPinForTest(2);
        Assert.Equal((1, 2), view.FrameDistanceBodies);
    }

    private sealed class PointerProbeSkeletonView : SkeletonView
    {
        public void MovePointer(Point point)
        {
            OnPointerMoved(new PointerEventArgs(
                InputElement.PointerMovedEvent,
                this,
                new Pointer(1, PointerType.Mouse, true),
                this,
                point,
                1,
                new PointerPointProperties(),
                KeyModifiers.None));
        }
    }
}
