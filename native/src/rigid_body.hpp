#pragma once

#include "math.hpp"

namespace simcore {

// Frames: NED world frame (x north, y east, z down), FRD body frame
// (x forward, y right, z down). Inertia is taken as principal (diagonal).
struct MassProperties {
    double mass = 1.0;             // kg
    Vec3 inertia{1.0, 1.0, 1.0};   // Ixx, Iyy, Izz, kg m^2
};

struct RigidBodyState {
    Vec3 pos_ned;     // m
    Vec3 vel_body;    // m/s (u, v, w)
    Quat att;         // body -> NED
    Vec3 omega_body;  // rad/s (p, q, r)
};

// Force and moment about the centre of gravity, body frame.
struct Wrench {
    Vec3 force;
    Vec3 moment;

    Wrench& operator+=(const Wrench& o) {
        force += o.force;
        moment += o.moment;
        return *this;
    }
};

struct StateDerivative {
    Vec3 pos_dot;
    Vec3 vel_dot;
    Quat att_dot{0.0, 0.0, 0.0, 0.0};
    Vec3 omega_dot;
};

// Newton-Euler equations of motion for a rigid body.
StateDerivative compute_derivative(const RigidBodyState& s, const MassProperties& mp,
                                   const Wrench& w);

// Returns s + h * d (attitude is not renormalised here).
RigidBodyState advance(const RigidBodyState& s, const StateDerivative& d, double h);

bool is_finite(const RigidBodyState& s);

}  // namespace simcore
