using System.Globalization;

namespace PokeRedSync;

public sealed class SaveListForm : Form
{
    readonly string root;
    readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, MultiSelect = false, HideSelection = false };
    readonly TextBox detail = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };

    public SaveListForm(string root, Icon? icon)
    {
        this.root = root;
        Text = "PokeRedSync - セーブの一覧";
        Icon = icon;
        Font = SystemFonts.MessageBoxFont;
        AutoScaleMode = AutoScaleMode.Font;
        Size = new Size(900, 400);
        MinimumSize = new Size(500, 260);
        StartPosition = FormStartPosition.CenterScreen;
        foreach (string title in new[] { "端末", "主人公", "いる場所", "先頭のポケモン", "セーブした日時", "HotSync した日時", "届けるセーブ" }) list.Columns.Add(title);
        list.SelectedIndexChanged += (_, _) => { if (list.SelectedItems.Count > 0) detail.Text = ((SaveOverviewRow)list.SelectedItems[0].Tag!).Message; };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        var close = new Button { Text = "閉じる", AutoSize = true };
        var reload = new Button { Text = "再読み込み", AutoSize = true };
        close.Click += (_, _) => Close();
        reload.Click += (_, _) => Reload();
        buttons.Controls.Add(close); buttons.Controls.Add(reload);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(list, 0, 0); layout.Controls.Add(detail, 0, 1); layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);
        VisibleChanged += (_, _) => { if (Visible) Reload(); };
    }

    public void Reload()
    {
        string? selected = list.SelectedItems.Count == 0 ? null : ((SaveOverviewRow)list.SelectedItems[0].Tag!).Device;
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            var rows = SaveOverview.Build(root);
            foreach (var row in rows)
            {
                string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
                string Date(DateTime? value) => value?.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture) ?? "不明";
                var item = new ListViewItem(new[] { Value(row.Device), Value(row.Player), Value(row.Location), Value(row.Pokemon), Date(row.SavedAt), Date(row.SyncedAt), row.Status switch { DeliveryStatus.Ready => "用意済み", DeliveryStatus.None => "届け物なし", _ => "確認が必要" } }) { Tag = row };
                if (row.Status == DeliveryStatus.Error) item.ForeColor = Color.DarkRed;
                list.Items.Add(item);
            }
            foreach (ColumnHeader column in list.Columns) { column.AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize); int width = column.Width; column.AutoResize(ColumnHeaderAutoResizeStyle.ColumnContent); column.Width = Math.Max(width, column.Width); }
            if (list.Items.Count == 0) detail.Text = $"HotSync のフォルダに端末が見つかりません{Environment.NewLine}探した場所：{root}";
            else
            {
                var item = list.Items.Cast<ListViewItem>().FirstOrDefault(x => string.Equals(((SaveOverviewRow)x.Tag!).Device, selected, StringComparison.OrdinalIgnoreCase)) ?? list.Items[0];
                item.Selected = true; item.Focused = true; detail.Text = ((SaveOverviewRow)item.Tag!).Message;
            }
        }
        catch (Exception e) { list.Items.Clear(); detail.Text = $"読み込めませんでした：{e.Message}"; }
        finally { list.EndUpdate(); }
    }
}
