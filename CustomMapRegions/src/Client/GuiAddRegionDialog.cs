using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace CustomMapRegions.Client;

public class GuiAddRegionDialog : GuiDialogGeneric
{
    public override bool PrefersUngrabbedMouse => true;
    public override bool DisableMouseGrab => true;
    public override EnumDialogType DialogType => EnumDialogType.Dialog;
    public override double DrawOrder => 0.2;

    private RegionMapLayer _mapLayer;
    private FastVec2i _chunkCoords;
    private int _curColor;
    private string _curFill;
    private int[] _colors;
    private string[] _fills;

    public GuiAddRegionDialog(ICoreClientAPI capi, RegionMapLayer mapLayer, FastVec2i chunkCoords) : base("Add Region", capi)
    {
        _mapLayer = mapLayer;
        _chunkCoords = chunkCoords;
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
        ElementBounds leftColumn = ElementBounds.Fixed(0, 28, 90, 25);
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

        _curColor = _colors[0];
        _curFill = _fills[0];

        SingleComposer = capi.Gui
            .CreateCompo("worldmap-addwp", dialogBounds)
            .AddShadedDialogBG(bgBounds, false)
            .AddDialogTitleBar(Lang.Get("regions-dlg-add-title"), () => TryClose())
            .BeginChildElements(bgBounds)
                .AddStaticText(Lang.Get("regions-dlg-name"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.FlatCopy())
                .AddTextInput(rightColumn = rightColumn.FlatCopy().WithFixedWidth(200), onNameChanged, CairoFont.TextInput(), "nameInput")

                .AddRichtext(Lang.Get("regions-dlg-color"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.BelowCopy(0, 5))
                .AddColorListPicker(_colors, onColorSelected, leftColumn = leftColumn.BelowCopy(0, 5).WithFixedSize(colorIconSize, colorIconSize), 270, "colorpicker")

                .AddRichtext(Lang.Get("regions-dlg-fill"), CairoFont.WhiteSmallText(), leftColumn = leftColumn.WithFixedPosition(0, leftColumn.fixedY + leftColumn.fixedHeight).WithFixedWidth(100).BelowCopy(0, 0))
                .AddIconListPicker(_fills, onFillSelected, leftColumn = leftColumn.BelowCopy(0, 5).WithFixedSize(colorIconSize+5, colorIconSize+5), 270, "fillpicker")

                .AddSmallButton(Lang.Get("regions-dlg-cancel"), onCancel, buttonRow.FlatCopy().FixedUnder(leftColumn, 0).WithFixedWidth(100), EnumButtonStyle.Normal)
                .AddSmallButton(Lang.Get("regions-dlg-save"), onSave, buttonRow.FlatCopy().FixedUnder(leftColumn, 0).WithFixedWidth(100).WithAlignment(EnumDialogArea.RightFixed), EnumButtonStyle.Normal, key: "saveButton")
            .EndChildElements()
            .Compose()
        ;

        SingleComposer.GetButton("saveButton").Enabled = false;

        SingleComposer.ColorListPickerSetValue("colorpicker", 0);
        SingleComposer.IconListPickerSetValue("fillpicker", 0);

        capi.Logger.Debug($"Dialog opening");
    }

    private void onColorSelected(int index)
    {
        _curColor = _colors[index];
    }

    private void onFillSelected(int index)
    {
        _curFill = _fills[index];
    }

    private bool onSave()
    {
        string name = SingleComposer.GetTextInput("nameInput").GetText();

        _mapLayer.CreateRegion(_chunkCoords, _curColor, name, _curFill);
        TryClose();
        return true;
    }

    private bool onCancel()
    {
        TryClose();
        return true;
    }

    private void onNameChanged(string t1)
    {
        SingleComposer.GetButton("saveButton").Enabled = (t1.Trim() != "");
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