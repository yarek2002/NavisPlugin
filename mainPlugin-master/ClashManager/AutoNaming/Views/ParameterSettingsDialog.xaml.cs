using System.Windows;

namespace ClashManager.AutoNaming.Views
{
    public partial class ParameterSettingsDialog : Window
    {
        public string ParameterSeparator { get; private set; }

        public ParameterSettingsDialog()
        {
            InitializeComponent();
        }

        public ParameterSettingsDialog(string parameterSeparator = ",")
        {
            InitializeComponent();

            ParameterSeparator = parameterSeparator ?? ",";
            ParameterSeparatorTextBox.Text = ParameterSeparator;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Save the separator setting
            ParameterSeparator = ParameterSeparatorTextBox.Text?.Trim() ?? ",";

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
