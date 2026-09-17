using System.Diagnostics;
using System.Drawing.Imaging;

namespace Eugeniusz.Samples.PixelArt;

public sealed class PixelArtForm : SampleForm
{
    private readonly TextBox prompt = Ui.TextArea("A small white house with a red triangular roof and a brown door, standing on green grass under a blue sky.", 65);
    private readonly NumericUpDown resolution = new() { Minimum = 4, Maximum = 32, Value = 12, Width = 65 };
    private readonly Button generate = Ui.Button("Generate", true);
    private readonly Button stop = Ui.Button("Stop");
    private readonly Button save = Ui.Button("Save PNG…");
    private readonly PixelCanvas canvas = new();
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill };
    private readonly Label detail = Ui.Label("One model decision per pixel • 16 colors • Pixels are visited in random order.");
    private readonly ToolTip tips = new();
    private PixelPlan? plan;
    private Bitmap? image;
    private PixelScene? scene;

    public PixelArtForm() : base("16-color pixel generator")
    {
        Model.PreferModel("Qwen3-4B-Q4_K_M.gguf");
        Model.PreferModel("Qwen3-4B-Instruct-2507-Q4_K_M.gguf");
        Model.SystemPrompt = PixelScene.RendererSystem;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8 };
        foreach (float height in new float[] { 27, 76, 58, 36 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(Ui.Label("IMAGE PROMPT"), 0, 0); layout.Controls.Add(prompt, 0, 1);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        controls.Controls.AddRange(new Control[] { generate, stop, save, Ui.Label("Square resolution"), resolution, Ui.Label("pixels per side (4–32)") });
        layout.Controls.Add(controls, 0, 2);
        var palette = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        palette.Controls.Add(Ui.Label("PALETTE"));
        for (int i = 0; i < 16; i++)
        {
            var swatch = new Panel { Width = 27, Height = 27, BackColor = ColorTranslator.FromHtml(PixelPlan.Hex[i]), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 2, 5, 0), AccessibleName = PixelPlan.Criteria[i] };
            tips.SetToolTip(swatch, PixelPlan.Criteria[i]); palette.Controls.Add(swatch);
        }
        layout.Controls.Add(palette, 0, 3); layout.Controls.Add(canvas, 0, 4); layout.Controls.Add(detail, 0, 5);
        layout.Controls.Add(progress, 0, 6); layout.Controls.Add(Ui.Label("Simple geometric illustrations • A shared composition keeps pixel decisions consistent."), 0, 7);
        Body.Controls.Add(layout);
        generate.Click += async (_, _) => await GenerateAsync(); stop.Click += (_, _) => CancelOperation();
        save.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = "eugeniusz-pixel-art.png" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                try { image!.Save(dialog.FileName, ImageFormat.Png); Status.Text = "Saved " + dialog.FileName; }
                catch (Exception error) { Status.Text = "Cannot save image: " + error.Message; }
            }
        };
        BusyChanged(false);
    }

    private Task GenerateAsync() => RunAsync(async token =>
    {
        if (string.IsNullOrWhiteSpace(prompt.Text)) throw new ArgumentException("Describe the image you want to draw.");
        int size = (int)resolution.Value;
        plan = new PixelPlan(size); image?.Dispose(); image = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        canvas.Image = image; canvas.Invalidate();
        progress.Value = 0; progress.Maximum = size * size;
        string description = prompt.Text;
        string[] options = PixelPlan.Criteria;
        var elapsed = Stopwatch.StartNew();
        Status.Text = "Planning the composition…"; detail.Text = "Preparing the image before painting its pixels…";
        progress.Style = ProgressBarStyle.Marquee;
        scene = await PixelScene.PlanAsync(description, size, (system, input, ct) => Model.GenerateAsync(system, input, ct, 1280), token);
        progress.Style = ProgressBarStyle.Blocks;
        foreach (int pixel in plan.Order)
        {
            token.ThrowIfCancellationRequested();
            int x = pixel % size, y = pixel / size;
            Status.Text = $"Choosing a color at {PixelScene.PositionAt(x, y, size)}…";
            Result result = await Model.EvaluateAsync(scene.TargetState(description, x, y, size),
                scene.TargetQuestion(x, y, size), options, Kind.Choice, token);
            plan.Set(pixel, result.Choice); image.SetPixel(x, y, ColorTranslator.FromHtml(PixelPlan.Hex[result.Choice]));
            progress.Value = plan.Filled; canvas.Invalidate();
            detail.Text = $"{plan.Filled}/{size * size} pixels • {PixelScene.PositionAt(x, y, size)} → {PixelPlan.ColorNames[result.Choice]} • {elapsed.Elapsed.TotalSeconds:F1} s";
        }
        Status.Text = $"Finished {size} × {size} image in {elapsed.Elapsed.TotalSeconds:F1} s. Save PNG preserves the original resolution.";
    });

    protected override void BusyChanged(bool busy)
    {
        generate.Enabled = resolution.Enabled = !busy; prompt.ReadOnly = busy; stop.Enabled = busy;
        save.Enabled = !busy && image != null;
        if (!busy) progress.Style = ProgressBarStyle.Blocks;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { tips.Dispose(); image?.Dispose(); }
        base.Dispose(disposing);
    }

    public async Task SmokeTestAsync()
    {
        resolution.Value = 8; await GenerateAsync();
        if (LastError != null || plan?.Filled != 64) throw new Exception(LastError ?? "Generation did not fill every pixel");
        var allowed = PixelPlan.Hex.Select(h => ColorTranslator.FromHtml(h).ToArgb()).ToHashSet();
        for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            if (!allowed.Contains(image!.GetPixel(x, y).ToArgb())) throw new Exception("Generated color is outside the palette");
        using var data = new MemoryStream(); image!.Save(data, ImageFormat.Png); data.Position = 0;
        using var decoded = new Bitmap(data);
        if (decoded.Width != 8 || decoded.Height != 8) throw new Exception("PNG dimensions are incorrect");
    }
}
