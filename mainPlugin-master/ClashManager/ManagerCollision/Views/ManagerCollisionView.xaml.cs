using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace ClashManager.ManagerCollision.Views
{
	public partial class ManagerCollisionView : Window
	{
		private Document _doc;
		private DocumentClash _documentClash;

		public ManagerCollisionView()
		{
			InitializeComponent();
			_doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
			_documentClash = _doc.GetClash();
			LoadTests();
		}

		private void LoadTests()
		{
			var tests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList() ?? Enumerable.Empty<ClashTest>().ToList();
			TestsList.ItemsSource = tests;
		}

		private void TestsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			var selectedTest = TestsList.SelectedItem as ClashTest;
			if (selectedTest == null)
			{
				CollisionsList.ItemsSource = null;
				return;
			}

			var results = selectedTest.Children
				.OfType<ClashResult>()
				.Select(r => new
				{
					Name = r.DisplayName,
					Status = r.Status.ToString(),
					Guid = r.Guid
				})
				.ToList();
			CollisionsList.ItemsSource = results;
		}

		private void ApplyRenameButton_Click(object sender, RoutedEventArgs e)
		{
			var selectedTest = TestsList.SelectedItem as ClashTest;
			if (selectedTest == null) { MessageBox.Show("Выберите тест."); return; }
			var findText = FindBox.Text ?? string.Empty;
			var replaceText = ReplaceBox.Text ?? string.Empty;

			try
			{
				// Определяем набор GUID выбранных в UI результатов, если выделение есть
				var selectedItems = CollisionsList?.SelectedItems?.Cast<object>()?.ToList();
				var selectedGuids = selectedItems?.Select(item =>
				{
					var guidProp = item.GetType().GetProperty("Guid");
					return guidProp != null ? (Guid)guidProp.GetValue(item) : Guid.Empty;
				})
				.Where(g => g != Guid.Empty)
				.ToHashSet() ?? new System.Collections.Generic.HashSet<Guid>();

				var results = selectedTest.Children.OfType<ClashResult>().ToList();

				foreach (var r in results)
				{
					// Если есть выделение — работаем только по выделенным
					if (selectedGuids.Count > 0 && !selectedGuids.Contains(r.Guid))
						continue;

					var originalName = r.DisplayName ?? string.Empty;
					var newName = string.IsNullOrEmpty(findText) ? originalName : originalName.Replace(findText, replaceText);
					if (newName == originalName) continue; // нет изменений

					var copy = (ClashResult)r.CreateCopy();
					copy.DisplayName = newName;
					_documentClash.TestsData.TestsReplaceWithCopy(r, copy);
				}

				TestsList_SelectionChanged(null, null);
				MessageBox.Show("Готово.");
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Ошибка при переименовании: {ex.Message}\n{ex.StackTrace}");
			}
		}
	}
} 