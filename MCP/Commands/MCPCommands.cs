using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Net.Sockets;
using RevitMCP.Core;

namespace RevitMCP.Commands
{
    /// <summary>
    /// ?? MCP ????隞?(????
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
                // 隞?IsServiceRunning() ?斗??????? IsServiceConnected嚗?                // 敺?鞈游???MCP Server 撌脤??嚗?撠??甈⊥???嚗?                bool isRunning = Application.IsServiceRunning();

                if (isRunning)
                {
                    // 憒????瑁?銝哨???甇?                    Application.StopMCPService();
                    Logger.Info("雿輻????甇?MCP ??");
                    TaskDialog.Show("MCP ??", "? ??撌脣?甇?);
                }
                else
                {
                    // 憒????芸銵?????                    Logger.Info("雿輻??????MCP ??");
                    Application.StartMCPService(commandData.Application);
                    TaskDialog.Show("MCP ??", "? ??撌脣???);
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("?航炊", "??????仃?? " + ex.Message);
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// ?頛 CoreRuntime ?賭誘嚗??? Revit嚗?    /// </summary>
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
                    message = "Core ??憭望?";
                    TaskDialog.Show("Core ??", "Core ??憭望?嚗?瑼Ｘ log??);
                    return Result.Failed;
                }

                string detail =
                    $"Core 撌脤?頛??n\n" +
                    $"????血歇頛: {(loadedBefore ? "?? : "??)}\n" +
                    $"????血銵葉: {(runningBefore ? "?? : "??)}\n" +
                    $"??敺?血銵葉: {(Application.IsServiceRunning() ? "?? : "??)}";

                TaskDialog.Show("Core ??", detail);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("?航炊", "Core ??憭望?: " + ex.Message);
                return Result.Failed;
            }
        }
    }


    /// <summary>
    /// ??閮剖?閬??賭誘
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
                    serviceStatus = "?嚗歇???嚗?;
                }
                else if (isRunning)
                {
                    serviceStatus = "?嚗????嚗?;
                }
                else if (portListening)
                {
                    serviceStatus = "?嚗?撌脩?踝?";
                }
                else
                {
                    serviceStatus = "?";
                }

                string info = $"?桀?閮剖?:\n\n" +
                    $"銝餅?: {settings.Host}\n" +
                    $"??: {settings.Port}\n" +
                    $"????? {serviceStatus}\n" +
                    $"Runtime.IsRunning: {(isRunning ? "?? : "??)}\n" +
                    $"Runtime.IsConnected: {(isConnected ? "?? : "??)}\n" +
                    $"Port??: {(portListening ? "?? : "??)}\n\n" +
                    $"?蔭瑼?蝵?\n" +
                    $"{Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\RevitMCP\\config.json";
                
                TaskDialog.Show("MCP 閮剖?", info);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("?航炊", "??閮剖?憭望?: " + ex.Message);
                return Result.Failed;
            }
        }
    }
}
