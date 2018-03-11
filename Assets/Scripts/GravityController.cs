using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GravityController : MonoBehaviour {

	private List<Rigidbody> planetRigidbody = new List<Rigidbody>();
	private List<float> planetMass = new List<float>();
	private List<Vector3> planetPosition = new List<Vector3>();
	private List<Rigidbody> rocketRigidbody = new List<Rigidbody>();
	private List<float> rocketMass = new List<float>();
	private List<Vector3> rocketPosition = new List<Vector3>();

	private int planetCount, rocketCount;
	private float distance;
	private Vector3 direction;
	private Vector3 force;

	public float gravityVariable;
	
	// Update is called once per frame
	void FixedUpdate () {
		updatePositions ();
		updateForce ();
	}

	void updateForce () {
		for (int i = 0; i < planetCount; i++) {
			for (int j = 0; j < rocketCount; j++) {
				distance = Vector3.Distance (planetPosition [i], rocketPosition [j]);
				direction = Vector3.Normalize (planetPosition [i] - rocketPosition [j]);
				force = direction * (gravityVariable * planetMass [i] * rocketMass [j] / (distance * distance));
				rocketRigidbody [j].AddForce (force);
			}
		}
	}

	void updatePositions () {
		for (int i = 0; i < planetCount; i++) {
			planetPosition [i] = planetRigidbody [i].transform.position;
		}

		for (int i = 0; i < rocketCount; i++) {
			rocketPosition [i] = rocketRigidbody [i].transform.position;
		}
	}

	public void addPlanet(GameObject planet){
		planetCount++;
		planetRigidbody.Add (planet.GetComponent<Rigidbody>());
		planetMass.Add (planet.GetComponent<Rigidbody>().mass);
		planetPosition.Add (planet.GetComponent<Transform> ().position);
	}

	public void addRocket(GameObject rocket){
		rocketCount++;
		rocketRigidbody.Add (rocket.GetComponent<Rigidbody> ());
		rocketMass.Add (rocket.GetComponent<Rigidbody>().mass);
		rocketPosition.Add (rocket.GetComponent<Transform> ().position);
	}
}
