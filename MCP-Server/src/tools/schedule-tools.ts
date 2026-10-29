/**
 * 明細表工具 — architect, mep Profile
 */

import { Tool } from "@modelcontextprotocol/sdk/types.js";

export const scheduleTools: Tool[] = [
    {
        name: "create_view_schedule",
        description: "在 Revit 中建立一個新的視圖明細表（Schedule/Quantities）。可指定品類、欄位、是否包含連結檔中的元素、篩選、排序／群組、是否逐項列舉與加總欄位。欄位名稱要用專案介面語言的名稱（中文 Revit 用「類型」「面積」等）；找不到的欄位會列在 MissingFields，不會自行補值。指定的品類解析不到時直接報錯（不會變成多品類明細表）。",
        inputSchema: {
            type: "object",
            properties: {
                name: { type: "string", description: "明細表名稱" },
                category: { type: "string", description: "品類名稱：介面名稱（如「帷幕板」「欄杆扶手」）或 BuiltInCategory 名稱（如 'Walls', 'CurtainWallPanels', 'StairsRailing'）。省略則建立多品類明細表" },
                fields: { type: "array", items: { type: "string" }, description: "欄位名稱列表；「計數」「Count」「數量」對應數量欄位" },
                includeLinkedFiles: { type: "boolean", description: "是否包含連結檔中的元素（明細表性質 → 欄位 → 包含連結檔中的元素），預設 false" },
                filters: {
                    type: "array",
                    description: "篩選條件；欄位不在 fields 內時會自動加入並隱藏。長度欄位的數值以 mm 輸入",
                    items: {
                        type: "object",
                        properties: {
                            field: { type: "string", description: "欄位名稱" },
                            operator: { type: "string", enum: ["equals", "not_equals", "contains", "not_contains", "begins_with", "greater_than", "less_than", "has_value", "has_no_value"], description: "運算子" },
                            value: { type: "string", description: "比對值（has_value／has_no_value 不需要）" },
                        },
                        required: ["field", "operator"],
                    },
                },
                sortGroupFields: {
                    type: "array",
                    description: "排序／群組欄位，依序套用；可給欄位名稱字串，或 {field, descending, showHeader, showFooter, showBlankLine}",
                    items: {
                        anyOf: [
                            { type: "string" },
                            {
                                type: "object",
                                properties: {
                                    field: { type: "string" },
                                    descending: { type: "boolean" },
                                    showHeader: { type: "boolean" },
                                    showFooter: { type: "boolean" },
                                    showBlankLine: { type: "boolean" },
                                },
                                required: ["field"],
                            },
                        ],
                    },
                },
                itemized: { type: "boolean", description: "是否逐項列舉每個實體；false 時同一群組合併成一列" },
                showGrandTotal: { type: "boolean", description: "是否顯示總計（含數量）" },
                totalFields: { type: "array", items: { type: "string" }, description: "要「計算總計」的欄位（如面積、長度、計數）" },
            },
            required: ["name"],
        },
    },
    {
        name: "modify_view_schedule",
        description: "修改既有明細表的欄位、格式、排序群組與篩選。可加入／移除欄位、改欄位標題、隱藏、對齊、加總、數字格式（單位、精度、不顯示單位符號）、欄位順序、排序／群組、篩選、逐項列舉、總計、包含連結檔、改名。只傳 scheduleId／scheduleName 不帶其他參數時為唯讀，回傳目前定義。欄位以參數名稱比對，找不到再比對欄位標題；不在表上的欄位會自動加入。sortGroupFields 與 filters 有傳就整組取代（空陣列＝清除）。全部在一個交易內完成，可用 Ctrl+Z 復原。",
        inputSchema: {
            type: "object",
            properties: {
                scheduleId: { type: "number", description: "明細表 ElementId（建議；由 list_schedules 取得）" },
                scheduleName: { type: "string", description: "明細表名稱；先完全比對，再部分比對（部分比對到多張會報錯）" },
                rename: { type: "string", description: "新名稱" },
                addFields: { type: "array", items: { type: "string" }, description: "要加入的欄位（參數名稱）" },
                removeFields: { type: "array", items: { type: "string" }, description: "要移除的欄位" },
                fields: {
                    type: "array",
                    description: "逐欄設定；欄位不在表上時自動加入",
                    items: {
                        type: "object",
                        properties: {
                            field: { type: "string", description: "參數名稱或目前的欄位標題" },
                            heading: { type: "string", description: "欄位標題，例如「面積(㎡)」" },
                            hidden: { type: "boolean", description: "是否隱藏（隱藏欄位仍可用於篩選與排序）" },
                            alignment: { type: "string", enum: ["left", "center", "right"], description: "水平對齊" },
                            total: { type: "boolean", description: "是否計算總計" },
                            format: {
                                type: "object",
                                description: "數字格式；useProjectSettings=true 改回專案設定",
                                properties: {
                                    useProjectSettings: { type: "boolean" },
                                    unit: { type: "string", enum: ["mm", "cm", "m", "m2", "m3", "deg"], description: "顯示單位；省略則沿用目前單位" },
                                    accuracy: { type: "number", description: "精度，例如 0.01" },
                                    showUnitSymbol: { type: "boolean", description: "false＝數字不帶單位符號（單位寫在標題）" },
                                },
                            },
                        },
                        required: ["field"],
                    },
                },
                fieldOrder: { type: "array", items: { type: "string" }, description: "列出的欄位依序排到最前面，其餘維持原順序" },
                sortGroupFields: {
                    type: "array",
                    description: "排序／群組，整組取代；欄位名稱字串，或 {field, descending, showHeader, showFooter, showBlankLine}",
                    items: {
                        anyOf: [
                            { type: "string" },
                            {
                                type: "object",
                                properties: {
                                    field: { type: "string" },
                                    descending: { type: "boolean" },
                                    showHeader: { type: "boolean" },
                                    showFooter: { type: "boolean" },
                                    showBlankLine: { type: "boolean" },
                                },
                                required: ["field"],
                            },
                        ],
                    },
                },
                filters: {
                    type: "array",
                    description: "篩選，整組取代；欄位不在表上時自動加入並隱藏。長度數值以 mm 輸入",
                    items: {
                        type: "object",
                        properties: {
                            field: { type: "string" },
                            operator: { type: "string", enum: ["equals", "not_equals", "contains", "not_contains", "begins_with", "greater_than", "less_than", "has_value", "has_no_value"] },
                            value: { type: "string" },
                        },
                        required: ["field", "operator"],
                    },
                },
                itemized: { type: "boolean", description: "是否逐項列舉每個實體" },
                showGrandTotal: { type: "boolean", description: "是否顯示總計" },
                includeLinkedFiles: { type: "boolean", description: "是否包含連結檔中的元素" },
            },
        },
    },
    {
        name: "list_schedules",
        description: "列出目前專案中所有可讀取的明細表（排除樣板、圖框修訂表）。回傳每張表的 ElementId、名稱、品類、列數與欄數，供儀錶板分類與展開使用。",
        inputSchema: {
            type: "object",
            properties: {},
        },
    },
    {
        name: "read_schedule",
        description: "讀取單一明細表的完整表格內容（欄位標頭 + 逐格 body 資料，忠實呈現 Revit 畫面顯示的文字）。用 scheduleId 或 scheduleName 指定。",
        inputSchema: {
            type: "object",
            properties: {
                scheduleId: { type: "number", description: "明細表 ElementId（建議；由 list_schedules 取得）" },
                scheduleName: { type: "string", description: "明細表名稱（scheduleId 未提供時使用，支援部分比對）" },
                maxRows: { type: "number", description: "最多回傳的資料列數（預設 2000，避免回應過大）" },
            },
        },
    },
];
