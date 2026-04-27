using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Net.Sockets;
using RevitMCP.Core;
using RevitMCP.Configuration;

namespace RevitMCP.Commands
{
    /// <summary>
    /// 切換 MCP 服務狀態命令 (開/關)
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ToggleServiceCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                // 以 IsServiceRunning() 判斷開關狀態（而非 IsServiceConnected，
                // 後者依賴外部 MCP Server 已連線，會導致需按兩次才生效）
                bool isRunning = Application.IsServiceRunning();

                if (isRunning)
                {
                    // 如果服務執行中，則停止
                    Application.StopMCPService();
                    Logger.Info("使用者手動停止 MCP 服務");
                    TaskDialog.Show("MCP 服務", "🔴 MCP 服務已關閉。");
                }
                else
                {
                    // 未執行 → 啟動
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
                TaskDialog.Show("錯誤", "切換服務狀態失敗: " + ex.Message);
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// 重新載入 CoreRuntime 命令（不重啟 Revit）
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ReloadCoreCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                bool loadedBefore = Application.IsCoreLoaded();
                bool runningBefore = Application.IsServiceRunning();

                Application.StartMCPService(commandData.Application);
                bool ok = Application.ReloadCore();

                if (!ok)
                {
                    message = "Core 重載失敗";
                    TaskDialog.Show("Core 重載", "Core 重載失敗，請檢查 log。");
                    return Result.Failed;
                }

                string detail =
                    $"Core 已重載成功\n\n" +
                    $"重載前是否已載入: {(loadedBefore ? "是" : "否")}\n" +
                    $"重載前是否執行中: {(runningBefore ? "是" : "否")}\n" +
                    $"重載後是否執行中: {(Application.IsServiceRunning() ? "是" : "否")}";

                TaskDialog.Show("Core 重載", detail);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("錯誤", "Core 重載失敗: " + ex.Message);
                return Result.Failed;
            }
        }
    }


    /// <summary>
    /// 開啟設定視窗命令
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SettingsCommand : IExternalCommand
    {
        private static bool IsPortListening(string host, int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var connectTask = client.ConnectAsync(host, port);
                    return connectTask.Wait(TimeSpan.FromMilliseconds(300)) && client.Connected;
                }
            }
            catch
            {
                return false;
            }
        }

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                var settings = Configuration.ConfigManager.Instance.Settings;

                bool isRunning = Application.IsServiceRunning();
                bool isConnected = Application.IsServiceConnected();
                bool portListening = IsPortListening(settings.Host, settings.Port);

                string serviceStatus;
                if (isConnected)
                {
                    serviceStatus = "啟用（已連線）";
                }
                else if (isRunning)
                {
                    serviceStatus = "啟用（待連線）";
                }
                else if (portListening)
                {
                    serviceStatus = "啟用（埠已監聽）";
                }
                else
                {
                    serviceStatus = "停用";
                }

                string info = $"目前設定:\n\n" +
                    $"主機: {settings.Host}\n" +
                    $"埠號: {settings.Port}\n" +
                    $"服務狀態: {serviceStatus}\n" +
                    $"Runtime.IsRunning: {(isRunning ? "是" : "否")}\n" +
                    $"Runtime.IsConnected: {(isConnected ? "是" : "否")}\n" +
                    $"Port監聽: {(portListening ? "是" : "否")}\n\n" +
                    $"配置檔位置:\n" +
                    $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\RevitMCP\\config.json";

                if (Application.SocketService?.IsRunning == true)
                {
                    var (locked, clientName, remote, sinceUtc) = Application.SocketService.GetStatusSnapshot();
                    string clientDisplay = string.IsNullOrEmpty(clientName)
                        ? (remote ?? "—")
                        : (clientName + " (" + (remote ?? "?") + ")");
                    info += "\n\n" +
                        "連線狀態: " + (locked ? "已鎖定" : "閒置(等待連入)") + "\n" +
                        "目前客戶端: " + clientDisplay + "\n" +
                        "連線時間: " + (sinceUtc.HasValue ? sinceUtc.Value.ToLocalTime().ToString() : "—");
                }

                TaskDialog.Show("MCP 設定", info);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("錯誤", "開啟設定失敗: " + ex.Message);
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// 切換/釋放目前連線命令：釋放後讓下一個重新連線的 client 取得鎖。
    /// 連線為匿名，無法保證釋放後由哪個 client 取得連線。
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SwitchConnectionCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                var svc = Application.SocketService;
                if (svc == null || !svc.IsRunning)
                {
                    TaskDialog.Show("切換/釋放連線", "MCP 服務尚未啟動。");
                    return Result.Succeeded;
                }

                var (released, prev) = svc.SwitchConnection();
                TaskDialog.Show("切換/釋放連線", released
                    ? ("已釋放連線（原客戶端 " + prev + "）。\n\n下一個重新連線的客戶端將取得連線。\n無法保證是特定客戶端——若要切換到另一個客戶端，請先讓目前客戶端停止重連。")
                    : "目前沒有已鎖定的連線。下一個連入的客戶端會直接取得連線。");
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
