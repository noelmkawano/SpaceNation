using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RocketController : MonoBehaviour {

	public int punch;

	// Use this for initialization
	void Start () {
		GameObject.Find ("Gravity Controller").GetComponent<GravityController> ().addRocket (gameObject);
	}
	
	// Update is called once per frame
	void FixedUpdate () {
		if (Input.GetKeyDown ("w")) {
			gameObject.GetComponent<Rigidbody> ().AddForce (Vector3.up * punch);
		}
		if (Input.GetKeyDown ("a")) {
			gameObject.GetComponent<Rigidbody> ().AddForce (Vector3.left * punch);
		}
		if (Input.GetKeyDown ("s")) {
			gameObject.GetComponent<Rigidbody> ().AddForce (Vector3.down * punch);
		}
		if (Input.GetKeyDown ("d")) {
			gameObject.GetComponent<Rigidbody> ().AddForce (Vector3.right * punch);
		}
	}
}
