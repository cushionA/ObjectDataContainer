using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MyTool
{
    /// <summary>
    /// 固定サイズ・スワップ削除版のキャラクターデータ辞書
    /// 最大容量を事前に確保しリサイズしない
    /// 削除時は削除部分と今の最後の要素を入れ替えることでデータが断片化しない
    /// ハッシュテーブルによりGetComponent不要でデータアクセスが可能
    /// </summary>
    //[ContainerSetting(
    //    structType: new[] {
    //        typeof(CharacterBaseInfo),
    //        typeof(CharacterAtkStatus),
    //        typeof(CharacterDefStatus),
    //        typeof(SolidData),
    //        typeof(CharacterStateInfo),
    //        typeof(MoveStatus),
    //        typeof(CharacterColdLog)
    //    },
    //    classType: new[] { typeof(BaseController) }
    //)]
    public unsafe partial class ObjectDataContainer : IDisposable
    {
        #region 定数

        /// <summary>
        /// デフォルトの最大容量
        /// </summary>
        private const int DEFAULT_MAX_CAPACITY = 130;

        /// <summary>
        /// バケット数（ハッシュテーブルのサイズ）
        /// </summary>
        private const int BUCKET_COUNT = 191;  // 素数を使用

        #endregion

        #region コンストラクタ

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="maxCapacity">最大容量（デフォルト: 100）</param>
        /// <param name="allocator">メモリアロケータ（デフォルト: Persistent）</param>
        public ObjectDataContainer(int maxCapacity = DEFAULT_MAX_CAPACITY, Allocator allocator = Allocator.Persistent)
        {
            InitializeContainer(maxCapacity, allocator);
        }

        /// <summary>
        /// コンテナの初期化処理（Source Generatorで実装される）
        /// </summary>
        partial void InitializeContainer(int maxCapacity, Allocator allocator);

        #endregion

        #region ユーティリティ

        /// <summary>
        /// すべてのエントリをクリア
        /// </summary>
        public void Clear()
        {
            ClearAllData();
        }

        /// <summary>
        /// データクリア処理（Source Generatorで実装される）
        /// </summary>
        partial void ClearAllData();

        #endregion

        // ContainsKey と ContainsKeyByHash は生成コードで完全実装される

        #region IDisposable

        /// <summary>
        /// リソースの解放
        /// </summary>
        public void Dispose()
        {
            DisposeResources();
        }

        /// <summary>
        /// リソース解放処理（Source Generatorで実装される）
        /// </summary>
        partial void DisposeResources();

        #endregion
    }
}