using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using APS.Controls.Theme;

namespace APS.SystemControl.Forms;

public partial class frmEditor : Form
{
    private readonly string[] keywords = { "begin", "end", "loop", "endloop", "var", "func", "andfunc" };
    private readonly string[] functions = { "Valve", "MFC", "Pump", "Stage", "Add", "delay" };
    private readonly string[] states = { "on", "off", "open", "close" };


    private string fileName = string.Empty;
    private BufferedRichTextBox text;
    private int previousLineCount = 0;
    private frmFindReplace? findReplaceDialog;
    private HintManager _hints = null!;

    public frmEditor()
    {
        InitializeComponent();
        APS.SystemControl.Core.UIScaling.ApplyToForm(this);
        // Designer sets 32 raw px; WinForms does not DPI-scale ImageScalingSize (the two
        // resource icons are 32 px bitmaps, so this stretches them rather than shrinking).
        toolStrip1.ImageScalingSize = new Size(DpiScale.Px(32), DpiScale.Px(32));
        text = new BufferedRichTextBox();
        tableLayout.Controls.Add(text, 1, 0);
        text.Dock = DockStyle.Fill;

        text.SelectionChanged += Text_SelectionChanged;
        text.TextChanged += Text_TextChanged;
        text.VScroll += Text_VScroll;
        panelLineNumbers.Paint += PanelLineNumbers_Paint;

        previousLineCount = text.Lines.Length;

        SetFontSize(12);

        DarkModeHelper.FixControls(this);

        // Dispose via FormClosed: Dispose(bool) is auto-generated in Designer.cs.
        _hints = new HintManager(this);
        _hints.Set(text, "Recipe script editor — keywords and numbers are syntax-highlighted. Use the Find/Replace toolbar button to search.");
        FormClosed += (_, _) => _hints?.Dispose();
    }

    private void SetFontSize(float size)
    {
        text.Font = new Font(text.Font.FontFamily, size);
    }

    private void Text_TextChanged(object? sender, EventArgs e)
    {
        HighlightLine(text);

        int currentLineCount = text.Lines.Length;
        if (currentLineCount != previousLineCount)
        {
            panelLineNumbers.Invalidate();
            previousLineCount = currentLineCount;
        }
    }

    private void Text_VScroll(object? sender, EventArgs e)
    {
        panelLineNumbers.Invalidate();
    }

    private void Text_SelectionChanged(object? sender, EventArgs e)
    {
        UpdateCaretPosition();
        panelLineNumbers.Invalidate();
    }

    private void UpdateCaretPosition()
    {
        int line = text.GetLineFromCharIndex(text.SelectionStart) + 1;
        int column = text.SelectionStart - text.GetFirstCharIndexOfCurrentLine() + 1;
        toolStripStatusLabelCaretPosition.Text = $"Line: {line}, Column: {column}";
    }

    private void PanelLineNumbers_Paint(object? sender, PaintEventArgs e)
    {
        DrawLineNumbers(e.Graphics);
    }

    private void DrawLineNumbers(Graphics g)
    {
        g.Clear(panelLineNumbers.BackColor);

        int firstLineIndex = text.GetCharIndexFromPosition(new Point(0, 0));
        int firstLine = text.GetLineFromCharIndex(firstLineIndex);
        int lastLineIndex = text.GetCharIndexFromPosition(new Point(0, text.ClientSize.Height));
        int lastLine = text.GetLineFromCharIndex(lastLineIndex);

        int lineHeight = text.GetPositionFromCharIndex(text.GetFirstCharIndexFromLine(1)).Y - text.GetPositionFromCharIndex(text.GetFirstCharIndexFromLine(0)).Y;
        if (lineHeight == 0) lineHeight = text.Font.Height;

        int currentLineIndex = text.GetLineFromCharIndex(text.SelectionStart);

        bool dark = VacuumTheme.IsDark;
        using var highlightBrush = new SolidBrush(dark ? Color.FromArgb(50, 50, 60) : Color.LightGray);
        using var textBrush = new SolidBrush(dark ? Color.FromArgb(180, 180, 190) : Color.Black);

        for (int i = firstLine; i <= lastLine; i++)
        {
            int y = (i - firstLine) * lineHeight;

            if (i == currentLineIndex)
            {
                g.FillRectangle(highlightBrush, panelLineNumbers.ClientRectangle.Left, y, panelLineNumbers.ClientRectangle.Width, lineHeight);
            }

            g.DrawString((i + 1).ToString(), text.Font, textBrush, panelLineNumbers.ClientRectangle.Left, y);
        }
    }

    internal void LoadText(string recipeFileName)
    {
        fileName = recipeFileName;
        text.LoadFile(fileName, RichTextBoxStreamType.PlainText);
        HighlightAll(text);
    }

    private void HighlightAll(RichTextBox box)
    {
        int selectionStart = box.SelectionStart;
        int selectionLength = box.SelectionLength;

        box.SuspendLayout();

        box.SelectAll();
        box.SelectionColor = box.ForeColor;
        box.SelectionFont = new Font(box.Font, FontStyle.Italic);

        HighlightWords(box, 0, box.TextLength);

        box.Select(selectionStart, selectionLength);

        box.ResumeLayout();
    }

    private void HighlightLine(RichTextBox box)
    {
        int currentLineIndex = box.GetLineFromCharIndex(box.SelectionStart);

        int lineStartIndex = box.GetFirstCharIndexFromLine(currentLineIndex);
        int lineEndIndex = box.GetFirstCharIndexFromLine(currentLineIndex + 1);
        if (lineEndIndex == -1) lineEndIndex = box.TextLength;

        int selectionStart = box.SelectionStart;
        int selectionLength = box.SelectionLength;

        box.SuspendLayout();

        box.Select(lineStartIndex, lineEndIndex - lineStartIndex);
        box.SelectionColor = box.ForeColor;
        box.SelectionFont = new Font(box.Font, FontStyle.Italic);

        HighlightWords(box, lineStartIndex, lineEndIndex);

        box.Select(selectionStart, selectionLength);

        box.ResumeLayout();
    }

    private void HighlightWords(RichTextBox box, int startIndex, int endIndex)
    {
        HighlightKeywords(box, startIndex, endIndex, keywords, Color.Brown, FontStyle.Bold);

        HighlightKeywords(box, startIndex, endIndex, functions, Color.Blue, FontStyle.Bold);

        HighlightKeywords(box, startIndex, endIndex, states, Color.Blue, FontStyle.Regular);


        HighlightNumbers(box, startIndex, endIndex, Color.OrangeRed, FontStyle.Regular);
    }

    private void HighlightKeywords(RichTextBox box, int startIndex, int endIndex, string[] words, Color color, FontStyle fontStyle)
    {
        foreach (string word in words)
        {
            int searchIndex = startIndex;
            while (searchIndex < endIndex)
            {
                int wordStartIndex = box.Find(word, searchIndex, endIndex, RichTextBoxFinds.WholeWord);
                if (wordStartIndex != -1)
                {
                    box.Select(wordStartIndex, word.Length);
                    box.SelectionColor = color;
                    box.SelectionFont = new Font(box.Font, fontStyle);
                    searchIndex = wordStartIndex + word.Length;
                }
                else
                {
                    break;
                }
            }
        }
    }

    private void HighlightNumbers(RichTextBox box, int startIndex, int endIndex, Color color, FontStyle fontStyle)
    {
        string pattern = @"\b\d+(\.\d+)?\b";
        Regex regex = new Regex(pattern);
        foreach (Match match in regex.Matches(box.Text.Substring(startIndex, endIndex - startIndex)))
        {
            box.Select(startIndex + match.Index, match.Length);
            box.SelectionColor = color;
            box.SelectionFont = new Font(box.Font, fontStyle);
        }
    }

    private void toolStripButton1_Click(object sender, EventArgs e)
    {
        text.SaveFile(fileName, RichTextBoxStreamType.PlainText);
        Close();
    }

    private void ShowFindReplaceDialog()
    {
        // Recreate if disposed: btnCancel/X Close() disposes the modeless dialog.
        if (findReplaceDialog == null || findReplaceDialog.IsDisposed)
        {
            findReplaceDialog = new frmFindReplace();
            findReplaceDialog.FindNextClicked += FindReplaceDialog_FindNextClicked;
            findReplaceDialog.ReplaceClicked += FindReplaceDialog_ReplaceClicked;
            findReplaceDialog.ReplaceAllClicked += FindReplaceDialog_ReplaceAllClicked;
        }
        findReplaceDialog.Show();
    }

    private void FindReplaceDialog_FindNextClicked(object? sender, EventArgs e)
    {
        if (findReplaceDialog is null) return;
        FindNext(findReplaceDialog.FindText);
    }

    private void FindReplaceDialog_ReplaceClicked(object? sender, EventArgs e)
    {
        if (findReplaceDialog is null) return;
        Replace(findReplaceDialog.FindText, findReplaceDialog.ReplaceText);
    }

    private void FindReplaceDialog_ReplaceAllClicked(object? sender, EventArgs e)
    {
        if (findReplaceDialog is null) return;
        ReplaceAll(findReplaceDialog.FindText, findReplaceDialog.ReplaceText);
    }

    private void FindNext(string findText)
    {
        int startIndex = text.SelectionStart + text.SelectionLength;
        int foundIndex = text.Find(findText, startIndex, RichTextBoxFinds.None);
        if (foundIndex != -1)
        {
            text.Select(foundIndex, findText.Length);
            text.ScrollToCaret();
        }
        else
        {
            ThemedMessageBox.Show(this, "Text not found.", "Find", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void Replace(string findText, string replaceText)
    {
        if (text.SelectedText == findText)
        {
            text.SelectedText = replaceText;
        }
        FindNext(findText);
    }

    private void ReplaceAll(string findText, string replaceText)
    {
        int startIndex = 0;
        int foundIndex;
        while ((foundIndex = text.Find(findText, startIndex, RichTextBoxFinds.None)) != -1)
        {
            text.Select(foundIndex, findText.Length);
            text.SelectedText = replaceText;
            startIndex = foundIndex + replaceText.Length;
        }
    }

    private void toolStripButton2_Click(object sender, EventArgs e)
    {
        ShowFindReplaceDialog();
    }
}
