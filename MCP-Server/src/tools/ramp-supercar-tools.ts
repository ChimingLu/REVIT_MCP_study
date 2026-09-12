/**
 * 超跑車道幾何適宜性檢查工具
 *
 * 依據建築技術規則 §61（坡度 1:6、曲線半徑 5m）與超跑底盤幾何極限，
 * 在 Revit 模型中自動執行三類幾何衝突判定，並產生彩色標示報告。
 *
 * Domain SOP：domain/ramp-supercar-check.md
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

const VEHICLE_PRESET_DESCRIPTION =
    "預設超跑車型。可選值：\"mclaren_750s\"（進入角 8.3°）、" +
    "\"porsche_gt3rs\"（進入角 6.2°，最嚴苛基準）、" +
    "\"ferrari_296gtb\"（進入角 <8.0°）、" +
    "\"lamborghini_revuelto\"（進入角 <9.0°，軸距最長）。" +
    "未指定時預設採用最嚴苛基準 porsche_gt3rs。";

const CUSTOM_VEHICLE_SCHEMA = {
    type: "object" as const,
    description:
        "自訂超跑幾何參數。若同時提供 vehiclePreset 與 customVehicle，" +
        "customVehicle 的欄位會覆寫 preset 對應值。",
    properties: {
        approachAngleDeg: {
            type: "number",
            description: "進入角（度）：前輪接地點→前保桿最低點切線角度",
        },
        departureAngleDeg: {
            type: "number",
            description: "離去角（度）：後輪接地點→後擴散器最低點切線角度",
        },
        breakoverAngleDeg: {
            type: "number",
            description: "跨越角（度）：前後輪接地點→車底中央最低點切線角度",
        },
        wheelbaseMm: {
            type: "number",
            description: "軸距（mm）：前軸中心至後軸中心距離",
        },
        groundClearanceMm: {
            type: "number",
            description: "最小離地高度（mm）：車底最低點至地面垂直距離",
        },
        vehicleWidthMm: {
            type: "number",
            description: "車寬（mm）",
        },
    },
};

export const rampSupercarTools: Tool[] = [
    {
        name: "check_ramp_slope_compliance",
        description:
            "掃描指定車道元素（Floor 或 Ramp）的縱向坡度，對比建技規 §61 之 1:6（9.46°）上限。" +
            "回傳每段坡度的百分比、角度（°）、1:N 比例與法規合規狀態（Pass/Warning/Fail），" +
            "並以圖形覆蓋著色：合規為綠色、介於 1:8~1:6 為黃色警告、超過 1:6 為紅色。" +
            "當使用者提到車道坡度、坡道法規、是否超過1:6、ramp slope 時呼叫。",
        inputSchema: {
            type: "object",
            properties: {
                elementIds: {
                    type: "array",
                    description:
                        "目標車道元素的 Element ID 清單（Floor 或 Ramp）。" +
                        "若省略，工具自動掃描當前視圖中所有 OST_Floors 與 OST_Ramps 元素。",
                    items: { type: "number" },
                },
                strictMode: {
                    type: "boolean",
                    description:
                        "嚴格模式。設為 true 時，以 1:8（7.12°）作為警告門檻（適用服務 ≥50 輛的坡道）；" +
                        "預設 false（以 1:6 為警告門檻）。",
                },
                applyColorOverride: {
                    type: "boolean",
                    description: "是否套用圖形覆蓋著色。預設 true。",
                },
            },
            required: [],
        },
    },
    {
        name: "check_ramp_transition_geometry",
        description:
            "分析車道各段坡度的連接點（轉折點），計算轉折角（Δθ），" +
            "並對比指定超跑的進入角（Approach Angle）與跨越角（Breakover Angle）。" +
            "轉折點分為兩類：起坡點 Sag（平地→下坡，前保桿風險）與坡頂 Crest（上坡→平地，車底卡底風險）。" +
            "回傳每個轉折點的類型、轉折角、與超跑幾何的安全餘裕（°），" +
            "以及建議的緩和坡坡度、最小緩和坡長度（mm）。" +
            "當使用者提到進入角、跨越角、緩和坡、前保桿會不會撞、坡頂卡底 時呼叫。",
        inputSchema: {
            type: "object",
            properties: {
                elementIds: {
                    type: "array",
                    description:
                        "目標車道元素的 Element ID 清單。若省略，工具自動偵測當前視圖中的車道元素。",
                    items: { type: "number" },
                },
                vehiclePreset: {
                    type: "string",
                    description: VEHICLE_PRESET_DESCRIPTION,
                    enum: [
                        "mclaren_750s",
                        "porsche_gt3rs",
                        "ferrari_296gtb",
                        "lamborghini_revuelto",
                    ],
                },
                customVehicle: CUSTOM_VEHICLE_SCHEMA,
                safetyMarginDeg: {
                    type: "number",
                    description:
                        "安全餘裕（度）。轉折角超過（超跑進入角 - safetyMarginDeg）時即視為警告。" +
                        "預設 1.0°（代表至少保留 1° 的動態安全餘裕，避免滿載下沉與施工誤差的影響）。",
                },
                transitionRampSlopeDeg: {
                    type: "number",
                    description:
                        "指定緩和坡坡度（度）。預設 4.76°（對應 1:12 坡度，" +
                        "常借用無障礙坡道標準作為超跑車道過渡坡的設計基準）。",
                },
                applyColorOverride: {
                    type: "boolean",
                    description: "是否套用圖形覆蓋著色。預設 true。",
                },
            },
            required: [],
        },
    },
    {
        name: "check_ramp_turning_radius",
        description:
            "量測車道轉彎段的內側曲線半徑，並以簡化阿克曼轉向模型估算指定超跑的最大掃掠寬度，" +
            "對比法規 §61 之 5m 最小內側半徑，以及車道寬度內的剩餘側向淨空。" +
            "回傳每個轉彎段的實際內側半徑（m）、法規合規狀態、超跑掃掠外側半徑（m）、" +
            "掃掠寬度（mm）與牆面（路緣）側向淨空（mm）。" +
            "當使用者提到轉彎半徑、曲線半徑、轉彎會不會撞牆、掃掠軌跡 時呼叫。",
        inputSchema: {
            type: "object",
            properties: {
                elementIds: {
                    type: "array",
                    description: "目標車道轉彎段元素的 Element ID 清單。",
                    items: { type: "number" },
                },
                vehiclePreset: {
                    type: "string",
                    description: VEHICLE_PRESET_DESCRIPTION,
                    enum: [
                        "mclaren_750s",
                        "porsche_gt3rs",
                        "ferrari_296gtb",
                        "lamborghini_revuelto",
                    ],
                },
                customVehicle: CUSTOM_VEHICLE_SCHEMA,
                laneWidthMm: {
                    type: "number",
                    description:
                        "車道有效寬度（mm）。預設依法規單車道 3500mm / 雙車道 5500mm 自動判斷。",
                },
                applyColorOverride: {
                    type: "boolean",
                    description: "是否套用圖形覆蓋著色。預設 true。",
                },
            },
            required: [],
        },
    },
    {
        name: "generate_ramp_compliance_report",
        description:
            "彙整車道坡度合規（check_ramp_slope_compliance）、" +
            "轉折點幾何衝突（check_ramp_transition_geometry）、" +
            "轉彎半徑分析（check_ramp_turning_radius）三項結果，" +
            "批次套用圖形覆蓋（綠/黃/紅），並回傳結構化的超跑車道適宜性評估報告。" +
            "報告包含：整體通過/警告/失敗判定、各類衝突點清單、" +
            "設計建議（緩和坡長度、建議最大坡度、豎曲線參數）。" +
            "當使用者提到出報告、綜合檢查、車道適宜性報告、能不能停超跑 時呼叫。",
        inputSchema: {
            type: "object",
            properties: {
                elementIds: {
                    type: "array",
                    description:
                        "目標車道元素的 Element ID 清單。若省略，工具自動掃描當前視圖中所有車道元素。",
                    items: { type: "number" },
                },
                vehiclePreset: {
                    type: "string",
                    description: VEHICLE_PRESET_DESCRIPTION,
                    enum: [
                        "mclaren_750s",
                        "porsche_gt3rs",
                        "ferrari_296gtb",
                        "lamborghini_revuelto",
                    ],
                },
                customVehicle: CUSTOM_VEHICLE_SCHEMA,
                includeDesignRecommendations: {
                    type: "boolean",
                    description:
                        "是否在報告中附上設計建議（緩和坡尺寸、豎曲線參數、附屬設施建議）。預設 true。",
                },
                applyColorOverride: {
                    type: "boolean",
                    description: "是否套用圖形覆蓋著色。預設 true。",
                },
            },
            required: [],
        },
    },
];
