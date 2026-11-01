#include "fixed_wing.hpp"

#include <cmath>

namespace simcore {

Wrench FixedWing::compute_wrench(const RigidBodyState& s, const Controls& c,
                                 const Environment& env) const {
    Wrench w;

    const double rho_ratio = env.atmosphere.density / kSeaLevelDensity;
    w.force.x += c.throttle * p_.max_thrust * rho_ratio;

    const AirData ad = compute_air_data(s, env);
    if (ad.airspeed < 0.5) return w;

    // Standard surface sign conventions: positive elevator/aileron/rudder is
    // trailing edge down / right aileron down / trailing edge left, so pilot
    // inputs map with a minus sign.
    const double de = -c.pitch * p_.elevator_max;
    const double da = -c.roll * p_.aileron_max;
    const double dr = -c.yaw * p_.rudder_max;

    const double V = ad.airspeed;
    const double ph = s.omega_body.x * p_.wing_span / (2 * V);
    const double qh = s.omega_body.y * p_.mean_chord / (2 * V);
    const double rh = s.omega_body.z * p_.wing_span / (2 * V);
    const double a = ad.alpha;
    const double b = ad.beta;

    const double CL = p_.CL0 + p_.CL_alpha * a + p_.CL_q * qh + p_.CL_de * de;
    const double CD = p_.CD0 + p_.CD_k * CL * CL;
    const double CY = p_.CY_beta * b + p_.CY_dr * dr;
    const double Cl = p_.Cl_beta * b + p_.Cl_p * ph + p_.Cl_r * rh + p_.Cl_da * da + p_.Cl_dr * dr;
    const double Cm = p_.Cm0 + p_.Cm_alpha * a + p_.Cm_q * qh + p_.Cm_de * de;
    const double Cn = p_.Cn_beta * b + p_.Cn_p * ph + p_.Cn_r * rh + p_.Cn_da * da + p_.Cn_dr * dr;

    const double qS = ad.qbar * p_.wing_area;
    const double lift = qS * CL;
    const double drag = qS * CD;

    w.force.x += lift * std::sin(a) - drag * std::cos(a);
    w.force.y += qS * CY;
    w.force.z += -lift * std::cos(a) - drag * std::sin(a);

    w.moment.x += qS * p_.wing_span * Cl;
    w.moment.y += qS * p_.mean_chord * Cm;
    w.moment.z += qS * p_.wing_span * Cn;
    return w;
}

}  // namespace simcore
