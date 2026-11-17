namespace ASPAssistant.Core.Services;

internal readonly record struct CovenantGrid(
    double FirstColumn, double ColumnSpacing, double ColumnTolerance, double IconToLabel,
    double CoreLabelToAdditionalTitle, double FirstAdditionalLabel, double AdditionalRowSpacing)
{
    public static CovenantGrid Preparation(int width, int height) =>
        new(width * .046875, width * .0671875, width * .025, height * .075,
            height * .07, height * .219, height * .20);

    public static CovenantGrid Overview(OcrTextResult coreTitle, OcrTextResult additionalTitle)
    {
        // Compare text centers so a box that includes the icon doesn't stretch the grid.
        double sectionHeight = additionalTitle.Box.Y + additionalTitle.Box.H / 2.0
            - coreTitle.Box.Y - coreTitle.Box.H / 2.0;
        double titleLeft = Math.Max(coreTitle.Box.X, additionalTitle.Box.X);
        return new(titleLeft - sectionHeight * .05, sectionHeight * .41, sectionHeight * .15,
            sectionHeight * .25, sectionHeight * .25, sectionHeight * .75, sectionHeight * .69);
    }

    public double ColumnCenter(int column) => FirstColumn + column * ColumnSpacing;
    public int NearestColumn(double x) => (int)Math.Round((x - FirstColumn) / ColumnSpacing);
}
