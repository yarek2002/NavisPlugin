using System;
using System.Windows;
using System.Windows.Forms;
using ClashManager.Automation;
using Forms = System.Windows.Forms;

namespace ClashManager.Externals
{
    public class ReportAutomationCmd : IExternalCommand
    {
        public void Execute()
        {
            try
            {
                // Запрашиваем папку для выгрузки
                var dialog = new Forms.FolderBrowserDialog();
                dialog.Description = "Выберите папку для выгрузки отчета";
                dialog.ShowNewFolderButton = true;
                
                if (dialog.ShowDialog() == Forms.DialogResult.OK)
                {
                    string outputPath = dialog.SelectedPath;
                    
                    // Запрашиваем подтверждение
                    var result = Forms.MessageBox.Show(
                        $"Выполнить автоматизацию выгрузки отчета в папку:\n{outputPath}?\n\nПроцесс включает:\n1. Обновление всех тестов\n2. Автогруппировку и кластеризацию\n3. Авто-наименование\n4. Выгрузку отчета",
                        "Подтверждение автоматизации",
                        Forms.MessageBoxButtons.YesNo,
                        Forms.MessageBoxIcon.Question);
                    
                    if (result == Forms.DialogResult.Yes)
                    {
                        // Показываем прогресс
                        var progressWindow = new ProgressWindow();
                        progressWindow.Show();
                        
                        try
                        {
                            var automation = new ReportAutomation(outputPath);
                            automation.ExecuteFullAutomation();
                            
                            progressWindow.Close();
                            
                            Forms.MessageBox.Show(
                                "Автоматизация завершена успешно!\n\nОтчет сохранен в указанную папку.\nЛог выполнения сохранен в automation_log.txt",
                                "Автоматизация завершена",
                                Forms.MessageBoxButtons.OK,
                                Forms.MessageBoxIcon.Information);
                        }
                        catch (Exception ex)
                        {
                            progressWindow.Close();
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Forms.MessageBox.Show(
                    $"Ошибка при выполнении автоматизации:\n{ex.Message}\n\nПодробности в логе ошибок.",
                    "Ошибка автоматизации",
                    Forms.MessageBoxButtons.OK,
                    Forms.MessageBoxIcon.Error);
            }
        }
    }
    
    /// <summary>
    /// Простое окно прогресса
    /// </summary>
    public partial class ProgressWindow : Window
    {
        public ProgressWindow()
        {
            InitializeComponent();
        }
        
        private void InitializeComponent()
        {
            Title = "Автоматизация отчетов";
            Width = 400;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.NoResize;
            
            var grid = new System.Windows.Controls.Grid();
            
            var textBlock = new System.Windows.Controls.TextBlock
            {
                Text = "Выполняется автоматизация выгрузки отчетов...\nПожалуйста, подождите.",
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                TextAlignment = System.Windows.TextAlignment.Center,
                FontSize = 14
            };
            
            grid.Children.Add(textBlock);
            Content = grid;
        }
    }
}
