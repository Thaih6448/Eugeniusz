namespace Eugeniusz.Samples.Snake;
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var form = new SnakeForm();
        SampleRunner.Run(form, args, form.SmokeTestAsync);
    }
}
