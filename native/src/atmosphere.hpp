#pragma once

namespace simcore {

struct AtmosphereState {
    double temperature = 0.0;     // K
    double pressure = 0.0;        // Pa
    double density = 0.0;         // kg/m^3
    double speed_of_sound = 0.0;  // m/s
};

inline constexpr double kSeaLevelDensity = 1.225;  // kg/m^3
inline constexpr double kStandardGravity = 9.80665;  // m/s^2

// International Standard Atmosphere (troposphere + lower stratosphere).
// Geometric altitude in metres; clamped to [-1000, 20000].
AtmosphereState isa(double altitude_m);

}  // namespace simcore
