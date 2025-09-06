using ImageMonitor.ViewModels;

namespace ImageMonitor.Views;

public partial class SettingsWindow : Window
{
    public SettingsViewModel ViewModel { get; }

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        
        InitializeComponent();
        
        // ウィンドウ表示後に設定を再読み込み
        Loaded += async (s, e) => 
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("SettingsWindow: Loaded event triggered");
                await ViewModel.LoadSettingsAsync();
                System.Diagnostics.Debug.WriteLine($"SettingsWindow: Settings loaded, ScanDirectories count = {ViewModel.ScanDirectories.Count}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingsWindow: Error in Loaded event: {ex}");
            }
        };
    }

    private async void OK_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.SaveSettingsAsync();
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"設定の保存に失敗しました: {ex.Message}", 
                "エラー", 
                MessageBoxButton.OK, 
                MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}