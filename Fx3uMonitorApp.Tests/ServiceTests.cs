using Fx3uMonitorApp.Services;
using Xunit;

namespace Fx3uMonitorApp.Tests;

public class ServiceTests
{
    [Fact]
    public async Task SerialService_SimulationMode_ShouldWorkEndToEnd()
    {
        using var service = new Fx3uSerialService();
        service.IsSimulationMode = true;

        bool connected = false;
        service.ConnectionChanged += (s) => connected = s;

        service.Connect("COM_DUMMY");
        Assert.True(connected);
        Assert.True(service.IsConnected);

        // 1. 強制設定 Y0 ON
        bool forceY0Result = await service.ForceYAsync(0, true);
        Assert.True(forceY0Result);

        // 2. 強制設定 Y17 ON (index 15)
        bool forceY17Result = await service.ForceYAsync(15, true);
        Assert.True(forceY17Result);

        // 3. 讀取 Y
        bool[] yStatus = await service.ReadYAsync();
        Assert.True(yStatus[0], "Y0 應該為 ON");
        Assert.False(yStatus[1], "Y1 應該為 OFF");
        Assert.True(yStatus[15], "Y17 應該為 ON");

        // 4. 強制設定 Y0 OFF
        bool forceY0Off = await service.ForceYAsync(0, false);
        Assert.True(forceY0Off);

        yStatus = await service.ReadYAsync();
        Assert.False(yStatus[0], "Y0 應該變為 OFF");
        Assert.True(yStatus[15], "Y17 依然為 ON");

        // 5. 斷開連線
        service.Disconnect();
        Assert.False(connected);
        Assert.False(service.IsConnected);
    }

    [Fact]
    public async Task JiBingErRunner_ShouldExecuteSuccessfully()
    {
        using var service = new Fx3uSerialService();
        service.IsSimulationMode = true;
        service.Connect("SIM");

        var runner = new JiBingErRunner(service);
        bool finished = false;
        string finalMsg = "";
        runner.Finished += (success, msg) =>
        {
            finished = success;
            finalMsg = msg;
        };

        // 執行指定功能 (單純翻料一次，索引 6)
        await runner.StartAsync(6);

        Assert.True(finished);
        Assert.Contains("完成", finalMsg);
        Assert.False(runner.IsRunning);
    }

    [Fact]
    public async Task JiBingErRunner_AutoMode_DetectOpeningDown_ShouldTriggerFlip()
    {
        using var service = new Fx3uSerialService();
        service.IsSimulationMode = true;
        service.Connect("SIM");

        // 模擬進料座放置開口朝下之工件 (X11 = ON)
        service.SetSimulatedWorkpiece(isOpeningDown: true, isRed: true);

        var runner = new JiBingErRunner(service);
        var steps = new List<string>();
        runner.StepChanged += (s) => steps.Add(s);

        bool finished = false;
        runner.Finished += (success, msg) => finished = success;

        // 啟動 0: ★ 自動感測模式
        await runner.StartAsync(0);

        Assert.True(finished);
        Assert.Contains(steps, s => s.Contains("開口朝下 (需翻料)"));
        Assert.Contains(steps, s => s.Contains("偵測到開口朝下！啟動氣壓夾爪與旋轉機構翻料程序"));
        Assert.Contains(steps, s => s.Contains("工件圓孔已朝上"));
    }

    [Fact]
    public async Task JiBingErRunner_OpeningDownScenario_ShouldTriggerFlip()
    {
        using var service = new Fx3uSerialService();
        service.IsSimulationMode = true;
        service.Connect("SIM");

        var runner = new JiBingErRunner(service);
        var steps = new List<string>();
        runner.StepChanged += (s) => steps.Add(s);

        bool finished = false;
        runner.Finished += (success, msg) => finished = success;

        // 啟動 2: 紅色圓料 (開口朝下-需翻料)
        await runner.StartAsync(2);

        Assert.True(finished);
        Assert.Contains(steps, s => s.Contains("偵測到開口朝下！啟動氣壓夾爪與旋轉機構翻料程序"));
        Assert.Contains(steps, s => s.Contains("【翻料完成】工件圓孔已朝上"));

        // 嚴格驗證翻料五步驟時序：先夾料 -> 上升 -> 翻轉 -> 下降 -> 放鬆
        int idxClamp = steps.FindIndex(s => s.Contains("氣壓夾爪夾料 (Y3 C+)"));
        int idxUp = steps.FindIndex(s => s.Contains("垂直缸上升 (Y0 A+)"));
        int idxRotate = steps.FindIndex(s => s.Contains("迴轉馬達"));
        int idxDown = steps.FindIndex(s => s.Contains("垂直缸下降回位 (Y1 A-)"));
        int idxRelease = steps.FindIndex(s => s.Contains("氣壓夾爪放鬆 (Y4 C-)"));

        Assert.True(idxClamp >= 0, "必須執行夾爪夾料");
        Assert.True(idxUp > idxClamp, "垂直缸上升必須在夾爪夾料之後");
        Assert.True(idxRotate > idxUp, "馬達翻轉必須在垂直缸上升到位之後");
        Assert.True(idxDown > idxRotate, "垂直缸下降必須在馬達翻轉之後");
        Assert.True(idxRelease > idxDown, "夾爪放鬆必須在垂直缸下降回位之後");
    }

    [Fact]
    public async Task JiBingErRunner_AutoMode_OpeningUp_ShouldNotMoveVerticalCylinder()
    {
        using var service = new Fx3uSerialService();
        service.IsSimulationMode = true;
        service.Connect("SIM");

        // 模擬進料座放置開口朝上之工件 (X11 = ON)
        service.SetSimulatedWorkpiece(isOpeningDown: false, isRed: true);

        var runner = new JiBingErRunner(service);
        var steps = new List<string>();
        runner.StepChanged += (s) => steps.Add(s);

        bool finished = false;
        runner.Finished += (success, msg) => finished = success;

        // 啟動 0: ★ 自動感測模式
        await runner.StartAsync(0);

        Assert.True(finished);
        // 必須判定為開口朝上，無需翻料
        Assert.Contains(steps, s => s.Contains("開口朝上 (無需翻料，垂直缸保持下位)"));
        // 絕對不能觸發垂直缸上升 Y0
        Assert.DoesNotContain(steps, s => s.Contains("垂直缸上升 (Y0 A+)"));
        // 必須直接由迴轉缸轉至進料端吸取
        Assert.Contains(steps, s => s.Contains("迴轉缸轉至進料端 (Y2 B)"));
        Assert.Contains(steps, s => s.Contains("真空吸盤啟動吸取 (Y5 D+)"));
    }

    [Fact]
    public async Task JiBingErRunner_Marquee_ShouldExecuteSuccessfullyAndTurnOffAll()
    {
        using var service = new Fx3uSerialService();
        service.IsSimulationMode = true;
        service.Connect("SIM");

        var runner = new JiBingErRunner(service);
        var steps = new List<string>();
        runner.StepChanged += (s) => steps.Add(s);

        bool finished = false;
        runner.Finished += (success, msg) => finished = success;

        // 啟動 8: 三色跑馬燈
        await runner.StartAsync(8);

        Assert.True(finished);
        Assert.Contains(steps, s => s.Contains("跑馬燈 [1/3]：紅燈亮 (RL/Y15 ON)"));
        Assert.Contains(steps, s => s.Contains("跑馬燈 [2/3]：黃燈亮 (YL/Y16 ON)"));
        Assert.Contains(steps, s => s.Contains("跑馬燈 [3/3]：綠燈亮 (GL/Y17 ON)"));
        Assert.Contains(steps, s => s.Contains("【跑馬燈完成】全部指示燈已熄滅！"));

        // 驗證最終狀態全部熄滅
        bool[] yStatus = await service.ReadYAsync();
        Assert.False(yStatus[13], "Y15 紅燈應熄滅");
        Assert.False(yStatus[14], "Y16 黃燈應熄滅");
        Assert.False(yStatus[15], "Y17 綠燈應熄滅");
    }
}
