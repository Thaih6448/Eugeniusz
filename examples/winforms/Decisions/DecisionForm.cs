using System.Globalization;

namespace Eugeniusz.Samples.Decisions;

public sealed class DecisionForm : SampleForm
{
    private readonly ComboBox kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly TextBox state = Ui.TextArea("I was charged twice for my subscription.", 70);
    private readonly TextBox prompt = Ui.TextArea("Which team should handle this ticket?", 55);
    private readonly TextBox criteria = Ui.TextArea("Billing: payments, charges and invoices\r\nShipping: deliveries and lost parcels\r\nTechnical: software faults", 85);
    private readonly NumericUpDown temperature = new() { DecimalPlaces = 2, Minimum = .05M, Maximum = 20, Value = 1, Increment = .05M, Width = 75 };
    private readonly NumericUpDown threshold = new() { DecimalPlaces = 2, Minimum = 0, Maximum = 1, Value = 0, Increment = .05M, Width = 75 };
    private readonly Button evaluate = Ui.Button("Evaluate", true);
    private readonly Button cancel = Ui.Button("Cancel");
    private readonly Label answer = Ui.Label("Your typed answer will appear here.");
    private readonly Label criteriaLabel = Ui.Label("Options — one description per line (2–26)");
    private readonly DataGridView distribution = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None };
    private Result? lastResult;

    public DecisionForm() : base("Decision playground")
    {
        ClientSize = new Size(1080, 880); MinimumSize = new Size(880, 840);
        kind.Items.AddRange(new object[] { "Choice", "Score", "Truth" }); kind.SelectedIndex = 0;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10 };
        foreach (float height in new float[] { 42, 28, 85, 28, 64, 28, 96, 58, 45 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var settings = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        settings.Controls.AddRange(new Control[] { Ui.Label("DECISION TYPE"), kind, Ui.Label("Temperature"), temperature, Ui.Label("Min. confidence"), threshold });
        layout.Controls.Add(settings, 0, 0);
        layout.Controls.Add(Ui.Label("State / context (optional)"), 0, 1); layout.Controls.Add(state, 0, 2);
        layout.Controls.Add(Ui.Label("Prompt / question"), 0, 3); layout.Controls.Add(prompt, 0, 4);
        layout.Controls.Add(criteriaLabel, 0, 5); layout.Controls.Add(criteria, 0, 6);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        buttons.Controls.AddRange(new Control[] { evaluate, cancel }); layout.Controls.Add(buttons, 0, 7);
        answer.Font = new Font(Font, FontStyle.Bold); layout.Controls.Add(answer, 0, 8);
        distribution.Columns.Add("option", "Option / level"); distribution.Columns.Add("probability", "Probability"); distribution.Columns.Add("logit", "Raw logit");
        layout.Controls.Add(distribution, 0, 9); Body.Controls.Add(layout);
        kind.SelectedIndexChanged += (_, _) => UpdateKind();
        evaluate.Click += async (_, _) => await EvaluateAsync(); cancel.Click += (_, _) => CancelOperation();
        BusyChanged(false);
    }

    private void UpdateKind()
    {
        criteria.Enabled = !Busy && kind.SelectedIndex != 2;
        criteriaLabel.Text = kind.SelectedIndex switch
        {
            1 => "Rubric levels — one per line, lowest to highest (2–26)",
            2 => "Truth uses two outcomes automatically: false / true",
            _ => "Options — one description per line (2–26)"
        };
    }

    private Task EvaluateAsync() => RunAsync(async token =>
    {
        if (string.IsNullOrWhiteSpace(prompt.Text)) throw new ArgumentException("Enter a prompt / question.");
        var type = (Kind)kind.SelectedIndex;
        string[] options = type == Kind.Truth ? Array.Empty<string>() : criteria.Lines.Select(s => s.Trim()).Where(s => s.Length != 0).ToArray();
        if (type != Kind.Truth && (options.Length < 2 || options.Length > 26)) throw new ArgumentException("Enter between 2 and 26 options or rubric levels.");
        Status.Text = "Evaluating locally…";
        Result result = await Model.EvaluateAsync(state.Text, prompt.Text, options, type, token, (double)temperature.Value, (double)threshold.Value);
        lastResult = result;
        answer.Text = type switch
        {
            Kind.Choice => $"Choice: {options[result.Choice]}",
            Kind.Score => $"Score: {result.Value:F3} on a 0–{result.Count - 1} scale",
            _ => $"P(true): {result.Value:P2} • {(result.Value >= .5 ? "True" : "False")} is more likely"
        };
        if (result.Abstained) answer.Text += " • ABSTAINED";
        distribution.Rows.Clear();
        for (int i = 0; i < result.Count; i++) distribution.Rows.Add(type == Kind.Truth ? (i == 0 ? "False" : "True") : options[i], result.Probabilities[i].ToString("P3"), result.Logits[i].ToString("F4", CultureInfo.InvariantCulture));
        Status.Text = $"Completed in {Model.LastMilliseconds:F0} ms • Confidence {result.Confidence:P2} • Certainty {result.Certainty:P2} • Abstained: {result.Abstained}";
    });

    protected override void BusyChanged(bool busy)
    {
        evaluate.Enabled = !busy; cancel.Enabled = busy; kind.Enabled = !busy;
        state.ReadOnly = prompt.ReadOnly = busy; temperature.Enabled = threshold.Enabled = !busy;
        UpdateKind();
    }

    public async Task SmokeTestAsync()
    {
        await EvaluateAsync();
        if (LastError != null || lastResult?.Kind != Kind.Choice) throw new Exception(LastError ?? "Choice result missing");
        kind.SelectedIndex = 1; state.Text = "The entire service is down."; prompt.Text = "How severe is the incident?";
        criteria.Text = "Cosmetic\r\nDegraded service\r\nTotal outage";
        await EvaluateAsync();
        if (LastError != null || lastResult?.Kind != Kind.Score || lastResult.Value.Value < 0 || lastResult.Value.Value > 2) throw new Exception(LastError ?? "Invalid score");
        kind.SelectedIndex = 2; state.Text = "The parcel arrived yesterday."; prompt.Text = "Has the parcel arrived?";
        await EvaluateAsync();
        if (LastError != null || lastResult?.Value <= .5) throw new Exception(LastError ?? "Truth smoke case failed");
        string validPath = Model.ModelPath.Text; Model.ModelPath.Text = validPath + ".missing";
        await EvaluateAsync();
        if (LastError == null) throw new Exception("Missing model should be reported in the UI");
        Model.ModelPath.Text = validPath; await EvaluateAsync();
        if (LastError != null) throw new Exception("Recovery after model path error failed: " + LastError);
    }
}
