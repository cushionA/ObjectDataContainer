using System;
using System.Collections.Generic;
using System.Text;

namespace ToolCodeGenerator
{
    /// <summary>
    /// Unity内で使用するアトリビュート
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ContainerSettingAttribute : Attribute
    {
        public Type[] StructType { get; }
        public Type[] ClassType { get; }

        public ContainerSettingAttribute(Type[] structType, Type[] classType)
        {
            StructType = structType;
            ClassType = classType;
        }
    }
}
