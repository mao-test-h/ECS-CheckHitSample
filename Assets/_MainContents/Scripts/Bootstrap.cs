//#define ENABLE_DEBUG

namespace MainContents
{
    using UnityEngine;

    using Unity.Entities;
    using Unity.Collections;
    using Unity.Collections.LowLevel.Unsafe;
    using Unity.Rendering;
    using Unity.Mathematics;
    using Unity.Transforms;

    using UnityRandom = UnityEngine.Random;

    /// <summary>
    /// プレイヤー識別子
    /// </summary>
    public struct Player : IComponentData { }

    /// <summary>
    /// 当たり判定確認用 entity
    /// </summary>
    public struct CheckHit : IComponentData { }


    public sealed class Bootstrap : MonoBehaviour
    {
        // ------------------------------
        #region // Constants

        /// <summary>
        /// 最大オブジェクト数
        /// </summary>
#if ENABLE_DEBUG
        const int MaxObjectNum = 16;
#else
        const int MaxObjectNum = 10000;
#endif

        /// <summary>
        /// プレイヤースケール
        /// </summary>
        const float PlayerScale = 5f;

        /// <summary>
        /// 当たり判定確認用オブジェクト スケール
        /// </summary>
        const float CheckHitScale = 1f;

        /// <summary>
        /// 球体形状の衝突プリミティブの半径
        /// </summary>
        const float SphereColliderRadius = 0.5f;

        /// <summary>
        /// 表示領域のサイズ
        /// </summary>
        readonly Vector3 Range = new Vector3(64f, 64f, 64f);

        #endregion // Constants

        // ------------------------------
        #region // Private Fields(Editable)
#pragma warning disable 0649

        /// <summary>
        /// プレイヤー 表示データ
        /// </summary>
        [SerializeField] MeshInstanceRenderer _playerLook;

        /// <summary>
        /// 当たり判定確認用 表示データ
        /// </summary>
        [SerializeField] MeshInstanceRenderer _checkHitLook;

        /// <summary>
        /// trueなら「衝突判定(空間分割)」を有効
        /// </summary>
        [SerializeField] bool _isSplitSpace = false;

#pragma warning restore 0649
        #endregion // Private Fields(Editable)


        // ----------------------------------------------------
        #region // Unity Events

        /// <summary>
        /// MonoBehaviour.Start
        /// </summary>
        void Start()
        {
            // Worldの作成
            World.Active = new World("Sample World");
            EntityManager entityManager = World.Active.CreateManager<EntityManager>();
            World.Active.CreateManager(typeof(EndFrameTransformSystem));
            World.Active.CreateManager(typeof(RenderingSystemBootstrap));
            World.Active.CreateManager(typeof(PlayerMove), Camera.main.transform, this.Range);
            World.Active.CreateManager(typeof(ColliderUpdate));

            if (this._isSplitSpace)
            {
                // 衝突判定(空間分割)
                World.Active.CreateManager(typeof(SplitSpaceCollisionSystem));
            }
            else
            {
                // 衝突判定(総当たり)
                World.Active.CreateManager(typeof(CollisionSystem));
            }
            var destroyBarrier = World.Active.CreateManager<DestroyBarrier>();
            World.Active.CreateManager(typeof(DestroySystem), destroyBarrier);

            ScriptBehaviourUpdateOrder.UpdatePlayerLoop(World.Active);

            // create player entity.
            var playerArchetype = entityManager.CreateArchetype(
                ComponentType.Create<Position>(), ComponentType.Create<Rotation>(), ComponentType.Create<Scale>(),
                ComponentType.Create<Player>(), ComponentType.Create<SphereCollider>(),
                ComponentType.Create<MeshInstanceRenderer>());
            var playerEntity = entityManager.CreateEntity(playerArchetype);
            entityManager.SetSharedComponentData(playerEntity, this._playerLook);
            entityManager.SetComponentData(playerEntity, new Position { Value = new float3(0) });
            entityManager.SetComponentData(playerEntity, new Scale { Value = new float3(PlayerScale) });
            entityManager.SetComponentData(playerEntity, new SphereCollider { Radius = SphereColliderRadius * PlayerScale });

            // create check hit entities.
            var checkHitArchetype = entityManager.CreateArchetype(
                ComponentType.Create<Position>(), ComponentType.Create<Rotation>(),
                ComponentType.Create<CheckHit>(), ComponentType.Create<SphereCollider>(),
                ComponentType.Create<Destroyable>(),
                ComponentType.Create<MeshInstanceRenderer>());

            // Entityの生成(各種ComponentData/SharedComponentDataの初期化)
            // やっている事としては以下のリンクを参照。
            // - https://qiita.com/pCYSl5EDgo/items/18f1827a5b323a7712d7
            var entities = new NativeArray<Entity>(MaxObjectNum, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            try
            {
                entities[0] = entityManager.CreateEntity(checkHitArchetype);

                // MeshInstanceRendererに対するデータの設定
                entityManager.SetSharedComponentData(entities[0], this._checkHitLook);
                unsafe
                {
                    var ptr = (Entity*)NativeArrayUnsafeUtility.GetUnsafePtr(entities);
                    var rest = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<Entity>(ptr + 1, entities.Length - 1, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref rest, AtomicSafetyHandle.GetTempUnsafePtrSliceHandle());
#endif
                    entityManager.Instantiate(entities[0], rest);
                }

                // 各種ComponentDataの設定
                for (int i = 0; i < MaxObjectNum; ++i)
                {
#if ENABLE_DEBUG
                    // テスト用. 横一列に配置
                    entityManager.SetComponentData(entities[i], new Position { Value = new float3(5 + (i * 10), 0, 0) });
#else
                    // ランダムに配置
                    entityManager.SetComponentData(entities[i], new Position { Value = this.GetRandomPosition() });
#endif

                    entityManager.SetComponentData(entities[i], new SphereCollider { Radius = SphereColliderRadius * CheckHitScale });
                }
            }
            finally
            {
                entities.Dispose();
            }
        }

        /// <summary>
        /// MonoBehaviour.OnDestroy
        /// </summary>
        void OnDestroy()
        {
            World.DisposeAllWorlds();
        }

        #endregion // Unity Events

        // ----------------------------------------------------
        #region // Private Methods

        /// <summary>
        /// ランダムな位置の取得
        /// </summary>
        float3 GetRandomPosition()
        {
            var halfX = Range.x / 2;
            var halfY = Range.y / 2;
            var halfZ = Range.z / 2;
            return new float3(
                UnityRandom.Range(-halfX, halfX),
                UnityRandom.Range(-halfY, halfY),
                UnityRandom.Range(-halfZ, halfZ));
        }

        #endregion // Private Methods
    }
}
