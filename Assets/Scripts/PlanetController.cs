using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlanetController : MonoBehaviour {

	// Use this for initialization
	void Start () {
		GameObject.Find ("Gravity Controller").GetComponent<GravityController> ().addPlanet (gameObject);
	}
	
	// Update is called once per frame
	void Update () {
		
	}
}