using Eugeniusz.Samples.Driving;

internal static class DrivingTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static DrivingTrack Straight(Circle[]? circles = null, Vec? start = null, double heading = 0, double finish = 900) =>
        new(new[] { new Vec(0, 0), new Vec(1000, 0), new Vec(1000, 400), new Vec(0, 400) }, circles ?? Array.Empty<Circle>(), start ?? new Vec(100, 200), heading, finish);
    public static void Run()
    {
        var track = Straight(new[] { new Circle(new Vec(200, 200), 10), new Circle(new Vec(300, 200), 20) });
        Check(Math.Abs(track.Raycast(new Vec(100, 200), 0, 230) - 90) < 1e-6, "A ray must hit the nearest circle surface, not its center.");
        Check(Math.Abs(track.Raycast(new Vec(100, 200), -Math.PI / 2, 230) - 200) < 1e-6, "A ray must see the track boundary.");
        Check(track.Raycast(new Vec(100, 200), 0, 50) == 50, "A ray must respect its range limit.");
        Check(Math.Abs(track.Raycast(new Vec(100, 190), 0, 230) - 100) < 1e-6, "A tangent ray must touch the circle.");
        Check(track.Collides(new Vec(177, 200), 13) && track.Collides(new Vec(13, 100), 13), "Touching an obstacle or boundary is a collision.");
        var forward = new DrivingGame(Straight()); var rotated = new DrivingGame(Straight(heading: Math.PI / 2));
        var rays = forward.Sensors();
        Check(rays.Length == 5 && rays[0].End.Y < forward.Position.Y && rays[4].End.Y > forward.Position.Y, "Left/right sensors must be relative to the car.");
        Check(Math.Abs(rotated.Sensors()[2].Distance - 200) < 1e-6, "Front sensor must rotate with heading.");
        Check(forward.PromptState().Contains("LEFT 60") && forward.PromptState().Contains("RIGHT 30") && !forward.PromptState().Contains("centerline"), "Only local observations should be supplied to the driver.");
        Check(forward.PromptState().Contains("BALANCED") && forward.PromptState().Contains("BELOW target") && forward.PromptState().Contains("ENOUGH room to stop"), "Observation summaries must match symmetric, stationary, open-road readings.");
        forward.SelectAction(1); forward.Advance(1);
        Check(forward.Speed > 19 && forward.Position.X > 100 && Math.Abs(forward.Position.Y - 200) < 1e-6, "Acceleration must move the car forward.");
        forward.SelectAction(7); forward.Advance(1);
        Check(forward.Speed == 0, "Braking must stop without reversing.");
        var left = new DrivingGame(Straight()); left.SelectAction(0); left.Advance(1);
        var right = new DrivingGame(Straight()); right.SelectAction(2); right.Advance(1);
        Check(left.Heading < 0 && right.Heading > 0, "Steering must change the heading in the requested direction.");
        var obstacle = new DrivingGame(Straight(new[] { new Circle(new Vec(180, 200), 2) }));
        obstacle.SelectAction(1); for (int i = 0; i < 5; i++) obstacle.Advance(1);
        Check(obstacle.Outcome == DriveOutcome.Collision && obstacle.Position.X < 168, "Substeps must prevent tunneling through a small obstacle.");
        var wall = new DrivingGame(Straight(start: new Vec(970, 200), finish: 1100));
        wall.SelectAction(1); for (int i = 0; i < 3; i++) wall.Advance(1);
        Check(wall.Outcome == DriveOutcome.Collision, "Leaving the road must end the race.");
        var winner = new DrivingGame(Straight(finish: 115));
        winner.SelectAction(1); winner.Advance(1); winner.Advance(1);
        Check(winner.Outcome == DriveOutcome.Finished && winner.Progress == 1, "Crossing a safe finish line must count as completion.");
        var blockedFinish = new DrivingGame(Straight(new[] { new Circle(new Vec(127, 200), 1) }, finish: 113));
        blockedFinish.SelectAction(1); blockedFinish.Advance(1); blockedFinish.Advance(1);
        Check(blockedFinish.Outcome == DriveOutcome.Collision, "A collision takes precedence over finishing.");
        double stoppedTime = winner.Time; winner.Advance(1);
        Check(winner.Time == stoppedTime, "Terminal rounds must stay frozen.");
        var stalled = new DrivingGame(Straight()); stalled.SelectAction(7);
        for (int i = 0; i < 121; i++) stalled.Advance(1);
        Check(stalled.Outcome == DriveOutcome.Timeout, "A stopped model must eventually time out.");
        var frames = new DrivingGame(Straight()); frames.SelectAction(7);
        for (int i = 0; i < 4000; i++) frames.Advance(.03);
        Check(frames.Outcome == DriveOutcome.Timeout && frames.Time == 120, "Fractional animation steps must hit the timeout despite floating-point rounding.");
        for (int seed = 0; seed < 100; seed++)
        {
            var generated = new DrivingTrack(seed); var duplicate = new DrivingTrack(seed);
            Check(generated.Obstacles.Count == 12 && generated.Obstacles.SequenceEqual(duplicate.Obstacles) && generated.Polygon.SequenceEqual(duplicate.Polygon), "Seeds must reproduce complete tracks.");
            Check(generated.Obstacles.Max(c => c.Radius) - generated.Obstacles.Min(c => c.Radius) > 3, "Tracks need varied obstacle radii.");
            for (double x = generated.Start.X; x <= generated.FinishX; x += 2)
                Check(!generated.Collides(new Vec(x, generated.CenterY(x)), DrivingGame.CarRadius + 8), "Generated tracks must reserve a connected, traversable corridor.");
        }
        Check(!new DrivingTrack(1).Polygon.SequenceEqual(new DrivingTrack(2).Polygon), "Different seeds must change the track.");
        Console.WriteLine("PASS: driving raycasts, sensors, physics, contacts, finish, timeout, and 100 seeded traversable tracks.");
    }
}
