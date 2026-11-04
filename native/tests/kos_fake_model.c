/*
 * Test double for model DLLs built with the kos_simulation Simulink target
 * (dll_malloc.tlc), e.g. AK13Dynamics.dll and DynamicsGorEl.dll. Exports the
 * same eight functions and the same port descriptor struct, so the managed
 * KosModel loader can be tested on any platform without MATLAB.
 *
 * Model: point mass, v' = thrust / mass, x' = v, fixed step 0.01 s.
 */
#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#  define KOS_EXPORT __declspec(dllexport)
#else
#  define KOS_EXPORT __attribute__((visibility("default")))
#endif

/* Mirrors trs_port_description.h; VARTYPE is an unsigned short (VT_R8 = 5). */
struct description {
    const char* name;
    int isParameter;
    int isFromBus;
    unsigned short type;
    int width;
    void* address;
};

typedef struct {
    double thrust, mass, gains[3];          /* inputs  */
    double velocity, position, sum_gains;   /* outputs */
    double time, step;
} fake_model;

KOS_EXPORT void* mdlCreate(void) {
    fake_model* m = (fake_model*)calloc(1, sizeof(fake_model));
    if (m) m->step = 0.01;
    return m;
}

KOS_EXPORT void mdlStart(void* h) {
    fake_model* m = (fake_model*)h;
    m->velocity = m->position = m->time = 0.0;
}

KOS_EXPORT void mdlStep(void* h) {
    fake_model* m = (fake_model*)h;
    const double mass = m->mass > 0.0 ? m->mass : 1.0;
    m->velocity += m->thrust / mass * m->step;
    m->position += m->velocity * m->step;
    m->sum_gains = m->gains[0] + m->gains[1] + m->gains[2];
    m->time += m->step;
}

KOS_EXPORT void mdlDestroy(void* h) { free(h); }

KOS_EXPORT double getTimeStepSize(void* h) { return ((fake_model*)h)->step; }
KOS_EXPORT double getCurrentTime(void* h) { return ((fake_model*)h)->time; }

KOS_EXPORT int descrCounter(void) { return 6; }

KOS_EXPORT struct description descrByIndex(void* h, int index) {
    fake_model* m = (fake_model*)h;
    struct description d = {NULL, -1, -1, 0, -1, NULL};
    switch (index) {
        case 0: d.name = "thrust";    d.isFromBus = 1; d.width = 1; d.address = &m->thrust; break;
        case 1: d.name = "mass";      d.isFromBus = 1; d.width = 1; d.address = &m->mass; break;
        case 2: d.name = "gains";     d.isFromBus = 1; d.width = 3; d.address = m->gains; break;
        case 3: d.name = "velocity";  d.isFromBus = 0; d.width = 1; d.address = &m->velocity; break;
        case 4: d.name = "position";  d.isFromBus = 0; d.width = 1; d.address = &m->position; break;
        case 5: d.name = "sum_gains"; d.isFromBus = 0; d.width = 1; d.address = &m->sum_gains; break;
        default: return d;
    }
    d.isParameter = 0;
    d.type = 5; /* VT_R8 */
    return d;
}
