using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace CadDetailerAi
{
    internal sealed class ColumnEditorWindow : Window
    {
        private readonly ObservableCollection<ColumnData> _columns;
        private readonly DataGrid _grid;

        public ColumnEditorWindow(IEnumerable<ColumnData> columns)
        {
            Title = "Column Schedule Data";
            Width = 1050;
            Height = 520;
            MinWidth = 720;
            MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _columns = new ObservableCollection<ColumnData>(columns);

            DockPanel root = new DockPanel { Margin = new Thickness(10) };
            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0)
            };
            DockPanel.SetDock(buttons, Dock.Bottom);

            Button load = new Button { Content = "Load columns.json", MinWidth = 120, Margin = new Thickness(4) };
            load.Click += LoadClick;
            Button save = new Button { Content = "Save columns.json", MinWidth = 120, Margin = new Thickness(4) };
            save.Click += SaveClick;
            Button close = new Button { Content = "Done", MinWidth = 90, Margin = new Thickness(4) };
            close.Click += delegate { DialogResult = true; };
            buttons.Children.Add(load);
            buttons.Children.Add(save);
            buttons.Children.Add(close);
            root.Children.Add(buttons);

            _grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = true,
                CanUserDeleteRows = true,
                IsReadOnly = false,
                ItemsSource = _columns,
                SelectionUnit = DataGridSelectionUnit.CellOrRowHeader
            };
            AddColumn("Mark", "Mark");
            AddColumn("Width", "Width");
            AddColumn("Depth", "Depth");
            AddColumn("Grid", "Grid");
            AddColumn("Main bars", "MainBars");
            AddColumn("Stirrups", "Stirrups");
            AddColumn("Cover", "Cover");
            AddColumn("Levels", "Levels");
            root.Children.Add(_grid);
            Content = root;
        }

        public List<ColumnData> GetColumns()
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);
            return new List<ColumnData>(_columns);
        }

        private void AddColumn(string label, string property)
        {
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = label,
                Binding = new Binding(property) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                MinWidth = 90
            });
        }

        private void SaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                ColumnsStore.Save(GetColumns());
                MessageBox.Show(this, "Column data saved to:\n" + Settings.ColumnsPath,
                    "Columns", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                ColumnsStore.Log("Save failed: " + ex);
                MessageBox.Show(this, ex.Message, "Could not save column data",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadClick(object sender, RoutedEventArgs e)
        {
            try
            {
                List<ColumnData> loaded = ColumnsStore.Load();
                _columns.Clear();
                foreach (ColumnData column in loaded)
                    _columns.Add(column);
            }
            catch (Exception ex)
            {
                ColumnsStore.Log("Load failed: " + ex);
                MessageBox.Show(this, ex.Message, "Could not load column data",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
