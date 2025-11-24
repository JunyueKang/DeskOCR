using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Size = System.Windows.Size;
using Point = System.Windows.Point;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Brushes = System.Windows.Media.Brushes;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MessageBox = System.Windows.MessageBox;
using OcrApp.Services;

namespace OcrApp.UI
{
    public partial class ResultWindow : Window, IDisposable, INotifyPropertyChanged
    {
        private double screenWidth = SystemParameters.PrimaryScreenWidth;
        private double screenHeight = SystemParameters.PrimaryScreenHeight;
        private bool isDragging = false;
        private Point startPoint;
        private bool userMoved = false;
        private Key _copyOriginalKey;
        private Key _closeKey;
        private Key _extension1Key;
        private Key _extension2Key;
        private ModifierKeys _copyOriginalMods = ModifierKeys.Alt;
        private ModifierKeys _closeMods = ModifierKeys.Alt;
        private ModifierKeys _extension1Mods = ModifierKeys.Alt;
        private ModifierKeys _extension2Mods = ModifierKeys.Alt;
        private string _extension1Name = "扩展一";
        private string _extension2Name = "扩展二";
        private int _extension1Index = 0;
        private int _extension2Index = 0;
        private bool _extension1Enabled = true;
        private bool _extension2Enabled = true;

        private double _resultWindowFontSize;
        public double ResultWindowFontSize
        {
            get => _resultWindowFontSize;
            set
            {
                _resultWindowFontSize = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ResultWindowFontSize)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ResultWindow(double fontSize, Key copyOriginalKey = Key.C, Key closeKey = Key.D)
        {
            InitializeComponent();
            
            // 保存快捷键设置
            _copyOriginalKey = copyOriginalKey;
            _closeKey = closeKey;
            ApplyExtensionSettingsFromConfig();
            
            this.Title = OcrApp.Services.LocalizationService.Get("ResultWindowTitle");
            ResultTextBox.Text = "";
            
            // 设置字体大小属性
            ResultWindowFontSize = fontSize;
            this.DataContext = this;
            
            TranslationTextBox.Visibility = Visibility.Collapsed;

            // 添加文本改变事件处理
            ResultTextBox.TextChanged += TextBox_TextChanged;
            TranslationTextBox.TextChanged += TextBox_TextChanged;

            // 先设置一个默认的合适大小
            this.Width = 480;  // MinWidth值
            this.Height = 200; // 一个合适的默认高度
            
            // 立即调整窗口位置和大小
            AdjustWindowSize();

            // Set window to gain focus
            this.Loaded += (s, e) => Keyboard.Focus(this);
        }

        private void UpdateHotkeyText()
        {
            Extension1Button.Content = $"{_extension1Name}({FormatHotkey(_extension1Mods, _extension1Key)})";
            Extension2Button.Content = $"{_extension2Name}({FormatHotkey(_extension2Mods, _extension2Key)})";
            CopyOriginalButton.Content = $"{OcrApp.Services.LocalizationService.Get("CopyOriginalButton")}(" + FormatHotkey(_copyOriginalMods, _copyOriginalKey) + ")";
            CloseButton.Content = $"{OcrApp.Services.LocalizationService.Get("CloseButton")}(" + FormatHotkey(_closeMods, _closeKey) + ")";
            Extension1Button.Visibility = _extension1Enabled ? Visibility.Visible : Visibility.Collapsed;
            Extension2Button.Visibility = _extension2Enabled ? Visibility.Visible : Visibility.Collapsed;
            UpdateButtonStates();
        }

        public void RefreshLocalization()
        {
            this.Title = OcrApp.Services.LocalizationService.Get("ResultWindowTitle");
            UpdateHotkeyText();
        }

        public void UpdateResult(string message)
        {
            ResultTextBox.Text = message;
            ResultTextBox.Foreground = Brushes.Snow; // 重置文颜色为黑色
            AdjustWindowSize();
        }

        public void ShowError(string errorMessage)
        {
            ResultTextBox.Text = errorMessage;
            ResultTextBox.Foreground = Brushes.OrangeRed; // 设置文本颜色为红色
            AdjustWindowSize();
        }

        public void UpdateSettings(double fontSize, Key copyOriginalKey, Key closeKey)
        {
            // 更新字体大小
            ResultWindowFontSize = fontSize;
            
            // 更新快捷键设置
            _copyOriginalKey = copyOriginalKey;
            _closeKey = closeKey;
            ApplyExtensionSettingsFromConfig();
        }

        private void Border_MouseMove(object sender, MouseEventArgs e) // Support window dragging
        {
            if (isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPosition = e.GetPosition(this);
                
                // 只有当鼠标移动超过一定距离时才进行窗口拖动
                if (Math.Abs(currentPosition.X - startPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(currentPosition.Y - startPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    userMoved = true;
                    DragMove();
                }
            }
        }

        private void AdjustWindowSize()
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            double prevW = ActualWidth;
            double prevH = ActualHeight;
            double prevLeft = Left;
            double prevTop = Top;
            double prevCenterX = prevLeft + prevW / 2;
            double prevCenterY = prevTop + prevH / 2;

            MaxWidth = screenWidth * 0.8;
            MaxHeight = screenHeight * 0.8;

            if (ActualWidth > MaxWidth)
            {
                Width = MaxWidth;
            }
            if (ActualHeight > MaxHeight)
            {
                Height = MaxHeight;
            }

            this.UpdateLayout();

            double newW = ActualWidth;
            double newH = ActualHeight;

            if (!userMoved)
            {
                Left = (screenWidth - newW) / 2;
                Top = (screenHeight - newH) / 2;
            }
            else
            {
                double newLeft = prevCenterX - newW / 2;
                double newTop = prevCenterY - newH / 2;
                if (newLeft < 0) newLeft = 0;
                if (newTop < 0) newTop = 0;
                if (newLeft + newW > screenWidth) newLeft = Math.Max(0, screenWidth - newW);
                if (newTop + newH > screenHeight) newTop = Math.Max(0, screenHeight - newH);
                Left = newLeft;
                Top = newTop;
            }
        }

        private Size MeasureString(string text)
        {
            var formattedText = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface(ResultTextBox.FontFamily, ResultTextBox.FontStyle, ResultTextBox.FontWeight, ResultTextBox.FontStretch),
                ResultTextBox.FontSize,
                Brushes.Black,
                new NumberSubstitution(),
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            return new Size(formattedText.Width, formattedText.Height);
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(ResultTextBox.Text))
            {
                ClipboardService.TrySetText(ResultTextBox.Text, this);
                this.Hide();
                ClearContent();
            }
        }

        

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            ClearContent();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (HotkeyMatches(e, _copyOriginalMods, _copyOriginalKey))
            {
                CopyButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }

            

            if (HotkeyMatches(e, _closeMods, _closeKey))
            {
                this.Hide();
                ClearContent();
                e.Handled = true;
            }

            if (_extension1Enabled && HotkeyMatches(e, _extension1Mods, _extension1Key))
            {
                Extension1Button_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            if (_extension2Enabled && HotkeyMatches(e, _extension2Mods, _extension2Key))
            {
                Extension2Button_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void LoadHotkeysFromConfig()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string cfgPath = System.IO.Path.Combine(baseDir, "appsettings.json");
                if (!System.IO.File.Exists(cfgPath)) return;
                var json = System.IO.File.ReadAllText(cfgPath);
                object? cfgObj = Newtonsoft.Json.JsonConvert.DeserializeObject<object?>(json);
                if (cfgObj == null) return;
                dynamic cfg = cfgObj;
                string? co = cfg.CopyOriginalKey as string;
                string? c = cfg.CloseKey as string;
                (_copyOriginalMods, _copyOriginalKey) = ParseHotkey(co ?? $"Alt+{_copyOriginalKey}", _copyOriginalMods, _copyOriginalKey);
                (_closeMods, _closeKey) = ParseHotkey(c ?? $"Alt+{_closeKey}", _closeMods, _closeKey);

                string? e1 = cfg.Extension1Hotkey as string;
                string? e2 = cfg.Extension2Hotkey as string;
                (_extension1Mods, _extension1Key) = ParseHotkey(e1 ?? "Alt+E", _extension1Mods, Key.E);
                (_extension2Mods, _extension2Key) = ParseHotkey(e2 ?? "Alt+R", _extension2Mods, Key.R);

                string? n1 = null;
                n1 = (string)cfg.Extension1Name;
                if (!string.IsNullOrWhiteSpace(n1)) _extension1Name = n1; else _extension1Name = "扩展一";
                string? n2 = null;
                n2 = (string)cfg.Extension2Name;
                if (!string.IsNullOrWhiteSpace(n2)) _extension2Name = n2; else _extension2Name = "扩展二";
                try { _extension1Index = (int)(cfg.Extension1PreferredIndex ?? (cfg.TranslationPreferredIndex ?? 0)); } catch { _extension1Index = 0; }
                try { _extension2Index = (int)(cfg.Extension2PreferredIndex ?? 0); } catch { _extension2Index = 0; }

                try
                {
                    if (cfg.Extension1Enabled != null)
                    {
                        try { _extension1Enabled = (bool)cfg.Extension1Enabled; }
                        catch
                        {
                            try { string s1 = (string)cfg.Extension1Enabled; if (bool.TryParse(s1, out var v1)) _extension1Enabled = v1; }
                            catch { }
                        }
                    }
                    if (cfg.Extension2Enabled != null)
                    {
                        try { _extension2Enabled = (bool)cfg.Extension2Enabled; }
                        catch
                        {
                            try { string s2 = (string)cfg.Extension2Enabled; if (bool.TryParse(s2, out var v2)) _extension2Enabled = v2; }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            catch { }
        }

        private void ApplyExtensionSettingsFromConfig()
        {
            LoadHotkeysFromConfig();
            UpdateHotkeyText();
            UpdateButtonStates();
        }

        private static (ModifierKeys mods, Key key) ParseHotkey(string? value, ModifierKeys defaultMods, Key defaultKey)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(value)) return (defaultMods, defaultKey);
                var s = value.Trim();
                if (!s.Contains("+")) return (defaultMods, defaultKey);
                var parts = s.Split('+');
                ModifierKeys mods = 0;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    var m = parts[i].Trim().ToLower();
                    if (m == "alt") mods |= ModifierKeys.Alt;
                    else if (m == "ctrl" || m == "control") mods |= ModifierKeys.Control;
                    else if (m == "shift") mods |= ModifierKeys.Shift;
                    else if (m == "win" || m == "windows") mods |= ModifierKeys.Windows;
                }
                var last = parts[parts.Length - 1].Trim();
                if (!Enum.TryParse(last, true, out Key k)) k = defaultKey;
                if (mods == 0) mods = defaultMods;
                return (mods, k);
            }
            catch { return (defaultMods, defaultKey); }
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

        private static bool HotkeyMatches(KeyEventArgs e, ModifierKeys mods, Key key)
        {
            bool modsMatch = (Keyboard.Modifiers & mods) == mods;
            bool keyMatch = ((mods & ModifierKeys.Alt) != 0) ? e.SystemKey == key : e.Key == key;
            return modsMatch && keyMatch;
        }

        private void UpdateButtonStates()
        {
            bool hasText = !string.IsNullOrEmpty(ResultTextBox.Text);
            CopyOriginalButton.IsEnabled = hasText;
            Extension1Button.IsEnabled = hasText && _extension1Enabled;
            Extension2Button.IsEnabled = hasText && _extension2Enabled;
            CloseButton.IsEnabled = true;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 只有当点击窗口背景时才允许拖动
            if (e.Source is Window)
            {
                isDragging = true;
                startPoint = e.GetPosition(this);
            }
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            isDragging = false;
        }

        private void Extension1Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(ResultTextBox.Text))
                {
                    MessageBox.Show(OcrApp.Services.LocalizationService.Get("MessagePleaseInputText"), OcrApp.Services.LocalizationService.Get("PromptTitle"));
                    return;
                }

                var url = GetExternalUrlByIndex(ResultTextBox.Text, _extension1Index);
                if (!string.IsNullOrEmpty(url))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show(OcrApp.Services.LocalizationService.Get("MessageNotFoundExternalConfig"), OcrApp.Services.LocalizationService.Get("PromptTitle"));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(OcrApp.Services.LocalizationService.Get("MessageOpenExternalFail") + ex.Message, OcrApp.Services.LocalizationService.Get("ErrorTitle"));
            }
        }

        private void Extension2Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(ResultTextBox.Text))
                {
                    MessageBox.Show(OcrApp.Services.LocalizationService.Get("MessagePleaseInputText"), OcrApp.Services.LocalizationService.Get("PromptTitle"));
                    return;
                }

                var url = GetExternalUrlByIndex(ResultTextBox.Text, _extension2Index);
                if (!string.IsNullOrEmpty(url))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show(OcrApp.Services.LocalizationService.Get("MessageNotFoundExternalConfig"), OcrApp.Services.LocalizationService.Get("PromptTitle"));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(OcrApp.Services.LocalizationService.Get("MessageOpenExternalFail") + ex.Message, OcrApp.Services.LocalizationService.Get("ErrorTitle"));
            }
        }

        // 添加文本改变事件处理方法
        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            AdjustWindowSize();
            UpdateButtonStates();
        }

        // 清空窗口内容的方法
        private void ClearContent()
        {
            ResultTextBox.Text = "";
            TranslationTextBox.Text = "";
            TranslationTextBox.Visibility = Visibility.Collapsed;
            ResultTextBox.Foreground = Brushes.Snow; // 重置文本颜色
            Dispose(true);
            GC.Collect();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // 防止窗口被关闭，而是隐藏窗口
            e.Cancel = true;
            this.Hide();
            ClearContent();
        }

        private bool disposed = false;
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
                    ResultTextBox?.Clear();
                    TranslationTextBox?.Clear();
                }
                
                disposed = true;
            }
        }
        
        ~ResultWindow()
        {
            Dispose(false);
        }

        

        private static string GetExternalUrlByIndex(string text, int index)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string cfgPath = System.IO.Path.Combine(baseDir, "appsettings.json");
                if (!System.IO.File.Exists(cfgPath)) return string.Empty;

                var json = System.IO.File.ReadAllText(cfgPath);
                object? cfgObj = Newtonsoft.Json.JsonConvert.DeserializeObject<object?>(json);
                if (cfgObj == null) return string.Empty;
                dynamic cfg = cfgObj;

                var urlsToken = cfg.TranslationUrls;
                if (urlsToken == null) return string.Empty;

                var urls = new System.Collections.Generic.List<string>();
                foreach (var u in urlsToken)
                {
                    string? s = null;
                    try { s = (string)u; } catch { try { s = u != null ? System.Convert.ToString(u) : null; } catch { } }
                    if (!string.IsNullOrWhiteSpace(s)) urls.Add(s);
                }
                if (urls.Count == 0) return string.Empty;

                if (index < 0 || index >= urls.Count) index = 0;

                string template = urls[index];
                string encoded = System.Uri.EscapeDataString(text);

                if (template.Contains("你好"))
                {
                    return template.Replace("你好", encoded);
                }
                if (template.Contains("?"))
                {
                    return template + "&q=" + encoded;
                }
                return template + "?q=" + encoded;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
