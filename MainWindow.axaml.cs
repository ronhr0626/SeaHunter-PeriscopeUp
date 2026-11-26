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
        Game.QuitToMenu += OnQuitToMenu;

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

    // M on the pause screen
    private void OnQuitToMenu()
    {
        HideAllScreens();
        StartScreen.IsVisible = true;
        StartHighScoreList.ItemsSource = HighScoreStore.Format(HighScoreStore.Load());
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
        _periscopeTorpedoes = System.Math.Clamp(played * PeriscopeWorld.TorpedoesPerLevel,
            PeriscopeWorld.MinTorpedoes, PeriscopeWorld.TorpedoCap(Game.Patrol));

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

    private int _pendingScore;   // Score waiting for a name before it is saved

    private void ShowGameOver(int score)
    {
        FinalScoreText.Text = $"SCORE {score:D6}";
        NewHighScoreText.IsVisible = false;
        GameOverHighScoreList.ItemsSource = HighScoreStore.Format(HighScoreStore.Load());

        // Good enough for the list (and not a test run): ask for a name first
        bool qualifies = !_testMode && HighScoreStore.Qualifies(score);
        _pendingScore = qualifies ? score : 0;
        ShowNameEntry(qualifies);

        GameOverScreen.IsVisible = true;
        if (qualifies)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => NameBox.Focus());
    }

    // Name entry and the play again / menu buttons are never shown at the same time
    private void ShowNameEntry(bool show)
    {
        NameEntryPanel.IsVisible = show;
        SaveNameButton.IsDefault = show;
        GameOverButtons.IsVisible = !show;
        PlayAgainButton.IsDefault = !show;
        if (show) NameBox.Text = "";
    }

    private void OnSaveNameClick(object? sender, RoutedEventArgs e)
    {
        var top = HighScoreStore.Add(NameBox.Text ?? "", _pendingScore);

        NewHighScoreText.IsVisible = top.Count > 0 && top[0].Score == _pendingScore;
        GameOverHighScoreList.ItemsSource = HighScoreStore.Format(top);
        StartHighScoreList.ItemsSource = HighScoreStore.Format(top);

        _pendingScore = 0;
        ShowNameEntry(false);
    }
}
