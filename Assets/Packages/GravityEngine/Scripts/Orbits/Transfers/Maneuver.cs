using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manuever.
/// Holds a future course change for the spaceship. Will be triggered based on world time.
/// 
/// In some cases (orbital transfers) the value of the change will be recorded as a
/// scalar. In trajectory intercept cases, a vector velocity change will be provided.
/// 
/// Manuevers are added to the GE. This allows them to be run at the closest possible time
/// step (in general GE will do multiple time steps per FixedUpdate). Due to time precision
/// the resulting trajectory may not be exactly as desired. More timesteps in the GE will
/// reduce this error. 
/// 
/// Manuevers support sorting based on earliest worldTime. 
/// 
/// Maneuvers are always expressed in internal physics units of distance and velocity. 
/// 
/// </summary>
public class Maneuver : IComparer<Maneuver>  {

	//! time at which the maneuver is to occur (physical time in GE)
	public float worldTime;

	//! velocity change vector to be applied (if mtype is vector)
	public Vector3 velChange;

	//! scalar value of the velocity change in physics units (+ve means in-line with motion). Use GetDvScaled() for scaled value.    		
	public float dV;

	//! position at which maneuver should occur (if known, trajectories only)
	public Vector3 r;		

	//! NBody to apply the course correction to
	public NBody nbody;

    //! Center body to use when maneuver is circularize
    public NBody centerBody; 

	//! Vector type - apply 3D vel change. Scalar, apply dV to current direction.
	public enum Mtype {vector, scalar, circularize};

	//! Type of information about manuever provided
	public Mtype mtype = Mtype.vector; 


	public delegate void OnExecuted(Maneuver m); 

	//! Delegate to be called when the maneuver is executed
	public OnExecuted onExecuted;

    /// <summary>
    /// Create a vector maneuver at the intercept point to match trajectory
    /// that was intercepted. 
    /// </summary>
    /// <param name="nbody"></param>
    /// <param name="intercept"></param>
	public Maneuver(NBody nbody, TrajectoryData.Intercept intercept) {
		this.nbody = nbody;
		worldTime = intercept.tp1.t;
		velChange = intercept.tp2.v - intercept.tp1.v;
		r = intercept.tp1.r;
		dV = intercept.dV;
	}

    /// <summary>
    /// Create a circularize manuever at time t around centerBody.
    /// </summary>
    /// <param name="nbody"></param>
    /// <param name="time"></param>
    /// <param name="centerBody"></param>
    public Maneuver(NBody nbody, float time, NBody centerBody)
    {
        this.nbody = nbody;
        this.centerBody = centerBody;
        mtype = Mtype.circularize;
        worldTime = time;
    }

	// Empty constructor when caller wishes to fill in field by field
	public Maneuver() {

	}

    /// <summary>
    /// Return the dV in scaled units. 
    /// </summary>
    /// <returns></returns>
    public float GetDvScaled() {
        return dV / GravityScaler.GetVelocityScale();
    }

    /// <summary>
    /// Set the deltaV for a scalar maneuver in world units (e.g. in ORBITAL
    /// units set velocity in km/hr)
    /// </summary>
    /// <param name="newDv"></param>
    public void SetDvScaled(float newDv) {
        dV = newDv * GravityScaler.GetVelocityScale();
    }

    /// <summary>
    /// Set the velocity change vector in owlrd units (e.g. in ORBITAL
    /// units set velocity in km/hr)
    /// </summary>
    /// <param name="newVel"></param>
    public void SetVelScaled(Vector3 newVel) {
        velChange = newVel * GravityScaler.GetVelocityScale();
    }

    /// <summary>
    /// Set the maneuver time in world units (e.g. in ORBITAL units in 
    /// hours). 
    /// </summary>
    /// <param name="time"></param>
    public void SetTimeScaled(float time) {
        worldTime = time / GravityScaler.GetGameSecondPerPhysicsSecond();
    }

    /// <summary>
    /// Execute the maneuver. Called automatically by Gravity Engine for maneuvers that
    /// have been added to the GE via AddManeuver(). 
    /// 
    /// Unusual to call this method directly. 
    /// </summary>
    /// <param name="ge"></param>
    public void Execute(GravityEngine ge)
    {
        Vector3 vel = ge.GetVelocity(nbody);
        switch(mtype)
        {
            case Mtype.vector:
                vel += velChange;
                break;

            case Mtype.scalar:
                // scalar: adjust existing velocity by dV
                Vector3 change = Vector3.Normalize(vel) * dV ;
                vel += change;
                break;

            case Mtype.circularize:
                // find velocity vector perpendicular to r for circular orbit
                // Since we could be mid-integration need to get exact position from GE
                double[] r_ship = new double[3];
                double[] v_ship = new double[3];
                ge.GetPositionVelocityScaled(nbody, ref r_ship, ref v_ship);
                double[] r_center = new double[3];
                double[] v_center = new double[3];
                ge.GetPositionVelocityScaled(centerBody, ref r_center, ref v_center);
                
                Vector3 pos_ship = new Vector3((float) r_ship[0], (float) r_ship[1],(float) r_ship[2]);
                Vector3 vel_ship = new Vector3((float) v_ship[0], (float) v_ship[1], (float) v_ship[2]);
                Vector3 pos_center = new Vector3((float)r_center[0], (float)r_center[1], (float) r_center[2]);
                Vector3 r = pos_ship - pos_center;

                // to get axis of orbit, can take v x r
                Vector3 axis = Vector3.Normalize(Vector3.Cross(vel_ship, pos_ship));
                // vis visa for circular orbit
                float mu = nbody.mass * ge.massScale;
                float v_mag = Mathf.Sqrt(mu / Vector3.Magnitude(r));
                // positive v is counter-clockwise
                Vector3 v_dir = Vector3.Normalize(Vector3.Cross(axis, r));
                Vector3 v_circular = v_mag * v_dir;
                ge.SetVelocity(nbody, v_circular);

                break;
        }
#pragma warning disable 162        // disable unreachable code warning
        if (GravityEngine.DEBUG)
        {
            Debug.Log("Applied manuever: " + LogString() + " engineRef.index=" + nbody.engineRef.index +
                " engineRef.bodyType=" + nbody.engineRef.bodyType + " timeError=" + (worldTime - ge.GetPhysicalTime()) );
            Debug.Log("r= " + Vector3.Magnitude(nbody.transform.position));
        }
#pragma warning restore 162        // enable unreachable code warning

        ge.SetVelocity(nbody, vel);
    }

	public int Compare(Maneuver m1, Maneuver m2) {
			if (m1 == m2) {
				return 0;
			}
			if (m1.worldTime < m2.worldTime) {
				return -1;
			}
			return 1;
	}

	public string LogString() {
		return string.Format("Maneuver {0} t={1} type={2} dV={3} vel={4}", nbody.gameObject.name, worldTime, mtype, dV, velChange);
	}

}
