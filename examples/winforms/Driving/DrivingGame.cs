using System.Globalization;

namespace Eugeniusz.Samples.Driving;

public readonly record struct Vec(double X, double Y)
{
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator *(Vec a, double b) => new(a.X * b, a.Y * b);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public static double Dot(Vec a, Vec b) => a.X * b.X + a.Y * b.Y;
    public static double Cross(Vec a, Vec b) => a.X * b.Y - a.Y * b.X;
    public static Vec Direction(double angle) => new(Math.Cos(angle), Math.Sin(angle));
}

public readonly record struct Circle(Vec Center, double Radius);
public readonly record struct Segment(Vec A, Vec B);
public readonly record struct Sensor(string Name, double Angle, double Distance, Vec End);
public enum DriveOutcome { Running, Collision, Finished, Timeout }

/// <summary>A seeded, open winding course with a reserved, collision-free center corridor.</summary>
public sealed class DrivingTrack
{
    public const double WorldWidth = 960, WorldHeight = 540, HalfWidth = 82;
    public IReadOnlyList<Vec> Center { get; }
    public IReadOnlyList<Vec> Polygon { get; }
    public IReadOnlyList<Circle> Obstacles { get; }
    public IReadOnlyList<Segment> Walls { get; }
    public Vec Start { get; }
    public double StartHeading { get; }
    public double FinishX { get; }
    public int Seed { get; }

    public DrivingTrack(int seed)
    {
        Seed = seed;
        var random = new Random(seed);
        double phase = random.NextDouble() * Math.PI * 2, amplitude = 38 + random.NextDouble() * 24;
        var center = Enumerable.Range(0, 31).Select(i => {
            double x = 30 + i * 30;
            return new Vec(x, 270 + amplitude * Math.Sin((x - 30) / 900 * Math.PI * 2 + phase));
        }).ToArray();
        Center = Array.AsReadOnly(center);
        var upper = center.Select(p => new Vec(p.X, p.Y - HalfWidth)).ToArray();
        var lower = center.Select(p => new Vec(p.X, p.Y + HalfWidth)).ToArray();
        var polygon = upper.Concat(lower.Reverse()).ToArray();
        Polygon = Array.AsReadOnly(polygon);
        Walls = Array.AsReadOnly(polygon.Select((p, i) => new Segment(p, polygon[(i + 1) % polygon.Length])).ToArray());
        Start = new Vec(65, CenterY(65));
        StartHeading = Math.Atan2(center[2].Y - center[1].Y, 30);
        FinishX = 895;
        var obstacles = new List<Circle>();
        // Reserve a tube around every center-line segment, including the car's collision radius.
        // The generator uses geometry only; it never steers the model-controlled vehicle.
        for (int attempt = 0; attempt < 1000 && obstacles.Count < 12; attempt++)
        {
            double x = 160 + random.NextDouble() * 650, radius = 9 + random.NextDouble() * 17;
            double offset = (random.Next(2) == 0 ? -1 : 1) * (39 + radius + random.NextDouble() * 6);
            var obstacle = new Circle(new Vec(x, CenterY(x) + offset), radius);
            if (Walls.Any(w => DistanceToSegment(obstacle.Center, w) <= radius + 3)) continue;
            if (Enumerable.Range(0, center.Length - 1).Any(i => DistanceToSegment(obstacle.Center, new Segment(center[i], center[i + 1])) < radius + DrivingGame.CarRadius + 15)) continue;
            if (obstacles.Any(o => (o.Center - obstacle.Center).Length < o.Radius + radius + 18)) continue;
            obstacles.Add(obstacle);
        }
        Obstacles = obstacles.AsReadOnly();
    }

    // Explicit geometry is useful for deterministic sensor/physics tests without any native runtime.
    public DrivingTrack(Vec[] polygon, Circle[] obstacles, Vec start, double heading, double finishX)
    {
        Polygon = Array.AsReadOnly((Vec[])polygon.Clone()); Obstacles = Array.AsReadOnly((Circle[])obstacles.Clone());
        Walls = Array.AsReadOnly(polygon.Select((p, i) => new Segment(p, polygon[(i + 1) % polygon.Length])).ToArray());
        Center = Array.AsReadOnly(new[] { start, new Vec(finishX, start.Y) });
        Start = start; StartHeading = heading; FinishX = finishX;
    }

    public double CenterY(double x)
    {
        for (int i = 1; i < Center.Count; i++)
            if (x <= Center[i].X) return Center[i - 1].Y + (Center[i].Y - Center[i - 1].Y) * Math.Clamp((x - Center[i - 1].X) / (Center[i].X - Center[i - 1].X), 0, 1);
        return Center[^1].Y;
    }

    public bool Contains(Vec point)
    {
        bool inside = false;
        for (int i = 0, j = Polygon.Count - 1; i < Polygon.Count; j = i++)
        {
            Vec a = Polygon[i], b = Polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    public bool Collides(Vec point, double radius) => !Contains(point) || Walls.Any(w => DistanceToSegment(point, w) <= radius)
        || Obstacles.Any(o => (point - o.Center).Length <= radius + o.Radius);

    public static double DistanceToSegment(Vec point, Segment segment)
    {
        Vec edge = segment.B - segment.A;
        double lengthSquared = Vec.Dot(edge, edge);
        return (point - (segment.A + edge * (lengthSquared == 0 ? 0 : Math.Clamp(Vec.Dot(point - segment.A, edge) / lengthSquared, 0, 1)))).Length;
    }

    public double Raycast(Vec origin, double angle, double range)
    {
        if (!Contains(origin) || Obstacles.Any(o => (origin - o.Center).Length <= o.Radius)) return 0;
        Vec direction = Vec.Direction(angle); double nearest = range;
        foreach (Segment wall in Walls)
        {
            Vec edge = wall.B - wall.A, relative = wall.A - origin;
            double cross = Vec.Cross(direction, edge);
            if (Math.Abs(cross) < 1e-10) continue;
            double distance = Vec.Cross(relative, edge) / cross, along = Vec.Cross(relative, direction) / cross;
            if (distance >= 0 && along >= 0 && along <= 1) nearest = Math.Min(nearest, distance);
        }
        foreach (Circle obstacle in Obstacles)
        {
            Vec relative = origin - obstacle.Center;
            double b = Vec.Dot(relative, direction), discriminant = b * b - Vec.Dot(relative, relative) + obstacle.Radius * obstacle.Radius;
            if (discriminant < 0) continue;
            double distance = -b - Math.Sqrt(discriminant);
            if (distance >= 0) nearest = Math.Min(nearest, distance);
        }
        return nearest;
    }
}

public sealed class DrivingGame
{
    public const double CarRadius = 13, SensorRange = 230, DecisionSeconds = .3, MaximumSpeed = 60;
    public static readonly string[] Actions = {
        "Accelerate and steer LEFT", "Accelerate and go STRAIGHT", "Accelerate and steer RIGHT",
        "Coast and steer LEFT", "Coast and go STRAIGHT", "Coast and steer RIGHT",
        "Brake and steer LEFT", "Brake and go STRAIGHT", "Brake and steer RIGHT"
    };
    public const string SystemPrompt = """
        You drive a car. Choose steering or pedal using the supplied measurements and comparisons. Steer toward more open space to avoid obstacles. Maintain moderate speed, brake for danger ahead, and accelerate if safe but below target speed.
        """;
    public static readonly string[] Pedals = { "ACCELERATE", "COAST", "BRAKE" };
    public static readonly string[] Steering = { "Steer LEFT", "Go STRAIGHT", "Steer RIGHT" };
    public const string SteeringQuestion = "Choose steering toward the more open diagonal side. If the diagonals are BALANCED, go straight.";
    public const string PedalQuestion = "Choose pedal. If NOT ENOUGH room to stop or ABOVE target speed, brake. Otherwise if BELOW target, accelerate. Otherwise coast.";
    public DrivingTrack Track { get; }
    public Vec Position { get; private set; }
    public double Heading { get; private set; }
    public double Speed { get; private set; }
    public double Time { get; private set; }
    public int Decisions { get; private set; }
    public int Action { get; private set; } = 4;
    public DriveOutcome Outcome { get; private set; }
    public bool Terminal => Outcome != DriveOutcome.Running;
    public double Progress => Outcome == DriveOutcome.Finished ? 1 : Math.Clamp((Position.X - Track.Start.X) / (Track.FinishX - Track.Start.X), 0, 1);
    public DrivingGame(DrivingTrack track) { Track = track; Position = track.Start; Heading = track.StartHeading; }

    public Sensor[] Sensors()
    {
        string[] names = { "LEFT 60", "LEFT 30", "FRONT", "RIGHT 30", "RIGHT 60" };
        return Enumerable.Range(0, 5).Select(i => {
            double angle = Heading + (i - 2) * Math.PI / 6;
            double distance = Track.Raycast(Position, angle, SensorRange);
            return new Sensor(names[i], angle, distance, Position + Vec.Direction(angle) * distance);
        }).ToArray();
    }

    public string PromptState()
    {
        var rays = Sensors(); double left = rays[1].Distance, right = rays[3].Distance;
        // These are summaries of observations, not steering commands. Both controls still come from inference.
        string comparison = Math.Max(left, right) <= 1.3 * Math.Min(left, right) ? "BALANCED" : left > right ? "LEFT has more space" : "RIGHT has more space";
        string speedBand = Speed < 32 ? "BELOW target" : Speed > 38 ? "ABOVE target" : "NEAR target";
        string stopping = rays[2].Distance > Speed * Speed / 80 + 25 ? "ENOUGH room to stop" : "NOT ENOUGH room to stop";
        return string.Join("; ", rays.Select(s => string.Create(CultureInfo.InvariantCulture, $"{s.Name}: {s.Distance:F0}")))
            + string.Create(CultureInfo.InvariantCulture, $".\nDiagonal comparison: {comparison}.\nSpeed: {Speed:F0}, {speedBand}.\nFront stopping clearance: {stopping}.");
    }

    public void SelectAction(int action)
    {
        if (action < 0 || action >= Actions.Length) throw new ArgumentOutOfRangeException(nameof(action));
        if (Terminal) return;
        Action = action; Decisions++;
    }

    public void Advance(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0 || seconds > 1) throw new ArgumentOutOfRangeException(nameof(seconds));
        // At maximum speed each integration step moves <= 0.5 units. Fast cars cannot jump a small obstacle.
        int steps = (int)Math.Ceiling(seconds * 120); double dt = seconds / steps;
        for (int i = 0; i < steps && !Terminal; i++)
        {
            Time += dt;
            Speed = Math.Clamp(Speed + (Action / 3 == 0 ? 20 : Action / 3 == 1 ? -2 : -40) * dt, 0, MaximumSpeed);
            int steering = Action % 3 - 1;
            Heading = Math.IEEERemainder(Heading + Speed / 28 * Math.Tan(steering * .32) * dt, Math.PI * 2);
            Position += Vec.Direction(Heading) * (Speed * dt);
            if (Track.Collides(Position, CarRadius)) { Outcome = DriveOutcome.Collision; Speed = 0; }
            else if (Position.X >= Track.FinishX) { Outcome = DriveOutcome.Finished; Speed = 0; }
            else if (Time >= 120 - 1e-9) { Outcome = DriveOutcome.Timeout; Time = 120; Speed = 0; }
        }
    }
}
