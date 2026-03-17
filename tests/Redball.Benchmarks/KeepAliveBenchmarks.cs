using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Redball.Services;

namespace Redball.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<KeepAliveBenchmarks>();
    }
}

[MemoryDiagnoser]
public class KeepAliveBenchmarks
{
    private KeepAwakeService? _service;

    [GlobalSetup]
    public void Setup()
    {
        _service = KeepAwakeService.Instance;
    }

    [Benchmark]
    public void ToggleActive()
    {
        _service?.Toggle();
    }

    [Benchmark]
    public void SetActiveTrue()
    {
        _service?.SetActive(true);
    }

    [Benchmark]
    public void SetActiveFalse()
    {
        _service?.SetActive(false);
    }

    [Benchmark]
    public void GetStatusText()
    {
        _service?.GetStatusText();
    }

    [Benchmark]
    public void StartTimedSession()
    {
        _service?.StartTimed(30);
    }

    [Benchmark]
    public void AutoPauseAndResume()
    {
        _service?.AutoPause("Battery");
        _service?.AutoResume("Battery");
    }
}
