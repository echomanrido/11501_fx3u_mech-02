# 三菱 FX3U 機電整合丙級第二站 (11501) 系統架構與 UML 分析報告

本報告針對 **Fx3uMonitorApp (三菱 FX3U PLC 串列監控與機電整合第二站自動化系統)** 進行全面深入的架構分析，並繪製標準 UML 圖形，涵蓋系統交互時序、類別物件結構、機構狀態機轉移以及自動化流程邏輯。

---

## 1. 循序圖 (Sequence Diagram)

此循序圖呈現從操作人員在主介面按下【機丙二開始】後的完整非同步時序交互，包含：
1. UI 停止背景輪詢（獨佔 COM 埠）。
2. `JiBingErRunner` 啟動檢測流程。
3. `Fx3uSerialService` 封包組裝、Checksum 校驗與 PLC 指令傳輸。
4. **開口朝下翻料連鎖保護**（夾料 $Y3 \rightarrow$ 上升 $Y0 \rightarrow$ 智慧翻轉 $Y6/Y7 \rightarrow$ 下降 $Y1 \rightarrow$ 放料 $Y4$）。
5. 旋臂進料 ($Y2$)、吸料 ($Y5$)、出料位移載、破真空排料 ($Y10$) 與指示燈切換。

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

## 2. 類別圖 (Class Diagram)

此類別圖展示軟體架構的分層設計：
- **UI 展示層**：`MainForm` (WinForms)、`LedBulb` (自繪 GDI+ 指示燈)。
- **業務流程層**：`JiBingErRunner` (機電整合第二站自動化流程協調器)。
- **服務層**：`Fx3uSerialService` (執行緒安全通訊、指令重試、離線模擬器)。
- **協定層**：`Fx3uProtocol` (三菱 FX 系列 16 進制 ASCII 協定封包、Checksum、位元拆解)、`IoPointInfo` (點位資訊模型)。

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

## 3. 狀態機圖 (State Machine Diagram)

此狀態機圖描繪機電整合第二站機構從開機、待機、急停、復歸到執行單一循環或跑馬燈的狀態變更生命週期。

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

## 4. 活動流程圖 (Activity Diagram)

此活動流程圖細緻描繪了系統運行【機電整合丙級第二站核心運動邏輯】的決策路徑與並行保護機制。

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

## 5. 模組互動與職責對應表

| 模組名稱 | 檔案路徑 | 核心職責 | 在架構中的關鍵模式 |
| :--- | :--- | :--- | :--- |
| **`MainForm`** | `Fx3uMonitorApp/MainForm.cs` | 使用者操作介面、指示燈更新、定時輪詢管理、控制指令獨佔協調 | View / Controller、Observer (訂閱 Service/Runner 事件) |
| **`JiBingErRunner`** | `Fx3uMonitorApp/Services/JiBingErRunner.cs` | 機丙第二站動作邏輯、翻料五部曲、智慧雙向旋轉、到位保護 | Command / State Pattern (狀態流程協調器) |
| **`Fx3uSerialService`** | `Fx3uMonitorApp/Services/Fx3uSerialService.cs` | 串列埠封裝 (RS-422)、執行緒安全通訊鎖、重試機制、離線氣壓機構模擬器 | Service Layer、Facade、Thread-Safe Proxy |
| **`Fx3uProtocol`** | `Fx3uMonitorApp/Protocols/Fx3uProtocol.cs` | 三菱編程口二進制/ASCII 協定封包構建、Modulo 256 Checksum 校驗、Nibble 位元解碼 | Protocol Parser / Utility Layer |
| **`LedBulb`** | `Fx3uMonitorApp/Controls/LedBulb.cs` | 自繪漸層抗鋸齒立體發光 LED 元件 | Custom UI Control (GDI+ Rendering) |

---

## 6. 結論與亮點回顧

1. **嚴密的物理互鎖**：在旋臂旋轉前嚴格檢查垂直缸下端點 ($X1=a0$) 與夾爪開極限 ($X4=c0$)，避免旋臂與翻料機構在半空中發生物理干涉碰撞。
2. **防原地瞬間誤判機制**：在啟動翻轉馬達時加入強制運動 400ms 延遲，徹底杜絕微動開關在原點未脫離時被軟體在 0 毫秒內誤判「已到位」而直接跳過的致命瑕疵。
3. **智慧雙向翻轉 (`RotateFlipMotorAsync`)**：自動偵測 $X6(e1)$ 與 $X7(e0)$，依機構所在端點彈性驅動 $Y6(R1)$ 或 $Y7(R2)$，完整實現往復雙向翻料。
4. **通訊零衝突保護**：藉由暫停背景 Polling Timer 與 `SemaphoreSlim` 雙重防護，確保連續強制控制指令具備 100% 傳輸優先權與穩定性。
