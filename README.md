# RevitMCP Core-Reload 獨立熱重載套件 SOP 指南

本分支 (`feature/core-reload-patch`) 是一個**獨立的熱重載補丁套件 (Orphan Branch)**。
它排除所有 `main` 的 commit 歷史與檔案污染，專門保存 RevitMCP 熱重載 (CoreRuntime) 架構所需的關鍵程式碼與設定。

---

## 🎯 適用場景

當您從最新的 `main` 建立全新的功能分支（例如 `feat/new-awesome-feature`）時，只需執行簡單的 checkout 指令，即可將熱重載架構完全併入您的新分支中，無需手動修改 C# 架構與相容性邏輯。

---

## 🚀 未來 AI / 開發者整合熱重載 SOP

### 步驟 1：建立並切換到您的新功能分支
```bash
git checkout main
git pull upstream main
git checkout -b feat/your-feature-name
```

### 步驟 2：從 `feature/core-reload-patch` 分支抽取熱重載套件
執行以下 PowerShell / Git 指令，一次覆蓋/注入熱重載檔案：

```bash
git checkout feature/core-reload-patch -- MCP.Contracts/ MCP.CoreRuntime/ MCP/Application.cs MCP/Commands/MCPCommands.cs MCP/Core/CoreLoadContext.cs MCP/Core/CoreRuntimeManager.cs MCP/RevitMCP.csproj
```

### 步驟 3：提交熱重載基礎結構
```bash
git commit -m "feat(hot-reload): inject core-reload architecture from feature/core-reload-patch"
```

---

## 📦 補丁套件包含的核心檔案說明

| 檔案/目錄 | 說明 |
|---|---|
| `MCP.Contracts/` | 熱重載介面契約專案 (`IRevitMcpRuntime.cs` / `netstandard2.0`)，由預設 ALC 載入以避免型別轉換異常。 |
| `MCP.CoreRuntime/` | 可動態載入與卸載的核心專案 (`RevitMcpCoreRuntime.cs`)，連結核心命令邏輯。 |
| `MCP/Core/CoreLoadContext.cs` | .NET 8+ (`AssemblyLoadContext`, `isCollectible: true`) 及 .NET Framework 4.8 雙向載入器。 |
| `MCP/Core/CoreRuntimeManager.cs` | CoreRuntime 的 Shadow-copy、Load、Unload 與 Reload 生命週期管理器。 |
| `MCP/Application.cs` | 註冊 `ReloadCoreEventHandler` (ExternalEvent) 與工具列「Core 重載」Ribbon 按鈕。 |
| `MCP/Commands/MCPCommands.cs` | 包含 `ReloadCoreCommand` 外部命令實作。 |
| `MCP/RevitMCP.csproj` | 設定包含 `MCP.Contracts` 參考與自動編譯/複製 `RevitMCP.CoreRuntime.dll` 至 `runtime/` 目錄的 Target。 |

---

## ⚠️ 注意事項與相容性說明

1. **Revit 版本支援**：
   - **Revit 2025+ (.NET 8+)**：支援完整 `AssemblyLoadContext` 卸載與真實記憶體釋放。
   - **Revit 2020~2024 (.NET Framework 4.8)**：支援 Shadow-copy 與 AppDomain 重新載入，避免 DLL 檔案鎖定。
2. **Visual Studio 建置**：
   - 由於內含 Revit API 多版本與 .NET Framework 參考，建議使用 Visual Studio 開啟 `MCP.sln` 並選擇目標版本（如 `Debug.R21` / `Debug.R24`）進行編譯。
3. **熱重載觸發方式**：
   - 在 Revit Ribbon 點選「**Core 重載**」按鈕。
   - 或從 MCP Server 發送 `reload_core` 命令。