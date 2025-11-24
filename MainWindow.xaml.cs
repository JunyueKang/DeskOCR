using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Extensions.Configuration;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using PaddleOCR_CSharp;
using OcrApp.UI;
using OcrApp.Services;


namespace OcrApp
{
    public partial class MainWindow : Window, IDisposable
    {
        private OCRManager ocrManager;
        

        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const int HOTKEY_ID = 9000;

        private uint currentModifiers = MOD_CONTROL;
        private uint currentKey = (uint)KeyInterop.VirtualKeyFromKey(Key.L);

        private IConfiguration Configuration { get; set; } = null!;
        private NotifyIcon trayIcon = null!;
        private ResultWindow? resultWindow;
        private SelectionResultWindow? selectionResultWindow;
        private double resultWindowFontSize = 12; // 默认字体大小
        private string ocrMode = "Classic"; // 默认为经典模式

        private Key copyOriginalKey = Key.C;
        private Key closeKey = Key.D;
        private Key selectionClearKey = Key.X;
        private Key selectionSelectAllKey = Key.A;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("gdi32.dll")]
        static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        private const int DESKTOPHORZRES = 118;
        private const int DESKTOPVERTRES = 117;

        private bool disposed = false;
        private DateTime lastResumeTime = DateTime.MinValue;

        
        private readonly object screenshotLock = new object();

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                InitializeConfiguration();

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string modelDir = System.IO.Path.Combine(baseDir, "Resources", "model");
                string detectionModelPath = System.IO.Path.Combine(modelDir, "det_model.onnx");
                string recognitionModelPath = System.IO.Path.Combine(modelDir, "rec_model.onnx");
                string recLabelPath = System.IO.Path.Combine(modelDir, "ppocr_keys_v1.txt");
                string layoutLabelPath = System.IO.Path.Combine(modelDir, "layout_publaynet_dict.txt");
                string tableLabelPath = System.IO.Path.Combine(modelDir, "table_structure_dict_ch.txt");

                ocrManager = new OCRManager(detectionModelPath, recognitionModelPath, recLabelPath, layoutLabelPath, tableLabelPath);

                LoadSettings();

                // 预创建ResultWindow实例
                resultWindow = new ResultWindow(
                    fontSize: resultWindowFontSize,
                    copyOriginalKey: copyOriginalKey,
                    closeKey: closeKey
                );

                this.SourceInitialized += (s, e) => 
                {
                    IntPtr handle = new WindowInteropHelper(this).Handle;
                    HwndSource.FromHwnd(handle).AddHook(HwndHook);
                    RegisterCurrentHotkey();
                };

                this.ShowInTaskbar = true;
                this.WindowState = WindowState.Minimized;
                this.Visibility = Visibility.Visible;

                CreateTrayIcon();

                // 确保窗口在加载后获取焦点以接收 KeyDown 事件
                this.Loaded += (s, e) => Keyboard.Focus(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(OcrApp.Services.LocalizationService.Get("MessageInitFailedPrefix") + ex.Message + "\n\n" + ex.StackTrace, OcrApp.Services.LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }
        }

        private void InitializeConfiguration()
        {
            try
            {
                var builder = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

                Configuration = builder.Build();
                
                // 加载快捷键设置
                LoadHotkeySettings();
            }
            catch (Exception ex)
            {
                throw new Exception("无法加载配置文件", ex);
            }
        }

        private void LoadHotkeySettings()
        {
            try
            {
                // 从配置文件加载快捷键设置（支持 Alt/Ctrl/Shift/Win 组合）
                copyOriginalKey = ParseKeyFromConfig(Configuration["CopyOriginalKey"], Key.C);
                closeKey = ParseKeyFromConfig(Configuration["CloseKey"], Key.D);
                selectionClearKey = ParseKeyFromConfig(Configuration["SelectionClearKey"], Key.X);
                selectionSelectAllKey = ParseKeyFromConfig(Configuration["SelectionSelectAllKey"], Key.A);

                if (CopyOriginalHotkeyTextBox != null) CopyOriginalHotkeyTextBox.Text = Configuration["CopyOriginalKey"] ?? $"Alt+{copyOriginalKey}";
                if (CloseHotkeyTextBox != null) CloseHotkeyTextBox.Text = Configuration["CloseKey"] ?? $"Alt+{closeKey}";
                if (SelectionClearHotkeyTextBox != null) SelectionClearHotkeyTextBox.Text = Configuration["SelectionClearKey"] ?? $"Alt+{selectionClearKey}";
                if (SelectionSelectAllHotkeyTextBox != null) SelectionSelectAllHotkeyTextBox.Text = Configuration["SelectionSelectAllKey"] ?? $"Alt+{selectionSelectAllKey}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(OcrApp.Services.LocalizationService.Get("MessageLoadHotkeySettingsErrorPrefix") + ex.Message, OcrApp.Services.LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static Key ParseKeyFromConfig(string? value, Key defaultKey)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(value)) return defaultKey;
                var s = value.Trim();
                if (s.Contains("+"))
                {
                    var parts = s.Split('+');
                    var last = parts[parts.Length - 1].Trim();
                    if (Enum.TryParse(last, true, out Key k)) return k;
                    return defaultKey;
                }
                if (Enum.TryParse(s, true, out Key k2)) return k2;
                return defaultKey;
            }
            catch { return defaultKey; }
        }

        

        private void LoadSettings()
        {
            try
            {
                string? hotkey = Configuration["Hotkey"];
                string? fontSizeStr = Configuration["ResultWindowFontSize"];
                string? mode = Configuration["OCRMode"];
                
                // 加载热键设置
                if (!string.IsNullOrEmpty(hotkey))
                {
                    ParseHotkey(hotkey);
                    if (HotkeyTextBox != null)
                    {
                        HotkeyTextBox.Text = hotkey;
                    }
                }

                // 加载字体大小设置
                if (!string.IsNullOrEmpty(fontSizeStr) && double.TryParse(fontSizeStr, out double parsedFontSize))
                {
                    resultWindowFontSize = parsedFontSize;
                    if (FontSizeComboBox != null)
                    {
                        bool found = false;
                        foreach (ComboBoxItem item in FontSizeComboBox.Items)
                        {
                            if ((item.Content?.ToString() ?? "") == fontSizeStr)
                            {
                                FontSizeComboBox.SelectedItem = item;
                                found = true;
                                break;
                            }
                        }
                        if (!found)
                        {
                            var newItem = new ComboBoxItem { Content = fontSizeStr };
                            FontSizeComboBox.Items.Add(newItem);
                            FontSizeComboBox.SelectedItem = newItem;
                        }
                    }
                }

                // 加载OCR模式设置
                if (!string.IsNullOrEmpty(mode))
                {
                    ocrMode = mode;
                }
                else
                {
                    ocrMode = "Classic"; // 默认为经典模式
                }

                // 设置ComboBox的选中项
                if (OCRModeComboBox != null)
                {
                    foreach (ComboBoxItem item in OCRModeComboBox.Items)
                    {
                        if (item.Tag?.ToString() == ocrMode)
                        {
                            OCRModeComboBox.SelectedItem = item;
                            break;
                        }
                    }
                }

                var urls = Configuration.GetSection("TranslationUrls").GetChildren().Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
                if (Extension1PluginComboBox != null)
                {
                    Extension1PluginComboBox.ItemsSource = urls;
                    int preferredIndex1 = 0;
                    if (int.TryParse(Configuration["Extension1PreferredIndex"], out int idx1)) preferredIndex1 = idx1;
                    else if (int.TryParse(Configuration["TranslationPreferredIndex"], out int tidx)) preferredIndex1 = tidx;
                    if (preferredIndex1 < 0 || preferredIndex1 >= urls.Count) preferredIndex1 = 0;
                    Extension1PluginComboBox.SelectedIndex = preferredIndex1;
                }
                if (Extension2PluginComboBox != null)
                {
                    Extension2PluginComboBox.ItemsSource = urls;
                    int preferredIndex2 = 0;
                    if (int.TryParse(Configuration["Extension2PreferredIndex"], out int idx2)) preferredIndex2 = idx2;
                    if (preferredIndex2 < 0 || preferredIndex2 >= urls.Count) preferredIndex2 = 0;
                    Extension2PluginComboBox.SelectedIndex = preferredIndex2;
                }

                if (Extension1NameTextBox != null)
                {
                    Extension1NameTextBox.Text = Configuration["Extension1Name"] ?? "扩展一";
                }
                if (Extension2NameTextBox != null)
                {
                    Extension2NameTextBox.Text = Configuration["Extension2Name"] ?? "扩展二";
                }
                if (Extension1HotkeyTextBox != null)
                {
                    Extension1HotkeyTextBox.Text = Configuration["Extension1Hotkey"] ?? "Alt+E";
                }
                if (Extension2HotkeyTextBox != null)
                {
                    Extension2HotkeyTextBox.Text = Configuration["Extension2Hotkey"] ?? "Alt+R";
                }

                if (Extension1EnabledCheckBox != null)
                {
                    bool e1 = true; var s1 = Configuration["Extension1Enabled"]; if (s1 != null && bool.TryParse(s1, out var v1)) e1 = v1; Extension1EnabledCheckBox.IsChecked = e1;
                }
                if (Extension2EnabledCheckBox != null)
                {
                    bool e2 = true; var s2 = Configuration["Extension2Enabled"]; if (s2 != null && bool.TryParse(s2, out var v2)) e2 = v2; Extension2EnabledCheckBox.IsChecked = e2;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载设置时发生错误: {ex.Message}", LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ParseAndValidateHotkey(string hotkeyText, out Key key)
        {
            key = Key.None;
            string[] parts = hotkeyText.Split('+');
            if (parts.Length < 2)
            {
                MessageBox.Show(OcrApp.Services.LocalizationService.Get("InvalidHotkeyFormatPrefix") + hotkeyText + "\n" + OcrApp.Services.LocalizationService.Get("HotkeyFormatHint"), OcrApp.Services.LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string modifier = parts[i].Trim().ToLower();
                if (modifier != "alt" && modifier != "ctrl" && modifier != "control" && modifier != "shift" && modifier != "win" && modifier != "windows")
                {
                    MessageBox.Show(OcrApp.Services.LocalizationService.Get("InvalidModifierPrefix") + parts[i], OcrApp.Services.LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }
            if (!Enum.TryParse(parts[parts.Length - 1].Trim(), true, out key))
            {
                MessageBox.Show(OcrApp.Services.LocalizationService.Get("InvalidKeyPrefix") + parts[parts.Length - 1], OcrApp.Services.LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            return true;
        }

        private bool ParseAndValidateHotkeyRequireAlt(string hotkeyText, out Key key)
        {
            key = Key.None;
            string[] parts = hotkeyText.Split('+');
            if (parts.Length < 2)
            {
                MessageBox.Show(OcrApp.Services.LocalizationService.Get("InvalidHotkeyFormatPrefix") + hotkeyText, OcrApp.Services.LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            bool hasAlt = false;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string modifier = parts[i].Trim().ToLower();
                if (modifier == "alt") hasAlt = true;
                else if (modifier != "ctrl" && modifier != "control" && modifier != "shift")
                {
                    MessageBox.Show(OcrApp.Services.LocalizationService.Get("InvalidModifierPrefix") + parts[i], OcrApp.Services.LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }
            if (!hasAlt)
            {
                MessageBox.Show(OcrApp.Services.LocalizationService.Get("RequireAltPrefix") + hotkeyText, OcrApp.Services.LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            if (!Enum.TryParse(parts[parts.Length - 1].Trim(), true, out key))
            {
                MessageBox.Show(OcrApp.Services.LocalizationService.Get("InvalidKeyPrefix") + parts[parts.Length - 1], OcrApp.Services.LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            return true;
        }

        private void ParseHotkey(string hotkeyText)
        {
            try
            {
                string[] parts = hotkeyText.Split('+');
                if (parts.Length >= 2)
                {
                    // 解析修饰键
                    currentModifiers = 0;
                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        string modifier = parts[i].Trim().ToLower();
                        switch (modifier)
                        {
                            case "ctrl":
                            case "control":
                                currentModifiers |= MOD_CONTROL;
                                break;
                            case "alt":
                                currentModifiers |= MOD_ALT;
                                break;
                            case "shift":
                                currentModifiers |= MOD_SHIFT;
                                break;
                            case "win":
                            case "windows":
                                currentModifiers |= MOD_WIN;
                                break;
                        }
                    }

                    // 解析主键
                    string keyStr = parts[parts.Length - 1].Trim();
                    if (Enum.TryParse(keyStr, true, out Key key))
                    {
                        currentKey = (uint)KeyInterop.VirtualKeyFromKey(key);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(OcrApp.Services.LocalizationService.Get("ParseHotkeyErrorPrefix") + ex.Message, OcrApp.Services.LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY)
            {
                if (wParam.ToInt32() == HOTKEY_ID)
                {
                    // 如果 ResultWindow 已打开，则先隐藏它而不是关闭
                    if (resultWindow != null && resultWindow.IsVisible)
                    {
                        resultWindow.Hide();
                    }

                    // 使用 BeginInvoke 确保 CaptureScreenshot 在隐藏窗口后执行
                    Dispatcher.BeginInvoke(new Action(() => CaptureScreenshot()));
                    handled = true;
                }
            }
            return IntPtr.Zero;
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
                    // 释放托管资源
                    if (ocrManager != null)
                    {
                        ocrManager.Dispose();
                        ocrManager = null!;
                    }
                    
                    if (trayIcon != null)
                    {
                        trayIcon.Dispose();
                        trayIcon = null!;
                    }
                    
                    if (selectionResultWindow != null)
                    {
                        selectionResultWindow.Dispose();
                        selectionResultWindow = null!;
                    }
                    
                    // 注销热键
                    IntPtr handle = new WindowInteropHelper(this).Handle;
                    UnregisterHotKey(handle, HOTKEY_ID);
                    
                   
                }
                
                disposed = true;
            }
        }

        ~MainWindow()
        {
            Dispose(false);
        }

        // 移除 Window_KeyDown 事件处理器，避免重复处理
        // 如果仍需要在某些情况下使用 Window_KeyDown，可以保留并确保不会与 HwndHook 冲突

        private async void CaptureScreenshot()
        {
            try
            {
                System.Drawing.Bitmap screenshot;
                
                lock (screenshotLock)
                {
                    screenshot = CaptureFullScreen();
                }

                
                // 创建新窗口
                ScreenSelectionWindow selectionWindow;
                selectionWindow = new ScreenSelectionWindow(screenshot);
                bool? result = selectionWindow.ShowDialog();
                if (result == true)
                {
                    Rect selectedRect = selectionWindow.SelectedRect;
                    
                    if (selectedRect.Width > 0 && selectedRect.Height > 0)
                    {
                        using (System.Drawing.Bitmap croppedImage = CropImage(screenshot, selectedRect))
                        {
                            var ocrResults = await Task.Run(() => 
                            {
                                return ocrManager.PerformOCR(croppedImage);
                            });
                            await ProcessOCRResults(ocrResults);
                        }
                    }
                }
                
                screenshot?.Dispose();
                selectionWindow?.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(OcrApp.Services.LocalizationService.Get("ScreenshotErrorPrefix") + ex.Message, OcrApp.Services.LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ProcessOCRResults(List<OCRResult> ocrResults)
        {
            if (ocrResults != null && ocrResults.Count > 0)
            {
                string resultText = string.Join("\n", ocrResults.Select(r => r.Text));
                
                if (ocrMode == "Silent")
                {
                    // 静默模式：直接复制到剪贴板，不显示ResultWindow
                    await Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            if (!ClipboardService.TrySetText(resultText, this))
                            {
                                MessageBox.Show(OcrApp.Services.LocalizationService.Get("CopyToClipboardFailed"), OcrApp.Services.LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(OcrApp.Services.LocalizationService.Get("CopyToClipboardFailedPrefix") + ex.Message, OcrApp.Services.LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    });
                }
                else if (ocrMode == "Selection")
                {
                    // 选择模式：显示SelectionResultWindow
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (selectionResultWindow == null)
                        {
                            selectionResultWindow = new SelectionResultWindow();
                        }
                        selectionResultWindow.ShowWithOCRResult(resultText);
                    });
                }
                else
                {
                    // 经典模式：显示ResultWindow
                    await Dispatcher.InvokeAsync(() =>
                    {
                        // 使用预创建的resultWindow实例
                        resultWindow?.Show();
                        resultWindow?.UpdateResult(resultText);
                    });
                }
            }
            else
            {
                string errorMessage = string.IsNullOrEmpty(ocrManager.LastErrorMessage)
                    ? OcrApp.Services.LocalizationService.Get("MessageOcrUnavailable")
                    : OcrApp.Services.LocalizationService.Get("MessageOcrUnavailable") + "\n" + OcrApp.Services.LocalizationService.Get("ErrorPrefix") + ocrManager.LastErrorMessage;
                await ShowOCRError(new Exception(errorMessage));
            }
        }
        

        private async Task ShowOCRError(Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                // 使用预创建的resultWindow实例显示错误
                resultWindow?.Show();
                resultWindow?.ShowError(OcrApp.Services.LocalizationService.Get("MessageOcrProcessFailedPrefix") + ex.Message);
            });
        }

        
        private Bitmap CaptureFullScreen()
        {
            IntPtr desktop = GetDC(IntPtr.Zero);
            int screenWidth = GetDeviceCaps(desktop, DESKTOPHORZRES);
            int screenHeight = GetDeviceCaps(desktop, DESKTOPVERTRES);
            ReleaseDC(IntPtr.Zero, desktop);

            Bitmap bmp = new Bitmap(screenWidth, screenHeight, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(0, 0, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }

        private Bitmap CropImage(Bitmap source, Rect cropArea)
        {
            // 获取DPI缩放比例
            double dpiScaleX = GetDpiScaleX();
            double dpiScaleY = GetDpiScaleY();
            
            // 将逻辑坐标转换为物理坐标
            Rectangle cropRect = new Rectangle(
                (int)((cropArea.X - SystemParameters.VirtualScreenLeft) * dpiScaleX),
                (int)((cropArea.Y - SystemParameters.VirtualScreenTop) * dpiScaleY),
                (int)(cropArea.Width * dpiScaleX),
                (int)(cropArea.Height * dpiScaleY)
            );

            // 边界检查，确保裁剪区域在源图像范围内
            cropRect.X = Math.Max(0, Math.Min(cropRect.X, source.Width - 1));
            cropRect.Y = Math.Max(0, Math.Min(cropRect.Y, source.Height - 1));
            cropRect.Width = Math.Min(cropRect.Width, source.Width - cropRect.X);
            cropRect.Height = Math.Min(cropRect.Height, source.Height - cropRect.Y);

            if (cropRect.Width <= 0 || cropRect.Height <= 0)
            {
                throw new ArgumentException("Invalid crop area");
            }

            // 使用Clone方法进行高效裁剪，避免Graphics.DrawImage的开销
            return source.Clone(cropRect, source.PixelFormat);
        }

        // 获取DPI缩放比例的辅助方法
        private double GetDpiScaleX()
        {
            IntPtr desktop = GetDC(IntPtr.Zero);
            int logicalScreenWidth = (int)SystemParameters.VirtualScreenWidth;
            int physicalScreenWidth = GetDeviceCaps(desktop, DESKTOPHORZRES);
            ReleaseDC(IntPtr.Zero, desktop);
            return (double)physicalScreenWidth / logicalScreenWidth;
        }

        private double GetDpiScaleY()
        {
            IntPtr desktop = GetDC(IntPtr.Zero);
            int logicalScreenHeight = (int)SystemParameters.VirtualScreenHeight;
            int physicalScreenHeight = GetDeviceCaps(desktop, DESKTOPVERTRES);
            ReleaseDC(IntPtr.Zero, desktop);
            return (double)physicalScreenHeight / logicalScreenHeight;
        }

        private void CreateTrayIcon()
        {
            trayIcon = new NotifyIcon();
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
            if (System.IO.File.Exists(iconPath))
            {
                trayIcon.Icon = new Icon(iconPath);
            }
            else
            {
                trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
            }
            trayIcon.Text = OcrApp.Services.LocalizationService.Get("TrayTooltip");
            trayIcon.Visible = true;

            trayIcon.ContextMenuStrip = new ContextMenuStrip();
            trayIcon.ContextMenuStrip.Items.Add(OcrApp.Services.LocalizationService.Get("TraySettings"), null, (s, e) => { ShowSettings(); });
            trayIcon.ContextMenuStrip.Items.Add(OcrApp.Services.LocalizationService.Get("TrayExit"), null, (s, e) => { Application.Current.Shutdown(); });

            trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ShowSettings();
                }
            };
        }

        private void ShowSettings()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
            this.Focus();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }

        private bool RegisterCurrentHotkey()
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                // 先注销之前的热键
                UnregisterHotKey(handle, HOTKEY_ID);
                // 注册新的热键
                bool success = RegisterHotKey(handle, HOTKEY_ID, currentModifiers, currentKey);
                return success;
            }
            catch (Exception ex)
            {
                MessageBox.Show(OcrApp.Services.LocalizationService.Get("RegisterHotkeyErrorPrefix") + ex.Message, OcrApp.Services.LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 更新TextBox的文本
            CopyOriginalHotkeyTextBox.Text = $"Alt+{copyOriginalKey}";
            CloseHotkeyTextBox.Text = $"Alt+{closeKey}";
            if (SelectionClearHotkeyTextBox != null) SelectionClearHotkeyTextBox.Text = Configuration["SelectionClearKey"] ?? $"Alt+{selectionClearKey}";
            if (SelectionSelectAllHotkeyTextBox != null) SelectionSelectAllHotkeyTextBox.Text = Configuration["SelectionSelectAllKey"] ?? $"Alt+{selectionSelectAllKey}";

            var lang = Configuration["InterfaceLanguage"] ?? "zh-CN";
            OcrApp.Services.LocalizationService.SetLanguage(lang);
            if (LanguageComboBox != null)
            {
                foreach (ComboBoxItem item in LanguageComboBox.Items)
                {
                    var tag = item.Tag != null ? item.Tag.ToString() : null;
                    if ((tag ?? "zh-CN").Equals(lang, StringComparison.OrdinalIgnoreCase))
                    {
                        LanguageComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
            ApplyLocalization();
        }

        private void HotkeyInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var tb = sender as System.Windows.Controls.TextBox;
            if (tb == null) return;
            var mods = Keyboard.Modifiers;
            if (mods == ModifierKeys.None) { e.Handled = true; return; }
            Key key = (mods & ModifierKeys.Alt) != 0 && e.SystemKey != Key.None ? e.SystemKey : e.Key;
            if (IsModifierKey(key)) { e.Handled = true; return; }
            tb.Text = FormatHotkey(mods, key);
            e.Handled = true;
        }

        private static bool IsModifierKey(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftShift || key == Key.RightShift || key == Key.System;
        }

        private static string FormatHotkey(ModifierKeys mods, Key key)
        {
            var parts = new System.Collections.Generic.List<string>();
            if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");
            parts.Add(key.ToString());
            return string.Join("+", parts);
        }

        private void HotkeyInput_GotFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            var tb = sender as System.Windows.Controls.TextBox;
            if (tb == null) return;
            tb.Tag = tb.Text;
            tb.Text = OcrApp.Services.LocalizationService.Get("PressCustomHotkey");
        }

        private void HotkeyInput_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            var tb = sender as System.Windows.Controls.TextBox;
            if (tb == null) return;
            var placeholder = OcrApp.Services.LocalizationService.Get("PressCustomHotkey");
            if (tb.Text == placeholder)
            {
                if (tb.Tag is string prev) tb.Text = prev;
            }
            tb.Tag = null;
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 解析并验证OCR热键
                if (!ParseAndValidateHotkey(HotkeyTextBox.Text, out Key ocrKey))
                {
                    return; // 验证失败，不保存
                }

                // 解析并验证其他热键
                if (!ParseAndValidateHotkey(CopyOriginalHotkeyTextBox.Text, out Key coKey))
                {
                    return;
                }
                if (!ParseAndValidateHotkey(CloseHotkeyTextBox.Text, out Key cKey))
                {
                    return;
                }
                if (!ParseAndValidateHotkey(SelectionClearHotkeyTextBox.Text, out Key scKey))
                {
                    return;
                }
                if (!ParseAndValidateHotkey(SelectionSelectAllHotkeyTextBox.Text, out Key saKey))
                {
                    return;
                }

                if (!ParseAndValidateHotkey(Extension1HotkeyTextBox.Text, out Key ext1Key))
                {
                    return;
                }
                if (!ParseAndValidateHotkey(Extension2HotkeyTextBox.Text, out Key ext2Key))
                {
                    return;
                }

                // 解析字体大小
                double fontSize = resultWindowFontSize;
                if (FontSizeComboBox.SelectedItem is ComboBoxItem fsItem)
                {
                    var fsText = fsItem.Content?.ToString() ?? "";
                    if (!double.TryParse(fsText, out fontSize) || fontSize <= 0)
                    {
                        MessageBox.Show(OcrApp.Services.LocalizationService.Get("SelectValidFontSize"), OcrApp.Services.LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                // 获取选中的OCR模式
                string selectedMode = "Classic";
                if (OCRModeComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    selectedMode = selectedItem.Tag?.ToString() ?? "Classic";
                }

                // 更新内部变量
                copyOriginalKey = coKey;
                closeKey = cKey;
                selectionClearKey = scKey;
                selectionSelectAllKey = saKey;
                resultWindowFontSize = fontSize;
                ocrMode = selectedMode;

                // 更新热键注册
                ParseHotkey(HotkeyTextBox.Text);
                RegisterCurrentHotkey();

                var configText = File.ReadAllText("appsettings.json");
                var configObj = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(configText) ?? new Newtonsoft.Json.Linq.JObject();
                configObj.Hotkey = HotkeyTextBox.Text;
                configObj.ResultWindowFontSize = resultWindowFontSize.ToString();
                // 移除 TranslateKey 的保存，扩展热键独立配置
                configObj.CopyOriginalKey = CopyOriginalHotkeyTextBox.Text;
                configObj.CloseKey = CloseHotkeyTextBox.Text;
                configObj.OCRMode = selectedMode;
                configObj.SelectionClearKey = SelectionClearHotkeyTextBox.Text;
                configObj.SelectionSelectAllKey = SelectionSelectAllHotkeyTextBox.Text;
                if (Extension1PluginComboBox != null && Extension1PluginComboBox.SelectedIndex >= 0)
                {
                    configObj.Extension1PreferredIndex = Extension1PluginComboBox.SelectedIndex;
                }
                if (Extension2PluginComboBox != null && Extension2PluginComboBox.SelectedIndex >= 0)
                {
                    configObj.Extension2PreferredIndex = Extension2PluginComboBox.SelectedIndex;
                }
                configObj.Extension1Name = Extension1NameTextBox.Text;
                configObj.Extension2Name = Extension2NameTextBox.Text;
                configObj.Extension1Hotkey = Extension1HotkeyTextBox.Text;
                configObj.Extension2Hotkey = Extension2HotkeyTextBox.Text;
                configObj.Extension1Enabled = Extension1EnabledCheckBox.IsChecked == true;
                configObj.Extension2Enabled = Extension2EnabledCheckBox.IsChecked == true;
                if (LanguageComboBox != null && LanguageComboBox.SelectedItem is ComboBoxItem langItem)
                {
                    configObj.InterfaceLanguage = langItem.Tag?.ToString() ?? "zh-CN";
                }
                File.WriteAllText("appsettings.json", Newtonsoft.Json.JsonConvert.SerializeObject(configObj, Newtonsoft.Json.Formatting.Indented));

                

                // 更新ResultWindow的设置
                if (resultWindow != null)
                {
                    resultWindow.UpdateSettings(fontSize, copyOriginalKey, closeKey);
                }
                if (selectionResultWindow != null)
                {
                    selectionResultWindow.RefreshExtensionSettings();
                }

                MessageBox.Show(OcrApp.Services.LocalizationService.Get("MessageSaved"), OcrApp.Services.LocalizationService.Get("PromptTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(OcrApp.Services.LocalizationService.Get("MessageSaveSettingsErrorPrefix") + ex.Message, OcrApp.Services.LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyLocalization()
        {
            try
            {
                if (TitleBarText != null) TitleBarText.Text = OcrApp.Services.LocalizationService.Get("SettingsTitle");
                this.Title = OcrApp.Services.LocalizationService.Get("SettingsTitle");
                if (HotkeySettingsTitle != null) HotkeySettingsTitle.Text = OcrApp.Services.LocalizationService.Get("HotkeySettings");
                if (ScreenshotOCRLabel != null) ScreenshotOCRLabel.Text = OcrApp.Services.LocalizationService.Get("ScreenshotOCRHotkey");
                if (CopyOriginalHotkeyLabel != null) CopyOriginalHotkeyLabel.Text = OcrApp.Services.LocalizationService.Get("CopyOriginalHotkeyLabel");
                if (CloseWindowHotkeyLabel != null) CloseWindowHotkeyLabel.Text = OcrApp.Services.LocalizationService.Get("CloseWindowHotkeyLabel");
                if (SelectionClearHotkeyLabel != null) SelectionClearHotkeyLabel.Text = OcrApp.Services.LocalizationService.Get("SelectionClearHotkeyLabel");
                if (SelectionSelectAllHotkeyLabel != null) SelectionSelectAllHotkeyLabel.Text = OcrApp.Services.LocalizationService.Get("SelectionSelectAllHotkeyLabel");
                if (DisplayModeTitle != null) DisplayModeTitle.Text = OcrApp.Services.LocalizationService.Get("DisplayAndMode");
                if (ResultWindowFontSizeLabel != null) ResultWindowFontSizeLabel.Text = OcrApp.Services.LocalizationService.Get("ResultWindowFontSizeLabel");
                if (OCRModeLabel != null) OCRModeLabel.Text = OcrApp.Services.LocalizationService.Get("OCRModeLabel");
                if (OcrClassicItem != null) OcrClassicItem.Content = OcrApp.Services.LocalizationService.Get("ClassicMode");
                if (OcrSilentItem != null) OcrSilentItem.Content = OcrApp.Services.LocalizationService.Get("SilentMode");
                if (OcrSelectionItem != null) OcrSelectionItem.Content = OcrApp.Services.LocalizationService.Get("SelectionMode");
                if (InterfaceLanguageLabel != null) InterfaceLanguageLabel.Text = OcrApp.Services.LocalizationService.Get("InterfaceLanguage");
                if (Extension1Title != null) Extension1Title.Text = OcrApp.Services.LocalizationService.Get("Extension1Title");
                if (Extension1EnabledCheckBox != null) Extension1EnabledCheckBox.Content = OcrApp.Services.LocalizationService.Get("EnableExtension1");
                if (Extension1NameLabel != null) Extension1NameLabel.Text = OcrApp.Services.LocalizationService.Get("NameLabel");
                if (Extension1PluginLabel != null) Extension1PluginLabel.Text = OcrApp.Services.LocalizationService.Get("PluginLabel");
                if (Extension1HotkeyLabel != null) Extension1HotkeyLabel.Text = OcrApp.Services.LocalizationService.Get("HotkeyLabel");
                if (Extension2Title != null) Extension2Title.Text = OcrApp.Services.LocalizationService.Get("Extension2Title");
                if (Extension2EnabledCheckBox != null) Extension2EnabledCheckBox.Content = OcrApp.Services.LocalizationService.Get("EnableExtension2");
                if (Extension2NameLabel != null) Extension2NameLabel.Text = OcrApp.Services.LocalizationService.Get("NameLabel");
                if (Extension2PluginLabel != null) Extension2PluginLabel.Text = OcrApp.Services.LocalizationService.Get("PluginLabel");
                if (Extension2HotkeyLabel != null) Extension2HotkeyLabel.Text = OcrApp.Services.LocalizationService.Get("HotkeyLabel");
                if (SaveSettingsButton != null) SaveSettingsButton.Content = OcrApp.Services.LocalizationService.Get("SaveSettings");
                if (resultWindow != null) resultWindow.RefreshLocalization();
                if (selectionResultWindow != null) selectionResultWindow.RefreshExtensionSettings();
                if (trayIcon != null && trayIcon.ContextMenuStrip != null && trayIcon.ContextMenuStrip.Items.Count >= 2)
                {
                    trayIcon.Text = OcrApp.Services.LocalizationService.Get("TrayTooltip");
                    trayIcon.ContextMenuStrip.Items[0].Text = OcrApp.Services.LocalizationService.Get("TraySettings");
                    trayIcon.ContextMenuStrip.Items[1].Text = OcrApp.Services.LocalizationService.Get("TrayExit");
                }
            }
            catch { }
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var item = LanguageComboBox.SelectedItem as ComboBoxItem;
                var lang = item != null ? (string)(item.Tag ?? "zh-CN") : "zh-CN";
                OcrApp.Services.LocalizationService.SetLanguage(lang);
                ApplyLocalization();
            }
            catch { }
        }
    }
}
