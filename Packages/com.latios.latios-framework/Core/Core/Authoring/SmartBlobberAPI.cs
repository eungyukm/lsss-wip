using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Jobs;

namespace Latios.Authoring
{
    /// <summary>
    /// A handle to a computed blob to be created by a smart blobber
    /// </summary>
    /// <typeparam name="TBlobType">The top of blob to be created</typeparam>
    public struct SmartBlobberHandle<TBlobType> where TBlobType : unmanaged
    {
        internal Entity entityWithResultBlob;
        internal bool   wasFiltered;

        /// <summary>
        /// Retrieves the blob asset after the smart blobber has run. Throws an exception if called before the smart blobber has run.
        /// </summary>
        /// <returns>The blob asset generated by the smart blobber</returns>
        public BlobAssetReference<TBlobType> Resolve(EntityManager entityManager)
        {
            if (wasFiltered)
                return BlobAssetReference<TBlobType>.Null;

            if (!IsValid)
                throw new System.InvalidOperationException("This handle has not been initialized.");

            if (!entityManager.HasComponent<SmartBlobberTrackingData>(entityWithResultBlob))
            {
                UnityEngine.Debug.LogError("Something internally went wrong.");
            }
            var trackingData = entityManager.GetComponentData<SmartBlobberTrackingData>(entityWithResultBlob);
            if (!trackingData.isFinalized)
                throw new System.InvalidOperationException(
                    $"The smart blobber has not processed the blob yet. Please request the blob generation prior to smart blobber execution such as during ISmartBakerData.CaptureInputsAndFilter() and do not attempt to resolve the blob until after smart blobber execution such as during SmartBaker.Process().");

            var result = entityManager.GetComponentData<SmartBlobberResult>(entityWithResultBlob);

            return result.blob.Reinterpret<TBlobType>();
        }

        /// <summary>
        /// Retrieves the blob asset after the smart blobber has run. Throws an exception if called before the smart blobber has run.
        /// </summary>
        /// <returns>The blob asset generated by the smart blobber</returns>
        public BlobAssetReference<TBlobType> Resolve(ref SmartBlobberResolverLookup resolverLookup)
        {
            if (wasFiltered)
                return BlobAssetReference<TBlobType>.Null;

            if (!IsValid)
                throw new System.InvalidOperationException("This handle has not been initialized.");

            if (!resolverLookup.trackingDataLookup.HasComponent(entityWithResultBlob))
            {
                UnityEngine.Debug.LogError("Something internally went wrong.");
            }
            var trackingData = resolverLookup.trackingDataLookup[entityWithResultBlob];
            if (!trackingData.isFinalized)
                throw new System.InvalidOperationException(
                    $"The smart blobber has not processed the blob yet. Please request the blob generation prior to smart blobber execution such as during ISmartBakerData.CaptureInputsAndFilter() and do not attempt to resolve the blob until after smart blobber execution such as during SmartBaker.Process().");

            var result = resolverLookup.resultLookup[entityWithResultBlob];

            return result.blob.Reinterpret<TBlobType>();
        }

        /// <summary>
        /// Returns true if this handle was generated by a smart blobber.
        /// </summary>
        public bool IsValid => entityWithResultBlob != Entity.Null;

        public static implicit operator SmartBlobberHandleUntyped(SmartBlobberHandle<TBlobType> typed)
        {
            return new SmartBlobberHandleUntyped { entityWithResultBlob = typed.entityWithResultBlob, wasFiltered = typed.wasFiltered };
        }
    }

    /// <summary>
    /// A handle to a computed blob to be created by a smart blobber
    /// </summary>
    public struct SmartBlobberHandleUntyped
    {
        internal Entity entityWithResultBlob;
        internal bool   wasFiltered;

        /// <summary>
        /// Retrieves the blob asset after the smart blobber has run. Throws an exception if called before the smart blobber has run.
        /// </summary>
        /// <returns>The blob asset generated by the smart blobber</returns>
        public UnsafeUntypedBlobAssetReference Resolve(EntityManager entityManager)
        {
            if (wasFiltered)
                return default;

            if (!IsValid)
                throw new System.InvalidOperationException("This handle has not been initialized.");

            var trackingData = entityManager.GetComponentData<SmartBlobberTrackingData>(entityWithResultBlob);
            if (!trackingData.isFinalized)
                throw new System.InvalidOperationException(
                    $"The smart blobber has not processed the blob yet. Please request the blob generation prior to smart blobber execution such as during ISmartBakerData.CaptureInputsAndFilter() and do not attempt to resolve the blob until after smart blobber execution such as during SmartBaker.Process().");

            var result = entityManager.GetComponentData<SmartBlobberResult>(entityWithResultBlob);

            return result.blob;
        }

        /// <summary>
        /// Returns true if this handle was generated by a smart blobber.
        /// </summary>
        public bool IsValid => entityWithResultBlob != Entity.Null;
    }

    [BurstCompile]
    public static class SmartBlobberRequestExtensions
    {
        public static SmartBlobberHandle<TBlobType> RequestCreateBlobAsset<TBlobType, TInputType>(
            this IBaker baker,
            TInputType input)
            where TBlobType : unmanaged
            where TInputType : ISmartBlobberRequestFilter<TBlobType>
        {
            var entity = baker.CreateAdditionalEntity(TransformUsageFlags.None, true);
            if (input.Filter(baker, entity))
            {
                MakeComponentTypeSet(out var typesToAdd);
                baker.AddComponent(entity, typesToAdd);
                baker.SetComponent(entity, new SmartBlobberTrackingData
                {
                    authoringInstanceID = baker.GetAuthoringInstancedID(),
                    isFinalized         = false
                });
                baker.AddSharedComponent(entity, new SmartBlobberBlobTypeHash { hash = BurstRuntime.GetHashCode64<TBlobType>() });
                return new SmartBlobberHandle<TBlobType> { entityWithResultBlob      = entity, wasFiltered = false };
            }
            else
            {
                return new SmartBlobberHandle<TBlobType> { entityWithResultBlob = Entity.Null, wasFiltered = true };
            }
        }

        [BurstCompile]
        private static void MakeComponentTypeSet(out ComponentTypeSet typeSet)
        {
            typeSet = new ComponentTypeSet(ComponentType.ReadWrite<SmartBlobberResult>(), ComponentType.ReadWrite<SmartBlobberTrackingData>());
        }
    }

    public partial struct SmartBlobberResolverLookup
    {
        [ReadOnly] internal ComponentLookup<SmartBlobberTrackingData> trackingDataLookup;
        [ReadOnly] internal ComponentLookup<SmartBlobberResult>       resultLookup;

        public void Update(ref SystemState state)
        {
            trackingDataLookup.Update(ref state);
            resultLookup.Update(ref state);
        }

        public void Update(SystemBase system)
        {
            trackingDataLookup.Update(system);
            resultLookup.Update(system);
        }
    }

    public static class SmartBlobberResolverExtensions
    {
        public static SmartBlobberResolverLookup GetSmartBlobberResolverLookup(this ref SystemState state)
        {
            return new SmartBlobberResolverLookup
            {
                trackingDataLookup = state.GetComponentLookup<SmartBlobberTrackingData>(true),
                resultLookup       = state.GetComponentLookup<SmartBlobberResult>(true)
            };
        }

        public static SmartBlobberResolverLookup GetSmartBlobberResolverLookup(this SystemBase system)
        {
            return new SmartBlobberResolverLookup
            {
                trackingDataLookup = system.GetComponentLookup<SmartBlobberTrackingData>(true),
                resultLookup       = system.GetComponentLookup<SmartBlobberResult>(true)
            };
        }
    }

    public interface ISmartBlobberRequestFilter<TBlobType> where TBlobType : unmanaged
    {
        bool Filter(IBaker baker, Entity blobBakingEntity);
    }

    [TemporaryBakingType]
    public struct SmartBlobberResult : IComponentData
    {
        public UnsafeUntypedBlobAssetReference blob;
    }

    public partial struct SmartBlobberTools<TBlobType> where TBlobType : unmanaged
    {
        public void Register(World managedWorld)
        {
            if (managedWorld.GetExistingSystemManaged<Systems.SmartBlobberTypedPostProcessBakingSystem<TBlobType> >() != null)
                return;
            var system = managedWorld.GetOrCreateSystemManaged<Systems.SmartBlobberTypedPostProcessBakingSystem<TBlobType> >();
            var group  = managedWorld.GetExistingSystemManaged<Systems.SmartBlobberCleanupBakingGroup>();
            group.AddSystemToUpdateList(system);
        }
    }

    [TemporaryBakingType]
    internal struct SmartBlobberTrackingData : IComponentData
    {
        public Hash128 hash;
        public Entity  thisEntity;
        public int     authoringInstanceID;
        public bool    isFinalized;
        public bool    isNull;
        public bool    shouldBeKept;
    }

    [TemporaryBakingType]
    internal struct SmartBlobberBlobTypeHash : ISharedComponentData
    {
        public long hash;
    }
}

