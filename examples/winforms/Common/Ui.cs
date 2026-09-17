using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;

namespace Eugeniusz.Samples;

public static class Ui
{
    public static readonly Color Background = Color.FromArgb(244, 247, 250);
    public static readonly Color Ink = Color.FromArgb(26, 43, 61);
    public static readonly Color Accent = Color.FromArgb(0, 111, 105);
    public static Button Button(string text, bool primary = false) => new()
    {
        Text = text, AutoSize = true, MinimumSize = new Size(100, 36),
        FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 4, 10, 4),
        BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink
    };
    public static Label Label(string text) => new() { Text = text, AutoSize = true, ForeColor = Ink, Margin = new Padding(0, 1, 8, 1), TextAlign = ContentAlignment.MiddleLeft };
    public static TextBox TextArea(string text = "", int height = 100) => new()
    {
        Text = text, Multiline = true, ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill, MinimumSize = new Size(80, height), BorderStyle = BorderStyle.FixedSingle
    };
    public static void SavePreview(Form form, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(path, ImageFormat.Png);
    }
}

/// <summary>One persistent model, loaded on a worker thread; no UI thread inference.</summary>
public sealed class ModelPanel : UserControl
{
    public TextBox ModelPath { get; } = new() { Dock = DockStyle.Fill };
    public ComboBox Backend { get; } = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly Label details = Ui.Label("Model loads on the first request and stays in memory.");
    private Engine? engine;
    private string key = "";
    public double LastMilliseconds { get; private set; }
    public long Evaluations { get; private set; }
    public string? SystemPrompt { get; set; }

    public ModelPanel()
    {
        Dock = DockStyle.Top; Height = 132; Padding = new Padding(20, 12, 20, 10); BackColor = Color.White;
        Backend.Items.AddRange(new object[] { "CPU", "GPU (all layers)" });
        Backend.SelectedIndex = 0;
        ModelPath.Text = SampleRuntime.FindModel();
        var browse = Ui.Button("Browse…");
        browse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "GGUF model (*.gguf)|*.gguf", CheckFileExists = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) ModelPath.Text = dialog.FileName;
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        layout.Controls.Add(Ui.Label("LOCAL MODEL / GGUF"), 0, 0);
        layout.Controls.Add(ModelPath, 0, 1); layout.Controls.Add(browse, 1, 1); layout.Controls.Add(Backend, 2, 1);
        layout.Controls.Add(details, 0, 2); layout.SetColumnSpan(details, 3);
        Controls.Add(layout);
    }

    public void PreferModel(string filename)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EUGENIUSZ_MODEL"))) return;
        // Bundles keep their selected profile. Source checkouts may use a better demo model.
        if (SampleRuntime.Ancestors().Any(root => File.Exists(Path.Combine(root, "profile.json")))) return;
        foreach (string root in SampleRuntime.Ancestors())
        {
            string path = Path.Combine(root, "models", "downloads", filename);
            if (File.Exists(path)) { ModelPath.Text = path; return; }
        }
    }

    public Task<Result> EvaluateAsync(string state, string question, string[] criteria, Kind kind,
        CancellationToken cancellation, double temperature = 1, double threshold = 0)
        => ExecuteAsync(engine => engine.Evaluate(state, question, criteria, kind, temperature, threshold), cancellation);

    public Task<string> GenerateAsync(string systemPrompt, string prompt, CancellationToken cancellation, uint maxTokens = 1024)
        => ExecuteAsync(engine => engine.Generate(systemPrompt, prompt, maxTokens), cancellation);

    private async Task<T> ExecuteAsync<T>(Func<Engine, T> action, CancellationToken cancellation)
    {
        // This method is called on the UI thread. Only captured values enter the worker.
        string path = Path.GetFullPath(ModelPath.Text.Trim());
        int layers = Backend.SelectedIndex == 1 ? 99 : 0;
        string? system = SystemPrompt;
        if (!File.Exists(path)) throw new FileNotFoundException("Choose an existing GGUF file. Download light with: python scripts/download_model.py light", path);
        details.Text = engine == null ? "Loading model…" : "Evaluating…";
        var timer = Stopwatch.StartNew();
        var result = await Task.Run(() =>
        {
            cancellation.ThrowIfCancellationRequested();
            SampleRuntime.Configure();
            string requestedKey = path + "|" + layers + "|" + system;
            if (engine == null || key != requestedKey)
            {
                engine?.Dispose(); engine = null; key = "";
                var options = ModelOptions.Default;
                options.ContextSize = 4096; options.GpuLayers = layers;
                engine = Engine.Load(path, options, system); key = requestedKey;
            }
            cancellation.ThrowIfCancellationRequested();
            return action(engine);
        });
        LastMilliseconds = timer.Elapsed.TotalMilliseconds;
        Evaluations++;
        details.Text = $"Loaded • {(layers == 0 ? "CPU" : "GPU")} • {LastMilliseconds:F0} ms (includes load on first call) • {Evaluations} requests";
        cancellation.ThrowIfCancellationRequested();
        return result;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { engine?.Dispose(); engine = null; }
        base.Dispose(disposing);
    }
}

public abstract class SampleForm : Form
{
    public ModelPanel Model { get; } = new();
    protected Panel Body { get; } = new() { Dock = DockStyle.Fill, Padding = new Padding(20) };
    protected ToolStripStatusLabel Status { get; } = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    public bool Busy { get; private set; }
    public string? LastError { get; private set; }
    private CancellationTokenSource? operation;
    private bool closeWhenFinished;
    private readonly TextBox errorText = new() { Dock = DockStyle.Bottom, Height = 65, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.MistyRose, Visible = false };

    protected SampleForm(string title)
    {
        Text = title + " | Eugeniusz"; Font = new Font("Segoe UI", 10);
        BackColor = Ui.Background; ForeColor = Ui.Ink;
        ClientSize = new Size(1080, 780); MinimumSize = new Size(880, 700);
        StartPosition = FormStartPosition.CenterScreen;
        var status = new StatusStrip(); status.Items.Add(Status);
        Controls.Add(Body); Controls.Add(Model); Controls.Add(errorText); Controls.Add(status);
        Status.Text = "Ready • Local inference • Probabilities are uncalibrated unless you supply a fitted temperature.";
    }

    protected abstract void BusyChanged(bool busy);
    protected void CancelOperation() => operation?.Cancel();
    protected async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (Busy) return;
        Busy = true; LastError = null; errorText.Visible = false; Model.Enabled = false;
        operation = new CancellationTokenSource(); BusyChanged(true);
        try { await action(operation.Token); }
        catch (OperationCanceledException) { Status.Text = "Stopped. The current native evaluation finished before cancellation."; }
        catch (Exception error) { LastError = error.Message; errorText.Text = error.Message; errorText.Visible = true; Status.Text = "Error — see the message above. Correct the input and try again."; }
        finally
        {
            Busy = false; Model.Enabled = true; operation.Dispose(); operation = null; BusyChanged(false);
            if (closeWhenFinished) Close();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (Busy)
        {
            e.Cancel = true; closeWhenFinished = true; CancelOperation();
            Status.Text = "Finishing the active native call before closing…";
        }
        base.OnFormClosing(e);
    }
}

/// <summary>Pixel-perfect nearest-neighbor display, without mutating the source bitmap.</summary>
public sealed class PixelCanvas : Control
{
    public Bitmap? Image { get; set; }
    public PixelCanvas() { DoubleBuffered = true; Dock = DockStyle.Fill; BackColor = Color.FromArgb(226, 232, 239); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Image == null) return;
        int scale = Math.Max(1, Math.Min((Width - 24) / Image.Width, (Height - 24) / Image.Height));
        var bounds = new Rectangle((Width - Image.Width * scale) / 2, (Height - Image.Height * scale) / 2, Image.Width * scale, Image.Height * scale);
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(Image, bounds, new Rectangle(0, 0, Image.Width, Image.Height), GraphicsUnit.Pixel);
    }
}
