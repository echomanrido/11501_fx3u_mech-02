using System.Text;

namespace Fx3uMonitorApp.Protocols;

/// <summary>
/// 三菱 FX3U 編程口串列通訊協定引擎
/// </summary>
public static class Fx3uProtocol
{
    public const byte STX = 0x02; // 起始碼 (Start of Text)
    public const byte ETX = 0x03; // 結束碼 (End of Text)
    public const byte ACK = 0x06; // 正確回應 (Acknowledge)
    public const byte NAK = 0x15; // 錯誤回應 (Negative Acknowledge)

    /// <summary>
    /// 讀取 Y0~Y17 輸出接點指令
    /// 02 30 30 30 41 30 30 32 03 36 36
    /// </summary>
    public static readonly byte[] ReadYCommand = [
        0x02, 0x30, 0x30, 0x30, 0x41, 0x30, 0x30, 0x32, 0x03, 0x36, 0x36
    ];

    /// <summary>
    /// 讀取 X0~X17 輸入接點指令
    /// 02 30 30 30 38 30 30 32 03 35 44
    /// </summary>
    public static readonly byte[] ReadXCommand = [
        0x02, 0x30, 0x30, 0x30, 0x38, 0x30, 0x30, 0x32, 0x03, 0x35, 0x44
    ];

    /// <summary>
    /// 八進制接點編號清單 (0~7, 10~17)
    /// </summary>
    public static readonly string[] OctalPointNames = [
        "0", "1", "2", "3", "4", "5", "6", "7",
        "10", "11", "12", "13", "14", "15", "16", "17"
    ];

    /// <summary>
    /// X 輸入點配置定義 (0~7, 10~17)
    /// </summary>
    public static readonly IoPointInfo[] XPointDefinitions = [
        new("X0", "a1", "垂直缸上端點"),
        new("X1", "a0", "垂直缸下端點"),
        new("X2", "b1", "迴轉缸進料端點"),
        new("X3", "b0", "迴轉缸出料端點"),
        new("X4", "c0", "氣壓夾爪開極限點"),
        new("X5", "ps1", "真空吸盤壓力開關"),
        new("X6", "e1", "迴轉馬達正轉端點"),
        new("X7", "e0", "迴轉馬達反轉端點"),
        new("X10", "s0", "進料感測器"),
        new("X11", "s1", "姿勢感測器"),
        new("X12", "s2", "顏色感測器"),
        new("X13", "COS1", "*復歸模式/工作模式"),
        new("X14", "COS2_L", "*單一循環/指定功能"),
        new("X15", "COS2_R", "*單一循環/連續循環"),
        new("X16", "ST/RST", "啟動鈕/復歸鈕"),
        new("X17", "EMS", "緊急停止開關(NC)")
    ];

    /// <summary>
    /// Y 輸出點配置定義 (0~7, 10~17)
    /// </summary>
    public static readonly IoPointInfo[] YPointDefinitions = [
        new("Y0", "A＋", "垂直缸上升"),
        new("Y1", "A－", "垂直缸下降"),
        new("Y2", "B", "迴轉缸轉至進料端"),
        new("Y3", "C＋", "氣壓夾爪夾料"),
        new("Y4", "C－", "氣壓夾爪放鬆"),
        new("Y5", "D＋", "真空吸盤吸"),
        new("Y6", "R1", "迴轉馬達正轉(M＋)"),
        new("Y7", "R2", "迴轉馬達反轉(M－)"),
        new("Y10", "D－", "真空吸盤放"),
        new("Y11", "-", "(備用)"),
        new("Y12", "-", "(備用)"),
        new("Y13", "-", "(備用)"),
        new("Y14", "-", "(備用)"),
        new("Y15", "RL", "紅燈 (運轉燈)"),
        new("Y16", "YL", "黃燈 (復歸燈)"),
        new("Y17", "GL", "綠燈 (待機燈)")
    ];

    /// <summary>
    /// 計算 FX 協議 Checksum (從 STX 下一個 Byte 到 ETX 包含的所有位元組和 Modulo 256，以 2 位 ASCII Hex 表示)
    /// </summary>
    public static byte[] CalculateChecksum(byte[] bodyWithoutStxAndEtx)
    {
        int sum = 0;
        foreach (byte b in bodyWithoutStxAndEtx)
        {
            sum += b;
        }
        sum += ETX; // ETX 也納入 Checksum 計算
        byte check = (byte)(sum & 0xFF);
        string hex = check.ToString("X2");
        return Encoding.ASCII.GetBytes(hex);
    }

    /// <summary>
    /// 驗證封包 Checksum 是否正確
    /// </summary>
    public static bool VerifyChecksum(byte[] packet)
    {
        if (packet == null || packet.Length < 4)
            return false;

        if (packet[0] != STX || packet[^3] != ETX)
            return false;

        int sum = 0;
        // 從 packet[1] 加到 packet[packet.Length - 3] (即 ETX)
        for (int i = 1; i <= packet.Length - 3; i++)
        {
            sum += packet[i];
        }

        byte calculated = (byte)(sum & 0xFF);
        string calculatedStr = calculated.ToString("X2");
        string receivedStr = Encoding.ASCII.GetString(packet, packet.Length - 2, 2);

        return string.Equals(calculatedStr, receivedStr, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 取得 Y 接點強制 ON/OFF 的指令封包 (index: 0~15，對應 Y0~Y7 與 Y10~Y17)
    /// </summary>
    /// <param name="index">0~15 的索引</param>
    /// <param name="state">true: 強制 ON, false: 強制 OFF</param>
    /// <returns>11 位元組的 Hex 指令陣列</returns>
    public static byte[] BuildForceYCommand(int index, bool state)
    {
        if (index < 0 || index > 15)
            throw new ArgumentOutOfRangeException(nameof(index), "索引必須介於 0~15 之間 (對應 Y0~Y17)");

        // 強制 ON 指令碼: 0x37 ('7')，強制 OFF 指令碼: 0x38 ('8')
        byte cmd = state ? (byte)0x37 : (byte)0x38;

        // 位址格式為 4 Bytes ASCII: "0{X}05" 其中 X 為 index 的十六進制
        string addrHex = index.ToString("X1"); // 0..F
        string addrStr = $"0{addrHex}05";
        byte[] addrBytes = Encoding.ASCII.GetBytes(addrStr);

        // 封包本體: cmd + addrBytes
        byte[] body = new byte[1 + addrBytes.Length];
        body[0] = cmd;
        Array.Copy(addrBytes, 0, body, 1, addrBytes.Length);

        byte[] checksum = CalculateChecksum(body);

        byte[] packet = new byte[1 + body.Length + 1 + 2]; // STX + body + ETX + 2 Bytes Checksum
        packet[0] = STX;
        Array.Copy(body, 0, packet, 1, body.Length);
        packet[1 + body.Length] = ETX;
        packet[^2] = checksum[0];
        packet[^1] = checksum[1];

        return packet;
    }

    /// <summary>
    /// 解析 PLC 讀取接點回傳封包為 16 點 Boolean 狀態陣列
    /// 回傳格式: 02 [Byte1] [Byte2] [Byte3] [Byte4] 03 [CS1] [CS2]
    /// 範例: 02 35 35 38 34 03 44 39 (若全為0則為 02 30 30 30 30 03 43 33)
    /// </summary>
    public static bool[] ParseStatusResponse(byte[] responsePacket, bool verifyChecksum = true)
    {
        if (responsePacket == null || responsePacket.Length != 8)
            throw new ArgumentException("回傳封包長度必須為 8 個位元組", nameof(responsePacket));

        if (responsePacket[0] != STX || responsePacket[5] != ETX)
            throw new FormatException("封包格式錯誤：缺少 STX 或 ETX");

        if (verifyChecksum && !VerifyChecksum(responsePacket))
            throw new InvalidOperationException("封包 Checksum 校驗失敗");

        char c1 = (char)responsePacket[1]; // Y4~Y7 / X4~X7
        char c0 = (char)responsePacket[2]; // Y0~Y3 / X0~X3
        char c3 = (char)responsePacket[3]; // Y14~Y17 / X14~X17
        char c2 = (char)responsePacket[4]; // Y10~Y13 / X10~X13

        int n0 = Convert.ToInt32(c0.ToString(), 16);
        int n1 = Convert.ToInt32(c1.ToString(), 16);
        int n2 = Convert.ToInt32(c2.ToString(), 16);
        int n3 = Convert.ToInt32(c3.ToString(), 16);

        bool[] status = new bool[16];

        // 點位 0~3 (低 Nibble)
        for (int i = 0; i < 4; i++) status[i] = ((n0 >> i) & 1) == 1;

        // 點位 4~7 (高 Nibble)
        for (int i = 0; i < 4; i++) status[4 + i] = ((n1 >> i) & 1) == 1;

        // 點位 10~13 (次低 Nibble，對應 index 8~11)
        for (int i = 0; i < 4; i++) status[8 + i] = ((n2 >> i) & 1) == 1;

        // 點位 14~17 (次高 Nibble，對應 index 12~15)
        for (int i = 0; i < 4; i++) status[12 + i] = ((n3 >> i) & 1) == 1;

        return status;
    }

    /// <summary>
    /// 將位元組陣列轉為以空白分隔的十六進制大寫字串 (例如 "02 35 35 38 34 03 43 33")
    /// </summary>
    public static string ToHexString(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return string.Empty;
        return BitConverter.ToString(bytes).Replace("-", " ");
    }
}

/// <summary>
/// PLC I/O 點位詳細規格
/// </summary>
public record IoPointInfo(string Address, string Code, string Description)
{
    public string DisplayText => string.IsNullOrWhiteSpace(Code) || Code == "-"
        ? $"{Address} {Description}"
        : $"{Address} [{Code}] {Description}";
}

