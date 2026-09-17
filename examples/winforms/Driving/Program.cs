namespace Eugeniusz.Samples.Driving;
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var form = new DrivingForm();
        int benchmark = Array.IndexOf(args, "--benchmark");
        if (benchmark >= 0)
        {
            if (benchmark + 1 >= args.Length) throw new ArgumentException("--benchmark requires an output directory.");
            string output = Path.GetFullPath(args[benchmark + 1]);
            int seedsIndex = Array.IndexOf(args, "--seeds");
            int[] seeds = seedsIndex >= 0 && seedsIndex + 1 < args.Length ? args[seedsIndex + 1].Split(',').Select(int.Parse).ToArray() : new[] { 42, 7, 103 };
            if (seeds.Any(s => s < 0)) throw new ArgumentException("Seeds must be nonnegative.");
            SampleRunner.Run(form, args.Concat(new[] { "--smoke-test", output }).ToArray(), () => form.BenchmarkAsync(output, seeds));
        }
        else SampleRunner.Run(form, args, form.SmokeTestAsync);
    }
}
