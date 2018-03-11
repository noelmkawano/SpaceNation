using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CircularizeXfer : OrbitTransfer
{

    public CircularizeXfer(OrbitData fromOrbit) : base(fromOrbit)
    {
        name = "Circularize";

        // find velocity vector perpendicular to r for circular orbit
        double[] r_ship = new double[3];
        double[] v_ship = new double[3];
        GravityEngine ge = GravityEngine.Instance();

        ge.GetPositionVelocityScaled(fromOrbit.nbody, ref r_ship, ref v_ship);
        double[] r_center = new double[3];
        double[] v_center = new double[3];
        ge.GetPositionVelocityScaled(fromOrbit.centralMass, ref r_center, ref v_center);

        Vector3 pos_ship = new Vector3((float)r_ship[0], (float)r_ship[1], (float)r_ship[2]);
        Vector3 vel_ship = new Vector3((float)v_ship[0], (float)v_ship[1], (float)v_ship[2]);
        Vector3 pos_center = new Vector3((float)r_center[0], (float)r_center[1], (float)r_center[2]);
        Vector3 vel_center = new Vector3((float)v_center[0], (float)v_center[1], (float)v_center[2]);
        Vector3 r = pos_ship - pos_center;

        // want velocity relative to central mass (it could be moving)
        vel_ship = vel_ship - vel_center;

        // to get axis of orbit, can take r x v
        Vector3 axis = Vector3.Normalize(Vector3.Cross(pos_ship, vel_ship));
        // vis visa for circular orbit
        float mu = fromOrbit.centralMass.mass * ge.massScale;
        float v_mag = Mathf.Sqrt(mu / Vector3.Magnitude(r));
        // positive v is counter-clockwise
        Vector3 v_dir = Vector3.Normalize(Vector3.Cross(axis, r));
        Vector3 v_circular = v_mag * v_dir;

        Maneuver m1;
        m1 = new Maneuver();
        m1.nbody = fromOrbit.nbody;
        m1.mtype = Maneuver.Mtype.vector;
        m1.velChange = v_circular - vel_ship;
        m1.dV = Vector3.Magnitude(m1.velChange);
        Debug.Log(string.Format("v_ship={0} v_circular={1} axis={2}", vel_ship, v_circular, axis));
        m1.worldTime = GravityEngine.Instance().GetPhysicalTime();
        maneuvers.Add(m1);
    }

    public override string ToString()
    {
        return name;
    }
}
