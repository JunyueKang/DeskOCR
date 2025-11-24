using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Cursors = System.Windows.Input.Cursors;

namespace OcrApp.UI
{
    public class ScreenSelectionWindow : Window, IDisposable
    {
        private bool isSelecting = false;
        private System.Windows.Point startPoint;
        private System.Windows.Shapes.Rectangle selectionRectangle = null!;
        private Canvas canvas = null!;
        private System.Windows.Controls.Image backgroundImage = null!;
        private System.Windows.Shapes.Rectangle overlay = null!;
        private double dpiScale;
        private bool disposed = false;

        public Rect SelectedRect { get; private set; }
        
        // 添加IsDisposed属性
        public bool IsDisposed => disposed;

        public ScreenSelectionWindow(System.Drawing.Bitmap screenshot)
        {
            InitializeWindow();
            SetupScreenshot(screenshot);
        }

        private void InitializeWindow()
        {
            this.WindowStyle = WindowStyle.None;
            this.WindowState = WindowState.Normal;
            this.AllowsTransparency = true;
            this.Background = new SolidColorBrush(Colors.Transparent);
            this.Cursor = Cursors.Cross;
            this.Topmost = true;
            this.WindowStartupLocation = WindowStartupLocation.Manual;
            this.ShowInTaskbar = false;

            dpiScale = GetDpiScale();

            this.Left = SystemParameters.VirtualScreenLeft;
            this.Top = SystemParameters.VirtualScreenTop;
            this.Width = SystemParameters.VirtualScreenWidth;
            this.Height = SystemParameters.VirtualScreenHeight;

            canvas = new Canvas();
            this.Content = canvas;

            // 初始化UI元素
            backgroundImage = new System.Windows.Controls.Image
            {
                Stretch = Stretch.None,
                Width = SystemParameters.VirtualScreenWidth,
                Height = SystemParameters.VirtualScreenHeight
            };
            Canvas.SetLeft(backgroundImage, 0);
            Canvas.SetTop(backgroundImage, 0);
            canvas.Children.Add(backgroundImage);

            // 添加半透明灰色遮罩
            overlay = new System.Windows.Shapes.Rectangle
            {
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 10, 0, 0)),
                Width = this.Width,
                Height = this.Height
            };
            Canvas.SetLeft(overlay, 0);
            Canvas.SetTop(overlay, 0);
            canvas.Children.Add(overlay);

            selectionRectangle = new System.Windows.Shapes.Rectangle
            {
                Stroke = System.Windows.Media.Brushes.Red,
                StrokeThickness = 2,
                Fill = System.Windows.Media.Brushes.Transparent,
                Visibility = Visibility.Hidden
            };
            canvas.Children.Add(selectionRectangle);

            // 绑定事件
            this.MouseLeftButtonDown += OnMouseLeftButtonDown;
            this.MouseMove += OnMouseMove;
            this.MouseLeftButtonUp += OnMouseLeftButtonUp;
            this.KeyDown += OnKeyDown;
        }

        private void SetupScreenshot(System.Drawing.Bitmap screenshot)
        {
            if (screenshot != null)
            {
                BitmapImage bitmapImage = ConvertBitmapToBitmapImage(screenshot);
                backgroundImage.Source = bitmapImage;
            }
        }

        // 添加更新截图的方法
        public void UpdateScreenshot(System.Drawing.Bitmap screenshot)
        {
            if (disposed) return;
            
            try
            {
                SetupScreenshot(screenshot);
                
                // 重置选择状态
                isSelecting = false;
                selectionRectangle.Visibility = Visibility.Hidden;
                
                // 重置遮罩
                overlay.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 10, 0, 0));
                overlay.OpacityMask = null;
                
                // 显示窗口
                this.Visibility = Visibility.Visible;
            }
            catch (Exception)
            {
                // 更新失败时忽略错误
            }
        }

        private BitmapImage ConvertBitmapToBitmapImage(System.Drawing.Bitmap bitmap)
        {
            using (var memory = new System.IO.MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                memory.Position = 0;

                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                return bitmapImage;
            }
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            isSelecting = true;
            startPoint = e.GetPosition(this);
            CaptureMouse();
            selectionRectangle.Visibility = Visibility.Visible;

            Canvas.SetLeft(selectionRectangle, startPoint.X);
            Canvas.SetTop(selectionRectangle, startPoint.Y);
            selectionRectangle.Width = 0;
            selectionRectangle.Height = 0;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (isSelecting)
            {
                System.Windows.Point currentPoint = e.GetPosition(this);

                var x = Math.Min(currentPoint.X, startPoint.X);
                var y = Math.Min(currentPoint.Y, startPoint.Y);
                var width = Math.Abs(currentPoint.X - startPoint.X);
                var height = Math.Abs(currentPoint.Y - startPoint.Y);

                Canvas.SetLeft(selectionRectangle, x);
                Canvas.SetTop(selectionRectangle, y);
                selectionRectangle.Width = width;
                selectionRectangle.Height = height;

                UpdateOverlay(x, y, width, height);
            }
        }

        private void UpdateOverlay(double x, double y, double width, double height)
        {
            var geometry = new GeometryGroup();
            geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, this.Width, this.Height)));
            geometry.Children.Add(new RectangleGeometry(new Rect(x, y, width, height)));
            overlay.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 10, 0, 0));
            overlay.OpacityMask = new VisualBrush(new Border
            {
                Width = this.Width,
                Height = this.Height,
                Background = System.Windows.Media.Brushes.Black,
                OpacityMask = new DrawingBrush(new GeometryDrawing(System.Windows.Media.Brushes.White, null, geometry))
            });
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                // 如果按下 ESC 键，取消选择并关闭窗口
                this.DialogResult = false;
                this.Close();
            }
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isSelecting)
            {
                isSelecting = false;
                ReleaseMouseCapture();

                System.Windows.Point endPoint = e.GetPosition(this);

                double x = Math.Min(startPoint.X, endPoint.X);
                double y = Math.Min(startPoint.Y, endPoint.Y);
                double width = Math.Abs(endPoint.X - startPoint.X);
                double height = Math.Abs(endPoint.Y - startPoint.Y);

                SelectedRect = new Rect(
                    x + SystemParameters.VirtualScreenLeft,
                    y + SystemParameters.VirtualScreenTop,
                    width,
                    height);

                // 只有当选择区域有效时才设置 DialogResult 为 true
                if (width > 0 && height > 0)
                {
                    this.DialogResult = true;
                }
                else
                {
                    this.DialogResult = false;
                }
                this.Close();
            }
        }

        private double GetDpiScale()
        {
            PresentationSource source = PresentationSource.FromVisual(this);
            return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        }

        protected override void OnClosed(EventArgs e)
        {
            Dispose(true);
            base.OnClosed(e);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // 清理托管资源
                    if (backgroundImage != null)
                    {
                        backgroundImage.Source = null;
                        backgroundImage = null!;
                    }
                    
                    if (canvas != null)
                    {
                        canvas.Children.Clear();
                        canvas = null!;
                    }
                    
                    if (selectionRectangle != null)
                    {
                        selectionRectangle = null!;
                    }
                    
                    if (overlay != null)
                    {
                        overlay = null!;
                    }
                }
                
                disposed = true;
            }
        }

        ~ScreenSelectionWindow()
        {
            Dispose(false);
        }
    }
}