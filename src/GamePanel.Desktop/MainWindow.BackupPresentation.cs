using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GamePanel.Core;

namespace GamePanel.Desktop;

internal sealed partial class MainWindow
{
    private void RenderBackupPoints()
    {
        var body = new StackPanel(); var header = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        header.Children.Add(Text("I tuoi punti di ripristino", 21, Ink, FontWeights.SemiBold));
        var count = DesignSystem.Chip($"{_snapshots.Count} BACKUP", Accent); count.HorizontalAlignment = HorizontalAlignment.Right; header.Children.Add(count); body.Children.Add(header);
        if (_snapshots.Count == 0) body.Children.Add(Text("Nessun backup caricato per questa copia. Crea un punto di ripristino oppure carica i backup del percorso selezionato.", 12, Muted));
        foreach (var snapshot in _snapshots)
        {
            var row = new Grid { Margin = new Thickness(0, 14, 0, 14) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var symbol = DesignSystem.Icon("backup", Accent, 25); symbol.Margin = new Thickness(0, 0, 15, 0); symbol.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(symbol);
            var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            copy.Children.Add(Text($"{snapshot.CreatedAt.LocalDateTime:dd MMM yyyy · HH:mm}", 14, Ink, FontWeights.SemiBold));
            var game = _library.Games.FirstOrDefault(g => string.Equals(g.ExecutablePath, _backupForPath, StringComparison.OrdinalIgnoreCase));
            var path = _backupForPath ?? "Percorso non disponibile";
            copy.Children.Add(Text((game?.Title ?? System.IO.Path.GetFileNameWithoutExtension(path)) + $" · {snapshot.FileCount} file · {snapshot.Bytes / (1024d * 1024):F1} MB", 11, Muted));
            var pathLabel = Text(path, 10, Muted); pathLabel.TextWrapping = TextWrapping.NoWrap; pathLabel.TextTrimming = TextTrimming.CharacterEllipsis; pathLabel.ToolTip = path; copy.Children.Add(pathLabel);
            Grid.SetColumn(copy, 1); row.Children.Add(copy);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var inspect = Button("Esamina", async (_, _) => { _selectedBackup = snapshot.Id; _restorePlan = null; await PreviewBackupAsync(); });
            SmallAction(inspect, "Esamina", "search"); inspect.IsEnabled = !_busy;
            System.Windows.Automation.AutomationProperties.SetAutomationId(inspect, snapshot.Id == _selectedBackup ? "VerifyBackup" : "InspectBackup-" + snapshot.Id); actions.Children.Add(inspect);
            var delete = Button("Elimina", async (_, _) => await DeleteBackupAsync(snapshot)); SmallAction(delete, "Elimina", "trash"); delete.IsEnabled = !_busy;
            System.Windows.Automation.AutomationProperties.SetAutomationId(delete, "DeleteBackup-" + snapshot.Id); actions.Children.Add(delete);
            Grid.SetColumn(actions, 2); row.Children.Add(actions); body.Children.Add(row);
            body.Children.Add(new Border { Height = 1, Background = DesignSystem.Line });
        }
        // Keep the existing accessible selector as a compact alternate selection route.
        if (_snapshots.Count > 1)
        {
            var chooser = new ComboBox { ItemsSource = _snapshots.Select(s => new BackupChoice(s.Id, $"{s.CreatedAt.LocalDateTime:dd MMM yyyy · HH:mm} · {s.FileCount} file")).ToArray(),
                DisplayMemberPath = nameof(BackupChoice.Label), SelectedValuePath = nameof(BackupChoice.Id), SelectedValue = _selectedBackup, IsEnabled = !_busy, Margin = new Thickness(0, 18, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(chooser, "BackupSnapshotPicker");
            chooser.SelectionChanged += (_, _) => { if (chooser.SelectedValue is string id && id != _selectedBackup) { _selectedBackup = id; _restorePlan = null; Render(); } };
            body.Children.Add(chooser);
        }
        var panel = AssetPanel(body, 22); panel.Margin = new Thickness(0, 0, 0, 26);
        System.Windows.Automation.AutomationProperties.SetAutomationId(panel, "BackupPoints"); _content.Children.Add(panel);
    }
    private async Task DeleteBackupAsync(BackupSummary snapshot)
    {
        if (_busy || _backupForPath is not { } path) return;
        if (MessageBox.Show(this, $"Eliminare il backup del {snapshot.CreatedAt.LocalDateTime:dd MMM yyyy · HH:mm}?\n\nViene eliminata soltanto questa copia di ripristino. I file del gioco restano conservati. L'operazione non è annullabile.", "Elimina punto di ripristino", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await RunBackupTaskAsync((_, _) => { _backups.Delete(path, snapshot.Id); return _backups.List(path); }, snapshots =>
        { _snapshots = snapshots; if (_selectedBackup == snapshot.Id) _selectedBackup = snapshots.FirstOrDefault()?.Id; _restorePlan = null; _backupMessage = "Punto di ripristino eliminato. I file del gioco sono conservati."; }, "Eliminazione del backup selezionato");
    }
}
