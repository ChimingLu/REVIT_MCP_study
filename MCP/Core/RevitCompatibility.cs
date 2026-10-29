using Autodesk.Revit.DB;

// Revit 2025+ 將 ElementId 從 int 改為 long
// 使用 IdType alias 和 GetIdValue() 擴充方法，讓同一份程式碼同時支援 2022-2026
#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// Revit API 跨版本相容性工具
    /// Nice3point.Revit.Sdk 會自動定義 REVIT2025_OR_GREATER 等預處理器符號
    /// </summary>
    internal static class RevitCompatibility
    {
        /// <summary>
        /// 取得 ElementId 的數值（2022-2024 回傳 int，2025+ 回傳 long）
        /// </summary>
        internal static IdType GetIdValue(this ElementId id)
        {
#if REVIT2025_OR_GREATER
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }

        /// <summary>
        /// 從數值建立 ElementId
        /// </summary>
        internal static ElementId ToElementId(this IdType value)
        {
            return new ElementId(value);
        }

        /// <summary>
        /// 規格是否為文字（2022+ 為 SpecTypeId.String.Text；2021 沒有這個常數，改看 TypeId 字串）
        /// </summary>
        internal static bool IsTextSpec(ForgeTypeId spec)
        {
            if (spec == null) return false;
#if REVIT2022_OR_GREATER
            return spec == SpecTypeId.String.Text;
#else
            return (spec.TypeId ?? "").Contains("string");
#endif
        }

        /// <summary>
        /// 參數定義是否為文字（2021 用 ParameterType，2022+ 用 GetDataType）
        /// </summary>
        internal static bool IsTextDefinition(Definition def)
        {
#if REVIT2022_OR_GREATER
            return def.GetDataType() == SpecTypeId.String.Text;
#else
            return def.ParameterType == ParameterType.Text;
#endif
        }

        /// <summary>
        /// 參數定義的資料型別名稱（2022+ 回傳 ForgeTypeId 字串，2021 回傳 ParameterType 名稱）
        /// </summary>
        internal static string DataTypeName(Definition def)
        {
#if REVIT2022_OR_GREATER
            return def.GetDataType().TypeId;
#else
            return def.ParameterType.ToString();
#endif
        }
    }
}
