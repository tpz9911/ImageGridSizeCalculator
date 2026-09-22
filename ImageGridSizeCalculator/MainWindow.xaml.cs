using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;

namespace ImageGridSizeCalculator
{
    public partial class MainWindow : Window
    {
        private static readonly Regex NonDigitRegex = new Regex(@"[^\d]+", RegexOptions.Compiled);
        private readonly string _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "igsc_config.json");

        public MainWindow()
        {
            InitializeComponent();
            RegisterPastingValidation();
            LoadConfiguration();
            Calculate();
        }

        #region 配置读写 (igsc_config.json)

        private void LoadConfiguration()
        {
            AppConfig config = null;

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

            // 若文件不存在或反序列化失败，使用默认配置
            config ??= new AppConfig();

            TxtSourceWidth.Text = config.SourceWidth;
            TxtSourceHeight.Text = config.SourceHeight;
            TxtMatrixColumns.Text = config.MatrixColumns;
            TxtMatrixRows.Text = config.MatrixRows;
            TxtHorizontalSpacing.Text = config.HorizontalSpacing;
            TxtVerticalSpacing.Text = config.VerticalSpacing;
            ChkEnableBorder.IsChecked = config.EnableBorder;
            TxtBorderWidth.Text = config.BorderWidth;
        }

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
                    BorderWidth = TxtBorderWidth.Text
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

        #region 数字输入约束

        private void RegisterPastingValidation()
        {
            TextBox[] textBoxes = [
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

        private void OnTextBoxPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string text = (string)e.DataObject.GetData(typeof(string));
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

        #endregion

        #region 计算逻辑

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

        private void Calculate()
        {
            if (TxtSourceWidth == null || TxtSourceHeight == null ||
                TxtMatrixColumns == null || TxtMatrixRows == null ||
                TxtHorizontalSpacing == null || TxtVerticalSpacing == null ||
                TxtBorderWidth == null || ChkEnableBorder == null)
            {
                return;
            }

            bool valid = true;
            valid &= int.TryParse(TxtSourceWidth.Text.Trim(), out int sourceWidth) && sourceWidth > 0;
            valid &= int.TryParse(TxtSourceHeight.Text.Trim(), out int sourceHeight) && sourceHeight > 0;
            valid &= int.TryParse(TxtMatrixColumns.Text.Trim(), out int matrixColumns) && matrixColumns > 0;
            valid &= int.TryParse(TxtMatrixRows.Text.Trim(), out int matrixRows) && matrixRows > 0;
            valid &= int.TryParse(TxtHorizontalSpacing.Text.Trim(), out int horizontalSpacing) && horizontalSpacing >= 0;
            valid &= int.TryParse(TxtVerticalSpacing.Text.Trim(), out int verticalSpacing) && verticalSpacing >= 0;

            int borderWidth = 0;
            if (ChkEnableBorder.IsChecked == true)
            {
                valid &= int.TryParse(TxtBorderWidth.Text.Trim(), out borderWidth) && borderWidth >= 0;
            }

            if (!valid)
            {
                LblTargetWidth.Text = "--";
                LblTargetHeight.Text = "--";
                LblStatus.Text = "请输入有效的正整数尺寸与非负间距/边框值";
                LblStatus.Visibility = Visibility.Visible;
                return;
            }

            LblStatus.Visibility = Visibility.Collapsed;

            // 基础网格拼接尺寸
            long targetWidth = (long)sourceWidth * matrixColumns + (long)(matrixColumns - 1) * horizontalSpacing;
            long targetHeight = (long)sourceHeight * matrixRows + (long)(matrixRows - 1) * verticalSpacing;

            // 计入外边框（上下、左右各2倍）
            if (ChkEnableBorder.IsChecked == true)
            {
                targetWidth += (long)borderWidth * 2;
                targetHeight += (long)borderWidth * 2;
            }

            LblTargetWidth.Text = $"{targetWidth:N0} px";
            LblTargetHeight.Text = $"{targetHeight:N0} px";
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
    }
}