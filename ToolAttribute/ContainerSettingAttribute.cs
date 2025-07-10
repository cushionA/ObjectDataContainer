using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace ToolAttribute.GenContainer
{
    /// <summary>
    /// Unity内で使用するアトリビュート
    /// 配列で指定したデータ型を管理するデータコンテナを作成する。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ContainerSettingAttribute : Attribute
    {
        /// <summary>
        /// 管理対象のアンマネージ型。
        /// 一括確保したメモリ領域で管理される。
        /// </summary>
        public Type[] StructType { get; }

        /// <summary>
        /// 管理対象のマネージ型。
        /// 配列で管理される。
        /// </summary>
        public Type[] ClassType { get; }

        public ContainerSettingAttribute(Type[] structType, Type[] classType)
        {
            // 構造体の型がアンマネージであることの確認。
            for ( int i = 0; i < structType.Length; i++ )
            {
                if ( !IsUnmanagedType(structType[i]) )
                {
                    throw new ArgumentException("structTypeはunmanaged型である必要があります");
                }
            }

            // classTypeがクラスオブジェクトであることも確認。
            for ( int i = 0; i < classType.Length; i++ )
            {
                // 実行時に検証
                if ( !classType[i].IsClass )
                {
                    throw new ArgumentException("classTypeはクラスである必要があります");
                }
            }

            StructType = structType;
            ClassType = classType;
        }

        /// <summary>
        /// 型がUnmanageであるかチェックするためのメソッド
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private static bool IsUnmanagedType(Type type)
        {
            // 値型でない場合は false
            if ( !type.IsValueType )
                return false;

            // プリミティブ型は unmanaged
            if ( type.IsPrimitive )
                return true;

            // 各フィールドを再帰的にチェック
            foreach ( var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) )
            {
                if ( !IsUnmanagedType(field.FieldType) )
                    return false;
            }

            return true;
        }
    }
}