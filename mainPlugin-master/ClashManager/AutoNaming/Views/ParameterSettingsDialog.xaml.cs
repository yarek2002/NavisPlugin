using System.Windows;

namespace ClashManager.AutoNaming.Views
{
    public partial class ParameterSettingsDialog : Window
    {
        public bool DontRepeatParameter { get; private set; }
        public bool ParameterForEachCollisionElement { get; private set; }
        public string ParameterSeparator { get; private set; }

        public ParameterSettingsDialog()
        {
            InitializeComponent();
        }

        public ParameterSettingsDialog(bool dontRepeatParameter, bool parameterForEachCollisionElement, string parameterSeparator = ",")
        {
            InitializeComponent();

            DontRepeatParameter = dontRepeatParameter;
            ParameterForEachCollisionElement = parameterForEachCollisionElement;
            ParameterSeparator = parameterSeparator ?? ",";

            // Set initial values
            DontRepeatParameterCheckBox.IsChecked = dontRepeatParameter;
            ParameterForEachElementCheckBox.IsChecked = parameterForEachCollisionElement;
            ParameterSeparatorTextBox.Text = ParameterSeparator;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Save the settings
            DontRepeatParameter = DontRepeatParameterCheckBox.IsChecked == true;
            ParameterForEachCollisionElement = ParameterForEachElementCheckBox.IsChecked == true;
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
