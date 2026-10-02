using System;
using System.Windows.Forms;
using RSBot.Core;
using RSBot.Core.Objects;
using SDUI.Controls;

namespace RSBot.Training.Views.Dialogs;

public partial class CreateTrainingAreaDialog : UIWindowBase
{
    /// <summary>
    ///     The player position the coordinate boxes were filled with, used as is while they aren't edited (keeps the height).
    /// </summary>
    private Position _filledPosition;

    private string _filledX;
    private string _filledY;
    private string _filledRegion;

    public CreateTrainingAreaDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    ///     Gets the position of the training area that was entered.
    /// </summary>
    public Position AreaPosition { get; private set; }

    private void buttonAccept_Click(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TrainingName.Text))
        {
            DialogResult = DialogResult.Retry;
            return;
        }

        if (!TryGetPosition(out var position))
        {
            MessageBox.Show(
                "Please enter valid X, Y and Region values.",
                "Warning",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            DialogResult = DialogResult.Retry;
            return;
        }

        AreaPosition = position;
    }

    private void CreateTrainingAreaDialog_FormClosing(object sender, FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.Retry)
            e.Cancel = true;
    }

    private void CreateTrainingAreaDialog_Load(object sender, EventArgs e)
    {
        FillWithPlayerPosition();
    }

    private void buttonUseMyPosition_Click(object sender, EventArgs e)
    {
        FillWithPlayerPosition();
    }

    private void textRegion_TextChanged(object sender, EventArgs e)
    {
        labelArea.Text = ushort.TryParse(textRegion.Text, out var region)
            ? Game.ReferenceManager.GetTranslation(region.ToString())
            : "< Area >";
    }

    private void FillWithPlayerPosition()
    {
        _filledPosition = Game.Player.Movement.Source;

        _filledX = _filledPosition.X.ToString("0.0");
        _filledY = _filledPosition.Y.ToString("0.0");
        _filledRegion = _filledPosition.Region.Id.ToString();

        textX.Text = _filledX;
        textY.Text = _filledY;
        textRegion.Text = _filledRegion;
    }

    /// <summary>
    ///     Gets the position from the coordinate boxes. Outside of dungeons the region follows from X and Y.
    /// </summary>
    private bool TryGetPosition(out Position position)
    {
        position = default;

        if (textX.Text == _filledX && textY.Text == _filledY && textRegion.Text == _filledRegion)
        {
            position = _filledPosition;
            return true;
        }

        if (
            !float.TryParse(textX.Text, out var x)
            || !float.TryParse(textY.Text, out var y)
            || !ushort.TryParse(textRegion.Text, out var region)
        )
            return false;

        position = new Position(x, y, region);
        return true;
    }
}
