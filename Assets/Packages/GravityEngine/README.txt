Gravity Engine 1.5
August 23, 2017
====================

Trajectory prediction: N-body look-ahead simulation to show future paths of objects.

Bug fixes/minor enhancements:

1) OrbitPrediction for systems that use non-default units (e.g. ORBITAL
    or SOLAR) has been corrected.
2) Kepler objects now correctly handle a timeZoom change. They have
   been added to the AddDeleteTester demo scene.
3) Kepler objects can now be added via script.
4) A method to get double precision speed and velocity for an object 
   has been added: GravityEngine.GetPositionVelocityScaled(). This is 
   demonstrated in the Units-EarthOrbit tutorial via the 
   LogPositionVelocity script (pressing L will log position and
   velocity to the console)
5) OrbitData parameters have been made public in the OrbitPredictor 
   to allow their access via scripts.

On-line documentation/tutorial videos:
http://nbodyphysics.com/blog/gravity-engine-doc-1-3-2/

Docs for script elements:
http://nbodyphysics.com/gravityengine/html/

Support: nbodyphysics@gmail.com

