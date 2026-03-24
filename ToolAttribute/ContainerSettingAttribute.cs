using System;
using System.Reflection;

namespace ODC.Attributes
{
    /// <summary>
    /// コンテナで管理するデータ型を指定するアトリビュート。
    /// partial classに付与すると、Source Generatorが高性能なデータコンテナを自動生成する。
    /// structTypeのみ、classTypeのみの指定も可能。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class ContainerSettingAttribute : Attribute
    {
        /// <summary>
        /// 管理対象のアンマネージド型。
        /// 一括確保したメモリ領域で管理される。
        /// </summary>
        public Type[] StructType { get; }

        /// <summary>
        /// 管理対象のマネージド型。
        /// 配列で管理される。
        /// </summary>
        public Type[] ClassType { get; }

        /// <summary>
        /// コンテナの設定を指定します。
        /// structTypeとclassTypeの少なくとも一方を指定する必要があります。
        /// </summary>
        /// <param name="structType">管理するアンマネージド構造体型の配列（省略時は空配列）</param>
        /// <param name="classType">管理するクラス型の配列（省略時は空配列）</param>
        public ContainerSettingAttribute(Type[] structType = null, Type[] classType = null)
        {
            structType = structType ?? Array.Empty<Type>();
            classType = classType ?? Array.Empty<Type>();

            for (int i = 0; i < structType.Length; i++)
            {
                if (!IsUnmanagedType(structType[i]))
                {
                    throw new ArgumentException(
                        $"structType[{i}] ({structType[i].Name}) はunmanaged型である必要があります。" +
                        "参照型フィールドを含む構造体は指定できません。");
                }
            }

            for (int i = 0; i < classType.Length; i++)
            {
                if (!classType[i].IsClass)
                {
                    throw new ArgumentException(
                        $"classType[{i}] ({classType[i].Name}) はクラスである必要があります。" +
                        "構造体やインターフェースは指定できません。");
                }
            }

            StructType = structType;
            ClassType = classType;
        }

        /// <summary>
        /// 型がUnmanagedであるかチェックするメソッド。
        /// プリミティブ型、enum型、および全フィールドがunmanagedな構造体をtrueとする。
        /// </summary>
        private static bool IsUnmanagedType(Type type)
        {
            if (!type.IsValueType)
                return false;

            if (type.IsPrimitive || type.IsEnum)
                return true;

            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!IsUnmanagedType(field.FieldType))
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// コンテナのデフォルト初期容量を指定するアトリビュート。
    /// 生成されるコンストラクタのデフォルト引数値に反映される。
    /// </summary>
    /// <example>
    /// [ContainerSetting(structType: new[] { typeof(Health) })]
    /// [InitialCapacity(256)]
    /// public partial class EnemyContainer { }
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class InitialCapacityAttribute : Attribute
    {
        /// <summary>デフォルトの初期容量</summary>
        public int Capacity { get; }

        public InitialCapacityAttribute(int capacity)
        {
            if (capacity <= 0 || capacity > 10000)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity),
                    "容量は1～10000の範囲で指定してください。");
            }
            Capacity = capacity;
        }
    }

    /// <summary>
    /// 生成されるコンテナクラスにXMLドキュメントコメントを付与するアトリビュート。
    /// </summary>
    /// <example>
    /// [ContainerSetting(structType: new[] { typeof(Health) })]
    /// [ContainerDescription("敵キャラクターのデータを一括管理するコンテナ")]
    /// public partial class EnemyContainer { }
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class ContainerDescriptionAttribute : Attribute
    {
        /// <summary>コンテナの説明文</summary>
        public string Description { get; }

        public ContainerDescriptionAttribute(string description)
        {
            Description = description ?? throw new ArgumentNullException(nameof(description));
        }
    }

    /// <summary>
    /// ReadOnly/Spanアクセサの生成を抑制するアトリビュート。
    /// JobSystem連携が不要な軽量コンテナに使用する。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class DisableReadOnlyViewAttribute : Attribute
    {
    }

    /// <summary>
    /// ForEachメソッドの生成を有効化するアトリビュート。
    /// 全データ型を引数に取るデリゲートベースの一括処理メソッドが生成される。
    /// structはref引数、classはそのまま渡される。
    /// </summary>
    /// <example>
    /// [ContainerSetting(structType: new[] { typeof(Health), typeof(Movement) }, classType: new[] { typeof(AI) })]
    /// [ForEachGenerate]
    /// public partial class EnemyContainer { }
    /// // 以下が生成される:
    /// // public delegate void ForEachDelegate(ref Health health, ref Movement movement, AI ai);
    /// // public void ForEach(ForEachDelegate action) { ... }
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class ForEachGenerateAttribute : Attribute
    {
    }
}
