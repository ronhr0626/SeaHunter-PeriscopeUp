using System;
using System.Collections.Generic;

namespace SeaHunter.Game;

// One square of the minefield
public class MineCell
{
    public bool IsMine { get; set; }
    public bool IsRevealed { get; set; }   // Pinged or visited: shows its number
    public bool IsDetected { get; set; }   // A mine found by sonar: drawn as a mine
    public bool IsFlagged { get; set; }    // Player marked it as a mine
    public bool IsExploded { get; set; }   // We drove into it
    public int Number { get; set; }        // Mines in the 8 cells around
}

public class MineBlast
{
    public const double Duration = 0.7;
    public int Col { get; init; }
    public int Row { get; init; }
    public double Age { get; set; }
    public double Progress => Age / Duration;
    public bool IsDone => Age >= Duration;
}

// Stage 3: limping home through a minefield, Minesweeper style
public class MinefieldWorld
{
    // ===== Tweak these =====
    public const int Rows = 8;
    public const int BaseCols = 40;             // Length of the field on patrol 1, the harbor comes after
    public const int ColsPerPatrol = 20;        // Each new patrol: this much longer way home
    public const int MaxCols = 120;
    public const double CellSize = 40;          // Pixels in the 640x360 world
    public const double GridTop = 40;           // HUD strip above the grid
    public const int SubScreenCol = 3;          // Where on screen the sub sits (columns from the left)
    public const double MineChance = 0.22;      // Chance for a mine in each free cell
    public const int SafeStartCols = 3;         // No mines in the first columns
    public const int SafeEndCols = 2;           // ...or the last ones before the harbor
    public const double AdvanceInterval = 3.0;  // Seconds between automatic steps forward
    public const double MoveSpeed = 6;          // Cells per second for the smooth slide
    public const int StartHull = 3;
    public const int BasePings = 3;
    public const int PingsPerDestroyer = 2;
    public const int PingRange = 3;             // Columns ahead the sonar reaches
    public const double PingAnimTime = 0.6;
    public const int HomeBonus = 1000;
    public const int HullBonus = 500;           // Per hull piece left
    public const int PingBonus = 100;           // Per ping left
    // =======================

    // Length of this field: 40 on patrol 1, 60 on patrol 2, 80 on patrol 3 ...
    public int Cols { get; }
    public MineCell[,] Cells { get; }

    public int Col { get; private set; }        // Sub's cell
    public int Row { get; private set; }
    public double VisualCol { get; private set; }  // Smooth position for drawing
    public double VisualRow { get; private set; }

    public int Hull { get; private set; } = StartHull;
    public int Pings { get; private set; }
    public int Score { get; private set; }
    public double AdvanceTimer { get; private set; } = AdvanceInterval;
    public double PingAnim { get; private set; }  // > 0 while the sonar wave is shown

    public List<MineBlast> Blasts { get; } = new();

    public bool IsHome { get; private set; }
    public bool IsSunk => Hull <= 0;
    public bool IsOver => (IsHome || IsSunk) && Blasts.Count == 0 && _endDelay <= 0;

    private readonly Random _rng = new();
    private double _endDelay = 1.5;

    public MinefieldWorld(int destroyersSunk, int startScore, int patrol = 1)
    {
        Cols = Math.Min(MaxCols, BaseCols + (Math.Max(1, patrol) - 1) * ColsPerPatrol);
        Cells = new MineCell[Cols, Rows];

        Pings = BasePings + destroyersSunk * PingsPerDestroyer;
        Score = startScore;

        for (int c = 0; c < Cols; c++)
            for (int r = 0; r < Rows; r++)
                Cells[c, r] = new MineCell();

        Row = Rows / 2;
        BuildField();

        VisualCol = Col;
        VisualRow = Row;
        Visit(Col, Row);
    }

    // Places mines, but keeps one guaranteed safe path all the way home
    private void BuildField()
    {
        var safe = new bool[Cols, Rows];
        int r = Row;
        for (int c = 0; c < Cols; c++)
        {
            safe[c, r] = true;

            // Sometimes the safe path changes depth inside this column
            if (_rng.NextDouble() < 0.4)
            {
                r = Math.Clamp(r + (_rng.Next(2) == 0 ? -1 : 1), 0, Rows - 1);
                safe[c, r] = true;
            }
        }

        for (int c = SafeStartCols; c < Cols - SafeEndCols; c++)
            for (int rr = 0; rr < Rows; rr++)
                if (!safe[c, rr] && _rng.NextDouble() < MineChance)
                    Cells[c, rr].IsMine = true;

        for (int c = 0; c < Cols; c++)
            for (int rr = 0; rr < Rows; rr++)
                Cells[c, rr].Number = CountMinesAround(c, rr);
    }

    private int CountMinesAround(int c, int r)
    {
        int count = 0;
        for (int dc = -1; dc <= 1; dc++)
            for (int dr = -1; dr <= 1; dr++)
                if ((dc != 0 || dr != 0) && Inside(c + dc, r + dr) && Cells[c + dc, r + dr].IsMine)
                    count++;
        return count;
    }

    private bool Inside(int c, int r) => c >= 0 && c < Cols && r >= 0 && r < Rows;

    public void Update(double dt, InputState input)
    {
        foreach (var b in Blasts) b.Age += dt;
        Blasts.RemoveAll(b => b.IsDone);
        PingAnim = Math.Max(0, PingAnim - dt);

        // Smooth slide towards the real cell
        VisualCol = MoveTowards(VisualCol, Col, MoveSpeed * dt);
        VisualRow = MoveTowards(VisualRow, Row, MoveSpeed * dt);

        if (IsHome || IsSunk)
        {
            // Let the sub glide into the harbor / the last blast finish
            if (IsHome) VisualCol = MoveTowards(VisualCol, Cols + 2, MoveSpeed * 0.5 * dt);
            _endDelay -= dt;
            return;
        }

        if (input.UpPressed) TryMove(Col, Row - 1);
        if (input.DownPressed) TryMove(Col, Row + 1);
        if (input.Fire) Ping();
        if (input.FlagPressed) ToggleFlagAhead();

        // D / right arrow: step forward now. Otherwise the sub creeps forward on its own.
        AdvanceTimer -= dt;
        if (input.RightPressed || AdvanceTimer <= 0)
            StepForward();
    }

    private void StepForward()
    {
        AdvanceTimer = AdvanceInterval;

        if (Col + 1 >= Cols)
        {
            ReachHome();
            return;
        }
        TryMove(Col + 1, Row);
    }

    private void TryMove(int c, int r)
    {
        if (!Inside(c, r)) return;
        Col = c;
        Row = r;
        Visit(c, r);
    }

    // Entering a cell: boom if it is a mine, otherwise it is revealed
    private void Visit(int c, int r)
    {
        var cell = Cells[c, r];
        cell.IsFlagged = false;

        if (cell.IsMine && !cell.IsExploded)
        {
            cell.IsExploded = true;
            cell.IsDetected = true;
            Hull--;
            Blasts.Add(new MineBlast { Col = c, Row = r });
            return;
        }

        Reveal(c, r);
    }

    // Sonar: reveals a 3 rows high cone ahead. Mines inside it are detected.
    private void Ping()
    {
        if (Pings <= 0) return;
        Pings--;
        PingAnim = PingAnimTime;

        for (int dc = 1; dc <= PingRange; dc++)
            for (int dr = -1; dr <= 1; dr++)
            {
                int c = Col + dc, r = Row + dr;
                if (!Inside(c, r)) continue;

                if (Cells[c, r].IsMine) Cells[c, r].IsDetected = true;
                else Reveal(c, r);
            }
    }

    // Shows a cell's number. A 0 opens its neighbours too, like Minesweeper.
    private void Reveal(int startC, int startR)
    {
        var queue = new Queue<(int c, int r)>();
        queue.Enqueue((startC, startR));

        while (queue.Count > 0)
        {
            var (c, r) = queue.Dequeue();
            var cell = Cells[c, r];
            if (cell.IsRevealed || cell.IsMine) continue;

            cell.IsRevealed = true;
            cell.IsFlagged = false;
            if (cell.Number != 0) continue;

            for (int dc = -1; dc <= 1; dc++)
                for (int dr = -1; dr <= 1; dr++)
                    if (Inside(c + dc, r + dr))
                        queue.Enqueue((c + dc, r + dr));
        }
    }

    // F: mark / unmark the cell straight ahead as a mine
    private void ToggleFlagAhead()
    {
        int c = Col + 1;
        if (!Inside(c, Row)) return;
        var cell = Cells[c, Row];
        if (!cell.IsRevealed) cell.IsFlagged = !cell.IsFlagged;
    }

    private void ReachHome()
    {
        IsHome = true;
        Score += HomeBonus + Hull * HullBonus + Pings * PingBonus;
    }

    private static double MoveTowards(double value, double target, double maxStep) =>
        Math.Abs(target - value) <= maxStep ? target : value + Math.Sign(target - value) * maxStep;
}
