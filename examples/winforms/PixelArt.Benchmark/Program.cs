using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using Eugeniusz;
using Eugeniusz.Samples;
using Eugeniusz.Samples.PixelArt;

if (args.Length == 4 && args[0] == "--combine")
{
    using var before = JsonDocument.Parse(File.ReadAllText(Path.Combine(args[1], "results.json")));
    using var after = JsonDocument.Parse(File.ReadAllText(Path.Combine(args[2], "results.json")));
    var records = before.RootElement.EnumerateArray().Where(r => r.GetProperty("variant").GetString() == "baseline")
        .Concat(after.RootElement.EnumerateArray().Where(r => r.GetProperty("variant").GetString() == "planned")).ToArray();
    var names = records.Select(r => r.GetProperty("name").GetString()!).Distinct().ToArray();
    Directory.CreateDirectory(args[3]);
    File.WriteAllText(Path.Combine(args[3], "results.json"), JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
    using var comparison = new Bitmap(600, names.Length * 292);
    using var drawing = Graphics.FromImage(comparison); drawing.Clear(Color.White);
    using var labelFont = new Font("Segoe UI", 11);
    drawing.InterpolationMode = InterpolationMode.NearestNeighbor; drawing.PixelOffsetMode = PixelOffsetMode.Half;
    foreach (var record in records)
    {
        int side = record.GetProperty("size").GetInt32();
        string name = record.GetProperty("name").GetString()!, variant = record.GetProperty("variant").GetString()!;
        var colors = record.GetProperty("colors").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        using var picture = new Bitmap(side, side);
        for (int y = 0; y < side; y++) for (int x = 0; x < side; x++) picture.SetPixel(x, y, ColorTranslator.FromHtml(PixelPlan.Hex[colors[y * side + x]]));
        picture.Save(Path.Combine(args[3], name + "-" + variant + ".png"), ImageFormat.Png);
        int left = variant == "planned" ? 310 : 10, top = Array.IndexOf(names, name) * 292;
        drawing.DrawString(name + " / " + variant, labelFont, Brushes.Black, left, top + 2);
        drawing.DrawImage(picture, new Rectangle(left, top + 28, 256, 256), 0, 0, side, side, GraphicsUnit.Pixel);
    }
    foreach (string scenePath in Directory.GetFiles(args[2], "*-scene.json")) File.Copy(scenePath, Path.Combine(args[3], Path.GetFileName(scenePath)), true);
    comparison.Save(Path.Combine(args[3], "comparison.png"), ImageFormat.Png);
    return;
}

if (args.Length < 2) throw new ArgumentException("Usage: PixelArt.Benchmark model.gguf output-directory [--cpu] [--planned-only]");
string model = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output); SampleRuntime.Configure();
var cases = new (string Name, string Description)[] {
    ("square", "A red square filling the middle half of the image, on a sky blue background."),
    ("flag", "A flag of three equal vertical stripes: blue on the left, white in the middle, red on the right."),
    ("apple", "A single red apple with a small green leaf and a brown stem, centered on a sky blue background."),
    ("house", "A small white house with a red triangular roof and a brown door, standing on green grass under a blue sky."),
    ("tree", "A green tree with a brown trunk on a cream background."),
    ("boat", "A small brown sailboat with a white triangular sail on blue water under a sky blue sky.")
};
int sizeIndex = Array.IndexOf(args, "--size"), caseOption = Array.IndexOf(args, "--case");
int size = sizeIndex >= 0 ? int.Parse(args[sizeIndex + 1]) : 8;
if (size < 4 || size > 32) throw new ArgumentOutOfRangeException(nameof(size));
if (caseOption >= 0) cases = cases.Where(c => c.Name == args[caseOption + 1]).ToArray();
if (cases.Length == 0) throw new ArgumentException("Unknown benchmark case");
var results = new List<object>();
using var sheet = new Bitmap(600, cases.Length * 292);
using var graphics = Graphics.FromImage(sheet); graphics.Clear(Color.White);
using var font = new Font("Segoe UI", 11);
var options = ModelOptions.Default; options.GpuLayers = args.Contains("--cpu") ? 0 : 99;
foreach (bool planned in args.Contains("--planned-only") ? new[] { true } : new[] { false, true })
{
    using var engine = Engine.Load(model, options, planned ? PixelScene.RendererSystem : null);
    for (int caseIndex = 0; caseIndex < cases.Length; caseIndex++)
    {
        var (name, description) = cases[caseIndex];
        var watch = Stopwatch.StartNew(); var pixels = new PixelPlan(size, 42);
        PixelScene? scene = null; int requests = 0, sceneMatches = 0, correct = 0;
        if (planned)
        {
            scene = await PixelScene.PlanAsync(description, size, (system, prompt, _) => {
                requests++; return Task.FromResult(engine.Generate(system, prompt, 1280));
            }, CancellationToken.None);
            File.WriteAllText(Path.Combine(output, name + "-scene.json"), scene.Json);
        }
        foreach (int index in pixels.Order)
        {
            int x = index % size, y = index / size;
            var answer = scene == null
                ? engine.Choice(pixels.State(description, index), pixels.Question(description, index), PixelPlan.Criteria)
                : engine.Choice(scene.TargetState(description, x, y, size), scene.TargetQuestion(x, y, size), PixelPlan.Criteria);
            requests++; pixels.Set(index, answer.Choice);
            if (scene != null && answer.Choice == scene.ColorAt(x, y, size)) sceneMatches++;
            double u = (x + .5) / size, v = (y + .5) / size;
            int expected = name == "square" ? (u >= .25 && u <= .75 && v >= .25 && v <= .75 ? 7 : 15) : u < 1.0 / 3 ? 2 : u < 2.0 / 3 ? 11 : 7;
            if (answer.Choice == expected) correct++;
        }
        var colors = Enumerable.Range(0, size * size).Select(pixels.Get).ToArray();
        string variant = planned ? "planned" : "baseline";
        results.Add(new { name, description, variant, size, model = Path.GetFileName(model), gpuLayers = options.GpuLayers,
            requests, seconds = Math.Round(watch.Elapsed.TotalSeconds, 2), sceneMatches = planned ? (int?)sceneMatches : null,
            correctPixels = name is "square" or "flag" ? (int?)correct : null, colors });
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        using var bitmap = new Bitmap(size, size);
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) bitmap.SetPixel(x, y, ColorTranslator.FromHtml(PixelPlan.Hex[pixels.Get(y * size + x)]));
        bitmap.Save(Path.Combine(output, name + "-" + variant + ".png"), ImageFormat.Png);
        int left = planned ? 310 : 10, top = caseIndex * 292;
        graphics.DrawString(name + " / " + variant, font, Brushes.Black, left, top + 2);
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor; graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(bitmap, new Rectangle(left, top + 28, 256, 256), 0, 0, size, size, GraphicsUnit.Pixel);
        sheet.Save(Path.Combine(output, "comparison.png"), ImageFormat.Png);
        Console.WriteLine($"{name} / {variant}: {watch.Elapsed.TotalSeconds:F1}s, scene consistency {sceneMatches}/{size * size}" + (name is "square" or "flag" ? $", geometric correctness {correct}/{size * size}" : ""));
    }
}
