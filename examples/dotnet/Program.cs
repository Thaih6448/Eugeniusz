using Eugeniusz;
using Eugeniusz.Samples;

try
{
SampleRuntime.Configure();
var result = Engine.FromLogits(new[] { 0.0, Math.Log(3.0) }, Kind.Truth);
if (Math.Abs(result.Value - 0.75) > 1e-10) throw new Exception("C# ABI check failed");
Console.WriteLine($"C# ABI check passed: P(true)={result.Value:F3}");
if (args.Length > 0)
{
    var options = ModelOptions.Default;
    if (args.Length > 1) options.GpuLayers = int.Parse(args[1]);
    using var engine = Engine.Load(args[0], options);
    var decision = engine.Choice("I was charged twice for my subscription.", "Which team should handle this ticket?",
        new[] { "Billing: payments, charges and invoices", "Shipping: deliveries and lost parcels", "Technical: software faults" });
    Console.WriteLine($"Choice={decision.Choice}, confidence={decision.Confidence:F3}");
    var truth = engine.Truth("The parcel arrived.", "Has the parcel arrived?");
    if (!double.IsFinite(truth.Value)) throw new Exception("Invalid inference output");
    Console.WriteLine($"P(true)={truth.Value:F3}");
}
}
catch (Exception error)
{
    Console.Error.WriteLine($"Eugeniusz: {error.Message}");
    Environment.ExitCode = 1;
}
