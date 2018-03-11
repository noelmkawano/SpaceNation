using UnityEngine;
using System.Collections;

/// <summary>
/// Fixed object.
///
/// Object does not move (but it's gravity will affect others). 
///
/// Good choice for e.g. central star in a system
/// </summary>
public class FixedObject : MonoBehaviour, IFixedOrbit {

	private Vector3 physicsPosition; 

	public bool IsFixed() {
		return true;
	}

	public void PreEvolve(float physicalScale, float massScale) {
		physicsPosition = transform.position/physicalScale;
	}
	
	public void Evolve(float physicsTime, float physicalScale, ref float[] r) {
		r[0] = physicsPosition.x;
		r[1] = physicsPosition.y;
		r[2] = physicsPosition.z;
	}

    public Vector3 GetVelocity() {
        return Vector3.zero;
    }

    public Vector3 GetPosition() {
        return transform.position;
    }

    public void GEUpdate() {
        // no position update required - object does not move
    }
}
