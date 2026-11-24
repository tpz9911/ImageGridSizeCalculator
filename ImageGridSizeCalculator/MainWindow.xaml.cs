using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace ImageGridSizeCalculator
{
    public partial class MainWindow : Window
    {
        // 仅允许 ASCII 半角数字（排除全角数字等其他 Unicode 数字字符）
        private static readonly Regex NonDigitRegex = new(@"[^0-9]+", RegexOptions.Compiled);
        private const double PreviewBoxSize = 54.0;
        private readonly string _configFilePath = Path.Combine(AppContext.BaseDirectory, "igsc_config.json");

        private long? _currentTargetWidth;
        private long? _currentTargetHeight;
        private DispatcherTimer? _copyHintTimer;

        private double _maxPreviewAspectRatio;
        private double _copyHintDurationSeconds;
        private double _standardRatioMaxRelativeError;
        private double _standardRatioMaxPixelDiff;

        private Brush _previewShapeBrush = Brushes.Transparent;
        private Brush _previewErrorBrush = Brushes.Transparent;
        private Brush _grayTextBrush = Brushes.Transparent;
        private Brush _normalInfoBrush = Brushes.Transparent;
        private Brush _errorTextBrush = Brushes.Transparent;
        private Brush _successHintBrush = Brushes.Transparent;

        public MainWindow()
        {
            InitializeComponent();
            RegisterPastingValidation();
            LoadConfiguration();
            Calculate();
        }

        #region 配置读写 (igsc_config.json)

        /// <summary>
        /// 从本地 JSON 文件加载用户上次保存的参数，异常或缺失字段时自动回退默认值。
        /// </summary>
        private void LoadConfiguration()
        {
            AppConfig? config = null;

            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    config = JsonSerializer.Deserialize<AppConfig>(json);
                }
            }
            catch
            {
                // 读取或解析失败时使用默认值
                config = null;
            }

            var defaultConfig = new AppConfig();
            config ??= defaultConfig;

            // 对每个字符串配置项进行 null 兜底，防止配置文件被手动改坏
            TxtSourceWidth.Text = config.SourceWidth ?? defaultConfig.SourceWidth;
            TxtSourceHeight.Text = config.SourceHeight ?? defaultConfig.SourceHeight;
            TxtMatrixColumns.Text = config.MatrixColumns ?? defaultConfig.MatrixColumns;
            TxtMatrixRows.Text = config.MatrixRows ?? defaultConfig.MatrixRows;
            TxtHorizontalSpacing.Text = config.HorizontalSpacing ?? defaultConfig.HorizontalSpacing;
            TxtVerticalSpacing.Text = config.VerticalSpacing ?? defaultConfig.VerticalSpacing;
            ChkEnableBorder.IsChecked = config.EnableBorder;
            TxtBorderWidth.Text = config.BorderWidth ?? defaultConfig.BorderWidth;
            Topmost = config.Topmost;

            // 加载不可持久化的界面与计算参数（以 AppConfig 默认值为准）
            _maxPreviewAspectRatio = double.IsFinite(defaultConfig.MaxPreviewAspectRatio) && defaultConfig.MaxPreviewAspectRatio > 1.0
                ? defaultConfig.MaxPreviewAspectRatio
                : 10.0;
            _copyHintDurationSeconds = double.IsFinite(defaultConfig.CopyHintDurationSeconds) && defaultConfig.CopyHintDurationSeconds > 0
                ? defaultConfig.CopyHintDurationSeconds
                : 1.5;
            _standardRatioMaxRelativeError = double.IsFinite(defaultConfig.StandardRatioMaxRelativeError) && defaultConfig.StandardRatioMaxRelativeError >= 0
                ? defaultConfig.StandardRatioMaxRelativeError
                : 0.001;
            _standardRatioMaxPixelDiff = double.IsFinite(defaultConfig.StandardRatioMaxPixelDiff) && defaultConfig.StandardRatioMaxPixelDiff >= 0
                ? defaultConfig.StandardRatioMaxPixelDiff
                : 1.0;

            _previewShapeBrush = CreateBrushOrDefault(defaultConfig.PreviewShapeColor, "#4CC1D8");
            _previewErrorBrush = CreateBrushOrDefault(defaultConfig.PreviewErrorColor, "#CCCCCC");
            _grayTextBrush = CreateBrushOrDefault(defaultConfig.GrayTextColor, "#909399");
            _normalInfoBrush = CreateBrushOrDefault(defaultConfig.NormalInfoColor, "#606266");
            _errorTextBrush = CreateBrushOrDefault(defaultConfig.ErrorTextColor, "#F56C6C");
            _successHintBrush = CreateBrushOrDefault(defaultConfig.SuccessHintColor, "#67C23A");
        }

        /// <summary>
        /// 将当前界面参数持久化保存到本地 JSON 文件。
        /// </summary>
        private void SaveConfiguration()
        {
            try
            {
                var config = new AppConfig
                {
                    SourceWidth = TxtSourceWidth.Text,
                    SourceHeight = TxtSourceHeight.Text,
                    MatrixColumns = TxtMatrixColumns.Text,
                    MatrixRows = TxtMatrixRows.Text,
                    HorizontalSpacing = TxtHorizontalSpacing.Text,
                    VerticalSpacing = TxtVerticalSpacing.Text,
                    EnableBorder = ChkEnableBorder.IsChecked == true,
                    BorderWidth = TxtBorderWidth.Text,
                    Topmost = Topmost
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(config, options);
                File.WriteAllText(_configFilePath, json);
            }
            catch
            {
                // 静默忽略异常，防止退出崩溃
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SaveConfiguration();
        }

        #endregion

        #region 数字输入约束与焦点交互

        /// <summary>
        /// 为所有数字输入框注册粘贴拦截事件。
        /// </summary>
        private void RegisterPastingValidation()
        {
            TextBox[] textBoxes =
            [
                TxtSourceWidth,
                TxtSourceHeight,
                TxtMatrixColumns,
                TxtMatrixRows,
                TxtHorizontalSpacing,
                TxtVerticalSpacing,
                TxtBorderWidth
            ];

            foreach (var tb in textBoxes)
            {
                if (tb != null)
                {
                    DataObject.AddPastingHandler(tb, OnTextBoxPasting);
                }
            }
        }

        private void OnTextBoxGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.SelectAll();
            }
        }

        private void OnNumberPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = NonDigitRegex.IsMatch(e.Text);
        }

        private void OnNumberPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// 拦截粘贴操作，仅允许粘贴非空且纯 ASCII 数字的文本。
        /// </summary>
        private void OnTextBoxPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string? text = e.DataObject.GetData(typeof(string)) as string;
                if (string.IsNullOrEmpty(text) || NonDigitRegex.IsMatch(text))
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }

        /// <summary>
        /// 拖拽悬停校验：禁止拖入非纯数字文本。
        /// </summary>
        private void OnNumberPreviewDragOver(object sender, DragEventArgs e)
        {
            if (!IsValidNumericDragData(e.Data))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
        }

        /// <summary>
        /// 拖拽放下校验：兜底拦截非纯数字文本的拖入。
        /// </summary>
        private void OnNumberPreviewDrop(object sender, DragEventArgs e)
        {
            if (!IsValidNumericDragData(e.Data))
            {
                e.Handled = true;
            }
        }

        private static bool IsValidNumericDragData(IDataObject dataObject)
        {
            if (dataObject.GetDataPresent(DataFormats.UnicodeText))
            {
                string? text = dataObject.GetData(DataFormats.UnicodeText) as string;
                return !string.IsNullOrEmpty(text) && !NonDigitRegex.IsMatch(text);
            }

            if (dataObject.GetDataPresent(DataFormats.Text))
            {
                string? text = dataObject.GetData(DataFormats.Text) as string;
                return !string.IsNullOrEmpty(text) && !NonDigitRegex.IsMatch(text);
            }

            return false;
        }

        #endregion

        #region 计算逻辑与结果复制

        private void OnInputChanged(object sender, TextChangedEventArgs e)
        {
            if (IsLoaded)
            {
                Calculate();
            }
        }

        private void OnBorderCheckChanged(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
            {
                Calculate();
            }
        }

        /// <summary>
        /// 校验输入参数并实时计算素材信息、成品图宽高及总像素。
        /// </summary>
        private void Calculate()
        {
            if (TxtSourceWidth == null || TxtSourceHeight == null ||
                TxtMatrixColumns == null || TxtMatrixRows == null ||
                TxtHorizontalSpacing == null || TxtVerticalSpacing == null ||
                TxtBorderWidth == null || ChkEnableBorder == null ||
                LblSourceAspectRatio == null || LblSourceStandardRatio == null ||
                LblSourceTotalPixels == null || LblTargetTotalPixels == null ||
                PreviewAspectRect == null || PreviewErrorShape == null ||
                PreviewErrorCircle == null || PreviewErrorLine1 == null || PreviewErrorLine2 == null)
            {
                return;
            }

            string sourceWidthText = TxtSourceWidth.Text.Trim();
            string sourceHeightText = TxtSourceHeight.Text.Trim();
            string columnsText = TxtMatrixColumns.Text.Trim();
            string rowsText = TxtMatrixRows.Text.Trim();
            string hSpacingText = TxtHorizontalSpacing.Text.Trim();
            string vSpacingText = TxtVerticalSpacing.Text.Trim();
            string borderText = TxtBorderWidth.Text.Trim();

            // 1. 实时计算素材宽高比、标准比例与总像素
            int sourceWidth = 0;
            int sourceHeight = 0;
            bool isSourceWidthEmpty = string.IsNullOrEmpty(sourceWidthText);
            bool isSourceHeightEmpty = string.IsNullOrEmpty(sourceHeightText);
            bool isSourceWidthValid = !isSourceWidthEmpty && int.TryParse(sourceWidthText, out sourceWidth) && sourceWidth > 0;
            bool isSourceHeightValid = !isSourceHeightEmpty && int.TryParse(sourceHeightText, out sourceHeight) && sourceHeight > 0;

            if ((!isSourceWidthEmpty && !isSourceWidthValid) || (!isSourceHeightEmpty && !isSourceHeightValid))
            {
                // 存在不合法输入 -> 红色 ERR
                SetInfoText(LblSourceAspectRatio, "ERR", _errorTextBrush);
                SetInfoText(LblSourceStandardRatio, "ERR", _errorTextBrush);
                SetInfoText(LblSourceTotalPixels, "ERR", _errorTextBrush);
                UpdateSourceAspectPreview(isValid: false, aspectRatio: 0);
            }
            else if (isSourceWidthEmpty || isSourceHeightEmpty)
            {
                // 宽或高空缺 -> 灰色 --
                SetInfoText(LblSourceAspectRatio, "--", _grayTextBrush);
                SetInfoText(LblSourceStandardRatio, "--", _grayTextBrush);
                SetInfoText(LblSourceTotalPixels, "--", _grayTextBrush);
                UpdateSourceAspectPreview(isValid: false, aspectRatio: 0);
            }
            else
            {
                double aspectRatio = (double)sourceWidth / sourceHeight;
                SetInfoText(LblSourceAspectRatio, aspectRatio.ToString("F4"), _normalInfoBrush);

                string? matchedRatioText = MatchStandardAspectRatio(sourceWidth, sourceHeight, aspectRatio);
                if (matchedRatioText != null)
                {
                    SetInfoText(LblSourceStandardRatio, matchedRatioText, _normalInfoBrush);
                }
                else
                {
                    SetInfoText(LblSourceStandardRatio, "--", _grayTextBrush);
                }

                double sourceMegapixels = (double)sourceWidth * sourceHeight / 1_000_000.0;
                SetInfoText(LblSourceTotalPixels, sourceMegapixels.ToString("F3"), _normalInfoBrush);
                UpdateSourceAspectPreview(isValid: true, aspectRatio: aspectRatio);
            }

            // 2. 校验其余网格与边框参数
            bool isBorderEnabled = ChkEnableBorder.IsChecked == true;

            bool isColumnsEmpty = string.IsNullOrEmpty(columnsText);
            bool isRowsEmpty = string.IsNullOrEmpty(rowsText);
            bool isHSpacingEmpty = string.IsNullOrEmpty(hSpacingText);
            bool isVSpacingEmpty = string.IsNullOrEmpty(vSpacingText);
            bool isBorderEmpty = string.IsNullOrEmpty(borderText);

            int matrixColumns = 0;
            int matrixRows = 0;
            int horizontalSpacing = 0;
            int verticalSpacing = 0;
            int borderWidth = 0;

            bool isColumnsValid = !isColumnsEmpty && int.TryParse(columnsText, out matrixColumns) && matrixColumns > 0;
            bool isRowsValid = !isRowsEmpty && int.TryParse(rowsText, out matrixRows) && matrixRows > 0;
            bool isHSpacingValid = !isHSpacingEmpty && int.TryParse(hSpacingText, out horizontalSpacing) && horizontalSpacing >= 0;
            bool isVSpacingValid = !isVSpacingEmpty && int.TryParse(vSpacingText, out verticalSpacing) && verticalSpacing >= 0;
            bool isBorderValid = !isBorderEnabled || (!isBorderEmpty && int.TryParse(borderText, out borderWidth) && borderWidth >= 0);

            bool allValid = isSourceWidthValid && isSourceHeightValid &&
                            isColumnsValid && isRowsValid &&
                            isHSpacingValid && isVSpacingValid &&
                            isBorderValid;

            // 判断所有非空输入项中是否存在非法值
            bool hasInvalidNonEmptyInput =
                (!isSourceWidthEmpty && !isSourceWidthValid) ||
                (!isSourceHeightEmpty && !isSourceHeightValid) ||
                (!isColumnsEmpty && !isColumnsValid) ||
                (!isRowsEmpty && !isRowsValid) ||
                (!isHSpacingEmpty && !isHSpacingValid) ||
                (!isVSpacingEmpty && !isVSpacingValid) ||
                (isBorderEnabled && !isBorderEmpty && !isBorderValid);

            if (!allValid)
            {
                ResetTargetResult(
                    showError: hasInvalidNonEmptyInput,
                    statusMessage: "请输入有效的正整数尺寸与非负间距/边框值");
                return;
            }

            // 3. 计算成品图尺寸（使用 checked 防止极端大数导致 long 静默溢出）
            long targetWidth;
            long targetHeight;
            try
            {
                checked
                {
                    targetWidth = (long)sourceWidth * matrixColumns + (long)(matrixColumns - 1) * horizontalSpacing;
                    targetHeight = (long)sourceHeight * matrixRows + (long)(matrixRows - 1) * verticalSpacing;

                    if (isBorderEnabled)
                    {
                        targetWidth += (long)borderWidth * 2;
                        targetHeight += (long)borderWidth * 2;
                    }
                }
            }
            catch (OverflowException)
            {
                ResetTargetResult(
                    showError: true,
                    statusMessage: "计算结果超出数值范围，请减小输入参数");
                return;
            }

            LblStatus.Visibility = Visibility.Collapsed;
            _currentTargetWidth = targetWidth;
            _currentTargetHeight = targetHeight;

            LblTargetWidth.Text = $"{targetWidth:N0} px";
            LblTargetHeight.Text = $"{targetHeight:N0} px";

            double targetMegapixels = (double)targetWidth * targetHeight / 1_000_000.0;
            SetInfoText(LblTargetTotalPixels, targetMegapixels.ToString("F3"), _normalInfoBrush);
        }

        /// <summary>
        /// 根据素材宽高比实时更新右列 1:1 框内的矢量比例示意图。
        /// </summary>
        private void UpdateSourceAspectPreview(bool isValid, double aspectRatio)
        {
            if (!isValid || !double.IsFinite(aspectRatio) || aspectRatio <= 0 ||
                aspectRatio > _maxPreviewAspectRatio || aspectRatio < 1.0 / _maxPreviewAspectRatio)
            {
                PreviewErrorCircle.Stroke = _previewErrorBrush;
                PreviewErrorLine1.Stroke = _previewErrorBrush;
                PreviewErrorLine2.Stroke = _previewErrorBrush;
                PreviewAspectRect.Visibility = Visibility.Collapsed;
                PreviewErrorShape.Visibility = Visibility.Visible;
                return;
            }

            double rectWidth;
            double rectHeight;
            if (aspectRatio >= 1.0)
            {
                rectWidth = PreviewBoxSize;
                rectHeight = PreviewBoxSize / aspectRatio;
            }
            else
            {
                rectHeight = PreviewBoxSize;
                rectWidth = PreviewBoxSize * aspectRatio;
            }

            PreviewAspectRect.Width = rectWidth;
            PreviewAspectRect.Height = rectHeight;
            PreviewAspectRect.Stroke = _previewShapeBrush;
            PreviewAspectRect.Visibility = Visibility.Visible;
            PreviewErrorShape.Visibility = Visibility.Collapsed;
        }

        private void ResetTargetResult(bool showError, string statusMessage)
        {
            _currentTargetWidth = null;
            _currentTargetHeight = null;
            LblTargetWidth.Text = "--";
            LblTargetHeight.Text = "--";

            if (showError)
            {
                SetInfoText(LblTargetTotalPixels, "ERR", _errorTextBrush);
            }
            else
            {
                SetInfoText(LblTargetTotalPixels, "--", _grayTextBrush);
            }

            LblStatus.Text = statusMessage;
            LblStatus.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 匹配最接近的常见标准宽高比并计算像素误差。
        /// </summary>
        private string? MatchStandardAspectRatio(int width, int height, double aspectRatio)
        {
            StandardAspectRatio bestMatch = StandardAspectRatios[0];
            double minRatioDiff = double.MaxValue;

            foreach (var item in StandardAspectRatios)
            {
                double diff = Math.Abs(aspectRatio - item.Ratio);
                if (diff < minRatioDiff)
                {
                    minRatioDiff = diff;
                    bestMatch = item;
                }
            }

            double expectedHeight = width / bestMatch.Ratio;
            double absHeightDiff = Math.Abs(height - expectedHeight);
            double relativeError = absHeightDiff / expectedHeight;

            if (relativeError > _standardRatioMaxRelativeError && absHeightDiff > _standardRatioMaxPixelDiff)
            {
                return null;
            }

            long roundedExpectedHeight = (long)Math.Round(expectedHeight, MidpointRounding.AwayFromZero);
            long pixelDiff = height - roundedExpectedHeight;
            string diffStr = pixelDiff >= 0 ? $"+{pixelDiff}" : pixelDiff.ToString();

            return $"{bestMatch.Name} ({diffStr})";
        }

        private static void SetInfoText(TextBlock target, string text, Brush foreground)
        {
            target.Text = text;
            target.Foreground = foreground;
        }

        private void OnResultValueClick(object sender, MouseButtonEventArgs e)
        {
            long? valueToCopy = null;
            if (sender == LblTargetWidth)
            {
                valueToCopy = _currentTargetWidth;
            }
            else if (sender == LblTargetHeight)
            {
                valueToCopy = _currentTargetHeight;
            }

            if (valueToCopy == null)
            {
                return;
            }

            string text = valueToCopy.Value.ToString();
            try
            {
                TrySetClipboardText(text);
                ShowCopySuccessHint(text);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"复制到剪贴板失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 写入剪贴板并附带重试机制，缓解其他程序短暂占用剪贴板导致的 COMException。
        /// </summary>
        private static void TrySetClipboardText(string text)
        {
            const int maxRetries = 3;
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    return;
                }
                catch (COMException) when (attempt < maxRetries - 1)
                {
                    Thread.Sleep(30);
                }
            }
        }

        private void ShowCopySuccessHint(string copiedText)
        {
            LblCopyHint.Text = $"✓ {copiedText}";
            LblCopyHint.Foreground = _successHintBrush;

            if (_copyHintTimer == null)
            {
                _copyHintTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(_copyHintDurationSeconds)
                };
                _copyHintTimer.Tick += (_, _) =>
                {
                    _copyHintTimer.Stop();
                    LblCopyHint.Text = "点击数值可复制 (Click to Copy)";
                    LblCopyHint.Foreground = _grayTextBrush;
                };
            }
            else
            {
                _copyHintTimer.Stop();
            }

            _copyHintTimer.Start();
        }

        #endregion

        #region 超链接导航

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                // .NET 8 中需显式设置 UseShellExecute = true 才能调用系统默认浏览器打开 URL
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开链接失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        private readonly record struct StandardAspectRatio(string Name, double Ratio);

        private static readonly StandardAspectRatio[] StandardAspectRatios =
        [
            new("1:1", 1.0),
            new("5:4", 5.0 / 4.0),
            new("4:5", 4.0 / 5.0),
            new("4:3", 4.0 / 3.0),
            new("3:4", 3.0 / 4.0),
            new("3:2", 3.0 / 2.0),
            new("2:3", 2.0 / 3.0),
            new("16:10", 16.0 / 10.0),
            new("10:16", 10.0 / 16.0),
            new("16:9", 16.0 / 9.0),
            new("9:16", 9.0 / 16.0),
            new("21:9", 21.0 / 9.0),
            new("9:21", 9.0 / 21.0)
        ];

        /// <summary>
        /// 创建并冻结不可变画刷；若颜色格式无效，则回退至兜底颜色。
        /// </summary>
        private static Brush CreateBrushOrDefault(string? colorText, string fallbackHexColor)
        {
            if (!string.IsNullOrWhiteSpace(colorText) &&
                TryCreateFrozenBrush(colorText.Trim(), out Brush? customBrush) &&
                customBrush != null)
            {
                return customBrush;
            }

            return CreateFrozenBrush(fallbackHexColor);
        }

        /// <summary>
        /// 创建并冻结不可变画刷，减少 UI 渲染与内存分配开销。
        /// </summary>
        private static SolidColorBrush CreateFrozenBrush(string hexColor)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// 尝试解析颜色字符串并创建冻结画刷，解析失败时返回 false。
        /// </summary>
        private static bool TryCreateFrozenBrush(string colorText, out Brush? brush)
        {
            try
            {
                var parsed = ColorConverter.ConvertFromString(colorText);
                if (parsed is Color color)
                {
                    var solidBrush = new SolidColorBrush(color);
                    solidBrush.Freeze();
                    brush = solidBrush;
                    return true;
                }
            }
            catch
            {
                // 颜色格式无效时交由调用方回退默认值
            }

            brush = null;
            return false;
        }
    }
}