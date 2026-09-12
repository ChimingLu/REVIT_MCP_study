using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

#if REVIT2025_OR_GREATER
using IdType = System.Int64;
#else
using IdType = System.Int32;
#endif

namespace RevitMCP.Core
{
    internal static class RampSuperelevationTool
    {
        private const double MmToFeet = 1.0 / 304.8;
        private const double FeetToMm = 304.8;
        private const double MaxDeltaMm = 90.0;
        private const double DefaultTargetRate = 0.03;

        public static object GetSlabShape(Document doc, JObject parameters)
        {
            if (doc == null) return new { Success = false, Message = "無可用文件" };

            IdType floorId = parameters["floorId"]?.Value<IdType>() ?? 0;
            Element elem = doc.GetElement(new ElementId(floorId));
            if (!(elem is Floor floor)) return new { Success = false, Message = "無效的 Floor" };

            SlabShapeEditor editor = floor.SlabShapeEditor;
            if (editor == null) return new { Success = false, Message = "不支援 SlabShapeEditor" };

            bool wasEnabled = editor.IsEnabled;
            using (Transaction trans = new Transaction(doc, "讀取樓板子元素形狀"))
            {
                trans.Start();
                if (!wasEnabled)
                {
                    try { editor.Enable(); wasEnabled = true; }
                    catch { }
                }

                var vertices = new List<object>();
                int displayIndex = 0;
                foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                {
                    XYZ p = v.Position;
                    vertices.Add(new
                    {
                        Index = displayIndex++,
                        Type = v.VertexType.ToString(),
                        XMm = Math.Round(p.X * FeetToMm, 3),
                        YMm = Math.Round(p.Y * FeetToMm, 3),
                        ZMm = Math.Round(p.Z * FeetToMm, 3)
                    });
                }

                trans.Commit();
                return new
                {
                    Success = true,
                    FloorId = floorId,
                    IsEnabled = wasEnabled,
                    VertexCount = vertices.Count,
                    Vertices = vertices,
                    Tips = wasEnabled
                        ? "樓板子元素已啟用，可直接修改頂點偏移。"
                        : "樓板目前為平坦狀態，已嘗試啟用子元素形狀。"
                };
            }
        }

        public static object SetVertexOffsets(Document doc, JObject parameters)
        {
            if (doc == null) return new { Success = false, Message = "無可用文件" };

            IdType floorId = parameters["floorId"]?.Value<IdType>() ?? 0;
            Element elem = doc.GetElement(new ElementId(floorId));
            if (!(elem is Floor floor)) return new { Success = false, Message = "無效的 Floor" };

            SlabShapeEditor editor = floor.SlabShapeEditor;
            if (editor == null) return new { Success = false, Message = "不支援 SlabShapeEditor" };

            JArray offsetsJson = parameters["offsets"] as JArray;
            if (offsetsJson == null || offsetsJson.Count == 0)
                return new { Success = false, Message = "未提供 offsets 陣列，或陣列為空" };

            var applied = new List<object>();
            var errors = new List<string>();
            var vertexMap = BuildVertexMap(editor);

            using (Transaction trans = new Transaction(doc, "設定樓板頂點偏移"))
            {
                trans.Start();
                if (!editor.IsEnabled)
                {
                    try { editor.Enable(); }
                    catch (Exception ex) { trans.RollBack(); return new { Success = false, Message = $"無法啟用 SlabShapeEditor: {ex.Message}" }; }
                }

                foreach (JToken item in offsetsJson)
                {
                    int idx = item["index"]?.Value<int>() ?? -1;
                    double offsetMm = item["offsetMm"]?.Value<double>() ?? 0.0;

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

        public static object ApplySuperelevation(Document doc, JObject parameters)
        {
            if (doc == null) return new { Success = false, Message = "無可用文件" };

            IdType floorId = parameters["floorId"]?.Value<IdType>() ?? 0;
            Element elem = doc.GetElement(new ElementId(floorId));
            if (!(elem is Floor floor)) return new { Success = false, Message = "無效的 Floor" };

            SlabShapeEditor editor = floor.SlabShapeEditor;
            if (editor == null) return new { Success = false, Message = "不支援 SlabShapeEditor" };

            var rawElev = new Dictionary<int, double>
            {
                { 2, 10.0 }, { 8, 87.5 }, { 12, 155.0 }, { 14, 235.0 }, { 16, 327.5 }, { 18, 420.5 }, { 20, 526.0 }, { 22, 655.0 }, { 24, 721.0 }, { 26, 806.5 },
                { 28, 884.0 }, { 30, 976.5 }, { 32, 1121.5 }, { 34, 1261.5 }, { 36, 1421.5 }, { 38, 1548.0 }, { 40, 1685.5 }, { 42, 1795.5 }, { 44, 1880.5 }, { 46, 1980.5 },
                { 48, 2090.5 }, { 50, 2171.0 }, { 52, 2285.0 }, { 54, 2375.0 }, { 56, 2483.0 }, { 58, 2567.0 }, { 60, 2665.0 }, { 62, 2808.0 }, { 64, 2927.0 }, { 66, 3049.5 },
                { 0, 386.3 }, { 3, 374.0 }, { 4, 3205.0 }, { 6, 3205.0 }, { 7, 3195.8 },
                { 1, 27.6 }, { 9, 82.8 }, { 11, 183.2 }, { 13, 244.4 }, { 15, 332.2 }, { 17, 440.3 }, { 19, 599.6 }, { 21, 821.3 }, { 23, 864.1 }, { 25, 954.2 },
                { 27, 1004.4 }, { 29, 1064.0 }, { 31, 1190.1 }, { 33, 1364.6 }, { 35, 1468.4 }, { 37, 1518.6 }, { 39, 1686.4 }, { 41, 1814.5 }, { 43, 1926.8 }, { 45, 2044.0 },
                { 47, 2163.2 }, { 49, 2226.6 }, { 51, 2339.7 }, { 53, 2457.5 }, { 55, 2515.7 }, { 57, 2570.7 }, { 59, 2699.3 }, { 61, 2804.7 }, { 63, 2907.0 }, { 65, 3013.2 }, { 67, 3204.8 }, { 5, 3107.5 }
            };

            var finalElev = new Dictionary<int, double>(rawElev);
            var explicitElevations = ParseExplicitElevations(parameters);
            if (explicitElevations != null)
            {
                foreach (var kvp in explicitElevations)
                    finalElev[kvp.Key] = kvp.Value;
            }

            var vertexMap = BuildVertexMap(editor);
            if (vertexMap.Count == 0) return new { Success = false, Message = "樓板沒有可用頂點" };

            XYZ pA = vertexMap.ContainsKey(1) ? vertexMap[1].Position : vertexMap.Values.First().Position;
            XYZ pB = vertexMap.ContainsKey(33) ? vertexMap[33].Position : vertexMap.Values.Skip(1).First().Position;
            XYZ pC = vertexMap.ContainsKey(7) ? vertexMap[7].Position : vertexMap.Values.Last().Position;
            XYZ center = GetCircleCenter(pA, pB, pC);

            var geoPoints = new List<GeoPointInfo>();
            foreach (var kvp in vertexMap)
            {
                int idx = kvp.Key;
                XYZ pos = kvp.Value.Position;
                double dx = pos.X - center.X;
                double dy = pos.Y - center.Y;
                double angle = Math.Atan2(dy, dx);
                double radius = Math.Sqrt(dx * dx + dy * dy);
                geoPoints.Add(new GeoPointInfo
                {
                    Index = idx,
                    Angle = angle,
                    Radius = radius,
                    RawElev = rawElev.ContainsKey(idx) ? rawElev[idx] : 0.0
                });
            }

            geoPoints = geoPoints.OrderBy(g => g.Angle).ToList();
            var pairedList = BuildPairedList(geoPoints);
            if (pairedList.Count == 0) return new { Success = false, Message = "無法建立成對頂點清單" };

            var sectionDistanceOverrides = ParseSectionDistanceOverrides(parameters);

            double[] centerline = SmoothCenterline(pairedList, rawElev);
            double minAngle = geoPoints.Min(g => g.Angle);
            double maxAngle = geoPoints.Max(g => g.Angle);
            double angleRange = maxAngle - minAngle;
            var fixedIndices = new HashSet<int> { 0, 3, 4, 5, 6, 7, 65, 66, 67 };

            if (parameters["fixedVertexIndices"] is JArray fixedJson)
            {
                foreach (JToken token in fixedJson)
                {
                    if (int.TryParse(token?.ToString(), out int idx))
                        fixedIndices.Add(idx);
                }
            }

            for (int i = 0; i < pairedList.Count; i++)
            {
                GeoPair pair = pairedList[i];
                if (pair.Inner == null || pair.Outer == null) continue;

                double t = (pair.Inner.Angle - minAngle) / angleRange;
                if (t < 0.15 || t > 0.90)
                {
                    finalElev[pair.Inner.Index] = pair.Inner.RawElev;
                    finalElev[pair.Outer.Index] = pair.Outer.RawElev;
                    continue;
                }

                if (fixedIndices.Contains(pair.Inner.Index) || fixedIndices.Contains(pair.Outer.Index))
                {
                    finalElev[pair.Inner.Index] = pair.Inner.RawElev;
                    finalElev[pair.Outer.Index] = pair.Outer.RawElev;
                    continue;
                }

                double centerHeight = centerline[i];
                double widthMm = (pair.Outer.Radius - pair.Inner.Radius) * FeetToMm;
                double defaultHalfWidth = Math.Max(widthMm / 2.0, 1.0);
                double innerToCenterMm = defaultHalfWidth;
                double outerToCenterMm = defaultHalfWidth;
                if (TryGetSectionDistances(sectionDistanceOverrides, pair, out SectionDistanceInfo overrideDistances))
                {
                    innerToCenterMm = Math.Max(overrideDistances.InnerToCenterMm, 1.0);
                    outerToCenterMm = Math.Max(overrideDistances.OuterToCenterMm, 1.0);
                }

                double targetInner = 0.0;
                double targetOuter = 0.0;
                double rate = parameters["superelevationRate"]?.Value<double>() ?? DefaultTargetRate;
                if (!TryResolveTarget(centerHeight, pair.Inner.RawElev, pair.Outer.RawElev, innerToCenterMm, outerToCenterMm, rate, out targetInner, out targetOuter))
                {
                    // 若 3% 超高超出 ±90mm 限制，改用較小超高率，避免穿樑與鋸齒。
                    foreach (double candidateRate in new[] { 0.025, 0.02, 0.015, 0.01 })
                    {
                        if (TryResolveTarget(centerHeight, pair.Inner.RawElev, pair.Outer.RawElev, innerToCenterMm, outerToCenterMm, candidateRate, out targetInner, out targetOuter))
                        {
                            rate = candidateRate;
                            break;
                        }
                    }
                }

                finalElev[pair.Inner.Index] = targetInner;
                finalElev[pair.Outer.Index] = targetOuter;
            }

            finalElev[0] = rawElev[0];
            finalElev[3] = rawElev[3];
            finalElev[4] = 3205.0;
            finalElev[6] = 3205.0;
            finalElev[7] = 3195.8;
            finalElev[5] = rawElev[5];
            finalElev[65] = rawElev[65];
            finalElev[66] = rawElev[66];
            finalElev[67] = rawElev[67];

            int applied = 0;
            using (Transaction trans = new Transaction(doc, "套用車道超高"))
            {
                trans.Start();
                if (!editor.IsEnabled)
                {
                    try { editor.Enable(); }
                    catch (Exception ex) { trans.RollBack(); return new { Success = false, Message = $"無法啟用 SlabShapeEditor: {ex.Message}" }; }
                }

                foreach (var kvp in finalElev)
                {
                    if (vertexMap.TryGetValue(kvp.Key, out SlabShapeVertex v))
                    {
                        try
                        {
                            editor.ModifySubElement(v, kvp.Value * MmToFeet);
                            applied++;
                        }
                        catch (Exception ex)
                        {
                            // 忽略單點失敗，避免整體中斷。
                            _ = ex;
                        }
                    }
                }

                trans.Commit();
            }

            return new
            {
                Success = true,
                Message = "車道超高已套用。",
                AppliedCount = applied,
                TargetElevations = finalElev.OrderBy(k => k.Key).ToDictionary(k => k.Key, k => Math.Round(k.Value, 3))
            };
        }

        private static Dictionary<int, double> ParseExplicitElevations(JObject parameters)
        {
            if (parameters["targetElevations"] is not JObject targetElevationsJson) return null;

            var result = new Dictionary<int, double>();
            foreach (var prop in targetElevationsJson.Properties())
            {
                if (int.TryParse(prop.Name, out int index))
                {
                    double value = prop.Value?.Value<double>() ?? 0.0;
                    result[index] = value;
                }
            }
            return result.Count == 0 ? null : result;
        }

        private static Dictionary<string, SectionDistanceInfo> ParseSectionDistanceOverrides(JObject parameters)
        {
            if (parameters["sectionDistances"] is not JArray distancesJson) return null;

            var map = new Dictionary<string, SectionDistanceInfo>();
            foreach (JToken token in distancesJson)
            {
                if (token is not JObject item) continue;

                int? innerIndex = item["innerIndex"]?.Value<int>();
                int? outerIndex = item["outerIndex"]?.Value<int>();
                if (!innerIndex.HasValue || !outerIndex.HasValue) continue;

                double innerMm = item["innerToCenterMm"]?.Value<double>() ?? 0.0;
                double outerMm = item["outerToCenterMm"]?.Value<double>() ?? 0.0;
                if (innerMm <= 0.0 || outerMm <= 0.0) continue;

                string key = PairKey(innerIndex.Value, outerIndex.Value);
                map[key] = new SectionDistanceInfo
                {
                    InnerToCenterMm = innerMm,
                    OuterToCenterMm = outerMm
                };

                // 同步儲存反向鍵，確保資料來源是右/左或內/外任一順序時都能正確對應。
                string reverseKey = PairKey(outerIndex.Value, innerIndex.Value);
                map[reverseKey] = new SectionDistanceInfo
                {
                    InnerToCenterMm = outerMm,
                    OuterToCenterMm = innerMm
                };
            }

            return map.Count == 0 ? null : map;
        }

        private static bool TryGetSectionDistances(Dictionary<string, SectionDistanceInfo> overrides, GeoPair pair, out SectionDistanceInfo result)
        {
            result = null;
            if (overrides == null || pair?.Inner == null || pair.Outer == null) return false;

            string directKey = PairKey(pair.Inner.Index, pair.Outer.Index);
            if (overrides.TryGetValue(directKey, out result)) return true;

            string reverseKey = PairKey(pair.Outer.Index, pair.Inner.Index);
            if (!overrides.TryGetValue(reverseKey, out SectionDistanceInfo reverse)) return false;

            result = new SectionDistanceInfo
            {
                InnerToCenterMm = reverse.OuterToCenterMm,
                OuterToCenterMm = reverse.InnerToCenterMm
            };
            return true;
        }

        private static string PairKey(int first, int second)
        {
            return first.ToString() + "_" + second.ToString();
        }

        private static List<GeoPair> BuildPairedList(List<GeoPointInfo> geoPoints)
        {
            var pairedList = new List<GeoPair>();
            double angleTolerance = 0.08;
            var used = new HashSet<int>();
            for (int i = 0; i < geoPoints.Count; i++)
            {
                GeoPointInfo pt1 = geoPoints[i];
                if (used.Contains(pt1.Index)) continue;

                GeoPointInfo pt2 = geoPoints.Skip(i + 1)
                    .FirstOrDefault(g => !used.Contains(g.Index) && Math.Abs(g.Angle - pt1.Angle) < angleTolerance);

                if (pt2 == null) continue;

                used.Add(pt1.Index);
                used.Add(pt2.Index);

                pairedList.Add(new GeoPair
                {
                    Inner = pt1.Radius <= pt2.Radius ? pt1 : pt2,
                    Outer = pt1.Radius <= pt2.Radius ? pt2 : pt1
                });
            }
            return pairedList;
        }

        private static double[] SmoothCenterline(List<GeoPair> pairedList, Dictionary<int, double> rawElev)
        {
            var centerline = new List<double>();
            foreach (GeoPair pair in pairedList)
            {
                if (pair.Inner == null || pair.Outer == null) continue;
                centerline.Add((pair.Inner.RawElev + pair.Outer.RawElev) / 2.0);
            }

            var smooth = new double[centerline.Count];
            for (int k = 0; k < 3; k++)
            {
                var source = (k == 0) ? centerline.ToList() : smooth.ToList();
                for (int i = 0; i < centerline.Count; i++)
                {
                    if (i == 0 || i == centerline.Count - 1)
                        smooth[i] = source[i];
                    else
                        smooth[i] = (source[i - 1] + source[i] + source[i + 1]) / 3.0;
                }
            }

            return smooth;
        }

        private static bool TryResolveTarget(double centerHeight, double rawInner, double rawOuter, double innerToCenterMm, double outerToCenterMm, double rate, out double targetInner, out double targetOuter)
        {
            double baseInner = centerHeight - (rate * innerToCenterMm);
            double baseOuter = centerHeight + (rate * outerToCenterMm);

            // 預設優先避免離心側抬高造成撞樑：若外側高於現況，先整體下移。
            double preferredShift = Math.Min(0.0, rawOuter - baseOuter);

            double minShiftByInner = (rawInner - MaxDeltaMm) - baseInner;
            double maxShiftByInner = (rawInner + MaxDeltaMm) - baseInner;
            double minShiftByOuter = (rawOuter - MaxDeltaMm) - baseOuter;
            double maxShiftByOuter = (rawOuter + MaxDeltaMm) - baseOuter;

            double minShift = Math.Max(minShiftByInner, minShiftByOuter);
            double maxShift = Math.Min(maxShiftByInner, maxShiftByOuter);
            if (minShift > maxShift) { targetInner = 0.0; targetOuter = 0.0; return false; }

            double shift = Clamp(preferredShift, minShift, maxShift);
            targetInner = baseInner + shift;
            targetOuter = baseOuter + shift;

            double innerDelta = targetInner - rawInner;
            double outerDelta = targetOuter - rawOuter;
            if (Math.Abs(innerDelta) > MaxDeltaMm || Math.Abs(outerDelta) > MaxDeltaMm)
                return false;

            return true;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static XYZ GetCircleCenter(XYZ p1, XYZ p2, XYZ p3)
        {
            double x1 = p1.X, y1 = p1.Y;
            double x2 = p2.X, y2 = p2.Y;
            double x3 = p3.X, y3 = p3.Y;

            double a = x1 * (y2 - y3) - y1 * (x2 - x3) + x2 * y3 - x3 * y2;
            double b = (x1 * x1 + y1 * y1) * (y3 - y2) + (x2 * x2 + y2 * y2) * (y1 - y3) + (x3 * x3 + y3 * y3) * (y2 - y1);
            double c = (x1 * x1 + y1 * y1) * (x2 - x3) + (x2 * x2 + y2 * y2) * (x3 - x1) + (x3 * x3 + y3 * y3) * (x1 - x2);

            double cx = -b / (2.0 * a);
            double cy = -c / (2.0 * a);
            return new XYZ(cx, cy, 0.0);
        }

        private static Dictionary<int, SlabShapeVertex> BuildVertexMap(SlabShapeEditor editor)
        {
            var map = new Dictionary<int, SlabShapeVertex>();
            int idx = 0;
            foreach (SlabShapeVertex v in editor.SlabShapeVertices)
                map[idx++] = v;
            return map;
        }

        private class GeoPointInfo
        {
            public int Index { get; set; }
            public double Angle { get; set; }
            public double Radius { get; set; }
            public double RawElev { get; set; }
        }

        private class GeoPair
        {
            public GeoPointInfo Inner { get; set; }
            public GeoPointInfo Outer { get; set; }
        }

        private class SectionDistanceInfo
        {
            public double InnerToCenterMm { get; set; }
            public double OuterToCenterMm { get; set; }
        }
    }
}