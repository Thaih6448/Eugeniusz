using Eugeniusz.Samples.Snake;
using Eugeniusz.Samples.PixelArt;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var left = new SnakeGame(6, 3, new[] { new Cell(3, 3), new Cell(2, 3), new Cell(1, 3) }, Direction.Right, new Cell(5, 5));
Check(left.Step(0) == MoveOutcome.Moved && left.Heading == Direction.Up && left.Body[0] == new Cell(3, 2), "Left turn must be relative to heading");
var right = new SnakeGame(6, 3, new[] { new Cell(3, 3), new Cell(2, 3), new Cell(1, 3) }, Direction.Right, new Cell(5, 5));
Check(right.Step(1) == MoveOutcome.Moved && right.Heading == Direction.Down && right.Body[0] == new Cell(3, 4), "Right turn must be clockwise");
var victory = new SnakeGame(6, 1, new[] { new Cell(3, 3), new Cell(2, 3), new Cell(1, 3) }, Direction.Right, new Cell(4, 3));
Check(victory.Step(2) == MoveOutcome.Won && victory.Apples == 1 && victory.Wins == 1 && victory.Body.Count == 4, "Eating must grow and count a victory at the target");
victory.ResetRound();
Check(victory.Wins == 1 && victory.Apples == 1 && victory.RoundApples == 0 && !victory.Terminal, "New rounds preserve session totals");
var wall = new SnakeGame(6, 3, new[] { new Cell(5, 2), new Cell(4, 2), new Cell(3, 2) }, Direction.Right, new Cell(5, 5));
Check(wall.Step(2) == MoveOutcome.Died && wall.Deaths == 1 && wall.Terminal, "Wall collision must count a death");
var tail = new SnakeGame(6, 3, new[] { new Cell(2, 2), new Cell(2, 3), new Cell(1, 3), new Cell(1, 2) }, Direction.Up, new Cell(5, 5));
Check(tail.Step(0) == MoveOutcome.Moved && tail.Body[0] == new Cell(1, 2), "Moving into a departing tail is legal");
var collision = new SnakeGame(6, 3, new[] { new Cell(2, 2), new Cell(2, 3), new Cell(1, 3), new Cell(1, 2), new Cell(1, 1) }, Direction.Up, new Cell(5, 5));
Check(collision.Step(0) == MoveOutcome.Died && collision.Deaths == 1, "Moving into a non-tail body cell must count a death");
Check(left.PromptState().Contains("Length:") && left.PromptState().Contains("Apple:") && left.PromptState().Contains("Heading:"), "Snake prompt must describe observable state");

var pixels = new PixelPlan(8, 42);
Check(pixels.Order.Distinct().Count() == 64 && pixels.Order.Min() == 0 && pixels.Order.Max() == 63, "Random order must visit each pixel exactly once");
Check(!pixels.Order.SequenceEqual(Enumerable.Range(0, 64)), "Order must be shuffled");
foreach (int i in pixels.Order) pixels.Set(i, i % 16);
Check(pixels.Filled == 64 && PixelPlan.Criteria.Length == 16, "Every pixel must have a palette entry");
pixels.Set(0, 1); Check(pixels.Filled == 64, "Replacing a pixel must not increment completion");
Check(pixels.Position(0) == "top 0%, left 0%" && pixels.Position(7) == "top 0%, left 100%" && pixels.Position(56) == "top 100%, left 0%" && pixels.Position(63) == "top 100%, left 100%", "Percent positions must map corners without swapping axes");
Check(pixels.Position(10) == "top 14%, left 29%", "Percent positions must round to whole percentages");
Check(pixels.State("An apple", 10).Contains("top 14%, left 29%") && pixels.State("An apple", 10).Contains("An apple") && pixels.Question("An apple", 10).Contains("top 14%, left 29%"), "State and question must agree on the pixel position");
bool rejected = false; try { pixels.Set(0, 16); } catch (ArgumentOutOfRangeException) { rejected = true; }
Check(rejected, "Out-of-palette colors must be rejected");
var scene = PixelScene.Parse("""
    {"background":"Sky blue","layers":[
      {"name":"body","shape":"rectangle","color":"Red","x":0.25,"y":0.25,"width":0.5,"height":0.5},
      {"name":"detail","shape":"ellipse","color":"Green","x":0.375,"y":0.375,"width":0.25,"height":0.25}
    ]}
    """);
Check(scene.ColorAt(0, 0, 8) == 15 && scene.ColorAt(2, 2, 8) == 7 && scene.ColorAt(3, 3, 8) == 5, "Pixel centers and layer occlusion must be consistent");
Check(scene.ColorAt(6, 6, 8) == 15 && scene.TargetState("A square", 3, 3, 8).Contains("Visible surface color: Green"), "Target facts must describe the topmost visible surface");
var triangle = new PixelScene.Layer("roof", "triangle", 7, 0, 0, 1, 1);
Check(triangle.Contains(.5, .1) && !triangle.Contains(.1, .1) && triangle.Contains(.1, .9), "Triangle must point up");
var smallRoof = new PixelScene.Layer("roof", "triangle", 7, .35, .15, .3, .15);
Check(Enumerable.Range(0, 64).Any(i => smallRoof.ContainsPixel(i % 8, i / 8, 8)), "A sub-row roof must survive snapping to the pixel grid");
foreach (string invalid in new[] { "{}", scene.Json.Replace("Red", "Ultraviolet"), scene.Json.Replace("\"x\":0.25", "\"x\":0.9"), scene.Json.Replace("ellipse", "script"), scene.Json.Replace("\"x\":0.25", "\"rotation\":180,\"x\":0.25") })
{
    bool invalidRejected = false;
    try { PixelScene.Parse(invalid); } catch (Exception e) when (e is FormatException or KeyNotFoundException) { invalidRejected = true; }
    Check(invalidRejected, "Malformed scenes must not be silently accepted");
}
int attempts = 0;
var repaired = await PixelScene.PlanAsync("A square", 8, (_, _, _) => Task.FromResult(++attempts == 1 ? "{}" : scene.Json), CancellationToken.None);
Check(attempts == 2 && repaired.Layers.Count == 2, "An invalid scene must receive one bounded repair attempt");
attempts = 0;
bool repairFailed = false;
try { await PixelScene.PlanAsync("A square", 8, (_, _, _) => { attempts++; return Task.FromResult("{}"); }, CancellationToken.None); }
catch (FormatException) { repairFailed = true; }
Check(repairFailed && attempts == 2, "Persistent malformed scenes must fail without an infinite retry loop");
Console.WriteLine("PASS: game rules, pixel coverage, palette bounds, scene validation/repair, occlusion, and pixel-grid snapping.");
DrivingTests.Run();
