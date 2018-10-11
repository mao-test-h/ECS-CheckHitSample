#pragma warning disable 0649

namespace MainContents
{
    // 衝突判定関連グループ
    public sealed class CollisionUpdateGroup { }
}

// ヒット情報 & 衝突プリミティブ 定義
// ▽参照
// https://github.com/Unity-Technologies/AnotherThreadECS
// AnotherThreadECS/Assets/Scripts/ECSCollider.cs
namespace MainContents
{
    using Unity.Entities;
    using Unity.Transforms;
    using Unity.Mathematics;
    using Unity.Jobs;
    using Unity.Collections;
    using Unity.Burst;

    /// <summary>
    /// 球体形状の衝突プリミティブ
    /// </summary>
    public struct SphereCollider : IComponentData
    {
        public float3 Position;
        public float3 OffsetPosition;
        public float Radius;
        public byte IsUpdated;  // boolean
        public bool Intersect(ref SphereCollider another)
        {
            if (this.IsUpdated == 0) { return false; }
            var diff = another.Position - this.Position;
            var dist2 = math.lengthsq(diff);
            var rad = this.Radius + another.Radius;
            var rad2 = rad * rad;
            return (dist2 < rad2);
        }
    }

    /// <summary>
    /// 衝突プリミティブの更新処理
    /// </summary>
    [UpdateInGroup(typeof(CollisionUpdateGroup))]
    [UpdateAfter(typeof(Unity.Rendering.MeshInstanceRendererSystem))]
    public sealed class ColliderUpdate : JobComponentSystem
    {
        [BurstCompile]
        public struct UpdateJob : IJobProcessComponentData<Position, Rotation, SphereCollider>
        {
            public void Execute([ReadOnly] ref Position position, [ReadOnly] ref Rotation rotation, ref SphereCollider collider)
            {
                collider.Position = math.mul(rotation.Value, collider.OffsetPosition) + position.Value;
                collider.IsUpdated = 1;
            }
        }
        protected override JobHandle OnUpdate(JobHandle inputDeps) => new UpdateJob().Schedule(this, inputDeps);
    }
}

// 衝突判定(総当たり)
namespace MainContents
{
    using Unity.Entities;
    using Unity.Mathematics;
    using Unity.Jobs;
    using Unity.Collections;
    using Unity.Burst;

    /// <summary>
    /// 衝突判定(総当たり)
    /// </summary>
    [UpdateInGroup(typeof(CollisionUpdateGroup))]
    [UpdateAfter(typeof(ColliderUpdate))]
    public sealed class CollisionSystem : JobComponentSystem
    {
        // ------------------------------
        #region // Jobs

        /// <summary>
        /// 当たり判定(CheckHit → Player)
        /// </summary>
        [BurstCompile]
        struct CheckHitJob : IJobProcessComponentData<SphereCollider, Destroyable>
        {
            [ReadOnly] public NativeArray<SphereCollider> PlayerColliders;
            public void Execute([ReadOnly] ref SphereCollider checkHitCollider, ref Destroyable destroyable)
            {
                for (int i = 0; i < this.PlayerColliders.Length; ++i)
                {
                    var playerCollider = this.PlayerColliders[i];
                    if (checkHitCollider.Intersect(ref playerCollider))
                    {
                        // ヒット
                        destroyable = Destroyable.Kill;
                    }
                }
            }
        }

        #endregion // Jobs

        // ------------------------------
        #region // Private Fields

        ComponentGroup _playerGroup;
        NativeArray<SphereCollider> _playerColliders;

        #endregion // Private Fields

        // ----------------------------------------------------
        #region // Protected Methods

        protected override void OnCreateManager() => this._playerGroup = GetComponentGroup(ComponentType.ReadOnly<Player>(), ComponentType.ReadOnly<SphereCollider>());
        protected override void OnDestroyManager() => this.DisposeBuffers();

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            this.DisposeBuffers();
            var handle = inputDeps;

            var playerGroupLength = this._playerGroup.CalculateLength();

            // Allocate Memory
            this._playerColliders = new NativeArray<SphereCollider>(
                playerGroupLength,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);

            // ComponentDataArray
            handle = new CopyComponentData<SphereCollider>
            {
                Source = this._playerGroup.GetComponentDataArray<SphereCollider>(),
                Results = this._playerColliders,
            }.Schedule(playerGroupLength, 32, handle); ;

            // Check Hit
            handle = new CheckHitJob
            {
                PlayerColliders = this._playerColliders
            }.Schedule(this, handle);

            return handle;
        }

        #endregion // Protected Methods

        // ----------------------------------------------------
        #region // Private Methods

        void DisposeBuffers()
        {
            if (this._playerColliders.IsCreated) { this._playerColliders.Dispose(); }
        }

        #endregion // Private Methods
    }
}

// 衝突判定(空間分割)
// ▽参照
// https://github.com/Unity-Technologies/AnotherThreadECS
// AnotherThreadECS/Assets/Scripts/ECSCollision.cs
namespace MainContents
{
    using Unity.Entities;
    using Unity.Mathematics;
    using Unity.Jobs;
    using Unity.Collections;
    using Unity.Burst;

    /// <summary>
    /// 衝突判定(空間分割)
    /// </summary>
    [UpdateInGroup(typeof(CollisionUpdateGroup))]
    [UpdateAfter(typeof(ColliderUpdate))]
    public sealed class SplitSpaceCollisionSystem : JobComponentSystem
    {
        // ------------------------------
        #region // Jobs

        /// <summary>
        /// ハッシュ値の算出
        /// </summary>
        [BurstCompile]
        struct HashPositions : IJobParallelFor
        {
            public float CellRadius;
            [ReadOnly] public ComponentDataArray<SphereCollider> SphereColliders;
            [ReadOnly] public NativeArray<float3> Offsets;
            public NativeMultiHashMap<int, int>.Concurrent Hashmap;
            public void Execute(int i)
            {
                var sphereCollider = this.SphereColliders[i];
                var center = sphereCollider.Position;
                var hashCenter = GridHash.Hash(center, this.CellRadius);
                this.Hashmap.Add(hashCenter, i);
                for (int j = 0; j < this.Offsets.Length; ++j)
                {
                    var offset = center + this.Offsets[j] * sphereCollider.Radius;
                    var hash = GridHash.Hash(offset, this.CellRadius);
                    if (hash == hashCenter) { continue; }
                    this.Hashmap.Add(hash, i);
                }
            }
        }

        /// <summary>
        /// 当たり判定(CheckHit → Player)
        /// </summary>
        [BurstCompile]
        struct CheckHitJob : IJobProcessComponentData<SphereCollider, Destroyable>
        {
            public float CellRadius;
            [ReadOnly] public NativeArray<SphereCollider> PlayerColliders;
            [ReadOnly] public NativeMultiHashMap<int, int> PlayerHashmap;
            public void Execute([ReadOnly] ref SphereCollider checkHitCollider, ref Destroyable destroyable)
            {
                int hash = GridHash.Hash(checkHitCollider.Position, this.CellRadius);
                int index; NativeMultiHashMapIterator<int> iterator;
                for (bool success = this.PlayerHashmap.TryGetFirstValue(hash, out index, out iterator);
                    success;
                    success = this.PlayerHashmap.TryGetNextValue(out index, ref iterator))
                {
                    var playerCollider = this.PlayerColliders[index];
                    if (checkHitCollider.Intersect(ref playerCollider))
                    {
                        // ヒット
                        destroyable = Destroyable.Kill;
                    }
                }
            }
        }

        #endregion // Jobs

        // ------------------------------
        #region // Private Fields
        const int MaxGridNum = 3 * 3 * 3;
        const float CellRadius = 8f;

        NativeArray<float3> _offsets;

        ComponentGroup _playerGroup;
        NativeArray<SphereCollider> _playerColliders;
        NativeMultiHashMap<int, int> _playerHashmap;

        #endregion // Private Fields

        // ----------------------------------------------------
        #region // Protected Methods

        protected override void OnCreateManager()
        {
            // 3 x 3 x 3( = 27)のグリッドを作成。中心をremoveしているので実体としては26
            this._offsets = new NativeArray<float3>(27 - 1, Allocator.Persistent);
            this._offsets[0] = new float3(1f, 1f, 1f);
            this._offsets[1] = new float3(1f, 1f, 0f);
            this._offsets[2] = new float3(1f, 1f, -1f);
            this._offsets[3] = new float3(1f, 0f, 1f);
            this._offsets[4] = new float3(1f, 0f, 0f);
            this._offsets[5] = new float3(1f, 0f, -1f);
            this._offsets[6] = new float3(1f, -1f, 1f);
            this._offsets[7] = new float3(1f, -1f, 0f);
            this._offsets[8] = new float3(1f, -1f, -1f);

            this._offsets[9] = new float3(0f, 1f, 1f);
            this._offsets[10] = new float3(0f, 1f, 0f);
            this._offsets[11] = new float3(0f, 1f, -1f);
            this._offsets[12] = new float3(0f, 0f, 1f);
            // removed center
            this._offsets[13] = new float3(0f, 0f, -1f);
            this._offsets[14] = new float3(0f, -1f, 1f);
            this._offsets[15] = new float3(0f, -1f, 0f);
            this._offsets[16] = new float3(0f, -1f, -1f);

            this._offsets[17] = new float3(-1f, 1f, 1f);
            this._offsets[18] = new float3(-1f, 1f, 0f);
            this._offsets[19] = new float3(-1f, 1f, -1f);
            this._offsets[20] = new float3(-1f, 0f, 1f);
            this._offsets[21] = new float3(-1f, 0f, 0f);
            this._offsets[22] = new float3(-1f, 0f, -1f);
            this._offsets[23] = new float3(-1f, -1f, 1f);
            this._offsets[24] = new float3(-1f, -1f, 0f);
            this._offsets[25] = new float3(-1f, -1f, -1f);

            // ComponentGroupの設定
            this._playerGroup = GetComponentGroup(ComponentType.ReadOnly<Player>(), ComponentType.ReadOnly<SphereCollider>());
        }

        protected override void OnDestroyManager()
        {
            if (this._offsets.IsCreated) { this._offsets.Dispose(); }
            this.DisposeBuffers();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            this.DisposeBuffers();
            var handle = inputDeps;
            var playerGroupLength = this._playerGroup.CalculateLength();

            // ---------------------
            // Allocate Memory
            this._playerColliders = new NativeArray<SphereCollider>(
                playerGroupLength,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);

            this._playerHashmap = new NativeMultiHashMap<int, int>(
                playerGroupLength * MaxGridNum,
                Allocator.TempJob);

            // ---------------------
            // ComponentDataArray
            var copyPlayerColliderJobHandle = new CopyComponentData<SphereCollider>
            {
                Source = this._playerGroup.GetComponentDataArray<SphereCollider>(),
                Results = this._playerColliders,
            }.Schedule(playerGroupLength, 32, handle);

            // Hashmap Settings
            var playerHashmapJobHandle = new HashPositions
            {
                CellRadius = CellRadius,
                Offsets = this._offsets,
                SphereColliders = this._playerGroup.GetComponentDataArray<SphereCollider>(),
                Hashmap = this._playerHashmap.ToConcurrent(),
            }.Schedule(playerGroupLength, 32, handle);

            // ※Jobの依存関係の結合
            var handles = new NativeArray<JobHandle>(2, Allocator.Temp);
            handles[0] = copyPlayerColliderJobHandle;
            handles[1] = playerHashmapJobHandle;
            handle = JobHandle.CombineDependencies(handles);
            handles.Dispose();

            // ---------------------
            // Check Hit
            handle = new CheckHitJob
            {
                CellRadius = CellRadius,
                PlayerColliders = this._playerColliders,
                PlayerHashmap = this._playerHashmap,
            }.Schedule(this, handle);

            return handle;
        }

        #endregion // Protected Methods

        // ----------------------------------------------------
        #region // Private Methods

        void DisposeBuffers()
        {
            if (this._playerColliders.IsCreated) { this._playerColliders.Dispose(); }
            if (this._playerHashmap.IsCreated) { this._playerHashmap.Dispose(); }
        }

        #endregion // Private Methods
    }
}
