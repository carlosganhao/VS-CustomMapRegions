using System;
using System.Linq;
using CustomMapRegions.Common.Models;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Util;

namespace CustomMapRegions.Client;

public class GuiEditRegionDialog : GuiDialogGeneric
{
    public override bool PrefersUngrabbedMouse => true;
    public override bool DisableMouseGrab => true;
    public override EnumDialogType DialogType => EnumDialogType.Dialog;
    public override double DrawOrder => 0.2;

    private RegionMapLayer _mapLayer;
    private Region _regionToEdit;
    private RegionMapComponent _component;
    private bool _allowUpdate;
    private bool _allowDeletion;
    private string _curName;
    private int _curColor;
    private string _curFill;
    private int[] _colors;
    private string[] _fills;

    public GuiEditRegionDialog(ICoreClientAPI capi, RegionMapLayer mapLayer, RegionMapComponent comp, bool allowUpdate, bool allowDeletion) : base("Edit Region", capi)
    {
        _mapLayer = mapLayer;
        _regionToEdit = comp.Region;
        _component = comp;
        _allowUpdate = allowUpdate;
        _allowDeletion = allowDeletion;
        _colors = _mapLayer.AvailableColors.ToArray();
        _fills = _mapLayer.FillIcons.Keys.ToArray();
    }

    public override bool TryOpen()
    {
        ComposeDialog();
        return base.TryOpen();
    }

    private void ComposeDialog()
    {
        ElementBounds leftColumn = ElementBounds.Fixed(0, 28, 120, 25);
        ElementBounds rightColumn = leftColumn.RightCopy();

        ElementBounds buttonRow = ElementBounds.Fixed(0, 28, 360, 25);

        ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        bgBounds.WithChildren(leftColumn, rightColumn);

        ElementBounds dialogBounds =
            ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);


        if (SingleComposer != null) SingleComposer.Dispose();

        int colorIconSize = 22;

        _curName = _regionToEdit.Name;
        _curColor = _regionToEdit.Color;
        _curFill = _regionToEdit.Fill;
        
        int curColorIndex = _colors.IndexOf(_regionToEdit.Color);
        if(curColorIndex < 0)
        {
            _colors.Append(_regionToEdit.Color);
            curColorIndex = _colors.Length - 1;
        }

        int curFillIndex = _fills.IndexOf(_regionToEdit.Fill);
        if(curFillIndex < 0)
        {
            curFillIndex = 0;
        }

        SingleComposer = capi.Gui
            .CreateCompo("worldmap-addwp", dialogBounds)
            .AddShadedDialogBG(bgBounds, false)
            .AddDialogTitleBar(Lang.Get("regions-dlg-edit-title"), () => TryClose())
            .BeginChildElements(bgBounds)
                .AddStaticText(Lang.Get("regions-dlg-name"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.FlatCopy())
                .AddTextInput(rightColumn = rightColumn.FlatCopy().WithFixedWidth(200), onNameChanged, CairoFont.TextInput(), "nameInput")

                .AddRichtext(Lang.Get("regions-dlg-color"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.BelowCopy(0, 5))
                .AddColorListPicker(_colors, onColorSelected, leftColumn = leftColumn.BelowCopy(0, 5).WithFixedSize(colorIconSize, colorIconSize), 270, "colorpicker")

                .AddRichtext(Lang.Get("regions-dlg-fill"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.WithFixedPosition(0, leftColumn.fixedY + leftColumn.fixedHeight).WithFixedWidth(100).BelowCopy(0, 0))
                .AddIconListPicker(_fills, onFillSelected, leftColumn = leftColumn.BelowCopy(0, 5).WithFixedSize(colorIconSize+5, colorIconSize+5), 270, "fillpicker")

                .AddSmallButton(Lang.Get("regions-dlg-cancel"), onCancel, buttonRow.FlatCopy().FixedUnder(leftColumn, 0).WithFixedWidth(100), EnumButtonStyle.Normal)
                .AddIf(_allowDeletion)
                    .AddSmallButton(Lang.Get("regions-dlg-delete"), onDelete, buttonRow.FlatCopy().FixedUnder(leftColumn, 0).WithFixedWidth(100).WithAlignment(EnumDialogArea.CenterFixed), EnumButtonStyle.Normal)
                .EndIf()
                .AddIf(_allowUpdate)
                    .AddSmallButton(Lang.Get("regions-dlg-save"), onSave, buttonRow.FlatCopy().FixedUnder(leftColumn, 0).WithFixedWidth(100).WithAlignment(EnumDialogArea.RightFixed), EnumButtonStyle.Normal, key: "saveButton")
                .EndIf()
            .EndChildElements()
            .Compose()
        ;

        if(_allowUpdate)
        {
            SingleComposer.GetButton("saveButton").Enabled = false;
        }
        else
        {
            SingleComposer.GetTextInput("nameInput").Enabled = false;
            for(int i = 0; i < _colors.Length; i++)
            {
                SingleComposer.GetColorListPicker($"colorpicker-{i}").Enabled = false;
            }

            for(int i = 0; i < _fills.Length; i++)
            {
                SingleComposer.GetIconListPicker($"fillpicker-{i}").Enabled = false;
            }
        }

        SingleComposer.GetTextInput("nameInput").SetValue(_regionToEdit.Name);
        SingleComposer.ColorListPickerSetValue("colorpicker", curColorIndex);
        SingleComposer.IconListPickerSetValue("fillpicker", curFillIndex);
    }

    private void onNameChanged(string text)
    {
        if(!_allowUpdate) return;
        SingleComposer.GetButton("saveButton").Enabled = text.Trim() != "";
        _curName = text;
        _component.TempUpdate(_curName, _curColor, _curFill);
    }

    private void onColorSelected(int index)
    {
        if(!_allowUpdate) return;
        _curColor = _colors[index];
        _component.TempUpdate(_curName, _curColor, _curFill);
    }

    private void onFillSelected(int index)
    {
        if(!_allowUpdate) return;
        _curFill = _fills[index];
        _component.TempUpdate(_curName, _curColor, _curFill);
    }

    private bool onSave()
    {
        string name = SingleComposer.GetTextInput("nameInput").GetText();

        _mapLayer.EditRegion(_regionToEdit.RegionId, _curColor, name, _curFill);
        TryClose();
        return true;
    }

    private bool onDelete()
    {
        _mapLayer.DeleteRegion(_regionToEdit.RegionId);
        TryClose();
        return true;
    }

    private bool onCancel()
    {
        TryClose();
        return true;
    }

    public override void OnGuiClosed()
    {
        _component.ReinitLocals();
        base.OnGuiClosed();
    }

    public override bool CaptureAllInputs()
    {
        return IsOpened();
    }

    public override void OnMouseDown(MouseEvent args)
    {
        base.OnMouseDown(args);

        args.Handled = true;
    }

    public override void OnMouseUp(MouseEvent args)
    {
        base.OnMouseUp(args);
        args.Handled = true;
    }

    public override void OnMouseMove(MouseEvent args)
    {
        base.OnMouseMove(args);
        args.Handled = true;
    }

    public override void OnMouseWheel(MouseWheelEventArgs args)
    {
        base.OnMouseWheel(args);
        args.SetHandled(true);
    }
}