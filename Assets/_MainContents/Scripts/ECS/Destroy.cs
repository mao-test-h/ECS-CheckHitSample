// ▽参照
// https://github.com/Unity-Technologies/AnotherThreadECS
// AnotherThreadECS/Assets/Scripts/ECSDestroy.cs
namespace MainContents
{
    using Unity.Entities;
    using Unity.Jobs;
    using Unity.Collections;
    using Unity.Burst;

    /// <summary>
    /// 破棄管理用データ
    /// </summary>
    public struct Destroyable : IComponentData
    {
        public byte Killed; // boolean
        public static Destroyable Kill { get { return new Destroyable { Killed = 1, }; } }
    }

    /// <summary>
    /// 破棄処理用 BarrierSystem
    /// </summary>
    [UpdateBefore(typeof(Unity.Rendering.MeshInstanceRendererSystem))]
    public sealed class DestroyBarrier : BarrierSystem { }

    /// <summary>
    /// 破棄処理
    /// </summary>
    [UpdateAfter(typeof(CollisionUpdateGroup))]
    public sealed class DestroySystem : JobComponentSystem
    {
        // ------------------------------
        #region // Jobs

        [BurstCompile]
        struct DestroyJob : IJobProcessComponentDataWithEntity<Destroyable>
        {
            public EntityCommandBuffer.Concurrent CommandBuffer;
            public void Execute(Entity entity, int index, [ReadOnly]ref Destroyable destroyable)
            {
                if (destroyable.Killed == 0) { return; }
                this.CommandBuffer.DestroyEntity(index, entity);
            }
        }

        #endregion // Jobs

        // ------------------------------
        #region // Private Fields

        DestroyBarrier _destroyBarrier;

        #endregion // Private Fields

        // ----------------------------------------------------
        #region // Public Methods

        public DestroySystem(DestroyBarrier destroyBarrier) => this._destroyBarrier = destroyBarrier;

        #endregion // Public Methods

        // ----------------------------------------------------
        #region // Protected Methods

        protected override JobHandle OnUpdate(JobHandle inputDep) => new DestroyJob
        {
            CommandBuffer = this._destroyBarrier.CreateCommandBuffer().ToConcurrent(),
        }.Schedule(this, inputDep);

        #endregion // Protected Methods
    }
}
