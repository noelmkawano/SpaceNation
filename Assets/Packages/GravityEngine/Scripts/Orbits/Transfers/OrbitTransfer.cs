using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Orbit transfer.
/// Base class for all orbit transfers
/// </summary>
public class OrbitTransfer  {

	//! Name of the transfer (will be over-riden by implementing class
	protected string name = "base (error)";

	//! Maneuvers required to execute the transfer
	protected List<Maneuver> maneuvers;

	//! total cost of the manuevers
	protected float deltaV;
	protected float deltaT;

	public OrbitTransfer(OrbitData fromOrbit, OrbitData toOrbit) {
		maneuvers = new List<Maneuver>();
	}

    public OrbitTransfer(OrbitData fromOrbit)
    {
        maneuvers = new List<Maneuver>();
    }

    public float GetDeltaV() {
		return deltaV;
	}

	public float GetDeltaT() {
		return deltaT;
	}

	public List<Maneuver> GetManeuvers() {
		return maneuvers;
	}

	public override string ToString() {
	
		return "forgot to override";
	}

}
