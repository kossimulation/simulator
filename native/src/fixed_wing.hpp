#pragma once

#include "vehicle.hpp"

namespace simcore {

// Light single-engine aircraft (Cessna 172 class). Linear stability-derivative
// aerodynamics: valid for small angles of attack, no stall model yet.
struct FixedWingParams {
    MassProperties mass{1043.0, {1285.0, 1825.0, 2667.0}};

    double wing_area = 16.2;   // S, m^2
    double wing_span = 10.9;   // b, m
    double mean_chord = 1.49;  // c, m

    double max_thrust = 2500.0;  // N at sea level, scales with density

    // Control surface limits, rad.
    double elevator_max = 0.4363;  // 25 deg
    double aileron_max = 0.2618;   // 15 deg
    double rudder_max = 0.2618;    // 15 deg

    // Lift / drag / side force.
    double CL0 = 0.31, CL_alpha = 5.143, CL_q = 3.9, CL_de = 0.43;
    double CD0 = 0.031, CD_k = 0.054;
    double CY_beta = -0.31, CY_dr = 0.187;
    // Rolling moment.
    double Cl_beta = -0.089, Cl_p = -0.47, Cl_r = 0.096, Cl_da = -0.178, Cl_dr = 0.0147;
    // Pitching moment.
    double Cm0 = -0.015, Cm_alpha = -0.89, Cm_q = -12.4, Cm_de = -1.28;
    // Yawing moment.
    double Cn_beta = 0.065, Cn_p = -0.03, Cn_r = -0.099, Cn_da = -0.053, Cn_dr = -0.0657;

    double gear_height = 1.2;  // m, CG above ground when parked
};

class FixedWing final : public VehicleModel {
public:
    explicit FixedWing(const FixedWingParams& p = {}) : p_(p) {}

    const char* name() const override { return "fixed_wing"; }
    const MassProperties& mass_properties() const override { return p_.mass; }
    Wrench compute_wrench(const RigidBodyState& s, const Controls& c,
                          const Environment& env) const override;
    double ground_contact_offset() const override { return p_.gear_height; }

    const FixedWingParams& params() const { return p_; }

private:
    FixedWingParams p_;
};

}  // namespace simcore
