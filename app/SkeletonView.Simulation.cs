using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Hkx;

namespace BehaviourStudio.App;

public partial class SkeletonView
{
    private IReadOnlyDictionary<int, HavokBoneFrame>? _simulationFrames;

    public void ShowSimulationFrames(IReadOnlyDictionary<int, HavokBoneFrame>? frames)
    {
        _simulationFrames = frames;
        if (frames != null && _bodies?.Skeleton is { } skeleton)
        {
            _physicsPose = skeleton.WorldReferenceFrames().ToArray();
            foreach (var binding in _bodies.BoneBindings)
                if (binding.BoneIndex >= 0 && binding.BoneIndex < _physicsPose.Length && frames.TryGetValue(binding.BodyId, out var frame))
                    _physicsPose[binding.BoneIndex] = frame;
        }
        else _physicsPose = null;
        InvalidateVisual();
    }
}
