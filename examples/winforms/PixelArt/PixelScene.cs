using System.Text;
using System.Text.Json;

namespace Eugeniusz.Samples.PixelArt;

/// <summary>A model-authored composition. Geometry is evaluated consistently by the host.</summary>
public sealed class PixelScene
{
    public const string PlannerSystem = """
        You draw tiny geometric illustrations. Return only JSON with background and layers. Layers are painted in array order: distant ground/water FIRST, main objects NEXT, small details LAST. Never cover the subject with background scenery.
        Each layer has name, shape, color, x, y, width, height. Allowed shapes: rectangle, ellipse, triangle (points up), diamond. Allowed colors: Black, Navy, Blue, Teal, Dark green, Green, Brown, Red, Orange, Yellow, Cream, White, Gray, Purple, Pink, Sky blue.
        Coordinates are fractions of the canvas. x,y describe the TOP LEFT, not center. y increases DOWN. All boxes must fit inside 0..1. Use at most 12 layers. Make the main subject large enough to recognize. Attached parts touch: a roof's bottom equals the walls' top; a door's bottom equals the walls' bottom; a stem's bottom equals the fruit's top. Ground and water extend to the bottom edge.
        Example of connected parts and correct layering for 'a yellow flower on grass':
        {"background":"Sky blue","layers":[{"name":"ground","shape":"rectangle","color":"Green","x":0,"y":0.8,"width":1,"height":0.2},{"name":"stem","shape":"rectangle","color":"Dark green","x":0.45,"y":0.45,"width":0.1,"height":0.4},{"name":"flower","shape":"ellipse","color":"Yellow","x":0.2,"y":0.1,"width":0.6,"height":0.5},{"name":"center","shape":"ellipse","color":"Orange","x":0.4,"y":0.25,"width":0.2,"height":0.2}]}
        Create the requested scene, not the example. Use exact colors and shapes from the user's description. Return the complete JSON.
        """;

    public const string RendererSystem = """
        You render one pixel of a frozen, model-authored scene. The host has already computed which surface is visible at this pixel, including shape boundaries and occlusion.
        The Visible surface color field is authoritative. Select the palette option matching that color. Do not reinterpret the scene, move shapes, blend colors, or substitute the dominant object color. The user's description explains the whole composition; the visible surface field identifies this specific pixel.
        """;

    public sealed record Layer(string Name, string Shape, int Color, double X, double Y, double Width, double Height)
    {
        public bool Contains(double x, double y)
        {
            // Half-open rectangles assign adjacent bands to exactly one surface.
            if (x < X || y < Y || x >= X + Width || y >= Y + Height) return false;
            double u = 2 * (x - X) / Width - 1, v = 2 * (y - Y) / Height - 1;
            return ContainsUnit(u, v);
        }

        public bool ContainsPixel(int x, int y, int size)
        {
            // Snap a model-authored box to the pixel grid so a narrow roof/leaf
            // cannot disappear between sample centers. Positive boxes get at least one pixel.
            int left = Math.Clamp((int)Math.Round(X * size, MidpointRounding.AwayFromZero), 0, size - 1);
            int top = Math.Clamp((int)Math.Round(Y * size, MidpointRounding.AwayFromZero), 0, size - 1);
            int right = Math.Clamp((int)Math.Round((X + Width) * size, MidpointRounding.AwayFromZero), left + 1, size);
            int bottom = Math.Clamp((int)Math.Round((Y + Height) * size, MidpointRounding.AwayFromZero), top + 1, size);
            if (x < left || y < top || x >= right || y >= bottom) return false;
            return ContainsUnit(2.0 * (x - left + .5) / (right - left) - 1, 2.0 * (y - top + .5) / (bottom - top) - 1);
        }

        private bool ContainsUnit(double u, double v) => Shape switch
            {
                "rectangle" => true,
                "ellipse" => u * u + v * v <= 1,
                "triangle" => Math.Abs(u) <= (v + 1) / 2,
                "diamond" => Math.Abs(u) + Math.Abs(v) <= 1,
                _ => false
            };
    }

    public int Background { get; }
    public IReadOnlyList<Layer> Layers { get; }
    public string Json { get; }
    private PixelScene(int background, List<Layer> layers, string json)
    { Background = background; Layers = layers.AsReadOnly(); Json = json; }

    public static async Task<PixelScene> PlanAsync(string description, int size,
        Func<string, string, CancellationToken, Task<string>> generate, CancellationToken cancellation)
    {
        string request = $"Image description: {description}\nTarget canvas: {size} by {size} pixels. Return the complete scene JSON.";
        for (int attempt = 0; attempt < 2; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            string json = await generate(PlannerSystem, request, cancellation);
            try { return Parse(json); }
            catch (Exception error) when (error is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
            {
                if (attempt == 1) throw new FormatException("The model could not produce a valid scene. Try the pixel instruction model or simplify the description. " + error.Message, error);
                request = $"Image description: {description}\nTarget canvas: {size} by {size} pixels.\nThe previous scene was invalid: {error.Message}\nPrevious output:\n{json}\nReturn a corrected complete JSON scene that satisfies the schema and bounds.";
            }
        }
        throw new InvalidOperationException("Scene planning did not finish.");
    }

    public static PixelScene Parse(string json)
    {
        // Tolerate a single markdown fence, but never accept surrounding prose or partial JSON.
        json = json.Trim();
        if (json.StartsWith("```json", StringComparison.OrdinalIgnoreCase) && json.EndsWith("```")) json = json[7..^3].Trim();
        else if (json.StartsWith("```") && json.EndsWith("```")) json = json[3..^3].Trim();
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        ValidateFields(root, "background", "layers");
        int background = ReadColor(root.GetProperty("background"));
        var shapes = root.GetProperty("layers");
        if (shapes.ValueKind != JsonValueKind.Array || shapes.GetArrayLength() > 12) throw new FormatException("Expected at most 12 scene layers.");
        var layers = new List<Layer>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in shapes.EnumerateArray())
        {
            ValidateFields(item, "name", "shape", "color", "x", "y", "width", "height");
            string name = item.GetProperty("name").GetString()?.Trim() ?? "";
            string shape = item.GetProperty("shape").GetString()?.Trim().ToLowerInvariant() ?? "";
            if (name.Length is < 1 or > 80 || name.Any(char.IsControl) || !names.Add(name)) throw new FormatException("Scene layer names must be short and unique.");
            if (shape is not ("rectangle" or "ellipse" or "triangle" or "diamond")) throw new FormatException("Unsupported scene shape: " + shape);
            double x = ReadNumber(item, "x"), y = ReadNumber(item, "y"), width = ReadNumber(item, "width"), height = ReadNumber(item, "height");
            if (width <= 0 || height <= 0 || x + width > 1.000001 || y + height > 1.000001) throw new FormatException("Scene bounding boxes must fit inside the canvas.");
            layers.Add(new Layer(name, shape, ReadColor(item.GetProperty("color")), x, y, width, height));
        }
        return new PixelScene(background, layers, json);
    }

    private static void ValidateFields(JsonElement element, params string[] allowed)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in element.EnumerateObject())
            if (!allowed.Contains(field.Name) || !seen.Add(field.Name)) throw new FormatException("Unknown or duplicate scene field: " + field.Name);
    }

    private static double ReadNumber(JsonElement item, string name)
    {
        double value = item.GetProperty(name).GetDouble();
        if (!double.IsFinite(value) || value < 0 || value > 1) throw new FormatException("Scene coordinates must be finite fractions in 0..1.");
        return value;
    }

    private static int ReadColor(JsonElement element)
    {
        string color = element.GetString()?.Trim() ?? "";
        int index = Array.FindIndex(PixelPlan.ColorNames, name => string.Equals(name, color, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new FormatException("Unknown palette color: " + color);
        return index;
    }

    public int Surface(int x, int y, int size)
    {
        if (size < 1 || x < 0 || y < 0 || x >= size || y >= size) throw new ArgumentOutOfRangeException(nameof(x));
        for (int i = Layers.Count - 1; i >= 0; i--) if (Layers[i].ContainsPixel(x, y, size)) return i;
        return -1;
    }

    public int ColorAt(int x, int y, int size)
    {
        int surface = Surface(x, y, size);
        return surface < 0 ? Background : Layers[surface].Color;
    }

    public string TargetState(string description, int x, int y, int size)
    {
        int surface = Surface(x, y, size);
        string name = surface < 0 ? "background" : Layers[surface].Name;
        var text = new StringBuilder();
        text.AppendLine("Image description: " + description);
        text.AppendLine($"Canvas: {size} columns by {size} rows. Target: column {x}, row {y} (zero-based, origin top-left).");
        text.AppendLine($"Pixel center: {PositionAt(x, y, size)}.");
        text.AppendLine("A complete scene was planned once. Shape membership and occlusion were computed at this pixel center.");
        text.AppendLine("Visible surface: " + name);
        text.AppendLine("Visible surface color: " + PixelPlan.ColorNames[ColorAt(x, y, size)]);
        return text.ToString();
    }

    public static string PositionAt(int x, int y, int size)
        => FormattableString.Invariant($"top {100 * (y + .5) / size:F1}%, left {100 * (x + .5) / size:F1}%");

    public string TargetQuestion(int x, int y, int size)
    {
        int color = ColorAt(x, y, size);
        return $"The frozen scene requires exactly {PixelPlan.ColorNames[color]} ({PixelPlan.Hex[color]}) at this pixel. Which palette option is that exact color?";
    }
}
