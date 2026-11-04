using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using PlaylistMaker.Services;

namespace PlaylistMaker.Views
{
    /// <summary>One playlist column: a stable key for persistence plus the GridView column it drives.</summary>
    public sealed class PlaylistColumnDefinition
    {
        public PlaylistColumnDefinition(string key, string header, GridViewColumn column, double defaultWidth)
        {
            Key = key;
            Header = header;
            Column = column;
            DefaultWidth = defaultWidth;
            PreferredWidth = defaultWidth;
        }

        public string Key { get; }

        public string Header { get; }

        public GridViewColumn Column { get; }

        public double DefaultWidth { get; }

        /// <summary>Width to restore when the column is shown again.</summary>
        public double PreferredWidth { get; set; }

        public bool IsVisible { get; set; } = true;
    }

    /// <summary>
    /// Keeps the GridView columns in the order the user dragged them into, remembers widths and
    /// hides columns by setting their width to zero.
    /// Hiding this way (instead of removing the column) keeps <see cref="GridView.Columns"/> complete,
    /// so the drag-reordered order can always be read back and saved - including for hidden columns.
    /// </summary>
    public sealed class PlaylistColumnSet
    {
        private const double MinimumUsefulWidth = 20.0;

        private readonly GridView gridView;
        private readonly List<PlaylistColumnDefinition> columns = new List<PlaylistColumnDefinition>();

        public PlaylistColumnSet(GridView gridView, IEnumerable<PlaylistColumnDefinition> definitions)
        {
            this.gridView = gridView;
            columns.AddRange(definitions);
        }

        public IReadOnlyList<PlaylistColumnDefinition> Columns => columns;

        /// <summary>True while the saved layout is being applied, so callers can skip reacting to the moves.</summary>
        public bool IsApplyingLayout { get; private set; }

        public void ApplySavedLayout(UserSettings settings)
        {
            List<PlaylistColumnDefinition> ordered = new List<PlaylistColumnDefinition>();
            foreach (ColumnSetting setting in settings.Columns)
            {
                PlaylistColumnDefinition? match = columns.FirstOrDefault(
                    candidate => string.Equals(candidate.Key, setting.Key, StringComparison.OrdinalIgnoreCase));
                if (match != null && !ordered.Contains(match))
                {
                    ordered.Add(match);
                }
            }
            foreach (PlaylistColumnDefinition definition in columns)
            {
                if (!ordered.Contains(definition))
                {
                    ordered.Add(definition);
                }
            }

            columns.Clear();
            columns.AddRange(ordered);

            foreach (ColumnSetting setting in settings.Columns)
            {
                PlaylistColumnDefinition? match = columns.FirstOrDefault(
                    candidate => string.Equals(candidate.Key, setting.Key, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    continue;
                }
                if (setting.Width > MinimumUsefulWidth)
                {
                    match.PreferredWidth = setting.Width;
                }
                match.IsVisible = setting.Visible;
            }

            // the first ever launch has every column visible, but a corrupted file must never hide them all
            if (!columns.Any(column => column.IsVisible))
            {
                foreach (PlaylistColumnDefinition definition in columns)
                {
                    definition.IsVisible = true;
                }
            }

            IsApplyingLayout = true;
            try
            {
                for (int index = 0; index < columns.Count; index++)
                {
                    GridViewColumn column = columns[index].Column;
                    int currentIndex = gridView.Columns.IndexOf(column);
                    if (currentIndex >= 0 && currentIndex != index)
                    {
                        gridView.Columns.Move(currentIndex, index);
                    }
                }
            }
            finally
            {
                IsApplyingLayout = false;
            }

            ApplyWidths();
        }

        /// <summary>Returns false when the toggle was refused because it would hide the last visible column.</summary>
        public bool ToggleVisibility(PlaylistColumnDefinition definition)
        {
            if (definition.IsVisible && columns.Count(column => column.IsVisible) <= 1)
            {
                return false;
            }

            definition.IsVisible = !definition.IsVisible;
            ApplyWidths();
            return true;
        }

        public void ShowAllColumns()
        {
            foreach (PlaylistColumnDefinition definition in columns)
            {
                definition.IsVisible = true;
            }
            ApplyWidths();
        }

        /// <summary>Reads the live order/widths out of the GridView back into the settings object.</summary>
        public void SaveInto(UserSettings settings)
        {
            List<ColumnSetting> saved = new List<ColumnSetting>();

            // gridView.Columns is in display order, so it is the authority for ordering
            for (int index = 0; index < gridView.Columns.Count; index++)
            {
                GridViewColumn column = gridView.Columns[index];
                PlaylistColumnDefinition? definition = columns.FirstOrDefault(
                    candidate => ReferenceEquals(candidate.Column, column));
                if (definition == null)
                {
                    continue;
                }

                if (definition.IsVisible)
                {
                    double width = column.Width;
                    if (double.IsNaN(width) || width <= MinimumUsefulWidth)
                    {
                        width = column.ActualWidth;
                    }
                    if (width > MinimumUsefulWidth)
                    {
                        definition.PreferredWidth = width;
                    }
                }

                saved.Add(new ColumnSetting
                {
                    Key = definition.Key,
                    Visible = definition.IsVisible,
                    Width = definition.PreferredWidth
                });
            }

            settings.Columns = saved;
        }

        private void ApplyWidths()
        {
            foreach (PlaylistColumnDefinition definition in columns)
            {
                double width = definition.PreferredWidth > MinimumUsefulWidth
                    ? definition.PreferredWidth
                    : definition.DefaultWidth;
                definition.Column.Width = definition.IsVisible ? width : 0.0;
            }
        }
    }
}
