using System.Collections.Generic;
using UnityEngine;

// Owns only overlay bindings. The backend task owns coverage and validity.
public sealed class BuildingRangeDisplay
{
    public BuildingRangeEvaluation Evaluation { get; private set; }
    private WorldPlacementHighlight highlight;
    private Material material;
    private Sprite edge, corner;
    private float heightOffset;
    private readonly List<object> keys = new List<object>();

    public void Show(BuildingRangeEvaluation evaluation, WorldPlacementHighlight renderer, Material material,
        Sprite edge, Sprite corner, float heightOffset)
    {
        Clear();
        Evaluation = evaluation;
        highlight = renderer; this.material = material; this.edge = edge; this.corner = corner;
        this.heightOffset = heightOffset;
        if (evaluation == null) return;
        if (evaluation.IsComplete) HandleFinished(evaluation);
        else evaluation.Finished += HandleFinished;
    }

    private void HandleFinished(BuildingRangeEvaluation evaluation)
    {
        if (evaluation != Evaluation || evaluation.IsCancelled || !evaluation.IsCurrent() || highlight == null) return;
        foreach (BuildingRangeResult result in evaluation.Results)
        {
            if (result.Cells.Count == 0) continue;
            keys.Add(result);
            highlight.ShowRange(result, result.Cells, material, edge, corner, result.Color, heightOffset);
        }
    }

    public void Clear()
    {
        if (Evaluation != null)
        {
            Evaluation.Finished -= HandleFinished;
            Evaluation.Cancel();
            Evaluation = null;
        }
        if (highlight != null)
            foreach (object key in keys) highlight.ClearRange(key);
        keys.Clear();
        highlight = null;
    }
}
