#include "atmosphere.hpp"

#include <algorithm>
#include <cmath>

namespace simcore {

namespace {
constexpr double kGasConstant = 287.05287;  // J/(kg K), dry air
constexpr double kGamma = 1.4;
constexpr double kT0 = 288.15;
constexpr double kP0 = 101325.0;
constexpr double kLapseRate = 0.0065;  // K/m
constexpr double kTropopause = 11000.0;
}  // namespace

AtmosphereState isa(double altitude_m) {
    const double h = std::clamp(altitude_m, -1000.0, 20000.0);
    AtmosphereState s;
    if (h <= kTropopause) {
        s.temperature = kT0 - kLapseRate * h;
        s.pressure = kP0 * std::pow(s.temperature / kT0,
                                    kStandardGravity / (kGasConstant * kLapseRate));
    } else {
        const double t11 = kT0 - kLapseRate * kTropopause;
        const double p11 = kP0 * std::pow(t11 / kT0,
                                          kStandardGravity / (kGasConstant * kLapseRate));
        s.temperature = t11;
        s.pressure = p11 * std::exp(-kStandardGravity / (kGasConstant * t11) * (h - kTropopause));
    }
    s.density = s.pressure / (kGasConstant * s.temperature);
    s.speed_of_sound = std::sqrt(kGamma * kGasConstant * s.temperature);
    return s;
}

}  // namespace simcore
