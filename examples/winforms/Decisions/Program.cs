namespace Eugeniusz.Samples.Decisions;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var form = new DecisionForm();
        SampleRunner.Run(form, args, form.SmokeTestAsync);
    }
}
