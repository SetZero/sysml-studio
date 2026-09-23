/*
 * A small model that exercises every shape the indexer recognises.
 */
package SampleLifecycle {
    metadata def implemented;
    metadata def planned;
}

package Sample {
    private import ScalarValues::*;
    private import SampleLifecycle::*;

    part def <'P.1'> Engine {
        doc /* Turns fuel into torque. */
        attribute power : Real;
        port shaft : ShaftPort;
    }

    port def ShaftPort;

    #implemented
    part def Vehicle {
        part engine : Engine;
        part wheels : Wheel[4];
        connect engine.shaft to wheels;
    }

    #planned
    part def Wheel :> RollingThing;

    part def RollingThing;

    requirement def <'R.1'> MustStart {
        doc /* The vehicle starts within two seconds. */
    }

    satisfy MustStart by Vehicle::engine;

    action def StartUp {
        action crank;
        action idle;
        first crank then idle;
    }

    state def Running {
        entry; then off;
        state off;
        transition off_to_on
            first off
            then on;
        state on;
    }
}

package Broken {
    private import Sample::*;
    part def UsesMissing {
        part x : Sample::NoSuchThing;
    }
}
