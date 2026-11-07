using TwinCatMcp.Runtime;

namespace TwinCatMcp.Tests.Unit;

public sealed class RuntimeRealTimeXmlTests
{
    // ProduceXml() of TIRS in a fresh project (TwinCAT 3.1.4026.22).
    private const string ProducedTirs =
        "<?xml version=\"1.0\"?><TreeItem><ItemName>Real-Time</ItemName><PathName>TIRS</PathName><ItemType>31</ItemType><ItemId>1</ItemId>" +
        "<ChildCount>0</ChildCount><Disabled>false</Disabled><TreeImageId>47</TreeImageId><RTimeSetDef><RouterMemory>32768</RouterMemory>" +
        "<MaxStackSize>64</MaxStackSize><CoreBoostActive>false</CoreBoostActive><RtLogMask>#x0</RtLogMask><TargetCPUInfo><RealTimeCPUs>1</RealTimeCPUs>" +
        "<CPUFamily>0</CPUFamily><CPUFrequency>0</CPUFrequency><CPUType>0</CPUType><AvailabeCPUs>256</AvailabeCPUs><NonWindowsCPUs>128</NonWindowsCPUs>" +
        "<NumProcessors>1</NumProcessors><CpuConfigValidOnTarget>1</CpuConfigValidOnTarget><RTimeCapabilities>0</RTimeCapabilities></TargetCPUInfo>" +
        "<MaxCPUs>1</MaxCPUs><Affinity>#x1</Affinity><PCoreAffinity>0</PCoreAffinity><ECoreAffinity>0</ECoreAffinity><CPUs><CPU id=\"0\">" +
        "<LoadLimit>80</LoadLimit><BaseTime>10000</BaseTime><LatencyWarning>0</LatencyWarning><CpuMemorySize>524288</CpuMemorySize>" +
        "<CoreFrequency>0</CoreFrequency><CpuMemoryAllocLimit>2048</CpuMemoryAllocLimit></CPU></CPUs></RTimeSetDef></TreeItem>";

    [Fact]
    public void Parses_real_time_settings()
    {
        var s = RealTimeXml.Parse(ProducedTirs);

        Assert.Equal(1, s.MaxCpus);
        Assert.Equal("#x1", s.Affinity);
        Assert.Equal([0], s.RealTimeCores);
        Assert.Equal(32768, s.RouterMemoryKb);
        Assert.Equal(64, s.MaxStackSizeKb);
        var cpu = Assert.Single(s.Cpus);
        Assert.Equal(0, cpu.Id);
        Assert.Equal(80, cpu.LoadLimitPercent);
        Assert.Equal(10000u, cpu.BaseTime.Ticks100ns);
        Assert.Equal(1.0, cpu.BaseTime.Milliseconds);
        Assert.Equal(0, cpu.LatencyWarningUs);
        Assert.Equal(1, s.TargetCpuInfo!.NumProcessors);
        Assert.Equal(1, s.TargetCpuInfo.RealTimeCpus);
    }

    [Fact]
    public void Builds_RTimeSetDef_fragment_like_the_Beckhoff_core_sample()
    {
        // Structure from the Beckhoff sample "Assigning CPU cores" (TIRS ConsumeXml).
        var xml = RealTimeXml.Build(
            maxCpus: 3,
            affinity: 0x7,
            cpus:
            [
                new RealTimeXml.CpuSetting(0, LoadLimitPercent: 10, BaseTimeTicks: 10000, LatencyWarningUs: 200),
                new RealTimeXml.CpuSetting(1, LoadLimitPercent: 20, BaseTimeTicks: 5000, LatencyWarningUs: null),
            ],
            routerMemoryKb: null);

        Assert.Equal(
            "<TreeItem><RTimeSetDef><MaxCPUs>3</MaxCPUs><Affinity>#x0000000000000007</Affinity><CPUs>" +
            "<CPU id=\"0\"><LoadLimit>10</LoadLimit><BaseTime>10000</BaseTime><LatencyWarning>200</LatencyWarning></CPU>" +
            "<CPU id=\"1\"><LoadLimit>20</LoadLimit><BaseTime>5000</BaseTime></CPU>" +
            "</CPUs></RTimeSetDef></TreeItem>",
            xml);
    }

    [Fact]
    public void Builds_only_router_memory_when_nothing_else_is_given()
        => Assert.Equal(
            "<TreeItem><RTimeSetDef><RouterMemory>65536</RouterMemory></RTimeSetDef></TreeItem>",
            RealTimeXml.Build(maxCpus: null, affinity: null, cpus: [], routerMemoryKb: 65536));
}
