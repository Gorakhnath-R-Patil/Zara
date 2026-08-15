namespace Zara.Engine.Hosting;

/// <summary>Shared, thread-safe status a scan runner writes and
/// <c>IndexServiceImpl</c> reads — deliberately just a flag at M7; grows as
/// T39 wires a real background scan runner into the Engine.</summary>
public sealed class EngineIndexState
{
    private volatile bool _isScanning;

    public bool IsScanning
    {
        get => _isScanning;
        set => _isScanning = value;
    }
}
