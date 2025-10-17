using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace RatPet;

public partial class InventoryWindow : Window
{
    public record InvItem(string Name, string PackUri);
    private System.Windows.Point _pressPoint;
    private bool _pressed;

    public InventoryWindow()
    {
        InitializeComponent();
        LoadDefaultItems();
    }

    private void LoadDefaultItems()
    {
        var items = new[]
        {
            new InvItem("Toy Ball", "pack://application:,,,/toy.png"),
        };

        foreach (var it in items)
        {
            var img = new System.Windows.Controls.Image
            {
                Source = new BitmapImage(new System.Uri(it.PackUri)),
                Stretch = System.Windows.Media.Stretch.None,
                Margin = new Thickness(6),
                Tag = it,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            img.PreviewMouseLeftButtonDown += Item_PreviewMouseLeftButtonDown;
            img.MouseMove += Item_MouseMove;
            img.PreviewMouseLeftButtonUp += Item_PreviewMouseLeftButtonUp;
            ItemsPanel.Children.Add(img);
        }
    }

    private void Item_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _pressed = true;
        _pressPoint = e.GetPosition(this);
        e.Handled = true;
        if (sender is System.Windows.Controls.Image img && img.Tag is InvItem it)
        {
            // Spawn toy immediately and follow cursor; overlay owns visuals
            (System.Windows.Application.Current.MainWindow as MainWindow)?.BeginDragSpawn(it.PackUri);
        }
    }

    private void Item_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _pressed = false;
        (System.Windows.Application.Current.MainWindow as MainWindow)?.EndDragSpawn(handOff:false);
    }

    private void Item_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_pressed || e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not System.Windows.Controls.Image img || img.Tag is not InvItem it) return;
        var pos = e.GetPosition(this);
        // if user moved a bit, keep following; if they leave window bounds, handoff to physics
        if (pos.X < 0 || pos.Y < 0 || pos.X > ActualWidth || pos.Y > ActualHeight)
        {
            _pressed = false;
            (System.Windows.Application.Current.MainWindow as MainWindow)?.EndDragSpawn(handOff:true);
            e.Handled = true;
            return;
        }
    }
}


