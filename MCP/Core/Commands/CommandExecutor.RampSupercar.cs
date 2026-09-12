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
    /// <summary>
    /// 超跑車道幾何適宜性檢查命令執行器
    ///
    /// 依據建築技術規則 §61 與超跑底盤幾何極限，執行三類衝突判定：
    /// 1. 坡度合規性（vs. 1:6 法規上限）
    /// 2. 轉折點幾何（起坡 Sag / 坡頂 Crest，vs. 進入角 / 跨越角）
    /// 3. 平面轉彎半徑（vs. 5m 法規下限與超跑掃掠空間）
    ///
    /// Domain SOP: domain/ramp-supercar-check.md
    /// </summary>
    public partial class CommandExecutor
    {
        // -------------------------------------------------------
        // 超跑幾何規格常數表
        // -------------------------------------------------------

        private class VehicleSpec
        {
            public string DisplayName { get; set; }
            public double ApproachAngleDeg { get; set; }
            public double DepartureAngleDeg { get; set; }
            public double BreakoverAngleDeg { get; set; }
            public double WheelbaseMm { get; set; }
            public double GroundClearanceMm { get; set; }
            public double VehicleWidthMm { get; set; }
        }

        private static readonly Dictionary<string, VehicleSpec> VehiclePresets =
            new Dictionary<string, VehicleSpec>(StringComparer.OrdinalIgnoreCase)
        {
            {
                "mclaren_750s", new VehicleSpec
                {
                    DisplayName = "McLaren 750S",
                    ApproachAngleDeg = 8.3,
                    DepartureAngleDeg = 13.3,
                    BreakoverAngleDeg = 11.5,
                    WheelbaseMm = 2450,
                    GroundClearanceMm = 107,
                    VehicleWidthMm = 2161,
                }
            },
            {
                "porsche_gt3rs", new VehicleSpec
                {
                    DisplayName = "Porsche 911 GT3 RS",
                    ApproachAngleDeg = 6.2,
                    DepartureAngleDeg = 9.8,
                    BreakoverAngleDeg = 10.6,
                    WheelbaseMm = 2457,
                    GroundClearanceMm = 100,
                    VehicleWidthMm = 1900,
                }
            },
            {
                "ferrari_296gtb", new VehicleSpec
                {
                    DisplayName = "Ferrari 296 GTB",
                    ApproachAngleDeg = 7.5,
                    DepartureAngleDeg = 11.0,
                    BreakoverAngleDeg = 10.5,
                    WheelbaseMm = 2600,
                    GroundClearanceMm = 115,
                    VehicleWidthMm = 1958,
                }
            },
            {
                "lamborghini_revuelto", new VehicleSpec
                {
                    DisplayName = "Lamborghini Revuelto",
                    ApproachAngleDeg = 8.5,
                    DepartureAngleDeg = 12.0,
                    BreakoverAngleDeg = 9.5,
                    WheelbaseMm = 2779,
                    GroundClearanceMm = 110,
                    VehicleWidthMm = 2024,
                }
            },
        };

        // 法規常數（建技規 §61）
        private const double RAMP_MAX_SLOPE_RATIO = 1.0 / 6.0;      // 1:6 = 16.67%
        private const double RAMP_MAX_SLOPE_DEG = 9.463;            // arctan(1/6)
        private const double RAMP_STRICT_SLOPE_DEG = 7.125;         // 1:8，服務≥50輛
        private const double RAMP_TRANSITION_SLOPE_DEG = 4.764;     // 1:12，緩和坡參考
        private const double RAMP_MIN_INNER_RADIUS_M = 5.0;         // 最小內側曲線半徑
        private const double RAMP_SINGLE_LANE_WIDTH_M = 3.5;        // 單車道最小寬度
        private const double RAMP_DOUBLE_LANE_WIDTH_M = 5.5;        // 雙車道最小寬度
        private const double FEET_TO_MM = 304.8;
        private const double SAFETY_MARGIN_DEG = 1.0;               // 動態安全餘裕

        // -------------------------------------------------------
        // 工具方法：解析車輛規格
        // -------------------------------------------------------

        private VehicleSpec ResolveVehicleSpec(JObject parameters)
        {
            string presetKey = parameters["vehiclePreset"]?.Value<string>() ?? "porsche_gt3rs";

            if (!VehiclePresets.TryGetValue(presetKey, out VehicleSpec spec))
                spec = VehiclePresets["porsche_gt3rs"];

            // 複製一份，避免修改靜態資料
            spec = new VehicleSpec
            {
                DisplayName = spec.DisplayName,
                ApproachAngleDeg = spec.ApproachAngleDeg,
                DepartureAngleDeg = spec.DepartureAngleDeg,
                BreakoverAngleDeg = spec.BreakoverAngleDeg,
                WheelbaseMm = spec.WheelbaseMm,
                GroundClearanceMm = spec.GroundClearanceMm,
                VehicleWidthMm = spec.VehicleWidthMm,
            };

            // 套用自訂覆寫
            var custom = parameters["customVehicle"] as JObject;
            if (custom != null)
            {
                if (custom["approachAngleDeg"] != null)
                    spec.ApproachAngleDeg = custom["approachAngleDeg"].Value<double>();
                if (custom["departureAngleDeg"] != null)
                    spec.DepartureAngleDeg = custom["departureAngleDeg"].Value<double>();
                if (custom["breakoverAngleDeg"] != null)
                    spec.BreakoverAngleDeg = custom["breakoverAngleDeg"].Value<double>();
                if (custom["wheelbaseMm"] != null)
                    spec.WheelbaseMm = custom["wheelbaseMm"].Value<double>();
                if (custom["groundClearanceMm"] != null)
                    spec.GroundClearanceMm = custom["groundClearanceMm"].Value<double>();
                if (custom["vehicleWidthMm"] != null)
                    spec.VehicleWidthMm = custom["vehicleWidthMm"].Value<double>();
                spec.DisplayName = "自訂車型";
            }

            return spec;
        }

        /// <summary>
        /// 收集目標元素（Floor + Ramp）。若未指定 elementIds，則自動從當前文件查詢。
        /// </summary>
        private List<Element> CollectRampElements(Document doc, JObject parameters)
        {
            var result = new List<Element>();
            var idsToken = parameters["elementIds"] as JArray;

            if (idsToken != null && idsToken.Count > 0)
            {
                foreach (var idToken in idsToken)
                {
                    var el = doc.GetElement(new ElementId(idToken.Value<IdType>()));
                    if (el != null) result.Add(el);
                }
                return result;
            }

            // 自動收集：Floor（樓板）
            result.AddRange(
                new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfCategory(BuiltInCategory.OST_Floors)
                    .WhereElementIsNotElementType()
                    .Cast<Element>());

            // 自動收集：Ramp（坡道）
            result.AddRange(
                new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfCategory(BuiltInCategory.OST_Ramps)
                    .WhereElementIsNotElementType()
                    .Cast<Element>());

            return result;
        }

        /// <summary>
        /// 計算元素頂面的主要坡度角（度）。
        ///
        /// 利用 SlabShapeEditor 子元素頂點（Floor）或 BoundingBox 高差（Ramp）估算。
        /// 回傳坡面法向量與 XY 平面夾角，單位為度。
        /// </summary>
        private bool TryGetElementSlopeDeg(Element element, out double slopeDeg, out string method)
        {
            slopeDeg = 0.0;
            method = "unknown";

            if (element is Floor floor)
            {
                // 嘗試透過頂面法向量計算坡度
                Options geoOptions = new Options { DetailLevel = ViewDetailLevel.Fine };
                GeometryElement geoElem = floor.get_Geometry(geoOptions);
                if (geoElem == null) return false;

                double maxSlopeDeg = 0.0;
                bool found = false;

                foreach (GeometryObject geoObj in geoElem)
                {
                    if (!(geoObj is Solid solid) || solid.Volume < 1e-9) continue;

                    foreach (Face face in solid.Faces)
                    {
                        if (!(face is PlanarFace pf)) continue;
                        XYZ normal = pf.FaceNormal;
                        // 僅取朝上頂面（法向量 Z 分量 > 0.3，即坡度 < 約 73°）
                        if (normal.Z <= 0.3) continue;

                        // 坡度角 = 與水平面夾角 = arctan(水平分量 / Z 分量)
                        double horizontalComponent = Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y);
                        double angleDeg = Math.Atan2(horizontalComponent, normal.Z) * (180.0 / Math.PI);

                        if (angleDeg > maxSlopeDeg)
                        {
                            maxSlopeDeg = angleDeg;
                            found = true;
                        }
                    }
                }

                if (found)
                {
                    slopeDeg = maxSlopeDeg;
                    method = "PlanarFace法向量";
                    return true;
                }

                // Fallback：BoundingBox 高差估算
                BoundingBoxXYZ bbox = floor.get_BoundingBox(null);
                if (bbox != null)
                {
                    double heightDiff = Math.Abs(bbox.Max.Z - bbox.Min.Z) * FEET_TO_MM;
                    double horizontalLen = Math.Sqrt(
                        Math.Pow((bbox.Max.X - bbox.Min.X) * FEET_TO_MM, 2) +
                        Math.Pow((bbox.Max.Y - bbox.Min.Y) * FEET_TO_MM, 2));
                    if (horizontalLen > 1.0)
                    {
                        slopeDeg = Math.Atan2(heightDiff, horizontalLen) * (180.0 / Math.PI);
                        method = "BoundingBox估算";
                        return true;
                    }
                }
                return false;
            }

            if (element.Category?.Id.GetIdValue() == (long)BuiltInCategory.OST_Ramps)
            {
                // Ramp 元素：以 BoundingBox 高差估算
                BoundingBoxXYZ bbox = element.get_BoundingBox(null);
                if (bbox == null) return false;

                double heightDiff = Math.Abs(bbox.Max.Z - bbox.Min.Z) * FEET_TO_MM;
                double horizontalLen = Math.Sqrt(
                    Math.Pow((bbox.Max.X - bbox.Min.X) * FEET_TO_MM, 2) +
                    Math.Pow((bbox.Max.Y - bbox.Min.Y) * FEET_TO_MM, 2));
                if (horizontalLen < 1.0) return false;

                slopeDeg = Math.Atan2(heightDiff, horizontalLen) * (180.0 / Math.PI);
                method = "Ramp BoundingBox估算";
                return true;
            }

            return false;
        }

        /// <summary>
        /// 套用圖形覆蓋著色。
        /// status: "pass"=綠, "warning"=黃, "fail"=紅, "clear"=清除
        /// </summary>
        private void ApplyRampStatusColor(Document doc, Element element, string status)
        {
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();

            Color color;
            switch (status.ToLower())
            {
                case "pass":    color = new Color(0, 180, 80);   break;
                case "warning": color = new Color(255, 165, 0);  break;
                case "fail":    color = new Color(220, 30, 30);  break;
                default:
                    doc.ActiveView.SetElementOverrides(element.Id, new OverrideGraphicSettings());
                    return;
            }

            ogs.SetSurfaceForegroundPatternColor(color);
            ogs.SetSurfaceForegroundPatternVisible(true);
            var fillPatternId = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(fp => fp.GetFillPattern().IsSolidFill)?.Id;
            if (fillPatternId != null && fillPatternId != ElementId.InvalidElementId)
                ogs.SetSurfaceForegroundPatternId(fillPatternId);

            doc.ActiveView.SetElementOverrides(element.Id, ogs);
        }

        // -------------------------------------------------------
        // 命令 1：車道坡度合規性檢查
        // -------------------------------------------------------

        private object CheckRampSlopeCompliance(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            bool strictMode = parameters["strictMode"]?.Value<bool>() ?? false;
            bool applyColor = parameters["applyColorOverride"]?.Value<bool>() ?? true;

            double warningThresholdDeg = strictMode ? RAMP_STRICT_SLOPE_DEG : RAMP_MAX_SLOPE_DEG;
            string warningRatioStr = strictMode ? "1:8（雙向/大量）" : "1:6（建技規 §61）";

            var elements = CollectRampElements(doc, parameters);
            var results = new List<object>();
            var passCount = 0;
            var warnCount = 0;
            var failCount = 0;

            using (Transaction trans = new Transaction(doc, "超跑車道坡度合規著色"))
            {
                trans.Start();
                foreach (var element in elements)
                {
                    if (!TryGetElementSlopeDeg(element, out double slopeDeg, out string method))
                        continue;

                    double slopePct = Math.Tan(slopeDeg * Math.PI / 180.0) * 100.0;
                    double slopeRatio = slopePct > 0.01 ? 1.0 / (slopePct / 100.0) : 9999;
                    string status;

                    if (slopeDeg <= RAMP_TRANSITION_SLOPE_DEG + 0.01)
                    {
                        status = "pass";
                        passCount++;
                    }
                    else if (slopeDeg <= warningThresholdDeg + 0.01)
                    {
                        status = "warning";
                        warnCount++;
                    }
                    else
                    {
                        status = "fail";
                        failCount++;
                    }

                    if (applyColor)
                        ApplyRampStatusColor(doc, element, status);

                    results.Add(new
                    {
                        ElementId = element.Id.GetIdValue(),
                        ElementType = element.Category?.Name ?? "Unknown",
                        SlopeDeg = Math.Round(slopeDeg, 2),
                        SlopePct = Math.Round(slopePct, 2),
                        SlopeRatioApprox = $"1:{Math.Round(slopeRatio, 1)}",
                        Status = status,
                        LegalLimit = warningRatioStr,
                        Method = method,
                    });
                }
                trans.Commit();
            }

            return new
            {
                Summary = new
                {
                    Total = results.Count,
                    Pass = passCount,
                    Warning = warnCount,
                    Fail = failCount,
                    StrictMode = strictMode,
                },
                Results = results,
                Message = $"完成車道坡度合規檢查，共 {results.Count} 個元素：" +
                          $"通過 {passCount}、警告 {warnCount}、不合規 {failCount}。",
            };
        }

        // -------------------------------------------------------
        // 命令 2：轉折點幾何衝突分析
        // -------------------------------------------------------

        private object CheckRampTransitionGeometry(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            bool applyColor = parameters["applyColorOverride"]?.Value<bool>() ?? true;
            VehicleSpec vehicle = ResolveVehicleSpec(parameters);
            double safetyMarginDeg = parameters["safetyMarginDeg"]?.Value<double>() ?? SAFETY_MARGIN_DEG;
            double transitionSlopeDeg = parameters["transitionRampSlopeDeg"]?.Value<double>() ?? RAMP_TRANSITION_SLOPE_DEG;

            var elements = CollectRampElements(doc, parameters);

            // 取得各元素的坡度，依 Z 高程排序後計算相鄰差值
            var slopeData = new List<(Element el, double slopeDeg)>();
            foreach (var el in elements)
            {
                if (TryGetElementSlopeDeg(el, out double slopeDeg, out _))
                    slopeData.Add((el, slopeDeg));
            }

            // 依元素底部高程排序（模擬車輛行駛順序）
            slopeData.Sort((a, b) =>
            {
                var bboxA = a.el.get_BoundingBox(null);
                var bboxB = b.el.get_BoundingBox(null);
                double zA = bboxA?.Min.Z ?? 0;
                double zB = bboxB?.Min.Z ?? 0;
                return zA.CompareTo(zB);
            });

            var transitionResults = new List<object>();
            var overallStatus = "pass";

            using (Transaction trans = new Transaction(doc, "超跑車道轉折點幾何著色"))
            {
                trans.Start();

                // 分析相鄰元素之間的轉折
                for (int i = 0; i < slopeData.Count - 1; i++)
                {
                    var current = slopeData[i];
                    var next = slopeData[i + 1];

                    // 判斷轉折類型
                    // Sag（起坡）：坡度由小變大（從平地進入下坡）→ 前保桿風險 → 對比進入角
                    // Crest（坡頂）：坡度由大變小（上坡頂端進入平地）→ 車底卡底風險 → 對比跨越角
                    double deltaSlopeDeg = next.slopeDeg - current.slopeDeg;
                    string transitionType;
                    double vehicleLimitDeg;

                    if (deltaSlopeDeg >= 0)
                    {
                        // Sag：坡度增加，使用進入角
                        transitionType = "Sag（起坡點）";
                        vehicleLimitDeg = vehicle.ApproachAngleDeg;
                    }
                    else
                    {
                        // Crest：坡度減少，使用跨越角
                        transitionType = "Crest（坡頂）";
                        vehicleLimitDeg = vehicle.BreakoverAngleDeg;
                    }

                    double absDelta = Math.Abs(deltaSlopeDeg);
                    double effectiveLimit = vehicleLimitDeg - safetyMarginDeg;
                    double marginDeg = effectiveLimit - absDelta;

                    string status;
                    if (marginDeg >= 0)
                        status = "pass";
                    else if (absDelta <= vehicleLimitDeg)
                        status = "warning";   // 超過安全餘裕但未超角度極限
                    else
                        status = "fail";

                    // 計算建議緩和坡長度（必須 > 最大軸距）
                    double requiredTransitionLengthMm = 0;
                    string transitionDesignAdvice = "";
                    if (status != "pass")
                    {
                        // 緩和坡設計：插入 transitionSlopeDeg 坡度段
                        // 驗證兩段轉折角均 < 進入角/跨越角
                        double delta1 = transitionSlopeDeg;                       // 平地→緩和坡
                        double delta2 = Math.Abs(absDelta - transitionSlopeDeg);  // 緩和坡→主坡

                        // 緩和坡長度取最大軸距 + 25% 安全餘裕
                        double maxWheelbaseMm = VehiclePresets.Values.Max(v => v.WheelbaseMm);
                        requiredTransitionLengthMm = maxWheelbaseMm * 1.25;

                        bool transitionFeasible = delta1 < effectiveLimit && delta2 < effectiveLimit;
                        transitionDesignAdvice = transitionFeasible
                            ? $"插入坡度 {transitionSlopeDeg:F2}° 的緩和坡，長度 ≥ {requiredTransitionLengthMm:F0}mm（建議 ≥ 3500mm）"
                            : $"單段緩和坡不足，需考慮多段分解或拋物線豎曲線（長度 ≥ 5000mm）";

                        if (overallStatus == "pass") overallStatus = status;
                        if (status == "fail") overallStatus = "fail";
                    }

                    if (applyColor)
                    {
                        ApplyRampStatusColor(doc, current.el, status);
                        ApplyRampStatusColor(doc, next.el, status == "fail" ? "fail" : status);
                    }

                    transitionResults.Add(new
                    {
                        FromElementId = current.el.Id.GetIdValue(),
                        ToElementId = next.el.Id.GetIdValue(),
                        TransitionType = transitionType,
                        FromSlopeDeg = Math.Round(current.slopeDeg, 2),
                        ToSlopeDeg = Math.Round(next.slopeDeg, 2),
                        DeltaAngleDeg = Math.Round(absDelta, 2),
                        VehicleLimit = vehicleLimitDeg,
                        SafetyMarginDeg = safetyMarginDeg,
                        EffectiveLimit = Math.Round(effectiveLimit, 2),
                        MarginDeg = Math.Round(marginDeg, 2),
                        Status = status,
                        RequiredTransitionLengthMm = status != "pass" ? Math.Round(requiredTransitionLengthMm, 0) : (double?)null,
                        TransitionDesignAdvice = status != "pass" ? transitionDesignAdvice : null,
                    });
                }

                trans.Commit();
            }

            return new
            {
                Vehicle = new
                {
                    vehicle.DisplayName,
                    ApproachAngleDeg = vehicle.ApproachAngleDeg,
                    BreakoverAngleDeg = vehicle.BreakoverAngleDeg,
                    WheelbaseMm = vehicle.WheelbaseMm,
                },
                SafetyMarginDeg = safetyMarginDeg,
                OverallStatus = overallStatus,
                TransitionCount = transitionResults.Count,
                Transitions = transitionResults,
                Message = $"分析 {transitionResults.Count} 個坡道轉折點，整體判定：{overallStatus}。",
            };
        }

        // -------------------------------------------------------
        // 命令 3：平面轉彎半徑分析
        // -------------------------------------------------------

        private object CheckRampTurningRadius(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            bool applyColor = parameters["applyColorOverride"]?.Value<bool>() ?? true;
            VehicleSpec vehicle = ResolveVehicleSpec(parameters);

            var elements = CollectRampElements(doc, parameters);
            var results = new List<object>();

            using (Transaction trans = new Transaction(doc, "超跑車道轉彎半徑著色"))
            {
                trans.Start();
                foreach (var element in elements)
                {
                    // 以 BoundingBox 尺寸估算是否為彎道段
                    BoundingBoxXYZ bbox = element.get_BoundingBox(null);
                    if (bbox == null) continue;

                    double dimX = Math.Abs(bbox.Max.X - bbox.Min.X) * FEET_TO_MM;
                    double dimY = Math.Abs(bbox.Max.Y - bbox.Min.Y) * FEET_TO_MM;

                    // 元素兩方向尺寸差異過大（細長型）→ 直線段，不分析轉彎
                    double aspectRatio = Math.Max(dimX, dimY) / Math.Max(Math.Min(dimX, dimY), 1.0);
                    if (aspectRatio > 4.0) continue;

                    // 估算元素內側曲線半徑（以較短方向作為近似）
                    // 注意：精確分析需 Revit Curve 物件，此處為 BoundingBox 近似
                    double shortSideMm = Math.Min(dimX, dimY);
                    double estimatedInnerRadiusM = shortSideMm / 2000.0; // 轉換為公尺

                    // 超跑掃掠外側半徑估算（簡化阿克曼模型）
                    // R_outer ≈ √(R_inner² + L²) + w/2
                    double L = vehicle.WheelbaseMm / 1000.0;
                    double w = vehicle.VehicleWidthMm / 1000.0;
                    double R = estimatedInnerRadiusM;
                    double outerSweptRadiusM = Math.Sqrt(R * R + L * L) + w / 2.0;
                    double sweptWidthMm = (outerSweptRadiusM - (R - w / 2.0)) * 1000.0;

                    string status;
                    if (estimatedInnerRadiusM < RAMP_MIN_INNER_RADIUS_M)
                        status = "fail";
                    else if (sweptWidthMm > RAMP_DOUBLE_LANE_WIDTH_M * 1000.0 * 0.9)
                        status = "warning";
                    else
                        status = "pass";

                    if (applyColor)
                        ApplyRampStatusColor(doc, element, status);

                    results.Add(new
                    {
                        ElementId = element.Id.GetIdValue(),
                        EstimatedInnerRadiusM = Math.Round(estimatedInnerRadiusM, 2),
                        LegalMinInnerRadiusM = RAMP_MIN_INNER_RADIUS_M,
                        RadiusCompliant = estimatedInnerRadiusM >= RAMP_MIN_INNER_RADIUS_M,
                        OuterSweptRadiusM = Math.Round(outerSweptRadiusM, 2),
                        SweptWidthMm = Math.Round(sweptWidthMm, 0),
                        Status = status,
                        Note = "內側半徑為 BoundingBox 近似值，精確值需以 Revit Curve API 量測",
                    });
                }
                trans.Commit();
            }

            return new
            {
                Vehicle = new
                {
                    vehicle.DisplayName,
                    WheelbaseMm = vehicle.WheelbaseMm,
                    VehicleWidthMm = vehicle.VehicleWidthMm,
                },
                LegalMinInnerRadiusM = RAMP_MIN_INNER_RADIUS_M,
                AnalyzedCount = results.Count,
                Results = results,
                Message = results.Count > 0
                    ? $"分析 {results.Count} 個可能的轉彎段元素。" +
                      "注意：半徑為 BoundingBox 近似，精確值請以 Autodesk Vehicle Tracking 驗證。"
                    : "未偵測到明顯轉彎段（所有元素為細長直線型）。",
            };
        }

        // -------------------------------------------------------
        // 命令 4：彙整報告與批次著色
        // -------------------------------------------------------

        private object GenerateRampComplianceReport(JObject parameters)
        {
            bool includeRecommendations = parameters["includeDesignRecommendations"]?.Value<bool>() ?? true;
            VehicleSpec vehicle = ResolveVehicleSpec(parameters);

            // 依序執行三項檢查
            var slopeResult = CheckRampSlopeCompliance(parameters) as dynamic;
            var transitionResult = CheckRampTransitionGeometry(parameters) as dynamic;
            var turningResult = CheckRampTurningRadius(parameters) as dynamic;

            // 彙整整體判定
            string overallStatus = "pass";
            if (transitionResult != null)
            {
                var ts = (string)((dynamic)transitionResult).OverallStatus;
                if (ts == "fail") overallStatus = "fail";
                else if (ts == "warning" && overallStatus == "pass") overallStatus = "warning";
            }

            var recommendations = new List<string>();
            if (includeRecommendations)
            {
                recommendations.Add(
                    $"坡度設計：主坡道坡度建議 ≤ 1:8（7.12°），確保對 {vehicle.DisplayName}（進入角 {vehicle.ApproachAngleDeg}°）有充足安全餘裕。");
                recommendations.Add(
                    $"緩和坡（必要時）：在 1:6 主坡道起坡點與坡頂插入坡度 ≤ 4.76°（1:12）之緩和坡，長度 ≥ 3500mm（涵蓋最長軸距 Lamborghini Revuelto 2779mm）。");
                recommendations.Add(
                    "坡頂豎曲線（高標設計）：採用長度 ≥ 5000mm 之拋物線豎曲線取代銳角折點，徹底消除跨越角衝突風險。");
                recommendations.Add(
                    "截水溝蓋板：坡道轉折點處截水溝改採 FRP 蓋板（D400 等級），模組化安裝確保與周邊鋪面零高低差。");
                recommendations.Add(
                    "車道鋪面：選用表面平整度 ±3mm 內的樹脂系防滑耐磨地坪，禁用大塊卵石洗石子等起伏過大面材。");
                recommendations.Add(
                    "實體減速丘：禁止設置高度 > 50mm 之傳統減速丘。若需物理減速，改用 Speed Table（高度 < 50mm、坡率 ≤ 1:12）並重新進行本 SOP 檢核。");
            }

            return new
            {
                ReportTitle = $"超跑車道幾何適宜性評估報告 — {vehicle.DisplayName}",
                GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                VehicleSpec = new
                {
                    vehicle.DisplayName,
                    ApproachAngleDeg = vehicle.ApproachAngleDeg,
                    DepartureAngleDeg = vehicle.DepartureAngleDeg,
                    BreakoverAngleDeg = vehicle.BreakoverAngleDeg,
                    WheelbaseMm = vehicle.WheelbaseMm,
                    GroundClearanceMm = vehicle.GroundClearanceMm,
                    VehicleWidthMm = vehicle.VehicleWidthMm,
                },
                OverallStatus = overallStatus,
                SlopeCheck = slopeResult,
                TransitionCheck = transitionResult,
                TurningRadiusCheck = turningResult,
                DesignRecommendations = includeRecommendations ? recommendations : null,
                LegalBasis = new[]
                {
                    "建築技術規則建築設計施工編 §61：車道坡度 ≤ 1:6（9.46°）",
                    "建築技術規則建築設計施工編 §61：車道內側曲線半徑 ≥ 5m",
                    "建築技術規則建築設計施工編 §61：單車道寬度 ≥ 3.5m、雙車道 ≥ 5.5m",
                },
                Message = $"彙整報告完成，整體評定：{overallStatus}。" +
                          "詳細資料請查閱 SlopeCheck、TransitionCheck、TurningRadiusCheck 各欄位。",
            };
        }
    }
}
