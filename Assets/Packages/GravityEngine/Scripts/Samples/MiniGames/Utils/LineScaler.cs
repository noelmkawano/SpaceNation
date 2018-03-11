using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Find all line renderers in the scene and allow their width to be globally changed. 
/// 
/// This is useful when zooming across larger scales with orbit predictor and/or trajectory
/// paths in use. 
/// 
/// </summary>
public class LineScaler : MonoBehaviour {

    //! scale of width w.r.t. zoom value
    public float zoomSlope = 0.2f;

    //! end width as a fraction of start width 
    public float endWidthPercent = 1f;

    private LineRenderer[] lineRenderers;

	void Start () {

        lineRenderers = (LineRenderer[]) Object.FindObjectsOfType(typeof(LineRenderer));
	}

    /// <summary>
    /// Re-detect all line renderers in a scene
    /// </summary>
    public void FindAll() {
        lineRenderers = (LineRenderer[])Object.FindObjectsOfType(typeof(LineRenderer));
    }

    // Update is called once per frame
    public void SetZoom (float zoom) {

        // turn zoom into a start/end width
        // this will be scene dependent

        float width = zoom * zoomSlope; 

		foreach (LineRenderer lr in lineRenderers) {
            lr.startWidth = width;
            lr.endWidth = endWidthPercent * width;
        }
	}
}
