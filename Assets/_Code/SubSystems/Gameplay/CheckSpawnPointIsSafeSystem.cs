﻿using Latios;
using Latios.Psyshock;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.Profiling;

//The IFindPairsProcessors only force safeToSpawn from true to false.
//Because of this, it is safe to use the Unsafe parallel schedulers.
//However, if the logic is ever modified, this decision needs to be re-evaluated.

namespace Lsss
{
    [RequireMatchingQueriesForUpdate]
    public partial class CheckSpawnPointIsSafeSystem : SubSystem
    {
        int frameCount = 0;

        protected override void OnUpdate()
        {
            frameCount++;
            Profiler.BeginSample("CheckSpawnPointIsSafe_OnUpdate");

            Profiler.BeginSample("Clear safe");
            Entities.WithAll<SpawnPoint>().ForEach((ref SafeToSpawn safeToSpawn) =>
            {
                safeToSpawn.safe = true;
            }).ScheduleParallel();
            Profiler.EndSample();

            var processor = new SpawnPointIsNotSafeProcessor
            {
                safeToSpawnLookup = GetComponentLookup<SafeToSpawn>()
            };

            var closeProcessor = new SpawnPointsAreTooCloseProcessor
            {
                safeToSpawnLookup = processor.safeToSpawnLookup
            };

            Profiler.BeginSample("Fetch");
            var spawnLayer = sceneBlackboardEntity.GetCollectionComponent<SpawnPointCollisionLayer>(true).layer;
            Profiler.EndSample();
            Profiler.BeginSample("Schedule");
            Dependency = Physics.FindPairs(spawnLayer, closeProcessor).ScheduleParallelUnsafe(Dependency);
            Profiler.EndSample();

            //Profiler.BeginSample("Fetch");
            //var wallLayer = sceneBlackboardEntity.GetCollectionComponent<WallCollisionLayer>(true).layer;
            //Profiler.EndSample();
            //Profiler.BeginSample("Schedule");
            //Dependency = Physics.FindPairs(spawnLayer, wallLayer, processor).ScheduleParallelUnsafe(Dependency);
            //Profiler.EndSample();

            Profiler.BeginSample("Fetch");
            var bulletLayer = sceneBlackboardEntity.GetCollectionComponent<BulletCollisionLayer>(true).layer;
            Profiler.EndSample();
            Profiler.BeginSample("Schedule");
            Dependency = Physics.FindPairs(spawnLayer, bulletLayer, processor).ScheduleParallelUnsafe(Dependency);
            Profiler.EndSample();

            Profiler.BeginSample("Fetch");
            var explosionLayer = sceneBlackboardEntity.GetCollectionComponent<ExplosionCollisionLayer>(true).layer;
            Profiler.EndSample();
            Profiler.BeginSample("Schedule");
            Dependency = Physics.FindPairs(spawnLayer, explosionLayer, processor).ScheduleParallelUnsafe(Dependency);
            Profiler.EndSample();

            //Profiler.BeginSample("Fetch");
            //var wormholeLayer = sceneBlackboardEntity.GetCollectionComponent<WormholeCollisionLayer>(true).layer;
            //Profiler.EndSample();
            //Profiler.BeginSample("Schedule");
            //Dependency = Physics.FindPairs(spawnLayer, wormholeLayer, processor).ScheduleParallelUnsafe(Dependency);
            //Profiler.EndSample();

            //Todo: Remove hack
            //This hack exists because the .Run forces Dependency to complete first. But we don't want that.
            var backupDependency = Dependency;
            Dependency           = default;

            Entities.WithAll<FactionTag>().ForEach((Entity entity, int entityInQueryIndex) =>
            {
                if (entityInQueryIndex == 0)
                    Dependency = backupDependency;

                Profiler.BeginSample("Fetch2");
                var shipLayer = EntityManager.GetCollectionComponent<FactionShipsCollisionLayer>(entity, true).layer;
                Profiler.EndSample();
                Profiler.BeginSample("Schedule2");
                Dependency = Physics.FindPairs(spawnLayer, shipLayer, processor).ScheduleParallelUnsafe(Dependency);
                Profiler.EndSample();
            }).WithoutBurst().Run();

            Profiler.EndSample();
        }

        //Assumes A is SpawnPoint
        struct SpawnPointIsNotSafeProcessor : IFindPairsProcessor
        {
            public PhysicsComponentLookup<SafeToSpawn> safeToSpawnLookup;

            public void Execute(in FindPairsResult result)
            {
                // No need to check narrow phase. AABB check is good enough
                safeToSpawnLookup[result.entityA] = new SafeToSpawn { safe = false };
            }
        }

        struct SpawnPointsAreTooCloseProcessor : IFindPairsProcessor
        {
            public PhysicsComponentLookup<SafeToSpawn> safeToSpawnLookup;

            public void Execute(in FindPairsResult result)
            {
                safeToSpawnLookup[result.entityA] = new SafeToSpawn { safe = false };
                safeToSpawnLookup[result.entityB]                          = new SafeToSpawn { safe = false };
            }
        }
    }
}

