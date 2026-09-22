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
    }
}