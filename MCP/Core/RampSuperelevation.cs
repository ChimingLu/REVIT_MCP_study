using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

// Revit 2025+ ElementId: int -> long
#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    /// <summary>
    /// 車道超高 (Ramp Superelevation) - SlabShapeEditor 子元素操控。
    ///
    /// 功能：
    ///   1. GetSlabShape        - 讀取樓板所有 SlabShapeVertex 座標與偏移，供 AI 診斷目前形狀。
    ///   2. SetVertexOffsets    - 批次修改指定頂點的 Z 軸偏移（mm）。
    ///   3. ApplySuperelevation - 依超高率、車道寬自動計算並套用超高漸變。
    ///
    /// 設計：獨立靜態類別，不修改 CommandExecutor 內部邏輯，遵循 FloorSlopeAnalyzer 慣例。
    /// </summary>
    internal static class RampSuperelevationTool
    {
        private const double MmToFeet = 1.0 / 304.8;
        private const double FeetToMm = 304.8;

        // -----------------------------------------------------------------------
        // GetSlabShape
        // -----------------------------------------------------------------------

        /// <summary>
        /// 讀取樓板子元素頂點清單。
        /// 參數：{ "floorId": number }
        /// </summary>
        public static object GetSlabShape(Document doc, JObject parameters)
        {
            if (doc == null)
                return Error("無可用文件。");

            IdType floorId = parameters["floorId"]?.Value<IdType>() ?? 0;
            if (!TryGetFloor(doc, floorId, out Floor floor, out string err))
                return Error(err);

            SlabShapeEditor editor = floor.SlabShapeEditor;
            if (editor == null)
                return Error($"樓板 {floorId} 不支援 SlabShapeEditor（可能為結構或異型板）。");

            bool wasEnabled = editor.IsEnabled;

            var vertices = new List<object>();
            foreach (SlabShapeVertex v in editor.SlabShapeVertices)
            {
                XYZ pt = v.Position;
#if REVIT2022_OR_GREATER
                vertices.Add(new
                {
                    Index = v.VertexIndex,
                    X = Math.Round(pt.X * FeetToMm, 1),
                    Y = Math.Round(pt.Y * FeetToMm, 1),
                    Z = Math.Round(pt.Z * FeetToMm, 1),
                    OffsetMm = Math.Round(v.CreaseDistanceFromFloorSurface * FeetToMm, 2),
                    Type = v.VertexType.ToString()
                });
#else
                // Revit 2021 以下：VertexIndex / CreaseDistanceFromFloorSurface 不存在
                vertices.Add(new
                {
                    Index = vertices.Count,  // 以枚舉順序作為替代索引
                    X = Math.Round(pt.X * FeetToMm, 1),
                    Y = Math.Round(pt.Y * FeetToMm, 1),
                    Z = Math.Round(pt.Z * FeetToMm, 1),
                    OffsetMm = 0.0,
                    Type = v.VertexType.ToString()
                });
#endif
            }

            var creases = new List<object>();
            foreach (SlabShapeCrease c in editor.SlabShapeCreases)
            {
                creases.Add(new
                {
                    Type = c.CreaseType.ToString(),
#if REVIT2022_OR_GREATER
                    IsNew = c.IsNewCrease
#else
                    IsNew = false
#endif
                });
            }

            return new
            {
                Success = true,
                FloorId = floorId,
                IsEnabled = wasEnabled,
                VertexCount = vertices.Count,
                Vertices = vertices,
                CreaseCount = creases.Count,
                Creases = creases,
                Tips = wasEnabled
                    ? "樓板子元素已啟用，可直接修改頂點偏移。"
                    : "樓板目前為平坦狀態 (IsEnabled=false)，請先在 Revit 中啟用子元素形狀編輯，或呼叫 set_floor_vertex_offsets 會自動嘗試啟用。"
            };
        }

        // -----------------------------------------------------------------------
        // SetVertexOffsets
        // -----------------------------------------------------------------------

        /// <summary>
        /// 批次修改指定頂點的 Z 偏移。
        /// 參數：{ "floorId": number, "offsets": [{ "index": int, "offsetMm": double }, ...] }
        /// </summary>
        public static object SetVertexOffsets(Document doc, JObject parameters)
        {
            if (doc == null)
                return Error("無可用文件。");

            IdType floorId = parameters["floorId"]?.Value<IdType>() ?? 0;
            if (!TryGetFloor(doc, floorId, out Floor floor, out string err))
                return Error(err);

            JArray offsetsJson = parameters["offsets"] as JArray;
            if (offsetsJson == null || offsetsJson.Count == 0)
                return Error("未提供 offsets 陣列，或陣列為空。");

            SlabShapeEditor editor = floor.SlabShapeEditor;
            if (editor == null)
                return Error($"樓板 {floorId} 不支援 SlabShapeEditor。");

            var applied = new List<object>();
            var errors = new List<string>();

            using (Transaction trans = new Transaction(doc, "車道超高 - 設定頂點偏移"))
            {
                trans.Start();

                if (!editor.IsEnabled)
                {
                    try { editor.Enable(); }
                    catch (Exception ex)
                    {
                        trans.RollBack();
                        return Error($"無法啟用 SlabShapeEditor: {ex.Message}");
                    }
                }

                var vertexMap = BuildVertexMap(editor);

                foreach (JToken item in offsetsJson)
                {
                    int idx = item["index"]?.Value<int>() ?? -1;
                    double offsetMm = item["offsetMm"]?.Value<double>() ?? 0;

                    if (!vertexMap.TryGetValue(idx, out SlabShapeVertex vertex))
                    {
                        errors.Add($"找不到索引 {idx} 的頂點，已略過。");
                        continue;
                    }

                    try
                    {
                        editor.ModifySubElement(vertex, offsetMm * MmToFeet);
                        applied.Add(new { Index = idx, OffsetMm = offsetMm });
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"頂點 {idx} 偏移設定失敗: {ex.Message}");
                    }
                }

                trans.Commit();
            }

            return new
            {
                Success = errors.Count == 0,
                FloorId = floorId,
                AppliedCount = applied.Count,
                Applied = applied,
                Errors = errors
            };
        }

        // -----------------------------------------------------------------------
        // ApplySuperelevation
        // -----------------------------------------------------------------------

        /// <summary>
        /// 依超高率自動計算並套用車道超高漸變。
        ///
        /// 參數：
        ///   floorId                   - 目標樓板 ElementId
        ///   laneWidthMm               - 車道寬（mm）
        ///   superelevationRate        - 橫向超高率（%），例如 4.0 代表 4%
        ///   innerEdgeVertexIds        - 向心側（內側）全段頂點索引
        ///   outerEdgeVertexIds        - 離心側（外側）全段頂點索引
        ///   innerTransitionVertexIds  - 內側漸變段頂點索引（可選）
        ///   outerTransitionVertexIds  - 外側漸變段頂點索引（可選）
        ///   transitionRatio           - 漸變段達到的超高比例（0~1，預設 0.5）
        ///   keepInnerFlat             - 內側頂點是否保持 0 偏移（預設 true）
        /// </summary>
        public static object ApplySuperelevation(Document doc, JObject parameters)
        {
            if (doc == null)
                return Error("無可用文件。");

            IdType floorId = parameters["floorId"]?.Value<IdType>() ?? 0;
            if (!TryGetFloor(doc, floorId, out Floor floor, out string err))
                return Error(err);

            double laneWidthMm = parameters["laneWidthMm"]?.Value<double>() ?? 0;
            if (laneWidthMm <= 0)
                return Error("laneWidthMm 必須大於 0。");

            double superelevationRate = parameters["superelevationRate"]?.Value<double>() ?? 0;
            if (superelevationRate <= 0 || superelevationRate > 20)
                return Error("superelevationRate 應為 0~20（%）的正數。");

            bool keepInnerFlat = parameters["keepInnerFlat"]?.Value<bool>() ?? true;
            double transitionRatio = Clamp(parameters["transitionRatio"]?.Value<double>() ?? 0.5, 0.0, 1.0);

            // 外側最大 Z 偏移量 (mm)：車道寬 x 超高率
            double deltaZmm = laneWidthMm * (superelevationRate / 100.0);

            List<int> innerIds = ParseIdList(parameters["innerEdgeVertexIds"]);
            List<int> outerIds = ParseIdList(parameters["outerEdgeVertexIds"]);
            List<int> innerTransIds = ParseIdList(parameters["innerTransitionVertexIds"]);
            List<int> outerTransIds = ParseIdList(parameters["outerTransitionVertexIds"]);

            SlabShapeEditor editor = floor.SlabShapeEditor;
            if (editor == null)
                return Error($"樓板 {floorId} 不支援 SlabShapeEditor。");

            var applied = new List<object>();
            var errors = new List<string>();

            using (Transaction trans = new Transaction(doc, "車道超高 - 自動套用超高漸變"))
            {
                trans.Start();

                if (!editor.IsEnabled)
                {
                    try { editor.Enable(); }
                    catch (Exception ex)
                    {
                        trans.RollBack();
                        return Error($"無法啟用 SlabShapeEditor: {ex.Message}");
                    }
                }

                var vertexMap = BuildVertexMap(editor);

                // 內側全段（向心側：保持平坦或輕微降低）
                double innerFullOffset = keepInnerFlat ? 0.0 : -(deltaZmm / 2.0);
                ApplyGroup(editor, vertexMap, innerIds, innerFullOffset, applied, errors, "inner-full");

                // 外側全段（離心側：抬高到最大超高量）
                ApplyGroup(editor, vertexMap, outerIds, deltaZmm, applied, errors, "outer-full");

                // 漸變段：依 transitionRatio 線性插值
                double innerTransOffset = keepInnerFlat ? 0.0 : -(deltaZmm * transitionRatio / 2.0);
                ApplyGroup(editor, vertexMap, innerTransIds, innerTransOffset, applied, errors, "inner-transition");
                ApplyGroup(editor, vertexMap, outerTransIds, deltaZmm * transitionRatio, applied, errors, "outer-transition");

                trans.Commit();
            }

            return new
            {
                Success = errors.Count == 0,
                FloorId = floorId,
                LaneWidthMm = laneWidthMm,
                SuperelevationRate = superelevationRate,
                DeltaZmm = Math.Round(deltaZmm, 1),
                TransitionRatio = transitionRatio,
                AppliedCount = applied.Count,
                Applied = applied,
                Errors = errors
            };
        }

        // -----------------------------------------------------------------------
        // 私有輔助方法
        // -----------------------------------------------------------------------

        private static void ApplyGroup(
            SlabShapeEditor editor,
            Dictionary<int, SlabShapeVertex> vertexMap,
            List<int> ids,
            double offsetMm,
            List<object> applied,
            List<string> errors,
            string groupLabel)
        {
            foreach (int idx in ids)
            {
                if (!vertexMap.TryGetValue(idx, out SlabShapeVertex vertex))
                {
                    errors.Add($"[{groupLabel}] 找不到頂點索引 {idx}。");
                    continue;
                }
                try
                {
                    editor.ModifySubElement(vertex, offsetMm * MmToFeet);
                    applied.Add(new { Group = groupLabel, Index = idx, OffsetMm = Math.Round(offsetMm, 1) });
                }
                catch (Exception ex)
                {
                    errors.Add($"[{groupLabel}] 頂點 {idx} 失敗: {ex.Message}");
                }
            }
        }

        private static Dictionary<int, SlabShapeVertex> BuildVertexMap(SlabShapeEditor editor)
        {
            var map = new Dictionary<int, SlabShapeVertex>();
#if REVIT2022_OR_GREATER
            foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                map[v.VertexIndex] = v;
#else
            // Revit 2021：VertexIndex 不存在，以枚舉順序作為索引
            int idx = 0;
            foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                map[idx++] = v;
#endif
            return map;
        }

        private static bool TryGetFloor(Document doc, IdType id, out Floor floor, out string errorMsg)
        {
            floor = null;
            errorMsg = null;
            if (id == 0) { errorMsg = "未提供有效的 floorId。"; return false; }
            Element elem = doc.GetElement(new ElementId(id));
            if (elem is Floor f) { floor = f; return true; }
            errorMsg = $"元素 ID {id} 不是 Floor（實際類型: {elem?.GetType().Name ?? "null"}）。";
            return false;
        }

        private static List<int> ParseIdList(JToken token)
        {
            var list = new List<int>();
            if (token is JArray arr)
                foreach (JToken t in arr)
                    try { list.Add(t.Value<int>()); } catch { }
            return list;
        }

        private static double Clamp(double value, double min, double max) =>
            value < min ? min : value > max ? max : value;

        private static object Error(string message) =>
            new { Success = false, Message = message };
    }
}
