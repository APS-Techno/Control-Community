using APS.SystemControl.GUIControls.Process;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// Height distribution for the process-panel side columns: leftover stack
/// height is spread equally over the ReadingRowControl rows so their
/// sparklines grow to fill the column (4K), clamped to [base, max]. Headers,
/// LED rows, and drive pills are part of the fixed budget and never stretch.
/// </summary>
public class ReadingRowHeightTests
{
    private const int Base = 24;
    private const int Max  = 120;

    [Fact]
    public void ZeroRows_ReturnsBase_NoDivisionByZero()
        => Assert.Equal(Base, BaseProcessPanel.ComputeReadingRowHeight(
            availableHeight: 800, fixedBudget: 100, rowCount: 0, Base, Max));

    [Fact]
    public void StackSmallerThanCompactLayout_RowsStayAtBase()
        => Assert.Equal(Base, BaseProcessPanel.ComputeReadingRowHeight(
            availableHeight: 200, fixedBudget: 100, rowCount: 10, Base, Max));

    [Fact]
    public void ExactCompactFit_RowsStayAtBase()
        // 100 fixed + 10 × 24 = 340 — nothing left over.
        => Assert.Equal(Base, BaseProcessPanel.ComputeReadingRowHeight(
            availableHeight: 340, fixedBudget: 100, rowCount: 10, Base, Max));

    [Fact]
    public void LeftoverBelowCap_DistributedEquallyAcrossRows()
        // leftover = 1000 − 100 − 240 = 660 → +66 per row.
        => Assert.Equal(90, BaseProcessPanel.ComputeReadingRowHeight(
            availableHeight: 1000, fixedBudget: 100, rowCount: 10, Base, Max));

    [Fact]
    public void HugeLeftover_ClampedToMax()
        => Assert.Equal(Max, BaseProcessPanel.ComputeReadingRowHeight(
            availableHeight: 5000, fixedBudget: 100, rowCount: 10, Base, Max));

    [Fact]
    public void RoundingRemainder_FlooredSoRowsNeverOverflow()
        // leftover = 365 − 100 − 240 = 25 over 10 rows → +2 each (floor), not +3.
        => Assert.Equal(26, BaseProcessPanel.ComputeReadingRowHeight(
            availableHeight: 365, fixedBudget: 100, rowCount: 10, Base, Max));
}
