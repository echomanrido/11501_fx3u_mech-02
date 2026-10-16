# 三菱 FX3U PLC 監控系統實作計畫 (C# Windows Forms)

本計畫旨在依據 [FX3U_Monitor_Specification.md](file:///d:/Test_Wugo/000.VibeCoding%E6%A9%9F%E4%B8%99/11501_fx3u/FX3U_Monitor_Specification.md) 規格，於 Visual Studio 2026 環境中開發一個穩定、高響應性且具工業級質感的 C# Windows Forms 監控應用程式。

---

## 系統規格確認
- **目標框架**：`net9.0-windows` (完美支援最新 Visual Studio 2026，同時支援 High-DPI 與現代化 UI 樣式)
- **通訊參數**：`BaudRate = 9600`, `DataBits = 7`, `Parity = Even`, `StopBits = One`
- **通訊協定**：三菱 FX Programming Port Protocol (ASCII Hex 封包 + Checksum 驗證)

---

## 建議架構與檔案清單

### 1. 專案核心結構
```
11501_fx3u/
├── Fx3uMonitorApp/
│   ├── Fx3uMonitorApp.csproj            # 專案檔 (Windows Desktop App, net9.0-windows)
│   ├── Program.cs                       # 應用程式啟動點
│   ├── Controls/
│   │   └── LedBulb.cs                   # 自訂高品質圓形平滑 LED 指示燈元件
│   ├── Protocols/
│   │   └── Fx3uProtocol.cs              # 協議封包組裝、Checksum 計算與位元解析引擎
│   ├── Services/
│   │   ├── IFx3uService.cs              # 通訊服務介面
│   │   └── Fx3uSerialService.cs         # 串列埠底層通訊、執行緒鎖、輪詢與非同步讀寫
│   └── UI/
│       ├── MainForm.cs                  # 主視窗邏輯
│       └── MainForm.Designer.cs         # 主視窗介面佈局 (工業風深淺配色、模組化排版)
└── Fx3uMonitorApp.Tests/                # 協議解析與 Checksum 單元驗證專案
    ├── Fx3uMonitorApp.Tests.csproj
    └── ProtocolTests.cs
```

---

## 模組詳細設計

### 1. 協議層 (`Fx3uProtocol.cs`)
- **常數定義**：`STX = 0x02`, `ETX = 0x03`, `ACK = 0x06`, `NAK = 0x15`
- **Checksum 運算**：計算 `STX` 至 `ETX` 之間所有位元組總和 $\pmod{256}$ 轉換為 2 Bytes ASCII
- **指令產生器**：
  - 讀取 $Y0 \sim Y17$：`02 30 30 30 41 30 30 32 03 36 36`
  - 讀取 $X0 \sim X17$：`02 30 30 30 38 30 30 32 03 35 44`
  - 強制 $Y0 \sim Y17$ ON/OFF 指令對照字典
- **回傳值位元映射解碼**：
  - 將 4 位元組的 ASCII (`[Byte1] [Byte2] [Byte3] [Byte4]`) 轉換為 16 點布林陣列 (`bool[16]`)
  - 正確處理八進制對應關係（低 Nibble 為 $0\sim3$，高 Nibble 為 $4\sim7$）

### 2. 通訊服務層 (`Fx3uSerialService.cs`)
- **執行緒保護**：利用 `SemaphoreSlim` 或 `lock` 保證輪詢定時器與手動點擊不會同時發送封包。
- **自動偵測與連接**：列舉系統 COM 埠，支援熱插拔與斷線狀態捕獲。
- **日誌事件廣播**：即時回傳發送 (TX) 與接收 (RX) 的十六進制字串，供 UI 記錄顯示。
- **虛擬模擬模式 (Simulation Mode)**：提供切換開關，若無實體 PLC 連接，可啟動虛擬回應模式便於離線測試與演示。

### 3. 使用者介面 (`MainForm`)
- **頂部工具列**：COM 埠選擇、重新整理、鮑率/參數標示、連線/中斷按鈕、模擬模式開關。
- **中左區塊 ($X0 \sim X17$ 輸入接點)**：16 個綠色 LED 指示燈，以 4 點一組清晰排版（$X0\sim X7$ 與 $X10\sim X17$）。
- **中右區塊 ($Y0 \sim Y17$ 輸出接點與控制)**：16 個紅色 LED 指示燈，每個接點附帶專屬的「強制 ON」與「強制 OFF」快速切換按鈕。
- **底部監控控制區**：自動輪詢間隔設定（預設 200ms）、即時通訊頻率顯示。
- **即時通訊終端 (Log Window)**：彩色語法高亮顯示 TX（藍色）與 RX（綠色），提供複製與清除功能。

---

## 驗證計畫

### 1. 自動化測試 (Automated Tests)
- 建立 `Fx3uMonitorApp.Tests` 測試專案：
  - 驗證使用者提供的 $Y0\sim Y17$ 讀取回傳資料 `02 35 35 38 34 03 43 33` 解碼結果是否為：
    - $Y0=\text{ON}, Y2=\text{ON}, Y4=\text{ON}, Y6=\text{ON}, Y12=\text{ON}, Y17=\text{ON}$
  - 驗證 $X0\sim X17$ 讀取回傳解碼結果。
  - 驗證所有 16 點強制 ON/OFF 的 Hex 封包與 Checksum 是否與規格書 100% 吻合。

### 2. 應用程式編譯與建置
- 執行 `dotnet build` 確保 0 錯誤、0 警告。
- 啟動模擬通訊測試，驗證 UI 反應、LED 切換流暢度與 Log 輸出。
