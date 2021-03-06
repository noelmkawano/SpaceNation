﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Calculate the transfer options from one body to a target body given
/// the gravitational field of a central mass. 
/// 
/// The options can be retrieved as a list of string or of OrbitTransfer
/// objects. (Some orbit transfers have tunable parameters, e.g. bi-elliptic
/// xfer)
/// </summary>
public class TransferCalc  {

	private NBody ship; 
	private NBody target; 
	private NBody centralMass; 

	// Limits to decide when orbits are effectivly co-planar or circular
	public const float DELTA_INCL = 0.01f;
	private const float DELTA_ECC = 0.01f;


	public TransferCalc(NBody ship, NBody target, NBody centralMass) {
		this.ship = ship;
		this.target = target;
		this.centralMass = centralMass;
	}

	public List<OrbitTransfer> FindTransfers() {
		// find orbit parameters for each body. 
		OrbitData shipOrbit = new OrbitData();
		shipOrbit.SetOrbit(ship, centralMass);
		OrbitData targetOrbit = new OrbitData();
		targetOrbit.SetOrbit(target, centralMass);
        //Debug.Log("ship:" + shipOrbit.LogString());
        //Debug.Log("target:" + targetOrbit.LogString());
        List<OrbitTransfer> transfers = new List<OrbitTransfer>();

        if ((shipOrbit.ecc < 1f) && (targetOrbit.ecc < 1f)) {
            // both ellipses
            if (shipOrbit.ecc >= DELTA_ECC)
            {
                // add option to circularize our orbit (independent of target)
                // TODO: need more general ellipse tuning
                transfers.Add(new CircularizeXfer(shipOrbit));
            } 

			if ((shipOrbit.ecc < DELTA_ECC) && (targetOrbit.ecc < DELTA_ECC)) {
                // both circular - can use Hohmann
                if (Mathf.Abs(shipOrbit.inclination - targetOrbit.inclination) > DELTA_INCL)
                {
                    // change in inclination
                    // DEBUG hack
                    // transfers.Add(new CircularPlaneChange(shipOrbit, targetOrbit));
                }
                else { 
                    transfers.Add(new HohmannXfer(shipOrbit, targetOrbit));
                    if (BiellipticXfer.HasLowerDv(shipOrbit, targetOrbit))
                    {
                        // need to pick (or let user select) the xfer radius - could show a curve?
                        // Test code - just pick 1.5x dest as xfer radius
                        // TODO: Make transfer radius selectable
                        transfers.Add(new BiellipticXfer(shipOrbit, targetOrbit, targetOrbit.a * 1.5f));
                    }
                }
			} else {
				Debug.Log("TODO: Transfer between co-planar ellipses.");
			}
		} else {
			Debug.Log("Not transfer between ellipses.");
		}
        return transfers;
	}


}
