# 三菱 FX3U PLC 串列監控與機電整合第二站自動化系統 (C# Windows Forms)

專為 **Visual Studio 2026** (.NET 9.0 Windows Forms) 開發之工業級三菱 MELSEC-F FX3U PLC 串列監控系統與機電整合丙級第二站（11501）自動化控制平台。

---

## 系統架構與 UML 設計模型

本專案採物件導向與分層解耦架構，GitHub 支援原生 Mermaid 渲染，以下展示系統核心運作之四大 UML 模型：

### 1. 循序圖 (Sequence Diagram) - 單一循環與翻料時序交互

展示主視窗、機丙運行器 (`JiBingErRunner`)、串列通訊服務 (`Fx3uSerialService`)、協定引擎 (`Fx3uProtocol`) 與實體/模擬 PLC 之間的非同步時序交互，涵蓋開口朝下翻料五部曲 ($C+ \rightarrow A+ \rightarrow R1/R2 \rightarrow A- \rightarrow C-$) 與旋臂移載排料：

```mermaid
sequenceDiagram
    autonumber
    actor Operator as 操作者 (Operator)
    participant UI as 主視窗 (MainForm)
    participant Runner as 機丙運行器 (JiBingErRunner)
    participant Service as 串列服務 (Fx3uSerialService)
    participant Proto as 協定引擎 (Fx3uProtocol)
    participant PLC as 三菱 FX3U PLC / 模擬器

    Operator->>UI: 選擇情境 0 (自動感測模式)，點擊【▶ 機丙二開始】
    activate UI
    UI->>UI: 暫停背景輪詢 Polling Timer (避免 COM 埠搶佔)
    UI->>Runner: StartAsync(scenarioIndex = 0)
    activate Runner

    Note over Runner, PLC: 階段一：待機狀態與指示燈初始化
    Runner->>Service: ForceYAsync(17, false) [待機燈 GL 滅]
    Service->>Proto: BuildForceYCommand(15, false)
    Service->>PLC: 發送強制指令 (02 38 ... 03 CS)
    PLC-->>Service: 回傳 ACK (0x06)
    Runner->>Service: ForceYAsync(15, true) [運轉燈 RL 亮]
    Service->>PLC: 發送強制指令 Y15 ON
    PLC-->>Service: 回傳 ACK (0x06)

    Note over Runner, PLC: 階段二：工件檢知 (進料、姿勢、顏色)
    Runner->>Service: ReadXAsync()
    Service->>Proto: ReadXCommand
    Service->>PLC: 查詢 X0~X17 狀態 (02 30 00 80 ... 03 CS)
    PLC-->>Service: 回傳 8 Bytes ASCII Hex (02 ... 03 CS)
    Service->>Proto: ParseBits(buffer)
    Proto-->>Service: 回傳 bool[16]
    Service-->>Runner: 返回 X 接點狀態 (X10=s0, X11=s1, X12=s2)

    alt 判定為黑色圓料 (X12=OFF)
        Runner->>Runner: 啟動背景執行緒使 RL (Y15) 0.5s 週期閃爍
    end

    alt 判定為開口朝下 (X11=OFF，需翻料)
        Note over Runner, PLC: 階段三：開口朝下翻料五步時序 (C+ -> A+ -> R1/R2 -> A- -> C-)
        Runner->>Service: ForceYAsync(3, true) [翻料 1/5: 夾爪夾料 C+]
        Service->>PLC: Y3 ON
        PLC-->>Service: ACK
        Runner->>Runner: 延遲 400ms 建立氣壓夾持力

        Runner->>Service: ForceYAsync(0, true) [翻料 2/5: 垂直缸上升 A+]
        Service->>PLC: Y0 ON
        PLC-->>Service: ACK
        Runner->>Runner: WaitForXAsync(X0=a1, 上端點到位)

        Runner->>Runner: RotateFlipMotorAsync() [翻料 3/5: 智慧雙向翻面]
        Runner->>Service: ReadXAsync() -> 檢測 X6(e1), X7(e0)
        alt 在反轉端點 e0 (X7=ON)
            Runner->>Service: ForceYAsync(6, true) [啟動正轉 R1]
            Service->>PLC: Y6 ON
            Runner->>Runner: 強制運轉 400ms (脫離原點防誤判)
            Runner->>Runner: WaitForXAsync(X6=e1, 正轉端點到位)
            Runner->>Service: ForceYAsync(6, false) [停止馬達]
        else 在正轉端點 e1 (X6=ON)
            Runner->>Service: ForceYAsync(7, true) [啟動反轉 R2]
            Service->>PLC: Y7 ON
            Runner->>Runner: 強制運轉 400ms (脫離原點防誤判)
            Runner->>Runner: WaitForXAsync(X7=e0, 反轉端點到位)
            Runner->>Service: ForceYAsync(7, false) [停止馬達]
        end

        Runner->>Service: ForceYAsync(1, true) [翻料 4/5: 垂直缸下降回進料座 A-]
        Service->>PLC: Y1 ON
        Runner->>Runner: WaitForXAsync(X1=a0, 下端點到位)
        Runner->>Service: ForceYAsync(1, false)

        Runner->>Service: ForceYAsync(4, true) [翻料 5/5: 夾爪張開釋放 C-]
        Service->>PLC: Y4 ON
        Runner->>Runner: WaitForXAsync(X4=c0, 開極限到位)
        Runner->>Service: ForceYAsync(4, false)
    end

    Note over Runner, PLC: 階段四：迴轉旋臂移載與真空吸取排料
    Runner->>Runner: 互鎖確認 (垂直缸在下位 X1 且 夾爪開極限 X4)
    Runner->>Service: ForceYAsync(2, true) [迴轉缸轉向進料端 B]
    Service->>PLC: Y2 ON
    PLC-->>Service: ACK
    Runner->>Runner: WaitForXAsync(X2=b1, 進料端點到位)

    Runner->>Service: ForceYAsync(5, true) [真空吸盤吸料 D+]
    Service->>PLC: Y5 ON
    Runner->>Runner: WaitForXAsync(X5=ps1, 負壓開關閉合)

    Runner->>Service: ForceYAsync(2, false) [迴轉缸轉回出料端 B OFF]
    Service->>PLC: Y2 OFF
    Runner->>Runner: WaitForXAsync(X3=b0, 出料端點到位)

    Runner->>Service: ForceYAsync(5, false) [關閉吸盤 D+]
    Runner->>Service: ForceYAsync(8, true) [破真空排料 D- (Y10)]
    Service->>PLC: Y10 ON
    Runner->>Runner: 延遲 400ms 吹氣破真空
    Runner->>Service: ForceYAsync(8, false)

    Note over Runner, PLC: 階段五：循環完成與復歸待機
    Runner->>Service: ForceYAsync(15, false) [運轉燈 RL 滅]
    Runner->>Service: ForceYAsync(17, true) [待機燈 GL 亮]
    Runner-->>UI: 發出 Finished(true) 事件
    deactivate Runner

    UI->>UI: 重啟背景輪詢 Polling Timer
    UI->>UI: 恢復介面按鈕狀態 (【▶ 機丙二開始】)
    UI-->>Operator: 介面日誌提示「機丙二單一循環完成」
    deactivate UI
```

---

### 2. 類別圖 (Class Diagram) - 模組與物件導向架構

展示 UI 展示層 (`MainForm`、`LedBulb`)、流程控制層 (`JiBingErRunner`)、通訊服務層 (`Fx3uSerialService`) 與協定引擎 (`Fx3uProtocol`、`IoPointInfo`) 之職責劃分與關聯：

```mermaid
classDiagram
    direction TB

    class MainForm {
        -Fx3uSerialService _serialService
        -JiBingErRunner _jiBingErRunner
        -System.Windows.Forms.Timer _pollingTimer
        -LedBulb[] _ledX
        -LedBulb[] _ledY
        -Button[] _btnYOn
        -Button[] _btnYOff
        +MainForm()
        -BtnConnect_Click(object sender, EventArgs e)
        -BtnStartJiBingEr_Click(object sender, EventArgs e)
        -BtnMarquee_Click(object sender, EventArgs e)
        -OnPollingTimerTick(object sender, EventArgs e)
        -UpdateConnectionUi(bool isConnected)
        -UpdateXDisplays(bool[] xPoints)
        -UpdateYDisplays(bool[] yPoints)
        -AppendLog(string tag, string msg, bool isSuccess)
    }

    class JiBingErRunner {
        -Fx3uSerialService _serialService
        -CancellationTokenSource _cts
        +bool IsRunning
        +bool InvertS1Polarity
        +event Action~string~ StepChanged
        +event Action~bool, string~ Finished
        +JiBingErRunner(Fx3uSerialService serialService)
        +StartAsync(int scenarioIndex) Task
        +Stop() void
        +RunMarqueeAsync(CancellationToken ct) Task
        -RunSingleCycleAsync(int scenarioIndex, CancellationToken ct) Task
        -RunFlipOnlyAsync(CancellationToken ct) Task
        -RunHomingAsync(CancellationToken ct) Task
        -RotateFlipMotorAsync(CancellationToken ct) Task
        -WaitForXAsync(int xIndex, bool expected, string desc, int timeoutMs, CancellationToken ct) Task
        -RunBlinkingLightAsync(int yIndex, int intervalMs, CancellationToken ct) Task
    }

    class Fx3uSerialService {
        -SerialPort _serialPort
        -SemaphoreSlim _commLock
        -bool[] _simulatedX
        -bool[] _simulatedY
        -bool _isSimConnected
        +bool IsSimulationMode
        +bool EnableCommLog
        +bool IsConnected
        +event Action~bool~ ConnectionChanged
        +event Action~string, string, bool~ LogOccurred
        +event Action~bool[]~ XStatusUpdated
        +event Action~bool[]~ YStatusUpdated
        +Connect(string portName) void
        +Disconnect() void
        +ReadXAsync(CancellationToken ct) Task~bool[]~
        +ReadYAsync(CancellationToken ct) Task~bool[]~
        +ForceYAsync(int index, bool state, CancellationToken ct) Task~bool~
        +SetSimulatedWorkpiece(bool isOpeningDown, bool isRed) void
        +ToggleSimulatedX(int index) void
        -SendAndReceiveAsync(byte[] sendPacket, int expectedLength, CancellationToken ct) Task~byte[]~
        -SimulateMechanicalResponse(int yIndex, bool state) void
        -GenerateSimulatedResponse(byte[] sendPacket, int expectedLength) byte[]
    }

    class Fx3uProtocol {
        <<static>>
        +byte STX = 0x02
        +byte ETX = 0x03
        +byte ACK = 0x06
        +byte NAK = 0x15
        +byte[] ReadYCommand
        +byte[] ReadXCommand
        +string[] OctalPointNames
        +IoPointInfo[] XPointDefinitions
        +IoPointInfo[] YPointDefinitions
        +CalculateChecksum(byte[] body) byte[]
        +VerifyChecksum(byte[] packet) bool
        +BuildForceYCommand(int index, bool state) byte[]
        +ParseBits(byte[] response8Bytes) bool[]
        +ToHexString(byte[] data) string
    }

    class IoPointInfo {
        +string Name
        +string Code
        +string Description
        +string DisplayText
        +IoPointInfo(string name, string code, string description)
    }

    class LedBulb {
        -Color _color
        -Color _offColor
        -bool _isOn
        +Color Color
        +Color OffColor
        +bool On
        +LedBulb()
        #OnPaint(PaintEventArgs e) void
    }

    MainForm --> JiBingErRunner : 組合 / 觸發運算
    MainForm --> Fx3uSerialService : 組合 / 狀態訂閱
    MainForm *-- LedBulb : 包含 32 組燈號 UI
    JiBingErRunner --> Fx3uSerialService : 依賴 (發送動作與讀取感測)
    Fx3uSerialService ..> Fx3uProtocol : 呼叫靜態協定編解碼
    Fx3uProtocol *-- IoPointInfo : 包含 16 點輸入與 16 點輸出定義
```

---

### 3. 狀態機圖 (State Machine Diagram) - 機構生命週期與模式轉移

描繪機構從開機、待機、急停、復歸、跑馬燈到工作模式的完整狀態轉移：

```mermaid
stateDiagram-v2
    [*] --> Disconnected : 啟動應用程式

    state Disconnected {
        [*] --> Closed
        Closed --> Connecting : 點擊【連線】/ 勾選模擬
        Connecting --> Closed : 失敗
    }

    Disconnected --> Standby : 連線成功 / 模擬建立
    Standby --> Disconnected : 點擊【中斷連線】

    state Standby {
        [*] --> Idle
        note right of Idle
            機構回到機械原點：
            • 垂直導桿滑台在下位 (X1=a0)
            • 迴轉缸在出料位 (X3=b0)
            • 氣壓夾爪水平張開 (X4=c0)
            • 迴轉馬達 M 停止於反轉原點 (X7=e0)
            • 指示燈：待機燈 GL 亮 (Y17), 運轉燈 RL 滅 (Y15)
        end note
    }

    Standby --> Inspection : 按下 ST/RST (X16) 或 點擊【機丙二開始】
    Standby --> Homing : 切換 COS1 至「復歸模式」(X13=ON) 並按 ST/RST
    Standby --> Marquee : 點擊【▶ 跑馬燈開始】
    Standby --> FlipOnly : 切換 COS2 至「指定功能」(X14=ON) 並按 ST/RST

    state Inspection {
        [*] --> CheckWorkpiece : 等待有無料感測器 X10 (s0)
        CheckWorkpiece --> ReadSensors : X10 = ON
        ReadSensors --> CheckOrientation : 讀取姿勢 X11(s1) 與 顏色 X12(s2)
    }

    Inspection --> Standby : 連續循環逾時 (10 秒無料)

    state FlippingSequence {
        [*] --> ClampWorkpiece : 氣壓夾爪夾料 (Y3 C+)
        ClampWorkpiece --> VerticalLift : 延遲 400ms -> 垂直缸上升 (Y0 A+)
        VerticalLift --> MotorRotate : 到位 X0(a1) -> 啟動迴轉馬達
        state MotorRotate {
            [*] --> EvalDirection
            EvalDirection --> ForwardRotate : 若在 e0(X7=ON) -> Y6(R1) 正轉
            EvalDirection --> ReverseRotate : 若在 e1(X6=ON) -> Y7(R2) 反轉
            ForwardRotate --> StopMotor : 400ms延遲 + 等待 X6 到位
            ReverseRotate --> StopMotor : 400ms延遲 + 等待 X7 到位
        }
        MotorRotate --> VerticalLower : 垂直缸下降 (Y1 A-) 回進料座
        VerticalLower --> ReleaseClamp : 到位 X1(a0) -> 夾爪張開 (Y4 C-)
        ReleaseClamp --> [*] : 到位 X4(c0)，翻料完成
    }

    Inspection --> FlippingSequence : 判定開口朝下 (X11=OFF)
    Inspection --> ArmTransfer : 判定開口朝上 (X11=ON，垂直缸保持下位)
    FlippingSequence --> ArmTransfer : 翻料完成 (工件已朝上)

    state ArmTransfer {
        [*] --> SafetyInterlock : 確認 X1=下位 且 X4=夾爪張開
        SafetyInterlock --> RotaryToFeed : 迴轉缸轉向進料端 (Y2 B)
        RotaryToFeed --> VacuumSuction : 到位 X2(b1) -> 吸盤吸料 (Y5 D+)
        VacuumSuction --> RotaryToDischarge : 負壓到位 X5(ps1) -> 迴轉缸轉回出料端 (Y2 OFF)
        RotaryToDischarge --> Discharge : 到位 X3(b0) -> 破真空釋放 (Y10 D-)
        Discharge --> [*] : 吹氣 400ms 完成排料
    }

    ArmTransfer --> Standby : 單一循環排料完成 (GL 亮, RL 滅)
    ArmTransfer --> Inspection : 連續循環模式 (延遲 3~5 秒接下一料)

    state Homing {
        [*] --> HomeClamp : 夾爪張開 (Y4 C-)
        HomeClamp --> HomeVacuum : 破真空釋放 (Y10 D-)
        HomeVacuum --> HomeArm : 迴轉缸回出料端 (Y2 OFF, 等待 X3)
        HomeArm --> HomeVertical : 垂直缸下降回位 (Y1 A-, 等待 X1)
        HomeVertical --> HomeMotor : 檢查馬達反轉原點 (若非 e0 則反轉 Y7 至 X7)
        HomeMotor --> [*] : 復歸黃燈 YL 閃爍滿 3 秒，結束
    }
    Homing --> Standby : 機構回機械原點完畢

    state Marquee {
        [*] --> StageRed : 紅燈亮 (Y15)，黃綠滅 (1 秒)
        StageRed --> StageYellow : 黃燈亮 (Y16)，紅綠滅 (1 秒)
        StageYellow --> StageGreen : 綠燈亮 (Y17)，紅黃滅 (1 秒)
        StageGreen --> AllOff : 全熄滅 (Y15/Y16/Y17 OFF)
        AllOff --> [*]
    }
    Marquee --> Standby : 跑馬燈展示完成或手動停止

    state FlipOnly {
        [*] --> FlipOnlySeq : 運轉燈 RL 閃爍 -> 執行翻料五步時序
        FlipOnlySeq --> [*] : 翻料完成回原點
    }
    FlipOnly --> Standby : 翻料結束

    %% 緊急停止轉換
    Inspection --> EmergencyStop : 按下 EMS (X17 斷開)
    FlippingSequence --> EmergencyStop : 按下 EMS (X17 斷開)
    ArmTransfer --> EmergencyStop : 按下 EMS (X17 斷開)
    Homing --> EmergencyStop : 按下 EMS (X17 斷開)

    state EmergencyStop {
        [*] --> CutPower : 電磁閥與馬達斷電 (停止運動)
        CutPower --> KeepHold : 吸盤與夾爪保持現況，防工件墜落
    }
    EmergencyStop --> Homing : 解除 EMS 後切換復歸模式並按 ST/RST
```

---

### 4. 活動流程圖 (Activity Diagram) - 核心運動邏輯與安全互鎖

呈現詳細的感測決策路徑、顏色判別、姿勢判別、防原地誤判馬達翻轉與旋臂移載排料：

```mermaid
flowchart TD
    Start(["開始 (啟動運行流程)"]) --> InitUI["UI 介面鎖定並暫停背景輪詢 Timer"]
    InitUI --> TurnOnRL["點亮運轉燈 RL (Y15 ON)，熄滅待機燈 GL (Y17 OFF)"]
    TurnOnRL --> CheckS0{"進料座是否有料?<br/>(感測器 X10 [s0])"}

    CheckS0 -- 無料 (False) --> Wait10s{"是否連續循環模式且超過 10 秒?"}
    Wait10s -- 是 (逾時) --> FinishNoFeed["終止流程，提示無料"]
    Wait10s -- 否 --> DelayRetry["延遲 200ms 重新感測"] --> CheckS0

    CheckS0 -- 有料 (True) --> DetectColor{"顏色感測器 X12 [s2]?"}

    DetectColor -- "紅色 (X12=ON)" --> RLSteady["運轉燈 RL (Y15) 維持常亮"]
    DetectColor -- "黑色 (X12=OFF)" --> RLBlink["啟動背景執行緒：運轉燈 RL 以 0.5s 週期閃爍"]

    RLSteady --> DetectOrientation{"姿勢感測器 X11 [s1]?"}
    RLBlink --> DetectOrientation

    %% 開口朝上分支
    DetectOrientation -- "開口朝上 (X11=ON)" --> LogNoFlip["記錄：開口朝上無需翻料，垂直缸嚴格保持在下位"]
    LogNoFlip --> CheckSafetyInterlock

    %% 開口朝下分支
    DetectOrientation -- "開口朝下 (X11=OFF)" --> LogFlip["記錄：開口朝下，啟動翻料程序 (C+/A+/旋轉/A-/C-)"]
    LogFlip --> Step1["翻料 1/5: 氣壓夾爪夾料 (Y3 C+)，延遲 400ms 建立氣壓"]
    Step1 --> Step2["翻料 2/5: 垂直缸上升 (Y0 A+)，等待 X0 [a1] 到位"]
    Step2 --> ReadMotorEndpoints["讀取翻轉馬達端點狀態 (X6 [e1], X7 [e0])"]

    ReadMotorEndpoints --> MotorDecision{"馬達當前端點位置?"}
    MotorDecision -- "在反轉端點 (X7=ON, X6=OFF)" --> RotForward["啟動迴轉馬達正轉 (Y6 R1)"]
    MotorDecision -- "在正轉端點 (X6=ON, X7=OFF)" --> RotReverse["啟動迴轉馬達反轉 (Y7 R2)"]
    MotorDecision -- "兩者皆非 (中間或未知)" --> RotForward

    RotForward --> ForcedDelayFwd["強制運動 400ms (防止原地瞬間誤判)"]
    RotReverse --> ForcedDelayRev["強制運動 400ms (防止原地瞬間誤判)"]

    ForcedDelayFwd --> WaitX6["等待 X6 [e1] 正轉端點到位 (逾時 5000ms)"]
    ForcedDelayRev --> WaitX7["等待 X7 [e0] 反轉端點到位 (逾時 5000ms)"]

    WaitX6 --> StopMotor["停止馬達輸出 (Y6 OFF / Y7 OFF)"]
    WaitX7 --> StopMotor

    StopMotor --> Step4["翻料 4/5: 垂直缸下降回進料座 (Y1 A-)，等待 X1 [a0] 到位"]
    Step4 --> Step5["翻料 5/5: 氣壓夾爪放鬆 (Y4 C-)，等待 X4 [c0] 到位"]
    Step5 --> CheckSafetyInterlock

    %% 旋臂移載流程
    CheckSafetyInterlock{"旋臂安全互鎖檢查:<br/>垂直缸在下位 (X1=ON) 且 夾爪張開 (X4=ON)?"}
    CheckSafetyInterlock -- 未滿足安全條件 --> WaitSafe["等待機構回到下位與開極限"] --> CheckSafetyInterlock
    CheckSafetyInterlock -- 通過安全互鎖 --> ArmToFeed["迴轉缸轉向進料端 (Y2 B)，等待 X2 [b1] 到位"]

    ArmToFeed --> SuctionOn["真空吸盤啟動吸料 (Y5 D+)，等待壓力開關 X5 [ps1] 閉合"]
    SuctionOn --> ArmToDischarge["迴轉缸轉回出料端 (Y2 OFF)，等待 X3 [b0] 到位"]
    ArmToDischarge --> SuctionOff["真空吸盤關閉 (Y5 OFF)"]
    SuctionOff --> BlowRelease["啟動破真空吹氣 (Y10 D-)，延遲 400ms 釋放圓料"]
    BlowRelease --> BlowOff["關閉破真空電磁閥 (Y10 OFF)"]

    BlowOff --> CheckMode{"選擇開關模式 (COS2)?"}
    CheckMode -- "單一循環" --> ResetStandby["機構確認回原點，運轉燈 RL 滅，待機燈 GL 亮"]
    CheckMode -- "連續循環" --> DelayNext["進料延遲 3~5 秒"] --> CheckS0

    ResetStandby --> EndSuccess(["流程正常結束 (回呼 Finished 事件，重啟輪詢)"])
    FinishNoFeed --> ResetStandby
```

---

## 專案特性
- **嚴格遵循三菱 FX Programming Port Protocol**：
  - 完整支援 $X0 \sim X17$ 輸入接點即時狀態監控。
  - 完整支援 $Y0 \sim Y17$ 輸出接點即時狀態監控與單點強制 ON/OFF 控制。
  - 正確處理八進制接點高低位 Nibble 映射解碼。
  - 具備 Checksum 檢查碼自動計算與雙向驗證。
- **串列通訊 (SerialPort)**：
  - 固定參數：`BaudRate = 9600`, `DataBits = 7`, `Parity = Even`, `StopBits = 1`。
  - 具備執行緒防衝突鎖定 (`SemaphoreSlim`)，確保自動輪詢與手動按鈕不發生衝突。
  - 自動列舉可用 COM 埠，支援連線/中斷連線與逾時重試。
- **立體質感圓形 LED 指示燈 (`LedBulb`)**：
  - 輸入接點 ($X$)：亮綠色高光指示燈。
  - 輸出接點 ($Y$)：鮮紅色高光指示燈。
  - 支援點擊測試與即時文字顯示。
- **內建離線模擬測試模式 (Simulation Mode)**：
  - 無需實體 PLC 或轉接線，勾選「離線模擬模式」即可完整測試全功能：
    - 點擊任一 $X$ 點即可即時模擬輸入開關訊號切換。
    - 點擊 $Y$ 點「ON」/「OFF」可模擬輸出動作與即時燈號反饋。
- **「機丙二開始」自動程序運行器 (`JiBingErRunner`)**：
  - 專屬整合機電整合丙級第二站（11501）自動程序。
  - 支援單一循環（紅/黑料件、朝上/朝下翻料）、連續循環（自動排料與 10s 無料終止）、指定功能（翻料一次）及自動復歸模式（YL 閃爍 3 秒回原點）。
  - 具備**防原地瞬間誤判機制**與**智慧雙向旋轉翻料**。
- **TX / RX Raw Hex 通訊封包即時日誌視窗**：
  - 語法彩色標示（TX 天藍色、RX 嫩綠色、機丙二進度紫色、系統橘黃色、錯誤紅色）。
  - 自動滾動與一鍵清除功能。

---

## 專案結構
```
11501_fx3u/
├── FX3U_Monitor_Specification.md   # 軟體詳細規格書 (包含所有指令與 Checksum)
├── Fx3uMonitor.slnx                # Visual Studio 2026 方案檔
├── README.md                       # 專案操作指南與說明文件 (含完整 UML)
├── .gitignore                      # 標準 .NET / Visual Studio 忽略規則
├── Fx3uMonitorApp/                 # Windows Forms 主專案 (.NET 9.0-windows)
│   ├── Controls/
│   │   └── LedBulb.cs              # 自繪高品質立體平滑圓形 LED 元件
│   ├── Protocols/
│   │   └── Fx3uProtocol.cs         # FX 編程口通訊協定引擎 (指令組裝/Checksum/解碼)
│   ├── Services/
│   │   ├── Fx3uSerialService.cs    # 串列埠通訊管理、執行緒保護與模擬核心
│   │   └── JiBingErRunner.cs       # 機丙二核心運動控制器 (翻料/移載/跑馬燈)
│   ├── MainForm.cs                 # 主視窗控制邏輯
│   ├── MainForm.Designer.cs        # 工業風 UI 排版
│   └── Program.cs                  # 程式啟動點
├── Fx3uMonitorApp.Tests/           # xUnit 單元測試專案 (44 項自動化驗證測試)
│   ├── ProtocolTests.cs            # 32 條強制指令、讀取指令與 Checksum 驗證
│   └── ServiceTests.cs             # 服務層全流程模擬整合測試 (含翻料時序驗證)
└── md/                             # Markdown 專案詳細文件庫
    ├── FX3U_Monitor_Specification.md
    ├── UML_Architecture_Analysis.md # 四大 UML 架構專題分析報告
    └── walkthrough.md              # 實作進度與歷史歷程回顧
```

---

## 快速開始與編譯執行

### 1. 開發工具
* **Visual Studio 2026**（或 Visual Studio 2022 v17.10+）
* 需安裝工作負載：**.NET 桌面開發 (.NET Desktop Development)**

### 2. 命令列編譯與測試
若您習慣使用命令列，可在本目錄直接執行：

```powershell
# 建置整個解決方案
dotnet build

# 執行所有單元測試與協定驗證 (44 項測試 100% 通過)
dotnet test

# 啟動應用程式
dotnet run --project Fx3uMonitorApp/Fx3uMonitorApp.csproj
```

---

## 指令對照快速查閱

### 1. 讀取指令
- **讀取 $Y0 \sim Y17$**：`02 30 30 30 41 30 30 32 03 36 36`
- **讀取 $X0 \sim X17$**：`02 30 30 30 38 30 30 32 03 35 44`

### 2. 強制控制指令
- 強制 ON 指令碼為 `0x37` ('7')，強制 OFF 指令碼為 `0x38` ('8')。
- 完整 16 組接點十六進制封包已封裝於 `Fx3uProtocol.BuildForceYCommand(index, state)` 中。
