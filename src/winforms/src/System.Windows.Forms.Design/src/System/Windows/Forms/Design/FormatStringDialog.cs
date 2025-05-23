﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.ComponentModel.Design;

namespace System.Windows.Forms.Design;

internal class FormatStringDialog : Form
{
    // we need the context for the HELP service provider
    private readonly ITypeDescriptorContext? _context;
    private Button _cancelButton;
    private Button _okButton;
    private FormatControl _formatControl1;
    private bool _dirty;
    private DataGridViewCellStyle? _dgvCellStyle;
    private ListControl? _listControl;

    public FormatStringDialog(ITypeDescriptorContext? context)
    {
        _context = context;
        InitializeComponent();

        // Set right to left property according to SR.RTL value.
        string rtlString = SR.RTL;

        if (rtlString.Equals("RTL_False"))
        {
            RightToLeft = RightToLeft.No;
            RightToLeftLayout = false;
        }
        else
        {
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
        }
    }

    public DataGridViewCellStyle DataGridViewCellStyle
    {
        set
        {
            _dgvCellStyle = value;
            _listControl = null;
        }
    }

    public bool Dirty
    {
        get
        {
            return _dirty || _formatControl1.Dirty;
        }
    }

    public ListControl ListControl
    {
        set
        {
            _listControl = value;
            _dgvCellStyle = null;
        }
    }

    private void FormatStringDialog_HelpButtonClicked(object? sender, CancelEventArgs e)
    {
        FormatStringDialog_HelpRequestHandled();
        e.Cancel = true;
    }

    private void FormatStringDialog_HelpRequested(object? sender, HelpEventArgs e)
    {
        FormatStringDialog_HelpRequestHandled();
        e.Handled = true;
    }

    private void FormatStringDialog_HelpRequestHandled()
    {
        if (_context.TryGetService(out IHelpService? helpService))
        {
            helpService.ShowHelpFromKeyword("vs.FormatStringDialog");
        }
    }

    // HACK: if we're adjusting positions after the form's loaded, we didn't set the form up correctly.
    internal void FormatControlFinishedLoading()
    {
        _okButton.Top = _formatControl1.Bottom + 5;
        _cancelButton.Top = _formatControl1.Bottom + 5;
        int formatControlRightSideOffset = GetRightSideOffset(_formatControl1);
        int cancelButtonRightSideOffset = GetRightSideOffset(_cancelButton);
        _okButton.Left += formatControlRightSideOffset - cancelButtonRightSideOffset;
        _cancelButton.Left += formatControlRightSideOffset - cancelButtonRightSideOffset;
    }

    private static int GetRightSideOffset(Control ctl)
    {
        int result = ctl.Width;
        Control? control = ctl;
        while (control is not null)
        {
            result += control.Left;
            control = control.Parent;
        }

        return result;
    }

    private void FormatStringDialog_Load(object? sender, EventArgs e)
    {
        // make a reasonable guess what user control should be shown
        string formatString = _dgvCellStyle is not null ? _dgvCellStyle.Format : _listControl!.FormatString;
        object? nullValue = _dgvCellStyle?.NullValue;
        string formatType = string.Empty;

        if (!string.IsNullOrEmpty(formatString))
        {
            formatType = FormatControl.FormatTypeStringFromFormatString(formatString);
        }

        // the null value text box should be enabled only when editing DataGridViewCellStyle
        // when we are editing ListControl, it should be disabled
        if (_dgvCellStyle is not null)
        {
            _formatControl1.NullValueTextBoxEnabled = true;
        }
        else
        {
            Debug.Assert(_listControl is not null, "we check this everywhere, but it does not hurt to check it again");
            _formatControl1.NullValueTextBoxEnabled = false;
        }

        _formatControl1.FormatType = formatType;

        // push the information from FormatString/FormatInfo/NullValue into the FormattingUserControl
        FormatControl.FormatTypeClass? formatTypeItem = _formatControl1.FormatTypeItem;

        if (formatTypeItem is not null)
        {
            // parsing the FormatString uses the CultureInfo. So push the CultureInfo before push the FormatString.
            formatTypeItem.PushFormatStringIntoFormatType(formatString);
        }
        else
        {
            // make General format type the default
            _formatControl1.FormatType = SR.BindingFormattingDialogFormatTypeNoFormatting;
        }

        _formatControl1.NullValue = nullValue is not null ? nullValue.ToString() : "";
    }

    public static void End()
    {
        // clear the tree nodes collection
    }

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    [MemberNotNull(nameof(_cancelButton))]
    [MemberNotNull(nameof(_okButton))]
    [MemberNotNull(nameof(_formatControl1))]
    private void InitializeComponent()
    {
        _cancelButton = new Button();
        _okButton = new Button();
        _formatControl1 = new FormatControl();
        SuspendLayout();
        //
        // formatControl1
        //
        _formatControl1.Location = new Drawing.Point(10, 10);
        _formatControl1.Margin = new Padding(0);
        _formatControl1.Name = "formatControl1";
        _formatControl1.Size = new Drawing.Size(376, 268);
        _formatControl1.TabIndex = 0;
        //
        // cancelButton
        //
        _cancelButton.Location = new Drawing.Point(299, 288);
        _cancelButton.Name = "cancelButton";
        _cancelButton.Size = new Drawing.Size(87, 23);
        _cancelButton.TabIndex = 2;
        _cancelButton.Text = SR.DataGridView_Cancel;
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Click += cancelButton_Click;
        //
        // okButton
        //
        _okButton.Location = new Drawing.Point(203, 288);
        _okButton.Name = "okButton";
        _okButton.Size = new Drawing.Size(87, 23);
        _okButton.TabIndex = 1;
        _okButton.Text = SR.DataGridView_OK;
        _okButton.DialogResult = DialogResult.OK;
        _okButton.Click += okButton_Click;
        //
        // Form1
        //
        AutoScaleMode = AutoScaleMode.Font;
        AutoScaleDimensions = new Drawing.SizeF(6, 13);
        ClientSize = new Drawing.Size(396, 295);
        AutoSize = true;
        HelpButton = true;
        MaximizeBox = false;
        MinimizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        Icon = null;
        Name = "Form1";
        Controls.Add(_okButton);
        Controls.Add(_formatControl1);
        Controls.Add(_cancelButton);
        Padding = new Padding(0);
        Text = SR.FormatStringDialogTitle;
        HelpButtonClicked += FormatStringDialog_HelpButtonClicked;
        HelpRequested += FormatStringDialog_HelpRequested;
        Load += FormatStringDialog_Load;
        ResumeLayout(false);
    }

    private void cancelButton_Click(object? sender, EventArgs e)
    {
        _dirty = false;
    }

    private void okButton_Click(object? sender, EventArgs e)
    {
        PushChanges();
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if ((keyData & Keys.Modifiers) != 0)
        {
            return base.ProcessDialogKey(keyData);
        }

        switch (keyData & Keys.KeyCode)
        {
            case Keys.Enter:
                DialogResult = DialogResult.OK;
                PushChanges();
                Close();
                return true;
            case Keys.Escape:
                _dirty = false;
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            default:
                return base.ProcessDialogKey(keyData);
        }
    }

    private void PushChanges()
    {
        FormatControl.FormatTypeClass? formatTypeItem = _formatControl1.FormatTypeItem;

        if (formatTypeItem is null)
        {
            return;
        }

        if (_dgvCellStyle is not null)
        {
            _dgvCellStyle.Format = formatTypeItem.FormatString;
            _dgvCellStyle.NullValue = _formatControl1.NullValue;
        }
        else
        {
            _listControl!.FormatString = formatTypeItem.FormatString;
        }

        _dirty = true;
    }
}
