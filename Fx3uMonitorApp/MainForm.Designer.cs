using System.ComponentModel;
using Fx3uMonitorApp.Controls;

namespace Fx3uMonitorApp;

partial class MainForm
{
    private IContainer components = null!;

    // 頂部控制面板元件
    private Panel panelTop = null!;
    private Label lblTitle = null!;
    private Label lblPort = null!;
    private ComboBox cmbPort = null!;
    private Button btnRefreshPorts = null!;
    private Label lblParams = null!;
    private Button btnConnect = null!;
    private CheckBox chkSimulation = null!;
    private CheckBox chkAutoPolling = null!;
    private Label lblInterval = null!;
    private NumericUpDown numInterval = null!;
    private Label lblStatusIndicator = null!;

    // 機丙二動作控制元件
    private Button btnStartJiBingEr = null!;
    private Button btnMarquee = null!;
    private ComboBox cmbScenario = null!;
    private Label lblJiBingErStep = null!;

    // 主內容分隔容器
    private TableLayoutPanel tableMain = null!;
    private GroupBox grpXPoints = null!;
    private GroupBox grpYPoints = null!;
    private GroupBox grpLog = null!;

    // X 點與 Y 點元件陣列
    private readonly LedBulb[] _ledX = new LedBulb[16];
    private readonly Label[] _lblXStatus = new Label[16];
    private readonly LedBulb[] _ledY = new LedBulb[16];
    private readonly Button[] _btnYOn = new Button[16];
    private readonly Button[] _btnYOff = new Button[16];

    // 日誌面板元件
    private RichTextBox txtLog = null!;
    private Button btnClearLog = null!;
    private CheckBox chkAutoScroll = null!;
    private CheckBox chkShowRawComm = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new Container();
        
        // 視窗基本設定
        Text = "三菱 FX3U PLC 串列通訊監控系統 (機電整合機構 I/O 監控)";
        ClientSize = new Size(1380, 860);
        MinimumSize = new Size(1260, 800);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = Color.FromArgb(30, 34, 42);
        ForeColor = Color.White;

        // 1. 頂部工具列
        panelTop = new Panel
        {
            Dock = DockStyle.Top,
            Height = 100,
            BackColor = Color.FromArgb(38, 43, 54),
            Padding = new Padding(15, 10, 15, 10)
        };

        lblTitle = new Label
        {
            Text = "MITSUBISHI MELSEC FX3U 機電整合串列監控與控制系統 (11501)",
            Font = new Font("Microsoft JhengHei UI", 13.5F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(77, 208, 225),
            Location = new Point(15, 12),
            AutoSize = true
        };

        lblPort = new Label
        {
            Text = "COM 埠:",
            Location = new Point(16, 56),
            AutoSize = true,
            ForeColor = Color.Gainsboro
        };

        cmbPort = new ComboBox
        {
            Location = new Point(80, 53),
            Width = 110,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(48, 54, 66),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };

        btnRefreshPorts = new Button
        {
            Text = "重整",
            Location = new Point(198, 52),
            Width = 60,
            Height = 30,
            BackColor = Color.FromArgb(60, 68, 82),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnRefreshPorts.FlatAppearance.BorderSize = 0;

        lblParams = new Label
        {
            Text = "9600, 7, E, 1",
            Location = new Point(268, 57),
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Italic),
            ForeColor = Color.DarkGray
        };

        btnConnect = new Button
        {
            Text = "連線 (Connect)",
            Location = new Point(360, 50),
            Width = 135,
            Height = 34,
            BackColor = Color.FromArgb(0, 150, 136),
            ForeColor = Color.White,
            Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat
        };
        btnConnect.FlatAppearance.BorderSize = 0;

        chkSimulation = new CheckBox
        {
            Text = "離線模擬模式",
            Location = new Point(510, 56),
            AutoSize = true,
            ForeColor = Color.FromArgb(255, 183, 77)
        };

        chkAutoPolling = new CheckBox
        {
            Text = "自動輪詢監控",
            Location = new Point(635, 56),
            AutoSize = true,
            Checked = true,
            ForeColor = Color.Gainsboro
        };

        lblInterval = new Label
        {
            Text = "週期 (ms):",
            Location = new Point(755, 56),
            AutoSize = true,
            ForeColor = Color.Gainsboro
        };

        numInterval = new NumericUpDown
        {
            Location = new Point(830, 53),
            Width = 70,
            Minimum = 50,
            Maximum = 5000,
            Value = 200,
            Increment = 50,
            BackColor = Color.FromArgb(48, 54, 66),
            ForeColor = Color.White
        };

        lblStatusIndicator = new Label
        {
            Text = "● 未連線",
            Location = new Point(915, 55),
            AutoSize = true,
            Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(239, 83, 80)
        };

        // 機丙二動作控制元件初始化
        cmbScenario = new ComboBox
        {
            Location = new Point(780, 11),
            Width = 280,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(48, 54, 66),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Microsoft JhengHei UI", 9F, FontStyle.Regular)
        };
        cmbScenario.Items.AddRange([
            "★ 自動感測模式 (依X10進料/X11姿勢/X12顏色自動判定)",
            "1. 單一循環 - 紅色圓料 (開口朝上)",
            "2. 單一循環 - 紅色圓料 (開口朝下-需翻料)",
            "3. 單一循環 - 黑色圓料 (開口朝上-閃爍RL)",
            "4. 單一循環 - 黑色圓料 (開口朝下-翻料+閃爍RL)",
            "5. 連續循環 (依感測器自動排料2件)",
            "6. 指定功能 (單純翻料一次)",
            "7. 自動復歸模式 (YL閃爍3秒回原點)",
            "8. 三色跑馬燈 (紅->黃->綠 1秒順序，最後全滅)"
        ]);
        cmbScenario.SelectedIndex = 0;

        btnStartJiBingEr = new Button
        {
            Text = "▶ 機丙二開始",
            Location = new Point(1070, 8),
            Width = 140,
            Height = 36,
            BackColor = Color.FromArgb(124, 77, 255), // 亮紫色
            ForeColor = Color.White,
            Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnStartJiBingEr.FlatAppearance.BorderSize = 0;

        btnMarquee = new Button
        {
            Text = "▶ 跑馬燈開始",
            Location = new Point(1220, 8),
            Width = 140,
            Height = 36,
            BackColor = Color.FromArgb(255, 112, 67), // 亮橙色
            ForeColor = Color.White,
            Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        btnMarquee.FlatAppearance.BorderSize = 0;

        lblJiBingErStep = new Label
        {
            Text = "機丙二進度: [待機中]",
            Location = new Point(1020, 55),
            AutoSize = true,
            Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(128, 222, 234)
        };

        panelTop.Controls.AddRange([
            lblTitle, lblPort, cmbPort, btnRefreshPorts, lblParams,
            btnConnect, chkSimulation, chkAutoPolling, lblInterval,
            numInterval, lblStatusIndicator,
            cmbScenario, btnStartJiBingEr, btnMarquee, lblJiBingErStep
        ]);

        // 2. 主排版表格
        tableMain = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(12),
            BackColor = Color.FromArgb(30, 34, 42)
        };
        tableMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47F)); // X 輸入區
        tableMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53F)); // Y 輸出區
        tableMain.RowStyles.Add(new RowStyle(SizeType.Percent, 65F));    // 接點區塊
        tableMain.RowStyles.Add(new RowStyle(SizeType.Percent, 35F));    // 日誌區塊

        // 3. X 接點群組
        grpXPoints = CreateXPointsGroupBox();
        tableMain.Controls.Add(grpXPoints, 0, 0);

        // 4. Y 接點群組
        grpYPoints = CreateYPointsGroupBox();
        tableMain.Controls.Add(grpYPoints, 1, 0);

        // 5. 通訊日誌群組 (橫跨兩欄)
        grpLog = CreateLogGroupBox();
        tableMain.Controls.Add(grpLog, 0, 1);
        tableMain.SetColumnSpan(grpLog, 2);

        Controls.Add(tableMain);
        Controls.Add(panelTop);
    }

    private GroupBox CreateXPointsGroupBox()
    {
        var grp = new GroupBox
        {
            Text = " 輸入接點狀態監控 (X0 ~ X17) [可點擊燈號模擬感測輸入] ",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(77, 208, 225),
            Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold),
            Padding = new Padding(8)
        };

        var flow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
            AutoScroll = true
        };
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        var xDefs = Protocols.Fx3uProtocol.XPointDefinitions;

        for (int i = 0; i < 16; i++)
        {
            int col = i < 8 ? 0 : 1;
            int row = i % 8;
            var def = xDefs[i];

            var panelItem = new Panel
            {
                Dock = DockStyle.Fill,
                Height = 36,
                Margin = new Padding(3, 2, 3, 2),
                BackColor = Color.FromArgb(38, 43, 54)
            };

            var led = new LedBulb
            {
                Location = new Point(6, 6),
                Size = new Size(24, 24),
                OnColor = Color.FromArgb(0, 230, 118),
                OffColor = Color.FromArgb(55, 71, 79),
                Cursor = Cursors.Hand
            };
            int pointIndex = i;
            led.Click += (s, e) => OnLedXClicked(pointIndex);

            var lblName = new Label
            {
                Text = $"{def.Address} [{def.Code}]",
                Location = new Point(34, 8),
                Width = 90,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 238, 88)
            };

            var lblDesc = new Label
            {
                Text = def.Description,
                Location = new Point(125, 9),
                Width = 120,
                Font = new Font("Microsoft JhengHei UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.WhiteSmoke
            };

            var lblStatus = new Label
            {
                Text = "OFF",
                Location = new Point(245, 9),
                Width = 45,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.DarkGray
            };

            panelItem.Controls.AddRange([led, lblName, lblDesc, lblStatus]);
            flow.Controls.Add(panelItem, col, row);

            _ledX[i] = led;
            _lblXStatus[i] = lblStatus;
        }

        grp.Controls.Add(flow);
        return grp;
    }

    private GroupBox CreateYPointsGroupBox()
    {
        var grp = new GroupBox
        {
            Text = " 輸出接點強制控制與監控 (Y0 ~ Y17) ",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(255, 112, 67),
            Font = new Font("Microsoft JhengHei UI", 10F, FontStyle.Bold),
            Padding = new Padding(8)
        };

        var flow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
            AutoScroll = true
        };
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        var yDefs = Protocols.Fx3uProtocol.YPointDefinitions;

        for (int i = 0; i < 16; i++)
        {
            int col = i < 8 ? 0 : 1;
            int row = i % 8;
            var def = yDefs[i];

            var panelItem = new Panel
            {
                Dock = DockStyle.Fill,
                Height = 36,
                Margin = new Padding(3, 2, 3, 2),
                BackColor = Color.FromArgb(38, 43, 54)
            };

            // 針對指示燈 (Y15 RL 紅燈, Y16 YL 黃燈, Y17 GL 綠燈) 特別設定對應燈色
            Color onColor = i switch
            {
                13 => Color.FromArgb(255, 23, 68),  // Y15 RL (紅)
                14 => Color.FromArgb(255, 214, 0), // Y16 YL (黃)
                15 => Color.FromArgb(0, 230, 118),  // Y17 GL (綠)
                _  => Color.FromArgb(255, 87, 34)   // 電磁閥/馬達作動 (亮橘紅)
            };

            var led = new LedBulb
            {
                Location = new Point(6, 6),
                Size = new Size(24, 24),
                OnColor = onColor,
                OffColor = Color.FromArgb(55, 71, 79)
            };

            var lblName = new Label
            {
                Text = def.Code == "-" ? def.Address : $"{def.Address} [{def.Code}]",
                Location = new Point(34, 8),
                Width = 85,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(128, 222, 234)
            };

            var lblDesc = new Label
            {
                Text = def.Description,
                Location = new Point(120, 9),
                Width = 110,
                Font = new Font("Microsoft JhengHei UI", 8.5F, FontStyle.Regular),
                ForeColor = Color.WhiteSmoke
            };

            var btnOn = new Button
            {
                Text = "ON",
                Location = new Point(232, 5),
                Width = 46,
                Height = 26,
                BackColor = Color.FromArgb(46, 125, 50),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnOn.FlatAppearance.BorderSize = 0;
            int pointIndex = i;
            btnOn.Click += (s, e) => OnForceYClicked(pointIndex, true);

            var btnOff = new Button
            {
                Text = "OFF",
                Location = new Point(280, 5),
                Width = 46,
                Height = 26,
                BackColor = Color.FromArgb(198, 40, 40),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnOff.FlatAppearance.BorderSize = 0;
            btnOff.Click += (s, e) => OnForceYClicked(pointIndex, false);

            panelItem.Controls.AddRange([led, lblName, lblDesc, btnOn, btnOff]);
            flow.Controls.Add(panelItem, col, row);

            _ledY[i] = led;
            _btnYOn[i] = btnOn;
            _btnYOff[i] = btnOff;
        }

        grp.Controls.Add(flow);
        return grp;
    }

    private GroupBox CreateLogGroupBox()
    {
        var grp = new GroupBox
        {
            Text = " 機丙二動作步驟與流程狀態日誌 (即時步驟追蹤) ",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(200, 205, 215),
            Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold),
            Padding = new Padding(8)
        };

        var panelLogTop = new Panel
        {
            Dock = DockStyle.Top,
            Height = 32
        };

        btnClearLog = new Button
        {
            Text = "清除日誌",
            Location = new Point(5, 2),
            Width = 85,
            Height = 26,
            BackColor = Color.FromArgb(60, 68, 82),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnClearLog.FlatAppearance.BorderSize = 0;

        chkAutoScroll = new CheckBox
        {
            Text = "自動滾動至最新",
            Location = new Point(100, 5),
            AutoSize = true,
            Checked = true,
            ForeColor = Color.DarkGray
        };

        chkShowRawComm = new CheckBox
        {
            Text = "記錄通訊原始封包 (TX/RX)",
            Location = new Point(230, 5),
            AutoSize = true,
            Checked = false, // 預設關閉，只記錄機丙狀態
            ForeColor = Color.FromArgb(176, 190, 197)
        };

        panelLogTop.Controls.AddRange([btnClearLog, chkAutoScroll, chkShowRawComm]);

        txtLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(20, 23, 28),
            ForeColor = Color.LightGreen,
            Font = new Font("Consolas", 9F, FontStyle.Regular),
            ReadOnly = true,
            BorderStyle = BorderStyle.None
        };

        grp.Controls.Add(txtLog);
        grp.Controls.Add(panelLogTop);
        return grp;
    }
}
