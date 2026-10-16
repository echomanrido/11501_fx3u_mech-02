using Fx3uMonitorApp.Protocols;
using Xunit;

namespace Fx3uMonitorApp.Tests;

public class ProtocolTests
{
    [Fact]
    public void ReadYCommand_ShouldMatchSpecification()
    {
        // 規格書: 02 30 30 30 41 30 30 32 03 36 36
        byte[] expected = [0x02, 0x30, 0x30, 0x30, 0x41, 0x30, 0x30, 0x32, 0x03, 0x36, 0x36];
        Assert.Equal(expected, Fx3uProtocol.ReadYCommand);
        Assert.True(Fx3uProtocol.VerifyChecksum(expected));
    }

    [Fact]
    public void ReadXCommand_ShouldMatchSpecification()
    {
        // 規格書: 02 30 30 30 38 30 30 32 03 35 44
        byte[] expected = [0x02, 0x30, 0x30, 0x30, 0x38, 0x30, 0x30, 0x32, 0x03, 0x35, 0x44];
        Assert.Equal(expected, Fx3uProtocol.ReadXCommand);
        Assert.True(Fx3uProtocol.VerifyChecksum(expected));
    }

    [Fact]
    public void ParseStatusResponse_WithSampleData_ShouldDecodeCorrectly()
    {
        // 規格書範例資料: 02 35 35 38 34 03 43 33
        // 第一個數值 35 ('5') 代表 Y4~Y7 (0101b -> Y4:ON, Y6:ON)
        // 第二個數值 35 ('5') 代表 Y0~Y3 (0101b -> Y0:ON, Y2:ON)
        // 第三個數值 38 ('8') 代表 Y14~Y17 (1000b -> Y17:ON)
        // 第四個數值 34 ('4') 代表 Y10~Y13 (0100b -> Y12:ON)
        byte[] samplePacket = [0x02, 0x35, 0x35, 0x38, 0x34, 0x03, 0x43, 0x33];

        // 測試在模擬/自訂資料情況下的解碼 (verifyChecksum = false)
        bool[] status = Fx3uProtocol.ParseStatusResponse(samplePacket, verifyChecksum: false);

        // Y0~Y7 (index 0~7)
        Assert.True(status[0], "Y0 應為 ON");
        Assert.False(status[1], "Y1 應為 OFF");
        Assert.True(status[2], "Y2 應為 ON");
        Assert.False(status[3], "Y3 應為 OFF");
        Assert.True(status[4], "Y4 應為 ON");
        Assert.False(status[5], "Y5 應為 OFF");
        Assert.True(status[6], "Y6 應為 ON");
        Assert.False(status[7], "Y7 應為 OFF");

        // Y10~Y13 (index 8~11)
        Assert.False(status[8], "Y10 應為 OFF");
        Assert.False(status[9], "Y11 應為 OFF");
        Assert.True(status[10], "Y12 應為 ON");
        Assert.False(status[11], "Y13 應為 OFF");

        // Y14~Y17 (index 12~15)
        Assert.False(status[12], "Y14 應為 OFF");
        Assert.False(status[13], "Y15 應為 OFF");
        Assert.False(status[14], "Y16 應為 OFF");
        Assert.True(status[15], "Y17 應為 ON");
    }

    [Fact]
    public void ParseStatusResponse_WithCorrectChecksum_ShouldPass()
    {
        // 全接點 OFF: 02 30 30 30 30 03 43 33 (CheckSum: 0x30*4 + 0x03 = 195 = 0xC3)
        byte[] allOffPacket = [0x02, 0x30, 0x30, 0x30, 0x30, 0x03, 0x43, 0x33];
        Assert.True(Fx3uProtocol.VerifyChecksum(allOffPacket));
        bool[] statusAllOff = Fx3uProtocol.ParseStatusResponse(allOffPacket, verifyChecksum: true);
        Assert.All(statusAllOff, s => Assert.False(s));

        // 35 35 38 34 的嚴格 Checksum 為 D9 (0x44, 0x39)
        byte[] validChecksumPacket = [0x02, 0x35, 0x35, 0x38, 0x34, 0x03, 0x44, 0x39];
        Assert.True(Fx3uProtocol.VerifyChecksum(validChecksumPacket));
        bool[] status = Fx3uProtocol.ParseStatusResponse(validChecksumPacket, verifyChecksum: true);
        Assert.True(status[0]);
        Assert.True(status[2]);
        Assert.True(status[4]);
        Assert.True(status[6]);
        Assert.True(status[10]);
        Assert.True(status[15]);
    }

    [Theory]
    // 規格書 Y0~Y17 強制指令測試
    [InlineData(0, true, "02 37 30 30 30 35 03 46 46")]
    [InlineData(0, false, "02 38 30 30 30 35 03 30 30")]
    [InlineData(1, true, "02 37 30 31 30 35 03 30 30")]
    [InlineData(1, false, "02 38 30 31 30 35 03 30 31")]
    [InlineData(2, true, "02 37 30 32 30 35 03 30 31")]
    [InlineData(2, false, "02 38 30 32 30 35 03 30 32")]
    [InlineData(3, true, "02 37 30 33 30 35 03 30 32")]
    [InlineData(3, false, "02 38 30 33 30 35 03 30 33")]
    [InlineData(4, true, "02 37 30 34 30 35 03 30 33")]
    [InlineData(4, false, "02 38 30 34 30 35 03 30 34")]
    [InlineData(5, true, "02 37 30 35 30 35 03 30 34")]
    [InlineData(5, false, "02 38 30 35 30 35 03 30 35")]
    [InlineData(6, true, "02 37 30 36 30 35 03 30 35")]
    [InlineData(6, false, "02 38 30 36 30 35 03 30 36")]
    [InlineData(7, true, "02 37 30 37 30 35 03 30 36")]
    [InlineData(7, false, "02 38 30 37 30 35 03 30 37")]
    [InlineData(8, true, "02 37 30 38 30 35 03 30 37")]
    [InlineData(8, false, "02 38 30 38 30 35 03 30 38")]
    [InlineData(9, true, "02 37 30 39 30 35 03 30 38")]
    [InlineData(9, false, "02 38 30 39 30 35 03 30 39")]
    [InlineData(10, true, "02 37 30 41 30 35 03 31 30")]
    [InlineData(10, false, "02 38 30 41 30 35 03 31 31")]
    [InlineData(11, true, "02 37 30 42 30 35 03 31 31")]
    [InlineData(11, false, "02 38 30 42 30 35 03 31 32")]
    [InlineData(12, true, "02 37 30 43 30 35 03 31 32")]
    [InlineData(12, false, "02 38 30 43 30 35 03 31 33")]
    [InlineData(13, true, "02 37 30 44 30 35 03 31 33")]
    [InlineData(13, false, "02 38 30 44 30 35 03 31 34")]
    [InlineData(14, true, "02 37 30 45 30 35 03 31 34")]
    [InlineData(14, false, "02 38 30 45 30 35 03 31 35")]
    [InlineData(15, true, "02 37 30 46 30 35 03 31 35")]
    [InlineData(15, false, "02 38 30 46 30 35 03 31 36")]
    public void BuildForceYCommand_ShouldMatchSpecificationTable(int index, bool state, string expectedHex)
    {
        byte[] command = Fx3uProtocol.BuildForceYCommand(index, state);
        string actualHex = Fx3uProtocol.ToHexString(command);

        Assert.Equal(expectedHex, actualHex);
        Assert.True(Fx3uProtocol.VerifyChecksum(command));
    }

    [Fact]
    public void IoDefinitions_ShouldMatchConfiguration()
    {
        Assert.Equal(16, Fx3uProtocol.XPointDefinitions.Length);
        Assert.Equal(16, Fx3uProtocol.YPointDefinitions.Length);

        // 驗證關鍵控制面板點位
        Assert.Equal("X16", Fx3uProtocol.XPointDefinitions[14].Address);
        Assert.Equal("ST/RST", Fx3uProtocol.XPointDefinitions[14].Code);

        Assert.Equal("X17", Fx3uProtocol.XPointDefinitions[15].Address);
        Assert.Equal("EMS", Fx3uProtocol.XPointDefinitions[15].Code);

        // 驗證面板指示燈
        Assert.Equal("Y15", Fx3uProtocol.YPointDefinitions[13].Address);
        Assert.Equal("RL", Fx3uProtocol.YPointDefinitions[13].Code);

        Assert.Equal("Y16", Fx3uProtocol.YPointDefinitions[14].Address);
        Assert.Equal("YL", Fx3uProtocol.YPointDefinitions[14].Code);

        Assert.Equal("Y17", Fx3uProtocol.YPointDefinitions[15].Address);
        Assert.Equal("GL", Fx3uProtocol.YPointDefinitions[15].Code);
    }
}
