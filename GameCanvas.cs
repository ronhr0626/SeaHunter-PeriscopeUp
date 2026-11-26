using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SeaHunter.Game;

namespace SeaHunter;

// Draws the game and runs the game loop. Menus are handled by MainWindow.
public class GameCanvas : Control
{
    private static readonly IBrush TorpedoBrush = new SolidColorBrush(Color.Parse("#D8DEE9"));
    private static readonly IBrush ChargeBrush = new SolidColorBrush(Color.Parse("#1C1F26"));
    private static readonly IBrush ChargeBandBrush = new SolidColorBrush(Color.Parse("#E0A030"));
    private static readonly IBrush AmmoFullBrush = new SolidColorBrush(Color.Parse("#FFD34D"));
    private static readonly IBrush AmmoEmptyBrush = new SolidColorBrush(Color.Parse("#40FFFFFF"));
    private static readonly IBrush HudBackBrush = new SolidColorBrush(Color.Parse("#60000000"));

    private static readonly Typeface HudFont = new("Consolas", FontStyle.Normal, FontWeight.Bold);

    private static readonly IBrush WakeHeadBrush = new SolidColorBrush(Color.Parse("#F0FFFFFF"));
    private static readonly IBrush RadarBlipBrush = new SolidColorBrush(Color.Parse("#60FF60"));
    private static readonly IBrush RadarTextBrush = new SolidColorBrush(Color.Parse("#80FF80"));
    private static readonly IPen RadarSweepPen = new Pen(new SolidColorBrush(Color.Parse("#A060FF60")), 1);
    private static readonly IPen RadarFovPen = new Pen(new SolidColorBrush(Color.Parse("#5060FF60")), 0.7);
    private static readonly IBrush LampOffBrush = new SolidColorBrush(Color.Parse("#B0000000"));
    private static readonly IBrush HunterBrush = new SolidColorBrush(Color.Parse("#FF3030"));
    private static readonly IBrush WarningTextBrush = new SolidColorBrush(Color.Parse("#FF5050"));
    private double _blink; // Seconds, for blinking warnings

    // Periscope screen layout, measured on Periscope.png and scaled to 640x360
    private const double PeriHorizon = 154;        // Crosshair line = horizon
    private const double PeriCenterX = 320;
    private const double PeriLensBottom = 286;
    private const double RangeDrop = 40000;        // Waterline y = horizon + RangeDrop / range
    private const double PixelsPerDegree = GameWorld.Width / PeriscopeWorld.FieldOfView;
    private const double SeaTileWidth = 720;       // 360 degrees = 8 tiles, so the panorama wraps cleanly
    private const double SeaTileHeight = 532.7;
    private const double SeaTileY = -127.3;        // Puts the photo's horizon on the crosshair
    private const double RadarX = 318, RadarY = 330, RadarRadius = 90;

    private GameWorld _world = new(); // Replaced every time a new game starts
    private PeriscopeWorld? _periscope; // Set when the periscope round starts
    private MinefieldWorld? _minefield; // Set when the minefield stage starts
    private readonly InputState _input = new();
    private readonly Bitmap _background;
    private readonly Bitmap _subSprite;
    private readonly Bitmap _patrolBoatSprite;
    private readonly Bitmap _freighterSprite;
    private readonly Bitmap _battleshipSprite;
    private readonly Bitmap _periscopeFrame;
    private readonly Bitmap _periscopeSea;
    private readonly Bitmap _destroyerSprite;
    private readonly Bitmap _splashSprite;
    private readonly Bitmap _warningGlow;
    private readonly Bitmap _mineBackground;
    private readonly Bitmap _damagedSub;
    private readonly Bitmap _harbor;
    private readonly Bitmap _mineSprite;
    private readonly Bitmap _mineBlastSprite;
    private double _radarSweep; // Degrees, just for the look
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private TopLevel? _topLevel;

    // True while a game is being played (not in menus)
    public bool IsRunning { get; private set; }

    // True while the periscope round is shown
    public bool InPeriscope { get; private set; }

    // True while the minefield (limping home) stage is shown
    public bool InMinefield { get; private set; }

    // Destroyers sunk in the last periscope round (gives sonar pings in the minefield)
    public int DestroyersSunk => _periscope?.DestroyersSunk ?? 0;

    // Raised once when the minefield ends: (final score, true = made it home)
    public event Action<int, bool>? MinefieldOver;

    // Raised once when the side view game ends: (score, level reached)
    public event Action<int, int>? SideViewOver;

    // Raised once when the periscope round ends. The int is the final total score.
    public event Action<int>? GameOver;

    public GameCanvas()
    {
        _background = LoadImage("BackgroundSea.png");
        _subSprite = LoadImage("sub.png");
        _patrolBoatSprite = LoadImage("1.png");
        _freighterSprite = LoadImage("2.png");
        _battleshipSprite = LoadImage("3.png");
        _periscopeFrame = LoadImage("Periscope.png");
        _periscopeSea = LoadImage("PeriscopeSea.png");
        _destroyerSprite = LoadImage("destroyer.png");
        _splashSprite = LoadImage("splash.png");
        _warningGlow = LoadImage("warning_glow.png");
        _mineBackground = LoadImage("backgroundMine.png");
        _damagedSub = LoadImage("damageSub.png");
        _harbor = LoadImage("harbor.png");
        _mineSprite = LoadImage("mine.png");
        _mineBlastSprite = LoadImage("Minexploding.png");

        // Keep pixel art sharp when scaled up
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);

        // ~60 frames per second
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => GameLoop();
    }

    private static Bitmap LoadImage(string fileName) =>
        new(AssetLoader.Open(new Uri($"avares://SeaHunter/Assets/{fileName}")));

    public void StartNewGame()
    {
        _world = new GameWorld();
        _periscope = null;
        _minefield = null;
        InPeriscope = false;
        InMinefield = false;
        IsRunning = true;
    }

    // Starts the periscope round with the score from the side view game
    public void StartPeriscope(int torpedoes)
    {
        _periscope = new PeriscopeWorld(torpedoes, _world.Score);
        InPeriscope = true;
        IsRunning = true;
    }

    // Starts the minefield stage with the score and destroyers from the periscope round
    public void StartMinefield()
    {
        int score = _periscope?.Score ?? _world.Score;
        _minefield = new MinefieldWorld(DestroyersSunk, score);
        InPeriscope = false;
        InMinefield = true;
        IsRunning = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Listen for keys on the whole window, not just this control
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(KeyDownEvent, HandleKeyDown, RoutingStrategies.Tunnel);
        _topLevel?.AddHandler(KeyUpEvent, HandleKeyUp, RoutingStrategies.Tunnel);

        _clock.Start();
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        _topLevel?.RemoveHandler(KeyDownEvent, HandleKeyDown);
        _topLevel?.RemoveHandler(KeyUpEvent, HandleKeyUp);
        base.OnDetachedFromVisualTree(e);
    }

    private void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        // In menus, let keys reach the buttons (Enter, Tab, Space)
        if (!IsRunning) return;

        _input.Press(e.PhysicalKey);
        e.Handled = true; // Stop Space etc. from clicking hidden buttons
    }

    // Always track releases, so no key gets "stuck" when switching screens
    private void HandleKeyUp(object? sender, KeyEventArgs e) => _input.Release(e.PhysicalKey);

    private void GameLoop()
    {
        // Time since last frame in seconds
        double dt = _clock.Elapsed.TotalSeconds;
        _clock.Restart();
        dt = Math.Min(dt, 0.05); // Avoid huge jumps if the window freezes

        _radarSweep = (_radarSweep + 120 * dt) % 360;
        _blink += dt;

        if (InMinefield && _minefield != null)
        {
            if (IsRunning)
                _minefield.Update(dt, _input);

            if (IsRunning && _minefield.IsOver)
            {
                IsRunning = false;
                MinefieldOver?.Invoke(_minefield.Score, _minefield.IsHome);
            }
        }
        else if (InPeriscope && _periscope != null)
        {
            if (IsRunning)
                _periscope.Update(dt, _input);

            // Tell the window exactly once that the periscope round is over
            if (IsRunning && _periscope.IsOver)
            {
                IsRunning = false;
                GameOver?.Invoke(_periscope.Score);
            }
        }
        else
        {
            // Update while playing, and after game over so the last explosions finish
            if (IsRunning || _world.IsGameOver)
                _world.Update(dt, _input);

            // Tell the window exactly once that the side view game just ended
            if (IsRunning && _world.IsGameOver)
            {
                IsRunning = false;
                SideViewOver?.Invoke(_world.Score, _world.Level);
            }
        }

        _input.EndFrame(); // Forget one-shot presses so they only count once

        InvalidateVisual(); // Ask Avalonia to call Render() again
    }

    public override void Render(DrawingContext context)
    {
        // Black bars around the game if the window has a different shape
        context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));

        // Scale the 640x360 world to fit the window, centered
        double scale = Math.Min(Bounds.Width / GameWorld.Width, Bounds.Height / GameWorld.Height);
        double offsetX = (Bounds.Width - GameWorld.Width * scale) / 2;
        double offsetY = (Bounds.Height - GameWorld.Height * scale) / 2;

        var transform = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY);

        // Everything inside this block is drawn in world coordinates (0..640, 0..360)
        using (context.PushTransform(transform))
        // Clip: anything outside the game world (like ships entering) is hidden in the black bars
        using (context.PushClip(new Rect(0, 0, GameWorld.Width, GameWorld.Height)))
        {
            if (InMinefield && _minefield != null)
            {
                RenderMinefield(context, _minefield);
                return;
            }

            if (InPeriscope && _periscope != null)
            {
                RenderPeriscope(context, _periscope);
                return;
            }

            context.DrawImage(_background, new Rect(0, 0, GameWorld.Width, GameWorld.Height));
            DrawShips(context);
            DrawDepthCharges(context);
            DrawTorpedoes(context);
            DrawSubmarine(context);
            DrawExplosions(context);

            if (IsRunning)
                DrawHud(context);
        }
    }

    // Draws a sprite that faces right, mirrored when facingRight is false
    private static void DrawSprite(DrawingContext context, Bitmap sprite,
        double x, double y, double width, double height, bool facingRight)
    {
        if (facingRight)
        {
            context.DrawImage(sprite, new Rect(x, y, width, height));
            return;
        }

        // Mirror horizontally: flip X, then move back into place
        var flip = Matrix.CreateScale(-1, 1) * Matrix.CreateTranslation(x + width, y);
        using (context.PushTransform(flip))
            context.DrawImage(sprite, new Rect(0, 0, width, height));
    }

    private void DrawShips(DrawingContext context)
    {
        // Draw far ships first so near ships end up on top
        foreach (var s in _world.Ships.OrderBy(s => s.Lane))
        {
            var sprite = s.Type switch
            {
                ShipType.PatrolBoat => _patrolBoatSprite,
                ShipType.Freighter => _freighterSprite,
                _ => _battleshipSprite
            };

            // Ships further away are slightly faded, like haze over the sea
            double opacity = s.Lane switch
            {
                Lane.Far => 0.7,
                Lane.Middle => 0.85,
                _ => 1.0
            };

            using (context.PushOpacity(opacity))
                DrawSprite(context, sprite, s.X, s.Y, s.Width, s.Height, s.MovingRight);
        }
    }

    private void DrawSubmarine(DrawingContext context)
    {
        // Hidden in menus and after game over
        if (!IsRunning) return;

        // Blink while invulnerable: skip every other 0.1 second
        if (_world.IsInvulnerable && (int)(_world.InvulnerableTimeLeft * 10) % 2 == 0)
            return;

        var sub = _world.Player;
        DrawSprite(context, _subSprite, sub.X, sub.Y, sub.Width, sub.Height, sub.FacingRight);
    }

    // Small dark barrels with a yellow band
    private void DrawDepthCharges(DrawingContext context)
    {
        foreach (var c in _world.DepthCharges)
        {
            var body = new Rect(c.X, c.Y, DepthCharge.Size, DepthCharge.Size);
            context.DrawRectangle(ChargeBrush, null, body, 1.5, 1.5);
            context.FillRectangle(ChargeBandBrush,
                new Rect(c.X, c.Y + DepthCharge.Size * 0.4, DepthCharge.Size, 1.2));
        }
    }

    private void DrawTorpedoes(DrawingContext context)
    {
        foreach (var t in _world.Torpedoes)
        {
            // Bubble trail: small fading circles behind the torpedo
            for (int i = 1; i <= 3; i++)
            {
                byte alpha = (byte)(140 - i * 40);
                var bubble = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255));
                double size = 2.5 - i * 0.4;
                context.DrawEllipse(bubble, null,
                    new Point(t.X + Torpedo.Width / 2, t.Y + Torpedo.Height + i * 4), size, size);
            }

            // Torpedo body
            context.DrawRectangle(TorpedoBrush, null,
                new Rect(t.X, t.Y, Torpedo.Width, Torpedo.Height), 1.5, 1.5);
        }
    }

    // Expanding, fading fireball
    private void DrawExplosions(DrawingContext context)
    {
        foreach (var e in _world.Explosions)
        {
            byte alpha = (byte)(255 * (1 - e.Progress));
            double radius = 4 + e.Progress * 16;

            var outer = new SolidColorBrush(Color.FromArgb(alpha, 255, 140, 0));
            var inner = new SolidColorBrush(Color.FromArgb(alpha, 255, 230, 120));

            context.DrawEllipse(outer, null, new Point(e.X, e.Y), radius, radius);
            context.DrawEllipse(inner, null, new Point(e.X, e.Y), radius * 0.5, radius * 0.5);
        }
    }

    private void DrawHud(DrawingContext context)
    {
        DrawAmmo(context);
        DrawText(context, $"SCORE {_world.Score:D6}", 10, 8, 14);
        DrawText(context, $"LEVEL {_world.Level}", 10, 26, 12);
        DrawText(context, $"LIVES {_world.Lives}", GameWorld.Width - 10, 8, 14, TextAlignment.Right);

        // Big banner in the middle for a moment after leveling up
        if (_world.LevelUpTimeLeft > 0)
            DrawText(context, $"LEVEL {_world.Level}", GameWorld.Width / 2, 150, 28, TextAlignment.Center);
    }

    // Torpedo ammo at the top center, with a reload bar when empty
    private void DrawAmmo(DrawingContext context)
    {
        const double slotWidth = 6;
        const double slotHeight = 14;
        const double gap = 4;

        double totalWidth = GameWorld.MaxTorpedoes * slotWidth + (GameWorld.MaxTorpedoes - 1) * gap;
        double startX = GameWorld.Width / 2 - totalWidth / 2;
        const double y = 8;

        // Dark backing so the HUD is readable on the bright sky
        context.DrawRectangle(HudBackBrush, null,
            new Rect(startX - 6, y - 4, totalWidth + 12, slotHeight + 14), 4, 4);

        for (int i = 0; i < GameWorld.MaxTorpedoes; i++)
        {
            var brush = i < _world.Ammo ? AmmoFullBrush : AmmoEmptyBrush;
            context.DrawRectangle(brush, null,
                new Rect(startX + i * (slotWidth + gap), y, slotWidth, slotHeight), 2, 2);
        }

        if (_world.IsReloading)
        {
            double barY = y + slotHeight + 3;
            context.FillRectangle(AmmoEmptyBrush, new Rect(startX, barY, totalWidth, 3));
            context.FillRectangle(AmmoFullBrush, new Rect(startX, barY, totalWidth * _world.ReloadProgress, 3));
        }
    }

    // ===================== PERISCOPE =====================

    private void RenderPeriscope(DrawingContext context, PeriscopeWorld p)
    {
        DrawPeriscopeSea(context, p);

        // Far ships first so near ships end up on top
        foreach (var s in p.Ships.OrderByDescending(s => s.Range))
            DrawPeriShip(context, p, s);

        DrawPeriTorpedoes(context, p);
        DrawPeriExplosions(context, p);

        // Frame on top hides everything outside the lenses
        context.DrawImage(_periscopeFrame, new Rect(0, 0, GameWorld.Width, GameWorld.Height));

        DrawRadar(context, p);
        DrawPeriHud(context, p);
    }

    // Screen x for a bearing, relative to where the periscope points
    private static double BearingToX(PeriscopeWorld p, double bearing) =>
        PeriCenterX + PeriscopeWorld.Delta(bearing, p.Heading) * PixelsPerDegree;

    private static double RangeToY(double range) => PeriHorizon + RangeDrop / range;

    // Panorama made of the sea photo. PeriscopeSea.png is seamless, so the tiles just repeat.
    private void DrawPeriscopeSea(DrawingContext context, PeriscopeWorld p)
    {
        double offset = (p.Heading * PixelsPerDegree) % SeaTileWidth;
        double x = Math.Floor(-offset);

        while (x < GameWorld.Width)
        {
            // 1 pixel overlap hides the thin gap between tiles
            context.DrawImage(_periscopeSea, new Rect(x, SeaTileY, SeaTileWidth + 1, SeaTileHeight));
            x += SeaTileWidth;
        }
    }

    private void DrawPeriShip(DrawingContext context, PeriscopeWorld p, PeriShip s)
    {
        double width = s.AngularWidth * PixelsPerDegree;
        double cx = BearingToX(p, s.Bearing);
        if (cx + width < 0 || cx - width > GameWorld.Width) return; // Not in view

        double waterline = RangeToY(s.Range);

        // Haze: far ships are fainter
        double opacity = Math.Clamp(1.3 - s.Range / PeriscopeWorld.MaxShipRange * 0.6, 0.6, 1);

        if (s.IsHunter)
        {
            // Destroyer seen from the front, getting bigger as it closes in
            // Twice as big far away, and grows extra fast the last stretch so it fills the view when it hits
            double closeness = 1 - s.Range / PeriscopeWorld.HunterStartRange;   // 0 far, 1 on top of us
            double hunterScale = 2.0 + 3.0 * Math.Pow(closeness, 3);
            const double hunterMaxY = 235;       // Water line never lower than this on screen

            double w = width * hunterScale;
            double h = w * _destroyerSprite.Size.Height / _destroyerSprite.Size.Width;
            double y = Math.Min(waterline, hunterMaxY) - h * 0.88;  // Bow wave sits on the water line
            using (context.PushOpacity(opacity))
                context.DrawImage(_destroyerSprite, new Rect(cx - w / 2, y, w, h));

            // Red arrow above it, so it is easy to spot far away
            double a = 4;
            var arrow = new StreamGeometry();
            using (var g = arrow.Open())
            {
                g.BeginFigure(new Point(cx - a, y - a * 2.5), true);
                g.LineTo(new Point(cx + a, y - a * 2.5));
                g.LineTo(new Point(cx, y - a));
                g.EndFigure(true);
            }
            context.DrawGeometry(HunterBrush, null, arrow);
            return;
        }

        double height = width * s.Aspect;
        var sprite = s.Type switch
        {
            ShipType.PatrolBoat => _patrolBoatSprite,
            ShipType.Freighter => _freighterSprite,
            _ => _battleshipSprite
        };

        using (context.PushOpacity(opacity))
            DrawSprite(context, sprite, cx - width / 2, waterline - height * 0.85, width, height, s.MovingRight);
    }

    // White wake from the bottom of the view towards the horizon, getting smaller with distance
    private void DrawPeriTorpedoes(DrawingContext context, PeriscopeWorld p)
    {
        const double wakeLength = 1200; // Meters of visible wake behind the torpedo
        const double minRange = 250;    // Closer than this is below the lens

        foreach (var t in p.Torpedoes)
        {
            double x = BearingToX(p, t.Bearing);
            if (x < -20 || x > GameWorld.Width + 20) continue;

            for (double r = Math.Max(minRange, t.Range - wakeLength); r <= t.Range; r += 30)
            {
                double fade = 1 - (t.Range - r) / wakeLength;   // 1 at the head, 0 at the tail
                double size = 1500 / r;
                byte alpha = (byte)(40 + 140 * fade);
                var brush = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255));
                context.DrawEllipse(brush, null, new Point(x, RangeToY(r)), size * 1.3, size * 0.45);
            }

            if (t.Range > minRange)
            {
                double head = 1800 / t.Range;
                context.DrawEllipse(WakeHeadBrush, null, new Point(x, RangeToY(t.Range)), head * 1.4, head * 0.5);
            }
        }
    }

    // Water column from splash.png: shoots up fast, then fades out
    private void DrawPeriExplosions(DrawingContext context, PeriscopeWorld p)
    {
        foreach (var e in p.Explosions)
        {
            double x = BearingToX(p, e.Bearing);
            double waterline = RangeToY(e.Range);

            double fullHeight = 60000 / e.Range;                     // Closer = bigger
            double rise = Math.Min(1, e.Progress / 0.3);              // Up in the first 30%
            double fade = e.Progress < 0.5 ? 1 : 1 - (e.Progress - 0.5) / 0.5;

            double h = fullHeight * (0.4 + 0.6 * rise);
            double w = fullHeight * _splashSprite.Size.Width / _splashSprite.Size.Height;

            using (context.PushOpacity(fade))
                context.DrawImage(_splashSprite, new Rect(x - w / 2, waterline - h * 0.92, w, h));
        }
    }

    // Radar for the front half circle: straight up is the bow, left/right edge is 90 degrees to each side.
    // The green wedge shows where the periscope is pointing.
    private void DrawRadar(DrawingContext context, PeriscopeWorld p)
    {
        var center = new Point(RadarX, RadarY);

        // Periscope view wedge
        double look = PeriscopeWorld.Delta(p.Heading, 0);
        double fovHalf = PeriscopeWorld.FieldOfView / 2;
        context.DrawLine(RadarFovPen, center, RadarPoint(look - fovHalf, 1));
        context.DrawLine(RadarFovPen, center, RadarPoint(look + fovHalf, 1));

        // Sweep line going back and forth over the half circle
        double sweep = Math.Abs(_radarSweep - 180) - 90;
        context.DrawLine(RadarSweepPen, center, RadarPoint(sweep, 1));

        foreach (var s in p.Ships)
        {
            double angle = PeriscopeWorld.Delta(s.Bearing, 0);
            double dist = s.Range / PeriscopeWorld.TorpedoMaxRange;

            if (s.IsHunter)
            {
                // Red dot that starts small in the outer ring and grows as it closes in
                double closeness = 1 - s.Range / PeriscopeWorld.HunterStartRange;   // 0 far, 1 on top of us
                double size = 2 + closeness * 5;
                context.DrawEllipse(HunterBrush, null, RadarPoint(angle, dist), size, size);
            }
            else
            {
                context.DrawEllipse(RadarBlipBrush, null, RadarPoint(angle, dist), 2.2, 2.2);
            }
        }

        foreach (var t in p.Torpedoes)
        {
            double angle = PeriscopeWorld.Delta(t.Bearing, 0);
            double dist = Math.Min(1, t.Range / PeriscopeWorld.TorpedoMaxRange);
            context.DrawEllipse(Brushes.White, null, RadarPoint(angle, dist), 1, 1);
        }
    }

    // angle: degrees from straight up, dist: 0 = center, 1 = edge
    private static Point RadarPoint(double angle, double dist)
    {
        double rad = angle * Math.PI / 180;
        return new Point(RadarX + Math.Sin(rad) * RadarRadius * dist,
                         RadarY - Math.Cos(rad) * RadarRadius * dist);
    }

    private void DrawPeriHud(DrawingContext context, PeriscopeWorld p)
    {
        // Heading just under the top notch, next to the vertical line
        DrawText(context, $"{PeriscopeWorld.Delta(p.Heading, 0):+0;-0;0}°", PeriCenterX + 4, 50, 9);   // - = left of bow, + = right

        // Left lens: torpedoes in stock and the three tubes
               // Stock plus the ones already loaded (or loading) in the tubes
        int torpedoesLeft = p.TorpedoesInStock + Array.FindAll(p.Tubes, t => t >= 0).Length;
        DrawText(context, $"TORPEDOES {torpedoesLeft}", 105, 36, 9);
        for (int i = 0; i < PeriscopeWorld.TubeCount; i++)
        {
            double tx = 105 + i * 22;
            const double ty = 50;
            double t = p.Tubes[i];

            context.DrawRectangle(AmmoEmptyBrush, null, new Rect(tx, ty, 18, 5), 1, 1);
            if (t == 0)
                context.DrawRectangle(AmmoFullBrush, null, new Rect(tx, ty, 18, 5), 1, 1);
            else if (t > 0)
            {
                double progress = 1 - t / PeriscopeWorld.TubeReloadTime;
                context.DrawRectangle(AmmoFullBrush, null, new Rect(tx, ty, 18 * progress, 5), 1, 1);
            }
        }

        if (p.IsOutOfTorpedoes && !p.IsSunk)
            DrawColoredText(context, "OUT OF TORPEDOES", PeriCenterX, 70, 12, WarningTextBrush);

        // Right lens: score
               // Destroyers sunk, and the sonar pings they give in the minefield
        DrawColoredText(context, $"DESTROYERS {p.DestroyersSunk}", 495, 50, 9, WarningTextBrush);
        DrawText(context, $"+{p.DestroyersSunk * MinefieldWorld.PingsPerDestroyer} PINGS", 535, 62, 8, TextAlignment.Right);

        // Fire lamp (bottom right): dark when no tube is ready
        if (!Array.Exists(p.Tubes, t => t == 0))
            context.DrawEllipse(LampOffBrush, null, new Point(555, 287), 8, 8);

        // Destroyer warning: blinks faster the closer it gets
        var hunter = p.ClosestHunter();
        if (hunter != null && !p.IsSunk)
        {
            double rate = Math.Clamp(hunter.Range / 1000, 0.15, 0.6);
            if ((int)(_blink / rate) % 2 == 0)
            {
                DrawColoredText(context, $"DESTROYER {hunter.Range / 1000:0.0}km  {PeriscopeWorld.Delta(hunter.Bearing, 0):+0;-0;0}°",
                    PeriCenterX, 262, 10, WarningTextBrush);

                // Light up the red lamp top left
                const double glow = 30;
                context.DrawImage(_warningGlow, new Rect(53.5 - glow / 2, 28 - glow / 2, glow, glow));
            }
        }

        if (p.IsSunk)
        {
            byte alpha = (byte)(140 * p.SunkTimeLeft / PeriscopeWorld.SunkDelay + 40);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(alpha, 200, 0, 0)),
                new Rect(0, 0, GameWorld.Width, GameWorld.Height));
            DrawColoredText(context, "CRASH DIVE!", PeriCenterX, 140, 28, Brushes.White);
        }

        // Target data on the small plate bottom right
        var target = p.TargetInSight();
        if (target != null)
        {
            double x = 452;
            DrawSmallGreen(context, $"RNG {target.Range / 1000:0.0}km", x, 316);
            DrawSmallGreen(context, $"SPD {target.Knots:0}kn {(target.MovingRight ? ">" : "<")}", x, 324);
        }
    }

    // Centered text with a dark shadow in any color
    private static void DrawColoredText(DrawingContext context, string text, double centerX, double y,
        double size, IBrush brush)
    {
        var main = MakeText(text, size, brush);
        var shadow = MakeText(text, size, Brushes.Black);
        double x = centerX - main.Width / 2;
        context.DrawText(shadow, new Point(x + 1, y + 1));
        context.DrawText(main, new Point(x, y));
    }

    private static void DrawSmallGreen(DrawingContext context, string text, double x, double y) =>
        context.DrawText(MakeText(text, 6.5, RadarTextBrush), new Point(x, y));

    // ===================== MINEFIELD =====================

    private static readonly IBrush FogBrush = new SolidColorBrush(Color.Parse("#B0061A2E"));
    private static readonly IBrush RevealedBrush = new SolidColorBrush(Color.Parse("#2060C0FF"));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#3080C8FF")), 0.6);
    private static readonly IPen FlagPen = new Pen(new SolidColorBrush(Color.Parse("#FF4040")), 1.2);
    private static readonly IBrush HudStripBrush = new SolidColorBrush(Color.Parse("#A0001020"));
    private static readonly IBrush HullFullBrush = new SolidColorBrush(Color.Parse("#E24B4A"));
    private static readonly IBrush[] NumberBrushes =
    {
        Brushes.Transparent,
        new SolidColorBrush(Color.Parse("#85B7EB")),  // 1
        new SolidColorBrush(Color.Parse("#97C459")),  // 2
        new SolidColorBrush(Color.Parse("#EF9F27")),  // 3
        new SolidColorBrush(Color.Parse("#E24B4A")),  // 4
        new SolidColorBrush(Color.Parse("#D4537E")),  // 5+
    };

    private const double HarborWaterline = 0.47; // Where the water surface is in harbor.png (0 = top)

    private void RenderMinefield(DrawingContext context, MinefieldWorld m)
    {
        const double cell = MinefieldWorld.CellSize;
        double cameraX = (m.VisualCol - MinefieldWorld.SubScreenCol) * cell;

        DrawMineBackground(context, cameraX);
        DrawHarbor(context, cameraX);
        DrawMineGrid(context, m, cameraX);
        DrawSonarPing(context, m, cameraX);
        DrawMineSub(context, m, cameraX);
        DrawMineBlasts(context, m, cameraX);
        DrawMineHud(context, m);
    }

    // Seamless background, scrolls at half speed for a bit of depth
    private void DrawMineBackground(DrawingContext context, double cameraX)
    {
        double h = GameWorld.Height;
        double w = h * _mineBackground.Size.Width / _mineBackground.Size.Height;
        double x = -((cameraX * 0.5) % w);
        if (x > 0) x -= w;
        x = Math.Floor(x);

        while (x < GameWorld.Width)
        {
            context.DrawImage(_mineBackground, new Rect(x, 0, w + 1, h));
            x += w;
        }
    }

    // Harbor right after the last column. Its water line sits at the top of the grid.
    private void DrawHarbor(DrawingContext context, double cameraX)
    {
        double underwater = GameWorld.Height - MinefieldWorld.GridTop;
        double h = underwater / (1 - HarborWaterline);
        double w = h * _harbor.Size.Width / _harbor.Size.Height;
        double x = MinefieldWorld.Cols * MinefieldWorld.CellSize - cameraX;
        double y = MinefieldWorld.GridTop - h * HarborWaterline;

        if (x < GameWorld.Width)
            context.DrawImage(_harbor, new Rect(x, y, w, h));
    }

    private void DrawMineGrid(DrawingContext context, MinefieldWorld m, double cameraX)
    {
        const double cell = MinefieldWorld.CellSize;
        int first = Math.Max(0, (int)(cameraX / cell) - 1);
        int last = Math.Min(MinefieldWorld.Cols - 1, first + (int)(GameWorld.Width / cell) + 2);

        for (int c = first; c <= last; c++)
        {
            double x = c * cell - cameraX;
            for (int r = 0; r < MinefieldWorld.Rows; r++)
            {
                var mc = m.Cells[c, r];
                var rect = new Rect(x, MinefieldWorld.GridTop + r * cell, cell, cell);

                if (mc.IsRevealed)
                {
                    context.FillRectangle(RevealedBrush, rect);
                    if (mc.Number > 0)
                    {
                        var brush = NumberBrushes[Math.Min(mc.Number, 5)];
                        var text = MakeText(mc.Number.ToString(), 18, brush);
                        context.DrawText(text, new Point(rect.Center.X - text.Width / 2, rect.Center.Y - text.Height / 2));
                    }
                }
                else if (!mc.IsExploded)
                {
                    context.FillRectangle(FogBrush, rect);
                }

                // Mines found by sonar, or flagged by the player (faint)
                if ((mc.IsDetected && !mc.IsExploded) || mc.IsFlagged)
                {
                    double mh = cell * 0.9;
                    double mw = mh * _mineSprite.Size.Width / _mineSprite.Size.Height;
                    using (context.PushOpacity(mc.IsDetected ? 1.0 : 0.45))
                        context.DrawImage(_mineSprite, new Rect(rect.Center.X - mw / 2, rect.Y + cell * 0.05, mw, mh));
                }

                if (mc.IsFlagged)
                    context.DrawRectangle(null, FlagPen, rect.Deflate(2), 3, 3);

                context.DrawRectangle(null, GridPen, rect);
            }
        }
    }

    // Green arc that sweeps forward from the bow
    private void DrawSonarPing(DrawingContext context, MinefieldWorld m, double cameraX)
    {
        if (m.PingAnim <= 0) return;

        const double cell = MinefieldWorld.CellSize;
        double progress = 1 - m.PingAnim / MinefieldWorld.PingAnimTime;
        double cx = (m.VisualCol + 1) * cell - cameraX;
        double cy = MinefieldWorld.GridTop + (m.VisualRow + 0.5) * cell;
        double radius = progress * MinefieldWorld.PingRange * cell;
        byte alpha = (byte)(220 * (1 - progress));

        var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 96, 255, 96)), 2);
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            g.BeginFigure(new Point(cx, cy - radius), false);
            g.ArcTo(new Point(cx, cy + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, pen, arc);
    }

    private void DrawMineSub(DrawingContext context, MinefieldWorld m, double cameraX)
    {
        const double cell = MinefieldWorld.CellSize;
        double w = cell * 2.2;
        double h = w * _damagedSub.Size.Height / _damagedSub.Size.Width;
        double cx = (m.VisualCol + 0.5) * cell - cameraX;
        double cy = MinefieldWorld.GridTop + (m.VisualRow + 0.5) * cell;

        // Slight bobbing, the boat is barely holding together
        cy += Math.Sin(_blink * 2.5) * 1.5;

        if (m.IsSunk) return;
        context.DrawImage(_damagedSub, new Rect(cx - w * 0.6, cy - h / 2, w, h));
    }

    private void DrawMineBlasts(DrawingContext context, MinefieldWorld m, double cameraX)
    {
        const double cell = MinefieldWorld.CellSize;
        foreach (var b in m.Blasts)
        {
            double size = cell * (1 + b.Progress * 1.5);
            double cx = (b.Col + 0.5) * cell - cameraX;
            double cy = MinefieldWorld.GridTop + (b.Row + 0.5) * cell;
            using (context.PushOpacity(1 - b.Progress * 0.8))
                context.DrawImage(_mineBlastSprite, new Rect(cx - size / 2, cy - size / 2, size, size));
        }
    }

    private void DrawMineHud(DrawingContext context, MinefieldWorld m)
    {
        context.FillRectangle(HudStripBrush, new Rect(0, 0, GameWorld.Width, MinefieldWorld.GridTop));

        DrawText(context, "LIMPING HOME", 10, 12, 12);

        // Hull pieces
        DrawText(context, "HULL", 150, 12, 11);
        for (int i = 0; i < MinefieldWorld.StartHull; i++)
        {
            var brush = i < m.Hull ? HullFullBrush : AmmoEmptyBrush;
            context.DrawRectangle(brush, null, new Rect(185 + i * 16, 13, 12, 10), 2, 2);
        }

        DrawText(context, $"PINGS {m.Pings}", 250, 12, 11);
        DrawText(context, $"HARBOR {Math.Max(0, MinefieldWorld.Cols - m.Col)}", 330, 12, 11);
        DrawText(context, $"SCORE {m.Score:D6}", GameWorld.Width - 10, 12, 11, TextAlignment.Right);

        // Countdown bar to the next automatic step
        double progress = 1 - m.AdvanceTimer / MinefieldWorld.AdvanceInterval;
        context.FillRectangle(AmmoEmptyBrush, new Rect(0, MinefieldWorld.GridTop - 3, GameWorld.Width, 3));
        context.FillRectangle(AmmoFullBrush, new Rect(0, MinefieldWorld.GridTop - 3, GameWorld.Width * progress, 3));

        if (m.IsHome)
            DrawColoredText(context, "HOME SAFE!", GameWorld.Width / 2, 150, 28, AmmoFullBrush);
        else if (m.IsSunk)
            DrawColoredText(context, "SUNK!", GameWorld.Width / 2, 150, 28, WarningTextBrush);
    }

    private static FormattedText MakeText(string text, double size, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, HudFont, size, brush);

    // Text with a dark shadow. x is the left edge, center or right edge depending on align.
    private static void DrawText(DrawingContext context, string text, double x, double y, double size,
        TextAlignment align = TextAlignment.Left)
    {
        var main = MakeText(text, size, Brushes.White);
        var shadow = MakeText(text, size, Brushes.Black);

        double drawX = align switch
        {
            TextAlignment.Center => x - main.Width / 2,
            TextAlignment.Right => x - main.Width,
            _ => x
        };

        context.DrawText(shadow, new Point(drawX + 1, y + 1));
        context.DrawText(main, new Point(drawX, y));
    }
}