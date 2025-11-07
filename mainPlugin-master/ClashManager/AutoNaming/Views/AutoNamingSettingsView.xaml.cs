using System;
using System.Windows;
using System.Windows.Controls;

namespace ClashManager.AutoNaming.Views
{
    public partial class AutoNamingSettingsView : Window
    {
        public AutoNamingSettingsView()
        {
            InitializeComponent();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            // Показываем окно выбора тестов
            var testSelectionWindow = new TestSelectionView();
            testSelectionWindow.Owner = this;
            var result = testSelectionWindow.ShowDialog();

            if (result == true)
            {
                // TODO: Применить настройки к выбранным тестам
                MessageBox.Show($"Настройки будут применены к {testSelectionWindow.SelectedTestGuids.Count} тестам!", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                this.DialogResult = true;
                this.Close();
            }
        }

        private void CheckParametersButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Реализовать проверку параметров
            string message = "Текущие настройки:\n\n";

            if (IncludeParam1CheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(Param1TextBox.Text))
                message += $"Параметр 1: '{Param1TextBox.Text}'\n";

            if (IncludeParam2CheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(Param2TextBox.Text))
                message += $"Параметр 2: '{Param2TextBox.Text}'\n";

            if (IncludeParam3CheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(Param3TextBox.Text))
                message += $"Параметр 3: '{Param3TextBox.Text}'\n";

            if (IncludeParam4CheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(Param4TextBox.Text))
                message += $"Параметр 4: '{Param4TextBox.Text}'\n";

            if (IncludeParam5CheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(Param5TextBox.Text))
                message += $"Параметр 5: '{Param5TextBox.Text}'\n";

            message += $"Разделитель: '{SeparatorTextBox.Text}'";

            MessageBox.Show(message, "Проверка параметров", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
