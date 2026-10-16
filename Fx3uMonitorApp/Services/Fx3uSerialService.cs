using System.IO.Ports;
using System.Text;
using Fx3uMonitorApp.Protocols;

namespace Fx3uMonitorApp.Services;

/// <summary>
/// 串列埠通訊與 PLC 監控管理服務
/// </summary>
public class Fx3uSerialService : IDisposable
{
    private SerialPort? _serialPort;
    private readonly SemaphoreSlim _commLock = new(1, 1);
    private bool _isDisposed;

    // 模擬模式內部狀態
    private readonly bool[] _simulatedX = new bool[16];
    private readonly bool[] _simulatedY = new bool[16];

    private bool _isSimConnected = false;

    /// <summary>
    /// 是否為離線模擬模式
    /// </summary>
    public bool IsSimulationMode { get; set; } = false;

    /// <summary>
    /// 是否記錄高頻通訊底層封包 (TX/RX)，預設關閉以專注於機丙步驟狀態
    /// </summary>
    public bool EnableCommLog { get; set; } = false;

    /// <summary>
    /// 目前是否已連線
    /// </summary>
    public bool IsConnected => IsSimulationMode ? _isSimConnected : (_serialPort != null && _serialPort.IsOpen);

    /// <summary>
    /// 連線狀態變更事件 (isConnected)
    /// </summary>
    public event Action<bool>? ConnectionChanged;

    /// <summary>
    /// 通訊日誌回報事件 (tag, hexString, isSuccess)
    /// </summary>
    public event Action<string, string, bool>? LogOccurred;

    /// <summary>
    /// X 接點狀態更新事件
    /// </summary>
    public event Action<bool[]>? XStatusUpdated;

    /// <summary>
    /// Y 接點狀態更新事件
    /// </summary>
    public event Action<bool[]>? YStatusUpdated;

    /// <summary>
    /// 發佈日誌訊息
    /// </summary>
    public void Log(string tag, string message, bool isSuccess = true)
    {
        LogOccurred?.Invoke(tag, message, isSuccess);
    }

    public Fx3uSerialService()
    {
        ResetToMechanicalHome();
    }

    /// <summary>
    /// 重設為機丙二標準機械原點 (滑台在下位、迴轉缸在出料位、夾爪張開、馬達原點、EMS NC正常)
    /// </summary>
    public void ResetToMechanicalHome()
    {
        Array.Clear(_simulatedX, 0, _simulatedX.Length);
        _simulatedX[1] = true;  // X1 (a0 垂直缸下端點)
        _simulatedX[3] = true;  // X3 (b0 迴轉缸出料端點)
        _simulatedX[4] = true;  // X4 (c0 氣壓夾爪開極限點)
        _simulatedX[7] = true;  // X7 (e0 迴轉馬達反轉端點)
        _simulatedX[8] = true;  // X10 (s0 進料感測器有圓料，index 8)
        _simulatedX[9] = true;  // X11 (s1 姿勢感測器: 開口朝上為 ON，index 9)
        _simulatedX[10] = true; // X12 (s2 顏色感測器: 紅色為 ON，index 10)
        _simulatedX[15] = true; // X17 (EMS NC 接點常閉導通，index 15)
    }

    /// <summary>
    /// 設定模擬工件屬性 (姿勢與顏色)
    /// </summary>
    /// <param name="isOpeningDown">true: 開口朝下 (s1/X11 = OFF), false: 開口朝上 (s1/X11 = ON)</param>
    /// <param name="isRed">true: 紅色圓料 (s2/X12 = ON), false: 黑色圓料 (s2/X12 = OFF)</param>
    public void SetSimulatedWorkpiece(bool isOpeningDown, bool isRed)
    {
        _simulatedX[9] = !isOpeningDown; // X11 (s1 姿勢感測器: 開口朝上為 ON, 開口朝下為 OFF)
        _simulatedX[10] = isRed;         // X12 (s2 顏色感測器: 紅色為 ON, 黑色為 OFF)
    }

    /// <summary>
    /// 列舉電腦上所有可用的 COM 埠名稱
    /// </summary>
    public static string[] GetAvailablePorts()
    {
        return SerialPort.GetPortNames();
    }

    /// <summary>
    /// 開啟串列埠連線
    /// </summary>
    public void Connect(string portName)
    {
        if (IsSimulationMode)
        {
            _isSimConnected = true;
            LogOccurred?.Invoke("SYSTEM", $"已進入離線模擬模式 (無需實體 COM 埠)", true);
            ConnectionChanged?.Invoke(true);
            return;
        }

        if (string.IsNullOrWhiteSpace(portName))
            throw new ArgumentException("COM 埠名稱不得為空", nameof(portName));

        Disconnect();

        _serialPort = new SerialPort
        {
            PortName = portName,
            BaudRate = 9600,
            DataBits = 7,
            Parity = Parity.Even,
            StopBits = StopBits.One,
            ReadTimeout = 1000,
            WriteTimeout = 500
        };

        _serialPort.Open();
        _serialPort.DiscardInBuffer();
        _serialPort.DiscardOutBuffer();

        LogOccurred?.Invoke("SYSTEM", $"成功連接串列埠 {portName} (9600, 7, Even, 1)", true);
        ConnectionChanged?.Invoke(true);
    }

    /// <summary>
    /// 關閉串列埠連線
    /// </summary>
    public void Disconnect()
    {
        _isSimConnected = false;
        if (_serialPort != null)
        {
            try
            {
                if (_serialPort.IsOpen)
                {
                    _serialPort.Close();
                }
            }
            catch
            {
                // 忽略關閉時可能的例外
            }
            finally
            {
                _serialPort.Dispose();
                _serialPort = null;
            }
        }

        LogOccurred?.Invoke("SYSTEM", "通訊已中斷連線", false);
        ConnectionChanged?.Invoke(false);
    }

    /// <summary>
    /// 讀取 Y0~Y17 狀態
    /// </summary>
    public async Task<bool[]> ReadYAsync(CancellationToken ct = default)
    {
        byte[] response = await SendAndReceiveAsync(Fx3uProtocol.ReadYCommand, 8, ct);
        bool[] status = Fx3uProtocol.ParseStatusResponse(response, verifyChecksum: !IsSimulationMode);
        YStatusUpdated?.Invoke(status);
        return status;
    }

    /// <summary>
    /// 讀取 X0~X17 狀態
    /// </summary>
    public async Task<bool[]> ReadXAsync(CancellationToken ct = default)
    {
        byte[] response = await SendAndReceiveAsync(Fx3uProtocol.ReadXCommand, 8, ct);
        bool[] status = Fx3uProtocol.ParseStatusResponse(response, verifyChecksum: !IsSimulationMode);
        XStatusUpdated?.Invoke(status);
        return status;
    }

    /// <summary>
    /// 強制設定 Y 接點 ON / OFF (含失敗重試機制)
    /// </summary>
    /// <param name="index">0~15 對應 Y0~Y7 與 Y10~Y17</param>
    /// <param name="state">true 為 ON, false 為 OFF</param>
    public async Task<bool> ForceYAsync(int index, bool state, CancellationToken ct = default)
    {
        byte[] cmd = Fx3uProtocol.BuildForceYCommand(index, state);
        var def = Fx3uProtocol.YPointDefinitions[index];

        bool success = false;
        int maxRetries = 2;

        for (int retry = 0; retry <= maxRetries; retry++)
        {
            try
            {
                byte[] response = await SendAndReceiveAsync(cmd, 1, ct);
                success = response.Length > 0 && response[0] == Fx3uProtocol.ACK;
                if (success)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                if (retry == maxRetries)
                {
                    LogOccurred?.Invoke("ERROR", $"控制 {def.DisplayText} 發生異常: {ex.Message}", false);
                    throw;
                }
            }
            await Task.Delay(60, ct);
        }

        if (!success)
        {
            LogOccurred?.Invoke("ERROR", $"控制 {def.DisplayText} {(state ? "ON" : "OFF")} 失敗，PLC 未回應 ACK！", false);
        }
        else
        {
            // 更新模擬狀態
            if (IsSimulationMode)
            {
                _simulatedY[index] = state;
                SimulateMechanicalResponse(index, state);
            }
        }
        return success;
    }

    /// <summary>
    /// 模擬真實氣壓缸與馬達動作行程，在延遲後反饋對應極限端點感測器 (X)
    /// </summary>
    private void SimulateMechanicalResponse(int yIndex, bool state)
    {
        switch (yIndex)
        {
            case 2: // Y2 B 迴轉缸
                if (state) // 轉向進料端
                {
                    _simulatedX[3] = false; // X3 (b0 出料端) 斷開
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1200); // 旋臂旋轉物理行程時間
                        _simulatedX[2] = true;  // X2 (b1 進料端) 閉合到位
                    });
                }
                else // 轉回出料端
                {
                    _simulatedX[2] = false; // X2 (b1 進料端) 斷開
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1200);
                        _simulatedX[3] = true;  // X3 (b0 出料端) 閉合到位
                    });
                }
                break;

            case 5: // Y5 D+ 真空吸盤吸
                if (state)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(600); // 抽真空壓力建立時間
                        _simulatedX[5] = true; // X5 (ps1 壓力開關) 閉合
                    });
                }
                else
                {
                    _simulatedX[5] = false;
                }
                break;

            case 8: // Y10 D- 破真空放
                if (state)
                {
                    _simulatedX[5] = false; // 負壓消失
                }
                break;

            case 0: // Y0 A+ 垂直缸上升
                if (state)
                {
                    _simulatedX[1] = false; // X1 (a0 下端點) 斷開
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(800);
                        _simulatedX[0] = true;  // X0 (a1 上端點) 到位
                    });
                }
                break;

            case 1: // Y1 A- 垂直缸下降
                if (state)
                {
                    _simulatedX[0] = false; // X0 (a1 上端點) 斷開
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(800);
                        _simulatedX[1] = true;  // X1 (a0 下端點) 到位
                    });
                }
                break;

            case 3: // Y3 C+ 氣壓夾爪夾料
                if (state)
                {
                    _simulatedX[4] = false; // X4 (c0 開極限點) 斷開
                }
                break;

            case 4: // Y4 C- 氣壓夾爪放鬆
                if (state)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(400);
                        _simulatedX[4] = true;  // X4 (c0 開極限點) 到位
                    });
                }
                break;

            case 6: // Y6 R1 迴轉馬達正轉
                if (state)
                {
                    _simulatedX[7] = false; // X7 (e0 反轉端點) 斷開
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1000);
                        _simulatedX[6] = true;  // X6 (e1 正轉端點) 到位
                    });
                }
                break;

            case 7: // Y7 R2 迴轉馬達反轉
                if (state)
                {
                    _simulatedX[6] = false; // X6 (e1 正轉端點) 斷開
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1000);
                        _simulatedX[7] = true;  // X7 (e0 反轉端點) 到位
                    });
                }
                break;
        }
    }

    /// <summary>
    /// 切換模擬 X 點狀態 (供離線測試輸入變化)
    /// </summary>
    public void ToggleSimulatedX(int index)
    {
        if (index >= 0 && index < 16)
        {
            _simulatedX[index] = !_simulatedX[index];
        }
    }

    /// <summary>
    /// 核心非同步發送與接收邏輯 (具備 Thread-Safe Lock)
    /// </summary>
    private async Task<byte[]> SendAndReceiveAsync(byte[] sendPacket, int expectedLength, CancellationToken ct)
    {
        await _commLock.WaitAsync(ct);
        try
        {
            if (EnableCommLog)
            {
                string txHex = Fx3uProtocol.ToHexString(sendPacket);
                LogOccurred?.Invoke("TX", txHex, true);
            }

            if (IsSimulationMode)
            {
                // 模擬模式直接回傳假資料
                await Task.Delay(20, ct); // 模擬串列通訊延遲
                byte[] simResponse = GenerateSimulatedResponse(sendPacket, expectedLength);
                if (EnableCommLog)
                {
                    string rxHex = Fx3uProtocol.ToHexString(simResponse);
                    LogOccurred?.Invoke("RX (SIM)", rxHex, true);
                }
                return simResponse;
            }

            if (_serialPort == null || !_serialPort.IsOpen)
            {
                throw new InvalidOperationException("串列埠尚未開啟");
            }

            // 清除緩衝區殘留資料
            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();

            // 發送封包
            _serialPort.Write(sendPacket, 0, sendPacket.Length);

            // 接收回傳資料
            byte[] buffer = new byte[expectedLength];
            int totalRead = 0;
            DateTime startTime = DateTime.Now;

            while (totalRead < expectedLength)
            {
                ct.ThrowIfCancellationRequested();

                if ((DateTime.Now - startTime).TotalMilliseconds > _serialPort.ReadTimeout)
                {
                    throw new TimeoutException($"接收資料逾時，已接收 {totalRead}/{expectedLength} 位元組");
                }

                if (_serialPort.BytesToRead > 0)
                {
                    int bytesToRead = Math.Min(_serialPort.BytesToRead, expectedLength - totalRead);
                    int read = _serialPort.Read(buffer, totalRead, bytesToRead);
                    totalRead += read;
                }
                else
                {
                    await Task.Delay(10, ct);
                }
            }

            if (EnableCommLog)
            {
                string realRxHex = Fx3uProtocol.ToHexString(buffer);
                LogOccurred?.Invoke("RX", realRxHex, true);
            }
            return buffer;
        }
        catch (Exception ex)
        {
            LogOccurred?.Invoke("ERROR", ex.Message, false);
            throw;
        }
        finally
        {
            _commLock.Release();
        }
    }

    /// <summary>
    /// 產生模擬的回傳封包
    /// </summary>
    private byte[] GenerateSimulatedResponse(byte[] sendPacket, int expectedLength)
    {
        if (expectedLength == 1)
        {
            // 強制指令回傳 ACK
            return [Fx3uProtocol.ACK];
        }

        // 讀取指令回傳 8 位元組
        bool[] targetPoints = (sendPacket.SequenceEqual(Fx3uProtocol.ReadXCommand)) ? _simulatedX : _simulatedY;

        int n0 = 0, n1 = 0, n2 = 0, n3 = 0;
        for (int i = 0; i < 4; i++) if (targetPoints[i]) n0 |= (1 << i);
        for (int i = 0; i < 4; i++) if (targetPoints[4 + i]) n1 |= (1 << i);
        for (int i = 0; i < 4; i++) if (targetPoints[8 + i]) n2 |= (1 << i);
        for (int i = 0; i < 4; i++) if (targetPoints[12 + i]) n3 |= (1 << i);

        char c1 = n1.ToString("X1")[0];
        char c0 = n0.ToString("X1")[0];
        char c3 = n3.ToString("X1")[0];
        char c2 = n2.ToString("X1")[0];

        byte[] body = [(byte)c1, (byte)c0, (byte)c3, (byte)c2];
        byte[] checksum = Fx3uProtocol.CalculateChecksum(body);

        return [
            Fx3uProtocol.STX,
            body[0], body[1], body[2], body[3],
            Fx3uProtocol.ETX,
            checksum[0], checksum[1]
        ];
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Disconnect();
        _commLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
