using UnityEngine;

namespace Backpacking.World
{
    /// <summary>
    /// Passes the weather's wind to the trees and plants (their "Wind Lit" shader bends and flutters them; see
    /// Shaders/Wind.hlsl), and to the terrain's grass, which waves harder and faster as the wind gets up.
    /// </summary>
    public class TreeWind : MonoBehaviour
    {
        [SerializeField] WeatherSystem weather;
        [Tooltip("Wind speed (km/h) at which trees are bending their hardest.")]
        [SerializeField] float stormKmh = 45f;
        [Tooltip("How quickly the trees follow changes in the wind, per second.")]
        [SerializeField] float response = 1.5f;

        static readonly int WindId = Shader.PropertyToID("_BackpackingWind");

        TerrainData terrainData;
        float baseWaveSpeed, baseWaveStrength, baseWaveAmount;
        float strength;
        Vector3 direction = Vector3.right;

        void Start()
        {
            Terrain terrain = Terrain.activeTerrain;
            if (terrain == null)
                return;
            terrainData = terrain.terrainData;
            baseWaveSpeed = terrainData.wavingGrassSpeed;
            baseWaveStrength = terrainData.wavingGrassStrength;
            baseWaveAmount = terrainData.wavingGrassAmount;
        }

        void Update()
        {
            float target = weather != null ? Mathf.Clamp01(weather.WindKmh / stormKmh) : 0.1f;
            strength = Mathf.MoveTowards(strength, target, response * Time.deltaTime);
            if (weather != null)
                direction = Vector3.Slerp(direction, weather.WindDirection, Time.deltaTime * 0.2f).normalized;
            Shader.SetGlobalVector(WindId, new Vector4(direction.x, 0f, direction.z, strength));

            if (terrainData != null)
            {
                // Grass is easier to stir than trees: it's already moving in a breeze.
                terrainData.wavingGrassSpeed = baseWaveSpeed * Mathf.Lerp(0.6f, 2.2f, strength);
                terrainData.wavingGrassStrength = baseWaveStrength * Mathf.Lerp(0.5f, 2f, strength);
                terrainData.wavingGrassAmount = Mathf.Clamp01(baseWaveAmount * Mathf.Lerp(0.6f, 1.8f, strength));
            }
        }

        void OnDisable()
        {
            Shader.SetGlobalVector(WindId, Vector4.zero);
            // The terrain data is an asset: put its grass back as it was.
            if (terrainData != null)
            {
                terrainData.wavingGrassSpeed = baseWaveSpeed;
                terrainData.wavingGrassStrength = baseWaveStrength;
                terrainData.wavingGrassAmount = baseWaveAmount;
            }
        }
    }
}
