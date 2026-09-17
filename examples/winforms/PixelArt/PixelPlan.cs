using System.Text;

namespace Eugeniusz.Samples.PixelArt;

public sealed class PixelPlan
{
    public static readonly string[] ColorNames = { "Black", "Navy", "Blue", "Teal", "Dark green", "Green", "Brown", "Red", "Orange", "Yellow", "Cream", "White", "Gray", "Purple", "Pink", "Sky blue" };
    public static readonly string[] Hex = { "#101820", "#202D59", "#305CCF", "#008080", "#205C35", "#55AB41", "#854C30", "#D83C45", "#EF8834", "#F4D04B", "#F5E4BF", "#FFFFFF", "#85939D", "#7947A8", "#F28FB8", "#8FD3EB" };
    public static string[] Criteria => Enumerable.Range(0, 16).Select(i => $"{ColorNames[i]} ({Hex[i]})").ToArray();
    private readonly int[] colors;
    public int Size { get; }
    public int[] Order { get; }
    public int Filled { get; private set; }
    public PixelPlan(int size, int? seed = null)
    {
        if (size < 4 || size > 32) throw new ArgumentOutOfRangeException(nameof(size));
        Size = size; colors = Enumerable.Repeat(-1, size * size).ToArray();
        Order = Enumerable.Range(0, size * size).ToArray();
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
        for (int i = Order.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1); (Order[i], Order[j]) = (Order[j], Order[i]);
        }
    }
    public void Set(int index, int color)
    {
        if (index < 0 || index >= colors.Length || color < 0 || color >= 16) throw new ArgumentOutOfRangeException(nameof(color));
        if (colors[index] < 0) Filled++;
        colors[index] = color;
    }
    public int Get(int index) => colors[index];
    public string Position(int index)
    {
        if (index < 0 || index >= colors.Length) throw new ArgumentOutOfRangeException(nameof(index));
        int x = index % Size, y = index / Size;
        return FormattableString.Invariant($"top {100.0 * y / (Size - 1):F0}%, left {100.0 * x / (Size - 1):F0}%");
    }

    // Retained as the independent-pixel baseline for the comparison runner.
    // The GUI now uses PixelScene.TargetState and TargetQuestion instead.
    public string State(string description, int index)
    {
        var text = new StringBuilder();
        text.AppendLine("Create a coherent tiny pixel-art image by choosing a color for one pixel at a time.");
        text.AppendLine($"Image description: {description}");
        text.AppendLine($"Canvas: {Size} by {Size}. Target pixel position: {Position(index)}.");
        text.AppendLine("Top is distance down from the top edge: 0% is the top row, 100% is the bottom row. Left is distance across from the left edge: 0% is the leftmost column, 100% is the rightmost column.");
        return text.ToString();
    }

    public string Question(string description, int index)
    {
        return description + $"\nApply this specification at {Position(index)}. Select the color of ONLY this target pixel, not the dominant color of the whole image.";
    }
}
