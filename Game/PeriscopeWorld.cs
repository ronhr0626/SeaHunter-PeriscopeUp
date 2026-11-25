using System;
using System.Collections.Generic;

namespace SeaHunter.Game;

// A ship seen through the periscope. Position is bearing (degrees, 0-360) and range (meters).
public class PeriShip
{
    public ShipType Type { get; init; }
    public double Bearing { get; set; }
    public double Range { get; set; }
    public bool IsHunter { get; init; }        // Destroyer that comes straight for us
    public double ApproachSpeed { get; init; } // Meters per second towards us (hunters only)
    public double SpeedMs { get; init; }       // Meters per second, sideways across our view
    public bool MovingRight { get; init; }     // Right = bearing increases
    public double LengthM { get; init; }
    public double Aspect { get; init; }        // Sprite height / width
    public int Points { get; init; }
    public double Travelled { get; set; }      // Degrees sailed since spawn

    // How many degrees per second the ship moves across the view
    public double AngularSpeed => (MovingRight ? 1 : -1) * SpeedMs / Range * (180 / Math.PI);

    // How wide the ship looks, in degrees
    public double AngularWidth => LengthM / Range * (180 / Math.PI);

    // Speed in knots, for the target display
    public double Knots => SpeedMs * 1.944;
}

public class PeriTorpedo
{
    public double Bearing { get; init; }
    public double Range { get; set; }
}

public class PeriExplosion
{
    public const double Duration = 1.2;
    public double Bearing { get; init; }
    public double Range { get; init; }
    public double Age { get; set; }
    public double Progress => Age / Duration;
    public bool IsDone => Age >= Duration;
}

public class PeriscopeWorld
{
    // ===== Tweak these =====
    public const double FieldOfView = 40;        // Degrees visible across the screen
    public const double TurnSpeed = 45;          // Periscope turn speed at first, degrees per second
    public const double MaxTurnSpeed = 200;      // Top speed when the turn key is held
    public const double TurnAcceleration = 150;  // Degrees per second gained each second while held
    public const double TrackSpeed = 220;        // Auto-swing speed towards a destroyer (Q / T)
    public const double ArcHalf = 90;            // Periscope covers 90 degrees left and right of bow (180 total)
    public const double SpawnHalf = 80;          // Ships appear inside this, so they start in view range
    public const int TorpedoesPerLevel = 3;      // Torpedoes earned per level reached in the side view
    public const int MinTorpedoes = 3;           // Never fewer than this
    public const int TubeCount = 3;
    public const double TubeReloadTime = 3.0;    // Seconds per tube
    public const double TorpedoSpeed = 450;      // Meters per second (game speed, not real)
    public const double TorpedoMaxRange = 5000;
    public const double MinShipRange = 1200;
    public const double MaxShipRange = 4500;
    public const int MaxShips = 4;
    public const double ShipLifeArc = 120;       // Degrees a ship sails before it is gone
    public const double HunterInterval = 25;     // Seconds between each destroyer
    public const double HunterStartRange = 4800;
    public const double HunterSpeed = 130;       // Meters per second towards us
    public const double HunterKillRange = 350;   // Closer than this and we are sunk
    public const int HunterPoints = 500;
    public const double SunkDelay = 2.0;         // Seconds of red flash before the round ends
    // =======================

    public double Heading { get; private set; }  // Where the periscope points, 0-360
    public int TorpedoesInStock { get; private set; }
    public int Score { get; private set; }

    public List<PeriShip> Ships { get; } = new();
    public List<PeriTorpedo> Torpedoes { get; } = new();
    public List<PeriExplosion> Explosions { get; } = new();

    // Time left until each tube is loaded. 0 = ready, -1 = empty (no stock left).
    public double[] Tubes { get; } = new double[TubeCount];

    // True after a destroyer reached us
    public bool IsSunk { get; private set; }
    public double SunkTimeLeft { get; private set; }

    // The round only ends when a destroyer gets us
    public bool IsOver => IsSunk && SunkTimeLeft <= 0;

    // No torpedoes left anywhere: stock, tubes and water
    public bool IsOutOfTorpedoes =>
        TorpedoesInStock == 0 && Torpedoes.Count == 0 && Array.TrueForAll(Tubes, t => t < 0);

    // The closest destroyer, or null. Used for warnings.
    public PeriShip? ClosestHunter()
    {
        PeriShip? best = null;
        foreach (var s in Ships)
            if (s.IsHunter && (best == null || s.Range < best.Range))
                best = s;
        return best;
    }

    private readonly Random _rng = new();
    private double _spawnTimer;
    private double _turnSpeed = TurnSpeed;

    private double _hunterTimer = HunterInterval / 2; // First destroyer comes a bit earlier

    public PeriscopeWorld(int torpedoes, int startScore)
    {
        TorpedoesInStock = Math.Max(MinTorpedoes, torpedoes);
        Score = startScore;

        // Load the tubes from the stock
        for (int i = 0; i < TubeCount; i++)
            Tubes[i] = TakeFromStock() ? 0 : -1;

        // Start with a couple of ships, one of them roughly ahead
        SpawnShip(_rng.NextDouble() * 30 - 15);
        SpawnShip(null);
    }

    private bool TakeFromStock()
    {
        if (TorpedoesInStock <= 0) return false;
        TorpedoesInStock--;
        return true;
    }

    public void Update(double dt, InputState input)
    {
        if (IsSunk)
        {
            // Frozen: only the explosion and red flash play out
            SunkTimeLeft = Math.Max(0, SunkTimeLeft - dt);
            foreach (var e in Explosions) e.Age += dt;
            Explosions.RemoveAll(e => e.IsDone);
            return;
        }

        UpdateTurning(dt, input);

        if (input.Fire)
            TryFire();

        UpdateTubes(dt);
        UpdateShips(dt);
        UpdateTorpedoes(dt);

        foreach (var e in Explosions) e.Age += dt;
        Explosions.RemoveAll(e => e.IsDone);
    }

    private void UpdateTurning(double dt, InputState input)
    {
        // Q / T: swing the shortest way to the closest destroyer and stop on it
        var hunter = ClosestHunter();
        if (input.Track && hunter != null)
        {
            double diff = Delta(hunter.Bearing, Heading);
            double step = TrackSpeed * dt;
            Heading = ClampToArc(Heading + Math.Clamp(diff, -step, step));
            return;
        }

        // A / D: starts slow for fine aiming, speeds up the longer the key is held
        int dir = (input.Right ? 1 : 0) - (input.Left ? 1 : 0);
        if (dir == 0)
        {
            _turnSpeed = TurnSpeed;
            return;
        }

        _turnSpeed = Math.Min(MaxTurnSpeed, _turnSpeed + TurnAcceleration * dt);
        Heading = ClampToArc(Heading + dir * _turnSpeed * dt);
    }

    // Keeps a bearing inside the front half circle (bow +- ArcHalf)
    public static double ClampToArc(double bearing) =>
        Normalize(Math.Clamp(Delta(bearing, 0), -ArcHalf, ArcHalf));

    // Random bearing inside the spawn arc
    private double RandomFrontBearing() => Normalize((_rng.NextDouble() * 2 - 1) * SpawnHalf);

    private void TryFire()
    {
        for (int i = 0; i < TubeCount; i++)
        {
            if (Tubes[i] != 0) continue;

            Torpedoes.Add(new PeriTorpedo { Bearing = Heading, Range = 0 });
            Tubes[i] = TakeFromStock() ? TubeReloadTime : -1;
            return;
        }
    }

    private void UpdateTubes(double dt)
    {
        for (int i = 0; i < TubeCount; i++)
            if (Tubes[i] > 0)
                Tubes[i] = Math.Max(0, Tubes[i] - dt);
    }

    private void UpdateShips(double dt)
    {
        _spawnTimer -= dt;
        if (_spawnTimer <= 0 && Ships.Count < MaxShips)
        {
            SpawnShip(null);
            _spawnTimer = 3 + _rng.NextDouble() * 4;
        }

        // Out of torpedoes and no destroyer coming: send one right away
        if (IsOutOfTorpedoes && ClosestHunter() == null)
            _hunterTimer = 0;

        _hunterTimer -= dt;
        if (_hunterTimer <= 0)
        {
            SpawnHunter();
            _hunterTimer = HunterInterval * (0.8 + _rng.NextDouble() * 0.4);
        }

        foreach (var s in Ships)
        {
            double move = s.AngularSpeed * dt;
            s.Bearing = Normalize(s.Bearing + move);
            s.Travelled += Math.Abs(move);

            if (s.IsHunter)
                s.Range -= s.ApproachSpeed * dt;
        }

        // A destroyer got too close: we are sunk
        var killer = Ships.Find(s => s.IsHunter && s.Range <= HunterKillRange);
        if (killer != null)
        {
            IsSunk = true;
            SunkTimeLeft = SunkDelay;
            Torpedoes.Clear();
            Explosions.Add(new PeriExplosion { Bearing = killer.Bearing, Range = HunterKillRange });
            return;
        }

        // Ships that sailed far enough, or out of the front half circle, are gone
        Ships.RemoveAll(s => !s.IsHunter &&
            (s.Travelled > ShipLifeArc || Math.Abs(Delta(s.Bearing, 0)) > ArcHalf + 5));
    }

    // Destroyer from a random direction, heading straight for us with a little sideways drift
    private void SpawnHunter()
    {
        Ships.Add(new PeriShip
        {
            Type = ShipType.PatrolBoat,
            IsHunter = true,
            Bearing = RandomFrontBearing(),
            Range = HunterStartRange,
            ApproachSpeed = HunterSpeed,
            SpeedMs = 3 + _rng.NextDouble() * 3,
            MovingRight = _rng.Next(2) == 0,
            LengthM = 40,          // Seen from the front: only the beam and bow wave are visible
            Aspect = 0,            // Taken from destroyer.png when drawn
            Points = HunterPoints
        });
    }

    // aheadOffset: spawn this many degrees from the current heading, or null for anywhere
    private void SpawnShip(double? aheadOffset)
    {
        var type = (ShipType)_rng.Next(3);
        var (length, aspect, speed, points) = type switch
        {
            ShipType.PatrolBoat => (60.0, 14.0 / 50, 63.0, 300),
            ShipType.Freighter => (140.0, 22.0 / 90, 28.0, 150),
            _ => (220.0, 28.0 / 120, 39.0, 200)
        };

        double bearing = aheadOffset.HasValue
            ? Heading + aheadOffset.Value
            : RandomFrontBearing();

        double range = MinShipRange + _rng.NextDouble() * (MaxShipRange - MinShipRange);

        // Don't put two ships at almost the same range and bearing
        foreach (var other in Ships)
            if (Math.Abs(other.Range - range) < 300 && Math.Abs(Delta(other.Bearing, bearing)) < 15)
                return;

        Ships.Add(new PeriShip
        {
            Type = type,
            Bearing = Normalize(bearing),
            Range = range,
            SpeedMs = speed * (0.8 + _rng.NextDouble() * 0.4),
            MovingRight = _rng.Next(2) == 0,
            LengthM = length,
            Aspect = aspect,
            Points = points
        });
    }

    private void UpdateTorpedoes(double dt)
    {
        for (int i = Torpedoes.Count - 1; i >= 0; i--)
        {
            var t = Torpedoes[i];
            double before = t.Range;
            t.Range += TorpedoSpeed * dt;

            // Hit if the torpedo passed the ship's range this frame while inside its width
            var hit = Ships.Find(s =>
                before < s.Range && t.Range >= s.Range &&
                Math.Abs(Delta(s.Bearing, t.Bearing)) <= s.AngularWidth / 2);

            if (hit != null)
            {
                // Further away = harder = more points
                Score += (int)(hit.Points * hit.Range / 1000);
                Explosions.Add(new PeriExplosion { Bearing = hit.Bearing, Range = hit.Range });
                Ships.Remove(hit);
                Torpedoes.RemoveAt(i);
                continue;
            }

            if (t.Range > TorpedoMaxRange)
                Torpedoes.RemoveAt(i);
        }
    }

    // The ship closest to the crosshair, used for the target display
    public PeriShip? TargetInSight()
    {
        PeriShip? best = null;
        double bestDiff = FieldOfView / 2;
        foreach (var s in Ships)
        {
            double diff = Math.Abs(Delta(s.Bearing, Heading));
            if (diff < bestDiff) { best = s; bestDiff = diff; }
        }
        return best;
    }

    public static double Normalize(double deg) => ((deg % 360) + 360) % 360;

    // Shortest signed difference a - b, in -180..180
    public static double Delta(double a, double b)
    {
        double d = Normalize(a - b);
        return d > 180 ? d - 360 : d;
    }
}