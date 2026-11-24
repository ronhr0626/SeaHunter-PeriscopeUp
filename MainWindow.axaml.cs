using Avalonia.Controls;
using Avalonia.Interactivity;
using SeaHunter.Game;

namespace SeaHunter;

public partial class MainWindow : Window
{
    private int _periscopeTorpedoes;

    public MainWindow()
    {
        InitializeComponent();

        // The canvas tells us when each part of the game ends
        Game.SideViewOver += OnSideViewOver;
        Game.GameOver += OnGameOver;

        StartHighScoreList.ItemsSource = HighScoreStore.Format(HighScoreStore.Load());
    }

    private void OnStartClick(object? sender, RoutedEventArgs e) => StartGame();
    private void OnPlayAgainClick(object? sender, RoutedEventArgs e) => StartGame();

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
        Game.StartNewGame();
    }

    // Side view is over: show how many torpedoes the periscope round gets
    private void OnSideViewOver(int score, int level)
    {
        _periscopeTorpedoes = level * PeriscopeWorld.TorpedoesPerLevel;

        LevelReachedText.Text = $"LEVEL {level} REACHED   SCORE {score:D6}";
        TorpedoCountText.Text = $"{_periscopeTorpedoes} TORPEDOES";
        PeriscopeIntroScreen.IsVisible = true;
    }

    private void OnPeriscopeClick(object? sender, RoutedEventArgs e)
    {
        PeriscopeIntroScreen.IsVisible = false;
        Game.StartPeriscope(_periscopeTorpedoes);
    }

    private void OnGameOver(int score)
    {
        // Only save real scores
        var top = score > 0 ? HighScoreStore.Add(score) : HighScoreStore.Load();

        FinalScoreText.Text = $"SCORE {score:D6}";
        NewHighScoreText.IsVisible = score > 0 && top[0] == score;
        GameOverHighScoreList.ItemsSource = HighScoreStore.Format(top);

        GameOverScreen.IsVisible = true;
    }
}
