using System.Collections.Generic;
using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;

namespace OpenCommonwealth.Services.Hkx;

public sealed partial class RagdollSimulation
{
    private struct Contacts : INarrowPhaseCallbacks
    {
        private readonly HashSet<(int, int)> _adjacent;
        public Contacts(HashSet<(int, int)> adjacent) => _adjacent = adjacent;
        public void Initialize(Simulation simulation) { }
        public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float margin) =>
            (a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic) &&
            (a.Mobility == CollidableMobility.Static || b.Mobility == CollidableMobility.Static ||
             !_adjacent.Contains(Pair(a.BodyHandle.Value, b.BodyHandle.Value)));
        public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childA, int childB) => true;
        public bool ConfigureContactManifold<T>(int workerIndex, CollidablePair pair, ref T manifold,
            out PairMaterialProperties material) where T : unmanaged, IContactManifold<T>
        {
            material = new PairMaterialProperties { FrictionCoefficient = 0.8f, MaximumRecoveryVelocity = 100,
                SpringSettings = new SpringSettings(30, 1) };
            return true;
        }
        public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childA, int childB,
            ref ConvexContactManifold manifold) => true;
        public void Dispose() { }
    }

    private struct Gravity : IPoseIntegratorCallbacks
    {
        private readonly float _gravity;
        private Vector3Wide _velocityChange;
        private Vector<float> _damping;
        public Gravity(float gravity) { _gravity = gravity; _velocityChange = default; _damping = default; }
        public AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
        public bool AllowSubstepsForUnconstrainedBodies => false;
        public bool IntegrateVelocityForKinematics => false;
        public void Initialize(Simulation simulation) { }
        public void PrepareForIntegration(float dt)
        {
            _velocityChange = Vector3Wide.Broadcast(new Vector3(0, 0, -_gravity * dt));
            _damping = new Vector<float>(System.MathF.Pow(0.98f, dt));
        }
        public void IntegrateVelocity(Vector<int> indices, Vector3Wide position, QuaternionWide orientation,
            BodyInertiaWide inertia, Vector<int> mask, int workerIndex, Vector<float> dt, ref BodyVelocityWide velocity)
        {
            velocity.Linear = (velocity.Linear + _velocityChange) * _damping;
            velocity.Angular *= _damping;
        }
    }
}
