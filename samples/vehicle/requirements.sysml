/*
 * What the vehicle has to do, and what satisfies it.
 */
package VehicleRequirements {
    private import VehicleLifecycle::*;
    private import VehicleStructure::*;

    requirement def <'R.1'> MustStart {
        doc /* The vehicle starts within two seconds. */
    }

    #planned
    requirement def <'R.2'> Range {
        doc /* A full tank lasts 600 km. */
    }

    satisfy MustStart by Vehicle::engine;
    satisfy Range by Vehicle::tank;
}
