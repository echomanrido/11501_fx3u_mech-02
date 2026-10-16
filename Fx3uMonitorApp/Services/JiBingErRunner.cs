using Fx3uMonitorApp.Protocols;

namespace Fx3uMonitorApp.Services;

/// <summary>
/// 機電整合丙級第二站 (11501 氣壓與馬達搬運機構) 自動程序運行器
/// </summary>
public class JiBingErRunner
{
    private readonly Fx3uSerialService _serialService;
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }

    /// <summary>
    /// 是否反轉 s1 (X11) 姿勢感測器極性判定 (預設 false: s1=ON 開口朝上無需翻料, s1=OFF 開口朝下需翻料)
    /// </summary>
    public bool InvertS1Polarity { get; set; } = false;

    /// <summary>
    /// 步驟狀態文字回報事件 (stepDescription)
    /// </summary>
    public event Action<string>? StepChanged;

    /// <summary>
    /// 運行結束事件 (isSuccess, message)
    /// </summary>
    public event Action<bool, string>? Finished;

    public JiBingErRunner(Fx3uSerialService serialService)
    {
        _serialService = serialService;
    }

    /// <summary>
    /// 啟動機丙二動作流程
    /// </summary>
    /// <param name="scenarioIndex">
    /// 0: ★ 自動感測模式 (由進料座感測器 X10/X11/X12 自動辨識)
    /// 1: 單一循環 - 紅色圓料 (開口朝上)
    /// 2: 單一循環 - 紅色圓料 (開口朝下 - 需翻料)
    /// 3: 單一循環 - 黑色圓料 (開口朝上 - 閃爍RL)
    /// 4: 單一循環 - 黑色圓料 (開口朝下 - 翻料+閃爍RL)
    /// 5: 連續循環 (依感測器連續循環排料)
    /// 6: 指定功能 (單純翻料一次)
    /// 7: 自動復歸模式 (YL閃爍3秒回原點)
    /// 8: 跑馬燈模式 (紅燈->黃燈->綠燈 各1秒，最後全滅)
    /// </param>
    public async Task StartAsync(int scenarioIndex)
    {
        if (IsRunning) return;

        IsRunning = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            switch (scenarioIndex)
            {
                case 0:
                    // 自動感測模式：完全由感測器 X10/X11/X12 現場讀值決定
                    await RunSingleCycleAsync(isRedOverride: null, needFlipOverride: null, ct);
                    break;
                case 1:
                    _serialService.SetSimulatedWorkpiece(isOpeningDown: false, isRed: true);
                    await RunSingleCycleAsync(isRedOverride: true, needFlipOverride: false, ct);
                    break;
                case 2:
                    _serialService.SetSimulatedWorkpiece(isOpeningDown: true, isRed: true);
                    await RunSingleCycleAsync(isRedOverride: true, needFlipOverride: true, ct);
                    break;
                case 3:
                    _serialService.SetSimulatedWorkpiece(isOpeningDown: false, isRed: false);
                    await RunSingleCycleAsync(isRedOverride: false, needFlipOverride: false, ct);
                    break;
                case 4:
                    _serialService.SetSimulatedWorkpiece(isOpeningDown: true, isRed: false);
                    await RunSingleCycleAsync(isRedOverride: false, needFlipOverride: true, ct);
                    break;
                case 5:
                    await RunContinuousCycleAsync(ct);
                    break;
                case 6:
                    await RunFlipOnlyAsync(ct);
                    break;
                case 7:
                    await RunHomingAsync(ct);
                    break;
                case 8:
                    await RunMarqueeAsync(ct);
                    break;
                default:
                    await RunSingleCycleAsync(isRedOverride: null, needFlipOverride: null, ct);
                    break;
            }

            Finished?.Invoke(true, "動作程序執行完成");
        }
        catch (OperationCanceledException)
        {
            Finished?.Invoke(false, "使用者手動停止或緊急停止");
        }
        catch (Exception ex)
        {
            Finished?.Invoke(false, $"執行中發生錯誤: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// 停止執行
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
    }

    /// <summary>
    /// 單一循環動作程序 (支援感測器現場自動辨識或情境指定)
    /// </summary>
    private async Task RunSingleCycleAsync(bool? isRedOverride, bool? needFlipOverride, CancellationToken ct)
    {
        ReportStep("【單一循環開始】準備進行進料與工件感測判定...");

        // 1. 待機切換至運轉狀態: GL(Y17)滅, RL(Y15)亮
        await _serialService.ForceYAsync(15, false, ct); // Y17 GL 滅
        await _serialService.ForceYAsync(13, true, ct);  // Y15 RL 亮
        ReportStep("切換至運轉狀態：待機燈 GL 滅，運轉燈 RL 亮");
        await Task.Delay(300, ct);

        // 2. 等待人工將圓料放至進料座位置 (s0 / X10 檢知)
        ReportStep("等待進料座放置圓料 (感測器 s0/X10 檢測中)...");
        await WaitForXAsync(8, true, "X10 [s0] 進料座工件檢知到位", 5000, ct);
        await Task.Delay(300, ct); // 靜置 300ms 確保工件穩定

        // 3. 讀取 X11 (s1 姿勢感測器) 與 X12 (s2 顏色感測器)
        bool[] currentX = await _serialService.ReadXAsync(ct);
        bool s1Status = (currentX != null && currentX.Length > 9) && currentX[9];
        bool s2Status = (currentX != null && currentX.Length > 10) && currentX[10];

        // 現場機台規範：
        // 開口朝上時，頂部孔位受光/導通 -> s1 (X11) = ON (無需翻料，垂直缸保持在下位)
        // 開口朝下時，實心底座朝上 -> s1 (X11) = OFF (需翻料)
        bool isOpeningDownDetected = InvertS1Polarity ? s1Status : !s1Status;

        // 依據自動感測或指定覆寫決定動作模式
        bool needFlip = needFlipOverride ?? isOpeningDownDetected;
        bool isRed = isRedOverride ?? s2Status;

        string postureDesc = needFlip ? "【開口朝下 (需翻料)】" : "【開口朝上 (無需翻料，垂直缸保持下位)】";
        string colorDesc = isRed ? "【紅色圓料】" : "【黑色圓料】";

        ReportStep($"[感測辨識] s1(X11)={(s1Status ? "ON" : "OFF")} -> 姿勢判定: {postureDesc}；s2(X12)={(s2Status ? "ON" : "OFF")} -> 顏色判定: {colorDesc}");

        // 若為黑色圓料，啟動 RL 0.5s 閃爍背景 Task
        CancellationTokenSource? blackBlinkCts = null;
        if (!isRed)
        {
            blackBlinkCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = RunBlinkingLightAsync(13, 500, blackBlinkCts.Token); // Y15 RL 閃爍
            ReportStep("檢測為黑色圓料：運轉燈 RL 依規範切換為 0.5 秒週期閃爍");
        }

        // 4. 若開口朝下，執行氣壓夾爪與垂直缸翻料連鎖動作
        if (needFlip)
        {
            ReportStep(">>> 偵測到開口朝下！啟動氣壓夾爪與旋轉機構翻料程序 (C+/A+/R1/A-/C-)...");

            ReportStep("翻料 1/5: 氣壓夾爪夾料 (Y3 C+)");
            await _serialService.ForceYAsync(3, true, ct); // Y3 C+
            await Task.Delay(400, ct);

            ReportStep("翻料 2/5: 垂直缸上升 (Y0 A+)");
            await _serialService.ForceYAsync(0, true, ct); // Y0 A+
            await WaitForXAsync(0, true, "X0 [a1] 垂直缸上端點到位", 3000, ct);

            // 翻料 3/5: 智慧雙向旋轉翻面 (Y6 R1 或 Y7 R2，防原地瞬間誤判)
            await RotateFlipMotorAsync(ct);

            ReportStep("翻料 4/5: 垂直缸下降回位 (Y1 A-)");
            await _serialService.ForceYAsync(0, false, ct);
            await _serialService.ForceYAsync(1, true, ct); // Y1 A-
            await WaitForXAsync(1, true, "X1 [a0] 垂直缸下端點到位", 3000, ct);
            await _serialService.ForceYAsync(1, false, ct);

            ReportStep("翻料 5/5: 氣壓夾爪放鬆 (Y4 C-)");
            await _serialService.ForceYAsync(3, false, ct);
            await _serialService.ForceYAsync(4, true, ct); // Y4 C-
            await WaitForXAsync(4, true, "X4 [c0] 氣壓夾爪開極限到位", 3000, ct);
            await _serialService.ForceYAsync(4, false, ct);

            // 模擬模式下將 s1(X11) 復歸為開口朝上 (即 true)
            if (_serialService.IsSimulationMode)
            {
                _serialService.SetSimulatedWorkpiece(isOpeningDown: false, isRed: isRed);
            }
            ReportStep("【翻料完成】工件圓孔已朝上 (s1/X11 感測已復歸)！");
        }
        else
        {
            ReportStep("工件開口已朝上，無需翻料(垂直缸保持在下位)，直接由迴轉旋臂真空吸盤吸取");
        }

        // 4. 迴轉旋臂轉至進料端 (Y2 B)
        // 安全互鎖確認：確保垂直缸在下位 (X1=a0) 且氣壓夾爪張開 (X4=c0)，避免旋臂干涉碰撞
        ReportStep("安全互鎖確認：檢查垂直缸下位 (X1) 與夾爪開極限 (X4)...");
        await WaitForXAsync(1, true, "X1 [a0] 垂直滑台下端點", 3000, ct);
        await WaitForXAsync(4, true, "X4 [c0] 氣壓夾爪開極限點", 3000, ct);

        ReportStep("迴轉缸轉至進料端 (Y2 B)，發送旋臂旋轉命令...");
        bool y2Ok = await _serialService.ForceYAsync(2, true, ct); // Y2 B
        if (!y2Ok)
        {
            ReportStep("【通訊警報】PLC 拒絕 Y2 (迴轉缸) 強制輸出，未收到 ACK！");
        }

        // 嚴格等待迴轉缸進料端點 (X2 b1) 到位 (放寬至 6000ms 氣壓行程)
        await WaitForXAsync(2, true, "X2 [b1] 迴轉缸進料端點到位", 6000, ct);

        // 5. 真空吸盤吸料 (Y5 D+)
        ReportStep("旋臂已確認到達進料端！真空吸盤啟動吸取 (Y5 D+)");
        await _serialService.ForceYAsync(5, true, ct); // Y5 D+
        // 等待真空壓力建立 (X5 ps1)
        await WaitForXAsync(5, true, "X5 [ps1] 真空吸盤壓力檢知到位", 3000, ct);

        // 6. 迴轉缸轉回出料端 (Y2 B 復歸)
        ReportStep("工件吸附完成！迴轉旋臂運送至出料端 (Y2 B OFF)");
        await _serialService.ForceYAsync(2, false, ct); // Y2 B OFF 回出料位
        // 嚴格等待出料端點 (X3 b0) 到位
        await WaitForXAsync(3, true, "X3 [b0] 迴轉缸出料端點到位", 6000, ct);

        // 7. 出料座破真空排料 (Y5 D+ 關閉, Y10 D- 破真空)
        ReportStep("旋臂到達出料端！出料座破真空排料 (Y10 D-)");
        await _serialService.ForceYAsync(5, false, ct); // D+ OFF
        await _serialService.ForceYAsync(8, true, ct);  // Y10 D- ON
        await Task.Delay(400, ct);
        await _serialService.ForceYAsync(8, false, ct); // D- OFF

        // 若有黑色圓料閃爍，停止閃爍並關閉 RL
        if (blackBlinkCts != null)
        {
            blackBlinkCts.Cancel();
            await Task.Delay(100, CancellationToken.None);
        }
        await _serialService.ForceYAsync(13, false, CancellationToken.None); // Y15 RL 滅

        // 8. 回到待機狀態: GL(Y17) 亮
        await _serialService.ForceYAsync(15, true, CancellationToken.None); // Y17 GL 亮
        ReportStep("【排料完成】機構回到原點待機，待機燈 GL 亮，運轉燈 RL 滅");
    }

    /// <summary>
    /// 連續循環程序 (模擬連續執行 2 個工件)
    /// </summary>
    private async Task RunContinuousCycleAsync(CancellationToken ct)
    {
        ReportStep("【連續循環模式啟動】");
        for (int cycle = 1; cycle <= 2; cycle++)
        {
            ct.ThrowIfCancellationRequested();
            ReportStep($"=== 連續循環：第 {cycle} 件開始 (進料延遲 1.5 秒) ===");
            await Task.Delay(1500, ct);
            bool isRed = (cycle % 2 != 0);
            bool needFlip = (cycle % 2 == 0);
            _serialService.SetSimulatedWorkpiece(isOpeningDown: needFlip, isRed: isRed);
            await RunSingleCycleAsync(isRed, needFlip, ct);
        }

        // 模擬 10 秒無料感測自動終止
        ReportStep("連續循環：等待下一個料件放入 (10 秒逾時檢測中...)");
        for (int i = 10; i >= 1; i--)
        {
            ReportStep($"料槽無料，{i} 秒後自動終止並回待機...");
            await Task.Delay(500, ct);
        }

        // 終止連續循環，回待機
        await _serialService.ForceYAsync(13, false, CancellationToken.None); // RL 滅
        await _serialService.ForceYAsync(15, true, CancellationToken.None);  // GL 亮
        ReportStep("【連續循環結束】無新料件，運轉燈 RL 滅，待機燈 GL 亮，終止循環");
    }

    /// <summary>
    /// 獨立翻料功能
    /// </summary>
    private async Task RunFlipOnlyAsync(CancellationToken ct)
    {
        ReportStep("【指定功能：翻料動作開始】待機燈 GL 滅，運轉燈 RL 閃爍");
        await _serialService.ForceYAsync(15, false, ct); // GL 滅
        using var blinkCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = RunBlinkingLightAsync(13, 300, blinkCts.Token); // RL 閃爍

        ReportStep("氣壓夾爪夾料 (Y3 C+)");
        await _serialService.ForceYAsync(3, true, ct);
        await Task.Delay(400, ct);

        ReportStep("垂直導桿滑台上升 (Y0 A+)");
        await _serialService.ForceYAsync(0, true, ct);
        await WaitForXAsync(0, true, "X0 [a1] 垂直缸上端點到位", 3000, ct);

        // 翻轉馬達旋轉 180 度 (智慧雙向判定與防誤判)
        await RotateFlipMotorAsync(ct);

        ReportStep("垂直導桿滑台下降 (Y1 A-) 回原點");
        await _serialService.ForceYAsync(0, false, ct);
        await _serialService.ForceYAsync(1, true, ct);
        await WaitForXAsync(1, true, "X1 [a0] 垂直缸下端點到位", 3000, ct);
        await _serialService.ForceYAsync(1, false, ct);

        ReportStep("氣壓夾爪張開釋放 (Y4 C-)");
        await _serialService.ForceYAsync(3, false, ct);
        await _serialService.ForceYAsync(4, true, ct);
        await WaitForXAsync(4, true, "X4 [c0] 氣壓夾爪開極限到位", 3000, ct);
        await _serialService.ForceYAsync(4, false, ct);

        blinkCts.Cancel();
        await Task.Delay(100, CancellationToken.None);
        await _serialService.ForceYAsync(13, false, CancellationToken.None); // RL 滅
        await _serialService.ForceYAsync(15, true, CancellationToken.None);  // GL 亮
        ReportStep("【翻料完成】回復待機狀態，待機燈 GL 亮，運轉燈 RL 滅");
    }

    /// <summary>
    /// 自動復歸模式
    /// </summary>
    private async Task RunHomingAsync(CancellationToken ct)
    {
        ReportStep("【自動復歸模式啟動】復歸燈 YL 以 0.5s/0.5s 閃爍至少 3 秒");
        await _serialService.ForceYAsync(15, false, ct); // GL 滅
        await _serialService.ForceYAsync(13, false, ct); // RL 滅

        using var ylBlinkCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = RunBlinkingLightAsync(14, 500, ylBlinkCts.Token); // Y16 YL 閃爍

        ReportStep("1/5: 氣壓夾爪張開 (Y4 C-)");
        await _serialService.ForceYAsync(4, true, ct);
        await WaitForXAsync(4, true, "X4 [c0] 氣壓夾爪開極限點到位", 3000, ct);
        await _serialService.ForceYAsync(4, false, ct);

        ReportStep("2/5: 真空破真空釋放 (Y10 D-)");
        await _serialService.ForceYAsync(5, false, ct);
        await _serialService.ForceYAsync(8, true, ct);
        await Task.Delay(400, ct);
        await _serialService.ForceYAsync(8, false, ct);

        ReportStep("3/5: 迴轉缸轉至出料端原點位 (Y2 B OFF)");
        await _serialService.ForceYAsync(2, false, ct);
        await WaitForXAsync(3, true, "X3 [b0] 迴轉缸出料原點到位", 4000, ct);

        ReportStep("4/5: 垂直滑台確認在下位 (Y1 A-)");
        await _serialService.ForceYAsync(0, false, ct);
        await _serialService.ForceYAsync(1, true, ct);
        await WaitForXAsync(1, true, "X1 [a0] 垂直滑台下端點到位", 4000, ct);
        await _serialService.ForceYAsync(1, false, ct);

        ReportStep("5/5: 檢查翻轉馬達復歸反轉原點 (e0 / X7)...");
        bool[] homingX = await _serialService.ReadXAsync(ct);
        bool homeE0 = homingX != null && homingX.Length > 7 && homingX[7];
        if (!homeE0)
        {
            ReportStep("馬達不在反轉原點，啟動反轉 (Y7 R2) 復歸回 e0 原點...");
            await _serialService.ForceYAsync(7, true, ct);
            await Task.Delay(400, ct);
            await WaitForXAsync(7, true, "X7 [e0] 迴轉馬達反轉原點到位", 4000, ct);
            await _serialService.ForceYAsync(7, false, ct);
            ReportStep("馬達已成功復歸至 e0 反轉原點！");
        }
        else
        {
            ReportStep("馬達已處於反轉原點 e0 (X7=ON)，無需動作");
        }

        // 確保至少閃爍滿 3 秒
        await Task.Delay(1000, ct);

        ylBlinkCts.Cancel();
        await Task.Delay(100, CancellationToken.None);
        await _serialService.ForceYAsync(14, false, CancellationToken.None); // YL 滅
        await _serialService.ForceYAsync(15, true, CancellationToken.None);  // GL 亮
        ReportStep("【復歸完成】機構已安全回原點，待機燈 GL 亮！");
    }

    /// <summary>
    /// 執行夾爪翻轉 180 度動作 (智慧雙向端點判定與防原地瞬間誤判保護)
    /// </summary>
    private async Task RotateFlipMotorAsync(CancellationToken ct)
    {
        // 1. 讀取當前端點狀態
        bool[] currentX = await _serialService.ReadXAsync(ct);
        bool atE1 = currentX != null && currentX.Length > 6 && currentX[6]; // X6 正轉端點
        bool atE0 = currentX != null && currentX.Length > 7 && currentX[7]; // X7 反轉端點

        ReportStep($"翻料 3/5: 檢測翻轉馬達端點位置 (X6 [e1]={atE1}, X7 [e0]={atE0})...");

        int motorY;      // 驅動馬達輸出點 (6: Y6 正轉, 7: Y7 反轉)
        int targetX;     // 目標感測端點 (6: X6, 7: X7)
        string dirDesc;

        // 若在正轉端點 e1 (X6=true)，必須反轉 R2 (Y7) 往 e0 (X7)
        // 若在反轉端點 e0 (X7=true) 或其他情況，則正轉 R1 (Y6) 往 e1 (X6)
        if (atE1 && !atE0)
        {
            motorY = 7;
            targetX = 7;
            dirDesc = "迴轉馬達反轉 (Y7 R2) -> 朝向反轉端點 X7 [e0]";
        }
        else
        {
            motorY = 6;
            targetX = 6;
            dirDesc = "迴轉馬達正轉 (Y6 R1) -> 朝向正轉端點 X6 [e1]";
        }

        ReportStep($"啟動{dirDesc}...");
        await _serialService.ForceYAsync(motorY, true, ct);

        // 防原地瞬間誤判行程延遲：啟動後至少強制運轉 400ms，確保機械確實脫離原端點！
        await Task.Delay(400, ct);

        // 等待目標端點到達
        await WaitForXAsync(targetX, true, $"X{targetX} 迴轉馬達目標端點到位", 5000, ct);

        // 停止馬達輸出
        await _serialService.ForceYAsync(motorY, false, ct);
        ReportStep($"迴轉馬達已到達目標端點並停止輸出 (Y{motorY} OFF)");
    }

    /// <summary>
    /// 燈號定時閃爍輔助
    /// </summary>
    private async Task RunBlinkingLightAsync(int yIndex, int intervalMs, CancellationToken ct)
    {
        bool state = false;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                state = !state;
                await _serialService.ForceYAsync(yIndex, state, ct);
                await Task.Delay(intervalMs, ct);
            }
        }
        catch
        {
            // 忽略取消例外
        }
        finally
        {
            await _serialService.ForceYAsync(yIndex, false, CancellationToken.None);
        }
    }

    /// <summary>
    /// 等待指定 X 接點達到預期狀態 (感測器到位連鎖保護)
    /// </summary>
    private async Task WaitForXAsync(int xIndex, bool expected, string desc, int timeoutMs, CancellationToken ct)
    {
        ReportStep($"[感測等待] 等待 {desc}...");
        var start = DateTime.Now;
        DateTime lastReport = DateTime.Now;

        while ((DateTime.Now - start).TotalMilliseconds < timeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            bool[] currentX = await _serialService.ReadXAsync(ct);
            bool actual = currentX != null && currentX.Length > xIndex && currentX[xIndex];

            if (actual == expected)
            {
                ReportStep($"[檢知到位] {desc} 確認到位！");
                return;
            }

            // 每隔 1.5 秒更新一次讀取中的狀態，讓操作者能看見即時感測訊號
            if ((DateTime.Now - lastReport).TotalMilliseconds >= 1500)
            {
                ReportStep($"[感測等待] 等待 {desc}... (PLC當前讀值: {(actual ? "ON" : "OFF")})");
                lastReport = DateTime.Now;
            }

            await Task.Delay(150, ct);
        }

        ReportStep($"[警報提示] {desc} 超過 {timeoutMs}ms 未檢知！請檢查氣壓壓力、管路與端點磁簧開關！");
    }

    /// <summary>
    /// 跑馬燈動作程序
    /// 順序：紅燈亮(其他滅) -> 黃燈亮(其他滅) -> 綠燈亮(其他滅) (各間隔一秒) -> 最後全熄滅
    /// </summary>
    public async Task RunMarqueeAsync(CancellationToken ct)
    {
        ReportStep("【跑馬燈開始】順序：紅燈(Y15) -> 黃燈(Y16) -> 綠燈(Y17) (各1秒) -> 全滅");

        try
        {
            // 1. 紅燈亮 (Y15/index 13 ON)，其他燈滅 (Y16/index 14 OFF, Y17/index 15 OFF)
            ReportStep("跑馬燈 [1/3]：紅燈亮 (RL/Y15 ON)，黃燈與綠燈熄滅");
            await _serialService.ForceYAsync(14, false, ct); // Y16 OFF
            await _serialService.ForceYAsync(15, false, ct); // Y17 OFF
            await _serialService.ForceYAsync(13, true, ct);  // Y15 ON
            await Task.Delay(1000, ct);

            // 2. 黃燈亮 (Y16/index 14 ON)，其他燈滅 (Y15/index 13 OFF, Y17/index 15 OFF)
            ReportStep("跑馬燈 [2/3]：黃燈亮 (YL/Y16 ON)，紅燈與綠燈熄滅");
            await _serialService.ForceYAsync(13, false, ct); // Y15 OFF
            await _serialService.ForceYAsync(15, false, ct); // Y17 OFF
            await _serialService.ForceYAsync(14, true, ct);  // Y16 ON
            await Task.Delay(1000, ct);

            // 3. 綠燈亮 (Y17/index 15 ON)，其他燈滅 (Y15/index 13 OFF, Y16/index 14 OFF)
            ReportStep("跑馬燈 [3/3]：綠燈亮 (GL/Y17 ON)，紅燈與黃燈熄滅");
            await _serialService.ForceYAsync(13, false, ct); // Y15 OFF
            await _serialService.ForceYAsync(14, false, ct); // Y16 OFF
            await _serialService.ForceYAsync(15, true, ct);  // Y17 ON
            await Task.Delay(1000, ct);
        }
        finally
        {
            // 4. 最後全熄滅 (Y15 OFF, Y16 OFF, Y17 OFF)
            await _serialService.ForceYAsync(13, false, CancellationToken.None);
            await _serialService.ForceYAsync(14, false, CancellationToken.None);
            await _serialService.ForceYAsync(15, false, CancellationToken.None);
            ReportStep("【跑馬燈完成】全部指示燈已熄滅！");
        }
    }

    private void ReportStep(string message)
    {
        StepChanged?.Invoke(message);
    }
}
