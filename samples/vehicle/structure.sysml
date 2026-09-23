/*
 * What a vehicle is made of.
 */
package VehicleStructure {
    private import ScalarValues::*;
    private import VehicleLifecycle::*;

    port def ShaftPort;
    port def FuelPort;

    #implemented
    part def <'V.1'> Engine {
        doc /* Turns fuel into torque. */
        attribute power : Real;
        port shaft : ShaftPort;
        port fuel : FuelPort;
    }

    #implemented
    part def FuelTank {
        attribute capacity : Real;
        port outlet : FuelPort;
    }

    part def RollingThing;

    #inProgress
    part def Wheel :> RollingThing {
        attribute diameter : Real;
    }

    #implemented
    part def <'V.0'> Vehicle {
        doc /* The whole car, as the driver sees it. */
        part engine : Engine;
        part tank : FuelTank;
        part wheels : Wheel[4];
        connection fuelLine connect tank.outlet to engine.fuel;
        connection driveShaft connect engine.shaft to wheels;
    }
}
