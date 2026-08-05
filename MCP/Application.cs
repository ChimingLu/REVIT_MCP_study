using System;
using Autodesk.Revit.UI;
using System.Reflection;
using RevitMCP.Core;
using RevitMCP.Configuration;

namespace RevitMCP
{
    public class Application : IExternalApplication
    {
        private static CoreRuntimeManager _runtimeManager;
        private static UIApplication _uiApp;
        private static ExternalEvent _reloadExternalEvent;
        private static ReloadCoreEventHandler _reloadEventHandler;

        public static CoreRuntimeManager RuntimeManager => _runtimeManager;
        public static UIApplication UIApp => _uiApp;

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // ????蝵桃恣?
                _ = ConfigManager.Instance;
                _runtimeManager = new CoreRuntimeManager();
                
                // 撱箇? Core Reload ??ExternalEvent嚗?? Startup/UI ?瑁?蝺遣蝡?
                _reloadEventHandler = new ReloadCoreEventHandler();
                _reloadExternalEvent = ExternalEvent.Create(_reloadEventHandler);

                // 閮???
                Logger.Info("RevitMCP Plugin 甇???...");

                // 撱箇????Ｘ
                RibbonPanel panel = application.CreateRibbonPanel("MCP Tools");
                
                string assemblyPath = Assembly.GetExecutingAssembly().Location;

                // 1. MCP ?????? (Toggle)
                PushButtonData toggleButtonData = new PushButtonData(
                    "MCPToggle",
                    "MCP ??\n(????",
                    assemblyPath,
                    "RevitMCP.Commands.ToggleServiceCommand");
                toggleButtonData.ToolTip = "????甇?MCP WebSocket ??";
                panel.AddItem(toggleButtonData);

                // 2. ???亥???
                PushButtonData logButtonData = new PushButtonData(
                    "OpenLog",
                    "??\n?亥?",
                    assemblyPath,
                    "RevitMCP.Commands.OpenLogCommand");
                logButtonData.ToolTip = "?? MCP ?瑁??亥?";
                panel.AddItem(logButtonData);

                // 3. 閮剖???
                PushButtonData settingsButtonData = new PushButtonData(
                    "MCPSettings",
                    "MCP\n閮剖?",
                    assemblyPath,
                    "RevitMCP.Commands.SettingsCommand");
                settingsButtonData.ToolTip = "?? MCP 閮剖?閬?";
                panel.AddItem(settingsButtonData);

                // 4. Core ????
                PushButtonData reloadButtonData = new PushButtonData(
                    "ReloadCore",
                    "Core\n??",
                    assemblyPath,
                    "RevitMCP.Commands.ReloadCoreCommand");
                reloadButtonData.ToolTip = "?頛 RevitMCP.CoreRuntime.dll嚗??? Revit嚗?;
                panel.AddItem(reloadButtonData);

                // 閮餃? Process ??箔?隞???Revit crash ??OnShutdown 銝?鋡怠?恬?
                // 雿?ProcessExit 隞?璈?閫貊嚗Ⅱ靽?HttpListener 鋡恍??橘?
                // ?踹? HTTP.sys 摮文? Request Queue 雿? port??                AppDomain.CurrentDomain.ProcessExit += (s, e) => EmergencyStopSocket();

                Logger.Info("RevitMCP Plugin 撌脫?????);

                TaskDialog.Show("RevitMCP", 
                    "RevitMCP Plugin 撌脰??功n\n" +
                    "隢??CP ?? (????????? AI ?批?");
                
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("頛 MCP Tools 憭望?", ex);
                TaskDialog.Show("?航炊", "頛 MCP Tools 憭望?: " + ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            try
            {
                EmergencyStopSocket();
                return Result.Succeeded;
            }
            catch
            {
                return Result.Failed;
            }
        }

        /// <summary>
        /// 蝺仿???Socket ?????◤ OnShutdown ??ProcessExit ?澆嚗?        /// ?折??null check ?脫迫???瑁???        /// </summary>
        private static void EmergencyStopSocket()
        {
            try
            {
                if (_runtimeManager != null)
                {
                    _runtimeManager.Shutdown();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"EmergencyStopSocket failed: {ex.Message}");
            }
        }

        /// <summary>
        /// ?? MCP ??
        /// </summary>
        public static void StartMCPService(UIApplication uiApp)
        {
            try
            {
                _uiApp = uiApp;
                _runtimeManager.SetUIApplication(_uiApp);

                if (_runtimeManager.IsRunning)
                {
                    TaskDialog.Show("MCP ??", "??撌脣?瑁?銝准?);
                    return;
                }

                _runtimeManager.StartService();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("?航炊", $"?? MCP ??憭望?: {ex.Message}");
            }
        }

        /// <summary>
        /// ?迫 MCP ??
        /// </summary>
        public static void StopMCPService()
        {
            try
            {
                if (_runtimeManager != null)
                {
                    _runtimeManager.StopService();
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("?航炊", $"?迫 MCP ??憭望?: {ex.Message}");
            }
        }

        public static bool ReloadCore()
        {
            try
            {
                if (_uiApp == null)
                {
                    return false;
                }

                _runtimeManager.SetUIApplication(_uiApp);
                _runtimeManager.ReloadCore();
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"ReloadCore 憭望?: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// 敺??臬銵?摰?啗孛??Core Reload??        /// ?芸??ExternalEvent.Raise()嚗祕??ReloadCore() ??Revit UI ?瑁?蝺銵?        /// </summary>
        public static void RequestReloadCoreFromBackground()
        {
            if (_reloadExternalEvent == null)
            {
                Logger.Error("ReloadExternalEvent 撠????);
                return;
            }
            _reloadEventHandler.SetRuntimeManager(_runtimeManager, _uiApp);
            _reloadExternalEvent.Raise();
        }

        public static bool IsServiceConnected()
        {
            try
            {
                return _runtimeManager != null && _runtimeManager.IsConnected;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsServiceRunning()
        {
            try
            {
                return _runtimeManager != null && _runtimeManager.IsRunning;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsCoreLoaded()
        {
            try
            {
                return _runtimeManager != null && _runtimeManager.IsLoaded;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// ExternalEvent handler for reloading CoreRuntime from a background thread.
    /// Execute() runs on the Revit UI thread, which satisfies ExternalEvent.Create() requirements.
    /// </summary>
    internal class ReloadCoreEventHandler : IExternalEventHandler
    {
        private CoreRuntimeManager _runtimeManager;
        private UIApplication _uiApp;

        public void SetRuntimeManager(CoreRuntimeManager runtimeManager, UIApplication uiApp)
        {
            _runtimeManager = runtimeManager;
            _uiApp = uiApp;
        }

        public void Execute(UIApplication app)
        {
            try
            {
                if (_runtimeManager == null) return;
                _runtimeManager.SetUIApplication(_uiApp ?? app);
                _runtimeManager.ReloadCore();
                Logger.Info("CoreRuntime ?梢?頛???ExternalEvent UI ?瑁?蝺?");
            }
            catch (Exception ex)
            {
                Logger.Error($"ReloadCoreEventHandler.Execute 憭望?: {ex.Message}", ex);
            }
        }

        public string GetName() => "RevitMCP CoreRuntime Reload";
    }
}
