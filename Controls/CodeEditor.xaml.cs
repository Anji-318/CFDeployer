using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace CFDeployer.Controls
{
    public partial class CodeEditor : UserControl
    {
        public string Code
        {
            get => CodeTextBox.Text;
            set => CodeTextBox.Text = value;
        }

        public CodeEditor()
        {
            InitializeComponent();
            UpdateLineNumbers();
        }

        private void UpdateLineNumbers()
        {
            var lines = CodeTextBox.Text.Split('\n').Length;
            LineNumbers.Text = string.Join("\n", Enumerable.Range(1, lines));
        }

        private void CodeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateLineNumbers();
        }

        private void CodeTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Tab)
            {
                e.Handled = true;
                var caretIndex = CodeTextBox.CaretIndex;
                CodeTextBox.Text = CodeTextBox.Text.Insert(caretIndex, "  ");
                CodeTextBox.CaretIndex = caretIndex + 2;
            }
        }

        private void FormatCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var code = CodeTextBox.Text;
                var lines = code.Split('\n');
                var indent = 0;
                var formatted = new System.Collections.Generic.List<string>();

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    if (trimmed.StartsWith("}") || trimmed.StartsWith("]") || trimmed.StartsWith(")"))
                    {
                        indent = System.Math.Max(0, indent - 1);
                    }

                    formatted.Add(new string(' ', indent * 2) + trimmed);

                    if (trimmed.EndsWith("{") || trimmed.EndsWith("[") || trimmed.EndsWith("(") ||
                        trimmed.EndsWith("=>") || trimmed.EndsWith(":"))
                    {
                        indent++;
                    }
                }

                CodeTextBox.Text = string.Join("\n", formatted);
            }
            catch { }
        }

        private void UploadFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JavaScript files (*.js)|*.js|TypeScript files (*.ts)|*.ts|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                CodeTextBox.Text = File.ReadAllText(dialog.FileName);
            }
        }

        private void DownloadFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JavaScript files (*.js)|*.js|All files (*.*)|*.*",
                FileName = "worker.js"
            };

            if (dialog.ShowDialog() == true)
            {
                File.WriteAllText(dialog.FileName, CodeTextBox.Text);
            }
        }
    }
}
