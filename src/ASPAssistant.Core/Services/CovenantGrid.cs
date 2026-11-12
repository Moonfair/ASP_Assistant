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
        // The gap between the headings scales with the grid, including when the whole panel is inset.
        double sectionHeight = additionalTitle.Box.Y - coreTitle.Box.Y;
        return new(coreTitle.Box.X - sectionHeight * .05, sectionHeight * .41, sectionHeight * .15,
            sectionHeight * .25, sectionHeight * .25, sectionHeight * .75, sectionHeight * .69);
    }

    public double ColumnCenter(int column) => FirstColumn + column * ColumnSpacing;
    public int NearestColumn(double x) => (int)Math.Round((x - FirstColumn) / ColumnSpacing);
}
