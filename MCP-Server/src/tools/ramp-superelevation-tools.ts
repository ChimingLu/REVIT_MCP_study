/**
 * 車道超高工具 — ramp superelevation profile
 * 透過 SlabShapeEditor 調整樓板子元素頂點 Z 偏移，實現車道橫向超高設計。
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const rampSuperelevationTools: Tool[] = [
    {
        name: "get_floor_slab_shape",
        description:
            "讀取指定樓板（Floor）的 SlabShapeEditor 子元素頂點清單，回傳每個頂點的索引、X/Y/Z 座標（mm）、" +
            "當前 Z 偏移量（mm）及頂點類型。用於在呼叫 apply_ramp_superelevation 前，先診斷目前的板面形狀，" +
            "確認哪些頂點屬於向心側（內側）或離心側（外側）。",
        inputSchema: {
            type: "object",
            properties: {
                floorId: {
                    type: "number",
                    description: "目標樓板的 Element ID",
                },
            },
            required: ["floorId"],
        },
    },
    {
        name: "set_floor_vertex_offsets",
        description:
            "批次修改指定樓板子元素頂點的 Z 軸偏移量（mm）。適合精細控制單一頂點的超高調整，" +
            "或在 apply_ramp_superelevation 後做局部微調。若樓板尚未啟用子元素形狀，會自動嘗試啟用。",
        inputSchema: {
            type: "object",
            properties: {
                floorId: {
                    type: "number",
                    description: "目標樓板的 Element ID",
                },
                offsets: {
                    type: "array",
                    description: "頂點偏移清單，每筆包含頂點索引與目標 Z 偏移量（mm）",
                    minItems: 1,
                    items: {
                        type: "object",
                        properties: {
                            index: {
                                type: "integer",
                                description: "頂點索引（由 get_floor_slab_shape 取得）",
                            },
                            offsetMm: {
                                type: "number",
                                description: "Z 偏移量（mm），正值為抬高，負值為下沉",
                            },
                        },
                        required: ["index", "offsetMm"],
                    },
                },
            },
            required: ["floorId", "offsets"],
        },
    },
    {
        name: "apply_ramp_superelevation",
        description:
            "依車道寬度與橫向超高率，自動計算並批次套用車道樓板的超高漸變。" +
            "向心側（內側）頂點保持平坦（Z 偏移=0），離心側（外側）頂點依 laneWidthMm x superelevationRate% 抬高。" +
            "漸變段頂點依 transitionRatio 做線性插值，確保車道表面平滑過渡。" +
            "使用前請先以 get_floor_slab_shape 取得頂點索引，再依轉彎幾何判定內外側。",
        inputSchema: {
            type: "object",
            properties: {
                floorId: {
                    type: "number",
                    description: "目標樓板的 Element ID",
                },
                laneWidthMm: {
                    type: "number",
                    description: "車道有效寬度（mm），用於計算外側最大 Z 偏移量",
                },
                superelevationRate: {
                    type: "number",
                    description: "橫向超高率（%），常見設計值 2~8%，最大不得超過 20%",
                },
                innerEdgeVertexIds: {
                    type: "array",
                    description: "向心側（內側）完整超高段頂點索引清單",
                    items: { type: "integer" },
                },
                outerEdgeVertexIds: {
                    type: "array",
                    description: "離心側（外側）完整超高段頂點索引清單，將抬高至最大超高量",
                    items: { type: "integer" },
                },
                innerTransitionVertexIds: {
                    type: "array",
                    description: "（選填）內側漸變段頂點索引，偏移量為 transitionRatio 比例",
                    items: { type: "integer" },
                },
                outerTransitionVertexIds: {
                    type: "array",
                    description: "（選填）外側漸變段頂點索引，偏移量為最大超高量 x transitionRatio",
                    items: { type: "integer" },
                },
                transitionRatio: {
                    type: "number",
                    description: "漸變段所達到的超高比例（0~1，預設 0.5），用於產生平滑漸變",
                    default: 0.5,
                    minimum: 0,
                    maximum: 1,
                },
                keepInnerFlat: {
                    type: "boolean",
                    description: "內側頂點是否保持 Z 偏移=0（預設 true）；false 時內側會對稱下沉以加強橫坡效果",
                    default: true,
                },
            },
            required: ["floorId", "laneWidthMm", "superelevationRate", "innerEdgeVertexIds", "outerEdgeVertexIds"],
        },
    },
];
