using System;

namespace RevitMCP.Contracts
{
    public interface IRevitMcpRuntime
    {
        void Initialize(object uiApplication);
        void StartService();
        void StopService();
        void Shutdown();

        /// <summary>
        /// 由 Loader 注入的 reload callback，讓 CoreRuntime 在收到 reload_core 命令時可回呼 Loader 執行重載。
        /// </summary>
        void SetReloadCallback(Action reloadCallback);

        bool IsRunning { get; }
        bool IsConnected { get; }

        /// <summary>
        /// 目前連線鎖的狀態（供「MCP 設定」對話框顯示）。服務未啟動時回傳 false。
        /// </summary>
        bool TryGetConnectionStatus(out bool locked, out string? clientName, out string? remote, out DateTime? sinceUtc);

        /// <summary>
        /// 釋放目前連線，讓下一個重新連線的客戶端取得鎖（「切換/釋放連線」按鈕）。
        /// </summary>
        bool SwitchConnection(out string? previousClient);
    }
}
