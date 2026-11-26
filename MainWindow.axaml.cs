using Avalonia.Controls;
using Avalonia.Interactivity;
using SeaHunter.Game;

namespace SeaHunter;

public partial class MainWindow : Window
{
    private int _periscopeTorpedoes;
    private int _finalScore;
    private bool _testMode;   // True when started from the TEST button: no high score is saved

    public MainWindow()
    {
        InitializeComponent();

        // The canvas tells us when each part of the game ends
        Game.SideViewOver += OnSideViewOver;
        Game.GameOver += OnCrashDive;
        Game.MinefieldOver += OnMinefieldOver;

        StartHighScoreList.ItemsSource = HighScoreStore.Format(HighScoreStore.Load());
    }

    private void OnStartClick(object? sender, RoutedEventArgs e)
    {
        _testMode = false;
        StartGame();
    }

    // Test mode: start any patrol on any stage
    private void OnTestClick(object? sender, RoutedEventArgs e)
    {
        _testMode = true;
        int patrol = TestPatrolBox.SelectedIndex + 1;
        int stage = TestStageBox.SelectedIndex + 1;

        HideAllScreens();
        Game.StartTest(patrol, stage);
    }

    private void HideAllScreens()
    {
        StartScreen.IsVisible = false;
        GameOverScreen.IsVisible = false;
        PeriscopeIntroScreen.IsVisible = false;
        CrashDiveScreen.IsVisible = false;
        HomeSafeScreen.IsVisible = false;
    }
    private void OnPlayAgainClick(object? sender, RoutedEventArgs e)
    {
        _testMode = false;
        StartGame();
    }

    private void OnMenuClick(object? sender, RoutedEventArgs e)
    {
        GameOverScreen.IsVisible = false;
        StartScreen.IsVisible = true;
        StartHighScoreList.ItemsSource = HighScoreStore.Format(HighScoreStore.Load());
    }

    private void StartGame()
    {
        StartScreen.IsVisible = false;
        GameOverScreen.IsVisible = false;
        PeriscopeIntroScreen.IsVisible = false;
        CrashDiveScreen.IsVisible = false;
        HomeSafeScreen.IsVisible = false;
        Game.StartNewGame();
    }

    // Side view is over: show how many torpedoes the periscope round gets
    private void OnSideViewOver(int score, int level)
    {
        // Only levels actually played this patrol give torpedoes
        int played = Game.LevelsPlayed;
        _periscopeTorpedoes = played * PeriscopeWorld.TorpedoesPerLevel;

        LevelReachedText.Text = $"LEVEL {level} REACHED ({played} played)   SCORE {score:D6}";
        int destroyers = System.Math.Min(PeriscopeWorld.MaxHuntersPerWave, Game.Patrol);
        TorpedoCountText.Text = destroyers == 1
            ? $"{_periscopeTorpedoes} TORPEDOES"
            : $"{_periscopeTorpedoes} TORPEDOES   –   DESTROYERS COME {destroyers} AT A TIME";
        PeriscopeIntroScreen.IsVisible = true;
    }

    private void OnPeriscopeClick(object? sender, RoutedEventArgs e)
    {
        PeriscopeIntroScreen.IsVisible = false;
        Game.StartPeriscope(_periscopeTorpedoes);
    }

    // The destroyer got us in the periscope: show the crash dive message first
    private void OnCrashDive(int score)
    {
        _finalScore = score;

        int sunk = Game.DestroyersSunk;
        int pings = MinefieldWorld.BasePings + sunk * MinefieldWorld.PingsPerDestroyer;
        PingInfoText.Text = $"Destroyers sunk: {sunk}  →  {pings} sonar pings";

        CrashDiveScreen.IsVisible = true;
    }

    // Starts stage 3: limping home through the minefield
    private void OnCrashDiveContinueClick(object? sender, RoutedEventArgs e)
    {
        CrashDiveScreen.IsVisible = false;
        Game.StartMinefield();
    }

    private void OnMinefieldOver(int score, bool madeItHome)
    {
        if (!madeItHome)
        {
            ShowGameOver(score);
            return;
        }

        // Made it home: offer a new patrol with bubble shields
        HomeScoreText.Text = $"SCORE {score:D6}   NEXT PATROL: LEVEL {Game.NextPatrolLevel}";
        HomeSafeScreen.IsVisible = true;
    }

    private void OnNextPatrolClick(object? sender, RoutedEventArgs e)
    {
        HomeSafeScreen.IsVisible = false;
        Game.StartNextPatrol();
    }

    private void ShowGameOver(int score)
    {
        // Only save real scores, never from test mode
        var top = score > 0 && !_testMode ? HighScoreStore.Add(score) : HighScoreStore.Load();

        FinalScoreText.Text = $"SCORE {score:D6}";
        NewHighScoreText.IsVisible = score > 0 && top[0] == score;
        GameOverHighScoreList.ItemsSource = HighScoreStore.Format(top);

        GameOverScreen.IsVisible = true;
    }
}
