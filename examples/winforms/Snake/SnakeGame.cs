using System.Text;

namespace Eugeniusz.Samples.Snake;

public readonly record struct Cell(int X, int Y);
public enum Direction { Up, Right, Down, Left }
public enum MoveOutcome { Moved, AteApple, Died, Won }

/// <summary>Pure game rules. The model selects every move; there is no steering fallback.</summary>
public sealed class SnakeGame
{
    private readonly Random random;
    private readonly List<Cell> body = new();
    public int Size { get; }
    public int Goal { get; }
    public IReadOnlyList<Cell> Body => body;
    public Cell Apple { get; private set; }
    public Direction Heading { get; private set; }
    public int Apples { get; private set; }
    public int Deaths { get; private set; }
    public int Wins { get; private set; }
    public int RoundApples { get; private set; }
    public int Moves { get; private set; }
    public int MovesWithoutApple { get; private set; }
    public bool Terminal { get; private set; }
    public string LastEvent { get; private set; } = "Ready";

    public SnakeGame(int size = 12, int goal = 6, int? seed = null)
    {
        if (size < 5 || goal < 1 || goal > size * size - 3) throw new ArgumentOutOfRangeException(nameof(goal));
        Size = size; Goal = goal; random = seed.HasValue ? new Random(seed.Value) : new Random(); ResetRound();
    }

    // Deterministic scenarios exercise edge cases without involving a language model.
    internal SnakeGame(int size, int goal, IEnumerable<Cell> cells, Direction heading, Cell apple) : this(size, goal, 1)
    {
        body.Clear(); body.AddRange(cells); Heading = heading; Apple = apple;
    }

    public void ResetRound()
    {
        int middle = Size / 2;
        body.Clear(); body.AddRange(new[] { new Cell(middle, middle), new Cell(middle - 1, middle), new Cell(middle - 2, middle) });
        Heading = Direction.Right; RoundApples = 0; MovesWithoutApple = 0; Terminal = false;
        PlaceApple(); LastEvent = "New round";
    }

    private void PlaceApple()
    {
        var free = new List<Cell>();
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                if (!body.Contains(new Cell(x, y))) free.Add(new Cell(x, y));
        if (free.Count > 0) Apple = free[random.Next(free.Count)];
    }

    public Cell NextCell(int action)
    {
        if (action < 0 || action > 2) throw new ArgumentOutOfRangeException(nameof(action));
        int turn = action == 0 ? -1 : action == 1 ? 1 : 0;
        var direction = (Direction)(((int)Heading + turn + 4) % 4);
        return direction switch
        {
            Direction.Up => body[0] with { Y = body[0].Y - 1 },
            Direction.Right => body[0] with { X = body[0].X + 1 },
            Direction.Down => body[0] with { Y = body[0].Y + 1 },
            _ => body[0] with { X = body[0].X - 1 }
        };
    }

    public bool WouldCollide(int action)
    {
        Cell next = NextCell(action);
        bool grows = next == Apple;
        return next.X < 0 || next.X >= Size || next.Y < 0 || next.Y >= Size || body.Take(body.Count - (grows ? 0 : 1)).Contains(next);
    }

    public MoveOutcome Step(int action)
    {
        if (Terminal) throw new InvalidOperationException("Reset the round after a death or victory.");
        Cell next = NextCell(action); Moves++;
        if (WouldCollide(action))
        {
            Deaths++; Terminal = true; LastEvent = "Collision — round lost"; return MoveOutcome.Died;
        }
        Heading = (Direction)(((int)Heading + (action == 0 ? -1 : action == 1 ? 1 : 0) + 4) % 4);
        body.Insert(0, next);
        if (next == Apple)
        {
            Apples++; RoundApples++; MovesWithoutApple = 0;
            if (RoundApples >= Goal || body.Count == Size * Size)
            {
                Wins++; Terminal = true; LastEvent = "Apple target reached — victory"; return MoveOutcome.Won;
            }
            PlaceApple(); LastEvent = "Apple collected"; return MoveOutcome.AteApple;
        }
        body.RemoveAt(body.Count - 1);
        MovesWithoutApple++;
        if (MovesWithoutApple >= Size * Size * 4)
        {
            Deaths++; Terminal = true; LastEvent = "Move limit without an apple — round lost"; return MoveOutcome.Died;
        }
        LastEvent = "Moving"; return MoveOutcome.Moved;
    }

    public string PromptState()
    {
        var text = new StringBuilder();
        text.AppendLine($"Snake on a {Size} by {Size} grid. Coordinates start at (0,0) in the top-left. X increases right; Y increases down. Walls do not wrap.");
        text.AppendLine($"Length: {body.Count}. Head: ({body[0].X},{body[0].Y}). Heading: {Heading}. Apple: ({Apple.X},{Apple.Y}).");
        text.AppendLine($"Body from head to tail: {string.Join(" ", body.Select(c => $"({c.X},{c.Y})"))}.");
        text.AppendLine($"Apples this round: {RoundApples}/{Goal}. Moves without food: {MovesWithoutApple}/{Size * Size * 4}.");
        text.AppendLine("Choose a relative turn followed by one step. Avoid walls and your body; reach the apple. Moving into the departing tail is legal when not eating.");
        for (int action = 0; action < 3; action++)
        {
            Cell next = NextCell(action);
            text.AppendLine($"{new[] { "Turn left", "Turn right", "Keep direction" }[action]} -> ({next.X},{next.Y}); collision: {WouldCollide(action)}; distance to apple: {Math.Abs(next.X - Apple.X) + Math.Abs(next.Y - Apple.Y)}.");
        }
        return text.ToString();
    }

    public string[] ActionCriteria() => Enumerable.Range(0, 3).Select(action =>
    {
        Cell next = NextCell(action);
        string name = new[] { "Turn left", "Turn right", "Keep the current direction" }[action];
        return $"{name}: next head ({next.X},{next.Y}), " +
            (WouldCollide(action) ? "COLLISION, immediate death" : $"safe, distance to apple {Math.Abs(next.X - Apple.X) + Math.Abs(next.Y - Apple.Y)}");
    }).ToArray();
}
