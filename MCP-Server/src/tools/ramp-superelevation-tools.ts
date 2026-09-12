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
            "套用車道橫向超高，內建中心線平順化、端點保護與 ±90mm 變形限制。" +
            "可選擇提供 targetElevations（絕對高程表）一次性精確寫入；" +
            "或提供 sectionDistances（每個斷面的內/外側到中心線距離）以支援不對稱車道幾何。",
        inputSchema: {
            type: "object",
            properties: {
                floorId: {
                    type: "number",
                    description: "目標樓板的 Element ID",
                },
                superelevationRate: {
                    type: "number",
                    description: "橫向超高率（小數），預設 0.03 代表 3%",
                },
                targetElevations: {
                    type: "object",
                    description: "可選的絕對高程表，鍵值為頂點索引，值為目標高程（mm）。若提供，會優先套用這組高程，適合直接貼上 Phase 1 計算出的 68 點表。",
                    additionalProperties: { type: "number" },
                },
                sectionDistances: {
                    type: "array",
                    description: "可選。每個斷面的內外側到中心線距離（mm），用於不對稱車道超高計算。",
                    items: {
                        type: "object",
                        properties: {
                            innerIndex: { type: "integer", description: "內側頂點索引" },
                            outerIndex: { type: "integer", description: "外側頂點索引" },
                            innerToCenterMm: { type: "number", description: "內側到中心線距離（mm）" },
                            outerToCenterMm: { type: "number", description: "外側到中心線距離（mm）" },
                        },
                        required: ["innerIndex", "outerIndex", "innerToCenterMm", "outerToCenterMm"],
                    },
                },
                fixedVertexIndices: {
                    type: "array",
                    description: "可選。強制保持原高程不變的頂點索引（例如起終點接合處），用於避免端部折落。",
                    items: { type: "integer" },
                },
            },
            required: ["floorId"],
        },
    },
];
