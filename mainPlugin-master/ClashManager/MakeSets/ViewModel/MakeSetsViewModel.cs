using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using ClashManager.MakeSets.Models;
using System.IO;
using System.Windows;

namespace ClashManager.MakeSets.ViewModel
{
    public class MakeSetsViewModel
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private string _csvFilePath;
        public string CsvFilePath
        {
            get => _csvFilePath;
            set
            {
                _csvFilePath = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CsvFilePath)));
            }
        }
        private List<SetsWithParamModel> _setsWithParams = new List<SetsWithParamModel>();
        public List<SetsWithParamModel> SetsWithParams
        {
            get => _setsWithParams;
            set
            {
                _setsWithParams = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SetsWithParams)));
            }
        }
        public ICommand CreateSetsCommand { get; private set; }
        public MakeSetsViewModel()
        {
            BrowseCsvCommand = new RelayCommand(BrowseCsv);
            CreateSetsCommand = new RelayCommand(CreateSets);
        }

        public ICommand BrowseCsvCommand { get; private set; }
        private void BrowseCsv(object parameter)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                Title = "Выберите CSV файл"
            };

            if (dialog.ShowDialog() == true)
            {
                CsvFilePath = dialog.FileName;
                ParseCsv();
            }
        }
        private void CreateSets(object parameter)
        {
            SearchSetCreator creator = new SearchSetCreator();
            creator.SetsWithParams = this.SetsWithParams;
            creator.CsvFilePath = this.CsvFilePath;
            creator.CreateSets();
        }
        private List<SetsWithParamModel> ParseCsv()
        {
            SetsWithParams.Clear();
            try
            {
                string[] lines = File.ReadAllLines(CsvFilePath, Encoding.GetEncoding(1251));
                if (lines.Length == 0)
                {
                    MessageBox.Show("Файл пустой");
                    return SetsWithParams;
                }

                // Чтение заголовков
                string[] headers = lines[0].Split(';');
                if (!string.IsNullOrEmpty(headers[0].Trim()))
                {
                    MessageBox.Show("Первый столбец заголовков должен быть пустым");
                    return SetsWithParams;
                }

                List<string> paramHeaders = new List<string>();
                for (int i = 1; i < headers.Length; i++)
                {
                    string header = headers[i].Trim();
                    if (string.IsNullOrEmpty(header))
                    {
                        MessageBox.Show($"Пустой заголовок в столбце {i + 1}");
                        return SetsWithParams;
                    }
                    paramHeaders.Add(header);
                }
                MessageBox.Show($"Заголовки: {string.Join(", ", paramHeaders)}");

                // Чтение строк
                for (int row = 1; row < lines.Length; row++)
                {
                    string[] cells = lines[row].Split(';');
                    string setName = cells[0].Trim();
                    if (string.IsNullOrEmpty(setName)) continue;

                    for (int col = 1; col < cells.Length && col - 1 < paramHeaders.Count; col++)
                    {
                        if (cells[col].Trim() == "+")
                        {
                            SetsWithParams.Add(new SetsWithParamModel
                            {
                                SetName = setName,
                                ParamName = paramHeaders[col - 1]
                            });
                        }
                    }
                }

                // Отладка
                string result = string.Join("\n", SetsWithParams.Select(x => $"Набор: {x.SetName}, Параметр: {x.ParamName}"));
                MessageBox.Show($"Пары:\n{result}");
                return SetsWithParams;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка парсинга: {ex.Message}");
                return SetsWithParams;
            }
        }
    }
}
