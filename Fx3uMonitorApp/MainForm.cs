using System.Text;
using Fx3uMonitorApp.Protocols;
using Fx3uMonitorApp.Services;

namespace Fx3uMonitorApp;

public partial class MainForm : Form
{
    private readonly Fx3uSerialService _serialService;
    private readonly JiBingErRunner _jiBingErRunner;
    private readonly System.Windows.Forms.Timer _pollingTimer;
    private bool _isPollingInProgress = false;

    public MainForm()
    {
        InitializeComponent();

        _serialService = new Fx3uSerialService();
        _serialService.ConnectionChanged += OnConnectionChanged;
        _serialService.LogOccurred += OnLogOccurred;
        _serialService.XStatusUpdated += OnXStatusUpdated;
        _serialService.YStatusUpdated += OnYStatusUpdated;

        _jiBingErRunner = new JiBingErRunner(_serialService);
        _jiBingErRunner.StepChanged += OnJiBingErStepChanged;
        _jiBingErRunner.Finished += OnJiBingErFinished;

        _pollingTimer = new System.Windows.Forms.Timer
        {
            Interval = (int)numInterval.Value
        };
        _pollingTimer.Tick += PollingTimer_Tick;

        // 事件綁定
        btnRefreshPorts.Click += (s, e) => RefreshPorts();
        btnConnect.Click += BtnConnect_Click;
        btnStartJiBingEr.Click += BtnStartJiBingEr_Click;
        btnMarquee.Click += BtnMarquee_Click;
        chkShowRawComm.CheckedChanged += (s, e) => _serialService.EnableCommLog = chkShowRawComm.Checked;
        chkSimulation.CheckedChanged += ChkSimulation_CheckedChanged;
        chkAutoPolling.CheckedChanged += ChkAutoPolling_CheckedChanged;
        numInterval.ValueChanged += (s, e) => _pollingTimer.Interval = (int)numInterval.Value;
        btnClearLog.Click += (s, e) => txtLog.Clear();

        RefreshPorts();
        UpdateUiState(false);
    }

    private void RefreshPorts()
    {
        string current = cmbPort.SelectedItem?.ToString() ?? "";
        cmbPort.Items.Clear();

        string[] ports = Fx3uSerialService.GetAvailablePorts();
        if (ports.Length > 0)
        {
            cmbPort.Items.AddRange(ports);
            cmbPort.SelectedIndex = cmbPort.Items.Contains(current) ? cmbPort.Items.IndexOf(current) : 0;
        }
        else
        {
            cmbPort.Items.Add("無可用 COM 埠");
            cmbPort.SelectedIndex = 0;
        }
    }

    private void ChkSimulation_CheckedChanged(object? sender, EventArgs e)
    {
        _serialService.IsSimulationMode = chkSimulation.Checked;
        if (chkSimulation.Checked)
        {
            cmbPort.Enabled = false;
            btnRefreshPorts.Enabled = false;
        }
        else if (!_serialService.IsConnected)
        {
            cmbPort.Enabled = true;
            btnRefreshPorts.Enabled = true;
        }
    }

    private void ChkAutoPolling_CheckedChanged(object? sender, EventArgs e)
    {
        if (chkAutoPolling.Checked && _serialService.IsConnected)
        {
            _pollingTimer.Start();
        }
        else
        {
            _pollingTimer.Stop();
        }
    }

    private void BtnConnect_Click(object? sender, EventArgs e)
    {
        if (!_serialService.IsConnected)
        {
            try
            {
                string portName = cmbPort.SelectedItem?.ToString() ?? "";
                _serialService.Connect(portName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"連線失敗：{ex.Message}", "連線錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        else
        {
            _serialService.Disconnect();
        }
    }

    private void OnConnectionChanged(bool isConnected)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<bool>(OnConnectionChanged), isConnected);
            return;
        }

        UpdateUiState(isConnected);

        if (isConnected && chkAutoPolling.Checked)
        {
            _pollingTimer.Start();
        }
        else
        {
            _pollingTimer.Stop();
        }
    }

    private void UpdateUiState(bool isConnected)
    {
        if (isConnected)
        {
            btnConnect.Text = "中斷 (Disconnect)";
            btnConnect.BackColor = Color.FromArgb(211, 47, 47);
            lblStatusIndicator.Text = _serialService.IsSimulationMode ? "● 模擬連線中" : "● 已連線 (Online)";
            lblStatusIndicator.ForeColor = Color.FromArgb(0, 230, 118);

            cmbPort.Enabled = false;
            btnRefreshPorts.Enabled = false;
            chkSimulation.Enabled = false;
            btnStartJiBingEr.Enabled = true;
            btnMarquee.Enabled = true;
            cmbScenario.Enabled = !_jiBingErRunner.IsRunning;
        }
        else
        {
            btnConnect.Text = "連線 (Connect)";
            btnConnect.BackColor = Color.FromArgb(0, 150, 136);
            lblStatusIndicator.Text = "● 未連線";
            lblStatusIndicator.ForeColor = Color.FromArgb(239, 83, 80);

            cmbPort.Enabled = !chkSimulation.Checked;
            btnRefreshPorts.Enabled = !chkSimulation.Checked;
            chkSimulation.Enabled = true;
            btnStartJiBingEr.Enabled = false;
            btnMarquee.Enabled = false;
            cmbScenario.Enabled = true;

            if (_jiBingErRunner.IsRunning)
            {
                _jiBingErRunner.Stop();
            }

            // 接點復位為 OFF
            for (int i = 0; i < 16; i++)
            {
                _ledX[i].IsOn = false;
                _lblXStatus[i].Text = "OFF";
                _lblXStatus[i].ForeColor = Color.DarkGray;

                _ledY[i].IsOn = false;
            }
        }

        // 按鈕可用性
        for (int i = 0; i < 16; i++)
        {
            _btnYOn[i].Enabled = isConnected;
            _btnYOff[i].Enabled = isConnected;
        }
    }

    private async void BtnStartJiBingEr_Click(object? sender, EventArgs e)
    {
        if (!_serialService.IsConnected)
        {
            MessageBox.Show("請先點擊【連線】或勾選【離線模擬模式】後再啟動機丙二動作！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_jiBingErRunner.IsRunning)
        {
            btnStartJiBingEr.Text = "停止中...";
            btnStartJiBingEr.Enabled = false;
            _jiBingErRunner.Stop();
            return;
        }

        // 啟動機丙二 (暫停背景輪詢以確保控制指令獨佔與零干擾)
        _pollingTimer.Stop();
        btnStartJiBingEr.Text = "⏹ 機丙二停止";
        btnStartJiBingEr.BackColor = Color.FromArgb(213, 0, 0); // 亮紅色
        btnMarquee.Enabled = false;
        cmbScenario.Enabled = false;

        int selectedIndex = cmbScenario.SelectedIndex;
        string scenarioName = cmbScenario.SelectedItem?.ToString() ?? "";
        _serialService.Log("機丙二", $"【啟動流程】{scenarioName}", true);

        await _jiBingErRunner.StartAsync(selectedIndex);
    }

    private async void BtnMarquee_Click(object? sender, EventArgs e)
    {
        if (!_serialService.IsConnected)
        {
            MessageBox.Show("請先點擊【連線】或勾選【離線模擬模式】後再啟動跑馬燈！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_jiBingErRunner.IsRunning)
        {
            btnMarquee.Text = "停止中...";
            btnMarquee.Enabled = false;
            _jiBingErRunner.Stop();
            return;
        }

        // 啟動跑馬燈 (暫停背景輪詢)
        _pollingTimer.Stop();
        btnMarquee.Text = "⏹ 跑馬燈停止";
        btnMarquee.BackColor = Color.FromArgb(213, 0, 0); // 亮紅色
        btnStartJiBingEr.Enabled = false;
        cmbScenario.Enabled = false;

        _serialService.Log("跑馬燈", "【啟動流程】三色跑馬燈 (紅燈->黃燈->綠燈 各1秒，最後全滅)", true);

        // 8 為三色跑馬燈情境索引
        await _jiBingErRunner.StartAsync(8);
    }

    private void OnJiBingErStepChanged(string step)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<string>(OnJiBingErStepChanged), step);
            return;
        }

        lblJiBingErStep.Text = $"進度: {step}";
        _serialService.Log("機丙二", step, true);
    }

    private void OnJiBingErFinished(bool isSuccess, string message)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<bool, string>(OnJiBingErFinished), isSuccess, message);
            return;
        }

        btnStartJiBingEr.Text = "▶ 機丙二開始";
        btnStartJiBingEr.BackColor = Color.FromArgb(124, 77, 255);
        btnStartJiBingEr.Enabled = _serialService.IsConnected;

        btnMarquee.Text = "▶ 跑馬燈開始";
        btnMarquee.BackColor = Color.FromArgb(255, 112, 67);
        btnMarquee.Enabled = _serialService.IsConnected;

        cmbScenario.Enabled = true;

        lblJiBingErStep.Text = $"進度: [{(isSuccess ? "完成" : "已終止")}]";
        _serialService.Log("機丙二", $"【流程結束】{message}", isSuccess);

        // 恢復自動輪詢
        if (chkAutoPolling.Checked && _serialService.IsConnected)
        {
            _pollingTimer.Start();
        }
    }

    private async void PollingTimer_Tick(object? sender, EventArgs e)
    {
        if (!_serialService.IsConnected || _isPollingInProgress)
            return;

        _isPollingInProgress = true;
        try
        {
            // 讀取 Y 接點
            await _serialService.ReadYAsync();

            // 讀取 X 接點
            await _serialService.ReadXAsync();
        }
        catch (Exception ex)
        {
            // 輪詢錯誤已由日誌記錄，若為實體斷線可在此處理
            System.Diagnostics.Debug.WriteLine($"輪詢異常: {ex.Message}");
        }
        finally
        {
            _isPollingInProgress = false;
        }
    }

    private async void OnForceYClicked(int index, bool state)
    {
        if (!_serialService.IsConnected) return;

        try
        {
            _btnYOn[index].Enabled = false;
            _btnYOff[index].Enabled = false;

            bool success = await _serialService.ForceYAsync(index, state);
            if (!success)
            {
                var def = Fx3uProtocol.YPointDefinitions[index];
                MessageBox.Show($"強制 {def.DisplayText} {(state ? "ON" : "OFF")} 失敗！", "控制警示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"控制發生錯誤：{ex.Message}", "控制錯誤", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (_serialService.IsConnected)
            {
                _btnYOn[index].Enabled = true;
                _btnYOff[index].Enabled = true;
            }
        }
    }

    private void OnLedXClicked(int index)
    {
        if (_serialService.IsSimulationMode)
        {
            _serialService.ToggleSimulatedX(index);
        }
    }

    private void OnXStatusUpdated(bool[] status)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<bool[]>(OnXStatusUpdated), status);
            return;
        }

        for (int i = 0; i < 16 && i < status.Length; i++)
        {
            bool isOn = status[i];
            _ledX[i].IsOn = isOn;
            _lblXStatus[i].Text = isOn ? "ON" : "OFF";
            _lblXStatus[i].ForeColor = isOn ? Color.FromArgb(0, 230, 118) : Color.DarkGray;
        }
    }

    private void OnYStatusUpdated(bool[] status)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<bool[]>(OnYStatusUpdated), status);
            return;
        }

        for (int i = 0; i < 16 && i < status.Length; i++)
        {
            _ledY[i].IsOn = status[i];
        }
    }

    private void OnLogOccurred(string tag, string message, bool isSuccess)
    {
        if (InvokeRequired)
        {
            Invoke(new Action<string, string, bool>(OnLogOccurred), tag, message, isSuccess);
            return;
        }

        // 若未勾選顯示底層通訊封包，則過濾掉高頻輪詢 TX/RX，專注顯示機丙二狀態步驟
        if (!chkShowRawComm.Checked && (tag == "TX" || tag.StartsWith("RX")))
        {
            return;
        }

        Color tagColor = tag switch
        {
            "機丙二" => Color.FromArgb(224, 64, 251), // 亮粉紫色
            "TX" => Color.FromArgb(77, 208, 225),
            "RX" or "RX (SIM)" => Color.FromArgb(129, 199, 132),
            "SYSTEM" => Color.FromArgb(255, 183, 77),
            _ => Color.FromArgb(239, 83, 80)
        };

        string timestamp = DateTime.Now.ToString("HH:mm:ss");

        // 若日誌太長則截斷保持順暢
        if (txtLog.TextLength > 15000)
        {
            txtLog.Select(0, 5000);
            txtLog.SelectedText = "";
        }

        txtLog.SelectionStart = txtLog.TextLength;
        txtLog.SelectionLength = 0;

        txtLog.SelectionColor = Color.DarkGray;
        txtLog.AppendText($"[{timestamp}] ");

        txtLog.SelectionColor = tagColor;
        txtLog.SelectionFont = new Font(txtLog.Font, FontStyle.Bold);
        txtLog.AppendText($"[{tag}] ");
        txtLog.SelectionFont = new Font(txtLog.Font, FontStyle.Regular);

        // 機丙二步驟日誌以明亮顏色與清晰格式呈現
        if (tag == "機丙二")
        {
            txtLog.SelectionColor = Color.FromArgb(255, 241, 118); // 亮黃色，步驟一目了然
        }
        else
        {
            txtLog.SelectionColor = isSuccess ? Color.WhiteSmoke : Color.FromArgb(255, 120, 120);
        }
        txtLog.AppendText($"{message}{Environment.NewLine}");

        if (chkAutoScroll.Checked)
        {
            txtLog.ScrollToCaret();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _pollingTimer.Stop();
        _serialService.Dispose();
        base.OnFormClosing(e);
    }
}
