using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCP.Configuration;
using RevitMCP.Core;

namespace RevitMCP.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ToggleServiceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                bool isRunning = Application.IsServiceRunning();
                if (isRunning)
                {
                    Application.StopMCPService();
                    Logger.Info("使用者手動停止 MCP 服務");
                    TaskDialog.Show("MCP 服務", "🔴 MCP 服務已關閉。");
                }
                else
                {
                    Logger.Info("使用者手動啟動 MCP 服務");
                    Application.StartMCPService(commandData.Application);
                    TaskDialog.Show("MCP 服務",
                        "🟢 MCP 服務已啟動。\n\n" +
                        "正在 localhost:8964 監聽，等待 AI 客戶端連入。\n" +
                        "（連上後可在「MCP 設定」查看目前佔用連線的客戶端）");
                }

                Application.RequestToggleButtonIconRefresh();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("錯誤", "切換 MCP 服務失敗: " + ex.Message);
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class ReloadCoreCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                bool loadedBefore = Application.IsCoreLoaded();
                bool runningBefore = Application.IsServiceRunning();

                Application.StartMCPService(commandData.Application);
                bool ok = Application.ReloadCore();
                if (!ok)
                {
                    TaskDialog.Show("Core 重載", "❌ Core 重載失敗，請查看 log。");
                    return Result.Failed;
                }

                bool loadedAfter = Application.IsCoreLoaded();
                bool runningAfter = Application.IsServiceRunning();
                TaskDialog.Show("Core 重載",
                    "✅ Core 重載完成（不需重啟 Revit）。\n\n" +
                    $"Loaded: {loadedBefore} → {loadedAfter}\n" +
                    $"Running: {runningBefore} → {runningAfter}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("Core 重載錯誤", ex.Message);
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class SettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var settings = ConfigManager.Instance.Settings;
                bool isRunning = Application.IsServiceRunning();
                bool isConnected = Application.IsServiceConnected();
                bool portListening = isRunning;

                string serviceStatus;
                if (isConnected) serviceStatus = "執行中（已連線）";
                else if (isRunning) serviceStatus = "執行中（待連線）";
                else if (portListening) serviceStatus = "未執行（port 由本程序佔用）";
                else serviceStatus = "未執行";

                string info = $"設定資訊:\n\n" +
                    $"主機: {settings.Host}\n" +
                    $"Port: {settings.Port}\n" +
                    $"服務狀態: {serviceStatus}\n" +
                    $"Runtime.IsRunning: {(isRunning ? "是" : "否")}\n" +
                    $"Runtime.IsConnected: {(isConnected ? "是" : "否")}\n" +
                    $"Port 監聽中: {(portListening ? "是" : "否")}\n\n" +
                    $"設定檔:\n{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\RevitMCP\\config.json\n\n" +
                    "CoreRuntime 模式：連線鎖定細節請於日誌查看。";

                TaskDialog.Show("MCP 設定", info);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("錯誤", "讀取設定失敗: " + ex.Message);
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class SwitchConnectionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (!Application.IsServiceRunning())
                {
                    TaskDialog.Show("切換/釋放連線", "MCP 服務尚未啟動。");
                    return Result.Succeeded;
                }

                TaskDialog.Show("切換/釋放連線", "CoreRuntime 模式目前不提供手動切換連線，請先停止服務再啟動。");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("錯誤", "切換/釋放連線失敗: " + ex.Message);
                return Result.Failed;
            }
        }
    }
}
