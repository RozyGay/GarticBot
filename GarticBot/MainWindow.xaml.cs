using AForge.Imaging.ColorReduction;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static GarticBot.Utils;
using Color = System.Drawing.Color;

namespace GarticBot
{
    public partial class MainWindow : Window
    {
        private DrawingRectSelector drawingRectSelector;
        private double x;
        private double y;
        private double w;
        private double h;
        private Bitmap currentImage, originalImage;
        private Settings settings = new(true);
        private Color[] palette;
        private Thread runner;
        public bool RunThread = false;
        public bool SkipColor = false;

        [DllImport("User32.dll")]
        public static extern bool RegisterHotKey(
            [In] IntPtr hWnd,
            [In] int id,
            [In] uint fsModifiers,
            [In] uint vk);

        [DllImport("User32.dll")]
        public static extern bool UnregisterHotKey(
            [In] IntPtr hWnd,
            [In] int id);

        private HwndSource _source;
        private const int CLOSETHREAD_ID = 9000;
        private const int SKIPCOLOR_ID = 9001;

        public MainWindow()
        {
            InitializeComponent();
            x = settings.DrawingPlace.X;
            y = settings.DrawingPlace.Y;
            w = settings.DrawingPlace.Width;
            h = settings.DrawingPlace.Height;
            UpdateTopmostButton();
            CompileOpenCLKernels();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new WindowInteropHelper(this);
            _source = HwndSource.FromHwnd(helper.Handle);
            _source.AddHook(HwndHook);
            RegisterHotKey();
        }

        protected override void OnClosed(EventArgs e)
        {
            _source.RemoveHook(HwndHook);
            _source = null;
            UnregisterHotKey();
            base.OnClosed(e);
        }

        private void RegisterHotKey()
        {
            var helper = new WindowInteropHelper(this);

            if (!RegisterHotKey(helper.Handle, CLOSETHREAD_ID, 0, settings.CloseThreadKeycode) ||
                !RegisterHotKey(helper.Handle, SKIPCOLOR_ID, 0, settings.SkipColorKeycode))
            {
                errorLabel.Text = "Ошибка создания глобальных хоткеев";
            }
        }

        private void UnregisterHotKey()
        {
            var helper = new WindowInteropHelper(this);
            UnregisterHotKey(helper.Handle, CLOSETHREAD_ID);
            UnregisterHotKey(helper.Handle, SKIPCOLOR_ID);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            switch (msg)
            {
                case WM_HOTKEY:
                    switch (wParam.ToInt32())
                    {
                        case CLOSETHREAD_ID:
                            RunThread = false;
                            handled = true;
                            break;
                        case SKIPCOLOR_ID:
                            SkipColor = true;
                            handled = true;
                            break;
                    }
                    break;
            }
            return IntPtr.Zero;
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new("[^0-9]+");
            int curr = 0;
            if (!regex.IsMatch(e.Text)) 
                int.TryParse(ColorCountInput.Text + e.Text, out curr);
            e.Handled = regex.IsMatch(e.Text) || curr > 256;
        }

        private void ContrastSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ContrastLabel.Text = $"контраст ({(int)ContrastSlider.Value})";
            UpdateImage();
        }

        private void SpacingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            SpacingLabel.Text = $"пропуск ({(int)SpacingSlider.Value})";
        }

        private void colorsChangeButton_Click(object sender, RoutedEventArgs e)
        {
            int currentColor = 0;
            if (int.TryParse(ColorCountInput.Text, out currentColor))
            {
                switch (((System.Windows.Controls.Button)sender).Name)
                {
                    case "colorsIncreaseButton":
                        currentColor = currentColor < 256 ? currentColor + 1 : 256;
                        break;
                    case "colorsDecreaseButton":
                        currentColor = currentColor > 2 ? currentColor - 1 : 2;
                        break;
                }
            }

            ColorCountInput.Text = currentColor.ToString();
            UpdateImage();
        }

        private void UpdateTopmostButton()
        {
            Topmost = settings.OnTop;
            var imageBrush = new ImageBrush();
            if (settings.OnTop)
                imageBrush.ImageSource = new BitmapImage(new Uri("pack://application:,,,/Assets/onTopButtonSelected.png"));
            else
                imageBrush.ImageSource = new BitmapImage(new Uri("pack://application:,,,/Assets/onTopButton.png"));
            OnTopButton.Background = imageBrush;
        }

        private void OnTopButton_Click(object sender, RoutedEventArgs e)
        {
            settings.OnTop = !settings.OnTop;
            settings.Save();
            UpdateTopmostButton();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            UnregisterHotKey();
            CoordinateSettings coordinateSettings = new(settings);
            coordinateSettings.ShowDialog();
            RegisterHotKey();
        }

        private void DownloadImageFromURLButton(object sender, RoutedEventArgs e)
        {
            try
            {
                WebClient client = new();
                Stream stream = client.OpenRead(imageUrlTextBox.Text);
                originalImage = new Bitmap(stream);
                UpdateImage();
                errorLabel.Text = "";
            }
            catch (Exception ex)
            {
                errorLabel.Text = "Ошибка загрузки картинки";
                Console.WriteLine(ex);
            }
        }

        private void GetImageFromFIleButton(object sender, RoutedEventArgs e)
        {
            try
            {
                Microsoft.Win32.OpenFileDialog dlg = new()
                {
                    Filter = "Image files (*.jpg, *.jpeg, *.jpe, *.jfif, *.png) | *.jpg; *.jpeg; *.jpe; *.jfif; *.png"
                };

                bool? result = dlg.ShowDialog();

                if (result == true)
                {
                    originalImage = (Bitmap)System.Drawing.Image.FromFile(dlg.FileName);
                    UpdateImage();
                    errorLabel.Text = "";
                }
            }
            catch (Exception ex)
            {
                errorLabel.Text = "Не удалось открыть картинку";
                Console.WriteLine(ex);
            }
        }

        private void GrayScaleCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            UpdateImage();
        }

        private void ColorCountInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateImage();
        }

        private int GetCurrentSpeed()
        {
            if ((bool)SpeedRadio1.IsChecked)
                return 1;
            else if ((bool)SpeedRadio2.IsChecked)
                return 2;
            else if ((bool)SpeedRadio3.IsChecked)
                return 3;
            else
                return 4;
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            int gap = (int)SpacingSlider.Value;
            int speed = GetCurrentSpeed();
            bool drawRect = (bool)drawWithRectCheckbox.IsChecked;
            Bitmap img = (Bitmap)currentImage.Clone();
            runner = new Thread(() =>
            {
                RunThread = true;
                StartDraw(img, settings, gap, speed, (int)x, (int)y, drawRect);
            });
            runner.Start();
        }

        private void GetImageFromClipboardButton(object sender, RoutedEventArgs e)
        {
            try
            {
                if (System.Windows.Clipboard.ContainsImage())
                {
                    originalImage = BitmapFromSource(System.Windows.Clipboard.GetImage());
                    UpdateImage();
                    errorLabel.Text = "";
                }
                else
                {
                    errorLabel.Text = "В буфере обмена нету картинки";
                }
            }
            catch (Exception ex)
            {
                errorLabel.Text = "Ошибка загрузки картинки";
                Console.WriteLine(ex);
            }
        }

        private void DrawRectSelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (drawingRectSelector != null)
            {
                x = drawingRectSelector.Left;
                y = drawingRectSelector.Top;
                w = drawingRectSelector.Width;
                h = drawingRectSelector.Height;
                drawingRectSelector.Close();
                drawingRectSelector = null;
                errorLabel.Text = "";
                settings.DrawingPlace = new Rectangle((int)x, (int)y, (int)w, (int)h);
                settings.Save();
                if (currentImage != null)
                    UpdateImage();
            }
            else
            {
                drawingRectSelector = new DrawingRectSelector();
                drawingRectSelector.Top = settings.DrawingPlace.Y;
                drawingRectSelector.Left = settings.DrawingPlace.X;
                drawingRectSelector.Width = settings.DrawingPlace.Width;
                drawingRectSelector.Height = settings.DrawingPlace.Height;
                drawingRectSelector.Show();
                errorLabel.Text = "Переместите окно и нажмите кнопку ещё раз.";
            }

        }

        public void DrawLine(Rectangle coordinates, int speed, bool drawRect)
        {
            SetMousePos(coordinates.Location);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
            
            int posX = coordinates.X + coordinates.Width;
            int posY = coordinates.Y + coordinates.Height;
            
            if (drawRect)
                Thread.Sleep(5);
            
            SetMousePos(new System.Drawing.Point(posX, posY));
            
            if (!drawRect)
                Thread.Sleep(coordinates.Width > 0 || coordinates.Height > 0 ? speed : speed / 2);
            else
                Thread.Sleep(speed * 2 / 3);

            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        }

        private void imageSizeCombobox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateImage();
        }

        public void SelectColor(Color color, Settings settings)
        {
            SetMousePos(settings.OpenPalette);
            Mouse_Click();
            SetMousePos(settings.RedValue);
            Thread.Sleep(100);
            Mouse_Click();
            Type(color.R.ToString());
            SetMousePos(settings.GreenValue);
            Thread.Sleep(100);
            Mouse_Click();
            Type(color.G.ToString());
            SetMousePos(settings.BlueValue);
            Thread.Sleep(100);
            Mouse_Click();
            Type(color.B.ToString());
            SetMousePos(settings.EmptySpace);
            Thread.Sleep(100);
            Mouse_Click();
        }

        public void StartDraw(Bitmap image, Settings settings, int gapSize, int speed, int x, int y, bool rectDraw)
        {
            Dictionary<Color, List<Rectangle>> pixelLinesToDraw;

            if (!rectDraw)
            {
                pixelLinesToDraw = ExtractPixelLinesToDraw(image, gapSize, x, y);
            }
            else
            {
                pixelLinesToDraw = ExtractRectsToDraw(image, x, y);
            }

            uint done = 0;
            foreach (var line in pixelLinesToDraw)
            {
                if (line.Key != Color.FromArgb(0, 0, 0))
                {
                    if (line.Key.GetBrightness() < 0.95)
                    {
                        SkipColor = false;

                        SelectColor(line.Key, settings);
                        foreach (var j in line.Value)
                        {
                            DrawLine(j, ConvertSpeed(speed), rectDraw);

                            if (!RunThread) { return; }
                            if (SkipColor) break;
                        }

                        workProgressBar.Dispatcher.Invoke(() => { workProgressBar.Value = 100.0 * ((float)done / pixelLinesToDraw.Count); });
                    }
                    done++;
                }

                if (!rectDraw)
                {
                    if (speed == 1) Thread.Sleep(100);
                    else if (speed == 2) Thread.Sleep(50);
                    else if (speed == 3) Thread.Sleep(25);
                    else if (speed == 4) Thread.Sleep(10);
                }
                else
                {
                    if (speed == 1) Thread.Sleep(80);
                    else if (speed == 2) Thread.Sleep(60);
                    else if (speed == 3) Thread.Sleep(30);
                    else if (speed == 4) Thread.Sleep(15);
                }

            }
            if (pixelLinesToDraw.ContainsKey(Color.FromArgb(0, 0, 0)))
            {
                SkipColor = false;

                SelectColor(Color.Black, settings);
                foreach (var j in pixelLinesToDraw[Color.FromArgb(0, 0, 0)])
                {
                    DrawLine(j, ConvertSpeed(speed), rectDraw);

                    if (!RunThread) { return; }
                    if (SkipColor) break;
                }
                done++;

                workProgressBar.Dispatcher.Invoke(() => { workProgressBar.Value = 100.0 * ((float)done / pixelLinesToDraw.Count); });
            }
        }


        private void UpdateImage()
        {
            if (originalImage != null)
            {
                bool grayscaleFlag = (bool)GrayScaleCheckbox.IsChecked;
                string paletteCount = ColorCountInput.Text;
                int resizeModeFlag = imageSizeCombobox.SelectedIndex;
                double contrastValue = ContrastSlider.Value;

                currentImage?.Dispose();
                currentImage = (Bitmap)originalImage.Clone();

                Bitmap temp = currentImage;
                if (grayscaleFlag)
                {
                    currentImage = ToGrayScale(temp);
                    temp.Dispose();
                    temp = currentImage;
                }

                ColorImageQuantizer ciq = new(new MedianCutQuantizer());
                int colCount = TryParse(paletteCount);
                if (colCount > 256) colCount = 256;
                else if (colCount < 2) colCount = 2;
                currentImage = ciq.ReduceColors(temp, colCount);
                if (temp != currentImage) temp.Dispose();
                palette = currentImage.Palette.Entries;

                switch (resizeModeFlag)
                {
                    case 0:
                        if (currentImage.Width > settings.DrawingPlace.Width || currentImage.Height > settings.DrawingPlace.Height)
                        {
                            temp = currentImage;
                            currentImage = ResizeImageAspect(temp, settings.DrawingPlace.Width, settings.DrawingPlace.Height);
                            temp.Dispose();
                        }
                        break;
                    case 1:
                        temp = currentImage;
                        currentImage = ResizeImageAspect(temp, settings.DrawingPlace.Width, settings.DrawingPlace.Height);
                        temp.Dispose();
                        break;
                    case 2:
                        temp = currentImage;
                        currentImage = ResizeImage(temp, settings.DrawingPlace.Width, settings.DrawingPlace.Height);
                        temp.Dispose();
                        break;
                    case 3:
                        if (currentImage.Width > settings.DrawingPlace.Width || currentImage.Height > settings.DrawingPlace.Height)
                        {
                            temp = currentImage;
                            currentImage = ResizeImageAspect(temp, settings.DrawingPlace.Width, settings.DrawingPlace.Height);
                            temp.Dispose();
                        }
                        Bitmap centered = new(settings.DrawingPlace.Width, settings.DrawingPlace.Height);
                        using (Graphics gfx = Graphics.FromImage(centered))
                        {
                            gfx.FillRectangle(System.Drawing.Brushes.White, 0, 0, settings.DrawingPlace.Width, settings.DrawingPlace.Height);
                            gfx.DrawImage(currentImage, (settings.DrawingPlace.Width / 2) - (currentImage.Width / 2), (settings.DrawingPlace.Height / 2) - (currentImage.Height / 2));
                        }
                        currentImage.Dispose();
                        currentImage = centered;
                        break;
                }

                if (contrastValue != 0)
                {
                    temp = currentImage;
                    currentImage = AdjustContrast(temp, contrastValue);
                    temp.Dispose();
                }

                previewImage.Dispatcher.Invoke(() => { previewImage.Source = BitmapToBitmapSource(currentImage); });
            }
        }
    }
}
