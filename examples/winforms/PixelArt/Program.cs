namespace Eugeniusz.Samples.PixelArt;
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var form = new PixelArtForm();
        SampleRunner.Run(form, args, form.SmokeTestAsync);
    }
}
