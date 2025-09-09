using System;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using System.Reflection;
using Autodesk.Navisworks.Api.Interop;
using Autodesk.Navisworks.Api.ComApi;
using System.Windows.Input;
using System.Windows.Forms.Integration;
using System.Threading;
using System.Windows.Threading;
using System.IO;
using System.Windows.Media;
using Application = Autodesk.Navisworks.Api.Application;

namespace ClashManager.ManagerCollision.Views
{
	public partial class ManagerCollisionView : Window
		{
		private Document _doc;
		private DocumentClash _documentClash;
		// Чекбоксы выбора: для строк коллизий и для тестов
		private readonly System.Collections.Generic.HashSet<Guid> _checkedRowIds = new System.Collections.Generic.HashSet<Guid>();
		private readonly System.Collections.Generic.HashSet<Guid> _checkedTestIds = new System.Collections.Generic.HashSet<Guid>();
		private int _lastTestClickIndex = -1;
		private int _lastCollisionClickIndex = -1;
		private bool _suppressCheckboxHandlers = false;
		private bool _searchByNameMode = true; // true = по имени, false = по GUID
		private readonly System.Collections.Generic.Dictionary<string, bool> _sortDirections = new System.Collections.Generic.Dictionary<string, bool>();
		private DispatcherTimer _searchTimer;
		private string _lastSearchQuery = string.Empty;
		private DispatcherTimer _clashDetectiveMonitorTimer;
		private Guid _lastDetectedClashGuid = Guid.Empty;
		private bool _isSyncingFromPlugin = false;

		public ManagerCollisionView()
		{
			InitializeComponent();
			// Сброс лога при старте окна
			try { File.WriteAllText(GetLogPath(), ""); } catch { }
			// Включаем клавиатурную интероп-совместимость для модельного окна в Win32-хосте (Navisworks)
			try { ElementHost.EnableModelessKeyboardInterop(this); } catch { }
			// Обеспечим корректную активацию окна и ввод с клавиатуры
			try
			{
				// Привязываем Owner к главному окну процесса Navisworks для корректной активации
				var hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
				if (hwnd != IntPtr.Zero)
				{
					new System.Windows.Interop.WindowInteropHelper(this).Owner = hwnd;
				}
			}
			catch { }
			this.WindowStyle = WindowStyle.SingleBorderWindow; // Обычный стиль окна
			this.ShowInTaskbar = false; // Не обязательно, но удобно
			this.Topmost = true; // Позволяет окну получать фокус поверх Navisworks
			this.Loaded += (s, e) =>
			{
				try
				{
					this.Activate();
					this.Focus();
					// Установим фокус в первое поле ввода, чтобы сразу работала клавиатура
					if (FindBox != null)
						System.Windows.Input.Keyboard.Focus(FindBox);
				}
				catch { }
			};
			_doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
			_documentClash = _doc.GetClash();
			LoadTests();

			// Initialize search timer for dynamic filtering
			_searchTimer = new DispatcherTimer();
			_searchTimer.Interval = TimeSpan.FromMilliseconds(300);
			_searchTimer.Tick += SearchTimer_Tick;

			// Initialize Clash Detective monitoring timer
			_clashDetectiveMonitorTimer = new DispatcherTimer();
			_clashDetectiveMonitorTimer.Interval = TimeSpan.FromMilliseconds(500);
			_clashDetectiveMonitorTimer.Tick += ClashDetectiveMonitorTimer_Tick;
			StartClashDetectiveMonitoring();
		}

		private void LoadTests()
		{
			var tests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList() ?? Enumerable.Empty<ClashTest>().ToList();
			// Оборачиваем в объекты с IsSelected для чекбоксов
			var testRows = tests.Select(t => new { Test = t, DisplayName = t.DisplayName, IsSelected = false, Guid = t.Guid }).ToList();
			TestsList.ItemsSource = testRows;
		}

		private sealed class ResultRow
		{
			public ClashResult Result { get; set; }
			public string GroupName { get; set; }
			public ClashResultGroup ParentGroup { get; set; }
		}

        public static (string LevelName, string IntersectionName, string Line1, string Line2, Point3D Position)
         GetClashGridInfo(ClashResult clash)
        {
            const string NA = "N/A";

            if (clash == null || clash.Center == null)
                return (NA, NA, NA, NA, null);

            try
            {
                // Try to access the Grids property dynamically
                var doc = Application.ActiveDocument;
                if (doc == null)
                    return (NA, NA, NA, NA, null);

                // Use reflection to access Grids property safely
                var gridsProp = doc.GetType().GetProperty("Grids", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (gridsProp == null)
                {
                    // Grids property doesn't exist in this API version
                    return (NA, NA, NA, NA, null);
                }

                var docGrids = gridsProp.GetValue(doc);
                if (docGrids == null)
                    return (NA, NA, NA, NA, null);

                // Get the Systems property
                var systemsProp = docGrids.GetType().GetProperty("Systems", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (systemsProp == null)
                    return (NA, NA, NA, NA, null);

                var systems = systemsProp.GetValue(docGrids) as System.Collections.IEnumerable;
                if (systems == null)
                    return (NA, NA, NA, NA, null);

                GridIntersection nearest = null;
                double minDist = double.MaxValue;

                foreach (var systemObj in systems)
                {
                    if (systemObj == null) continue;

                    // Get Levels property
                    var levelsProp = systemObj.GetType().GetProperty("Levels", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (levelsProp == null) continue;

                    var levels = levelsProp.GetValue(systemObj) as System.Collections.IEnumerable;
                    if (levels == null) continue;

                    foreach (var levelObj in levels)
                    {
                        if (levelObj == null) continue;

                        // Try to get closest intersection
                        var closestIntersectionMethod = levelObj.GetType().GetMethod("ClosestIntersection",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                            null, new[] { typeof(Point3D) }, null);

                        if (closestIntersectionMethod == null) continue;

                        GridIntersection gi = null;
                        try
                        {
                            gi = closestIntersectionMethod.Invoke(levelObj, new object[] { clash.Center }) as GridIntersection;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error getting closest intersection: {ex.Message}");
                            continue;
                        }

                        if (gi == null) continue;

                        // Get Position property
                        var giPositionProp = gi.GetType().GetProperty("Position", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (giPositionProp == null) continue;

                        var giPosition = giPositionProp.GetValue(gi) as Point3D;
                        if (giPosition == null) continue;

                        double dist = clash.Center.DistanceTo(giPosition);
                        if (dist < minDist)
                        {
                            minDist = dist;
                            nearest = gi;
                        }
                    }
                }

                if (nearest == null)
                    return (NA, NA, NA, NA, null);

                // Extract information from the nearest intersection
                string levelName = GetGridPropertyValue(nearest, "Level", "DisplayName") ?? NA;
                string intersectionName = GetGridPropertyValue(nearest, "DisplayName") ?? NA;
                string line1 = GetGridPropertyValue(nearest, "Line1", "DisplayName") ?? NA;
                string line2 = GetGridPropertyValue(nearest, "Line2", "DisplayName") ?? NA;

                var positionProp = nearest.GetType().GetProperty("Position", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                Point3D pos = positionProp?.GetValue(nearest) as Point3D;

                return (levelName, intersectionName, line1, line2, pos);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetClashGridInfo: {ex.Message}");
                return (NA, NA, NA, NA, null);
            }
        }

        /// <summary>
        /// Helper method to safely get property values from grid objects using reflection
        /// </summary>
        private static string GetGridPropertyValue(object obj, params string[] propertyPath)
        {
            if (obj == null || propertyPath == null || propertyPath.Length == 0)
                return null;

            object current = obj;
            foreach (string propName in propertyPath)
            {
                if (current == null) return null;

                var prop = current.GetType().GetProperty(propName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop == null) return null;

                current = prop.GetValue(current);
            }

            return current?.ToString();
        }

        /// <summary>
        /// Alternative method for determining axis intersection using a different approach
        /// This method searches for intersections within a specified tolerance and considers multiple candidates
        /// </summary>
        public static (string LevelName, string IntersectionName, string Line1, string Line2, Point3D Position)
         GetClashGridInfoAlternative(ClashResult clash, double tolerance = 5.0)
        {
            const string NA = "N/A";

            if (clash == null || clash.Center == null)
                return (NA, NA, NA, NA, null);

            var docGrids = Application.ActiveDocument?.Grids;
            if (docGrids == null)
                return (NA, NA, NA, NA, null);

            var systems = docGrids.Systems;
            if (systems == null || systems.Count == 0)
                return (NA, NA, NA, NA, null);

            GridIntersection bestIntersection = null;
            double bestScore = double.MaxValue;

            // Try with larger tolerance and more test points
            double[] tolerances = { tolerance, tolerance * 2, tolerance * 5 };
            double[] offsets = { 0.0, 0.01, 0.05, 0.1, 0.5, 1.0 };

            foreach (GridSystem system in systems)
            {
                foreach (GridLevel level in system.Levels)
                {
                    foreach (double currentTolerance in tolerances)
                    {
                        // Try multiple test points with different offsets
                        foreach (double xOffset in offsets)
                        {
                            foreach (double yOffset in offsets)
                            {
                                if (xOffset == 0.0 && yOffset == 0.0) continue; // Skip center point for now

                                Point3D testPoint = new Point3D(
                                    clash.Center.X + xOffset,
                                    clash.Center.Y + yOffset,
                                    clash.Center.Z
                                );

                                GridIntersection testGi = null;
                                try
                                {
                                    testGi = level.ClosestIntersection(testPoint);
                                }
                                catch { continue; }

                                if (testGi != null)
                                {
                                    double dist = clash.Center.DistanceTo(testGi.Position);

                                    if (dist <= currentTolerance)
                                    {
                                        // Calculate a score based on distance and level proximity
                                        double levelDiff = Math.Abs(clash.Center.Z - testGi.Position.Z);
                                        double score = dist + levelDiff * 0.1; // Weight level difference less

                                        if (score < bestScore)
                                        {
                                            bestScore = score;
                                            bestIntersection = testGi;
                                        }
                                    }
                                }
                            }
                        }

                        // Also try the original center point
                        GridIntersection centerGi = null;
                        try
                        {
                            centerGi = level.ClosestIntersection(clash.Center);
                        }
                        catch { continue; }

                        if (centerGi != null)
                        {
                            double dist = clash.Center.DistanceTo(centerGi.Position);

                            if (dist <= currentTolerance)
                            {
                                double levelDiff = Math.Abs(clash.Center.Z - centerGi.Position.Z);
                                double score = dist + levelDiff * 0.1;

                                if (score < bestScore)
                                {
                                    bestScore = score;
                                    bestIntersection = centerGi;
                                }
                            }
                        }
                    }
                }
            }

            if (bestIntersection == null)
                return (NA, NA, NA, NA, null);

            string levelName = bestIntersection.Level?.DisplayName ?? NA;
            string intersectionName = bestIntersection.DisplayName ?? NA;
            string line1 = bestIntersection.Line1?.DisplayName ?? NA;
            string line2 = bestIntersection.Line2?.DisplayName ?? NA;
            Point3D pos = bestIntersection.Position;

            return (levelName, intersectionName, line1, line2, pos);
        }

        private void TestsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			// Если выбрано несколько тестов через чекбоксы — показываем объединённый список коллизий этих тестов
			var allTests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList() ?? new System.Collections.Generic.List<ClashTest>();
			var checkedTests = allTests.Where(t => _checkedTestIds.Contains(t.Guid)).ToList();
			if (checkedTests.Count > 0)
			{
				var mergedRows = new System.Collections.Generic.List<object>();
				foreach (var t in checkedTests)
				{
					var groupRowsMerged = EnumerateAllGroupsWithLevel(t)
						.Select(x => new
						{
							Name = x.Group.DisplayName ?? string.Empty,
							Status = x.Group.Status.ToString(),
							AssignedTo = (x.Group.AssignedTo ?? string.Empty).ToString(),
							Guid = x.Group.Guid,
							TestGuid = t.Guid,
							IsGroup = true,
							IsSelected = false,
							Level = GetLevelFromGroup(x.Group),
							GridIntersection = GetGridIntersectionInfo(null, null), // No items for groups
							TestName = t.DisplayName ?? string.Empty
						});
					var ungroupedResultRowsMerged = t.Children
						.OfType<ClashResult>()
						.Select(r => new
						{
							Name = r.DisplayName ?? string.Empty,
							Status = r.Status.ToString(),
							AssignedTo = (r.AssignedTo ?? string.Empty).ToString(),
							Guid = r.Guid,
							TestGuid = t.Guid,
							IsGroup = false,
							IsSelected = false,
							Level = GetLevelFromItems(r.CompositeItem1, r.CompositeItem2, r),
							GridIntersection = FormatGridIntersectionDisplay(r),
                            TestName = t.DisplayName ?? string.Empty
						});
					mergedRows.AddRange(groupRowsMerged);
					mergedRows.AddRange(ungroupedResultRowsMerged);
				}
				CollisionsList.ItemsSource = mergedRows;
				return;
			}

			var selectedTest = (TestsList.SelectedItem != null) ? (TestsList.SelectedItem.GetType().GetProperty("Test")?.GetValue(TestsList.SelectedItem) as ClashTest) : null;
			if (selectedTest == null)
			{
				CollisionsList.ItemsSource = null;
				return;
			}

			// Показываем по одной строке на группу, плюс отдельные (негрупповые) результаты теста
			var groupRows = EnumerateAllGroupsWithLevel(selectedTest)
				.Select(x => new
				{
					Name = x.Group.DisplayName ?? string.Empty,
					Status = x.Group.Status.ToString(),
					AssignedTo = (x.Group.AssignedTo ?? string.Empty).ToString(),
					Guid = x.Group.Guid,
					TestGuid = selectedTest.Guid,
					IsGroup = true,
					IsSelected = false,
					Level = GetLevelFromGroup(x.Group),
					GridIntersection = GetGridIntersectionInfo(null, null), // No items for groups
					TestName = selectedTest.DisplayName ?? string.Empty
				});

			var ungroupedResultRows = selectedTest.Children
				.OfType<ClashResult>()
				.Select(r => new
				{
					Name = r.DisplayName ?? string.Empty,
					Status = r.Status.ToString(),
					AssignedTo = (r.AssignedTo ?? string.Empty).ToString(),
					Guid = r.Guid,
					TestGuid = selectedTest.Guid,
					IsGroup = false,
					IsSelected = false,
							Level = GetLevelFromItems(r.CompositeItem1, r.CompositeItem2, r),
					GridIntersection = FormatGridIntersectionDisplay(r),
                    TestName = selectedTest.DisplayName ?? string.Empty
				});

			var rows = groupRows.Concat(ungroupedResultRows).ToList();
			CollisionsList.ItemsSource = rows;
		}

		private System.Collections.Generic.IEnumerable<(ClashResultGroup Group, int Level)> EnumerateAllGroupsWithLevel(ClashTest test)
		{
			foreach (var g in test.Children.OfType<ClashResultGroup>())
			{
				yield return (g, 1);
				foreach (var child in EnumerateAllGroupsWithLevel(g, 2))
					yield return child;
			}
		}

		private System.Collections.Generic.IEnumerable<(ClashResultGroup Group, int Level)> EnumerateAllGroupsWithLevel(ClashResultGroup group, int level)
		{
			foreach (var g in group.Children.OfType<ClashResultGroup>())
			{
				yield return (g, level);
				foreach (var child in EnumerateAllGroupsWithLevel(g, level + 1))
					yield return child;
			}
		}


		// Поиск и навигация
		private void SearchButton_Click(object sender, RoutedEventArgs e) => ApplySearch();
		private void ResetButton_Click(object sender, RoutedEventArgs e)
		{
			SetSearchText(string.Empty);
			TestsList_SelectionChanged(null, null);
		}
		private void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
		{
			if (e.Key == System.Windows.Input.Key.Enter) ApplySearch();
		}



		private void ApplySearch()
		{
			string query = (GetSearchText() ?? string.Empty).Trim();
			if (string.IsNullOrEmpty(query))
			{
				TestsList_SelectionChanged(null, null);
				return;
			}

			// Сначала попробуем найти и открыть конкретный элемент (старое поведение)
			if (TryGlobalSearchAndOpen(query)) return;

			// Если не нашли для открытия, фильтруем список
			ApplySearchFilter(query);
		}

		private void ApplySearchFilter(string query)
		{
			try
			{
				var allTests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList() ?? new System.Collections.Generic.List<ClashTest>();

				// Определяем, по каким тестам искать: выбранные через чекбоксы или текущий выбранный
				var testsToSearch = _checkedTestIds.Count > 0
					? allTests.Where(t => _checkedTestIds.Contains(t.Guid)).ToList()
					: new System.Collections.Generic.List<ClashTest>();

				// Если нет выбранных через чекбоксы, используем текущий выбранный тест
				if (testsToSearch.Count == 0)
				{
					var selectedTest = (TestsList.SelectedItem != null) ? (TestsList.SelectedItem.GetType().GetProperty("Test")?.GetValue(TestsList.SelectedItem) as ClashTest) : null;
					if (selectedTest != null)
					{
						testsToSearch.Add(selectedTest);
					}
				}

				if (testsToSearch.Count == 0)
				{
					CollisionsList.ItemsSource = null;
					return;
				}

				// Получаем все элементы из выбранных тестов
				var allItems = new System.Collections.Generic.List<object>();

				foreach (var test in testsToSearch)
				{
					// Добавляем группы
					var groupRows = EnumerateAllGroupsWithLevel(test)
						.Select(x => new
						{
							Name = x.Group.DisplayName ?? string.Empty,
							Status = x.Group.Status.ToString(),
							AssignedTo = (x.Group.AssignedTo ?? string.Empty).ToString(),
							Guid = x.Group.Guid,
							TestGuid = test.Guid,
							IsGroup = true,
							IsSelected = false,
							Level = GetLevelFromGroup(x.Group),
							GridIntersection = GetGridIntersectionInfo(null, null), // No items for groups
							TestName = test.DisplayName ?? string.Empty,
							Item = x.Group
						});

					// Добавляем отдельные результаты
					var ungroupedResultRows = test.Children
						.OfType<ClashResult>()
						.Select(r => new
						{
							Name = r.DisplayName ?? string.Empty,
							Status = r.Status.ToString(),
							AssignedTo = (r.AssignedTo ?? string.Empty).ToString(),
							Guid = r.Guid,
							TestGuid = test.Guid,
							IsGroup = false,
							IsSelected = false,
							Level = GetLevelFromItems(r.CompositeItem1, r.CompositeItem2, r),
							GridIntersection = FormatGridIntersectionDisplay(r),
							TestName = test.DisplayName ?? string.Empty,
							Item = r
						});

					allItems.AddRange(groupRows);
					allItems.AddRange(ungroupedResultRows);
				}

				// Фильтруем элементы, которые содержат запрос
				var filteredItems = allItems.Where(item =>
				{
					string name = item.GetType().GetProperty("Name")?.GetValue(item)?.ToString() ?? string.Empty;
					if (_searchByNameMode)
					{
						return name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
					}
					else
					{
						Guid guid = (Guid)(item.GetType().GetProperty("Guid")?.GetValue(item) ?? Guid.Empty);
						return guid.ToString().IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
					}
				}).ToList();

				// Сортируем: сначала элементы, начинающиеся с запроса, потом остальные
				var sortedItems = filteredItems.OrderByDescending(item =>
				{
					string name = item.GetType().GetProperty("Name")?.GetValue(item)?.ToString() ?? string.Empty;
					if (_searchByNameMode)
					{
						return name.StartsWith(query, StringComparison.OrdinalIgnoreCase);
					}
					else
					{
						Guid guid = (Guid)(item.GetType().GetProperty("Guid")?.GetValue(item) ?? Guid.Empty);
						return guid.ToString().StartsWith(query, StringComparison.OrdinalIgnoreCase);
					}
				}).ToList();

				CollisionsList.ItemsSource = sortedItems;
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Ошибка при фильтрации: {ex.Message}");
			}
		}

		private bool TryGlobalSearchAndOpen(string query)
		{
			try
			{
				var allTests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList();
				if (allTests == null || allTests.Count == 0) return false;

				// Если выбраны тесты через чекбоксы, ищем только по ним; иначе по всем тестам
				var tests = _checkedTestIds.Count > 0
					? allTests.Where(t => _checkedTestIds.Contains(t.Guid)).ToList()
					: allTests;

				foreach (var test in tests)
				{
					// Поиск по одиночным результатам (не в группах)
					foreach (var r in test.Children.OfType<ClashResult>())
					{
						bool match = false;
						if (_searchByNameMode)
						{
							// Поиск только по имени
							match = (r.DisplayName ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
						}
						else
						{
							// Поиск только по GUID
							match = r.Guid.ToString().IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
						}

						if (match)
						{
							OpenFound(test, r.Guid, false);
							return true;
						}
					}

					// Поиск по группам (все уровни)
					foreach (var tpl in EnumerateAllGroupsWithLevel(test))
					{
						var g = tpl.Group;
						bool match = false;
						if (_searchByNameMode)
						{
							// Поиск только по имени
							match = (g.DisplayName ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
						}
						else
						{
							// Поиск только по GUID
							match = g.Guid.ToString().IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
						}

						if (match)
						{
							OpenFound(test, g.Guid, true);
							return true;
						}
					}
				}
			}
			catch { }
			return false;
		}

		private void OpenFound(ClashTest test, Guid id, bool isGroup)
		{
			try
			{
				// Выбираем тест (ItemsSource содержит обёртки с полем Test)
				object testRowToSelect = null;
				foreach (var row in TestsList.Items)
				{
					var rowTest = row?.GetType().GetProperty("Test")?.GetValue(row) as ClashTest;
					if (rowTest != null && rowTest.Guid == test.Guid) { testRowToSelect = row; break; }
				}
				if (testRowToSelect != null)
				{
					TestsList.SelectedItem = testRowToSelect;
					TestsList_SelectionChanged(null, null);
				}

				// Выбираем строку
				object rowToSelect = null;
				foreach (var item in CollisionsList.Items)
				{
					var t = item.GetType();
					Guid rowId = t.GetProperty("Guid") != null ? (Guid)t.GetProperty("Guid").GetValue(item) : Guid.Empty;
					if (rowId == id) { rowToSelect = item; break; }
				}
				if (rowToSelect != null)
				{
					CollisionsList.SelectedItem = rowToSelect;
					CollisionsList.ScrollIntoView(rowToSelect);
				}

				// Открываем 3D и Clash Detective
				if (isGroup)
				{
					var group = FindGroupByGuid(test, id);
					if (group != null)
					{
						var items = new ModelItemCollection();
						foreach (var r in GetAllResultsFromGroup(group))
						{
							if (r.CompositeItem1 != null) items.Add(r.CompositeItem1);
							if (r.CompositeItem2 != null) items.Add(r.CompositeItem2);
						}
						ActivateClashDetective(test, group);
						FocusOnItems(items);
					}
				}
				else
				{
					var clash = FindResultByGuid(test, id);
					if (clash != null)
					{
						var items = new ModelItemCollection();
						if (clash.CompositeItem1 != null) items.Add(clash.CompositeItem1);
						if (clash.CompositeItem2 != null) items.Add(clash.CompositeItem2);
						ActivateClashDetective(test, clash);
						FocusOnItems(items);
					}
				}
			}
			catch { }
		}

		private string GetSearchText()
		{
			try
			{
				var tb = System.Windows.LogicalTreeHelper.FindLogicalNode(this, "SearchBox") as System.Windows.Controls.TextBox;
				return tb?.Text ?? string.Empty;
			}
			catch { return string.Empty; }
		}

		private bool GetCheckBoxState(string name)
		{
			try
			{
				var cb = System.Windows.LogicalTreeHelper.FindLogicalNode(this, name) as System.Windows.Controls.CheckBox;
				return cb?.IsChecked ?? false;
			}
			catch { return false; }
		}

		private System.Collections.Generic.List<object> GetCheckedCollisionRows()
		{
			var result = new System.Collections.Generic.List<object>();
			foreach (var item in CollisionsList.Items.Cast<object>())
			{
				var type = item.GetType();
				Guid id = type.GetProperty("Guid") != null ? (Guid)type.GetProperty("Guid").GetValue(item) : Guid.Empty;
				if (id != Guid.Empty && _checkedRowIds.Contains(id)) result.Add(item);
			}
			return result;
		}

		private bool IsTestMarkedForRename(ClashTest test)
		{
			return _checkedTestIds.Contains(test.Guid);
		}

		// Обработчики кликов по чекбоксам в XAML
		private void CollisionCheckBox_Click(object sender, RoutedEventArgs e)
		{
			var cb = sender as System.Windows.Controls.CheckBox;
			if (cb == null) return;
			if (_suppressCheckboxHandlers) return;
			// Найдём Guid из DataContext строки
			if (cb.DataContext != null)
			{
				var t = cb.DataContext.GetType();
				Guid id = t.GetProperty("Guid") != null ? (Guid)t.GetProperty("Guid").GetValue(cb.DataContext) : Guid.Empty;
				if (id != Guid.Empty)
				{
					// Если выделено несколько строк в списке, применяем новое состояние ко всем выделенным
					bool targetCheckedMulti = cb.IsChecked == true;
					if (CollisionsList.SelectedItems != null && CollisionsList.SelectedItems.Count > 1 && CollisionsList.SelectedItems.Contains(cb.DataContext))
					{
						_suppressCheckboxHandlers = true;
						try
						{
							foreach (var sel in CollisionsList.SelectedItems.Cast<object>())
							{
								var st = sel.GetType();
								Guid sid = st.GetProperty("Guid") != null ? (Guid)st.GetProperty("Guid").GetValue(sel) : Guid.Empty;
								if (sid == Guid.Empty) continue;
								if (targetCheckedMulti) _checkedRowIds.Add(sid); else _checkedRowIds.Remove(sid);
								SetCheckboxStateForListItem(CollisionsList, sel, targetCheckedMulti);
							}
						}
						finally { _suppressCheckboxHandlers = false; }
						return;
					}
					// Поддержка Shift-диапазона
					int currentIndex = CollisionsList.Items.IndexOf(cb.DataContext);
					bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
					bool targetChecked = cb.IsChecked == true;
					if (isShift && _lastCollisionClickIndex >= 0)
					{
						int from = Math.Min(_lastCollisionClickIndex, currentIndex);
						int to = Math.Max(_lastCollisionClickIndex, currentIndex);
						_suppressCheckboxHandlers = true;
						try
						{
							for (int i = from; i <= to; i++)
							{
								var item = CollisionsList.Items[i];
								var it = item.GetType();
								Guid rid = it.GetProperty("Guid") != null ? (Guid)it.GetProperty("Guid").GetValue(item) : Guid.Empty;
								if (rid == Guid.Empty) continue;
								if (targetChecked) _checkedRowIds.Add(rid); else _checkedRowIds.Remove(rid);
								// Установим визуально чекбокс
								SetCheckboxStateForListItem(CollisionsList, item, targetChecked);
							}
						}
						finally { _suppressCheckboxHandlers = false; }
					}
					else
					{
						if (targetChecked) _checkedRowIds.Add(id); else _checkedRowIds.Remove(id);
					}
					_lastCollisionClickIndex = currentIndex;
				}
			}
		}

		private void TestCheckBox_Click(object sender, RoutedEventArgs e)
		{
			var cb = sender as System.Windows.Controls.CheckBox;
			if (cb == null) return;
			if (_suppressCheckboxHandlers) return;
			// В шаблоне у нас Tag привязан к Guid
			var tag = cb.Tag;
			if (tag is Guid g)
			{
				int currentIndex = TestsList.Items.IndexOf(cb.DataContext);
				bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
				bool targetChecked = cb.IsChecked == true;

				// Если выделено несколько тестов и кликнули по одному из них — применяем состояние ко всем выделенным
				if (TestsList.SelectedItems != null && TestsList.SelectedItems.Count > 1 && TestsList.SelectedItems.Contains(cb.DataContext))
				{
					_suppressCheckboxHandlers = true;
					try
					{
						foreach (var sel in TestsList.SelectedItems.Cast<object>())
						{
							var it = sel.GetType();
							Guid tg = it.GetProperty("Guid") != null ? (Guid)it.GetProperty("Guid").GetValue(sel) : Guid.Empty;
							if (tg == Guid.Empty) continue;
							if (targetChecked) _checkedTestIds.Add(tg); else _checkedTestIds.Remove(tg);
							SetCheckboxStateForListItem(TestsList, sel, targetChecked);
						}
					}
					finally { _suppressCheckboxHandlers = false; }
					_lastTestClickIndex = currentIndex;
					// Обновляем список коллизий с учётом множественного выбора тестов
					TestsList_SelectionChanged(null, null);
					return;
				}

				if (isShift && _lastTestClickIndex >= 0)
				{
					int from = Math.Min(_lastTestClickIndex, currentIndex);
					int to = Math.Max(_lastTestClickIndex, currentIndex);
					_suppressCheckboxHandlers = true;
					try
					{
						for (int i = from; i <= to; i++)
						{
							var item = TestsList.Items[i];
							// wrapper has Guid property
							var it = item.GetType();
							Guid tg = it.GetProperty("Guid") != null ? (Guid)it.GetProperty("Guid").GetValue(item) : Guid.Empty;
							if (tg == Guid.Empty) continue;
							if (targetChecked) _checkedTestIds.Add(tg); else _checkedTestIds.Remove(tg);
							SetCheckboxStateForListItem(TestsList, item, targetChecked);
						}
					}
					finally { _suppressCheckboxHandlers = false; }
				}
				else
				{
					if (targetChecked) _checkedTestIds.Add(g); else _checkedTestIds.Remove(g);
				}
				_lastTestClickIndex = currentIndex;
				// Обновляем список коллизий с учётом множественного выбора тестов
				TestsList_SelectionChanged(null, null);
			}
		}

		private void SetCheckboxStateForListItem(System.Windows.Controls.ItemsControl list, object item, bool isChecked)
		{
			try
			{
				var container = list.ItemContainerGenerator.ContainerFromItem(item) as System.Windows.DependencyObject;
				if (container == null)
				{
					list.UpdateLayout();
					container = list.ItemContainerGenerator.ContainerFromItem(item) as System.Windows.DependencyObject;
				}
				if (container == null) return;
				var cb = FindFirstCheckbox(container);
				if (cb != null) cb.IsChecked = isChecked;
			}
			catch { }
		}

		private System.Windows.Controls.CheckBox FindFirstCheckbox(DependencyObject parent)
		{
			try
			{
				int count = VisualTreeHelper.GetChildrenCount(parent);
				for (int i = 0; i < count; i++)
				{
					var child = VisualTreeHelper.GetChild(parent, i);
					if (child is System.Windows.Controls.CheckBox c) return c;
					var deeper = FindFirstCheckbox(child);
					if (deeper != null) return deeper;
				}
			}
			catch { }
			return null;
		}

		private void SetSearchText(string text)
		{
			try
			{
				var tb = System.Windows.LogicalTreeHelper.FindLogicalNode(this, "SearchBox") as System.Windows.Controls.TextBox;
				if (tb != null) tb.Text = text ?? string.Empty;
			}
			catch { }
		}

		private void CollisionsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
		{
			OnPluginSelectionChanged();

			var selected = CollisionsList.SelectedItem;
			if (selected == null) return;

			var t = selected.GetType();
			bool isGroup = t.GetProperty("IsGroup") != null && (bool)t.GetProperty("IsGroup").GetValue(selected);
			Guid id = t.GetProperty("Guid") != null ? (Guid)t.GetProperty("Guid").GetValue(selected) : Guid.Empty;

			// Определяем тест-источник строки (важно при объединённом списке)
			ClashTest selectedTest = null;
			Guid testGuidFromRow = Guid.Empty;
			var testGuidProp = t.GetProperty("TestGuid");
			if (testGuidProp != null)
				testGuidFromRow = (Guid)testGuidProp.GetValue(selected);
			if (testGuidFromRow != Guid.Empty)
			{
				// Находим тест по GUID
				var allTests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList();
				selectedTest = allTests?.FirstOrDefault(tt => tt.Guid == testGuidFromRow);
			}
			else
			{
				// Fallback: берём тест из текущего выбора TestsList
				var testObj = TestsList.SelectedItem;
				if (testObj != null)
				{
					var testType = testObj.GetType();
					var testProp = testType.GetProperty("Test");
					if (testProp != null)
						selectedTest = testProp.GetValue(testObj) as ClashTest;
				}
			}
			if (selectedTest == null || id == Guid.Empty) return;

			ModelItemCollection items = new ModelItemCollection();
			SavedItem issue = null;

			if (isGroup)
			{
				var group = FindGroupByGuid(selectedTest, id);
				if (group != null)
				{
					foreach (var r in GetAllResultsFromGroup(group))
					{
						if (r.CompositeItem1 != null) items.Add(r.CompositeItem1);
						if (r.CompositeItem2 != null) items.Add(r.CompositeItem2);
					}
					issue = group;
				}
			}
			else
			{
				var clash = FindResultByGuid(selectedTest, id);
				if (clash != null)
				{
					if (clash.CompositeItem1 != null) items.Add(clash.CompositeItem1);
					if (clash.CompositeItem2 != null) items.Add(clash.CompositeItem2);
					issue = clash;
				}
			}

			// 1. Активируем в Clash Detective
			if (issue != null)
				ActivateClashDetective(selectedTest, issue);

			// 2. Выделяем и зуммируем в 3D
			if (items.Count > 0)
				FocusOnItems(items);
		}

		private bool SelectClashInDetective(ClashTest test, SavedItem issue)
		{
			try
			{
				var comState = ComApiBridge.State; // InwOpState
				if (comState == null) return false;
				// Достаём Clash через отражение: либо свойство "Clash", либо ищем свойство с типом, содержащим "Clash"
				object comClash = comState.GetType().GetProperty("Clash", BindingFlags.Public | BindingFlags.Instance)?.GetValue(comState);
				if (comClash == null)
				{
					foreach (var p in comState.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
					{
						var pt = p.PropertyType?.Name ?? string.Empty;
						if (pt.IndexOf("Clash", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							try { comClash = p.GetValue(comState); } catch { comClash = null; }
							if (comClash != null) break;
						}
					}
					if (comClash == null) return false;
				}

				// Находим тест по GUID
				object comTest = null;
				var testsCollection = comClash.GetType().GetProperty("TestsData")?.GetValue(comClash)
									?.GetType().GetProperty("Tests")?.GetValue(comClash.GetType().GetProperty("TestsData")?.GetValue(comClash));
				foreach (var ct in EnumerateComCollection(testsCollection))
				{
					if ((Guid)ct.GetType().GetProperty("Guid")?.GetValue(ct) == test.Guid)
					{
						comTest = ct;
						break;
					}
				}
				if (comTest == null) return false;

				// Устанавливаем текущий тест
				comClash.GetType().GetProperty("CurrentTest")?.SetValue(comClash, comTest);

				// Находим issue по GUID (результат или группа)
				object comIssue = FindComIssueByGuidRecursive(comTest, GetIssueGuidForActivation(issue));
				if (comIssue != null)
				{
					comClash.GetType().GetProperty("CurrentResult")?.SetValue(comClash, comIssue);
					return true;
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show("Ошибка выбора коллизии в Clash Detective: " + ex.Message);
			}
			return false;
		}


		private void ActivateClashDetective(ClashTest test, SavedItem issue)
		{
			try
			{
				Log($"Activate start: test={test?.Guid}, issue={issue?.Guid}, type={issue?.GetType().Name}");
				if (test == null) return;
				var opState = _doc?.State as LcOpState;
				if (opState == null) return;
				var currentIssueCtl = LcClCurrentIssue.GetInstance(opState);
				if (currentIssueCtl == null) return;

				// Попытка №1: быстрый и надёжный способ по образцу — выбрать issue без создания точки обзора
				try
				{
					// Устанавливаем тест
					currentIssueCtl.SetCurrentTest(test);
					// Выставляем нужный элемент (результат/группа) напрямую
					currentIssueCtl.SetCurrentIssueFromSavedItem(issue, 0, false);
					Log("Activated via SetCurrentIssueFromSavedItem");
					return; // успешно, дальнейшие фоллбеки не нужны
				}
				catch { /* перейдём к прежним стратегиям ниже */ }

				// Активируем панель Clash Detective (если возможно), чтобы выбор не терялся
				TryActivateClashPanelUi();
				PumpDispatcherOnce();
				Log("Clash panel activated");

				// Открываем тест через managed API (фоллбек)
				TryInvokeMethod(currentIssueCtl, new[] { "SetCurrentTest", "SelectTest" }, new object[] { test });
				Log($"Managed set test: {test.Guid}");
				// Managed: если выбрана группа — выставляем группу; если выбран результат — сначала группа родителя, затем результат
				bool managedIssueSelected = false;
				if (issue is ClashResultGroup selGroup)
				{
					TryInvokeMethod(currentIssueCtl, new[] { "SetCurrentGroup", "SelectGroup" }, new object[] { selGroup });
					// NW2020: подождём и проверим, что реально выбралась нужная группа
					managedIssueSelected = VerifyManagedSelection(currentIssueCtl, selGroup.Guid, isGroup: true);
				}
				else if (issue is ClashResult selResult)
				{
					var parentGroup = selResult.Parent as ClashResultGroup;
					if (parentGroup != null)
						TryInvokeMethod(currentIssueCtl, new[] { "SetCurrentGroup", "SelectGroup" }, new object[] { parentGroup });
					TryInvokeMethod(currentIssueCtl, new[] { "SetCurrentResult", "SelectResult", "SetCurrentIssue", "SelectIssue" }, new object[] { selResult });
					managedIssueSelected = VerifyManagedSelection(currentIssueCtl, selResult.Guid, isGroup: false);
				}

				// Открываем тест и результат через COM API
				try
				{
					var comState = ComApiBridge.State;
					if (comState == null) { Log("COM state is null"); }
					if (comState != null)
					{
						var clashProp = comState.GetType().GetProperty("Clash", BindingFlags.Public | BindingFlags.Instance);
						var comClash = clashProp?.GetValue(comState);
						if (comClash == null)
						{
							// NW2020 fallback: ищем любой публичный инстанс-свойство, у которого тип или имя содержит "Clash"
							foreach (var p in comState.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
							{
								try
								{
									var pt = p.PropertyType?.Name ?? string.Empty;
									if (pt.IndexOf("Clash", StringComparison.OrdinalIgnoreCase) >= 0 ||
										p.Name.IndexOf("Clash", StringComparison.OrdinalIgnoreCase) >= 0)
									{
										var val = p.GetValue(comState);
										if (val != null) { comClash = val; Log($"COM Clash fallback via property '{p.Name}'"); break; }
									}
								}
								catch { }
							}
						}
						if (comClash == null) { Log("COM Clash is null"); }
						if (comClash != null)
						{
							Log("COM Clash acquired");
							var testsDataProp = comClash.GetType().GetProperty("TestsData", BindingFlags.Public | BindingFlags.Instance);
							var testsData = testsDataProp?.GetValue(comClash);
							var testsProp = testsData?.GetType().GetProperty("Tests", BindingFlags.Public | BindingFlags.Instance);
							var comTests = testsProp?.GetValue(testsData);
							object comTest = null;
							if (comTests is System.Collections.IEnumerable testsEnum)
							{
								foreach (var ct in testsEnum)
								{
									var guidProp = ct.GetType().GetProperty("Guid", BindingFlags.Public | BindingFlags.Instance);
									var gObj = guidProp?.GetValue(ct);
									if (gObj is Guid g && g == test.Guid) { comTest = ct; break; }
								}
							}
							if (comTest == null) { Log("COM test not found by GUID"); }
							if (comTest != null)
							{
								// Выбираем тест
								var currentTestProp = comClash.GetType().GetProperty("CurrentTest", BindingFlags.Public | BindingFlags.Instance);
								try { currentTestProp?.SetValue(comClash, comTest); } catch { }
								try
								{
									var readTest = currentTestProp?.GetValue(comClash);
									var rtGuid = readTest?.GetType().GetProperty("Guid", BindingFlags.Public | BindingFlags.Instance)?.GetValue(readTest);
									Log($"COM set CurrentTest={rtGuid}");
								}
								catch { }
								// NW2020: дать времени после установки теста
								Thread.Sleep(80);

								// COM: применяем с повторами и прокачкой диспетчера, чтобы избежать гонок обновления UI
								bool comSetOk = false;
								for (int attempt = 0; attempt < 5 && !comSetOk; attempt++)
								{
									if (issue is ClashResultGroup issueGroup)
									{
										var comGroup = FindComIssueByGuidRecursive(comTest, issueGroup.Guid);
										if (comGroup == null) { Log($"Attempt {attempt+1}: COM group not found by GUID={issueGroup.Guid}"); }
										var currentGroupProp = comClash.GetType().GetProperty("CurrentGroup", BindingFlags.Public | BindingFlags.Instance);
										try { currentGroupProp?.SetValue(comClash, comGroup); } catch { }
										// NW2020: короткая пауза
										Thread.Sleep(60);
										var currentResultProp = comClash.GetType().GetProperty("CurrentResult", BindingFlags.Public | BindingFlags.Instance);
										// Некоторые версии NW требуют также установить CurrentResult = группе
										try { currentResultProp?.SetValue(comClash, comGroup); } catch { }
										// Проверяем, что выставилась нужная группа
										var readGroup = currentGroupProp?.GetValue(comClash);
										var guidProp = readGroup?.GetType().GetProperty("Guid", BindingFlags.Public | BindingFlags.Instance);
										var gObj = guidProp?.GetValue(readGroup);
										comSetOk = (gObj is Guid g && g == issueGroup.Guid);
										Log($"Attempt {attempt+1}: set group={issueGroup.Guid}, read group={gObj}, ok={comSetOk}");
										if (!comSetOk)
										{
											// fallback: очищаем CurrentResult и пробуем ещё раз
											try { currentResultProp?.SetValue(comClash, null); } catch { }
										}
									}
									else if (issue is ClashResult issueResult)
									{
										Guid? parentGuid = (issueResult.Parent as ClashResultGroup)?.Guid;
										if (parentGuid.HasValue)
										{
											var comParentGroup = FindComIssueByGuidRecursive(comTest, parentGuid.Value);
											if (comParentGroup == null) { Log($"Attempt {attempt+1}: COM parent group not found by GUID={parentGuid}"); }
											var currentGroupProp = comClash.GetType().GetProperty("CurrentGroup", BindingFlags.Public | BindingFlags.Instance);
											try { currentGroupProp?.SetValue(comClash, comParentGroup); } catch { }
											Thread.Sleep(60);
										}
										Guid issueGuid = issueResult.Guid;
										object comIssue = FindComIssueByGuidRecursive(comTest, issueGuid);
										if (comIssue == null)
										{
											var managedPath = new System.Collections.Generic.List<int>();
											if (TryGetManagedIndexPath(test, issueResult, managedPath))
											{
												var comExact = FindComNodeByIndexPath(comTest, managedPath);
												if (comExact != null) comIssue = comExact; else Log($"Attempt {attempt+1}: COM exact by path not found [{string.Join(",", managedPath)}]");
											}
											else { Log($"Attempt {attempt+1}: Managed path not found for result"); }
										}
										if (comIssue != null)
										{
											var currentResultProp = comClash.GetType().GetProperty("CurrentResult", BindingFlags.Public | BindingFlags.Instance);
											try { currentResultProp?.SetValue(comClash, comIssue); } catch { }
											// Проверяем, что выставился нужный результат
											var readRes = currentResultProp?.GetValue(comClash);
											var guidProp = readRes?.GetType().GetProperty("Guid", BindingFlags.Public | BindingFlags.Instance);
											var gObj = guidProp?.GetValue(readRes);
											comSetOk = (gObj is Guid g && g == issueResult.Guid);
											Log($"Attempt {attempt+1}: set result={issueResult.Guid}, read result={gObj}, ok={comSetOk}");
										}
										else { Log($"Attempt {attempt+1}: COM issue not found by GUID={issueGuid}"); }
									}
									// даём UI шанс обновиться
									PumpDispatcherOnce();
									if (!comSetOk) Thread.Sleep(30);
								}
							}
						}
					}
				}
				catch { }

				// Если ни managed, ни COM явно не выбрали нужный issue, пробуем через UI Automation (сначала по пути индексов, затем GUID/имя)
				if (!managedIssueSelected)
				{
					try
					{
						var managedPath = new System.Collections.Generic.List<int>();
						if (TryGetManagedIndexPath(test, issue, managedPath))
						{
							Log($"UIA path select: [{string.Join(",", managedPath)}]");
							TryActivateViaUIAutomationByIndexPath(managedPath);
						}
						else
						{
							// Если это группа и ничего не сработало, используем rename-select-restore
							if (issue is ClashResultGroup targetGroup)
							{
								string originalName = targetGroup.DisplayName ?? string.Empty;
								string tempName = $"__SELECT__ {targetGroup.Guid}";
								if (TryRenameGroupOnce(test, targetGroup.Guid, tempName))
								{
									// небольшая пауза и выбор по точному имени
									Thread.Sleep(120);
									Log($"UIA temp-name select: {tempName}");
									TryActivateViaUIAutomation(tempName, null);
									// восстановление имени
									Thread.Sleep(120);
									TryRenameGroupOnce(test, targetGroup.Guid, originalName);
								}
								else
								{
									Log("Rename group failed; fallback to UIA guid/name");
									TryActivateViaUIAutomation(issue?.DisplayName, issue?.Guid);
								}
							}
							else
							{
								Log($"UIA guid/name select: issue={issue?.Guid} name={issue?.DisplayName}");
								TryActivateViaUIAutomation(issue?.DisplayName, issue?.Guid);
							}
						}
					}
					catch { }
				}
			}
			catch { }
		}

		private bool VerifyManagedSelection(object currentIssueCtl, Guid targetGuid, bool isGroup)
		{
			for (int attempt = 0; attempt < 6; attempt++)
			{
				try
				{
					// Небольшая пауза и прокачка UI
					PumpDispatcherOnce();
					if (attempt > 0) Thread.Sleep(50);
					// читаем текущий issue
					var getMethod = currentIssueCtl.GetType().GetMethod("GetCurrentIssueAsSavedItem", BindingFlags.Public | BindingFlags.Instance);
					var savedItem = getMethod?.Invoke(currentIssueCtl, null) as SavedItem;
					var guid = savedItem?.Guid ?? Guid.Empty;
					var ok = guid == targetGuid;
					Log($"Managed verify {attempt+1}: target={(isGroup?"Group":"Result")} {targetGuid}, read={guid}, ok={ok}");
					if (ok) return true;
					// если не совпало, повторим установку
					if (isGroup)
					{
						// Попробуем ещё раз выставить группу
						// savedItem может быть не той группой — у нас нет объекта группы здесь, поэтому повторную установку сделаем на стороне вызывающего кода
					}
					else
					{
						// Для результата перезададим текущий результат не меняя группу
						// К сожалению, без объекта результата повторно установить нельзя здесь — оставляем только ожидание.
					}
				}
				catch { }
			}
			return false;
		}

		private void PumpDispatcherOnce()
		{
			try
			{
				var frame = new DispatcherFrame();
				Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
				Dispatcher.PushFrame(frame);
			}
			catch { }
		}

		// Активирует вкладку Clash Detective в интерфейсе Navisworks через UI Automation
		private void TryActivateClashPanelUi()
		{
			try
			{
				var root = System.Windows.Automation.AutomationElement.RootElement;
				if (root == null) return;
				var navWindow = root.FindFirst(System.Windows.Automation.TreeScope.Children,
					new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, _doc?.Title ?? ""));
				if (navWindow == null) return;
				// Ищем вкладку/панель по известным именам интерфейса
				string[] names = { "Clash Detective", "Проверка коллизий", "Проверка столкновений" };
				foreach (var n in names)
				{
					var tab = navWindow.FindFirst(System.Windows.Automation.TreeScope.Descendants,
						new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, n));
					if (tab != null)
					{
						var sel = tab.GetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern) as System.Windows.Automation.SelectionItemPattern;
						try { sel?.Select(); Log($"Activated panel: {n}"); } catch { }
						break;
					}
				}
			}
			catch { }
		}

		private static string GetLogPath()
		{
			try { return Path.Combine(Path.GetTempPath(), "ClashSelection.log"); } catch { return "ClashSelection.log"; }
		}

		private void Log(string message)
		{
			try
			{
				File.AppendAllText(GetLogPath(), $"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n");
			}
			catch { }
		}

		private bool TryRenameGroupOnce(ClashTest test, Guid groupGuid, string newName)
		{
			try
			{
				if (test == null) return false;
				int idx = _documentClash.TestsData.Tests.IndexOf(test);
				if (idx < 0) return false;
				var copy = (ClashTest)test.CreateCopy();
				var grp = FindGroupByGuid(copy, groupGuid);
				if (grp == null) return false;
				grp.DisplayName = newName ?? string.Empty;
				_documentClash.TestsData.TestsReplaceWithCopy(idx, copy);
				PumpDispatcherOnce();
				return true;
			}
			catch { return false; }
		}

		private bool TryInvokeMethod(object target, string[] methodNames, object[] args)
		{
			foreach (var name in methodNames)
			{
				var methods = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
					.Where(m => string.Equals(m.Name, name, StringComparison.Ordinal))
					.ToList();
				foreach (var m in methods)
				{
					var pars = m.GetParameters();
					if (pars.Length != args.Length) continue;
					bool compatible = true;
					for (int i = 0; i < pars.Length; i++)
					{
						if (args[i] == null) continue;
						if (!pars[i].ParameterType.IsAssignableFrom(args[i].GetType()))
						{
							compatible = false;
							break;
						}
					}
					if (!compatible) continue;
					try { m.Invoke(target, args); return true; } catch { }
				}
			}
			return false;
		}

		private void FocusOnItems(ModelItemCollection items)
		{
			if (items == null || items.Count == 0) return;
			try
			{
				// Текущая выборка в Navisworks копируется, а не редактируется напрямую
				var selection = new ModelItemCollection();
				foreach (var it in items) selection.Add(it);
				_doc.CurrentSelection.CopyFrom(selection);
				// Попытка приблизить камеру к выделению через interop, если доступно
				TryZoomToSelectedViaInterop();
			}
			catch (Exception ex)
			{
				MessageBox.Show("Не удалось выделить элементы: " + ex.Message);
			}
		}

		private void TryZoomToSelectedViaInterop()
		{
			// Сначала через COM — надёжно зуммирует выделение в текущем виде (без dynamic)
			try
			{
				var comState = ComApiBridge.State; // InwOpState
				var currentViewProp = comState?.GetType().GetProperty("CurrentView", BindingFlags.Public | BindingFlags.Instance);
				var comView = currentViewProp?.GetValue(comState);
				if (comView != null)
				{
					var zoomM = comView.GetType().GetMethod("ZoomSelected", BindingFlags.Public | BindingFlags.Instance)
						?? comView.GetType().GetMethod("ZoomToSelection", BindingFlags.Public | BindingFlags.Instance)
						?? comView.GetType().GetMethod("FitToSelection", BindingFlags.Public | BindingFlags.Instance);
					if (zoomM != null) { zoomM.Invoke(comView, null); return; }
				}
			}
			catch { }

			// Fallback: через managed API отражением
			try
			{
				var state = _doc?.State;
				if (state == null) return;
				var currentView = state.GetType().GetProperty("CurrentView", BindingFlags.Public | BindingFlags.Instance)?.GetValue(state);
				if (currentView == null) return;
				var m = currentView.GetType().GetMethod("ZoomSelected", BindingFlags.Public | BindingFlags.Instance)
					?? currentView.GetType().GetMethod("ZoomToSelection", BindingFlags.Public | BindingFlags.Instance)
					?? currentView.GetType().GetMethod("FitToSelection", BindingFlags.Public | BindingFlags.Instance);
				m?.Invoke(currentView, null);
			}
			catch { }
		}

		// Последняя надежда: через UI Automation ищем список Clash Detective и кликаем по строке (по GUID или имени)
		private void TryActivateViaUIAutomation(string displayName, Guid? guid)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(displayName) && !guid.HasValue) return;
				var root = System.Windows.Automation.AutomationElement.RootElement;
				if (root == null) return;
				// Ищем окно Navisworks
				var navWindow = root.FindFirst(System.Windows.Automation.TreeScope.Children,
					new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, _doc?.Title ?? ""));
				if (navWindow == null) return;
				// Сначала пробуем найти по GUID (в имени/AutomationId)
				System.Windows.Automation.AutomationElement match = null;
				if (guid.HasValue)
				{
					string guidStr = guid.Value.ToString();
					match = navWindow.FindFirst(System.Windows.Automation.TreeScope.Descendants,
						new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, guidStr));
					if (match == null)
					{
						match = navWindow.FindFirst(System.Windows.Automation.TreeScope.Descendants,
							new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.AutomationIdProperty, guidStr));
					}
				}
				// Затем по имени
				if (match == null && !string.IsNullOrWhiteSpace(displayName))
				{
					match = navWindow.FindFirst(System.Windows.Automation.TreeScope.Descendants,
						new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, displayName));
				}
				if (match == null) return;
				var invoke = match.GetCurrentPattern(System.Windows.Automation.InvokePattern.Pattern) as System.Windows.Automation.InvokePattern;
				if (invoke != null)
				{
					invoke.Invoke();
					return;
				}
				// Если нет Invoke, пробуем выбрать
				var selection = match.GetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern) as System.Windows.Automation.SelectionItemPattern;
				selection?.Select();
			}
			catch { }
		}

		// Доп. вариант: выбор по GUID через UI Automation, если имя не уникально/не сработало
		private void TryActivateViaUIAutomationByGuid(Guid guid)
		{
			try
			{
				if (guid == Guid.Empty) return;
				var root = System.Windows.Automation.AutomationElement.RootElement;
				if (root == null) return;
				var navWindow = root.FindFirst(System.Windows.Automation.TreeScope.Children,
					new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, _doc?.Title ?? ""));
				if (navWindow == null) return;
				var match = navWindow.FindFirst(System.Windows.Automation.TreeScope.Descendants,
					new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, guid.ToString()));
				if (match == null) return;
				var selection = match.GetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern) as System.Windows.Automation.SelectionItemPattern;
				selection?.Select();
			}
			catch { }
		}

		private Guid GetIssueGuidForActivation(SavedItem issue)
		{
			// Возвращаем GUID самого выбранного элемента (группа или результат)
			// Не подменяем на первый результат группы, чтобы не было смещения выбора
			return issue.Guid;
		}

		private IEnumerable<object> EnumerateComCollection(object comCollection)
		{
			if (comCollection == null) yield break;
			if (comCollection is System.Collections.IEnumerable enumerable)
			{
				foreach (var item in enumerable) yield return item;
				yield break;
			}
			var countProp = comCollection.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
			var itemMethod = comCollection.GetType().GetMethod("Item", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
			int count = 0;
			try { count = (int)(countProp?.GetValue(comCollection) ?? 0); } catch { }
			for (int i = 1; i <= count; i++)
			{
				object val = null;
				try { val = itemMethod?.Invoke(comCollection, new object[] { i }); } catch { }
				if (val != null) yield return val;
			}
		}

		private object FindComIssueByGuidRecursive(object comNode, Guid targetGuid)
		{
			if (comNode == null) return null;
			var guidProp = comNode.GetType().GetProperty("Guid", BindingFlags.Public | BindingFlags.Instance);
			var gObj = guidProp?.GetValue(comNode);
			if (gObj is Guid g && g == targetGuid) return comNode;
			var childrenProp = comNode.GetType().GetProperty("Children", BindingFlags.Public | BindingFlags.Instance);
			var children = childrenProp?.GetValue(comNode);
			foreach (var child in EnumerateComCollection(children))
			{
				var found = FindComIssueByGuidRecursive(child, targetGuid);
				if (found != null) return found;
			}
			return null;
		}

		// Построение пути индексов до issue в дереве managed (по свойству Children) через отражение,
		// чтобы одинаково работать для ClashTest и ClashResultGroup и избежать конфликтов типов GroupItem
		private bool TryGetManagedIndexPath(object root, SavedItem target, System.Collections.Generic.List<int> path)
		{
			if (root == null || target == null) return false;
			var childrenProp = root.GetType().GetProperty("Children", BindingFlags.Public | BindingFlags.Instance);
			var childrenObj = childrenProp?.GetValue(root);
			if (childrenObj == null) return false;
			// Пытаемся получить Count и индексированный доступ [i]
			var countProp = childrenObj.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
			int count = 0;
			try { count = (int)(countProp?.GetValue(childrenObj) ?? 0); } catch { }
			for (int i = 0; i < count; i++)
			{
				object child = null;
				try { child = childrenObj.GetType().GetProperty("Item")?.GetValue(childrenObj, new object[] { i }); } catch { }
				if (child == null)
				{
					// альтернативный способ – если Children реализует IEnumerable
					int idx = 0;
					foreach (var c in (System.Collections.IEnumerable)childrenObj)
					{
						if (idx == i) { child = c; break; }
						idx++;
					}
				}
				if (child == null) continue;
				var guidProp = child.GetType().GetProperty("Guid", BindingFlags.Public | BindingFlags.Instance);
				var gObj = guidProp?.GetValue(child);
				if (gObj is Guid g && g == target.Guid)
				{
					path.Add(i);
					return true;
				}
				// рекурсивно спускаемся, если у child есть Children
				var childChildrenProp = child.GetType().GetProperty("Children", BindingFlags.Public | BindingFlags.Instance);
				if (childChildrenProp != null)
				{
					path.Add(i);
					if (TryGetManagedIndexPath(child, target, path)) return true;
					path.RemoveAt(path.Count - 1);
				}
			}
			return false;
		}

		private object FindComNodeByIndexPath(object comRoot, System.Collections.Generic.IReadOnlyList<int> path)
		{
			object current = comRoot;
			for (int depth = 0; depth < path.Count; depth++)
			{
				var childrenProp = current.GetType().GetProperty("Children", BindingFlags.Public | BindingFlags.Instance);
				var children = childrenProp?.GetValue(current);
				if (children == null) return null;
				int targetIndex = path[depth]; // 0-based
				int idx = 0;
				object next = null;
				foreach (var c in EnumerateComCollection(children))
				{
					if (idx == targetIndex) { next = c; break; }
					idx++;
				}
				if (next == null) return null;
				current = next;
			}
			return current;
		}

		// UIA: выбор элемента Clash Detective по пути индексов (соответствует Children managed-дерева)
		private void TryActivateViaUIAutomationByIndexPath(System.Collections.Generic.IReadOnlyList<int> indexPath)
		{
			try
			{
				if (indexPath == null || indexPath.Count == 0) return;
				var root = System.Windows.Automation.AutomationElement.RootElement;
				if (root == null) return;
				var navWindow = root.FindFirst(System.Windows.Automation.TreeScope.Children,
					new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.NameProperty, _doc?.Title ?? ""));
				if (navWindow == null) return;
				// Ищем дерево результатов Clash Detective (обычно ControlType.Tree или List)
				var tree = navWindow.FindFirst(System.Windows.Automation.TreeScope.Descendants,
					new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.Tree));
				if (tree == null)
				{
					tree = navWindow.FindFirst(System.Windows.Automation.TreeScope.Descendants,
						new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.List));
					if (tree == null) return;
				}
				var current = tree;
				for (int depth = 0; depth < indexPath.Count; depth++)
				{
					int targetIndex = indexPath[depth];
					var children = current.FindAll(System.Windows.Automation.TreeScope.Children,
						new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.TreeItem));
					if (children == null || children.Count == 0)
					{
						children = current.FindAll(System.Windows.Automation.TreeScope.Children,
							new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.ListItem));
					}
					if (children == null || children.Count <= targetIndex) return;
					current = children[targetIndex];
					// разворачиваем, если есть ExpandCollapse
					var ec = current.GetCurrentPattern(System.Windows.Automation.ExpandCollapsePattern.Pattern) as System.Windows.Automation.ExpandCollapsePattern;
					try { if (ec != null && ec.Current.ExpandCollapseState != System.Windows.Automation.ExpandCollapseState.Expanded) ec.Expand(); } catch { }
				}
				var sel = current.GetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern) as System.Windows.Automation.SelectionItemPattern;
				try { sel?.Select(); } catch { }
			}
			catch { }
		}

		private System.Collections.Generic.IEnumerable<ResultRow> EnumerateResultsWithGroupFromTest(ClashTest test)
		{
			foreach (var r in test.Children.OfType<ClashResult>())
				yield return new ResultRow { Result = r, GroupName = null, ParentGroup = null };

			foreach (var g in test.Children.OfType<ClashResultGroup>())
			{
				foreach (var row in EnumerateResultsWithGroupFromGroup(g))
					yield return row;
			}
		}

		private System.Collections.Generic.IEnumerable<ResultRow> EnumerateResultsWithGroupFromGroup(ClashResultGroup group)
		{
			foreach (var r in group.Children.OfType<ClashResult>())
				yield return new ResultRow { Result = r, GroupName = group.DisplayName, ParentGroup = group };

			foreach (var g in group.Children.OfType<ClashResultGroup>())
			{
				foreach (var row in EnumerateResultsWithGroupFromGroup(g))
					yield return row;
			}
		}

		private System.Collections.Generic.IEnumerable<ClashResult> GetAllResultsFromTest(ClashTest test)
		{
			foreach (var r in test.Children.OfType<ClashResult>())
				yield return r;

			foreach (var g in test.Children.OfType<ClashResultGroup>())
			{
				foreach (var r in GetAllResultsFromGroup(g))
					yield return r;
			}
		}

		private System.Collections.Generic.IEnumerable<ClashResult> GetAllResultsFromGroup(ClashResultGroup group)
		{
			foreach (var r in group.Children.OfType<ClashResult>())
				yield return r;

			foreach (var g in group.Children.OfType<ClashResultGroup>())
			{
				foreach (var r in GetAllResultsFromGroup(g))
					yield return r;
			}
		}

		private ClashResult FindResultByGuid(ClashTest test, Guid guid)
		{
			foreach (var r in test.Children.OfType<ClashResult>())
				if (r.Guid == guid) return r;

			foreach (var g in test.Children.OfType<ClashResultGroup>())
			{
				var found = FindResultByGuid(g, guid);
				if (found != null) return found;
			}
			return null;
		}

		private ClashResult FindResultByGuid(ClashResultGroup group, Guid guid)
		{
			foreach (var r in group.Children.OfType<ClashResult>())
				if (r.Guid == guid) return r;

			foreach (var g in group.Children.OfType<ClashResultGroup>())
			{
				var found = FindResultByGuid(g, guid);
				if (found != null) return found;
			}
			return null;
		}

		private ClashResultGroup FindGroupByGuid(ClashTest test, Guid guid)
		{
			foreach (var g in test.Children.OfType<ClashResultGroup>())
			{
				if (g.Guid == guid) return g;
				var found = FindGroupByGuid(g, guid);
				if (found != null) return found;
			}
			return null;
		}

		private ClashResultGroup FindGroupByGuid(ClashResultGroup group, Guid guid)
		{
			foreach (var g in group.Children.OfType<ClashResultGroup>())
			{
				if (g.Guid == guid) return g;
				var found = FindGroupByGuid(g, guid);
				if (found != null) return found;
			}
			return null;
		}

		private static string ReplaceWithComparison(string source, string search, string replacement, StringComparison comparison)
		{
			if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(search)) return source;
			int previousIndex = 0;
			int index = source.IndexOf(search, comparison);
			if (index < 0) return source;
			var result = new System.Text.StringBuilder(source.Length);
			while (index >= 0)
			{
				result.Append(source, previousIndex, index - previousIndex);
				result.Append(replacement ?? string.Empty);
				previousIndex = index + search.Length;
				index = source.IndexOf(search, previousIndex, comparison);
			}
			result.Append(source, previousIndex, source.Length - previousIndex);
			return result.ToString();
		}

		private void ApplyRenameButton_Click(object sender, RoutedEventArgs e)
		{
			Log("ApplyRenameButton_Click invoked");
			var findText = (FindBox.Text ?? string.Empty).Trim();
			var replaceText = (ReplaceBox.Text ?? string.Empty).Trim();

			try
			{
				bool hasCheckedTests = _checkedTestIds.Count > 0;
				bool hasCheckedRows = _checkedRowIds.Count > 0;
				bool isTestMode = TestModeRadioButton?.IsChecked == true;

				// Запрещаем одновременное переименование тестов и коллизий/групп
				// Исключение: в режиме "Коллизии" выбранные тесты означают работу с их коллизиями
				if (hasCheckedTests && hasCheckedRows && isTestMode)
				{
					MessageBox.Show("Выберите только тесты или только коллизии/группы для переименования.");
					return;
				}

				// Если ничего не выбрано — глобальная замена по режиму
				if (!hasCheckedTests && !hasCheckedRows)
				{
					if (string.IsNullOrWhiteSpace(findText))
					{
						MessageBox.Show("Введите текст в поле 'Найти' для глобальной замены.");
						return;
					}

					if (isTestMode)
					{
						// Глобальная замена тестов
						int testsChanged = 0;
						var allTests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList() ?? new System.Collections.Generic.List<ClashTest>();
						foreach (var test in allTests)
						{
							var originalName = test.DisplayName ?? string.Empty;
							if (string.Equals(originalName, findText, StringComparison.OrdinalIgnoreCase))
							{
								int idx = _documentClash.TestsData.Tests.IndexOf(test);
								if (idx >= 0)
								{
									var copy = (ClashTest)test.CreateCopy();
									copy.DisplayName = replaceText ?? string.Empty;
									_documentClash.TestsData.TestsReplaceWithCopy(idx, copy);
									testsChanged++;
								}
							}
						}
						LoadTests();
						MessageBox.Show(testsChanged > 0 ? $"Глобальная замена тестов выполнена: {testsChanged} изменений." : "Совпадающие имена тестов не найдены.");
						return;
					}
					else
					{
						// Глобальная замена коллизий/групп
						int changes = 0;
						var allTests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList() ?? new System.Collections.Generic.List<ClashTest>();
						for (int ti = 0; ti < allTests.Count; ti++)
						{
							var test = allTests[ti];
							var copy = (ClashTest)test.CreateCopy();
							bool testChanged = false;

							// Группы (все уровни)
							foreach (var tpl in EnumerateAllGroupsWithLevel(copy))
							{
								var group = tpl.Group;
								var originalName = group.DisplayName ?? string.Empty;
															if (string.Equals(originalName, findText, StringComparison.Ordinal))
							{
								group.DisplayName = replaceText ?? string.Empty;
								changes++;
								testChanged = true;
							}
							}

							// Неклассифицированные результаты
							foreach (var r in copy.Children.OfType<ClashResult>())
							{
								var originalName = r.DisplayName ?? string.Empty;
								if (string.Equals(originalName, findText, StringComparison.Ordinal))
								{
									r.DisplayName = replaceText ?? string.Empty;
									changes++;
									testChanged = true;
								}
							}

							// Результаты внутри групп (все уровни)
							foreach (var g in copy.Children.OfType<ClashResultGroup>())
							{
								foreach (var r in GetAllResultsFromGroup(g))
								{
									var originalName = r.DisplayName ?? string.Empty;
									if (string.Equals(originalName, findText, StringComparison.Ordinal))
									{
										r.DisplayName = replaceText ?? string.Empty;
										changes++;
										testChanged = true;
									}
								}
							}

							if (testChanged)
							{
								int idx = _documentClash.TestsData.Tests.IndexOf(test);
								if (idx >= 0)
								{
									_documentClash.TestsData.TestsReplaceWithCopy(idx, copy);
								}
							}
						}

						LoadTests();
						MessageBox.Show(changes > 0 ? $"Глобальная замена коллизий/групп выполнена: {changes} изменений." : "Совпадающие имена коллизий/групп не найдены.");
						return;
					}
				}

				// Если выбраны чекбоксы — работаем с выбранными
				if (isTestMode && hasCheckedTests)
				{
					// Переименование выбранных тестов
					int testsRenamed = 0;
					var allTests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList() ?? new System.Collections.Generic.List<ClashTest>();
					foreach (var test in allTests)
					{
						if (!_checkedTestIds.Contains(test.Guid)) continue;
						int idx = _documentClash.TestsData.Tests.IndexOf(test);
						if (idx < 0) continue;
						var copy = (ClashTest)test.CreateCopy();
						var originalName = copy.DisplayName ?? string.Empty;
						var newName = string.IsNullOrWhiteSpace(findText)
							? (replaceText ?? string.Empty)
							: ReplaceWithComparison(originalName, findText, replaceText, StringComparison.Ordinal);
						if (newName != originalName)
						{
							copy.DisplayName = newName;
							_documentClash.TestsData.TestsReplaceWithCopy(idx, copy);
							testsRenamed++;
						}
					}
					LoadTests();
					_checkedTestIds.Clear();
					MessageBox.Show(testsRenamed > 0 ? "Тесты успешно переименованы." : "Нет изменений по заданным критериям.");
					return;
				}
				else if (!isTestMode && hasCheckedRows)
				{
					// Переименование выбранных коллизий/групп
					int changes = 0;
					var allTests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList() ?? new System.Collections.Generic.List<ClashTest>();
					for (int ti = 0; ti < allTests.Count; ti++)
					{
						var test = allTests[ti];
						var copy = (ClashTest)test.CreateCopy();
						bool testChanged = false;

						foreach (var rowId in _checkedRowIds.ToList())
						{
							var group = FindGroupByGuid(copy, rowId);
							if (group != null)
							{
								var originalName = group.DisplayName ?? string.Empty;
								var newName = string.IsNullOrWhiteSpace(findText)
									? (replaceText ?? string.Empty)
									: ReplaceWithComparison(originalName, findText, replaceText, StringComparison.Ordinal);
								if (newName != originalName)
								{
									group.DisplayName = newName;
									changes++;
									testChanged = true;
								}
								continue;
							}
							var clash = FindResultByGuid(copy, rowId);
							if (clash != null)
							{
								var originalName = clash.DisplayName ?? string.Empty;
								var newName = string.IsNullOrWhiteSpace(findText)
									? (replaceText ?? string.Empty)
									: ReplaceWithComparison(originalName, findText, replaceText, StringComparison.Ordinal);
								if (newName != originalName)
								{
									clash.DisplayName = newName;
									changes++;
									testChanged = true;
								}
							}
						}
						if (testChanged)
						{
							int idx = _documentClash.TestsData.Tests.IndexOf(test);
							if (idx >= 0)
							{
								_documentClash.TestsData.TestsReplaceWithCopy(idx, copy);
							}
						}
					}
					LoadTests();
					_checkedRowIds.Clear();
					MessageBox.Show(changes > 0 ? "Коллизии/группы успешно переименованы." : "Нет изменений по заданным критериям.");
					return;
				}

				// Если режим не соответствует выбранным чекбоксам
				if (isTestMode && !hasCheckedTests)
				{
					MessageBox.Show("В режиме 'Тесты' выберите тесты для переименования через чекбоксы.");
				}
				else if (!isTestMode && !hasCheckedRows)
				{
					MessageBox.Show("В режиме 'Коллизии' выберите коллизии/группы для переименования через чекбоксы.");
				}
				else
				{
					MessageBox.Show("Выберите тесты или коллизии/группы для переименования через чекбоксы.");
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Ошибка при переименовании: {ex.Message}\n{ex.StackTrace}");
			}
		}

		private void ApplySuffixPreffix_Click(object sender, RoutedEventArgs e)
		{
			Log("ApplySuffixPreffix_Click invoked");
			var prefixText = Prefix?.Text ?? string.Empty;
			var suffixText = Suffix?.Text ?? string.Empty;

			try
			{
				bool hasCheckedTests = _checkedTestIds.Count > 0;
				bool hasCheckedRows = _checkedRowIds.Count > 0;
				bool isTestMode = TestModeRadioButton?.IsChecked == true;

				// Запрещаем одновременное изменение тестов и коллизий/групп
				if (hasCheckedTests && hasCheckedRows)
				{
					MessageBox.Show("Выберите только тесты или только коллизии/группы для изменения префикса/суффикса.");
					return;
				}

				// Если ничего не выбрано — глобальное применение по режиму
				if (!hasCheckedTests && !hasCheckedRows)
				{
					if (isTestMode)
					{
						// Глобальное применение к тестам
						int testsChanged = 0;
						var allTests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList() ?? new System.Collections.Generic.List<ClashTest>();
						foreach (var test in allTests)
						{
							var originalName = test.DisplayName ?? string.Empty;
							var newName = (prefixText ?? string.Empty) + originalName + (suffixText ?? string.Empty);
							if (!string.Equals(newName, originalName, StringComparison.Ordinal))
							{
								int idx = _documentClash.TestsData.Tests.IndexOf(test);
								if (idx >= 0)
								{
									var copy = (ClashTest)test.CreateCopy();
									copy.DisplayName = newName;
									_documentClash.TestsData.TestsReplaceWithCopy(idx, copy);
									testsChanged++;
								}
							}
						}
						LoadTests();
						MessageBox.Show(testsChanged > 0 ? $"Префикс/суффикс применены к тестам: {testsChanged} изменений." : "Нет изменений для тестов.");
						return;
					}
					else
					{
						// Глобальное применение к коллизиям/группам
						int changes = 0;
						var allTests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList() ?? new System.Collections.Generic.List<ClashTest>();
						for (int ti = 0; ti < allTests.Count; ti++)
						{
							var test = allTests[ti];
							var copy = (ClashTest)test.CreateCopy();
							bool testChanged = false;

							// Группы (все уровни)
							foreach (var tpl in EnumerateAllGroupsWithLevel(copy))
							{
								var group = tpl.Group;
								var originalName = group.DisplayName ?? string.Empty;
								var newName = (prefixText ?? string.Empty) + originalName + (suffixText ?? string.Empty);
								if (!string.Equals(newName, originalName, StringComparison.Ordinal))
								{
									group.DisplayName = newName;
									changes++;
									testChanged = true;
								}
							}

							// Неклассифицированные результаты
							foreach (var r in copy.Children.OfType<ClashResult>())
							{
								var originalName = r.DisplayName ?? string.Empty;
								var newName = (prefixText ?? string.Empty) + originalName + (suffixText ?? string.Empty);
								if (!string.Equals(newName, originalName, StringComparison.Ordinal))
								{
									r.DisplayName = newName;
									changes++;
									testChanged = true;
								}
							}

							// Результаты внутри групп (все уровни)
							foreach (var g in copy.Children.OfType<ClashResultGroup>())
							{
								foreach (var r in GetAllResultsFromGroup(g))
								{
									var originalName = r.DisplayName ?? string.Empty;
									var newName = (prefixText ?? string.Empty) + originalName + (suffixText ?? string.Empty);
									if (!string.Equals(newName, originalName, StringComparison.Ordinal))
									{
										r.DisplayName = newName;
										changes++;
										testChanged = true;
									}
								}
							}

							if (testChanged)
							{
								int idx = _documentClash.TestsData.Tests.IndexOf(test);
								if (idx >= 0)
								{
									_documentClash.TestsData.TestsReplaceWithCopy(idx, copy);
								}
							}
						}

						LoadTests();
						MessageBox.Show(changes > 0 ? $"Префикс/суффикс применены к коллизиям/группам: {changes} изменений." : "Нет изменений для коллизий/групп.");
						return;
					}
				}

				// Если выбраны чекбоксы — работаем с выбранными
				if (isTestMode && hasCheckedTests)
				{
					// Изменение выбранных тестов
					int testsChanged = 0;
					var allTests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList() ?? new System.Collections.Generic.List<ClashTest>();
					foreach (var test in allTests)
					{
						if (!_checkedTestIds.Contains(test.Guid)) continue;
						int idx = _documentClash.TestsData.Tests.IndexOf(test);
						if (idx < 0) continue;
						var copy = (ClashTest)test.CreateCopy();
						var originalName = copy.DisplayName ?? string.Empty;
						var newName = (prefixText ?? string.Empty) + originalName + (suffixText ?? string.Empty);
						if (!string.Equals(newName, originalName, StringComparison.Ordinal))
						{
							copy.DisplayName = newName;
							_documentClash.TestsData.TestsReplaceWithCopy(idx, copy);
							testsChanged++;
						}
					}
					LoadTests();
					_checkedTestIds.Clear();
					MessageBox.Show(testsChanged > 0 ? "Префикс/суффикс применены к тестам." : "Нет изменений для выбранных тестов.");
					return;
				}
				else if (!isTestMode && hasCheckedRows)
				{
					// Изменение выбранных коллизий/групп
					int changes = 0;
					var allTests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList() ?? new System.Collections.Generic.List<ClashTest>();
					for (int ti = 0; ti < allTests.Count; ti++)
					{
						var test = allTests[ti];
						var copy = (ClashTest)test.CreateCopy();
						bool testChanged = false;

						foreach (var rowId in _checkedRowIds.ToList())
						{
							var group = FindGroupByGuid(copy, rowId);
							if (group != null)
							{
								var originalName = group.DisplayName ?? string.Empty;
								var newName = (prefixText ?? string.Empty) + originalName + (suffixText ?? string.Empty);
								if (!string.Equals(newName, originalName, StringComparison.Ordinal))
								{
									group.DisplayName = newName;
									changes++;
									testChanged = true;
								}
								continue;
							}
							var clash = FindResultByGuid(copy, rowId);
							if (clash != null)
							{
								var originalName = clash.DisplayName ?? string.Empty;
								var newName = (prefixText ?? string.Empty) + originalName + (suffixText ?? string.Empty);
								if (!string.Equals(newName, originalName, StringComparison.Ordinal))
								{
									clash.DisplayName = newName;
									changes++;
									testChanged = true;
								}
							}
						}
						if (testChanged)
						{
							int idx = _documentClash.TestsData.Tests.IndexOf(test);
							if (idx >= 0)
							{
								_documentClash.TestsData.TestsReplaceWithCopy(idx, copy);
							}
						}
					}
					LoadTests();
					_checkedRowIds.Clear();
					MessageBox.Show(changes > 0 ? "Префикс/суффикс применены к коллизиям/группам." : "Нет изменений для выбранных коллизий/групп.");
					return;
				}

				// Новый случай: режим "Коллизии" + выбраны тесты, но не выбраны коллизии
				if (!isTestMode && hasCheckedTests && !hasCheckedRows)
				{
					// Работаем с коллизиями выбранных тестов
					int changes = 0;
					var allTests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList() ?? new System.Collections.Generic.List<ClashTest>();
					for (int ti = 0; ti < allTests.Count; ti++)
					{
						var test = allTests[ti];
						if (!_checkedTestIds.Contains(test.Guid)) continue;
						
						var copy = (ClashTest)test.CreateCopy();
						bool testChanged = false;

						// Группы (все уровни)
						foreach (var tpl in EnumerateAllGroupsWithLevel(copy))
						{
							var group = tpl.Group;
							var originalName = group.DisplayName ?? string.Empty;
							var newName = (prefixText ?? string.Empty) + originalName + (suffixText ?? string.Empty);
							if (!string.Equals(newName, originalName, StringComparison.Ordinal))
							{
								group.DisplayName = newName;
								changes++;
								testChanged = true;
							}
						}

						// Неклассифицированные результаты
						foreach (var r in copy.Children.OfType<ClashResult>())
						{
							var originalName = r.DisplayName ?? string.Empty;
							var newName = (prefixText ?? string.Empty) + originalName + (suffixText ?? string.Empty);
							if (!string.Equals(newName, originalName, StringComparison.Ordinal))
							{
								r.DisplayName = newName;
								changes++;
								testChanged = true;
							}
						}

						// Результаты внутри групп (все уровни)
						foreach (var g in copy.Children.OfType<ClashResultGroup>())
						{
							foreach (var r in GetAllResultsFromGroup(g))
							{
								var originalName = r.DisplayName ?? string.Empty;
								var newName = (prefixText ?? string.Empty) + originalName + (suffixText ?? string.Empty);
								if (!string.Equals(newName, originalName, StringComparison.Ordinal))
								{
									r.DisplayName = newName;
									changes++;
									testChanged = true;
								}
							}
						}

						if (testChanged)
						{
							int idx = _documentClash.TestsData.Tests.IndexOf(test);
							if (idx >= 0)
							{
								_documentClash.TestsData.TestsReplaceWithCopy(idx, copy);
							}
						}
					}
					LoadTests();
					_checkedTestIds.Clear();
					MessageBox.Show(changes > 0 ? $"Префикс/суффикс применены к коллизиям в выбранных тестах: {changes} изменений." : "Нет изменений для коллизий в выбранных тестах.");
					return;
				}

				// Если режим не соответствует выбранным чекбоксам
				if (isTestMode && !hasCheckedTests)
				{
					MessageBox.Show("В режиме 'Тесты' выберите тесты для изменения через чекбоксы.");
				}
				else if (!isTestMode && !hasCheckedRows && !hasCheckedTests)
				{
					MessageBox.Show("В режиме 'Коллизии' выберите тесты для работы с их коллизиями или выберите конкретные коллизии/группы через чекбоксы.");
				}
				else
				{
					MessageBox.Show("Выберите тесты или коллизии/группы для изменения через чекбоксы.");
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Ошибка при применении префикса/суффикса: {ex.Message}\n{ex.StackTrace}");
			}
		}

		private void TestModeRadioButton_Checked(object sender, RoutedEventArgs e)
		{
			// Режим работы с тестами
			Log("TestModeRadioButton_Checked: режим 'Тесты' активирован");
		}

		private void CollisionModeRadioButton_Checked(object sender, RoutedEventArgs e)
		{
			// Режим работы с коллизиями
			Log("CollisionModeRadioButton_Checked: режим 'Коллизии' активирован");
		}

		private void SearchByNameRadioButton_Checked(object sender, RoutedEventArgs e)
		{
			_searchByNameMode = true;
			Log("SearchByNameRadioButton_Checked: режим поиска 'По имени' активирован");
		}

		private void SearchByGuidRadioButton_Checked(object sender, RoutedEventArgs e)
		{
			_searchByNameMode = false;
			Log("SearchByGuidRadioButton_Checked: режим поиска 'По GUID' активирован");
		}

		private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (_searchTimer == null) return;

			// Reset the timer
			_searchTimer.Stop();
			_searchTimer.Start();
		}

		private void SearchTimer_Tick(object sender, EventArgs e)
		{
			if (_searchTimer == null) return;

			_searchTimer.Stop();

			string currentQuery = GetSearchText() ?? string.Empty;
			if (currentQuery != _lastSearchQuery)
			{
				_lastSearchQuery = currentQuery;
				if (string.IsNullOrEmpty(currentQuery))
				{
					// Reset to show all items
					TestsList_SelectionChanged(null, null);
				}
				else
				{
					// Apply dynamic filtering
					ApplySearchFilter(currentQuery);
				}
			}
		}

		private void GridViewColumnHeaderClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
		{
			var header = sender as System.Windows.Controls.TextBlock;
			if (header == null || header.Tag == null) return;
			string propertyName = header.Tag.ToString();
			bool ascending = !_sortDirections.ContainsKey(propertyName) || !_sortDirections[propertyName];
			_sortDirections[propertyName] = ascending;

			var view = System.Windows.Data.CollectionViewSource.GetDefaultView(CollisionsList.ItemsSource);
			if (view == null) return;

			view.SortDescriptions.Clear();
			view.SortDescriptions.Add(new System.ComponentModel.SortDescription(propertyName,
				ascending ? System.ComponentModel.ListSortDirection.Ascending : System.ComponentModel.ListSortDirection.Descending));
			view.Refresh();
		}	

		private object GetSortValue(object item, string propertyName)
		{
			var prop = item.GetType().GetProperty(propertyName);
			if (prop == null) return string.Empty;
			var value = prop.GetValue(item)?.ToString() ?? string.Empty;
			// Try to parse as number
			if (double.TryParse(value, out double num))
				return num;
			return value;
		}

		private void RefreshButton_Click(object sender, RoutedEventArgs e)
		{
			// Обновляем список коллизий на основе текущего выбора тестов
			TestsList_SelectionChanged(null, null);
		}

		private string GetGridIntersectionInfo(ModelItem item1, ModelItem item2)
		{
			string grid1 = GetGridIntersectionFromItem(item1);
			string grid2 = GetGridIntersectionFromItem(item2);

			if (!string.IsNullOrEmpty(grid1) && !string.IsNullOrEmpty(grid2))
			{
				if (grid1 == grid2)
					return $"Grid: {grid1}";
				else
					return $"Grid Intersection: {grid1} x {grid2}";
			}
			else if (!string.IsNullOrEmpty(grid1))
				return $"Grid: {grid1}";
			else if (!string.IsNullOrEmpty(grid2))
				return $"Grid: {grid2}";
			else
				return "N/A";
		}

		private string GetGridIntersectionFromItem(ModelItem item)
		{
			if (item == null) return "N/A";
			try
			{
				foreach (PropertyCategory cat in item.PropertyCategories)
				{
					foreach (DataProperty prop in cat.Properties)
					{
						// Search for grid-related properties
						if (prop.DisplayName.IndexOf("Grid", StringComparison.OrdinalIgnoreCase) >= 0 ||
							prop.DisplayName.IndexOf("Сетка", StringComparison.OrdinalIgnoreCase) >= 0 ||
							prop.DisplayName.IndexOf("Intersection", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							try
							{
								return prop.Value?.ToDisplayString() ?? "N/A";
							}
							catch
							{
								return "N/A";
							}
						}
					}
				}
			}
			catch
			{
				return "N/A";
			}
			return "N/A";
		}

		private string GetLevelFromGroup(ClashResultGroup group)
		{
			if (group == null) return "N/A";
			// Get level from the first result in the group
			var firstResult = GetAllResultsFromGroup(group).FirstOrDefault();
			if (firstResult != null)
			{
				return GetLevelFromItems(firstResult.CompositeItem1, firstResult.CompositeItem2, firstResult);
			}
			return "N/A";
		}

		private string GetLevelFromItems(ModelItem item1, ModelItem item2, ClashResult clash = null)
		{
			// First priority: try to get level from the ClashResult itself
			if (clash != null)
			{
				string clashLevel = GetLevelFromClashResult(clash);
				if (!string.IsNullOrEmpty(clashLevel) && clashLevel != "N/A")
				{
					return clashLevel;
				}
			}

			// Second priority: get level from ModelItem properties
			string level1 = GetLevelFromItem(item1);
			string level2 = GetLevelFromItem(item2);

			// Try to match Clash Detective behavior - prefer the first valid level
			if (!string.IsNullOrEmpty(level1) && level1 != "N/A")
				return level1;
			else if (!string.IsNullOrEmpty(level2) && level2 != "N/A")
				return level2;
			else
				return "N/A";
		}

		/// <summary>
		/// Try to get level information from the ClashResult itself first
		/// </summary>
		private string GetLevelFromClashResult(ClashResult clash)
		{
			if (clash == null) return "N/A";

			try
			{
				// Check if ClashResult has any level-related properties
				// This is a more direct approach that might match Clash Detective's behavior

				// First, try to get level from the clash result's display name or description
				string clashDisplayName = clash.DisplayName ?? string.Empty;
				if (!string.IsNullOrEmpty(clashDisplayName) && IsValidLevelValue(clashDisplayName))
				{
					return clashDisplayName;
				}

				// Try to find level information in clash properties (if any exist)
				// ClashResult might have additional properties we're not currently checking

				// For now, return N/A and let the ModelItem approach handle it
				// But this gives us a hook to add ClashResult-specific level detection later
				return "N/A";
			}
			catch
			{
				return "N/A";
			}
		}

        private string GetLevelFromItem(ModelItem item)
		{
			if (item == null) return "N/A";
			try
			{
				// First, try the targeted approach using FindPropertyByDisplayName with common category/property combinations
				string[] categoryNames = { "Элемент", "Объект", "Свойства элемента", "Element", "Object", "Properties", "Identity Data", "Данные идентификации" };
				string[] propertyNames = { "Level", "Этаж", "Floor", "Storey", "Story", "Уровень", "Level Name", "Имя уровня", "Floor Name", "Имя этажа" };

				foreach (string catName in categoryNames)
				{
					foreach (string propName in propertyNames)
					{
						try
						{
							DataProperty prop = item.PropertyCategories.FindPropertyByDisplayName(catName, propName);
							if (prop != null)
							{
								string value = prop.Value?.ToDisplayString() ?? "N/A";
								if (IsValidLevelValue(value))
								{
									return value;
								}
							}
						}
						catch
						{
							// Continue searching
						}
					}
				}

				// Fallback: search through all properties for level-related keywords in property names
				foreach (PropertyCategory cat in item.PropertyCategories)
				{
					foreach (DataProperty prop in cat.Properties)
					{
						try
						{
							string displayName = prop.DisplayName ?? string.Empty;
							if (displayName.IndexOf("Level", StringComparison.OrdinalIgnoreCase) >= 0 ||
								displayName.IndexOf("Этаж", StringComparison.OrdinalIgnoreCase) >= 0 ||
								displayName.IndexOf("Floor", StringComparison.OrdinalIgnoreCase) >= 0 ||
								displayName.IndexOf("Storey", StringComparison.OrdinalIgnoreCase) >= 0 ||
								displayName.IndexOf("Story", StringComparison.OrdinalIgnoreCase) >= 0 ||
								displayName.IndexOf("Уровень", StringComparison.OrdinalIgnoreCase) >= 0)
							{
								string value = prop.Value?.ToDisplayString() ?? "N/A";
								if (IsValidLevelValue(value))
								{
									return value;
								}
							}
						}
						catch
						{
							// Continue searching
						}
					}
				}

				// Last resort: try to get level from parent item if current item doesn't have it
				if (item.Parent != null)
				{
					return GetLevelFromItem(item.Parent);
				}
			}
			catch
			{
				return "N/A";
			}
			return "N/A";
		}

		/// <summary>
		/// Validates if a string value looks like a legitimate level name
		/// </summary>
		private bool IsValidLevelValue(string value)
		{
			if (string.IsNullOrEmpty(value) || value == "N/A")
				return false;

			// Reject values that are too long (likely descriptions or family names)
			if (value.Length > 50)
				return false;

			// Reject values that contain certain keywords indicating they're not level names
			string lowerValue = value.ToLower();
			string[] invalidKeywords = { "семейство", "family", "тип", "type", "описание", "description", "комментарий", "comment" };

			foreach (string keyword in invalidKeywords)
			{
				if (lowerValue.Contains(keyword))
					return false;
			}

			// Accept values that look like level names (contain level-related terms but are reasonably short)
			if (lowerValue.Contains("этаж") || lowerValue.Contains("floor") || lowerValue.Contains("level") ||
				lowerValue.Contains("storey") || lowerValue.Contains("story") || lowerValue.Contains("уровень"))
			{
				// Additional validation: should not contain too many spaces or special characters
				// (indicating it's a complex description rather than a simple level name)
				int spaceCount = value.Count(c => c == ' ');
				int specialCharCount = value.Count(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c) && c != '(' && c != ')' && c != '-' && c != '.');

				// Allow reasonable level names but reject complex descriptions
				if (spaceCount <= 3 && specialCharCount <= 2)
					return true;
			}

			return false;
		}

        private string FormatGridIntersectionDisplay(ClashResult clash)
        {
            if (clash == null) return "N/A";

            // First priority: try to get grid intersection from ClashResult itself
            string clashGridInfo = GetGridIntersectionFromClashResult(clash);
            if (!string.IsNullOrEmpty(clashGridInfo) && clashGridInfo != "N/A")
            {
                return clashGridInfo;
            }

            // Second priority: use item-based grid intersection
            string gridInfo = GetGridIntersectionInfo(clash.CompositeItem1, clash.CompositeItem2);
            if (gridInfo != "N/A")
            {
                return gridInfo;
            }

            // Third priority: try alternative grid intersection method with larger tolerance
            var (altLevelName, altIntersection, altLine1, altLine2, altPosition) = GetClashGridInfoAlternative(clash, 10.0);
            if (altIntersection != "N/A" || altLine1 != "N/A" || altLine2 != "N/A")
            {
                var parts = new System.Collections.Generic.List<string>();
                if (altIntersection != "N/A") parts.Add($"Grid: {altIntersection}");
                if (altLine1 != "N/A" && altLine2 != "N/A")
                {
                    if (altLine1 == altLine2)
                        parts.Add($"Line: {altLine1}");
                    else
                        parts.Add($"Lines: {altLine1} x {altLine2}");
                }
                else if (altLine1 != "N/A")
                    parts.Add($"Line: {altLine1}");
                else if (altLine2 != "N/A")
                    parts.Add($"Line: {altLine2}");

                if (parts.Count > 0)
                    return string.Join(", ", parts);
            }

            // Last resort: fall back to original nearest grid intersection calculation
            var (levelName, intersection, line1, line2, position) = GetClashGridInfo(clash);

            if (intersection == "N/A" && line1 == "N/A" && line2 == "N/A")
                return "N/A";

            var partsOrig = new System.Collections.Generic.List<string>();
            if (intersection != "N/A") partsOrig.Add($"Grid: {intersection}");
            if (line1 != "N/A" && line2 != "N/A")
            {
                if (line1 == line2)
                    partsOrig.Add($"Line: {line1}");
                else
                    partsOrig.Add($"Lines: {line1} x {line2}");
            }
            else if (line1 != "N/A")
                partsOrig.Add($"Line: {line1}");
            else if (line2 != "N/A")
                partsOrig.Add($"Line: {line2}");

            return partsOrig.Count > 0 ? string.Join(", ", partsOrig) : "N/A";
        }

        /// <summary>
        /// Try to get grid intersection information from the ClashResult itself first
        /// </summary>
        private string GetGridIntersectionFromClashResult(ClashResult clash)
        {
            if (clash == null) return "N/A";

            try
            {
                // Check if ClashResult has any grid-related properties
                // This is a more direct approach that might match Clash Detective's behavior

                // First, try to get grid info from the clash result's display name
                string clashDisplayName = clash.DisplayName ?? string.Empty;
                if (!string.IsNullOrEmpty(clashDisplayName))
                {
                    // Look for grid-related patterns in the display name
                    string lowerDisplayName = clashDisplayName.ToLower();
                    if (lowerDisplayName.Contains("grid") || lowerDisplayName.Contains("сетка") ||
                        lowerDisplayName.Contains("intersection") || lowerDisplayName.Contains("пересечение"))
                    {
                        return clashDisplayName;
                    }
                }

                // Try to find grid intersection information in clash properties (if any exist)
                // ClashResult might have additional properties we're not currently checking

                // For now, return N/A and let the other approaches handle it
                // But this gives us a hook to add ClashResult-specific grid detection later
                return "N/A";
            }
            catch
            {
                return "N/A";
            }
        }

        // ===== ДВУСТОРОННЯЯ СИНХРОНИЗАЦИЯ С CLASH DETECTIVE =====

        /// <summary>
        /// Запускает мониторинг изменений выбора в Clash Detective
        /// </summary>
        private void StartClashDetectiveMonitoring()
        {
            if (_clashDetectiveMonitorTimer != null)
            {
                _clashDetectiveMonitorTimer.Start();
                Log("Clash Detective monitoring started");
            }
        }

        /// <summary>
        /// Останавливает мониторинг изменений выбора в Clash Detective
        /// </summary>
        private void StopClashDetectiveMonitoring()
        {
            if (_clashDetectiveMonitorTimer != null)
            {
                _clashDetectiveMonitorTimer.Stop();
                Log("Clash Detective monitoring stopped");
            }
        }

        /// <summary>
        /// Обработчик таймера для проверки изменений выбора в Clash Detective
        /// </summary>
        private void ClashDetectiveMonitorTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // Проверяем, не синхронизируем ли мы уже выбор из плагина
                if (_isSyncingFromPlugin) return;

                Guid currentClashGuid = GetCurrentClashDetectiveSelection();
                if (currentClashGuid != Guid.Empty && currentClashGuid != _lastDetectedClashGuid)
                {
                    _lastDetectedClashGuid = currentClashGuid;
                    SyncSelectionFromClashDetective(currentClashGuid);
                }
            }
            catch (Exception ex)
            {
                Log($"Error in ClashDetectiveMonitorTimer_Tick: {ex.Message}");
            }
        }

        /// <summary>
        /// Получает GUID текущего выбранного элемента в Clash Detective
        /// </summary>
        private Guid GetCurrentClashDetectiveSelection()
        {
            try
            {
                var opState = _doc?.State as LcOpState;
                if (opState == null) return Guid.Empty;

                var currentIssueCtl = LcClCurrentIssue.GetInstance(opState);
                if (currentIssueCtl == null) return Guid.Empty;

                // Получаем текущий выбранный элемент через managed API
                var getMethod = currentIssueCtl.GetType().GetMethod("GetCurrentIssueAsSavedItem", BindingFlags.Public | BindingFlags.Instance);
                var savedItem = getMethod?.Invoke(currentIssueCtl, null) as SavedItem;

                return savedItem?.Guid ?? Guid.Empty;
            }
            catch
            {
                // Fallback через COM API
                try
                {
                    var comState = ComApiBridge.State;
                    if (comState == null) return Guid.Empty;

                    object comClash = null;
                    var clashProp = comState.GetType().GetProperty("Clash", BindingFlags.Public | BindingFlags.Instance);
                    comClash = clashProp?.GetValue(comState);

                    if (comClash == null)
                    {
                        foreach (var p in comState.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        {
                            var pt = p.PropertyType?.Name ?? string.Empty;
                            if (pt.IndexOf("Clash", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                comClash = p.GetValue(comState);
                                if (comClash != null) break;
                            }
                        }
                    }

                    if (comClash != null)
                    {
                        var currentResultProp = comClash.GetType().GetProperty("CurrentResult", BindingFlags.Public | BindingFlags.Instance);
                        var currentResult = currentResultProp?.GetValue(comClash);

                        if (currentResult != null)
                        {
                            var guidProp = currentResult.GetType().GetProperty("Guid", BindingFlags.Public | BindingFlags.Instance);
                            var guidObj = guidProp?.GetValue(currentResult);
                            if (guidObj is Guid g) return g;
                        }
                    }
                }
                catch { }

                return Guid.Empty;
            }
        }

        /// <summary>
        /// Синхронизирует выбор в плагине с выбранным элементом в Clash Detective
        /// </summary>
        private void SyncSelectionFromClashDetective(Guid selectedGuid)
        {
            try
            {
                Log($"Syncing selection from Clash Detective: {selectedGuid}");

                // Сначала ищем тест, содержащий выбранную коллизию
                Guid? testGuid = FindTestGuidForClashGuid(selectedGuid);

                if (testGuid.HasValue)
                {
                    // Выбираем тест в плагине
                    SelectTestInPlugin(testGuid.Value);

                    // Обновляем список коллизий для выбранного теста
                    TestsList_SelectionChanged(null, null);

                    // Теперь ищем элемент в обновленном списке коллизий
                    object itemToSelect = null;
                    foreach (var item in CollisionsList.Items)
                    {
                        var guidProp = item.GetType().GetProperty("Guid");
                        if (guidProp != null)
                        {
                            var itemGuid = (Guid)guidProp.GetValue(item);
                            if (itemGuid == selectedGuid)
                            {
                                itemToSelect = item;
                                break;
                            }
                        }
                    }

                    if (itemToSelect != null)
                    {
                        // Выбираем элемент в списке
                        CollisionsList.SelectedItem = itemToSelect;
                        CollisionsList.ScrollIntoView(itemToSelect);

                        Log($"Successfully synced selection: {selectedGuid}");
                    }
                    else
                    {
                        Log($"Item not found in plugin list after test selection: {selectedGuid}");
                    }
                }
                else
                {
                    Log($"Test not found for clash: {selectedGuid}");
                }
            }
            catch (Exception ex)
            {
                Log($"Error syncing selection from Clash Detective: {ex.Message}");
            }
        }

        /// <summary>
        /// Выбирает тест в списке тестов плагина
        /// </summary>
        private void SelectTestInPlugin(Guid testGuid)
        {
            try
            {
                foreach (var item in TestsList.Items)
                {
                    var testProp = item.GetType().GetProperty("Test");
                    if (testProp != null)
                    {
                        var test = testProp.GetValue(item) as ClashTest;
                        if (test != null && test.Guid == testGuid)
                        {
                            TestsList.SelectedItem = item;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error selecting test in plugin: {ex.Message}");
            }
        }

        /// <summary>
        /// Находит GUID теста, содержащего коллизию с заданным GUID
        /// </summary>
        private Guid? FindTestGuidForClashGuid(Guid clashGuid)
        {
            try
            {
                var allTests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList();
                if (allTests == null) return null;

                foreach (var test in allTests)
                {
                    // Ищем коллизию среди непосредственных результатов теста
                    foreach (var result in test.Children.OfType<ClashResult>())
                    {
                        if (result.Guid == clashGuid)
                        {
                            return test.Guid;
                        }
                    }

                    // Ищем коллизию среди результатов в группах (все уровни вложенности)
                    foreach (var group in test.Children.OfType<ClashResultGroup>())
                    {
                        if (FindClashInGroupRecursive(group, clashGuid))
                        {
                            return test.Guid;
                        }
                    }

                    // Ищем среди самих групп
                    foreach (var group in test.Children.OfType<ClashResultGroup>())
                    {
                        if (FindGroupRecursive(group, clashGuid))
                        {
                            return test.Guid;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error finding test for clash {clashGuid}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Рекурсивно ищет коллизию в группе и её подгруппах
        /// </summary>
        private bool FindClashInGroupRecursive(ClashResultGroup group, Guid clashGuid)
        {
            // Ищем среди непосредственных результатов группы
            foreach (var result in group.Children.OfType<ClashResult>())
            {
                if (result.Guid == clashGuid)
                {
                    return true;
                }
            }

            // Рекурсивно ищем в подгруппах
            foreach (var subGroup in group.Children.OfType<ClashResultGroup>())
            {
                if (FindClashInGroupRecursive(subGroup, clashGuid))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Рекурсивно ищет группу с заданным GUID
        /// </summary>
        private bool FindGroupRecursive(ClashResultGroup group, Guid groupGuid)
        {
            if (group.Guid == groupGuid)
            {
                return true;
            }

            // Ищем в подгруппах
            foreach (var subGroup in group.Children.OfType<ClashResultGroup>())
            {
                if (FindGroupRecursive(subGroup, groupGuid))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Обновляет флаг синхронизации при выборе в плагине
        /// </summary>
        private void OnPluginSelectionChanged()
        {
            _isSyncingFromPlugin = true;

            // Сбрасываем флаг через короткий интервал
            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(100);
            timer.Tick += (s, e) =>
            {
                _isSyncingFromPlugin = false;
                timer.Stop();
            };
            timer.Start();
        }



        /// <summary>
        /// Обработчик закрытия окна - останавливаем мониторинг
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            StopClashDetectiveMonitoring();
            base.OnClosed(e);
        }
	}
}
