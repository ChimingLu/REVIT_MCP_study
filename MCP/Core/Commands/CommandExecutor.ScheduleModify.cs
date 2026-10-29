using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitMCP.Core
{
    /// <summary>
    /// 修改既有明細表（modify_view_schedule）。
    ///
    /// create_view_schedule 只能新建；實務上明細表建好後還要反覆調欄位、標題、格式、排序群組，
    /// 以前只能靠 pyRevit 按鈕寫死版面，每次改版面都要重裝按鈕。這支工具把那些設定開放給 MCP。
    ///
    /// 只傳 scheduleId／scheduleName、不帶任何修改參數時，等於唯讀檢視：回傳目前定義。
    /// 所有修改在同一個 Transaction 內完成，可在 Revit 用 Ctrl+Z 一次復原。
    /// 欄位以參數名稱（ScheduleField.GetName）比對，找不到再比對欄位標題。
    /// </summary>
    public partial class CommandExecutor
    {
        private object ModifyViewSchedule(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;
            ViewSchedule schedule = FindScheduleForModify(doc, parameters);
            ScheduleDefinition def = schedule.Definition;

            var changes = new List<string>();
            var problems = new List<string>();

            bool hasChange = new[] { "rename", "addFields", "removeFields", "fields", "fieldOrder", "sortGroupFields", "filters", "itemized", "showGrandTotal", "includeLinkedFiles" }
                .Any(k => parameters[k] != null && parameters[k].Type != JTokenType.Null);

            if (hasChange)
            {
                using (Transaction trans = new Transaction(doc, "修改明細表"))
                {
                    trans.Start();

                    string rename = parameters["rename"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(rename) && rename != schedule.Name)
                    {
                        changes.Add($"名稱 {schedule.Name} → {rename}");
                        schedule.Name = rename;
                    }

                    if (parameters["includeLinkedFiles"] != null)
                    {
                        bool v = parameters["includeLinkedFiles"].Value<bool>();
                        try { def.IncludeLinkedFiles = v; changes.Add($"包含連結檔 = {v}"); }
                        catch (Exception ex) { problems.Add("包含連結檔：" + ex.Message); }
                    }

                    // 移除欄位（排序群組或篩選若用到它，Revit 會一併移除）
                    foreach (string name in parameters["removeFields"]?.ToObject<List<string>>() ?? new List<string>())
                    {
                        ScheduleField f = FindScheduleField(def, name);
                        if (f == null) { problems.Add($"移除欄位「{name}」：表上沒有這個欄位"); continue; }
                        def.RemoveField(f.FieldId);
                        changes.Add($"移除欄位 {name}");
                    }

                    // 加入欄位
                    foreach (string name in parameters["addFields"]?.ToObject<List<string>>() ?? new List<string>())
                    {
                        if (FindScheduleField(def, name) != null) continue;
                        if (AddScheduleFieldByName(doc, def, name) != null) changes.Add($"加入欄位 {name}");
                        else problems.Add($"加入欄位「{name}」：此品類沒有這個可排程欄位");
                    }

                    // 逐欄設定：標題、隱藏、對齊、加總、格式（欄位不在表上時自動加入）
                    if (parameters["fields"] is JArray fieldSpecs)
                    {
                        foreach (JObject spec in fieldSpecs.OfType<JObject>())
                        {
                            string name = spec["field"]?.Value<string>();
                            if (string.IsNullOrEmpty(name)) continue;
                            ScheduleField f = FindScheduleField(def, name);
                            if (f == null)
                            {
                                f = AddScheduleFieldByName(doc, def, name);
                                if (f == null) { problems.Add($"欄位「{name}」：此品類沒有這個可排程欄位"); continue; }
                                changes.Add($"加入欄位 {name}");
                            }
                            ApplyFieldSpec(doc, f, spec, name, changes, problems);
                        }
                    }

                    // 欄位順序：列出的欄位依序排在最前面，其餘維持原順序接在後面
                    if (parameters["fieldOrder"] is JArray orderArr)
                    {
                        var front = new List<ScheduleFieldId>();
                        foreach (string name in orderArr.ToObject<List<string>>())
                        {
                            ScheduleField f = FindScheduleField(def, name);
                            if (f == null) { problems.Add($"欄位順序「{name}」：表上沒有這個欄位"); continue; }
                            if (!front.Contains(f.FieldId)) front.Add(f.FieldId);
                        }
                        var rest = def.GetFieldOrder().Where(id => !front.Contains(id));
                        def.SetFieldOrder(front.Concat(rest).ToList());
                        changes.Add("欄位順序：" + string.Join("、", orderArr.ToObject<List<string>>()));
                    }

                    // 排序／群組：有傳就整組取代（空陣列＝清除）
                    if (parameters["sortGroupFields"] is JArray sortArr)
                    {
                        def.ClearSortGroupFields();
                        var applied = new List<string>();
                        foreach (JToken token in sortArr)
                        {
                            string name = token.Type == JTokenType.String ? token.Value<string>() : token["field"]?.Value<string>();
                            if (string.IsNullOrEmpty(name)) continue;
                            ScheduleField f = FindScheduleField(def, name);
                            if (f == null)
                            {
                                f = AddScheduleFieldByName(doc, def, name);
                                if (f == null) { problems.Add($"排序群組「{name}」：找不到欄位"); continue; }
                                changes.Add($"加入欄位 {name}");
                            }
                            bool descending = token.Type == JTokenType.Object && (token["descending"]?.Value<bool>() ?? false);
                            var sgf = new ScheduleSortGroupField(f.FieldId, descending ? ScheduleSortOrder.Descending : ScheduleSortOrder.Ascending);
                            if (token.Type == JTokenType.Object)
                            {
                                sgf.ShowHeader = token["showHeader"]?.Value<bool>() ?? false;
                                sgf.ShowFooter = token["showFooter"]?.Value<bool>() ?? false;
                                sgf.ShowBlankLine = token["showBlankLine"]?.Value<bool>() ?? false;
                            }
                            def.AddSortGroupField(sgf);
                            applied.Add(name);
                        }
                        changes.Add(applied.Count > 0 ? "排序群組：" + string.Join(" → ", applied) : "清除排序群組");
                    }

                    // 篩選：有傳就整組取代（空陣列＝清除）。篩選欄位不在表上時自動加入並隱藏
                    if (parameters["filters"] is JArray filterArr)
                    {
                        def.ClearFilters();
                        var applied = new List<string>();
                        foreach (JObject spec in filterArr.OfType<JObject>())
                        {
                            string name = spec["field"]?.Value<string>();
                            string op = spec["operator"]?.Value<string>() ?? "equals";
                            string value = spec["value"]?.ToString() ?? "";
                            string label = $"{name} {op} {value}".Trim();
                            ScheduleField f = string.IsNullOrEmpty(name) ? null : FindScheduleField(def, name);
                            if (f == null && !string.IsNullOrEmpty(name))
                            {
                                f = AddScheduleFieldByName(doc, def, name);
                                if (f != null) { f.IsHidden = true; changes.Add($"加入隱藏欄位 {name}（篩選用）"); }
                            }
                            if (f == null) { problems.Add($"篩選「{label}」：找不到欄位"); continue; }
                            if (!TryMapScheduleFilterType(op, out ScheduleFilterType type)) { problems.Add($"篩選「{label}」：不支援的運算子"); continue; }
                            try { def.AddFilter(CreateScheduleFilter(f, type, value)); applied.Add(label); }
                            catch (Exception ex) { problems.Add($"篩選「{label}」：{ex.Message}"); }
                        }
                        changes.Add(applied.Count > 0 ? "篩選：" + string.Join("；", applied) : "清除篩選");
                    }

                    if (parameters["itemized"] != null)
                    {
                        def.IsItemized = parameters["itemized"].Value<bool>();
                        changes.Add($"逐項列舉 = {def.IsItemized}");
                    }
                    if (parameters["showGrandTotal"] != null)
                    {
                        bool v = parameters["showGrandTotal"].Value<bool>();
                        def.ShowGrandTotal = v;
                        if (v) def.ShowGrandTotalCount = true;
                        changes.Add($"總計 = {v}");
                    }

                    trans.Commit();
                }
            }

            return new
            {
                ElementId = schedule.Id.GetIdValue(),
                Name = schedule.Name,
                Changes = changes,
                Problems = problems,
                Definition = DescribeScheduleDefinition(doc, def),
                Message = hasChange
                    ? $"已修改明細表「{schedule.Name}」：{changes.Count} 項變更，{problems.Count} 項問題"
                    : $"明細表「{schedule.Name}」目前的定義（未修改）"
            };
        }

        private ViewSchedule FindScheduleForModify(Document doc, JObject parameters)
        {
            long? id = parameters["scheduleId"]?.Value<long?>();
            string name = parameters["scheduleName"]?.Value<string>();
            var all = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                .Where(s => !s.IsTemplate && !s.IsTitleblockRevisionSchedule).ToList();

            if (id.HasValue)
            {
                var byId = all.FirstOrDefault(s => s.Id.GetIdValue() == id.Value);
                if (byId == null) throw new Exception($"找不到 ElementId {id} 的明細表");
                return byId;
            }
            if (string.IsNullOrWhiteSpace(name)) throw new Exception("請提供 scheduleId 或 scheduleName");

            var exact = all.FirstOrDefault(s => s.Name == name);
            if (exact != null) return exact;
            var partial = all.Where(s => s.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (partial.Count == 1) return partial[0];
            if (partial.Count == 0) throw new Exception($"找不到名稱含「{name}」的明細表");
            throw new Exception($"名稱含「{name}」的明細表有 {partial.Count} 張，請給完整名稱：" + string.Join("、", partial.Select(s => s.Name)));
        }

        /// <summary>依參數名稱找表上的欄位，找不到再比對欄位標題。</summary>
        private static ScheduleField FindScheduleField(ScheduleDefinition def, string name)
        {
            ScheduleField byHeading = null;
            for (int i = 0; i < def.GetFieldCount(); i++)
            {
                ScheduleField f = def.GetField(i);
                if (string.Equals(f.GetName(), name, StringComparison.OrdinalIgnoreCase)) return f;
                if (byHeading == null && string.Equals(f.ColumnHeading, name, StringComparison.OrdinalIgnoreCase)) byHeading = f;
            }
            return byHeading;
        }

        private static ScheduleField AddScheduleFieldByName(Document doc, ScheduleDefinition def, string name)
        {
            var schedulable = def.GetSchedulableFields();
            var sf = schedulable.FirstOrDefault(f => string.Equals(f.GetName(doc), name, StringComparison.OrdinalIgnoreCase));
            if (sf == null && (name.Equals("Count", StringComparison.OrdinalIgnoreCase) || name == "計數" || name == "數量"))
                sf = schedulable.FirstOrDefault(f => f.FieldType == ScheduleFieldType.Count);
            return sf == null ? null : def.AddField(sf);
        }

        private static void ApplyFieldSpec(Document doc, ScheduleField f, JObject spec, string name, List<string> changes, List<string> problems)
        {
            string heading = spec["heading"]?.Value<string>();
            if (heading != null && heading != f.ColumnHeading)
            {
                changes.Add($"{name}：標題 {f.ColumnHeading} → {heading}");
                f.ColumnHeading = heading;
            }

            if (spec["hidden"] != null)
            {
                f.IsHidden = spec["hidden"].Value<bool>();
                changes.Add($"{name}：{(f.IsHidden ? "隱藏" : "顯示")}");
            }

            string align = spec["alignment"]?.Value<string>();
            if (!string.IsNullOrEmpty(align))
            {
                switch (align.ToLowerInvariant())
                {
                    case "left": f.HorizontalAlignment = ScheduleHorizontalAlignment.Left; break;
                    case "center": f.HorizontalAlignment = ScheduleHorizontalAlignment.Center; break;
                    case "right": f.HorizontalAlignment = ScheduleHorizontalAlignment.Right; break;
                    default: problems.Add($"{name}：不支援的對齊「{align}」"); align = null; break;
                }
                if (align != null) changes.Add($"{name}：{align} 對齊");
            }

            if (spec["total"] != null)
            {
                bool total = spec["total"].Value<bool>();
                try
                {
                    if (total && !f.CanTotal()) problems.Add($"{name}：此欄位不能加總");
                    else
                    {
                        f.DisplayType = total ? ScheduleFieldDisplayType.Totals : ScheduleFieldDisplayType.Standard;
                        changes.Add($"{name}：{(total ? "計算總計" : "不計算總計")}");
                    }
                }
                catch (Exception ex) { problems.Add($"{name}：加總設定失敗 {ex.Message}"); }
            }

            if (spec["format"] is JObject fmt)
            {
                try
                {
                    if (fmt["useProjectSettings"]?.Value<bool>() == true)
                    {
                        f.SetFormatOptions(new FormatOptions());
                        changes.Add($"{name}：格式改回專案設定");
                    }
                    else
                    {
                        string unitKey = fmt["unit"]?.Value<string>();
                        ForgeTypeId unit = string.IsNullOrEmpty(unitKey) ? f.GetFormatOptions().GetUnitTypeId() : MapScheduleUnit(unitKey);
                        if (unit == null || unit.Empty())
                        {
                            problems.Add($"{name}：不支援的單位「{unitKey}」（可用 mm、cm、m、m2、m3、deg）");
                        }
                        else
                        {
                            var fo = new FormatOptions(unit);
                            fo.UseDefault = false;
                            if (fmt["accuracy"] != null) fo.Accuracy = fmt["accuracy"].Value<double>();
                            if (fmt["showUnitSymbol"]?.Value<bool>() == false) fo.SetSymbolTypeId(new ForgeTypeId());
                            f.SetFormatOptions(fo);
                            changes.Add($"{name}：格式 {unitKey ?? "原單位"}" +
                                        (fmt["accuracy"] != null ? $"、精度 {fo.Accuracy}" : "") +
                                        (fmt["showUnitSymbol"]?.Value<bool>() == false ? "、不顯示單位符號" : ""));
                        }
                    }
                }
                catch (Exception ex) { problems.Add($"{name}：格式設定失敗 {ex.Message}"); }
            }
        }

        private static ForgeTypeId MapScheduleUnit(string key)
        {
            switch ((key ?? "").Trim().ToLowerInvariant())
            {
                case "mm": return UnitTypeId.Millimeters;
                case "cm": return UnitTypeId.Centimeters;
                case "m": return UnitTypeId.Meters;
                case "m2": case "m²": case "㎡": return UnitTypeId.SquareMeters;
                case "m3": case "m³": return UnitTypeId.CubicMeters;
                case "deg": case "°": return UnitTypeId.Degrees;
                default: return null;
            }
        }

        private static object DescribeScheduleDefinition(Document doc, ScheduleDefinition def)
        {
            var sortNames = new List<string>();
            for (int i = 0; i < def.GetSortGroupFieldCount(); i++)
            {
                var sg = def.GetSortGroupField(i);
                sortNames.Add(def.GetField(sg.FieldId).GetName() + (sg.SortOrder == ScheduleSortOrder.Descending ? "（遞減）" : ""));
            }

            var filters = new List<string>();
            for (int i = 0; i < def.GetFilterCount(); i++)
            {
                var flt = def.GetFilter(i);
                string val = flt.IsStringValue ? flt.GetStringValue()
                           : flt.IsIntegerValue ? flt.GetIntegerValue().ToString()
                           : flt.IsDoubleValue ? flt.GetDoubleValue().ToString("0.###")
                           : flt.IsElementIdValue ? flt.GetElementIdValue().GetIdValue().ToString() : "";
                filters.Add($"{def.GetField(flt.FieldId).GetName()} {flt.FilterType} {val}".Trim());
            }

            var fields = new List<object>();
            foreach (ScheduleFieldId id in def.GetFieldOrder())
            {
                ScheduleField f = def.GetField(id);
                string unit = null, symbol = null;
                double? accuracy = null;
                bool useDefault = true;
                try
                {
                    var fo = f.GetFormatOptions();
                    useDefault = fo.UseDefault;
                    if (!fo.UseDefault)
                    {
                        unit = fo.GetUnitTypeId()?.TypeId;
                        symbol = fo.GetSymbolTypeId()?.TypeId;
                        accuracy = fo.Accuracy;
                    }
                }
                catch { }
                fields.Add(new
                {
                    Name = f.GetName(),
                    Heading = f.ColumnHeading,
                    Hidden = f.IsHidden,
                    Alignment = f.HorizontalAlignment.ToString(),
                    Total = f.DisplayType == ScheduleFieldDisplayType.Totals,
                    FormatUsesProjectSettings = useDefault,
                    Unit = unit,
                    UnitSymbol = symbol,
                    Accuracy = accuracy,
                });
            }

            return new
            {
                Category = def.CategoryId == ElementId.InvalidElementId ? "多品類" : Category.GetCategory(doc, def.CategoryId)?.Name,
                IsItemized = def.IsItemized,
                ShowGrandTotal = def.ShowGrandTotal,
                IncludeLinkedFiles = def.IncludeLinkedFiles,
                SortGroupFields = sortNames,
                Filters = filters,
                Fields = fields,
            };
        }
    }
}
