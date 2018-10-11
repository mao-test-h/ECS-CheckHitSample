namespace MainContents
{
    using UnityEngine;

    using Unity.Entities;
    using Unity.Transforms;
    using Unity.Mathematics;
    using Unity.Collections;

    /// <summary>
    /// プレイヤー操作(仮)
    /// </summary>
    /// <remarks>TransformSystem(EndFrameTransformSystem)よりも前に実行する必要がある</remarks>
    [UpdateBefore(typeof(EndFrameTransformSystem))]
    public sealed class PlayerMove : ComponentSystem
    {
        // ------------------------------
        #region // Private Fields

        readonly EntityArchetypeQuery query = new EntityArchetypeQuery
        {
            Any = System.Array.Empty<ComponentType>(),
            All = new ComponentType[] { ComponentType.Create<Position>(), ComponentType.Create<Player>() },
            None = System.Array.Empty<ComponentType>(),
        };
        NativeList<EntityArchetype> foundArchetypeList = new NativeList<EntityArchetype>(Allocator.Persistent);

        // LookAt用にMainCameraを期待
        Transform _cameraTrs;

        // 表示領域
        float3 _halfRange;

        #endregion // Private Fields

        // ----------------------------------------------------
        #region // Public Methods

        public PlayerMove(Transform cameraTrs, Vector3 range)
        {
            this._cameraTrs = cameraTrs;
            this._halfRange = range / 2f;
        }

        #endregion // Public Methods

        // ----------------------------------------------------
        #region // Protected Methods

        protected override void OnDestroyManager() => foundArchetypeList.Dispose();

        protected override void OnUpdate()
        {
            // Chunk Iterationでプレイヤーの座標データを取得
            var manager = EntityManager;
            manager.AddMatchingArchetypes(this.query, this.foundArchetypeList);
            var PositionTypeRW = manager.GetArchetypeChunkComponentType<Position>(isReadOnly: false);
            using (var chunks = manager.CreateArchetypeChunkArray(this.foundArchetypeList, Allocator.TempJob))
            {
                for (int i = 0; i < chunks.Length; ++i)
                {
                    var positions = chunks[i].GetNativeArray(PositionTypeRW);
                    for (int j = 0; j < positions.Length; ++j)
                    {
                        var pos = positions[j].Value;

                        // とりあえずは当たり判定確認用に動けるようにする
                        // ※操作は超適当
                        const float Speed = 0.5f;

                        // A, Dキーで左右移動
                        var x = pos.x + (Input.GetAxis("Horizontal") * Speed);
                        if (x >= this._halfRange.x) { x = this._halfRange.x; }
                        if (x <= -this._halfRange.x) { x = -this._halfRange.x; }

                        // ↑, ↓キーで上下移動
                        var y = pos.y + (Input.GetAxis("Vertical") * Speed);
                        if (y >= this._halfRange.y) { y = this._halfRange.y; }
                        if (y <= -this._halfRange.y) { y = -this._halfRange.y; }

                        // W, Sキーで前進後退
                        var z = pos.z + (Input.GetAxis("Depth") * Speed);
                        if (z >= this._halfRange.z) { z = this._halfRange.z; }
                        if (z <= -this._halfRange.z) { z = -this._halfRange.z; }

                        pos = new float3(x, y, z);
                        this._cameraTrs.LookAt(pos);
                        positions[j] = new Position { Value = pos };
                    }
                }
            }
        }

        #endregion // Protected Methods
    }
}
