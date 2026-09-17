using System.Drawing.Drawing2D;

namespace Eugeniusz.Samples.Snake;

public sealed class SnakeForm : SampleForm
{
    private SnakeGame game = new();
    private readonly Board board = new();
    private readonly Label scoreboard = Ui.Label("");
    private readonly Label current = Ui.Label("");
    private readonly TextBox log = Ui.TextArea("", 90);
    private readonly NumericUpDown goal = new() { Minimum = 1, Maximum = 100, Value = 6, Width = 80 };
    private readonly NumericUpDown delay = new() { Minimum = 0, Maximum = 5000, Value = 80, Increment = 20, Width = 90 };
    private readonly Button start = Ui.Button("Start model", true);
    private readonly Button stop = Ui.Button("Stop");
    private readonly Button reset = Ui.Button("Reset statistics");

    public SnakeForm() : base("Model plays Snake")
    {
        Model.PreferModel("Qwen3-1.7B-Q8_0.gguf");
        log.ReadOnly = true; log.Font = new Font("Consolas", 9);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        scoreboard.Font = new Font("Segoe UI", 17, FontStyle.Bold); layout.Controls.Add(scoreboard, 0, 0);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        controls.Controls.AddRange(new Control[] { start, stop, reset, Ui.Label("Apples to win"), goal, Ui.Label("Delay (ms)"), delay });
        layout.Controls.Add(controls, 0, 1); layout.Controls.Add(board, 0, 2); layout.Controls.Add(current, 0, 3); layout.Controls.Add(log, 0, 4);
        Body.Controls.Add(layout);
        start.Click += async (_, _) => await PlayAsync(); stop.Click += (_, _) => CancelOperation();
        reset.Click += (_, _) => { game = new SnakeGame(goal: (int)goal.Value); log.Clear(); RefreshBoard(); };
        goal.ValueChanged += (_, _) => { game = new SnakeGame(goal: (int)goal.Value); log.Clear(); RefreshBoard(); };
        RefreshBoard(); BusyChanged(false);
    }

    private void RefreshBoard()
    {
        board.Game = game; board.Invalidate();
        scoreboard.Text = $"Apples  {game.Apples}          Deaths  {game.Deaths}          Victories  {game.Wins}";
        current.Text = $"Length {game.Body.Count} • Round {game.RoundApples}/{game.Goal} apples • Heading {game.Heading} • {game.Moves} moves • {game.LastEvent}";
    }

    private Task PlayAsync(int maxMoves = int.MaxValue) => RunAsync(async token =>
    {
        for (int move = 0; move < maxMoves; move++)
        {
            token.ThrowIfCancellationRequested();
            if (game.Terminal) { game.ResetRound(); RefreshBoard(); }
            Status.Text = "The model is choosing the next relative turn…";
            Result decision = await Model.EvaluateAsync(game.PromptState(), "Choose a safe action with the smallest distance to the apple. Reject any action that causes a collision. If safe distances tie, prefer keeping the current direction. Which action is best?", game.ActionCriteria(), Kind.Choice, token);
            MoveOutcome outcome = game.Step(decision.Choice); RefreshBoard();
            string action = new[] { "LEFT", "RIGHT", "STRAIGHT" }[decision.Choice];
            log.AppendText($"Move {game.Moves,4}: {action,-8} confidence {decision.Confidence:P1}, {Model.LastMilliseconds:F0} ms — {game.LastEvent}\r\n");
            if (log.Lines.Length > 150) log.Text = string.Join(Environment.NewLine, log.Lines.TakeLast(100));
            log.SelectionStart = log.TextLength; log.ScrollToCaret();
            Status.Text = $"{action} • {Model.LastMilliseconds:F0} ms • No steering fallback: model mistakes can cause deaths.";
            await Task.Delay(outcome is MoveOutcome.Died or MoveOutcome.Won ? 350 : (int)delay.Value, token);
        }
    });

    protected override void BusyChanged(bool busy)
    {
        start.Enabled = reset.Enabled = goal.Enabled = !busy; stop.Enabled = busy;
    }

    public async Task SmokeTestAsync()
    {
        delay.Value = 0; await PlayAsync(6);
        if (LastError != null || game.Moves != 6 || Model.Evaluations != 6) throw new Exception(LastError ?? "Snake failed to execute six model decisions");
        // Verify stop waits for any pending call and does not keep issuing new ones.
        Task play = PlayAsync(); await Task.Delay(30); CancelOperation(); await play;
        if (Busy) throw new Exception("Snake did not stop");
    }

    private sealed class Board : Control
    {
        public SnakeGame? Game { get; set; }
        public Board() { DoubleBuffered = true; Dock = DockStyle.Fill; BackColor = Color.FromArgb(21, 37, 51); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (Game == null) return;
            int cell = Math.Max(1, Math.Min((Width - 24) / Game.Size, (Height - 24) / Game.Size));
            int x = (Width - Game.Size * cell) / 2, y = (Height - Game.Size * cell) / 2;
            using var line = new Pen(Color.FromArgb(39, 56, 69));
            for (int i = 0; i <= Game.Size; i++)
            {
                e.Graphics.DrawLine(line, x + i * cell, y, x + i * cell, y + Game.Size * cell);
                e.Graphics.DrawLine(line, x, y + i * cell, x + Game.Size * cell, y + i * cell);
            }
            using var head = new SolidBrush(Game.Terminal ? Color.Gold : Color.FromArgb(123, 237, 180));
            using var body = new SolidBrush(Color.FromArgb(42, 166, 133));
            for (int i = Game.Body.Count - 1; i >= 0; i--)
                e.Graphics.FillRectangle(i == 0 ? head : body, x + Game.Body[i].X * cell + 2, y + Game.Body[i].Y * cell + 2, cell - 3, cell - 3);
            using var apple = new SolidBrush(Color.FromArgb(251, 106, 102));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillEllipse(apple, x + Game.Apple.X * cell + 3, y + Game.Apple.Y * cell + 3, cell - 6, cell - 6);
        }
    }
}
