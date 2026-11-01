#include "helicopter.hpp"

#include <cmath>

namespace simcore {

Wrench Helicopter::compute_wrench(const RigidBodyState& s, const Controls& c,
                                  const Environment& env) const {
    Wrench w;
    const double rho = env.atmosphere.density;
    const double area = kPi * p_.rotor_radius * p_.rotor_radius;
    const double tip_speed = p_.rotor_speed * p_.rotor_radius;

    // Main rotor thrust, tilted by cyclic (aft stick tilts the disc aft).
    const double thrust = p_.ct_max * c.throttle * rho * area * tip_speed * tip_speed;
    const double tilt_lon = c.pitch * p_.max_disc_tilt;
    const double tilt_lat = c.roll * p_.max_disc_tilt;
    const Vec3 rotor_force{-thrust * std::sin(tilt_lon), thrust * std::sin(tilt_lat),
                           -thrust * std::cos(tilt_lon) * std::cos(tilt_lat)};
    const Vec3 hub{0.0, 0.0, -p_.hub_height};
    w.force += rotor_force;
    w.moment += cross(hub, rotor_force);
    w.moment += Vec3{p_.hub_stiffness * tilt_lat, p_.hub_stiffness * tilt_lon, 0.0};

    // Main rotor torque; the rotor turns counter-clockwise seen from above,
    // so the reaction yaws the fuselage nose right.
    const double induced_velocity = thrust > 0.0 ? std::sqrt(thrust / (2 * rho * area)) : 0.0;
    const double power = thrust * induced_velocity / p_.figure_of_merit + p_.profile_power;
    const double torque = power / p_.rotor_speed;
    w.moment.z += torque;

    // Tail rotor side force at (-tail_arm, 0, 0).
    const double tail_force = torque / p_.tail_arm - c.yaw * p_.tail_pedal_authority;
    const Vec3 tail_pos{-p_.tail_arm, 0.0, 0.0};
    const Vec3 tail_vec{0.0, tail_force, 0.0};
    w.force += tail_vec;
    w.moment += cross(tail_pos, tail_vec);

    // Fuselage parasite drag.
    const AirData ad = compute_air_data(s, env);
    const Vec3& va = ad.vel_air_body;
    const Vec3& f = p_.fuselage_drag_area;
    w.force += Vec3{-0.5 * rho * f.x * std::abs(va.x) * va.x,
                    -0.5 * rho * f.y * std::abs(va.y) * va.y,
                    -0.5 * rho * f.z * std::abs(va.z) * va.z};

    // Rotor/fuselage angular damping.
    const Vec3& om = s.omega_body;
    const Vec3& d = p_.rotor_damping;
    w.moment -= Vec3{d.x * om.x, d.y * om.y, d.z * om.z};
    return w;
}

}  // namespace simcore
