using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SqlPilot.Core.Database;

namespace SqlPilot.UI.Controls
{
    public partial class SearchControl : UserControl
    {
        public event Action<DatabaseObject, string> ActionRequested;

        public SearchControl()
        {
            InitializeComponent();
            PreviewKeyDown += OnPreviewKeyDown;
        }

        public void FocusSearchBox()
        {
            SearchTextBox.Focus();
            SearchTextBox.SelectAll();
        }

        private void FireAction(string action)
        {
            var obj = GetSelectedObject();
            if (obj != null)
                ActionRequested?.Invoke(obj, action);
        }

        private DatabaseObject GetSelectedObject()
        {
            return (DataContext as ViewModels.SearchViewModel)?.SelectedResult?.DatabaseObject;
        }

        private bool IsSearchBoxFocused => SearchTextBox.IsFocused;

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not ViewModels.SearchViewModel vm) return;

            switch (e.Key)
            {
                case Key.Tab when IsSearchBoxFocused && vm.Results.Count > 0:
                case Key.Down when IsSearchBoxFocused && vm.Results.Count > 0:
                    ResultsListBox.Focus();
                    if (vm.SelectedResult == null && vm.Results.Count > 0)
                        vm.SelectedResult = vm.Results[0];
                    ResultsListBox.ScrollIntoView(vm.SelectedResult);
                    (ResultsListBox.ItemContainerGenerator.ContainerFromItem(vm.SelectedResult) as ListBoxItem)?.Focus();
                    e.Handled = true;
                    break;

                case Key.Up when IsSearchBoxFocused:
                    e.Handled = true;
                    break;

                case Key.Enter when IsSearchBoxFocused && vm.Results.Count > 0:
                    // Enter in search box = execute default on first result
                    if (vm.SelectedResult == null)
                        vm.SelectedResult = vm.Results[0];
                    FireAction(SearchActions.Default);
                    e.Handled = true;
                    break;

                case Key.Escape:
                    if (!IsSearchBoxFocused)
                        FocusSearchBox();
                    else
                        SearchTextBox.Clear();
                    e.Handled = true;
                    break;

                case Key.Tab when !IsSearchBoxFocused:
                    FocusSearchBox();
                    e.Handled = true;
                    break;
            }
        }

        private void ResultsListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not ViewModels.SearchViewModel vm) return;

            switch (e.Key)
            {
                case Key.Escape:
                    FocusSearchBox();
                    e.Handled = true;
                    break;
                case Key.Tab:
                    FocusSearchBox();
                    e.Handled = true;
                    break;
                case Key.Up when vm.Results.Count > 0 && vm.Results.IndexOf(vm.SelectedResult) == 0:
                    FocusSearchBox();
                    e.Handled = true;
                    break;
                case Key.Space:
                    ShowContextMenuAtSelection();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    FireAction(SearchActions.Default);
                    e.Handled = true;
                    break;
                case Key.Right:
                    // hunting-dog parity: Right = type-dependent secondary action
                    // (Edit Data for tables, Execute for procs/functions)
                    FireAction(SearchActions.Secondary);
                    e.Handled = true;
                    break;
            }
        }

        private void ShowContextMenuAtSelection()
        {
            if (DataContext is not ViewModels.SearchViewModel vm) return;

            BuildContextMenu();

            var container = ResultsListBox.ItemContainerGenerator
                .ContainerFromItem(vm.SelectedResult) as ListBoxItem;
            if (container != null)
            {
                ResultsListBox.ContextMenu.PlacementTarget = container;
                ResultsListBox.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            }
            ResultsListBox.ContextMenu.IsOpen = true;
        }

        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            BuildContextMenu();
        }

        private void BuildContextMenu()
        {
            var obj = GetSelectedObject();
            if (obj == null) return;

            var menu = ResultsListBox.ContextMenu;
            menu.Items.Clear();

            switch (obj.ObjectType)
            {
                case DatabaseObjectType.Table:
                    menu.Items.Add(MakeItem("_Select Data", SearchActions.SelectTop, "Enter", true));
                    menu.Items.Add(MakeItem("_Edit Data", SearchActions.EditData, "Right", false));
                    menu.Items.Add(MakeItem("_Design Table", SearchActions.DesignTable, null, false));
                    menu.Items.Add(MakeItem("Script _Table", SearchActions.ScriptCreate, null, false));
                    break;

                case DatabaseObjectType.View:
                    menu.Items.Add(MakeItem("_Select Data", SearchActions.SelectTop, "Enter", true));
                    menu.Items.Add(MakeItem("_Modify View", SearchActions.ScriptAlter, null, false));
                    break;

                case DatabaseObjectType.StoredProcedure:
                    menu.Items.Add(MakeItem("_Modify", SearchActions.ScriptAlter, "Enter", true));
                    menu.Items.Add(MakeItem("_Execute", SearchActions.Execute, "Right", false));
                    break;

                case DatabaseObjectType.ScalarFunction:
                case DatabaseObjectType.TableValuedFunction:
                    menu.Items.Add(MakeItem("_Modify", SearchActions.ScriptAlter, "Enter", true));
                    menu.Items.Add(MakeItem("_Execute", SearchActions.Execute, "Right", false));
                    menu.Items.Add(MakeItem("_View Definition", SearchActions.ScriptCreate, null, false));
                    break;

                default:
                    menu.Items.Add(MakeItem("_View Definition", SearchActions.ScriptCreate, "Enter", true));
                    break;
            }

            AddFavoriteFooter(menu);
        }

        private void AddFavoriteFooter(ContextMenu menu)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(MakeItem("Toggle _Favorite", SearchActions.ToggleFavorite, null, false));
        }

        private MenuItem MakeItem(string header, string action, string gesture, bool bold)
        {
            var item = new MenuItem
            {
                Header = header,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                InputGestureText = gesture ?? ""
            };

            if (action == SearchActions.ToggleFavorite)
            {
                item.Click += (s, e) =>
                {
                    if (DataContext is ViewModels.SearchViewModel vm)
                        vm.ToggleFavoriteCommand.Execute(null);
                };
            }
            else
            {
                item.Click += (s, e) => FireAction(action);
            }

            return item;
        }

        private void ResultsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e) => FireAction("Default");
        private void OnControlLoaded(object sender, RoutedEventArgs e) => FocusSearchBox();
    }
}
