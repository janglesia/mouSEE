using System.Windows;

namespace app.UI;

public partial class MainMenuWindow : Window
{
    private void ReturnButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
    public MainMenuWindow()
    {
        InitializeComponent();
    }

}
