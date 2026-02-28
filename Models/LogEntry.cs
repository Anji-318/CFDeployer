using System.Windows.Media;

namespace CFDeployer.Models
{
    public class LogEntry
    {
        public string Time { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Details { get; set; }
        public string Type { get; set; } = "info";
        public string LevelIcon { get; set; } = "ℹ️";
        public Brush Brush { get; set; } = Brushes.White;
        public Brush BackgroundBrush { get; set; } = Brushes.Transparent;
        
        // 添加 MessageColor 属性，与 Brush 保持一致
        public Brush MessageColor 
        { 
            get => Brush;
            set => Brush = value;
        }
    }
}