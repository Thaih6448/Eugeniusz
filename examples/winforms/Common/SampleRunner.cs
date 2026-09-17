namespace Eugeniusz.Samples;

public static class SampleRunner
{
    public static void Run(SampleForm form, string[] args, Func<Task> smokeTest)
    {
        if (args.Contains("--gpu")) form.Model.Backend.SelectedIndex = 1;
        int modelIndex = Array.IndexOf(args, "--model");
        if (modelIndex >= 0 && modelIndex + 1 < args.Length) form.Model.ModelPath.Text = Path.GetFullPath(args[modelIndex + 1]);
        int smokeIndex = Array.IndexOf(args, "--smoke-test");
        if (smokeIndex >= 0)
        {
            string output = smokeIndex + 1 < args.Length ? Path.GetFullPath(args[smokeIndex + 1]) : Path.GetFullPath("smoke-output");
            form.Shown += async (_, _) =>
            {
                try
                {
                    await smokeTest();
                    if (form.LastError != null) throw new InvalidOperationException(form.LastError);
                    Ui.SavePreview(form, Path.Combine(output, "preview.png"));
                    File.WriteAllText(Path.Combine(output, "result.txt"), $"PASS\n{form.Text}\nEvaluations: {form.Model.Evaluations}\n");
                }
                catch (Exception error)
                {
                    Directory.CreateDirectory(output);
                    File.WriteAllText(Path.Combine(output, "result.txt"), error.ToString());
                    Environment.ExitCode = 1;
                }
                finally { form.Close(); }
            };
        }
        Application.Run(form);
    }
}
