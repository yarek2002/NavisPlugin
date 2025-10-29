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
using ClashManager;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using System.Windows.Data;

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
		private string _currentSortProperty = null;
		private bool _currentSortAscending = true;
		private DispatcherTimer _searchTimer;
		private string _lastSearchQuery = string.Empty;
		private DispatcherTimer _clashDetectiveMonitorTimer;
		private Guid _lastDetectedClashGuid = Guid.Empty;
		private Guid _pendingDetectedClashGuid = Guid.Empty;
		private int _pendingDetectedStableTicks = 0;
		private DateTime _lastClashDetectiveSyncUtc = DateTime.MinValue;
		private readonly TimeSpan _minClashDetectiveSyncInterval = TimeSpan.FromMilliseconds(350);
		private bool _isSyncingFromClashDetective = false;
		private DateTime _lastUserScrollUtc = DateTime.MinValue;
		private readonly TimeSpan _suppressScrollIntoViewAfterUserScroll = TimeSpan.FromMilliseconds(1800);
		private bool _isUserScrolling = false;
		private DateTime _lastScrollEventUtc = DateTime.MinValue;
		private DispatcherTimer _scrollIdleTimer;
		private readonly TimeSpan _scrollIdleTimeout = TimeSpan.FromMilliseconds(700);
		private bool _suppressUIUpdates = false;
		private bool _isSyncingFromPlugin = false;
		private bool _pendingListRefresh = false;
		private readonly System.Collections.Generic.Dictionary<Guid, string> _levelCache = new System.Collections.Generic.Dictionary<Guid, string>();
		private readonly System.Collections.Generic.Dictionary<Guid, string> _gridCache = new System.Collections.Generic.Dictionary<Guid, string>();

		// Класс для оптимизированного отображения элементов списка
		public class CollisionListItem : INotifyPropertyChanged
		{
			private bool _isSelected;
			private string _status;

			public string Name { get; set; }
			public string Status 
			{ 
				get => _status;
				set
				{
					if (_status != value)
					{
						_status = value;
						OnPropertyChanged();
					}
				}
			}
			public string AssignedTo { get; set; }
			public Guid Guid { get; set; }
			public Guid TestGuid { get; set; }
			public bool IsGroup { get; set; }

			// Список доступных статусов
			public static List<string> StatusOptions { get; } = new List<string>
			{
				"Новый",
				"Активный", 
				"Проанализирован",
				"Утвержден",
				"Исправлен"
			};
			
			// Экземплярная ссылка на статическое свойство для привязки в XAML
			public List<string> StatusOptionsInstance => StatusOptions;
			
			public bool IsSelected 
			{ 
				get => _isSelected;
				set
				{
					if (_isSelected != value)
					{
						_isSelected = value;
						OnPropertyChanged();
					}
				}
			}
			
			public string Level { get; set; }
			public string GridIntersection { get; set; }
			public string TestName { get; set; }
			public object Item { get; set; }
			public int GroupClashCount { get; set; }

			public event PropertyChangedEventHandler PropertyChanged;

			protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
			{
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
			}
		}

		// Преобразует enum Navisworks в русскую метку для отображения/привязки
		private static string ToRuStatus(ClashResultStatus status)
		{
			switch (status)
			{
				case ClashResultStatus.New: return "Новый";
				case ClashResultStatus.Active: return "Активный";
				case ClashResultStatus.Reviewed: return "Проанализирован";
				case ClashResultStatus.Approved: return "Утвержден";
				case ClashResultStatus.Resolved: return "Исправлен";
				default: return "Новый";
			}
		}

	public ManagerCollisionView()
	{
		InitializeComponent();
		// Сброс лога при старте окна
		try 
		{ 
			File.WriteAllText(GetLogPath(), ""); 
			Log("=== ManagerCollisionView started ===");
		} 
		catch (Exception ex) { LogError("Failed to reset log file", ex); }
		// Включаем клавиатурную интероп-совместимость для модельного окна в Win32-хосте (Navisworks)
		try { ElementHost.EnableModelessKeyboardInterop(this); } catch (Exception ex) { LogError("Failed to enable modeless keyboard interop", ex); }
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
		catch (Exception ex) { LogError("Failed to set window owner", ex); }
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
					// Отслеживание пользовательского скролла для подавления авто-прокрутки
					var scrollViewer = FindVisualChild<ScrollViewer>(CollisionsList);
					if (scrollViewer != null)
					{
						scrollViewer.ScrollChanged += OnCollisionsListScrollChanged;
					}

                    // Инициализация таймера простоя скролла
                    _scrollIdleTimer = new DispatcherTimer();
                    _scrollIdleTimer.Interval = TimeSpan.FromMilliseconds(150);
                    _scrollIdleTimer.Tick += (o, args2) =>
                    {
                        if ((DateTime.UtcNow - _lastScrollEventUtc) > _scrollIdleTimeout)
                        {
                            _isUserScrolling = false;
                            _suppressUIUpdates = false;
							_scrollIdleTimer.Stop();
                            try { System.Windows.Input.Mouse.OverrideCursor = null; } catch {}
                            Log("Scroll idle detected: resume sync");
                            // Возобновим мониторинг Clash Detective
                            _clashDetectiveMonitorTimer?.Start();
							// Выполним отложенное обновление списка, если было запрошено во время скролла
							TryPerformPendingListRefresh();
                        }
					};
				}
				catch (Exception ex) { LogError("Failed to initialize window loaded event", ex); }
			};
            CollisionsList.PreviewMouseWheel += (s, e) =>
            {
                _lastUserScrollUtc = DateTime.UtcNow;
                _isUserScrolling = true;
                _suppressUIUpdates = true;
                _lastScrollEventUtc = DateTime.UtcNow;
                if (!(_scrollIdleTimer?.IsEnabled ?? false)) _scrollIdleTimer?.Start();
                // Во время скролла приостанавливаем мониторинг Clash Detective
                _clashDetectiveMonitorTimer?.Stop();
                try { System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Arrow; } catch {}
            };

            CollisionsList.PreviewMouseUp += (s, e) =>
            {
                // Дадим таймеру простоя определить окончание скролла и потом возобновим мониторинг
                if (!(_scrollIdleTimer?.IsEnabled ?? false)) _scrollIdleTimer?.Start();
            };
			_doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
			_documentClash = _doc.GetClash();
			
			// Подписываемся на события изменения модели
			SubscribeToModelEvents();
			
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

			//restorecolumns order and width if user saved it
			LoadColumnLayout();
		}

	private void LoadTests()
	{
		try
		{
			var tests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList() ?? Enumerable.Empty<ClashTest>().ToList();
			// Оборачиваем в объекты с IsSelected для чекбоксов
			var testRows = tests.Select(t => new { Test = t, DisplayName = t.DisplayName, IsSelected = false, Guid = t.Guid }).ToList();
			TestsList.ItemsSource = testRows;
			Log($"Loaded {tests.Count} tests");
		}
		catch (Exception ex)
		{
			LogError("Error in LoadTests", ex);
		}
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
                            LogError("Error getting closest intersection", ex);
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
                LogError("Error in GetClashGridInfo", ex);
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
                                catch (Exception ex) { LogError("Error getting test intersection", ex); continue; }

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
                        catch (Exception ex) { LogError("Error getting center intersection", ex); continue; }

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
			// Не обновляем список во время пользовательского скролла
			if (_suppressUIUpdates) return;
			
			// Если выбрано несколько тестов через чекбоксы — показываем объединённый список коллизий этих тестов
			var allTests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList() ?? new System.Collections.Generic.List<ClashTest>();
			var checkedTests = allTests.Where(t => _checkedTestIds.Contains(t.Guid)).ToList();
			if (checkedTests.Count > 0)
			{
				var mergedRows = new System.Collections.Generic.List<object>();
				foreach (var t in checkedTests)
				{
					var groupRowsMerged = EnumerateAllGroupsWithLevel(t)
						.Select(x => new CollisionListItem
						{
							Name = x.Group.DisplayName ?? string.Empty,
							Status = ToRuStatus(x.Group.Status),
							AssignedTo = (x.Group.AssignedTo ?? string.Empty).ToString(),
							Guid = x.Group.Guid,
							TestGuid = t.Guid,
							IsGroup = true,
							IsSelected = _checkedRowIds.Contains(x.Group.Guid),
							Level = GetCachedLevelFromGroup(x.Group),
							GridIntersection = GetCachedGridFromGroup(x.Group),
							TestName = t.DisplayName ?? string.Empty,
							Item = x.Group,
							GroupClashCount = GetAllResultsFromGroup(x.Group).Count()
						});
					var ungroupedResultRowsMerged = t.Children
						.OfType<ClashResult>()
						.Select(r => new CollisionListItem
						{
							Name = r.DisplayName ?? string.Empty,
							Status = ToRuStatus(r.Status),
							AssignedTo = (r.AssignedTo ?? string.Empty).ToString(),
							Guid = r.Guid,
							TestGuid = t.Guid,
							IsGroup = false,
							IsSelected = _checkedRowIds.Contains(r.Guid),
							Level = GetCachedLevelFromItems(r.CompositeItem1, r.CompositeItem2, r),
							GridIntersection = GetCachedGridFromResult(r),
                            TestName = t.DisplayName ?? string.Empty,
							Item = r,
							GroupClashCount = 0
						});
					mergedRows.AddRange(groupRowsMerged);
					mergedRows.AddRange(ungroupedResultRowsMerged);
				}
				CollisionsList.ItemsSource = mergedRows;
				SubscribeToCollisionItemsPropertyChanged(mergedRows);
				ApplySorting();
				UpdateCollisionCounters();
				return;
			}

			var selectedTest = (TestsList.SelectedItem != null) ? (TestsList.SelectedItem.GetType().GetProperty("Test")?.GetValue(TestsList.SelectedItem) as ClashTest) : null;
			if (selectedTest == null)
			{
				CollisionsList.ItemsSource = null;
				return;
			}

			// Показываем по одной строке на группу, плюс отдельные (негрупповые) результаты теста (оптимизированно)
			var groupRows = EnumerateAllGroupsWithLevel(selectedTest)
				.Select(x => new CollisionListItem
				{
					Name = x.Group.DisplayName ?? string.Empty,
					Status = ToRuStatus(x.Group.Status),
					AssignedTo = (x.Group.AssignedTo ?? string.Empty).ToString(),
					Guid = x.Group.Guid,
					TestGuid = selectedTest.Guid,
					IsGroup = true,
					IsSelected = _checkedRowIds.Contains(x.Group.Guid),
					Level = GetCachedLevelFromGroup(x.Group),
					GridIntersection = GetCachedGridFromGroup(x.Group),
					TestName = selectedTest.DisplayName ?? string.Empty,
					Item = x.Group,
					GroupClashCount = GetAllResultsFromGroup(x.Group).Count()
				});

			var ungroupedResultRows = selectedTest.Children
				.OfType<ClashResult>()
				.Select(r => new CollisionListItem
				{
					Name = r.DisplayName ?? string.Empty,
					Status = ToRuStatus(r.Status),
					AssignedTo = (r.AssignedTo ?? string.Empty).ToString(),
					Guid = r.Guid,
					TestGuid = selectedTest.Guid,
					IsGroup = false,
							IsSelected = _checkedRowIds.Contains(r.Guid),
					Level = GetCachedLevelFromItems(r.CompositeItem1, r.CompositeItem2, r),
					GridIntersection = GetCachedGridFromResult(r),
                    TestName = selectedTest.DisplayName ?? string.Empty,
					Item = r,
					GroupClashCount = 0
				});

			var rows = groupRows.Concat(ungroupedResultRows).ToList();
			CollisionsList.ItemsSource = rows;
			SubscribeToCollisionItemsPropertyChanged(rows);
			ApplySorting();
			UpdateCollisionCounters();
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
			try
			{
				SetSearchText(string.Empty);
				TestsList_SelectionChanged(null, null);
			}
			catch (Exception ex) { LogError("Error in ResetButton_Click", ex); }
		}
	private void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		try
		{
			if (e.Key == System.Windows.Input.Key.Enter) ApplySearch();
		}
		catch (Exception ex) { LogError("Error in SearchBox_KeyDown", ex); }
	}



	private void ApplySearch()
	{
		try
		{
			if (_isUserScrolling || _suppressUIUpdates)
			{
				_pendingListRefresh = true;
				return;
			}
			string query = (GetSearchText() ?? string.Empty).Trim();
			if (string.IsNullOrEmpty(query))
			{
				TestsList_SelectionChanged(null, null);
				return;
			}

			// Всегда используем фильтрацию для показа всех найденных элементов
			ApplySearchFilter(query);
		}
		catch (Exception ex) { LogError("Error in ApplySearch", ex); }
	}

		private void ApplySearchFilter(string query)
		{
			try
			{
				if (_isUserScrolling || _suppressUIUpdates)
				{
					_pendingListRefresh = true;
					return;
				}
				var allTests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList() ?? new System.Collections.Generic.List<ClashTest>();

				// Определяем, по каким тестам искать: выбранные через чекбоксы или все тесты
				var testsToSearch = _checkedTestIds.Count > 0
					? allTests.Where(t => _checkedTestIds.Contains(t.Guid)).ToList()
					: allTests; // Если нет выбранных через чекбоксы, используем все тесты

				if (testsToSearch.Count == 0)
				{
					CollisionsList.ItemsSource = null;
					return;
				}

				// Получаем все элементы из выбранных тестов
				var allItems = new System.Collections.Generic.List<object>();

				foreach (var test in testsToSearch)
				{
					// Добавляем группы (оптимизированно)
					var groupRows = EnumerateAllGroupsWithLevel(test)
						.Select(x => new CollisionListItem
						{
							Name = x.Group.DisplayName ?? string.Empty,
								Status = ToRuStatus(x.Group.Status),
							AssignedTo = (x.Group.AssignedTo ?? string.Empty).ToString(),
							Guid = x.Group.Guid,
							TestGuid = test.Guid,
							IsGroup = true,
							IsSelected = _checkedRowIds.Contains(x.Group.Guid),
							Level = GetCachedLevelFromGroup(x.Group),
							GridIntersection = GetCachedGridFromGroup(x.Group),
							TestName = test.DisplayName ?? string.Empty,
							Item = x.Group,
							GroupClashCount = GetAllResultsFromGroup(x.Group).Count()
						});

					// Добавляем отдельные результаты (оптимизированно)
					var ungroupedResultRows = test.Children
						.OfType<ClashResult>()
						.Select(r => new CollisionListItem
						{
							Name = r.DisplayName ?? string.Empty,
								Status = ToRuStatus(r.Status),
							AssignedTo = (r.AssignedTo ?? string.Empty).ToString(),
							Guid = r.Guid,
							TestGuid = test.Guid,
							IsGroup = false,
							IsSelected = _checkedRowIds.Contains(r.Guid),
							Level = GetCachedLevelFromItems(r.CompositeItem1, r.CompositeItem2, r),
							GridIntersection = GetCachedGridFromResult(r),
							TestName = test.DisplayName ?? string.Empty,
							Item = r,
							GroupClashCount = 0
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
				SubscribeToCollisionItemsPropertyChanged(sortedItems);
				ApplySorting();
				UpdateCollisionCounters();
				
				Log($"ApplySearchFilter: applied {sortedItems.Count} items for query: {query}");
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Ошибка при фильтрации: {ex.Message}");
			}
		}

		/// <summary>
		/// Обновляет счетчики коллизий (всего и выделено)
		/// </summary>
		private void UpdateCollisionCounters()
		{
			try
			{
				var itemsSource = CollisionsList.ItemsSource;
				if (itemsSource == null)
				{
					TotalCollisionsText.Text = "Всего: 0";
					SelectedCollisionsText.Text = "Выделено: 0";
					return;
				}

				int totalCount = 0;
				int selectedCount = 0;

				foreach (var item in itemsSource)
				{
					if (item is CollisionListItem collisionItem)
					{
						totalCount++;
						if (collisionItem.IsSelected)
						{
							selectedCount++;
						}
					}
				}

				TotalCollisionsText.Text = $"Всего: {totalCount}";
				SelectedCollisionsText.Text = $"Выделено: {selectedCount}";
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Ошибка при обновлении счетчиков коллизий: {ex.Message}");
				TotalCollisionsText.Text = "Всего: 0";
				SelectedCollisionsText.Text = "Выделено: 0";
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
		catch (Exception ex) { LogError("Error in OpenFoundByName", ex); }
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
		catch (Exception ex) { LogError("Error in OpenFound", ex); }
	}

	private string GetSearchText()
		{
			try
			{
				var tb = System.Windows.LogicalTreeHelper.FindLogicalNode(this, "SearchBox") as System.Windows.Controls.TextBox;
				return tb?.Text ?? string.Empty;
			}
			catch (Exception ex) { LogError("Error in GetSearchText", ex); return string.Empty; }
		}

	private bool GetCheckBoxState(string name)
		{
			try
			{
				var cb = System.Windows.LogicalTreeHelper.FindLogicalNode(this, name) as System.Windows.Controls.CheckBox;
				return cb?.IsChecked ?? false;
			}
			catch (Exception ex) { LogError($"Error in GetCheckBoxState for {name}", ex); return false; }
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

		/// <summary>
		/// Подписывается на события PropertyChanged для элементов списка коллизий
		/// </summary>
		private void SubscribeToCollisionItemsPropertyChanged(System.Collections.Generic.IEnumerable<object> items)
		{
			foreach (var item in items)
			{
				if (item is CollisionListItem collisionItem)
				{
					collisionItem.PropertyChanged += CollisionItem_PropertyChanged;
				}
			}
		}

		/// <summary>
		/// Обработчик изменения свойства CollisionListItem
		/// </summary>
		private void CollisionItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(CollisionListItem.IsSelected) && sender is CollisionListItem item)
			{
				// Синхронизируем с HashSet
				if (item.IsSelected)
				{
					_checkedRowIds.Add(item.Guid);
				}
				else
				{
					_checkedRowIds.Remove(item.Guid);
				}

				// Обновляем счетчики коллизий
				UpdateCollisionCounters();
			}
		}

		// Обработчики кликов по чекбоксам в XAML
		private void CollisionCheckBox_Click(object sender, RoutedEventArgs e)
		{
			var cb = sender as System.Windows.Controls.CheckBox;
			if (cb == null) return;
			if (_suppressCheckboxHandlers) return;

			var item = cb.DataContext as CollisionListItem;
			if (item == null) return;

			// Предотвращаем повторную обработку события
			e.Handled = true;

			int currentIndex = CollisionsList.Items.IndexOf(item);
			bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
			bool targetChecked = cb.IsChecked == true;

			// Если выделено несколько элементов в списке, применяем действие ко всем выделенным
			if (CollisionsList.SelectedItems.Count > 1 && CollisionsList.SelectedItems.Contains(item))
			{
				_suppressCheckboxHandlers = true;
				try
				{
					foreach (var selectedItem in CollisionsList.SelectedItems)
					{
						if (selectedItem is CollisionListItem selectedCollisionItem)
						{
							selectedCollisionItem.IsSelected = targetChecked;
						}
					}
				}
				finally { _suppressCheckboxHandlers = false; }
			}
			else if (isShift && _lastCollisionClickIndex >= 0)
			{
				// Shift-выбор: применяем действие ко всем элементам между последним и текущим кликом
				int from = Math.Min(_lastCollisionClickIndex, currentIndex);
				int to = Math.Max(_lastCollisionClickIndex, currentIndex);

				_suppressCheckboxHandlers = true;
				try
				{
					for (int i = from; i <= to; i++)
					{
						if (CollisionsList.Items[i] is CollisionListItem shiftItem)
						{
							shiftItem.IsSelected = targetChecked; // меняем состояние через модель
						}
					}
				}
				finally { _suppressCheckboxHandlers = false; }
			}
			else
			{
				// Одиночный клик
				item.IsSelected = targetChecked;
			}

			_lastCollisionClickIndex = currentIndex;
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
		catch (Exception ex) { LogError("Error in SetCheckboxState", ex); }
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
		catch (Exception ex) { LogError("Error in FindFirstCheckbox", ex); }
		return null;
	}

	private void SetSearchText(string text)
	{
		try
		{
			var tb = System.Windows.LogicalTreeHelper.FindLogicalNode(this, "SearchBox") as System.Windows.Controls.TextBox;
			if (tb != null) tb.Text = text ?? string.Empty;
		}
		catch (Exception ex) { LogError("Error in SetSearchText", ex); }
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
					try { sel?.Select(); Log($"Activated panel: {n}"); } catch (Exception ex) { LogError($"Failed to activate panel: {n}", ex); }
					break;
				}
			}
		}
		catch (Exception ex) { LogError("Failed to activate panel", ex); }
		}

	private static string GetLogPath()
	{
		try 
		{ 
			string logDir = @"C:\temp";
			if (!Directory.Exists(logDir))
			{
				Directory.CreateDirectory(logDir);
			}
			return Path.Combine(logDir, "ClashSelection.log"); 
		} 
		catch (Exception ex) 
		{ 
			System.Diagnostics.Debug.WriteLine($"Error getting log path: {ex.Message}");
			return "ClashSelection.log"; 
		}
	}

	private static void Log(string message)
	{
		try
		{
			File.AppendAllText(GetLogPath(), $"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n");
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Error writing log: {ex.Message}");
		}
	}

	private static void LogError(string message, Exception ex = null)
	{
		try
		{
			string errorMsg = $"[ERROR] {message}";
			if (ex != null)
			{
				errorMsg += $"\r\nException: {ex.GetType().Name}: {ex.Message}\r\nStackTrace: {ex.StackTrace}";
			}
			File.AppendAllText(GetLogPath(), $"[{DateTime.Now:HH:mm:ss.fff}] {errorMsg}\r\n");
		}
		catch (Exception logEx)
		{
			System.Diagnostics.Debug.WriteLine($"Error writing error log: {logEx.Message}");
		}
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
		catch (Exception ex) { LogError("Failed to rename group", ex); return false; }
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
				try { m.Invoke(target, args); return true; } catch (Exception ex) { LogError($"Failed to invoke method: {name}", ex); }
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
				try { sel?.Select(); } catch (Exception ex) { LogError("Error selecting automation item", ex); }
			}
			catch (Exception ex) { LogError("Error in NavigateToAutomationItemByIndex", ex); }
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
					var checkedTestIdsCopy = _checkedTestIds.ToList();
					foreach (var test in allTests)
					{
						if (!checkedTestIdsCopy.Contains(test.Guid)) continue;
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
				// Исключение: в режиме "Коллизии" выбранные тесты означают работу с их коллизиями
				if (hasCheckedTests && hasCheckedRows && isTestMode)
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
		try
		{
			if (_searchTimer == null) return;

			// Reset the timer
			_searchTimer.Stop();
			_searchTimer.Start();
			
			Log($"SearchBox_TextChanged: timer restarted");
		}
		catch (Exception ex) { LogError("Error in SearchBox_TextChanged", ex); }
	}

	private void SearchTimer_Tick(object sender, EventArgs e)
	{
		try
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
		
		Log("Search Timer completed successfully");
		}
		catch (Exception ex) { LogError("Error in SearchTimer_Tick", ex); }
		
	}

		private void GridViewColumnHeaderClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
		{
			var header = sender as System.Windows.Controls.TextBlock;
			if (header == null || header.Tag == null) return;
			string propertyName = header.Tag.ToString();
			bool ascending = !_sortDirections.ContainsKey(propertyName) || !_sortDirections[propertyName];
			_sortDirections[propertyName] = ascending;
			
			// Сохраняем текущую сортировку
			_currentSortProperty = propertyName;
			_currentSortAscending = ascending;

			ApplySorting();
		}	

		private void ApplySorting()
		{
			if (string.IsNullOrEmpty(_currentSortProperty)) return;
			
			var view = System.Windows.Data.CollectionViewSource.GetDefaultView(CollisionsList.ItemsSource);
			if (view == null) return;

			view.SortDescriptions.Clear();
			view.SortDescriptions.Add(new System.ComponentModel.SortDescription(_currentSortProperty,
				_currentSortAscending ? System.ComponentModel.ListSortDirection.Ascending : System.ComponentModel.ListSortDirection.Descending));
			view.Refresh();
		}

		// Оптимизированные методы с кэшированием
		private string GetCachedLevelFromGroup(ClashResultGroup group)
		{
			if (_levelCache.TryGetValue(group.Guid, out string cachedLevel))
				return cachedLevel;
			
			string level = GetLevelFromGroup(group);
			_levelCache[group.Guid] = level;
			return level;
		}

		private string GetCachedGridFromGroup(ClashResultGroup group)
		{
			if (_gridCache.TryGetValue(group.Guid, out string cachedGrid))
				return cachedGrid;
			
			string grid = GetGridIntersectionFromGroup(group);
			_gridCache[group.Guid] = grid;
			return grid;
		}

		private string GetCachedLevelFromItems(ModelItem item1, ModelItem item2, ClashResult result)
		{
			if (_levelCache.TryGetValue(result.Guid, out string cachedLevel))
				return cachedLevel;
			
			string level = GetLevelFromItems(item1, item2, result);
			_levelCache[result.Guid] = level;
			return level;
		}

		private string GetCachedGridFromResult(ClashResult result)
		{
			if (_gridCache.TryGetValue(result.Guid, out string cachedGrid))
				return cachedGrid;
			
			string grid = FormatGridIntersectionDisplay(result);
			_gridCache[result.Guid] = grid;
			return grid;
		}

		// Очистка кэша при необходимости
		private void ClearCache()
		{
			_levelCache.Clear();
			_gridCache.Clear();
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

		private void ExportCsvButton_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				// Получаем все тесты из документа (не только отображаемые)
				var allTests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList();
				if (allTests == null || allTests.Count == 0)
				{
					MessageBox.Show("Нет тестов коллизий для экспорта.");
					return;
				}

				// Собираем все коллизии из всех тестов
				var allCollisionItems = new System.Collections.Generic.List<CollisionListItem>();

				foreach (var test in allTests)
				{
					// Добавляем группы с их коллизиями (все уровни)
					foreach (var tpl in EnumerateAllGroupsWithLevel(test))
					{
						var group = tpl.Group;
						var groupItem = new CollisionListItem
						{
							Name = group.DisplayName ?? string.Empty,
							Status = ToRuStatus(group.Status),
							AssignedTo = (group.AssignedTo ?? string.Empty).ToString(),
							Guid = group.Guid,
							IsGroup = true,
							Item = group,
							TestName = test.DisplayName ?? string.Empty,
							GroupClashCount = GetAllResultsFromGroup(group).Count(),
							Level = GetCachedLevelFromGroup(group),
							GridIntersection = GetCachedGridFromGroup(group),
							TestGuid = test.Guid
						};
						allCollisionItems.Add(groupItem);
					}

					// Добавляем одиночные коллизии (не в группах)
					foreach (var result in test.Children.OfType<ClashResult>())
					{
						var resultItem = new CollisionListItem
						{
							Name = result.DisplayName ?? string.Empty,
							Status = ToRuStatus(result.Status),
							AssignedTo = (result.AssignedTo ?? string.Empty).ToString(),
							Guid = result.Guid,
							IsGroup = false,
							Item = result,
							TestName = test.DisplayName ?? string.Empty,
							GroupClashCount = 0,
							Level = GetCachedLevelFromItems(result.CompositeItem1, result.CompositeItem2, result),
							GridIntersection = GetCachedGridFromResult(result),
							TestGuid = test.Guid
						};
						allCollisionItems.Add(resultItem);
					}
				}

				if (allCollisionItems.Count == 0)
				{
					MessageBox.Show("Коллизии не найдены — нечего экспортировать.");
					return;
				}

				// Выбираем путь сохранения
				var sfd = new Microsoft.Win32.SaveFileDialog
				{
					Filter = "CSV (*.csv)|*.csv",
					FileName = $"Отчет_Коллизии_{DateTime.Now:yyyyMMdd}.csv",
					OverwritePrompt = true
				};
				if (sfd.ShowDialog() != true) return;

				using (var writer = new StreamWriter(sfd.FileName, false, System.Text.Encoding.UTF8))
				{
					// Заголовки соответствуют видимым столбцам
					WriteCsvRow(writer,
						"Имя",
						"Статус",
						"Назначение",
						"GUID",
						"Уровень",
						"Пересечения сетки",
						"Название теста",
						"Кол-во коллизий в группе");

					foreach (var c in allCollisionItems)
					{
						WriteCsvRow(writer,
							c.Name,
							c.Status,
							c.AssignedTo,
							c.Guid.ToString(),
							c.Level,
							c.GridIntersection,
							c.TestName,
							c.GroupClashCount.ToString());
					}
				}

				MessageBox.Show($"Экспорт завершен. Экспортировано {allCollisionItems.Count} коллизий из {allTests.Count} тестов.");
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Ошибка экспорта: {ex.Message}");
			}
		}

		private static void WriteCsvRow(StreamWriter writer, params string[] columns)
		{
			// Экранируем значения по CSV-правилам
			string Escape(string s)
			{
				if (s == null) return string.Empty;
				s = s.Replace("\r", " ").Replace("\n", " ");
				bool mustQuote = s.Contains(';') || s.Contains('"') || s.Contains(',') || s.Contains('\t');
				// Используем ; как разделитель, чтобы не конфликтовать с локальными Excel
				string value = s.Replace("\"", "\"\"");
				return mustQuote ? $"\"{value}\"" : value;
			}

			// Разделитель — ; (точка с запятой)
			var line = string.Join(";", columns.Select(Escape));
			writer.WriteLine(line);
		}

		/// <summary>
		/// Обновляет список коллизий для отображения изменений
		/// </summary>
		private void RefreshCollisionsList()
		{
			try
			{
				// Если сейчас идёт синхронизация или пользователь скроллит — откладываем refresh
				if (_isSyncingFromClashDetective || _isUserScrolling || _suppressUIUpdates)
				{
					_pendingListRefresh = true;
					return;
				}

				// Если активен поиск — пере-применяем фильтр, иначе не пересобираем весь список
				if (!string.IsNullOrEmpty(_lastSearchQuery))
				{
					Log("RefreshCollisionsList: активен поиск, пере-применяем ApplySearch без полной пересборки");
					ApplySearch();
				}
				else
				{
					// Легкий рефреш текущего представления, без смены ItemsSource
					var view = System.Windows.Data.CollectionViewSource.GetDefaultView(CollisionsList.ItemsSource);
					view?.Refresh();
					Log("RefreshCollisionsList: выполнен легкий refresh текущего представления");
				}

				// Обновляем счетчики
				UpdateCollisionCounters();
				_pendingListRefresh = false;
			}
			catch (Exception ex)
			{
				Log($"Ошибка при обновлении списка коллизий: {ex.Message}");
			}
		}

		private void TryPerformPendingListRefresh()
		{
			if (_pendingListRefresh && !_isUserScrolling && !_suppressUIUpdates)
			{
				RefreshCollisionsList();
			}
		}

		/// <summary>
		/// Подписывается на события изменения модели Navisworks
		/// </summary>
		private void SubscribeToModelEvents()
		{
			try
			{
                // Подписываемся на события изменения документа
                Application.ActiveDocumentChanged += OnDocumentChanged;
                var clashDoc = Application.ActiveDocument?.Clash as DocumentClash;
                if (clashDoc?.TestsData != null)
                {
                    clashDoc.TestsData.Changed += OnTestsChanged;
                }
            }
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Ошибка при подписке на события модели: {ex.Message}");
			}
		}

		/// <summary>
		/// Обработчик изменения файла документа
		/// </summary>
		private void OnDocumentChanged(object sender, EventArgs e)
		{
			try
			{
				// Очищаем все кэши и состояние при изменении документа
				ClearAllCachesAndState();
				
				// Обновляем ссылки на документ и тесты коллизий
				_doc = Application.ActiveDocument;
				_documentClash = _doc?.GetClash();
				
				// Переподписываемся на события
				SubscribeToModelEvents();
				
				// Перезагружаем данные
				LoadTests();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Ошибка при обработке изменения документа: {ex.Message}");
			}
		}

        /// <summary>
        /// Handle close of ManagerCollisionView window
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
			SaveColumnLayout();

            StopClashDetectiveMonitoring();
            base.OnClosed(e);
        }


		/// <summary>
		/// Load saved GridView column order and widths (if exist)
		/// Matching by Tag first, then by Header string. Unknown columns left in their current order.
		/// </summary>
		// ...existing code...
private void SaveColumnLayout()
{
    try
    {
        var gridView = CollisionsList.View as System.Windows.Controls.GridView;
        if (gridView == null) return;

        var list = new System.Collections.Generic.List<ColumnLayoutItem>();
        foreach (var column in gridView.Columns)
        {
            list.Add(new ColumnLayoutItem
            {
                Header = column.Header?.ToString() ?? string.Empty,
                Tag = GetColumnKey(column),
                Width = column.Width
            });
        }

        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NavisworksClashManager");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "ColumnLayout.json");

        var jsonOut = JsonConvert.SerializeObject(list, Formatting.Indented);
        File.WriteAllText(path, jsonOut);
        Log($"Column layout saved to {path}");
    }
    catch (Exception ex)
    {
        Log($"Error saving column layout: {ex.Message}");
    }
}

private void LoadColumnLayout()
{
    try
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NavisworksClashManager",
            "ColumnLayout.json");
        if (!File.Exists(path)) return;

        var json = File.ReadAllText(path);
        var items = JsonConvert.DeserializeObject<System.Collections.Generic.List<ColumnLayoutItem>>(json);
        if (items == null || items.Count == 0) return;

        var gridView = CollisionsList.View as System.Windows.Controls.GridView;
        if (gridView == null) return;

        var existing = gridView.Columns.ToList();
        var used = new System.Collections.Generic.HashSet<System.Windows.Controls.GridViewColumn>();
        var ordered = new System.Collections.Generic.List<System.Windows.Controls.GridViewColumn>();

        foreach (var it in items)
        {
            System.Windows.Controls.GridViewColumn found = null;
            if (!string.IsNullOrEmpty(it.Tag))
                found = existing.FirstOrDefault(c => string.Equals(GetColumnKey(c), it.Tag, StringComparison.Ordinal));
            if (found == null && !string.IsNullOrEmpty(it.Header))
                found = existing.FirstOrDefault(c => (c.Header?.ToString() ?? string.Empty) == it.Header);
            if (found != null && !used.Contains(found))
            {
                if (it.Width > 0) found.Width = it.Width;
                ordered.Add(found);
                used.Add(found);
            }
        }

        foreach (var c in existing)
        {
            if (!used.Contains(c)) ordered.Add(c);
        }

        gridView.Columns.Clear();
        foreach (var c in ordered) gridView.Columns.Add(c);

        Log($"Loaded column layout from {path}");
    }
    catch (Exception ex)
    {
        Log($"LoadColumnLayout error: {ex.Message}");
    }
}
// ...existing code...

		private class ColumnLayoutItem
		{
			[JsonProperty("Header")]
			public string Header { get; set; }
			[JsonProperty("Tag")]
			public string Tag { get; set; }
			[JsonProperty("Width")]
			public double Width { get; set; }
		}

        private static string GetColumnKey(System.Windows.Controls.GridViewColumn column)
        {
            // Prefer binding path as a stable identifier
            if (column.DisplayMemberBinding is Binding binding && binding.Path != null && !string.IsNullOrEmpty(binding.Path.Path))
            {
                return binding.Path.Path;
            }
            // Fallback to header text
            return GetHeaderText(column.Header);
        }

        private static string GetHeaderText(object header)
        {
            if (header == null) return string.Empty;
            if (header is string s) return s;
            if (header is TextBlock tb) return tb.Text ?? string.Empty;
            if (header is ContentControl cc)
            {
                if (cc.Content is TextBlock ctb) return ctb.Text ?? string.Empty;
                return cc.Content?.ToString() ?? string.Empty;
            }
            return header.ToString() ?? string.Empty;
        }


        /// <summary>
        /// Handle copy menu item click for text controls
        /// </summary>
        private void CopyTextMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var menuItem = sender as System.Windows.Controls.MenuItem;
                if (menuItem == null) return;

                var contextMenu = menuItem.Parent as ContextMenu;
                if (contextMenu == null) return;

                // Get the text from the control that opened the context menu
                string textToCopy = null;

                if (contextMenu.PlacementTarget is System.Windows.Controls.TextBlock textBlock)
                {
                    textToCopy = textBlock.Text;
                }
                else if (contextMenu.PlacementTarget is System.Windows.Controls.ComboBox comboBox)
                {
                    textToCopy = comboBox.SelectedItem?.ToString();
                }

                if (!string.IsNullOrEmpty(textToCopy))
                {
                    System.Windows.Clipboard.SetText(textToCopy);
                    Log($"Successfully copied text to clipboard: {textToCopy}");
                }
                else
                {
                    Log("No text to copy");
                }
            }
            catch (Exception ex)
            {
                Log($"Error copying text to clipboard: {ex.Message}");
            }
        }

		/// <summary>
		/// Обработчик изменения тестов коллизий
		/// </summary>
		private void OnTestsChanged(object sender, EventArgs e)
		{
			try
			{
				// Сохраняем состояние галочек тестов перед очисткой
				var savedCheckedTestIds = new System.Collections.Generic.HashSet<Guid>(_checkedTestIds);
				
				// Очищаем кэши и состояние при изменении тестов
				ClearAllCachesAndState();
				
				// Перезагружаем данные
				LoadTests();
				
				// Восстанавливаем состояние галочек тестов
				_checkedTestIds.Clear();
				foreach (var testId in savedCheckedTestIds)
				{
					_checkedTestIds.Add(testId);
				}
				
				// Обновляем визуальное состояние чекбоксов
				RestoreTestCheckboxStates();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Ошибка при обработке изменения тестов: {ex.Message}");
			}
		}

        /// <summary>
        /// Добавляет коллизию в объединенный список при множественном выборе тестов
        /// </summary>
        private void AddClashToMergedList(Guid clashGuid, Guid testGuid)
        {
            try
            {
                Log($"AddClashToMergedList called: clashGuid={clashGuid}, testGuid={testGuid}");
                
                // Находим тест по GUID
                var allTests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList() ?? new System.Collections.Generic.List<ClashTest>();
                var test = allTests.FirstOrDefault(t => t.Guid == testGuid);
                if (test == null) 
                {
                    Log($"Test not found for GUID: {testGuid}");
                    return;
                }
                
                Log($"Found test: {test.DisplayName}");

                // Ищем коллизию в тесте
                var clash = FindResultByGuid(test, clashGuid);
                var group = FindGroupByGuid(test, clashGuid);
                
                Log($"Found clash: {clash != null}, group: {group != null}");
                
                if (clash != null || group != null)
                {
                    // Создаем элемент для добавления в список
                    var item = clash != null ? (object)clash : group;
                    var itemName = clash != null ? clash.DisplayName : group.DisplayName;
                    var status = GetStatusFromItem(item);
                    var assignedTo = clash != null ? (clash.AssignedTo ?? string.Empty).ToString() : (group.AssignedTo ?? string.Empty).ToString();
                    var level = clash != null ? GetCachedLevelFromItems(clash.CompositeItem1, clash.CompositeItem2, clash) : GetCachedLevelFromGroup(group);
                    var grid = clash != null ? GetCachedGridFromResult(clash) : GetCachedGridFromGroup(group);

                    var collisionItem = new CollisionListItem
                    {
                        Name = itemName,
                        Status = status,
                        AssignedTo = assignedTo,
                        Guid = clashGuid,
                        TestGuid = testGuid,
                        IsGroup = group != null,
                        Item = item
                    };

                    // Добавляем в текущий список коллизий
                    var currentItems = CollisionsList.ItemsSource as System.Collections.Generic.List<object>;
                    if (currentItems != null)
                    {
                        currentItems.Add(collisionItem);
                        CollisionsList.ItemsSource = null;
                        CollisionsList.ItemsSource = currentItems;
                        Log($"Added clash {itemName} to merged list from test {test.DisplayName}");
                    }
                    else
                    {
                        // Если список пустой или имеет другой тип, пересоздаем объединенный список
                        Log("Rebuilding merged list to include new clash");
                        TestsList_SelectionChanged(null, null);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error adding clash to merged list: {ex.Message}");
            }
        }

        /// <summary>
        /// Восстанавливает визуальное состояние чекбоксов тестов
        /// </summary>
        private void RestoreTestCheckboxStates()
		{
			try
			{
				foreach (var item in TestsList.Items)
				{
					var itemType = item.GetType();
					var guidProperty = itemType.GetProperty("Guid");
					if (guidProperty != null)
					{
						var testGuid = (Guid)guidProperty.GetValue(item);
						bool shouldBeChecked = _checkedTestIds.Contains(testGuid);
						SetCheckboxStateForListItem(TestsList, item, shouldBeChecked);
					}
				}
			}
			catch (Exception ex)
			{
				Log($"Ошибка при восстановлении состояния чекбоксов тестов: {ex.Message}");
			}
		}

		/// <summary>
		/// Очищает все кэши и состояние выбранных элементов
		/// </summary>
		private void ClearAllCachesAndState()
		{
			try
			{
				// Очищаем состояние выбранных элементов
				_checkedRowIds.Clear();
				_checkedTestIds.Clear();
				
				// Сбрасываем индексы последних кликов
				_lastTestClickIndex = -1;
				_lastCollisionClickIndex = -1;
				
				// Очищаем кэши
				_levelCache.Clear();
				_gridCache.Clear();
				
				// Очищаем состояние поиска
				_lastSearchQuery = string.Empty;
				
				// Очищаем состояние синхронизации
				_isSyncingFromPlugin = false;
				_lastDetectedClashGuid = Guid.Empty;
				
				// Очищаем списки в UI
				TestsList.ItemsSource = null;
				CollisionsList.ItemsSource = null;
				
				System.Diagnostics.Debug.WriteLine("Все кэши и состояние очищены");
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Ошибка при очистке кэшей: {ex.Message}");
			}
		}

		private string GetGridIntersectionInfo(ModelItem item1, ModelItem item2)
		{
			string grid1 = GetGridIntersectionFromItem(item1);
			string grid2 = GetGridIntersectionFromItem(item2);

			if (!string.IsNullOrEmpty(grid1) && !string.IsNullOrEmpty(grid2))
			{
				if (grid1 == grid2)
					return $"{grid1}";
				else
					return $"Grid Intersection: {grid1} x {grid2}";
			}
			else if (!string.IsNullOrEmpty(grid1))
				return $"{grid1}";
			else if (!string.IsNullOrEmpty(grid2))
				return $"{grid2}";
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
			
			// First priority: try GridHelper method using LevelSystem (most reliable)
			string gridHelperLevel = GridHelper.GetLevelForGroup(group);
			if (!string.IsNullOrEmpty(gridHelperLevel) && gridHelperLevel != "—")
			{
				return gridHelperLevel;
			}
			
			// Second priority: get level from the first result in the group
			var firstResult = GetAllResultsFromGroup(group).FirstOrDefault();
			if (firstResult != null)
			{
				return GetLevelFromItems(firstResult.CompositeItem1, firstResult.CompositeItem2, firstResult);
			}
			return "N/A";
		}

		private string GetGridIntersectionFromGroup(ClashResultGroup group)
		{
			if (group == null) return "N/A";
			
			// Get grid intersection from the first result in the group
			var firstResult = GetAllResultsFromGroup(group).FirstOrDefault();
			if (firstResult != null)
			{
				return FormatGridIntersectionDisplay(firstResult);
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

			// Second priority: get level from ModelItem properties (most reliable)
			string level1 = GetLevelFromItem(item1);
			string level2 = GetLevelFromItem(item2);

			// Try to match Clash Detective behavior - prefer the first valid level
			if (!string.IsNullOrEmpty(level1) && level1 != "N/A")
				return level1;
			else if (!string.IsNullOrEmpty(level2) && level2 != "N/A")
				return level2;

			// Third priority: try GridHelper method using elevation calculation (more accurate)
			if (clash != null)
			{
				string floorByElevation = GridHelper.GetFloorByElevation(clash);
				if (!string.IsNullOrEmpty(floorByElevation) && floorByElevation != "—")
				{
					return floorByElevation;
				}
			}

			// Fourth priority: try GridHelper method using GridLevel (fallback)
			if (clash != null)
			{
				string gridHelperLevel = GridHelper.GetLevelForClash(clash);
				if (!string.IsNullOrEmpty(gridHelperLevel) && gridHelperLevel != "—")
				{
					return gridHelperLevel;
				}
			}

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

            // First priority: try GridHelper method (most reliable)
            string gridHelperResult = GridHelper.GetGridIntersectionForClash(clash);
            if (!string.IsNullOrEmpty(gridHelperResult) && gridHelperResult != "—")
            {
                return $"{gridHelperResult}";
            }

            // Second priority: try GridHelper alternative method
            string gridHelperAltResult = GridHelper.GetGridIntersectionForClashAlternative(clash);
            if (!string.IsNullOrEmpty(gridHelperAltResult) && gridHelperAltResult != "—")
            {
                return $"{gridHelperAltResult}";
            }

            // Third priority: try to get grid intersection from ClashResult itself
            string clashGridInfo = GetGridIntersectionFromClashResult(clash);
            if (!string.IsNullOrEmpty(clashGridInfo) && clashGridInfo != "N/A")
            {
                return clashGridInfo;
            }

            // Fourth priority: use item-based grid intersection
            string gridInfo = GetGridIntersectionInfo(clash.CompositeItem1, clash.CompositeItem2);
            if (gridInfo != "N/A")
            {
                return gridInfo;
            }

            // Fifth priority: try alternative grid intersection method with larger tolerance
            var (altLevelName, altIntersection, altLine1, altLine2, altPosition) = GetClashGridInfoAlternative(clash, 10.0);
            if (altIntersection != "N/A" || altLine1 != "N/A" || altLine2 != "N/A")
            {
                var parts = new System.Collections.Generic.List<string>();
                if (altIntersection != "N/A") parts.Add($"{altIntersection}");
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
            if (intersection != "N/A") partsOrig.Add($"{intersection}");
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
                if (_isSyncingFromPlugin || _isUserScrolling) return;

                Guid currentClashGuid = GetCurrentClashDetectiveSelection();

                // Ничего не выбрано — сбрасываем pending/стабилизацию
                if (currentClashGuid == Guid.Empty)
                {
                    _pendingDetectedClashGuid = Guid.Empty;
                    _pendingDetectedStableTicks = 0;
                    return;
                }

                // Если GUID меняется — начинаем стабилизацию заново
                if (currentClashGuid != _pendingDetectedClashGuid)
                {
                    _pendingDetectedClashGuid = currentClashGuid;
                    _pendingDetectedStableTicks = 1;
                    return;
                }

                // Увеличиваем счетчик стабильных тиков
                _pendingDetectedStableTicks++;

                // Дебаунс: синхронизируем только если GUID стабилен минимум 2 тика и прошло достаточно времени с последней синхронизации
                if (_pendingDetectedStableTicks >= 2 &&
                    (DateTime.UtcNow - _lastClashDetectiveSyncUtc) >= _minClashDetectiveSyncInterval &&
                    _pendingDetectedClashGuid != _lastDetectedClashGuid)
                {
                    _lastDetectedClashGuid = _pendingDetectedClashGuid;
                    _lastClashDetectiveSyncUtc = DateTime.UtcNow;
                    _isSyncingFromClashDetective = true;
                    try
                    {
                        SyncSelectionFromClashDetective(_lastDetectedClashGuid);
                    }
                    finally
                    {
                        _isSyncingFromClashDetective = false;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("Error in ClashDetectiveMonitorTimer_Tick", ex);
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
                catch (Exception ex) { LogError("Error in GetCurrentClashDetectiveSelection fallback", ex); }

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
                    // Если пользователь работает с несколькими тестами — не переключаем тесты
                    bool testChanged = false;
                    if (!AreMultipleTestsChecked())
                    {
                        // Выбираем тест в плагине, если он еще не выбран
                        testChanged = EnsureTestSelected(testGuid.Value);
                    }

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
                        // Во время пользовательского скролла не вмешиваемся
                        if (!_isUserScrolling && (DateTime.UtcNow - _lastUserScrollUtc) > _suppressScrollIntoViewAfterUserScroll)
                        {
                            CollisionsList.ScrollIntoView(itemToSelect);
                        }

                        Log($"Successfully synced selection: {selectedGuid}");
                    }
                    else
                    {
                        // Если элемент не найден в текущем списке
                        if (AreMultipleTestsChecked())
                        {
                            // При множественном выборе тестов добавляем коллизию в объединенный список
                            Log($"Item not found in merged list; adding to merged list. Current items count: {CollisionsList.Items?.Count ?? 0}");
                            AddClashToMergedList(selectedGuid, testGuid.Value);
                            
                            // Ищем элемент в обновленном списке
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
                                CollisionsList.SelectedItem = itemToSelect;
                                if (!_isUserScrolling && (DateTime.UtcNow - _lastUserScrollUtc) > _suppressScrollIntoViewAfterUserScroll)
                                {
                                    CollisionsList.ScrollIntoView(itemToSelect);
                                }
                                Log($"Successfully added and selected item in merged list: {selectedGuid}");
                            }
                        }
                        else if (testChanged)
                        {
                            // Если тест сменился, мягко пересобираем список только для одного теста
                            Log("Item not found; rebuilding list for selected test once");
                            TestsList_SelectionChanged(null, null);

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
                                CollisionsList.SelectedItem = itemToSelect;
                                if (!_isUserScrolling && (DateTime.UtcNow - _lastUserScrollUtc) > _suppressScrollIntoViewAfterUserScroll)
                                {
                                    CollisionsList.ScrollIntoView(itemToSelect);
                                }
                                Log($"Successfully synced selection after rebuild: {selectedGuid}");
                            }
                            else
                            {
                                Log($"Item not found in plugin list even after rebuild: {selectedGuid}");
                            }
                        }
                        else
                        {
                            Log($"Item not found in plugin list after test selection: {selectedGuid}");
                        }
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

        // Возвращает true, если выбранный тест изменился
        private bool EnsureTestSelected(Guid testGuid)
        {
            try
            {
                if (AreMultipleTestsChecked())
                {
                    // Не меняем выбранный тест, если пользователь работает с несколькими тестами
                    return false;
                }
                // Проверяем, уже выбран ли нужный тест
                var current = TestsList.SelectedItem;
                if (current != null)
                {
                    var testProp = current.GetType().GetProperty("Test");
                    var curTest = testProp?.GetValue(current) as ClashTest;
                    if (curTest != null && curTest.Guid == testGuid)
                    {
                        return false; // тест уже выбран
                    }
                }

                SelectTestInPlugin(testGuid);
                // Перестроим список для выбранного теста
                TestsList_SelectionChanged(null, null);
                return true;
            }
            catch (Exception ex)
            {
                Log($"Error ensuring test selected: {ex.Message}");
                return true;
            }
        }

        private bool AreMultipleTestsChecked()
        {
            try
            {
            return _checkedTestIds != null && _checkedTestIds.Count > 1;
        }
        catch (Exception ex) { LogError("Error in AreMultipleTestsChecked", ex); return false; }
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
                            // Не скроллим список коллизий автоматически при пользовательском скролле
                            _lastUserScrollUtc = DateTime.UtcNow;
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
				/// Обновляет статус коллизии в Navisworks
				/// </summary>
				private void UpdateCollisionStatus(CollisionListItem item, string newStatus)
				{
					try
					{
						// Временно отключаем обработчик OnTestsChanged, чтобы избежать перезагрузки тестов
						var clashDoc = Application.ActiveDocument?.Clash as DocumentClash;
						if (clashDoc?.TestsData != null)
						{
							clashDoc.TestsData.Changed -= OnTestsChanged;
						}

						// Преобразуем строку статуса в ClashResultStatus
						ClashResultStatus statusEnum;
						switch (newStatus)
						{
							case "Новый":
								statusEnum = ClashResultStatus.New;
								break;
							case "Активный":
								statusEnum = ClashResultStatus.Active;
								break;
							case "Проанализирован":
								statusEnum = ClashResultStatus.Reviewed;
								break;
							case "Утвержден":
								statusEnum = ClashResultStatus.Approved;
								break;
							case "Исправлен":
								statusEnum = ClashResultStatus.Resolved;
								break;
							default:
								throw new ArgumentException($"Неизвестный статус: {newStatus}");
						}

						// Используем сохраненную ссылку на исходный объект
						if (item.Item is ClashResultGroup group)
						{
							// Для группы используем API для изменения статуса (как в MoveToConfirmedPlugin)
							// Приводим к IClashResult как в оригинальном коде
							IClashResult iGroup = group as IClashResult;
							_documentClash.TestsData.TestsEditResultStatus(group, statusEnum);
							Log($"Статус группы {group.DisplayName} изменен на {newStatus} через API");
						}
						else if (item.Item is ClashResult result)
						{
							// Для отдельных коллизий также приводим к IClashResult и используем API
							IClashResult iResult = result as IClashResult;
							_documentClash.TestsData.TestsEditResultStatus(result, statusEnum);
							Log($"Статус коллизии {result.DisplayName} изменен на {newStatus} через API");
						}
						else
						{
							throw new InvalidOperationException($"Неизвестный тип объекта: {item.Item?.GetType()?.Name}");
						}

						// Восстанавливаем обработчик OnTestsChanged
						if (clashDoc?.TestsData != null)
						{
							clashDoc.TestsData.Changed += OnTestsChanged;
						}
					}
					catch (Exception ex)
					{
						// Восстанавливаем обработчик в случае ошибки
						var clashDoc = Application.ActiveDocument?.Clash as DocumentClash;
						if (clashDoc?.TestsData != null)
						{
							clashDoc.TestsData.Changed += OnTestsChanged;
						}
						Log($"Ошибка при обновлении статуса в Navisworks: {ex.Message}");
						throw;
					}
				}
				private void StatusComboBox_Loaded(object sender, RoutedEventArgs e)
				{
					var comboBox = sender as ComboBox;
					if (comboBox?.Tag is CollisionListItem item)
					{
						// Привязка SelectedItem теперь работает автоматически через XAML
						// Этот метод оставлен для логирования и возможной дополнительной логики
						Log($"StatusComboBox_Loaded: ComboBox загружен для {item.Name} со статусом '{item.Status}'");
					}
				}

private void StatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    Log("=== StatusComboBox_SelectionChanged вызван ===");
    
    var comboBox = sender as ComboBox;
    Log($"ComboBox Text: '{comboBox?.Text}', SelectedItem: '{comboBox?.SelectedItem}'");
    Log($"Added items: [{string.Join(", ", e.AddedItems.Cast<object>().Select(x => x?.ToString() ?? "null"))}]");
    Log($"Removed items: [{string.Join(", ", e.RemovedItems.Cast<object>().Select(x => x?.ToString() ?? "null"))}]");
    Log($"Tag type: {comboBox?.Tag?.GetType().Name}");
    
    if (comboBox?.Tag is CollisionListItem item)
    {
        Log($"CollisionListItem: {item.Name}, текущий статус: {item.Status}");
        
        var newStatus = e.AddedItems.Count > 0 ? e.AddedItems[0]?.ToString() : null;
        var oldStatus = e.RemovedItems.Count > 0 ? e.RemovedItems[0]?.ToString() : null;
        Log($"Новый статус из AddedItems: '{newStatus}'");
        Log($"Старый статус из RemovedItems: '{oldStatus}'");
        
        if (newStatus != null)
        {
            Log($"Выбрано {CollisionsList.SelectedItems.Count} элементов в списке");
            
            // Если выделено несколько элементов, применяем статус ко всем выделенным
            if (CollisionsList.SelectedItems.Count > 1 && CollisionsList.SelectedItems.Contains(item))
            {
                Log($"Применяем статус '{newStatus}' ко всем {CollisionsList.SelectedItems.Count} выбранным элементам");
                
                try
                {
                    // Создаем копию выбранных элементов, чтобы избежать ошибки "Коллекция была изменена"
                    var selectedItemsCopy = CollisionsList.SelectedItems.Cast<CollisionListItem>().ToList();
                    
                    foreach (var selectedCollisionItem in selectedItemsCopy)
                    {
                        Log($"Обновляем статус для: {selectedCollisionItem.Name} (текущий: {selectedCollisionItem.Status})");
                        // Обновляем независимо от текущего значения, чтобы гарантировать применение к Navisworks
                        UpdateCollisionStatus(selectedCollisionItem, newStatus);
                        selectedCollisionItem.Status = newStatus;
                        Log($"Статус обновлен на: {newStatus}");
                    }
                    
                    // Временно сбрасываем флаги скролла для обновления UI
                    bool wasUserScrolling = _isUserScrolling;
                    bool wasSuppressUIUpdates = _suppressUIUpdates;
                    _isUserScrolling = false;
                    _suppressUIUpdates = false;
                    
                    // Обновляем UI для синхронизации с Clash Detective
                    RefreshCollisionsList();
                    Log("UI обновлен для всех выбранных элементов");
                    
                    // Восстанавливаем флаги скролла
                    _isUserScrolling = wasUserScrolling;
                    _suppressUIUpdates = wasSuppressUIUpdates;
                }
                catch (Exception ex)
                {
                    Log($"ОШИБКА при обновлении статуса множественных элементов: {ex.Message}");
                    MessageBox.Show($"Ошибка при изменении статуса: {ex.Message}");
                }
            }
            else
            {
                // Обычное обновление одного элемента
                // Сравниваем с RemovedItems, так как привязка уже могла установить item.Status в newStatus
                var statusChanged = oldStatus == null || !string.Equals(oldStatus, newStatus, StringComparison.Ordinal);
                Log($"Определено изменение статуса (по RemovedItems): {statusChanged}");

                if (statusChanged)
                {
                    Log($"Обновляем один элемент...");
                    try
                    {
                        Log($"Статус до изменения (из Navisworks): {GetStatusFromItem(item.Item)}");
                        UpdateCollisionStatus(item, newStatus);
                        Log($"Статус после изменения (из Navisworks): {GetStatusFromItem(item.Item)}");

                        // Синхронизируем модель (на случай если binding еще не выполнил запись)
                        item.Status = newStatus;
                        Log($"Статус в модели установлен: {item.Status}");

                        // Временно сбрасываем флаги скролла для обновления UI
                        bool wasUserScrolling = _isUserScrolling;
                        bool wasSuppressUIUpdates = _suppressUIUpdates;
                        _isUserScrolling = false;
                        _suppressUIUpdates = false;
                        
                        RefreshCollisionsList();
                        
                        // Восстанавливаем флаги скролла
                        _isUserScrolling = wasUserScrolling;
                        _suppressUIUpdates = wasSuppressUIUpdates;
                    }
                    catch (Exception ex)
                    {
                        Log($"ОШИБКА при обновлении статуса: {ex.Message}");
                        MessageBox.Show($"Ошибка при изменении статуса: {ex.Message}");
                    }
                }
                else
                {
                    Log("Статус не изменился по сути, обновление Navisworks не требуется");
                }
            }
        }
        else
        {
            Log("Новый статус равен null, ничего не делаем");
        }
    }
    else
    {
        Log($"ОШИБКА: Tag не является CollisionListItem: {comboBox?.Tag}");
    }
}

        private void OnCollisionsListScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            _lastUserScrollUtc = DateTime.UtcNow;
            _isUserScrolling = true;
            _suppressUIUpdates = true;
            _lastScrollEventUtc = DateTime.UtcNow;
            if (!(_scrollIdleTimer?.IsEnabled ?? false)) _scrollIdleTimer?.Start();
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }
		private string GetStatusFromItem(object item)
		{
			if (item is ClashResultGroup group)
				return group.Status.ToString();
			if (item is ClashResult result)
				return result.Status.ToString();
			return "Unknown";
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

            // Также временно подавляем авто-прокрутку, чтобы не мешать пользователю
            _lastUserScrollUtc = DateTime.UtcNow;
        }



    }
}
