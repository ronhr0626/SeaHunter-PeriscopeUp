using System;
using System.Collections.Generic;

namespace SeaHunter.Game;

public class GameWorld
{
    // Virtual resolution: everything is drawn at 640x360 and scaled up to the window
    public const double Width = 640;
    public const double Height = 360;

    // Y positions measured on the background image
    public const double Horizon = 42;    // Where the sky meets the sea
    public const double WaterLine = 128; // Underwater starts here
    public const double SeaFloor = 335;

    // Torpedo rules
    public const int BaseTorpedoes = 5;        // Magazine size on patrol 1
    public const int MaxTorpedoCap = 12;       // Never more than this, however many patrols
    public const double ReloadTime = 2.5;    // Seconds to reload when empty
    public const double FireCooldown = 0.25; // Seconds between shots

    // Player rules
    public const int StartLives = 3;
    public const double InvulnerableTime = 2.0; // Seconds of safety after being hit

    // Bubble shield (Q): stops depth charges for a while. Earned by making it home.
    public const int RewardShields = 3;
    public const double ShieldTime = 5.0;       // Seconds each shield lasts

    // ===== Level rules: tweak these =====
    public const int PointsPerLevel = 1000;        // Score needed for each new level
    public const double DifficultyPerLevel = 0.2;  // +20% more ships and bombs per level
    public const double MaxDifficulty = 3.0;       // Never more than 3x harder than level 1
    public const double LevelUpBannerTime = 2.0;   // Seconds the "LEVEL X" text is shown
    // ====================================

    public Submarine Player { get; } = new() { X = 80, Y = 220 };
    public List<Torpedo> Torpedoes { get; } = new();
    public List<Ship> Ships { get; } = new();
    public List<DepthCharge> DepthCharges { get; } = new();
    public List<Explosion> Explosions { get; } = new();

    public int Score { get; private set; }
    public int Lives { get; private set; } = StartLives;
    public bool IsGameOver => Lives <= 0;

    // Level is calculated from score, so it can never get out of sync
    // Later patrols start on a higher level. Level still rises every PointsPerLevel from there.
    public int StartLevel { get; } = 1;
    public int Level => StartLevel + (Score - _startScore) / PointsPerLevel;

    // Levels actually played this patrol (gives periscope torpedoes)
    public int LevelsPlayed => Level - StartLevel + 1;

    private readonly int _startScore;

    // 1.0 at level 1, 1.2 at level 2, 1.4 at level 3 ... capped at MaxDifficulty
    public double Difficulty => Math.Min(MaxDifficulty, 1 + (Level - 1) * DifficultyPerLevel);

    public double LevelUpTimeLeft { get; private set; }

    // Magazine size: 5 on patrol 1, then +1 for every new patrol, up to 12
    public int MaxTorpedoes { get; private set; } = BaseTorpedoes;
    public int Ammo { get; private set; } = BaseTorpedoes;
    public bool IsReloading => Ammo == 0;

    // 0.0 = just started reloading, 1.0 = done
    public double ReloadProgress => IsReloading ? 1 - _reloadTimer / ReloadTime : 1;

    public int ShieldsLeft { get; private set; }
    public double ShieldTimeLeft { get; private set; }
    public bool IsShielded => ShieldTimeLeft > 0;

    public double InvulnerableTimeLeft { get; private set; }
    public bool IsInvulnerable => InvulnerableTimeLeft > 0;

    private readonly Random _rng = new();
    private double _reloadTimer;
    private double _cooldown;
    private double _spawnTimer = 1;

    public GameWorld() { }

    // New patrol after making it home: keeps the score (and so the level), lives are repaired
    public GameWorld(int startScore, int shields, int startLevel, int patrol = 1)
    {
        MaxTorpedoes = Math.Min(MaxTorpedoCap, BaseTorpedoes + (Math.Max(1, patrol) - 1));
        Ammo = MaxTorpedoes;
        Score = startScore;
        _startScore = startScore;
        ShieldsLeft = shields;
        StartLevel = startLevel;
        LevelUpTimeLeft = LevelUpBannerTime; // Show "LEVEL X" when the patrol starts
    }

    public void Update(double dt, InputState input)
    {
        // Let explosions finish after game over, but freeze everything else
        if (IsGameOver)
        {
            UpdateExplosions(dt);
            return;
        }

        LevelUpTimeLeft = Math.Max(0, LevelUpTimeLeft - dt);

        UpdatePlayer(dt, input);
        UpdateShips(dt);
        UpdateTorpedoes(dt);
        UpdateDepthCharges(dt);
        CheckHits();
        UpdateExplosions(dt);
    }

    private void UpdatePlayer(double dt, InputState input)
    {
        Player.Update(dt, input);
        Player.X = Math.Clamp(Player.X, 0, Width - Player.Width);
        Player.Y = Math.Clamp(Player.Y, WaterLine + 6, SeaFloor - Player.Height);

        InvulnerableTimeLeft = Math.Max(0, InvulnerableTimeLeft - dt);
        ShieldTimeLeft = Math.Max(0, ShieldTimeLeft - dt);

        // Q: pop a bubble shield (one at a time)
        if (input.ShieldPressed && ShieldsLeft > 0 && !IsShielded)
        {
            ShieldsLeft--;
            ShieldTimeLeft = ShieldTime;
        }

        _cooldown -= dt;
        if (input.Fire)
            TryFire();

        if (IsReloading)
        {
            _reloadTimer -= dt;
            if (_reloadTimer <= 0)
                Ammo = MaxTorpedoes;
        }
    }

    private void TryFire()
    {
        if (IsReloading || _cooldown > 0) return;

        // Launch from the middle of the submarine
        double x = Player.X + Player.Width / 2 - Torpedo.Width / 2;
        Torpedoes.Add(new Torpedo { X = x, Y = Player.Y - Torpedo.Height });

        Ammo--;
        _cooldown = FireCooldown;

        if (Ammo == 0)
            _reloadTimer = ReloadTime;
    }

    private void UpdateShips(double dt)
    {
        // Spawn a new ship every 1.5 to 3.5 seconds, faster on higher levels
        _spawnTimer -= dt;
        if (_spawnTimer <= 0)
        {
            var type = (ShipType)_rng.Next(3);
            bool movingRight = _rng.Next(2) == 0;
            var candidate = new Ship(type, movingRight);

            // Ships already in this lane decide the direction, so nobody sails through each other
            var laneShip = Ships.Find(s => s.Lane == candidate.Lane);
            if (laneShip != null && laneShip.MovingRight != movingRight)
                candidate = new Ship(type, laneShip.MovingRight);

            if (HasRoomToSpawn(candidate))
            {
                Ships.Add(candidate);
                _spawnTimer = (1.5 + _rng.NextDouble() * 2) / Difficulty;
            }
            else
            {
                // Lane is busy at the entry point: try again shortly
                _spawnTimer = 0.3;
            }
        }

        foreach (var ship in Ships)
        {
            ship.Update(dt);
            TryDropDepthCharge(ship, dt);
        }

        Ships.RemoveAll(s => s.IsOffScreen);
    }

    // True if the last ship in this lane has sailed at least one ship length away from the entry point
    private bool HasRoomToSpawn(Ship candidate)
    {
        foreach (var s in Ships)
        {
            if (s.Lane != candidate.Lane) continue;

            double gap = candidate.MovingRight
                ? s.X - (candidate.X + candidate.Width)   // Entering from the left: space to the ship ahead
                : candidate.X - (s.X + s.Width);          // Entering from the right

            if (gap < s.Width) return false;
        }
        return true;
    }

    private void TryDropDepthCharge(Ship ship, double dt)
    {
        // Unarmed ships and ships outside the screen never drop
        if (ship.DropInterval <= 0 || !ship.IsOnScreen) return;

        ship.DropTimer -= dt;
        if (ship.DropTimer > 0) return;

        DepthCharges.Add(new DepthCharge
        {
            X = ship.X + ship.Width / 2 - DepthCharge.Size / 2,
            Y = WaterLine,
            // Sink speed grows at half the rate of difficulty, so it stays dodgeable
            SinkSpeed = DepthCharge.BaseSinkSpeed * (1 + (Difficulty - 1) * 0.5)
        });

        // Some randomness so drops don't feel robotic, and more often on higher levels
        ship.DropTimer = ship.DropInterval * (0.7 + _rng.NextDouble() * 0.6) / Difficulty;
    }

    private void UpdateTorpedoes(double dt)
    {
        foreach (var torpedo in Torpedoes)
            torpedo.Update(dt);

        // Missed everything and passed the horizon
        Torpedoes.RemoveAll(t => t.Y + Torpedo.Height < Horizon);
    }

    private void UpdateDepthCharges(double dt)
    {
        // Hitbox is a bit smaller than the sprite, so near misses feel fair
        double hitX = Player.X + Player.Width * 0.15;
        double hitY = Player.Y + Player.Height * 0.2;
        double hitWidth = Player.Width * 0.7;
        double hitHeight = Player.Height * 0.6;

        for (int i = DepthCharges.Count - 1; i >= 0; i--)
        {
            var c = DepthCharges[i];
            c.Update(dt);

            // Reached the sea floor: explode harmlessly
            if (c.Y + DepthCharge.Size >= SeaFloor)
            {
                Explosions.Add(new Explosion { X = c.X + DepthCharge.Size / 2, Y = SeaFloor - 4 });
                DepthCharges.RemoveAt(i);
                continue;
            }

            bool hitsPlayer =
                c.X < hitX + hitWidth && c.X + DepthCharge.Size > hitX &&
                c.Y < hitY + hitHeight && c.Y + DepthCharge.Size > hitY;

            // The bubble shield pops the charge without any damage
            if (hitsPlayer && IsShielded)
            {
                Explosions.Add(new Explosion
                {
                    X = c.X + DepthCharge.Size / 2,
                    Y = c.Y + DepthCharge.Size / 2
                });
                DepthCharges.RemoveAt(i);
                continue;
            }

            if (hitsPlayer && !IsInvulnerable)
            {
                Explosions.Add(new Explosion
                {
                    X = c.X + DepthCharge.Size / 2,
                    Y = c.Y + DepthCharge.Size / 2
                });
                DepthCharges.RemoveAt(i);

                Lives--;
                InvulnerableTimeLeft = InvulnerableTime;
            }
        }
    }

    private void CheckHits()
    {
        // Loop backwards so we can remove torpedoes while looping
        for (int i = Torpedoes.Count - 1; i >= 0; i--)
        {
            var t = Torpedoes[i];
            var hit = Ships.Find(s => s.Overlaps(t.X, t.Y, Torpedo.Width, Torpedo.Height));
            if (hit == null) continue;

            Torpedoes.RemoveAt(i);
            Explosions.Add(new Explosion
            {
                X = t.X + Torpedo.Width / 2,
                Y = hit.Y + hit.Height / 2
            });

            // Battleships survive the first hit
            if (hit.TakeHit())
            {
                int levelBefore = Level;
                Score += hit.Points;
                Ships.Remove(hit);

                // Show the banner when the score pushes us to a new level
                if (Level > levelBefore)
                    LevelUpTimeLeft = LevelUpBannerTime;
            }
        }
    }

    private void UpdateExplosions(double dt)
    {
        foreach (var explosion in Explosions)
            explosion.Update(dt);

        Explosions.RemoveAll(e => e.IsDone);
    }
}