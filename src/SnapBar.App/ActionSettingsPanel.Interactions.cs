using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SnapBar.Core;

namespace SnapBar.App;

internal sealed partial class ActionSettingsPanel
{
    private const string DragFormat = "SnapBar.ActionSettings.Move";
    private sealed record ActionDragPayload(ActionSettingsPanel Owner, string Id);
    private Action? clearDropFeedback;
    private long lastDragScroll;

    private void ScrollDuringDrag(object sender, DragEventArgs e)
    {
        if (!e.Handled) e.Effects = DragDropEffects.None;
        if (e.Data.GetData(DragFormat) is not ActionDragPayload payload || payload.Owner != this
            || !string.IsNullOrWhiteSpace(filter.Text)) return;
        var now = Environment.TickCount64;
        if (now - lastDragScroll < 60) return;
        var y = e.GetPosition(listScroll).Y;
        if (y < 28) listScroll.ScrollToVerticalOffset(listScroll.VerticalOffset - 18);
        else if (y > listScroll.ActualHeight - 28) listScroll.ScrollToVerticalOffset(listScroll.VerticalOffset + 18);
        lastDragScroll = now;
    }

    private string? DropSource(DragEventArgs e, ActionPlacement placement, string? targetId = null)
    {
        if (!e.AllowedEffects.HasFlag(DragDropEffects.Move)
            || e.Data.GetData(DragFormat) is not ActionDragPayload payload || payload.Owner != this
            || payload.Id == targetId) return null;
        var entry = settings.Layout.SingleOrDefault(a => a.Id == payload.Id);
        if (entry == null || (entry.Placement == placement && placement == ActionPlacement.More)) return null;
        try { ActionCatalog.SetPlacement(settings, payload.Id, placement); return payload.Id; }
        catch (ArgumentException error) { status.Text = ValidationMessages.Describe(error, language); return null; }
    }

    private void ChangePlacement(string id, ActionPlacement placement) => MoveAction(id, placement);

    // Build the complete candidate before committing, so failed capacity checks never save a partial move.
    internal bool MoveAction(string id, ActionPlacement placement, string? targetId = null, bool after = false)
    {
        try
        {
            var original = settings.Layout.Single(a => a.Id == id);
            var candidate = ActionCatalog.SetPlacement(settings, id, placement);
            if (placement == ActionPlacement.Bar)
            {
                var pinned = candidate.Layout.Where(a => a.Placement == ActionPlacement.Bar).ToList();
                var from = pinned.FindIndex(a => a.Id == id);
                var to = targetId == null ? pinned.Count : pinned.FindIndex(a => a.Id == targetId);
                if (to < 0 || targetId == id) return false;
                if (targetId != null && after) to++;
                if (from < to) to--;
                candidate = ActionCatalog.Move(candidate, id, to - from);
            }
            if (settings.Layout.SequenceEqual(candidate.Layout)) return false;
            var item = ActionCatalog.GetItems(settings).Single(a => a.Id == id);
            return Commit(candidate, original.Placement == placement
                ? Text("SettingsFeedback_Reordered", UiNames.Action(item, language))
                : Text("SettingsFeedback_Moved", UiNames.Action(item, language),
                    Text(placement == ActionPlacement.Bar ? "ActionSettingsPanel_040" : "ActionSettingsPanel_041")));
        }
        catch (ArgumentException error) { status.Text = ValidationMessages.Describe(error, language); return false; }
    }

    private void EnableRowDrop(Grid row, string id, ActionPlacement placement)
    {
        var marker = new Border { BorderBrush = ActionUi.Accent, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(marker, "ActionDropMarker_" + id);
        Grid.SetColumnSpan(marker, 7); row.Children.Add(marker);
        row.AllowDrop = true;
        row.DragOver += (_, e) =>
        {
            clearDropFeedback?.Invoke();
            var valid = DropSource(e, placement, id) != null;
            e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;
            if (valid)
            {
                var top = e.GetPosition(row).Y < row.ActualHeight / 2;
                marker.BorderThickness = placement == ActionPlacement.More ? new(2)
                    : top ? new(0, 2, 0, 0) : new(0, 0, 0, 2);
                marker.Visibility = Visibility.Visible;
                clearDropFeedback = () => marker.Visibility = Visibility.Collapsed;
            }
            e.Handled = true;
        };
        row.DragLeave += (_, _) => marker.Visibility = Visibility.Collapsed;
        row.Drop += (_, e) =>
        {
            marker.Visibility = Visibility.Collapsed;
            var source = DropSource(e, placement, id);
            var after = e.GetPosition(row).Y >= row.ActualHeight / 2;
            e.Effects = source != null && MoveAction(source, placement, placement == ActionPlacement.Bar ? id : null, after)
                ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
    }

    private void EnableGroupDrop(FrameworkElement target, ActionPlacement placement)
    {
        target.AllowDrop = true;
        void Highlight(bool active)
        {
            var brush = active ? ActionUi.Pale : Brushes.Transparent;
            if (target is Panel panel) panel.Background = brush;
            if (target is TextBlock text) text.Background = brush;
        }
        Highlight(false);
        target.DragOver += (_, e) =>
        {
            clearDropFeedback?.Invoke();
            var valid = DropSource(e, placement) != null;
            e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;
            Highlight(valid);
            clearDropFeedback = () => Highlight(false);
            e.Handled = true;
        };
        target.DragLeave += (_, _) => Highlight(false);
        target.Drop += (_, e) =>
        {
            Highlight(false);
            var source = DropSource(e, placement);
            e.Effects = source != null && MoveAction(source, placement) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
    }
}
