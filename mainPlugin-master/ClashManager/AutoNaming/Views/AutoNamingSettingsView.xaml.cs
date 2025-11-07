using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ClashManager.AutoNaming;

namespace ClashManager.AutoNaming.Views
{
    public partial class AutoNamingSettingsView : Window
    {
        public AutoNamingSettings AppliedSettings { get; private set; }

        public AutoNamingSettingsView()
        {
            InitializeComponent();
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            // Создаем объект настроек
            var settings = new AutoNamingSettings
            {
                IncludeParam1 = IncludeParam1CheckBox.IsChecked == true,
                Param1Name = Param1TextBox.Text?.Trim(),

                IncludeParam2 = IncludeParam2CheckBox.IsChecked == true,
                Param2Name = Param2TextBox.Text?.Trim(),

                IncludeParam3 = IncludeParam3CheckBox.IsChecked == true,
                Param3Name = Param3TextBox.Text?.Trim(),

                IncludeParam4 = IncludeParam4CheckBox.IsChecked == true,
                Param4Name = Param4TextBox.Text?.Trim(),

                IncludeParam5 = IncludeParam5CheckBox.IsChecked == true,
                Param5Name = Param5TextBox.Text?.Trim(),

                Separator = SeparatorTextBox.Text?.Trim() ?? " | "
            };

            // Показываем окно выбора тестов
            var testSelectionWindow = new TestSelectionView(settings);
            testSelectionWindow.Owner = this;
            var result = testSelectionWindow.ShowDialog();

            if (result == true)
            {
                // Сохраняем примененные настройки
                AppliedSettings = settings;

                // Применить настройки к выбранным тестам
                ApplySettingsToTests(settings, testSelectionWindow.SelectedTestGuids);
                MessageBox.Show($"Настройки применены к {testSelectionWindow.SelectedTestGuids.Count} тестам!", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
                this.DialogResult = true;
                this.Close();
            }
        }

        private void ApplySettingsToTests(AutoNamingSettings settings, List<System.Guid> selectedTestGuids)
        {
            // TODO: Реализовать применение настроек к выбранным тестам
            // Здесь будет логика применения кастомных параметров к выбранным тестам
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
