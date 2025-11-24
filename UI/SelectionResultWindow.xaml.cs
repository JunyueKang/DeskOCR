using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OcrApp.Services;

namespace OcrApp.UI
{
    public partial class SelectionResultWindow : Window, IDisposable
    {
        private List<string> words = new List<string>();
        private List<string> selectedWords = new List<string>();
        private Dictionary<System.Windows.Controls.Button, string> buttonWordMap = new Dictionary<System.Windows.Controls.Button, string>();
        private HashSet<System.Windows.Controls.Button> selectedButtons = new HashSet<System.Windows.Controls.Button>();
        private bool disposed = false;
        private Key _selectionClearKey = Key.X;
        private Key _selectionSelectAllKey = Key.A;
        private ModifierKeys _selectionClearMods = ModifierKeys.Alt;
        private ModifierKeys _selectionSelectAllMods = ModifierKeys.Alt;
        private Key _extension1Key = Key.E;
        private Key _extension2Key = Key.R;
        private ModifierKeys _extension1Mods = ModifierKeys.Alt;
        private ModifierKeys _extension2Mods = ModifierKeys.Alt;
        private string _extension1Name = "扩展一";
        private string _extension2Name = "扩展二";
        private int _extension1Index = 0;
        private int _extension2Index = 0;
        private bool _extension1Enabled = true;
        private bool _extension2Enabled = true;
        
        // 拖动功能相关字段
        private bool isDragging = false;
        private System.Windows.Point startPoint;


        private JiebaSegmenterManager segmenterManager;

        public SelectionResultWindow()
        {
            InitializeComponent();
            this.KeyDown += SelectionResultWindow_KeyDown;
            
            // 使用 JiebaSegmenterManager 单例
            segmenterManager = JiebaSegmenterManager.Instance;
            ApplyExtensionSettingsFromConfig();
        }

        public void RefreshExtensionSettings()
        {
            ApplyExtensionSettingsFromConfig();
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

        private void ApplyExtensionSettingsFromConfig()
        {
            try
            {
                string cfgPath = System.IO.Path.GetFullPath("appsettings.json");
                if (!System.IO.File.Exists(cfgPath))
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    cfgPath = System.IO.Path.Combine(baseDir, "appsettings.json");
                }
                if (System.IO.File.Exists(cfgPath))
                {
                    var json = System.IO.File.ReadAllText(cfgPath);
                    object? cfgObj = Newtonsoft.Json.JsonConvert.DeserializeObject<object?>(json);
                    if (cfgObj != null)
                    {
                        dynamic cfg = cfgObj;
                        string? clearStr = cfg.SelectionClearKey as string;
                        string? selAllStr = cfg.SelectionSelectAllKey as string;
                        (_selectionClearMods, _selectionClearKey) = ParseHotkey(clearStr ?? "ALT+X", ModifierKeys.Alt, Key.X);
                        (_selectionSelectAllMods, _selectionSelectAllKey) = ParseHotkey(selAllStr ?? "ALT+A", ModifierKeys.Alt, Key.A);
                        string? e1 = cfg.Extension1Hotkey as string;
                        string? e2 = cfg.Extension2Hotkey as string;
                        (_extension1Mods, _extension1Key) = ParseHotkey(e1 ?? "Alt+E", ModifierKeys.Alt, Key.E);
                        (_extension2Mods, _extension2Key) = ParseHotkey(e2 ?? "Alt+R", ModifierKeys.Alt, Key.R);
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
                }
            }
            catch { }

            try
            {
                ClearButton.Content = OcrApp.Services.LocalizationService.Get("ClearSelectionButton") + $"({FormatHotkey(_selectionClearMods, _selectionClearKey)})";
                SelectAllButton.Content = OcrApp.Services.LocalizationService.Get("SelectAllButton") + $"({FormatHotkey(_selectionSelectAllMods, _selectionSelectAllKey)})";
                if (Extension1Button != null) Extension1Button.Content = $"{_extension1Name}({FormatHotkey(_extension1Mods, _extension1Key)})";
                if (Extension2Button != null) Extension2Button.Content = $"{_extension2Name}({FormatHotkey(_extension2Mods, _extension2Key)})";
                if (Extension1Button != null) Extension1Button.Visibility = _extension1Enabled ? Visibility.Visible : Visibility.Collapsed;
                if (Extension2Button != null) Extension2Button.Visibility = _extension2Enabled ? Visibility.Visible : Visibility.Collapsed;
                UpdateButtonStates();
            }
            catch { }
        }

        public void ShowWithOCRResult(string ocrResult)
        {
            if (string.IsNullOrWhiteSpace(ocrResult))
            {
                System.Windows.MessageBox.Show(OcrApp.Services.LocalizationService.Get("MessageOcrEmpty"), OcrApp.Services.LocalizationService.Get("PromptTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 分词处理
            words = segmenterManager.SegmentText(ocrResult);
            
            // 清空之前的内容
            ClearSelection();
            
            // 创建词汇按钮
            CreateWordButtons();
            
            // 显示窗口
            this.Show();
            this.Activate();
            this.Focus();
        }

        private void CreateWordButtons()
        {
            WordsPanel.Children.Clear();
            buttonWordMap.Clear();
            selectedButtons.Clear();
            
            foreach (string word in words)
            {
                var button = new System.Windows.Controls.Button
                {
                    Tag = word
                };
                
                // 检查是否为换行符
                if (word == "\n" || word == "\r\n" || word == "\r")
                {
                    // 换行符显示为橙色字体，背景色与其他格子一致
                    button.Content = OcrApp.Services.LocalizationService.Get("LineBreakLabel");
                    button.Style = (Style)FindResource("WordButtonStyle");
                    button.Foreground = new SolidColorBrush(Colors.Orange);
                    button.ToolTip = OcrApp.Services.LocalizationService.Get("LineBreakTooltip");
                }
                else
                {
                    // 普通词汇正常显示
                    button.Content = word;
                    button.Style = (Style)FindResource("WordButtonStyle");
                }
                
                // 绑定点击事件和Shift+悬停事件
                button.Click += WordButton_Click;
                button.MouseEnter += WordButton_MouseEnter;
                button.MouseLeave += WordButton_MouseLeave;
                buttonWordMap[button] = word;
                WordsPanel.Children.Add(button);
            }
        }

        private void WordButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && buttonWordMap.ContainsKey(button))
            {
                ToggleButtonSelection(button);
            }
        }

        private void WordButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // 只有在按住Shift键时才通过悬停选择
            if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.LeftShift) || 
                System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.RightShift))
            {
                if (sender is System.Windows.Controls.Button button && buttonWordMap.ContainsKey(button))
                {
                    ToggleButtonSelection(button);
                }
            }
        }

        private void ToggleButtonSelection(System.Windows.Controls.Button button)
        {
            string word = buttonWordMap[button];
            
            if (selectedButtons.Contains(button))
            {
                // 如果这个按钮已选中，则取消选择
                selectedButtons.Remove(button);
                selectedWords.Remove(word);
                
                // 恢复原始样式
                if (word == "\n" || word == "\r\n" || word == "\r")
                {
                    // 换行符恢复为橙色字体，背景色与其他格子一致
                    button.Style = (Style)FindResource("WordButtonStyle");
                    button.Foreground = new SolidColorBrush(Colors.Orange);
                }
                else
                {
                    // 普通词汇恢复默认样式
                    button.Style = (Style)FindResource("WordButtonStyle");
                }
            }
            else
            {
                // 如果这个按钮未选中，则选择
                selectedButtons.Add(button);
                selectedWords.Add(word);
                button.Style = (Style)FindResource("SelectedWordButtonStyle");
            }
            
            UpdateSelectedText();
            UpdateButtonStates();
        }

        private void WordButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // 现在使用点击或Shift+悬停选择，MouseLeave不需要特殊处理
        }

        private void UpdateSelectedText()
        {
            if (selectedButtons.Count == 0)
            {
                SelectedTextBox.Text = "请点击上方词汇进行选择，或按住Shift键悬停选择...";
            }
            else
            {
                // 按原始顺序排列选中的词汇，基于按钮而不是词汇内容
                // 不添加额外的空格，直接拼接选中的内容
                var orderedSelectedWords = new List<string>();
                foreach (var child in WordsPanel.Children)
                {
                    if (child is System.Windows.Controls.Button button && selectedButtons.Contains(button))
                    {
                        string word = buttonWordMap[button];
                        orderedSelectedWords.Add(word);
                    }
                }
                SelectedTextBox.Text = string.Join("", orderedSelectedWords);
            }
        }

        private void UpdateButtonStates()
        {
            bool hasSelection = selectedButtons.Count > 0;
            CopyButton.IsEnabled = hasSelection;
            if (Extension1Button != null) Extension1Button.IsEnabled = hasSelection && _extension1Enabled;
            if (Extension2Button != null) Extension2Button.IsEnabled = hasSelection && _extension2Enabled;
            ClearButton.IsEnabled = hasSelection;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedButtons.Count > 0)
            {
                // 按原始顺序排列选中的词汇，基于按钮而不是词汇内容
                // 不添加额外的空格，直接拼接选中的内容
                var orderedSelectedWords = new List<string>();
                foreach (var child in WordsPanel.Children)
                {
                    if (child is System.Windows.Controls.Button button && selectedButtons.Contains(button))
                    {
                        string word = buttonWordMap[button];
                        orderedSelectedWords.Add(word);
                    }
                }
                string textToCopy = string.Join("", orderedSelectedWords);
                try
                {
                    if (ClipboardService.TrySetText(textToCopy, this))
                    {
                        this.Hide();
                    }
                    else
                    {
                        System.Windows.MessageBox.Show(OcrApp.Services.LocalizationService.Get("MessageCopyFailed"), OcrApp.Services.LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(OcrApp.Services.LocalizationService.Get("MessageCopyFailed") + ex.Message, OcrApp.Services.LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Extension1Button_Click(object sender, RoutedEventArgs e)
        {
            if (selectedButtons.Count > 0)
            {
                // 按原始顺序排列选中的词汇，基于按钮而不是词汇内容
                // 不添加额外的空格，直接拼接选中的内容
                var orderedSelectedWords = new List<string>();
                foreach (var child in WordsPanel.Children)
                {
                    if (child is System.Windows.Controls.Button button && selectedButtons.Contains(button))
                    {
                        string word = buttonWordMap[button];
                        orderedSelectedWords.Add(word);
                    }
                }
                string textToTranslate = string.Join("", orderedSelectedWords);
                try
                {
                    var url = GetExternalUrlByIndex(textToTranslate, _extension1Index);
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
                        System.Windows.MessageBox.Show(OcrApp.Services.LocalizationService.Get("MessageNotFoundExternalConfig"), OcrApp.Services.LocalizationService.Get("PromptTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(OcrApp.Services.LocalizationService.Get("MessageOpenExternalFail") + ex.Message, OcrApp.Services.LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            }
        }

        private void Extension2Button_Click(object sender, RoutedEventArgs e)
        {
            if (selectedButtons.Count > 0)
            {
                var orderedSelectedWords = new List<string>();
                foreach (var child in WordsPanel.Children)
                {
                    if (child is System.Windows.Controls.Button button && selectedButtons.Contains(button))
                    {
                        string word = buttonWordMap[button];
                        orderedSelectedWords.Add(word);
                    }
                }
                string textToTranslate = string.Join("", orderedSelectedWords);
                try
                {
                    var url = GetExternalUrlByIndex(textToTranslate, _extension2Index);
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
                        System.Windows.MessageBox.Show(OcrApp.Services.LocalizationService.Get("MessageNotFoundExternalConfig"), OcrApp.Services.LocalizationService.Get("PromptTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(OcrApp.Services.LocalizationService.Get("MessageOpenExternalFail") + ex.Message, OcrApp.Services.LocalizationService.Get("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            ClearSelection();
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            selectedWords.Clear();
            selectedButtons.Clear();
            selectedWords.AddRange(words);
            
            // 更新所有按钮样式并添加到selectedButtons
            foreach (var kvp in buttonWordMap)
            {
                kvp.Key.Style = (Style)FindResource("SelectedWordButtonStyle");
                selectedButtons.Add(kvp.Key);
            }
            
            UpdateSelectedText();
            UpdateButtonStates();
        }

        private void ClearSelection()
        {
            selectedWords.Clear();
            selectedButtons.Clear();
            
            // 重置所有按钮样式
            foreach (var kvp in buttonWordMap)
            {
                kvp.Key.Style = (Style)FindResource("WordButtonStyle");
            }
            
            UpdateSelectedText();
            UpdateButtonStates();
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

        private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                System.Windows.Point currentPosition = e.GetPosition(this);
                
                // 只有当鼠标移动超过一定距离时才进行窗口拖动
                if (Math.Abs(currentPosition.X - startPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(currentPosition.Y - startPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    DragMove();
                }
            }
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            isDragging = false;
        }

        

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            ClearSelection();
        }

        private void SelectionResultWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Alt+D 关闭窗口
            if (e.SystemKey == Key.D && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                CloseButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            // Alt+C 复制原文
            else if (e.SystemKey == Key.C && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                if (CopyButton.IsEnabled)
                {
                    CopyButton_Click(this, new RoutedEventArgs());
                }
                e.Handled = true;
            }
            // Alt+E 翻译
            else if (_extension1Enabled && HotkeyMatches(e, _extension1Mods, _extension1Key))
            {
                if (Extension1Button.IsEnabled)
                {
                    Extension1Button_Click(this, new RoutedEventArgs());
                }
                e.Handled = true;
            }
            else if (_extension2Enabled && HotkeyMatches(e, _extension2Mods, _extension2Key))
            {
                if (Extension2Button.IsEnabled)
                {
                    Extension2Button_Click(this, new RoutedEventArgs());
                }
                e.Handled = true;
            }
            // ESC键关闭窗口
            else if (e.Key == Key.Escape)
            {
                CloseButton_Click(this, new RoutedEventArgs());
            }
            // 清空选择（支持 Alt/Ctrl/Shift/Win 组合）
            else if (HotkeyMatches(e, _selectionClearMods, _selectionClearKey))
            {
                if (ClearButton.IsEnabled)
                {
                    ClearButton_Click(this, new RoutedEventArgs());
                }
                e.Handled = true;
            }
            // 全选（支持 Alt/Ctrl/Shift/Win 组合）
            else if (HotkeyMatches(e, _selectionSelectAllMods, _selectionSelectAllKey))
            {
                SelectAllButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            // Delete 清空选择
            else if (e.Key == Key.Delete)
            {
                if (ClearButton.IsEnabled)
                {
                    ClearButton_Click(this, new RoutedEventArgs());
                }
                e.Handled = true;
            }
        }

        private static bool HotkeyMatches(System.Windows.Input.KeyEventArgs e, ModifierKeys mods, Key key)
        {
            bool modsMatch = (Keyboard.Modifiers & mods) == mods;
            bool keyMatch = ((mods & ModifierKeys.Alt) != 0) ? e.SystemKey == key : e.Key == key;
            return modsMatch && keyMatch;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // 防止窗口被关闭，而是隐藏窗口
            e.Cancel = true;
            this.Hide();
            ClearSelection();
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
                }
                disposed = true;
            }
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