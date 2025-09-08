using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using ClashManager.MakeSets.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace ClashManager.MakeSets.ViewModel
{
    public class SearchSetCreator
    {
        public List<SetsWithParamModel> SetsWithParams { get; set; }
        public string CsvFilePath { get; set; }

        private SelectionSet GetExistingSelectionSet(string setName)
        {
            Document doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            return FindSelectionSetInCollection(doc.SelectionSets.Value, setName);
        }
        private SelectionSet FindSelectionSetInCollection(SavedItemCollection collection, string setName)
        {
            SelectionSet foundSet = collection.FirstOrDefault(s => s is SelectionSet && s.DisplayName == setName) as SelectionSet;
            if (foundSet != null)
                return foundSet;

            foreach (var item in collection)
            {
                if (item is FolderItem folder)
                {
                    foundSet = FindSelectionSetInCollection(folder.Children, setName);
                    if (foundSet != null)
                        return foundSet;
                }
            }
            return null;
        }
        private ModelItemCollection FindItems(SelectionSet existingSet, string paramName)
        {
            Search search = new Search();
            search.Selection.CopyFrom(existingSet.GetSelectedItems());
            search.SearchConditions.Add(SearchCondition.HasPropertyByDisplayName("Объект", paramName).Negate());
            search.SearchConditions.Add(SearchCondition.HasPropertyByDisplayName("Тип в приложении Revit", paramName).Negate());
            return search.FindAll(Autodesk.Navisworks.Api.Application.ActiveDocument, false);
        }

        private void CreateNewSelectionSet(Document doc, ModelItemCollection items, string setName, string paramName, List<string[]> reportRows)
        {
            SelectionSet newSet = new SelectionSet(items);
            newSet.DisplayName = setName + "_" + paramName;
            doc.SelectionSets.AddCopy(newSet);
            reportRows.Add(new[] { setName, paramName, "Success", "Создан набор элементов", items.Count.ToString() });
        }
        public ModelItemCollection GetCurrentElement(ModelItemCollection modelItems)
        {
            ModelItemCollection currentItems = new ModelItemCollection();
            foreach (ModelItem item in modelItems)
            {
                DataProperty propertyByDisplayName = item.PropertyCategories.FindPropertyByDisplayName("Объект", "Id");
                if (propertyByDisplayName != null)
                {
                    currentItems.Add(item);
                }
            }
            return currentItems;
        }
        public void CreateSets()
        {
            try
            {
                Document doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
                List<string[]> reportRows = new List<string[]>
                {
                    new[] { "SetName", "ParamName", "Status", "Message", "Count" }
                };

                if (SetsWithParams == null || !SetsWithParams.Any())
                {
                    reportRows.Add(new[] { "", "", "Failed", "Список пар SetName-ParamName пуст", "0" });
                }
                else
                {
                    foreach (var pair in SetsWithParams)
                    {
                        SelectionSet existingSet = GetExistingSelectionSet(pair.SetName);
                        if (existingSet == null)
                        {
                            reportRows.Add(new[] { pair.SetName, pair.ParamName, "Failed", "Набор не найден", "0" });
                        }
                        else
                        {
                            ModelItemCollection items = FindItems(existingSet, pair.ParamName);
                            ModelItemCollection currentItems = GetCurrentElement(items);
                            CreateNewSelectionSet(doc, currentItems, pair.SetName, pair.ParamName, reportRows);
                        }
                    }
                }
                if (!string.IsNullOrEmpty(CsvFilePath))
                {
                    string directory = Path.GetDirectoryName(CsvFilePath);
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string reportPath = Path.Combine(directory, $"Report_{timestamp}.csv");
                    using (StreamWriter writer = new StreamWriter(reportPath, false, Encoding.GetEncoding(1251)))
                    {
                        foreach (var row in reportRows)
                        {
                            writer.WriteLine(string.Join(";", row));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");
            }
        }
    }
}