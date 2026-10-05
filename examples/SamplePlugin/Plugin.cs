using System;
using LabApi.Loader.Features.Plugins;

namespace SamplePlugin;

public sealed class CounterPlugin : Plugin
{
    public static int Count { get; set; }
    public static void Increment() => Count++;
    public override string Name => "SamplePlugin";
    public override string Description => "Minimal LabTesting integration example";
    public override string Author => "LabTesting";
    public override Version RequiredApiVersion => new(1, 1, 0);
    public override void Enable() => Count = 0;
    public override void Disable() => Count = 0;
}
