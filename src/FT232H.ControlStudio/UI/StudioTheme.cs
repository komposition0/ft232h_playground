namespace FT232H.ControlStudio.UI;

/// <summary>Dark engineering theme colors and control styling helpers.</summary>
public static class StudioTheme
{
    public static readonly Color Background = Color.FromArgb(32, 34, 37);
    public static readonly Color Surface = Color.FromArgb(44, 47, 51);
    public static readonly Color SurfaceLight = Color.FromArgb(54, 57, 63);
    public static readonly Color Border = Color.FromArgb(70, 74, 80);
    public static readonly Color TextPrimary = Color.FromArgb(220, 221, 222);
    public static readonly Color TextSecondary = Color.FromArgb(140, 145, 150);
    public static readonly Color Accent = Color.FromArgb(0, 150, 190);
    public static readonly Color Ok = Color.FromArgb(80, 200, 120);
    public static readonly Color Warning = Color.FromArgb(230, 190, 60);
    public static readonly Color Error = Color.FromArgb(230, 90, 90);

    public static void ApplyDark(Control root)
    {
        root.BackColor = Background;
        root.ForeColor = TextPrimary;
        foreach (Control child in root.Controls)
        {
            switch (child)
            {
                case TabControl tabs:
                    tabs.BackColor = Surface;
                    tabs.ForeColor = TextPrimary;
                    break;
                case TabPage page:
                    page.BackColor = Background;
                    page.ForeColor = TextPrimary;
                    break;
                case DataGridView grid:
                    grid.BackgroundColor = Surface;
                    grid.GridColor = Border;
                    grid.ForeColor = TextPrimary;
                    grid.DefaultCellStyle.BackColor = SurfaceLight;
                    grid.DefaultCellStyle.ForeColor = TextPrimary;
                    grid.ColumnHeadersDefaultCellStyle.BackColor = Surface;
                    grid.ColumnHeadersDefaultCellStyle.ForeColor = TextSecondary;
                    grid.RowHeadersDefaultCellStyle.BackColor = Surface;
                    grid.EnableHeadersVisualStyles = false;
                    break;
                case Button button:
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = Border;
                    button.BackColor = SurfaceLight;
                    button.ForeColor = TextPrimary;
                    break;
                case TextBox or NumericUpDown or ComboBox:
                    child.BackColor = SurfaceLight;
                    child.ForeColor = TextPrimary;
                    break;
                case Label label:
                    label.ForeColor = TextPrimary;
                    break;
                case PropertyGrid pg:
                    pg.BackColor = Surface;
                    pg.ViewBackColor = SurfaceLight;
                    pg.ViewForeColor = TextPrimary;
                    pg.HelpBackColor = Surface;
                    pg.HelpForeColor = TextSecondary;
                    pg.LineColor = Border;
                    break;
                case SplitContainer split:
                    split.BackColor = Border;
                    break;
            }

            if (child.HasChildren)
                ApplyDark(child);
        }
    }

    public static void StyleStatusButton(Button button, Color accent)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = accent;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(
            Math.Min(255, accent.R / 2 + 30), Math.Min(255, accent.G / 2 + 30), Math.Min(255, accent.B / 2 + 30));
    }
}