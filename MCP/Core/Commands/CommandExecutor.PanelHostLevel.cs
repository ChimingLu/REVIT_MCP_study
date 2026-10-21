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
    public partial class CommandExecutor
    {
        #region 帷幕嵌板樓層同步 (sync_curtain_panel_host_level)

        /// <summary>
        /// 把每片帷幕嵌板（含以基本牆充當的嵌板）所屬帷幕牆的樓層名稱，寫進嵌板上的指定參數。
        /// 帷幕板品類本身沒有樓層欄位，明細表無法依樓層分組；寫入後即可用該參數分組。
        /// 可重跑：值已相同則跳過，模型更新後再跑一次即可同步。
        /// 預設 dryRun=true，只回報會寫入的數量，不開交易。
        /// 嵌板多半位於模型群組內，參數須設為「值可依群組實體變化」才寫得進去，寫入失敗會逐項統計回報。
        /// </summary>
        private object SyncCurtainPanelHostLevel(JObject parameters)
        {
            Document doc = _uiApp.ActiveUIDocument.Document;

            string paramName = parameters?["parameterName"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(paramName)) paramName = "牆樓層";
            bool dryRun = parameters?["dryRun"]?.Value<bool>() ?? true;
            bool includeHostWall = parameters?["includeHostWall"]?.Value<bool>() ?? false;
            string levelFilter = parameters?["levelName"]?.Value<string>();

            var walls = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .Where(w => w.CurtainGrid != null)
                .ToList();

            int wallsScanned = 0, panelsScanned = 0;
            int toWrite = 0, written = 0, unchanged = 0;
            int missingParam = 0, readOnly = 0, unsupportedStorage = 0, noLevel = 0, failed = 0;
            var missingParamTypes = new SortedSet<string>();
            var failureSamples = new List<string>();
            var perLevel = new SortedDictionary<string, int>();
            var seen = new HashSet<IdType>();

            // 先收集要寫的目標，dryRun 與實寫共用同一份清單
            var targets = new List<KeyValuePair<Element, Level>>();

            foreach (Wall wall in walls)
            {
                Level level = doc.GetElement(wall.LevelId) as Level;
                if (level == null) { noLevel++; continue; }
                if (!string.IsNullOrEmpty(levelFilter) && !level.Name.Equals(levelFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                wallsScanned++;
                if (includeHostWall) targets.Add(new KeyValuePair<Element, Level>(wall, level));

                foreach (ElementId panelId in wall.CurtainGrid.GetPanelIds())
                {
                    Element panel = doc.GetElement(panelId);
                    if (panel == null || panel is Wall) continue; // 牆型嵌板在下一段處理
                    if (!seen.Add(panelId.GetIdValue())) continue;
                    panelsScanned++;
                    targets.Add(new KeyValuePair<Element, Level>(panel, level));
                }
            }

            // 以基本牆充當的嵌板（鋁板等）：用它自己的「底部約束」。
            // 這類嵌板不一定出現在 CurtainGrid.GetPanelIds()，改從帷幕板品類收集。
            var wallPanels = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_CurtainWallPanels)
                .WhereElementIsNotElementType()
                .OfType<Wall>();
            foreach (Wall wallPanel in wallPanels)
            {
                if (!seen.Add(wallPanel.Id.GetIdValue())) continue;
                ElementId baseId = wallPanel.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT)?.AsElementId();
                Level baseLevel = baseId != null ? doc.GetElement(baseId) as Level : null;
                if (baseLevel == null) { noLevel++; continue; }
                if (!string.IsNullOrEmpty(levelFilter) && !baseLevel.Name.Equals(levelFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                panelsScanned++;
                targets.Add(new KeyValuePair<Element, Level>(wallPanel, baseLevel));
            }

            Transaction trans = null;
            if (!dryRun)
            {
                trans = new Transaction(doc, "同步帷幕嵌板樓層");
                trans.Start();
                var opts = trans.GetFailureHandlingOptions();
                opts.SetFailuresPreprocessor(new WarningSwallower());
                trans.SetFailureHandlingOptions(opts);
            }

            try
            {
                foreach (var pair in targets)
                {
                    Element elem = pair.Key;
                    Level level = pair.Value;

                    Parameter p = elem.LookupParameter(paramName);
                    if (p == null)
                    {
                        missingParam++;
                        string typeName = doc.GetElement(elem.GetTypeId())?.Name ?? elem.Name;
                        missingParamTypes.Add($"{elem.Category?.Name}: {typeName}");
                        continue;
                    }
                    if (p.IsReadOnly) { readOnly++; continue; }

                    bool same;
                    if (p.StorageType == StorageType.String)
                        same = string.Equals(p.AsString() ?? "", level.Name, StringComparison.Ordinal);
                    else if (p.StorageType == StorageType.ElementId)
                        same = p.AsElementId() == level.Id;
                    else { unsupportedStorage++; continue; }

                    if (same) { unchanged++; continue; }

                    toWrite++;
                    perLevel[level.Name] = perLevel.TryGetValue(level.Name, out int c) ? c + 1 : 1;
                    if (dryRun) continue;

                    try
                    {
                        bool ok = p.StorageType == StorageType.String ? p.Set(level.Name) : p.Set(level.Id);
                        if (ok) written++;
                        else
                        {
                            failed++;
                            if (failureSamples.Count < 10) failureSamples.Add($"{elem.Id.GetIdValue()}: Set 回傳 false");
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        if (failureSamples.Count < 10) failureSamples.Add($"{elem.Id.GetIdValue()}: {ex.Message}");
                    }
                }

                trans?.Commit();
            }
            catch
            {
                if (trans != null && trans.HasStarted()) trans.RollBack();
                throw;
            }
            finally
            {
                trans?.Dispose();
            }

            return new
            {
                Success = true,
                DryRun = dryRun,
                ParameterName = paramName,
                CurtainWallsScanned = wallsScanned,
                PanelsScanned = panelsScanned,
                ToWrite = toWrite,
                Written = written,
                Unchanged = unchanged,
                Failed = failed,
                MissingParameter = missingParam,
                MissingParameterTypes = missingParamTypes.Take(30).ToList(),
                ReadOnly = readOnly,
                UnsupportedStorageType = unsupportedStorage,
                CurtainWallsWithoutLevel = noLevel,
                ToWriteByLevel = perLevel,
                FailureSamples = failureSamples,
                Message = dryRun
                    ? $"預覽：{toWrite} 個元素會寫入「{paramName}」，{unchanged} 個已是正確值。以 dryRun=false 執行寫入。"
                    : $"已寫入 {written} 個元素，失敗 {failed} 個，{unchanged} 個已是正確值。"
            };
        }

        #endregion
    }
}
