using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Add/delete tester.
/// Create and remove massive and massless planet prefabs (with attached OrbitEllipse) at 
/// </summary>
public class AddDeleteTester : MonoBehaviour {

	public GameObject orbitingPrefab;
	public GameObject star;

	public  float maxRadius= 30f;
	public  float minRadius = 5f;
    public float mass = 0.001f;

	private List<GameObject> massiveObjects; 
	private List<GameObject> masslessObjects; 
	private List<GameObject> keplerObjects; 

	private Color[] colors = { Color.red, Color.white, Color.blue, Color.cyan, Color.gray, Color.green, Color.magenta, Color.yellow};
	private int colorIndex = 0; 

	// Use this for initialization
	void Awake () {
		massiveObjects = new List<GameObject>();
		masslessObjects = new List<GameObject>();
		keplerObjects = new List<GameObject>();
	}

	private const string MASSLESS = "massless";
	private const string KEPLER = "kepler";
	
	public void AddBody(string bodyType) {

		GameObject go = Instantiate(orbitingPrefab) as GameObject;
		go.transform.parent = star.transform;
		NBody nbody = go.GetComponent<NBody>();
		if (bodyType == MASSLESS) {
			nbody.mass = 0f;
			masslessObjects.Add(go);
		} else if (bodyType == KEPLER) {
			nbody.mass = 0f;
			keplerObjects.Add(go);
		} else {
			nbody.mass = mass;	// nominal
			massiveObjects.Add(go);
		}

		OrbitEllipse eb = go.GetComponent<OrbitEllipse>();
		if (eb == null) {
			Debug.LogError("Failed to get EllipseStart from prefab");
			return;
		}
		eb.paramBy = EllipseBase.ParamBy.AXIS_A;
		eb.a = Random.Range(minRadius, maxRadius);
		eb.inclination = Random.Range(-80f, 80f);
		if (bodyType == KEPLER) {
			eb.evolveMode = OrbitEllipse.evolveType.KEPLERS_EQN;
		}
		eb.Init();
        GravityEngine.instance.AddBody(go);

        // Do this after added otherwise Kepler trails have a segment to origin
        TrailRenderer trail = go.GetComponentInChildren<TrailRenderer>();
        if (trail != null) {
			trail.material.color = colors[colorIndex];
			colorIndex = (colorIndex+1)%colors.Length;
            trail.enabled = true;
		}

	}

	public void RemoveBody(string bodyType) {

		List<GameObject> bodyList = null;

		if (bodyType == MASSLESS) {
			bodyList = masslessObjects; 
		} else if (bodyType == KEPLER) {
			bodyList = keplerObjects; 
		} else {
			bodyList = massiveObjects;
		}
		if (bodyList.Count > 0) {
			int entry = (int)(Random.Range(0, (float) bodyList.Count));
			GameObject toDestroy = bodyList[entry];
			GravityEngine.instance.RemoveBody(toDestroy);
			bodyList.RemoveAt(entry);
			Destroy(toDestroy);
		} else {
			Debug.Log("All objects of that type removed.");
		}

	}

	void Update () {
		if (Input.GetKeyDown(KeyCode.C)) {
			GravityEngine.Instance().Clear();
			Debug.Log("Clear all bodies"); 
			foreach (GameObject go in massiveObjects) {
				Destroy(go);
			}
			massiveObjects.Clear();
			foreach (GameObject go in masslessObjects) {
				Destroy(go);
			}
			masslessObjects.Clear();
		} 

	}
}
