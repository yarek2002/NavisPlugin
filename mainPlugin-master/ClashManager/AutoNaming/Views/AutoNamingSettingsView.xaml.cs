using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ClashManager.AutoNaming;

namespace ClashManager.AutoNaming.Views
{
    public partial class AutoNamingSettingsView : Window
    {
        public TestAutoNamingSettings AppliedSettings { get; private set; }
        public List<System.Guid> SelectedTestGuids { get; set; } = new List<System.Guid>();

        // Observable collection for dynamic parameters
        public ObservableCollection<ParameterItem> Parameters { get; set; } = new ObservableCollection<ParameterItem>();

        public AutoNamingSettingsView()
        {
            InitializeComponent();

            // Load existing settings if available
            LoadExistingSettings();

            // Bind the ItemsControl to our parameters collection
            ParametersItemsControl.ItemsSource = Parameters;
        }

        private void LoadExistingSettings()
        {
            // Try to load settings for the first selected test (if any)
            if (SelectedTestGuids.Count > 0)
            {
                var allSettings = AutoNamingSettings.LoadFromFile();
                var testSettings = allSettings.GetTestSettings(SelectedTestGuids[0]);

                if (testSettings != null)
                {
                    // Copy existing parameters
                    Parameters.Clear();
                    foreach (var param in testSettings.Parameters)
                    {
                        Parameters.Add(new ParameterItem(param.IsEnabled, param.ParameterName));
                    }

                    // Set separator
                    SeparatorTextBox.Text = testSettings.Separator ?? " | ";
                }
                else
                {
                    // No existing settings, start with one empty parameter
                    Parameters.Add(new ParameterItem());
                }
            }
            else
            {
                // No tests selected, start with one empty parameter
                Parameters.Add(new ParameterItem());
            }
        }

        private void AddParameterButton_Click(object sender, RoutedEventArgs e)
        {
            // Show dialog to enter parameter name
            var dialog = new ParameterNameDialog();
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                string parameterName = dialog.ParameterName?.Trim();
                if (!string.IsNullOrEmpty(parameterName))
                {
                    Parameters.Add(new ParameterItem(false, parameterName));
                }
                else
                {
                    Parameters.Add(new ParameterItem());
                }
            }
        }

        private void RemoveParameterButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button != null && button.Tag is ParameterItem parameterItem)
            {
                // Don't allow removing the last parameter
                if (Parameters.Count > 1)
                {
                    Parameters.Remove(parameterItem);
                }
                else
                {
                    MessageBox.Show("Нельзя удалить последний параметр!", "Предупреждение",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            // Create settings object from current UI state
            var settings = new TestAutoNamingSettings
            {
                Parameters = new List<ParameterItem>(Parameters),
                Separator = SeparatorTextBox.Text?.Trim() ?? " | "
            };

            // Сохраняем примененные настройки
            AppliedSettings = settings;

            // Загружаем существующие настройки и добавляем/обновляем настройки для выбранных тестов
            var allSettings = AutoNamingSettings.LoadFromFile();
            allSettings.SetTestSettings(SelectedTestGuids, settings);
            allSettings.SaveToFile();

            MessageBox.Show($"Настройки сохранены для {SelectedTestGuids.Count} тестов!", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            this.DialogResult = true;
            this.Close();
        }

        private void CheckParametersButton_Click(object sender, RoutedEventArgs e)
        {
            string message = "Текущие настройки:\n\n";

            int paramIndex = 1;
            foreach (var param in Parameters)
            {
                if (param.IsEnabled && !string.IsNullOrWhiteSpace(param.ParameterName))
                {
                    message += $"Параметр {paramIndex}: '{param.ParameterName}' (включен)\n";
                }
                else if (!string.IsNullOrWhiteSpace(param.ParameterName))
                {
                    message += $"Параметр {paramIndex}: '{param.ParameterName}' (отключен)\n";
                }
                else
                {
                    message += $"Параметр {paramIndex}: (пустой)\n";
                }
                paramIndex++;
            }

            message += $"Разделитель: '{SeparatorTextBox.Text}'";

            MessageBox.Show(message, "Проверка параметров", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }

    /// <summary>
    /// Диалог для ввода имени параметра
    /// </summary>
    public class ParameterNameDialog : Window
    {
        public string ParameterName { get; private set; }

        private TextBox _parameterNameTextBox;

        public ParameterNameDialog()
        {
            Title = "Введите имя параметра";
            Width = 300;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Margin = new Thickness(10);

            // Label
            var label = new TextBlock
            {
                Text = "Имя параметра:",
                Margin = new Thickness(0, 0, 0, 5)
            };
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            // TextBox
            _parameterNameTextBox = new TextBox
            {
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(_parameterNameTextBox, 1);
            grid.Children.Add(_parameterNameTextBox);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetRow(buttonPanel, 2);

            var okButton = new Button
            {
                Content = "OK",
                Width = 60,
                Margin = new Thickness(0, 0, 5, 0),
                IsDefault = true
            };
            okButton.Click += OkButton_Click;

            var cancelButton = new Button
            {
                Content = "Отмена",
                Width = 60,
                IsCancel = true
            };
            cancelButton.Click += CancelButton_Click;

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            grid.Children.Add(buttonPanel);

            Content = grid;

            // Set focus to textbox
            Loaded += (s, e) => _parameterNameTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            ParameterName = _parameterNameTextBox.Text?.Trim();
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
