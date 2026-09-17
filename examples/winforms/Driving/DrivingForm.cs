using System.Drawing.Drawing2D;
using System.Text.Json;

namespace Eugeniusz.Samples.Driving;

public sealed class DrivingForm : SampleForm
{
    private DrivingGame game = new(new DrivingTrack(42));
    private readonly TrackCanvas canvas = new();
    private readonly Button start = Ui.Button("Start model", true), stop = Ui.Button("Pause"), retry = Ui.Button("Retry track"), randomize = Ui.Button("New track");
    private readonly NumericUpDown seed = new() { Minimum = 0, Maximum = int.MaxValue, Value = 42, Width = 110 };
    private readonly Label score = Ui.Label(""), sensors = Ui.Label(""), telemetry = Ui.Label("");
    private readonly TextBox log = Ui.TextArea("", 65);
    private int crashes, wins, timeouts;
    private bool resultCounted;
    private readonly List<object> trace = new();
    public DrivingForm() : base("Model drives a car")
    {
        Model.PreferModel("Qwen3-1.7B-Q8_0.gguf");
        Model.PreferModel("Qwen3-4B-Instruct-2507-Q4_K_M.gguf");
        Model.SystemPrompt = DrivingGame.SystemPrompt;
        log.ReadOnly = true; log.Font = new Font("Consolas", 9);
        score.Font = new Font("Segoe UI", 15, FontStyle.Bold);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
        foreach (float height in new[] { 36f, 50f }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        foreach (float height in new[] { 28f, 28f, 28f, 75f }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        controls.Controls.AddRange(new Control[] { start, stop, retry, randomize, Ui.Label("Track seed"), seed });
        layout.Controls.Add(score, 0, 0); layout.Controls.Add(controls, 0, 1); layout.Controls.Add(canvas, 0, 2);
        layout.Controls.Add(sensors, 0, 3); layout.Controls.Add(telemetry, 0, 4);
        layout.Controls.Add(Ui.Label("Five rays: left 60° / 30° • front • right 30° / 60°     |     Reach the checkered finish without contact."), 0, 5);
        layout.Controls.Add(log, 0, 6); Body.Controls.Add(layout);
        start.Click += async (_, _) => await DriveAsync(); stop.Click += (_, _) => CancelOperation();
        retry.Click += (_, _) => ResetTrack();
        randomize.Click += (_, _) => { int next; do { next = Random.Shared.Next(int.MaxValue); } while (next == (int)seed.Value); seed.Value = next; };
        seed.ValueChanged += (_, _) => ResetTrack();
        RefreshScene(); BusyChanged(false);
    }

    private void ResetTrack()
    {
        game = new DrivingGame(new DrivingTrack((int)seed.Value)); resultCounted = false; log.Clear(); trace.Clear();
        canvas.ClearTrail(); RefreshScene(); BusyChanged(false); Status.Text = "Ready • The same seed reproduces the same track.";
    }

    private void RefreshScene()
    {
        if (game.Terminal && !resultCounted)
        {
            resultCounted = true;
            if (game.Outcome == DriveOutcome.Collision) crashes++;
            else if (game.Outcome == DriveOutcome.Finished) wins++;
            else timeouts++;
        }
        canvas.Game = game; canvas.RecordPosition(); canvas.Invalidate();
        score.Text = $"Finished  {wins}       Collisions  {crashes}       Timeouts  {timeouts}       Progress  {game.Progress:P0}";
        sensors.Text = string.Join("     ", game.Sensors().Select(s => $"{s.Name}: {s.Distance:F0}"));
        telemetry.Text = $"Speed {game.Speed:F1}/{DrivingGame.MaximumSpeed} u/s • Simulation {game.Time:F1} s • Decisions {game.Decisions} • {game.Outcome} • {DrivingGame.Actions[game.Action]}";
    }

    private Task DriveAsync(int maxDecisions = int.MaxValue, bool animate = true) => RunAsync(async token =>
    {
        for (int i = 0; i < maxDecisions && !game.Terminal; i++)
        {
            token.ThrowIfCancellationRequested(); Status.Text = "Reading five sensors and choosing pedal + steering…";
            Result steering = await Model.EvaluateAsync(game.PromptState(), DrivingGame.SteeringQuestion, DrivingGame.Steering, Kind.Choice, token);
            double steeringMilliseconds = Model.LastMilliseconds;
            Result pedal = await Model.EvaluateAsync(game.PromptState(), DrivingGame.PedalQuestion, DrivingGame.Pedals, Kind.Choice, token);
            double milliseconds = steeringMilliseconds + Model.LastMilliseconds;
            game.SelectAction(pedal.Choice * 3 + steering.Choice);
            trace.Add(new { decision = game.Decisions, time = game.Time, speed = game.Speed,
                sensors = game.Sensors().Select(s => new { s.Name, s.Distance }).ToArray(),
                action = DrivingGame.Actions[game.Action], steeringConfidence = steering.Confidence, pedalConfidence = pedal.Confidence, milliseconds });
            log.AppendText($"{game.Decisions,3}: {DrivingGame.Actions[game.Action],-33} steer {steering.Confidence:P0} / pedal {pedal.Confidence:P0} • {milliseconds:F0} ms\r\n");
            if (log.Lines.Length > 120) log.Text = string.Join(Environment.NewLine, log.Lines.TakeLast(80));
            log.SelectionStart = log.TextLength; log.ScrollToCaret();
            for (int frame = 0; frame < 10 && !game.Terminal; frame++)
            {
                token.ThrowIfCancellationRequested(); game.Advance(DrivingGame.DecisionSeconds / 10); RefreshScene();
                if (animate) await Task.Delay(30, token);
            }
            Status.Text = game.Outcome switch {
                DriveOutcome.Collision => "Collision • Retry this seed or generate a new track.",
                DriveOutcome.Finished => "Finished without contact! • Generate a new track to try again.",
                DriveOutcome.Timeout => "Time limit reached (120 simulation seconds) • Retry or choose another model.",
                _ => $"{DrivingGame.Actions[game.Action]} • {milliseconds:F0} ms inference • Simulation waits for each decision."
            };
        }
    });

    protected override void BusyChanged(bool busy)
    {
        start.Enabled = !busy && !game.Terminal; stop.Enabled = busy;
        retry.Enabled = randomize.Enabled = seed.Enabled = !busy;
        start.Text = game.Decisions > 0 && !game.Terminal ? "Resume model" : "Start model";
    }

    public async Task SmokeTestAsync()
    {
        await DriveAsync(8, false);
        if (LastError != null || game.Decisions != 8 || Model.Evaluations != 16 || game.Sensors().Length != 5)
            throw new Exception(LastError ?? "Driving did not execute eight sensor-based model decisions.");
        Task pending = DriveAsync(2, false); await Task.Delay(10); CancelOperation(); await pending;
        if (Busy) throw new Exception("Driving failed to pause.");
    }

    public async Task BenchmarkAsync(string directory, int[] seeds)
    {
        Directory.CreateDirectory(directory); var results = new List<object>();
        foreach (int value in seeds)
        {
            seed.Value = value; ResetTrack();
            await DriveAsync(400, false);
            if (LastError != null) throw new Exception(LastError);
            Ui.SavePreview(this, Path.Combine(directory, $"track-{value}.png"));
            File.WriteAllText(Path.Combine(directory, $"track-{value}.log"), log.Text);
            File.WriteAllText(Path.Combine(directory, $"track-{value}-decisions.json"), JsonSerializer.Serialize(trace, new JsonSerializerOptions { WriteIndented = true }));
            results.Add(new { seed = value, outcome = game.Outcome.ToString(), game.Decisions, game.Time, game.Progress,
                model = Path.GetFileName(Model.ModelPath.Text), backend = Model.Backend.Text });
            File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private sealed class TrackCanvas : Control
    {
        public DrivingGame? Game { get; set; }
        private readonly List<Vec> trail = new();
        public TrackCanvas() { DoubleBuffered = true; Dock = DockStyle.Fill; BackColor = Color.FromArgb(22, 41, 38); }
        public void ClearTrail() => trail.Clear();
        public void RecordPosition()
        {
            if (Game != null && (trail.Count == 0 || (trail[^1] - Game.Position).Length > 3)) trail.Add(Game.Position);
        }
        private static PointF Point(Vec v) => new((float)v.X, (float)v.Y);
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (Game == null || Width <= 0 || Height <= 0) return;
            var g = e.Graphics; var saved = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float top = (float)Game.Track.Polygon.Min(p => p.Y) - 28, bottom = (float)Game.Track.Polygon.Max(p => p.Y) + 42;
            float worldHeight = bottom - top;
            float scale = Math.Min(Width / (float)DrivingTrack.WorldWidth, Height / worldHeight);
            g.TranslateTransform((Width - (float)DrivingTrack.WorldWidth * scale) / 2, (Height - worldHeight * scale) / 2 - top * scale);
            g.ScaleTransform(scale, scale);
            using var road = new SolidBrush(Color.FromArgb(61, 73, 83));
            using var boundary = new Pen(Color.FromArgb(237, 241, 219), 3);
            g.FillPolygon(road, Game.Track.Polygon.Select(Point).ToArray());
            g.DrawPolygon(boundary, Game.Track.Polygon.Select(Point).ToArray());
            using var center = new Pen(Color.FromArgb(119, 133, 137), 1.5f) { DashStyle = DashStyle.Dash };
            g.DrawLines(center, Game.Track.Center.Select(Point).ToArray());
            double finishY = Game.Track.CenterY(Game.Track.FinishX);
            for (int row = 0; row < 16; row++) for (int col = 0; col < 2; col++)
                g.FillRectangle((row + col) % 2 == 0 ? Brushes.WhiteSmoke : Brushes.Black, (float)Game.Track.FinishX + col * 7, (float)finishY - 80 + row * 10, 7, 10);
            using var markerFont = new Font("Segoe UI", 15, FontStyle.Bold);
            g.DrawString("START", markerFont, Brushes.WhiteSmoke, (float)Game.Track.Start.X - 25, (float)Game.Track.Start.Y + 90);
            g.DrawString("FINISH", markerFont, Brushes.WhiteSmoke, (float)Game.Track.FinishX - 35, (float)finishY + 90);
            using var obstacle = new SolidBrush(Color.FromArgb(234, 132, 83));
            using var rim = new Pen(Color.FromArgb(255, 195, 123), 2);
            foreach (Circle circle in Game.Track.Obstacles)
            {
                var box = new RectangleF((float)(circle.Center.X - circle.Radius), (float)(circle.Center.Y - circle.Radius), (float)(circle.Radius * 2), (float)(circle.Radius * 2));
                g.FillEllipse(obstacle, box); g.DrawEllipse(rim, box);
            }
            using var trailPen = new Pen(Color.FromArgb(75, 175, 182), 2);
            if (trail.Count > 1) g.DrawLines(trailPen, trail.Select(Point).ToArray());
            Color[] colors = { Color.MediumPurple, Color.DeepSkyBlue, Color.Yellow, Color.Cyan, Color.HotPink };
            var readings = Game.Sensors();
            for (int i = 0; i < readings.Length; i++)
            {
                using var ray = new Pen(colors[i], 2); using var hit = new SolidBrush(colors[i]);
                g.DrawLine(ray, Point(Game.Position), Point(readings[i].End));
                g.FillEllipse(hit, (float)readings[i].End.X - 3, (float)readings[i].End.Y - 3, 6, 6);
            }
            var carState = g.Save(); g.TranslateTransform((float)Game.Position.X, (float)Game.Position.Y); g.RotateTransform((float)(Game.Heading * 180 / Math.PI));
            using var hull = new SolidBrush(Game.Outcome == DriveOutcome.Collision ? Color.Tomato : Color.FromArgb(115, 239, 192));
            g.FillRectangle(Brushes.Black, -9, -10, 7, 20); g.FillRectangle(Brushes.Black, 4, -10, 6, 20);
            g.FillRectangle(hull, -11, -7, 22, 14); g.FillRectangle(Brushes.DarkSlateGray, 1, -5, 5, 10);
            using var collisionCircle = new Pen(Color.FromArgb(160, 255, 255, 255), 1) { DashStyle = DashStyle.Dot };
            g.DrawEllipse(collisionCircle, -13, -13, 26, 26);
            g.Restore(carState); g.Restore(saved);
        }
    }
}
