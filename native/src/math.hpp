#pragma once

#include <algorithm>
#include <cmath>

namespace simcore {

inline constexpr double kPi = 3.14159265358979323846;

struct Vec3 {
    double x = 0.0;
    double y = 0.0;
    double z = 0.0;

    constexpr Vec3() = default;
    constexpr Vec3(double x_, double y_, double z_) : x(x_), y(y_), z(z_) {}

    Vec3& operator+=(const Vec3& o) { x += o.x; y += o.y; z += o.z; return *this; }
    Vec3& operator-=(const Vec3& o) { x -= o.x; y -= o.y; z -= o.z; return *this; }
};

inline Vec3 operator+(Vec3 a, const Vec3& b) { return a += b; }
inline Vec3 operator-(Vec3 a, const Vec3& b) { return a -= b; }
inline Vec3 operator-(const Vec3& a) { return {-a.x, -a.y, -a.z}; }
inline Vec3 operator*(const Vec3& a, double s) { return {a.x * s, a.y * s, a.z * s}; }
inline Vec3 operator*(double s, const Vec3& a) { return a * s; }
inline Vec3 operator/(const Vec3& a, double s) { return {a.x / s, a.y / s, a.z / s}; }

inline double dot(const Vec3& a, const Vec3& b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
inline Vec3 cross(const Vec3& a, const Vec3& b) {
    return {a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x};
}
inline double norm(const Vec3& a) { return std::sqrt(dot(a, a)); }
inline bool is_finite(const Vec3& a) {
    return std::isfinite(a.x) && std::isfinite(a.y) && std::isfinite(a.z);
}

// Unit quaternion (w, x, y, z) rotating body-frame vectors into the NED frame.
struct Quat {
    double w = 1.0;
    double x = 0.0;
    double y = 0.0;
    double z = 0.0;
};

inline Quat operator*(const Quat& a, const Quat& b) {
    return {a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z,
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w};
}

inline Quat normalized(const Quat& q) {
    const double n = std::sqrt(q.w * q.w + q.x * q.x + q.y * q.y + q.z * q.z);
    return {q.w / n, q.x / n, q.y / n, q.z / n};
}

inline bool is_finite(const Quat& q) {
    return std::isfinite(q.w) && std::isfinite(q.x) && std::isfinite(q.y) && std::isfinite(q.z);
}

struct Mat3 {
    double m[3][3] = {{1, 0, 0}, {0, 1, 0}, {0, 0, 1}};

    Vec3 operator*(const Vec3& v) const {
        return {m[0][0] * v.x + m[0][1] * v.y + m[0][2] * v.z,
                m[1][0] * v.x + m[1][1] * v.y + m[1][2] * v.z,
                m[2][0] * v.x + m[2][1] * v.y + m[2][2] * v.z};
    }

    Mat3 transposed() const {
        Mat3 t;
        for (int i = 0; i < 3; ++i)
            for (int j = 0; j < 3; ++j) t.m[i][j] = m[j][i];
        return t;
    }
};

// Direction cosine matrix body -> NED.
inline Mat3 to_dcm(const Quat& q) {
    const double w = q.w, x = q.x, y = q.y, z = q.z;
    Mat3 r;
    r.m[0][0] = 1 - 2 * (y * y + z * z);
    r.m[0][1] = 2 * (x * y - w * z);
    r.m[0][2] = 2 * (x * z + w * y);
    r.m[1][0] = 2 * (x * y + w * z);
    r.m[1][1] = 1 - 2 * (x * x + z * z);
    r.m[1][2] = 2 * (y * z - w * x);
    r.m[2][0] = 2 * (x * z - w * y);
    r.m[2][1] = 2 * (y * z + w * x);
    r.m[2][2] = 1 - 2 * (x * x + y * y);
    return r;
}

// Aerospace Z-Y-X (yaw, pitch, roll) Euler angles, radians.
struct Euler {
    double roll = 0.0;
    double pitch = 0.0;
    double yaw = 0.0;
};

inline Quat from_euler(const Euler& e) {
    const double cr = std::cos(e.roll / 2), sr = std::sin(e.roll / 2);
    const double cp = std::cos(e.pitch / 2), sp = std::sin(e.pitch / 2);
    const double cy = std::cos(e.yaw / 2), sy = std::sin(e.yaw / 2);
    return {cr * cp * cy + sr * sp * sy,
            sr * cp * cy - cr * sp * sy,
            cr * sp * cy + sr * cp * sy,
            cr * cp * sy - sr * sp * cy};
}

inline Euler to_euler(const Quat& q) {
    Euler e;
    e.roll = std::atan2(2 * (q.w * q.x + q.y * q.z), 1 - 2 * (q.x * q.x + q.y * q.y));
    e.pitch = std::asin(std::clamp(2 * (q.w * q.y - q.z * q.x), -1.0, 1.0));
    e.yaw = std::atan2(2 * (q.w * q.z + q.x * q.y), 1 - 2 * (q.y * q.y + q.z * q.z));
    return e;
}

}  // namespace simcore
