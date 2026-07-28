namespace POE2Radar.Overlay.Pickup;

internal readonly record struct PickupMatchItem(
    int Index,
    string Name,
    float ScreenX,
    float ScreenY);

internal readonly record struct PickupMatchLabel(
    int Index,
    float ScreenX,
    float ScreenY,
    IReadOnlySet<string> Lines);

internal readonly record struct PickupLabelMatch(
    int ItemIndex,
    int LabelIndex,
    float ScreenDistance);

/// <summary>
/// Globally associates projected ground items with visible labels. It maximizes the number of
/// credible name matches first, then minimizes their total screen-space distance.
/// </summary>
internal static class PickupLabelMatcher
{
    public static IReadOnlyList<PickupLabelMatch> Match(
        IReadOnlyList<PickupMatchItem> items,
        IReadOnlyList<PickupMatchLabel> labels,
        float maxScreenDistance)
    {
        if (items.Count == 0 || labels.Count == 0)
            return [];

        var maxDistanceSquared = maxScreenDistance > 0f && float.IsFinite(maxScreenDistance)
            ? maxScreenDistance * maxScreenDistance
            : float.PositiveInfinity;
        var distances = new double[items.Count, labels.Count];
        var valid = new bool[items.Count, labels.Count];
        var itemEdges = Enumerable.Range(0, items.Count)
            .Select(_ => new List<int>())
            .ToArray();
        var labelEdges = Enumerable.Range(0, labels.Count)
            .Select(_ => new List<int>())
            .ToArray();

        for (var i = 0; i < items.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(items[i].Name) ||
                !float.IsFinite(items[i].ScreenX) ||
                !float.IsFinite(items[i].ScreenY))
                continue;
            for (var j = 0; j < labels.Count; j++)
            {
                if (!float.IsFinite(labels[j].ScreenX) ||
                    !float.IsFinite(labels[j].ScreenY))
                    continue;
                if (!ContainsLine(labels[j].Lines, items[i].Name)) continue;
                var dx = items[i].ScreenX - labels[j].ScreenX;
                var dy = items[i].ScreenY - labels[j].ScreenY;
                var distanceSquared = (double)dx * dx + (double)dy * dy;
                if (!double.IsFinite(distanceSquared) || distanceSquared > maxDistanceSquared)
                    continue;
                valid[i, j] = true;
                distances[i, j] = distanceSquared;
                itemEdges[i].Add(j);
                labelEdges[j].Add(i);
            }
        }

        // Solve disconnected name/conflict groups independently. A screen full of distinct item
        // names stays close to linear instead of paying for one mostly-invalid global matrix.
        var result = new List<PickupLabelMatch>(Math.Min(items.Count, labels.Count));
        var seenItems = new bool[items.Count];
        var seenLabels = new bool[labels.Count];
        for (var startItem = 0; startItem < items.Count; startItem++)
        {
            if (seenItems[startItem] || itemEdges[startItem].Count == 0) continue;
            var componentItems = new List<int>();
            var componentLabels = new List<int>();
            var pendingItems = new Queue<int>();
            seenItems[startItem] = true;
            pendingItems.Enqueue(startItem);
            while (pendingItems.Count > 0)
            {
                var itemRow = pendingItems.Dequeue();
                componentItems.Add(itemRow);
                foreach (var labelRow in itemEdges[itemRow])
                {
                    if (seenLabels[labelRow]) continue;
                    seenLabels[labelRow] = true;
                    componentLabels.Add(labelRow);
                    foreach (var connectedItem in labelEdges[labelRow])
                    {
                        if (seenItems[connectedItem]) continue;
                        seenItems[connectedItem] = true;
                        pendingItems.Enqueue(connectedItem);
                    }
                }
            }

            AddComponentMatches(
                componentItems,
                componentLabels,
                items,
                labels,
                distances,
                valid,
                result);
        }
        return result;
    }

    private static void AddComponentMatches(
        IReadOnlyList<int> componentItems,
        IReadOnlyList<int> componentLabels,
        IReadOnlyList<PickupMatchItem> items,
        IReadOnlyList<PickupMatchLabel> labels,
        double[,] distances,
        bool[,] valid,
        List<PickupLabelMatch> result)
    {
        var greatestValidCost = 1d;
        foreach (var itemRow in componentItems)
            foreach (var labelRow in componentLabels)
                if (valid[itemRow, labelRow])
                    greatestValidCost = Math.Max(greatestValidCost, distances[itemRow, labelRow]);

        // One private dummy column per item permits an unmatched result. Its cost dominates every
        // possible valid-distance improvement, so assignment cardinality wins before proximity.
        var columnCount = componentLabels.Count + componentItems.Count;
        var unmatchedCost = greatestValidCost * (componentItems.Count + 1d) + 1d;
        var invalidCost = unmatchedCost * (componentItems.Count + 1d) + 1d;
        var costs = new double[componentItems.Count, columnCount];
        for (var i = 0; i < componentItems.Count; i++)
        {
            for (var j = 0; j < componentLabels.Count; j++)
                costs[i, j] = valid[componentItems[i], componentLabels[j]]
                    ? distances[componentItems[i], componentLabels[j]]
                    : invalidCost;
            for (var j = componentLabels.Count; j < columnCount; j++)
                costs[i, j] = unmatchedCost;
        }

        var assignment = MinimumCostAssignment(costs);
        for (var i = 0; i < assignment.Length; i++)
        {
            var componentLabelRow = assignment[i];
            if (componentLabelRow < 0 || componentLabelRow >= componentLabels.Count) continue;
            var itemRow = componentItems[i];
            var labelRow = componentLabels[componentLabelRow];
            if (!valid[itemRow, labelRow]) continue;
            result.Add(new PickupLabelMatch(
                items[itemRow].Index,
                labels[labelRow].Index,
                MathF.Sqrt((float)distances[itemRow, labelRow])));
        }
    }

    private static bool ContainsLine(IReadOnlySet<string> lines, string name)
    {
        if (lines.Contains(name)) return true;
        foreach (var line in lines)
            if (string.Equals(line, name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>Hungarian assignment for a rectangular matrix where rows never exceed columns.</summary>
    private static int[] MinimumCostAssignment(double[,] costs)
    {
        var rowCount = costs.GetLength(0);
        var columnCount = costs.GetLength(1);
        var rowPotential = new double[rowCount + 1];
        var columnPotential = new double[columnCount + 1];
        var columnRow = new int[columnCount + 1];
        var previousColumn = new int[columnCount + 1];

        for (var row = 1; row <= rowCount; row++)
        {
            columnRow[0] = row;
            var currentColumn = 0;
            var minValue = Enumerable.Repeat(double.PositiveInfinity, columnCount + 1).ToArray();
            var used = new bool[columnCount + 1];

            do
            {
                used[currentColumn] = true;
                var currentRow = columnRow[currentColumn];
                var delta = double.PositiveInfinity;
                var nextColumn = 0;
                for (var column = 1; column <= columnCount; column++)
                {
                    if (used[column]) continue;
                    var reduced = costs[currentRow - 1, column - 1] -
                                  rowPotential[currentRow] -
                                  columnPotential[column];
                    if (reduced < minValue[column])
                    {
                        minValue[column] = reduced;
                        previousColumn[column] = currentColumn;
                    }
                    if (minValue[column] >= delta) continue;
                    delta = minValue[column];
                    nextColumn = column;
                }

                for (var column = 0; column <= columnCount; column++)
                {
                    if (used[column])
                    {
                        rowPotential[columnRow[column]] += delta;
                        columnPotential[column] -= delta;
                    }
                    else
                    {
                        minValue[column] -= delta;
                    }
                }
                currentColumn = nextColumn;
            } while (columnRow[currentColumn] != 0);

            do
            {
                var nextColumn = previousColumn[currentColumn];
                columnRow[currentColumn] = columnRow[nextColumn];
                currentColumn = nextColumn;
            } while (currentColumn != 0);
        }

        var assignment = Enumerable.Repeat(-1, rowCount).ToArray();
        for (var column = 1; column <= columnCount; column++)
            if (columnRow[column] != 0)
                assignment[columnRow[column] - 1] = column - 1;
        return assignment;
    }
}
