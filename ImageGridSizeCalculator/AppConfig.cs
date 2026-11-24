using System.Text.Json.Serialization;

namespace ImageGridSizeCalculator
{
    public class AppConfig
    {
        public string SourceWidth { get; set; } = "300";
        public string SourceHeight { get; set; } = "300";
        public string MatrixColumns { get; set; } = "3";
        public string MatrixRows { get; set; } = "3";
        public string HorizontalSpacing { get; set; } = "10";
        public string VerticalSpacing { get; set; } = "10";
        public bool EnableBorder { get; set; } = false;
        public string BorderWidth { get; set; } = "20";
        public bool Topmost { get; set; } = false;

        // 以下为内部可调节配置，不写入配置文件
        [JsonIgnore]
        public double MaxPreviewAspectRatio { get; set; } = 10.0;

        [JsonIgnore]
        public string PreviewShapeColor { get; set; } = "#4CC1D8";

        [JsonIgnore]
        public string PreviewErrorColor { get; set; } = "#DDDDDD";

        [JsonIgnore]
        public string GrayTextColor { get; set; } = "#909399";

        [JsonIgnore]
        public string NormalInfoColor { get; set; } = "#606266";

        [JsonIgnore]
        public string ErrorTextColor { get; set; } = "#F56C6C";

        [JsonIgnore]
        public string SuccessHintColor { get; set; } = "#67C23A";

        [JsonIgnore]
        public double CopyHintDurationSeconds { get; set; } = 1.5;

        [JsonIgnore]
        public double StandardRatioMaxRelativeError { get; set; } = 0.001;

        [JsonIgnore]
        public double StandardRatioMaxPixelDiff { get; set; } = 1.0;
    }
}