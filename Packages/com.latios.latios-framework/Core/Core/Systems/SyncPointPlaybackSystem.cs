﻿using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.Profiling;

namespace Latios.Systems
{
    public class SyncPointPlaybackSystem : SubSystem
    {
        enum PlaybackType
        {
            Entity,
            Enable,
            Disable,
            Destroy,
        }

        struct PlaybackInstance
        {
            public PlaybackType type;
            public Type         requestingSystemType;
        }

        List<PlaybackInstance>     m_playbackInstances     = new List<PlaybackInstance>();
        List<EntityCommandBuffer>  m_entityCommandBuffers  = new List<EntityCommandBuffer>();
        List<EnableCommandBuffer>  m_enableCommandBuffers  = new List<EnableCommandBuffer>();
        List<DisableCommandBuffer> m_disableCommandBuffers = new List<DisableCommandBuffer>();
        List<DestroyCommandBuffer> m_destroyCommandBuffers = new List<DestroyCommandBuffer>();

        NativeList<JobHandle> m_jobHandles;

        protected override void OnCreate()
        {
            m_jobHandles = new NativeList<JobHandle>(Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            m_playbackInstances.Clear();
            JobHandle.CompleteAll(m_jobHandles);
            m_jobHandles.Dispose();
            foreach (var ecb in m_entityCommandBuffers)
                ecb.Dispose();
            foreach (var ecb in m_enableCommandBuffers)
                ecb.Dispose();
            foreach (var dcb in m_disableCommandBuffers)
                dcb.Dispose();
            foreach (var dcb in m_destroyCommandBuffers)
                dcb.Dispose();
        }

        public override bool ShouldUpdateSystem()
        {
            return m_playbackInstances.Count > 0;
        }

        protected override void OnUpdate()
        {
            JobHandle.CompleteAll(m_jobHandles);
            m_jobHandles.Clear();
            CompleteDependency();

            int entityIndex  = 0;
            int enableIndex  = 0;
            int disableIndex = 0;
            int destroyIndex = 0;
            foreach (var instance in m_playbackInstances)
            {
                //Todo: We don't fail as gracefully as EntityCommandBufferSystem, but I'm not sure what is exactly required to meet that. There's way too much magic there.
                Profiler.BeginSample(instance.requestingSystemType == null ? "Unknown" : instance.requestingSystemType.Name);
                switch (instance.type)
                {
                    case PlaybackType.Entity:
                    {
                        var ecb = m_entityCommandBuffers[entityIndex];
                        ecb.Playback(EntityManager);
                        ecb.Dispose();
                        entityIndex++;
                        break;
                    }
                    case PlaybackType.Enable:
                    {
                        var ecb = m_enableCommandBuffers[enableIndex];
                        ecb.Playback(EntityManager, GetBufferFromEntity<LinkedEntityGroup>(true));
                        ecb.Dispose();
                        enableIndex++;
                        break;
                    }
                    case PlaybackType.Disable:
                    {
                        var dcb = m_disableCommandBuffers[disableIndex];
                        dcb.Playback(EntityManager, GetBufferFromEntity<LinkedEntityGroup>(true));
                        dcb.Dispose();
                        disableIndex++;
                        break;
                    }
                    case PlaybackType.Destroy:
                    {
                        var dcb = m_destroyCommandBuffers[destroyIndex];
                        dcb.Playback(EntityManager);
                        dcb.Dispose();
                        destroyIndex++;
                        break;
                    }
                }
                Profiler.EndSample();
            }
            m_playbackInstances.Clear();
            m_entityCommandBuffers.Clear();
            m_enableCommandBuffers.Clear();
            m_disableCommandBuffers.Clear();
            m_destroyCommandBuffers.Clear();
        }

        public EntityCommandBuffer CreateEntityCommandBuffer()
        {
            //Todo: Expose variant of ECB constructor which allows us to set DisposeSentinal stack depth to -1 and use TempJob.
            var ecb      = new EntityCommandBuffer(Allocator.Persistent, PlaybackPolicy.SinglePlayback);
            var instance = new PlaybackInstance
            {
                type                 = PlaybackType.Entity,
                requestingSystemType = ExecutingSystemType,
            };
            m_playbackInstances.Add(instance);
            m_entityCommandBuffers.Add(ecb);
            return ecb;
        }

        public EnableCommandBuffer CreateEnableCommandBuffer()
        {
            //Todo: We use Persistent allocator here because of the NativeReference. This recreates the DisposeSentinal stuff except with the slower allocator.
            var ecb      = new EnableCommandBuffer(Allocator.Persistent);
            var instance = new PlaybackInstance
            {
                type                 = PlaybackType.Enable,
                requestingSystemType = ExecutingSystemType,
            };
            m_playbackInstances.Add(instance);
            m_enableCommandBuffers.Add(ecb);
            return ecb;
        }

        public DisableCommandBuffer CreateDisableCommandBuffer()
        {
            //Todo: We use Persistent allocator here because of the NativeReference. This recreates the DisposeSentinal stuff except with the slower allocator.
            var dcb      = new DisableCommandBuffer(Allocator.Persistent);
            var instance = new PlaybackInstance
            {
                type                 = PlaybackType.Disable,
                requestingSystemType = ExecutingSystemType,
            };
            m_playbackInstances.Add(instance);
            m_disableCommandBuffers.Add(dcb);
            return dcb;
        }

        public DestroyCommandBuffer CreateDestroyCommandBuffer()
        {
            //Todo: We use Persistent allocator here because of the NativeReference. This recreates the DisposeSentinal stuff except with the slower allocator.
            var dcb      = new DestroyCommandBuffer(Allocator.Persistent);
            var instance = new PlaybackInstance
            {
                type                 = PlaybackType.Destroy,
                requestingSystemType = ExecutingSystemType,
            };
            m_playbackInstances.Add(instance);
            m_destroyCommandBuffers.Add(dcb);
            return dcb;
        }

        public void AddJobHandleForProducer(JobHandle handle)
        {
            //Todo, maybe we could reason about this better and get better job scheduling, but this seems fine for now.
            //We will always need this if a request comes from a MonoBehaviour or something.
            m_jobHandles.Add(handle);
        }
    }
}
