// Wind for trees and shrubs, used by the generated "Backpacking/Wind Lit" shader (a copy of URP Lit whose vertex
// functions run each vertex through WindDisplace first).
//
// The whole plant leans and sways with the wind, more the higher up it is (a trunk barely moves at its foot),
// and with _WIND_FLUTTER (leaves and needles) each bit also flutters on its own. Each tree gets its own phase from
// where it stands, so a forest doesn't move in step. TreeWind sets the globals from the weather.
#ifndef BACKPACKING_WIND_INCLUDED
#define BACKPACKING_WIND_INCLUDED

// xyz: direction the wind blows towards (flat, unit length). w: strength, 0 calm to 1 storm.
float4 _BackpackingWind;

float3 WindDisplace(float3 positionOS)
{
    float strength = _BackpackingWind.w;
    if (strength <= 0.001)
        return positionOS;

    float3 origin = TransformObjectToWorld(float3(0, 0, 0));
    float3 world = TransformObjectToWorld(positionOS);
    float height = max(world.y - origin.y, 0.0);
    float3 direction = _BackpackingWind.xyz;
    float t = _Time.y;
    float phase = dot(origin.xz, float2(0.071, 0.053));

    // Gusts sweep across the land in the wind's direction, so neighbouring trees bow one after another.
    float along = dot(origin.xz, direction.xz) * 0.04;
    float gust = 0.55 + 0.45 * sin(t * 0.55 - along + phase * 0.3) * sin(t * 0.23 - along * 0.6 + phase);
    // A steady lean, and a sway back and forth about it.
    float sway = gust + sin(t * (1.1 + 0.25 * sin(phase)) + phase) * 0.3 * (0.4 + strength);
    float bend = height * height * 0.0025 * strength * sway;
    world += direction * bend;
    // Bending over, the top comes down a little rather than stretching.
    world.y -= bend * bend / max(height, 1.0) * 0.5;
    // A little side-to-side as well.
    float3 side = float3(-direction.z, 0, direction.x);
    world += side * sin(t * 1.7 + phase * 2.0) * height * height * 0.0004 * strength;

#ifdef _WIND_FLUTTER
    // Leaves and twigs flutter, more out at the tips.
    float flutter = sin(t * (7.0 + 3.0 * strength) + dot(world, float3(1.9, 2.7, 1.3)))
                  * cos(t * 5.3 + dot(world, float3(-2.3, 1.1, 1.7)));
    world += (direction * 0.6 + float3(0, 0.8, 0)) * flutter * 0.035 * (0.3 + strength) * saturate(height * 0.25);
#endif

    return TransformWorldToObject(world);
}

#endif
