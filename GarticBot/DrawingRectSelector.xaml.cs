using System.Windows;
using System.Windows.Input;


namespace GarticBot
{
    public partial class DrawingRectSelector : Window
    {
        public DrawingRectSelector()
        {
            InitializeComponent();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
    }
}
