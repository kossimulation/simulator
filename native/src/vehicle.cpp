#include "vehicle.hpp"

#include <algorithm>
#include <cmath>

namespace simcore {

Controls clamp_controls(const Controls& c) {
    return {std::clamp(c.throttle, 0.0, 1.0), std::clamp(c.pitch, -1.0, 1.0),
            std::clamp(c.roll, -1.0, 1.0), std::clamp(c.yaw, -1.0, 1.0)};
}

AirData compute_air_data(const RigidBodyState& s, const Environment& env) {
    AirData a;
    const Vec3 wind_body = to_dcm(s.att).transposed() * env.wind_ned;
    a.vel_air_body = s.vel_body - wind_body;
    a.airspeed = norm(a.vel_air_body);
    if (a.airspeed > 1e-6) {
        a.alpha = std::atan2(a.vel_air_body.z, a.vel_air_body.x);
        a.beta = std::asin(std::clamp(a.vel_air_body.y / a.airspeed, -1.0, 1.0));
    }
    a.qbar = 0.5 * env.atmosphere.density * a.airspeed * a.airspeed;
    return a;
}

}  // namespace simcore
